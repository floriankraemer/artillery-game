<#
.SYNOPSIS
  Run the standalone world-view sandbox scene (does not change the project main scene).
.PARAMETER Extra
  Extra sandbox args, e.g. --lid=16 --view=side --shot=C:\tmp\a.png --frames=40 --fire
#>
param([string]$Godot = '', [Parameter(ValueFromRemainingArguments)][string[]]$Extra)

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

& $exe --path $project 'res://scenes/Sandbox.tscn' -- @Extra
