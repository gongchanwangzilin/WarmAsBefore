using WarmAsBefore.Helpers;
using WarmAsBefore.ViewModels;

namespace WarmAsBefore.Views;

public partial class FigurePackPage : ContentPage, IQueryAttributable
{
    private readonly FigurePackViewModel _vm;

    public FigurePackPage(FigurePackViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = vm;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("character", out var value))
            _vm.SelectCharacter(Uri.UnescapeDataString(value?.ToString() ?? ""));
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        Motion.ArrivePage(this);
        await _vm.LoadAsync();
    }

    protected override void OnDisappearing()
    {
        _vm.StopPlayback();
        base.OnDisappearing();
    }
}
