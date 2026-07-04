$repoRoot = if ($PSScriptRoot) { $PSScriptRoot } else { (Get-Location).Path }
$scriptPath = Join-Path $repoRoot 'LonghuaWeatherWidget.ps1'
$scriptText = Get-Content -LiteralPath $scriptPath -Raw

function Get-WeatherFieldBlock {
    param([string]$VariableName)

    $match = [regex]::Match(
        $scriptText,
        "\`$script:$VariableName\s*=\s*@\((?<body>.*?)\)\s*-join",
        [System.Text.RegularExpressions.RegexOptions]::Singleline
    )
    if (-not $match.Success) {
        throw "Could not find field block: $VariableName"
    }
    return $match.Groups['body'].Value
}

Describe 'Open-Meteo field contracts' {
    It 'requests the required current fields' {
        $currentFieldsBlock = Get-WeatherFieldBlock -VariableName 'CurrentFields'
        foreach ($field in @(
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
        )) {
            $currentFieldsBlock | Should Match ([regex]::Escape("'$field'"))
        }
    }

    It 'requests the required hourly fields' {
        $hourlyFieldsBlock = Get-WeatherFieldBlock -VariableName 'HourlyFields'
        foreach ($field in @(
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
        )) {
            $hourlyFieldsBlock | Should Match ([regex]::Escape("'$field'"))
        }
    }

    It 'requests the required daily fields' {
        $dailyFieldsBlock = Get-WeatherFieldBlock -VariableName 'DailyFields'
        foreach ($field in @(
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
        )) {
            $dailyFieldsBlock | Should Match ([regex]::Escape("'$field'"))
        }
    }

    It 'uses the required Open-Meteo forecast window and units' {
        $scriptText | Should Match 'timezone=auto'
        $scriptText | Should Match 'ForecastDayCount = 14'
        $scriptText | Should Match 'ForecastHourCount = 336'
        $scriptText | Should Match 'temperature_unit=celsius'
        $scriptText | Should Match 'wind_speed_unit=kmh'
        $scriptText | Should Match 'precipitation_unit=mm'
    }
}

Describe 'Thin CLR launcher contracts' {
    $launcherRoot = Join-Path $repoRoot 'src\launcher'
    $workerPath = Join-Path $repoRoot 'src\worker\WeatherWorker.ps1'

    It 'keeps the legacy PowerShell fallback entrypoint' {
        Test-Path -LiteralPath (Join-Path $repoRoot 'LonghuaWeatherWidget.ps1') | Should Be $true
    }

    It 'adds the launcher and worker source layout' {
        foreach ($relativePath in @(
            'src\launcher\WeatherLauncher.cs',
            'src\launcher\App.xaml',
            'src\launcher\App.xaml.cs',
            'src\launcher\MainWindow.xaml',
            'src\launcher\MainWindow.xaml.cs',
            'src\launcher\build-launcher.ps1',
            'src\worker\WeatherWorker.ps1'
        )) {
            Test-Path -LiteralPath (Join-Path $repoRoot $relativePath) | Should Be $true
        }
    }

    It 'supports single exe embedded worker packaging' {
        Test-Path -LiteralPath (Join-Path $repoRoot 'build-single-exe-release.ps1') | Should Be $true
        Test-Path -LiteralPath (Join-Path $repoRoot 'VERSION') | Should Be $true
        $launcherText = Get-Content -LiteralPath (Join-Path $launcherRoot 'WeatherLauncher.cs') -Raw
        $buildText = Get-Content -LiteralPath (Join-Path $launcherRoot 'build-launcher.ps1') -Raw
        $singleBuildText = Get-Content -LiteralPath (Join-Path $repoRoot 'build-single-exe-release.ps1') -Raw
        $thinReleaseText = Get-Content -LiteralPath (Join-Path $repoRoot 'build-thinclr-release.ps1') -Raw
        foreach ($token in @(
            'internal static class BundledRuntime',
            'WeatherLauncher.Resources.WeatherWorker.ps1',
            'WeatherLauncher.Resources.ChinaRegionCatalog.json',
            'TryGetWorkerScript',
            'TryGetCatalogPath',
            'Path.Combine(localAppData, "PaperWeatherWidget", "runtime", GetRuntimeVersion())'
        )) {
            $launcherText | Should Match ([regex]::Escape($token))
        }
        foreach ($token in @(
            '[switch]$SingleExe',
            '/resource:$workerResource,WeatherLauncher.Resources.WeatherWorker.ps1',
            '/resource:$catalogResource,WeatherLauncher.Resources.ChinaRegionCatalog.json',
            'if (-not $SingleExe)'
        )) {
            $buildText | Should Match ([regex]::Escape($token))
        }
        $singleBuildText | Should Match ([regex]::Escape('PaperWeatherWidget-v$Version-win-x64.exe'))
        $singleBuildText | Should Match ([regex]::Escape('-SingleExe'))
        $singleBuildText | Should Match ([regex]::Escape('Resolve-BuildVersion'))
        $buildText | Should Match ([regex]::Escape('Resolve-BuildVersion'))
        $buildText | Should Match ([regex]::Escape('Assert-NoForbiddenOutputFiles'))
        $thinReleaseText | Should Match ([regex]::Escape('Assert-NoForbiddenPackageFiles'))
    }
    It 'keeps WPF out of the PowerShell worker' {
        $workerText = Get-Content -LiteralPath $workerPath -Raw
        $workerText | Should Not Match 'PresentationFramework'
        $workerText | Should Not Match 'PresentationCore'
        $workerText | Should Not Match 'WindowsBase'
        $workerText | Should Not Match 'System\.Windows'
        $workerText | Should Not Match 'XamlReader'
    }

    It 'uses stdout JSON IPC and Dispatcher UI patching in the launcher' {
        $mainWindowText = Get-Content -LiteralPath (Join-Path $launcherRoot 'MainWindow.xaml.cs') -Raw
        $mainWindowText | Should Match 'RedirectStandardOutput = true'
        $mainWindowText | Should Match 'UseShellExecute = false'
        $mainWindowText | Should Match 'CreateNoWindow = true'
        $mainWindowText | Should Match 'Dispatcher\.BeginInvoke'
        $mainWindowText | Should Match 'ApplyWorkerPayload'
    }

    It 'pins launcher paths to the app or user data roots and escapes worker arguments' {
        $launcherText = Get-Content -LiteralPath (Join-Path $launcherRoot 'WeatherLauncher.cs') -Raw
        $mainWindowText = Get-Content -LiteralPath (Join-Path $launcherRoot 'MainWindow.xaml.cs') -Raw
        $workerText = Get-Content -LiteralPath $workerPath -Raw
        $launcherText | Should Not Match 'WalkForRepoRoot'
        $launcherText | Should Not Match ([regex]::Escape('LonghuaWeatherWidget.ps1"))'))
        $launcherText | Should Match ([regex]::Escape('GetLocalAppDataRoot()'))
        $mainWindowText | Should Match ([regex]::Escape('JoinCommandLineArguments(arguments)'))
        $mainWindowText | Should Match ([regex]::Escape('QuoteCommandLineArgument'))
        $mainWindowText | Should Match ([regex]::Escape('"-ParentProcessId"'))
        $workerText | Should Not Match 'Invoke-Expression'
        $workerText | Should Not Match 'LegacyScriptPath'
        $workerText | Should Match ([regex]::Escape('Get-WorkerSettingsPath'))
        $workerText | Should Match ([regex]::Escape('Test-IpcOwnerAlive'))
    }

    It 'writes startup benchmark milestones' {
        $launcherText = Get-Content -LiteralPath (Join-Path $launcherRoot 'WeatherLauncher.cs') -Raw
        $launcherText | Should Match '\[Launcher\] window shown:'
        $launcherText | Should Match '\[Worker\] process started:'
        $launcherText | Should Match '\[Worker\] first data:'
    }
}

