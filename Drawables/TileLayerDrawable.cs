using System;
using Microsoft.Maui.Graphics;
using WarmAsBefore.Services;

namespace WarmAsBefore.Drawables;

/// <summary>
/// 底图层：把 XYZ 瓦片铺在现有地图画布的最底下，叠加层（地点、地块、路网、备注）照旧压在它上面。
///
/// 和 <see cref="MapCanvasDrawable"/> 共用同一套 Offset / Scale，所以「瓦片 → 卡片」不需要第二套坐标系，
/// 拖拽、滚轮、双击、框选这些交互一行都不用改。
///
/// 每一帧只做三件事：算出可见瓦片、画内存里已经有的、缺的交给 <see cref="TileService"/> 后台排队。
/// 缺图先用子级或父级顶替（不要闪黑），再没有就露出纸面底色 —— 任何时候都不等网络。
///
/// 换源、跨档时还有一层「垫底」：上一份底图 / 上一档的瓦片先铺满，新图到了再盖上去。
/// 这样切来源、滚轮缩放的过程中不会先白一下再出图。
/// </summary>
public sealed class TileLayerDrawable
{
    private MapTileSource _source = MapTiles.Manual;

    /// <summary>底图来源；<see cref="MapTiles.Manual"/> 时这层彻底不画。</summary>
    public MapTileSource Source
    {
        get => _source;
        set
        {
            if (string.Equals(_source.Id, value.Id, StringComparison.Ordinal)) return;
            // 旧来源留着当垫底：新来源一块都没到之前，不能先把画面清空。
            _backdropSource = _source.HasTiles ? _source : null;
            _backdropZ = LastZoom;
            _backdropUntil = DateTime.UtcNow.AddSeconds(6);
            _source = value;
        }
    }

    public TileService? Service { get; set; }

    /// <summary>画布原点的经纬度：手绘地图没有真实中心时用 <see cref="MapTiles.DefaultAnchorLat"/>。</summary>
    public double AnchorLat { get; set; } = MapTiles.DefaultAnchorLat;
    public double AnchorLon { get; set; } = MapTiles.DefaultAnchorLon;

    /// <summary>与 MapCanvasDrawable.MetersPerUnit 同步：画布 1 单位 = 多少米。</summary>
    public double MetersPerUnit { get; set; } = 0.5;

    /// <summary>来源标注的颜色（由画布按当前世界令牌给，不在这里写死色值）。</summary>
    public Color AttributionColor { get; set; } = Colors.Gray;

    // ---- 上一帧的账，状态条读它（「z18 · 看得见 12 块 / 已到 9 块」） ----
    public int LastZoom { get; private set; }
    public int LastTiles { get; private set; }
    public int LastReady { get; private set; }

    // ---- 垫底层：换源或跨档时先把旧图铺住，等新图到齐再撤 ----
    private MapTileSource? _backdropSource;
    private int _backdropZ;
    private DateTime _backdropUntil = DateTime.MinValue;

    public bool Active => Service is not null && Source.HasTiles;

    /// <summary>这一档下，屏幕一像素多少地面米（比例尺跟着纬度走，见 MapProjection）。</summary>
    public double MetersPerPixel(double scale) => MetersPerUnit / Math.Max(1e-9, scale);

