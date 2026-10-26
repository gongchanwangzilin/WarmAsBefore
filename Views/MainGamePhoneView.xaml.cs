using WarmAsBefore.ViewModels;

namespace WarmAsBefore.Views;

public partial class MainGamePhoneView : ContentView
{
    public MainGamePhoneView() => InitializeComponent();

    /// <summary>手机版舞台尺寸变化 → 交给 VM 重算立绘位置与缩放。</summary>
    private void OnStageSizeChanged(object? sender, EventArgs e)
    {
        if (BindingContext is not MainGameViewModel vm) return;
        if (sender is not VisualElement el || !el.IsVisible || el.Width <= 0 || el.Height <= 0) return;
        vm.OnStageSizeChanged(el.Width, el.Height, true);
    }

    public void Pause()
    {
        try { if (SceneVideoPhone is { Source: not null }) SceneVideoPhone.Pause(); }
        catch { }
    }

    public void Play()
    {
        try { if (SceneVideoPhone is { Source: not null, IsVisible: true }) SceneVideoPhone.Play(); }
        catch { }
    }
}
