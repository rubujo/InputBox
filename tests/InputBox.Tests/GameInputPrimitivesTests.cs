using InputBox.Core.Input;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace InputBox.Tests;

/// <summary>
/// 驗證自有 GameInput shim 的受控資料模型，避免 native ABI 擴充後破壞既有 Gamepad 語意。
/// </summary>
public sealed class GameInputPrimitivesTests
{
    /// <summary>
    /// Managed P/Invoke 宣告的 EntryPoint 清單應維持固定，讓 CI/release export 驗證能防止執行期才發現符號缺失。
    /// </summary>
    [Fact]
    public void GameInputNativeMethods_DeclaredEntryPoints_MatchExpectedExportSet()
    {
        string sourcePath = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "InputBox",
            "Core",
            "Input",
            "GameInputNative.cs");
        string source = File.ReadAllText(sourcePath, Encoding.UTF8);

        string[] actual = Regex
            .Matches(source, @"EntryPoint\s*=\s*""(?<name>InputBoxGameInput[^""]+)""")
            .Select(match => match.Groups["name"].Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        string[] expected =
        [
            "InputBoxGameInputCreate",
            "InputBoxGameInputDestroy",
            "InputBoxGameInputGetDeviceCount",
            "InputBoxGameInputGetDeviceInfo",
            "InputBoxGameInputGetDeviceStatus",
            "InputBoxGameInputGetDiagnosticsSnapshot",
            "InputBoxGameInputGetShimInfo",
            "InputBoxGameInputProbeRuntime",
            "InputBoxGameInputReadGamepadState",
            "InputBoxGameInputRefreshDevices",
            "InputBoxGameInputRegisterDeviceCallback",
            "InputBoxGameInputRegisterReadingCallback",
            "InputBoxGameInputSetFocusPolicy",
            "InputBoxGameInputSetRumbleState",
            "InputBoxGameInputUnregisterCallback"
        ];

        Assert.Equal(expected.OrderBy(name => name, StringComparer.Ordinal), actual);
    }

    /// <summary>
    /// GameInput v3 官方新增的 C/Z、搖桿方向與背鍵位元，應與 Microsoft header 常數一致且不覆蓋既有按鍵。
    /// </summary>
    [Fact]
    public void GameInputGamepadButtons_OfficialV3Values_MatchMicrosoftConstants()
    {
        Assert.Equal(0x00004000u, (uint)GameInputGamepadButtons.C);
        Assert.Equal(0x00008000u, (uint)GameInputGamepadButtons.Z);
        Assert.Equal(0x00040000u, (uint)GameInputGamepadButtons.LeftThumbstickUp);
        Assert.Equal(0x00080000u, (uint)GameInputGamepadButtons.LeftThumbstickDown);
        Assert.Equal(0x00100000u, (uint)GameInputGamepadButtons.LeftThumbstickLeft);
        Assert.Equal(0x00200000u, (uint)GameInputGamepadButtons.LeftThumbstickRight);
        Assert.Equal(0x00400000u, (uint)GameInputGamepadButtons.RightThumbstickUp);
        Assert.Equal(0x00800000u, (uint)GameInputGamepadButtons.RightThumbstickDown);
        Assert.Equal(0x01000000u, (uint)GameInputGamepadButtons.RightThumbstickLeft);
        Assert.Equal(0x02000000u, (uint)GameInputGamepadButtons.RightThumbstickRight);
        Assert.Equal(0x04000000u, (uint)GameInputGamepadButtons.PaddleLeft1);
        Assert.Equal(0x08000000u, (uint)GameInputGamepadButtons.PaddleLeft2);
        Assert.Equal(0x10000000u, (uint)GameInputGamepadButtons.PaddleRight1);
        Assert.Equal(0x20000000u, (uint)GameInputGamepadButtons.PaddleRight2);
    }

