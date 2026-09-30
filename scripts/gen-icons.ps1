# Gera identidade visual do DualAudioMirror em assets/
#   icon.ico (16..256), wizard-image.bmp (164x314), wizard-small.bmp (55x58)
param(
    [string]$OutDir = (Join-Path $PSScriptRoot "..\assets")
)

Add-Type -AssemblyName System.Drawing

$OutDir = [System.IO.Path]::GetFullPath($OutDir)
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$Bg     = [System.Drawing.Color]::FromArgb(255, 20, 24, 33)
$Border = [System.Drawing.Color]::FromArgb(255, 46, 54, 70)
$Accent = [System.Drawing.Color]::FromArgb(255, 77, 163, 255)
$Accent2= [System.Drawing.Color]::FromArgb(255, 124, 196, 255)
$Text   = [System.Drawing.Color]::FromArgb(255, 232, 236, 244)
$Muted  = [System.Drawing.Color]::FromArgb(255, 151, 160, 180)

function New-Font {
    param([float]$Size, [bool]$Bold)
    $style = [System.Drawing.FontStyle]::Regular
    if ($Bold) { $style = [System.Drawing.FontStyle]::Bold }
    try {
        return [System.Drawing.Font]::new("Segoe UI", $Size, $style, [System.Drawing.GraphicsUnit]::Pixel)
    } catch {
        return [System.Drawing.Font]::new([System.Drawing.FontFamily]::GenericSansSerif, $Size, $style, [System.Drawing.GraphicsUnit]::Pixel)
    }
}

