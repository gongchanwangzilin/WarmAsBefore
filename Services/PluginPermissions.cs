using System.Text.Json;

namespace WarmAsBefore.Services;

/// <summary>用户对某项能力的策略。Ask 不是「模糊」：它会真的问一次，答案按能力+参数记下来。</summary>
public enum PluginGrant
{
    Deny = 0,
    Ask = 1,
    Allow = 2
}

/// <summary>询问时用户能给的答复。Once / DenyOnce 只活在本次进程，不落盘。</summary>
public enum PluginAnswer
{
    Deny = 0,
    Allow = 1,
    Once = 2,
    /// <summary>这一次不给，但别记住——下次照旧再问。</summary>
    DenyOnce = 3
}

/// <summary>能力按「越出自己家门的程度」分三档，档位决定默认策略。</summary>
public enum PluginRiskTier
{
    /// <summary>只动自己的设置与文件：默认放行，但可以撤销。</summary>
    Own = 0,
    /// <summary>会碰主程序的管线、别的插件、用户注意力：默认放行或询问。</summary>
    Pipeline = 1,
    Interact = 2,
    /// <summary>出本机：默认禁止，必须逐条声明并逐条放行。</summary>
    External = 3
}

/// <summary>一项能力的说明书：界面上摆出来的就是它。</summary>
public sealed record PluginCapability(
    string Name,
    string Title,
    string Detail,
    PluginRiskTier Tier,
    PluginGrant DefaultGrant,
    /// <summary>true = 授权要落到具体对象上（工具名、插件 id、主机、话题），不是一句「准许用工具」。</summary>
    bool ArgScoped,
    string ArgHint);

public static class PluginCapabilities
{
    public const string Settings = "settings";
    public const string Storage = "storage";
    public const string Log = "log";
    public const string Tools = "tools";
    public const string Plugins = "plugins";
    public const string Broadcast = "broadcast";
    public const string Notify = "notify";
    public const string Page = "page";
    public const string PromptWrite = "prompt.write";
    public const string PipelineTransform = "pipeline.transform";
    public const string PipelineOverride = "pipeline.override";
    public const string Network = "network";
    public const string LocalNet = "localnet";
    public const string Stats = "stats";

    public static readonly IReadOnlyList<PluginCapability> All = new List<PluginCapability>
    {
        new(Settings, "自己的设置",
            "读与写 plugins/<插件>/settings.json 里属于它自己的那几项。看不到游戏设置，也看不到别的插件的设置。",
            PluginRiskTier.Own, PluginGrant.Allow, false, ""),
        new(Storage, "自己的文件区",
            "在 plugins/<插件>/files/ 下面写小文件（单个 1MB、合计 8MB）。出了这个目录一律拒绝，读写不到存档、图片与设置。",
            PluginRiskTier.Own, PluginGrant.Allow, false, ""),
        new(Log, "写应用日志",
            "往应用日志追加一行（自动带上插件名前缀）。日志会随数据包导出，别把用户的私密内容写进去。",
            PluginRiskTier.Own, PluginGrant.Allow, false, ""),
        new(PromptWrite, "注入人设与上下文",
            "persona / context 钩子往提示词里加文字。设为禁止后这两个钩子直接不执行——它唯一的作用就是加字。",
            PluginRiskTier.Pipeline, PluginGrant.Allow, false, ""),
        new(PipelineTransform, "改写对话文本",
            "beforeSend / afterReply 的返回值替换原文。这是「改写这一句」，不是「接管管线」。设为禁止后钩子不执行，文本原样流过。",
            PluginRiskTier.Pipeline, PluginGrant.Allow, false, ""),
        new(PipelineOverride, "整段接管管线",
            "声明了 overridePersona 的插件会清空别的人设段，只留自己那份。等于替主程序决定「TA 是谁」。",
            PluginRiskTier.Pipeline, PluginGrant.Ask, false, ""),
        new(Tools, "调用工具",
            "callTool 让宿主执行一个工具（内置、外部、MCP 都算）。工具能做的事比 JS 多得多，所以要逐个工具名放行。",
            PluginRiskTier.Interact, PluginGrant.Ask, true, "工具名，如 tools:send_to_user"),
        new(Plugins, "调用其它插件",
            "callPlugin 进入另一个插件的钩子。被调用方仍然受自己那份授权约束，不会借着别人的手越权。",
            PluginRiskTier.Interact, PluginGrant.Ask, true, "插件 id，如 plugins:mood_echo"),
        new(Broadcast, "广播与收听",
            "emit(topic, payload) 发给其它插件的 on:<topic> 钩子，话题名要逐条声明。禁止后 emit 直接被拦。",
            PluginRiskTier.Interact, PluginGrant.Ask, true, "话题名，如 broadcast:turn.done"),
        new(Notify, "发用户通知",
            "在应用里弹一条用户可见的提示。打扰归打扰，但它不改数据，所以默认问一次。",
            PluginRiskTier.Interact, PluginGrant.Ask, false, ""),
        new(Stats, "调整角色数值",
            "adjustStat 直接改动某个角色的好感 / 信任 / 精力（走与主流程同一套钳制与落盘）。这是改数据，不是弹提示，所以逐个角色放行。",
            PluginRiskTier.Interact, PluginGrant.Ask, true, "角色 id，如 stats:mom"),
        new(Page, "打开自带页面",
            "manifest.pages 声明的页面，用系统浏览器打开。点了才开，插件自己弹不出来。",
            PluginRiskTier.Interact, PluginGrant.Allow, false, ""),
        new(Network, "访问外部网络",
            "fetch 出网。必须逐主机在 plugin.json 里声明（network:api.example.com），不跟跳转，只给 http/https。",
            PluginRiskTier.External, PluginGrant.Deny, true, "主机名，如 network:api.example.com"),
        new(LocalNet, "访问本机与内网",
            "允许 fetch 打到 127.0.0.1、局域网与 169.254 这类地址——本机服务往往没鉴权，这一条比外网更危险。",
            PluginRiskTier.External, PluginGrant.Deny, false, "")
    };

