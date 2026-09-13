<#
.SYNOPSIS
    Builds, publishes, checksums, and packages the Windows Zero-Knowledge Backup Application.
.DESCRIPTION
    Produces a single-file, self-contained Release build for win-x64, generates SHA-256 checksums,
    and produces an SBOM manifest.
#>
param (
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$OutputDir = "artifacts/release"
)

$ErrorActionPreference = "Stop"
Write-Host "=== Starting Windows Zero-Knowledge Backup Release Build ===" -ForegroundColor Cyan

$ProjectRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$ProjectFile = Join-Path $ProjectRoot "src/BackupApp.UI/BackupApp.UI.csproj"
$OutputFullPath = Join-Path $ProjectRoot $OutputDir

if (Test-Path $OutputFullPath) {
    Remove-Item -Recurse -Force $OutputFullPath
}
New-Item -ItemType Directory -Force -Path $OutputFullPath | Out-Null

Write-Host "1. Publishing self-contained single-file executable..." -ForegroundColor Yellow
dotnet publish $ProjectFile `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $OutputFullPath

if ($LASTEXITCODE -ne 0) {
    throw "Publish failed with exit code $LASTEXITCODE"
}

Write-Host "2. Creating Release ZIP Archive..." -ForegroundColor Yellow
$ZipPackage = Join-Path $OutputFullPath "BackupApp-v1.0.0-win-x64.zip"
if (Test-Path $ZipPackage) { Remove-Item -Force $ZipPackage }
Compress-Archive -Path (Join-Path $OutputFullPath "BackupApp.UI.exe") -DestinationPath $ZipPackage -Force

Write-Host "3. Generating SHA-256 Checksums..." -ForegroundColor Yellow
$ChecksumFile = Join-Path $OutputFullPath "SHA256SUMS.txt"
$Files = Get-ChildItem -Path $OutputFullPath -File | Where-Object { $_.Name -ne "SHA256SUMS.txt" }
$ChecksumLines = @()

foreach ($file in $Files) {
    $hash = (Get-FileHash -Path $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    $line = "$hash  $($file.Name)"
    $ChecksumLines += $line
    Write-Host "   $line" -ForegroundColor Gray
}

$ChecksumLines | Out-File -FilePath $ChecksumFile -Encoding utf8

Write-Host "4. Generating SBOM Package Inventory..." -ForegroundColor Yellow
$SbomFile = Join-Path $OutputFullPath "SBOM.txt"
dotnet list $ProjectRoot package > $SbomFile

$LauncherDir = "C:\Users\owner\Desktop\BackupApp-Launcher"
if (Test-Path $LauncherDir) {
    Write-Host "5. Updating Desktop Launcher executable..." -ForegroundColor Yellow
    Copy-Item -Path (Join-Path $OutputFullPath "BackupApp.UI.exe") -Destination (Join-Path $LauncherDir "BackupApp.exe") -Force
    Write-Host "   Launcher executable updated at $LauncherDir\BackupApp.exe" -ForegroundColor Green
}

Write-Host "=== Release Packaging Completed Successfully at: $OutputFullPath ===" -ForegroundColor Green
