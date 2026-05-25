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

        Assert.True((bool)method.Invoke(null, [GameInputDeviceStatus.GameInputDeviceConnected])!);
        Assert.False((bool)method.Invoke(null, [(GameInputDeviceStatus)0])!);
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
        return new GamepadReadingSnapshot(0, state);
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
