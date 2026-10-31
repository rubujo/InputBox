using InputBox.Core.Configuration;
using InputBox.Core.Input;
using InputBox.Core.Interop;
using InputWeave.GameInput;
using InputWeave.GameInput.Interop;
using System.Reflection;
using Xunit;

namespace InputBox.Tests;

/// <summary>
/// 驗證控制器在暫停輪詢時，會清掉暫態輸入與連發狀態，避免原生對話框返回後出現方向卡住。
/// </summary>
public sealed class GamepadControllerPauseTests
{
    /// <summary>
    /// 提供測試使用的最小輸入情境，固定回報可接受輸入。
    /// </summary>
    private sealed class StubInputContext : IInputContext
    {
        /// <summary>
        /// 取得目前是否允許控制器輸入。
        /// </summary>
        public bool IsInputActive => true;

        /// <summary>
        /// 測試替身不持有任何外部資源，因此不需實際處置。
        /// </summary>
        public void Dispose()
        {

        }
    }

    /// <summary>
    /// XInput 控制器在 Pause 後，應重置上一幀快照與各種連發計數器，避免恢復時把舊方向視為仍然按住。
    /// </summary>
    [Fact]
    public void Pause_XInputController_ClearsTransientRuntimeState()
    {
        using var controller = new XInputGamepadController(new StubInputContext());

        SetPrivateField(controller, "_repeatCounter", 5);
        SetPrivateField(controller, "_currentRepeatInterval", 3);
        SetPrivateField(controller, "_rsRepeatCounter", 4);
        SetPrivateField(controller, "_currentRSRepeatInterval", 2);
        SetPrivateField(controller, "_ltRepeatCounter", 7);
        SetPrivateField(controller, "_currentLTRepeatInterval", 1);
        SetPrivateField(controller, "_rtRepeatCounter", 6);
        SetPrivateField(controller, "_currentRTRepeatInterval", 1);
        SetPrivateField(controller, "_rsRepeatDirection", 1);
        SetPrivateField(controller, "_repeatDirection", XInput.GamepadButton.DpadRight);
        SetPrivateField(controller, "_hasPreviousState", true);

        controller.Pause();

        Assert.False(GetPrivateField<bool>(controller, "_hasPreviousState"));
        Assert.Equal(0, GetPrivateField<int>(controller, "_repeatCounter"));
        Assert.Equal(0, GetPrivateField<int>(controller, "_currentRepeatInterval"));
        Assert.Equal(0, GetPrivateField<int>(controller, "_rsRepeatCounter"));
        Assert.Equal(0, GetPrivateField<int>(controller, "_currentRSRepeatInterval"));
        Assert.Equal(0, GetPrivateField<int>(controller, "_ltRepeatCounter"));
        Assert.Equal(0, GetPrivateField<int>(controller, "_currentLTRepeatInterval"));
        Assert.Equal(0, GetPrivateField<int>(controller, "_rtRepeatCounter"));
        Assert.Equal(0, GetPrivateField<int>(controller, "_currentRTRepeatInterval"));
        Assert.Equal(0, GetPrivateField<int>(controller, "_rsRepeatDirection"));
        Assert.Null(GetPrivateField<XInput.GamepadButton?>(controller, "_repeatDirection"));
    }

