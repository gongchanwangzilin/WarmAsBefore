using Microsoft.Maui.Controls;
using WarmAsBefore.Modules.Cg;
using WarmAsBefore.Services;

namespace WarmAsBefore.Views;

public partial class CgViewPage : ContentPage
{
    private readonly CgViewPayload _payload;
    private readonly AudioController _audio;

    public CgViewPage(CgViewPayload payload, AudioController audio)
    {
        InitializeComponent();
        _payload = payload;
        _audio = audio;
        BindingContext = new ViewModels.CgViewViewModel();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        // 播放 CG 时静音背景音乐，离开时恢复
        _audio.SetBgmMuted(true);
        var v = (ViewModels.CgViewViewModel)BindingContext;
        v.ImagePath = _payload.ImagePath;
        v.Title = _payload.Title;
        v.Refresh();
        if (!_payload.HasPayload || string.IsNullOrWhiteSpace(v.ImagePath))
        {
            // 无图像直接退回
            if (Navigation.NavigationStack.Count > 1)
                _ = Microsoft.Maui.Controls.Shell.Current.GoToAsync("..");
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _audio.SetBgmMuted(false);
    }
}