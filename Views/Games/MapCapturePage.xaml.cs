using WarmAsBefore.Helpers;
using WarmAsBefore.ViewModels;

namespace WarmAsBefore.Views;

/// <summary>取景会话页（手机端）：规定开始地点 → 走路随拍，照片钉在地图坐标上。</summary>
public partial class MapCapturePage : ContentPage
{
    public MapCapturePage(MapCaptureViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is MapCaptureViewModel vm)
        {
            vm.AppearingCommand.Execute(null);
            Motion.ArrivePage(this);
        }
    }
}
