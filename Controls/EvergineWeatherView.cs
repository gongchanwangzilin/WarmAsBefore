using EvergineApplication = global::Evergine.Framework.Application;

namespace WarmAsBefore.Controls;

/// <summary>
/// Evergine 3D 天气大气场的 MAUI 承载视图（共享层）。
/// 平台渲染由两端 partial handler 实现：
/// Platforms/Android/EvergineWeatherViewHandler.Android.cs（Vulkan + AndroidSurfaceView）
/// Platforms/Windows/EvergineWeatherViewHandler.Windows.cs（DX11 + SwapChainPanel）
/// InputTransparent 的语义由使用方（XAML）设置，本控件不强制。
/// </summary>
public sealed partial class EvergineWeatherView : View
{
    public static readonly BindableProperty ApplicationProperty =
        BindableProperty.Create(nameof(Application), typeof(EvergineApplication), typeof(EvergineWeatherView), null);

    public static readonly BindableProperty DisplayNameProperty =
        BindableProperty.Create(nameof(DisplayName), typeof(string), typeof(EvergineWeatherView), string.Empty);

    public EvergineApplication Application
    {
        get => (EvergineApplication)this.GetValue(ApplicationProperty);
        set => this.SetValue(ApplicationProperty, value);
    }

    public string DisplayName
    {
        get => (string)this.GetValue(DisplayNameProperty);
        set => this.SetValue(DisplayNameProperty, value);
    }

    public event EventHandler? PointerPressed;

    public event EventHandler? PointerMoved;

    public event EventHandler? PointerReleased;

    internal void StartInteraction() => this.PointerPressed?.Invoke(this, EventArgs.Empty);

    internal void MovedInteraction() => this.PointerMoved?.Invoke(this, EventArgs.Empty);

    internal void EndInteraction() => this.PointerReleased?.Invoke(this, EventArgs.Empty);
}
