using WarmAsBefore.ViewModels;

namespace WarmAsBefore.Views;

public partial class NovelWorldPage : ContentPage, IQueryAttributable
{
    private string? _novelId;

    public NovelWorldPage(NovelWorldViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("id", out var id)) _novelId = id?.ToString();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is NovelWorldViewModel vm && !string.IsNullOrEmpty(_novelId))
            _ = vm.LoadAsync(_novelId);
    }
}
