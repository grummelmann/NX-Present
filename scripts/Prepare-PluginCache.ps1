[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$AppDirectory)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$cacheDirectory=Join-Path $root 'installer/cache'
$manifest=Get-Content -LiteralPath (Join-Path $cacheDirectory 'manifest.json') -Raw | ConvertFrom-Json
$native=Join-Path $AppDirectory 'libvlc/win-x64'
$cache=Join-Path $cacheDirectory 'plugins-3.0.24-x64.dat'
if((Get-FileHash -LiteralPath $cache -Algorithm SHA256).Hash -ne $manifest.CacheSha256){throw 'Plugin cache integrity check failed'}
if((Get-FileHash -LiteralPath (Join-Path $native 'libvlccore.dll') -Algorithm SHA256).Hash -ne $manifest.CoreSha256){throw 'LibVLC version changed; regenerate and verify the plugin cache before packaging'}
$plugins=Join-Path $native 'plugins'
if(@(Get-ChildItem -LiteralPath $plugins -Filter '*.dll' -File -Recurse).Count -ne $manifest.Plugins.Count){throw 'Plugin set changed; regenerate the cache'}
foreach($entry in $manifest.Plugins){
 $path=Join-Path $plugins $entry.Path
 if((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $entry.Sha256){throw ('Plugin differs from cache manifest: '+$entry.Path)}
 # VLC caches include file mtimes. Normalize verified build artifacts to the
 # timestamps used by this architecture/version-specific generated catalog.
 [IO.File]::SetLastWriteTimeUtc($path,([DateTime]$entry.LastWriteUtc).ToUniversalTime())
}
Copy-Item -LiteralPath $cache -Destination (Join-Path $plugins 'plugins.dat') -Force
Write-Host ('VLC plugin catalog prepared for '+$manifest.Plugins.Count+' verified plugins.')
