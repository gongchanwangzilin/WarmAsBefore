using System.Text.RegularExpressions;
using WarmAsBefore.Models;
using WarmAsBefore.Modules.ApiManager;
using WarmAsBefore.Services;

namespace WarmAsBefore.Modules.AiChat;

public sealed class ChatEngine
{
    private readonly ApiGateway _api;
    private readonly MemoryVault _memory;
    private readonly Dictionary<string, List<ChatMessage>> _sessions = new();
    private AiEndpoint _cfg = new();
    private CharacterProfile? _active;
    private string _rosterContext = "";
    private string _mapContext = "";

    public ChatEngine(ApiGateway api, MemoryVault memory)
    {
        _api = api;
        _memory = memory;
    }

    /// <summary>告诉 AI 当前陪伴的主角是谁（名字/性格/称呼/背景设定）。</summary>
    public void ConfigureCharacter(CharacterProfile profile) => _active = profile;

    /// <summary>注入角色库：世界中还有其他角色时，AI 可在剧情里自由调用/扮演她们。</summary>
    public void SetRoster(string roster) => _rosterContext = roster ?? "";

    /// <summary>注入地图上下文：告诉 AI 当前可去的场景与【移动:场景名】移动协议。</summary>
    public void SetMapContext(string context) => _mapContext = context ?? "";

    public void Configure(AiEndpoint cfg)
    {
        _cfg = cfg;
        _api.Configure(cfg);
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

        // API 已配置：调用 API
        try
        {
            var reply = await _api.Chat(session, _cfg);
            // ApiGateway 现在返回详细错误信息（不以null结尾）
            if (reply is null)
            {
                reply = "（AI 暂时无法回应，请检查 API 配置或网络连接）";
            }
            else if (reply.StartsWith("[", StringComparison.Ordinal))
            {
                // API 返回了错误信息，直接显示给用户（不存会话历史）
                App.WriteLog("ChatEngine: API error: " + reply);
                return reply;
            }
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
            session.Add(new ChatMessage { Role = "assistant", Content = errorMsg });
            Trim(session);
            return errorMsg;
        }
    }

    /// <summary>AI 离线时的友好回复：根据用户消息生成自然的回应。</summary>
    private static string GenerateOfflineReply(string input)
    {
        // 根据输入内容生成不同回复
        var lower = input.ToLowerInvariant();
        if (lower.Contains("你好") || lower.Contains("您好") || lower.Contains("hi") || lower.Contains("hello"))
            return "你好呀！今天过得怎么样？有什么想和我聊的吗？";
        if (lower.Contains("再见") || lower.Contains("拜拜") || lower.Contains("bye"))
            return "再见！记得常来找我聊天哦~";
        if (lower.Contains("喜欢") || lower.Contains("爱"))
            return "谢谢你这么说，我心里暖暖的~";
        if (lower.Contains("今天"))
            return "今天呢...希望能和你一起度过愉快的时光！";
        if (lower.Contains("天气") || lower.Contains("下雨") || lower.Contains("晴天"))
            return "天气变化会影响心情呢，你那边现在怎么样？";
        if (lower.Contains("吃饭") || lower.Contains("饿") || lower.Contains("吃"))
            return "要好好吃饭哦！你想吃什么？我可以陪你一起 '吃'~";
        if (lower.Contains("困") || lower.Contains("累") || lower.Contains("睡"))
            return "辛苦了！要注意休息哦，我会一直陪着你的。";
        if (lower.Contains("开心") || lower.Contains("高兴") || lower.Contains("快乐"))
            return "看到你开心我也很开心呢！有什么好事想和我分享吗？";
        if (lower.Contains("难过") || lower.Contains("伤心") || lower.Contains("哭"))
            return "别难过，我会一直在这里陪着你。想说说发生了什么吗？";
        if (lower.Contains("工作") || lower.Contains("学习") || lower.Contains("考试"))
            return "加油！你努力的样子一定很耀眼，我会为你加油的！";
        if (lower.Contains("游戏") || lower.Contains("玩"))
            return "想玩游戏吗？我们可以一起玩游戏，或者就随便聊聊~";
        if (lower.Contains("名字") || lower.Contains("叫什么"))
            return "我是小雨呀，是你的陪伴者~ 你叫什么名字呢？";
        if (lower.Contains("可爱") || lower.Contains("漂亮") || lower.Contains("好看"))
            return "嘿嘿，谢谢你夸我！你也很可爱呢~";
        if (lower.Contains("谢谢"))
            return "不客气！能帮到你是我最大的荣幸~";
        if (lower.Contains("喜欢"))
            return "我也很喜欢和你在一起呢~";
        return $"{input}…嗯，我在听呢。有什么想和我说的吗？";
    }

    private static string Fallback(string input) => $"{input}…嗯，我在听。";

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
    private List<MemoryEntry> _cache = new();

    public MemoryVault(StorageProvider store)
    {
        _store = store;
        // 启动即载入全部记忆（回忆录可查询所有历史对话），失败时保持空缓存
        _ = LoadAllAsync();
    }

    private async Task LoadAllAsync()
    {
        try
        {
            var saved = await _store.Load<List<MemoryEntry>>("memories");
            if (saved is not null && saved.Count > 0) _cache = saved;
        }
        catch (Exception ex)
        {
            App.WriteLog("MemoryVault.LoadAll -> " + ex);
        }
    }

    public async Task Store(MemoryEntry entry)
    {
        _cache.Add(entry);
        await _store.Save("memories", _cache);
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

    public Task<List<MemoryEntry>> Recent(string charId, int n = 10) =>
        Task.FromResult(_cache.Where(m => m.CharacterId == charId)
            .OrderByDescending(m => m.At).Take(n).ToList());

    /// <summary>某个角色的全部记忆（可限定类别，如 affection / dialogue）。</summary>
    public Task<List<MemoryEntry>> All(string charId, string? category = null) =>
        Task.FromResult(_cache.Where(m => m.CharacterId == charId
                && (category is null || m.Category == category))
            .OrderByDescending(m => m.At).ToList());

    public Task<List<MemoryEntry>> Search(string charId, string query)
    {
        var hits = _cache.Where(m => m.CharacterId == charId).AsEnumerable();
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