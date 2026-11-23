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

        private const double CardWidth = 340;
        private const double CompactCardHeight = 316;
        private const double DetailedCardHeight = 472;
        private const double SettingsCardHeight = 408;
        private const double CollapsedWidth = 54;
        private const double CollapsedHeight = 104;

        private readonly LaunchOptions launchOptions;
        private readonly SettingsService settingsService;
        private readonly RegionCatalogService catalogService;
        private readonly WeatherService weatherService;
        private TrayService trayService;

        private AppSettings settings;
        private DistrictItem currentDistrict;
        private WeatherSnapshot currentSnapshot;
        private WeatherSyncState syncState = WeatherSyncState.Loading;
        private ThemePalette theme;
        private bool glassBackdropActive;
        private bool settingsOpen;
        private int fetchVersion;
        private string lastErrorText = String.Empty;

        private DispatcherTimer autoRefreshTimer;
        private DispatcherTimer memoryTrimTimer;

        // Visual tree
        private Border rootCard;
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
        private TextBlock conditionBlock;
        private TextBlock feelsRangeBlock;
        private Border nearTermBadge;
        private TextBlock nearTermText;
        private Border heroIconHost;

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

            settings = settingsService.Load();
            if (!String.IsNullOrWhiteSpace(launchOptions.OverrideTheme))
            {
                settings.ThemeMode = launchOptions.OverrideTheme;
            }
            if (!String.IsNullOrWhiteSpace(launchOptions.OverrideLang))
            {
                settings.Language = launchOptions.OverrideLang;
            }
            if (launchOptions.DisableTopmost)
            {
                settings.Topmost = false;
            }

            currentDistrict = catalogService.ResolveFromSettings(settings);
            // If we just hydrated cached coords/names from catalog on first upgrade from v3, save & unload catalog!
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

            // Instant zero-latency boot from disk snapshot if available
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
            AllowsTransparency = true;
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
                glassBackdropActive = GlassWindowHelper.ApplyBackdrop(this, theme.IsDark, theme.AcrylicTintAbgr);
                UpdateCardAtmosphere();
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

            // Trim unused startup pages after 4 seconds
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
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        Close();
                    }));
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
                var cond = LocalizationService.WeatherConditionText(settings.Language, currentSnapshot.WeatherCode, currentSnapshot.IsDay, currentSnapshot.RawConditionFallback);
                tip = String.Format(CultureInfo.InvariantCulture, "{0} · {1} {2:0.0}°C", loc, cond, currentSnapshot.Temperature.Value);
            }
            trayService.UpdateState(settings.Language, settings.DrawerExpanded, settings.Topmost, tip);
        }

        private void BuildUiTree()
        {
            rootCard = new Border
            {
                CornerRadius = new CornerRadius(18),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(14, 12, 14, 12),
                ClipToBounds = true
            };
            rootCard.MouseLeftButtonDown += OnCardMouseLeftButtonDown;
            rootCard.ContextMenu = BuildContextMenu();

            var container = new Grid();
            rootCard.Child = container;

            weatherViewGrid = BuildWeatherView();
            container.Children.Add(weatherViewGrid);

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
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // spacer
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 7: Footer

            // Row 0: Header bar
            var header = new Grid { Height = 28, Margin = new Thickness(0, 0, 0, 8) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            locationPillBtn = new Button
            {
                Height = 26,
                Padding = new Thickness(7, 0, 8, 0),
                BorderThickness = new Thickness(0),
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

            // Compact live status indicator
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
                FontWeight = FontWeights.Medium,
                VerticalAlignment = VerticalAlignment.Center
            };
            statusPanel.Children.Add(statusDot);
            statusPanel.Children.Add(statusText);
            Grid.SetColumn(statusPanel, 1);
            header.Children.Add(statusPanel);

            // Minimal crisp vector action buttons
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

            // Row 1: Hero weather section
            var hero = new Grid { Margin = new Thickness(2, 2, 2, 12) };
            hero.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            hero.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var heroLeft = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var tempRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom };
            tempBlock = new TextBlock
            {
                Text = "--°",
                FontFamily = UiFont,
                FontSize = 44,
                FontWeight = FontWeights.SemiBold,
                LineHeight = 46,
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                Margin = new Thickness(0, 0, 8, 0)
            };
            conditionBlock = new TextBlock
            {
                Text = "--",
                FontFamily = UiFont,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, 6),
                MaxWidth = 104,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            tempRow.Children.Add(tempBlock);
            tempRow.Children.Add(conditionBlock);
            heroLeft.Children.Add(tempRow);

            feelsRangeBlock = new TextBlock
            {
                FontFamily = UiFont,
                FontSize = 11.5,
                Margin = new Thickness(1, 3, 0, 7)
            };
            heroLeft.Children.Add(feelsRangeBlock);

            nearTermBadge = new Border
            {
                CornerRadius = new CornerRadius(7),
                Padding = new Thickness(8, 3, 8, 3),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            nearTermText = new TextBlock
            {
                FontFamily = UiFont,
                FontSize = 10.5,
                FontWeight = FontWeights.Medium,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            nearTermBadge.Child = nearTermText;
            heroLeft.Children.Add(nearTermBadge);

            hero.Children.Add(heroLeft);

            heroIconHost = new Border
            {
                Width = 76,
                Height = 76,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(6, 0, 0, 0)
            };
            Grid.SetColumn(heroIconHost, 1);
            hero.Children.Add(heroIconHost);

            Grid.SetRow(hero, 1);
            grid.Children.Add(hero);

            // Row 2: One-click forecast switcher strip right on the main card
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
                    Height = 25,
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
                Height = 25,
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

            // Row 3: Primary 4-column glass metrics strip
            primaryMetricsCard = new Border
            {
                CornerRadius = new CornerRadius(12),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(6, 9, 6, 9),
                Margin = new Thickness(0, 0, 0, 8)
            };
            var primaryGrid = new UniformGrid { Rows = 1, Columns = 4 };
            primaryGrid.Children.Add(BuildMetricCell(out rainProbLabel, out rainProbValue));
            primaryGrid.Children.Add(BuildMetricCell(out humidityLabel, out humidityValue));
            primaryGrid.Children.Add(BuildMetricCell(out windLabel, out windValue));
            primaryGrid.Children.Add(BuildMetricCell(out todayRainLabel, out todayRainValue));
            primaryMetricsCard.Child = primaryGrid;
            Grid.SetRow(primaryMetricsCard, 3);
            grid.Children.Add(primaryMetricsCard);

            // Row 4: Expandable details toggle row
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
                FontWeight = FontWeights.Medium,
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

            // Row 5: Expandable details (remaining 4 metrics + 5-day mini forecast)
            detailsContainer = new StackPanel
            {
                Visibility = settings.DetailsExpanded ? Visibility.Visible : Visibility.Collapsed,
                Margin = new Thickness(0, 2, 0, 4)
            };

            secondaryMetricsCard = new Border
            {
                CornerRadius = new CornerRadius(12),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(6, 8, 6, 8),
                Margin = new Thickness(0, 0, 0, 8)
            };
            var secondaryGrid = new UniformGrid { Rows = 1, Columns = 4 };
            secondaryGrid.Children.Add(BuildMetricCell(out rainNowLabel, out rainNowValue));
            secondaryGrid.Children.Add(BuildMetricCell(out gustLabel, out gustValue));
            secondaryGrid.Children.Add(BuildMetricCell(out cloudLabel, out cloudValue));
            secondaryGrid.Children.Add(BuildMetricCell(out pressureLabel, out pressureValue));
            secondaryMetricsCard.Child = secondaryGrid;
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
                FontSize = 12.5,
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

            // Instant projection from memory cache -> 0 network calls!
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
                settingsView.LanguageChanged += OnLanguageChanged;
                settingsView.RefreshIntervalChanged += OnRefreshIntervalChanged;
                settingsView.TopmostChanged += SetTopmost;
            }

            var container = rootCard.Child as Grid;
            if (container != null)
            {
                weatherViewGrid.Visibility = Visibility.Collapsed;
                if (!container.Children.Contains(settingsView))
                {
                    container.Children.Add(settingsView);
                }
                settingsView.Visibility = Visibility.Visible;
            }
            settingsView.OpenWith(settings, theme, focusSearch);
            ApplyWindowModeAndBounds(false);
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

            // Release the 2,875-district catalog from memory when Settings closes!
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
                glassBackdropActive = GlassWindowHelper.ApplyBackdrop(this, theme.IsDark, theme.AcrylicTintAbgr);
            }
            ApplyThemeToAll();
            RenderSnapshot();
            if (settingsView != null && settingsOpen)
            {
                settingsView.RefreshThemeAndLabels(settings, theme);
            }
        }

        private void OnLanguageChanged(string lang)
        {
            settings.Language = LocalizationService.IsEnglish(lang) ? "en" : "zh";
            settingsService.SaveSettings(settings);
            ApplyLanguageToAll();
            RenderSnapshot();
            if (settingsView != null && settingsOpen)
            {
                settingsView.RefreshThemeAndLabels(settings, theme);
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
                settingsView.RefreshThemeAndLabels(settings, theme);
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
                settingsView.RefreshThemeAndLabels(settings, theme);
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
            lastErrorText = String.Empty;
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
                        lastErrorText = String.Empty;
                    }
                    else
                    {
                        lastErrorText = res != null ? res.ErrorMessage : String.Empty;
                        syncState = currentSnapshot != null ? WeatherSyncState.FailedShowingCache : WeatherSyncState.Failed;
                    }
                    RenderSnapshot();
                    UpdateTrayVisual();
                }));
            });
        }

        private void ApplyThemeToAll()
        {
            rootCard.BorderBrush = theme.WindowBorder;
            UpdateCardAtmosphere();

            locationPillBtn.Background = theme.SurfaceCard;
            locationPillBtn.Template = SettingsView.BuildRoundedButtonTemplate(13, theme.SurfaceHover);
            locationPillText.Foreground = theme.TextPrimary;
            locationPinHost.Child = WeatherIcons.MicroIcon(WeatherIcons.GeoPin, theme.AccentText, 12, 1.7);

            ApplyHeaderIcons();

            tempBlock.Foreground = theme.TextPrimary;
            conditionBlock.Foreground = theme.TextSecondary;
            feelsRangeBlock.Foreground = theme.TextSecondary;

            primaryMetricsCard.Background = theme.SurfaceCard;
            primaryMetricsCard.BorderBrush = theme.SurfaceBorder;
            secondaryMetricsCard.Background = theme.SurfaceSubtle;
            secondaryMetricsCard.BorderBrush = theme.SurfaceBorder;

            var metricLabels = new[] { rainProbLabel, humidityLabel, windLabel, todayRainLabel, rainNowLabel, gustLabel, cloudLabel, pressureLabel };
            for (var i = 0; i < metricLabels.Length; i++) { metricLabels[i].Foreground = theme.TextMuted; }

            var metricVals = new[] { rainProbValue, humidityValue, windValue, todayRainValue, rainNowValue, gustValue, cloudValue, pressureValue };
            for (var i = 0; i < metricVals.Length; i++) { metricVals[i].Foreground = theme.TextPrimary; }

            detailsToggleBtn.Template = SettingsView.BuildRoundedButtonTemplate(8, theme.SurfaceHover);
            detailsToggleText.Foreground = theme.TextMuted;
            fiveDayTitle.Foreground = theme.TextMuted;

            footerUpdatedBlock.Foreground = theme.TextMuted;
            footerErrorBlock.Foreground = theme.DangerDot;

            UpdateSlotPillStyles();
            ApplyDetailsToggleLabel();
            collapsedPill.ApplyVisual(theme, settings.DrawerEdge, currentSnapshot, settings.Language);
        }

        private void ApplyHeaderIcons()
        {
            var hover = theme.SurfaceHover;
            refreshBtn.Template = SettingsView.BuildRoundedButtonTemplate(7, hover);
            pinBtn.Template = SettingsView.BuildRoundedButtonTemplate(7, hover);
            settingsBtn.Template = SettingsView.BuildRoundedButtonTemplate(7, hover);
            collapseBtn.Template = SettingsView.BuildRoundedButtonTemplate(7, hover);
            closeBtn.Template = SettingsView.BuildRoundedButtonTemplate(7, theme.DangerSoft);

            refreshBtn.Content = WeatherIcons.MicroIcon(WeatherIcons.GeoRefresh, theme.TextSecondary, 13, 1.6);
            pinBtn.Content = WeatherIcons.MicroIcon(WeatherIcons.GeoThumbtack, settings.Topmost ? theme.AccentText : theme.TextMuted, 13, 1.6);
            pinBtn.Background = settings.Topmost ? theme.AccentSoft : Brushes.Transparent;
            settingsBtn.Content = WeatherIcons.MicroIcon(WeatherIcons.GeoSettings, theme.TextSecondary, 13, 1.6);
            collapseBtn.Content = WeatherIcons.MicroIcon(WeatherIcons.GeoCollapse, theme.TextSecondary, 13, 1.6);
            closeBtn.Content = WeatherIcons.MicroIcon(WeatherIcons.GeoClose, theme.TextSecondary, 12, 1.6);

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
                theme.TextMuted,
                10,
                1.7);
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
                btn.BorderBrush = active ? theme.Accent : Brushes.Transparent;
                btn.Foreground = active ? theme.AccentText : theme.TextSecondary;
                btn.Template = SettingsView.BuildRoundedButtonTemplate(8, theme.SurfaceHover);
            }

            moreSlotsBtn.Content = !isQuickMatch ? LocalizationService.SlotLabel(lang, activeSlot) : (LocalizationService.Ui(lang, "MoreSlots") + "▾");
            moreSlotsBtn.Background = !isQuickMatch ? theme.AccentSoft : Brushes.Transparent;
            moreSlotsBtn.BorderBrush = !isQuickMatch ? theme.Accent : Brushes.Transparent;
            moreSlotsBtn.Foreground = !isQuickMatch ? theme.AccentText : theme.TextMuted;
            moreSlotsBtn.Template = SettingsView.BuildRoundedButtonTemplate(8, theme.SurfaceHover);
        }

        private void UpdateCardAtmosphere()
        {
            var code = currentSnapshot != null ? currentSnapshot.WeatherCode : (int?)2;
            var isDay = currentSnapshot != null ? currentSnapshot.IsDay : (int?)1;
            rootCard.Background = theme.BuildAtmosphereBackground(code, isDay, glassBackdropActive);
        }

        private void RenderSyncBadge()
        {
            var lang = settings.Language;
            switch (syncState)
            {
                case WeatherSyncState.Live:
                    statusDot.Background = theme.SuccessDot;
                    statusText.Text = LocalizationService.Ui(lang, "StateLive");
                    statusText.Foreground = theme.TextMuted;
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
                    statusText.Foreground = theme.TextMuted;
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
            RenderSyncBadge();
            UpdateCardAtmosphere();

            if (currentSnapshot == null)
            {
                tempBlock.Text = "--°";
                conditionBlock.Text = syncState == WeatherSyncState.Failed
                    ? LocalizationService.Ui(lang, "StateFailed")
                    : LocalizationService.Ui(lang, "StateRefreshing");
                feelsRangeBlock.Text = LocalizationService.Ui(lang, "Feels") + " --°";
                nearTermBadge.Background = theme.SurfaceCard;
                nearTermText.Foreground = theme.TextMuted;
                nearTermText.Text = syncState == WeatherSyncState.Failed
                    ? LocalizationService.Ui(lang, "OfflineHint")
                    : LocalizationService.Ui(lang, "StateRefreshing");
                heroIconHost.Child = WeatherIcons.CreateWeatherIcon(2, 1, 74, theme.IsDark);

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
                collapsedPill.ApplyVisual(theme, settings.DrawerEdge, null, lang);
                return;
            }

            tempBlock.Text = currentSnapshot.Temperature.HasValue
                ? currentSnapshot.Temperature.Value.ToString("0.0", CultureInfo.InvariantCulture) + "°"
                : "--°";

            conditionBlock.Text = LocalizationService.WeatherConditionText(
                lang,
                currentSnapshot.WeatherCode,
                currentSnapshot.IsDay,
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

            // Near-term rain badge
            nearTermText.Text = LocalizationService.NearTermText(lang, currentSnapshot.NearTermKey);
            if (currentSnapshot.NearTermKey == "heavy")
            {
                nearTermBadge.Background = theme.DangerSoft;
                nearTermText.Foreground = theme.DangerDot;
            }
            else if (currentSnapshot.NearTermKey == "possible")
            {
                nearTermBadge.Background = theme.AccentSoft;
                nearTermText.Foreground = theme.AccentText;
            }
            else
            {
                nearTermBadge.Background = theme.SurfaceCard;
                nearTermText.Foreground = theme.TextSecondary;
            }

            heroIconHost.Child = WeatherIcons.CreateWeatherIcon(
                currentSnapshot.WeatherCode,
                currentSnapshot.IsDay,
                74,
                theme.IsDark);

            // Primary 4 metrics
            rainProbValue.Text = FormatPercent(currentSnapshot.RainProbability);
            humidityValue.Text = FormatPercent(currentSnapshot.Humidity);
            windValue.Text = FormatUnit(currentSnapshot.Wind, " km/h", 0);
            todayRainValue.Text = FormatMm(currentSnapshot.TodayRain);

            // Secondary 4 metrics
            rainNowValue.Text = FormatMm(currentSnapshot.RainNow);
            gustValue.Text = FormatUnit(currentSnapshot.Gust, " km/h", 0);
            cloudValue.Text = FormatPercent(currentSnapshot.Cloud);
            pressureValue.Text = FormatUnit(currentSnapshot.Pressure, " hPa", 0);

            // Footer
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
            collapsedPill.ApplyVisual(theme, settings.DrawerEdge, currentSnapshot, lang);
        }

        private void RenderFiveDayForecast()
        {
            fiveDayGrid.Children.Clear();
            if (currentSnapshot == null || currentSnapshot.DailyForecast == null || currentSnapshot.DailyForecast.Count == 0)
            {
                return;
            }

            var lang = settings.Language;
            var activeSlot = SettingsService.NormalizeSlotKey(settings.ForecastSlotKey);
            var count = Math.Min(5, currentSnapshot.DailyForecast.Count);

            for (var i = 0; i < count; i++)
            {
                var item = currentSnapshot.DailyForecast[i];
                var slotKey = item.SlotKey ?? ("Day" + i.ToString(CultureInfo.InvariantCulture));
                var isSelected = String.Equals(activeSlot, slotKey, StringComparison.OrdinalIgnoreCase);

                var tile = new Button
                {
                    Margin = new Thickness(i == 0 ? 0 : 3, 0, i == count - 1 ? 0 : 3, 0),
                    Padding = new Thickness(2, 6, 2, 6),
                    Background = isSelected ? theme.AccentSoft : theme.SurfaceSubtle,
                    BorderBrush = isSelected ? theme.Accent : theme.SurfaceBorder,
                    BorderThickness = new Thickness(1),
                    Cursor = Cursors.Hand,
                    FocusVisualStyle = null,
                    Template = SettingsView.BuildRoundedButtonTemplate(10, theme.SurfaceHover)
                };
                tile.Click += delegate { SelectForecastSlot(slotKey); };

                var inner = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
                inner.Children.Add(new TextBlock
                {
                    Text = LocalizationService.ShortWeekday(lang, item.Date, i),
                    FontFamily = UiFont,
                    FontSize = 10,
                    FontWeight = isSelected ? FontWeights.SemiBold : FontWeights.Medium,
                    Foreground = isSelected ? theme.AccentText : theme.TextSecondary,
                    HorizontalAlignment = HorizontalAlignment.Center
                });

                var iconWrap = new Border
                {
                    Width = 24,
                    Height = 24,
                    Margin = new Thickness(0, 4, 0, 4),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Child = WeatherIcons.CreateWeatherIcon(item.WeatherCode, 1, 22, theme.IsDark)
                };
                inner.Children.Add(iconWrap);

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

            collapsedPill.ApplyVisual(theme, settings.DrawerEdge, currentSnapshot, settings.Language);
            Content = collapsedPill;
            Width = CollapsedWidth;
            Height = CollapsedHeight;
            Top = Clamp(Top, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - Height));
            Left = String.Equals(settings.DrawerEdge, "Left", StringComparison.OrdinalIgnoreCase)
                ? workArea.Left
                : workArea.Right - Width;

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
            SaveCurrentPlacement();
            if (autoRefreshTimer != null) { autoRefreshTimer.Stop(); }
            if (memoryTrimTimer != null) { memoryTrimTimer.Stop(); }
            if (trayService != null)
            {
                trayService.Dispose();
                trayService = null;
            }
        }

        // Deterministic 2x PNG snapshot capture for visual QA and before/after comparison
        private void RunOffscreenShotCapture(string outDir)
        {
            try
            {
                Directory.CreateDirectory(outDir);

                // Ensure fixture data is loaded synchronously for deterministic comparison
                var res = weatherService.FetchWeatherSync(currentDistrict, "Day0", true, true);
                if (res != null && res.Success && res.Snapshot != null)
                {
                    currentSnapshot = res.Snapshot;
                    syncState = WeatherSyncState.Live;
                }

                // 1. Dark Compact
                settings.ThemeMode = "dark";
                settings.Language = "zh";
                settings.DetailsExpanded = false;
                settings.DrawerExpanded = true;
                theme = ThemePalette.Create(true);
                ApplyThemeToAll();
                ApplyLanguageToAll();
                RenderSnapshot();
                detailsContainer.Visibility = Visibility.Collapsed;
                Width = CardWidth;
                Height = CompactCardHeight;
                RenderVisualToPng(rootCard, Width, Height, Path.Combine(outDir, "after-v4-dark-compact.png"));

                // 2. Dark Expanded Details + 5-day
                settings.DetailsExpanded = true;
                detailsContainer.Visibility = Visibility.Visible;
                ApplyDetailsToggleLabel();
                RenderFiveDayForecast();
                Height = DetailedCardHeight;
                RenderVisualToPng(rootCard, Width, Height, Path.Combine(outDir, "after-v4-dark-details.png"));

                // 3. Dark Settings + Search
                OpenSettings(false);
                Height = SettingsCardHeight;
                RenderVisualToPng(rootCard, Width, Height, Path.Combine(outDir, "after-v4-dark-settings.png"));
                CloseSettings();

                // 4. Light Compact
                settings.ThemeMode = "light";
                settings.DetailsExpanded = false;
                detailsContainer.Visibility = Visibility.Collapsed;
                theme = ThemePalette.Create(false);
                ApplyThemeToAll();
                ApplyLanguageToAll();
                RenderSnapshot();
                Height = CompactCardHeight;
                RenderVisualToPng(rootCard, Width, Height, Path.Combine(outDir, "after-v4-light-compact.png"));

                // 5. Light English Compact
                settings.Language = "en";
                ApplyLanguageToAll();
                RenderSnapshot();
                RenderVisualToPng(rootCard, Width, Height, Path.Combine(outDir, "after-v4-light-en.png"));

                // 6. Collapsed Edge Pill (Dark)
                settings.ThemeMode = "dark";
                settings.Language = "zh";
                theme = ThemePalette.Create(true);
                collapsedPill.ApplyVisual(theme, "Right", currentSnapshot, "zh");
                Content = collapsedPill;
                Width = CollapsedWidth;
                Height = CollapsedHeight;
                RenderVisualToPng(collapsedPill, Width, Height, Path.Combine(outDir, "after-v4-collapsed-pill.png"));
            }
            finally
            {
                Close();
            }
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
