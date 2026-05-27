using InputBox.Core.Input;
using InputWeave.GameInput;
using InputWeave.GameInput.Interop;
using System.Reflection;
using System.Runtime.InteropServices;
using Xunit;

namespace InputBox.Tests;

/// <summary>
/// 驗證 InputBox 直接使用 InputWeave.GameInput gamepad API 時，仍保留既有 GameInput 行為守門。
/// </summary>
public sealed class GameInputDirectUsageTests
{
    /// <summary>
    /// InputBox 依賴的 InputWeave gamepad client / device / callback / runtime API surface 必須存在。
    /// </summary>
    [Fact]
    public void RequiredInputWeaveGamepadApi_Surface_IsAvailable()
    {
        Assert.NotNull(typeof(GameInputRuntime).GetMethod(nameof(GameInputRuntime.TryProbe), BindingFlags.Public | BindingFlags.Static));
        Assert.NotNull(typeof(GameInputRuntime).GetMethod(nameof(GameInputRuntime.GetInfo), BindingFlags.Public | BindingFlags.Static));
        Assert.NotNull(typeof(GameInputClient).GetMethod(nameof(GameInputClient.Create), BindingFlags.Public | BindingFlags.Static));
        Assert.NotNull(typeof(GameInputClient).GetMethod(nameof(GameInputClient.SetFocusPolicy), [typeof(GameInputFocusPolicy)]));
        Assert.NotNull(typeof(GameInputClient).GetMethod(nameof(GameInputClient.EnumerateDevices), [typeof(GameInputKind), typeof(GameInputDeviceStatus)]));
        Assert.NotNull(typeof(GameInputClient).GetMethod(nameof(GameInputClient.GetCurrentGamepad), [typeof(GameInputDevice)]));
        Assert.NotNull(typeof(GameInputClient).GetMethod(nameof(GameInputClient.RegisterReadingCallback), [typeof(GameInputDevice), typeof(GameInputKind), typeof(GameInputReadingHandler)]));
        Assert.NotNull(typeof(GameInputClient).GetMethod(nameof(GameInputClient.RegisterDeviceCallback), [typeof(GameInputDevice), typeof(GameInputKind), typeof(GameInputDeviceStatus), typeof(GameInputEnumerationKind), typeof(GameInputDeviceHandler)]));
        Assert.NotNull(typeof(GameInputDevice).GetProperty(nameof(GameInputDevice.Status)));
        Assert.NotNull(typeof(GameInputDevice).GetMethod(nameof(GameInputDevice.GetDeviceInfoSnapshot), Type.EmptyTypes));
        Assert.NotNull(typeof(GameInputDevice).GetMethod(nameof(GameInputDevice.SetRumbleState), [typeof(GameInputRumbleParams).MakeByRefType()]));
    }

    /// <summary>
    /// InputWeave 的 GameInput callback 參數必須以 unmanaged function pointer 傳遞，避免退回 _Delegate COM marshaling。
    /// </summary>
    [Fact]
    public void GameInputCallbackParameters_AreFunctionPointers()
    {
        AssertCallbackParameterIsFunctionPointer(nameof(IGameInput.RegisterReadingCallback), typeof(GameInputReadingCallback));
        AssertCallbackParameterIsFunctionPointer(nameof(IGameInput.RegisterDeviceCallback), typeof(GameInputDeviceCallback));
        AssertCallbackParameterIsFunctionPointer(nameof(IGameInput.RegisterSystemButtonCallback), typeof(GameInputSystemButtonCallback));
        AssertCallbackParameterIsFunctionPointer(nameof(IGameInput.RegisterKeyboardLayoutCallback), typeof(GameInputKeyboardLayoutCallback));
    }

