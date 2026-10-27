[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$directory = Join-Path $root 'installer/dependencies'
New-Item -ItemType Directory -Path $directory -Force | Out-Null
$path = Join-Path $directory 'MicrosoftEdgeWebview2Setup.exe'
if (-not (Test-Path -LiteralPath $path)) {
    Invoke-WebRequest -Uri 'https://go.microsoft.com/fwlink/p/?LinkId=2124703' -OutFile $path
}
$signature = Get-AuthenticodeSignature -LiteralPath $path
if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation') {
    throw 'Der WebView2-Bootstrapper hat keine gültige Microsoft-Signatur. Es wird kein Installer gebaut.'
}
Get-FileHash -LiteralPath $path -Algorithm SHA256 | Select-Object Algorithm,Hash
