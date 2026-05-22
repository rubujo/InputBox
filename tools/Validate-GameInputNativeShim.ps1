param(
    [Parameter(Mandatory = $true)]
    [string]$NativeShimPath,

    [Parameter(Mandatory = $true)]
    [string]$ManagedSourcePath,

    [string]$DumpBinPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Resolve-RequiredPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$Description
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "找不到 $Description：$Path"
    }

    return (Resolve-Path -LiteralPath $Path).Path
}

function Get-DumpBin {
    if (-not [string]::IsNullOrWhiteSpace($DumpBinPath)) {
        return Resolve-RequiredPath -Path $DumpBinPath -Description 'dumpbin.exe'
    }

    $command = Get-Command dumpbin.exe -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    $programFilesX86 = ${env:ProgramFiles(x86)}
    if (-not [string]::IsNullOrWhiteSpace($programFilesX86)) {
        $vswhere = Join-Path $programFilesX86 'Microsoft Visual Studio\Installer\vswhere.exe'
        if (Test-Path -LiteralPath $vswhere) {
            $installPath = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
            if (-not [string]::IsNullOrWhiteSpace($installPath)) {
                $toolsRoot = Join-Path $installPath 'VC\Tools\MSVC'
                if (Test-Path -LiteralPath $toolsRoot) {
                    $candidates = Get-ChildItem -LiteralPath $toolsRoot -Directory |
                        Sort-Object -Property Name -Descending |
                        ForEach-Object {
                            Join-Path $_.FullName 'bin\Hostx64\x64\dumpbin.exe'
                            Join-Path $_.FullName 'bin\Hostx86\x64\dumpbin.exe'
                        } |
                        Where-Object { Test-Path -LiteralPath $_ }

                    $dumpbin = $candidates | Select-Object -First 1
                    if ($dumpbin) {
                        return $dumpbin
                    }
                }
            }
        }
    }

    throw '找不到 dumpbin.exe，請確認 Visual Studio C++ 工具鏈已安裝。'
}

function Get-ExpectedExports {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $source = Get-Content -LiteralPath $Path -Raw -Encoding UTF8
    $matches = [regex]::Matches($source, 'EntryPoint\s*=\s*"(?<name>InputBoxGameInput[^"]+)"')
    $exports = @($matches |
        ForEach-Object { $_.Groups['name'].Value } |
        Sort-Object -Unique)

    if ($exports.Count -eq 0) {
        throw "未在 $Path 找到任何 InputBoxGameInput* P/Invoke EntryPoint。"
    }

    return @($exports)
}

function Test-NativeExports {
    param(
        [Parameter(Mandatory = $true)]
        [string]$NativeShim,

        [Parameter(Mandatory = $true)]
        [string]$ManagedSource
    )

    $dumpbin = Get-DumpBin
    $expectedExports = Get-ExpectedExports -Path $ManagedSource
    $dumpbinOutput = & $dumpbin /exports $NativeShim

    if ($LASTEXITCODE -ne 0) {
        throw "dumpbin /exports 執行失敗，ExitCode=$LASTEXITCODE。"
    }

    $missingExports = @(foreach ($export in $expectedExports) {
        if (-not ($dumpbinOutput -match "\b$([regex]::Escape($export))\b")) {
            $export
        }
    })

    if ($missingExports.Count -gt 0) {
        throw "GameInput native shim 缺少必要 export：$($missingExports -join ', ')"
    }

    Write-Host "GameInput native exports 驗證通過：$($expectedExports.Count) 個 managed P/Invoke EntryPoint 皆存在。"
}

function ConvertTo-UInt32HexValue {
    param(
        [Parameter(Mandatory = $true)]
        [int]$Value
    )

    return [BitConverter]::ToUInt32([BitConverter]::GetBytes($Value), 0)
}

