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
using System.Windows.Forms.Automation;

namespace InputBox.Core.Controls;

// 阻擋設計工具。
partial class DesignerBlocker { };

/// <summary>
/// 專門用於數值輸入的對話框
/// </summary>
internal sealed partial class NumericInputDialog : Form
{
    /// <summary>
    /// 預設數值
    /// </summary>
    private readonly decimal _defaultValue;

    /// <summary>
    /// AccessibleNumericUpDown 實例
    /// </summary>
    private AccessibleNumericUpDown? _nud;

    /// <summary>
    /// 3x2 網格連動區容器
    /// </summary>
    private TableLayoutPanel? _tlpGrid;

    /// <summary>
    /// 確定按鈕
    /// </summary>
    private Button? _btnOk;

    /// <summary>
    /// 取消按鈕
    /// </summary>
    private Button? _btnCancel;

    /// <summary>
    /// 增加按鈕
    /// </summary>
    private Button? _btnPlus;

    /// <summary>
    /// 減少按鈕
    /// </summary>
    private Button? _btnMinus;

    /// <summary>
    /// 重設按鈕
    /// </summary>
    private Button? _btnReset;

    /// <summary>
    /// 用於 A11y 廣播的 Label
    /// </summary>
    private AnnouncerLabel? _announcer;

    /// <summary>
    /// 統一放大的 A11y 字型
    /// </summary>
    private Font? _a11yFont;

    /// <summary>
    /// NUD 專屬放大字型（來自共享快取，絕對禁止在此處手動處置）
    /// </summary>
    private Font? _nudFont;

    /// <summary>
    /// 用於管理對話框生命週期內非同步任務的取消權杖來源
    /// </summary>
    private CancellationTokenSource? _cts = new();

    /// <summary>
    /// 右搖桿虛擬選取的起點錨點
    /// </summary>
    private int? _rsSelectionAnchor = null;

    /// <summary>
    /// 右搖桿快速選取時，用來形成拉鏈感的 burst 視窗。
    /// </summary>
    private const int SelectionBurstWindowMs = 110;

    /// <summary>
    /// 記錄最近一次右搖桿選取回饋的時間與 burst 等級。
    /// </summary>
    private DateTime _lastSelectionFeedbackUtc = DateTime.MinValue;
    private int _selectionFeedbackBurstLevel;

    /// <summary>
    /// A11y 廣播防抖用的序號
    /// </summary>
    private long _a11yDebounceId = 0;

    /// <summary>
    /// 上一次建立的游標寬度
    /// </summary>
    private int _lastCaretWidth = -1;

    /// <summary>
    /// 上一次建立的游標高度
    /// </summary>
    private int _lastCaretHeight = -1;

    /// <summary>
    /// 用於管理警示動畫的中斷控制
    /// </summary>
    private CancellationTokenSource? _alertCts;

    /// <summary>
    /// 是否正在閃爍（用於防止重複觸發閃爍效果）
    /// </summary>
    private volatile int _isFlashing = 0;

    /// <summary>
    /// Gamepad 控制介面
    /// </summary>
    private IGamepadController? _gamepadController;

    /// <summary>
    /// 已套用的 DPI 快取，避免重複計算最小尺寸
    /// </summary>
    private float _lastAppliedDpi;

    /// <summary>
    /// 取得目前數值
    /// </summary>
    public decimal Value => _nud?.Value ?? 0m;

    /// <summary>
    /// 確認關閉前的驗證回呼。回傳 false 表示中止關閉，對話框維持開啟狀態。
    /// </summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Func<decimal, bool>? ConfirmBeforeClose { get; set; }

