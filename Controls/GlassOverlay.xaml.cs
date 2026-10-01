namespace WarmAsBefore.Controls;

/// <summary>
/// Apple Liquid Glass 标准玻璃叠加层（iOS 26）。
/// 三档：Frost（磨砂）/ Glass（毛玻璃）/ Liquid（液态玻璃）。
/// · Translucency（0-1）：自适应透明度，控制叠加层 Alpha；
/// · ReducedTransparency（true）：减弱透明降级 → 且确有玻璃时切不透明磨砂层，保证正文 4.5:1 对比；
/// · 液态档含 2 条斜向折射光带（静态高亮）。
///
/// 叠加层的挂载/可见性关键约束见 GlassOverlay.xaml 顶部注释（InputTransparent 必须配
/// CascadeInputTransparent=False，否则整层不会被合成）。
/// </summary>
public partial class GlassOverlay : Grid
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

    public static readonly BindableProperty TranslucencyProperty =
        // 默认值与 UserSettings.GlassTranslucency 保持一致（1.0），
        // 避免叠加层初始状态与设置页滑块显示的值不一致。
        BindableProperty.Create(nameof(Translucency), typeof(double), typeof(GlassOverlay), 1.0,
            propertyChanged: (b, _, _) => ((GlassOverlay)b).Refresh());

    public static readonly BindableProperty ReducedTransparencyProperty =
        BindableProperty.Create(nameof(ReducedTransparency), typeof(bool), typeof(GlassOverlay), false,
            propertyChanged: (b, _, _) => ((GlassOverlay)b).Refresh());

    public bool Frost { get => (bool)GetValue(FrostProperty); set => SetValue(FrostProperty, value); }
    public bool Glass { get => (bool)GetValue(GlassProperty); set => SetValue(GlassProperty, value); }
    public bool Liquid { get => (bool)GetValue(LiquidProperty); set => SetValue(LiquidProperty, value); }

    /// <summary>自适应透明度 0-1：1=全液态透明；0=接近不透明（弱化）。</summary>
    public double Translucency
    {
        get => (double)GetValue(TranslucencyProperty);
        set => SetValue(TranslucencyProperty, value);
    }

    /// <summary>减弱透明降级：true 时切到不透明磨砂层（Accessibility）。</summary>
    public bool ReducedTransparency
    {
        get => (bool)GetValue(ReducedTransparencyProperty);
        set => SetValue(ReducedTransparencyProperty, value);
    }

    public GlassOverlay()
    {
        InitializeComponent();
        // 叠加层常驻挂载，只切换各层的可见性。
        // 不要让根元素跟着做 false→true 的显隐：导航过程中那一步容易被漏掉，整层就不显示。
        IsVisible = true;
    }

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
        Refresh();
    }

    private void Refresh()
    {
        if (FrostLayer is null || GlassLayer is null || LiquidLayer is null) return;

        // 减弱透明：不透明磨砂层替代所有透明层。
        // 注意「减弱透明」只改变玻璃怎么画，不能反过来接管三个开关：
        // 旧实现 IsVisible = reduced || Frost || Glass || Liquid，导致
        // ①reduced 一旦为 true，磨砂/毛玻璃/液态三个开关全部失效（开关看起来没接线）；
        // ②三个开关全关时叠加层仍留一层不透明磨砂，关不掉。
        var reduced = ReducedTransparency;
        var any = Frost || Glass || Liquid;
        OpaqueFrostLayer.IsVisible = reduced && any;
        FrostLayer.IsVisible = !reduced && (Frost || Glass);
        GlassLayer.IsVisible = !reduced && Glass;
        LiquidLayer.IsVisible = !reduced && Liquid;

        // 自适应透明度：按 Translucency 缩放各层透明度
        var t = Math.Clamp(Translucency, 0, 1);
        LiquidLayer.Opacity = 0.35 + 0.65 * t;
        GlassLayer.Opacity = 0.5 + 0.5 * t;
        // 液态档：折射光带点亮，强化「液态玻璃」观感
        if (RefractionBand is not null)
            RefractionBand.Opacity = Liquid ? (0.5 + 0.5 * t) : 0;
    }
}
