using Microsoft.Maui.Graphics;
using WarmAsBefore.ViewModels;
#if ANDROID || WINDOWS
using System.Collections.Generic;
using WarmAsBefore.Services;
#endif

namespace WarmAsBefore.Views;

/// <summary>
/// 超级监测整页：进来才建数据、离开就退订停钟（见 VM.StartClock/StopClock）。
/// 画布照 MemoryGraphPage 的套路：XAML 里只放 GraphicsView，Drawable 在构造函数里从 VM 取。
/// 3D 向量层是纯装饰：初始化或显隐失败都保持隐藏，2D 流程图照常工作。
/// </summary>
public partial class SuperMonitorPage : ContentPage
{
    private readonly SuperMonitorViewModel _vm;

#if ANDROID || WINDOWS
    private bool _vectorReady;
    private WarmAsBefore.Weather3D.MonitorVectorApplication? _vectorApp;
#endif

    public SuperMonitorPage(SuperMonitorViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = vm;
        FlowCanvas.Drawable = vm.Drawable;
        vm.AttachInvalidate(() => FlowCanvas.Invalidate());
        FlowCanvas.SizeChanged += (_, _) => vm.SetViewport(FlowCanvas.Width, FlowCanvas.Height);

#if ANDROID || WINDOWS
        try
        {
            _vectorApp = new WarmAsBefore.Weather3D.MonitorVectorApplication();
            Vector3DView.Application = _vectorApp;
            _vectorReady = true;
        }
        catch (Exception ex)
        {
            // 3D 初始化失败：视图保持 XAML 里的 IsVisible=False，2D 流程图不受影响。
            _vectorReady = false;
            Vector3DView.IsVisible = false;
            App.WriteLog("SuperMonitor.InitVector3D -> " + ex.Message);
        }
#endif
    }

    protected override void OnNavigatedTo(NavigatedToEventArgs args)
    {
        base.OnNavigatedTo(args);
        _vm.StartClock();
#if ANDROID || WINDOWS
        SuperMonitorViewModel.VectorOverlayChanged = OnVectorOverlayChanged;
#endif
    }

    protected override void OnNavigatedFrom(NavigatedFromEventArgs args)
    {
#if ANDROID || WINDOWS
        // 钩子是静态的，离页必须置空，避免残留到下一次导航。
        SuperMonitorViewModel.VectorOverlayChanged = null;
#endif
        _vm.StopClock();
        base.OnNavigatedFrom(args);
    }

#if ANDROID || WINDOWS
    /// <summary>
    /// VM 的 3D 开关回调：只管显隐与数据推送，任何 3D 失败都吞掉，绝不回头影响 2D 监测页。
    /// </summary>
    private void OnVectorOverlayChanged(bool show, string charId)
    {
        try
        {
            if (Vector3DView is null) return;

            Vector3DView.IsVisible = show && _vectorReady;
            if (!show || !_vectorReady) return;

            // Application.Initialize 没跑成功时 Scene 为 null：跳过，不崩。
            var scene = _vectorApp?.Scene;
            if (scene is null) return;

            // id 约定见 MonitorVectorScene：organ-0..organ-7 对应 OrganKind 的整数值。
            var states = OrganSimulationService.Instance.Observe(charId);
            var nodes = new List<(string id, double value, bool lit)>(states.Count);
            foreach (var state in states)
                nodes.Add(($"organ-{(int)state.Kind}", state.Value, state.IsLit));

            scene.ApplyNodes(nodes);
        }
        catch (Exception ex)
        {
            App.WriteLog("SuperMonitor.VectorOverlay -> " + ex.Message);
            try { Vector3DView.IsVisible = false; } catch { /* 装饰层失败不影响 2D */ }
        }
    }
#endif

    private void OnCanvasEnd(object? sender, TouchEventArgs e)
    {
        var p = e.Touches.FirstOrDefault();
        _vm.CanvasTap(new PointF((float)p.X, (float)p.Y));
    }
}
