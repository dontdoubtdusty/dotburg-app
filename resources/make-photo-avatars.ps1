# Builds resources/avatars.png (display pictures, 8 across, 96 x 96 px cells) from the list in avatar-photos/avatars.txt:
# each line is a photo or a kept cartoon cell, a square crop and a 2005-style treatment (a flash-lit snapshot, a blocky
# JPEG, a webcam grab, a tilted print, sepia, black and white, oversaturated, a faded print, a digicam date stamp).
# Usage: powershell -ExecutionPolicy Bypass -File make-photo-avatars.ps1 [out.png]
param([string]$Out = "$PSScriptRoot\avatars.png")
Add-Type -AssemblyName System.Drawing
$C = 96; $Across = 8; $Dir = "$PSScriptRoot\avatar-photos"
$rng = New-Object System.Random 2006   # fixed seed so the atlas is the same every build

# A fresh 96 px ARGB bitmap and its Graphics set for smooth scaling.
function NewCell { $b = New-Object System.Drawing.Bitmap $C, $C, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb); return $b }
function Gfx($b) {
  $g = [System.Drawing.Graphics]::FromImage($b)
  $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
  $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
  $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
  return $g
}

# Redraws a cell through a 5x5 colour matrix (rows: r, g, b, a, offset).
function Matrix($b, [single[][]]$m) {
  $cm = New-Object System.Drawing.Imaging.ColorMatrix (, $m)
  $ia = New-Object System.Drawing.Imaging.ImageAttributes; $ia.SetColorMatrix($cm)
  $o = NewCell; $g = Gfx $o
  $g.DrawImage($b, (New-Object System.Drawing.Rectangle 0, 0, $C, $C), 0, 0, $C, $C, [System.Drawing.GraphicsUnit]::Pixel, $ia)
  $g.Dispose(); $b.Dispose(); return $o
}

# Saturation matrix: s = 0 is grey, 1 unchanged, above 1 more colourful.
function Sat([single]$s) {
  $r = 0.3086 * (1 - $s); $gr = 0.6094 * (1 - $s); $bl = 0.0820 * (1 - $s)
  return [single[][]]@(@(($r + $s), $r, $r, 0, 0), @($gr, ($gr + $s), $gr, 0, 0), @($bl, $bl, ($bl + $s), 0, 0), @(0, 0, 0, 1, 0), @(0, 0, 0, 0, 1))
}

# Saves the cell as a JPEG at the given quality and loads it back (blocky artefacts).
function Jpeg($b, [long]$q) {
  $enc = [System.Drawing.Imaging.ImageCodecInfo]::GetImageEncoders() | Where-Object { $_.MimeType -eq "image/jpeg" }
  $ep = New-Object System.Drawing.Imaging.EncoderParameters 1
  $ep.Param[0] = New-Object System.Drawing.Imaging.EncoderParameter ([System.Drawing.Imaging.Encoder]::Quality), $q
  $ms = New-Object System.IO.MemoryStream; $b.Save($ms, $enc, $ep); $b.Dispose()
  $j = New-Object System.Drawing.Bitmap $ms; $o = NewCell; $g = Gfx $o; $g.DrawImage($j, 0, 0, $C, $C); $g.Dispose(); $j.Dispose()
  return $o
}

# Shrinks the cell to n pixels and stretches it back (a low-resolution camera).
function LowRes($b, [int]$n) {
  $s = New-Object System.Drawing.Bitmap $n, $n; $g = Gfx $s; $g.DrawImage($b, 0, 0, $n, $n); $g.Dispose()
  $o = NewCell; $g = Gfx $o; $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::Bilinear
  $g.DrawImage($s, (New-Object System.Drawing.Rectangle 0, 0, $C, $C), -0.5, -0.5, $n, $n, [System.Drawing.GraphicsUnit]::Pixel); $g.Dispose(); $s.Dispose(); $b.Dispose()
  return $o
}

# Adds grain: each pixel nudged by up to +-amt.
function Noise($b, [int]$amt) {
  for ($y = 0; $y -lt $C; $y++) { for ($x = 0; $x -lt $C; $x++) {
    $p = $b.GetPixel($x, $y); $d = $rng.Next(-$amt, $amt + 1)
    $b.SetPixel($x, $y, [System.Drawing.Color]::FromArgb($p.A, [Math]::Max(0, [Math]::Min(255, $p.R + $d)), [Math]::Max(0, [Math]::Min(255, $p.G + $d)), [Math]::Max(0, [Math]::Min(255, $p.B + $d))))
  } }
  return $b
}

# Darkens the corners (a cheap lens or a flash that only reaches the middle).
function Vignette($b, [int]$alpha) {
  $g = Gfx $b; $path = New-Object System.Drawing.Drawing2D.GraphicsPath; $path.AddEllipse(-30, -30, $C + 60, $C + 60)
  $br = New-Object System.Drawing.Drawing2D.PathGradientBrush $path
  $br.CenterColor = [System.Drawing.Color]::FromArgb(0, 0, 0, 0); $br.SurroundColors = @([System.Drawing.Color]::FromArgb($alpha, 0, 0, 0))
  $g.FillRectangle($br, 0, 0, $C, $C); $g.Dispose(); return $b
}

