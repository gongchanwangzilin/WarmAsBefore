using System.Text.Json;
using WarmAsBefore.DesignSystem.Theme;
using WarmAsBefore.Helpers;
using WarmAsBefore.Services.Web;

namespace WarmAsBefore.Views.Web;

/// <summary>
/// Web 前端宿主页：一块 WebView 加一条 <see cref="WebBridge"/>。
///
/// 地址解析（四级，顺序固定，见 <see cref="ResolveSource"/>）：
///  1. <c>WARM_WEB_PROBE=1</c> —— 诊断页（内联 HTML，只有显式开关才可达；默认完全不存在）；
///  2. <c>WARM_WEB_URL</c>    —— 显式指定的 dev server（开发期热更新）；
///  3. 构建产物 <c>web/dist</c> —— **默认来源**：整目录挂到虚拟主机 <c>https://wab.local/</c>，
///     加载 <c>index.html</c>（Vite 产物的 <c>/assets/…</c> 绝对路径因此正好落在映射根上）；
///  4. 都没有 —— 不加载任何页面，窗口里写明「没有找到 web/dist，先跑 npm run build」和查过的路径。
///
/// 为什么不默认连 dev server：dev server 要人先起进程，没起时窗口一片空白（或错误页），
/// 用户看到的不是应用；构建产物是随仓库一起走的那一份，谁打开都是应用本身。
///
/// 事件通道（Windows）：
///   收 —— <c>CoreWebView2.WebMessageReceived</c>（前端 <c>window.chrome.webview.postMessage(obj)</c>）；
///   发 —— <c>CoreWebView2.PostWebMessageAsJson</c>（前端 <c>window.chrome.webview.addEventListener('message', …)</c>）。
///   MAUI 的 <see cref="WebView"/> 只是个壳，WebView2 本体要从 <c>Handler.PlatformView</c> 拿，
///   且必须等 <c>EnsureCoreWebView2Async()</c> 完成之后才能挂事件、做主机映射。
///
/// Android 上同一份协议换一层皮：前端 <c>window.WabBridge.postMessage(json)</c> 进 JavascriptInterface，
/// 宿主回过去是 <c>window.__wabBridgeDispatch(jsonText)</c>。
/// </summary>
public partial class WebShellPage : ContentPage
{
    /// <summary>
    /// 入口：**默认就是 Web 宿主**（迁移动完成了，前端是正式界面）。
    /// <c>WARM_NATIVE_SHELL=1</c> 退回原生 Shell（对照、排障用）；
    /// <c>WARM_WEB_SHELL=1</c> 仍然有效，等于默认值。
    /// 找不到 web/dist 时 <see cref="App.CreateWindow"/> 会自动落回原生界面，所以默认开不会开出一个空窗。
    /// </summary>
    /// <summary>
    /// 正在用 Web 宿主接管界面。原生提示控件照这个让位：条子该由前端画，
    /// 但原生 Shell 还在用时不能把提示静默吞掉（那才是真的少了东西）。
    /// </summary>
    public static bool Enabled { get; private set; }

    public static bool ShellRequested
        => !string.Equals(Environment.GetEnvironmentVariable("WARM_NATIVE_SHELL")?.Trim(), "1", StringComparison.Ordinal);

    /// <summary>虚拟主机名：WebView2 只把它映射到磁盘目录，不联网。</summary>
    private const string AssetHost = "wab.local";

    /// <summary>素材主机：存储根（立绘、头像、背景…）。前端用 https://wab.assets/&lt;相对路径&gt; 取。</summary>
    public const string AssetHostName = "wab.assets";

    /// <summary>素材根目录 = StorageProvider.Root；取不到就是 null（那就只不挂素材，页面照旧）。</summary>
    public static string? AssetsRoot()
    {
        try
        {
            return Application.Current?.Handler?.MauiContext?.Services?
                .GetService(typeof(WarmAsBefore.Services.StorageProvider)) is WarmAsBefore.Services.StorageProvider store
                ? store.Root
                : null;
        }
        catch { return null; }
    }

    /// <summary>发布/构建产物目录名（Vite 的 <c>npm run build</c> 输出）。</summary>
    private const string BundleDir = "dist";

    private readonly WebBridge _bridge;
    private WebSource _source;
    private bool _attached;
    private bool _domChecked;

