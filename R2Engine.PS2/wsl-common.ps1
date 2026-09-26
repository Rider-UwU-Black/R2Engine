function Get-R2UbuntuDistribution {
    $wsl = Get-Command 'wsl.exe' -ErrorAction SilentlyContinue
    if ($null -eq $wsl) {
        throw 'PS2 build prerequisites missing: Windows Subsystem for Linux (WSL) is not installed.'
    }

    $previousErrorPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $probeOutput = @(& wsl.exe --list --quiet 2>&1)
        $probeExitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorPreference
    }

    $distributions = @($probeOutput |
        ForEach-Object { ($_ -replace "`0", '').Trim() } |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    if ($probeExitCode -ne 0 -or $distributions.Count -eq 0) {
        throw 'PS2 build prerequisites missing: WSL has no initialized Linux distribution. Install and launch a WSL distribution once, then install PS2DEV inside it.'
    }

    $ubuntu = $distributions | Where-Object {
        $_.StartsWith('Ubuntu', [StringComparison]::OrdinalIgnoreCase)
    } | Select-Object -First 1
    if ($null -eq $ubuntu) {
        throw 'PS2 build prerequisites missing: install and initialize Ubuntu under WSL, then run R2Engine.PS2/setup-toolchain.ps1.'
    }
    return $ubuntu
}
