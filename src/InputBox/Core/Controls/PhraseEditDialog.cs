using InputBox.Core.Configuration;
using InputBox.Core.Extensions;
using InputBox.Core.Feedback;
using InputBox.Core.Input;
using InputBox.Core.Services;
using InputBox.Core.Utilities;
using InputBox.Resources;
using Microsoft.Win32;
using System.Diagnostics;

namespace InputBox.Core.Controls;

// 阻擋設計工具。
partial class DesignerBlocker { };

/// <summary>
/// 片語編輯對話框（新增／編輯單一片語）
/// </summary>
internal sealed partial class PhraseEditDialog : Form
{
    /// <summary>
    /// 片語編輯視窗的基準最小寬度（96 DPI）
    /// </summary>
    private const int BaseDialogMinWidth = 760;

    /// <summary>
    /// 右搖桿快速選取時，用來形成拉鏈感的 burst 視窗。
    /// </summary>
    private const int SelectionBurstWindowMs = 110;

    /// <summary>
    /// 文字接近輸入上限時開始提供物理預警的剩餘字元門檻。
    /// </summary>
    private const int TextLimitWarningThreshold = 10;

    /// <summary>
    /// 文字已碰到上限後，重複撞牆回饋的最小節流時間。
    /// </summary>
    private const int RepeatedBoundaryFeedbackThrottleMs = 180;

    /// <summary>
    /// 片語名稱輸入框
    /// </summary>
    private readonly TextBox _txtName;

    /// <summary>
    /// 片語內容輸入框
    /// </summary>
    private readonly TextBox _txtContent;

    /// <summary>
    /// 確認按鈕
    /// </summary>
    private readonly Button _btnOk;

    /// <summary>
    /// 取消按鈕
    /// </summary>
    private readonly Button _btnCancel;

    /// <summary>
    /// A11y 廣播用的 Label
    /// </summary>
    private readonly AnnouncerLabel _announcer;

    /// <summary>
    /// 用於管理對話框生命週期內非同步任務的取消權杖來源
    /// </summary>
    private CancellationTokenSource? _cts = new();

    /// <summary>
    /// A11y 廣播防抖用的序號
    /// </summary>
    private long _a11yDebounceId;

    /// <summary>
    /// 遊戲控制器（由外部導入，生命週期由外部管理）
    /// </summary>
    private IGamepadController? _gamepadController;

    /// <summary>
    /// 統一放大的 A11y 字型（來自共享快取）
    /// </summary>
    private Font? _a11yFont;

    /// <summary>
    /// A11y Bold 字型（來自共享快取，用於焦點加粗）
    /// </summary>
    private Font? _boldFont;

    /// <summary>
    /// 建構子中直接建立的輸入框字型（未替換為共享快取前暫用）
    /// <para>在 <see cref="OnShown"/> 時替換為共享快取字型並釋放此實例。</para>
    /// </summary>
    private Font? _txtInputFont;

    /// <summary>
    /// 使用者輸入的片語名稱
    /// </summary>
    public string PhraseName => _txtName.Text.Trim();

    /// <summary>
    /// 使用者輸入的片語內容
    /// </summary>
    public string PhraseContent => _txtContent.Text;

    /// <summary>
    /// 右搖桿選取錨點
    /// </summary>
    private int? _rsSelectionAnchor;

    /// <summary>
    /// 記錄最近一次右搖桿選取回饋的時間與 burst 等級。
    /// </summary>
    private DateTime _lastSelectionFeedbackUtc = DateTime.MinValue;
    private int _selectionFeedbackBurstLevel;

    /// <summary>
    /// 追蹤片語名稱與內容目前長度，用於偵測接近字數上限時的物理預警。
    /// </summary>
    private int _lastObservedNameLength;
    private int _lastObservedContentLength;
    private int _lastNameLimitWarningBucket = -1;
    private int _lastContentLimitWarningBucket = -1;
    private DateTime _lastNameLimitWallUtc = DateTime.MinValue;
    private DateTime _lastContentLimitWallUtc = DateTime.MinValue;

    /// <summary>
    /// 動畫警示執行緒安全旗標
    /// </summary>
    private int _isFlashing = 0;

    /// <summary>
    /// 片語名稱字元數提示標籤（{current}/{max}，近上限時顯示橙色）
    /// </summary>
    private readonly Label? _lblNameCount;

    /// <summary>
    /// 片語內容字元數提示標籤（{current}/{max}，近上限時顯示橙色）
    /// </summary>
    private readonly Label? _lblContentCount;

