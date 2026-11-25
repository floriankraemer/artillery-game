<#
.SYNOPSIS
  Headless smoke test: import the project, run the main scene for 120 frames,
  and fail if Godot prints any ERROR or SCRIPT ERROR line.
#>
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

$log = [System.IO.Path]::GetTempFileName()
try {
    Write-Host "Importing assets..."
    & $exe --headless --path $project --import *> $log
    Write-Host "Running main scene headless (120 frames)..."
    & $exe --headless --path $project --quit-after 120 *>> $log
    $exitCode = $LASTEXITCODE
    $output = Get-Content $log
    $output | Where-Object { $_ -notmatch '^\[\s*\d+%' } | ForEach-Object { Write-Host $_ }
    $bad = $output | Where-Object { $_ -match 'SCRIPT ERROR|ERROR' }
    if ($bad) {
        Write-Host "SMOKE FAILED: error lines found:" -ForegroundColor Red
        $bad | ForEach-Object { Write-Host $_ -ForegroundColor Red }
        exit 1
    }
    if ($exitCode -ne 0) { Write-Host "SMOKE FAILED: godot exit code $exitCode" -ForegroundColor Red; exit $exitCode }
    if (-not ($output -match 'Artillery3D ready')) { Write-Host "SMOKE FAILED: expected startup print not found" -ForegroundColor Red; exit 1 }
    Write-Host "SMOKE OK" -ForegroundColor Green
} finally {
    Remove-Item $log -ErrorAction SilentlyContinue
}
