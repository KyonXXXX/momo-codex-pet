$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$bitmap = [System.Drawing.Bitmap]::new(256, 256)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$graphics.Clear([System.Drawing.Color]::Transparent)
$fill = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(158, 121, 192))
$graphics.FillEllipse($fill, 8, 8, 240, 240)
$points = [System.Drawing.PointF[]]@(
    [System.Drawing.PointF]::new(128, 40), [System.Drawing.PointF]::new(152, 104),
    [System.Drawing.PointF]::new(216, 128), [System.Drawing.PointF]::new(152, 152),
    [System.Drawing.PointF]::new(128, 216), [System.Drawing.PointF]::new(104, 152),
    [System.Drawing.PointF]::new(40, 128), [System.Drawing.PointF]::new(104, 104))
$graphics.FillPolygon([System.Drawing.Brushes]::White, $points)
$stream = [System.IO.MemoryStream]::new()
$bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
$png = $stream.ToArray()
$output = [System.IO.File]::Create((Join-Path (Get-Location) 'assets\momo.ico'))
$writer = [System.IO.BinaryWriter]::new($output)
$writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]1)
$writer.Write([byte]0); $writer.Write([byte]0); $writer.Write([byte]0); $writer.Write([byte]0)
$writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]$png.Length); $writer.Write([uint32]22)
$writer.Write($png)
$writer.Dispose(); $stream.Dispose(); $graphics.Dispose(); $fill.Dispose(); $bitmap.Dispose()
Write-Output 'Created assets/momo.ico'
