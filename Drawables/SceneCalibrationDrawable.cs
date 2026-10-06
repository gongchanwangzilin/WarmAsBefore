using Microsoft.Maui.Graphics;
using Microsoft.Maui.Graphics.Platform;
using WarmAsBefore.Models;
using WarmAsBefore.Services;

namespace WarmAsBefore.Drawables;

/// <summary>
/// 标定画布：在一张背景原图上画落脚点与缩放依据。
///
/// 与地图编辑器画布（MapCanvasDrawable）的关键区别：
/// - 世界坐标 = **原图像素**（0..iw, 0..ih），不是抽象画布坐标。像素坐标让"球半径""命中容差"都直观；
///   落盘时才除以 (iw, ih) 归一化。这样同一份标定数据在任何分辨率/任何设备上都能还原。
/// - 整图 AspectFit 显示（留黑边），因为划线必须看到图的全貌；
///   而游戏里是 AspectFill（会裁切），所以这里额外画出**游戏实际可见框**，避免标到看不见的地方。
/// </summary>
public sealed class SceneCalibrationDrawable : IDrawable
{
    // ---- 数据（VM 填充）----
    public ScenePlacement? Placement { get; set; }
    /// <summary>背景图绝对路径。</summary>
    public string? ImagePath { get; set; }
    /// <summary>原图像素尺寸（世界坐标的范围）。</summary>
    public int ImageWidth { get; set; }
    public int ImageHeight { get; set; }

    // ---- 视口 ----
    public double ViewportWidth { get; set; }
    public double ViewportHeight { get; set; }
    public double OffsetX { get; set; }
    public double OffsetY { get; set; }
    public double Scale { get; set; } = 1.0;

    public Point WorldToScreen(Point w) => new(w.X * Scale + OffsetX, w.Y * Scale + OffsetY);
    public Point ScreenToWorld(Point s) => new((s.X - OffsetX) / Scale, (s.Y - OffsetY) / Scale);

    /// <summary>把整图缩放居中（AspectFit）。</summary>
    public void FitToViewport(double vw, double vh)
    {
        ViewportWidth = vw;
        ViewportHeight = vh;
        if (ImageWidth <= 0 || ImageHeight <= 0 || vw <= 0 || vh <= 0) return;
        Scale = Math.Min(vw / ImageWidth, vh / ImageHeight);
        OffsetX = (vw - ImageWidth * Scale) / 2;
        OffsetY = (vh - ImageHeight * Scale) / 2;
    }

    /// <summary>游戏里目标容器的宽高比（宽/高）。null = 不画可视框。</summary>
    public double? TargetAspect { get; set; }

    // ---- 高亮状态（VM 填）----
    /// <summary>当前正在拖拽的折线顶点。</summary>
    public (LineGroup Group, int LineIndex, int PointIndex) DragVertex { get; set; } = (LineGroup.None, -1, -1);
    /// <summary>当前被拖拽的球索引（-1 = 无）。</summary>
    public int DragSphere { get; set; } = -1;
    /// <summary>悬停的球索引（-1 = 无）。</summary>
    public int HoverSphere { get; set; } = -1;
    /// <summary>正在拖拽创建的线段预览（原图像素坐标）。</summary>
    public (Point A, Point B)? LinePreview { get; set; }

    /// <summary>折线归属：落脚线 vs 缩放线。两者是独立数据，命中/绘制都要分开。</summary>
    public enum LineGroup { None, Landing, Scale }

    // ---- 图片缓存 ----
    // 必须写全限定名：Microsoft.Maui.Graphics.Platform 与 Microsoft.Maui 下都有 IImage
    private Microsoft.Maui.Graphics.IImage? _image;
    private bool _imageLoaded;

    /// <summary>异步取图（照 MapCanvasDrawable 的做法：后台读盘、主线程解码）。返回后需自行 Invalidate。</summary>
    public void EnsureImageLoaded(Action onLoaded)
    {
        if (_imageLoaded) return;
        _imageLoaded = true;
        var path = ImagePath;
        if (string.IsNullOrEmpty(path)) return;
        _ = Task.Run(() =>
        {
            byte[] bytes;
            try { bytes = File.ReadAllBytes(path); }
            catch { return; }
            MainThread.BeginInvokeOnMainThread(() =>
            {
                try
                {
                    using var ms = new MemoryStream(bytes);
                    _image = PlatformImage.FromStream(ms);
                }
                catch { _image = null; }
                onLoaded();
            });
        });
    }

    public void ResetImage() { _image = null; _imageLoaded = false; }

    // ============ 命中测试（都收世界坐标 = 原图像素）============

