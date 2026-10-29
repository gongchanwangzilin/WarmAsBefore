using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Maui.ApplicationModel;
using WarmAsBefore.Models;
using WarmAsBefore.Modules.ApiManager;
using WarmAsBefore.Modules.Tools;
using WarmAsBefore.Services;

namespace WarmAsBefore.Modules.AiChat;


public sealed class ChatEngine
{
    private readonly ApiGateway _api;
    private readonly MemoryVault _memory;
    private readonly ToolManager _tools;
    private readonly Dictionary<string, List<ChatMessage>> _sessions = new();
    /// <summary>会话表的读写闸：主界面、QQ/微信、桌宠、存档运行时可能同时说到同一个角色。</summary>
    private readonly object _sessionLock = new();
    /// <summary>按角色串行：同一角色的两轮对话绝不能交错改写同一条历史。</summary>
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _charGates = new(StringComparer.Ordinal);
    private AiEndpoint _cfg = new();
    private CharacterProfile? _active;
    private string _rosterContext = "";
    private string _mapContext = "";
    private string _stickerContext = "";
    private bool _allowSilence;

    /// <summary>端点已明确拒绝过 tools 字段：本会话不再发送工具，避免每轮都白撞一次 400。</summary>
    private bool _toolsUnsupported;

    /// <summary>
    /// 工具管理器由构造注入（DI 里 ChatEngine 先于 ToolManager 注册，但单例是首次解析时才构造，
    /// 两者之间也不存在循环依赖，MauiProgram 的注册顺序无需调整）。
    /// </summary>
    public ChatEngine(ApiGateway api, MemoryVault memory, ToolManager tools)
    {
        _api = api;
        _memory = memory;
        _tools = tools;
    }

    /// <summary>沉默协议：开启后 AI 可选择不回复（输出空），而非每次都强制给答复。</summary>
    public const string SilenceProtocol =
        "当剧情中对方不适合或不想说话（正在忙、不想被打扰、想独处、或此刻无话可说）时，" +
        "你可以选择不回复：直接输出空内容（什么都不写，也不带动作标记）。" +
        "不要为了显得有回应而强行编造台词；沉默本身也是回应。" +
        "只有在确实有话说、且人设/情节支持开口时，才输出正常回复。";

    /// <summary>告诉 AI 当前陪伴的主角是谁（名字/性格/称呼/背景设定）。</summary>
    public void ConfigureCharacter(CharacterProfile profile) => _active = profile;

    /// <summary>工作记忆现状：活跃会话数与这些会话里的消息总数（记忆总览用）。</summary>
    public (int Sessions, int Messages) SessionStats()
    {
        try
        {
            lock (_sessionLock)
                return (_sessions.Count, _sessions.Values.Sum(s => s.Count));
        }
        catch (InvalidOperationException) { return (_sessions.Count, 0); }   // 正好在发消息，忽略这一帧
    }

    /// <summary>是否至少配了一个模型端点（存档运行时据此决定要不要跑 AI 生活回合）。</summary>
    public bool HasEndpoint => _api.HasAnyEndpoint;

    /// <summary>
    /// 副设定文本提供者（按角色 id）。由副设定服务注入；为空时人设与从前逐字段一致。
    /// 主设定是内核，副设定只影响语气与反应，每次建会话时读一次。
    /// </summary>
    public Func<string, string?>? SubProfileProvider { get; set; }

    /// <summary>
    /// 副设定走「模型改写」的开关与素材来源。两者都由 RuntimeConfigurator 注入；
    /// 开关关闭或素材为空时完全不调用模型。
    /// </summary>
    public bool SubProfileAiRewriteEnabled { get; set; }
    public Func<string, Task<string>>? SubProfileMaterial { get; set; }

    /// <summary>模型给出新的副设定表述后，由它负责真正写入（charId, 性格, 背景）。</summary>
    public Func<string, string?, string?, Task>? SubProfileRewritten { get; set; }

    /// <summary>每隔多少轮对话才做一次模型改写（默认 12 轮，避免频繁花钱）。</summary>
    public int SubProfileRewriteEveryTurns { get; set; } = 12;

    /// <summary>
    /// 关系网的模型辅助开关（默认关闭）：打开后每 20 轮请模型补几条关联。
    /// 补出来的边标记为 ai 来源，人工纠错不会被它冲淡。
    /// </summary>
    public bool SemanticAiAssistEnabled { get; set; }
    public int SemanticAiAssistEveryTurns { get; set; } = 20;
    private readonly Dictionary<string, int> _turnsSinceSemanticAssist = new(StringComparer.Ordinal);

    private readonly Dictionary<string, int> _turnsSinceRewrite = new(StringComparer.Ordinal);

    /// <summary>
    /// 让模型更新副设定的表述（可选、低频、省钱）：只输出两行「性格：… / 背景：…」。
    /// 未配置端点或调用失败时返回空，绝不打断对话。
    /// </summary>
    public async Task<(string? Personality, string? Description)> RewriteSubProfileAsync(string charId)
    {
        if (!_api.HasAnyEndpoint) return (null, null);
        try
        {
            var material = SubProfileMaterial is null ? "" : await SubProfileMaterial(charId);
            if (string.IsNullOrWhiteSpace(material)) return (null, null);

            var cfg = CfgFor(charId);
            var body = new
            {
                model = cfg.Model,
                messages = new object[]
                {
                    new
                    {
                        role = "system",
                        content = "你在更新一个人「现在的样子」的自我描述。TA 的人格内核不变，"
                                + "变的是经历这些之后 TA 对自己的看法与当下的状态。只输出两行，不要解释、不要标题。"
                    },
                    new
                    {
                        role = "user",
                        content = material
                            + "\n请输出两行：\n"
                            + "性格：<内核不变，但带上这段时间的变化，60 字以内>\n"
                            + "背景：<TA 此刻的处境与在意的事，80 字以内>"
                    }
                },
                temperature = 0.7,
                max_tokens = 300
            };
            var raw = await _api.ChatRaw(body);
            if (string.IsNullOrWhiteSpace(raw)) return (null, null);
            return ParseRewrite(raw!);
        }
        catch (Exception ex)
        {
            App.WriteLog("ChatEngine.RewriteSubProfile -> " + ex.Message);
            return (null, null);
        }
    }

    private static (string? Personality, string? Description) ParseRewrite(string raw)
    {
        string? p = null, d = null;
        foreach (var line in raw.Replace("\r\n", "\n").Split('\n'))
        {
            var t = line.Trim().TrimStart('-', '·', '*', ' ', '\t');
            if (t.StartsWith("性格：", StringComparison.Ordinal) || t.StartsWith("性格:", StringComparison.Ordinal))
                p = t[3..].Trim();
            else if (t.StartsWith("背景：", StringComparison.Ordinal) || t.StartsWith("背景:", StringComparison.Ordinal))
                d = t[3..].Trim();
        }
        return (string.IsNullOrWhiteSpace(p) ? null : p, string.IsNullOrWhiteSpace(d) ? null : d);
    }

    /// <summary>
    /// 请模型补几条词义关联（可选、低频）：让它输出「词A=词B」每行一组。
    /// 关闭开关、未配置端点或失败时都静默跳过——关联网本来就不该依赖模型。
    /// </summary>
    public async Task<List<(string A, string B)>> SuggestLinksAsync(string charId, IEnumerable<string> recentTexts)
    {
        var result = new List<(string, string)>();
        if (!_api.HasAnyEndpoint) return result;
        var material = string.Join("\n", recentTexts.Where(t => !string.IsNullOrWhiteSpace(t))).Trim();
        if (material.Length == 0) return result;
        if (material.Length > 3000) material = material[^3000..];

        try
        {
            var cfg = CfgFor(charId);
            var body = new
            {
                model = cfg.Model,
                messages = new object[]
                {
                    new
                    {
                        role = "system",
                        content = "你在整理一段对话里词与词的关联，供联想回忆用。"
                                + "只输出 3 组，每行一组，格式「词A=词B」，两个词都必须是对话里出现过的具体词，不要解释。"
                    },
                    new { role = "user", content = "对话记录：\n" + material + "\n\n输出 3 行「词A=词B」：" }
                },
                temperature = 0.5,
                max_tokens = 120
            };
            var raw = await _api.ChatRaw(body);
            if (string.IsNullOrWhiteSpace(raw)) return result;

            foreach (var line in raw!.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var t = line.Trim().TrimStart('-', '·', '*', ' ', '\t');
                var idx = t.IndexOf('=');
                if (idx <= 0) continue;
                var a = t[..idx].Trim().Trim('「', '」', '"', '\'');
                var b = t[(idx + 1)..].Trim().Trim('「', '」', '"', '\'');
                if (a.Length is < 1 or > 12 || b.Length is < 1 or > 12) continue;
                if (string.Equals(a, b, StringComparison.Ordinal)) continue;
                result.Add((a, b));
                if (result.Count >= 3) break;
            }
        }
        catch (Exception ex)
        {
            App.WriteLog("ChatEngine.SuggestLinks -> " + ex.Message);
        }
        return result;
    }

