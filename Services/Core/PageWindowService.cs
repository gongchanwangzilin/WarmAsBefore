using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json.Nodes;
using WarmAsBefore.Views;

namespace WarmAsBefore.Services;

/// <summary>
/// 把一页搬进独立窗口：关系网开一扇、搜索开一扇，主窗口继续聊天，两边同时看得见。
///
/// 两种开法（<see cref="ProcessMode"/> 决定，默认独立进程）：
///  1. **独立进程**（默认，<see cref="OpenStandalone"/>）：用 <see cref="Environment.ProcessPath"/> 再拉起一个自己，
///     参数 <c>--wab-standalone=&lt;route&gt;</c>。那扇窗有自己的消息循环、自己的 WebView2、自己的生命周期 ——
///     主窗卡死或崩掉时手机照样能用，反过来也一样。代价是「同一份状态」要重新读盘（设置、存档都是文件）。
///  2. **同进程第二扇窗**（<see cref="OpenPage"/>，<c>--wab-window-mode=window</c>）：老路径，事件靠单例桥广播。
///
/// 桌宠那扇是无边框分层窗口（<see cref="PetService"/> 自己管的），这里要的是普通带标题栏的工具窗，
/// 所以同进程那条路统一包成 <see cref="ToolWindow"/> —— 既有正常窗口行为，又能让「找主窗口」的那些代码认出谁不是主窗口。
///
/// 同一扇只开一个（按 key 记账）：点两下「独立窗口」就摊出四张网，不是多窗口，是刷屏。
/// 手机上没有多窗口，调用方拿到 false 之后自己退回页面内导航，不能点了没反应。
///
/// 父子互相关闭：子进程的 <see cref="Process"/> 句柄按 key 记在这里，子窗一关（进程退出）就从账上划掉；
/// 父进程退出（正常关窗或 <see cref="AppDomain.ProcessExit"/>）时 <see cref="ShutdownAll"/> 整棵树 Kill。
/// 反向：子进程里 <c>nav.openMain</c> 走 §外部的那条命名管道问父进程，因为跨进程之后单例桥不再是同一座。
/// </summary>
public sealed class PageWindowService
{
    private readonly Dictionary<string, Window> _open = new(StringComparer.Ordinal);

    /// <summary>开着的独立进程，键与 <see cref="_open"/> 同一套（<c>web:phone</c> 这种）。</summary>
    private readonly Dictionary<string, Process> _children = new(StringComparer.Ordinal);

    // ==================== 启动参数 ====================

    /// <summary>子进程标记：带这个参数启动的进程只开那一扇窗，不进主界面。</summary>
    public const string StandalonePrefix = "--wab-standalone=";
    /// <summary>窗标题（父进程给的，免得子进程自己猜路由叫什么名字）。</summary>
    public const string StandaloneTitlePrefix = "--wab-standalone-title=";
    /// <summary>窗尺寸 <c>宽x高</c>（逻辑像素，与 <see cref="Window.Width"/> 同单位）。</summary>
    public const string StandaloneSizePrefix = "--wab-standalone-size=";
    /// <summary>父进程的回问管道名：子进程要「让主窗打开某页」时写一条过去。</summary>
    public const string ParentPipePrefix = "--wab-parent-pipe=";
    /// <summary>开法开关：<c>process</c>（默认）或 <c>window</c>。</summary>
    public const string ModePrefix = "--wab-window-mode=";

    private static readonly string[] Args = Environment.GetCommandLineArgs();

    private static string? ArgValue(string prefix)
    {
        foreach (var a in Args)
            if (a.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && a.Length > prefix.Length)
                return a[prefix.Length..].Trim();
        return null;
    }

    /// <summary>本进程是不是「被父进程拉起来的那扇窗」；返回它要加载的路由（如 <c>phone</c>）。</summary>
    public static string? StandaloneRoute() => ArgValue(StandalonePrefix);

    public static bool IsStandalone => StandaloneRoute() is { Length: > 0 };

    public static string? StandaloneTitle() => ArgValue(StandaloneTitlePrefix);