    /// <summary>
    /// GameInput 控制器在 Pause 後，應丟棄暫態按鍵快照與加工後方向狀態，避免原生檔案對話框關閉後重播舊輸入。
    /// </summary>
    [Fact]
    public void Pause_GameInputController_ClearsTransientRuntimeState()
    {
        using var controller = (GameInputGamepadController)Activator.CreateInstance(
            typeof(GameInputGamepadController),
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            args: [new StubInputContext(), null],
            culture: null)!;

        SetPrivateField(controller, "_repeatCounter", 5);
        SetPrivateField(controller, "_currentRepeatInterval", 3);
        SetPrivateField(controller, "_rsRepeatCounter", 4);
        SetPrivateField(controller, "_currentRSRepeatInterval", 2);
        SetPrivateField(controller, "_ltRepeatCounter", 7);
        SetPrivateField(controller, "_currentLTRepeatInterval", 1);
        SetPrivateField(controller, "_rtRepeatCounter", 6);
        SetPrivateField(controller, "_currentRTRepeatInterval", 1);
        SetPrivateField(controller, "_rsRepeatDirection", 1);
        SetPrivateField(controller, "_repeatDirection", GameInputGamepadButtons.GameInputGamepadDPadRight);
        SetPrivateField(controller, "_previousProcessedButtons", GameInputGamepadButtons.GameInputGamepadDPadRight);
        SetPrivateField(controller, "_hasPreviousState", true);

        controller.Pause();

        Assert.False(GetPrivateField<bool>(controller, "_hasPreviousState"));
        Assert.Equal(0, GetPrivateField<int>(controller, "_repeatCounter"));
        Assert.Equal(0, GetPrivateField<int>(controller, "_currentRepeatInterval"));
        Assert.Equal(0, GetPrivateField<int>(controller, "_rsRepeatCounter"));
        Assert.Equal(0, GetPrivateField<int>(controller, "_currentRSRepeatInterval"));
        Assert.Equal(0, GetPrivateField<int>(controller, "_ltRepeatCounter"));
        Assert.Equal(0, GetPrivateField<int>(controller, "_currentLTRepeatInterval"));
        Assert.Equal(0, GetPrivateField<int>(controller, "_rtRepeatCounter"));
        Assert.Equal(0, GetPrivateField<int>(controller, "_currentRTRepeatInterval"));
        Assert.Equal(0, GetPrivateField<int>(controller, "_rsRepeatDirection"));
        Assert.Null(GetPrivateField<GameInputGamepadButtons?>(controller, "_repeatDirection"));
        Assert.Equal((GameInputGamepadButtons)0, GetPrivateField<GameInputGamepadButtons>(controller, "_previousProcessedButtons"));
    }

