using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PaperWeatherWidget.Models;
using PaperWeatherWidget.Services;

namespace PaperWeatherWidget.UI
{
    public sealed class LaunchOptions
    {
        public bool FixtureMode { get; set; }
        public bool DisableTopmost { get; set; }
        public bool DisableTray { get; set; }
        public string OverrideTheme { get; set; }
        public string OverrideLang { get; set; }
        public string OverrideScene { get; set; }
        public string RenderShotsDir { get; set; }
        public int HoldOpenMs { get; set; }

        public static LaunchOptions Parse(string[] args)
        {
            var opt = new LaunchOptions();
            if (args == null) { return opt; }
            for (var i = 0; i < args.Length; i++)
            {
                var a = args[i] ?? String.Empty;
                if (String.Equals(a, "--fixture", StringComparison.OrdinalIgnoreCase) || String.Equals(a, "--fixture-weather-success", StringComparison.OrdinalIgnoreCase))
                {
                    opt.FixtureMode = true;
                }
                else if (String.Equals(a, "--no-topmost", StringComparison.OrdinalIgnoreCase))
                {
                    opt.DisableTopmost = true;
                }
                else if (String.Equals(a, "--no-tray", StringComparison.OrdinalIgnoreCase))
                {
                    opt.DisableTray = true;
                }
                else if (String.Equals(a, "--theme", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    opt.OverrideTheme = args[++i];
                }
                else if (String.Equals(a, "--lang", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    opt.OverrideLang = args[++i];
                }
                else if (String.Equals(a, "--scene", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    opt.OverrideScene = args[++i];
                }
                else if (String.Equals(a, "--render-shots", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    opt.RenderShotsDir = args[++i];
                }
                else if (String.Equals(a, "--hold-open-ms", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    int ms;
                    if (Int32.TryParse(args[++i], out ms)) { opt.HoldOpenMs = Math.Max(0, Math.Min(60000, ms)); }
                }
            }
            return opt;
        }
    }

    public sealed class MainWindow : Window
    {
        private static readonly FontFamily UiFont = new FontFamily("Segoe UI Variable Display, Segoe UI, Microsoft YaHei UI");

        private const double CardWidth = 348;
        private const double CompactCardHeight = 332;
        private const double DetailedCardHeight = 540;
        private const double SettingsCardHeight = 504;
        private const double CollapsedWidth = 54;
        private const double CollapsedHeight = 108;

        private readonly LaunchOptions launchOptions;
        private readonly SettingsService settingsService;
        private readonly RegionCatalogService catalogService;
        private readonly WeatherService weatherService;
        private readonly WeatherSceneController sceneController;
        private readonly bool useNativeWin11Acrylic;
        private TrayService trayService;

        private AppSettings settings;
        private DistrictItem currentDistrict;
        private WeatherSnapshot currentSnapshot;
        private WeatherSyncState syncState = WeatherSyncState.Loading;
        private ThemePalette theme;
        private bool glassBackdropActive;
        private bool settingsOpen;
        private int fetchVersion;
        private string scenePreviewKey = "live"; // "live" | "sunny" | "cloudy" | "rain" | "storm" | "night" | "snow"

        private DispatcherTimer autoRefreshTimer;
        private DispatcherTimer memoryTrimTimer;

        // Visual tree (layered Liquid Glass + Animated Sky)
        private Border rootCard;
        private Border sheenOverlay;
        private Border foregroundScrimHost;
        private Grid foregroundContentGrid;
        private Grid weatherViewGrid;
        private SettingsView settingsView;
        private CollapsedPillView collapsedPill;

        // Header controls
        private Button locationPillBtn;
        private TextBlock locationPillText;
        private Border locationPinHost;
        private Border statusDot;
        private TextBlock statusText;
        private Button refreshBtn;
        private Button pinBtn;
        private Button settingsBtn;
        private Button collapseBtn;
        private Button closeBtn;

        // Hero controls
        private TextBlock tempBlock;
        private Border inlineConditionIconHost;
        private TextBlock conditionBlock;
        private TextBlock feelsRangeBlock;
        private Border nearTermBadge;
        private TextBlock nearTermText;

        // Forecast strip
        private readonly Dictionary<string, Button> slotButtons = new Dictionary<string, Button>(StringComparer.OrdinalIgnoreCase);
        private Button moreSlotsBtn;
        private static readonly string[] QuickSlots = new[] { "Day0", "Hour+1h", "Hour+3h", "Hour+6h", "Day1", "Day2" };

        // Primary 4 metrics
        private Border primaryMetricsCard;
        private TextBlock rainProbLabel, rainProbValue;
        private TextBlock humidityLabel, humidityValue;
        private TextBlock windLabel, windValue;
        private TextBlock todayRainLabel, todayRainValue;

        // Expandable details drawer
        private Button detailsToggleBtn;
        private TextBlock detailsToggleText;
        private Border detailsToggleChevronHost;
        private StackPanel detailsContainer;
        private Border secondaryMetricsCard;
        private TextBlock rainNowLabel, rainNowValue;
        private TextBlock gustLabel, gustValue;
        private TextBlock cloudLabel, cloudValue;
        private TextBlock pressureLabel, pressureValue;
        private TextBlock fiveDayTitle;
        private UniformGrid fiveDayGrid;

        // Footer
        private TextBlock footerUpdatedBlock;
        private TextBlock footerErrorBlock;

        public MainWindow(LaunchOptions options)
        {
            launchOptions = options ?? new LaunchOptions();
            settingsService = new SettingsService();
            catalogService = new RegionCatalogService();
            weatherService = new WeatherService();
            sceneController = new WeatherSceneController();
            useNativeWin11Acrylic = GlassWindowHelper.IsWindows11NativeAcrylicSupported() && String.IsNullOrWhiteSpace(launchOptions.RenderShotsDir);

            settings = settingsService.Load();
            if (!String.IsNullOrWhiteSpace(launchOptions.OverrideTheme))
            {
                settings.ThemeMode = launchOptions.OverrideTheme;
            }
            if (!String.IsNullOrWhiteSpace(launchOptions.OverrideLang))
            {
                settings.Language = launchOptions.OverrideLang;
            }
            if (!String.IsNullOrWhiteSpace(launchOptions.OverrideScene))
            {
                scenePreviewKey = launchOptions.OverrideScene.ToLowerInvariant();
            }
            if (launchOptions.DisableTopmost)
            {
                settings.Topmost = false;
            }

            currentDistrict = catalogService.ResolveFromSettings(settings);
            if (catalogService.IsLoaded && String.IsNullOrWhiteSpace(launchOptions.RenderShotsDir))
            {
                settingsService.SaveSettings(settings);
                catalogService.UnloadCatalog();
            }

            theme = ThemePalette.Create(ThemePalette.ResolveIsDark(settings.ThemeMode));

            ConfigureWindow();
            BuildUiTree();
            ApplyThemeToAll();
            ApplyLanguageToAll();

            var diskSnap = launchOptions.FixtureMode ? null : weatherService.TryLoadDiskSnapshot(currentDistrict, settings.ForecastSlotKey);
            if (diskSnap != null)
            {
                currentSnapshot = diskSnap;
                syncState = diskSnap.IsStale ? WeatherSyncState.Stale : WeatherSyncState.Refreshing;
                RenderSnapshot();
            }
            else
            {
                syncState = WeatherSyncState.Loading;
                RenderSnapshot();
            }

            ApplyWindowModeAndBounds(true);

            SourceInitialized += OnSourceInitialized;
            ContentRendered += OnContentRendered;
            Closing += OnWindowClosing;
        }

        private void ConfigureWindow()
        {
            Title = "Paper Weather";
            WindowStyle = WindowStyle.None;
            // On Windows 11 22H2+, native DWM SystemBackdrop Acrylic requires AllowsTransparency=false
            // and gives anti-aliased native rounded corners + drop shadow with ZERO square-corner Acrylic leak!
            AllowsTransparency = !useNativeWin11Acrylic;
            ResizeMode = ResizeMode.NoResize;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;
            Topmost = settings.Topmost;
            WindowStartupLocation = WindowStartupLocation.Manual;

            if (!String.IsNullOrWhiteSpace(launchOptions.RenderShotsDir))
            {
                Left = -10000;
                Top = -10000;
                ShowActivated = false;
            }
        }

        private void OnSourceInitialized(object sender, EventArgs e)
        {
            if (String.IsNullOrWhiteSpace(launchOptions.RenderShotsDir))
            {
                glassBackdropActive = GlassWindowHelper.ApplyBackdrop(this, theme.IsDark, useNativeWin11Acrylic);
                UpdateCardAtmosphere(true);
            }
        }

        private void OnContentRendered(object sender, EventArgs e)
        {
            if (!String.IsNullOrWhiteSpace(launchOptions.RenderShotsDir))
            {
                RunOffscreenShotCapture(launchOptions.RenderShotsDir);
                return;
            }

            if (!launchOptions.DisableTray)
            {
                InitTray();
            }

            UpdateAnimationPlayback();
            StartAutoRefreshTimer();
            TriggerWeatherRefresh(true);

            if (launchOptions.HoldOpenMs > 0)
            {
                var holdTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(launchOptions.HoldOpenMs) };
                holdTimer.Tick += delegate
                {
                    holdTimer.Stop();
                    Close();
                };
                holdTimer.Start();
            }

            memoryTrimTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            memoryTrimTimer.Tick += delegate
            {
                memoryTrimTimer.Stop();
                GlassWindowHelper.TrimWorkingSet();
            };
            memoryTrimTimer.Start();
        }

        private void InitTray()
        {
            try
            {
                trayService = new TrayService();
                trayService.ToggleShowRequested += delegate { Dispatcher.BeginInvoke(new Action(ToggleFromTray)); };
                trayService.RefreshRequested += delegate { Dispatcher.BeginInvoke(new Action(delegate { TriggerWeatherRefresh(true); })); };
                trayService.ToggleTopmostRequested += delegate { Dispatcher.BeginInvoke(new Action(delegate { SetTopmost(!settings.Topmost); })); };
                trayService.OpenSettingsRequested += delegate
                {
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        if (!settings.DrawerExpanded) { ExpandDrawer(); }
                        Show();
                        Activate();
                        if (!settingsOpen) { OpenSettings(false); }
                    }));
                };
                trayService.ExitRequested += delegate
                {
                    Dispatcher.BeginInvoke(new Action(delegate { Close(); }));
                };
                UpdateTrayVisual();
            }
            catch
            {
            }
        }

        private void ToggleFromTray()
        {
            if (!IsVisible)
            {
                Show();
                Activate();
                UpdateAnimationPlayback();
                return;
            }
            if (settings.DrawerExpanded)
            {
                CollapseDrawer();
            }
            else
            {
                ExpandDrawer();
                Activate();
            }
        }

        private void UpdateTrayVisual()
        {
            if (trayService == null) { return; }
            var loc = LocalizationService.FormatShortLocation(settings.Language, currentDistrict);
            var tip = loc;
            if (currentSnapshot != null && currentSnapshot.Temperature.HasValue)
            {
                int? effWmo, effDay;
                ResolveEffectiveVisualCode(out effWmo, out effDay);
                var cond = LocalizationService.WeatherConditionText(settings.Language, effWmo, effDay, currentSnapshot.RawConditionFallback);
                tip = String.Format(CultureInfo.InvariantCulture, "{0} · {1} {2:0.0}°C", loc, cond, currentSnapshot.Temperature.Value);
            }
            trayService.UpdateState(settings.Language, settings.DrawerExpanded, settings.Topmost, tip);
        }

        private void BuildUiTree()
        {
            var outerRadius = useNativeWin11Acrylic ? 8.0 : 18.0;
            rootCard = new Border
            {
                CornerRadius = new CornerRadius(outerRadius),
                BorderThickness = new Thickness(useNativeWin11Acrylic ? 0.8 : 1.2),
                ClipToBounds = true
            };
            rootCard.MouseLeftButtonDown += OnCardMouseLeftButtonDown;
            rootCard.ContextMenu = BuildContextMenu();

            var masterStack = new Grid { ClipToBounds = true };
            rootCard.Child = masterStack;

            // Layer 0: Living Apple Weather Sky Canvas (behind text & glass cards)
            sceneController.SetViewportSize(CardWidth, CompactCardHeight);
            Panel.SetZIndex(sceneController.BackgroundLayer, 0);
            masterStack.Children.Add(sceneController.BackgroundLayer);

            // Layer 1: Curved Liquid Glass Specular Sheen (non-interactive)
            sheenOverlay = new Border
            {
                CornerRadius = new CornerRadius(Math.Max(6.0, outerRadius - 1.0)),
                IsHitTestVisible = false
            };
            Panel.SetZIndex(sheenOverlay, 1);
            masterStack.Children.Add(sheenOverlay);

            // Layer 2: Floating Foreground UI (Weather view or Settings view)
            foregroundScrimHost = new Border
            {
                Padding = new Thickness(15, 13, 15, 13),
                Background = Brushes.Transparent
            };
            foregroundContentGrid = new Grid();
            foregroundScrimHost.Child = foregroundContentGrid;
            Panel.SetZIndex(foregroundScrimHost, 2);
            weatherViewGrid = BuildWeatherView();
            foregroundContentGrid.Children.Add(weatherViewGrid);
            masterStack.Children.Add(foregroundScrimHost);

            // Layer 3: Foreground Liquid Glass Water Droplets (in front of cards, IsHitTestVisible=false!)
            Panel.SetZIndex(sceneController.ForegroundGlassLayer, 3);
            masterStack.Children.Add(sceneController.ForegroundGlassLayer);

            collapsedPill = new CollapsedPillView();
            collapsedPill.ContextMenu = BuildContextMenu();
            collapsedPill.ExpandRequested += ExpandDrawer;
            collapsedPill.VerticalDragMoved += OnPillVerticalDrag;
            collapsedPill.VerticalDragCompleted += SaveCurrentPlacement;

            Content = settings.DrawerExpanded ? (object)rootCard : collapsedPill;
        }

        private Grid BuildWeatherView()
        {
            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 0: Header
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 1: Hero
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 2: Forecast pills
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 3: Primary 4 metrics
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 4: Details toggle
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 5: Expandable details
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 7: Footer

            // Row 0: Header bar
            var header = new Grid { Height = 28, Margin = new Thickness(0, 0, 0, 10) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            locationPillBtn = new Button
            {
                Height = 27,
                Padding = new Thickness(8, 0, 10, 0),
                BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Left,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Cursor = Cursors.Hand,
                FocusVisualStyle = null
            };
            var locInner = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            locationPinHost = new Border { Width = 12, Height = 12, Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center };
            locationPillText = new TextBlock
            {
                FontFamily = UiFont,
                FontSize = 12.5,
                FontWeight = FontWeights.SemiBold,
                MaxWidth = 148,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            };
            locInner.Children.Add(locationPinHost);
            locInner.Children.Add(locationPillText);
            locationPillBtn.Content = locInner;
            locationPillBtn.Click += delegate { OpenSettings(true); };
            header.Children.Add(locationPillBtn);

            var statusPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 6, 0)
            };
            statusDot = new Border
            {
                Width = 6,
                Height = 6,
                CornerRadius = new CornerRadius(3),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 4, 0)
            };
            statusText = new TextBlock
            {
                FontFamily = UiFont,
                FontSize = 10.5,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            statusPanel.Children.Add(statusDot);
            statusPanel.Children.Add(statusText);
            Grid.SetColumn(statusPanel, 1);
            header.Children.Add(statusPanel);

            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            refreshBtn = MakeIconButton(delegate { TriggerWeatherRefresh(true); });
            pinBtn = MakeIconButton(delegate { SetTopmost(!settings.Topmost); });
            settingsBtn = MakeIconButton(delegate { OpenSettings(false); });
            collapseBtn = MakeIconButton(CollapseDrawer);
            closeBtn = MakeIconButton(delegate { Close(); });
            actions.Children.Add(refreshBtn);
            actions.Children.Add(pinBtn);
            actions.Children.Add(settingsBtn);
            actions.Children.Add(collapseBtn);
            actions.Children.Add(closeBtn);
            Grid.SetColumn(actions, 2);
            header.Children.Add(actions);

            Grid.SetRow(header, 0);
            grid.Children.Add(header);

            // Row 1: Apple Weather Open-Sky Hero (leaves upper-right open so dynamic sun / moon / rain shines!)
            var hero = new StackPanel { Margin = new Thickness(2, 4, 90, 14) };

            var tempRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom };
            tempBlock = new TextBlock
            {
                Text = "--°",
                FontFamily = UiFont,
                FontSize = 48,
                FontWeight = FontWeights.Light,
                LineHeight = 50,
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                Margin = new Thickness(0, 0, 8, 0)
            };
            var condBadge = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, 7)
            };
            inlineConditionIconHost = new Border
            {
                Width = 20,
                Height = 20,
                Margin = new Thickness(0, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            conditionBlock = new TextBlock
            {
                Text = "--",
                FontFamily = UiFont,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                MaxWidth = 100,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            condBadge.Children.Add(inlineConditionIconHost);
            condBadge.Children.Add(conditionBlock);
            tempRow.Children.Add(tempBlock);
            tempRow.Children.Add(condBadge);
            hero.Children.Add(tempRow);

            feelsRangeBlock = new TextBlock
            {
                FontFamily = UiFont,
                FontSize = 12,
                FontWeight = FontWeights.Medium,
                Margin = new Thickness(2, 3, 0, 8)
            };
            hero.Children.Add(feelsRangeBlock);

            nearTermBadge = new Border
            {
                CornerRadius = new CornerRadius(9),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(9, 3.5, 9, 3.5),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            nearTermText = new TextBlock
            {
                FontFamily = UiFont,
                FontSize = 10.5,
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            nearTermBadge.Child = nearTermText;
            hero.Children.Add(nearTermBadge);

            Grid.SetRow(hero, 1);
            grid.Children.Add(hero);

            // Row 2: One-click forecast switcher strip
            var slotBar = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            for (var i = 0; i < QuickSlots.Length + 1; i++)
            {
                slotBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            }
            for (var i = 0; i < QuickSlots.Length; i++)
            {
                var slotKey = QuickSlots[i];
                var pill = new Button
                {
                    Height = 26,
                    Margin = new Thickness(i == 0 ? 0 : 2, 0, 2, 0),
                    FontFamily = UiFont,
                    FontSize = 10.5,
                    FontWeight = FontWeights.SemiBold,
                    BorderThickness = new Thickness(1),
                    Cursor = Cursors.Hand,
                    FocusVisualStyle = null
                };
                pill.Click += delegate { SelectForecastSlot(slotKey); };
                slotButtons[slotKey] = pill;
                Grid.SetColumn(pill, i);
                slotBar.Children.Add(pill);
            }

            moreSlotsBtn = new Button
            {
                Height = 26,
                Margin = new Thickness(2, 0, 0, 0),
                FontFamily = UiFont,
                FontSize = 10.5,
                FontWeight = FontWeights.SemiBold,
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                FocusVisualStyle = null
            };
            moreSlotsBtn.Click += OnMoreSlotsClick;
            Grid.SetColumn(moreSlotsBtn, QuickSlots.Length);
            slotBar.Children.Add(moreSlotsBtn);

            Grid.SetRow(slotBar, 2);
            grid.Children.Add(slotBar);

            // Row 3: Liquid Glass 4-column primary metrics bar with subtle vertical hairlines
            primaryMetricsCard = new Border
            {
                CornerRadius = new CornerRadius(14),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(4, 10, 4, 10),
                Margin = new Thickness(0, 0, 0, 8)
            };
            primaryMetricsCard.Child = BuildMetricStripWithDividers(
                out rainProbLabel, out rainProbValue,
                out humidityLabel, out humidityValue,
                out windLabel, out windValue,
                out todayRainLabel, out todayRainValue);
            Grid.SetRow(primaryMetricsCard, 3);
            grid.Children.Add(primaryMetricsCard);

            // Row 4: Expandable details toggle
            detailsToggleBtn = new Button
            {
                Height = 24,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                Cursor = Cursors.Hand,
                FocusVisualStyle = null,
                Margin = new Thickness(0, 0, 0, 4)
            };
            var toggleInner = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            detailsToggleText = new TextBlock
            {
                FontFamily = UiFont,
                FontSize = 10.5,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            detailsToggleChevronHost = new Border
            {
                Width = 12,
                Height = 12,
                Margin = new Thickness(4, 1, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            toggleInner.Children.Add(detailsToggleText);
            toggleInner.Children.Add(detailsToggleChevronHost);
            detailsToggleBtn.Content = toggleInner;
            detailsToggleBtn.Click += delegate { ToggleDetailsDrawer(); };
            Grid.SetRow(detailsToggleBtn, 4);
            grid.Children.Add(detailsToggleBtn);

            // Row 5: Expandable secondary metrics + 5-day strip
            detailsContainer = new StackPanel
            {
                Visibility = settings.DetailsExpanded ? Visibility.Visible : Visibility.Collapsed,
                Margin = new Thickness(0, 2, 0, 4)
            };

            secondaryMetricsCard = new Border
            {
                CornerRadius = new CornerRadius(14),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(4, 9, 4, 9),
                Margin = new Thickness(0, 0, 0, 10)
            };
            secondaryMetricsCard.Child = BuildMetricStripWithDividers(
                out rainNowLabel, out rainNowValue,
                out gustLabel, out gustValue,
                out cloudLabel, out cloudValue,
                out pressureLabel, out pressureValue);
            detailsContainer.Children.Add(secondaryMetricsCard);

            fiveDayTitle = new TextBlock
            {
                FontFamily = UiFont,
                FontSize = 10.5,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(2, 0, 0, 5)
            };
            detailsContainer.Children.Add(fiveDayTitle);

            fiveDayGrid = new UniformGrid { Rows = 1, Columns = 5 };
            detailsContainer.Children.Add(fiveDayGrid);

            Grid.SetRow(detailsContainer, 5);
            grid.Children.Add(detailsContainer);

            // Row 7: Footer
            var footer = new StackPanel { Margin = new Thickness(2, 2, 2, 0) };
            footerUpdatedBlock = new TextBlock
            {
                FontFamily = UiFont,
                FontSize = 10.5,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            footerErrorBlock = new TextBlock
            {
                FontFamily = UiFont,
                FontSize = 10,
                Margin = new Thickness(0, 2, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
                Visibility = Visibility.Collapsed
            };
            footer.Children.Add(footerUpdatedBlock);
            footer.Children.Add(footerErrorBlock);
            Grid.SetRow(footer, 7);
            grid.Children.Add(footer);

            return grid;
        }

        private static Grid BuildMetricStripWithDividers(
            out TextBlock l0, out TextBlock v0,
            out TextBlock l1, out TextBlock v1,
            out TextBlock l2, out TextBlock v2,
            out TextBlock l3, out TextBlock v3)
        {
            var g = new Grid();
            for (var c = 0; c < 7; c++)
            {
                g.ColumnDefinitions.Add(new ColumnDefinition
                {
                    Width = (c % 2 == 0) ? new GridLength(1, GridUnitType.Star) : new GridLength(1)
                });
            }

            var cells = new[]
            {
                BuildMetricCell(out l0, out v0),
                BuildMetricCell(out l1, out v1),
                BuildMetricCell(out l2, out v2),
                BuildMetricCell(out l3, out v3)
            };
            var dividerBrush = ThemePalette.FreezeBrush("#1CFFFFFF");

            for (var i = 0; i < 4; i++)
            {
                Grid.SetColumn(cells[i], i * 2);
                g.Children.Add(cells[i]);
                if (i < 3)
                {
                    var div = new Border
                    {
                        Width = 1,
                        Height = 24,
                        Background = dividerBrush,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    Grid.SetColumn(div, i * 2 + 1);
                    g.Children.Add(div);
                }
            }
            return g;
        }

        private static StackPanel BuildMetricCell(out TextBlock label, out TextBlock value)
        {
            var cell = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            label = new TextBlock
            {
                FontFamily = UiFont,
                FontSize = 10,
                FontWeight = FontWeights.Medium,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 0, 0, 3)
            };
            value = new TextBlock
            {
                Text = "--",
                FontFamily = UiFont,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center
            };
            cell.Children.Add(label);
            cell.Children.Add(value);
            return cell;
        }

        private static Button MakeIconButton(Action onClick)
        {
            var btn = new Button
            {
                Width = 24,
                Height = 24,
                Margin = new Thickness(2, 0, 0, 0),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                FocusVisualStyle = null
            };
            btn.Click += delegate { onClick(); };
            return btn;
        }

        private ContextMenu BuildContextMenu()
        {
            var menu = new ContextMenu();
            menu.Opened += delegate
            {
                menu.Items.Clear();
                var lang = settings.Language;
                var mRefresh = new MenuItem { Header = LocalizationService.Ui(lang, "RefreshNow") };
                mRefresh.Click += delegate { TriggerWeatherRefresh(true); };
                var mPin = new MenuItem { Header = LocalizationService.Ui(lang, settings.Topmost ? "UnpinTopmost" : "PinTopmost") };
                mPin.Click += delegate { SetTopmost(!settings.Topmost); };
                var mDrawer = new MenuItem { Header = LocalizationService.Ui(lang, settings.DrawerExpanded ? "DrawerCollapse" : "DrawerExpand") };
                mDrawer.Click += delegate { if (settings.DrawerExpanded) { CollapseDrawer(); } else { ExpandDrawer(); } };

                var mPreview = new MenuItem { Header = LocalizationService.Ui(lang, "ScenePreview") };
                var pairs = new[]
                {
                    new[] { "live", "SceneLive" },
                    new[] { "sunny", "SceneSunny" },
                    new[] { "cloudy", "SceneCloudy" },
                    new[] { "rain", "SceneRain" },
                    new[] { "storm", "SceneStorm" },
                    new[] { "night", "SceneNight" },
                    new[] { "snow", "SceneSnow" }
                };
                for (var i = 0; i < pairs.Length; i++)
                {
                    var key = pairs[i][0];
                    var sub = new MenuItem
                    {
                        Header = LocalizationService.Ui(lang, pairs[i][1]),
                        IsCheckable = true,
                        IsChecked = String.Equals(scenePreviewKey, key, StringComparison.OrdinalIgnoreCase)
                    };
                    sub.Click += delegate { OnScenePreviewChanged(key); };
                    mPreview.Items.Add(sub);
                }

                var mSettings = new MenuItem { Header = LocalizationService.Ui(lang, "Settings") };
                mSettings.Click += delegate
                {
                    if (!settings.DrawerExpanded) { ExpandDrawer(); }
                    OpenSettings(false);
                };
                var mExit = new MenuItem { Header = LocalizationService.Ui(lang, "Exit") };
                mExit.Click += delegate { Close(); };

                menu.Items.Add(mRefresh);
                menu.Items.Add(mPin);
                menu.Items.Add(mDrawer);
                menu.Items.Add(mPreview);
                menu.Items.Add(mSettings);
                menu.Items.Add(new Separator());
                menu.Items.Add(mExit);
            };
            return menu;
        }

        private void OnMoreSlotsClick(object sender, RoutedEventArgs e)
        {
            var menu = new ContextMenu();
            var slots = WeatherService.GetAllSlots();
            var lang = settings.Language;
            for (var i = 0; i < slots.Count; i++)
            {
                var slotKey = slots[i].Key;
                var item = new MenuItem
                {
                    Header = LocalizationService.SlotLabel(lang, slotKey),
                    IsCheckable = true,
                    IsChecked = String.Equals(settings.ForecastSlotKey, slotKey, StringComparison.OrdinalIgnoreCase)
                };
                item.Click += delegate { SelectForecastSlot(slotKey); };
                menu.Items.Add(item);
            }
            menu.PlacementTarget = moreSlotsBtn;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        }

        private void SelectForecastSlot(string slotKey)
        {
            slotKey = SettingsService.NormalizeSlotKey(slotKey);
            settings.ForecastSlotKey = slotKey;
            settingsService.SaveSettings(settings);
            UpdateSlotPillStyles();

            var projected = weatherService.TryProjectFromMemoryCache(currentDistrict, slotKey);
            if (projected != null)
            {
                currentSnapshot = projected;
                syncState = WeatherSyncState.Live;
                RenderSnapshot();
                return;
            }

            TriggerWeatherRefresh(false);
        }

        private void ToggleDetailsDrawer()
        {
            settings.DetailsExpanded = !settings.DetailsExpanded;
            settingsService.SaveSettings(settings);
            detailsContainer.Visibility = settings.DetailsExpanded ? Visibility.Visible : Visibility.Collapsed;
            ApplyDetailsToggleLabel();
            ApplyWindowModeAndBounds(false);
            UpdateCardAtmosphere(true);
            RenderFiveDayForecast();
        }

        private void OpenSettings(bool focusSearch)
        {
            if (settingsOpen) { return; }
            settingsOpen = true;
            if (settingsView == null)
            {
                settingsView = new SettingsView(catalogService);
                settingsView.CloseRequested += CloseSettings;
                settingsView.DistrictSelected += OnDistrictSelected;
                settingsView.ThemeModeChanged += OnThemeModeChanged;
                settingsView.GlassOpacityChanged += OnGlassOpacityChanged;
                settingsView.LanguageChanged += OnLanguageChanged;
                settingsView.RefreshIntervalChanged += OnRefreshIntervalChanged;
                settingsView.ScenePreviewChanged += OnScenePreviewChanged;
                settingsView.TopmostChanged += SetTopmost;
            }

            weatherViewGrid.Visibility = Visibility.Collapsed;
            if (!foregroundContentGrid.Children.Contains(settingsView))
            {
                foregroundContentGrid.Children.Add(settingsView);
            }
            settingsView.Visibility = Visibility.Visible;
            settingsView.OpenWith(settings, theme, focusSearch, scenePreviewKey);
            ApplyWindowModeAndBounds(false);
            UpdateCardAtmosphere(true);
            UpdateAnimationPlayback();
        }

        private void CloseSettings()
        {
            if (!settingsOpen) { return; }
            settingsOpen = false;
            if (settingsView != null)
            {
                settingsView.Visibility = Visibility.Collapsed;
            }
            weatherViewGrid.Visibility = Visibility.Visible;
            ApplyWindowModeAndBounds(false);
            UpdateCardAtmosphere(true);
            UpdateAnimationPlayback();

            catalogService.UnloadCatalog();
            GlassWindowHelper.TrimWorkingSet();
        }

        private void OnDistrictSelected(DistrictItem district)
        {
            if (district == null) { return; }
            currentDistrict = district;
            RegionCatalogService.CacheIntoSettings(settings, district);
            settingsService.SaveSettings(settings);

            currentSnapshot = weatherService.TryLoadDiskSnapshot(currentDistrict, settings.ForecastSlotKey);
            syncState = currentSnapshot != null ? WeatherSyncState.Refreshing : WeatherSyncState.Loading;
            ApplyLanguageToAll();
            RenderSnapshot();
            TriggerWeatherRefresh(true);
        }

        private void OnThemeModeChanged(string mode)
        {
            settings.ThemeMode = mode;
            settingsService.SaveSettings(settings);
            theme = ThemePalette.Create(ThemePalette.ResolveIsDark(settings.ThemeMode));

            if (String.IsNullOrWhiteSpace(launchOptions.RenderShotsDir))
            {
                glassBackdropActive = GlassWindowHelper.ApplyBackdrop(this, theme.IsDark, useNativeWin11Acrylic);
            }
            ApplyThemeToAll();
            RenderSnapshot();
            if (settingsView != null && settingsOpen)
            {
                settingsView.RefreshThemeAndLabels(settings, theme, scenePreviewKey);
            }
        }

        private void OnGlassOpacityChanged(string mode)
        {
            settings.GlassOpacityMode = mode;
            settingsService.SaveSettings(settings);
            UpdateCardAtmosphere(false);
            if (settingsView != null && settingsOpen)
            {
                settingsView.RefreshThemeAndLabels(settings, theme, scenePreviewKey);
            }
        }

        private void OnScenePreviewChanged(string key)
        {
            scenePreviewKey = String.IsNullOrWhiteSpace(key) ? "live" : key.ToLowerInvariant();
            RenderSnapshot();
            if (settingsView != null && settingsOpen)
            {
                settingsView.RefreshThemeAndLabels(settings, theme, scenePreviewKey);
            }
            UpdateTrayVisual();
        }

        private void OnLanguageChanged(string lang)
        {
            settings.Language = LocalizationService.IsEnglish(lang) ? "en" : "zh";
            settingsService.SaveSettings(settings);
            ApplyLanguageToAll();
            RenderSnapshot();
            if (settingsView != null && settingsOpen)
            {
                settingsView.RefreshThemeAndLabels(settings, theme, scenePreviewKey);
            }
            UpdateTrayVisual();
        }

        private void OnRefreshIntervalChanged(int seconds)
        {
            settings.RefreshSeconds = seconds;
            settingsService.SaveSettings(settings);
            StartAutoRefreshTimer();
            if (settingsView != null && settingsOpen)
            {
                settingsView.RefreshThemeAndLabels(settings, theme, scenePreviewKey);
            }
            TriggerWeatherRefresh(true);
        }

        private void SetTopmost(bool value)
        {
            settings.Topmost = value;
            Topmost = value;
            settingsService.SaveSettings(settings);
            ApplyHeaderIcons();
            if (settingsView != null && settingsOpen)
            {
                settingsView.RefreshThemeAndLabels(settings, theme, scenePreviewKey);
            }
            UpdateTrayVisual();
        }

        private void StartAutoRefreshTimer()
        {
            if (autoRefreshTimer != null)
            {
                autoRefreshTimer.Stop();
            }
            autoRefreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(Math.Max(60, settings.RefreshSeconds))
            };
            autoRefreshTimer.Tick += delegate { TriggerWeatherRefresh(true); };
            autoRefreshTimer.Start();
        }

        private void TriggerWeatherRefresh(bool forceNetwork)
        {
            var myVersion = ++fetchVersion;
            var district = currentDistrict;
            var slotKey = settings.ForecastSlotKey;

            syncState = currentSnapshot != null ? WeatherSyncState.Refreshing : WeatherSyncState.Loading;
            RenderSyncBadge();

            weatherService.FetchWeatherAsync(district, slotKey, forceNetwork, launchOptions.FixtureMode, delegate(WeatherFetchResult res)
            {
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    if (myVersion != fetchVersion) { return; }
                    if (res != null && res.Success && res.Snapshot != null)
                    {
                        currentSnapshot = res.Snapshot;
                        syncState = WeatherSyncState.Live;
                    }
                    else
                    {
                        syncState = currentSnapshot != null ? WeatherSyncState.FailedShowingCache : WeatherSyncState.Failed;
                    }
                    RenderSnapshot();
                    UpdateTrayVisual();
                }));
            });
        }

        private void ResolveEffectiveVisualCode(out int? wmoCode, out int? isDay)
        {
            switch (scenePreviewKey)
            {
                case "sunny": wmoCode = 0; isDay = 1; return;
                case "cloudy": wmoCode = 2; isDay = 1; return;
                case "rain": wmoCode = 63; isDay = 1; return;
                case "storm": wmoCode = 95; isDay = 0; return;
                case "night": wmoCode = 0; isDay = 0; return;
                case "snow": wmoCode = 73; isDay = 1; return;
                default:
                    wmoCode = currentSnapshot != null ? currentSnapshot.WeatherCode : (int?)2;
                    isDay = currentSnapshot != null ? currentSnapshot.IsDay : (int?)1;
                    return;
            }
        }

        private void UpdateAnimationPlayback()
        {
            var shouldAnimate = settings.DrawerExpanded && !settingsOpen && IsVisible && String.IsNullOrWhiteSpace(launchOptions.RenderShotsDir);
            sceneController.SetRunning(shouldAnimate);
        }

        private void ApplyThemeToAll()
        {
            rootCard.BorderBrush = theme.OuterGlassBorder;
            sheenOverlay.Background = theme.TopSheenBrush;
            UpdateCardAtmosphere(true);

            locationPillBtn.Background = theme.InnerGlassCard;
            locationPillBtn.BorderBrush = theme.InnerGlassBorder;
            locationPillBtn.Template = SettingsView.BuildRoundedButtonTemplate(13.5, theme.InnerGlassHover);
            locationPillText.Foreground = theme.TextPrimary;
            locationPinHost.Child = WeatherIcons.MicroIcon(WeatherIcons.GeoPin, theme.TextPrimary, 12, 1.7);

            ApplyHeaderIcons();

            tempBlock.Foreground = theme.TextPrimary;
            conditionBlock.Foreground = theme.TextPrimary;
            feelsRangeBlock.Foreground = theme.TextSecondary;

            primaryMetricsCard.Background = theme.InnerGlassCard;
            primaryMetricsCard.BorderBrush = theme.InnerGlassBorder;
            secondaryMetricsCard.Background = theme.InnerGlassSubtle;
            secondaryMetricsCard.BorderBrush = theme.InnerGlassBorder;

            var metricLabels = new[] { rainProbLabel, humidityLabel, windLabel, todayRainLabel, rainNowLabel, gustLabel, cloudLabel, pressureLabel };
            for (var i = 0; i < metricLabels.Length; i++) { metricLabels[i].Foreground = theme.TextMuted; }

            var metricVals = new[] { rainProbValue, humidityValue, windValue, todayRainValue, rainNowValue, gustValue, cloudValue, pressureValue };
            for (var i = 0; i < metricVals.Length; i++) { metricVals[i].Foreground = theme.TextPrimary; }

            detailsToggleBtn.Template = SettingsView.BuildRoundedButtonTemplate(9, theme.InnerGlassHover);
            detailsToggleText.Foreground = theme.TextSecondary;
            fiveDayTitle.Foreground = theme.TextSecondary;

            footerUpdatedBlock.Foreground = theme.TextMuted;
            footerErrorBlock.Foreground = theme.DangerDot;

            UpdateSlotPillStyles();
            ApplyDetailsToggleLabel();

            int? effWmo, effDay;
            ResolveEffectiveVisualCode(out effWmo, out effDay);
            collapsedPill.ApplyVisual(theme, settings.DrawerEdge, currentSnapshot, settings.Language, effWmo, effDay, useNativeWin11Acrylic);
        }

        private void ApplyHeaderIcons()
        {
            var hover = theme.InnerGlassHover;
            refreshBtn.Template = SettingsView.BuildRoundedButtonTemplate(8, hover);
            pinBtn.Template = SettingsView.BuildRoundedButtonTemplate(8, hover);
            settingsBtn.Template = SettingsView.BuildRoundedButtonTemplate(8, hover);
            collapseBtn.Template = SettingsView.BuildRoundedButtonTemplate(8, hover);
            closeBtn.Template = SettingsView.BuildRoundedButtonTemplate(8, theme.DangerSoft);

            refreshBtn.Content = WeatherIcons.MicroIcon(WeatherIcons.GeoRefresh, theme.TextSecondary, 13, 1.7);
            pinBtn.Content = WeatherIcons.MicroIcon(WeatherIcons.GeoThumbtack, settings.Topmost ? theme.TextPrimary : theme.TextMuted, 13, 1.7);
            pinBtn.Background = settings.Topmost ? theme.AccentSoft : Brushes.Transparent;
            settingsBtn.Content = WeatherIcons.MicroIcon(WeatherIcons.GeoSettings, theme.TextSecondary, 13, 1.7);
            collapseBtn.Content = WeatherIcons.MicroIcon(WeatherIcons.GeoCollapse, theme.TextSecondary, 13, 1.7);
            closeBtn.Content = WeatherIcons.MicroIcon(WeatherIcons.GeoClose, theme.TextSecondary, 12, 1.7);

            var lang = settings.Language;
            refreshBtn.ToolTip = LocalizationService.Ui(lang, "RefreshNow");
            pinBtn.ToolTip = LocalizationService.Ui(lang, settings.Topmost ? "UnpinTopmost" : "PinTopmost");
            settingsBtn.ToolTip = LocalizationService.Ui(lang, "Settings");
            collapseBtn.ToolTip = LocalizationService.Ui(lang, "DrawerCollapse");
            closeBtn.ToolTip = LocalizationService.Ui(lang, "Exit");
        }

        private void ApplyLanguageToAll()
        {
            var lang = settings.Language;
            locationPillText.Text = LocalizationService.FormatShortLocation(lang, currentDistrict);
            locationPillBtn.ToolTip = LocalizationService.FormatFullLocation(lang, currentDistrict);

            rainProbLabel.Text = LocalizationService.Ui(lang, "RainProb");
            humidityLabel.Text = LocalizationService.Ui(lang, "Humidity");
            windLabel.Text = LocalizationService.Ui(lang, "Wind");
            todayRainLabel.Text = LocalizationService.Ui(lang, "TodayRain");
            rainNowLabel.Text = LocalizationService.Ui(lang, "RainNow");
            gustLabel.Text = LocalizationService.Ui(lang, "Gust");
            cloudLabel.Text = LocalizationService.Ui(lang, "Cloud");
            pressureLabel.Text = LocalizationService.Ui(lang, "Pressure");
            fiveDayTitle.Text = LocalizationService.Ui(lang, "FiveDayForecast");

            ApplyHeaderIcons();
            UpdateSlotPillStyles();
            ApplyDetailsToggleLabel();
        }

        private void ApplyDetailsToggleLabel()
        {
            var lang = settings.Language;
            detailsToggleText.Text = LocalizationService.Ui(lang, settings.DetailsExpanded ? "LessDetails" : "MoreDetails");
            detailsToggleChevronHost.Child = WeatherIcons.MicroIcon(
                settings.DetailsExpanded ? WeatherIcons.GeoChevronUp : WeatherIcons.GeoChevronDown,
                theme.TextSecondary,
                10,
                1.8);
        }

        private void UpdateSlotPillStyles()
        {
            var lang = settings.Language;
            var activeSlot = SettingsService.NormalizeSlotKey(settings.ForecastSlotKey);
            var isQuickMatch = false;

            for (var i = 0; i < QuickSlots.Length; i++)
            {
                var key = QuickSlots[i];
                var btn = slotButtons[key];
                var active = String.Equals(activeSlot, key, StringComparison.OrdinalIgnoreCase);
                if (active) { isQuickMatch = true; }
                btn.Content = LocalizationService.SlotPillLabel(lang, key);
                btn.ToolTip = LocalizationService.SlotLabel(lang, key);
                btn.Background = active ? theme.AccentSoft : Brushes.Transparent;
                btn.BorderBrush = active ? theme.InnerGlassBorder : Brushes.Transparent;
                btn.Foreground = active ? theme.TextPrimary : theme.TextSecondary;
                btn.Template = SettingsView.BuildRoundedButtonTemplate(9, theme.InnerGlassHover);
            }

            moreSlotsBtn.Content = !isQuickMatch ? LocalizationService.SlotLabel(lang, activeSlot) : (LocalizationService.Ui(lang, "MoreSlots") + "▾");
            moreSlotsBtn.Background = !isQuickMatch ? theme.AccentSoft : Brushes.Transparent;
            moreSlotsBtn.BorderBrush = !isQuickMatch ? theme.InnerGlassBorder : Brushes.Transparent;
            moreSlotsBtn.Foreground = !isQuickMatch ? theme.TextPrimary : theme.TextMuted;
            moreSlotsBtn.Template = SettingsView.BuildRoundedButtonTemplate(9, theme.InnerGlassHover);
        }

        private void UpdateCardAtmosphere(bool forceRebuild)
        {
            int? effWmo, effDay;
            ResolveEffectiveVisualCode(out effWmo, out effDay);

            var currentHeight = settingsOpen ? SettingsCardHeight : (settings.DetailsExpanded ? DetailedCardHeight : CompactCardHeight);
            sceneController.SetViewportSize(CardWidth, currentHeight);
            rootCard.Background = theme.BuildSkyGradient(effWmo, effDay, glassBackdropActive, settings.GlassOpacityMode);

            var kind = WeatherSceneController.ResolveSceneKind(effWmo, effDay);
            sceneController.SetScene(kind, forceRebuild);

            // Keep water droplets subtle/hidden and add gentle frosted scrim in Settings for razor-sharp readability
            sceneController.ForegroundGlassLayer.Visibility = settingsOpen ? Visibility.Collapsed : Visibility.Visible;
            if (foregroundScrimHost != null)
            {
                foregroundScrimHost.Background = settingsOpen ? ThemePalette.FreezeBrush("#580B1426") : Brushes.Transparent;
            }
            ScheduleWorkingSetTrim();
        }

        private void ScheduleWorkingSetTrim()
        {
            if (!String.IsNullOrWhiteSpace(launchOptions.RenderShotsDir)) { return; }
            if (memoryTrimTimer == null)
            {
                memoryTrimTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
                memoryTrimTimer.Tick += delegate
                {
                    memoryTrimTimer.Stop();
                    GlassWindowHelper.TrimWorkingSet();
                };
            }
            memoryTrimTimer.Stop();
            memoryTrimTimer.Interval = TimeSpan.FromSeconds(3);
            memoryTrimTimer.Start();
        }

        private void RenderSyncBadge()
        {
            var lang = settings.Language;
            if (!String.Equals(scenePreviewKey, "live", StringComparison.OrdinalIgnoreCase))
            {
                statusDot.Background = theme.WarningDot;
                statusText.Text = LocalizationService.IsEnglish(lang) ? "FX" : "预览";
                statusText.Foreground = theme.TextSecondary;
                return;
            }
            switch (syncState)
            {
                case WeatherSyncState.Live:
                    statusDot.Background = theme.SuccessDot;
                    statusText.Text = LocalizationService.Ui(lang, "StateLive");
                    statusText.Foreground = theme.TextSecondary;
                    break;
                case WeatherSyncState.Refreshing:
                case WeatherSyncState.Loading:
                    statusDot.Background = theme.WarningDot;
                    statusText.Text = LocalizationService.Ui(lang, "StateRefreshing");
                    statusText.Foreground = theme.WarningDot;
                    break;
                case WeatherSyncState.Cached:
                    statusDot.Background = theme.Accent;
                    statusText.Text = LocalizationService.Ui(lang, "StateCached");
                    statusText.Foreground = theme.TextSecondary;
                    break;
                case WeatherSyncState.Stale:
                    statusDot.Background = theme.WarningDot;
                    statusText.Text = LocalizationService.Ui(lang, "StateStale");
                    statusText.Foreground = theme.WarningDot;
                    break;
                default:
                    statusDot.Background = theme.DangerDot;
                    statusText.Text = LocalizationService.Ui(lang, "StateFailed");
                    statusText.Foreground = theme.DangerDot;
                    break;
            }
        }

        private void RenderSnapshot()
        {
            var lang = settings.Language;
            int? effWmo, effDay;
            ResolveEffectiveVisualCode(out effWmo, out effDay);

            RenderSyncBadge();
            UpdateCardAtmosphere(false);

            inlineConditionIconHost.Child = WeatherIcons.CreateWeatherIcon(effWmo, effDay, 20, true);

            if (currentSnapshot == null)
            {
                tempBlock.Text = "--°";
                conditionBlock.Text = syncState == WeatherSyncState.Failed
                    ? LocalizationService.Ui(lang, "StateFailed")
                    : LocalizationService.Ui(lang, "StateRefreshing");
                feelsRangeBlock.Text = LocalizationService.Ui(lang, "Feels") + " --°";
                nearTermBadge.Background = theme.InnerGlassCard;
                nearTermBadge.BorderBrush = theme.InnerGlassBorder;
                nearTermText.Foreground = theme.TextSecondary;
                nearTermText.Text = syncState == WeatherSyncState.Failed
                    ? LocalizationService.Ui(lang, "OfflineHint")
                    : LocalizationService.Ui(lang, "StateRefreshing");

                rainProbValue.Text = "--%";
                humidityValue.Text = "--%";
                windValue.Text = "--";
                todayRainValue.Text = "--";
                rainNowValue.Text = "--";
                gustValue.Text = "--";
                cloudValue.Text = "--%";
                pressureValue.Text = "--";

                footerUpdatedBlock.Text = LocalizationService.Ui(lang, syncState == WeatherSyncState.Failed ? "StateFailed" : "StateRefreshing");
                footerErrorBlock.Visibility = syncState == WeatherSyncState.Failed ? Visibility.Visible : Visibility.Collapsed;
                footerErrorBlock.Text = LocalizationService.Ui(lang, "OfflineHint");
                collapsedPill.ApplyVisual(theme, settings.DrawerEdge, null, lang, effWmo, effDay, useNativeWin11Acrylic);
                return;
            }

            tempBlock.Text = currentSnapshot.Temperature.HasValue
                ? currentSnapshot.Temperature.Value.ToString("0.0", CultureInfo.InvariantCulture) + "°"
                : "--°";

            conditionBlock.Text = LocalizationService.WeatherConditionText(
                lang,
                effWmo,
                effDay,
                currentSnapshot.RawConditionFallback);

            var feelsStr = currentSnapshot.FeelsLike.HasValue
                ? LocalizationService.Ui(lang, "Feels") + " " + currentSnapshot.FeelsLike.Value.ToString("0.0", CultureInfo.InvariantCulture) + "°"
                : LocalizationService.Ui(lang, "Feels") + " --°";

            if (currentSnapshot.TempMin.HasValue && currentSnapshot.TempMax.HasValue)
            {
                feelsRangeBlock.Text = String.Format(
                    CultureInfo.InvariantCulture,
                    "{0}  ·  {1:0}° / {2:0}°",
                    feelsStr,
                    currentSnapshot.TempMin.Value,
                    currentSnapshot.TempMax.Value);
            }
            else
            {
                feelsRangeBlock.Text = feelsStr;
            }

            var nearKey = currentSnapshot.NearTermKey;
            if (scenePreviewKey == "rain") { nearKey = "possible"; }
            else if (scenePreviewKey == "storm") { nearKey = "heavy"; }
            else if (scenePreviewKey == "sunny" || scenePreviewKey == "night") { nearKey = "none"; }

            nearTermText.Text = LocalizationService.NearTermText(lang, nearKey);
            nearTermBadge.BorderBrush = theme.InnerGlassBorder;
            if (nearKey == "heavy")
            {
                nearTermBadge.Background = theme.DangerSoft;
                nearTermText.Foreground = theme.DangerDot;
            }
            else if (nearKey == "possible")
            {
                nearTermBadge.Background = theme.AccentSoft;
                nearTermText.Foreground = theme.TextPrimary;
            }
            else
            {
                nearTermBadge.Background = theme.InnerGlassCard;
                nearTermText.Foreground = theme.TextSecondary;
            }

            rainProbValue.Text = FormatPercent(currentSnapshot.RainProbability);
            humidityValue.Text = FormatPercent(currentSnapshot.Humidity);
            windValue.Text = FormatUnit(currentSnapshot.Wind, " km/h", 0);
            todayRainValue.Text = FormatMm(currentSnapshot.TodayRain);

            rainNowValue.Text = FormatMm(currentSnapshot.RainNow);
            gustValue.Text = FormatUnit(currentSnapshot.Gust, " km/h", 0);
            cloudValue.Text = FormatPercent(currentSnapshot.Cloud);
            pressureValue.Text = FormatUnit(currentSnapshot.Pressure, " hPa", 0);

            var localTime = currentSnapshot.FetchedAtUtc.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);
            var src = String.IsNullOrWhiteSpace(currentSnapshot.Source) ? "Open-Meteo" : currentSnapshot.Source;
            footerUpdatedBlock.Text = String.Format(
                CultureInfo.InvariantCulture,
                "{0} {1} · {2}",
                LocalizationService.Ui(lang, "UpdatedPrefix"),
                localTime,
                src);

            if (syncState == WeatherSyncState.FailedShowingCache)
            {
                footerErrorBlock.Text = LocalizationService.Ui(lang, "RefreshFailedHint");
                footerErrorBlock.Visibility = Visibility.Visible;
            }
            else
            {
                footerErrorBlock.Visibility = Visibility.Collapsed;
            }

            RenderFiveDayForecast();
            collapsedPill.ApplyVisual(theme, settings.DrawerEdge, currentSnapshot, lang, effWmo, effDay, useNativeWin11Acrylic);
        }

        private void RenderFiveDayForecast()
        {
            fiveDayGrid.Children.Clear();
            var lang = settings.Language;
            if (currentSnapshot == null || currentSnapshot.DailyForecast == null || currentSnapshot.DailyForecast.Count == 0)
            {
                fiveDayTitle.Text = LocalizationService.ForecastHeading(lang, 5);
                return;
            }

            var activeSlot = SettingsService.NormalizeSlotKey(settings.ForecastSlotKey);
            var count = Math.Min(5, currentSnapshot.DailyForecast.Count);
            fiveDayGrid.Columns = count;
            fiveDayTitle.Text = LocalizationService.ForecastHeading(lang, count);

            // Compute overall min/max span across displayed days for relative temperature range bars
            var globalMin = Double.MaxValue;
            var globalMax = Double.MinValue;
            for (var i = 0; i < count; i++)
            {
                var d = currentSnapshot.DailyForecast[i];
                if (d.TempMin.HasValue && d.TempMin.Value < globalMin) { globalMin = d.TempMin.Value; }
                if (d.TempMax.HasValue && d.TempMax.Value > globalMax) { globalMax = d.TempMax.Value; }
            }
            var hasRange = globalMax > globalMin && globalMin < Double.MaxValue;
            var span = hasRange ? Math.Max(4.0, globalMax - globalMin) : 10.0;

            var rangeBarGrad = new LinearGradientBrush(
                ThemePalette.ColorFrom("#60A5FA"),
                ThemePalette.ColorFrom("#FBBF24"),
                new Point(0, 0.5),
                new Point(1, 0.5));
            rangeBarGrad.Freeze();
            var trackBrush = ThemePalette.FreezeBrush("#24FFFFFF");

            for (var i = 0; i < count; i++)
            {
                var item = currentSnapshot.DailyForecast[i];
                var slotKey = item.SlotKey ?? ("Day" + i.ToString(CultureInfo.InvariantCulture));
                var isSelected = String.Equals(activeSlot, slotKey, StringComparison.OrdinalIgnoreCase);

                var tile = new Button
                {
                    Margin = new Thickness(i == 0 ? 0 : 3, 0, i == count - 1 ? 0 : 3, 0),
                    Padding = new Thickness(4, 7, 4, 7),
                    Background = isSelected ? theme.AccentSoft : theme.InnerGlassSubtle,
                    BorderBrush = isSelected ? theme.OuterGlassBorder : theme.InnerGlassBorder,
                    BorderThickness = new Thickness(1),
                    Cursor = Cursors.Hand,
                    FocusVisualStyle = null,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Template = SettingsView.BuildRoundedButtonTemplate(12, theme.InnerGlassHover)
                };
                tile.Click += delegate { SelectForecastSlot(slotKey); };

                var inner = new StackPanel { HorizontalAlignment = HorizontalAlignment.Stretch };
                inner.Children.Add(new TextBlock
                {
                    Text = LocalizationService.ShortWeekday(lang, item.Date, i),
                    FontFamily = UiFont,
                    FontSize = 10.5,
                    FontWeight = isSelected ? FontWeights.SemiBold : FontWeights.Medium,
                    Foreground = isSelected ? theme.TextPrimary : theme.TextSecondary,
                    HorizontalAlignment = HorizontalAlignment.Center
                });

                var iconWrap = new Border
                {
                    Width = 24,
                    Height = 24,
                    Margin = new Thickness(0, 4, 0, 5),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Child = WeatherIcons.CreateWeatherIcon(item.WeatherCode, 1, 22, true)
                };
                inner.Children.Add(iconWrap);

                // Apple Weather-style relative temperature range bar
                var barTrack = new Grid
                {
                    Height = 3.5,
                    Margin = new Thickness(4, 0, 4, 5)
                };
                barTrack.Children.Add(new Border
                {
                    CornerRadius = new CornerRadius(2),
                    Background = trackBrush
                });
                if (hasRange && item.TempMin.HasValue && item.TempMax.HasValue)
                {
                    var leftFrac = Math.Max(0.0, Math.Min(0.82, (item.TempMin.Value - globalMin) / span));
                    var widthFrac = Math.Max(0.18, Math.Min(1.0 - leftFrac, (item.TempMax.Value - item.TempMin.Value) / span));
                    var barGrid = new Grid();
                    barGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(leftFrac, GridUnitType.Star) });
                    barGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(widthFrac, GridUnitType.Star) });
                    barGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.0, 1.0 - leftFrac - widthFrac), GridUnitType.Star) });
                    var fill = new Border
                    {
                        CornerRadius = new CornerRadius(2),
                        Background = rangeBarGrad
                    };
                    Grid.SetColumn(fill, 1);
                    barGrid.Children.Add(fill);
                    barTrack.Children.Add(barGrid);
                }
                inner.Children.Add(barTrack);

                var hiLo = (item.TempMin.HasValue && item.TempMax.HasValue)
                    ? String.Format(CultureInfo.InvariantCulture, "{0:0}°/{1:0}°", item.TempMin.Value, item.TempMax.Value)
                    : "--";
                inner.Children.Add(new TextBlock
                {
                    Text = hiLo,
                    FontFamily = UiFont,
                    FontSize = 10,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = theme.TextPrimary,
                    HorizontalAlignment = HorizontalAlignment.Center
                });

                tile.Content = inner;
                fiveDayGrid.Children.Add(tile);
            }
        }

        private void CollapseDrawer()
        {
            if (!settings.DrawerExpanded) { return; }
            settings.DrawerExpanded = false;
            if (settingsOpen) { CloseSettings(); }

            var workArea = SystemParameters.WorkArea;
            var midX = workArea.Left + workArea.Width / 2.0;
            settings.DrawerEdge = (Left + Width / 2.0) < midX ? "Left" : "Right";

            int? effWmo, effDay;
            ResolveEffectiveVisualCode(out effWmo, out effDay);
            collapsedPill.ApplyVisual(theme, settings.DrawerEdge, currentSnapshot, settings.Language, effWmo, effDay, useNativeWin11Acrylic);
            Content = collapsedPill;
            Width = CollapsedWidth;
            Height = CollapsedHeight;
            Top = Clamp(Top, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - Height));
            var edgeInset = useNativeWin11Acrylic ? 6.0 : 0.0;
            Left = String.Equals(settings.DrawerEdge, "Left", StringComparison.OrdinalIgnoreCase)
                ? (workArea.Left + edgeInset)
                : (workArea.Right - Width - edgeInset);

            UpdateAnimationPlayback();
            SaveCurrentPlacement();
            UpdateTrayVisual();
            GlassWindowHelper.TrimWorkingSet();
        }

        private void ExpandDrawer()
        {
            if (settings.DrawerExpanded) { return; }
            settings.DrawerExpanded = true;
            Content = rootCard;
            ApplyWindowModeAndBounds(false);

            var workArea = SystemParameters.WorkArea;
            Left = String.Equals(settings.DrawerEdge, "Left", StringComparison.OrdinalIgnoreCase)
                ? workArea.Left + 12
                : workArea.Right - Width - 12;
            Top = Clamp(Top, workArea.Top + 12, Math.Max(workArea.Top, workArea.Bottom - Height - 12));

            UpdateAnimationPlayback();
            SaveCurrentPlacement();
            UpdateTrayVisual();
        }

        private void OnPillVerticalDrag(double newTop)
        {
            var workArea = SystemParameters.WorkArea;
            Top = Clamp(newTop, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - Height));
        }

        private void ApplyWindowModeAndBounds(bool initialLoad)
        {
            var workArea = SystemParameters.WorkArea;
            var targetWidth = settings.DrawerExpanded ? CardWidth : CollapsedWidth;
            var targetHeight = !settings.DrawerExpanded
                ? CollapsedHeight
                : (settingsOpen ? SettingsCardHeight : (settings.DetailsExpanded ? DetailedCardHeight : CompactCardHeight));

            Width = targetWidth;
            Height = Math.Min(targetHeight, Math.Max(CollapsedHeight, workArea.Height - 24));

            if (!String.IsNullOrWhiteSpace(launchOptions.RenderShotsDir))
            {
                Left = -10000;
                Top = -10000;
                return;
            }

            if (initialLoad)
            {
                if (settings.WindowLeft.HasValue && settings.WindowTop.HasValue && IsOnVirtualScreen(settings.WindowLeft.Value, settings.WindowTop.Value, Width, Height))
                {
                    Left = settings.WindowLeft.Value;
                    Top = settings.WindowTop.Value;
                }
                else
                {
                    Left = Math.Max(workArea.Left, workArea.Right - Width - 20);
                    Top = Math.Max(workArea.Top, workArea.Top + 20);
                }
            }
            else
            {
                Top = Clamp(Top, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - Height));
                Left = Clamp(Left, SystemParameters.VirtualScreenLeft, Math.Max(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - Width));
            }
        }

        private void OnCardMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) { return; }
            var src = e.OriginalSource as DependencyObject;
            if (IsInteractiveControl(src)) { return; }
            try
            {
                DragMove();
                SaveCurrentPlacement();
            }
            catch
            {
            }
        }

        private static bool IsInteractiveControl(DependencyObject obj)
        {
            var cur = obj;
            while (cur != null)
            {
                if (cur is ButtonBase || cur is TextBoxBase || cur is Selector || cur is ScrollBar || cur is MenuItem || cur is Popup)
                {
                    return true;
                }
                cur = VisualTreeHelper.GetParent(cur);
            }
            return false;
        }

        private void SaveCurrentPlacement()
        {
            if (!String.IsNullOrWhiteSpace(launchOptions.RenderShotsDir)) { return; }
            if (Double.IsNaN(Left) || Double.IsNaN(Top)) { return; }
            settings.WindowLeft = Left;
            settings.WindowTop = Top;
            settingsService.SavePlacement(settings);
        }

        private void OnWindowClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            sceneController.SetRunning(false);
            SaveCurrentPlacement();
            if (autoRefreshTimer != null) { autoRefreshTimer.Stop(); }
            if (memoryTrimTimer != null) { memoryTrimTimer.Stop(); }
            if (trayService != null)
            {
                trayService.Dispose();
                trayService = null;
            }
        }

        // Deterministic 2x PNG snapshot capture across all v4.1.0 Apple Weather + Liquid Glass scenes
        private void RunOffscreenShotCapture(string outDir)
        {
            try
            {
                Directory.CreateDirectory(outDir);
                glassBackdropActive = true;
                settings.GlassOpacityMode = "airy";
                var res = weatherService.FetchWeatherSync(currentDistrict, "Day0", true, true);
                if (res != null && res.Success && res.Snapshot != null)
                {
                    currentSnapshot = res.Snapshot;
                    syncState = WeatherSyncState.Live;
                }

                // 1. Sunny Day — Apple Weather Big Radiant Dynamic Sun + Liquid Glass
                CaptureSceneShot("sunny", false, "zh", false, 2.4, Path.Combine(outDir, "v41-sunny-day.png"));

                // 2. Rain on Liquid Glass — Falling streaks + 3D Refractive Water Droplets on Glass!
                currentSnapshot.Temperature = 21.3;
                currentSnapshot.FeelsLike = 20.8;
                currentSnapshot.RainProbability = 85;
                currentSnapshot.TodayRain = 14.6;
                currentSnapshot.RainNow = 3.2;
                CaptureSceneShot("rain", true, "zh", false, 3.1, Path.Combine(outDir, "v41-rain-glass.png"));

                // 3. Clear Night Expanded 5-Day (matches user's night screenshot, verifying full 5 days + temp bars + clean upper sky!)
                currentSnapshot.Temperature = 14.0;
                currentSnapshot.FeelsLike = 12.0;
                currentSnapshot.TempMin = 13.0;
                currentSnapshot.TempMax = 16.0;
                currentSnapshot.RainProbability = 20;
                currentSnapshot.TodayRain = 0.0;
                CaptureSceneShot("night", true, "zh", true, 1.8, Path.Combine(outDir, "v42-night-details.png"));

                // 3b. Clear Night Compact
                CaptureSceneShot("night", true, "zh", false, 1.8, Path.Combine(outDir, "v41-clear-night.png"));

                // 4. Thunderstorm + Expanded 5-Day Forecast Details
                currentSnapshot.Temperature = 22.4;
                currentSnapshot.FeelsLike = 23.1;
                currentSnapshot.RainProbability = 95;
                currentSnapshot.TodayRain = 28.4;
                currentSnapshot.RainNow = 8.6;
                CaptureSceneShot("storm", true, "zh", true, 2.2, Path.Combine(outDir, "v41-storm-details.png"));

                // 5. Partly Cloudy English Vivid Mode
                currentSnapshot.Temperature = 24.6;
                currentSnapshot.FeelsLike = 25.8;
                currentSnapshot.RainProbability = 35;
                currentSnapshot.TodayRain = 1.2;
                CaptureSceneShot("cloudy", false, "en", false, 1.5, Path.Combine(outDir, "v41-cloudy-en.png"));

                // 6. Settings + Instant Search + Live FX Preview bar
                scenePreviewKey = "sunny";
                settings.Language = "zh";
                settings.DetailsExpanded = false;
                detailsContainer.Visibility = Visibility.Collapsed;
                theme = ThemePalette.Create(true);
                ApplyThemeToAll();
                OpenSettings(false);
                Width = CardWidth;
                Height = SettingsCardHeight;
                UpdateCardAtmosphere(true);
                sceneController.AdvanceDeterministic(2.0);
                RenderVisualToPng(rootCard, Width, Height, Path.Combine(outDir, "v41-settings.png"));
                CloseSettings();

                // 7. Collapsed Liquid Glass Edge Pill (Sunny)
                scenePreviewKey = "sunny";
                currentSnapshot.Temperature = 26.2;
                theme = ThemePalette.Create(false);
                collapsedPill.ApplyVisual(theme, "Right", currentSnapshot, "zh", 0, 1, false);
                Content = collapsedPill;
                Width = CollapsedWidth;
                Height = CollapsedHeight;
                RenderVisualToPng(collapsedPill, Width, Height, Path.Combine(outDir, "v41-collapsed-pill.png"));
            }
            finally
            {
                Close();
            }
        }

        private void CaptureSceneShot(string previewKey, bool isDark, string lang, bool expandDetails, double previewSec, string filePath)
        {
            scenePreviewKey = previewKey;
            settings.Language = lang;
            settings.DetailsExpanded = expandDetails;
            detailsContainer.Visibility = expandDetails ? Visibility.Visible : Visibility.Collapsed;
            theme = ThemePalette.Create(isDark);
            Width = CardWidth;
            Height = expandDetails ? DetailedCardHeight : CompactCardHeight;
            ApplyThemeToAll();
            ApplyLanguageToAll();
            RenderSnapshot();
            UpdateCardAtmosphere(true);
            sceneController.AdvanceDeterministic(previewSec);
            RenderVisualToPng(rootCard, Width, Height, filePath);
        }

        private void RenderVisualToPng(FrameworkElement visual, double w, double h, string outPath)
        {
            visual.Measure(new Size(w, h));
            visual.Arrange(new Rect(0, 0, w, h));
            visual.UpdateLayout();
            Dispatcher.Invoke(new Action(delegate { }), DispatcherPriority.Render);

            const double dpi = 192.0;
            const double scale = dpi / 96.0;
            var rtb = new RenderTargetBitmap((int)Math.Ceiling(w * scale), (int)Math.Ceiling(h * scale), dpi, dpi, PixelFormats.Pbgra32);
            rtb.Render(visual);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            using (var fs = File.Create(outPath))
            {
                enc.Save(fs);
            }
        }

        private static bool IsOnVirtualScreen(double left, double top, double w, double h)
        {
            var vl = SystemParameters.VirtualScreenLeft;
            var vt = SystemParameters.VirtualScreenTop;
            var vr = vl + SystemParameters.VirtualScreenWidth;
            var vb = vt + SystemParameters.VirtualScreenHeight;
            var ox = Math.Min(left + w, vr) - Math.Max(left, vl);
            var oy = Math.Min(top + h, vb) - Math.Max(top, vt);
            return ox >= 40 && oy >= 40;
        }

        private static double Clamp(double val, double min, double max)
        {
            if (max < min) { return min; }
            if (val < min) { return min; }
            if (val > max) { return max; }
            return val;
        }

        private static string FormatPercent(double? v)
        {
            return v.HasValue ? v.Value.ToString("0", CultureInfo.InvariantCulture) + "%" : "--%";
        }

        private static string FormatMm(double? v)
        {
            return v.HasValue ? v.Value.ToString("0.0", CultureInfo.InvariantCulture) + " mm" : "-- mm";
        }

        private static string FormatUnit(double? v, string unit, int digits)
        {
            return v.HasValue ? v.Value.ToString("F" + digits, CultureInfo.InvariantCulture) + unit : "--" + unit;
        }
    }
}
