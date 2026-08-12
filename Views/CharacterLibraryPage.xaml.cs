using WarmAsBefore.ViewModels;

namespace WarmAsBefore.Views;

public partial class CharacterLibraryPage : ContentPage
{
    public CharacterLibraryPage(CharacterLibraryViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is CharacterLibraryViewModel vm) _ = vm.RefreshCommand.ExecuteAsync(null);
    }
}
