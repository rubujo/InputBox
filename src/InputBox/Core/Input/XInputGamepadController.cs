using InputBox.Core.Configuration;
using InputBox.Core.Extensions;
using InputBox.Core.Interop;
using InputBox.Core.Services;
using InputBox.Resources;
using System.Diagnostics;

namespace InputBox.Core.Input;

/// <summary>
/// Gamepad 控制介面（XInput 實作）
/// </summary>
internal sealed partial class XInputGamepadController : IGamepadController
{
    /// <summary>
    /// IInputContext
    /// </summary>
    private readonly IInputContext _context;

    /// <summary>
    /// 控制器的 User Index
    /// </summary>
    private volatile uint _userIndex;

    /// <summary>
    /// GamepadRepeatSettings
    /// </summary>
    private readonly GamepadRepeatSettings _repeatSettings;

    /// <summary>
    /// 重新連接計數器
    /// </summary>
    private int _reconnectCounter;

    /// <summary>
    /// 輪詢任務的 CancellationTokenSource
    /// </summary>
    private CancellationTokenSource? _ctsPolling;

    /// <summary>
    /// 輪詢任務
    /// </summary>
    private Task? _taskPolling;

    /// <summary>
    /// 前一次的 XInputState
    /// </summary>
    private XInput.XInputState _previousState;

    /// <summary>
    /// 重複計數器
    /// </summary>
    private int _repeatCounter;

    /// <summary>
    /// 右搖桿重複計數器
    /// </summary>
    private int _rsRepeatCounter;

    /// <summary>
    /// 是否有前一次的 XInputState
    /// </summary>
    private volatile bool _hasPreviousState;

    /// <summary>
    /// 是否已處置（0 = 未處置，1 = 已處置；使用 int 以支援 Interlocked.CompareExchange 原子操作）
    /// </summary>
    private volatile int _disposed;

    /// <summary>
    /// 控制器按鈕重複方向
    /// </summary>
    private XInput.GamepadButton? _repeatDirection;

    /// <summary>
    /// 右搖桿重複方向（虛擬按鍵）
    /// <para>-1：Left、1：Right、0：None</para>
    /// </summary>
    private int _rsRepeatDirection;

    /// <summary>
    /// 輪詢間隔（毫秒），約 60 FPS
    /// </summary>
    private const double PollingIntervalMs = 16.6;

    /// <summary>
    /// 取得或設定搖桿進入死區閾值
    /// </summary>
    public int ThumbDeadzoneEnter
    {
        get => AppSettings.Current.ThumbDeadzoneEnter;
        set => AppSettings.Current.ThumbDeadzoneEnter = value;
    }

    /// <summary>
    /// 取得或設定搖桿離開死區閾值
    /// </summary>
    public int ThumbDeadzoneExit
    {
        get => AppSettings.Current.ThumbDeadzoneExit;
        set => AppSettings.Current.ThumbDeadzoneExit = value;
    }

    /// <summary>
    /// 控制器上鍵
    /// </summary>
    public event Action? UpPressed;

    /// <summary>
    /// 控制器下鍵
    /// </summary>
    public event Action? DownPressed;

    /// <summary>
    /// 控制器左鍵
    /// </summary>
    public event Action? LeftPressed;

    /// <summary>
    /// 控制器右鍵
    /// </summary>
    public event Action? RightPressed;

    /// <summary>
    /// 控制器開始鍵
    /// </summary>
    public event Action? StartPressed;

    /// <summary>
    /// 控制器返回鍵
    /// </summary>
    public event Action? BackPressed;

    /// <summary>
    /// 控制器返回鍵放開
    /// </summary>
    public event Action? BackReleased;

    /// <summary>
    /// 快取的裝置名稱
    /// </summary>
    private string _cachedDeviceName = string.Empty;

    /// <summary>
    /// 取得目前使用的裝置名稱（XInput 則回傳格式化後的 User Index）
    /// </summary>
    public string DeviceName => _cachedDeviceName;

    /// <summary>
    /// 更新快取的裝置名稱
    /// </summary>
    private void UpdateCachedDeviceName()
    {
        _cachedDeviceName = string.Format(Strings.App_Gamepad_XInput_Format, _userIndex);
    }

