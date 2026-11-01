using Microsoft.Maui.Graphics;
using WarmAsBefore.DesignSystem.Worlds;
using WarmAsBefore.Models;
using WarmAsBefore.Services;

namespace WarmAsBefore.Drawables;

/// <summary>
/// 小地图（右上角那一块）：把 <see cref="MinimapPlan"/> 画成一枚方片或一张展开的面板。
///
/// 它和大画布（<see cref="MapCanvasDrawable"/>）是同一套语法的缩小版：路按类别涂「路面 + 路基」、
/// 楼按类别涂灰面、字用墨色——所以从主界面瞄一眼右上角，再进大地图不会读成另一张图。
/// 颜色不许在这里写死成最终值：这块板压在场景背景照片上，浅世界的米纸到了暗世界就是一块糊眼的白斑，
/// 一律用 <see cref="StyleWorldTokens.Reanchor"/> 按当前地面重钉明度（同 <see cref="GraphPalette"/> 的规矩）。
///
/// 两档形态共用一个绘制器：<see cref="Compact"/> = 60pt 方片，只留路形、当前位置与南向标（字一律不画，
/// 60 像素里摆字只会糊成一团）；展开档才长出房间名、POI 名字与列表。
/// </summary>
public sealed class MinimapDrawable : IDrawable
{
    public MinimapPlan? Plan { get; set; }

    /// <summary>方片档：只画形，不画字。</summary>
    public bool Compact { get; set; }

    /// <summary>点命中的 POI 由页面回读（画的时候顺手记下屏幕位置，不另算一遍）。</summary>
    private readonly List<(string Id, float X, float Y, float R)> _hits = new();

    /// <summary>屏幕点落在哪个 POI 上；没命中返回 null。</summary>
    public string? HitPoi(Point screen)
    {
        for (var i = _hits.Count - 1; i >= 0; i--)
        {
            var h = _hits[i];
            var dx = (float)screen.X - h.X;
            var dy = (float)screen.Y - h.Y;
            if (dx * dx + dy * dy <= (h.R + 5) * (h.R + 5)) return h.Id;
        }
        return null;
    }

    // ==================== 颜色：一份钉在当前地面上的色带 ====================

    private static readonly Color DesignGround = Color.FromArgb("#F5F0E8");
    private static readonly Color DesignInk = Color.FromArgb("#443626");
    private static readonly Color DesignMuted = Color.FromArgb("#6B5D4B");
    private static readonly Color DesignGlow = Color.FromArgb("#F5D8B8");
    private static readonly Color DesignPaper = Color.FromArgb("#FFFDF7");
    private static readonly Color DesignWall = Color.FromArgb("#C9BCA8");
    private static readonly Color DesignRoom = Color.FromArgb("#EFE6D6");

    private sealed class Bands
    {
        public required Color Ground;
        public required Color Paper;
        public required Color Ink;
        public required Color Muted;
        public required Color Glow;
        public required Color Wall;
        public required Color Room;
        public required Dictionary<string, RoadStyle> Road;
        public required Dictionary<string, FieldStyle> Field;
    }

    /// <summary>
    /// 一类路 = 一对颜色（深路基 + 亮路面），抄自大画布 <see cref="MapCanvasDrawable"/> 的那张表：
    /// 两处用同一族配色，右上角和主界面才不会读成两张图。表里存的是设计稿种子，
    /// 真画出来的一律过 Reanchor——暗世界上几块米黄路面会亮得扎眼。
    /// </summary>
    private static readonly (string Kind, string Casing, string Surface, bool Dash)[] RoadSeeds =
    {
        (RoadKinds.Highway, "#6B4A18", "#E3B34C", false),
        (RoadKinds.Trunk, "#7A5620", "#E8C97A", false),
        (RoadKinds.County, "#705122", "#DFC489", false),
        (RoadKinds.Rural, "#6F5E46", "#D9CBB2", false),
        (RoadKinds.Street, "#6E5240", "#D8C3AA", false),
        (RoadKinds.Lane, "#7A5F4A", "#E4D6C4", false),
        (RoadKinds.Walkway, "#4E6B4A", "#BFD6BC", true),
        (RoadKinds.Steps, "#5B4A6B", "#D6C8E2", true),
        (RoadKinds.Bridge, "#5D4037", "#DCCDB8", false),
        (RoadKinds.Tunnel, "#3E5163", "#BFCAD6", true),
        (RoadKinds.Waterway, "#2F6A85", "#A8CFE0", false),
        (RoadKinds.Interior, "#8A6D1A", "#E7D9A8", true),
    };

