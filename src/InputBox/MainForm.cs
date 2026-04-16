using InputBox.Core.Configuration;
using InputBox.Core.Controls;
using InputBox.Core.Extensions;
using InputBox.Core.Feedback;
using InputBox.Core.Input;
using InputBox.Core.Interop;
using InputBox.Core.Services;
using InputBox.Core.Utilities;
using InputBox.Resources;
using Microsoft.Win32;
using System.Diagnostics;
using System.Media;

namespace InputBox;

public partial class MainForm : Form
{
    /// <summary>
    /// 標題快取同步鎖
    /// </summary>
    private readonly Lock _titleLock = new();

    /// <summary>
    /// 輸入歷程服務，負責管理一般模式下的歷史文字記錄。
    /// </summary>
    private readonly InputHistoryService _historyService;

    /// <summary>
    /// 片語服務，負責載入、儲存與查詢使用者自訂片語。
    /// </summary>
    private readonly PhraseService _phraseService;

    /// <summary>
    /// 視窗焦點服務，封裝前景視窗切換與焦點恢復邏輯。
    /// </summary>
    private readonly WindowFocusService _windowFocusService;

    /// <summary>
    /// 視窗導覽服務，處理返回目標視窗等操作流程。
    /// </summary>
    private readonly WindowNavigationService _navigationService;

    /// <summary>
    /// 目前表單使用的輸入上下文，提供控制器與 UI 互動所需資訊。
    /// </summary>
    private FormInputContext? _inputContext;

    /// <summary>
    /// 目前啟用的遊戲控制器後端實例，可能為 XInput 或 GameInput。
    /// </summary>
    private IGamepadController? _gamepadController;

    /// <summary>
    /// 表單輸入狀態管理器（集中管理原子旗標）
    /// </summary>
    private readonly FormInputStateManager _inputState = new();

    /// <summary>
    /// 輸入框標籤（用於 A11y 關聯）
    /// </summary>
    private Label? _lblInput;

    /// <summary>
    /// A11y 廣播用的標籤
    /// </summary>
    private AnnouncerLabel? _lblA11yAnnouncer;

    /// <summary>
    /// 記錄上一次開啟觸控式鍵盤的時間點，用於防抖
    /// </summary>
    private DateTime _lastTouchKeyboardOpened = DateTime.MinValue;

    /// <summary>
    /// 抑制控制器返回行為至指定 UTC Ticks（避免切回前景瞬間誤觸返回）
    /// </summary>
    private long _suppressGamepadReturnUntilUtcTicks;

    /// <summary>
    /// Back 鍵按下-放開配對閂鎖（1=已觀察到按下，允許一次放開；0=忽略放開）
    /// </summary>
    private int _backReleaseArmed;

    /// <summary>
    /// 進入快速鍵擷取模式前的輸入框描述快取（用於對稱還原 A11y 狀態）
    /// </summary>
    private string? _tbInputAccessibleDescriptionBeforeCapture;

    /// <summary>
    /// 用於管理視窗生命週期內所有非同步任務的取消權杖來源
    /// </summary>
    private CancellationTokenSource? _formCts = new();

    /// <summary>
    /// 用於管理警示動畫（FlashAlert）的中斷控制
    /// </summary>
    private CancellationTokenSource? _alertCts;

    /// <summary>
    /// 控制器初始化鎖（防止重複建立實例）
    /// </summary>
    private SemaphoreSlim? _gamepadInitLock = new(1, 1);

    /// <summary>
    /// 右鍵選單
    /// </summary>
    private ContextMenuStrip? _cmsInput;

    /// <summary>
    /// 隱私模式選單項
    /// </summary>
    private ToolStripMenuItem? _tsmiPrivacyMode;

    /// <summary>
    /// 允許中斷廣播選單項
    /// </summary>
    private ToolStripMenuItem? _tsmiA11yInterrupt;

    /// <summary>
    /// 動畫式視覺警示選單項
    /// </summary>
    private ToolStripMenuItem? _tsmiAnimatedVisualAlerts;

    /// <summary>
    /// 返回時最小化選單項
    /// </summary>
    private ToolStripMenuItem? _tsmiMinimizeOnReturn;

    /// <summary>
    /// 片語子選單項
    /// </summary>
    private ToolStripMenuItem? _tsmiPhrases;

    /// <summary>
    /// 上一次的遊戲控制器連線狀態（用於防止重複廣播）
    /// </summary>
    private bool? _lastGamepadConnectedState;

    /// <summary>
    /// 上一次已播報的 Face 鍵配置模式，用於避免控制器重連時重複報讀相同的配置資訊。
    /// </summary>
    private AppSettings.GamepadFaceButtonMode? _lastAnnouncedGamepadFaceButtonMode;

    /// <summary>
    /// 剪貼簿重試回呼的訂閱老據（用於提交時正確取消訂閱）
    /// </summary>
    private readonly Action? _onClipboardRetry;