    /// <summary>
    /// 當控制器連線狀態改變時觸發（true: 已連線, false: 已斷開）
    /// </summary>
    public event Action<bool>? ConnectionChanged;

    /// <summary>
    /// 取得目前是否已連線
    /// </summary>
    public bool IsConnected => _hasPreviousState;

    /// <summary>
    /// Controls for A, B, X, Y buttons
    /// </summary>
    public event Action? APressed;
    public event Action? BPressed;
    public event Action? XPressed;
    public event Action? YPressed;

    /// <summary>
    /// 控制器鍵重複事件
    /// </summary>
    public event Action? UpRepeat;
    public event Action? DownRepeat;
    public event Action? LeftRepeat;
    public event Action? RightRepeat;

    /// <summary>
    /// 右搖桿按壓事件
    /// </summary>
    public event Action? RSLeftPressed;
    public event Action? RSRightPressed;

    /// <summary>
    /// 右搖桿重複事件
    /// </summary>
    public event Action? RSLeftRepeat;
    public event Action? RSRightRepeat;

    /// <summary>
    /// 左右觸發鍵按壓事件
    /// </summary>
    public event Action? LeftTriggerPressed;
    public event Action? RightTriggerPressed;

    /// <summary>
    /// 控制器 LB 鍵是否按住
    /// </summary>
    public bool IsLeftShoulderHeld { get; private set; }

    /// <summary>
    /// 控制器 RB 鍵是否按住
    /// </summary>
    public bool IsRightShoulderHeld { get; private set; }

    /// <summary>
    /// 控制器左觸發鍵（LT 鍵）是否按住
    /// </summary>
    public bool IsLeftTriggerHeld { get; private set; }

    /// <summary>
    /// 控制器右觸發鍵（RT 鍵）是否按住
    /// </summary>
    public bool IsRightTriggerHeld { get; private set; }

    /// <summary>
    /// 控制器 Back 鍵是否按住
    /// </summary>
    public bool IsBackHeld { get; private set; }

    /// <summary>
    /// 控制器 B 鍵是否按住
    /// </summary>
    public bool IsBHeld { get; private set; }

    /// <summary>
    /// 震動 Token（用於追蹤目前的震動狀態，確保在非同步震動期間不會重複觸發震動或錯誤停止震動）
    /// </summary>
    private long _vibrationToken = 0;

    /// <summary>
    /// 震動延遲任務的取消權杖來源
    /// </summary>
    private CancellationTokenSource? _vibrationCts;

    /// <summary>
    /// 保護震動狀態切換的鎖物件
    /// </summary>
    private readonly Lock _vibrationLock = new();