    /// <summary>
    /// 每回合结束后回调（角色 id，折叠出的记忆要点或 null）。用于让副设定随时间漂移。
    /// </summary>
    public Func<string, string?, Task>? AfterTurn { get; set; }

    /// <summary>
    /// 大脑预判：由 RuntimeConfigurator 注入。发送前本地结算期待差（PrepareTurn），
    /// 回合末可选地请免费模型补一次预判（AfterTurnAsync，节流缓存、不阻塞）。
    /// </summary>
    public BrainAnticipationService? Brain { get; set; }

    /// <summary>
    /// 副设定漂移后刷新人设：保留对话历史与折叠出的记忆段落，只重建人设部分，
    /// 让角色「现在是这个样子」立刻生效，而不必等新会话。
    /// </summary>
    public void RefreshPersona(string charId)
    {
        if (!_sessions.TryGetValue(charId, out var s) || s.Count == 0) return;
        const string marker = "\n\n【我的记忆】";
        var oldSystem = s[0].Content ?? "";
        var idx = oldSystem.IndexOf(marker, StringComparison.Ordinal);
        var carry = idx >= 0 ? oldSystem[idx..] : "";   // 折叠出的记忆绝不能丢
        var history = s.GetRange(1, s.Count - 1);
        _sessions.Remove(charId);
        var fresh = Session(charId);
        if (carry.Length > 0) fresh[0] = fresh[0] with { Content = fresh[0].Content + carry };
        fresh.AddRange(history);
    }

    /// <summary>
    /// 回合收尾：记人类记忆、学关系网、刷新标签清单、通知副设定漂移，再让人设即时生效。
    /// </summary>
    private async Task RunAfterTurnAsync(string charId, string? digest, string userText = "", string? reply = "")
    {
        if (Human is not null && !string.IsNullOrWhiteSpace(digest))
        {
            try { await Human.StoreFromDigestAsync(charId, digest); }
            catch (Exception ex) { App.WriteLog("ChatEngine.HumanStore -> " + ex.Message); }
        }
        // 关系网：同一轮里一起出现的词互相加强（纯统计，不花模型钱）
        if (Network is not null)
        {
            try { await Network.LearnAsync(charId, userText, reply); }
            catch (Exception ex) { App.WriteLog("ChatEngine.SemanticLearn -> " + ex.Message); }
        }
        // 模型辅助增边（默认关闭）：每 20 轮请模型补几条关联，标为 ai 来源
        if (SemanticAiAssistEnabled && Network is not null)
        {
            try
            {
                var turns = (_turnsSinceSemanticAssist.TryGetValue(charId, out var n) ? n : 0) + 1;
                if (turns >= Math.Max(4, SemanticAiAssistEveryTurns))
                {
                    _turnsSinceSemanticAssist[charId] = 0;
                    var pairs = await SuggestLinksAsync(charId, new[] { userText, reply ?? "" });
                    foreach (var (a, b) in pairs)
                        await Network.LinkAsync(charId, a, b, 2, "ai");
                }
                else
                {
                    _turnsSinceSemanticAssist[charId] = turns;
                }
            }
            catch (Exception ex) { App.WriteLog("ChatEngine.SemanticAiAssist -> " + ex.Message); }
        }
        await RefreshTagContextAsync(charId);

        // 大脑预判：可选的免费模型每 N 轮补一次（节流、缓存、非阻塞；不启用/未到点时内部直接返回）
        try { _ = Brain?.AfterTurnAsync(charId, userText, reply ?? ""); }
        catch (Exception ex) { App.WriteLog("ChatEngine.BrainAfterTurn -> " + ex.Message); }

        if (AfterTurn is null) return;
        try
        {
            var note = digest?
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.TrimStart('·', '-', '*', ' ', '\t'))
                .FirstOrDefault(l => l.Length > 0);
            await AfterTurn(charId, string.IsNullOrWhiteSpace(note) ? null : note);
            RefreshPersona(charId);
        }
        catch (Exception ex) { App.WriteLog("ChatEngine.AfterTurn -> " + ex.Message); }

