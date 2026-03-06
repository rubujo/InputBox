using GameInputDotNet;
using GameInputDotNet.Interop.Enums;
using GameInputDotNet.Interop.Structs;
using GameInputDotNet.States;
using InputBox.Core.Configuration;
using System.Diagnostics;

namespace InputBox.Core.Input;

/// <summary>
/// 遊戲手把控制器控制器（GameInput 實作）
/// </summary>
internal sealed partial class GameInputGamepadController : IGamepadController
{
    /// <summary>
    /// IInputContext
    /// </summary>
    private readonly IInputContext _context;

    /// <summary>
    /// GamepadRepeatSettings
    /// </summary>
    private readonly GamepadRepeatSettings _repeatSettings;

    /// <summary>
    /// GameInput 實體
    /// </summary>
    private GameInput? _gameInput;

    /// <summary>
    /// GameInput 設備
    /// </summary>
    private GameInputDevice? _device;

    /// <summary>
    /// 控制器的 User Index
    /// </summary>
    private int _userIndex;

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
    /// 前一次的 GamepadStateSnapshot
    /// </summary>
    private GamepadStateSnapshot? _previousState;

    /// <summary>
    /// 重複計數器
    /// </summary>
    private int _repeatCounter;

    /// <summary>
    /// 是否有前一次的 GamepadStateSnapshot
    /// </summary>
    private bool _hasPreviousState;

    /// <summary>
    /// 是否已處置
    /// </summary>
    private volatile bool _disposed;

    /// <summary>
    /// 控制器按鈕重複方向
    /// </summary>
    private GameInputGamepadButtons? _repeatDirection;

    /// <summary>
    /// 輪詢間隔（毫秒），約 60 FPS
    /// </summary>
    private const int PollingIntervalMs = 16;

    /// <summary>
    /// 斷線重連的降頻計數閾值
    /// </summary>
    private const int ReconnectThreshold = 30;

    /// <summary>
    /// 搖桿死區觸發閾值（Enter）
    /// </summary>
    private readonly int _thumbDeadzoneEnter;

    /// <summary>
    /// 搖桿死區重置閾值（Exit）
    /// </summary>
    private readonly int _thumbDeadzoneExit;

    /// <summary>
    /// 觸發鍵閾值（GameInput 標準：0.12）
    /// </summary>
    private const float TriggerThreshold = 0.12f;

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
    /// 震動 Token
    /// </summary>
    private int _vibrationToken = 0;

