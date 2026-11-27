using SkiaSharp.Views.Maui.Controls.Hosting;
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
            // 相机实时预览（人脸监测页的 CameraView）；包自带的分析器要求它挂在 UseMauiApp 的链上。
            .UseMauiCommunityToolkitCamera()
            // 玻璃面用 SKCanvasView 直画（见 GlassPlate）：需要它的 handler 注册。
            .UseSkiaSharp()
            // v1.61 3D 天气大气场：注册 Evergine 承载视图的平台 handler
            // （Android: AndroidSurfaceView+Vulkan / Windows: SwapChainPanel+DX11）。
            .ConfigureMauiHandlers(h => h.AddHandler<EvergineWeatherView, EvergineWeatherViewHandler>());

        // Windows 的输入控件自带一层系统皮肤（TextBox/ComboBox 的边框、圆角、焦点矩形、填充色）。
        // 页面已经用玻璃面与令牌把那一格画好了，系统再压一层就出现「两个框」，而且它落在所有令牌
        // 之外 —— 这正是用户说的「像另一个图层」。这里**全部**剥掉，不再是按 StyleId 逐个 opting-in：
        // 一个界面里只要有一个控件还穿着系统皮，整块就散了。
#if WINDOWS
        Microsoft.Maui.Handlers.EntryHandler.Mapper.AppendToMapping("WabSkin", (handler, view) =>
            WarmAsBefore.Platforms.Windows.InputSkin.Apply(handler.PlatformView));
        Microsoft.Maui.Handlers.EditorHandler.Mapper.AppendToMapping("WabSkin", (handler, view) =>
            WarmAsBefore.Platforms.Windows.InputSkin.Apply(handler.PlatformView));
        Microsoft.Maui.Handlers.PickerHandler.Mapper.AppendToMapping("WabSkin", (handler, view) =>
            WarmAsBefore.Platforms.Windows.InputSkin.Apply(handler.PlatformView));

#endif

        builder.Services
            .AddDesignSystemServices()
            .AddCoreServices()
            .AddModuleServices()
            .AddCharacterServices()
            .AddPluginServices()
            .AddViewModels()
            .AddPages()
            // Web 前端宿主（渐进迁移）：桥是单例（持设置与背景服务），页面每次导航新建。
            .AddSingleton<WarmAsBefore.Services.Web.WebBridge>()
            .AddTransient<WarmAsBefore.Views.Web.WebShellPage>();

        App.WriteLog("CreateMauiApp: building");
        return builder.Build();
    }
}
