using Microsoft.Maui.Graphics;

namespace WarmAsBefore.Drawables;

/// <summary>三维空间里的一个点：坐标是这条记忆的向量分块均值，词语是它自己的说法。</summary>
public sealed record VectorPoint(string Id, string Label, double X, double Y, double Z, double Weight);

/// <summary>
/// 向量空间的二维投影：一个三维坐标系 + 落在里面的点。
///
/// 为什么不用真 3D 引擎：这台机器上 Evergine/DX11 那条路会在建链后直接把进程带走
/// （事件日志 0xc000027b / CoreMessagingXP，WER 里裹着 combase 的 E_POINTER），
/// 托管侧兜不住。而「显示一个坐标系和一些点」本来不需要引擎 —— 一次透视投影就够了，
/// 而且我完全控制它：画得对不对、算得准不准，都能验证。
///
/// 投影是标准的两步旋转（先绕 Y 偏航，再绕 X 俯仰），深度用来排序与压暗：
/// 远的点先画、更淡，近的后画、更亮，眼睛就能读出前后关系。
/// </summary>
public sealed class VectorCloudDrawable : IDrawable
{
    private const float Pad = 46f;

    /// <summary>点集。为空时只画坐标系和一句「还没有可投影的向量」。</summary>
    public IReadOnlyList<VectorPoint> Points { get; set; } = Array.Empty<VectorPoint>();

    /// <summary>三根轴的名字：它们是向量分块均值，不是人工指定的语义，所以名字必须照实写。</summary>
    public string AxisX { get; set; } = "分块 Ⅰ";
    public string AxisY { get; set; } = "分块 Ⅱ";
    public string AxisZ { get; set; } = "分块 Ⅲ";

    /// <summary>被选中的那个点：只有它会把坐标写在点上，其余靠右侧列表读。</summary>
    public string? SelectedId { get; set; }

    /// <summary>0..1 缓慢自转相位，让静止的球体有「活着」的感觉；由 VM 的钟推进。</summary>
    public double Phase { get; set; }

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        var w = dirtyRect.Width;
        var h = dirtyRect.Height;
        if (w < 40 || h < 40) return;

        var cx = w / 2f;
        var cy = h / 2f + 6f;
        var scale = MathF.Min(w - Pad * 2, h - Pad * 2) * 0.42f;

        // 偏航带一点缓慢摆动，幅度很小：看得出有深度，不至于让人盯着晃。
        var yaw = -0.52 + 0.10 * Math.Sin(Phase * Math.PI * 2);
        const double pitch = 0.34;

        var ink = GraphPalette.Unknown;
        var faint = ink.WithAlpha(0.20f);

        (float X, float Y, double Depth) Project(double x, double y, double z)
        {
            var xr = x * Math.Cos(yaw) + z * Math.Sin(yaw);
            var zr = -x * Math.Sin(yaw) + z * Math.Cos(yaw);
            var yr = y * Math.Cos(pitch) - zr * Math.Sin(pitch);
            var zr2 = y * Math.Sin(pitch) + zr * Math.Cos(pitch);
            return ((float)(cx + xr * scale), (float)(cy - yr * scale), zr2);
        }

        // 底面网格：给高度一个参照，没有它点会像浮在真空里。
        canvas.StrokeColor = faint.WithAlpha(0.10f);
        canvas.StrokeSize = 1f;
        for (var i = -2; i <= 2; i++)
        {
            var t = i / 2.0;
            var (ax, ay, _) = Project(t, -1, -1);
            var (bx, by, _) = Project(t, -1, 1);
            canvas.DrawLine(ax, ay, bx, by);
            var (cx2, cy2, _) = Project(-1, -1, t);
            var (dx2, dy2, _) = Project(1, -1, t);
            canvas.DrawLine(cx2, cy2, dx2, dy2);
        }

        // 三根轴：从原点指向 +1，末端写轴名。
        DrawAxis(canvas, Project, 1, 0, 0, AxisX, ink);
        DrawAxis(canvas, Project, 0, 1, 0, AxisY, ink);
        DrawAxis(canvas, Project, 0, 0, 1, AxisZ, ink);

        var (ox, oy, _) = Project(0, 0, 0);
        canvas.FillColor = ink.WithAlpha(0.55f);
        canvas.FillCircle(ox, oy, 2.4f);

        if (Points.Count == 0)
        {
            canvas.Font = Microsoft.Maui.Graphics.Font.Default;
            canvas.FontSize = 11.5f;
            canvas.FontColor = ink.WithAlpha(0.55f);
            canvas.DrawString("还没有可投影的向量。", Pad, h - Pad + 10, w - Pad * 2, 20,
                HorizontalAlignment.Left, VerticalAlignment.Center);
            return;
        }

        // 深度排序：远的先画，近的后画，重叠时近的压在上面。
        var ordered = Points
            .Select(p => (P: p, S: Project(p.X, p.Y, p.Z)))
            .OrderBy(t => t.S.Depth)
            .ToList();

        foreach (var (p, s) in ordered)
        {
            var near = (s.Depth + 1.6) / 3.2;           // 0 远 1 近
            var alpha = (float)(0.24 + 0.62 * Math.Clamp(near, 0, 1));
            var r = (float)(2.6 + 3.2 * Math.Clamp(p.Weight, 0, 1));

            canvas.FillColor = GraphPalette.Associated.WithAlpha(alpha * 0.35f);
            canvas.FillCircle(s.X, s.Y, r + 3f);
            canvas.FillColor = GraphPalette.Tag.WithAlpha(alpha);
            canvas.FillCircle(s.X, s.Y, r);

            var isSelected = SelectedId is not null && string.Equals(p.Id, SelectedId, StringComparison.Ordinal);
            if (!isSelected && p.Weight < 0.72) continue;

            canvas.Font = Microsoft.Maui.Graphics.Font.Default;
            canvas.FontSize = 10.5f;
            canvas.FontColor = ink.WithAlpha(Math.Max(0.5f, alpha));
            var text = isSelected
                ? $"{p.Label} ({p.X:0.00}, {p.Y:0.00}, {p.Z:0.00})"
                : p.Label;
            canvas.DrawString(text, s.X + r + 3f, s.Y - 8f, 220, 16,
                HorizontalAlignment.Left, VerticalAlignment.Center);
        }
    }

    private static void DrawAxis(
        ICanvas canvas,
        Func<double, double, double, (float X, float Y, double Depth)> project,
        double x, double y, double z,
        string label, Color ink)
    {
        var (ox, oy, _) = project(0, 0, 0);
        var (tx, ty, _) = project(x, y, z);
        canvas.StrokeColor = ink.WithAlpha(0.38f);
        canvas.StrokeSize = 1.2f;
        canvas.DrawLine(ox, oy, tx, ty);

        canvas.Font = Microsoft.Maui.Graphics.Font.Default;
        canvas.FontSize = 10f;
        canvas.FontColor = ink.WithAlpha(0.6f);
        canvas.DrawString(label, tx + 4f, ty - 8f, 90, 16,
            HorizontalAlignment.Left, VerticalAlignment.Center);
    }
}
