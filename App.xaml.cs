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
            {
                WriteLog("APPDOMAIN UNHANDLED: " + e.ExceptionObject);
                TryNotifyCrash("温暖如初遇到问题",
                    "程序即将退出。启动日志已写入：Documents\\warm_startup.log，可把这份文件发给开发者定位问题。");
            };
            TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                WriteLog("UNOBSERVED TASK: " + e.Exception);
                e.SetObserved();
            };
            InitializeComponent();
            // 首帧之前必须把世界令牌摆好：样式字典引用的是 W* / Glass* 这些计算出来的键，
            // 空着解析不到，卡片会既没有面也没有边（设置是异步恢复的，等不到那时候）。
            DesignSystem.Worlds.StyleWorldTokens.Publish(
                DesignSystem.Worlds.StyleWorld.Endfield, RequestedTheme == AppTheme.Dark, "classic");
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
                    // 这条路径上进程随时会没：同步写盘，不进后台队列（队列 500ms 才刷一次，
                    // 崩溃前那几行最要紧）。
                    WriteLogNow("WINUI UNHANDLED: " + e.Exception);
                    WriteLogNow("WINUI UNHANDLED STACK: " + e.Exception?.StackTrace);
                    FlushLog();
                    // 审计 U5：窗口无声消失对用户是「程序坏了还什么都不说」。
                    // 尽力弹一次提示指路日志；不设 e.Handled，崩溃语义保持原样。
                    TryNotifyCrash("温暖如初遇到问题",
                        "发生了一个未处理的错误，窗口可能即将关闭。\n日志位置：Documents\\warm_startup.log");
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
    /// <summary>上一次记录进日志的视口宽（节流：拖动时 SizeChanged 一秒几十次，全记会刷屏）。</summary>
    private static double _loggedViewportW;

    private static void ApplyViewport(double width, double height)
    {
        // 诊断记录：路径是否走通、喂进来的宽度是多少（逻辑还是有效像素），差距大于 4 才记一条。
        if (Math.Abs(width - _loggedViewportW) > 4)
        {
            _loggedViewportW = width;
            WriteLog($"viewport {width:0}x{height:0} → 期望内容列 wide={Math.Clamp(Math.Round(width * 0.9), 320, 1600):0} narrow={Math.Clamp(Math.Round(width * 0.62), 320, 900):0}");
        }

        // 宽度令牌（内容列宽）跟着窗口宽度逐次重发：跨了尺度档就顺手整套重发（字号/内距要重算），
        // 没跨档也要把 WCardWidth/WCardWidthWide 刷新——以前不跨档就什么都不发，
        // 内容列宽卡在启动那一次的值上，用户实测就是「设置首页老是固定大小」。
        if (DesignSystem.Worlds.StyleWorldTokens.SetViewport(width, height))
            DesignSystem.Theme.ThemeManager.Current?.RepublishForScale();
        else
            DesignSystem.Worlds.StyleWorldTokens.RepublishWidth();
    }

    /// <summary>
    /// Windows 桌面拖放导入：把 zip / .wbsave / 存档 json 直接拖进窗口任意位置，
    /// 自动识别包类型并交给 SmartImporter 路由加载，结果以 Toast 反馈（识别不出会说明原因）。
    /// 为保证「窗口任意位置都能拖入」：整棵可视树都设为放置目标并挂同一套处理，
    /// 且在内容就绪前 / 页面导航后补挂，避免静默漏挂导致整窗显示禁止拖入。
    /// </summary>
