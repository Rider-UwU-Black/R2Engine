param(
    [string]$RuntimeIdentifier = 'win-x64',
    [string]$PackageName = 'R2Engine-Portable-win-x64'
)

$ErrorActionPreference = 'Stop'

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..')).TrimEnd('\')
$artifactsRoot = Join-Path $repositoryRoot 'artifacts'
$packageRoot = Join-Path $artifactsRoot $PackageName
$archivePath = Join-Path $artifactsRoot "$PackageName.zip"

function Confirm-PackagePath([string]$Path) {
    $resolved = [System.IO.Path]::GetFullPath($Path)
    if (-not $resolved.StartsWith($artifactsRoot + '\', [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to modify a path outside the release artifacts folder: $resolved"
    }
    return $resolved
}

function Copy-PortableTree([string]$RelativeSource) {
    $source = Join-Path $repositoryRoot $RelativeSource
    $destination = Join-Path $packageRoot $RelativeSource
    $excludedDirectories = @('bin', 'obj', 'tests', '.git', '.vs', '.idea', '__pycache__')
    $excludedFiles = @('Build-PortableRelease.ps1')

    Get-ChildItem -LiteralPath $source -File -Recurse -Force | ForEach-Object {
        $relative = $_.FullName.Substring($source.TrimEnd('\').Length).TrimStart('\')
        $parts = $relative -split '[\\/]'
        if ($parts | Where-Object { $_ -in $excludedDirectories }) {
            return
        }
        if ($_.Name -in $excludedFiles) {
            return
        }
        $target = Join-Path $destination $relative
        $targetDirectory = Split-Path -Parent $target
        New-Item -ItemType Directory -Path $targetDirectory -Force | Out-Null
        Copy-Item -LiteralPath $_.FullName -Destination $target -Force
    }
}

New-Item -ItemType Directory -Path $artifactsRoot -Force | Out-Null
$packageRoot = Confirm-PackagePath $packageRoot
$archivePath = Confirm-PackagePath $archivePath

if (Test-Path -LiteralPath $packageRoot) {
    Remove-Item -LiteralPath $packageRoot -Recurse -Force
}
if (Test-Path -LiteralPath $archivePath) {
    Remove-Item -LiteralPath $archivePath -Force
}
New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null

dotnet publish (Join-Path $repositoryRoot 'R2Engine.Hub\R2Engine.Hub.csproj') `
    --configuration Release `
    --runtime $RuntimeIdentifier `
    --self-contained true `
    --output $packageRoot
if ($LASTEXITCODE -ne 0) { throw 'The Hub publish failed.' }

$editorOutput = Join-Path $packageRoot 'Editor'
dotnet publish (Join-Path $repositoryRoot 'R2Engine.Editor\R2Engine.Editor.csproj') `
    --configuration Release `
    --runtime $RuntimeIdentifier `
    --self-contained true `
    --output $editorOutput
if ($LASTEXITCODE -ne 0) { throw 'The Editor publish failed.' }

$editorIcons = Join-Path $editorOutput 'Editor\Icons'
New-Item -ItemType Directory -Path $editorIcons -Force | Out-Null
Get-ChildItem -LiteralPath (Join-Path $repositoryRoot 'R2Engine.Editor\Editor\Icons') -File |
    Copy-Item -Destination $editorIcons -Force

foreach ($directory in @(
    'Docs',
    'R2Engine.Editor',
    'R2Engine.Runtime',
    'R2Engine.Desktop',
    'R2Engine.Player',
    'R2Engine.PS2',
    'Tools'
)) {
    Copy-PortableTree $directory
}

foreach ($file in @('README.md', 'QUICKSTART.md', 'LICENSE', 'R2Engine.slnx')) {
    Copy-Item -LiteralPath (Join-Path $repositoryRoot $file) -Destination $packageRoot -Force
}

Set-Content -LiteralPath (Join-Path $packageRoot '.r2portable') `
    -Value 'R2Engine portable user data marker. Do not delete.' `
    -Encoding ASCII

Compress-Archive -LiteralPath $packageRoot -DestinationPath $archivePath -CompressionLevel Optimal

$archive = Get-Item -LiteralPath $archivePath
Write-Host "Portable release created: $($archive.FullName)"
Write-Host ("Archive size: {0:N1} MB" -f ($archive.Length / 1MB))
