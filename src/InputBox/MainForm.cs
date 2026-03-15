using InputBox.Core.Configuration;
using InputBox.Core.Controls;
using InputBox.Core.Extensions;
using InputBox.Core.Feedback;
using InputBox.Core.Input;
using InputBox.Core.Interop;
using InputBox.Core.Services;
using InputBox.Resources;
using Microsoft.Win32;
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
    /// 基礎最小寬度
    /// </summary>
    private const int BaseMinWidth = 400;

    /// <summary>
    /// 基礎最小高度
    /// </summary>
    private const int BaseMinHeight = 100;

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
    private readonly CancellationTokenSource _formCts = new();

    /// <summary>
    /// 控制器初始化鎖（防止重複建立實例）
    /// </summary>
    private readonly SemaphoreSlim _gamepadInitLock = new(1, 1);

    /// <summary>
    /// 右鍵選單
    /// </summary>
    private ContextMenuStrip? _cmsInput;

    /// <summary>
    /// 隱私模式選單項
    /// </summary>
    private ToolStripMenuItem? _tsmiPrivacyMode;

    /// <summary>
    /// 上一次的遊戲手把連線狀態（用於防止重複廣播）
    /// </summary>
    private bool? _lastGamepadConnectedState;

    /// <summary>
    /// 原始字型
    /// </summary>
    private Font? _originalBtnFont;

    /// <summary>
    /// 快取的加粗字型（A11y 視覺強化，需手動 Dispose）
    /// </summary>
    private Font? _boldBtnFont;

    /// <summary>
    /// 原始背景色
    /// </summary>
    private Color _originalBtnBackColor;

    /// <summary>
    /// 原始前景色
    /// </summary>
    private Color _originalBtnForeColor;

    /// <summary>
    /// 快取的視窗標題前綴（包含標題、隱私狀態與快速鍵）
    /// </summary>
    private string _cachedTitlePrefix = string.Empty;

    /// <summary>
    /// 更新快取的視窗標題前綴
    /// </summary>
    private void UpdateTitlePrefix()
    {
        string hotkeyInfo = $"[{GlobalHotKeyService.GetHotKeyDisplayString()}]",
            privacyInfo = AppSettings.Current.IsPrivacyMode ?
                Strings.App_Privacy_Suffix :
                string.Empty;

        _cachedTitlePrefix = $"{Strings.App_Title}{privacyInfo} {hotkeyInfo}";
    }

    public MainForm()
    {
        InitializeComponent();

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

        // 在初始化完成後，套用本地化。
        ApplyLocalization();

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

        // 綁定剪貼簿重試通知。
        ClipboardService.OnRetry = () => AnnounceA11y(Strings.A11y_Clipboard_Retrying);

        // 限制輸入字數，與 InputHistoryService 的上限保持一致。
        TBInput.MaxLength = 10000;
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

        if (_isShowingTouchKeyboard != 0)
        {
            return;
        }

        // 使用 SafeBeginInvoke 延遲執行，避開 ShowDialog 瞬間的焦點空窗期。
        this.SafeBeginInvoke(() =>
        {
            // 如果還有其他視窗（例如數值輸入對話框）是活躍的，不應停止手把輪詢。
            if (ActiveForm != null)
            {
                return;
            }

            FeedbackService.StopAllVibrationsAsync(_gamepadController).SafeFireAndForget();

            _gamepadController?.Pause();
        });
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        GlobalHotKeyService.UnregisterShowInputHotkey(Handle);

        SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;

        base.OnHandleDestroyed(e);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // 1. 立即發出全域取消訊號，中止所有 UI 相關的非同步任務。
        _formCts.Cancel();

        // 確保系統快速鍵資源在視窗關閉前已被徹底釋放。
        GlobalHotKeyService.UnregisterShowInputHotkey(Handle);

        // 優先停止輸入源，防止其在通道關閉後仍嘗試寫入廣播。
        _gamepadController?.Dispose();

        // 停止 A11y 背景工作者。
        // 先標記 Writer 完成，讓 ReadAllAsync 平順結束。
        _a11yChannel?.Writer.TryComplete();

        // 原子性地取出並清理 CTS。
        CancellationTokenSource? cts = Interlocked.Exchange(ref _a11yCts, null);

        if (cts != null)
        {
            try
            {
                cts.Cancel();
                cts.Dispose();
            }
            catch (ObjectDisposedException)
            {

            }
        }

        // 確保在視窗關閉、物件釋放前，硬體震動已被同步切斷。
        FeedbackService.EmergencyStopAllActiveControllers();

        // 清理靜態引用，防止記憶體洩漏。
        // 僅在最後一個 MainForm 關閉時才清理全域委派，確保多視窗擴充性。
        if (Application.OpenForms.OfType<MainForm>().Count() <= 1)
        {
            ClipboardService.OnRetry = null;
            Core.Extensions.TaskExtensions.GlobalExceptionHandler = null;
        }

        // 處置輸入上下文，解除事件訂閱。
        _inputContext?.Dispose();

        _historyService.Clear();

        // 釋放鎖資源。
        _gamepadInitLock.Dispose();

        // 釋放 GDI 與選單資源。
        _boldBtnFont?.Dispose();
        _boldBtnFont = null;

        if (_cmsInput != null)
        {
            _cmsInput.Font?.Dispose();
            _cmsInput.Dispose();
            _cmsInput = null;
        }

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

                    Text = $"{Strings.App_Title} - [{Strings.Msg_HotkeyUpdated}: {fullHotkeyStr}]";

                    BtnCopy.Enabled = false;
                    BtnCopy.Text = Strings.Msg_HotkeyUpdated;

                    // A11y 廣播：延後播報（1200ms），確保在視窗標題變更引發的語音中斷結束後再進行播報。
                    async Task DelayedAnnounce()
                    {
                        try
                        {
                            await Task.Delay(1200, _formCts.Token);

                            if (IsDisposed) return;

                            AnnounceA11y(string.Format(Strings.A11y_Hotkey_Captured, fullHotkeyStr));
                        }
                        catch (OperationCanceledException) { }
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

                    Text = $"{Strings.App_Title} - [{Strings.Err_Title}]";

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
                        await Task.Delay(1500, _formCts.Token);

                        this.SafeInvoke(() =>
                        {
                            if (IsDisposed) return;

                            UpdateTitle();
                        });
                    }
                    catch (OperationCanceledException)
                    {

                    }

                }, _formCts.Token);

                return true;
            }

            // A11y 語音引導：當使用者只按住修飾鍵（尚未按下主要按鍵）時，重新廣播提示語並給予輕微震動回饋。
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
                // A11y 廣播：告知使用者已換行。
                AnnounceA11y(Strings.A11y_New_Line);

                return base.ProcessCmdKey(ref msg, keyData);
            }
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    /// <summary>
    /// 上一次套用佈局時的 DPI 值（用於防抖）
    /// </summary>
    private float _lastAppliedDpi = -1;

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);

        UpdateMinimumSize();

        RegisterHotKeyInternal();

        // 先解除再訂閱靜態事件，防止 Handle 重建時產生重複訂閱。
        SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
        SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);

        UpdateMinimumSize();
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

        float scale = currentDpi / BaseDpi;

        int minWidth = (int)Math.Round(BaseMinWidth * scale),
            minHeight = (int)Math.Round(BaseMinHeight * scale);

        MinimumSize = new Size(minWidth, minHeight);
    }

    /// <summary>
    /// 重設按鈕狀態
    /// </summary>
    /// <returns>Task</returns>
    private async Task ResetButtonStateAsync()
    {
        try
        {
            await Task.Delay(Delay_ButtonReset, _formCts.Token);
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
            Program.ReleaseMutex();

            Application.Restart();

            Environment.Exit(0);
        }
    }
}