using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace WarmAsBefore.Modules.Mcp;

/// <summary>
/// 一个外部 MCP 服务的接入配置，落在 McpPacks/&lt;Key&gt;/server.json。
/// MCP 是「外部服务」不是「数据包」：要么本机起一个进程（stdio），要么连一个地址（http），
/// 所以这份配置里必须写清楚从哪里启动、怎么启动。
/// </summary>
public sealed class McpServerConfig
{
    /// <summary>McpPacks 下的目录名，工具归属与删除都按它。</summary>
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";

    /// <summary>"stdio" 或 "http"。别的值一律拒绝：猜成 http 会把用户以为在本地启动的东西发到网上去。</summary>
    public string Transport { get; set; } = "stdio";

    // ---- stdio ----

    /// <summary>可执行文件（npx / uvx / 绝对路径都算）。</summary>
    public string Command { get; set; } = "";
    public List<string> Args { get; set; } = new();
    public string Cwd { get; set; } = "";
    public Dictionary<string, string> Env { get; set; } = new();

    // ---- http ----

    public string Url { get; set; } = "";
    public Dictionary<string, string> Headers { get; set; } = new();

    /// <summary>
    /// stdio 等于在用户机器上跑任意命令，这一位必须由用户在设置页亲手点亮（AllowServer）；
    /// 没放行前 ConnectServerAsync 与工具调用都直接拒绝。http 不看这一位。
    /// </summary>
    public bool Allowed { get; set; }

    public string CreatedAt { get; set; } = "";
    public string LastError { get; set; } = "";

    /// <summary>默认值（0001-01-01）表示从没握手成功过。</summary>
    public DateTime LastConnectedUtc { get; set; }
}

/// <summary>握手之后从远端拿到的一个工具。ParametersSchema 是 JSON Schema 原文，直接给模型看。</summary>
public sealed record McpRemoteTool(string Name, string Description, string ParametersSchema);

/// <summary>
/// MCP 客户端传输层：JSON-RPC 2.0，走 stdio（NDJSON）或 streamable HTTP。
///
/// 成帧、解析、命令行拆分、结果拼接都是纯函数，自检直接对字符串断言，不需要真起进程或联网。
///
/// 进程策略是有意的取舍：stdio 每次调用都新起一个进程、用完连进程树一起杀掉。
/// 冷启动慢（npx 首次能等几秒），但绝不留常驻进程——省掉一套生命周期缓存，
/// 也就省掉「应用崩了子进程还在」「同一服务两个会话抢同一根 stdin」这两类最难查的坑。
/// </summary>
public static class McpProtocol
{
    public const string ProtocolVersion = "2024-11-05";
    public const string ClientName = "WarmAsBefore";
    private const string ClientVersion = "1.0";

    /// <summary>远端报上来的字符串一律在入口掐长度，与 Minecraft 桥那套同一口径。</summary>
    public const int MaxStringChars = 2000;
    public const int MaxToolNameChars = 120;
    public const int MaxSchemaChars = 6000;

    /// <summary>工具结果会进模型上下文：一次调用就能吐整份文件，不能原样塞进去。</summary>
    public const int MaxToolResultChars = 8000;

    /// <summary>分页硬上限：坏服务用 nextCursor 死循环也不能把这里撑爆。</summary>
    public const int MaxToolPages = 50;
    public const int MaxToolCount = 500;

    private const int MaxLineChars = 4 * 1024 * 1024;
    private const int MaxBodyChars = 8 * 1024 * 1024;

    /// <summary>模型传来的参数上限：超过这个长度基本是把整篇文档当参数了。</summary>
    public const int MaxArgsChars = 200_000;

    public static readonly TimeSpan InitializeTimeout = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan ToolsListTimeout = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan ToolCallTimeout = TimeSpan.FromSeconds(60);

    /// <summary>stdio 未放行时回给 UI 的原话（设置页那一次点击才会把 Allowed 置 true）。</summary>
    public const string NeedConsentMessage = "需要你先在设置里点「启动并连接」允许它运行";

    private static readonly JsonSerializerOptions WireJson = new() { WriteIndented = false };

    /// <summary>HTTP 只用一个 static 客户端：连接池复用，超时交给 CancellationTokenSource 管。</summary>
    private static readonly HttpClient Http = new() { Timeout = System.Threading.Timeout.InfiniteTimeSpan };

    // ==================== 成帧（纯函数） ====================

    /// <summary>
    /// 一行 NDJSON 请求。System.Text.Json 本来就会转义字符串里的换行，末尾再兜一次，
    /// 保证「一行一条」这个 stdio 的生命线不会被任何 params 打穿。
    /// </summary>
    public static string FrameRequest(int id, string method, object? paramsObj) => Frame(id, method, paramsObj);

    /// <summary>通知没有 id，也永远等不到回应（notifications/initialized 就是这种）。</summary>
    public static string FrameNotification(string method, object? paramsObj) => Frame(null, method, paramsObj);

