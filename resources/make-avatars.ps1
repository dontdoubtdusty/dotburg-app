# Cuts display-picture icons out of AI-made sheets (each a grid of grey cells split by white gutters) into
# resources/avatars.png: an atlas 8 cells across, 96 x 96 px each, with the grey cell background made see-through.
# The grid is found from the white gutters, so a slightly uneven sheet still cuts cleanly.
# Usage: powershell -File make-avatars.ps1 <out.png> <sheet1> [sheet2 ...]
param([string]$Out, [Parameter(ValueFromRemainingArguments = $true)][string[]]$Sheets)
Add-Type -AssemblyName System.Drawing
$C = 96; $Across = 8

# Runs of non-gutter lines along one axis: a line is gutter when nearly all of it is near-white.
function Runs($bmp, [bool]$cols) {
  $len = if ($cols) { $bmp.Width } else { $bmp.Height }
  $other = if ($cols) { $bmp.Height } else { $bmp.Width }
  $runs = @(); $start = -1
  for ($i = 0; $i -lt $len; $i++) {
    $white = 0; $n = 0
    for ($j = 0; $j -lt $other; $j += 4) {
      $p = if ($cols) { $bmp.GetPixel($i, $j) } else { $bmp.GetPixel($j, $i) }
      if ($p.R -gt 238 -and $p.G -gt 238 -and $p.B -gt 238) { $white++ }; $n++
    }
    $gutter = $white -gt $n * 0.9
    if (-not $gutter -and $start -lt 0) { $start = $i }
    if (($gutter -or $i -eq $len - 1) -and $start -ge 0) { if ($i - $start -gt 20) { $runs += , @($start, $i) }; $start = -1 }
  }
  return , $runs
}

$cells = New-Object System.Collections.Generic.List[System.Drawing.Bitmap]
foreach ($s in $Sheets) {
  $src = [System.Drawing.Bitmap]::FromFile((Resolve-Path $s))
  $xs = Runs $src $true; $ys = Runs $src $false
  Write-Host "$s : $($xs.Count) x $($ys.Count) cells"
  foreach ($r in $ys) { foreach ($col in $xs) {
    $w = $col[1] - $col[0] - 4; $h = $r[1] - $r[0] - 4; $side = [Math]::Min($w, $h)
    $cell = New-Object System.Drawing.Bitmap $C, $C, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($cell)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $sx = $col[0] + 2 + [Math]::Floor(($w - $side) / 2); $sy = $r[0] + 2 + [Math]::Floor(($h - $side) / 2)
    $g.DrawImage($src, (New-Object System.Drawing.Rectangle 0, 0, $C, $C), (New-Object System.Drawing.Rectangle $sx, $sy, $side, $side), [System.Drawing.GraphicsUnit]::Pixel)
    $g.Dispose()
    $cells.Add($cell)
  } }
  $src.Dispose()
}

# The cell's grey: the average of its four corners. Background is grey close to it, reached from the edge.
$atlas = New-Object System.Drawing.Bitmap ($Across * $C), ([Math]::Ceiling($cells.Count / $Across) * $C), ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
for ($k = 0; $k -lt $cells.Count; $k++) {
  $b = $cells[$k]
  $cs = @($b.GetPixel(2, 2), $b.GetPixel($C - 3, 2), $b.GetPixel(2, $C - 3), $b.GetPixel($C - 3, $C - 3))
  $gr = ($cs | Measure-Object -Property R -Average).Average; $gg = ($cs | Measure-Object -Property G -Average).Average; $gb = ($cs | Measure-Object -Property B -Average).Average
  $dist = New-Object 'double[,]' $C, $C
  for ($y = 0; $y -lt $C; $y++) { for ($x = 0; $x -lt $C; $x++) { $p = $b.GetPixel($x, $y)
    $dist[$x, $y] = [Math]::Abs($p.R - $gr) + [Math]::Abs($p.G - $gg) + [Math]::Abs($p.B - $gb) } }
  $bg = New-Object 'bool[,]' $C, $C
  $q = New-Object System.Collections.Generic.Queue[int]
  for ($i = 0; $i -lt $C; $i++) { foreach ($p in @(@($i, 0), @($i, ($C - 1)), @(0, $i), @(($C - 1), $i))) { if (-not $bg[$p[0], $p[1]] -and $dist[$p[0], $p[1]] -lt 40) { $bg[$p[0], $p[1]] = $true; $q.Enqueue($p[0] * 1000 + $p[1]) } } }
  while ($q.Count -gt 0) { $v = $q.Dequeue(); $x = [Math]::Floor($v / 1000); $y = $v % 1000
    foreach ($d in @(@(1, 0), @(-1, 0), @(0, 1), @(0, -1))) { $nx = $x + $d[0]; $ny = $y + $d[1]
      if ($nx -ge 0 -and $ny -ge 0 -and $nx -lt $C -and $ny -lt $C -and -not $bg[$nx, $ny] -and $dist[$nx, $ny] -lt 40) { $bg[$nx, $ny] = $true; $q.Enqueue($nx * 1000 + $ny) } } }
  $ox = ($k % $Across) * $C; $oy = [Math]::Floor($k / $Across) * $C
  for ($y = 0; $y -lt $C; $y++) { for ($x = 0; $x -lt $C; $x++) {
    if ($bg[$x, $y]) { continue }
    $p = $b.GetPixel($x, $y); $a = 255
    # A pixel next to the background that is still close to the grey is anti-aliasing: fade it.
    $edge = $false
    foreach ($d in @(@(1, 0), @(-1, 0), @(0, 1), @(0, -1))) { $nx = $x + $d[0]; $ny = $y + $d[1]; if ($nx -lt 0 -or $ny -lt 0 -or $nx -ge $C -or $ny -ge $C -or $bg[$nx, $ny]) { $edge = $true } }
    if ($edge) { $dv = $dist[$x, $y]; $a = [int][Math]::Min(255, [Math]::Max(60, 3 * $dv)) }
    $atlas.SetPixel($ox + $x, $oy + $y, [System.Drawing.Color]::FromArgb($a, $p.R, $p.G, $p.B))
  } }
}
$atlas.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
Write-Host "Wrote $($cells.Count) icons to $Out"
