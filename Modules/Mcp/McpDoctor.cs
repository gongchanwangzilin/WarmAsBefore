using System.Text.RegularExpressions;
using WarmAsBefore.Services;

namespace WarmAsBefore.Modules.Mcp;

/// <summary>
/// MCP 服务体检：连接状态 + 错误原因 + 怎么改。
///
/// 为什么需要它：MCP 的失败信息来自别人的进程与别人的 HTTP 服务，原话基本没法用——
/// 「系统找不到指定的文件」是 Node 没装，「由于目标计算机积极拒绝」是服务根本没起来，
/// 「服务进程提前退出了」通常是那个包在 import 阶段就崩了。这三句都只需要一句人话的指路，
/// 所以这里把协议层吐出来的原文对着这张表过一遍，翻成「改哪里、怎么改」。
///
/// 三条边界：
/// · 体检**不写状态**：不碰 registry、不落 LastError、不改 Allowed。它只回答「现在是什么情况」。
/// · 只有 cfg.Allowed 的 stdio 才真的起进程——没放行时那条闸就是给用户拦的，体检不能替用户点头。
/// · 起进程之前先查运行时：npx 冷启动要几秒，而「Node 没装」这件事扫一次 PATH 就知道，
///   没必要让用户等一次注定失败的启动。
/// </summary>
public static class McpDoctor
{
    /// <summary>
    /// 协议层原文 → 修改意见。认不出来返回空串：宁可只展示原文，也别编一句看着像那么回事的指引。
    /// 纯函数，自检直接对字符串断言。
    /// </summary>
    public static string AdviceFor(string? message, McpServerConfig? cfg = null)
    {
        var text = (message ?? "").Trim();
        if (text.Length == 0) return "";

        if (text.Contains(McpProtocol.NeedConsentMessage, StringComparison.Ordinal))
            return "stdio 等于在你机器上跑一条命令，这一位要亲手点亮：在「启动并连接」上按一次，"
                 + "确认过的命令行会原样显示在那儿，看完再决定";

        if (Contains(text, "未知的传输方式"))
            return "transport 只能写 \"stdio\"（本机起进程）或 \"http\"（连一个地址）；"
                 + "写成 streamable-http / sse / websocket 这一类都不认，猜成 http 会把你以为在本地跑的东西发到网上";

        if (Contains(text, "没填服务地址") || Contains(text, "只允许 http/https")
            || Contains(text, "不是一个合法的绝对地址") || Contains(text, "解析不了"))
            return "地址要带协议头写全，例如 http://127.0.0.1:8931/mcp 或 https://example.com/mcp；"
                 + "file:// 与相对路径一律不收";

        if (StartsRuntimeMissing(text, out var command))
            return RuntimeMissingAdvice(command);

        if (Contains(text, "拒绝访问", "access is denied"))
            return "这条命令不让你启动：多半是安全软件拦了、或者路径在只读目录里。"
                 + "给绝对路径并把目录加进白名单再试";

        if (Contains(text, "启动「"))
            return "命令本身没能变成进程：先在终端里原样跑一遍这条命令，"
                 + "能跑起来再回来照抄，跑不起来就是命令写法或安装位置的问题";

        if (Contains(text, "积极拒绝", "econnrefused", "unable to connect", "no such host"))
            return "端口那边没人听：先把这个 MCP 服务本身跑起来，再核对地址的端口写得对不对。"
                 + "它如果是本机进程，检查一下是不是崩了或者换了端口";

        if (Contains(text, "请求超时"))
            return "命令能起来但不回话：npx / uvx 首次拉包慢是正常现象（可以多点一次连接），"
                 + "反复超时多半是这条命令卡在交互提问上等输入——MCP 只走标准输入输出，"
                 + "任何「要不要继续 (y/n)」都会把它挂死";

        if (Contains(text, "服务进程提前退出"))
            return "进程刚起就退了，一般是命令行参数里的包名不对、依赖没装好，或者它把日志打到了 stdout。"
                 + "把这条命令拷到终端里手动跑一遍，看得见真正的报错";

        if (Contains(text, "单行输出超过"))
            return "服务往 stdout 吐了超长的一行：MCP 只认「一行一条 JSON」，"
                 + "启动横幅、进度条、debug 日志都要改到 stderr 或干脆关掉";

        if (Contains(text, "没给出工具清单") || Contains(text, "tools/list"))
            return "握手通了但 tools/list 没给出对象：这个服务要么没实现工具，要么返回的不是 JSON-RPC 2.0";

        var http = Regex.Match(text, @"HTTP\s+(\d{3})");
        if (http.Success)
        {
            var code = int.Parse(http.Groups[1].Value);
            return code switch
            {
                401 or 403 => "鉴权没过：检查 Headers 里的 token / Authorization 是否填了、是否过期，"
                            + "注意有些服务要求 Bearer 前缀",
                404 => "地址路径不对：streamable HTTP 服务通常挂在 /mcp 或 /sse 这种具体路径上，"
                     + "只填到域名根路径会 404",
                405 => "这个端点不收 POST：说明它不是 streamable HTTP 传输，"
                     + "要么该用 stdio 本机起，要么换成服务给出的 /mcp 地址",
                >= 500 => "服务端自己出错了：这一段报错是它写的，本地改不了；"
                        + "先看它的日志，或换回 stdio 本机启动排除网络这一层",
                _ => ""
            };
        }

        if (Contains(text, "不是合法 JSON") || Contains(text, "响应里找不到 id"))
            return "回来的东西对不上协议：确认这个地址真的在说 MCP，"
                 + "而不是一个普通 REST 接口或者被登录页/代理拦下来了";

        return "";
    }

