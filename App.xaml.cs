using System.IO;
using WarmAsBefore.Services;
using WarmAsBefore.Services.Core;

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
                try { BugReporter.Report(e.ExceptionObject as Exception, "AppDomain", "未处理的域异常"); } catch { /* 故意忽略：报告器自己失败时无处再报，日志已在上一行写过 */ }
                // 域级异常若 IsTerminating=true 进程仍会退出，这里尽力再弹一次指路日志的提示。
                TryNotifyCrash("温暖如初遇到问题",
                    "程序即将退出。启动日志已写入：Documents\\warm_startup.log，可把这份文件发给开发者定位问题。");
            };
            TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                WriteLog("UNOBSERVED TASK: " + e.Exception);
                try { e.SetObserved(); } catch { /* 故意忽略：SetObserved 不抛异常；真出错也没有补救手段 */ }
                // 未观察任务异常默认不会崩进程，但也要归口到 BugReporter，避免被静默吞掉。
                try { BugReporter.Report(e.Exception, "Task", "未观察的任务异常"); } catch { /* 故意忽略：同上一行：BugReporter 的失败不再上报 */ }
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
                    // 全局兜底：不让未处理异常直接终结进程。标 Handled，把错误交给 BugReporter
                    // （写 bugs.log + 弹 bug 日志窗）。这是「卡死退出」类问题的最终防线。
                    // 同步写盘不进后台队列（崩溃前那几行最要紧）。
                    e.Handled = true;
                    WriteLogNow("WINUI UNHANDLED: " + e.Exception);
                    WriteLogNow("WINUI UNHANDLED STACK: " + e.Exception?.StackTrace);
                    // 布局环取证（2026-11-15）：LayoutCycleException 原生层无堆栈，静态排查多轮未定位。
                    // 首次捕获时自抓进程 dump，事后用 dotnet-dump analyze 看 UI 线程此刻的托管调用栈，
                    // 直接定位是谁在布局风暴里反复 Measure。只抓第一次，避免每次都写几百 MB。
                    if (e.Exception is Microsoft.UI.Xaml.LayoutCycleException && _lcDumpDone)
                    {
                        // 已抓过：只记一次增量日志
                    }
                    else if (e.Exception is Microsoft.UI.Xaml.LayoutCycleException)
                    {
                        _lcDumpDone = true;
                        var dumpPath = Path.Combine(FileSystem.AppDataDirectory, "WarmAsBefore", $"layoutcycle_{DateTime.Now:HHmmss}.dmp");
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                var psi = new System.Diagnostics.ProcessStartInfo(
                                    "dotnet-dump", $"collect -p {Environment.ProcessId} -o \"{dumpPath}\"")
                                {
                                    UseShellExecute = false,
                                    CreateNoWindow = true,
                                };
                                using var p = System.Diagnostics.Process.Start(psi);
                                p?.WaitForExit(60000);
                                WriteLogNow("LC DUMP: " + dumpPath + " exit=" + (p?.ExitCode));
                            }
                            catch (Exception ex) { WriteLogNow("LC DUMP FAIL: " + ex.Message); }
                        });
                    }
                    FlushLog();
                    try { BugReporter.Report(e.Exception, "WinUI", "未处理的 UI 异常"); } catch { /* 故意忽略：同上：崩溃上报的失败无人可接 */ }
                };

