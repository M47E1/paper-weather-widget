using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Web.Script.Serialization;
using Microsoft.Win32;
using PaperWeatherWidget.Models;

namespace PaperWeatherWidget.Services
{
    public sealed class SettingsService
    {
        private const string StartupRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string StartupValueName = "PaperWeatherWidget";

        private readonly JavaScriptSerializer serializer = new JavaScriptSerializer();

        public static string GetDataRoot()
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
            return Path.Combine(localAppData, "PaperWeatherWidget");
        }

        public static string GetSettingsPath()
        {
            return Path.Combine(GetDataRoot(), "LonghuaWeatherWidget.settings.json");
        }

        public static string GetPlacementPath()
        {
            return Path.Combine(GetDataRoot(), "launcher-settings.json");
        }

        public static string GetSnapshotPath()
        {
            return Path.Combine(GetDataRoot(), "weather-snapshot.json");
        }

        public AppSettings Load()
        {
            var settings = new AppSettings();
            try
            {
                var path = GetSettingsPath();
                if (File.Exists(path))
                {
                    var text = File.ReadAllText(path, Encoding.UTF8);
                    var dict = serializer.DeserializeObject(text) as Dictionary<string, object>;
                    if (dict != null)
                    {
                        var lang = GetString(dict, "Language");
                        if (String.Equals(lang, "en", StringComparison.OrdinalIgnoreCase) || String.Equals(lang, "zh", StringComparison.OrdinalIgnoreCase))
                        {
                            settings.Language = lang.ToLowerInvariant();
                        }

                        var theme = GetString(dict, "ThemeMode");
                        if (String.Equals(theme, "light", StringComparison.OrdinalIgnoreCase)
                            || String.Equals(theme, "dark", StringComparison.OrdinalIgnoreCase)
                            || String.Equals(theme, "auto", StringComparison.OrdinalIgnoreCase))
                        {
                            settings.ThemeMode = theme.ToLowerInvariant();
                        }

                        var pKey = NormalizeLegacyKey(GetString(dict, "ProvinceKey"));
                        var cKey = NormalizeLegacyKey(GetString(dict, "CityKey"));
                        var dKey = NormalizeLegacyKey(GetString(dict, "DistrictKey"));
                        if (!String.IsNullOrWhiteSpace(pKey)) { settings.ProvinceKey = pKey; }
                        if (!String.IsNullOrWhiteSpace(cKey)) { settings.CityKey = cKey; }
                        if (!String.IsNullOrWhiteSpace(dKey)) { settings.DistrictKey = dKey; }

                        var cachedKey = GetString(dict, "CachedLocationKey");
                        if (String.Equals(cachedKey, settings.LocationKey, StringComparison.OrdinalIgnoreCase))
                        {
                            settings.CachedProvinceZh = GetString(dict, "CachedProvinceZh");
                            settings.CachedProvinceEn = GetString(dict, "CachedProvinceEn");
                            settings.CachedCityZh = GetString(dict, "CachedCityZh");
                            settings.CachedCityEn = GetString(dict, "CachedCityEn");
                            settings.CachedDistrictZh = GetString(dict, "CachedDistrictZh");
                            settings.CachedDistrictEn = GetString(dict, "CachedDistrictEn");
                            settings.CachedLat = GetDouble(dict, "CachedLat");
                            settings.CachedLon = GetDouble(dict, "CachedLon");
                        }

                        var refresh = GetInt(dict, "RefreshSeconds");
                        if (refresh.HasValue && (refresh.Value == 60 || refresh.Value == 900 || refresh.Value == 3600 || refresh.Value == 86400))
                        {
                            settings.RefreshSeconds = refresh.Value;
                        }

                        var slot = GetString(dict, "ForecastSlotKey");
                        if (!String.IsNullOrWhiteSpace(slot))
                        {
                            settings.ForecastSlotKey = NormalizeSlotKey(slot);
                        }

                        var topmost = GetBool(dict, "Topmost");
                        if (topmost.HasValue) { settings.Topmost = topmost.Value; }

                        var details = GetBool(dict, "DetailsExpanded");
                        if (details.HasValue) { settings.DetailsExpanded = details.Value; }
                    }
                }
            }
            catch
            {
            }

            try
            {
                var placePath = GetPlacementPath();
                if (File.Exists(placePath))
                {
                    var text = File.ReadAllText(placePath, Encoding.UTF8);
                    var dict = serializer.DeserializeObject(text) as Dictionary<string, object>;
                    if (dict != null)
                    {
                        settings.WindowLeft = GetDouble(dict, "left");
                        settings.WindowTop = GetDouble(dict, "top");
                        var expanded = GetBool(dict, "drawerExpanded");
                        if (expanded.HasValue) { settings.DrawerExpanded = expanded.Value; }
                        var edge = GetString(dict, "drawerEdge");
                        if (String.Equals(edge, "Left", StringComparison.OrdinalIgnoreCase) || String.Equals(edge, "Right", StringComparison.OrdinalIgnoreCase))
                        {
                            settings.DrawerEdge = edge;
                        }
                    }
                }
            }
            catch
            {
            }

            return settings;
        }

