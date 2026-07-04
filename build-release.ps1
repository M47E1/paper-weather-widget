#Requires -Version 5.1
param(
    [string]$Version = '',
    [switch]$SkipPs2ExeDownload
)

$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$repoRoot = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
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

function Assert-NoForbiddenPackageFiles {
    param([string]$Root)

    $matches = @(Get-ChildItem -LiteralPath $Root -Recurse -Force -File -ErrorAction SilentlyContinue | Where-Object {
        $_.Name -eq 'LonghuaWeatherWidget.settings.json' -or $_.Name -eq 'LonghuaWeatherWidget.ps1'
    })
    if ($matches.Count -gt 0) {
        throw ('Release package contains forbidden local or legacy file: {0}' -f (($matches | Select-Object -ExpandProperty FullName) -join '; '))
    }
}

$Version = Resolve-BuildVersion -RequestedVersion $Version
if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw 'Version must use Major.Minor.Patch format, for example 2.0.1.'
}

$sourcePath = Join-Path $repoRoot 'LonghuaWeatherWidget.ps1'
$licensePath = Join-Path $repoRoot 'LICENSE'
$distDir = Join-Path $repoRoot 'dist'
$distFullPath = [IO.Path]::GetFullPath($distDir)

if (-not (Test-Path -LiteralPath $sourcePath)) {
    throw "Source script not found: $sourcePath"
}
if (-not (Test-Path -LiteralPath $licensePath)) {
    throw "LICENSE not found: $licensePath"
}
if (-not $distFullPath.StartsWith($repoRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to clean dist outside repo: $distFullPath"
}

$productName = 'Paper Weather Widget'
$assetBaseName = "PaperWeatherWidget-v$Version-win-x64"
$packageExeName = 'PaperWeatherWidget.exe'
$exeName = "$assetBaseName.exe"
$zipName = "$assetBaseName.zip"
$exePath = Join-Path $distDir $exeName
$zipPath = Join-Path $distDir $zipName
$shaPath = Join-Path $distDir 'SHA256SUMS.txt'

function Import-PS2EXEForBuild {
    param([switch]$SkipDownload)

    $command = Get-Command Invoke-PS2EXE -ErrorAction SilentlyContinue
    if ($null -ne $command) {
        return
    }

    $moduleRoot = Join-Path ([IO.Path]::GetTempPath()) 'LonghuaWeatherWidget-build-modules'
    if (Test-Path -LiteralPath $moduleRoot) {
        $modulePaths = @($env:PSModulePath -split [regex]::Escape([string][IO.Path]::PathSeparator))
        if ($modulePaths -notcontains $moduleRoot) {
            $env:PSModulePath = $moduleRoot + [IO.Path]::PathSeparator + $env:PSModulePath
        }
    }

    $existingModule = Get-Module -ListAvailable -Name ps2exe | Select-Object -First 1
    if ($null -ne $existingModule) {
        Import-Module ps2exe -ErrorAction Stop
        return
    }

    if ($SkipDownload) {
        throw 'PS2EXE is not available. Re-run without -SkipPs2ExeDownload or install it for the build machine only.'
    }

    if (-not (Get-Command Save-Module -ErrorAction SilentlyContinue)) {
        throw 'Save-Module is required to fetch PS2EXE as a build dependency.'
    }

    $moduleRoot = Join-Path ([IO.Path]::GetTempPath()) 'LonghuaWeatherWidget-build-modules'
    if (-not (Test-Path -LiteralPath $moduleRoot)) {
        New-Item -ItemType Directory -Path $moduleRoot -Force | Out-Null
    }

    if (-not (Get-PackageProvider -Name NuGet -ListAvailable -ErrorAction SilentlyContinue)) {
        Install-PackageProvider -Name NuGet -MinimumVersion 2.8.5.201 -Force -Scope CurrentUser | Out-Null
    }

    Save-Module -Name ps2exe -Path $moduleRoot -Repository PSGallery -Force
    $env:PSModulePath = $moduleRoot + [IO.Path]::PathSeparator + $env:PSModulePath
    Import-Module ps2exe -ErrorAction Stop
}

Import-PS2EXEForBuild -SkipDownload:$SkipPs2ExeDownload

if (Test-Path -LiteralPath $distDir) {
    Remove-Item -LiteralPath $distDir -Recurse -Force
}
New-Item -ItemType Directory -Path $distDir -Force | Out-Null

$fileVersion = "$Version.0"
$iconPath = Join-Path $repoRoot 'assets\liquidmetal-sun.ico'
$ps2exeParams = @{
    InputFile = $sourcePath
    OutputFile = $exePath
    NoConsole = $true
    STA = $true
    DPIAware = $true
    SupportOS = $true
    X64 = $true
    Title = $productName
    Description = 'Anthropic-inspired local Windows PowerShell WPF weather widget.'
    Product = $productName
    Company = 'Paper Weather Widget Project'
    Copyright = 'Copyright (c) 2026 Paper Weather Widget contributors'
    Version = $fileVersion
}

if (Test-Path -LiteralPath $iconPath) { $ps2exeParams.IconFile = $iconPath }
Invoke-PS2EXE @ps2exeParams

if (-not (Test-Path -LiteralPath $exePath)) {
    throw "PS2EXE did not create: $exePath"
}

$packageDir = Join-Path ([IO.Path]::GetTempPath()) ('LonghuaWeatherWidget-package-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $packageDir -Force | Out-Null
try {
    Copy-Item -LiteralPath $exePath -Destination (Join-Path $packageDir $packageExeName) -Force
    Copy-Item -LiteralPath $licensePath -Destination (Join-Path $packageDir 'LICENSE') -Force

    $packageReadme = @"
Paper Weather Widget v$Version

Run PaperWeatherWidget.exe. No administrator rights are required.

Settings are stored under the current user's local app data folder. The app does not require an API key, account, telemetry, or paid weather service.

This edition uses an Anthropic-inspired style: paper-toned surfaces, restrained borders, and warm accent colors. It is not affiliated with, endorsed by, or using brand assets from Anthropic or Claude.

The app uses Open-Meteo as the primary weather provider and wttr.in as fallback. Weather data by Open-Meteo.

This build is unsigned. Windows SmartScreen may show an unknown publisher warning on first launch.
"@
    Set-Content -LiteralPath (Join-Path $packageDir 'README.txt') -Value $packageReadme -Encoding ASCII

    Assert-NoForbiddenPackageFiles -Root $packageDir

    if (Test-Path -LiteralPath $zipPath) {
        Remove-Item -LiteralPath $zipPath -Force
    }
    Compress-Archive -Path (Join-Path $packageDir '*') -DestinationPath $zipPath -Force
} finally {
    if (Test-Path -LiteralPath $packageDir) {
        Remove-Item -LiteralPath $packageDir -Recurse -Force
    }
}

$hashLines = foreach ($artifact in @($exePath, $zipPath)) {
    $hash = Get-FileHash -Algorithm SHA256 -LiteralPath $artifact
    '{0}  {1}' -f $hash.Hash.ToLowerInvariant(), (Split-Path -Leaf $artifact)
}
Set-Content -LiteralPath $shaPath -Value $hashLines -Encoding ASCII

Write-Host "EXE: $exePath"
Write-Host "ZIP: $zipPath"
Write-Host "SHA256: $shaPath"
