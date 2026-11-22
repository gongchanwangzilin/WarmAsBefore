using System;

namespace WarmAsBefore.Services;

/// <summary>
/// 地图的几何底子：经纬度、Web 墨卡托瓦片编号、画布坐标三者互转。
///
/// 全是纯函数（不碰 MAUI、不碰 IO），所以 <c>tools/map_selftest</c> 能把这份文件直接链过去对拍。
///
/// 三套坐标各管一段：
/// - <b>经纬度</b>：存档里的真实位置（WGS-84）。
/// - <b>画布坐标</b>：地图页那张无限画布的世界坐标，屏幕 = 世界 × Scale + Offset。
/// - <b>瓦片 (z/x/y)</b>：底图切图，Web 墨卡托。
///
/// 画布与经纬度之间不是「一度 = 多少米」的线性换算，而是以锚点为原点的等角墨卡托等比映射。
/// 城市尺度下它与 <see cref="MapImportService"/> 导入时用的线性换算只差不到一米；
/// 但拉到省级视野时线性换算会和瓦片越拉越歪，墨卡托不会。
/// </summary>
public static class MapProjection
{
    /// <summary>WGS-84 长半轴（Web 墨卡托把地球当正球用，就是这个半径）。</summary>
    public const double EarthRadius = 6378137.0;

    /// <summary>一块瓦片的边长（像素）。XYZ 瓦片全是 256。</summary>
    public const int TileSize = 256;

    /// <summary>与 <see cref="MapImportService"/> 同一常数：画布 1 单位 = 多少米，导入那套就用它。</summary>
    public const double MetersPerDegreeLat = 111320.0;

    /// <summary>缩放档下限：整块大陆。再往外就没有「城市地图」的意义了。</summary>
    public const int MinZoom = 3;

    /// <summary>缩放档上限：高德的 z19 已经是建筑轮廓级。</summary>
    public const int MaxZoom = 19;

    /// <summary>赤道、0 级、一块 256 像素瓦片的每像素地面米数（≈156543.034）。</summary>
    public const double EquatorMetersPerPixelZ0 = 2 * Math.PI * EarthRadius / TileSize;

    private const double Deg2Rad = Math.PI / 180.0;
    private const double Rad2Deg = 180.0 / Math.PI;

    // ==================== 米 / 度 ====================

    /// <summary>该纬度上一个经度有多少米。越往两极越小，所以「一度 = 111 公里」只在赤道成立。</summary>
    public static double MetersPerDegreeLon(double lat) =>
        Math.Max(1, MetersPerDegreeLat * Math.Cos(ClampLat(lat) * Deg2Rad));

    /// <summary>
    /// 该纬度、该缩放档下，屏幕一个像素是多少地面米。
    /// 80° 纬度处的 cos 只剩 0.17，赤道换算会高估近六倍 —— 比例尺必须走这里。
    /// </summary>
    public static double GroundMetersPerPixel(double zoom, double lat) =>
        EquatorMetersPerPixelZ0 * Math.Cos(ClampLat(lat) * Deg2Rad) / Math.Pow(2, zoom);

    /// <summary>反过来：给定「每像素多少地面米」，落在哪个缩放档（可带小数）。</summary>
    public static double ZoomForGroundMetersPerPixel(double metersPerPixel, double lat)
    {
        var mpp = Math.Max(1e-9, metersPerPixel);
        return Math.Log(EquatorMetersPerPixelZ0 * Math.Cos(ClampLat(lat) * Deg2Rad) / mpp, 2);
    }

    // ==================== Web 墨卡托 ====================

    /// <summary>经度 → 墨卡托归一化横坐标 [0,1]（-180°→0，180°→1）。</summary>
    public static double NormalizedX(double lon) => (ClampLon(lon) + 180.0) / 360.0;

    /// <summary>纬度 → 墨卡托归一化纵坐标 [0,1]（北 85.05°→0，南 →1）。</summary>
    public static double NormalizedY(double lat)
    {
        var sin = Math.Sin(ClampLat(lat) * Deg2Rad);
        return 0.5 - Math.Log((1 + sin) / (1 - sin)) / (4 * Math.PI);
    }

    public static double LonFromNormalizedX(double x) => x * 360.0 - 180.0;

    public static double LatFromNormalizedY(double y) =>
        Math.Atan(Math.Sinh(Math.PI * (1 - 2 * y))) * Rad2Deg;

    /// <summary>经度落在第几个瓦片（可带小数，乘上 2^z 之后）。</summary>
    public static double TileX(double lon, double zoom) => NormalizedX(lon) * Math.Pow(2, zoom);

    /// <summary>纬度落在第几个瓦片（可带小数）。</summary>
    public static double TileY(double lat, double zoom) => NormalizedY(lat) * Math.Pow(2, zoom);

