using System.Text;

namespace WarmAsBefore.Services;

/// <summary>
/// 一种外部运行时的台账：怎么确认它在、不在的话要装什么、去哪装。
/// 纯数据，不碰 IO，自检可以直接断言。
/// </summary>
public sealed record RuntimeSpec(
    string Id,
    string Label,
    string[] Commands,
    string InstallUrl,
    string InstallHint,
    // 本应用自带的替代路线（没有也要说清楚，让用户自己选）。
    string Fallback = "");

/// <summary>某一次探测的结果：在不在、看到的版本或路径、以及缺了怎么办。</summary>
public sealed record RuntimeState(
    string Id,
    string Label,
    bool Present,
    string Detail,
    string InstallUrl,
    string Advice,
    string Fallback = "");

/// <summary>
/// 外部运行时目录。
///
/// 为什么要这张表：插件与 MCP 服务都是别人写的东西，它们要跑起来靠的是本机的 node / python / java /
/// WebView2 这些运行时。装了个用 `npx -y something` 启动的 MCP，机器上却没 Node，报回来的错是
/// 「系统找不到指定的文件」——这句话对作者是线索，对用户是天书。所以把「命令 → 需要什么 → 去哪装 → 本应用有什么替代」
/// 写成一张表，检测缺了就直接给出可执行的那句话，而不是把进程错误原样吐出去。
///
/// Commands 的顺序就是探测顺序：先试规范名，再试发行版别名（python3 / py 这类）。
/// </summary>
public static class RuntimeCatalog
{
    public static readonly IReadOnlyList<RuntimeSpec> All = new List<RuntimeSpec>
    {
        new("node", "Node.js", new[] { "node" },
            "https://nodejs.org/zh-cn/download",
            "装 Node.js（LTS 即可），装完重启本应用。npx 随 Node 一起装，不必单独装。"),
        new("npx", "npx（随 Node.js 分发）", new[] { "npx" },
            "https://nodejs.org/zh-cn/download",
            "npx 是 Node.js 自带的，装 Node.js 就有了；只装 Node 不要单独下 npx。"),
        new("python", "Python", new[] { "python", "python3", "py" },
            "https://www.python.org/downloads/",
            "装 Python 3，或者在「设置 → 外部工具运行时」里让本应用自动下载便携版 Python。"),
        new("java", "Java", new[] { "java" },
            "https://adoptium.net/temurin/releases/",
            "装 JRE/JDK 17 以上，或者在「设置 → 外部工具运行时」里自动下载便携版 Java。"),
        new("uv", "uv（Python 包运行器）", new[] { "uvx", "uv" },
            "https://docs.astral.sh/uv/getting-started/installation/",
            "uvx 来自 uv。Windows 上最省事的一句：powershell -c \"irm https://astral.sh/uv/install.ps1 | iex\"。"),
        new("deno", "Deno", new[] { "deno" },
            "https://docs.deno.com/runtime/manual/getting_started/installation",
            "装 Deno（Windows：winget install denoland.deno）。"),
        new("bun", "Bun", new[] { "bun" },
            "https://bun.sh/docs/installation",
            "装 Bun（Windows：powershell -c \"irm bun.sh/install.ps1 | iex\"）。"),
        new("dotnet", ".NET 运行时", new[] { "dotnet" },
            "https://dotnet.microsoft.com/download",
            "装对应版本的 .NET 运行时。"),
        new("git", "Git", new[] { "git" },
            "https://git-scm.com/downloads",
            "装 Git 并让它进 PATH。"),
        new("webview2", "WebView2 浏览器内核", new[] { "msedgewebview2.exe" },
            "https://developer.microsoft.com/microsoft-edge/webview2/",
            "装 Microsoft Edge WebView2 运行时（Win11 与最新的 Edge 已自带）。",
            "本应用的插件页面一律交给系统浏览器打开，不装 WebView2 也能用——只是页面不会嵌在窗口里。"),
        new("browser", "系统浏览器", new[] { "" },
            "https://www.microsoft.com/edge",
            "系统里没登记能打开网页的程序。装一个浏览器（如 Edge）并设为默认。",
            "插件自带的 HTML 页面靠系统浏览器打开；没有浏览器就只剩应用内 native 设置可用。"),
    };

