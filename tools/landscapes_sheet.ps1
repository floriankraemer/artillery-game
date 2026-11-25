<#
.SYNOPSIS
  Screenshots all 27 landscapes in the sandbox (iso view, same seed) and tiles them into artifacts/screens/landscapes_sheet.png.
#>
param([uint32]$Seed = 12345, [int]$Cols = 6)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$root = Split-Path -Parent $PSScriptRoot
$dir = Join-Path $root 'artifacts\screens\ls'
New-Item -ItemType Directory -Force $dir | Out-Null
$exe = 'M:\Godot_v4.7-stable_mono_win64\Godot_v4.7-stable_mono_win64.exe'
$tw = 480; $th = 270
$rows = [math]::Ceiling(27 / $Cols)
$sheet = New-Object System.Drawing.Bitmap ($tw * $Cols), ($th * $rows)
$g = [System.Drawing.Graphics]::FromImage($sheet)
$g.InterpolationMode = 'HighQualityBicubic'
$font = New-Object System.Drawing.Font 'Arial', 11, ([System.Drawing.FontStyle]::Bold)
for ($i = 1; $i -le 27; $i++) {
    $f = Join-Path $dir ("l{0:00}.png" -f $i)
    & $exe --path (Join-Path $root 'game') res://scenes/Sandbox.tscn --resolution 1280x720 -- --lid=$i --seed=$Seed --view=iso --shot=$f | Out-Null
    $img = [System.Drawing.Image]::FromFile($f)
    $x = (($i - 1) % $Cols) * $tw; $y = [math]::Floor(($i - 1) / $Cols) * $th
    $g.DrawImage($img, $x, $y, $tw, $th)
    $g.DrawString("$i", $font, [System.Drawing.Brushes]::White, $x + 4, $y + 2)
    $img.Dispose()
}
$g.Dispose()
$out = Join-Path $root 'artifacts\screens\landscapes_sheet.png'
$sheet.Save($out, [System.Drawing.Imaging.ImageFormat]::Png); $sheet.Dispose()
"saved $out"
