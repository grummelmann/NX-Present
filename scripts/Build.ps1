[CmdletBinding()]
param(
    [switch]$Installer,
    [string]$InstallerCompiler,
    [string]$DotNet = 'dotnet'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    function Invoke-DotNet([string[]]$Arguments) {
        & $DotNet @Arguments
        if ($LASTEXITCODE -ne 0) { throw "dotnet failed (exit $LASTEXITCODE): $Arguments" }
    }
    [xml]$project = Get-Content -LiteralPath 'src/NX-Present/NX-Present.csproj'
    $version = [string]$project.Project.PropertyGroup.Version
    $buildId = Get-Date -Format 'yyyyMMdd-HHmmss'
    $appDir = Join-Path $root "artifacts/publish/$version/$buildId"
    $releaseDir = Join-Path $root 'artifacts/release'
    New-Item -ItemType Directory -Path $appDir,$releaseDir -Force | Out-Null
    Invoke-DotNet -Arguments @('restore','src/NX-Present/NX-Present.csproj','--locked-mode')
    Invoke-DotNet -Arguments @('build','src/NX-Present/NX-Present.csproj','-c','Release','--no-restore')
    Invoke-DotNet -Arguments @('run','--project','tests/NX-Present.Validation/NX-Present.Validation.csproj','-c','Release')
    Invoke-DotNet -Arguments @('publish','src/NX-Present/NX-Present.csproj','-c','Release','-r','win-x64','--self-contained','true','-o',$appDir,'-p:DebugType=None','-p:DebugSymbols=false')
    & (Join-Path $PSScriptRoot 'Prepare-PluginCache.ps1') -AppDirectory $appDir
    Copy-Item -LiteralPath 'LICENSE.txt' -Destination (Join-Path $appDir 'LICENSE-NX-Present.txt')
    Copy-Item -LiteralPath 'THIRD-PARTY.md' -Destination $appDir
    Copy-Item -LiteralPath 'licenses' -Destination $appDir -Recurse
    Copy-Item -LiteralPath 'docs/Hilfe.html' -Destination $appDir
    if ($Installer) {
        if (-not $InstallerCompiler) {
            $candidate = Get-Command ISCC.exe -ErrorAction SilentlyContinue
            if ($candidate) { $InstallerCompiler = $candidate.Source }
            else { $InstallerCompiler = Join-Path $env:ProgramFiles 'Inno Setup 7/ISCC.exe' }
        }
        if (-not (Test-Path -LiteralPath $InstallerCompiler)) { throw 'Inno Setup 7 nicht gefunden. -InstallerCompiler mit dem Pfad zu ISCC.exe angeben.' }
        $readmeText = Get-Content -LiteralPath 'README.md' -Raw
        $urlMatch = [regex]::Match($readmeText,'https://github\.com/[^/\s)]+/NX-Present')
        if (-not $urlMatch.Success) { throw 'Die offizielle GitHub-Projekt-URL fehlt in README.md.' }
        $projectUrl = $urlMatch.Value
        if ((Get-Content -LiteralPath 'LICENSE.txt' -Raw).Contains('{{')) { throw 'Unaufgelöste Platzhalter in der Projektlizenz.' }
        & (Join-Path $PSScriptRoot 'Get-WebView2.ps1')
        & $InstallerCompiler ("--define=AppVersion=$version") ("--define=AppDir=$appDir") ("--define=ReleaseDir=$releaseDir") ("--define=ProjectUrl=$projectUrl") 'installer/NX-Present.iss'
        if ($LASTEXITCODE -ne 0) { throw 'Installer-Kompilierung fehlgeschlagen.' }
        $setup = Join-Path $releaseDir "NX-Present-Setup-$version-x64.exe"
        $hash = (Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash.ToLowerInvariant()
        "$hash  $([IO.Path]::GetFileName($setup))" | Set-Content -LiteralPath (Join-Path $releaseDir 'SHA256SUMS.txt') -Encoding ascii
        Write-Host "Installer: $setup"
    }
    Write-Host "Anwendung: $appDir"
} finally { Pop-Location }