    /// <summary>
    /// GameInput 在恢復前景後若目前快照已經是中立狀態，應立即解除中立等待閘門，避免第一個 Back/View 按壓被吞掉而必須按第二次。
    /// </summary>
    [Fact]
    public void PrimeResumeStateFromSnapshot_WhenIdle_ClearsNeutralGate()
    {
        using var controller = (GameInputGamepadController)Activator.CreateInstance(
            typeof(GameInputGamepadController),
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            args: [new StubInputContext(), null],
            culture: null)!;

        MethodInfo primeMethod = typeof(GameInputGamepadController).GetMethod(
            "PrimeResumeStateFromSnapshot",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("找不到 GameInputGamepadController.PrimeResumeStateFromSnapshot。");

        SetPrivateField(controller, "_requireNeutralBeforeInput", true);

        AppSettings.GamepadConfigSnapshot config = AppSettings.Current.GamepadSettings;
        GamepadReadingSnapshot idleState = CreateGameInputSnapshot(new GameInputGamepadState());

        _ = primeMethod.Invoke(controller, [idleState, config]);

        Assert.False(GetPrivateField<bool>(controller, "_requireNeutralBeforeInput"));
        Assert.True(GetPrivateField<bool>(controller, "_hasPreviousState"));
        Assert.Equal((GameInputGamepadButtons)0, GetPrivateField<GameInputGamepadButtons>(controller, "_previousProcessedButtons"));
    }

    /// <summary>
    /// XInput 的 IsConnected 應反映實際可用連線狀態，而非僅依賴上一幀快照是否已建立，
    /// 以避免裝置切換或恢復輪詢的瞬間讓 UI 誤判為未連線。
    /// </summary>
    [Fact]
    public void IsConnected_XInputController_UsesConnectionAvailabilityFlag()
    {
        using var controller = new XInputGamepadController(new StubInputContext());

        SetPrivateField(controller, "_hasPreviousState", false);
        SetPrivateField(controller, "_isConnected", true);

        Assert.True(controller.IsConnected);
    }

    /// <summary>
    /// GameInput 的 IsConnected 應與 XInput 一樣反映統一的連線可用性旗標，
    /// 避免不同後端在同一個 UI 情境下給出不一致的提示列顯示結果。
    /// </summary>
    [Fact]
    public void IsConnected_GameInputController_UsesConnectionAvailabilityFlag()
    {
        using var controller = (GameInputGamepadController)Activator.CreateInstance(
            typeof(GameInputGamepadController),
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            args: [new StubInputContext(), null],
            culture: null)!;

        SetPrivateField(controller, "_hasPreviousState", false);
        SetPrivateField(controller, "_isConnected", true);

        Assert.True(controller.IsConnected);
    }

    /// <summary>
    /// GameInput 即使目前沒有可用裝置，StopVibration 也應同步取消待執行震動並讓令牌失效。
    /// </summary>
    [Fact]
    public void StopVibration_GameInputControllerWithoutDevice_CancelsPendingRumbleState()
    {
        using var controller = (GameInputGamepadController)Activator.CreateInstance(
            typeof(GameInputGamepadController),
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            args: [new StubInputContext(), null],
            culture: null)!;

        CancellationTokenSource vibrationCts = new();
        CancellationToken token = vibrationCts.Token;

        SetPrivateField(controller, "_vibrationCts", vibrationCts);
        SetPrivateField(controller, "_vibrationToken", 10L);

        controller.StopVibration();

        Assert.Null(GetPrivateField<CancellationTokenSource?>(controller, "_vibrationCts"));
        Assert.True(token.IsCancellationRequested);
        Assert.Equal(11L, GetPrivateField<long>(controller, "_vibrationToken"));
    }

    /// <summary>
    /// GameInput 目前裝置連續讀不到 reading 時，應在重連閾值後觸發裝置重列舉，避免斷線後仍沿用上一幀狀態。
    /// </summary>
    [Fact]
    public void ShouldRefreshAfterMissingCurrentReading_WhenThresholdReached_ReturnsTrueAndResetsCounter()
    {
        using var controller = (GameInputGamepadController)Activator.CreateInstance(
            typeof(GameInputGamepadController),
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            args: [new StubInputContext(), null],
            culture: null)!;

        MethodInfo method = typeof(GameInputGamepadController).GetMethod(
            "ShouldRefreshAfterMissingCurrentReading",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("找不到 GameInputGamepadController.ShouldRefreshAfterMissingCurrentReading。");

        for (int i = 1; i < AppSettings.GamepadReconnectThresholdFrames; i++)
        {
            Assert.False((bool)method.Invoke(controller, [])!);
            Assert.Equal(i, GetPrivateField<int>(controller, "_missingReadingFrameCounter"));
        }

        Assert.True((bool)method.Invoke(controller, [])!);
        Assert.Equal(0, GetPrivateField<int>(controller, "_missingReadingFrameCounter"));
    }

    /// <summary>
    /// GameInput 裝置列舉後，只有狀態仍包含 Connected 的裝置才應被視為可用，
    /// 避免拔除瞬間仍在列舉清單中的裝置造成假重連公告。
    /// </summary>
    [Fact]
    public void IsConnectedStatus_GameInputDeviceStatus_FiltersUnavailableDevices()
    {
        MethodInfo method = typeof(GameInputGamepadController).GetMethod(
            "IsConnectedStatus",
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("找不到 GameInputGamepadController.IsConnectedStatus。");

        Assert.True((bool)method.Invoke(null, [true])!);
        Assert.False((bool)method.Invoke(null, [false])!);
    }

    /// <summary>
    /// GameInput 控制器在背景裝置探索未完成或無工作時，CheckAndApplyDiscoveryResults 不應改變狀態或丟出例外。
    /// </summary>
    [Fact]
    public void CheckAndApplyDiscoveryResults_WhenNoTask_DoesNotThrow()
    {
        using var controller = (GameInputGamepadController)Activator.CreateInstance(
            typeof(GameInputGamepadController),
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            args: [new StubInputContext(), null],
            culture: null)!;

        MethodInfo method = typeof(GameInputGamepadController).GetMethod(
            "CheckAndApplyDiscoveryResults",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("找不到 GameInputGamepadController.CheckAndApplyDiscoveryResults。");

        method.Invoke(controller, [0]);

        Assert.Null(GetPrivateField<Task<IReadOnlyList<GameInputDevice>>?>(controller, "_deviceDiscoveryTask"));
    }

    /// <summary>
    /// GameInput 控制器在 Pause 後，應保留 _needsRefresh 重新整理標記並遞增版本號，
    /// 確保原生對話框關閉或恢復輪詢時不會遺失探索需求。
    /// </summary>
    [Fact]
    public void Pause_GameInputController_PreservesNeedsRefreshAndIncrementsVersion()
    {
        using var controller = (GameInputGamepadController)Activator.CreateInstance(
            typeof(GameInputGamepadController),
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            args: [new StubInputContext(), null],
            culture: null)!;

        SetPrivateField(controller, "_needsRefresh", false);
        SetPrivateField(controller, "_refreshRequestVersion", 10L);

        CancellationTokenSource discoveryCts = new();
        SetPrivateField(controller, "_discoveryCts", discoveryCts);
        SetPrivateField(controller, "_deviceDiscoveryTask", Task.FromResult<IReadOnlyList<GameInputDevice>>([]));

        controller.Pause();

        Assert.True(GetPrivateField<bool>(controller, "_needsRefresh"));
        Assert.True(GetPrivateField<long>(controller, "_refreshRequestVersion") > 10L);
        Assert.Null(GetPrivateField<Task<IReadOnlyList<GameInputDevice>>?>(controller, "_deviceDiscoveryTask"));
        Assert.True(discoveryCts.IsCancellationRequested);
    }

    /// <summary>
    /// GameInput 控制器在背景探索工作失敗（Faulted）時，CheckAndApplyDiscoveryResults 應重新設定
    /// _needsRefresh 為 true 並遞增版本號，確保後續輪詢能重新嘗試探索。
    /// </summary>
    [Fact]
    public void CheckAndApplyDiscoveryResults_WhenTaskFaulted_RestoresNeedsRefreshAndIncrementsVersion()
    {
        using var controller = (GameInputGamepadController)Activator.CreateInstance(
            typeof(GameInputGamepadController),
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            args: [new StubInputContext(), null],
            culture: null)!;

        MethodInfo method = typeof(GameInputGamepadController).GetMethod(
            "CheckAndApplyDiscoveryResults",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("找不到 GameInputGamepadController.CheckAndApplyDiscoveryResults。");

        SetPrivateField(controller, "_needsRefresh", false);
        SetPrivateField(controller, "_refreshRequestVersion", 5L);
        SetPrivateField(
            controller,
            "_deviceDiscoveryTask",
            Task.FromException<IReadOnlyList<GameInputDevice>>(new InvalidOperationException("測試探索異常")));

        method.Invoke(controller, [0]);

        Assert.True(GetPrivateField<bool>(controller, "_needsRefresh"));
        Assert.True(GetPrivateField<long>(controller, "_refreshRequestVersion") > 5L);
        Assert.Null(GetPrivateField<Task<IReadOnlyList<GameInputDevice>>?>(controller, "_deviceDiscoveryTask"));
    }

    /// <summary>
    /// GameInput 控制器在背景探索期間若收到新的重新整理要求（版本號改變），
    /// CheckAndApplyDiscoveryResults 不應將 _needsRefresh 重設為 false，避免蓋過新插拔事件。
    /// </summary>
    [Fact]
    public void CheckAndApplyDiscoveryResults_WhenVersionChangedDuringDiscovery_PreservesNeedsRefresh()
    {
        using var controller = (GameInputGamepadController)Activator.CreateInstance(
            typeof(GameInputGamepadController),
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            args: [new StubInputContext(), null],
            culture: null)!;

        MethodInfo method = typeof(GameInputGamepadController).GetMethod(
            "CheckAndApplyDiscoveryResults",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("找不到 GameInputGamepadController.CheckAndApplyDiscoveryResults。");

        SetPrivateField(controller, "_needsRefresh", true);
        SetPrivateField(controller, "_activeDiscoveryVersion", 1L);
        SetPrivateField(controller, "_refreshRequestVersion", 2L);
        SetPrivateField(controller, "_deviceDiscoveryTask", Task.FromResult<IReadOnlyList<GameInputDevice>>([]));

        method.Invoke(controller, [0]);

        Assert.True(GetPrivateField<bool>(controller, "_needsRefresh"));
        Assert.Null(GetPrivateField<Task<IReadOnlyList<GameInputDevice>>?>(controller, "_deviceDiscoveryTask"));
    }

    /// <summary>
    /// GameInput 控制器在背景探索期間若無新的重新整理要求（版本號相同），
    /// CheckAndApplyDiscoveryResults 應正常將 _needsRefresh 重設為 false。
    /// </summary>
    [Fact]
    public void CheckAndApplyDiscoveryResults_WhenVersionUnchanged_ClearsNeedsRefresh()
    {
        using var controller = (GameInputGamepadController)Activator.CreateInstance(
            typeof(GameInputGamepadController),
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            args: [new StubInputContext(), null],
            culture: null)!;

        MethodInfo method = typeof(GameInputGamepadController).GetMethod(
            "CheckAndApplyDiscoveryResults",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("找不到 GameInputGamepadController.CheckAndApplyDiscoveryResults。");

        SetPrivateField(controller, "_needsRefresh", true);
        SetPrivateField(controller, "_activeDiscoveryVersion", 3L);
        SetPrivateField(controller, "_refreshRequestVersion", 3L);
        SetPrivateField(controller, "_deviceDiscoveryTask", Task.FromResult<IReadOnlyList<GameInputDevice>>([]));

        method.Invoke(controller, [0]);

        Assert.False(GetPrivateField<bool>(controller, "_needsRefresh"));
        Assert.Null(GetPrivateField<Task<IReadOnlyList<GameInputDevice>>?>(controller, "_deviceDiscoveryTask"));
    }

    /// <summary>
    /// GameInput 控制器在過期輪詢世代執行時，CheckAndApplyDiscoveryResults 應拒絕取走工作，
    /// 確保新世代進行中的探索工作不被過期呼叫者清空或誤釋放。
    /// </summary>
    [Fact]
    public void CheckAndApplyDiscoveryResults_WhenStaleGeneration_RetainsDiscoveryTaskForCurrentGeneration()
    {
        using var controller = (GameInputGamepadController)Activator.CreateInstance(
            typeof(GameInputGamepadController),
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            args: [new StubInputContext(), null],
            culture: null)!;

        MethodInfo method = typeof(GameInputGamepadController).GetMethod(
            "CheckAndApplyDiscoveryResults",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("找不到 GameInputGamepadController.CheckAndApplyDiscoveryResults。");

        Task<IReadOnlyList<GameInputDevice>> currentGenTask = Task.FromResult<IReadOnlyList<GameInputDevice>>([]);

        SetPrivateField(controller, "_pollingGeneration", 5);
        SetPrivateField(controller, "_deviceDiscoveryTask", currentGenTask);

        // 傳入過期世代 4
        method.Invoke(controller, [4]);

        // 驗證過期呼叫者被拒絕，新世代的工作未被清空
        Assert.Same(currentGenTask, GetPrivateField<Task<IReadOnlyList<GameInputDevice>>?>(controller, "_deviceDiscoveryTask"));
    }

    /// <summary>
    /// StopPolling 應追蹤移交的探索清理工作，確保 DisposeResourcesAsync 完成前等待所有清理結束。
    /// </summary>
    [Fact]
    public async Task StopPolling_TracksPendingDiscoveryCleanup_WaitedBeforeDisposeResourcesCompletes()
    {
        using var controller = (GameInputGamepadController)Activator.CreateInstance(
            typeof(GameInputGamepadController),
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            args: [new StubInputContext(), null],
            culture: null)!;

        TaskCompletionSource<IReadOnlyList<GameInputDevice>> discoveryTcs = new();

        SetPrivateField(controller, "_deviceDiscoveryTask", discoveryTcs.Task);

        MethodInfo stopPollingMethod = typeof(GameInputGamepadController).GetMethod(
            "StopPolling",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("找不到 GameInputGamepadController.StopPolling。");

        MethodInfo disposeResourcesMethod = typeof(GameInputGamepadController).GetMethod(
            "DisposeResourcesAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("找不到 GameInputGamepadController.DisposeResourcesAsync。");

        // 呼叫 StopPolling，進行中工作應被移交並加入清理追蹤清單
        stopPollingMethod.Invoke(controller, []);

        Assert.Null(GetPrivateField<Task<IReadOnlyList<GameInputDevice>>?>(controller, "_deviceDiscoveryTask"));
        List<Task> pendingCleanups = GetPrivateField<List<Task>>(controller, "_pendingDiscoveryCleanups");
        Task cleanupTask = Assert.Single(pendingCleanups);
        Assert.NotNull(cleanupTask);

        // 呼叫 DisposeResourcesAsync，在 discoveryTcs 未完成前，Dispose 任務不應結束
        Task disposeTask = (Task)disposeResourcesMethod.Invoke(controller, [])!;
        Assert.False(disposeTask.IsCompleted);

        // 完成探索工作
        discoveryTcs.SetResult([]);

        // 等待 Dispose 完成
        await disposeTask;

        Assert.True(disposeTask.IsCompletedSuccessfully);
        Assert.Empty(GetPrivateField<List<Task>>(controller, "_pendingDiscoveryCleanups"));
    }

    /// <summary>
    /// 若舊輪詢工作傳入過期世代，PollingLoopAsync 應立即放棄並取消 initTcs，且不得竄改目前最新世代編號。
    /// </summary>
    [Fact]
    public async Task PollingLoopAsync_WhenStaleGeneration_AbortsWithoutMutatingPollingGeneration()
    {
        using var controller = (GameInputGamepadController)Activator.CreateInstance(
            typeof(GameInputGamepadController),
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            args: [new StubInputContext(), null],
            culture: null)!;

        MethodInfo method = typeof(GameInputGamepadController).GetMethod(
            "PollingLoopAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("找不到 GameInputGamepadController.PollingLoopAsync。");

        SetPrivateField(controller, "_pollingGeneration", 3);

        using CancellationTokenSource cts = new();
        TaskCompletionSource tcs = new();

        // 傳入過期世代 2
        Task task = (Task)method.Invoke(controller, [cts.Token, 2, tcs])!;
        await task;

        Assert.Equal(3, GetPrivateField<int>(controller, "_pollingGeneration"));
        Assert.True(tcs.Task.IsCanceled);
    }

    /// <summary>
    /// 在已暫停或已處置的狀態下，ApplyDiscoveredDevicesWithLifecycle 應拒絕寫入裝置快取，並標記需要重新整理。
    /// </summary>
    [Fact]
    public void ApplyDiscoveredDevicesWithLifecycle_WhenPaused_DoesNotPolluteDeviceCache()
    {
        using var controller = (GameInputGamepadController)Activator.CreateInstance(
            typeof(GameInputGamepadController),
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            args: [new StubInputContext(), null],
            culture: null)!;

        MethodInfo method = typeof(GameInputGamepadController).GetMethod(
            "ApplyDiscoveredDevicesWithLifecycle",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("找不到 GameInputGamepadController.ApplyDiscoveredDevicesWithLifecycle。");

        SetPrivateField(controller, "_isPaused", true);
        SetPrivateField(controller, "_needsRefresh", false);
        SetPrivateField(controller, "_pollingGeneration", 1);

        method.Invoke(controller, [Array.Empty<GameInputDevice>(), 1L, 1]);

        Assert.True(GetPrivateField<bool>(controller, "_needsRefresh"));
        Assert.Empty(GetPrivateField<List<GameInputDevice>>(controller, "_allDevices"));
        Assert.Null(GetPrivateField<GameInputDevice?>(controller, "_device"));
    }

    /// <summary>
    /// 若在探索完成結算時有新的重新整理請求發布（版本號已推進），AcknowledgeRefresh 不得清空重新整理標記。
    /// </summary>
    [Fact]
    public void AcknowledgeRefresh_WhenNewVersionPublishedConcurrently_RetainsNeedsDeviceRefresh()
    {
        using var controller = (GameInputGamepadController)Activator.CreateInstance(
            typeof(GameInputGamepadController),
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            args: [new StubInputContext(), null],
            culture: null)!;

        MethodInfo reqMethod = typeof(GameInputGamepadController).GetMethod(
            "RequestRefresh",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("找不到 GameInputGamepadController.RequestRefresh。");

        MethodInfo ackMethod = typeof(GameInputGamepadController).GetMethod(
            "AcknowledgeRefresh",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("找不到 GameInputGamepadController.AcknowledgeRefresh。");

        PropertyInfo needsProp = typeof(GameInputGamepadController).GetProperty(
            "NeedsDeviceRefresh",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("找不到 GameInputGamepadController.NeedsDeviceRefresh。");

        // 第一次請求，版本號變為 1
        reqMethod.Invoke(controller, []);
        long capturedVersion = GetPrivateField<long>(controller, "_refreshRequestVersion");
        Assert.Equal(1L, capturedVersion);

        // 模擬探索期間又有新設備熱插拔，發布第二個請求，版本號變為 2
        reqMethod.Invoke(controller, []);
        Assert.Equal(2L, GetPrivateField<long>(controller, "_refreshRequestVersion"));

        // 探索結算第一版 (1)
        ackMethod.Invoke(controller, [capturedVersion]);

        // 因為版本 2 尚未處理，重新整理標記必須維持 true
        Assert.True((bool)needsProp.GetValue(controller)!);
        Assert.True(GetPrivateField<bool>(controller, "_needsRefresh"));
    }

    /// <summary>
    /// GameInput 控制器在套用空裝置清單時，應將目前裝置設為 null 並觸發斷線流程。
    /// </summary>
    [Fact]
    public void ApplyDiscoveredDevices_EmptyList_ClearsDeviceAndNotifiesDisconnect()
    {
        using var controller = (GameInputGamepadController)Activator.CreateInstance(
            typeof(GameInputGamepadController),
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            args: [new StubInputContext(), null],
            culture: null)!;

        bool? connectionEventStatus = null;
        controller.ConnectionChanged += status => connectionEventStatus = status;

        SetPrivateField(controller, "_isConnected", true);

        MethodInfo method = typeof(GameInputGamepadController).GetMethod(
            "ApplyDiscoveredDevices",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("找不到 GameInputGamepadController.ApplyDiscoveredDevices。");

        method.Invoke(controller, [Array.Empty<GameInputDevice>()]);

        Assert.Null(GetPrivateField<GameInputDevice?>(controller, "_device"));
        Assert.False(controller.IsConnected);
        Assert.Equal(false, connectionEventStatus);
    }

    /// <summary>
    /// XInput 控制器在 ClearAllEvents 後，必須一併清除 LeftShoulderReleased 與
    /// RightShoulderReleased 兩個事件訂閱，避免處置後仍殘留呼叫鏈造成事件洩漏。
    /// </summary>
    [Fact]
    public void ClearAllEvents_XInputController_ClearsShoulderReleasedSubscriptions()
    {
        using var controller = new XInputGamepadController(new StubInputContext());

        controller.LeftShoulderReleased += static () => { };
        controller.RightShoulderReleased += static () => { };

        Assert.NotNull(GetEventDelegate(controller, "LeftShoulderReleased"));
        Assert.NotNull(GetEventDelegate(controller, "RightShoulderReleased"));

        InvokeClearAllEvents(controller);

        Assert.Null(GetEventDelegate(controller, "LeftShoulderReleased"));
        Assert.Null(GetEventDelegate(controller, "RightShoulderReleased"));
    }

    /// <summary>
    /// GameInput 控制器在 ClearAllEvents 後，必須一併清除 LeftShoulderReleased 與
    /// RightShoulderReleased 兩個事件訂閱，與 XInput 行為對齊，避免不同後端產生
    /// 事件殘留差異。
    /// </summary>
    [Fact]
    public void ClearAllEvents_GameInputController_ClearsShoulderReleasedSubscriptions()
    {
        using var controller = (GameInputGamepadController)Activator.CreateInstance(
            typeof(GameInputGamepadController),
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            args: [new StubInputContext(), null],
            culture: null)!;

        controller.LeftShoulderReleased += static () => { };
        controller.RightShoulderReleased += static () => { };

        Assert.NotNull(GetEventDelegate(controller, "LeftShoulderReleased"));
        Assert.NotNull(GetEventDelegate(controller, "RightShoulderReleased"));

        InvokeClearAllEvents(controller);

        Assert.Null(GetEventDelegate(controller, "LeftShoulderReleased"));
        Assert.Null(GetEventDelegate(controller, "RightShoulderReleased"));
    }

    /// <summary>
    /// 連續丟失讀取達到重新整理閾值時，HandleMissingCurrentReading 應啟動重新整理並回傳 true，
    /// 讓輪詢迴圈中斷當前幀的過期狀態評估。
    /// </summary>
    [Fact]
    public void HandleMissingCurrentReading_WhenThresholdReached_ReturnsTrueAndRequestsRefresh()
    {
        using var controller = (GameInputGamepadController)Activator.CreateInstance(
            typeof(GameInputGamepadController),
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            args: [new StubInputContext(), null],
            culture: null)!;

        MethodInfo method = typeof(GameInputGamepadController).GetMethod(
            "HandleMissingCurrentReading",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("找不到 GameInputGamepadController.HandleMissingCurrentReading。");

        SetPrivateField(controller, "_missingReadingFrameCounter", AppSettings.GamepadReconnectThresholdFrames - 1);
        SetPrivateField(controller, "_needsRefresh", false);

        bool result = (bool)method.Invoke(controller, [0])!;

        Assert.True(result);
        Assert.True(GetPrivateField<bool>(controller, "_needsRefresh"));
        Assert.Equal(0, GetPrivateField<int>(controller, "_missingReadingFrameCounter"));
    }

    /// <summary>
    /// 連續丟失讀取未達到重新整理閾值時，HandleMissingCurrentReading 應回傳 false 且不觸發探索。
    /// </summary>
    [Fact]
    public void HandleMissingCurrentReading_BeforeThresholdReached_ReturnsFalse()
    {
        using var controller = (GameInputGamepadController)Activator.CreateInstance(
            typeof(GameInputGamepadController),
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            args: [new StubInputContext(), null],
            culture: null)!;

        MethodInfo method = typeof(GameInputGamepadController).GetMethod(
            "HandleMissingCurrentReading",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("找不到 GameInputGamepadController.HandleMissingCurrentReading。");

        SetPrivateField(controller, "_missingReadingFrameCounter", 0);
        SetPrivateField(controller, "_needsRefresh", false);

        bool result = (bool)method.Invoke(controller, [0])!;

        Assert.False(result);
        Assert.False(GetPrivateField<bool>(controller, "_needsRefresh"));
        Assert.Equal(1, GetPrivateField<int>(controller, "_missingReadingFrameCounter"));
    }

    /// <summary>
    /// DisposeResourcesAsync 在等待進行中的背景探索工作前，應確實取消 _discoveryCts，防止處置延遲等待。
    /// </summary>
    [Fact]
    public async Task DisposeResourcesAsync_WhenDiscoveryTaskInFlight_CancelsDiscoveryCts()
    {
        using var controller = (GameInputGamepadController)Activator.CreateInstance(
            typeof(GameInputGamepadController),
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            args: [new StubInputContext(), null],
            culture: null)!;

        CancellationTokenSource discoveryCts = new();
        TaskCompletionSource<IReadOnlyList<GameInputDevice>> discoveryTcs = new();

        SetPrivateField(controller, "_discoveryCts", discoveryCts);
        SetPrivateField(controller, "_deviceDiscoveryTask", discoveryTcs.Task);

        MethodInfo disposeResourcesMethod = typeof(GameInputGamepadController).GetMethod(
            "DisposeResourcesAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("找不到 GameInputGamepadController.DisposeResourcesAsync。");

        Task disposeTask = (Task)disposeResourcesMethod.Invoke(controller, [])!;

        // 驗證 _discoveryCts 已在 lock (_discoveryLock) 內被取消
        Assert.True(discoveryCts.IsCancellationRequested);

        // 完成探索工作以允許 dispose 完成
        discoveryTcs.SetResult([]);
        await disposeTask;

        Assert.True(disposeTask.IsCompletedSuccessfully);
    }

    /// <summary>
    /// DisposeDevices 傳入 null 參數時應安全返回，杜絕 NullReferenceException。
    /// </summary>
    [Fact]
    public void DisposeDevices_WhenNull_DoesNotThrow()
    {
        MethodInfo method = typeof(GameInputGamepadController).GetMethod(
            "DisposeDevices",
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("找不到 GameInputGamepadController.DisposeDevices。");

        Exception? ex = Record.Exception(() => method.Invoke(null, [null]));
        Assert.Null(ex);
    }

    /// <summary>
    /// 透過反射呼叫控制器的 private ClearAllEvents 方法。
    /// </summary>
    /// <param name="controller">目標控制器實例。</param>
    private static void InvokeClearAllEvents(object controller)
    {
        MethodInfo method = controller.GetType().GetMethod(
            "ClearAllEvents",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("找不到 ClearAllEvents 方法。");

        method.Invoke(controller, []);
    }

    /// <summary>
    /// 透過反射讀取 field-like event 的 backing delegate，用於驗證事件訂閱是否已清除。
    /// </summary>
    /// <param name="target">事件所屬物件。</param>
    /// <param name="eventName">事件名稱（與 backing field 同名）。</param>
    /// <returns>backing delegate；若為 null 表示無訂閱。</returns>
    private static Delegate? GetEventDelegate(object target, string eventName)
    {
        FieldInfo field = target.GetType().GetField(eventName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"找不到事件 backing field：{eventName}");

        return field.GetValue(target) as Delegate;
    }

    /// <summary>
    /// 建立 GameInput 狀態快照，供反射測試用。
    /// </summary>
    /// <param name="state">原始 GameInput 狀態。</param>
    /// <returns>包裝後的快照物件。</returns>
    private static GamepadReadingSnapshot CreateGameInputSnapshot(GameInputGamepadState state)
    {
        ConstructorInfo ctor = typeof(GamepadReadingSnapshot).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            types: [typeof(ulong), typeof(GameInputGamepadState)],
            modifiers: null)
            ?? throw new InvalidOperationException("找不到 GamepadReadingSnapshot 內部建構子。");

        return (GamepadReadingSnapshot)ctor.Invoke([0ul, state]);
    }

    /// <summary>
    /// 透過反射寫入私有欄位，模擬控制器在暫停前已存在的執行期狀態。
    /// </summary>
    /// <typeparam name="T">欄位值型別。</typeparam>
    /// <param name="target">要修改欄位的目標物件。</param>
    /// <param name="name">私有欄位名稱。</param>
    /// <param name="value">要寫入的欄位值。</param>
    private static void SetPrivateField<T>(object target, string name, T value)
    {
        FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"找不到欄位：{name}");

        field.SetValue(target, value);
    }

    /// <summary>
    /// 透過反射讀取私有欄位，驗證 Pause 後暫態狀態是否已正確清除。
    /// </summary>
    /// <typeparam name="T">欄位值型別。</typeparam>
    /// <param name="target">要讀取欄位的目標物件。</param>
    /// <param name="name">私有欄位名稱。</param>
    /// <returns>欄位目前的值；若為空值則回傳對應型別的預設值。</returns>
    private static T GetPrivateField<T>(object target, string name)
    {
        FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"找不到欄位：{name}");

        object? value = field.GetValue(target);

        if (value is null)
        {
            return default!;
        }

        return (T)value;
    }
}