    public void Draw(ICanvas canvas, RectF dirty, double scale, double offsetX, double offsetY)
    {
        var previousZoom = LastZoom;
        LastZoom = 0;
        LastTiles = 0;
        LastReady = 0;
        if (!Active || scale <= 0 || dirty.Width <= 0 || dirty.Height <= 0) return;

        var service = Service!;
        var zoomF = MapProjection.ZoomForCanvasScale(scale, MetersPerUnit, AnchorLat);
        var z = Math.Clamp((int)Math.Round(zoomF), MapProjection.MinZoom, MapProjection.MaxZoom);

        // 跨档：上一档铺住，免得新档还没到货时整屏空一下。
        if (previousZoom > 0 && previousZoom != z && _backdropSource is null)
        {
            _backdropSource = Source;
            _backdropZ = previousZoom;
            _backdropUntil = DateTime.UtcNow.AddSeconds(6);
        }
        LastZoom = z;

        DrawBackdrop(canvas, service, dirty, scale, offsetX, offsetY);

        if (!VisibleRange(Source, z, dirty, scale, offsetX, offsetY,
                out var x0, out var x1, out var y0, out var y1)) return;

        var last = MapProjection.TileCount(z) - 1;
        x0 = Math.Clamp(x0, 0, last);
        x1 = Math.Clamp(x1, 0, last);
        y0 = Math.Clamp(y0, 0, last);
        y1 = Math.Clamp(y1, 0, last);

        // 极端缩放（或还没量到视口）时别一次排上千块：宁可这一帧空着。
        if ((long)(x1 - x0 + 1) * (y1 - y0 + 1) > 400) return;

        // 一帧最多排 12 块：大屏一次能看见上百块，全塞进队列等于对服务器来个突发，
        // 而画面上真正要看的是先到的那几块 —— 剩下的下一帧接着排。
        var budget = 12;
        for (var y = y0; y <= y1; y++)
        {
            for (var x = x0; x <= x1; x++)
            {
                LastTiles++;
                var img = service.Peek(Source, z, x, y);
                if (img is null)
                {
                    if (budget-- > 0) service.Request(Source, z, x, y);
                    if (DrawDescendant(canvas, service, z, x, y, scale, offsetX, offsetY)) continue;
                    if (DrawAncestor(canvas, service, z, x, y, scale, offsetX, offsetY)) continue;
                    if (DrawSibling(canvas, service, z, x, y, scale, offsetX, offsetY)) continue;
                    continue;
                }
                LastReady++;
                var rect = TileRect(Source, z, x, y, scale, offsetX, offsetY);
                DrawTile(canvas, img, rect);
            }
        }

        // 周边一屏的预取：拖动时不至于一路看着空白跟上来。预算小，离视口越近越先排。
        PrefetchAround(service, z, x0, x1, y0, y1, 16);

        // 当前这块底图铺满了，垫底的就撤：留着只会拖慢后面的帧。
        if (_backdropSource is not null && LastTiles > 0 && LastReady >= LastTiles
            && (_backdropSource.Id != Source.Id || _backdropZ != z))
            _backdropSource = null;

        DrawAttribution(canvas, dirty);
    }

    /// <summary>垫底：用上一份来源 / 上一档已经缓存好的瓦片，按当前变换铺一遍。不发请求。</summary>
    private void DrawBackdrop(ICanvas canvas, TileService service, RectF dirty,
        double scale, double offsetX, double offsetY)
    {
        if (_backdropSource is not { HasTiles: true } source || _backdropZ <= 0) return;
        // 新图一直没来（离线包里没有这片）就别永远挂着旧底图：6 秒后露出纸面，状态行会说明缺图。
        if (DateTime.UtcNow > _backdropUntil) { _backdropSource = null; return; }
        var z = Math.Clamp(_backdropZ, MapProjection.MinZoom, MapProjection.MaxZoom);
        if (!VisibleRange(source, z, dirty, scale, offsetX, offsetY,
                out var x0, out var x1, out var y0, out var y1)) return;

        var last = MapProjection.TileCount(z) - 1;
        x0 = Math.Max(0, x0);
        x1 = Math.Min(last, x1);
        y0 = Math.Max(0, y0);
        y1 = Math.Min(last, y1);
        for (var y = y0; y <= y1; y++)
            for (var x = x0; x <= x1; x++)
            {
                var img = service.Peek(source, z, x, y);
                if (img is null) continue;
                DrawTile(canvas, img, TileRect(source, z, x, y, scale, offsetX, offsetY));
            }
    }

