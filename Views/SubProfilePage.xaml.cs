using WarmAsBefore.Controls;
using WarmAsBefore.Helpers;
using WarmAsBefore.ViewModels;

namespace WarmAsBefore.Views;

/// <summary>主设定 × 副设定对比页。</summary>
public partial class SubProfilePage : ContentPage
{
    public SubProfilePage(SubProfileViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
        // 设定文本要等服务读出来：MainPersonality 初始为空，落值即算就绪。
        LoadingVeil.Attach(this, vm, "MainPersonality");
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        Motion.ArrivePage(this);
    }
}
