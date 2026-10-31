using Microsoft.Maui.Graphics;
using WarmAsBefore.Models;

namespace WarmAsBefore.Drawables;

/// <summary>3D 快照：整张图压成「几条路 + 几栋楼」的纯几何（世界坐标=米）。</summary>
public sealed class Minimal3DScene
{
    public string Name { get; set; } = "";
    public string Source { get; set; } = "";
    public List<MiniRoad> Roads { get; } = new();
    public List<MiniBuilding> Buildings { get; } = new();
    /// <summary>全图包围盒的中心与尺寸（米）：鸟瞰机位从这儿起。</summary>
    public double CenterX { get; set; }
    public double CenterY { get; set; }
    public double SpanM { get; set; }
    public bool Empty => Roads.Count == 0 && Buildings.Count == 0;
}

public sealed class MiniRoad
{
    /// <summary>中心线（世界米，已抽稀到 ≥2.5m 步长）。</summary>
    public List<Point> Pts { get; set; } = new();
    public double WidthM { get; set; }
    public string Name { get; set; } = "";
}

public sealed class MiniBuilding
{
    /// <summary>占地外轮廓（世界米，已抽稀到 ≤16 个顶点）。</summary>
    public List<Point> Footprint { get; set; } = new();
    public double HeightM { get; set; }
    /// <summary>质心（排序/雾裁/画名字用，不另算）。</summary>
    public double BaseX { get; set; }
    public double BaseY { get; set; }
    public string Name { get; set; } = "";
}

/// <summary>
/// 针孔相机：世界（米，y 轴向下）+ 偏航/俯仰（弧度，俯仰向下为正）。
/// 偏航 0 = 朝北（世界 -y）；向右转（朝东）为正。
/// </summary>
public sealed class Minimal3DCamera
{
    public const double PitchMin = -1.15;   // 仰头看楼顶
    public const double PitchMax = 1.35;    // 低头看脚面
    public double X { get; set; }
    public double Y { get; set; }
    public double Height { get; set; } = 40;
    public double YawRad { get; set; }
    public double PitchRad { get; set; } = 0.6;
    public double VFovDeg { get; set; } = 60;

    /// <summary>鸟瞰机位：站在全图中心上空，以 34° 俯角看北。</summary>
    public void OverviewFrom(Minimal3DScene scene)
    {
        X = scene.CenterX;
        Y = scene.CenterY;
        YawRad = 0;
        PitchRad = 34 * Math.PI / 180;
        Height = Math.Clamp(scene.SpanM * 0.42, 24, 380);
    }

    /// <summary>街面机位：站在某条街某个位置，按街的走向朝前看。</summary>
    public void StreetPose(Microsoft.Maui.Graphics.Point pos, double yawRad)
    {
        X = pos.X;
        Y = pos.Y;
        Height = 1.7;
        YawRad = yawRad;
        PitchRad = 6 * Math.PI / 180;
        VFovDeg = 75;
    }
}

/// <summary>
/// Minimal 3D 灰度视图的渲染器（ICanvas 矢量画）：把街道平铺到地面、
/// 把建筑按 <see cref="MapLocation.HeightPerFloor"/> 挤出灰楼。直线投影使世界的
/// 直线在屏幕上仍是直线，所以每条路/每面墙都是几笔多边形填充，不走位图。
/// </summary>
public sealed class Minimal3DDrawable : IDrawable
{
    public static readonly Color SkyColor = Color.FromArgb("#EDEDEB");
    public static readonly Color GroundColor = Color.FromArgb("#C7C7C5");

    public Minimal3DScene? Scene { get; set; }
    public Minimal3DCamera Camera { get; } = new();

    // 帧（在 Draw 开始时刷新）
    private double _f, _cx, _cy, _cosP, _sinP, _cosY, _sinY, _camH;
    private int _quads;

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        var w = dirtyRect.Width;
        var h = dirtyRect.Height;
        canvas.FillColor = SkyColor;
        canvas.FillRectangle(dirtyRect);

        var scene = Scene;
        if (scene is null || scene.Empty) return;
        var cam = Camera;
        _camH = cam.Height;
        _camX = cam.X;
        _camY = cam.Y;
        _cx = w / 2;
        _cy = h / 2;
        _f = (h / 2) / Math.Tan(cam.VFovDeg * Math.PI / 360);
        var yaw = cam.YawRad;
        var pitch = cam.PitchRad;
        _cosY = Math.Cos(yaw); _sinY = Math.Sin(yaw);
        _cosP = Math.Cos(pitch); _sinP = Math.Sin(pitch);
        _quads = 0;