    /// <summary>父进程要求的窗尺寸；没给或写坏了就 null（调用方用自己的默认）。</summary>
    public static (double Width, double Height)? StandaloneSize()
    {
        var raw = ArgValue(StandaloneSizePrefix);
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var parts = raw!.Split('x', 2);
        if (parts.Length != 2) return null;
        if (!double.TryParse(parts[0], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var w)) return null;
        if (!double.TryParse(parts[1], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var h)) return null;
        return (w, h);
    }

    /// <summary>子进程里：父进程的管道名（没有就是「没爹」，只能退回页内导航）。</summary>
    public static string? ParentPipeName() => ArgValue(ParentPipePrefix);

    /// <summary>
    /// 默认开独立进程。要回退到「同进程再开一扇窗」用 <c>--wab-window-mode=window</c>
    /// （排障时想看两扇窗共享单例桥的行为就用它）。
    /// </summary>
    public static bool ProcessMode
        => !string.Equals(ArgValue(ModePrefix), "window", StringComparison.OrdinalIgnoreCase);

    /// <summary>这台设备能不能真的另开一扇窗（手机上的多窗口是空话，只能退回页内导航）。</summary>
    public static bool Supported => DeviceInfo.Platform == DevicePlatform.WinUI;

    public PageWindowService()
    {
        // 父进程无论是正常关窗、被任务管理器结束还是崩溃退出，子进程都不该留成孤儿：
        // 正常路径由主窗 Destroying 调 ShutdownAll，这条兜住其余几种（含 Ctrl+C / 任务管理器）。
        AppDomain.CurrentDomain.ProcessExit += (_, _) => ShutdownAll();
    }

    // ==================== 独立进程 ====================

    /// <summary>
    /// 用独立进程开一扇页窗：拉起自己，参数 <c>--wab-standalone=&lt;route&gt;</c>，工作目录与宿主一致
    /// （找 web/dist 靠它往上翻）。已经开着的就把它唤到前面，不开第二份。
    /// 返回 false = 起不来（取不到 exe 路径、启动异常），调用方回落到同进程窗口 / 页内导航。
    /// </summary>
    public bool OpenStandalone(string key, string title, string route, double width = 420, double height = 820)
    {
        if (!Supported) return false;

        if (_children.TryGetValue(key, out var running))
        {
            if (!HasExited(running))
            {
                BringProcessToFront(running);
                return true;
            }
            _children.Remove(key);
        }

        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exe))
            {
                App.WriteLog("PageWindow: 取不到自身 exe 路径，独立进程开不了");
                return false;
            }

            var psi = new ProcessStartInfo(exe)
            {
                UseShellExecute = false,
                // 与宿主同目录：BundleCandidates 从工作目录往上找 web/dist，换了目录就从别处找了。
                WorkingDirectory = AppContext.BaseDirectory,
            };
            psi.ArgumentList.Add(StandalonePrefix + route);
            psi.ArgumentList.Add(StandaloneTitlePrefix + title);
            psi.ArgumentList.Add($"{StandaloneSizePrefix}{(int)Math.Max(320, width)}x{(int)Math.Max(320, height)}");
            if (ParentPipe() is { } pipe) psi.ArgumentList.Add(ParentPipePrefix + pipe);

            var child = Process.Start(psi);
            if (child is null)
            {
                App.WriteLog($"PageWindow: 独立进程没起来（{route}）");
                return false;
            }

            child.EnableRaisingEvents = true;
            child.Exited += (s, _) =>
            {
                lock (_children)
                    if (_children.TryGetValue(key, out var cur) && ReferenceEquals(cur, s)) _children.Remove(key);
                App.WriteLogNow($"PageWindow: 独立进程退出 {route}（pid={(s as Process)?.Id}）");
            };
            lock (_children) _children[key] = child;
            App.WriteLogNow($"PageWindow: 独立进程打开 {title}（{route}，pid={child.Id}）");
            return true;
        }
        catch (Exception ex)
        {
            App.WriteLog("PageWindow.OpenStandalone -> " + ex);
            return false;
        }
    }

    private static bool HasExited(Process p)
    {
        try { return p.HasExited; }
        catch { return true; }
    }

    /// <summary>已经开着的那扇：把它的窗口提到前台（独立进程的窗口句柄是它的 MainWindowHandle）。</summary>
    private static void BringProcessToFront(Process p)
    {
#if WINDOWS
        try
        {
            if (p.MainWindowHandle == IntPtr.Zero) p.WaitForInputIdle(1500);
            p.Refresh();
            var hwnd = p.MainWindowHandle;
            if (hwnd == IntPtr.Zero) return;
            ShowWindow(hwnd, SW_RESTORE);
            SetForegroundWindow(hwnd);
        }
        catch (Exception ex) { App.WriteLog("PageWindow.BringProcessToFront -> " + ex.Message); }
#endif
    }

    /// <summary>
    /// 父进程退出时把开着的子进程全部收掉（<c>entireProcessTree</c>：子进程自己再拉起来的东西也不留）。
    /// 幂等：关窗路径与 ProcessExit 路径可能都走到这里。
    /// </summary>
    public void ShutdownAll()
    {
        Process[] children;
        lock (_children) { children = _children.Values.ToArray(); _children.Clear(); }
        foreach (var p in children)
        {
            try
            {
                if (HasExited(p)) continue;
                p.Kill(entireProcessTree: true);
                App.WriteLogNow($"PageWindow: 关闭独立进程 pid={p.Id}");
            }
            catch (Exception ex) { App.WriteLog("PageWindow.ShutdownAll -> " + ex.Message); }
        }
        _pipeStop?.Cancel();
    }