    private static readonly Dictionary<string, RuntimeSpec> ById =
        All.ToDictionary(s => s.Id, StringComparer.OrdinalIgnoreCase);

    /// <summary>插件 plugin.json 里 runtime 写的 token；不认识时返回 null，调用方负责给出修改意见。</summary>
    public static RuntimeSpec? ByIdOrNull(string? token) =>
        string.IsNullOrWhiteSpace(token) ? null
        : ById.TryGetValue(token.Trim(), out var s) ? s : null;

    /// <summary>
    /// 由启动命令反查它属于哪个运行时：`npx`、`npx.cmd`、`C:\\Program Files\\nodejs\\node.exe`、
    /// `python3.11` 都要认得。认不出（用户自己的 exe）就返回 null——不是所有命令都对应公开运行时。
    /// </summary>
    public static RuntimeSpec? ByCommand(string? command)
    {
        var name = NormalizeCommand(command);
        if (name.Length == 0) return null;
        foreach (var spec in All)
            foreach (var cmd in spec.Commands)
            {
                var c = cmd.ToLowerInvariant();
                if (c.Length == 0) continue;
                if (c.EndsWith(".exe")) c = c[..^4];
                if (name == c || name.StartsWith(c, StringComparison.Ordinal)
                    && name.Length > c.Length && char.IsDigit(name[c.Length]))
                    return spec;
            }
        return null;
    }

    private static readonly string[] ExeExts = { ".exe", ".cmd", ".bat", ".com" };

    /// <summary>
    /// 取命令的可执行名：去掉路径、去掉 .exe/.cmd/.bat/.com，统一小写。
    ///
    /// 「哪一段才是可执行文件」不能一律砍到第一个空格：Windows 上 `C:\Program Files\nodejs\node.exe`
    /// 砍完只剩 "Program"，装了 Node 的机器会被判成「本应用不认识这个命令」。
    /// 所以先认引号（`"…\node.exe" -y` 是最常见写法），再认带扩展名的那一段（它得是绝对路径或本身不含空格），
    /// 两者都不满足才按裸命令名 `npx -y pkg` 砍到第一个空格。
    /// </summary>
    public static string NormalizeCommand(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return "";
        var s = command.Trim();
        s = s.StartsWith('"') ? InsideQuotes(s) : PickExecutable(s);
        s = System.IO.Path.GetFileName(s);
        foreach (var ext in ExeExts)
            if (s.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) { s = s[..^ext.Length]; break; }
        return s.ToLowerInvariant();
    }

    private static string InsideQuotes(string line)
    {
        var end = line.IndexOf('"', 1);
        // 引号没闭合（用户只打了半个引号）：去掉引号按普通写法处理，别把整行当路径。
        return end > 1 ? line.Substring(1, end - 1) : PickExecutable(line.Trim('"'));
    }

    private static string PickExecutable(string line)
    {
        foreach (var ext in ExeExts)
        {
            var i = line.IndexOf(ext, StringComparison.OrdinalIgnoreCase);
            if (i <= 0) continue;
            var after = i + ext.Length;
            // 后面还接着字符（b.exe\tool）说明它不是这一个可执行文件的结尾。
            if (after < line.Length && !char.IsWhiteSpace(line[after])) continue;
            var head = line[..after];
            if (head.Contains('\t')) continue;
            if (System.IO.Path.IsPathRooted(head) || !head.Contains(' ')) return head;
        }
        var sp = line.IndexOfAny(new[] { ' ', '\t' });
        return sp > 0 ? line[..sp] : line;
    }

    /// <summary>认不出的 token 该说什么：把合法取值列全，别让人去猜。</summary>
    public static string KnownTokens => string.Join(" / ", All.Select(s => s.Id));
}