    public static readonly IReadOnlyDictionary<string, PluginCapability> ByName =
        All.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);

    public static PluginCapability? Find(string name)
        => ByName.TryGetValue((name ?? "").Trim(), out var c) ? c : null;

    /// <summary>不认识的能力名按最高风险处理：宁可让作者去改清单，也不给它一个默认放行。</summary>
    public static PluginGrant DefaultGrantOf(string name) => Find(name)?.DefaultGrant ?? PluginGrant.Deny;

    public static string TierLabel(PluginRiskTier tier) => tier switch
    {
        PluginRiskTier.Own => "只在自己家里",
        PluginRiskTier.Pipeline => "动主程序的管线",
        PluginRiskTier.Interact => "动用别的东西",
        _ => "出本机"
    };

    public static string GrantLabel(PluginGrant grant) => grant switch
    {
        PluginGrant.Allow => "允许",
        PluginGrant.Ask => "问一次",
        _ => "禁止"
    };

    /// <summary>plugin.json 里一句 "tools:send_to_user" → 能力 + 对象。* 与空对象都算「任意」。</summary>
    public static void SplitDeclaration(string raw, out string capability, out string arg)
    {
        capability = (raw ?? "").Trim();
        arg = "";
        var i = capability.IndexOf(':');
        if (i < 0) return;
        arg = capability[(i + 1)..].Trim();
        capability = capability[..i].Trim();
        if (arg is "*") arg = "";
    }
}

/// <summary>一次判定的结果：能不能做、为什么、按哪条策略。</summary>
public sealed record PluginDecision(bool Allowed, string Reason, PluginGrant Policy, bool Declared)
{
    public static PluginDecision Ok(PluginGrant policy) => new(true, "", policy, true);
    public static PluginDecision Block(string reason, PluginGrant policy, bool declared = true)
        => new(false, reason, policy, declared);
}

/// <summary>要问用户时递给界面的一句话。</summary>
public sealed record PluginAskRequest(string PluginId, string PluginName, PluginCapability Capability, string Arg);

public sealed class PluginPermissionEvent
{
    public string At { get; set; } = "";
    public string Plugin { get; set; } = "";
    public string Capability { get; set; } = "";
    public string Arg { get; set; } = "";
    /// <summary>放行 / 拒绝 / 未声明拦截 / 问过之后放行 / 问过之后拒绝。</summary>
    public string Result { get; set; } = "";
    public string Note { get; set; } = "";
    /// <summary>同一个插件同一对象在短时间内反复撞同一条判定，只留一行并计次——否则一段对话就能把留痕刷满。</summary>
    public int Hits { get; set; } = 1;
    public string LastAt { get; set; } = "";
}