    /// <summary>
    /// 父级顶替：z-1 的一块正好盖住这一块的四分之一，铺满后按本块裁剪。
    /// 缩放过程中新档还没到，就是靠它顶着，不会闪黑。
    /// </summary>
    private bool DrawAncestor(ICanvas canvas, TileService service, int z, int x, int y,
        double scale, double offsetX, double offsetY)
    {
        for (var k = 1; k <= 4; k++)
        {
            var az = z - k;
            if (az < MapProjection.MinZoom) break;
            var ax = x >> k;
            var ay = y >> k;
            var img = service.Peek(Source, az, ax, ay);
            if (img is null)
            {
                // 父级的父级未必值得排队：只把最近的一层补上，别为顶替把整条链都拉下来。
                if (k == 1) service.Request(Source, az, ax, ay);
                continue;
            }
            var target = TileRect(Source, z, x, y, scale, offsetX, offsetY);
            var cover = TileRect(Source, az, ax, ay, scale, offsetX, offsetY);
            canvas.SaveState();
            canvas.ClipRectangle(target);
            DrawTile(canvas, img, cover);
            canvas.RestoreState();
            return true;
        }
        return false;
    }

    /// <summary>子级顶替：缩小一档时 z+1 的四块已经在了，拼起来比父级更清楚。</summary>
    private bool DrawDescendant(ICanvas canvas, TileService service, int z, int x, int y,
        double scale, double offsetX, double offsetY)
    {
        var cz = z + 1;
        if (cz > MapProjection.MaxZoom) return false;
        var target = TileRect(Source, z, x, y, scale, offsetX, offsetY);
        var any = false;
        for (var dy = 0; dy < 2; dy++)
            for (var dx = 0; dx < 2; dx++)
            {
                var cx = (x << 1) + dx;
                var cy = (y << 1) + dy;
                var img = service.Peek(Source, cz, cx, cy);
                if (img is null) continue;
                if (!any)
                {
                    canvas.SaveState();
                    canvas.ClipRectangle(target);
                    any = true;
                }
                DrawTile(canvas, img, TileRect(Source, cz, cx, cy, scale, offsetX, offsetY));
            }
        if (any) canvas.RestoreState();
        return any;
    }

    /// <summary>
    /// 高德在个别位置会缺一块（海域、境外）：拿左右或上下紧邻的一块拉伸过来，
    /// 总比在路网中间留一个白洞强。只做一次，不会连环借。
    /// </summary>
    private bool DrawSibling(ICanvas canvas, TileService service, int z, int x, int y,
        double scale, double offsetX, double offsetY)
    {
        var last = MapProjection.TileCount(z) - 1;
        var cands = new (int X, int Y)[] { (x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1) };
        foreach (var (sx, sy) in cands)
        {
            if (sx < 0 || sy < 0 || sx > last || sy > last) continue;
            var img = service.Peek(Source, z, sx, sy);
            if (img is null) continue;
            var target = TileRect(Source, z, x, y, scale, offsetX, offsetY);
            // 把邻居里「贴着这一块的那半边」拉过来，方向对得上，接缝处才不像贴纸。
            // ICanvas 没有源矩形重载，所以按比例把整张图放大并把要用的那半推到目标框里。
            var fx0 = sx < x ? 0.5 : 0.0;
            var fx1 = sx > x ? 0.5 : 1.0;
            var fy0 = sy < y ? 0.5 : 0.0;
            var fy1 = sy > y ? 0.5 : 1.0;
            var w = target.Width / Math.Max(0.5, fx1 - fx0);
            var h = target.Height / Math.Max(0.5, fy1 - fy0);
            canvas.SaveState();
            canvas.ClipRectangle(target);
            canvas.DrawImage(img, (float)(target.X - fx0 * w), (float)(target.Y - fy0 * h), (float)w, (float)h);
            canvas.RestoreState();
            return true;
        }
        return false;
    }

    /// <summary>
    /// 视口周围一圈预取：从贴着视口的一环往外走，只排内存里没有的，排满预算就停。
    /// 与可见块共用同一套并发闸（TileService 按来源限并发），所以预取不会把服务器敲爆。
    /// </summary>
    private void PrefetchAround(TileService service, int z, int x0, int x1, int y0, int y1, int budget)
    {
        if (budget <= 0 || !Source.HasTiles) return;
        var last = MapProjection.TileCount(z) - 1;
        var maxRing = Math.Max(x1 - x0 + 1, y1 - y0 + 1);
        for (var ring = 1; ring <= maxRing && budget > 0; ring++)
        {
            for (var y = y0 - ring; y <= y1 + ring && budget > 0; y++)
            {
                for (var x = x0 - ring; x <= x1 + ring && budget > 0; x++)
                {
                    var onRing = x == x0 - ring || x == x1 + ring || y == y0 - ring || y == y1 + ring;
                    if (!onRing) continue;
                    if (x < 0 || y < 0 || x > last || y > last) continue;
                    if (service.Cached(Source, z, x, y)) continue;
                    service.Request(Source, z, x, y);
                    budget--;
                }
            }
        }
    }

