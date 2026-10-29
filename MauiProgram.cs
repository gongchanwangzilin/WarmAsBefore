using CommunityToolkit.Maui;
using WarmAsBefore.Composition;

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
            .UseMauiCommunityToolkitMediaElement();

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
