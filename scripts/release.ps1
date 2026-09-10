<#
.SYNOPSIS
    Publishes SmartCord self-contained for win-x64 and zips the output.
    No installer, no update feed -- this is a tool I built for myself, not a
    product with a support lifecycle. Grab the zip, unzip it, run the exe.

.EXAMPLE
    ./scripts/release.ps1 -Version 0.1.0
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$Version
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$publishDir = Join-Path $root "SmartCord\bin\Release\net8.0-windows\win-x64\publish"
$distDir = Join-Path $root "dist"
$stageName = "SmartCord-v$Version-win-x64"
$stageDir = Join-Path $distDir $stageName
$zipPath = Join-Path $distDir "$stageName.zip"

Write-Host "publishing v$Version..."
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }

dotnet publish (Join-Path $root "SmartCord\SmartCord.csproj") `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=false -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

# appsettings.local.json / .env / secrets.dat never belong in a release -- they're
# machine-specific and gitignored for the same reason. Belt-and-suspenders in case a
# stray one ever ends up next to the build output.
Get-ChildItem $publishDir -Filter "appsettings.local.json" -ErrorAction SilentlyContinue | Remove-Item -Force
Get-ChildItem $publishDir -Filter "secrets.dat" -ErrorAction SilentlyContinue | Remove-Item -Force
Get-ChildItem $publishDir -Filter ".env" -Force -ErrorAction SilentlyContinue | Remove-Item -Force

Write-Host "staging..."
if (Test-Path $stageDir) { Remove-Item $stageDir -Recurse -Force }
New-Item -ItemType Directory -Path $stageDir -Force | Out-Null
Copy-Item (Join-Path $publishDir "*") $stageDir -Recurse
Copy-Item (Join-Path $root ".env.example") $stageDir
Copy-Item (Join-Path $root "README.md") $stageDir

Write-Host "zipping..."
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path (Join-Path $stageDir "*") -DestinationPath $zipPath
Remove-Item $stageDir -Recurse -Force

Write-Host ""
Write-Host "done -> $zipPath"
Write-Host "  $([math]::Round((Get-Item $zipPath).Length / 1MB, 1)) MB"
