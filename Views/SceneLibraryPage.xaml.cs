using WarmAsBefore.ViewModels;

namespace WarmAsBefore.Views;

public partial class SceneLibraryPage : ContentPage
{
    public SceneLibraryPage(SceneLibraryViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is SceneLibraryViewModel vm)
            vm.Refresh();
    }
}
