$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$testRoot = Join-Path $repo ('Builds/FeedbackTest-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($testRoot) | Out-Null
$scene = Get-Content (Join-Path $repo 'R2Engine.Editor/Scenes/scene1.r2scene') -Raw | ConvertFrom-Json
$button = $scene.GameObjects | Where-Object { $_.UIButton } | Select-Object -First 1
if (-not $button) { throw 'Fixture needs a button' }
$button.UIButton | Add-Member -Force NoteProperty SpriteBorders ([pscustomobject]@{X=10; Y=12; Z=14; W=16})
$button.UIButton.SortOrder = 2147483646
$scene.GameObjects += [pscustomobject]@{
    Name = 'Feedback test label'; IsActive = $true
    Scale = @{ X = 1; Y = 1; Z = 1 }
    UIText = @{
        Text = ''; Ps2SaveLoadFeedback = $true
        SavedMessage = 'Custom saved'; SaveFailedMessage = 'Custom save failure'
        LoadedMessage = 'Custom loaded'; LoadFailedMessage = 'Custom load failure'
        SortOrder = 2147483647
    }
}
$fixture = Join-Path $testRoot 'Feedback.r2scene'
[IO.File]::WriteAllText($fixture, ($scene | ConvertTo-Json -Depth 100))
& dotnet (Join-Path $repo 'R2Engine.Editor/bin/Release/net10.0/R2Engine.Editor.dll') --cook-ps2-build $testRoot $fixture (Join-Path $repo 'R2Engine.Editor/Scenes/scene2.r2scene')
if ($LASTEXITCODE -ne 0) { throw 'Fixture cook failed' }
$bytes = [IO.File]::ReadAllBytes((Join-Path $testRoot 'R2Data/Scenes/Feedback.r2scene'))
if ([BitConverter]::ToUInt32($bytes, 4) -ne 24) { throw 'Expected R2SC v24' }
# v16 adds 64 bytes of nine-slice destination/UV borders to the v15 UI record.
$record = $bytes.Length - ([BitConverter]::ToUInt32($bytes, 16) * 8) - 648
if ([BitConverter]::ToUInt32($bytes, $record) -ne 3) { throw 'Expected UIText as final record' }
if ($bytes[$record + 80] -ne 0) { throw 'Blank initial text did not survive cooking' }
if ([BitConverter]::ToUInt32($bytes, $record + 180) -ne 1) { throw 'Feedback flag missing' }
$expected = @('Custom saved', 'Custom save failure', 'Custom loaded', 'Custom load failure')
for ($i = 0; $i -lt 4; ++$i) {
    $actual = [Text.Encoding]::UTF8.GetString($bytes, $record + 184 + $i * 64, 64).TrimEnd([char]0)
    if ($actual -ne $expected[$i]) { throw "Feedback message $i did not survive cooking: $actual" }
}
$buttonRecord = $record - 648
if ([BitConverter]::ToUInt32($bytes, $buttonRecord) -ne 2) { throw 'Expected sorted button before final label' }
$margins = @(10,12,14,16)
for ($i = 0; $i -lt 4; ++$i) {
    if ([BitConverter]::ToSingle($bytes, $buttonRecord + 440 + $i*4) -ne $margins[$i]) { throw 'Authored border lost in cook' }
    $uv = [BitConverter]::ToSingle($bytes, $buttonRecord + 456 + $i*4)
    if ($uv -le 0 -or $uv -ge 1) { throw 'Expected normalized source-image UV border' }
}
Write-Output 'PASS: authored button margins and normalized UV borders survive cooking.'
Write-Output "PASS: v16 record, blank initial text, feedback flag and all four authored messages. Fixture retained at $testRoot"