Describe 'Thin CLR launcher P0 parity contracts' {
    $launcherRoot = Join-Path $repoRoot 'src\launcher'
    $workerPath = Join-Path $repoRoot 'src\worker\WeatherWorker.ps1'

    It 'supports IPC v1 commands in the PowerShell worker' {
        $workerText = Get-Content -LiteralPath $workerPath -Raw
        foreach ($token in @(
            'protocol = 1',
            'CommandFile',
            'SessionId',
            'IpcMode',
            'getSettings',
            'getRegionCatalog',
            'setLocation',
            'setRefreshInterval',
            'setLanguage',
            'setForecastSlot',
            'manualRefresh',
            'status'
        )) {
            $workerText | Should Match ([regex]::Escape($token))
        }
    }

    It 'exposes fourteen day and hourly forecast slot options' {
        $workerText = Get-Content -LiteralPath $workerPath -Raw
        $mainWindowText = Get-Content -LiteralPath (Join-Path $launcherRoot 'MainWindow.xaml.cs') -Raw
        foreach ($token in @(
            "`$script:SelectedForecastSlotKey = 'Day0'",
            "`$script:ForecastDayCount = 14",
            "`$script:ForecastHourCount = 336",
            "for (`$day = 0; `$day -lt 14; `$day++)",
            "Key = ('Day{0}' -f `$day)",
            "Key = ('Hour+{0}h' -f `$hour)",
            'Normalize-ForecastSlotKey',
            "'Now' { return 'Day0' }"
        )) {
            $workerText | Should Match ([regex]::Escape($token))
        }
        foreach ($token in @(
            'PopulateForecastSlots(DefaultForecastSlots(), "Day0")',
            'for (var day = 0; day < 14; day++)',
            'ForecastDayLabel(day)',
            '"Hour+" + hour.ToString(CultureInfo.InvariantCulture) + "h"',
            'NormalizeForecastSlotKey(selectedKey)',
            'case "Now": return "Day0";'
        )) {
            $mainWindowText | Should Match ([regex]::Escape($token))
        }
    }
    It 'keeps worker cache and stale request guards location-scoped' {
        $workerText = Get-Content -LiteralPath $workerPath -Raw
        foreach ($token in @(
            'WeatherSnapshotCache',
            'RawWeatherCache',
            'Get-CachedWeatherSnapshot',
            'fromCache',
            'WeatherRequestSequence',
            'ActiveWeatherRequestId',
            'ActiveWeatherRequestLocationKey',
            'Test-WeatherRequestIsCurrent',
            'SettingsVersion',
            'Set-SelectedLocation'
        )) {
            $workerText | Should Match ([regex]::Escape($token))
        }
    }

    It 'adds real settings controls and command writer to the C# launcher' {
        $mainWindowText = Get-Content -LiteralPath (Join-Path $launcherRoot 'MainWindow.xaml.cs') -Raw
        foreach ($token in @(
            'BuildSettingsPanel',
            'settingsButton',
            'provinceCombo',
            'cityCombo',
            'districtCombo',
            'refreshCombo',
            'forecastSlotCombo',
            'zhButton',
            'enButton',
            'SendCommand',
            'getSettings',
            'getRegionCatalog',
            'setLocation',
            'ApplySelectedLocationFromUi',
            'FormatLocationLabel',
            'RenderLocation(FormatLocationLabel(province, city, district))',
            'setRefreshInterval',
            'setLanguage',
            'setForecastSlot',
            'manualRefresh'
        )) {
            $mainWindowText | Should Match ([regex]::Escape($token))
        }
    }

    It 'uses manualRefresh command instead of default worker restart' {
        $mainWindowText = Get-Content -LiteralPath (Join-Path $launcherRoot 'MainWindow.xaml.cs') -Raw
        $mainWindowText | Should Match 'ManualRefresh\(\)'
        $mainWindowText | Should Match 'SendCommand\("manualRefresh"'
        $mainWindowText | Should Not Match 'refreshButton\.Click \+= delegate \{ RestartWorker\(\); \};'
    }

    It 'limits window dragging to explicit title and border handles' {
        $mainWindowText = Get-Content -LiteralPath (Join-Path $launcherRoot 'MainWindow.xaml.cs') -Raw
        $mainWindowText | Should Match 'OnDragHandleMouseDown'
        $mainWindowText | Should Match 'rootBorder\.MouseLeftButtonDown \+= OnWindowBorderMouseLeftButtonDown'
        $mainWindowText | Should Match 'BuildDragEdgeZone'
        $mainWindowText | Should Match 'zone\.MouseLeftButtonDown \+= OnDragHandleMouseDown'
        $mainWindowText | Should Match 'IsDragExcludedSource\(source\)'
    }
}

