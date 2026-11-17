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
    private static bool isEvergineInitialized;
    private static VKGraphicsContext? graphicsContext;
    private static SwapChain? swapChain;
    private static bool surfaceInvalidated;

    public EvergineWeatherViewHandler(IPropertyMapper mapper, CommandMapper? commandMapper = null)
        : base(mapper, commandMapper)
    {
    }

    /// <summary>由共享 partial 的 MapApplication 调用（签名与 Windows 端保持一致）。</summary>
    internal void UpdateApplication(EvergineWeatherView view, string displayName)
    {
        if (view.Application is null) return;

        if (!isEvergineInitialized)
        {
            // Register Windows system
            view.Application.Container.RegisterInstance(windowsSystem);

            // Creates XAudio device
            var xaudio = new global::Evergine.OpenAL.ALAudioDevice();
            view.Application.Container.RegisterInstance(xaudio);
        }
        else
        {
            view.Application.Container.Unregister<AndroidWindowsSystem>();
            view.Application.Container.RegisterInstance(windowsSystem);
        }

        System.Diagnostics.Stopwatch clockTimer = System.Diagnostics.Stopwatch.StartNew();
        windowsSystem.Run(
        () =>
        {
            if (!isEvergineInitialized)
            {
                this.ConfigureGraphicsContext(view.Application, androidSurface);
                view.Application.Initialize();
                isEvergineInitialized = true;
            }
            else
            {
                this.ConfigureGraphicsContext(view.Application, androidSurface);
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

        if (!isEvergineInitialized)
        {
            graphicsPresenter.AddDisplay("DefaultDisplay", firstDisplay);
            application.Container.RegisterInstance(graphicsContext);
        }
        else
        {
            graphicsPresenter.RemoveDisplay("DefaultDisplay");
            graphicsPresenter.AddDisplay("DefaultDisplay", firstDisplay);
        }
    }
}