# The treatments, each a small recipe on a 96 px cell.
function Treat($b, [string]$t) {
  switch ($t) {
    "flash" { $b = Matrix $b ([single[][]]@(@(1.3, 0, 0, 0, 0), @(0, 1.3, 0, 0, 0), @(0, 0, 1.4, 0, 0), @(0, 0, 0, 1, 0), @(0.02, 0.02, 0.06, 0, 1))); $b = Vignette $b 170; $b = Jpeg $b 45 }
    "jpeg" { $b = LowRes $b 56; $b = Jpeg $b 9 }
    "webcam" { $b = LowRes $b 34; $b = Matrix $b ([single[][]]@(@(0.85, 0.05, 0, 0, 0), @(0.1, 0.95, 0.1, 0, 0), @(0, 0.05, 1.05, 0, 0), @(0, 0, 0, 1, 0), @(0.04, 0.06, 0.08, 0, 1))); $b = Noise $b 14; $b = Jpeg $b 30 }
    "sepia" { $b = Matrix $b ([single[][]]@(@(0.393, 0.349, 0.272, 0, 0), @(0.769, 0.686, 0.534, 0, 0), @(0.189, 0.168, 0.131, 0, 0), @(0, 0, 0, 1, 0), @(0.03, 0.02, 0, 0, 1))); $b = Noise $b 10; $b = Vignette $b 110 }
    "bw" { $b = Matrix $b (Sat 0); $b = Matrix $b ([single[][]]@(@(1.25, 0, 0, 0, 0), @(0, 1.25, 0, 0, 0), @(0, 0, 1.25, 0, 0), @(0, 0, 0, 1, 0), @(-0.1, -0.1, -0.1, 0, 1))) }
    "oversat" { $b = Matrix $b (Sat 1.9); $b = Jpeg $b 60 }
    "faded" { $b = Matrix $b ([single[][]]@(@(0.75, 0, 0, 0, 0), @(0, 0.68, 0, 0, 0), @(0, 0, 0.62, 0, 0), @(0, 0, 0, 1, 0), @(0.2, 0.15, 0.16, 0, 1))); $b = Noise $b 6 }
    "tilt" {
      # A print photographed (or scanned) crooked: white border, on a dark table.
      $o = NewCell; $g = Gfx $o; $g.Clear([System.Drawing.Color]::FromArgb(255, 70, 58, 48))
      $ang = ($rng.NextDouble() * 6 + 4) * $(if ($rng.Next(2) -eq 0) { -1 } else { 1 })
      $g.TranslateTransform($C / 2, $C / 2); $g.RotateTransform($ang)
      $g.FillRectangle([System.Drawing.Brushes]::WhiteSmoke, -40, -40, 80, 80)
      $g.DrawImage($b, -35, -35, 70, 70); $g.Dispose(); $b.Dispose(); $b = Jpeg $o 55
    }
    "datestamp" {
      # The orange date a 2005 digital camera burned into the corner.
      $g = Gfx $b; $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::SingleBitPerPixelGridFit
      $d = "'0{0} {1} {2}" -f ($rng.Next(4, 7)), ($rng.Next(1, 13)), ($rng.Next(1, 29))
      $f = New-Object System.Drawing.Font "Lucida Console", 7, ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
      $sz = $g.MeasureString($d, $f); $x = $C - $sz.Width - 2; $y = $C - $sz.Height - 2
      $g.DrawString($d, $f, (New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(160, 60, 20, 0))), $x + 1, $y + 1)
      $g.DrawString($d, $f, (New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 255, 140, 30))), $x, $y)
      $g.Dispose(); $b = Jpeg $b 70
    }
  }
  return $b
}

$lines = Get-Content "$Dir\avatars.txt" | Where-Object { $_.Trim() -and -not $_.StartsWith("#") }
$rows = [Math]::Ceiling($lines.Count / $Across)
$atlas = New-Object System.Drawing.Bitmap ($Across * $C), ($rows * $C), ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$ag = [System.Drawing.Graphics]::FromImage($atlas)
$i = 0
foreach ($ln in $lines) {
  $parts = $ln.Split("|") | ForEach-Object { $_.Trim() }
  $crop = $parts[1].Split(",") | ForEach-Object { [double]$_ }
  $src = [System.Drawing.Bitmap]::FromFile("$Dir\$($parts[0])")
  # Square crop round the given centre, kept inside the picture, scaled to one cell.
  $side = [Math]::Min($src.Width, $src.Height) * $crop[2]
  $sx = [Math]::Max(0, [Math]::Min($src.Width - $side, $crop[0] * $src.Width - $side / 2))
  $sy = [Math]::Max(0, [Math]::Min($src.Height - $side, $crop[1] * $src.Height - $side / 2))
  $cell = NewCell; $g = Gfx $cell
  $g.DrawImage($src, (New-Object System.Drawing.Rectangle 0, 0, $C, $C), [single]$sx, [single]$sy, [single]$side, [single]$side, [System.Drawing.GraphicsUnit]::Pixel)
  $g.Dispose(); $src.Dispose()
  $cell = Treat $cell $parts[2]
  $ag.DrawImageUnscaled($cell, ($i % $Across) * $C, [Math]::Floor($i / $Across) * $C); $cell.Dispose(); $i++
}
$ag.Dispose(); $atlas.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png); $atlas.Dispose()
Write-Host "$i cells -> $Out"
