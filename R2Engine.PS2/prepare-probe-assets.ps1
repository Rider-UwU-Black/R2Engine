$ErrorActionPreference = 'Stop'
$assetRoot = Join-Path $PSScriptRoot 'bin\assets'
New-Item -ItemType Directory -Force -Path $assetRoot | Out-Null

$texturePath = Join-Path $assetRoot 'probe.r2tex'
$stream = [System.IO.File]::Create($texturePath)
$writer = [System.IO.BinaryWriter]::new($stream)
try {
    $writer.Write([System.Text.Encoding]::ASCII.GetBytes('R2TX'))
    foreach ($value in @(2, 3, 4, 4, 1, 16, 0)) { $writer.Write([int]$value) }
    for ($index = 0; $index -lt 16; $index++) {
        $writer.Write([byte]((($index -shr 2) -band 3) * 255 / 3))
        $writer.Write([byte]((($index -shr 1) -band 1) * 255))
        $writer.Write([byte](($index -band 1) * 255)); $writer.Write([byte]255)
    }
    foreach ($value in @(4, 4, 8)) { $writer.Write([int]$value) }
    for ($pixel = 0; $pixel -lt 16; $pixel += 2) {
        $firstBright = (([math]::Floor($pixel / 4) + ($pixel % 4)) % 2) -eq 1
        $second = $pixel + 1
        $secondBright = (([math]::Floor($second / 4) + ($second % 4)) % 2) -eq 1
        $low = $(if ($firstBright) { 3 } else { 1 })
        $high = $(if ($secondBright) { 3 } else { 1 })
        $writer.Write([byte]($low -bor ($high -shl 4)))
    }
}
finally { $writer.Dispose(); $stream.Dispose() }

$meshPath = Join-Path $assetRoot 'probe.r2mesh'
$stream = [System.IO.File]::Create($meshPath)
$writer = [System.IO.BinaryWriter]::new($stream)
try {
    $writer.Write([System.Text.Encoding]::ASCII.GetBytes('R2MS'))
    foreach ($value in @(1, 24, 36, 32, 2)) { $writer.Write([int]$value) }
    foreach ($value in @(-1,-1,-1, 1,1,1)) { $writer.Write([single]$value) }
    $faces = @(
        @{ N=@(0,0,-1); P=@(-1,-1,-1, -1,1,-1, 1,1,-1, 1,-1,-1) },
        @{ N=@(0,0, 1); P=@(-1,-1, 1,  1,-1,1,  1,1,1, -1, 1, 1) },
        @{ N=@(-1,0,0); P=@(-1,-1,-1, -1,-1,1, -1,1,1, -1, 1,-1) },
        @{ N=@( 1,0,0); P=@( 1,-1,-1,  1, 1,-1, 1,1,1,  1,-1, 1) },
        @{ N=@(0,-1,0); P=@(-1,-1,-1,  1,-1,-1, 1,-1,1, -1,-1, 1) },
        @{ N=@(0, 1,0); P=@(-1, 1,-1, -1, 1, 1, 1, 1,1,  1, 1,-1) }
    )
    foreach ($face in $faces) {
        for ($vertex = 0; $vertex -lt 4; $vertex++) {
            $positionOffset = $vertex * 3
            foreach ($value in @(
                $face.P[$positionOffset], $face.P[$positionOffset + 1], $face.P[$positionOffset + 2],
                $face.N[0], $face.N[1], $face.N[2])) { $writer.Write([single]$value) }
            $u = $(if ($vertex -ge 2) { 1.0 } else { 0.0 })
            $v = $(if ($vertex -eq 0 -or $vertex -eq 3) { 1.0 } else { 0.0 })
            $writer.Write([single]$u); $writer.Write([single]$v)
        }
    }
    for ($face = 0; $face -lt 6; $face++) {
        $base = $face * 4
        foreach ($index in @($base,($base+1),($base+2),$base,($base+2),($base+3))) { $writer.Write([uint16]$index) }
    }
}
finally { $writer.Dispose(); $stream.Dispose() }

$path = Join-Path $assetRoot 'probe.r2scene'
$stream = [System.IO.File]::Create($path)
$writer = [System.IO.BinaryWriter]::new($stream)
try {
    $writer.Write([System.Text.Encoding]::ASCII.GetBytes('R2SC'))
    $writer.Write([int]5); $writer.Write([int]1); $writer.Write([int]1); $writer.Write([int]2)
    foreach ($value in @(0.0,0.0,6.5, 0.0,0.0,0.0, 60.0,0.1,1000.0, (4.0/3.0))) {
        $writer.Write([single]$value)
    }
    foreach ($value in @(0.012,0.027,0.071, 0.28,0.28,0.28, -0.35,0.75,0.56, 1.0,1.0,1.0, 0.72)) {
        $writer.Write([single]$value)
    }
    $writer.Write([uint32]1)
    foreach ($value in @(0.45,0.50,0.58, 20.0,80.0)) { $writer.Write([single]$value) }
    $writer.Write([uint32]0)
    foreach ($name in @('probe.r2mesh', 'probe.r2tex')) {
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($name)
        $writer.Write($bytes.Length); $writer.Write($bytes)
    }
    $instances = @(
        @{ Position = @(-1.65, 0.0, 0.0); Rotation = @(0.0, 0.0, 0.0); Scale = @(0.62, 0.62, 0.62); Color = @(1.0,1.0,1.0,1.0) },
        @{ Position = @( 1.65, 0.0, 0.0); Rotation = @(0.0, 45.0, 0.0); Scale = @(0.62,0.62,0.62); Color = @(0.55,1.0,0.65,1.0) }
    )
    foreach ($instance in $instances) {
        $writer.Write([uint32]0); $writer.Write([uint32]0)
        foreach ($value in $instance.Position + $instance.Rotation + $instance.Scale) { $writer.Write([single]$value) }
        foreach ($value in $instance.Color) { $writer.Write([single]$value) }
    }
    foreach ($instance in $instances) {
        foreach ($value in @([uint32]1,[uint32]0,[uint32]0,[uint32]1)) { $writer.Write($value) }
        $writer.Write([single]0.5)
    }
}
finally { $writer.Dispose(); $stream.Dispose() }
Write-Host "Prepared external R2SC/R2MS/R2TX probe assets in $assetRoot"
