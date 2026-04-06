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
    /// 目前動態計算的連發間隔幀數（加入生理抖動）
    /// </summary>
    private int _currentRepeatInterval;

    /// <summary>
    /// 目前動態計算的右搖桿連發間隔幀數（加入生理抖動）
    /// </summary>
    private int _currentRSRepeatInterval;

    /// <summary>
    /// GameInput 實體
    /// </summary>
    private GameInput? _gameInput;

    /// <summary>
    /// 目前使用的 GameInput 設備
    /// </summary>
    private volatile GameInputDevice? _device;

    /// <summary>
    /// 標記是否需要重新整理設備清單
    /// </summary>
    private volatile bool _needsRefresh = true;

    /// <summary>
    /// 記錄最後一次要求重新整理的時間點 (Tick)
    /// </summary>
    private long _refreshRequestedTicks;

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
    /// 右搖桿重複計數器
    /// </summary>
    private int _rsRepeatCounter;

    /// <summary>
    /// 是否有前一次的 GamepadStateSnapshot
    /// </summary>
    private volatile bool _hasPreviousState;

    /// <summary>
    /// 是否已處置
    /// </summary>
    private volatile int _disposed;

    /// <summary>
    /// 控制器按鈕重複方向
    /// </summary>
    private GameInputGamepadButtons? _repeatDirection;

    /// <summary>
    /// 右搖桿重複方向（虛擬按鍵）
    /// </summary>
    private int _rsRepeatDirection; // -1: Left, 1: Right, 0: None

    /// <summary>
    /// 無新輸入狀態時的方向維持幀計數（防卡住）
    /// </summary>
    private int _directionalStaleFrameCounter;

    /// <summary>
    /// 有新輸入狀態時的方向幽靈重入幀計數（防抖動復發）
    /// </summary>
    private int _directionalGhostFrameCounter;

    /// <summary>
    /// 左搖桿 X 軸動態偏移估計（bias compensation）
    /// </summary>
    private float _leftStickBiasX;

    /// <summary>
    /// 左搖桿 Y 軸動態偏移估計（bias compensation）
    /// </summary>
    private float _leftStickBiasY;

    /// <summary>
    /// 右搖桿 X 軸動態偏移估計（bias compensation）
    /// </summary>
    private float _rightStickBiasX;

    /// <summary>
    /// 右搖桿 Y 軸動態偏移估計（bias compensation）
    /// </summary>
    private float _rightStickBiasY;

#if DEBUG
    /// <summary>
    /// D-Pad Right 連發診斷計數
    /// </summary>
    private int _dpadRightRepeatDiagnosticCounter;

    /// <summary>
    /// RS Right 連發診斷計數
    /// </summary>
    private int _rsRightRepeatDiagnosticCounter;

    /// <summary>
    /// 機制健康度診斷計數（節流輸出）
    /// </summary>
    private int _mechanismHealthLogCounter;

    /// <summary>
    /// 機制閒置幀計數（避免短暫空窗造成重新啟動即連續輸出）
    /// </summary>
    private int _mechanismIdleFrameCounter;

    /// <summary>
    /// 上一幀是否處於機制活躍狀態
    /// </summary>
    private bool _wasMechanismEngaged;

    /// <summary>
    /// 上一次機制健康度輸出的 D-Pad 方向
    /// </summary>
    private GameInputGamepadButtons? _lastHealthDpadDirection;

    /// <summary>
    /// 上一次機制健康度輸出的右搖桿方向
    /// </summary>
    private int _lastHealthRsDirection;

    /// <summary>
    /// 上一次機制健康度輸出的 stale 活躍狀態
    /// </summary>
    private bool _lastHealthStaleActive;

    /// <summary>
    /// 上一次機制健康度輸出的 ghost 活躍狀態
    /// </summary>
    private bool _lastHealthGhostActive;

    /// <summary>
    /// 連發風暴診斷輸出間隔（每 N 次輸出一次）
    /// </summary>
    private const int RepeatStormLogInterval = 30;

    /// <summary>
    /// 機制健康度診斷心跳輸出間隔（每 N 幀輸出一次，約 10 秒）
    /// </summary>
    private const int MechanismHealthLogIntervalFrames = 600;

    /// <summary>
    /// 機制視為真正閒置前的連續幀數（約 2 秒，提高避免 bias 補正波動觸發）
    /// </summary>
    private const int MechanismIdleResetFrames = 120;
