# 生成 assets\icon.ico。
#
# 为什么不用 PNG 压缩的 ICO：System.Drawing 的 Icon 类读不了 PNG 压缩的条目，
# 托盘/任务栏小图标会取不到或显示成空白。所以 16~128 一律写经典 BMP(DIB) 条目，
# 只有 256 用 PNG（Windows 只在需要大图标时才读它）。
#
# 用法：
#   pwsh -File tools\make-icon.ps1                          # 用内置占位鲸鱼
#   pwsh -File tools\make-icon.ps1 -Source assets\source-icon.png -CropSize 670 -CropTop 10
[CmdletBinding()]
param(
    [string]$OutPath = (Join-Path $PSScriptRoot '..\assets\icon.ico'),
    [string]$Source = '',
    [string]$PreviewPath = '',
    [int]$CropSize = 0,
    [int]$CropTop = 0
)

Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'

function New-RoundedPath([single]$x, [single]$y, [single]$w, [single]$h, [single]$r) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $path.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $path.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

# 从原图裁出方形并缩放到目标尺寸（可自动避开底部字幕区），再套圆角遮罩
function New-IconBitmapFromSource([System.Drawing.Image]$source, [int]$size) {
    $side = $CropSize
    if ($side -le 0) { $side = [Math]::Min($source.Width, $source.Height) }
    if ($side -gt $source.Width) { $side = $source.Width }
    if ($side -gt $source.Height) { $side = $source.Height }

    $left = [int](($source.Width - $side) / 2)
    $top = $CropTop
    if ($top + $side -gt $source.Height) { $top = $source.Height - $side }
    if ($top -lt 0) { $top = 0 }

    $bitmap = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $destination = New-Object System.Drawing.Rectangle(0, 0, $size, $size)
    $graphics.DrawImage($source, $destination, $left, $top, $side, $side, [System.Drawing.GraphicsUnit]::Pixel)

    # 圆角遮罩：把圆角矩形之外的像素刷成完全透明（配合 Win11 圆角观感）
    $radius = [single]($size * 0.24)
    $mask = New-RoundedPath 0 0 $size $size $radius
    $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
    $graphics.SetClip($mask, [System.Drawing.Drawing2D.CombineMode]::Exclude)
    $transparent = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(0, 0, 0, 0))
    $graphics.FillRectangle($transparent, 0, 0, $size, $size)
    $transparent.Dispose()
    $graphics.ResetClip()
    $mask.Dispose()
    $graphics.Dispose()
    return $bitmap
}

function New-PlaceholderBitmap([int]$size) {
    $bitmap = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.Clear([System.Drawing.Color]::Transparent)

    $pad = [single]($size * 0.055)
    $radius = [single]($size * 0.22)
    $tile = New-RoundedPath $pad $pad ([single]($size - 2 * $pad)) ([single]($size - 2 * $pad)) $radius
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.PointF(0, 0)),
        (New-Object System.Drawing.PointF($size, $size)),
        [System.Drawing.Color]::FromArgb(255, 93, 124, 255),
        [System.Drawing.Color]::FromArgb(255, 43, 71, 214))
    $graphics.FillPath($brush, $tile)

    $scale = [single]($size / 100.0)
    $white = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
    $body = New-Object System.Drawing.Drawing2D.GraphicsPath
    $body.AddBezier([single](18 * $scale), [single](56 * $scale), [single](18 * $scale), [single](34 * $scale), [single](44 * $scale), [single](28 * $scale), [single](62 * $scale), [single](34 * $scale))
    $body.AddBezier([single](62 * $scale), [single](34 * $scale), [single](78 * $scale), [single](39 * $scale), [single](80 * $scale), [single](56 * $scale), [single](66 * $scale), [single](66 * $scale))
    $body.AddBezier([single](66 * $scale), [single](66 * $scale), [single](48 * $scale), [single](74 * $scale), [single](24 * $scale), [single](72 * $scale), [single](18 * $scale), [single](56 * $scale))
    $body.CloseFigure()
    $graphics.FillPath($white, $body)

    $tail = New-Object System.Drawing.Drawing2D.GraphicsPath
    [System.Drawing.PointF[]]$tailPoints = @(
        (New-Object System.Drawing.PointF([single](78 * $scale), [single](44 * $scale))),
        (New-Object System.Drawing.PointF([single](95 * $scale), [single](30 * $scale))),
        (New-Object System.Drawing.PointF([single](91 * $scale), [single](50 * $scale))),
        (New-Object System.Drawing.PointF([single](97 * $scale), [single](63 * $scale))),
        (New-Object System.Drawing.PointF([single](76 * $scale), [single](58 * $scale)))
    )
    $tail.AddPolygon($tailPoints)
    $graphics.FillPath($white, $tail)

    $eyeBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 43, 71, 214))
    $graphics.FillEllipse($eyeBrush, [single](30 * $scale), [single](45 * $scale), [single](7 * $scale), [single](7 * $scale))
    $graphics.FillEllipse($white, [single](38 * $scale), [single](14 * $scale), [single](6 * $scale), [single](6 * $scale))
    $graphics.FillEllipse($white, [single](46 * $scale), [single](9 * $scale), [single](5 * $scale), [single](5 * $scale))

    $eyeBrush.Dispose(); $white.Dispose(); $brush.Dispose(); $tile.Dispose(); $body.Dispose(); $tail.Dispose(); $graphics.Dispose()
    return $bitmap
}

