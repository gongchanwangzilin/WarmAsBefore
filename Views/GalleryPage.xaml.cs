using WarmAsBefore.Controls;
using WarmAsBefore.Helpers;
using WarmAsBefore.ViewModels;

namespace WarmAsBefore.Views;

public partial class GalleryPage : ContentPage
{
    public GalleryPage(GalleryViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
        // 回忆/日记/CG 四个页签的数据都要现攒：任一到货就收骨架，用 VM 已有的四个集合当信号。
        LoadingVeil.Attach(this, vm, "AffectionItems", "MemoryItems", "DiaryItems", "CgItems");
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is GalleryViewModel vm)
            vm.RefreshCommand.Execute(null);
        Motion.ArrivePage(this);
    }
}
