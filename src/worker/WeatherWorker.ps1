#Requires -Version 5.1
param(
    [string]$AppRoot = '',
    [int]$PollSeconds = 10,
    [switch]$Once,
    [string]$CommandFile = '',
    [string]$SessionId = '',
    [switch]$IpcMode,
    [switch]$IpcSmoke,
    [switch]$FixtureWeatherSuccess,
    [switch]$AllowFixtureSnapshotWrite,
    [switch]$StartupTrace,
    [string]$StartupTracePath = ''
)

$script:WorkerTraceStartedAtUtc = [DateTime]::UtcNow
$script:StartupTraceEnabled = [bool]$StartupTrace
$script:StartupTracePath = $StartupTracePath
if ($script:StartupTraceEnabled -and [string]::IsNullOrWhiteSpace($script:StartupTracePath)) {
    $script:StartupTracePath = Join-Path (Join-Path (Get-Location).Path 'reports') 'startup-trace.log'
}

function Write-WorkerStartupTrace {
    param([string]$Milestone)

    if (-not $script:StartupTraceEnabled -or [string]::IsNullOrWhiteSpace($Milestone)) { return }
    try {
        $directory = Split-Path -Parent $script:StartupTracePath
        if (-not [string]::IsNullOrWhiteSpace($directory) -and -not (Test-Path -LiteralPath $directory -PathType Container)) {
            New-Item -ItemType Directory -Path $directory -Force | Out-Null
        }
        $elapsed = [Math]::Max(0, [int](([DateTime]::UtcNow - $script:WorkerTraceStartedAtUtc).TotalMilliseconds))
        $line = '[Trace][Worker] elapsed_ms={0} wall={1:o} milestone={2}' -f $elapsed, [DateTime]::UtcNow, $Milestone
        [IO.File]::AppendAllText($script:StartupTracePath, $line + [Environment]::NewLine)
    } catch {}
}

Write-WorkerStartupTrace 'Worker process entry'
Write-WorkerStartupTrace 'Args parsed'
$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

try {
    $utf8 = New-Object System.Text.UTF8Encoding $false
    [Console]::OutputEncoding = $utf8
    $OutputEncoding = $utf8
} catch {}

function T {
    param([string]$Base64)

    try {
        return [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($Base64))
    } catch {
        return $Base64
    }
}

function Write-JsonLine {
    param([object]$Payload)

    $json = $Payload | ConvertTo-Json -Depth 8 -Compress
    [Console]::Out.WriteLine($json)
    [Console]::Out.Flush()
}

function New-ProtocolEnvelope {
    param(
        [string]$Type,
        [object]$Payload,
        [string]$Id = '',
        [Nullable[int]]$RequestId = $null
    )

    $event = [ordered]@{
        protocol = 1
        type = $Type
        id = $(if ([string]::IsNullOrWhiteSpace($Id)) { [Guid]::NewGuid().ToString('N') } else { $Id })
        timestamp = [DateTimeOffset]::Now.ToUnixTimeSeconds()
        payload = $Payload
    }
    if ($null -ne $RequestId) {
        $event.requestId = [int]$RequestId
    }
    return $event
}

function Write-ProtocolEvent {
    param(
        [string]$Type,
        [object]$Payload,
        [string]$Id = '',
        [Nullable[int]]$RequestId = $null
    )

    Write-JsonLine (New-ProtocolEnvelope -Type $Type -Payload $Payload -Id $Id -RequestId $RequestId)
}

function Remove-StaleProcessedCommands {
    $cutoff = (Get-Date).AddMinutes(-10)
    foreach ($key in @($script:ProcessedCommandIds.Keys)) {
        if ($script:ProcessedCommandIds[$key] -lt $cutoff) {
            $script:ProcessedCommandIds.Remove($key)
        }
    }
}

function Write-CommandAck {
    param(
        [string]$Id,
        [string]$CommandType,
        [Nullable[int]]$RequestId = $null
    )

    if (-not $script:IpcModeActive) { return }
    $payload = [ordered]@{
        accepted = $true
        commandType = $CommandType
        settingsVersion = $script:SettingsVersion
        forecastSelectionVersion = $script:ForecastSelectionVersion
        locationKey = Get-ProtocolLocationKey
    }
    if ($null -ne $RequestId) {
        $payload.requestId = [int]$RequestId
    }
    Write-ProtocolEvent -Type 'ack' -Id $Id -RequestId $RequestId -Payload $payload
}

function Write-CommandError {
    param(
        [string]$Id,
        [string]$CommandType,
        [string]$Code,
        [string]$Message
    )

    Write-ProtocolEvent -Type 'error' -Id $Id -Payload ([ordered]@{
        accepted = $false
        commandType = $CommandType
        code = $Code
        message = $Message
        settingsVersion = $script:SettingsVersion
        forecastSelectionVersion = $script:ForecastSelectionVersion
    })
}

function Write-WorkerError {
    param(
        [string]$Message,
        [string]$Id = ''
    )

    if ($script:IpcModeActive) {
        Write-ProtocolEvent -Type 'error' -Id $Id -Payload ([ordered]@{
            code = 'weather_worker_error'
            message = $Message
        })
        return
    }

    Write-JsonLine ([ordered]@{
        type = 'weather'
        status = 'error'
        error = $Message
        timestamp = [DateTimeOffset]::Now.ToUnixTimeSeconds()
    })
}

function Get-PersistentWeatherSnapshotPath {
    $localAppData = $env:LOCALAPPDATA
    if ([string]::IsNullOrWhiteSpace($localAppData)) {
        try { $localAppData = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData) } catch {}
    }
    if ([string]::IsNullOrWhiteSpace($localAppData)) {
        $localAppData = [IO.Path]::GetTempPath()
    }
    return (Join-Path (Join-Path $localAppData 'PaperWeatherWidget') 'weather-snapshot.json')
}
function Resolve-AppRoot {
    param([string]$RequestedRoot)

    if (-not [string]::IsNullOrWhiteSpace($RequestedRoot)) {
        try {
            $full = [IO.Path]::GetFullPath($RequestedRoot)
            if (Test-Path -LiteralPath $full -PathType Container) {
                return $full
            }
        } catch {}
    }

    $candidates = @(
        (Get-Location).Path,
        (Split-Path -Parent $PSScriptRoot),
        (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))
    )

    foreach ($candidate in $candidates) {
        if ([string]::IsNullOrWhiteSpace($candidate)) { continue }
        try {
            $full = [IO.Path]::GetFullPath($candidate)
            if (Test-Path -LiteralPath (Join-Path $full 'LonghuaWeatherWidget.ps1') -PathType Leaf) {
                return $full
            }
        } catch {}
    }

    return (Get-Location).Path
}

$script:AppRoot = Resolve-AppRoot -RequestedRoot $AppRoot
$script:SettingsPath = Join-Path $script:AppRoot 'LonghuaWeatherWidget.settings.json'
$script:LegacyScriptPath = Join-Path $script:AppRoot 'LonghuaWeatherWidget.ps1'
Write-WorkerStartupTrace 'Settings path resolved'
$script:DefaultProvinceKey = '440000'
$script:DefaultCityKey = '440300'
$script:DefaultDistrictKey = '440309'
$script:Language = 'zh'
$script:SelectedProvinceKey = $script:DefaultProvinceKey
$script:SelectedCityKey = $script:DefaultCityKey
$script:SelectedDistrictKey = $script:DefaultDistrictKey
$script:SelectedForecastSlotKey = 'Day0'
$script:ForecastDayCount = 14
$script:ForecastHourCount = 336
$script:WeatherRequestTimeoutMs = 6000
$script:UiSmokeMode = $false
$script:RefreshOptions = @(60, 3600, 86400)
$script:RefreshSeconds = if ($script:RefreshOptions -contains [int]$PollSeconds) { [int]$PollSeconds } else { 60 }
$script:PollSeconds = [Math]::Max(5, [Math]::Min(10, [int]$PollSeconds))
$script:IpcModeActive = [bool]($IpcMode -or -not [string]::IsNullOrWhiteSpace($CommandFile) -or $IpcSmoke)
$script:FixtureWeatherSuccess = [bool]$FixtureWeatherSuccess
$script:AllowFixtureSnapshotWrite = [bool]$AllowFixtureSnapshotWrite
$script:SnapshotPath = Get-PersistentWeatherSnapshotPath
Write-WorkerStartupTrace ('Fixture flag resolved: ' + [string]$script:FixtureWeatherSuccess)
$script:CommandFile = $CommandFile
$script:SessionId = $SessionId
$script:CommandFilePosition = 0L
$script:ProcessedCommandIds = @{}
$script:WeatherSnapshotCache = @{}
$script:RawWeatherCache = @{}
$script:WeatherRequestSequence = 0
$script:ActiveWeatherRequestId = 0
$script:ActiveWeatherRequestLocationKey = ''
$script:SettingsVersion = 0
$script:ForecastSelectionVersion = 0
$script:WeatherRefreshInProgress = $false
$script:PendingWeatherRefresh = $false
$script:LatestWeatherSnapshot = $null
$script:LatestRawWeather = $null
$script:LatestWeatherLocationKey = ''
$script:LocationCatalogImported = $false
$script:UsingMinimalCatalog = $false
$script:NextAutoRefreshAt = (Get-Date).AddSeconds($script:RefreshSeconds)

$script:CurrentFields = @(
    'temperature_2m',
    'relative_humidity_2m',
    'apparent_temperature',
    'is_day',
    'precipitation',
    'rain',
    'showers',
    'weather_code',
    'cloud_cover',
    'pressure_msl',
    'surface_pressure',
    'wind_speed_10m',
    'wind_direction_10m',
    'wind_gusts_10m'
) -join ','

$script:HourlyFields = @(
    'temperature_2m',
    'relative_humidity_2m',
    'dew_point_2m',
    'apparent_temperature',
    'precipitation_probability',
    'precipitation',
    'rain',
    'showers',
    'weather_code',
    'cloud_cover',
    'pressure_msl',
    'surface_pressure',
    'visibility',
    'wind_speed_10m',
    'wind_direction_10m',
    'wind_gusts_10m',
    'uv_index',
    'is_day'
) -join ','

$script:DailyFields = @(
    'weather_code',
    'temperature_2m_max',
    'temperature_2m_min',
    'apparent_temperature_max',
    'apparent_temperature_min',
    'sunrise',
    'sunset',
    'daylight_duration',
    'sunshine_duration',
    'uv_index_max',
    'precipitation_sum',
    'rain_sum',
    'precipitation_hours',
    'precipitation_probability_max',
    'wind_speed_10m_max',
    'wind_gusts_10m_max',
    'wind_direction_10m_dominant'
) -join ','