    /// <summary>
    /// GameInputGamepadController
    /// </summary>
    /// <param name="context">IInputContext</param>
    /// <param name="userIndex">控制器的索引</param>
    /// <param name="repeatSettings">重複設定</param>
    public GameInputGamepadController(
        IInputContext context,
        int userIndex = 0,
        GamepadRepeatSettings? repeatSettings = null)
    {
        _context = context;
        _userIndex = userIndex;
        _repeatSettings = repeatSettings ?? new();
        _repeatSettings.Validate();

        AppSettings settings = AppSettings.Current;

        _thumbDeadzoneEnter = settings.ThumbDeadzoneEnter;

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

            }
            finally
            {
                cts.Dispose();
            }
        }
    }

    /// <summary>
    /// 背景輪詢迴圈
    /// </summary>
    private async Task PollingLoopAsync(CancellationToken cancellationToken)
    {
        // 確保初始化是在 MTA 執行緒中執行（懶載入）。
        if (_gameInput == null)
        {
            try
            {
                _gameInput = GameInput.Create();

                // 使用預設政策，僅在前景時讀取輸入。
                _gameInput.SetFocusPolicy(GameInputFocusPolicy.Default);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GameInput 在背景執行緒初始化失敗：{ex.Message}");
            }
        }

        try
        {
            using PeriodicTimer periodicTimer = new(TimeSpan.FromMilliseconds(PollingIntervalMs));

            while (await periodicTimer.WaitForNextTickAsync(cancellationToken))
            {
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

        }
        catch (Exception ex)
        {
            Debug.WriteLine($"GameInput 輪詢迴圈錯誤：{ex.Message}");
        }
    }

    /// <summary>
    /// 輪詢
    /// </summary>
    private void Poll()
    {
        if (_gameInput == null)
        {
            return;
        }

        // 檢查裝置狀態。
        if (_device == null)
        {
            _reconnectCounter++;

            if (_reconnectCounter >= ReconnectThreshold)
            {
                _reconnectCounter = 0;

                TryFindDevice();
            }

            return;
        }

        using GameInputReading? reading = _gameInput.GetCurrentReading(GameInputKind.Gamepad, _device);

        if (reading == null)
        {
            // 裝置可能斷開。
            _device = null;
            _hasPreviousState = false;

            return;
        }

        GamepadStateSnapshot? currentState = reading.GetGamepadState();

        if (currentState != null)
        {
            _reconnectCounter = 0;

            // 閒置偵測：若目前控制器沒動作且不是第一幀，嘗試掃描其他控制器。
            bool isIdle = _hasPreviousState && 
                currentState.Buttons == _previousState!.Buttons &&
                Math.Abs(currentState.LeftThumbstickX - _previousState.LeftThumbstickX) < 0.01f &&
                Math.Abs(currentState.LeftThumbstickY - _previousState.LeftThumbstickY) < 0.01f;

            if (isIdle)
            {
                ScanForActiveDevice();
            }

            ProcessState(currentState);
        }
    }

    /// <summary>
    /// 嘗試尋找裝置
    /// </summary>
    private void TryFindDevice()
    {
        if (_gameInput == null)
        {
            return;
        }

        // GameInput 透過列舉獲取裝置。
        IReadOnlyList<GameInputDevice> devices = _gameInput.EnumerateDevices(GameInputKind.Gamepad);

        if (devices.Count > _userIndex)
        {
            _device = devices[_userIndex];
            _hasPreviousState = false;
        }
        else if (devices.Count > 0)
        {
            _device = devices[0];
            _userIndex = 0;
            _hasPreviousState = false;
        }
    }

    /// <summary>
    /// 掃描目前是否有其他正在活動的裝置，若有則切換（對齊 XInput 行為）。
    /// </summary>
    private void ScanForActiveDevice()
    {
        if (_gameInput == null)
        {
            return;
        }

        IReadOnlyList<GameInputDevice> devices = _gameInput.EnumerateDevices(GameInputKind.Gamepad);

        for (int i = 0; i < devices.Count; i++)
        {
            if (i == _userIndex)
            {
                continue;
            }

            using GameInputReading? reading = _gameInput.GetCurrentReading(GameInputKind.Gamepad, devices[i]);
            GamepadStateSnapshot? state = reading?.GetGamepadState();

            if (state != null)
            {
                // 若其他控制器有明顯的動作（按下任何按鈕或推動搖桿），則切換過去。
                if (state.Buttons != 0 || 
                    state.LeftTrigger > 0.1f || 
                    state.RightTrigger > 0.1f)
                {
                    _device = devices[i];
                    _userIndex = i;
                    _hasPreviousState = false;

                    break;
                }
            }
        }
    }

    /// <summary>
    /// 處理狀態
    /// </summary>
    private void ProcessState(GamepadStateSnapshot currentState)
    {
        // 直接使用 GameInput 的按鈕旗標，避免不必要的 XInput 映射。
        GameInputGamepadButtons currentButtons = currentState.Buttons;

        // 處理搖桿模擬 D-Pad。
        short thumbLX = (short)(currentState.LeftThumbstickX * 32767),
            thumbLY = (short)(currentState.LeftThumbstickY * 32767);

        ApplyStickToButtons(ref currentButtons, thumbLX, thumbLY);

        if (!_context.IsInputActive)
        {
            _repeatCounter = 0;
            _repeatDirection = null;
            _previousState = currentState;
            _hasPreviousState = true;

            return;
        }

        // 偵測事件觸發。
        DetectRisingEdge(currentButtons, currentState);

        // 處理重複輸入。
        HandleRepeat(currentButtons);

        _previousState = currentState;
        _hasPreviousState = true;
    }

    /// <summary>
    /// 將搖桿映射至 D-Pad 按鈕
    /// </summary>
    private void ApplyStickToButtons(ref GameInputGamepadButtons currentButtons, short thumbLX, short thumbLY)
    {
        bool wasLeft = _repeatDirection == GameInputGamepadButtons.DPadLeft,
            wasRight = _repeatDirection == GameInputGamepadButtons.DPadRight,
            wasUp = _repeatDirection == GameInputGamepadButtons.DPadUp,
            wasDown = _repeatDirection == GameInputGamepadButtons.DPadDown;

        int thresholdLeft = wasLeft ? _thumbDeadzoneExit : _thumbDeadzoneEnter,
            thresholdRight = wasRight ? _thumbDeadzoneExit : _thumbDeadzoneEnter,
            thresholdUp = wasUp ? _thumbDeadzoneExit : _thumbDeadzoneEnter,
            thresholdDown = wasDown ? _thumbDeadzoneExit : _thumbDeadzoneEnter;

        if (thumbLX < -thresholdLeft)
        {
            currentButtons |= GameInputGamepadButtons.DPadLeft;
        }
        else if (thumbLX > thresholdRight)
        {
            currentButtons |= GameInputGamepadButtons.DPadRight;
        }

        if (thumbLY < -thresholdDown)
        {
            currentButtons |= GameInputGamepadButtons.DPadDown;
        }
        else if (thumbLY > thresholdUp)
        {
            currentButtons |= GameInputGamepadButtons.DPadUp;
        }
    }

    /// <summary>
    /// 偵測 Rising Edge
    /// </summary>
    private void DetectRisingEdge(GameInputGamepadButtons currentButtons, GamepadStateSnapshot currentState)
    {
        GameInputGamepadButtons prevButtons = _hasPreviousState ? _previousState!.Buttons : 0;

        // 更新按住狀態（使用 GameInput 原生旗標）。
        IsLeftShoulderHeld = currentButtons.HasFlag(GameInputGamepadButtons.LeftShoulder);
        IsRightShoulderHeld = currentButtons.HasFlag(GameInputGamepadButtons.RightShoulder);
        IsBackHeld = currentButtons.HasFlag(GameInputGamepadButtons.View);
        IsBHeld = currentButtons.HasFlag(GameInputGamepadButtons.B);
        IsLeftTriggerHeld = currentState.LeftTrigger > TriggerThreshold;
        IsRightTriggerHeld = currentState.RightTrigger > TriggerThreshold;

        // 偵測按下事件。
        if (currentButtons.HasFlag(GameInputGamepadButtons.DPadUp) && 
            !prevButtons.HasFlag(GameInputGamepadButtons.DPadUp))
        {
            UpPressed?.Invoke();
        }

        if (currentButtons.HasFlag(GameInputGamepadButtons.DPadDown) && 
            !prevButtons.HasFlag(GameInputGamepadButtons.DPadDown))
        {
            DownPressed?.Invoke();
        }

        if (currentButtons.HasFlag(GameInputGamepadButtons.DPadLeft) && 
            !prevButtons.HasFlag(GameInputGamepadButtons.DPadLeft))
        {
            LeftPressed?.Invoke();
        }

        if (currentButtons.HasFlag(GameInputGamepadButtons.DPadRight) && 
            !prevButtons.HasFlag(GameInputGamepadButtons.DPadRight))
        {
            RightPressed?.Invoke();
        }

        if (currentButtons.HasFlag(GameInputGamepadButtons.Menu) && 
            !prevButtons.HasFlag(GameInputGamepadButtons.Menu))
        {
            StartPressed?.Invoke();
        }

        if (currentButtons.HasFlag(GameInputGamepadButtons.View) && 
            !prevButtons.HasFlag(GameInputGamepadButtons.View))
        {
            BackPressed?.Invoke();
        }

        if (currentButtons.HasFlag(GameInputGamepadButtons.A) && 
            !prevButtons.HasFlag(GameInputGamepadButtons.A))
        {
            APressed?.Invoke();
        }

        if (currentButtons.HasFlag(GameInputGamepadButtons.B) && 
            !prevButtons.HasFlag(GameInputGamepadButtons.B))
        {
            BPressed?.Invoke();
        }

        if (currentButtons.HasFlag(GameInputGamepadButtons.X) && 
            !prevButtons.HasFlag(GameInputGamepadButtons.X))
        {
            XPressed?.Invoke();
        }

        bool prevLtDown = _hasPreviousState && 
            _previousState!.LeftTrigger > TriggerThreshold;

        if (IsLeftTriggerHeld && 
            !prevLtDown)
        {
            LeftTriggerPressed?.Invoke();
        }

        bool prevRtDown = _hasPreviousState && 
            _previousState!.RightTrigger > TriggerThreshold;

        if (IsRightTriggerHeld && 
            !prevRtDown)
        {
            RightTriggerPressed?.Invoke();
        }
    }

    /// <summary>
    /// 處理重複
    /// </summary>
    private void HandleRepeat(GameInputGamepadButtons buttons)
    {
        GameInputGamepadButtons? currentDir =
            buttons.HasFlag(GameInputGamepadButtons.DPadLeft) ? GameInputGamepadButtons.DPadLeft :
            buttons.HasFlag(GameInputGamepadButtons.DPadRight) ? GameInputGamepadButtons.DPadRight :
            buttons.HasFlag(GameInputGamepadButtons.DPadUp) ? GameInputGamepadButtons.DPadUp :
            buttons.HasFlag(GameInputGamepadButtons.DPadDown) ? GameInputGamepadButtons.DPadDown :
            null;

        if (currentDir == null)
        {
            _repeatCounter = 0;
            _repeatDirection = null;

            return;
        }

        if (_repeatDirection != currentDir)
        {
            _repeatCounter = 0;
            _repeatDirection = currentDir;

            return;
        }

        _repeatCounter++;

        // 依照 XInput 版本，使用設定檔的延遲與間隔。
        if (_repeatCounter < _repeatSettings.InitialDelayFrames)
        {
            return;
        }

        if (_repeatCounter % _repeatSettings.IntervalFrames != 0)
        {
            return;
        }

        if (currentDir == GameInputGamepadButtons.DPadLeft)
        {
            LeftRepeat?.Invoke();
        }
        else if (currentDir == GameInputGamepadButtons.DPadRight)
        {
            RightRepeat?.Invoke();
        }
        else if (currentDir == GameInputGamepadButtons.DPadUp)
        {
            UpRepeat?.Invoke();
        }
        else if (currentDir == GameInputGamepadButtons.DPadDown)
        {
            DownRepeat?.Invoke();
        }
    }

    /// <summary>
    /// 震動
    /// </summary>
    /// <param name="strength">強度</param>
    /// <param name="milliseconds">持續時間</param>
    public Task VibrateAsync(ushort strength, int milliseconds = 60)
    {
        if (_device == null)
        {
            return Task.CompletedTask;
        }

        try
        {
            // 檢查硬體是否支援震動，
            // 透過 GetDeviceInfo() 取得裝置資訊，
            // 如果設備完全不具備任何震動馬達，直接返回，節省 Task 資源。
            GameInputDeviceInfo deviceInfo = _device.GetDeviceInfo();

            if (deviceInfo.SupportedRumbleMotors == GameInputRumbleMotors.None)
            {
                return Task.CompletedTask;
            }
        }
        catch
        {
            // 若取得 GameInputDeviceInfo 失敗，容錯處理：不阻擋，交由底層 API 自動忽略。
        }

        // 確保在 MTA（背景）執行緒呼叫 GameInput COM 物件。
        return Task.Run(async () =>
        {
            int token = Interlocked.Increment(ref _vibrationToken);

            // 強度。
            float intensity = strength / 65535f;

            // GameInput API 接收 0.0f ~ 1.0f，不支援特定馬達的手把會自動忽略該欄位。
            GameInputRumbleParams rumble = new()
            {
                // 左方大馬達（低頻／重）。
                LowFrequency = intensity,
                // 右方小馬達（高頻／輕）。
                HighFrequency = intensity,
                // 左板機馬達（Xbox 控制器專有）。
                LeftTrigger = intensity,
                // 右板機馬達（Xbox 控制器專有）。
                RightTrigger = intensity
            };

            try
            {
                _device.SetRumbleState(rumble);

                await Task.Delay(milliseconds).ConfigureAwait(false);

                if (_disposed ||
                    token != _vibrationToken)
                {
                    return;
                }

                // 停止震動（全歸零）。
                _device.SetRumbleState(new GameInputRumbleParams());
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GameInput 震動發生錯誤：{ex.Message}");
            }
        });
    }

    /// <summary>
    /// 暫替
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
    /// 非同步處置
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        await StopPollingAsync().ConfigureAwait(false);

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

        StopPolling();
        DisposeResources();
    }

    /// <summary>
    /// 停止輪詢（非同步）
    /// </summary>
    private Task StopPollingAsync()
    {
        StopPolling();

        return _taskPolling ?? Task.CompletedTask;
    }

    /// <summary>
    /// 處置資源
    /// </summary>
    private void DisposeResources()
    {
        try
        {
            // 如果是在 STA 執行緒被處置，這裡同樣可能拋出例外，因此加以捕捉避免閃退。
            _device?.SetRumbleState(new GameInputRumbleParams());
        }
        catch
        {
            // 忽略 COM 例外。
        }

        _ctsPolling?.Dispose();
        _gameInput?.Dispose();
    }
}
