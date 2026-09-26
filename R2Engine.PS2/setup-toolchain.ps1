$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'wsl-common.ps1')
$distribution = Get-R2UbuntuDistribution

Write-Host "Preparing the R2Engine PS2 toolchain in WSL distribution '$distribution'."
Write-Host 'Linux may ask for your password while installing prerequisites.'

& wsl.exe -d $distribution -- bash -lc `
    'sudo apt-get update && sudo apt-get install -y curl make python3 genisoimage'
if ($LASTEXITCODE -ne 0) {
    throw "Linux prerequisite installation failed in '$distribution'."
}

$resolvedScript = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot 'install-toolchain-wsl.sh')).Path
$drive = $resolvedScript.Substring(0, 1).ToLowerInvariant()
$relative = $resolvedScript.Substring(2).Replace('\', '/')
$wslScript = "/mnt/$drive$relative"

& wsl.exe -d $distribution -- bash $wslScript
if ($LASTEXITCODE -ne 0) {
    throw "PS2DEV installation failed in '$distribution'."
}

& wsl.exe -d $distribution -- bash -lc `
    '"${HOME}/.local/ps2dev/ee/bin/mips64r5900el-ps2-elf-gcc" --version | head -n 1'
if ($LASTEXITCODE -ne 0) {
    throw "The PS2DEV compiler could not be verified in '$distribution'."
}

Write-Host "R2Engine PS2 toolchain setup completed in '$distribution'."
