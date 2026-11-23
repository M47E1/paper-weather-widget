using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using PaperWeatherWidget.Models;

namespace PaperWeatherWidget.Services
{
    public static class LocalizationService
    {
        public static bool IsEnglish(string lang)
        {
            return String.Equals(lang, "en", StringComparison.OrdinalIgnoreCase);
        }

        public static string Ui(string lang, string key)
        {
            var zh = !IsEnglish(lang);
            switch (key)
            {
                case "AppTitle": return zh ? "纸感天气" : "Paper Weather";
                case "Settings": return zh ? "设置" : "Settings";
                case "Back": return zh ? "返回" : "Back";
                case "Location": return zh ? "选择地区" : "Location";
                case "SearchPlaceholder": return zh ? "搜索省 / 市 / 区县，如 顺德、龙华、朝阳..." : "Search city or district, e.g. Shunde, Longhua...";
                case "NoSearchResults": return zh ? "未找到匹配的地区" : "No matching locations";
                case "RefreshNow": return zh ? "立即刷新" : "Refresh now";
                case "PinTopmost": return zh ? "窗口置顶" : "Always on top";
                case "UnpinTopmost": return zh ? "取消置顶" : "Unpin from top";
                case "DrawerCollapse": return zh ? "收起到屏幕边缘" : "Dock to screen edge";
                case "DrawerExpand": return zh ? "展开天气卡片" : "Expand weather widget";
                case "Exit": return zh ? "退出" : "Exit";
                case "HideToTray": return zh ? "隐藏到托盘" : "Hide to tray";
                case "ShowWidget": return zh ? "显示天气" : "Show widget";
                case "Province": return zh ? "省 / 直辖市" : "Province";
                case "City": return zh ? "城市" : "City";
                case "District": return zh ? "区 / 县" : "District";
                case "Refresh": return zh ? "自动刷新频率" : "Refresh interval";
                case "Theme": return zh ? "玻璃色调" : "Glass tone";
                case "ThemeAuto": return zh ? "自动" : "Auto";
                case "ThemeLight": return zh ? "晴朗" : "Vivid";
                case "ThemeDark": return zh ? "深邃" : "Deep";
                case "GlassOpacity": return zh ? "毛玻璃通透度" : "Glass translucency";
                case "GlassAiry": return zh ? "高透" : "Airy";
                case "GlassBalanced": return zh ? "均衡" : "Balanced";
                case "GlassRich": return zh ? "浓郁" : "Rich";
                case "ScenePreview": return zh ? "动态场景预览" : "Live FX Preview";
                case "SceneLive": return zh ? "实时" : "Live";
                case "SceneSunny": return zh ? "晴阳" : "Sun";
                case "SceneCloudy": return zh ? "多云" : "Cloud";
                case "SceneRain": return zh ? "雨滴" : "Rain";
                case "SceneStorm": return zh ? "雷暴" : "Storm";
                case "SceneNight": return zh ? "星空" : "Night";
                case "SceneSnow": return zh ? "降雪" : "Snow";
                case "Language": return zh ? "显示语言" : "Language";
                case "Startup": return zh ? "开机自启" : "Start on boot";
                case "TopmostLabel": return zh ? "窗口置顶" : "Always on top";
                case "On": return zh ? "已开启" : "On";
                case "Off": return zh ? "已关闭" : "Off";
                case "MoreDetails": return zh ? "展开更多指标与 5 日预报" : "More metrics & 5-day";
                case "LessDetails": return zh ? "收起详细面板" : "Hide details";
                case "MoreSlots": return zh ? "更多" : "More";
                case "Now": return zh ? "现在" : "Now";
                case "Today": return zh ? "今天" : "Today";
                case "Tomorrow": return zh ? "明天" : "Tomorrow";
                case "DayAfterTomorrow": return zh ? "后天" : "+2d";
                case "Feels": return zh ? "体感" : "Feels";
                case "RainProb": return zh ? "降雨概率" : "Precip";
                case "Humidity": return zh ? "湿度" : "Humidity";
                case "Wind": return zh ? "风速" : "Wind";
                case "TodayRain": return zh ? "今日降雨" : "Today rain";
                case "RainNow": return zh ? "当前降雨" : "Rain now";
                case "Gust": return zh ? "阵风" : "Gust";
                case "Cloud": return zh ? "云量" : "Cloud";
                case "Pressure": return zh ? "气压" : "Pressure";
                case "FiveDayForecast": return zh ? "未来 5 天预报" : "5-Day Forecast";
                case "StateLive": return zh ? "实时" : "Live";
                case "StateRefreshing": return zh ? "刷新中" : "Updating";
                case "StateCached": return zh ? "缓存" : "Cached";
                case "StateStale": return zh ? "旧缓存" : "Stale";
                case "StateFailed": return zh ? "离线" : "Offline";
                case "UpdatedPrefix": return zh ? "更新于" : "Updated";
                case "RefreshFailedHint": return zh ? "刷新失败，当前显示缓存" : "Update failed, showing cached";
                case "OfflineHint": return zh ? "网络连接失败，请稍后重试" : "Network unavailable, retry later";
                case "NearTermNone": return zh ? "未来 2 小时无明显降雨" : "No rain expected in 2h";
                case "NearTermPossible": return zh ? "未来 2 小时可能有降雨" : "Rain possible in next 2h";
                case "NearTermHeavy": return zh ? "未来 2 小时有强降雨风险" : "Heavy rain risk in next 2h";
                case "NearTermUnavailable": return zh ? "暂无临近降雨预报" : "Near-term radar unavailable";
                case "CreditPrefix": return zh ? "开源项目" : "Open source";
                default: return key;
            }
        }

