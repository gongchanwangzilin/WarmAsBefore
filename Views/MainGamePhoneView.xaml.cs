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
        // 同桌面版：星号行会被立绘的期望高度撑大，撑过窗口就再也收不回来。
        // 能信的只有容器（= 窗口）减去舞台偏移，按这个预算往回钳。
        var w = Width > 0 ? Math.Min(el.Width, Width - el.X) : el.Width;
        var h = Height > 0 ? Math.Min(el.Height, Height - el.Y) : el.Height;
        if (w < 1 || h < 1) return;
        vm.OnStageSizeChanged(w, h, true);
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