    public WebShellPage(WebBridge bridge)
    {
        InitializeComponent();
        _bridge = bridge;
        Enabled = true;
        _sink = SendAsync;
        _bridge.AttachSink(_sink);
        _source = ResolveSource();
        App.WriteLogNow($"WebShellPage: 加载 {(_source.Kind == WebSourceKind.Missing ? "(无)" : _source.Url)}"
            + $"（来源={_source.Kind.ToString().ToLowerInvariant()}"
            + (_source.Folder is null ? "" : $"，目录={_source.Folder}") + "）");
    }

    /// <summary>这一页是不是装在自己那扇窗口里（手机页那种）。窗里的「←」应该关窗，不是翻主窗的栈。</summary>
    public bool IsDetachedWindow => _startRoute is not null;

    private string? _startRoute;

    /// <summary>
    /// 这**一扇**窗的前端上屏了没有。<see cref="WebBridge.WebFrontendReady"/> 是桥上的一个开关，
    /// 而桥是单例、几扇窗共用 —— 手机窗先加载完时，主窗那个判断会跟着变真。
    /// 「这条路由交给主窗」必须按窗判断，所以这份状态放在页上。
    /// </summary>
    public bool FrontendReady { get; private set; }

    /// <summary>
    /// 这一扇窗自己的收件地址。桥的 <c>Emit</c> 是广播（所有窗都挂一条 sink），
    /// 定向只叫主窗时（<c>nav.openMain</c>）要拿它，不能广播。
    /// </summary>
    public Func<string, Task> Sink => _sink;

    private readonly Func<string, Task> _sink;

