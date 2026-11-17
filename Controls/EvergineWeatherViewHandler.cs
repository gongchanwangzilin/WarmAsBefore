using Microsoft.Maui;
using Microsoft.Maui.Handlers;

namespace WarmAsBefore.Controls;

/// <summary>
/// EvergineWeatherView 的共享 handler 部分：这里只放 mapper / 构造 / 平台无关的映射入口，
/// 平台类型（AndroidSurfaceView / WinUIGrid）只出现在各平台 partial 中。
/// </summary>
public partial class EvergineWeatherViewHandler
{
    public static IPropertyMapper<EvergineWeatherView, EvergineWeatherViewHandler> PropertyMapper =
        new PropertyMapper<EvergineWeatherView, EvergineWeatherViewHandler>(ViewMapper)
        {
            [nameof(EvergineWeatherView.Application)] = MapApplication,
        };

    public static CommandMapper<EvergineWeatherView, EvergineWeatherViewHandler> CommandMapper = new(ViewCommandMapper);

    public EvergineWeatherViewHandler()
        : base(PropertyMapper, CommandMapper)
    {
    }

    /// <summary>
    /// Application 属性变更的共享入口。平台差异收敛在各端的 UpdateApplication 内部：
    /// Android 直接配置图形上下文并起窗口系统；Windows 需等 SwapChainPanel Loaded（isViewLoaded）后再更新。
    /// </summary>
    public static void MapApplication(EvergineWeatherViewHandler handler, EvergineWeatherView view)
    {
        handler.UpdateApplication(view, view.DisplayName);
    }
}
