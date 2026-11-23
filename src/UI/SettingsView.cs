using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using PaperWeatherWidget.Models;
using PaperWeatherWidget.Services;

namespace PaperWeatherWidget.UI
{
    /// <summary>
    /// Modern in-card Settings & Instant Region Search view.
    /// Replaces the card body smoothly inside a compact height (never expanding to 790px!).
    /// </summary>
    public sealed class SettingsView : Grid
    {
        private sealed class OptionItem
        {
            public string Key { get; set; }
            public string Text { get; set; }
            public object Tag { get; set; }
            public override string ToString() { return Text ?? Key ?? String.Empty; }
        }

        private static readonly FontFamily UiFont = new FontFamily("Segoe UI Variable Display, Segoe UI, Microsoft YaHei UI");

        private readonly RegionCatalogService catalog;
        private AppSettings settings;
        private ThemePalette theme;
        private bool updating;

        private Button backButton;
        private TextBlock headerTitle;
        private TextBlock searchPlaceholderBlock;
        private TextBox searchBox;
        private Border searchBoxBorder;
        private Border searchResultsHost;
        private StackPanel searchResultsPanel;

        private TextBlock provinceLabel;
        private TextBlock cityLabel;
        private TextBlock districtLabel;
        private ComboBox provinceCombo;
        private ComboBox cityCombo;
        private ComboBox districtCombo;

        private TextBlock themeLabel;
        private TextBlock languageLabel;
        private TextBlock refreshLabel;
        private TextBlock topmostLabel;
        private TextBlock startupLabel;

        private Button themeAutoBtn;
        private Button themeLightBtn;
        private Button themeDarkBtn;
        private Button langZhBtn;
        private Button langEnBtn;
        private readonly Dictionary<int, Button> refreshButtons = new Dictionary<int, Button>();
        private Button topmostToggleBtn;
        private Button startupToggleBtn;
        private Button creditBtn;
        private TextBlock creditLabel;

        public event Action CloseRequested;
        public event Action<DistrictItem> DistrictSelected;
        public event Action<string> ThemeModeChanged;
        public event Action<string> LanguageChanged;
        public event Action<int> RefreshIntervalChanged;
        public event Action<bool> TopmostChanged;

        public SettingsView(RegionCatalogService catalogService)
        {
            catalog = catalogService;
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            BuildHeader();
            BuildBody();
            BuildFooter();
        }