    /// <summary>
    /// XInputGamepadController
    /// </summary>
    /// <param name="context">IInputContext</param>
    /// <param name="userIndex">控制器的 UserIndex，預設值為 0，有效值為 0~3。</param>
    /// <param name="repeatSettings">連發設定（可選）</param>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    public XInputGamepadController(
        IInputContext context,
        uint userIndex = 0,
        GamepadRepeatSettings? repeatSettings = null)
    {
        _context = context;

        ArgumentOutOfRangeException.ThrowIfGreaterThan<uint>(userIndex, 3);

        _userIndex = userIndex;

        _repeatSettings = repeatSettings ?? new();
        _repeatSettings.Validate();

        // 註冊至回饋服務以供緊急停止追蹤。
        FeedbackService.RegisterController(this);

        UpdateCachedDeviceName();

        StartPolling();
    }

    /// <summary>
    /// 啟動背景輪詢
    /// </summary>
    private void StartPolling()
    {
        // 防止重複啟動。
        StopPolling();

        // 先以區域變數持有新的 CancellationTokenSource，再寫入欄位，
        // 避免 StopPolling()（Interlocked.Exchange）在欄位寫入與 Token 讀取之間介入，
        // 導致取得 CancellationToken.None 的 TOCTOU 競態。
        CancellationTokenSource cts = new();

        _ctsPolling = cts;

        _taskPolling = Task.Run(() => PollingLoopAsync(cts.Token), cts.Token);
    }

    /// <summary>
    /// 停止輪詢
    /// </summary>
    private void StopPolling()
    {
        Interlocked.Exchange(ref _ctsPolling, null)?.CancelAndDispose();
    }

    /// <summary>
    /// 停止輪詢
    /// </summary>
    /// <returns>Task</returns>
    private Task StopPollingAsync()
    {
        StopPolling();

        // 回傳 Task，讓開啟者決定是否要等待。
        return _taskPolling ?? Task.CompletedTask;
    }

    /// <summary>
    /// 背景輪詢迴圈
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>Task</returns>
    private async Task PollingLoopAsync(CancellationToken cancellationToken)
    {
        // 使用 PeriodicTimer。
        using PeriodicTimer periodicTimer = new(TimeSpan.FromMilliseconds(PollingIntervalMs));

        try
        {
            while (await periodicTimer.WaitForNextTickAsync(cancellationToken))
            {
                // 在執行 Poll 前檢查是否已處置，避免觸發事件。
                if (_disposed != 0 ||
                    cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                // 將 try-catch 包裝在迴圈內部，防止 UI 尚未準備好時的跨執行緒例外殺死整個輪詢任務！
                try
                {
                    Poll();
                }
                catch (Exception ex)
                {
                    // 僅記錄 Debug 資訊，避免輪詢期間頻繁寫入日誌。
                    Debug.WriteLine($"XInput Poll 發生未預期錯誤：{ex.Message}");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 這是預期中的行為：當 StopPolling 開啟 Cancel() 時會觸發此處。
        }
        catch (ObjectDisposedException)
        {
            // Timer 已處置。
        }
        catch (Exception ex)
        {
            LoggerService.LogException(ex, "XInput 輪詢迴圈發生致命錯誤");

            Debug.WriteLine($"輪詢迴圈發生致命錯誤：{ex.Message}");
        }
    }

    /// <summary>
    /// 輪詢
    /// </summary>
    private void Poll()
    {
        // 0. 取得目前的設定快照，確保本幀處理邏輯的原子性。
        AppSettings.GamepadConfigSnapshot config = AppSettings.Current.GamepadSettings;

        // 嘗試讀取目前控制器狀態（本 Tick 只讀一次）。
        uint result = XInput.XInputGetState(_userIndex, out XInput.XInputState currentState);

        if (result != 0)
        {
            if (_hasPreviousState)
            {
                _hasPreviousState = false;

                // 斷線時立即重置所有按住狀態，防止邏輯鎖死。
                ResetHoldStates();

                ConnectionChanged?.Invoke(false);
            }

            // 斷線狀態：執行降頻重連邏輯。
            _repeatCounter = 0;
            _repeatDirection = null;
            _rsRepeatCounter = 0;
            _rsRepeatDirection = 0;

            // 降頻重連掃描（約每 500ms 一次）。
            _reconnectCounter++;

            if (_reconnectCounter < AppSettings.GamepadReconnectThresholdFrames)
            {
                return;
            }

            _reconnectCounter = 0;

            // 嘗試搜尋其他可用的控制器。
            for (uint i = 0; i < AppSettings.XInputMaxControllers; i++)
            {
                if (XInput.XInputGetState(i, out XInput.XInputState newState) == 0)
                {
                    // 找到新的控制器，更新 Index。
                    _userIndex = i;

                    _previousState = newState;

                    _hasPreviousState = true;

                    UpdateCachedDeviceName();

                    ConnectionChanged?.Invoke(true);

                    break;
                }
            }

            return;
        }
        else
        {
            if (!_hasPreviousState)
            {
                _hasPreviousState = true;

                ConnectionChanged?.Invoke(true);
            }

            // 閒置偵測：若 PacketNumber 相同，代表搖桿與按鍵狀態完全沒變（完全閒置）
            bool isIdle = _previousState.PacketNumber == currentState.PacketNumber;

            if (isIdle)
            {
                _reconnectCounter++;

                if (_reconnectCounter >= AppSettings.GamepadReconnectThresholdFrames)
                {
                    _reconnectCounter = 0;

                    // 若掃描到並切換了新裝置，本幀提早結束，等待下一幀處理新裝置的狀態。
                    if (ScanForActiveDevice())
                    {
                        return;
                    }
                }
            }
            else
            {
                _reconnectCounter = 0;
            }
        }

        // 將搖桿訊號合併到按鍵狀態中。
        ApplyStickToButtons(ref currentState, _previousState, config);

        // 只有在 Input 啟用時才處理按鍵。
        if (!_context.IsInputActive)
        {
            _repeatCounter = 0;
            _repeatDirection = null;
            _rsRepeatCounter = 0;
            _rsRepeatDirection = 0;

            // 非活躍期間仍需更新 _previousState，否則重新啟動後邊緣偵測會以舊狀態比較，
            // 導致幻象按鍵事件（spurious press events）。
            _previousState = currentState;
            _hasPreviousState = true;

            return;
        }

        // 備註：
        // PacketNumber 僅用於捷徑處理「狀態變更驅動」的邏輯。
        // 即使控制器狀態未發生變化，仍必須持續執行「時間驅動」的行為（例如重複輸入、長按判斷）。
        bool isStateChanged = !_hasPreviousState ||
            currentState.PacketNumber != _previousState.PacketNumber;

        // 檢查狀態是否改變。
        if (isStateChanged)
        {
            // 更新一般按鈕的「按住」狀態。
            IsLeftShoulderHeld = currentState.Has(XInput.GamepadButton.LeftShoulder);
            IsRightShoulderHeld = currentState.Has(XInput.GamepadButton.RightShoulder);
            IsBackHeld = currentState.Has(XInput.GamepadButton.Back);
            IsBHeld = currentState.Has(XInput.GamepadButton.B);

            // 處理觸發鍵。
            // 更新「按住」狀態。
            IsLeftTriggerHeld = currentState.Gamepad.LeftTrigger > AppSettings.XInputTriggerThreshold;
            IsRightTriggerHeld = currentState.Gamepad.RightTrigger > AppSettings.XInputTriggerThreshold;

            Detect(currentState, _previousState, XInput.GamepadButton.DpadUp, UpPressed);
            Detect(currentState, _previousState, XInput.GamepadButton.DpadDown, DownPressed);
            Detect(currentState, _previousState, XInput.GamepadButton.DpadLeft, LeftPressed);
            Detect(currentState, _previousState, XInput.GamepadButton.DpadRight, RightPressed);
            Detect(currentState, _previousState, XInput.GamepadButton.Start, StartPressed);
            Detect(currentState, _previousState, XInput.GamepadButton.Back, BackPressed);
            DetectReleased(currentState, _previousState, XInput.GamepadButton.Back, BackReleased);
            Detect(currentState, _previousState, XInput.GamepadButton.A, APressed);
            Detect(currentState, _previousState, XInput.GamepadButton.B, BPressed);
            Detect(currentState, _previousState, XInput.GamepadButton.X, XPressed);
            Detect(currentState, _previousState, XInput.GamepadButton.Y, YPressed);

            // 處理右搖桿虛擬按键偵測（使用 Hysteresis 邏輯以對抗漂移）。
            int thresholdLeft = _rsRepeatDirection == -1 ?
                    config.ThumbDeadzoneExit :
                    config.ThumbDeadzoneEnter,
                thresholdRight = _rsRepeatDirection == 1 ?
                    config.ThumbDeadzoneExit :
                    config.ThumbDeadzoneEnter,
                currentRsDir = 0;

            if (currentState.Gamepad.ThumbRightX < -thresholdLeft)
            {
                currentRsDir = -1;
            }
            else if (currentState.Gamepad.ThumbRightX > thresholdRight)
            {
                currentRsDir = 1;
            }

            // 偵測正緣觸發（使用「進入本區塊前」的舊方向進行比對）。
            if (currentRsDir == -1 &&
                _rsRepeatDirection != -1)
            {
                RSLeftPressed?.Invoke();
            }

            if (currentRsDir == 1 &&
                _rsRepeatDirection != 1)
            {
                RSRightPressed?.Invoke();
            }

            // 偵測到方向變化時，重置連發計數器。
            if (currentRsDir != _rsRepeatDirection)
            {
                _rsRepeatCounter = 0;
                _rsRepeatDirection = currentRsDir;
            }

            // 偵測一般按鈕事件觸發（Rising Edge：原本沒按 -> 現在按了）。
            // 處理 LT。
            bool wasLtDownBefore = _hasPreviousState &&
                _previousState.Gamepad.LeftTrigger > AppSettings.XInputTriggerThreshold;

            if (IsLeftTriggerHeld &&
                !wasLtDownBefore)
            {
                LeftTriggerPressed?.Invoke();
            }

            // 處理 RT。
            bool wasRtDownBefore = _hasPreviousState &&
                _previousState.Gamepad.RightTrigger > AppSettings.XInputTriggerThreshold;

            if (IsRightTriggerHeld &&
                !wasRtDownBefore)
            {
                RightTriggerPressed?.Invoke();
            }
        }

        HandleRepeat(currentState, config);

        _previousState = currentState;
        _hasPreviousState = true;
    }

    /// <summary>
    /// 掃描目前是否有其他正在活動的裝置，若有則切換
    /// </summary>
    /// <returns>是否有成功切換至新裝置</returns>
    private bool ScanForActiveDevice()
    {
        for (uint i = 0; i < AppSettings.XInputMaxControllers; i++)
        {
            // 略過目前正在使用的索引
            if (i == _userIndex)
            {
                continue;
            }

            // 取得其他 Index 的狀態
            if (XInput.XInputGetState(i, out XInput.XInputState state) == 0)
            {
                // 若其他控制器有明顯動作（按下按鈕、扳機超過閾值、搖桿超過活動閾值）。
                if (state.Gamepad.Buttons != 0 ||
                    state.Gamepad.LeftTrigger > AppSettings.XInputTriggerThreshold ||
                    state.Gamepad.RightTrigger > AppSettings.XInputTriggerThreshold ||
                    Math.Abs(state.Gamepad.ThumbLeftX) > AppSettings.XInputActiveThumbstickThreshold ||
                    Math.Abs(state.Gamepad.ThumbLeftY) > AppSettings.XInputActiveThumbstickThreshold ||
                    Math.Abs(state.Gamepad.ThumbRightX) > AppSettings.XInputActiveThumbstickThreshold ||
                    Math.Abs(state.Gamepad.ThumbRightY) > AppSettings.XInputActiveThumbstickThreshold)
                {
                    // 重置原本的按鍵狀態，避免按住的按鍵殘留。
                    ResetHoldStates();

                    // 切換過去。
                    _userIndex = i;
                    _hasPreviousState = false;

                    UpdateCachedDeviceName();

                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// 處理重複輸入
    /// </summary>
    /// <param name="state">XInput.XInputState</param>
    /// <param name="config">AppSettings.GamepadConfigSnapshot</param>
    private void HandleRepeat(
        XInput.XInputState state,
        AppSettings.GamepadConfigSnapshot config)
    {
        // 處理 D-Pad 重複輸入。
        XInput.GamepadButton? gbCurrentDirection =
            state.Has(XInput.GamepadButton.DpadLeft) ? XInput.GamepadButton.DpadLeft :
            state.Has(XInput.GamepadButton.DpadRight) ? XInput.GamepadButton.DpadRight :
            state.Has(XInput.GamepadButton.DpadUp) ? XInput.GamepadButton.DpadUp :
            state.Has(XInput.GamepadButton.DpadDown) ? XInput.GamepadButton.DpadDown :
            null;

        if (gbCurrentDirection is null)
        {
            _repeatCounter = 0;
            _repeatDirection = null;
        }
        else if (_repeatDirection != gbCurrentDirection)
        {
            _repeatCounter = 0;
            _repeatDirection = gbCurrentDirection;
        }
        else
        {
            _repeatCounter++;

            if (_repeatCounter >= config.RepeatInitialDelayFrames &&
                (_repeatCounter - config.RepeatInitialDelayFrames) % config.RepeatIntervalFrames == 0)
            {
                if (gbCurrentDirection == XInput.GamepadButton.DpadLeft)
                {
                    LeftRepeat?.Invoke();
                }
                else if (gbCurrentDirection == XInput.GamepadButton.DpadRight)
                {
                    RightRepeat?.Invoke();
                }
                else if (gbCurrentDirection == XInput.GamepadButton.DpadUp)
                {
                    UpRepeat?.Invoke();
                }
                else if (gbCurrentDirection == XInput.GamepadButton.DpadDown)
                {
                    DownRepeat?.Invoke();
                }
            }
        }

        // 處理右搖桿（RS）重複輸入。
        // 已在 Poll 中使用 Hysteresis 邏輯更新了 _rsRepeatDirection。
        int rsDir = _rsRepeatDirection;

        if (rsDir == 0)
        {
            _rsRepeatCounter = 0;
        }
        else
        {
            _rsRepeatCounter++;

            if (_rsRepeatCounter >= config.RepeatInitialDelayFrames &&
                (_rsRepeatCounter - config.RepeatInitialDelayFrames) % config.RepeatIntervalFrames == 0)
            {
                if (rsDir == -1)
                {
                    RSLeftRepeat?.Invoke();
                }
                else if (rsDir == 1)
                {
                    RSRightRepeat?.Invoke();
                }
            }
        }
    }

    /// <summary>
    /// 偵測按鍵按下
    /// </summary>
    /// <param name="currentState">目前的 XInputState</param>
    /// <param name="previousState">前一次的 XInputState</param>
    /// <param name="gamepadButton">控制器按鍵</param>
    /// <param name="action">Action</param>
    private static void Detect(
        in XInput.XInputState currentState,
        in XInput.XInputState previousState,
        XInput.GamepadButton gamepadButton,
        Action? action)
    {
        if (currentState.Has(gamepadButton) &&
            !previousState.Has(gamepadButton))
        {
            action?.Invoke();
        }
    }

    /// <summary>
    /// 偵測按鍵放開
    /// </summary>
    /// <param name="currentState">目前的 XInputState</param>
    /// <param name="previousState">前一次的 XInputState</param>
    /// <param name="gamepadButton">控制器按鍵</param>
    /// <param name="action">Action</param>
    private static void DetectReleased(
        in XInput.XInputState currentState,
        in XInput.XInputState previousState,
        XInput.GamepadButton gamepadButton,
        Action? action)
    {
        if (!currentState.Has(gamepadButton) &&
            previousState.Has(gamepadButton))
        {
            action?.Invoke();
        }
    }

    /// <summary>
    /// 尋找第一個已連接的控制器索引
    /// </summary>
    /// <returns>回傳 0-3 代表找到的控制器，若都沒找到則回傳 0（預設）</returns>
    public static uint GetFirstConnectedUserIndex()
    {
        for (uint i = 0; i < AppSettings.XInputMaxControllers; i++)
        {
            // 開啟 XInput.XInputGetState，若回傳 0（ERROR_SUCCESS）代表該控制器已連接。
            if (XInput.XInputGetState(i, out _) == 0)
            {
                return i;
            }
        }

        // 預設還是 0。
        return 0;
    }

    /// <summary>
    /// 將左搖桿的類比輸入映射為 D-Pad 數位訊號
    /// </summary>
    /// <param name="currentState">目前的 XInput.XInputState</param>
    /// <param name="previousState">前一次的 XInput.XInputState（用於遲滯判斷）</param>
    /// <param name="config">AppSettings.GamepadConfigSnapshot</param>
    private static void ApplyStickToButtons(
        ref XInput.XInputState currentState,
        XInput.XInputState previousState,
        AppSettings.GamepadConfigSnapshot config)
    {
        // 注意：previousState 包含了上一幀的「實體按鍵」+「虛擬搖桿按鍵」的結果，
        // 這正好符合我們需要的遲滯行為（保持狀態）。
        bool wasLeft = previousState.Has(XInput.GamepadButton.DpadLeft),
            wasRight = previousState.Has(XInput.GamepadButton.DpadRight),
            wasUp = previousState.Has(XInput.GamepadButton.DpadUp),
            wasDown = previousState.Has(XInput.GamepadButton.DpadDown);

        // 決定閾值：
        // - 如果原本是 ON，使用較低的 Exit 閾值（讓它更容易保持 ON，不容易斷）。
        // - 如果原本是 OFF，使用較高的 Enter 閾值（需要推得夠深才觸發）。
        int thresholdLeft = wasLeft ? config.ThumbDeadzoneExit : config.ThumbDeadzoneEnter,
            thresholdRight = wasRight ? config.ThumbDeadzoneExit : config.ThumbDeadzoneEnter,
            thresholdUp = wasUp ? config.ThumbDeadzoneExit : config.ThumbDeadzoneEnter,
            thresholdDown = wasDown ? config.ThumbDeadzoneExit : config.ThumbDeadzoneEnter;

        // 處理 X 軸（左右）。
        if (currentState.Gamepad.ThumbLeftX < -thresholdLeft)
        {
            // 搖桿向左 -> 視為按下 D-Pad Left。
            currentState.Gamepad.Buttons |= (ushort)XInput.GamepadButton.DpadLeft;
        }
        else if (currentState.Gamepad.ThumbLeftX > thresholdRight)
        {
            // 搖桿向右 -> 視為按下 D-Pad Right。
            currentState.Gamepad.Buttons |= (ushort)XInput.GamepadButton.DpadRight;
        }

        // 處理 Y 軸（上下）。
        if (currentState.Gamepad.ThumbLeftY < -thresholdDown)
        {
            // 搖桿向下 -> 視為按下 D-Pad Down。
            currentState.Gamepad.Buttons |= (ushort)XInput.GamepadButton.DpadDown;
        }
        else if (currentState.Gamepad.ThumbLeftY > thresholdUp)
        {
            // 搖桿向上 -> 視為按下 D-Pad Up。
            currentState.Gamepad.Buttons |= (ushort)XInput.GamepadButton.DpadUp;
        }
    }

    /// <summary>
    /// 同步強制停止震動（用於應用程式關閉等緊急情境）
    /// </summary>
    public void StopVibration()
    {
        lock (_vibrationLock)
        {
            // 立即遞增 Token 並取消現有任務的延遲，確保進行中的 VibrateAsync 任務失效。
            Interlocked.Increment(ref _vibrationToken);

            Interlocked.Exchange(ref _vibrationCts, null)?.CancelAndDispose();
        }

        try
        {
            XInput.XInputVibration stopVibration = default;

            _ = XInput.XInputSetState(_userIndex, in stopVibration);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[XInput] StopVibration 失敗（已忽略）：{ex.Message}");
        }
    }

    /// <summary>
    /// 震動
    /// </summary>
    /// <param name="strength">強度</param>
    /// <param name="milliseconds">毫秒，預設為 60</param>
    /// <param name="ct">取消權杖</param>
    /// <returns>Task</returns>
    public Task VibrateAsync(
        ushort strength,
        int milliseconds = 60,
        CancellationToken ct = default)
    {
        // 如果外部在呼叫前就已經要求取消，直接返回（Fast-path）。
        if (ct.IsCancellationRequested)
        {
            return Task.CompletedTask;
        }

        // 強度為 0 時直接停止並回傳，減少 GC 分配（Fast-path）。
        if (strength == 0)
        {
            StopVibration();

            return Task.CompletedTask;
        }

        // 將 XInput 呼叫推入背景執行緒，避免因藍牙控制器休眠或驅動延遲而阻塞 UI 執行緒。
        return Task.Run(async () =>
        {
            // 再檢查一次：避免在 Task ThreadPool 排隊等待時，外部就已經取消了。
            if (ct.IsCancellationRequested)
            {
                return;
            }

            // 捕獲目前的索引快照，確保開始與停止動作作用於同一個 Port。
            uint userIndex = _userIndex;

            long currentToken;

            CancellationTokenSource newCts = new();

            lock (_vibrationLock)
            {
                // 每次呼叫時產生一個新的通行證（Token）。
                currentToken = Interlocked.Increment(ref _vibrationToken);

                // 取消並更換 CTS，確保只有最後一個震動任務的延遲會執行。
                Interlocked.Exchange(ref _vibrationCts, null)?.CancelAndDispose();

                _vibrationCts = newCts;
            }

            CancellationToken token = newCts.Token;

            XInput.XInputVibration vibration = new()
            {
                LeftMotorSpeed = strength,
                RightMotorSpeed = strength
            };

            _ = XInput.XInputSetState(userIndex, in vibration);

            // 將「內部震動覆蓋權杖」與「外部傳入的取消權杖」綁定在一起。
            using CancellationTokenSource linkedCts = CancellationTokenSource
                .CreateLinkedTokenSource(newCts.Token, ct);

            try
            {
                // 只要有新震動進來，或外部要求取消，這裡的 Delay 就會立刻中斷。
                await Task.Delay(milliseconds, linkedCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // 判斷是誰觸發了取消？
                if (ct.IsCancellationRequested)
                {
                    // 狀況 A：外部 ct 被取消了（例如：使用者關閉視窗、切換頁面）
                    // 這種情況下，不會有新的震動來接管，我們必須「強制煞車」！
                    XInput.XInputVibration stop = default;

                    _ = XInput.XInputSetState(userIndex, in stop);
                }

                // 狀況 B：外部 ct 沒事，是內部的 newCts 取消了
                // 代表「有新的震動請求進來了」，新的 Task 已經啟動了馬達。
                // 我們這裡什麼都不用做，直接 return 默默退場即可。
                return;
            }

            // 檢查：
            // 1. 是否已處置。
            // 2. 我的通行證是不是最新的？
            if (_disposed != 0 ||
                currentToken != Interlocked.Read(ref _vibrationToken))
            {
                return;
            }

            XInput.XInputVibration stopVibration = default;

            _ = XInput.XInputSetState(userIndex, in stopVibration);
        }, ct);
    }

    /// <summary>
    /// 暫停
    /// </summary>
    public void Pause() => StopPolling();

    /// <summary>
    /// 恢復
    /// </summary>
    public void Resume()
    {
        if (_disposed == 0)
        {
            StartPolling();
        }
    }

    /// <summary>
    /// 取得或設定連發設定
    /// </summary>
    public GamepadRepeatSettings RepeatSettings
    {
        get => _repeatSettings;
        set
        {
            _repeatSettings.InitialDelayFrames = value.InitialDelayFrames;
            _repeatSettings.IntervalFrames = value.IntervalFrames;
        }
    }

    /// <summary>
    /// 非同步處置
    /// </summary>
    /// <returns>ValueTask</returns>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
        {
            return;
        }

        // 取消註冊。
        FeedbackService.UnregisterController(this);

        // 取消震動任務。
        Interlocked.Exchange(ref _vibrationCts, null)?.CancelAndDispose();

        // 發出取消訊號。
        Task pollingTask = StopPollingAsync();

        // 非同步等待背景工作真正結束。
        try
        {
            await pollingTask.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[XInput] 等待輪詢任務結束時發生錯誤（已忽略）：{ex.Message}");
        }

        // 停止震動與釋放資源。
        DisposeResources();

        // 關鍵安全措施：解除所有事件訂閱。
        ClearAllEvents();

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// 同步處置
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
        {
            return;
        }

        // 取消註冊。
        FeedbackService.UnregisterController(this);

        // 取消震動任務。
        Interlocked.Exchange(ref _vibrationCts, null)?.CancelAndDispose();

        // 發出取消訊號。
        StopPolling();

        // 移除 GetAwaiter().GetResult() 避免在 UI 執行緒上發生死結。
        // 背景輪詢任務會在收到取消訊號後自然結束，且接下來的 ClearAllEvents()
        // 能確保即使任務多執行一次，也不會觸發任何 UI 更新。

        // 清除所有事件訂閱。
        ClearAllEvents();

        // 停止震動與釋放資源。
        DisposeResources();

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// 處置資源
    /// </summary>
    private void DisposeResources()
    {
        // 停止震動。
        StopVibration();

        // 此處不再呼叫 StopPolling，因為它已在 Dispose/DisposeAsync 中被呼叫過，
        // 且 StopPolling 內部已包含原子處置邏輯。
    }

    /// <summary>
    /// 重置所有按鍵按住狀態
    /// </summary>
    private void ResetHoldStates()
    {
        IsLeftShoulderHeld = false;
        IsRightShoulderHeld = false;
        IsLeftTriggerHeld = false;
        IsRightTriggerHeld = false;
        IsBackHeld = false;
        IsBHeld = false;
    }

    /// <summary>
    /// 清除所有事件
    /// </summary>
    private void ClearAllEvents()
    {
        UpPressed = null;
        DownPressed = null;
        LeftPressed = null;
        RightPressed = null;
        StartPressed = null;
        BackPressed = null;
        BackReleased = null;
        APressed = null;
        BPressed = null;
        XPressed = null;
        YPressed = null;
        UpRepeat = null;
        DownRepeat = null;
        LeftRepeat = null;
        RightRepeat = null;
        RSLeftPressed = null;
        RSRightPressed = null;
        RSLeftRepeat = null;
        RSRightRepeat = null;
        LeftTriggerPressed = null;
        RightTriggerPressed = null;
        ConnectionChanged = null;
    }
}