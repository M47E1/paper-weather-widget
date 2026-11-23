using System;
using System.Collections.Generic;

namespace PaperWeatherWidget.Models
{
    public sealed class RegionNode
    {
        public string Key { get; set; }
        public string Zh { get; set; }
        public string En { get; set; }
        public double Lat { get; set; }
        public double Lon { get; set; }
    }

    public sealed class DistrictItem
    {
        public string Key { get; set; }
        public string Zh { get; set; }
        public string En { get; set; }
        public double Lat { get; set; }
        public double Lon { get; set; }
        public string CityKey { get; set; }
        public string CityZh { get; set; }
        public string CityEn { get; set; }
        public string ProvinceKey { get; set; }
        public string ProvinceZh { get; set; }
        public string ProvinceEn { get; set; }

        public string LocationKey
        {
            get { return ProvinceKey + "|" + CityKey + "|" + Key; }
        }
    }

    public sealed class CityItem
    {
        public string Key { get; set; }
        public string Zh { get; set; }
        public string En { get; set; }
        public double Lat { get; set; }
        public double Lon { get; set; }
        public string ProvinceKey { get; set; }
        public List<DistrictItem> Districts { get; private set; }

        public CityItem()
        {
            Districts = new List<DistrictItem>();
        }
    }

    public sealed class ProvinceItem
    {
        public string Key { get; set; }
        public string Zh { get; set; }
        public string En { get; set; }
        public double Lat { get; set; }
        public double Lon { get; set; }
        public List<CityItem> Cities { get; private set; }

        public ProvinceItem()
        {
            Cities = new List<CityItem>();
        }
    }
}