Describe 'Thin CLR launcher P1 visible parity contracts' {
    $launcherRoot = Join-Path $repoRoot 'src\launcher'

    It 'uses semantic top controls instead of bare preview buttons' {
        $mainWindowText = Get-Content -LiteralPath (Join-Path $launcherRoot 'MainWindow.xaml.cs') -Raw
        $mainWindowText | Should Not Match ([regex]::Escape('ChromeButton("R",'))
        $mainWindowText | Should Not Match ([regex]::Escape('ChromeButton("S",'))
        $mainWindowText | Should Not Match ([regex]::Escape('ChromeButton("x",'))
        foreach ($token in @(
            'RefreshNowCard',
            'SettingsButton',
            'CloseButton',
            '((char)0x2699)',
            '((char)0x00D7)'
        )) {
            $mainWindowText | Should Match ([regex]::Escape($token))
        }
        $chromeButtonBlock = [regex]::Match($mainWindowText, '(?s)private Button ChromeButton.*?private static void SetAutomationId').Value
        $chromeButtonBlock | Should Match ([regex]::Escape('button.BorderBrush = Brushes.Transparent;'))
        $chromeButtonBlock | Should Match ([regex]::Escape('button.BorderThickness = new Thickness(0);'))
        $mainWindowText | Should Match ([regex]::Escape('ChromeButtonSize = 32'))
        $chromeButtonBlock | Should Match ([regex]::Escape('button.MinHeight = ChromeButtonSize;'))
        $chromeButtonBlock | Should Match ([regex]::Escape('button.FocusVisualStyle = null;'))
        $chromeButtonBlock | Should Match ([regex]::Escape('button.Template = ChromeButtonTemplate();'))
        $chromeButtonBlock | Should Match ([regex]::Escape('private static ControlTemplate ChromeButtonTemplate()'))
        $chromeButtonBlock | Should Not Match ([regex]::Escape('button.BorderThickness = new Thickness(1);'))
        $mainWindowText | Should Match 'OnProvinceSelectionChanged[\s\S]*?PopulateCityCombo\(item\);[\s\S]*?ApplySelectedLocationFromUi\(\);'
        $mainWindowText | Should Match 'OnCitySelectionChanged[\s\S]*?PopulateDistrictCombo\(item\);[\s\S]*?ApplySelectedLocationFromUi\(\);'
        $mainWindowText | Should Match 'OnDistrictSelectionChanged[\s\S]*?ApplySelectedLocationFromUi\(\);'
    }

    It 'opens settings from the location strip without making the whole window draggable' {
        $mainWindowText = Get-Content -LiteralPath (Join-Path $launcherRoot 'MainWindow.xaml.cs') -Raw
        $mainWindowText | Should Match ([regex]::Escape('LocationTitle'))
        $mainWindowText | Should Match ([regex]::Escape('statusShell.MouseLeftButtonUp += delegate { ToggleSettingsPanel(); };'))
        $mainWindowText | Should Match 'IsDragExcludedSource\(source\)'
    }


    It 'renders the credit link as bare text without a card container' {
        $mainWindowText = Get-Content -LiteralPath (Join-Path $launcherRoot 'MainWindow.xaml.cs') -Raw
        $creditButtonBlock = [regex]::Match($mainWindowText, '(?s)private Button BuildCreditButton\(\).*?private void UpdateCreditButtonContent').Value
        $creditButtonBlock | Should Match ([regex]::Escape('SetAutomationId(button, "CreditLinkButton")'))
        $creditButtonBlock | Should Match ([regex]::Escape('button.Height = 24;'))
        $creditButtonBlock | Should Match ([regex]::Escape('button.Background = Brushes.Transparent;'))
        $creditButtonBlock | Should Match ([regex]::Escape('button.BorderBrush = Brushes.Transparent;'))
        $creditButtonBlock | Should Match ([regex]::Escape('button.BorderThickness = new Thickness(0);'))
        $creditButtonBlock | Should Not Match ([regex]::Escape('button.Background = BrushFrom(ColorSurface);'))
        $creditButtonBlock | Should Not Match ([regex]::Escape('button.BorderThickness = new Thickness(1);'))
        $mainWindowText | Should Match ([regex]::Escape('case "CreditPrefix"'))
        $mainWindowText | Should Match ([regex]::Escape('((char)0x2197)'))
        $mainWindowText | Should Match ([regex]::Escape('https://github.com/M47E1/paper-weather-widget'))
    }
    It 'restores drawer collapse and handle behavior in the C# host' {
        $mainWindowText = Get-Content -LiteralPath (Join-Path $launcherRoot 'MainWindow.xaml.cs') -Raw
        foreach ($token in @(
            'BuildDrawerHandle',
            'DrawerHandle',
            'CollapseDrawer',
            'ExpandDrawer',
            'drawerExpanded',
            'drawerEdge',
            'CollapsedWidth',
            'CollapsedHeight',
            'Content = BuildDrawerHandle',
            'Content = rootBorder',
            'state["drawerExpanded"]',
            'state["drawerEdge"]'
        )) {
            $mainWindowText | Should Match ([regex]::Escape($token))
        }
    }

    It 'restores the minimal context menu and denser settings combo style' {
        $mainWindowText = Get-Content -LiteralPath (Join-Path $launcherRoot 'MainWindow.xaml.cs') -Raw
        foreach ($token in @(
            'BuildWindowContextMenu',
            'rootBorder.ContextMenu = BuildWindowContextMenu()',
            'MenuItem { Header = Ui("RefreshNow") }',
            'refreshItem.Click += delegate { ManualRefresh(); };',
            'closeItem.Click += delegate { Close(); };',
            'SettingsComboItemStyle',
            'SettingsComboTemplate',
            'SettingsComboItemTemplate',
            'combo.Height = 32',
            'combo.Template = SettingsComboTemplate()',
            'new Binding("Text")',
            'SetComboSelection',
            'SyncComboText',
            'combo.ItemContainerStyle = SettingsComboItemStyle()',
            'Control.MinHeightProperty, 31.0',
            'Control.PaddingProperty, new Thickness(SpaceMd - 1, SpaceSm - 2, SpaceMd - 1, SpaceSm - 2)'
        )) {
            $mainWindowText | Should Match ([regex]::Escape($token))
        }
    }
    It 'refresh interval changes apply settings and request fresh weather immediately' {
        $mainWindowText = Get-Content -LiteralPath (Join-Path $launcherRoot 'MainWindow.xaml.cs') -Raw
        $handler = [regex]::Match($mainWindowText, 'private void OnRefreshSelectionChanged[\s\S]*?\r?\n        \}').Value
        $handler | Should Match 'SendCommand\("setRefreshInterval"'
        $handler | Should Match 'SetRefreshingState\(\);\s*SendCommand\("manualRefresh"'
        $handler | Should Not Match 'RestartWorker\(\)'
    }
}


