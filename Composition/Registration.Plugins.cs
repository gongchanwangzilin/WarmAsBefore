using Microsoft.Extensions.DependencyInjection;
using WarmAsBefore.Services;

namespace WarmAsBefore.Composition;

/// <summary>插件与工具链：权限表必须先于宿主注册，宿主函数才能拿到同一份授权状态。</summary>
public static class PluginRegistration
{
    public static IServiceCollection AddPluginServices(this IServiceCollection services)
    {
        services.AddSingleton<PluginPermissions>();
        services.AddSingleton<PluginHost>();
        // 能力包（skill）：说明书渐进加载 + 可执行入口复用外部工具机制
        services.AddSingleton<SkillLibrary>();
        services.AddSingleton<Modules.Sandbox.SandboxPolicy>(sp =>
            new Modules.Sandbox.SandboxPolicy(
                sp.GetRequiredService<StorageProvider>(),
                sp.GetRequiredService<SettingsManager>()));
        services.AddSingleton<Modules.Tools.ToolManager>(sp =>
            new Modules.Tools.ToolManager(
                sp.GetRequiredService<Modules.Tools.RuntimeManager>(),
                sp.GetRequiredService<Modules.Sandbox.SandboxPolicy>(),
                sp.GetRequiredService<SkillLibrary>(),
                sp.GetRequiredService<PluginHost>()));
        return services;
    }
}
