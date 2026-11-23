using System;
using System.Collections.Generic;

namespace PaperWeatherWidget.Models
{
    public enum WeatherSyncState
    {
        Loading,
        Live,
        Refreshing,
        Cached,
        Stale,
        Failed,
        FailedShowingCache
    }

    public sealed class ForecastSlotDefinition
    {
        public string Key { get; set; }
        public bool IsDaySlot { get; set; }
        public int Offset { get; set; }
    }

    public sealed class DailyForecastItem
    {
        public DateTime Date { get; set; }
        public int? WeatherCode { get; set; }
        public double? TempMax { get; set; }
        public double? TempMin { get; set; }
        public double? FeelsMax { get; set; }
        public double? FeelsMin { get; set; }
        public double? PrecipSum { get; set; }
        public int? PrecipProbMax { get; set; }
        public double? WindMax { get; set; }
        public double? GustMax { get; set; }
        public string SlotKey { get; set; }
    }

    public sealed class HourlyForecastItem
    {
        public DateTime Time { get; set; }
        public int? WeatherCode { get; set; }
        public int? IsDay { get; set; }
        public double? Temp { get; set; }
        public double? FeelsLike { get; set; }
        public double? RainNow { get; set; }
        public int? RainProbability { get; set; }
        public double? Humidity { get; set; }
        public double? Cloud { get; set; }
        public double? Pressure { get; set; }
        public double? Wind { get; set; }
        public double? Gust { get; set; }
    }

    public sealed class WeatherSnapshot
    {
        public string LocationKey { get; set; }
        public string LocationLabelZh { get; set; }
        public string LocationLabelEn { get; set; }
        public string ShortLocationZh { get; set; }
        public string ShortLocationEn { get; set; }
        public string Source { get; set; }
        public string SlotKey { get; set; }
        public DateTime FetchedAtUtc { get; set; }
        public bool FromCache { get; set; }
        public bool IsStale { get; set; }

        public int? WeatherCode { get; set; }
        public int? IsDay { get; set; }
        public string RawConditionFallback { get; set; }

        public double? Temperature { get; set; }
        public double? FeelsLike { get; set; }
        public double? TempMax { get; set; }
        public double? TempMin { get; set; }

        public double? RainNow { get; set; }
        public double? TodayRain { get; set; }
        public double? RainProbability { get; set; }
        public double? Humidity { get; set; }
        public double? Cloud { get; set; }
        public double? Pressure { get; set; }
        public double? Wind { get; set; }
        public double? Gust { get; set; }

        public string NearTermKey { get; set; } // "none" | "possible" | "heavy" | "unavailable"
        public List<DailyForecastItem> DailyForecast { get; set; }

        public WeatherSnapshot()
        {
            DailyForecast = new List<DailyForecastItem>();
            SlotKey = "Day0";
            NearTermKey = "unavailable";
        }
    }

    public sealed class RawForecastBundle
    {
        public string LocationKey { get; set; }
        public string Source { get; set; }
        public DateTime FetchedAtUtc { get; set; }
        public DateTime BaseTimeLocal { get; set; }

        public HourlyForecastItem Current { get; set; }
        public List<HourlyForecastItem> Hourly { get; private set; }
        public List<DailyForecastItem> Daily { get; private set; }
        public string RawConditionFallback { get; set; }
        public string NearTermKey { get; set; }

        public RawForecastBundle()
        {
            Hourly = new List<HourlyForecastItem>();
            Daily = new List<DailyForecastItem>();
            NearTermKey = "unavailable";
        }
    }
}