Describe 'Thin CLR launcher UI design system consolidation contracts' {
    $launcherRoot = Join-Path $repoRoot 'src\launcher'

    It 'centralizes weather visual grammar and display renderers in the launcher' {
        $mainWindowText = Get-Content -LiteralPath (Join-Path $launcherRoot 'MainWindow.xaml.cs') -Raw
        foreach ($token in @(
            'ConditionVisual',
            'ResolveConditionVisual',
            'ConditionLabel',
            'UiStateLabel',
            'RenderStatus',
            'RenderCondition',
            'RenderMetrics',
            'RenderMetricPlaceholders',
            'ApplyConditionVisual',
            'StateBrush'
        )) {
            $mainWindowText | Should Match ([regex]::Escape($token))
        }
    }

    It 'uses a bounded density token set and a single ComboBox wrapper' {
        $mainWindowText = Get-Content -LiteralPath (Join-Path $launcherRoot 'MainWindow.xaml.cs') -Raw
        foreach ($token in @(
            'SpaceXs = 4',
            'SpaceSm = 8',
            'SpaceMd = 12',
            'SpaceLg = 16',
            'RadiusSm = 8',
            'RadiusLg = 12',
            'RadiusXl = 14',
            'SettingsCombo()',
            'SettingsComboItemStyle()',
            'SettingsComboTemplate()',
            'SettingsComboItemTemplate()',
            'combo.Template = SettingsComboTemplate()',
            'combo.ItemContainerStyle = SettingsComboItemStyle()'
        )) {
            $mainWindowText | Should Match ([regex]::Escape($token))
        }
        $mainWindowText | Should Match ([regex]::Escape('public sealed class ComboItem'))
        $mainWindowText | Should Match ([regex]::Escape('combo.DisplayMemberPath = "Text";'))
        $mainWindowText | Should Match ([regex]::Escape('public override string ToString()'))
    }

    It 'removes development-state copy from visible launcher rendering' {
        $mainWindowText = Get-Content -LiteralPath (Join-Path $launcherRoot 'MainWindow.xaml.cs') -Raw
        foreach ($forbidden in @(
            'Fixture forecast ready',
            'Weather service unavailable',
            'Near-term forecast unavailable',
            'Fetching weather',
            'Fetching near-term forecast',
            'WeatherWorker.ps1 was not found.',
            'Weather worker exited.'
        )) {
            $mainWindowText | Should Not Match ([regex]::Escape($forbidden))
        }
        $mainWindowText | Should Match ([regex]::Escape('DisplayTextOrState'))
        $mainWindowText | Should Match ([regex]::Escape('IsDevelopmentWeatherText'))
    }
}
Describe 'Thin CLR launcher UI grammar finalization contracts' {
    $launcherRoot = Join-Path $repoRoot 'src\launcher'

    It 'routes weather payloads through a renderer-only model' {
        $mainWindowText = Get-Content -LiteralPath (Join-Path $launcherRoot 'MainWindow.xaml.cs') -Raw
        foreach ($token in @(
            'WeatherRenderModel',
            'ConditionRenderModel',
            'WeatherMetrics',
            'CreateWeatherModel',
            'RenderWeather(payload, false, false, null)',
            'RenderWeather(copy, true, diagnostics.Stale, diagnostics.SavedAtUtc)',
            'RenderWeatherState',
            'RenderUiLabel'
        )) {
            $mainWindowText | Should Match ([regex]::Escape($token))
        }
        $mainWindowText | Should Not Match ([regex]::Escape('ApplyWeatherPayload'))

        $modelBlock = [regex]::Match($mainWindowText, '(?s)private WeatherRenderModel CreateWeatherModel.*?private void RenderWeather').Value
        $modelBlock.Length | Should BeGreaterThan 0
        foreach ($token in @(
            'GetString(payload, "title")',
            'GetString(payload, "location")',
            'GetString(payload, "updated")',
            'GetString(payload, "near_term")',
            'GetInt(payload, "weather_code")',
            'GetInt(payload, "is_day")',
            'GetDouble(payload, "temp")',
            'GetDouble(payload, "feels_like")',
            'GetDouble(payload, "rain")',
            'GetDouble(payload, "rain_probability")',
            'GetDouble(payload, "humidity")',
            'GetDouble(payload, "cloud")',
            'GetDouble(payload, "pressure")',
            'GetDouble(payload, "wind")',
            'GetDouble(payload, "gust")'
        )) {
            $modelBlock | Should Match ([regex]::Escape($token))
        }
        $outsideModel = $mainWindowText.Replace($modelBlock, '')
        $outsideModel | Should Not Match 'Get(String|Double|Int)\(payload, "(title|location|updated|near_term|weather_code|is_day|temp|feels_like|rain|today_rain|rain_probability|humidity|cloud|pressure|wind|gust)"\)'
    }

    It 'keeps visible weather control text assignments inside renderer functions' {
        $mainWindowText = Get-Content -LiteralPath (Join-Path $launcherRoot 'MainWindow.xaml.cs') -Raw
        $rendererBlock = [regex]::Match($mainWindowText, '(?s)private void RenderStatus.*?private TextBlock Text').Value
        $rendererBlock.Length | Should BeGreaterThan 0
        foreach ($token in @(
            'statusBlock.Text = UiStateLabel(stateKey)',
            'titleBlock.Text = DisplayTextOrState',
            'locationBlock.Text = DisplayTextOrState',
            'updatedBlock.Text = DisplayTextOrState',
            'conditionBlock.Text = ConditionLabel(visual.Key)',
            'nearTermBlock.Text = DisplayTextOrState',
            'RenderMetric(temperatureBlock',
            'RenderMetric(feelsBlock'
        )) {
            $rendererBlock | Should Match ([regex]::Escape($token))
        }
        $outsideRenderer = $mainWindowText.Replace($rendererBlock, '')
        $outsideRenderer | Should Not Match '(statusBlock|titleBlock|locationBlock|conditionBlock|nearTermBlock|temperatureBlock|feelsBlock|updatedBlock|errorBlock)\.Text\s*='
        $mainWindowText | Should Not Match 'Text\("Weather"'
        $mainWindowText | Should Not Match 'Text\("Shenzhen - Longhua"'
        $mainWindowText | Should Not Match 'Text\("--"'
    }

    It 'keeps weather and state strings inside grammar layers only' {
        $mainWindowText = Get-Content -LiteralPath (Join-Path $launcherRoot 'MainWindow.xaml.cs') -Raw
        foreach ($forbidden in @(
            'Fixture forecast ready',
            'Weather service unavailable',
            'Near-term forecast unavailable',
            'Fetching weather',
            'Fetching near-term forecast',
            'WeatherWorker.ps1 was not found.',
            'Weather worker exited.',
            'Refresh failed, showing cached',
            'Cached, refreshing',
            'Updating...'
        )) {
            $mainWindowText | Should Not Match ([regex]::Escape($forbidden))
        }
        $mainWindowText | Should Match ([regex]::Escape('case "cloudy": return zh ? "\u591a\u4e91" : "Cloudy";'))
        $mainWindowText | Should Match ([regex]::Escape('default: return ConditionLabel("cloudy");'))
        $mainWindowText | Should Match ([regex]::Escape('default: return String.Empty;'))
        $mainWindowText | Should Match ([regex]::Escape('case "Updating": return UiStateLabel("refreshing");'))
        $mainWindowText | Should Not Match '\.Text\s*=\s*"(cached|failed|refreshing|stale|live|Cloudy|Rain|Clear|Night|Thunderstorm)"'
    }

    It 'documents grammar consistency and finalization reports' {
        foreach ($relativePath in @(
            'reports\ui-grammar-consistency.md',
            'reports\ui-grammar-consistency.json',
            'reports\ui-grammar-finalization.md',
            'reports\ui-grammar-finalization.json'
        )) {
            Test-Path -LiteralPath (Join-Path $repoRoot $relativePath) | Should Be $true
        }
        $finalReport = Get-Content -LiteralPath (Join-Path $repoRoot 'reports\ui-grammar-finalization.md') -Raw
        foreach ($token in @(
            'raw condition strings from payload',
            'UiStateLabel',
            'ConditionLabel',
            'RenderMetrics',
            'RenderWeather(payload, fromSnapshot, staleSnapshot, snapshotSavedAt)',
            'IPC, worker architecture, snapshot protocol, region logic, or startup performance logic changed'
        )) {
            $finalReport | Should Match ([regex]::Escape($token))
        }
    }
}
Describe 'Thin CLR launcher production UI final check contracts' {
    $launcherRoot = Join-Path $repoRoot 'src\launcher'

    It 'uses the final production state label mapping only through UiStateLabel' {
        $mainWindowText = Get-Content -LiteralPath (Join-Path $launcherRoot 'MainWindow.xaml.cs') -Raw
        $stateBlock = [regex]::Match($mainWindowText, '(?s)private string UiStateLabel.*?private string ConditionLabel').Value
        $stateBlock.Length | Should BeGreaterThan 0
        foreach ($token in @(
            'return zh ? "\u5b9e\u65f6" : "Live";',
            'return zh ? "\u7f13\u5b58" : "Cached";',
            'return zh ? "\u8fc7\u65f6" : "Stale";',
            'return zh ? "\u5237\u65b0\u5931\u8d25" : "Failed";',
            'return zh ? "\u6b63\u5728\u5237\u65b0" : "Refreshing";'
        )) {
            $stateBlock | Should Match ([regex]::Escape($token))
        }
        $outsideStateBlock = $mainWindowText.Replace($stateBlock, '')
        $outsideStateBlock | Should Not Match '\.Text\s*=\s*"(Live|Cached|Stale|Refreshing|Failed|live|cached|stale|refreshing|failed)"'
    }

    It 'keeps final condition labels inside ConditionLabel and never renders raw condition text' {
        $mainWindowText = Get-Content -LiteralPath (Join-Path $launcherRoot 'MainWindow.xaml.cs') -Raw
        $conditionBlock = [regex]::Match($mainWindowText, '(?s)private string ConditionLabel.*?private ConditionVisual ResolveConditionVisual').Value
        $conditionBlock.Length | Should BeGreaterThan 0
        foreach ($token in @(
            'case "clear": return zh ? "\u6674" : "Clear";',
            'case "cloudy": return zh ? "\u591a\u4e91" : "Cloudy";',
            'case "rain": return zh ? "\u964d\u96e8" : "Rain";',
            'case "thunderstorm": return zh ? "\u96f7\u9635\u96e8" : "Thunderstorm";',
            'case "night": return zh ? "\u591c\u95f4" : "Night";',
            'default: return ConditionLabel("cloudy");'
        )) {
            $conditionBlock | Should Match ([regex]::Escape($token))
        }
        $mainWindowText | Should Match ([regex]::Escape('conditionBlock.Text = ConditionLabel(visual.Key);'))
        $mainWindowText | Should Not Match 'conditionBlock\.Text\s*=\s*GetString'
        $mainWindowText | Should Not Match 'conditionBlock\.Text\s*=\s*condition\.'
    }

    It 'keeps production visible UI free of development and internal strings' {
        $mainWindowText = Get-Content -LiteralPath (Join-Path $launcherRoot 'MainWindow.xaml.cs') -Raw
        $visibleLines = ($mainWindowText -split "`r?`n") | Where-Object {
            $_ -match '(\.Text\s*=|Text\(|\.Content\s*=|\.ToolTip\s*=|MenuItem \{ Header)'
        }
        $visibleSurface = $visibleLines -join "`n"
        foreach ($forbidden in @(
            'Fixture',
            'debug',
            'testing',
            'Weather service unavailable',
            'Near-term forecast unavailable',
            'Fixture forecast ready',
            'Fetching weather',
            'Fetching near-term forecast',
            'WeatherWorker.ps1 was not found.',
            'Weather worker exited.',
            'Refresh failed, showing cached',
            'Cached, refreshing',
            'Updating...'
        )) {
            $visibleSurface | Should Not Match ([regex]::Escape($forbidden))
        }
        $mainWindowText | Should Match ([regex]::Escape('IsDevelopmentWeatherText'))
        $mainWindowText | Should Match ([regex]::Escape('default: return String.Empty;'))
        $mainWindowText | Should Match ([regex]::Escape('case "Weather": return zh ? "\u4f60\u4e00\u6765\u5c31\u662f\u597d\u5929\u6c14" : "Weather";'))
        $mainWindowText | Should Not Match ([regex]::Escape('case "Weather": return zh ? "\u5929\u6c14" : "Weather";'))
    }

    It 'documents the production readiness final check' {
        foreach ($relativePath in @(
            'reports\ui-production-final-check.md',
            'reports\ui-production-final-check.json'
        )) {
            Test-Path -LiteralPath (Join-Path $repoRoot $relativePath) | Should Be $true
        }
        $report = Get-Content -LiteralPath (Join-Path $repoRoot 'reports\ui-production-final-check.md') -Raw
        foreach ($token in @(
            'Raw String Leak Scan Result',
            'zh/en Consistency Result',
            'State Mapping Coverage',
            'Condition Mapping Coverage',
            'Renderer-Only Compliance Check',
            'Fallback Behavior Check',
            'Production Readiness Verdict',
            'PASS'
        )) {
            $report | Should Match ([regex]::Escape($token))
        }
    }
}
Describe 'Thin CLR launcher reliability hardening contracts' {
    $launcherRoot = Join-Path $repoRoot 'src\launcher'
    $workerPath = Join-Path $repoRoot 'src\worker\WeatherWorker.ps1'

    It 'provides deterministic worker fixture success mode' {
        $workerText = Get-Content -LiteralPath $workerPath -Raw
        foreach ($token in @(
            'FixtureWeatherSuccess',
            'New-FixtureWeatherSnapshot',
            "source = 'fixture'",
            'fixture = $true',
            'Save-WeatherSnapshotCache -LocationKey $locationKey -Snapshot $snapshot -Source ''fixture'''
        )) {
            $workerText | Should Match ([regex]::Escape($token))
        }
    }

    It 'uses explicit IPC ack error session and duplicate command guards' {
        $workerText = Get-Content -LiteralPath $workerPath -Raw
        foreach ($token in @(
            'Write-CommandAck',
            'Write-CommandError',
            'accepted = $true',
            'missing_command_id',
            'missing_command_type',
            'unknown_command',
            'malformed_json',
            'ProcessedCommandIds',
            '$commandSessionId -ne $script:SessionId',
            '$script:ProcessedCommandIds.ContainsKey($id)'
        )) {
            $workerText | Should Match ([regex]::Escape($token))
        }
    }

    It 'stale guards worker results across settings and forecast changes' {
        $workerText = Get-Content -LiteralPath $workerPath -Raw
        foreach ($token in @(
            'SettingsVersion = $script:SettingsVersion',
            'ForecastSelectionVersion = $script:ForecastSelectionVersion',
            '$Request.SettingsVersion -ne [int]$script:SettingsVersion',
            '$Request.ForecastSelectionVersion -ne [int]$script:ForecastSelectionVersion',
            '$script:ForecastSelectionVersion++'
        )) {
            $workerText | Should Match ([regex]::Escape($token))
        }
    }

    It 'tracks pending launcher commands and handles ack error timeout' {
        $mainWindowText = Get-Content -LiteralPath (Join-Path $launcherRoot 'MainWindow.xaml.cs') -Raw
        foreach ($token in @(
            'PendingCommand',
            'pendingCommands',
            'RegisterPendingCommand',
            'RemovePendingCommand',
            'OnCommandTimeoutTimerTick',
            'ApplyAck',
            'ApplyWorkerError',
            'CommandTimeout',
            'return 45000;',
            'command["sessionId"] = sessionId'
        )) {
            $mainWindowText | Should Match ([regex]::Escape($token))
        }
    }

    It 'uses numeric region keys for defaults and example settings' {
        $mainWindowText = Get-Content -LiteralPath (Join-Path $launcherRoot 'MainWindow.xaml.cs') -Raw
        $example = Get-Content -LiteralPath (Join-Path $repoRoot 'LonghuaWeatherWidget.settings.example.json') -Raw | ConvertFrom-Json
        $mainWindowText | Should Match ([regex]::Escape('var province = "440000";'))
        $mainWindowText | Should Match ([regex]::Escape('var city = "440300";'))
        $mainWindowText | Should Match ([regex]::Escape('var district = "440309";'))
        [string]$example.ProvinceKey | Should Be '440000'
        [string]$example.CityKey | Should Be '440300'
        [string]$example.DistrictKey | Should Be '440309'
    }

    It 'persists window position with monitor guard' {
        $mainWindowText = Get-Content -LiteralPath (Join-Path $launcherRoot 'MainWindow.xaml.cs') -Raw
        foreach ($token in @(
            'LoadWindowPlacement',
            'SaveWindowPlacement',
            'IsWindowPlacementOnScreen',
            'SystemParameters.VirtualScreenWidth',
            'launcher-settings.json'
        )) {
            $mainWindowText | Should Match ([regex]::Escape($token))
        }
    }

    It 'wires fixture launch option and monitor guard build references' {
        $launcherText = Get-Content -LiteralPath (Join-Path $launcherRoot 'WeatherLauncher.cs') -Raw
        $mainWindowText = Get-Content -LiteralPath (Join-Path $launcherRoot 'MainWindow.xaml.cs') -Raw
        $buildText = Get-Content -LiteralPath (Join-Path $launcherRoot 'build-launcher.ps1') -Raw
        $launcherText | Should Match ([regex]::Escape('--fixture-weather-success'))
        $launcherText | Should Match ([regex]::Escape('FixtureWeatherSuccess'))
        $mainWindowText | Should Match ([regex]::Escape('-FixtureWeatherSuccess'))
        $buildText | Should Not Match ([regex]::Escape('System.Windows.Forms.dll'))
        $buildText | Should Not Match ([regex]::Escape('System.Drawing.dll'))
    }

    It 'exits IPC worker instead of falling through to polling when owner disappears' {
        $tempRoot = Join-Path $env:TEMP ('pww-pester-owner-gone-' + [Guid]::NewGuid().ToString('N'))
        $commandFile = Join-Path $tempRoot 'commands.jsonl'
        $tracePath = Join-Path $tempRoot 'startup-trace.log'
        New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null
        New-Item -ItemType File -Path $commandFile -Force | Out-Null
        $process = $null
        try {
            $arguments = @(
                '-NoLogo',
                '-NoProfile',
                '-NonInteractive',
                '-ExecutionPolicy',
                'Bypass',
                '-File',
                ('"{0}"' -f $workerPath),
                '-AppRoot',
                ('"{0}"' -f $repoRoot),
                '-IpcMode',
                '-CommandFile',
                ('"{0}"' -f $commandFile),
                '-SessionId',
                'owner-gone-test',
                '-ParentProcessId',
                '999999',
                '-FixtureWeatherSuccess',
                '-StartupTrace',
                '-StartupTracePath',
                ('"{0}"' -f $tracePath)
            ) -join ' '

            $process = Start-Process -FilePath 'powershell.exe' -ArgumentList $arguments -PassThru -WindowStyle Hidden
            Wait-Process -Id $process.Id -Timeout 25
            $process.Refresh()
            $process.HasExited | Should Be $true
            $trace = Get-Content -LiteralPath $tracePath -Raw
            $trace | Should Match ([regex]::Escape('IPC owner gone; worker exiting'))
            ([regex]::Matches($trace, 'First stdout weather event emitted')).Count | Should Be 1
        }
        finally {
            if ($process -and -not $process.HasExited) {
                Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
            }
            Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}
Describe 'Privacy and release hygiene contracts' {
    It 'keeps published docs free of local user paths and documents fallback privacy' {
        $releaseDoc = Get-Content -LiteralPath (Join-Path $repoRoot 'docs\release\RELEASE_NOTES_v1.1.0.md') -Raw
        $readme = Get-Content -LiteralPath (Join-Path $repoRoot 'README.md') -Raw
        $releaseDoc | Should Not Match 'C:\\Users\\'
        $readme | Should Match ([regex]::Escape('%LOCALAPPDATA%\PaperWeatherWidget\'))
        $readme | Should Match 'wttr\.in .*selected district coordinates'
    }
}

Describe 'Thin CLR launcher startup profile fast-path contracts' {
    $launcherRoot = Join-Path $repoRoot 'src\launcher'
    $workerPath = Join-Path $repoRoot 'src\worker\WeatherWorker.ps1'

    It 'adds startup trace for launcher and worker without stdout pollution' {
        $launcherText = Get-Content -LiteralPath (Join-Path $launcherRoot 'WeatherLauncher.cs') -Raw
        $mainWindowText = Get-Content -LiteralPath (Join-Path $launcherRoot 'MainWindow.xaml.cs') -Raw
        $workerText = Get-Content -LiteralPath $workerPath -Raw
        foreach ($token in @(
            '--startup-trace',
            'TraceLauncher("WeatherLauncher.Main entry")',
            'TraceLauncher("ContentRendered")',
            'TraceFirstStdoutLine',
            'TraceFirstWeatherApplied'
        )) {
            ($launcherText + $mainWindowText) | Should Match ([regex]::Escape($token))
        }
        foreach ($token in @(
            'StartupTrace',
            'StartupTracePath',
            'Write-WorkerStartupTrace',
            'Before first fixture weather payload build',
            'First stdout weather event emitted'
        )) {
            $workerText | Should Match ([regex]::Escape($token))
        }
        $workerText | Should Not Match ([regex]::Escape('Write-JsonLine (Write-WorkerStartupTrace'))
    }

    It 'keeps fixture fast path away from network and static catalog load before first weather' {
        $workerText = Get-Content -LiteralPath $workerPath -Raw
        foreach ($token in @(
            '-not $FixtureWeatherSuccess -and -not (''LonghuaWeatherWorkerTimeoutWebClient'' -as [type])',
            'Import-LocationCatalog -SkipLegacyExtraction',
            'After static catalog import fixture fast path',
            'Get-ProtocolLocationKey',
            'Get-ProtocolLocationTitle'
        )) {
            $workerText | Should Match ([regex]::Escape($token))
        }
    }

    It 'does not execute planted legacy scripts for catalog loading' {
        $tempRoot = Join-Path $env:TEMP ('pww-pester-legacy-script-' + [Guid]::NewGuid().ToString('N'))
        $tempWorker = Join-Path $tempRoot 'WeatherWorker.ps1'
        $sentinel = Join-Path $tempRoot 'legacy-executed.txt'
        New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null
        try {
            Copy-Item -LiteralPath $workerPath -Destination $tempWorker -Force
            @"
`$script:ForecastSlotDefinitions = @(
    [pscustomobject]@{ Key = 'Day0'; OffsetDays = 0; Kind = 'Day' }
)
function New-District {
    Set-Content -LiteralPath '$sentinel' -Value 'executed'
}
`$script:Text = @{}
"@ | Set-Content -LiteralPath (Join-Path $tempRoot 'LonghuaWeatherWidget.ps1') -Encoding UTF8
            $output = & powershell.exe -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $tempWorker -AppRoot $tempRoot -IpcSmoke -SessionId legacy-smoke 2>&1
            $LASTEXITCODE | Should Be 0
            Test-Path -LiteralPath $sentinel | Should Be $false
            ($output | Out-String) | Should Match '"type":"catalog"'
        }
        finally {
            Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    It 'keeps getRegionCatalog lazy and command-driven after fixture first weather' {
        $workerText = Get-Content -LiteralPath $workerPath -Raw
        $workerText | Should Match ([regex]::Escape('function Ensure-FullLocationCatalog'))
        $workerText | Should Match ([regex]::Escape('function Get-RegionCatalogPayload'))
        $workerText | Should Match ([regex]::Escape('Ensure-FullLocationCatalog'))
        $workerText | Should Match ([regex]::Escape('smoke-manualRefresh'))
        $workerText | Should Match ([regex]::Escape('smoke-catalog'))
    }

    It 'keeps launcher settings and metrics controls lazy but functional' {
        $mainWindowText = Get-Content -LiteralPath (Join-Path $launcherRoot 'MainWindow.xaml.cs') -Raw
        foreach ($token in @(
            'EnsureSettingsPanelCreated',
            'settingsPanelBuilt',
            'pendingSettingsPayload',
            'pendingCatalogPayload',
            'ApplyLocalSettingsFallback',
            'LoadLocalRegionCatalogPayload',
            'LocalRegionCatalogPaths',
            'DisplayLabel',
            'SelectCatalogKeys(GetString(pendingSettingsPayload',
            'updatingSettingsControls = true;',
            'EnsureMetricsGridCreated',
            'metricsGridBuilt'
        )) {
            $mainWindowText | Should Match ([regex]::Escape($token))
        }
        $mainWindowText | Should Match ([regex]::Escape('settingsPanel.Visibility = Visibility.Collapsed'))
        $mainWindowText | Should Match ([regex]::Escape('settingsPanel.Child = stack'))
        $mainWindowText | Should Not Match ([regex]::Escape('scroll.MaxHeight = 286'))
        $mainWindowText | Should Match ([regex]::Escape('DispatcherPriority.ApplicationIdle'))
        $mainWindowText | Should Match ([regex]::Escape('SendCommand("manualRefresh"'))
        $mainWindowText | Should Match 'IsDragExcludedSource\(source\)'
        $mainWindowText | Should Match 'OnDragHandleMouseDown'
    }

    It 'uses lightweight monitor guard and NonInteractive worker process args' {
        $mainWindowText = Get-Content -LiteralPath (Join-Path $launcherRoot 'MainWindow.xaml.cs') -Raw
        $buildText = Get-Content -LiteralPath (Join-Path $launcherRoot 'build-launcher.ps1') -Raw
        $mainWindowText | Should Match ([regex]::Escape('SystemParameters.VirtualScreenWidth'))
        $mainWindowText | Should Match ([regex]::Escape('-NonInteractive'))
        $mainWindowText | Should Match ([regex]::Escape('SaveWindowPlacement'))
        $buildText | Should Not Match ([regex]::Escape('System.Windows.Forms.dll'))
    }
}
Describe 'Thin CLR launcher snapshot boot contracts' {
    $launcherRoot = Join-Path $repoRoot 'src\launcher'
    $workerPath = Join-Path $repoRoot 'src\worker\WeatherWorker.ps1'

    It 'defines versioned atomic snapshot protocol and shared path' {
        $launcherText = Get-Content -LiteralPath (Join-Path $launcherRoot 'WeatherLauncher.cs') -Raw
        $workerText = Get-Content -LiteralPath $workerPath -Raw
        foreach ($token in @(
            'weather-snapshot.json',
            'PaperWeatherWidget',
            'FindWeatherSnapshot',
            'LogSnapshotApplied'
        )) {
            $launcherText | Should Match ([regex]::Escape($token))
        }
        foreach ($token in @(
            'Get-PersistentWeatherSnapshotPath',
            'schema = 1',
            'savedAt',
            'locationKey',
            'source',
            'payload = $payloadCopy',
            'weather-snapshot.{0}.{1}.tmp',
            '[IO.File]::Replace',
            '[IO.File]::Move'
        )) {
            $workerText | Should Match ([regex]::Escape($token))
        }
    }

    It 'writes fixture snapshot only when explicitly allowed' {
        $tempLocal = Join-Path $env:TEMP ('pww-pester-snapshot-' + [Guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $tempLocal -Force | Out-Null
        $oldLocal = $env:LOCALAPPDATA
        try {
            $env:LOCALAPPDATA = $tempLocal
            $snapshotPath = Join-Path (Join-Path $tempLocal 'PaperWeatherWidget') 'weather-snapshot.json'
            $defaultOutput = & powershell.exe -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $workerPath -AppRoot $tempLocal -Once -FixtureWeatherSuccess 2>&1
            Test-Path -LiteralPath $snapshotPath | Should Be $false
            $allowedOutput = & powershell.exe -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $workerPath -AppRoot $tempLocal -Once -FixtureWeatherSuccess -AllowFixtureSnapshotWrite 2>&1
            Test-Path -LiteralPath $snapshotPath | Should Be $true
            $snapshot = Get-Content -LiteralPath $snapshotPath -Encoding UTF8 -Raw | ConvertFrom-Json
            [int]$snapshot.schema | Should Be 1
            [string]$snapshot.source | Should Be 'fixture'
            [bool]$snapshot.fixture | Should Be $true
            [string]$snapshot.locationKey | Should Be '440000|440300|440309'
            [string]$snapshot.payload.status | Should Be 'ok'
            $secondOutput = & powershell.exe -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $workerPath -AppRoot $tempLocal -Once -FixtureWeatherSuccess -AllowFixtureSnapshotWrite 2>&1
            Test-Path -LiteralPath $snapshotPath | Should Be $true
            $snapshot2 = Get-Content -LiteralPath $snapshotPath -Encoding UTF8 -Raw | ConvertFrom-Json
            [int]$snapshot2.schema | Should Be 1
            [string]$snapshot2.payload.status | Should Be 'ok'
        }
        finally {
            $env:LOCALAPPDATA = $oldLocal
            Remove-Item -LiteralPath $tempLocal -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    It 'does not persist error memory-cache or fixture payloads by default' {
        $workerText = Get-Content -LiteralPath $workerPath -Raw
        foreach ($token in @(
            'status-not-ok',
            'memory-cache',
            'fixture-not-allowed',
            '$script:FixtureWeatherSuccess) -and -not $script:AllowFixtureSnapshotWrite',
            'Write-SnapshotPersistenceIssue',
            'Snapshot write failed'
        )) {
            $workerText | Should Match ([regex]::Escape($token))
        }
        $workerText | Should Match ([regex]::Escape('Write-WorkerError'))
        $workerText | Should Match ([regex]::Escape('Save-PersistentWeatherSnapshot -Snapshot $snapshot'))
    }

    It 'loads validates applies and skips snapshots without blocking startup' {
        $mainWindowText = Get-Content -LiteralPath (Join-Path $launcherRoot 'MainWindow.xaml.cs') -Raw
        foreach ($token in @(
            'ThreadPool.QueueUserWorkItem',
            'BeginSnapshotBoot',
            'SnapshotBoot file read start',
            'SnapshotBoot file read end',
            'SnapshotBoot valid',
            'SnapshotBoot invalid',
            'SnapshotBoot skipped reason',
            'ValidateSnapshotEnvelope',
            'cross-location',
            'invalid-payload-status',
            'ApplySnapshotPayload',
            'StartupBenchmark.LogSnapshotApplied',
            'SnapshotBoot applied'
        )) {
            $mainWindowText | Should Match ([regex]::Escape($token))
        }
        $mainWindowText.IndexOf('BeginSnapshotBoot();') | Should BeLessThan $mainWindowText.IndexOf('StartWorker();')
    }

    It 'preserves snapshot UI on worker errors and keeps existing interaction contracts' {
        $mainWindowText = Get-Content -LiteralPath (Join-Path $launcherRoot 'MainWindow.xaml.cs') -Raw
        foreach ($token in @(
            'PreserveSnapshotOffline',
            'LiveRefreshFailedCached',
            'ClearSnapshotForLocationChange',
            'settingsPanel.Visibility = Visibility.Collapsed',
            'EnsureSettingsPanelCreated',
            'pendingSettingsPayload',
            'command["sessionId"] = sessionId'
        )) {
            $mainWindowText | Should Match ([regex]::Escape($token))
        }
        $mainWindowText | Should Match 'IsDragExcludedSource\(source\)'
        $mainWindowText | Should Match 'OnDragHandleMouseDown'
    }

    It 'keeps IPC ack error and stale guards while adding snapshot trace milestones' {
        $workerText = Get-Content -LiteralPath $workerPath -Raw
        foreach ($token in @(
            'Write-CommandAck',
            'Write-CommandError',
            'ProcessedCommandIds',
            '$commandSessionId -ne $script:SessionId',
            'Test-WeatherRequestIsCurrent',
            'Snapshot write start',
            'Snapshot write success',
            'Snapshot write skipped reason',
            'Snapshot write failed'
        )) {
            $workerText | Should Match ([regex]::Escape($token))
        }
    }
}

Describe 'Thin CLR launcher snapshot diagnostics contracts' {
    $launcherRoot = Join-Path $repoRoot 'src\launcher'
    $workerPath = Join-Path $repoRoot 'src\worker\WeatherWorker.ps1'

    It 'shows cached snapshot state separately from live weather state' {
        $mainWindowText = Get-Content -LiteralPath (Join-Path $launcherRoot 'MainWindow.xaml.cs') -Raw
        foreach ($token in @(
            'SnapshotDisplayState',
            'FreshSnapshot',
            'StaleSnapshot',
            'RefreshingFromSnapshot',
            'RefreshFailedShowingSnapshot',
            'TryReadSnapshotDiagnostics',
            'GetSnapshotAge',
            'GetSnapshotDisplayStatus',
            'SnapshotBoot diagnostics valid=',
            'statusBlock.Name = "SnapshotStatusText"',
            'updatedBlock.Name = "SnapshotUpdatedText"',
            'errorBlock.Name = "SnapshotErrorText"',
            'CachedData',
            'LastDataRefreshing',
            'StaleCacheRefreshing',
            'RefreshFailedShowingCached'
        )) {
            $mainWindowText | Should Match ([regex]::Escape($token))
        }
        $mainWindowText | Should Match ([regex]::Escape('SetSnapshotDisplayState(staleSnapshot ? SnapshotDisplayState.StaleSnapshot : SnapshotDisplayState.RefreshingFromSnapshot)'))
        $mainWindowText | Should Match ([regex]::Escape('SetSnapshotDisplayState(SnapshotDisplayState.Live)'))
        $mainWindowText | Should Match ([regex]::Escape('SetSnapshotDisplayState(SnapshotDisplayState.RefreshFailedShowingSnapshot)'))
    }

    It 'keeps cached snapshot visible on worker failure and replaces it on live success' {
        $mainWindowText = Get-Content -LiteralPath (Join-Path $launcherRoot 'MainWindow.xaml.cs') -Raw
        foreach ($token in @(
            'if (snapshotWeatherVisible && !liveWeatherApplied)',
            'PreserveSnapshotOffline(message)',
            'RenderErrorMessage(message)',
            'if (liveWeatherApplied)',
            'RenderStatus("failed")',
            'snapshotWeatherVisible = false;',
            'liveWeatherApplied = true;',
            'StartupBenchmark.LogFirstData();',
            'StartupBenchmark.TraceFirstWeatherApplied();',
            'UiStateLabel("failed") + " / " + UiStateLabel("cached")',
            'FormatSnapshotStatus(UiStateLabel("refreshing"))'
        )) {
            $mainWindowText | Should Match ([regex]::Escape($token))
        }
    }

    It 'exposes read-only worker snapshot diagnostics over IPC' {
        $workerText = Get-Content -LiteralPath $workerPath -Raw
        foreach ($token in @(
            'function Get-PersistentSnapshotDiagnosticsPayload',
            "'getSnapshotDiagnostics'",
            "Write-ProtocolEvent -Type 'snapshotDiagnostics'",
            'exists = $false',
            'valid = $false',
            'ageSeconds = $null',
            "skipReason = 'missing-file'",
            "skipReason = 'malformed-json'",
            "skipReason = 'cross-location'",
            'smoke-snapshotDiagnostics'
        )) {
            $workerText | Should Match ([regex]::Escape($token))
        }
        $diagnosticsBlock = [regex]::Match($workerText, '(?s)function Get-PersistentSnapshotDiagnosticsPayload.*?function Get-CachedWeatherSnapshot').Value
        $diagnosticsBlock | Should Not Match ([regex]::Escape('Save-PersistentWeatherSnapshot'))
        $diagnosticsBlock | Should Not Match ([regex]::Escape('Invoke-WeatherRefresh'))
        $diagnosticsBlock | Should Not Match ([regex]::Escape('Get-WeatherSnapshot'))
    }

    It 'falls back to persistent snapshots after worker restarts and guards stale raw forecast cache' {
        $workerText = Get-Content -LiteralPath $workerPath -Raw
        foreach ($token in @(
            'function Get-PersistentWeatherSnapshot',
            '$persistent = Get-PersistentWeatherSnapshot',
            '$copy[''fromSnapshot''] = $true',
            '$copy[''fromCache''] = $true',
            'function Test-RawWeatherCacheFresh',
            '$script:RawWeatherCacheMaxAgeMinutes = 30',
            'Test-RawWeatherCacheFresh -Entry $script:RawWeatherCache[$locationKey]',
            'ConvertTo-OpenMeteoSnapshot -Weather $rawEntry.Weather -FromCache:$true',
            '$script:NextAutoRefreshAt = (Get-Date).AddSeconds($script:RefreshSeconds)'
        )) {
            $workerText | Should Match ([regex]::Escape($token))
        }
    }

    It 'emits malformed snapshot diagnostics without modifying the snapshot file' {
        $tempLocal = Join-Path $env:TEMP ('pww-pester-diag-malformed-' + [Guid]::NewGuid().ToString('N'))
        $snapshotDir = Join-Path $tempLocal 'PaperWeatherWidget'
        $snapshotPath = Join-Path $snapshotDir 'weather-snapshot.json'
        New-Item -ItemType Directory -Path $snapshotDir -Force | Out-Null
        $utf8NoBom = New-Object System.Text.UTF8Encoding $false
        [IO.File]::WriteAllText($snapshotPath, '{bad', $utf8NoBom)
        $before = [IO.File]::ReadAllText($snapshotPath, [Text.Encoding]::UTF8)
        $oldLocal = $env:LOCALAPPDATA
        try {
            $env:LOCALAPPDATA = $tempLocal
            $output = & powershell.exe -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $workerPath -AppRoot $repoRoot -IpcSmoke -SessionId diag-smoke-malformed
            $LASTEXITCODE | Should Be 0
        }
        finally {
            $env:LOCALAPPDATA = $oldLocal
        }
        $after = [IO.File]::ReadAllText($snapshotPath, [Text.Encoding]::UTF8)
        $before | Should Be $after
        $events = @($output | Where-Object { ($_ -is [string]) -and $_.Trim().StartsWith('{') } | ForEach-Object { $_ | ConvertFrom-Json })
        $diag = @($events | Where-Object { $_.type -eq 'snapshotDiagnostics' })[0]
        $diag | Should Not Be $null
        [bool]$diag.payload.exists | Should Be $true
        [bool]$diag.payload.valid | Should Be $false
        [string]$diag.payload.skipReason | Should Be 'malformed-json'
        Remove-Item -LiteralPath $tempLocal -Recurse -Force -ErrorAction SilentlyContinue
    }

    It 'emits cross-location snapshot diagnostics as valid but not current-location matched' {
        $tempLocal = Join-Path $env:TEMP ('pww-pester-diag-cross-' + [Guid]::NewGuid().ToString('N'))
        $snapshotDir = Join-Path $tempLocal 'PaperWeatherWidget'
        $snapshotPath = Join-Path $snapshotDir 'weather-snapshot.json'
        New-Item -ItemType Directory -Path $snapshotDir -Force | Out-Null
        $envelope = [ordered]@{
            schema = 1
            savedAt = [DateTime]::UtcNow.ToString('o')
            source = 'fixture'
            fixture = $true
            locationKey = 'Guangdong|Shenzhen|Futian'
            locationLabel = 'Shenzhen - Futian'
            payload = [ordered]@{
                status = 'ok'
                source = 'fixture'
                locationKey = 'Guangdong|Shenzhen|Futian'
            }
        }
        $utf8NoBom = New-Object System.Text.UTF8Encoding $false
        [IO.File]::WriteAllText($snapshotPath, ($envelope | ConvertTo-Json -Depth 6), $utf8NoBom)
        $oldLocal = $env:LOCALAPPDATA
        try {
            $env:LOCALAPPDATA = $tempLocal
            $output = & powershell.exe -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $workerPath -AppRoot $repoRoot -IpcSmoke -SessionId diag-smoke-cross
            $LASTEXITCODE | Should Be 0
        }
        finally {
            $env:LOCALAPPDATA = $oldLocal
        }
        $events = @($output | Where-Object { ($_ -is [string]) -and $_.Trim().StartsWith('{') } | ForEach-Object { $_ | ConvertFrom-Json })
        $diag = @($events | Where-Object { $_.type -eq 'snapshotDiagnostics' })[0]
        $diag | Should Not Be $null
        [bool]$diag.payload.exists | Should Be $true
        [bool]$diag.payload.valid | Should Be $true
        [bool]$diag.payload.matchesCurrentLocation | Should Be $false
        [string]$diag.payload.skipReason | Should Be 'cross-location'
        [string]$diag.payload.locationKey | Should Be 'Guangdong|Shenzhen|Futian'
        Remove-Item -LiteralPath $tempLocal -Recurse -Force -ErrorAction SilentlyContinue
    }
}









