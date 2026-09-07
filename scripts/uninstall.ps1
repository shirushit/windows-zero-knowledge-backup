<#
.SYNOPSIS
    Uninstalls the Windows Zero-Knowledge Backup Application.
.DESCRIPTION
    Removes application binaries, Start Menu shortcut, and Windows Add/Remove Programs registry entry.
    Optionally purges local application state and catalog databases if -PurgeData is specified.
#>
param (
    [string]$InstallDir = "$env:LOCALAPPDATA\Programs\BackupApp",
    [switch]$PurgeData
)

$ErrorActionPreference = "Stop"

Write-Host "=== Uninstalling Windows Zero-Knowledge Backup ===" -ForegroundColor Cyan

# Stop running process
$running = Get-Process -Name "BackupApp.UI" -ErrorAction SilentlyContinue
if ($running) {
    Write-Host "Stopping running BackupApp.UI process..." -ForegroundColor Yellow
    $running | Stop-Process -Force
    Start-Sleep -Seconds 1
}

# Remove Start Menu shortcut
$StartMenuDir = [System.IO.Path]::Combine($env:APPDATA, "Microsoft\Windows\Start Menu\Programs")
$ShortcutPath = Join-Path $StartMenuDir "Windows Zero-Knowledge Backup.lnk"
if (Test-Path $ShortcutPath) {
    Write-Host "Removing Start Menu shortcut..." -ForegroundColor Yellow
    Remove-Item -Path $ShortcutPath -Force
}

# Remove Add/Remove Programs registry entry
$UninstallKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\BackupApp"
if (Test-Path $UninstallKey) {
    Write-Host "Removing registry uninstall entry..." -ForegroundColor Yellow
    Remove-Item -Path $UninstallKey -Recurse -Force
}

# Remove program directory
if (Test-Path $InstallDir) {
    Write-Host "Removing application files from $InstallDir..." -ForegroundColor Yellow
    Remove-Item -Path $InstallDir -Recurse -Force
}

# Check data purge
$AppDataDir = [System.IO.Path]::Combine($env:LOCALAPPDATA, "BackupApp")
if ($PurgeData) {
    if (Test-Path $AppDataDir) {
        Write-Host "Purging local user data and catalog databases at $AppDataDir..." -ForegroundColor Yellow
        Remove-Item -Path $AppDataDir -Recurse -Force
    }
} else {
    Write-Host "Preserving user catalog and databases at $AppDataDir (use -PurgeData to remove)." -ForegroundColor Gray
}

Write-Host "=== Uninstallation Completed Successfully! ===" -ForegroundColor Green