# 位图 → ICO 里的 BMP(DIB) 条目：40 字节头 + BGRA 倒序像素 + AND 掩码
function ConvertTo-IcoBmpBytes([System.Drawing.Bitmap]$bitmap) {
    $width = $bitmap.Width
    $height = $bitmap.Height
    $stream = New-Object System.IO.MemoryStream
    $writer = New-Object System.IO.BinaryWriter($stream)

    $writer.Write([UInt32]40)
    $writer.Write([Int32]$width)
    $writer.Write([Int32]($height * 2))
    $writer.Write([UInt16]1)
    $writer.Write([UInt16]32)
    $writer.Write([UInt32]0)
    $writer.Write([UInt32]($width * $height * 4))
    $writer.Write([Int32]0); $writer.Write([Int32]0)
    $writer.Write([UInt32]0); $writer.Write([UInt32]0)

    for ($y = $height - 1; $y -ge 0; $y--) {
        for ($x = 0; $x -lt $width; $x++) {
            $color = $bitmap.GetPixel($x, $y)
            $writer.Write([Byte]$color.B)
            $writer.Write([Byte]$color.G)
            $writer.Write([Byte]$color.R)
            $writer.Write([Byte]$color.A)
        }
    }

    $maskRowBytes = [int]([Math]::Floor(($width + 31) / 32) * 4)
    $zeroRow = New-Object byte[] $maskRowBytes
    for ($y = 0; $y -lt $height; $y++) { $writer.Write($zeroRow) }

    $writer.Flush()
    $writer.Dispose()
    return $stream.ToArray()
}

function ConvertTo-IcoPngBytes([System.Drawing.Bitmap]$bitmap) {
    $stream = New-Object System.IO.MemoryStream
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $bytes = $stream.ToArray()
    $stream.Dispose()
    return $bytes
}

$bmpSizes = @(16, 20, 24, 32, 48, 64, 128)
$pngSizes = @(256)

$sourceImage = $null
if ($Source -ne '' -and (Test-Path $Source)) {
    $sourceImage = [System.Drawing.Image]::FromFile((Resolve-Path $Source).Path)
    Write-Output ("源图 {0}  {1}x{2}" -f (Split-Path -Leaf $Source), $sourceImage.Width, $sourceImage.Height)
}

$entries = @()
foreach ($size in $bmpSizes) {
    if ($sourceImage) { $bitmap = New-IconBitmapFromSource $sourceImage $size } else { $bitmap = New-PlaceholderBitmap $size }
    $entries += , @{ Size = $size; Bytes = (ConvertTo-IcoBmpBytes $bitmap); IsPng = $false }
    $bitmap.Dispose()
}
foreach ($size in $pngSizes) {
    if ($sourceImage) { $bitmap = New-IconBitmapFromSource $sourceImage $size } else { $bitmap = New-PlaceholderBitmap $size }
    $entries += , @{ Size = $size; Bytes = (ConvertTo-IcoPngBytes $bitmap); IsPng = $true }
    if ($PreviewPath -ne '') { $bitmap.Save($PreviewPath, [System.Drawing.Imaging.ImageFormat]::Png) }
    $bitmap.Dispose()
}
if ($sourceImage) { $sourceImage.Dispose() }

$directory = Split-Path -Parent $OutPath
if (-not (Test-Path $directory)) { New-Item -ItemType Directory -Path $directory -Force | Out-Null }

$file = [System.IO.File]::Create($OutPath)
$writer = New-Object System.IO.BinaryWriter($file)
$writer.Write([UInt16]0)
$writer.Write([UInt16]1)
$writer.Write([UInt16]$entries.Count)

$offset = 6 + 16 * $entries.Count
foreach ($entry in $entries) {
    $dimension = 0
    if ($entry.Size -lt 256) { $dimension = $entry.Size }
    $writer.Write([Byte]$dimension)
    $writer.Write([Byte]$dimension)
    $writer.Write([Byte]0)
    $writer.Write([Byte]0)
    $writer.Write([UInt16]1)
    $writer.Write([UInt16]32)
    $writer.Write([UInt32]$entry.Bytes.Length)
    $writer.Write([UInt32]$offset)
    $offset += $entry.Bytes.Length
}
foreach ($entry in $entries) { $writer.Write([Byte[]]$entry.Bytes) }
$writer.Flush(); $writer.Dispose(); $file.Dispose()

Write-Output ("icon 已生成: {0}  ({1} 字节, 尺寸 {2})" -f $OutPath, (Get-Item $OutPath).Length, (($entries | ForEach-Object { $_.Size }) -join '/'))