        // 地面从「地平线」起：无限远的地面点投出来的高度（俯视时在天上那一头）。
        var horizonY = _cy - _f * Math.Tan(pitch);
        var gy = (float)Math.Clamp(horizonY, 0, h);
        canvas.FillColor = GroundColor;
        canvas.FillRectangle(0f, gy, w, h - gy);

        foreach (var r in scene.Roads) DrawRoad(canvas, r);

        // 建筑：远→近 Painter's order，楼顶随各自最后画。
        var order = new List<MiniBuilding>(scene.Buildings);
        order.Sort((a, b) => DepthAt(b.BaseX, b.BaseY).CompareTo(DepthAt(a.BaseX, a.BaseY)));
        foreach (var b in order)
        {
            if (_quads > 4500) break;    // 超大城兜底：宁可远处糊掉，不把帧拖死
            if (DepthAt(b.BaseX, b.BaseY) > 900) continue;
            DrawBuilding(canvas, b);
        }
    }

    // ==================== 道路 ====================

    private void DrawRoad(ICanvas canvas, MiniRoad road)
    {
        var pts = road.Pts;
        if (pts.Count < 2) return;
        canvas.FillColor = RoadColor(road.WidthM);

        var half = road.WidthM / 2;
        var n = pts.Count;
        Span<bool> ok = stackalloc bool[n];
        Span<float> lx = stackalloc float[n], ly = stackalloc float[n];
        Span<float> rx = stackalloc float[n], ry = stackalloc float[n];

        for (var i = 0; i < n; i++)
        {
            var prev = pts[Math.Max(0, i - 1)];
            var next = pts[Math.Min(n - 1, i + 1)];
            var dx = next.X - prev.X;
            var dy = next.Y - prev.Y;
            var len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 1e-6) { ok[i] = false; continue; }
            var nx = dy / len * half;
            var ny = -dx / len * half;
            var a = Proj(pts[i].X + nx, pts[i].Y + ny, 0);
            var b = Proj(pts[i].X - nx, pts[i].Y - ny, 0);
            if (!(a.ok && b.ok)) { ok[i] = false; continue; }
            ok[i] = true;
            lx[i] = a.x; ly[i] = a.y;
            rx[i] = b.x; ry[i] = b.y;
        }

        var start = -1;
        for (var i = 0; i <= n; i++)
        {
            var open = i < n && ok[i];
            if (open)
            {
                if (start < 0) start = i;
                continue;
            }
            if (start < 0) continue;

            // 一段可用的 run：左侧顺一遍、右侧倒回来，闭合成带子
            var path = new PathF(lx[start], ly[start]);
            for (var k = start + 1; k < i; k++) path.LineTo(lx[k], ly[k]);
            for (var k = i - 1; k >= start; k--) path.LineTo(rx[k], ry[k]);
            canvas.FillPath(path);
            _quads++;
            start = -1;
        }
    }

    private static Color RoadColor(double widthM) => widthM >= 10
        ? Color.FromArgb("#B7B7B5")     // 主干道亮些，认识层级
        : Color.FromArgb("#A7A7A5");

    // ==================== 建筑 ====================

    private void DrawBuilding(ICanvas canvas, MiniBuilding b)
    {
        var fp = b.Footprint;
        var n = fp.Count;
        if (n < 3) return;

        for (var i = 0; i < n; i++)
        {
            if (_quads > 4500) return;
            var p0 = fp[i];
            var p1 = fp[(i + 1) % n];
            var midX = (p0.X + p1.X) / 2;
            var midY = (p0.Y + p1.Y) / 2;
            var nrm = OutwardNormal(fp, i, b);
            // 背面剔除：面法线背着相机的不画
            if (nrm.x * (_camX - midX) + nrm.y * (_camY - midY) <= 0) continue;
            var depth = DepthAt(midX, midY);
            if (depth < -1) continue;
            var fog = FogAt(depth);

            var baseA = Proj(p0.X, p0.Y, 0);
            var baseB = Proj(p1.X, p1.Y, 0);
            var topA = Proj(p0.X, p0.Y, b.HeightM);
            var topB = Proj(p1.X, p1.Y, b.HeightM);
            if (!(baseA.ok && baseB.ok && topA.ok && topB.ok)) continue;

            canvas.FillColor = WallColor(nrm.x, nrm.y, fog);
            var path = new PathF(baseA.x, baseA.y);
            path.LineTo(baseB.x, baseB.y);
            path.LineTo(topB.x, topB.y);
            path.LineTo(topA.x, topA.y);
            canvas.FillPath(path);
            _quads++;
        }

        // 楼顶：盖在所有墙面之上
        var top = new PathF();
        var topOk = true;
        float firstTopX = 0, firstTopY = 0;
        for (var i = 0; i < n; i++)
        {
            var t = Proj(fp[i].X, fp[i].Y, b.HeightM);
            if (!t.ok) { topOk = false; break; }
            if (i == 0) { firstTopX = t.x; firstTopY = t.y; top.MoveTo(t.x, t.y); }
            else top.LineTo(t.x, t.y);
        }
        if (topOk)
        {
            var fog = FogAt(DepthAt(b.BaseX, b.BaseY));
            canvas.FillColor = MixGray(208, fog);
            top.LineTo(firstTopX, firstTopY);   // 闭合
            canvas.FillPath(top);
            _quads++;

            // 名字：楼投影够高才标，别把远景糊成一片字
            var baseC = Proj(b.BaseX, b.BaseY, 0);
            var topC = Proj(b.BaseX, b.BaseY, b.HeightM);
            if (baseC.ok && topC.ok && topC.y - baseC.y > 26 && !string.IsNullOrWhiteSpace(b.Name))
            {
                canvas.FontColor = MixGray(96, fog);
                canvas.FontSize = 11;
                var label = b.Name.Length > 14 ? b.Name[..13] + "…" : b.Name;
                canvas.DrawString(label, baseC.x - 60, topC.y - 18, 120, 16,
                    HorizontalAlignment.Center, VerticalAlignment.Center);
            }
        }
    }

    private double _camX, _camY;

    // ==================== 投影 / 颜色 ====================

    private double DepthAt(double wx, double wy)
    {
        var dx = wx - _camX;
        var dy = wy - _camY;
        return dx * _sinY - dy * _cosY;
    }

    private static double FogAt(double depth) => Math.Clamp((depth - 60) / 640, 0, 0.9);

    private (bool ok, float x, float y) Proj(double wx, double wy, double wz)
    {
        var dx = wx - _camX;
        var dy = wy - _camY;
        var depth = dx * _sinY - dy * _cosY;
        if (depth < 0.1) return (false, 0, 0);
        var side = dx * _cosY + dy * _sinY;
        var up = wz - _camH;
        var denom = depth * _cosP - up * _sinP;
        if (denom < 0.2) return (false, 0, 0);
        var x = _cx + _f * side / denom;
        var y = _cy - _f * (depth * _sinP + up * _cosP) / denom;
        return (true, (float)x, (float)y);
    }

    /// <summary>面朝外法线（与顶点绕向无关：用质心方向定向）。</summary>
    private static (double x, double y) OutwardNormal(List<Point> fp, int i, MiniBuilding b)
    {
        var p0 = fp[i];
        var p1 = fp[(i + 1) % fp.Count];
        var dx = p1.X - p0.X;
        var dy = p1.Y - p0.Y;
        var len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 1e-6) return (0, 0);
        var nx = dy / len;
        var ny = -dx / len;
        var mx = (p0.X + p1.X) / 2 - b.BaseX;
        var my = (p0.Y + p1.Y) / 2 - b.BaseY;
        if (nx * mx + ny * my > 0) return (-nx, -ny);   // 让它朝外（与质心反向）
        return (nx, ny);
    }

    /// <summary>墙影：光从西北来，越朝光越亮（±灰度，不引入彩色）。</summary>
    private static Color WallColor(double nx, double ny, double fog)
    {
        var lit = -nx * 0.6 - ny * 0.8;         // n 朝 (-0.6,-0.8) 方向的面最亮
        var g = 132 + 82 * Math.Clamp(lit, -0.6, 1);
        return MixGray((int)g, fog);
    }

    private static Color MixGray(int g, double fog)
    {
        var skyG = 237;
        var v = (int)(g + (skyG - g) * fog);
        v = Math.Clamp(v, 0, 255);
        return Color.FromRgb(v, v, v);
    }

    // ==================== 构建：MapDefinition → 快照 ====================
}

