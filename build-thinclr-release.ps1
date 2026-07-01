#Requires -Version 5.1
param(
    [string]$Version = '2.0.0'
)

$ErrorActionPreference = 'Stop'

if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw 'Version must use Major.Minor.Patch format, for example 2.0.0.'
}

$repoRoot = if ($PSScriptRoot) { $PSScriptRoot } else { (Get-Location).Path }
$repoRoot = [IO.Path]::GetFullPath($repoRoot)
$releaseBase = [IO.Path]::GetFullPath((Join-Path $repoRoot 'dist\release'))
$releaseRoot = [IO.Path]::GetFullPath((Join-Path $releaseBase ("v$Version")))
$packageName = "PaperWeatherWidget-v$Version-win-x64"
$packageDir = Join-Path $releaseRoot $packageName
$zipPath = Join-Path $releaseRoot ("$packageName.zip")
$shaPath = Join-Path $releaseRoot 'SHA256SUMS.txt'
$buildScript = Join-Path $repoRoot 'src\launcher\build-launcher.ps1'
$licensePath = Join-Path $repoRoot 'LICENSE'

if (-not $releaseRoot.StartsWith($releaseBase, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to write release output outside dist\release: $releaseRoot"
}
if (-not (Test-Path -LiteralPath $buildScript -PathType Leaf)) {
    throw "Launcher build script not found: $buildScript"
}
if (-not (Test-Path -LiteralPath $licensePath -PathType Leaf)) {
    throw "LICENSE not found: $licensePath"
}

if (Test-Path -LiteralPath $releaseRoot) {
    Remove-Item -LiteralPath $releaseRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $packageDir -Force | Out-Null

& powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File $buildScript -OutputDir $packageDir -Version $Version
if ($LASTEXITCODE -ne 0) {
    throw "Launcher build failed with exit code $LASTEXITCODE"
}

Copy-Item -LiteralPath $licensePath -Destination (Join-Path $packageDir 'LICENSE') -Force

$packageReadme = @"
Paper Weather Widget v$Version
纸感天气小组件 v$Version

English
Run WeatherLauncher.exe. Keep WeatherWorker.ps1, ChinaRegionCatalog.json, App.xaml, and MainWindow.xaml in the same folder.
No administrator rights are required. The app does not need an API key, account, telemetry, WebView2, Node.js, or a paid weather service.
Settings and cache files are stored locally under the current user profile or next to the launched build when applicable.
The UI uses a paper-toned visual style with bilingual Chinese / English labels, district-level region switching, forecast slots, cached weather, and a compact side-drawer window.
Weather data comes from Open-Meteo first, with wttr.in used as fallback. This unsigned build may trigger a Windows SmartScreen unknown publisher warning.
This project is independent and is not affiliated with Anthropic, Claude, Open-Meteo, or wttr.in.

中文
运行 WeatherLauncher.exe。请保持 WeatherWorker.ps1、ChinaRegionCatalog.json、App.xaml 和 MainWindow.xaml 与启动器在同一个文件夹。
无需管理员权限。应用不需要 API key、账号、遥测、WebView2、Node.js 或付费天气服务。
设置和缓存只保存在本机当前用户环境，或在适用场景下保存在启动目录旁边。
界面采用纸感视觉风格，支持中文 / English 双语、区县级地区切换、预报时段、缓存天气和紧凑侧边抽屉窗口。
天气数据优先来自 Open-Meteo，失败时使用 wttr.in 作为备用。此版本未签名，Windows SmartScreen 首次启动时可能提示未知发布者。
本项目为独立项目，与 Anthropic、Claude、Open-Meteo、wttr.in 均无从属或官方合作关系。
"@
Set-Content -LiteralPath (Join-Path $packageDir 'README.txt') -Value $packageReadme -Encoding UTF8

$expectedFiles = @(
    'WeatherLauncher.exe',
    'WeatherWorker.ps1',
    'ChinaRegionCatalog.json',
    'App.xaml',
    'MainWindow.xaml',
    'LICENSE',
    'README.txt'
)
foreach ($file in $expectedFiles) {
    $fullPath = Join-Path $packageDir $file
    if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
        throw "Release package is missing $file"
    }
}

if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}
Compress-Archive -Path (Join-Path $packageDir '*') -DestinationPath $zipPath -Force

$hashTargets = @($zipPath) + ($expectedFiles | ForEach-Object { Join-Path $packageDir $_ })
$hashLines = foreach ($artifact in $hashTargets) {
    $hash = Get-FileHash -Algorithm SHA256 -LiteralPath $artifact
    if ([string]::Equals($artifact, $zipPath, [StringComparison]::OrdinalIgnoreCase)) {
        $name = Split-Path -Leaf $artifact
    } else {
        $name = "$packageName/$(Split-Path -Leaf $artifact)"
    }
    '{0}  {1}' -f $hash.Hash.ToLowerInvariant(), $name
}
Set-Content -LiteralPath $shaPath -Value $hashLines -Encoding ASCII

Write-Host "PACKAGE_DIR: $packageDir"
Write-Host "ZIP: $zipPath"
Write-Host "SHA256: $shaPath"