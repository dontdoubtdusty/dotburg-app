# Cuts the 40 classic emoticons out of a reference sheet (10 x 4 boxes) into resources/emoticons.png:
# a 10 x 4 atlas of 24 x 24 cells with the box background made transparent and the light fringe softened.
# Usage: powershell -File make-emoticons.ps1 <sheet.png> [preview.png]
param([string]$Sheet, [string]$Preview = "")
Add-Type -AssemblyName System.Drawing
$src = [System.Drawing.Bitmap]::FromFile($Sheet)
$C = 24
$out = New-Object System.Drawing.Bitmap (10 * $C), (4 * $C), ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
for ($row = 0; $row -lt 4; $row++) {
  for ($k = 0; $k -lt 10; $k++) {
    $bx = 5 + 31 * $k + 1; $by = 4 + 49 * $row + 1; $n = 23
    # Copy the box inside, and mark background: light and grey, reached from the edge.
    $px = New-Object 'int[,]' $n, $n
    $bg = New-Object 'bool[,]' $n, $n
    for ($y = 0; $y -lt $n; $y++) { for ($x = 0; $x -lt $n; $x++) { $px[$x, $y] = $src.GetPixel($bx + $x, $by + $y).ToArgb() } }
    $q = New-Object System.Collections.Generic.Queue[int]
    function Light([int]$v) { $R = ($v -shr 16) -band 255; $G = ($v -shr 8) -band 255; $B = $v -band 255
      $mx = [Math]::Max($R, [Math]::Max($G, $B)); $mn = [Math]::Min($R, [Math]::Min($G, $B)); return (($R + $G + $B) -ge 585) -and (($mx - $mn) -lt 45) }
    for ($i = 0; $i -lt $n; $i++) { foreach ($p in @(@($i, 0), @($i, ($n - 1)), @(0, $i), @(($n - 1), $i))) { if (-not $bg[$p[0], $p[1]] -and (Light $px[$p[0], $p[1]])) { $bg[$p[0], $p[1]] = $true; $q.Enqueue($p[0] * 100 + $p[1]) } } }
    while ($q.Count -gt 0) { $v = $q.Dequeue(); $x = [int][Math]::Floor($v / 100); $y = $v % 100
      foreach ($d in @(@(1, 0), @(-1, 0), @(0, 1), @(0, -1))) { $nx = $x + $d[0]; $ny = $y + $d[1]
        if ($nx -ge 0 -and $ny -ge 0 -and $nx -lt $n -and $ny -lt $n -and -not $bg[$nx, $ny] -and (Light $px[$nx, $ny])) { $bg[$nx, $ny] = $true; $q.Enqueue($nx * 100 + $ny) } } }
    for ($y = 0; $y -lt $n; $y++) { for ($x = 0; $x -lt $n; $x++) {
      if ($bg[$x, $y]) { continue }
      $v = $px[$x, $y]; $R = ($v -shr 16) -band 255; $G = ($v -shr 8) -band 255; $B = $v -band 255
      $a = 255
      # A pixel touching the background that is pale is mostly halo: fade it by how pale it is.
      $edge = $false
      foreach ($d in @(@(1, 0), @(-1, 0), @(0, 1), @(0, -1))) { $nx = $x + $d[0]; $ny = $y + $d[1]; if ($nx -lt 0 -or $ny -lt 0 -or $nx -ge $n -or $ny -ge $n -or $bg[$nx, $ny]) { $edge = $true } }
      $sum = $R + $G + $B
      if ($edge -and $sum -gt 480) { $a = [int][Math]::Max(60, 255 - ($sum - 480) * 0.9) }
      $out.SetPixel($k * $C + $x, $row * $C + $y, [System.Drawing.Color]::FromArgb($a, $R, $G, $B))
    } }
  }
}
$dst = Join-Path $PSScriptRoot "emoticons.png"
$out.Save($dst, [System.Drawing.Imaging.ImageFormat]::Png)
if ($Preview -ne "") {
  $s = 4; $pv = New-Object System.Drawing.Bitmap ($out.Width * $s), ($out.Height * $s)
  $g = [System.Drawing.Graphics]::FromImage($pv); $g.Clear([System.Drawing.Color]::FromArgb(255, 70, 120, 200))
  $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor; $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
  $g.DrawImage($out, 0, 0, $pv.Width, $pv.Height); $pv.Save($Preview, [System.Drawing.Imaging.ImageFormat]::Png)
}
"wrote $dst"
