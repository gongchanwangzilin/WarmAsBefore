namespace WarmAsBefore.Controls;

/// <summary>
/// 玻璃效果叠加层（覆盖整页，不拦截输入）。
/// 磨砂 = 半透明磨砂；毛玻璃 = 磨砂的高级版（半透明叠加层 + 边缘高光）。
/// 说明：真实背景模糊需要「层后内容」可被采样，但页面背景为不透明奶油色，整页模糊会把文字一并糊掉，
/// 故改为轻透叠层模拟磨砂质感，保证内容清晰可读。
/// </summary>
public partial class GlassOverlay : ContentView
{
    public static readonly BindableProperty FrostProperty =
        BindableProperty.Create(nameof(Frost), typeof(bool), typeof(GlassOverlay), false,
            propertyChanged: (b, _, _) => ((GlassOverlay)b).Refresh());
    public static readonly BindableProperty GlassProperty =
        BindableProperty.Create(nameof(Glass), typeof(bool), typeof(GlassOverlay), false,
            propertyChanged: (b, _, _) => ((GlassOverlay)b).Refresh());
    public static readonly BindableProperty LiquidProperty =
        BindableProperty.Create(nameof(Liquid), typeof(bool), typeof(GlassOverlay), false,
            propertyChanged: (b, _, _) => ((GlassOverlay)b).Refresh());

    public bool Frost { get => (bool)GetValue(FrostProperty); set => SetValue(FrostProperty, value); }
    public bool Glass { get => (bool)GetValue(GlassProperty); set => SetValue(GlassProperty, value); }
    public bool Liquid { get => (bool)GetValue(LiquidProperty); set => SetValue(LiquidProperty, value); }

    public GlassOverlay() => InitializeComponent();

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
        Refresh();
    }

    private void Refresh()
    {
        if (FrostLayer is null || GlassLayer is null || LiquidLayer is null) return;
        FrostLayer.IsVisible = Frost || Glass;
        GlassLayer.IsVisible = Glass;
        LiquidLayer.IsVisible = Liquid;
        IsVisible = Frost || Glass || Liquid;
    }
}
