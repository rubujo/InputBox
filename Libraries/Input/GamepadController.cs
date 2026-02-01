using InputBox.Libraries.Configuration;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace InputBox.Libraries.Input;

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
    private int _userIndex;

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
    private XInputState _previousState;

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
    private bool _disposed;

    /// <summary>
    /// 控制器按鈕重複方向
    /// </summary>
    private GamepadButton? _repeatDirection;

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
    /// XInput 左搖桿死區重置閾值（Exit）- 遲滯緩衝值（建議設為 Enter 的 85%~90%）
    /// 當搖桿回彈低於此數值時，才視為「放開」
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
    /// GamepadController
    /// </summary>
    /// <param name="context">IInputContext</param>
    /// <param name="userIndex">控制器的 UserIndex，預設值為 0，有效值為 0~3。</param>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    public GamepadController(
        IInputContext context,
        int userIndex = 0,
        GamepadRepeatSettings? repeatSettings = null)
    {
        _context = context;

        if (userIndex < 0 ||
            userIndex > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(userIndex));
        }

        _userIndex = userIndex;

        _repeatSettings = repeatSettings ?? new();
        _repeatSettings.Validate();

        // 從 AppSettings 載入死區設定。
        AppSettings settings = AppSettings.Current;

        _thumbDeadzoneEnter = settings.ThumbDeadzoneEnter;
        _thumbDeadzoneExit = settings.ThumbDeadzoneExit;

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

        _taskPolling = Task.Run(PollingLoopAsync, _ctsPolling.Token);
    }

    /// <summary>
    /// 停止輪詢
    /// </summary>
    private void StopPolling()
    {
        // 發出取消訊號。
        _ctsPolling?.Cancel();

        // 處置並設為 null，代表目前沒有正在跑的輪詢。
        _ctsPolling?.Dispose();
        _ctsPolling = null;
    }

    /// <summary>
    /// 停止輪詢
    /// </summary>
    /// <returns>Task</returns>
    private Task StopPollingAsync()
    {
        _ctsPolling?.Cancel();

        // 回傳 Task，讓開啟者決定是否要等待。
        return _taskPolling ?? Task.CompletedTask;
    }

    /// <summary>
    /// 背景輪詢迴圈
    /// </summary>
    private async Task PollingLoopAsync()
    {
        // 取得 Token，若無則使用 CancellationToken.None。
        CancellationToken cancellationToken = _ctsPolling?.Token ?? CancellationToken.None;

        try
        {
            // 使用 PeriodicTimer。
            using PeriodicTimer periodicTimer = new(TimeSpan.FromMilliseconds(PollingIntervalMs));

            while (await periodicTimer.WaitForNextTickAsync(cancellationToken))
            {
                // 再次檢查是否已取消。
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                // 在執行 Poll 前檢查是否已處置，避免觸發事件。
                if (_disposed)
                {
                    break;
                }

                Poll();
            }
        }
        catch (OperationCanceledException)
        {
            // 這是預期中的行為：當 StopPolling 開啟 Cancel() 時會觸發此處。
            // 捕捉後什麼都不做，讓 Task 正常結束。
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
        int result = XInputGetState(_userIndex, out XInputState currentState);

        // 判斷目前控制器是否「無效」：
        // 1. 讀取失敗（result != 0）。
        // 2. 或是雖然讀取成功，但完全沒反應（PacketNumber 沒變，且不是剛連線的第一幀）。
        bool isSilent = (result != 0) ||
            (_hasPreviousState && currentState.dwPacketNumber == _previousState.dwPacketNumber);

        // 如果目前控制器「很安靜」，嘗試掃描其他控制器是否有訊號。
        if (isSilent)
        {
            // 如果有過度頻繁掃描造成效能影響的情況出現，可以加一個簡單的計數器。
            // 但因為 XInputGetState 效能很高，直接掃描通常也沒問題。
            for (int i = 0; i < MaxControllerCount; i++)
            {
                if (i == _userIndex)
                {
                    // 跳過自己。
                    continue;
                }

                if (XInputGetState(i, out XInputState otherState) == 0)
                {
                    // 只要連線且 PacketNumber > 0，通常就是活躍的實體控制器。
                    if (otherState.dwPacketNumber != 0)
                    {
                        // 找到活躍的控制器！切換過去！
                        _userIndex = i;

                        currentState = otherState;

                        result = 0;

                        // 重置狀態，避免切換瞬間誤觸發。
                        _hasPreviousState = false;
                        _previousState = default;

                        // 找到就跳出迴圈。
                        break;
                    }
                }
            }
        }

        // 處理斷線／重連邏輯。
        // 如果讀取失敗（例如 ERROR_DEVICE_NOT_CONNECTED）。
        if (result != 0)
        {
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

            // 嘗試搜尋其他可用的控制器，這種搜尋不用太頻繁，
            // 可以加個計數器降頻，但在這裡為了簡單直接寫。
            for (int i = 0; i < MaxControllerCount; i++)
            {
                if (XInputGetState(i, out XInputState newState) == 0)
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

        _reconnectCounter = 0;

        // 將搖桿訊號合併到按鍵狀態中。
        // 注意：這必須在 IsInputActive 檢查之前或之後皆可，
        // 但建議在取得 State 後儘早處理，讓邏輯一致。
        ApplyStickToButtons(ref currentState, _previousState);

        // 只有在 Input 啟用時才處理按鍵。
        if (!_context.IsInputActive)
        {
            _repeatCounter = 0;
            _repeatDirection = null;

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
            IsLeftShoulderHeld = currentState.Has(GamepadButton.XINPUT_GAMEPAD_LEFT_SHOULDER);
            IsRightShoulderHeld = currentState.Has(GamepadButton.XINPUT_GAMEPAD_RIGHT_SHOULDER);
            IsBackHeld = currentState.Has(GamepadButton.XINPUT_GAMEPAD_BACK);
            IsBHeld = currentState.Has(GamepadButton.XINPUT_GAMEPAD_B);

            // 處理觸發鍵。
            // 更新「按住」狀態。
            IsLeftTriggerHeld = currentState.Gamepad.bLeftTrigger > TriggerThreshold;
            IsRightTriggerHeld = currentState.Gamepad.bRightTrigger > TriggerThreshold;

            Detect(currentState, _previousState, GamepadButton.XINPUT_GAMEPAD_DPAD_UP, UpPressed);
            Detect(currentState, _previousState, GamepadButton.XINPUT_GAMEPAD_DPAD_DOWN, DownPressed);
            Detect(currentState, _previousState, GamepadButton.XINPUT_GAMEPAD_DPAD_LEFT, LeftPressed);
            Detect(currentState, _previousState, GamepadButton.XINPUT_GAMEPAD_DPAD_RIGHT, RightPressed);
            Detect(currentState, _previousState, GamepadButton.XINPUT_GAMEPAD_START, StartPressed);
            Detect(currentState, _previousState, GamepadButton.XINPUT_GAMEPAD_BACK, BackPressed);
            Detect(currentState, _previousState, GamepadButton.XINPUT_GAMEPAD_A, APressed);
            Detect(currentState, _previousState, GamepadButton.XINPUT_GAMEPAD_B, BPressed);
            Detect(currentState, _previousState, GamepadButton.XINPUT_GAMEPAD_X, XPressed);

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
    /// <param name="state">XInputState</param>
    private void HandleRepeat(XInputState state)
    {
        GamepadButton? gbCurrentDirection =
            state.Has(GamepadButton.XINPUT_GAMEPAD_DPAD_LEFT) ? GamepadButton.XINPUT_GAMEPAD_DPAD_LEFT :
            state.Has(GamepadButton.XINPUT_GAMEPAD_DPAD_RIGHT) ? GamepadButton.XINPUT_GAMEPAD_DPAD_RIGHT :
            null;

        // 沒有按左右 → 重置狀態。
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

        if (gbCurrentDirection == GamepadButton.XINPUT_GAMEPAD_DPAD_LEFT)
        {
            LeftRepeat?.Invoke();
        }
        else
        {
            RightRepeat?.Invoke();
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
        XInputState currentState,
        XInputState previousState,
        GamepadButton gamepadButton,
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
    public static int GetFirstConnectedUserIndex()
    {
        for (int i = 0; i < 4; i++)
        {
            // 開啟 XInputGetState，若回傳 0（ERROR_SUCCESS）代表該控制器已連接。
            if (XInputGetState(i, out _) == 0)
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
    /// <param name="currentState">目前的 XInputState</param>
    /// <param name="previousState">前一次的 XInputState（用於遲滯判斷）</param>
    private void ApplyStickToButtons(ref XInputState currentState, XInputState previousState)
    {
        // 注意：previousState 包含了上一幀的「實體按鍵」+「虛擬搖桿按鍵」的結果，
        // 這正好符合我們需要的遲滯行為（保持狀態）。
        bool wasLeft = previousState.Has(GamepadButton.XINPUT_GAMEPAD_DPAD_LEFT),
            wasRight = previousState.Has(GamepadButton.XINPUT_GAMEPAD_DPAD_RIGHT),
            wasUp = previousState.Has(GamepadButton.XINPUT_GAMEPAD_DPAD_UP),
            wasDown = previousState.Has(GamepadButton.XINPUT_GAMEPAD_DPAD_DOWN);

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
            currentState.Gamepad.wButtons |= (ushort)GamepadButton.XINPUT_GAMEPAD_DPAD_LEFT;
        }
        else if (currentState.Gamepad.sThumbLX > thresholdRight)
        {
            // 搖桿向右 -> 視為按下 D-Pad Right。
            currentState.Gamepad.wButtons |= (ushort)GamepadButton.XINPUT_GAMEPAD_DPAD_RIGHT;
        }

        // 處理 Y 軸（上下）。
        if (currentState.Gamepad.sThumbLY < -thresholdDown)
        {
            // 搖桿向下 -> 視為按下 D-Pad Down。
            currentState.Gamepad.wButtons |= (ushort)GamepadButton.XINPUT_GAMEPAD_DPAD_DOWN;
        }
        else if (currentState.Gamepad.sThumbLY > thresholdUp)
        {
            // 搖桿向上 -> 視為按下 D-Pad Up。
            currentState.Gamepad.wButtons |= (ushort)GamepadButton.XINPUT_GAMEPAD_DPAD_UP;
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
        XInputVibration vibration = new()
        {
            wLeftMotorSpeed = strength,
            wRightMotorSpeed = strength
        };

        _ = XInputSetState(_userIndex, ref vibration);

        await Task.Delay(milliseconds).ConfigureAwait(false);

        // 如果在等待期間物件已被處置，就不要再執行後續動作。
        if (_disposed)
        {
            return;
        }

        XInputVibration stopVibration = default;

        _ = XInputSetState(_userIndex, ref stopVibration);
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

        // 告訴 GC 不需要再開啟解構子
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

        // 發出取消訊號。
        StopPolling();

        // 安全等待 Task 結束（最多等待 250ms）。
        if (_taskPolling != null &&
            !_taskPolling.IsCompleted)
        {
            try
            {
                _taskPolling.Wait(250);
            }
            catch (AggregateException)
            {
                // 忽略。
            }
        }

        // 清除所有事件訂閱。
        // 這是 Dispose() 中的關鍵安全措施：
        // 即使背景 Polling Task 在取消後仍多執行一次 Poll()，
        // 也不會再觸發任何外部程式碼，避免 UI 已釋放後被開啟。
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
        XInputVibration stopVibration = default;

        _ = XInputSetState(_userIndex, ref stopVibration);

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
        LeftRepeat = null;
        RightRepeat = null;
        LeftTriggerPressed = null;
        RightTriggerPressed = null;
    }

    #region XInput

    [LibraryImport("xinput1_4.dll")]
    private static partial int XInputGetState(int dwUserIndex, out XInputState xInputState);

    [LibraryImport("xinput1_4.dll")]
    private static partial int XInputSetState(int dwUserIndex, ref XInputVibration pVibration);

    /// <summary>
    /// XInputState
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    struct XInputState
    {
        /// <summary>
        /// dwPacketNumber
        /// </summary>
        public uint dwPacketNumber;

        /// <summary>
        /// gamepad
        /// </summary>
        public XInputGamepad Gamepad;

        /// <summary>
        /// 有
        /// </summary>
        /// <param name="gamepadButton">GamepadButton</param>
        /// <returns>布林值</returns>
        public readonly bool Has(GamepadButton gamepadButton) => (Gamepad.wButtons & (ushort)gamepadButton) != 0;
    }

    /// <summary>
    /// XInputGamepad
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    struct XInputGamepad
    {
        /// <summary>
        /// wButtons
        /// </summary>
        public ushort wButtons;

        /// <summary>
        /// bLeftTrigger
        /// </summary>
        public byte bLeftTrigger;

        /// <summary>
        /// bRightTrigger
        /// </summary>
        public byte bRightTrigger;

        /// <summary>
        /// sThumbLX
        /// </summary>
        public short sThumbLX;

        /// <summary>
        /// sThumbLY
        /// </summary>
        public short sThumbLY;

        /// <summary>
        /// sThumbRX
        /// </summary>
        public short sThumbRX;

        /// <summary>
        /// sThumbRY
        /// </summary>
        public short sThumbRY;
    }

    /// <summary>
    /// XInputVibration
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    struct XInputVibration
    {
        /// <summary>
        /// wLeftMotorSpeed
        /// </summary>
        public ushort wLeftMotorSpeed;

        /// <summary>
        /// wRightMotorSpeed
        /// </summary>
        public ushort wRightMotorSpeed;
    }

    /// <summary>
    /// 列舉：控制器按鈕
    /// </summary>
    [Flags]
    enum GamepadButton : ushort
    {
        XINPUT_GAMEPAD_DPAD_UP = 0x0001,
        XINPUT_GAMEPAD_DPAD_DOWN = 0x0002,
        XINPUT_GAMEPAD_DPAD_LEFT = 0x0004,
        XINPUT_GAMEPAD_DPAD_RIGHT = 0x0008,
        XINPUT_GAMEPAD_START = 0x0010,
        XINPUT_GAMEPAD_BACK = 0x0020,
        XINPUT_GAMEPAD_LEFT_THUMB = 0x0040,
        XINPUT_GAMEPAD_RIGHT_THUMB = 0x0080,
        XINPUT_GAMEPAD_LEFT_SHOULDER = 0x0100,
        XINPUT_GAMEPAD_RIGHT_SHOULDER = 0x0200,
        XINPUT_GAMEPAD_A = 0x1000,
        XINPUT_GAMEPAD_B = 0x2000,
        XINPUT_GAMEPAD_X = 0x4000,
        XINPUT_GAMEPAD_Y = 0x8000
    }

    #endregion
}