using Microsoft.Maui.Graphics;

namespace WarmAsBefore.Drawables;

/// <summary>
/// 成本图表：条形图（各模型对比）与折线图（按天趋势）。
///
/// 自绘而不是引第三方图表库：这里只需要两条轴、两组数据，且要跟着主题色走，
/// 自绘反而更好控（也避免再拖一个包进包）。
/// </summary>
public sealed class CostChartDrawable : IDrawable
{
    public enum ChartMode { Bars, Lines }

    public sealed record Bar(string Label, double Value, string ValueText);
    public sealed record Series(string Name, Color Color, IReadOnlyList<double> Values);

    public ChartMode Mode { get; set; } = ChartMode.Bars;

    /// <summary>条形图数据。</summary>
    public IReadOnlyList<Bar> Bars { get; set; } = Array.Empty<Bar>();

    /// <summary>折线图数据（各序列长度一致）。</summary>
    public IReadOnlyList<Series> SeriesList { get; set; } = Array.Empty<Series>();

    /// <summary>折线图的 X 轴标签（日期）。</summary>
    public IReadOnlyList<string> XLabels { get; set; } = Array.Empty<string>();

    /// <summary>Y 轴单位说明（如 "tok" / "元"）。</summary>
    public string Unit { get; set; } = "";

    /// <summary>无数据时的提示。</summary>
    public string EmptyHint { get; set; } = "还没有数据";

    private static readonly Color AxisColor = Color.FromArgb("#8A8A8A");
    private static readonly Color GridColor = Color.FromArgb("#338A8A8A");
    private static readonly Color BarColor = Color.FromArgb("#7BA7D7");
    private static readonly Color BarColorAlt = Color.FromArgb("#D79B7B");
    private static readonly Color LabelColor = Color.FromArgb("#6A6A6A");

    public void Draw(ICanvas canvas, RectF rect)
    {
        if (rect.Width <= 0 || rect.Height <= 0) return;

        var pad = new Thickness(44, 16, 12, 28);
        var plot = new RectF(
            rect.Left + (float)pad.Left,
            rect.Top + (float)pad.Top,
            Math.Max(1, rect.Width - (float)pad.Left - (float)pad.Right),
            Math.Max(1, rect.Height - (float)pad.Top - (float)pad.Bottom));

        if (Mode == ChartMode.Bars) DrawBars(canvas, plot);
        else DrawLines(canvas, plot);
    }

    private void DrawBars(ICanvas canvas, RectF plot)
    {
        DrawAxes(canvas, plot, Unit);

        var bars = Bars.Where(b => b.Value > 0).ToList();
        if (bars.Count == 0)
        {
            DrawEmpty(canvas, plot);
            return;
        }

        var max = bars.Max(b => b.Value);
        var slot = plot.Width / bars.Count;
        var barW = Math.Min(slot * 0.55f, 46);

        canvas.FontSize = 10;
        for (var i = 0; i < bars.Count; i++)
        {
            var b = bars[i];
            var h = (float)(b.Value / max * (plot.Height - 16));
            var x = plot.Left + slot * i + (slot - barW) / 2;
            var y = plot.Bottom - h;

            canvas.FillColor = i % 2 == 0 ? BarColor : BarColorAlt;
            canvas.FillRoundedRectangle(x, y, barW, Math.Max(1, h), 3);

            // 数值标在柱顶，超出就贴近柱顶内侧
            canvas.FontColor = LabelColor;
            var label = b.ValueText;
            canvas.DrawString(label, x - 8, y - 13, barW + 16, 13,
                HorizontalAlignment.Center, VerticalAlignment.Center);

            // 模型名太长就截断，免得互相压字
            var name = b.Label.Length > 10 ? b.Label[..10] + "…" : b.Label;
            canvas.DrawString(name, plot.Left + slot * i, plot.Bottom + 3, slot, 12,
                HorizontalAlignment.Center, VerticalAlignment.Top);
        }
    }

