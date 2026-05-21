using System.Runtime.InteropServices;

namespace InputBox.Core.Input;

/// <summary>
/// GameInput 輸入類型。
/// </summary>
[Flags]
internal enum GameInputKind
{
    /// <summary>
    /// 未知或不支援的輸入類型。
    /// </summary>
    Unknown = 0x00000000,

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
    /// 無狀態。
    /// </summary>
    None = 0x00000000,

    /// <summary>
    /// 已連線。
    /// </summary>
    Connected = 0x00000001,

    /// <summary>
    /// Haptic 資訊已可用。
    /// </summary>
    HapticInfoReady = 0x00200000,

    /// <summary>
    /// 任一狀態變更。
    /// </summary>
    Any = 0xFFFFFFFF
}

/// <summary>
/// GameInput 裝置列舉模式。
/// </summary>
internal enum GameInputEnumerationKind
{
    /// <summary>
    /// 不執行初始列舉。
    /// </summary>
    None = 0,

    /// <summary>
    /// 非同步列舉。
    /// </summary>
    Async = 1,

    /// <summary>
    /// 阻塞列舉。
    /// </summary>
    Blocking = 2
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
    C = 0x00004000,
    X = 0x00000010,
    Y = 0x00000020,
    Z = 0x00008000,
    DPadUp = 0x00000040,
    DPadDown = 0x00000080,
    DPadLeft = 0x00000100,
    DPadRight = 0x00000200,
    LeftShoulder = 0x00000400,
    RightShoulder = 0x00000800,
    LeftThumbstick = 0x00001000,
    RightThumbstick = 0x00002000,
    LeftTriggerButton = 0x00010000,
    RightTriggerButton = 0x00020000,
    LeftThumbstickUp = 0x00040000,
    LeftThumbstickDown = 0x00080000,
    LeftThumbstickLeft = 0x00100000,
    LeftThumbstickRight = 0x00200000,
    RightThumbstickUp = 0x00400000,
    RightThumbstickDown = 0x00800000,
    RightThumbstickLeft = 0x01000000,
    RightThumbstickRight = 0x02000000,
    PaddleLeft1 = 0x04000000,
    PaddleLeft2 = 0x08000000,
    PaddleRight1 = 0x10000000,
    PaddleRight2 = 0x20000000
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
    public ulong Timestamp;
    public GameInputKind InputKind;
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
            state.Timestamp,
            state.InputKind,
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
        ulong timestamp,
        GameInputKind inputKind,
        GameInputGamepadButtons buttons,
        float leftTrigger,
        float rightTrigger,
        float leftThumbstickX,
        float leftThumbstickY,
        float rightThumbstickX,
        float rightThumbstickY)
    {
        Timestamp = timestamp;
        InputKind = inputKind;
        Buttons = buttons;
        LeftTrigger = leftTrigger;
        RightTrigger = rightTrigger;
        LeftThumbstickX = leftThumbstickX;
        LeftThumbstickY = leftThumbstickY;
        RightThumbstickX = rightThumbstickX;
        RightThumbstickY = rightThumbstickY;
    }

    internal GamepadStateSnapshot(
        GameInputGamepadButtons buttons,
        float leftTrigger,
        float rightTrigger,
        float leftThumbstickX,
        float leftThumbstickY,
        float rightThumbstickX,
        float rightThumbstickY)
        : this(
            0,
            GameInputKind.Gamepad,
            buttons,
            leftTrigger,
            rightTrigger,
            leftThumbstickX,
            leftThumbstickY,
            rightThumbstickX,
            rightThumbstickY)
    {

    }

    public ulong Timestamp { get; init; }

    public GameInputKind InputKind { get; init; }

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
/// GameInput 版本資訊。
/// </summary>
internal readonly record struct GameInputVersionInfo(
    ushort Major,
    ushort Minor,
    ushort Build,
    ushort Revision);

/// <summary>
/// GameInput gamepad 能力資訊。
/// </summary>
internal readonly record struct GameInputGamepadCapabilities(
    GameInputGamepadButtons SupportedLayout,
    uint GamepadExtraButtonCount,
    uint GamepadExtraAxisCount,
    uint ExtraButtonCount,
    uint ExtraAxisCount,
    byte[] ExtraButtonIndexes,
    byte[] ExtraAxisIndexes,
    bool HasInputMapper)
{
    public static GameInputGamepadCapabilities Empty { get; } = new(
        GameInputGamepadButtons.None,
        0,
        0,
        0,
        0,
        [],
        [],
        false);
}

/// <summary>
/// GameInput shim 字串欄位截斷旗標。
/// </summary>
[Flags]
internal enum GameInputStringTruncationFlags : uint
{
    None = 0x00000000,
    DeviceId = 0x00000001,
    DeviceRootId = 0x00000002,
    ContainerId = 0x00000004,
    DisplayName = 0x00000008,
    PnpPath = 0x00000010,
    AttemptedModulePath = 0x00000020,
    LoadedModulePath = 0x00000040
}

