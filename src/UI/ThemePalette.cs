using System;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace PaperWeatherWidget.UI
{
    /// <summary>
    /// v4.1.0 Apple Weather + iOS 26/27 Liquid Glass material system.
    /// Ultra-translucent DWM frosted blur + vertical sky wash + refractive specular rims.
    /// </summary>
    public sealed class ThemePalette
    {
        public bool IsDark { get; private set; }
        public uint AcrylicTintAbgr { get; private set; }

        public Brush OuterGlassBorder { get; private set; }
        public Brush InnerGlassCard { get; private set; }
        public Brush InnerGlassSubtle { get; private set; }
        public Brush InnerGlassHover { get; private set; }
        public Brush InnerGlassBorder { get; private set; }
        public Brush TopSheenBrush { get; private set; }
        public Brush PillGlassBg { get; private set; }

        public Brush TextPrimary { get; private set; }
        public Brush TextSecondary { get; private set; }
        public Brush TextMuted { get; private set; }

        public Brush Accent { get; private set; }
        public Brush AccentSoft { get; private set; }
        public Brush AccentText { get; private set; }
        public Brush SuccessDot { get; private set; }
        public Brush WarningDot { get; private set; }
        public Brush DangerDot { get; private set; }
        public Brush DangerSoft { get; private set; }

        public Brush InputBg { get; private set; }
        public Brush PopupBg { get; private set; }

        public Brush WindowBorder { get { return OuterGlassBorder; } }
        public Brush SurfaceCard { get { return InnerGlassCard; } }
        public Brush SurfaceSubtle { get { return InnerGlassSubtle; } }
        public Brush SurfaceHover { get { return InnerGlassHover; } }
        public Brush SurfaceBorder { get { return InnerGlassBorder; } }
        public Brush PillBg { get { return PillGlassBg; } }

        public static bool DetectSystemDarkTheme()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", false))
                {
                    if (key != null)
                    {
                        var val = key.GetValue("AppsUseLightTheme");
                        if (val != null)
                        {
                            return Convert.ToInt32(val) == 0;
                        }
                    }
                }
            }
            catch
            {
            }
            return true;
        }

        public static bool ResolveIsDark(string themeMode)
        {
            if (String.Equals(themeMode, "dark", StringComparison.OrdinalIgnoreCase)) { return true; }
            if (String.Equals(themeMode, "light", StringComparison.OrdinalIgnoreCase)) { return false; }
            return DetectSystemDarkTheme();
        }

        public static ThemePalette Create(bool isDark)
        {
            return isDark ? DarkGlassInstance : LightGlassInstance;
        }

        public static readonly ThemePalette DarkGlassInstance = BuildLiquidGlass(true);
        public static readonly ThemePalette LightGlassInstance = BuildLiquidGlass(false);

        private static ThemePalette BuildLiquidGlass(bool dark)
        {
            // iOS 26/27 Liquid Glass outer specular rim: bright top-left highlight, clear midsection, soft bottom-right bounce
            var outerRim = new LinearGradientBrush();
            outerRim.StartPoint = new Point(0, 0);
            outerRim.EndPoint = new Point(1, 1);
            outerRim.GradientStops.Add(new GradientStop(ColorFrom(dark ? "#A6FFFFFF" : "#D8FFFFFF"), 0.0));
            outerRim.GradientStops.Add(new GradientStop(ColorFrom(dark ? "#2EFFFFFF" : "#52FFFFFF"), 0.36));
            outerRim.GradientStops.Add(new GradientStop(ColorFrom(dark ? "#14FFFFFF" : "#26FFFFFF"), 0.66));
            outerRim.GradientStops.Add(new GradientStop(ColorFrom(dark ? "#58FFFFFF" : "#88FFFFFF"), 1.0));
            outerRim.Freeze();

            var innerRim = new LinearGradientBrush();
            innerRim.StartPoint = new Point(0, 0);
            innerRim.EndPoint = new Point(1, 1);
            innerRim.GradientStops.Add(new GradientStop(ColorFrom(dark ? "#66FFFFFF" : "#96FFFFFF"), 0.0));
            innerRim.GradientStops.Add(new GradientStop(ColorFrom(dark ? "#18FFFFFF" : "#2CFFFFFF"), 0.48));
            innerRim.GradientStops.Add(new GradientStop(ColorFrom(dark ? "#38FFFFFF" : "#60FFFFFF"), 1.0));
            innerRim.Freeze();

            // Delicate curved glass sheen along upper-left bevel + soft bottom caustic bounce
            var sheen = new LinearGradientBrush();
            sheen.StartPoint = new Point(0.18, 0.0);
            sheen.EndPoint = new Point(0.72, 1.0);
            sheen.GradientStops.Add(new GradientStop(ColorFrom(dark ? "#34FFFFFF" : "#44FFFFFF"), 0.0));
            sheen.GradientStops.Add(new GradientStop(ColorFrom(dark ? "#10FFFFFF" : "#18FFFFFF"), 0.18));
            sheen.GradientStops.Add(new GradientStop(ColorFrom("#00FFFFFF"), 0.38));
            sheen.GradientStops.Add(new GradientStop(ColorFrom("#00FFFFFF"), 0.86));
            sheen.GradientStops.Add(new GradientStop(ColorFrom(dark ? "#18FFFFFF" : "#24FFFFFF"), 1.0));
            sheen.Freeze();

            return new ThemePalette
            {
                IsDark = dark,
                // Low-alpha DWM acrylic tint (~14%!) so Windows 11 native frosted Gaussian blur really shines through
                AcrylicTintAbgr = dark ? 0x2414100Cu : 0x20ECE6DEu,

                OuterGlassBorder = outerRim,
                InnerGlassCard = FreezeBrush(dark ? "#2009111E" : "#28FFFFFF"),
                InnerGlassSubtle = FreezeBrush(dark ? "#1609111E" : "#1BFFFFFF"),
                InnerGlassHover = FreezeBrush(dark ? "#38FFFFFF" : "#46FFFFFF"),
                InnerGlassBorder = innerRim,
                TopSheenBrush = sheen,
                PillGlassBg = FreezeBrush(dark ? "#8C121D30" : "#942466B5"),

                TextPrimary = FreezeBrush("#FFFFFF"),
                TextSecondary = FreezeBrush(dark ? "#E6F0F8FF" : "#F5FFFFFF"),
                TextMuted = FreezeBrush(dark ? "#B8DCEAF8" : "#D0FFFFFF"),

                Accent = FreezeBrush(dark ? "#99FFFFFF" : "#CCFFFFFF"),
                AccentSoft = FreezeBrush(dark ? "#34FFFFFF" : "#44FFFFFF"),
                AccentText = FreezeBrush("#FFFFFF"),
                SuccessDot = FreezeBrush("#4ADE80"),
                WarningDot = FreezeBrush("#FDE047"),
                DangerDot = FreezeBrush("#FCA5A5"),
                DangerSoft = FreezeBrush("#4AEF4444"),

                InputBg = FreezeBrush(dark ? "#36080F1C" : "#340C2342"),
                PopupBg = FreezeBrush(dark ? "#EB101829" : "#EB1A3A63")
            };
        }

        public Brush BuildSkyGradient(int? wmoCode, int? isDay, bool glassActive, string glassOpacityMode)
        {
            var code = wmoCode ?? 2;
            var night = isDay.HasValue && isDay.Value == 0;
            var storm = code >= 95;
            var snow = (code >= 71 && code <= 77) || code == 85 || code == 86;
            var rain = (code >= 51 && code <= 67) || (code >= 80 && code <= 82);
            var fog = code == 45 || code == 48;
            var clear = code <= 1;
            var partlyCloudy = code == 2;

            // True Liquid Glass vertical translucency taper:
            // Upper sky has atmospheric color tint; lower half fades more transparent so desktop wallpaper blurs through!
            byte aTop, aMid, aBot;
            if (!glassActive)
            {
                aTop = 0xEA; aMid = 0xE0; aBot = 0xD2;
            }
            else if (String.Equals(glassOpacityMode, "rich", StringComparison.OrdinalIgnoreCase))
            {
                aTop = 0xC8; aMid = 0xB4; aBot = 0x98;
            }
            else if (String.Equals(glassOpacityMode, "balanced", StringComparison.OrdinalIgnoreCase))
            {
                aTop = 0xA4; aMid = 0x8A; aBot = 0x6A;
            }
            else
            {
                // "airy" (default high-translucency iOS Liquid Glass!)
                aTop = 0x84; aMid = 0x68; aBot = 0x48;
            }

            Color cTop, cMid, cBot;
            if (storm)
            {
                cTop = Argb(aTop, 0x16, 0x1D, 0x33);
                cMid = Argb(aMid, 0x24, 0x2F, 0x4E);
                cBot = Argb(aBot, 0x18, 0x21, 0x38);
            }
            else if (rain)
            {
                cTop = IsDark ? Argb(aTop, 0x1A, 0x2C, 0x46) : Argb(aTop, 0x2A, 0x49, 0x6E);
                cMid = IsDark ? Argb(aMid, 0x26, 0x40, 0x60) : Argb(aMid, 0x3D, 0x63, 0x8D);
                cBot = IsDark ? Argb(aBot, 0x19, 0x2A, 0x40) : Argb(aBot, 0x2E, 0x4D, 0x72);
            }
            else if (snow)
            {
                cTop = IsDark ? Argb(aTop, 0x27, 0x3E, 0x5B) : Argb(aTop, 0x4D, 0x77, 0xA4);
                cMid = IsDark ? Argb(aMid, 0x3A, 0x55, 0x76) : Argb(aMid, 0x6C, 0x98, 0xC4);
                cBot = IsDark ? Argb(aBot, 0x2D, 0x46, 0x66) : Argb(aBot, 0x5A, 0x85, 0xB1);
            }
            else if (night)
            {
                cTop = Argb(aTop, 0x0A, 0x12, 0x29);
                cMid = Argb(aMid, 0x16, 0x25, 0x4C);
                cBot = Argb(aBot, 0x1E, 0x30, 0x5E);
            }
            else if (clear)
            {
                cTop = IsDark ? Argb(aTop, 0x13, 0x52, 0xB2) : Argb(aTop, 0x1A, 0x70, 0xE2);
                cMid = IsDark ? Argb(aMid, 0x24, 0x73, 0xD1) : Argb(aMid, 0x38, 0x96, 0xF0);
                cBot = IsDark ? Argb(aBot, 0x3B, 0x8B, 0xD8) : Argb(aBot, 0x62, 0xB8, 0xFA);
            }
            else if (partlyCloudy)
            {
                cTop = IsDark ? Argb(aTop, 0x1F, 0x4E, 0x8A) : Argb(aTop, 0x2E, 0x72, 0xBC);
                cMid = IsDark ? Argb(aMid, 0x32, 0x68, 0xA6) : Argb(aMid, 0x4E, 0x92, 0xD2);
                cBot = IsDark ? Argb(aBot, 0x2E, 0x58, 0x8A) : Argb(aBot, 0x66, 0xA0, 0xD6);
            }
            else if (fog)
            {
                cTop = IsDark ? Argb(aTop, 0x32, 0x41, 0x54) : Argb(aTop, 0x58, 0x6E, 0x86);
                cMid = IsDark ? Argb(aMid, 0x46, 0x57, 0x6B) : Argb(aMid, 0x72, 0x89, 0xA2);
                cBot = IsDark ? Argb(aBot, 0x36, 0x46, 0x59) : Argb(aBot, 0x5D, 0x74, 0x8C);
            }
            else
            {
                cTop = IsDark ? Argb(aTop, 0x26, 0x37, 0x4E) : Argb(aTop, 0x44, 0x5E, 0x7C);
                cMid = IsDark ? Argb(aMid, 0x38, 0x4C, 0x68) : Argb(aMid, 0x5B, 0x78, 0x98);
                cBot = IsDark ? Argb(aBot, 0x29, 0x3B, 0x53) : Argb(aBot, 0x4A, 0x66, 0x85);
            }

            var brush = new LinearGradientBrush();
            brush.StartPoint = new Point(0.2, 0.0);
            brush.EndPoint = new Point(0.8, 1.0);
            brush.GradientStops.Add(new GradientStop(cTop, 0.0));
            brush.GradientStops.Add(new GradientStop(cMid, 0.50));
            brush.GradientStops.Add(new GradientStop(cBot, 1.0));
            brush.Freeze();
            return brush;
        }

        public Brush BuildSkyGradient(int? wmoCode, int? isDay, bool glassActive)
        {
            return BuildSkyGradient(wmoCode, isDay, glassActive, "airy");
        }

        private static Color Argb(byte a, byte r, byte g, byte b)
        {
            return Color.FromArgb(a, r, g, b);
        }

        public static Color ColorFrom(string hex)
        {
            return (Color)ColorConverter.ConvertFromString(hex);
        }

        public static SolidColorBrush FreezeBrush(string hex)
        {
            var b = new SolidColorBrush(ColorFrom(hex));
            b.Freeze();
            return b;
        }
    }
}
