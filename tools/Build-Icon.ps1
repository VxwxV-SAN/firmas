Add-Type -AssemblyName System.Drawing

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$iconPath = Join-Path $root 'PdfEditor\Resources\app.ico'
$pngSourcePath = Join-Path $root 'PdfEditor\Resources\app.png'
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $iconPath) | Out-Null

# Tamaños habituales en iconos de Windows: 16, 24, 32, 48, 64, 128, 256.
$sizes = 16, 24, 32, 48, 64, 128, 256

function New-IconBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit

    # Fondo con esquinas redondeadas y degradado (tono azul oscuro / pizarra).
    $radius = [int]([Math]::Max(2, [Math]::Round($size * 0.18)))
    $rect = New-Object System.Drawing.RectangleF 0.0, 0.0, ([float]$size), ([float]$size)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = [float]$radius
    $path.AddArc($rect.X, $rect.Y, $d, $d, 180, 90)
    $path.AddArc(($rect.X + $rect.Width - $d), $rect.Y, $d, $d, 270, 90)
    $path.AddArc(($rect.X + $rect.Width - $d), ($rect.Y + $rect.Height - $d), $d, $d, 0, 90)
    $path.AddArc($rect.X, ($rect.Y + $rect.Height - $d), $d, $d, 90, 90)
    $path.CloseFigure()
    $g.SetClip($path)

    $bgRect = New-Object System.Drawing.RectangleF 0.0, 0.0, ([float]$size), ([float]$size)
    $bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        $bgRect,
        [System.Drawing.Color]::FromArgb(255, 33, 49, 75),
        [System.Drawing.Color]::FromArgb(255, 18, 26, 45),
        [System.Drawing.Drawing2D.LinearGradientMode]::Vertical)
    $g.FillRectangle($bg, $bgRect)
    $g.ResetClip()

    # Hoja de documento con esquina doblada.
    $m = [float]($size * 0.18)
    $fold = [float]($size * 0.22)
    $docRect = New-Object System.Drawing.RectangleF ($m), ($m), ($size - 2 * $m), ($size - 2 * $m)
    $docPath = New-Object System.Drawing.Drawing2D.GraphicsPath
    $docPath.AddLine($docRect.X, $docRect.Y, ($docRect.Right - $fold), $docRect.Y)
    $docPath.AddLine(($docRect.Right - $fold), $docRect.Y, $docRect.Right, ($docRect.Y + $fold))
    $docPath.AddLine($docRect.Right, ($docRect.Y + $fold), $docRect.Right, $docRect.Bottom)
    $docPath.AddLine($docRect.Right, $docRect.Bottom, $docRect.X, $docRect.Bottom)
    $docPath.AddLine($docRect.X, $docRect.Bottom, $docRect.X, $docRect.Y)
    $docPath.CloseFigure()
    $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 240, 244, 252))), $docPath)

    # Sombra de la esquina doblada.
    $foldPath = New-Object System.Drawing.Drawing2D.GraphicsPath
    $foldPath.AddLine(($docRect.Right - $fold), $docRect.Y, ($docRect.Right - $fold), ($docRect.Y + $fold))
    $foldPath.AddLine(($docRect.Right - $fold), ($docRect.Y + $fold), $docRect.Right, ($docRect.Y + $fold))
    $foldPath.CloseFigure()
    $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 200, 210, 228))), $foldPath)

    # Líneas de texto del documento.
    $lineColor = [System.Drawing.Color]::FromArgb(255, 150, 162, 188)
    $linePen = New-Object System.Drawing.Pen $lineColor, ([Math]::Max(1.0, [float]($size * 0.035)))
    $linePen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $linePen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $linePen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $lineLeft = $docRect.X + ($docRect.Width * 0.12)
    $lineRightBase = $docRect.X + ($docRect.Width * 0.62)
    $lineCount = 4
    $lineTop = $docRect.Y + ($docRect.Height * 0.20)
    $lineStep = ($docRect.Height * 0.13)
    for ($i = 0; $i -lt $lineCount; $i++) {
        $right = if ($i -eq ($lineCount - 1)) { $lineRightBase * 0.55 } else { $lineRightBase }
        $y = $lineTop + ($i * $lineStep)
        $g.DrawLine($linePen, $lineLeft, $y, $right, $y)
    }

    # Firma manuscrita sobre las líneas.
    $sigPen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 60, 110, 230)), ([Math]::Max(1.5, [float]($size * 0.075)))
    $sigPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $sigPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $sigPen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $sigY = $docRect.Y + ($docRect.Height * 0.74)
    $sigLeft = $docRect.X + ($docRect.Width * 0.12)
    $sigRight = $docRect.X + ($docRect.Width * 0.86)
    $w = $sigRight - $sigLeft
    $pts = @(
        @{ x = $sigLeft * 1.00; y = $sigY * 1.00 },
        @{ x = $sigLeft + $w * 0.10; y = $sigY - $size * 0.10 },
        @{ x = $sigLeft + $w * 0.18; y = $sigY + $size * 0.02 },
        @{ x = $sigLeft + $w * 0.26; y = $sigY - $size * 0.06 },
        @{ x = $sigLeft + $w * 0.36; y = $sigY + $size * 0.04 },
        @{ x = $sigLeft + $w * 0.48; y = $sigY - $size * 0.10 },
        @{ x = $sigLeft + $w * 0.60; y = $sigY + $size * 0.06 },
        @{ x = $sigLeft + $w * 0.72; y = $sigY - $size * 0.08 },
        @{ x = $sigLeft + $w * 0.85; y = $sigY + $size * 0.02 },
        @{ x = $sigRight * 1.00; y = $sigY * 1.00 }
    )
    for ($i = 1; $i -lt $pts.Count; $i++) {
        $g.DrawLine($sigPen, $pts[$i - 1].x, $pts[$i - 1].y, $pts[$i].x, $pts[$i].y)
    }

    $g.Dispose()
    return $bmp
}

