#!/usr/bin/env pwsh
# One-command release build:  pwsh tools/publish.ps1  [-Version 2026.10.17.1] [-Runtime win-x64] [-Output artifacts]
# Produces artifacts/RipOff-<version>-<runtime>.zip: a self-contained build with RipOff(.exe), steam_api64.dll (Windows)
# and steam_appid.txt (480) next to the executable. Works on Windows, Linux and macOS (cross-publishes win-x64).
# Version (README "Versioning"): YYYY.MM.DD.N. Without -Version: today's date in America/New_York and N = 1 + the
# highest N of the tags v<date>.* found locally or on origin (tools/release/NextVersion). -Version dev builds a dev zip.
# Needs the .NET 10 SDK; the first build downloads Steamworks.NET (pinned SHA-256, build/Steamworks.targets).
param(
    [string]$Version = "",
    [string]$Runtime = "win-x64",
    [string]$Output = ""
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
if (-not $Output) { $Output = Join-Path $root "artifacts" }
$publishDir = Join-Path $Output "publish/$Runtime"
$project = Join-Path $root "src/RipOff.Game/RipOff.Game.csproj"
$nextVersion = Join-Path $root "tools/release/NextVersion/NextVersion.csproj"

function Invoke-NextVersion([string[]]$ToolArgs) {
    $out = & dotnet run --project $nextVersion -c Release -v q -- @ToolArgs
    if ($LASTEXITCODE -ne 0) { throw "NextVersion $($ToolArgs -join ' ') failed ($LASTEXITCODE)" }
    return ($out | Select-Object -Last 1).Trim()
}

if ($Version -eq "dev") {
    $Version = "0.0.0.0"
} elseif (-not $Version) {
    $tags = @(& git -C $root tag -l "v*")
    $remote = @(& git -C $root ls-remote --tags origin "refs/tags/v*" 2>$null | ForEach-Object { ($_ -split "\s+")[1] })
    if ($LASTEXITCODE -ne 0) { Write-Warning "could not list tags on origin; using local tags only" }
    $Version = Invoke-NextVersion (@("next") + $tags + $remote)
    Write-Host "Version (next for today, America/New_York): $Version"
} else {
    $Version = Invoke-NextVersion @("check", $Version)
}
$zipName = if ($Version -eq "0.0.0.0") { "RipOff-dev-$Runtime.zip" } else { "RipOff-$Version-$Runtime.zip" }
$zip = Join-Path $Output $zipName

if (Test-Path $publishDir) { Remove-Item -Recurse -Force $publishDir }
New-Item -ItemType Directory -Force -Path $Output | Out-Null

# -r on the command line (not only in the profile) so build/Steamworks.targets sees the RID at evaluation time.
& dotnet publish $project -c Release -r $Runtime --self-contained true -p:PublishSingleFile=false "-p:RipOffVersion=$Version" -o $publishDir
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
Write-Host "Created $zip ($size MB), version $Version"
