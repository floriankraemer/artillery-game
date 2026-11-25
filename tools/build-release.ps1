#Requires -Version 5.1
<#
.SYNOPSIS
  Production release build of Artillery3D (Godot 4.7 .NET, Windows x64).
.DESCRIPTION
  Builds the solution in Release, runs the core tests, exports a self-contained
  Windows build with Godot (bundled .NET runtime) into artifacts/ at the repo root.
  Needs: .NET SDK 8+, Godot 4.7 .NET editor, Godot 4.7 .NET export templates.
  Godot is found via -GodotPath, $env:GODOT, tools/godot.path, or the default M:\ install.
.PARAMETER SkipTests   Skip dotnet test.
.PARAMETER Zip         Also create artifacts/Artillery3D-<commit>-win64.zip.
.EXAMPLE
  .\tools\build-release.ps1 -Zip
#>
[CmdletBinding()]
param(
    [string] $GodotPath = "",
    [string] $OutputDir = "artifacts",
    [switch] $SkipTests,
    [switch] $Zip
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$Root      = Split-Path -Parent $PSScriptRoot
$Project   = Join-Path $Root "game"
$Solution  = Join-Path $Root "Artillery3D.slnx"
$Tests     = Join-Path $Root "tests\Artillery.Core.Tests"
$Out       = Join-Path $Root $OutputDir
$ExeName   = "Artillery3D.exe"
$ExePath   = Join-Path $Out $ExeName
$PresetCfg = Join-Path $Project "export_presets.cfg"
$GodotSln  = Join-Path $Project "Artillery3D.sln"

function Step([string] $m) { Write-Host "`n==> $m" -ForegroundColor Cyan }
function Ok([string] $m)   { Write-Host "    $m" -ForegroundColor Green }

function Resolve-Godot {
    $c = @()
    if ($GodotPath) { $c += $GodotPath }
    if ($env:GODOT) { $c += $env:GODOT }
    $pf = Join-Path $PSScriptRoot "godot.path"
    if (Test-Path $pf) {
        $l = Get-Content $pf | Where-Object { $_ -and -not $_.StartsWith("#") } | Select-Object -First 1
        if ($l) { $c += $l.Trim() }
    }
    $c += "M:\Godot_v4.7-stable_mono_win64"
    foreach ($p in $c) {
        $p = $p.Trim('"')
        if (-not (Test-Path -LiteralPath $p)) { continue }
        if ((Get-Item -LiteralPath $p).PSIsContainer) {
            $exe = Join-Path $p "Godot_v4.7-stable_mono_win64_console.exe"
            if (-not (Test-Path $exe)) { $exe = Join-Path $p "Godot_v4.7-stable_mono_win64.exe" }
            if (Test-Path $exe) { return $exe }
        } else { return $p }
    }
    throw "Godot 4.7 .NET not found. Put its folder in tools\godot.path, or set `$env:GODOT / -GodotPath."
}

Step "Artillery3D production build"

Step "Checking tools"
$sdk = (& dotnet --list-sdks | Where-Object { $_ -match '^(\d+)\.' -and [int]$Matches[1] -ge 8 } | Select-Object -Last 1)
if (-not $sdk) { throw ".NET SDK 8+ required." }
Ok ".NET SDK $sdk"
$godot = Resolve-Godot
Ok $godot
$tpl = Join-Path ([Environment]::GetFolderPath("ApplicationData")) "Godot\export_templates"
$tplDir = Get-ChildItem $tpl -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -match '^4\.7.*mono' } | Select-Object -First 1
if (-not $tplDir -or -not (Test-Path (Join-Path $tplDir.FullName "windows_release_x86_64.exe"))) {
    throw "Godot 4.7 .NET export templates missing in $tpl (Editor > Manage Export Templates)."
}
Ok "Export templates $($tplDir.Name)"

Step "Building solution (Release)"
dotnet build $Solution -c Release --nologo -v minimal
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed" }

if (-not $SkipTests) {
    Step "Running core tests"
    dotnet test $Tests -c Release --nologo -v minimal --no-build
    if ($LASTEXITCODE -ne 0) { throw "tests failed" }
}

