using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace WarmAsBefore.Services;

/// <summary>MBTiles 落在地图上的入口状态：没选、文件不在、扩展名不对、可以读。</summary>
public enum MbTilesPathState
{
    NotSet,
    Missing,
    WrongExtension,
    Ok
}

/// <summary>
/// 一个 MBTiles 包说自己是什么（metadata 表里那几行）。
/// 所有字段都允许缺失：包制作者漏写 metadata 很常见，缺了就用保守默认值。
/// </summary>
public sealed record MbTilesMeta(
    string Name,
    string Format,
    int MinZoom,
    int MaxZoom,
    double? West,
    double? South,
    double? East,
    double? North,
    bool XyzScheme,
    string Layout)
{
    public bool HasBounds => West is not null && South is not null && East is not null && North is not null;

    /// <summary>这个包有没有这一档：包外的档位直接说「没有」，别去查一张注定空的表。</summary>
    public bool Covers(int z) => z >= MinZoom && z <= MaxZoom;

    /// <summary>bounds 按 MBTiles 规范是 west,south,east,north。</summary>
    public string BoundsText => HasBounds
        ? string.Create(CultureInfo.InvariantCulture,
            $"{West:0.0000},{South:0.0000},{East:0.0000},{North:0.0000}")
        : "";

    public string SchemeText => XyzScheme ? "XYZ" : "TMS";
}

/// <summary>
/// MBTiles 读取里能脱开 SQLite 独立验证的那一半：纯函数，<c>tools/map_selftest</c> 直接链过去对拍。
///
/// 三件事最容易写错，所以全在这里：
/// 1. <b>y 轴</b>：MBTiles 的 tile_row 是 TMS（自南向北），地图画的是 XYZ（自北向南），
///    y = 2^z - 1 - row；但少数包写的是 scheme=xyz，那就不能再翻。
/// 2. <b>metadata</b>：name/format/minzoom/maxzoom/bounds 都是字符串，缺失、写错、上下限颠倒都见过。
/// 3. <b>路径</b>：用户选的路径可能是空的、被移走的、或者根本不是 .mbtiles。
/// </summary>
public static class MbTilesInfo
{
    public const string Extension = ".mbtiles";

    public const string KeyName = "name";
    public const string KeyFormat = "format";
    public const string KeyMinZoom = "minzoom";
    public const string KeyMaxZoom = "maxzoom";
    public const string KeyBounds = "bounds";
    public const string KeyScheme = "scheme";

    public const int MaxSupportedZoom = 22;

    // ==================== y 轴 ====================

    /// <summary>TMS tile_row → XYZ y。同一次翻转再做一次就回到原值。</summary>
    public static int FlipRow(int tileRow, int zoom)
    {
        var z = Math.Clamp(zoom, 0, MaxSupportedZoom);
        return (1 << z) - 1 - tileRow;
    }

    /// <summary>XYZ y → TMS tile_row，写进 SQL 的就是它。</summary>
    public static int RowForY(int y, int zoom, bool xyzScheme) =>
        xyzScheme ? y : FlipRow(y, zoom);

    /// <summary>读出来的 tile_row → 画布用的 y。</summary>
    public static int YForRow(int row, int zoom, bool xyzScheme) =>
        xyzScheme ? row : FlipRow(row, zoom);

    /// <summary>SQuAD 的 SQL 参数要验一下范围：坏的 row 查出来是空，但缓存会记成「没有」。</summary>
    public static bool TryYForRow(int row, int zoom, bool xyzScheme, out int y)
    {
        y = 0;
        if (zoom is < 0 or > MaxSupportedZoom) return false;
        var count = 1 << zoom;
        if (row < 0 || row >= count) return false;
        y = YForRow(row, zoom, xyzScheme);
        return y >= 0 && y < count;
    }

    // ==================== metadata ====================

    /// <summary>
    /// 把 metadata 表的键值对折成一个包描述。
    /// <paramref name="layout"/> 是 sqlite_master 查到的 tiles 对象类型（table / view），只记录，不改读法。
    /// </summary>
    public static MbTilesMeta ParseMetadata(
        IReadOnlyDictionary<string, string?>? raw, string? fallbackName, string layout = "unknown")
    {
        string Read(string key) =>
            raw is not null && raw.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : "";

        var name = Read(KeyName);
        if (name.Length == 0)
            name = string.IsNullOrWhiteSpace(fallbackName) ? "离线包" : fallbackName!.Trim();

        var format = NormalizeFormat(Read(KeyFormat));
        var minZoom = ParseZoom(Read(KeyMinZoom), 0);
        var maxZoom = ParseZoom(Read(KeyMaxZoom), MaxSupportedZoom);
        if (minZoom > maxZoom) (minZoom, maxZoom) = (maxZoom, minZoom);

        var bounds = ParseBounds(Read(KeyBounds));
        var xyz = IsXyzScheme(Read(KeyScheme));

        return new MbTilesMeta(
            name, format, minZoom, maxZoom,
            bounds?.West, bounds?.South, bounds?.East, bounds?.North,
            xyz, string.IsNullOrWhiteSpace(layout) ? "unknown" : layout.Trim().ToLowerInvariant());
    }