    /// <summary>
    /// GameInput v3 的 gamepad button 位元必須維持 Microsoft 定義值，避免 face key、D-Pad、背鍵與搖桿方向判斷錯位。
    /// </summary>
    [Fact]
    public void GameInputGamepadButtons_OfficialV3Values_AreStable()
    {
        Assert.Equal(0x00000004u, Convert.ToUInt32(GameInputGamepadButtons.GameInputGamepadA));
        Assert.Equal(0x00000008u, Convert.ToUInt32(GameInputGamepadButtons.GameInputGamepadB));
        Assert.Equal(0x00004000u, Convert.ToUInt32(GameInputGamepadButtons.GameInputGamepadC));
        Assert.Equal(0x00008000u, Convert.ToUInt32(GameInputGamepadButtons.GameInputGamepadZ));
        Assert.Equal(0x00000200u, Convert.ToUInt32(GameInputGamepadButtons.GameInputGamepadDPadRight));
        Assert.Equal(0x00040000u, Convert.ToUInt32(GameInputGamepadButtons.GameInputGamepadLeftThumbstickUp));
        Assert.Equal(0x02000000u, Convert.ToUInt32(GameInputGamepadButtons.GameInputGamepadRightThumbstickRight));
        Assert.Equal(0x04000000u, Convert.ToUInt32(GameInputGamepadButtons.GameInputGamepadPaddleLeft1));
        Assert.Equal(0x20000000u, Convert.ToUInt32(GameInputGamepadButtons.GameInputGamepadPaddleRight2));
    }

    /// <summary>
    /// InputWeave 的 GamepadReadingSnapshot 應直接保存 timestamp 與 gamepad state，供 InputBox polling 與診斷使用。
    /// </summary>
    [Fact]
    public void GamepadReadingSnapshot_InputWeaveState_PreservesTimestampAndValues()
    {
        GameInputGamepadState state = new()
        {
            Buttons = GameInputGamepadButtons.GameInputGamepadA | GameInputGamepadButtons.GameInputGamepadPaddleLeft1,
            LeftTrigger = 0.1f,
            RightTrigger = 0.2f,
            LeftThumbstickX = -0.3f,
            LeftThumbstickY = 0.4f,
            RightThumbstickX = -0.5f,
            RightThumbstickY = 0.6f
        };

        GamepadReadingSnapshot snapshot = new(987654321, state);

        Assert.Equal(987654321ul, snapshot.Timestamp);
        Assert.Equal(state.Buttons, snapshot.State.Buttons);
        Assert.Equal(state.LeftTrigger, snapshot.State.LeftTrigger);
        Assert.Equal(state.RightTrigger, snapshot.State.RightTrigger);
        Assert.Equal(state.RightThumbstickY, snapshot.State.RightThumbstickY);
    }

