using System.Globalization;
using System.Text.RegularExpressions;
using WarmAsBefore.Helpers;

namespace WarmAsBefore.Services;

/// <summary>一条归属明确的消息：这是「你」说的、TA 说的，还是群里另外的人说的。</summary>
public sealed record ChatTurn
{
    public DateTime? At { get; init; }
    public string Speaker { get; init; } = "";
    public string Text { get; init; } = "";
    public ChatSpeakerRole Role { get; init; }
    public string Source { get; init; } = "";
}

/// <summary>一段连续的聊天：中间隔了半小时以上就算另起一段，和人对「那一次聊天」的记忆颗粒度一致。</summary>
public sealed record ChatEpisode
{
    public int Index { get; init; }
    public DateTime? Start { get; init; }
    public DateTime? End { get; init; }
    public int Turns { get; init; }
    public string Opener { get; init; } = "";
    public string Closer { get; init; } = "";
    public string? Notable { get; init; }
    public double Emotion { get; init; }
    /// <summary>这一段里第一个说话的人；两边都不是就为 null（群聊/系统消息）。</summary>
    public ChatSpeakerRole? InitiatedBy { get; init; }
    /// <summary>在 <see cref="ChatLogDigest.Turns"/> 里的起止下标（End 不含）。</summary>
    public int StartIndex { get; init; }
    public int EndIndex { get; init; }
}

/// <summary>一条准备写进记忆层的种子：正文、要不要当重要时刻、初始重要性。</summary>
public sealed record ChatMemorySeed(string Text, bool Important, double Importance);

/// <summary>
/// 从导入的聊天记录里读出「这段关系本来的样子」：互动总量、双方主动比例、情绪密度，
/// 再折成一个初始好感/信任与一批记忆条目。
///
/// 全部是确定性纯函数：同样的聊天记录每次算出的数值完全一致，方便自测与事后解释。
/// 公式里的每一档都对应一句人话（见 <see cref="Explain"/>），数值不是凭空来的。
/// </summary>
public sealed record ChatLogDigest
{
    public List<ChatTurn> Turns { get; init; } = new();
    public List<ChatEpisode> Episodes { get; init; } = new();

    public int Messages => Turns.Count;
    public int Skipped { get; init; }
    public int UserMessages { get; init; }
    public int CharacterMessages { get; init; }
    public int OtherMessages { get; init; }
    public int UserInitiated { get; init; }
    public int CharacterInitiated { get; init; }
    public double UserInitiatedRatio { get; init; }
    public int SpanDays { get; init; }
    public DateTime? FirstAt { get; init; }
    public DateTime? LastAt { get; init; }

    /// <summary>初始好感：绝对点数 0–1000（<see cref="AffectionScale"/> 的唯一刻度）。</summary>
    public int Affection { get; init; }
    public int Trust { get; init; }

    public string RecentSummary { get; init; } = "";
    public List<string> Notables { get; init; } = new();

    /// <summary>这个文件里出现过的发言者与条数（导入页用来「谁是 TA」的选择与结果说明）。</summary>
    public Dictionary<string, int> Speakers { get; init; } = new(StringComparer.Ordinal);
    /// <summary>被当成 TA 的发言者；没有就是自动选的。</summary>
    public string CharacterSpeaker { get; init; } = "";
    public string UserSpeaker { get; init; } = "";

    public bool HasAnything => Messages > 0;

    /// <summary>给导入结果页的一句人话：数值从哪些事实推出来。</summary>
    public string Explain()
    {
        if (Messages == 0) return "没有可用的消息。";
        var span = SpanDays > 1 ? $"跨 {SpanDays} 天" : "同一天";
        return $"共 {Messages} 条 · 你 {UserMessages} / TA {CharacterMessages}" +
               (OtherMessages > 0 ? $" / 其他人 {OtherMessages}" : "") +
               $" · {span} · 你发起 {UserInitiatedRatio:P0} 的对话 → 初始 {AffectionScale.TextOf(Affection)}、信任 {Trust}";
    }