    /// <summary>
    /// 让**这一扇**窗口从某个路由开始（<c>/phone</c> 这类）。
    /// 主机方（<see cref="WebBridge"/>）在开窗之前调用；主窗不调，走默认入口。
    /// </summary>
    public void StartAt(string route)
    {
        _startRoute = route;
        _source = ResolveSource(route);
        App.WriteLogNow($"WebShellPage: 独立窗加载 {_source.Url}");
    }

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
        _ = AttachAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        if (ThemeManager.Current is { } tm) tm.Applied -= OnThemeChanged;
    }

    // ==================== 地址解析 ====================

    /// <summary>前端来自哪里。决定窗口里到底显示什么，也决定启动日志里那一行「来源=」。</summary>
    public enum WebSourceKind
    {
        /// <summary>构建产物（默认）。</summary>
        Bundle,
        /// <summary>显式指定的 dev server（<c>WARM_WEB_URL</c>）。</summary>
        Dev,
        /// <summary>内联诊断页（<c>WARM_WEB_PROBE=1</c>）。</summary>
        Probe,
        /// <summary>一个都没有：窗口里显示错误与查过的路径。</summary>
        Missing,
    }

    /// <summary>解析结果：<see cref="Folder"/> 非空表示要把这个目录挂到虚拟主机上。</summary>
    public readonly record struct WebSource(WebSourceKind Kind, string Url, string? Folder, string Detail);

    /// <summary>按文档注释里的四级顺序解析；默认（没有任何环境变量）是构建产物。</summary>
    public static WebSource ResolveSource(string? forcedRoute = null)
    {
        if (string.Equals(Environment.GetEnvironmentVariable("WARM_WEB_PROBE")?.Trim(), "1", StringComparison.Ordinal))
            return new WebSource(WebSourceKind.Probe, "about:blank", null, "WARM_WEB_PROBE=1");

        var devUrl = Environment.GetEnvironmentVariable("WARM_WEB_URL")?.Trim();
        var source = !string.IsNullOrWhiteSpace(devUrl)
            ? new WebSource(WebSourceKind.Dev, devUrl!, null, "WARM_WEB_URL")
            : FindBundle() is { } bundle
                ? new WebSource(WebSourceKind.Bundle, $"https://{AssetHost}/index.html", bundle, "web/" + BundleDir)
                : new WebSource(WebSourceKind.Missing, "", null, string.Join("\n", BundleCandidates()));

        // 显式指定的路由（独立窗的 /phone）优先；验收用的 WARM_WEB_ROUTE 次之。
        var route = string.IsNullOrWhiteSpace(forcedRoute)
            ? Environment.GetEnvironmentVariable("WARM_WEB_ROUTE")?.Trim()
            : forcedRoute;
        if (string.IsNullOrWhiteSpace(route) || source.Kind == WebSourceKind.Missing) return source;
        var hash = route!.StartsWith('#') ? route : "#" + (route.StartsWith('/') ? route : "/" + route);
        return source with { Url = source.Url + hash };
    }

    /// <summary>
    /// 找构建产物：先看显式覆盖 <c>WARM_WEB_BUNDLE</c>，再看输出目录旁边的 <c>web/dist</c>，
    /// 再从输出目录和工作目录往上一层层找 <c>web/dist</c>（开发期 exe 在 bin/… 深处，
    /// 仓库根的 web/dist 只有往上找才够得到），最后认旧的打包位置 <c>Resources/Raw/web</c>。
    /// </summary>
    private static string? FindBundle()
    {
        foreach (var dir in BundleCandidates())
            if (Directory.Exists(dir) && File.Exists(Path.Combine(dir, "index.html"))) return dir;
        return null;
    }

    /// <summary>候选目录，按优先级排列；找不到时这份清单会原样显示在窗口里，省得猜。</summary>
    private static List<string> BundleCandidates()
    {
        var list = new List<string>();
        var explicitDir = Environment.GetEnvironmentVariable("WARM_WEB_BUNDLE")?.Trim();
        if (!string.IsNullOrEmpty(explicitDir)) list.Add(explicitDir!);

        var baseDir = AppContext.BaseDirectory;
        list.Add(Path.Combine(baseDir, "web", BundleDir));
        list.Add(Path.Combine(baseDir, "Resources", "Raw", "web"));

        // 开发期：bin/Debug/<tfm>/<rid>/ 往上到仓库根，才看得到 web/dist。
        foreach (var start in new[] { baseDir, Directory.GetCurrentDirectory() })
        {
            try
            {
                for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
                {
                    var candidate = Path.Combine(dir.FullName, "web", BundleDir);
                    if (!list.Contains(candidate)) list.Add(candidate);
                    if (dir.Parent is null) break;
                }
            }
            catch (Exception ex)
            {
                App.WriteLog("WebShellPage.BundleCandidates -> " + ex.Message);
            }
        }
        return list;
    }

    // ==================== 装配 ====================

    private async Task AttachAsync()
    {
        if (_attached) return;
        try
        {
            if (_source.Kind == WebSourceKind.Missing)
            {
                // 不加载任何页面：窗口里只说清楚缺什么、去哪儿找。空白页看起来像程序坏了。
                ShowFallback("没有找到 web/dist，先跑 npm run build", "查过这些位置：\n" + _source.Detail);
                return;
            }

#if WINDOWS
            if (Browser.Handler?.PlatformView is not Microsoft.UI.Xaml.Controls.WebView2 web) return;
            _attached = true;
            await web.EnsureCoreWebView2Async();
            var core = web.CoreWebView2;
            if (core is null)
            {
                ShowFallback("WebView2 内核没有就绪。", "系统里可能缺少 Microsoft Edge WebView2 运行时。");
                return;
            }

            if (_source.Kind == WebSourceKind.Bundle && _source.Folder is { } folder)
            {
                // 主机映射必须在第一次导航之前：映射之后 https://wab.local/… 才解析到这个目录。
                // Allow：同一个映射源内部还要取 /assets/*.js|css，前端也可能用 fetch 读同源资源。
                core.SetVirtualHostNameToFolderMapping(
                    AssetHost, folder, Microsoft.Web.WebView2.Core.CoreWebView2HostResourceAccessKind.Allow);
            }

            core.Settings.IsWebMessageEnabled = true;

            // 页面不缩放：版式只随窗口/屏幕变。Ctrl+滚轮、捏合、双击三种入口一起关掉。
            core.Settings.IsZoomControlEnabled = false;
            core.Settings.IsPinchZoomEnabled = false;
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;

            // 这是一扇应用窗口，不是浏览器窗口：WebView2 自带的浏览器暴露面全部关掉。
            // 右键菜单（返回/刷新/另存为/打印/发送标签页/检查）、开发者工具、左下角悬停的链接地址条、
            // alert/confirm/prompt 那种脚本对话框、触控板侧滑前进后退、表单自动填充与密码保存。
            // 页面里没有一处需要它们；关掉之后用户看到的只有应用本身。
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.AreDevToolsEnabled = DevToolsAllowed();
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.AreDefaultScriptDialogsEnabled = false;
            core.Settings.IsSwipeNavigationEnabled = false;
            core.Settings.IsGeneralAutofillEnabled = false;
            core.Settings.IsPasswordAutosaveEnabled = false;

            // 验收与排障：这些开关只在每次创建内核时读一次，窗口看起来还是「像浏览器」时先看这一行。
            App.WriteLogNow(
                "WebShellPage: 浏览器面 context=" + core.Settings.AreDefaultContextMenusEnabled
                + " devtools=" + core.Settings.AreDevToolsEnabled
                + " status=" + core.Settings.IsStatusBarEnabled
                + " dialogs=" + core.Settings.AreDefaultScriptDialogsEnabled
                + " swipe=" + core.Settings.IsSwipeNavigationEnabled
                + " autofill=" + core.Settings.IsGeneralAutofillEnabled + "/" + core.Settings.IsPasswordAutosaveEnabled
                + " zoom=" + core.Settings.IsZoomControlEnabled + "/" + core.Settings.IsPinchZoomEnabled
                + " keys=" + core.Settings.AreBrowserAcceleratorKeysEnabled);

            // 素材（立绘/头像/背景）走第二个虚拟主机：同一台机器上的文件目录，
            // 不联网、不走环回端口，`https://wab.assets/<相对路径>` 直接解析到存储根。
            if (AssetsRoot() is { } root && Directory.Exists(root))
            {
                core.SetVirtualHostNameToFolderMapping(
                    AssetHostName, root, Microsoft.Web.WebView2.Core.CoreWebView2HostResourceAccessKind.Allow);
            }

            core.WebMessageReceived += OnWebMessageReceived;
            core.NavigationCompleted += OnNavigationCompleted;

            if (ThemeManager.Current is { } tm) tm.Applied += OnThemeChanged;

            if (_source.Kind == WebSourceKind.Probe) core.NavigateToString(ProbeHtml);
            else Browser.Source = _source.Url;
#elif ANDROID
            if (Browser.Handler?.PlatformView is not Android.Webkit.WebView view) return;
            _attached = true;
            view.Settings.JavaScriptEnabled = true;

            // 与 Windows 那份设置同一件事：Android 的 WebView 也自带一套浏览器能力 ——
            // 捏合缩放（连缩放按钮一起）、长按弹「保存图片 / 复制链接」的滑出菜单、
            // 拖到边界时整页跟着晃的回弹。全部收掉，窗口里只剩应用。
            view.Settings.SetSupportZoom(false);
            view.Settings.BuiltInZoomControls = false;
            view.Settings.DisplayZoomControls = false;
            view.Settings.SetTextZoom(100);
            view.LongClickable = false;
            view.HapticFeedbackEnabled = false;
            view.OverScrollMode = Android.Views.OverScrollMode.Never;
            view.LongClick += (_, e) => e.Handled = true;

            view.AddJavascriptInterface(new AndroidHostInterface(this), "WabBridge");
            view.SetWebViewClient(new AndroidLoadClient(this));
            if (ThemeManager.Current is { } tm) tm.Applied += OnThemeChanged;

            if (_source.Kind == WebSourceKind.Probe)
                view.LoadDataWithBaseURL(null, ProbeHtml, "text/html", "utf-8", null);
            else
                Browser.Source = _source.Url;
#else
            await Task.CompletedTask;
#endif
        }
        catch (Exception ex)
        {
            App.WriteLog("WebShellPage.Attach -> " + ex);
            ShowFallback("宿主没起来。", ex.Message);
        }
    }

    /// <summary>
    /// 开发者工具默认关：它是浏览器的东西，不是应用的。调试期要 F12 / 检查元素，
    /// 显式给 <c>WARM_WEB_DEVTOOLS=1</c>（与 <c>WARM_WEB_EVAL</c>、<c>WARM_WEB_SHOT</c> 同一族验收开关）。
    /// </summary>
    private static bool DevToolsAllowed()
        => string.Equals(Environment.GetEnvironmentVariable("WARM_WEB_DEVTOOLS")?.Trim(), "1", StringComparison.Ordinal);

    // ==================== 收：前端 → 宿主 ====================

