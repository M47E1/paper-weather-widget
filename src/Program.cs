using System;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using PaperWeatherWidget.UI;

namespace PaperWeatherWidget
{
    internal static class Program
    {
        private const string SingleInstanceMutexName = "Local\\PaperWeatherWidget_v4_SingleInstance";

        [STAThread]
        private static void Main(string[] args)
        {
            // A compact 2D desktop weather card does not need a ~110MB DirectX 9 GPU shader pool.
            // SoftwareOnly rendering cuts Private Memory drastically while keeping crisp vector UI.
            RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;

            var options = LaunchOptions.Parse(args);
            var useMutex = String.IsNullOrWhiteSpace(options.RenderShotsDir) && options.HoldOpenMs <= 0;

            Mutex mutex = null;
            if (useMutex)
            {
                bool createdNew;
                mutex = new Mutex(true, SingleInstanceMutexName, out createdNew);
                if (!createdNew)
                {
                    mutex.Dispose();
                    return;
                }
            }

            try
            {
                var app = new Application
                {
                    ShutdownMode = ShutdownMode.OnMainWindowClose
                };
                var window = new MainWindow(options);
                app.Run(window);
            }
            finally
            {
                if (mutex != null)
                {
                    try { mutex.ReleaseMutex(); } catch { }
                    mutex.Dispose();
                }
            }
        }
    }
}
