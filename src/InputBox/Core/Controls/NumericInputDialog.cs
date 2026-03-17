using InputBox.Core.Configuration;
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
    /// <param name="parent">父對話框實例</param>
    private sealed class AccessibleNumericUpDown(NumericInputDialog parent) : NumericUpDown
    {
        /// <summary>
        /// NumericInputDialog
        /// </summary>
        private readonly NumericInputDialog _parent = parent;

        public override void UpButton()
        {
            decimal oldValue = Value;

            base.UpButton();

            if (Value == oldValue)
            {
                _parent.HandleBoundaryHit(true);
            }
        }

        public override void DownButton()
        {
            decimal oldValue = Value;

            base.DownButton();

            if (Value == oldValue)
            {
                _parent.HandleBoundaryHit(false);
            }
        }

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
    /// 3x2 網格連動區容器
    /// </summary>
    private readonly TableLayoutPanel _tlpGrid;

    /// <summary>
    /// 增加按鈕
    /// </summary>
    private readonly Button? _btnPlus;

    /// <summary>
    /// 減少按鈕
    /// </summary>
    private readonly Button? _btnMinus;

    /// <summary>
    /// 重設按鈕
    /// </summary>
    private readonly Button? _btnReset;

    /// <summary>
    /// 用於 A11y 廣播的 Label
    /// </summary>
    private readonly AnnouncerLabel _announcer;

    /// <summary>
    /// 統一放大的 A11y 字型
    /// </summary>
    private readonly Font _a11yFont;

    /// <summary>
    /// NUD 專屬放大字型，需手動 Dispose
    /// </summary>
    private readonly Font _nudFont;

    /// <summary>
    /// 用於管理對話框生命週期內非同步任務的取消權杖來源
    /// </summary>
    private readonly CancellationTokenSource _cts = new();

    /// <summary>
    /// A11y 廣播防抖用的序號
    /// </summary>
    private long _a11yDebounceId = 0;

    /// <summary>
    /// 是否正在閃爍（用於防止重複觸發閃爍效果）
    /// </summary>
    private volatile int _isFlashing = 0;

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

    protected override void OnHandleDestroyed(EventArgs e)
    {
        try
        {
            // 確保靜態事件在視窗控制項控制代碼銷毀時被絕對釋放。
            SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
        }
        finally
        {
            base.OnHandleDestroyed(e);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _cts.Cancel();
            _cts.Dispose();
            _a11yFont?.Dispose();
            _nudFont?.Dispose();
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

        // A11y 廣播：告知使用者手把連線狀態變更。
        AnnounceA11y(connected ?
            Strings.A11y_Gamepad_Connected :
            Strings.A11y_Gamepad_Disconnected);
    }

    /// <summary>
    /// 處理數值達到邊界的事件（由按鈕或控制項內部觸發）
    /// </summary>
    /// <param name="isUpperLimit">指示是否為上限</param>
    internal void HandleBoundaryHit(bool isUpperLimit) => this.SafeInvoke(() =>
    {
        FeedbackService.PlaySound(SystemSounds.Beep);

        FeedbackService.VibrateAsync(
                _gamepadController,
                VibrationPatterns.ActionFail)
            .SafeFireAndForget();

        FlashAlertAsync().SafeFireAndForget();

        AnnounceA11y(
            isUpperLimit ?
                Strings.A11y_Value_Max :
                Strings.A11y_Value_Min,
            interrupt: true);
    });

    /// <summary>
    /// 處理數值增加，並保留焦點以支援眼動儀連發與鍵盤連點
    /// </summary>
    private void HandlePlus() => this.SafeInvoke(_nud.UpButton);

    /// <summary>
    /// 處理數值減少，並保留焦點以支援眼動儀連發與鍵盤連點
    /// </summary>
    private void HandleMinus() => this.SafeInvoke(_nud.DownButton);

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

        FeedbackService.VibrateAsync(
                _gamepadController,
                VibrationPatterns.CursorMove)
            .SafeFireAndForget();
    });

    /// <summary>
    /// 執行視覺警示閃爍效果
    /// </summary>
    /// <returns>Task</returns>
    private async Task FlashAlertAsync()
    {
        if (IsDisposed ||
            !IsHandleCreated ||
            Interlocked.CompareExchange(ref _isFlashing, 1, 0) != 0)
        {
            return;
        }

        CancellationTokenSource alertCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);

        CancellationToken token = alertCts.Token;

        try
        {
            Color alertColor = SystemInformation.HighContrast ?
                SystemColors.HighlightText :
                Color.FromArgb(255, 140, 0);

            void ApplyAlertVisuals(float intensity)
            {
                if (IsDisposed ||
                    !IsHandleCreated)
                {
                    return;
                }

                if (SystemInformation.HighContrast)
                {
                    _nud.BackColor = intensity > 0.5f ?
                        alertColor :
                        SystemColors.Highlight;

                    // 高對比雙重補償：字體脈衝。
                    if (_nudFont != null)
                    {
                        float pulseSize = _nudFont.Size + (1.0f * intensity);

                        Font oldFont = _nud.Font;

                        _nud.Font = new Font(_nudFont.FontFamily, pulseSize, _nudFont.Style);

                        // 核心修正：釋放動態建立的字型資源，避免 GDI Handle 耗盡引發洩漏。
                        if (oldFont != null &&
                            oldFont != _nudFont)
                        {
                            oldFont.Dispose();
                        }
                    }
                }
                else
                {
                    Color baseColor = _nud.Focused ?
                        SystemColors.Highlight :
                        SystemColors.Window;

                    int r = (int)(baseColor.R + (alertColor.R - baseColor.R) * intensity),
                        g = (int)(baseColor.G + (alertColor.G - baseColor.G) * intensity),
                        b = (int)(baseColor.B + (alertColor.B - baseColor.B) * intensity);

                    _nud.BackColor = Color.FromArgb(255, r, g, b);
                }
            }

            if (!SystemInformation.UIEffectsEnabled)
            {
                this.SafeInvoke(() => ApplyAlertVisuals(1.0f));

                await Task.Delay(800, token);

                return;
            }

            int totalDuration = AppSettings.PhotoSafeFrequencyMs,
                delayMs = 16;

            long startTime = Stopwatch.GetTimestamp();

            while (true)
            {
                token.ThrowIfCancellationRequested();

                long elapsedTicks = Stopwatch.GetTimestamp() - startTime;

                double elapsedMs = (double)elapsedTicks / Stopwatch.Frequency * 1000.0;

                if (elapsedMs >= totalDuration)
                {
                    break;
                }

                // 使用 AppSettings.PhotoSafeFrequencyMs（1000ms）對齊閃爍週期。
                double angle = elapsedMs / AppSettings.PhotoSafeFrequencyMs * 2.0 * Math.PI - (Math.PI / 2.0);

                float intensity = (float)((Math.Sin(angle) + 1.0) / 2.0);

                this.SafeInvoke(() => ApplyAlertVisuals(intensity));

                await Task.Delay(delayMs, token);
            }
        }
        catch (OperationCanceledException)
        {

        }
        finally
        {
            Interlocked.Exchange(ref _isFlashing, 0);

            alertCts.Dispose();

            if (!IsDisposed && IsHandleCreated)
            {
                this.SafeInvoke(() =>
                {
                    UpdateFocusVisuals(_nud.Focused || _nud.ContainsFocus);

                    // 核心修正：動畫結束後還原字型並清理最後一個臨時字型。
                    Font lastFont = _nud.Font;

                    _nud.Font = _nudFont;

                    if (lastFont != null &&
                        lastFont != _nudFont)
                    {
                        lastFont.Dispose();
                    }
                });
            }
        }
    }

    /// <summary>
    /// 內部 A11y 廣播方法
    /// </summary>
    /// <param name="message">要廣播的訊息</param>
    /// <param name="interrupt">是否中斷目前的廣播</param>
    private void AnnounceA11y(
        string message,
        bool interrupt = false)
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
            long currentId = Interlocked.Increment(ref _a11yDebounceId);

            // 在獨立對話框模式下，我們手動加入 150ms 的音訊避讓與節流延遲。
            Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(150, _cts.Token);

                    // 只有最新的請求才會被執行，實現 Debounce 效果。
                    if (Interlocked.Read(ref _a11yDebounceId) == currentId &&
                        !IsDisposed &&
                        IsHandleCreated)
                    {
                        this.SafeInvoke(() => _announcer.Announce(message, interrupt));
                    }
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

        // 綜合判斷：只要數值框本身有焦點，或是旁邊的加減按鈕有焦點，數值框都應該保持高亮。
        bool shouldHighlight = isFocused ||
            (_btnPlus != null && _btnPlus.Focused) ||
            (_btnMinus != null && _btnMinus.Focused);

        if (shouldHighlight)
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
        _nudFont = new Font(_a11yFont.FontFamily, _a11yFont.Size * 2.0f, FontStyle.Bold);

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
            AutoSize = true,
            MaximumSize = new Size((int)(500 * scale), 0),
            Margin = new Padding(0, 0, 0, (int)(25 * scale)),
            Font = _a11yFont,
            AccessibleRole = AccessibleRole.StaticText
        };

        // 3x2 網格連動區。
        _tlpGrid = new TableLayoutPanel()
        {
            AutoSize = true,
            ColumnCount = 3,
            RowCount = 2,
            AccessibleRole = AccessibleRole.Grouping,
            AccessibleName = string.Format(Strings.Msg_EnterValue, title)
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

        // 為 NumericUpDown 加入鍵盤與滾輪監聽，確保所有操作路徑撞牆時都能觸發 A11y 視覺回饋。
        _nud.KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Up)
            {
                HandlePlus();
            }
            else if (e.KeyCode == Keys.Down)
            {
                HandleMinus();
            }
        };

        _nud.MouseWheel += (s, e) =>
        {
            if (e.Delta > 0)
            {
                HandlePlus();
            }
            else if (e.Delta < 0)
            {
                HandleMinus();
            }
        };

        // 當數值改變時，同步更新無障礙描述，並主動廣播最新數值。
        _nud.ValueChanged += (s, e) =>
        {
            _nud.AccessibleDescription = string.Format(
                Strings.A11y_Value_Range_Desc,
                _nud.Value,
                _nud.Minimum,
                _nud.Maximum);

            // 主動廣播最新值，讓使用者在微調（如按上下鍵）時能立即獲得語音反饋。
            // 使用打斷模式（interrupt: true），避免在高頻率變動下產生語音隊列堆積。
            AnnounceA11y(
                string.Format(Strings.A11y_CurrentValue, _nud.Value),
                interrupt: true);

            FeedbackService.VibrateAsync(
                    _gamepadController,
                    VibrationPatterns.CursorMove)
                .SafeFireAndForget();
        };

        // 按鈕連動 NUD 高亮邏輯。

        // 第 1 列：數值加減。
        _btnMinus = CreateEyeTrackerButton(
            Strings.Btn_Minus,
            Strings.A11y_Btn_Minus_Desc,
            scale,
            _a11yFont,
            (active) => UpdateFocusVisuals(active || _nud.Focused || _nud.ContainsFocus));
        _btnMinus.Click += (s, e) => HandleMinus();
        _btnMinus.Anchor = AnchorStyles.None;
        _btnMinus.TabIndex = 0;

        _btnPlus = CreateEyeTrackerButton(
            Strings.Btn_Plus,
            Strings.A11y_Btn_Plus_Desc,
            scale,
            _a11yFont,
            (active) => UpdateFocusVisuals(active || _nud.Focused || _nud.ContainsFocus));
        _btnPlus.Click += (s, e) => HandlePlus();
        _btnPlus.Anchor = AnchorStyles.None;
        _btnPlus.TabIndex = 2;

        // 第 2 列：操作按鈕。
        Button btnOk = CreateEyeTrackerButton(
            ControlExtensions.GetMnemonicText(Strings.Btn_OK, 'A'),
            Strings.A11y_Btn_OK_Desc,
            scale,
            _a11yFont);
        btnOk.DialogResult = DialogResult.OK;
        btnOk.Anchor = AnchorStyles.None;
        btnOk.TabIndex = 3;

        Button btnCancel = CreateEyeTrackerButton(
            ControlExtensions.GetMnemonicText(Strings.Btn_Cancel, 'B'),
            Strings.A11y_Btn_Cancel_Desc,
            scale,
            _a11yFont);
        btnCancel.DialogResult = DialogResult.Cancel;
        btnCancel.Anchor = AnchorStyles.None;
        btnCancel.TabIndex = 4;

        _btnReset = CreateEyeTrackerButton(
            ControlExtensions.GetMnemonicText(Strings.Btn_SetDefault, 'X'),
            string.Format(Strings.A11y_Btn_SetDefault_Desc, _defaultValue),
            scale,
            _a11yFont);
        _btnReset.Click += (s, e) => HandleReset();
        _btnReset.Anchor = AnchorStyles.None;
        _btnReset.TabIndex = 5;

        // 填充 3x2 網格。
        _tlpGrid.Controls.Add(_btnMinus, 0, 0);
        _tlpGrid.Controls.Add(_nud, 1, 0);
        _tlpGrid.Controls.Add(_btnPlus, 2, 0);
        _tlpGrid.Controls.Add(btnOk, 2, 1);
        _tlpGrid.Controls.Add(btnCancel, 0, 1);
        _tlpGrid.Controls.Add(_btnReset, 1, 1);

        tlpMain.Controls.Add(lblPrompt, 0, 0);
        tlpMain.Controls.Add(_tlpGrid, 0, 1);

        // 將大小設為 1x1 並移至不可見區域，避免 Size.Empty 被 UIA 剔除。
        _announcer.Size = new Size(1, 1);
        _announcer.Location = new Point(-100, -100);
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

        // 使用 TextRenderer 預先測量 Bold 字型的最大寬度，確保加粗時不會引發佈局抖動。
        Size boldTextSize = TextRenderer.MeasureText(text, boldFont);

        int baseMinWidth = Math.Max((int)(120 * scale), boldTextSize.Width + (int)(24 * scale)),
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
            BackColor = SystemColors.Control,
            ForeColor = SystemColors.ControlText
        };

        btn.Disposed += (s, e) => boldFont.Dispose();

        Color originalBackColor = btn.BackColor,
            originalForeColor = btn.ForeColor;

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

                btn.RunDwellAnimationAsync(
                        id,
                        () => Interlocked.Read(ref animationId),
                        (p) => dwellProgress = p)
                    .SafeFireAndForget();
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

        btn.Paint += (s, e) =>
        {
            if (dwellProgress <= 0)
            {
                return;
            }

            // 動態存取最新的 DeviceDpi，避免靜態捕獲舊 DPI 導致跨螢幕拖曳時繪圖偏移。
            float currentScale = btn.DeviceDpi / 96.0f;

            int barHeight = (int)(6 * currentScale),
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