Step "Writing export preset"
if (Test-Path $Out) { Remove-Item $Out -Recurse -Force }
New-Item -ItemType Directory $Out -Force | Out-Null
$rel = "../$OutputDir/$ExeName"
$cfg = @"
[preset.0]

name="Windows Desktop"
platform="Windows Desktop"
runnable=true
advanced_options=false
dedicated_server=false
custom_features=""
export_filter="all_resources"
include_filter=""
exclude_filter="export_presets.cfg, *.md"
export_path="$rel"
encryption_include_filters=""
encryption_exclude_filters=""
encrypt_pck=false
encrypt_directory=false
script_export_mode=2

[preset.0.options]

custom_template/debug=""
custom_template/release=""
debug/export_console_wrapper=0
binary_format/embed_pck=false
texture_format/s3tc_bptc=true
texture_format/etc2_astc=false
binary_format/architecture="x86_64"
codesign/enable=false
application/modify_resources=true
application/icon=""
application/company_name="High Oak Games Studio"
application/product_name="Artillery3D"
application/file_description="Artillery3D"
application/file_version=""
application/product_version=""
application/copyright=""
ssh_remote_deploy/enabled=false
dotnet/include_scripts_content=false
dotnet/include_debug_symbols=false
dotnet/embed_build_outputs=false
"@
# Godot's ConfigFile rejects a UTF-8 BOM.
[System.IO.File]::WriteAllText($PresetCfg, $cfg, (New-Object System.Text.UTF8Encoding($false)))

# Godot's C# export looks for game/Artillery3D.sln; the repo uses .slnx, so make a throwaway one.
if (-not (Test-Path $GodotSln)) {
    dotnet new sln --format sln -n Artillery3D -o $Project --force | Out-Null
    dotnet sln $GodotSln add (Join-Path $Project "Artillery3D.csproj") | Out-Null
}

try {
    Step "Exporting release build (self-contained .NET)"
    # first run imports assets so the export sees them
    & $godot --headless --path $Project --import 2>&1 | Out-Null
    & $godot --headless --path $Project --export-release "Windows Desktop" $rel
    if ($LASTEXITCODE -ne 0) { throw "Godot export failed (exit $LASTEXITCODE)" }
}
finally {
    Remove-Item $PresetCfg, $GodotSln -Force -ErrorAction SilentlyContinue
}
if (-not (Test-Path $ExePath)) { throw "Export did not produce $ExePath" }

Step "Verifying"
$files = Get-ChildItem $Out -Recurse -File
Ok "$ExeName ($([math]::Round((Get-Item $ExePath).Length / 1MB, 1)) MB)"
if (-not (Get-ChildItem $Out -Directory -Filter "data_*")) { throw "No data_* folder: .NET runtime not bundled." }
if (-not (Test-Path (Join-Path $Out "Artillery3D.pck"))) { throw "Artillery3D.pck missing." }

$commit = "unknown"
try { $commit = (git -C $Root rev-parse --short HEAD 2>$null).Trim() } catch {}
@{ product = "Artillery3D"; platform = "Windows x86_64"; builtAt = (Get-Date).ToUniversalTime().ToString("o"); gitCommit = $commit } |
    ConvertTo-Json | Set-Content (Join-Path $Out "build-manifest.json") -Encoding utf8
Ok ("Total: {0} files, {1} MB in {2}/" -f $files.Count, [math]::Round(($files | Measure-Object Length -Sum).Sum / 1MB, 1), $OutputDir)

if ($Zip) {
    Step "Creating zip"
    $zipPath = Join-Path $Root "Artillery3D-$commit-win64.zip"
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
    Compress-Archive -Path (Join-Path $Out "*") -DestinationPath $zipPath -CompressionLevel Optimal
    Ok $zipPath
}

Write-Host "`nRelease build complete: $ExePath" -ForegroundColor Green
Write-Host "Ship the whole $OutputDir/ folder (exe + pck + data_* folder)."
