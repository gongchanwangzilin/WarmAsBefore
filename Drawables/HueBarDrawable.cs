using Microsoft.Maui.Graphics;

namespace WarmAsBefore.Drawables;

/// <summary>
/// 调色页的色相条：一条水平渐变（0° 在左、360° 在右，七段色标）加一枚可拖的游标。
///
/// 七段而不是两端插值：只有红—蓝两端的渐变在中间会绕开绿，色相条就指不到绿色。
/// 游标与 SV 方的准星同一套画法（黑一圈 + 白一圈），在条子上任何色相处都看得清。
/// </summary>
public sealed class HueBarDrawable : IDrawable
{
    /// <summary>当前色相，0..360。</summary>
    public double Hue { get; set; }

    /// <summary>七段色标：红 / 黄 / 绿 / 青 / 蓝 / 洋红 / 红。</summary>
    private static readonly Color[] Stops =
    {
        Color.FromRgb(255, 0, 0),
        Color.FromRgb(255, 255, 0),
        Color.FromRgb(0, 255, 0),
        Color.FromRgb(0, 255, 255),
        Color.FromRgb(0, 0, 255),
        Color.FromRgb(255, 0, 255),
        Color.FromRgb(255, 0, 0),
    };

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        if (dirtyRect.Width <= 0 || dirtyRect.Height <= 0) return;
        canvas.Antialias = true;

        var rect = new RectF(dirtyRect.X, dirtyRect.Y, dirtyRect.Width, dirtyRect.Height);
        var radius = dirtyRect.Height / 2;

        var shape = new PathF();
        shape.AppendRoundedRectangle(rect, radius);

        canvas.SaveState();
        canvas.ClipPath(shape, WindingMode.NonZero);
        var stops = new PaintGradientStop[Stops.Length];
        for (var i = 0; i < Stops.Length; i++)
            stops[i] = new PaintGradientStop((float)i / (Stops.Length - 1), Stops[i]);
        canvas.SetFillPaint(new LinearGradientPaint(stops, new Point(0, 0), new Point(1, 0)), rect);
        canvas.FillRectangle(rect);
        canvas.RestoreState();

        // 游标：贴着条子的两端时不越界（半径留出余量），否则半个环会被裁掉。
        var cy = rect.Y + rect.Height / 2;
        var r = Math.Min(9f, rect.Height / 2 + 1f);
        var x = (float)Math.Clamp(rect.X + Hue / 360 * rect.Width, rect.X + r, rect.Right - r);

        canvas.StrokeColor = Colors.Black.WithAlpha(0.62f);
        canvas.StrokeSize = 4.4f;
        canvas.DrawCircle(x, cy, r);
        canvas.StrokeColor = Colors.White;
        canvas.StrokeSize = 2.2f;
        canvas.DrawCircle(x, cy, r);
    }
}
