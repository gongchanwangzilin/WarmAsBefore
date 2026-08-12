namespace WarmAsBefore.Controls;

/// <summary>
/// 装饰层背景模糊（仅 Windows）：把指定视图（装饰层，如标题页渐变光斑）的层后内容高斯模糊后作为其背景。
/// 注意：真实模糊只能作用在「层后内容」上，因此只应挂在纯装饰元素上（光斑/渐变色块），
/// 不能放在文字按钮之上，否则会把内容一并糊掉。
/// </summary>
#if WINDOWS
public static class DecorBlur
{
    private static readonly Dictionary<Microsoft.UI.Xaml.Controls.Panel, BackdropBlurBrush> _applied = new();

    /// <summary>开启/关闭装饰模糊。on=true 时给视图平台背景套上合成高斯模糊画笔。</summary>
    public static void Apply(Microsoft.Maui.IView? view, bool on, float blurAmount = 14f)
    {
        if (view?.Handler?.PlatformView is not Microsoft.UI.Xaml.Controls.Panel panel) return;
        try
        {
            if (on)
            {
                if (_applied.ContainsKey(panel)) return; // 已应用
                var compositor = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview
                    .GetElementVisual(panel).Compositor;
                var brush = new BackdropBlurBrush(compositor, blurAmount);
                panel.Background = brush;
                _applied[panel] = brush;
            }
            else
            {
                if (_applied.Remove(panel, out var existing))
                {
                    existing.DisposeBrush();
                }
                panel.ClearValue(Microsoft.UI.Xaml.Controls.Panel.BackgroundProperty);
            }
        }
        catch (Exception ex)
        {
            App.WriteLog("DecorBlur.Apply -> " + ex);
        }
    }
}

/// <summary>合成高斯模糊画笔：把层后内容模糊后作为背景（渲染于子元素之下）。</summary>
public sealed class BackdropBlurBrush : Microsoft.UI.Xaml.Media.XamlCompositionBrushBase
{
    private readonly float _blurAmount;
    private Microsoft.UI.Composition.CompositionEffectBrush? _effect;

    public BackdropBlurBrush(Microsoft.UI.Composition.Compositor compositor, float blurAmount = 14f)
        => _blurAmount = blurAmount;

    protected override void OnConnected()
    {
        if (_effect is not null) { CompositionBrush = _effect; return; }
        try
        {
            var compositor = Microsoft.UI.Xaml.Media.CompositionTarget.GetCompositorForCurrentThread();
            var backdrop = compositor.CreateBackdropBrush();
            var effect = new Microsoft.Graphics.Canvas.Effects.GaussianBlurEffect
            {
                Name = "decorBlur",
                BlurAmount = _blurAmount,
                Optimization = Microsoft.Graphics.Canvas.Effects.EffectOptimization.Speed,
                BorderMode = Microsoft.Graphics.Canvas.Effects.EffectBorderMode.Soft,
                Source = new Microsoft.UI.Composition.CompositionEffectSourceParameter("backdrop")
            };
            var factory = compositor.CreateEffectFactory(effect);
            _effect = factory.CreateBrush();
            _effect.SetSourceParameter("backdrop", backdrop);
            CompositionBrush = _effect;
        }
        catch (Exception ex)
        {
            App.WriteLog("BackdropBlurBrush.OnConnected -> " + ex);
        }
    }

    protected override void OnDisconnected()
    {
        if (_effect is not null) { _effect.Dispose(); _effect = null; }
        CompositionBrush = null;
    }

    /// <summary>主动释放（画笔被移除后 XamlCompositionBrushBase 也会调用 OnDisconnected，双重释放已做空值保护）。</summary>
    public void DisposeBrush()
    {
        if (_effect is not null) { _effect.Dispose(); _effect = null; }
        CompositionBrush = null;
    }
}
#else
public static class DecorBlur
{
    public static void Apply(Microsoft.Maui.IView? view, bool on, float blurAmount = 14f) { }
}
#endif
