using WarmAsBefore.Models;

namespace WarmAsBefore.Services;

/// <summary>
/// 副设定服务：负责「主设定 → 副设定」的播种、随时间的漂移、提示词文本生成与回正。
///
/// 设计约束：
/// · 主设定是内核，本服务只读它、绝不改写它；
/// · 漂移不调用模型——由好感/信任/心情/精力等既有状态按规则推导，另加少量近期心事条目；
/// · 留影有界（<see cref="MaxHistory"/>），避免存档无限膨胀。
/// </summary>
public sealed class CharacterSubProfileService
{
    public const int MaxHistory = 12;
    public const int MaxNotes = 8;

    private readonly GameEngine _engine;
    private readonly CharacterLibrary _library;

    public CharacterSubProfileService(GameEngine engine, CharacterLibrary library)
    {
        _engine = engine;
        _library = library;
    }

    /// <summary>
    /// 播种副设定：新角色时把主设定复制一份作为起点；旧存档里的角色（没有副设定）在加载时补种。
    /// </summary>
    public static CharacterSubProfile Seed(CharacterProfile main, CharacterSubProfile? current = null)
    {
        var cur = current ?? new CharacterSubProfile();
        return cur with
        {
            Personality = string.IsNullOrEmpty(cur.Personality) ? main.Personality : cur.Personality,
            Description = string.IsNullOrEmpty(cur.Description) ? main.Description : cur.Description,
            Nickname = string.IsNullOrEmpty(cur.Nickname) ? main.Nickname : cur.Nickname,
            UserAddress = string.IsNullOrEmpty(cur.UserAddress) ? main.UserAddress : cur.UserAddress,
            Tone = string.IsNullOrEmpty(cur.Tone) ? DeriveTone(main, new CharacterState()) : cur.Tone
        };
    }

    /// <summary>角色库里所有角色的副设定补齐（幂等，仅在缺失时写入）。</summary>
    public async Task EnsureSeededAsync()
    {
        foreach (var ch in _engine.Roster.Values.ToList())
        {
            if (ch.SubProfile.IsSeeded) continue;
            await _library.SetSubProfileAsync(ch.Profile.Id, Seed(ch.Profile, ch.SubProfile));
        }
    }

    /// <summary>
    /// 按当前状态重算副设定（每轮对话/重要事件后调用）。
    /// <paramref name="note"/> 是可选的一条近期心事（如折叠出来的记忆要点）。
    /// </summary>
    public async Task DriftAsync(string charId, string reason, string? note = null)
    {
        if (!_engine.Roster.TryGetValue(charId, out var ch)) return;
        var cur = Seed(ch.Profile, ch.SubProfile);
        var tone = DeriveTone(ch.Profile, ch.State);

        var notes = new List<string>(cur.RecentNotes);
        if (!string.IsNullOrWhiteSpace(note))
        {
            var line = note.Trim();
            if (line.Length > 60) line = line[..60];
            if (notes.Count == 0 || notes[^1] != line)
            {
                notes.Add(line);
                if (notes.Count > MaxNotes) notes.RemoveRange(0, notes.Count - MaxNotes);
            }
        }

        // 心境与心事都没变就不写盘，避免每轮都产生留影
        if (notes.Count == cur.RecentNotes.Count && tone == cur.Tone) return;

        var history = new List<SubProfileSnapshot>(cur.History)
        {
            new() { Reason = reason, Tone = cur.Tone, Notes = cur.RecentNotes.ToList() }
        };
        if (history.Count > MaxHistory) history.RemoveRange(0, history.Count - MaxHistory);

        var next = cur with
        {
            Tone = tone,
            RecentNotes = notes,
            UpdatedAt = DateTime.UtcNow,
            LastDriftReason = reason,
            DriftCount = cur.DriftCount + 1,
            History = history
        };
        await _library.SetSubProfileAsync(charId, next);
    }

    /// <summary>回正：副设定退回主设定的副本（清空心事与留影），主设定不受影响。</summary>
    public async Task<bool> ResetAsync(string charId)
    {
        if (!_engine.Roster.TryGetValue(charId, out var ch)) return false;
        return await _library.SetSubProfileAsync(charId, Seed(ch.Profile, new CharacterSubProfile()));
    }

    /// <summary>
    /// 用模型给出的表述改写副设定（性格 / 背景的当下版本）。
    /// 主设定一个字都不动，改动进留影、可回正；两项都空则视为无改动。
    /// </summary>
    public async Task<bool> RewriteAsync(string charId, string? personality, string? description, string reason)
    {
        if (!_engine.Roster.TryGetValue(charId, out var ch)) return false;
        var cur = Seed(ch.Profile, ch.SubProfile);
        var p = (personality ?? "").Trim();
        var d = (description ?? "").Trim();
        if (p.Length == 0 && d.Length == 0) return false;
        if (p.Length > 300) p = p[..300];
        if (d.Length > 500) d = d[..500];
        if (p == cur.Personality && d == cur.Description) return false;

        var history = new List<SubProfileSnapshot>(cur.History)
        {
            new() { Reason = reason, Tone = cur.Tone, Notes = cur.RecentNotes.ToList() }
        };
        if (history.Count > MaxHistory) history.RemoveRange(0, history.Count - MaxHistory);

        var next = cur with
        {
            Personality = p.Length > 0 ? p : cur.Personality,
            Description = d.Length > 0 ? d : cur.Description,
            UpdatedAt = DateTime.UtcNow,
            LastDriftReason = reason,
            DriftCount = cur.DriftCount + 1,
            History = history
        };
        return await _library.SetSubProfileAsync(charId, next);
    }