/// <summary>plugins/permissions.json 的形状（整表读写，所以必须有装载护栏）。</summary>
public sealed class PluginPermissionDoc
{
    /// <summary>插件 id → 能力名 → 策略。</summary>
    public Dictionary<string, Dictionary<string, PluginGrant>> Grants { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>插件 id → "能力:对象" → 上次问出来的答案（allow / deny）。</summary>
    public Dictionary<string, Dictionary<string, string>> Answers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<PluginPermissionEvent> Audit { get; set; } = new();
}

/// <summary>界面上的一行：能力、说明书、当前策略、插件有没有声明。</summary>
public sealed record PluginPermissionRow(
    PluginCapability Capability,
    PluginGrant Policy,
    PluginGrant Default,
    bool Declared,
    bool Dirty,
    IReadOnlyList<string> Args,
    string LastResult);

/// <summary>
/// 插件权限：能力目录 + 三态授权 + 「声明 ∩ 授予」判定 + 留痕。
///
/// 两条原则决定了它既不是一刀切、也不是随便放：
/// 1）**没声明就没有**：plugin.json 里没写的能力，用户就算在界面上允许了也不给——声明才是可审计的，
///    一个不声明就能用网络的清单等于「作者没打算要，运行时却拿到了」。
/// 2）**默认按危险程度分档**：待在自己家里（设置、文件区、日志）默认放行；
///    动管线与别人的东西默认放行或问一次；出本机默认禁止，还要逐主机声明、逐主机放行。
/// 「问一次」是真问：第一次撞上时弹窗，选「总是允许 / 总是拒绝」就落盘，选「仅本次」只活到进程结束。
/// </summary>
public sealed class PluginPermissions
{
    private const string StoreKey = "plugins/permissions";
    private const int AuditCap = 120;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly StorageProvider _store;
    private readonly object _gate = new();
    private readonly PluginPermissionDoc _doc = new();
    private readonly HashSet<string> _sessionAllow = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _sessionDeny = new(StringComparer.OrdinalIgnoreCase);
    private bool _loaded;
    private bool _loadFaulted;
    private bool _auditPending;
    private DateTime _lastAuditFlush = DateTime.MinValue;

    /// <summary>Ask 时的提问入口，由 RuntimeConfigurator 接线。没接上就按最安全的答复办：拒绝。</summary>
    public Func<PluginAskRequest, Task<PluginAnswer>>? Prompter { get; set; }

    /// <summary>策略变了（用户点了按钮、或一次询问落了盘）：界面据此刷新。</summary>
    public event Action? Changed;

    public PluginPermissions(StorageProvider store) => _store = store;

    public async Task EnsureLoadedAsync()
    {
        if (_loaded) return;
        try
        {
            var saved = await _store.Load<PluginPermissionDoc>(StoreKey);
            lock (_gate)
            {
                _doc.Grants.Clear();
                _doc.Answers.Clear();
                _doc.Audit.Clear();
                if (saved is not null)
                {
                    foreach (var (k, v) in saved.Grants)
                        _doc.Grants[k] = new Dictionary<string, PluginGrant>(v, StringComparer.OrdinalIgnoreCase);
                    foreach (var (k, v) in saved.Answers)
                        _doc.Answers[k] = new Dictionary<string, string>(v, StringComparer.OrdinalIgnoreCase);
                    _doc.Audit.AddRange(saved.Audit);
                }
                _loadFaulted = false;
                _loaded = true;
            }
        }
        catch (Exception ex)
        {
            // 读不出来时 _doc 是空的，下一次整表保存就会把用户所有授权抹成默认值。
            lock (_gate) { _loadFaulted = true; _loaded = false; }
            App.WriteLog("PluginPermissions.Load -> " + ex.Message);
        }
    }