    /// <summary>
    /// 專案預設字型家族（初始化後由 InitA11y 設定，供所有對話框共用）
    /// </summary>
    private static FontFamily? _defaultFontFamily;

    /// <summary>
    /// 統一放大的 A11y 字型（實例引用）
    /// </summary>
    private Font A11yFont => FontResourceManager.GetSharedA11yFont(
        DeviceDpi,
        FontStyle.Regular,
        BtnCopy?.Font?.FontFamily);

    /// <summary>
    /// 快取的加粗字型（實例引用）
    /// </summary>
    private Font BoldBtnFont => FontResourceManager.GetSharedA11yFont(
        DeviceDpi,
        FontStyle.Bold,
        BtnCopy?.Font?.FontFamily);

    /// <summary>
    /// 快取的視窗標題前綴（包含標題、隱私狀態與快速鍵）
    /// </summary>
    private string _cachedTitlePrefix = string.Empty;

    /// <summary>
    /// 啟動時的需重啟設定快照。
    /// </summary>
    private readonly RestartRequirementSnapshot _restartRequirementSnapshot;

    /// <summary>
    /// 目前待處理的重啟原因旗標。
    /// </summary>
    private RestartPendingReason CurrentRestartPendingReason =>
        _restartRequirementSnapshot.GetPendingReason(
            AppSettings.Current,
            this.IsDarkModeActive(),
            SystemInformation.HighContrast);

    /// <summary>
    /// 判斷是否仍有待處理的重啟需求（主題、控制器輸入 API 或歷程容量）。
    /// </summary>
    private bool IsRestartUpdatePending => CurrentRestartPendingReason != RestartPendingReason.None;

    /// <summary>
    /// 依目前待重啟原因動態產生右鍵選單中的重啟項目文字。
    /// </summary>
    private string RestartMenuLabel => RestartMenuTextResolver.GetMenuLabel(CurrentRestartPendingReason);

    /// <summary>
    /// 依目前待重啟原因動態產生右鍵選單中的重啟項目無障礙描述。
    /// </summary>
    private string RestartMenuAccessibleDescription => RestartMenuTextResolver.GetAccessibleDescription(CurrentRestartPendingReason);

    /// <summary>
    /// 啟動時是否應強制把主視窗重新帶回前景（例如由本程式要求的重新啟動）。
    /// </summary>
    private readonly bool _forceForegroundOnFirstShow;

    /// <summary>
    /// 是否已進入應用程式重新啟動流程，用於略過失焦時的返回目標捕捉。
    /// </summary>
    private int _isRestartingApplication;

    /// <summary>
    /// 上一次套用佈局時的 DPI 值（用於防抖）
    /// </summary>
    private float _lastAppliedDpi = -1;

    /// <summary>
    /// 上一次建立游標時的寬度快取（-1 表示未初始化，與 Leave 事件重置語意一致）
    /// </summary>
    private int _lastCaretWidth = -1;

    /// <summary>
    /// 上一次建立游標時的高度快取（-1 表示未初始化，與 Leave 事件重置語意一致）
    /// </summary>
    private int _lastCaretHeight = -1;

    /// <summary>
    /// 冷卻旗標，防止在短時間內重複觸發按鈕動作（例如連續點擊或快速鍵）
    /// </summary>
    private bool _isActionCooldown = false;

    /// <summary>
    /// 記錄最後一個獲得焦點或被選取的選單項目，用於對話框關閉後的焦點還原
    /// </summary>
    private ToolStripItem? _lastFocusedMenuItem = null;

    /// <summary>
    /// 右搖桿虛擬選取的起點錨點
    /// 當目前沒有選取範圍時，此值為 null
    /// </summary>
    private int? _rsSelectionAnchor = null;