if (-not $FixtureWeatherSuccess -and -not ('LonghuaWeatherWorkerTimeoutWebClient' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Net;

public sealed class LonghuaWeatherWorkerTimeoutWebClient : WebClient
{
    public int TimeoutMilliseconds { get; set; }

    public LonghuaWeatherWorkerTimeoutWebClient()
    {
        TimeoutMilliseconds = 6000;
    }

    protected override WebRequest GetWebRequest(Uri address)
    {
        WebRequest request = base.GetWebRequest(address);
        if (request != null)
        {
            request.Timeout = TimeoutMilliseconds;
            HttpWebRequest httpRequest = request as HttpWebRequest;
            if (httpRequest != null)
            {
                httpRequest.ReadWriteTimeout = TimeoutMilliseconds;
                httpRequest.KeepAlive = false;
            }
        }
        return request;
    }
}
'@
}

function Start-WeatherRequestContext {
    param([string]$LocationKey = (Get-SelectedLocationKey))

    $script:WeatherRequestSequence++
    $script:ActiveWeatherRequestId = $script:WeatherRequestSequence
    $script:ActiveWeatherRequestLocationKey = $LocationKey

    [pscustomobject]@{
        RequestId = $script:WeatherRequestSequence
        LocationKey = $LocationKey
        SettingsVersion = $script:SettingsVersion
        ForecastSelectionVersion = $script:ForecastSelectionVersion
        StartedAt = Get-Date
    }
}

function Test-WeatherRequestIsCurrent {
    param([object]$Request)

    if ($null -eq $Request) { return $false }
    if ([int]$Request.RequestId -ne [int]$script:ActiveWeatherRequestId) { return $false }
    if ([string]$Request.LocationKey -ne [string]$script:ActiveWeatherRequestLocationKey) { return $false }
    if ([int]$Request.SettingsVersion -ne [int]$script:SettingsVersion) { return $false }
    if ([int]$Request.ForecastSelectionVersion -ne [int]$script:ForecastSelectionVersion) { return $false }
    return ((Get-ProtocolLocationKey) -eq [string]$Request.LocationKey)
}

function ConvertTo-LegacyWeatherEvent {
    param([object]$Snapshot)

    $legacy = [ordered]@{ type = 'weather' }
    foreach ($entry in $Snapshot.GetEnumerator()) { $legacy[$entry.Key] = $entry.Value }
    return $legacy
}

function Invoke-WeatherRefresh {
    param(
        [string]$Reason = 'manual',
        [string]$CommandId = '',
        [string]$CommandType = ''
    )

    $effectiveCommandType = $(if ([string]::IsNullOrWhiteSpace($CommandType)) { $Reason } else { $CommandType })
    if ($script:WeatherRefreshInProgress) {
        $script:PendingWeatherRefresh = $true
        if ($script:IpcModeActive) {
            if (-not [string]::IsNullOrWhiteSpace($CommandId)) {
                Write-CommandAck -Id $CommandId -CommandType $effectiveCommandType -RequestId $script:ActiveWeatherRequestId
            }
            Write-ProtocolEvent -Type 'status' -Id $CommandId -Payload ([ordered]@{
                phase = 'refreshing'
                message = 'Refresh already in progress'
                reason = $Reason
                queued = $true
            })
        }
        return
    }

    do {
        $script:PendingWeatherRefresh = $false
        $request = Start-WeatherRequestContext
        $script:WeatherRefreshInProgress = $true
        if ($script:IpcModeActive) {
            if (-not [string]::IsNullOrWhiteSpace($CommandId)) {
                Write-CommandAck -Id $CommandId -CommandType $effectiveCommandType -RequestId $request.RequestId
            }
            Write-ProtocolEvent -Type 'status' -Id $CommandId -RequestId $request.RequestId -Payload ([ordered]@{
                phase = 'refreshing'
                message = 'Refreshing weather'
                reason = $Reason
                locationKey = $request.LocationKey
                settingsVersion = $request.SettingsVersion
                forecastSelectionVersion = $request.ForecastSelectionVersion
            })
        }

        try {
            $snapshot = Get-WeatherSnapshot
            if (Test-WeatherRequestIsCurrent -Request $request) {
                if ($script:IpcModeActive) {
                    Write-ProtocolEvent -Type 'weather' -Id $CommandId -RequestId $request.RequestId -Payload $snapshot
                    if ($script:FixtureWeatherSuccess) { Write-WorkerStartupTrace 'First stdout weather event emitted' }
                    Write-ProtocolEvent -Type 'status' -Id $CommandId -RequestId $request.RequestId -Payload ([ordered]@{
                        phase = $(if ($snapshot.fromCache) { 'cached' } else { 'idle' })
                        message = $(if ($snapshot.fromCache) { 'Cached weather' } else { 'Weather ready' })
                        locationKey = $request.LocationKey
                    })
                } else {
                    Write-JsonLine (ConvertTo-LegacyWeatherEvent -Snapshot $snapshot)
                }
            } elseif ($script:IpcModeActive) {
                Write-ProtocolEvent -Type 'status' -RequestId $request.RequestId -Payload ([ordered]@{
                    phase = 'stale'
                    message = 'Skipped stale weather result'
                    locationKey = $request.LocationKey
                    settingsVersion = $script:SettingsVersion
                    forecastSelectionVersion = $script:ForecastSelectionVersion
                })
            }
        } catch {
            if ($script:IpcModeActive) {
                Write-ProtocolEvent -Type 'status' -Id $CommandId -RequestId $request.RequestId -Payload ([ordered]@{
                    phase = 'offline'
                    message = $_.Exception.Message
                    locationKey = $request.LocationKey
                })
                if (-not [string]::IsNullOrWhiteSpace($CommandId)) {
                    Write-CommandError -Id $CommandId -CommandType $effectiveCommandType -Code 'weather_worker_error' -Message $_.Exception.Message
                } else {
                    Write-WorkerError -Id $CommandId -Message $_.Exception.Message
                }
            } else {
                Write-WorkerError -Message $_.Exception.Message
            }
        } finally {
            $script:WeatherRefreshInProgress = $false
        }
    } while ($script:PendingWeatherRefresh)
}
function Get-SettingsPayload {
    [ordered]@{
        language = $script:Language
        refreshSeconds = $script:RefreshSeconds
        provinceKey = $script:SelectedProvinceKey
        cityKey = $script:SelectedCityKey
        districtKey = $script:SelectedDistrictKey
        forecastSlotKey = $script:SelectedForecastSlotKey
        locationKey = Get-ProtocolLocationKey
        locationLabel = Get-ProtocolLocationTitle
        refreshOptions = @(
            foreach ($seconds in $script:RefreshOptions) {
                [ordered]@{
                    seconds = [int]$seconds
                    key = [string]$seconds
                    label = $(switch ([int]$seconds) { 60 { '1 min' } 3600 { '1 hour' } 86400 { '1 day' } default { [string]$seconds } })
                }
            }
        )
        forecastSlots = Get-ForecastSlotsPayload
    }
}

function Write-SettingsEvent {
    param([string]$Id = '')
    Write-ProtocolEvent -Type 'settings' -Id $Id -Payload (Get-SettingsPayload)
}

function Get-RegionCatalogPayload {
    Ensure-FullLocationCatalog
    [ordered]@{
        selected = [ordered]@{
            provinceKey = $script:SelectedProvinceKey
            cityKey = $script:SelectedCityKey
            districtKey = $script:SelectedDistrictKey
        }
        provinces = @(
            foreach ($province in @($script:Provinces)) {
                [ordered]@{
                    key = [string]$province.Key
                    label = Get-DisplayName $province
                    cities = @(
                        foreach ($city in @($province.Cities)) {
                            [ordered]@{
                                key = [string]$city.Key
                                label = Get-DisplayName $city
                                districts = @(
                                    foreach ($district in @($city.Districts)) {
                                        [ordered]@{ key = [string]$district.Key; label = Get-DisplayName $district }
                                    }
                                )
                            }
                        }
                    )
                }
            }
        )
    }
}

function Write-CatalogEvent {
    param([string]$Id = '')
    Write-ProtocolEvent -Type 'catalog' -Id $Id -Payload (Get-RegionCatalogPayload)
}

function Set-SelectedLocation {
    param([string]$ProvinceKey, [string]$CityKey, [string]$DistrictKey)

    Ensure-FullLocationCatalog
    if (-not (Test-LocationKeys -ProvinceKey $ProvinceKey -CityKey $CityKey -DistrictKey $DistrictKey)) {
        throw ('Invalid location keys: {0}|{1}|{2}' -f $ProvinceKey, $CityKey, $DistrictKey)
    }
    $script:SelectedProvinceKey = $ProvinceKey
    $script:SelectedCityKey = $CityKey
    $script:SelectedDistrictKey = $DistrictKey
    $script:SettingsVersion++
    Save-Settings
}

function Set-RefreshInterval {
    param([int]$Seconds)
    if (-not ($script:RefreshOptions -contains [int]$Seconds)) { throw ('Invalid refresh interval: {0}' -f $Seconds) }
    $script:RefreshSeconds = [int]$Seconds
    $script:SettingsVersion++
    $script:NextAutoRefreshAt = (Get-Date).AddSeconds($script:RefreshSeconds)
    Save-Settings
}

function Set-WorkerLanguage {
    param([string]$Language)
    if (-not (@('zh', 'en') -contains $Language)) { throw ('Invalid language: {0}' -f $Language) }
    $script:Language = $Language
    $script:SettingsVersion++
    Save-Settings
}

function Set-WorkerForecastSlot {
    param([string]$SlotKey)
    $definition = Get-ForecastSlotDefinition -SlotKey $SlotKey
    $script:SelectedForecastSlotKey = [string]$definition.Key
    $script:SettingsVersion++
    $script:ForecastSelectionVersion++
    Save-Settings
}
function Ensure-DefaultForecastSlotDefinitions {
    if ($null -ne $script:ForecastSlotDefinitions -and @($script:ForecastSlotDefinitions | Where-Object { $_.Key -eq 'Day0' -or $_.Key -eq 'Day13' }).Count -eq 2) { return }
    $definitions = New-Object System.Collections.Generic.List[object]
    for ($day = 0; $day -lt 14; $day++) {
        $definitions.Add([pscustomobject]@{ Key = ('Day{0}' -f $day); OffsetDays = $day; Kind = 'Day' }) | Out-Null
    }
    foreach ($hour in @(1, 3, 6, 12, 24)) {
        $definitions.Add([pscustomobject]@{ Key = ('Hour+{0}h' -f $hour); OffsetHours = $hour; Kind = 'Hour' }) | Out-Null
    }
    $script:ForecastSlotDefinitions = $definitions.ToArray()
}

function Resolve-StaticLocationCatalogPath {
    $candidates = @(
        (Join-Path $script:AppRoot 'ChinaRegionCatalog.json'),
        (Join-Path $PSScriptRoot 'ChinaRegionCatalog.json'),
        (Join-Path (Join-Path $script:AppRoot 'src\worker') 'ChinaRegionCatalog.json'),
        (Join-Path (Join-Path (Split-Path -Parent $PSScriptRoot) 'worker') 'ChinaRegionCatalog.json')
    )
    foreach ($candidate in $candidates) {
        if ([string]::IsNullOrWhiteSpace($candidate)) { continue }
        try {
            $full = [IO.Path]::GetFullPath($candidate)
            if (Test-Path -LiteralPath $full -PathType Leaf) { return $full }
        } catch {}
    }
    return ''
}

function ConvertTo-StaticCatalogNode {
    param(
        [object]$Item,
        [string]$PrecisionFallback = 'CatalogCenter'
    )

    [pscustomobject]@{
        Key = [string]$Item.key
        En = [string]$Item.en
        Zh = [string]$Item.zh
        Lat = ConvertTo-NullableDouble -Value $Item.lat
        Lon = ConvertTo-NullableDouble -Value $Item.lon
        CoordinateSource = $(if ($Item.coordinateSource) { [string]$Item.coordinateSource } else { 'DataV areas_v3 center' })
        CoordinatePrecision = $(if ($Item.coordinatePrecision) { [string]$Item.coordinatePrecision } else { $PrecisionFallback })
        CoordinateValidatedAt = $(if ($Item.coordinateValidatedAt) { [string]$Item.coordinateValidatedAt } else { '2026-07-01' })
        IsApproximateCoordinate = [bool]$Item.isApproximateCoordinate
    }
}

function Import-StaticLocationCatalog {
    $catalogPath = Resolve-StaticLocationCatalogPath
    if ([string]::IsNullOrWhiteSpace($catalogPath)) { return $false }
    try {
        $catalog = Get-Content -LiteralPath $catalogPath -Raw -Encoding UTF8 | ConvertFrom-Json
        $provinces = @()
        foreach ($provinceItem in @($catalog.provinces)) {
            $province = ConvertTo-StaticCatalogNode -Item $provinceItem -PrecisionFallback 'ProvinceCenter'
            $cities = @()
            foreach ($cityItem in @($provinceItem.cities)) {
                $city = ConvertTo-StaticCatalogNode -Item $cityItem -PrecisionFallback 'CityCenter'
                $districts = @()
                foreach ($districtItem in @($cityItem.districts)) {
                    $districts += ConvertTo-StaticCatalogNode -Item $districtItem -PrecisionFallback 'DistrictCenter'
                }
                if ($districts.Count -eq 0) {
                    $districts += ConvertTo-StaticCatalogNode -Item $cityItem -PrecisionFallback 'CityCenterFallback'
                }
                $city | Add-Member -NotePropertyName Districts -NotePropertyValue $districts
                $cities += $city
            }
            if ($cities.Count -eq 0) {
                $city = ConvertTo-StaticCatalogNode -Item $provinceItem -PrecisionFallback 'ProvinceCenterFallback'
                $city | Add-Member -NotePropertyName Districts -NotePropertyValue @($city)
                $cities += $city
            }
            $province | Add-Member -NotePropertyName Cities -NotePropertyValue $cities
            $provinces += $province
        }
        if ($provinces.Count -gt 0) {
            $script:Provinces = $provinces
            $script:LocationCatalogImported = $true
            $script:UsingMinimalCatalog = $false
            return $true
        }
    } catch {
        [Console]::Error.WriteLine('[Worker] static catalog import failed: ' + $_.Exception.Message)
    }
    return $false
}
function Import-LocationCatalog {
    param([switch]$SkipLegacyExtraction)

    if ($script:LocationCatalogImported -and -not $SkipLegacyExtraction) { Ensure-DefaultForecastSlotDefinitions; return }
    if (-not $SkipLegacyExtraction -and (Import-StaticLocationCatalog)) { Ensure-DefaultForecastSlotDefinitions; return }
    if (-not $SkipLegacyExtraction -and (Test-Path -LiteralPath $script:LegacyScriptPath -PathType Leaf)) {
        try {
            $text = Get-Content -LiteralPath $script:LegacyScriptPath -Raw
            $slotStart = $text.IndexOf('$script:ForecastSlotDefinitions = @(', [StringComparison]::Ordinal)
            $slotEnd = $text.IndexOf('function New-District', [StringComparison]::Ordinal)
            if ($slotStart -ge 0 -and $slotEnd -gt $slotStart) {
                Invoke-Expression $text.Substring($slotStart, $slotEnd - $slotStart)
            }
            $start = $text.IndexOf('function New-District', [StringComparison]::Ordinal)
            $end = $text.IndexOf('$script:Text = @{', [StringComparison]::Ordinal)
            if ($start -ge 0 -and $end -gt $start) {
                $catalogScript = $text.Substring($start, $end - $start)
                Invoke-Expression $catalogScript
                if ($null -ne $script:Provinces -and @($script:Provinces).Count -gt 0) {
                    $script:LocationCatalogImported = $true
                    $script:UsingMinimalCatalog = $false
                    return
                }
            }
        } catch {
            [Console]::Error.WriteLine('[Worker] catalog import failed: ' + $_.Exception.Message)
        }
    }

    Ensure-DefaultForecastSlotDefinitions


    $script:UsingMinimalCatalog = $true
    $script:Provinces = @(
        [pscustomobject]@{
            Key = '440000'
            En = 'Guang Dong Sheng'
            Zh = '5bm/5Lic55yB'
            Cities = @(
                [pscustomobject]@{
                    Key = '440300'
                    En = 'Shen Zhen Shi'
                    Zh = '5rex5Zyz5biC'
                    Lat = 22.547
                    Lon = 114.085947
                    Districts = @(
                        [pscustomobject]@{
                            Key = '440309'
                            En = 'Long Hua Qu'
                            Zh = '6b6Z5Y2O5Yy6'
                            Lat = 22.691963
                            Lon = 114.044346
                            CoordinateSource = 'DataV areas_v3 center'
                            CoordinatePrecision = 'DistrictCenter'
                            CoordinateValidatedAt = '2026-07-01'
                            IsApproximateCoordinate = $false
                        }
                    )
                }
            )
        }
    )
}

function Ensure-FullLocationCatalog {
    if ($script:LocationCatalogImported) { return }
    Write-WorkerStartupTrace 'Before legacy catalog extraction/import'
    Import-LocationCatalog
    Write-WorkerStartupTrace 'After legacy catalog extraction/import'
}

function Get-ProtocolLocationKey {
    if ($script:FixtureWeatherSuccess -and $script:UsingMinimalCatalog) {
        return New-LocationKey -ProvinceKey $script:SelectedProvinceKey -CityKey $script:SelectedCityKey -DistrictKey $script:SelectedDistrictKey
    }
    return Get-SelectedLocationKey
}

function Get-ProtocolLocationTitle {
    if ($script:FixtureWeatherSuccess -and $script:UsingMinimalCatalog) {
        if ($script:SelectedProvinceKey -eq $script:DefaultProvinceKey -and $script:SelectedCityKey -eq $script:DefaultCityKey -and $script:SelectedDistrictKey -eq $script:DefaultDistrictKey) {
            return ('{0} - {1} - {2}' -f (T '5bm/5Lic55yB'), (T '5rex5Zyz5biC'), (T '6b6Z5Y2O5Yy6'))
        }
        return '{0} - {1} - {2}' -f $script:SelectedProvinceKey, $script:SelectedCityKey, $script:SelectedDistrictKey
    }
    return Get-LocationTitle
}

function Get-ProtocolCityLabel {
    if ($script:FixtureWeatherSuccess -and $script:UsingMinimalCatalog -and $script:SelectedCityKey -ne $script:DefaultCityKey) {
        return $script:SelectedCityKey
    }
    return Get-DisplayName (Get-SelectedCity)
}

function Get-ProtocolDistrictLabel {
    if ($script:FixtureWeatherSuccess -and $script:UsingMinimalCatalog -and $script:SelectedDistrictKey -ne $script:DefaultDistrictKey) {
        return $script:SelectedDistrictKey
    }
    return Get-DisplayName (Get-SelectedDistrict)
}
function Choice {
    param(
        [string]$Zh,
        [string]$En
    )

    if ($script:Language -eq 'zh') {
        return (T $Zh)
    }
    return $En
}

function Get-EnglishLocationDisplayNameByKey {
    param([string]$Key)

    switch ($Key) {
        '110000' { return 'Beijing' }
        '120000' { return 'Tianjin' }
        '130000' { return 'Hebei' }
        '140000' { return 'Shanxi' }
        '150000' { return 'Inner Mongolia' }
        '210000' { return 'Liaoning' }
        '220000' { return 'Jilin' }
        '230000' { return 'Heilongjiang' }
        '310000' { return 'Shanghai' }
        '320000' { return 'Jiangsu' }
        '330000' { return 'Zhejiang' }
        '340000' { return 'Anhui' }
        '350000' { return 'Fujian' }
        '360000' { return 'Jiangxi' }
        '370000' { return 'Shandong' }
        '410000' { return 'Henan' }
        '420000' { return 'Hubei' }
        '430000' { return 'Hunan' }
        '440000' { return 'Guangdong' }
        '440300' { return 'Shenzhen' }
        '440303' { return 'Luohu' }
        '440309' { return 'Longhua' }
        '450000' { return 'Guangxi' }
        '460000' { return 'Hainan' }
        '500000' { return 'Chongqing' }
        '510000' { return 'Sichuan' }
        '520000' { return 'Guizhou' }
        '530000' { return 'Yunnan' }
        '540000' { return 'Tibet' }
        '610000' { return 'Shaanxi' }
        '620000' { return 'Gansu' }
        '630000' { return 'Qinghai' }
        '640000' { return 'Ningxia' }
        '650000' { return 'Xinjiang' }
        '710000' { return 'Taiwan' }
        '810000' { return 'Hong Kong' }
        '820000' { return 'Macau' }
        default { return '' }
    }
}
function ConvertTo-EnglishLocationDisplayName {
    param([string]$Name)

    if ([string]::IsNullOrWhiteSpace($Name)) { return '' }
    $text = $Name.Trim() -replace '\s+', ' '
    $exact = @{
        'Guang Dong Sheng' = 'Guangdong'
        'Shen Zhen Shi' = 'Shenzhen'
        'Luo Hu Qu' = 'Luohu'
        'Long Hua Qu' = 'Longhua'
        'Bei Jing Shi' = 'Beijing'
        'Shang Hai Shi' = 'Shanghai'
        'Tian Jin Shi' = 'Tianjin'
        'Chong Qing Shi' = 'Chongqing'
        'Xiang Gang Te Bie Xing Zheng Qu' = 'Hong Kong'
        'Ao Men Te Bie Xing Zheng Qu' = 'Macau'
        'He Bei Sheng' = 'Hebei'
        'Shan Xi Sheng' = 'Shanxi'
        'Nei Meng Gu Zi Zhi Qu' = 'Inner Mongolia'
        'Liao Ning Sheng' = 'Liaoning'
        'Ji Lin Sheng' = 'Jilin'
        'Hei Long Jiang Sheng' = 'Heilongjiang'
        'Jiang Su Sheng' = 'Jiangsu'
        'Zhe Jiang Sheng' = 'Zhejiang'
        'An Hui Sheng' = 'Anhui'
        'Fu Jian Sheng' = 'Fujian'
        'Jiang Xi Sheng' = 'Jiangxi'
        'Shan Dong Sheng' = 'Shandong'
        'He Nan Sheng' = 'Henan'
        'Hu Bei Sheng' = 'Hubei'
        'Hu Nan Sheng' = 'Hunan'
        'Guang Xi Zhuang Zu Zi Zhi Qu' = 'Guangxi'
        'Hai Nan Sheng' = 'Hainan'
        'Si Chuan Sheng' = 'Sichuan'
        'Gui Zhou Sheng' = 'Guizhou'
        'Yun Nan Sheng' = 'Yunnan'
        'Xi Zang Zi Zhi Qu' = 'Tibet'
        'Gan Su Sheng' = 'Gansu'
        'Qing Hai Sheng' = 'Qinghai'
        'Ning Xia Hui Zu Zi Zhi Qu' = 'Ningxia'
        'Xin Jiang Wei Wu Er Zi Zhi Qu' = 'Xinjiang'
    }
    if ($exact.ContainsKey($text)) { return $exact[$text] }

    foreach ($suffix in @(' Te Bie Xing Zheng Qu', ' Zi Zhi Qu', ' Di Qu', ' Jie Dao', ' Sheng', ' Shi', ' Xian', ' Qu', ' Zhen', ' Meng', ' Qi')) {
        if ($text.EndsWith($suffix, [System.StringComparison]::OrdinalIgnoreCase)) {
            $text = $text.Substring(0, $text.Length - $suffix.Length).Trim()
            break
        }
    }

    if ([string]::IsNullOrWhiteSpace($text)) { return $Name.Trim() }
    $words = @($text -split '\s+' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    if ($words.Count -eq 0) { return $Name.Trim() }
    return (($words | ForEach-Object {
        $part = [string]$_
        if ($part.Length -le 1) { $part.ToUpperInvariant() } else { $part.Substring(0, 1).ToUpperInvariant() + $part.Substring(1).ToLowerInvariant() }
    }) -join '')
}

function ConvertTo-LanguageNearTermText {
    param([string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) { return $Value }
    $text = $Value.Trim()
    if ($script:Language -eq 'zh') { return $text }
    $map = @{
        (T '5Li06L+R6aKE5oql5pqC5LiN5Y+v55So') = 'Near-term forecast unavailable'
        (T '5Li06L+R5pyJ5by66ZmN6Zuo6aOO6Zmp') = 'Near-term heavy rain risk'
        (T '5Li06L+R5pyJ6ZmN6Zuo5Y+v6IO9') = 'Near-term rain possible'
        (T '5Li06L+R5pqC5peg5by66ZmN6Zuo') = 'No near-term heavy rain'
    }
    if ($map.ContainsKey($text)) { return $map[$text] }
    return $text
}

function Get-DisplayName {
    param([object]$Item)

    if ($null -eq $Item) { return '' }
    if ($script:Language -eq 'zh') {
        return (T $Item.Zh)
    }
    $keyName = Get-EnglishLocationDisplayNameByKey ([string]$Item.Key)
    if (-not [string]::IsNullOrWhiteSpace($keyName)) { return $keyName }
    return ConvertTo-EnglishLocationDisplayName ([string]$Item.En)
}

function Set-DefaultLocation {
    $script:SelectedProvinceKey = $script:DefaultProvinceKey
    $script:SelectedCityKey = $script:DefaultCityKey
    $script:SelectedDistrictKey = $script:DefaultDistrictKey
}

function Get-ProvinceByKey {
    param([string]$Key)

    return $script:Provinces | Where-Object { $_.Key -eq $Key } | Select-Object -First 1
}

function Find-ProvinceForCityKey {
    param([string]$CityKey)

    foreach ($province in @($script:Provinces)) {
        $city = @($province.Cities) | Where-Object { $_.Key -eq $CityKey } | Select-Object -First 1
        if ($null -ne $city) {
            return $province
        }
    }
    return $null
}

function Test-LocationKeys {
    param(
        [string]$ProvinceKey,
        [string]$CityKey,
        [string]$DistrictKey
    )

    $province = Get-ProvinceByKey -Key $ProvinceKey
    if ($null -eq $province) { return $false }
    $city = @($province.Cities) | Where-Object { $_.Key -eq $CityKey } | Select-Object -First 1
    if ($null -eq $city) { return $false }
    $district = @($city.Districts) | Where-Object { $_.Key -eq $DistrictKey } | Select-Object -First 1
    return ($null -ne $district)
}

function Get-SelectedProvince {
    $province = Get-ProvinceByKey -Key $script:SelectedProvinceKey
    if ($null -eq $province) {
        Set-DefaultLocation
        $province = Get-ProvinceByKey -Key $script:SelectedProvinceKey
    }
    if ($null -eq $province) {
        $province = @($script:Provinces)[0]
        $script:SelectedProvinceKey = $province.Key
    }
    return $province
}

function Get-SelectedCity {
    $province = Get-SelectedProvince
    $city = @($province.Cities) | Where-Object { $_.Key -eq $script:SelectedCityKey } | Select-Object -First 1
    if ($null -eq $city) {
        $city = @($province.Cities)[0]
        $script:SelectedCityKey = $city.Key
    }
    return $city
}

function Get-SelectedDistrict {
    $city = Get-SelectedCity
    $district = @($city.Districts) | Where-Object { $_.Key -eq $script:SelectedDistrictKey } | Select-Object -First 1
    if ($null -eq $district) {
        $district = @($city.Districts)[0]
        $script:SelectedDistrictKey = $district.Key
    }
    return $district
}

function Normalize-LocationKeyPart {
    param([object]$Value)

    if ($null -eq $Value) { return '_' }
    $text = ([string]$Value).Trim()
    if ([string]::IsNullOrWhiteSpace($text)) { return '_' }
    return $text.Replace('|', '%7C')
}

function New-LocationKey {
    param(
        [object]$ProvinceKey,
        [object]$CityKey,
        [object]$DistrictKey
    )

    return '{0}|{1}|{2}' -f `
        (Normalize-LocationKeyPart $ProvinceKey),
        (Normalize-LocationKeyPart $CityKey),
        (Normalize-LocationKeyPart $DistrictKey)
}

function Get-SelectedLocationKey {
    $province = Get-SelectedProvince
    $city = Get-SelectedCity
    $district = Get-SelectedDistrict

    return New-LocationKey -ProvinceKey $province.Key -CityKey $city.Key -DistrictKey $district.Key
}

function Get-LocationTitle {
    $province = Get-SelectedProvince
    $city = Get-SelectedCity
    $district = Get-SelectedDistrict
    return '{0} - {1} - {2}' -f (Get-DisplayName $province), (Get-DisplayName $city), (Get-DisplayName $district)
}

function Load-Settings {
    if (-not (Test-Path -LiteralPath $script:SettingsPath -PathType Leaf)) {
        return
    }

    try {
        $settings = Get-Content -LiteralPath $script:SettingsPath -Raw | ConvertFrom-Json
        if (@('zh', 'en') -contains $settings.Language) {
            $script:Language = [string]$settings.Language
        }
        if ($script:RefreshOptions -contains [int]$settings.RefreshSeconds) {
            $script:RefreshSeconds = [int]$settings.RefreshSeconds
        }
        if ($settings.ForecastSlotKey) {
            $script:SelectedForecastSlotKey = Normalize-ForecastSlotKey -SlotKey ([string]$settings.ForecastSlotKey)
        }
        if ($settings.ProvinceKey) {
            $script:SelectedProvinceKey = [string]$settings.ProvinceKey
        }
        if ($settings.CityKey) {
            $script:SelectedCityKey = [string]$settings.CityKey
        }
        if ($settings.DistrictKey) {
            $script:SelectedDistrictKey = [string]$settings.DistrictKey
        }
        if (-not $settings.ProvinceKey -and $settings.CityKey) {
            $province = Find-ProvinceForCityKey -CityKey ([string]$settings.CityKey)
            if ($null -ne $province) {
                $script:SelectedProvinceKey = $province.Key
            }
        }
        if (-not (Test-LocationKeys -ProvinceKey $script:SelectedProvinceKey -CityKey $script:SelectedCityKey -DistrictKey $script:SelectedDistrictKey)) {
            Set-DefaultLocation
        }
        Get-SelectedDistrict | Out-Null
        if ($script:FixtureWeatherSuccess -and $script:UsingMinimalCatalog) {
            $script:NextAutoRefreshAt = (Get-Date).AddSeconds($script:RefreshSeconds)
            return
        }
        $script:NextAutoRefreshAt = (Get-Date).AddSeconds($script:RefreshSeconds)
    } catch {
        $script:Language = 'zh'
        $script:RefreshSeconds = 60
        $script:SelectedForecastSlotKey = 'Day0'
        Set-DefaultLocation
    }
}

function Save-Settings {
    $settings = [ordered]@{}
    if (Test-Path -LiteralPath $script:SettingsPath -PathType Leaf) {
        try {
            $existing = Get-Content -LiteralPath $script:SettingsPath -Raw | ConvertFrom-Json
            foreach ($property in $existing.PSObject.Properties) {
                $settings[$property.Name] = $property.Value
            }
        } catch {}
    }

    $settings['Language'] = $script:Language
    $settings['ProvinceKey'] = $script:SelectedProvinceKey
    $settings['CityKey'] = $script:SelectedCityKey
    $settings['DistrictKey'] = $script:SelectedDistrictKey
    $settings['RefreshSeconds'] = $script:RefreshSeconds
    $settings['ForecastSlotKey'] = $script:SelectedForecastSlotKey
    $settings['SavedAt'] = (Get-Date).ToString('s')

    $directory = Split-Path -Parent $script:SettingsPath
    if (-not [string]::IsNullOrWhiteSpace($directory)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }
    $settings | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $script:SettingsPath -Encoding UTF8
}

function ConvertTo-NullableDouble {
    param([object]$Value)

    if ($null -eq $Value -or [string]::IsNullOrWhiteSpace([string]$Value)) {
        return $null
    }
    try {
        return [double]$Value
    } catch {
        return $null
    }
}

function ConvertTo-NullableInt {
    param([object]$Value)

    if ($null -eq $Value -or [string]::IsNullOrWhiteSpace([string]$Value)) {
        return $null
    }
    try {
        return [int]$Value
    } catch {
        return $null
    }
}

function Get-PropertyValue {
    param(
        [object]$Object,
        [string]$Name,
        [object]$Default = $null
    )

    if ($null -eq $Object -or $null -eq $Object.PSObject.Properties[$Name]) {
        return $Default
    }
    return $Object.PSObject.Properties[$Name].Value
}

function Get-SeriesValue {
    param(
        [object]$Series,
        [int]$Index
    )

    if ($null -eq $Series) { return $null }
    $items = @($Series)
    if ($Index -lt 0 -or $Index -ge $items.Count) { return $null }
    return $items[$Index]
}

function Test-CoordinateValue {
    param([object]$Value)

    $number = ConvertTo-NullableDouble -Value $Value
    return ($null -ne $number -and -not [double]::IsNaN($number) -and -not [double]::IsInfinity($number))
}

function Get-SelectedWeatherLocation {
    $province = Get-SelectedProvince
    $city = Get-SelectedCity
    $district = Get-SelectedDistrict
    if (-not (Test-CoordinateValue $district.Lat) -or -not (Test-CoordinateValue $district.Lon)) {
        throw ('LOCATION_DATA_FAIL: {0}: coordinate missing or invalid' -f (Get-SelectedLocationKey))
    }

    return [pscustomobject]@{
        Lat = [double]$district.Lat
        Lon = [double]$district.Lon
        Label = Get-DisplayName $district
        LocationKey = Get-SelectedLocationKey
    }
}

function Get-WeatherUrls {
    $location = Get-SelectedWeatherLocation
    $lat = $location.Lat.ToString([Globalization.CultureInfo]::InvariantCulture)
    $lon = $location.Lon.ToString([Globalization.CultureInfo]::InvariantCulture)
    $openMeteo = [string]::Concat(
        'https://api.open-meteo.com/v1/forecast?latitude=',
        $lat,
        '&longitude=',
        $lon,
        '&current=',
        $script:CurrentFields,
        '&hourly=',
        $script:HourlyFields,
        '&daily=',
        $script:DailyFields,
        '&timezone=auto',
        '&forecast_days=',
        [string]$script:ForecastDayCount,
        '&forecast_hours=',
        [string]$script:ForecastHourCount,
        '&temperature_unit=celsius',
        '&wind_speed_unit=kmh',
        '&precipitation_unit=mm'
    )
    $wttr = [string]::Concat('https://wttr.in/', $lat, ',', $lon, '?format=j1')

    [pscustomobject]@{
        OpenMeteo = $openMeteo
        Wttr = $wttr
    }
}

function New-WeatherHttpClient {
    $client = New-Object LonghuaWeatherWorkerTimeoutWebClient
    $client.TimeoutMilliseconds = $script:WeatherRequestTimeoutMs
    $client.Headers.Add('User-Agent', 'LonghuaWeatherWidget/2.0')
    return $client
}

function Invoke-WeatherHttpGet {
    param([string]$Uri)

    $timeoutSeconds = [Math]::Max(10, [int][Math]::Ceiling($script:WeatherRequestTimeoutMs / 1000.0))
    try {
        $response = Invoke-WebRequest `
            -UseBasicParsing `
            -Uri $Uri `
            -Headers @{ 'User-Agent' = 'LonghuaWeatherWidget/2.0' } `
            -TimeoutSec $timeoutSeconds
        return [string]$response.Content
    } catch {
        $webRequestError = $_.Exception.Message
        $curl = Get-Command curl.exe -ErrorAction SilentlyContinue
        if ($null -eq $curl) {
            throw $webRequestError
        }

        $output = & $curl.Source --fail --silent --show-error --max-time $timeoutSeconds --user-agent 'LonghuaWeatherWidget/2.0' $Uri 2>&1
        if ($LASTEXITCODE -ne 0) {
            throw ('{0}; curl fallback failed: {1}' -f $webRequestError, (($output | Out-String).Trim()))
        }
        return (($output | Out-String).Trim())
    }
}

function Get-WeatherText {
    param([object]$Code)

    if ($null -eq $Code) {
        return Choice '5aSp5rCU' 'Weather'
    }

    switch ([int]$Code) {
        0 { return Choice '5pm0' 'Clear' }
        1 { return Choice '5pm05pyJ5pe25aSa5LqR' 'Mainly clear' }
        2 { return Choice '5aSa5LqR' 'Partly cloudy' }
        3 { return Choice '5aSa5LqR' 'Cloudy' }
        45 { return Choice '6Zu+5aSp' 'Fog' }
        48 { return Choice '6Zu+5aSp' 'Fog' }
        51 { return Choice '5bCP6Zuo' 'Light drizzle' }
        53 { return Choice '5bCP6Zuo' 'Drizzle' }
        55 { return Choice '5bCP6Zuo' 'Heavy drizzle' }
        61 { return Choice '5bCP6Zuo' 'Light rain' }
        63 { return Choice '6ZmN6Zuo' 'Rain' }
        65 { return Choice '5by66ZmN6Zuo' 'Heavy rain' }
        80 { return Choice '6Zi15Zuo' 'Rain showers' }
        81 { return Choice '6Zi15Zuo' 'Rain showers' }
        82 { return Choice '5by66Zi15Zuo' 'Heavy rain showers' }
        95 { return Choice '6Zu36Zuo' 'Thunderstorm' }
        96 { return Choice '6Zu36Zuo' 'Thunderstorm' }
        99 { return Choice '6Zu36Zuo' 'Thunderstorm' }
        default { return Choice '5aSp5rCU' 'Weather' }
    }
}

function Normalize-ForecastSlotKey {
    param([string]$SlotKey)

    if ([string]::IsNullOrWhiteSpace($SlotKey)) { return 'Day0' }
    switch ([string]$SlotKey) {
        'Now' { return 'Day0' }
        '+1h' { return 'Hour+1h' }
        '+3h' { return 'Hour+3h' }
        '+6h' { return 'Hour+6h' }
        '+12h' { return 'Hour+12h' }
        'Tonight' { return 'Hour+12h' }
        'Tomorrow' { return 'Day1' }
        default { return [string]$SlotKey }
    }
}

function Get-ForecastSlotDefinition {
    param([string]$SlotKey)

    $normalizedSlotKey = Normalize-ForecastSlotKey -SlotKey $SlotKey
    $definition = $script:ForecastSlotDefinitions | Where-Object { $_.Key -eq $normalizedSlotKey } | Select-Object -First 1
    if ($null -ne $definition) { return $definition }
    return $script:ForecastSlotDefinitions[0]
}

function Get-ForecastTargetTime {
    param(
        [object]$Definition,
        [DateTime]$BaseTime
    )

    switch ($Definition.Kind) {
        'Current' { return $BaseTime }
        'Offset' { return $BaseTime.AddHours([double]$Definition.OffsetHours) }
        'Hour' { return $BaseTime.AddHours([double]$Definition.OffsetHours) }
        'Day' {
            $offsetDays = [int]$Definition.OffsetDays
            if ($offsetDays -le 0) { return $BaseTime }
            return $BaseTime.Date.AddDays($offsetDays).AddHours(12)
        }
        'Tonight' {
            $target = $BaseTime.Date.AddHours(20)
            if ($target -le $BaseTime) { $target = $target.AddDays(1) }
            return $target
        }
        'Tomorrow' { return $BaseTime.Date.AddDays(1).AddHours(9) }
        default { return $BaseTime }
    }
}

function Get-ForecastSlotLabel {
    param([object]$Definition)

    if ([string]$Definition.Kind -eq 'Day') {
        $offsetDays = [int]$Definition.OffsetDays
        switch ($offsetDays) {
            0 { return Choice '5LuK5aSp' 'Today' }
            1 { return Choice '5piO5aSp' 'Tomorrow' }
            2 { return Choice '5ZCO5aSp' '+2d' }
            default { return $(if ($script:Language -eq 'zh') { '+{0}{1}' -f $offsetDays, (T '5aSp') } else { '+{0}d' -f $offsetDays }) }
        }
    }
    if ([string]$Definition.Kind -eq 'Hour') {
        return '+{0}h' -f ([int]$Definition.OffsetHours)
    }
    switch ([string]$Definition.LabelKey) {
        'Now' { return Choice '5LuK5aSp' 'Today' }
        'Plus1h' { return '+1h' }
        'Plus3h' { return '+3h' }
        'Plus6h' { return '+6h' }
        'Plus12h' { return '+12h' }
        'Tonight' { return '+12h' }
        'Tomorrow' { return Choice '5piO5aSp' 'Tomorrow' }
        default { return Choice '5LuK5aSp' 'Today' }
    }
}

function Get-ForecastSlotsPayload {
    $slots = New-Object System.Collections.Generic.List[object]
    foreach ($definition in @($script:ForecastSlotDefinitions)) {
        $slots.Add([ordered]@{
            key = [string]$definition.Key
            label = Get-ForecastSlotLabel -Definition $definition
            kind = [string]$definition.Kind
            selected = ([string]$definition.Key -eq [string]$script:SelectedForecastSlotKey)
        }) | Out-Null
    }
    return $slots.ToArray()
}

function Get-NearestHourlyIndex {
    param(
        [object]$Hourly,
        [DateTime]$TargetTime
    )

    $times = @((Get-PropertyValue -Object $Hourly -Name 'time' -Default @()))
    if ($times.Count -eq 0) { return -1 }
    $bestIndex = -1
    $bestDiff = [double]::MaxValue
    for ($i = 0; $i -lt $times.Count; $i++) {
        try { $time = [datetime]$times[$i] } catch { continue }
        $diff = [Math]::Abs(($time - $TargetTime).TotalMinutes)
        if ($diff -lt $bestDiff) {
            $bestIndex = $i
            $bestDiff = $diff
        }
    }
    return $bestIndex
}

function Get-DailyIndexForTime {
    param(
        [object]$Daily,
        [DateTime]$TargetTime
    )

    $times = @((Get-PropertyValue -Object $Daily -Name 'time' -Default @()))
    for ($i = 0; $i -lt $times.Count; $i++) {
        try {
            if (([datetime]$times[$i]).Date -eq $TargetTime.Date) { return $i }
        } catch {}
    }
    return 0
}
function Resolve-NearTermForecast {
    param([object]$Weather)

    $hourly = $Weather.hourly
    if ($null -eq $hourly) {
        return Choice '5Li06L+R6aKE5oql5pqC5LiN5Y+v55So' 'Near-term forecast unavailable'
    }

    $times = @((Get-PropertyValue -Object $hourly -Name 'time' -Default @()))
    if ($times.Count -eq 0) {
        return Choice '5Li06L+R6aKE5oql5pqC5LiN5Y+v55So' 'Near-term forecast unavailable'
    }

    $now = Get-Date
    $maxRain = 0.0
    $maxProbability = 0
    foreach ($i in 0..($times.Count - 1)) {
        try {
            $time = [datetime]$times[$i]
        } catch {
            continue
        }
        if ($time -lt $now -or $time -gt $now.AddHours(2)) {
            continue
        }

        $precip = ConvertTo-NullableDouble (Get-SeriesValue -Series $hourly.precipitation -Index $i)
        $rain = ConvertTo-NullableDouble (Get-SeriesValue -Series $hourly.rain -Index $i)
        $showers = ConvertTo-NullableDouble (Get-SeriesValue -Series $hourly.showers -Index $i)
        $probability = ConvertTo-NullableInt (Get-SeriesValue -Series $hourly.precipitation_probability -Index $i)
        $amount = [Math]::Max($(if ($null -ne $precip) { $precip } else { 0.0 }), ($(if ($null -ne $rain) { $rain } else { 0.0 }) + $(if ($null -ne $showers) { $showers } else { 0.0 })))
        $maxRain = [Math]::Max($maxRain, $amount)
        if ($null -ne $probability) {
            $maxProbability = [Math]::Max($maxProbability, [int]$probability)
        }
    }

    if ($maxRain -ge 8.0 -or $maxProbability -ge 80) {
        return Choice '5Li06L+R5pyJ5by66ZmN6Zuo6aOO6Zmp' 'Near-term heavy rain risk'
    }

    if ($maxRain -gt 0.0 -or $maxProbability -ge 40) {
        return Choice '5Li06L+R5pyJ6ZmN6Zuo5Y+v6IO9' 'Near-term rain possible'
    }

    return Choice '5Li06L+R5pqC5peg5by66ZmN6Zuo' 'No near-term heavy rain'
}

function ConvertTo-OpenMeteoSnapshot {
    param(
        [object]$Weather,
        [bool]$FromCache = $false
    )

    $current = $Weather.current
    $hourly = $Weather.hourly
    $daily = $Weather.daily
    $definition = Get-ForecastSlotDefinition -SlotKey $script:SelectedForecastSlotKey
    $isCurrent = ([string]$definition.Kind -eq 'Current')
    $baseTime = Get-Date
    try { if ($null -ne $current.time) { $baseTime = [datetime]$current.time } } catch {}
    $targetTime = Get-ForecastTargetTime -Definition $definition -BaseTime $baseTime
    $dailyIndex = Get-DailyIndexForTime -Daily $daily -TargetTime $targetTime

    if ($isCurrent) {
        $code = ConvertTo-NullableInt (Get-PropertyValue -Object $current -Name 'weather_code')
        $precip = ConvertTo-NullableDouble (Get-PropertyValue -Object $current -Name 'precipitation')
        $rain = ConvertTo-NullableDouble (Get-PropertyValue -Object $current -Name 'rain')
        $showers = ConvertTo-NullableDouble (Get-PropertyValue -Object $current -Name 'showers')
        $temp = ConvertTo-NullableDouble (Get-PropertyValue -Object $current -Name 'temperature_2m')
        $feels = ConvertTo-NullableDouble (Get-PropertyValue -Object $current -Name 'apparent_temperature')
        $humidity = ConvertTo-NullableDouble (Get-PropertyValue -Object $current -Name 'relative_humidity_2m')
        $cloud = ConvertTo-NullableDouble (Get-PropertyValue -Object $current -Name 'cloud_cover')
        $pressure = ConvertTo-NullableDouble (Get-PropertyValue -Object $current -Name 'surface_pressure')
        $wind = ConvertTo-NullableDouble (Get-PropertyValue -Object $current -Name 'wind_speed_10m')
        $gust = ConvertTo-NullableDouble (Get-PropertyValue -Object $current -Name 'wind_gusts_10m')
        $isDay = ConvertTo-NullableInt (Get-PropertyValue -Object $current -Name 'is_day')
        $mode = Get-ForecastSlotLabel -Definition (Get-ForecastSlotDefinition -SlotKey $script:SelectedForecastSlotKey)
    } else {
        $hourIndex = Get-NearestHourlyIndex -Hourly $hourly -TargetTime $targetTime
        if ($hourIndex -lt 0) { $hourIndex = 0 }
        $code = ConvertTo-NullableInt (Get-SeriesValue -Series $hourly.weather_code -Index $hourIndex)
        $precip = ConvertTo-NullableDouble (Get-SeriesValue -Series $hourly.precipitation -Index $hourIndex)
        $rain = ConvertTo-NullableDouble (Get-SeriesValue -Series $hourly.rain -Index $hourIndex)
        $showers = ConvertTo-NullableDouble (Get-SeriesValue -Series $hourly.showers -Index $hourIndex)
        $temp = ConvertTo-NullableDouble (Get-SeriesValue -Series $hourly.temperature_2m -Index $hourIndex)
        $feels = ConvertTo-NullableDouble (Get-SeriesValue -Series $hourly.apparent_temperature -Index $hourIndex)
        $humidity = ConvertTo-NullableDouble (Get-SeriesValue -Series $hourly.relative_humidity_2m -Index $hourIndex)
        $cloud = ConvertTo-NullableDouble (Get-SeriesValue -Series $hourly.cloud_cover -Index $hourIndex)
        $pressure = ConvertTo-NullableDouble (Get-SeriesValue -Series $hourly.surface_pressure -Index $hourIndex)
        $wind = ConvertTo-NullableDouble (Get-SeriesValue -Series $hourly.wind_speed_10m -Index $hourIndex)
        $gust = ConvertTo-NullableDouble (Get-SeriesValue -Series $hourly.wind_gusts_10m -Index $hourIndex)
        $isDay = ConvertTo-NullableInt (Get-SeriesValue -Series $hourly.is_day -Index $hourIndex)
        $mode = Get-ForecastSlotLabel -Definition $definition
        $slotProbability = ConvertTo-NullableInt (Get-SeriesValue -Series $hourly.precipitation_probability -Index $hourIndex)
    }

    $rainNow = [Math]::Max($(if ($null -ne $precip) { $precip } else { 0.0 }), ($(if ($null -ne $rain) { $rain } else { 0.0 }) + $(if ($null -ne $showers) { $showers } else { 0.0 })))
    $todayRain = ConvertTo-NullableDouble (Get-SeriesValue -Series $daily.precipitation_sum -Index $dailyIndex)
    $rainProbability = ConvertTo-NullableInt (Get-SeriesValue -Series $daily.precipitation_probability_max -Index $dailyIndex)
    if ($null -ne $slotProbability) { $rainProbability = $slotProbability }

    [ordered]@{
        type = 'weather'
        status = 'ok'
        title = $(if ($script:Language -eq 'zh') { T '5L2g5LiA5p2l5bCx5piv5aW95aSp5rCU' } else { 'Weather' })
        mode = $mode
        slot_key = [string]$definition.Key
        source = 'Open-Meteo'
        fromCache = [bool]$FromCache
        from_cache = [bool]$FromCache
        city = Get-ProtocolCityLabel
        district = Get-ProtocolDistrictLabel
        location = Get-ProtocolLocationTitle
        locationLabel = Get-ProtocolLocationTitle
        location_key = Get-ProtocolLocationKey
        locationKey = Get-ProtocolLocationKey
        temp = $temp
        feels_like = $feels
        condition = Get-WeatherText -Code $code
        weather_code = $code
        rain = $rainNow
        today_rain = $(if ($null -ne $todayRain) { $todayRain } else { $rainNow })
        rain_probability = $(if ($null -ne $rainProbability) { $rainProbability } else { 0 })
        humidity = $humidity
        cloud = $cloud
        pressure = $pressure
        wind = $wind
        gust = $gust
        is_day = $isDay
        near_term = Resolve-NearTermForecast -Weather $Weather
        forecastSlots = Get-ForecastSlotsPayload
        current = [ordered]@{ temp = $temp; feels_like = $feels; condition = (Get-WeatherText -Code $code); weather_code = $code; is_day = $isDay }
        metrics = [ordered]@{ rain = $rainNow; today_rain = $(if ($null -ne $todayRain) { $todayRain } else { $rainNow }); rain_probability = $(if ($null -ne $rainProbability) { $rainProbability } else { 0 }); humidity = $humidity; cloud = $cloud; pressure = $pressure; wind = $wind; gust = $gust }
        updated = ('{0} {1} | {2}' -f $(if ($script:Language -eq 'zh') { T '5bey5pu05paw' } else { 'Updated' }), (Get-Date -Format 'HH:mm:ss'), 'Open-Meteo')
        timestamp = [DateTimeOffset]::Now.ToUnixTimeSeconds()
    }
}
function ConvertTo-WttrSnapshot {
    param([object]$Weather)

    $current = @($Weather.current_condition)[0]
    $today = @($Weather.weather)[0]
    $hourly = @($today.hourly)
    $todayRain = 0.0
    $rainProbability = 0
    foreach ($hour in $hourly) {
        $hourRain = ConvertTo-NullableDouble (Get-PropertyValue -Object $hour -Name 'precipMM')
        if ($null -ne $hourRain) { $todayRain += $hourRain }
        $hourProbability = ConvertTo-NullableInt (Get-PropertyValue -Object $hour -Name 'chanceofrain')
        if ($null -ne $hourProbability) { $rainProbability = [Math]::Max($rainProbability, [int]$hourProbability) }
    }

    $rawText = ''
    if ($current.weatherDesc) {
        $rawText = [string]@($current.weatherDesc)[0].value
    }

    $rainNow = ConvertTo-NullableDouble (Get-PropertyValue -Object $current -Name 'precipMM')
    [ordered]@{
        type = 'weather'
        status = 'ok'
        title = $(if ($script:Language -eq 'zh') { T '5L2g5LiA5p2l5bCx5piv5aW95aSp5rCU' } else { 'Weather' })
        mode = Get-ForecastSlotLabel -Definition (Get-ForecastSlotDefinition -SlotKey $script:SelectedForecastSlotKey)
        source = 'wttr.in'
        fromCache = [bool]$false
        from_cache = [bool]$false
        city = Get-ProtocolCityLabel
        district = Get-ProtocolDistrictLabel
        location = Get-ProtocolLocationTitle
        locationLabel = Get-ProtocolLocationTitle
        location_key = Get-ProtocolLocationKey
        locationKey = Get-ProtocolLocationKey
        temp = ConvertTo-NullableDouble (Get-PropertyValue -Object $current -Name 'temp_C')
        feels_like = ConvertTo-NullableDouble (Get-PropertyValue -Object $current -Name 'FeelsLikeC')
        condition = $(if ([string]::IsNullOrWhiteSpace($rawText)) { Choice '5aSp5rCU' 'Weather' } else { $rawText })
        weather_code = $null
        rain = $(if ($null -ne $rainNow) { $rainNow } else { 0.0 })
        today_rain = $todayRain
        rain_probability = $rainProbability
        humidity = ConvertTo-NullableDouble (Get-PropertyValue -Object $current -Name 'humidity')
        cloud = ConvertTo-NullableDouble (Get-PropertyValue -Object $current -Name 'cloudcover')
        pressure = ConvertTo-NullableDouble (Get-PropertyValue -Object $current -Name 'pressure')
        wind = ConvertTo-NullableDouble (Get-PropertyValue -Object $current -Name 'windspeedKmph')
        gust = ConvertTo-NullableDouble (Get-PropertyValue -Object $current -Name 'WindGustKmph')
        is_day = $null
        forecastSlots = Get-ForecastSlotsPayload
        near_term = Choice '5Li06L+R6aKE5oql5pqC5LiN5Y+v55So' 'Near-term forecast unavailable'
        updated = ('{0} {1} | {2}' -f $(if ($script:Language -eq 'zh') { T '5bey5pu05paw' } else { 'Updated' }), (Get-Date -Format 'HH:mm:ss'), 'wttr.in')
        timestamp = [DateTimeOffset]::Now.ToUnixTimeSeconds()
    }
}

function Save-WeatherSnapshotCache {
    param(
        [string]$LocationKey,
        [object]$Snapshot,
        [object]$RawWeather = $null,
        [string]$Source = ''
    )

    if ([string]::IsNullOrWhiteSpace($LocationKey)) { return }
    $script:WeatherSnapshotCache[$LocationKey] = [pscustomobject]@{
        Snapshot = $Snapshot
        FetchedAt = Get-Date
        Source = $Source
    }
    if ($null -ne $RawWeather) {
        $script:RawWeatherCache[$LocationKey] = [pscustomobject]@{
            Weather = $RawWeather
            FetchedAt = Get-Date
            Source = $Source
        }
    }
}

function Copy-SnapshotPayloadForPersistence {
    param([object]$Snapshot)

    $copy = [ordered]@{}
    if ($null -eq $Snapshot) { return $copy }
    if ($Snapshot -is [System.Collections.IDictionary]) {
        foreach ($key in $Snapshot.Keys) { $copy[$key] = $Snapshot[$key] }
        return $copy
    }
    foreach ($property in $Snapshot.PSObject.Properties) {
        $copy[$property.Name] = $property.Value
    }
    return $copy
}

function Get-SnapshotFieldValue {
    param(
        [object]$Snapshot,
        [string[]]$Names
    )

    foreach ($name in $Names) {
        $value = $null
        if ($Snapshot -is [System.Collections.IDictionary] -and $Snapshot.Contains($name)) {
            $value = $Snapshot[$name]
        } else {
            $value = Get-PropertyValue -Object $Snapshot -Name $name -Default $null
        }
        if ($null -ne $value -and -not [string]::IsNullOrWhiteSpace([string]$value)) {
            return $value
        }
    }
    return $null
}

function ConvertTo-SnapshotBool {
    param([object]$Value)

    if ($null -eq $Value) { return $false }
    if ($Value -is [bool]) { return [bool]$Value }
    $text = ([string]$Value).Trim()
    return ($text -eq 'true' -or $text -eq '1' -or $text -eq 'yes')
}
function Get-PersistentSnapshotSkipReason {
    param([object]$Snapshot)

    if ($null -eq $Snapshot) { return 'empty-payload' }
    $status = [string](Get-SnapshotFieldValue -Snapshot $Snapshot -Names @('status'))
    if ($status -ne 'ok') { return 'status-not-ok' }
    $source = [string](Get-SnapshotFieldValue -Snapshot $Snapshot -Names @('source'))
    if ([string]::IsNullOrWhiteSpace($source)) { return 'missing-source' }
    $locationKey = [string](Get-SnapshotFieldValue -Snapshot $Snapshot -Names @('locationKey', 'location_key'))
    if ([string]::IsNullOrWhiteSpace($locationKey)) { return 'missing-location-key' }
    $fromCache = ConvertTo-SnapshotBool (Get-SnapshotFieldValue -Snapshot $Snapshot -Names @('fromCache', 'from_cache'))
    if ($fromCache) { return 'memory-cache' }
    $fixture = ConvertTo-SnapshotBool (Get-SnapshotFieldValue -Snapshot $Snapshot -Names @('fixture'))
    if (($fixture -or $source -eq 'fixture' -or $script:FixtureWeatherSuccess) -and -not $script:AllowFixtureSnapshotWrite) {
        return 'fixture-not-allowed'
    }
    return ''
}

function Write-SnapshotPersistenceIssue {
    param([string]$Message)

    if ($script:IpcModeActive) {
        Write-ProtocolEvent -Type 'status' -Payload ([ordered]@{
            phase = 'snapshot-error'
            message = $Message
            locationKey = Get-ProtocolLocationKey
        })
        return
    }
    try { [Console]::Error.WriteLine('[Worker] ' + $Message) } catch {}
}

function Save-PersistentWeatherSnapshot {
    param([object]$Snapshot)

    Write-WorkerStartupTrace 'Snapshot write start'
    $skipReason = Get-PersistentSnapshotSkipReason -Snapshot $Snapshot
    if (-not [string]::IsNullOrWhiteSpace($skipReason)) {
        Write-WorkerStartupTrace ('Snapshot write skipped reason: ' + $skipReason)
        return
    }

    $tempPath = ''
    try {
        $source = [string](Get-SnapshotFieldValue -Snapshot $Snapshot -Names @('source'))
        $locationKey = [string](Get-SnapshotFieldValue -Snapshot $Snapshot -Names @('locationKey', 'location_key'))
        $locationLabel = [string](Get-SnapshotFieldValue -Snapshot $Snapshot -Names @('locationLabel', 'location'))
        $fixture = ConvertTo-SnapshotBool (Get-SnapshotFieldValue -Snapshot $Snapshot -Names @('fixture'))
        $payloadCopy = Copy-SnapshotPayloadForPersistence -Snapshot $Snapshot
        $payloadCopy['fromSnapshot'] = $false
        $payloadCopy['from_snapshot'] = $false

        $envelope = [ordered]@{
            schema = 1
            savedAt = [DateTime]::UtcNow.ToString('o')
            source = $source
            fixture = [bool]$fixture
            fromSnapshot = $true
            locationKey = $locationKey
            locationLabel = $locationLabel
            language = $script:Language
            refreshSeconds = $script:RefreshSeconds
            selectedForecastSlot = $script:SelectedForecastSlotKey
            payload = $payloadCopy
        }

        $snapshotPath = $script:SnapshotPath
        if ([string]::IsNullOrWhiteSpace($snapshotPath)) { $snapshotPath = Get-PersistentWeatherSnapshotPath }
        $directory = Split-Path -Parent $snapshotPath
        if (-not [string]::IsNullOrWhiteSpace($directory) -and -not (Test-Path -LiteralPath $directory -PathType Container)) {
            New-Item -ItemType Directory -Path $directory -Force | Out-Null
        }
        $tempPath = Join-Path $directory ('weather-snapshot.{0}.{1}.tmp' -f $PID, [Guid]::NewGuid().ToString('N'))
        $snapshotJson = $envelope | ConvertTo-Json -Depth 12
        $utf8NoBom = New-Object System.Text.UTF8Encoding $false
        [IO.File]::WriteAllText($tempPath, $snapshotJson, $utf8NoBom)
        if (Test-Path -LiteralPath $snapshotPath -PathType Leaf) {
            $backupPath = $snapshotPath + '.replace.bak'
            [IO.File]::Replace($tempPath, $snapshotPath, $backupPath, $true)
            Remove-Item -LiteralPath $backupPath -Force -ErrorAction SilentlyContinue
        } else {
            [IO.File]::Move($tempPath, $snapshotPath)
        }
        Write-WorkerStartupTrace 'Snapshot write success'
    } catch {
        Write-WorkerStartupTrace ('Snapshot write failed: ' + $_.Exception.Message)
        Write-SnapshotPersistenceIssue -Message ('Snapshot write failed: {0}' -f $_.Exception.Message)
        try {
            if (-not [string]::IsNullOrWhiteSpace($tempPath) -and (Test-Path -LiteralPath $tempPath -PathType Leaf)) {
                Remove-Item -LiteralPath $tempPath -Force -ErrorAction SilentlyContinue
            }
        } catch {}
    }
}
function Get-PersistentSnapshotDiagnosticsPayload {
    $snapshotPath = $script:SnapshotPath
    if ([string]::IsNullOrWhiteSpace($snapshotPath)) { $snapshotPath = Get-PersistentWeatherSnapshotPath }

    $diagnostics = [ordered]@{
        exists = $false
        valid = $false
        path = $snapshotPath
        schema = $null
        savedAt = $null
        ageSeconds = $null
        source = $null
        fixture = $false
        locationKey = $null
        locationLabel = $null
        matchesCurrentLocation = $false
        stale = $false
        skipReason = 'missing-file'
    }

    if ([string]::IsNullOrWhiteSpace($snapshotPath) -or -not (Test-Path -LiteralPath $snapshotPath -PathType Leaf)) {
        return $diagnostics
    }

    $diagnostics.exists = $true
    $text = ''
    try {
        $text = [IO.File]::ReadAllText($snapshotPath, [Text.Encoding]::UTF8)
    } catch {
        $diagnostics.skipReason = 'read-failed: ' + $_.Exception.GetType().Name
        return $diagnostics
    }

    $envelope = $null
    try {
        $envelope = $text | ConvertFrom-Json
    } catch {
        $diagnostics.skipReason = 'malformed-json'
        return $diagnostics
    }

    $schema = ConvertTo-NullableInt -Value (Get-SnapshotFieldValue -Snapshot $envelope -Names @('schema'))
    $savedAtValue = [string](Get-SnapshotFieldValue -Snapshot $envelope -Names @('savedAt'))
    $source = [string](Get-SnapshotFieldValue -Snapshot $envelope -Names @('source'))
    $locationKey = [string](Get-SnapshotFieldValue -Snapshot $envelope -Names @('locationKey', 'location_key'))
    $locationLabel = [string](Get-SnapshotFieldValue -Snapshot $envelope -Names @('locationLabel', 'location'))
    $fixture = ConvertTo-SnapshotBool (Get-SnapshotFieldValue -Snapshot $envelope -Names @('fixture'))

    $diagnostics.schema = $schema
    $diagnostics.savedAt = $savedAtValue
    $diagnostics.source = $source
    $diagnostics.fixture = [bool]$fixture
    $diagnostics.locationKey = $locationKey
    $diagnostics.locationLabel = $locationLabel

    $savedAtUtc = $null
    if (-not [string]::IsNullOrWhiteSpace($savedAtValue)) {
        try {
            $styles = [Globalization.DateTimeStyles]::AssumeUniversal -bor [Globalization.DateTimeStyles]::AdjustToUniversal
            $savedAtUtc = [DateTime]::Parse($savedAtValue, [Globalization.CultureInfo]::InvariantCulture, $styles).ToUniversalTime()
            $diagnostics.savedAt = $savedAtUtc.ToString('o')
            $diagnostics.ageSeconds = [int64][Math]::Max(0, ([DateTime]::UtcNow - $savedAtUtc).TotalSeconds)
            $diagnostics.stale = ([int64]$diagnostics.ageSeconds -ge 86400)
        } catch {}
    }

    if ($schema -ne 1) {
        $diagnostics.skipReason = 'invalid-schema'
        return $diagnostics
    }
    if ($null -eq $savedAtUtc) {
        $diagnostics.skipReason = 'invalid-savedAt'
        return $diagnostics
    }
    if ([string]::IsNullOrWhiteSpace($source)) {
        $diagnostics.skipReason = 'invalid-source'
        return $diagnostics
    }
    if ([string]::IsNullOrWhiteSpace($locationKey)) {
        $diagnostics.skipReason = 'invalid-locationKey'
        return $diagnostics
    }

    $payload = Get-PropertyValue -Object $envelope -Name 'payload' -Default $null
    $payloadStatus = [string](Get-SnapshotFieldValue -Snapshot $payload -Names @('status'))
    if ($payloadStatus -ne 'ok') {
        $diagnostics.skipReason = 'invalid-payload-status'
        return $diagnostics
    }

    $currentLocationKey = Get-ProtocolLocationKey
    $diagnostics.matchesCurrentLocation = [string]::Equals($locationKey, $currentLocationKey, [StringComparison]::OrdinalIgnoreCase)
    $diagnostics.valid = $true
    if (-not $diagnostics.matchesCurrentLocation) {
        $diagnostics.skipReason = 'cross-location'
        return $diagnostics
    }

    $diagnostics.skipReason = $null
    return $diagnostics
}
function Get-CachedWeatherSnapshot {
    param([string]$LocationKey)

    if ([string]::IsNullOrWhiteSpace($LocationKey)) { return $null }
    if (-not $script:WeatherSnapshotCache.ContainsKey($LocationKey)) { return $null }
    $entry = $script:WeatherSnapshotCache[$LocationKey]
    $copy = [ordered]@{}
    foreach ($property in $entry.Snapshot.GetEnumerator()) {
        $copy[$property.Key] = $property.Value
    }
    $definition = Get-ForecastSlotDefinition -SlotKey $script:SelectedForecastSlotKey
    $copy['fromCache'] = $true
    $copy['from_cache'] = $true
    $copy['title'] = $(if ($script:Language -eq 'zh') { T '5L2g5LiA5p2l5bCx5piv5aW95aSp5rCU' } else { 'Weather' })
    $copy['mode'] = Get-ForecastSlotLabel -Definition $definition
    $copy['city'] = Get-ProtocolCityLabel
    $copy['district'] = Get-ProtocolDistrictLabel
    $copy['location'] = Get-ProtocolLocationTitle
    $copy['locationLabel'] = Get-ProtocolLocationTitle
    $copy['location_key'] = Get-ProtocolLocationKey
    $copy['locationKey'] = Get-ProtocolLocationKey
    if ($copy.Contains('weather_code')) {
        $copy['condition'] = Get-WeatherText -Code $copy['weather_code']
    }
    if ($copy.Contains('near_term')) {
        $copy['near_term'] = ConvertTo-LanguageNearTermText ([string]$copy['near_term'])
    }
    $copy['forecastSlots'] = Get-ForecastSlotsPayload
    $copy['updated'] = ('{0} {1} | {2}' -f $(if ($script:Language -eq 'zh') { T '57yT5a2Y' } else { 'Cached' }), $entry.FetchedAt.ToString('HH:mm:ss'), $entry.Source)
    return $copy
}

function New-FixtureWeatherSnapshot {
    Write-WorkerStartupTrace 'Before first fixture weather payload build'
    $timestamp = [DateTimeOffset]::Now.ToUnixTimeSeconds()
    $definition = Get-ForecastSlotDefinition -SlotKey $script:SelectedForecastSlotKey
    $slotLabel = Get-ForecastSlotLabel -Definition $definition
    [ordered]@{
        type = 'weather'
        status = 'ok'
        title = $(if ($script:Language -eq 'zh') { T '5L2g5LiA5p2l5bCx5piv5aW95aSp5rCU' } else { 'Weather' })
        mode = $slotLabel
        slot_key = $script:SelectedForecastSlotKey
        selectedForecastSlot = $script:SelectedForecastSlotKey
        source = 'fixture'
        fixture = $true
        fromCache = $false
        from_cache = $false
        city = Get-ProtocolCityLabel
        district = Get-ProtocolDistrictLabel
        location = Get-ProtocolLocationTitle
        locationLabel = Get-ProtocolLocationTitle
        location_key = Get-ProtocolLocationKey
        locationKey = Get-ProtocolLocationKey
        temp = 23.4
        feels_like = 25.1
        condition = 'Cloudy'
        weather_code = 3
        rain = 0.2
        today_rain = 1.6
        rain_probability = 72
        humidity = 81
        cloud = 88
        pressure = 1006
        wind = 9
        gust = 18
        is_day = 1
        near_term = 'Fixture forecast ready'
        forecastSlots = Get-ForecastSlotsPayload
        current = [ordered]@{
            temp = 23.4
            feels_like = 25.1
            condition = 'Cloudy'
            weather_code = 3
            rain = 0.2
            humidity = 81
            cloud = 88
            pressure = 1006
            wind = 9
            gust = 18
            is_day = 1
        }
        metrics = [ordered]@{
            feels_like = 25.1
            rain = 0.2
            today_rain = 1.6
            rain_probability = 72
            humidity = 81
            cloud = 88
            pressure = 1006
            wind = 9
            gust = 18
        }
        updated = ('Fixture {0}' -f (Get-Date -Format 'HH:mm:ss'))
        timestamp = $timestamp
    }
    Write-WorkerStartupTrace 'After first fixture weather payload build'
}
function Get-WeatherSnapshot {
    $locationKey = $(if ($script:FixtureWeatherSuccess) { Get-ProtocolLocationKey } else { Get-SelectedLocationKey })
    if ($script:FixtureWeatherSuccess) {
        $snapshot = New-FixtureWeatherSnapshot
        Save-WeatherSnapshotCache -LocationKey $locationKey -Snapshot $snapshot -Source 'fixture'
        Save-PersistentWeatherSnapshot -Snapshot $snapshot
        $script:LatestWeatherSnapshot = $snapshot
        $script:LatestWeatherLocationKey = $locationKey
        return $snapshot
    }
    $urls = Get-WeatherUrls
    $openMeteoError = ''
    try {
        $json = Invoke-WeatherHttpGet -Uri ($urls.OpenMeteo + '&_=' + [DateTimeOffset]::Now.ToUnixTimeSeconds())
        $weather = $json | ConvertFrom-Json
        $snapshot = ConvertTo-OpenMeteoSnapshot -Weather $weather -FromCache:$false
        Save-WeatherSnapshotCache -LocationKey $locationKey -Snapshot $snapshot -RawWeather $weather -Source 'Open-Meteo'
        Save-PersistentWeatherSnapshot -Snapshot $snapshot
        $script:LatestRawWeather = $weather
        $script:LatestWeatherSnapshot = $snapshot
        $script:LatestWeatherLocationKey = $locationKey
        return $snapshot
    } catch {
        if ($_.Exception.Message -like 'LOCATION_DATA_FAIL:*') { throw }
        $openMeteoError = $_.Exception.Message
        [Console]::Error.WriteLine('[Worker] Open-Meteo failed: ' + $_.Exception.Message)
    }

    try {
        $json = Invoke-WeatherHttpGet -Uri ($urls.Wttr + '&_=' + [DateTimeOffset]::Now.ToUnixTimeSeconds())
        $weather = $json | ConvertFrom-Json
        $snapshot = ConvertTo-WttrSnapshot -Weather $weather
        Save-WeatherSnapshotCache -LocationKey $locationKey -Snapshot $snapshot -RawWeather $weather -Source 'wttr.in'
        Save-PersistentWeatherSnapshot -Snapshot $snapshot
        $script:LatestRawWeather = $weather
        $script:LatestWeatherSnapshot = $snapshot
        $script:LatestWeatherLocationKey = $locationKey
        return $snapshot
    } catch {
        [Console]::Error.WriteLine('[Worker] wttr failed: ' + $_.Exception.Message)
        $cache = Get-CachedWeatherSnapshot -LocationKey $locationKey
        if ($null -ne $cache) {
            $script:LatestWeatherSnapshot = $cache
            $script:LatestWeatherLocationKey = $locationKey
            return $cache
        }
        throw ('{0}; wttr fallback failed: {1}' -f $openMeteoError, $_.Exception.Message)
    }
}
function Process-WorkerCommand {
    param([object]$Command)

    if ($null -eq $Command) { return }
    Remove-StaleProcessedCommands

    $id = [string](Get-PropertyValue -Object $Command -Name 'id' -Default '')
    $type = [string](Get-PropertyValue -Object $Command -Name 'type' -Default '')
    $commandSessionId = [string](Get-PropertyValue -Object $Command -Name 'sessionId' -Default '')
    $payload = Get-PropertyValue -Object $Command -Name 'payload' -Default ([pscustomobject]@{})

    if (-not [string]::IsNullOrWhiteSpace($script:SessionId) -and $commandSessionId -ne $script:SessionId) {
        return
    }
    if ([string]::IsNullOrWhiteSpace($id)) {
        Write-CommandError -Id '' -CommandType $type -Code 'missing_command_id' -Message 'Command id is required'
        return
    }
    if ($script:ProcessedCommandIds.ContainsKey($id)) {
        return
    }
    $script:ProcessedCommandIds[$id] = Get-Date
    if ([string]::IsNullOrWhiteSpace($type)) {
        Write-CommandError -Id $id -CommandType '' -Code 'missing_command_type' -Message 'Command type is required'
        return
    }

    try {
        switch ($type) {
            'getSettings' {
                Write-CommandAck -Id $id -CommandType $type
                Write-SettingsEvent -Id $id
            }
            'getRegionCatalog' {
                Write-CommandAck -Id $id -CommandType $type
                Write-CatalogEvent -Id $id
            }
            'getSnapshotDiagnostics' {
                Write-CommandAck -Id $id -CommandType $type
                Write-ProtocolEvent -Type 'snapshotDiagnostics' -Id $id -Payload (Get-PersistentSnapshotDiagnosticsPayload)
            }
            'setLocation' {
                Set-SelectedLocation `
                    -ProvinceKey ([string](Get-PropertyValue -Object $payload -Name 'provinceKey')) `
                    -CityKey ([string](Get-PropertyValue -Object $payload -Name 'cityKey')) `
                    -DistrictKey ([string](Get-PropertyValue -Object $payload -Name 'districtKey'))
                Write-SettingsEvent -Id $id
                Invoke-WeatherRefresh -Reason 'setLocation' -CommandId $id -CommandType $type
            }
            'setRefreshInterval' {
                Set-RefreshInterval -Seconds ([int](Get-PropertyValue -Object $payload -Name 'refreshSeconds'))
                Write-CommandAck -Id $id -CommandType $type
                Write-SettingsEvent -Id $id
                Write-ProtocolEvent -Type 'status' -Id $id -Payload ([ordered]@{
                    phase = 'idle'
                    message = 'Refresh interval updated'
                    refreshSeconds = $script:RefreshSeconds
                })
            }
            'setLanguage' {
                Set-WorkerLanguage -Language ([string](Get-PropertyValue -Object $payload -Name 'language'))
                Write-SettingsEvent -Id $id
                Write-CatalogEvent -Id $id
                Invoke-WeatherRefresh -Reason 'setLanguage' -CommandId $id -CommandType $type
            }
            'setForecastSlot' {
                Set-WorkerForecastSlot -SlotKey ([string](Get-PropertyValue -Object $payload -Name 'slotKey'))
                Write-SettingsEvent -Id $id
                $locationKey = $(if ($script:FixtureWeatherSuccess) { Get-ProtocolLocationKey } else { Get-SelectedLocationKey })
                if ($script:RawWeatherCache.ContainsKey($locationKey) -and $script:RawWeatherCache[$locationKey].Source -eq 'Open-Meteo') {
                    $request = Start-WeatherRequestContext
                    Write-CommandAck -Id $id -CommandType $type -RequestId $request.RequestId
                    $rawEntry = $script:RawWeatherCache[$locationKey]
                    $snapshot = ConvertTo-OpenMeteoSnapshot -Weather $rawEntry.Weather -FromCache:$false
                    Save-WeatherSnapshotCache -LocationKey $locationKey -Snapshot $snapshot -RawWeather $rawEntry.Weather -Source $rawEntry.Source
                    Save-PersistentWeatherSnapshot -Snapshot $snapshot
                    if (Test-WeatherRequestIsCurrent -Request $request) {
                        Write-ProtocolEvent -Type 'weather' -Id $id -RequestId $request.RequestId -Payload $snapshot
                        Write-ProtocolEvent -Type 'status' -Id $id -RequestId $request.RequestId -Payload ([ordered]@{
                            phase = 'idle'
                            message = 'Forecast slot updated'
                            locationKey = $locationKey
                        })
                    }
                } else {
                    Invoke-WeatherRefresh -Reason 'setForecastSlot' -CommandId $id -CommandType $type
                }
            }
            'manualRefresh' {
                Invoke-WeatherRefresh -Reason 'manualRefresh' -CommandId $id -CommandType $type
            }
            'status' {
                Write-CommandAck -Id $id -CommandType $type
                Write-ProtocolEvent -Type 'status' -Id $id -Payload ([ordered]@{
                    phase = $(if ($script:WeatherRefreshInProgress) { 'refreshing' } else { 'idle' })
                    message = 'Worker running'
                    locationKey = Get-ProtocolLocationKey
                    requestId = $script:ActiveWeatherRequestId
                })
            }
            default {
                Write-CommandError -Id $id -CommandType $type -Code 'unknown_command' -Message ('Unknown command: {0}' -f $type)
            }
        }
    } catch {
        Write-CommandError -Id $id -CommandType $type -Code 'command_failed' -Message $_.Exception.Message
    }
}
function Initialize-CommandFile {
    if ([string]::IsNullOrWhiteSpace($script:CommandFile)) { return }
    $full = [IO.Path]::GetFullPath($script:CommandFile)
    $script:CommandFile = $full
    $directory = Split-Path -Parent $full
    if (-not [string]::IsNullOrWhiteSpace($directory)) { New-Item -ItemType Directory -Path $directory -Force | Out-Null }
    if (-not (Test-Path -LiteralPath $full -PathType Leaf)) {
        New-Item -ItemType File -Path $full -Force | Out-Null
        $script:CommandFilePosition = 0L
    } else {
        $script:CommandFilePosition = (Get-Item -LiteralPath $full).Length
    }
}

