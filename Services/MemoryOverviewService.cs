using WarmAsBefore.Models;
using WarmAsBefore.Modules.AiChat;

namespace WarmAsBefore.Services;

/// <summary>
/// 记忆总览：把五层各自的规模与「最近发生了什么」汇总到一处。
///
/// 存在的意义是防止分层只停留在文档里——界面与提示词都通过这里了解各层现状，
/// 哪一层没在长（比如关联网一直是 0 条）一眼可见。
/// </summary>
public sealed class MemoryOverviewService
{
    private readonly ChatEngine _chat;
    private readonly MemoryVault _vault;
    private readonly HumanMemoryService _human;
    private readonly SemanticNetworkService _semantic;
    private readonly MemoryTagService _tags;
    private readonly SettingsManager _settings;
    private readonly GameEngine _engine;

    public MemoryOverviewService(ChatEngine chat, MemoryVault vault, HumanMemoryService human,
        SemanticNetworkService semantic, MemoryTagService tags, SettingsManager settings, GameEngine engine)
    {
        _chat = chat;
        _vault = vault;
        _human = human;
        _semantic = semantic;
        _tags = tags;
        _settings = settings;
        _engine = engine;
    }

    /// <summary>单层的规模与说明。</summary>
    public sealed record LayerStat(MemoryLayer Layer, string Name, string Describe, int Count, string Unit, string Detail);

    /// <summary>取各层现状。<paramref name="charId"/> 为空时用当前存档角色。</summary>
    public async Task<List<LayerStat>> StatsAsync(string? charId = null)
    {
        var id = ResolveCharId(charId);
        var list = new List<LayerStat>();

        // 工作记忆：活跃会话的条数 + 配置的记忆条数
        var (sessions, messages) = _chat.SessionStats();
        list.Add(new LayerStat(MemoryLayer.Working, MemoryLayers.Name(MemoryLayer.Working),
            MemoryLayers.Describe(MemoryLayer.Working), messages, "条消息",
            $"活跃会话 {sessions} 个 · 记忆条数设置 {_settings.Current.MemoryTurns}"));

        var human = await _human.AllAsync(id);
        var photos = human.Count(h => h.Kind == HumanMemoryKind.Image);
        var important = human.Count(h => h.Kind == HumanMemoryKind.Important);
        list.Add(new LayerStat(MemoryLayer.Human, MemoryLayers.Name(MemoryLayer.Human),
            MemoryLayers.Describe(MemoryLayer.Human), human.Count, "条",
            $"其中照片 {photos} 张 · 重要时刻 {important} 条 · 回想窗口 {WindowText()}"));

        var links = await _semantic.LinkCountAsync(id);
        var topLinks = await _semantic.TopLinksAsync(id, 3);
        list.Add(new LayerStat(MemoryLayer.Semantic, MemoryLayers.Name(MemoryLayer.Semantic),
            MemoryLayers.Describe(MemoryLayer.Semantic), links, "条联系",
            topLinks.Count == 0 ? "还没长出来（多聊几句就会积累）"
                : "最紧的：" + string.Join("、", topLinks.Select(l => $"{l.A}↔{l.B}"))));

        var vaultCount = (await _vault.All(id)).Count;
        var tags = await _tags.ListAsync(id);
        list.Add(new LayerStat(MemoryLayer.Vault, MemoryLayers.Name(MemoryLayer.Vault),
            MemoryLayers.Describe(MemoryLayer.Vault), vaultCount, "条记录",
            tags.Count == 0 ? "还没有标签" : $"标签 {tags.Count} 个：" + string.Join("、", tags.Take(8).Select(t => t.Name))));

        var diary = await _vault.Diary(id);
        list.Add(new LayerStat(MemoryLayer.Archive, MemoryLayers.Name(MemoryLayer.Archive),
            MemoryLayers.Describe(MemoryLayer.Archive), diary.Count, "篇日记",
            diary.Count == 0 ? "还没有日记（每日自动生成）"
                : $"最近一篇 {diary.Last().Date:MM-dd}"));

        return list;
    }

    /// <summary>给提示词/界面用的一段总览文本。</summary>
    public async Task<string> SummaryTextAsync(string? charId = null)
    {
        var stats = await StatsAsync(charId);
        return string.Join("\n", stats.Select(s => $"· {s.Name}：{s.Count} {s.Unit}（{s.Detail}）"));
    }

    private string WindowText()
    {
        var days = _settings.Current.MemoryWindowDays;
        return days <= 0 ? "不限" : $"{days:0.#} 天";
    }

    private string ResolveCharId(string? charId)
    {
        if (!string.IsNullOrWhiteSpace(charId)) return charId!;
        var id = _engine.State.CharacterId;
        if (!string.IsNullOrEmpty(id)) return id;
        if (_engine.Roster.Count > 0) return _engine.Roster.Values.First().Profile.Id;
        return "小雨";
    }
}
