using System;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace PaperWeatherWidget.UI
{
    /// <summary>
    /// Native Windows 11 22H2+ (Build >= 22621) DWM SystemBackdrop Acrylic + native anti-aliased rounded corners
    /// (zero square-corner Acrylic leak!), Alt+Tab ToolWindow hiding, and OS working-set trimming.
    /// Falls back cleanly to pure WPF rounded translucent glass on Windows 10 without rectangular Acrylic artifacts.
    /// </summary>
    internal static class GlassWindowHelper
    {
        private const int DwmwaUseImmersiveDarkMode = 20;
        private const int DwmwaWindowCornerPreference = 33;
        private const int DwmwaSystemBackdropType = 38;

        private const int DwmwcpRound = 2;             // Native Windows 11 8px anti-aliased rounded corners + drop shadow
        private const int DwmsbtTransientWindow = 3;   // Native Windows 11 Acrylic frosted glass backdrop

        private const int GwlExStyle = -20;
        private const int WsExToolWindow = 0x00000080;

        [StructLayout(LayoutKind.Sequential)]
        private struct MARGINS
        {
            public int Left;
            public int Right;
            public int Top;
            public int Bottom;
        }

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr", SetLastError = true)]
        private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("psapi.dll", SetLastError = true)]
        private static extern bool EmptyWorkingSet(IntPtr hProcess);

        private static bool? cachedWin11Support;

        /// <summary>
        /// Reads real Windows build number from registry (unaffected by .NET Framework manifest version lie).
        /// Windows 11 22H2+ (Build >= 22621) natively supports DWMWA_SYSTEMBACKDROP_TYPE = Acrylic + DWMWCP_ROUND.
        /// </summary>
        public static bool IsWindows11NativeAcrylicSupported()
        {
            if (cachedWin11Support.HasValue) { return cachedWin11Support.Value; }
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", false))
                {
                    if (key != null)
                    {
                        var raw = Convert.ToString(key.GetValue("CurrentBuildNumber"), CultureInfo.InvariantCulture);
                        int build;
                        if (Int32.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out build))
                        {
                            cachedWin11Support = build >= 22621;
                            return cachedWin11Support.Value;
                        }
                    }
                }
            }
            catch
            {
            }
            cachedWin11Support = false;
            return false;
        }

        public static bool ApplyBackdrop(Window window, bool isDark, bool useNativeWin11Acrylic)
        {
            try
            {
                var hwnd = new WindowInteropHelper(window).Handle;
                if (hwnd == IntPtr.Zero) { return false; }

                // Hide from Alt+Tab like a proper desktop widget
                var exStyle = GetWindowLongPtr(hwnd, GwlExStyle).ToInt64();
                if ((exStyle & WsExToolWindow) == 0)
                {
                    SetWindowLongPtr(hwnd, GwlExStyle, new IntPtr(exStyle | WsExToolWindow));
                }

                if (!useNativeWin11Acrylic)
                {
                    // On Windows 10 / offscreen mode, rely cleanly on WPF CornerRadius without rectangular Acrylic leaks.
                    return false;
                }

                var src = HwndSource.FromHwnd(hwnd);
                if (src != null && src.CompositionTarget != null)
                {
                    src.CompositionTarget.BackgroundColor = Colors.Transparent;
                }

                var margins = new MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
                var hrExtend = DwmExtendFrameIntoClientArea(hwnd, ref margins);

                var cornerPref = DwmwcpRound;
                var hrCorner = DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref cornerPref, sizeof(int));

                var darkFlag = isDark ? 1 : 0;
                DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref darkFlag, sizeof(int));

                var backdrop = DwmsbtTransientWindow;
                var hrBackdrop = DwmSetWindowAttribute(hwnd, DwmwaSystemBackdropType, ref backdrop, sizeof(int));

                return hrExtend == 0 && hrCorner == 0 && hrBackdrop == 0;
            }
            catch
            {
                return false;
            }
        }

        public static void TrimWorkingSet()
        {
            try
            {
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized, false);
                EmptyWorkingSet(Process.GetCurrentProcess().Handle);
            }
            catch
            {
            }
        }
    }
}
