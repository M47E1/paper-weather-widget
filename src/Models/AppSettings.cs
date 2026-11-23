using System;

namespace PaperWeatherWidget.Models
{
    public sealed class AppSettings
    {
        public string Language { get; set; }
        public string ThemeMode { get; set; } // "auto" | "light" | "dark"
        public string GlassOpacityMode { get; set; } // "airy" | "balanced" | "rich"
        public string ProvinceKey { get; set; }
        public string CityKey { get; set; }
        public string DistrictKey { get; set; }

        // Cached selected district coordinates/names so startup needs ZERO catalog decompression
        public string CachedProvinceZh { get; set; }
        public string CachedProvinceEn { get; set; }
        public string CachedCityZh { get; set; }
        public string CachedCityEn { get; set; }
        public string CachedDistrictZh { get; set; }
        public string CachedDistrictEn { get; set; }
        public double? CachedLat { get; set; }
        public double? CachedLon { get; set; }

        public int RefreshSeconds { get; set; }
        public string ForecastSlotKey { get; set; }
        public bool Topmost { get; set; }
        public bool DetailsExpanded { get; set; }

        // Window placement
        public double? WindowLeft { get; set; }
        public double? WindowTop { get; set; }
        public bool DrawerExpanded { get; set; }
        public string DrawerEdge { get; set; } // "Left" | "Right"

        public AppSettings()
        {
            Language = "zh";
            ThemeMode = "auto";
            GlassOpacityMode = "airy";
            ProvinceKey = "440000";
            CityKey = "440300";
            DistrictKey = "440309";
            RefreshSeconds = 60;
            ForecastSlotKey = "Day0";
            Topmost = true;
            DetailsExpanded = false;
            DrawerExpanded = true;
            DrawerEdge = "Right";
        }

        public string LocationKey
        {
            get { return ProvinceKey + "|" + CityKey + "|" + DistrictKey; }
        }
    }
}
