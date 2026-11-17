using CommunityToolkit.Maui;
using WarmAsBefore.Composition;
using WarmAsBefore.Controls;

namespace WarmAsBefore;

/// <summary>
/// 组合根：只负责创建应用与按顺序挂上各层注册，具体注册表在 <c>Composition/</c>。
/// </summary>
public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        App.WriteLog("CreateMauiApp: start");
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseMauiCommunityToolkit()
            .UseMauiCommunityToolkitMediaElement()
            // v1.61 3D 天气大气场：注册 Evergine 承载视图的平台 handler
            // （Android: AndroidSurfaceView+Vulkan / Windows: SwapChainPanel+DX11）。
            .ConfigureMauiHandlers(h => h.AddHandler<EvergineWeatherView, EvergineWeatherViewHandler>());

        builder.Services
            .AddDesignSystemServices()
            .AddCoreServices()
            .AddModuleServices()
            .AddCharacterServices()
            .AddPluginServices()
            .AddViewModels()
            .AddPages();

        App.WriteLog("CreateMauiApp: building");
        return builder.Build();
    }
}
