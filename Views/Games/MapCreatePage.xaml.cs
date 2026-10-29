using WarmAsBefore.Helpers;
using WarmAsBefore.ViewModels;

namespace WarmAsBefore.Views;

/// <summary>AI 地图创建页：检索真实城市 / 导入 / 让 AI 补背景。</summary>
public partial class MapCreatePage : ContentPage
{
    private readonly MapCreateViewModel _vm;

    public MapCreatePage(MapCreateViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        // 每次进来都重新对齐一次：Key 可能在设置里被动过，手上那张地图也可能在地图页被换掉了。
        _ = _vm.LoadAsync();
        Motion.ArrivePage(this);
    }
}
