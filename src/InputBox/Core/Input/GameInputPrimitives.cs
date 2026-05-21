using System.Runtime.InteropServices;

namespace InputBox.Core.Input;

/// <summary>
/// GameInput 輸入類型。
/// </summary>
internal enum GameInputKind
{
    /// <summary>
    /// Gamepad。
    /// </summary>
    Gamepad = 0x00040000
}

/// <summary>
/// GameInput focus policy。
/// </summary>
[Flags]
internal enum GameInputFocusPolicy : uint
{
    /// <summary>
    /// 預設 focus policy。
    /// </summary>
    Default = 0
}

/// <summary>
/// GameInput 裝置狀態。
/// </summary>
[Flags]
internal enum GameInputDeviceStatus : uint
{
    /// <summary>
    /// 已連線。
    /// </summary>
    Connected = 0x00000001
}

/// <summary>
/// GameInput 裝置列舉模式。
/// </summary>
internal enum GameInputEnumerationKind
{
    /// <summary>
    /// 不執行初始列舉。
    /// </summary>
    None = 0
}

/// <summary>
/// GameInput gamepad 按鍵旗標。
/// </summary>
[Flags]
internal enum GameInputGamepadButtons : uint
{
    None = 0x00000000,
    Menu = 0x00000001,
    View = 0x00000002,
    A = 0x00000004,
    B = 0x00000008,
    X = 0x00000010,
    Y = 0x00000020,
    DPadUp = 0x00000040,
    DPadDown = 0x00000080,
    DPadLeft = 0x00000100,
    DPadRight = 0x00000200,
    LeftShoulder = 0x00000400,
    RightShoulder = 0x00000800,
    LeftThumbstick = 0x00001000,
    RightThumbstick = 0x00002000,
    LeftTriggerButton = 0x00010000,
    RightTriggerButton = 0x00020000
}

/// <summary>
/// GameInput 支援的震動馬達旗標。
/// </summary>
[Flags]
internal enum GameInputRumbleMotors : uint
{
    None = 0x00000000,
    LowFrequency = 0x00000001,
    HighFrequency = 0x00000002,
    LeftTrigger = 0x00000004,
    RightTrigger = 0x00000008
}

/// <summary>
/// GameInput gamepad 狀態。
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct GameInputGamepadState
{
    public GameInputGamepadButtons Buttons;
    public float LeftTrigger;
    public float RightTrigger;
    public float LeftThumbstickX;
    public float LeftThumbstickY;
    public float RightThumbstickX;
    public float RightThumbstickY;
}

/// <summary>
/// GameInput gamepad 狀態快照。
/// </summary>
internal sealed record class GamepadStateSnapshot
{
    internal GamepadStateSnapshot(GameInputGamepadState state)
        : this(
            state.Buttons,
            state.LeftTrigger,
            state.RightTrigger,
            state.LeftThumbstickX,
            state.LeftThumbstickY,
            state.RightThumbstickX,
            state.RightThumbstickY)
    {

    }

    internal GamepadStateSnapshot(
        GameInputGamepadButtons buttons,
        float leftTrigger,
        float rightTrigger,
        float leftThumbstickX,
        float leftThumbstickY,
        float rightThumbstickX,
        float rightThumbstickY)
    {
        Buttons = buttons;
        LeftTrigger = leftTrigger;
        RightTrigger = rightTrigger;
        LeftThumbstickX = leftThumbstickX;
        LeftThumbstickY = leftThumbstickY;
        RightThumbstickX = rightThumbstickX;
        RightThumbstickY = rightThumbstickY;
    }

    public GameInputGamepadButtons Buttons { get; init; }

    public float LeftTrigger { get; init; }

    public float RightTrigger { get; init; }

    public float LeftThumbstickX { get; init; }

    public float LeftThumbstickY { get; init; }

    public float RightThumbstickX { get; init; }

    public float RightThumbstickY { get; init; }
}

/// <summary>
/// GameInput 震動參數。
/// </summary>
internal readonly record struct GameInputRumbleParams
{
    public float LowFrequency { get; init; }

    public float HighFrequency { get; init; }

    public float LeftTrigger { get; init; }

    public float RightTrigger { get; init; }
}

/// <summary>
/// GameInput 裝置資訊快照。
/// </summary>
internal readonly record struct GameInputDeviceInfo
{
    internal GameInputDeviceInfo(
        string deviceId,
        ushort vendorId,
        ushort productId,
        GameInputRumbleMotors supportedRumbleMotors,
        string displayName)
    {
        DeviceId = deviceId;
        VendorId = vendorId;
        ProductId = productId;
        SupportedRumbleMotors = supportedRumbleMotors;
        DisplayName = displayName;
    }

    public string DeviceId { get; }

    public ushort VendorId { get; }

    public ushort ProductId { get; }

    public GameInputRumbleMotors SupportedRumbleMotors { get; }

    private string DisplayName { get; }

    public string GetDisplayName() => DisplayName;
}

/// <summary>
/// Native shim 回傳的裝置資訊。
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct GameInputNativeDeviceInfo
{
    public ushort VendorId;
    public ushort ProductId;
    public uint SupportedRumbleMotors;
    public fixed byte DeviceId[65];
    public fixed byte DisplayName[256];

    public GameInputDeviceInfo ToDeviceInfo()
    {
        string parsedDeviceId;
        string parsedDisplayName;

        fixed (byte* deviceIdPtr = DeviceId)
        fixed (byte* displayNamePtr = DisplayName)
        {
            parsedDeviceId = Marshal.PtrToStringUTF8((nint)deviceIdPtr) ?? string.Empty;
            parsedDisplayName = Marshal.PtrToStringUTF8((nint)displayNamePtr) ?? string.Empty;
        }

        return new GameInputDeviceInfo(
            parsedDeviceId,
            VendorId,
            ProductId,
            (GameInputRumbleMotors)SupportedRumbleMotors,
            parsedDisplayName);
    }
}