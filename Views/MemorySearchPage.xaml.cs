using WarmAsBefore.Helpers;
using WarmAsBefore.ViewModels;

namespace WarmAsBefore.Views;

/// <summary>
/// 搜事件：独立窗口那一扇，或手机上退回页内导航的那一页。
/// 装载由 <see cref="MemorySearchViewModel.ApplyQueryAttributes"/> 起头（带 charId / q 进来就直接搜），
/// 这里只补一句「空手回到这一页时把最近的先摊开」。
/// </summary>
public partial class MemorySearchPage : ContentPage
{
    private readonly MemorySearchViewModel _vm;

    public MemorySearchPage(MemorySearchViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (!_vm.Searching && _vm.Results.Count == 0) _vm.SearchNowCommand.Execute(null);
        Motion.ArrivePage(this);
    }
}
