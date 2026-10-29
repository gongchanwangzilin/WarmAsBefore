using Microsoft.Extensions.DependencyInjection;

namespace WarmAsBefore.Composition;

/// <summary>设计系统注册：主题管理器与中文别名包装。</summary>
public static class DesignSystemRegistration
{
    public static IServiceCollection AddDesignSystemServices(this IServiceCollection services)
    {
        services.AddSingleton<DesignSystem.Theme.ThemeManager>();
        // 中文别名包装：把同一个 ThemeManager 注入给中文标识符，两个入口共用一份状态。
        services.AddSingleton<DesignSystem.Theme.主题管理器>(sp =>
            new DesignSystem.Theme.主题管理器(sp.GetRequiredService<DesignSystem.Theme.ThemeManager>()));
        return services;
    }
}