    private async Task PersistAsync()
    {
        PluginPermissionDoc snapshot;
        lock (_gate)
        {
            if (_loadFaulted)
            {
                App.WriteLog("PluginPermissions.Persist -> 已跳过落盘：授权表没能读出来，不拿空表盖盘");
                return;
            }
            snapshot = new PluginPermissionDoc
            {
                Grants = _doc.Grants.ToDictionary(kv => kv.Key,
                    kv => new Dictionary<string, PluginGrant>(kv.Value, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase),
                Answers = _doc.Answers.ToDictionary(kv => kv.Key,
                    kv => new Dictionary<string, string>(kv.Value, StringComparer.Ordinal), StringComparer.Ordinal),
                Audit = _doc.Audit.ToList()
            };
        }
        await _store.Save(StoreKey, snapshot);
    }

    public PluginGrant PolicyOf(string pluginId, PluginCapability cap)
    {
        lock (_gate)
            if (_doc.Grants.TryGetValue(pluginId ?? "", out var m)
                && m.TryGetValue(cap.Name, out var g)) return g;
        return cap.DefaultGrant;
    }

    /// <summary>用户有没有为这个插件改过任何策略。</summary>
    public bool IsDirty(string pluginId)
    {
        lock (_gate) return _doc.Grants.TryGetValue(pluginId ?? "", out var m) && m.Count > 0;
    }

    /// <summary>插件声明的原始条目（"tools:send_to_user" 之类）。</summary>
    public static IReadOnlyList<string> DeclaredOf(PluginManifest manifest)
        => manifest.Permissions ?? new List<string>();

    /// <summary>
    /// 清单别处已经表达过同一个意图的能力：不必再往 permissions 里写一遍。
    ///
    /// 为什么要有这张表：「注入人设」「改写文本」这类能力的声明就是钩子本身——plugin.json 里点了
    /// persona 文件，作者要的就是往提示词里加字。再要求 permissions 重复写一次，
    /// 结果是所有已经装好的、以及旧版工坊生成的插件一夜之间全被「未声明」拦掉，而它们没做错任何事。
    /// 反过来，出本机（network/localnet）、调工具、调别的插件都只能靠 permissions 声明：没有别的表达方式。
    /// </summary>
    private static readonly Dictionary<string, string[]> ImpliedByHook = new(StringComparer.OrdinalIgnoreCase)
    {
        [PluginCapabilities.PromptWrite] = new[] { "persona", "context" },
        [PluginCapabilities.PipelineTransform] = new[] { "beforeSend", "afterReply" }
    };

    /// <summary>这个插件到底有没有被赋予这项能力（显式声明，或清单里的钩子/接管/页面隐含）。</summary>
    public static bool DeclaredFor(PluginManifest manifest, PluginCapability cap, string? arg = null)
    {
        // 待在自己家里的能力不要求声明：它碰的只有插件自己的设置项和自己的文件，
        // 边界是宿主划的（P0-4 那个包含校验），作者再写一遍 "storage" 只增加 ceremony，不增加任何保证。
        // 这一档照样能禁止——禁止拦的是运行时，声明拦的是「越出家门」。
        if (cap.Tier == PluginRiskTier.Own) return true;
        if (Declares(DeclaredOf(manifest), cap.Name, arg)) return true;
        if (ImpliedByHook.TryGetValue(cap.Name, out var hooks))
            foreach (var h in hooks)
                if (manifest.Hooks.ContainsKey(h)) return true;
        // 「整段接管」的声明就是 overrides 那一段：能清空别人的人设，前提是先声明了要清空什么。
        if (cap.Name == PluginCapabilities.PipelineOverride) return manifest.Overrides.Count > 0;
        // 自带页面的声明就是 pages：用户点了才开，不存在插件自己弹出来的路径。
        if (cap.Name == PluginCapabilities.Page) return manifest.Pages.Count > 0;
        // 出本机的一律只认显式声明，空数组也不放过。反过来，permissions 整个数组为空
        // 说明这份清单写于权限目录之前：拿「没声明」去拦它，等于让一次升级把所有已装插件
        // 的家门全锁上，而它们一行代码都没改。这种插件按策略走（该问的问、该禁的禁），
        // 一旦它补上任何一条声明，就立刻只认声明过的那几条。
        return DeclaredOf(manifest).Count == 0 && cap.Tier != PluginRiskTier.External;
    }

    /// <summary>声明里出现过某项能力的对象清单，界面用它说明「这条允许范围有多大」。</summary>
    public static IReadOnlyList<string> DeclaredArgs(IEnumerable<string> declarations, string capability)
    {
        var args = new List<string>();
        foreach (var raw in declarations)
        {
            PluginCapabilities.SplitDeclaration(raw, out var cap, out var arg);
            if (!string.Equals(cap, capability, StringComparison.OrdinalIgnoreCase)) continue;
            if (arg.Length == 0) return new[] { "任意" };
            args.Add(arg);
        }
        return args.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static bool Declares(IEnumerable<string> declarations, string capability, string? arg)
    {
        foreach (var raw in declarations)
        {
            PluginCapabilities.SplitDeclaration(raw, out var cap, out var declaredArg);
            if (!string.Equals(cap, capability, StringComparison.OrdinalIgnoreCase)) continue;
            if (declaredArg.Length == 0) return true;                       // 声明了整类
            if (string.IsNullOrWhiteSpace(arg)) return true;                  // 调用没带对象，按能力名放行
            if (string.Equals(declaredArg, arg, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    /// <summary>一次能力调用前的总闸：声明 ∩ 策略 ∩ 上次答案 ∩ 现场询问。</summary>
    public async Task<PluginDecision> CheckAsync(string pluginId, string pluginName, PluginManifest manifest,
        string capability, string? arg = null)
    {
        await EnsureLoadedAsync();
        var cap = PluginCapabilities.Find(capability);
        if (cap is null)
        {
            await RecordAsync(pluginId, capability, arg, "未知能力拦截", "目录里没有这项能力");
            return PluginDecision.Block("未知能力：" + capability, PluginGrant.Deny, false);
        }

        // 一、没声明就没有。用户允许也不能绕过清单。
        if (!DeclaredFor(manifest, cap, arg))
        {
            var hint = cap.ArgScoped && !string.IsNullOrWhiteSpace(arg)
                ? $"{cap.Name}:{arg}"
                : cap.Name;
            await RecordAsync(pluginId, cap.Name, arg ?? "", "未声明拦截", "清单里要写 " + hint);
            return PluginDecision.Block($"插件没有声明「{cap.Title}」（{hint}）", cap.DefaultGrant, false);
        }

        var policy = PolicyOf(pluginId, cap);
        var answerKey = AnswerKey(cap.Name, arg);
        var sessionKey = SessionKey(pluginId ?? "", cap.Name, arg);

        if (policy == PluginGrant.Allow) return PluginDecision.Ok(policy);
        if (policy == PluginGrant.Deny)
        {
            await RecordAsync(pluginId, cap.Name, arg ?? "", "拒绝", "策略是禁止");
            return PluginDecision.Block($"「{cap.Title}」已被设为禁止", policy);
        }

        // 二、问一次：先看落过盘的答案，再看本次进程的答案。
        lock (_gate)
        {
            if (_doc.Answers.TryGetValue(pluginId ?? "", out var m)
                && m.TryGetValue(answerKey, out var remembered))
                return remembered == "allow"
                    ? PluginDecision.Ok(policy)
                    : PluginDecision.Block($"上次问过，你选了拒绝：{answerKey}", policy);
            if (_sessionDeny.Contains(sessionKey))
                return PluginDecision.Block($"本次进程里已拒绝过：{answerKey}", policy);
            if (_sessionAllow.Contains(sessionKey)) return PluginDecision.Ok(policy);
        }

        var prompter = Prompter;
        if (prompter is null)
        {
            await RecordAsync(pluginId, cap.Name, arg ?? "", "拒绝", "问不到人");
            return PluginDecision.Block($"需要授权（{cap.Title}），但现在问不到用户", policy);
        }

        PluginAnswer answer;
        try
        {
            answer = await prompter(new PluginAskRequest(pluginId ?? "", pluginName ?? "", cap, arg ?? ""));
        }
        catch (Exception ex)
        {
            App.WriteLog("PluginPermissions.Prompt -> " + ex.Message);
            answer = PluginAnswer.Deny;
        }

        switch (answer)
        {
            case PluginAnswer.Allow:
                lock (_gate)
                {
                    if (!_doc.Answers.TryGetValue(pluginId ?? "", out var m))
                        _doc.Answers[pluginId ?? ""] = m = new Dictionary<string, string>(StringComparer.Ordinal);
                    m[answerKey] = "allow";
                }
                await PersistAsync();
                await RecordAsync(pluginId, cap.Name, arg ?? "", "问过之后放行", "总是允许");
                Raise();
                return PluginDecision.Ok(policy);
            case PluginAnswer.Deny:
                lock (_gate)
                {
                    if (!_doc.Answers.TryGetValue(pluginId ?? "", out var m))
                        _doc.Answers[pluginId ?? ""] = m = new Dictionary<string, string>(StringComparer.Ordinal);
                    m[answerKey] = "deny";
                }
                await PersistAsync();
                await RecordAsync(pluginId, cap.Name, arg ?? "", "问过之后拒绝", "总是拒绝");
                Raise();
                return PluginDecision.Block($"「{cap.Title}」被拒绝：{answerKey}", policy);
            case PluginAnswer.Once:
                lock (_gate) _sessionAllow.Add(sessionKey);
                await RecordAsync(pluginId, cap.Name, arg ?? "", "问过之后放行", "仅本次");
                return PluginDecision.Ok(policy);
            default:
                // DenyOnce：这次不给，也不落盘——下次撞上照旧再问一遍。
                lock (_gate) _sessionDeny.Add(sessionKey);
                await RecordAsync(pluginId, cap.Name, arg ?? "", "问过之后拒绝", "仅本次不给");
                return PluginDecision.Block($"这次没有放行：{answerKey}", policy);
        }
    }

    private static string SessionKey(string pluginId, string capability, string? arg)
        => pluginId + "|" + capability + (string.IsNullOrWhiteSpace(arg) ? "" : "|" + arg);

    private static string AnswerKey(string capability, string? arg)
        => capability + (string.IsNullOrWhiteSpace(arg) ? "" : "|" + arg);

    public async Task SetGrantAsync(string pluginId, string capability, PluginGrant grant)
    {
        await EnsureLoadedAsync();
        lock (_gate)
        {
            if (!_doc.Grants.TryGetValue(pluginId, out var m))
                _doc.Grants[pluginId] = m = new Dictionary<string, PluginGrant>(StringComparer.OrdinalIgnoreCase);
            if (grant == (PluginCapabilities.Find(capability)?.DefaultGrant ?? PluginGrant.Deny)) m.Remove(capability);
            else m[capability] = grant;
            // 策略一改，上次问出来的答案就作废：否则「禁止」下面还藏着一条「上次允许」的暗账。
            if (_doc.Answers.TryGetValue(pluginId, out var a)) a.Clear();
            _sessionAllow.Clear();
            _sessionDeny.Clear();
        }
        await PersistAsync();
        Raise();
    }

    /// <summary>撤销一个插件的全部定制（删插件、或用户想从头再授权一遍）。</summary>
    public async Task ResetAsync(string pluginId)
    {
        await EnsureLoadedAsync();
        lock (_gate)
        {
            _doc.Grants.Remove(pluginId);
            _doc.Answers.Remove(pluginId);
            DropSession(pluginId, null);
        }
        await PersistAsync();
        Raise();
    }

    /// <summary>
    /// 丢掉「仅本次」的临时允许/拒绝。停用插件时调它：重新启用等于重新认识一次，
    /// 不该继承上次关之前顺手点的那个「这次先允许」。
    /// </summary>
    public void ForgetSession(string pluginId)
    {
        lock (_gate) DropSession(pluginId, null);
    }

    private void DropSession(string pluginId, string? capability)
    {
        var prefix = pluginId + "|" + (capability ?? "");
        foreach (var set in new[] { _sessionAllow, _sessionDeny })
            foreach (var k in set.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList())
                set.Remove(k);
    }

    /// <summary>清掉「上次问过」的落盘答案，让这条能力重新问一次。</summary>
    public async Task AskAgainAsync(string pluginId, string capability)
    {
        await EnsureLoadedAsync();
        var prefix = pluginId + "|" + capability;
        lock (_gate)
        {
            if (_doc.Answers.TryGetValue(pluginId, out var a))
                foreach (var k in a.Keys.Where(k => k.StartsWith(capability, StringComparison.OrdinalIgnoreCase)).ToList())
                    a.Remove(k);
            foreach (var k in _sessionAllow.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList())
                _sessionAllow.Remove(k);
            foreach (var k in _sessionDeny.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList())
                _sessionDeny.Remove(k);
        }
        await PersistAsync();
        Raise();
    }

    public IReadOnlyList<PluginPermissionEvent> AuditOf(string? pluginId = null, int max = 40)
    {
        lock (_gate)
        {
            var q = _doc.Audit.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(pluginId))
                q = q.Where(e => string.Equals(e.Plugin, pluginId, StringComparison.OrdinalIgnoreCase));
            return q.OrderByDescending(e => e.At).Take(max).ToList();
        }
    }

    private async Task RecordAsync(string pluginId, string capability, string arg, string result, string note)
    {
        var now = DateTime.Now;
        var stamp = now.ToString("yyyy-MM-dd HH:mm:ss");
        lock (_gate)
        {
            // 同一条判定在短时间内反复撞上（每轮对话都会走一遍）只刷时间并计次，
            // 不然留痕会被重复行挤满，而且每一次都要整表落盘。
            var dup = _doc.Audit.FirstOrDefault(e =>
                string.Equals(e.Plugin, pluginId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(e.Capability, capability, StringComparison.OrdinalIgnoreCase)
                && string.Equals(e.Arg, arg, StringComparison.OrdinalIgnoreCase)
                && string.Equals(e.Result, result, StringComparison.OrdinalIgnoreCase));
            if (dup is not null)
            {
                dup.Hits++;
                dup.LastAt = stamp;
                dup.Note = note;
            }
            else
            {
                _doc.Audit.Insert(0, new PluginPermissionEvent
                {
                    At = stamp,
                    LastAt = stamp,
                    Plugin = pluginId,
                    Capability = capability,
                    Arg = arg,
                    Result = result,
                    Note = note
                });
                if (_doc.Audit.Count > AuditCap) _doc.Audit.RemoveRange(AuditCap, _doc.Audit.Count - AuditCap);
            }
            if ((now - _lastAuditFlush).TotalSeconds < 10) { _auditPending = true; return; }
            _lastAuditFlush = now;
            _auditPending = false;
        }
        await PersistAsync();
        Raise();
    }

    /// <summary>留痕是攒着写的（同一条判定只计次），界面读取时把没落盘的那点补上。</summary>
    public async Task FlushAsync()
    {
        bool pending;
        lock (_gate) { pending = _auditPending; _auditPending = false; }
        if (pending) await PersistAsync();
    }

    private void Raise()
    {
        try { Changed?.Invoke(); } catch (Exception ex) { App.WriteLog("PluginPermissions.Changed -> " + ex.Message); }
    }

    /// <summary>界面上的一张表：目录里的每条能力一行，附上插件声明的范围。</summary>
    public async Task<List<PluginPermissionRow>> RowsAsync(string pluginId, PluginManifest manifest)
    {
        await EnsureLoadedAsync();
        await FlushAsync();
        var decl = DeclaredOf(manifest);
        var audit = AuditOf(pluginId, 200);
        return PluginCapabilities.All.Select(cap => new PluginPermissionRow(
            cap,
            PolicyOf(pluginId, cap),
            cap.DefaultGrant,
            DeclaredFor(manifest, cap),
            IsDirty(pluginId) && PolicyOf(pluginId, cap) != cap.DefaultGrant,
            DeclaredArgs(decl, cap.Name),
            audit.FirstOrDefault(e => string.Equals(e.Capability, cap.Name, StringComparison.OrdinalIgnoreCase))?.Result ?? ""
        )).ToList();
    }

    /// <summary>清单里写了目录不认识的能力：界面要显式点出来，别让作者以为申请到了。</summary>
    public static List<string> UnknownDeclarations(PluginManifest manifest)
    {
        var bad = new List<string>();
        foreach (var raw in DeclaredOf(manifest))
        {
            PluginCapabilities.SplitDeclaration(raw, out var cap, out _);
            if (cap.Length > 0 && PluginCapabilities.Find(cap) is null) bad.Add(raw);
        }
        return bad;
    }

    public static string Serialize(PluginPermissionDoc doc) => JsonSerializer.Serialize(doc, Json);
}