    /// <summary>
    /// Native gamepad state 的 timestamp 與 input kind 應被帶入快照，供診斷使用但不改變按鍵資料。
    /// </summary>
    [Fact]
    public void GamepadStateSnapshot_NativeState_CarriesTimestampAndInputKind()
    {
        GameInputGamepadState nativeState = new()
        {
            Timestamp = 123456789,
            InputKind = GameInputKind.Gamepad,
            Buttons = GameInputGamepadButtons.A | GameInputGamepadButtons.PaddleLeft1,
            LeftTrigger = 0.25f,
            RightTrigger = 0.75f,
            LeftThumbstickX = -0.5f,
            LeftThumbstickY = 0.5f,
            RightThumbstickX = -1.0f,
            RightThumbstickY = 1.0f
        };

        GamepadStateSnapshot snapshot = new(nativeState);

        Assert.Equal(123456789ul, snapshot.Timestamp);
        Assert.Equal(GameInputKind.Gamepad, snapshot.InputKind);
        Assert.Equal(nativeState.Buttons, snapshot.Buttons);
        Assert.Equal(nativeState.LeftTrigger, snapshot.LeftTrigger);
        Assert.Equal(nativeState.RightTrigger, snapshot.RightTrigger);
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

        GamepadStateSnapshot first = new(
            1,
            GameInputKind.Gamepad,
            GameInputGamepadButtons.A,
            0.1f,
            0.2f,
            -0.3f,
            0.4f,
            -0.5f,
            0.6f);
        GamepadStateSnapshot second = first with { Timestamp = 2 };

        Assert.True((bool)method.Invoke(null, [second, first])!);
    }

    /// <summary>
    /// Shim info 應保留 native struct size metadata，供 managed create 階段拒絕載錯或 ABI 不符的 DLL。
    /// </summary>
    [Fact]
    public unsafe void ToShimInfo_NativeAbiSizes_PreservesManagedLayoutMetadata()
    {
        GameInputNativeShimInfo nativeInfo = CreateNativeShimInfo();
        byte* loadedModulePath = nativeInfo.LoadedModulePath;
        WriteUtf8(loadedModulePath, 512, @"C:\Windows\System32\GameInput.dll");

        GameInputShimInfo shimInfo = nativeInfo.ToShimInfo();

        Assert.Equal(GameInputAbiInfo.Managed, shimInfo.AbiInfo);
        Assert.True(shimInfo.AbiInfo.MatchesManagedLayout);
        Assert.Equal(GameInputShimModuleKind.SystemGameInput, shimInfo.LoadedModuleKind);
        Assert.Equal(@"C:\Windows\System32\GameInput.dll", shimInfo.LoadedModulePath);
    }

    /// <summary>
    /// ABI size mismatch 應被視為 shim 載錯，避免 managed layer 用錯 struct layout 讀取 native 資料。
    /// </summary>
    [Fact]
    public void ThrowIfMismatch_DifferentNativeDeviceInfoSize_ThrowsBadImageFormatException()
    {
        GameInputAbiInfo mismatched = GameInputAbiInfo.Managed with
        {
            DeviceInfoSize = GameInputAbiInfo.Managed.DeviceInfoSize + 4
        };

        Assert.Throws<BadImageFormatException>(mismatched.ThrowIfMismatch);
    }

    /// <summary>
    /// Runtime probe 應完整保留載入階段錯誤資訊，讓 fallback XInput 時能定位 DLL、export 或 initialize 失敗。
    /// </summary>
    [Fact]
    public unsafe void ToProbeInfo_NativeRuntimeProbe_PreservesLoadDiagnostics()
    {
        GameInputNativeRuntimeProbeInfo nativeInfo = new()
        {
            AbiVersion = 3,
            GameInputApiVersion = 0x12345678,
            PointerSize = GameInputAbiInfo.Managed.PointerSize,
            ShimInfoSize = GameInputAbiInfo.Managed.ShimInfoSize,
            RuntimeProbeInfoSize = GameInputAbiInfo.Managed.RuntimeProbeInfoSize,
            DeviceInfoSize = GameInputAbiInfo.Managed.DeviceInfoSize,
            GamepadStateSize = GameInputAbiInfo.Managed.GamepadStateSize,
            DiagnosticsSnapshotSize = GameInputAbiInfo.Managed.DiagnosticsSnapshotSize,
            AttemptedModuleKind = (uint)GameInputShimModuleKind.RegistryGameInputRedist,
            LoadedModuleKind = (uint)GameInputShimModuleKind.RegistryGameInputRedist,
            LoadLibraryHResult = unchecked((int)0x8007007E),
            GetProcAddressHResult = 0,
            InitializeHResult = unchecked((int)0x80004005),
            FinalHResult = unchecked((int)0x80004005),
            LoadLibraryWin32Error = 126,
            GetProcAddressWin32Error = 0,
            InitializeWin32Error = 0,
            StringTruncationFlags = (uint)GameInputStringTruncationFlags.AttemptedModulePath
        };

        byte* attemptedModulePath = nativeInfo.AttemptedModulePath;
        byte* loadedModulePath = nativeInfo.LoadedModulePath;
        WriteUtf8(attemptedModulePath, 512, @"C:\Program Files\GameInput\GameInputRedist.dll");
        WriteUtf8(loadedModulePath, 512, @"C:\Program Files\GameInput\GameInputRedist.dll");

        GameInputRuntimeProbeInfo probeInfo = nativeInfo.ToProbeInfo();

        Assert.Equal(GameInputAbiInfo.Managed, probeInfo.AbiInfo);
        Assert.Equal(GameInputShimModuleKind.RegistryGameInputRedist, probeInfo.AttemptedModuleKind);
        Assert.Equal(GameInputShimModuleKind.RegistryGameInputRedist, probeInfo.LoadedModuleKind);
        Assert.Equal(unchecked((int)0x8007007E), probeInfo.LoadLibraryHResult);
        Assert.Equal(unchecked((int)0x80004005), probeInfo.InitializeHResult);
        Assert.Equal(126u, probeInfo.LoadLibraryWin32Error);
        Assert.True(probeInfo.StringTruncationFlags.HasFlag(GameInputStringTruncationFlags.AttemptedModulePath));
        Assert.Equal(@"C:\Program Files\GameInput\GameInputRedist.dll", probeInfo.AttemptedModulePath);
    }

