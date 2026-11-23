using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using PaperWeatherWidget.Models;

namespace PaperWeatherWidget.Services
{
    public sealed class WeatherFetchResult
    {
        public bool Success { get; set; }
        public WeatherSnapshot Snapshot { get; set; }
        public string ErrorMessage { get; set; }
    }

    /// <summary>
    /// Native C# weather engine:
    /// - Primary: Open-Meteo (GZip-only to avoid .NET 4.8 zlib-deflate bug, lean 48h hourly + 14d daily payload).
    /// - Fallback: wttr.in (with full per-day weatherCode, 3-hourly forecast slots, WindGustKmph, and near-term rain).
    /// - Instant in-memory slot projection (Day0..Day13 + Hour+1h/+3h/+6h/+12h/+24h) and offline snapshot cache.
    /// </summary>
    public sealed class WeatherService
    {
        private const int RequestTimeoutMs = 9000;
        private const int SnapshotStaleHours = 24;
        private const int RawCacheMaxAgeMinutes = 25;

        private const string OpenMeteoCurrentFields =
            "temperature_2m,relative_humidity_2m,apparent_temperature,is_day,precipitation,rain,showers,weather_code,cloud_cover,surface_pressure,wind_speed_10m,wind_gusts_10m";
        private const string OpenMeteoHourlyFields =
            "temperature_2m,relative_humidity_2m,apparent_temperature,precipitation_probability,precipitation,rain,showers,weather_code,cloud_cover,surface_pressure,wind_speed_10m,wind_gusts_10m,is_day";
        private const string OpenMeteoDailyFields =
            "weather_code,temperature_2m_max,temperature_2m_min,apparent_temperature_max,apparent_temperature_min,precipitation_sum,precipitation_probability_max,wind_speed_10m_max,wind_gusts_10m_max";

        private readonly object cacheLock = new object();
        private RawForecastBundle cachedBundle;

        static WeatherService()
        {
            try
            {
                ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072 | SecurityProtocolType.Tls12;
                ServicePointManager.DefaultConnectionLimit = 4;
            }
            catch
            {
            }
        }

        public static List<ForecastSlotDefinition> GetAllSlots()
        {
            var list = new List<ForecastSlotDefinition>(19);
            for (var d = 0; d < 14; d++)
            {
                list.Add(new ForecastSlotDefinition { Key = "Day" + d.ToString(CultureInfo.InvariantCulture), IsDaySlot = true, Offset = d });
            }
            var hours = new[] { 1, 3, 6, 12, 24 };
            for (var i = 0; i < hours.Length; i++)
            {
                list.Add(new ForecastSlotDefinition { Key = "Hour+" + hours[i].ToString(CultureInfo.InvariantCulture) + "h", IsDaySlot = false, Offset = hours[i] });
            }
            return list;
        }

        public WeatherSnapshot TryProjectFromMemoryCache(DistrictItem district, string slotKey)
        {
            if (district == null) { return null; }
            RawForecastBundle bundle;
            lock (cacheLock)
            {
                bundle = cachedBundle;
            }
            if (bundle == null
                || !String.Equals(bundle.LocationKey, district.LocationKey, StringComparison.OrdinalIgnoreCase)
                || (DateTime.UtcNow - bundle.FetchedAtUtc).TotalMinutes > RawCacheMaxAgeMinutes)
            {
                return null;
            }
            return ProjectBundle(bundle, district, slotKey, false);
        }