#endif
        }
        catch (Exception ex)
        {
            WriteLog("App.InitializeComponent: " + ex);
            throw;
        }
    }

    /// <summary>布局环取证 dump 只抓第一次（每进程一份）。</summary>
    private static bool _lcDumpDone;

    /// <summary>上一次记录进日志的视口宽（节流：拖动时 SizeChanged 一秒几十次，全记会刷屏）。</summary>
    private static double _loggedViewportW;

    /// <summary>上一次真正下发过令牌的窗口逻辑尺寸；同尺寸不再写资源（幂等）。</summary>
    private static double _appliedViewportW = double.NaN;
    private static double _appliedViewportH = double.NaN;

    /// <summary>非法尺寸写同步日志的节流时间戳，避免最小化时的事件风暴刷盘。</summary>
    private static long _lastInvalidViewportTick;

    /// <summary>
    /// 视口下发重入闸：0=空闲，1=正在写资源。
    /// 本方法是在 WinUI 的 SizeChanged / 布局回调里被调的；写 Application.Resources 顶层键会触发
    /// ValuesChanged，让全树 {DynamicResource} 重新解析 = 再排一次版。若这次重排又冒泡出
    /// SizeChanged，就形成 WinUI 布局死循环（Layout cycle → 0xc000027b stowed exception，
    /// 进程被原生层直接带走，托管层的 UnhandledException / BugReporter 完全收不到）。
    /// </summary>
    private static int _viewportPublishing;

    /// <summary>
    /// 把窗口逻辑尺寸喂给尺度档，跨档才重发令牌。
    /// 尺度只在这一处被写：窗口、启动对齐、以后任何新入口都走这里，
    /// 免得出现「颜色跟着窗口变了、字号没变」这种半套下发。
    ///
    /// 三条硬约束（缩窗崩坏 / 女仆桥连接后卡几秒退出，都栽在它们缺席上）：
    ///  1. 入参必须有限且为正：NaN / ±∞ / 0 / 负 一律直接丢弃，既不喂尺度系统也不重发令牌。
    ///  2. 幂等 + 重入闸：同一尺寸只下发一次；布局周期内被再次调用直接丢掉。
    ///  3. 异常自吞 + 同步落盘：布局回调里抛出的异常会被 WinUI 打包成原生 stowed exception，
    ///     全局托管兜底拦不住，只能就地拦住并同步写日志（含把队列里没落盘的几行冲出去）。
    /// </summary>
    private static void ApplyViewport(double width, double height)
    {
        // 重入闸放在最前面：布局周期内被再次调用就丢掉，下一次真实尺寸变化还会再来。
        if (System.Threading.Interlocked.Exchange(ref _viewportPublishing, 1) != 0) return;
        try
        {
            // 最小化 / 句柄切换 / 销毁期，WinUI 会报 0×0，DPI 换算异常时会给出 NaN 或 ±∞。
            // 旧代码只挡了 <= 0（NaN 与任何数比较都为 false），且挡住后仍会走 RepublishWidth。
            if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0)
            {
                var now = Environment.TickCount64;
                if (now - _lastInvalidViewportTick > 2000)
                {
                    _lastInvalidViewportTick = now;
                    WriteLogNow($"[viewport] 忽略非法尺寸 {width}x{height}（不重发令牌）");
                }
                return;
            }

            // 极端值（多屏坐标、DPI 密度换算溢出）不许进资源字典。
            width = Math.Clamp(width, 1, 8192);
            height = Math.Clamp(height, 1, 8192);

            // 幂等：SizeChanged 一秒几十次，写一次顶层资源键就是整屏重排。
            if (double.IsFinite(_appliedViewportW)
                && Math.Abs(width - _appliedViewportW) < 1
                && Math.Abs(height - _appliedViewportH) < 1)
                return;

            // 诊断记录：路径是否走通、喂进来的宽度是多少（逻辑还是有效像素），差距大于 4 才记一条。
            if (Math.Abs(width - _loggedViewportW) > 4)
            {
                _loggedViewportW = width;
                WriteLog($"viewport {width:0}x{height:0} → 期望内容列 wide={Math.Clamp(Math.Round(width * 0.9), 320, 1600):0} narrow={Math.Clamp(Math.Round(width * 0.62), 320, 900):0}");
            }

            // 宽度令牌（内容列宽）跟着窗口宽度逐次重发：跨了尺度档就顺手整套重发（字号/内距要重算），
            // 没跨档也要把 WCardWidth/WCardWidthWide 刷新——以前不跨档就什么都不发，
            // 内容列宽卡在启动那一次的值上，用户实测就是「设置首页老是固定大小」。
            // 真正的「值没变就不写」由 StyleWorldTokens.PublishWidth 的 SetToken 兜底。
            if (DesignSystem.Worlds.StyleWorldTokens.SetViewport(width, height))
            {
                // ThemeManager.Current 是 DI 懒建，可能比首帧晚（见 ThemeManager.cs:44-49）。
                // 跨档这一刻若为 null，旧写法 ?. 直接短路：整套令牌与宽度令牌都不重发，
                // 而 _appliedViewportW/H 照常更新，该次跨档之后不会再补发 —— 拖窗时字号/内容列不跟手。
                // Current 在就仍交给 RepublishForScale 做整套下发（内含 PublishWidth），
                // 不重复触发整屏重排；只有取不到时才退一步补发三个宽度令牌。
                var theme = DesignSystem.Theme.ThemeManager.Current;
                if (theme is not null)
                    theme.RepublishForScale();
                else
                    DesignSystem.Worlds.StyleWorldTokens.RepublishWidth();
            }
            else
                DesignSystem.Worlds.StyleWorldTokens.RepublishWidth();

            _appliedViewportW = width;
            _appliedViewportH = height;
        }
        catch (Exception ex)
        {
            // 同步写盘：这条路径上进程可能马上就没了，不能只进后台队列。
            WriteLogNow("[viewport] ApplyViewport 自吞异常: " + ex);
            FlushLog();
        }
        finally
        {
            System.Threading.Interlocked.Exchange(ref _viewportPublishing, 0);
        }
    }

    /// <summary>
    /// Windows 桌面拖放导入：把 zip / .wbsave / 存档 json 直接拖进窗口任意位置，
    /// 自动识别包类型并交给 SmartImporter 路由加载，结果以 Toast 反馈（识别不出会说明原因）。
    ///
    /// 为什么用 HWND 级 WM_DROPFILES 而不是 WinUI 的 UIElement.DragOver/Drop：
    /// 在非 MSIX 打包的桌面 exe 里，从 Windows 资源管理器拖进来的文件走的是经典 HDROP 消息，
    /// 根本不会触发 WinUI 的 WinRT DataPackage 拖放事件。于是元素级 AllowDrop/DragOver 不管挂到
    /// 整棵可视树的哪个节点都收不到数据，OS 全程显示禁止符号、整窗无法放置。必须到 HWND 层用
    /// WM_DROPFILES 截获——这与页面 / XAML 元素无关，窗口任意位置都能放（默认全局）。
    /// </summary>
