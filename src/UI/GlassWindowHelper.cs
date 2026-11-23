using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace PaperWeatherWidget.UI
{
    /// <summary>
    /// Native Windows 10/11 DWM integration (rounded corners, subtle backdrop blur, hide from Alt+Tab)
    /// plus OS working-set trim to keep idle desktop widget RAM ultra-low.
    /// </summary>
    internal static class GlassWindowHelper
    {
        private const int WcaAccentPolicy = 19;
        private const int DwmwaUseImmersiveDarkMode = 20;
        private const int DwmwaWindowCornerPreference = 33;
        private const int DwmwcpRound = 2;

        private const int GwlExStyle = -20;
        private const int WsExToolWindow = 0x00000080;

        [StructLayout(LayoutKind.Sequential)]
        private struct AccentPolicy
        {
            public int AccentState;
            public int AccentFlags;
            public uint GradientColor;
            public int AnimationId;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WindowCompositionAttributeData
        {
            public int Attribute;
            public IntPtr Data;
            public int SizeOfData;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr", SetLastError = true)]
        private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("psapi.dll", SetLastError = true)]
        private static extern bool EmptyWorkingSet(IntPtr hProcess);

        public static bool ApplyBackdrop(Window window, bool isDark, uint acrylicTintAbgr)
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

                // Windows 11 rounded corners + dark mode attribute
                var cornerPref = DwmwcpRound;
                try { DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref cornerPref, sizeof(int)); } catch { }
                var darkFlag = isDark ? 1 : 0;
                try { DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref darkFlag, sizeof(int)); } catch { }

                // Acrylic blur-behind (4 = EnableAcrylicBlurBehind, 3 = EnableBlurBehind)
                if (TrySetAccent(hwnd, 4, acrylicTintAbgr))
                {
                    return true;
                }
                return TrySetAccent(hwnd, 3, 0x00000000u);
            }
            catch
            {
                return false;
            }
        }

        private static bool TrySetAccent(IntPtr hwnd, int state, uint color)
        {
            var policy = new AccentPolicy
            {
                AccentState = state,
                AccentFlags = 2,
                GradientColor = color,
                AnimationId = 0
            };
            var size = Marshal.SizeOf(typeof(AccentPolicy));
            var ptr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(policy, ptr, false);
                var data = new WindowCompositionAttributeData
                {
                    Attribute = WcaAccentPolicy,
                    Data = ptr,
                    SizeOfData = size
                };
                return SetWindowCompositionAttribute(hwnd, ref data) != 0;
            }
            catch
            {
                return false;
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
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
