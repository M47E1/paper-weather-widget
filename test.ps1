#Requires -Version 5.1
$ErrorActionPreference = 'Stop'

$repoRoot = if ($PSScriptRoot) { $PSScriptRoot } else { (Get-Location).Path }
$version = (Get-Content -LiteralPath (Join-Path $repoRoot 'VERSION') -Raw).Trim()
$exePath = Join-Path $repoRoot "dist\PaperWeatherWidget-v$version-win-x64.exe"

Write-Host "=== 1. Build Verification ==="
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $repoRoot 'build.ps1')
if (-not (Test-Path -LiteralPath $exePath)) { throw "EXE not found: $exePath" }

Write-Host "`n=== 2. Unit Tests (Catalog, Search, Localization, Parser) ==="
Add-Type -AssemblyName PresentationFramework
Add-Type -AssemblyName PresentationCore
Add-Type -AssemblyName WindowsBase
$asm = [System.Reflection.Assembly]::LoadFrom($exePath)

# Catalog count + search + unload
$catalogType = $asm.GetType('PaperWeatherWidget.Services.RegionCatalogService')
$cat = [Activator]::CreateInstance($catalogType)
$provinces = $cat.GetProvinces()
$pCount = $provinces.Count
$cCount = 0; $dCount = 0
foreach ($p in $provinces) {
    $cCount += $p.Cities.Count
    foreach ($c in $p.Cities) { $dCount += $c.Districts.Count }
}
if ($pCount -ne 34 -or $cCount -ne 370 -or $dCount -ne 2875) {
    throw "Catalog counts mismatch: P=$pCount C=$cCount D=$dCount"
}
Write-Host "[PASS] Region catalog loaded: $pCount provinces, $cCount cities, $dCount districts"

$zhHits = $cat.Search('顺德', 5)
if ($zhHits.Count -eq 0 -or $zhHits[0].Key -ne '440606') { throw "Chinese search for 顺德 failed" }
$enHits = $cat.Search('longhua', 5)
if ($enHits.Count -eq 0) { throw "English/pinyin search for longhua failed" }
Write-Host "[PASS] Instant region search (ZH + EN/Pinyin): 顺德 -> $($zhHits[0].Zh), longhua -> $($enHits[0].Zh)"

$cat.UnloadCatalog()
if ($cat.IsLoaded) { throw "Catalog did not unload cleanly" }
Write-Host "[PASS] Catalog unload verified"

# English mode zero-CJK audit
$locType = $asm.GetType('PaperWeatherWidget.Services.LocalizationService')
$uiKeys = @('AppTitle','Settings','Back','Location','SearchPlaceholder','NoSearchResults','RefreshNow','PinTopmost','UnpinTopmost','DrawerCollapse','DrawerExpand','Exit','Province','City','District','Refresh','Theme','ThemeAuto','ThemeLight','ThemeDark','Language','Startup','TopmostLabel','On','Off','MoreDetails','LessDetails','MoreSlots','Now','Today','Tomorrow','DayAfterTomorrow','Feels','RainProb','Humidity','Wind','TodayRain','RainNow','Gust','Cloud','Pressure','FiveDayForecast','StateLive','StateRefreshing','StateCached','StateStale','StateFailed','UpdatedPrefix','RefreshFailedHint','OfflineHint','NearTermNone','NearTermPossible','NearTermHeavy','NearTermUnavailable','CreditPrefix')
foreach ($k in $uiKeys) {
    $txt = $locType::Ui('en', $k)
    if ($locType::ContainsCjk($txt)) { throw "CJK leaked in EN UI key '$k': $txt" }
}
foreach ($code in @(0,1,2,3,45,51,61,63,65,71,80,95,99)) {
    $txt = $locType::WeatherConditionText('en', $code, 1, '')
    if ($locType::ContainsCjk($txt)) { throw "CJK leaked in EN weather code ${code}: $txt" }
}
Write-Host "[PASS] English localization audit: 0 CJK leaks"

# WeatherService Open-Meteo parser + slot projection + live network fetch
$wsType = $asm.GetType('PaperWeatherWidget.Services.WeatherService')
$ws = [Activator]::CreateInstance($wsType)
$shunde = $cat.FindDistrict('440000', '440600', '440606')
$fixtureRes = $ws.FetchWeatherSync($shunde, 'Day0', $true, $true)
if (-not $fixtureRes.Success -or $null -eq $fixtureRes.Snapshot) { throw "Fixture fetch failed" }
$slotRes = $ws.TryProjectFromMemoryCache($shunde, 'Day1')
if ($null -eq $slotRes -or $slotRes.SlotKey -ne 'Day1') { throw "Instant Day1 slot projection failed" }
$hourRes = $ws.TryProjectFromMemoryCache($shunde, 'Hour+3h')
if ($null -eq $hourRes -or $hourRes.SlotKey -ne 'Hour+3h') { throw "Instant Hour+3h slot projection failed" }
Write-Host "[PASS] Weather slot projection (Day0, Day1, Hour+3h) verified from memory cache"

$liveRes = $ws.FetchWeatherSync($shunde, 'Day0', $true, $false)
if ($liveRes.Success) {
    Write-Host "[PASS] Live weather fetch succeeded: $($shunde.Zh) $($liveRes.Snapshot.Temperature)°C via $($liveRes.Snapshot.Source)"
} else {
    Write-Host "[WARN] Live weather fetch failed (offline environment?): $($liveRes.ErrorMessage)"
}

Write-Host "`n=== 3. Live Process & Memory Footprint Benchmark ==="
$proc = Start-Process -FilePath $exePath -ArgumentList '--no-topmost','--no-tray','--hold-open-ms','7000' -PassThru
Start-Sleep -Seconds 5
$children = @(Get-CimInstance Win32_Process | Where-Object { $_.ParentProcessId -eq $proc.Id })
$p = Get-Process -Id $proc.Id -ErrorAction SilentlyContinue
if ($null -eq $p) { throw "v4 process exited unexpectedly before 5s" }
$wsMB = [math]::Round($p.WorkingSet64 / 1MB, 2)
$privMB = [math]::Round($p.PrivateMemorySize64 / 1MB, 2)
Write-Host "v4.0.0 Process Count: $(1 + $children.Count) (Child processes: $($children.Count))"
Write-Host "v4.0.0 Memory (after 5s trim): WorkingSet=${wsMB}MB, Private=${privMB}MB"
if ($children.Count -gt 0) { throw "Unexpected child process spawned!" }
$proc.WaitForExit(5000) | Out-Null
if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue }
Write-Host "[PASS] Single-process zero-PowerShell runtime verified!"
