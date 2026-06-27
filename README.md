# Longhua Weather Widget — Anthropic-inspired Edition

Anthropic-inspired 风格的轻量 Windows 天气小组件。支持当前天气、临近预报、模型风险提示、中文 / English 双语界面、地区切换、侧边抽屉和本地设置保存。

A lightweight Anthropic-inspired Windows weather widget with current weather, near-term forecast, model risk tips, bilingual UI, region switching, a side-drawer window, and local settings.

This is the 基础版本: a WPF-only desktop widget without WebView2, Node.js, Cloudflare, API keys, telemetry, or tracking.

## Download

Download v1.1.0 from GitHub Releases:

https://github.com/M47E1/longhua-weather-widget/releases/tag/v1.1.0

Recommended assets:

- `LonghuaWeatherWidget-v1.1.0-anthropic-win-x64.exe`
- `LonghuaWeatherWidget-v1.1.0-anthropic-win-x64.zip`
- `SHA256SUMS.txt`

The ZIP contains only `LICENSE`, `LonghuaWeatherWidget.exe`, and `README.txt`.

No administrator rights are required.

## Edition

The v1.1.0 UI uses an Anthropic-inspired style: paper-toned surfaces, restrained borders, compact typography, side-drawer mode, and warm accent controls. It does not use Anthropic or Claude logos, brand assets, or official product claims.

## Features

- Current weather and forecast views are visually and textually distinct: `Now` for current data, `Forecast · HH:mm` for forecast data.
- Open-Meteo is the primary weather provider; wttr.in remains the fallback.
- Cached weather stays usable when refresh fails.
- Model-derived risk tips are separated from official weather warnings.
- Chinese and English UI labels.
- 47 real supported regions in the current built-in catalog.
- Side-drawer window with settings, language, refresh interval, and forecast slot controls.
- Local settings are saved next to the launched script or EXE.

## Run From Source

```powershell
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -Sta -File .\LonghuaWeatherWidget.ps1
```

Or use:

```cmd
Start-LonghuaWeather.cmd
```

## Settings

Runtime settings are stored in `LonghuaWeatherWidget.settings.json` next to the launched script or EXE. UI smoke runs isolate settings under their output directory.

Do not commit real local settings, private paths, tokens, or exact home addresses.

## Build Release Assets

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build-release.ps1
```

The build writes:

- `dist/LonghuaWeatherWidget-v1.1.0-anthropic-win-x64.exe`
- `dist/LonghuaWeatherWidget-v1.1.0-anthropic-win-x64.zip`
- `dist/SHA256SUMS.txt`

PS2EXE flags include `NoConsole`, `STA`, `DPIAware`, `SupportOS`, and `x64`. The build does not use `RequireAdmin`.

## Testing

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Run-ProjectTests.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\LonghuaWeatherWidget.ps1 -TestMode
```

Release evidence uses `reports/final-evidence/english-ui-gate-20260627-125712/final-evidence-index.json` from the RC source tree.

## Known Limitations

- The EXE is unsigned.
- Windows SmartScreen may show an Unknown publisher warning.
- Open-Meteo provides model current weather, not official on-site observation.
- The base edition does not integrate an official weather warning API.
- Model-derived content is a risk tip, not an official warning.
- The built-in region catalog contains 47 real supported regions.
- `RealUiInteractionSmoke` remains FAIL because of WPF UI Automation popup and AutomationId limitations.
- Anthropic-inspired only: this is not an official Anthropic or Claude product.

## Weather Providers

Primary provider: Open-Meteo Forecast API.

Fallback provider: wttr.in JSON endpoint.

No API key is required. Third-party services remain subject to their own availability and terms.

## License

MIT License. See `LICENSE`.