    /// <summary>
    /// GameInput edge detection 應忽略 timestamp 差異，避免硬體持續回報相同狀態時被誤判為新按鍵。
    /// </summary>
    [Fact]
    public void HasSameInputValues_TimestampOnlyChanged_ReturnsTrue()
    {
        MethodInfo method = typeof(GameInputGamepadController).GetMethod(
            "HasSameInputValues",
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("找不到 GameInputGamepadController.HasSameInputValues。");
        GameInputGamepadState state = new()
        {
            Buttons = GameInputGamepadButtons.GameInputGamepadA,
            LeftTrigger = 0.1f,
            RightTrigger = 0.2f,
            LeftThumbstickX = -0.3f,
            LeftThumbstickY = 0.4f,
            RightThumbstickX = -0.5f,
            RightThumbstickY = 0.6f
        };
        GamepadReadingSnapshot first = new(1, state);
        GamepadReadingSnapshot second = new(2, state);

        Assert.True((bool)method.Invoke(null, [second, first])!);
    }

    /// <summary>
    /// GameInput edge detection 仍必須比較實際 gamepad 值，避免按鍵或軸變化被 timestamp 邏輯吞掉。
    /// </summary>
    [Fact]
    public void HasSameInputValues_ButtonChanged_ReturnsFalse()
    {
        MethodInfo method = typeof(GameInputGamepadController).GetMethod(
            "HasSameInputValues",
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("找不到 GameInputGamepadController.HasSameInputValues。");
        GamepadReadingSnapshot previous = new(1, new GameInputGamepadState
        {
            Buttons = GameInputGamepadButtons.GameInputGamepadA
        });
        GamepadReadingSnapshot current = new(2, new GameInputGamepadState
        {
            Buttons = GameInputGamepadButtons.GameInputGamepadB
        });

        Assert.False((bool)method.Invoke(null, [current, previous])!);
    }

    /// <summary>
    /// InputBox 的穩定裝置識別 helper 應優先使用 PnP path，缺失時退回 VID/PID 與顯示名稱。
    /// </summary>
    [Fact]
    public void GetStableDeviceId_InputWeaveSnapshot_UsesPnpPathOrVidPidFallback()
    {
        MethodInfo method = typeof(GameInputGamepadController).GetMethod(
            "GetStableDeviceId",
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("找不到 GameInputGamepadController.GetStableDeviceId。");
        GameInputDeviceInfoSnapshot withPnpPath = CreateDeviceInfoSnapshot(
            0x054C,
            0x0CE6,
            "DualSense Wireless Controller",
            @"HID\VID_054C&PID_0CE6");
        GameInputDeviceInfoSnapshot withoutPnpPath = CreateDeviceInfoSnapshot(
            0x057E,
            0x2009,
            "Pro Controller",
            string.Empty);

        Assert.Equal(@"HID\VID_054C&PID_0CE6", method.Invoke(null, [withPnpPath]));
        Assert.Equal("VID_057E PID_2009 Pro Controller", method.Invoke(null, [withoutPnpPath]));
    }

    /// <summary>
    /// InputWeave rumble 參數應保留四個馬達欄位，供 InputBox 的震動安全限制器輸出到左右主馬達與雙扳機馬達。
    /// </summary>
    [Fact]
    public void GameInputRumbleParams_MotorFields_PreserveAssignedValues()
    {
        GameInputRumbleParams rumble = new()
        {
            LowFrequency = 0.1f,
            HighFrequency = 0.2f,
            LeftTrigger = 0.3f,
            RightTrigger = 0.4f
        };

        Assert.Equal(0.1f, rumble.LowFrequency);
        Assert.Equal(0.2f, rumble.HighFrequency);
        Assert.Equal(0.3f, rumble.LeftTrigger);
        Assert.Equal(0.4f, rumble.RightTrigger);
    }

    private static void AssertCallbackParameterIsFunctionPointer(string methodName, Type callbackType)
    {
        MethodInfo method = typeof(IGameInput).GetMethod(methodName)
            ?? throw new InvalidOperationException($"找不到 IGameInput.{methodName}。");
        ParameterInfo callbackParameter = method.GetParameters().Single(parameter => parameter.ParameterType == callbackType);
        MarshalAsAttribute? marshalAs = callbackParameter.GetCustomAttribute<MarshalAsAttribute>();

        Assert.NotNull(marshalAs);
        Assert.Equal(UnmanagedType.FunctionPtr, marshalAs.Value);
    }

    private static GameInputDeviceInfoSnapshot CreateDeviceInfoSnapshot(
        ushort vendorId,
        ushort productId,
        string displayName,
        string pnpPath)
    {
        GameInputDeviceInfo nativeInfo = new()
        {
            VendorId = vendorId,
            ProductId = productId,
            SupportedInput = GameInputKind.GameInputKindGamepad
        };

        return CreateInstance<GameInputDeviceInfoSnapshot>(
            nativeInfo,
            displayName,
            pnpPath,
            null,
            null,
            null,
            null,
            null,
            null,
            new GameInputGamepadInfo(),
            null,
            Array.Empty<GameInputForceFeedbackMotorInfo>(),
            Array.Empty<GameInputRawDeviceReportInfo>(),
            Array.Empty<GameInputRawDeviceReportInfo>());
    }

    private static T CreateInstance<T>(params object?[] args)
        => (T)(Activator.CreateInstance(
            typeof(T),
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: args,
            culture: null) ?? throw new InvalidOperationException($"無法建立 {typeof(T).FullName}。"));
}
