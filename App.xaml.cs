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
            // 关闭窗口前自动保存当前进度，并收起桌宠/托盘，保证干净退出
            win.Destroying += (_, _) =>
            {
                _ = AutoSaveOnExitAsync();
                ShutdownPetAsync();
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
    /// 窗口已建好之后要做的事：语言、屏幕方向、自动备份、置顶、运行时配置下发。
    /// 设置本身已在 <see cref="CreateWindow"/> 里同步读过（见那里的说明）。
    /// </summary>
    private static async Task AfterWindowStartedAsync()
    {
        try
        {
            var services = Application.Current?.Handler?.MauiContext?.Services;
            if (services is null) return;
            var sm = services.GetService(typeof(SettingsManager)) as SettingsManager;
            LocalizationService.Current.SetCulture(sm?.Current.Lang ?? "zh-CN");
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

            // 诊断：--open=页面 时直接打开该页并计时（见 DiagnosticRoute 说明）
            var route = DiagnosticRoute();
            if (!string.IsNullOrWhiteSpace(route)) _ = OpenDiagnosticRouteAsync(route!);
        }
        catch (Exception ex)
        {
            WriteLog("AfterWindowStarted: " + ex);
        }
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
