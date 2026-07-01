using System;
using System.Diagnostics;
using System.IO;
using System.Globalization;
using System.Windows;

namespace WeatherLauncher
{
    internal static class WeatherLauncherProgram
    {
        [STAThread]
        private static void Main(string[] args)
        {
            StartupBenchmark.ConfigureStartupTrace(args);
            StartupBenchmark.TraceLauncher("Launcher process start", 0);
            StartupBenchmark.TraceLauncher("WeatherLauncher.Main entry");
            var app = new App(args);
            StartupBenchmark.TraceLauncher("App/Application created");
            app.Run(new MainWindow(args));
        }
    }

    internal static class StartupBenchmark
    {
        private static readonly DateTime ProcessStartedAt = Process.GetCurrentProcess().StartTime.ToUniversalTime();
        private static string logPath;
        private static string startupTracePath;
        private static bool startupTraceEnabled;
        private static bool firstDataLogged;
        private static bool firstWeatherAppliedLogged;
        private static bool firstStdoutLogged;
        private static bool snapshotAppliedLogged;

        public static long ElapsedMilliseconds
        {
            get { return Math.Max(0, (long)(DateTime.UtcNow - ProcessStartedAt).TotalMilliseconds); }
        }

        public static string StartupTracePath
        {
            get { return startupTracePath; }
        }

        public static bool StartupTraceEnabled
        {
            get { return startupTraceEnabled; }
        }