/// <summary>把 <see cref="MapDefinition"/> 压成 3D 快照：与 2D 画布共用同一套边端点/折点/曲线采样几何。</summary>
public static class Minimal3DBuilder
{
    public static Minimal3DScene Build(MapDefinition map)
    {
        var scene = new Minimal3DScene
        {
            Name = map.Name,
            Source = map.Source,
        };
        var mpu = map.MetersPerUnit > 0 ? map.MetersPerUnit : 0.5;
        double U(double v) => v * mpu;

        // 端点位置（画布单位）：场景 = 卡片中心，临时节点 = 圆心（与 MapCanvasDrawable.PointOf 同一约定）
        var posOf = new Dictionary<string, Point>();
        foreach (var loc in map.Locations)
            foreach (var s in loc.Scenes)
                if (s.X >= 0)
                    posOf[s.Id] = new Point(s.X + MapCanvasDrawable.CardW / 2, s.Y + MapCanvasDrawable.CardH / 2);
        foreach (var t in map.Transits)
            if (t.X >= 0)
                posOf[t.Id] = new Point(t.X, t.Y);

        // —— 道路 ——
        foreach (var e in map.Edges)
        {
            var kind = string.IsNullOrEmpty(e.Road) ? RoadKinds.Street : e.Road;
            if (kind is RoadKinds.Interior or RoadKinds.Steps) continue;
            if (!posOf.TryGetValue(e.A, out var a) || !posOf.TryGetValue(e.B, out var b)) continue;

            var units = SampleEdge(a, b, e);
            var road = new MiniRoad { WidthM = RoadWidthM(kind), Name = e.RoadName ?? "" };
            foreach (var p in units)
            {
                var m = new Point(U(p.X), U(p.Y));
                if (road.Pts.Count == 0 || Distance(m, road.Pts[^1]) >= 2.5) road.Pts.Add(m);
            }
            var tail = new Point(U(units[^1].X), U(units[^1].Y));
            if (road.Pts.Count == 0 || Distance(tail, road.Pts[^1]) >= 0.5) road.Pts.Add(tail);
            if (road.Pts.Count >= 2) scene.Roads.Add(road);
        }

        // —— 楼：子地点表用来跳过会包住孩子的父框 ——
        var children = new Dictionary<string, List<MapLocation>>();
        foreach (var loc in map.Locations)
        {
            if (string.IsNullOrEmpty(loc.ParentId)) continue;
            if (!children.TryGetValue(loc.ParentId, out var list))
                children[loc.ParentId] = list = new List<MapLocation>();
            list.Add(loc);
        }
        bool HasSubstance(MapLocation l) => l.Scenes.Count > 0 || l.Outline.Count >= 3;

        foreach (var loc in map.Locations)
        {
            var foot = FootprintUnits(loc, children, HasSubstance);
            if (foot is null) continue;

            var fp = foot.Select(p => new Point(U(p.X), U(p.Y))).ToList();
            fp = DecimatePolygon(fp, 16);
            if (fp.Count < 3) continue;

            var floors = loc.Floors > 0
                ? loc.Floors
                : Math.Max(1, loc.Scenes.Where(s => s.Floor is not null && s.Floor > 0)
                    .Select(s => s.Floor!.Value).DefaultIfEmpty(1).Max());
            var hpf = loc.HeightPerFloor > 0 ? loc.HeightPerFloor : 3.0;
            var h = Math.Clamp(floors * hpf, 3, 140);

            scene.Buildings.Add(new MiniBuilding
            {
                Footprint = fp,
                HeightM = h,
                Name = loc.Name,
                BaseX = fp.Average(p => p.X),
                BaseY = fp.Average(p => p.Y),
            });
        }

        // —— 包围盒：鸟瞰机位与镜头高度 ——
        var any = false;
        double minX = 0, minY = 0, maxX = 0, maxY = 0;
        void Add(double x, double y)
        {
            if (!any) { minX = maxX = x; minY = maxY = y; any = true; return; }
            if (x < minX) minX = x;
            if (y < minY) minY = y;
            if (x > maxX) maxX = x;
            if (y > maxY) maxY = y;
        }
        foreach (var r in scene.Roads) foreach (var p in r.Pts) Add(p.X, p.Y);
        foreach (var b in scene.Buildings)
        {
            foreach (var p in b.Footprint) Add(p.X, p.Y);
        }
        if (any)
        {
            scene.CenterX = (minX + maxX) / 2;
            scene.CenterY = (minY + maxY) / 2;
            scene.SpanM = Math.Max(maxX - minX, maxY - minY);
        }
        return scene;
    }

