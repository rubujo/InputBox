using InputBox.Core.Extensions;
using InputBox.Core.Feedback;
using InputBox.Core.Input;
using InputBox.Core.Services;
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
internal sealed class NumericInputDialog : Form
{
    /// <summary>
    /// 繼承自 NumericUpDown 以公開受保護的成員方法
    /// </summary>
    private sealed class AccessibleNumericUpDown : NumericUpDown
    {
        /// <summary>
        /// 主動觸發無障礙狀態變更通知
        /// </summary>
        public void NotifyAccessibilityChange()
        {
            // 主動通知輔助科技（AT）數值已變更。
            AccessibilityNotifyClients(AccessibleEvents.ValueChange, -1);
        }

        /// <summary>
        /// 強制驗證編輯文字，確保 Value 屬性與目前輸入內容同步
        /// </summary>
        public void ValidateValue()
        {
            ValidateEditText();
        }
    }

    /// <summary>
    /// 預設數值
    /// </summary>
    private readonly decimal _defaultValue;

    /// <summary>
    /// AccessibleNumericUpDown 實例
    /// </summary>
    private readonly AccessibleNumericUpDown _nud;

    /// <summary>
    /// 用於 A11y 廣播的 Label
    /// </summary>
    private readonly AnnouncerLabel _announcer;

    /// <summary>
    /// 統一放大的 A11y 字型
    /// </summary>
    private readonly Font _a11yFont;

    /// <summary>
    /// 用於管理對話框生命週期內非同步任務的取消權杖來源
    /// </summary>
    private readonly CancellationTokenSource _cts = new();

    /// <summary>
    /// 遊戲手把控制器
    /// </summary>
    private IGamepadController? _gamepadController;

    /// <summary>
    /// 取得目前數值
    /// </summary>
    public decimal Value => _nud.Value;

    /// <summary>
    /// 設定控制器實作，並訂閱事件
    /// </summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public IGamepadController? GamepadController
    {
        get => _gamepadController;
        set
        {
            // 如果控制器相同，不執行任何操作。
            if (ReferenceEquals(_gamepadController, value))
            {
                return;
            }

            // 安全清理舊控制器的訂閱。
            UnsubscribeGamepadEvents();

            _gamepadController = value;

            if (_gamepadController != null)
            {
                // 訂閱新控制器事件。
                _gamepadController.UpPressed += HandlePlus;
                _gamepadController.UpRepeat += HandlePlus;
                _gamepadController.DownPressed += HandleMinus;
                _gamepadController.DownRepeat += HandleMinus;
                _gamepadController.APressed += HandleConfirm;
                _gamepadController.StartPressed += HandleConfirm;
                _gamepadController.BPressed += HandleCancel;
                _gamepadController.XPressed += HandleReset;
                _gamepadController.ConnectionChanged += HandleGamepadConnectionChanged;
            }
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);

        UpdateMinimumSize();

        SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);