    /// <summary>
    /// 「启动失败 + 找不到东西」这一族才算运行时缺失。只有「启动失败」而没有找不到字样的，
    /// 走上面那条通用指引——把 Access denied 说成「你没装 Node」比不说更糟。
    /// </summary>
    private static bool StartsRuntimeMissing(string text, out string command)
    {
        command = "";
        if (!Contains(text, "找不到指定的文件", "找不到文件", "找不到指定的目录", "不能找到",
                "no such file or directory", "cannot find the file", "cannot find the path",
                "the system cannot find", "不是内部或外部命令",
                "not recognized as an internal or external command"))
            return false;
        var named = Regex.Match(text, @"启动「(.+?)」失败");
        command = named.Success ? named.Groups[1].Value : "";
        return true;
    }

    /// <summary>命令 → 需要什么运行时 → 去哪装。认不出的命令不瞎猜，只说「本机 PATH 里找不到」。</summary>
    private static string RuntimeMissingAdvice(string commandLine)
    {
        var name = RuntimeCatalog.NormalizeCommand(commandLine);
        if (name.Length == 0) return "本机跑不起这条命令：Command 要写成能直接执行的名字或绝对路径";
        var spec = RuntimeCatalog.ByCommand(name);
        if (spec is null)
            return $"本机 PATH 里找不到「{name}」：把 Command 改成它的绝对路径，"
                 + "或者确认这个程序真的装过（本应用不认识它，没法替你下载）";
        var present = RuntimeProbe.LocateOnPath(spec.Commands.FirstOrDefault() ?? name) is not null;
        var head = present
            ? $"「{name}」属于 {spec.Label}，PATH 里能扫到但启动仍失败"
            : $"本机没装 {spec.Label}——「{name}」这条命令起不来";
        var tail = $"{spec.InstallHint}（{spec.InstallUrl}）";
        return string.IsNullOrWhiteSpace(spec.Fallback) ? head + "。" + tail : head + "。" + tail + "。替代路线：" + spec.Fallback;
    }

