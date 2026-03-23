using GameInputDotNet;
using GameInputDotNet.Interop.Enums;
using GameInputDotNet.Interop.Structs;
using GameInputDotNet.States;
using InputBox.Core.Configuration;
using InputBox.Core.Extensions;
using InputBox.Core.Services;
using InputBox.Resources;
using System.Collections.Concurrent;
using System.Diagnostics;
using UsbVendorsLibrary;

namespace InputBox.Core.Input;

/// <summary>
/// Gamepad 控制介面（GameInput 實作）
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
    /// 目前使用的 GameInput 設備
    /// </summary>
    private GameInputDevice? _device;

    /// <summary>
    /// 標記是否需要重新整理設備清單
    /// </summary>
    private volatile bool _needsRefresh = true;

    /// <summary>
    /// 記錄最後一次要求重新整理的時間點 (Tick)
    /// </summary>
    private long _refreshRequestedTicks;

    /// <summary>
    /// 裝置重整的冷卻時間（毫秒）- 用於過濾 ROG Ally 等掌機虛擬裝置切換造成的連發事件
    /// </summary>
    private const int RefreshCooldownMs = 500;

    /// <summary>
    /// 儲存 GameInput 讀取回呼註冊憑證
    /// </summary>
    private GameInput.GameInputCallbackRegistration? _readingCallbackReg;

    /// <summary>
    /// 安全的事件佇列，用於暫存兩次 Timer Tick 之間的所有硬體輸入
    /// </summary>
    private readonly ConcurrentQueue<GamepadStateSnapshot> _readingQueue = new();

    /// <summary>
    /// 保護設備存取的鎖
    /// </summary>
    private readonly Lock _deviceLock = new();

    /// <summary>
    /// GameInput 設備回呼註冊憑證
    /// </summary>
    private GameInput.GameInputCallbackRegistration? _deviceCallbackReg;

    /// <summary>
    /// 所有目前連接的設備清單（用於多控制器自動切換）
    /// </summary>
    private readonly List<GameInputDevice> _allDevices = [];

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
    /// 前一次「包含搖桿模擬方向鍵」的最終按鍵狀態
    /// </summary>
    private GameInputGamepadButtons _previousProcessedButtons;

    /// <summary>
    /// 重複計數器
    /// </summary>
    private int _repeatCounter;

    /// <summary>
    /// 是否有前一次的 GamepadStateSnapshot
    /// </summary>
    private volatile bool _hasPreviousState;

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
    /// 閒置判定閾值
    /// </summary>
    private const float IdleThreshold = 0.01f;

    /// <summary>
    /// 活動判定閾值（用於多控制器切換）
    /// </summary>
    private const float ActiveThreshold = 0.1f;

    /// <summary>
    /// 快取的設備名稱（避免跨執行緒 COM 存取）
    /// </summary>
    private string _cachedDeviceName = string.Empty;

    /// <summary>
    /// 快取的震動支援狀態
    /// </summary>
    private bool _supportsRumble = false;

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
    /// 控制器返回鍵放開
    /// </summary>
    public event Action? BackReleased;

    /// <summary>
    /// 控制器 A 鍵
    /// </summary>
    public event Action? APressed;

    /// <summary>
    /// 取得目前使用的裝置名稱
    /// </summary>
    public string DeviceName => string.Format(Strings.App_Gamepad_Suffix, _cachedDeviceName);

    /// <summary>
    /// 當控制器連線狀態改變時觸發（true: 已連線, false: 已斷開）
    /// </summary>
    public event Action<bool>? ConnectionChanged;

    /// <summary>
    /// 取得目前是否已連線
    /// </summary>
    public bool IsConnected => _hasPreviousState;

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
    /// 震動 Token
    /// </summary>
    private int _vibrationToken = 0;

    /// <summary>
    /// 震動延遲任務的取消權杖來源
    /// </summary>
    private CancellationTokenSource? _vibrationCts;

    /// <summary>
    /// 私有建構子，透過 CreateAsync 建立實體。
    /// </summary>
    /// <param name="context">IInputContext</param>
    /// <param name="repeatSettings">重複設定</param>
    private GameInputGamepadController(
        IInputContext context,
        GamepadRepeatSettings? repeatSettings = null)
    {
        _context = context;
        _repeatSettings = repeatSettings ?? new();
        _repeatSettings.Validate();

        // 註冊至回饋服務以供緊急停止追蹤。
        FeedbackService.RegisterController(this);

        StartPolling();
    }

    /// <summary>
    /// 非同步建立 GameInputGamepadController 實體
    /// </summary>
    /// <param name="context">IInputContext</param>
    /// <param name="repeatSettings">重複設定</param>
    /// <returns>GameInputGamepadController</returns>
    public static async Task<GameInputGamepadController> CreateAsync(
        IInputContext context,
        GamepadRepeatSettings? repeatSettings = null)
    {
        // 探測支援性：在臨時背景執行緒（MTA）嘗試建立實體。
        // 這能確保若系統不支援 GameInput，方法會拋出例外以觸發退避邏輯。
        await Task.Run(() =>
        {
            using GameInput probe = GameInput.Create();
        }).ConfigureAwait(false);

        return new GameInputGamepadController(context, repeatSettings);
    }

    /// <summary>
    /// 啟動背景輪詢
    /// </summary>
    private void StartPolling()
    {
        // 確保先停止舊的。
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
        // 取得目前的 CTS 並將欄位設為 null，確保只會由一個執行緒處理取消。
        CancellationTokenSource? cts = Interlocked.Exchange(ref _ctsPolling, null);

        if (cts != null)
        {
            try
            {
                // 發出取消訊號。
                if (!cts.IsCancellationRequested)
                {
                    cts.Cancel();
                }

                cts.Dispose();
            }
            catch (ObjectDisposedException)
            {

            }
        }
    }

    /// <summary>
    /// 處理裝置斷線與清理邏輯
    /// </summary>
    private void HandleDisconnect()
    {
        if (_hasPreviousState)
        {
            _hasPreviousState = false;
            _previousState = null;
            _previousProcessedButtons = 0;

            // 重置狀態防止殘留，避免放開按鍵的事件遺失。
            ResetHoldStates();

            ConnectionChanged?.Invoke(false);
        }

        // 解除 Callback 註冊。
        _readingCallbackReg?.Dispose();
        _readingCallbackReg = null;

        // 清空事件佇列。
        _readingQueue.Clear();

        if (_device != null)
        {
            // 裝置的處置現在主要由 TryFindDevice 邏輯管理。
            _device = null;

            // 更新快取，讓 DeviceName 等屬性反映斷線狀態。
            UpdateDeviceInfo();
        }
    }

    /// <summary>
    /// 設定裝置讀取回呼
    /// </summary>
    private void SetupReadingCallback()
    {
        _readingCallbackReg?.Dispose();
        _readingCallbackReg = null;

        _readingQueue.Clear();

        if (_device != null && _gameInput != null)
        {
            try
            {
                _readingCallbackReg = _gameInput.RegisterReadingCallback(
                    _device,
                    GameInputKind.Gamepad,
                    (reading) =>
                    {
                        try
                        {
                            GamepadStateSnapshot? state = reading.GetGamepadState();

                            if (state != null)
                            {
                                _readingQueue.Enqueue(state);
                            }
                        }
                        catch
                        {

                        }
                    });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GameInput 註冊 ReadingCallback 失敗：{ex.Message}");
            }
        }
    }

    /// <summary>
    /// 背景輪詢迴圈
    /// </summary>
    /// <param name="cancellationToken">CancellationToken</param>
    /// <returns>Task</returns>
    private async Task PollingLoopAsync(CancellationToken cancellationToken)
    {
        // 在背景執行緒（MTA）建立正式使用的 GameInput 實體。
        // 這樣 COM 物件的單元模型就會與輪詢執行緒一致，解決 InvalidCastException。
        try
        {
            _gameInput = GameInput.Create();
            _gameInput.SetFocusPolicy(GameInputFocusPolicy.Default);

            // 註冊裝置連線／斷線事件。
            // 這裡我們只將旗標設為需要重新整理，真正的列舉會在 Poll 執行緒中安全執行。
            _deviceCallbackReg = _gameInput.RegisterDeviceCallback(
                null,
                GameInputKind.Gamepad,
                GameInputDeviceStatus.Connected,
                // 這會強迫 Windows 系統「立刻」交出設備名單，並瞬間觸發 callback 讓 _needsRefresh 變成 true。
                GameInputEnumerationKind.Blocking,
                (_, _, _, _) =>
                {
                    _needsRefresh = true;

                    // 記錄觸發當下的時間。
                    _refreshRequestedTicks = Stopwatch.GetTimestamp();
                });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"GameInput 在背景執行緒初始化失敗：{ex.Message}");

            return;
        }

        try
        {
            using PeriodicTimer periodicTimer = new(TimeSpan.FromMilliseconds(PollingIntervalMs));

            // 手動執行首發 Poll，搶下第一幀！
            // 既然 Blocking 已經拿到了名單，我們不要白白等待計時器的第一個 16ms 延遲。
            // 直接手動呼叫一次 Poll()，達成「視窗一出現，手把就連上」的秒抓體感！
            if (!cancellationToken.IsCancellationRequested &&
                !_disposed)
            {
                try
                {
                    Poll();
                }
                catch
                {

                }
            }

            while (await periodicTimer.WaitForNextTickAsync(cancellationToken))
            {
                if (_disposed ||
                    cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                try
                {
                    Poll();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"GameInput Poll 發生未預期錯誤：{ex.Message}");
                }
            }
        }
        catch (OperationCanceledException)
        {

        }
        catch (ObjectDisposedException)
        {
            // Timer 已處置。
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
        GameInput? gameInput = _gameInput;

        if (gameInput == null)
        {
            return;
        }

        // 取得目前的設定快照，確保本幀處理邏輯的原子性。
        AppSettings.GamepadConfigSnapshot config = AppSettings.Current.GamepadSettings;

        // 裝置清單防抖與掃描邏輯。
        if (_needsRefresh)
        {
            long elapsedTicks = Stopwatch.GetTimestamp() - _refreshRequestedTicks,
                elapsedMs = elapsedTicks / (Stopwatch.Frequency / 1000);

            if (elapsedMs >= RefreshCooldownMs)
            {
                _reconnectCounter = 0;

                TryFindDevice();
            }
        }
        else if (_device == null)
        {
            _reconnectCounter++;

            if (_reconnectCounter >= ReconnectThreshold)
            {
                _reconnectCounter = 0;

                TryFindDevice();
            }
        }

        GameInputDevice? dev = _device;

        if (dev == null)
        {
            HandleDisconnect();

            return;
        }

        // 從佇列中取出所有事件（Event-Driven）。
        // 分離「邊緣偵測」與「狀態維持」。
        bool hasNewState = false;

        GamepadStateSnapshot? latestSnapshot = null;

        while (_readingQueue.TryDequeue(out GamepadStateSnapshot? state))
        {
            if (state == null)
            {
                continue;
            }

            hasNewState = true;

            _reconnectCounter = 0;

            // 必須處理每一個狀態以偵測「按下」與「放開」的邊緣（Edge），防止漏鍵。
            ProcessEdgeTransitions(state, config);

            latestSnapshot = state;
        }

        // 處理基於時間的邏輯（連發與閒置偵測）。
        if (hasNewState &&
            latestSnapshot != null)
        {
            // 使用最後一個快照作為本 Tick 的最終狀態。
            _previousState = latestSnapshot;
            _hasPreviousState = true;

            // 對齊 Timer Tick 執行連發邏輯，避免因硬體回報頻率過高導致連發過快。
            HandleRepeat(_previousProcessedButtons, config);
        }
        else if (_hasPreviousState &&
            _previousState != null)
        {
            // 如果這 16ms 內沒有任何新狀態，判定閒置或維持按住。
            if (IsStateIdle(_previousState))
            {
                _reconnectCounter++;

                if (_reconnectCounter >= ReconnectThreshold)
                {
                    _reconnectCounter = 0;

                    if (ScanForActiveDevice())
                    {
                        return;
                    }
                }
            }
            else
            {
                _reconnectCounter = 0;

                // 玩家按住按鍵不放且硬體無新事件時，繼續執行連發計時。
                HandleRepeat(_previousProcessedButtons, config);
            }
        }
    }

    /// <summary>
    /// 處理邊緣轉換（按下／放開），確保所有輸入都能被捕捉。
    /// </summary>
    /// <param name="currentState">目前的遊戲控制器狀態快照</param>
    /// <param name="config">遊戲控制器設定快照</param>
    private void ProcessEdgeTransitions(
        GamepadStateSnapshot currentState,
        AppSettings.GamepadConfigSnapshot config)
    {
        GameInputGamepadButtons currentButtons = currentState.Buttons;

        short thumbLX = (short)(currentState.LeftThumbstickX * short.MaxValue),
            thumbLY = (short)(currentState.LeftThumbstickY * short.MaxValue);

        ApplyStickToButtons(ref currentButtons, thumbLX, thumbLY, config);

        if (!_context.IsInputActive)
        {
            _repeatCounter = 0;
            _repeatDirection = null;
            _previousProcessedButtons = currentButtons;

            return;
        }

        // 偵測按下事件。
        DetectRisingEdge(currentButtons, currentState);

        // 儲存加工後的按鍵狀態，給下一個快照或下一幀比對用。
        _previousProcessedButtons = currentButtons;
    }

    /// <summary>
    /// 判定狀態是否為閒置
    /// </summary>
    /// <param name="state">目前的遊戲控制器狀態快照</param>
    /// <returns>若狀態為閒置，則回傳 true；否則回傳 false。</returns>
    private bool IsStateIdle(GamepadStateSnapshot state)
    {
        return _previousState != null &&
            state.Buttons == 0 && _previousState.Buttons == 0 &&
            Math.Abs(state.LeftThumbstickX) < IdleThreshold &&
            Math.Abs(state.LeftThumbstickY) < IdleThreshold &&
            Math.Abs(state.RightThumbstickX) < IdleThreshold &&
            Math.Abs(state.RightThumbstickY) < IdleThreshold &&
            state.LeftTrigger < IdleThreshold &&
            state.RightTrigger < IdleThreshold;
    }

    /// <summary>
    /// 嘗試尋找裝置
    /// </summary>
    private void TryFindDevice()
    {
        GameInput? gameInput = _gameInput;

        if (gameInput == null)
        {
            return;
        }

        lock (_deviceLock)
        {
            _needsRefresh = false;

            // 執行一次完整的列舉。
            IReadOnlyList<GameInputDevice> devices = gameInput.EnumerateDevices(GameInputKind.Gamepad);

            // 1. 釋放清單中已不再存在的舊裝置代理（Zero-allocation 替換 LINQ Any）。
            for (int i = _allDevices.Count - 1; i >= 0; i--)
            {
                GameInputDevice oldDev = _allDevices[i];

                try
                {
                    GameInputDeviceInfo oldInfo = oldDev.GetDeviceInfo();

                    bool stillExists = false;

                    for (int j = 0; j < devices.Count; j++)
                    {
                        if (devices[j].GetDeviceInfo().DeviceId.Equals(oldInfo.DeviceId))
                        {
                            stillExists = true;

                            break;
                        }
                    }

                    if (!stillExists)
                    {
                        if (oldDev == _device)
                        {
                            _device = null;
                        }

                        oldDev.Dispose();

                        _allDevices.RemoveAt(i);
                    }
                }
                catch
                {
                    // 確保即使發生例外，COM 物件的釋放也不會被延遲。
                    if (oldDev == _device)
                    {
                        _device = null;
                    }

                    try
                    {
                        oldDev.Dispose();
                    }
                    catch
                    {
                        // 忽略已經失效的 COM 釋放錯誤。
                    }

                    // 若裝置已失效，則直接移除。
                    _allDevices.RemoveAt(i);
                }
            }

            // 2. 加入新發現的裝置（Zero-allocation 替換 LINQ Any）。
            for (int i = 0; i < devices.Count; i++)
            {
                GameInputDevice newDev = devices[i];

                try
                {
                    GameInputDeviceInfo newInfo = newDev.GetDeviceInfo();

                    bool alreadyTracked = false;

                    for (int j = 0; j < _allDevices.Count; j++)
                    {
                        if (_allDevices[j].GetDeviceInfo().DeviceId.Equals(newInfo.DeviceId))
                        {
                            alreadyTracked = true;

                            break;
                        }
                    }

                    if (!alreadyTracked)
                    {
                        _allDevices.Add(newDev);
                    }
                    else
                    {
                        // 已經存在的，釋放重複產生的 COM 代理物件。
                        newDev.Dispose();
                    }
                }
                catch
                {
                    newDev.Dispose();
                }
            }

            // 3. 確保目前有一個啟動中的裝置。
            if (_allDevices.Count > 0)
            {
                if (_device == null)
                {
                    _device = _allDevices[0];

                    _hasPreviousState = false;

                    UpdateDeviceInfo();
                    InitializeDeviceState();
                    SetupReadingCallback();
                }
            }
            else
            {
                _device = null;

                HandleDisconnect();
            }
        }
    }

    /// <summary>
    /// 掃描目前是否有其他正在活動的裝置，若有則切換
    /// </summary>
    /// <returns>若有切換至其他裝置，則回傳 true；否則回傳 false。</returns>
    private bool ScanForActiveDevice()
    {
        GameInput? gameInput = _gameInput;

        if (gameInput == null)
        {
            return false;
        }

        lock (_deviceLock)
        {
            for (int i = 0; i < _allDevices.Count; i++)
            {
                GameInputDevice otherDev = _allDevices[i];

                if (otherDev == _device)
                {
                    continue;
                }

                try
                {
                    // 檢查其他控制器的活動狀態。
                    using GameInputReading? reading = gameInput.GetCurrentReading(GameInputKind.Gamepad, otherDev);

                    GamepadStateSnapshot? state = reading?.GetGamepadState();

                    if (state != null)
                    {
                        // 若其他控制器有明顯的動作（按下任何按鈕、推動搖桿或按板機），則切換過去。
                        if (state.Buttons != 0 ||
                            state.LeftTrigger > ActiveThreshold ||
                            state.RightTrigger > ActiveThreshold ||
                            Math.Abs(state.LeftThumbstickX) > ActiveThreshold ||
                            Math.Abs(state.LeftThumbstickY) > ActiveThreshold ||
                            Math.Abs(state.RightThumbstickX) > ActiveThreshold ||
                            Math.Abs(state.RightThumbstickY) > ActiveThreshold)
                        {
                            // 重置原本的按鍵狀態，避免按住的按鍵殘留給新控制器。
                            ResetHoldStates();

                            // 切換裝置參考。
                            _device = otherDev;

                            UpdateDeviceInfo();
                            InitializeDeviceState();
                            SetupReadingCallback();

                            return true;
                        }
                    }
                }
                catch
                {
                    // 忽略切換過程中的讀取錯誤。
                }
            }
        }

        return false;
    }

    /// <summary>
    /// 更新快取的裝置資訊（必須在 MTA 執行緒中呼叫）
    /// </summary>
    private void UpdateDeviceInfo()
    {
        GameInputDevice? dev = _device;

        if (dev == null)
        {
            _cachedDeviceName = string.Empty;

            _supportsRumble = false;

            return;
        }

        try
        {
            GameInputDeviceInfo info = dev.GetDeviceInfo();

            // 1. 更新震動支援狀態。
            _supportsRumble = info.SupportedRumbleMotors != GameInputRumbleMotors.None;

            // 2. 更新裝置名稱。
            string displayName = string.Empty;

            if (UsbIds.TryGetVendorName(info.VendorId, out string vendorName))
            {
                displayName = $"{vendorName} ";
            }

            if (UsbIds.TryGetProductName(info.VendorId, info.ProductId, out string productName))
            {
                displayName += $"{productName}";
            }
            else
            {
                displayName += info.GetDisplayName();
            }

            _cachedDeviceName = displayName;
        }
        catch
        {
            _cachedDeviceName = "Unknown Gamepad";
            _supportsRumble = false;
        }
    }

    /// <summary>
    /// 初始化裝置的初始狀態，並觸發連線事件更新 UI
    /// </summary>
    private void InitializeDeviceState()
    {
        if (_device == null ||
            _gameInput == null)
        {
            return;
        }

        try
        {
            using GameInputReading? reading = _gameInput.GetCurrentReading(GameInputKind.Gamepad, _device);

            GamepadStateSnapshot? state = reading?.GetGamepadState();

            if (state != null)
            {
                _previousState = state;

                // 這裡直接使用 state.Buttons 等屬性即可。
                GameInputGamepadButtons currentButtons = state.Buttons;

                short thumbLX = (short)(state.LeftThumbstickX * short.MaxValue),
                    thumbLY = (short)(state.LeftThumbstickY * short.MaxValue);

                // 取得快照以確保初始化時死區校驗一致。
                AppSettings.GamepadConfigSnapshot config = AppSettings.Current.GamepadSettings;

                ApplyStickToButtons(ref currentButtons, thumbLX, thumbLY, config);

                _previousProcessedButtons = currentButtons;

                _hasPreviousState = true;
            }
            else
            {
                // 無法取得狀態時，設為 null 即可。
                _previousState = null;
                _previousProcessedButtons = 0;
                _hasPreviousState = false;
            }
        }
        catch
        {
            _previousState = null;
            _previousProcessedButtons = 0;
            _hasPreviousState = false;
        }

        ConnectionChanged?.Invoke(true);
    }

    /// <summary>
    /// 將搖桿映射至 D-Pad 按鈕
    /// </summary>
    /// <param name="currentButtons">目前的按鈕狀態</param>
    /// <param name="thumbLX">左搖桿 X 軸值</param>
    /// <param name="thumbLY">左搖桿 Y 軸值</param>
    /// <param name="config">遊戲控制器設定快照</param>
    private void ApplyStickToButtons(
        ref GameInputGamepadButtons currentButtons,
        short thumbLX,
        short thumbLY,
        AppSettings.GamepadConfigSnapshot config)
    {
        bool wasLeft = _hasPreviousState &&
                _previousProcessedButtons.HasFlag(GameInputGamepadButtons.DPadLeft),
            wasRight = _hasPreviousState &&
                _previousProcessedButtons.HasFlag(GameInputGamepadButtons.DPadRight),
            wasUp = _hasPreviousState &&
                _previousProcessedButtons.HasFlag(GameInputGamepadButtons.DPadUp),
            wasDown = _hasPreviousState &&
                _previousProcessedButtons.HasFlag(GameInputGamepadButtons.DPadDown);

        int thresholdLeft = wasLeft ? config.ThumbDeadzoneExit : config.ThumbDeadzoneEnter,
            thresholdRight = wasRight ? config.ThumbDeadzoneExit : config.ThumbDeadzoneEnter,
            thresholdUp = wasUp ? config.ThumbDeadzoneExit : config.ThumbDeadzoneEnter,
            thresholdDown = wasDown ? config.ThumbDeadzoneExit : config.ThumbDeadzoneEnter;

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
    /// <param name="currentButtons">目前的按鈕狀態</param>
    /// <param name="currentState">目前的遊戲控制器狀態快照</param>
    private void DetectRisingEdge(
        GameInputGamepadButtons currentButtons,
        GamepadStateSnapshot currentState)
    {
        GameInputGamepadButtons prevButtons = _hasPreviousState ?
            _previousProcessedButtons :
            0;

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

        if (!currentButtons.HasFlag(GameInputGamepadButtons.View) &&
            prevButtons.HasFlag(GameInputGamepadButtons.View))
        {
            BackReleased?.Invoke();
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

        if (currentButtons.HasFlag(GameInputGamepadButtons.Y) &&
            !prevButtons.HasFlag(GameInputGamepadButtons.Y))
        {
            YPressed?.Invoke();
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
    private void HandleRepeat(
        GameInputGamepadButtons buttons,
        AppSettings.GamepadConfigSnapshot config)
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
        if (_repeatCounter < config.RepeatInitialDelayFrames)
        {
            return;
        }

        if (_repeatCounter % config.RepeatIntervalFrames != 0)
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
    public Task VibrateAsync(ushort strength, int milliseconds = 60)
    {
        GameInputDevice? dev = _device;

        if (dev == null ||
            !_supportsRumble)
        {
            return Task.CompletedTask;
        }

        // 強度為 0 時直接停止並回傳，減少 GC 分配（Fast-path）。
        if (strength == 0)
        {
            Task.Run(() =>
            {
                try
                {
                    dev.SetRumbleState(new GameInputRumbleParams());
                }
                catch
                {
                    // 忽略失效設備的錯誤。
                }
            });

            return Task.CompletedTask;
        }

        return Task.Run(async () =>
        {
            int token = Interlocked.Increment(ref _vibrationToken);

            // 取消並更換 CTS，確保只有最後一個震動任務的延遲會執行。
            CancellationTokenSource newCts = new();

            CancellationTokenSource? oldCts = Interlocked.Exchange(ref _vibrationCts, newCts);

            if (oldCts != null)
            {
                try
                {
                    oldCts.Cancel();
                    oldCts.Dispose();
                }
                catch
                {

                }
            }

            CancellationToken ctsToken = newCts.Token;

            // 強度。
            float intensity = strength / 65535f;

            GameInputRumbleParams rumble = new()
            {
                LowFrequency = intensity,
                HighFrequency = intensity,
                LeftTrigger = intensity,
                RightTrigger = intensity
            };

            try
            {
                // 再次檢查狀態。
                if (_disposed ||
                    _device == null ||
                    _device != dev)
                {
                    return;
                }

                dev.SetRumbleState(rumble);

                await Task.Delay(milliseconds, ctsToken).ConfigureAwait(false);

                // 檢查是否已被處置、裝置是否已變更、或是否有更新的震動請求。
                if (_disposed ||
                    token != _vibrationToken ||
                    _device == null ||
                    _device != dev)
                {
                    return;
                }

                // 停止震動（全歸零）。
                dev.SetRumbleState(new GameInputRumbleParams());
            }
            catch (OperationCanceledException)
            {
                // 任務被新的震動請求或釋放動作取消。
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GameInput 震動發生錯誤：{ex.Message}");
            }
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
        CancellationTokenSource? vCts = Interlocked.Exchange(ref _vibrationCts, null);

        if (vCts != null)
        {
            try
            {
                vCts.Cancel();
                vCts.Dispose();
            }
            catch
            {

            }
        }

        await StopPollingAsync().ConfigureAwait(false);

        // 安全清理：確實等待背景清理完成。
        await DisposeResourcesAsync().ConfigureAwait(false);

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
        CancellationTokenSource? vCts = Interlocked.Exchange(ref _vibrationCts, null);

        if (vCts != null)
        {
            try
            {
                vCts.Cancel();
                vCts.Dispose();
            }
            catch
            {

            }
        }

        // 1. 發出取消訊號。
        StopPolling();

        // 2. 清理資源（同步環境下使用 SafeFireAndForget）。
        ClearAllEvents();
        DisposeResourcesAsync().SafeFireAndForget();

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// 停止輪詢（非同步）
    /// </summary>
    private Task StopPollingAsync()
    {
        StopPolling();

        return _taskPolling ??
            Task.CompletedTask;
    }

    /// <summary>
    /// 強制停止震動（用於應用程式關閉等緊急情境）
    /// </summary>
    public void StopVibration()
    {
        GameInputDevice? dev = _device;

        if (dev == null)
        {
            return;
        }

        // 將 COM 呼叫推入 MTA 背景執行緒，避免在 STA (UI) 執行緒引發 InvalidCastException
        Task.Run(() =>
        {
            try
            {
                dev.SetRumbleState(new GameInputRumbleParams());
            }
            catch
            {
                // 忽略
            }
        });
    }

    /// <summary>
    /// 非同步處置資源
    /// </summary>
    private Task DisposeResourcesAsync()
    {
        // 捕獲目前實例以供背景執行緒釋放。
        GameInput? gameInput = _gameInput;
        GameInputDevice? dev = _device;
        GameInput.GameInputCallbackRegistration? deviceCallbackReg = _deviceCallbackReg;
        GameInput.GameInputCallbackRegistration? readingCallbackReg = _readingCallbackReg;
        List<GameInputDevice> devicesToDispose;

        lock (_deviceLock)
        {
            devicesToDispose = [.. _allDevices];

            _allDevices.Clear();
        }

        _device = null;
        _gameInput = null;
        _deviceCallbackReg = null;
        _readingCallbackReg = null;

        _readingQueue.Clear();

        // 使用 Task.Run 確保 COM 物件在 MTA 背景執行緒中釋放。
        // 這能避免在 STA（UI）執行緒釋放時可能引發的 COM 異常或死結。
        return Task.Run(async () =>
        {
            try
            {
                // 先嘗試停止震動。
                dev?.SetRumbleState(new GameInputRumbleParams());

                // 等待輪詢 Task 結束，避免發生清空過程中的競態。
                if (_taskPolling != null)
                {
                    try
                    {
                        await _taskPolling.ConfigureAwait(false);
                    }
                    catch
                    {
                        // 忽略 Task 結束時的任何錯誤。
                    }
                }

                // 移除裝置註冊回呼。
                deviceCallbackReg?.Dispose();
                readingCallbackReg?.Dispose();

                // 釋放所有連線中的裝置代理。
                foreach (GameInputDevice d in devicesToDispose)
                {
                    d.Dispose();
                }

                dev?.Dispose();
                gameInput?.Dispose();
            }
            catch
            {

            }
        });
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
    /// 清除所有事件訂閱
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