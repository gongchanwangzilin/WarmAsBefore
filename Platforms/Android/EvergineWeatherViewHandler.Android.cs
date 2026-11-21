using Evergine.Android;
using Evergine.Common.Graphics;
using Evergine.Common.Helpers;
using Evergine.Framework.Services;
using Evergine.Vulkan;
using Microsoft.Maui.Handlers;

namespace WarmAsBefore.Controls;

/// <summary>
/// EvergineWeatherView 的 Android handler：AndroidSurfaceView + AndroidWindowsSystem
/// + Vulkan(VKGraphicsContext) + SwapChain。结构照官方 EverSneaks 示例，
/// 差别仅在把 MyApplication 换成基类 Evergine.Framework.Application。
/// </summary>
public partial class EvergineWeatherViewHandler : ViewHandler<EvergineWeatherView, AndroidSurfaceView>
{
    private AndroidSurface androidSurface = null!;
    private AndroidWindowsSystem windowsSystem = null!;
    // 按 Application 实例记录初始化状态：主界面与超级监测页各有一个 Evergine Application，
    // 全局单布尔会让第二个 app 跳过 Initialize（拿不到 GraphicsContext，场景空白）。
    private static readonly HashSet<global::Evergine.Framework.Application> InitializedApps = new();
    private static VKGraphicsContext? graphicsContext;
    private static SwapChain? swapChain;
    private static bool surfaceInvalidated;

    /// <summary>最近一次 3D 初始化失败的原因（与 Windows 端同名成员保持跨端一致，供页面提示用）。</summary>
    internal static string? LastFailure { get; private set; }

    /// <summary>每次进入带 3D 的页面时给一次重试机会（与 Windows 端同名成员保持一致）。</summary>
    internal static void ResetGiveUpForNewPage() => LastFailure = null;

    public EvergineWeatherViewHandler(IPropertyMapper mapper, CommandMapper? commandMapper = null)
        : base(mapper, commandMapper)
    {
    }

    /// <summary>由共享 partial 的 MapApplication 调用（签名与 Windows 端保持一致）。</summary>
    internal void UpdateApplication(EvergineWeatherView view, string displayName)
    {
        if (view.Application is null) return;

        // 每个 app 都要注册自己的 WindowsSystem / 音频设备，不能只看全局标志。
        // 第二个 Application 可能从未注册过这些单例，无条件 Unregister 抛 KeyNotFoundException。
        if (view.Application.Container.IsRegistered<AndroidWindowsSystem>())
            view.Application.Container.Unregister<AndroidWindowsSystem>();
        view.Application.Container.RegisterInstance(windowsSystem);
        var xaudio = new global::Evergine.OpenAL.ALAudioDevice();
        if (view.Application.Container.IsRegistered<global::Evergine.OpenAL.ALAudioDevice>())
            view.Application.Container.Unregister<global::Evergine.OpenAL.ALAudioDevice>();
        view.Application.Container.RegisterInstance(xaudio);

        System.Diagnostics.Stopwatch clockTimer = System.Diagnostics.Stopwatch.StartNew();
        windowsSystem.Run(
        () =>
        {
            this.ConfigureGraphicsContext(view.Application, androidSurface);
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

    protected override AndroidSurfaceView CreatePlatformView()
    {
        this.windowsSystem = new AndroidWindowsSystem(this.Context!);
        this.androidSurface = this.windowsSystem.CreateSurface(0, 0) as AndroidSurface ?? null!;

        return this.androidSurface.NativeSurface;
    }

    protected override void ConnectHandler(AndroidSurfaceView platformView)
    {
        base.ConnectHandler(platformView);
        this.androidSurface.OnSurfaceInfoChanged += this.AndroidSurface_OnSurfaceInfoChanged;
        this.androidSurface.Closing += this.AndroidSurface_OnClosing;
        this.androidSurface.OnScreenSizeChanged += this.AndroidSurface_OnScreenSizeChanged;
    }

    protected override void DisconnectHandler(AndroidSurfaceView platformView)
    {
        base.DisconnectHandler(platformView);
        this.androidSurface.OnSurfaceInfoChanged -= this.AndroidSurface_OnSurfaceInfoChanged;
        this.androidSurface.Closing -= this.AndroidSurface_OnClosing;
        this.androidSurface.OnScreenSizeChanged -= this.AndroidSurface_OnScreenSizeChanged;
    }

    private void AndroidSurface_OnSurfaceInfoChanged(object sender, SurfaceInfo surfaceInfo)
    {
        swapChain?.RefreshSurfaceInfo(surfaceInfo);
        swapChain?.ResizeSwapChain(this.androidSurface.Width, this.androidSurface.Height);
        surfaceInvalidated = false;
    }

    private void AndroidSurface_OnClosing(object sender, EventArgs e)
    {
        surfaceInvalidated = true;
    }

    private void AndroidSurface_OnScreenSizeChanged(object sender, SizeEventArgs e)
    {
        if (!surfaceInvalidated)
        {
            swapChain?.ResizeSwapChain(this.androidSurface.Width, this.androidSurface.Height);
        }
    }

    private void ConfigureGraphicsContext(global::Evergine.Framework.Application application, Surface surface)
    {
        if (graphicsContext is null)
        {
            graphicsContext = new VKGraphicsContext();
            graphicsContext.CreateDevice();
        }

        SwapChainDescription swapChainDescription = new SwapChainDescription()
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
        swapChain = graphicsContext.CreateSwapChain(swapChainDescription);
        swapChain.VerticalSync = true;

        var graphicsPresenter = application.Container.Resolve<GraphicsPresenter>();
        var firstDisplay = new global::Evergine.Framework.Graphics.Display(surface, swapChain);

        graphicsPresenter.RemoveDisplay("DefaultDisplay");
        graphicsPresenter.AddDisplay("DefaultDisplay", firstDisplay);
        // 每个 app 都要拿到同一个 GraphicsContext，否则它的 Scene 解析不到、渲染不出东西。
        application.Container.Unregister<VKGraphicsContext>();
        application.Container.RegisterInstance(graphicsContext);
    }
}