    /// <summary>
    /// 不联网、不起进程的静态一遍过：配置字段、运行时在不在、工作目录存不存在。
    /// 这一份是「还没点连接就该看见」的红字，所以调用方在列表刷新时也能用。
    /// </summary>
    public static List<DoctorFinding> InspectConfig(McpServerConfig cfg, IReadOnlyList<RuntimeState>? runtimes = null)
    {
        var f = new List<DoctorFinding>();
        if (cfg is null) return f;

        var transport = (cfg.Transport ?? "").Trim().ToLowerInvariant();
        if (transport.Length == 0)
            f.Add(new DoctorFinding(DoctorLevel.Error, "transport", "没写传输方式",
                "stdio = 本机起一个进程；http = 连一个已运行的地址。这一项决定后面填 Command 还是填 Url"));
        else if (transport != "stdio" && transport != "http")
            f.Add(new DoctorFinding(DoctorLevel.Error, "transport", $"「{Clip(cfg.Transport, 60)}」不认识",
                "只支持 stdio 与 http；别的写法不会「猜一个试试」，猜错方向比报错更糟"));

        if (transport == "stdio")
        {
            if (string.IsNullOrWhiteSpace(cfg.Command))
                f.Add(new DoctorFinding(DoctorLevel.Error, "command", "没写要启动的命令",
                    "写成 node / npx / uvx / python 这类可执行名，Args 里再分开填参数"));
            else
            {
                var spec = RuntimeCatalog.ByCommand(cfg.Command);
                var state = spec is null ? null
                    : runtimes?.FirstOrDefault(r => string.Equals(r.Id, spec.Id, StringComparison.OrdinalIgnoreCase));
                if (state is { Present: false })
                {
                    f.Add(new DoctorFinding(DoctorLevel.Error, "command", $"{spec!.Label}：没装",
                        spec.InstallHint + "（" + spec.InstallUrl + "）"));
                    if (!string.IsNullOrWhiteSpace(spec.Fallback))
                        f.Add(new DoctorFinding(DoctorLevel.Info, "command", spec.Label + "的替代路线", spec.Fallback));
                }
                else if (state is { Present: true })
                    f.Add(new DoctorFinding(DoctorLevel.Info, "command", spec!.Label + "：" + state.Detail));
                else if (spec is null && RuntimeProbe.LocateOnPath(cfg.Command) is null && !IsExistingPath(cfg.Command))
                    f.Add(new DoctorFinding(DoctorLevel.Warn, "command", $"本机 PATH 里找不到「{RuntimeCatalog.NormalizeCommand(cfg.Command)}」",
                        "本应用不认识这个命令，所以不替你装。确认它装过就给绝对路径，是 Node/Python 之类的就写规范名"));

                if (IsNpxColdStart(cfg))
                    f.Add(new DoctorFinding(DoctorLevel.Info, "command", "用 npx/uvx 现场拉包",
                        "第一次连接要等它下载依赖，几秒到几十秒都算正常；装过之后才快。嫌慢就预先全局装好，Command 直接写 node/uvx"));
            }

            if (!string.IsNullOrWhiteSpace(cfg.Cwd) && !Directory.Exists(cfg.Cwd))
                f.Add(new DoctorFinding(DoctorLevel.Error, "cwd", $"工作目录不存在：{Clip(cfg.Cwd, 120)}",
                    "服务多半靠相对路径找自己的文件，这个目录没了会在启动瞬间就退；改目录或清掉这一项"));

            if (!cfg.Allowed)
                f.Add(new DoctorFinding(DoctorLevel.Warn, "allowed", "还没允许在本机启动",
                    "这是硬闸：不点「启动并连接」，握手与工具调用都会被直接拒绝，跟服务本身对不对没关系"));
        }

        if (transport == "http")
        {
            if (!McpProtocol.IsHttpEndpoint(cfg.Url, out var urlError))
                f.Add(new DoctorFinding(DoctorLevel.Error, "url", urlError,
                    "填服务实际监听的完整地址，例如 http://127.0.0.1:8931/mcp"));
        }

        if (transport == "http" && cfg.Headers is { Count: > 0 })
        {
            foreach (var key in cfg.Headers.Keys)
                if (string.IsNullOrWhiteSpace(key))
                    f.Add(new DoctorFinding(DoctorLevel.Warn, "headers", "有一条请求头的名字是空的", "带上名字服务端才收得到"));
        }

        if (!string.IsNullOrWhiteSpace(cfg.LastError))
        {
            var advice = AdviceFor(cfg.LastError, cfg);
            f.Add(new DoctorFinding(DoctorLevel.Error, "上次连接", Clip(cfg.LastError, 300), advice));
        }

        if (string.IsNullOrWhiteSpace(cfg.LastError) && cfg.LastConnectedUtc != default)
            f.Add(new DoctorFinding(DoctorLevel.Info, "上次连接",
                "成功过（" + cfg.LastConnectedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm") + " 本地时间）"));

        return f;
    }

    /// <summary>
    /// 完整体检：先过一遍静态项，再（可选）真的握一次手。
    /// 已经能确定连不上的那些情形不再白等一次启动——静态结论里就有原因了。
    /// </summary>
    public static async Task<List<DoctorFinding>> DiagnoseAsync(McpServerConfig cfg, bool probe,
        IReadOnlyList<RuntimeState>? runtimes = null, CancellationToken ct = default)
    {
        var f = InspectConfig(cfg, runtimes);
        if (!probe) return f;
        if (f.Any(x => x.Blocking)) return f;

        using var timeout = new CancellationTokenSource(McpProtocol.ToolsListTimeout + McpProtocol.InitializeTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        var (ok, message, tools) = await McpProtocol.DiscoverAsync(cfg, linked.Token).ConfigureAwait(false);
        if (ok)
            f.Add(new DoctorFinding(DoctorLevel.Info, "连接",
                $"通了，远端给出 {tools.Count} 个工具" + (tools.Count == 0 ? "（一个都没有：这个服务只提供资源/提示词，聊天里就没东西可调）" : "")));
        else
        {
            f.Add(new DoctorFinding(DoctorLevel.Error, "连接", Clip(message, 300), AdviceFor(message, cfg)));
            if (tools.Count == 0 && Contains(message, "没给出工具清单"))
                f.Add(new DoctorFinding(DoctorLevel.Info, "连接", "握手本身是成功的，卡在 tools/list"));
        }
        return f;
    }

    /// <summary>一行给 UI 的短结论：状态 + 有几个要改。</summary>
    public static string Summarize(IReadOnlyList<DoctorFinding> findings, bool connected, int toolCount)
    {
        var blocking = PluginDoctor.CountBlocking(findings);
        if (connected) return blocking == 0 ? $"已连接，{toolCount} 个工具" : $"已连接（{blocking} 个要改）";
        if (blocking > 0) return $"{blocking} 个障碍";
        return "还没连上";
    }

    /// <summary>
    /// stdio 的 Command 只有绝对路径这一种写法能直接确认存在；
    /// 裸命令名要交给 PATH 扫描，那条路在 RuntimeProbe 里。
    /// </summary>
    private static bool IsExistingPath(string command)
    {
        try { return Path.IsPathRooted(command.Trim().Trim('"')) && File.Exists(command.Trim().Trim('"')); }
        catch { return false; }
    }

    /// <summary>npx / uvx 这类「跑之前先装」的启动方式。</summary>
    private static bool IsNpxColdStart(McpServerConfig cfg)
    {
        var name = RuntimeCatalog.NormalizeCommand(cfg.Command);
        if (name is "npx" or "uvx" or "pnpx" or "bunx") return true;
        return cfg.Args.Any(a => Contains(a, "-y", "--yes", "--from", "-p", "--package"));
    }

    private static string Clip(string? value, int max)
    {
        var s = (value ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
        return s.Length <= max ? s : s[..max] + "…";
    }

    private static bool Contains(string text, params string[] tokens)
    {
        foreach (var t in tokens)
            if (t.Length > 0 && text.Contains(t, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
