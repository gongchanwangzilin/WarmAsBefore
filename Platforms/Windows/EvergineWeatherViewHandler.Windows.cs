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

    private SwapChainPanel swapChainPanel = null!;

    private WinUIWindowsSystem? windowsSystem;
    // 按 Application 实例记录「是否已 Initialize」：主界面与超级监测页各持一个 Evergine Application，
    // 全局单布尔会让第二个 app 跳过 Initialize（拿不到 GraphicsContext、场景为空）。
    private static readonly HashSet<global::Evergine.Framework.Application> InitializedApps = new();
    private static DX11GraphicsContext? graphicsContext;
    private static SwapChain? swapChain;

    public EvergineWeatherViewHandler(IPropertyMapper mapper, CommandMapper? commandMapper = null)
        : base(mapper, commandMapper)
    {
    }

    /// <summary>由共享 partial 的 MapApplication 调用；未 Loaded 前直接跳过，Loaded 后会再触发一次。</summary>
    internal void UpdateApplication(EvergineWeatherView view, string displayName)
    {
        if (!this.isViewLoaded) return;
        if (view.Application is null) return;

        this.windowsSystem ??= new WinUIWindowsSystem();

        // 第二个 Application（超级监测页）从未注册过 WinUIWindowsSystem，
        // 无条件 Unregister 会抛 KeyNotFoundException（导航即崩、3D 层出不来）。
        if (view.Application.Container.IsRegistered<WinUIWindowsSystem>())
            view.Application.Container.Unregister<WinUIWindowsSystem>();
        view.Application.Container.RegisterInstance(this.windowsSystem);

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

        swapChain?.ResizeSwapChain((uint)e.NewSize.Width, (uint)e.NewSize.Height);
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

        swapChain = graphicsContext.CreateSwapChain(swapChainDescription);
        swapChain.VerticalSync = true;
        surface.NativeSurface.SwapChain = swapChain;

        var graphicsPresenter = application.Container.Resolve<GraphicsPresenter>();
        var firstDisplay = new Display(surface, swapChain);

        graphicsPresenter.RemoveDisplay(displayName);
        graphicsPresenter.AddDisplay(displayName, firstDisplay);
        // 每个 app 都要拿到同一个 GraphicsContext，否则它的 Scene 解析不到、渲染不出东西。
        application.Container.Unregister<DX11GraphicsContext>();
        application.Container.RegisterInstance(graphicsContext);
    }
}
