using InputBox.Core.Configuration;
using InputBox.Core.Interop;
using InputBox.Core.Services;
using InputBox.Resources;
using System.Diagnostics;

namespace InputBox.Core.Input;

/// <summary>
/// 遊戲手把控制器（XInput 實作）
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
    /// 是否有前一次的 XInputState
    /// </summary>
    private volatile bool _hasPreviousState;

    /// <summary>
    /// 是否已處置
    /// </summary>
    private volatile bool _disposed;

    /// <summary>
    /// 控制器按鈕重複方向
    /// </summary>
    private XInput.GamepadButton? _repeatDirection;

    /// <summary>
    /// 輪詢間隔（毫秒），約 60 FPS
    /// </summary>
    private const int PollingIntervalMs = 16;

    /// <summary>
    /// 斷線重連的降頻計數閾值
    /// </summary>
    private const int ReconnectThreshold = 30;

    /// <summary>
    /// 最大支援的控制器數量（XInput 標準）
    /// </summary>
    private const int MaxControllerCount = 4;

    /// <summary>
    /// 多控制器自動切換時的搖桿活動閾值（約為 XInput 類比搖桿最大 32767 的 25% 推動量）
    /// </summary>
    private const short ActiveThumbstickThreshold = 8000;

    /// <summary>
    /// XInput 左搖桿死區觸發閾值（Enter）
    /// 當搖桿推動超過此數值時，才視為「推動」。
    /// </summary>
    private int _thumbDeadzoneEnter;

    /// <summary>
    /// XInput 左搖桿死區重置閾值（Exit）- 遲滯緩衝值
    /// 當搖桿回彈低於此數值時，才視為「放開」。此數值必須夠低以吸收硬體抖動。
    /// </summary>
    private int _thumbDeadzoneExit;

    /// <summary>
    /// 取得或設定搖桿進入死區閾值
    /// </summary>
    public int ThumbDeadzoneEnter
    {
        get => _thumbDeadzoneEnter;
        set => _thumbDeadzoneEnter = value;
    }

    /// <summary>
    /// 取得或設定搖桿離開死區閾值
    /// </summary>
    public int ThumbDeadzoneExit
    {
        get => _thumbDeadzoneExit;
        set => _thumbDeadzoneExit = value;
    }

    /// <summary>
    /// 觸發鍵閾值（XInput 標準：30）
    /// </summary>
    private const byte TriggerThreshold = 30;

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
    /// 控制器 A 鍵
    /// </summary>
    public event Action? APressed;

    /// <summary>
    /// 控制器 B 鍵
    /// </summary>
    public event Action? BPressed;

    /// <summary>
    /// 控制器 X 鍵
    /// </summary>
    public event Action? XPressed;

    /// <summary>
    /// 控制器 Y 鍵
    /// </summary>
    public event Action? YPressed;

    /// <summary>
    /// 控制器上鍵重複
    /// </summary>
    public event Action? UpRepeat;

    /// <summary>
    /// 控制器下鍵重複
    /// </summary>
    public event Action? DownRepeat;

    /// <summary>
    /// 控制器左鍵重複
    /// </summary>
    public event Action? LeftRepeat;

    /// <summary>
    /// 控制器右鍵重複
    /// </summary>
    public event Action? RightRepeat;

    /// <summary>
    /// 當左觸發鍵（LT 鍵）被按下時觸發
    /// </summary>
    public event Action? LeftTriggerPressed;

    /// <summary>
    /// 當右觸發鍵（RT 鍵）被按下時觸發
    /// </summary>
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
    private int _vibrationToken = 0;

    /// <summary>
    /// 震動延遲任務的取消權杖來源
    /// </summary>
    private CancellationTokenSource? _vibrationCts;

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

        AppSettings settings = AppSettings.Current;

        _thumbDeadzoneEnter = settings.ThumbDeadzoneEnter;
        _thumbDeadzoneExit = settings.ThumbDeadzoneExit;

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

        _ctsPolling = new CancellationTokenSource();

        CancellationToken token = _ctsPolling.Token;

        _taskPolling = Task.Run(() => PollingLoopAsync(token), token);
    }

    /// <summary>
    /// 停止輪詢
    /// </summary>
    private void StopPolling()
    {
        CancelAndDisposeCts(ref _ctsPolling);
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
        try
        {
            // 使用 PeriodicTimer。
            using PeriodicTimer periodicTimer = new(TimeSpan.FromMilliseconds(PollingIntervalMs));

            while (await periodicTimer.WaitForNextTickAsync(cancellationToken))
            {
                // 在執行 Poll 前檢查是否已處置，避免觸發事件。
                if (_disposed ||
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
            Debug.WriteLine($"輪詢迴圈發生致命錯誤：{ex.Message}");
        }
    }

    /// <summary>
    /// 輪詢
    /// </summary>
    private void Poll()
    {
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

            // 降頻重連掃描（約每 500ms 一次）。
            _reconnectCounter++;

            if (_reconnectCounter < ReconnectThreshold)
            {
                return;
            }

            _reconnectCounter = 0;

            // 嘗試搜尋其他可用的控制器。
            for (uint i = 0; i < MaxControllerCount; i++)
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

                if (_reconnectCounter >= ReconnectThreshold)
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
        ApplyStickToButtons(ref currentState, _previousState);

        // 只有在 Input 啟用時才處理按鍵。
        if (!_context.IsInputActive)
        {
            _repeatCounter = 0;
            _repeatDirection = null;
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
            IsLeftTriggerHeld = currentState.Gamepad.LeftTrigger > TriggerThreshold;
            IsRightTriggerHeld = currentState.Gamepad.RightTrigger > TriggerThreshold;

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

            // 偵測事件觸發（Rising Edge：原本沒按 -> 現在按了）。
            // 處理 LT。
            bool wasLtDownBefore = _hasPreviousState &&
                _previousState.Gamepad.LeftTrigger > TriggerThreshold;

            if (IsLeftTriggerHeld &&
                !wasLtDownBefore)
            {
                LeftTriggerPressed?.Invoke();
            }

            // 處理 RT。
            bool wasRtDownBefore = _hasPreviousState &&
                _previousState.Gamepad.RightTrigger > TriggerThreshold;

            if (IsRightTriggerHeld &&
                !wasRtDownBefore)
            {
                RightTriggerPressed?.Invoke();
            }
        }

        HandleRepeat(currentState);

        _previousState = currentState;
        _hasPreviousState = true;
    }

    /// <summary>
    /// 掃描目前是否有其他正在活動的裝置，若有則切換
    /// </summary>
    /// <returns>是否有成功切換至新裝置</returns>
    private bool ScanForActiveDevice()
    {
        for (uint i = 0; i < MaxControllerCount; i++)
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
                    state.Gamepad.LeftTrigger > TriggerThreshold ||
                    state.Gamepad.RightTrigger > TriggerThreshold ||
                    Math.Abs(state.Gamepad.ThumbLeftX) > ActiveThumbstickThreshold ||
                    Math.Abs(state.Gamepad.ThumbLeftY) > ActiveThumbstickThreshold ||
                    Math.Abs(state.Gamepad.ThumbRightX) > ActiveThumbstickThreshold ||
                    Math.Abs(state.Gamepad.ThumbRightY) > ActiveThumbstickThreshold)
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
    private void HandleRepeat(XInput.XInputState state)
    {
        // 支援上下左右四個方向的長按重複判斷。
        XInput.GamepadButton? gbCurrentDirection =
            state.Has(XInput.GamepadButton.DpadLeft) ? XInput.GamepadButton.DpadLeft :
            state.Has(XInput.GamepadButton.DpadRight) ? XInput.GamepadButton.DpadRight :
            state.Has(XInput.GamepadButton.DpadUp) ? XInput.GamepadButton.DpadUp :
            state.Has(XInput.GamepadButton.DpadDown) ? XInput.GamepadButton.DpadDown :
            null;

        // 沒有按方向鍵 → 重置狀態。
        if (gbCurrentDirection is null)
        {
            _repeatCounter = 0;
            _repeatDirection = null;

            return;
        }

        // 方向改變 → 重置 repeat（重新給初始延遲）。
        if (_repeatDirection != gbCurrentDirection)
        {
            _repeatCounter = 0;
            _repeatDirection = gbCurrentDirection;

            return;
        }

        _repeatCounter++;

        // 初始延遲（由設定決定）。
        if (_repeatCounter < _repeatSettings.InitialDelayFrames)
        {
            return;
        }

        // Repeat 速度（由設定決定）。
        if (_repeatCounter % _repeatSettings.IntervalFrames != 0)
        {
            return;
        }

        // 觸發對應的重複事件。
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
        for (uint i = 0; i < 4; i++)
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
    private void ApplyStickToButtons(
        ref XInput.XInputState currentState,
        XInput.XInputState previousState)
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
        int thresholdLeft = wasLeft ? _thumbDeadzoneExit : _thumbDeadzoneEnter,
            thresholdRight = wasRight ? _thumbDeadzoneExit : _thumbDeadzoneEnter,
            thresholdUp = wasUp ? _thumbDeadzoneExit : _thumbDeadzoneEnter,
            thresholdDown = wasDown ? _thumbDeadzoneExit : _thumbDeadzoneEnter;

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
        try
        {
            XInput.XInputVibration stopVibration = default;

            _ = XInput.XInputSetState(_userIndex, in stopVibration);
        }
        catch
        {
            // 忽略
        }
    }

    /// <summary>
    /// 震動
    /// </summary>
    /// <param name="strength">強度</param>
    /// <param name="milliseconds">毫秒，預設為 60</param>
    /// <returns>Task</returns>
    public Task VibrateAsync(
        ushort strength,
        int milliseconds = 60)
    {
        // 優化：強度為 0 時直接停止並回傳，減少 GC 分配（Fast-path）。
        if (strength == 0)
        {
            StopVibration();

            return Task.CompletedTask;
        }

        // 將 XInput 呼叫推入背景執行緒，避免因藍牙手把休眠或驅動延遲而阻塞 UI 執行緒。
        return Task.Run(async () =>
        {
            // 捕獲目前的索引快照，確保開始與停止動作作用於同一個 Port。
            uint userIndex = _userIndex;

            // 每次呼叫時產生一個新的通行證（Token）。
            int currentToken = Interlocked.Increment(ref _vibrationToken);

            // 取消並更換 CTS，確保只有最後一個震動任務的延遲會執行。
            CancellationTokenSource newCts = new();

            CancelAndDisposeCts(ref _vibrationCts);

            _vibrationCts = newCts;

            CancellationToken token = newCts.Token;

            XInput.XInputVibration vibration = new()
            {
                LeftMotorSpeed = strength,
                RightMotorSpeed = strength
            };

            _ = XInput.XInputSetState(userIndex, in vibration);

            try
            {
                await Task.Delay(milliseconds, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // 任務被新的震動請求或釋放動作取消。
                return;
            }

            // 檢查：
            // 1. 是否已處置。
            // 2. 我的通行證是不是最新的？
            if (_disposed ||
                currentToken != _vibrationToken)
            {
                return;
            }

            XInput.XInputVibration stopVibration = default;

            _ = XInput.XInputSetState(userIndex, in stopVibration);
        });
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
        if (!_disposed)
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
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // 取消註冊。
        FeedbackService.UnregisterController(this);

        // 取消震動任務。
        CancelAndDisposeCts(ref _vibrationCts);

        // 發出取消訊號。
        Task pollingTask = StopPollingAsync();

        // 非同步等待背景工作真正結束。
        try
        {
            await pollingTask.ConfigureAwait(false);
        }
        catch (Exception)
        {
            // 忽略等待過程中的任何錯誤。
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
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // 取消註冊。
        FeedbackService.UnregisterController(this);

        // 取消震動任務。
        CancelAndDisposeCts(ref _vibrationCts);

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
    /// 取消並處置 CancellationTokenSource
    /// </summary>
    /// <param name="source">CancellationTokenSource 來源</param>
    private static void CancelAndDisposeCts(ref CancellationTokenSource? source)
    {
        CancellationTokenSource? cts = Interlocked.Exchange(ref source, null);

        if (cts != null)
        {
            try
            {
                if (!cts.IsCancellationRequested)
                {
                    cts.Cancel();
                }

                cts.Dispose();
            }
            catch
            {
                // 忽略處置例外。
            }
        }
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
        LeftTriggerPressed = null;
        RightTriggerPressed = null;
        ConnectionChanged = null;
    }
}