function Invoke-ProbeSmoke {
    param(
        [Parameter(Mandatory = $true)]
        [string]$NativeShim
    )

    $nativeShimLiteral = $NativeShim.Replace('\', '\\').Replace('"', '\"')
    $typeName = 'InputBoxGameInputNativeSmoke' + [Guid]::NewGuid().ToString('N')
    $source = @"
using System;
using System.Runtime.InteropServices;

public static unsafe class $typeName
{
    [StructLayout(LayoutKind.Sequential)]
    public struct VersionInfo
    {
        public ushort Major;
        public ushort Minor;
        public ushort Build;
        public ushort Revision;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct ShimInfo
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
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct RuntimeProbeInfo
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
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct DeviceInfo
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
        public VersionInfo HardwareVersion;
        public VersionInfo FirmwareVersion;
        public fixed byte ExtraButtonIndexes[32];
        public fixed byte ExtraAxisIndexes[32];
        public fixed byte DeviceId[65];
        public fixed byte DeviceRootId[65];
        public fixed byte ContainerId[39];
        public fixed byte DisplayName[256];
        public fixed byte PnpPath[512];
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GamepadState
    {
        public ulong Timestamp;
        public uint InputKind;
        public uint Buttons;
        public float LeftTrigger;
        public float RightTrigger;
        public float LeftThumbstickX;
        public float LeftThumbstickY;
        public float RightThumbstickX;
        public float RightThumbstickY;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DiagnosticsSnapshot
    {
        public ulong MissingReadingCount;
        public ulong RepeatedTimestampCount;
        public ulong BackwardTimestampCount;
        public ulong DeviceUnavailableRefreshCount;
        public ulong LastReadingTimestamp;
        public int LastReadHResult;
        public uint LastReadDeviceStatus;
        public uint Reserved;
    }

    [DllImport("$nativeShimLiteral", EntryPoint = "InputBoxGameInputProbeRuntime", CallingConvention = CallingConvention.StdCall)]
    public static extern int ProbeRuntime(out RuntimeProbeInfo info);

    public static uint GetPointerSize() => (uint)IntPtr.Size;

    public static uint GetShimInfoSize() => (uint)Marshal.SizeOf<ShimInfo>();

    public static uint GetRuntimeProbeInfoSize() => (uint)Marshal.SizeOf<RuntimeProbeInfo>();

    public static uint GetDeviceInfoSize() => (uint)Marshal.SizeOf<DeviceInfo>();

    public static uint GetGamepadStateSize() => (uint)Marshal.SizeOf<GamepadState>();

    public static uint GetDiagnosticsSnapshotSize() => (uint)Marshal.SizeOf<DiagnosticsSnapshot>();
}
"@

    $smokeType = Add-Type -TypeDefinition $source -Language CSharp -CompilerOptions '/unsafe' -PassThru |
        Where-Object { $_.Name -eq $typeName } |
        Select-Object -First 1
    if ($smokeType -eq $null) {
        throw '無法建立 GameInput native probe smoke 型別。'
    }

    $probeType = $smokeType.GetNestedType('RuntimeProbeInfo')
    $probe = [Activator]::CreateInstance($probeType)
    $arguments = @($probe)
    $hr = [int]$smokeType.GetMethod('ProbeRuntime').Invoke($null, $arguments)
    $probe = $arguments[0]

    if ($probe.AbiVersion -eq 0) {
        throw 'GameInput native probe 未回報 ABI version。'
    }

    if ($probe.PointerSize -ne [uint32][IntPtr]::Size) {
        throw "GameInput native probe 指標大小不符：native=$($probe.PointerSize), process=$([IntPtr]::Size)。"
    }

    $expectedSizes = @{
        PointerSize = [uint32]$smokeType.GetMethod('GetPointerSize').Invoke($null, @())
        ShimInfoSize = [uint32]$smokeType.GetMethod('GetShimInfoSize').Invoke($null, @())
        RuntimeProbeInfoSize = [uint32]$smokeType.GetMethod('GetRuntimeProbeInfoSize').Invoke($null, @())
        DeviceInfoSize = [uint32]$smokeType.GetMethod('GetDeviceInfoSize').Invoke($null, @())
        GamepadStateSize = [uint32]$smokeType.GetMethod('GetGamepadStateSize').Invoke($null, @())
        DiagnosticsSnapshotSize = [uint32]$smokeType.GetMethod('GetDiagnosticsSnapshotSize').Invoke($null, @())
    }

    foreach ($name in $expectedSizes.Keys) {
        $actual = [uint32]$probe.$name
        $expected = $expectedSizes[$name]
        if ($actual -ne $expected) {
            throw "GameInput native probe ABI size mismatch: $name native=$actual, managed=$expected。"
        }
    }

    Write-Host ("GameInput native probe smoke 通過：hr=0x{0:X8}, abi={1}, api=0x{2:X8}, pointer={3}, shimInfoSize={4}, runtimeProbeInfoSize={5}, deviceInfoSize={6}, gamepadStateSize={7}, diagnosticsSnapshotSize={8}, finalHr=0x{9:X8}, initHr=0x{10:X8}" -f `
        (ConvertTo-UInt32HexValue -Value $hr),
        $probe.AbiVersion,
        $probe.GameInputApiVersion,
        $probe.PointerSize,
        $probe.ShimInfoSize,
        $probe.RuntimeProbeInfoSize,
        $probe.DeviceInfoSize,
        $probe.GamepadStateSize,
        $probe.DiagnosticsSnapshotSize,
        (ConvertTo-UInt32HexValue -Value $probe.FinalHResult),
        (ConvertTo-UInt32HexValue -Value $probe.InitializeHResult))

    return [pscustomobject]@{
        HResult = $hr
        FinalHResult = [int]$probe.FinalHResult
        InitializeHResult = [int]$probe.InitializeHResult
    }
}

function Invoke-LifecycleStressSmoke {
    param(
        [Parameter(Mandatory = $true)]
        [string]$NativeShim,

        [Parameter(Mandatory = $true)]
        [pscustomobject]$ProbeInfo
    )

    $nativeShimLiteral = $NativeShim.Replace('\', '\\').Replace('"', '\"')
    $typeName = 'InputBoxGameInputNativeLifecycleSmoke' + [Guid]::NewGuid().ToString('N')
    $source = @"
using System;
using System.Runtime.InteropServices;
using System.Threading;

public static unsafe class $typeName
{
    private const uint GameInputKindGamepad = 0x00040000;
    private const uint GameInputDeviceStatusAny = 0xFFFFFFFF;
    private const uint GameInputEnumerationNone = 0;
    private const int HResultNotFound = unchecked((int)0x80070490);

    private static int s_readingCallbackCount;
    private static int s_deviceCallbackCount;

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct ShimInfo
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
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GamepadState
    {
        public ulong Timestamp;
        public uint InputKind;
        public uint Buttons;
        public float LeftTrigger;
        public float RightTrigger;
        public float LeftThumbstickX;
        public float LeftThumbstickY;
        public float RightThumbstickX;
        public float RightThumbstickY;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DiagnosticsSnapshot
    {
        public ulong MissingReadingCount;
        public ulong RepeatedTimestampCount;
        public ulong BackwardTimestampCount;
        public ulong DeviceUnavailableRefreshCount;
        public ulong LastReadingTimestamp;
        public int LastReadHResult;
        public uint LastReadDeviceStatus;
        public uint Reserved;
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate void ReadingCallback(IntPtr context, ref GamepadState state);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate void DeviceCallback(
        IntPtr context,
        IntPtr deviceId,
        ulong timestamp,
        uint currentStatus,
        uint previousStatus);

    private static readonly ReadingCallback ReadingCallbackRoot = OnReadingCallback;
    private static readonly DeviceCallback DeviceCallbackRoot = OnDeviceCallback;

    [DllImport("$nativeShimLiteral", EntryPoint = "InputBoxGameInputCreate", CallingConvention = CallingConvention.StdCall)]
    public static extern int Create(out IntPtr context);

    [DllImport("$nativeShimLiteral", EntryPoint = "InputBoxGameInputDestroy", CallingConvention = CallingConvention.StdCall)]
    public static extern void Destroy(IntPtr context);

    [DllImport("$nativeShimLiteral", EntryPoint = "InputBoxGameInputGetShimInfo", CallingConvention = CallingConvention.StdCall)]
    public static extern int GetShimInfo(IntPtr context, out ShimInfo info);

    [DllImport("$nativeShimLiteral", EntryPoint = "InputBoxGameInputRefreshDevices", CallingConvention = CallingConvention.StdCall)]
    public static extern int RefreshDevices(IntPtr context);

    [DllImport("$nativeShimLiteral", EntryPoint = "InputBoxGameInputGetDeviceCount", CallingConvention = CallingConvention.StdCall)]
    public static extern int GetDeviceCount(IntPtr context);

    [DllImport("$nativeShimLiteral", EntryPoint = "InputBoxGameInputRegisterReadingCallback", CallingConvention = CallingConvention.StdCall)]
    public static extern int RegisterReadingCallback(
        IntPtr context,
        IntPtr deviceId,
        uint kind,
        ReadingCallback callback,
        IntPtr callbackContext,
        out ulong callbackToken);

    [DllImport("$nativeShimLiteral", EntryPoint = "InputBoxGameInputRegisterDeviceCallback", CallingConvention = CallingConvention.StdCall)]
    public static extern int RegisterDeviceCallback(
        IntPtr context,
        IntPtr deviceId,
        uint kind,
        uint statusFilter,
        uint enumerationKind,
        DeviceCallback callback,
        IntPtr callbackContext,
        out ulong callbackToken);

    [DllImport("$nativeShimLiteral", EntryPoint = "InputBoxGameInputGetDiagnosticsSnapshot", CallingConvention = CallingConvention.StdCall)]
    public static extern int GetDiagnosticsSnapshot(IntPtr context, out DiagnosticsSnapshot snapshot);

    [DllImport("$nativeShimLiteral", EntryPoint = "InputBoxGameInputUnregisterCallback", CallingConvention = CallingConvention.StdCall)]
    public static extern int UnregisterCallback(IntPtr context, ulong callbackToken);

    public static int TryCreateAndDestroy()
    {
        IntPtr context = IntPtr.Zero;
        int hr = Create(out context);

        if (context != IntPtr.Zero)
        {
            Destroy(context);
        }

        return hr;
    }

    public static string RunStress(int iterations)
    {
        int explicitUnregisterCount = 0;
        int destroyCleanupCount = 0;
        int doubleUnregisterNotFoundCount = 0;
        int maxDeviceCount = 0;

        for (int i = 0; i < iterations; i++)
        {
            IntPtr context = IntPtr.Zero;
            ulong readingToken = 0;
            ulong deviceToken = 0;
            bool destroyOwnsCallbacks = (i % 2) == 1;

            try
            {
                int hr = Create(out context);
                ThrowIfFailed(hr, "Create");

                if (context == IntPtr.Zero)
                {
                    throw new InvalidOperationException("Create 回傳成功但 context 為空。");
                }

                ShimInfo shimInfo;
                ThrowIfFailed(GetShimInfo(context, out shimInfo), "GetShimInfo");
                ThrowIfFailed(RefreshDevices(context), "RefreshDevices");

                int deviceCount = GetDeviceCount(context);
                if (deviceCount < 0)
                {
                    throw new InvalidOperationException("GetDeviceCount 回傳負值。");
                }

                maxDeviceCount = Math.Max(maxDeviceCount, deviceCount);

                ThrowIfFailed(
                    RegisterDeviceCallback(
                        context,
                        IntPtr.Zero,
                        GameInputKindGamepad,
                        GameInputDeviceStatusAny,
                        GameInputEnumerationNone,
                        DeviceCallbackRoot,
                        IntPtr.Zero,
                        out deviceToken),
                    "RegisterDeviceCallback");
                EnsureToken(deviceToken, "RegisterDeviceCallback");

                ThrowIfFailed(
                    RegisterReadingCallback(
                        context,
                        IntPtr.Zero,
                        GameInputKindGamepad,
                        ReadingCallbackRoot,
                        IntPtr.Zero,
                        out readingToken),
                    "RegisterReadingCallback");
                EnsureToken(readingToken, "RegisterReadingCallback");

                DiagnosticsSnapshot diagnostics;
                ThrowIfFailed(GetDiagnosticsSnapshot(context, out diagnostics), "GetDiagnosticsSnapshot");

                if (!destroyOwnsCallbacks)
                {
                    ThrowIfFailed(UnregisterCallback(context, readingToken), "Unregister reading callback");
                    int secondReadingUnregister = UnregisterCallback(context, readingToken);
                    if (secondReadingUnregister != HResultNotFound)
                    {
                        throw new InvalidOperationException(
                            string.Format(
                                "第二次解除 reading callback 應回傳 not found，但得到 0x{0:X8}。",
                                unchecked((uint)secondReadingUnregister)));
                    }

                    doubleUnregisterNotFoundCount++;
                    ThrowIfFailed(UnregisterCallback(context, deviceToken), "Unregister device callback");
                    explicitUnregisterCount++;
                    readingToken = 0;
                    deviceToken = 0;
                }
                else
                {
                    destroyCleanupCount++;
                }
            }
            finally
            {
                if (context != IntPtr.Zero)
                {
                    Destroy(context);
                }

                GC.KeepAlive(ReadingCallbackRoot);
                GC.KeepAlive(DeviceCallbackRoot);
            }
        }

        return string.Format(
            "iterations={0}, explicitUnregister={1}, destroyCleanup={2}, doubleUnregisterNotFound={3}, maxDeviceCount={4}, readingCallbacks={5}, deviceCallbacks={6}",
            iterations,
            explicitUnregisterCount,
            destroyCleanupCount,
            doubleUnregisterNotFoundCount,
            maxDeviceCount,
            Volatile.Read(ref s_readingCallbackCount),
            Volatile.Read(ref s_deviceCallbackCount));
    }

    private static void OnReadingCallback(IntPtr context, ref GamepadState state)
    {
        Interlocked.Increment(ref s_readingCallbackCount);
    }

    private static void OnDeviceCallback(
        IntPtr context,
        IntPtr deviceId,
        ulong timestamp,
        uint currentStatus,
        uint previousStatus)
    {
        Interlocked.Increment(ref s_deviceCallbackCount);
    }

    private static void ThrowIfFailed(int hr, string operation)
    {
        if (hr < 0)
        {
            throw new InvalidOperationException(
                string.Format(
                    "{0} failed: 0x{1:X8}",
                    operation,
                    unchecked((uint)hr)));
        }
    }

    private static void EnsureToken(ulong callbackToken, string operation)
    {
        if (callbackToken == 0)
        {
            throw new InvalidOperationException(operation + " 回傳成功但 callback token 為 0。");
        }
    }
}
"@

    $stressType = Add-Type -TypeDefinition $source -Language CSharp -CompilerOptions '/unsafe' -PassThru |
        Where-Object { $_.Name -eq $typeName } |
        Select-Object -First 1
    if ($stressType -eq $null) {
        throw '無法建立 GameInput native lifecycle smoke 型別。'
    }

    $createHr = [int]$stressType.GetMethod('TryCreateAndDestroy').Invoke($null, @())
    if ($createHr -lt 0) {
        if ($ProbeInfo.FinalHResult -eq 0 -or $ProbeInfo.InitializeHResult -eq 0) {
            throw ("GameInput native lifecycle smoke 建立 context 失敗，但 probe 顯示 runtime 初始化成功：createHr=0x{0:X8}, finalHr=0x{1:X8}, initHr=0x{2:X8}" -f `
                (ConvertTo-UInt32HexValue -Value $createHr),
                (ConvertTo-UInt32HexValue -Value $ProbeInfo.FinalHResult),
                (ConvertTo-UInt32HexValue -Value $ProbeInfo.InitializeHResult))
        }

        Write-Warning ("GameInput native lifecycle smoke 已略過：runtime/context 不可用，createHr=0x{0:X8}, finalHr=0x{1:X8}, initHr=0x{2:X8}。" -f `
            (ConvertTo-UInt32HexValue -Value $createHr),
            (ConvertTo-UInt32HexValue -Value $ProbeInfo.FinalHResult),
            (ConvertTo-UInt32HexValue -Value $ProbeInfo.InitializeHResult))
        return
    }

    try {
        $summary = [string]$stressType.GetMethod('RunStress').Invoke($null, @(16))
        Write-Host "GameInput native lifecycle stress smoke 通過：$summary"
    }
    catch [System.Reflection.TargetInvocationException] {
        if ($_.Exception.InnerException -ne $null) {
            throw $_.Exception.InnerException
        }

        throw
    }
}

$resolvedNativeShim = Resolve-RequiredPath -Path $NativeShimPath -Description 'GameInput native shim'
$resolvedManagedSource = Resolve-RequiredPath -Path $ManagedSourcePath -Description 'managed GameInputNative.cs'

Test-NativeExports -NativeShim $resolvedNativeShim -ManagedSource $resolvedManagedSource
$probeInfo = Invoke-ProbeSmoke -NativeShim $resolvedNativeShim
Invoke-LifecycleStressSmoke -NativeShim $resolvedNativeShim -ProbeInfo $probeInfo
