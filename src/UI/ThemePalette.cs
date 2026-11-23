using System;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace PaperWeatherWidget.UI
{
    public sealed class ThemePalette
    {
        public bool IsDark { get; private set; }
        public uint AcrylicTintAbgr { get; private set; }

        public Brush WindowBg { get; private set; }
        public Brush WindowBorder { get; private set; }
        public Brush SurfaceCard { get; private set; }
        public Brush SurfaceSubtle { get; private set; }
        public Brush SurfaceHover { get; private set; }
        public Brush SurfaceBorder { get; private set; }
        public Brush Divider { get; private set; }

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
        public Brush PillBg { get; private set; }

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
            return isDark ? DarkInstance : LightInstance;
        }

        public static readonly ThemePalette DarkInstance = BuildDark();
        public static readonly ThemePalette LightInstance = BuildLight();

        private static ThemePalette BuildDark()
        {
            return new ThemePalette
            {
                IsDark = true,
                AcrylicTintAbgr = 0xCC1E1814u, // translucent dark obsidian
                WindowBg = FreezeBrush("#EE151922"),
                WindowBorder = FreezeBrush("#2AFFFFFF"),
                SurfaceCard = FreezeBrush("#14FFFFFF"),
                SurfaceSubtle = FreezeBrush("#0DFFFFFF"),
                SurfaceHover = FreezeBrush("#22FFFFFF"),
                SurfaceBorder = FreezeBrush("#1CFFFFFF"),
                Divider = FreezeBrush("#16FFFFFF"),

                TextPrimary = FreezeBrush("#F3F6FC"),
                TextSecondary = FreezeBrush("#A8B2C5"),
                TextMuted = FreezeBrush("#727E96"),

                Accent = FreezeBrush("#60A5FA"),
                AccentSoft = FreezeBrush("#2B60A5FA"),
                AccentText = FreezeBrush("#93C5FD"),
                SuccessDot = FreezeBrush("#34D399"),
                WarningDot = FreezeBrush("#FBBF24"),
                DangerDot = FreezeBrush("#F87171"),
                DangerSoft = FreezeBrush("#26F87171"),

                InputBg = FreezeBrush("#1AFFFFFF"),
                PopupBg = FreezeBrush("#F71B202C"),
                PillBg = FreezeBrush("#E6161B26")
            };
        }

        private static ThemePalette BuildLight()
        {
            return new ThemePalette
            {
                IsDark = false,
                AcrylicTintAbgr = 0xB8F9F7F4u, // crisp frosted warm porcelain
                WindowBg = FreezeBrush("#F2F6F7FA"),
                WindowBorder = FreezeBrush("#1F0F172A"),
                SurfaceCard = FreezeBrush("#D9FFFFFF"),
                SurfaceSubtle = FreezeBrush("#99FFFFFF"),
                SurfaceHover = FreezeBrush("#120F172A"),
                SurfaceBorder = FreezeBrush("#160F172A"),
                Divider = FreezeBrush("#120F172A"),

                TextPrimary = FreezeBrush("#111827"),
                TextSecondary = FreezeBrush("#4B5563"),
                TextMuted = FreezeBrush("#7B8494"),

                Accent = FreezeBrush("#2563EB"),
                AccentSoft = FreezeBrush("#1A2563EB"),
                AccentText = FreezeBrush("#1D4ED8"),
                SuccessDot = FreezeBrush("#10B981"),
                WarningDot = FreezeBrush("#D97706"),
                DangerDot = FreezeBrush("#DC2626"),
                DangerSoft = FreezeBrush("#1ADC2626"),

                InputBg = FreezeBrush("#F2FFFFFF"),
                PopupBg = FreezeBrush("#FAFFFFFF"),
                PillBg = FreezeBrush("#EBFFFFFF")
            };
        }

        public Brush BuildAtmosphereBackground(int? wmoCode, int? isDay, bool glassActive)
        {
            Color baseTop;
            Color baseBottom;
            Color aura;

            var night = isDay.HasValue && isDay.Value == 0;
            var storm = wmoCode.HasValue && wmoCode.Value >= 95;
            var rain = wmoCode.HasValue && ((wmoCode.Value >= 51 && wmoCode.Value <= 67) || (wmoCode.Value >= 80 && wmoCode.Value <= 82));
            var clear = wmoCode.HasValue && wmoCode.Value <= 1;

            if (IsDark)
            {
                var alpha = glassActive ? (byte)0xCC : (byte)0xF5;
                baseBottom = Color.FromArgb(alpha, 0x13, 0x17, 0x20);
                if (storm)
                {
                    baseTop = Color.FromArgb(alpha, 0x21, 0x1B, 0x33);
                    aura = Color.FromArgb(0x28, 0xA7, 0x8B, 0xFA);
                }
                else if (rain)
                {
                    baseTop = Color.FromArgb(alpha, 0x15, 0x21, 0x33);
                    aura = Color.FromArgb(0x26, 0x38, 0xBD, 0xF8);
                }
                else if (night)
                {
                    baseTop = Color.FromArgb(alpha, 0x15, 0x1A, 0x2E);
                    aura = Color.FromArgb(0x24, 0x81, 0x8C, 0xF8);
                }
                else if (clear)
                {
                    baseTop = Color.FromArgb(alpha, 0x24, 0x1E, 0x1B);
                    aura = Color.FromArgb(0x24, 0xFB, 0xBF, 0x24);
                }
                else
                {
                    baseTop = Color.FromArgb(alpha, 0x1A, 0x20, 0x2C);
                    aura = Color.FromArgb(0x1E, 0x94, 0xA3, 0xB8);
                }
            }
            else
            {
                var alpha = glassActive ? (byte)0xD8 : (byte)0xFA;
                baseBottom = Color.FromArgb(alpha, 0xF4, 0xF6, 0xFA);
                if (storm)
                {
                    baseTop = Color.FromArgb(alpha, 0xEE, 0xEC, 0xF8);
                    aura = Color.FromArgb(0x22, 0x7C, 0x3A, 0xED);
                }
                else if (rain)
                {
                    baseTop = Color.FromArgb(alpha, 0xE9, 0xF2, 0xFB);
                    aura = Color.FromArgb(0x24, 0x02, 0x84, 0xC7);
                }
                else if (night)
                {
                    baseTop = Color.FromArgb(alpha, 0xEC, 0xEF, 0xF8);
                    aura = Color.FromArgb(0x20, 0x4F, 0x46, 0xE5);
                }
                else if (clear)
                {
                    baseTop = Color.FromArgb(alpha, 0xFD, 0xF7, 0xEC);
                    aura = Color.FromArgb(0x26, 0xF5, 0x9E, 0x0B);
                }
                else
                {
                    baseTop = Color.FromArgb(alpha, 0xF2, 0xF5, 0xF9);
                    aura = Color.FromArgb(0x18, 0x64, 0x74, 0x8B);
                }
            }

            var brush = new LinearGradientBrush();
            brush.StartPoint = new Point(0.15, 0.0);
            brush.EndPoint = new Point(0.85, 1.0);
            brush.GradientStops.Add(new GradientStop(Blend(baseTop, aura), 0.0));
            brush.GradientStops.Add(new GradientStop(baseTop, 0.35));
            brush.GradientStops.Add(new GradientStop(baseBottom, 1.0));
            brush.Freeze();
            return brush;
        }

        private static Color Blend(Color bg, Color fg)
        {
            var a = fg.A / 255.0;
            var r = (byte)Math.Min(255, bg.R * (1.0 - a) + fg.R * a);
            var g = (byte)Math.Min(255, bg.G * (1.0 - a) + fg.G * a);
            var b = (byte)Math.Min(255, bg.B * (1.0 - a) + fg.B * a);
            return Color.FromArgb(bg.A, r, g, b);
        }

        public static SolidColorBrush FreezeBrush(string hex)
        {
            var c = (Color)ColorConverter.ConvertFromString(hex);
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }
    }
}