    private static int ParseZoom(string text, int fallback) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)
            ? Math.Clamp(v, 0, MaxSupportedZoom)
            : fallback;

    /// <summary>bounds = "west,south,east,north"。顺序颠倒、超范围（纬度 ±85）都算坏数据。</summary>
    public static (double West, double South, double East, double North)? ParseBounds(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var parts = text.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length != 4) return null;
        var nums = new double[4];
        for (var i = 0; i < 4; i++)
            if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out nums[i])
                || double.IsNaN(nums[i]) || double.IsInfinity(nums[i]))
                return null;
        var (west, south, east, north) = (nums[0], nums[1], nums[2], nums[3]);
        if (west < -180 || east > 180 || south < -85.0511 || north > 85.0511) return null;
        if (west >= east || south >= north) return null;
        return (west, south, east, north);
    }

    public static string NormalizeFormat(string? format)
    {
        var f = (format ?? "").Trim().ToLowerInvariant();
        if (f.Length == 0) return "png";
        if (f is "jpg" or "jpeg") return "jpeg";
        if (f is "png" or "webp") return f;
        return f;
    }

    /// <summary>少数包把 scheme=xyz 写在 metadata 里：那种 tile_row 已经是自北向南，不能再翻。</summary>
    public static bool IsXyzScheme(string? scheme) =>
        string.Equals(scheme?.Trim(), "xyz", StringComparison.OrdinalIgnoreCase);

    /// <summary>sqlite_master 里 tiles 是 table（标准两张表）还是 view（map + images 合成）。</summary>
    public static bool IsViewLayout(string? sqliteType) =>
        string.Equals(sqliteType?.Trim(), "view", StringComparison.OrdinalIgnoreCase);

    // ==================== 瓦片字节的格式 ====================

    /// <summary>按魔数判断格式：PNG / JPEG / WEBP，其余返回空串（这张图不画，别当缓存命中）。</summary>
    public static string SniffFormat(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 8
            && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47
            && bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A)
            return "png";
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
            return "jpeg";
        if (bytes.Length >= 12
            && bytes[0] == (byte)'R' && bytes[1] == (byte)'I' && bytes[2] == (byte)'F' && bytes[3] == (byte)'F'
            && bytes[8] == (byte)'W' && bytes[9] == (byte)'E' && bytes[10] == (byte)'B' && bytes[11] == (byte)'P')
            return "webp";
        return "";
    }

    // ==================== 路径 ====================

    /// <summary>
    /// 路径校验只看字符串与一个「文件在不在」的布尔值（查盘留给调用方），
    /// 这样自测能穷举「空路径 / 被移走 / 选错文件」三种状态而不碰文件系统。
    /// </summary>
    public static (MbTilesPathState State, string Message) CheckPath(string? path, bool exists)
    {
        var p = (path ?? "").Trim();
        if (p.Length == 0)
            return (MbTilesPathState.NotSet, "还没选离线包：点「选择 .mbtiles 文件」挑一个包，选完地图就能离线看。");
        if (!p.EndsWith(Extension, StringComparison.OrdinalIgnoreCase))
            return (MbTilesPathState.WrongExtension, $"这不是 MBTiles 包（要 .mbtiles 扩展名）：{p}");
        if (!exists)
            return (MbTilesPathState.Missing, $"离线包文件不在了：{p} —— 可能被移走或删掉了，重新选一次。");
        return (MbTilesPathState.Ok, $"离线包已就位：{Path.GetFileName(p)}");
    }

    /// <summary>
    /// 只读连接串。单例连接 + 只读：地图线程不会写坏别人的包，
    /// 也不会因为两个进程同时开包而互相锁住。
    /// </summary>
    public static string ReadOnlyConnectionString(string path)
    {
        var escaped = path.Replace("'", "''");
        var sb = new StringBuilder("Data Source='");
        sb.Append(escaped);
        sb.Append("';Mode=ReadOnly;Cache=Private;Pooling=False");
        return sb.ToString();
    }

    /// <summary>状态行/抽屉里那句「这是什么包」。</summary>
    public static string Describe(MbTilesMeta meta)
    {
        var head = $"{meta.Name} · z{meta.MinZoom}–{meta.MaxZoom} · {meta.Format.ToUpperInvariant()}";
        if (meta.HasBounds) head += $" · 范围 {meta.BoundsText}";
        if (meta.XyzScheme) head += " · XYZ";
        if (meta.Layout == "view") head += " · 视图结构";
        return head;
    }
}
