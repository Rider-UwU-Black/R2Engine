param(
    [Parameter(Mandatory = $true)][string]$CookedBuildDirectory,
    [Parameter(Mandatory = $true)][string]$SceneName,
    [switch]$ProgressiveScan,
    [switch]$DevelopmentBuild,
    [switch]$DisableVu1
)
$ErrorActionPreference = 'Stop'
function Convert-ToWslPath([string]$value) {
    $full = [IO.Path]::GetFullPath($value)
    if ($full -notmatch '^[A-Za-z]:\\') { throw "Expected a local drive path: $full" }
    return '/mnt/' + $full.Substring(0,1).ToLowerInvariant() + $full.Substring(2).Replace('\','/')
}
$build = (Resolve-Path -LiteralPath $CookedBuildDirectory).Path
$root = Convert-ToWslPath $PSScriptRoot
$nativeArguments = @('FINAL=1')
if ($ProgressiveScan) { $nativeArguments += 'PROGRESSIVE=1' }
if ($DevelopmentBuild) { $nativeArguments += 'DEVELOPMENT=1' }
if ($DisableVu1) { $nativeArguments += 'VU1=0' }
& wsl.exe -d PSBBN -- bash "$root/build-wsl.sh" @nativeArguments
if ($LASTEXITCODE -ne 0) { throw 'PS2 Final native build failed.' }
& wsl.exe -d PSBBN -- python3 "$root/package-iso.py" (Convert-ToWslPath $build) $SceneName
if ($LASTEXITCODE -ne 0) { throw 'PS2 ISO packaging failed. See build output for details.' }
$filename = 'R2GM_000.01.' + [IO.Path]::GetFileNameWithoutExtension($SceneName) + '.iso'
Write-Host ('Exported PS2 ISO: ' + (Join-Path $build ('PS2-Final\' + $filename)))
