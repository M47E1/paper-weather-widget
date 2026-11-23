#Requires -Version 5.1
param(
    [string]$Version = ''
)

$ErrorActionPreference = 'Stop'

$repoRoot = if ($PSScriptRoot) { $PSScriptRoot } else { (Get-Location).Path }
$repoRoot = [IO.Path]::GetFullPath($repoRoot)

if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = (Get-Content -LiteralPath (Join-Path $repoRoot 'VERSION') -Raw).Trim()
}
if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Invalid SemVer VERSION: $Version"
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
        if (-not (Test-Path -LiteralPath $root -PathType Container)) { continue }
        $match = Get-ChildItem -LiteralPath $root -Filter "$AssemblyName.dll" -Recurse -ErrorAction SilentlyContinue |
            Sort-Object FullName -Descending | Select-Object -First 1
        if ($null -ne $match) { return $match.FullName }
    }
    throw "Required GAC assembly not found: $AssemblyName"
}

# 1. Compress ChinaRegionCatalog.json (1.34 MB) -> compact GZip TSV (~72 KB)
$objDir = Join-Path $repoRoot 'obj'
$distDir = Join-Path $repoRoot 'dist'
New-Item -ItemType Directory -Path $objDir -Force | Out-Null
New-Item -ItemType Directory -Path $distDir -Force | Out-Null

$catalogJson = Join-Path $repoRoot 'data\ChinaRegionCatalog.json'
$catalogGz = Join-Path $objDir 'ChinaRegions.tsv.gz'

if (-not (Test-Path -LiteralPath $catalogJson -PathType Leaf)) {
    throw "Region catalog source not found: $catalogJson"
}

$needsCatalogRebuild = (-not (Test-Path -LiteralPath $catalogGz -PathType Leaf)) -or `
    ((Get-Item -LiteralPath $catalogJson).LastWriteTimeUtc -gt (Get-Item -LiteralPath $catalogGz).LastWriteTimeUtc)

if ($needsCatalogRebuild) {
    Write-Host "Compressing region catalog..."
    $raw = Get-Content -LiteralPath $catalogJson -Raw -Encoding UTF8 | ConvertFrom-Json
    $sb = New-Object System.Text.StringBuilder
    foreach ($p in $raw.provinces) {
        [void]$sb.Append("P`t$($p.key)`t$($p.zh)`t$($p.en)`t$($p.lat)`t$($p.lon)`n")
        foreach ($c in $p.cities) {
            [void]$sb.Append("C`t$($c.key)`t$($c.zh)`t$($c.en)`t$($c.lat)`t$($c.lon)`n")
            foreach ($d in $c.districts) {
                [void]$sb.Append("D`t$($d.key)`t$($d.zh)`t$($d.en)`t$($d.lat)`t$($d.lon)`n")
            }
        }
    }
    $tsvBytes = [System.Text.Encoding]::UTF8.GetBytes($sb.ToString())
    $fs = [System.IO.File]::Create($catalogGz)
    try {
        $gz = New-Object System.IO.Compression.GZipStream($fs, [System.IO.Compression.CompressionLevel]::Optimal)
        try { $gz.Write($tsvBytes, 0, $tsvBytes.Length) } finally { $gz.Dispose() }
    } finally {
        $fs.Dispose()
    }
}

# 2. Generate AssemblyInfo
$asmInfoPath = Join-Path $objDir 'AssemblyInfo.g.cs'
$fileVersion = "$Version.0"
@"
using System.Reflection;
[assembly: AssemblyTitle("Paper Weather Widget")]
[assembly: AssemblyDescription("Lightweight native Windows WPF desktop weather widget.")]
[assembly: AssemblyProduct("Paper Weather Widget")]
[assembly: AssemblyVersion("$fileVersion")]
[assembly: AssemblyFileVersion("$fileVersion")]
[assembly: AssemblyInformationalVersion("$Version")]
"@ | Set-Content -LiteralPath $asmInfoPath -Encoding UTF8

# 3. Compile native single-EXE with optimizations
$frameworkDir = Split-Path -Parent $csc
$references = @(
    (Join-Path $frameworkDir 'System.dll'),
    (Join-Path $frameworkDir 'System.Core.dll'),
    (Join-Path $frameworkDir 'System.Drawing.dll'),
    (Join-Path $frameworkDir 'System.Windows.Forms.dll'),
    (Resolve-GacAssembly 'PresentationFramework'),
    (Resolve-GacAssembly 'PresentationCore'),
    (Resolve-GacAssembly 'WindowsBase'),
    (Resolve-GacAssembly 'System.Xaml'),
    (Resolve-GacAssembly 'System.Web.Extensions')
)

$sources = @(Get-ChildItem -LiteralPath (Join-Path $repoRoot 'src') -Filter '*.cs' -Recurse | Select-Object -ExpandProperty FullName)
$sources += $asmInfoPath

$exeName = "PaperWeatherWidget-v$Version-win-x64.exe"
$exePath = Join-Path $distDir $exeName

$compilerArgs = @(
    '/noconfig',
    '/nologo',
    '/optimize+',
    '/utf8output',
    '/target:winexe',
    '/platform:x64',
    "/out:$exePath",
    "/resource:$catalogGz,PaperWeatherWidget.Resources.ChinaRegions.tsv.gz"
)

$iconPath = Join-Path $repoRoot 'assets\liquidmetal-sun.ico'
if (Test-Path -LiteralPath $iconPath -PathType Leaf) {
    $compilerArgs += "/win32icon:$iconPath"
}

foreach ($ref in $references) { $compilerArgs += "/reference:$ref" }
$compilerArgs += $sources

& $csc @compilerArgs
if ($LASTEXITCODE -ne 0) {
    throw "Compilation failed with exit code $LASTEXITCODE"
}

$hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $exePath).Hash.ToLowerInvariant()
Set-Content -LiteralPath (Join-Path $distDir 'SHA256SUMS.txt') -Value "$hash  $exeName" -Encoding ASCII

$sizeKb = [math]::Round((Get-Item -LiteralPath $exePath).Length / 1KB, 1)
Write-Host "Built: $exePath ($sizeKb KB)"
Write-Host "SHA256: $hash"