function New-RoundedRectPath {
    param([System.Drawing.RectangleF]$R, [float]$Radius)
    $d = $Radius * 2
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $path.AddArc($R.X, $R.Y, $d, $d, 180, 90)
    $path.AddArc($R.Right - $d, $R.Y, $d, $d, 270, 90)
    $path.AddArc($R.Right - $d, $R.Bottom - $d, $d, $d, 0, 90)
    $path.AddArc($R.X, $R.Bottom - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

function Draw-Mark {
    param(
        [System.Drawing.Graphics]$G,
        [float]$Cx,
        [float]$Cy,
        [float]$S,
        [bool]$WithBackground
    )
    $G.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $G.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAlias

    if ($WithBackground) {
        $pad = [Math]::Max(1.0, $S * 0.03)
        $x = $Cx - ($S / 2.0) + $pad
        $y = $Cy - ($S / 2.0) + $pad
        $w = $S - (2.0 * $pad)
        $rect = [System.Drawing.RectangleF]::new([float]$x, [float]$y, [float]$w, [float]$w)
        $r = [Math]::Max(2.0, $S * 0.22)
        $path = New-RoundedRectPath $rect ([float]$r)
        $bgBrush = [System.Drawing.SolidBrush]::new($Bg)
        $G.FillPath($bgBrush, $path)
        $penB = [System.Drawing.Pen]::new($Border, [Math]::Max(1.0, $S * 0.035))
        $G.DrawPath($penB, $path)
        $path.Dispose()
        $bgBrush.Dispose()
        $penB.Dispose()
    }

    $r1 = [float]($S * 0.17)
    $r2 = [float]($S * 0.30)
    $w  = [Math]::Max(1.2, $S * 0.055)
    $pen = [System.Drawing.Pen]::new($Accent, [float]$w)
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap   = [System.Drawing.Drawing2D.LineCap]::Round

    foreach ($r in @($r1, $r2)) {
        $box = [System.Drawing.RectangleF]::new([float]($Cx - $r), [float]($Cy - $r), [float](2 * $r), [float](2 * $r))
        $G.DrawArc($pen, $box, [float]120, [float]120)
        $G.DrawArc($pen, $box, [float]300, [float]120)
    }

    $dotR = [float]($S * 0.085)
    $dotBrush = [System.Drawing.SolidBrush]::new($Accent2)
    $G.FillEllipse($dotBrush, [float]($Cx - $dotR), [float]($Cy - $dotR), [float](2 * $dotR), [float](2 * $dotR))
    $dotBrush.Dispose()
    $pen.Dispose()
}

function New-IconBitmap {
    param([int]$Size)
    $bmp = [System.Drawing.Bitmap]::new($Size, $Size)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.Clear([System.Drawing.Color]::Transparent)
    Draw-Mark $g ([float]($Size / 2.0)) ([float]($Size / 2.0)) ([float]$Size) $true
    $g.Dispose()
    return $bmp
}

$iconSizes = @(16, 24, 32, 48, 64, 128, 256)
$streams = [System.Collections.Generic.List[System.IO.MemoryStream]]::new()
foreach ($s in $iconSizes) {
    $b = New-IconBitmap $s
    $ms = [System.IO.MemoryStream]::new()
    $b.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $b.Dispose()
    $streams.Add($ms)
}

$icoPath = Join-Path $OutDir "icon.ico"
$fs = [System.IO.File]::Create($icoPath)
$bw = [System.IO.BinaryWriter]::new($fs)
$bw.Write([uint16]0)
$bw.Write([uint16]1)
$bw.Write([uint16]$iconSizes.Count)
$offset = 6 + (16 * $iconSizes.Count)
for ($i = 0; $i -lt $iconSizes.Count; $i++) {
    $size = $iconSizes[$i]
    $len = $streams[$i].Length
    $entry = if ($size -ge 256) { [byte]0 } else { [byte]$size }
    $bw.Write($entry)
    $bw.Write($entry)
    $bw.Write([byte]0)
    $bw.Write([byte]0)
    $bw.Write([uint16]1)
    $bw.Write([uint16]32)
    $bw.Write([uint32]$len)
    $bw.Write([uint32]$offset)
    $offset += $len
}
for ($i = 0; $i -lt $iconSizes.Count; $i++) {
    $bw.Write($streams[$i].ToArray())
}
$bw.Flush()
$fs.Close()
foreach ($ms in $streams) { $ms.Dispose() }

$wizard = [System.Drawing.Bitmap]::new(164, 314)
$wg = [System.Drawing.Graphics]::FromImage($wizard)
$wg.Clear($Bg)
$wg.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$wg.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
Draw-Mark $wg ([float]82.0) ([float]92.0) ([float]116.0) $false
$accentBrush = [System.Drawing.SolidBrush]::new($Accent)
$wg.FillRectangle($accentBrush, 52, 168, 60, 3)
$accentBrush.Dispose()
$titleFont = New-Font 15 $true
$subFont   = New-Font 10 $false
$fmt = [System.Drawing.StringFormat]::new()
$fmt.Alignment = [System.Drawing.StringAlignment]::Center
$textBrush  = [System.Drawing.SolidBrush]::new($Text)
$mutedBrush = [System.Drawing.SolidBrush]::new($Muted)
$wg.DrawString("DualAudioMirror", $titleFont, $textBrush, [System.Drawing.RectangleF]::new(8, 186, 148, 40), $fmt)
$wg.DrawString("Áudio em dois", $subFont, $mutedBrush, [System.Drawing.RectangleF]::new(8, 214, 148, 20), $fmt)
$wg.DrawString("dispositivos", $subFont, $mutedBrush, [System.Drawing.RectangleF]::new(8, 230, 148, 20), $fmt)
$wg.Dispose()
$wizard.Save((Join-Path $OutDir "wizard-image.bmp"), [System.Drawing.Imaging.ImageFormat]::Bmp)
$wizard.Dispose()

$small = [System.Drawing.Bitmap]::new(55, 58)
$sg = [System.Drawing.Graphics]::FromImage($small)
$sg.Clear($Bg)
Draw-Mark $sg ([float]27.5) ([float]29.0) ([float]50.0) $false
$sg.Dispose()
$small.Save((Join-Path $OutDir "wizard-small.bmp"), [System.Drawing.Imaging.ImageFormat]::Bmp)
$small.Dispose()

Get-ChildItem $OutDir | Select-Object Name, Length
