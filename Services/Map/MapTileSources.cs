using System;
using System.Collections.Generic;

namespace WarmAsBefore.Services;

/// <summary>底图瓦片的坐标系：境外一律 WGS-84，国内高德是 GCJ-02。</summary>
public enum MapTileCrs
{
    Wgs84,
    Gcj02
}

/// <summary>
/// 一种底图来源：地址模板 + 坐标系 + 授权说明。
///
/// 「离线包」是同一套缓存目录的另一个视角 —— 它不发请求，只把已经落盘的瓦片当底图。
/// 缓存里存着什么就看得见什么，看不到的用父级放大顶着，不会一直转圈。
/// </summary>
public sealed class MapTileSource
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    /// <summary>抽屉里那行小字：说清这张底图是什么、能不能离线。</summary>
    public string Note { get; init; } = "";

    /// <summary>URL 模板；null = 不发请求（手工绘制 / 离线包）。</summary>
    public string? UrlTemplate { get; init; }

    /// <summary>瓦片的坐标系。<see cref="MapTileCrs.Gcj02"/> 的会把请求中心先偏移再取图，落图时再偏回来。</summary>
    public MapTileCrs Crs { get; init; } = MapTileCrs.Wgs84;

    /// <summary>这个来源允许的最大并发请求数：OSM 是志愿者服务器，敲太狠会被封 UA。</summary>
    public int MaxConcurrency { get; init; } = 6;

    /// <summary>下载时带的 User-Agent（对方按它识别客户端，OSM 明确要求写应用名）。</summary>
    public string UserAgent { get; init; } = MapTiles.UserAgent;

    /// <summary>地图右下角的来源标注，按各家授权要求必须露出来。</summary>
    public string Attribution { get; init; } = "";

    /// <summary>能不能真的发请求。</summary>
    public bool IsOnline => UrlTemplate is not null;

    /// <summary>是不是一张底图（手工绘制不是）。</summary>
    public bool HasTiles => Id != MapTiles.ManualId;

    /// <summary>是不是 MBTiles 离线包（读本地文件，不走网络也不走瓦片缓存目录）。</summary>
    public bool IsMbTiles => Id == MapTiles.MbTilesId;

    /// <summary>拼一块瓦片的地址。高德有 wprd01–04 四台前端机，按 x+y 摊开，别全压在 1 号上。</summary>
    public string BuildUrl(int z, int x, int y)
    {
        if (UrlTemplate is not { } tpl) return "";
        var url = tpl.Replace("{z}", z.ToString()).Replace("{x}", x.ToString()).Replace("{y}", y.ToString());
        if (url.Contains("{s}"))
            url = url.Replace("{s}", ((x + y) % 4 + 1).ToString());
        return url;
    }

    public override string ToString() => Name;
}

/// <summary>底图来源清单 + 默认落点。全是常量，别在这里做 IO。</summary>
public static class MapTiles
{
    public const string ManualId = "manual";
    public const string AmapRoadId = "amap-road";
    public const string AmapSatId = "amap-sat";
    public const string OsmId = "osm";
    public const string OfflineId = "offline";
    public const string MbTilesId = "mbtiles";

    /// <summary>OSM 要求 UA 里带应用标识与联系方式，别用默认库 UA。</summary>
    public const string UserAgent = "WarmAsBefore/1.65 (MAUI map tile client; +https://github.com/gongchanwangzilin/warmasbefore)";

    /// <summary>手工绘制：现状那套画布，没有任何底图。</summary>
    public static readonly MapTileSource Manual = new()
    {
        Id = ManualId,
        Name = "手工绘制",
        Note = "只画你自己的地点与路网，不联网。",
    };

    public static readonly MapTileSource AmapRoad = new()
    {
        Id = AmapRoadId,
        Name = "高德路网",
        Note = "高德瓦片（GCJ-02，落图时按 WGS-84 偏回来），中文路名。",
        UrlTemplate = "https://wprd0{s}.is.autonavi.com/appmaptile?lang=zh_cn&size=1&style=7&x={x}&y={y}&z={z}",
        Crs = MapTileCrs.Gcj02,
        Attribution = "© 高德地图",
    };

    public static readonly MapTileSource AmapSatellite = new()
    {
        Id = AmapSatId,
        Name = "高德卫星",
        Note = "高德卫星影像（GCJ-02），没有路名，适合看地块与屋顶。",
        UrlTemplate = "https://wprd0{s}.is.autonavi.com/appmaptile?lang=zh_cn&size=1&style=6&x={x}&y={y}&z={z}",
        Crs = MapTileCrs.Gcj02,
        Attribution = "© 高德地图",
    };

    public static readonly MapTileSource Osm = new()
    {
        Id = OsmId,
        Name = "OSM",
        Note = "开放街道图（WGS-84）。志愿者服务器，并发压到 2，别当自家图床用。",
        UrlTemplate = "https://tile.openstreetmap.org/{z}/{x}/{y}.png",
        Crs = MapTileCrs.Wgs84,
        MaxConcurrency = 2,
        Attribution = "© OpenStreetMap contributors",
    };

    public static readonly MapTileSource Offline = new()
    {
        Id = OfflineId,
        Name = "本地缓存",
        Note = "只用本地缓存里的瓦片，不发任何请求。切到这个来源前先联网逛一遍要用的区域。",
        UrlTemplate = null,
        Attribution = "本地瓦片缓存",
    };

    /// <summary>
    /// 第六种来源：用户自己选的 .mbtiles 文件。
    /// 与「本地缓存」不同 —— 缓存是逛图时顺手存的，离线包是一个完整的、打包好的地区。
    /// 路径在设置里，这里只给定义；真正打开与读瓦片由 <see cref="MbTilesSource"/> 负责。
    /// </summary>
    public static readonly MapTileSource MbTiles = new()
    {
        Id = MbTilesId,
        Name = "离线包",
        Note = "读取你选的 .mbtiles 文件：整个地区都在本地，没网也能看。",
        UrlTemplate = null,
        Attribution = "离线包 · MBTiles",
    };

    public static readonly IReadOnlyList<MapTileSource> All = new[]
    {
        Manual, AmapRoad, AmapSatellite, Osm, Offline, MbTiles
    };

    /// <summary>存档里的来源 id → 定义。认不出来的（老存档 / 手改过）当手工绘制。</summary>
    public static MapTileSource ById(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return Manual;
        foreach (var s in All)
            if (string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase)) return s;
        return Manual;
    }

    /// <summary>
    /// 没有经纬度的地图（手绘）要对齐到哪里：上海人民广场。
    /// 只是给底图一个落点，不改存档里的中心 —— 想真对齐就导入一张带经纬度的城市图。
    /// </summary>
    public const double DefaultAnchorLat = 31.2304;
    public const double DefaultAnchorLon = 121.4737;
    public const string DefaultAnchorName = "上海（默认落点）";
}