    /// <summary>当前漂移次数（判断该不该触发改写用）。</summary>
    public int DriftCount(string? charId)
    {
        if (string.IsNullOrEmpty(charId) || !_engine.Roster.TryGetValue(charId, out var ch)) return 0;
        return ch.SubProfile.DriftCount;
    }

    /// <summary>对比页用的数据：主设定、副设定、留影。</summary>
    public sealed record Comparison(
        string MainName, string MainPersonality, string MainDescription,
        string SubPersonality, string SubDescription, string Tone,
        List<string> Notes, int DriftCount, string LastReason, DateTime UpdatedAt,
        List<SubProfileSnapshot> History);

    public Comparison? Compare(string? charId)
    {
        if (string.IsNullOrEmpty(charId) || !_engine.Roster.TryGetValue(charId, out var ch)) return null;
        var sub = Seed(ch.Profile, ch.SubProfile);
        return new Comparison(
            ch.Profile.Name,
            ch.Profile.Personality, ch.Profile.Description,
            sub.Personality, sub.Description, sub.Tone,
            sub.RecentNotes.ToList(), sub.DriftCount, sub.LastDriftReason, sub.UpdatedAt,
            sub.History.ToList());
    }

    /// <summary>改写提示词要用的素材：主设定 + 当下心境 + 近期心事 + 记得的事。</summary>
    public async Task<string> BuildRewriteMaterialAsync(string charId)
    {
        if (!_engine.Roster.TryGetValue(charId, out var ch)) return "";
        var sub = Seed(ch.Profile, ch.SubProfile);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"名字：{ch.Profile.Name}");
        sb.AppendLine($"主设定（内核，不可改）：{ch.Profile.Personality}");
        if (!string.IsNullOrWhiteSpace(ch.Profile.Description))
            sb.AppendLine($"主设定背景：{ch.Profile.Description}");
        if (!string.IsNullOrWhiteSpace(sub.Tone)) sb.AppendLine($"当下心境：{sub.Tone}");
        if (sub.RecentNotes.Count > 0) sb.AppendLine("近期心事：" + string.Join("；", sub.RecentNotes));
        try
        {
            var memories = await _library.ListAsync();   // 保证角色库已就绪
            _ = memories;
        }
        catch { /* 仅用于确保已加载 */ }
        return sb.ToString();
    }

    /// <summary>提示词文本：主设定是内核，副设定只体现在语气里，不该被直接说出来。</summary>
    public string Describe(string? charId)
    {
        if (string.IsNullOrEmpty(charId) || !_engine.Roster.TryGetValue(charId, out var ch)) return "";
        var sub = ch.SubProfile;
        if (!sub.IsSeeded) sub = Seed(ch.Profile, sub);
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(sub.Tone)) parts.Add(sub.Tone);
        if (sub.RecentNotes.Count > 0)
            parts.Add("最近放在心上的事：" + string.Join("；", sub.RecentNotes));
        if (parts.Count == 0) return "";
        return "你此刻的状态（会随经历变化，只体现在语气与反应里，不要直接说出来）："
            + string.Join("。", parts) + "。";
    }

    /// <summary>与主设定的差异摘要，供角色库查看。</summary>
    public string DiffSummary(string? charId)
    {
        if (string.IsNullOrEmpty(charId) || !_engine.Roster.TryGetValue(charId, out var ch))
            return "角色不存在。";
        var sub = Seed(ch.Profile, ch.SubProfile);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("主设定（内核，不随时间改变）");
        sb.AppendLine("· 性格：" + (string.IsNullOrWhiteSpace(ch.Profile.Personality) ? "（空）" : ch.Profile.Personality));
        sb.AppendLine("· 背景：" + (string.IsNullOrWhiteSpace(ch.Profile.Description) ? "（空）" : ch.Profile.Description));
        sb.AppendLine();
        sb.AppendLine($"副设定（当下之我，已漂移 {sub.DriftCount} 次，{sub.UpdatedAt.ToLocalTime():MM-dd HH:mm} 更新）");
        sb.AppendLine("· 心境：" + (string.IsNullOrWhiteSpace(sub.Tone) ? "（未建立）" : sub.Tone));
        sb.Append(sub.RecentNotes.Count == 0
            ? "· 心事：暂无"
            : "· 心事：\n  " + string.Join("\n  ", sub.RecentNotes));
        if (!string.IsNullOrWhiteSpace(sub.LastDriftReason))
            sb.Append($"\n\n最近一次变化的原因：{sub.LastDriftReason}");
        return sb.ToString();
    }

    /// <summary>
    /// 心境基调推导：只用好感/信任/心情/精力这些已经有的事实，不额外花钱。
    /// </summary>
    private static string DeriveTone(CharacterProfile main, CharacterState st)
    {
        var closeness = st.Affection switch
        {
            < 10 => "对你还有些生疏，说话会留一点距离",
            < 30 => "已经习惯你在身边，语气自然放松",
            < 60 => "把你当成亲近的人，会主动找话说",
            < 80 => "很依赖你，愿意示弱、也会小小地撒娇",
            _ => "把你当成离不开的人，说话带着不加掩饰的在意"
        };
        var trust = st.Trust switch
        {
            <= 20 => "心里对你还有保留",
            >= 70 => "什么都愿意跟你讲",
            _ => ""
        };
        var mood = st.Mood?.Trim().ToLowerInvariant() switch
        {
            "happy" or "joy" or "开心" => "今天心情不错",
            "sad" or "难过" or "低落" => "情绪有点低落",
            "angry" or "生气" => "还在生闷气",
            "tired" or "累" => "有点疲倦",
            _ => st.Energy < 40 ? "精力不太够" : ""
        };
        var bits = new List<string> { closeness };
        if (trust.Length > 0) bits.Add(trust);
        if (mood.Length > 0) bits.Add(mood);
        return string.Join("；", bits) + "。";
    }
}