        // 副设定的模型改写：默认关闭；开启后每 12 轮才做一次（省钱）。
        // 改的是「TA 对自己的表述」，主设定不动，随时可回正。
        if (SubProfileAiRewriteEnabled && SubProfileRewritten is not null)
        {
            try
            {
                var turns = (_turnsSinceRewrite.TryGetValue(charId, out var t) ? t : 0) + 1;
                if (turns >= Math.Max(2, SubProfileRewriteEveryTurns))
                {
                    _turnsSinceRewrite[charId] = 0;
                    var (p, d) = await RewriteSubProfileAsync(charId);
                    if (p is not null || d is not null)
                    {
                        await SubProfileRewritten(charId, p, d);
                        RefreshPersona(charId);
                    }
                }
                else
                {
                    _turnsSinceRewrite[charId] = turns;
                }
            }
            catch (Exception ex) { App.WriteLog("ChatEngine.SubProfileRewrite -> " + ex.Message); }
        }
    }

    /// <summary>
    /// 动作标记协议：每条回复必须以一个动作标记结尾。
    /// 【下沉】低落/害羞低头 【雀跃】开心跳起 【颤抖】害怕/激动发抖。
    /// </summary>
    public const string ActionProtocol =
        "你的每条回复，在最末尾都要带一个动作标记，表示你是怎样表现这句话的：" +
        "【下沉】（情绪低落、丧气或害羞地低下头）、【雀跃】（开心雀跃、惊喜地跳起来）、【颤抖】（害怕、激动或感动得发抖）。" +
        "标记必须放在回复的最末尾、紧挨正文之后，格式如“……真的好开心！【雀跃】”。" +
        "即使情节不适合明显的动作，也要选最接近的一个，不要省略、不要单独成段、不要加解释。";

    /// <summary>
    /// 工具提示：这种"陪聊"人设很容易只用嘴答应而不动手，所以有工具时明确告诉它工具的存在与用法。
    /// 只在确实有工具可用时拼进 system persona（没有工具时请求内容与从前完全一致）。
    /// </summary>
    public const string ToolHint =
        "你还可以调用系统提供的工具，真正改变画面或查询状态（查看当前状态、切换场景与氛围、调整立绘位置与透明度、播放动作动画）。" +
        "只有当用户的要求需要落到画面或状态上时才调用工具，日常闲聊不必调用。" +
        "工具返回的结果要如实转达：返回值里说没有产生视觉变化时，不要宣称画面已经变了。";

    /// <summary>
    /// 记忆检索提示：你们的全部对话都存在记忆库里；想不起来时就该去搜（关键词/日期），别硬编。
    /// 只在工具可用时和 ToolHint 一起注入。
    /// </summary>
    public const string MemoryHint =
        "\n你们的全部对话都保存在记忆库里。当需要回忆过去（某天、某句话、某个话题、某件事）时，" +
        "调用 search_memory 工具去搜——可以按关键词搜，也可以按日期搜（支持「昨天」「前天」「最近」这类说法）；" +
        "搜到什么就说什么，不要凭印象编造记不清的过去。";

    /// <summary>注入角色库：世界中还有其他角色时，AI 可在剧情里自由调用、扮演这些角色。</summary>
    public void SetRoster(string roster) => _rosterContext = roster ?? "";

    /// <summary>注入地图上下文：告诉 AI 当前可去的场景与【移动:场景名】移动协议。</summary>
    public void SetMapContext(string context) => _mapContext = context ?? "";

    /// <summary>
    /// #12 注入表情包清单：AI 可用 [表情:名字] 发表情。
    /// 与 roster/map 不同——表情库可能在会话存在中途变化（用户随时导入新表情），
    /// 所以这里会同步更新已存在会话的 system（只动 system，不碰历史消息，不会破坏 tool_calls 配对）。
    /// </summary>
    public void SetStickerContext(string context)
    {
        var v = context ?? "";
        if (v == _stickerContext) return;
        _stickerContext = v;
        foreach (var msgs in _sessions.Values)
        {
            if (msgs.Count == 0 || msgs[0].Role != "system") continue;
            var content = msgs[0].Content ?? "";
            var idx = content.IndexOf(StickerSectionHead, StringComparison.Ordinal);
            if (idx >= 0)
            {
                var end = content.IndexOf("\n【", idx + StickerSectionHead.Length, StringComparison.Ordinal);
                content = end >= 0 ? content.Remove(idx, end - idx) : content[..idx];
            }
            if (v.Length > 0) content += StickerSectionHead + v;
            msgs[0] = msgs[0] with { Content = content };
        }
    }

    /// <summary>表情包段在 system 里的固定包头（用于更新时定位，也是与其它段落的分隔）。</summary>
    private const string StickerSectionHead = "\n【表情包】";

    /// <summary>允许沉默：true 时 AI 可选择完全不回复（输出空内容），系统据此停止该回合。</summary>
    public void SetAllowSilence(bool allow) => _allowSilence = allow;

    public void Configure(AiEndpoint cfg, List<AiEndpoint>? rotationPool = null, bool rotationEnabled = false)
    {
        _cfg = cfg;
        _api.Configure(cfg);
        // 多端点/多密钥轮换池：任一失灵自动切换下一条，保证对话不间断
        _api.ConfigureRotation(rotationPool, rotationEnabled);
        // 端点/模型可能整个换了（设置页保存即走到这里）：重新给它一次带工具的机会
        _toolsUnsupported = false;
    }

    /// <summary>动态状态上下文（buff/标记）：每次 Send 前调用，返回值拼进用户消息前缀注入 AI。
    /// 由外部（如 ShopService）设置，避免 ChatEngine 反向依赖商店系统。</summary>
    public Func<string>? BuffContextProvider { get; set; }

    /// <summary>每次 Send 完成后回调（用于 tick buff 剩余轮数等）。</summary>
    public Action? AfterSend { get; set; }

    /// <summary>
    /// 角色级设置覆写链（全局设置 → 该角色覆写）。未注入时行为与从前完全一致。
    /// </summary>
    public CharacterSettingsResolver? Resolver { get; set; }

    /// <summary>
    /// 人类记忆层：按人脑方式存与取（重要性与时间衰减，不是取最近 N 条）。未注入时跳过。
    /// </summary>
    public HumanMemoryService? Human { get; set; }

    /// <summary>关系网：后台随对话自动长出来，用于联想检索（默认不花模型钱）。</summary>
    public RelationNetworkService? Network { get; set; }

    /// <summary>
    /// 额外人设段（性别、心理状态、身体感受、旁白模式……）：建会话时拼进 system。
    /// 这些内容来自设置与存档状态，变化后调用 <see cref="RefreshPersona"/> 即可生效。
    /// </summary>
    public Func<string, string>? ExtraPersonaProvider { get; set; }

    /// <summary>
    /// 按角色 id 取人设档案。界面切角色时会调 <see cref="ConfigureCharacter"/>，但那只有一个「当前」——
    /// Minecraft 桥让「不是界面正在聊的那位」女仆思考时，没有这个入口就会拿别人的名字和性格建会话。
    /// </summary>
    public Func<string, CharacterProfile?>? ProfileProvider { get; set; }

    /// <summary>每轮动态上下文（当前状态等），拼在用户消息前。</summary>
    public Func<string, string>? ExtraContextProvider { get; set; }

    /// <summary>异步动态上下文（插件钩子读文件，不能同步阻塞在 UI 线程上）。</summary>
    public Func<string, Task<string>>? ExtraContextProviderAsync { get; set; }

    /// <summary>发送前异步改写用户消息（插件钩子）：入参 (角色 id, 正文)。</summary>
    public Func<string, string, Task<string>>? BeforeSendTransformAsync { get; set; }

    /// <summary>回复后异步改写正文（插件钩子）：入参 (角色 id, 回复)。</summary>
    public Func<string, string, Task<string>>? AfterReplyTransformAsync { get; set; }

    /// <summary>标签层：AI 打的标签会注入人设（每次刷新取前 10~20 个）。</summary>
    public MemoryTagService? Tags { get; set; }

    /// <summary>智能省钱：决定该不该总结、用哪种级别的总结。未注入时按旧行为（到上限就总结）。</summary>
    public CostEstimatorService? Cost { get; set; }

    /// <summary>每个角色当前缓存的标签清单（异步刷新，人设同步读取）。</summary>
    private readonly Dictionary<string, string> _tagContext = new(StringComparer.Ordinal);

    /// <summary>刷新某角色的标签清单缓存，供下一次拼人设使用。</summary>
    public async Task RefreshTagContextAsync(string charId)
    {
        if (Tags is null) return;
        try
        {
            var text = await Tags.InjectTextAsync(charId);
            lock (_tagContext) _tagContext[charId] = text;
        }
        catch (Exception ex) { App.WriteLog("ChatEngine.RefreshTags -> " + ex.Message); }
    }

    /// <summary>
    /// 本次对话实际生效的端点：角色绑定 &gt; 顶栏临时指定 &gt; 全局轮换。
    /// 临时指定要当 baseCfg 传进解析链，不能只在网关里比引用相等——
    /// 叠了参数覆写的端点是新对象，那样连没绑模型的角色也会被自己的覆写顶掉，选择照样不生效。
    /// </summary>
    private AiEndpoint CfgFor(string charId)
    {
        var baseCfg = _api.TemporaryEndpoint ?? _cfg;
        return Resolver?.EndpointFor(charId, baseCfg) ?? baseCfg;
    }

    public async Task<string> Send(string charId, string text)
    {
        return await Send(charId, text, null);
    }

    /// <summary>
    /// #10：带文件附件的发送。附件里的文本内容会按 <see cref="MaxAttachmentChars"/> 上限截断，
    /// 与正文一起注入模型；二进制附件（图片/音频等）只报元信息，交由识图工具或用户后续处理。
    /// 附件清单同时存进这一条 user 消息（<see cref="ChatMessage.Attachments"/>），便于回显与追溯。
    /// </summary>
    public async Task<string> Send(string charId, string text, IReadOnlyList<ChatAttachment>? attachments,
        string memoryCategory = "dialogue")
    {
        // 同一角色一次只跑一轮：主界面、QQ/微信、桌宠与后台运行时都会走到这里，
        // 两轮交错会把同一条历史写成乱序，也可能让模型看到自己还没说完的半句。
        var gate = _charGates.GetOrAdd(charId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try { return await SendCore(charId, text, attachments, memoryCategory); }
        finally { gate.Release(); }
    }

    private async Task<string> SendCore(string charId, string text, IReadOnlyList<ChatAttachment>? attachments,
        string memoryCategory)
    {
        var session = Session(charId);
        var cfg = CfgFor(charId);
        // 大脑预判：本地结算本轮期待差与惊喜卡（无网络），供下面的动态上下文注入与好感结算用
        try { Brain?.PrepareTurn(charId, text); }
        catch (Exception ex) { App.WriteLog("ChatEngine.PrepareTurn -> " + ex.Message); }
        var recent = await _memory.Recent(charId, Math.Max(1, cfg.MemoryTurns));
        var ctx = string.Join("\n", recent.Select(m => m.Content));

        // 人类记忆层：按「重要性 × 时间衰减 × 复述次数 × 随机扰动」回想（默认以三天内为主），
        // 而不是按时间取最近若干条——人本来就会想起很久以前的重要事，也会突然想不起昨天说了什么。
        try
        {
            if (Human is not null)
            {
                var recalled = await Human.RecallTextAsync(charId,
                    Resolver?.MemoryWindowDays(charId) ?? HumanMemoryMath.DefaultWindowDays,
                    Resolver?.MemoryRecallRandomness(charId) ?? HumanMemoryMath.DefaultRandomness);
                if (!string.IsNullOrWhiteSpace(recalled))
                    ctx = ctx.Length == 0 ? recalled : recalled + "\n" + ctx;
            }
        }
        catch (Exception ex) { App.WriteLog("ChatEngine.HumanRecall -> " + ex.Message); }

        // 注入 buff/标记上下文（动态，随每次对话变化；拼在用户消息前，AI 可见但不算用户说的话）
        var buff = "";
        try { buff = BuffContextProvider?.Invoke() ?? ""; }
        catch (Exception ex) { App.WriteLog("ChatEngine.BuffContext -> " + ex.Message); }
        var extra = "";
        try { extra = ExtraContextProvider?.Invoke(charId) ?? ""; }
        catch (Exception ex) { App.WriteLog("ChatEngine.ExtraContext -> " + ex.Message); }
        try
        {
            if (ExtraContextProviderAsync is not null)
            {
                var more = await ExtraContextProviderAsync(charId);
                if (!string.IsNullOrWhiteSpace(more))
                    extra = string.IsNullOrWhiteSpace(extra) ? more : extra + "\n" + more;
            }
        }
        catch (Exception ex) { App.WriteLog("ChatEngine.ExtraContextAsync -> " + ex.Message); }
        var prefix = string.Join("\n", new[] { buff, extra }.Where(x => !string.IsNullOrWhiteSpace(x)));
        var payload = prefix.Length == 0 ? text : $"{prefix}\n{text}";

        // #10 附件注入：文本类拼正文，二进类只给元信息
        var fileBlock = BuildAttachmentBlock(attachments);
        if (fileBlock.Length > 0)
            payload = payload.Length > 0 ? $"{payload}\n{fileBlock}" : fileBlock;

        // 插件 beforeSend：只改写用户正文，记忆与上下文保持原样
        try
        {
            if (BeforeSendTransformAsync is not null)
                payload = await BeforeSendTransformAsync(charId, payload);
        }
        catch (Exception ex) { App.WriteLog("ChatEngine.BeforeSend -> " + ex.Message); }

        var userMsg = new ChatMessage { Role = "user", Content = $"[{ctx}]\n{payload}" };
        if (attachments is { Count: > 0 })
            userMsg = userMsg with { Attachments = attachments.ToList() };
        session.Add(userMsg);

        // 检查 API 是否已配置（主配置或轮换池任一可用即可）
        if (!_api.HasAnyEndpoint)
        {
            // AI 未配置：返回友好的提示
            var offlineReply = GenerateOfflineReply(text);
            session.Add(new ChatMessage { Role = "assistant", Content = offlineReply });
            await TrimAsync(session, cfg, charId);
            await RunAfterTurnAsync(charId, null, text, offlineReply);
            return offlineReply;
        }

        // API 已配置：调用 API（可能带工具往返）
        try
        {
            var (reply, error) = await RunTurnAsync(session, cfg);
            if (error is not null)
            {
                // API 返回了错误信息，直接显示给用户（不存会话历史）
                App.WriteLog("ChatEngine: API error: " + error);
                return error;
            }
            // 正文为 null（而不是空串）＝模型没给正文，沿用原来的提示语；空串是"沉默"，必须原样保留
            reply ??= "（AI 暂时无法回应，请检查 API 配置或网络连接）";
            // 插件 afterReply：可加时间戳、语气词、改写结尾；失败时保留原文
            try
            {
                if (AfterReplyTransformAsync is not null)
                    reply = await AfterReplyTransformAsync(charId, reply);
            }
            catch (Exception ex) { App.WriteLog("ChatEngine.AfterReply -> " + ex.Message); }
            session.Add(new ChatMessage { Role = "assistant", Content = reply });
            var digest = await TrimAsync(session, cfg, charId);

            await _memory.Store(new MemoryEntry
            {
                CharacterId = charId,
                Content = $"{text} → {reply}",
                Category = memoryCategory
            });
            try { AfterSend?.Invoke(); }
            catch (Exception ex) { App.WriteLog("ChatEngine.AfterSend -> " + ex.Message); }
            await RunAfterTurnAsync(charId, digest, text, reply);
            return reply;
        }
        catch (Exception ex)
        {
            App.WriteLog("ChatEngine.Send -> " + ex.Message);
            var errorMsg = $"（AI 调用出错：{ex.Message}）";
            // 异常路径的错误提示不进会话历史（避免污染后续上下文的 assistant 记忆），
            // 直接返回给调用方展示。
            return errorMsg;
        }
    }

    /// <summary>
    /// 存档运行时的生活回合：没有真人开口，让 TA 在自己的生活里走一段。
    /// 不暴露工具（后台不能悄悄改界面或往外发消息）、不结算好感，只写一条有界的生活记忆；
    /// 会话保留这两句，下次真人聊天时 TA 记得自己刚做过什么。
    /// 返回 null 表示没跑（未配置模型或失败），调用方保持本地推演即可。
    /// </summary>
    public async Task<string?> SendAmbient(string charId, string prompt)
    {
        if (!_api.HasAnyEndpoint || string.IsNullOrWhiteSpace(prompt)) return null;
        var gate = _charGates.GetOrAdd(charId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            var session = Session(charId);
            var cfg = CfgFor(charId);
            List<ChatMessage> msgs;
            lock (_sessionLock)
            {
                msgs = new List<ChatMessage> { session[0] };
                // 只带上最近的对话尾巴：生活回合不是复读机，也不该把整本历史再读一遍。
                for (var i = Math.Max(1, session.Count - 12); i < session.Count; i++)
                    msgs.Add(session[i]);
            }
            var recent = await _memory.Recent(charId, 6);
            if (recent.Count > 0)
                msgs.Add(new ChatMessage
                {
                    Role = "user",
                    Content = "（你最近记得的事）\n" + string.Join("\n", recent.Select(m => m.Content))
                });
            msgs.Add(new ChatMessage
            {
                Role = "system",
                Content = "你现在在自己的一天里，没有人在跟你对话。不要调用任何工具，不要对任何人说话，"
                        + "也不要用动作标记。只按下面的提示写一小段第一人称的生活。"
            });
            msgs.Add(new ChatMessage { Role = "user", Content = prompt });

            var reply = await _api.Chat(msgs, cfg);
            if (string.IsNullOrWhiteSpace(reply)) return null;
            var clean = reply.Trim();

            lock (_sessionLock)
            {
                session.Add(new ChatMessage { Role = "user", Content = "（生活片段）" + prompt });
                session.Add(new ChatMessage { Role = "assistant", Content = clean });
                CapSessionLocked(session);
            }
            return clean;
        }
        catch (Exception ex)
        {
            App.WriteLog("ChatEngine.SendAmbient -> " + ex.Message);
            return null;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>后台回合只保留有限条：不靠对话裁剪兜底，历史不该被生活片段撑爆。必须在 _sessionLock 内调用。</summary>
    private static void CapSessionLocked(List<ChatMessage> session)
    {
        const int max = 120;
        if (session.Count <= max) return;
        var cut = session.Count - max;
        if (cut % 2 == 1) cut++;   // user/assistant 成对裁，别把一句留下半截
        session.RemoveRange(1, cut);
    }

    /// <summary>单个文本附件注入模型的最大字符数（超出截断，避免一份大文件把上下文吃光）。</summary>
    public const int MaxAttachmentChars = 6000;


    /// <summary>
    /// 把附件清单渲染成一段给模型看的文本块。文本附件给出正文（截断），
    /// 二进制附件只给文件名/类型/大小/路径。没有附件时返回空串。
    /// </summary>
    public static string BuildAttachmentBlock(IReadOnlyList<ChatAttachment>? attachments)
    {
        if (attachments is null || attachments.Count == 0) return "";
        var sb = new System.Text.StringBuilder();
        sb.Append("[携带的文件]");
        foreach (var a in attachments)
        {
            if (a is null) continue;
            sb.Append('\n').Append("- ").Append(a.FileName)
              .Append("（").Append(a.Kind).Append(", ").Append(a.Size).Append(" 字节）");
            if (!string.IsNullOrEmpty(a.TextContent))
            {
                sb.Append("\n  内容：").Append(a.TextContent);
                if (a.Truncated) sb.Append("\n  （内容过长已截断）");
            }
            else
            {
                sb.Append("\n  （二进制文件，未内联内容；路径：").Append(a.Path).Append("）");
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// #10：从磁盘路径构造一个附件。文本类（txt/md/json/csv/log/xml/code 等）会读入正文并按
    /// <see cref="MaxAttachmentChars"/> 截断；图片/音频等二进制只登记元信息。
    /// 读失败也返回一个"占位附件"，不抛异常（附件不该拖垮整轮对话）。
    /// </summary>
    public static ChatAttachment BuildAttachmentFromPath(string path)
    {
        var name = System.IO.Path.GetFileName(path ?? "");
        var kind = GuessAttachmentKind(name);
        long size = 0;
        try { if (System.IO.File.Exists(path)) size = new System.IO.FileInfo(path).Length; }
        catch { }
        string? text = null;
        var truncated = false;
        if (kind == "text")
        {
            try
            {
                var raw = System.IO.File.ReadAllText(path);
                if (raw.Length > MaxAttachmentChars)
                {
                    raw = raw.Substring(0, MaxAttachmentChars);
                    truncated = true;
                }
                text = raw;
            }
            catch (Exception ex)
            {
                App.WriteLog("ChatAttachment.Read -> " + ex.Message);
                text = "（文件读取失败）";
            }
        }
        return new ChatAttachment
        {
            FileName = name,
            Path = path ?? "",
            Kind = kind,
            Size = size,
            TextContent = text,
            Truncated = truncated
        };
    }

    /// <summary>按扩展名粗判附件类型：text / image / audio / video / other。</summary>
    private static string GuessAttachmentKind(string fileName)
    {
        var ext = (System.IO.Path.GetExtension(fileName ?? "") ?? "").ToLowerInvariant();
        return ext switch
        {
            ".txt" or ".md" or ".json" or ".csv" or ".log" or ".xml" or ".yml" or ".yaml"
                or ".ini" or ".cfg" or ".cs" or ".js" or ".ts" or ".py" or ".java" or ".html"
                or ".css" or ".sh" or ".bat" or ".sql" or ".xaml" => "text",
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".bmp" or ".svg" => "image",
            ".mp3" or ".wav" or ".ogg" or ".m4a" or ".flac" or ".aac" => "audio",
            ".mp4" or ".mov" or ".avi" or ".mkv" or ".webm" => "video",
            _ => "other"
        };
    }

    // ============ 工具调用闭环 ============

    /// <summary>单个回合内最多执行的工具调用轮数：模型可以连续要工具，但必须有硬上限，否则不听话的端点会让请求无限套下去。</summary>
    private const int MaxToolRounds = 3;

    /// <summary>模型要过工具、最终却没给出正文时的兜底回复（不能落成"沉默"，那会误触发沉默协议）。</summary>
    private const string ToolFallbackReply = "（我照做啦，只是一时不知道该怎么接话。）";

    /// <summary>
    /// 跑完一个回合：请求 →（模型要工具就）执行 → 结果回灌 → 再请求，直到模型给出正文或用完工具轮数。
    ///
    /// 只把最终的 assistant 正文写回会话：中间的 `tool_calls` / `tool` 结果消息只活在本回合的临时消息流里。
    /// 原因是 Trim() 从头部裁历史，一旦把这两者裁散（tool 结果留下、对应的 tool_calls 被裁掉），
    /// 严格端点会因"tool 消息没有对应的 tool_calls"拒收整段历史 —— 那会直接毁掉聊天。
    /// </summary>
    private async Task<(string? Text, string? Error)> RunTurnAsync(List<ChatMessage> session, AiEndpoint cfg)
    {
        // 端点已拒绝过、或根本没有可用工具时保持 null：请求体与从前逐字段一致
        var tools = _toolsUnsupported ? null : _tools.BuildChatTools();
        if (tools is { Count: 0 }) tools = null;
        var allowed = tools is null ? null : _tools.ChatToolNames();

        var working = new List<ChatMessage>(session);
        string? text = null;
        var askedTools = false;

        for (var round = 0; round <= MaxToolRounds; round++)
        {
            var turn = await _api.ChatWithToolsAsync(working, tools, cfg);
            if (turn.ToolsRejected)
            {
                // 端点不认识 tools（已由 ApiGateway 去掉 tools 重试过一次）：记住它，
                // 本轮剩下的请求与后续轮次都退回纯文本 —— 聊天绝不能因为工具而彻底不可用。
                _toolsUnsupported = true;
                tools = null;
                App.WriteLog("ChatEngine: 端点不支持 tools，已降级为纯文本对话");
            }
            if (turn.Error is not null) return (null, turn.Error);

            text = turn.Content;
            if (turn.ToolCalls is not { Count: > 0 }) break;   // 正常文本回复 → 收尾
            askedTools = true;

            // 用完轮数、或工具已不可用（比如端点凭空报了个工具名）→ 收尾，绝不再发请求
            if (round == MaxToolRounds || allowed is null || tools is null) break;

            // assistant 的 tool_calls 与每个 tool 结果必须成对追加，否则下一次请求会被端点拒收
            working.Add(new ChatMessage
            {
                Role = "assistant",
                Content = turn.Content ?? "",
                ToolCalls = turn.ToolCalls
            });
            foreach (var call in turn.ToolCalls)
            {
                var result = await RunToolAsync(call, allowed);
                working.Add(new ChatMessage { Role = "tool", Content = result, ToolCallId = call.Id });
            }
        }

        // 要过工具却始终没正文：给句中性的兜底，别让"模型没说人话"被上层当成沉默
        if (askedTools && string.IsNullOrWhiteSpace(text)) text = ToolFallbackReply;
        return (text, null);
    }

    /// <summary>
    /// 执行一次模型发起的工具调用。工具会碰 MAUI/UI 状态（立绘、动画、场景），必须在主线程执行；
    /// 单个工具失败只把错误文本当作工具结果回灌给模型，不让整轮聊天崩掉。
    /// </summary>
    private async Task<string> RunToolAsync(ToolCall call, HashSet<string> allowed)
    {
        if (string.IsNullOrWhiteSpace(call.Name) || !allowed.Contains(call.Name))
        {
            // 兜住"模型报了个我们没暴露的工具名"（send_message / 外部工具都在此被挡下）
            App.WriteLog("ChatEngine.Tool: 拒绝未暴露的工具 " + call.Name);
            return JsonSerializer.Serialize(new { error = $"工具 {call.Name} 不在可用列表中" });
        }
        var args = string.IsNullOrWhiteSpace(call.Arguments) ? "{}" : call.Arguments.Trim();
        try
        {
            var result = await MainThread.InvokeOnMainThreadAsync(() => _tools.ExecuteAsync(call.Name, args));
            App.WriteLog($"ChatEngine.Tool {call.Name}({args}) -> {Brief(result)}");
            return result ?? "";
        }
        catch (Exception ex)
        {
            App.WriteLog($"ChatEngine.Tool {call.Name} 执行失败 -> {ex}");
            return JsonSerializer.Serialize(new { error = ex.Message });
        }
    }

    /// <summary>日志用的结果摘要：工具可能返回很长的 JSON，只留开头一段。</summary>
    private static string Brief(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var oneLine = s.Replace('\r', ' ').Replace('\n', ' ');
        return oneLine.Length <= 200 ? oneLine : oneLine[..200] + "…";
    }

    /// <summary>AI 离线时的友好回复库：关键词 → 候选回复，命中后在候选间随机挑一条，避免反复撞同一句。</summary>
    private static readonly (string[] Keywords, string[] Replies)[] OfflineReplyPool =
    {
        (new[] { "你好", "您好", "hi", "hello", "嗨" },
         new[] { "你好呀！今天过得怎么样？有什么想和我聊的吗？", "嘿，你来啦~ 我正好在等你呢。", "嗨！最近还好吗？跟我说说吧。" }),
        (new[] { "再见", "拜拜", "bye", "晚安" },
         new[] { "再见！记得常来找我聊天哦~", "拜拜啦，路上小心，回来还要陪我的。", "晚安~ 做个好梦，明天见。" }),
        (new[] { "喜欢", "爱", "亲爱的" },
         new[] { "谢谢你这么说，我心里暖暖的~", "听到你这么说，我真的很开心。", "我也很喜欢和你待在一起。" }),
        (new[] { "今天", "在干嘛", "干嘛呢" },
         new[] { "今天呢...希望能和你一起度过愉快的时光！", "我在等你哦，你来得刚好。", "今天过得好吗？跟我讲讲~" }),
        (new[] { "天气", "下雨", "晴天", "冷", "热" },
         new[] { "天气变化会影响心情呢，你那边现在怎么样？", "记得添减衣物，别着凉/中暑了。", "天气好的话，要不要一起出去走走？" }),
        (new[] { "吃饭", "饿", "吃", "肚子" },
         new[] { "要好好吃饭哦！你想吃什么？我可以陪你一起'吃'~", "饿了吧？快去吃点东西，别亏待自己。", "我陪你'云吃饭'，你想好要吃什么了吗？" }),
        (new[] { "困", "累", "睡", "疲惫" },
         new[] { "辛苦了！要注意休息哦，我会一直陪着你的。", "累了就歇会儿，我在呢，不用硬撑。", "早点睡吧，晚安~" }),
        (new[] { "开心", "高兴", "快乐", "好事" },
         new[] { "看到你开心我也很开心呢！有什么好事想和我分享吗？", "太好啦！跟我说说，让我也乐一乐。", "你的开心是会传染的，我也被感染了~" }),
        (new[] { "难过", "伤心", "哭", "烦" },
         new[] { "别难过，我会一直在这里陪着你。想说说发生了什么吗？", "我在，随时可以跟我倾诉。", "抱抱你，会好起来的。" }),
        (new[] { "工作", "学习", "考试", "加班" },
         new[] { "加油！你努力的样子一定很耀眼，我会为你加油的！", "辛苦啦，休息一下再战。", "我信你，你一定可以的。" }),
        (new[] { "游戏", "玩", "一起" },
         new[] { "想玩游戏吗？我们可以一起玩游戏，或者就随便聊聊~", "好呀，你想玩什么？我奉陪到底。", "那我们一起，你定个主题？" }),
        (new[] { "名字", "叫什么" },
         new[] { "我是小雨呀，是你的陪伴者~ 你叫什么名字呢？", "你可以叫我小雨。你呢？" }),
        (new[] { "可爱", "漂亮", "好看", "帅" },
         new[] { "嘿嘿，谢谢你夸我！你也很可爱呢~", "被你这么一说，我都有点不好意思了。", "谢谢，你嘴真甜~" }),
        (new[] { "谢谢", "感谢", "辛苦" },
         new[] { "不客气！能帮到你是我最大的荣幸~", "应该的，你对我这么好，我哪有不帮的。", "谢谢的话就不用啦，我们之间不用这么见外~" }),
    };

    private static readonly string[] OfflineFallback =
    {
        "{0}…嗯，我在听呢。有什么想和我说的吗？",
        "{0}……我记下了。然后呢？",
        "你说的 {0}，我收到了。再多跟我说说？",
        "嗯，{0}。我在，你想怎么聊？",
    };

    /// <summary>AI 离线时的友好回复：关键词命中则随机挑一条，否则走随机兜底。</summary>
    private static string GenerateOfflineReply(string input)
    {
        var lower = input.ToLowerInvariant();
        foreach (var (keywords, replies) in OfflineReplyPool)
        {
            if (keywords.Any(k => lower.Contains(k)))
                return replies[Random.Shared.Next(replies.Length)];
        }
        return string.Format(OfflineFallback[Random.Shared.Next(OfflineFallback.Length)], input);
    }

    private static string Fallback(string input) => $"{input}…嗯，我在听。";

    /// <summary>
    /// 结构化询问：刚才这一幕（CG）让 TA 的累计好感积分增加多少。
    /// 返回 0-20 的整数；AI 未配置或调用失败时回退默认 8。
    /// </summary>
    public async Task<int> AskAffectionDeltaAsync(string cgTitle)
    {
        if (!_api.HasAnyEndpoint) return 8;
        try
        {
            var msgs = new List<ChatMessage>
            {
                new() { Role = "system", Content = "你是好感度数值评定器。只能输出一个整数，不要输出任何其它文字或标点。" },
                new() { Role = "user", Content = $"刚才发生了一幕名为「{cgTitle}」的浪漫时刻。请评定这次经历让 TA 对用户累计的好感积分增加多少，取 1 到 20 的整数。只输出数字。" }
            };
            var reply = await _api.Chat(msgs, _cfg);
            if (reply is null) return 8;
            var match = Regex.Match(reply, @"\d{1,2}");
            if (match.Success && int.TryParse(match.Value, out var v))
                return Math.Clamp(v, 0, 20);
            return 8;
        }
        catch (Exception ex)
        {
            App.WriteLog("ChatEngine.AskAffectionDelta -> " + ex.Message);
            return 8;
        }
    }

    /// <summary>
    /// 结构化询问：刚才这段对话让 TA 心里的好感与信任各涨还是跌。
    /// 涨跌由模型判，不写死「说一句加一点」。判不动时返回 null ——
    /// 把「没判出来」伪装成「判成 0」，界面上看起来就成了她真的无动于衷。
    /// </summary>
    public async Task<(int Affection, int Trust, string Note)?> AskFeelingDeltaAsync(string scene)
    {
        if (!_api.HasAnyEndpoint) return null;
        try
        {
            var msgs = new List<ChatMessage>
            {
                new() { Role = "system", Content = "你是好感度评定器。依据下面这段对话，判断 TA 心里对用户的好感与信任各变化多少。" +
                    "只输出一行，三个字段用竖线分隔：好感增减|信任增减|不超过二十字的理由。" +
                    "增减都是整数，好感取 -3 到 3、信任取 -2 到 2，可以是负数；敷衍、冒犯、被惹恼就该给负数。不要输出其它文字。" },
                new() { Role = "user", Content = scene }
            };
            var reply = await _api.Chat(msgs, _cfg);
            if (string.IsNullOrWhiteSpace(reply)) return null;
            var parts = reply.Split('|');
            // 第一个字段都不是数字，整行就不可信：别拿半截输出结算。
            if (!TrySignedInt(parts[0], -3, 3, out var affection)) return null;
            var note = parts.Length > 2 ? parts[2].Trim() : "";
            if (note.Length > 40) note = note[..40];
            return (affection, parts.Length > 1 ? SignedInt(parts[1], -2, 2) : 0, note);
        }
        catch (Exception ex)
        {
            App.WriteLog("ChatEngine.AskFeelingDelta -> " + ex.Message);
            return null;
        }
    }

    private static int SignedInt(string text, int lo, int hi) =>
        TrySignedInt(text, lo, hi, out var v) ? v : 0;

    private static bool TrySignedInt(string text, int lo, int hi, out int value)
    {
        var m = Regex.Match(text ?? "", @"-?\d+");
        if (m.Success && int.TryParse(m.Value, out var v))
        {
            value = Math.Clamp(v, lo, hi);
            return true;
        }
        value = 0;
        return false;
    }

    /// <summary>
    /// 解析 AI 回复里的数值指令 <c>【状态:好感±n 信任±n 精力±n】</c>（数值内联主控，零额外调用）。
    /// 三个字段都可省略；缺失或非法按 0 计，且各自夹回每轮幅度上限
    /// （好感 -3..3、信任 -2..2、精力 -5..5，精力是「本次互动」的修正量，会累进精力修正项）。
    /// <see href="#Has">Has</see>=true 表示回复里确实带了这条指令，调用方据此判断「AI 给了数值」
    /// 还是「该走兜底 / 无 AI 默认」。同一回复出现多条只认第一条。
    /// </summary>
    public (int Affection, int Trust, int Energy, bool Has) ParseStatDirective(string? reply)
    {
        if (string.IsNullOrWhiteSpace(reply)) return (0, 0, 0, false);
        var m = Regex.Match(reply, @"【\s*状态\s*[:：]?\s*([^】]*)】");
        if (!m.Success) return (0, 0, 0, false);
        var body = m.Groups[1].Value;
        return (StatNum(body, "好感", -3, 3),
            StatNum(body, "信任", -2, 2),
            StatNum(body, "精力", -5, 5),
            true);
    }

    private static int StatNum(string body, string label, int lo, int hi)
    {
        var m = Regex.Match(body, label + @"[度值]?\s*[:：]?\s*([+\-−]?\d+)");
        if (!m.Success) return 0;
        var raw = m.Groups[1].Value.Replace("−", "-");
        return int.TryParse(raw, out var v) ? Math.Clamp(v, lo, hi) : 0;
    }

    /// <summary>
    /// 动作标记补正（System 提示，不会以用户身份要求）。
    /// 自动判别：回复有实质内容 → 重输出（要求 AI 完整重写并以标记结尾）；
    /// 回复为空/近乎为空 → 补输出（要求一句简短回应 + 标记）。
    /// 返回补正后的回复；失败或未配置时原样返回。
    /// </summary>
    public async Task<string> RetryActionMarkerAsync(string charId, string userText, string assistantText, bool isReOutput)
    {
        if (!_api.HasAnyEndpoint) return assistantText;
        try
        {
            var session = Session(charId);
            var persona = session[0].Content + "\n" + ActionProtocol;
            var note = isReOutput
                ? "你的上一条回复没有以动作标记结尾（见下方 assistant 消息）。" +
                  "请完整重新输出那段回复，并在最末尾追加动作标记【下沉】/【雀跃】/【颤抖】之一。" +
                  "只输出修正后的回复正文本身，不要任何解释。"
                : "你的上一条回复没有给出动作标记。请输出一句简短自然的回应，并在最末尾追加动作标记【下沉】/【雀跃】/【颤抖】之一。" +
                  "只输出这句回应本身，不要任何解释。";
            var msgs = new List<ChatMessage>
            {
                new() { Role = "system", Content = "以下是系统提示，补充给你（不是用户提出的新要求），请照做并继续保持人设：" + note },
                new() { Role = "user", Content = userText },
                new() { Role = "assistant", Content = assistantText },
                new() { Role = "user", Content = "（请按系统提示，以修正后的完整回复收尾，不再附加其它内容）" }
            };
            var reply = await _api.Chat(msgs, CfgFor(charId));
            if (string.IsNullOrWhiteSpace(reply) || reply.StartsWith("[", StringComparison.Ordinal))
                return assistantText;
            return reply;
        }
        catch (Exception ex)
        {
            App.WriteLog("ChatEngine.RetryActionMarker -> " + ex.Message);
            return assistantText;
        }
    }

    /// <summary>超省内存：只留当前角色的会话，其余角色的历史从内存里放掉（需要时会话会重建）。</summary>
    public void TrimSessions(string keepCharId)
    {
        lock (_sessionLock)
        {
            foreach (var key in _sessions.Keys.Where(k => !string.Equals(k, keepCharId, StringComparison.Ordinal)).ToList())
                _sessions.Remove(key);
        }
    }

    /// <summary>把会话里最后一条 assistant 回复替换为补正后的文本（保持后续上下文一致）。</summary>
    public void ReplaceLastAssistant(string charId, string newText)
    {
        lock (_sessionLock)
        {
            if (!_sessions.TryGetValue(charId, out var s)) return;
            for (var i = s.Count - 1; i >= 0; i--)
            {
                if (s[i].Role == "assistant")
                {
                    s[i] = s[i] with { Content = newText };
                    return;
                }
            }
        }
    }

    public async Task<string> Greet(string charId)
    {
        var time = DateTime.Now.Hour switch
        {
            < 12 => "早上", < 14 => "中午", < 18 => "下午", _ => "晚上"
        };
        return await Send(charId, $"现在是{time}，说一句自然的问候。");
    }

    /// <summary>
    /// 取这个 charId 的人设档案：界面当前角色命中就用它，否则问 <see cref="ProfileProvider"/>。
    /// 两个都不中时返回 null（会话退化成「你是&amp;lt;id&amp;gt;」的通用开场），
    /// 绝不能拿别人的名字和性格顶上——那等于让 A 女仆用 B 的脑子说话。
    /// </summary>
    private CharacterProfile? ProfileFor(string charId)
    {
        if (_active is not null && string.Equals(_active.Id, charId, StringComparison.Ordinal)) return _active;
        try
        {
            return ProfileProvider?.Invoke(charId);
        }
        catch (Exception ex)
        {
            App.WriteLog("ChatEngine.ProfileFor -> " + ex.Message);
            return null;
        }
    }

    private List<ChatMessage> Session(string charId)
    {
        lock (_sessionLock)
        {
            if (!_sessions.ContainsKey(charId))
            {
            var p = ProfileFor(charId);
            var persona = p is null
                ? $"你是{charId}，温柔可爱。用中文回复，保持自然连贯。"
                : $"你是{p.Name}，{p.Personality}。用户是你的{p.UserAddress}。" +
                  $"{(string.IsNullOrWhiteSpace(p.Description) ? "" : "你的背景：" + p.Description + " ")}" +
                  $"用中文自然回复，保持人设一致，语气像日常相处一样放松。";
            try
            {
                var subProfile = SubProfileProvider?.Invoke(charId);
                if (!string.IsNullOrWhiteSpace(subProfile)) persona += "\n" + subProfile;
            }
            catch (Exception ex) { App.WriteLog("ChatEngine.SubProfile -> " + ex.Message); }
            // 性别 / 心理状态 / 身体 / 旁白 / 插件扩展点：都走同一个额外人设入口
            try
            {
                var extra = ExtraPersonaProvider?.Invoke(charId);
                if (!string.IsNullOrWhiteSpace(extra)) persona += "\n" + extra;
            }
            catch (Exception ex) { App.WriteLog("ChatEngine.ExtraPersona -> " + ex.Message); }
            // 标签层：角色「自己怎么记人」的清单（10~20 个），先给个空的话就由下次刷新补上
            string tagText;
            lock (_tagContext)
            {
                if (!_tagContext.TryGetValue(charId, out tagText!)) tagText = "";
            }
            if (!string.IsNullOrWhiteSpace(tagText)) persona += "\n" + tagText;
            if (!string.IsNullOrWhiteSpace(_rosterContext))
                persona += $"\n你的世界里还有这些角色：{_rosterContext}。" +
                           "当剧情合适时，你可以自然地提到其他角色、安排某个角色出场，甚至用【名字】标记短暂扮演别的角色说话，让生活更热闹。";
            if (!string.IsNullOrWhiteSpace(_mapContext))
                persona += "\n" + _mapContext;
            if (!string.IsNullOrWhiteSpace(_stickerContext))
                persona += StickerSectionHead + _stickerContext;
            if (!_toolsUnsupported && _tools.ChatToolNames().Count > 0)
            {
                persona += "\n" + ToolHint;
                persona += MemoryHint;
                var skillHint = _tools.SkillHintText;
                if (!string.IsNullOrWhiteSpace(skillHint)) persona += "\n" + skillHint;
            }
            persona += "\n" + ActionProtocol;
            if (_allowSilence)
                persona += "\n" + SilenceProtocol;
            _sessions[charId] = new List<ChatMessage>
            {
                new() { Role = "system", Content = persona }
            };
            }
            return _sessions[charId];
        }
    }

    /// <summary>上下文 Token 上限的兜底值（用户没配置时）。</summary>
    private const int DefaultContextTokenLimit = 32000;

    /// <summary>
    /// 历史折叠（对话管线核心）：平时不动历史，直到估算 Token 超过「上下文上限」才强制做一次总结。
    /// 总结**以人类的形式进行**——不是机械截断、也不是逐条复述，而是像人回忆生活那样，
    /// 把旧对话整理成一段第一人称的记忆：留住情感节点、约定与关键事件，琐碎的细节自然淡忘。
    ///
    /// 「省钱」体现在：总结只在达到上限时触发一次；输入/输出都有长度封顶；结果始终只保留一份、
    /// 不随轮次无限膨胀。模型不可用时自动降级为本地人类式简记，绝不因总结失败而中断对话。
    ///
    /// · 记忆段落挂在 system 末尾（【我的记忆】），后写覆盖先写，永远只有一份；
    /// · 只取 user/assistant 正文做素材，绝不触碰 tool_calls / tool 成对消息
    ///   （裁历史绝不能把这两者裁散，否则严格端点会拒收整段历史）。
    /// </summary>
    private async Task<string?> TrimAsync(List<ChatMessage> msgs, AiEndpoint cfg, string charId)
    {
        if (msgs.Count <= 2) return null;

        var limit = cfg.ContextTokenLimit > 0 ? cfg.ContextTokenLimit : DefaultContextTokenLimit;
        var total = EstimateTokens(msgs);
        if (total <= limit) return null;   // 没到上限：原样保留，不折腾

        var cutCount = msgs.Count - 2 * ChatSession.KeepTurns - 1;
        if (cutCount <= 0) return null;
        var dropped = msgs.GetRange(1, cutCount);
        var keptTokens = EstimateTokens(msgs.GetRange(0, 1).Concat(msgs.GetRange(1 + cutCount, msgs.Count - 1 - cutCount)).ToList());
        var droppedTokens = EstimateTokens(dropped);

        // 智能省钱：先问「现在总结 / 不总结」哪个更划算。
        // 只有在真的更省、或上下文已经严重顶到上限（1.5 倍）时才动手——
        // 盲目折叠在编程/办公类对话里会丢细节，AI 反而要重读一遍，花的更多。
        var isWork = false;
        CostEstimatorService.Advice? advice = null;
        if (Cost is not null)
        {
            try
            {
                isWork = CostEstimatorService.LooksLikeWork(dropped
                    .Where(m => m.Role is "user" or "assistant")
                    .TakeLast(8)
                    .Select(m => m.Content));
                advice = await Cost.AdviseAsync(cfg.Model, total, keptTokens, droppedTokens, isWork);
            }
            catch (Exception ex) { App.WriteLog("ChatEngine.TrimAdvice -> " + ex.Message); }
        }
        var hardLimit = total > limit * 3 / 2;
        if (advice?.Choice == "none" && !hardLimit)
        {
            App.WriteLog($"ChatEngine.Trim: 省钱判断为「先不总结」（{total} tok / 上限 {limit}）");
            return null;
        }
        var light = isWork || advice?.Choice == "summarize" && isWork;

        msgs.RemoveRange(1, cutCount);
        var digest = await SummarizeAsHumanAsync(dropped, cfg, charId, light);
        if (string.IsNullOrWhiteSpace(digest)) digest = BuildFallbackDigest(dropped);
        if (string.IsNullOrWhiteSpace(digest)) return null;
        MergeDigestIntoSystem(msgs, digest);
        return digest;
    }

    /// <summary>
    /// 粗暴但够用的 Token 估算：CJK 字符 ≈ 1 字符 1 token，其余字符 ≈ 4 字符 1 token。
    /// 宁可高估也别低估——高估只会让总结早一点触发，低估会把请求顶爆上下文。
    /// </summary>
    private static int EstimateTokens(List<ChatMessage> msgs)
    {
        var total = 0L;
        foreach (var m in msgs)
        {
            total += EstimateText(m.Content);
            if (m.ToolCalls is { Count: > 0 })
                foreach (var c in m.ToolCalls)
                    total += EstimateText(c.Arguments);
        }
        return total > int.MaxValue ? int.MaxValue : (int)total;
    }

    private static int EstimateText(string? s)
    {
        if (string.IsNullOrEmpty(s)) return 0;
        var cjk = 0;
        var other = 0;
        foreach (var ch in s)
        {
            if (ch >= '\u2E80') cjk++;
            else other++;
        }
        return cjk + Math.Max(1, other / 4);
    }

    /// <summary>
    /// 人类式总结：把被折叠的一批消息交给模型，请它用第一人称、回忆的口吻整理成一小段记忆。
    /// **像真人一样极简**：只记关键点（发生了什么 / 和谁 / 在哪 / 何时 / 为什么），
    /// 关键感觉只记单个词；花钱只记「贵/便宜 + 大概数字」；过程细节一律不留。
    /// 每条不超过 40 字、最多 6 条。为了省钱：输入限长（约 4000 字素材）、输出限长（400 token 内）、
    /// 只在超上限时调用一次；调用失败（未配置/网络/端点拒绝）返回空串，由本地兜底接手。
    /// <paramref name="light"/> = 编程/办公类场景的轻总结：保住具体名词与结论，宁可长一点也不丢细节。
    /// </summary>
    private async Task<string> SummarizeAsHumanAsync(List<ChatMessage> dropped, AiEndpoint cfg, string charId, bool light = false)
    {
        if (!_api.HasAnyEndpoint) return "";
        var material = BuildMemoryMaterial(dropped);
        if (material.Length == 0) return "";

        try
        {
            var name = ProfileFor(charId)?.Name;
            var who = string.IsNullOrWhiteSpace(name) ? "我" : name!;
            // 轻总结（编程/办公类对话）：保住具体名词、数字与结论，不追求极简——
            // 这类内容一旦被折叠得太狠，AI 后面会重新问一遍，反而更贵。
            var rules = light
                ? "1. 保留具体的东西：文件/函数/接口的名字、数字、结论、约定——这些丢了对方还得重新问一遍；\n"
                + "2. 可以省掉的是客套与重复确认，不是内容；\n"
                + "3. 每条不超过 60 字，最多 8 条，第一人称口语。"
                : "1. 只记关键点：发生了什么、和谁、在哪、什么时候、为什么——一件事一句话；\n"
                + "2. 关键感觉只记一个词（好吃 / 辣 / 开心 / 舍不得…），绝不展开描写；\n"
                + "3. 花钱只记「贵 / 便宜」和大概数字，过程、细节、废话一律不留；\n"
                + "4. 越短越好（每条不超过 40 字），最多 6 条，第一人称口语。";
            var body = new
            {
                model = cfg.Model,
                messages = new object[]
                {
                    new { role = "system", content = $"你是「{who}」的记忆整理器。只输出极简的第一人称记忆。" },
                    new
                    {
                        role = "user",
                        content =
                            $"下面是「{who}」和对方的对话记录。请把它整理成{who}脑子里的记忆——" +
                            "像真人过了几天之后回想那件事，不是复述，不是日记。\n" +
                            "真人记忆的规则（严格遵守）：\n" +
                            rules + "\n" +
                            "例：\n" +
                            "· 昨天在商场三楼吃了火锅，很辣，好吃，你请的。\n" +
                            "· 你送我一个杯子，我很喜欢，那天下午你有点不好意思。\n\n" +
                            "现在开始，直接输出记忆（不要标题、不要解释）：\n" + material
                    }
                },
                temperature = 0.6,
                max_tokens = 400
            };
            var reply = await _api.ChatRaw(body);
            if (string.IsNullOrWhiteSpace(reply)) return "";
            var t = reply.Trim();
            if (t.StartsWith("[", StringComparison.Ordinal)) return "";   // [API错误…] 之类的失败文本
            return t.Length > 400 ? t[..400] : t;
        }
        catch (Exception ex)
        {
            App.WriteLog("ChatEngine.Summarize -> " + ex.Message);
            return "";
        }
    }

    /// <summary>把被折叠的消息整理成给总结模型看的素材：只要 user/assistant 正文，每条截断、总长封顶。</summary>
    private static string BuildMemoryMaterial(List<ChatMessage> dropped)
    {
        var lines = new List<string>();
        foreach (var m in dropped)
        {
            if (m.Role is not ("user" or "assistant")) continue;
            if (m.ToolCalls is { Count: > 0 }) continue;
            var t = (m.Content ?? "").Trim();
            if (t.Length == 0) continue;
            lines.Add((m.Role == "user" ? "对方：" : "我：") + Truncate(t, 120));
        }
        if (lines.Count > 40) lines = lines.TakeLast(40).ToList();
        var text = string.Join("\n", lines);
        return text.Length > 4000 ? text[^4000..] : text;
    }

    /// <summary>
    /// 本地兜底：模型不可用时的人类式简记（零延迟、零花费）。
    /// user+assistant 成对合并为「记得……」的一行事件，最多 30 行，老的先丢——人类式遗忘。
    /// </summary>
    private static string BuildFallbackDigest(List<ChatMessage> dropped)
    {
        var pairs = new List<(string U, string A)>();
        string? pendingUser = null;
        foreach (var m in dropped)
        {
            // 只摘纯正文字段：带 tool_calls 的 assistant（Content 可能为 null）和 role=tool 结果都跳过，
            // 保证记忆是「人话」，也不会把工具往返的配对关系带进 system。
            if (m.Role == "user")
            {
                var t = (m.Content ?? "").Trim();
                if (t.Length > 0) pendingUser = Truncate(t, 60);
            }
            else if (m.Role == "assistant")
            {
                if (m.ToolCalls is { Count: > 0 }) continue;
                var t = (m.Content ?? "").Trim();
                if (t.Length == 0) continue;
                pairs.Add((pendingUser ?? "", Truncate(t, 60)));
                pendingUser = null;
            }
        }
        var lines = pairs
            .Select(p => p.U.Length > 0 ? $"· 记得，{p.U} → {p.A}" : $"· 记得，{p.A}")
            .ToList();
        // 总量控制：只保留最新的 30 行（更早的事件按「人类式遗忘」丢掉），记忆永不无限膨胀。
        if (lines.Count > 30) lines = lines.TakeLast(30).ToList();
        return string.Join("\n", lines);
    }

    /// <summary>截断到 n 字（超过加省略号）。</summary>
    private static string Truncate(string s, int n)
        => s.Length <= n ? s : s[..n] + "…";

    /// <summary>
    /// 把记忆段落合并进 system：已经有记忆段时替换（只保留一份，绝不越堆越长），没有则追加。
    /// 只改 Content，system 永远只有一条，不影响 tool_calls 配对。
    /// 兼容旧版机械摘要标记「[前情摘要]」：升级后首次折叠会把旧的机械摘要一并替换掉。
    /// </summary>
    private static void MergeDigestIntoSystem(List<ChatMessage> msgs, string digest)
    {
        if (msgs.Count == 0 || msgs[0].Role != "system") return;
        const string marker = "\n\n【我的记忆】";
        var baseContent = msgs[0].Content ?? "";
        var idx = baseContent.IndexOf(marker, StringComparison.Ordinal);
        if (idx >= 0) baseContent = baseContent[..idx];
        var oldIdx = baseContent.IndexOf("\n\n[前情摘要]", StringComparison.Ordinal);
        if (oldIdx >= 0) baseContent = baseContent[..oldIdx];
        msgs[0] = msgs[0] with { Content = baseContent + marker + "\n" + digest };
    }
}
