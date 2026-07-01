# Paper Weather Widget v2.0.1 / 纸感天气小组件 v2.0.1

Release title: Paper Weather Widget v2.0.1 / 纸感天气小组件 v2.0.1

## English

### What Changed

- Ships the Thin CLR launcher as the main Windows build: `WeatherLauncher.exe` starts fast, then `WeatherWorker.ps1` handles weather data over stdout JSON IPC.
- Adds full Chinese / English UI coverage for weather state, settings, location names, forecast labels, cached state, and startup controls.
- Keeps the UI in Chinese after refresh when Chinese is selected, and keeps English mode free of leftover Chinese labels.
- Makes the window draggable from the title area and border edges while keeping normal content clicks usable.
- Enlarges and aligns the title-bar controls so the cache / live badge, refresh interval, settings, minimize, and close buttons feel balanced.
- Fixes the expanded default height so weather metrics and settings are no longer clipped.
- Adds a settings footer credit link to `M47E1/paper-weather-widget` with an external-link arrow, rendered as plain text without a card background.
- Packages the runtime into a single Windows exe for v2.0.1.

### Package

Download `PaperWeatherWidget-v2.0.1-win-x64.exe` and run it directly. The worker script and region catalog are bundled inside the exe and are released to the local user cache at runtime.

No administrator rights, API key, WebView2, Node.js, telemetry, or paid weather service is required.

### Validation

- `src/launcher/build-launcher.ps1 -Version 2.0.1`: PASS.
- `build-single-exe-release.ps1 -Version 2.0.1`: PASS.
- Project tests: `271/271 PASS`.
- Package verification: the exe contains `WeatherWorker.ps1` and `ChinaRegionCatalog.json` as embedded resources.
- Version verification: `PaperWeatherWidget-v2.0.1-win-x64.exe` FileVersion is `2.0.1.0` and ProductVersion is `2.0.1`.
- Single exe smoke: running from an otherwise empty folder extracts the worker and catalog, opens settings, and shows `爱来自 M47E1/paper-weather-widget` with the external-link arrow.

## 中文

### 更新内容

- 正式采用 Thin CLR 启动器作为 Windows 主构建：`WeatherLauncher.exe` 负责快速显示窗口，`WeatherWorker.ps1` 通过 stdout JSON IPC 处理天气数据。
- 补齐中文 / English 双语界面，覆盖天气状态、设置项、地点名称、预报标签、缓存状态和开机启动控件。
- 中文模式下刷新后继续保持中文；英文模式下不再残留中文标签。
- 窗口可从标题区域和边框边缘拖动，内容区点击不再误触拖拽。
- 放大并统一标题栏控件，让缓存 / 实时标记、刷新频率、设置、最小化和关闭按钮更平衡。
- 修正展开后的默认高度，天气指标和设置区不再被遮挡。
- 在设置页底部加入 `M47E1/paper-weather-widget` 来源链接和外链箭头，改为纯文字样式，不再显示背后的卡片容器。
- v2.0.1 改为发布单个 Windows exe。

### 包内容

下载 `PaperWeatherWidget-v2.0.1-win-x64.exe` 后直接运行。worker 脚本和地区库已内嵌在 exe 中，运行时会释放到当前用户的本地缓存目录。

无需管理员权限、API key、WebView2、Node.js、遥测或付费天气服务。

### 验证

- `src/launcher/build-launcher.ps1 -Version 2.0.1`：通过。
- `build-single-exe-release.ps1 -Version 2.0.1`：通过。
- 项目测试：`271/271 PASS`。
- 打包校验：exe 内嵌了 `WeatherWorker.ps1` 和 `ChinaRegionCatalog.json`。
- 版本校验：`PaperWeatherWidget-v2.0.1-win-x64.exe` FileVersion 为 `2.0.1.0`，ProductVersion 为 `2.0.1`。
- 单 exe smoke：在空目录中只运行这一个 exe，可以自动释放 worker 和地区库，打开设置页后显示 `爱来自 M47E1/paper-weather-widget` 和外链箭头。

## SHA256

```text
80b06d773e1fc65563aaa0153a5ebb9882e11a1f4ff8446c58441e5026ff904f  PaperWeatherWidget-v2.0.1-win-x64.exe
```

## Notes / 说明

This build is unsigned. Windows SmartScreen may show an unknown publisher warning on first launch.

此版本未签名，Windows SmartScreen 首次启动时可能提示未知发布者。

This project is independent and is not affiliated with Anthropic, Claude, Open-Meteo, or wttr.in.

本项目为独立项目，与 Anthropic、Claude、Open-Meteo、wttr.in 均无从属或官方合作关系。