        public static void ConfigureStartupTrace(string[] args)
        {
            args = args ?? new string[0];
            for (var i = 0; i < args.Length; i++)
            {
                var arg = args[i] ?? String.Empty;
                if (String.Equals(arg, "--startup-trace", StringComparison.OrdinalIgnoreCase))
                {
                    startupTraceEnabled = true;
                    continue;
                }
                if (String.Equals(arg, "--startup-trace-log", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    startupTraceEnabled = true;
                    startupTracePath = Path.GetFullPath(args[++i]);
                }
            }

            if (!startupTraceEnabled)
            {
                return;
            }
            if (String.IsNullOrWhiteSpace(startupTracePath))
            {
                startupTracePath = Path.Combine(Environment.CurrentDirectory, "reports", "startup-trace.log");
            }
            try
            {
                var directory = Path.GetDirectoryName(startupTracePath);
                if (!String.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                File.WriteAllText(startupTracePath, String.Empty);
            }
            catch
            {
                startupTraceEnabled = false;
                startupTracePath = null;
            }
        }

        public static void TraceLauncher(string milestone)
        {
            TraceLauncher(milestone, ElapsedMilliseconds);
        }

        public static void TraceLauncher(string milestone, long elapsedMilliseconds)
        {
            Trace("Launcher", milestone, elapsedMilliseconds);
        }

        public static void TraceWorker(string line)
        {
            if (String.IsNullOrWhiteSpace(line)) { return; }
            try
            {
                if (!String.IsNullOrWhiteSpace(startupTracePath))
                {
                    File.AppendAllText(startupTracePath, line + Environment.NewLine);
                }
            }
            catch
            {
            }
        }

        public static void TraceFirstStdoutLine()
        {
            if (firstStdoutLogged) { return; }
            firstStdoutLogged = true;
            TraceLauncher("First stdout line received");
        }

        public static void TraceFirstWeatherApplied()
        {
            if (firstWeatherAppliedLogged) { return; }
            firstWeatherAppliedLogged = true;
            TraceLauncher("First weather event applied");
        }

        private static void Trace(string component, string milestone, long elapsedMilliseconds)
        {
            if (!startupTraceEnabled || String.IsNullOrWhiteSpace(milestone))
            {
                return;
            }
            var line = String.Format(
                CultureInfo.InvariantCulture,
                "[Trace][{0}] elapsed_ms={1} wall={2:o} milestone={3}",
                component,
                Math.Max(0, elapsedMilliseconds),
                DateTime.UtcNow,
                milestone);
            try
            {
                if (!String.IsNullOrWhiteSpace(startupTracePath))
                {
                    File.AppendAllText(startupTracePath, line + Environment.NewLine);
                }
                Debug.WriteLine(line);
            }
            catch
            {
            }
        }

        public static void Initialize(string path)
        {
            if (String.IsNullOrWhiteSpace(path))
            {
                return;
            }

            logPath = path;
            try
            {
                var directory = Path.GetDirectoryName(logPath);
                if (!String.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(logPath, String.Empty);
            }
            catch
            {
                logPath = null;
            }
        }

        public static void Log(string line)
        {
            if (String.IsNullOrWhiteSpace(line))
            {
                return;
            }

            try
            {
                if (!String.IsNullOrWhiteSpace(logPath))
                {
                    File.AppendAllText(logPath, line + Environment.NewLine);
                }

                Debug.WriteLine(line);
            }
            catch
            {
            }
        }

        public static void LogWindowShown()
        {
            Log(String.Format("[Launcher] window shown: {0}ms", ElapsedMilliseconds));
        }

        public static void LogWorkerStarted()
        {
            Log(String.Format("[Worker] process started: {0}ms", ElapsedMilliseconds));
        }

        public static void LogSnapshotApplied()
        {
            if (snapshotAppliedLogged)
            {
                return;
            }

            snapshotAppliedLogged = true;
            Log(String.Format("[Launcher] snapshot applied: {0}ms", ElapsedMilliseconds));
        }

        public static void LogFirstData()
        {
            if (firstDataLogged)
            {
                return;
            }

            firstDataLogged = true;
            Log(String.Format("[Worker] first data: {0:0.0}s", ElapsedMilliseconds / 1000.0));
        }
    }

    internal static class LauncherPaths
    {
        public static string FindRepositoryRoot()
        {
            var candidates = new[]
            {
                AppDomain.CurrentDomain.BaseDirectory,
                Environment.CurrentDirectory
            };

            foreach (var candidate in candidates)
            {
                var root = WalkForRepoRoot(candidate);
                if (!String.IsNullOrWhiteSpace(root))
                {
                    return root;
                }
            }

            return Environment.CurrentDirectory;
        }

        public static string FindWorkerScript(string explicitPath)
        {
            if (!String.IsNullOrWhiteSpace(explicitPath))
            {
                var full = Path.GetFullPath(explicitPath);
                if (File.Exists(full))
                {
                    return full;
                }
            }

            var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            var currentDirectory = Environment.CurrentDirectory;
            var candidates = new[]
            {
                Path.Combine(baseDirectory, "WeatherWorker.ps1"),
                Path.Combine(baseDirectory, "src", "worker", "WeatherWorker.ps1"),
                Path.Combine(currentDirectory, "src", "worker", "WeatherWorker.ps1"),
                Path.Combine(FindRepositoryRoot(), "src", "worker", "WeatherWorker.ps1")
            };

            foreach (var candidate in candidates)
            {
                if (File.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }

            return String.Empty;
        }

        public static string FindBenchmarkLog(string explicitPath)
        {
            if (!String.IsNullOrWhiteSpace(explicitPath))
            {
                return Path.GetFullPath(explicitPath);
            }

            return Path.Combine(FindRepositoryRoot(), "reports", "startup-benchmark.log");
        }

        public static string FindSettingsFile()
        {
            return Path.Combine(FindRepositoryRoot(), "LonghuaWeatherWidget.settings.json");
        }

        public static string FindWeatherSnapshot()
        {
            var localAppData = Environment.GetEnvironmentVariable("LOCALAPPDATA");
            if (String.IsNullOrWhiteSpace(localAppData))
            {
                localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            }
            if (String.IsNullOrWhiteSpace(localAppData))
            {
                localAppData = Path.GetTempPath();
            }

            return Path.Combine(localAppData, "PaperWeatherWidget", "weather-snapshot.json");
        }

        private static string WalkForRepoRoot(string start)
        {
            if (String.IsNullOrWhiteSpace(start))
            {
                return String.Empty;
            }

            var directory = new DirectoryInfo(Path.GetFullPath(start));
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "LonghuaWeatherWidget.ps1")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            return String.Empty;
        }
    }

    internal sealed class LauncherOptions
    {
        public string WorkerPath { get; private set; }
        public string BenchmarkLogPath { get; private set; }
        public bool Topmost { get; private set; }
        public bool FixtureWeatherSuccess { get; private set; }
        public bool AllowFixtureSnapshotWrite { get; private set; }
        public int HoldOpenMs { get; private set; }
        public bool StartupTrace { get; private set; }
        public string StartupTracePath { get; private set; }
        public int PollSeconds { get; private set; }

        private LauncherOptions()
        {
            Topmost = true;
            PollSeconds = 10;
        }

        public static LauncherOptions Parse(string[] args)
        {
            var options = new LauncherOptions();
            args = args ?? new string[0];

            for (var i = 0; i < args.Length; i++)
            {
                var arg = args[i] ?? String.Empty;
                if (String.Equals(arg, "--worker", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    options.WorkerPath = args[++i];
                    continue;
                }

                if (String.Equals(arg, "--benchmark-log", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    options.BenchmarkLogPath = args[++i];
                    continue;
                }

                if (String.Equals(arg, "--poll-seconds", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    int parsed;
                    if (Int32.TryParse(args[++i], out parsed))
                    {
                        options.PollSeconds = Math.Max(5, Math.Min(10, parsed));
                    }

                    continue;
                }

                if (String.Equals(arg, "--no-topmost", StringComparison.OrdinalIgnoreCase))
                {
                    options.Topmost = false;
                    continue;
                }

                if (String.Equals(arg, "--fixture-weather-success", StringComparison.OrdinalIgnoreCase))
                {
                    options.FixtureWeatherSuccess = true;
                    continue;
                }

                if (String.Equals(arg, "--allow-fixture-snapshot", StringComparison.OrdinalIgnoreCase))
                {
                    options.AllowFixtureSnapshotWrite = true;
                    continue;
                }

                if (String.Equals(arg, "--hold-open-ms", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    int parsed;
                    if (Int32.TryParse(args[++i], out parsed))
                    {
                        options.HoldOpenMs = Math.Max(0, Math.Min(60000, parsed));
                    }
                    continue;
                }

                if (String.Equals(arg, "--startup-trace", StringComparison.OrdinalIgnoreCase))
                {
                    options.StartupTrace = true;
                    continue;
                }

                if (String.Equals(arg, "--startup-trace-log", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    options.StartupTrace = true;
                    options.StartupTracePath = Path.GetFullPath(args[++i]);
                    continue;
                }
            }

            return options;
        }
    }
}

