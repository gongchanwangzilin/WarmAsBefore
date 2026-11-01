using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using WarmAsBefore.Drawables;
using WarmAsBefore.Services;

namespace WarmAsBefore.Controls;

/// <summary>
/// 右上角那块小地图：一颗自绘画布 + 点按命中。
///
/// 「画什么」全在 <see cref="MinimapDrawable"/> 里，这个壳只管三件事：把 VM 的取材送进绘制器、
/// 在 60pt 方片与展开面板之间切换画法、把落在 POI 上的点按换成一个场景 Id 抛给外面。
///
/// 方片态请配 <c>InputTransparent="True"</c>：那时它只是一枚会变的图章，点击归外面那颗按钮，
/// 别让画布把点按吞掉（GraphicsView 在 Windows 上是原生视图，会拦掉父级的手势）。
/// </summary>
public sealed class MinimapView : ContentView
{
    private readonly GraphicsView _surface;
    private PointF _downAt;

    public MinimapDrawable Drawable { get; } = new();

    /// <summary>点在了一个 POI 上（参数 = 场景 Id）。</summary>
    public event Action<string>? PoiTapped;

    /// <summary>点在了画布的空白处（展开态没有别的可点，留给外面决定要不要收回）。</summary>
    public event Action? SurfaceTapped;

    public MinimapView()
    {
        _surface = new GraphicsView
        {
            Drawable = Drawable,
            BackgroundColor = Colors.Transparent,
        };
        _surface.StartInteraction += (_, e) => _downAt = FirstTouch(e);
        _surface.EndInteraction += OnEnd;
        Content = _surface;
    }

    public static readonly BindableProperty PlanProperty = BindableProperty.Create(
        nameof(Plan), typeof(MinimapPlan), typeof(MinimapView),
        propertyChanged: (bindable, _, value) =>
        {
            var view = (MinimapView)bindable;
            view.Drawable.Plan = (MinimapPlan?)value;
            view.Refresh();
        });

    /// <summary>这一帧画什么（null = 只有纸底，什么都还没摆上来）。</summary>
    public MinimapPlan? Plan
    {
        get => (MinimapPlan?)GetValue(PlanProperty);
        set => SetValue(PlanProperty, value);
    }

    public static readonly BindableProperty CompactProperty = BindableProperty.Create(
        nameof(Compact), typeof(bool), typeof(MinimapView), false,
        propertyChanged: (bindable, _, value) =>
        {
            var view = (MinimapView)bindable;
            view.Drawable.Compact = (bool)value;
            view.Refresh();
        });

    /// <summary>方片档：只画形、不画字。</summary>
    public bool Compact
    {
        get => (bool)GetValue(CompactProperty);
        set => SetValue(CompactProperty, value);
    }

    public void Refresh() => _surface.Invalidate();

    private void OnEnd(object? sender, TouchEventArgs e)
    {
        var at = FirstTouch(e);
        var dx = at.X - _downAt.X;
        var dy = at.Y - _downAt.Y;
        // 按下与松开隔了十几像素是「抹了一下」，不算点：误触不该把人送进大地图。
        if (dx * dx + dy * dy > 144) return;
        var hit = Drawable.HitPoi(new Point(at.X, at.Y));
        if (hit is not null) PoiTapped?.Invoke(hit);
        else SurfaceTapped?.Invoke();
    }

    /// <summary>MAUI 给 GraphicsView 的触点是一列 PointF（鼠标只有一个），取第一个就是这里要的那个。</summary>
    private static PointF FirstTouch(TouchEventArgs e) => e.Touches.FirstOrDefault();
}