#if WINDOWS
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
    private const int SW_RESTORE = 9;
#endif

    // ==================== 父进程回问管道 ====================
    //
    // 子进程点手机上的格子要「让主窗打开某页」，但跨进程之后单例桥不再共享：
    // 子进程往这条管道写一行 open <route>，父进程在 UI 线程上把 route 交给主窗的 sink 并回 ok。
    // 还没起过子进程就完全不碰管道 —— 单进程用户一个句柄都不多。

    private static readonly ConcurrentDictionary<int, string> PipeNames = new();
    private static CancellationTokenSource? _pipeStop;

    /// <summary>本进程的管道名；第一次要开子进程时才建，起监听后返回。</summary>
    private string ParentPipe()
    {
        try
        {
            var pipe = PipeNames.GetOrAdd(Environment.ProcessId, pid => "wab-page-" + pid);
            if (_pipeStop is null)
            {
                _pipeStop = new CancellationTokenSource();
                _ = Task.Run(() => PipeLoopAsync(pipe, _pipeStop.Token));
            }
            return pipe;
        }
        catch (Exception ex)
        {
            App.WriteLog("PageWindow.ParentPipe -> " + ex.Message);
            return "";
        }
    }

    private async Task PipeLoopAsync(string pipe, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(pipe, PipeDirection.InOut, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync(token).ConfigureAwait(false);

                var utf8 = new UTF8Encoding(false);
                using var reader = new StreamReader(server, utf8, false, 1024, leaveOpen: true);
                var line = (await reader.ReadLineAsync(token).ConfigureAwait(false))?.Trim();
                var ok = await HandleChildRequestAsync(line).ConfigureAwait(false);

                using var writer = new StreamWriter(server, utf8, 1024, leaveOpen: true) { AutoFlush = true };
                await writer.WriteLineAsync(ok ? "ok" : "no").ConfigureAwait(false);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                App.WriteLog("PageWindow.Pipe -> " + ex.Message);
                try { await Task.Delay(500, token).ConfigureAwait(false); } catch { return; }
            }
        }
    }

    /// <summary>
    /// 处理子进程的一条请求（目前只有 <c>open &lt;route&gt;</c>）：在 UI 线程上把它交给主窗的前端。
    /// 主窗不在、或它的前端还没上屏就回 false —— 子进程据此退回页内导航，不会点了没反应。
    /// </summary>
    private Task<bool> HandleChildRequestAsync(string? line)
    {
        if (string.IsNullOrWhiteSpace(line) || !line!.StartsWith("open ", StringComparison.Ordinal))
            return Task.FromResult(false);
        var route = line[5..].Trim();
        if (route.Length == 0) return Task.FromResult(false);

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            try
            {
                var window = MainWindow;
                if (window?.Page is not Views.Web.WebShellPage page || !page.FrontendReady)
                {
                    App.WriteLog($"PageWindow.Pipe -> 没有可接手的主窗，{route} 退回子进程页内导航");
                    tcs.TrySetResult(false);
                    return;
                }
                var envelope = new JsonObject
                {
                    ["event"] = "nav.request",
                    ["payload"] = new JsonObject { ["route"] = route },
                };
                await page.Sink(envelope.ToJsonString());
                BringToFront(window);
                App.WriteLogNow($"PageWindow.Pipe -> 主窗接手 {route}");
                tcs.TrySetResult(true);
            }
            catch (Exception ex)
            {
                App.WriteLog("PageWindow.Pipe.Handle -> " + ex.Message);
                tcs.TrySetResult(false);
            }
        });
        return tcs.Task;
    }

    /// <summary>
    /// 子进程一侧：问父进程开一页。超时 1.5 秒连不上就当父进程不在了，回 false。
    /// （父进程崩掉时管道不会自己通知，固定等一次比无限等好。）
    /// </summary>
    public static async Task<bool> AskParentAsync(string pipeName, string line)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await client.ConnectAsync(1500).ConfigureAwait(false);

            var utf8 = new UTF8Encoding(false);
            var bytes = utf8.GetBytes(line + "\n");
            await client.WriteAsync(bytes).ConfigureAwait(false);
            await client.FlushAsync().ConfigureAwait(false);

            using var reader = new StreamReader(client, utf8, false, 1024, leaveOpen: true);
            var answer = (await reader.ReadLineAsync().ConfigureAwait(false))?.Trim();
            return string.Equals(answer, "ok", StringComparison.Ordinal);
        }
        catch (Exception ex)
        {
            App.WriteLog("PageWindow.AskParent -> " + ex.Message);
            return false;
        }
    }

    /// <summary>开一扇工具窗；已经开着就把它唤到前面。返回 false = 这平台开不了。</summary>
    public bool Open(string key, string title, Type pageType,
        IDictionary<string, object>? query = null, double width = 980, double height = 700)
    {
        if (Application.Current is not { } app) return false;
        if (Exists(key))
        {
            BringToFront(_open[key]);
            return true;
        }
        if (app.Handler?.MauiContext?.Services is not { } services) return false;
        if (services.GetService(pageType) is not Page page)
        {
            App.WriteLog($"PageWindow: {pageType.Name} 没注册到 DI，开不了窗");
            return false;
        }

        var q = query is null ? new Dictionary<string, object>() : new Dictionary<string, object>(query);
        // 告诉页面「你在这回是自己一扇窗」：窗里的「←」不该去翻主窗口的导航栈，直接不显示它。
        q["inWindow"] = "1";
        // Shell 导航时页面和它的 BindingContext 都会收到参数，自己开窗也得照做：
        // 这几个页面的参数是视图模型在接的，只递给 Page 就等于谁也没接到，页面会拿当前主角去猜。
        if (page is IQueryAttributable qa) qa.ApplyQueryAttributes(q);
        if (page.BindingContext is IQueryAttributable vmq && !ReferenceEquals(vmq, page as IQueryAttributable))
            vmq.ApplyQueryAttributes(q);

        var win = new ToolWindow(page)
        {
            Title = title,
            Width = Math.Max(420, width),
            Height = Math.Max(360, height)
        };
        win.Destroying += (_, _) => _open.Remove(key);
        app.OpenWindow(win);
        _open[key] = win;
        App.WriteLog($"PageWindow: 打开 {title}（{pageType.Name}）");
        return true;
    }

    /// <summary>
    /// 开一扇窗，装调用方**自己造好的**页面实例（Web 宿主就是这一种：它要带一个初始路由，
    /// 而 DI 只会造一个默认页）。其余行为与 <see cref="Open"/> 一致：同 key 只一扇。
    /// </summary>
    public bool OpenPage(string key, string title, Page page, double width = 980, double height = 700)
    {
        if (Application.Current is not { } app) return false;
        if (Exists(key))
        {
            BringToFront(_open[key]);
            return true;
        }
        var win = new ToolWindow(page)
        {
            Title = title,
            Width = Math.Max(320, width),
            Height = Math.Max(320, height),
        };
        win.Destroying += (_, _) => _open.Remove(key);
        app.OpenWindow(win);
        _open[key] = win;
        App.WriteLog($"PageWindow: 打开 {title}（{page.GetType().Name}）");
        return true;
    }

    private bool Exists(string key)
        => _open.TryGetValue(key, out var w) && Application.Current?.Windows.Contains(w) == true;

    /// <summary>
    /// 主窗口：启动时那扇 Web 宿主窗。手机、关系网这些是 <see cref="ToolWindow"/> 里的一页，
    /// 而且页面自己带着 <c>StartAt</c> 给的起始路由 —— 两个条件一起看，桌宠窗与工具窗都不会被认成主窗。
    ///
    /// 「手机里点格子、页面回主窗显示」靠它认人：没有这个标记时，谁也没法只说主窗那一扇。
    /// </summary>
    public Window? MainWindow
    {
        get
        {
            var windows = Application.Current?.Windows;
            if (windows is null) return null;
            foreach (var w in windows)
                if (w.Page is Views.Web.WebShellPage page && !page.IsDetachedWindow) return w;
            return null;
        }
    }

    /// <summary>
    /// 唤到前面：MAUI 的 <see cref="Window"/> 没有「激活」这个动作，它在平台窗口上（桌宠找回主窗口也是这么做的）。
    /// 最小化过的窗只 Activate 不会自己弹回来，得先 Show。
    /// </summary>
    public static void BringToFront(Window w)
    {
#if WINDOWS
        if (w.Handler?.PlatformView is Microsoft.UI.Xaml.Window xw)
        {
            xw.AppWindow.Show();
            xw.Activate();
        }
#endif
    }

    /// <summary>开不了窗时的退路：走 Shell 导航，功能不缺，只是少一扇窗。</summary>
    public async Task<bool> OpenOrNavigateAsync(string key, string title, Type pageType, string route,
        IDictionary<string, object>? query = null, double width = 980, double height = 700)
    {
        if (Supported && Open(key, title, pageType, query, width, height)) return true;
        var qs = BuildQuery(route, query);
        await Shell.Current.GoToAsync(qs);
        return true;
    }

    /// <summary>把 query 拼成 Shell 的 route?a=1&amp;b=2（值全部转义，路径由调用方给死的常量）。</summary>
    private static string BuildQuery(string route, IDictionary<string, object>? query)
    {
        if (query is null || query.Count == 0) return route;
        var parts = query.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value?.ToString() ?? "")}");
        return route + "?" + string.Join("&", parts);
    }

    /// <summary>关掉某一扇（页面自己要求收起时用）。</summary>
    public void Close(string key)
    {
        if (!_open.TryGetValue(key, out var w)) return;
        _open.Remove(key);
        if (Application.Current is { } app && app.Windows.Contains(w)) app.CloseWindow(w);
    }
}

/// <summary>
/// 工具窗的记账键，同时也是 Shell 路由名（<c>AppShell</c> 里注册的那两个）。
/// 两处必须同一个字符串：键和路由各写一份，就会变成「点两下开两扇」和「手机上跳不到页」。
/// </summary>
public static class PageWindowKeys
{
    public const string Graph = "memoryGraph";
    public const string Search = "memorySearch";
    /// <summary>手机桌面：主窗口聊天时把手机摊在一边。键同时是 Shell 路由（<c>AppShell</c> 里注册的 phone）。</summary>
    public const string Phone = "phone";
}
