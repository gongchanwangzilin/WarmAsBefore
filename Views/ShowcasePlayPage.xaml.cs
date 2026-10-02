using WarmAsBefore.ViewModels;

namespace WarmAsBefore.Views;

public partial class ShowcasePlayPage : ContentPage
{
    private readonly ShowcasePlayViewModel _vm;
    public ShowcasePlayPage(ShowcasePlayViewModel vm)
    {
        _vm = vm;
        BindingContext = vm;
        InitializeComponent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        var id = ShowcasePlayViewModel.PendingScriptId;
        ShowcasePlayViewModel.PendingScriptId = null;
        if (!string.IsNullOrEmpty(id)) _vm.LoadCommand.Execute(id);
    }

    private void OnTap(object? sender, TappedEventArgs e) => _vm.AdvanceTapCommand.Execute(null);
}