    private void DrawLines(ICanvas canvas, RectF plot)
    {
        DrawAxes(canvas, plot, Unit);

        var series = SeriesList.Where(s => s.Values.Count > 1).ToList();
        var maxValue = series.Count == 0 ? 0 : series.Max(s => s.Values.Max());
        if (series.Count == 0 || maxValue <= 0)
        {
            DrawEmpty(canvas, plot);
            return;
        }

        // Y 轴刻度（4 档）
        canvas.FontSize = 9;
        canvas.FontColor = LabelColor;
        for (var i = 0; i <= 4; i++)
        {
            var v = maxValue * i / 4.0;
            var y = plot.Bottom - (float)(plot.Height * i / 4.0);
            canvas.StrokeColor = GridColor;
            canvas.StrokeSize = 0.5f;
            canvas.DrawLine(plot.Left, y, plot.Right, y);
            canvas.DrawString(Format(v), plot.Left - 42, y - 6, 38, 12,
                HorizontalAlignment.Right, VerticalAlignment.Center);
        }

        var count = series[0].Values.Count;
        var stepX = plot.Width / Math.Max(1, count - 1);

        foreach (var s in series)
        {
            var path = new PathF();
            for (var i = 0; i < count; i++)
            {
                var x = plot.Left + stepX * i;
                var y = plot.Bottom - (float)(s.Values[i] / maxValue * plot.Height);
                if (i == 0) path.MoveTo(x, y); else path.LineTo(x, y);
            }
            canvas.StrokeColor = s.Color;
            canvas.StrokeSize = 2;
            canvas.DrawPath(path);

            // 数据点：少的时候画出来，多的时候省掉免得太乱
            if (count <= 16)
            {
                canvas.FillColor = s.Color;
                for (var i = 0; i < count; i++)
                {
                    var x = plot.Left + stepX * i;
                    var y = plot.Bottom - (float)(s.Values[i] / maxValue * plot.Height);
                    canvas.FillCircle(x, y, 2.5f);
                }
            }
        }

        // X 轴标签：隔一个画一个，防止挤在一起
        canvas.FontSize = 9;
        canvas.FontColor = LabelColor;
        for (var i = 0; i < XLabels.Count && i < count; i++)
        {
            if (count > 8 && i % 2 != 0) continue;
            canvas.DrawString(XLabels[i], plot.Left + stepX * i - 16, plot.Bottom + 3, 32, 12,
                HorizontalAlignment.Center, VerticalAlignment.Top);
        }

        // 图例
        var lx = plot.Left + 4;
        foreach (var s in series)
        {
            canvas.FillColor = s.Color;
            canvas.FillRectangle(lx, plot.Top + 2, 8, 8);
            canvas.FontColor = LabelColor;
            canvas.DrawString(s.Name, lx + 11, plot.Top - 4, 90, 16, HorizontalAlignment.Left, VerticalAlignment.Center);
            lx += 14 + s.Name.Length * 11;
        }
    }

    private static void DrawAxes(ICanvas canvas, RectF plot, string unit)
    {
        canvas.StrokeColor = AxisColor;
        canvas.StrokeSize = 1;
        canvas.DrawLine(plot.Left, plot.Top, plot.Left, plot.Bottom);
        canvas.DrawLine(plot.Left, plot.Bottom, plot.Right, plot.Bottom);

        if (!string.IsNullOrEmpty(unit))
        {
            canvas.FontColor = LabelColor;
            canvas.FontSize = 9;
            canvas.DrawString(unit, plot.Left - 42, plot.Top - 12, 40, 12,
                HorizontalAlignment.Right, VerticalAlignment.Center);
        }
    }

    private void DrawEmpty(ICanvas canvas, RectF plot)
    {
        canvas.FontColor = LabelColor;
        canvas.FontSize = 11;
        canvas.DrawString(EmptyHint, plot.Left, plot.Top, plot.Width, plot.Height,
            HorizontalAlignment.Center, VerticalAlignment.Center);
    }

    /// <summary>把大数字缩写成 k / M，避免轴标签过长。</summary>
    private static string Format(double v) => v switch
    {
        >= 1_000_000 => (v / 1_000_000).ToString("0.#") + "M",
        >= 1_000 => (v / 1_000).ToString("0.#") + "k",
        _ => v.ToString("0.#")
    };
}
