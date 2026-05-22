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
}

$resolvedNativeShim = Resolve-RequiredPath -Path $NativeShimPath -Description 'GameInput native shim'
$resolvedManagedSource = Resolve-RequiredPath -Path $ManagedSourcePath -Description 'managed GameInputNative.cs'

Test-NativeExports -NativeShim $resolvedNativeShim -ManagedSource $resolvedManagedSource
Invoke-ProbeSmoke -NativeShim $resolvedNativeShim