    /// <summary>地块底色：种子抄自大画布那张地表表，同样一律过 Reanchor，两处读出来才是一块地。</summary>
    private static readonly (string Kind, string Fill, string Line, bool Dash)[] FieldSeeds =
    {
        (RegionKinds.Green, "#9CC49A", "#4E6B4A", false),
        (RegionKinds.Water, "#9FC8DE", "#2F6A85", false),
        (RegionKinds.Route, "#A8CFE0", "#2F6A85", false),
        (RegionKinds.Plaza, "#E7DCC8", "#A08A62", false),
        (RegionKinds.District, "#C9A8E1", "#6B4A8A", true),
    };

    private const string StreetFallback = RoadKinds.Street;

    private static Bands _bands = Build(DesignGround);

    private static Bands Build(Color ground)
    {
        Color Re(Color design) => StyleWorldTokens.Reanchor(design, ground);
        var road = new Dictionary<string, RoadStyle>(StringComparer.Ordinal);
        foreach (var s in RoadSeeds)
            road[s.Kind] = new RoadStyle(Re(Color.FromArgb(s.Casing)), Re(Color.FromArgb(s.Surface)), s.Dash);
        var field = new Dictionary<string, FieldStyle>(StringComparer.Ordinal);
        foreach (var s in FieldSeeds)
            field[s.Kind] = new FieldStyle(Re(Color.FromArgb(s.Fill)), Re(Color.FromArgb(s.Line)), s.Dash);
        return new Bands
        {
            Ground = ground,
            Paper = Re(DesignPaper),
            Ink = Re(DesignInk),
            Muted = Re(DesignMuted),
            Glow = Re(DesignGlow),
            Wall = Re(DesignWall),
            Room = Re(DesignRoom),
            Road = road,
            Field = field,
        };
    }

    /// <summary>地面换了才重算这一份（切世界 / 翻深浅才会换），每帧 Build 会把 HSL 换算做十几遍。</summary>
    private static Bands Current()
    {
        var ground = StyleWorldTokens.Pick("PageBg", DesignGround);
        if (!Same(_bands.Ground, ground)) _bands = Build(ground);
        return _bands;
    }

    private static bool Same(Color a, Color b) =>
        a.Red == b.Red && a.Green == b.Green && a.Blue == b.Blue && a.Alpha == b.Alpha;

    // ==================== 一帧 ====================

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        var b = Current();
        var w = dirtyRect.Width;
        var h = dirtyRect.Height;
        _hits.Clear();

        canvas.FillColor = b.Paper;
        canvas.FillRoundedRectangle(dirtyRect, 14f);

        var plan = Plan;
        if (plan is null) return;

        if (!plan.HasCanvas)
        {
            DrawListGlyph(canvas, b, w, h);
            DrawCompass(canvas, b, w, h);
            return;
        }

        // 世界米 → 屏幕：屏幕短边的一半对应可见半径（室内档的半径已由取景框算好）。
        var pad = Compact ? 5f : 8f;
        var scale = (MathF.Min(w, h) / 2 - pad) / (float)Math.Max(plan.RangeM, 1);
        var cx = w / 2;
        var cy = h / 2;
        float Sx(double x) => (float)(cx + (x - plan.CenterX) * scale);
        float Sy(double y) => (float)(cy + (y - plan.CenterY) * scale);

        if (plan.Mode == MinimapMode.Indoor) DrawIndoor(canvas, b, plan, Sx, Sy, scale);
        else DrawOutdoor(canvas, b, plan, Sx, Sy, scale, w, h);

