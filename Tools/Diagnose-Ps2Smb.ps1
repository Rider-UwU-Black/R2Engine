$ErrorActionPreference = 'Stop'
$resultPath = Join-Path (Split-Path -Parent $PSScriptRoot) 'ps2-smb-diagnostic.txt'

$events = Get-WinEvent -FilterHashtable @{
    LogName = 'Security'
    Id = 4625
    StartTime = (Get-Date).AddMinutes(-20)
} -ErrorAction SilentlyContinue | Select-Object -First 15 TimeCreated, Id, Message

$smbEvents = @()
foreach ($log in @('Microsoft-Windows-SMBServer/Audit', 'Microsoft-Windows-SMBServer/Operational')) {
    if (Get-WinEvent -ListLog $log -ErrorAction SilentlyContinue) {
        $smbEvents += Get-WinEvent -FilterHashtable @{
            LogName = $log
            StartTime = (Get-Date).AddMinutes(-20)
        } -ErrorAction SilentlyContinue | Select-Object -First 15 TimeCreated, Id, Message
    }
}

$account = Get-LocalUser -Name 'r2ps2' | Format-List * | Out-String
$lsa = Get-ItemProperty -LiteralPath 'HKLM:\SYSTEM\CurrentControlSet\Control\Lsa' |
    Select-Object LmCompatibilityLevel, ForceGuest, RestrictAnonymous, RestrictAnonymousSAM |
    Format-List | Out-String

@(
    '=== ACCOUNT ==='
    $account
    '=== LSA POLICY ==='
    $lsa
    '=== LOGON FAILURES ==='
    ($events | Format-List | Out-String)
    '=== SMB EVENTS ==='
    ($smbEvents | Format-List | Out-String)
) | Set-Content -LiteralPath $resultPath
