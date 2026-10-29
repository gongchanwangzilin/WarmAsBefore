using WarmAsBefore.Helpers;
using WarmAsBefore.ViewModels;

namespace WarmAsBefore.Views;

/// <summary>立绘标定页：不合规素材（像素比例不是 1080×1920 逻辑画布）的调试入口。</summary>
public partial class PortraitCalibrationPage : ContentPage
{
    public PortraitCalibrationPage(PortraitCalibrationViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        Motion.ArrivePage(this);
    }
}