    private List<NormLine> LinesOf(LineGroup g) => g switch
    {
        LineGroup.Landing => Placement?.Lines ?? new List<NormLine>(),
        LineGroup.Scale => Placement?.ScaleLines ?? new List<NormLine>(),
        _ => new List<NormLine>()
    };

    /// <summary>命中折线顶点（两组线都查）。返回归属与索引，未命中 (None,-1,-1)。</summary>
    public (LineGroup Group, int LineIndex, int PointIndex) HitVertex(Point w, double tol)
    {
        foreach (var g in new[] { LineGroup.Landing, LineGroup.Scale })
        {
            var lines = LinesOf(g);
            for (int li = 0; li < lines.Count; li++)
            {
                var pts = lines[li].Points;
                for (int pi = 0; pi < pts.Count; pi++)
                    if (Near(w, ToPixels(pts[pi]), tol)) return (g, li, pi);
            }
        }
        return (LineGroup.None, -1, -1);
    }

    /// <summary>命中折线线身（不含顶点，两组线都查）。返回归属与线索引，未命中 (None,-1)。</summary>
    public (LineGroup Group, int LineIndex) HitLineBody(Point w, double tol)
    {
        foreach (var g in new[] { LineGroup.Landing, LineGroup.Scale })
        {
            var lines = LinesOf(g);
            for (int li = 0; li < lines.Count; li++)
            {
                var pts = lines[li].Points;
                for (int i = 1; i < pts.Count; i++)
                {
                    var a = ToPixels(pts[i - 1]);
                    var b = ToPixels(pts[i]);
                    if (DistanceToSegment(w, a, b) <= tol) return (g, li);
                }
            }
        }
        return (LineGroup.None, -1);
    }

    /// <summary>命中球心。返回球索引，未命中 -1。</summary>
    public int HitSphereCenter(Point w, double tol)
    {
        var p = Placement;
        if (p is null) return -1;
        for (int i = 0; i < p.Spheres.Count; i++)
            if (Near(w, SphereCenterPixels(p.Spheres[i]), tol)) return i;
        return -1;
    }

    /// <summary>命中球缘（拖拽定半径）。返回球索引，未命中 -1。</summary>
    public int HitSphereRim(Point w, double tol)
    {
        var p = Placement;
        if (p is null) return -1;
        for (int i = 0; i < p.Spheres.Count; i++)
        {
            var s = p.Spheres[i];
            var c = SphereCenterPixels(s);
            var r = s.R * ImageWidth;
            var d = Math.Sqrt((w.X - c.X) * (w.X - c.X) + (w.Y - c.Y) * (w.Y - c.Y));
            if (Math.Abs(d - r) <= tol) return i;
        }
        return -1;
    }

    /// <summary>命中落脚点。返回点索引，未命中 -1。</summary>
    public int HitPoint(Point w, double tol)
    {
        var p = Placement;
        if (p is null) return -1;
        for (int i = 0; i < p.Points.Count; i++)
            if (Near(w, ToPixels(p.Points[i]), tol)) return i;
        return -1;
    }

    private Point ToPixels(NormPoint n) => new(n.X * ImageWidth, n.Y * ImageHeight);

    /// <summary>球心的原图像素坐标（NormSphere 不是 NormPoint，需单独换算）。</summary>
    private Point SphereCenterPixels(NormSphere s) => new(s.X * ImageWidth, s.Y * ImageHeight);
    private NormPoint ToNorm(Point px) => new(
        ImageWidth > 0 ? px.X / ImageWidth : 0,
        ImageHeight > 0 ? px.Y / ImageHeight : 0);

    public NormPoint ToNormalized(Point px) => ToNorm(px);

