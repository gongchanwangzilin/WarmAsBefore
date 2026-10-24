using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace WarmAsBefore.Services;

/// <summary>
/// plugin.json 里「一个字符串或一个数组都算对」的字段用这个。
/// 直接写成 List&lt;string&gt; 的话，作者少写一对中括号（"runtime": "node"）会让整份清单解析失败，
/// 而解析失败的插件是**直接不出现**在列表里——用户看不到任何原因。宽松读、严格报，才是体检。
/// </summary>
public sealed class OneOrManyConverter : JsonConverter<List<string>>
{
    public override List<string> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var list = new List<string>();
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                Add(list, reader.GetString());
                break;
            case JsonTokenType.StartArray:
                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                {
                    if (reader.TokenType == JsonTokenType.String) Add(list, reader.GetString());
                    else if (reader.TokenType == JsonTokenType.StartObject || reader.TokenType == JsonTokenType.StartArray)
                        reader.Skip();   // 数组里混了对象：跳过这个元素，别把整份清单弄挂
                    // 数字/布尔之类的值转成字符串没意义，直接略过
                }
                break;
            default:
                reader.Skip();
                break;
        }
        return list;

        static void Add(List<string> to, string? v)
        {
            if (!string.IsNullOrWhiteSpace(v)) to.Add(v.Trim());
        }
    }

    public override void Write(Utf8JsonWriter writer, List<string> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var v in value) writer.WriteStringValue(v);
        writer.WriteEndArray();
    }
}

/// <summary>体检结论的一条。Blocking = 这一条不修就确实跑不起来。</summary>
public enum DoctorLevel { Error, Warn, Info }

public sealed record DoctorFinding(DoctorLevel Level, string Where, string Problem, string Advice = "")
{
    public bool Blocking => Level == DoctorLevel.Error;
    public string Glyph => Level switch { DoctorLevel.Error => "✕", DoctorLevel.Warn => "△", _ => "·" };
    /// <summary>一行给 UI 看的结论：问题 + 怎么改。</summary>
    public string Line => Advice.Length == 0 ? $"{Glyph} [{Where}] {Problem}" : $"{Glyph} [{Where}] {Problem} → {Advice}";
}

/// <summary>
/// 插件体检：把「装了但什么都不发生」翻译成人话。
///
/// 为什么需要它：这套插件体系里的失败大多是**静默**的。plugin.json 解析失败＝插件不出现在列表里；
/// 钩子名写错＝没人调用那个文件；能力没声明＝宿主函数返回一个错误 JSON，不抛异常；
/// 缺运行时＝进程报一句「系统找不到指定的文件」。每一种都不会有弹窗，作者只能猜。
/// 所以这里把清单、钩子文件、JS 源码、权限声明、依赖与运行时放在一起对一遍，
/// 每条结论都带一句「怎么改」（多数直接给出可以照抄的那一行）。
///
/// 全程只读文件、只解析语法，不执行插件的 JS——体检不能把没审过的代码跑起来。
/// </summary>
public static class PluginDoctor
{
    /// <summary>宿主认识的钩子名。运行时的白名单以这份为准（工坊提示词也引用它，两处不再各写一份）。</summary>
    public static readonly IReadOnlyList<string> HookNames = new[]
    {
        "persona", "context", "beforeSend", "afterReply", "afterTurn",
        "onSceneChange", "onToolResult", "onLoad", "onUnload", "onInstall"
    };

    /// <summary>这几个钩子的返回值会被用上；没写 result 等于白跑一趟。</summary>
    private static readonly HashSet<string> ValueHooks =
        new(StringComparer.OrdinalIgnoreCase) { "persona", "context", "beforeSend", "afterReply" };

    private static readonly string[] SettingTypes = { "text", "number", "switch", "select", "secret" };

    /// <summary>本版本真的会接管的环节（PluginHost 只认 persona）。写别的是给自己空头承诺。</summary>
    private static readonly string[] HonoredOverrides = { "persona" };