    /// <summary>视口四角 → 经纬度 → 瓦片编号范围。任何来源的 CRS 都由调用方传的那一份决定。</summary>
    private bool VisibleRange(MapTileSource source, int z, RectF dirty, double scale,
        double offsetX, double offsetY, out int x0, out int x1, out int y0, out int y1)
    {
        x0 = x1 = y0 = y1 = 0;
        if (dirty.Width <= 0 || dirty.Height <= 0) return false;

        var (latN, lonW) = MapProjection.CanvasToGeo(
            (dirty.Left - offsetX) / scale, (dirty.Top - offsetY) / scale,
            AnchorLat, AnchorLon, MetersPerUnit);
        var (latS, lonE) = MapProjection.CanvasToGeo(
            (dirty.Right - offsetX) / scale, (dirty.Bottom - offsetY) / scale,
            AnchorLat, AnchorLon, MetersPerUnit);

        if (source.Crs == MapTileCrs.Gcj02)
        {
            (latN, lonW) = CoordConvert.Wgs84ToGcj02(latN, lonW);
            (latS, lonE) = CoordConvert.Wgs84ToGcj02(latS, lonE);
        }

        x0 = (int)Math.Floor(MapProjection.TileX(lonW, z));
        x1 = (int)Math.Floor(MapProjection.TileX(lonE, z));
        y0 = (int)Math.Floor(MapProjection.TileY(latN, z));
        y1 = (int)Math.Floor(MapProjection.TileY(latS, z));
        return true;
    }

    private void DrawTile(ICanvas canvas, Microsoft.Maui.Graphics.IImage img, RectF rect)
    {
        canvas.DrawImage(img, rect.X, rect.Y, rect.Width, rect.Height);
    }

    /// <summary>一块瓦片在屏幕上的矩形：从它的四至换算，所以任何缩放档都严丝合缝。</summary>
    private RectF TileRect(MapTileSource source, int z, int x, int y,
        double scale, double offsetX, double offsetY)
    {
        var b = MapProjection.TileBounds(z, x, y);
        var (north, west, south, east) = (b.North, b.West, b.South, b.East);
        if (source.Crs == MapTileCrs.Gcj02)
        {
            (north, west) = CoordConvert.Gcj02ToWgs84(north, west);
            (south, east) = CoordConvert.Gcj02ToWgs84(south, east);
        }
        var (wx0, wy0) = MapProjection.GeoToCanvas(north, west, AnchorLat, AnchorLon, MetersPerUnit);
        var (wx1, wy1) = MapProjection.GeoToCanvas(south, east, AnchorLat, AnchorLon, MetersPerUnit);
        return new RectF(
            (float)(wx0 * scale + offsetX),
            (float)(wy0 * scale + offsetY),
            (float)((wx1 - wx0) * scale),
            (float)((wy1 - wy0) * scale));
    }

    /// <summary>右下角一行来源标注：各家授权都要求露出来，字号小但不许没有。</summary>
    private void DrawAttribution(ICanvas canvas, RectF dirty)
    {
        if (Source.Attribution.Length == 0) return;
        const float size = 10f;
        try
        {
            var font = Microsoft.Maui.Graphics.Font.Default;
            var measured = canvas.GetStringSize(Source.Attribution, font, size);
            var x = dirty.Right - measured.Width - 10;
            var y = dirty.Bottom - size - 6;
            canvas.Font = font;
            canvas.FontSize = size;
            canvas.FontColor = AttributionColor.WithAlpha(0.75f);
            canvas.DrawString(Source.Attribution, x, y, measured.Width + 2, size + 4,
                HorizontalAlignment.Left, VerticalAlignment.Top);
        }
        catch
        {
            // 标注画不出来不该影响地图本身。
        }
    }
}