#if WINDOWS
    private static readonly HashSet<IntPtr> _dropHwndHooked = new();
    private static WndProcDelegate? _dropWndProc;   // 必须保活，否则被 GC 回收后回调变野指针会崩
    private static IntPtr _dropOldProc;

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private static void HookZipDrop(Microsoft.UI.Xaml.Window xw)
    {
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(xw);
            if (hwnd == IntPtr.Zero) return;
            if (!_dropHwndHooked.Add(hwnd)) return;   // 同一窗口只挂一次

            DropNative.DragAcceptFiles(hwnd, true);   // 告诉 OS 本窗口接受文件拖放 -> 光标变可复制
            _dropWndProc = new WndProcDelegate(DropWndProc);
            _dropOldProc = DropNative.SetWindowLongPtrW(
                hwnd, DropNative.GWLP_WNDPROC,
                System.Runtime.InteropServices.Marshal.GetFunctionPointerForDelegate(_dropWndProc));
            WriteLog($"HookZipDrop: WM_DROPFILES hooked hwnd={hwnd}");
        }
        catch (Exception ex)
        {
            WriteLog("HookZipDrop: " + ex.Message);
        }
    }

    private static IntPtr DropWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == DropNative.WM_DROPFILES)
        {
            try
            {
                var hDrop = wParam;
                uint count = DropNative.DragQueryFile(hDrop, 0xFFFFFFFFu, null, 0);
                var paths = new List<string>(capacity: (int)count);
                var sb = new System.Text.StringBuilder(32768);
                for (uint i = 0; i < count; i++)
                {
                    uint len = DropNative.DragQueryFile(hDrop, i, sb, (uint)sb.Capacity);
                    if (len > 0) paths.Add(sb.ToString(0, (int)len));
                }
                DropNative.DragFinish(hDrop);
                ImportPaths(paths);
            }
            catch (Exception ex) { WriteLog("WM_DROPFILES -> " + ex); }
            return IntPtr.Zero;   // 已处理，不再向下传递
        }
        return DropNative.CallWindowProcW(_dropOldProc, hWnd, msg, wParam, lParam);
    }

    private static void ImportPaths(List<string> paths)
    {
        var wanted = paths
            .Where(p => p.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                     || p.EndsWith(".wbsave", StringComparison.OrdinalIgnoreCase)
                     || p.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (wanted.Count == 0)
        {
            MainThread.BeginInvokeOnMainThread(() =>
                Controls.Toast.Warn("拖进来的文件不是可导入的包（支持 zip / .wbsave / 存档 json）。"));
            return;
        }

        var services = Application.Current?.Handler?.MauiContext?.Services;
        var smart = services?.GetService(typeof(Modules.DataPack.SmartImporter)) as Modules.DataPack.SmartImporter;
        if (smart is null)
        {
            WriteLog("ImportPaths: SmartImporter 未注册");
            return;
        }

        _ = Task.Run(async () =>
        {
            foreach (var p in wanted)
            {
                try
                {
                    var result = await smart.ImportAsync(p);
                    App.WriteLog($"ZipDrop({result.KindName}) {Path.GetFileName(p)} -> {result.Message}");
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        if (result.Ok) Controls.Toast.Success($"【{result.KindName}】{result.Message}");
                        else Controls.Toast.Error(result.Message);
                    });
                }
                catch (Exception ex)
                {
                    WriteLog("ImportPaths -> " + ex);
                    MainThread.BeginInvokeOnMainThread(() => Controls.Toast.Error("导入失败：" + ex.Message));
                }
            }
        });
    }

    internal static class DropNative
    {
        public const int GWLP_WNDPROC = -4;
        public const uint WM_DROPFILES = 0x0233;

        [System.Runtime.InteropServices.DllImport("shell32", SetLastError = true)]
        public static extern void DragAcceptFiles(IntPtr hWnd, bool fAccept);

        [System.Runtime.InteropServices.DllImport("shell32", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
        public static extern uint DragQueryFile(IntPtr hDrop, uint iFile, System.Text.StringBuilder? lpszFile, uint cch);

        [System.Runtime.InteropServices.DllImport("shell32", SetLastError = true)]
        public static extern void DragFinish(IntPtr hDrop);

        [System.Runtime.InteropServices.DllImport("user32", SetLastError = true)]
        public static extern IntPtr SetWindowLongPtrW(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [System.Runtime.InteropServices.DllImport("user32", SetLastError = true)]
        public static extern IntPtr CallWindowProcW(IntPtr lpPrevWndFunc, IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
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
            // 独立进程探针：3D 建链失败可能在原生侧直接带走进程（WER: combase.dll / 0x80004003），
            // 那就单独开一个进程去测它，崩了只死探针，主程序与存档完全不受影响。
            var argv = Environment.GetCommandLineArgs();
            for (var ai = 1; ai < argv.Length; ai++)
            {
                var isProbe = string.Equals(argv[ai], "--probe3d", StringComparison.OrdinalIgnoreCase);
                var isLazy = string.Equals(argv[ai], "--probe3d-hide", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(argv[ai], "--probe3d-show", StringComparison.OrdinalIgnoreCase);
                if (!isProbe && !isLazy) continue;
                var mode = isProbe ? "page" : (argv[ai].EndsWith("hide", StringComparison.OrdinalIgnoreCase) ? "hide" : "show");
                WriteLog("Probe3D: 以探针模式启动（独立进程, mode=" + mode + "）");
                return new Window(new Views.Probe3DPage(mode))
                {
                    Title = "3D 建链探针",
                    Width = 760,
                    Height = 680
                };
            }

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

            // 入口开关（渐进迁移）：WARM_WEB_SHELL=1 时整窗换成 Web 前端宿主，
            // 默认（未设置）仍是现有原生界面。注：Web 宿主不是 Shell，桥的 nav.* 在它下面不可用。
            if (Views.Web.WebShellPage.ShellRequested && TryCreateWebShellWindow() is { } webWin)
            {
                Services.PerfLog.Note("启动 · 构建 WebShell+Window", "WARM_WEB_SHELL=1");
                _ = AfterWindowStartedAsync();
                return webWin;
            }

            WriteLog("CreateWindow: building AppShell");
            var win = new Window(new AppShell())
            {
                // 窗框上只留作品名（2026-11-27）：原来的「· Warm As Before」是一串没人读的
                // 主题英文名，压在标题栏里跟界面语言对不上。任务栏与 Alt+Tab 仍然认得出这是谁。
                Title = "温暖如初",
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
                // 事件层再包一层「异常自吞 + 同步落盘」：窗口销毁期读 Bounds 也可能抛原生异常，
                // 而 SizeChanged 是 WinUI 布局回调，托管全局兜底对这里的原生崩溃无效。
                // 校验 / 幂等 / 重入闸 / 令牌写入全在 ApplyViewport 里，事件本身不重复逻辑。
                xw.SizeChanged += (_, _) =>
                {
                    try
                    {
                        var b = xw.Bounds;
                        ApplyViewport(b.Width, b.Height);
                    }
                    catch (Exception ex)
                    {
                        WriteLogNow("[viewport] SizeChanged 自吞异常: " + ex);
                        FlushLog();
                    }
                };
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
    /// 入口开关的另一半：<c>WARM_WEB_SHELL=1</c> 时建一个「页面就是 Web 宿主」的窗口。
    /// 拿不到 DI 里的页面（极少见：MauiContext 还没就绪）时返回 null，调用方回落到原生界面 ——
    /// 开关失灵应该表现为「还是老界面」，而不是启动即崩。
    /// </summary>
    private static Window? TryCreateWebShellWindow()
    {
        try
        {
            var page = Microsoft.Maui.IPlatformApplication.Current?.Services
                ?.GetService(typeof(Views.Web.WebShellPage)) as Views.Web.WebShellPage;
            if (page is null)
            {
                WriteLog("CreateWindow: WARM_WEB_SHELL=1 但取不到 WebShellPage，回落到原生界面");
                return null;
            }
            WriteLog("CreateWindow: 入口 = WebShellPage（WARM_WEB_SHELL=1）");
            var win = new Window(page)
            {
                Title = "温暖如初",
                Width = 1280,
                Height = 800
            };
            Services.WindowTaskbarFocus.Attach(win);
            win.Destroying += (_, _) =>
            {
                Services.UiStallMonitor.Stop();
                ShutdownPetAsync();
                _ = AutoSaveOnExitAsync();
            };
            return win;
        }
        catch (Exception ex)
        {
            WriteLog("CreateWindow.WebShell: " + ex);
            return null;
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
        catch { /* 故意忽略：读启动参数只为诊断，取不到就当没传 */ }
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
                    catch { /* 故意忽略：提示条弹不出来不影响后续启动流程 */ }
                });
            }

            // F08 启动自检：开启设置建议 AI 且到了频率时间就静默跑一次（结果在设置页查看，不弹窗不打扰）
            var advisor = services.GetService(typeof(WarmAsBefore.Services.Core.SettingsAdvisorService)) as WarmAsBefore.Services.Core.SettingsAdvisorService;
            if (advisor is not null)
            {
                _ = System.Threading.Tasks.Task.Run(async () =>
                {
                    await System.Threading.Tasks.Task.Delay(4000);
                    try { await advisor.MaybeRunAsync(); } catch (Exception ex) { App.WriteLog("设置建议 AI 自检 -> " + ex.Message); }
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
                catch { /* 故意忽略：日志落盘失败即丢一行，不能反过来吵醒主流程 */ }
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
        catch { /* 故意忽略：日志入队失败只能丢这一行，不能抛回调用方 */ }
    }

    /// <summary>
    /// 同步写盘：只在「进程可能马上没了」的路径上用（未处理异常、崩溃）。
    /// 常规日志走队列是为了不噎住 UI 线程，这条路径已经没有下一次机会了。
    ///
    /// 双写：MyDocuments 被 OneDrive 重定向 / 只读时，LogPath 那份 warm_startup.log
    /// 在某些机器上根本不会出现（见 Helpers\HoverFeel.cs:903-905 的同款说明），
    /// 而这里的 catch 又会静默吞掉，崩溃证据就全丢了 —— 本轮新增的 [viewport] 三条诊断
    /// 全走这条通道。所以再同步落一份到 App.RootDirectory（perf.log / bridge_diag.log
    /// 所在的数据根，也正是实机排查看的目录）。必须同步写：原生布局崩溃 0xc000027b 时
    /// PerfLog.Note 的异步队列根本来不及 flush。
    /// 两处写入各自独立自吞，任一处失败都不影响另一处，也不改变原有写入语义。
    /// </summary>
    public static void WriteLogNow(string msg)
    {
        var line = $"[{DateTime.Now.ToString("HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture)}] {msg}{Environment.NewLine}";
        try
        {
            File.AppendAllText(LogPath, line);
        }
        catch { }
        try
        {
            var mirror = Path.Combine(RootDirectory, "warm_startup.log");
            // 极端配置下两个路径可能指向同一个文件，别把同一行写两遍。
            if (!string.Equals(LogPath, mirror, StringComparison.OrdinalIgnoreCase))
            {
                Directory.CreateDirectory(RootDirectory);
                File.AppendAllText(mirror, line);
            }
        }
        catch { }
    }

    /// <summary>进存档/桥接热路径的带时间戳检查点：卡死时最后一条没配对的 step 就是卡点。
    /// 走 WriteLogNow 同步落盘（含数据根双写），崩溃前也能留下现场。</summary>
    public static void TraceStep(string tag)
    {
        try { WriteLogNow("[trace] " + tag); } catch { /* 故意忽略：trace 只用于事后定位，写不进去也不影响存档 */ }
    }

    // ============ 读档会话标记（P1，2026-11-14）============
    // 进程被 Windows 判无响应杀掉时，常规日志队列可能来不及 flush；这份同步写的小文件
    // 是「上次读取未完成」在下次启动仍成立的唯一路径。读档开始写、任一终态删。
    public const string LoadMarkerFile = "load_session.marker";
    public static string LoadMarkerPath => Path.Combine(RootDirectory, LoadMarkerFile);

    public static void WriteLoadMarker(string saveId, int phase)
    {
        try
        {
            Directory.CreateDirectory(RootDirectory);
            File.WriteAllText(LoadMarkerPath,
                $"saveId={saveId}\nphase={phase}\nts={DateTime.Now:O}\n");
        }
        catch (Exception ex) { WriteLog("App.WriteLoadMarker -> " + ex.Message); }
    }

    public static void ClearLoadMarker()
    {
        try { if (File.Exists(LoadMarkerPath)) File.Delete(LoadMarkerPath); }
        catch (Exception ex) { WriteLog("App.ClearLoadMarker -> " + ex.Message); }
    }

    /// <summary>启动后读取残留标记；有内容说明上次进程在读取中途没了。只读，不在这里删。</summary>
    public static string? PeekLoadMarker()
    {
        try { return File.Exists(LoadMarkerPath) ? File.ReadAllText(LoadMarkerPath) : null; }
        catch (Exception ex) { WriteLog("App.PeekLoadMarker -> " + ex.Message); return null; }
    }

    /// <summary>崩溃前把队列里没落盘的几行一次写完。</summary>
    public static void FlushLog()
    {
        try
        {
            while (LogQueue.TryDequeue(out var line)) File.AppendAllText(LogPath, line);
        }
        catch { /* 故意忽略：崩溃前尽力冲一次，失败也无处再报 */ }
    }
}
