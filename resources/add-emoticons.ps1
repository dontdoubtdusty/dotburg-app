# Adds picked emoticons from the owner's MSN screenshot (resources/msn-emoticon-sheet.png, a 21 x 15 grid with a
# 23 px pitch) to resources/emoticons.png: the 40 classics stay in rows 0-3, the picks fill rows 4-6 in the order
# below, centred in 24 x 24 cells with the white background made transparent. Safe to run again.
# The codes for these cells are listed in the same order in MessengerUi.cs (EmoCodes).
# Usage: powershell -ExecutionPolicy Bypass -File add-emoticons.ps1 [preview.png]
param([string]$Preview = "")
Add-Type -AssemblyName System.Drawing
# (row, column) on the sheet, in atlas order.
$picks = @(
  @(0,4), @(0,5), @(0,6), @(0,10), @(0,12), @(1,2), @(1,8), @(1,9), @(1,11), @(1,14),
  @(1,15), @(1,18), @(2,8), @(4,7), @(4,10), @(6,13), @(7,1), @(7,12), @(7,20), @(8,0),
  @(8,1), @(9,16), @(9,17), @(9,18), @(10,2), @(14,13)
)
$C = 24
$sheet = [System.Drawing.Bitmap]::FromFile((Join-Path $PSScriptRoot "msn-emoticon-sheet.png"))
$atlasPath = Join-Path $PSScriptRoot "emoticons.png"
$old = [System.Drawing.Bitmap]::FromFile($atlasPath)
$out = New-Object System.Drawing.Bitmap (10 * $C), (7 * $C), ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
# Keep the 40 classics.
for ($y = 0; $y -lt 4 * $C; $y++) { for ($x = 0; $x -lt 10 * $C; $x++) { $out.SetPixel($x, $y, $old.GetPixel($x, $y)) } }
$old.Dispose()
function Light([int]$v) { $R = ($v -shr 16) -band 255; $G = ($v -shr 8) -band 255; $B = $v -band 255
  $mx = [Math]::Max($R, [Math]::Max($G, $B)); $mn = [Math]::Min($R, [Math]::Min($G, $B)); return (($R + $G + $B) -ge 600) -and (($mx - $mn) -lt 40) }
$n = 22
for ($i = 0; $i -lt $picks.Count; $i++) {
  $bx = 17 + 23 * $picks[$i][1]; $by = 19 + 23 * $picks[$i][0]
  $ax = ($i % 10) * $C + 1; $ay = (4 + [Math]::Floor($i / 10)) * $C + 1
  $px = New-Object 'int[,]' $n, $n
  $bg = New-Object 'bool[,]' $n, $n
  for ($y = 0; $y -lt $n; $y++) { for ($x = 0; $x -lt $n; $x++) { $px[$x, $y] = $sheet.GetPixel($bx + $x, $by + $y).ToArgb() } }
  # Background: light pixels reached from the edge of the cell.
  $q = New-Object System.Collections.Generic.Queue[int]
  for ($k = 0; $k -lt $n; $k++) { foreach ($p in @(@($k, 0), @($k, ($n - 1)), @(0, $k), @(($n - 1), $k))) { if (-not $bg[$p[0], $p[1]] -and (Light $px[$p[0], $p[1]])) { $bg[$p[0], $p[1]] = $true; $q.Enqueue($p[0] * 100 + $p[1]) } } }
  while ($q.Count -gt 0) { $v = $q.Dequeue(); $x = [int][Math]::Floor($v / 100); $y = $v % 100
    foreach ($d in @(@(1, 0), @(-1, 0), @(0, 1), @(0, -1))) { $nx = $x + $d[0]; $ny = $y + $d[1]
      if ($nx -ge 0 -and $ny -ge 0 -and $nx -lt $n -and $ny -lt $n -and -not $bg[$nx, $ny] -and (Light $px[$nx, $ny])) { $bg[$nx, $ny] = $true; $q.Enqueue($nx * 100 + $ny) } } }
  for ($y = 0; $y -lt $n; $y++) { for ($x = 0; $x -lt $n; $x++) {
    if ($bg[$x, $y]) { continue }
    $v = $px[$x, $y]; $R = ($v -shr 16) -band 255; $G = ($v -shr 8) -band 255; $B = $v -band 255
    # A pale pixel touching the background is mostly halo: fade it by how pale it is.
    $edge = $false
    foreach ($d in @(@(1, 0), @(-1, 0), @(0, 1), @(0, -1))) { $nx = $x + $d[0]; $ny = $y + $d[1]; if ($nx -lt 0 -or $ny -lt 0 -or $nx -ge $n -or $ny -ge $n -or $bg[$nx, $ny]) { $edge = $true } }
    $a = 255; $sum = $R + $G + $B
    if ($edge -and $sum -gt 480) { $a = [int][Math]::Max(60, 255 - ($sum - 480) * 0.9) }
    $out.SetPixel($ax + $x, $ay + $y, [System.Drawing.Color]::FromArgb($a, $R, $G, $B))
  } }
}
$sheet.Dispose()
$out.Save($atlasPath, [System.Drawing.Imaging.ImageFormat]::Png)
if ($Preview -ne "") {
  $s = 4; $pv = New-Object System.Drawing.Bitmap ($out.Width * $s), ($out.Height * $s)
  $g = [System.Drawing.Graphics]::FromImage($pv); $g.Clear([System.Drawing.Color]::FromArgb(255, 236, 233, 216))
  $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor; $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
  $g.DrawImage($out, 0, 0, $pv.Width, $pv.Height); $pv.Save($Preview, [System.Drawing.Imaging.ImageFormat]::Png)
}
"wrote $atlasPath"
