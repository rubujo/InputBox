using InputBox.Core.Extensions;
using InputBox.Core.Feedback;
using InputBox.Core.Input;
using InputBox.Core.Services;
using InputBox.Resources;
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
    /// AccessibleNumericUpDown
    /// </summary>
    private readonly AccessibleNumericUpDown _nud;

    /// <summary>
    /// 用於 A11y 廣播的 Label
    /// </summary>
    private readonly AnnouncerLabel _announcer;

    /// <summary>
    /// 預設數值
    /// </summary>
    private readonly decimal _defaultValue;

    /// <summary>
    /// 遊戲手把控制器
    /// </summary>
    private IGamepadController? _gamepadController;

    /// <summary>
    /// 取得使用者輸入的數值
    /// </summary>
    public decimal Value => _nud.Value;

    /// <summary>
    /// 設定控制器實作，並訂閱事件。
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
                _gamepadController.UpPressed += HandleUp;
                _gamepadController.UpRepeat += HandleUp;
                _gamepadController.DownPressed += HandleDown;
                _gamepadController.DownRepeat += HandleDown;
                _gamepadController.APressed += HandleConfirm;
                _gamepadController.StartPressed += HandleConfirm;
                _gamepadController.BPressed += HandleCancel;
                _gamepadController.XPressed += HandleReset;
            }
        }
    }

    /// <summary>
    /// 取消訂閱目前手把控制器的所有事件
    /// </summary>
    private void UnsubscribeGamepadEvents()
    {
        if (_gamepadController != null)
        {
            _gamepadController.UpPressed -= HandleUp;
            _gamepadController.UpRepeat -= HandleUp;
            _gamepadController.DownPressed -= HandleDown;
            _gamepadController.DownRepeat -= HandleDown;
            _gamepadController.APressed -= HandleConfirm;
            _gamepadController.StartPressed -= HandleConfirm;
            _gamepadController.BPressed -= HandleCancel;
            _gamepadController.XPressed -= HandleReset;
        }
    }

    /// <summary>
    /// 是否正在處理數值變更（原子旗標：0=否, 1=是）
    /// </summary>
    private volatile int _isProcessingValue = 0;

    /// <summary>
    /// 處理向上按鍵事件，委派給 NumericUpDown 的 UpButton 方法，確保行為一致且獲得內建的邊界檢查與事件觸發
    /// </summary>
    private void HandleUp() => this.SafeInvoke(() =>
    {
        try
        {
            if (IsDisposed ||
                Interlocked.CompareExchange(ref _isProcessingValue, 1, 0) != 0)
            {
                return;
            }

            try
            {
                _nud.UpButton();
            }
            finally
            {
                Interlocked.Exchange(ref _isProcessingValue, 0);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[NumericInputDialog] HandleUp 錯誤：{ex.Message}");
        }
    });

    /// <summary>
    /// 處理向下按鍵事件，委派給 NumericUpDown 的 DownButton 方法，確保行為一致且獲得內建的邊界檢查與事件觸發
    /// </summary>
    private void HandleDown() => this.SafeInvoke(() =>
    {
        try
        {
            if (IsDisposed ||
                Interlocked.CompareExchange(ref _isProcessingValue, 1, 0) != 0)
            {
                return;
            }

            try
            {
                _nud.DownButton();
            }
            finally
            {
                Interlocked.Exchange(ref _isProcessingValue, 0);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[NumericInputDialog] HandleDown 錯誤：{ex.Message}");
        }
    });

    /// <summary>
    /// 處理確認按鍵事件，先強制驗證輸入內容確保 Value 屬性是最新的，然後設定 DialogResult 並關閉對話框
    /// </summary>
    private void HandleConfirm() => this.SafeInvoke(() =>
    {
        try
        {
            if (IsDisposed)
            {
                return;
            }

            _nud.ValidateValue();

            DialogResult = DialogResult.OK;

            Close();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[NumericInputDialog] HandleConfirm 錯誤：{ex.Message}");
        }
    });

    /// <summary>
    /// 處理取消按鍵事件，設定 DialogResult 為 Cancel 並關閉對話框
    /// </summary>
    private void HandleCancel() => this.SafeInvoke(() =>
    {
        try
        {
            if (IsDisposed)
            {
                return;
            }

            DialogResult = DialogResult.Cancel;

            Close();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[NumericInputDialog] HandleCancel 錯誤：{ex.Message}");
        }
    });

    /// <summary>
    /// 用於管理對話框生命週期內非同步任務的取消權杖來源
    /// </summary>
    private readonly CancellationTokenSource _cts = new();

    /// <summary>
    /// 處理重設按鍵事件，將數值重置為預設值，並提供震動反饋。
    /// 使用 SafeInvoke 確保在 UI 執行緒執行，並使用 Math.Clamp 確保重置值在有效範圍內。
    /// 重置後會有短暫的防抖機制，避免快速重複觸發造成問題。
    /// 重置完成後會透過 ValueChanged 事件統一處理無障礙通知，避免重複播報。
    /// </summary>
    private void HandleReset() => this.SafeInvoke(() =>
    {
        try
        {
            if (IsDisposed ||
                Interlocked.CompareExchange(ref _isProcessingValue, 1, 0) != 0)
            {
                return;
            }

            try
            {
                // 執行重設邏輯。
                _nud.Value = Math.Clamp(_defaultValue, _nud.Minimum, _nud.Maximum);

                _nud.Focus();

                // 使用 SafeBeginInvoke 確保在 UI 執行緒空閒時執行選取，提升可靠性。
                this.SafeBeginInvoke(() => _nud.Select(0, _nud.Text.Length));

                FeedbackService.PlaySound(SystemSounds.Asterisk);

                FeedbackService.VibrateAsync(_gamepadController, VibrationPatterns.CursorMove).SafeFireAndForget();
            }
            finally
            {
                // 延遲一下再重置旗標，達到防抖效果。
                Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(250, _cts.Token);

                        Interlocked.Exchange(ref _isProcessingValue, 0);
                    }
                    catch (OperationCanceledException)
                    {

                    }
                },
                _cts.Token)
                .SafeFireAndForget();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[NumericInputDialog] HandleReset 錯誤：{ex.Message}");
        }
    });

    /// <summary>
    /// 內部廣播方法：委派給主視窗處理，確保進入語音隊列，防止訊息被切斷。
    /// 若無 Owner，則使用本地廣播器並包含避讓延遲。
    /// </summary>
    /// <param name="message">要廣播的訊息</param>
    /// <param name="interrupt">是否中斷當前廣播</param>
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
    /// 用於對話框的統一放大字型
    /// </summary>
    private readonly Font _a11yFont;

    /// <summary>
    /// 處置對話框資源
    /// </summary>
    /// <param name="disposing">是否正在處置受控資源</param>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _nud?.Font?.Dispose();
            _nud?.Dispose();
            _announcer?.Dispose();
            _a11yFont?.Dispose();
        }

        base.Dispose(disposing);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);

        UpdateMinimumSize();
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);

        UpdateMinimumSize();
    }

    /// <summary>
    /// 更新視窗最小尺寸，確保在高 DPI 縮放時佈局不崩潰。
    /// </summary>
    private void UpdateMinimumSize()
    {
        // 基準寬度為 350，根據目前的 DeviceDpi 與標準 96.0f 的比例進行縮放。
        float scale = DeviceDpi / 96.0f;

        MinimumSize = new Size((int)(350 * scale), 0);
    }

    /// <summary>
    /// 初始化數值輸入對話框
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

        TableLayoutPanel tlp = new()
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding((int)(25 * scale)),
            // 設定為 Grouping 角色，協助輔助科技識別這是一個邏輯區塊。
            AccessibleRole = AccessibleRole.Grouping
        };

        // 明確定義排版樣式。
        tlp.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        tlp.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        tlp.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        tlp.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        // 建立一個統一放大的 A11y 字型（預設為 11f），供所有控制項共用，讓視覺協調且易讀。
        // 優先使用 MainForm 提供的共享字型邏輯，確保全專案 A11y 視覺規格一致。
        _a11yFont = MainForm.GetSharedA11yFont(DeviceDpi, FontStyle.Bold);

        Label lbl = new()
        {
            Text = promptText,
            AutoSize = true,
            // 設定最大寬度以強制長文字換行，避免對話框寬度失控。
            MaximumSize = new Size((int)(400 * scale), 0),
            Margin = new Padding(0, 0, 0, (int)(15 * scale)),
            AccessibleRole = AccessibleRole.StaticText,
            AccessibleName = promptText,
            Font = _a11yFont
        };

        _nud = new()
        {
            DecimalPlaces = decimalPlaces,
            Increment = increment,
            Minimum = minimum,
            Maximum = maximum,
            // 使用 Math.Clamp 確保數值在有效範圍內，防止異常。
            Value = Math.Clamp(currentValue, minimum, maximum),
            // 動態縮放寬度，高度則由 Font 自動決定。
            Width = (int)(280 * scale),
            // 數值右對齊，符合 UI 慣例。
            TextAlign = HorizontalAlignment.Right,
            AccessibleName = promptText,
            // A11y 描述：報讀目前值與有效範圍。
            AccessibleDescription = string.Format(Strings.A11y_Value_Range_Desc, currentValue, minimum, maximum),
            TabIndex = 0,
            Font = _a11yFont
        };

        // 確保在 NumericUpDown 中按 Enter 能正確觸發 AcceptButton。
        _nud.KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                // 強制驗證編輯文字，確保 Value 屬性已同步為最新輸入。
                _nud.ValidateValue();

                DialogResult = DialogResult.OK;

                Close();

                e.SuppressKeyPress = true;
            }
        };

        // 當數值改變時，同步更新無障礙描述，並主動廣播最新數值。
        _nud.ValueChanged += (s, e) =>
        {
            _nud.AccessibleDescription = string.Format(Strings.A11y_Value_Range_Desc, _nud.Value, _nud.Minimum, _nud.Maximum);

            // 主動廣播最新值，讓使用者在微調（如按上下鍵）時能立即獲得語音反饋。
            // 使用打斷模式（interrupt: true），避免在高頻率變動下產生語音隊列堆積。
            AnnounceA11y(string.Format(Strings.A11y_CurrentValue, _nud.Value), interrupt: true);
        };

        FlowLayoutPanel flpButtons = new()
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Fill,
            AutoSize = true,
            Margin = new Padding(0, (int)(25 * scale), 0, 0),
            // A11y：設定為 Grouping 角色，協助輔助科技識別按鈕區。
            AccessibleRole = AccessibleRole.Grouping,
            AccessibleName = Strings.A11y_ButtonArea
        };

        Button btnCancel = new()
        {
            // 將 Mnemonic 設為 'B'，與手把的 B 鍵同步。
            // 鍵盤使用者按 Alt+B，手把使用者按 B，滑鼠點擊。
            Text = ControlExtensions.GetMnemonicText(Strings.Btn_Cancel, 'B'),
            DialogResult = DialogResult.Cancel,
            AccessibleName = Strings.Btn_Cancel,
            AccessibleDescription = Strings.A11y_Btn_Cancel_Desc,
            AutoSize = true,
            MinimumSize = new Size((int)(100 * scale), (int)(36 * scale)),
            TabIndex = 2,
            UseMnemonic = true,
            Font = _a11yFont
        };

        Button btnOk = new()
        {
            // 將 Mnemonic 設為 'A'，與手把的 A 鍵同步。
            Text = ControlExtensions.GetMnemonicText(Strings.Btn_OK, 'A'),
            DialogResult = DialogResult.OK,
            AccessibleName = Strings.Btn_OK,
            AccessibleDescription = Strings.A11y_Btn_OK_Desc,
            AutoSize = true,
            MinimumSize = new Size((int)(100 * scale), (int)(36 * scale)),
            TabIndex = 1,
            UseMnemonic = true,
            Font = _a11yFont
        };

        Button btnSetDefault = new()
        {
            // 將 Mnemonic 設為 'X'，與手把的 X 鍵同步。
            Text = ControlExtensions.GetMnemonicText(Strings.Btn_SetDefault, 'X'),
            AccessibleName = Strings.Btn_SetDefault,
            AccessibleDescription = string.Format(Strings.A11y_Btn_SetDefault_Desc, _defaultValue),
            AutoSize = true,
            MinimumSize = new Size((int)(100 * scale), (int)(36 * scale)),
            TabIndex = 3,
            UseMnemonic = true,
            Font = _a11yFont
        };

        btnSetDefault.Click += (s, e) =>
        {
            HandleReset();
        };

        flpButtons.Controls.Add(btnCancel);
        flpButtons.Controls.Add(btnSetDefault);
        flpButtons.Controls.Add(btnOk);

        // 指定明確的 Row 索引。
        tlp.Controls.Add(lbl, 0, 0);
        tlp.Controls.Add(_nud, 0, 1);
        tlp.Controls.Add(flpButtons, 0, 2);

        // 將 A11y 廣播標籤加入視窗，但不佔用佈局空間。
        _announcer.Size = Size.Empty;
        Controls.Add(_announcer);

        Controls.Add(tlp);

        AcceptButton = btnOk;
        CancelButton = btnCancel;

        Shown += (s, e) =>
        {
            _nud.Focus();

            // 協調 LiveRegion：當彈出對話框時，暫時停用主視窗的廣播器 LiveSetting，防止訊息干擾。
            if (Owner is MainForm mainForm)
            {
                mainForm.SetA11yLiveSetting(AutomationLiveSetting.Off);
            }

            // 使用 SafeBeginInvoke 確保在 UI 執行緒完全準備好後再執行選取，
            // 解決 NumericUpDown 內部 TextBox 延遲初始化的問題。
            this.SafeBeginInvoke(() => _nud.Select(0, _nud.Text.Length));

            // 主動廣播提示文字，確保使用者在焦點轉移後立即得知操作目標。
            AnnounceA11y(promptText);
        };

        // 確保對話框取得焦點時，喚醒手把控制器。
        Activated += (s, e) =>
        {
            // 使用非同步延遲（50 毫秒），確保這個 Resume() 指令
            // 絕對會在 MainForm 的 Deactivate 相關動作都處理完畢後才執行，
            // 避免被主視窗的 Pause() 給誤殺。
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
                catch (OperationCanceledException)
                {

                }
            },
            _cts.Token)
            .SafeFireAndForget();
        };

        // 當對話框失去焦點時，檢查是否切換到其他應用程式，若是則暫停手把。
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

        // 視窗關閉時的 A11y 提示。
        FormClosing += (s, e) =>
        {
            // 中止所有背景任務。
            _cts.Cancel();
            _cts.Dispose();

            // 還原 LiveRegion 協調：當對話框關閉時，恢復主視窗廣播器的 LiveSetting。
            if (Owner is MainForm mainForm)
            {
                mainForm.SetA11yLiveSetting(AutomationLiveSetting.Polite);
            }

            // 關鍵修正：對話框關閉時，必須徹底取消手把事件的訂閱。
            // 這能防止對話框雖然不可見但仍持續在背景處理手把輸入的競態風險。
            UnsubscribeGamepadEvents();

            _gamepadController = null;

            if (DialogResult == DialogResult.Cancel)
            {
                // 注意：由於視窗即將關閉，AnnounceA11y 可能會失效，
                // 這裡我們在主視窗發送廣播（如果有 Owner 的話）。
                (Owner as MainForm)?.AnnounceA11y(Strings.A11y_Cancelled);
            }

            // 釋放廣播器資源。
            _announcer?.Dispose();
        };
    }
}