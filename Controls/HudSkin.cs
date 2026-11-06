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

    /// <summary>卡面：HUD 世界右上/左下各切一刀；其它世界是 8px 圆角。</summary>
    public static IShape PanelCardShape => Path_Shape(14, 8);

    /// <summary>井面：切角更小，读起来是「内嵌的槽」。其它世界 8px 圆角。</summary>
    public static IShape PanelWellShape => Path_Shape(9, 6);

    /// <summary>对话/弹窗：切角更大，主按钮与对话框共用一个「被削过」的感觉。</summary>
    public static IShape PanelDialogShape => Path_Shape(18, 8);

    /// <summary>
    /// 切角面板：切角用**绝对像素**（大卡上一刀 14px 才读得成模具，按比例切在大屏上会切掉一整角）。
    /// 几何必须由 <see cref="HudPanelShape.PathForBounds"/> 按实际边界现画 ——
    /// 早期版本返回一颗固定 0–100 单位的 Path，Border 把它当成固有尺寸，整块卡会塌成 100px 的小方块。
    /// </summary>
    private static IShape Path_Shape(double cut, double roundRadius)
    {
        if (!IsActive)
            return new RoundRectangle { CornerRadius = new CornerRadius(roundRadius) };

        return new HudPanelShape { CutSize = cut };
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

/// <summary>
/// HUD 切角面板形状：只实现 <see cref="IShape"/>，几何按控件**实际边界**现画。
///
/// 为什么不是继承 Shape：Shape 的测量契约要求 GetPath() 返回一遍固定坐标，
/// Border 会拿它当固有尺寸；早期版本用 0–100 单位的 Path 当 StrokeShape，
/// 标题页那块填满型 ShellFrame 就塌成了 100px 的小方块（已踩实）。
/// IShape 只回答「给我这个矩形，边线怎么走」，不参与测量 —— Border 的布局完全由内容与
/// Horizontal/VerticalOptions 决定。
/// </summary>
public sealed class HudPanelShape : IShape
{
    /// <summary>切角边长（像素）：右上与左下各切一刀。</summary>
    public double CutSize { get; set; } = 14;

    public PathF PathForBounds(Rect rect)
    {
        var x = (float)rect.X;
        var y = (float)rect.Y;
        var w = (float)Math.Max(0, rect.Width);
        var h = (float)Math.Max(0, rect.Height);
        var cut = (float)Math.Clamp(CutSize, 0, Math.Min(w, h) * 0.4);
        var path = new PathF();
        path.MoveTo(x, y);
        path.LineTo(x + w - cut, y);        // 顶边，到右上切角起点
        path.LineTo(x + w, y + cut);        // 右上斜切
        path.LineTo(x + w, y + h);          // 右边
        path.LineTo(x + cut, y + h);        // 底边，到左下切角起点
        path.LineTo(x, y + h - cut);        // 左下斜切
        path.Close();
        return path;
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
