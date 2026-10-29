using WarmAsBefore.ViewModels;

namespace WarmAsBefore.Views;

public partial class MentalHealthPage : ContentPage
{
    private readonly MentalHealthViewModel _vm;

    public MentalHealthPage(MentalHealthViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _ = _vm.LoadAsync();
        Helpers.Motion.ArrivePage(this);
    }
}
