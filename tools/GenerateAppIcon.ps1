# GenerateAppIcon.ps1
Add-Type -AssemblyName System.Drawing

function Create-AppIconImage {
    param([int]$size)

    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    $scale = $size / 256.0

    # 1. Background Squircle (Rounded Rectangle)
    $margin = 12 * $scale
    $rectW = $size - (2 * $margin)
    $rectH = $size - (2 * $margin)
    $radius = 48 * $scale

    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $radius * 2
    $path.AddArc($margin, $margin, $d, $d, 180, 90)
    $path.AddArc($margin + $rectW - $d, $margin, $d, $d, 270, 90)
    $path.AddArc($margin + $rectW - $d, $margin + $rectH - $d, $d, $d, 0, 90)
    $path.AddArc($margin, $margin + $rectH - $d, $d, $d, 90, 90)
    $path.CloseFigure()

    # Gradient Background: Deep Navy to Modern Slate/Blue
    $bgBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.PointF($margin, $margin)),
        (New-Object System.Drawing.PointF($margin + $rectW, $margin + $rectH)),
        [System.Drawing.Color]::FromArgb(255, 11, 23, 54),
        [System.Drawing.Color]::FromArgb(255, 17, 43, 90)
    )
    $g.FillPath($bgBrush, $path)

    # Subtle inner border (Fluent glow)
    $borderPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(160, 56, 189, 248), [Math]::Max(1.0, 2.5 * $scale))
    $g.DrawPath($borderPen, $path)

    # 2. Bluetooth Symbol (Left-aligned & Centered vertically)
    $btCenterX = 98 * $scale
    $btCenterY = 128 * $scale
    $btHeight = 110 * $scale
    $btTopY = $btCenterY - ($btHeight / 2)
    $btBottomY = $btCenterY + ($btHeight / 2)
    $wingW = 34 * $scale
    $wingH = $btHeight * 0.28

    $btPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 241, 245, 249), [Math]::Max(1.5, 9.0 * $scale))
    $btPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $btPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $btPen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round

    # Center vertical spine
    $g.DrawLine($btPen, $btCenterX, $btTopY, $btCenterX, $btBottomY)
    # Upper chevron and loop
    $p1 = New-Object System.Drawing.PointF($btCenterX - $wingW * 0.8, $btCenterY - $wingH)
    $p2 = New-Object System.Drawing.PointF($btCenterX + $wingW, $btTopY + ($wingH * 0.7))
    $p3 = New-Object System.Drawing.PointF($btCenterX, $btTopY)
    $g.DrawLine($btPen, $p1, $p2)
    $g.DrawLine($btPen, $p2, $p3)

    # Lower chevron and loop
    $p5 = New-Object System.Drawing.PointF($btCenterX - $wingW * 0.8, $btCenterY + $wingH)
    $p6 = New-Object System.Drawing.PointF($btCenterX + $wingW, $btBottomY - ($wingH * 0.7))
    $p7 = New-Object System.Drawing.PointF($btCenterX, $btBottomY)
    $g.DrawLine($btPen, $p5, $p6)
    $g.DrawLine($btPen, $p6, $p7)

    # 3. Battery Badge with Lightning Bolt (Right Side)
    $batX = 160 * $scale
    $batY = 78 * $scale
    $batW = 54 * $scale
    $batH = 98 * $scale
    $batRad = 10 * $scale

    # Battery Outer Shell
    $batPath = New-Object System.Drawing.Drawing2D.GraphicsPath
    $bd = $batRad * 2
    $batPath.AddArc($batX, $batY, $bd, $bd, 180, 90)
    $batPath.AddArc($batX + $batW - $bd, $batY, $bd, $bd, 270, 90)
    $batPath.AddArc($batX + $batW - $bd, $batY + $batH - $bd, $bd, $bd, 0, 90)
    $batPath.AddArc($batX, $batY + $batH - $bd, $bd, $bd, 90, 90)
    $batPath.CloseFigure()

    $batShellBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(230, 15, 23, 42))
    $g.FillPath($batShellBrush, $batPath)

    $batShellPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 71, 85, 105), [Math]::Max(1.0, 3.5 * $scale))
    $g.DrawPath($batShellPen, $batPath)

    # Battery Terminal Cap (top nipple)
    $termW = 20 * $scale
    $termH = 7 * $scale
    $termX = $batX + (($batW - $termW) / 2)
    $termY = $batY - $termH + (1 * $scale)
    $termBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 148, 163, 184))
    $g.FillRectangle($termBrush, $termX, $termY, $termW, $termH)

    # Battery Fill (80% full, vibrant neon green gradient)
    $fillMargin = 4 * $scale
    $fillH = ($batH - (2 * $fillMargin)) * 0.78
    $fillY = $batY + $batH - $fillMargin - $fillH
    $fillW = $batW - (2 * $fillMargin)
    $fillX = $batX + $fillMargin

    if ($fillH -gt 0 -and $fillW -gt 0) {
        $fillBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
            (New-Object System.Drawing.PointF($fillX, $fillY)),
            (New-Object System.Drawing.PointF($fillX, $fillY + $fillH)),
            [System.Drawing.Color]::FromArgb(255, 52, 211, 153),
            [System.Drawing.Color]::FromArgb(255, 16, 185, 129)
        )
        $g.FillRectangle($fillBrush, $fillX, $fillY, $fillW, $fillH)
    }

    # Energy Lightning Bolt (Center of battery)
    if ($size -ge 32) {
        $boltPath = New-Object System.Drawing.Drawing2D.GraphicsPath
        $bx = $batX + ($batW / 2)
        $by = $batY + ($batH / 2)
        $bw = 14 * $scale
        $bh = 32 * $scale

        $boltPts = @(
            (New-Object System.Drawing.PointF($bx + ($bw * 0.1), $by - ($bh * 0.5))),
            (New-Object System.Drawing.PointF($bx - ($bw * 0.6), $by + ($bh * 0.05))),
            (New-Object System.Drawing.PointF($bx - ($bw * 0.05), $by + ($bh * 0.05))),
            (New-Object System.Drawing.PointF($bx - ($bw * 0.2), $by + ($bh * 0.5))),
            (New-Object System.Drawing.PointF($bx + ($bw * 0.6), $by - ($bh * 0.05))),
            (New-Object System.Drawing.PointF($bx + ($bw * 0.05), $by - ($bh * 0.05)))
        )
        $boltPath.AddLines($boltPts)
        $boltPath.CloseFigure()

        $boltBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 255, 255, 255))
        $g.FillPath($boltBrush, $boltPath)
    }

    $g.Dispose()
    return $bmp
}