#if WINDOWS
    private void OnWebMessageReceived(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs e)
    {
        string json;
        try
        {
            var raw = e.WebMessageAsJson;
            // 前端按协议 postMessage(对象) → 这里是对象 JSON；万一有人 postMessage(字符串)，
            // 拿到的是一段带引号的 JSON 字符串，先解一层再交给桥。
            json = raw.TrimStart().StartsWith('"')
                ? JsonSerializer.Deserialize<string>(raw) ?? ""
                : raw;
        }
        catch (Exception ex)
        {
            App.WriteLog("WebShellPage.OnWebMessageReceived -> " + ex.Message);
            return;
        }
        Receive(json);
    }

    private void OnNavigationCompleted(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2NavigationCompletedEventArgs e)
    {
        if (!e.IsSuccess)
        {
            // 开发期最常见的两种：dev server 没起、构建产物路径不对。把地址一起写出来，省一轮猜。
            _bridge.WebFrontendReady = false;
            FrontendReady = false;
            ShowFallback("前端页面没打开。",
                DateTime.Now.ToString("HH:mm:ss") + " · " + _source.Url + " · " + e.WebErrorStatus);
            App.WriteLogNow($"WebShellPage: 导航失败 {_source.Url} -> {e.WebErrorStatus}");
            return;
        }
        HideFallback();
        // 前端上屏即接管路由：此后 nav.open 走 nav.request 事件，而不是 Shell / deferred。
        _bridge.WebFrontendReady = true;
        FrontendReady = true;
        App.WriteLogNow("WebShellPage: 前端已就绪 " + _source.Url);
        _bridge.Emit("host.ready", new { url = _source.Url, packaged = _source.Kind == WebSourceKind.Bundle });
        _ = LogDomAsync();
    }

    /// <summary>
    /// 装载后的取证：标题、<c>#root</c> 在不在、正文长度。用来区分「应用真的渲染了」
    /// 与「加载到了别的页面（比如一段纯文本）」—— 后者只有正文、没有根节点。
    /// 取两次：第 0 秒（DOM 刚可用）与 1.2 秒后（React 首帧 + 首页数据都到位）。
    /// </summary>
    private async Task LogDomAsync()
    {
        if (_domChecked) return;
        _domChecked = true;
        try
        {
            if (Browser.Handler?.PlatformView is not Microsoft.UI.Xaml.Controls.WebView2 { CoreWebView2: { } core }) return;
            await SampleDomAsync(core, "t0");
            await Task.Delay(1200);
            await SampleDomAsync(core, "t1");

            // 验收钩子：WARM_WEB_EVAL 里放一段 JS，跑完把结果写进日志。
            // 它只在显式设置时执行，用来在真窗口里点按钮、读 DOM（这台机器没有桌面，点击只能这样发生）。
            var script = Environment.GetEnvironmentVariable("WARM_WEB_EVAL")?.Trim();
            if (!string.IsNullOrWhiteSpace(script))
            {
                // 脚本是 async 的，ExecuteScriptAsync 只会把 Promise 序列化成 {}；
                // 所以先把结果落到 window.__evalResult，再轮询取回来。
                var wrapped =
                    "window.__evalResult=null;(async()=>{try{window.__evalResult=String(await (" + script + "));}"
                    + "catch(e){window.__evalResult='EVAL_ERROR '+e.message;}})();";
                await core.ExecuteScriptAsync(wrapped);
                string? result = null;
                for (var i = 0; i < 60 && result is null; i++)
                {
                    await Task.Delay(500);
                    var raw = await core.ExecuteScriptAsync("window.__evalResult");
                    if (raw is null or "null") continue;
                    result = raw;
                }
                App.WriteLogNow("WebShellPage: EVAL -> " + (result is null ? "(timeout)" : result.Length > 6000 ? result[..6000] + "…" : result));
            }

            // 截图钩子：WARM_WEB_SHOT=<png 路径>。给验收与设计复盘用 ——
            // 这台机器没有桌面，像素只能由应用自己抓（CapturePreviewAsync），存成 PNG 谁都能打开看。
            var shot = Environment.GetEnvironmentVariable("WARM_WEB_SHOT")?.Trim();
            if (!string.IsNullOrWhiteSpace(shot))
            {
                try
                {
                    var dir = Path.GetDirectoryName(shot);
                    if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
                    await using var file = File.Create(shot!);
                    await core.CapturePreviewAsync(
                        Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat.Png, file.AsRandomAccessStream());
                    App.WriteLogNow($"WebShellPage: SHOT -> {shot}（{new FileInfo(shot!).Length / 1024} KB）");
                }
                catch (Exception ex)
                {
                    App.WriteLogNow("WebShellPage: SHOT -> " + ex.Message);
                }
            }
        }
        catch (Exception ex)
        {
            App.WriteLog("WebShellPage.LogDom -> " + ex.Message);
        }
    }

    private static async Task SampleDomAsync(Microsoft.Web.WebView2.Core.CoreWebView2 core, string when)
    {
        var title = await core.ExecuteScriptAsync("document.title");
        var root = await core.ExecuteScriptAsync("!!document.getElementById('root')");
        var textLen = await core.ExecuteScriptAsync("document.body.innerText.length");
        var pre = await core.ExecuteScriptAsync("document.querySelectorAll('pre').length");
        App.WriteLogNow($"WebShellPage: DOM[{when}] title={title} root={root} textLen={textLen} pre={pre}");
    }
