using Microsoft.Maui.Graphics;

namespace WarmAsBefore.Drawables;

/// <summary>
/// 调色页的饱和度 / 明度方块（SV 方）。
///
/// 叠加顺序就是颜色本身的两步：先把白铺成「纯色相 → 白」的水平渐变（横轴 = 饱和度），
/// 再叠一层「透明 → 黑」的竖向渐变（纵轴 = 明度）。两刀恰好是 HSV 方块的构成方式，
/// 所以准星落在哪儿，取出来就是那个点的 S/V，不需要任何反查表。
///
/// 准星必须同时在浅色与深色区可见：外圈黑、内圈白，两道叠着画 ——
/// 白在浅色（低饱和 / 高明度）那半边看不见，黑在深色那半边看不见，两道一起才全程有反差。
/// </summary>
public sealed class ColorFieldDrawable : IDrawable
{
    /// <summary>当前色相上的纯色（S=1、V=1）。由调色页每次变更时喂进来。</summary>
    public Color HueColor { get; set; } = Colors.Red;

    /// <summary>横轴位置，0..1。</summary>
    public double Saturation { get; set; } = 1;

    /// <summary>纵轴位置（0 = 底边 = 黑）。</summary>
    public double Value { get; set; } = 1;

    private const float CornerRadius = 10f;
    private const float MarkerRadius = 8f;

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        if (dirtyRect.Width <= 0 || dirtyRect.Height <= 0) return;
        canvas.Antialias = true;

        var rect = new RectF(dirtyRect.X, dirtyRect.Y, dirtyRect.Width, dirtyRect.Height);

        // 圆角裁一次：两片渐变与准星都落在同一块圆角里，边缘不会露出直角。
        var shape = new PathF();
        shape.AppendRoundedRectangle(rect, CornerRadius);

        canvas.SaveState();
        canvas.ClipPath(shape, WindingMode.NonZero);

        // 横轴：白 → 纯色相
        canvas.SetFillPaint(
            new LinearGradientPaint(
                new[]
                {
                    new PaintGradientStop(0f, Colors.White),
                    new PaintGradientStop(1f, HueColor),
                },
                new Point(0, 0), new Point(1, 0)),
            rect);
        canvas.FillRectangle(rect);

        // 纵轴：透明 → 黑
        canvas.SetFillPaint(
            new LinearGradientPaint(
                new[]
                {
                    new PaintGradientStop(0f, Colors.Transparent),
                    new PaintGradientStop(1f, Colors.Black),
                },
                new Point(0, 0), new Point(0, 1)),
            rect);
        canvas.FillRectangle(rect);

        var cx = (float)(rect.X + Math.Clamp(Saturation, 0, 1) * rect.Width);
        var cy = (float)(rect.Y + (1 - Math.Clamp(Value, 0, 1)) * rect.Height);
        DrawMarker(canvas, cx, cy, Math.Min(rect.Width, rect.Height));

        canvas.RestoreState();
    }

    /// <summary>黑一圈 + 白一圈的准星：十字线各露在环外一小截，指着点本身而不是环。</summary>
    private static void DrawMarker(ICanvas canvas, float cx, float cy, float side)
    {
        var r = Math.Min(MarkerRadius, Math.Max(3f, side * 0.06f));
        var arm = r + 5f;
        var dark = Colors.Black.WithAlpha(0.62f);

        canvas.StrokeColor = dark;
        canvas.StrokeSize = 3.4f;
        canvas.DrawLine(cx - arm, cy, cx + arm, cy);
        canvas.DrawLine(cx, cy - arm, cx, cy + arm);
        canvas.DrawCircle(cx, cy, r);

        canvas.StrokeColor = Colors.White;
        canvas.StrokeSize = 1.6f;
        canvas.DrawLine(cx - arm, cy, cx + arm, cy);
        canvas.DrawLine(cx, cy - arm, cx, cy + arm);
        canvas.DrawCircle(cx, cy, r);
    }
}
