using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace WeatherLauncher
{
    /// <summary>
    /// Liquid-glass backdrop support (v3.0.0).
    /// Applies a DWM acrylic blur-behind to the window via SetWindowCompositionAttribute,
    /// plus Windows 11 rounded window corners when available.
    /// Every call is best-effort: on any failure the caller keeps the painted
    /// paper-tone fallback, so no OS version loses functionality.
    /// </summary>
    internal static class GlassEffects
    {
        private enum AccentState
        {
            Disabled = 0,
            EnableGradient = 1,
            EnableTransparentGradient = 2,
            EnableBlurBehind = 3,
            EnableAcrylicBlurBehind = 4
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct AccentPolicy
        {
            public int AccentState;
            public int AccentFlags;
            public uint GradientColor; // AABBGGRR
            public int AnimationId;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WindowCompositionAttributeData
        {
            public int Attribute; // 19 = WCA_ACCENT_POLICY
            public IntPtr Data;
            public int SizeOfData;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        private const int WcaAccentPolicy = 19;
        private const int DwmwaWindowCornerPreference = 33;
        private const int DwmwcpRound = 2;

        // Warm paper tint over the acrylic blur: alpha 0x66, RGB #FAF9F5 packed as AABBGGRR.
        private const uint DefaultTint = 0x66F5F9FAu;

        /// <summary>
        /// Tries acrylic blur-behind first, then plain blur-behind, then gives up.
        /// Returns true when a real backdrop blur is active.
        /// </summary>
        public static bool TryEnableAcrylicBackdrop(Window window)
        {
            try
            {
                var hwnd = new WindowInteropHelper(window).Handle;
                if (hwnd == IntPtr.Zero) { return false; }

                TryRoundCorners(hwnd);

                if (TryApplyAccent(hwnd, AccentState.EnableAcrylicBlurBehind, DefaultTint))
                {
                    return true;
                }
                // Older Windows 10 builds reject acrylic but accept classic blur-behind.
                return TryApplyAccent(hwnd, AccentState.EnableBlurBehind, 0x00000000u);
            }
            catch
            {
                return false;
            }
        }

        private static bool TryApplyAccent(IntPtr hwnd, AccentState state, uint gradientColor)
        {
            var accent = new AccentPolicy
            {
                AccentState = (int)state,
                AccentFlags = 2, // draw all borders
                GradientColor = gradientColor,
                AnimationId = 0
            };

            var accentSize = Marshal.SizeOf(typeof(AccentPolicy));
            var accentPtr = Marshal.AllocHGlobal(accentSize);
            try
            {
                Marshal.StructureToPtr(accent, accentPtr, false);
                var data = new WindowCompositionAttributeData
                {
                    Attribute = WcaAccentPolicy,
                    Data = accentPtr,
                    SizeOfData = accentSize
                };
                return SetWindowCompositionAttribute(hwnd, ref data) != 0;
            }
            catch
            {
                return false;
            }
            finally
            {
                Marshal.FreeHGlobal(accentPtr);
            }
        }

        private static void TryRoundCorners(IntPtr hwnd)
        {
            try
            {
                var preference = DwmwcpRound;
                DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref preference, sizeof(int));
            }
            catch
            {
                // Pre-Windows-11: the layered-window CornerRadius clip already rounds the visuals.
            }
        }
    }
}