#endif

#if ANDROID
    /// <summary>Android 的收口：<c>window.WabBridge.postMessage(JSON.stringify(msg))</c>。</summary>
    private sealed class AndroidHostInterface : Java.Lang.Object
    {
        private readonly WebShellPage _page;
        public AndroidHostInterface(WebShellPage page) => _page = page;

        [Android.Runtime.Register("postMessage", "(Ljava/lang/String;)V", "GetPostMessageHandler")]
        [Android.Webkit.JavascriptInterface]
        public void PostMessage(string? json) => _page.Receive(json);
    }

    /// <summary>页面装载完成 = 前端接管路由（与 Windows 的 NavigationCompleted 同一语义）。</summary>
    private sealed class AndroidLoadClient : Android.Webkit.WebViewClient
    {
        private readonly WebShellPage _page;
        public AndroidLoadClient(WebShellPage page) => _page = page;

        public override void OnPageFinished(Android.Webkit.WebView? view, string? url)
        {
            _page._bridge.WebFrontendReady = true;
            _page.FrontendReady = true;
            App.WriteLogNow("WebShellPage: 前端已就绪 " + (url ?? _page._source.Url));
            _page._bridge.Emit("host.ready", new { url = url ?? _page._source.Url, packaged = _page._source.Kind == WebSourceKind.Bundle });
        }
    }