        public void SaveSettings(AppSettings settings)
        {
            if (settings == null) { return; }
            try
            {
                var dict = new Dictionary<string, object>();
                dict["Language"] = settings.Language;
                dict["ThemeMode"] = settings.ThemeMode;
                dict["ProvinceKey"] = settings.ProvinceKey;
                dict["CityKey"] = settings.CityKey;
                dict["DistrictKey"] = settings.DistrictKey;
                dict["CachedLocationKey"] = settings.LocationKey;
                dict["CachedProvinceZh"] = settings.CachedProvinceZh ?? String.Empty;
                dict["CachedProvinceEn"] = settings.CachedProvinceEn ?? String.Empty;
                dict["CachedCityZh"] = settings.CachedCityZh ?? String.Empty;
                dict["CachedCityEn"] = settings.CachedCityEn ?? String.Empty;
                dict["CachedDistrictZh"] = settings.CachedDistrictZh ?? String.Empty;
                dict["CachedDistrictEn"] = settings.CachedDistrictEn ?? String.Empty;
                if (settings.CachedLat.HasValue) { dict["CachedLat"] = settings.CachedLat.Value; }
                if (settings.CachedLon.HasValue) { dict["CachedLon"] = settings.CachedLon.Value; }
                dict["RefreshSeconds"] = settings.RefreshSeconds;
                dict["ForecastSlotKey"] = settings.ForecastSlotKey;
                dict["Topmost"] = settings.Topmost;
                dict["DetailsExpanded"] = settings.DetailsExpanded;
                dict["SavedAt"] = DateTime.Now.ToString("s", CultureInfo.InvariantCulture);

                WriteTextAtomic(GetSettingsPath(), serializer.Serialize(dict));
            }
            catch
            {
            }
        }

        public void SavePlacement(AppSettings settings)
        {
            if (settings == null || !settings.WindowLeft.HasValue || !settings.WindowTop.HasValue) { return; }
            try
            {
                var dict = new Dictionary<string, object>();
                dict["left"] = settings.WindowLeft.Value;
                dict["top"] = settings.WindowTop.Value;
                dict["drawerExpanded"] = settings.DrawerExpanded;
                dict["drawerEdge"] = settings.DrawerEdge ?? "Right";
                dict["drawerTop"] = settings.WindowTop.Value;
                dict["savedAt"] = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);

                WriteTextAtomic(GetPlacementPath(), serializer.Serialize(dict));
            }
            catch
            {
            }
        }

        public static bool IsStartupEnabled()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(StartupRegistryPath, false))
                {
                    if (key == null) { return false; }
                    var val = Convert.ToString(key.GetValue(StartupValueName), CultureInfo.InvariantCulture);
                    var exePath = GetExecutablePath();
                    return !String.IsNullOrWhiteSpace(val)
                        && !String.IsNullOrWhiteSpace(exePath)
                        && val.IndexOf(exePath, StringComparison.OrdinalIgnoreCase) >= 0;
                }
            }
            catch
            {
                return false;
            }
        }

        public static bool SetStartupEnabled(bool enabled)
        {
            try
            {
                using (var key = enabled
                    ? Registry.CurrentUser.CreateSubKey(StartupRegistryPath)
                    : Registry.CurrentUser.OpenSubKey(StartupRegistryPath, true))
                {
                    if (key == null) { return false; }
                    if (enabled)
                    {
                        key.SetValue(StartupValueName, "\"" + GetExecutablePath() + "\"");
                    }
                    else
                    {
                        key.DeleteValue(StartupValueName, false);
                    }
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        private static string GetExecutablePath()
        {
            try { return Process.GetCurrentProcess().MainModule.FileName; }
            catch { return Assembly.GetEntryAssembly().Location; }
        }

        public static void WriteTextAtomic(string path, string content)
        {
            var dir = Path.GetDirectoryName(path);
            if (!String.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }
            var tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(tmp, content, new UTF8Encoding(false));
            if (File.Exists(path))
            {
                File.Replace(tmp, path, null);
            }
            else
            {
                File.Move(tmp, path);
            }
        }

        private static string NormalizeLegacyKey(string value)
        {
            if (String.IsNullOrWhiteSpace(value)) { return String.Empty; }
            var t = value.Trim();
            switch (t)
            {
                case "Guangdong":
                case "广东省": return "440000";
                case "Shenzhen":
                case "深圳市": return "440300";
                case "Longhua":
                case "龙华区": return "440309";
                case "Luohu":
                case "罗湖区": return "440303";
                default: return t;
            }
        }

        public static string NormalizeSlotKey(string slot)
        {
            if (String.IsNullOrWhiteSpace(slot)) { return "Day0"; }
            switch (slot.Trim())
            {
                case "Now": return "Day0";
                case "+1h": return "Hour+1h";
                case "+3h": return "Hour+3h";
                case "+6h": return "Hour+6h";
                case "+12h": return "Hour+12h";
                case "+24h": return "Hour+24h";
                case "Tonight": return "Hour+12h";
                case "Tomorrow": return "Day1";
                default: return slot.Trim();
            }
        }

        private static string GetString(Dictionary<string, object> d, string key)
        {
            object v;
            return (d != null && d.TryGetValue(key, out v) && v != null)
                ? Convert.ToString(v, CultureInfo.InvariantCulture)
                : String.Empty;
        }

        private static int? GetInt(Dictionary<string, object> d, string key)
        {
            object v;
            if (d == null || !d.TryGetValue(key, out v) || v == null) { return null; }
            try { return Convert.ToInt32(v, CultureInfo.InvariantCulture); } catch { return null; }
        }

        private static double? GetDouble(Dictionary<string, object> d, string key)
        {
            object v;
            if (d == null || !d.TryGetValue(key, out v) || v == null) { return null; }
            try { return Convert.ToDouble(v, CultureInfo.InvariantCulture); } catch { return null; }
        }

        private static bool? GetBool(Dictionary<string, object> d, string key)
        {
            object v;
            if (d == null || !d.TryGetValue(key, out v) || v == null) { return null; }
            try { return Convert.ToBoolean(v, CultureInfo.InvariantCulture); } catch { return null; }
        }
    }
}