    private static string Frame(int? id, string method, object? paramsObj)
    {
        var msg = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = Clip(CleanLine(method), MaxStringChars)
        };
        if (id is { } rid) msg["id"] = rid;
        var parms = ToNode(paramsObj);
        if (parms is not null) msg["params"] = parms;
        return SingleLine(msg.ToJsonString(WireJson));
    }

    private static JsonNode? ToNode(object? value)
        => value switch
        {
            null => null,
            JsonNode node => node,
            string json when json.Length > 0 && (json[0] == '{' || json[0] == '[') => SafeParseNode(json),
            _ => SafeParseNode(JsonSerializer.Serialize(value, WireJson))
        };

    private static JsonNode? SafeParseNode(string json)
    {
        try { return JsonNode.Parse(json); }
        catch (Exception ex)
        {
            App.WriteLog("[MCP] params 片段不是合法 JSON，已丢弃：" + ex.Message);
            return null;
        }
    }

    // ==================== 解析（纯函数） ====================

    /// <summary>
    /// 从一行里取出 expectId 的那条响应。支持裸 JSON、美化过的多行 JSON、SSE 的 data: 行。
    /// 返回 false 的意思是「这条不是我要的，接着读」：通知、别的 id 的响应、心跳、
    /// 混在 stdout 里的日志垃圾行都走这条路，一律不抛——服务端往标准输出打日志太常见了。
    /// </summary>
    public static bool TryParseResponse(string? line, int expectId,
        out int id, out JsonElement? result, out string? error)
    {
        id = 0;
        result = null;
        error = null;

        var text = (line ?? "").Trim();
        if (text.Length == 0) return false;
        if (text.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            text = text.Substring(5).Trim();
            if (text.Length == 0) return false;   // data: 空帧是 SSE 心跳
        }
        else if (text[0] != '{') return false;    // event: / id: / retry: / : 注释 / 普通日志行

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(text, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });
        }
        catch (Exception)
        {
            return false;   // 垃圾行：忽略，不抛
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;

            // 带 method 的是服务端推来的通知，永远不该被当成某个请求的响应吃掉
            if (root.TryGetProperty("method", out var m) && m.ValueKind == JsonValueKind.String) return false;

            if (root.TryGetProperty("id", out var idEl) && TryReadId(idEl, out var got)) id = got;
            // 没有 id（或 id 类型不认识）时 id 仍是 0，而真实请求 id 从 1 起，所以这里天然被跳过
            if (id != expectId) return false;

            if (root.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.Object)
            {
                error = FormatRpcError(err);
                return true;
            }
            if (root.TryGetProperty("result", out var res))
            {
                result = res.Clone();   // doc 一 dispose，element 就废了
                return true;
            }
            // result 与 error 都没有：不合协议。丢掉继续等，让上层超时，而不是当成一次空成功
            return false;
        }
    }

    private static bool TryReadId(JsonElement el, out int id)
    {
        id = 0;
        if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var n)) { id = n; return true; }
        // 有的服务端把我们发的数字 id 回成字符串
        if (el.ValueKind == JsonValueKind.String && int.TryParse(el.GetString(), out var s)) { id = s; return true; }
        return false;
    }

    private static string FormatRpcError(JsonElement err)
    {
        var code = err.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.Number
            ? c.GetRawText() : "?";
        var message = err.TryGetProperty("message", out var msg) && msg.ValueKind == JsonValueKind.String
            ? (msg.GetString() ?? "") : err.GetRawText();
        var text = $"服务返回错误 {code}：{Clip(message, MaxStringChars)}";
        if (err.TryGetProperty("data", out var data) && data.ValueKind != JsonValueKind.Null)
            text += "　" + Clip(data.GetRawText(), 500);
        return Clip(text, MaxStringChars + 600);
    }

    // ==================== 命令行与目标（纯函数） ====================

    /// <summary>
    /// 按 Windows argv 规则（与 CommandLineToArgvW 同构）拆一行命令：
    /// 引号里的空格属于参数本身，<c>"C:\Program Files\x\node.exe" run "a b"</c> 必须拆成三段；
    /// Split(' ') 在这种行上会把路径切成好几块。奇数个反斜杠紧跟引号时那个引号是字面量。
    /// </summary>
    public static List<string> SplitCommandLine(string? commandLine)
    {
        var parts = new List<string>();
        if (string.IsNullOrEmpty(commandLine)) return parts;

        int i = 0, len = commandLine.Length;
        while (i < len)
        {
            while (i < len && char.IsWhiteSpace(commandLine[i])) i++;
            if (i >= len) break;

            var token = new StringBuilder();
            while (i < len && !char.IsWhiteSpace(commandLine[i]))
            {
                var backslashes = 0;
                while (i < len && commandLine[i] == '\\') { i++; backslashes++; }

                if (i < len && commandLine[i] == '"')
                {
                    token.Append('\\', backslashes / 2);
                    i++;
                    if (backslashes % 2 == 1) { token.Append('"'); continue; }   // 转义引号：留在本段，仍在引号外

                    // 进入引号段：里面的空格不再切分
                    while (i < len)
                    {
                        var bs2 = 0;
                        while (i < len && commandLine[i] == '\\') { i++; bs2++; }
                        if (i < len && commandLine[i] == '"')
                        {
                            token.Append('\\', bs2 / 2);
                            i++;
                            if (bs2 % 2 == 1) { token.Append('"'); continue; }   // 奇数反斜杠：字面量引号，段没结束
                            break;                                               // 偶数反斜杠：引号段收尾
                        }
                        token.Append('\\', bs2);
                        if (i >= len) break;
                        token.Append(commandLine[i++]);
                    }
                    continue;
                }

                token.Append('\\', backslashes);
                if (i < len) token.Append(commandLine[i++]);
            }
            parts.Add(token.ToString());
        }
        return parts;
    }

    /// <summary>地址必须是绝对 http/https URL——file:// 这一类的口子不能开。</summary>
    public static bool IsHttpEndpoint(string? url, out string error)
    {
        error = "";
        var text = (url ?? "").Trim();
        if (text.Length == 0) { error = "没填服务地址"; return false; }
        if (text.Length > MaxStringChars) { error = "服务地址过长"; return false; }
        if (!Uri.IsWellFormedUriString(text, UriKind.Absolute)) { error = $"「{Clip(text, 80)}」不是一个合法的绝对地址"; return false; }
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri)) { error = $"「{Clip(text, 80)}」解析不了"; return false; }
        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            error = $"只允许 http/https，收到的是「{uri.Scheme}」";
            return false;
        }
        return true;
    }

    /// <summary>stdio 需要用户放行，http 不需要（但仍受 MCP 总开关与自动确认约束）。</summary>
    public static bool UsesStdio(McpServerConfig? cfg)
        => string.Equals(cfg?.Transport, "stdio", StringComparison.OrdinalIgnoreCase);

    /// <summary>未知传输方式在握手之前就拒绝，不猜。</summary>
    public static bool TryResolveTransport(McpServerConfig? cfg, out bool stdio, out string error)
    {
        stdio = false;
        error = "";
        if (cfg is null) { error = "没读到这个服务的 server.json"; return false; }
        var t = (cfg.Transport ?? "").Trim();
        if (string.Equals(t, "stdio", StringComparison.OrdinalIgnoreCase)) { stdio = true; return true; }
        if (string.Equals(t, "http", StringComparison.OrdinalIgnoreCase)) return true;
        error = $"未知的传输方式「{Clip(t, 60)}」，只支持 stdio 与 http";
        return false;
    }

    /// <summary>
    /// stdio 的启动参数。Windows 上用户常把 Command 写成 <c>npx</c> / <c>npm</c> / <c>uvx</c>，
    /// 那些其实是 .cmd 垫片，CreateProcess 只认 exe，直接 Start 会抛，所以这种情形改用
    /// cmd.exe /d /c 包裹整条命令行（cmd 的 /d 表示不读 AutoRun，免得别人的注册项把进程劫了）。
    /// </summary>
    public static ProcessStartInfo BuildStdioStartInfo(McpServerConfig cfg)
    {
        var psi = new ProcessStartInfo
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        var command = (cfg.Command ?? "").Trim();
        foreach (var (k, v) in cfg.Env ?? new Dictionary<string, string>())
        {
            if (string.IsNullOrWhiteSpace(k)) continue;
            psi.Environment[k.Trim()] = v ?? "";
        }
        var cwd = (cfg.Cwd ?? "").Trim();
        if (cwd.Length > 0 && Directory.Exists(cwd)) psi.WorkingDirectory = cwd;
        // Cwd 填了又不存在时不静默退回进程当前目录：那会让相对路径的脚本在应用目录下乱跑
        if (cwd.Length > 0 && !Directory.Exists(cwd)) App.WriteLog($"[MCP] 工作目录不存在，已忽略：{Clip(cwd, 200)}");

        if (NeedsCmdShim(command))
        {
            psi.FileName = "cmd.exe";
            psi.Arguments = "/d /c " + CmdWrapped(command, cfg.Args);
            return psi;
        }

        psi.FileName = command;
        foreach (var a in cfg.Args ?? new List<string>())
        {
            if (a is null) continue;
            psi.ArgumentList.Add(a);   // 交给 ArgumentList 拼引号，自己拼就是注入面的开始
        }
        return psi;
    }

    /// <summary>不含路径分隔符、自身又不是 .exe 的名字，在 Windows 上大概率是 .cmd/.bat 垫片。</summary>
    public static bool NeedsCmdShim(string? command)
    {
        var c = (command ?? "").Trim();
        if (c.Length == 0 || !OperatingSystem.IsWindows()) return false;
        if (c.IndexOf('/') >= 0 || c.IndexOf('\\') >= 0) return false;
        if (c.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }

    /// <summary>
    /// cmd.exe 的分词器不认反斜杠转义，参数里再出现引号只会把命令行从中间切开——那是注入面。
    /// 所以被 cmd 包裹的这条路只牺牲引号：参数里的 " 一律去掉，含空格或 cmd 元字符的整段加引号。
    /// </summary>
    private static string CmdWrapped(string command, IEnumerable<string>? args)
    {
        var sb = new StringBuilder(QuoteForCmd(command));
        foreach (var a in args ?? new List<string>())
        {
            if (a is null) continue;
            sb.Append(' ').Append(QuoteForCmd(a));
        }
        return sb.ToString();
    }

    private static string QuoteForCmd(string value)
    {
        var clean = value.Replace("\"", "");
        if (clean.Length == 0) return "\"\"";
        if (!clean.Any(c => char.IsWhiteSpace(c) || "&<>|^()".IndexOf(c) >= 0)) return clean;
        return "\"" + clean + "\"";
    }

    // ==================== 结果文本（纯函数） ====================

    /// <summary>
    /// 把 tools/call 的 result.content 拼成给模型看的文本。非 text 片段（图片/音频/资源）只留一句占位：
    /// 这里进的是文字上下文，塞一串 base64 只会把上下文烧掉。
    /// </summary>
    public static string ExtractResultText(JsonElement? result, out bool isError)
    {
        isError = false;
        if (result is not { } root || root.ValueKind != JsonValueKind.Object) return "";

        if (root.TryGetProperty("isError", out var flag)
            && (flag.ValueKind == JsonValueKind.True
                || (flag.ValueKind == JsonValueKind.String && bool.TryParse(flag.GetString(), out var b) && b)))
            isError = true;

        var sb = new StringBuilder();
        if (root.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
        {
            foreach (var part in content.EnumerateArray())
            {
                if (sb.Length > MaxToolResultChars * 2) { sb.Append("…（内容片段过多，已停止拼接）"); break; }
                if (part.ValueKind != JsonValueKind.Object) continue;
                var type = ReadStr(part, "type");
                switch (type)
                {
                    case "text":
                        if (part.TryGetProperty("text", out var tx) && tx.ValueKind == JsonValueKind.String)
                            sb.Append(CleanLine(tx.GetString() ?? "")).Append('\n');
                        break;
                    case "image":
                        sb.Append(Placeholder(part, "图片"));
                        break;
                    case "audio":
                        sb.Append(Placeholder(part, "音频"));
                        break;
                    case "resource_link":
                        sb.Append(Placeholder(part, "资源链接"));
                        break;
                    case "resource":
                        sb.Append(EmbeddedResourceText(part));
                        break;
                    default:
                        sb.Append($"[未知内容片段「{Clip(type, 40)}」，未解析]\n");
                        break;
                }
            }
        }

        var text = sb.ToString().TrimEnd();
        // 带 structuredContent 的服务：content 空着时它就是唯一内容
        if (text.Length == 0 && root.TryGetProperty("structuredContent", out var sc)
            && sc.ValueKind == JsonValueKind.Object)
            text = Clip(sc.GetRawText(), MaxToolResultChars);
        return text;
    }

    private static string Placeholder(JsonElement part, string label)
    {
        var mime = ReadStr(part, "mimeType");
        var uri = ReadStr(part, "uri");
        var tail = mime.Length > 0 ? mime : uri;
        return $"[{label}{(tail.Length > 0 ? "：" + Clip(tail, 120) : "")}，未内联到文本上下文]\n";
    }

    private static string EmbeddedResourceText(JsonElement part)
    {
        if (part.TryGetProperty("resource", out var r) && r.ValueKind == JsonValueKind.Object)
        {
            var txt = ReadStr(r, "text");
            var uri = ReadStr(r, "uri");
            if (txt.Length > 0) return $"[资源 {Clip(uri.Length > 0 ? uri : "（无地址）", 120)}]\n{Clip(txt, MaxToolResultChars)}\n";
            return $"[资源 {Clip(uri.Length > 0 ? uri : "（无地址）", 120)}，二进制内容未内联]\n";
        }
        return "[资源，未内联]\n";
    }

    private static string ReadStr(JsonElement el, string prop)
        => el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? (v.GetString() ?? "") : "";

    /// <summary>钳长度，并明说裁过、原长多少——别让模型以为看到全部了。</summary>
    public static string ClipForModel(string? text)
    {
        var s = text ?? "";
        if (s.Length <= MaxToolResultChars) return s;
        return s[..MaxToolResultChars] + $"\n…（共 {s.Length} 字，已截断，仅保留前 {MaxToolResultChars} 字）";
    }

    public static string Clip(string? text, int max)
        => string.IsNullOrEmpty(text) || text.Length <= max ? (text ?? "") : text[..max];

    /// <summary>工具名要当注册表的键用：控制字符与首尾空白去掉，空名给一个能认出来的兜底名。</summary>
    public static string CleanToolName(string? raw, int fallbackOrdinal = 0)
    {
        var s = CleanLine(raw ?? "").Trim();
        if (s.Length == 0) s = fallbackOrdinal > 0 ? $"unnamed_tool_{fallbackOrdinal}" : "unnamed_tool";
        return s.Length <= MaxToolNameChars ? s : s[..MaxToolNameChars];
    }

    private static string CleanLine(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        if (s.IndexOfAny(ControlChars) < 0) return s;
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            if (c == '\t' || c == '\n' || c == '\r') sb.Append(' ');
            else if (!char.IsControl(c)) sb.Append(c);
        }
        return sb.ToString();
    }

    private static readonly char[] ControlChars =
    {
        '\u0000', '\u0001', '\u0002', '\u0003', '\u0004', '\u0005', '\u0006', '\a',
        '\b', '\v', '\f', '\u000e', '\u000f', '\u0010', '\u0011', '\u0012', '\u0013',
        '\u0014', '\u0015', '\u0016', '\u0017', '\u0018', '\u0019', '\u001a', '\u001b',
        '\u001c', '\u001d', '\u001e', '\u001f'
    };

    /// <summary>成帧的输出绝不能带裸换行，否则一行 NDJSON 会被切成两条。</summary>
    private static string SingleLine(string json)
    {
        if (json.IndexOf('\n') < 0 && json.IndexOf('\r') < 0) return json;
        var sb = new StringBuilder(json.Length);
        foreach (var c in json)
        {
            if (c == '\n' || c == '\r') sb.Append(' ');
            else sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>模型传来的参数：空/坏 JSON 都当 {}（远端工具要的是对象，喂数组过去只会换回一个 -32602）。</summary>
    public static bool TryNormalizeArguments(string? argumentsJson, out JsonObject args, out string error)
    {
        error = "";
        args = new JsonObject();
        var text = (argumentsJson ?? "").Trim();
        if (text.Length == 0) text = "{}";
        if (text.Length > MaxArgsChars)
        {
            error = $"参数太长（{text.Length} 字，上限 {MaxArgsChars}），已拒绝";
            return false;
        }
        try
        {
            if (JsonNode.Parse(text) is JsonObject obj) { args = obj; return true; }
            App.WriteLog("[MCP] 参数不是 JSON 对象，已按空对象调用");
            return true;
        }
        catch (Exception ex)
        {
            App.WriteLog("[MCP] 参数不是合法 JSON，已按空对象调用：" + ex.Message);
            return true;
        }
    }

    // ==================== 握手 ====================

    /// <summary>
    /// initialize → notifications/initialized → tools/list（按 nextCursor 翻页，硬上限 50 页 / 500 个工具）。
    /// Message 不带 [MCP] 前缀：它是要显示给用户看的文案，不是回给模型的工具结果。
    /// </summary>
    public static async Task<(bool Ok, string Message, List<McpRemoteTool> Tools)> DiscoverAsync(
        McpServerConfig cfg, CancellationToken ct)
    {
        var tools = new List<McpRemoteTool>();
        if (!TryResolveTransport(cfg, out var stdio, out var error)) return (false, error, tools);
        if (stdio && !cfg!.Allowed) return (false, NeedConsentMessage, tools);

        try
        {
            if (stdio)
            {
                using var runner = StdioRunner.Start(cfg!, out error);
                if (runner is null) return (false, error, tools);
                var (initOk, initError) = await runner.InitializeAsync(cfg.Name, ct).ConfigureAwait(false);
                if (!initOk) return (false, initError, tools);
                return await DiscoverWithAsync(runner.ListAsync, cfg, tools, ct).ConfigureAwait(false);
            }

            using var http = new HttpChannel(cfg!);
            var (hInitOk, hInitError) = await http.InitializeAsync(cfg.Name, ct).ConfigureAwait(false);
            if (!hInitOk) return (false, hInitError, tools);
            return await DiscoverWithAsync(http.ListAsync, cfg, tools, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            App.WriteLog($"[MCP] 连接「{cfg?.Name}」被取消");
            return (false, "连接已取消", tools);
        }
        catch (Exception ex)
        {
            App.WriteLog($"[MCP] 连接「{cfg?.Name}」失败：{ex}");
            return (false, "连接失败：" + Clip(ex.Message, MaxStringChars), tools);
        }
    }

    private delegate Task<(bool Ok, JsonElement? Result, string Error)> ExchangeAsync(
        string method, JsonNode? parms, CancellationToken ct);

    private static async Task<(bool Ok, string Message, List<McpRemoteTool> Tools)> DiscoverWithAsync(
        ExchangeAsync exchange, McpServerConfig cfg, List<McpRemoteTool> tools, CancellationToken ct)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        string? cursor = null;

        for (var page = 0; page < MaxToolPages; page++)
        {
            JsonNode? parms = cursor is null ? null : new JsonObject { ["cursor"] = cursor };
            var (ok, result, error) = await exchange("tools/list", parms, ct).ConfigureAwait(false);
            if (!ok) return (false, error, tools);

            if (result is not { } root || root.ValueKind != JsonValueKind.Object)
                return (false, "服务没给出工具清单（tools/list 的 result 不是对象）", tools);

            if (root.TryGetProperty("tools", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in arr.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object) continue;
                    var name = CleanToolName(ReadStr(item, "name"), tools.Count + 1);
                    if (!seen.Add(name)) continue;   // 同一服务报的重名：留第一个，别让注册表键互相覆盖
                    var schema = item.TryGetProperty("inputSchema", out var s) && s.ValueKind == JsonValueKind.Object
                        ? Clip(s.GetRawText(), MaxSchemaChars) : "";
                    tools.Add(new McpRemoteTool(name, Clip(CleanLine(ReadStr(item, "description")), MaxStringChars), schema));
                    if (tools.Count >= MaxToolCount)
                    {
                        App.WriteLog($"[MCP] 「{cfg.Name}」工具数触到上限 {MaxToolCount}，后面的不再登记");
                        return (true, $"已连接「{cfg.Name}」，登记 {tools.Count} 个远端工具（到上限就停了）。", tools);
                    }
                }
            }

            cursor = root.TryGetProperty("nextCursor", out var nc) && nc.ValueKind == JsonValueKind.String
                ? nc.GetString() : null;
            if (string.IsNullOrEmpty(cursor))
                return (true, $"已连接「{cfg.Name}」，登记 {tools.Count} 个远端工具。", tools);
        }

        App.WriteLog($"[MCP] 「{cfg.Name}」的 tools/list 翻页超过 {MaxToolPages} 页，停止");
        return (true, $"已连接「{cfg.Name}」，登记 {tools.Count} 个远端工具（翻页到上限）。", tools);
    }

    // ==================== 调用 ====================

    /// <summary>
    /// tools/call。所有失败都变成 [MCP] 开头的字符串返回，不抛——工具结果是模型的输入，
    /// 抛出去只会让上层记成一个「未知错误」，模型也就不知道自己刚才那一下没成。
    /// </summary>
    public static async Task<string> CallToolAsync(McpServerConfig cfg, McpRemoteTool tool,
        string argumentsJson, CancellationToken ct)
    {
        if (!TryResolveTransport(cfg, out var stdio, out var error)) return "[MCP] " + error;
        if (stdio && !cfg!.Allowed) return "[MCP] 服务「" + cfg.Name + "」还没被允许在本机启动（" + NeedConsentMessage + "）";
        if (tool is null || string.IsNullOrWhiteSpace(tool.Name)) return "[MCP] 要调用的工具名是空的";
        if (!TryNormalizeArguments(argumentsJson, out var arguments, out var argsError)) return "[MCP] " + argsError;

        var parms = new JsonObject { ["name"] = tool.Name, ["arguments"] = arguments };

        try
        {
            if (stdio)
            {
                using var runner = StdioRunner.Start(cfg!, out var startError);
                if (runner is null) return "[MCP] " + startError;
                var (initOk, initError) = await runner.InitializeAsync(cfg.Name, ct).ConfigureAwait(false);
                if (!initOk) return "[MCP] " + initError;
                var (ok, result, callError) = await runner.RequestAsync("tools/call", parms, ToolCallTimeout, ct)
                    .ConfigureAwait(false);
                return ok ? Render(result) : "[MCP] " + callError;
            }

            using var http = new HttpChannel(cfg!);
            var (hInitOk, hInitError) = await http.InitializeAsync(cfg.Name, ct).ConfigureAwait(false);
            if (!hInitOk) return "[MCP] " + hInitError;
            var (hOk, hResult, hError) = await http.RequestAsync("tools/call", parms, ToolCallTimeout, ct)
                .ConfigureAwait(false);
            return hOk ? Render(hResult) : "[MCP] " + hError;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            App.WriteLog($"[MCP] 调用 {tool.Name} 被取消");
            return "[MCP] 调用已取消";
        }
        catch (Exception ex)
        {
            App.WriteLog($"[MCP] 调用 {tool.Name} 失败：{ex}");
            return "[MCP] 调用失败：" + Clip(ex.Message, MaxStringChars);
        }

        static string Render(JsonElement? result)
        {
            var text = ExtractResultText(result, out var isError);
            if (text.Length == 0) text = isError ? "服务报告这次调用失败，没给出内容" : "服务没有返回内容";
            return ClipForModel(isError ? "[MCP] " + text : text);
        }
    }

    // ==================== stdio 传输 ====================

    /// <summary>一次往返一个进程：构造即启动，Dispose 即把进程树收干净。</summary>
    private sealed class StdioRunner : IDisposable
    {
        private readonly Process _proc;
        private readonly StreamWriter _stdin;
        private readonly StreamReader _stdout;
        private readonly StreamReader _stderr;
        private readonly Task _stderrTask;
        private readonly StringBuilder _stderrText = new();
        private readonly object _stderrGate = new();
        private int _nextId;

        private StdioRunner(Process proc)
        {
            _proc = proc;
            _stdin = proc.StandardInput;
            _stdin.NewLine = "\n";   // NDJSON 用 \n；Windows 默认给 \r\n，多数服务端能忍，但没必要赌
            _stdout = proc.StandardOutput;
            _stderr = proc.StandardError;
            // stderr 必须一直抽：管道缓冲区只有几 KB，写满了子进程就会卡在自己的 WriteFile 上，
            // 表现是「进程在、就是永远不回话」——stdio 传输最经典的一个坑。
            _stderrTask = Task.Run(DrainStderrAsync);
        }

        /// <summary>起不来就把原因写进 error 并返回 null，不抛。</summary>
        public static StdioRunner? Start(McpServerConfig cfg, out string error)
        {
            error = "";
            Process? proc = null;
            try
            {
                proc = new Process { StartInfo = BuildStdioStartInfo(cfg) };
                if (!proc.Start())
                {
                    error = $"启动「{Clip(cfg.Command, 120)}」失败（Process.Start 返回 false）";
                    proc.Dispose();
                    return null;
                }
                return new StdioRunner(proc);
            }
            catch (Exception ex)
            {
                try { proc?.Dispose(); }
                catch (Exception dex) { App.WriteLog("[MCP] 释放启动失败的进程句柄出错：" + dex.Message); }
                error = $"启动「{Clip(cfg.Command, 120)}」失败：{Clip(ex.Message, MaxStringChars)}";
                App.WriteLog("[MCP] stdio 启动失败：" + ex);
                return null;
            }
        }

        public async Task<(bool Ok, string Error)> InitializeAsync(string serverName, CancellationToken ct)
        {
            var parms = new JsonObject
            {
                ["protocolVersion"] = ProtocolVersion,
                ["capabilities"] = new JsonObject(),
                ["clientInfo"] = new JsonObject
                {
                    ["name"] = ClientName,
                    ["title"] = Clip(CleanLine(serverName), MaxStringChars),
                    ["version"] = ClientVersion
                }
            };
            var (ok, _, error) = await RequestAsync("initialize", parms, InitializeTimeout, ct).ConfigureAwait(false);
            if (!ok) return (false, error);

            // initialized 是通知：写完就算完，没有回应要等
            try
            {
                await _stdin.WriteAsync(FrameNotification("notifications/initialized", null)).ConfigureAwait(false);
                await _stdin.WriteAsync("\n").ConfigureAwait(false);
                await _stdin.FlushAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                App.WriteLog("[MCP] initialized 通知没写进去（服务端可能不认）：" + ex.Message);
            }
            return (true, "");
        }

        public Task<(bool Ok, JsonElement? Result, string Error)> ListAsync(
            string method, JsonNode? parms, CancellationToken ct)
            => RequestAsync(method, parms, ToolsListTimeout, ct);

        public async Task<(bool Ok, JsonElement? Result, string Error)> RequestAsync(
            string method, JsonNode? parms, TimeSpan timeout, CancellationToken ct)
        {
            var id = ++_nextId;
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(timeout);
            var seconds = (int)Math.Round(timeout.TotalSeconds);

            try
            {
                await _stdin.WriteAsync(FrameRequest(id, method, parms)).ConfigureAwait(false);
                await _stdin.WriteAsync("\n").ConfigureAwait(false);
                await _stdin.FlushAsync().ConfigureAwait(false);   // 不 flush 就永远等不到回应
            }
            catch (Exception ex)
            {
                return (false, null, $"往服务进程里写 {method} 失败：{Clip(ex.Message, 200)}");
            }

            while (true)
            {
                string? line;
                // AsTask 之后再 await：ValueTask 一旦被 await 过就不能二次消费，而catch 里还要把它交给 Observe
                var read = _stdout.ReadLineAsync(timeoutCts.Token).AsTask();
                try
                {
                    line = await read.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    Observe(read);
                    Abort();
                    if (ct.IsCancellationRequested) return (false, null, $"{method} 已取消");
                    return (false, null,
                        $"{method} 超时（{seconds} 秒），已终止进程。进程报错：{StderrTail()}");
                }
                catch (Exception ex)
                {
                    Observe(read);
                    return (false, null, $"读 {method} 的输出出错：{Clip(ex.Message, 200)}");
                }

                if (line is null)
                    return (false, null, $"服务进程提前退出了，{method} 没等到回应。进程报错：{StderrTail()}");
                if (line.Length > MaxLineChars)
                {
                    Abort();
                    return (false, null, $"{method} 的单行输出超过 {MaxLineChars} 字，已终止进程");
                }
                if (!TryParseResponse(line, id, out _, out var result, out var error)) continue;
                return error is null ? (true, result, "") : (false, null, error);
            }
        }

        /// <summary>
        /// 超时后那个 ReadLineAsync 还在飞行中（取消只是让它尽快结束）。异常得有人接，
        /// 不然它会漂进 UnobservedTaskException。
        /// </summary>
        private static void Observe(Task t)
            => _ = t.ContinueWith(x => _ = x.Exception,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);

        private async Task DrainStderrAsync()
        {
            try
            {
                while (true)
                {
                    var line = await _stderr.ReadLineAsync().ConfigureAwait(false);
                    if (line is null) break;
                    lock (_stderrGate)
                    {
                        if (_stderrText.Length < MaxStringChars * 2) _stderrText.Append(line).Append(' ');
                    }
                }
            }
            catch (Exception ex)
            {
                lock (_stderrGate) _stderrText.Append("[stderr 抽取中断] ").Append(ex.Message);
            }
        }

        private string StderrTail()
        {
            lock (_stderrGate)
            {
                var s = _stderrText.ToString().Trim();
                return s.Length == 0 ? "（无）" : Clip(s, 600);
            }
        }

        private void Abort()
        {
            try
            {
                if (!_proc.HasExited) _proc.Kill(entireProcessTree: true);
            }
            catch (Exception ex)
            {
                App.WriteLog("[MCP] 结束子进程失败：" + ex.Message);
            }
        }

        public void Dispose()
        {
            Abort();
            try { if (!_proc.WaitForExit(2000)) _proc.Kill(entireProcessTree: true); }
            catch (Exception ex) { App.WriteLog("[MCP] 等子进程退出时出错：" + ex.Message); }
            try { _stdin.Dispose(); }
            catch (Exception ex) { App.WriteLog("[MCP] 关闭 stdin 出错：" + ex.Message); }
            try { _stderrTask.Wait(TimeSpan.FromSeconds(1)); }
            catch (Exception ex) { App.WriteLog("[MCP] 等 stderr 抽取收尾时出错：" + ex.Message); }
            try { _proc.Dispose(); }
            catch (Exception ex) { App.WriteLog("[MCP] 释放子进程出错：" + ex.Message); }
        }
    }

    // ==================== http 传输 ====================

    /// <summary>
    /// streamable HTTP：一个请求一次 POST。initialize 响应头里的 mcp-session-id 要记下来回传，
    /// 否则服务端把后续请求当新会话，直接给你一个 404。
    /// </summary>
    private sealed class HttpChannel : IDisposable
    {
        private readonly McpServerConfig _cfg;
        private string? _sessionId;
        private int _nextId;

        public HttpChannel(McpServerConfig cfg) => _cfg = cfg;

        public async Task<(bool Ok, string Error)> InitializeAsync(string serverName, CancellationToken ct)
        {
            var parms = new JsonObject
            {
                ["protocolVersion"] = ProtocolVersion,
                ["capabilities"] = new JsonObject(),
                ["clientInfo"] = new JsonObject
                {
                    ["name"] = ClientName,
                    ["title"] = Clip(CleanLine(serverName), MaxStringChars),
                    ["version"] = ClientVersion
                }
            };
            var (ok, _, error) = await RequestAsync("initialize", parms, InitializeTimeout, ct).ConfigureAwait(false);
            if (!ok) return (false, error);
            await SendNotificationAsync("notifications/initialized", ct).ConfigureAwait(false);
            return (true, "");
        }

        public Task<(bool Ok, JsonElement? Result, string Error)> ListAsync(
            string method, JsonNode? parms, CancellationToken ct)
            => RequestAsync(method, parms, ToolsListTimeout, ct);

        public async Task<(bool Ok, JsonElement? Result, string Error)> RequestAsync(
            string method, JsonNode? parms, TimeSpan timeout, CancellationToken ct)
        {
            var id = ++_nextId;
            var (ok, body, session, error) = await PostAsync(FrameRequest(id, method, parms), timeout, ct)
                .ConfigureAwait(false);
            if (!ok) return (false, null, error);
            if (!string.IsNullOrEmpty(session)) _sessionId = session;
            if (string.IsNullOrWhiteSpace(body)) return (false, null, $"{method} 收到空响应体");

            // 先整体试一次（普通 JSON，可能是美化过的多行），再逐行试（SSE 一帧一条）
            if (TryParseResponse(body, id, out _, out var direct, out var directError))
                return directError is null ? (true, direct, "") : (false, null, directError);

            foreach (var line in body.Split('\n'))
            {
                if (!TryParseResponse(line, id, out _, out var result, out var lineError)) continue;
                return lineError is null ? (true, result, "") : (false, null, lineError);
            }
            return (false, null, $"{method} 的响应里找不到 id={id} 的那一条");
        }

        /// <summary>通知不等回应：202/204 都算送到，送不到也只记日志，不该打断调用。</summary>
        private async Task SendNotificationAsync(string method, CancellationToken ct)
        {
            var (ok, _, _, error) = await PostAsync(FrameNotification(method, null), ToolsListTimeout, ct)
                .ConfigureAwait(false);
            if (!ok) App.WriteLog($"[MCP] 通知 {method} 没送出去：{error}");
        }

        private async Task<(bool Ok, string Body, string? Session, string Error)> PostAsync(
            string frame, TimeSpan timeout, CancellationToken ct)
        {
            if (!IsHttpEndpoint(_cfg.Url, out var urlError)) return (false, "", null, urlError);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(timeout);
            using var req = new HttpRequestMessage(HttpMethod.Post, _cfg.Url.Trim());
            req.Content = new StringContent(frame, new UTF8Encoding(false), "application/json");
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
            req.Headers.TryAddWithoutValidation("MCP-Protocol-Version", ProtocolVersion);
            if (!string.IsNullOrEmpty(_sessionId)) req.Headers.TryAddWithoutValidation("Mcp-Session-Id", _sessionId);
            foreach (var (k, v) in _cfg.Headers ?? new Dictionary<string, string>())
            {
                if (string.IsNullOrWhiteSpace(k)) continue;
                // 用户自填的鉴权头与协议头合并；TryAddWithoutValidation 不会因头名生僻就炸
                req.Headers.TryAddWithoutValidation(k.Trim(), v ?? "");
            }

            HttpResponseMessage resp;
            try
            {
                resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                return (false, "", null, $"请求超时（{(int)timeout.TotalSeconds} 秒）：{Clip(_cfg.Url, 120)}");
            }
            catch (HttpRequestException ex)
            {
                return (false, "", null, $"连不上 {Clip(_cfg.Url, 120)}：{Clip(ex.Message, 300)}");
            }

            using (resp)
            {
                // HttpResponseHeaders 本身就是大小写不敏感的，这里只需要别把服务端的小写头名当成新头
                var session = resp.Headers.TryGetValues("mcp-session-id", out var values)
                    ? Clip(string.Join(",", values), MaxStringChars) : null;
                var body = await ReadBodyAsync(resp, timeoutCts.Token).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                    return (false, body, session,
                        $"HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}：{Clip(CleanLine(body), 600)}");
                return (true, body, session, "");
            }
        }

        /// <summary>有界读取：坏服务端可以用一条无限流把内存吃掉，读到上限就停手。</summary>
        private static async Task<string> ReadBodyAsync(HttpResponseMessage resp, CancellationToken ct)
        {
            try
            {
                await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                using var reader = new StreamReader(stream, Encoding.UTF8);
                var sb = new StringBuilder();
                var buf = new char[8192];
                while (sb.Length <= MaxBodyChars)
                {
                    var n = await reader.ReadAsync(buf.AsMemory(), ct).ConfigureAwait(false);
                    if (n <= 0) break;
                    sb.Append(buf, 0, n);
                }
                if (sb.Length > MaxBodyChars)
                    App.WriteLog($"[MCP] 响应体超过 {MaxBodyChars} 字，只留前段");
                return Clip(sb.ToString(), MaxBodyChars);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                App.WriteLog("[MCP] 读响应体中断：" + ex.Message);
                return "";
            }
        }

        public void Dispose() { }   // HttpClient 是 static 的，这里没有要收的东西
    }
}
