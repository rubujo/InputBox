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

    [DllImport("$nativeShimLiteral", EntryPoint = "InputBoxGameInputProbeRuntime", CallingConvention = CallingConvention.StdCall)]
    public static extern int ProbeRuntime(out RuntimeProbeInfo info);
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

    if ($probe.ShimInfoSize -eq 0 -or
        $probe.RuntimeProbeInfoSize -eq 0 -or
        $probe.DeviceInfoSize -eq 0 -or
        $probe.GamepadStateSize -eq 0 -or
        $probe.DiagnosticsSnapshotSize -eq 0) {
        throw 'GameInput native probe 回報的 struct size 不完整。'
    }

    Write-Host ("GameInput native probe smoke 通過：hr=0x{0:X8}, abi={1}, api=0x{2:X8}, pointer={3}, finalHr=0x{4:X8}, initHr=0x{5:X8}" -f `
        (ConvertTo-UInt32HexValue -Value $hr),
        $probe.AbiVersion,
        $probe.GameInputApiVersion,
        $probe.PointerSize,
        (ConvertTo-UInt32HexValue -Value $probe.FinalHResult),
        (ConvertTo-UInt32HexValue -Value $probe.InitializeHResult))
}

$resolvedNativeShim = Resolve-RequiredPath -Path $NativeShimPath -Description 'GameInput native shim'
$resolvedManagedSource = Resolve-RequiredPath -Path $ManagedSourcePath -Description 'managed GameInputNative.cs'

Test-NativeExports -NativeShim $resolvedNativeShim -ManagedSource $resolvedManagedSource
Invoke-ProbeSmoke -NativeShim $resolvedNativeShim