#if WINDOWS
    private static readonly HashSet<object> _dropHooked = new();
    private static bool _dropRetryPending;

    private static void HookZipDrop(Microsoft.UI.Xaml.Window xw)
    {
        try
        {
            if (xw.Content is not Microsoft.UI.Xaml.FrameworkElement root)
            {
                // 主窗口内容尚未就绪（例如 HandlerChanged 早于 Content 赋值）：等首次激活再挂一次，
                // 否则静默 return 会让整窗没有任何放置目标，鼠标全程显示禁止符号。
                if (!_dropRetryPending)
                {
                    _dropRetryPending = true;
                    void OnActivated(object? s, Microsoft.UI.Xaml.WindowActivatedEventArgs? e)
                    {
                        xw.Activated -= OnActivated;
                        _dropRetryPending = false;
                        HookZipDrop(xw);
                    }
                    xw.Activated += OnActivated;
                }
                return;
            }
            AttachDrop(root);
        }
        catch (Exception ex)
        {
            WriteLog("HookZipDrop: " + ex.Message);
        }
    }

    private static void AttachDrop(Microsoft.UI.Xaml.FrameworkElement root)
    {
        try
        {
            // 整棵可视树都设为放置目标 + 挂同一套处理，保证窗口任意位置都能拖入。
            EnableDropOnSubtree(root);
            root.Loaded += (_, _) => EnableDropOnSubtree(root);
            // 页面导航后新子树也要补挂（祖先已有处理，这里主要覆盖新页控件）。
            try
            {
                if (Microsoft.Maui.Controls.Shell.Current is { } sh)
                    sh.Navigated += (_, _) => EnableDropOnSubtree(root);
            }
            catch (Exception ex) { WriteLog("HookZipDrop.ShellNav -> " + ex.Message); }
        }
        catch (Exception ex) { WriteLog("AttachDrop: " + ex.Message); }
    }

    /// <summary>递归把整棵可视树设为放置目标并挂上拖入处理（处理只挂一次，但 AllowDrop 每次都补开）。</summary>
    private static void EnableDropOnSubtree(Microsoft.UI.Xaml.DependencyObject node)
    {
        try
        {
            if (node is Microsoft.UI.Xaml.FrameworkElement fe)
            {
                if (_dropHooked.Add(fe))
                {
                    fe.AllowDrop = true;
                    fe.DragOver += RootDragOver;
                    fe.Drop += RootDrop;
                }
                else
                {
                    fe.AllowDrop = true;
                }
            }
            var n = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(node);
            for (var i = 0; i < n; i++)
                EnableDropOnSubtree(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(node, i));
        }
        catch (Exception ex) { WriteLog("EnableDropOnSubtree: " + ex.Message); }
    }

    private static void RootDragOver(object? _, Microsoft.UI.Xaml.DragEventArgs e)
    {
        try
        {
            if (e.DataView.Contains(global::Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
                e.AcceptedOperation = global::Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
        }
        catch (Exception ex) { WriteLog("ZipDrop.DragOver -> " + ex.Message); }
    }

    private static async void RootDrop(object? _, Microsoft.UI.Xaml.DragEventArgs e)
    {
        var deferral = e.GetDeferral();
        try
        {
            if (!e.DataView.Contains(global::Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
                return;
            e.Handled = true;
            var items = await e.DataView.GetStorageItemsAsync();
            var paths = items.OfType<global::Windows.Storage.StorageFile>()
                .Select(f => f.Path)
                .Where(p => p.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                            || p.EndsWith(".wbsave", StringComparison.OrdinalIgnoreCase)
                            || p.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (paths.Count == 0)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                    Controls.Toast.Warn("拖进来的文件不是可导入的包（支持 zip / .wbsave / 存档 json）。"));
                return;
            }

            var services = Application.Current?.Handler?.MauiContext?.Services;
            var smart = services?.GetService(typeof(Modules.DataPack.SmartImporter))
                as Modules.DataPack.SmartImporter;
            if (smart is null) return;

            _ = Task.Run(async () =>
            {
                foreach (var p in paths)
                {
                    var result = await smart.ImportAsync(p);
                    App.WriteLog($"ZipDrop({result.KindName}) {Path.GetFileName(p)} -> {result.Message}");
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        if (result.Ok) Controls.Toast.Success($"【{result.KindName}】{result.Message}");
                        else Controls.Toast.Error(result.Message);
                    });
                }
            });
        }
        catch (Exception ex)
        {
            WriteLog("ZipDrop -> " + ex);
        }
        finally
        {
            deferral.Complete();
        }
    }
#endif

    /// <summary>
    /// 崩溃兜底提示（审计 U5）：尽力弹一次对话框指路日志。崩溃路径上什么都可能坏，
    /// 所以这一步整体自吞异常（吞掉的只是「提示弹不出来」，日志那步已经先行落盘）。
    /// </summary>
    private static void TryNotifyCrash(string title, string detail)
    {
        try
        {
            var page = Application.Current?.Windows?.FirstOrDefault()?.Page;
            if (page is null) return;
            _ = MainThread.InvokeOnMainThreadAsync(async () =>
            {
                try { await page.DisplayAlert(title, detail, "知道了"); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine("crash alert: " + ex.Message); }
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("TryNotifyCrash: " + ex.Message);
        }
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        try
        {
            // 设置必须在构建界面之前读完：标题页要按 DisclaimerAcknowledged 决定是否弹免责声明；
            // 读晚了首帧拿到默认值，每次启动都会弹一遍。
            try
            {
                var sm = Application.Current?.Handler?.MauiContext?.Services
                    ?.GetService(typeof(SettingsManager)) as SettingsManager;
                sm?.RestoreBlocking();
                // 语言也必须在建界面之前落定，否则首帧先按 zh-CN 排一遍、再被 PropertyChanged 重排；
                // 启动卡的名牌是逐字建出来的，中途重排会出现错字（繁体 溫 与简体 温 不是同一个字）。
                LocalizationService.Current.SetCulture(sm?.Current.Lang ?? "zh-CN");
                // 终末地是暗色 HUD 主题：不管系统是深是浅，整窗按暗色渲染；离开它再恢复跟随系统。
                // 必须在建 AppShell 之前设，否则首帧会先按浅色排一遍、再整体重绘成暗色（一次明显闪白）。
                try
                {
                    var endfield = string.Equals(sm?.Current.ThemeName, "endfield", StringComparison.OrdinalIgnoreCase);
                    if (Application.Current is { } appTheme)
                        appTheme.UserAppTheme = endfield ? AppTheme.Dark : AppTheme.Unspecified;
                }
                catch (Exception ex) { WriteLog("CreateWindow.AppTheme: " + ex.Message); }
            }
            catch (Exception ex) { WriteLog("CreateWindow.RestoreSettings: " + ex); }

            var swStartup = System.Diagnostics.Stopwatch.StartNew();
            Services.PerfLog.SetPhase("启动 · 构建 AppShell+Window");
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
                // 关窗询问：WinUI 的 Closing 可取消，MAUI 的 Destroying 不能（见 WindowCloseGuard）
                Services.WindowCloseGuard.Attach(win, xw);
                // 原生标题栏跟随主题（终末地是暗色 HUD，白标题栏会像贴了一张纸条）。
                Platforms.Windows.WindowChrome.ApplyTitleBar(xw);
                try
                {
                    Application.Current!.RequestedThemeChanged += (_, _) =>
                        MainThread.BeginInvokeOnMainThread(() => Platforms.Windows.WindowChrome.ApplyTitleBar(xw));
                }
                catch (Exception ex) { WriteLog("CreateWindow.TitleBarHook: " + ex.Message); }
                // 不读事件参数（那个 args 类型和 MAUI 引进来的同名类型撞了），直接问窗口现在的 Bounds。
                xw.SizeChanged += (_, _) => { var b = xw.Bounds; ApplyViewport(b.Width, b.Height); };
                // 桌面拖 zip 进窗口 = 自动识别导入（见 HookZipDrop）。
                HookZipDrop(xw);
            };
#endif
            // 焦点在时压住任务栏，失焦让位；永久置顶由 WindowTaskbarFocus 自己判断并保留。
            Services.WindowTaskbarFocus.Attach(win);
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
                Services.UiStallMonitor.Stop();
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
            // 主线程卡顿看门狗：从此以后 UI 线程停摆超过 200ms 就有一条带阶段标记的 perf.log。
            Services.UiStallMonitor.Start();
            Services.PerfLog.SetPhase("启动 · 窗口就绪");
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

            // F26 启动自检：没有任何可用模型时，温和提醒去部署向导。模型名 / 来源 / 位置都由用户自己定。
            var deploySvc = services.GetService(typeof(WarmAsBefore.Services.Model.ModelDeploymentService)) as WarmAsBefore.Services.Model.ModelDeploymentService;
            if (deploySvc is not null)
            {
                _ = System.Threading.Tasks.Task.Run(async () =>
                {
                    await System.Threading.Tasks.Task.Delay(1500);
                    if (deploySvc.IsModelConfigured()) return;
                    try
                    {
                        Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(() =>
                            Controls.Toast.Warn("还没配置任何模型：去「AI 模型管理 → 模型部署向导」下载并安装（名字、来源、位置都由你定）。"));
                    }
                    catch { }
                });
            }

            // F08 启动自检：开启设置建议 AI 且到了频率时间就静默跑一次（结果在设置页查看，不弹窗不打扰）
            var advisor = services.GetService(typeof(WarmAsBefore.Services.Core.SettingsAdvisorService)) as WarmAsBefore.Services.Core.SettingsAdvisorService;
            if (advisor is not null)
            {
                _ = System.Threading.Tasks.Task.Run(async () =>
                {
                    await System.Threading.Tasks.Task.Delay(4000);
                    try { await advisor.MaybeRunAsync(); } catch { }
                });
            }

#if WINDOWS
            // 主题/明暗在 Start → Apply 里才落定：此时再刷一次标题栏，首帧与暗色 HUD 对齐。
            try
            {
                var platformWindow = Application.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView
                    as Microsoft.UI.Xaml.Window;
                Platforms.Windows.WindowChrome.ApplyTitleBar(platformWindow);
            }
            catch (Exception ex) { WriteLog("AfterWindowStarted.TitleBar: " + ex.Message); }
#endif

            // 注：启动读盘预热由 RuntimeConfigurator.WarmUpAsync（延后 4 秒的空闲预热）负责，
            // 这里不再重复一份，避免两套预热并发抢 IO。

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

    /// <summary>
    /// 同步写盘：只在「进程可能马上没了」的路径上用（未处理异常、崩溃）。
    /// 常规日志走队列是为了不噎住 UI 线程，这条路径已经没有下一次机会了。
    /// </summary>
    public static void WriteLogNow(string msg)
    {
        try
        {
            File.AppendAllText(LogPath,
                $"[{DateTime.Now.ToString("HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture)}] {msg}{Environment.NewLine}");
        }
        catch { }
    }

    /// <summary>崩溃前把队列里没落盘的几行一次写完。</summary>
    public static void FlushLog()
    {
        try
        {
            while (LogQueue.TryDequeue(out var line)) File.AppendAllText(LogPath, line);
        }
        catch { }
    }
}
