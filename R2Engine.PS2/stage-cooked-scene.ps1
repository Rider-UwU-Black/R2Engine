param(
    [Parameter(Mandatory = $true)]
    [string]$BuildDirectory,
    [Parameter(Mandatory = $true)]
    [string]$SceneName
)

$ErrorActionPreference = 'Stop'

$resolvedBuild = (Resolve-Path -LiteralPath $BuildDirectory).Path
$dataRoot = Join-Path $resolvedBuild 'R2Data'
$sceneFileName = if ([System.IO.Path]::GetExtension($SceneName)) { $SceneName } else { "$SceneName.r2scene" }
$sceneSource = Join-Path (Join-Path $dataRoot 'Scenes') $sceneFileName
if (-not (Test-Path -LiteralPath $sceneSource)) {
    throw "Cooked scene '$sceneSource' was not found. Build the project in the editor first."
}

$assetRoot = Join-Path $PSScriptRoot 'bin\assets'
New-Item -ItemType Directory -Force -Path $assetRoot | Out-Null
Copy-Item -LiteralPath $sceneSource -Destination (Join-Path $assetRoot 'probe.r2scene') -Force

Get-ChildItem -LiteralPath (Join-Path $dataRoot 'Scenes') -Filter '*.r2scene' -File | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $assetRoot $_.Name) -Force
}

# R2SC asset table entries retain their project-relative paths. Merge the cooked
# mesh and texture trees into the HostFS root so those paths resolve unchanged.
foreach ($packageFolder in @('Meshes', 'Textures')) {
    $sourceRoot = Join-Path $dataRoot $packageFolder
    if (-not (Test-Path -LiteralPath $sourceRoot)) { continue }
    Get-ChildItem -LiteralPath $sourceRoot -File -Recurse | ForEach-Object {
        # Windows PowerShell 5.1 runs on .NET Framework, which does not expose
        # Path.GetRelativePath. Every enumerated file is guaranteed to be below
        # sourceRoot, so removing that resolved prefix is equivalent here.
        $relative = $_.FullName.Substring($sourceRoot.Length).TrimStart([char[]]@('\', '/'))
        $destination = Join-Path $assetRoot $relative
        $destinationDirectory = Split-Path -Parent $destination
        New-Item -ItemType Directory -Force -Path $destinationDirectory | Out-Null
        Copy-Item -LiteralPath $_.FullName -Destination $destination -Force
    }
}

# Audio Sources retain their project-relative Assets/Audio paths in R2SC v9.
$audioSourceRoot = Join-Path $resolvedBuild 'Assets\Audio'
if (Test-Path -LiteralPath $audioSourceRoot) {
    Get-ChildItem -LiteralPath $audioSourceRoot -File -Recurse | ForEach-Object {
        $relative = $_.FullName.Substring($resolvedBuild.Length).TrimStart([char[]]@('\', '/'))
        $destination = Join-Path $assetRoot $relative
        $destinationDirectory = Split-Path -Parent $destination
        New-Item -ItemType Directory -Force -Path $destinationDirectory | Out-Null
        Copy-Item -LiteralPath $_.FullName -Destination $destination -Force
    }

    $audioDestination = Join-Path $assetRoot 'Assets\Audio'
    $resolvedAudio = (Resolve-Path -LiteralPath $audioDestination).Path
    $drive = $resolvedAudio.Substring(0, 1).ToLowerInvariant()
    $relativeAudio = $resolvedAudio.Substring(2).Replace('\', '/')
    $wslAudio = "/mnt/$drive$relativeAudio"
    $resolvedScript = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot 'prepare-ui-audio.py')).Path
    $scriptDrive = $resolvedScript.Substring(0, 1).ToLowerInvariant()
    $scriptRelative = $resolvedScript.Substring(2).Replace('\', '/')
    . (Join-Path $PSScriptRoot 'wsl-common.ps1')
    $wslDistribution = Get-R2WslDistribution
    & wsl.exe -d $wslDistribution -- python3 "/mnt/$scriptDrive$scriptRelative" $wslAudio
    if ($LASTEXITCODE -ne 0) { throw 'PS2 UI audio conversion failed.' }
}

Write-Host "Staged cooked scene '$sceneFileName' from '$resolvedBuild' for PS2 HostFS."

$savePresentation = Join-Path $dataRoot 'Save'
$saveDestination = Join-Path $assetRoot 'Save'
New-Item -ItemType Directory -Force -Path $saveDestination | Out-Null
foreach ($saveFile in @('icon.sys', 'save.icn', 'identity.bin')) {
    $saveSource = Join-Path $savePresentation $saveFile
    if (-not (Test-Path -LiteralPath $saveSource)) { throw "Missing PS2 save presentation '$saveSource'. Rebuild in the editor." }
    Copy-Item -LiteralPath $saveSource -Destination (Join-Path $saveDestination $saveFile) -Force
}