        public static string WeatherConditionText(string lang, int? wmoCode, int? isDay, string fallback)
        {
            var zh = !IsEnglish(lang);
            if (!wmoCode.HasValue)
            {
                if (!String.IsNullOrWhiteSpace(fallback) && (!IsEnglish(lang) || !ContainsCjk(fallback)))
                {
                    return fallback;
                }
                return zh ? "多云" : "Cloudy";
            }

            switch (wmoCode.Value)
            {
                case 0: return zh ? (isDay == 0 ? "晴朗夜间" : "晴") : (isDay == 0 ? "Clear night" : "Clear");
                case 1: return zh ? "大部晴朗" : "Mainly clear";
                case 2: return zh ? "多云" : "Partly cloudy";
                case 3: return zh ? "阴" : "Overcast";
                case 45:
                case 48: return zh ? "有雾" : "Fog";
                case 51: return zh ? "小毛毛雨" : "Light drizzle";
                case 53: return zh ? "毛毛雨" : "Drizzle";
                case 55: return zh ? "浓毛毛雨" : "Heavy drizzle";
                case 56:
                case 57: return zh ? "冻毛毛雨" : "Freezing drizzle";
                case 61: return zh ? "小雨" : "Light rain";
                case 63: return zh ? "中雨" : "Rain";
                case 65: return zh ? "大雨" : "Heavy rain";
                case 66:
                case 67: return zh ? "冻雨" : "Freezing rain";
                case 71: return zh ? "小雪" : "Light snow";
                case 73: return zh ? "中雪" : "Snow";
                case 75: return zh ? "大雪" : "Heavy snow";
                case 77: return zh ? "雪粒" : "Snow grains";
                case 80: return zh ? "阵雨" : "Rain showers";
                case 81: return zh ? "中阵雨" : "Moderate showers";
                case 82: return zh ? "强阵雨" : "Heavy showers";
                case 85:
                case 86: return zh ? "阵雪" : "Snow showers";
                case 95: return zh ? "雷阵雨" : "Thunderstorm";
                case 96:
                case 99: return zh ? "雷暴伴冰雹" : "Thunderstorm & hail";
                default: return zh ? "多云" : "Cloudy";
            }
        }

        public static string NearTermText(string lang, string nearTermKey)
        {
            switch (nearTermKey)
            {
                case "heavy": return Ui(lang, "NearTermHeavy");
                case "possible": return Ui(lang, "NearTermPossible");
                case "none": return Ui(lang, "NearTermNone");
                default: return Ui(lang, "NearTermUnavailable");
            }
        }

