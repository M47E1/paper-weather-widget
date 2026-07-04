# Paper Weather Widget / 纸感天气小组件

Paper Weather Widget is a lightweight Windows weather widget with a paper-toned WPF interface, Chinese / English UI, district-level region switching, cached weather, forecast slots, and a compact side-drawer window.

纸感天气小组件是一款轻量 Windows 天气小组件，采用纸感 WPF 界面，支持中文 / English 双语、区县级地区切换、缓存天气、预报时段和紧凑侧边抽屉窗口。

This project is independent. It is not affiliated with Anthropic, Claude, Open-Meteo, or wttr.in, and it does not use their brand assets.

本项目为独立项目，与 Anthropic、Claude、Open-Meteo、wttr.in 均无从属或官方合作关系，也不使用它们的品牌资产。

## Download / 下载

Download v2.0.1 from GitHub Releases:

从 GitHub Releases 下载 v2.0.1：

https://github.com/M47E1/paper-weather-widget/releases/tag/v2.0.1

Recommended assets / 推荐下载：

- `PaperWeatherWidget-v2.0.1-win-x64.exe`
- `SHA256SUMS.txt`

Run `PaperWeatherWidget-v2.0.1-win-x64.exe` directly. The worker script and region catalog are bundled inside the exe and are released to the local user cache at runtime.

直接运行 `PaperWeatherWidget-v2.0.1-win-x64.exe`。worker 脚本和地区库已内嵌在 exe 中，运行时会释放到当前用户的本地缓存目录。

No administrator rights are required.

无需管理员权限。

## What It Does / 功能

- Shows current weather, daily forecast, hourly forecast slots, rain, humidity, pressure, wind, gust, cloud cover, and feels-like temperature.
- 显示当前天气、按日预报、逐小时预报时段、降雨、湿度、气压、风速、阵风、云量和体感温度。
- Supports Chinese and English labels without restarting the app.
- 支持中文和英文界面，无需重启即可切换。
- Switches between built-in province, city, and district locations.
- 支持内置省、市、区县地点切换。
- Uses cached weather when refresh fails, then refreshes again in the background.
- 刷新失败时继续显示缓存天气，并在后台继续刷新。
- Saves local settings such as language, location, refresh interval, forecast slot, startup option, and window position.
- 本地保存语言、地点、刷新频率、预报时段、开机启动、窗口位置等设置。

## Weather Providers / 天气数据

Primary provider: Open-Meteo Forecast API.

主要数据源：Open-Meteo Forecast API。

Fallback provider: wttr.in JSON endpoint.

备用数据源：wttr.in JSON 接口。

When the fallback is used, wttr.in receives the selected district coordinates needed for the weather lookup.

启用备用数据源时，wttr.in 会收到用于查询天气的所选区县坐标。

No API key, account, paid weather service, telemetry, WebView2, Node.js, or browser runtime is required.

不需要 API key、账号、付费天气服务、遥测、WebView2、Node.js 或浏览器运行时。

## Build From Source / 从源码构建

Build the Thin CLR launcher for development:

构建 Thin CLR 开发版启动器：

```powershell
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File .\src\launcher\build-launcher.ps1 -Version 2.0.1
```

Create the single exe release asset and hashes:

生成单 exe release 资产和哈希：

```powershell
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File .\build-single-exe-release.ps1 -Version 2.0.1
```

Release output:

发布输出：

- `dist/release/v2.0.1/PaperWeatherWidget-v2.0.1-win-x64.exe`
- `dist/release/v2.0.1/SHA256SUMS.txt`

## Run From Source / 从源码运行

Legacy PowerShell entrypoint:

旧 PowerShell 入口：

```powershell
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -Sta -File .\LonghuaWeatherWidget.ps1
```

Thin CLR development build:

Thin CLR 开发构建：

```powershell
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File .\src\launcher\build-launcher.ps1 -Version 2.0.1
.\dist\launcher\WeatherLauncher.exe
```

## Settings / 设置

Runtime settings are stored under the current user's local app data folder: `%LOCALAPPDATA%\PaperWeatherWidget\`. Do not commit real local settings, private paths, tokens, or exact home addresses.

运行时设置保存在当前用户的本地应用数据目录：`%LOCALAPPDATA%\PaperWeatherWidget\`。请不要提交真实本地设置、私有路径、token 或精确家庭地址。

## Verification / 验证

Use the verification scope that matches the change. Release builds should at least run project tests, launcher build, package verification, and a targeted UI smoke test for the changed surface.

请按改动范围选择验证方式。正式发布至少应运行项目测试、启动器构建、打包校验，以及针对改动界面的 UI smoke 检查。

## Known Limitations / 已知限制

- The EXE is unsigned. Windows SmartScreen may show an unknown publisher warning on first launch.
- EXE 未签名，Windows SmartScreen 首次启动时可能提示未知发布者。
- Open-Meteo provides model weather data, not official on-site observation.
- Open-Meteo 提供的是模型天气数据，不是官方现场观测。
- Model-derived tips are only weather risk hints, not official warnings.
- 模型推导内容只作为天气风险提示，不是官方预警。

## License / 许可证

MIT License. See `LICENSE`.

MIT 许可证。详见 `LICENSE`。
