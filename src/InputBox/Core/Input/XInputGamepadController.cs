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
    /// 目前動態計算的連發間隔幀數（加入生理抖動）
    /// </summary>
    private int _currentRepeatInterval;

    /// <summary>
    /// 目前動態計算的右搖桿連發間隔幀數（加入生理抖動）
    /// </summary>
    private int _currentRSRepeatInterval;

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
    /// LT 連發計數器
    /// </summary>
    private int _ltRepeatCounter;

    /// <summary>
    /// LT 目前動態計算的連發間隔幀數
    /// </summary>
    private int _currentLTRepeatInterval;

    /// <summary>
    /// RT 連發計數器
    /// </summary>
    private int _rtRepeatCounter;

    /// <summary>
    /// RT 目前動態計算的連發間隔幀數
    /// </summary>
    private int _currentRTRepeatInterval;

    /// <summary>
    /// 是否有前一次的 XInputState
    /// </summary>
    private volatile bool _hasPreviousState;

    /// <summary>
    /// 是否已處置（0 = 未處置，1 = 已處置；使用 int 以支援 Interlocked.CompareExchange 原子操作）
    /// </summary>
    private volatile int _disposed;

    /// <summary>
    /// 是否處於暫停狀態（例如原生檔案對話框顯示期間）。
    /// </summary>
    private volatile bool _isPaused;

    /// <summary>
    /// 恢復輪詢後是否必須先回到中立狀態，避免把暫停期間的方向殘留當成新輸入。
    /// </summary>
    private volatile bool _requireNeutralBeforeInput;

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
    /// 狀態未變時的方向維持幀計數（防卡住）
    /// </summary>
    private int _directionalStaleFrameCounter;

    /// <summary>
    /// 狀態有變時的方向幽靈重入幀計數（防抖動復發）
    /// </summary>
    private int _directionalGhostFrameCounter;

    /// <summary>
    /// 是否暫時封鎖左搖桿映射為 D-Pad Right（防重入）
    /// </summary>
    private bool _suppressMappedRightFromLeftStick;

    /// <summary>
    /// 右向映射封鎖解除的中立幀計數
    /// </summary>
    private int _mappedRightNeutralFrameCounter;

    /// <summary>
    /// 右向映射封鎖剩餘冷卻幀數
    /// </summary>
    private int _mappedRightSuppressionCooldownFrames;

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
    private XInput.GamepadButton? _lastHealthDpadDirection;

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
    /// 上一次機制健康度輸出的映射保護狀態
    /// </summary>
    private bool _lastHealthMapGuardActive;

    /// <summary>
    /// 連發風暴診斷輸出間隔（每 N 次輸出一次）
    /// </summary>
    private const int RepeatStormLogInterval = 30;

    /// <summary>
    /// 機制健康度診斷心跳輸出間隔（每 N 幀輸出一次，約 10 秒）
    /// </summary>
    private const int MechanismHealthLogIntervalFrames = 600;

    /// <summary>
    /// 機制視為真正閒置前的連續幀數（約 2 秒）
    /// </summary>
    private const int MechanismIdleResetFrames = 120;
#endif

    /// <summary>
    /// 右向映射封鎖解除所需的連續中立幀數
    /// </summary>
    private const int MappedDirectionUnsuppressFrames = 6;

    /// <summary>
    /// 右向映射封鎖最長冷卻幀數（約 200ms）
    /// </summary>
    private const int MappedDirectionSuppressCooldownFrames = 12;

    // ── 自適應 EMA 係數（Adaptive Exponential Moving Average）────────────────
    // 設計原則：每個軸保有獨立的「基礎值」與「最大值」。
    //   • 當估計誤差（rawValue − currentBias）落在 BiasAdaptiveErrorRange 以內時，
    //     學習率從 Base 線性插值至 Max，誤差越大學習越快（快速收斂）。
    //   • 當誤差接近 0 時，退回 Base（保守維持），避免把有效輸入誤學成硬體偏移。
    //   • 係數與 GameInputGamepadController 對齊，確保兩路手把行為一致。

    /// <summary>
    /// 左搖桿 X 軸：偏移估計的最低保守學習率（誤差接近 0 時使用）。
    /// </summary>
    private const float LeftStickBiasXBaseSmoothing = 0.03f;

    /// <summary>
    /// 左搖桿 X 軸：偏移估計的最高學習率（誤差達到 BiasAdaptiveErrorRange 時使用）。
    /// </summary>
    private const float LeftStickBiasXMaxSmoothing = 0.15f;

    /// <summary>
    /// 左搖桿 Y 軸：偏移估計的最低保守學習率。
    /// </summary>
    private const float LeftStickBiasYBaseSmoothing = 0.03f;

    /// <summary>
    /// 左搖桿 Y 軸：偏移估計的最高學習率。
    /// </summary>
    private const float LeftStickBiasYMaxSmoothing = 0.12f;

    /// <summary>
    /// 右搖桿 X 軸：偏移估計的最低保守學習率。
    /// </summary>
    private const float RightStickBiasBaseSmoothing = 0.05f;

    /// <summary>
    /// 右搖桿 X 軸：偏移估計的最高學習率。
    /// 右搖桿無 D-Pad 閘門，低 Max 可防止快速劃過中立區時累積偏移。
    /// </summary>
    private const float RightStickBiasMaxSmoothing = 0.07f;

    /// <summary>
    /// 右搖桿 Y 軸：偏移估計的最低保守學習率。
    /// </summary>
    private const float RightStickBiasYBaseSmoothing = 0.05f;

    /// <summary>
    /// 右搖桿 Y 軸：偏移估計的最高學習率。
    /// </summary>
    private const float RightStickBiasYMaxSmoothing = 0.07f;

    /// <summary>
    /// 觸發全速學習的誤差閾值（short 尺度；對應 float 空間的 0.05，即 0.05 × 32767 ≈ 1638）。
    /// </summary>
    private const float BiasAdaptiveErrorRange = 1638f;

    /// <summary>
    /// 僅在接近中立時更新偏移，避免把有效操作學成偏移
    /// </summary>
    private const int LeftStickBiasLearningThreshold = 9000;

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
    /// 控制器 A, B, X, Y 按鈕事件
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
    public event Action? LeftTriggerRepeat;
    public event Action? RightTriggerRepeat;

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
    /// 連續震動硬體保護器（每個控制器實例獨立）。
    /// </summary>
    private readonly VibrationSafetyLimiter _vibrationSafetyLimiter = new();

