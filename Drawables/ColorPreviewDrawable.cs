using Microsoft.Maui.Graphics;

namespace WarmAsBefore.Drawables;

/// <summary>
/// 调色页顶部的实时预览：这块色当**背景**时是什么样，而不是单独一块色卡。
///
/// 只画一块纯色说明不了任何事 —— 这个软件的地面永远隔着一层糊底与玻璃出现在画面里。
/// 所以预览里先铺底色，再压几团虚边光斑（糊底的「糊」），最后摆一张半透明的卡面
/// 与几行字条：用户看到的是「换成这个色以后，卡片透出来是什么颜色」，而不是一块色块。
///
/// 卡面用半透明的白，不挑主题：底色深浅都由它自己那层白来拉开，深色底上读起来是灰玻璃，
/// 浅色底上是奶白玻璃 —— 两种都像真的卡片，也都是同一条绘制路径。
/// </summary>
public sealed class ColorPreviewDrawable : IDrawable
{
    /// <summary>要预览的底色。</summary>
    public Color Fill { get; set; } = Colors.White;

    private const float CornerRadius = 16f;

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        if (dirtyRect.Width <= 0 || dirtyRect.Height <= 0) return;
        canvas.Antialias = true;

        var rect = new RectF(dirtyRect.X, dirtyRect.Y, dirtyRect.Width, dirtyRect.Height);
        var shape = new PathF();
        shape.AppendRoundedRectangle(rect, CornerRadius);

        canvas.SaveState();
        canvas.ClipPath(shape, WindingMode.NonZero);

        // 地面
        canvas.FillColor = Fill;
        canvas.FillRectangle(rect);

        // 糊底的「糊」：大小不一的柔边光斑，位置按画布比例摆，窗口宽窄都不跑版。
        var w = rect.Width;
        var h = rect.Height;
        Blob(canvas, rect.X + w * 0.18f, rect.Y + h * 0.82f, h * 0.62f, Colors.White, 0.13f);
        Blob(canvas, rect.X + w * 0.74f, rect.Y + h * 0.16f, h * 0.48f, Colors.White, 0.09f);
        Blob(canvas, rect.X + w * 0.62f, rect.Y + h * 0.92f, h * 0.44f, Colors.Black, 0.10f);

        // 卡面：左上一个角，右边留出地面 —— 半透明要有「背后还有东西」可对比。
        var cardW = Math.Min(w * 0.66f, 420f);
        var cardH = Math.Max(58f, h * 0.52f);
        var card = new RectF(rect.X + 16f, rect.Y + (h - cardH) / 2, cardW, cardH);

        canvas.FillColor = Colors.White.WithAlpha(0.52f);
        canvas.FillRoundedRectangle(card, 12f);
        canvas.StrokeColor = Colors.White.WithAlpha(0.55f);
        canvas.StrokeSize = 1f;
        canvas.DrawRoundedRectangle(card, 12f);

        // 卡面上的字条：三行深灰，长短不同，读起来像一段说明而不是色块。
        var ink = Color.FromRgb(0.09f, 0.09f, 0.09f).WithAlpha(0.42f);
        var lineH = Math.Max(4f, cardH * 0.09f);
        var lx = card.X + 14f;
        var ly = card.Y + cardH * 0.28f;
        Bar(canvas, lx, ly, cardW * 0.44f, lineH, ink);
        Bar(canvas, lx, ly + lineH * 2.6f, cardW * 0.62f, lineH, ink.WithAlpha(0.62f));
        Bar(canvas, lx, ly + lineH * 5.2f, cardW * 0.34f, lineH, ink.WithAlpha(0.42f));

        // 卡角一颗强调点：这张卡是「软件里的一张卡」，不是一块色纸。
        canvas.FillColor = Colors.White.WithAlpha(0.9f);
        canvas.FillCircle(card.Right - 16f, card.Y + 16f, 5f);

        canvas.RestoreState();
    }

    private static void Blob(ICanvas canvas, float x, float y, float r, Color color, float alpha)
    {
        if (r <= 0 || alpha <= 0) return;
        canvas.FillColor = color.WithAlpha(alpha);
        canvas.FillCircle(x, y, r);
    }

    private static void Bar(ICanvas canvas, float x, float y, float w, float h, Color color)
    {
        if (w <= 0 || h <= 0) return;
        canvas.FillColor = color;
        canvas.FillRoundedRectangle(x, y, w, h, h / 2);
    }
}
