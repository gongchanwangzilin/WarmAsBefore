using System.ComponentModel;
using WarmAsBefore.Controls;
using WarmAsBefore.ViewModels;

namespace WarmAsBefore.Views;

public partial class MainGamePhoneView : ContentView
{
    private readonly CharacterBackdropPresenter _backdrop;
    private MainGameViewModel? _vm;

    public MainGamePhoneView()
    {
        InitializeComponent();
        _backdrop = new CharacterBackdropPresenter(PhoneCharacterBgA, PhoneCharacterBgB, PhoneCharacterBgHost);
        BindingContextChanged += OnBindingContextChangedInternal;
    }

    private void OnBindingContextChangedInternal(object? sender, EventArgs e)
    {
        if (_vm is not null) _vm.PropertyChanged -= OnVmPropertyChanged;
        _vm = BindingContext as MainGameViewModel;
        if (_vm is null) return;
        _vm.PropertyChanged += OnVmPropertyChanged;
        _ = ApplyCharacterBackdropAsync(instant: true);
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainGameViewModel.CharacterBackdrop))
            MainThread.BeginInvokeOnMainThread(() => _ = ApplyCharacterBackdropAsync());
    }

    /// <summary>角色背景换场；场景/地图背景接管时 VM 会给空，这里收掉露出底色。</summary>
    private async Task ApplyCharacterBackdropAsync(bool instant = false)
    {
        var state = _vm?.CharacterBackdrop;
        try
        {
            if (state is null)
            {
                await _backdrop.ApplyAsync(null, "fill", 0, 0, 1, 1, false, instant);
                return;
            }
            await _backdrop.ApplyAsync(
                state.Path, state.Fit, state.CropX, state.CropY, state.CropW, state.CropH,
                state.IsVideo, instant);
        }
        catch (Exception ex) { App.WriteLog("MainGamePhone.CharacterBackdrop -> " + ex.Message); }
    }

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
        _backdrop.Pause();
    }

    public void Play()
    {
        try { if (SceneVideoPhone is { Source: not null, IsVisible: true }) SceneVideoPhone.Play(); }
        catch { }
        _backdrop.Resume();
    }
}