/// <summary>
/// GameInput shim 與 managed layer 的 ABI 尺寸資訊。
/// </summary>
internal readonly record struct GameInputAbiInfo(
    uint PointerSize,
    uint ShimInfoSize,
    uint RuntimeProbeInfoSize,
    uint DeviceInfoSize,
    uint GamepadStateSize,
    uint DiagnosticsSnapshotSize)
{
    public static GameInputAbiInfo Managed { get; } = new(
        (uint)IntPtr.Size,
        (uint)Marshal.SizeOf<GameInputNativeShimInfo>(),
        (uint)Marshal.SizeOf<GameInputNativeRuntimeProbeInfo>(),
        (uint)Marshal.SizeOf<GameInputNativeDeviceInfo>(),
        (uint)Marshal.SizeOf<GameInputGamepadState>(),
        (uint)Marshal.SizeOf<GameInputNativeDiagnosticsSnapshot>());

    public bool MatchesManagedLayout => this == Managed;

    public void ThrowIfMismatch()
    {
        if (!MatchesManagedLayout)
        {
            throw new BadImageFormatException(
                $"GameInput native shim ABI mismatch. Native={this}; Managed={Managed}");
        }
    }
}

/// <summary>
/// GameInput shim 載入診斷資訊。
/// </summary>
internal readonly record struct GameInputShimInfo(
    uint AbiVersion,
    uint GameInputApiVersion,
    GameInputAbiInfo AbiInfo,
    GameInputShimModuleKind LoadedModuleKind,
    string LoadedModulePath);

/// <summary>
/// GameInput runtime probe 結果。
/// </summary>
internal readonly record struct GameInputRuntimeProbeInfo(
    uint AbiVersion,
    uint GameInputApiVersion,
    GameInputAbiInfo AbiInfo,
    GameInputShimModuleKind AttemptedModuleKind,
    GameInputShimModuleKind LoadedModuleKind,
    int LoadLibraryHResult,
    int GetProcAddressHResult,
    int InitializeHResult,
    int FinalHResult,
    uint LoadLibraryWin32Error,
    uint GetProcAddressWin32Error,
    uint InitializeWin32Error,
    GameInputStringTruncationFlags StringTruncationFlags,
    string AttemptedModulePath,
    string LoadedModulePath);

/// <summary>
/// GameInput shim 診斷計數器快照。
/// </summary>
internal readonly record struct GameInputDiagnosticsSnapshot(
    ulong MissingReadingCount,
    ulong RepeatedTimestampCount,
    ulong BackwardTimestampCount,
    ulong DeviceUnavailableRefreshCount,
    ulong LastReadingTimestamp,
    int LastReadHResult,
    uint LastReadDeviceStatus);

/// <summary>
/// GameInput runtime 載入來源。
/// </summary>
internal enum GameInputShimModuleKind : uint
{
    Unknown = 0,
    SystemGameInput = 1,
    SystemGameInputRedist = 2,
    RegistryGameInputRedist = 3
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
        ushort revisionNumber,
        ushort usagePage,
        ushort usageId,
        uint deviceFamily,
        GameInputKind supportedInput,
        GameInputRumbleMotors supportedRumbleMotors,
        uint supportedSystemButtons,
        GameInputVersionInfo hardwareVersion,
        GameInputVersionInfo firmwareVersion,
        string deviceRootId,
        string containerId,
        string pnpPath,
        GameInputStringTruncationFlags stringTruncationFlags,
        GameInputGamepadCapabilities gamepadCapabilities,
        string displayName)
    {
        DeviceId = deviceId;
        VendorId = vendorId;
        ProductId = productId;
        RevisionNumber = revisionNumber;
        UsagePage = usagePage;
        UsageId = usageId;
        DeviceFamily = deviceFamily;
        SupportedInput = supportedInput;
        SupportedRumbleMotors = supportedRumbleMotors;
        SupportedSystemButtons = supportedSystemButtons;
        HardwareVersion = hardwareVersion;
        FirmwareVersion = firmwareVersion;
        DeviceRootId = deviceRootId;
        ContainerId = containerId;
        PnpPath = pnpPath;
        StringTruncationFlags = stringTruncationFlags;
        GamepadCapabilities = gamepadCapabilities;
        DisplayName = displayName;
    }

    public string DeviceId { get; }

    public ushort VendorId { get; }

    public ushort ProductId { get; }

    public ushort RevisionNumber { get; }

    public ushort UsagePage { get; }

    public ushort UsageId { get; }

    public uint DeviceFamily { get; }

    public GameInputKind SupportedInput { get; }

    public GameInputRumbleMotors SupportedRumbleMotors { get; }

    public uint SupportedSystemButtons { get; }

    public GameInputVersionInfo HardwareVersion { get; }

    public GameInputVersionInfo FirmwareVersion { get; }

    public string DeviceRootId { get; }

    public string ContainerId { get; }

    public string PnpPath { get; }

    public GameInputStringTruncationFlags StringTruncationFlags { get; }

    public GameInputGamepadCapabilities GamepadCapabilities { get; }

    private string DisplayName { get; }

    public string GetDisplayName() => DisplayName;
}

