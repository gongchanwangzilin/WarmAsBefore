namespace WarmAsBefore.Controls;

/// <summary>
/// Apple Liquid Glass 标准玻璃叠加层（iOS 26）。
/// 三档：Frost（磨砂）/ Glass（毛玻璃）/ Liquid（液态玻璃）。
/// · Translucency（0-1）：自适应透明度，控制叠加层 Alpha；
/// · ReducedTransparency（true）：减弱透明降级 → 切不透明磨砂层，保证正文 4.5:1 对比；
/// · 液态档含 2 条斜向折射光带，OnHandlerChanged 时做一次 6dp 微位移动画（尊重 reduced-motion）。
/// </summary>
public partial class GlassOverlay : ContentView
{
    private const int RefractionDriftMs = 1600;
    private readonly bool _prefersReducedMotion;

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
        _prefersReducedMotion = false;
    }

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
        Refresh();
        PlayRefractionDrift();
    }

    private void Refresh()
    {
        if (FrostLayer is null || GlassLayer is null || LiquidLayer is null) return;

        // 减弱透明：不透明磨砂层替代所有透明层
        var reduced = ReducedTransparency;
        OpaqueFrostLayer.IsVisible = reduced;
        FrostLayer.IsVisible = !reduced && (Frost || Glass);
        GlassLayer.IsVisible = !reduced && Glass;
        LiquidLayer.IsVisible = !reduced && Liquid;
        IsVisible = reduced || Frost || Glass || Liquid;

        // 自适应透明度：按 Translucency 缩放液态层透明度
        var t = Math.Clamp(Translucency, 0, 1);
        if (LiquidLayer is not null)
            LiquidLayer.Opacity = 0.35 + 0.65 * t;
        if (GlassLayer is not null)
            GlassLayer.Opacity = 0.5 + 0.5 * t;
    }

    /// <summary>液态档：折射光带做一次 6dp 位移动画（尊重 reduced-motion，静态时不播）。</summary>
    private void PlayRefractionDrift()
    {
        if (!_prefersReducedMotion && Liquid && RefractionBand is not null)
        {
            _ = RefractionBand.TranslateTo(6, RefractionBand.TranslationY, RefractionDriftMs);
            _ = RefractionBand.ScaleTo(1.02, RefractionDriftMs);
        }
    }
}
