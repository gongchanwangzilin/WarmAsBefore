using WarmAsBefore.ViewModels;

namespace WarmAsBefore.Views;

[QueryProperty(nameof(SaveId), "id")]
public partial class SaveRulesPage : ContentPage
{
    private readonly SaveRulesViewModel _vm;
    private bool _loaded;

    public SaveRulesPage(SaveRulesViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = vm;
    }

    public string SaveId { get; set; } = "";

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (_loaded) return;
        _loaded = true;
        _ = _vm.LoadAsync(Uri.UnescapeDataString(SaveId));
    }
}