    /// <summary>楼基：有外轮廓用外轮廓；没有就按「自己有没有孩子的实体」决定卡片包围盒或空地点小桩。</summary>
    private static List<Point>? FootprintUnits(
        MapLocation loc, Dictionary<string, List<MapLocation>> children, Func<MapLocation, bool> hasSubstance)
    {
        if (loc.Outline.Count >= 3)
            return loc.Outline.Select(o => new Point(o.X, o.Y)).ToList();

        // 父框只是装孩子的盒：孩子自己会挤楼，这里不重复
        var kids = children.TryGetValue(loc.Id, out var list) ? list : null;
        if (kids is not null && kids.Any(hasSubstance)) return null;

        if (loc.Scenes.Any(s => s.X >= 0))
        {
            // 手工图：本楼场景卡片的包围盒（垫一圈边距）当楼基
            var minX = double.MaxValue; var minY = double.MaxValue;
            var maxX = double.MinValue; var maxY = double.MinValue;
            foreach (var s in loc.Scenes)
            {
                if (s.X < 0) continue;
                minX = Math.Min(minX, s.X);
                minY = Math.Min(minY, s.Y);
                maxX = Math.Max(maxX, s.X + MapCanvasDrawable.CardW);
                maxY = Math.Max(maxY, s.Y + MapCanvasDrawable.CardH);
            }
            minX -= 20; minY -= 30; maxX += 20; maxY += 16;
            return new List<Point>
            {
                new(minX, minY), new(maxX, minY), new(maxX, maxY), new(minX, maxY),
            };
        }

        // 空地点锚点：放一个小桩，地图不至于在 3D 里显得什么都没有
        if (loc.X >= 0)
            return new List<Point>
            {
                new(loc.X - 6, loc.Y - 4), new(loc.X + 6, loc.Y - 4),
                new(loc.X + 6, loc.Y + 4), new(loc.X - 6, loc.Y + 4),
            };
        return null;
    }

