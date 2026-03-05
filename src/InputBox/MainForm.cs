using InputBox.Core.Configuration;
using InputBox.Core.Controls;
using InputBox.Core.Extensions;
using InputBox.Core.Feedback;
using InputBox.Core.Input;
using InputBox.Core.Interop;
using InputBox.Core.Services;
using InputBox.Resources;
using Microsoft.Win32;
using System.Diagnostics;
using System.Media;

namespace InputBox;

public partial class MainForm : Form
{
    /// <summary>
    /// InputHistoryService
    /// </summary>
    private readonly InputHistoryService _historyService;

    /// <summary>
    /// WindowFocusService
    /// </summary>
    private readonly WindowFocusService _windowFocusService;

    /// <summary>
    /// GlobalHotKeyService
    /// </summary>
    private readonly GlobalHotKeyService _hotKeyService;

    /// <summary>
    /// FeedbackService
    /// </summary>
    private readonly FeedbackService _feedbackService;

    /// <summary>
    /// ClipboardService
    /// </summary>
    private readonly ClipboardService _clipboardService;

    /// <summary>
    /// TouchKeyboardService
    /// </summary>
    private readonly TouchKeyboardService _touchKeyboardService;

    /// <summary>
    /// WindowNavigationService
    /// </summary>
    private readonly WindowNavigationService _navigationService;

    /// <summary>
    /// IInputContext
    /// </summary>
    private IInputContext? _inputContext;

    /// <summary>
    /// GamepadController
    /// </summary>
    private GamepadController? _gamepadController;

    /// <summary>
    /// 是否正在切換回先前的前景視窗
    /// </summary>
    private bool _isReturning;

    /// <summary>
    /// 是否正在顯示觸控式鍵盤
    /// </summary>
    private bool _isShowingTouchKeyboard;

    /// <summary>
    /// 用於無障礙廣播的隱藏標籤
    /// </summary>
    private AnnouncerLabel? _lblA11yAnnouncer;

    /// <summary>
    /// 基礎 DPI
    /// </summary>
    private const float BaseDpi = 96f;

    /// <summary>
    /// 基礎最小寬度
    /// </summary>
    private const int BaseMinWidth = 400;

    /// <summary>
    /// 基礎最小高度
    /// </summary>
    private const int BaseMinHeight = 85;

    /// <summary>
    /// 按鈕文字復原
    /// </summary>
    private const int Delay_ButtonReset = 1000;

    /// <summary>
    /// 最後一次開啟觸控式鍵盤的時間（用於防止重複開啟）
    /// </summary>
    private DateTime _lastTouchKeyboardOpened = DateTime.MinValue;

    /// <summary>
    /// 是否正在閃爍（用於防止重複觸發閃爍效果）
    /// </summary>
    private bool _isFlashing = false;

    public MainForm()
    {
        InitializeComponent();

        // 套用全域震動強度設定。
        VibrationPatterns.GlobalIntensityMultiplier = AppSettings.Current.VibrationIntensity;

        // 使用設定檔容量初始化 InputHistoryManager。
        _historyService = new InputHistoryService(AppSettings.Current.HistoryCapacity);
        _windowFocusService = new WindowFocusService();

        // 這裡採用手動依賴注入（DI）的方式。
        _hotKeyService = new GlobalHotKeyService();
        _feedbackService = new FeedbackService();
        _navigationService = new WindowNavigationService(_windowFocusService, _feedbackService);
        _clipboardService = new ClipboardService();
        _touchKeyboardService = new TouchKeyboardService();

        // 在初始化完成後，套用本地化。
        ApplyLocalization();

        // 初始化無障礙廣播。
        InitializeA11yAnnouncer();

        SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Win32.WM_HOTKEY &&
            m.WParam.ToInt32() == HotKey.ShowInput)
        {
            ShowForInput();

            return;
        }

