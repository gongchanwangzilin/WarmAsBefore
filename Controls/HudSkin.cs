using Microsoft.Maui.Controls.Shapes;
using WarmAsBefore.DesignSystem.Worlds;

namespace WarmAsBefore.Controls;

/// <summary>
/// 终末地 HUD 的外观原子：切角面板、角标括号、斜纹条。
///
/// 为什么不能只换配色：HUD 的辨识度来自形状与线 —— 切掉一个角的矩形、压在角上的细括号、
/// 边缘那条 45° 斜纹，这三样才是「终端界面」；把圆角调小、颜色调深，读起来仍然是同一套卡片。
/// 这里的形状全部挂在 <see cref="StyleWorld.Endfield"/> 上：
/// 其它世界返回与原先一致的圆角矩形 / 隐藏，所以页面里统一写
/// <c>StrokeShape="{Binding Source={x:Static controls:HudSkin.PanelCardShape}}"</c> 也不会改变原有观感。
/// </summary>
public static class HudSkin
{
    /// <summary>当前是不是 HUD 世界（每次现算：换世界后新建的元素立刻用对形状）。</summary>
    public static bool IsActive
    {
        get
        {
            try { return StyleWorldTokens.World == StyleWorld.Endfield; }
            catch { return false; }
        }
    }

    /// <summary>强调色（角标、斜纹、状态线都用它）：从当前资源取 Signal，取不到用终末地黄。</summary>
    public static Color Accent
    {
        get
        {
            try
            {
                if (Application.Current?.Resources.TryGetValue("Signal", out var v) == true && v is Color c)
                    return c;
            }
            catch { }
            return Color.FromArgb("#FAD41F");
        }
    }

    // ── 三种面板形状（页面里直接绑到 StrokeShape）─────────────────────────

    /// <summary>卡面：HUD 世界右上/左下各切一刀；其它世界是 12px 圆角。</summary>
    public static Shape PanelCardShape => Path_Shape(0.16, 12);

    /// <summary>井面：切角更小，读起来是「内嵌的槽」。其它世界 10px 圆角。</summary>
    public static Shape PanelWellShape => Path_Shape(0.10, 10);

    /// <summary>对话/弹窗：切角更大，主按钮与对话框共用一个「被削过」的感觉。</summary>
    public static Shape PanelDialogShape => Path_Shape(0.18, 16);

    /// <summary>
    /// 切角矩形：PathF 走「设计单位」坐标，Border 会把它等比拉伸到自身尺寸。
    /// 切角用比例（k）表示而不是绝对像素 —— 一张大卡与一颗小芯片切掉同样的比例才像同一套模具。
    /// </summary>
    private static Shape Path_Shape(double k, double roundRadius)
    {
        if (!IsActive)
            return new RoundRectangle { CornerRadius = new CornerRadius(roundRadius) };

        const double side = 100;
        var cut = side * Math.Clamp(k, 0.04, 0.30);
        return new Microsoft.Maui.Controls.Shapes.Path { Data = new PathGeometry
        {
            Figures = new PathFigureCollection
            {
                new PathFigure
                {
                    StartPoint = new Point(0, 0),
                    IsClosed = true,
                    Segments = new PathSegmentCollection
                    {
                        new LineSegment(new Point(side - cut, 0)),
                        new LineSegment(new Point(side, cut)),
                        new LineSegment(new Point(side, side)),
                        new LineSegment(new Point(cut, side)),
                        new LineSegment(new Point(0, side - cut)),
                    }
                }
            }
        } };
    }

    /// <summary>HUD 四角括号（只画线、不挡触摸）。非 HUD 世界隐藏。</summary>
    public static GraphicsView CornerBrackets(double size = 9, double stroke = 1.4)
    {
        var view = new GraphicsView { InputTransparent = true, IsVisible = IsActive, HeightRequest = 14 };
        view.Drawable = new CornerBracketDrawable(size, stroke);
        return view;
    }

    /// <summary>HUD 斜纹带（顶栏/卡片底部那条 45° 斜纹）。非 HUD 世界隐藏。</summary>
    public static GraphicsView Hatch(double height = 6, float gap = 5, float line = 1.2f)
    {
        var view = new GraphicsView { InputTransparent = true, IsVisible = IsActive, HeightRequest = height };
        view.Drawable = new HudHatchDrawable(gap, line);
        return view;
    }
}

/// <summary>角标括号：四条边的角各两根短线。</summary>
public sealed class CornerBracketDrawable : IDrawable
{
    private double _stroke;

    /// <summary>XAML 里要能无参 new：尺寸/粗细走属性 setter（Size / StrokeThickness）。</summary>
    public CornerBracketDrawable() : this(18, 1.8) { }

    public CornerBracketDrawable(double size, double stroke)
    {
        Size = size;
        _stroke = stroke;
    }

    /// <summary>角标线粗（XAML 可设；默认 1.8）。</summary>
    public double StrokeThickness { get => _stroke; set => _ = value; }

    /// <summary>每个角两根短线的长度（XAML 可设）。</summary>
    public double Size { get; set; }

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        canvas.StrokeColor = HudSkin.Accent;
        canvas.StrokeSize = (float)_stroke;
        canvas.StrokeLineCap = LineCap.Butt;
        var m = (float)Math.Min(Size, Math.Min(dirtyRect.Width, dirtyRect.Height) / 2);
        var r = dirtyRect.Right;
        var b = dirtyRect.Bottom;
        var l = dirtyRect.Left;
        var t = dirtyRect.Top;

        canvas.DrawLine(l, t, l + m, t);            // 左上
        canvas.DrawLine(l, t, l, t + m);
        canvas.DrawLine(r - m, t, r, t);            // 右上
        canvas.DrawLine(r, t, r, t + m);
        canvas.DrawLine(l, b - m, l, b);            // 左下
        canvas.DrawLine(l, b, l + m, b);
        canvas.DrawLine(r, b - m, r, b);            // 右下
        canvas.DrawLine(r - m, b, r, b);
    }
}

/// <summary>斜纹带：45° 细斜线压在一条淡色带上（HUD 的「警示带」质感）。</summary>
public sealed class HudHatchDrawable : IDrawable
{
    private float _gap;
    private float _line;

    /// <summary>XAML 里要能无参 new：间距/粗细走属性 setter（Gap / Thickness）。</summary>
    public HudHatchDrawable() : this(7, 1.2f) { }

    public HudHatchDrawable(float gap, float line)
    {
        _gap = Math.Max(3, gap);
        _line = line;
    }

    /// <summary>斜线间距（XAML 可设）。</summary>
    public float Gap { get => _gap; set => _gap = Math.Max(3, value); }

    /// <summary>斜线粗细（XAML 可设）。</summary>
    public float Thickness { get => _line; set => _line = value; }

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        var ink = HudSkin.Accent;
        canvas.SaveState();
        canvas.ClipRectangle(dirtyRect);
        canvas.FillColor = new Color(ink.Red, ink.Green, ink.Blue, 0.10f);
        canvas.FillRectangle(dirtyRect);

        canvas.StrokeColor = ink.WithAlpha(0.55f);
        canvas.StrokeSize = _line;
        for (var x = -dirtyRect.Height; x < dirtyRect.Width; x += _gap)
            canvas.DrawLine(dirtyRect.Left + x, dirtyRect.Bottom, dirtyRect.Left + x + dirtyRect.Height, dirtyRect.Top);
        canvas.RestoreState();
    }
}
