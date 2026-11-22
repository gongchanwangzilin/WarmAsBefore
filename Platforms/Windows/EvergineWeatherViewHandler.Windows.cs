using Evergine.Common.Graphics;
using Evergine.DirectX11;
using Evergine.Framework.Graphics;
using Evergine.Framework.Services;
using Evergine.WinUI;
using Microsoft.Maui.Handlers;
using Microsoft.UI.Xaml.Controls;
using System.Diagnostics;
using WinUIGrid = Microsoft.UI.Xaml.Controls.Grid;

namespace WarmAsBefore.Controls;

/// <summary>
/// EvergineWeatherView 的 Windows handler：WinUIGrid + SwapChainPanel
/// + WinUIWindowsSystem + DX11GraphicsContext + WinUISurface。
/// 结构照官方 EverSneaks 示例；MapApplication 的 isViewLoaded 判空收敛在 UpdateApplication 内。
/// </summary>
public partial class EvergineWeatherViewHandler : ViewHandler<EvergineWeatherView, WinUIGrid>
{
    private bool isViewLoaded;
    // 建链单线：布局线程与 Loaded 回调可能同时触发，重入会崩原生层。
    private bool _creatingSwapChain;
    /// <summary>已排队一次「下一轮再建链」：布局没落定之前不碰 SwapChainPanel。</summary>
    private bool _createScheduled;

    private SwapChainPanel swapChainPanel = null!;

    private WinUIWindowsSystem? windowsSystem;
    // 按 Application 实例记录「是否已 Initialize」：主界面与超级监测页各持一个 Evergine Application，
    // 全局单布尔会让第二个 app 跳过 Initialize（拿不到 GraphicsContext、场景为空）。
    private static readonly HashSet<global::Evergine.Framework.Application> InitializedApps = new();
    // 显卡设备是全进程一份（两个 Evergine Application 必须共用同一个 device）；
    // 但 SwapChain 必须按面板各建一条 —— 它绑死在一个 SwapChainPanel 的合成表面上。
    // 曾经是静态字段：第二个面板建链时把字段指过去，第一个面板的尺寸变化就会去 Resize 别人的链，
    // 原生层随即抛 COMException 0x80004005（DComp 合成失败），托管侧连堆栈都没有。
    private static DX11GraphicsContext? graphicsContext;
    private SwapChain? swapChain;
    // 本次会话已经因原生层异常放弃 3D 层（放弃后只保留 2D 天气，不再重试拉起）。
    private static bool _evergineGivenUp;

    /// <summary>最近一次 3D 初始化失败的原因：界面上要能说「为什么没有 3D」，不能静默失败。</summary>
    internal static string? LastFailure { get; private set; }

    /// <summary>
    /// 每次进入带 3D 的页面时给一次重试机会：以前这个放弃标记是整个进程一份，
    /// 主界面 3D 天气层失败一次，监测页的 3D 就再也起不来，且界面上没有任何提示。
    /// 重置后若原生层仍然失败，会重新置位并记录原因，不会陷入崩溃循环。
    /// </summary>
    internal static void ResetGiveUpForNewPage() => _evergineGivenUp = false;

    public EvergineWeatherViewHandler(IPropertyMapper mapper, CommandMapper? commandMapper = null)
        : base(mapper, commandMapper)
    {
    }

