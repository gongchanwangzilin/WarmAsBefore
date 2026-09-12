using Microsoft.Maui.Controls;
using WarmAsBefore.Modules.Cg;

namespace WarmAsBefore.Views;

public partial class CgViewPage : ContentPage
{
    private readonly CgViewPayload _payload;

    public CgViewPage(CgViewPayload payload)
    {
        InitializeComponent();
        _payload = payload;
        BindingContext = new ViewModels.CgViewViewModel();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        var v = (ViewModels.CgViewViewModel)BindingContext;
        v.ImagePath = _payload.ImagePath;
        v.Title = _payload.Title;
        v.Refresh();
        if (!_payload.HasPayload || string.IsNullOrWhiteSpace(v.ImagePath))
        {
            // 无图像直接退回
            if (Navigation.NavigationStack.Count > 1)
                _ = Microsoft.Maui.Controls.Shell.Current.GoToAsync("..");
        }
    }
}