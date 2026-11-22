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
/// 缺图先用父级 z-1 放大顶替（祖级最多追四层），再没有就露出纸面底色 —— 任何时候都不等网络。
/// </summary>
public sealed class TileLayerDrawable
{
    /// <summary>底图来源；<see cref="MapTiles.Manual"/> 时这层彻底不画。</summary>
    public MapTileSource Source { get; set; } = MapTiles.Manual;

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

    public bool Active => Service is not null && Source.HasTiles;

    /// <summary>这一档下，屏幕一像素多少地面米（比例尺跟着纬度走，见 MapProjection）。</summary>
    public double MetersPerPixel(double scale) => MetersPerUnit / Math.Max(1e-9, scale);

    public void Draw(ICanvas canvas, RectF dirty, double scale, double offsetX, double offsetY)
    {
        LastZoom = 0;
        LastTiles = 0;
        LastReady = 0;
        if (!Active || scale <= 0 || dirty.Width <= 0 || dirty.Height <= 0) return;

        var service = Service!;
        var zoomF = MapProjection.ZoomForCanvasScale(scale, MetersPerUnit, AnchorLat);
        var z = Math.Clamp((int)Math.Round(zoomF), MapProjection.MinZoom, MapProjection.MaxZoom);
        LastZoom = z;

        // 视口四角 → 经纬度（先用 WGS-84 算，再按来源需要换到 GCJ）
        var (latN, lonW) = MapProjection.CanvasToGeo(
            (dirty.Left - offsetX) / scale, (dirty.Top - offsetY) / scale,
            AnchorLat, AnchorLon, MetersPerUnit);
        var (latS, lonE) = MapProjection.CanvasToGeo(
            (dirty.Right - offsetX) / scale, (dirty.Bottom - offsetY) / scale,
            AnchorLat, AnchorLon, MetersPerUnit);

        if (Source.Crs == MapTileCrs.Gcj02)
        {
            (latN, lonW) = CoordConvert.Wgs84ToGcj02(latN, lonW);
            (latS, lonE) = CoordConvert.Wgs84ToGcj02(latS, lonE);
        }

        var last = MapProjection.TileCount(z) - 1;
        var x0 = Math.Clamp((int)Math.Floor(MapProjection.TileX(lonW, z)), 0, last);
        var x1 = Math.Clamp((int)Math.Floor(MapProjection.TileX(lonE, z)), 0, last);
        var y0 = Math.Clamp((int)Math.Floor(MapProjection.TileY(latN, z)), 0, last);
        var y1 = Math.Clamp((int)Math.Floor(MapProjection.TileY(latS, z)), 0, last);

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
                    if (DrawAncestor(canvas, service, z, x, y, scale, offsetX, offsetY)) continue;
                    if (DrawSibling(canvas, service, z, x, y, scale, offsetX, offsetY)) continue;
                    continue;
                }
                LastReady++;
                var rect = TileRect(z, x, y, scale, offsetX, offsetY);
                DrawTile(canvas, img, rect);
            }
        }

        DrawAttribution(canvas, dirty);
    }

    /// <summary>父级顶替：z-1 的一块正好盖住这一块的四分之一，铺满后按本块裁剪。</summary>
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
            var target = TileRect(z, x, y, scale, offsetX, offsetY);
            var cover = TileRect(az, ax, ay, scale, offsetX, offsetY);
            canvas.SaveState();
            canvas.ClipRectangle(target);
            DrawTile(canvas, img, cover);
            canvas.RestoreState();
            return true;
        }
        return false;
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
            var target = TileRect(z, x, y, scale, offsetX, offsetY);
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

    private void DrawTile(ICanvas canvas, Microsoft.Maui.Graphics.IImage img, RectF rect)
    {
        canvas.DrawImage(img, rect.X, rect.Y, rect.Width, rect.Height);
    }

    /// <summary>一块瓦片在屏幕上的矩形：从它的四至换算，所以任何缩放档都严丝合缝。</summary>
    private RectF TileRect(int z, int x, int y, double scale, double offsetX, double offsetY)
    {
        var b = MapProjection.TileBounds(z, x, y);
        var (north, west, south, east) = (b.North, b.West, b.South, b.East);
        if (Source.Crs == MapTileCrs.Gcj02)
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
