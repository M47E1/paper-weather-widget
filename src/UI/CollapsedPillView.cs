using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PaperWeatherWidget.Models;
using PaperWeatherWidget.Services;

namespace PaperWeatherWidget.UI
{
    /// <summary>
    /// v4.1.0 Liquid Glass edge-docked mini pill (54x108) with sky-matched translucent gradient,
    /// refractive specular rim, live vector weather icon, and current temperature.
    /// </summary>
    public sealed class CollapsedPillView : Border
    {
        private readonly Border sheenLayer;
        private readonly Border iconHost;
        private readonly TextBlock tempBlock;
        private readonly Border chevronHost;
        private Point dragStartScreen;
        private double startWindowTop;
        private bool isDragging;
        private bool dragMoved;

        public event Action ExpandRequested;
        public event Action<double> VerticalDragMoved;
        public event Action VerticalDragCompleted;

        public CollapsedPillView()
        {
            Width = 54;
            Height = 108;
            Cursor = Cursors.Hand;
            BorderThickness = new Thickness(1.2);
            ClipToBounds = true;
            SnapsToDevicePixels = true;

            var layer = new Grid();

            sheenLayer = new Border
            {
                IsHitTestVisible = false
            };
            layer.Children.Add(sheenLayer);

            var stack = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            iconHost = new Border
            {
                Width = 34,
                Height = 34,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 2, 0, 2)
            };

            tempBlock = new TextBlock
            {
                Text = "--°",
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI, Microsoft YaHei UI"),
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 0, 0, 4)
            };

            chevronHost = new Border
            {
                Width = 14,
                Height = 14,
                HorizontalAlignment = HorizontalAlignment.Center,
                Opacity = 0.8
            };

            stack.Children.Add(iconHost);
            stack.Children.Add(tempBlock);
            stack.Children.Add(chevronHost);
            layer.Children.Add(stack);
            Child = layer;

            MouseLeftButtonDown += OnMouseDown;
            MouseMove += OnMouseMove;
            MouseLeftButtonUp += OnMouseUp;
        }

        public void ApplyVisual(ThemePalette theme, string edge, WeatherSnapshot snap, string lang, int? overrideWmo, int? overrideIsDay, bool useNativeWin11Radius)
        {
            var isLeft = String.Equals(edge, "Left", StringComparison.OrdinalIgnoreCase);
            var radius = useNativeWin11Radius
                ? new CornerRadius(8)
                : (isLeft ? new CornerRadius(0, 18, 18, 0) : new CornerRadius(18, 0, 0, 18));
            CornerRadius = radius;
            sheenLayer.CornerRadius = radius;
            sheenLayer.Background = theme.TopSheenBrush;

            var code = overrideWmo.HasValue ? overrideWmo : (snap != null ? snap.WeatherCode : (int?)2);
            var isDay = overrideIsDay.HasValue ? overrideIsDay : (snap != null ? snap.IsDay : (int?)1);

            Background = theme.BuildSkyGradient(code, isDay, true);
            BorderBrush = theme.OuterGlassBorder;
            BorderThickness = useNativeWin11Radius ? new Thickness(0.8) : new Thickness(1.2);
            tempBlock.Foreground = theme.TextPrimary;

            iconHost.Child = WeatherIcons.CreateWeatherIcon(code, isDay, 32, true);

            if (snap != null && snap.Temperature.HasValue)
            {
                tempBlock.Text = Math.Round(snap.Temperature.Value).ToString("0", CultureInfo.InvariantCulture) + "°";
            }
            else
            {
                tempBlock.Text = "--°";
            }

            chevronHost.Child = WeatherIcons.MicroIcon(
                isLeft ? WeatherIcons.GeoChevronRight : WeatherIcons.GeoChevronLeft,
                theme.TextSecondary,
                11,
                1.8);

            ToolTip = LocalizationService.Ui(lang, "DrawerExpand");
        }

        private void OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) { return; }
            var win = Window.GetWindow(this);
            if (win == null) { return; }
            dragStartScreen = PointToScreen(e.GetPosition(this));
            startWindowTop = win.Top;
            isDragging = true;
            dragMoved = false;
            CaptureMouse();
            e.Handled = true;
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (!isDragging) { return; }
            var current = PointToScreen(e.GetPosition(this));
            var deltaY = current.Y - dragStartScreen.Y;
            if (!dragMoved && Math.Abs(deltaY) > 6)
            {
                dragMoved = true;
            }
            if (dragMoved && VerticalDragMoved != null)
            {
                var dpiScale = GetDpiScaleY();
                VerticalDragMoved(startWindowTop + deltaY / dpiScale);
            }
        }

        private void OnMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (!isDragging) { return; }
            isDragging = false;
            ReleaseMouseCapture();
            e.Handled = true;
            if (dragMoved)
            {
                if (VerticalDragCompleted != null) { VerticalDragCompleted(); }
            }
            else
            {
                if (ExpandRequested != null) { ExpandRequested(); }
            }
        }

        private double GetDpiScaleY()
        {
            var src = PresentationSource.FromVisual(this);
            if (src != null && src.CompositionTarget != null)
            {
                return src.CompositionTarget.TransformToDevice.M22;
            }
            return 1.0;
        }
    }
}
