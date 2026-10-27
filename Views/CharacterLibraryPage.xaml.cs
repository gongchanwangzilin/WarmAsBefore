using WarmAsBefore.Controls;
using WarmAsBefore.Helpers;
using WarmAsBefore.ViewModels;

namespace WarmAsBefore.Views;

public partial class CharacterLibraryPage : ContentPage
{
    private readonly CharacterBackdropPresenter _backdrop;
    private CharacterLibraryViewModel? _vm;

    public CharacterLibraryPage(CharacterLibraryViewModel vm)
    {
        using (WarmAsBefore.Services.PerfLog.Measure("页 · 角色库 · XAML 膨胀"))
            InitializeComponent();
        BindingContext = vm;
        _vm = vm;
        _backdrop = new CharacterBackdropPresenter(PreviewBgA, PreviewBgB, PreviewBgHost);
        vm.PropertyChanged += OnVmChanged;
        // 角色库要先读盘才填得上：数据到位前铺一层骨架，信号用 VM 已有的 Items（集合非空即就绪）。
        LoadingVeil.Attach(this, vm, "Items");
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        Motion.ArrivePage(this);
        using var _ = WarmAsBefore.Services.PerfLog.Measure("页 · 角色库 · 刷新");
        if (BindingContext is CharacterLibraryViewModel vm) await vm.RefreshCommand.ExecuteAsync(null);
        _backdrop.Resume();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _backdrop.Pause();
    }

    private void OnVmChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(CharacterLibraryViewModel.PreviewBackdrop)) return;
        MainThread.BeginInvokeOnMainThread(() => _ = ApplyBackdropAsync());
    }

    /// <summary>换人 = 换背景：有图走 220ms 交叉淡入，没图收掉露出井面。</summary>
    private async Task ApplyBackdropAsync()
    {
        try
        {
            var state = _vm?.PreviewBackdrop;
            if (state is null)
            {
                await _backdrop.ApplyAsync(null, "fill", 0, 0, 1, 1, false);
                return;
            }
            await _backdrop.ApplyAsync(
                state.Path, state.Fit, state.CropX, state.CropY, state.CropW, state.CropH,
                state.IsVideo);
        }
        catch (Exception ex) { App.WriteLog("CharacterLibrary.Backdrop -> " + ex.Message); }
    }
}
