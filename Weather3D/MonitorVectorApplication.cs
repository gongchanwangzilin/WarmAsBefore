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
/// 超级监测页「3D 向量显示」的 Evergine 应用入口。由 MAUI 层的 EvergineWeatherView 持有。
/// 服务注册照抄 WeatherApplication（引擎基类只留空钩子，必须由应用自己注册）。
/// 任何失败都被吞掉并写调试日志：3D 失败时 Scene 保持 null，页面维持纯 2D 流程图。
/// </summary>
public sealed class MonitorVectorApplication : Application
{
    private const string LogTag = "[MonitorVectorApplication]";

    /// <summary>
    /// 当前场景。Initialize 成功后才有值；拿不到就跳过 ApplyNodes（不崩）。
    /// </summary>
    public MonitorVectorScene? Scene { get; private set; }

    public MonitorVectorApplication()
    {
        try
        {
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
            // 基类负责初始化上面注册的服务，必须在切场景前调用。
            base.Initialize();

            var scene = new MonitorVectorScene();
            this.Scene = scene;
            this.Container.Resolve<ScreenContextManager>()
                .To(new ScreenContext(scene));
        }
        catch (Exception ex)
        {
            // 3D 初始化失败绝不能影响 2D 监测页。
            Debug.WriteLine(LogTag + " Initialize 失败，3D 向量显示降级为 2D: " + ex);
            this.Scene = null;
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
