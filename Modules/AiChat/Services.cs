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
        var reply = await _api.Chat(session, _cfg) ?? Fallback(text);
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

    private static string Fallback(string input) => $"{input}…嗯，我在听。";
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