/// <summary>
/// 外部运行时探测。
///
/// 三条约束决定了这里的实现方式：
/// · **不启动进程**来判断「装没装」。跑 `node -v` 要 8 秒，还可能被 Windows 的应用执行别名拦下来
///   弹商店；插件中心每次刷新都要问一遍，等不起。这里只扫 PATH 找文件，注册表查 WebView2。
/// · 扫到的文件是 0 字节的 WindowsApps 占位（商店跳板）时算没装——它存在但跑不起来。
/// · 结果按 id 缓存 60 秒：装了之后的确认要重新扫（用户装完会回来点「重新检测」），
///   但不该每个列表项都落一次磁盘。
/// </summary>
public sealed class RuntimeProbe
{
    private static readonly Dictionary<string, (RuntimeState State, DateTime At)> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public const int CacheSeconds = 60;

    /// <summary>探测一批 runtime id（含缺失时的建议文案）。未知 id 直接按「无法识别」返回。</summary>
    public static async Task<List<RuntimeState>> ProbeAsync(IEnumerable<string?> ids)
    {
        var list = ids.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var outList = new List<RuntimeState>(list.Count);
        foreach (var id in list) outList.Add(await ProbeOneAsync(id));
        return outList;
    }

    public static async Task<RuntimeState> ProbeOneAsync(string id)
    {
        var spec = RuntimeCatalog.ByIdOrNull(id);
        if (spec is null)
            return new RuntimeState(id, id, false, "本应用不认识这个运行时名",
                "", $"runtime 只认这些值：{RuntimeCatalog.KnownTokens}");

        var key = spec.Id;
        await Gate.WaitAsync();
        try
        {
            if (Cache.TryGetValue(key, out var hit) && (DateTime.UtcNow - hit.At).TotalSeconds < CacheSeconds)
                return hit.State;
            var state = Probe(spec);
            Cache[key] = (state, DateTime.UtcNow);
            return state;
        }
        finally { Gate.Release(); }
    }

    /// <summary>「重新检测」用：清掉缓存，下一次真的重扫。</summary>
    public static void Invalidate() => Cache.Clear();

    private static RuntimeState Probe(RuntimeSpec spec)
    {
        var missing = new RuntimeState(spec.Id, spec.Label, false, "未检测到",
            spec.InstallUrl, spec.InstallHint, spec.Fallback);

        if (spec.Id == "webview2")
            return WebView2Present() is { } v
                ? new RuntimeState(spec.Id, spec.Label, true, v, spec.InstallUrl, "", spec.Fallback)
                : missing;

        if (spec.Id == "browser")
            return DefaultBrowserPresent() is { } b
                ? new RuntimeState(spec.Id, spec.Label, true, b, spec.InstallUrl, "", spec.Fallback)
                : missing;

        foreach (var cmd in spec.Commands)
        {
            if (cmd.Length == 0) continue;
            if (LocateOnPath(cmd) is { } found)
                return new RuntimeState(spec.Id, spec.Label, true, found, spec.InstallUrl, "", spec.Fallback);
        }
        return missing;
    }

    /// <summary>
    /// 在 PATH 里找可执行文件（同步、纯磁盘）。Windows 上补 .exe/.cmd/.bat/.com 的组合，
    /// 因为 npx 的真身是 `npx.cmd` 垫片，只找 `npx.exe` 会把装了 Node 的机器判成没装。
    /// </summary>
    public static string? LocateOnPath(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;
        var name = RuntimeCatalog.NormalizeCommand(command);
        if (name.Length == 0) return null;

        string? pathVar;
        try { pathVar = Environment.GetEnvironmentVariable("PATH"); }
        catch { return null; }
        if (string.IsNullOrEmpty(pathVar)) return null;

        var windows = OperatingSystem.IsWindows();
        var suffixes = windows
            ? new[] { ".exe", ".cmd", ".bat", ".com", "" }
            : new[] { "" };

        foreach (var raw in pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var dir = raw.Trim().Trim('"');
            if (dir.Length == 0) continue;
            try
            {
                if (!Directory.Exists(dir)) continue;
                foreach (var suffix in suffixes)
                {
                    var candidate = Path.Combine(dir, name + suffix);
                    if (!File.Exists(candidate)) continue;
                    // 商店别名占位：文件在、长度 0，跑起来只会弹 Microsoft Store。
                    var fi = new FileInfo(candidate);
                    if (fi.Length == 0 && IsWindowsAppsStub(candidate)) continue;
                    if (!windows && !IsExecutable(fi)) continue;   // POSIX 认权限位，没执行位的不是解释器
                    return candidate;
                }
            }
            catch { /* 单个目录没权限/名字太长，跳过它，别因为一个坏 PATH 项就判全部没装 */ }
        }
        return null;
    }