    /// <summary>由共享 partial 的 MapApplication 调用；未 Loaded 前直接跳过，Loaded 后会再触发一次。</summary>
    internal void UpdateApplication(EvergineWeatherView view, string displayName)
    {
        // 时序日志：这三条早退是「3D 起不来」的全部入口，写清楚是哪一条拦下的，
        // 免得再靠猜（同步落盘，进程被原生崩溃带走也留得下）。
        if (!this.isViewLoaded)
        {
            App.WriteLogNow("Evergine.UpdateApplication 跳过: 面板尚未 Loaded");
            return;
        }
        if (view.Application is null)
        {
            App.WriteLogNow("Evergine.UpdateApplication 跳过: Application 为 null");
            return;
        }
        if (_evergineGivenUp)
        {
            App.WriteLogNow("Evergine.UpdateApplication 跳过: 已放弃（" + LastFailure + "）");
            return;
        }
        App.WriteLogNow("Evergine.UpdateApplication 进入: " + displayName);

        // 整段兜底：Evergine + DX11 + DComp 这一路任何失败都只让 3D 层消失，
        // 绝不以未处理 UI 异常的形式冒到 WinUI 上（那会弹 bug 窗，还把整页画布带成空白）。
        try
        {
            this.UpdateApplicationCore(view, displayName);
        }
        catch (Exception ex)
        {
            _evergineGivenUp = true;
            LastFailure = ex.Message;
            this.TryHide(view);
            App.WriteLogNow("Evergine.UpdateApplication -> " + ex);
            try
            {
                WarmAsBefore.Services.Core.BugReporter.Report(
                    ex, "Weather3D", "3D 天气层初始化失败，已降级为 2D 天气层", recordOnly: true);
            }
            catch { }
        }
        finally
        {
            this._creatingSwapChain = false;
        }
    }

    private void UpdateApplicationCore(EvergineWeatherView view, string displayName)
        => UpdateApplicationCore(view, displayName, allowDefer: true);