        public void OpenWith(AppSettings currentSettings, ThemePalette currentTheme, bool focusSearch)
        {
            settings = currentSettings;
            theme = currentTheme;

            PopulateCatalogControls();
            ApplyThemeAndLabels();
            ClearSearch();

            if (focusSearch)
            {
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    if (searchBox != null)
                    {
                        searchBox.Focus();
                        Keyboard.Focus(searchBox);
                    }
                }));
            }
        }

        public void RefreshThemeAndLabels(AppSettings currentSettings, ThemePalette currentTheme)
        {
            settings = currentSettings;
            theme = currentTheme;
            PopulateCatalogControls();
            ApplyThemeAndLabels();
        }

        private void BuildHeader()
        {
            var header = new Grid { Height = 32, Margin = new Thickness(0, 0, 0, 10) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            backButton = new Button
            {
                Height = 28,
                Padding = new Thickness(8, 0, 10, 0),
                Cursor = Cursors.Hand,
                BorderThickness = new Thickness(0),
                FocusVisualStyle = null,
                FontFamily = UiFont,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold
            };
            backButton.Click += delegate { if (CloseRequested != null) { CloseRequested(); } };

            headerTitle = new TextBlock
            {
                FontFamily = UiFont,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            Grid.SetColumn(headerTitle, 1);

            header.Children.Add(backButton);
            header.Children.Add(headerTitle);
            Grid.SetRow(header, 0);
            Children.Add(header);
        }

        private void BuildBody()
        {
            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            Grid.SetRow(scroll, 1);

            var stack = new StackPanel();
            scroll.Content = stack;

            // 1. Instant Search Box
            searchBoxBorder = new Border
            {
                CornerRadius = new CornerRadius(10),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10, 6, 10, 6),
                Margin = new Thickness(0, 0, 0, 6)
            };
            var searchGrid = new Grid();
            searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var searchIconHost = new Border
            {
                Width = 14,
                Height = 14,
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            searchIconHost.Tag = "searchIcon";
            searchGrid.Children.Add(searchIconHost);

            var inputLayer = new Grid();
            Grid.SetColumn(inputLayer, 1);

            searchPlaceholderBlock = new TextBlock
            {
                FontFamily = UiFont,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false,
                TextTrimming = TextTrimming.CharacterEllipsis
            };

            searchBox = new TextBox
            {
                FontFamily = UiFont,
                FontSize = 12,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(0)
            };
            searchBox.TextChanged += OnSearchTextChanged;

            inputLayer.Children.Add(searchPlaceholderBlock);
            inputLayer.Children.Add(searchBox);
            searchGrid.Children.Add(inputLayer);
            searchBoxBorder.Child = searchGrid;
            stack.Children.Add(searchBoxBorder);

            // Live search results container
            searchResultsHost = new Border
            {
                CornerRadius = new CornerRadius(10),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(4),
                Margin = new Thickness(0, 0, 0, 8),
                Visibility = Visibility.Collapsed
            };
            searchResultsPanel = new StackPanel();
            searchResultsHost.Child = searchResultsPanel;
            stack.Children.Add(searchResultsHost);

            // 2. Cascading Province / City / District
            var regionGrid = new Grid { Margin = new Thickness(0, 2, 0, 10) };
            regionGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            regionGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            regionGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            regionGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            regionGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            provinceLabel = MakeSmallLabel();
            cityLabel = MakeSmallLabel();
            districtLabel = MakeSmallLabel();
            provinceCombo = MakeCombo();
            cityCombo = MakeCombo();
            districtCombo = MakeCombo();

            provinceCombo.SelectionChanged += OnProvinceChanged;
            cityCombo.SelectionChanged += OnCityChanged;
            districtCombo.SelectionChanged += OnDistrictChanged;

            AddRegionCol(regionGrid, 0, provinceLabel, provinceCombo, new Thickness(0, 0, 4, 0));
            AddRegionCol(regionGrid, 1, cityLabel, cityCombo, new Thickness(2, 0, 2, 0));
            AddRegionCol(regionGrid, 2, districtLabel, districtCombo, new Thickness(4, 0, 0, 0));
            stack.Children.Add(regionGrid);

            // 3. Appearance (Theme) & Language side-by-side
            var row2 = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            row2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.35, GridUnitType.Star) });
            row2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.95, GridUnitType.Star) });

            var themeBlock = new StackPanel { Margin = new Thickness(0, 0, 6, 0) };
            themeLabel = MakeSmallLabel();
            var themePills = new UniformGrid { Rows = 1, Columns = 3 };
            themeAutoBtn = MakeSegmentButton(delegate { EmitTheme("auto"); });
            themeLightBtn = MakeSegmentButton(delegate { EmitTheme("light"); });
            themeDarkBtn = MakeSegmentButton(delegate { EmitTheme("dark"); });
            themePills.Children.Add(themeAutoBtn);
            themePills.Children.Add(themeLightBtn);
            themePills.Children.Add(themeDarkBtn);
            themeBlock.Children.Add(themeLabel);
            themeBlock.Children.Add(themePills);
            row2.Children.Add(themeBlock);

            var langBlock = new StackPanel { Margin = new Thickness(6, 0, 0, 0) };
            languageLabel = MakeSmallLabel();
            var langPills = new UniformGrid { Rows = 1, Columns = 2 };
            langZhBtn = MakeSegmentButton(delegate { EmitLanguage("zh"); });
            langEnBtn = MakeSegmentButton(delegate { EmitLanguage("en"); });
            langPills.Children.Add(langZhBtn);
            langPills.Children.Add(langEnBtn);
            langBlock.Children.Add(languageLabel);
            langBlock.Children.Add(langPills);
            Grid.SetColumn(langBlock, 1);
            row2.Children.Add(langBlock);
            stack.Children.Add(row2);

            // 4. Refresh interval segmented bar
            var refreshBlock = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            refreshLabel = MakeSmallLabel();
            var refreshGrid = new UniformGrid { Rows = 1, Columns = 4 };
            var intervals = new[] { 60, 900, 3600, 86400 };
            for (var i = 0; i < intervals.Length; i++)
            {
                var sec = intervals[i];
                var btn = MakeSegmentButton(delegate { EmitRefresh(sec); });
                refreshButtons[sec] = btn;
                refreshGrid.Children.Add(btn);
            }
            refreshBlock.Children.Add(refreshLabel);
            refreshBlock.Children.Add(refreshGrid);
            stack.Children.Add(refreshBlock);

            // 5. Switches row: Always on Top + Start on Boot
            var switchesGrid = new UniformGrid { Rows = 1, Columns = 2, Margin = new Thickness(0, 0, 0, 6) };

            var topCell = new StackPanel { Margin = new Thickness(0, 0, 5, 0) };
            topmostLabel = MakeSmallLabel();
            topmostToggleBtn = MakeToggleButton(OnToggleTopmost);
            topCell.Children.Add(topmostLabel);
            topCell.Children.Add(topmostToggleBtn);
            switchesGrid.Children.Add(topCell);

            var startCell = new StackPanel { Margin = new Thickness(5, 0, 0, 0) };
            startupLabel = MakeSmallLabel();
            startupToggleBtn = MakeToggleButton(OnToggleStartup);
            startCell.Children.Add(startupLabel);
            startCell.Children.Add(startupToggleBtn);
            switchesGrid.Children.Add(startCell);

            stack.Children.Add(switchesGrid);
            Children.Add(scroll);
        }

        private void BuildFooter()
        {
            creditBtn = new Button
            {
                Height = 26,
                Margin = new Thickness(0, 6, 0, 0),
                Padding = new Thickness(6, 0, 6, 0),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                FocusVisualStyle = null,
                HorizontalContentAlignment = HorizontalAlignment.Stretch
            };
            creditBtn.Click += delegate
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
            };

            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            creditLabel = new TextBlock
            {
                FontFamily = UiFont,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            var arrow = new TextBlock
            {
                Text = "↗",
                FontFamily = UiFont,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(arrow, 1);
            row.Children.Add(creditLabel);
            row.Children.Add(arrow);
            creditBtn.Content = row;
            creditBtn.ToolTip = "https://github.com/M47E1/paper-weather-widget";
            Grid.SetRow(creditBtn, 2);
            Children.Add(creditBtn);
        }

        private static TextBlock MakeSmallLabel()
        {
            return new TextBlock
            {
                FontFamily = UiFont,
                FontSize = 10.5,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(2, 0, 0, 4)
            };
        }

        private static void AddRegionCol(Grid grid, int col, TextBlock label, ComboBox combo, Thickness margin)
        {
            label.Margin = new Thickness(margin.Left + 2, 0, margin.Right, 4);
            combo.Margin = margin;
            Grid.SetRow(label, 0);
            Grid.SetColumn(label, col);
            Grid.SetRow(combo, 1);
            Grid.SetColumn(combo, col);
            grid.Children.Add(label);
            grid.Children.Add(combo);
        }

        private ComboBox MakeCombo()
        {
            var combo = new ComboBox
            {
                Height = 30,
                FontFamily = UiFont,
                FontSize = 11.5,
                DisplayMemberPath = "Text",
                MaxDropDownHeight = 200,
                VerticalContentAlignment = VerticalAlignment.Center,
                BorderThickness = new Thickness(1)
            };
            return combo;
        }

        private static Button MakeSegmentButton(Action onClick)
        {
            var btn = new Button
            {
                Height = 28,
                Margin = new Thickness(2, 0, 2, 0),
                FontFamily = UiFont,
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold,
                Cursor = Cursors.Hand,
                BorderThickness = new Thickness(1),
                FocusVisualStyle = null
            };
            btn.Click += delegate { onClick(); };
            return btn;
        }

        private static Button MakeToggleButton(Action onClick)
        {
            var btn = new Button
            {
                Height = 30,
                FontFamily = UiFont,
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold,
                Cursor = Cursors.Hand,
                BorderThickness = new Thickness(1),
                FocusVisualStyle = null
            };
            btn.Click += delegate { onClick(); };
            return btn;
        }

        private void ClearSearch()
        {
            if (searchBox != null)
            {
                searchBox.Text = String.Empty;
            }
            if (searchResultsHost != null)
            {
                searchResultsHost.Visibility = Visibility.Collapsed;
                searchResultsPanel.Children.Clear();
            }
        }

        private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
        {
            var query = searchBox != null ? searchBox.Text : String.Empty;
            if (searchPlaceholderBlock != null)
            {
                searchPlaceholderBlock.Visibility = String.IsNullOrEmpty(query) ? Visibility.Visible : Visibility.Collapsed;
            }

            if (String.IsNullOrWhiteSpace(query))
            {
                searchResultsHost.Visibility = Visibility.Collapsed;
                searchResultsPanel.Children.Clear();
                return;
            }

            var lang = settings != null ? settings.Language : "zh";
            var matches = catalog.Search(query, 6);
            searchResultsPanel.Children.Clear();

            if (matches.Count == 0)
            {
                searchResultsPanel.Children.Add(new TextBlock
                {
                    Text = LocalizationService.Ui(lang, "NoSearchResults"),
                    FontFamily = UiFont,
                    FontSize = 11.5,
                    Foreground = theme.TextMuted,
                    Margin = new Thickness(8, 6, 8, 6)
                });
            }
            else
            {
                for (var i = 0; i < matches.Count; i++)
                {
                    var district = matches[i];
                    var rowBtn = new Button
                    {
                        Height = 28,
                        Padding = new Thickness(8, 0, 8, 0),
                        Background = Brushes.Transparent,
                        BorderThickness = new Thickness(0),
                        HorizontalContentAlignment = HorizontalAlignment.Left,
                        Cursor = Cursors.Hand,
                        FocusVisualStyle = null,
                        Template = BuildRoundedButtonTemplate(6, theme.SurfaceHover)
                    };
                    var txt = new TextBlock
                    {
                        Text = LocalizationService.FormatFullLocation(lang, district),
                        FontFamily = UiFont,
                        FontSize = 11.5,
                        Foreground = theme.TextPrimary,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    rowBtn.Content = txt;
                    rowBtn.Click += delegate
                    {
                        ClearSearch();
                        if (DistrictSelected != null) { DistrictSelected(district); }
                    };
                    searchResultsPanel.Children.Add(rowBtn);
                }
            }
            searchResultsHost.Visibility = Visibility.Visible;
        }

        private void PopulateCatalogControls()
        {
            if (settings == null) { return; }
            updating = true;
            try
            {
                var lang = settings.Language;
                var provinces = catalog.GetProvinces();
                provinceCombo.Items.Clear();
                OptionItem selectedProvince = null;
                for (var i = 0; i < provinces.Count; i++)
                {
                    var p = provinces[i];
                    var item = new OptionItem
                    {
                        Key = p.Key,
                        Text = LocalizationService.IsEnglish(lang) ? LocalizationService.FormatEnglishRegionName(p.En, p.Key) : p.Zh,
                        Tag = p
                    };
                    provinceCombo.Items.Add(item);
                    if (String.Equals(p.Key, settings.ProvinceKey, StringComparison.OrdinalIgnoreCase))
                    {
                        selectedProvince = item;
                    }
                }
                if (selectedProvince == null && provinceCombo.Items.Count > 0)
                {
                    selectedProvince = provinceCombo.Items[0] as OptionItem;
                }
                provinceCombo.SelectedItem = selectedProvince;

                PopulateCities(selectedProvince, settings.CityKey, settings.DistrictKey);
            }
            finally
            {
                updating = false;
            }
        }

        private void PopulateCities(OptionItem provinceOption, string targetCityKey, string targetDistrictKey)
        {
            var lang = settings != null ? settings.Language : "zh";
            cityCombo.Items.Clear();
            var province = provinceOption != null ? provinceOption.Tag as ProvinceItem : null;
            OptionItem selectedCity = null;
            if (province != null)
            {
                for (var i = 0; i < province.Cities.Count; i++)
                {
                    var c = province.Cities[i];
                    var item = new OptionItem
                    {
                        Key = c.Key,
                        Text = LocalizationService.IsEnglish(lang) ? LocalizationService.FormatEnglishRegionName(c.En, c.Key) : c.Zh,
                        Tag = c
                    };
                    cityCombo.Items.Add(item);
                    if (String.Equals(c.Key, targetCityKey, StringComparison.OrdinalIgnoreCase))
                    {
                        selectedCity = item;
                    }
                }
            }
            if (selectedCity == null && cityCombo.Items.Count > 0)
            {
                selectedCity = cityCombo.Items[0] as OptionItem;
            }
            cityCombo.SelectedItem = selectedCity;
            PopulateDistricts(selectedCity, targetDistrictKey);
        }

        private void PopulateDistricts(OptionItem cityOption, string targetDistrictKey)
        {
            var lang = settings != null ? settings.Language : "zh";
            districtCombo.Items.Clear();
            var city = cityOption != null ? cityOption.Tag as CityItem : null;
            OptionItem selectedDistrict = null;
            if (city != null)
            {
                for (var i = 0; i < city.Districts.Count; i++)
                {
                    var d = city.Districts[i];
                    var item = new OptionItem
                    {
                        Key = d.Key,
                        Text = LocalizationService.IsEnglish(lang) ? LocalizationService.FormatEnglishRegionName(d.En, d.Key) : d.Zh,
                        Tag = d
                    };
                    districtCombo.Items.Add(item);
                    if (String.Equals(d.Key, targetDistrictKey, StringComparison.OrdinalIgnoreCase))
                    {
                        selectedDistrict = item;
                    }
                }
            }
            if (selectedDistrict == null && districtCombo.Items.Count > 0)
            {
                selectedDistrict = districtCombo.Items[0] as OptionItem;
            }
            districtCombo.SelectedItem = selectedDistrict;
        }

        private void OnProvinceChanged(object sender, SelectionChangedEventArgs e)
        {
            if (updating) { return; }
            updating = true;
            try
            {
                PopulateCities(provinceCombo.SelectedItem as OptionItem, null, null);
            }
            finally
            {
                updating = false;
            }
            EmitSelectedDistrict();
        }

        private void OnCityChanged(object sender, SelectionChangedEventArgs e)
        {
            if (updating) { return; }
            updating = true;
            try
            {
                PopulateDistricts(cityCombo.SelectedItem as OptionItem, null);
            }
            finally
            {
                updating = false;
            }
            EmitSelectedDistrict();
        }

        private void OnDistrictChanged(object sender, SelectionChangedEventArgs e)
        {
            if (updating) { return; }
            EmitSelectedDistrict();
        }

        private void EmitSelectedDistrict()
        {
            var opt = districtCombo.SelectedItem as OptionItem;
            var d = opt != null ? opt.Tag as DistrictItem : null;
            if (d != null && DistrictSelected != null)
            {
                DistrictSelected(d);
            }
        }

        private void EmitTheme(string mode)
        {
            if (ThemeModeChanged != null) { ThemeModeChanged(mode); }
        }

        private void EmitLanguage(string lang)
        {
            if (LanguageChanged != null) { LanguageChanged(lang); }
        }

        private void EmitRefresh(int seconds)
        {
            if (RefreshIntervalChanged != null) { RefreshIntervalChanged(seconds); }
        }

        private void OnToggleTopmost()
        {
            if (settings == null) { return; }
            if (TopmostChanged != null) { TopmostChanged(!settings.Topmost); }
        }

        private void OnToggleStartup()
        {
            var target = !SettingsService.IsStartupEnabled();
            SettingsService.SetStartupEnabled(target);
            ApplyToggleStyles();
        }

        private void ApplyThemeAndLabels()
        {
            if (theme == null || settings == null) { return; }
            var lang = settings.Language;

            // Header
            var backPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            backPanel.Children.Add(WeatherIcons.MicroIcon(WeatherIcons.GeoBack, theme.TextPrimary, 12, 1.8));
            backPanel.Children.Add(new TextBlock
            {
                Text = LocalizationService.Ui(lang, "Back"),
                Margin = new Thickness(4, 0, 0, 0),
                Foreground = theme.TextPrimary,
                VerticalAlignment = VerticalAlignment.Center
            });
            backButton.Content = backPanel;
            backButton.Background = theme.SurfaceCard;
            backButton.Template = BuildRoundedButtonTemplate(8, theme.SurfaceHover);

            headerTitle.Text = LocalizationService.Ui(lang, "Settings");
            headerTitle.Foreground = theme.TextPrimary;

            // Search box
            searchBoxBorder.Background = theme.InputBg;
            searchBoxBorder.BorderBrush = theme.SurfaceBorder;
            var searchGrid = searchBoxBorder.Child as Grid;
            if (searchGrid != null && searchGrid.Children.Count > 0)
            {
                var iconHost = searchGrid.Children[0] as Border;
                if (iconHost != null)
                {
                    iconHost.Child = WeatherIcons.MicroIcon(WeatherIcons.GeoSearch, theme.TextMuted, 13, 1.6);
                }
            }
            searchBox.Foreground = theme.TextPrimary;
            searchBox.CaretBrush = theme.Accent;
            searchPlaceholderBlock.Text = LocalizationService.Ui(lang, "SearchPlaceholder");
            searchPlaceholderBlock.Foreground = theme.TextMuted;

            searchResultsHost.Background = theme.PopupBg;
            searchResultsHost.BorderBrush = theme.SurfaceBorder;

            // Labels
            provinceLabel.Text = LocalizationService.Ui(lang, "Province");
            cityLabel.Text = LocalizationService.Ui(lang, "City");
            districtLabel.Text = LocalizationService.Ui(lang, "District");
            themeLabel.Text = LocalizationService.Ui(lang, "Theme");
            languageLabel.Text = LocalizationService.Ui(lang, "Language");
            refreshLabel.Text = LocalizationService.Ui(lang, "Refresh");
            topmostLabel.Text = LocalizationService.Ui(lang, "TopmostLabel");
            startupLabel.Text = LocalizationService.Ui(lang, "Startup");

            var labels = new[] { provinceLabel, cityLabel, districtLabel, themeLabel, languageLabel, refreshLabel, topmostLabel, startupLabel };
            for (var i = 0; i < labels.Length; i++)
            {
                labels[i].Foreground = theme.TextMuted;
            }

            StyleCombo(provinceCombo);
            StyleCombo(cityCombo);
            StyleCombo(districtCombo);

            // Theme pills
            themeAutoBtn.Content = LocalizationService.Ui(lang, "ThemeAuto");
            themeLightBtn.Content = LocalizationService.Ui(lang, "ThemeLight");
            themeDarkBtn.Content = LocalizationService.Ui(lang, "ThemeDark");
            StyleSegment(themeAutoBtn, String.Equals(settings.ThemeMode, "auto", StringComparison.OrdinalIgnoreCase));
            StyleSegment(themeLightBtn, String.Equals(settings.ThemeMode, "light", StringComparison.OrdinalIgnoreCase));
            StyleSegment(themeDarkBtn, String.Equals(settings.ThemeMode, "dark", StringComparison.OrdinalIgnoreCase));

            // Language pills
            langZhBtn.Content = "中文";
            langEnBtn.Content = "EN";
            StyleSegment(langZhBtn, !LocalizationService.IsEnglish(lang));
            StyleSegment(langEnBtn, LocalizationService.IsEnglish(lang));

            // Refresh pills
            foreach (var kv in refreshButtons)
            {
                kv.Value.Content = LocalizationService.FormatRefreshShort(lang, kv.Key);
                StyleSegment(kv.Value, settings.RefreshSeconds == kv.Key);
            }

            ApplyToggleStyles();

            creditLabel.Text = LocalizationService.Ui(lang, "CreditPrefix") + " · M47E1/paper-weather-widget";
            creditLabel.Foreground = theme.TextMuted;
            creditBtn.Foreground = theme.TextMuted;
            creditBtn.Template = BuildRoundedButtonTemplate(6, theme.SurfaceHover);
        }

        private void ApplyToggleStyles()
        {
            if (settings == null || theme == null) { return; }
            var lang = settings.Language;
            StyleToggle(topmostToggleBtn, settings.Topmost, lang);
            StyleToggle(startupToggleBtn, SettingsService.IsStartupEnabled(), lang);
        }

        private void StyleToggle(Button btn, bool active, string lang)
        {
            btn.Content = LocalizationService.Ui(lang, active ? "On" : "Off");
            btn.Background = active ? theme.AccentSoft : theme.SurfaceCard;
            btn.BorderBrush = active ? theme.Accent : theme.SurfaceBorder;
            btn.Foreground = active ? theme.AccentText : theme.TextSecondary;
            btn.Template = BuildRoundedButtonTemplate(8, theme.SurfaceHover);
        }

        private void StyleSegment(Button btn, bool active)
        {
            btn.Background = active ? theme.AccentSoft : theme.SurfaceCard;
            btn.BorderBrush = active ? theme.Accent : theme.SurfaceBorder;
            btn.Foreground = active ? theme.AccentText : theme.TextSecondary;
            btn.Template = BuildRoundedButtonTemplate(8, theme.SurfaceHover);
        }

        private void StyleCombo(ComboBox combo)
        {
            combo.Background = theme.InputBg;
            combo.Foreground = theme.TextPrimary;
            combo.BorderBrush = theme.SurfaceBorder;
            combo.Template = BuildComboTemplate(theme);
            combo.ItemContainerStyle = BuildComboItemStyle(theme);
        }

        private static ControlTemplate BuildComboTemplate(ThemePalette t)
        {
            var tpl = new ControlTemplate(typeof(ComboBox));
            var grid = new FrameworkElementFactory(typeof(Grid));

            var border = new FrameworkElementFactory(typeof(Border));
            border.Name = "Root";
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
            grid.AppendChild(border);

            var text = new FrameworkElementFactory(typeof(TextBlock));
            text.SetBinding(TextBlock.TextProperty, new Binding("Text") { RelativeSource = RelativeSource.TemplatedParent });
            text.SetValue(TextBlock.ForegroundProperty, new TemplateBindingExtension(Control.ForegroundProperty));
            text.SetValue(TextBlock.FontSizeProperty, new TemplateBindingExtension(Control.FontSizeProperty));
            text.SetValue(TextBlock.FontFamilyProperty, UiFont);
            text.SetValue(TextBlock.FontWeightProperty, FontWeights.Medium);
            text.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            text.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
            text.SetValue(TextBlock.MarginProperty, new Thickness(8, 0, 20, 0));
            text.SetValue(UIElement.IsHitTestVisibleProperty, false);
            grid.AppendChild(text);

            var arrow = new FrameworkElementFactory(typeof(TextBlock));
            arrow.SetValue(TextBlock.TextProperty, "▾");
            arrow.SetValue(TextBlock.FontSizeProperty, 11.0);
            arrow.SetValue(TextBlock.ForegroundProperty, t.TextMuted);
            arrow.SetValue(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Right);
            arrow.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
            arrow.SetValue(TextBlock.MarginProperty, new Thickness(0, 0, 7, 0));
            arrow.SetValue(UIElement.IsHitTestVisibleProperty, false);
            grid.AppendChild(arrow);

            var toggle = new FrameworkElementFactory(typeof(ToggleButton));
            toggle.SetValue(Control.TemplateProperty, BuildTransparentToggleTemplate());
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
            popupBorder.SetValue(Border.BackgroundProperty, t.PopupBg);
            popupBorder.SetValue(Border.BorderBrushProperty, t.WindowBorder);
            popupBorder.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            popupBorder.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
            popupBorder.SetValue(Border.PaddingProperty, new Thickness(3));
            popupBorder.SetValue(Border.MarginProperty, new Thickness(0, 3, 0, 0));
            popupBorder.SetBinding(FrameworkElement.MinWidthProperty, new Binding("ActualWidth") { RelativeSource = RelativeSource.TemplatedParent });

            var scroll = new FrameworkElementFactory(typeof(ScrollViewer));
            scroll.SetValue(ScrollViewer.MaxHeightProperty, 190.0);
            scroll.SetValue(ScrollViewer.CanContentScrollProperty, true);
            scroll.AppendChild(new FrameworkElementFactory(typeof(ItemsPresenter)));
            popupBorder.AppendChild(scroll);
            popup.AppendChild(popupBorder);
            grid.AppendChild(popup);

            tpl.VisualTree = grid;
            return tpl;
        }

        private static ControlTemplate BuildTransparentToggleTemplate()
        {
            var tpl = new ControlTemplate(typeof(ToggleButton));
            var b = new FrameworkElementFactory(typeof(Border));
            b.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            tpl.VisualTree = b;
            return tpl;
        }

        private static Style BuildComboItemStyle(ThemePalette t)
        {
            var style = new Style(typeof(ComboBoxItem));
            style.Setters.Add(new Setter(Control.ForegroundProperty, t.TextPrimary));
            style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 5, 8, 5)));
            style.Setters.Add(new Setter(Control.FontFamilyProperty, UiFont));
            style.Setters.Add(new Setter(Control.FontSizeProperty, 11.5));

            var tpl = new ControlTemplate(typeof(ComboBoxItem));
            var bd = new FrameworkElementFactory(typeof(Border));
            bd.Name = "ItemBd";
            bd.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            bd.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
            bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(5));
            bd.AppendChild(new FrameworkElementFactory(typeof(ContentPresenter)));
            tpl.VisualTree = bd;

            var hl = new Trigger { Property = ComboBoxItem.IsHighlightedProperty, Value = true };
            hl.Setters.Add(new Setter(Border.BackgroundProperty, t.SurfaceHover, "ItemBd"));
            tpl.Triggers.Add(hl);

            var sel = new Trigger { Property = Selector.IsSelectedProperty, Value = true };
            sel.Setters.Add(new Setter(Border.BackgroundProperty, t.AccentSoft, "ItemBd"));
            tpl.Triggers.Add(sel);

            style.Setters.Add(new Setter(Control.TemplateProperty, tpl));
            return style;
        }

        public static ControlTemplate BuildRoundedButtonTemplate(double radius, Brush hoverBg)
        {
            var tpl = new ControlTemplate(typeof(Button));
            var bd = new FrameworkElementFactory(typeof(Border));
            bd.Name = "BtnBd";
            bd.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            bd.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            bd.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
            bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(radius));
            bd.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));

            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, new TemplateBindingExtension(Control.HorizontalContentAlignmentProperty));
            cp.SetValue(ContentPresenter.VerticalAlignmentProperty, new TemplateBindingExtension(Control.VerticalContentAlignmentProperty));
            bd.AppendChild(cp);
            tpl.VisualTree = bd;

            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Border.BackgroundProperty, hoverBg, "BtnBd"));
            tpl.Triggers.Add(hover);

            return tpl;
        }
    }
}
