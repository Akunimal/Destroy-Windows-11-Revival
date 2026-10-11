# ---------------------------------------------------------------------------
# gen-icon.ps1 - regenerate DWS/icon.ico from scratch.
#
# Produces the Windows 11 style icon used by DWS 11: a rounded square with a
# blue gradient, a subtle top gloss, a white shield with a blue inner band and
# a white "11" glyph. Seven sizes are written as PNG-compressed ICO entries
# (16, 24, 32, 48, 64, 128, 256 px), which is what Windows Vista and later use.
#
# Run with:
#   powershell -ExecutionPolicy Bypass -File tools\gen-icon.ps1
#
# Requires only Windows PowerShell 5.1 (System.Drawing ships with .NET
# Framework, but PowerShell does not load it by default - hence the Add-Type).
# ---------------------------------------------------------------------------

Add-Type -AssemblyName System.Drawing
if (-not ('System.Drawing.Bitmap' -as [type])) {
    throw 'Could not load System.Drawing. Run this with Windows PowerShell 5.1 (.NET Framework).'
}

$ErrorActionPreference = 'Stop'

$sizes = 16,24,32,48,64,128,256
$pngs = @{}

$c1 = [System.Drawing.Color]::FromArgb(255, 76, 194, 255)   # light cyan-blue
$c2 = [System.Drawing.Color]::FromArgb(255, 0, 90, 200)     # deep Windows 11 blue
$white = [System.Drawing.Color]::White

function Draw-ShieldPath([float]$x, [float]$y, [float]$w, [float]$h) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $x1 = $x; $x2 = $x + $w
    $yt = $y; $yb = $y + $h
    $ym = $y + $h * 0.42
    $p.AddLine($x1, $yt, $x2, $yt)
    $p.AddLine($x2, $yt, $x2, $ym)
    # Note: AddBezier takes 4 points (8 scalars) - the 6-scalar form does not
    # exist in .NET Framework, that was the one compile-time trap here.
    $p.AddBezier($x2, $ym, $x2, $ym + ($h*0.30), $x + ($w*0.62), $yb, $x + $w/2, $yb)
    $p.AddBezier($x + $w/2, $yb, $x + ($w*0.38), $yb, $x1, $ym + ($h*0.30), $x1, $ym)
    $p.AddLine($x1, $ym, $x1, $yt)
    $p.CloseFigure()
    return $p
}

foreach ($s in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap($s, $s, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $g.Clear([System.Drawing.Color]::Transparent)

    # ---- rounded-square background with gradient ----
    $radius = [float]$s * 0.225
    $bgPath = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $radius * 2
    $x0 = 0.0; $y0 = 0.0; $wh = [float]$s
    $bgPath.AddArc([float]($x0), [float]($y0), [float]$d, [float]$d, 180, 90)
    $bgPath.AddArc([float]($wh-$d), [float]($y0), [float]$d, [float]$d, 270, 90)
    $bgPath.AddArc([float]($wh-$d), [float]($wh-$d), [float]$d, [float]$d, 0, 90)
    $bgPath.AddArc([float]($x0), [float]($wh-$d), [float]$d, [float]$d, 90, 90)
    $bgPath.CloseFigure()
    $lg = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.PointF(0,0)), (New-Object System.Drawing.PointF([float]$s,[float]$s)), $c1, $c2)
    $g.FillPath($lg, $bgPath)

    # subtle top-left gloss
    $gloss = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.PointF(0,0)), (New-Object System.Drawing.PointF(0,[float]$s)),
        ([System.Drawing.Color]::FromArgb(70,255,255,255)), ([System.Drawing.Color]::FromArgb(0,255,255,255)))
    $g.FillPath($gloss, $bgPath)

    # ---- white shield centred ----
    $sw = [float]$s * 0.60
    $sh = [float]$s * 0.66
    $sx = ([float]$s - $sw) / 2.0
    $sy = [float]($s * 0.17)
    $shield = Draw-ShieldPath $sx $sy $sw $sh
    $g.FillPath((New-Object System.Drawing.SolidBrush($white)), $shield)

    # inner shield cut (gradient coloured) => gives a "11" band effect and depth
    $inset = [float]$s * 0.045
    $shield2 = Draw-ShieldPath ($sx+$inset) ($sy+$inset) ($sw - 2*$inset) ($sh - 2*$inset)
    $g.FillPath((New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(235, 20, 100, 210))), $shield2)

    # ---- "11" inside, only when big enough to read ----
    if ($s -ge 40) {
        $fs = [float]($s * 0.34)
        $fnt = New-Object System.Drawing.Font("Segoe UI", $fs, [System.Drawing.FontStyle]::Bold,
            [System.Drawing.GraphicsUnit]::Pixel)
        $txt = "11"
        $tfmt = [System.Drawing.StringFormat]::GenericTypographic
        $tfmt.Alignment = [System.Drawing.StringAlignment]::Center
        $tfmt.LineAlignment = [System.Drawing.StringAlignment]::Center
        $rect = New-Object System.Drawing.RectangleF(0, [float]($s*0.10), [float]$s, [float]($s*0.76))
        $g.DrawString($txt, $fnt, (New-Object System.Drawing.SolidBrush($white)), $rect, $tfmt)
        $fnt.Dispose()
    }

    $lg.Dispose(); $gloss.Dispose(); $bgPath.Dispose(); $shield.Dispose(); $shield2.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs[$s] = $ms.ToArray()
    $ms.Close(); $ms.Dispose(); $bmp.Dispose(); $g.Dispose()
}

# ---- assemble .ico ----
$out = Join-Path $PSScriptRoot '..\DWS\icon.ico'
$fs2 = [System.IO.File]::Create($out)
$bw = New-Object System.IO.BinaryWriter($fs2)
$n = $sizes.Count
$bw.Write([uint16]0)   # reserved
$bw.Write([uint16]1)   # type = icon
$bw.Write([uint16]$n)  # count
$offset = 6 + (16 * $n)
foreach ($s in $sizes) {
    $data = $pngs[$s]
    $w = if ($s -ge 256) { 0 } else { $s }
    $h = if ($s -ge 256) { 0 } else { $s }
    $bw.Write([byte]$w)
    $bw.Write([byte]$h)
    $bw.Write([byte]0)      # palette entries
    $bw.Write([byte]0)      # reserved
    $bw.Write([uint16]1)    # colour planes
    $bw.Write([uint16]32)   # bits per pixel
    $bw.Write([uint32]$data.Length)
    $bw.Write([uint32]$offset)
    $offset += $data.Length
}
foreach ($s in $sizes) { $bw.Write($pngs[$s]) }
$bw.Flush(); $bw.Close(); $fs2.Close()
"icono escrito: $out ($((Get-Item $out).Length) bytes, $n entradas)"
