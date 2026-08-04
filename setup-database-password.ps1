<#
.SYNOPSIS
    Encrypts the database password for LagerSystem and stores it in
    Pass/db.password.enc using AES-256-CBC.

.DESCRIPTION
    The SecureConnectionStringProvider decrypts this file at application
    startup and replaces the Password=PLACEHOLDER token inside the
    connection string with the plaintext password - so the real password
    is never written to appsettings.json, never committed to git and never
    printed in logs.

    Layout of the Pass/ folder after running this script:

        Pass/
        |-- encryption.key      (base64-encoded 32-byte AES-256 key)
        |-- db.password.enc     (base64-encoded IV + ciphertext)

    Both files are already excluded by .gitignore. Back them up out-of-band
    or recreate them by re-running this script on a new machine.

.PARAMETER ProjectRoot
    Path to the LagersystemLVHome project folder. Defaults to a folder
    named 'LagersystemLVHome' next to this script.

.PARAMETER Force
    Overwrite existing Pass/ files without prompting.

.EXAMPLE
    ./setup-database-password.ps1

    Interactive prompt for the database password; (re-)creates the key if
    none exists.

.EXAMPLE
    ./setup-database-password.ps1 -Force

    Regenerate both the encryption key and the encrypted password.

.NOTES
    - Run once per deployment target. Do not commit Pass/ to git.
    - Keep Pass/encryption.key on a separate medium from
      Pass/db.password.enc in production (volume mount, secret store).
#>
[CmdletBinding()]
param(
    [string]$ProjectRoot,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'

if (-not $ProjectRoot) {
    $ProjectRoot = Join-Path $PSScriptRoot 'LagersystemLVHome'
}

if (-not (Test-Path $ProjectRoot)) {
    throw "Project folder not found: $ProjectRoot. Pass -ProjectRoot explicitly."
}

$passDir      = Join-Path $ProjectRoot 'Pass'
$keyFile      = Join-Path $passDir 'encryption.key'
$passwordFile = Join-Path $passDir 'db.password.enc'

if (-not (Test-Path $passDir)) {
    New-Item -ItemType Directory -Path $passDir | Out-Null
    Write-Host "Created $passDir" -ForegroundColor DarkGray
}

# ---------------------------------------------------------------------------
# 1) Encryption key (32 bytes, base64 encoded)
# ---------------------------------------------------------------------------
if ((Test-Path $keyFile) -and -not $Force) {
    Write-Host "Encryption key already present at $keyFile" -ForegroundColor Yellow
} else {
    $keyBytes = New-Object byte[] 32
    [System.Security.Cryptography.RandomNumberGenerator]::Fill($keyBytes)
    [System.IO.File]::WriteAllText($keyFile, [Convert]::ToBase64String($keyBytes))
    Write-Host "Wrote new 32-byte AES-256 key to $keyFile" -ForegroundColor Green
}

# ---------------------------------------------------------------------------
# 2) Encrypt the password
# ---------------------------------------------------------------------------
$securePassword = Read-Host -AsSecureString -Prompt 'Enter the database password'
$confirmPassword = Read-Host -AsSecureString -Prompt 'Repeat the database password'

$plain1 = [Runtime.InteropServices.Marshal]::PtrToStringUni(
    [Runtime.InteropServices.Marshal]::SecureStringToGlobalAllocUnicode($securePassword))
$plain2 = [Runtime.InteropServices.Marshal]::PtrToStringUni(
    [Runtime.InteropServices.Marshal]::SecureStringToGlobalAllocUnicode($confirmPassword))

if ($plain1 -ne $plain2) {
    throw 'Passwords do not match. Aborting.'
}
if ([string]::IsNullOrEmpty($plain1)) {
    throw 'Empty password. Aborting.'
}

try {
    $keyBytes = [Convert]::FromBase64String(
        ([System.IO.File]::ReadAllText($keyFile)).Trim())
    if ($keyBytes.Length -ne 32) {
        throw "encryption.key is not 32 bytes (length=$($keyBytes.Length))."
    }

    $iv = New-Object byte[] 16
    [System.Security.Cryptography.RandomNumberGenerator]::Fill($iv)

    $aes = [System.Security.Cryptography.Aes]::Create()
    try {
        $aes.Key = $keyBytes
        $aes.IV  = $iv
        $aes.Mode = [System.Security.Cryptography.CipherMode]::CBC
        $aes.Padding = [System.Security.Cryptography.PaddingMode]::PKCS7

        $encryptor = $aes.CreateEncryptor()
        try {
            $plainBytes = [System.Text.Encoding]::UTF8.GetBytes($plain1)
            $cipher = $encryptor.TransformFinalBlock($plainBytes, 0, $plainBytes.Length)
        } finally {
            $encryptor.Dispose()
        }
    } finally {
        $aes.Dispose()
    }

    $combined = New-Object byte[] ($iv.Length + $cipher.Length)
    [Array]::Copy($iv, 0, $combined, 0, $iv.Length)
    [Array]::Copy($cipher, 0, $combined, $iv.Length, $cipher.Length)

    [System.IO.File]::WriteAllText($passwordFile, [Convert]::ToBase64String($combined))

    Write-Host ''
    Write-Host "Wrote encrypted password to $passwordFile" -ForegroundColor Green
    Write-Host 'Start the app and the Password=PLACEHOLDER token will be replaced at runtime.' -ForegroundColor Green
}
finally {
    # Zero out the plaintext password from memory.
    $plain1 = $null
    $plain2 = $null
    [System.GC]::Collect()
}
