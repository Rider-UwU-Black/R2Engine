$ErrorActionPreference = 'Stop'
$resultPath = Join-Path (Split-Path -Parent $PSScriptRoot) 'ps2-smb-password-reset.txt'
$password = Read-Host "Enter a new password for the local PS2 SMB account 'r2ps2'" -AsSecureString

Set-LocalUser -Name 'r2ps2' -Password $password

$user = Get-LocalUser -Name 'r2ps2'
@(
    "Reset completed: $(Get-Date -Format o)"
    "Account: $($user.Name)"
    "Enabled: $($user.Enabled)"
) | Set-Content -LiteralPath $resultPath -Encoding UTF8
