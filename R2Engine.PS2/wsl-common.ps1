function Get-R2WslDistribution {
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

    if (-not [string]::IsNullOrWhiteSpace($env:R2ENGINE_WSL_DISTRO)) {
        if ([string]::Equals($env:R2ENGINE_WSL_DISTRO, 'PSBBN', [StringComparison]::OrdinalIgnoreCase)) {
            throw 'R2ENGINE_WSL_DISTRO cannot be PSBBN. R2Engine requires a separate general-purpose Linux distribution.'
        }
        $configured = $distributions | Where-Object {
            [string]::Equals($_, $env:R2ENGINE_WSL_DISTRO, [StringComparison]::OrdinalIgnoreCase)
        } | Select-Object -First 1
        if ($null -eq $configured) {
            throw "Configured R2ENGINE_WSL_DISTRO '$env:R2ENGINE_WSL_DISTRO' is not installed."
        }
        return $configured
    }

    $eligible = @($distributions | Where-Object {
        -not [string]::Equals($_, 'PSBBN', [StringComparison]::OrdinalIgnoreCase)
    })
    if ($eligible.Count -eq 0) {
        throw 'PS2 build prerequisites missing: install a separate Ubuntu WSL distribution. R2Engine will not use the PSBBN distribution.'
    }

    $dedicated = $eligible | Where-Object {
        [string]::Equals($_, 'R2Engine-PS2', [StringComparison]::OrdinalIgnoreCase)
    } | Select-Object -First 1
    if ($null -ne $dedicated) { return $dedicated }
    return $eligible[0]
}
