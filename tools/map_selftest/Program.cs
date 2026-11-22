using System.Globalization;
using WarmAsBefore.Services;

// 地图几何的单元自测：把投影、瓦片编号、GCJ-02 偏移这些纯函数按住对一遍。
// 跑法：dotnet run --project tools/map_selftest
// 不启动应用、不联网，纯粹算数。

var fail = 0;
var pass = 0;

void Check(string name, bool ok, string detail = "")
{
    if (ok)
    {
        pass++;
        Console.WriteLine($"  ok   {name}{(detail.Length > 0 ? "  " + detail : "")}");
    }
    else
    {
        fail++;
        Console.WriteLine($"  FAIL {name}  {detail}");
    }
}

void Near(string name, double actual, double expected, double tol, string unit = "")
{
    var ok = Math.Abs(actual - expected) <= tol;
    Check(name, ok, $"实际 {actual.ToString("0.########", CultureInfo.InvariantCulture)}{unit} / 期望 {expected.ToString("0.########", CultureInfo.InvariantCulture)}{unit}");
}

Console.WriteLine("== 1. XYZ 瓦片编号（对照 python / 通行算法） ==");
{
    var (x, y) = ((int)Math.Floor(MapProjection.TileX(121.4737, 10)), (int)Math.Floor(MapProjection.TileY(31.2304, 10)));
    Check("上海 z10 = 857,418", x == 857 && y == 418, $"得到 {x},{y}");
    (x, y) = ((int)Math.Floor(MapProjection.TileX(116.4074, 10)), (int)Math.Floor(MapProjection.TileY(39.9042, 10)));
    Check("北京 z10 = 843,388", x == 843 && y == 388, $"得到 {x},{y}");
    (x, y) = ((int)Math.Floor(MapProjection.TileX(121.4737, 3)), (int)Math.Floor(MapProjection.TileY(31.2304, 3)));
    Check("上海 z3 = 6,3", x == 6 && y == 3, $"得到 {x},{y}");
    (x, y) = ((int)Math.Floor(MapProjection.TileX(121.4737, 19)), (int)Math.Floor(MapProjection.TileY(31.2304, 19)));
    Check("上海 z19 = 439052,214226", x == 439052 && y == 214226, $"得到 {x},{y}");
    Check("z3 共 8×8 块", MapProjection.TileCount(3) == 8);
    Check("z19 共 524288×524288 块", MapProjection.TileCount(19) == 524288);
}

Console.WriteLine("== 2. 瓦片四至与中心 ==");
{
    var b = MapProjection.TileBounds(10, 857, 418);
    var c = MapProjection.TileCenter(10, 857, 418);
    Check("上海落在 857/418 的四至之内",
        c.Lat < b.North && c.Lat > b.South && c.Lon > b.West && c.Lon < b.East);
    // 四至的角点应当正好回到整数瓦片边界
    Near("四至西边 = x/2^z 归一化", MapProjection.NormalizedX(b.West), 857 / 1024.0, 1e-9);
    Near("四至北边 = y/2^z 归一化", MapProjection.NormalizedY(b.North), 418 / 1024.0, 1e-9);
    var roundTrip = MapProjection.LatFromNormalizedY(MapProjection.NormalizedY(31.2304));
    Near("纬度归一化往返", roundTrip, 31.2304, 1e-9, "°");
}

Console.WriteLine("== 3. 画布 ↔ 经纬度（墨卡托等比） ==");
{
    foreach (var (lat, lon) in new[] { (31.2304, 121.4737), (39.9042, 116.4074), (80.0, 20.0), (-33.8688, 151.2093) })
    {
        var (cx, cy) = MapProjection.GeoToCanvas(lat, lon, 31.2304, 121.4737, 0.5);
        var (blat, blon) = MapProjection.CanvasToGeo(cx, cy, 31.2304, 121.4737, 0.5);
        Near($"往返纬度 ({lat}°)", blat, lat, 1e-9, "°");
        Near($"往返经度 ({lon}°)", blon, lon, 1e-9, "°");
    }

    // 锚点处 1 画布单位 = MetersPerUnit 米：走 1000 个单位，换算回来应是 1000×0.5 米
    var (lat0, lon0) = (31.2304, 121.4737);
    var (ax, _) = MapProjection.GeoToCanvas(lat0, lon0, lat0, lon0, 0.5);
    var (bx, _) = MapProjection.GeoToCanvas(lat0, lon0 + 0.0001, lat0, lon0, 0.5);
    var metersPerUnit = 0.5;
    var expectedUnits = 0.0001 * MapProjection.MetersPerDegreeLon(lat0) / metersPerUnit;
    Near("锚点处经度方向的比例尺", bx - ax, expectedUnits, expectedUnits * 2e-3);

    // 与导入器（线性换算）在城市范围内的偏差：1 公里处应当小于 1 米
    var (mx, my) = MapProjection.GeoToCanvas(lat0, lon0 + 0.01, lat0, lon0, 0.5);   // 约 950 米
    var linearMeters = 0.01 * MapProjection.MetersPerDegreeLon(lat0);
    var mercatorMeters = Math.Abs(mx) * metersPerUnit;
    Near("1 公里内与线性换算的差", mercatorMeters - linearMeters, 0, 1.0, " 米");
    // 差异来自两套常数：墨卡托一度 = 111319.49 米，导入器用 111320 米 —— 城市尺度下互相看不见。
    Check("与线性换算同量级（不是两套坐标系）", Math.Abs(mercatorMeters - linearMeters) < 1.0);
}

Console.WriteLine("== 4. 画布 Scale ↔ 瓦片缩放档 ==");
{
    foreach (var lat in new[] { 0.0, 31.2304, 60.0, 80.0 })
    {
        foreach (var z in new[] { 3.0, 10.5, 14.0, 19.0 })
        {
            var scale = MapProjection.CanvasScaleForZoom(z, 0.5, lat);
            var back = MapProjection.ZoomForCanvasScale(scale, 0.5, lat);
            Near($"缩放档往返 (lat {lat}, z {z})", back, z, 1e-9);
        }
    }

    var (min, max) = MapProjection.ScaleRangeForZooms(0.5, 31.2304, 3, 19);
    Check("档位范围 z3–z19 单调", min < max && min > 0);
    Near("z19 上限对应的 Scale", Math.Log(max / MapProjection.CanvasScaleForZoom(19, 0.5, 31.2304), 2), 0, 1e-9);
}

Console.WriteLine("== 5. 每像素地面米数（比例尺必须跟着纬度） ==");
{
    Near("赤道 z0 = 156543.034", MapProjection.GroundMetersPerPixel(0, 0), 156543.03392804097, 1e-6, " 米/像素");
    Near("z19@31.2304°", MapProjection.GroundMetersPerPixel(19, 31.2304), 0.255314, 1e-5, " 米/像素");
    var equator = MapProjection.GroundMetersPerPixel(19, 0);
    var high = MapProjection.GroundMetersPerPixel(19, 80);
    Near("80° 处相对赤道的比值 = cos(80°)", high / equator, Math.Cos(80 * Math.PI / 180), 1e-9);
    Check("80° 处明显小于赤道（旧算法会差近六倍）", high < equator / 5, $"赤道 {equator:0.####} vs 80° {high:0.####}");
    Near("mpp → zoom 往返", MapProjection.ZoomForGroundMetersPerPixel(MapProjection.GroundMetersPerPixel(12.5, 45), 45), 12.5, 1e-9);
}

Console.WriteLine("== 6. GCJ-02 偏移（高德底图必须偏回来） ==");
{
    var (glat, glon) = CoordConvert.Wgs84ToGcj02(31.2304, 121.4737);
    var dLat = (glat - 31.2304) * MapProjection.MetersPerDegreeLat;
    var dLon = (glon - 121.4737) * MapProjection.MetersPerDegreeLon(31.2304);
    var distance = Math.Sqrt(dLat * dLat + dLon * dLon);
    Check("上海市区偏差在 100–1000 米", distance is > 100 and < 1000, $"{distance:0} 米");

    var (wlat, wlon) = CoordConvert.Gcj02ToWgs84(glat, glon);
    Near("GCJ → WGS 往返纬度（迭代反解）", wlat, 31.2304, 1e-9, "°");
    Near("GCJ → WGS 往返经度（迭代反解）", wlon, 121.4737, 1e-9, "°");

    var (olat, olon) = CoordConvert.Wgs84ToGcj02(48.8566, 2.3522);
    Check("境外不加偏（巴黎原样）", Math.Abs(olat - 48.8566) < 1e-12 && Math.Abs(olon - 2.3522) < 1e-12);

    // 落图时把高德瓦片的四至偏回来：偏移后的瓦片矩形不能比原瓦片大出一个身位
    var before = MapProjection.GeoToCanvas(31.2304, 121.4737, 31.2304, 121.4737, 0.5);
    var (plat, plon) = CoordConvert.Gcj02ToWgs84(glat, glon);
    var after = MapProjection.GeoToCanvas(plat, plon, 31.2304, 121.4737, 0.5);
    // 偏回来的点应当落在原点附近：0.001 画布单位 = 0.5 毫米，肉眼与像素都到不了。
    Near("偏回原点的画布位置", Math.Abs(after.X - before.X) + Math.Abs(after.Y - before.Y), 0, 1e-3);
}

Console.WriteLine("== 7. 档位边界 ==");
{
    Check("档位下限 3 / 上限 19", MapProjection.MinZoom == 3 && MapProjection.MaxZoom == 19);
    var z = MapProjection.ZoomForCanvasScale(MapProjection.CanvasScaleForZoom(25, 0.5, 31.2304), 0.5, 31.2304);
    Check("超出范围的档位能被认出来并钳位", z > 19);
    Near("钳位后仍是 19", Math.Clamp(z, MapProjection.MinZoom, MapProjection.MaxZoom), 19, 0);
}

Console.WriteLine("== 8. MBTiles：TMS tile_row → XYZ y ==");
{
    Check("z0 只有一块，怎么翻都是 0", MbTilesInfo.FlipRow(0, 0) == 0);
    Check("z3 最南一行 row0 → 最北 y7", MbTilesInfo.FlipRow(0, 3) == 7, $"得到 {MbTilesInfo.FlipRow(0, 3)}");
    Check("z10 row418 → y605", MbTilesInfo.FlipRow(418, 10) == 605, $"得到 {MbTilesInfo.FlipRow(418, 10)}");
    Check("翻两次回到原值", MbTilesInfo.FlipRow(MbTilesInfo.FlipRow(12345, 15), 15) == 12345);
    Check("z19 顶行 → 底行", MbTilesInfo.FlipRow(0, 19) == 524287);
    Check("TMS 写库：y605 → row418", MbTilesInfo.RowForY(605, 10, xyzScheme: false) == 418);
    Check("XYZ 包不翻：y605 → row605", MbTilesInfo.RowForY(605, 10, xyzScheme: true) == 605);
    Check("读出来的 row418 → y605", MbTilesInfo.YForRow(418, 10, xyzScheme: false) == 605);
    Check("越界 row 被拒", !MbTilesInfo.TryYForRow(1024, 10, xyzScheme: false, out _));
    // 与地图几何对拍：上海 z10 的 XYZ y 是 418，那么它在 MBTiles 里的 row 就是 605。
    var y = (int)Math.Floor(MapProjection.TileY(31.2304, 10));
    Check("上海 z10 y418 ↔ row605", y == 418 && MbTilesInfo.RowForY(y, 10, false) == 605);
}

Console.WriteLine("== 9. MBTiles metadata 解析 ==");
{
    var raw = new Dictionary<string, string?>
    {
        ["name"] = "上海离线包",
        ["format"] = "jpg",
        ["minzoom"] = "8",
        ["maxzoom"] = "16",
        ["bounds"] = "121.0,30.8,121.9,31.5"
    };
    var m = MbTilesInfo.ParseMetadata(raw, "fallback", "view");
    Check("name 取自 metadata", m.Name == "上海离线包", m.Name);
    Check("jpg 归一成 jpeg", m.Format == "jpeg", m.Format);
    Check("档位 8–16", m.MinZoom == 8 && m.MaxZoom == 16);
    Check("bounds 四值", m.HasBounds && m.West == 121.0 && m.South == 30.8 && m.East == 121.9 && m.North == 31.5);
    Check("默认 TMS", !m.XyzScheme);
    Check("视图结构被记下", m.Layout == "view", m.Layout);
    Check("覆盖判断", m.Covers(10) && !m.Covers(7) && !m.Covers(17));
    Check("摘要带档位与结构", MbTilesInfo.Describe(m).Contains("z8–16") && MbTilesInfo.Describe(m).Contains("视图结构"));

    var bare = MbTilesInfo.ParseMetadata(null, "无名包");
    Check("缺 metadata：名字用文件名", bare.Name == "无名包", bare.Name);
    Check("缺 metadata：默认 png / z0–22", bare.Format == "png" && bare.MinZoom == 0 && bare.MaxZoom == 22);
    Check("缺 bounds 不算有范围", !bare.HasBounds);

    var swapped = MbTilesInfo.ParseMetadata(new Dictionary<string, string?>
        { ["minzoom"] = "16", ["maxzoom"] = "8" }, null);
    Check("上下限写反了自动换回来", swapped.MinZoom == 8 && swapped.MaxZoom == 16);

    var bad = MbTilesInfo.ParseMetadata(new Dictionary<string, string?>
        { ["bounds"] = "121.9,30.8,121.0,31.5" }, null);
    Check("bounds 顺序反了当坏数据", !bad.HasBounds);
    Check("bounds 少一个数当坏数据", MbTilesInfo.ParseBounds("1,2,3") is null);
    Check("bounds 超纬度当坏数据", MbTilesInfo.ParseBounds("121,30.8,121.9,90") is null);

    var xyz = MbTilesInfo.ParseMetadata(new Dictionary<string, string?> { ["scheme"] = "XYZ" }, null);
    Check("scheme=xyz 记成 XYZ", xyz.XyzScheme);
    Check("format 大小写归一", MbTilesInfo.NormalizeFormat("JPEG") == "jpeg" && MbTilesInfo.NormalizeFormat("WebP") == "webp");
    Check("认不出的 format 原样小写", MbTilesInfo.NormalizeFormat("AVIF") == "avif");
}

Console.WriteLine("== 10. MBTiles 路径校验（纯函数） ==");
{
    var empty = MbTilesInfo.CheckPath("", exists: false);
    Check("空路径 = 没选", empty.State == MbTilesPathState.NotSet);
    Check("空路径给的是可操作的提示", empty.Message.Contains("选择"));

    var gone = MbTilesInfo.CheckPath(@"C:\maps\shanghai.mbtiles", exists: false);
    Check("文件不在 → Missing", gone.State == MbTilesPathState.Missing);
    Check("Missing 的提示带文件名", gone.Message.Contains("shanghai.mbtiles"));

    var wrong = MbTilesInfo.CheckPath(@"C:\maps\shanghai.sqlite", exists: true);
    Check("扩展名不对 → WrongExtension", wrong.State == MbTilesPathState.WrongExtension);

    var ok = MbTilesInfo.CheckPath(@"C:\maps\shanghai.MBTILES", exists: true);
    Check("存在且扩展名对（大小写不敏感）→ Ok", ok.State == MbTilesPathState.Ok);
    Check("Ok 的提示带文件名", ok.Message.Contains("shanghai.MBTILES"));

    var cs = MbTilesInfo.ReadOnlyConnectionString(@"C:\maps\it's.mbtiles");
    Check("连接串只读", cs.Contains("Mode=ReadOnly"));
    Check("连接串不池化（文件能换）", cs.Contains("Pooling=False"));
    Check("连接串转义单引号", cs.Contains("it''s.mbtiles"), cs);
}

Console.WriteLine("== 11. 瓦片格式按魔数判断 ==");
{
    byte[] png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00 };
    byte[] jpeg = { 0xFF, 0xD8, 0xFF, 0xE0, 0x00 };
    byte[] webp = { (byte)'R', (byte)'I', (byte)'F', (byte)'F', 0, 0, 0, 0, (byte)'W', (byte)'E', (byte)'B', (byte)'P' };
    Check("PNG 魔数", MbTilesInfo.SniffFormat(png) == "png");
    Check("JPEG 魔数", MbTilesInfo.SniffFormat(jpeg) == "jpeg");
    Check("WEBP 魔数", MbTilesInfo.SniffFormat(webp) == "webp");
    Check("短字节不认", MbTilesInfo.SniffFormat(new byte[] { 1, 2 }) == "");
    Check("非图片不认", MbTilesInfo.SniffFormat(System.Text.Encoding.UTF8.GetBytes("not an image")) == "");
}

Console.WriteLine("== 12. MBTiles 表结构：table 与 view ==");
{
    Check("tiles 是 view", MbTilesInfo.IsViewLayout("view"));
    Check("tiles 是 view（大小写）", MbTilesInfo.IsViewLayout("VIEW"));
    Check("tiles 是 table", !MbTilesInfo.IsViewLayout("table"));
    Check("查不到类型", !MbTilesInfo.IsViewLayout(null));
    var viewMeta = MbTilesInfo.ParseMetadata(null, "x", "view");
    var tableMeta = MbTilesInfo.ParseMetadata(null, "x", "table");
    Check("两种结构都只影响描述、不影响读法", viewMeta.Layout == "view" && tableMeta.Layout == "table");
}

Console.WriteLine();
Console.WriteLine(fail == 0
    ? $"全部通过：{pass} 项"
    : $"有 {fail} 项没过（通过 {pass} 项）");
return fail == 0 ? 0 : 1;
