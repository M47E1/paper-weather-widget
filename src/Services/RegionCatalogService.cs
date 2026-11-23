using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using PaperWeatherWidget.Models;

namespace PaperWeatherWidget.Services
{
    /// <summary>
    /// Lazy-loaded, GZip-compressed China administrative region catalog (34 provinces, 370 cities, 2875 districts).
    /// Zero memory footprint until the user opens the location picker or resolves an uncached key.
    /// </summary>
    public sealed class RegionCatalogService
    {
        private const string EmbeddedResourceName = "PaperWeatherWidget.Resources.ChinaRegions.tsv.gz";

        private readonly object syncLock = new object();
        private List<ProvinceItem> provinces;
        private List<DistrictItem> flatDistricts;
        private bool isLoaded;

        public bool IsLoaded
        {
            get { return isLoaded; }
        }

        public DistrictItem ResolveFromSettings(AppSettings settings)
        {
            if (settings != null
                && settings.CachedLat.HasValue
                && settings.CachedLon.HasValue
                && !String.IsNullOrWhiteSpace(settings.CachedDistrictZh)
                && !String.IsNullOrWhiteSpace(settings.CachedCityZh)
                && !String.IsNullOrWhiteSpace(settings.CachedProvinceZh))
            {
                return new DistrictItem
                {
                    ProvinceKey = settings.ProvinceKey,
                    ProvinceZh = settings.CachedProvinceZh,
                    ProvinceEn = settings.CachedProvinceEn ?? String.Empty,
                    CityKey = settings.CityKey,
                    CityZh = settings.CachedCityZh,
                    CityEn = settings.CachedCityEn ?? String.Empty,
                    Key = settings.DistrictKey,
                    Zh = settings.CachedDistrictZh,
                    En = settings.CachedDistrictEn ?? String.Empty,
                    Lat = settings.CachedLat.Value,
                    Lon = settings.CachedLon.Value
                };
            }

            EnsureLoaded();
            var match = FindDistrict(
                settings != null ? settings.ProvinceKey : "440000",
                settings != null ? settings.CityKey : "440300",
                settings != null ? settings.DistrictKey : "440309");

            if (match != null && settings != null)
            {
                CacheIntoSettings(settings, match);
            }
            return match ?? CreateDefaultFallback();
        }

        public static void CacheIntoSettings(AppSettings settings, DistrictItem d)
        {
            if (settings == null || d == null) { return; }
            settings.ProvinceKey = d.ProvinceKey;
            settings.CityKey = d.CityKey;
            settings.DistrictKey = d.Key;
            settings.CachedProvinceZh = d.ProvinceZh;
            settings.CachedProvinceEn = d.ProvinceEn;
            settings.CachedCityZh = d.CityZh;
            settings.CachedCityEn = d.CityEn;
            settings.CachedDistrictZh = d.Zh;
            settings.CachedDistrictEn = d.En;
            settings.CachedLat = d.Lat;
            settings.CachedLon = d.Lon;
        }

        public List<ProvinceItem> GetProvinces()
        {
            EnsureLoaded();
            return provinces;
        }

        public DistrictItem FindDistrict(string provinceKey, string cityKey, string districtKey)
        {
            EnsureLoaded();
            if (flatDistricts == null || flatDistricts.Count == 0) { return CreateDefaultFallback(); }

            if (!String.IsNullOrWhiteSpace(districtKey))
            {
                for (var i = 0; i < flatDistricts.Count; i++)
                {
                    var d = flatDistricts[i];
                    if (String.Equals(d.Key, districtKey, StringComparison.OrdinalIgnoreCase)
                        && (String.IsNullOrWhiteSpace(cityKey) || String.Equals(d.CityKey, cityKey, StringComparison.OrdinalIgnoreCase)))
                    {
                        return d;
                    }
                }
                for (var i = 0; i < flatDistricts.Count; i++)
                {
                    if (String.Equals(flatDistricts[i].Key, districtKey, StringComparison.OrdinalIgnoreCase))
                    {
                        return flatDistricts[i];
                    }
                }
            }

            if (!String.IsNullOrWhiteSpace(cityKey))
            {
                for (var i = 0; i < flatDistricts.Count; i++)
                {
                    if (String.Equals(flatDistricts[i].CityKey, cityKey, StringComparison.OrdinalIgnoreCase))
                    {
                        return flatDistricts[i];
                    }
                }
            }

            if (!String.IsNullOrWhiteSpace(provinceKey))
            {
                for (var i = 0; i < flatDistricts.Count; i++)
                {
                    if (String.Equals(flatDistricts[i].ProvinceKey, provinceKey, StringComparison.OrdinalIgnoreCase))
                    {
                        return flatDistricts[i];
                    }
                }
            }

            return flatDistricts[0];
        }