    /// <summary>
    /// Shim diagnostics counter 應只作為診斷快照保留，不與 gamepad state edge detection 混在一起。
    /// </summary>
    [Fact]
    public void ToDiagnosticsSnapshot_NativeCounters_PreservesStaleReadingMetadata()
    {
        GameInputNativeDiagnosticsSnapshot nativeSnapshot = new()
        {
            MissingReadingCount = 3,
            RepeatedTimestampCount = 4,
            BackwardTimestampCount = 5,
            DeviceUnavailableRefreshCount = 6,
            LastReadingTimestamp = 7,
            LastReadHResult = unchecked((int)0x8007048F),
            LastReadDeviceStatus = 0x20
        };

        GameInputDiagnosticsSnapshot snapshot = nativeSnapshot.ToDiagnosticsSnapshot();

        Assert.Equal(3ul, snapshot.MissingReadingCount);
        Assert.Equal(4ul, snapshot.RepeatedTimestampCount);
        Assert.Equal(5ul, snapshot.BackwardTimestampCount);
        Assert.Equal(6ul, snapshot.DeviceUnavailableRefreshCount);
        Assert.Equal(7ul, snapshot.LastReadingTimestamp);
        Assert.Equal(unchecked((int)0x8007048F), snapshot.LastReadHResult);
        Assert.Equal(0x20u, snapshot.LastReadDeviceStatus);
    }

    /// <summary>
    /// Managed GameInput kind 應維持 gamepad-only subset，不暴露 keyboard、mouse、sensor 或 raw report 路徑。
    /// </summary>
    [Fact]
    public void GameInputKind_ManagedSubset_OnlyExposesUnknownAndGamepad()
    {
        GameInputKind[] values = Enum.GetValues<GameInputKind>();

        Assert.Equal([GameInputKind.Unknown, GameInputKind.Gamepad], values);
    }

