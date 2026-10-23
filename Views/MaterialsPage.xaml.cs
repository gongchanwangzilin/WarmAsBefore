using WarmAsBefore.Controls;
using WarmAsBefore.Helpers;
using WarmAsBefore.ViewModels;

namespace WarmAsBefore.Views;

public partial class MaterialsPage : ContentPage
{
    public MaterialsPage(MaterialsViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
        // 素材页要扫背景/音乐两个目录：任一非空就算就绪（切页签不会重新装载，骨架只铺一次）。
        LoadingVeil.Attach(this, vm, "Backgrounds", "Musics");
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is MaterialsViewModel vm) _ = vm.RefreshCommand.ExecuteAsync(null);
        Motion.ArrivePage(this);
    }
}