function Read-NewCommandLines {
    if ([string]::IsNullOrWhiteSpace($script:CommandFile)) { return @() }
    if (-not (Test-Path -LiteralPath $script:CommandFile -PathType Leaf)) { return @() }
    $fs = $null
    $reader = $null
    try {
        $fs = [IO.File]::Open($script:CommandFile, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
        if ($script:CommandFilePosition -gt $fs.Length) { $script:CommandFilePosition = 0L }
        [void]$fs.Seek($script:CommandFilePosition, [IO.SeekOrigin]::Begin)
        $reader = New-Object IO.StreamReader($fs, [Text.Encoding]::UTF8)
        $text = $reader.ReadToEnd()
        $script:CommandFilePosition = $fs.Position
        if ([string]::IsNullOrWhiteSpace($text)) { return @() }
        return $text -split "`r?`n" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    } catch {
        [Console]::Error.WriteLine('[Worker] command read failed: ' + $_.Exception.Message)
        return @()
    } finally {
        if ($null -ne $reader) { $reader.Dispose() } elseif ($null -ne $fs) { $fs.Dispose() }
    }
}

function Process-NewCommandLines {
    foreach ($line in Read-NewCommandLines) {
        try {
            $command = $line | ConvertFrom-Json
            Process-WorkerCommand -Command $command
        } catch {
            Write-CommandError -Id '' -CommandType '' -Code 'malformed_json' -Message $_.Exception.Message
        }
    }
}

function Invoke-IpcSmoke {
    Write-ProtocolEvent -Type 'status' -Payload ([ordered]@{ phase = 'ready'; message = 'Worker ready'; sessionId = $script:SessionId })
    if ($script:FixtureWeatherSuccess) {
        Process-WorkerCommand -Command ([pscustomobject]@{ protocol = 1; id = 'smoke-manualRefresh'; sessionId = $script:SessionId; type = 'manualRefresh'; payload = [pscustomobject]@{} })
    }
    Process-WorkerCommand -Command ([pscustomobject]@{ protocol = 1; id = 'smoke-settings'; sessionId = $script:SessionId; type = 'getSettings'; payload = [pscustomobject]@{} })
    Process-WorkerCommand -Command ([pscustomobject]@{ protocol = 1; id = 'smoke-catalog'; sessionId = $script:SessionId; type = 'getRegionCatalog'; payload = [pscustomobject]@{} })
    Process-WorkerCommand -Command ([pscustomobject]@{ protocol = 1; id = 'smoke-snapshotDiagnostics'; sessionId = $script:SessionId; type = 'getSnapshotDiagnostics'; payload = [pscustomobject]@{} })
    Process-WorkerCommand -Command ([pscustomobject]@{ protocol = 1; id = 'smoke-status'; sessionId = $script:SessionId; type = 'status'; payload = [pscustomobject]@{} })
}
if ($script:FixtureWeatherSuccess) {
    Write-WorkerStartupTrace 'Before legacy catalog extraction/import'
    Import-LocationCatalog -SkipLegacyExtraction
    Write-WorkerStartupTrace 'After legacy catalog extraction/import skipped fixture fast path'
} else {
    Write-WorkerStartupTrace 'Before legacy catalog extraction/import'
    Import-LocationCatalog
    Write-WorkerStartupTrace 'After legacy catalog extraction/import'
}
Write-WorkerStartupTrace 'Before settings load'
Load-Settings
Write-WorkerStartupTrace 'After settings load'

if ($IpcSmoke) {
    $script:IpcModeActive = $true
    Invoke-IpcSmoke
    return
}

if ($Once) {
    $script:IpcModeActive = $false
    try {
        $onceSnapshot = Get-WeatherSnapshot
        Write-JsonLine (ConvertTo-LegacyWeatherEvent -Snapshot $onceSnapshot)
        if ($script:FixtureWeatherSuccess) { Write-WorkerStartupTrace 'First stdout weather event emitted' }
    } catch {
        Write-WorkerError -Message $_.Exception.Message
    }
    return
}

if ($script:IpcModeActive) {
    Initialize-CommandFile
    Write-WorkerStartupTrace 'Before initial ready/status event'
    Write-ProtocolEvent -Type 'status' -Payload ([ordered]@{ phase = 'ready'; message = 'Worker ready'; sessionId = $script:SessionId })
    Write-WorkerStartupTrace 'After initial ready/status event'
    if ($script:FixtureWeatherSuccess) {
        Invoke-WeatherRefresh -Reason 'startup'
        Write-SettingsEvent
    } else {
        Write-SettingsEvent
        Invoke-WeatherRefresh -Reason 'startup'
    }

    Write-WorkerStartupTrace 'IPC loop entered'
    while ($true) {
        Process-NewCommandLines
        if ((Get-Date) -ge $script:NextAutoRefreshAt) {
            Invoke-WeatherRefresh -Reason 'auto'
            $script:NextAutoRefreshAt = (Get-Date).AddSeconds($script:RefreshSeconds)
        }
        Start-Sleep -Milliseconds 250
    }
}

do {
    Invoke-WeatherRefresh -Reason 'poll'
    Start-Sleep -Seconds $script:PollSeconds
} while ($true)