    private static bool Near(Point a, Point b, double tol)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return dx * dx + dy * dy <= tol * tol;
    }

    private static double DistanceToSegment(Point p, Point a, Point b)
    {
        var vx = b.X - a.X;
        var vy = b.Y - a.Y;
        var len2 = vx * vx + vy * vy;
        if (len2 <= 0) return Math.Sqrt((p.X - a.X) * (p.X - a.X) + (p.Y - a.Y) * (p.Y - a.Y));
        var t = Math.Clamp(((p.X - a.X) * vx + (p.Y - a.Y) * vy) / len2, 0, 1);
        var px = a.X + vx * t;
        var py = a.Y + vy * t;
        return Math.Sqrt((p.X - px) * (p.X - px) + (p.Y - py) * (p.Y - py));
    }

    // ============ 绘制 ============

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        canvas.FillColor = Colors.Black;
        canvas.FillRectangle(dirtyRect);

        if (ImageWidth <= 0 || ImageHeight <= 0) return;

        var imgRect = new RectF(
            (float)WorldToScreen(new Point(0, 0)).X,
            (float)WorldToScreen(new Point(0, 0)).Y,
            (float)(ImageWidth * Scale),
            (float)(ImageHeight * Scale));

        if (_image is not null)
            canvas.DrawImage(_image, imgRect.X, imgRect.Y, imgRect.Width, imgRect.Height);
        else
        {
            canvas.FillColor = Color.FromArgb("#1A1A1A");
            canvas.FillRectangle(imgRect);
        }

        DrawVisibleFrame(canvas, imgRect);
        DrawScalePreview(canvas);
        DrawPlacement(canvas);
    }

    /// <summary>
    /// 画出游戏里 AspectFill 之后**实际可见**的区域：框外压暗。
    /// 标到框外的落脚点/球心在游戏里根本看不到，必须让用户当场看见这件事。
    /// </summary>
    private void DrawVisibleFrame(ICanvas canvas, RectF imgRect)
    {
        if (TargetAspect is not { } ar || ar <= 0 || ImageWidth <= 0 || ImageHeight <= 0) return;

        var imgAr = (double)ImageWidth / ImageHeight;
        double visW, visH;
        if (ar > imgAr)
        {
            // 容器比图更宽 → AspectFill 先满足宽度，上下被裁
            visW = 1.0;
            visH = (ImageWidth / ar) / ImageHeight;
        }
        else
        {
            // 容器比图更高 → 先满足高度，左右被裁
            visH = 1.0;
            visW = (ImageHeight * ar) / ImageWidth;
        }
        visW = Math.Clamp(visW, 0, 1);
        visH = Math.Clamp(visH, 0, 1);

        var nx = (1 - visW) / 2;
        var ny = (1 - visH) / 2;
        var frame = new RectF(
            imgRect.X + (float)(nx * imgRect.Width),
            imgRect.Y + (float)(ny * imgRect.Height),
            (float)(visW * imgRect.Width),
            (float)(visH * imgRect.Height));

        // 框外压暗
        canvas.FillColor = Color.FromRgba(0, 0, 0, 150);
        canvas.FillRectangle(imgRect.X, imgRect.Y, imgRect.Width, frame.Y - imgRect.Y);
        canvas.FillRectangle(imgRect.X, frame.Bottom, imgRect.Width, imgRect.Bottom - frame.Bottom);
        canvas.FillRectangle(imgRect.X, frame.Y, frame.X - imgRect.X, frame.Height);
        canvas.FillRectangle(frame.Right, frame.Y, imgRect.Right - frame.Right, frame.Height);

        canvas.StrokeColor = Color.FromArgb("#FFCC55");
        canvas.StrokeSize = 1.5f;
        canvas.StrokeDashPattern = new[] { 6f, 4f };
        canvas.DrawRectangle(frame);
        canvas.StrokeDashPattern = null;
    }

    /// <summary>
    /// 缩放曲线预览：沿落脚区域撒几个采样点，画出"角色在这里会多大"的高度条。
    /// 双线画反了、间距没变化之类的问题，肉眼一看就知道，不用等进游戏才发现。
    /// </summary>
    private void DrawScalePreview(ICanvas canvas)
    {
        var p = Placement;
        if (p is null) return;

        var (_, missing) = PlacementMath.Validate(p);
        if (missing.Count > 0) return;   // 数据还不完整，不画误导性的预览

        var samples = SamplePreviewPositions(p);
        var refH = p.RefHeightRatio * ImageHeight;   // 归一化高度（用图高当容器高的近似）
        canvas.StrokeColor = Color.FromRgba(255, 255, 255, 160);
        canvas.StrokeSize = 1f;
        foreach (var s in samples)
        {
            var k = PlacementMath.ScaleAt(p, s);
            var h = refH * k;
            var top = WorldToScreen(new Point(s.X * ImageWidth, s.Y * ImageHeight - h));
            var bot = WorldToScreen(new Point(s.X * ImageWidth, s.Y * ImageHeight));
            canvas.DrawLine((float)top.X, (float)top.Y, (float)bot.X, (float)bot.Y);
            canvas.DrawLine((float)top.X - 4, (float)top.Y, (float)top.X + 4, (float)top.Y);
            canvas.DrawLine((float)bot.X - 4, (float)bot.Y, (float)bot.X + 4, (float)bot.Y);
        }
    }

    private static List<NormPoint> SamplePreviewPositions(ScenePlacement p)
    {
        var list = new List<NormPoint>();
        const int n = 5;
        if (p.Landing == LandingKinds.Point && p.Points.Count > 0)
        {
            list.Add(p.Points[0]);
            return list;
        }
        if (p.Landing == LandingKinds.Linear && p.Lines.Count > 0)
        {
            foreach (var line in p.Lines)
            {
                var pts = PlacementMath.SamplePolyline(line, n);
                foreach (var q in pts) list.Add(q);
            }
            return list;
        }
        // 整图可站：沿下半部横向撒点
        for (int i = 0; i < n; i++)
        {
            var t = i / (double)(n - 1);
            list.Add(new NormPoint(0.1 + t * 0.8, 0.9));
        }
        return list;
    }

    private void DrawPlacement(ICanvas canvas)
    {
        var p = Placement;
        if (p is null) return;

        // 落脚点
        canvas.StrokeColor = Colors.White;
        canvas.StrokeSize = 1.5f;
        for (int i = 0; i < p.Points.Count; i++)
        {
            var s = WorldToScreen(ToPixels(p.Points[i]));
            canvas.FillColor = Color.FromArgb("#4FC3F7");
            canvas.FillCircle((float)s.X, (float)s.Y, 6f);
            canvas.DrawCircle((float)s.X, (float)s.Y, 6f);
        }

        // 折线：先画落脚线（绿系），再画缩放线（橙系，两条用深浅区分远近便于核对配对方向）
        DrawLineGroup(canvas, LineGroup.Landing);
        DrawLineGroup(canvas, LineGroup.Scale);

        // 正在拖拽创建的线（虚线预览）
        if (LinePreview is { } prev)
        {
            var a = WorldToScreen(prev.A);
            var b = WorldToScreen(prev.B);
            canvas.StrokeColor = Color.FromRgba(255, 255, 255, 200);
            canvas.StrokeSize = 2f;
            canvas.StrokeDashPattern = new[] { 5f, 4f };
            canvas.DrawLine((float)a.X, (float)a.Y, (float)b.X, (float)b.Y);
            canvas.StrokeDashPattern = null;
        }

        DrawSpheres(canvas);
    }

    private void DrawLineGroup(ICanvas canvas, LineGroup g)
    {
        var lines = LinesOf(g);
        for (int li = 0; li < lines.Count; li++)
        {
            var pts = lines[li].Points;
            if (pts.Count == 0) continue;

            var color = g == LineGroup.Landing
                ? Color.FromArgb("#81C784")
                : (li == 1 ? Color.FromArgb("#FF7043") : Color.FromArgb("#FFB74D"));
            canvas.StrokeColor = color;
            canvas.StrokeSize = g == LineGroup.Scale ? 3f : 2.5f;
            for (int i = 1; i < pts.Count; i++)
            {
                var a = WorldToScreen(ToPixels(pts[i - 1]));
                var b = WorldToScreen(ToPixels(pts[i]));
                canvas.DrawLine((float)a.X, (float)a.Y, (float)b.X, (float)b.Y);
            }
            for (int i = 0; i < pts.Count; i++)
            {
                var s = WorldToScreen(ToPixels(pts[i]));
                var active = DragVertex.Group == g && DragVertex.LineIndex == li && DragVertex.PointIndex == i;
                canvas.FillColor = active ? Color.FromArgb("#FFD54F") : Colors.White;
                canvas.FillCircle((float)s.X, (float)s.Y, active ? 6f : 4.5f);
            }
        }

        // 示例圆球（矢量圆，不用图片资源）
        DrawSpheres(canvas);
    }

    private void DrawSpheres(ICanvas canvas)
    {
        var p = Placement;
        if (p is null) return;
        for (int i = 0; i < p.Spheres.Count; i++)
        {
            var sp = p.Spheres[i];
            var c = WorldToScreen(SphereCenterPixels(sp));
            var r = (float)(sp.R * ImageWidth * Scale);
            var active = DragSphere == i || HoverSphere == i;
            canvas.FillColor = Color.FromRgba(255, 213, 79, active ? 90 : 50);
            canvas.FillCircle((float)c.X, (float)c.Y, r);
            canvas.StrokeColor = active ? Color.FromArgb("#FFD54F") : Color.FromArgb("#FFE082");
            canvas.StrokeSize = active ? 3f : 2f;
            canvas.DrawCircle((float)c.X, (float)c.Y, r);
            // 球心手柄
            canvas.FillColor = Colors.White;
            canvas.FillCircle((float)c.X, (float)c.Y, 4f);
            canvas.StrokeSize = 1.5f;
            canvas.StrokeColor = Colors.Black;
            canvas.DrawCircle((float)c.X, (float)c.Y, 4f);
            // 半径标注
            canvas.FontColor = Color.FromArgb("#FFE082");
            canvas.FontSize = 12;
            canvas.DrawString($"r={sp.R:0.###}", (float)c.X + r + 4, (float)c.Y - 8, 90, 16,
                HorizontalAlignment.Left, VerticalAlignment.Center);
        }
    }
}