    /// <summary>
    /// 初始化主視窗。
    /// </summary>
    /// <param name="forceForegroundOnFirstShow">若為 true，表示此執行個體是由程式主動重啟喚起，首次顯示時需額外重試搶回前景焦點。</param>
    public MainForm(bool forceForegroundOnFirstShow = false)
    {
        InitializeComponent();

        // 記錄啟動時是否需要執行一次性的前景搶回流程。
        _forceForegroundOnFirstShow = forceForegroundOnFirstShow;

        // 記錄啟動時所有需重啟才會完全生效的基準狀態。
        bool initialIsDarkMode = this.IsDarkModeActive();
        bool initialHighContrast = SystemInformation.HighContrast;
        _restartRequirementSnapshot = RestartRequirementSnapshot.CaptureCurrent(initialIsDarkMode, initialHighContrast);

        Disposed += (s, e) =>
        {
            // 若直接呼叫 Dispose() 跳過 OnFormClosing，仍需先取消再釋放，
            // 確保等待此 Token 的非同步工作都能收到取消訊號。
            Interlocked.Exchange(ref _formCts, null)?.CancelAndDispose();
        };

        // 套用全域震動強度設定。
        VibrationPatterns.GlobalIntensityMultiplier = AppSettings.Current.VibrationIntensity;

        // 使用設定檔容量初始化 InputHistoryManager。
        _historyService = new InputHistoryService(AppSettings.Current.HistoryCapacity)
        {
            IsPrivacyMode = AppSettings.Current.IsPrivacyMode
        };
        _phraseService = new PhraseService();
        _phraseService.Load();
        _windowFocusService = new WindowFocusService();
        _navigationService = new WindowNavigationService(_windowFocusService);

        // 初始化標題快取。
        UpdateTitlePrefix();

        // 初始化無障礙廣播。
        InitializeA11yAnnouncer();

        // 註冊背景任務全域例外處理。
        Core.Extensions.TaskExtensions.GlobalExceptionHandler = (ex) =>
        {
            this.SafeInvoke(() => AnnounceA11y(string.Format(Strings.A11y_Background_Error, ex.Message)));
        };

        // 初始化右鍵選單。
        InitializeContextMenu();

        // 確保在選單建立後立即填充動態文字與在地化標籤。
        RefreshMenu();

        // 綁定剪貼簿重試通知。
        _onClipboardRetry = () => AnnounceA11y(Strings.A11y_Clipboard_Retrying);
        ClipboardService.OnRetry += _onClipboardRetry;

        // 設定預設動作按鈕，支援 A11y 視覺引導。
        AcceptButton = BtnCopy;

        // 限制輸入字數，與 InputHistoryService 的上限保持一致。
        TBInput.MaxLength = AppSettings.MaxInputLength;

        // 精確的滑鼠滾輪導覽歷程。
        TBInput.MouseWheel += (s, e) =>
        {
            // 攔截並阻斷系統多倍捲動與 TextBox 原生捲動行為。
            if (e is HandledMouseEventArgs hme)
            {
                hme.Handled = true;
            }

            // 向上捲動 = 往前找舊的（direction：-1），
            // 向下捲動 = 往後找新的（direction：1）。
            // 強制鎖定為 Step 1（一格跳一筆）。
            if (e.Delta > 0)
            {
                NavigateHistory(-1);
            }
            else if (e.Delta < 0)
            {
                NavigateHistory(+1);
            }
        };

        // 在 InputBox 不在前景時持續追蹤外部前景視窗，
        // 可修補滑鼠／Alt+Tab 手動切回 InputBox 時 WM_ACTIVATE lParam 為 0 的情境。
        StartExternalForegroundTrackingLoop();
    }

    /// <summary>
    /// 背景追蹤外部前景視窗，作為返回目標更新來源
    /// </summary>
    private void StartExternalForegroundTrackingLoop()
    {
        Task.Run(async () =>
        {
            CancellationToken token = _formCts?.Token ?? CancellationToken.None;

            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(80, token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                nint foregroundHwnd = User32.ForegroundWindow;

                if (foregroundHwnd == IntPtr.Zero)
                {
                    continue;
                }

                _ = User32.GetWindowThreadProcessId(foregroundHwnd, out uint processId);

                // 只追蹤外部前景視窗，避免覆寫為本程式內視窗。
                if (processId == 0 ||
                    processId == Environment.ProcessId)
                {
                    continue;
                }

                bool captured = _windowFocusService.TryCaptureWindow(foregroundHwnd);

#if DEBUG
                if (captured)
                {
                    Debug.WriteLine($"[前景追蹤] 已捕捉 前景視窗={foregroundHwnd}");
                }
#endif
            }
        }, _formCts?.Token ?? CancellationToken.None).SafeFireAndForget();
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == (int)User32.WindowMessage.Activate)
        {
            const int WA_INACTIVE = 0;

            // WM_ACTIVATE 的低位表示啟用狀態；非 0 代表本視窗正要成為啟用視窗。
            int activateState = m.WParam.ToInt32() & 0xFFFF;

#if DEBUG
            Debug.WriteLine($"[視窗訊息] WM_ACTIVATE 狀態={activateState} 前一視窗={m.LParam}");
#endif

            // lParam 在啟用時為「先前啟用視窗」Handle。
            // 這可涵蓋 Alt + Tab／滑鼠切換等非全域熱鍵進入路徑。
            if (activateState != WA_INACTIVE &&
                m.LParam != IntPtr.Zero)
            {
                bool captured = _windowFocusService.TryCaptureWindow(m.LParam);

#if DEBUG
                Debug.WriteLine($"[視窗訊息] WM_ACTIVATE 嘗試捕捉 前一視窗={m.LParam} 已捕捉={captured}");
#endif
            }
        }

