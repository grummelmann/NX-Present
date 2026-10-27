[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$manifest=Get-Content -LiteralPath (Join-Path $root 'licenses/third-party-sources.json') -Raw | ConvertFrom-Json
$destination=Join-Path $root 'artifacts/third-party-sources'
New-Item -ItemType Directory -Path $destination -Force | Out-Null
$entries=@($manifest.Vlc)+@($manifest.Wrappers)+@($manifest.Contrib)
foreach($entry in $entries){
 $path=Join-Path $destination $entry.Name
 $algorithm=if($entry.Sha512){'SHA512'}else{'SHA256'}
 $expected=if($entry.Sha512){$entry.Sha512}else{$entry.Sha256}
 if(-not (Test-Path -LiteralPath $path) -or (Get-FileHash -LiteralPath $path -Algorithm $algorithm).Hash -ne $expected){
  Invoke-WebRequest -Uri $entry.Url -OutFile ($path+'.partial')
  if((Get-FileHash -LiteralPath ($path+'.partial') -Algorithm $algorithm).Hash -ne $expected){throw ('Source checksum mismatch: '+$entry.Name)}
  Move-Item -LiteralPath ($path+'.partial') -Destination $path -Force
 }
 Write-Host ('Verified '+$entry.Name)
}
Copy-Item -LiteralPath (Join-Path $root 'licenses/third-party-sources.json') -Destination (Join-Path $destination 'SOURCE-MANIFEST.json') -Force
Write-Host ('Verified third-party sources: '+$destination)
