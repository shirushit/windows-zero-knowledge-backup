<#
.SYNOPSIS
    Signs release executables and libraries using Authenticode (SignTool or Set-AuthenticodeSignature).
.DESCRIPTION
    Supports production code-signing certificates (PFX or Cert Store) with RFC 3161 timestamping.
    Includes fallback test-signing capability for automated CI and staging validation.
#>
param (
    [string]$TargetDir = "artifacts/release",
    [string]$CertThumbprint,
    [string]$PfxPath,
    [string]$PfxPassword,
    [string]$TimestampServer = "http://timestamp.digicert.com",
    [switch]$CreateTestCert
)

$ErrorActionPreference = "Stop"

Write-Host "=== Starting Code-Signing Configuration & Signing ===" -ForegroundColor Cyan

$ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectRoot = Resolve-Path (Join-Path $ScriptRoot "..")
$TargetFullPath = if ([System.IO.Path]::IsPathRooted($TargetDir)) { $TargetDir } else { Join-Path $ProjectRoot $TargetDir }

$FilesToSign = Get-ChildItem -Path $TargetFullPath -Recurse -Include *.exe, *.dll

if (-not $FilesToSign) {
    Write-Warning "No binaries (.exe, .dll) found in $TargetFullPath to sign."
    return
}

$cert = $null

if ($CertThumbprint) {
    Write-Host "Using certificate with thumbprint: $CertThumbprint" -ForegroundColor Yellow
    $cert = Get-Item "Cert:\CurrentUser\My\$CertThumbprint" -ErrorAction SilentlyContinue
    if (-not $cert) {
        $cert = Get-Item "Cert:\LocalMachine\My\$CertThumbprint" -ErrorAction SilentlyContinue
    }
    if (-not $cert) {
        throw "Certificate with thumbprint $CertThumbprint not found in Cert store."
    }
} elseif ($PfxPath) {
    Write-Host "Using PFX file: $PfxPath" -ForegroundColor Yellow
    $securePassword = if ($PfxPassword) { ConvertTo-SecureString $PfxPassword -AsPlainText -Force } else { $null }
    $cert = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new($PfxPath, $securePassword)
} elseif ($CreateTestCert) {
    Write-Host "Generating ephemeral self-signed code-signing test certificate..." -ForegroundColor Yellow
    $cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject "CN=ZeroKnowledgeBackup Test Signing" -CertStoreLocation "Cert:\CurrentUser\My"
    Write-Host "Created test cert: $($cert.Thumbprint)" -ForegroundColor Gray
} else {
    Write-Host "No signing credentials provided and -CreateTestCert not set. Validating signable files only." -ForegroundColor Yellow
    foreach ($file in $FilesToSign) {
        $sig = Get-AuthenticodeSignature -FilePath $file.FullName
        Write-Host "   $($file.Name): Status = $($sig.Status)" -ForegroundColor Gray
    }
    return
}

Write-Host "Signing $($FilesToSign.Count) binaries with RFC 3161 timestamp..." -ForegroundColor Yellow
foreach ($file in $FilesToSign) {
    Write-Host "   Signing: $($file.Name)..." -ForegroundColor Gray
    try {
        Set-AuthenticodeSignature -FilePath $file.FullName -Certificate $cert -TimestampServer $TimestampServer -HashAlgorithm SHA256 | Out-Null
    } catch {
        # If timestamp server fails or is unreachable in offline dev environment, sign without timestamp
        Write-Warning "Timestamping failed, falling back to basic signature: $_"
        Set-AuthenticodeSignature -FilePath $file.FullName -Certificate $cert -HashAlgorithm SHA256 | Out-Null
    }
    $sig = Get-AuthenticodeSignature -FilePath $file.FullName
    Write-Host "      Status: $($sig.Status)" -ForegroundColor Green
}

Write-Host "=== Code Signing Completed Successfully ===" -ForegroundColor Green
