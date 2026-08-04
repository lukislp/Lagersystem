<#
.SYNOPSIS
    One-time creation of a self-signed code-signing certificate for local
    development, installed into the trust stores so assemblies signed by
    MSBuild are allowed by Windows Application Control (WDAC / Defender
    Application Control).

.DESCRIPTION
    - Creates a cert "LagersystemHome Dev Code Signing" in the CurrentUser\My store.
    - Exports the cert (without private key) as .cer.
    - Imports the .cer into LocalMachine\Root (Trusted Root CA) and
      LocalMachine\TrustedPublisher (so WDAC trusts it).
    - Saves the thumbprint to scripts\codesigning.thumbprint, so
      Directory.Build.props finds the right identity.

    NOTE: With Smart App Control active (Win11), a self-signed certificate
    is NOT sufficient - SAC requires cloud reputation. In that case SAC
    must be disabled once (irreversible without a Windows reinstall).

.NOTES
    Script must run in an ELEVATED PowerShell, since importing into
    LocalMachine\Root and LocalMachine\TrustedPublisher requires admin rights.
#>

[CmdletBinding()]
param(
    [string]$Subject = "CN=LagersystemHome Dev Code Signing",
    [string]$FriendlyName = "LagersystemHome Dev Code Signing",
    [int]$ValidYears = 5
)

$ErrorActionPreference = 'Stop'

# Check for admin rights
$isAdmin = ([Security.Principal.WindowsPrincipal] `
    [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)

if (-not $isAdmin) {
    Write-Error "This script must be run in an ELEVATED PowerShell (right-click -> Run as Administrator)."
    exit 1
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$thumbprintFile = Join-Path $PSScriptRoot 'codesigning.thumbprint'
$cerPath = Join-Path $PSScriptRoot 'codesigning.cer'

# 1) Reuse an existing cert if present, otherwise create a new one
$cert = Get-ChildItem Cert:\CurrentUser\My |
    Where-Object { $_.Subject -eq $Subject -and $_.HasPrivateKey } |
    Sort-Object NotAfter -Descending |
    Select-Object -First 1

if ($null -eq $cert) {
    Write-Host "Creating new code-signing certificate: $Subject" -ForegroundColor Cyan
    $cert = New-SelfSignedCertificate `
        -Subject $Subject `
        -FriendlyName $FriendlyName `
        -CertStoreLocation 'Cert:\CurrentUser\My' `
        -Type CodeSigningCert `
        -KeyAlgorithm RSA `
        -KeyLength 2048 `
        -HashAlgorithm SHA256 `
        -NotAfter (Get-Date).AddYears($ValidYears) `
        -KeyUsage DigitalSignature `
        -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3')
} else {
    Write-Host "Using existing certificate: $($cert.Thumbprint)" -ForegroundColor Yellow
}

# 2) Export the public key
Export-Certificate -Cert $cert -FilePath $cerPath -Force | Out-Null
Write-Host "Public key exported to: $cerPath" -ForegroundColor Green

# 3) Import into Trusted Root + TrustedPublisher (LocalMachine)
foreach ($store in @('Root', 'TrustedPublisher')) {
    $storePath = "Cert:\LocalMachine\$store"
    $existing = Get-ChildItem $storePath | Where-Object { $_.Thumbprint -eq $cert.Thumbprint }
    if ($null -eq $existing) {
        Write-Host "Importing cert into $storePath ..." -ForegroundColor Cyan
        Import-Certificate -FilePath $cerPath -CertStoreLocation $storePath | Out-Null
    } else {
        Write-Host "Cert is already in $storePath" -ForegroundColor Yellow
    }
}

# 4) Persist the thumbprint for MSBuild
Set-Content -Path $thumbprintFile -Value $cert.Thumbprint -Encoding ASCII -NoNewline
Write-Host "Thumbprint written: $thumbprintFile" -ForegroundColor Green
Write-Host ""
Write-Host "DONE. Thumbprint: $($cert.Thumbprint)" -ForegroundColor Green
Write-Host "Next step: rebuild the solution -> output assemblies will be signed automatically."
