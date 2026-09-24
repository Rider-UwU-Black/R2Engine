param(
    [switch]$SkipBuild,
    [switch]$ProgressiveScan,
    [switch]$DevelopmentBuild,
    [string]$Pcsx2Path,
    [string]$CookedBuildDirectory,
    [string]$SceneName
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($Pcsx2Path)) {
    $candidates = @(
        'E:\PCSX2\pcsx2-qt.exe',
        (Join-Path $env:LOCALAPPDATA 'Programs\PCSX2\pcsx2-qt.exe'),
        (Join-Path $env:ProgramFiles 'PCSX2\pcsx2-qt.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'PCSX2\pcsx2-qt.exe')
    )
    $Pcsx2Path = $candidates | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -First 1
}

if (-not $SkipBuild) {
    & (Join-Path $PSScriptRoot 'build-ps2.ps1') -ProgressiveScan:$ProgressiveScan -DevelopmentBuild:$DevelopmentBuild
}
if ($CookedBuildDirectory -or $SceneName) {
    if (-not $CookedBuildDirectory -or -not $SceneName) {
        throw 'CookedBuildDirectory and SceneName must be supplied together.'
    }
    & (Join-Path $PSScriptRoot 'stage-cooked-scene.ps1') `
        -BuildDirectory $CookedBuildDirectory -SceneName $SceneName
}
else {
    & (Join-Path $PSScriptRoot 'prepare-probe-assets.ps1')
}

$elf = Join-Path $PSScriptRoot 'bin\r2engine-ps2-probe.elf'
if (-not (Test-Path -LiteralPath $Pcsx2Path)) {
    throw "PCSX2 was not found. Choose its executable in R2Engine Hub > Settings."
}
if (-not (Test-Path -LiteralPath $elf)) {
    throw "The PS2 probe ELF was not found at '$elf'."
}

Start-Process -FilePath $Pcsx2Path -ArgumentList @('-elf', $elf) -WorkingDirectory $PSScriptRoot
Write-Host "Launched $elf in PCSX2."