    private static bool IsWindowsAppsStub(string full) =>
        full.Contains(@"\Microsoft\WindowsApps", StringComparison.OrdinalIgnoreCase);

    /// <summary>POSIX 上「文件在」不等于「能跑」：还要有执行位。读不到权限位就当能跑。</summary>
    private static bool IsExecutable(FileInfo fi)
    {
        try { return (fi.UnixFileMode & UnixFileMode.UserExecute) != 0; }
        catch { return true; }
    }

    /// <summary>WebView2 常青版运行时的版本串（Edge 自带时这里也有值）。</summary>
    public static string? WebView2Present()
    {
#if WINDOWS
        // 同一个 CLSID 在三个位置出现过：64 位机器上装的是 WOW6432Node 那一份，
        // 只查一份会把装了的说成没装。
        const string client = @"Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}";
        string[] paths =
        {
            @"SOFTWARE\WOW6432Node\" + client,
            @"SOFTWARE\" + client,
        };
        foreach (var root in new[] { Microsoft.Win32.Registry.LocalMachine, Microsoft.Win32.Registry.CurrentUser })
        {
            foreach (var p in paths)
            {
                try
                {
                    using var key = root.OpenSubKey(p);
                    if (key?.GetValue("pv") is string pv && pv.Length > 0 && pv != "0.0.0.0")
                        return "WebView2 " + pv;
                }
                catch { }
            }
        }
#endif
        return null;
    }

    /// <summary>系统里有没有能打开 http 的程序（插件自带页面靠它）。</summary>
    public static string? DefaultBrowserPresent()
    {
#if WINDOWS
        try
        {
            using var http = Microsoft.Win32.Registry.ClassesRoot.OpenSubKey(@"http\shell\open\command");
            if (http?.GetValue(null) is string cmd && cmd.Length > 0)
                return "已登记：" + ShortCommand(cmd);
        }
        catch { }
        try
        {
            // Win10/11 默认程序记在 User Choice 里，ClassesRoot 那份可能是历史值。
            using var app = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\http\UserChoice");
            if (app?.GetValue("ProgId") is string id && id.Length > 0)
                return "已登记：" + id;
        }
        catch { }
#else
        try
        {
            // 非 Windows 不查注册表：能起一个 http(s) 的浏览器就按存在处理。
            if (OperatingSystem.IsAndroid()) return "由系统接管";
        }
        catch { }
#endif
        return null;
    }

    private static string ShortCommand(string cmd)
    {
        var s = cmd.Trim();
        if (s.StartsWith('"'))
        {
            var end = s.IndexOf('"', 1);
            if (end > 1) s = s.Substring(1, end - 1);
        }
        else
        {
            var sp = s.IndexOf(' ');
            if (sp > 0) s = s[..sp];
        }
        try { return Path.GetFileName(s); } catch { return s; }
    }

    /// <summary>把一批状态拼成给用户看的一行话（缺的才说话，齐了说一句「都齐」）。</summary>
    public static string Summarize(IReadOnlyList<RuntimeState> states)
    {
        if (states.Count == 0) return "";
        var sb = new StringBuilder();
        foreach (var s in states)
        {
            if (s.Present) { sb.Append($"{s.Label}：{s.Detail}"); sb.AppendLine(); continue; }
            sb.AppendLine($"{s.Label}：没装。{s.Advice}");
            if (!string.IsNullOrWhiteSpace(s.Fallback)) sb.AppendLine("  替代路线：" + s.Fallback);
        }
        return sb.ToString().TrimEnd();
    }
}