function Save-MultiResIcon {
    param(
        [int[]]$sizes,
        [string]$outputPath
    )

    $imagesData = @()
    foreach ($sz in $sizes) {
        $bmp = Create-AppIconImage -size $sz
        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $bmp.Dispose()
        $imagesData += ,@($sz, $ms.ToArray())
        $ms.Dispose()
    }

    $fs = New-Object System.IO.FileStream($outputPath, [System.IO.FileMode]::Create)
    $bw = New-Object System.IO.BinaryWriter($fs)

    # ICONDIR Header
    $bw.Write([uint16]0)
    $bw.Write([uint16]1)
    $bw.Write([uint16]$imagesData.Count)

    $offset = 6 + ($imagesData.Count * 16)

    foreach ($item in $imagesData) {
        $sz = $item[0]
        $pngBytes = $item[1]

        $bWidth = if ($sz -ge 256) { 0 } else { [byte]$sz }
        $bHeight = if ($sz -ge 256) { 0 } else { [byte]$sz }

        $bw.Write([byte]$bWidth)
        $bw.Write([byte]$bHeight)
        $bw.Write([byte]0)
        $bw.Write([byte]0)
        $bw.Write([uint16]1)
        $bw.Write([uint16]32)
        $bw.Write([uint32]$pngBytes.Length)
        $bw.Write([uint32]$offset)
        $offset += $pngBytes.Length
    }

    foreach ($item in $imagesData) {
        $pngBytes = $item[1]
        $bw.Write($pngBytes, 0, $pngBytes.Length)
    }

    $bw.Flush()
    $bw.Dispose()
    $fs.Dispose()
}

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$outIco = 'F:\Projects\BlueetoothBatteryMonitor\src\BluetoothBatteryMonitor.App\Assets\app.ico'
Save-MultiResIcon -sizes $sizes -outputPath $outIco
Write-Host 'Created multi-resolution icon at:' $outIco

$previewBmp = Create-AppIconImage -size 256
$previewPath = 'F:\Projects\BlueetoothBatteryMonitor\docs\app_icon_preview.png'
$previewBmp.Save($previewPath, [System.Drawing.Imaging.ImageFormat]::Png)
$previewBmp.Dispose()
Write-Host 'Created preview PNG at:' $previewPath
