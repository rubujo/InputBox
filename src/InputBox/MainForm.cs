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
    /// WindowNavigationService
    /// </summary>
    private readonly WindowNavigationService _navigationService;

    /// <summary>
    /// FormInputContext
    /// </summary>
    private FormInputContext? _inputContext;

    /// <summary>
    /// IGamepadController
    /// </summary>
    private IGamepadController? _gamepadController;

    /// <summary>
    /// 是否正在切換回先前的前景視窗
    /// </summary>
    private volatile int _isReturning;

    /// <summary>
    /// 是否正在顯示觸控式鍵盤
    /// </summary>
    private volatile int _isShowingTouchKeyboard;

    /// <summary>
    /// 輸入框標籤（用於 A11y 關聯）
    /// </summary>
    private Label? _lblInput;

    /// <summary>
    /// A11y 廣播用的標籤
    /// </summary>
    private AnnouncerLabel? _lblA11yAnnouncer;

    /// <summary>
    /// 基礎 DPI
    /// </summary>
    private const float BaseDpi = 96f;

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
    private volatile int _isFlashing = 0;

    /// <summary>
    /// 是否正在處理 Activated 事件（防止焦點競爭）
    /// </summary>
    private volatile int _isProcessingActivated = 0;

    /// <summary>
    /// 是否正在擷取快速鍵（原子旗標：0=否, 1=是）
    /// </summary>
    private volatile int _isCapturingHotkey = 0;

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
    /// 上一次的遊戲控制器連線狀態（用於防止重複廣播）
    /// </summary>
    private bool? _lastGamepadConnectedState;

    /// <summary>
    /// 待回收的舊字體資源池（防止跨視窗引用時發生 ObjectDisposedException）
    /// </summary>
    private static readonly List<Font> _fontTrashCan = [];

    /// <summary>
    /// 全域共享的 A11y 標準字型快取（依據 DPI 儲存，支援 PerMonitorV2）
    /// </summary>
    private static readonly Dictionary<int, Font> _regularFontCache = [];

    /// <summary>
    /// 全域共享的 A11y 加粗字型快取（依據 DPI 儲存，支援 PerMonitorV2）
    /// </summary>
    private static readonly Dictionary<int, Font> _boldFontCache = [];

    /// <summary>
    /// 統一放大的 A11y 字型（實例引用）
    /// </summary>
    private Font A11yFont => GetCachedFont(
        DeviceDpi,
        FontStyle.Regular,
        BtnCopy?.Font?.FontFamily);

    /// <summary>
    /// 快取的加粗字型（實例引用）
    /// </summary>
    private Font BoldBtnFont => GetCachedFont(
        DeviceDpi,
        FontStyle.Bold,
        BtnCopy?.Font?.FontFamily);

    /// <summary>
    /// 快取的視窗標題前綴（包含標題、隱私狀態與快速鍵）
    /// </summary>
    private string _cachedTitlePrefix = string.Empty;

    /// <summary>
    /// 啟動時是否為深色模式
    /// </summary>
    private readonly bool _initialIsDarkMode;

    /// <summary>
    /// 啟動時是否為高對比模式
    /// </summary>
    private readonly bool _initialHighContrast;

    /// <summary>
    /// 判斷是否有待處理的主題變更（需重啟以完全套用）
    /// </summary>
    private bool IsThemeUpdatePending =>
        _initialIsDarkMode != this.IsDarkModeActive() ||
        _initialHighContrast != SystemInformation.HighContrast;

    /// <summary>
    /// 上一次套用佈局時的 DPI 值（用於防抖）
    /// </summary>
    private float _lastAppliedDpi = -1;

    /// <summary>
    /// 快取的原始邊距（用於懸停零邊距策略）
    /// </summary>
    private Padding _originalBtnPadding;

    /// <summary>
    /// 注視進度（0.0 ~ 1.0）
    /// </summary>
    private float _dwellProgress = 0f;

    /// <summary>
    /// 是否正被注視中
    /// </summary>
    private bool _isBtnHovered = false;

    /// <summary>
    /// 目前按鈕動畫的序號（用於處理中止與競爭）
    /// </summary>
    private long _animationId = 0;

    /// <summary>
    /// 冷卻旗標，防止在短時間內重複觸發按鈕動作（例如連續點擊或快速鍵）
    /// </summary>
    private bool _isActionCooldown = false;

    /// <summary>
    /// 記錄最後一個獲得焦點或被選取的選單項目，用於對話框關閉後的焦點還原。
    /// </summary>
    private ToolStripItem? _lastFocusedMenuItem = null;

    public MainForm()
    {
        InitializeComponent();

        // 記錄啟動時的主題狀態基準值。
        _initialIsDarkMode = this.IsDarkModeActive();
        _initialHighContrast = SystemInformation.HighContrast;

        Disposed += (s, e) =>
        {
            _formCts?.Dispose();
        };

        // 套用全域震動強度設定。
        VibrationPatterns.GlobalIntensityMultiplier = AppSettings.Current.VibrationIntensity;

        // 使用設定檔容量初始化 InputHistoryManager。
        _historyService = new InputHistoryService(AppSettings.Current.HistoryCapacity)
        {
            IsPrivacyMode = AppSettings.Current.IsPrivacyMode
        };
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
        ClipboardService.OnRetry = () => AnnounceA11y(Strings.A11y_Clipboard_Retrying);

        // 設定預設動作按鈕，支援 A11y 視覺引導。
        AcceptButton = BtnCopy;

        // 限制輸入字數，與 InputHistoryService 的上限保持一致。
        TBInput.MaxLength = 10000;

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
    }

    protected override void WndProc(ref Message m)
    {
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

        _gamepadController?.Resume();
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);

        // 如果是因為正在呼叫觸控小鍵盤而失去焦點，則不進行任何處理（保留視覺狀態與手把輪詢）。
        if (_isShowingTouchKeyboard != 0)
        {
            return;
        }

        // 使用 SafeInvoke 延遲執行，避開 ShowDialog 瞬間的焦點空窗期，並確保在 UI 執行緒操作。
        this.SafeInvoke(() =>
        {
            try
            {
                // 清除 UI 視覺殘留。
                // 確保視窗在背景或被對話框遮擋時，不會殘留 Hover 的灰底或 Focus 的邊框。

                // 1. 強制清除按鈕的 Hover 或 Focus 視覺殘留。
                _isBtnHovered = false;

                if (BtnCopy != null && !BtnCopy.IsDisposed)
                {
                    // 強制洗掉所有的顏色、粗體與邊框。
                    RestoreButtonDefaultStyle(force: true);
                }

                // 2. 清除輸入框的視覺殘留。
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

            // 如果還有其他視窗（例如數值輸入對話框）是活躍的，代表應用程式還在前景，不應停止手把輪詢。
            if (ActiveForm != null)
            {
                return;
            }

            // 當整個應用程式完全退到背景時，停止震動並暫停手把輪詢。
            FeedbackService.StopAllVibrationsAsync(_gamepadController).SafeFireAndForget();

            _gamepadController?.Pause();
        });
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
        // 立即發出全域取消訊號，中止所有 UI 相關的非同步任務。
        _formCts?.Cancel();

        // 停止 A11y 背景工作者。
        // 先標記 Writer 完成，並取消對應的 CTS 以強行中斷 Delay。
        _a11yChannel?.Writer.TryComplete();
        _a11yCts?.Cancel();

        // 非同步硬體緊急清理。
        // 將同步的硬體 I/O 操作（如 XInput 設定）移至背景執行，防止阻塞 UI 執行緒導致關閉停頓。
        Task.Run(() =>
        {
            try
            {
                FeedbackService.EmergencyStopAllActiveControllers();
            }
            catch
            {
                // 忽略背景清理的任何錯誤。
            }
        });

        try
        {
            // 優先停止輸入源。
            IGamepadController? gamepad = Interlocked.Exchange(ref _gamepadController, null);

            gamepad?.Dispose();
        }
        catch
        {
            // 忽略個別控制器的釋放失敗。
        }

        // 原子性清理其餘權杖資源。
        CancellationTokenSource? alertCts = Interlocked.Exchange(ref _alertCts, null);

        alertCts?.Dispose();

        CancellationTokenSource? a11yCts = Interlocked.Exchange(ref _a11yCts, null);

        a11yCts?.Dispose();

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

        // 原子化處置輸入框專用字型。
        Font? inputFont = Interlocked.Exchange(ref _inputFont, null);

        if (inputFont != null)
        {
            AddFontToTrashCan(inputFont);
        }

        // 確保所有旗標正確歸零。
        Interlocked.Exchange(ref _isCapturingHotkey, 0);

        // 最後處置主權杖。
        CancellationTokenSource? formCts = Interlocked.Exchange(ref _formCts, null);

        formCts?.Dispose();

        base.OnFormClosing(e);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (_isCapturingHotkey != 0)
        {
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

                    // 使用統一的方法還原 UI 狀態。
                    RestoreUIFromCaptureMode();

                    string fullHotkeyStr = GlobalHotKeyService.GetHotKeyDisplayString();

                    // 調用帶參數的 UpdateTitle 以顯示更新成功狀態，同時保留快速鍵資訊。
                    UpdateTitle($"{Strings.Msg_HotkeyUpdated}: {fullHotkeyStr}");

                    BtnCopy.Enabled = false;
                    BtnCopy.Text = Strings.Msg_HotkeyUpdated;

                    // 延後播報（1200ms），確保在視窗標題變更引發的語音中斷結束後再進行播報。
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

                    // 發生錯誤，仍需還原 UI。
                    RestoreUIFromCaptureMode();

                    // 調用帶參數的 UpdateTitle 以顯示錯誤狀態，同時保留快速鍵資訊。
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
                _formCts?.Token ?? CancellationToken.None);

                return true;
            }

            // 當使用者只按住修飾鍵（尚未按下主要按鍵）時，重新廣播提示語並給予輕微震動回饋。
            string strA11yMsg = $"{Strings.Msg_PressAnyKey} ({Strings.A11y_Capture_Esc_Cancel})";

            AnnounceA11y(strA11yMsg);

            VibrateAsync(VibrationPatterns.CursorMove).SafeFireAndForget();

            return true;
        }

        if (ActiveControl == TBInput)
        {
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

                return true;
            }

            if (keyData == (Keys.Enter | Keys.Shift))
            {
                // 告知使用者已換行。
                AnnounceA11y(Strings.A11y_New_Line);

                // 物理回饋。
                VibrateAsync(VibrationPatterns.CursorMove).SafeFireAndForget();

                return base.ProcessCmdKey(ref msg, keyData);
            }
        }

        return base.ProcessCmdKey(ref msg, keyData);
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
            themeInfo = IsThemeUpdatePending ?
                Strings.App_ThemePending_Suffix :
                string.Empty;

        _cachedTitlePrefix = $"{Strings.App_Title} {themeInfo}{privacyInfo} {hotkeyInfo}";
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

        // 若數值有實質變動才處理。
        if (Math.Abs(oldOpacity - config.WindowOpacity) > 0.001f)
        {
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
        if (!IsHandleCreated ||
            IsDisposed)
        {
            return;
        }

        Screen screen = Screen.FromControl(this);

        Rectangle workArea = screen.WorkingArea;

        int newX = Location.X,
            newY = Location.Y;

        bool adjusted = false;

        // 檢查右邊界。
        if (newX + Width > workArea.Right)
        {
            newX = workArea.Right - Width;

            adjusted = true;
        }

        // 檢查左邊界。
        if (newX < workArea.Left)
        {
            newX = workArea.Left;

            adjusted = true;
        }

        // 檢查下邊界。
        if (newY + Height > workArea.Bottom)
        {
            newY = workArea.Bottom - Height;

            adjusted = true;
        }

        // 檢查上邊界。
        if (newY < workArea.Top)
        {
            newY = workArea.Top;

            adjusted = true;
        }

        if (adjusted)
        {
            Location = new Point(newX, newY);

            // 告知使用者視窗已修正位置。
            AnnounceA11y(Strings.A11y_SnapBack);
        }
    }

    /// <summary>
    /// 更新最小尺寸以適應 DPI 變化
    /// </summary>
    private void UpdateMinimumSize()
    {
        float currentDpi = DeviceDpi;

        // 防抖：如果 DPI 沒有實質變動，則跳過計算。
        if (Math.Abs(_lastAppliedDpi - currentDpi) < 0.01f)
        {
            return;
        }

        _lastAppliedDpi = currentDpi;

        // 此處不再執行昂貴的 ApplyLocalization，而是僅更新佈局約束。
        // 這樣能避免 DPI 變更（如螢幕拖曳）時導致按鈕文字被意外重置。
        UpdateLayoutConstraints();

        // 高對比模式下強制 100% 不透明度，確保絕對符合無障礙可讀性規範。
        if (SystemInformation.HighContrast)
        {
            UpdateOpacity();
        }
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
                Delay_ButtonReset,
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
            BtnCopy.Text = ControlExtensions.GetMnemonicText(Strings.Btn_CopyDefault, 'A');
            BtnCopy.AccessibleDescription = Strings.A11y_BtnCopyDesc;

            // 還原視覺樣式（顏色與粗細）。
            RestoreButtonDefaultStyle();

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
    /// 要求使用者重啟應用程式以套用變更
    /// </summary>
    private static void AskForRestart()
    {
        if (MessageBox.Show(
            Strings.Msg_RestartRequired,
            Strings.Wrn_Title,
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question) == DialogResult.Yes)
        {
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
    }

    /// <summary>
    /// 將不再使用的字體移入回收桶進行延遲處置
    /// </summary>
    /// <param name="font">要回收的字體實例</param>
    public static void AddFontToTrashCan(Font font)
    {
        if (font == null)
        {
            return;
        }

        lock (_fontTrashCan)
        {
            _fontTrashCan.Add(font);
        }

        // 啟動延遲清理任務，避免字體堆積引發 GDI 資源洩漏。
        // 等待足夠長的時間（例如 2 秒），確保所有進行中的重繪事件都已結束，不再使用該字體。
        Task.Run(async () =>
        {
            try
            {
                await Task.Delay(2000);

                lock (_fontTrashCan)
                {
                    if (_fontTrashCan.Contains(font))
                    {
                        font.Dispose();
                        _fontTrashCan.Remove(font);
                    }
                }
            }
            catch
            {
                // 忽略背景清理的任何錯誤。
            }
        });
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
        catch
        {
            // 緊急清理路徑，忽略所有錯誤。
        }
    }

    /// <summary>
    /// 取得指定 DPI 的全域共享字體
    /// </summary>
    /// <param name="dpi">視窗目前的 DeviceDpi</param>
    /// <param name="style">字型樣式</param>
    /// <param name="family">字型家族</param>
    /// <returns>Font</returns>
    public static Font GetCachedFont(int dpi, FontStyle style, FontFamily? family = null)
    {
        Dictionary<int, Font> cache = style == FontStyle.Bold ?
            _boldFontCache :
            _regularFontCache;

        lock (cache)
        {
            if (!cache.TryGetValue(dpi, out Font? font))
            {
                font = GetSharedA11yFont(dpi, style, family);

                cache[dpi] = font;
            }

            return font;
        }
    }

    /// <summary>
    /// 處置所有快取的字體資源，防止程式結束後的 GDI 洩漏
    /// </summary>
    public static void DisposeCaches()
    {
        lock (_regularFontCache)
        {
            foreach (Font font in _regularFontCache.Values)
            {
                font.Dispose();
            }

            _regularFontCache.Clear();
        }

        lock (_boldFontCache)
        {
            foreach (Font font in _boldFontCache.Values)
            {
                font.Dispose();
            }

            _boldFontCache.Clear();
        }

        // 清理回收桶中剩餘的字體。
        lock (_fontTrashCan)
        {
            foreach (Font font in _fontTrashCan)
            {
                font.Dispose();
            }

            _fontTrashCan.Clear();
        }
    }
}