        public List<DistrictItem> Search(string query, int maxResults)
        {
            var results = new List<DistrictItem>();
            if (String.IsNullOrWhiteSpace(query)) { return results; }

            EnsureLoaded();
            if (flatDistricts == null) { return results; }

            var q = query.Trim();
            var qNoSpace = q.Replace(" ", String.Empty);
            var exactDistrictList = new List<DistrictItem>();
            var exactOtherList = new List<DistrictItem>();
            var prefixList = new List<DistrictItem>();
            var containsList = new List<DistrictItem>();

            for (var i = 0; i < flatDistricts.Count; i++)
            {
                var d = flatDistricts[i];
                var dEnCompact = (d.En ?? String.Empty).Replace(" ", String.Empty);
                var dEnStem = LocalizationService.FormatEnglishRegionName(d.En, d.Key).Replace(" ", String.Empty);
                var cEnCompact = (d.CityEn ?? String.Empty).Replace(" ", String.Empty);
                var pEnCompact = (d.ProvinceEn ?? String.Empty).Replace(" ", String.Empty);

                if (String.Equals(d.Zh, q, StringComparison.OrdinalIgnoreCase)
                    || String.Equals(d.CityZh, q, StringComparison.OrdinalIgnoreCase)
                    || String.Equals(dEnCompact, qNoSpace, StringComparison.OrdinalIgnoreCase)
                    || String.Equals(dEnStem, qNoSpace, StringComparison.OrdinalIgnoreCase))
                {
                    if (d.Zh.EndsWith("区", StringComparison.Ordinal))
                    {
                        exactDistrictList.Add(d);
                    }
                    else
                    {
                        exactOtherList.Add(d);
                    }
                    continue;
                }

                if (d.Zh.StartsWith(q, StringComparison.OrdinalIgnoreCase)
                    || d.CityZh.StartsWith(q, StringComparison.OrdinalIgnoreCase)
                    || dEnCompact.StartsWith(qNoSpace, StringComparison.OrdinalIgnoreCase)
                    || cEnCompact.StartsWith(qNoSpace, StringComparison.OrdinalIgnoreCase))
                {
                    prefixList.Add(d);
                    continue;
                }

                if (d.Zh.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
                    || d.CityZh.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
                    || d.ProvinceZh.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
                    || dEnCompact.IndexOf(qNoSpace, StringComparison.OrdinalIgnoreCase) >= 0
                    || cEnCompact.IndexOf(qNoSpace, StringComparison.OrdinalIgnoreCase) >= 0
                    || pEnCompact.IndexOf(qNoSpace, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    containsList.Add(d);
                }
            }

            AppendUpTo(results, exactDistrictList, maxResults);
            AppendUpTo(results, exactOtherList, maxResults);
            AppendUpTo(results, prefixList, maxResults);
            AppendUpTo(results, containsList, maxResults);
            return results;
        }

        private static void AppendUpTo(List<DistrictItem> target, List<DistrictItem> source, int limit)
        {
            for (var i = 0; i < source.Count && target.Count < limit; i++)
            {
                target.Add(source[i]);
            }
        }

        public void UnloadCatalog()
        {
            lock (syncLock)
            {
                provinces = null;
                flatDistricts = null;
                isLoaded = false;
            }
        }

        private void EnsureLoaded()
        {
            if (isLoaded) { return; }
            lock (syncLock)
            {
                if (isLoaded) { return; }
                provinces = new List<ProvinceItem>(36);
                flatDistricts = new List<DistrictItem>(2900);

                using (var stream = OpenCatalogStream())
                {
                    if (stream != null)
                    {
                        using (var gz = new GZipStream(stream, CompressionMode.Decompress))
                        using (var reader = new StreamReader(gz, Encoding.UTF8))
                        {
                            ParseTsv(reader);
                        }
                    }
                }

                if (provinces.Count == 0)
                {
                    var fallback = CreateDefaultFallback();
                    var p = new ProvinceItem { Key = fallback.ProvinceKey, Zh = fallback.ProvinceZh, En = fallback.ProvinceEn, Lat = fallback.Lat, Lon = fallback.Lon };
                    var c = new CityItem { Key = fallback.CityKey, Zh = fallback.CityZh, En = fallback.CityEn, Lat = fallback.Lat, Lon = fallback.Lon, ProvinceKey = p.Key };
                    c.Districts.Add(fallback);
                    p.Cities.Add(c);
                    provinces.Add(p);
                    flatDistricts.Add(fallback);
                }

                isLoaded = true;
            }
        }

        private Stream OpenCatalogStream()
        {
            var asm = Assembly.GetExecutingAssembly();
            var res = asm.GetManifestResourceStream(EmbeddedResourceName);
            if (res != null) { return res; }

            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var candidates = new[]
            {
                Path.Combine(baseDir, "ChinaRegions.tsv.gz"),
                Path.Combine(baseDir, "data", "ChinaRegions.tsv.gz")
            };
            for (var i = 0; i < candidates.Length; i++)
            {
                if (File.Exists(candidates[i]))
                {
                    return File.OpenRead(candidates[i]);
                }
            }
            return null;
        }

        private void ParseTsv(StreamReader reader)
        {
            ProvinceItem currentProvince = null;
            CityItem currentCity = null;
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                if (line.Length < 4) { continue; }
                var parts = line.Split('\t');
                if (parts.Length < 6) { continue; }

                var kind = parts[0];
                var key = parts[1];
                var zh = parts[2];
                var en = parts[3];
                double lat, lon;
                Double.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out lat);
                Double.TryParse(parts[5], NumberStyles.Float, CultureInfo.InvariantCulture, out lon);

                if (kind == "P")
                {
                    currentProvince = new ProvinceItem
                    {
                        Key = key,
                        Zh = zh,
                        En = en,
                        Lat = lat,
                        Lon = lon
                    };
                    provinces.Add(currentProvince);
                    currentCity = null;
                }
                else if (kind == "C" && currentProvince != null)
                {
                    currentCity = new CityItem
                    {
                        Key = key,
                        Zh = zh,
                        En = en,
                        Lat = lat,
                        Lon = lon,
                        ProvinceKey = currentProvince.Key
                    };
                    currentProvince.Cities.Add(currentCity);
                }
                else if (kind == "D" && currentProvince != null && currentCity != null)
                {
                    var d = new DistrictItem
                    {
                        Key = key,
                        Zh = zh,
                        En = en,
                        Lat = lat,
                        Lon = lon,
                        CityKey = currentCity.Key,
                        CityZh = currentCity.Zh,
                        CityEn = currentCity.En,
                        ProvinceKey = currentProvince.Key,
                        ProvinceZh = currentProvince.Zh,
                        ProvinceEn = currentProvince.En
                    };
                    currentCity.Districts.Add(d);
                    flatDistricts.Add(d);
                }
            }
        }

        public static DistrictItem CreateDefaultFallback()
        {
            return new DistrictItem
            {
                ProvinceKey = "440000",
                ProvinceZh = "广东省",
                ProvinceEn = "Guang Dong Sheng",
                CityKey = "440300",
                CityZh = "深圳市",
                CityEn = "Shen Zhen Shi",
                Key = "440309",
                Zh = "龙华区",
                En = "Long Hua Qu",
                Lat = 22.695679,
                Lon = 114.044346
            };
        }
    }
}