        public WeatherSnapshot TryLoadDiskSnapshot(DistrictItem district, string slotKey)
        {
            if (district == null) { return null; }
            try
            {
                var path = SettingsService.GetSnapshotPath();
                if (!File.Exists(path)) { return null; }

                var serializer = new JavaScriptSerializer();
                var env = serializer.DeserializeObject(File.ReadAllText(path, Encoding.UTF8)) as Dictionary<string, object>;
                if (env == null) { return null; }
                if ((GetInt(env, "schema") ?? 0) != 1) { return null; }

                var locationKey = GetString(env, "locationKey");
                if (!String.Equals(locationKey, district.LocationKey, StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                DateTime savedAtUtc;
                if (!DateTime.TryParse(GetString(env, "savedAt"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out savedAtUtc))
                {
                    return null;
                }
                savedAtUtc = savedAtUtc.ToUniversalTime();

                var payload = GetDict(env, "payload");
                if (payload == null || !String.Equals(GetString(payload, "status"), "ok", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                var snap = new WeatherSnapshot
                {
                    LocationKey = locationKey,
                    LocationLabelZh = LocalizationService.FormatFullLocation("zh", district),
                    LocationLabelEn = LocalizationService.FormatFullLocation("en", district),
                    ShortLocationZh = LocalizationService.FormatShortLocation("zh", district),
                    ShortLocationEn = LocalizationService.FormatShortLocation("en", district),
                    Source = NonEmpty(GetString(env, "source"), GetString(payload, "source")),
                    SlotKey = NonEmpty(GetString(payload, "slot_key"), slotKey ?? "Day0"),
                    FetchedAtUtc = savedAtUtc,
                    FromCache = true,
                    IsStale = (DateTime.UtcNow - savedAtUtc).TotalHours >= SnapshotStaleHours,
                    WeatherCode = GetInt(payload, "weather_code"),
                    IsDay = GetInt(payload, "is_day"),
                    RawConditionFallback = GetString(payload, "condition"),
                    Temperature = GetDouble(payload, "temp"),
                    FeelsLike = GetDouble(payload, "feels_like"),
                    TempMax = GetDouble(payload, "temp_max"),
                    TempMin = GetDouble(payload, "temp_min"),
                    RainNow = GetDouble(payload, "rain"),
                    TodayRain = GetDouble(payload, "today_rain"),
                    RainProbability = GetDouble(payload, "rain_probability"),
                    Humidity = GetDouble(payload, "humidity"),
                    Cloud = GetDouble(payload, "cloud"),
                    Pressure = GetDouble(payload, "pressure"),
                    Wind = GetDouble(payload, "wind"),
                    Gust = GetDouble(payload, "gust"),
                    NearTermKey = InferNearTermKey(GetString(payload, "near_term_key"), GetString(payload, "near_term"))
                };

                var dailyArr = GetArray(payload, "daily_preview");
                for (var i = 0; i < dailyArr.Length; i++)
                {
                    var dDict = dailyArr[i] as Dictionary<string, object>;
                    if (dDict == null) { continue; }
                    DateTime dt;
                    if (!DateTime.TryParse(GetString(dDict, "date"), CultureInfo.InvariantCulture, DateTimeStyles.None, out dt))
                    {
                        dt = DateTime.Today.AddDays(i);
                    }
                    snap.DailyForecast.Add(new DailyForecastItem
                    {
                        Date = dt,
                        WeatherCode = GetInt(dDict, "code"),
                        TempMax = GetDouble(dDict, "max"),
                        TempMin = GetDouble(dDict, "min"),
                        PrecipProbMax = GetInt(dDict, "prob"),
                        SlotKey = "Day" + i.ToString(CultureInfo.InvariantCulture)
                    });
                }

                return snap;
            }
            catch
            {
                return null;
            }
        }

        public void FetchWeatherAsync(DistrictItem district, string slotKey, bool forceNetwork, bool useFixture, Action<WeatherFetchResult> onCompleted)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                var result = FetchWeatherSync(district, slotKey, forceNetwork, useFixture);
                if (onCompleted != null)
                {
                    onCompleted(result);
                }
            });
        }

        public WeatherFetchResult FetchWeatherSync(DistrictItem district, string slotKey, bool forceNetwork, bool useFixture)
        {
            if (district == null)
            {
                district = RegionCatalogService.CreateDefaultFallback();
            }
            slotKey = SettingsService.NormalizeSlotKey(slotKey);

            if (useFixture)
            {
                var fixtureBundle = BuildFixtureBundle(district);
                lock (cacheLock) { cachedBundle = fixtureBundle; }
                return new WeatherFetchResult
                {
                    Success = true,
                    Snapshot = ProjectBundle(fixtureBundle, district, slotKey, false)
                };
            }

            if (!forceNetwork)
            {
                var mem = TryProjectFromMemoryCache(district, slotKey);
                if (mem != null)
                {
                    return new WeatherFetchResult { Success = true, Snapshot = mem };
                }
            }

            string openMeteoError = null;
            try
            {
                var url = BuildOpenMeteoUrl(district.Lat, district.Lon);
                var body = HttpGetString(url);
                var bundle = ParseOpenMeteo(body, district);
                if (bundle != null)
                {
                    lock (cacheLock) { cachedBundle = bundle; }
                    var snap = ProjectBundle(bundle, district, slotKey, false);
                    SaveDiskSnapshot(snap, district);
                    return new WeatherFetchResult { Success = true, Snapshot = snap };
                }
            }
            catch (Exception ex)
            {
                openMeteoError = ex.Message;
            }

            // Fallback: wttr.in
            try
            {
                var wttrUrl = String.Format(
                    CultureInfo.InvariantCulture,
                    "https://wttr.in/{0},{1}?format=j1",
                    district.Lat.ToString("0.######", CultureInfo.InvariantCulture),
                    district.Lon.ToString("0.######", CultureInfo.InvariantCulture));
                var body = HttpGetString(wttrUrl);
                var bundle = ParseWttr(body, district);
                if (bundle != null)
                {
                    lock (cacheLock) { cachedBundle = bundle; }
                    var snap = ProjectBundle(bundle, district, slotKey, false);
                    SaveDiskSnapshot(snap, district);
                    return new WeatherFetchResult { Success = true, Snapshot = snap };
                }
            }
            catch (Exception ex)
            {
                return new WeatherFetchResult
                {
                    Success = false,
                    ErrorMessage = NonEmpty(openMeteoError, ex.Message)
                };
            }

            return new WeatherFetchResult
            {
                Success = false,
                ErrorMessage = NonEmpty(openMeteoError, "Weather service unavailable")
            };
        }

        private static string BuildOpenMeteoUrl(double lat, double lon)
        {
            // Lean 48h hourly + 14d daily (~6 KB total, responds in ~1-2s with full 5-14 day forecasts!)
            return String.Format(
                CultureInfo.InvariantCulture,
                "https://api.open-meteo.com/v1/forecast?latitude={0}&longitude={1}&current={2}&hourly={3}&daily={4}&timezone=auto&forecast_days=14&forecast_hours=48&temperature_unit=celsius&wind_speed_unit=kmh&precipitation_unit=mm",
                lat.ToString("0.######", CultureInfo.InvariantCulture),
                lon.ToString("0.######", CultureInfo.InvariantCulture),
                OpenMeteoCurrentFields,
                OpenMeteoHourlyFields,
                OpenMeteoDailyFields);
        }

        private static string HttpGetString(string url)
        {
            // Prefer direct connection first (fast on Open-Meteo & wttr.in), fall back to system proxy if direct fails
            try
            {
                return HttpGetCore(url, false);
            }
            catch
            {
                return HttpGetCore(url, true);
            }
        }

        private static string HttpGetCore(string url, bool useSystemProxy)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = "GET";
            req.Timeout = RequestTimeoutMs;
            req.ReadWriteTimeout = RequestTimeoutMs;
            req.UserAgent = "PaperWeatherWidget/4.2";
            // CRITICAL: Use GZip only — .NET Framework 4.8 DeflateStream fails on Open-Meteo zlib-deflate headers!
            req.AutomaticDecompression = DecompressionMethods.GZip;
            req.KeepAlive = false;
            if (!useSystemProxy)
            {
                req.Proxy = null;
            }

            using (var resp = (HttpWebResponse)req.GetResponse())
            using (var stream = resp.GetResponseStream())
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            {
                return reader.ReadToEnd();
            }
        }

        public static RawForecastBundle ParseOpenMeteo(string jsonText, DistrictItem district)
        {
            if (String.IsNullOrWhiteSpace(jsonText)) { return null; }
            var serializer = new JavaScriptSerializer();
            serializer.MaxJsonLength = 8 * 1024 * 1024;
            var root = serializer.DeserializeObject(jsonText) as Dictionary<string, object>;
            if (root == null) { return null; }

            var current = GetDict(root, "current");
            var hourly = GetDict(root, "hourly");
            var daily = GetDict(root, "daily");
            if (current == null && hourly == null) { return null; }

            var bundle = new RawForecastBundle
            {
                LocationKey = district.LocationKey,
                Source = "Open-Meteo",
                FetchedAtUtc = DateTime.UtcNow,
                BaseTimeLocal = DateTime.Now
            };

            if (current != null)
            {
                DateTime parsedBase;
                if (DateTime.TryParse(GetString(current, "time"), CultureInfo.InvariantCulture, DateTimeStyles.None, out parsedBase))
                {
                    bundle.BaseTimeLocal = parsedBase;
                }
                bundle.Current = new HourlyForecastItem
                {
                    Time = bundle.BaseTimeLocal,
                    WeatherCode = GetInt(current, "weather_code"),
                    IsDay = GetInt(current, "is_day"),
                    Temp = GetDouble(current, "temperature_2m"),
                    FeelsLike = GetDouble(current, "apparent_temperature"),
                    RainNow = ComputeRainAmount(GetDouble(current, "precipitation"), GetDouble(current, "rain"), GetDouble(current, "showers")),
                    Humidity = GetDouble(current, "relative_humidity_2m"),
                    Cloud = GetDouble(current, "cloud_cover"),
                    Pressure = GetDouble(current, "surface_pressure"),
                    Wind = GetDouble(current, "wind_speed_10m"),
                    Gust = GetDouble(current, "wind_gusts_10m")
                };
            }

            if (hourly != null)
            {
                var times = GetArray(hourly, "time");
                var codes = GetArray(hourly, "weather_code");
                var isDays = GetArray(hourly, "is_day");
                var temps = GetArray(hourly, "temperature_2m");
                var feels = GetArray(hourly, "apparent_temperature");
                var precips = GetArray(hourly, "precipitation");
                var rains = GetArray(hourly, "rain");
                var showers = GetArray(hourly, "showers");
                var probs = GetArray(hourly, "precipitation_probability");
                var hums = GetArray(hourly, "relative_humidity_2m");
                var clouds = GetArray(hourly, "cloud_cover");
                var pressures = GetArray(hourly, "surface_pressure");
                var winds = GetArray(hourly, "wind_speed_10m");
                var gusts = GetArray(hourly, "wind_gusts_10m");

                for (var i = 0; i < times.Length; i++)
                {
                    DateTime t;
                    var ts = times[i] == null ? String.Empty : Convert.ToString(times[i], CultureInfo.InvariantCulture);
                    if (!DateTime.TryParse(ts, CultureInfo.InvariantCulture, DateTimeStyles.None, out t))
                    {
                        continue;
                    }
                    bundle.Hourly.Add(new HourlyForecastItem
                    {
                        Time = t,
                        WeatherCode = ArrInt(codes, i),
                        IsDay = ArrInt(isDays, i),
                        Temp = ArrDouble(temps, i),
                        FeelsLike = ArrDouble(feels, i),
                        RainNow = ComputeRainAmount(ArrDouble(precips, i), ArrDouble(rains, i), ArrDouble(showers, i)),
                        RainProbability = ArrInt(probs, i),
                        Humidity = ArrDouble(hums, i),
                        Cloud = ArrDouble(clouds, i),
                        Pressure = ArrDouble(pressures, i),
                        Wind = ArrDouble(winds, i),
                        Gust = ArrDouble(gusts, i)
                    });
                }
            }

            if (daily != null)
            {
                var dates = GetArray(daily, "time");
                var codes = GetArray(daily, "weather_code");
                var maxs = GetArray(daily, "temperature_2m_max");
                var mins = GetArray(daily, "temperature_2m_min");
                var fMaxs = GetArray(daily, "apparent_temperature_max");
                var fMins = GetArray(daily, "apparent_temperature_min");
                var sums = GetArray(daily, "precipitation_sum");
                var probs = GetArray(daily, "precipitation_probability_max");
                var wMaxs = GetArray(daily, "wind_speed_10m_max");
                var gMaxs = GetArray(daily, "wind_gusts_10m_max");

                for (var i = 0; i < dates.Length; i++)
                {
                    DateTime d;
                    var ds = dates[i] == null ? String.Empty : Convert.ToString(dates[i], CultureInfo.InvariantCulture);
                    if (!DateTime.TryParse(ds, CultureInfo.InvariantCulture, DateTimeStyles.None, out d))
                    {
                        d = DateTime.Today.AddDays(i);
                    }
                    bundle.Daily.Add(new DailyForecastItem
                    {
                        Date = d,
                        WeatherCode = ArrInt(codes, i),
                        TempMax = ArrDouble(maxs, i),
                        TempMin = ArrDouble(mins, i),
                        FeelsMax = ArrDouble(fMaxs, i),
                        FeelsMin = ArrDouble(fMins, i),
                        PrecipSum = ArrDouble(sums, i),
                        PrecipProbMax = ArrInt(probs, i),
                        WindMax = ArrDouble(wMaxs, i),
                        GustMax = ArrDouble(gMaxs, i),
                        SlotKey = "Day" + i.ToString(CultureInfo.InvariantCulture)
                    });
                }
            }

            bundle.NearTermKey = ComputeNearTermKey(bundle.Hourly, bundle.BaseTimeLocal);
            return bundle;
        }

        private static RawForecastBundle ParseWttr(string jsonText, DistrictItem district)
        {
            if (String.IsNullOrWhiteSpace(jsonText)) { return null; }
            var serializer = new JavaScriptSerializer();
            var root = serializer.DeserializeObject(jsonText) as Dictionary<string, object>;
            if (root == null) { return null; }

            var ccArr = GetArray(root, "current_condition");
            if (ccArr.Length == 0) { return null; }
            var cur = ccArr[0] as Dictionary<string, object>;
            if (cur == null) { return null; }

            var now = DateTime.Now;
            var bundle = new RawForecastBundle
            {
                LocationKey = district.LocationKey,
                Source = "wttr.in",
                FetchedAtUtc = DateTime.UtcNow,
                BaseTimeLocal = now,
                NearTermKey = "unavailable"
            };

            var descArr = GetArray(cur, "weatherDesc");
            if (descArr.Length > 0)
            {
                var descDict = descArr[0] as Dictionary<string, object>;
                bundle.RawConditionFallback = GetString(descDict, "value");
            }

            var rainNow = GetDouble(cur, "precipMM") ?? 0.0;
            bundle.Current = new HourlyForecastItem
            {
                Time = now,
                WeatherCode = MapWttrCodeToWmo(GetInt(cur, "weatherCode")),
                IsDay = (now.Hour >= 6 && now.Hour < 19) ? 1 : 0,
                Temp = GetDouble(cur, "temp_C"),
                FeelsLike = GetDouble(cur, "FeelsLikeC"),
                RainNow = rainNow,
                Humidity = GetDouble(cur, "humidity"),
                Cloud = GetDouble(cur, "cloudcover"),
                Pressure = GetDouble(cur, "pressure"),
                Wind = GetDouble(cur, "windspeedKmph"),
                Gust = GetDouble(cur, "WindGustKmph")
            };

            var weatherDays = GetArray(root, "weather");
            for (var i = 0; i < weatherDays.Length; i++)
            {
                var dayDict = weatherDays[i] as Dictionary<string, object>;
                if (dayDict == null) { continue; }
                DateTime dt;
                if (!DateTime.TryParse(GetString(dayDict, "date"), CultureInfo.InvariantCulture, DateTimeStyles.None, out dt))
                {
                    dt = DateTime.Today.AddDays(i);
                }

                double sumRain = 0.0;
                int maxProb = 0;
                double maxWind = 0.0;
                double maxGust = 0.0;
                int? middayCode = null;

                var hrs = GetArray(dayDict, "hourly");
                for (var h = 0; h < hrs.Length; h++)
                {
                    var hd = hrs[h] as Dictionary<string, object>;
                    if (hd == null) { continue; }
                    var hrPrecip = GetDouble(hd, "precipMM") ?? 0.0;
                    var hrProb = GetInt(hd, "chanceofrain") ?? 0;
                    var hrWind = GetDouble(hd, "windspeedKmph");
                    var hrGust = GetDouble(hd, "WindGustKmph");
                    var hrWmo = MapWttrCodeToWmo(GetInt(hd, "weatherCode"));
                    var timeRaw = GetInt(hd, "time") ?? (h * 300);
                    var hrClock = Math.Max(0, Math.Min(23, timeRaw / 100));

                    sumRain += hrPrecip;
                    if (hrProb > maxProb) { maxProb = hrProb; }
                    if (hrWind.HasValue && hrWind.Value > maxWind) { maxWind = hrWind.Value; }
                    if (hrGust.HasValue && hrGust.Value > maxGust) { maxGust = hrGust.Value; }
                    if (hrClock == 12 || (middayCode == null && h == hrs.Length / 2))
                    {
                        middayCode = hrWmo;
                    }

                    bundle.Hourly.Add(new HourlyForecastItem
                    {
                        Time = dt.Date.AddHours(hrClock),
                        WeatherCode = hrWmo,
                        IsDay = (hrClock >= 6 && hrClock < 19) ? 1 : 0,
                        Temp = GetDouble(hd, "tempC"),
                        FeelsLike = GetDouble(hd, "FeelsLikeC"),
                        RainNow = hrPrecip,
                        RainProbability = hrProb,
                        Humidity = GetDouble(hd, "humidity"),
                        Cloud = GetDouble(hd, "cloudcover"),
                        Pressure = GetDouble(hd, "pressure"),
                        Wind = hrWind,
                        Gust = hrGust
                    });
                }

                bundle.Daily.Add(new DailyForecastItem
                {
                    Date = dt,
                    WeatherCode = middayCode ?? bundle.Current.WeatherCode,
                    TempMax = GetDouble(dayDict, "maxtempC"),
                    TempMin = GetDouble(dayDict, "mintempC"),
                    PrecipSum = sumRain,
                    PrecipProbMax = maxProb,
                    WindMax = maxWind > 0 ? (double?)maxWind : null,
                    GustMax = maxGust > 0 ? (double?)maxGust : null,
                    SlotKey = "Day" + i.ToString(CultureInfo.InvariantCulture)
                });
            }

            // Fill missing current gust from nearest hourly slot if wttr current_condition omitted it
            if (!bundle.Current.Gust.HasValue && bundle.Hourly.Count > 0)
            {
                var nearest = FindNearestHourly(bundle.Hourly, now);
                if (nearest != null) { bundle.Current.Gust = nearest.Gust; }
            }
            bundle.NearTermKey = ComputeNearTermKey(bundle.Hourly, now);
            return bundle;
        }

        public static WeatherSnapshot ProjectBundle(RawForecastBundle bundle, DistrictItem district, string slotKey, bool fromCache)
        {
            slotKey = SettingsService.NormalizeSlotKey(slotKey);
            var isCurrent = String.Equals(slotKey, "Day0", StringComparison.OrdinalIgnoreCase);
            var targetTime = ResolveTargetTime(slotKey, bundle.BaseTimeLocal);
            DailyForecastItem dailyMatch = FindDailyForDate(bundle.Daily, targetTime.Date);

            HourlyForecastItem sample = null;
            if (isCurrent && bundle.Current != null)
            {
                sample = bundle.Current;
            }
            else if (bundle.Hourly.Count > 0)
            {
                var nearest = FindNearestHourly(bundle.Hourly, targetTime);
                if (nearest != null && Math.Abs((nearest.Time - targetTime).TotalHours) <= 12.0)
                {
                    sample = nearest;
                }
            }

            // For days beyond the 48h hourly horizon (Day2..Day13), project cleanly from DailyForecastItem!
            if (sample == null && dailyMatch != null)
            {
                double? midTemp = (dailyMatch.TempMax.HasValue && dailyMatch.TempMin.HasValue)
                    ? (double?)Math.Round((dailyMatch.TempMax.Value + dailyMatch.TempMin.Value) / 2.0, 1)
                    : (dailyMatch.TempMax ?? dailyMatch.TempMin);
                double? midFeels = (dailyMatch.FeelsMax.HasValue && dailyMatch.FeelsMin.HasValue)
                    ? (double?)Math.Round((dailyMatch.FeelsMax.Value + dailyMatch.FeelsMin.Value) / 2.0, 1)
                    : midTemp;
                sample = new HourlyForecastItem
                {
                    Time = targetTime,
                    WeatherCode = dailyMatch.WeatherCode,
                    IsDay = 1,
                    Temp = midTemp,
                    FeelsLike = midFeels,
                    RainNow = dailyMatch.PrecipSum,
                    RainProbability = dailyMatch.PrecipProbMax,
                    Wind = dailyMatch.WindMax,
                    Gust = dailyMatch.GustMax
                };
            }
            if (sample == null)
            {
                sample = bundle.Current ?? new HourlyForecastItem();
            }

            double? rainProb = sample.RainProbability.HasValue
                ? (double?)sample.RainProbability.Value
                : (dailyMatch != null && dailyMatch.PrecipProbMax.HasValue ? (double?)dailyMatch.PrecipProbMax.Value : 0.0);

            if (isCurrent && dailyMatch != null && dailyMatch.PrecipProbMax.HasValue)
            {
                rainProb = dailyMatch.PrecipProbMax.Value;
            }

            var snap = new WeatherSnapshot
            {
                LocationKey = district.LocationKey,
                LocationLabelZh = LocalizationService.FormatFullLocation("zh", district),
                LocationLabelEn = LocalizationService.FormatFullLocation("en", district),
                ShortLocationZh = LocalizationService.FormatShortLocation("zh", district),
                ShortLocationEn = LocalizationService.FormatShortLocation("en", district),
                Source = bundle.Source,
                SlotKey = slotKey,
                FetchedAtUtc = bundle.FetchedAtUtc,
                FromCache = fromCache,
                IsStale = false,
                WeatherCode = sample.WeatherCode,
                IsDay = sample.IsDay,
                RawConditionFallback = bundle.RawConditionFallback,
                Temperature = sample.Temp,
                FeelsLike = sample.FeelsLike,
                TempMax = dailyMatch != null ? dailyMatch.TempMax : null,
                TempMin = dailyMatch != null ? dailyMatch.TempMin : null,
                RainNow = sample.RainNow ?? 0.0,
                TodayRain = dailyMatch != null && dailyMatch.PrecipSum.HasValue ? dailyMatch.PrecipSum.Value : (sample.RainNow ?? 0.0),
                RainProbability = rainProb,
                Humidity = sample.Humidity,
                Cloud = sample.Cloud,
                Pressure = sample.Pressure,
                Wind = sample.Wind,
                Gust = sample.Gust,
                NearTermKey = bundle.NearTermKey
            };

            var previewCount = Math.Min(5, bundle.Daily.Count);
            for (var i = 0; i < previewCount; i++)
            {
                snap.DailyForecast.Add(bundle.Daily[i]);
            }

            return snap;
        }

        private static RawForecastBundle BuildFixtureBundle(DistrictItem district)
        {
            var now = DateTime.Now;
            var bundle = new RawForecastBundle
            {
                LocationKey = district.LocationKey,
                Source = "Open-Meteo",
                FetchedAtUtc = DateTime.UtcNow,
                BaseTimeLocal = now,
                NearTermKey = "none"
            };
            bundle.Current = new HourlyForecastItem
            {
                Time = now,
                WeatherCode = 2,
                IsDay = 1,
                Temp = 24.6,
                FeelsLike = 25.8,
                RainNow = 0.0,
                RainProbability = 35,
                Humidity = 68.0,
                Cloud = 45.0,
                Pressure = 1013.0,
                Wind = 12.0,
                Gust = 21.0
            };
            for (var h = 0; h <= 48; h++)
            {
                bundle.Hourly.Add(new HourlyForecastItem
                {
                    Time = now.AddHours(h),
                    WeatherCode = h % 6 == 0 ? 61 : (h % 3 == 0 ? 2 : 1),
                    IsDay = (now.AddHours(h).Hour >= 6 && now.AddHours(h).Hour < 19) ? 1 : 0,
                    Temp = 24.6 + Math.Sin(h / 4.0) * 3.2,
                    FeelsLike = 25.4 + Math.Sin(h / 4.0) * 3.2,
                    RainNow = h % 6 == 0 ? 0.8 : 0.0,
                    RainProbability = h % 6 == 0 ? 60 : 25,
                    Humidity = 65.0 + (h % 10),
                    Cloud = 35.0 + (h % 5) * 10,
                    Pressure = 1013.0,
                    Wind = 11.0 + (h % 5),
                    Gust = 19.0 + (h % 6)
                });
            }
            var codes = new[] { 2, 61, 1, 0, 80, 2, 1 };
            for (var d = 0; d < 14; d++)
            {
                bundle.Daily.Add(new DailyForecastItem
                {
                    Date = DateTime.Today.AddDays(d),
                    WeatherCode = codes[d % codes.Length],
                    TempMax = 27.5 + (d % 3),
                    TempMin = 19.2 + (d % 2),
                    FeelsMax = 28.0 + (d % 3),
                    FeelsMin = 18.8 + (d % 2),
                    PrecipSum = d % 2 == 1 ? 3.4 : 1.2,
                    PrecipProbMax = d % 2 == 1 ? 65 : 35,
                    WindMax = 14.0 + (d % 4),
                    GustMax = 22.0 + (d % 5),
                    SlotKey = "Day" + d.ToString(CultureInfo.InvariantCulture)
                });
            }
            return bundle;
        }

        private static void SaveDiskSnapshot(WeatherSnapshot snap, DistrictItem district)
        {
            if (snap == null || district == null) { return; }
            try
            {
                var payload = new Dictionary<string, object>();
                payload["status"] = "ok";
                payload["source"] = snap.Source ?? "Open-Meteo";
                payload["slot_key"] = snap.SlotKey ?? "Day0";
                payload["locationKey"] = snap.LocationKey;
                payload["location"] = snap.LocationLabelZh;
                payload["weather_code"] = snap.WeatherCode;
                payload["is_day"] = snap.IsDay;
                payload["condition"] = snap.RawConditionFallback ?? String.Empty;
                payload["temp"] = snap.Temperature;
                payload["feels_like"] = snap.FeelsLike;
                payload["temp_max"] = snap.TempMax;
                payload["temp_min"] = snap.TempMin;
                payload["rain"] = snap.RainNow;
                payload["today_rain"] = snap.TodayRain;
                payload["rain_probability"] = snap.RainProbability;
                payload["humidity"] = snap.Humidity;
                payload["cloud"] = snap.Cloud;
                payload["pressure"] = snap.Pressure;
                payload["wind"] = snap.Wind;
                payload["gust"] = snap.Gust;
                payload["near_term_key"] = snap.NearTermKey ?? "unavailable";

                var dailyList = new List<Dictionary<string, object>>();
                if (snap.DailyForecast != null)
                {
                    for (var i = 0; i < snap.DailyForecast.Count && i < 5; i++)
                    {
                        var item = snap.DailyForecast[i];
                        var dd = new Dictionary<string, object>();
                        dd["date"] = item.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                        dd["code"] = item.WeatherCode;
                        dd["max"] = item.TempMax;
                        dd["min"] = item.TempMin;
                        dd["prob"] = item.PrecipProbMax;
                        dailyList.Add(dd);
                    }
                }
                payload["daily_preview"] = dailyList;

                var envelope = new Dictionary<string, object>();
                envelope["schema"] = 1;
                envelope["savedAt"] = snap.FetchedAtUtc.ToString("o", CultureInfo.InvariantCulture);
                envelope["source"] = snap.Source ?? "Open-Meteo";
                envelope["locationKey"] = snap.LocationKey;
                envelope["locationLabel"] = snap.LocationLabelZh;
                envelope["payload"] = payload;

                var serializer = new JavaScriptSerializer();
                SettingsService.WriteTextAtomic(SettingsService.GetSnapshotPath(), serializer.Serialize(envelope));
            }
            catch
            {
            }
        }

        private static DateTime ResolveTargetTime(string slotKey, DateTime baseTime)
        {
            if (String.IsNullOrWhiteSpace(slotKey) || String.Equals(slotKey, "Day0", StringComparison.OrdinalIgnoreCase))
            {
                return baseTime;
            }
            if (slotKey.StartsWith("Day", StringComparison.OrdinalIgnoreCase))
            {
                int day;
                if (Int32.TryParse(slotKey.Substring(3), out day) && day > 0)
                {
                    return baseTime.Date.AddDays(day).AddHours(12);
                }
            }
            if (slotKey.StartsWith("Hour+", StringComparison.OrdinalIgnoreCase) && slotKey.EndsWith("h", StringComparison.OrdinalIgnoreCase))
            {
                int hrs;
                var num = slotKey.Substring(5, slotKey.Length - 6);
                if (Int32.TryParse(num, out hrs) && hrs > 0)
                {
                    return baseTime.AddHours(hrs);
                }
            }
            return baseTime;
        }

        private static HourlyForecastItem FindNearestHourly(List<HourlyForecastItem> list, DateTime target)
        {
            HourlyForecastItem best = null;
            var bestDiff = Double.MaxValue;
            for (var i = 0; i < list.Count; i++)
            {
                var diff = Math.Abs((list[i].Time - target).TotalMinutes);
                if (diff < bestDiff)
                {
                    bestDiff = diff;
                    best = list[i];
                }
            }
            return best;
        }

        private static DailyForecastItem FindDailyForDate(List<DailyForecastItem> list, DateTime date)
        {
            if (list == null || list.Count == 0) { return null; }
            for (var i = 0; i < list.Count; i++)
            {
                if (list[i].Date.Date == date.Date)
                {
                    return list[i];
                }
            }
            return list[0];
        }

        private static string ComputeNearTermKey(List<HourlyForecastItem> hourly, DateTime now)
        {
            if (hourly == null || hourly.Count == 0) { return "unavailable"; }
            double maxRain = 0.0;
            int maxProb = 0;
            bool any = false;
            var limit = now.AddHours(3.5);
            for (var i = 0; i < hourly.Count; i++)
            {
                var item = hourly[i];
                if (item.Time < now.AddMinutes(-90) || item.Time > limit) { continue; }
                any = true;
                if (item.RainNow.HasValue && item.RainNow.Value > maxRain) { maxRain = item.RainNow.Value; }
                if (item.RainProbability.HasValue && item.RainProbability.Value > maxProb) { maxProb = item.RainProbability.Value; }
            }
            if (!any) { return "unavailable"; }
            if (maxRain >= 8.0 || maxProb >= 80) { return "heavy"; }
            if (maxRain > 0.0 || maxProb >= 40) { return "possible"; }
            return "none";
        }

        private static string InferNearTermKey(string explicitKey, string legacyText)
        {
            if (!String.IsNullOrWhiteSpace(explicitKey)) { return explicitKey; }
            if (String.IsNullOrWhiteSpace(legacyText)) { return "unavailable"; }
            if (legacyText.IndexOf("强降雨", StringComparison.Ordinal) >= 0 || legacyText.IndexOf("heavy", StringComparison.OrdinalIgnoreCase) >= 0) { return "heavy"; }
            if (legacyText.IndexOf("可能", StringComparison.Ordinal) >= 0 || legacyText.IndexOf("possible", StringComparison.OrdinalIgnoreCase) >= 0) { return "possible"; }
            if (legacyText.IndexOf("暂无", StringComparison.Ordinal) >= 0 || legacyText.IndexOf("No near", StringComparison.OrdinalIgnoreCase) >= 0) { return "none"; }
            return "unavailable";
        }

        private static double ComputeRainAmount(double? precip, double? rain, double? showers)
        {
            var p = precip ?? 0.0;
            var rs = (rain ?? 0.0) + (showers ?? 0.0);
            return Math.Max(p, rs);
        }

        private static int? MapWttrCodeToWmo(int? wttrCode)
        {
            if (!wttrCode.HasValue) { return 2; }
            var c = wttrCode.Value;
            if (c == 113) { return 0; }
            if (c == 116) { return 2; }
            if (c == 119 || c == 122) { return 3; }
            if (c == 143 || c == 248 || c == 260) { return 45; }
            if (c >= 386) { return 95; }
            if (c >= 323 && c <= 338) { return 73; }
            if (c >= 302 && c <= 359) { return 63; }
            if (c >= 176 && c <= 301) { return 61; }
            return 2;
        }

        private static Dictionary<string, object> GetDict(Dictionary<string, object> d, string key)
        {
            object v;
            return (d != null && d.TryGetValue(key, out v)) ? v as Dictionary<string, object> : null;
        }

        private static object[] GetArray(Dictionary<string, object> d, string key)
        {
            object v;
            if (d == null || !d.TryGetValue(key, out v) || v == null) { return new object[0]; }
            var arr = v as object[];
            if (arr != null) { return arr; }
            var en = v as IEnumerable;
            if (en == null || v is string) { return new object[0]; }
            var list = new List<object>();
            foreach (var x in en) { list.Add(x); }
            return list.ToArray();
        }

        private static string GetString(Dictionary<string, object> d, string key)
        {
            object v;
            return (d != null && d.TryGetValue(key, out v) && v != null) ? Convert.ToString(v, CultureInfo.InvariantCulture) : String.Empty;
        }

        private static double? GetDouble(Dictionary<string, object> d, string key)
        {
            object v;
            if (d == null || !d.TryGetValue(key, out v) || v == null) { return null; }
            try { return Convert.ToDouble(v, CultureInfo.InvariantCulture); } catch { return null; }
        }

        private static int? GetInt(Dictionary<string, object> d, string key)
        {
            object v;
            if (d == null || !d.TryGetValue(key, out v) || v == null) { return null; }
            try { return Convert.ToInt32(v, CultureInfo.InvariantCulture); } catch { return null; }
        }

        private static double? ArrDouble(object[] arr, int i)
        {
            if (arr == null || i < 0 || i >= arr.Length || arr[i] == null) { return null; }
            try { return Convert.ToDouble(arr[i], CultureInfo.InvariantCulture); } catch { return null; }
        }

        private static int? ArrInt(object[] arr, int i)
        {
            if (arr == null || i < 0 || i >= arr.Length || arr[i] == null) { return null; }
            try { return Convert.ToInt32(arr[i], CultureInfo.InvariantCulture); } catch { return null; }
        }

        private static string NonEmpty(string a, string b)
        {
            return String.IsNullOrWhiteSpace(a) ? (b ?? String.Empty) : a;
        }
    }
}
