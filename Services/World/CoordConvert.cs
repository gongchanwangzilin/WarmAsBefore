using System;

namespace WarmAsBefore.Services;

/// <summary>
/// 国内坐标系互转：高德 / 腾讯用 GCJ-02（俗称「火星坐标」），谷歌 / 开放街道图用 WGS84。
/// 两套坐标在国内能差出几百米 —— 把高德抓来的 GCJ-02 直接喂给谷歌的地名检索，
/// 经常搜到隔壁小区；取照片时先转换一次，命中的才是同一个地方。
///
/// 算法用通行的「克拉索夫斯基椭球 + 偏移多项式」：误差在米级，对找地点照片、
/// 街景这种「差几米无所谓」的用途完全够，不必引第三方库。
/// 境外坐标不加密，原样返回。
/// </summary>
public static class CoordConvert
{
    /// <summary>克拉索夫斯基椭球长半轴（米）。这是 GCJ-02 偏移公式约定的基准椭球。</summary>
    private const double A = 6378245.0;

    /// <summary>第一偏心率平方。</summary>
    private const double Ee = 0.00669342162296594323;

    /// <summary>GCJ-02（高德）→ WGS84（谷歌 / GPS）。</summary>
    public static (double Lat, double Lon) Gcj02ToWgs84(double lat, double lon)
    {
        if (OutOfChina(lat, lon)) return (lat, lon);
        var (dLat, dLon) = Delta(lat, lon);
        return (lat - dLat, lon - dLon);
    }

    /// <summary>WGS84（谷歌 / GPS）→ GCJ-02（高德）。</summary>
    public static (double Lat, double Lon) Wgs84ToGcj02(double lat, double lon)
    {
        if (OutOfChina(lat, lon)) return (lat, lon);
        var (dLat, dLon) = Delta(lat, lon);
        return (lat + dLat, lon + dLon);
    }

    /// <summary>
    /// 中国大陆范围之外不加偏：港澳台与境外都用 WGS84，给它们套一次偏移反而会错开。
    /// 判据沿用常见实现的一圈粗略矩形，边缘误差可以接受（边界附近本来也是加密/未加密混用）。
    /// </summary>
    private static bool OutOfChina(double lat, double lon) =>
        lon < 72.004 || lon > 137.8347 || lat < 0.8293 || lat > 55.8271;

    /// <summary>在给定坐标处算一次 GCJ-02 的偏移量（纬度差、经度差，单位：度）。</summary>
    private static (double DLat, double DLon) Delta(double lat, double lon)
    {
        var dLat = TransformLat(lon - 105.0, lat - 35.0);
        var dLon = TransformLon(lon - 105.0, lat - 35.0);

        var radLat = lat / 180.0 * Math.PI;
        var magic = Math.Sin(radLat);
        magic = 1 - Ee * magic * magic;
        var sqrtMagic = Math.Sqrt(magic);

        // 偏移多项式算出来的是「米」，这里按椭球曲率换算成「度」。
        dLat = dLat * 180.0 / (A * (1 - Ee) / (magic * sqrtMagic) * Math.PI);
        dLon = dLon * 180.0 / (A / sqrtMagic * Math.Cos(radLat) * Math.PI);
        return (dLat, dLon);
    }

    private static double TransformLat(double x, double y) =>
        -100.0 + 2.0 * x + 3.0 * y + 0.2 * y * y + 0.1 * x * y + 0.2 * Math.Sqrt(Math.Abs(x))
        + (20.0 * Math.Sin(6.0 * x * Math.PI) + 20.0 * Math.Sin(2.0 * x * Math.PI)) * 2.0 / 3.0
        + (20.0 * Math.Sin(y * Math.PI) + 40.0 * Math.Sin(y / 3.0 * Math.PI)) * 2.0 / 3.0
        + (160.0 * Math.Sin(y / 12.0 * Math.PI) + 320 * Math.Sin(y * Math.PI / 30.0)) * 2.0 / 3.0;

    private static double TransformLon(double x, double y) =>
        300.0 + x + 2.0 * y + 0.1 * x * x + 0.1 * x * y + 0.1 * Math.Sqrt(Math.Abs(x))
        + (20.0 * Math.Sin(6.0 * x * Math.PI) + 20.0 * Math.Sin(2.0 * x * Math.PI)) * 2.0 / 3.0
        + (20.0 * Math.Sin(x * Math.PI) + 40.0 * Math.Sin(x / 3.0 * Math.PI)) * 2.0 / 3.0
        + (150.0 * Math.Sin(x / 12.0 * Math.PI) + 300.0 * Math.Sin(x / 30.0 * Math.PI)) * 2.0 / 3.0;
}