    /// <summary>
    /// 用於中斷動畫的專屬 Token 來源
    /// </summary>
    private CancellationTokenSource? _alertCts;

    /// <summary>
    /// 已套用的 DPI 快取，避免重複計算最小寬度
    /// </summary>
    private float _lastAppliedDpi;

    /// <summary>
    /// 初始化片語編輯對話框
    /// </summary>
    /// <param name="name">初始名稱</param>
    /// <param name="content">初始內容</param>
    /// <param name="a11yFont">A11y 字型（從父對話框傳入）</param>
    public PhraseEditDialog(string name, string content, Font? a11yFont)
    {
        DoubleBuffered = true;
        KeyPreview = true;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        TopMost = true;
        Text = string.IsNullOrEmpty(name) ? Strings.Phrase_Edit_Title_Add : Strings.Phrase_Edit_Title_Edit;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(12);
        AccessibleName = Text;
        AccessibleDescription = Strings.Phrase_A11y_Edit_Dialog_Desc;
        AccessibleRole = AccessibleRole.Dialog;

        Icon = Application.OpenForms.OfType<MainForm>().FirstOrDefault()?.Icon ??
            ActiveForm?.Icon;

        if (a11yFont != null)
        {
            _a11yFont = a11yFont;
            Font = a11yFont;
        }

        _announcer = new AnnouncerLabel()
        {
            AccessibleName = "\u200B",
            Dock = DockStyle.Bottom,
            Height = 1,
            TabStop = false,
            BackColor = Color.Empty,
            ForeColor = Color.Empty,
        };
        Controls.Add(_announcer);

        TableLayoutPanel tlp = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 6,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(0)
        };
        tlp.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        tlp.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        tlp.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        tlp.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        tlp.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        tlp.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        tlp.RowStyles.Add(new RowStyle(SizeType.Absolute, 8));
        tlp.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        // 名稱標籤。
        Label lblName = new()
        {
            Text = Strings.Phrase_Edit_Name,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            BackColor = Color.Empty,
            ForeColor = Color.Empty,
            Margin = new Padding(0, 4, 8, 4)
        };
        tlp.Controls.Add(lblName, 0, 0);

        // 名稱輸入。
        // 使用 _txtInputFont 追蹤此字體實例，以便在 OnShown 中替換為共享快取字體後安全釋放。
        _txtInputFont = new Font(Font.FontFamily, 28f, FontStyle.Regular, GraphicsUnit.Point);
        _txtName = new TextBox
        {
            Text = name,
            Dock = DockStyle.Fill,
            MaxLength = AppSettings.MaxPhraseNameLength,
            BorderStyle = BorderStyle.None,
            BackColor = Color.Empty,
            ForeColor = Color.Empty,
            ImeMode = ImeMode.On,
            Font = _txtInputFont,
            AccessibleName = Strings.Phrase_Edit_Name,
            AccessibleDescription = Strings.Phrase_A11y_Edit_Name_Desc,
            TabIndex = 0,
            Margin = new Padding(0, 4, 0, 4),
            PlaceholderText = GetPhraseTextOrFallback("Phrase_Edit_Name_Placeholder", Strings.Phrase_Edit_Name)
        };
        _txtName.Enter += HandleTextBoxEnter;
        _txtName.Leave += HandleTextBoxLeave;
        _txtName.TextChanged += (_, _) => HandleNameTextChanged();
        _txtName.KeyPress += HandleNameKeyPress;
        tlp.Controls.Add(_txtName, 1, 0);