    /// <summary>瓦片四至：北 / 西 / 南 / 东。</summary>
    public static (double North, double West, double South, double East) TileBounds(int z, int x, int y)
    {
        var n = Math.Pow(2, z);
        return (
            LatFromNormalizedY(y / n),
            LonFromNormalizedX(x / n),
            LatFromNormalizedY((y + 1) / n),
            LonFromNormalizedX((x + 1) / n));
    }

    /// <summary>瓦片中心：长按/取景要「这一块的正中」，用它。</summary>
    public static (double Lat, double Lon) TileCenter(int z, int x, int y) =>
        (LatFromNormalizedY((y + 0.5) / Math.Pow(2, z)),
         LonFromNormalizedX((x + 0.5) / Math.Pow(2, z)));

    /// <summary>该档位下瓦片总边长（一块 = 2^z × 2^z），越界钳位用。</summary>
    public static int TileCount(int z) => 1 << Math.Clamp(z, 0, 22);

    // ==================== 画布 ↔ 经纬度 ====================

    /// <summary>墨卡托横坐标（米）。</summary>
    private static double MercatorX(double lon) => EarthRadius * ClampLon(lon) * Deg2Rad;

    /// <summary>墨卡托纵坐标（米，北为正）。</summary>
    private static double MercatorY(double lat) =>
        EarthRadius * Math.Log(Math.Tan(Math.PI / 4 + ClampLat(lat) * Deg2Rad / 2));

    /// <summary>
    /// 画布 1 单位对应的墨卡托米数：锚点上 1 单位正好是 <paramref name="metersPerUnit"/> 地面米。
    /// 越往极点墨卡托膨胀越大，所以按锚点纬度折一次 cos。
    /// </summary>
    private static double MercatorMetersPerUnit(double anchorLat, double metersPerUnit) =>
        Math.Max(1e-6, metersPerUnit) / Math.Cos(ClampLat(anchorLat) * Deg2Rad);

    /// <summary>经纬度 → 画布世界坐标（y 向下 = 往南）。</summary>
    public static (double X, double Y) GeoToCanvas(
        double lat, double lon, double anchorLat, double anchorLon, double metersPerUnit)
    {
        var k = 1.0 / MercatorMetersPerUnit(anchorLat, metersPerUnit);
        return (
            (MercatorX(lon) - MercatorX(anchorLon)) * k,
            -(MercatorY(lat) - MercatorY(anchorLat)) * k);
    }

    /// <summary>画布世界坐标 → 经纬度。</summary>
    public static (double Lat, double Lon) CanvasToGeo(
        double x, double y, double anchorLat, double anchorLon, double metersPerUnit)
    {
        var mpu = MercatorMetersPerUnit(anchorLat, metersPerUnit);
        var mx = MercatorX(anchorLon) + x * mpu;
        var my = MercatorY(anchorLat) - y * mpu;
        var lat = Math.Atan(Math.Sinh(my / EarthRadius)) * Rad2Deg;
        return (lat, mx / EarthRadius * Rad2Deg);
    }

    // ==================== 画布缩放 ↔ 瓦片缩放档 ====================

    /// <summary>
    /// 画布 Scale（屏幕像素 / 画布单位）落到哪个瓦片缩放档（可带小数）。
    /// 锚点处一单位 = metersPerUnit 米，所以屏幕一像素是 metersPerUnit / Scale 米。
    /// </summary>
    public static double ZoomForCanvasScale(double scale, double metersPerUnit, double anchorLat)
    {
        var perPixel = Math.Max(1e-9, metersPerUnit) / Math.Max(1e-9, scale);
        return ZoomForGroundMetersPerPixel(perPixel, anchorLat);
    }

    /// <summary>反过来：某个瓦片缩放档对应多大的画布 Scale。</summary>
    public static double CanvasScaleForZoom(double zoom, double metersPerUnit, double anchorLat) =>
        Math.Pow(2, zoom) * Math.Max(1e-9, metersPerUnit)
        / (EquatorMetersPerPixelZ0 * Math.Cos(ClampLat(anchorLat) * Deg2Rad));

    /// <summary>底图在用的缩放范围：档 3–19 折成画布 Scale 的上下限。</summary>
    public static (double Min, double Max) ScaleRangeForZooms(
        double metersPerUnit, double anchorLat, int minZoom = MinZoom, int maxZoom = MaxZoom)
    {
        var lo = CanvasScaleForZoom(minZoom, metersPerUnit, anchorLat);
        var hi = CanvasScaleForZoom(maxZoom, metersPerUnit, anchorLat);
        return (Math.Max(1e-7, lo), Math.Max(lo * 1.0001, hi));
    }

    // ==================== 小工具 ====================

    /// <summary>墨卡托在两极会发散，画面只做到 85°，多出来的纬度按边界算。</summary>
    private static double ClampLat(double lat) => Math.Clamp(lat, -85.05112878, 85.05112878);

    private static double ClampLon(double lon) => Math.Clamp(lon, -180.0, 180.0);
}
