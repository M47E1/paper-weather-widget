using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace PaperWeatherWidget.UI
{
    /// <summary>
    /// Crisp pure-vector WPF weather icons and UI micro-icons.
    /// Zero bitmap memory overhead, DPI-independent, and zero expensive BlurEffect passes.
    /// </summary>
    public static class WeatherIcons
    {
        public static UIElement CreateWeatherIcon(int? wmoCode, int? isDay, double size, bool isDark)
        {
            var viewbox = new Viewbox
            {
                Width = size,
                Height = size,
                Stretch = Stretch.Uniform
            };

            var canvas = new Canvas { Width = 64, Height = 64 };
            viewbox.Child = canvas;

            var code = wmoCode ?? 2;
            var night = isDay.HasValue && isDay.Value == 0;

            if (code >= 95)
            {
                DrawGlow(canvas, "#44A78BFA", 32, 30, 28);
                DrawCloud(canvas, 10, 14, 1.0, "#64748B", "#475569");
                DrawLightning(canvas, 28, 34);
                DrawRainDrop(canvas, 20, 44, "#60A5FA");
                DrawRainDrop(canvas, 44, 44, "#60A5FA");
                return viewbox;
            }

            if ((code >= 71 && code <= 77) || code == 85 || code == 86)
            {
                DrawGlow(canvas, "#3893C5FD", 32, 30, 28);
                DrawCloud(canvas, 10, 14, 1.0, isDark ? "#CBD5E1" : "#94A3B8", isDark ? "#94A3B8" : "#64748B");
                DrawSnowDot(canvas, 21, 46, isDark ? "#E0F2FE" : "#38BDF8");
                DrawSnowDot(canvas, 32, 50, isDark ? "#E0F2FE" : "#38BDF8");
                DrawSnowDot(canvas, 43, 46, isDark ? "#E0F2FE" : "#38BDF8");
                return viewbox;
            }

            if ((code >= 51 && code <= 67) || (code >= 80 && code <= 82))
            {
                var heavy = code == 65 || code == 82 || code == 55;
                DrawGlow(canvas, "#3A38BDF8", 32, 30, 28);
                DrawCloud(canvas, 10, 14, 1.0, isDark ? "#94A3B8" : "#64748B", isDark ? "#64748B" : "#475569");
                DrawRainDrop(canvas, 20, 42, "#38BDF8");
                DrawRainDrop(canvas, 31, 46, "#60A5FA");
                DrawRainDrop(canvas, 42, 42, "#38BDF8");
                if (heavy)
                {
                    DrawRainDrop(canvas, 26, 52, "#0EA5E9");
                    DrawRainDrop(canvas, 37, 52, "#0EA5E9");
                }
                return viewbox;
            }

            if (code == 45 || code == 48)
            {
                DrawGlow(canvas, "#2E94A3B8", 32, 30, 26);
                DrawCloud(canvas, 11, 13, 0.94, isDark ? "#CBD5E1" : "#94A3B8", isDark ? "#94A3B8" : "#64748B");
                DrawFogLine(canvas, 14, 43, 36, isDark ? "#94A3B8" : "#64748B");
                DrawFogLine(canvas, 18, 49, 28, isDark ? "#64748B" : "#94A3B8");
                return viewbox;
            }

            if (code == 0 || code == 1)
            {
                if (night)
                {
                    DrawGlow(canvas, "#38818CF8", 32, 32, 26);
                    DrawMoon(canvas, 16, 15);
                    DrawStar(canvas, 46, 18, 4.5, "#FDE68A");
                    DrawStar(canvas, 49, 31, 3.0, "#BFDBFE");
                }
                else
                {
                    DrawGlow(canvas, "#4DFBBF24", 32, 32, 28);
                    DrawSun(canvas, 32, 32, 13, true);
                }
                return viewbox;
            }

            if (code == 2)
            {
                if (night)
                {
                    DrawGlow(canvas, "#32818CF8", 38, 22, 20);
                    DrawMoonSmall(canvas, 31, 10);
                    DrawCloud(canvas, 8, 22, 0.96, isDark ? "#E2E8F0" : "#CBD5E1", isDark ? "#94A3B8" : "#94A3B8");
                }
                else
                {
                    DrawGlow(canvas, "#44FBBF24", 41, 22, 22);
                    DrawSun(canvas, 42, 22, 9.5, false);
                    DrawCloud(canvas, 8, 22, 0.96, isDark ? "#F1F5F9" : "#FFFFFF", isDark ? "#CBD5E1" : "#CBD5E1");
                }
                return viewbox;
            }

            // Overcast / default cloudy (code 3)
            DrawGlow(canvas, "#2694A3B8", 32, 32, 26);
            DrawCloud(canvas, 15, 16, 0.78, isDark ? "#94A3B8" : "#94A3B8", isDark ? "#64748B" : "#64748B");
            DrawCloud(canvas, 8, 24, 0.96, isDark ? "#E2E8F0" : "#CBD5E1", isDark ? "#CBD5E1" : "#94A3B8");
            return viewbox;
        }

        private static void DrawGlow(Canvas canvas, string colorHex, double cx, double cy, double radius)
        {
            var c = (Color)ColorConverter.ConvertFromString(colorHex);
            var brush = new RadialGradientBrush();
            brush.GradientStops.Add(new GradientStop(c, 0.0));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, c.R, c.G, c.B), 1.0));
            brush.Freeze();

            var el = new Ellipse
            {
                Width = radius * 2,
                Height = radius * 2,
                Fill = brush,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(el, cx - radius);
            Canvas.SetTop(el, cy - radius);
            canvas.Children.Add(el);
        }

        private static void DrawSun(Canvas canvas, double cx, double cy, double radius, bool withRays)
        {
            if (withRays)
            {
                var rayBrush = ThemePalette.FreezeBrush("#FBBF24");
                for (var i = 0; i < 8; i++)
                {
                    var angle = i * Math.PI / 4.0;
                    var x1 = cx + Math.Cos(angle) * (radius + 4.5);
                    var y1 = cy + Math.Sin(angle) * (radius + 4.5);
                    var x2 = cx + Math.Cos(angle) * (radius + 9.0);
                    var y2 = cy + Math.Sin(angle) * (radius + 9.0);
                    var ray = new Line
                    {
                        X1 = x1,
                        Y1 = y1,
                        X2 = x2,
                        Y2 = y2,
                        Stroke = rayBrush,
                        StrokeThickness = 2.6,
                        StrokeStartLineCap = PenLineCap.Round,
                        StrokeEndLineCap = PenLineCap.Round
                    };
                    canvas.Children.Add(ray);
                }
            }

            var sunBrush = new LinearGradientBrush(
                (Color)ColorConverter.ConvertFromString("#FDE047"),
                (Color)ColorConverter.ConvertFromString("#F59E0B"),
                new Point(0.2, 0),
                new Point(0.8, 1));
            sunBrush.Freeze();

            var disc = new Ellipse
            {
                Width = radius * 2,
                Height = radius * 2,
                Fill = sunBrush
            };
            Canvas.SetLeft(disc, cx - radius);
            Canvas.SetTop(disc, cy - radius);
            canvas.Children.Add(disc);
        }

        private static void DrawMoon(Canvas canvas, double left, double top)
        {
            var brush = new LinearGradientBrush(
                (Color)ColorConverter.ConvertFromString("#E0E7FF"),
                (Color)ColorConverter.ConvertFromString("#818CF8"),
                new Point(0, 0),
                new Point(1, 1));
            brush.Freeze();

            var path = new Path
            {
                Data = Geometry.Parse("M 18,2 A 15,15 0 1 0 32,24 A 12,12 0 1 1 18,2 Z"),
                Fill = brush
            };
            Canvas.SetLeft(path, left);
            Canvas.SetTop(path, top);
            canvas.Children.Add(path);
        }

        private static void DrawMoonSmall(Canvas canvas, double left, double top)
        {
            var brush = new LinearGradientBrush(
                (Color)ColorConverter.ConvertFromString("#E0E7FF"),
                (Color)ColorConverter.ConvertFromString("#818CF8"),
                new Point(0, 0),
                new Point(1, 1));
            brush.Freeze();

            var path = new Path
            {
                Data = Geometry.Parse("M 12,1 A 10,10 0 1 0 22,16 A 8,8 0 1 1 12,1 Z"),
                Fill = brush
            };
            Canvas.SetLeft(path, left);
            Canvas.SetTop(path, top);
            canvas.Children.Add(path);
        }

        private static void DrawStar(Canvas canvas, double cx, double cy, double r, string hex)
        {
            var el = new Ellipse
            {
                Width = r,
                Height = r,
                Fill = ThemePalette.FreezeBrush(hex)
            };
            Canvas.SetLeft(el, cx - r / 2.0);
            Canvas.SetTop(el, cy - r / 2.0);
            canvas.Children.Add(el);
        }

        private static void DrawCloud(Canvas canvas, double left, double top, double scale, string topHex, string bottomHex)
        {
            var brush = new LinearGradientBrush(
                (Color)ColorConverter.ConvertFromString(topHex),
                (Color)ColorConverter.ConvertFromString(bottomHex),
                new Point(0.5, 0),
                new Point(0.5, 1));
            brush.Freeze();

            var path = new Path
            {
                Data = Geometry.Parse("M 13,26 H 38 A 8,8 0 0 0 39,10.2 A 11.5,11.5 0 0 0 17.5,7.5 A 9.5,9.5 0 0 0 13,26 Z"),
                Fill = brush
            };
            if (Math.Abs(scale - 1.0) > 0.01)
            {
                path.RenderTransform = new ScaleTransform(scale, scale);
            }
            Canvas.SetLeft(path, left);
            Canvas.SetTop(path, top);
            canvas.Children.Add(path);
        }

        private static void DrawRainDrop(Canvas canvas, double x, double y, string hex)
        {
            var line = new Line
            {
                X1 = x,
                Y1 = y,
                X2 = x - 2.5,
                Y2 = y + 7.5,
                Stroke = ThemePalette.FreezeBrush(hex),
                StrokeThickness = 2.6,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            };
            canvas.Children.Add(line);
        }

        private static void DrawLightning(Canvas canvas, double left, double top)
        {
            var path = new Path
            {
                Data = Geometry.Parse("M 6,0 L 0,10 H 5 L 2,19 L 12,8 H 7 Z"),
                Fill = ThemePalette.FreezeBrush("#FBBF24")
            };
            Canvas.SetLeft(path, left);
            Canvas.SetTop(path, top);
            canvas.Children.Add(path);
        }

        private static void DrawFogLine(Canvas canvas, double x, double y, double w, string hex)
        {
            var line = new Line
            {
                X1 = x,
                Y1 = y,
                X2 = x + w,
                Y2 = y,
                Stroke = ThemePalette.FreezeBrush(hex),
                StrokeThickness = 2.8,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            };
            canvas.Children.Add(line);
        }

        private static void DrawSnowDot(Canvas canvas, double cx, double cy, string hex)
        {
            var el = new Ellipse
            {
                Width = 4.5,
                Height = 4.5,
                Fill = ThemePalette.FreezeBrush(hex)
            };
            Canvas.SetLeft(el, cx - 2.25);
            Canvas.SetTop(el, cy - 2.25);
            canvas.Children.Add(el);
        }

        // Crisp 16x16 vector UI micro-icons (zero Unicode font inconsistency!)
        public static Path MicroIcon(string geometryData, Brush stroke, double size, double thickness)
        {
            var path = new Path
            {
                Data = Geometry.Parse(geometryData),
                Stroke = stroke,
                StrokeThickness = thickness,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                Width = size,
                Height = size,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            return path;
        }

        public const string GeoPin = "M 8,1.5 C 5.2,1.5 3,3.7 3,6.5 C 3,10.2 8,14.5 8,14.5 C 8,14.5 13,10.2 13,6.5 C 13,3.7 10.8,1.5 8,1.5 Z M 8,8.2 A 1.8,1.8 0 1 1 8,4.6 A 1.8,1.8 0 1 1 8,8.2 Z";
        public const string GeoRefresh = "M 13.5,8 A 5.5,5.5 0 1 1 11.8,4 M 13.5,2 V 5.5 H 10";
        public const string GeoThumbtack = "M 6,2 H 10 L 9.5,6.5 L 12,8.5 V 9.8 H 8.7 V 14.5 L 8,15 L 7.3,14.5 V 9.8 H 4 V 8.5 L 6.5,6.5 Z";
        public const string GeoSettings = "M 3,5 H 13 M 3,11 H 13 M 6,3.2 A 1.8,1.8 0 1 0 6,6.8 A 1.8,1.8 0 1 0 6,3.2 M 10,9.2 A 1.8,1.8 0 1 0 10,12.8 A 1.8,1.8 0 1 0 10,9.2";
        public const string GeoCollapse = "M 4,4 L 8,8 L 4,12 M 11,4 V 12";
        public const string GeoClose = "M 4,4 L 12,12 M 12,4 L 4,12";
        public const string GeoBack = "M 10,3.5 L 5,8 L 10,12.5";
        public const string GeoSearch = "M 7,2.5 A 4.5,4.5 0 1 0 7,11.5 A 4.5,4.5 0 1 0 7,2.5 M 10.5,10.5 L 14,14";
        public const string GeoChevronDown = "M 4,6 L 8,10 L 12,6";
        public const string GeoChevronUp = "M 4,10 L 8,6 L 12,10";
        public const string GeoChevronLeft = "M 10,4 L 6,8 L 10,12";
        public const string GeoChevronRight = "M 6,4 L 10,8 L 6,12";
    }
}
