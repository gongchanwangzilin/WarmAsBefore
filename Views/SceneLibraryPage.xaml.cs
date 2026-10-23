using WarmAsBefore.Controls;
using WarmAsBefore.Helpers;
using WarmAsBefore.ViewModels;

namespace WarmAsBefore.Views;

public partial class SceneLibraryPage : ContentPage
{
    public SceneLibraryPage(SceneLibraryViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
        LoadingVeil.Attach(this, vm, "Entries");   // 场景表要读素材库：用已有的 Entries 当就绪信号
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is SceneLibraryViewModel vm)
            vm.Refresh();
        Motion.ArrivePage(this);
    }
}
