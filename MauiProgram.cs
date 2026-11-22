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

        // Windows 的 Entry/Editor 自带一层系统边框（TextBox 的 BorderThickness + 焦点底色）。
        // 页面自己画了圆角框时，两层框会一起出现。只对显式标了 StyleId="WabPlainInput" 的输入框剥掉。
#if WINDOWS
        Microsoft.Maui.Handlers.EntryHandler.Mapper.AppendToMapping("WabPlainInput", (handler, view) =>
        {
            if (!string.Equals((view as Microsoft.Maui.Controls.Element)?.StyleId, "WabPlainInput", StringComparison.Ordinal)) return;
            handler.PlatformView.BorderThickness = new Microsoft.UI.Xaml.Thickness(0);
            handler.PlatformView.Background = null;
            handler.PlatformView.Padding = new Microsoft.UI.Xaml.Thickness(0);
            handler.PlatformView.MinHeight = 0;
            handler.PlatformView.FocusVisualPrimaryThickness = new Microsoft.UI.Xaml.Thickness(0);
            handler.PlatformView.FocusVisualSecondaryThickness = new Microsoft.UI.Xaml.Thickness(0);
        });
        Microsoft.Maui.Handlers.EditorHandler.Mapper.AppendToMapping("WabPlainInput", (handler, view) =>
        {
            if (!string.Equals((view as Microsoft.Maui.Controls.Element)?.StyleId, "WabPlainInput", StringComparison.Ordinal)) return;
            handler.PlatformView.BorderThickness = new Microsoft.UI.Xaml.Thickness(0);
            handler.PlatformView.Background = null;
        });
#endif

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
