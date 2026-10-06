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
    private AiEndpoint _cfg = new();
    private CharacterProfile? _active;
    private string _rosterContext = "";
    private string _mapContext = "";
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

    /// <summary>注入角色库：世界中还有其他角色时，AI 可在剧情里自由调用/扮演她们。</summary>
    public void SetRoster(string roster) => _rosterContext = roster ?? "";

    /// <summary>注入地图上下文：告诉 AI 当前可去的场景与【移动:场景名】移动协议。</summary>
    public void SetMapContext(string context) => _mapContext = context ?? "";

    /// <summary>允许沉默：true 时 AI 可选择完全不回复（输出空内容），系统据此停止该回合。</summary>
    public void SetAllowSilence(bool allow) => _allowSilence = allow;

    public void Configure(AiEndpoint cfg)
    {
        _cfg = cfg;
        _api.Configure(cfg);
        // 端点/模型可能整个换了（设置页保存即走到这里）：重新给它一次带工具的机会
        _toolsUnsupported = false;
    }

    /// <summary>动态状态上下文（buff/标记）：每次 Send 前调用，返回值拼进用户消息前缀注入 AI。
    /// 由外部（如 ShopService）设置，避免 ChatEngine 反向依赖商店系统。</summary>
    public Func<string>? BuffContextProvider { get; set; }

    /// <summary>每次 Send 完成后回调（用于 tick buff 剩余轮数等）。</summary>
    public Action? AfterSend { get; set; }

    public async Task<string> Send(string charId, string text)
    {
        var session = Session(charId);
        var recent = await _memory.Recent(charId, Math.Max(1, _cfg.MemoryTurns));
        var ctx = string.Join("\n", recent.Select(m => m.Content));

        // 注入 buff/标记上下文（动态，随每次对话变化；拼在用户消息前，AI 可见但不算用户说的话）
        var buff = "";
        try { buff = BuffContextProvider?.Invoke() ?? ""; }
        catch (Exception ex) { App.WriteLog("ChatEngine.BuffContext -> " + ex.Message); }
        var payload = string.IsNullOrWhiteSpace(buff) ? text : $"{buff}\n{text}";

        session.Add(new ChatMessage { Role = "user", Content = $"[{ctx}]\n{payload}" });

        // 检查 API 是否已配置
        if (string.IsNullOrWhiteSpace(_cfg.Key))
        {
            // AI 未配置：返回友好的提示
            var offlineReply = GenerateOfflineReply(text);
            session.Add(new ChatMessage { Role = "assistant", Content = offlineReply });
            Trim(session);
            return offlineReply;
        }

        // API 已配置：调用 API（可能带工具往返）
        try
        {
            var (reply, error) = await RunTurnAsync(session);
            if (error is not null)
            {
                // API 返回了错误信息，直接显示给用户（不存会话历史）
                App.WriteLog("ChatEngine: API error: " + error);
                return error;
            }
            // 正文为 null（而不是空串）＝模型没给正文，沿用原来的提示语；空串是"沉默"，必须原样保留
            reply ??= "（AI 暂时无法回应，请检查 API 配置或网络连接）";
            session.Add(new ChatMessage { Role = "assistant", Content = reply });
            Trim(session);

            await _memory.Store(new MemoryEntry
            {
                CharacterId = charId,
                Content = $"{text} → {reply}",
                Category = "dialogue"
            });
            try { AfterSend?.Invoke(); }
            catch (Exception ex) { App.WriteLog("ChatEngine.AfterSend -> " + ex.Message); }
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
    private async Task<(string? Text, string? Error)> RunTurnAsync(List<ChatMessage> session)
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
            var turn = await _api.ChatWithToolsAsync(working, tools, _cfg);
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
    /// 结构化询问：刚才这一幕（CG）让女主的累计好感积分增加多少。
    /// 返回 0-20 的整数；AI 未配置或调用失败时回退默认 8。
    /// </summary>
    public async Task<int> AskAffectionDeltaAsync(string cgTitle)
    {
        if (string.IsNullOrWhiteSpace(_cfg.Key)) return 8;
        try
        {
            var msgs = new List<ChatMessage>
            {
                new() { Role = "system", Content = "你是好感度数值评定器。只能输出一个整数，不要输出任何其它文字或标点。" },
                new() { Role = "user", Content = $"刚才发生了一幕名为「{cgTitle}」的浪漫时刻。请评定这次经历让女主对用户的累计好感积分增加多少，取 1 到 20 的整数。只输出数字。" }
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
    /// 动作标记补正（System 提示，不会以用户身份要求）。
    /// 自动判别：回复有实质内容 → 重输出（要求 AI 完整重写并以标记结尾）；
    /// 回复为空/近乎为空 → 补输出（要求一句简短回应 + 标记）。
    /// 返回补正后的回复；失败或未配置时原样返回。
    /// </summary>
    public async Task<string> RetryActionMarkerAsync(string charId, string userText, string assistantText, bool isReOutput)
    {
        if (string.IsNullOrWhiteSpace(_cfg.Key)) return assistantText;
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
            var reply = await _api.Chat(msgs, _cfg);
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

    /// <summary>把会话里最后一条 assistant 回复替换为补正后的文本（保持后续上下文一致）。</summary>
    public void ReplaceLastAssistant(string charId, string newText)
    {
        if (_sessions.TryGetValue(charId, out var s))
        {
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

    private List<ChatMessage> Session(string charId)
    {
        if (!_sessions.ContainsKey(charId))
        {
            var p = _active;
            var persona = p is null
                ? $"你是{charId}，温柔可爱。用中文回复，保持自然连贯。"
                : $"你是{p.Name}，{p.Personality}。用户是你的{p.UserAddress}。" +
                  $"{(string.IsNullOrWhiteSpace(p.Description) ? "" : "你的背景：" + p.Description + " ")}" +
                  $"用中文自然回复，保持人设一致，语气像日常相处一样放松。";
            if (!string.IsNullOrWhiteSpace(_rosterContext))
                persona += $"\n你的世界里还有这些角色：{_rosterContext}。" +
                           "当剧情合适时，你可以自然地提到她们、让她们出场，甚至用【名字】标记来短暂扮演她们说话，让生活更热闹。";
            if (!string.IsNullOrWhiteSpace(_mapContext))
                persona += "\n" + _mapContext;
            if (!_toolsUnsupported && _tools.ChatToolNames().Count > 0)
                persona += "\n" + ToolHint;
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

    private static void Trim(List<ChatMessage> msgs)
    {
        if (msgs.Count > 2 * ChatSession.KeepTurns + 1)
            msgs.RemoveRange(1, msgs.Count - 2 * ChatSession.KeepTurns - 1);
    }
}

public sealed class MemoryVault
{
    private readonly StorageProvider _store;
    private readonly object _gate = new();
    private List<MemoryEntry> _cache = new();
    private bool _loaded;

    public MemoryVault(StorageProvider store) => _store = store;

    /// <summary>惰性加载：首次真正读写前 await，避免构造函数 fire-and-forget 造成「旧记忆还没读进来就被覆盖」的竞态。</summary>
    private async Task EnsureLoadedAsync()
    {
        lock (_gate)
        {
            if (_loaded) return;
            _loaded = true;   // 先置位，后续并发调用直接走快路径
        }
        try
        {
            var saved = await _store.Load<List<MemoryEntry>>("memories");
            lock (_gate)
            {
                if (saved is not null && saved.Count > 0) _cache = saved;
            }
        }
        catch (Exception ex)
        {
            App.WriteLog("MemoryVault.LoadAll -> " + ex);
        }
    }

    public async Task Store(MemoryEntry entry)
    {
        await EnsureLoadedAsync();
        List<MemoryEntry> snapshot;
        lock (_gate)
        {
            _cache.Add(entry);
            // 快照整份列表再序列化，避免序列化到一半列表被另一线程修改
            snapshot = new List<MemoryEntry>(_cache);
        }
        await _store.Save("memories", snapshot);
    }

    /// <summary>记录好感提升瞬间（回忆录的「全部好感时刻」来源，随记忆持久化）。</summary>
    public Task LogAffection(string charId, int delta, string reason, string? imagePath = null) =>
        Store(new MemoryEntry
        {
            CharacterId = charId,
            Content = $"{delta:+0;-0;0} 好感（{reason}）",
            Category = "affection",
            Keywords = reason,
            Weight = delta,
            ImagePath = imagePath
        });

    public Task<List<MemoryEntry>> Recent(string charId, int n = 10)
    {
        _ = EnsureLoadedAsync();
        lock (_gate)
            return Task.FromResult(_cache.Where(m => m.CharacterId == charId)
                .OrderByDescending(m => m.At).Take(n).ToList());
    }

    /// <summary>某个角色的全部记忆（可限定类别，如 affection / dialogue）。</summary>
    public Task<List<MemoryEntry>> All(string charId, string? category = null)
    {
        _ = EnsureLoadedAsync();
        lock (_gate)
            return Task.FromResult(_cache.Where(m => m.CharacterId == charId
                    && (category is null || m.Category == category))
                .OrderByDescending(m => m.At).ToList());
    }

    public Task<List<MemoryEntry>> Search(string charId, string query)
    {
        _ = EnsureLoadedAsync();
        IEnumerable<MemoryEntry> hits;
        lock (_gate)
        {
            hits = _cache.Where(m => m.CharacterId == charId).AsEnumerable();
        }
        if (!string.IsNullOrWhiteSpace(query))
        {
            try
            {
                var re = new Regex(query, RegexOptions.IgnoreCase | RegexOptions.Compiled);
                hits = hits.Where(m => re.IsMatch(m.Content) || (m.Keywords is not null && re.IsMatch(m.Keywords)));
            }
            catch
            {
                hits = hits.Where(m => m.Content.Contains(query, StringComparison.OrdinalIgnoreCase));
            }
        }
        return Task.FromResult(hits.OrderByDescending(m => m.Weight).ThenByDescending(m => m.At).ToList());
    }

    public async Task<List<DiaryNote>> Diary(string charId)
    {
        var all = await _store.Load<List<DiaryNote>>($"diary_{charId}");
        return all ?? new();
    }

    public async Task WriteDiary(string charId, string content, string mood)
    {
        var d = await Diary(charId);
        d.Add(new DiaryNote { CharacterId = charId, Content = content, Mood = mood });
        await _store.Save($"diary_{charId}", d);
    }
}