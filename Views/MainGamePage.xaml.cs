using WarmAsBefore.ViewModels;

namespace WarmAsBefore.Views;

public partial class MainGamePage : ContentPage
{
    public MainGamePage(MainGameViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
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