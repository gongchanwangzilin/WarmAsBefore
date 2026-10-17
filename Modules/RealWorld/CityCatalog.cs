namespace WarmAsBefore.Modules.RealWorld;

/// <summary>一个可选城市：名字只给人看，天气始终按坐标取。</summary>
public sealed record CityHit
{
    public required string Name { get; init; }
    public string Admin { get; init; } = "";
    public double Latitude { get; init; }
    public double Longitude { get; init; }

    public string Display => string.IsNullOrWhiteSpace(Admin) ? Name : $"{Name}（{Admin}）";
    public string CoordText => $"{Latitude.ToString("0.00")}, {Longitude.ToString("0.00")}";
}

/// <summary>
/// 内置城市表。两个用途：设置页离线也能直接点选热门城市（不依赖网络）；
/// 拿到坐标后（设备定位没有城市名）按最近城市回一个名字。
/// 坐标精度到小数点后 4 位足够——天气本来就按坐标查，不在城市边界上较真。
/// </summary>
public static class CityCatalog
{
    public static readonly IReadOnlyList<CityHit> Popular = new List<CityHit>
    {
        C("北京", "华北", 39.9042, 116.4074), C("上海", "华东", 31.2304, 121.4737),
        C("广州", "华南", 23.1291, 113.2644), C("深圳", "华南", 22.5431, 114.0579),
        C("杭州", "华东", 30.2741, 120.1551), C("南京", "华东", 32.0603, 118.7969),
        C("苏州", "华东", 31.2989, 120.5853), C("无锡", "华东", 31.5720, 120.3010),
        C("宁波", "华东", 29.8746, 121.5503), C("温州", "华东", 27.9942, 120.6994),
        C("合肥", "华东", 31.8612, 117.2830), C("福州", "华东", 26.0745, 119.2965),
        C("厦门", "华东", 24.4798, 118.0894), C("南昌", "华中", 28.6820, 115.8582),
        C("武汉", "华中", 30.5931, 114.3055), C("长沙", "华中", 28.2282, 112.9388),
        C("郑州", "华中", 34.7466, 113.6254), C("洛阳", "华中", 34.6197, 112.4541),
        C("成都", "西南", 30.5723, 104.0665), C("重庆", "西南", 29.5630, 106.5516),
        C("昆明", "西南", 24.8801, 102.8329), C("贵阳", "西南", 26.6470, 106.6302),
        C("西安", "西北", 34.3416, 108.9402), C("兰州", "西北", 36.0611, 103.8343),
        C("西宁", "西北", 36.6171, 101.7782), C("银川", "西北", 38.4865, 106.2325),
        C("乌鲁木齐", "西北", 43.8256, 87.6168), C("拉萨", "西南", 29.6600, 91.1322),
        C("南宁", "华南", 22.8170, 108.3665), C("海口", "华南", 20.0444, 110.1999),
        C("三亚", "华南", 18.2528, 109.5119), C("佛山", "华南", 23.0218, 113.1219),
        C("东莞", "华南", 23.0418, 113.7463), C("珠海", "华南", 22.2707, 113.5767),
        C("天津", "华北", 39.0842, 117.2010), C("石家庄", "华北", 38.0428, 114.5149),
        C("唐山", "华北", 39.6305, 118.1802), C("太原", "华北", 37.8706, 112.5489),
        C("呼和浩特", "华北", 40.8414, 111.7519), C("沈阳", "东北", 41.8057, 123.4315),
        C("大连", "东北", 38.9140, 121.6186), C("长春", "东北", 43.8170, 125.3235),
        C("哈尔滨", "东北", 45.8038, 126.5350), C("青岛", "华东", 36.0671, 120.3826),
        C("烟台", "华东", 37.4638, 121.3914), C("济南", "华东", 36.6758, 117.0009),
        C("台北", "中国台湾", 25.0330, 121.5654), C("香港", "中国香港", 22.3193, 114.1734),
        C("澳门", "中国澳门", 22.1987, 113.5431), C("东京", "日本", 35.6895, 139.6917),
        C("大阪", "日本", 34.6937, 135.5023), C("首尔", "韩国", 37.5665, 126.9780),
        C("新加坡", "新加坡", 1.3521, 103.8198), C("曼谷", "泰国", 13.7563, 100.5018),
        C("吉隆坡", "马来西亚", 3.1390, 101.6869), C("悉尼", "澳大利亚", -33.8688, 151.2093),
        C("墨尔本", "澳大利亚", -37.8136, 144.9631), C("温哥华", "加拿大", 49.2827, -123.1207),
        C("多伦多", "加拿大", 43.6532, -79.3832), C("洛杉矶", "美国", 34.0522, -118.2437),
        C("旧金山", "美国", 37.7749, -122.4194), C("纽约", "美国", 40.7128, -74.0060),
        C("伦敦", "英国", 51.5072, -0.1276), C("巴黎", "法国", 48.8566, 2.3522),
        C("柏林", "德国", 52.5200, 13.4050), C("莫斯科", "俄罗斯", 55.7558, 37.6173),
        C("迪拜", "阿联酋", 25.2048, 55.2708),
    };

    private static CityHit C(string name, string admin, double lat, double lon) =>
        new() { Name = name, Admin = admin, Latitude = lat, Longitude = lon };

    /// <summary>本地关键词匹配：断网或接口没返回时，界面仍有一个能用的候选列表。</summary>
    public static List<CityHit> Match(string keyword, int max = 8)
    {
        var k = (keyword ?? "").Trim();
        if (k.Length == 0) return new List<CityHit>();
        return Popular.Where(c => c.Name.Contains(k, StringComparison.OrdinalIgnoreCase)
                || k.Contains(c.Name, StringComparison.OrdinalIgnoreCase)
                || c.Admin.Contains(k, StringComparison.OrdinalIgnoreCase))
            .Take(max).ToList();
    }

    /// <summary>坐标 → 表内最近城市。设备定位只给经纬度，名字靠它回推。</summary>
    public static CityHit Nearest(double lat, double lon)
    {
        var best = Popular[0];
        var min = double.MaxValue;
        foreach (var c in Popular)
        {
            var d = DistanceKm(lat, lon, c.Latitude, c.Longitude);
            if (d < min) { min = d; best = c; }
        }
        return best;
    }

    private static double DistanceKm(double a, double b, double c, double d)
    {
        const double R = 6371;
        var dLat = (c - a) * Math.PI / 180;
        var dLon = (d - b) * Math.PI / 180;
        var h = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
            + Math.Cos(a * Math.PI / 180) * Math.Cos(c * Math.PI / 180) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return 2 * R * Math.Asin(Math.Min(1, Math.Sqrt(h)));
    }
}
