param([switch]$ProgressiveScan, [switch]$DevelopmentBuild)
$ErrorActionPreference = 'Stop'
$makeArguments = @()
if ($ProgressiveScan) { $makeArguments += 'PROGRESSIVE=1' }
if ($DevelopmentBuild) { $makeArguments += 'DEVELOPMENT=1' }

$nativeCompiler = Get-Command 'mips64r5900el-ps2-elf-gcc' -ErrorAction SilentlyContinue
$nativeMake = Get-Command 'make' -ErrorAction SilentlyContinue

if ($null -ne $nativeCompiler -and $null -ne $nativeMake) {
    Push-Location $PSScriptRoot
    try {
        & $nativeMake.Source @makeArguments
        if ($LASTEXITCODE -ne 0) { throw "PS2 build failed with exit code $LASTEXITCODE." }
    }
    finally {
        Pop-Location
    }
}
else {
    . (Join-Path $PSScriptRoot 'wsl-common.ps1')
    $wslDistribution = Get-R2WslDistribution

    & wsl.exe -d $wslDistribution -- bash -lc 'test -x "${HOME}/.local/ps2dev/ee/bin/mips64r5900el-ps2-elf-gcc"'
    if ($LASTEXITCODE -ne 0) {
        throw "PS2 build prerequisites missing in WSL distribution '$wslDistribution': the PS2DEV compiler is not installed at ~/.local/ps2dev. Run R2Engine.PS2/install-toolchain-wsl.sh inside that distribution."
    }

    $resolvedRoot = (Resolve-Path -LiteralPath $PSScriptRoot).Path
    $drive = $resolvedRoot.Substring(0, 1).ToLowerInvariant()
    $relative = $resolvedRoot.Substring(2).Replace('\', '/')
    $linuxScript = "/mnt/$drive$relative/build-wsl.sh"
    & wsl.exe -d $wslDistribution -- bash $linuxScript @makeArguments
    if ($LASTEXITCODE -ne 0) { throw "PS2 compilation failed in WSL distribution '$wslDistribution' with exit code $LASTEXITCODE." }
}

$elf = Join-Path $PSScriptRoot 'bin\r2engine-ps2-probe.elf'
if (-not (Test-Path -LiteralPath $elf) -or (Get-Item -LiteralPath $elf).Length -lt 1024) {
    throw 'The PS2 build did not produce a valid ELF output.'
}
Write-Host "Built $elf"
