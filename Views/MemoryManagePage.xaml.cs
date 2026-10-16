using WarmAsBefore.ViewModels;

namespace WarmAsBefore.Views;

/// <summary>记忆管理页：关系网 / 标签 / 记得的事 / 副设定的统一查看与修改入口。</summary>
public partial class MemoryManagePage : ContentPage
{
    private readonly MemoryManageViewModel _vm;

    public MemoryManagePage(MemoryManageViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _vm.LoadCommand.Execute(null);
    }
}
