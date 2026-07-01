# Paper Weather Widget v2.0.0 / 纸感天气小组件 v2.0.0

Release title: Paper Weather Widget v2.0.0 / 纸感天气小组件 v2.0.0

## English

### What Changed

- Ships the Thin CLR launcher as the main Windows build: `WeatherLauncher.exe` starts fast, then `WeatherWorker.ps1` handles weather data over stdout JSON IPC.
- Adds full Chinese / English UI coverage for weather state, settings, location names, forecast labels, cached state, and startup controls.
- Keeps the UI in Chinese after refresh when Chinese is selected, and keeps English mode free of leftover Chinese labels.
- Makes the window draggable from the title area and border edges while keeping normal content clicks usable.
- Enlarges and aligns the title-bar controls so the cache / live badge, refresh interval, settings, minimize, and close buttons feel balanced.
- Fixes the expanded default height so weather metrics and settings are no longer clipped.
- Adds a settings footer credit link to `M47E1/paper-weather-widget` with an external-link arrow, rendered as plain text without a card background.
- Packages the full runtime folder for v2.0.0 instead of shipping a single legacy PowerShell exe.

### Package

Download `PaperWeatherWidget-v2.0.0-win-x64.zip`, unzip it, and run `WeatherLauncher.exe`. Keep every file from the ZIP in the same folder.

ZIP contents:

- `WeatherLauncher.exe`
- `WeatherWorker.ps1`
- `ChinaRegionCatalog.json`
- `App.xaml`
- `MainWindow.xaml`
- `LICENSE`
- `README.txt`

No administrator rights, API key, WebView2, Node.js, telemetry, or paid weather service is required.

### Validation

- `src/launcher/build-launcher.ps1 -Version 2.0.0`: PASS.
- `build-thinclr-release.ps1 -Version 2.0.0`: PASS.
- Package verification: ZIP contains the expected seven runtime files.
- Version verification: `WeatherLauncher.exe` FileVersion is `2.0.0.0` and ProductVersion is `2.0.0`.
- UI verification: the settings footer shows `爱来自 M47E1/paper-weather-widget` and the external-link arrow; closing settings hides the credit from the weather view.

## 中文

### 更新内容

- 正式采用 Thin CLR 启动器作为 Windows 主构建：`WeatherLauncher.exe` 负责快速显示窗口，`WeatherWorker.ps1` 通过 stdout JSON IPC 处理天气数据。
- 补齐中文 / English 双语界面，覆盖天气状态、设置项、地点名称、预报标签、缓存状态和开机启动控件。
- 中文模式下刷新后继续保持中文；英文模式下不再残留中文标签。
- 窗口可从标题区域和边框边缘拖动，内容区点击不再误触拖拽。
- 放大并统一标题栏控件，让缓存 / 实时标记、刷新频率、设置、最小化和关闭按钮更平衡。
- 修正展开后的默认高度，天气指标和设置区不再被遮挡。
- 在设置页底部加入 `M47E1/paper-weather-widget` 来源链接和外链箭头，改为纯文字样式，不再显示背后的卡片容器。
- v2.0.0 改为发布完整运行目录，不再只发布旧版单文件 PowerShell exe。

### 包内容

下载 `PaperWeatherWidget-v2.0.0-win-x64.zip`，解压后运行 `WeatherLauncher.exe`。请保持 ZIP 内全部文件在同一个文件夹。

ZIP 内容：

- `WeatherLauncher.exe`
- `WeatherWorker.ps1`
- `ChinaRegionCatalog.json`
- `App.xaml`
- `MainWindow.xaml`
- `LICENSE`
- `README.txt`

无需管理员权限、API key、WebView2、Node.js、遥测或付费天气服务。

### 验证

- `src/launcher/build-launcher.ps1 -Version 2.0.0`：通过。
- `build-thinclr-release.ps1 -Version 2.0.0`：通过。
- 打包校验：ZIP 包含预期 7 个运行文件。
- 版本校验：`WeatherLauncher.exe` FileVersion 为 `2.0.0.0`，ProductVersion 为 `2.0.0`。
- UI 校验：设置页底部显示 `爱来自 M47E1/paper-weather-widget` 和外链箭头；关闭设置后，天气页不显示来源链接。

## SHA256

```text
dc6a013b563f3dd57e5513d7e028ad96892e042c16563a7ee677bea8219fc73d  PaperWeatherWidget-v2.0.0-win-x64.zip
8c4fa602002d129a33860b14b56b344b9f81118213e1c91e435940c2ca1575a2  PaperWeatherWidget-v2.0.0-win-x64/WeatherLauncher.exe
2054523faa4b083293b6661336648b5b667bb93f0f32cfb016d73735adf33953  PaperWeatherWidget-v2.0.0-win-x64/WeatherWorker.ps1
e4a0963613a176fa16ceb50783bc4e55a1e1bba37734614556023763a47b445a  PaperWeatherWidget-v2.0.0-win-x64/ChinaRegionCatalog.json
313c2858fb81ee5305a04a283741f5eeb6453dcf9973b3c99ff4cb92025e68a9  PaperWeatherWidget-v2.0.0-win-x64/App.xaml
523b28ccfe07ceb2c47fedfd2caf02b54665ccfade372923bbbf01b3854d39d1  PaperWeatherWidget-v2.0.0-win-x64/MainWindow.xaml
3f016f66f73251ed95c7c1950bc8c0ddee702fb28c076d3e806399e507798624  PaperWeatherWidget-v2.0.0-win-x64/LICENSE
087f76b88cfcc39b372404c524e069b92485f164d488aa6e908333286ac35c82  PaperWeatherWidget-v2.0.0-win-x64/README.txt
```

## Notes / 说明

This build is unsigned. Windows SmartScreen may show an unknown publisher warning on first launch.

此版本未签名，Windows SmartScreen 首次启动时可能提示未知发布者。

This project is independent and is not affiliated with Anthropic, Claude, Open-Meteo, or wttr.in.

本项目为独立项目，与 Anthropic、Claude、Open-Meteo、wttr.in 均无从属或官方合作关系。