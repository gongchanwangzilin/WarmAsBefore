using WarmAsBefore.ViewModels;

namespace WarmAsBefore.Views;

public partial class GalleryPage : ContentPage
{
    public GalleryPage(GalleryViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is GalleryViewModel vm)
            vm.RefreshCommand.Execute(null);
    }
}