    /// <summary>
    /// 读一遍聊天记录。顺序：归属发言者 → 切段 → 找情绪事件 → 算初始值 → 生成记忆种子。
    /// </summary>
    /// <param name="characterName">角色主名。</param>
    /// <param name="nickname">角色昵称（可为空）。</param>
    /// <param name="characterSpeaker">用户指认的「TA 在这个导出里的名字」；空 = 自动判断。</param>
    public static ChatLogDigest Build(IReadOnlyList<ImportedMessage> messages, string characterName,
        string nickname = "", string characterSpeaker = "", int skipped = 0)
    {
        if (messages.Count == 0) return new ChatLogDigest { Skipped = skipped };

        var speakers = messages
            .GroupBy(m => m.Speaker, StringComparer.Ordinal)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        var (charSpeaker, userSpeaker) = ResolveSpeakers(speakers, characterName, nickname, characterSpeaker);

        var turns = messages.Select(m => new ChatTurn
        {
            At = m.At,
            Speaker = m.Speaker,
            Text = m.Text.Trim(),
            Role = string.Equals(m.Speaker, charSpeaker, StringComparison.Ordinal) ? ChatSpeakerRole.Character
                 : string.Equals(m.Speaker, userSpeaker, StringComparison.Ordinal) ? ChatSpeakerRole.User
                 : ChatSpeakerRole.Other,
            Source = m.Source
        }).ToList();

        var episodes = BuildEpisodes(turns);
        var userCount = turns.Count(t => t.Role == ChatSpeakerRole.User);
        var charCount = turns.Count(t => t.Role == ChatSpeakerRole.Character);
        var otherCount = turns.Count(t => t.Role == ChatSpeakerRole.Other);

        var initiatedUser = episodes.Count(e => e.InitiatedBy == ChatSpeakerRole.User);
        var initiatedChar = episodes.Count(e => e.InitiatedBy == ChatSpeakerRole.Character);
        var initiatedTotal = Math.Max(1, initiatedUser + initiatedChar);
        var ratio = (double)initiatedUser / initiatedTotal;

        var dated = turns.Where(t => t.At.HasValue).Select(t => t.At!.Value).OrderBy(d => d).ToList();
        var firstAt = dated.Count > 0 ? dated[0] : (DateTime?)null;
        var lastAt = dated.Count > 0 ? dated[^1] : (DateTime?)null;
        var spanDays = dated.Count > 1 ? Math.Max(1, (int)Math.Ceiling((dated[^1] - dated[0]).TotalDays) + 1) : (dated.Count == 1 ? 1 : 0);

        var notables = turns
            .Where(t => t.Role != ChatSpeakerRole.Other)
            .Where(t => EmotionOf(t.Text) >= 1.5)
            .Select(t => (Turn: t, Score: EmotionOf(t.Text)))
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Turn.At ?? DateTime.MinValue)
            .Take(8)
            .OrderBy(x => x.Turn.At ?? DateTime.MinValue)
            .Select(x => $"{(x.Turn.Role == ChatSpeakerRole.Character ? "TA" : "你")}说过：「{Excerpt(x.Turn.Text, 34)}」")
            .ToList();

        var emotionDensity = turns.Count == 0 ? 0 : Math.Min(1,
            turns.Sum(t => EmotionOf(t.Text)) / Math.Max(8.0, turns.Count * 0.8));

        var (legacyAffection, trust) = Score(userCount, charCount, turns.Count, ratio, spanDays, emotionDensity, notables.Count);
        // 公式算出来的是旧 0–100 的「熟度」，落进存档前必须换成 0–1000 点数：
        // 换算只有一个入口（AffectionScale.FromLegacy），别在这里再写一遍 ×10。
        var affection = AffectionScale.Clamp(AffectionScale.FromLegacy(legacyAffection));

        var recent = BuildRecentSummary(turns, episodes);