    /// <summary>
    /// allowDefer 只在第一次进来时为 true：它把建链排到下一轮低优先级派发，
    /// 排到队后的那一轮用 false 进来真正建链。两轮共用同一个入口，但只有一轮会真正动手，
    /// 否则会在「排队 → 执行 → 再排队」之间空转（第一版就是这么空转的，日志刷了 1.6 万行）。
    /// </summary>
    private void UpdateApplicationCore(EvergineWeatherView view, string displayName, bool allowDefer)
    {
        // 一次机会制：每个面板只尝试建一次链。尺寸还没就绪（0 宽高）时
        // CreateSwapChainForComposition 会返回 DXGI_ERROR_INVALID_CALL，那就直接放弃这一层并写明原因，
        // 绝不在布局/尺寸事件里重入建链——那是把进程带崩的路径（无托管异常的那种崩）。
        if (this.swapChainPanel.ActualWidth < 4 || this.swapChainPanel.ActualHeight < 4)
        {
            LastFailure = $"面板尺寸未就绪（{this.swapChainPanel.ActualWidth:0}x{this.swapChainPanel.ActualHeight:0}）";
            App.WriteLog("Evergine.跳过建链: " + LastFailure);
            this.TryHide(view);
            return;
        }
        // 面板必须在活动可视树里：不在树里时 SwapChainPanel 的 COM native 接口拿不到，
        // Evergine 随后对 null 调 SetSwapChain，结果就是 combase.dll / 0x80004003（WER 已证）。
        if (!this.swapChainPanel.IsLoaded || this.swapChainPanel.XamlRoot is null)
        {
            LastFailure = "面板还没进可视树（IsLoaded=" + this.swapChainPanel.IsLoaded
                + ", XamlRoot=" + (this.swapChainPanel.XamlRoot is null ? "null" : "ok") + "）";
            App.WriteLogNow("Evergine.等待上树: " + LastFailure);
            return;
        }
        // 防重入：布局线程与 Loaded 回调可能同时进来，建链必须单线。
        if (this._creatingSwapChain) return;

        // 不在「尺寸/可见性正在变」的那一帧建链。
        // 事件里发起的 SwapChainPanel COM interop 会变成 stowed exception：
        // 事件日志实证 0xc000027b / CoreMessagingXP.dll，WER 里是 combase 的 E_POINTER —— 这种崩
        // 托管 catch 兜不住（进程直接消失，bugs.log 一条都没有）。推迟到下一轮低优先级派发，
        // 那时布局已经落定，面板的 native peer 才是稳定的。
        if (allowDefer && !this._createScheduled)
        {
            var queue = this.swapChainPanel.DispatcherQueue;
            if (queue is null)
            {
                App.WriteLogNow("Evergine.推迟建链失败: 面板还没有 DispatcherQueue（不在可视树里）");
                return;
            }
            this._createScheduled = true;
            var queued = queue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                this._createScheduled = false;
                App.WriteLogNow("Evergine.推迟建链: 下一轮派发开始建链");
                this.UpdateApplicationCore(view, displayName, allowDefer: false);
            });
            if (!queued)
            {
                this._createScheduled = false;
                App.WriteLogNow("Evergine.推迟建链失败: 排队被拒");
            }
            return;
        }
        this._creatingSwapChain = true;

        this.windowsSystem ??= new WinUIWindowsSystem();

        // 第二个 Application（超级监测页）从未注册过 WinUIWindowsSystem，
        // 无条件 Unregister 会抛 KeyNotFoundException（导航即崩、3D 层出不来）。
        // 换了容器实现之后 IsRegistered 与 Unregister 也不保证同口径，故再兜一层。
        ReplaceRegistration(view.Application.Container, this.windowsSystem);

        App.WriteLogNow("Evergine.CreateSurface: panel=" + this.swapChainPanel.ActualWidth.ToString("0")
            + "x" + this.swapChainPanel.ActualHeight.ToString("0") + ", app=" + displayName);
        var surface = (WinUISurface)this.windowsSystem.CreateSurface(this.swapChainPanel);

        var clockTimer = Stopwatch.StartNew();
        this.windowsSystem.Run(
            () =>
            {
                this.ConfigureGraphicsContext(view.Application, surface, displayName);

                if (!InitializedApps.Contains(view.Application))
                {
                    view.Application.Initialize();
                    InitializedApps.Add(view.Application);
                }
            },
            () =>
            {
                var gameTime = clockTimer.Elapsed;
                clockTimer.Restart();

                view.Application.UpdateFrame(gameTime);
                view.Application.DrawFrame(gameTime);
            });
    }

    protected override WinUIGrid CreatePlatformView()
    {
        var platformView = new WinUIGrid();

        this.swapChainPanel = new SwapChainPanel
        {
            IsHitTestVisible = true,
        };

        platformView.Children.Add(this.swapChainPanel);

        return platformView;
    }

    protected override void ConnectHandler(WinUIGrid platformView)
    {
        base.ConnectHandler(platformView);

        this.isViewLoaded = false;

        platformView.Loaded += this.OnPlatformViewLoaded;

        this.swapChainPanel.PointerPressed += this.OnPlatformViewPointerPressed;
        this.swapChainPanel.PointerMoved += this.OnPlatformViewPointerMoved;
        this.swapChainPanel.PointerReleased += this.OnPlatformViewPointerReleased;
        this.swapChainPanel.SizeChanged += this.OnSwapChainPanelSizeChanged;
    }

    protected override void DisconnectHandler(WinUIGrid platformView)
    {
        base.DisconnectHandler(platformView);

        platformView.Loaded -= this.OnPlatformViewLoaded;

        this.swapChainPanel.PointerPressed -= this.OnPlatformViewPointerPressed;
        this.swapChainPanel.PointerMoved -= this.OnPlatformViewPointerMoved;
        this.swapChainPanel.PointerReleased -= this.OnPlatformViewPointerReleased;
        this.swapChainPanel.SizeChanged -= this.OnSwapChainPanelSizeChanged;
    }

    private void OnPlatformViewLoaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        this.isViewLoaded = true;
        this.UpdateValue(nameof(EvergineWeatherView.Application));
    }

    private void OnPlatformViewPointerReleased(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        this.VirtualView?.StartInteraction();
    }

    private void OnPlatformViewPointerMoved(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        this.VirtualView?.MovedInteraction();
    }

    private void OnPlatformViewPointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        this.VirtualView?.EndInteraction();
    }

    private void OnSwapChainPanelSizeChanged(object sender, Microsoft.UI.Xaml.SizeChangedEventArgs e)
    {
        if (e.NewSize.Width <= 0 || e.NewSize.Height <= 0)
        {
            return;
        }

        // 只动自己这条链：DComp 表面绑面板，替别的面板调 Resize 会直接抛原生 COMException。
        try
        {
            // 只调尺寸，不在这里重入建链：在布局过程中回推 Application 会让 Evergine 的循环线程
            // 与 DComp 合成撞在一起，表现为进程无声退出（无托管异常、日志直接断掉）。
            swapChain?.ResizeSwapChain((uint)e.NewSize.Width, (uint)e.NewSize.Height);
        }
        catch (Exception ex) { App.WriteLog("Evergine.ResizeSwapChain -> " + ex.Message); }
    }

    /// <summary>
    /// 用 <paramref name="instance"/> 覆盖容器里的同类型注册，缺注册 / 重复注册都不抛。
    ///
    /// Evergine 的 SimpleIoCContainer：Unregister 对未注册类型抛 KeyNotFoundException，
    /// RegisterInstance 对已注册类型同样会抛。两条各自兜一次 —— 这类异常以前直接冒到
    /// WinUI 的 UnhandledException，用户看到的是「子系统 WinUI 已停止运行」加一整页空白。
    /// 放不进去也不致命：容器里已有的那一个仍然是可用的设备 / 窗口系统。
    /// </summary>
    private static void ReplaceRegistration<T>(global::Evergine.Framework.Container container, T instance)
        where T : class
    {
        try { container.Unregister<T>(); }
        catch { /* 本来就没注册（或口径不一致）：忽略，下面照样放进去 */ }
        try { container.RegisterInstance(instance); }
        catch (Exception ex)
        {
            App.WriteLog($"Evergine.ReplaceRegistration<{typeof(T).Name}> -> {ex.Message}");
        }
    }

    private void TryHide(EvergineWeatherView view)
    {
        try { view.IsVisible = false; }
        catch { /* 隐藏失败也只剩一条空链，不抛 */ }
    }

    private void ConfigureGraphicsContext(global::Evergine.Framework.Application application, WinUISurface surface, string displayName)
    {
        if (graphicsContext is null)
        {
            graphicsContext = new DX11GraphicsContext();
            graphicsContext.CreateDevice();
        }

        var swapChainDescription = new SwapChainDescription()
        {
            SurfaceInfo = surface.SurfaceInfo,
            Width = surface.Width,
            Height = surface.Height,
            ColorTargetFormat = PixelFormat.R8G8B8A8_UNorm,
            ColorTargetFlags = TextureFlags.RenderTarget | TextureFlags.ShaderResource,
            DepthStencilTargetFormat = PixelFormat.D24_UNorm_S8_UInt,
            DepthStencilTargetFlags = TextureFlags.DepthStencil,
            SampleCount = TextureSampleCount.None,
            IsWindowed = true,
            RefreshRate = 60,
        };

        displayName = string.IsNullOrWhiteSpace(displayName) ? "DefaultDisplay" : displayName;

        App.WriteLogNow("Evergine.CreateSwapChain: " + surface.Width.ToString("0") + "x"
            + surface.Height.ToString("0") + ", app=" + displayName);
        swapChain = graphicsContext.CreateSwapChain(swapChainDescription);
        swapChain.VerticalSync = true;
        App.WriteLogNow("Evergine.SetSwapChain.Before: app=" + displayName);
        surface.NativeSurface.SwapChain = swapChain;
        App.WriteLogNow("Evergine.SetSwapChain.After: app=" + displayName);

        var graphicsPresenter = application.Container.Resolve<GraphicsPresenter>();
        var firstDisplay = new Display(surface, swapChain);

        graphicsPresenter.RemoveDisplay(displayName);
        graphicsPresenter.AddDisplay(displayName, firstDisplay);
        // 每个 app 都要拿到同一个 GraphicsContext，否则它的 Scene 解析不到、渲染不出东西。
        // 这里原来是裸 Unregister：该 app 没注册过就抛 KeyNotFoundException（进监测页即崩）。
        ReplaceRegistration(application.Container, graphicsContext);
    }
}