        DrawCompass(canvas, b, w, h);
    }

    // ==================== 室外：路网 + 楼基 + 附近可去 ====================

    private void DrawOutdoor(ICanvas canvas, Bands b, MinimapPlan plan,
        Func<double, float> Sx, Func<double, float> Sy, float scale, float w, float h)
    {
        var geo = plan.Geo;
        if (geo is null) return;
        var r2 = plan.RangeM * plan.RangeM * 1.6;   // 稍微放一点：贴着边缘的路画半条也比没有强

        // 地表在最底：公园、湖水、广场铺一层色，航道是一条带子——楼与路都长在这上面，一眼能读出「我在绿色的边上」。
        foreach (var f in plan.Fields)
        {
            if (!b.Field.TryGetValue(f.Kind, out var style)) continue;
            if (RegionKinds.IsLinear(f.Kind))
            {
                if (f.Points.Count < 2) continue;
                canvas.StrokeColor = style.Fill;
                canvas.StrokeSize = Compact ? 1.6f : 3.2f;
                StrokePolyline(canvas, f.Points, Sx, Sy);
                continue;
            }
            if (f.Points.Count < 3) continue;
            var path = FootPath(f.Points, Sx, Sy);
            // 方片只有 60 像素，颜色掺得淡一点，别把路网淹了。
            canvas.FillColor = Mix(b.Paper, style.Fill, Compact ? 0.34f : 0.5f);
            canvas.FillPath(path);
            if (!Compact && style.Dash)
            {
                canvas.StrokeColor = Mix(style.Line, b.Paper, 0.45f);
                canvas.StrokeSize = 0.8f;
                canvas.StrokeDashPattern = Dash3;
                canvas.DrawPath(path);
                canvas.StrokeDashPattern = null;
            }
        }

        // 楼基先铺：城里的楼是「块」，乡下的楼只是几笔轮廓——乡野不该被大片灰面压住。
        foreach (var bd in geo.Buildings)
        {
            if (DistSq(bd.Box, plan) > r2) continue;
            if (bd.Footprint.Count < 3) continue;
            var path = FootPath(bd.Footprint, Sx, Sy);
            if (plan.Tier == MinimapTier.City)
            {
                canvas.FillColor = Mix(b.Paper, b.Wall, 0.5f);
                canvas.FillPath(path);
            }
            canvas.StrokeColor = Mix(b.Wall, b.Paper, 0.35f);
            canvas.StrokeSize = Compact ? 0.5f : 0.8f;
            canvas.DrawPath(path);
        }

        // 路：先涂一遍深路基、再压一道亮路面（与大画布同一手法，相交处才读出「路网」而非一摊色）。
        // 宽度不按真实米数硬换算：拉到 3 公里时一条巷只有 0.06 像素，层级全没了。
        // 改成「一档底线 + 米数加成」，巷子永远还看得见，主干道仍然比它粗。
        foreach (var road in geo.Roads)
        {
            if (DistSq(road.Box, plan) > r2) continue;
            var style = b.Road.TryGetValue(road.Kind ?? "", out var s) ? s : b.Road[StreetFallback];
            var pts = road.Pts;
            if (pts.Count < 2) continue;
            var casing = Math.Clamp(1.1f + (float)road.WidthM * scale * 0.8f, 1.1f, Compact ? 3f : 6f);
            var surface = Math.Max(casing - 1.2f, 0.7f);

            canvas.StrokeColor = style.Casing;
            canvas.StrokeSize = casing;
            canvas.StrokeDashPattern = !Compact && style.Dash ? Dash3 : null;
            StrokePolyline(canvas, pts, Sx, Sy);

            canvas.StrokeColor = style.Surface;
            canvas.StrokeSize = surface;
            StrokePolyline(canvas, pts, Sx, Sy);
            canvas.StrokeDashPattern = null;
        }

        DrawPois(canvas, b, plan, Sx, Sy, w, h);

        // 我自己：暖光点 + 一圈白边，压在所有东西上面（站在楼基上也得看得见）。
        canvas.FillColor = b.Glow;
        canvas.FillCircle(Sx(plan.CenterX), Sy(plan.CenterY), Compact ? 3.4f : 4.6f);
        canvas.StrokeColor = b.Paper;
        canvas.StrokeSize = Compact ? 1.2f : 1.6f;
        canvas.DrawCircle(Sx(plan.CenterX), Sy(plan.CenterY), Compact ? 3.4f : 4.6f);
    }

    /// <summary>POI 点：方片只画点，展开档才把最近的几处写上名字。</summary>
    private void DrawPois(ICanvas canvas, Bands b, MinimapPlan plan,
        Func<double, float> Sx, Func<double, float> Sy, float w, float h)
    {
        var labelBudget = Compact ? 0 : plan.Tier == MinimapTier.Country ? 4 : 3;
        var dotR = Compact ? 2.2f : 3f;
        var named = 0;
        foreach (var poi in plan.Dots)
        {
            var x = Sx(poi.X);
            var y = Sy(poi.Y);
            if (x < -20 || x > w + 20 || y < -20 || y > h + 20) continue;
            canvas.FillColor = b.Ink;
            canvas.FillCircle(x, y, dotR);
            _hits.Add((poi.SceneId, x, y, dotR));
            if (named >= labelBudget) continue;
            named++;
            DrawLabel(canvas, b, Shorten(poi.Name, plan.Tier == MinimapTier.Country ? 8 : 6),
                new RectF(x - 62, y - 16, 124, 13));
        }
    }

    // ==================== 室内：本楼本层户型简图 ====================

    private void DrawIndoor(ICanvas canvas, Bands b, MinimapPlan plan,
        Func<double, float> Sx, Func<double, float> Sy, float scale)
    {
        // 外轮廓：有真轮廓就描那栋楼，没有就把房间框的外壳当楼壳（手工图常常只有卡片）。
        if (plan.Outline is { Count: >= 3 } outline)
        {
            var path = FootPath(outline, Sx, Sy);
            canvas.FillColor = b.Room;
            canvas.FillPath(path);
            canvas.StrokeColor = b.Wall;
            canvas.StrokeSize = Compact ? 1f : 1.6f;
            canvas.DrawPath(path);
        }

        var labelBudget = Compact ? 0 : 6;
        var labeled = 0;
        foreach (var room in plan.Rooms)
        {
            var box = room.BoxMeters;
            var x = Sx(box.Left);
            var y = Sy(box.Top);
            var rw = (float)(box.Width * scale);
            var rh = (float)(box.Height * scale);
            var rect = new RectF(x, y, rw, rh);
            canvas.FillColor = room.IsCurrent ? b.Glow : Mix(b.Paper, b.Room, 0.7f);
            canvas.FillRoundedRectangle(rect, Compact ? 2f : 4f);
            canvas.StrokeColor = room.IsCurrent ? b.Ink : b.Wall;
            canvas.StrokeSize = room.IsCurrent ? (Compact ? 1.2f : 1.8f) : 0.8f;
            canvas.DrawRoundedRectangle(rect, Compact ? 2f : 4f);
            if (room.IsCurrent || labeled >= labelBudget || Compact || rw < 34 || rh < 12) continue;
            labeled++;
            DrawLabel(canvas, b, Shorten(room.Name, 7), rect);
        }

        // 我在这间：房间框已经填成暖色，再钉一颗墨点，免得整片暖色读成「这儿有一间房」。
        var cur = plan.Rooms.FirstOrDefault(r => r.IsCurrent);
        if (cur is not null)
        {
            var box = cur.BoxMeters;
            var px = Sx(box.Left + box.Width / 2);
            var py = Sy(box.Top + box.Height / 2);
            canvas.FillColor = b.Ink;
            canvas.FillCircle(px, py, Compact ? 2.6f : 3.4f);
        }
    }

    // ==================== 兜底：一张纸也画不出时的列表符号 ====================

    private static void DrawListGlyph(ICanvas canvas, Bands b, float w, float h)
    {
        var rows = 3;
        var left = w * 0.26f;
        var top = h * 0.34f;
        var gap = h * 0.14f;
        for (var i = 0; i < rows; i++)
        {
            var y = top + i * gap;
            canvas.FillColor = b.Glow;
            canvas.FillCircle(left, y + 1.5f, 1.8f);
            canvas.StrokeColor = b.Muted;
            canvas.StrokeSize = 1.6f;
            var len = i == rows - 1 ? w * 0.24f : w * 0.32f;
            canvas.DrawLine(left + 5, y, left + 5 + len, y);
        }
    }

    // ==================== 南向标 ====================

    /// <summary>正北朝上，所以「南」永远在下方：一支朝下的箭 + 一个字，位置钉死在右下角。</summary>
    private void DrawCompass(ICanvas canvas, Bands b, float w, float h)
    {
        var cxp = w - (Compact ? 11f : 15f);
        var cyTop = Compact ? h - 20f : h - 24f;
        var cyBot = h - (Compact ? 9f : 11f);
        canvas.StrokeColor = b.Muted;
        canvas.StrokeSize = Compact ? 1f : 1.2f;
        canvas.DrawLine(cxp, cyTop, cxp, cyBot);
        canvas.DrawLine(cxp - 2.4f, cyBot - 2.6f, cxp, cyBot);
        canvas.DrawLine(cxp + 2.4f, cyBot - 2.6f, cxp, cyBot);
        if (Compact) return;
        canvas.FontSize = 8;
        canvas.FontColor = b.Muted;
        canvas.DrawString("南", cxp - 12, cyTop - 1, 10, 10, HorizontalAlignment.Center, VerticalAlignment.Center);
    }

    // ==================== 小工具 ====================

    private readonly record struct RoadStyle(Color Casing, Color Surface, bool Dash);
    private readonly record struct FieldStyle(Color Fill, Color Line, bool Dash);

    private static readonly float[] Dash3 = { 3, 3 };

    private static readonly (float X, float Y)[] HaloOffsets = { (-1, 0), (1, 0), (0, -1), (0, 1) };

    /// <summary>
    /// 名字压在路面上还得读得清：先用纸色把字往四边垫开一格，再落墨。
    /// 同一个位置叠两遍不叫描边，叫糊。
    /// </summary>
    private static void DrawLabel(ICanvas canvas, Bands b, string text, RectF box)
    {
        if (string.IsNullOrEmpty(text)) return;
        canvas.FontSize = 9;
        foreach (var o in HaloOffsets)
        {
            canvas.FontColor = b.Paper;
            canvas.DrawString(text, box.X + o.X, box.Y + o.Y, box.Width, box.Height,
                HorizontalAlignment.Center, VerticalAlignment.Center);
        }
        canvas.FontColor = b.Ink;
        canvas.DrawString(text, box.X, box.Y, box.Width, box.Height,
            HorizontalAlignment.Center, VerticalAlignment.Center);
    }

    private static void StrokePolyline(ICanvas canvas, List<Point> pts,
        Func<double, float> Sx, Func<double, float> Sy)
    {
        for (var i = 1; i < pts.Count; i++)
            canvas.DrawLine(Sx(pts[i - 1].X), Sy(pts[i - 1].Y), Sx(pts[i].X), Sy(pts[i].Y));
    }

    private static PathF FootPath(List<Point> foot, Func<double, float> Sx, Func<double, float> Sy)
    {
        var path = new PathF(Sx(foot[0].X), Sy(foot[0].Y));
        for (var i = 1; i < foot.Count; i++) path.LineTo(Sx(foot[i].X), Sy(foot[i].Y));
        path.Close();
        return path;
    }

    /// <summary>包围盒中心到视点的距离平方：整条路/整栋楼离得远就一笔不碰。</summary>
    private static double DistSq(RectF box, MinimapPlan plan)
    {
        var dx = (box.Left + box.Right) / 2 - plan.CenterX;
        var dy = (box.Top + box.Bottom) / 2 - plan.CenterY;
        return dx * dx + dy * dy;
    }

    private static string Shorten(string s, int max)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Length <= max ? s : s[..max] + "…";
    }

    /// <summary>两色按 k 掺（k=0 全 a、k=1 全 b）：小地图的颜色都是「纸与墨之间的一档」。</summary>
    private static Color Mix(Color a, Color b, float k) => Color.FromRgb(
        (int)Math.Round(a.Red * (1 - k) + b.Red * k),
        (int)Math.Round(a.Green * (1 - k) + b.Green * k),
        (int)Math.Round(a.Blue * (1 - k) + b.Blue * k));
}