        if (m.Msg == (int)User32.WindowMessage.HotKey &&
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

        // 當使用者用滑鼠／Alt+Tab 切回時，先短暫抑制返回，
        // 避免控制器狀態邊緣（例如 BackReleased）在恢復輪詢瞬間誤觸返回。
        SuppressGamepadReturnForActivationWindow();

        // 切回前景時清除 Back 放開配對，避免恢復輪詢瞬間的孤兒 BackReleased 事件誤觸返回。
        Interlocked.Exchange(ref _backReleaseArmed, 0);

        _gamepadController?.Resume();
    }

    /// <summary>
    /// 建立切回前景後的短抑制窗，降低控制器邊緣事件誤觸返回風險
    /// </summary>
    private void SuppressGamepadReturnForActivationWindow()
    {
        long untilTicks = DateTime.UtcNow.AddMilliseconds(300).Ticks;

        Interlocked.Exchange(ref _suppressGamepadReturnUntilUtcTicks, untilTicks);
    }

    /// <summary>
    /// 是否仍在控制器返回抑制窗內
    /// </summary>
    /// <returns>若在抑制窗內回傳 true。</returns>
    private bool IsGamepadReturnSuppressed()
    {
        long untilTicks = Interlocked.Read(ref _suppressGamepadReturnUntilUtcTicks);

        return untilTicks > DateTime.UtcNow.Ticks;
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);

        // 如果是因為正在呼叫觸控小鍵盤而失去焦點，則不進行任何處理（保留視覺狀態與控制器輪詢）。
        if (_inputState.IsShowingTouchKeyboard)
        {
            return;
        }

        // 程式內部主動要求重啟時，不應把焦點空窗誤記錄成「返回上一個視窗」目標。
        if (Volatile.Read(ref _isRestartingApplication) != 0)
        {
            return;
        }

        // 備援捕捉：部分環境下 WM_ACTIVATE 的 lParam 可能為 0，
        // 因此在失焦後短延遲再讀取前景視窗，確保返回目標可建立。
        CaptureForegroundWindowAfterDeactivate();

        // 使用 SafeBeginInvoke 延遲執行，避開 ShowDialog 瞬間的焦點空窗期，並確保在 UI 執行緒操作。
        this.SafeBeginInvoke(() =>
        {
            try
            {
                // 清除 UI 視覺殘留。
                // 確保視窗在背景或被對話框遮擋時，不會殘留 Hover 的灰底或 Focus 的邊框。

                // 清除輸入框的視覺殘留。
                if (TBInput != null &&
                    !TBInput.IsDisposed &&
                    !TBInput.Focused)
                {
                    UpdateBorderColor(false);

                    TBInput.BackColor = Color.Empty;
                    TBInput.ForeColor = Color.Empty;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[事件] 清除視窗失焦視覺殘留失敗：{ex.Message}");
            }

            // 如果還有其他視窗（例如數值輸入對話框）是活躍的，代表應用程式還在前景，不應停止控制器輪詢。
            if (ActiveForm != null)
            {
                return;
            }

            // 當整個應用程式完全退到背景時，停止震動並暫停控制器輪詢。
            FeedbackService.StopAllVibrationsAsync(_gamepadController).SafeFireAndForget();

            _gamepadController?.Pause();
        });
    }

    /// <summary>
    /// 失焦後短延遲捕捉前景視窗，作為返回目標備援
    /// </summary>
    private void CaptureForegroundWindowAfterDeactivate()
    {
        Task.Run(async () =>
        {
            try
            {
                // 讓系統先完成焦點轉移，避免讀到仍為目前視窗的瞬時狀態。
                await Task.Delay(50, _formCts?.Token ?? CancellationToken.None);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            nint foregroundHwnd = User32.ForegroundWindow;

            bool captured = _windowFocusService.TryCaptureWindow(foregroundHwnd);

#if DEBUG
            Debug.WriteLine($"[失焦事件] 備援捕捉 前景視窗={foregroundHwnd} 已捕捉={captured}");
#endif
        }, _formCts?.Token ?? CancellationToken.None).SafeFireAndForget();
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        try
        {
            GlobalHotKeyService.UnregisterShowInputHotkey(Handle);

            // 確保靜態事件在視窗控制項控制代碼銷毀時被絕對釋放。
            SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
        }
        finally
        {
            base.OnHandleDestroyed(e);
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);

        if (e.Cancel)
        {
            return;
        }

        // 立即發出全域取消訊號，中止所有 UI 相關的非同步任務，並處置控制代碼。
        Interlocked.Exchange(ref _formCts, null)?.CancelAndDispose();

        // 停止 A11y 廣播服務。
        AnnouncementService? announcementService = Interlocked.Exchange(ref _announcementService, null);

        announcementService?.Dispose();

        // 非同步硬體緊急清理。
        // 將同步的硬體 I/O 操作（如 XInput 設定）移至背景執行，防止阻塞 UI 執行緒導致關閉停頓。
        Task.Run(() =>
        {
            try
            {
                FeedbackService.EmergencyStopAllActiveControllers();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"背景硬體清理失敗，已忽略：{ex.Message}");
            }
        }).SafeFireAndForget();

        try
        {
            // 優先停止輸入源。
            IGamepadController? gamepad = Interlocked.Exchange(ref _gamepadController, null);

            gamepad?.Dispose();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"控制器釋放失敗，已忽略：{ex.Message}");
        }