/// <summary>
/// Native shim 回傳的版本資訊。
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct GameInputNativeVersionInfo
{
    public ushort Major;
    public ushort Minor;
    public ushort Build;
    public ushort Revision;

    public readonly GameInputVersionInfo ToVersionInfo()
        => new(Major, Minor, Build, Revision);
}

/// <summary>
/// Native shim 回傳的載入診斷資訊。
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct GameInputNativeShimInfo
{
    public uint AbiVersion;
    public uint GameInputApiVersion;
    public uint PointerSize;
    public uint ShimInfoSize;
    public uint RuntimeProbeInfoSize;
    public uint DeviceInfoSize;
    public uint GamepadStateSize;
    public uint DiagnosticsSnapshotSize;
    public uint LoadedModuleKind;
    public fixed byte LoadedModulePath[512];

    public readonly GameInputShimInfo ToShimInfo()
    {
        string loadedModulePath;

        fixed (byte* loadedModulePathPtr = LoadedModulePath)
        {
            loadedModulePath = Marshal.PtrToStringUTF8((nint)loadedModulePathPtr) ?? string.Empty;
        }

        return new GameInputShimInfo(
            AbiVersion,
            GameInputApiVersion,
            new GameInputAbiInfo(
                PointerSize,
                ShimInfoSize,
                RuntimeProbeInfoSize,
                DeviceInfoSize,
                GamepadStateSize,
                DiagnosticsSnapshotSize),
            (GameInputShimModuleKind)LoadedModuleKind,
            loadedModulePath);
    }
}

/// <summary>
/// Native shim 回傳的 runtime probe 診斷資訊。
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct GameInputNativeRuntimeProbeInfo
{
    public uint AbiVersion;
    public uint GameInputApiVersion;
    public uint PointerSize;
    public uint ShimInfoSize;
    public uint RuntimeProbeInfoSize;
    public uint DeviceInfoSize;
    public uint GamepadStateSize;
    public uint DiagnosticsSnapshotSize;
    public uint AttemptedModuleKind;
    public uint LoadedModuleKind;
    public int LoadLibraryHResult;
    public int GetProcAddressHResult;
    public int InitializeHResult;
    public int FinalHResult;
    public uint LoadLibraryWin32Error;
    public uint GetProcAddressWin32Error;
    public uint InitializeWin32Error;
    public uint StringTruncationFlags;
    public fixed byte AttemptedModulePath[512];
    public fixed byte LoadedModulePath[512];

    public readonly GameInputRuntimeProbeInfo ToProbeInfo()
    {
        string attemptedModulePath;
        string loadedModulePath;

        fixed (byte* attemptedModulePathPtr = AttemptedModulePath)
        fixed (byte* loadedModulePathPtr = LoadedModulePath)
        {
            attemptedModulePath = Marshal.PtrToStringUTF8((nint)attemptedModulePathPtr) ?? string.Empty;
            loadedModulePath = Marshal.PtrToStringUTF8((nint)loadedModulePathPtr) ?? string.Empty;
        }

        return new GameInputRuntimeProbeInfo(
            AbiVersion,
            GameInputApiVersion,
            new GameInputAbiInfo(
                PointerSize,
                ShimInfoSize,
                RuntimeProbeInfoSize,
                DeviceInfoSize,
                GamepadStateSize,
                DiagnosticsSnapshotSize),
            (GameInputShimModuleKind)AttemptedModuleKind,
            (GameInputShimModuleKind)LoadedModuleKind,
            LoadLibraryHResult,
            GetProcAddressHResult,
            InitializeHResult,
            FinalHResult,
            LoadLibraryWin32Error,
            GetProcAddressWin32Error,
            InitializeWin32Error,
            (GameInputStringTruncationFlags)StringTruncationFlags,
            attemptedModulePath,
            loadedModulePath);
    }
}

