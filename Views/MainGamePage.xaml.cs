using WarmAsBefore.ViewModels;

namespace WarmAsBefore.Views;

public partial class MainGamePage : ContentPage
{
    public MainGamePage(MainGameViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }

    /// <summary>
    /// 承载背景与立绘的容器尺寸变化 → 交给 VM 重算立绘位置与缩放。
    /// 手机版与桌面版都有各自的容器，只认当前可见的那一个（隐藏的那个尺寸是陈旧的）。
    /// </summary>
    private void OnStageSizeChanged(object? sender, EventArgs e)
    {
        if (BindingContext is not MainGameViewModel vm) return;
        if (sender is not VisualElement el || !el.IsVisible || el.Width <= 0 || el.Height <= 0) return;
        vm.OnStageSizeChanged(el.Width, el.Height, ReferenceEquals(el, PhoneStage));
    }

    /// <summary>
    /// 未标定遮罩期间吃掉安卓返回键：遮罩本来就是"不选一个出口不让走"，
    /// 返回键绕过去等于把强制标定变成可以随手跳过。
    /// </summary>
    protected override bool OnBackButtonPressed()
    {
        if (BindingContext is MainGameViewModel { IsCalibrationBlocked: true })
            return true;
        return base.OnBackButtonPressed();
    }

    /// <summary>离开主页（如进 CG/设置）暂停场景视频，避免声音与画面串场；返回后从当前位置恢复。</summary>
    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        PauseSceneVideo();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        ResumeSceneVideo();
    }

    private void PauseSceneVideo()
    {
        try
        {
            if (SceneVideoPhone is { Source: not null }) SceneVideoPhone.Pause();
            if (SceneVideoDesktop is { Source: not null }) SceneVideoDesktop.Pause();
        }
        catch { }
    }

    private void ResumeSceneVideo()
    {
        try
        {
            if (SceneVideoPhone is { Source: not null, IsVisible: true }) SceneVideoPhone.Play();
            if (SceneVideoDesktop is { Source: not null, IsVisible: true }) SceneVideoDesktop.Play();
        }
        catch { }
    }
}