        // 原子性清理其餘權杖資源。
        Interlocked.Exchange(ref _alertCts, null)?.CancelAndDispose();

        // 處置 UI 相關 Label 以防 GDI 洩漏。
        Label? lblInput = Interlocked.Exchange(ref _lblInput, null);

        lblInput?.Dispose();

        AnnouncerLabel? lblA11y = Interlocked.Exchange(ref _lblA11yAnnouncer, null);

        lblA11y?.Dispose();

        FormInputContext? inputCtx = Interlocked.Exchange(ref _inputContext, null);

        inputCtx?.Dispose();

        SemaphoreSlim? initLock = Interlocked.Exchange(ref _gamepadInitLock, null);

        initLock?.Dispose();

        ContextMenuStrip? cmsInput = Interlocked.Exchange(ref _cmsInput, null);

        cmsInput?.Dispose();

        // 原子化清除輸入框字型參考（共用字型不呼叫 Dispose）。
        Interlocked.Exchange(ref _inputFont, null);

        // 確保快速鍵擷取旗標正確歸零。
        _inputState.EndHotkeyCapture();

        // 解除靜態委派，防止記憶體洩漏。
        Core.Extensions.TaskExtensions.GlobalExceptionHandler = null;

        ClipboardService.OnRetry -= _onClipboardRetry;
    }

    /// <summary>
    /// 處理系統層級的命令鍵（快速鍵）
    /// </summary>
    /// <remarks>
    /// 覆寫此方法能確保 Alt 組合鍵在事件到達控制項（如 TextBox）前被攔截。
    /// 這解決了 WinForms 在按下 Alt 鍵時會將焦點移向選單列（Menu Bar）導致 KeyDown 事件遺失的問題，
    /// 同時也能在全視窗範圍內提供穩定、且不干擾按鈕助記鍵（如 Alt + A）的操作體驗。
    /// </remarks>
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (HandleCaptureModeCmdKey(keyData))
        {
            return true;
        }

        if (HandleGlobalCmdKey(keyData))
        {
            return true;
        }

        InputBoxCmdResult inputBoxResult = HandleInputBoxCmdKey(keyData);

        if (inputBoxResult == InputBoxCmdResult.Handled)
        {
            return true;
        }

        if (inputBoxResult == InputBoxCmdResult.ForwardToBase)
        {
            return base.ProcessCmdKey(ref msg, keyData);
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    /// <summary>
    /// 處理快速鍵擷取模式下的命令鍵
    /// </summary>
    /// <param name="keyData">目前命令鍵組合。</param>
    /// <returns>若命令鍵已處理則回傳 true。</returns>
    private bool HandleCaptureModeCmdKey(Keys keyData)
    {
        if (!_inputState.IsHotkeyCaptureActive)
        {
            return false;
        }

        Keys key = keyData & Keys.KeyCode;

        if (key == Keys.Escape)
        {
            RestoreUIFromCaptureMode();

            AnnounceA11y(Strings.A11y_Capture_Cancelled);

            FeedbackService.PlaySound(SystemSounds.Beep);

            return true;
        }

        if (key != Keys.ControlKey &&
            key != Keys.ShiftKey &&
            key != Keys.Menu &&
            key != Keys.LWin &&
            key != Keys.RWin &&
            key != Keys.None &&
            key != Keys.ProcessKey)
        {
            string oldKey = AppSettings.Current.HotKeyKey;
            AppSettings.Current.HotKeyKey = key.ToString();

            if (RegisterHotKeyInternal())
            {
                AppSettings.Save();

                RestoreUIFromCaptureMode();

                string fullHotkeyStr = GlobalHotKeyService.GetHotKeyDisplayString();

                UpdateTitle($"{Strings.Msg_HotkeyUpdated}: {fullHotkeyStr}");

                BtnCopy.Enabled = false;
                BtnCopy.Text = Strings.Msg_HotkeyUpdated;

                async Task DelayedAnnounce()
                {
                    try
                    {
                        await Task.Delay(
                            1200,
                            _formCts?.Token ?? CancellationToken.None);

                        if (IsDisposed)
                        {
                            return;
                        }

                        AnnounceA11y(string.Format(Strings.A11y_Hotkey_Captured, fullHotkeyStr));
                    }
                    catch (OperationCanceledException)
                    {

                    }
                }

                DelayedAnnounce().SafeFireAndForget();

                FeedbackService.PlaySound(SystemSounds.Asterisk);
            }
            else
            {
                AppSettings.Current.HotKeyKey = oldKey;

                RegisterHotKeyInternal();

                RestoreUIFromCaptureMode();

                UpdateTitle(Strings.Err_Title);

                BtnCopy.Enabled = false;
                BtnCopy.Text = Strings.Err_Title;

                AnnounceA11y(Strings.Err_HotkeyRegFail_Brief);

                FeedbackService.PlaySound(SystemSounds.Hand);
            }

            _ = ResetButtonStateAsync();

            Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(
                        1500,
                        _formCts?.Token ?? CancellationToken.None);

                    this.SafeInvoke(() =>
                    {
                        if (IsDisposed)
                        {
                            return;
                        }

                        UpdateTitle();
                    });
                }
                catch (OperationCanceledException)
                {

                }

            },
            _formCts?.Token ?? CancellationToken.None).SafeFireAndForget();

            return true;
        }

        string strA11yMsg = $"{Strings.Msg_PressAnyKey} ({Strings.A11y_Capture_Esc_Cancel})";

        AnnounceA11y(strA11yMsg, interrupt: true);

        VibrateAsync(VibrationPatterns.CursorMove).SafeFireAndForget();

        return true;
    }

    /// <summary>
    /// 處理全域命令鍵分派。
    /// </summary>
    /// <param name="keyData">目前命令鍵組合。</param>
    /// <returns>若命令鍵已處理則回傳 true。</returns>
    private bool HandleGlobalCmdKey(Keys keyData)
    {
        if (!CmdKeyDispatcher.TryHandleGlobal(
            keyData,
            onReturnPreviousWindow: () => HandleReturnToPreviousWindowSafeAsync().SafeFireAndForget(),
            onAdjustOpacity: AdjustOpacity,
            onResetOpacity: ResetOpacity,
            onTogglePrivacyMode: TogglePrivacyMode,
            onShowContextMenu: () => this.SafeInvoke(ShowContextMenuAtInput),
            canFocusInput: () => TBInput.CanFocus,
            onFocusInput: () => TBInput.Focus(),
            onAnnounceSkipNav: () => AnnounceA11y(Strings.A11y_SkipNav_JumpToInput)))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// 處理輸入框焦點相關命令鍵分派。
    /// </summary>
    /// <param name="keyData">目前命令鍵組合。</param>
    /// <returns>輸入框命令鍵處理結果。</returns>
    private InputBoxCmdResult HandleInputBoxCmdKey(Keys keyData)
    {
        return CmdKeyDispatcher.HandleInputBox(
            keyData,
            ActiveControl,
            TBInput,
            onShowTouchKeyboard: ShowTouchKeyboard,
            onConfirm: BtnCopy.PerformClick,
            onAnnounceNewLine: () => AnnounceA11y(Strings.A11y_New_Line),
            onVibrateCursorMove: () => VibrateAsync(VibrationPatterns.CursorMove).SafeFireAndForget());
    }

    protected override void OnMove(EventArgs e)
    {
        base.OnMove(e);

        // 使用者移動視窗後，不立即彈回（以免干擾拖曳），
        // 但可在特定時機呼叫 ApplySmartPosition。
    }

    protected override void OnResizeEnd(EventArgs e)
    {
        base.OnResizeEnd(e);

        // 拖曳結束時執行智慧定位修正。
        ApplySmartPosition();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);

        UpdateMinimumSize();

        // 使用 SafeBeginInvoke 讓字型替換邏輯排在 Handle 建立完成「之後」才執行。
        this.SafeBeginInvoke(new Action(() =>
        {
            // 根據規範，在 Handle 建立時套用在地化與 A11y 屬性。
            ApplyLocalization();

            // 套用透明度。
            UpdateOpacity();

            // 執行初始位置檢查。
            ApplySmartPosition();
        }));

        RegisterHotKeyInternal();

        // 先解除再訂閱靜態事件，防止 Handle 重建時產生重複訂閱。
        SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
        SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);

        // 更新最小尺寸與佈局約束。
        UpdateMinimumSize();

        // 在 DPI 與佈局尺寸變更後，立即執行智慧重定位。
        // 這能防止視窗在螢幕邊緣切換 DPI 時導致視窗內容暫時移出可視範圍。
        ApplySmartPosition();

        // DPI 變更時，需重新計算並套用字型縮放。
        ApplyLocalization();

        // DPI 變更時強制失效游標快取，確保下次進入或目前焦點中的游標尺寸一定會被重新計算。
        _lastCaretWidth = -1;
        _lastCaretHeight = -1;

        // 若輸入框正取得焦點，立即同步更新游標寬度以符合新 DPI。
        if (TBInput.Focused)
        {
            UpdateCaretWidth();
        }
    }

    /// <summary>
    /// 更新快取的視窗標題前綴
    /// </summary>
    private void UpdateTitlePrefix()
    {
        string hotkeyInfo = $"[{GlobalHotKeyService.GetHotKeyDisplayString()}]",
            privacyInfo = AppSettings.Current.IsPrivacyMode ?
                Strings.App_Privacy_Suffix :
                string.Empty,
            restartInfo = IsRestartUpdatePending ?
                Strings.App_ThemePending_Suffix :
                string.Empty,
            gamepadLayoutInfo = GamepadFaceButtonProfile.GetActiveTitleLayoutHint();

        lock (_titleLock)
        {
            string[] titleParts =
            [
                Strings.App_Title,
                restartInfo,
                privacyInfo,
                gamepadLayoutInfo,
                hotkeyInfo,
            ];

            _cachedTitlePrefix = string.Join(
                " ",
                titleParts.Where(static part => !string.IsNullOrWhiteSpace(part)));
        }
    }

    /// <summary>
    /// 調整視窗不透明度
    /// </summary>
    /// <param name="delta">調整量（例如 0.05 代表增加 5%）</param>
    private void AdjustOpacity(float delta)
    {
        AppSettings config = AppSettings.Current;

        float oldOpacity = config.WindowOpacity;

        config.WindowOpacity += delta;

        // 消除浮點步進累積誤差，確保數值為精確的百分比整數。
        config.WindowOpacity = MathF.Round(config.WindowOpacity, 2);

        // 若數值有實質變動才處理。
        if (Math.Abs(oldOpacity - config.WindowOpacity) > 0.001f)
        {
            // 首次穿越 50% 下限時顯示知情警告。
            if (config.WindowOpacity < 0.5f && oldOpacity >= 0.5f)
            {
                DialogResult confirm = GamepadMessageBox.Show(
                    this,
                    Strings.Msg_LowOpacity_Warn,
                    Strings.Msg_LowOpacity_Warn_Title,
                    MessageBoxButtons.OKCancel,
                    MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2,
                    _gamepadController);

                if (confirm != DialogResult.OK)
                {
                    // 使用者取消：回復至原值，不套用。
                    config.WindowOpacity = oldOpacity;

                    return;
                }
            }

            // 立即套用視覺變更。
            UpdateOpacity();

            // 目前百分比。
            AnnounceA11y(string.Format(Strings.A11y_Opacity_Changed, config.WindowOpacity));

            // 震動回饋。
            VibrateAsync(VibrationPatterns.CursorMove).SafeFireAndForget();

            // 儲存設定。
            AppSettings.Save();

            // 更新右鍵選單顯示。
            RefreshMenu();
        }
    }

    /// <summary>
    /// 更新視窗不透明度
    /// 根據規範，若系統開啟高對比模式，則強制為 1.0 以確保可讀性
    /// </summary>
    private void UpdateOpacity()
    {
        if (SystemInformation.HighContrast)
        {
            Opacity = 1.0;

            return;
        }

        Opacity = AppSettings.Current.WindowOpacity;
    }

    /// <summary>
    /// 執行智慧定位修正，確保視窗不會跑出螢幕邊界
    /// </summary>
    private void ApplySmartPosition()
    {
        InputBoxLayoutManager.ApplySmartPosition(
            this,
            () => AnnounceA11y(Strings.A11y_SnapBack));
    }

    /// <summary>
    /// 更新最小尺寸以適應 DPI 變化
    /// </summary>
    private void UpdateMinimumSize()
    {
        _lastAppliedDpi = InputBoxLayoutManager.UpdateMinimumSize(
            DeviceDpi,
            _lastAppliedDpi,
            UpdateLayoutConstraints,
            UpdateOpacity);
    }

    /// <summary>
    /// 重設按鈕狀態
    /// </summary>
    /// <returns>Task</returns>
    private async Task ResetButtonStateAsync()
    {
        try
        {
            await Task.Delay(
                AppSettings.ButtonResetDelayMs,
                _formCts?.Token ?? CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        this.SafeInvoke(() =>
        {
            if (IsDisposed ||
                BtnCopy == null)
            {
                return;
            }

            // 還原文字與無障礙描述。
            BtnCopy.Text = GamepadFaceButtonProfile.GetActiveProfile().FormatConfirmButtonText(Strings.Btn_CopyDefault);
            BtnCopy.AccessibleDescription = Strings.A11y_BtnCopyDesc;

            // 還原視覺樣式：由擴充方法統一重置進度條並檢測視線接合。
            BtnCopy.BackColor = Color.Empty;
            BtnCopy.ForeColor = Color.Empty;
            BtnCopy.Invalidate();

            // 最後重新啟用按鈕。
            BtnCopy.Enabled = true;
        });
    }

    /// <summary>
    /// 將視窗不透明度重設為 100%
    /// </summary>
    private void ResetOpacity()
    {
        AppSettings.Current.WindowOpacity = 1.0f;

        UpdateOpacity();

        AppSettings.Save();

        // 更新右鍵選單顯示。
        RefreshMenu();

        // A11y 廣播。
        AnnounceA11y(string.Format(Strings.A11y_Opacity_Changed, 1.0));

        // 震動回饋。
        VibrateAsync(VibrationPatterns.ClearInput).SafeFireAndForget();
    }

    /// <summary>
    /// 切換隱私模式
    /// </summary>
    private void TogglePrivacyMode()
    {
        _tsmiPrivacyMode?.Checked = !_tsmiPrivacyMode.Checked;
    }

    /// <summary>
    /// 依觸發來源決定是否需要詢問使用者，並在確認後重新啟動應用程式。
    /// </summary>
    /// <param name="source">重啟要求來源。</param>
    private void AskForRestart(RestartRequestSource source = RestartRequestSource.SettingChange)
    {
        bool shouldRestart = RestartRequestDecider.ShouldRestart(
            source,
            () => GamepadMessageBox.Show(
                this,
                Strings.Msg_RestartRequired,
                Strings.Wrn_Title,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2,
                gamepad: _gamepadController));

        if (!shouldRestart)
        {
            return;
        }

        RestartApplication();
    }

    /// <summary>
    /// 立即重新啟動應用程式並完成前景恢復交接。
    /// </summary>
    private void RestartApplication()
    {
        // 標記為程式內部重啟，避免在舊實例退場時把焦點空窗誤捕捉為返回目標。
        Interlocked.Exchange(ref _isRestartingApplication, 1);

        // 為下一個重啟後的執行個體建立一次性前景啟用請求，降低焦點跳回前一個視窗的機率。
        RestartActivationCoordinator.Shared.RequestActivationOnNextLaunch();

        // 由目前前景執行個體主動授權新的重啟程序可呼叫 SetForegroundWindow，
        // 提升 Hosted Runner 與桌面自動化環境下的前景恢復成功率。
        _ = User32.AllowSetForegroundWindow(User32.AllowSetForegroundWindowAnyProcess);

        // 在正式結束前同步停止所有控制器震動，防止程序關閉後馬達持續空轉。
        FeedbackService.EmergencyStopAllActiveControllers();

        Program.ReleaseMutex();

        // 安全地關閉所有 MainForm 實例，確保它們的 Dispose 與 FormClosing 被正確觸發，
        // 從而釋放全域的 SystemEvents 鉤子，防止重啟時發生靜態資源洩漏。
        foreach (Form form in Application.OpenForms.Cast<Form>().ToList())
        {
            if (form is MainForm mainForm)
            {
                mainForm.Close();
            }
        }

        Application.Restart();

        Environment.Exit(0);
    }

    /// <summary>
    /// 將不再使用的私有字體加入回收桶，延遲處置
    /// </summary>
    /// <param name="font">Font</param>
    public static void AddFontToTrashCan(Font font)
    {
        FontResourceManager.AddFontToTrashCan(font);
    }

    /// <summary>
    /// 緊急清理靜態系統事件訂閱
    /// <para>此方法用於程式發生嚴重崩潰時，從 Program.cs 調用以防止記憶體洩漏。</para>
    /// </summary>
    public static void EmergencyCleanupSystemEvents()
    {
        try
        {
            // 嘗試從所有已開啟的視窗實例中解除訂閱。
            foreach (Form form in Application.OpenForms.Cast<Form>().ToList())
            {
                if (form is MainForm mainForm)
                {
                    SystemEvents.UserPreferenceChanged -= mainForm.SystemEvents_UserPreferenceChanged;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"系統事件清理失敗，已忽略：{ex.Message}");
        }
    }

    /// <summary>
    /// 判斷目前字體是否為全域共享快取字體（絕對禁止在此視窗中手動處置）
    /// </summary>
    /// <param name="font">要檢查的字體實例</param>
    /// <returns>若為共享字體則傳回 true</returns>
    private static bool IsSharedFont(Font font)
    {
        return FontResourceManager.IsSharedFont(font);
    }

    /// <summary>
    /// 取得全域共享的 A11y 放大字型
    /// </summary>
    /// <param name="dpi">目前的 DeviceDpi</param>
    /// <param name="style">字體樣式（預設為 Regular）</param>
    /// <param name="family">字體家族（選用）</param>
    /// <param name="sizeMultiplier">尺寸倍率（預設為 1.0）</param>
    /// <returns>Font 實例</returns>
    public static Font GetSharedA11yFont(
        int dpi,
        FontStyle style = FontStyle.Regular,
        FontFamily? family = null,
        float sizeMultiplier = 1.0f)
    {
        return FontResourceManager.GetSharedA11yFont(dpi, style, family ?? _defaultFontFamily, sizeMultiplier);
    }

    /// <summary>
    /// 處置所有快取的字體資源，防止程式結束後的 GDI 洩漏
    /// </summary>
    public static void DisposeCaches()
    {
        FontResourceManager.DisposeCaches();
    }
}