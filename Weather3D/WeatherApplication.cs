#if ANDROID || WINDOWS
using System;
using System.Diagnostics;
using Evergine.Common.IO;
using Evergine.Framework;
using Evergine.Framework.Services;
// MAUI 与 Evergine 都有 Application，显式别名消歧
using Application = Evergine.Framework.Application;

namespace WarmAsBefore.Weather3D;

/// <summary>
/// 3D 天气场的 Evergine 应用入口。由 MAUI 层的 EvergineWeatherView 持有并调用 Initialize()。
/// 任何初始化失败都被吞掉并写调试日志，由 MAUI 层降级回旧的 SkiaSharp 天气层。
/// </summary>
public sealed class WeatherApplication : Application
{
    private const string LogTag = "[WeatherApplication]";

    public WeatherApplication()
    {
        try
        {
            // Evergine 的 Application.Initialize() 会依次调用 RegisterApplicationServices /
            // RegisterSceneSourceFactories，但基类实现为空，服务必须由应用自己注册。
            // Container.Register<T>() 的两个参数（工厂、reuse）都有默认值，可直接无参调用。
            RegisterIfMissing<Settings>();
            RegisterIfMissing<Clock>();
            RegisterIfMissing<TimerFactory>();
            RegisterIfMissing<Evergine.Framework.Services.Random>();
            RegisterIfMissing<ErrorHandler>();
            RegisterIfMissing<ScreenContextManager>();
            RegisterIfMissing<GraphicsPresenter>();
            RegisterIfMissing<AssetsDirectory>();
            RegisterIfMissing<AssetsService>();
            RegisterIfMissing<ForegroundTaskSchedulerService>();
            RegisterIfMissing<WorkActionScheduler>();
        }
        catch (Exception ex)
        {
            Debug.WriteLine(LogTag + " 服务注册失败: " + ex);
        }
    }

    public override void Initialize()
    {
        try
        {
            // 基类负责初始化上面注册的服务（InitializeServices），必须在切场景前调用。
            base.Initialize();

            this.Container.Resolve<ScreenContextManager>()
                .To(new ScreenContext(new WeatherScene()));
        }
        catch (Exception ex)
        {
            // 3D 初始化失败绝不能崩主界面：MAUI 层据此降级为旧的 2D 天气层。
            Debug.WriteLine(LogTag + " Initialize 失败，降级为 2D 天气层: " + ex);
        }
    }

    private void RegisterIfMissing<T>() where T : class
    {
        if (!this.Container.IsRegistered<T>())
        {
            this.Container.Register<T>();
        }
    }
}
#endif