#endif

    // ── 自適應 EMA 係數（Adaptive Exponential Moving Average）────────────────
    // 設計原則：每個軸保有獨立的「基礎值」與「最大值」。
    //   • 當估計誤差（rawValue − currentBias）落在 BiasAdaptiveErrorRange 以內時，
    //     學習率從 Base 線性插值至 Max，誤差越大學習越快（快速收斂）。
    //   • 當誤差接近 0 時，退回 Base（保守維持），避免把有效輸入誤學成硬體偏移。
    //   • 不同手把硬體偏移量不同；此機制讓程式自動適應，無需手動調整。

    /// <summary>
    /// 左搖桿 X 軸：偏移估計的最低保守學習率（誤差接近 0 時使用）。
    /// </summary>
    private const float LeftStickBiasXBaseSmoothing = 0.03f;

    /// <summary>
    /// 左搖桿 X 軸：偏移估計的最高學習率（誤差達到 BiasAdaptiveErrorRange 時使用）。
    /// Log 顯示 biasLx 在 D-Pad 操作期間漂移幅度大，Max 值不宜過高以防誤學方向輸入。
    /// </summary>
    private const float LeftStickBiasXMaxSmoothing = 0.15f;

    /// <summary>
    /// 左搖桿 Y 軸：偏移估計的最低保守學習率。
    /// </summary>
    private const float LeftStickBiasYBaseSmoothing = 0.03f;

    /// <summary>
    /// 左搖桿 Y 軸：偏移估計的最高學習率。
    /// Log 顯示 biasLy 恆在 ±0.009 以內，目標穩定，可略低於 X 軸 Max。
    /// </summary>
    private const float LeftStickBiasYMaxSmoothing = 0.12f;

    /// <summary>
    /// 右搖桿 X 軸：偏移估計的最低保守學習率。
    /// </summary>
    private const float RightStickBiasBaseSmoothing = 0.05f;

    /// <summary>
    /// 右搖桿 X 軸：偏移估計的最高學習率。
    /// 右搖桿無 D-Pad 閘門，低 Max 可防止快速劃過中立區時累積偏移；
    /// Warm-up 50 次後收斂率 ≈ 97.2%，起始收斂不受影響。
    /// </summary>
    private const float RightStickBiasMaxSmoothing = 0.07f;

    /// <summary>
    /// 右搖桿 Y 軸：偏移估計的最低保守學習率。
    /// </summary>
    private const float RightStickBiasYBaseSmoothing = 0.05f;

    /// <summary>
    /// 右搖桿 Y 軸：偏移估計的最高學習率。與 RX 相同理由，低 Max 防止壓力測試時逘速掟移。
    /// </summary>
    private const float RightStickBiasYMaxSmoothing = 0.07f;

    /// <summary>
    /// 觸發全速學習的誤差閾值（bias 誤差達此值時使用 Max 係數）。
    /// </summary>
    private const float BiasAdaptiveErrorRange = 0.05f;

    /// <summary>
    /// 僅在接近中立時更新偏移，避免把有效操作學成偏移
    /// </summary>
    private const float LeftStickBiasLearningThreshold = 0.28f;

    /// <summary>
    /// 輪詢間隔（毫秒），約 60 FPS
    /// </summary>
    private const double PollingIntervalMs = 16.6;

    /// <summary>
    /// 快取的設備名稱
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
    public bool IsConnected => _device != null;

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
    /// 右搖桿左推按下事件
    /// </summary>
    public event Action? RSLeftPressed;

    /// <summary>
    /// 右搖桿右推按下事件
    /// </summary>
    public event Action? RSRightPressed;

    /// <summary>
    /// 右搖桿左推重複事件
    /// </summary>
    public event Action? RSLeftRepeat;

    /// <summary>
    /// 右搖桿右推重複事件
    /// </summary>
    public event Action? RSRightRepeat;

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
        GameInputGamepadController controller = new(context, repeatSettings);

        // 透過 TCS 等待背景輪詢執行緒完成 GameInput 的初始化。
        // 這樣既能保證 COM 物件在輪詢執行緒上建立（避免 InvalidCastException 與執行緒綁定問題），
        // 又能將初始化結果（成功或例外）回傳給呼叫者，達成探測與重用實例的雙重目的，提升 50% 啟動速度。
        await controller.InitializeAndStartPollingAsync().ConfigureAwait(false);

        return controller;
    }

    /// <summary>
    /// 初始化並啟動背景輪詢
    /// </summary>
    private Task InitializeAndStartPollingAsync()
    {
        StopPolling();

        // 先以區域變數持有新的 CancellationTokenSource，再寫入欄位，
        // 避免 StopPolling()（Interlocked.Exchange）在欄位寫入與 Token 讀取之間介入，
        // 導致取得已釋放 Token 的 TOCTOU 競態。
        CancellationTokenSource cts = new();

        _ctsPolling = cts;

        CancellationToken token = cts.Token;

        TaskCompletionSource tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

        _taskPolling = Task.Run(() => PollingLoopAsync(token, tcs), token);

        return tcs.Task;
    }

    /// <summary>
    /// 啟動背景輪詢（用於 Resume）
    /// </summary>
    private void StartPolling()
    {
        // 確保先停止舊的。
        StopPolling();

        // 先以區域變數持有新的 CancellationTokenSource，再寫入欄位，
        // 避免 StopPolling()（Interlocked.Exchange）在欄位寫入與 Token 讀取之間介入，
        // 導致取得已釋放 Token 的 TOCTOU 競態。
        CancellationTokenSource cts = new();

        _ctsPolling = cts;

        CancellationToken token = cts.Token;

        _taskPolling = Task.Run(() => PollingLoopAsync(token, null), token);
    }

    /// <summary>
    /// 停止輪詢
    /// </summary>
    private void StopPolling()
    {
        Interlocked.Exchange(ref _ctsPolling, null)?.CancelAndDispose();
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

            _rsRepeatCounter = 0;
            _rsRepeatDirection = 0;

            ConnectionChanged?.Invoke(false);
        }

        // 解除 Callback 註冊。
        Interlocked.Exchange(ref _readingCallbackReg, null)?.Dispose();

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
        Interlocked.Exchange(ref _readingCallbackReg, null)?.Dispose();

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
                                // 防護機制：避免在背景暫停輪詢時，佇列因玩家在其他遊戲中的操作而無限增長。
                                if (_readingQueue.Count > 100)
                                {
                                    _readingQueue.TryDequeue(out _);
                                }

                                _readingQueue.Enqueue(state);
                            }
                        }
                        catch (Exception cbEx)
                        {
                            Debug.WriteLine($"GameInput 讀取回呼內部發生錯誤：{cbEx.Message}");
                        }
                    });
            }
            catch (Exception ex)
            {
                LoggerService.LogException(ex, "GameInput 註冊 ReadingCallback 失敗");

                Debug.WriteLine($"GameInput 註冊 ReadingCallback 失敗：{ex.Message}");
            }
        }
    }

    /// <summary>
    /// 背景輪詢迴圈
    /// </summary>
    /// <param name="cancellationToken">CancellationToken</param>
    /// <param name="initTcs">初始化結果回傳（僅在 CreateAsync 時傳入）</param>
    /// <returns>Task</returns>
    private async Task PollingLoopAsync(CancellationToken cancellationToken, TaskCompletionSource? initTcs = null)
    {
        // 在背景執行緒（MTA）建立正式使用的 GameInput 實體。
        // 這樣 COM 物件的單元模型就會與輪詢執行緒完全一致，徹底解決 InvalidCastException 與跨執行緒存取失效的問題。
        try
        {
            if (_gameInput == null)
            {
                _gameInput = GameInput.Create();
                _gameInput.SetFocusPolicy(GameInputFocusPolicy.Default);

                // 初始化時先直接列舉一次，再註冊非阻塞裝置回呼。
                // 這樣可避免為了拿到既有裝置名單而付出兩次同步枚舉成本。
                if (_needsRefresh &&
                    !cancellationToken.IsCancellationRequested &&
                    _disposed == 0)
                {
                    TryFindDevice();
                }

                // 註冊裝置變更事件。
                // 使用 None 避免在註冊當下再次為現有裝置執行同步枚舉，
                // 讓啟動成本只落在一次直接列舉上。
                _deviceCallbackReg = _gameInput.RegisterDeviceCallback(
                    null,
                    GameInputKind.Gamepad,
                    GameInputDeviceStatus.Connected,
                    GameInputEnumerationKind.None,
                    (_, _, _, _) =>
                    {
                        _needsRefresh = true;

                        // 記錄觸發當下的時間。
                        Interlocked.Exchange(ref _refreshRequestedTicks, Stopwatch.GetTimestamp());
                    });
            }

            // 成功完成初始化，通知 CreateAsync 解除等待
            initTcs?.TrySetResult();
        }
        catch (Exception ex)
        {
            LoggerService.LogException(ex, "GameInput 在背景執行緒初始化失敗");

            Debug.WriteLine($"GameInput 在背景執行緒初始化失敗：{ex.Message}");

            // 若系統不支援，通知 CreateAsync 丟出例外以觸發退避邏輯
            initTcs?.TrySetException(ex);

            return;
        }

        try
        {
            using PeriodicTimer periodicTimer = new(TimeSpan.FromMilliseconds(PollingIntervalMs));

            // 手動執行首發 Poll，讀取初始按鍵狀態。
            // 裝置連線已由初始化快速掃描完成；此次呼叫的目的是在計時器首幀（16ms）
            // 延遲發生前，先讀取一次完整的按鍵／搖桿初始狀態，避免首幀狀態為空白。
            if (!cancellationToken.IsCancellationRequested &&
                _disposed == 0)
            {
                try
                {
                    Poll();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"GameInput 首幀 Poll 發生錯誤：{ex.Message}");
                }
            }

            while (await periodicTimer.WaitForNextTickAsync(cancellationToken))
            {
                if (_disposed != 0 ||
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
                    // 僅記錄 Debug 資訊，避免輪詢期間頻繁寫入日誌。
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
            LoggerService.LogException(ex, "GameInput 輪詢迴圈發生致命錯誤");

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
            // 若目前尚未連線任何裝置，直接重掃，不套用 500ms 防抖，
            // 以縮短「未連線 → 新插入」的首段偵測延遲。
            if (_device == null)
            {
                _reconnectCounter = 0;

                TryFindDevice();
            }

            // 尚未連線時已做過即時重掃，本幀不再進行第二次防抖重掃，避免重複列舉。
            if (_device != null)
            {
                long elapsedTicks = Stopwatch.GetTimestamp() - Interlocked.Read(ref _refreshRequestedTicks),
                    elapsedMs = elapsedTicks / (Stopwatch.Frequency / 1000);

                if (elapsedMs >= AppSettings.GamepadRefreshCooldownMs)
                {
                    _reconnectCounter = 0;

                    TryFindDevice();
                }
            }
        }
        else if (_device == null)
        {
            _reconnectCounter++;

            if (_reconnectCounter >= AppSettings.GamepadReconnectThresholdFrames)
            {
                _reconnectCounter = 0;

                TryFindDevice();
            }
        }

        GameInputDevice? dev = _device;

        if (dev == null)
        {
            ResetMechanismHealthLogState();

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
            _directionalStaleFrameCounter = 0;

            // 使用最後一個快照作為本 Tick 的最終狀態。
            _previousState = latestSnapshot;
            _hasPreviousState = true;

            // 對齊 Timer Tick 執行連發邏輯。
            HandleRepeat(_previousProcessedButtons, config);

            EvaluateDirectionalGhostState(latestSnapshot, config);

            EmitMechanismHealthLog(latestSnapshot, config);
        }
        else if (_hasPreviousState &&
            _previousState != null)
        {
            // 如果這 16ms 內沒有任何新狀態，判定閒置或維持按住。
            if (IsStateIdle(_previousState))
            {
                _reconnectCounter++;

                if (_reconnectCounter >= AppSettings.GamepadReconnectThresholdFrames)
                {
                    _reconnectCounter = 0;

                    if (ScanForActiveDevice())
                    {
                        return;
                    }
                }

                _directionalGhostFrameCounter = 0;
            }
            else
            {
                _reconnectCounter = 0;

                if (HasActiveDirectionalRepeat())
                {
                    if (ShouldForceReleaseDirectionalRepeat(_previousState, config))
                    {
                        _directionalStaleFrameCounter++;

                        if (_directionalStaleFrameCounter >= AppSettings.GamepadDirectionalStuckGuardFrames)
                        {
                            LoggerService.LogInfo($"Gamepad.AntiStuckTriggered source=GameInput staleFrames={_directionalStaleFrameCounter} dpadDir={_repeatDirection?.ToString() ?? "None"} rsDir={_rsRepeatDirection}");

                            ResetDirectionalRepeatState();
                        }
                    }
                    else
                    {
                        _directionalStaleFrameCounter = 0;
                    }
                }
                else
                {
                    _directionalStaleFrameCounter = 0;
                }

                // 玩家按住按鍵不放且硬體無新事件時，繼續執行連發計時。
                HandleRepeat(_previousProcessedButtons, config);

                EvaluateDirectionalGhostState(_previousState, config);

                EmitMechanismHealthLog(_previousState, config);
            }
        }
        else
        {
            ResetMechanismHealthLogState();
        }
    }

    /// <summary>
    /// 評估方向幽靈重入狀態：處理「狀態有變化但方向持續誤判」情境。
    /// </summary>
    private void EvaluateDirectionalGhostState(
        GamepadStateSnapshot state,
        AppSettings.GamepadConfigSnapshot config)
    {
        if (!HasActiveDirectionalRepeat())
        {
            _directionalGhostFrameCounter = 0;

            return;
        }

        if (!ShouldForceReleaseDirectionalRepeat(state, config))
        {
            _directionalGhostFrameCounter = 0;

            return;
        }

        _directionalGhostFrameCounter++;

        if (_directionalGhostFrameCounter < AppSettings.GamepadDirectionalStuckGuardFrames)
        {
            return;
        }

        LoggerService.LogInfo($"Gamepad.AntiStuckTriggered source=GameInput reason=ghost_reentry ghostFrames={_directionalGhostFrameCounter} dpadDir={_repeatDirection?.ToString() ?? "None"} rsDir={_rsRepeatDirection} lx={state.LeftThumbstickX:F4} ly={state.LeftThumbstickY:F4} rx={state.RightThumbstickX:F4} biasLx={_leftStickBiasX:F4} biasLy={_leftStickBiasY:F4} biasRx={_rightStickBiasX:F4} biasRy={_rightStickBiasY:F4}");

        ResetDirectionalRepeatState();
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

        // 將搖桿原始值 clamp 至物理合法範圍 [-1.0, 1.0]。
        // Log 顯示，在裝置受到物理撞擊時，GameInput 可能回報超出正常範圍的值
        // （如 ly = -1.0514, -1.0630），這些值會干擾 bias 學習並造成假方向觸發。
        // clamp 不影響正常操作（GameInput 已校正值接近但不超過 ±1.0）。
        float clampedLX = Math.Clamp(currentState.LeftThumbstickX, -1.0f, 1.0f),
            clampedLY = Math.Clamp(currentState.LeftThumbstickY, -1.0f, 1.0f),
            clampedRX = Math.Clamp(currentState.RightThumbstickX, -1.0f, 1.0f),
            clampedRY = Math.Clamp(currentState.RightThumbstickY, -1.0f, 1.0f);

        // D-Pad 按下時，左搖桿因機械耦合會產生 ±0.15~0.25 的偏移，
        // 低於 LeftStickBiasLearningThreshold（0.28），會被 EMA 誤學成中立位置。
        // 在 D-Pad 作動期間暫停左搖桿 bias 學習以防止污染；右搖桿不受影響。
        bool isDPadActive = (currentButtons & (
            GameInputGamepadButtons.DPadLeft  |
            GameInputGamepadButtons.DPadRight |
            GameInputGamepadButtons.DPadUp    |
            GameInputGamepadButtons.DPadDown)) != 0;

        UpdateStickBias(
            clampedLX,
            clampedLY,
            clampedRX,
            clampedRY,
            isDPadActive);

        short correctedThumbLX = GetCorrectedLeftThumbShort(clampedLX, _leftStickBiasX),
            correctedThumbLY = GetCorrectedLeftThumbShort(clampedLY, _leftStickBiasY);
        float correctedRightThumbX = clampedRX - _rightStickBiasX;

        ApplyStickToButtons(ref currentButtons, correctedThumbLX, correctedThumbLY, config);

        if (!_context.IsInputActive)
        {
            _repeatCounter = 0;
            _repeatDirection = null;
            _rsRepeatCounter = 0;
            _rsRepeatDirection = 0;
            _previousProcessedButtons = currentButtons;

            return;
        }

        // 偵測按下事件。
        DetectRisingEdge(currentButtons, currentState, correctedRightThumbX, config);

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
            state.Buttons == 0 &&
            _previousState.Buttons == 0 &&
            GamepadSignalEvaluator.IsIdle(
                hasButtons: false,
                leftTrigger: state.LeftTrigger,
                rightTrigger: state.RightTrigger,
                leftThumbX: state.LeftThumbstickX,
                leftThumbY: state.LeftThumbstickY,
                rightThumbX: state.RightThumbstickX,
                rightThumbY: state.RightThumbstickY,
                threshold: AppSettings.GameInputIdleThreshold);
    }

    /// <summary>
    /// 是否存在方向連發狀態（D-Pad 或 RS）
    /// </summary>
    private bool HasActiveDirectionalRepeat()
    {
        return _repeatDirection.HasValue ||
            _rsRepeatDirection != 0;
    }

    /// <summary>
    /// 判斷目前是否符合「疑似已放開但事件未更新」條件
    /// </summary>
    /// <param name="state">目前的遊戲控制器狀態快照</param>
    /// <param name="config">遊戲控制器設定快照</param>
    /// <returns>若符合條件，則回傳 true；否則回傳 false。</returns>
    private bool ShouldForceReleaseDirectionalRepeat(
        GamepadStateSnapshot state,
        AppSettings.GamepadConfigSnapshot config)
    {
        const GameInputGamepadButtons directionalFlags =
            GameInputGamepadButtons.DPadLeft |
            GameInputGamepadButtons.DPadRight |
            GameInputGamepadButtons.DPadUp |
            GameInputGamepadButtons.DPadDown;

        if ((state.Buttons & directionalFlags) != 0)
        {
            return false;
        }

        float enterThreshold = config.ThumbDeadzoneEnter / 32768f,
            exitThreshold = config.ThumbDeadzoneExit / 32768f,
            // 使用介於 Exit 與 Enter 的中間區間作為「近中立」保護門檻，
            // 避免實際長按方向時被誤判為卡住。
            softCenterThreshold = MathF.Min(
                enterThreshold,
                exitThreshold + MathF.Max(0.02f, (enterThreshold - exitThreshold) * 0.5f)),
            correctedLeftX = state.LeftThumbstickX - _leftStickBiasX,
            correctedLeftY = state.LeftThumbstickY - _leftStickBiasY,
            correctedRightX = state.RightThumbstickX - _rightStickBiasX;

        bool leftStickNearCenter =
                MathF.Abs(correctedLeftX) <= softCenterThreshold &&
                MathF.Abs(correctedLeftY) <= softCenterThreshold,
            rightStickNearCenter =
                MathF.Abs(correctedRightX) <= softCenterThreshold;

        return leftStickNearCenter &&
            rightStickNearCenter;
    }

    /// <summary>
    /// 重置方向連發狀態（含 D-Pad 與 RS）
    /// </summary>
    private void ResetDirectionalRepeatState()
    {
        _repeatDirection = null;
        _repeatCounter = 0;
        _currentRepeatInterval = 0;

        _rsRepeatDirection = 0;
        _rsRepeatCounter = 0;
        _currentRSRepeatInterval = 0;

        _directionalStaleFrameCounter = 0;
        _directionalGhostFrameCounter = 0;

        _previousProcessedButtons &= ~(
            GameInputGamepadButtons.DPadLeft |
            GameInputGamepadButtons.DPadRight |
            GameInputGamepadButtons.DPadUp |
            GameInputGamepadButtons.DPadDown);
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

        bool needsDisconnect = false,
            needsConnectNotify = false;

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
                catch (Exception ex)
                {
                    LoggerService.LogException(ex, "GameInput 裝置狀態更新發生錯誤");

                    Debug.WriteLine($"[GameInput] 裝置狀態更新發生錯誤，嘗試清理：{ex.Message}");

                    // 確保即使發生例外，COM 物件的釋放也不會被延遲。
                    if (oldDev == _device)
                    {
                        _device = null;
                    }

                    try
                    {
                        oldDev.Dispose();
                    }
                    catch (Exception disposeEx)
                    {
                        Debug.WriteLine($"[GameInput] 釋放失效裝置時發生錯誤（已忽略）：{disposeEx.Message}");
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
                catch (Exception ex)
                {
                    LoggerService.LogException(ex, "GameInput 加入新裝置時發生錯誤");

                    Debug.WriteLine($"[GameInput] 加入新裝置時發生錯誤，已釋放：{ex.Message}");
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

                    // 連線事件需在鎖外觸發，避免 UI 執行緒回呼反向取鎖造成風險。
                    needsConnectNotify = true;
                }
            }
            else
            {
                _device = null;

                // 標記需要在鎖外呼叫 HandleDisconnect，避免持鎖期間觸發跨執行緒事件（ConnectionChanged）。
                needsDisconnect = true;
            }
        }

        // 在鎖外執行，防止持鎖時觸發 ConnectionChanged 事件導致死鎖風險。
        if (needsDisconnect)
        {
            HandleDisconnect();
        }

        if (needsConnectNotify)
        {
            ConnectionChanged?.Invoke(true);
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

        bool switched = false;

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
                        if (GamepadSignalEvaluator.IsActive(
                            hasButtons: state.Buttons != 0,
                            leftTrigger: state.LeftTrigger,
                            rightTrigger: state.RightTrigger,
                            leftThumbX: state.LeftThumbstickX,
                            leftThumbY: state.LeftThumbstickY,
                            rightThumbX: state.RightThumbstickX,
                            rightThumbY: state.RightThumbstickY,
                            threshold: AppSettings.GameInputActiveThreshold))
                        {
                            // 重置原本的按鍵狀態，避免按住的按鍵殘留給新控制器。
                            ResetHoldStates();

                            // 切換裝置參考。
                            _device = otherDev;

                            UpdateDeviceInfo();
                            InitializeDeviceState();
                            SetupReadingCallback();

                            switched = true;

                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[GameInput] 切換裝置時讀取狀態失敗（已忽略）：{ex.Message}");
                }
            }
        }

        if (switched)
        {
            ConnectionChanged?.Invoke(true);
        }

        return switched;
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

            // 更新震動支援狀態。
            _supportsRumble = info.SupportedRumbleMotors != GameInputRumbleMotors.None;

            // 更新裝置名稱。
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
        catch (Exception ex)
        {
            Debug.WriteLine($"[GameInput] 取得裝置資訊失敗，使用預設值：{ex.Message}");
            _cachedDeviceName = "Unknown Gamepad";
            _supportsRumble = false;
        }
    }

    /// <summary>
    /// 初始化裝置的初始狀態
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

                GameInputGamepadButtons currentButtons = state.Buttons;

                // Bias warm-up：以初始讀值連續執行 50 次 EMA，
                // 使偏移估計在第一幀即接近真實值（收斂率 ≈ 99%），
                // 避免 App 啟動初期因 bias ≈ 0 造成方向誤判。
                // Log 實測：biasRx 需約 100+ 幀才能自然收斂至 ±0.02，
                // 此熱身迴圈可將誤差縮短至首幀之內。
                for (int i = 0; i < 50; i++)
                {
                    // 暖機時不存在 D-Pad 操作情境，閘門固定傳 false。
                    UpdateStickBias(state.LeftThumbstickX, state.LeftThumbstickY, state.RightThumbstickX, state.RightThumbstickY, isDPadActive: false);
                }

                short correctedThumbLX = GetCorrectedLeftThumbShort(state.LeftThumbstickX, _leftStickBiasX),
                    correctedThumbLY = GetCorrectedLeftThumbShort(state.LeftThumbstickY, _leftStickBiasY);

                // 取得快照以確保初始化時死區校驗一致。
                AppSettings.GamepadConfigSnapshot config = AppSettings.Current.GamepadSettings;

                ApplyStickToButtons(ref currentButtons, correctedThumbLX, correctedThumbLY, config);

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
        catch (Exception ex)
        {
            Debug.WriteLine($"[GameInput] 初始化裝置狀態失敗，使用空狀態：{ex.Message}");
            _previousState = null;
            _previousProcessedButtons = 0;
            _hasPreviousState = false;
        }

    }

    /// <summary>
    /// 以接近中立區段估計搖桿中心偏移，降低固定偏壓造成的方向誤判
    /// </summary>
    /// <param name="rawLeftThumbX">左搖桿 X 軸原始值</param>
    /// <param name="rawLeftThumbY">左搖桿 Y 軸原始值</param>
    /// <param name="rawRightThumbX">右搖桿 X 軸原始值</param>
    /// <param name="rawRightThumbY">右搖桿 Y 軸原始值</param>
    /// <param name="isDPadActive">是否有任意 D-Pad 方向目前被按下；為 true 時暫停左搖桿 bias 學習以避免機械耦合污染。</param>
    private void UpdateStickBias(
        float rawLeftThumbX,
        float rawLeftThumbY,
        float rawRightThumbX,
        float rawRightThumbY,
        bool isDPadActive)
    {
        if (!isDPadActive && MathF.Abs(rawLeftThumbX) <= LeftStickBiasLearningThreshold)
        {
            float errorX = rawLeftThumbX - _leftStickBiasX;
            _leftStickBiasX += errorX * ComputeAdaptiveBiasSmoothing(
                errorX, LeftStickBiasXBaseSmoothing, LeftStickBiasXMaxSmoothing);
        }

        if (!isDPadActive && MathF.Abs(rawLeftThumbY) <= LeftStickBiasLearningThreshold)
        {
            float errorY = rawLeftThumbY - _leftStickBiasY;
            _leftStickBiasY += errorY * ComputeAdaptiveBiasSmoothing(
                errorY, LeftStickBiasYBaseSmoothing, LeftStickBiasYMaxSmoothing);
        }

        if (MathF.Abs(rawRightThumbX) <= LeftStickBiasLearningThreshold)
        {
            float errorRX = rawRightThumbX - _rightStickBiasX;
            _rightStickBiasX += errorRX * ComputeAdaptiveBiasSmoothing(
                errorRX, RightStickBiasBaseSmoothing, RightStickBiasMaxSmoothing);
        }

        if (MathF.Abs(rawRightThumbY) <= LeftStickBiasLearningThreshold)
        {
            float errorRY = rawRightThumbY - _rightStickBiasY;
            _rightStickBiasY += errorRY * ComputeAdaptiveBiasSmoothing(
                errorRY, RightStickBiasYBaseSmoothing, RightStickBiasYMaxSmoothing);
        }
    }

    /// <summary>
    /// 計算自適應 EMA 學習率：誤差越大越接近 <paramref name="maxSmoothing"/>，
    /// 誤差越小越接近 <paramref name="baseSmoothing"/>。
    /// </summary>
    /// <param name="error">目前估計誤差（rawValue − currentBias）。</param>
    /// <param name="baseSmoothing">誤差接近 0 時的保守係數。</param>
    /// <param name="maxSmoothing">誤差達到 BiasAdaptiveErrorRange 時的最大係數。</param>
    /// <returns>本次更新應使用的 EMA 係數。</returns>
    private static float ComputeAdaptiveBiasSmoothing(
        float error,
        float baseSmoothing,
        float maxSmoothing)
    {
        float t = Math.Clamp(MathF.Abs(error) / BiasAdaptiveErrorRange, 0f, 1f);
        return baseSmoothing + (maxSmoothing - baseSmoothing) * t;
    }

    /// <summary>
    /// 取得偏移校正後的左搖桿軸值（short）。
    /// </summary>
    private static short GetCorrectedLeftThumbShort(float rawValue, float bias)
    {
        return (short)Math.Clamp((rawValue - bias) * 32767f, short.MinValue, short.MaxValue);
    }

    /// <summary>
    /// 低頻輸出搖桿機制健康度，協助確認 bias／deadzone／hysteresis／repeat 正在生效。
    /// </summary>
    /// <param name="state">遊戲控制器狀態快照</param>
    /// <param name="config">遊戲控制器設定快照</param>
    private void EmitMechanismHealthLog(
        GamepadStateSnapshot state,
        AppSettings.GamepadConfigSnapshot config)
    {
#if DEBUG
        float correctedLeftX = state.LeftThumbstickX - _leftStickBiasX,
            correctedLeftY = state.LeftThumbstickY - _leftStickBiasY,
            correctedRightX = state.RightThumbstickX - _rightStickBiasX,
            exitThreshold = config.ThumbDeadzoneExit / 32768f;

        bool hasSignificantInput =
                MathF.Abs(correctedLeftX) > exitThreshold ||
                MathF.Abs(correctedLeftY) > exitThreshold ||
                MathF.Abs(correctedRightX) > exitThreshold,
            isEngaged = hasSignificantInput ||
                _repeatDirection.HasValue ||
                _rsRepeatDirection != 0;

        if (!isEngaged)
        {
            _mechanismIdleFrameCounter++;

            if (_mechanismIdleFrameCounter >= MechanismIdleResetFrames)
            {
                ResetMechanismHealthLogState();
            }

            return;
        }

        _mechanismIdleFrameCounter = 0;

        bool staleActive = _directionalStaleFrameCounter > 0,
            ghostActive = _directionalGhostFrameCounter > 0,
            stateChanged = _repeatDirection != _lastHealthDpadDirection ||
                _rsRepeatDirection != _lastHealthRsDirection ||
                staleActive != _lastHealthStaleActive ||
                ghostActive != _lastHealthGhostActive;

        _mechanismHealthLogCounter++;

        if (!_wasMechanismEngaged ||
            stateChanged ||
            _mechanismHealthLogCounter >= MechanismHealthLogIntervalFrames)
        {
            LoggerService.LogInfo(
                $"Gamepad.MechanismHealth source=GameInput stage=bias_deadzone_hysteresis_repeat dpadDir={_repeatDirection?.ToString() ?? "None"} rsDir={_rsRepeatDirection} lx={correctedLeftX:F4} ly={correctedLeftY:F4} rx={correctedRightX:F4} biasLx={_leftStickBiasX:F4} biasLy={_leftStickBiasY:F4} biasRx={_rightStickBiasX:F4} biasRy={_rightStickBiasY:F4} enter={config.ThumbDeadzoneEnter} exit={config.ThumbDeadzoneExit} stale={_directionalStaleFrameCounter} ghost={_directionalGhostFrameCounter}");

            _mechanismHealthLogCounter = 0;
            _lastHealthDpadDirection = _repeatDirection;
            _lastHealthRsDirection = _rsRepeatDirection;
            _lastHealthStaleActive = staleActive;
            _lastHealthGhostActive = ghostActive;
        }

        _wasMechanismEngaged = true;
#endif
    }

    /// <summary>
    /// 重置機制健康度診斷狀態。
    /// </summary>
    private void ResetMechanismHealthLogState()
    {
#if DEBUG
        _mechanismHealthLogCounter = 0;
        _mechanismIdleFrameCounter = 0;
        _wasMechanismEngaged = false;
        _lastHealthDpadDirection = null;
        _lastHealthRsDirection = 0;
        _lastHealthStaleActive = false;
        _lastHealthGhostActive = false;
#endif
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

        int horizontalDirection = GamepadDeadzoneHysteresis.ResolveDirection(
            thumbLX,
            wasLeft,
            wasRight,
            config.ThumbDeadzoneEnter,
            config.ThumbDeadzoneExit);

        if (horizontalDirection < 0)
        {
            currentButtons |= GameInputGamepadButtons.DPadLeft;
        }
        else if (horizontalDirection > 0)
        {
            currentButtons |= GameInputGamepadButtons.DPadRight;
        }

        int verticalDirection = GamepadDeadzoneHysteresis.ResolveDirection(
            thumbLY,
            wasDown,
            wasUp,
            config.ThumbDeadzoneEnter,
            config.ThumbDeadzoneExit);

        if (verticalDirection < 0)
        {
            currentButtons |= GameInputGamepadButtons.DPadDown;
        }
        else if (verticalDirection > 0)
        {
            currentButtons |= GameInputGamepadButtons.DPadUp;
        }
    }

    /// <summary>
    /// 偵測 Rising Edge
    /// </summary>
    /// <param name="currentButtons">目前的按鈕狀態</param>
    /// <param name="currentState">目前的遊戲控制器狀態快照</param>
    /// <param name="correctedRightThumbX">修正後的右搖桿 X 軸值</param>
    /// <param name="config">遊戲控制器設定快照</param>
    private void DetectRisingEdge(
        GameInputGamepadButtons currentButtons,
        GamepadStateSnapshot currentState,
        float correctedRightThumbX,
        AppSettings.GamepadConfigSnapshot config)
    {
        GameInputGamepadButtons prevButtons = _hasPreviousState ?
            _previousProcessedButtons :
            0;

        static bool HasFlag(GameInputGamepadButtons source, GameInputGamepadButtons flag)
        {
            return source.HasFlag(flag);
        }

        static void DetectRising(
            GameInputGamepadButtons current,
            GameInputGamepadButtons previous,
            GameInputGamepadButtons flag,
            Action? action)
        {
            if (HasFlag(current, flag) &&
                !HasFlag(previous, flag))
            {
                action?.Invoke();
            }
        }

        static void DetectReleased(
            GameInputGamepadButtons current,
            GameInputGamepadButtons previous,
            GameInputGamepadButtons flag,
            Action? action)
        {
            if (!HasFlag(current, flag) &&
                HasFlag(previous, flag))
            {
                action?.Invoke();
            }
        }

        // 更新按住狀態（使用 GameInput 原生旗標）。
        IsLeftShoulderHeld = currentButtons.HasFlag(GameInputGamepadButtons.LeftShoulder);
        IsRightShoulderHeld = currentButtons.HasFlag(GameInputGamepadButtons.RightShoulder);
        IsBackHeld = currentButtons.HasFlag(GameInputGamepadButtons.View);
        IsBHeld = currentButtons.HasFlag(GameInputGamepadButtons.B);
        IsLeftTriggerHeld = currentState.LeftTrigger > AppSettings.GameInputTriggerThreshold;
        IsRightTriggerHeld = currentState.RightTrigger > AppSettings.GameInputTriggerThreshold;

        // 偵測按下事件。
        DetectRising(currentButtons, prevButtons, GameInputGamepadButtons.DPadUp, UpPressed);
        DetectRising(currentButtons, prevButtons, GameInputGamepadButtons.DPadDown, DownPressed);
        DetectRising(currentButtons, prevButtons, GameInputGamepadButtons.DPadLeft, LeftPressed);
        DetectRising(currentButtons, prevButtons, GameInputGamepadButtons.DPadRight, RightPressed);
        DetectRising(currentButtons, prevButtons, GameInputGamepadButtons.Menu, StartPressed);
        DetectRising(currentButtons, prevButtons, GameInputGamepadButtons.View, BackPressed);
        DetectReleased(currentButtons, prevButtons, GameInputGamepadButtons.View, BackReleased);
        DetectRising(currentButtons, prevButtons, GameInputGamepadButtons.A, APressed);
        DetectRising(currentButtons, prevButtons, GameInputGamepadButtons.B, BPressed);
        DetectRising(currentButtons, prevButtons, GameInputGamepadButtons.X, XPressed);
        DetectRising(currentButtons, prevButtons, GameInputGamepadButtons.Y, YPressed);

        // 處理右搖桿（RS）虛擬按鍵偵測（使用 Hysteresis 邏輯以對抗漂移）。
        float enterThreshold = config.ThumbDeadzoneEnter / 32768f,
            exitThreshold = config.ThumbDeadzoneExit / 32768f;

        int curRsDir = GamepadDeadzoneHysteresis.ResolveDirection(
            correctedRightThumbX,
                _rsRepeatDirection == -1,
                _rsRepeatDirection == 1,
                enterThreshold,
                exitThreshold);

        // 偵測正緣觸發。
        if (curRsDir == -1 &&
            _rsRepeatDirection != -1)
        {
            RSLeftPressed?.Invoke();
        }
        else if (curRsDir == 1 &&
            _rsRepeatDirection != 1)
        {
            RSRightPressed?.Invoke();
        }

        // 偵測方向變化並更新狀態。
        if (curRsDir != _rsRepeatDirection)
        {
            _rsRepeatCounter = 0;
            _rsRepeatDirection = curRsDir;
        }

        bool prevLtDown = _hasPreviousState &&
            _previousState!.LeftTrigger > AppSettings.GameInputTriggerThreshold;

        if (IsLeftTriggerHeld &&
            !prevLtDown)
        {
            LeftTriggerPressed?.Invoke();
        }

        bool prevRtDown = _hasPreviousState &&
            _previousState!.RightTrigger > AppSettings.GameInputTriggerThreshold;

        if (IsRightTriggerHeld &&
            !prevRtDown)
        {
            RightTriggerPressed?.Invoke();
        }
    }

    /// <summary>
    /// 處理重複
    /// </summary>
    /// <param name="buttons">GameInputGamepadButtons</param>
    /// <param name="config">AppSettings.GamepadConfigSnapshot</param>
    private void HandleRepeat(
        GameInputGamepadButtons buttons,
        AppSettings.GamepadConfigSnapshot config)
    {
#if DEBUG
        bool emittedDpadRightRepeat = false,
            emittedRsRightRepeat = false;
#endif

        GameInputGamepadButtons? currentDir =
            buttons.HasFlag(GameInputGamepadButtons.DPadLeft) ? GameInputGamepadButtons.DPadLeft :
            buttons.HasFlag(GameInputGamepadButtons.DPadRight) ? GameInputGamepadButtons.DPadRight :
            buttons.HasFlag(GameInputGamepadButtons.DPadUp) ? GameInputGamepadButtons.DPadUp :
            buttons.HasFlag(GameInputGamepadButtons.DPadDown) ? GameInputGamepadButtons.DPadDown :
            null;

        if (GamepadRepeatStateMachine.AdvanceDirectionRepeat(
                currentDir,
                ref _repeatDirection,
                ref _repeatCounter,
                ref _currentRepeatInterval,
                config.RepeatInitialDelayFrames,
                config.RepeatIntervalFrames))
        {
            if (currentDir == GameInputGamepadButtons.DPadLeft)
            {
                LeftRepeat?.Invoke();
            }
            else if (currentDir == GameInputGamepadButtons.DPadRight)
            {
                RightRepeat?.Invoke();

#if DEBUG
                emittedDpadRightRepeat = true;
#endif
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

        // 2. 處理右搖桿（RS）重複輸入。
        int rsDir = _rsRepeatDirection;

        if (GamepadRepeatStateMachine.AdvanceHeldRepeat(
                rsDir != 0,
                ref _rsRepeatCounter,
                ref _currentRSRepeatInterval,
                config.RepeatInitialDelayFrames,
                config.RepeatIntervalFrames))
        {
            if (rsDir == -1)
            {
                RSLeftRepeat?.Invoke();
            }
            else if (rsDir == 1)
            {
                RSRightRepeat?.Invoke();

#if DEBUG
                emittedRsRightRepeat = true;
#endif
            }
        }

#if DEBUG
        if (emittedDpadRightRepeat)
        {
            _dpadRightRepeatDiagnosticCounter++;

            if (_dpadRightRepeatDiagnosticCounter % RepeatStormLogInterval == 0)
            {
                LoggerService.LogInfo($"Gamepad.RightRepeatStorm source=GameInput kind=DPad count={_dpadRightRepeatDiagnosticCounter} dpadDir={_repeatDirection?.ToString() ?? "None"}");
            }
        }
        else if (currentDir != GameInputGamepadButtons.DPadRight)
        {
            _dpadRightRepeatDiagnosticCounter = 0;
        }

        if (emittedRsRightRepeat)
        {
            _rsRightRepeatDiagnosticCounter++;

            if (_rsRightRepeatDiagnosticCounter % RepeatStormLogInterval == 0)
            {
                LoggerService.LogInfo($"Gamepad.RightRepeatStorm source=GameInput kind=RS count={_rsRightRepeatDiagnosticCounter} rsDir={_rsRepeatDirection}");
            }
        }
        else if (rsDir != 1)
        {
            _rsRightRepeatDiagnosticCounter = 0;
        }
#endif
    }

    /// <summary>
    /// 讓控制器震動
    /// </summary>
    /// <param name="strength">強度</param>
    /// <param name="milliseconds">持續時間（毫秒），預設值為 60 毫秒</param>
    /// <param name="ct">取消權杖</param>
    /// <returns>Task</returns>
    public Task VibrateAsync(
        ushort strength,
        int milliseconds = 60,
        CancellationToken ct = default)
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
            StopVibration();

            return Task.CompletedTask;
        }

        return Task.Run(async () =>
        {
            // 再次檢查，避免在 ThreadPool 排隊時外部就已取消
            if (ct.IsCancellationRequested)
            {
                return;
            }

            long token;

            CancellationToken internalToken;

            CancellationTokenSource newInternalCts = new();

            lock (_vibrationLock)
            {
                // 原子化遞增 Token 並更換 CTS，確保配對絕對正確。
                token = Interlocked.Increment(ref _vibrationToken);

                Interlocked.Exchange(ref _vibrationCts, null)?.CancelAndDispose();

                _vibrationCts = newInternalCts;

                // 在鎖內取得 Token，避免鎖外其他執行緒 CancelAndDispose() 後
                // 再存取 newInternalCts.Token 屬性時拋出 ObjectDisposedException。
                internalToken = newInternalCts.Token;
            }

            // 直接連結外部 ct 與內部 CTS。
            using CancellationTokenSource finalCts = CancellationTokenSource
                .CreateLinkedTokenSource(ct, internalToken);

            CancellationToken ctsToken = finalCts.Token;

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
                if (_disposed != 0 ||
                    _device == null ||
                    _device != dev)
                {
                    return;
                }

                dev.SetRumbleState(rumble);

                await Task.Delay(milliseconds, ctsToken).ConfigureAwait(false);

                // 檢查是否已被處置、裝置是否已變更、或是否有更新的震動請求。
                if (_disposed != 0 ||
                    Interlocked.Read(ref _vibrationToken) != token ||
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
                // 如果是因為新任務進來或視窗關閉而取消，確保馬達停止。
                try
                {
                    // 如果 token 沒變，代表是「外部 ct 取消的」，不是被新震動覆蓋的，
                    // 這種情況必須我們自己負責把馬達關掉！
                    if (Interlocked.Read(ref _vibrationToken) == token)
                    {
                        dev.SetRumbleState(new GameInputRumbleParams());
                    }
                }
                catch (Exception innerEx)
                {
                    Debug.WriteLine($"[GameInput] 取消後強制停止馬達失敗（已忽略）：{innerEx.Message}");
                }
            }
        },
        ct);
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

        // 確保馬達即時歸零，並在鎖內原子取消現有 CTS（與 StopVibration / VibrateAsync 保持鎖邊界一致）。
        StopVibration();

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
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
        {
            return;
        }

        // 取消註冊。
        FeedbackService.UnregisterController(this);

        // 確保在資源釋放前馬達已確實接收到歸零指令。
        StopVibration();

        // 發出取消訊號。
        StopPolling();

        // 清理資源（同步環境下使用 SafeFireAndForget）。
        ClearAllEvents();
        DisposeResourcesAsync().SafeFireAndForget();

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// 停止輪詢（非同步）
    /// </summary>
    /// <returns>Task</returns>
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
        lock (_vibrationLock)
        {
            // 立即遞增 Token 並取消現有任務的延遲，確保進行中的 VibrateAsync 任務失效。
            Interlocked.Increment(ref _vibrationToken);

            Interlocked.Exchange(ref _vibrationCts, null)?.CancelAndDispose();
        }

        GameInputDevice? dev = _device;

        if (dev == null)
        {
            return;
        }

        // 判斷目前執行緒的單元模型（Apartment State）。
        // GameInput 的 COM 物件建立於 MTA 背景執行緒，若在 STA（如 UI 執行緒）直接存取會引發 InvalidCastException (E_NOINTERFACE)。
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.MTA)
        {
            Task.Run(() =>
            {
                try
                {
                    dev.SetRumbleState(new GameInputRumbleParams());
                }
                catch (Exception bgEx)
                {
                    Debug.WriteLine($"[GameInput] 背景停止馬達失敗（已忽略）：{bgEx.Message}");
                }
            });
        }
        else
        {
            try
            {
                dev.SetRumbleState(new GameInputRumbleParams());
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GameInput 停止震動失敗：{ex.Message}");
            }
        }
    }

    /// <summary>
    /// 非同步處置資源
    /// </summary>
    /// <returns>Task</returns>
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
                    catch (Exception taskEx)
                    {
                        Debug.WriteLine($"[GameInput] 等待輪詢 Task 結束時發生錯誤（已忽略）：{taskEx.Message}");
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

                gameInput?.Dispose();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GameInput] 釋放資源時發生未預期錯誤：{ex.Message}");
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
        RSLeftPressed = null;
        RSRightPressed = null;
        RSLeftRepeat = null;
        RSRightRepeat = null;
        LeftTriggerPressed = null;
        RightTriggerPressed = null;
        ConnectionChanged = null;
    }
}