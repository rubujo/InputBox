using InputBox.Core.Configuration;
using InputBox.Core.Extensions;
using InputBox.Core.Feedback;
using InputBox.Core.Input;
using InputBox.Core.Interop;
using InputBox.Core.Services;
using InputBox.Core.Utilities;
using InputBox.Resources;
using Microsoft.Win32;
using System.ComponentModel;
using System.Diagnostics;
using System.Media;
using System.Runtime.CompilerServices;

namespace InputBox.Core.Controls;

// 阻擋設計工具。
partial class DesignerBlocker { };

/// <summary>
/// 片語編輯對話框（新增／編輯單一片語）
/// </summary>
internal sealed class PhraseEditDialog : Form
{
    /// <summary>
    /// 片語編輯視窗的基準最小寬度（96 DPI）
    /// </summary>
    private const int BaseDialogMinWidth = 760;

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
    /// 遅戲控制器（由外部導入，生命週期由外部管理）
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
    /// 按鈕視覺狀態追蹤
    /// </summary>

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
    /// 遊戲控制器
    /// </summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public IGamepadController? GamepadController
    {
        get => _gamepadController;
        set
        {
            if (ReferenceEquals(_gamepadController, value))
            {
                return;
            }

            UnsubscribeGamepadEvents();

            _gamepadController = value;

            if (_gamepadController != null)
            {
                GamepadFaceButtonProfile profile = GamepadFaceButtonProfile.GetActiveProfile();

                _gamepadController.APressed += profile.ConfirmOnSouth ? HandleGamepadA : HandleBackOrClear;
                _gamepadController.StartPressed += HandleOpenTouchKeyboardFromGamepad;
                _gamepadController.BPressed += profile.ConfirmOnSouth ? HandleBackOrClear : HandleGamepadA;
                _gamepadController.BackPressed += HandleCancel;
                _gamepadController.LeftPressed += HandleLeft;
                _gamepadController.LeftRepeat += HandleLeft;
                _gamepadController.RightPressed += HandleRight;
                _gamepadController.RightRepeat += HandleRight;
                _gamepadController.UpPressed += HandleFieldPrev;
                _gamepadController.DownPressed += HandleFieldNext;
                _gamepadController.XPressed += HandleBackspace;
                _gamepadController.YPressed += HandleOpenContextMenu;
                _gamepadController.RSLeftPressed += HandleRSLeft;
                _gamepadController.RSLeftRepeat += HandleRSLeft;
                _gamepadController.RSRightPressed += HandleRSRight;
                _gamepadController.RSRightRepeat += HandleRSRight;
                _gamepadController.ConnectionChanged += HandleConnectionChanged;
            }
        }
    }

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
        _txtName.TextChanged += (_, _) => UpdateNameCharCount();
        tlp.Controls.Add(_txtName, 1, 0);

        // 名稱字數提示標籤（顯示名稱已輸入字元數 / 上限）。
        _lblNameCount = new Label
        {
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            BackColor = Color.Empty,
            ForeColor = Color.Empty,
            TabStop = false,
            AccessibleRole = AccessibleRole.StaticText,
            Margin = new Padding(0, 0, 0, 4)
        };
        UpdateNameCharCount();
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
        tlp.Controls.Add(_txtContent, 1, 2);

        // 字元數提示標籤（顯示內容已輸入字元數 / 上限）。
        _lblContentCount = new Label
        {
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            BackColor = Color.Empty,
            ForeColor = Color.Empty,
            TabStop = false,
            AccessibleRole = AccessibleRole.StaticText,
            Margin = new Padding(0, 0, 0, 4)
        };
        _txtContent.TextChanged += (s, e) => UpdateContentCharCount();
        UpdateContentCharCount();

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
        var oldInputFont = Interlocked.Exchange(ref _txtInputFont, null);
        if (oldInputFont != null) FontResourceManager.AddFontToTrashCan(oldInputFont);

        UpdateButtonMinimumSizes();
        UpdateMinimumSize();
        ApplySmartPosition();