        base.WndProc(ref m);
    }

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);

        _gamepadController?.Resume();
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);

        if (_isShowingTouchKeyboard)
        {
            // 這次失焦是「預期內的」。
            return;
        }

        _gamepadController?.Pause();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // 應用程式關閉時，主動清除所有輸入歷程記錄。
        _historyService.Clear();

        base.OnFormClosing(e);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        // 檢查焦點是否在 TBInput 上。
        if (ActiveControl == TBInput)
        {
            // 當按下 Enter 鍵，且沒有同時按住 Shift 時：
            // - 阻止 TextBox 的預設行為（插入換行字元）。
            // - 讓 Enter 鍵只用來觸發自訂邏輯。
            // - Shift + Enter 則保留原本的換行功能。
            // 注意：ProcessCmdKey 的 keyData 已經包含所有修飾鍵資訊。
            if (keyData == Keys.Enter)
            {
                if (string.IsNullOrWhiteSpace(TBInput.Text))
                {
                    ShowTouchKeyboard();
                }
                else
                {
                    BtnCopy.PerformClick();
                }

                // 告訴 Windows 這個按鍵已經處理掉了，不要再傳給 TextBox。
                return true;
            }

            // Shift + Enter：換行。
            // 不攔截（回傳 base.ProcessCmdKey）。
            if (keyData == (Keys.Enter | Keys.Shift))
            {
                return base.ProcessCmdKey(ref msg, keyData);
            }
        }

        // 其他按鍵照常處理。
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);

        #region 依據 DPI 調整 MinimumSize

        // 96 DPI = 100%。
        float scale = DeviceDpi / BaseDpi;

        int minWidth = (int)Math.Round(BaseMinWidth * scale),
            minHeight = (int)Math.Round(BaseMinHeight * scale);

        // 依據 DPI 調整最小尺寸，確保在高 DPI 顯示器上不會太小。
        MinimumSize = new Size(minWidth, minHeight);

        #endregion

        // 註冊全域快速鍵：Ctrl + Alt + Shift + I。
        // 放在 OnHandleCreated 是為了確保當 WinForms 重建 Handle 時（如屬性變更），熱鍵能重新註冊。
        RegisterHotKeyInternal();
    }

    private void MainForm_Activated(object sender, EventArgs e)
    {
        // 確保視窗還原時，文字方塊直接取得焦點，不用再點一次。
        if (WindowState == FormWindowState.Normal)
        {
            TBInput.Focus();
        }
    }

    private async void MainForm_Shown(object sender, EventArgs e)
    {
        try
        {
            // Windows 平板模式會在 Shown 後強制最大化。
            // 延遲 50ms 讓系統完成動畫與視窗狀態更新，再強制還原。
            await Task.Delay(AppSettings.Current.WindowRestoreDelay);

            if (IsDisposed ||
                !IsHandleCreated)
            {
                return;
            }

            // 強制還原視窗，避免在 Windows 桌面（平板模式）下自動最大化。
            // 對「Windows 遊戲：全螢幕體驗」（Xbox 全螢幕體驗）不受影響，一樣會自動最大化。
            Win32.ShowWindow(Handle, Win32.SW_RESTORE);

            // 初始化 GamepadController。
            InitializeGamepadController();

            // 啟動時自動取得焦點。
            TBInput.Focus();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                Strings.Err_Title,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void SystemEvents_UserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category == UserPreferenceCategory.Color ||
            e.Category == UserPreferenceCategory.Accessibility)
        {
            this.SafeInvoke(() =>
            {
                if (TBInput.Focused)
                {
                    TBInput_Enter(TBInput, EventArgs.Empty);
                }
                else
                {
                    UpdateBorderColor();
                }
            });
        }
    }

    private void TBInput_Enter(object sender, EventArgs e)
    {
        // 判斷是否為高對比模式，如果系統已經開啟高對比，
        // 就完全尊重系統設定，不要自己改顏色。
        if (SystemInformation.HighContrast)
        {
            TBInput.BackColor = SystemColors.Window;
            TBInput.ForeColor = SystemColors.WindowText;
            PInputHost.BackColor = SystemColors.Highlight;

            return;
        }

        // 背景改為「純黑色」，這樣可以跟 Windows 預設的藍色選取範圍區隔開來。
        TBInput.BackColor = Color.Black;

        // 文字改為「純白色」，黑底白字，對比度最高，藍黃色盲也能清楚看見。
        TBInput.ForeColor = Color.White;

        // 邊框維持系統高亮色（藍色），這樣在黑色方塊外圍會有一圈藍光，
        // 視覺效果非常現代且清晰。
        PInputHost.BackColor = SystemColors.Highlight;
    }

    private void TBInput_Leave(object sender, EventArgs e)
    {
        // 還原為一般視窗背景（白色）。
        TBInput.BackColor = SystemColors.Window;
        // 還原為一般視窗文字（黑色）。
        TBInput.ForeColor = SystemColors.WindowText;

        // 設定邊框顏色為灰色。
        PInputHost.BackColor = SystemColors.ControlDark;
    }

    private void TBInput_KeyDown(object sender, KeyEventArgs e)
    {
        HandleKeyDown(e);
    }

    private async void BtnCopy_Click(object sender, EventArgs e)
    {
        // 在最開頭建立快照。
        string strTextToCopy = TBInput.Text;

        try
        {
            // 使用快照檢查。
            if (string.IsNullOrEmpty(strTextToCopy))
            {
                // 發出警告音。
                _feedbackService.PlaySound(SystemSounds.Beep);

                await VibrateAsync(VibrationPatterns.ActionFail);

                return;
            }

            BtnCopy.Enabled = false;

            // 加入輸入歷程記錄。
            _historyService.Add(strTextToCopy);

            bool isCopySuccess = await _clipboardService.TrySetTextAsync(strTextToCopy);

            if (IsDisposed ||
                BtnCopy == null)
            {
                return;
            }

            if (!isCopySuccess)
            {
                _feedbackService.PlaySound(SystemSounds.Hand);

                await VibrateAsync(VibrationPatterns.ActionFail);

                BtnCopy.Text = Strings.Msg_CopyFail;
                BtnCopy.Enabled = true;

                // 確保按鈕重新啟用後，如果目前視窗還是活著的，將焦點還給輸入框或按鈕。
                if (ContainsFocus)
                {
                    // 失敗後將焦點還給輸入框讓使用者修改，是很好的體驗。
                    TBInput.Focus();
                }

                AnnounceA11y(Strings.Msg_CopyFail);

                await ResetButtonStateAsync();

                return;
            }

            // 複製成功後的處理。
            _feedbackService.PlaySound(SystemSounds.Asterisk);

            await VibrateAsync(VibrationPatterns.CopySuccess);

            BtnCopy.Text = Strings.Msg_Copied;
            BtnCopy.AccessibleDescription = Strings.Msg_Copied;

            AnnounceA11y(Strings.Msg_Copied);

            // 複製後清除。
            TBInput.Clear();

            await ReturnToPreviousWindowAsync();

            // 恢復按鈕狀態。
            await ResetButtonStateAsync();
        }
        catch (Exception ex)
        {
            // 捕捉所有異常，包括 ExternalException 和其他可能的錯誤。

            _feedbackService.PlaySound(SystemSounds.Hand);

            await VibrateAsync(VibrationPatterns.ActionFail);

            // 如果視窗還在，顯示錯誤。
            if (!IsDisposed)
            {
                BtnCopy.Text = Strings.Msg_CopyFail;
                BtnCopy.AccessibleDescription = Strings.Msg_CopyFail;
                // 確保按鈕被重新啟用，否則使用者無法重試。
                BtnCopy.Enabled = true;

                // 恢復按鈕狀態。
                await ResetButtonStateAsync();
            }

            Debug.WriteLine($"複製到剪貼簿失敗：{ex.Message}");
        }
    }

    /// <summary>
    /// 註冊全域快速鍵的內部方法，並在註冊失敗時顯示包含目前設定的快速鍵組合的錯誤訊息
    /// </summary>
    private void RegisterHotKeyInternal()
    {
        bool isOkay = _hotKeyService.RegisterShowInputHotkey(Handle);

        if (!isOkay)
        {
            // 動態組合出目前設定的快速鍵字串，例如 "Ctrl + Alt + Shift + I"。
            List<string> keys = [];

            int mods = AppSettings.Current.HotKeyModifiers;

            // 檢查位元遮罩包含哪些修飾鍵。
            if ((mods & 2) == 2)
            {
                keys.Add("Ctrl");
            }

            if ((mods & 1) == 1)
            {
                keys.Add("Alt");
            }

            if ((mods & 4) == 4)
            {
                keys.Add("Shift");
            }

            if ((mods & 8) == 8)
            {
                keys.Add("Win");
            }

            keys.Add(AppSettings.Current.HotKeyKey.ToUpper());

            string currentHotkeyStr = string.Join(" + ", keys);

            MessageBox.Show(
                string.Format(Strings.Err_HotkeyRegFail, $"[{currentHotkeyStr}]"),
                caption: Strings.Wrn_Title,
                buttons: MessageBoxButtons.OK,
                icon: MessageBoxIcon.Exclamation);
        }
    }

    /// <summary>
    /// 套用本地化
    /// </summary>
    private void ApplyLocalization()
    {
        // 設定視窗標題。
        Text = Strings.App_Title;

        // 設定按鈕文字。
        BtnCopy.Text = Strings.Btn_CopyDefault;

        // 設定 PlaceholderText。
        TBInput.PlaceholderText = Strings.Pht_TBInput;

        // 設定無障礙資訊。
        AccessibleName = Strings.A11y_MainFormName;
        AccessibleDescription = Strings.A11y_MainFormDesc;
        TBInput.AccessibleName = Strings.A11y_TBInputName;
        TBInput.AccessibleDescription = Strings.A11y_TBInputDesc;
        BtnCopy.AccessibleName = Strings.A11y_BtnCopyName;
        BtnCopy.AccessibleDescription = Strings.A11y_BtnCopyDesc;
    }

    /// <summary>
    /// 初始化 GamepadController
    /// </summary>
    private void InitializeGamepadController()
    {
        _inputContext = new FormInputContext(this);

        // 自動偵測有效的控制器索引。
        int activeUserIndex = GamepadController.GetFirstConnectedUserIndex();

        // 建立 GamepadRepeatSettings。
        GamepadRepeatSettings gamepadRepeatSettings = new()
        {
            InitialDelayFrames = AppSettings.Current.RepeatInitialDelayFrames,
            IntervalFrames = AppSettings.Current.RepeatIntervalFrames
        };

        try
        {
            // 使用偵測到的索引初始化。
            _gamepadController = new GamepadController(
                _inputContext,
                activeUserIndex,
                gamepadRepeatSettings);

            // 控制器 ↑ 鍵：瀏覽上一筆輸入歷史（等同鍵盤 ↑）。
            _gamepadController.UpPressed += () =>
            {
                this.SafeInvoke(() => NavigateHistory(-1));
            };

            // 控制器 ↓ 鍵：瀏覽下一筆輸入歷史（等同鍵盤 ↓）。
            _gamepadController.DownPressed += () =>
            {
                this.SafeInvoke(() => NavigateHistory(+1));
            };

            // 控制器 ↑ 鍵長按重複：瀏覽上一筆輸入歷史（等同鍵盤 ↑）。
            _gamepadController.UpRepeat += () =>
            {
                this.SafeInvoke(() => NavigateHistory(-1));
            };

            // 控制器 ↓ 鍵長按重複：瀏覽下一筆輸入歷史（等同鍵盤 ↓）。
            _gamepadController.DownRepeat += () =>
            {
                this.SafeInvoke(() => NavigateHistory(+1));
            };

            // 控制器 ← 鍵：將游標向左移動一個字元。
            _gamepadController.LeftPressed += MoveCursorLeft;

            // 控制器 ← 鍵長按重複：將游標連續向左移動一個字元。
            _gamepadController.LeftRepeat += MoveCursorLeft;

            // 控制器 → 鍵：將游標向右移動一個字元。
            _gamepadController.RightPressed += MoveCursorRight;

            // 控制器 → 鍵長按重複：將游標連續向右移動一個字元。
            _gamepadController.RightRepeat += MoveCursorRight;

            // 控制器 Start 鍵（情境智慧切換）：
            // - 若文字方塊「未」取得焦點：將焦點拉回文字方塊（焦點救援）。
            // - 若文字方塊「已」取得焦點：功能等同控制器 A 鍵（開啟觸控式鍵盤或複製文字）。
            _gamepadController.StartPressed += () =>
            {
                this.SafeInvoke(() => 
                {
                    if (TBInput.CanFocus &&
                        !TBInput.Focused)
                    {
                        TBInput.Focus();
                    }
                    else
                    {
                        ExecuteConfirmAction();
                    }
                });
            };

            // 控制器 Back 鍵：嘗試將焦點切換回先前的前景視窗。
            _gamepadController.BackPressed += () =>
            {
                this.SafeInvoke(() =>
                {
                    HandleReturnToPreviousWindowSafeAsync().SafeFireAndForget();
                });
            };

            // 控制器 A 鍵 = Enter 鍵：
            // - 若文字方塊為空，開啟觸控式鍵盤開始輸入。
            // - 若文字方塊已有文字，執行複製到剪貼簿並完成輸入流程。
            _gamepadController.APressed += () =>
            {
                this.SafeInvoke(() =>
                {
                    ExecuteConfirmAction();
                });
            };

            // 控制器 B 鍵：
            // - 單獨按下時清除輸入內容（等同 Esc 鍵）。
            // - 同時按住 LB + RB 時，嘗試將焦點切換回先前的前景視窗。
            _gamepadController.BPressed += () =>
            {
                this.SafeInvoke(() =>
                {
                    if (_gamepadController.IsLeftShoulderHeld &&
                        _gamepadController.IsRightShoulderHeld)
                    {
                        HandleReturnToPreviousWindowSafeAsync().SafeFireAndForget();
                    }
                    else
                    {
                        ClearInput();
                    }
                });
            };

            // 控制器 X 鍵：刪除游標前一個字元（等同鍵盤 Backspace 鍵）。
            _gamepadController.XPressed += () =>
            {
                this.SafeInvoke(() =>
                {
                    if (TBInput.SelectionStart > 0)
                    {
                        int position = TBInput.SelectionStart;

                        TBInput.Text = TBInput.Text.Remove(position - 1, 1);
                        TBInput.SelectionStart = position - 1;
                    }
                });
            };
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"控制器初始化失敗：{ex.Message}");
        }
    }

    /// <summary>
    /// 執行確認操作（共用於 A 鍵與 Start 鍵）
    /// </summary>
    private void ExecuteConfirmAction()
    {
        if (string.IsNullOrWhiteSpace(TBInput.Text))
        {
            ShowTouchKeyboard();

            return;
        }

        if (!BtnCopy.Enabled)
        {
            return;
        }

        BtnCopy.PerformClick();
    }

    /// <summary>
    /// 處理 KeyDown
    /// </summary>
    /// <param name="e">KeyEventArgs</param>
    private void HandleKeyDown(KeyEventArgs e)
    {
        // Esc：清除。
        if (e.KeyCode == Keys.Escape)
        {
            ClearInput();

            e.SuppressKeyPress = true;

            return;
        }

        // ↑：上一筆。
        if (e.KeyCode == Keys.Up)
        {
            NavigateHistory(-1);

            e.SuppressKeyPress = true;

            return;
        }

        // ↓：下一筆。
        if (e.KeyCode == Keys.Down)
        {
            NavigateHistory(+1);

            e.SuppressKeyPress = true;
        }
    }

    /// <summary>
    /// 重置按鈕狀態（延遲後執行）
    /// </summary>
    private async Task ResetButtonStateAsync()
    {
        // 等待設定的時間。
        await Task.Delay(Delay_ButtonReset);

        // 檢查物件是否還存在（這是 async 處理 UI 必備的防護）。
        if (IsDisposed ||
            BtnCopy == null)
        {
            return;
        }

        // 執行 UI 更新（自動回到 UI 執行緒）。
        BtnCopy.Text = Strings.Btn_CopyDefault;
        BtnCopy.AccessibleDescription = Strings.Btn_CopyDefault;
        BtnCopy.Enabled = true;
    }

    /// <summary>
    /// 讓控制器震動
    /// </summary>
    /// <param name="profile">VibrationProfile</param>
    /// <returns>Task</returns>
    private Task VibrateAsync(VibrationProfile profile)
    {
        // 委派給 Service 處理。
        return _feedbackService.VibrateAsync(_gamepadController, profile);
    }

    /// <summary>
    /// 游標左移
    /// </summary>
    private void MoveCursorLeft()
    {
        this.SafeInvoke(() =>
        {
            if (TBInput.SelectionStart > 0)
            {
                TBInput.SelectionStart--;
                TBInput.ScrollToCaret();
            }
            else
            {
                // 只有在撞牆時才震動，避免長按時一直震動
                VibrateAsync(VibrationPatterns.CursorMove).SafeFireAndForget();
            }
        });
    }

    /// <summary>
    /// 游標右移
    /// </summary>
    private void MoveCursorRight()
    {
        this.SafeInvoke(() =>
        {
            if (TBInput.SelectionStart < TBInput.Text.Length)
            {
                TBInput.SelectionStart++;
                TBInput.ScrollToCaret();
            }
            else
            {
                // 只有在撞牆時才震動，避免長按時一直震動。
                VibrateAsync(VibrationPatterns.CursorMove).SafeFireAndForget();
            }
        });
    }

    /// <summary>
    /// 清除文字方塊
    /// </summary>
    private void ClearInput()
    {
        _feedbackService.PlaySound(SystemSounds.Beep);

        VibrateAsync(VibrationPatterns.ClearInput).SafeFireAndForget();

        TBInput.Clear();

        // 重置 InputHistoryManager 索引值。
        _historyService.ResetIndex();

        AnnounceA11y(Strings.Msg_InputCleared);
    }

    /// <summary>
    /// 導覽輸入歷程記錄
    /// </summary>
    /// <param name="direction">數值</param>
    private void NavigateHistory(int direction)
    {
        InputHistoryService.NavigationResult navigationResult = _historyService.Navigate(direction);

        // 處理震動（邊界或錯誤）。
        if (navigationResult.IsBoundaryHit)
        {
            VibrateAsync(VibrationPatterns.ActionFail).SafeFireAndForget();
        }

        // 處理文字更新。
        if (navigationResult.IsCleared)
        {
            TBInput.Clear();
        }
        else if (navigationResult.Success &&
            navigationResult.Text != null)
        {
            TBInput.Text = navigationResult.Text;
            // 游標移到最後。
            TBInput.SelectionStart = TBInput.Text.Length;
            TBInput.ScrollToCaret();
        }
    }

    /// <summary>
    /// 顯示觸控式鍵盤
    /// </summary>
    private void ShowTouchKeyboard()
    {
        if ((DateTime.UtcNow - _lastTouchKeyboardOpened).TotalMilliseconds < 500)
        {
            return;
        }

        _lastTouchKeyboardOpened = DateTime.UtcNow;

        // 確保文字方塊取得焦點。
        if (TBInput.CanFocus &&
            !TBInput.Focused)
        {
            TBInput.Focus();
        }

        try
        {
            _isShowingTouchKeyboard = true;

            bool isTouchKeyboardOpened = _touchKeyboardService.TryOpen();

            if (isTouchKeyboardOpened)
            {
                _feedbackService.PlaySound(SystemSounds.Asterisk);
            }
            else
            {
                // 找不到檔案時的處理。
                _feedbackService.PlaySound(SystemSounds.Hand);

                VibrateAsync(VibrationPatterns.ActionFail).SafeFireAndForget();

                // 錯誤時觸發視覺閃爍。
                FlashAlertAsync().SafeFireAndForget();

                AnnounceA11y(Strings.Err_TouchKeyboardNotFound);
            }
        }
        catch
        {
            _feedbackService.PlaySound(SystemSounds.Hand);

            VibrateAsync(VibrationPatterns.ActionFail).SafeFireAndForget();

            // 錯誤時觸發視覺閃爍。
            FlashAlertAsync().SafeFireAndForget();

            AnnounceA11y(Strings.Err_TouchKeyboardNotFound);
        }
        finally
        {
            // 改為呼叫獨立的非同步方法，並加上 _ = 捨棄回傳值以防堵編譯警告。
            _ = ResetTouchKeyboardFlagAsync();
        }
    }

    /// <summary>
    /// 顯示輸入
    /// </summary>
    public void ShowForInput()
    {
        // 捕捉目前視窗。
        _windowFocusService.CaptureCurrentWindow();

        // 顯示視窗。
        Show();

        // 還原視窗。
        Win32.ShowWindow(Handle, Win32.SW_RESTORE);

        // 帶至前方。
        BringToFront();

        // 啟用視窗。
        Activate();

        // 確保文字方塊取得焦點。
        TBInput.Focus();

        _feedbackService.PlaySound(SystemSounds.Asterisk);

        VibrateAsync(VibrationPatterns.ShowInput).SafeFireAndForget();
    }

    /// <summary>
    /// 重置觸控式鍵盤顯示旗標
    /// </summary>
    /// <returns>Task</returns>
    private async Task ResetTouchKeyboardFlagAsync()
    {
        try
        {
            await Task.Delay(AppSettings.Current.TouchKeyboardDismissDelay);

            _isShowingTouchKeyboard = false;
        }
        catch
        {
            // 忽略因視窗關閉或取消引發的錯誤。
        }
    }

    /// <summary>
    /// 返回前一個視窗
    /// </summary>
    /// <returns>Task</returns>
    private async Task ReturnToPreviousWindowAsync()
    {
        // 呼叫導航服務，並傳入目前的控制器實例以進行安全檢查與震動。
        await _navigationService.NavigateBackAsync(_gamepadController);
    }

    /// <summary>
    /// 讓輸入框邊框閃爍
    /// </summary>
    /// <returns>Task</returns>
    private async Task FlashAlertAsync()
    {
        if (!SystemInformation.UIEffectsEnabled ||
            _isFlashing ||
            IsDisposed ||
            !IsHandleCreated)
        {
            return;
        }

        _isFlashing = true;

        try
        {
            // 決定閃爍顏色。
            Color flashColor;

            // 檢查系統是否開啟了「高對比模式」。
            if (SystemInformation.HighContrast)
            {
                // 在高對比模式下，使用系統定義的「選取文字顏色」，
                // 這通常是高亮度的（如鮮黃色、青色或白色），在黑色背景下極為醒目。
                flashColor = SystemColors.HighlightText;
            }
            else
            {
                // 一般模式：改用 OrangeRed，
                // 紅色盲看 OrangeRed 會像「亮黃色／亮褐色」，與原本灰色的對比度遠高於純紅。
                flashColor = Color.OrangeRed;
            }

            int flashCount = 3,
                // 稍微放慢速度到 150ms，讓眼睛更容易捕捉變化。
                interval = 150;

            for (int i = 0; i < flashCount; i++)
            {
                // 變更顏色。
                PInputHost.BackColor = flashColor;

                await Task.Delay(interval);

                if (IsDisposed)
                {
                    return;
                }

                // 還原顏色。
                UpdateBorderColor();

                await Task.Delay(interval);
            }
        }
        finally
        {
            _isFlashing = false;

            if (!IsDisposed)
            {
                UpdateBorderColor();
            }
        }
    }

    /// <summary>
    /// 依據焦點狀態更新邊框顏色
    /// </summary>
    private void UpdateBorderColor()
    {
        if (TBInput.Focused)
        {
            // 取得焦點時：邊框變為系統高亮色（通常是藍色）。
            PInputHost.BackColor = SystemColors.Highlight;
        }
        else
        {
            // 失去焦點時：邊框變回一般的灰色。
            PInputHost.BackColor = SystemColors.ControlDark;
        }
    }

    /// <summary>
    /// 安全地處理返回前一個視窗的流程，避免重複觸發導致的問題
    /// </summary>
    /// <returns>Task</returns>
    private async Task HandleReturnToPreviousWindowSafeAsync()
    {
        if (_isReturning)
        {
            return;
        }

        _isReturning = true;

        try
        {
            await ReturnToPreviousWindowAsync();
        }
        finally
        {
            _isReturning = false;
        }
    }

    /// <summary>
    /// 初始化無障礙廣播用的隱藏標籤
    /// </summary>
    /// <remarks>
    /// WinForms 沒有原生的 Live Region，透過一個螢幕外的 Label 搭配 NameChange 事件
    /// 來模擬即時朗讀，且不搶佔輸入焦點。
    /// </remarks>
    private void InitializeA11yAnnouncer()
    {
        _lblA11yAnnouncer = new AnnouncerLabel
        {
            // 設定名稱以便識別。
            Name = "LblA11yAnnouncer",
            // 必須設為 Visible，螢幕閱讀器才抓得到。
            Visible = true,
            // 自動調整大小
            AutoSize = true,
            // 移出可視範圍，避免影響視覺排版。
            Location = new Point(-10000, -10000),
            // 設定為 StaticText 角色。
            AccessibleRole = AccessibleRole.StaticText,
            // 避免被 Tab 鍵選中。
            TabStop = false,
            TabIndex = 0,
            // 加入至表單控制項集合。
            Parent = this
        };
    }

    /// <summary>
    /// 發送無障礙廣播訊息
    /// </summary>
    /// <param name="message">要朗讀的訊息</param>
    private void AnnounceA11y(string message)
    {
        this.SafeInvoke(() =>
        {
            _lblA11yAnnouncer?.Announce(message);
        });
    }
}