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
    /// Sleek edge-docked mini pill (54x104) displaying live vector weather icon + temperature + expand chevron.
    /// Click to expand; drag vertically to reposition along the docked screen edge.
    /// </summary>
    public sealed class CollapsedPillView : Border
    {
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
            Height = 104;
            Cursor = Cursors.Hand;
            BorderThickness = new Thickness(1);
            SnapsToDevicePixels = true;

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
                Opacity = 0.72
            };

            stack.Children.Add(iconHost);
            stack.Children.Add(tempBlock);
            stack.Children.Add(chevronHost);
            Child = stack;

            MouseLeftButtonDown += OnMouseDown;
            MouseMove += OnMouseMove;
            MouseLeftButtonUp += OnMouseUp;
        }

        public void ApplyVisual(ThemePalette theme, string edge, WeatherSnapshot snap, string lang)
        {
            var isLeft = String.Equals(edge, "Left", StringComparison.OrdinalIgnoreCase);
            CornerRadius = isLeft ? new CornerRadius(0, 16, 16, 0) : new CornerRadius(16, 0, 0, 16);
            Background = theme.PillBg;
            BorderBrush = theme.WindowBorder;
            tempBlock.Foreground = theme.TextPrimary;

            var code = snap != null ? snap.WeatherCode : (int?)2;
            var isDay = snap != null ? snap.IsDay : (int?)1;
            iconHost.Child = WeatherIcons.CreateWeatherIcon(code, isDay, 32, theme.IsDark);

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
