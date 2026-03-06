using InputBox.Core.Configuration;
using InputBox.Core.Interop;
using System.Diagnostics;

namespace InputBox.Core.Input;

/// <summary>
/// 遊戲手把控制器
/// </summary>
internal sealed partial class GamepadController : IDisposable, IAsyncDisposable
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
    private Win32.XInputState _previousState;

    /// <summary>
    /// 重複計數器
    /// </summary>
    private int _repeatCounter;

    /// <summary>
    /// 是否有前一次的 XInputState
    /// </summary>
    private bool _hasPreviousState;

    /// <summary>
    /// 是否已處置
    /// </summary>
    private volatile bool _disposed;

    /// <summary>
    /// 控制器按鈕重複方向
    /// </summary>
    private Win32.GamepadButton? _repeatDirection;

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
    /// XInput 左搖桿死區觸發閾值（Enter）- 標準值 7849
    /// 當搖桿推動超過此數值時，視為「按下」。
    /// </summary>
    private readonly int _thumbDeadzoneEnter;

    /// <summary>
    /// XInput 左搖桿死區重置閾值（Exit）- 遲滯緩衝值
    /// 當搖桿回彈低於此數值時，才視為「放開」。此數值必須夠低以吸收硬體抖動。
    /// </summary>
    private readonly int _thumbDeadzoneExit;

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
    /// GamepadController
    /// </summary>
    /// <param name="context">IInputContext</param>
    /// <param name="userIndex">控制器的 UserIndex，預設值為 0，有效值為 0~3。</param>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    public GamepadController(
        IInputContext context,
        uint userIndex = 0,
        GamepadRepeatSettings? repeatSettings = null)
    {
        _context = context;

        ArgumentOutOfRangeException.ThrowIfGreaterThan<uint>(userIndex, 3);

        _userIndex = userIndex;

        _repeatSettings = repeatSettings ?? new();
        _repeatSettings.Validate();

        // 從 AppSettings 載入死區設定。
        AppSettings settings = AppSettings.Current;

        _thumbDeadzoneEnter = settings.ThumbDeadzoneEnter;

        // 防抖機制強制介入：
        // 如果 Exit 設定得太靠近 Enter（兩者差距小於 4000），極易造成搖桿微小抖動時的連點誤判。
        // 此時強制將 Exit 壓低至 2500，確保有足夠的遲滯緩衝區。
        if (settings.ThumbDeadzoneExit >= _thumbDeadzoneEnter - 4000)
        {
            _thumbDeadzoneExit = Math.Min(2500, _thumbDeadzoneEnter / 2);
        }
        else
        {
            _thumbDeadzoneExit = settings.ThumbDeadzoneExit;
        }

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
        CancellationTokenSource? cts = Interlocked.Exchange(ref _ctsPolling, null);

        if (cts != null)
        {
            try
            {
                cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // 已釋放則忽略。
            }
            finally
            {
                cts.Dispose();
            }
        }
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

                Poll();
            }
        }
        catch (OperationCanceledException)
        {
            // 這是預期中的行為：當 StopPolling 開啟 Cancel() 時會觸發此處。
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"輪詢迴圈發生錯誤：{ex.Message}");
        }
    }

    /// <summary>
    /// 輪詢
    /// </summary>
    private void Poll()
    {
        // 嘗試讀取目前控制器狀態（本 Tick 只讀一次）。
        uint result = Win32.XInputGetState(_userIndex, out Win32.XInputState currentState);

        if (result != 0)
        {
            // 斷線狀態：執行降頻重連邏輯。
            _hasPreviousState = false;
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
                if (Win32.XInputGetState(i, out Win32.XInputState newState) == 0)
                {
                    // 找到新的控制器，更新 Index。
                    _userIndex = i;

                    _previousState = newState;

                    _hasPreviousState = true;

                    break;
                }
            }

            return;
        }
        else
        {
            // 連線狀態：重置斷線計數器。
            _reconnectCounter = 0;

            // 判斷目前控制器是否「無效」：雖然讀取成功，但完全沒反應（PacketNumber 沒變，且不是剛連線的第一幀）。
            bool isIdle = _hasPreviousState && 
                currentState.dwPacketNumber == _previousState.dwPacketNumber;

            // 如果目前控制器「很安靜」，嘗試掃描其他控制器是否有訊號。
            if (isIdle)
            {
                for (uint i = 0; i < MaxControllerCount; i++)
                {
                    if (i == _userIndex)
                    {
                        // 跳過自己。
                        continue;
                    }

                    if (Win32.XInputGetState(i, out Win32.XInputState otherState) == 0)
                    {
                        // 只要連線且 PacketNumber > 0，通常就是活躍的實體控制器。
                        if (otherState.dwPacketNumber != 0)
                        {
                            // 找到活躍的控制器！如果它有不同的封包，切換過去！
                            if (!_hasPreviousState || 
                                otherState.dwPacketNumber != _previousState.dwPacketNumber)
                            {
                                _userIndex = i;

                                currentState = otherState;

                                // 重置狀態，避免切換瞬間誤觸發。
                                _hasPreviousState = false;

                                _previousState = default;

                                // 找到就跳出迴圈。
                                break;
                            }
                        }
                    }
                }
            }
        }

        // 將搖桿訊號合併到按鍵狀態中。
        ApplyStickToButtons(ref currentState, _previousState);

        // 只有在 Input 啟用時才處理按鍵。
        if (!_context.IsInputActive)
        {
            _repeatCounter = 0;
            _repeatDirection = null;

            // 即使輸入不活躍，也更新狀態，避免重新啟用時瞬間觸發 Rising Edge。
            _previousState = currentState;
            _hasPreviousState = true;

            return;
        }

        // 備註：
        // dwPacketNumber 僅用於捷徑處理「狀態變更驅動」的邏輯。
        // 即使控制器狀態未發生變化，仍必須持續執行「時間驅動」的行為（例如重複輸入、長按判斷）。
        bool isStateChanged = !_hasPreviousState ||
            currentState.dwPacketNumber != _previousState.dwPacketNumber;

        // 檢查狀態是否改變。
        if (isStateChanged)
        {
            // 更新一般按鈕的「按住」狀態。
            IsLeftShoulderHeld = currentState.Has(Win32.GamepadButton.XINPUT_GAMEPAD_LEFT_SHOULDER);
            IsRightShoulderHeld = currentState.Has(Win32.GamepadButton.XINPUT_GAMEPAD_RIGHT_SHOULDER);
            IsBackHeld = currentState.Has(Win32.GamepadButton.XINPUT_GAMEPAD_BACK);
            IsBHeld = currentState.Has(Win32.GamepadButton.XINPUT_GAMEPAD_B);

            // 處理觸發鍵。
            // 更新「按住」狀態。
            IsLeftTriggerHeld = currentState.Gamepad.bLeftTrigger > TriggerThreshold;
            IsRightTriggerHeld = currentState.Gamepad.bRightTrigger > TriggerThreshold;

            Detect(currentState, _previousState, Win32.GamepadButton.XINPUT_GAMEPAD_DPAD_UP, UpPressed);
            Detect(currentState, _previousState, Win32.GamepadButton.XINPUT_GAMEPAD_DPAD_DOWN, DownPressed);
            Detect(currentState, _previousState, Win32.GamepadButton.XINPUT_GAMEPAD_DPAD_LEFT, LeftPressed);
            Detect(currentState, _previousState, Win32.GamepadButton.XINPUT_GAMEPAD_DPAD_RIGHT, RightPressed);
            Detect(currentState, _previousState, Win32.GamepadButton.XINPUT_GAMEPAD_START, StartPressed);
            Detect(currentState, _previousState, Win32.GamepadButton.XINPUT_GAMEPAD_BACK, BackPressed);
            Detect(currentState, _previousState, Win32.GamepadButton.XINPUT_GAMEPAD_A, APressed);
            Detect(currentState, _previousState, Win32.GamepadButton.XINPUT_GAMEPAD_B, BPressed);
            Detect(currentState, _previousState, Win32.GamepadButton.XINPUT_GAMEPAD_X, XPressed);

            // 偵測事件觸發（Rising Edge：原本沒按 -> 現在按了）。
            // 處理 LT。
            bool wasLtDownBefore = _hasPreviousState &&
                _previousState.Gamepad.bLeftTrigger > TriggerThreshold;

            if (IsLeftTriggerHeld &&
                !wasLtDownBefore)
            {
                LeftTriggerPressed?.Invoke();
            }

            // 處理 RT。
            bool wasRtDownBefore = _hasPreviousState &&
                _previousState.Gamepad.bRightTrigger > TriggerThreshold;

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
    /// 處裡重複
    /// </summary>
    /// <param name="state">Win32.XInputState</param>
    private void HandleRepeat(Win32.XInputState state)
    {
        // 支援上下左右四個方向的長按重複判斷。
        Win32.GamepadButton? gbCurrentDirection =
            state.Has(Win32.GamepadButton.XINPUT_GAMEPAD_DPAD_LEFT) ? Win32.GamepadButton.XINPUT_GAMEPAD_DPAD_LEFT :
            state.Has(Win32.GamepadButton.XINPUT_GAMEPAD_DPAD_RIGHT) ? Win32.GamepadButton.XINPUT_GAMEPAD_DPAD_RIGHT :
            state.Has(Win32.GamepadButton.XINPUT_GAMEPAD_DPAD_UP) ? Win32.GamepadButton.XINPUT_GAMEPAD_DPAD_UP :
            state.Has(Win32.GamepadButton.XINPUT_GAMEPAD_DPAD_DOWN) ? Win32.GamepadButton.XINPUT_GAMEPAD_DPAD_DOWN :
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
        if (gbCurrentDirection == Win32.GamepadButton.XINPUT_GAMEPAD_DPAD_LEFT)
        {
            LeftRepeat?.Invoke();
        }
        else if (gbCurrentDirection == Win32.GamepadButton.XINPUT_GAMEPAD_DPAD_RIGHT)
        {
            RightRepeat?.Invoke();
        }
        else if (gbCurrentDirection == Win32.GamepadButton.XINPUT_GAMEPAD_DPAD_UP)
        {
            UpRepeat?.Invoke();
        }
        else if (gbCurrentDirection == Win32.GamepadButton.XINPUT_GAMEPAD_DPAD_DOWN)
        {
            DownRepeat?.Invoke();
        }
    }

    /// <summary>
    /// 偵測
    /// </summary>
    /// <param name="currentState">目前的 XInputState</param>
    /// <param name="previousState">前一次的 XInputState</param>
    /// <param name="gamepadButton">控制器按鍵</param>
    /// <param name="action">Action</param>
    private static void Detect(
        in Win32.XInputState currentState,
        in Win32.XInputState previousState,
        Win32.GamepadButton gamepadButton,
        Action? action)
    {
        if (currentState.Has(gamepadButton) &&
            !previousState.Has(gamepadButton))
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
            // 開啟 Win32.XInputGetState，若回傳 0（ERROR_SUCCESS）代表該控制器已連接。
            if (Win32.XInputGetState(i, out _) == 0)
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
    /// <param name="currentState">目前的 Win32.XInputState</param>
    /// <param name="previousState">前一次的 Win32.XInputState（用於遲滯判斷）</param>
    private void ApplyStickToButtons(ref Win32.XInputState currentState, Win32.XInputState previousState)
    {
        // 注意：previousState 包含了上一幀的「實體按鍵」+「虛擬搖桿按鍵」的結果，
        // 這正好符合我們需要的遲滯行為（保持狀態）。
        bool wasLeft = previousState.Has(Win32.GamepadButton.XINPUT_GAMEPAD_DPAD_LEFT),
            wasRight = previousState.Has(Win32.GamepadButton.XINPUT_GAMEPAD_DPAD_RIGHT),
            wasUp = previousState.Has(Win32.GamepadButton.XINPUT_GAMEPAD_DPAD_UP),
            wasDown = previousState.Has(Win32.GamepadButton.XINPUT_GAMEPAD_DPAD_DOWN);

        // 決定閾值：
        // - 如果原本是 ON，使用較低的 Exit 閾值（讓它更容易保持 ON，不容易斷）。
        // - 如果原本是 OFF，使用較高的 Enter 閾值（需要推得夠深才觸發）。
        int thresholdLeft = wasLeft ? _thumbDeadzoneExit : _thumbDeadzoneEnter,
            thresholdRight = wasRight ? _thumbDeadzoneExit : _thumbDeadzoneEnter,
            thresholdUp = wasUp ? _thumbDeadzoneExit : _thumbDeadzoneEnter,
            thresholdDown = wasDown ? _thumbDeadzoneExit : _thumbDeadzoneEnter;

        // 處理 X 軸（左右）。
        if (currentState.Gamepad.sThumbLX < -thresholdLeft)
        {
            // 搖桿向左 -> 視為按下 D-Pad Left。
            currentState.Gamepad.wButtons |= (ushort)Win32.GamepadButton.XINPUT_GAMEPAD_DPAD_LEFT;
        }
        else if (currentState.Gamepad.sThumbLX > thresholdRight)
        {
            // 搖桿向右 -> 視為按下 D-Pad Right。
            currentState.Gamepad.wButtons |= (ushort)Win32.GamepadButton.XINPUT_GAMEPAD_DPAD_RIGHT;
        }

        // 處理 Y 軸（上下）。
        if (currentState.Gamepad.sThumbLY < -thresholdDown)
        {
            // 搖桿向下 -> 視為按下 D-Pad Down。
            currentState.Gamepad.wButtons |= (ushort)Win32.GamepadButton.XINPUT_GAMEPAD_DPAD_DOWN;
        }
        else if (currentState.Gamepad.sThumbLY > thresholdUp)
        {
            // 搖桿向上 -> 視為按下 D-Pad Up。
            currentState.Gamepad.wButtons |= (ushort)Win32.GamepadButton.XINPUT_GAMEPAD_DPAD_UP;
        }
    }

    /// <summary>
    /// 震動
    /// </summary>
    /// <param name="strength">強度</param>
    /// <param name="milliseconds">毫秒，預設為 60</param>
    /// <returns>Task</returns>
    public async Task VibrateAsync(
        ushort strength,
        int milliseconds = 60)
    {
        // 每次呼叫時產生一個新的通行證（Token）。
        int currentToken = Interlocked.Increment(ref _vibrationToken);

        Win32.XInputVibration vibration = new()
        {
            wLeftMotorSpeed = strength,
            wRightMotorSpeed = strength
        };

        _ = Win32.XInputSetState(_userIndex, ref vibration);

        await Task.Delay(milliseconds).ConfigureAwait(false);

        // 檢查：1. 是否已處置／2. 我的通行證是不是最新的？
        // 如果通行證過期了（代表有新的震動蓋過去了），就不要去停止它。
        if (_disposed ||
            currentToken != _vibrationToken)
        {
            return;
        }

        Win32.XInputVibration stopVibration = default;

        _ = Win32.XInputSetState(_userIndex, ref stopVibration);
    }

    /// <summary>
    /// 暫停
    /// </summary>
    public void Pause()
    {
        StopPolling();
    }

    /// <summary>
    /// 恢復
    /// </summary>
    public void Resume()
    {
        if (_disposed)
        {
            return;
        }

        // 如果為 null（已停止）或已被取消，都重新啟動。
        if (_ctsPolling == null ||
            _ctsPolling.IsCancellationRequested)
        {
            StartPolling();
        }
    }

    /// <summary>
    /// 非同步處置
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

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

        // 發出取消訊號。
        StopPolling();

        // 清除所有事件訂閱。
        // 這是 Dispose() 中的關鍵安全措施：
        // 即使背景 Polling Task 在取消後仍多執行一次 Poll()，
        // 也不會再觸發任何外部程式碼，避免 UI 已釋放後被開啟。
        ClearAllEvents();

        // 停止震動與釋放資源。
        DisposeResources();
    }

    /// <summary>
    /// 處置資源
    /// </summary>
    private void DisposeResources()
    {
        // 停止震動。
        Win32.XInputVibration stopVibration = default;

        _ = Win32.XInputSetState(_userIndex, ref stopVibration);

        // 處置 Token Source。
        _ctsPolling?.Dispose();
        _ctsPolling = null;
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
        APressed = null;
        BPressed = null;
        XPressed = null;
        UpRepeat = null;
        DownRepeat = null;
        LeftRepeat = null;
        RightRepeat = null;
        LeftTriggerPressed = null;
        RightTriggerPressed = null;
    }
}