        public static string SlotPillLabel(string lang, string slotKey)
        {
            if (IsEnglish(lang) && String.Equals(slotKey, "Day1", StringComparison.OrdinalIgnoreCase))
            {
                return "+1d";
            }
            return SlotLabel(lang, slotKey);
        }

        public static string SlotLabel(string lang, string slotKey)
        {
            if (String.IsNullOrWhiteSpace(slotKey) || String.Equals(slotKey, "Day0", StringComparison.OrdinalIgnoreCase))
            {
                return Ui(lang, "Now");
            }
            if (String.Equals(slotKey, "Day1", StringComparison.OrdinalIgnoreCase))
            {
                return Ui(lang, "Tomorrow");
            }
            if (String.Equals(slotKey, "Day2", StringComparison.OrdinalIgnoreCase))
            {
                return Ui(lang, "DayAfterTomorrow");
            }
            if (slotKey.StartsWith("Day", StringComparison.OrdinalIgnoreCase))
            {
                int day;
                if (Int32.TryParse(slotKey.Substring(3), out day))
                {
                    return IsEnglish(lang) ? "+" + day.ToString(CultureInfo.InvariantCulture) + "d" : "+" + day.ToString(CultureInfo.InvariantCulture) + "天";
                }
            }
            if (slotKey.StartsWith("Hour+", StringComparison.OrdinalIgnoreCase))
            {
                return slotKey.Substring(4);
            }
            return slotKey;
        }

        public static string ShortWeekday(string lang, DateTime date, int dayIndex)
        {
            if (dayIndex == 0) { return Ui(lang, "Today"); }
            if (dayIndex == 1) { return Ui(lang, "Tomorrow"); }
            if (IsEnglish(lang))
            {
                return date.ToString("ddd", CultureInfo.InvariantCulture);
            }
            switch (date.DayOfWeek)
            {
                case DayOfWeek.Monday: return "周一";
                case DayOfWeek.Tuesday: return "周二";
                case DayOfWeek.Wednesday: return "周三";
                case DayOfWeek.Thursday: return "周四";
                case DayOfWeek.Friday: return "周五";
                case DayOfWeek.Saturday: return "周六";
                default: return "周日";
            }
        }

        public static string FormatRefreshShort(string lang, int seconds)
        {
            if (seconds >= 86400) { return IsEnglish(lang) ? "1d" : "1天"; }
            if (seconds >= 3600) { return IsEnglish(lang) ? "1h" : "1时"; }
            if (seconds >= 900) { return IsEnglish(lang) ? "15m" : "15分"; }
            return IsEnglish(lang) ? "1m" : "1分";
        }

        public static string FormatEnglishRegionName(string rawEn, string fallbackKey)
        {
            if (String.IsNullOrWhiteSpace(rawEn))
            {
                return fallbackKey ?? String.Empty;
            }
            var text = rawEn.Trim();
            switch (text)
            {
                case "Guang Dong Sheng": return "Guangdong";
                case "Shen Zhen Shi": return "Shenzhen";
                case "Fo Shan Shi": return "Foshan";
                case "Guang Zhou Shi": return "Guangzhou";
                case "Long Hua Qu": return "Longhua";
                case "Shun De Qu": return "Shunde";
                case "Bei Jing Shi": return "Beijing";
                case "Shang Hai Shi": return "Shanghai";
                case "Tian Jin Shi": return "Tianjin";
                case "Chong Qing Shi": return "Chongqing";
                case "Xiang Gang Te Bie Xing Zheng Qu": return "Hong Kong";
                case "Ao Men Te Bie Xing Zheng Qu": return "Macau";
                case "He Bei Sheng": return "Hebei";
                case "Shan Xi Sheng": return "Shanxi";
                case "Nei Meng Gu Zi Zhi Qu": return "Inner Mongolia";
                case "Liao Ning Sheng": return "Liaoning";
                case "Ji Lin Sheng": return "Jilin";
                case "Hei Long Jiang Sheng": return "Heilongjiang";
                case "Jiang Su Sheng": return "Jiangsu";
                case "Zhe Jiang Sheng": return "Zhejiang";
                case "An Hui Sheng": return "Anhui";
                case "Fu Jian Sheng": return "Fujian";
                case "Jiang Xi Sheng": return "Jiangxi";
                case "Shan Dong Sheng": return "Shandong";
                case "He Nan Sheng": return "Henan";
                case "Hu Bei Sheng": return "Hubei";
                case "Hu Nan Sheng": return "Hunan";
                case "Guang Xi Zhuang Zu Zi Zhi Qu": return "Guangxi";
                case "Hai Nan Sheng": return "Hainan";
                case "Si Chuan Sheng": return "Sichuan";
                case "Gui Zhou Sheng": return "Guizhou";
                case "Yun Nan Sheng": return "Yunnan";
                case "Xi Zang Zi Zhi Qu": return "Tibet";
                case "Gan Su Sheng": return "Gansu";
                case "Qing Hai Sheng": return "Qinghai";
                case "Ning Xia Hui Zu Zi Zhi Qu": return "Ningxia";
                case "Xin Jiang Wei Wu Er Zi Zhi Qu": return "Xinjiang";
                case "Tai Wan Sheng": return "Taiwan";
            }

            var suffixes = new[]
            {
                " Te Bie Xing Zheng Qu", " Zi Zhi Qu", " Zi Zhi Zhou", " Zi Zhi Xian",
                " Di Qu", " Jie Dao", " Sheng", " Shi", " Xian", " Qu", " Zhen", " Meng", " Qi"
            };
            foreach (var suffix in suffixes)
            {
                if (text.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) && text.Length > suffix.Length)
                {
                    text = text.Substring(0, text.Length - suffix.Length).Trim();
                    break;
                }
            }