#endif

    private void Receive(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return;
        _ = _bridge.ReceiveAsync(json);
    }

    // ==================== 发：宿主 → 前端 ====================

    private Task SendAsync(string json)
    {
#if WINDOWS
        var core = (Browser.Handler?.PlatformView as Microsoft.UI.Xaml.Controls.WebView2)?.CoreWebView2;
        if (core is null) return Task.CompletedTask;
        return Ui.PostAsync(() =>
        {
            try { core.PostWebMessageAsJson(json); }
            catch (Exception ex) { App.WriteLog("WebShellPage.SendAsync -> " + ex.Message); }
            return Task.CompletedTask;
        });
#elif ANDROID
        var view = Browser.Handler?.PlatformView as Android.Webkit.WebView;
        if (view is null) return Task.CompletedTask;
        return Ui.PostAsync(() =>
        {
            try
            {
                // 传的是 JSON 文本（再包一层 JS 字符串字面量），前端自己 JSON.parse —— 不拼可执行代码片段。
                var literal = JsonSerializer.Serialize(json);
                view.EvaluateJavascript($"window.__wabBridgeDispatch && window.__wabBridgeDispatch({literal});", null);
            }
            catch (Exception ex) { App.WriteLog("WebShellPage.SendAsync -> " + ex.Message); }
            return Task.CompletedTask;
        });
#else
        return Task.CompletedTask;
#endif
    }

    // ==================== 主题跟随 ====================

    private void OnThemeChanged()
        => _bridge.Emit("theme.tokens", new { tokens = _bridge.TokensSnapshot() });

    // ==================== 诊断页（WARM_WEB_PROBE=1）====================

    /// <summary>
    /// 内联诊断页：只做一件事 —— 把几条桥调用打印成文字。它不是应用页面，默认不会被加载，
    /// 也不占仓库里的任何文件（不会有人误以为它是前端入口）。
    /// </summary>
    private const string ProbeHtml = """
        <!doctype html><html lang="zh-CN"><head><meta charset="utf-8"><title>WAB bridge probe</title>
        <style>body{font:14px/1.5 system-ui;padding:16px;background:#fdf8f0;color:#121212}pre{white-space:pre-wrap}</style>
        </head><body><h1 id="status">probing…</h1><pre id="out"></pre><script>
        const out=[],el=id=>document.getElementById(id);
        const log=s=>{out.push(s);el('out').textContent=out.join('\n')};
        const send=(id,method,params)=>new Promise((res,rej)=>{
          const on=e=>{const m=e.data;if(m&&m.id===id){window.chrome.webview.removeEventListener('message',on);res(m)}};
          window.chrome.webview.addEventListener('message',on);
          window.chrome.webview.postMessage({id,method,params:params||{}});
          setTimeout(()=>rej(new Error('timeout '+method)),5000)});
        (async()=>{try{
          const info=await send(1,'app.info');log('app.info -> '+JSON.stringify(info.result));
          const tok=await send(2,'ui.tokens');
          log('ui.tokens -> theme='+tok.result.theme+' '+Object.keys(tok.result.tokens).length+' 个 --w-* 键');
          log('  --w-bg='+tok.result.tokens['--w-bg']+' --w-signal='+tok.result.tokens['--w-signal']+' --w-radius-card='+tok.result.tokens['--w-radius-card']);
          const st=await send(3,'settings.get');
          log('settings.get -> '+JSON.stringify(st.result.values)+' save='+(st.result.save?st.result.save.character:'null'));
          const unk=await send(4,'no.such.method');log('unknown method -> ok='+unk.ok+' error='+JSON.stringify(unk.error));
          const nav=await send(5,'nav.open',{route:'/'});log('nav.open -> '+JSON.stringify(nav.result));
          const good = tok.result && tok.result.tokens && tok.result.theme
            && st.result && st.result.values && st.result.values.glass
            && unk.ok === false && unk.error && unk.error.code === 'E_UNKNOWN_METHOD'
            && nav.result && nav.result.accepted === true;
          document.title = good ? 'PROBE OK' : 'PROBE BAD';
          el('status').textContent=good?'PROBE OK':'PROBE BAD';
        }catch(e){log('FAIL '+e.message);document.title='PROBE BAD';el('status').textContent='PROBE FAILED'}})();
        </script></body></html>
        """;

    // ==================== 失败提示 ====================

    private void OnRetryClicked(object sender, EventArgs e)
    {
        HideFallback();
        _bridge.WebFrontendReady = false;
        FrontendReady = false;
        _domChecked = false;
        _source = ResolveSource();
        App.WriteLogNow($"WebShellPage: 重试加载 {(_source.Kind == WebSourceKind.Missing ? "(无)" : _source.Url)}（来源={_source.Kind}）");
        if (_source.Kind == WebSourceKind.Missing)
        {
            ShowFallback("没有找到 web/dist，先跑 npm run build", "查过这些位置：\n" + _source.Detail);
            return;
        }
#if WINDOWS
        if (Browser.Handler?.PlatformView is Microsoft.UI.Xaml.Controls.WebView2 { CoreWebView2: { } core })
        {
            if (_source.Kind == WebSourceKind.Bundle && _source.Folder is { } folder)
                core.SetVirtualHostNameToFolderMapping(
                    AssetHost, folder, Microsoft.Web.WebView2.Core.CoreWebView2HostResourceAccessKind.Allow);
            if (_source.Kind == WebSourceKind.Probe) core.NavigateToString(ProbeHtml);
            else Browser.Source = _source.Url;
            return;
        }
#endif
        if (_source.Kind != WebSourceKind.Probe) Browser.Source = _source.Url;
    }

    private void ShowFallback(string title, string detail)
    {
        HintLabel.Text = title;
        HintDetail.Text = detail;
        Fallback.IsVisible = true;
    }

    private void HideFallback() => Fallback.IsVisible = false;
}
