using WarmAsBefore.ViewModels;

namespace WarmAsBefore.Views;

/// <summary>主设定 × 副设定对比页。</summary>
public partial class SubProfilePage : ContentPage
{
    public SubProfilePage(SubProfileViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
