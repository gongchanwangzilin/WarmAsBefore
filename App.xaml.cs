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
            _ = RestoreSettingsAsync();
            WriteLog("CreateWindow: ok");
            return win;
        }
        catch (Exception ex)
        {
            WriteLog("CreateWindow: " + ex);
            throw;
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

    private static async Task RestoreSettingsAsync()
    {
        try
        {
            var services = Application.Current?.Handler?.MauiContext?.Services;
            if (services is null) return;
            var sm = services.GetService(typeof(SettingsManager)) as SettingsManager;
            if (sm is not null) await sm.Restore();
            LocalizationService.Current.SetCulture(sm?.Current.Lang ?? "zh-CN");
            WindowTopmost.Apply(sm?.Current.AlwaysOnTop ?? false);
            var cfg = services.GetService(typeof(RuntimeConfigurator)) as RuntimeConfigurator;
            cfg?.Start();
        }
        catch (Exception ex)
        {
            WriteLog("RestoreSettings: " + ex);
        }
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
