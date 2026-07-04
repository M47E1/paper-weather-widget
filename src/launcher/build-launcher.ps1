#Requires -Version 5.1
param(
    [string]$OutputDir = '',
    [string]$Version = '',
    [switch]$SingleExe
)

$ErrorActionPreference = 'Stop'

$launcherRoot = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
$repoRoot = [IO.Path]::GetFullPath((Join-Path $launcherRoot '..\..'))

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

function Assert-NoForbiddenOutputFiles {
    param([string]$Root)

    $matches = @(Get-ChildItem -LiteralPath $Root -Recurse -Force -File -ErrorAction SilentlyContinue | Where-Object {
        $_.Name -eq 'LonghuaWeatherWidget.settings.json' -or $_.Name -eq 'LonghuaWeatherWidget.ps1'
    })
    if ($matches.Count -gt 0) {
        throw ('Build output contains forbidden local or legacy file: {0}' -f (($matches | Select-Object -ExpandProperty FullName) -join '; '))
    }
}

$Version = Resolve-BuildVersion -RequestedVersion $Version
if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw 'Version must use Major.Minor.Patch format, for example 2.0.1.'
}

if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path $repoRoot 'dist\launcher'
}
$OutputDir = [IO.Path]::GetFullPath($OutputDir)
if (-not $OutputDir.StartsWith($repoRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to write launcher output outside repo: $OutputDir"
}

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $csc -PathType Leaf)) {
    throw "C# compiler not found: $csc"
}

function Resolve-GacAssembly {
    param([string]$AssemblyName)

    $roots = @(
        (Join-Path $env:WINDIR "Microsoft.NET\assembly\GAC_MSIL\$AssemblyName"),
        (Join-Path $env:WINDIR "Microsoft.NET\assembly\GAC_64\$AssemblyName"),
        (Join-Path $env:WINDIR "Microsoft.NET\assembly\GAC_32\$AssemblyName")
    )

    foreach ($root in $roots) {
        if (-not (Test-Path -LiteralPath $root -PathType Container)) {
            continue
        }
        $match = Get-ChildItem -LiteralPath $root -Filter "$AssemblyName.dll" -Recurse -ErrorAction SilentlyContinue |
            Sort-Object FullName -Descending |
            Select-Object -First 1
        if ($null -ne $match) {
            return $match.FullName
        }
    }

    throw "Required assembly not found in GAC: $AssemblyName"
}

New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

$frameworkDir = Split-Path -Parent $csc
$references = @(
    (Join-Path $frameworkDir 'System.dll'),
    (Join-Path $frameworkDir 'System.Core.dll'),
    (Resolve-GacAssembly 'PresentationFramework'),
    (Resolve-GacAssembly 'PresentationCore'),
    (Resolve-GacAssembly 'WindowsBase'),
    (Resolve-GacAssembly 'System.Xaml'),
    (Resolve-GacAssembly 'System.Web.Extensions')
)

foreach ($reference in $references) {
    if (-not (Test-Path -LiteralPath $reference -PathType Leaf)) {
        throw "Reference not found: $reference"
    }
}

$sources = @(
    (Join-Path $launcherRoot 'WeatherLauncher.cs'),
    (Join-Path $launcherRoot 'App.xaml.cs'),
    (Join-Path $launcherRoot 'GlassEffects.cs'),
    (Join-Path $launcherRoot 'MainWindow.xaml.cs')
)

$generatedVersionSource = Join-Path ([IO.Path]::GetTempPath()) ('PaperWeatherWidgetLauncherVersion-' + [Guid]::NewGuid().ToString('N') + '.cs')
$fileVersion = "$Version.0"
$versionSource = @"
using System.Reflection;
[assembly: AssemblyTitle("Paper Weather Widget")]
[assembly: AssemblyDescription("Thin CLR WPF launcher for Paper Weather Widget.")]
[assembly: AssemblyProduct("Paper Weather Widget")]
[assembly: AssemblyCompany("Paper Weather Widget Project")]
[assembly: AssemblyCopyright("Copyright (c) 2026 Paper Weather Widget contributors")]
[assembly: AssemblyVersion("$fileVersion")]
[assembly: AssemblyFileVersion("$fileVersion")]
[assembly: AssemblyInformationalVersion("$Version")]
"@
Set-Content -LiteralPath $generatedVersionSource -Value $versionSource -Encoding ASCII
$sources += $generatedVersionSource

foreach ($source in $sources) {
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
        throw "Source not found: $source"
    }
}

$exePath = Join-Path $OutputDir 'WeatherLauncher.exe'
$compilerArgs = @(
    '/noconfig',
    '/nologo',
    '/target:winexe',
    '/platform:x64',
    "/out:$exePath"
)

$iconPath = Join-Path $repoRoot 'assets\liquidmetal-sun.ico'
if (Test-Path -LiteralPath $iconPath -PathType Leaf) {
    $compilerArgs += "/win32icon:$iconPath"
}

foreach ($reference in $references) {
    $compilerArgs += "/reference:$reference"
}

if ($SingleExe) {
    $workerResource = Join-Path $repoRoot 'src\worker\WeatherWorker.ps1'
    $catalogResource = Join-Path $repoRoot 'src\worker\ChinaRegionCatalog.json'
    foreach ($resource in @($workerResource, $catalogResource)) {
        if (-not (Test-Path -LiteralPath $resource -PathType Leaf)) {
            throw "Resource not found: $resource"
        }
    }
    $compilerArgs += "/resource:$workerResource,WeatherLauncher.Resources.WeatherWorker.ps1"
    $compilerArgs += "/resource:$catalogResource,WeatherLauncher.Resources.ChinaRegionCatalog.json"
}

$compilerArgs += $sources

& $csc @compilerArgs
if ($LASTEXITCODE -ne 0) {
    throw "Launcher compilation failed with exit code $LASTEXITCODE"
}
if (Test-Path -LiteralPath $generatedVersionSource) {
    Remove-Item -LiteralPath $generatedVersionSource -Force
}

if (-not $SingleExe) {
    Copy-Item -LiteralPath (Join-Path $repoRoot 'src\worker\WeatherWorker.ps1') -Destination (Join-Path $OutputDir 'WeatherWorker.ps1') -Force
    Copy-Item -LiteralPath (Join-Path $repoRoot 'src\worker\ChinaRegionCatalog.json') -Destination (Join-Path $OutputDir 'ChinaRegionCatalog.json') -Force
    Copy-Item -LiteralPath (Join-Path $launcherRoot 'App.xaml') -Destination (Join-Path $OutputDir 'App.xaml') -Force
    Copy-Item -LiteralPath (Join-Path $launcherRoot 'MainWindow.xaml') -Destination (Join-Path $OutputDir 'MainWindow.xaml') -Force
}

Assert-NoForbiddenOutputFiles -Root $OutputDir

Write-Host "Launcher: $exePath"
if ($SingleExe) {
    Write-Host 'Bundled: WeatherWorker.ps1, ChinaRegionCatalog.json'
} else {
    Write-Host "Worker: $(Join-Path $OutputDir 'WeatherWorker.ps1')"
}