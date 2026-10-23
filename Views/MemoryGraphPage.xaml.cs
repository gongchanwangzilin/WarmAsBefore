using Microsoft.Maui.Graphics;
using WarmAsBefore.Helpers;
using WarmAsBefore.ViewModels;

namespace WarmAsBefore.Views;

/// <summary>
/// 关系网页面：一进来先看它长出来，然后能改、能调参、能让模型整理。
/// 画布是 Skia（GraphicsView + IDrawable），跟地图同一套路：XAML 里不放 Drawable，
/// 在构造函数里从 VM 取，并把「重画」回调交回去。
/// </summary>
public partial class MemoryGraphPage : ContentPage
{
    private readonly MemoryGraphViewModel _vm;

    public MemoryGraphPage(MemoryGraphViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = vm;
        GraphCanvas.Drawable = vm.Drawable;
        vm.AttachInvalidate(() => GraphCanvas.Invalidate());
        vm.AttachDrawableInvalidate();
        GraphCanvas.SizeChanged += (_, _) => vm.SetViewport(GraphCanvas.Width, GraphCanvas.Height);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (_vm.NodeCount == 0) _ = _vm.LoadAsync();
        else _vm.OnShown();
        Motion.ArrivePage(this);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _vm.StopClock();
    }

#if WINDOWS
    private bool _hooked;
    private Microsoft.UI.Xaml.FrameworkElement? _winEl;
    private Microsoft.UI.Xaml.UIElement? _canvasEl;

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

    private void OnWinLoaded(object? sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (_hooked || _winEl is null) return;
        _hooked = true;
        _canvasEl = GraphCanvas.Handler?.PlatformView as Microsoft.UI.Xaml.UIElement ?? _winEl;
        _winEl.AddHandler(Microsoft.UI.Xaml.UIElement.PointerWheelChangedEvent,
            new Microsoft.UI.Xaml.Input.PointerEventHandler(OnWinWheel), true);
        _winEl.AddHandler(Microsoft.UI.Xaml.UIElement.PointerMovedEvent,
            new Microsoft.UI.Xaml.Input.PointerEventHandler(OnWinMoved), true);
    }

    private void OnWinUnloaded(object? sender, Microsoft.UI.Xaml.RoutedEventArgs e) => _hooked = false;

    private Microsoft.UI.Xaml.UIElement? CoordSource =>
        GraphCanvas.Handler?.PlatformView as Microsoft.UI.Xaml.UIElement ?? _canvasEl ?? _winEl;

    private void OnWinWheel(object? sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (CoordSource is not { } source) return;
        var pt = e.GetCurrentPoint(source);
        var delta = pt.Properties.MouseWheelDelta;
        var pos = new PointF((float)pt.Position.X, (float)pt.Position.Y);
        // 不取反：滚轮向上 / 双指外扩 = MouseWheelDelta 正 = 放大；双指内缩 = 负 = 缩小。
        // 原先写的是 -delta，于是触摸板上「捏合」反而把网撑大。
        source.DispatcherQueue.TryEnqueue(() => _vm.Zoom(delta, pos));
    }

    private void OnWinMoved(object? sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (CoordSource is not { } source) return;
        if (e.GetCurrentPoint(source).Properties.IsLeftButtonPressed) return;   // 只在没按下时跟指针（按住就是要拖画面）
        var pt = e.GetCurrentPoint(source);
        _vm.Hover(new PointF((float)pt.Position.X, (float)pt.Position.Y));
    }
#endif

    private void OnCanvasStart(object? sender, TouchEventArgs e) => _vm.CanvasStart(FirstTouch(e));
    private void OnCanvasDrag(object? sender, TouchEventArgs e) => _vm.CanvasDrag(FirstTouch(e));
    private void OnCanvasEnd(object? sender, TouchEventArgs e) => _vm.CanvasEnd(FirstTouch(e));

    private static PointF FirstTouch(TouchEventArgs e) => e.Touches.FirstOrDefault();
}