    public static List<DoctorFinding> Inspect(PluginEntry entry, IReadOnlyCollection<string> installedIds,
        IReadOnlyList<RuntimeState>? runtimes = null)
    {
        var f = new List<DoctorFinding>();
        var m = entry.Manifest;
        var dir = entry.Dir ?? "";

        InspectIdentity(f, m, dir);
        InspectSettings(f, m, dir);
        InspectPages(f, m, dir);
        InspectDependencies(f, m, installedIds);
        InspectOverrides(f, m);
        InspectRuntime(f, m, runtimes);
        InspectHooks(f, m, dir, entry.Enabled);
        return f;
    }

    /// <summary>
    /// 这个插件要探测哪些运行时：plugin.json 里写的，加上「有网页就得靠系统浏览器」这一项。
    ///
    /// 为什么把 browser 算进来：本应用没有内嵌 WebView（全库没有一处），插件自带的设置页与页面
    /// 一律用系统浏览器打开。所以「这个插件要浏览器内核」在这里是真的依赖——没有浏览器，
    /// 那些页面点了没反应，而插件本身看着完全正常。宁可每次刷新多扫一次注册表。
    /// </summary>
    public static IReadOnlyList<string> RequiredRuntimeIds(PluginManifest m)
    {
        var ids = new List<string>(m.Runtime ?? new List<string>());
        if (NeedsBrowser(m)) ids.Add("browser");
        return ids.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>有没有任何一处要靠浏览器打开（自带页面或 html 设置页）。</summary>
    public static bool NeedsBrowser(PluginManifest m) =>
        m.Pages.Count > 0 || string.Equals(m.Settings?.Kind, "html", StringComparison.OrdinalIgnoreCase);

    private static void Add(List<DoctorFinding> f, DoctorLevel lv, string where, string problem, string advice = "")
        => f.Add(new DoctorFinding(lv, where, problem, advice));

    // ============ 身份 ============

    private static void InspectIdentity(List<DoctorFinding> f, PluginManifest m, string dir)
    {
        var folder = string.IsNullOrEmpty(dir) ? "" : Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar));
        if (!string.IsNullOrEmpty(folder) && !string.Equals(m.Id, folder, StringComparison.OrdinalIgnoreCase))
            Add(f, DoctorLevel.Warn, "id",
                $"plugin.json 写的 id 是「{m.Id}」，实际装在「{folder}」目录",
                "装载时以目录名为准（防止 ../ 越界）：把 plugin.json 的 id 改成目录名，两边一致才好找设置与日志");
        if (string.IsNullOrWhiteSpace(m.Name))
            Add(f, DoctorLevel.Error, "name", "没有 name", "插件必须有名字，否则不会被装载");
        if (string.IsNullOrWhiteSpace(m.Description))
            Add(f, DoctorLevel.Info, "description", "没写描述",
                "插件中心里只会显示一个名字，用户看不出它做什么");
        if (!string.IsNullOrWhiteSpace(m.Version) && !char.IsAsciiDigit(m.Version[0]))
            Add(f, DoctorLevel.Warn, "version", $"版本号「{m.Version}」不是数字开头", "用 1.0.0 这种三段式，更新时才对得上");
    }

    // ============ 设置页 ============

    private static void InspectSettings(List<DoctorFinding> f, PluginManifest m, string dir)
    {
        var s = m.Settings;
        if (!s.IsValid)
        {
            Add(f, DoctorLevel.Error, "settings", $"settings.kind 是「{(string.IsNullOrWhiteSpace(s.Kind) ? "空" : s.Kind)}」",
                "必须三选一：native（应用内表单）/ html（自带网页）/ none（明确不需要）。这一条不修，插件不能启用");
            return;
        }
        switch (s.Kind)
        {
            case "html":
                if (string.IsNullOrWhiteSpace(s.Page))
                    Add(f, DoctorLevel.Error, "settings.page", "kind 是 html 却没给 page",
                        "补 \"page\": \"pages/settings.html\"（相对插件目录）或一个 http(s) 地址");
                else if (!IsRemote(s.Page) && !FileExists(dir, s.Page))
                    Add(f, DoctorLevel.Error, "settings.page", $"设置页文件不存在：{s.Page}",
                        "检查 zip 里是否有这一项（路径相对插件目录）；打不进 zip 就写 none");
                break;

            case "native":
                if (s.Fields.Count == 0)
                    Add(f, DoctorLevel.Warn, "settings.fields", "kind 是 native 却一个字段都没有",
                        "要么给出 fields，要么改成 none——空表单点进去只是一片白");
                break;
        }

        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var fd in s.Fields)
        {
            var at = $"settings.field[{(string.IsNullOrWhiteSpace(fd.Key) ? "?" : fd.Key)}]";
            if (string.IsNullOrWhiteSpace(fd.Key)) { Add(f, DoctorLevel.Error, at, "字段没有 key", "key 是存进 settings.json 的名字，必须有"); continue; }
            if (!keys.Add(fd.Key)) Add(f, DoctorLevel.Error, at, $"字段 key「{fd.Key}」重复", "后面的会把前面的盖掉，留一个");
            if (!fd.Key.All(ch => char.IsLetterOrDigit(ch) || ch is '_' or '-' or '.'))
                Add(f, DoctorLevel.Error, at, $"key「{fd.Key}」有非常规字符", "只用字母、数字、下划线、连字符");
            if (string.IsNullOrWhiteSpace(fd.Label))
                Add(f, DoctorLevel.Warn, at, "没有 label", "表单上这一项只剩一个空标题");
            if (!SettingTypes.Contains(fd.Type, StringComparer.OrdinalIgnoreCase))
                Add(f, DoctorLevel.Error, at, $"type「{fd.Type}」不认识", $"只有 {string.Join(" / ", SettingTypes)}");
            if (string.Equals(fd.Type, "select", StringComparison.OrdinalIgnoreCase) && fd.Options.Count == 0)
                Add(f, DoctorLevel.Error, at, $"select 却没有 options", "补 \"options\": [\"a\",\"b\"]，否则下拉是空的");
        }
    }

    // ============ 页面 ============

    private static void InspectPages(List<DoctorFinding> f, PluginManifest m, string dir)
    {
        for (var i = 0; i < m.Pages.Count; i++)
        {
            var p = m.Pages[i];
            var at = $"pages[{i}]";
            var hasFile = !string.IsNullOrWhiteSpace(p.File);
            var hasRoute = !string.IsNullOrWhiteSpace(p.Route);
            if (!hasFile && !hasRoute)
            {
                Add(f, DoctorLevel.Error, at, "页面既没有 file 也没有 route",
                    "本地页面写 \"file\": \"pages/panel.html\"，远程页面写 \"route\": \"https://…\"");
                continue;
            }
            if (hasFile && !FileExists(dir, p.File))
                Add(f, DoctorLevel.Error, at, $"本地页面文件不存在：{p.File}", "路径相对插件目录；确认它打进 zip 了");
            if (hasRoute && !IsRemote(p.Route))
                Add(f, DoctorLevel.Error, at, $"route 不是 http(s) 地址：{p.Route}", "只接受 https:// 或 http:// 开头的地址");
            if (string.IsNullOrWhiteSpace(p.Title))
                Add(f, DoctorLevel.Warn, at, "页面没有 title", "插件中心「页面」按钮上只会显示一个空名字");
        }
        if (m.Pages.Count > 0)
            Add(f, DoctorLevel.Info, "pages", "页面交给系统浏览器打开",
                "本应用不内嵌网页内核：页面是外部浏览器里的一个窗口，能读写设置（走打开时附带的 callback），"
                + "但不会嵌在应用里。要一个应用内的面，就用 native 设置");
    }

    // ============ 依赖 ============

    private static void InspectDependencies(List<DoctorFinding> f, PluginManifest m, IReadOnlyCollection<string> installedIds)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in m.Dependencies)
        {
            var at = $"dependencies[{(string.IsNullOrWhiteSpace(d.Id) ? "?" : d.Id)}]";
            if (string.IsNullOrWhiteSpace(d.Id))
            {
                Add(f, DoctorLevel.Error, at, "依赖没有 id", "写 \"id\": \"base_mood\"（是被依赖插件的目录名）");
                continue;
            }
            if (!seen.Add(d.Id)) Add(f, DoctorLevel.Warn, at, "依赖重复声明", "留一条就够");
            if (string.Equals(d.Id, m.Id, StringComparison.OrdinalIgnoreCase))
                Add(f, DoctorLevel.Error, at, "依赖了自己", "自己永远满足不了自己，会一直报缺依赖");
            else if (!installedIds.Contains(d.Id))
                Add(f, d.Optional ? DoctorLevel.Info : DoctorLevel.Error, at,
                    d.Optional ? $"可选依赖「{d.Id}」没装" : $"缺依赖「{d.Id}」",
                    string.IsNullOrWhiteSpace(d.Url)
                        ? "先装被依赖的那个插件（依赖不会自动安装）"
                        : $"到插件中心装 {d.Url}" + (d.Optional ? "（可选，不装也能跑）" : ""));
        }
    }

    private static void InspectOverrides(List<DoctorFinding> f, PluginManifest m)
    {
        foreach (var o in m.Overrides)
        {
            if (HonoredOverrides.Contains(o, StringComparer.OrdinalIgnoreCase)) continue;
            Add(f, DoctorLevel.Warn, "overrides", $"声明接管「{o}」，本版本只会真正兑现 persona",
                "beforeSend / afterReply 的改写本来就是返回值覆盖，不需要写在 overrides 里；其余目标目前无效");
        }
    }

    // ============ 外部运行时 ============

    private static void InspectRuntime(List<DoctorFinding> f, PluginManifest m, IReadOnlyList<RuntimeState>? runtimes)
    {
        // 传进来的探测结果就是「这个插件要的那些」，包括宿主替它补的 browser，
        // 所以这里不再回头要求 m.Runtime 非空——只声明了网页、没写 runtime 的插件也要能报缺浏览器。
        if (runtimes is null || runtimes.Count == 0) return;
        foreach (var r in runtimes)
        {
            if (r.Present)
            {
                Add(f, DoctorLevel.Info, "runtime", $"{r.Label}：{r.Detail}");
                continue;
            }
            var unknown = r.Detail.Contains("不认识", StringComparison.Ordinal);
            Add(f, unknown ? DoctorLevel.Warn : DoctorLevel.Error, "runtime",
                unknown ? $"plugin.json 里写了 runtime「{r.Id}」" : $"{r.Label}：没装",
                r.Advice.Length > 0 ? r.Advice : $"需要 {r.Id}，装完重启本应用（{r.InstallUrl}）");
            if (!string.IsNullOrWhiteSpace(r.Fallback))
                Add(f, DoctorLevel.Info, "runtime", $"{r.Label}的替代路线", r.Fallback);
        }
    }

    // ============ 钩子与 JS ============

    private static void InspectHooks(List<DoctorFinding> f, PluginManifest m, string dir, bool enabled)
    {
        foreach (var pair in m.Hooks)
        {
            var at = $"hooks.{pair.Key}";
            if (!HookNames.Contains(pair.Key, StringComparer.OrdinalIgnoreCase))
            {
                Add(f, DoctorLevel.Error, at, "钩子名宿主不认识",
                    $"只会执行这些名字：{string.Join(" / ", HookNames)}");
                continue;
            }
            if (string.IsNullOrWhiteSpace(pair.Value))
            {
                Add(f, DoctorLevel.Error, at, "钩子没给文件", "写成 \"persona\": \"hooks/persona.js\"");
                continue;
            }
            if (!WithinDir(dir, pair.Value))
            {
                Add(f, DoctorLevel.Error, at, $"钩子文件跑到插件外面：{pair.Value}", "只允许插件目录内的相对路径，不能带 ../ 也不能是绝对路径");
                continue;
            }
            if (!Path.GetExtension(pair.Value).Equals(".js", StringComparison.OrdinalIgnoreCase))
                Add(f, DoctorLevel.Warn, at, $"钩子不是 .js：{pair.Value}", "沙箱只跑 JavaScript");

            if (!FileExists(dir, pair.Value))
            {
                Add(f, DoctorLevel.Error, at, $"文件不存在：{pair.Value}",
                    enabled ? "现在这个钩子每次都被跳过（只在插件日志里留一行）" : "启用后会每次跳过");
                continue;
            }

            string js;
            try { js = File.ReadAllText(Path.Combine(dir, pair.Value)); }
            catch (Exception ex)
            {
                Add(f, DoctorLevel.Error, at, "读不出来：" + ex.Message, "检查文件是否被占用或名字过长");
                continue;
            }
            if (js.Trim().Length == 0)
            {
                Add(f, DoctorLevel.Warn, at, "文件是空的", "空文件等于这个钩子什么都不做");
                continue;
            }
            InspectJs(f, m, at, pair.Key, js);
        }
    }

    private static void InspectJs(List<DoctorFinding> f, PluginManifest m, string at, string hook, string js)
    {
        // 1) 语法。Acornima 是 Jint 自带的解析器，这里只 Parse 不执行。
        try
        {
            new Acornima.Parser().ParseScript(js, Path.GetFileNameWithoutExtension(hook));
        }
        catch (Acornima.SyntaxErrorException ex)
        {
            Add(f, DoctorLevel.Error, at, "JS 语法不过：" + ex.Message,
                "按括号里的文件名:行:列改；沙箱不支持 await，异步逻辑要拆成一次调用里能跑完的同步代码");
        }
        catch (Exception ex)
        {
            Add(f, DoctorLevel.Info, at, "语法检查没做成：" + ex.GetType().Name);
        }

        // 2) 沙箱里根本拿不到的东西。
        if (Regex.IsMatch(js, @"\brequire\s*\("))
            Add(f, DoctorLevel.Error, at, "用了 require(...)", "沙箱里没有模块系统，外部能力只走 callTool / callPlugin / emit / fetch 这四个宿主函数");
        if (Regex.IsMatch(js, @"^\s*(import|export)\s", RegexOptions.Multiline))
            Add(f, DoctorLevel.Error, at, "用了 import / export", "沙箱是单文件同步执行，没有模块");
        if (Regex.IsMatch(js, @"\beval\s*\(") || Regex.IsMatch(js, @"\bnew\s+Function\s*\("))
            Add(f, DoctorLevel.Warn, at, "用了 eval / new Function", "能跑，但这是把字符串当代码执行；用户看不到里面是什么，权限也判不准");

        // 3) 返回值。
        if (ValueHooks.Contains(hook) && !Regex.IsMatch(js, @"\bresult\s*="))
            Add(f, DoctorLevel.Warn, at, $"{hook} 钩子没有给 result 赋值",
                "这个钩子的返回值会被用上：不写 result（或写 null）等于原文照过");

        // 4) 用到的越界能力有没有声明。这里只查「用了没声明」——声明了没用只是白写。
        foreach (var (cap, arg, hint) in UsedAbilities(js))
        {
            var capability = PluginCapabilities.All.FirstOrDefault(c =>
                string.Equals(c.Name, cap, StringComparison.OrdinalIgnoreCase));
            if (capability is null) continue;
            if (PluginPermissions.DeclaredFor(m, capability, arg)) continue;
            var line = string.IsNullOrEmpty(arg) ? hint : $"\"{cap}:{arg}\"";
            Add(f, DoctorLevel.Error, at,
                string.IsNullOrEmpty(arg)
                    ? $"用了 {hint.Split('（')[0]}，但 permissions 里没声明这一类"
                    : $"用了 {hint.Split('（')[0]}（{arg}），但 permissions 里没声明它",
                $"permissions 里加 {line}。没声明时宿主函数不报错，只返回一个错误 JSON、什么都不会发生；"
                + (cap is PluginCapabilities.Network or PluginCapabilities.LocalNet
                    ? "而且这一类默认是禁止，还要用户在权限页放行"
                    : "声明后第一次调用会弹授权询问"));
        }

        // 5) 设置项键名对不上：读到的永远是空串，插件的表现就像「设了没用」。
        if (m.Settings.Kind == "native" && m.Settings.Fields.Count > 0)
        {
            var declared = new HashSet<string>(m.Settings.Fields.Select(x => x.Key), StringComparer.OrdinalIgnoreCase);
            foreach (Match read in Regex.Matches(js, @"\bgetSetting\s*\(\s*['""]([^'""]+)['""]"))
            {
                var key = read.Groups[1].Value;
                if (!declared.Contains(key))
                    Add(f, DoctorLevel.Warn, at, $"getSetting(\"{key}\") 在 settings.fields 里没有这一项",
                        $"要么补一个 key 为 {key} 的字段，要么改回已有的那些：{string.Join(" / ", declared)}");
            }
        }

        // 6) persona 这种每轮都跑的钩子里塞「问一次」的能力 = 每轮弹一次授权。
        if ((hook is "persona" or "context") && Regex.IsMatch(js, @"\b(callTool|callPlugin|emit|notify)\s*\("))
            Add(f, DoctorLevel.Info, at, $"{hook} 每轮对话都会执行，而它里面要用户授权",
                "第一次会弹询问，「这次先不给」之后本进程内不再问；要稳定表现就把这类动作放到 afterTurn");
    }

    /// <summary>
    /// 从 JS 源码里挑出「用了哪条越界能力、对象是什么」。
    /// 只认字面量参数（callTool("send_to_user")）；变量传的调用只报「这一类没声明」，
    /// 不装作知道对象是谁——静态扫出来的假对象名会把人往错的方向引。
    /// </summary>
    private static IEnumerable<(string Cap, string Arg, string Hint)> UsedAbilities(string js)
    {
        var outList = new List<(string, string, string)>();

        var tool = Literal(js, "callTool");
        if (tool.Count > 0 || Bare(js, "callTool"))
            foreach (var t in Named(tool, "某个工具名"))
                outList.Add((PluginCapabilities.Tools, t, $"tools（调用工具 {t}）"));

        var plug = Literal(js, "callPlugin");
        if (plug.Count > 0 || Bare(js, "callPlugin"))
            foreach (var p in Named(plug, "某个插件 id"))
                outList.Add((PluginCapabilities.Plugins, p, $"plugins（调用插件 {p}）"));

        var topic = Literal(js, "emit");
        if (topic.Count > 0 || Bare(js, "emit"))
            foreach (var t in Named(topic, "某个话题名"))
                outList.Add((PluginCapabilities.Broadcast, t, $"broadcast（广播话题 {t}）"));

        if (Bare(js, "notify"))
            outList.Add((PluginCapabilities.Notify, "", "notify（弹用户可见提示）"));

        foreach (var url in Literal(js, "fetch"))
        {
            var host = HostOf(url);
            if (host.Length == 0) continue;
            outList.Add((IsLocalHost(host) ? PluginCapabilities.LocalNet : PluginCapabilities.Network,
                IsLocalHost(host) ? "" : host,
                IsLocalHost(host) ? "localnet（打到本机/局域网）" : $"network（访问 {host}）"));
        }
        if (outList.Any(x => x.Item1 == PluginCapabilities.Network) && !Literal(js, "fetch").Any(u => HostOf(u).Length > 0))
            outList.Add((PluginCapabilities.Network, "某个主机名", "network（访问外部主机）"));

        return outList;
    }

    private static List<string> Literal(string js, string fn)
    {
        var list = new List<string>();
        foreach (Match mt in Regex.Matches(js, @"\b" + fn + @"\s*\(\s*['""]([^'""]+)['""]"))
            list.Add(mt.Groups[1].Value.Trim());
        return list;
    }

    private static bool Bare(string js, string fn) =>
        Regex.IsMatch(js, @"\b" + fn + @"\s*\(") && Literal(js, fn).Count == 0;

    private static IEnumerable<string> Named(List<string> found, string placeholder) =>
        found.Count > 0 ? found : new[] { placeholder };

    private static string HostOf(string url)
    {
        try
        {
            if (Uri.TryCreate(url.Trim(), UriKind.Absolute, out var u) && !string.IsNullOrEmpty(u.IdnHost))
                return u.IdnHost.ToLowerInvariant();
        }
        catch { }
        return "";
    }

    /// <summary>本机、局域网与元数据地址：这些走 localnet，不是「出家门到公网」。</summary>
    private static bool IsLocalHost(string host)
    {
        if (host.Length == 0) return false;
        if (host is "localhost" or "::1" or "0.0.0.0" or "127.0.0.1") return true;
        if (host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) || !host.Contains('.')
            && !host.Contains(':')) return true;
        if (host.StartsWith("127.") || host.StartsWith("10.") || host.StartsWith("192.168.")
            || host.StartsWith("169.254.")) return true;
        if (host.StartsWith("172."))
        {
            var parts = host.Split('.');
            if (parts.Length > 1 && int.TryParse(parts[1], out var second) && second is >= 16 and <= 31) return true;
        }
        return false;
    }

    /// <summary>权限数组里写了不认识的能力名：它不会报错、也不会亮灯，会被静默忽略——必须说。</summary>
    public static List<string> UnknownPermissions(PluginManifest m)
    {
        var bad = new List<string>();
        foreach (var raw in PluginPermissions.DeclaredOf(m))
        {
            var name = raw.Split(':', 2)[0].Trim();
            if (name.Length == 0) continue;
            if (!PluginCapabilities.All.Any(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)))
                bad.Add(raw);
        }
        return bad;
    }

    // ============ 路径工具 ============

    private static bool IsRemote(string value) =>
        value.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
        || value.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    private static bool FileExists(string dir, string relative)
    {
        if (string.IsNullOrEmpty(dir) || IsRemote(relative)) return true;
        try { return File.Exists(Path.Combine(dir, relative.TrimStart('/', '\\').Replace('/', Path.DirectorySeparatorChar))); }
        catch { return false; }
    }

    /// <summary>钩子/页面写的路径必须还在插件目录里（宿主装载时同样只认目录内）。</summary>
    private static bool WithinDir(string dir, string relative)
    {
        if (string.IsNullOrEmpty(dir)) return !Path.IsPathRooted(relative);
        if (Path.IsPathRooted(relative)) return false;
        try
        {
            var full = Path.GetFullPath(Path.Combine(dir, relative.Replace('/', Path.DirectorySeparatorChar)));
            var root = Path.GetFullPath(dir) + Path.DirectorySeparatorChar;
            return full.StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    /// <summary>结论数组拼成一段可读文本（详情面板与日志都用它）。</summary>
    public static string Render(IReadOnlyList<DoctorFinding> findings) =>
        findings.Count == 0 ? "没查出问题。" : string.Join("\n", findings.Select(x => x.Line));

    public static int CountBlocking(IReadOnlyList<DoctorFinding> findings) => findings.Count(x => x.Blocking);
}