# Genera un PNG de 256px (buena fuente para el .ico).
$big = New-IconBitmap 256
$big.Save($pngSourcePath, [System.Drawing.Imaging.ImageFormat]::Png)

# Construye el .ico multi-resolución a mano para no depender de herramientas externas.
$entries = @()
foreach ($s in $sizes) {
    $entries += New-IconBitmap $s
}

$ms = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter $ms
$bw.Write([uint16]0)              # reserved
$bw.Write([uint16]1)              # type: 1 = icon
$bw.Write([uint16]$entries.Count)  # count

$headerSize = 6
$entrySize = 16
$imageOffset = $headerSize + ($entrySize * $entries.Count)

$imageBytes = @()
for ($i = 0; $i -lt $entries.Count; $i++) {
    $bmp = $entries[$i]
    $pngMs = New-Object System.IO.MemoryStream
    $bmp.Save($pngMs, [System.Drawing.Imaging.ImageFormat]::Png)
    $bytes = $pngMs.ToArray()
    $imageBytes += ,$bytes
    $pngMs.Dispose()
    $bmp.Dispose()
}

for ($i = 0; $i -lt $entries.Count; $i++) {
    $s = $sizes[$i]
    $bytes = $imageBytes[$i]
    $w = if ($s -ge 256) { 0 } else { [byte]$s }
    $h = if ($s -ge 256) { 0 } else { [byte]$s }
    $bw.Write([byte]$w)            # width
    $bw.Write([byte]$h)            # height
    $bw.Write([byte]0)             # color count
    $bw.Write([byte]0)             # reserved
    $bw.Write([uint16]1)           # color planes
    $bw.Write([uint16]32)          # bits per pixel
    $bw.Write([uint32]$bytes.Length)
    $bw.Write([uint32]$imageOffset)
    $imageOffset += $bytes.Length
}

foreach ($bytes in $imageBytes) {
    $bw.Write($bytes)
}

$bw.Flush()
[System.IO.File]::WriteAllBytes($iconPath, $ms.ToArray())
$bw.Dispose()
$ms.Dispose()

Write-Host "Icono creado: $iconPath ($((Get-Item $iconPath).Length) bytes)"
Write-Host "PNG fuente:   $pngSourcePath"