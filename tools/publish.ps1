#!/usr/bin/env pwsh
# One-command release build:  pwsh tools/publish.ps1  [-Runtime win-x64] [-Output artifacts]
# Produces artifacts/RipOff-<runtime>.zip: a self-contained build with RipOff(.exe), steam_api64.dll (Windows) and
# steam_appid.txt (480) next to the executable. Works on Windows, Linux and macOS (cross-publishes win-x64).
# Needs the .NET 10 SDK; the first build downloads Steamworks.NET (pinned SHA-256, build/Steamworks.targets).
param(
    [string]$Runtime = "win-x64",
    [string]$Output = ""
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
if (-not $Output) { $Output = Join-Path $root "artifacts" }
$publishDir = Join-Path $Output "publish/$Runtime"
$zip = Join-Path $Output "RipOff-$Runtime.zip"
$project = Join-Path $root "src/RipOff.Game/RipOff.Game.csproj"

if (Test-Path $publishDir) { Remove-Item -Recurse -Force $publishDir }
New-Item -ItemType Directory -Force -Path $Output | Out-Null

# -r on the command line (not only in the profile) so build/Steamworks.targets sees the RID at evaluation time.
& dotnet publish $project -c Release -r $Runtime --self-contained true -p:PublishSingleFile=false -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }

$isWin = $Runtime.StartsWith("win")
$exe = if ($isWin) { "RipOff.exe" } else { "RipOff" }
$steamLib = if ($isWin) { "steam_api64.dll" } elseif ($Runtime.StartsWith("osx")) { "libsteam_api.dylib" } else { "libsteam_api.so" }
foreach ($f in @($exe, "steam_appid.txt", $steamLib, "Steamworks.NET.dll")) {
    if (-not (Test-Path (Join-Path $publishDir $f))) { throw "publish output is missing $f" }
}
if ((Get-Content (Join-Path $publishDir "steam_appid.txt") -Raw).Trim() -ne "480") { throw "steam_appid.txt is not 480" }

if (Test-Path $zip) { Remove-Item -Force $zip }
Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $zip
$size = [math]::Round((Get-Item $zip).Length / 1MB, 1)
Write-Host "Created $zip ($size MB)"