/// <summary>
/// Native shim 回傳的裝置資訊。
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct GameInputNativeDeviceInfo
{
    public ushort VendorId;
    public ushort ProductId;
    public ushort RevisionNumber;
    public ushort UsagePage;
    public ushort UsageId;
    public ushort Reserved;
    public uint DeviceFamily;
    public uint SupportedInput;
    public uint SupportedRumbleMotors;
    public uint SupportedSystemButtons;
    public uint GamepadSupportedLayout;
    public uint GamepadExtraButtonCount;
    public uint GamepadExtraAxisCount;
    public uint ForceFeedbackMotorCount;
    public uint InputReportCount;
    public uint OutputReportCount;
    public uint ExtraButtonCount;
    public uint ExtraAxisCount;
    public uint ExtraButtonIndexCount;
    public uint ExtraAxisIndexCount;
    public uint HasInputMapper;
    public uint StringTruncationFlags;
    public GameInputNativeVersionInfo HardwareVersion;
    public GameInputNativeVersionInfo FirmwareVersion;
    public fixed byte ExtraButtonIndexes[32];
    public fixed byte ExtraAxisIndexes[32];
    public fixed byte DeviceId[65];
    public fixed byte DeviceRootId[65];
    public fixed byte ContainerId[39];
    public fixed byte DisplayName[256];
    public fixed byte PnpPath[512];

    public GameInputDeviceInfo ToDeviceInfo()
    {
        string parsedDeviceId;
        string parsedDeviceRootId;
        string parsedContainerId;
        string parsedDisplayName;
        string parsedPnpPath;
        byte[] extraButtonIndexes;
        byte[] extraAxisIndexes;

        fixed (byte* extraButtonIndexesPtr = ExtraButtonIndexes)
        fixed (byte* extraAxisIndexesPtr = ExtraAxisIndexes)
        fixed (byte* deviceIdPtr = DeviceId)
        fixed (byte* deviceRootIdPtr = DeviceRootId)
        fixed (byte* containerIdPtr = ContainerId)
        fixed (byte* displayNamePtr = DisplayName)
        fixed (byte* pnpPathPtr = PnpPath)
        {
            extraButtonIndexes = ReadIndexes(extraButtonIndexesPtr, ExtraButtonIndexCount);
            extraAxisIndexes = ReadIndexes(extraAxisIndexesPtr, ExtraAxisIndexCount);
            parsedDeviceId = Marshal.PtrToStringUTF8((nint)deviceIdPtr) ?? string.Empty;
            parsedDeviceRootId = Marshal.PtrToStringUTF8((nint)deviceRootIdPtr) ?? string.Empty;
            parsedContainerId = Marshal.PtrToStringUTF8((nint)containerIdPtr) ?? string.Empty;
            parsedDisplayName = Marshal.PtrToStringUTF8((nint)displayNamePtr) ?? string.Empty;
            parsedPnpPath = Marshal.PtrToStringUTF8((nint)pnpPathPtr) ?? string.Empty;
        }

        GameInputGamepadCapabilities capabilities = new(
            (GameInputGamepadButtons)GamepadSupportedLayout,
            GamepadExtraButtonCount,
            GamepadExtraAxisCount,
            ExtraButtonCount,
            ExtraAxisCount,
            extraButtonIndexes,
            extraAxisIndexes,
            HasInputMapper != 0);

        return new GameInputDeviceInfo(
            parsedDeviceId,
            VendorId,
            ProductId,
            RevisionNumber,
            UsagePage,
            UsageId,
            DeviceFamily,
            (GameInputKind)SupportedInput,
            (GameInputRumbleMotors)SupportedRumbleMotors,
            SupportedSystemButtons,
            HardwareVersion.ToVersionInfo(),
            FirmwareVersion.ToVersionInfo(),
            parsedDeviceRootId,
            parsedContainerId,
            parsedPnpPath,
            (GameInputStringTruncationFlags)StringTruncationFlags,
            capabilities,
            parsedDisplayName);
    }

    private static byte[] ReadIndexes(byte* source, uint count)
    {
        int length = (int)Math.Min(count, 32);

        if (length == 0)
        {
            return [];
        }

        byte[] indexes = new byte[length];

        for (int i = 0; i < length; i++)
        {
            indexes[i] = source[i];
        }

        return indexes;
    }
}

/// <summary>
/// Native shim 回傳的診斷計數器快照。
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct GameInputNativeDiagnosticsSnapshot
{
    public ulong MissingReadingCount;
    public ulong RepeatedTimestampCount;
    public ulong BackwardTimestampCount;
    public ulong DeviceUnavailableRefreshCount;
    public ulong LastReadingTimestamp;
    public int LastReadHResult;
    public uint LastReadDeviceStatus;
    public uint Reserved;

    public readonly GameInputDiagnosticsSnapshot ToDiagnosticsSnapshot()
        => new(
            MissingReadingCount,
            RepeatedTimestampCount,
            BackwardTimestampCount,
            DeviceUnavailableRefreshCount,
            LastReadingTimestamp,
            LastReadHResult,
            LastReadDeviceStatus);
}
