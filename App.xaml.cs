using System.IO;
using WarmAsBefore.Services;

namespace WarmAsBefore;

public partial class App : Application
{
    private static readonly string LogPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "warm_startup.log");

    public static string RootDirectory { get; } =
        Path.Combine(FileSystem.AppDataDirectory, "WarmAsBefore");

    public App()
    {
        try
        {
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
                WriteLog("APPDOMAIN UNHANDLED: " + e.ExceptionObject);
            TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                WriteLog("UNOBSERVED TASK: " + e.Exception);
                e.SetObserved();
            };
            InitializeComponent();
            // 首帧之前必须把世界令牌摆好：样式字典现在引用的是 W* / Glass* 这些计算出来的键，
            // 空着解析不到，卡片会既没有面也没有边（设置是异步恢复的，等不到那时候）。
            DesignSystem.Worlds.StyleWorldTokens.Publish(
                DesignSystem.Worlds.StyleWorld.Soft, RequestedTheme == AppTheme.Dark, "classic");
            DesignSystem.Theme.ThemeManager.FollowSystemTheme();
            // 页面根背景统一走 PageBgBrush（可能被背景设置换成图片/透明）。
            // 必须在首帧页面解析前给一个默认值，否则 DynamicResource 找不到键。
            var pageBg = Resources.TryGetValue("PageBg", out var pv0) && pv0 is Color pc0 ? pc0 : Colors.WhiteSmoke;
            Resources[Services.AppBackgroundService.PageBgBrushKey] = new SolidColorBrush(pageBg);
#if WINDOWS
            // WinUI 层未处理异常（0xc000027b 崩溃源头大多从这里冒出来）
            if (Microsoft.UI.Xaml.Application.Current is { } app)
                app.UnhandledException += (_, e) =>
                {
                    WriteLog("WINUI UNHANDLED: " + e.Exception);
                    WriteLog("WINUI UNHANDLED STACK: " + e.Exception?.StackTrace);
                };
#endif
        }
        catch (Exception ex)
        {
            WriteLog("App.InitializeComponent: " + ex);
            throw;
        }
    }

    /// <summary>
    /// 把窗口逻辑尺寸喂给尺度档，跨档才重发令牌。
    /// 尺度只在这一处被写：窗口、启动对齐、以后任何新入口都走这里，
    /// 免得出现「颜色跟着窗口变了、字号没变」这种半套下发。
    /// </summary>
    private static void ApplyViewport(double width, double height)
    {
        if (!DesignSystem.Worlds.StyleWorldTokens.SetViewport(width, height)) return;
        DesignSystem.Theme.ThemeManager.Current?.RepublishForScale();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        try
        {
            // 设置必须在构建界面之前读完：标题页要按 DisclaimerAcknowledged 决定弹不弹免责声明，
            // 读晚了（原来的 fire-and-forget）首帧拿到的永远是默认值 →每次启动都弹一遍。
            try
            {
                var sm = Application.Current?.Handler?.MauiContext?.Services
                    ?.GetService(typeof(SettingsManager)) as SettingsManager;
                sm?.RestoreBlocking();
                // 语言也必须在建界面之前落定。以前它是窗口起来之后才设的，于是首帧永远先按 zh-CN
                // 排一遍、再被 PropertyChanged 重排一次；启动卡的名牌是逐字建出来的，
                // 中途重排就是「换错一个字」（繁体那档的 溫 和简体的 温 不是同一个字）。
                LocalizationService.Current.SetCulture(sm?.Current.Lang ?? "zh-CN");
            }
            catch (Exception ex) { WriteLog("CreateWindow.RestoreSettings: " + ex); }

            var swStartup = System.Diagnostics.Stopwatch.StartNew();
            WriteLog("CreateWindow: building AppShell");
            var win = new Window(new AppShell())
            {
                Title = "温暖如初 · Warm As Before",
                Width = 1280,
                Height = 800
            };
            // 视口尺度：窗口尺寸一变就重算尺度档，跨了一档才重发令牌（见 StyleWorldTokens.SetViewport）。
            // 挂在 WinUI 窗口的 SizeChanged 上而不是 MAUI Window 的事件上 —— MAUI 的 Window 在这个版本
            // 根本没有 Resized，而 WinUI 报的 NewSize 已经是有效像素（逻辑单位），正是尺度要吃的数。
            // 只订阅一次：HandlerChanged 会被平台重复触发，重复挂会让一次拖动重排好几遍。
#if WINDOWS
            var viewportHooked = false;
            win.HandlerChanged += (_, _) =>
            {
                if (viewportHooked || win.Handler?.PlatformView is not Microsoft.UI.Xaml.Window xw) return;
                viewportHooked = true;
                // 不读事件参数（那个 args 类型和 MAUI 引进来的同名类型撞了），直接问窗口现在的 Bounds。
                xw.SizeChanged += (_, _) => { var b = xw.Bounds; ApplyViewport(b.Width, b.Height); };
            };
#endif
            // 开局也要对一次尺：App 构造函数里那次 Publish 发生时窗口还不存在，
            // 尺度停在设计档。桌面端窗口就是请求的 1280×800（正好 1.0），
            // 但 Android 上 Window.Width 没有意义，退回主屏密度换算，否则手机会按 1280 的档排版。
            var di = DeviceDisplay.MainDisplayInfo;
            ApplyViewport(
                win.Width > 64 ? win.Width : di.Width / di.Density,
                win.Height > 64 ? win.Height : di.Height / di.Density);
            // 关闭窗口前自动保存当前进度，并收起桌宠/托盘，保证干净退出
            win.Destroying += (_, _) =>
            {
                ShutdownPetAsync();
                _ = AutoSaveOnExitAsync();
            };
            // 通知置顶由「对话框统一包装器」在真正弹 Alert 时触发（见 App.Dialog.ShowAlert），
            // 不在 Window.Activated 里做：Activate() 在 WinUI 有焦点重入副作用，会造成卡顿。
            Services.PerfLog.Note("启动 · 构建 AppShell+Window", $"{swStartup.ElapsedMilliseconds} ms");
            _ = AfterWindowStartedAsync();
            WriteLog("CreateWindow: ok");
            return win;
        }
        catch (Exception ex)
        {
            WriteLog("CreateWindow: " + ex);
            throw;
        }
    }

    /// <summary>
    /// 诊断用启动参数：--open=页面路由（如 --open=settings）直接打开该页并计时。
    /// 用来测量「某个页面装载到底多慢」——不靠手点，也能拿到数字。
    /// 正常启动不带此参数时完全没有影响。
    /// </summary>
    private static string? DiagnosticRoute()
    {
        try
        {
            foreach (var a in Environment.GetCommandLineArgs())
            {
                const string flag = "--open=";
                if (a.StartsWith(flag, StringComparison.OrdinalIgnoreCase) && a.Length > flag.Length)
                    return a[flag.Length..];
            }
        }
        catch { }
        return null;
    }

    private static bool HoldOnRoute()
    {
        try
        {
            return Environment.GetCommandLineArgs().Any(a => a.Equals("--hold", StringComparison.OrdinalIgnoreCase));
        }
        catch { return false; }
    }

    private static async Task OpenDiagnosticRouteAsync(string routes)
    {
        try
        {
            await Task.Delay(2500);   // 等首页与角色库加载完，避免与启动抢资源
            foreach (var route in routes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var ok = false;
                try
                {
                    await MainThread.InvokeOnMainThreadAsync(async () => await Shell.Current.GoToAsync(route));
                    ok = true;
                }
                catch (Exception ex) { Services.PerfLog.Note($"诊断 · 导航失败 {route}", ex.Message); }

                if (ok) Services.PerfLog.Note($"诊断 · 页面装载 {route}", $"{sw.ElapsedMilliseconds} ms{(sw.ElapsedMilliseconds >= 300 ? "  <<< SLOW" : "")}");
                await Task.Delay(3500);   // 留出该页自身的异步装载时间
                if (HoldOnRoute()) { await Task.Delay(60000); continue; }   // --hold：停在当前页，便于截图核查
                await MainThread.InvokeOnMainThreadAsync(async () => await Shell.Current.GoToAsync(".."));
                await Task.Delay(1200);
            }
        }
        catch (Exception ex)
        {
            Services.PerfLog.Note("诊断 · 导航异常", ex.Message);
        }
    }

    private static async Task AutoSaveOnExitAsync()
    {
        try
        {
            var services = Application.Current?.Handler?.MauiContext?.Services;
            if (services is null) return;
            var engine = services.GetService(typeof(GameEngine)) as GameEngine;
            var save = services.GetService(typeof(Modules.SaveSystem.SaveManager)) as Modules.SaveSystem.SaveManager;
            if (engine is null || save is null || string.IsNullOrEmpty(engine.CurrentSaveId)) return;
            await save.Commit("退出前自动保存");
            WriteLog("AutoSaveOnExit: ok");
            // 刚写下的这一笔也要尽量推上云；窗口不等网络，能到就够
            (services.GetService(typeof(Services.CloudSync.CloudSyncService))
                as Services.CloudSync.CloudSyncService)?.FlushOnExit();
        }
        catch (Exception ex)
        {
            WriteLog("AutoSaveOnExit: " + ex);
        }
    }

    private static void ShutdownPetAsync()
    {
        try
        {
            var services = Application.Current?.Handler?.MauiContext?.Services;
            if (services is null) return;
            (services.GetService(typeof(PetService)) as PetService)?.Shutdown();
        }
        catch (Exception ex)
        {
            WriteLog("ShutdownPet: " + ex);
        }
    }

    /// <summary>
    /// 窗口已建好之后要做的事：屏幕方向、自动备份、置顶、运行时配置下发。
    /// 设置与语言都在 <see cref="CreateWindow"/> 里同步做过（见那里的说明），这里不再补第二遍。
    /// </summary>
    private static async Task AfterWindowStartedAsync()
    {
        try
        {
            var services = Application.Current?.Handler?.MauiContext?.Services;
            if (services is null) return;
            var sm = services.GetService(typeof(SettingsManager)) as SettingsManager;
            // 屏幕方向：按设置锁屏（auto=竖屏 / landscape / portrait）
            Modules.Screen.ScreenOrientationService.ApplyOnStart(sm?.Current.ScreenOrientation);
            // 自动备份：启动时把数据目录最新内容备份一份到下载文件夹（桌面 no-op）
            _ = Services.DownloadBackupService.OnAppStartAsync();
            // 通知/桌宠类应用：默认常驻顶层，保证消息/通知不被其他窗口遮挡。
            // AlwaysOnTop 关闭时只临时 BringToFront（弹通知时抬一次），开则持续强制。
            if (sm?.Current.AlwaysOnTop == true)
                WindowTopmost.Force();
            else
                WindowTopmost.BringToFront();
            var cfg = services.GetService(typeof(RuntimeConfigurator)) as RuntimeConfigurator;
            cfg?.Start();

            // 云存档：启动后先悄悄拉一次（不挡首帧），之后每次改动停手两秒自动上传
            var cloud = services.GetService(typeof(Services.CloudSync.CloudSyncService))
                as Services.CloudSync.CloudSyncService;
            cloud?.Start();

            // 诊断：--open=页面 时直接打开该页并计时（见 DiagnosticRoute 说明）
            var route = DiagnosticRoute();
            if (!string.IsNullOrWhiteSpace(route)) _ = OpenDiagnosticRouteAsync(route!);
        }
        catch (Exception ex)
        {
            WriteLog("AfterWindowStarted: " + ex);
        }
        // 不在这里铺启动遮罩：实测「构建 AppShell+Window」每趟只有 80 ms 且全在窗口显示前跑完，
        // 铺骨架只会盖出一段假加载。真正慢的是导航进设置的 300–500 ms 建卡，已由分组淡入盖住。
        await Task.CompletedTask;
    }

    private static readonly System.Collections.Concurrent.ConcurrentQueue<string> LogQueue = new();
    private static readonly Lazy<Task> LogDrain = new(() => Task.Run(DrainLogsAsync));

    /// <summary>后台落盘日志队列，避免阻塞 UI 线程。</summary>
    private static async Task DrainLogsAsync()
    {
        while (true)
        {
            while (LogQueue.TryDequeue(out var line))
            {
                try { File.AppendAllText(LogPath, line); }
                catch { }
            }
            await Task.Delay(500);
        }
    }

    public static void WriteLog(string msg)
    {
        try
        {
            LogQueue.Enqueue($"[{DateTime.Now.ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture)}] {msg}{Environment.NewLine}");
            _ = LogDrain.Value;
        }
        catch { }
    }
}
