param(
    [Parameter(Mandatory = $true)][string]$Archive,
    [Parameter(Mandatory = $true)][string]$InstallRoot,
    [Parameter(Mandatory = $true)][string]$HubExecutable,
    [Parameter(Mandatory = $true)][int]$HubProcessId
)

$ErrorActionPreference = 'Stop'

try {
    for ($attempt = 0; $attempt -lt 60; $attempt++) {
        if ($null -eq (Get-Process -Id $HubProcessId -ErrorAction SilentlyContinue)) { break }
        Start-Sleep -Milliseconds 500
    }
    if ($null -ne (Get-Process -Id $HubProcessId -ErrorAction SilentlyContinue)) {
        throw 'The Hub did not close in time. Close it and try the update again.'
    }
    $staging = Join-Path ([System.IO.Path]::GetTempPath()) ("R2Engine-update-" + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $staging -Force | Out-Null
    Expand-Archive -LiteralPath $Archive -DestinationPath $staging -Force

    $source = Get-ChildItem -LiteralPath $staging -Directory | Select-Object -First 1
    if ($null -eq $source -or -not (Test-Path -LiteralPath (Join-Path $source.FullName 'R2Engine.Hub.exe'))) {
        throw 'The downloaded release does not contain the expected R2Engine Hub.'
    }

    Get-ChildItem -LiteralPath $source.FullName -Force | Where-Object { $_.Name -ne 'UserData' } | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $InstallRoot -Recurse -Force
    }

    Remove-Item -LiteralPath $staging -Recurse -Force
    Remove-Item -LiteralPath $Archive -Force
    Start-Process -FilePath $HubExecutable -WorkingDirectory $InstallRoot
}
catch {
    Add-Type -AssemblyName System.Windows.Forms
    [System.Windows.Forms.MessageBox]::Show(
        "R2Engine could not install the update.`n`n$($_.Exception.Message)",
        'R2Engine Update Failed',
        [System.Windows.Forms.MessageBoxButtons]::OK,
        [System.Windows.Forms.MessageBoxIcon]::Error) | Out-Null
    exit 1
}
