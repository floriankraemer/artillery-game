<#
.SYNOPSIS
  Launch the Artillery3D Godot project in the editor (default) or run it directly.
.PARAMETER Play
  Run the game scene instead of opening the editor.
.NOTES
  Godot folder/exe comes from tools/godot.path (gitignored; copy godot.path.example),
  else the default install below.
#>
param([switch]$Play)

$ErrorActionPreference = 'Stop'
$root    = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'game'
$pathFile = Join-Path $PSScriptRoot 'godot.path'
$godotDir = 'M:\Godot_v4.7-stable_mono_win64'
if (Test-Path $pathFile) {
    $line = Get-Content $pathFile | Where-Object { $_ -and -not $_.StartsWith('#') } | Select-Object -First 1
    if ($line) { $godotDir = $line.Trim() }
}
$exe = if ($godotDir.ToLower().EndsWith('.exe')) { $godotDir } else { Join-Path $godotDir 'Godot_v4.7-stable_mono_win64_console.exe' }
if (-not (Test-Path $exe)) { throw "Godot executable not found: $exe" }

if ($Play) {
    & $exe --path $project
} else {
    & $exe --path $project --editor
}