#if DEBUG
    /// <summary>
    /// 震動診斷採樣計數（避免環境震動請求每次都寫檔造成訊息洪水）。
    /// </summary>
    private int _vibrationDiagSampleCounter;
#endif

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

        // 先以區域變數捕捉 Token，再寫入欄位；確保若 StopPolling（Interlocked.Exchange）
        // 在欄位寫入後即介入並 CancelAndDispose，Token 捕捉不會取得已釋放物件的屬性。
        CancellationTokenSource cts = new();

        CancellationToken token = cts.Token;

        _ctsPolling = cts;

        _taskPolling = Task.Run(() => PollingLoopAsync(token), token);
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

        if (_isPaused)
        {
            return;
        }

        // 嘗試讀取目前控制器狀態（本 Tick 只讀一次）。
        uint result = XInput.XInputGetState(_userIndex, out XInput.XInputState currentState);

        // 保留映射前的原始按鍵位元，避免 anti-stuck 判定被虛擬方向鍵污染。
        ushort rawButtons = currentState.Gamepad.Buttons;

        // D-Pad 按下時，左搖桿因機械耦合會產生偏移，需暫停左搖桿 bias 學習。
        bool isDPadActive = (((XInput.GamepadButton)rawButtons) & (
            XInput.GamepadButton.DpadLeft  |
            XInput.GamepadButton.DpadRight |
            XInput.GamepadButton.DpadUp    |
            XInput.GamepadButton.DpadDown)) != 0;

        UpdateStickBias(
            currentState.Gamepad.ThumbLeftX,
            currentState.Gamepad.ThumbLeftY,
            currentState.Gamepad.ThumbRightX,
            currentState.Gamepad.ThumbRightY,
            isDPadActive);

        int correctedLeftThumbX = (int)MathF.Round(currentState.Gamepad.ThumbLeftX - _leftStickBiasX),
            correctedLeftThumbY = (int)MathF.Round(currentState.Gamepad.ThumbLeftY - _leftStickBiasY),
            correctedRightThumbX = (int)MathF.Round(currentState.Gamepad.ThumbRightX - _rightStickBiasX),
            correctedRightThumbY = (int)MathF.Round(currentState.Gamepad.ThumbRightY - _rightStickBiasY);

        UpdateMappedRightSuppression(correctedLeftThumbX, config);

        if (result != 0)
        {
            ResetMechanismHealthLogState();

            if (_hasPreviousState)
            {
                _hasPreviousState = false;

                // 連線中斷時清空熱狀態，避免重連後沿用舊熱負載造成過度保守。
                _vibrationSafetyLimiter.Reset();

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

                    // Bias warm-up：以初始讀值連續執行 50 次 EMA，
                    // 使偏移估計在第一幀即接近真實值（收斂率 ≈ 99%），
                    // 避免連線初期因 bias ≈ 0 造成方向誤判。
                    InitializeDeviceBias(newState);

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
                // Bias warm-up：以初始讀值連續執行 50 次 EMA，
                // 使偏移估計在第一幀即接近真實值（收斂率 ≈ 99%），
                // 避免連線初期因 bias ≈ 0 造成方向誤判。
                InitializeDeviceBias(currentState);

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

        // 將搖桿訊號合併到按鍵狀態中（使用偏移校正後的左搖桿）。
        ApplyStickToButtons(
            ref currentState,
            _previousState,
            config,
            _suppressMappedRightFromLeftStick,
            correctedLeftThumbX,
            correctedLeftThumbY);

        if (_requireNeutralBeforeInput)
        {
            bool hasActiveSignal = GamepadSignalEvaluator.IsActive(
                rawButtons != 0,
                currentState.Gamepad.LeftTrigger,
                currentState.Gamepad.RightTrigger,
                correctedLeftThumbX,
                correctedLeftThumbY,
                correctedRightThumbX,
                correctedRightThumbY,
                AppSettings.XInputTriggerThreshold,
                config.ThumbDeadzoneExit);

            _previousState = currentState;
            _hasPreviousState = true;

            if (hasActiveSignal)
            {
                _repeatCounter = 0;
                _repeatDirection = null;
                _rsRepeatCounter = 0;
                _rsRepeatDirection = 0;
                _ltRepeatCounter = 0;
                _currentLTRepeatInterval = 0;
                _rtRepeatCounter = 0;
                _currentRTRepeatInterval = 0;

                return;
            }

            _requireNeutralBeforeInput = false;

            return;
        }

        // 只有在 Input 啟用時才處理按鍵。
        if (!_context.IsInputActive)
        {
            _repeatCounter = 0;
            _repeatDirection = null;
            _rsRepeatCounter = 0;
            _rsRepeatDirection = 0;
            _directionalStaleFrameCounter = 0;
            _directionalGhostFrameCounter = 0;

            ResetMechanismHealthLogState();

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
            _directionalStaleFrameCounter = 0;

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
            int currentRsDir = GamepadDeadzoneHysteresis.ResolveDirection(
                correctedRightThumbX,
                _rsRepeatDirection == -1,
                _rsRepeatDirection == 1,
                config.ThumbDeadzoneEnter,
                config.ThumbDeadzoneExit);

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
        else if (HasActiveDirectionalRepeat())
        {
            if (ShouldForceReleaseDirectionalRepeat(
                rawButtons,
                correctedLeftThumbX,
                correctedLeftThumbY,
                correctedRightThumbX,
                config))
            {
                _directionalStaleFrameCounter++;

                if (_directionalStaleFrameCounter >= AppSettings.GamepadDirectionalStuckGuardFrames)
                {
                    LoggerService.LogInfo($"Gamepad.AntiStuckTriggered source=XInput staleFrames={_directionalStaleFrameCounter} dpadDir={_repeatDirection?.ToString() ?? "None"} rsDir={_rsRepeatDirection}");

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

        HandleRepeat(currentState, config);

        EvaluateDirectionalGhostState(
            currentState,
            rawButtons,
            correctedLeftThumbX,
            correctedLeftThumbY,
            correctedRightThumbX,
            config);

        EmitMechanismHealthLog(
            correctedLeftThumbX,
            correctedLeftThumbY,
            correctedRightThumbX,
            correctedRightThumbY,
            config);

        _previousState = currentState;
        _hasPreviousState = true;
    }

    /// <summary>
    /// 低頻輸出搖桿機制健康度，協助確認 bias／deadzone／hysteresis／repeat 正在生效
    /// </summary>
    /// <param name="correctedLeftThumbX">左搖桿 X 軸修正值</param>
    /// <param name="correctedLeftThumbY">左搖桿 Y 軸修正值</param>
    /// <param name="correctedRightThumbX">右搖桿 X 軸修正值</param>
    /// <param name="correctedRightThumbY">右搖桿 Y 軸修正值（僅用於診斷日誌，不影響導航判斷）</param>
    /// <param name="config">遊戲控制器配置快照</param>
    private void EmitMechanismHealthLog(
        int correctedLeftThumbX,
        int correctedLeftThumbY,
        int correctedRightThumbX,
        int correctedRightThumbY,
        AppSettings.GamepadConfigSnapshot config)
    {
#if DEBUG
        bool hasSignificantInput =
                Math.Abs(correctedLeftThumbX) > config.ThumbDeadzoneExit ||
                Math.Abs(correctedLeftThumbY) > config.ThumbDeadzoneExit ||
                Math.Abs(correctedRightThumbX) > config.ThumbDeadzoneExit,
            isEngaged = hasSignificantInput ||
                _repeatDirection.HasValue ||
                _rsRepeatDirection != 0 ||
                _suppressMappedRightFromLeftStick;

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
                ghostActive != _lastHealthGhostActive ||
                _suppressMappedRightFromLeftStick != _lastHealthMapGuardActive;

        _mechanismHealthLogCounter++;

        if (!_wasMechanismEngaged ||
            stateChanged ||
            _mechanismHealthLogCounter >= MechanismHealthLogIntervalFrames)
        {
            LoggerService.LogInfo(
                $"Gamepad.MechanismHealth source=XInput stage=bias_deadzone_hysteresis_repeat dpadDir={_repeatDirection?.ToString() ?? "None"} rsDir={_rsRepeatDirection} lx={correctedLeftThumbX} ly={correctedLeftThumbY} rx={correctedRightThumbX} ry={correctedRightThumbY} biasLx={(int)MathF.Round(_leftStickBiasX)} biasLy={(int)MathF.Round(_leftStickBiasY)} biasRx={(int)MathF.Round(_rightStickBiasX)} biasRy={(int)MathF.Round(_rightStickBiasY)} enter={config.ThumbDeadzoneEnter} exit={config.ThumbDeadzoneExit} stale={_directionalStaleFrameCounter} ghost={_directionalGhostFrameCounter} mapGuard={_suppressMappedRightFromLeftStick}");

            _mechanismHealthLogCounter = 0;
            _lastHealthDpadDirection = _repeatDirection;
            _lastHealthRsDirection = _rsRepeatDirection;
            _lastHealthStaleActive = staleActive;
            _lastHealthGhostActive = ghostActive;
            _lastHealthMapGuardActive = _suppressMappedRightFromLeftStick;
        }

        _wasMechanismEngaged = true;
#endif
    }

    /// <summary>
    /// 重置機制健康度診斷狀態
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
        _lastHealthMapGuardActive = false;
#endif
    }

    /// <summary>
    /// 評估方向幽靈重入狀態：處理「狀態有變化但方向持續誤判」情境
    /// </summary>
    /// <param name="state">XInput 狀態快照</param>
    /// <param name="rawButtons">原始按鍵狀態</param>
    /// <param name="correctedLeftThumbX">左搖桿 X 軸修正值</param>
    /// <param name="correctedLeftThumbY">左搖桿 Y 軸修正值</param>
    /// <param name="correctedRightThumbX">右搖桿 X 軸修正值</param>
    /// <param name="config">遊戲控制器配置快照</param>
    private void EvaluateDirectionalGhostState(
        in XInput.XInputState state,
        ushort rawButtons,
        int correctedLeftThumbX,
        int correctedLeftThumbY,
        int correctedRightThumbX,
        AppSettings.GamepadConfigSnapshot config)
    {
        if (!HasActiveDirectionalRepeat())
        {
            _directionalGhostFrameCounter = 0;

            return;
        }

        if (!ShouldForceReleaseDirectionalRepeat(
            rawButtons,
            correctedLeftThumbX,
            correctedLeftThumbY,
            correctedRightThumbX,
            config))
        {
            _directionalGhostFrameCounter = 0;

            return;
        }

        _directionalGhostFrameCounter++;

        if (_directionalGhostFrameCounter < AppSettings.GamepadDirectionalStuckGuardFrames)
        {
            return;
        }

        bool rawDpadRightDown = (((XInput.GamepadButton)rawButtons) & XInput.GamepadButton.DpadRight) != 0;

        int leftThumbX = state.Gamepad.ThumbLeftX,
            leftThumbY = state.Gamepad.ThumbLeftY,
            rightThumbX = state.Gamepad.ThumbRightX,
            rightThumbY = state.Gamepad.ThumbRightY;

        LoggerService.LogInfo($"Gamepad.AntiStuckTriggered source=XInput reason=ghost_reentry ghostFrames={_directionalGhostFrameCounter} dpadDir={_repeatDirection?.ToString() ?? "None"} rsDir={_rsRepeatDirection} rawDpadRight={rawDpadRightDown} lx={leftThumbX} ly={leftThumbY} rx={rightThumbX} ry={rightThumbY} biasLx={(int)MathF.Round(_leftStickBiasX)} biasLy={(int)MathF.Round(_leftStickBiasY)} biasRx={(int)MathF.Round(_rightStickBiasX)} biasRy={(int)MathF.Round(_rightStickBiasY)}");

        ResetDirectionalRepeatState();
    }

    /// <summary>
    /// 更新右向映射封鎖狀態，避免 anti-stuck 觸發後立即重入
    /// </summary>
    /// <param name="correctedThumbLeftX">修正後的左搖桿 X 軸值</param>
    /// <param name="config">遊戲控制器配置快照</param>
    private void UpdateMappedRightSuppression(
        int correctedThumbLeftX,
        AppSettings.GamepadConfigSnapshot config)
    {
        if (!_suppressMappedRightFromLeftStick)
        {
            return;
        }

        if (_mappedRightSuppressionCooldownFrames > 0)
        {
            _mappedRightSuppressionCooldownFrames--;

            // 冷卻期間一律不放行，避免 anti-stuck 後立即重入。
            return;
        }

        if (Math.Abs(correctedThumbLeftX) <= config.ThumbDeadzoneExit)
        {
            _mappedRightNeutralFrameCounter++;
        }
        else
        {
            _mappedRightNeutralFrameCounter = 0;
        }

        if (_mappedRightNeutralFrameCounter >= MappedDirectionUnsuppressFrames)
        {
            _suppressMappedRightFromLeftStick = false;
            _mappedRightNeutralFrameCounter = 0;
            _mappedRightSuppressionCooldownFrames = 0;

            LoggerService.LogInfo("Gamepad.MappingGuardReleased source=XInput direction=Right reason=neutral");
        }
    }

    /// <summary>
    /// Bias warm-up：以初始讀值連續執行 50 次 EMA，
    /// 使偏移估計在第一幀即接近真實值（收斂率 ≈ 99%），
    /// 避免連線初期因 bias ≈ 0 造成方向誤判。
    /// </summary>
    /// <param name="state">連線時的第一份 XInputState 快照。</param>
    private void InitializeDeviceBias(XInput.XInputState state)
    {
        for (int i = 0; i < 50; i++)
        {
            // 暖機時不存在 D-Pad 操作情境，閘門固定傳 false。
            UpdateStickBias(
                state.Gamepad.ThumbLeftX,
                state.Gamepad.ThumbLeftY,
                state.Gamepad.ThumbRightX,
                state.Gamepad.ThumbRightY,
                isDPadActive: false);
        }
    }

    /// <summary>
    /// 以接近中立區段估計搖桿中心偏移，降低固定偏壓造成的方向誤判
    /// </summary>
    /// <param name="rawLeftThumbX">原始左搖桿 X 軸值</param>
    /// <param name="rawLeftThumbY">原始左搖桿 Y 軸值</param>
    /// <param name="rawRightThumbX">原始右搖桿 X 軸值</param>
    /// <param name="rawRightThumbY">原始右搖桿 Y 軸值</param>
    /// <param name="isDPadActive">是否有任意 D-Pad 方向目前被按下；為 true 時暫停左搖桿 bias 學習以避免機械耦合污染。</param>
    private void UpdateStickBias(
        short rawLeftThumbX,
        short rawLeftThumbY,
        short rawRightThumbX,
        short rawRightThumbY,
        bool isDPadActive)
    {
        if (!isDPadActive && Math.Abs((int)rawLeftThumbX) <= LeftStickBiasLearningThreshold)
        {
            float errorLX = rawLeftThumbX - _leftStickBiasX;
            _leftStickBiasX += errorLX * ComputeAdaptiveBiasSmoothing(
                errorLX, LeftStickBiasXBaseSmoothing, LeftStickBiasXMaxSmoothing);
        }

        if (!isDPadActive && Math.Abs((int)rawLeftThumbY) <= LeftStickBiasLearningThreshold)
        {
            float errorLY = rawLeftThumbY - _leftStickBiasY;
            _leftStickBiasY += errorLY * ComputeAdaptiveBiasSmoothing(
                errorLY, LeftStickBiasYBaseSmoothing, LeftStickBiasYMaxSmoothing);
        }

        if (Math.Abs((int)rawRightThumbX) <= LeftStickBiasLearningThreshold)
        {
            float errorRX = rawRightThumbX - _rightStickBiasX;
            _rightStickBiasX += errorRX * ComputeAdaptiveBiasSmoothing(
                errorRX, RightStickBiasBaseSmoothing, RightStickBiasMaxSmoothing);
        }

        if (Math.Abs((int)rawRightThumbY) <= LeftStickBiasLearningThreshold)
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
    /// 是否存在方向連發狀態（D-Pad 或 RS）
    /// </summary>
    /// <returns>是否存在方向連發狀態</returns>
    private bool HasActiveDirectionalRepeat()
    {
        return _repeatDirection.HasValue ||
            _rsRepeatDirection != 0;
    }

    /// <summary>
    /// 判斷目前是否符合「疑似已放開但狀態未更新」條件
    /// </summary>
    /// <param name="rawButtons">原始按鈕狀態</param>
    /// <param name="correctedLeftThumbX">修正後的左搖桿 X 軸值</param>
    /// <param name="correctedLeftThumbY">修正後的左搖桿 Y 軸值</param>
    /// <param name="correctedRightThumbX">修正後的右搖桿 X 軸值</param>
    /// <param name="config">遊戲控制器配置快照</param>
    /// <returns>是否應強制釋放方向連發狀態</returns>
    private static bool ShouldForceReleaseDirectionalRepeat(
        ushort rawButtons,
        int correctedLeftThumbX,
        int correctedLeftThumbY,
        int correctedRightThumbX,
        AppSettings.GamepadConfigSnapshot config)
    {
        const XInput.GamepadButton directionalFlags =
            XInput.GamepadButton.DpadLeft |
            XInput.GamepadButton.DpadRight |
            XInput.GamepadButton.DpadUp |
            XInput.GamepadButton.DpadDown;

        if (((XInput.GamepadButton)rawButtons & directionalFlags) != 0)
        {
            return false;
        }

        int softCenterThreshold = Math.Min(
            config.ThumbDeadzoneEnter,
            config.ThumbDeadzoneExit + Math.Max(1000, (config.ThumbDeadzoneEnter - config.ThumbDeadzoneExit) / 2));

        bool leftStickNearCenter =
                Math.Abs(correctedLeftThumbX) <= softCenterThreshold &&
                Math.Abs(correctedLeftThumbY) <= softCenterThreshold,
            rightStickNearCenter =
                Math.Abs(correctedRightThumbX) <= softCenterThreshold;

        return leftStickNearCenter &&
            rightStickNearCenter;
    }

    /// <summary>
    /// 重置方向連發狀態（含 D-Pad 與 RS）
    /// </summary>
    private void ResetDirectionalRepeatState()
    {
        bool wasDpadRight = _repeatDirection == XInput.GamepadButton.DpadRight;

        _repeatDirection = null;
        _repeatCounter = 0;
        _currentRepeatInterval = 0;

        _rsRepeatDirection = 0;
        _rsRepeatCounter = 0;
        _currentRSRepeatInterval = 0;

        _directionalStaleFrameCounter = 0;
        _directionalGhostFrameCounter = 0;

        if (wasDpadRight)
        {
            _suppressMappedRightFromLeftStick = true;
            _mappedRightNeutralFrameCounter = 0;
            _mappedRightSuppressionCooldownFrames = MappedDirectionSuppressCooldownFrames;

            LoggerService.LogInfo("Gamepad.MappingGuardEnabled source=XInput direction=Right");
        }
    }

    /// <summary>
    /// 重置暫態輸入狀態，避免視窗切換後把舊方向、連發或震動殘留到下一個互動情境。
    /// </summary>
    /// <param name="requireNeutralBeforeInput">恢復後是否必須先觀察到中立輸入，才重新接受方向事件。</param>
    private void ResetTransientInputState(bool requireNeutralBeforeInput = true)
    {
        StopVibration();
        ResetHoldStates();
        ResetDirectionalRepeatState();

        _ltRepeatCounter = 0;
        _currentLTRepeatInterval = 0;
        _rtRepeatCounter = 0;
        _currentRTRepeatInterval = 0;
        _reconnectCounter = 0;
        _previousState = default;
        _hasPreviousState = false;
        _requireNeutralBeforeInput = requireNeutralBeforeInput;
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
                if (GamepadSignalEvaluator.IsActive(
                    state.Gamepad.Buttons != 0,
                    state.Gamepad.LeftTrigger,
                    state.Gamepad.RightTrigger,
                    state.Gamepad.ThumbLeftX,
                    state.Gamepad.ThumbLeftY,
                    state.Gamepad.ThumbRightX,
                    state.Gamepad.ThumbRightY,
                    AppSettings.XInputTriggerThreshold,
                    AppSettings.XInputActiveThumbstickThreshold))
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
#if DEBUG
        bool emittedDpadRightRepeat = false,
            emittedRsRightRepeat = false;
#endif

        // 處理 D-Pad 重複輸入。
        XInput.GamepadButton? gbCurrentDirection =
            state.Has(XInput.GamepadButton.DpadLeft) ? XInput.GamepadButton.DpadLeft :
            state.Has(XInput.GamepadButton.DpadRight) ? XInput.GamepadButton.DpadRight :
            state.Has(XInput.GamepadButton.DpadUp) ? XInput.GamepadButton.DpadUp :
            state.Has(XInput.GamepadButton.DpadDown) ? XInput.GamepadButton.DpadDown :
            null;

        if (GamepadRepeatStateMachine.AdvanceDirectionRepeat(
                gbCurrentDirection,
                ref _repeatDirection,
                ref _repeatCounter,
                ref _currentRepeatInterval,
                config.RepeatInitialDelayFrames,
                config.RepeatIntervalFrames))
        {
            if (gbCurrentDirection == XInput.GamepadButton.DpadLeft)
            {
                LeftRepeat?.Invoke();
            }
            else if (gbCurrentDirection == XInput.GamepadButton.DpadRight)
            {
                RightRepeat?.Invoke();

#if DEBUG
                emittedDpadRightRepeat = true;
#endif
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

        // 處理右搖桿（RS）重複輸入。
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

        // 處理左觸發鍵（LT）連發輸入。
        if (GamepadRepeatStateMachine.AdvanceHeldRepeat(
                IsLeftTriggerHeld,
                ref _ltRepeatCounter,
                ref _currentLTRepeatInterval,
                config.RepeatInitialDelayFrames,
                config.RepeatIntervalFrames))
        {
            LeftTriggerRepeat?.Invoke();
        }

        // 處理右觸發鍵（RT）連發輸入。
        if (GamepadRepeatStateMachine.AdvanceHeldRepeat(
                IsRightTriggerHeld,
                ref _rtRepeatCounter,
                ref _currentRTRepeatInterval,
                config.RepeatInitialDelayFrames,
                config.RepeatIntervalFrames))
        {
            RightTriggerRepeat?.Invoke();
        }

#if DEBUG
        if (emittedDpadRightRepeat)
        {
            _dpadRightRepeatDiagnosticCounter++;

            if (AppSettings.Current.GamepadProviderType == AppSettings.GamepadProvider.XInput &&
                _dpadRightRepeatDiagnosticCounter % RepeatStormLogInterval == 0)
            {
                LoggerService.LogInfo($"Gamepad.RightRepeatStorm source=XInput kind=DPad count={_dpadRightRepeatDiagnosticCounter} dpadDir={_repeatDirection?.ToString() ?? "None"}");
            }
        }
        else if (gbCurrentDirection != XInput.GamepadButton.DpadRight)
        {
            _dpadRightRepeatDiagnosticCounter = 0;
        }

        if (emittedRsRightRepeat)
        {
            _rsRightRepeatDiagnosticCounter++;

            if (AppSettings.Current.GamepadProviderType == AppSettings.GamepadProvider.XInput &&
                _rsRightRepeatDiagnosticCounter % RepeatStormLogInterval == 0)
            {
                LoggerService.LogInfo($"Gamepad.RightRepeatStorm source=XInput kind=RS count={_rsRightRepeatDiagnosticCounter} rsDir={_rsRepeatDirection}");
            }
        }
        else if (rsDir != 1)
        {
            _rsRightRepeatDiagnosticCounter = 0;
        }
#endif
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
    /// <param name="suppressMappedRightFromLeftStick">是否抑制左搖桿映射的右向按鍵</param>
    /// <param name="correctedLeftThumbX">修正後的左搖桿 X 軸值</param>
    /// <param name="correctedLeftThumbY">修正後的左搖桿 Y 軸值</param>
    private static void ApplyStickToButtons(
        ref XInput.XInputState currentState,
        XInput.XInputState previousState,
        AppSettings.GamepadConfigSnapshot config,
        bool suppressMappedRightFromLeftStick,
        int correctedLeftThumbX,
        int correctedLeftThumbY)
    {
        // 注意：previousState 包含了上一幀的「實體按鍵」+「虛擬搖桿按鍵」的結果，
        // 這正好符合我們需要的遲滯行為（保持狀態）。
        bool wasLeft = previousState.Has(XInput.GamepadButton.DpadLeft),
            wasRight = previousState.Has(XInput.GamepadButton.DpadRight),
            wasUp = previousState.Has(XInput.GamepadButton.DpadUp),
            wasDown = previousState.Has(XInput.GamepadButton.DpadDown);

        // 右向映射在筆電環境較容易受正偏噪聲影響，
        // 這裡對「已在右向」時使用更高的退出門檻，避免進入後黏滯在 DPadRight。
        int rightExitThreshold = Math.Max(
                config.ThumbDeadzoneExit,
                (int)(config.ThumbDeadzoneEnter * 0.75f)),
            thresholdNegative = wasLeft ?
                config.ThumbDeadzoneExit :
                config.ThumbDeadzoneEnter,
            thresholdPositive = wasRight ?
                rightExitThreshold :
                config.ThumbDeadzoneEnter,
            horizontalDirection = correctedLeftThumbX < -thresholdNegative ?
                -1 :
                correctedLeftThumbX > thresholdPositive ?
                    1 :
                    0;

        if (horizontalDirection < 0)
        {
            // 搖桿向左 -> 視為按下 D-Pad Left。
            currentState.Gamepad.Buttons |= (ushort)XInput.GamepadButton.DpadLeft;
        }
        else if (horizontalDirection > 0 &&
            !suppressMappedRightFromLeftStick)
        {
            // 搖桿向右 -> 視為按下 D-Pad Right。
            currentState.Gamepad.Buttons |= (ushort)XInput.GamepadButton.DpadRight;
        }

        int verticalDirection = GamepadDeadzoneHysteresis.ResolveDirection(
            correctedLeftThumbY,
            wasDown,
            wasUp,
            config.ThumbDeadzoneEnter,
            config.ThumbDeadzoneExit);

        if (verticalDirection < 0)
        {
            // 搖桿向下 -> 視為按下 D-Pad Down。
            currentState.Gamepad.Buttons |= (ushort)XInput.GamepadButton.DpadDown;
        }
        else if (verticalDirection > 0)
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

            uint stopResult = XInput.XInputSetState(_userIndex, in stopVibration);

#if DEBUG
            if (stopResult != 0)
            {
                LoggerService.LogInfo(
                    $"VibrationDiag source=XInput stage=api action=stop outcome=failed reason=sync-stop result={stopResult} userIndex={_userIndex}");
            }
#endif
        }
        catch (Exception ex)
        {
#if DEBUG
            LoggerService.LogInfo($"VibrationDiag source=XInput stage=api action=stop outcome=failed reason=sync-stop exception={ex.GetType().Name} message={ex.Message}");
#endif
            Debug.WriteLine($"[XInput] StopVibration 失敗（已忽略）：{ex.Message}");
        }
    }

    /// <summary>
    /// 震動
    /// </summary>
    /// <param name="strength">強度</param>
    /// <param name="milliseconds">毫秒，預設為 60</param>
    /// <param name="priority">震動優先級</param>
    /// <param name="ct">取消權杖</param>
    /// <returns>Task</returns>
    public Task VibrateAsync(
        ushort strength,
        int milliseconds = 60,
        VibrationPriority priority = VibrationPriority.Normal,
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

        bool accepted = _vibrationSafetyLimiter.TryApplyWithDiagnostics(
            strength,
            milliseconds,
            priority,
            out ushort safeStrength,
            out int safeDurationMs,
            out VibrationLimiterDebugInfo limiterDiagnostics,
            thermalCostMultiplier: 2.0);

#if DEBUG
        bool enableDebugDiagnostics =
            AppSettings.Current.EnableVibration &&
            AppSettings.Current.VibrationIntensity > 0f;

        bool shouldLogAccepted = priority != VibrationPriority.Ambient ||
            safeStrength != strength ||
            safeDurationMs != Math.Clamp(milliseconds, 1, 1000) ||
            Interlocked.Increment(ref _vibrationDiagSampleCounter) % 20 == 0;

        if (!accepted)
        {
            if (enableDebugDiagnostics)
            {
                LoggerService.LogInfo(
                    $"VibrationDiag source=XInput stage=limiter decision=blocked priority={priority} reqStrength={strength} reqMs={milliseconds} duty={limiterDiagnostics.DutyCycle:F3} thermal={limiterDiagnostics.ThermalLoad:F2} scale={limiterDiagnostics.AppliedScale:F3} flags={limiterDiagnostics.Flags} ambientCooldownMs={limiterDiagnostics.AmbientCooldownRemainingMs} fwProtectionSuspect=no appProtection=true");
            }
        }
        else if (enableDebugDiagnostics && shouldLogAccepted)
        {
            bool firmwareProtectionRisk = safeStrength >= 50000 && safeDurationMs >= 120 && priority != VibrationPriority.Critical;

            LoggerService.LogInfo(
                $"VibrationDiag source=XInput stage=limiter decision=accepted priority={priority} reqStrength={strength} reqMs={milliseconds} safeStrength={safeStrength} safeMs={safeDurationMs} duty={limiterDiagnostics.DutyCycle:F3} thermal={limiterDiagnostics.ThermalLoad:F2} scale={limiterDiagnostics.AppliedScale:F3} flags={limiterDiagnostics.Flags} appProtection={(safeStrength != strength || safeDurationMs != Math.Clamp(milliseconds, 1, 1000))} fwProtectionSuspect={(firmwareProtectionRisk ? "possible" : "low")}");
        }
#endif

        if (!accepted)
        {
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

            CancellationToken token;

            lock (_vibrationLock)
            {
                // 每次呼叫時產生一個新的通行證（Token）。
                currentToken = Interlocked.Increment(ref _vibrationToken);

                // 取消並更換 CTS，確保只有最後一個震動任務的延遲會執行。
                Interlocked.Exchange(ref _vibrationCts, null)?.CancelAndDispose();

                _vibrationCts = newCts;

                // 在鎖內取得 Token，避免鎖外其他執行緒 CancelAndDispose() 後
                // 再存取 newCts.Token 屬性時拋出 ObjectDisposedException。
                token = newCts.Token;
            }

            XInput.XInputVibration vibration = new()
            {
                LeftMotorSpeed = safeStrength,
                RightMotorSpeed = safeStrength
            };

            uint startResult = XInput.XInputSetState(userIndex, in vibration);

#if DEBUG
            if (startResult != 0)
            {
                LoggerService.LogInfo(
                    $"VibrationDiag source=XInput stage=api action=start outcome=failed result={startResult} userIndex={userIndex} strength={safeStrength} durationMs={safeDurationMs} priority={priority}");

                return;
            }

            bool shouldLogApiSuccess = priority != VibrationPriority.Ambient ||
                Interlocked.Increment(ref _vibrationDiagSampleCounter) % 20 == 0;

            if (enableDebugDiagnostics && shouldLogApiSuccess)
            {
                LoggerService.LogInfo(
                    $"VibrationDiag source=XInput stage=api action=start outcome=ok result={startResult} userIndex={userIndex} strength={safeStrength} durationMs={safeDurationMs} priority={priority}");
            }
#else
            if (startResult != 0)
            {
                return;
            }
#endif

            // 將「內部震動覆蓋權杖」與「外部傳入的取消權杖」綁定在一起。
            using CancellationTokenSource linkedCts = CancellationTokenSource
                .CreateLinkedTokenSource(token, ct);

            try
            {
                // 只要有新震動進來，或外部要求取消，這裡的 Delay 就會立刻中斷。
                await Task.Delay(safeDurationMs, linkedCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // 判斷是誰觸發了取消？
                if (ct.IsCancellationRequested)
                {
                    // 狀況 A：外部 ct 被取消了（例如：使用者關閉視窗、切換頁面）
                    // 這種情況下，不會有新的震動來接管，我們必須「強制煞車」！
                    XInput.XInputVibration stop = default;

                    uint stopResult = XInput.XInputSetState(userIndex, in stop);

#if DEBUG
                    if (stopResult != 0)
                    {
                        LoggerService.LogInfo(
                            $"VibrationDiag source=XInput stage=api action=stop outcome=failed reason=external-cancel result={stopResult} userIndex={userIndex}");
                    }
#endif
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

            uint stopFinalResult = XInput.XInputSetState(userIndex, in stopVibration);

#if DEBUG
            if (stopFinalResult != 0)
            {
                LoggerService.LogInfo(
                    $"VibrationDiag source=XInput stage=api action=stop outcome=failed reason=normal-finish result={stopFinalResult} userIndex={userIndex}");
            }
#endif
        }, ct);
    }

    /// <summary>
    /// 暫停
    /// </summary>
    public void Pause()
    {
        _isPaused = true;
        StopPolling();
        ResetTransientInputState();
    }

    /// <summary>
    /// 恢復
    /// </summary>
    public void Resume()
    {
        if (_disposed != 0)
        {
            return;
        }

        ResetTransientInputState();
        _isPaused = false;
        StartPolling();
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

        // 確保馬達即時歸零，並在鎖內原子取消現有 CTS（與 StopVibration / VibrateAsync 保持鎖邊界一致）。
        StopVibration();

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
        LeftTriggerRepeat = null;
        RightTriggerRepeat = null;
        ConnectionChanged = null;
    }
}