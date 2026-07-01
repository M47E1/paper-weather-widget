using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using Microsoft.Win32;
using System.Collections;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace WeatherLauncher
{
    public sealed class MainWindow : Window
    {
        private enum SnapshotDisplayState
        {
            None,
            FreshSnapshot,
            StaleSnapshot,
            RefreshingFromSnapshot,
            Live,
            RefreshFailedShowingSnapshot
        }
        private readonly LauncherOptions options;
        private readonly JavaScriptSerializer json;
        private Process workerProcess;
        private bool workerStarted;
        private Border rootBorder;
        private Border statusShell;
        private Border conditionCard;
        private Border iconShell;
        private Grid shellPanel;
        private UIElement metricsPlaceholder;
        private bool metricsGridBuilt;
        private TextBlock titleBlock;
        private TextBlock statusBlock;
        private TextBlock locationBlock;
        private TextBlock modeBlock;
        private TextBlock conditionBlock;
        private TextBlock nearTermBlock;
        private TextBlock temperatureBlock;
        private TextBlock feelsBlock;
        private TextBlock iconBlock;
        private TextBlock rainValueBlock;
        private TextBlock dayRainValueBlock;
        private TextBlock probabilityValueBlock;
        private TextBlock humidityValueBlock;
        private TextBlock cloudValueBlock;
        private TextBlock pressureValueBlock;
        private TextBlock windValueBlock;
        private TextBlock gustValueBlock;
        private TextBlock updatedBlock;
        private TextBlock errorBlock;
        private Button creditButton;
        private StackPanel footerPanel;
        private Button refreshButton;
        private Button settingsButton;
        private Button collapseButton;
        private Button closeButton;
        private Border settingsPanel;
        private Border drawerHandle;
        private TextBlock settingsTitleBlock;
        private TextBlock provinceLabelBlock;
        private TextBlock cityLabelBlock;
        private TextBlock districtLabelBlock;
        private TextBlock refreshLabelBlock;
        private TextBlock languageLabelBlock;
        private TextBlock forecastLabelBlock;
        private TextBlock startupLabelBlock;
        private ComboBox provinceCombo;
        private ComboBox cityCombo;
        private ComboBox districtCombo;
        private ComboBox refreshCombo;
        private ComboBox forecastSlotCombo;
        private Button startupButton;
        private Button zhButton;
        private Button enButton;
        private readonly Dictionary<string, TextBlock> metricLabelBlocks = new Dictionary<string, TextBlock>(StringComparer.OrdinalIgnoreCase);
        private bool settingsOpen;
        private bool drawerExpanded = true;
        private string drawerEdge = "Right";
        private bool settingsRequested;
        private bool updatingSettingsControls;
        private string language = "zh";
        private bool titleUsesDefaultLabel = true;
        private string sessionId;
        private string commandFilePath;
        private int commandCounter;
        private object[] catalogProvinces = new object[0];
        private readonly Dictionary<string, PendingCommand> pendingCommands = new Dictionary<string, PendingCommand>(StringComparer.OrdinalIgnoreCase);
        private DispatcherTimer commandTimeoutTimer;
        private Dictionary<string, object> pendingSettingsPayload;
        private Dictionary<string, object> pendingCatalogPayload;
        private bool settingsPanelBuilt;
        private DispatcherTimer holdOpenTimer;
        private const string StartupRegistryValueName = "PaperWeatherWidget";
        private const double ExpandedWidth = 386;
        private const double ExpandedHeight = 512;
        private const double ExpandedSettingsHeight = 790;
        private const double CollapsedWidth = 28;
        private const double CollapsedHeight = 92;
        private const double SpaceXs = 4;
        private const double SpaceSm = 8;
        private const double SpaceMd = 12;
        private const double SpaceLg = 16;
        private const double RadiusSm = 8;
        private const double RadiusMd = 10;
        private const double RadiusLg = 12;
        private const double RadiusXl = 14;
        private const double RadiusIcon = 20;
        private const double TitleRowHeight = 32;
        private const double ChromeButtonSize = 32;
        private const string ColorInk = "#2F2C25";
        private const string ColorMuted = "#6F6B60";
        private const string ColorLabel = "#8A867A";
        private const string ColorAccent = "#D97757";
        private const string ColorDanger = "#B5473C";
        private const string ColorRain = "#D97757";
        private const string ColorPaper = "#FFFAF9F5";
        private const string ColorWarm = "#FFF3F1EA";
        private const string ColorAlert = "#FFF3DED5";
        private const string ColorSurface = "#FFFFFFFF";
        private const string ColorShell = "#EEFFFFFF";
        private const string ColorBorder = "#33D8D4C8";
        private const int SnapshotStaleHours = 24;
        private bool snapshotWeatherVisible;
        private bool liveWeatherApplied;
        private string snapshotWeatherLocationKey;
        private bool snapshotWeatherStale;
        private DateTime? snapshotWeatherSavedAtUtc;
        private string snapshotWeatherSource;
        private SnapshotDisplayState snapshotDisplayState;


        private sealed class PendingCommand
        {
            public string Id { get; set; }
            public string Type { get; set; }
            public DateTime SentUtc { get; set; }
            public int TimeoutMs { get; set; }
        }
        private sealed class SnapshotDiagnostics
        {
            public bool Exists { get; set; }
            public bool Valid { get; set; }
            public bool Stale { get; set; }
            public bool MatchesCurrentLocation { get; set; }
            public string Path { get; set; }
            public int? Schema { get; set; }
            public DateTime? SavedAtUtc { get; set; }
            public long? AgeSeconds { get; set; }
            public string Source { get; set; }
            public bool Fixture { get; set; }
            public string LocationKey { get; set; }
            public string LocationLabel { get; set; }
            public string SkipReason { get; set; }
            public Dictionary<string, object> Envelope { get; set; }
        }


        public sealed class ComboItem
        {
            public string Key { get; set; }
            public string Text { get; set; }
            public object Data { get; set; }
            public int Seconds { get; set; }

            public override string ToString()
            {
                return Text ?? Key ?? String.Empty;
            }
        }
        private sealed class ConditionVisual
        {
            public string Key { get; set; }
            public string Icon { get; set; }
            public string IconColor { get; set; }
            public string GradientMiddle { get; set; }
        }

        private sealed class ConditionRenderModel
        {
            public int? WeatherCode { get; set; }
            public int? IsDay { get; set; }
            public double? Rain { get; set; }
            public string Mode { get; set; }
            public string NearTerm { get; set; }
        }

        private sealed class WeatherMetrics
        {
            public double? Temperature { get; set; }
            public double? FeelsLike { get; set; }
            public double? Rain { get; set; }
            public double? TodayRain { get; set; }
            public double? RainProbability { get; set; }
            public double? Humidity { get; set; }
            public double? Cloud { get; set; }
            public double? Pressure { get; set; }
            public double? Wind { get; set; }
            public double? Gust { get; set; }

            public static WeatherMetrics Empty()
            {
                return new WeatherMetrics();
            }
        }

        private sealed class WeatherRenderModel
        {
            public string Title { get; set; }
            public bool TitleUsesDefaultLabel { get; set; }
            public string Location { get; set; }
            public string Updated { get; set; }
            public ConditionRenderModel Condition { get; set; }
            public WeatherMetrics Metrics { get; set; }
        }
        public MainWindow(string[] args)
        {
            StartupBenchmark.TraceLauncher("MainWindow ctor start");
            options = LauncherOptions.Parse(args);
            json = new JavaScriptSerializer();
            json.MaxJsonLength = Int32.MaxValue;

            StartupBenchmark.TraceLauncher("MainWindow InitializeComponent start");
            StartupBenchmark.TraceLauncher("MainWindow InitializeComponent end");
            StartupBenchmark.TraceLauncher("ConfigureWindow start");
            ConfigureWindow();
            StartupBenchmark.TraceLauncher("ConfigureWindow end");
            StartupBenchmark.TraceLauncher("LoadWindowState start");
            LoadWindowPlacement();
            StartupBenchmark.TraceLauncher("LoadWindowState end");
            ApplyInitialLanguageFromDisk();
            StartupBenchmark.TraceLauncher("BuildSkeleton start");
            BuildSkeleton();
            StartupBenchmark.TraceLauncher("BuildSkeleton end");
            ApplyInitialSettingsFromDisk();
            SetLoadingState();

            ContentRendered += OnContentRendered;
            Closing += OnClosing;
        }

        private void ConfigureWindow()
        {
            Title = "Paper Weather Widget";
            Width = ExpandedWidth;
            Height = ExpandedHeight;
            SizeToContent = SizeToContent.Manual;
            WindowStartupLocation = WindowStartupLocation.Manual;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;
            ShowInTaskbar = false;
            Topmost = options.Topmost;
            var workArea = SystemParameters.WorkArea;
            Left = Math.Max(workArea.Left, workArea.Right - Width - 24);
            Top = Math.Max(workArea.Top, workArea.Top + 24);
        }

        private void BuildSkeleton()
        {
            rootBorder = new Border();
            rootBorder.CornerRadius = new CornerRadius(RadiusXl);
            rootBorder.Padding = new Thickness(SpaceMd);
            rootBorder.ClipToBounds = true;
            rootBorder.Background = MakeGradient(ColorPaper, ColorWarm, ColorSurface);
            rootBorder.BorderBrush = BrushFrom(ColorBorder);
            rootBorder.BorderThickness = new Thickness(1);
            rootBorder.ContextMenu = BuildWindowContextMenu();
            rootBorder.MouseLeftButtonDown += OnWindowBorderMouseLeftButtonDown;

            var rootLayer = new Grid();
            rootBorder.Child = rootLayer;

            shellPanel = new Grid();
            rootLayer.Children.Add(shellPanel);
            rootLayer.Children.Add(BuildDragEdgeZone(HorizontalAlignment.Left, VerticalAlignment.Stretch, SpaceMd, Double.NaN));
            rootLayer.Children.Add(BuildDragEdgeZone(HorizontalAlignment.Right, VerticalAlignment.Stretch, SpaceMd, Double.NaN));
            rootLayer.Children.Add(BuildDragEdgeZone(HorizontalAlignment.Stretch, VerticalAlignment.Bottom, Double.NaN, SpaceMd));
            shellPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            shellPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            shellPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            shellPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            shellPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            shellPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            shellPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            shellPanel.Children.Add(BuildTitleRow());
            shellPanel.Children.Add(BuildLocationStrip());
            shellPanel.Children.Add(BuildConditionCard());
            metricsPlaceholder = new Grid();
            Grid.SetRow(metricsPlaceholder, 3);
            shellPanel.Children.Add(metricsPlaceholder);
            shellPanel.Children.Add(BuildSettingsPanel());
            shellPanel.Children.Add(BuildFooter());

            Content = drawerExpanded ? (object)rootBorder : BuildDrawerHandle();
        }

        private Border BuildDragEdgeZone(HorizontalAlignment horizontal, VerticalAlignment vertical, double width, double height)
        {
            var zone = new Border();
            zone.Background = Brushes.Transparent;
            zone.Cursor = Cursors.SizeAll;
            zone.HorizontalAlignment = horizontal;
            zone.VerticalAlignment = vertical;
            if (!Double.IsNaN(width)) { zone.Width = width; }
            if (!Double.IsNaN(height)) { zone.Height = height; }
            Panel.SetZIndex(zone, 20);
            zone.MouseLeftButtonDown += OnDragHandleMouseDown;
            return zone;
        }
        private void ApplyDeferredVisualEffects()
        {
            if (rootBorder != null && rootBorder.Effect == null)
            {
                rootBorder.Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = ColorFrom("#D8D4C8"),
                    BlurRadius = 18,
                    ShadowDepth = 4,
                    Opacity = 0.18
                };
            }
            if (iconShell != null && iconShell.Effect == null)
            {
                iconShell.Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = ColorFrom("#F3DED5"),
                    BlurRadius = 14,
                    ShadowDepth = 1,
                    Opacity = 0.18
                };
            }
        }
        private UIElement BuildTitleRow()
        {
            var row = new Grid();
            row.Height = TitleRowHeight;
            row.VerticalAlignment = VerticalAlignment.Center;
            row.SnapsToDevicePixels = true;
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetRow(row, 0);

            var titleStack = new Grid();
            titleStack.Height = TitleRowHeight;
            titleStack.VerticalAlignment = VerticalAlignment.Center;
            titleStack.Cursor = Cursors.SizeAll;
            titleStack.MouseLeftButtonDown += OnDragHandleMouseDown;
            titleBlock = Text(Ui("Weather"), 17, ColorInk, FontWeights.SemiBold);
            titleBlock.VerticalAlignment = VerticalAlignment.Center;
            titleBlock.TextTrimming = System.Windows.TextTrimming.CharacterEllipsis;
            titleStack.Children.Add(titleBlock);
            row.Children.Add(titleStack);

            var statusBadge = new Border();
            statusBadge.CornerRadius = new CornerRadius(RadiusSm);
            statusBadge.Height = TitleRowHeight;
            statusBadge.VerticalAlignment = VerticalAlignment.Center;
            statusBadge.Padding = new Thickness(SpaceSm, 0, SpaceSm, 0);
            statusBadge.Margin = new Thickness(6, 0, 0, 0);
            statusBadge.Background = BrushFrom(ColorShell);
            statusBadge.BorderBrush = Brushes.Transparent;
            statusBadge.BorderThickness = new Thickness(0);
            statusBlock = Text(UiStateLabel("refreshing"), 10.5, ColorDanger, FontWeights.SemiBold);
            statusBlock.TextAlignment = TextAlignment.Center;
            statusBlock.Name = "SnapshotStatusText";
            statusBadge.Child = statusBlock;
            Grid.SetColumn(statusBadge, 1);
            row.Children.Add(statusBadge);

            refreshButton = ChromeButton(FormatRefreshSeconds(60), Ui("RefreshNow"));
            refreshButton.FontSize = 10.5;
            SetAutomationId(refreshButton, "RefreshNowCard");
            refreshButton.Click += delegate { ManualRefresh(); };
            Grid.SetColumn(refreshButton, 2);
            row.Children.Add(refreshButton);

            settingsButton = ChromeButton(((char)0x2699).ToString(), Ui("Settings"));
            settingsButton.FontSize = 15;
            SetAutomationId(settingsButton, "SettingsButton");
            settingsButton.Click += delegate { ToggleSettingsPanel(); };
            Grid.SetColumn(settingsButton, 3);
            row.Children.Add(settingsButton);

            collapseButton = ChromeButton(((char)0x2212).ToString(), Ui("DrawerCollapse"));
            collapseButton.FontSize = 18;
            SetAutomationId(collapseButton, "DrawerCollapseButton");
            collapseButton.Click += delegate { CollapseDrawer(); };
            Grid.SetColumn(collapseButton, 4);
            row.Children.Add(collapseButton);

            closeButton = ChromeButton(((char)0x00D7).ToString(), Ui("Exit"));
            closeButton.FontSize = 17;
            SetAutomationId(closeButton, "CloseButton");
            closeButton.Click += delegate { Close(); };
            Grid.SetColumn(closeButton, 5);
            row.Children.Add(closeButton);

            return row;
        }
        private UIElement BuildLocationStrip()
        {
            statusShell = new Border();
            statusShell.CornerRadius = new CornerRadius(RadiusSm);
            statusShell.Padding = new Thickness(SpaceSm + 1, SpaceXs + 1, SpaceSm + 1, SpaceXs + 1);
            statusShell.Margin = new Thickness(0, SpaceMd - 2, 0, SpaceSm);
            statusShell.Background = BrushFrom(ColorShell);
            statusShell.BorderBrush = BrushFrom(ColorBorder);
            statusShell.BorderThickness = new Thickness(1);
            statusShell.Cursor = Cursors.Hand;
            statusShell.Focusable = true;
            SetAutomationId(statusShell, "LocationTitle");
            statusShell.MouseLeftButtonUp += delegate { ToggleSettingsPanel(); };
            statusShell.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Enter || e.Key == Key.Space)
                {
                    e.Handled = true;
                    ToggleSettingsPanel();
                }
            };
            Grid.SetRow(statusShell, 1);

            locationBlock = Text(DefaultLocationLabel(), 11, ColorMuted, FontWeights.SemiBold);
            locationBlock.TextTrimming = System.Windows.TextTrimming.CharacterEllipsis;
            statusShell.Child = locationBlock;
            return statusShell;
        }

        private ContextMenu BuildWindowContextMenu()
        {
            var menu = new ContextMenu();
            var refreshItem = new MenuItem { Header = Ui("RefreshNow") };
            refreshItem.Click += delegate { ManualRefresh(); };
            var settingsItem = new MenuItem { Header = Ui("Settings") };
            settingsItem.Click += delegate { ToggleSettingsPanel(); };
            var closeItem = new MenuItem { Header = Ui("Exit") };
            closeItem.Click += delegate { Close(); };
            menu.Items.Add(refreshItem);
            menu.Items.Add(settingsItem);
            menu.Items.Add(new Separator());
            menu.Items.Add(closeItem);
            return menu;
        }
        private UIElement BuildConditionCard()
        {
            conditionCard = new Border();
            conditionCard.CornerRadius = new CornerRadius(RadiusLg);
            conditionCard.Padding = new Thickness(SpaceMd);
            conditionCard.Margin = new Thickness(0, 0, 0, SpaceSm - 2);
            conditionCard.Background = BrushFrom(ColorSurface);
            conditionCard.BorderBrush = BrushFrom("#55E8E6DC");
            conditionCard.BorderThickness = new Thickness(1);
            Grid.SetRow(conditionCard, 2);

            var layout = new Grid();
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(112) });
            conditionCard.Child = layout;

            var textStack = new StackPanel();
            modeBlock = Text(Ui("Now"), 10.5, ColorLabel, FontWeights.SemiBold);
            conditionBlock = Text(UiStateLabel("refreshing"), 20, ColorInk, FontWeights.SemiBold);
            conditionBlock.Margin = new Thickness(0, SpaceXs, 0, 0);
            conditionBlock.TextWrapping = TextWrapping.Wrap;
            nearTermBlock = Text(UiStateLabel("refreshing"), 11, ColorMuted, FontWeights.Normal);
            nearTermBlock.Margin = new Thickness(0, SpaceSm - 2, 0, 0);
            nearTermBlock.TextTrimming = System.Windows.TextTrimming.CharacterEllipsis;
            temperatureBlock = Text(PlainMetricPlaceholder(), 42, ColorInk, FontWeights.SemiBold);
            temperatureBlock.Margin = new Thickness(0, SpaceSm, 0, 0);
            feelsBlock = Text(PlainMetricPlaceholder(), 11, ColorLabel, FontWeights.SemiBold);
            textStack.Children.Add(modeBlock);
            textStack.Children.Add(conditionBlock);
            textStack.Children.Add(nearTermBlock);
            textStack.Children.Add(temperatureBlock);
            textStack.Children.Add(feelsBlock);
            layout.Children.Add(textStack);

            iconShell = new Border();
            iconShell.Width = 96;
            iconShell.Height = 96;
            iconShell.CornerRadius = new CornerRadius(RadiusIcon + 4);
            iconShell.HorizontalAlignment = HorizontalAlignment.Center;
            iconShell.VerticalAlignment = VerticalAlignment.Center;
            iconShell.Background = MakeGradient("#FFFFFDFC", ColorWarm, ColorSurface);
            iconBlock = Text(((char)0x2601).ToString(), 40, ColorAccent, FontWeights.Normal);
            iconBlock.HorizontalAlignment = HorizontalAlignment.Center;
            iconBlock.VerticalAlignment = VerticalAlignment.Center;
            iconShell.Child = iconBlock;
            Grid.SetColumn(iconShell, 1);
            layout.Children.Add(iconShell);

            return conditionCard;
        }

        private UIElement BuildMetricsGrid()
        {
            var grid = new Grid();
            grid.Margin = new Thickness(0, 0, 0, 0);
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(grid, 3);

            rainValueBlock = AddMetric(grid, 0, 0, "RainNow");
            dayRainValueBlock = AddMetric(grid, 0, 1, "TodayRain");
            probabilityValueBlock = AddMetric(grid, 1, 0, "Probability");
            humidityValueBlock = AddMetric(grid, 1, 1, "Humidity");
            cloudValueBlock = AddMetric(grid, 2, 0, "Cloud");
            pressureValueBlock = AddMetric(grid, 2, 1, "Pressure");
            windValueBlock = AddMetric(grid, 3, 0, "Wind");
            gustValueBlock = AddMetric(grid, 3, 1, "Gust");
            return grid;
        }

        private void EnsureMetricsGridCreated()
        {
            if (metricsGridBuilt || shellPanel == null) { return; }
            metricsGridBuilt = true;
            if (metricsPlaceholder != null)
            {
                shellPanel.Children.Remove(metricsPlaceholder);
                metricsPlaceholder = null;
            }
            shellPanel.Children.Add(BuildMetricsGrid());
            SetLoadingState();
        }
        private UIElement BuildSettingsPanel()
        {
            settingsPanel = new Border();
            settingsPanel.CornerRadius = new CornerRadius(RadiusLg);
            settingsPanel.Padding = new Thickness(SpaceMd - 2);
            settingsPanel.Margin = new Thickness(0, 0, 0, SpaceSm);
            settingsPanel.Background = BrushFrom(ColorSurface);
            settingsPanel.BorderBrush = BrushFrom("#55E8E6DC");
            settingsPanel.BorderThickness = new Thickness(1);
            settingsPanel.Visibility = Visibility.Collapsed;
            Grid.SetRow(settingsPanel, 4);
            return settingsPanel;
        }

        private void EnsureSettingsPanelCreated()
        {
            if (settingsPanelBuilt) { return; }
            settingsPanelBuilt = true;

            var stack = new StackPanel();
            settingsPanel.Child = stack;
            settingsTitleBlock = Text(Ui("Settings"), 13, ColorInk, FontWeights.SemiBold);
            settingsTitleBlock.Margin = new Thickness(0, 0, 0, SpaceSm - 2);
            stack.Children.Add(settingsTitleBlock);

            var settingsGrid = new Grid();
            settingsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            settingsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            stack.Children.Add(settingsGrid);

            provinceCombo = SettingsCombo();
            cityCombo = SettingsCombo();
            districtCombo = SettingsCombo();
            refreshCombo = SettingsCombo();
            forecastSlotCombo = SettingsCombo();
            startupButton = SettingsActionButton(FormatStartupButtonContent(IsStartupEnabled()));
            SetAutomationId(startupButton, "StartupToggleButton");
            startupButton.Click += delegate { ToggleStartupFromUi(); };
            ApplyStartupButtonState(IsStartupEnabled());

            zhButton = ChromeButton("ZH", "Chinese");
            enButton = ChromeButton("EN", "English");
            zhButton.Width = 44;
            enButton.Width = 44;
            zhButton.Height = 30;
            enButton.Height = 30;
            zhButton.Margin = new Thickness(0);
            enButton.Margin = new Thickness(SpaceXs, 0, 0, 0);
            zhButton.Click += delegate { SetLanguageFromUi("zh"); };
            enButton.Click += delegate { SetLanguageFromUi("en"); };
            var languageRow = new StackPanel { Orientation = Orientation.Horizontal };
            languageRow.Children.Add(zhButton);
            languageRow.Children.Add(enButton);

            provinceLabelBlock = AddSettingsCombo(settingsGrid, 0, 0, "Province", provinceCombo);
            cityLabelBlock = AddSettingsCombo(settingsGrid, 0, 1, "City", cityCombo);
            districtLabelBlock = AddSettingsCombo(settingsGrid, 1, 0, "District", districtCombo);
            refreshLabelBlock = AddSettingsCombo(settingsGrid, 1, 1, "Refresh", refreshCombo);
            forecastLabelBlock = AddSettingsCombo(settingsGrid, 2, 0, "Forecast", forecastSlotCombo);
            startupLabelBlock = AddSettingsControl(settingsGrid, 2, 1, "Startup", startupButton, false);
            languageLabelBlock = AddSettingsControl(settingsGrid, 3, 0, "Language", languageRow, true);
            provinceCombo.SelectionChanged += OnProvinceSelectionChanged;
            cityCombo.SelectionChanged += OnCitySelectionChanged;
            districtCombo.SelectionChanged += OnDistrictSelectionChanged;
            refreshCombo.SelectionChanged += OnRefreshSelectionChanged;
            forecastSlotCombo.SelectionChanged += OnForecastSlotSelectionChanged;

            ApplyLocalSettingsFallback();

            ApplyLanguageToVisibleText();
            if (pendingSettingsPayload != null) { ApplySettings(pendingSettingsPayload); }
            if (pendingCatalogPayload != null) { ApplyCatalog(pendingCatalogPayload); }
        }
        private TextBlock AddSettingsCombo(Grid grid, int row, int column, string label, ComboBox combo)
        {
            return AddSettingsControl(grid, row, column, label, combo, false);
        }

        private TextBlock AddSettingsControl(Grid grid, int row, int column, string label, UIElement control, bool spanColumns)
        {
            while (grid.RowDefinitions.Count <= row)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }
            var cell = new StackPanel();
            cell.Margin = spanColumns
                ? new Thickness(0, 0, 0, SpaceSm - 2)
                : new Thickness(column == 0 ? 0 : SpaceXs, 0, column == 0 ? SpaceXs : 0, SpaceSm - 2);
            var labelBlock = Text(Ui(label), 10, ColorLabel, FontWeights.SemiBold);
            labelBlock.Margin = new Thickness(0, 0, 0, SpaceXs);
            cell.Children.Add(labelBlock);
            cell.Children.Add(control);
            Grid.SetRow(cell, row);
            Grid.SetColumn(cell, column);
            if (spanColumns) { Grid.SetColumnSpan(cell, 2); }
            grid.Children.Add(cell);
            return labelBlock;
        }
        private TextBlock AddSettingsCombo(Panel parent, string label, ComboBox combo)
        {
            var labelBlock = Text(Ui(label), 10, ColorLabel, FontWeights.SemiBold);
            labelBlock.Margin = new Thickness(0, SpaceXs, 0, SpaceXs);
            parent.Children.Add(labelBlock);
            parent.Children.Add(combo);
            return labelBlock;
        }

        private ComboBox SettingsCombo()
        {
            var combo = new ComboBox();
            combo.Height = 32;
            combo.Margin = new Thickness(0);
            combo.Padding = new Thickness(SpaceMd - 2, 0, SpaceMd - 2, 0);
            combo.FontSize = 12;
            combo.VerticalContentAlignment = VerticalAlignment.Center;
            combo.DisplayMemberPath = "Text";
            combo.Foreground = BrushFrom(ColorInk);
            combo.Background = BrushFrom(ColorPaper);
            combo.BorderBrush = BrushFrom(ColorBorder);
            combo.BorderThickness = new Thickness(1);
            combo.MaxDropDownHeight = 220;
            combo.Template = SettingsComboTemplate();
            combo.ItemContainerStyle = SettingsComboItemStyle();
            return combo;
        }

        private static ControlTemplate SettingsComboTemplate()
        {
            var template = new ControlTemplate(typeof(ComboBox));
            var grid = new FrameworkElementFactory(typeof(Grid));

            var root = new FrameworkElementFactory(typeof(Border));
            root.Name = "ComboRoot";
            root.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            root.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            root.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
            root.SetValue(Border.CornerRadiusProperty, new CornerRadius(RadiusSm));
            grid.AppendChild(root);

            var selected = new FrameworkElementFactory(typeof(TextBlock));
            selected.SetBinding(TextBlock.TextProperty, new Binding("Text") { RelativeSource = RelativeSource.TemplatedParent });
            selected.SetValue(TextBlock.ForegroundProperty, new TemplateBindingExtension(Control.ForegroundProperty));
            selected.SetValue(TextBlock.FontSizeProperty, new TemplateBindingExtension(Control.FontSizeProperty));
            selected.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
            selected.SetValue(TextBlock.TextTrimmingProperty, System.Windows.TextTrimming.CharacterEllipsis);
            selected.SetValue(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Left);
            selected.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
            selected.SetValue(TextBlock.MarginProperty, new Thickness(SpaceMd - 2, 0, 32, 0));
            selected.SetValue(UIElement.IsHitTestVisibleProperty, false);
            grid.AppendChild(selected);

            var arrow = new FrameworkElementFactory(typeof(TextBlock));
            arrow.SetValue(TextBlock.TextProperty, "\u2304");
            arrow.SetValue(TextBlock.FontSizeProperty, 13.0);
            arrow.SetValue(TextBlock.ForegroundProperty, BrushFrom(ColorMuted));
            arrow.SetValue(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Right);
            arrow.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
            arrow.SetValue(TextBlock.MarginProperty, new Thickness(0, 0, SpaceMd - 2, 2));
            arrow.SetValue(UIElement.IsHitTestVisibleProperty, false);
            grid.AppendChild(arrow);

            var toggle = new FrameworkElementFactory(typeof(ToggleButton));
            toggle.SetValue(Control.TemplateProperty, SettingsComboToggleButtonTemplate());
            toggle.SetValue(Control.BackgroundProperty, Brushes.Transparent);
            toggle.SetValue(Control.BorderThicknessProperty, new Thickness(0));
            toggle.SetValue(UIElement.FocusableProperty, false);
            toggle.SetBinding(ToggleButton.IsCheckedProperty, new Binding("IsDropDownOpen") { RelativeSource = RelativeSource.TemplatedParent, Mode = BindingMode.TwoWay });
            grid.AppendChild(toggle);

            var popup = new FrameworkElementFactory(typeof(Popup));
            popup.Name = "PART_Popup";
            popup.SetValue(Popup.AllowsTransparencyProperty, true);
            popup.SetValue(Popup.FocusableProperty, false);
            popup.SetValue(Popup.PlacementProperty, PlacementMode.Bottom);
            popup.SetBinding(Popup.IsOpenProperty, new Binding("IsDropDownOpen") { RelativeSource = RelativeSource.TemplatedParent, Mode = BindingMode.TwoWay });

            var popupBorder = new FrameworkElementFactory(typeof(Border));
            popupBorder.SetValue(Border.BackgroundProperty, BrushFrom(ColorSurface));
            popupBorder.SetValue(Border.BorderBrushProperty, BrushFrom("#66D8D4C8"));
            popupBorder.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            popupBorder.SetValue(Border.CornerRadiusProperty, new CornerRadius(RadiusSm));
            popupBorder.SetValue(Border.MarginProperty, new Thickness(0, SpaceXs, 0, 0));
            popupBorder.SetBinding(FrameworkElement.MinWidthProperty, new Binding("ActualWidth") { RelativeSource = RelativeSource.TemplatedParent });

            var scroll = new FrameworkElementFactory(typeof(ScrollViewer));
            scroll.SetValue(ScrollViewer.MaxHeightProperty, 220.0);
            scroll.SetValue(ScrollViewer.CanContentScrollProperty, true);
            scroll.AppendChild(new FrameworkElementFactory(typeof(ItemsPresenter)));
            popupBorder.AppendChild(scroll);
            popup.AppendChild(popupBorder);
            grid.AppendChild(popup);

            template.VisualTree = grid;

            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Border.BackgroundProperty, BrushFrom(ColorSurface), "ComboRoot"));
            hover.Setters.Add(new Setter(Border.BorderBrushProperty, BrushFrom("#66D8D4C8"), "ComboRoot"));
            template.Triggers.Add(hover);

            var open = new Trigger { Property = ComboBox.IsDropDownOpenProperty, Value = true };
            open.Setters.Add(new Setter(Border.BackgroundProperty, BrushFrom(ColorSurface), "ComboRoot"));
            open.Setters.Add(new Setter(Border.BorderBrushProperty, BrushFrom(ColorAccent), "ComboRoot"));
            template.Triggers.Add(open);

            return template;
        }

        private static ControlTemplate SettingsComboToggleButtonTemplate()
        {
            var template = new ControlTemplate(typeof(ToggleButton));
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            template.VisualTree = border;
            return template;
        }

        private Style SettingsComboItemStyle()
        {
            var style = new Style(typeof(ComboBoxItem));
            style.Setters.Add(new Setter(Control.MinHeightProperty, 31.0));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(SpaceMd - 1, SpaceSm - 2, SpaceMd - 1, SpaceSm - 2)));
            style.Setters.Add(new Setter(Control.ForegroundProperty, BrushFrom(ColorInk)));
            style.Setters.Add(new Setter(Control.BackgroundProperty, BrushFrom(ColorSurface)));
            style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
            style.Setters.Add(new Setter(Control.TemplateProperty, SettingsComboItemTemplate()));
            return style;
        }

        private static ControlTemplate SettingsComboItemTemplate()
        {
            var template = new ControlTemplate(typeof(ComboBoxItem));
            var root = new FrameworkElementFactory(typeof(Border));
            root.Name = "ComboItemRoot";
            root.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            root.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
            root.SetValue(Border.CornerRadiusProperty, new CornerRadius(RadiusSm - 2));
            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Left);
            content.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            root.AppendChild(content);
            template.VisualTree = root;

            var hover = new Trigger { Property = ComboBoxItem.IsHighlightedProperty, Value = true };
            hover.Setters.Add(new Setter(Border.BackgroundProperty, BrushFrom(ColorWarm), "ComboItemRoot"));
            template.Triggers.Add(hover);

            var selected = new Trigger { Property = Selector.IsSelectedProperty, Value = true };
            selected.Setters.Add(new Setter(Border.BackgroundProperty, BrushFrom(ColorAlert), "ComboItemRoot"));
            template.Triggers.Add(selected);

            return template;
        }
        private Button SettingsActionButton(string text)
        {
            var button = new Button();
            button.Height = 30;
            button.MinHeight = 30;
            button.Margin = new Thickness(0);
            button.Padding = new Thickness(SpaceMd, 0, SpaceMd, 0);
            button.Content = text;
            button.FontSize = 12;
            button.FontWeight = FontWeights.SemiBold;
            button.Foreground = BrushFrom(ColorInk);
            button.Background = BrushFrom(ColorPaper);
            button.BorderBrush = Brushes.Transparent;
            button.BorderThickness = new Thickness(0);
            button.HorizontalContentAlignment = HorizontalAlignment.Center;
            button.FocusVisualStyle = null;
            button.Template = SettingsActionButtonTemplate();
            button.Cursor = Cursors.Hand;
            return button;
        }

        private static ControlTemplate SettingsActionButtonTemplate()
        {
            var template = new ControlTemplate(typeof(Button));
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(15));
            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            content.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            content.SetValue(ContentPresenter.MarginProperty, new Thickness(0));
            border.AppendChild(content);
            template.VisualTree = border;
            return template;
        }
        private UIElement BuildFooter()
        {
            footerPanel = new StackPanel();
            var footer = footerPanel;
            footer.Margin = new Thickness(0, SpaceSm - 2, 0, 0);
            Grid.SetRow(footer, 6);

            updatedBlock = Text(UiStateLabel("refreshing"), 11, ColorLabel, FontWeights.Normal);
            updatedBlock.Name = "SnapshotUpdatedText";
            updatedBlock.TextTrimming = System.Windows.TextTrimming.CharacterEllipsis;
            errorBlock = Text(String.Empty, 10, ColorDanger, FontWeights.Normal);
            errorBlock.Name = "SnapshotErrorText";
            errorBlock.Margin = new Thickness(0, 3, 0, 0);
            errorBlock.TextTrimming = System.Windows.TextTrimming.CharacterEllipsis;
            creditButton = BuildCreditButton();
            creditButton.Visibility = Visibility.Collapsed;
            footer.Children.Add(updatedBlock);
            footer.Children.Add(errorBlock);
            footer.Children.Add(creditButton);
            return footer;
        }

        private Button BuildCreditButton()
        {
            var button = new Button();
            button.Height = 24;
            button.MinHeight = 24;
            button.Padding = new Thickness(0);
            button.Margin = new Thickness(SpaceSm + 1, SpaceXs, SpaceSm + 1, 0);
            button.FontSize = 12;
            button.FontWeight = FontWeights.SemiBold;
            button.Foreground = BrushFrom(ColorMuted);
            button.Background = Brushes.Transparent;
            button.BorderBrush = Brushes.Transparent;
            button.BorderThickness = new Thickness(0);
            button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            button.FocusVisualStyle = null;
            button.Cursor = Cursors.Hand;
            button.Template = CreditButtonTemplate();
            button.Click += delegate { OpenCreditLink(); };
            SetAutomationId(button, "CreditLinkButton");
            UpdateCreditButtonContent();
            return button;
        }

        private void UpdateCreditButtonContent()
        {
            if (creditButton == null) { return; }
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var label = Text(Ui("CreditPrefix") + " M47E1/paper-weather-widget", 12, ColorMuted, FontWeights.SemiBold);
            label.TextTrimming = System.Windows.TextTrimming.CharacterEllipsis;
            var arrow = Text(((char)0x2197).ToString(), 15, ColorInk, FontWeights.SemiBold);
            arrow.Margin = new Thickness(SpaceMd, 0, 0, 0);
            Grid.SetColumn(arrow, 1);
            row.Children.Add(label);
            row.Children.Add(arrow);
            creditButton.Content = row;
            creditButton.ToolTip = "https://github.com/M47E1/paper-weather-widget";
        }

        private void OpenCreditLink()
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://github.com/M47E1/paper-weather-widget",
                    UseShellExecute = true
                });
            }
            catch
            {
            }
        }
        private static ControlTemplate CreditButtonTemplate()
        {
            var template = new ControlTemplate(typeof(Button));
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(RadiusSm));
            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
            content.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            content.SetValue(ContentPresenter.MarginProperty, new Thickness(0));
            border.AppendChild(content);
            template.VisualTree = border;
            return template;
        }

        private TextBlock AddMetric(Grid grid, int row, int column, string label)
        {
            var card = new Border();
            card.CornerRadius = new CornerRadius(RadiusSm);
            card.Padding = new Thickness(SpaceSm + 1, SpaceXs + 1, SpaceSm + 1, SpaceXs + 1);
            card.Margin = new Thickness(column == 0 ? 0 : SpaceXs, 0, column == 0 ? SpaceXs : 0, SpaceXs + 1);
            card.Background = BrushFrom("#FFFAF9F5");
            card.BorderBrush = BrushFrom("#44E8E6DC");
            card.BorderThickness = new Thickness(1);
            Grid.SetRow(card, row);
            Grid.SetColumn(card, column);

            var stack = new StackPanel();
            var labelBlock = Text(Ui(label), 10, ColorLabel, FontWeights.SemiBold);
            metricLabelBlocks[label] = labelBlock;
            var valueBlock = Text(PlainMetricPlaceholder(), 13, ColorInk, FontWeights.SemiBold);
            valueBlock.Margin = new Thickness(0, 2, 0, 0);
            stack.Children.Add(labelBlock);
            stack.Children.Add(valueBlock);
            card.Child = stack;
            grid.Children.Add(card);
            return valueBlock;
        }

        private Button ChromeButton(string text, string tooltip)
        {
            var button = new Button();
            button.Width = ChromeButtonSize;
            button.Height = ChromeButtonSize;
            button.MinWidth = ChromeButtonSize;
            button.MinHeight = ChromeButtonSize;
            button.VerticalAlignment = VerticalAlignment.Center;
            button.HorizontalContentAlignment = HorizontalAlignment.Center;
            button.VerticalContentAlignment = VerticalAlignment.Center;
            button.SnapsToDevicePixels = true;
            button.UseLayoutRounding = true;
            button.Margin = new Thickness(SpaceXs + 1, 0, 0, 0);
            button.Padding = new Thickness(0);
            button.Content = text;
            button.ToolTip = tooltip;
            button.FontWeight = FontWeights.SemiBold;
            button.FontSize = 13;
            button.Foreground = BrushFrom(ColorMuted);
            button.Background = Brushes.Transparent;
            button.BorderBrush = Brushes.Transparent;
            button.BorderThickness = new Thickness(0);
            button.FocusVisualStyle = null;
            button.Template = ChromeButtonTemplate();
            button.Cursor = Cursors.Hand;
            return button;
        }

        private static ControlTemplate ChromeButtonTemplate()
        {
            var template = new ControlTemplate(typeof(Button));
            var border = new FrameworkElementFactory(typeof(Border));
            border.Name = "ChromeButtonRoot";
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(RadiusSm));
            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            content.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(content);
            template.VisualTree = border;

            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Border.BackgroundProperty, BrushFrom(ColorShell), "ChromeButtonRoot"));
            template.Triggers.Add(hover);

            var pressed = new Trigger { Property = ButtonBase.IsPressedProperty, Value = true };
            pressed.Setters.Add(new Setter(Border.BackgroundProperty, BrushFrom(ColorWarm), "ChromeButtonRoot"));
            template.Triggers.Add(pressed);

            return template;
        }
        private static void SetAutomationId(DependencyObject control, string id)
        {
            if (control == null || String.IsNullOrWhiteSpace(id)) { return; }
            System.Windows.Automation.AutomationProperties.SetAutomationId(control, id);
        }

        private void OnProvinceSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (updatingSettingsControls) { return; }
            SyncComboText(provinceCombo);
            var item = provinceCombo.SelectedItem as ComboItem;
            if (item == null) { return; }
            PopulateCityCombo(item);
            ApplySelectedLocationFromUi();
        }

        private void OnCitySelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (updatingSettingsControls) { return; }
            SyncComboText(cityCombo);
            var item = cityCombo.SelectedItem as ComboItem;
            if (item == null) { return; }
            PopulateDistrictCombo(item);
            ApplySelectedLocationFromUi();
        }

        private void OnDistrictSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (updatingSettingsControls) { return; }
            SyncComboText(districtCombo);
            ApplySelectedLocationFromUi();
        }


        private static void SetComboSelection(ComboBox combo, ComboItem item)
        {
            if (combo == null) { return; }
            combo.SelectedItem = item;
            combo.Text = item == null ? String.Empty : NonEmpty(item.Text, item.Key);
        }

        private static void SyncComboText(ComboBox combo)
        {
            if (combo == null) { return; }
            var item = combo.SelectedItem as ComboItem;
            combo.Text = item == null ? String.Empty : NonEmpty(item.Text, item.Key);
        }
        private void ApplySelectedLocationFromUi()
        {
            var province = provinceCombo.SelectedItem as ComboItem;
            var city = cityCombo.SelectedItem as ComboItem;
            var district = districtCombo.SelectedItem as ComboItem;
            if (province == null || city == null || district == null) { return; }

            var payload = new Dictionary<string, object>();
            payload["provinceKey"] = province.Key;
            payload["cityKey"] = city.Key;
            payload["districtKey"] = district.Key;
            RenderLocation(FormatLocationLabel(province, city, district));
            ClearSnapshotForLocationChange();
            SetRefreshingState();
            SendCommand("setLocation", payload);
        }

        private static string FormatLocationLabel(ComboItem province, ComboItem city, ComboItem district)
        {
            return String.Format(
                CultureInfo.InvariantCulture,
                "{0} - {1} - {2}",
                NonEmpty(province == null ? null : province.Text, String.Empty),
                NonEmpty(city == null ? null : city.Text, String.Empty),
                NonEmpty(district == null ? null : district.Text, String.Empty));
        }
        private void OnRefreshSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (updatingSettingsControls) { return; }
            SyncComboText(refreshCombo);
            var item = refreshCombo.SelectedItem as ComboItem;
            if (item == null) { return; }
            var payload = new Dictionary<string, object>();
            payload["refreshSeconds"] = item.Seconds;
            SetRefreshButtonContent(item.Seconds);
            SendCommand("setRefreshInterval", payload);
            SetRefreshingState();
            SendCommand("manualRefresh", new Dictionary<string, object>());
        }

        private void OnForecastSlotSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (updatingSettingsControls) { return; }
            SyncComboText(forecastSlotCombo);
            var item = forecastSlotCombo.SelectedItem as ComboItem;
            if (item == null) { return; }
            var payload = new Dictionary<string, object>();
            payload["slotKey"] = item.Key;
            SetRefreshingState();
            SendCommand("setForecastSlot", payload);
        }

        private void SetLanguageFromUi(string newLanguage)
        {
            language = String.Equals(newLanguage, "en", StringComparison.OrdinalIgnoreCase) ? "en" : "zh";
            ApplyLanguageToVisibleText();
            LocalizeVisibleWeatherText();
            if (settingsPanelBuilt && pendingCatalogPayload != null) { ApplyCatalog(pendingCatalogPayload); }
            if (settingsPanelBuilt && pendingSettingsPayload != null) { PopulateForecastSlots(GetArray(pendingSettingsPayload, "forecastSlots"), GetString(pendingSettingsPayload, "forecastSlotKey")); }
            var payload = new Dictionary<string, object>();
            payload["language"] = language;
            SendCommand("setLanguage", payload);
            if (snapshotWeatherVisible && !liveWeatherApplied) { SetSnapshotDisplayState(snapshotDisplayState); }
        }
        private string FormatStartupButtonContent(bool enabled)
        {
            return enabled ? Ui("StartupOn") : Ui("StartupOff");
        }

        private void ApplyStartupButtonState(bool enabled)
        {
            if (startupButton == null) { return; }
            startupButton.Content = FormatStartupButtonContent(enabled);
            startupButton.Background = BrushFrom(enabled ? ColorAccent : ColorAlert);
            startupButton.Foreground = BrushFrom(enabled ? ColorSurface : ColorDanger);
            startupButton.BorderBrush = Brushes.Transparent;
            startupButton.BorderThickness = new Thickness(0);
        }

        private void ToggleStartupFromUi()
        {
            try
            {
                var enabled = !IsStartupEnabled();
                SetStartupEnabled(enabled);
                ApplyStartupButtonState(enabled);
                ClearError();
            }
            catch
            {
                RenderErrorState("failed");
            }
        }

        private static bool IsStartupEnabled()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", false))
                {
                    if (key == null) { return false; }
                    var value = Convert.ToString(key.GetValue(StartupRegistryValueName), CultureInfo.InvariantCulture);
                    return !String.IsNullOrWhiteSpace(value) && value.IndexOf(GetExecutablePath(), StringComparison.OrdinalIgnoreCase) >= 0;
                }
            }
            catch
            {
                return false;
            }
        }

        private static void SetStartupEnabled(bool enabled)
        {
            using (var key = enabled
                ? Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")
                : Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
            {
                if (key == null) { return; }
                if (enabled)
                {
                    key.SetValue(StartupRegistryValueName, "\"" + GetExecutablePath() + "\"");
                }
                else
                {
                    key.DeleteValue(StartupRegistryValueName, false);
                }
            }
        }

        private static string GetExecutablePath()
        {
            try { return Process.GetCurrentProcess().MainModule.FileName; }
            catch { return System.Reflection.Assembly.GetEntryAssembly().Location; }
        }
        private void ApplyLocalSettingsFallback()
        {
            var configuredRefreshSeconds = ReadConfiguredRefreshSeconds() ?? 60;
            if (refreshCombo != null && refreshCombo.Items.Count == 0)
            {
                PopulateRefreshOptions(DefaultRefreshOptions(), configuredRefreshSeconds);
            }
            SetRefreshButtonContent(configuredRefreshSeconds);
            if (forecastSlotCombo != null && forecastSlotCombo.Items.Count == 0)
            {
                PopulateForecastSlots(DefaultForecastSlots(), "Day0");
            }
            if (provinceCombo != null && provinceCombo.Items.Count == 0)
            {
                var localCatalog = LoadLocalRegionCatalogPayload();
                if (localCatalog != null) { ApplyCatalog(localCatalog); }
            }
            var keys = ReadConfiguredLocationKey().Split('|');
            if (keys.Length == 3)
            {
                SelectCatalogKeys(keys[0], keys[1], keys[2]);
            }
        }

        private object[] DefaultRefreshOptions()
        {
            return new object[]
            {
                NewRefreshOption(60, "1 min"),
                NewRefreshOption(3600, "1 hour"),
                NewRefreshOption(86400, "1 day")
            };
        }

        private object[] DefaultForecastSlots()
        {
            var slots = new List<object>();
            for (var day = 0; day < 14; day++)
            {
                slots.Add(NewForecastSlot("Day" + day.ToString(CultureInfo.InvariantCulture), ForecastDayLabel(day), day == 0));
            }
            foreach (var hour in new[] { 1, 3, 6, 12, 24 })
            {
                slots.Add(NewForecastSlot("Hour+" + hour.ToString(CultureInfo.InvariantCulture) + "h", "+" + hour.ToString(CultureInfo.InvariantCulture) + "h", false));
            }
            return slots.ToArray();
        }

        private string ForecastDayLabel(int dayOffset)
        {
            var zh = String.Equals(language, "zh", StringComparison.OrdinalIgnoreCase);
            if (dayOffset <= 0) { return zh ? "\u4eca\u5929" : "Today"; }
            if (dayOffset == 1) { return zh ? "\u660e\u5929" : "Tomorrow"; }
            if (dayOffset == 2) { return zh ? "\u540e\u5929" : "+2d"; }
            return zh ? "+" + dayOffset.ToString(CultureInfo.InvariantCulture) + "\u5929" : "+" + dayOffset.ToString(CultureInfo.InvariantCulture) + "d";
        }

        private static Dictionary<string, object> NewForecastSlot(string key, string label, bool selected)
        {
            var item = new Dictionary<string, object>();
            item["key"] = key;
            item["label"] = label;
            item["selected"] = selected;
            return item;
        }

        private Dictionary<string, object> LoadLocalRegionCatalogPayload()
        {
            foreach (var path in LocalRegionCatalogPaths())
            {
                try
                {
                    if (String.IsNullOrWhiteSpace(path) || !File.Exists(path)) { continue; }
                    var parsed = json.DeserializeObject(File.ReadAllText(path, Encoding.UTF8)) as Dictionary<string, object>;
                    if (parsed != null && GetArray(parsed, "provinces").Length > 0) { return parsed; }
                }
                catch
                {
                }
            }
            return null;
        }

        private static IEnumerable<string> LocalRegionCatalogPaths()
        {
            var bundledCatalog = BundledRuntime.TryGetCatalogPath();
            if (!String.IsNullOrWhiteSpace(bundledCatalog)) { yield return bundledCatalog; }
            var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            var repoRoot = LauncherPaths.FindRepositoryRoot();
            yield return Path.Combine(baseDirectory, "ChinaRegionCatalog.json");
            yield return Path.Combine(repoRoot, "ChinaRegionCatalog.json");
            yield return Path.Combine(repoRoot, "src", "worker", "ChinaRegionCatalog.json");
            yield return Path.Combine(Path.GetDirectoryName(repoRoot) ?? String.Empty, "worker", "ChinaRegionCatalog.json");
        }
        private void ApplyInitialLanguageFromDisk()
        {
            var configuredLanguage = ReadConfiguredLanguage();
            if (!String.IsNullOrWhiteSpace(configuredLanguage))
            {
                language = configuredLanguage;
            }
        }

        private string ReadConfiguredLanguage()
        {
            try
            {
                var settingsPath = LauncherPaths.FindSettingsFile();
                if (!File.Exists(settingsPath)) { return String.Empty; }
                var settings = json.DeserializeObject(File.ReadAllText(settingsPath, Encoding.UTF8)) as Dictionary<string, object>;
                var configuredLanguage = GetString(settings, "Language");
                return String.Equals(configuredLanguage, "en", StringComparison.OrdinalIgnoreCase) ? "en" : "zh";
            }
            catch
            {
                return String.Empty;
            }
        }
        private void ApplyInitialSettingsFromDisk()
        {
            var refreshSeconds = ReadConfiguredRefreshSeconds();
            if (refreshSeconds.HasValue) { SetRefreshButtonContent(refreshSeconds.Value); }
        }

        private int? ReadConfiguredRefreshSeconds()
        {
            try
            {
                var settingsPath = LauncherPaths.FindSettingsFile();
                if (!File.Exists(settingsPath)) { return null; }
                var settings = json.DeserializeObject(File.ReadAllText(settingsPath, Encoding.UTF8)) as Dictionary<string, object>;
                return GetInt(settings, "RefreshSeconds");
            }
            catch
            {
                return null;
            }
        }
        private void ApplySettings(Dictionary<string, object> payload)
        {
            if (payload == null) { return; }
            pendingSettingsPayload = payload;
            language = String.Equals(GetString(payload, "language"), "en", StringComparison.OrdinalIgnoreCase) ? "en" : "zh";
            ApplyLanguageToVisibleText();
            var label = GetString(payload, "locationLabel");
            if (!String.IsNullOrWhiteSpace(label)) { RenderLocation(LocalizeLocationDisplay(label)); }
            LocalizeVisibleWeatherText();
            var selectedRefreshSeconds = GetInt(payload, "refreshSeconds");
            if (selectedRefreshSeconds.HasValue) { SetRefreshButtonContent(selectedRefreshSeconds.Value); }
            if (!settingsPanelBuilt) { return; }
            PopulateRefreshOptions(GetArray(payload, "refreshOptions"), selectedRefreshSeconds);
            PopulateForecastSlots(GetArray(payload, "forecastSlots"), GetString(payload, "forecastSlotKey"));
            SelectCatalogKeys(GetString(payload, "provinceKey"), GetString(payload, "cityKey"), GetString(payload, "districtKey"));
        }

        private void ApplyCatalog(Dictionary<string, object> payload)
        {
            if (payload == null) { return; }
            pendingCatalogPayload = payload;
            catalogProvinces = GetArray(payload, "provinces");
            if (!settingsPanelBuilt) { return; }
            PopulateProvinceCombo();
            if (pendingSettingsPayload != null)
            {
                SelectCatalogKeys(GetString(pendingSettingsPayload, "provinceKey"), GetString(pendingSettingsPayload, "cityKey"), GetString(pendingSettingsPayload, "districtKey"));
            }
        }

        private void PopulateRefreshOptions(object[] options, int? selectedSeconds)
        {
            updatingSettingsControls = true;
            try
            {
                refreshCombo.Items.Clear();
                if (options == null || options.Length == 0)
                {
                    options = new object[] { NewRefreshOption(60, "1 min"), NewRefreshOption(3600, "1 hour"), NewRefreshOption(86400, "1 day") };
                }
                foreach (var entry in options)
                {
                    var dict = entry as Dictionary<string, object>;
                    var seconds = GetInt(dict, "seconds") ?? 60;
                    var item = new ComboItem { Key = seconds.ToString(CultureInfo.InvariantCulture), Seconds = seconds, Text = DisplayLabel(dict, seconds.ToString(CultureInfo.InvariantCulture)) };
                    refreshCombo.Items.Add(item);
                    if (selectedSeconds.HasValue && selectedSeconds.Value == seconds) { SetComboSelection(refreshCombo, item); }
                }
                if (selectedSeconds.HasValue) { SetRefreshButtonContent(selectedSeconds.Value); }
            }
            finally { updatingSettingsControls = false; }
        }


        private string DisplayLabel(Dictionary<string, object> payload, string fallback)
        {
            var label = GetString(payload, "label");
            var labelKey = GetString(payload, "key");
            if (IsEnglishUi())
            {
                var keyedName = StandardRegionNameByKey(labelKey);
                if (!String.IsNullOrWhiteSpace(keyedName)) { return keyedName; }
                var english = GetString(payload, "en");
                if (!String.IsNullOrWhiteSpace(english)) { return NormalizeEnglishLocationName(english); }
                if (!String.IsNullOrWhiteSpace(label))
                {
                    if (labelKey.StartsWith("Day", StringComparison.OrdinalIgnoreCase) || labelKey.StartsWith("Hour", StringComparison.OrdinalIgnoreCase)) { return LocalizeModeText(label); }
                    if ((payload != null && (payload.ContainsKey("cities") || payload.ContainsKey("districts"))) || LooksLikeRegionKey(labelKey)) { return LocalizeLocationSegment(label); }
                    return label;
                }
            }
            if (!String.IsNullOrWhiteSpace(label)) { return label; }
            label = GetString(payload, "zh");
            if (!String.IsNullOrWhiteSpace(label)) { return label; }
            label = GetString(payload, "en");
            return NonEmpty(label, fallback);
        }
        private static Dictionary<string, object> NewRefreshOption(int seconds, string label)
        {
            var item = new Dictionary<string, object>();
            item["seconds"] = seconds;
            item["label"] = label;
            return item;
        }

        private void SetRefreshButtonContent(int seconds)
        {
            if (refreshButton != null) { refreshButton.Content = FormatRefreshSeconds(seconds); }
        }

        private static string FormatRefreshSeconds(int seconds)
        {
            if (seconds < 60) { return seconds.ToString(CultureInfo.InvariantCulture) + "s"; }
            if (seconds % 3600 == 0) { return (seconds / 3600).ToString(CultureInfo.InvariantCulture) + "h"; }
            if (seconds % 60 == 0) { return (seconds / 60).ToString(CultureInfo.InvariantCulture) + "m"; }
            return seconds.ToString(CultureInfo.InvariantCulture) + "s";
        }

        private void PopulateForecastSlots(object[] slots, string selectedKey)
        {
            updatingSettingsControls = true;
            try
            {
                forecastSlotCombo.Items.Clear();
                var effectiveSelectedKey = NormalizeForecastSlotKey(selectedKey);
                foreach (var entry in slots ?? new object[0])
                {
                    var dict = entry as Dictionary<string, object>;
                    var key = GetString(dict, "key");
                    if (String.IsNullOrWhiteSpace(key)) { continue; }
                    var item = new ComboItem { Key = key, Text = DisplayLabel(dict, key), Data = dict };
                    forecastSlotCombo.Items.Add(item);
                    if (String.Equals(key, effectiveSelectedKey, StringComparison.OrdinalIgnoreCase) || GetBool(dict, "selected")) { SetComboSelection(forecastSlotCombo, item); }
                }
            }
            finally { updatingSettingsControls = false; }
        }

        private static string NormalizeForecastSlotKey(string selectedKey)
        {
            if (String.IsNullOrWhiteSpace(selectedKey)) { return "Day0"; }
            switch (selectedKey)
            {
                case "Now": return "Day0";
                case "+1h": return "Hour+1h";
                case "+3h": return "Hour+3h";
                case "+6h": return "Hour+6h";
                case "+12h": return "Hour+12h";
                case "Tonight": return "Hour+12h";
                case "Tomorrow": return "Day1";
                default: return selectedKey;
            }
        }
        private bool IsEnglishUi()
        {
            return String.Equals(language, "en", StringComparison.OrdinalIgnoreCase);
        }

        private string LocalizeWeatherTitle(string value)
        {
            if (!IsEnglishUi())
            {
                return String.IsNullOrWhiteSpace(value) || IsDevelopmentWeatherText(value) ? Ui("Weather") : value;
            }
            if (String.IsNullOrWhiteSpace(value) || IsDevelopmentWeatherText(value) || ContainsCjk(value)) { return Ui("Weather"); }
            return value.Trim();
        }

        private string LocalizeLocationDisplay(string value)
        {
            if (String.IsNullOrWhiteSpace(value) || !IsEnglishUi()) { return value; }
            var parts = value.Split(new[] { " - " }, StringSplitOptions.None);
            if (parts.Length <= 1) { return LocalizeLocationSegment(value); }
            var localized = new List<string>();
            foreach (var part in parts)
            {
                localized.Add(LocalizeLocationSegment(part));
            }
            return String.Join(" - ", localized.ToArray());
        }

        private static string LocalizeLocationSegment(string value)
        {
            if (String.IsNullOrWhiteSpace(value)) { return value; }
            var text = value.Trim();
            switch (text)
            {
                case "\u5e7f\u4e1c\u7701": return "Guangdong";
                case "\u6df1\u5733\u5e02": return "Shenzhen";
                case "\u7f57\u6e56\u533a": return "Luohu";
                case "\u9f99\u534e\u533a": return "Longhua";
                case "\u5317\u4eac\u5e02": return "Beijing";
                case "\u4e0a\u6d77\u5e02": return "Shanghai";
                case "\u5929\u6d25\u5e02": return "Tianjin";
                case "\u91cd\u5e86\u5e02": return "Chongqing";
                case "\u9999\u6e2f\u7279\u522b\u884c\u653f\u533a": return "Hong Kong";
                case "\u6fb3\u95e8\u7279\u522b\u884c\u653f\u533a": return "Macau";
                default: return NormalizeEnglishLocationName(text);
            }
        }

        private static string StandardRegionNameByKey(string key)
        {
            switch (key)
            {
                case "110000": return "Beijing";
                case "120000": return "Tianjin";
                case "130000": return "Hebei";
                case "140000": return "Shanxi";
                case "150000": return "Inner Mongolia";
                case "210000": return "Liaoning";
                case "220000": return "Jilin";
                case "230000": return "Heilongjiang";
                case "310000": return "Shanghai";
                case "320000": return "Jiangsu";
                case "330000": return "Zhejiang";
                case "340000": return "Anhui";
                case "350000": return "Fujian";
                case "360000": return "Jiangxi";
                case "370000": return "Shandong";
                case "410000": return "Henan";
                case "420000": return "Hubei";
                case "430000": return "Hunan";
                case "440000": return "Guangdong";
                case "440300": return "Shenzhen";
                case "440303": return "Luohu";
                case "440309": return "Longhua";
                case "450000": return "Guangxi";
                case "460000": return "Hainan";
                case "500000": return "Chongqing";
                case "510000": return "Sichuan";
                case "520000": return "Guizhou";
                case "530000": return "Yunnan";
                case "540000": return "Tibet";
                case "610000": return "Shaanxi";
                case "620000": return "Gansu";
                case "630000": return "Qinghai";
                case "640000": return "Ningxia";
                case "650000": return "Xinjiang";
                case "710000": return "Taiwan";
                case "810000": return "Hong Kong";
                case "820000": return "Macau";
                default: return String.Empty;
            }
        }
        private static string NormalizeEnglishLocationName(string value)
        {
            if (String.IsNullOrWhiteSpace(value)) { return value; }
            var text = value.Trim();
            while (text.IndexOf("  ", StringComparison.Ordinal) >= 0) { text = text.Replace("  ", " "); }
            switch (text)
            {
                case "Guang Dong Sheng": return "Guangdong";
                case "Shen Zhen Shi": return "Shenzhen";
                case "Luo Hu Qu": return "Luohu";
                case "Long Hua Qu": return "Longhua";
                case "Bei Jing Shi": return "Beijing";
                case "Shang Hai Shi": return "Shanghai";
                case "Tian Jin Shi": return "Tianjin";
                case "Chong Qing Shi": return "Chongqing";
                case "Xiang Gang Te Bie Xing Zheng Qu": return "Hong Kong";
                case "Ao Men Te Bie Xing Zheng Qu": return "Macau";                case "He Bei Sheng": return "Hebei";
                case "Shan Xi Sheng": return "Shanxi";
                case "Nei Meng Gu Zi Zhi Qu": return "Inner Mongolia";
                case "Liao Ning Sheng": return "Liaoning";
                case "Ji Lin Sheng": return "Jilin";
                case "Hei Long Jiang Sheng": return "Heilongjiang";
                case "Jiang Su Sheng": return "Jiangsu";
                case "Zhe Jiang Sheng": return "Zhejiang";
                case "An Hui Sheng": return "Anhui";
                case "Fu Jian Sheng": return "Fujian";
                case "Jiang Xi Sheng": return "Jiangxi";
                case "Shan Dong Sheng": return "Shandong";
                case "He Nan Sheng": return "Henan";
                case "Hu Bei Sheng": return "Hubei";
                case "Hu Nan Sheng": return "Hunan";
                case "Guang Xi Zhuang Zu Zi Zhi Qu": return "Guangxi";
                case "Hai Nan Sheng": return "Hainan";
                case "Si Chuan Sheng": return "Sichuan";
                case "Gui Zhou Sheng": return "Guizhou";
                case "Yun Nan Sheng": return "Yunnan";
                case "Xi Zang Zi Zhi Qu": return "Tibet";
                case "Gan Su Sheng": return "Gansu";
                case "Qing Hai Sheng": return "Qinghai";
                case "Ning Xia Hui Zu Zi Zhi Qu": return "Ningxia";
                case "Xin Jiang Wei Wu Er Zi Zhi Qu": return "Xinjiang";
            }
            var suffixes = new[] { " Te Bie Xing Zheng Qu", " Zi Zhi Qu", " Di Qu", " Jie Dao", " Sheng", " Shi", " Xian", " Qu", " Zhen", " Meng", " Qi" };
            foreach (var suffix in suffixes)
            {
                if (text.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    text = text.Substring(0, text.Length - suffix.Length).Trim();
                    break;
                }
            }
            var words = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0) { return value.Trim(); }
            var builder = new StringBuilder();
            foreach (var word in words)
            {
                if (word.Length == 1) { builder.Append(word.ToUpperInvariant()); }
                else { builder.Append(word.Substring(0, 1).ToUpperInvariant()).Append(word.Substring(1).ToLowerInvariant()); }
            }
            return builder.Length == 0 ? value.Trim() : builder.ToString();
        }

        private string LocalizeModeText(string value)
        {
            if (String.IsNullOrWhiteSpace(value) || !IsEnglishUi()) { return value; }
            var text = value.Trim();
            switch (text)
            {
                case "\u4eca\u5929": return "Today";
                case "\u660e\u5929": return "Tomorrow";
                case "\u540e\u5929": return "+2d";
                case "\u4eca\u665a": return "+12h";
            }
            if (text.StartsWith("+", StringComparison.Ordinal) && text.EndsWith("\u5929", StringComparison.Ordinal))
            {
                return text.Substring(0, text.Length - 1) + "d";
            }
            return ContainsCjk(text) ? Ui("Now") : text;
        }

        private string LocalizeConditionText(string value)
        {
            if (String.IsNullOrWhiteSpace(value) || !IsEnglishUi()) { return value; }
            var text = value.Trim();
            switch (text)
            {
                case "\u6674": return "Clear";
                case "\u591a\u4e91": return "Cloudy";
                case "\u964d\u96e8": return "Rain";
                case "\u5c0f\u96e8": return "Rain";
                case "\u5927\u96e8": return "Rain";
                case "\u96f7\u9635\u96e8": return "Thunderstorm";
                case "\u591c\u95f4": return "Night";
            }
            return ContainsCjk(text) ? "Weather" : text;
        }
        private string LocalizeNearTermText(string value)
        {
            if (String.IsNullOrWhiteSpace(value) || !IsEnglishUi()) { return value; }
            var text = value.Trim();
            switch (text)
            {
                case "\u4e34\u8fd1\u9884\u62a5\u6682\u4e0d\u53ef\u7528": return UiStateLabel("cached");
                case "\u4e34\u8fd1\u6709\u5f3a\u964d\u96e8\u98ce\u9669": return "Near-term heavy rain risk";
                case "\u4e34\u8fd1\u6709\u964d\u96e8\u53ef\u80fd": return "Near-term rain possible";
                case "\u4e34\u8fd1\u6682\u65e0\u5f3a\u964d\u96e8": return "No near-term heavy rain";
            }
            return ContainsCjk(text) ? UiStateLabel("cached") : text;
        }

        private static bool LooksLikeRegionKey(string key)
        {
            if (String.IsNullOrWhiteSpace(key) || key.Length < 6) { return false; }
            foreach (var ch in key)
            {
                if (!Char.IsDigit(ch)) { return false; }
            }
            return true;
        }
        private static bool ContainsCjk(string value)
        {
            if (String.IsNullOrEmpty(value)) { return false; }
            foreach (var ch in value)
            {
                if ((ch >= '\u3400' && ch <= '\u9FFF') || (ch >= '\uF900' && ch <= '\uFAFF')) { return true; }
            }
            return false;
        }
        private void PopulateProvinceCombo()
        {
            updatingSettingsControls = true;
            try
            {
                provinceCombo.Items.Clear();
                foreach (var entry in catalogProvinces ?? new object[0])
                {
                    var dict = entry as Dictionary<string, object>;
                    var key = GetString(dict, "key");
                    if (String.IsNullOrWhiteSpace(key)) { continue; }
                    provinceCombo.Items.Add(new ComboItem { Key = key, Text = DisplayLabel(dict, key), Data = dict });
                }
                if (provinceCombo.Items.Count > 0 && provinceCombo.SelectedItem == null) { SetComboSelection(provinceCombo, provinceCombo.Items[0] as ComboItem); }
            }
            finally { updatingSettingsControls = false; }
            var selected = provinceCombo.SelectedItem as ComboItem;
            if (selected != null) { PopulateCityCombo(selected); }
        }

        private void PopulateCityCombo(ComboItem province)
        {
            updatingSettingsControls = true;
            try
            {
                cityCombo.Items.Clear();
                var dict = province == null ? null : province.Data as Dictionary<string, object>;
                foreach (var entry in GetArray(dict, "cities"))
                {
                    var city = entry as Dictionary<string, object>;
                    var key = GetString(city, "key");
                    if (String.IsNullOrWhiteSpace(key)) { continue; }
                    cityCombo.Items.Add(new ComboItem { Key = key, Text = DisplayLabel(city, key), Data = city });
                }
                if (cityCombo.Items.Count > 0) { SetComboSelection(cityCombo, cityCombo.Items[0] as ComboItem); }
            }
            finally { updatingSettingsControls = false; }
            var selected = cityCombo.SelectedItem as ComboItem;
            if (selected != null) { PopulateDistrictCombo(selected); }
        }

        private void PopulateDistrictCombo(ComboItem city)
        {
            updatingSettingsControls = true;
            try
            {
                districtCombo.Items.Clear();
                var dict = city == null ? null : city.Data as Dictionary<string, object>;
                foreach (var entry in GetArray(dict, "districts"))
                {
                    var district = entry as Dictionary<string, object>;
                    var key = GetString(district, "key");
                    if (String.IsNullOrWhiteSpace(key)) { continue; }
                    districtCombo.Items.Add(new ComboItem { Key = key, Text = DisplayLabel(district, key), Data = district });
                }
                if (districtCombo.Items.Count > 0) { SetComboSelection(districtCombo, districtCombo.Items[0] as ComboItem); }
            }
            finally { updatingSettingsControls = false; }
        }

        private void SelectCatalogKeys(string provinceKey, string cityKey, string districtKey)
        {
            if (provinceCombo == null || provinceCombo.Items.Count == 0) { return; }
            updatingSettingsControls = true;
            try
            {
                SelectComboByKey(provinceCombo, provinceKey);
                PopulateCityCombo(provinceCombo.SelectedItem as ComboItem);
                updatingSettingsControls = true;
                SelectComboByKey(cityCombo, cityKey);
                PopulateDistrictCombo(cityCombo.SelectedItem as ComboItem);
                updatingSettingsControls = true;
                SelectComboByKey(districtCombo, districtKey);
            }
            finally { updatingSettingsControls = false; }
        }

        private static void SelectComboByKey(ComboBox combo, string key)
        {
            if (combo == null || String.IsNullOrWhiteSpace(key)) { return; }
            foreach (var obj in combo.Items)
            {
                var item = obj as ComboItem;
                if (item != null && String.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    SetComboSelection(combo, item);
                    return;
                }
            }
        }
        private void BeginSnapshotBoot()
        {
            StartupBenchmark.TraceLauncher("SnapshotBoot start");
            ThreadPool.QueueUserWorkItem(delegate
            {
                var diagnostics = TryReadSnapshotDiagnostics();
                StartupBenchmark.TraceLauncher(String.Format(
                    CultureInfo.InvariantCulture,
                    "SnapshotBoot diagnostics valid={0} stale={1} cross-location={2}",
                    diagnostics.Valid,
                    diagnostics.Stale,
                    diagnostics.Exists && !diagnostics.MatchesCurrentLocation));

                if (!diagnostics.Exists)
                {
                    StartupBenchmark.TraceLauncher("SnapshotBoot skipped reason: " + NonEmpty(diagnostics.SkipReason, "missing-file"));
                    return;
                }

                if (!diagnostics.Valid)
                {
                    StartupBenchmark.TraceLauncher("SnapshotBoot invalid: " + NonEmpty(diagnostics.SkipReason, "invalid"));
                    return;
                }

                if (!diagnostics.MatchesCurrentLocation)
                {
                    StartupBenchmark.TraceLauncher("SnapshotBoot skipped reason: " + NonEmpty(diagnostics.SkipReason, "cross-location"));
                    return;
                }

                StartupBenchmark.TraceLauncher("SnapshotBoot valid");
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    TryApplySnapshot(diagnostics);
                }), DispatcherPriority.Background);
            });
        }

        private SnapshotDiagnostics TryReadSnapshotDiagnostics()
        {
            var diagnostics = new SnapshotDiagnostics();
            diagnostics.Path = LauncherPaths.FindWeatherSnapshot();
            diagnostics.MatchesCurrentLocation = true;
            diagnostics.SkipReason = String.Empty;

            try
            {
                StartupBenchmark.TraceLauncher("SnapshotBoot file read start");
                if (String.IsNullOrWhiteSpace(diagnostics.Path) || !File.Exists(diagnostics.Path))
                {
                    diagnostics.Exists = false;
                    diagnostics.Valid = false;
                    diagnostics.SkipReason = "missing-file";
                    return diagnostics;
                }

                diagnostics.Exists = true;
                var text = File.ReadAllText(diagnostics.Path, Encoding.UTF8);
                StartupBenchmark.TraceLauncher("SnapshotBoot file read end");
                Dictionary<string, object> envelope;
                try
                {
                    var serializer = new JavaScriptSerializer();
                    envelope = serializer.DeserializeObject(text) as Dictionary<string, object>;
                }
                catch
                {
                    diagnostics.Valid = false;
                    diagnostics.SkipReason = "malformed-json";
                    return diagnostics;
                }

                diagnostics.Envelope = envelope;
                diagnostics.Schema = GetInt(envelope, "schema");
                diagnostics.Source = GetString(envelope, "source");
                diagnostics.Fixture = GetBool(envelope, "fixture");
                diagnostics.LocationKey = GetString(envelope, "locationKey");
                diagnostics.LocationLabel = GetString(envelope, "locationLabel");
                diagnostics.SavedAtUtc = ParseSnapshotSavedAt(GetString(envelope, "savedAt"));
                diagnostics.AgeSeconds = GetSnapshotAge(diagnostics.SavedAtUtc);
                diagnostics.Stale = diagnostics.AgeSeconds.HasValue && diagnostics.AgeSeconds.Value >= SnapshotStaleHours * 3600L;

                var expectedLocationKey = ReadConfiguredLocationKey();
                diagnostics.MatchesCurrentLocation = String.IsNullOrWhiteSpace(expectedLocationKey) || String.Equals(diagnostics.LocationKey, expectedLocationKey, StringComparison.OrdinalIgnoreCase);
                diagnostics.SkipReason = ValidateSnapshotEnvelope(envelope, expectedLocationKey);
                diagnostics.Valid = String.IsNullOrWhiteSpace(diagnostics.SkipReason) || String.Equals(diagnostics.SkipReason, "cross-location", StringComparison.OrdinalIgnoreCase);
                if (!diagnostics.MatchesCurrentLocation && String.IsNullOrWhiteSpace(diagnostics.SkipReason))
                {
                    diagnostics.SkipReason = "cross-location";
                }
                return diagnostics;
            }
            catch (Exception ex)
            {
                diagnostics.Valid = false;
                diagnostics.SkipReason = "read-failed: " + ex.GetType().Name;
                return diagnostics;
            }
        }

        private void TryApplySnapshot(SnapshotDiagnostics diagnostics)
        {
            if (diagnostics == null || diagnostics.Envelope == null)
            {
                StartupBenchmark.TraceLauncher("SnapshotBoot invalid: empty-diagnostics");
                return;
            }
            if (!diagnostics.Valid)
            {
                StartupBenchmark.TraceLauncher("SnapshotBoot invalid: " + NonEmpty(diagnostics.SkipReason, "invalid"));
                return;
            }
            if (!diagnostics.MatchesCurrentLocation)
            {
                StartupBenchmark.TraceLauncher("SnapshotBoot skipped reason: cross-location");
                return;
            }

            ApplySnapshotPayload(diagnostics);
        }

        private void ApplySnapshotPayload(SnapshotDiagnostics diagnostics)
        {
            var envelope = diagnostics.Envelope;
            var payload = GetDictionary(envelope, "payload");
            var copy = new Dictionary<string, object>(payload, StringComparer.OrdinalIgnoreCase);
            var source = NonEmpty(diagnostics.Source, GetString(copy, "source"));
            var locationKey = diagnostics.LocationKey;

            copy["fromSnapshot"] = true;
            copy["from_snapshot"] = true;
            copy["fromCache"] = true;
            copy["from_cache"] = true;
            copy["source"] = source;
            copy["locationKey"] = locationKey;
            copy["location_key"] = locationKey;
            if (!String.IsNullOrWhiteSpace(diagnostics.LocationLabel))
            {
                copy["location"] = diagnostics.LocationLabel;
                copy["locationLabel"] = diagnostics.LocationLabel;
            }

            snapshotWeatherVisible = true;
            liveWeatherApplied = false;
            snapshotWeatherLocationKey = locationKey;
            snapshotWeatherStale = diagnostics.Stale;
            snapshotWeatherSavedAtUtc = diagnostics.SavedAtUtc;
            snapshotWeatherSource = source;
            RenderWeather(copy, true, diagnostics.Stale, diagnostics.SavedAtUtc);
            StartupBenchmark.LogSnapshotApplied();
            StartupBenchmark.TraceLauncher("SnapshotBoot applied");
        }

        private static string ValidateSnapshotEnvelope(Dictionary<string, object> envelope, string expectedLocationKey)
        {
            if (envelope == null) { return "invalid-empty"; }
            if ((GetInt(envelope, "schema") ?? 0) != 1) { return "invalid-schema"; }
            var savedAt = ParseSnapshotSavedAt(GetString(envelope, "savedAt"));
            if (!savedAt.HasValue) { return "invalid-savedAt"; }
            var source = GetString(envelope, "source");
            if (String.IsNullOrWhiteSpace(source)) { return "invalid-source"; }
            var locationKey = GetString(envelope, "locationKey");
            if (String.IsNullOrWhiteSpace(locationKey)) { return "invalid-locationKey"; }
            var payload = GetDictionary(envelope, "payload");
            if (!String.Equals(GetString(payload, "status"), "ok", StringComparison.OrdinalIgnoreCase)) { return "invalid-payload-status"; }
            if (!String.IsNullOrWhiteSpace(expectedLocationKey) && !String.Equals(locationKey, expectedLocationKey, StringComparison.OrdinalIgnoreCase))
            {
                return "cross-location";
            }
            return String.Empty;
        }

        private static DateTime? ParseSnapshotSavedAt(string value)
        {
            DateTime parsed;
            if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out parsed))
            {
                return parsed.ToUniversalTime();
            }
            return null;
        }

        private static long? GetSnapshotAge(DateTime? savedAtUtc)
        {
            if (!savedAtUtc.HasValue) { return null; }
            return Math.Max(0, (long)(DateTime.UtcNow - savedAtUtc.Value).TotalSeconds);
        }

        private static string ReadConfiguredLocationKey()
        {
            var province = "Guangdong";
            var city = "Shenzhen";
            var district = "Longhua";
            try
            {
                var settingsPath = LauncherPaths.FindSettingsFile();
                if (File.Exists(settingsPath))
                {
                    var serializer = new JavaScriptSerializer();
                    var settings = serializer.DeserializeObject(File.ReadAllText(settingsPath, Encoding.UTF8)) as Dictionary<string, object>;
                    province = NonEmpty(GetString(settings, "ProvinceKey"), province);
                    city = NonEmpty(GetString(settings, "CityKey"), city);
                    district = NonEmpty(GetString(settings, "DistrictKey"), district);
                }
            }
            catch
            {
            }
            return BuildLocationKey(province, city, district);
        }

        private static string BuildLocationKey(string province, string city, string district)
        {
            return String.Format(CultureInfo.InvariantCulture, "{0}|{1}|{2}", NormalizeLocationKeyPart(province), NormalizeLocationKeyPart(city), NormalizeLocationKeyPart(district));
        }

        private static string NormalizeLocationKeyPart(string value)
        {
            if (String.IsNullOrWhiteSpace(value)) { return "_"; }
            return value.Trim().Replace("|", "%7C");
        }

        private string GetSnapshotDisplayStatus(SnapshotDisplayState state)
        {
            switch (state)
            {
                case SnapshotDisplayState.FreshSnapshot:
                case SnapshotDisplayState.RefreshingFromSnapshot:
                    return FormatSnapshotStatus(UiStateLabel("refreshing"));
                case SnapshotDisplayState.StaleSnapshot:
                    return FormatSnapshotStatus(UiStateLabel("stale") + " / " + UiStateLabel("refreshing"));
                case SnapshotDisplayState.RefreshFailedShowingSnapshot:
                    return UiStateLabel("failed") + " / " + UiStateLabel("cached");
                case SnapshotDisplayState.Live:
                    return UiStateLabel("live");
                default:
                    return String.Empty;
            }
        }

        private string FormatSnapshotStatus(string label)
        {
            if (snapshotWeatherSavedAtUtc.HasValue)
            {
                return label + " | " + snapshotWeatherSavedAtUtc.Value.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);
            }
            return label;
        }

        private void SetSnapshotDisplayState(SnapshotDisplayState state)
        {
            snapshotDisplayState = state;
            if (state == SnapshotDisplayState.None) { return; }
            if (state == SnapshotDisplayState.Live)
            {
                RenderStatus("live");
                ClearError();
                return;
            }

            RenderStatus(state == SnapshotDisplayState.StaleSnapshot ? "stale" : "cached");
            RenderUpdated(GetSnapshotDisplayStatus(state));
            if (state != SnapshotDisplayState.RefreshFailedShowingSnapshot)
            {
                ClearError();
            }
        }

        private void ClearSnapshotForLocationChange()
        {
            snapshotWeatherVisible = false;
            liveWeatherApplied = false;
            snapshotWeatherLocationKey = null;
            snapshotWeatherStale = false;
            snapshotWeatherSavedAtUtc = null;
            snapshotWeatherSource = null;
            snapshotDisplayState = SnapshotDisplayState.None;
        }

        private void PreserveSnapshotOffline(string message)
        {
            SetSnapshotDisplayState(SnapshotDisplayState.RefreshFailedShowingSnapshot);
            RenderErrorState("failed");
        }
        private void OnContentRendered(object sender, EventArgs e)
        {
            if (workerStarted)
            {
                return;
            }

            var logPath = LauncherPaths.FindBenchmarkLog(options.BenchmarkLogPath);
            StartupBenchmark.Initialize(logPath);
            StartupBenchmark.TraceLauncher("ContentRendered");
            StartupBenchmark.LogWindowShown();
            workerStarted = true;

            Dispatcher.BeginInvoke(new Action(delegate
            {
                StartupBenchmark.TraceLauncher("First Dispatcher idle");
                BeginSnapshotBoot();
                StartWorker();
                EnsureMetricsGridCreated();
                InitializeCommandTimeoutTimer();
                ApplyDeferredVisualEffects();
                InitializeHoldOpenTimer();
            }), DispatcherPriority.ApplicationIdle);
        }

        private void InitializeHoldOpenTimer()
        {
            if (options.HoldOpenMs <= 0 || holdOpenTimer != null) { return; }
            holdOpenTimer = new DispatcherTimer();
            holdOpenTimer.Interval = TimeSpan.FromMilliseconds(options.HoldOpenMs);
            holdOpenTimer.Tick += delegate
            {
                holdOpenTimer.Stop();
                Close();
            };
            holdOpenTimer.Start();
        }

        private void OnClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            SaveWindowPlacement();
            StopWorker();
        }

        private void OnWindowBorderMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left || !Object.ReferenceEquals(e.OriginalSource, rootBorder))
            {
                return;
            }

            OnDragHandleMouseDown(sender, e);
        }

        private static bool IsDragExcludedSource(DependencyObject source)
        {
            var current = source;
            while (current != null)
            {
                if (current is ButtonBase || current is Selector || current is TextBoxBase || current is PasswordBox || current is ScrollBar || current is MenuItem || current is ContextMenu || current is Popup)
                {
                    return true;
                }

                var element = current as FrameworkElement;
                if (element != null && element.Cursor == Cursors.Hand)
                {
                    return true;
                }

                current = GetDragSourceParent(current);
            }
            return false;
        }

        private static DependencyObject GetDragSourceParent(DependencyObject source)
        {
            try
            {
                var visualParent = VisualTreeHelper.GetParent(source);
                if (visualParent != null) { return visualParent; }
            }
            catch
            {
            }

            var element = source as FrameworkElement;
            if (element != null) { return element.Parent; }

            var contentElement = source as FrameworkContentElement;
            if (contentElement != null) { return contentElement.Parent; }

            return null;
        }

        private void OnDragHandleMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left)
            {
                return;
            }

            try
            {
                e.Handled = true;
                DragMove();
            }
            catch
            {
            }
        }

        private Border BuildDrawerHandle()
        {
            drawerHandle = new Border();
            drawerHandle.Width = CollapsedWidth;
            drawerHandle.Height = CollapsedHeight;
            drawerHandle.CornerRadius = String.Equals(drawerEdge, "Left", StringComparison.OrdinalIgnoreCase) ? new CornerRadius(0, RadiusLg, RadiusLg, 0) : new CornerRadius(RadiusLg, 0, 0, RadiusLg);
            drawerHandle.Background = BrushFrom(ColorShell);
            drawerHandle.BorderBrush = BrushFrom("#55D8D4C8");
            drawerHandle.BorderThickness = new Thickness(1);
            drawerHandle.ContextMenu = BuildWindowContextMenu();
            drawerHandle.Cursor = Cursors.Hand;
            drawerHandle.Focusable = true;
            drawerHandle.MouseLeftButtonUp += delegate { ExpandDrawer(); };
            drawerHandle.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Enter || e.Key == Key.Space)
                {
                    e.Handled = true;
                    ExpandDrawer();
                }
            };
            var glyph = Text(String.Equals(drawerEdge, "Left", StringComparison.OrdinalIgnoreCase) ? ">" : "<", 17, ColorMuted, FontWeights.SemiBold);
            glyph.HorizontalAlignment = HorizontalAlignment.Center;
            glyph.VerticalAlignment = VerticalAlignment.Center;
            var handleButton = new Button();
            handleButton.Width = CollapsedWidth;
            handleButton.Height = CollapsedHeight;
            handleButton.Padding = new Thickness(0);
            handleButton.Content = glyph;
            handleButton.ToolTip = Ui("DrawerExpand");
            handleButton.Background = BrushFrom("#00FFFFFF");
            handleButton.BorderThickness = new Thickness(0);
            handleButton.Cursor = Cursors.Hand;
            handleButton.Click += delegate { ExpandDrawer(); };
            SetAutomationId(handleButton, "DrawerHandle");
            drawerHandle.Child = handleButton;
            return drawerHandle;
        }

        private void CollapseDrawer()
        {
            if (!drawerExpanded) { return; }
            drawerExpanded = false;
            settingsOpen = false;
            if (settingsPanel != null) { settingsPanel.Visibility = Visibility.Collapsed; }
            SetSettingsModeChrome(false);
            var workArea = SystemParameters.WorkArea;
            var midpoint = workArea.Left + (workArea.Width / 2.0);
            drawerEdge = (Left + (Width / 2.0)) < midpoint ? "Left" : "Right";
            Content = BuildDrawerHandle();
            Width = CollapsedWidth;
            Height = CollapsedHeight;
            Top = ClampToRange(Top, workArea.Top, workArea.Bottom - Height);
            Left = String.Equals(drawerEdge, "Left", StringComparison.OrdinalIgnoreCase) ? workArea.Left : workArea.Right - Width;
            SaveWindowPlacement();
        }

        private void ExpandDrawer()
        {
            if (drawerExpanded) { return; }
            drawerExpanded = true;
            Content = rootBorder;
            Width = ExpandedWidth;
            ApplyWindowHeightForCurrentMode();
            var workArea = SystemParameters.WorkArea;
            Top = ClampToRange(Top, workArea.Top, workArea.Bottom - Height);
            Left = String.Equals(drawerEdge, "Left", StringComparison.OrdinalIgnoreCase) ? workArea.Left : workArea.Right - Width;
            SaveWindowPlacement();
        }

        private static double ClampToRange(double value, double minimum, double maximum)
        {
            if (maximum < minimum) { return minimum; }
            if (value < minimum) { return minimum; }
            if (value > maximum) { return maximum; }
            return value;
        }
        private void LoadWindowPlacement()
        {
            try
            {
                var path = GetLauncherSettingsPath();
                if (!File.Exists(path)) { return; }
                var state = json.DeserializeObject(File.ReadAllText(path, Encoding.UTF8)) as Dictionary<string, object>;
                var left = GetDouble(state, "left");
                var top = GetDouble(state, "top");
                if (!left.HasValue || !top.HasValue) { return; }
                drawerExpanded = !state.ContainsKey("drawerExpanded") || GetBool(state, "drawerExpanded");
                var savedEdge = GetString(state, "drawerEdge");
                if (String.Equals(savedEdge, "Left", StringComparison.OrdinalIgnoreCase) || String.Equals(savedEdge, "Right", StringComparison.OrdinalIgnoreCase)) { drawerEdge = savedEdge; }
                var placementWidth = drawerExpanded ? ExpandedWidth : CollapsedWidth;
                var placementHeight = drawerExpanded ? ExpandedHeight : CollapsedHeight;
                if (!IsWindowPlacementOnScreen(left.Value, top.Value, placementWidth, placementHeight)) { drawerExpanded = true; return; }
                Width = placementWidth;
                Height = placementHeight;
                Left = left.Value;
                Top = top.Value;
            }
            catch
            {
                drawerExpanded = true;
            }
        }

        private void SaveWindowPlacement()
        {
            try
            {
                if (Double.IsNaN(Left) || Double.IsNaN(Top)) { return; }
                if (!IsWindowPlacementOnScreen(Left, Top, Width, Height)) { return; }
                var path = GetLauncherSettingsPath();
                var directory = Path.GetDirectoryName(path);
                if (!String.IsNullOrWhiteSpace(directory)) { Directory.CreateDirectory(directory); }
                var state = new Dictionary<string, object>();
                state["left"] = Left;
                state["top"] = Top;
                state["drawerExpanded"] = drawerExpanded;
                state["drawerEdge"] = drawerEdge;
                state["drawerTop"] = Top;
                state["savedAt"] = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
                File.WriteAllText(path, json.Serialize(state), Encoding.UTF8);
            }
            catch
            {
            }
        }

        private static string GetLauncherSettingsPath()
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (String.IsNullOrWhiteSpace(localAppData))
            {
                localAppData = Path.GetTempPath();
            }
            return Path.Combine(localAppData, "PaperWeatherWidget", "launcher-settings.json");
        }

        private static bool IsWindowPlacementOnScreen(double left, double top, double width, double height)
        {
            if (Double.IsNaN(left) || Double.IsNaN(top)) { return false; }
            width = Math.Max(24, width);
            height = Math.Max(24, height);
            var screenLeft = SystemParameters.VirtualScreenLeft;
            var screenTop = SystemParameters.VirtualScreenTop;
            var screenRight = screenLeft + SystemParameters.VirtualScreenWidth;
            var screenBottom = screenTop + SystemParameters.VirtualScreenHeight;
            var overlapLeft = Math.Max(left, screenLeft);
            var overlapTop = Math.Max(top, screenTop);
            var overlapRight = Math.Min(left + width, screenRight);
            var overlapBottom = Math.Min(top + height, screenBottom);
            return overlapRight - overlapLeft >= 80 && overlapBottom - overlapTop >= 80;
        }
        private void InitializeCommandTimeoutTimer()
        {
            if (commandTimeoutTimer != null) { return; }
            commandTimeoutTimer = new DispatcherTimer();
            commandTimeoutTimer.Interval = TimeSpan.FromMilliseconds(500);
            commandTimeoutTimer.Tick += OnCommandTimeoutTimerTick;
            commandTimeoutTimer.Start();
        }

        private void RegisterPendingCommand(string id, string type)
        {
            if (String.IsNullOrWhiteSpace(id)) { return; }
            pendingCommands[id] = new PendingCommand
            {
                Id = id,
                Type = type,
                SentUtc = DateTime.UtcNow,
                TimeoutMs = GetCommandTimeoutMs(type)
            };
        }

        private void RemovePendingCommand(string id)
        {
            if (String.IsNullOrWhiteSpace(id)) { return; }
            pendingCommands.Remove(id);
        }

        private static int GetCommandTimeoutMs(string type)
        {
            if (String.Equals(type, "manualRefresh", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(type, "setLocation", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(type, "setLanguage", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(type, "setForecastSlot", StringComparison.OrdinalIgnoreCase))
            {
                return 5000;
            }
            return 3000;
        }

        private void OnCommandTimeoutTimerTick(object sender, EventArgs e)
        {
            if (pendingCommands.Count == 0) { return; }
            var now = DateTime.UtcNow;
            var expired = new List<PendingCommand>();
            foreach (var pair in pendingCommands)
            {
                if ((now - pair.Value.SentUtc).TotalMilliseconds >= pair.Value.TimeoutMs)
                {
                    expired.Add(pair.Value);
                }
            }
            foreach (var command in expired)
            {
                pendingCommands.Remove(command.Id);
                StartupBenchmark.Log("[Launcher] command timeout: " + command.Id + " " + command.Type);
            }
            if (expired.Count > 0)
            {
                var command = expired[expired.Count - 1];
                RenderErrorState("failed");
            }
        }

        private void ApplyAck(Dictionary<string, object> payload, string id)
        {
            RemovePendingCommand(id);
            if (errorBlock != null)
            {
                ClearError();
            }
        }

        private void ApplyWorkerError(Dictionary<string, object> payload, string id)
        {
            RemovePendingCommand(id);
            var code = GetString(payload, "code");
            var message = NonEmpty(GetString(payload, "message"), code);
            if (String.Equals(code, "weather_worker_error", StringComparison.OrdinalIgnoreCase))
            {
                if (snapshotWeatherVisible && !liveWeatherApplied)
                {
                    PreserveSnapshotOffline(message);
                    return;
                }
                SetOffline(message);
                return;
            }
            RenderErrorState("failed");
        }
        private void EnsureCommandFile()
        {
            if (!String.IsNullOrWhiteSpace(commandFilePath))
            {
                return;
            }

            sessionId = Guid.NewGuid().ToString("N");
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (String.IsNullOrWhiteSpace(localAppData))
            {
                localAppData = Path.GetTempPath();
            }
            var ipcDir = Path.Combine(localAppData, "PaperWeatherWidget", "ipc");
            Directory.CreateDirectory(ipcDir);
            commandFilePath = Path.Combine(ipcDir, sessionId + ".commands.jsonl");
            File.WriteAllText(commandFilePath, String.Empty, Encoding.UTF8);
        }

        private void CleanupCommandFile()
        {
            if (String.IsNullOrWhiteSpace(commandFilePath))
            {
                sessionId = null;
                return;
            }

            try
            {
                if (File.Exists(commandFilePath))
                {
                    File.Delete(commandFilePath);
                }
            }
            catch
            {
            }
            commandFilePath = null;
            sessionId = null;
        }

        private void SendCommand(string type, Dictionary<string, object> payload)
        {
            if (workerProcess == null || workerProcess.HasExited)
            {
                SetOffline(Ui("WorkerUnavailable"));
                RestartWorker();
                return;
            }

            string commandId = null;
            try
            {
                EnsureCommandFile();
                commandId = "cmd-" + (++commandCounter).ToString(CultureInfo.InvariantCulture);
                var command = new Dictionary<string, object>();
                command["protocol"] = 1;
                command["type"] = type;
                command["id"] = commandId;
                command["sessionId"] = sessionId;
                command["timestamp"] = UnixSeconds();
                command["payload"] = payload ?? new Dictionary<string, object>();
                RegisterPendingCommand(commandId, type);
                File.AppendAllText(commandFilePath, json.Serialize(command) + Environment.NewLine, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                RemovePendingCommand(commandId);
                SetOffline(ex.Message);
            }
        }
        private void ManualRefresh()
        {
            SetRefreshingState();
            SendCommand("manualRefresh", new Dictionary<string, object>());
        }

        private void SetSettingsModeChrome(bool isSettingsOpen)
        {
            var statusVisibility = isSettingsOpen ? Visibility.Collapsed : Visibility.Visible;
            if (footerPanel != null)
            {
                footerPanel.Visibility = Visibility.Visible;
                footerPanel.Height = Double.NaN;
            }
            if (updatedBlock != null) { updatedBlock.Visibility = statusVisibility; }
            if (errorBlock != null) { errorBlock.Visibility = statusVisibility; }
            if (creditButton != null) { creditButton.Visibility = isSettingsOpen ? Visibility.Visible : Visibility.Collapsed; }
        }

        private void ApplyWindowHeightForCurrentMode()
        {
            Height = settingsOpen ? ExpandedSettingsHeight : ExpandedHeight;
            var workArea = SystemParameters.WorkArea;
            Top = ClampToRange(Top, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - Height));
        }
        private void ToggleSettingsPanel()
        {
            if (!drawerExpanded)
            {
                ExpandDrawer();
                return;
            }
            settingsOpen = !settingsOpen;
            if (settingsOpen) { EnsureSettingsPanelCreated(); }
            settingsPanel.Visibility = settingsOpen ? Visibility.Visible : Visibility.Collapsed;
            SetSettingsModeChrome(settingsOpen);
            ApplyWindowHeightForCurrentMode();
            if (settingsOpen && !settingsRequested)
            {
                settingsRequested = true;
                SendCommand("getSettings", new Dictionary<string, object>());
                SendCommand("getRegionCatalog", new Dictionary<string, object>());
            }
        }
        private void RestartWorker()
        {
            StopWorker();
            SetLoadingState();
            StartWorker();
        }

        private void StartWorker()
        {
            StartupBenchmark.TraceLauncher("StartWorker begin");
            var workerPath = LauncherPaths.FindWorkerScript(options.WorkerPath);
            if (String.IsNullOrWhiteSpace(workerPath))
            {
                SetOffline(UiStateLabel("failed"));
                return;
            }

            try
            {
                var repoRoot = LauncherPaths.FindRepositoryRoot();
                EnsureCommandFile();
                var fixtureArg = options.FixtureWeatherSuccess ? " -FixtureWeatherSuccess" : String.Empty;
                var fixtureSnapshotArg = options.AllowFixtureSnapshotWrite ? " -AllowFixtureSnapshotWrite" : String.Empty;
                var traceArg = options.StartupTrace && !String.IsNullOrWhiteSpace(StartupBenchmark.StartupTracePath)
                    ? " -StartupTrace -StartupTracePath \"" + StartupBenchmark.StartupTracePath + "\""
                    : String.Empty;
                var psi = new ProcessStartInfo();
                psi.FileName = "powershell.exe";
                psi.Arguments = String.Format(
                    CultureInfo.InvariantCulture,
                    "-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{0}\" -AppRoot \"{1}\" -PollSeconds {2} -CommandFile \"{3}\" -SessionId \"{4}\" -IpcMode{5}{6}{7}",
                    workerPath,
                    repoRoot,
                    options.PollSeconds,
                    commandFilePath,
                    sessionId,
                    fixtureArg,
                    traceArg,
                    fixtureSnapshotArg);
                psi.WorkingDirectory = repoRoot;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.StandardOutputEncoding = Encoding.UTF8;
                psi.StandardErrorEncoding = Encoding.UTF8;

                workerProcess = new Process();
                workerProcess.StartInfo = psi;
                workerProcess.EnableRaisingEvents = true;
                workerProcess.OutputDataReceived += OnWorkerOutput;
                workerProcess.ErrorDataReceived += OnWorkerError;
                workerProcess.Exited += OnWorkerExited;
                workerProcess.Start();
                StartupBenchmark.TraceLauncher("StartWorker Process.Start returned");
                StartupBenchmark.LogWorkerStarted();
                workerProcess.BeginOutputReadLine();
                workerProcess.BeginErrorReadLine();
            }
            catch (Exception ex)
            {
                SetOffline(ex.Message);
            }
        }

        private void StopWorker()
        {
            var process = workerProcess;
            workerProcess = null;
            if (process == null)
            {
                pendingCommands.Clear();
                CleanupCommandFile();
                return;
            }

            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                }
            }
            catch
            {
            }

            try
            {
                process.Dispose();
            }
            catch
            {
            }

            pendingCommands.Clear();
            CleanupCommandFile();
        }

        private void OnWorkerOutput(object sender, DataReceivedEventArgs e)
        {
            if (String.IsNullOrWhiteSpace(e.Data))
            {
                return;
            }
            StartupBenchmark.TraceFirstStdoutLine();

            Dictionary<string, object> payload = null;
            try
            {
                payload = json.DeserializeObject(e.Data) as Dictionary<string, object>;
            }
            catch (Exception ex)
            {
                StartupBenchmark.Log("[Worker] ignored stdout: " + ex.Message);
            }

            if (payload == null)
            {
                return;
            }

            Dispatcher.BeginInvoke(new Action(delegate
            {
                ApplyWorkerPayload(payload);
            }));
        }

        private void OnWorkerError(object sender, DataReceivedEventArgs e)
        {
            if (!String.IsNullOrWhiteSpace(e.Data))
            {
                StartupBenchmark.Log("[Worker] stderr: " + e.Data);
            }
        }

        private void OnWorkerExited(object sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(delegate
            {
                if (workerProcess != null && workerProcess.HasExited && String.IsNullOrWhiteSpace(errorBlock.Text))
                {
                    SetOffline(UiStateLabel("failed"));
                }
            }));
        }

        private void ApplyStatus(Dictionary<string, object> payload)
        {
            var phase = GetString(payload, "phase");
            if (String.Equals(phase, "refreshing", StringComparison.OrdinalIgnoreCase) || String.Equals(phase, "loading", StringComparison.OrdinalIgnoreCase))
            {
                SetRefreshingState();
                return;
            }
            if (String.Equals(phase, "offline", StringComparison.OrdinalIgnoreCase))
            {
                var message = GetString(payload, "message");
                if (snapshotWeatherVisible && !liveWeatherApplied)
                {
                    PreserveSnapshotOffline(message);
                    return;
                }
                SetOffline(message);
                return;
            }
            if (String.Equals(phase, "cached", StringComparison.OrdinalIgnoreCase))
            {
                if (snapshotWeatherVisible && !liveWeatherApplied)
                {
                    SetSnapshotDisplayState(snapshotDisplayState == SnapshotDisplayState.None ? SnapshotDisplayState.RefreshingFromSnapshot : snapshotDisplayState);
                    return;
                }
                RenderStatus("cached");
                return;
            }
            if (String.Equals(phase, "idle", StringComparison.OrdinalIgnoreCase))
            {
                RenderStatus("live");
            }
        }

        private void SetRefreshingState()
        {
            if (snapshotWeatherVisible && !liveWeatherApplied)
            {
                SetSnapshotDisplayState(SnapshotDisplayState.RefreshingFromSnapshot);
                return;
            }
            RenderStatus("refreshing");
            RenderUpdatedState("refreshing");
            ClearError();
        }
        private string Ui(string key)
        {
            var zh = String.Equals(language, "zh", StringComparison.OrdinalIgnoreCase);
            switch (key)
            {
                case "Weather": return zh ? "\u4f60\u4e00\u6765\u5c31\u662f\u597d\u5929\u6c14" : "Weather";
                case "Settings": return zh ? "\u8bbe\u7f6e" : "Settings";
                case "RefreshNow": return zh ? "\u5237\u65b0" : "Refresh now";
                case "DrawerCollapse": return zh ? "\u6536\u8d77" : "Collapse";
                case "DrawerExpand": return zh ? "\u5c55\u5f00" : "Expand";
                case "Exit": return zh ? "\u5173\u95ed" : "Close";
                case "Province": return zh ? "\u7701" : "Province";
                case "City": return zh ? "\u5e02" : "City";
                case "District": return zh ? "\u533a" : "District";
                case "Refresh": return zh ? "\u5237\u65b0\u9891\u7387" : "Refresh interval";
                case "Language": return zh ? "\u8bed\u8a00" : "Language";
                case "CreditPrefix": return zh ? "\u7231\u6765\u81ea" : "Love from";
                case "Forecast": return zh ? "\u9884\u62a5" : "Forecast";
                case "Startup": return zh ? "\u5f00\u673a\u542f\u52a8" : "Start on boot";
                case "StartupOn": return zh ? "\u5df2\u5f00\u542f" : "Enabled";
                case "StartupOff": return zh ? "\u672a\u5f00\u542f" : "Disabled";
                case "Loading": return UiStateLabel("refreshing");
                case "Offline": return UiStateLabel("failed");
                case "Cached": return UiStateLabel("cached");
                case "Current": return UiStateLabel("live");
                case "Now": return zh ? "\u4eca\u5929" : "Today";
                case "RainNow": return zh ? "\u5f53\u524d\u964d\u96e8" : "Rain now";
                case "TodayRain": return zh ? "\u4eca\u65e5\u964d\u96e8" : "Today rain";
                case "Probability": return zh ? "\u964d\u96e8\u6982\u7387" : "Precip probability";
                case "Humidity": return zh ? "\u6e7f\u5ea6" : "Humidity";
                case "Cloud": return zh ? "\u4e91\u91cf" : "Cloud";
                case "Pressure": return zh ? "\u6c14\u538b" : "Pressure";
                case "Wind": return zh ? "\u98ce\u901f" : "Wind";
                case "Gust": return zh ? "\u9635\u98ce" : "Gust";
                case "Feels": return zh ? "\u4f53\u611f" : "Feels";
                case "Updating": return UiStateLabel("refreshing");
                case "WorkerUnavailable": return UiStateLabel("failed");
                case "CommandTimeout": return UiStateLabel("failed");
                case "CachedData": return UiStateLabel("cached");
                case "LastDataRefreshing": return UiStateLabel("refreshing");
                case "StaleCache": return UiStateLabel("stale");
                case "StaleCacheRefreshing": return UiStateLabel("stale") + " / " + UiStateLabel("refreshing");
                case "RefreshFailedShowingCached": return UiStateLabel("failed") + " / " + UiStateLabel("cached");
                case "StaleCached": return UiStateLabel("stale") + " / " + UiStateLabel("cached");
                case "LiveRefreshFailedCached": return UiStateLabel("failed") + " / " + UiStateLabel("cached");
                default: return String.Empty;
            }
        }

        private void ApplyLanguageToVisibleText()
        {
            if (titleBlock != null && titleUsesDefaultLabel) { RenderTitle(Ui("Weather"), true); }
            if (settingsButton != null) { settingsButton.ToolTip = Ui("Settings"); }
            if (refreshButton != null) { refreshButton.ToolTip = Ui("RefreshNow"); }
            if (closeButton != null) { closeButton.ToolTip = Ui("Exit"); }
            UpdateCreditButtonContent();
            RenderUiLabel(settingsTitleBlock, "Settings");
            RenderUiLabel(provinceLabelBlock, "Province");
            RenderUiLabel(cityLabelBlock, "City");
            RenderUiLabel(districtLabelBlock, "District");
            RenderUiLabel(refreshLabelBlock, "Refresh");
            RenderUiLabel(languageLabelBlock, "Language");
            RenderUiLabel(forecastLabelBlock, "Forecast");
            RenderUiLabel(startupLabelBlock, "Startup");
            ApplyStartupButtonState(IsStartupEnabled());
            foreach (var pair in metricLabelBlocks) { RenderUiLabel(pair.Value, pair.Key); }
        }
        private void LocalizeVisibleWeatherText()
        {
            if (!IsEnglishUi()) { return; }
            if (titleBlock != null && ContainsCjk(titleBlock.Text)) { RenderTitle(Ui("Weather"), true); }
            if (locationBlock != null) { RenderLocation(LocalizeLocationDisplay(locationBlock.Text)); }
            if (modeBlock != null) { RenderModeText(LocalizeModeText(modeBlock.Text)); }
            if (conditionBlock != null) { RenderConditionText(LocalizeConditionText(conditionBlock.Text)); }
            if (nearTermBlock != null) { RenderNearTermText(LocalizeNearTermText(nearTermBlock.Text)); }
            if (feelsBlock != null && ContainsCjk(feelsBlock.Text)) { RenderFeelsText(feelsBlock.Text.Replace("\u4f53\u611f", Ui("Feels"))); }
        }
        private void ApplyWorkerPayload(Dictionary<string, object> payload)
        {
            if (payload != null && payload.ContainsKey("protocol"))
            {
                var eventType = GetString(payload, "type");
                var eventId = GetString(payload, "id");
                var body = GetDictionary(payload, "payload");
                if (String.Equals(eventType, "ack", StringComparison.OrdinalIgnoreCase))
                {
                    ApplyAck(body, eventId);
                    return;
                }
                if (String.Equals(eventType, "weather", StringComparison.OrdinalIgnoreCase))
                {
                    ApplyWorkerPayload(body);
                    return;
                }
                if (String.Equals(eventType, "settings", StringComparison.OrdinalIgnoreCase))
                {
                    ApplySettings(body);
                    return;
                }
                if (String.Equals(eventType, "catalog", StringComparison.OrdinalIgnoreCase))
                {
                    ApplyCatalog(body);
                    return;
                }
                if (String.Equals(eventType, "status", StringComparison.OrdinalIgnoreCase))
                {
                    ApplyStatus(body);
                    return;
                }
                if (String.Equals(eventType, "error", StringComparison.OrdinalIgnoreCase))
                {
                    ApplyWorkerError(body, eventId);
                    return;
                }
            }
            var status = GetString(payload, "status");
            if (String.Equals(status, "error", StringComparison.OrdinalIgnoreCase))
            {
                var message = GetString(payload, "error");
                if (snapshotWeatherVisible && !liveWeatherApplied)
                {
                    PreserveSnapshotOffline(message);
                    return;
                }
                SetOffline(message);
                return;
            }

            RenderWeather(payload, false, false, null);
        }

        private void RenderStatus(string stateKey)
        {
            statusBlock.Text = UiStateLabel(stateKey);
            statusBlock.Foreground = StateBrush(stateKey);
        }

        private void RenderUiLabel(TextBlock block, string key)
        {
            if (block != null) { block.Text = Ui(key); }
        }

        private Brush StateBrush(string stateKey)
        {
            if (String.Equals(stateKey, "live", StringComparison.OrdinalIgnoreCase)) { return BrushFrom(ColorAccent); }
            if (String.Equals(stateKey, "failed", StringComparison.OrdinalIgnoreCase) || String.Equals(stateKey, "refreshing", StringComparison.OrdinalIgnoreCase)) { return BrushFrom(ColorDanger); }
            return BrushFrom(ColorMuted);
        }

        private string UiStateLabel(string stateKey)
        {
            var zh = String.Equals(language, "zh", StringComparison.OrdinalIgnoreCase);
            if (String.Equals(stateKey, "live", StringComparison.OrdinalIgnoreCase)) { return zh ? "\u5b9e\u65f6" : "Live"; }
            if (String.Equals(stateKey, "cached", StringComparison.OrdinalIgnoreCase)) { return zh ? "\u7f13\u5b58" : "Cached"; }
            if (String.Equals(stateKey, "stale", StringComparison.OrdinalIgnoreCase)) { return zh ? "\u8fc7\u65f6" : "Stale"; }
            if (String.Equals(stateKey, "failed", StringComparison.OrdinalIgnoreCase)) { return zh ? "\u5237\u65b0\u5931\u8d25" : "Failed"; }
            return zh ? "\u6b63\u5728\u5237\u65b0" : "Refreshing";
        }

        private string ConditionLabel(string conditionKey)
        {
            var zh = String.Equals(language, "zh", StringComparison.OrdinalIgnoreCase);
            switch (conditionKey)
            {
                case "thunderstorm": return zh ? "\u96f7\u9635\u96e8" : "Thunderstorm";
                case "rain": return zh ? "\u964d\u96e8" : "Rain";
                case "clear": return zh ? "\u6674" : "Clear";
                case "night": return zh ? "\u591c\u95f4" : "Night";
                case "cloudy": return zh ? "\u591a\u4e91" : "Cloudy";
                default: return ConditionLabel("cloudy");
            }
        }

        private ConditionVisual ResolveConditionVisual(int? weatherCode, int? isDay, double? rain)
        {
            var raining = rain.HasValue && rain.Value > 0.0;
            if (weatherCode.HasValue && (weatherCode.Value == 95 || weatherCode.Value == 96 || weatherCode.Value == 99))
            {
                return new ConditionVisual { Key = "thunderstorm", Icon = ((char)0x26C8).ToString(), IconColor = ColorAccent, GradientMiddle = ColorAlert };
            }
            if (raining || (weatherCode.HasValue && weatherCode.Value >= 51 && weatherCode.Value <= 82))
            {
                return new ConditionVisual { Key = "rain", Icon = ((char)0x2614).ToString(), IconColor = ColorRain, GradientMiddle = ColorWarm };
            }
            if (isDay.HasValue && isDay.Value == 0)
            {
                return new ConditionVisual { Key = "night", Icon = ((char)0x263E).ToString(), IconColor = ColorMuted, GradientMiddle = ColorWarm };
            }
            if (weatherCode.HasValue && weatherCode.Value <= 1)
            {
                return new ConditionVisual { Key = "clear", Icon = ((char)0x2600).ToString(), IconColor = ColorAccent, GradientMiddle = ColorWarm };
            }
            return new ConditionVisual { Key = "cloudy", Icon = ((char)0x2601).ToString(), IconColor = ColorAccent, GradientMiddle = ColorWarm };
        }

        private ConditionVisual ResolveStateVisual(string stateKey)
        {
            if (String.Equals(stateKey, "failed", StringComparison.OrdinalIgnoreCase))
            {
                return new ConditionVisual { Key = "failed", Icon = ((char)0x26A0).ToString(), IconColor = ColorDanger, GradientMiddle = ColorAlert };
            }
            return ResolveConditionVisual(null, null, null);
        }

        private void ApplyConditionVisual(ConditionVisual visual)
        {
            if (visual == null) { visual = ResolveConditionVisual(null, null, null); }
            rootBorder.Background = MakeGradient(ColorPaper, NonEmpty(visual.GradientMiddle, ColorWarm), ColorSurface);
            iconBlock.Text = NonEmpty(visual.Icon, ((char)0x2601).ToString());
            iconBlock.Foreground = BrushFrom(NonEmpty(visual.IconColor, ColorAccent));
        }

        private WeatherRenderModel CreateWeatherModel(Dictionary<string, object> payload)
        {
            var rawTitle = GetString(payload, "title");
            var title = LocalizeWeatherTitle(rawTitle);
            var titleUsesDefault = String.Equals(title, Ui("Weather"), StringComparison.Ordinal);
            return new WeatherRenderModel
            {
                Title = title,
                TitleUsesDefaultLabel = titleUsesDefault,
                Location = LocalizeLocationDisplay(GetString(payload, "location")),
                Updated = GetString(payload, "updated"),
                Condition = new ConditionRenderModel
                {
                    WeatherCode = GetInt(payload, "weather_code"),
                    IsDay = GetInt(payload, "is_day"),
                    Rain = GetDouble(payload, "rain"),
                    Mode = LocalizeModeText(GetString(payload, "mode")),
                    NearTerm = LocalizeNearTermText(GetString(payload, "near_term"))
                },
                Metrics = new WeatherMetrics
                {
                    Temperature = GetDouble(payload, "temp"),
                    FeelsLike = GetDouble(payload, "feels_like"),
                    Rain = GetDouble(payload, "rain"),
                    TodayRain = GetDouble(payload, "today_rain"),
                    RainProbability = GetDouble(payload, "rain_probability"),
                    Humidity = GetDouble(payload, "humidity"),
                    Cloud = GetDouble(payload, "cloud"),
                    Pressure = GetDouble(payload, "pressure"),
                    Wind = GetDouble(payload, "wind"),
                    Gust = GetDouble(payload, "gust")
                }
            };
        }

        private void RenderWeather(Dictionary<string, object> payload, bool fromSnapshot, bool staleSnapshot, DateTime? snapshotSavedAt)
        {
            EnsureMetricsGridCreated();
            if (fromSnapshot)
            {
                snapshotWeatherVisible = true;
                liveWeatherApplied = false;
                snapshotWeatherStale = staleSnapshot;
                snapshotWeatherSavedAtUtc = snapshotSavedAt;
            }
            else
            {
                snapshotWeatherVisible = false;
                liveWeatherApplied = true;
                snapshotWeatherLocationKey = null;
                snapshotWeatherStale = false;
                snapshotWeatherSavedAtUtc = null;
                snapshotWeatherSource = null;
                StartupBenchmark.LogFirstData();
                StartupBenchmark.TraceFirstWeatherApplied();
            }

            var model = CreateWeatherModel(payload);
            ClearError();
            RenderTitle(model.Title, model.TitleUsesDefaultLabel);
            RenderLocation(model.Location);
            RenderCondition(model.Condition);
            RenderMetrics(model.Metrics);
            RenderUpdated(model.Updated);

            if (fromSnapshot)
            {
                SetSnapshotDisplayState(staleSnapshot ? SnapshotDisplayState.StaleSnapshot : SnapshotDisplayState.RefreshingFromSnapshot);
            }
            else
            {
                SetSnapshotDisplayState(SnapshotDisplayState.Live);
            }
        }

        private void RenderWeatherState(string stateKey)
        {
            RenderTitle(Ui("Weather"), true);
            RenderStatus(stateKey);
            RenderLocation(DefaultLocationLabel());
            RenderConditionState(stateKey);
            RenderMetrics(WeatherMetrics.Empty());
            RenderUpdatedState(stateKey);
            if (String.Equals(stateKey, "failed", StringComparison.OrdinalIgnoreCase))
            {
                RenderErrorState("failed");
            }
            else
            {
                ClearError();
            }
        }

        private void RenderTitle(string title, bool usesDefaultLabel)
        {
            titleUsesDefaultLabel = usesDefaultLabel;
            titleBlock.Text = DisplayTextOrState(title, "live", Ui("Weather"));
        }

        private void RenderLocation(string location)
        {
            locationBlock.Text = DisplayTextOrState(location, "cached", DefaultLocationLabel());
        }

        private void RenderUpdated(string updated)
        {
            updatedBlock.Text = DisplayTextOrState(updated, "live", UiStateLabel("live"));
        }

        private void RenderUpdatedState(string stateKey)
        {
            updatedBlock.Text = UiStateLabel(stateKey);
        }

        private void RenderErrorState(string stateKey)
        {
            errorBlock.Text = UiStateLabel(stateKey);
        }

        private void ClearError()
        {
            errorBlock.Text = String.Empty;
        }

        private void RenderCondition(ConditionRenderModel condition)
        {
            if (condition == null) { condition = new ConditionRenderModel(); }
            var visual = ResolveConditionVisual(condition.WeatherCode, condition.IsDay, condition.Rain);
            modeBlock.Text = DisplayTextOrState(condition.Mode, "live", Ui("Now"));
            conditionBlock.Text = ConditionLabel(visual.Key);
            nearTermBlock.Text = DisplayTextOrState(condition.NearTerm, "cached", UiStateLabel("cached"));
            ApplyConditionVisual(visual);
        }

        private void RenderConditionState(string stateKey)
        {
            modeBlock.Text = Ui("Now");
            conditionBlock.Text = UiStateLabel(stateKey);
            nearTermBlock.Text = UiStateLabel(stateKey);
            ApplyConditionVisual(ResolveStateVisual(stateKey));
        }


        private void RenderModeText(string mode)
        {
            modeBlock.Text = DisplayTextOrState(mode, "live", Ui("Now"));
        }

        private void RenderConditionText(string condition)
        {
            conditionBlock.Text = DisplayTextOrState(condition, "cached", UiStateLabel("cached"));
        }

        private void RenderNearTermText(string nearTerm)
        {
            nearTermBlock.Text = DisplayTextOrState(nearTerm, "cached", UiStateLabel("cached"));
        }

        private void RenderFeelsText(string feels)
        {
            feelsBlock.Text = DisplayTextOrState(feels, "cached", Ui("Feels") + " --");
        }

        private void RenderMetrics(WeatherMetrics metrics)
        {
            if (metrics == null) { metrics = WeatherMetrics.Empty(); }
            RenderMetric(temperatureBlock, FormatCelsius(metrics.Temperature, 1));
            RenderMetric(feelsBlock, Ui("Feels") + " " + FormatCelsius(metrics.FeelsLike, 1));
            RenderMetric(rainValueBlock, FormatMillimeters(metrics.Rain));
            RenderMetric(dayRainValueBlock, FormatMillimeters(metrics.TodayRain));
            RenderMetric(probabilityValueBlock, FormatPercent(metrics.RainProbability));
            RenderMetric(humidityValueBlock, FormatPercent(metrics.Humidity));
            RenderMetric(cloudValueBlock, FormatPercent(metrics.Cloud));
            RenderMetric(pressureValueBlock, FormatUnit(metrics.Pressure, " hPa", 0));
            RenderMetric(windValueBlock, FormatUnit(metrics.Wind, " km/h", 0));
            RenderMetric(gustValueBlock, FormatUnit(metrics.Gust, " km/h", 0));
        }

        private void RenderMetricPlaceholders()
        {
            RenderMetrics(WeatherMetrics.Empty());
        }

        private static void RenderMetric(TextBlock block, string value)
        {
            if (block != null) { block.Text = value; }
        }

        private string DisplayTextOrState(string value, string fallbackStateKey, string fallback)
        {
            if (String.IsNullOrWhiteSpace(value) || IsDevelopmentWeatherText(value)) { return NonEmpty(fallback, UiStateLabel(fallbackStateKey)); }
            return value;
        }

        private static bool IsDevelopmentWeatherText(string value)
        {
            if (String.IsNullOrWhiteSpace(value)) { return false; }
            return value.IndexOf("fixture", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("debug", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("forecast ready", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("unavailable", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("WeatherWorker", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("worker exited", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void SetLoadingState()
        {
            RenderWeatherState("refreshing");
        }

        private void SetOffline(string message)
        {
            RenderWeatherState("failed");
        }

        private string DefaultLocationLabel()
        {
            return IsEnglishUi() ? "Guangdong - Shenzhen - Longhua" : "\u5e7f\u4e1c\u7701 - \u6df1\u5733\u5e02 - \u9f99\u534e\u533a";
        }

        private static string PlainMetricPlaceholder()
        {
            return "--";
        }
        private TextBlock Text(string text, double size, string color, FontWeight weight)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = size,
                Foreground = BrushFrom(color),
                FontWeight = weight,
                TextWrapping = TextWrapping.NoWrap,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        private static Brush BrushFrom(string color)
        {
            return new SolidColorBrush(ColorFrom(color));
        }

        private static Color ColorFrom(string color)
        {
            return (Color)ColorConverter.ConvertFromString(color);
        }

        private static Brush MakeGradient(string start, string middle, string end)
        {
            var brush = new LinearGradientBrush();
            brush.StartPoint = new Point(0, 0);
            brush.EndPoint = new Point(1, 1);
            brush.GradientStops.Add(new GradientStop(ColorFrom(start), 0.0));
            brush.GradientStops.Add(new GradientStop(ColorFrom(middle), 0.55));
            brush.GradientStops.Add(new GradientStop(ColorFrom(end), 1.0));
            return brush;
        }

        private static string NonEmpty(string value, string fallback)
        {
            return String.IsNullOrWhiteSpace(value) ? fallback : value;
        }

        private static long UnixSeconds()
        {
            return (long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds;
        }

        private static Dictionary<string, object> GetDictionary(Dictionary<string, object> payload, string key)
        {
            object value;
            if (payload != null && payload.TryGetValue(key, out value))
            {
                return value as Dictionary<string, object> ?? new Dictionary<string, object>();
            }
            return new Dictionary<string, object>();
        }

        private static object[] GetArray(Dictionary<string, object> payload, string key)
        {
            object value;
            if (payload == null || !payload.TryGetValue(key, out value) || value == null)
            {
                return new object[0];
            }
            var array = value as object[];
            if (array != null) { return array; }
            var enumerable = value as IEnumerable;
            if (enumerable == null || value is string) { return new object[0]; }
            var list = new List<object>();
            foreach (var item in enumerable) { list.Add(item); }
            return list.ToArray();
        }

        private static bool GetBool(Dictionary<string, object> payload, string key)
        {
            object value;
            if (payload == null || !payload.TryGetValue(key, out value) || value == null) { return false; }
            try { return Convert.ToBoolean(value, CultureInfo.InvariantCulture); } catch { return false; }
        }
        private static string GetString(Dictionary<string, object> payload, string key)
        {
            object value;
            if (payload != null && payload.TryGetValue(key, out value) && value != null)
            {
                return Convert.ToString(value, CultureInfo.InvariantCulture);
            }

            return String.Empty;
        }

        private static double? GetDouble(Dictionary<string, object> payload, string key)
        {
            object value;
            if (payload == null || !payload.TryGetValue(key, out value) || value == null)
            {
                return null;
            }

            try
            {
                return Convert.ToDouble(value, CultureInfo.InvariantCulture);
            }
            catch
            {
                return null;
            }
        }

        private static int? GetInt(Dictionary<string, object> payload, string key)
        {
            object value;
            if (payload == null || !payload.TryGetValue(key, out value) || value == null)
            {
                return null;
            }

            try
            {
                return Convert.ToInt32(value, CultureInfo.InvariantCulture);
            }
            catch
            {
                return null;
            }
        }

        private static string FormatCelsius(double? value, int digits)
        {
            if (!value.HasValue)
            {
                return "--";
            }

            return value.Value.ToString("F" + digits, CultureInfo.InvariantCulture) + " C";
        }

        private static string FormatMillimeters(double? value)
        {
            if (!value.HasValue)
            {
                return "-- mm";
            }

            return value.Value.ToString("F1", CultureInfo.InvariantCulture) + " mm";
        }

        private static string FormatPercent(double? value)
        {
            if (!value.HasValue)
            {
                return "--%";
            }

            return value.Value.ToString("F0", CultureInfo.InvariantCulture) + "%";
        }

        private static string FormatUnit(double? value, string unit, int digits)
        {
            if (!value.HasValue)
            {
                return "--" + unit;
            }

            return value.Value.ToString("F" + digits, CultureInfo.InvariantCulture) + unit;
        }
    }
}


