            var words = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0) { return rawEn.Trim(); }
            var sb = new StringBuilder();
            for (var i = 0; i < words.Length; i++)
            {
                var w = words[i];
                if (w.Length == 1)
                {
                    sb.Append(w.ToUpperInvariant());
                }
                else
                {
                    sb.Append(Char.ToUpperInvariant(w[0]));
                    sb.Append(w.Substring(1).ToLowerInvariant());
                }
            }
            return sb.Length > 0 ? sb.ToString() : rawEn.Trim();
        }

        public static string FormatShortLocation(string lang, DistrictItem d)
        {
            if (d == null)
            {
                return IsEnglish(lang) ? "Longhua, Shenzhen" : "龙华区 · 深圳";
            }
            if (IsEnglish(lang))
            {
                var dName = FormatEnglishRegionName(d.En, d.Key);
                var cName = FormatEnglishRegionName(d.CityEn, d.CityKey);
                if (String.Equals(dName, cName, StringComparison.OrdinalIgnoreCase))
                {
                    return dName;
                }
                return dName + ", " + cName;
            }
            var cityShort = TrimZhCitySuffix(d.CityZh);
            if (String.Equals(d.Zh, d.CityZh, StringComparison.Ordinal) || String.IsNullOrWhiteSpace(cityShort))
            {
                return d.Zh;
            }
            return d.Zh + " · " + cityShort;
        }

        public static string FormatFullLocation(string lang, DistrictItem d)
        {
            if (d == null)
            {
                return IsEnglish(lang) ? "Guangdong - Shenzhen - Longhua" : "广东省 - 深圳市 - 龙华区";
            }
            if (IsEnglish(lang))
            {
                return FormatEnglishRegionName(d.ProvinceEn, d.ProvinceKey)
                    + " - " + FormatEnglishRegionName(d.CityEn, d.CityKey)
                    + " - " + FormatEnglishRegionName(d.En, d.Key);
            }
            return d.ProvinceZh + " - " + d.CityZh + " - " + d.Zh;
        }

        private static string TrimZhCitySuffix(string cityZh)
        {
            if (String.IsNullOrWhiteSpace(cityZh)) { return String.Empty; }
            if (cityZh.EndsWith("市", StringComparison.Ordinal) && cityZh.Length >= 3)
            {
                return cityZh.Substring(0, cityZh.Length - 1);
            }
            return cityZh;
        }

        public static bool ContainsCjk(string value)
        {
            if (String.IsNullOrEmpty(value)) { return false; }
            for (var i = 0; i < value.Length; i++)
            {
                var ch = value[i];
                if ((ch >= '\u3400' && ch <= '\u9FFF') || (ch >= '\uF900' && ch <= '\uFAFF'))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
