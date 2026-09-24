param(
    [string]$ShareRoot = (Join-Path (Split-Path -Parent $PSScriptRoot) 'PS2SMB'),
    [string]$UserName = 'r2ps2',
    [SecureString]$Password,
    [string]$ResultPath = (Join-Path (Split-Path -Parent $PSScriptRoot) 'ps2-smb-setup-result.txt')
)

$ErrorActionPreference = 'Stop'
trap {
    ("FAILED`r`n" + ($_ | Out-String)) | Set-Content -LiteralPath $ResultPath
    exit 1
}

$feature = Get-WindowsOptionalFeature -Online -FeatureName SMB1Protocol-Server
$restartRequired = $false
if ($feature.State -eq 'EnablePending') {
    $restartRequired = $true
} elseif ($feature.State -ne 'Enabled') {
    $result = Enable-WindowsOptionalFeature -Online -FeatureName SMB1Protocol-Server -All -NoRestart
    $restartRequired = $result.RestartNeeded
}

if ($restartRequired) {
    @"
PENDING RESTART
SMB1 server-only was enabled, but Windows must restart before the PS2 share can be created.
"@ | Set-Content -LiteralPath $ResultPath
    exit 0
}

Set-SmbServerConfiguration -EnableSMB1Protocol $true -RequireSecuritySignature $false -Force | Out-Null

if (Test-Path -LiteralPath $ShareRoot) {
    & takeown.exe /F $ShareRoot /R /D Y | Out-Null
    & icacls.exe $ShareRoot /reset /T /C | Out-Null
}
New-Item -ItemType Directory -Path (Join-Path $ShareRoot 'DVD') -Force | Out-Null

$account = Get-LocalUser -Name $UserName -ErrorAction SilentlyContinue
if ($null -eq $account) {
    if ($null -eq $Password) {
        $Password = Read-Host "Create a password for the local PS2 SMB account '$UserName'" -AsSecureString
    }
    New-LocalUser -Name $UserName -Password $Password `
        -AccountNeverExpires -PasswordNeverExpires `
        -Description 'Read-only R2Engine PS2 network account' | Out-Null
}

$qualifiedUser = "$env:COMPUTERNAME\$UserName"
$editorUser = "$env:USERDOMAIN\$env:USERNAME"
& icacls.exe $ShareRoot /inheritance:r | Out-Null
& icacls.exe $ShareRoot /grant:r "${qualifiedUser}:(OI)(CI)RX" "${editorUser}:(OI)(CI)M" "*S-1-5-18:(OI)(CI)F" "*S-1-5-32-544:(OI)(CI)F" | Out-Null

$existingShare = Get-SmbShare -Name 'PS2SMB' -ErrorAction SilentlyContinue
if ($null -eq $existingShare) {
    & net.exe share "PS2SMB=$ShareRoot" "/GRANT:$qualifiedUser,READ" | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Windows could not create the PS2SMB share (net.exe exit code $LASTEXITCODE)."
    }
} elseif ($existingShare.Path -ne $ShareRoot) {
    throw "An SMB share named PS2SMB already points to $($existingShare.Path)."
} else {
    Grant-SmbShareAccess -Name 'PS2SMB' -AccountName $qualifiedUser -AccessRight Read -Force | Out-Null
}

$ruleName = 'R2Engine PS2 SMB (local subnet only)'
Remove-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue
New-NetFirewallRule -DisplayName $ruleName -Direction Inbound -Action Allow `
    -Protocol TCP -LocalPort 445 -RemoteAddress '192.168.0.0/24' `
    -Profile Private -Program System | Out-Null

$result = [PSCustomObject]@{
    Share = "\\$env:COMPUTERNAME\PS2SMB"
    User = $UserName
    RestartRequired = $restartRequired
}
$result | Format-List | Out-String | Set-Content -LiteralPath $ResultPath
$result
