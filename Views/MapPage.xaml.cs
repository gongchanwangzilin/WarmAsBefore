using System.Linq;
using Microsoft.Maui.Graphics;
using WarmAsBefore.ViewModels;

namespace WarmAsBefore.Views;

public partial class MapPage : ContentPage
{
    public MapPage(MapViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
        MapCanvas.Drawable = vm.Drawable;
        vm.AttachInvalidate(() => MapCanvas.Invalidate());
    }

#if WINDOWS
    private bool _hooked;
    private Microsoft.UI.Xaml.FrameworkElement? _winEl;
    private Microsoft.UI.Xaml.UIElement? _canvasEl;

    /// <summary>
    /// 桌面端需要额外的右键/滚轮事件源：MAUI GraphicsView 的 Touch 事件不含右键与滚轮。
    /// Win2D 的 CanvasControl 会先处理（handled）指针事件，普通 += 收不到，
    /// 因此用 AddHandler(handledEventsToo: true) 捕获，并在 Loaded 后挂载（避免 Handler 未就绪）。
    /// 关键：坐标必须相对「画布原生元素」而非页面根，否则头部高度会让右键永远命中空白分支。
    /// </summary>
    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
        HookWinUI();
    }

    private void HookWinUI()
    {
        if (_hooked) return;
        if (Handler?.PlatformView is not Microsoft.UI.Xaml.FrameworkElement element) return;
        _winEl = element;
        element.Loaded += OnWinLoaded;
        element.Unloaded += OnWinUnloaded;
        if (element.IsLoaded) OnWinLoaded(element, new Microsoft.UI.Xaml.RoutedEventArgs());
    }

    private void OnWinLoaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (_hooked || _winEl is null) return;
        _hooked = true;
        // 画布原生元素：作为右键/滚轮的坐标参照系（与 TouchEventArgs 同坐标系）
        _canvasEl = MapCanvas.Handler?.PlatformView as Microsoft.UI.Xaml.UIElement ?? _winEl;
        // 右键：弹出上下文菜单
        _winEl.AddHandler(Microsoft.UI.Xaml.UIElement.PointerPressedEvent,
            new Microsoft.UI.Xaml.Input.PointerEventHandler(OnWinPointerPressed), true);
        // 滚轮：缩放画布
        _winEl.AddHandler(Microsoft.UI.Xaml.UIElement.PointerWheelChangedEvent,
            new Microsoft.UI.Xaml.Input.PointerEventHandler(OnWinPointerWheelChanged), true);
    }

    private void OnWinUnloaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) => _hooked = false;

    private Microsoft.UI.Xaml.UIElement? CoordSource =>
        MapCanvas.Handler?.PlatformView as Microsoft.UI.Xaml.UIElement ?? _canvasEl ?? _winEl;

    private void OnWinPointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (CoordSource is not { } source) return;
        var pt = e.GetCurrentPoint(source);
        if (!pt.Properties.IsRightButtonPressed) return;
        var x = (float)pt.Position.X;
        var y = (float)pt.Position.Y;
        // 关键：同步记录时间戳（必须早于 MAUI TouchEnd 到达 CanvasEnd，否则清空框选）
        // 右键的同一按压会先被 GraphicsView 变成 Touch 事件；窗口级处理器后运行，
        // 但 TouchStart 装好 _marqueePending 后、TouchEnd 触发前，这里必然已执行。
        if (BindingContext is MapViewModel vm) vm.MarkRightPress();
        source.DispatcherQueue.TryEnqueue(() =>
            (BindingContext as MapViewModel)?.RightClick(new PointF(x, y)));
    }

    private void OnWinPointerWheelChanged(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (CoordSource is not { } source) return;
        var pt = e.GetCurrentPoint(source);
        var delta = pt.Properties.MouseWheelDelta;
        var pos = new PointF((float)pt.Position.X, (float)pt.Position.Y);
        source.DispatcherQueue.TryEnqueue(() => (BindingContext as MapViewModel)?.Zoom(-delta, pos));
    }
#endif

    private void OnCanvasStart(object? sender, TouchEventArgs e) =>
        (BindingContext as MapViewModel)?.CanvasStart(FirstTouch(e));

    private void OnCanvasDrag(object? sender, TouchEventArgs e) =>
        (BindingContext as MapViewModel)?.CanvasDrag(FirstTouch(e));

    private void OnCanvasEnd(object? sender, TouchEventArgs e) =>
        (BindingContext as MapViewModel)?.CanvasEnd(FirstTouch(e));

    private static PointF FirstTouch(TouchEventArgs e) =>
        e.Touches.FirstOrDefault();
}