        return new ChatLogDigest
        {
            Turns = turns,
            Episodes = episodes,
            Skipped = skipped,
            UserMessages = userCount,
            CharacterMessages = charCount,
            OtherMessages = otherCount,
            UserInitiated = initiatedUser,
            CharacterInitiated = initiatedChar,
            UserInitiatedRatio = ratio,
            SpanDays = spanDays,
            FirstAt = firstAt,
            LastAt = lastAt,
            Affection = affection,
            Trust = trust,
            RecentSummary = recent,
            Notables = notables,
            Speakers = speakers,
            CharacterSpeaker = charSpeaker,
            UserSpeaker = userSpeaker
        };
    }

    /// <summary>
    /// 谁是 TA、谁是你。优先用名字匹配；名字对不上时用「两方里发言最多的」当 TA，
    /// 并把选中的名字回给界面 —— 用户可以在导入页改，不能悄悄归错。
    /// </summary>
    private static (string Char, string User) ResolveSpeakers(Dictionary<string, int> speakers,
        string characterName, string nickname, string forced)
    {
        if (speakers.Count == 0) return ("", "");

        var names = new[] { characterName, nickname }
            .Select(n => (n ?? "").Trim())
            .Where(n => n.Length >= 2)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (!string.IsNullOrWhiteSpace(forced) && speakers.ContainsKey(forced.Trim()))
            return (forced.Trim(), speakers.Keys.FirstOrDefault(k => k != forced.Trim()) ?? "");

        bool Matches(string speaker) => names.Any(n =>
            speaker.Contains(n, StringComparison.OrdinalIgnoreCase) ||
            n.Contains(speaker, StringComparison.OrdinalIgnoreCase));

        var charSpeaker = speakers.Keys.FirstOrDefault(Matches);
        if (charSpeaker is null && speakers.Count >= 2)
            charSpeaker = speakers.Keys.First();          // 次数最多的那位（字典已按条数降序建）
        charSpeaker ??= speakers.Keys.First();

        var userSpeaker = speakers.Keys.FirstOrDefault(k =>
            !string.Equals(k, charSpeaker, StringComparison.Ordinal) &&
            (k is "我" or "自己" or "me" or "Me" || k.Contains("我", StringComparison.Ordinal))) ??
            speakers.Keys.FirstOrDefault(k => !string.Equals(k, charSpeaker, StringComparison.Ordinal)) ??
            "";
        return (charSpeaker, userSpeaker);
    }

    private static List<ChatEpisode> BuildEpisodes(List<ChatTurn> turns)
    {
        var list = new List<ChatEpisode>();
        var start = 0;
        for (var i = 1; i <= turns.Count; i++)
        {
            var cut = i == turns.Count;
            if (!cut)
            {
                var prev = turns[i - 1].At;
                var next = turns[i].At;
                var gap = prev.HasValue && next.HasValue && (next.Value - prev.Value).TotalMinutes > 30;
                if (!gap && i - start < 40) continue;
            }

            var slice = turns.GetRange(start, i - start);
            var first = slice.FirstOrDefault(t => t.Role != ChatSpeakerRole.Other) ?? slice[0];
            var last = slice.LastOrDefault(t => t.Role != ChatSpeakerRole.Other) ?? slice[^1];
            var notable = slice.Where(t => t.Role != ChatSpeakerRole.Other)
                .Select(t => (Turn: t, Score: EmotionOf(t.Text)))
                .Where(x => x.Score >= 1.5)
                .OrderByDescending(x => x.Score)
                .Select(x => x.Turn.Text)
                .FirstOrDefault();
            list.Add(new ChatEpisode
            {
                Index = list.Count,
                StartIndex = start,
                EndIndex = i,
                InitiatedBy = slice.FirstOrDefault(t => t.Role != ChatSpeakerRole.Other)?.Role,
                Start = slice.FirstOrDefault(t => t.At.HasValue)?.At,
                End = slice.LastOrDefault(t => t.At.HasValue)?.At,
                Turns = slice.Count,
                Opener = Excerpt(first.Text, 16),
                Closer = Excerpt(last.Text, 16),
                Notable = notable is null ? null : Excerpt(notable, 30),
                Emotion = slice.Count == 0 ? 0 : slice.Average(t => EmotionOf(t.Text))
            });
            start = i;
        }
        return list;
    }

    /// <summary>
    /// 初始关系：证据量（条数）、均衡度（谁说得更多）、时间跨度、情绪浓度各占一档。
    /// 上限压得比「聊了很久的老夫老妻」低：导入只是起点，剩下的时间该在游戏里走。
    /// </summary>
    private static (int Affection, int Trust) Score(int user, int character, int total,
        double userRatio, int spanDays, double emotion, int notables)
    {
        if (total == 0) return (0, 0);

        var evidence = Math.Clamp(Math.Log10(1 + total) / Math.Log10(401), 0, 1); // 400 条到顶
        var balance = 1 - Math.Abs(userRatio - 0.5) * 2;                          // 1=有来有回
        var span = Math.Clamp(spanDays / 90.0, 0, 1);                             // 90 天到顶
        var depth = Math.Clamp(total / 300.0, 0, 1);
        var mood = Math.Clamp(emotion * 0.6 + Math.Min(1, notables / 6.0) * 0.4, 0, 1);

        var affection = (int)Math.Round(8 + 40 * evidence + 10 * balance + 6 * span + 6 * depth + 8 * mood);
        var trust = (int)Math.Round(6 + 28 * evidence + 12 * balance + 8 * span + 6 * mood);

        // 单方面刷屏、只有几条、没有任何情绪事件的关系，不该一上来就很熟。
        if (user + character > 0 && Math.Min(user, character) == 0) { affection -= 10; trust -= 10; }
        if (total < 10) { affection = Math.Min(affection, 14); trust = Math.Min(trust, 12); }

        return (Math.Clamp(affection, 3, 92), Math.Clamp(trust, 0, 85));
    }

    private static string BuildRecentSummary(List<ChatTurn> turns, List<ChatEpisode> episodes)
    {
        var slice = episodes.Count > 0
            ? turns.GetRange(episodes[^1].StartIndex, episodes[^1].EndIndex - episodes[^1].StartIndex)
            : turns;

        var last = slice.LastOrDefault(t => t.Role != ChatSpeakerRole.Other) ?? slice[^1];
        var who = last.Role == ChatSpeakerRole.Character ? "TA" : last.Role == ChatSpeakerRole.User ? "你" : last.Speaker;
        var when = last.At.HasValue ? last.At.Value.ToString("M月d日 HH:mm", CultureInfo.InvariantCulture) : "最近";
        var head = slice.FirstOrDefault(t => t.Role != ChatSpeakerRole.Other) ?? slice[0];
        var tail = who + "说「" + Excerpt(last.Text, 18) + "」";
        var text = $"{when}那次聊了{slice.Count}句，从「{Excerpt(head.Text, 12)}」到{tail}";
        return Excerpt(text, 58);
    }

    /// <summary>情绪强度：0=叙事，1=有起伏，2=明显情绪事件。关键词表刻意短，宁缺勿滥。</summary>
    public static double EmotionOf(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        double score = 0;
        foreach (var w in StrongWords)
            if (text.Contains(w, StringComparison.Ordinal)) { score += 1.6; break; }
        foreach (var w in WarmWords)
            if (text.Contains(w, StringComparison.Ordinal)) { score += 0.6; break; }
        foreach (var w in RoughWords)
            if (text.Contains(w, StringComparison.Ordinal)) { score += 0.9; break; }
        return Math.Min(2, score);
    }

    private static readonly string[] StrongWords =
    {
        "我爱你", "喜欢你", "想你", "想念", "抱抱", "亲亲", "心疼", "对不起", "分手", "吵架",
        "生气", "难过", "哭了", "哭", "害怕", "孤独", "失眠", "谢谢", "再见", "等我"
    };

    private static readonly string[] WarmWords = { "开心", "高兴", "笑", "哈哈", "温暖", "陪", "在一起", "安心" };

    private static readonly string[] RoughWords = { "累", "烦", "担心", "失望", "委屈", "别扭", "冷战" };

    /// <summary>写进记忆层的种子：每段一次的摘要、最近一次、明显情绪事件。</summary>
    public List<ChatMemorySeed> MemorySeeds(int maxEpisodes = 30)
    {
        var seeds = new List<ChatMemorySeed>();
        if (Episodes.Count == 0) return seeds;

        var picked = Episodes
            .OrderByDescending(e => e.Emotion)
            .ThenByDescending(e => e.Turns)
            .Take(maxEpisodes)
            .OrderBy(e => e.Start ?? DateTime.MinValue)
            .ToList();

        foreach (var e in picked)
        {
            var day = e.Start?.ToString("M月d日", CultureInfo.InvariantCulture) ?? "某天";
            var tail = e.Notable is not null ? $"印象最深的是「{e.Notable}」" : $"最后说到「{e.Closer}」";
            seeds.Add(new ChatMemorySeed(
                Excerpt($"{day}聊了{e.Turns}句，从「{e.Opener}」开始，{tail}", 58),
                Important: e.Emotion >= 1.6,
                Importance: Math.Clamp(0.42 + e.Emotion * 0.2, 0.3, 0.88)));
        }

        if (RecentSummary.Length > 0)
            seeds.Add(new ChatMemorySeed("最近一次聊天：" + RecentSummary, Important: true, Importance: 0.82));

        foreach (var n in Notables)
            seeds.Add(new ChatMemorySeed(n, Important: false, Importance: 0.7));

        return seeds;
    }

    private static string Excerpt(string? text, int max)
    {
        var s = Regex.Replace((text ?? "").Trim(), @"\s+", " ");
        if (s.Length <= max) return s;
        return s[..Math.Max(1, max - 1)] + "…";
    }
}
