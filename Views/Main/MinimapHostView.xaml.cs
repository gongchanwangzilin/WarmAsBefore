using System.ComponentModel;
using WarmAsBefore.DesignSystem.Theme;
using WarmAsBefore.Helpers;
using WarmAsBefore.ViewModels;

namespace WarmAsBefore.Views;

/// <summary>
/// 右上角小地图的宿主：把「点在画布上的一个圈」换成「进大地图并定位到它」，再给展开那一下补个浮起。
///
/// 排版全在 xaml 里，这里只剩两件画不到也绑不了的事：自绘画布的事件（MAUI 的事件不能直接绑命令）、
/// 以及展开时的动效（得等面板真的量出尺寸再演，否则位移被布局覆盖，看着像没动）。
/// </summary>
public partial class MinimapHostView : ContentView
{
    private MinimapViewModel? _vm;

    public MinimapHostView()
    {
        InitializeComponent();
        // 列表行上的点按由 xaml 里的命令绑走；画布上的点按是事件，只能在代码里接。
        PanelCanvas.PoiTapped += id => _vm?.OpenPoiCommand.Execute(id);
        BindingContextChanged += OnBindingContextChangedInternal;
    }

    private void OnBindingContextChangedInternal(object? sender, EventArgs e)
    {
        if (_vm is not null) _vm.PropertyChanged -= OnVmPropertyChanged;
        _vm = BindingContext as MinimapViewModel;
        if (_vm is not null) _vm.PropertyChanged += OnVmPropertyChanged;
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MinimapViewModel.IsExpanded) || _vm?.IsExpanded != true) return;
        // 隔一帧再演：显隐是绑定推的，同一次布局里量不到面板的高度，位移会白给。
        Dispatcher.DispatchDelayed(
            TimeSpan.FromMilliseconds(MotionTokens.FrameMs),
            () => _ = Motion.Enter(Panel));
    }
}
