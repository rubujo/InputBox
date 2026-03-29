using InputBox.Core.Configuration;
using InputBox.Core.Extensions;
using InputBox.Core.Feedback;
using InputBox.Core.Input;
using InputBox.Core.Interop;
using InputBox.Core.Services;
using InputBox.Resources;
using Microsoft.Win32;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Media;
using System.Runtime.CompilerServices;

namespace InputBox.Core.Controls;

/// <summary>
/// 片語編輯對話框（新增／編輯單一片語）
/// </summary>
internal sealed class PhraseEditDialog : Form
{
    private readonly TextBox _txtName;
    private readonly TextBox _txtContent;
    private readonly Button _btnOk;
    private readonly Button _btnCancel;
    private readonly AnnouncerLabel _announcer;

    private CancellationTokenSource? _cts = new();
    private long _a11yDebounceId;
    private IGamepadController? _gamepadController;
    private Font? _a11yFont;
    private Font? _boldFont;

    /// <summary>
    /// 按鈕視覺狀態追蹤
    /// </summary>
    private sealed class ButtonVisualState
    {
        public long AnimId;
        public float DwellProgress;
        public bool IsHovered;
        public bool IsPressed;
        public string BaseDescription = string.Empty;
    }

    private readonly Dictionary<Button, ButtonVisualState> _btnStates = [];

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
                _gamepadController.APressed += HandleGamepadA;
                _gamepadController.StartPressed += HandleOpenTouchKeyboardFromGamepad;
                _gamepadController.BPressed += HandleBackOrClear;
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
                _gamepadController.LeftTriggerPressed += HandleLTNav;
                _gamepadController.RightTriggerPressed += HandleRTNav;
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
            Dock = DockStyle.Bottom,
            Height = 0
        };
        Controls.Add(_announcer);

        TableLayoutPanel tlp = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 4,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(0)
        };
        tlp.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        tlp.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
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
        _txtName = new TextBox()
        {
            Text = name,
            Dock = DockStyle.Fill,
            MaxLength = 50,
            BorderStyle = BorderStyle.None,
            BackColor = Color.Empty,
            ForeColor = Color.Empty,
            ImeMode = ImeMode.On,
            Font = new Font(Font.FontFamily, 28f, FontStyle.Regular, GraphicsUnit.Point),
            AccessibleName = Strings.Phrase_Edit_Name,
            AccessibleDescription = Strings.Phrase_A11y_Edit_Name_Desc,
            TabIndex = 0,
            Margin = new Padding(0, 4, 0, 4)
        };
        _txtName.PlaceholderText = GetPhraseTextOrFallback("Phrase_Edit_Name_Placeholder", Strings.Phrase_Edit_Name);
        _txtName.Enter += HandleInputBoxEnter;
        _txtName.Leave += HandleInputBoxLeave;
        tlp.Controls.Add(_txtName, 1, 0);

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
        tlp.Controls.Add(lblContent, 0, 1);

        // 內容輸入（多行）。
        _txtContent = new TextBox()
        {
            Text = content,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            Height = 140,
            Dock = DockStyle.Fill,
            MaxLength = 500,
            BorderStyle = BorderStyle.None,
            BackColor = Color.Empty,
            ForeColor = Color.Empty,
            ImeMode = ImeMode.On,
            Font = new Font(Font.FontFamily, 28f, FontStyle.Regular, GraphicsUnit.Point),
            AccessibleName = Strings.Phrase_Edit_Content,
            AccessibleDescription = Strings.Phrase_A11y_Edit_Content_Desc,
            AcceptsReturn = true,
            TabIndex = 1,
            Margin = new Padding(0, 4, 0, 4)
        };
        _txtContent.PlaceholderText = GetPhraseTextOrFallback("Phrase_Edit_Content_Placeholder", Strings.Phrase_Edit_Content);
        _txtContent.Enter += HandleInputBoxEnter;
        _txtContent.Leave += HandleInputBoxLeave;
        tlp.Controls.Add(_txtContent, 1, 1);

        // 按鈕區（Grouping）：與其他對話框一致，提供可導覽的群組語意。
        FlowLayoutPanel flpBtns = new()
        {
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
            WrapContents = false,
            Margin = new Padding(0),
            AccessibleName = Strings.Phrase_A11y_ButtonArea,
            AccessibleDescription = Strings.Phrase_A11y_ButtonArea_Desc,
            AccessibleRole = AccessibleRole.Grouping
        };

        _btnCancel = new Button()
        {
            Text = ControlExtensions.GetMnemonicText(Strings.Phrase_Btn_Cancel, 'B'),
            AutoSize = true,
            FlatStyle = FlatStyle.Flat,
            DialogResult = DialogResult.Cancel,
            BackColor = Color.Empty,
            ForeColor = Color.Empty,
            AccessibleName = Strings.Phrase_Btn_Cancel,
            AccessibleDescription = Strings.Phrase_A11y_Btn_Cancel_Desc,
            AccessibleRole = AccessibleRole.PushButton,
            TabIndex = 3,
            Margin = new Padding(8, 0, 0, 0)
        };
        _btnCancel.FlatAppearance.BorderSize = 0;
        _btnStates[_btnCancel] = new ButtonVisualState { BaseDescription = Strings.Phrase_A11y_Btn_Cancel_Desc };
        WireButtonEvents(_btnCancel);

        _btnOk = new Button()
        {
            Text = ControlExtensions.GetMnemonicText(Strings.Phrase_Btn_Confirm, 'A'),
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
            Margin = new Padding(8, 0, 0, 0)
        };
        _btnOk.FlatAppearance.BorderSize = 0;
        _btnStates[_btnOk] = new ButtonVisualState { BaseDescription = Strings.Phrase_A11y_Btn_Confirm_Desc };
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
        WireButtonEvents(_btnOk);

        flpBtns.Controls.Add(_btnOk);
        flpBtns.Controls.Add(_btnCancel);

        tlp.Controls.Add(flpBtns, 0, 3);
        tlp.SetColumnSpan(flpBtns, 2);

        AcceptButton = _btnOk;
        CancelButton = _btnCancel;

        Controls.Add(tlp);

        // 啟用/停用時控制器暫停/恢復。
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
                        try { GamepadController?.Resume(); }
                        catch (Exception ex) { Debug.WriteLine($"[片語編輯] Resume 失敗: {ex.Message}"); }
                    });
                }
                catch (OperationCanceledException) { }
                catch (Exception ex) { Debug.WriteLine($"[片語編輯] Activated 失敗: {ex.Message}"); }
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
                    try { GamepadController?.Pause(); }
                    catch (Exception ex) { Debug.WriteLine($"[片語編輯] Pause 失敗: {ex.Message}"); }
                });
            }
            catch (Exception ex) { Debug.WriteLine($"[片語編輯] Deactivate 失敗: {ex.Message}"); }
        };

        SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);

        // 從共享快取取得 Bold 字型。
        _boldFont = MainForm.GetSharedA11yFont(DeviceDpi, FontStyle.Bold);

        UpdateButtonMinimumSizes();

        float scale = DeviceDpi / AppSettings.BaseDpi;

        int minW = (int)(350 * scale);

        if (Width < minW)
        {
            Width = minW;
        }

        // 螢幕邊界修正。
        Rectangle workArea = Screen.GetWorkingArea(this);

        int x = Math.Max(workArea.Left, Math.Min(Left, workArea.Right - Width)),
            y = Math.Max(workArea.Top, Math.Min(Top, workArea.Bottom - Height));

        if (x != Left || y != Top)
        {
            Location = new Point(x, y);
        }

        _txtName.Focus();
        _txtName.SelectAll();
        ApplyInputBoxStrongVisual(_txtName);
    }

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

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        try
        {
            SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;

            UnsubscribeGamepadEvents();

            Interlocked.Exchange(ref _cts, null)?.CancelAndDispose();

            _a11yFont = null;
            _boldFont = null;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[片語編輯] OnFormClosing 失敗：{ex.Message}");
        }

        base.OnFormClosing(e);
    }

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
                    _btnOk.Invalidate();
                    _btnCancel.Invalidate();

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
    /// A 鍵：按鈕 → PerformClick；輸入框為空時開啟觸控鍵盤；其餘情境走確認驗證。
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
        catch (Exception ex) { Debug.WriteLine($"[片語編輯] HandleGamepadA 失敗: {ex.Message}"); }
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
        catch (Exception ex) { Debug.WriteLine($"[片語編輯] HandleOpenTouchKeyboardFromGamepad 失敗: {ex.Message}"); }
    });

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
        catch (Exception ex) { Debug.WriteLine($"[片語編輯] Confirm 失敗: {ex.Message}"); }
    });

    private void HandleCancel() => this.SafeInvoke(() =>
    {
        try
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }
        catch (Exception ex) { Debug.WriteLine($"[片語編輯] Cancel 失敗: {ex.Message}"); }
    });

    /// <summary>
    /// B 鍵：若焦點在輸入框且有內容則優先清空；否則執行取消。
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
                    catch (Exception ex) { Debug.WriteLine($"[片語編輯] 觸控鍵盤開啟失敗: {ex.Message}"); }
                });
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Debug.WriteLine($"[片語編輯] ShowTouchKeyboard 失敗: {ex.Message}"); }
        },
        _cts?.Token ?? CancellationToken.None)
        .SafeFireAndForget();
    }

    /// <summary>
    /// 取得目前焦點所在的 TextBox（名稱或內容）
    /// </summary>
    private TextBox? GetActiveTextBox()
    {
        if (_txtName.Focused) return _txtName;
        if (_txtContent.Focused) return _txtContent;
        return null;
    }

    private void HandleInputBoxEnter(object? sender, EventArgs e)
    {
        try
        {
            if (sender is TextBox tb)
            {
                ApplyInputBoxStrongVisual(tb);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[片語編輯] HandleInputBoxEnter 失敗：{ex.Message}");
        }
    }

    private void HandleInputBoxLeave(object? sender, EventArgs e)
    {
        try
        {
            if (sender is TextBox tb)
            {
                ResetInputBoxVisual(tb);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[片語編輯] HandleInputBoxLeave 失敗：{ex.Message}");
        }
    }

    /// <summary>
    /// 套用與主輸入框一致的強視覺焦點樣式（高對比優先，其次主題感知反轉）。
    /// </summary>
    private void ApplyInputBoxStrongVisual(TextBox tb)
    {
        if (tb.IsDisposed)
        {
            return;
        }

        if (SystemInformation.HighContrast)
        {
            tb.BackColor = SystemColors.Highlight;
            tb.ForeColor = SystemColors.HighlightText;
            return;
        }

        if (tb.IsDarkModeActive())
        {
            // 深色模式：反轉為白底黑字。
            tb.BackColor = Color.White;
            tb.ForeColor = Color.Black;
        }
        else
        {
            // 淺色模式：反轉為黑底白字。
            tb.BackColor = Color.Black;
            tb.ForeColor = Color.White;
        }
    }

    private static void ResetInputBoxVisual(TextBox tb)
    {
        if (tb.IsDisposed)
        {
            return;
        }

        tb.BackColor = Color.Empty;
        tb.ForeColor = Color.Empty;
    }

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

    private async Task FlashValidationCueAsync(TextBox target)
    {
        try
        {
            CancellationToken token = _cts?.Token ?? CancellationToken.None;

            for (int i = 0; i < 2; i++)
            {
                await this.SafeInvokeAsync(() =>
                {
                    if (target.IsDisposed)
                    {
                        return;
                    }

                    if (SystemInformation.HighContrast)
                    {
                        target.BackColor = SystemColors.Highlight;
                        target.ForeColor = SystemColors.HighlightText;
                        return;
                    }

                    bool isDark = target.IsDarkModeActive();

                    target.BackColor = isDark ?
                        Color.Firebrick :
                        Color.DarkOrange;
                    target.ForeColor = isDark ?
                        Color.White :
                        Color.Black;
                });

                await Task.Delay(120, token);

                await this.SafeInvokeAsync(() =>
                {
                    if (target.IsDisposed)
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
                });

                await Task.Delay(120, token);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Debug.WriteLine($"[片語編輯] FlashValidationCueAsync 失敗：{ex.Message}");
        }
    }

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
            if (tb == null) return;

            bool hasSelection = tb.SelectionLength > 0;

            if (hasSelection || tb.SelectionStart > 0)
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
        catch (Exception ex) { Debug.WriteLine($"[片語編輯] HandleLeft 失敗: {ex.Message}"); }
    });

    /// <summary>
    /// 游標右移（比照 MainForm.Gamepad.cs 的 MoveCursorRight）
    /// </summary>
    private void HandleRight() => this.SafeInvoke(() =>
    {
        try
        {
            TextBox? tb = GetActiveTextBox();
            if (tb == null) return;

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
        catch (Exception ex) { Debug.WriteLine($"[片語編輯] HandleRight 失敗: {ex.Message}"); }
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
        catch (Exception ex) { Debug.WriteLine($"[片語編輯] HandleFieldPrev 失敗: {ex.Message}"); }
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
        catch (Exception ex) { Debug.WriteLine($"[片語編輯] HandleFieldNext 失敗: {ex.Message}"); }
    });

    /// <summary>
    /// X 鍵：刪除選取文字或游標前一字元（比照 MainForm.Gamepad.cs 的 X 鍵刪除邏輯）
    /// </summary>
    private void HandleBackspace() => this.SafeInvoke(() =>
    {
        try
        {
            TextBox? tb = GetActiveTextBox();
            if (tb == null || tb.ReadOnly) return;

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
        catch (Exception ex) { Debug.WriteLine($"[片語編輯] HandleBackspace 失敗: {ex.Message}"); }
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
        catch (Exception ex) { Debug.WriteLine($"[片語編輯] HandleRSLeft 失敗: {ex.Message}"); }
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
        catch (Exception ex) { Debug.WriteLine($"[片語編輯] HandleRSRight 失敗: {ex.Message}"); }
    });

    /// <summary>
    /// Y 鍵：開啟焦點 TextBox 的原生右鍵選單
    /// </summary>
    private void HandleOpenContextMenu() => this.SafeInvoke(() =>
    {
        try
        {
            TextBox? tb = GetActiveTextBox();
            if (tb == null) return;

            // 在游標位置附近顯示內建右鍵選單。
            Point caretPos = tb.GetPositionFromCharIndex(tb.SelectionStart);
            tb.ContextMenuStrip?.Show(tb, caretPos);

            // TextBox 沒有 ContextMenuStrip 時，透過模擬 Shift+F10 觸發原生選單。
            if (tb.ContextMenuStrip == null)
            {
                User32.SendMessage(tb.Handle, 0x007B, tb.Handle, unchecked((nint)0xFFFFFFFF));
            }
        }
        catch (Exception ex) { Debug.WriteLine($"[片語編輯] HandleOpenContextMenu 失敗: {ex.Message}"); }
    });

    /// <summary>
    /// LT：在按鈕之間向左導覽（OK ← Cancel），若在 TextBox 則跳至按鈕區
    /// </summary>
    private void HandleLTNav() => this.SafeInvoke(() =>
    {
        try
        {
            if (_btnCancel.Focused)
            {
                _btnOk.Focus();
            }
            else
            {
                _btnOk.Focus();
            }

            FeedbackService.VibrateAsync(
                _gamepadController,
                VibrationPatterns.CursorMove,
                _cts?.Token ?? CancellationToken.None)
                .SafeFireAndForget();
        }
        catch (Exception ex) { Debug.WriteLine($"[片語編輯] HandleLTNav 失敗: {ex.Message}"); }
    });

    /// <summary>
    /// RT：在按鈕之間向右導覽（OK → Cancel），若在 TextBox 則跳至按鈕區
    /// </summary>
    private void HandleRTNav() => this.SafeInvoke(() =>
    {
        try
        {
            if (_btnOk.Focused)
            {
                _btnCancel.Focus();
            }
            else
            {
                _btnCancel.Focus();
            }

            FeedbackService.VibrateAsync(
                _gamepadController,
                VibrationPatterns.CursorMove,
                _cts?.Token ?? CancellationToken.None)
                .SafeFireAndForget();
        }
        catch (Exception ex) { Debug.WriteLine($"[片語編輯] HandleRTNav 失敗: {ex.Message}"); }
    });

    /// <summary>
    /// 擴張或縮減文字選取範圍（比照 MainForm.Gamepad.cs 的 ExpandSelection）
    /// </summary>
    private void ExpandSelection(int direction)
    {
        TextBox? tb = GetActiveTextBox();
        if (tb == null) return;

        if (tb.SelectionLength == 0 ||
            _rsSelectionAnchor == null ||
            (tb.SelectionStart != _rsSelectionAnchor.Value &&
             tb.SelectionStart + tb.SelectionLength != _rsSelectionAnchor.Value))
        {
            _rsSelectionAnchor = tb.SelectionStart;
        }

        int anchor = _rsSelectionAnchor.Value;

        int caret = (tb.SelectionStart == anchor)
            ? (anchor + tb.SelectionLength)
            : tb.SelectionStart;

        int safeDirection = Math.Sign(direction);
        int newCaret = Math.Clamp(caret + safeDirection, 0, tb.TextLength);

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
            Debug.WriteLine($"[片語編輯] 控制器連線變更: {ex.Message}");
        }
    }

    private void UnsubscribeGamepadEvents()
    {
        try
        {
            if (_gamepadController != null)
            {
                _gamepadController.APressed -= HandleGamepadA;
                _gamepadController.StartPressed -= HandleOpenTouchKeyboardFromGamepad;
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
                _gamepadController.LeftTriggerPressed -= HandleLTNav;
                _gamepadController.RightTriggerPressed -= HandleRTNav;
                _gamepadController.ConnectionChanged -= HandleConnectionChanged;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[片語編輯] UnsubscribeGamepadEvents 失敗：{ex.Message}");
        }
    }

    /// <summary>
    /// 為按鈕綁定完整 A11y 事件（Paint、Focus、Hover、Pressed）
    /// </summary>
    private void WireButtonEvents(Button btn)
    {
        btn.Paint += Btn_Paint;

        btn.GotFocus += (s, e) => ApplyStrongVisual(btn);

        btn.LostFocus += (s, e) =>
        {
            if (_btnStates.TryGetValue(btn, out ButtonVisualState? st)) st.IsPressed = false;
            StopFeedback(btn);
        };

        btn.MouseEnter += (s, e) =>
        {
            if (ActiveForm != this) return;
            if (_btnStates.TryGetValue(btn, out ButtonVisualState? st)) st.IsHovered = true;
            StartAnimationFeedback(btn);
        };

        btn.MouseLeave += (s, e) =>
        {
            if (_btnStates.TryGetValue(btn, out ButtonVisualState? st))
            {
                st.IsHovered = false;
                st.IsPressed = false;
            }
            StopFeedback(btn);
        };

        btn.MouseDown += (s, e) =>
        {
            if (e.Button == MouseButtons.Left && _btnStates.TryGetValue(btn, out ButtonVisualState? st))
            {
                st.IsPressed = true;
                StartAnimationFeedback(btn);
            }
        };

        btn.MouseUp += (s, e) =>
        {
            if (e.Button == MouseButtons.Left && _btnStates.TryGetValue(btn, out ButtonVisualState? st))
            {
                st.IsPressed = false;
                StartAnimationFeedback(btn);
            }
        };
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void UpdateSingleButtonMinimumSize(Button btn, float scale)
    {
        try
        {
            if (btn.IsDisposed) return;

            btn.MinimumSize = Size.Empty;

            Font boldFont = _boldFont ?? MainForm.GetSharedA11yFont(DeviceDpi, FontStyle.Bold);
            Size boldTextSize = TextRenderer.MeasureText(btn.Text, boldFont);

            int wcagMin = (int)(44 * scale);

            int minW = Math.Max(wcagMin, boldTextSize.Width + (int)(24 * scale)),
                minH = Math.Max(wcagMin, boldTextSize.Height + (int)(16 * scale));

            btn.MinimumSize = new Size(minW, minH);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[片語編輯] UpdateSingleButtonMinimumSize 失敗：{ex.Message}");
        }
    }

    /// <summary>
    /// 套用焦點反轉強視覺（Bold + 反轉色 + Pressed 琥珀色）
    /// </summary>
    private void ApplyStrongVisual(Button btn)
    {
        if (!_btnStates.TryGetValue(btn, out ButtonVisualState? st)) return;

        Interlocked.Increment(ref st.AnimId);
        st.DwellProgress = 0f;

        if (SystemInformation.HighContrast)
        {
            btn.BackColor = SystemColors.Highlight;
            btn.ForeColor = SystemColors.HighlightText;
        }
        else
        {
            bool isDark = this.IsDarkModeActive();

            if (st.IsPressed)
            {
                btn.BackColor = isDark ?
                    Color.FromArgb(255, 200, 120) :
                    Color.FromArgb(28, 28, 28);
                btn.ForeColor = isDark ?
                    Color.Black :
                    Color.White;
            }
            else
            {
                btn.BackColor = isDark ? Color.White : Color.Black;
                btn.ForeColor = isDark ? Color.Black : Color.White;
            }
        }

        Font? bold = _boldFont;
        if (bold != null && !ReferenceEquals(btn.Font, bold))
        {
            btn.Font = bold;
        }

        btn.AccessibleDescription = st.IsPressed ?
            $"{st.BaseDescription} ({Strings.A11y_State_Pressed})" :
            $"{st.BaseDescription} ({Strings.A11y_State_Focused})";

        btn.Invalidate();
    }

    /// <summary>
    /// 啟動 Dwell 動畫回饋
    /// </summary>
    private void StartAnimationFeedback(Button btn)
    {
        if (btn.IsDisposed || !btn.Enabled) return;
        if (!_btnStates.TryGetValue(btn, out ButtonVisualState? st)) return;

        if (st.IsPressed || (btn.Focused && !st.IsHovered))
        {
            ApplyStrongVisual(btn);
            return;
        }

        long currentId = Interlocked.Increment(ref st.AnimId);
        st.DwellProgress = 0f;

        if (SystemInformation.HighContrast)
        {
            btn.BackColor = SystemColors.HotTrack;
            btn.ForeColor = SystemColors.HighlightText;
        }
        else
        {
            bool isDark = this.IsDarkModeActive();

            btn.BackColor = isDark ?
                Color.FromArgb(60, 60, 60) :
                Color.FromArgb(220, 220, 220);
            btn.ForeColor = isDark ?
                Color.White :
                Color.Black;
        }

        Font? regular = _a11yFont;
        if (regular != null && !ReferenceEquals(btn.Font, regular))
        {
            btn.Font = regular;
        }

        btn.AccessibleDescription = $"{st.BaseDescription} ({Strings.A11y_State_Hover})";

        btn.Invalidate();

        CancellationToken ct = _cts?.Token ?? CancellationToken.None;

        btn.RunDwellAnimationAsync(
            id: currentId,
            animationIdGetter: () => Interlocked.Read(ref st.AnimId),
            progressSetter: p => st.DwellProgress = p,
            durationMs: 1000,
            ct: ct
        ).SafeFireAndForget();
    }

    /// <summary>
    /// 停止回饋
    /// </summary>
    private void StopFeedback(Button btn)
    {
        if (!_btnStates.TryGetValue(btn, out ButtonVisualState? st)) return;

        Interlocked.Increment(ref st.AnimId);
        st.DwellProgress = 0f;

        if (btn.Focused)
        {
            ApplyStrongVisual(btn);
            return;
        }

        if (st.IsHovered)
        {
            StartAnimationFeedback(btn);
            return;
        }

        btn.BackColor = Color.Empty;
        btn.ForeColor = Color.Empty;

        Font? regular = _a11yFont;
        if (regular != null && !ReferenceEquals(btn.Font, regular))
        {
            btn.Font = regular;
        }

        btn.AccessibleDescription = st.BaseDescription;

        btn.Invalidate();
    }

    /// <summary>
    /// 自訂按鈕繪製：基礎邊框 → 焦點／懸停邊框 → Pressed 內緣框 → Dwell 進度條
    /// </summary>
    private void Btn_Paint(object? sender, PaintEventArgs e)
    {
        if (sender is not Button btn) return;

        try
        {
            _btnStates.TryGetValue(btn, out ButtonVisualState? st);

            Graphics g = e.Graphics;
            float scale = btn.DeviceDpi / AppSettings.BaseDpi;
            bool isDark = btn.IsDarkModeActive(),
                 isFocused = btn.Focused,
                 isHoveredOrDwell = (st?.IsHovered ?? false) || (st?.DwellProgress ?? 0f) > 0f;

            // 停用態：統一使用共用非色彩提示（虛線邊框 + 斜線）。
            if (btn.TryDrawDisabledButtonCue(g, isDark, scale))
            {
                return;
            }

            // isDefault：AcceptButton 在焦點位於非按鈕控制項時顯示焦點色邊框。
            bool isDefault = ReferenceEquals(AcceptButton, btn) &&
                ActiveControl is not Button &&
                btn.Enabled;

            // 第一層：基礎邊框。
            if (!isFocused && !isHoveredOrDwell && !isDefault)
            {
                btn.DrawButtonBaseBorder(g, isDark, scale);
            }

            // 第二層：焦點／懸停邊框。
            bool isStrongVisual = (st?.IsPressed ?? false) ||
                (isFocused && !(st?.IsHovered ?? false));

            if (isFocused || isHoveredOrDwell || isDefault)
            {
                int borderThickness;
                int inset;

                Color borderColor = btn.GetButtonInteractiveBorderColor(isStrongVisual, isDark);

                btn.DrawButtonInteractiveBorder(
                    g,
                    borderColor,
                    scale,
                    out inset,
                    out borderThickness);

                // Pressed 內緣框。
                if (!SystemInformation.HighContrast && (st?.IsPressed ?? false))
                {
                    btn.DrawPressedInnerCue(g, scale, inset, borderThickness);
                }
            }

            // 第三層：Dwell 進度條。
            float progress = st?.DwellProgress ?? 0f;

            if (progress > 0f && !(st?.IsPressed ?? false))
            {
                int barH = (int)(6 * scale),
                    barW = (int)(btn.Width * progress);

                if (barW > 0)
                {
                    Rectangle barRect = new(0, btn.Height - barH, barW, barH);

                    if (SystemInformation.HighContrast)
                    {
                        using Brush barBrush = new SolidBrush(SystemColors.HighlightText);
                        g.FillRectangle(barBrush, barRect);
                    }
                    else
                    {
                        Color baseColor = isDark ? Color.LimeGreen : Color.Green,
                              hatchColor = isDark ? Color.DarkGreen : Color.PaleGreen;

                        using Brush bgBrush = new SolidBrush(baseColor);
                        using Brush hatchBrush = new HatchBrush(
                            HatchStyle.BackwardDiagonal,
                            hatchColor,
                            Color.Transparent);

                        g.FillRectangle(bgBrush, barRect);
                        g.FillRectangle(hatchBrush, barRect);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[片語編輯] Button Paint 失敗：{ex.Message}");
        }
    }

    private void AnnounceA11y(string message, bool interrupt = false)
    {
        if (IsDisposed || string.IsNullOrEmpty(message))
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

        // 本地備援。
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
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Debug.WriteLine($"[片語編輯] A11y 廣播失敗：{ex.Message}");
            }
        },
        _cts?.Token ?? CancellationToken.None)
        .SafeFireAndForget();
    }
}
