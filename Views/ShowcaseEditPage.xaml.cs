using WarmAsBefore.ViewModels;

namespace WarmAsBefore.Views;

public partial class ShowcaseEditPage : ContentPage
{
    private readonly ShowcaseEditViewModel _vm;
    public ShowcaseEditPage(ShowcaseEditViewModel vm)
    {
        _vm = vm;
        BindingContext = vm;
        InitializeComponent();
    }
    protected override void OnAppearing()
    {
        base.OnAppearing();
        var id = ShowcaseEditViewModel.PendingScriptId;
        ShowcaseEditViewModel.PendingScriptId = null;
        _vm.LoadCommand.Execute(id);
    }
}