    private static double RoadWidthM(string kind) => kind switch
    {
        RoadKinds.Highway => 14,
        RoadKinds.Trunk => 11,
        RoadKinds.Street => 7.5,
        RoadKinds.Bridge => 8,
        RoadKinds.Tunnel => 7.5,
        RoadKinds.Waterway => 9,
        RoadKinds.Lane => 4,
        RoadKinds.Walkway => 2,
        _ => 7.5,
    };

    /// <summary>把边展开成顶点序列（直线=端点+折点；曲线=Catmull-Rom 采样，与 2D 画布同几何）。</summary>
    private static List<Point> SampleEdge(Point a, Point b, MapEdge e)
    {
        var pts = new List<Point> { a };
        foreach (var w in e.Waypoints) pts.Add(new Point(w.X, w.Y));
        pts.Add(b);
        if (e.Kind != "curve" || pts.Count < 3) return pts;

        var outP = new List<Point>();
        for (var i = 0; i < pts.Count - 1; i++)
        {
            var p0 = pts[Math.Max(0, i - 1)];
            var p1 = pts[i];
            var p2 = pts[i + 1];
            var p3 = pts[Math.Min(pts.Count - 1, i + 2)];
            const int n = 16;
            for (var k = 0; k < n; k++)
            {
                var t = k / (double)n;
                var t2 = t * t;
                var t3 = t2 * t;
                var x = 0.5 * (2 * p1.X + (-p0.X + p2.X) * t + (2 * p0.X - 5 * p1.X + 4 * p2.X - p3.X) * t2 + (-p0.X + 3 * p1.X - 3 * p2.X + p3.X) * t3);
                var y = 0.5 * (2 * p1.Y + (-p0.Y + p2.Y) * t + (2 * p0.Y - 5 * p1.Y + 4 * p2.Y - p3.Y) * t2 + (-p0.Y + 3 * p1.Y - 3 * p2.Y + p3.Y) * t3);
                outP.Add(new Point(x, y));
            }
        }
        outP.Add(pts[^1]);
        return outP;
    }

    /// <summary>轮邠抽稀：优先折掉太近的顶点，把楼面墙数压在顶点数以内。</summary>
    private static List<Point> DecimatePolygon(List<Point> poly, int maxPoints)
    {
        if (poly.Count <= maxPoints) return poly;
        while (poly.Count > maxPoints)
        {
            // 每轮挑最短的边，把它与相邻边合并（顶点去掉一个）
            var best = -1;
            var bestLen = double.MaxValue;
            for (var i = 0; i < poly.Count; i++)
            {
                var a = poly[i];
                var c = poly[(i + 1) % poly.Count];
                var len = Distance(a, c);
                if (len < bestLen) { bestLen = len; best = i; }
            }
            if (best < 0) break;
            poly.RemoveAt(best);
            if (poly.Count < 3) return poly;
        }
        return poly;
    }

    private static double Distance(Point a, Point b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
