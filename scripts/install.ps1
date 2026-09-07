<#
.SYNOPSIS
    Installs the Windows Zero-Knowledge Backup Application for the current user.
.DESCRIPTION
    Installs the application into %LOCALAPPDATA%\Programs\BackupApp, creates Start Menu
    shortcut, and registers Windows Add/Remove Programs uninstall metadata.
#>
param (
    [string]$SourceDir = "artifacts/release",
    [string]$InstallDir = "$env:LOCALAPPDATA\Programs\BackupApp"
)

$ErrorActionPreference = "Stop"

Write-Host "=== Installing Windows Zero-Knowledge Backup ===" -ForegroundColor Cyan

$ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectRoot = Resolve-Path (Join-Path $ScriptRoot "..")
$SourceFullPath = if ([System.IO.Path]::IsPathRooted($SourceDir)) { $SourceDir } else { Join-Path $ProjectRoot $SourceDir }

$SourceExe = Join-Path $SourceFullPath "BackupApp.UI.exe"
if (-not (Test-Path $SourceExe)) {
    throw "Source executable not found at: $SourceExe. Please run scripts/build-release.ps1 first."
}

# Stop running processes if open
$running = Get-Process -Name "BackupApp.UI" -ErrorAction SilentlyContinue
if ($running) {
    Write-Host "Stopping running BackupApp.UI process..." -ForegroundColor Yellow
    $running | Stop-Process -Force
    Start-Sleep -Seconds 1
}

# Ensure destination directory exists
if (-not (Test-Path $InstallDir)) {
    New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
}

# Copy release files
Write-Host "Copying files to $InstallDir..." -ForegroundColor Yellow
Get-ChildItem -Path $SourceFullPath -Recurse | Copy-Item -Destination $InstallDir -Force

$InstalledExe = Join-Path $InstallDir "BackupApp.UI.exe"

# Create Start Menu Shortcut
Write-Host "Creating Start Menu shortcut..." -ForegroundColor Yellow
$StartMenuDir = [System.IO.Path]::Combine($env:APPDATA, "Microsoft\Windows\Start Menu\Programs")
$ShortcutPath = Join-Path $StartMenuDir "Windows Zero-Knowledge Backup.lnk"

$WshShell = New-Object -ComObject WScript.Shell
$Shortcut = $WshShell.CreateShortcut($ShortcutPath)
$Shortcut.TargetPath = $InstalledExe
$Shortcut.WorkingDirectory = $InstallDir
$Shortcut.Description = "Windows Zero-Knowledge Backup"
$Shortcut.Save()

# Register Add/Remove Programs entry in HKCU
Write-Host "Registering uninstall entry..." -ForegroundColor Yellow
$UninstallKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\BackupApp"
if (-not (Test-Path $UninstallKey)) {
    New-Item -Path $UninstallKey -Force | Out-Null
}

$UninstallScript = Join-Path $ScriptRoot "uninstall.ps1"
$UninstallCmd = "powershell.exe -ExecutionPolicy Bypass -File `"$UninstallScript`""

Set-ItemProperty -Path $UninstallKey -Name "DisplayName" -Value "Windows Zero-Knowledge Backup"
Set-ItemProperty -Path $UninstallKey -Name "DisplayVersion" -Value "1.0.0"
Set-ItemProperty -Path $UninstallKey -Name "Publisher" -Value "ZeroKnowledgeBackup"
Set-ItemProperty -Path $UninstallKey -Name "InstallLocation" -Value $InstallDir
Set-ItemProperty -Path $UninstallKey -Name "DisplayIcon" -Value "$InstalledExe,0"
Set-ItemProperty -Path $UninstallKey -Name "UninstallString" -Value $UninstallCmd
Set-ItemProperty -Path $UninstallKey -Name "NoModify" -Value 1 -Type DWord
Set-ItemProperty -Path $UninstallKey -Name "NoRepair" -Value 1 -Type DWord

Write-Host "=== Installation Completed Successfully! ===" -ForegroundColor Green
Write-Host "Application path: $InstalledExe" -ForegroundColor Green
Write-Host "Shortcut created: $ShortcutPath" -ForegroundColor Green