        // 名稱字數提示標籤（顯示名稱已輸入字元數 / 上限）。
        _lblNameCount = new Label
        {
            AutoSize = false,
            Anchor = AnchorStyles.Left,
            BackColor = Color.Empty,
            ForeColor = Color.Empty,
            TabStop = false,
            AccessibleRole = AccessibleRole.StaticText,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 0, 0, 4)
        };
        HandleNameTextChanged();
        tlp.Controls.Add(_lblNameCount, 1, 1);

        // 內容標籤。
        Label lblContent = new()
        {
            Text = Strings.Phrase_Edit_Content,
            AutoSize = true,
            Anchor = AnchorStyles.Left | AnchorStyles.Top,
            BackColor = Color.Empty,
            ForeColor = Color.Empty,
            Margin = new Padding(0, 4, 8, 4)
        };
        tlp.Controls.Add(lblContent, 0, 2);

        // 內容輸入（多行）；共用同一個私有字體實例。
        _txtContent = new TextBox
        {
            Text = content,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            Height = 140,
            Dock = DockStyle.Fill,
            MaxLength = AppSettings.MaxInputLength,
            BorderStyle = BorderStyle.None,
            BackColor = Color.Empty,
            ForeColor = Color.Empty,
            ImeMode = ImeMode.On,
            Font = _txtInputFont,
            AccessibleName = Strings.Phrase_Edit_Content,
            AccessibleDescription = Strings.Phrase_A11y_Edit_Content_Desc,
            AcceptsReturn = true,
            TabIndex = 1,
            Margin = new Padding(0, 4, 0, 4),
            PlaceholderText = GetPhraseTextOrFallback("Phrase_Edit_Content_Placeholder", Strings.Phrase_Edit_Content)
        };
        _txtContent.Enter += HandleTextBoxEnter;
        _txtContent.Leave += HandleTextBoxLeave;
        _txtContent.KeyPress += HandleContentKeyPress;
        tlp.Controls.Add(_txtContent, 1, 2);

        // 字元數提示標籤（顯示內容已輸入字元數 / 上限）。
        _lblContentCount = new Label
        {
            AutoSize = false,
            Anchor = AnchorStyles.Left,
            BackColor = Color.Empty,
            ForeColor = Color.Empty,
            TabStop = false,
            AccessibleRole = AccessibleRole.StaticText,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 0, 0, 4)
        };
        _txtContent.TextChanged += (_, _) => HandleContentTextChanged();
        HandleContentTextChanged();

        // 按鈕區（Grouping）：與其他對話框一致，提供可導覽的群組語意。
        FlowLayoutPanel flpBtns = new()
        {
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Anchor = AnchorStyles.Right | AnchorStyles.Top,
            WrapContents = false,
            Padding = new Padding(0, 2, 0, 2),
            Margin = new Padding(0, 0, 0, 2),
            AccessibleName = Strings.Phrase_A11y_ButtonArea,
            AccessibleDescription = Strings.Phrase_A11y_ButtonArea_Desc,
            AccessibleRole = AccessibleRole.Grouping
        };

        // profile 用於同步片語編輯對話框中的確認／取消按鈕提示與目前控制器配置。
        GamepadFaceButtonProfile profile = GamepadFaceButtonProfile.GetActiveProfile();

        _btnCancel = new Button()
        {
            Text = profile.FormatCancelButtonText(Strings.Phrase_Btn_Cancel),
            AutoSize = true,
            FlatStyle = FlatStyle.Flat,
            DialogResult = DialogResult.Cancel,
            BackColor = Color.Empty,
            ForeColor = Color.Empty,
            AccessibleName = Strings.Phrase_Btn_Cancel,
            AccessibleDescription = Strings.Phrase_A11y_Btn_Cancel_Desc,
            AccessibleRole = AccessibleRole.PushButton,
            TabIndex = 3,
            Margin = new Padding(8, 2, 0, 2)
        };
        _btnCancel.FlatAppearance.BorderSize = 0;

        _btnOk = new Button()
        {
            Text = profile.FormatConfirmButtonText(Strings.Phrase_Btn_Confirm),
            AutoSize = true,
            FlatStyle = FlatStyle.Flat,
            // 重要：確認按鈕不可直接回傳 OK，必須先經過 HandleConfirm 驗證。
            DialogResult = DialogResult.None,
            BackColor = Color.Empty,
            ForeColor = Color.Empty,
            AccessibleName = Strings.Phrase_Btn_Confirm,
            AccessibleDescription = Strings.Phrase_A11y_Btn_Confirm_Desc,
            AccessibleRole = AccessibleRole.PushButton,
            TabIndex = 2,
            Margin = new Padding(8, 2, 0, 2)
        };
        _btnOk.FlatAppearance.BorderSize = 0;
        _btnOk.Click += (s, e) =>
        {
            try
            {
                HandleConfirm();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[片語編輯] _btnOk.Click 失敗：{ex.Message}");
            }
        };

        flpBtns.Controls.Add(_btnCancel);
        flpBtns.Controls.Add(_btnOk);

        tlp.Controls.Add(_lblContentCount, 1, 3);
        tlp.Controls.Add(flpBtns, 1, 5);

        AcceptButton = _btnOk;
        CancelButton = _btnCancel;

        Controls.Add(tlp);

        UpdateCountLabelMinimumWidths();

        // 啟用／停用時控制器暫停／恢復。
        Activated += (s, e) =>
        {
            Task.Run(async () =>
            {
                try
                {
                    CancellationToken token = _cts?.Token ?? CancellationToken.None;

                    token.ThrowIfCancellationRequested();

                    await Task.Delay(50, token);

                    await this.SafeInvokeAsync(() =>
                    {
                        try
                        {
                            GamepadController?.Resume();
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[片語編輯] 控制器繼續失敗：{ex.Message}");
                        }
                    });
                }
                catch (OperationCanceledException)
                {

                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[片語編輯] 已啟動失敗：{ex.Message}");
                }
            },
            _cts?.Token ?? CancellationToken.None)
            .SafeFireAndForget();
        };

        Deactivate += (s, e) =>
        {
            try
            {
                this.SafeBeginInvoke(() =>
                {
                    try
                    {
                        GamepadController?.Pause();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[片語編輯] 控制器暂停失敗：{ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[片語編輯] 失焦失敗：{ex.Message}");
            }
        };
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);

        // 從共享快取取得 Bold 字型。
        _boldFont = MainForm.GetSharedA11yFont(DeviceDpi, FontStyle.Bold);

        _btnOk.AttachEyeTrackerFeedback(
            baseDescription: Strings.Phrase_A11y_Btn_Confirm_Desc,
            regularFont: _a11yFont,
            boldFont: _boldFont,
            formCt: _cts?.Token ?? CancellationToken.None);

        _btnCancel.AttachEyeTrackerFeedback(
            baseDescription: Strings.Phrase_A11y_Btn_Cancel_Desc,
            regularFont: _a11yFont,
            boldFont: _boldFont,
            formCt: _cts?.Token ?? CancellationToken.None);

        // 將建構子中建立的私有字體替換為共享快取字體，防止 GDI Handle 洩漏。
        // 使用 2.0x 倍率取得 28pt 大字型（與主視窗 TBInput 對齊）。
        Font sharedInputFont = MainForm.GetSharedA11yFont(
            DeviceDpi,
            FontStyle.Regular,
            _a11yFont?.FontFamily,
            2.0f);

        _txtName.Font = sharedInputFont;
        _txtContent.Font = sharedInputFont;

        // 釋放建構子中建立的私有字體實例（兩個輸入框共用同一實例）。
        // 使用 AddFontToTrashCan 延遲釋放，避免 GDI 管線仍在使用舊字體時立即 Dispose 引發例外。
        Font? oldInputFont = Interlocked.Exchange(ref _txtInputFont, null);
        if (oldInputFont != null) FontResourceManager.AddFontToTrashCan(oldInputFont);

        UpdateButtonMinimumSizes();
        UpdateMinimumSize();
        ApplySmartPosition();

        _txtName.Focus();
        _txtName.SelectAll();
        ApplyInputBoxStrongVisual(_txtName);
    }

    /// <summary>
    /// 處理命令鍵
    /// </summary>
    /// <param name="msg">訊息參數。</param>
    /// <param name="keyData">按鍵資料。</param>
    /// <returns>是否已處理按鍵。</returns>
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (ActiveControl is TextBox tb)
        {
            // 與主輸入框 TBInput 對齊：
            // Enter：空內容時開啟觸控鍵盤，非空內容時確認。
            if (keyData == Keys.Enter)
            {
                if (string.IsNullOrWhiteSpace(tb.Text))
                {
                    ShowTouchKeyboard(tb);
                }
                else
                {
                    HandleConfirm();
                }

                return true;
            }

            // 與主輸入框 TBInput 對齊：Shift+Enter 代表換行（僅內容欄位生效）。
            if (keyData == (Keys.Enter | Keys.Shift) &&
                ReferenceEquals(tb, _txtContent))
            {
                AnnounceA11y(Strings.A11y_New_Line, interrupt: true);

                FeedbackService.VibrateAsync(
                    _gamepadController,
                    VibrationPatterns.CursorMove,
                    _cts?.Token ?? CancellationToken.None)
                    .SafeFireAndForget();

                return base.ProcessCmdKey(ref msg, keyData);
            }
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    /// <summary>
    /// 關閉對話框時解除事件訂閱並釋放暫用資源
    /// </summary>
    /// <param name="e">表單關閉事件參數。</param>
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);

        if (e.Cancel)
        {
            return;
        }

        try
        {
            SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;

            UnsubscribeGamepadEvents();

            Interlocked.Exchange(ref _cts, null)?.CancelAndDispose();

            // 中止進行中的警示閃爍動畫（若對話框在閃爍期間被關閉，
            // _alertCts 與 _cts 的連結可能在 _cts 歸零後才建立，
            // 導致連結傳播失效；此處直接歸零並取消確保資源被釋放）。
            Interlocked.Exchange(ref _alertCts, null)?.CancelAndDispose();

            // 安全釋放建構子建立的私有字體（如果 OnShown 未執行即關閉對話框時使用）。
            // 使用 AddFontToTrashCan 延遲釋放，避免 GDI 管線仍在使用舊字體時立即 Dispose 引發例外。
            Font? oldInputFont = Interlocked.Exchange(ref _txtInputFont, null);
            if (oldInputFont != null) FontResourceManager.AddFontToTrashCan(oldInputFont);

            // 共享字體僅歸零，由 Program.cs 統一釋放。
            _a11yFont = null;
            _boldFont = null;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[片語編輯] OnFormClosing 失敗：{ex.Message}");
        }
    }

    /// <summary>
    /// 驗證片語名稱與內容，驗證通過時以 OK 關閉對話框
    /// </summary>
    private void HandleConfirm() => this.SafeInvoke(() =>
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_txtName.Text))
            {
                NotifyValidationFailure(_txtName, Strings.Phrase_A11y_NameRequired);

                return;
            }

            if (string.IsNullOrWhiteSpace(_txtContent.Text))
            {
                NotifyValidationFailure(
                    _txtContent,
                    GetPhraseTextOrFallback("Phrase_A11y_ContentRequired", "請輸入片語內容。"));

                return;
            }

            DialogResult = DialogResult.OK;

            Close();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[片語編輯] 確認失敗：{ex.Message}");
        }
    });

    /// <summary>
    /// 以取消結果關閉對話框
    /// </summary>
    private void HandleCancel() => this.SafeInvoke(() =>
    {
        try
        {
            DialogResult = DialogResult.Cancel;

            Close();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[片語編輯] 取消失敗：{ex.Message}");
        }
    });

    /// <summary>
    /// 片語名稱欄文字變更時，更新字數顯示並提供接近上限的回饋。
    /// </summary>
    private void HandleNameTextChanged()
    {
        UpdateNameCharCount();
        HandleTextLimitFeedbackFromLengthChange(
            _txtName,
            AppSettings.MaxPhraseNameLength,
            ref _lastObservedNameLength,
            ref _lastNameLimitWarningBucket);
    }

    /// <summary>
    /// 片語內容欄文字變更時，更新字數顯示並提供接近上限的回饋。
    /// </summary>
    private void HandleContentTextChanged()
    {
        UpdateContentCharCount();
        HandleTextLimitFeedbackFromLengthChange(
            _txtContent,
            AppSettings.MaxInputLength,
            ref _lastObservedContentLength,
            ref _lastContentLimitWarningBucket);
    }

    /// <summary>
    /// 片語名稱欄在已達上限時仍嘗試輸入一般字元，播放硬牆回饋。
    /// </summary>
    /// <param name="sender">事件來源。</param>
    /// <param name="e">按鍵事件引數。</param>
    private void HandleNameKeyPress(object? sender, KeyPressEventArgs e)
        => HandleTextLimitKeyPress(_txtName, AppSettings.MaxPhraseNameLength, ref _lastNameLimitWallUtc, e);

    /// <summary>
    /// 片語內容欄在已達上限時仍嘗試輸入一般字元，播放硬牆回饋。
    /// </summary>
    /// <param name="sender">事件來源。</param>
    /// <param name="e">按鍵事件引數。</param>
    private void HandleContentKeyPress(object? sender, KeyPressEventArgs e)
        => HandleTextLimitKeyPress(_txtContent, AppSettings.MaxInputLength, ref _lastContentLimitWallUtc, e);

    /// <summary>
    /// 從資源檔取得片語文字，若失敗則回傳後備文字
    /// </summary>
    /// <param name="key">資源鍵值。</param>
    /// <param name="fallback">找不到資源時使用的後備文字。</param>
    /// <returns>資源文字或後備文字。</returns>
    private static string GetPhraseTextOrFallback(string key, string fallback)
    {
        try
        {
            return Strings.ResourceManager.GetString(key, Strings.Culture) ?? fallback;
        }
        catch
        {
            return fallback;
        }
    }
}
