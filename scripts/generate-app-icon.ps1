$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Add-Type -AssemblyName System.Drawing

$assetDirectory = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\src\AIVitals.App\Assets'))
$iconPath = Join-Path $assetDirectory 'AppIcon.ico'
$previewPath = Join-Path $assetDirectory 'AppIcon.png'
$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)

function New-RingsBitmap([int]$size) {
    $bitmap = [Drawing.Bitmap]::new($size, $size, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.Clear([Drawing.Color]::Transparent)
        $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.CompositingQuality = [Drawing.Drawing2D.CompositingQuality]::HighQuality

        $rings = @(
            @{ Radius = 0.405; Sweep = 304.0; Color = [Drawing.ColorTranslator]::FromHtml('#25DCCD') },
            @{ Radius = 0.285; Sweep = 266.0; Color = [Drawing.ColorTranslator]::FromHtml('#3D8BFF') },
            @{ Radius = 0.165; Sweep = 226.0; Color = [Drawing.ColorTranslator]::FromHtml('#FF8A4C') }
        )
        $stroke = [Math]::Max(1.4, $size * 0.105)
        foreach ($ring in $rings) {
            $radius = $size * $ring.Radius
            $bounds = [Drawing.RectangleF]::new($size / 2 - $radius, $size / 2 - $radius, $radius * 2, $radius * 2)
            $track = [Drawing.Pen]::new([Drawing.Color]::FromArgb(110, 23, 41, 58), $stroke)
            $fill = [Drawing.Pen]::new($ring.Color, $stroke)
            try {
                $track.StartCap = $track.EndCap = [Drawing.Drawing2D.LineCap]::Round
                $fill.StartCap = $fill.EndCap = [Drawing.Drawing2D.LineCap]::Round
                $graphics.DrawEllipse($track, $bounds)
                $graphics.DrawArc($fill, $bounds, -90.0, $ring.Sweep)
            }
            finally {
                $track.Dispose()
                $fill.Dispose()
            }
        }
    }
    finally {
        $graphics.Dispose()
    }
    return $bitmap
}

$pngFrames = @()
foreach ($size in $sizes) {
    $bitmap = New-RingsBitmap $size
    try {
        $memory = [IO.MemoryStream]::new()
        $bitmap.Save($memory, [Drawing.Imaging.ImageFormat]::Png)
        $pngFrames += ,$memory.ToArray()
        if ($size -eq 256) { $bitmap.Save($previewPath, [Drawing.Imaging.ImageFormat]::Png) }
        $memory.Dispose()
    }
    finally {
        $bitmap.Dispose()
    }
}

$stream = [IO.File]::Open($iconPath, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::None)
$writer = [IO.BinaryWriter]::new($stream)
try {
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($index = 0; $index -lt $sizes.Count; $index++) {
        $size = $sizes[$index]
        $writer.Write([byte]$(if ($size -ge 256) { 0 } else { $size }))
        $writer.Write([byte]$(if ($size -ge 256) { 0 } else { $size }))
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$pngFrames[$index].Length)
        $writer.Write([uint32]$offset)
        $offset += $pngFrames[$index].Length
    }
    foreach ($frame in $pngFrames) { $writer.Write($frame) }
}
finally {
    $writer.Dispose()
    $stream.Dispose()
}

Write-Output "Generated $iconPath and $previewPath"