        _txtName.Focus();
        _txtName.SelectAll();
        ApplyInputBoxStrongVisual(_txtName);
    }

    /// <summary>
    /// 建立控制項 Handle 後套用最小尺寸、系統事件訂閱與初始定位。
    /// </summary>
    /// <param name="e">控制項事件參數。</param>
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);

        UpdateMinimumSize();

        SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
        SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;

        this.SafeBeginInvoke(ApplySmartPosition);
    }

    /// <summary>
    /// DPI 變更時重新量測按鈕尺寸與對話框最小尺寸。
    /// </summary>
    /// <param name="e">DPI 變更事件參數。</param>
    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);

        this.SafeInvoke(() =>
        {
            UpdateButtonMinimumSizes();
            UpdateMinimumSize();
            ApplySmartPosition();
        });
    }

    /// <summary>
    /// 使用者結束調整視窗大小後重新套用智慧定位。
    /// </summary>
    /// <param name="e">事件參數。</param>
    protected override void OnResizeEnd(EventArgs e)
    {
        base.OnResizeEnd(e);

        ApplySmartPosition();
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
            var oldInputFont = Interlocked.Exchange(ref _txtInputFont, null);
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
    /// Handle 銷毀時確保解除靜態系統事件訂閱
    /// </summary>
    /// <param name="e">控制項事件參數。</param>
    protected override void OnHandleDestroyed(EventArgs e)
    {
        try
        {
            SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
        }
        finally
        {
            base.OnHandleDestroyed(e);
        }
    }

    /// <summary>
    /// 系統偏好設定變更時同步更新按鈕尺寸、最小尺寸與焦點視覺
    /// </summary>
    /// <param name="sender">事件來源。</param>
    /// <param name="e">系統偏好設定事件參數。</param>
    private void SystemEvents_UserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        try
        {
            if (e.Category is UserPreferenceCategory.Accessibility or
                UserPreferenceCategory.Color or
                UserPreferenceCategory.General)
            {
                this.SafeInvoke(() =>
                {
                    UpdateButtonMinimumSizes();
                    UpdateMinimumSize(forceRecalculate: true);
                    ApplySmartPosition();

                    _btnOk.Invalidate();
                    _btnCancel.Invalidate();

                    UpdateNameCharCount();
                    UpdateContentCharCount();

                    TextBox? active = GetActiveTextBox();

                    if (active != null)
                    {
                        ApplyInputBoxStrongVisual(active);
                    }
                });
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[片語編輯] SystemEvents_UserPreferenceChanged 失敗：{ex.Message}");
        }
    }

    /// <summary>
    /// A 鍵：按鈕 → PerformClick；輸入框為空時開啟觸控鍵盤；其餘情境走確認驗證
    /// </summary>
    private void HandleGamepadA() => this.SafeInvoke(() =>
    {
        try
        {
            if (ActiveControl is Button btn)
            {
                btn.PerformClick();
                return;
            }

            TextBox? tb = GetActiveTextBox() ?? _txtName;

            if (tb != null && string.IsNullOrWhiteSpace(tb.Text))
            {
                ShowTouchKeyboard(tb);

                return;
            }

            HandleConfirm();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[片語編輯] 控制器 A 鍵失敗：{ex.Message}");
        }
    });

    /// <summary>
    /// Start 鍵：在輸入框焦點時直接開啟觸控式鍵盤（可在已有內容時修改）
    /// </summary>
    private void HandleOpenTouchKeyboardFromGamepad() => this.SafeInvoke(() =>
    {
        try
        {
            TextBox? tb = GetActiveTextBox() ?? _txtName;

            ShowTouchKeyboard(tb);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[片語編輯] 控制器開啟觸控鍵盤失敗：{ex.Message}");
        }
    });

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
    /// B 鍵：若焦點在輸入框且有內容則優先清空；否則執行取消
    /// </summary>
    private void HandleBackOrClear() => this.SafeInvoke(() =>
    {
        try
        {
            TextBox? tb = GetActiveTextBox();

            if (tb != null)
            {
                if (tb.SelectionLength > 0)
                {
                    tb.SelectedText = string.Empty;

                    _rsSelectionAnchor = null;

                    AnnounceA11y(Strings.Msg_InputCleared, interrupt: true);

                    FeedbackService.VibrateAsync(
                        _gamepadController,
                        VibrationPatterns.ClearInput,
                        _cts?.Token ?? CancellationToken.None)
                        .SafeFireAndForget();

                    return;
                }

                if (!string.IsNullOrEmpty(tb.Text))
                {
                    tb.Clear();

                    _rsSelectionAnchor = null;

                    AnnounceA11y(Strings.Msg_InputCleared, interrupt: true);

                    FeedbackService.VibrateAsync(
                        _gamepadController,
                        VibrationPatterns.ClearInput,
                        _cts?.Token ?? CancellationToken.None)
                        .SafeFireAndForget();

                    return;
                }
            }

            HandleCancel();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[片語編輯] HandleBackOrClear 失敗：{ex.Message}");
        }
    });

    /// <summary>
    /// 開啟觸控式鍵盤（比照 MainForm.ShowTouchKeyboard）
    /// </summary>
    private void ShowTouchKeyboard(TextBox tb)
    {
        if (tb.CanFocus && !tb.Focused)
        {
            tb.Focus();
        }

        AnnounceA11y(Strings.A11y_Opening_Keyboard, interrupt: true);

        Task.Run(async () =>
        {
            try
            {
                await Task.Delay(150, _cts?.Token ?? CancellationToken.None);

                if (TouchKeyboardService.IsVisible()) return;

                await this.SafeInvokeAsync(() =>
                {
                    try
                    {
                        bool opened = TouchKeyboardService.TryOpen();

                        if (opened)
                        {
                            FeedbackService.PlaySound(SystemSounds.Asterisk);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[片語編輯] 觸控鍵盤開啟失敗：{ex.Message}");
                    }
                });
            }
            catch (OperationCanceledException)
            {

            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[片語編輯] 開啟觸控鍵盤失敗：{ex.Message}");
            }
        },
        _cts?.Token ?? CancellationToken.None).SafeFireAndForget();
    }

    /// <summary>
    /// 取得目前焦點所在的 TextBox（名稱或內容）
    /// </summary>
    private TextBox? GetActiveTextBox()
    {
        if (_txtName.Focused)
        {
            return _txtName;
        }

        if (_txtContent.Focused)
        {
            return _txtContent;
        }

        return null;
    }

    /// <summary>
    /// 輸入框取得焦點時套用強化焦點視覺。
    /// </summary>
    /// <param name="sender">觸發事件的輸入框。</param>
    /// <param name="eventArgs">事件參數。</param>
    private void HandleTextBoxEnter(object? sender, EventArgs eventArgs)
    {
        try
        {
            if (sender is TextBox textBox)
            {
                ApplyInputBoxStrongVisual(textBox);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[片語編輯] HandleTextBoxEnter 失敗：{ex.Message}");
        }
    }

    /// <summary>
    /// 輸入框失去焦點時還原一般視覺樣式。
    /// </summary>
    /// <param name="sender">觸發事件的輸入框。</param>
    /// <param name="eventArgs">事件參數。</param>
    private void HandleTextBoxLeave(object? sender, EventArgs eventArgs)
    {
        try
        {
            if (sender is TextBox textBox)
            {
                ResetInputBoxVisual(textBox);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[片語編輯] HandleTextBoxLeave 失敗：{ex.Message}");
        }
    }

    /// <summary>
    /// 套用與主輸入框一致的強視覺焦點樣式（高對比優先，其次主題感知反轉）。
    /// </summary>
    private static void ApplyInputBoxStrongVisual(TextBox textBox)
    {
        if (textBox.IsDisposed)
        {
            return;
        }

        if (SystemInformation.HighContrast)
        {
            textBox.BackColor = SystemColors.Highlight;
            textBox.ForeColor = SystemColors.HighlightText;

            return;
        }

        if (textBox.IsDarkModeActive())
        {
            // 深色模式：反轉為白底黑字。
            textBox.BackColor = Color.White;
            textBox.ForeColor = Color.Black;
        }
        else
        {
            // 淺色模式：反轉為黑底白字。
            textBox.BackColor = Color.Black;
            textBox.ForeColor = Color.White;
        }
    }

    /// <summary>
    /// 還原輸入框為系統預設背景與前景色
    /// </summary>
    /// <param name="tb">目標輸入框。</param>
    private static void ResetInputBoxVisual(TextBox tb)
    {
        if (tb.IsDisposed)
        {
            return;
        }

        tb.BackColor = Color.Empty;
        tb.ForeColor = Color.Empty;
    }

    /// <summary>
    /// 針對驗證失敗的輸入框提供焦點、音效、震動與視覺提示
    /// </summary>
    /// <param name="target">驗證失敗的輸入框。</param>
    /// <param name="message">要播報的錯誤訊息。</param>
    private void NotifyValidationFailure(TextBox target, string message)
    {
        if (target.CanFocus && !target.Focused)
        {
            target.Focus();
        }

        AnnounceA11y(message, interrupt: true);

        FeedbackService.PlaySound(SystemSounds.Hand);

        FeedbackService.VibrateAsync(
            _gamepadController,
            VibrationPatterns.ActionFail,
            _cts?.Token ?? CancellationToken.None)
            .SafeFireAndForget();

        FlashValidationCueAsync(target).SafeFireAndForget();
    }

    /// <summary>
    /// 暫時閃爍輸入框以提供驗證失敗的視覺提示
    /// </summary>
    /// <param name="target">要閃爍提示的輸入框。</param>
    /// <returns>非同步作業。</returns>
    private async Task FlashValidationCueAsync(TextBox target)
    {
        if (target.IsDisposed ||
            !IsHandleCreated ||
            Interlocked.CompareExchange(ref _isFlashing, 1, 0) != 0)
        {
            return;
        }

        // 僅在對話框生命週期仍有效時建立警示權杖，避免關閉途中留下失去連結的動畫。
        CancellationTokenSource? newAlertCts = _cts.TryCreateLinkedTokenSource();

        if (newAlertCts == null)
        {
            Interlocked.Exchange(ref _isFlashing, 0);

            return;
        }

        Interlocked.Exchange(ref _alertCts, newAlertCts)?.CancelAndDispose();

        CancellationToken token = newAlertCts.Token;

        try
        {
            bool isDark = target.IsDarkModeActive();

            // 決定警示色。
            Color alertColor = SystemInformation.HighContrast ?
                SystemColors.Highlight :
                (isDark ? Color.Firebrick : Color.DarkOrange);

            void ApplyAlertVisuals(float intensity)
            {
                if (target.IsDisposed ||
                    !IsHandleCreated)
                {
                    return;
                }

                if (SystemInformation.HighContrast)
                {
                    bool isAlert = intensity > 0.5f;

                    Color hcBack = isAlert ?
                            alertColor :
                            SystemColors.Window,
                        hcFore = isAlert ?
                            SystemColors.HighlightText :
                            SystemColors.WindowText;

                    target.BackColor = hcBack;
                    target.ForeColor = hcFore;
                }
                else
                {
                    Color pureBase = isDark ?
                        Color.White :
                        Color.Black;

                    int rN = (int)(pureBase.R + (alertColor.R - pureBase.R) * intensity),
                        gN = (int)(pureBase.G + (alertColor.G - pureBase.G) * intensity),
                        bN = (int)(pureBase.B + (alertColor.B - pureBase.B) * intensity);

                    Color flashColor = Color.FromArgb(255, rN, gN, bN);

                    // WCAG 相對亮度精確切換閾值（crossover L≈0.1791）
                    static float FLin(int c)
                    {
                        float f = c / 255f;

                        return f <= 0.04045f ?
                            f / 12.92f :
                            MathF.Pow((f + 0.055f) / 1.055f, 2.4f);
                    }

                    Color flashFore = (0.2126f * FLin(flashColor.R) +
                            0.7152f * FLin(flashColor.G) +
                            0.0722f * FLin(flashColor.B)) > 0.1791f ?
                        Color.Black :
                        Color.White;

                    target.BackColor = flashColor;
                    target.ForeColor = flashFore;
                }
            }

            if (!SystemInformation.UIEffectsEnabled ||
                !AppSettings.Current.EnableAnimatedVisualAlerts)
            {
                await this.SafeInvokeAsync(() => ApplyAlertVisuals(1.0f));

                await Task.Delay(800, token);

                return;
            }

            using PeriodicTimer timer = new(TimeSpan.FromMilliseconds(AppSettings.TargetFrameTimeMs));

            long startTime = Stopwatch.GetTimestamp();

            while (await timer.WaitForNextTickAsync(token))
            {
                long elapsedTicks = Stopwatch.GetTimestamp() - startTime;

                double elapsedMs = (double)elapsedTicks / Stopwatch.Frequency * 1000.0;

                if (elapsedMs >= AppSettings.PhotoSafeFrequencyMs)
                {
                    break;
                }

                double angle = elapsedMs / AppSettings.PhotoSafeFrequencyMs * 2.0 * Math.PI - (Math.PI / 2.0);

                float intensity = (float)((Math.Sin(angle) + 1.0) / 2.0);

                await this.SafeInvokeAsync(() => ApplyAlertVisuals(intensity));
            }
        }
        catch (OperationCanceledException)
        {
            // 正常取消。
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[片語編輯] FlashValidationCueAsync 失敗：{ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _isFlashing, 0);
            Interlocked.Exchange(ref _alertCts, null)?.CancelAndDispose();

            // 確保 UI 狀態還原。
            this.SafeInvoke(() =>
            {
                try
                {
                    if (target.IsDisposed ||
                        !IsHandleCreated)
                    {
                        return;
                    }

                    if (target.Focused)
                    {
                        ApplyInputBoxStrongVisual(target);
                    }
                    else
                    {
                        ResetInputBoxVisual(target);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[片語編輯] 驗證提示動畫 UI 還原失敗：{ex.Message}");
                }
            });
        }
    }

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

    /// <summary>
    /// 游標左移（比照 MainForm.Gamepad.cs 的 MoveCursorLeft）
    /// </summary>
    private void HandleLeft() => this.SafeInvoke(() =>
    {
        try
        {
            TextBox? tb = GetActiveTextBox();

            if (tb == null)
            {
                // 焦點在按鈕區：D-Pad 左向在確認/取消按鈕間循環。
                if (_btnOk.Focused)
                {
                    _btnCancel.Focus();
                    AnnounceA11y(_btnCancel.AccessibleName ?? _btnCancel.Text, interrupt: true);
                    FeedbackService.VibrateAsync(_gamepadController, VibrationPatterns.CursorMove, _cts?.Token ?? CancellationToken.None).SafeFireAndForget();
                }
                else if (_btnCancel.Focused)
                {
                    _btnOk.Focus();
                    AnnounceA11y(_btnOk.AccessibleName ?? _btnOk.Text, interrupt: true);
                    FeedbackService.VibrateAsync(_gamepadController, VibrationPatterns.CursorMove, _cts?.Token ?? CancellationToken.None).SafeFireAndForget();
                }

                return;
            }

            bool hasSelection = tb.SelectionLength > 0;

            if (hasSelection ||
                tb.SelectionStart > 0)
            {
                if (hasSelection)
                {
                    tb.SelectionLength = 0;
                }
                else if (_gamepadController?.IsLeftShoulderHeld == true)
                {
                    tb.WordJump(false);
                }
                else
                {
                    tb.SelectionStart--;
                }

                tb.ScrollToCaret();

                FeedbackService.VibrateAsync(
                    _gamepadController,
                    VibrationPatterns.CursorMove,
                    _cts?.Token ?? CancellationToken.None)
                    .SafeFireAndForget();
            }
            else
            {
                FeedbackService.PlaySound(SystemSounds.Beep);

                FeedbackService.VibrateAsync(
                    _gamepadController,
                    VibrationPatterns.CursorMove,
                    _cts?.Token ?? CancellationToken.None)
                    .SafeFireAndForget();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[片語編輯] 左移失敗：{ex.Message}");
        }
    });

    /// <summary>
    /// 游標右移（比照 MainForm.Gamepad.cs 的 MoveCursorRight）
    /// </summary>
    private void HandleRight() => this.SafeInvoke(() =>
    {
        try
        {
            TextBox? tb = GetActiveTextBox();

            if (tb == null)
            {
                // 焦點在按鈕區：D-Pad 右向在取消/確認按鈕間循環。
                if (_btnCancel.Focused)
                {
                    _btnOk.Focus();
                    AnnounceA11y(_btnOk.AccessibleName ?? _btnOk.Text, interrupt: true);
                    FeedbackService.VibrateAsync(_gamepadController, VibrationPatterns.CursorMove, _cts?.Token ?? CancellationToken.None).SafeFireAndForget();
                }
                else if (_btnOk.Focused)
                {
                    _btnCancel.Focus();
                    AnnounceA11y(_btnCancel.AccessibleName ?? _btnCancel.Text, interrupt: true);
                    FeedbackService.VibrateAsync(_gamepadController, VibrationPatterns.CursorMove, _cts?.Token ?? CancellationToken.None).SafeFireAndForget();
                }

                return;
            }

            bool hasSelection = tb.SelectionLength > 0;

            if (hasSelection || tb.SelectionStart < tb.Text.Length)
            {
                if (hasSelection)
                {
                    tb.SelectionStart += tb.SelectionLength;
                    tb.SelectionLength = 0;
                }
                else if (_gamepadController?.IsLeftShoulderHeld == true)
                {
                    tb.WordJump(true);
                }
                else
                {
                    tb.SelectionStart++;
                }

                tb.ScrollToCaret();

                FeedbackService.VibrateAsync(
                    _gamepadController,
                    VibrationPatterns.CursorMove,
                    _cts?.Token ?? CancellationToken.None)
                    .SafeFireAndForget();
            }
            else
            {
                FeedbackService.PlaySound(SystemSounds.Beep);

                FeedbackService.VibrateAsync(
                    _gamepadController,
                    VibrationPatterns.CursorMove,
                    _cts?.Token ?? CancellationToken.None)
                    .SafeFireAndForget();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[片語編輯] 右移失敗：{ex.Message}");
        }
    });

    /// <summary>
    /// 上方向鍵：在名稱欄位與內容欄位和按鈕之間切換焦點
    /// </summary>
    private void HandleFieldPrev() => this.SafeInvoke(() =>
    {
        try
        {
            if (_txtContent.Focused)
            {
                _txtName.Focus();
            }
            else if (_btnOk.Focused)
            {
                _txtContent.Focus();
            }
            else if (_btnCancel.Focused)
            {
                _btnOk.Focus();
            }
            else if (_txtName.Focused)
            {
                _btnCancel.Focus();
            }

            FeedbackService.VibrateAsync(
                _gamepadController,
                VibrationPatterns.CursorMove,
                _cts?.Token ?? CancellationToken.None)
                .SafeFireAndForget();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[片語編輯] 欄位向前失敗：{ex.Message}");
        }
    });

    /// <summary>
    /// 下方向鍵：在名稱欄位、內容欄位與按鈕之間循環焦點
    /// </summary>
    private void HandleFieldNext() => this.SafeInvoke(() =>
    {
        try
        {
            if (_txtName.Focused)
            {
                _txtContent.Focus();
            }
            else if (_txtContent.Focused)
            {
                _btnOk.Focus();
            }
            else if (_btnOk.Focused)
            {
                _btnCancel.Focus();
            }
            else if (_btnCancel.Focused)
            {
                _txtName.Focus();
            }

            FeedbackService.VibrateAsync(
                _gamepadController,
                VibrationPatterns.CursorMove,
                _cts?.Token ?? CancellationToken.None)
                .SafeFireAndForget();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[片語編輯] 欄位向後失敗：{ex.Message}");
        }
    });

    /// <summary>
    /// X 鍵：刪除選取文字或游標前一字元（比照 MainForm.Gamepad.cs 的 X 鍵刪除邏輯）
    /// </summary>
    private void HandleBackspace() => this.SafeInvoke(() =>
    {
        try
        {
            TextBox? tb = GetActiveTextBox();

            if (tb == null ||
                tb.ReadOnly)
            {
                return;
            }

            if (tb.SelectionLength > 0)
            {
                tb.SelectedText = string.Empty;
            }
            else if (tb.SelectionStart > 0)
            {
                int pos = tb.SelectionStart;

                tb.Select(pos - 1, 1);
                tb.SelectedText = string.Empty;
            }
            else
            {
                FeedbackService.PlaySound(SystemSounds.Beep);

                FeedbackService.VibrateAsync(
                    _gamepadController,
                    VibrationPatterns.CursorMove,
                    _cts?.Token ?? CancellationToken.None)
                    .SafeFireAndForget();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[片語編輯] 退格失敗：{ex.Message}");
        }
    });

    /// <summary>
    /// 右搖桿左推：擴張選取範圍向左（比照 MainForm.Gamepad.cs 的 ExpandSelection）
    /// </summary>
    private void HandleRSLeft() => this.SafeInvoke(() =>
    {
        try
        {
            ExpandSelection(-1);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[片語編輯] 右摘桿左移失敗：{ex.Message}");
        }
    });

    /// <summary>
    /// 右搖桿右推：擴張選取範圍向右
    /// </summary>
    private void HandleRSRight() => this.SafeInvoke(() =>
    {
        try
        {
            ExpandSelection(1);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[片語編輯] 右摘桿右移失敗：{ex.Message}");
        }
    });

    /// <summary>
    /// Y 鍵：開啟焦點 TextBox 的原生右鍵選單
    /// </summary>
    private void HandleOpenContextMenu() => this.SafeInvoke(() =>
    {
        try
        {
            TextBox? tb = GetActiveTextBox();

            if (tb == null)
            {
                return;
            }

            // 在游標位置附近顯示內建右鍵選單。
            Point caretPos = tb.GetPositionFromCharIndex(tb.SelectionStart);

            tb.ContextMenuStrip?.Show(tb, caretPos);

            // TextBox 沒有 ContextMenuStrip 時，透過模擬 Shift+F10 觸發原生選單。
            if (tb.ContextMenuStrip == null)
            {
                User32.SendMessage(tb.Handle, 0x007B, tb.Handle, unchecked((nint)0xFFFFFFFF));
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[片語編輯] 開啟右鍵選單失敗：{ex.Message}");
        }
    });

    /// <summary>
    /// 擴張或縮減文字選取範圍（比照 MainForm.Gamepad.cs 的 ExpandSelection）
    /// </summary>
    /// <param name="direction">方向，正數表示向右擴張，負數表示向左擴張。</param>
    private void ExpandSelection(int direction)
    {
        TextBox? tb = GetActiveTextBox();

        if (tb == null)
        {
            return;
        }

        if (tb.SelectionLength == 0 ||
            _rsSelectionAnchor == null ||
            (tb.SelectionStart != _rsSelectionAnchor.Value &&
             tb.SelectionStart + tb.SelectionLength != _rsSelectionAnchor.Value))
        {
            _rsSelectionAnchor = tb.SelectionStart;
        }

        int anchor = _rsSelectionAnchor.Value,
            caret = (tb.SelectionStart == anchor) ?
                (anchor + tb.SelectionLength) :
                tb.SelectionStart;

        int safeDirection = Math.Sign(direction),
            newCaret = Math.Clamp(caret + safeDirection, 0, tb.TextLength);

        if (newCaret == caret)
        {
            FeedbackService.VibrateAsync(
                _gamepadController,
                VibrationPatterns.ActionFail,
                _cts?.Token ?? CancellationToken.None)
                .SafeFireAndForget();

            return;
        }

        // 使用 Win32 EM_SETSEL 正確設定選取範圍（含反向選取）。
        User32.SendMessage(tb.Handle, 0x00B1, anchor, newCaret);

        FeedbackService.VibrateAsync(
            _gamepadController,
            VibrationPatterns.CursorMove,
            _cts?.Token ?? CancellationToken.None)
            .SafeFireAndForget();
    }

    /// <summary>
    /// 控制器重新連線後恢復輪詢狀態。
    /// </summary>
    /// <param name="connected">新的控制器連線狀態。</param>
    private void HandleConnectionChanged(bool connected)
    {
        try
        {
            if (connected)
            {
                _gamepadController?.Resume();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[片語編輯] 控制器連線變更：{ex.Message}");
        }
    }

    /// <summary>
    /// 解除目前片語編輯對話框所綁定的控制器事件
    /// </summary>
    private void UnsubscribeGamepadEvents()
    {
        try
        {
            if (_gamepadController != null)
            {
                _gamepadController.APressed -= HandleGamepadA;
                _gamepadController.APressed -= HandleBackOrClear;
                _gamepadController.StartPressed -= HandleOpenTouchKeyboardFromGamepad;
                _gamepadController.BPressed -= HandleGamepadA;
                _gamepadController.BPressed -= HandleBackOrClear;
                _gamepadController.BackPressed -= HandleCancel;
                _gamepadController.LeftPressed -= HandleLeft;
                _gamepadController.LeftRepeat -= HandleLeft;
                _gamepadController.RightPressed -= HandleRight;
                _gamepadController.RightRepeat -= HandleRight;
                _gamepadController.UpPressed -= HandleFieldPrev;
                _gamepadController.DownPressed -= HandleFieldNext;
                _gamepadController.XPressed -= HandleBackspace;
                _gamepadController.YPressed -= HandleOpenContextMenu;
                _gamepadController.RSLeftPressed -= HandleRSLeft;
                _gamepadController.RSLeftRepeat -= HandleRSLeft;
                _gamepadController.RSRightPressed -= HandleRSRight;
                _gamepadController.RSRightRepeat -= HandleRSRight;
                _gamepadController.ConnectionChanged -= HandleConnectionChanged;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[片語編輯] UnsubscribeGamepadEvents 失敗：{ex.Message}");
        }
    }

    /// <summary>
    /// 更新片語名稱字元數提示標籤（{current}/{max}），近上限時顯示橙色。
    /// </summary>
    private void UpdateNameCharCount()
    {
        if (_lblNameCount == null || _lblNameCount.IsDisposed)
        {
            return;
        }

        int len = _txtName.TextLength;
        int max = AppSettings.MaxPhraseNameLength;
        string countLabel = GetPhraseTextOrFallback("Phrase_Edit_Name_Count", "Name length: ");
        string countText = $"{countLabel}{len}/{max}";

        _lblNameCount.Text = countText;
        _lblNameCount.AccessibleName = countText;
        _lblNameCount.AccessibleDescription = $"{Strings.Phrase_A11y_Edit_Name_Desc} {countText}";

        if (SystemInformation.HighContrast)
        {
            _lblNameCount.ForeColor = Color.Empty;

            return;
        }

        _lblNameCount.ForeColor = len >= max - 10 ?
            Color.DarkOrange :
            Color.Empty;
    }

    /// <summary>
    /// 更新片語內容字元數提示標籤（{current}/{max}），近上限時顯示橙色。
    /// </summary>
    private void UpdateContentCharCount()
    {
        if (_lblContentCount == null || _lblContentCount.IsDisposed)
        {
            return;
        }

        int len = _txtContent.TextLength;
        int max = AppSettings.MaxInputLength;
        string countLabel = GetPhraseTextOrFallback("Phrase_Edit_Content_Count", "Content length: ");
        string countText = $"{countLabel}{len}/{max}";

        _lblContentCount.Text = countText;
        _lblContentCount.AccessibleName = countText;
        _lblContentCount.AccessibleDescription = $"{Strings.Phrase_A11y_Edit_Content_Desc} {countText}";

        if (SystemInformation.HighContrast)
        {
            _lblContentCount.ForeColor = Color.Empty;

            return;
        }

        _lblContentCount.ForeColor = len >= max - 50 ?
            Color.DarkOrange :
            Color.Empty;
    }

    /// <summary>
    /// 更新按鈕最小尺寸（抗抖動 + WCAG 2.5.5 AAA 44×44）
    /// </summary>
    private void UpdateButtonMinimumSizes()
    {
        float scale = DeviceDpi / AppSettings.BaseDpi;

        UpdateSingleButtonMinimumSize(_btnOk, scale);
        UpdateSingleButtonMinimumSize(_btnCancel, scale);
    }

    /// <summary>
    /// 更新單一按鈕的最小尺寸，避免焦點加粗造成版面抖動
    /// </summary>
    /// <param name="btn">目標按鈕。</param>
    /// <param name="scale">目前 DPI 縮放比例。</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void UpdateSingleButtonMinimumSize(Button btn, float scale)
    {
        try
        {
            if (btn.IsDisposed) return;

            Font boldFont = _boldFont ?? MainForm.GetSharedA11yFont(DeviceDpi, FontStyle.Bold);

            DialogLayoutHelper.UpdateButtonMinimumSize(btn, boldFont, scale, 44, 44, 24, 16);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[片語編輯] UpdateSingleButtonMinimumSize 失敗：{ex.Message}");
        }
    }

    /// <summary>
    /// 廣播無障礙訊息
    /// </summary>
    /// <param name="message">要廣播的訊息</param>
    /// <param name="interrupt">是否中斷目前的廣播</param>
    private void AnnounceA11y(string message, bool interrupt = false)
    {
        if (IsDisposed ||
            string.IsNullOrEmpty(message))
        {
            return;
        }

        if (Owner is MainForm mainForm)
        {
            mainForm.AnnounceA11y(message, interrupt);
        }
        else if (Owner is PhraseManagerDialog phraseManager)
        {
            // 嘗試寫往主視窗。
            if (phraseManager.Owner is MainForm main)
            {
                main.AnnounceA11y(message, interrupt);

                return;
            }
        }

        // 本地備援
        long currentId = Interlocked.Increment(ref _a11yDebounceId);

        Task.Run(async () =>
        {
            try
            {
                await Task.Delay(AppSettings.AudioDuckingDelayMs, _cts?.Token ?? CancellationToken.None);

                if (Interlocked.Read(ref _a11yDebounceId) == currentId &&
                    !IsDisposed &&
                    IsHandleCreated)
                {
                    await this.SafeInvokeAsync(() =>
                        _announcer.Announce(message, interrupt && AppSettings.Current.A11yInterruptEnabled));
                }
            }
            catch (OperationCanceledException)
            {

            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[片語編輯] A11y 廣播失敗：{ex.Message}");
            }
        },
        _cts?.Token ?? CancellationToken.None)
        .SafeFireAndForget();
    }

    /// <summary>
    /// 依 DPI 更新最小尺寸，讓片語名稱／內容輸入框有更充足的可視範圍
    /// </summary>
    private void UpdateMinimumSize(bool forceRecalculate = false)
    {
        float currentDpi = DeviceDpi;

        if (!DialogLayoutHelper.TryBeginDpiLayout(currentDpi, ref _lastAppliedDpi, forceRecalculate))
        {
            return;
        }

        float scale = currentDpi / AppSettings.BaseDpi;

        int desiredMinWidth = (int)(BaseDialogMinWidth * scale);

        Rectangle workArea = Screen.GetWorkingArea(this);

        // 小尺寸螢幕保護：保留 40px 邊界，避免高縮放下最小尺寸超出可視區。
        (int maxFitWidth, int maxFitHeight) = DialogLayoutHelper.GetMaxFitSize(workArea);

        int
            // 正常情況至少保留 320px 的可編輯寬度；若工作區本身更窄，則以工作區上限為準。
            minWidth = maxFitWidth >= 320 ?
                Math.Clamp(desiredMinWidth, 320, maxFitWidth) :
                maxFitWidth;

        int desiredMinHeight = (int)(340 * scale),
            minH = Math.Min(desiredMinHeight, maxFitHeight);

        DialogLayoutHelper.ClampFormSize(this, minWidth, minH, maxFitWidth, maxFitHeight, ApplySmartPosition);
    }

    /// <summary>
    /// 保持對話框位於目前螢幕可視範圍內
    /// </summary>
    private void ApplySmartPosition()
    {
        if (InputBoxLayoutManager.TryGetClampedLocation(this, out Point clampedLocation))
        {
            Location = clampedLocation;
        }
    }
}