    protected override void Dispose(bool disposing)
    {
        try
        {
            if (disposing)
            {
                // 優先解除控制器事件訂閱，確保事件解除先於控制項處置，
                // 防止控制器輪詢在 UI 控制項釋放後仍觸發事件。
                UnsubscribeGamepadEvents();

                // 處置取消權杖來源（主要任務與 Flash Alert 各自獨立處置）。
                Interlocked.Exchange(ref _cts, null)?.CancelAndDispose();
                Interlocked.Exchange(ref _alertCts, null)?.CancelAndDispose();

                // 原子化處置 UI 控制項與資源。
                Interlocked.Exchange(ref _nud, null)?.Dispose();
                Interlocked.Exchange(ref _tlpGrid, null)?.Dispose();
                Interlocked.Exchange(ref _btnOk, null)?.Dispose();
                Interlocked.Exchange(ref _btnCancel, null)?.Dispose();
                Interlocked.Exchange(ref _btnPlus, null)?.Dispose();
                Interlocked.Exchange(ref _btnMinus, null)?.Dispose();
                Interlocked.Exchange(ref _btnReset, null)?.Dispose();

                // 共享資源僅歸零，由 Program.cs 統一釋放。
                Interlocked.Exchange(ref _nudFont, null);
                Interlocked.Exchange(ref _a11yFont, null);

                Interlocked.Exchange(ref _announcer, null)?.Dispose();

                // 確保靜態事件在視窗處置時被絕對釋放，防止 Handle 未建立時的洩漏。
                SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
            }
        }
        finally
        {
            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// 處理確認按鍵事件
    /// </summary>
    private void HandleConfirm() => this.SafeInvoke(() =>
    {
        try
        {
            if (IsDisposed ||
                !IsHandleCreated)
            {
                return;
            }

            _nud?.ValidateValue();

            // 關閉前執行呼叫端的驗證回呼（例如低不透明度警告）。
            if (ConfirmBeforeClose != null && !ConfirmBeforeClose(Value))
            {
                // 使用者在警告對話框中取消：保持 NumericInputDialog 開啟。
                return;
            }

            // 發送與控制器對等的震動與 A11y 播報。
            FeedbackService.PlaySound(SystemSounds.Asterisk);

            FeedbackService.VibrateAsync(
                    _gamepadController,
                    VibrationPatterns.CopySuccess,
                    _cts?.Token ?? CancellationToken.None)
                .SafeFireAndForget();

            // 直接在 UI 執行緒同步播報，避免 Task.Run 路徑在 Dispose 取消
            // _cts 後因 OperationCanceledException 而遺失此關鍵訊息。
            _announcer?.Announce(Strings.A11y_Returning, false);

            DialogResult = DialogResult.OK;

            Close();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[NumericInputDialog] HandleConfirm 失敗：{ex.Message}");
        }
    });

    /// <summary>
    /// 處理取消按鍵事件
    /// </summary>
    private void HandleCancel() => this.SafeInvoke(() =>
    {
        try
        {
            if (IsDisposed ||
                !IsHandleCreated)
            {
                return;
            }

            // 發送與控制器對等的震動（比照返回動作）。
            FeedbackService.PlaySound(SystemSounds.Exclamation);

            FeedbackService.VibrateAsync(
                    _gamepadController,
                    VibrationPatterns.ReturnStart,
                    _cts?.Token ?? CancellationToken.None)
                .SafeFireAndForget();

            // A11y 播報在 FormClosing 中已處理。

            DialogResult = DialogResult.Cancel;

            Close();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[NumericInputDialog] HandleCancel 失敗：{ex.Message}");
        }
    });

    /// <summary>
    /// 處理重設按鍵事件
    /// </summary>
    private void HandleReset() => this.SafeInvoke(() =>
    {
        try
        {
            if (GamescopeSurfaceRecovery.TryRecoverFromGamepadChord(
                this,
                RecreateHandle,
                _gamepadController,
                context: "NumericInputDialog Gamescope surface recovery 失敗"))
            {
                return;
            }

            if (IsDisposed ||
                !IsHandleCreated ||
                _nud == null)
            {
                return;
            }

            _nud.Value = Math.Clamp(_defaultValue, _nud.Minimum, _nud.Maximum);
            _nud.Focus();

            FeedbackService.PlaySound(SystemSounds.Asterisk);

            FeedbackService.VibrateAsync(
                    _gamepadController,
                    VibrationPatterns.CursorMove)
                .SafeFireAndForget();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[NumericInputDialog] HandleReset 失敗：{ex.Message}");
        }
    });

    /// <summary>
    /// 初始化對話框組件
    /// </summary>
    /// <param name="title">對話框標題</param>
    /// <param name="currentValue">當前數值</param>
    /// <param name="defaultValue">預設數值</param>
    /// <param name="decimalPlaces">小數位數</param>
    /// <param name="increment">增量值</param>
    /// <param name="minimum">最小值</param>
    /// <param name="maximum">最大值</param>
    public NumericInputDialog(
        string title,
        decimal currentValue,
        decimal defaultValue,
        int decimalPlaces,
        decimal increment,
        decimal minimum,
        decimal maximum)
    {
        // 邊界驗證：確保最小值不超過最大值，防止 Math.Clamp 拋出異常。
        if (minimum > maximum)
        {
            (minimum, maximum) = (maximum, minimum);
        }

        _defaultValue = defaultValue;

        Text = title;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        TopMost = true;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        AccessibleName = title;
        AccessibleRole = AccessibleRole.Dialog;
        DoubleBuffered = true;

        // 建立 A11y 廣播器（模擬標準狀態列，確保 NVDA 可靠識別為 live region）。
        _announcer = new AnnouncerLabel
        {
            AccessibleName = "\u200B",
            Dock = DockStyle.Bottom,
            Height = 1,
            TabStop = false,
            BackColor = Color.Empty,
            ForeColor = Color.Empty,
        };

        // 繼承圖示：優先從主視窗繼承，保持應用程式視覺識別的一致性。
        Icon = Application.OpenForms.OfType<MainForm>().FirstOrDefault()?.Icon ??
            ActiveForm?.Icon;

        // 根據 DPI 縮放比例計算佈局參數。
        float scale = DeviceDpi / AppSettings.BaseDpi;

        // 取得共享的 A11y 放大字型（預設為 Regular）。
        _a11yFont = MainForm.GetSharedA11yFont(DeviceDpi);

        // 數值顯示區字體：使用 2.0x 的放大倍率以突顯數值。
        // 注意：此字體為對話框特有，需獨立建立且強制為粗體。
        _nudFont = MainForm.GetSharedA11yFont(DeviceDpi, FontStyle.Bold, _a11yFont.FontFamily, 2.0f);

        // 主佈局容器。
        TableLayoutPanel tlpMain = new()
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding((int)(30 * scale)),
            // 設定為 Grouping 角色，協助輔助科技識別這是一個邏輯區塊。
            AccessibleRole = AccessibleRole.Grouping,
            AccessibleName = title
        };

        Label lblPrompt = new()
        {
            Text = string.Format(Strings.Msg_EnterValue, title),
            AccessibleName = string.Format(Strings.Msg_EnterValue, title),
            AutoSize = true,
            MaximumSize = new Size((int)(500 * scale), 0),
            Margin = new Padding(0, 0, 0, (int)(25 * scale)),
            Font = _a11yFont,
            // AccessibleRole.StaticText + 作為首要 Label：
            // WinForms 的 AccessibleRole 列舉不含 Heading，
            // 此標籤置於對話框最頂端，文字內容已充分描述區段目的，
            // 滿足 WCAG 2.4.10 的精神（區段標題，AAA）。
            AccessibleRole = AccessibleRole.StaticText
        };

        // 3x2 網格連動區。
        _tlpGrid = new TableLayoutPanel()
        {
            AutoSize = true,
            ColumnCount = 3,
            RowCount = 2,
            AccessibleRole = AccessibleRole.Grouping,
            AccessibleName = string.Format(Strings.Msg_EnterValue, title),
            AccessibleDescription = Strings.A11y_Grid_Numeric_Desc
        };
        _tlpGrid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _tlpGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        _tlpGrid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        // 核心控制項建立。

        _nud = new AccessibleNumericUpDown(this)
        {
            DecimalPlaces = decimalPlaces,
            Increment = increment,
            Minimum = minimum,
            Maximum = maximum,
            // 使用 Math.Clamp 確保數值在有效範圍內，防止異常。
            Value = Math.Clamp(currentValue, minimum, maximum),
            TextAlign = HorizontalAlignment.Center,
            // 動態縮放寬度，高度則由 Font 自動決定。
            Width = (int)(220 * scale),
            Font = _nudFont,
            BackColor = Color.Empty,
            ForeColor = Color.Empty,
            Margin = new Padding((int)(15 * scale), (int)(12 * scale), (int)(15 * scale), (int)(12 * scale)),
            AccessibleName = string.Format(Strings.Msg_EnterValue, title),
            // A11y 描述：報讀目前值與有效範圍。
            AccessibleDescription = string.Format(Strings.A11y_Value_Range_Desc, currentValue, minimum, maximum),
            AccessibleRole = AccessibleRole.SpinButton,
            TabIndex = 1,
            Anchor = AnchorStyles.None
        };

        _nud.Enter += (s, e) =>
        {
            try
            {
                UpdateFocusVisuals(true);

                UpdateCaretWidth();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[NumericInputDialog] _nud.Enter 失敗：{ex.Message}");
            }
        };
        _nud.Leave += (s, e) =>
        {
            try
            {
                Interlocked.Exchange(ref _alertCts, null)?.CancelAndDispose();

                User32.DestroyCaret();

                // 重置游標快取，確保下次焦點進入時能正確重建游標
                _lastCaretWidth = -1;
                _lastCaretHeight = -1;

                UpdateFocusVisuals(false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[NumericInputDialog] _nud.Leave 失敗：{ex.Message}");
            }
        };

        _nud.KeyDown += (s, e) =>
        {
            try
            {
                // 處理鍵盤左右鍵位移／選取 A11y 同步。
                if (e.KeyCode == Keys.Left ||
                    e.KeyCode == Keys.Right)
                {
                    // 使用 SafeBeginInvoke 確保在 Windows 原生位移處理完成後，才擷取最新的游標位置。
                    this.SafeBeginInvoke(() =>
                    {
                        try
                        {
                            if (_nud == null ||
                                _nud.IsDisposed)
                            {
                                return;
                            }

                            TextBox? textBox = _nud.Controls.OfType<TextBox>().FirstOrDefault();

                            if (textBox == null ||
                                textBox.IsDisposed)
                            {
                                return;
                            }

                            if (e.Shift)
                            {
                                if (textBox.SelectionLength > 0)
                                {
                                    AnnounceA11y(AppSettings.Current.IsPrivacyMode ?
                                        Strings.A11y_Selected_Text_PrivacySafe :
                                        string.Format(Strings.A11y_Selected_Text, textBox.SelectedText), true);
                                }
                            }
                            else
                            {
                                // 取得目前游標所在的絕對索引（1-based 報讀）。
                                int pos = textBox.SelectionStart;

                                AnnounceA11y(AppSettings.Current.IsPrivacyMode ?
                                    Strings.A11y_Cursor_Move_PrivacySafe :
                                    string.Format(Strings.A11y_Cursor_Move, pos + 1), true);
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[NumericInputDialog] KeyDown 位移同步失敗：{ex.Message}");
                        }
                    });
                }

                // Home、End、Ctrl + Left／Right：跳轉游標。
                if (e.KeyCode == Keys.Home ||
                    e.KeyCode == Keys.End ||
                    (e.Control && (e.KeyCode == Keys.Left || e.KeyCode == Keys.Right)))
                {
                    this.SafeBeginInvoke(() =>
                    {
                        try
                        {
                            if (_nud == null ||
                                _nud.IsDisposed)
                            {
                                return;
                            }

                            TextBox? textBox = _nud.Controls.OfType<TextBox>().FirstOrDefault();

                            if (textBox != null &&
                                !textBox.IsDisposed)
                            {
                                AnnounceA11y(AppSettings.Current.IsPrivacyMode ?
                                    Strings.A11y_Cursor_Move_PrivacySafe :
                                    string.Format(Strings.A11y_Cursor_Move, textBox.SelectionStart + 1), true);
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[NumericInputDialog] KeyDown 跳轉同步失敗：{ex.Message}");
                        }
                    });
                }

                // Backspace 或 Delete：刪除文字。
                if (e.KeyCode == Keys.Back ||
                    e.KeyCode == Keys.Delete)
                {
                    if (_nud == null ||
                        _nud.IsDisposed)
                    {
                        return;
                    }

                    TextBox? textBox = _nud.Controls.OfType<TextBox>().FirstOrDefault();

                    if (textBox == null ||
                        textBox.IsDisposed)
                    {
                        return;
                    }

                    HandleDeleteKey(textBox, e.KeyCode == Keys.Back);
                }

                // 確保Home／End 在改變數值時（WinForms 原生行為）也能透過 SafeBeginInvoke 觸發數值變更廣播。
                if (e.KeyCode == Keys.Home ||
                    e.KeyCode == Keys.End)
                {
                    this.SafeBeginInvoke(() =>
                    {
                        try
                        {
                            if (_nud == null ||
                                _nud.IsDisposed)
                            {
                                return;
                            }

                            // 主動通知數值變更，這會協助螢幕閱讀器抓取最新狀態。
                            _nud.NotifyAccessibilityChange();
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[NumericInputDialog] KeyDown 邊界通知失敗：{ex.Message}");
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[NumericInputDialog] KeyDown 處理失敗：{ex.Message}");
            }
        };

        // 當數值改變時，同步更新無障礙描述，並主動廣播最新數值。
        _nud.ValueChanged += (s, e) =>
        {
            try
            {
                if (_nud == null)
                {
                    return;
                }

                _nud.AccessibleDescription = string.Format(
                    Strings.A11y_Value_Range_Desc,
                    _nud.Value,
                    _nud.Minimum,
                    _nud.Maximum);

                // 廣播包含項目名稱的完整訊息（例如：「不透明度：50%」）。
                string valStr = _nud.DecimalPlaces > 0 ?
                    _nud.Value.ToString($"F{_nud.DecimalPlaces}") :
                    _nud.Value.ToString("F0");

                // 如果是對話框標題（如「不透明度」），則組合起來播報。
                string announcement = $"{title}：{valStr}";

                // 如果有小數位數（如 0.1），檢查是否需要格式化為百分比。
                if (title == Strings.Settings_WindowOpacity)
                {
                    announcement = string.Format(Strings.A11y_Opacity_Changed, _nud.Value / 100);
                }

                AnnounceA11y(announcement, interrupt: true);

                FeedbackService.VibrateAsync(
                        _gamepadController,
                        VibrationPatterns.CursorMove)
                    .SafeFireAndForget();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[NumericInputDialog] ValueChanged 失敗：{ex.Message}");
            }
        };

        // 按鈕連動 NUD 高亮邏輯。

        // 第 1 列：數值加減。
        _btnMinus = CreateEyeTrackerButton(
            Strings.Btn_Minus,
            Strings.A11y_Btn_Minus_Desc,
            scale,
            _a11yFont,
            (active) =>
            {
                try
                {
                    UpdateFocusVisuals(
                        active ||
                        (_nud?.Focused == true) ||
                        (_nud?.ContainsFocus == true));
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[NumericInputDialog] _btnMinus 焦點回呼失敗：{ex.Message}");
                }
            });

        AttachAutoRepeat(_btnMinus, HandleMinus);

        _btnMinus.Anchor = AnchorStyles.None;
        _btnMinus.TabIndex = 0;

        _btnPlus = CreateEyeTrackerButton(
            Strings.Btn_Plus,
            Strings.A11y_Btn_Plus_Desc,
            scale,
            _a11yFont,
            (active) =>
            {
                try
                {
                    UpdateFocusVisuals(
                        active ||
                        (_nud?.Focused == true) ||
                        (_nud?.ContainsFocus == true));
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[NumericInputDialog] _btnPlus 焦點回呼失敗：{ex.Message}");
                }
            });

        AttachAutoRepeat(_btnPlus, HandlePlus);

        _btnPlus.Anchor = AnchorStyles.None;
        _btnPlus.TabIndex = 2;

        // 第 2 列：操作按鈕。
        // profile 用於將確認／取消按鈕文字切換為目前控制器模式對應的顯示。
        GamepadFaceButtonProfile profile = GamepadFaceButtonProfile.GetActiveProfile();

        _btnOk = CreateEyeTrackerButton(
            profile.FormatConfirmButtonText(Strings.Btn_OK),
            Strings.A11y_Btn_OK_Desc,
            scale,
            _a11yFont);
        _btnOk.Click += (s, e) => HandleConfirm();
        _btnOk.Anchor = AnchorStyles.None;
        _btnOk.TabIndex = 3;

        _btnCancel = CreateEyeTrackerButton(
            profile.FormatCancelButtonText(Strings.Btn_Cancel),
            Strings.A11y_Btn_Cancel_Desc,
            scale,
            _a11yFont);
        _btnCancel.Click += (s, e) => HandleCancel();
        _btnCancel.Anchor = AnchorStyles.None;
        _btnCancel.TabIndex = 5;

        AcceptButton = _btnOk;
        CancelButton = _btnCancel;

        _btnReset = CreateEyeTrackerButton(
            profile.FormatMenuButtonText(Strings.Btn_SetDefault),
            string.Format(Strings.A11y_Btn_SetDefault_Desc, _defaultValue),
            scale,
            _a11yFont);
        _btnReset.Click += (s, e) => HandleReset();
        _btnReset.Anchor = AnchorStyles.None;
        _btnReset.TabIndex = 4;

        // 填充 3x2 網格。
        _tlpGrid.Controls.Add(_btnMinus, 0, 0);
        _tlpGrid.Controls.Add(_nud, 1, 0);
        _tlpGrid.Controls.Add(_btnPlus, 2, 0);
        _tlpGrid.Controls.Add(_btnOk, 0, 1);
        _tlpGrid.Controls.Add(_btnCancel, 2, 1);
        _tlpGrid.Controls.Add(_btnReset, 1, 1);

        tlpMain.Controls.Add(lblPrompt, 0, 0);
        tlpMain.Controls.Add(_tlpGrid, 0, 1);

        Controls.Add(tlpMain);
        Controls.Add(_announcer);

        Shown += (s, e) =>
        {
            try
            {
                // 協調 LiveRegion：當彈出對話框時，暫時停用主視窗的廣播器 LiveSetting，防止訊息干擾。
                MainForm? mainForm = GetOwnerMainForm();

                mainForm?.SetA11yLiveSetting(AutomationLiveSetting.Off);

                // 強化 Context 播報：包含提示文字、目前值與有效範圍。
                string contextMessage = $"{lblPrompt.Text} {string.Format(Strings.A11y_Value_Range_Desc, Value, minimum, maximum)}";

                AnnounceA11y(contextMessage);

                _nud?.Focus();

                UpdateFocusVisuals(true);

                // 執行智慧定位修正，確保視窗初次顯示時不會跑出螢幕邊界。
                ApplySmartPosition();

                this.SafeBeginInvoke(() =>
                {
                    try
                    {
                        _nud?.Select(0, _nud.Text.Length);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[NumericInputDialog] 選取文字失敗：{ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[NumericInputDialog] Shown 處理失敗：{ex.Message}");
            }
        };

        Activated += (s, e) =>
        {
            Task.Run(async () =>
            {
                try
                {
                    CancellationToken token = _cts?.Token ?? CancellationToken.None;

                    // 在執行任何延遲操作前先檢查 Token。
                    token.ThrowIfCancellationRequested();

                    await Task.Delay(50, token);

                    // 使用 SafeInvokeAsync 確保在 Handle 毀損或視窗關閉時不會拋出例外。
                    // 內部已包含 IsDisposed 與 IsHandleCreated 檢查。
                    await this.SafeInvokeAsync(() =>
                    {
                        try
                        {
                            _gamepadController?.Resume();
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[NumericInputDialog] 恢復控制器失敗：{ex.Message}");
                        }
                    });
                }
                catch (OperationCanceledException)
                {
                    // 正常取消，不進行報錯。
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Activated] 恢復控制器狀態失敗：{ex.Message}");
                }
            },
            _cts?.Token ?? CancellationToken.None)
            .SafeFireAndForget();
        };

        Deactivate += (s, e) =>
        {
            try
            {
                // 根據規範：視窗失去焦點時必須立即暫停控制器，防止背景誤觸。
                this.SafeBeginInvoke(() =>
                {
                    try
                    {
                        _gamepadController?.Pause();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[NumericInputDialog] 暫停控制器失敗：{ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[NumericInputDialog] Deactivate 處理失敗：{ex.Message}");
            }
        };

        FormClosing += (s, e) =>
        {
            try
            {
                MainForm? mainForm = GetOwnerMainForm();

                // 使所有尚在 Task.Run 延遲中的防抖播報失效，防止過時的數值／
                // 游標訊息在關閉播報之後才姍姍來遲地播出。
                Interlocked.Increment(ref _a11yDebounceId);

                UnsubscribeGamepadEvents();

                if (DialogResult == DialogResult.Cancel)
                {
                    // 直接在 UI 執行緒同步播報，避免 Task.Run 路徑在 Dispose 取消
                    // _cts 後因 OperationCanceledException 而遺失此關鍵訊息。
                    // 必須在還原主視窗 LiveSetting 之前播報，防止主視窗積壓訊息
                    // 在靜默解除後立即插播，與關閉訊息交錯。
                    _announcer?.Announce(Strings.A11y_Cancelled, false);
                }

                // 還原主視窗廣播器：在對話框自身播報完成後才解除靜默，
                // 確保積壓的主視窗訊息不會與關閉訊息交錯。
                mainForm?.SetA11yLiveSetting(AutomationLiveSetting.Polite);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[NumericInputDialog] FormClosing 處理失敗：{ex.Message}");
            }
        };
    }

    /// <summary>
    /// 建立符合眼動儀最佳實踐的按鈕
    /// </summary>
    /// <param name="text">按鈕顯示的文字</param>
    /// <param name="description">按鈕的輔助描述</param>
    /// <param name="scale">縮放比例</param>
    /// <param name="font">字型</param>
    /// <param name="onFocusStateChanged">焦點狀態變更的回呼函式</param>
    /// <returns>建立的按鈕</returns>
    private Button CreateEyeTrackerButton(
        string text,
        string description,
        float scale,
        Font font,
        Action<bool>? onFocusStateChanged = null)
    {
        // 取得專屬於此視窗 DPI 的 Bold 字體實例。
        Font boldFont = MainForm.GetSharedA11yFont(DeviceDpi, FontStyle.Bold, font.FontFamily);

        // 眼動儀友善：抗抖動寬度鎖定（Anti-Jitter Lock）。
        Size boldTextSize = TextRenderer.MeasureText(text, boldFont);

        int baseMinWidth = Math.Max((int)(120 * scale), boldTextSize.Width + (int)(32 * scale)),
            baseMinHeight = Math.Max((int)(60 * scale), boldTextSize.Height + (int)(24 * scale));

        Button btn = new()
        {
            Text = text,
            AccessibleName = text,
            AccessibleDescription = description,
            AccessibleRole = AccessibleRole.PushButton,
            Font = font,
            AutoSize = true,
            MinimumSize = new Size(baseMinWidth, baseMinHeight),
            Margin = new Padding((int)(12 * scale)),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.Empty,
            ForeColor = Color.Empty
        };

        // 消除 WinForms 原生 AcceptButton 產生的粗邊框。
        btn.FlatAppearance.BorderSize = 0;

        // 套用全套眼動儀回饋與按鈕繪製擴充。
        btn.AttachEyeTrackerFeedback(
            description,
            font,
            boldFont,
            _cts?.Token ?? CancellationToken.None,
            onFocusStateChanged);

        return btn;
    }

    /// <summary>
    /// 為按鈕附加「長按連發（Auto-Repeat）」功能，並完美避開原生 Click 的衝突
    /// </summary>
    /// <param name="btn">要附加連發功能的按鈕；為 null 時略過。</param>
    /// <param name="action">長按連發時要執行的動作。</param>
    private void AttachAutoRepeat(Button? btn, Action action)
    {
        if (btn == null)
        {
            return;
        }

        CancellationTokenSource? repeatCts = null;

        bool suppressNextClick = false;

        // 滑鼠按下時，啟動連發任務。
        btn.MouseDown += (s, e) =>
        {
            if (e.Button != MouseButtons.Left ||
                !btn.Enabled)
            {
                return;
            }

            suppressNextClick = false;

            repeatCts?.CancelAndDispose();
            repeatCts = _cts.TryCreateLinkedTokenSource();

            if (repeatCts == null)
            {
                return;
            }

            CancellationToken token = repeatCts.Token;

            // 取得目前連發設定（對齊控制器標準）。
            AppSettings.GamepadConfigSnapshot config = AppSettings.Current.GamepadSettings;

            int initialDelayMs = (int)(config.RepeatInitialDelayFrames * AppSettings.TargetFrameTimeMs),
                intervalMs = (int)(config.RepeatIntervalFrames * AppSettings.TargetFrameTimeMs);

            // 啟動連發背景任務。
            Task.Run(async () =>
            {
                try
                {
                    // 初始防呆延遲。
                    await Task.Delay(initialDelayMs, token);

                    this.SafeInvoke(() => suppressNextClick = true);

                    // 連發頻率：對齊目標基準。
                    using PeriodicTimer timer = new(TimeSpan.FromMilliseconds(intervalMs));

                    while (await timer.WaitForNextTickAsync(token))
                    {
                        this.SafeInvoke(action);
                    }
                }
                catch (OperationCanceledException)
                {

                }
            },
            token)
            .SafeFireAndForget();
        };

        // 任何中斷動作都會停止連發。
        void StopRepeat()
        {
            repeatCts?.CancelAndDispose();
            repeatCts = null;
        }

        btn.MouseUp += (s, e) => StopRepeat();
        btn.MouseLeave += (s, e) => StopRepeat();
        btn.LostFocus += (s, e) => StopRepeat();

        // 接管 Click 事件：執行動作，或過濾掉長按鬆開時產生的多餘點擊。
        btn.Click += (s, e) =>
        {
            if (suppressNextClick)
            {
                suppressNextClick = false;

                return;
            }

            action();
        };

        btn.Disposed += (s, e) => StopRepeat();
    }
}