        UpdateMinimumSize();
    }

    private void SystemEvents_UserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category == UserPreferenceCategory.Accessibility ||
            e.Category == UserPreferenceCategory.Color)
        {
            this.SafeInvoke(() =>
            {
                UpdateMinimumSize();

                UpdateFocusVisuals(_nud.Focused || _nud.ContainsFocus);
            });
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;

            _cts.Cancel();
            _cts.Dispose();
            _a11yFont?.Dispose();
            _announcer?.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <summary>
    /// 取消訂閱手把事件
    /// </summary>
    private void UnsubscribeGamepadEvents()
    {
        if (_gamepadController != null)
        {
            _gamepadController.UpPressed -= HandlePlus;
            _gamepadController.UpRepeat -= HandlePlus;
            _gamepadController.DownPressed -= HandleMinus;
            _gamepadController.DownRepeat -= HandleMinus;
            _gamepadController.APressed -= HandleConfirm;
            _gamepadController.StartPressed -= HandleConfirm;
            _gamepadController.BPressed -= HandleCancel;
            _gamepadController.XPressed -= HandleReset;
            _gamepadController.ConnectionChanged -= HandleGamepadConnectionChanged;
        }
    }

    /// <summary>
    /// 處理手把連線狀態變更
    /// </summary>
    private void HandleGamepadConnectionChanged(bool connected)
    {
        if (connected)
        {
            _gamepadController?.Resume();
        }
    }

    /// <summary>
    /// 處理數值增加，並將焦點移回數值框以確保視覺一致性
    /// </summary>
    private void HandlePlus() => this.SafeInvoke(() =>
    {
        _nud.UpButton();
        _nud.Focus();
    });

    /// <summary>
    /// 處理數值減少，並將焦點移回數值框以確保視覺一致性
    /// </summary>
    private void HandleMinus() => this.SafeInvoke(() =>
    {
        _nud.DownButton();
        _nud.Focus();
    });

    /// <summary>
    /// 處理確認按鍵事件
    /// </summary>
    private void HandleConfirm() => this.SafeInvoke(() =>
    {
        _nud.ValidateValue();

        DialogResult = DialogResult.OK;

        Close();
    });

    /// <summary>
    /// 處理取消按鍵事件
    /// </summary>
    private void HandleCancel() => this.SafeInvoke(() =>
    {
        DialogResult = DialogResult.Cancel;

        Close();
    });

    /// <summary>
    /// 處理重設按鍵事件
    /// </summary>
    private void HandleReset() => this.SafeInvoke(() =>
    {
        _nud.Value = Math.Clamp(_defaultValue, _nud.Minimum, _nud.Maximum);
        _nud.Focus();

        FeedbackService.PlaySound(SystemSounds.Asterisk);

        FeedbackService.VibrateAsync(_gamepadController, VibrationPatterns.CursorMove).SafeFireAndForget();
    });

    /// <summary>
    /// 內部 A11y 廣播方法
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
        else
        {
            // 在獨立對話框模式下，我們手動加入 200ms 的音訊避讓延遲。
            Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(200, _cts.Token);

                    this.SafeInvoke(() =>
                    {
                        if (!IsDisposed)
                        {
                            _announcer.Announce(message);
                        }
                    });
                }
                catch (OperationCanceledException)
                {
                    // 正常取消。
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[A11y] 對話框本地廣播失敗：{ex.Message}");
                }
            },
            _cts.Token)
            .SafeFireAndForget();
        }
    }

    /// <summary>
    /// 更新控制項焦點狀態的視覺表現
    /// </summary>
    /// <param name="isFocused">指示控制項是否具有焦點</param>
    private void UpdateFocusVisuals(bool isFocused)
    {
        if (_nud == null ||
            _nud.IsDisposed)
        {
            return;
        }

        if (isFocused)
        {
            if (SystemInformation.HighContrast)
            {
                _nud.BackColor = SystemColors.Highlight;
                _nud.ForeColor = SystemColors.HighlightText;
            }
            else
            {
                _nud.BackColor = Color.Black;
                _nud.ForeColor = Color.White;
            }
        }
        else
        {
            _nud.BackColor = SystemColors.Window;
            _nud.ForeColor = SystemColors.WindowText;
        }
    }

    /// <summary>
    /// 更新視窗最小尺寸
    /// </summary>
    private void UpdateMinimumSize()
    {
        float scale = DeviceDpi / 96.0f;

        MinimumSize = new Size((int)(450 * scale), (int)(250 * scale));
    }

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

        string promptText = string.Format(Strings.Msg_EnterValue, title);

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

        // 建立 A11y 廣播器（作為備援）。
        _announcer = new AnnouncerLabel();

        // 繼承圖示：優先從主視窗繼承，保持應用程式視覺識別的一致性。
        Icon = Application.OpenForms.OfType<MainForm>().FirstOrDefault()?.Icon ??
            ActiveForm?.Icon;

        // 根據 DPI 縮放比例計算佈局參數。
        float scale = DeviceDpi / 96.0f;

        _a11yFont = MainForm.GetSharedA11yFont(DeviceDpi, FontStyle.Bold);

        // 主佈局容器。
        TableLayoutPanel tlpMain = new()
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding((int)(30 * scale)),
            // 設定為 Grouping 角色，協助輔助科技識別這是一個邏輯區塊。
            AccessibleRole = AccessibleRole.Grouping
        };

        Label lblPrompt = new()
        {
            Text = string.Format(Strings.Msg_EnterValue, title),
            AutoSize = true,
            MaximumSize = new Size((int)(500 * scale), 0),
            Margin = new Padding(0, 0, 0, (int)(25 * scale)),
            Font = _a11yFont,
            AccessibleRole = AccessibleRole.StaticText
        };

        // 3x2 網格連動區。
        TableLayoutPanel tlpGrid = new()
        {
            AutoSize = true,
            ColumnCount = 3,
            RowCount = 2,
            AccessibleRole = AccessibleRole.Grouping
        };
        tlpGrid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        tlpGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        tlpGrid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        // 核心控制項建立。

        _nud = new AccessibleNumericUpDown
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
            Font = new Font(_a11yFont.FontFamily, _a11yFont.Size * 2.0f, FontStyle.Bold),
            BackColor = SystemColors.Window,
            ForeColor = SystemColors.WindowText,
            Margin = new Padding((int)(15 * scale), (int)(12 * scale), (int)(15 * scale), (int)(12 * scale)),
            AccessibleName = promptText,
            // A11y 描述：報讀目前值與有效範圍。
            AccessibleDescription = string.Format(Strings.A11y_Value_Range_Desc, currentValue, minimum, maximum),
            TabIndex = 1,
            Anchor = AnchorStyles.None
        };

        _nud.Enter += (s, e) => UpdateFocusVisuals(true);
        _nud.Leave += (s, e) => UpdateFocusVisuals(false);

        // 當數值改變時，同步更新無障礙描述，並主動廣播最新數值。
        _nud.ValueChanged += (s, e) =>
        {
            _nud.AccessibleDescription = string.Format(Strings.A11y_Value_Range_Desc, _nud.Value, _nud.Minimum, _nud.Maximum);

            // 主動廣播最新值，讓使用者在微調（如按上下鍵）時能立即獲得語音反饋。
            // 使用打斷模式（interrupt: true），避免在高頻率變動下產生語音隊列堆積。
            AnnounceA11y(string.Format(Strings.A11y_CurrentValue, _nud.Value), interrupt: true);

            FeedbackService.VibrateAsync(_gamepadController, VibrationPatterns.CursorMove).SafeFireAndForget();
        };

        // 按鈕連動 NUD 高亮邏輯。

        // 第 1 列：數值加減。
        Button btnMinus = CreateEyeTrackerButton(Strings.Btn_Minus, Strings.A11y_Btn_Minus_Desc, scale, _a11yFont, (active) => UpdateFocusVisuals(active || _nud.Focused || _nud.ContainsFocus));
        btnMinus.Click += (s, e) => HandleMinus();
        btnMinus.Anchor = AnchorStyles.None;
        btnMinus.TabIndex = 0;

        Button btnPlus = CreateEyeTrackerButton(Strings.Btn_Plus, Strings.A11y_Btn_Plus_Desc, scale, _a11yFont, (active) => UpdateFocusVisuals(active || _nud.Focused || _nud.ContainsFocus));
        btnPlus.Click += (s, e) => HandlePlus();
        btnPlus.Anchor = AnchorStyles.None;
        btnPlus.TabIndex = 2;

        // 第 2 列：操作按鈕。
        Button btnOk = CreateEyeTrackerButton(ControlExtensions.GetMnemonicText(Strings.Btn_OK, 'A'), Strings.A11y_Btn_OK_Desc, scale, _a11yFont);
        btnOk.DialogResult = DialogResult.OK;
        btnOk.Anchor = AnchorStyles.None;
        btnOk.TabIndex = 3;

        Button btnCancel = CreateEyeTrackerButton(ControlExtensions.GetMnemonicText(Strings.Btn_Cancel, 'B'), Strings.A11y_Btn_Cancel_Desc, scale, _a11yFont);
        btnCancel.DialogResult = DialogResult.Cancel;
        btnCancel.Anchor = AnchorStyles.None;
        btnCancel.TabIndex = 4;

        Button btnReset = CreateEyeTrackerButton(ControlExtensions.GetMnemonicText(Strings.Btn_SetDefault, 'X'), string.Format(Strings.A11y_Btn_SetDefault_Desc, _defaultValue), scale, _a11yFont);
        btnReset.Click += (s, e) => HandleReset();
        btnReset.Anchor = AnchorStyles.None;
        btnReset.TabIndex = 5;

        // 填充 3x2 網格。
        tlpGrid.Controls.Add(btnMinus, 0, 0);
        tlpGrid.Controls.Add(_nud, 1, 0);
        tlpGrid.Controls.Add(btnPlus, 2, 0);
        tlpGrid.Controls.Add(btnOk, 2, 1);
        tlpGrid.Controls.Add(btnCancel, 0, 1);
        tlpGrid.Controls.Add(btnReset, 1, 1);

        tlpMain.Controls.Add(lblPrompt, 0, 0);
        tlpMain.Controls.Add(tlpGrid, 0, 1);

        _announcer.Size = Size.Empty;
        Controls.Add(_announcer);
        Controls.Add(tlpMain);

        Shown += (s, e) =>
        {
            // 協調 LiveRegion：當彈出對話框時，暫時停用主視窗的廣播器 LiveSetting，防止訊息干擾。
            if (Owner is MainForm mainForm)
            {
                mainForm.SetA11yLiveSetting(AutomationLiveSetting.Off);
            }

            AnnounceA11y(lblPrompt.Text);

            _nud.Focus();

            UpdateFocusVisuals(true);

            this.SafeBeginInvoke(() => _nud.Select(0, _nud.Text.Length));
        };

        Activated += (s, e) =>
        {
            Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(50, _cts.Token);

                    this.SafeInvoke(() =>
                    {
                        if (!IsDisposed)
                        {
                            _gamepadController?.Resume();
                        }
                    });
                }
                catch
                {

                }
            },
            _cts.Token)
            .SafeFireAndForget();
        };

        Deactivate += (s, e) =>
        {
            this.SafeBeginInvoke(() =>
            {
                if (ActiveForm == null)
                {
                    _gamepadController?.Pause();
                }
            });
        };

        FormClosing += (s, e) =>
        {
            if (Owner is MainForm mainForm)
            {
                mainForm.SetA11yLiveSetting(AutomationLiveSetting.Polite);
            }

            UnsubscribeGamepadEvents();

            if (DialogResult == DialogResult.Cancel)
            {
                (Owner as MainForm)?.AnnounceA11y(Strings.A11y_Cancelled);
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
    private static Button CreateEyeTrackerButton(
        string text,
        string description,
        float scale,
        Font font,
        Action<bool>? onFocusStateChanged = null)
    {
        Font boldFont = new(font, FontStyle.Bold);

        Button btn = new()
        {
            Text = text,
            AccessibleName = text,
            AccessibleDescription = description,
            AccessibleRole = AccessibleRole.PushButton,
            Font = font,
            AutoSize = true,
            MinimumSize = new Size((int)(120 * scale), (int)(60 * scale)),
            Margin = new Padding((int)(12 * scale)),
            FlatStyle = FlatStyle.Flat,
            BackColor = SystemColors.Control,
            ForeColor = SystemColors.ControlText
        };

        btn.Disposed += (s, e) => boldFont.Dispose();

        Color originalBackColor = btn.BackColor;
        Color originalForeColor = btn.ForeColor;
        Padding originalPadding = btn.Padding;

        float dwellProgress = 0f;
        long animationId = 0;
        bool isHovered = false;

        btn.MouseEnter += (s, e) =>
        {
            isHovered = true;

            onFocusStateChanged?.Invoke(true);

            StartAnimationFeedback(false);
        };
        btn.MouseLeave += (s, e) =>
        {
            isHovered = false;

            onFocusStateChanged?.Invoke(btn.Focused);

            StopFeedback();
        };
        btn.GotFocus += (s, e) =>
        {
            onFocusStateChanged?.Invoke(true);

            StartAnimationFeedback(true);
        };
        btn.LostFocus += (s, e) =>
        {
            onFocusStateChanged?.Invoke(isHovered);

            StopFeedback();
        };

        // 點擊後重置進度，保留背景直到失焦。
        btn.Click += (s, e) =>
        {
            Interlocked.Increment(ref animationId);

            dwellProgress = 0f;

            btn.Invalidate();
        };

        void StartAnimationFeedback(bool isKeyboardFocus)
        {
            if (!btn.Enabled)
            {
                return;
            }

            if (SystemInformation.HighContrast)
            {
                btn.BackColor = SystemColors.Highlight;
                btn.ForeColor = SystemColors.HighlightText;
            }
            else
            {
                btn.BackColor = Color.Black;
                btn.ForeColor = Color.White;
            }

            btn.Font = boldFont;
            btn.Padding = new Padding(0);
            btn.AccessibleDescription = $"{description} ({Strings.A11y_State_Focused})";

            if (!isKeyboardFocus)
            {
                long id = Interlocked.Increment(ref animationId);

                RunAnimationAsync(id).SafeFireAndForget();
            }
        }

        void StopFeedback()
        {
            Interlocked.Increment(ref animationId);

            dwellProgress = 0f;

            // 狀態守衛：只有在確實失焦且無懸停時才還原顏色。
            if (!btn.Focused &&
                !isHovered)
            {
                if (SystemInformation.HighContrast)
                {
                    btn.BackColor = SystemColors.Control;
                    btn.ForeColor = SystemColors.ControlText;
                }
                else
                {
                    btn.BackColor = originalBackColor;
                    btn.ForeColor = originalForeColor;
                }

                btn.Font = font;
                btn.Padding = originalPadding;
                btn.AccessibleDescription = description;
            }

            btn.Invalidate();
        }

        async Task RunAnimationAsync(long id)
        {
            if (!SystemInformation.UIEffectsEnabled)
            {
                dwellProgress = 1.0f;

                btn.Invalidate();

                return;
            }

            Stopwatch sw = Stopwatch.StartNew();

            int duration = 1000;

            while (Interlocked.Read(ref animationId) == id && !btn.IsDisposed)
            {
                double elapsed = sw.Elapsed.TotalMilliseconds;

                dwellProgress = (float)Math.Min(1.0, elapsed / duration);

                btn.Invalidate();

                if (dwellProgress >= 1.0f)
                {
                    break;
                }

                await Task.Delay(16);
            }
        }

        btn.Paint += (s, e) =>
        {
            if (dwellProgress <= 0)
            {
                return;
            }

            int barHeight = (int)(6 * scale),
                barWidth = (int)(btn.Width * dwellProgress);

            using Brush barBrush = new SolidBrush(
                SystemInformation.HighContrast ?
                    SystemColors.HighlightText :
                    Color.DarkOrange);

            e.Graphics.FillRectangle(
                barBrush,
                0,
                btn.Height - barHeight,
                barWidth,
                barHeight);
        };

        return btn;
    }
}