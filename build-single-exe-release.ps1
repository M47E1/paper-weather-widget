#Requires -Version 5.1
param(
    [string]$Version = ''
)

$ErrorActionPreference = 'Stop'

$repoRoot = if ($PSScriptRoot) { $PSScriptRoot } else { (Get-Location).Path }
$repoRoot = [IO.Path]::GetFullPath($repoRoot)

function Resolve-BuildVersion {
    param([string]$RequestedVersion)

    if (-not [string]::IsNullOrWhiteSpace($RequestedVersion)) {
        return $RequestedVersion.Trim()
    }
    $versionPath = Join-Path $repoRoot 'VERSION'
    if (Test-Path -LiteralPath $versionPath -PathType Leaf) {
        return ((Get-Content -LiteralPath $versionPath -Raw).Trim())
    }
    $tag = (& git -C $repoRoot describe --tags --abbrev=0 2>$null)
    if ($LASTEXITCODE -eq 0 -and [string]$tag -match '^v?(\d+\.\d+\.\d+)$') {
        return $Matches[1]
    }
    throw 'Version was not provided and neither VERSION nor a SemVer git tag is available.'
}

$Version = Resolve-BuildVersion -RequestedVersion $Version
if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw 'Version must use Major.Minor.Patch format, for example 2.0.1.'
}

$releaseBase = [IO.Path]::GetFullPath((Join-Path $repoRoot 'dist\release'))
$releaseRoot = [IO.Path]::GetFullPath((Join-Path $releaseBase ("v$Version")))
$buildOutput = Join-Path $releaseRoot 'build'
$assetName = "PaperWeatherWidget-v$Version-win-x64.exe"
$assetPath = Join-Path $releaseRoot $assetName
$shaPath = Join-Path $releaseRoot 'SHA256SUMS.txt'
$buildScript = Join-Path $repoRoot 'src\launcher\build-launcher.ps1'

if (-not $releaseRoot.StartsWith($releaseBase, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to write release output outside dist\release: $releaseRoot"
}
if (-not (Test-Path -LiteralPath $buildScript -PathType Leaf)) {
    throw "Launcher build script not found: $buildScript"
}

if (Test-Path -LiteralPath $releaseRoot) {
    Remove-Item -LiteralPath $releaseRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $buildOutput -Force | Out-Null

& powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File $buildScript -OutputDir $buildOutput -Version $Version -SingleExe
if ($LASTEXITCODE -ne 0) {
    throw "Single EXE build failed with exit code $LASTEXITCODE"
}

$launcherExe = Join-Path $buildOutput 'WeatherLauncher.exe'
if (-not (Test-Path -LiteralPath $launcherExe -PathType Leaf)) {
    throw "Launcher EXE not found: $launcherExe"
}
Copy-Item -LiteralPath $launcherExe -Destination $assetPath -Force
Remove-Item -LiteralPath $buildOutput -Recurse -Force

$hash = Get-FileHash -Algorithm SHA256 -LiteralPath $assetPath
$hashLine = '{0}  {1}' -f $hash.Hash.ToLowerInvariant(), $assetName
Set-Content -LiteralPath $shaPath -Value $hashLine -Encoding ASCII

Write-Host "EXE: $assetPath"
Write-Host "SHA256: $shaPath"