    /// <summary>
    /// Native device info 擴充欄位應完整轉成 managed model，保留 VID/PID、版本、PnP path 與 extra control indexes。
    /// </summary>
    [Fact]
    public unsafe void ToDeviceInfo_ExtendedNativeFields_PreservesGamepadCapabilities()
    {
        GameInputNativeDeviceInfo nativeInfo = new()
        {
            VendorId = 0x054C,
            ProductId = 0x0CE6,
            RevisionNumber = 7,
            UsagePage = 1,
            UsageId = 5,
            DeviceFamily = 3,
            SupportedInput = (uint)GameInputKind.Gamepad,
            SupportedRumbleMotors = (uint)(GameInputRumbleMotors.LowFrequency | GameInputRumbleMotors.HighFrequency),
            SupportedSystemButtons = 0x12,
            GamepadSupportedLayout = (uint)(GameInputGamepadButtons.A | GameInputGamepadButtons.B | GameInputGamepadButtons.PaddleLeft1),
            GamepadExtraButtonCount = 2,
            GamepadExtraAxisCount = 1,
            ForceFeedbackMotorCount = 4,
            InputReportCount = 2,
            OutputReportCount = 1,
            ExtraButtonCount = 2,
            ExtraAxisCount = 1,
            ExtraButtonIndexCount = 2,
            ExtraAxisIndexCount = 1,
            HasInputMapper = 1,
            StringTruncationFlags = (uint)GameInputStringTruncationFlags.PnpPath,
            HardwareVersion = new GameInputNativeVersionInfo { Major = 1, Minor = 2, Build = 3, Revision = 4 },
            FirmwareVersion = new GameInputNativeVersionInfo { Major = 5, Minor = 6, Build = 7, Revision = 8 }
        };

        byte* deviceId = nativeInfo.DeviceId;
        byte* deviceRootId = nativeInfo.DeviceRootId;
        byte* containerId = nativeInfo.ContainerId;
        byte* displayName = nativeInfo.DisplayName;
        byte* pnpPath = nativeInfo.PnpPath;
        byte* extraButtonIndexes = nativeInfo.ExtraButtonIndexes;
        byte* extraAxisIndexes = nativeInfo.ExtraAxisIndexes;

        WriteUtf8(deviceId, 65, "00112233445566778899AABBCCDDEEFF");
        WriteUtf8(deviceRootId, 65, "FFEEDDCCBBAA99887766554433221100");
        WriteUtf8(containerId, 39, "{00112233-4455-6677-8899-AABBCCDDEEFF}");
        WriteUtf8(displayName, 256, "DualSense Wireless Controller");
        WriteUtf8(pnpPath, 512, @"HID\VID_054C&PID_0CE6");
        extraButtonIndexes[0] = 4;
        extraButtonIndexes[1] = 5;
        extraAxisIndexes[0] = 6;

        GameInputDeviceInfo info = nativeInfo.ToDeviceInfo();

        Assert.Equal("00112233445566778899AABBCCDDEEFF", info.DeviceId);
        Assert.Equal("FFEEDDCCBBAA99887766554433221100", info.DeviceRootId);
        Assert.Equal("{00112233-4455-6677-8899-AABBCCDDEEFF}", info.ContainerId);
        Assert.Equal("DualSense Wireless Controller", info.GetDisplayName());
        Assert.Equal(@"HID\VID_054C&PID_0CE6", info.PnpPath);
        Assert.Equal((ushort)0x054C, info.VendorId);
        Assert.Equal((ushort)0x0CE6, info.ProductId);
        Assert.Equal((ushort)7, info.RevisionNumber);
        Assert.Equal(GameInputStringTruncationFlags.PnpPath, info.StringTruncationFlags);
        Assert.Equal(new GameInputVersionInfo(1, 2, 3, 4), info.HardwareVersion);
        Assert.Equal(new GameInputVersionInfo(5, 6, 7, 8), info.FirmwareVersion);
        Assert.True(info.GamepadCapabilities.HasInputMapper);
        Assert.Equal(GameInputGamepadButtons.A | GameInputGamepadButtons.B | GameInputGamepadButtons.PaddleLeft1, info.GamepadCapabilities.SupportedLayout);
        Assert.Equal(new byte[] { 4, 5 }, info.GamepadCapabilities.ExtraButtonIndexes);
        Assert.Equal(new byte[] { 6 }, info.GamepadCapabilities.ExtraAxisIndexes);
    }

    private static GameInputNativeShimInfo CreateNativeShimInfo()
        => new()
        {
            AbiVersion = 3,
            GameInputApiVersion = 0x12345678,
            PointerSize = GameInputAbiInfo.Managed.PointerSize,
            ShimInfoSize = GameInputAbiInfo.Managed.ShimInfoSize,
            RuntimeProbeInfoSize = GameInputAbiInfo.Managed.RuntimeProbeInfoSize,
            DeviceInfoSize = GameInputAbiInfo.Managed.DeviceInfoSize,
            GamepadStateSize = GameInputAbiInfo.Managed.GamepadStateSize,
            DiagnosticsSnapshotSize = GameInputAbiInfo.Managed.DiagnosticsSnapshotSize,
            LoadedModuleKind = (uint)GameInputShimModuleKind.SystemGameInput
        };

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory != null)
        {
            string candidate = Path.Combine(directory.FullName, "src", "InputBox", "Core", "Input", "GameInputNative.cs");
            if (File.Exists(candidate))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("找不到 InputBox repository root。");
    }

    private static unsafe void WriteUtf8(byte* destination, int destinationLength, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        int count = Math.Min(bytes.Length, destinationLength - 1);

        for (int i = 0; i < count; i++)
        {
            destination[i] = bytes[i];
        }

        destination[count] = 0;
    }
}
