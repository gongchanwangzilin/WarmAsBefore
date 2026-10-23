namespace WarmAsBefore.Models;

/// <summary>人类记忆的条目类型：人只记事件、抽象、重要的事，以及单张图片。</summary>
public enum HumanMemoryKind
{
    /// <summary>发生过的一件事（一句话）。</summary>
    Event = 0,
    /// <summary>从经历里抽出来的结论/印象（「TA 其实很怕黑」）。</summary>
    Abstraction = 1,
    /// <summary>重要时刻（约定、第一次、重大情绪节点）。</summary>
    Important = 2,
    /// <summary>单张图片（照片式记忆，只留一张，不留整段过程）。</summary>
    Image = 3
}

/// <summary>手工贴 / 摘句级标签的结果，界面据此决定要不要再问一次「这句是记的什么」。</summary>
public enum SentenceTagChange
{
    None = 0,
    Added = 1,
    Removed = 2
}

/// <summary>
/// 句级标签：一条记忆里「第几句」贴了哪些标签。
/// 整条打一个标签太粗（一条事里既有地点又有情绪），按句贴才看得清 TA 到底在记什么。
/// </summary>
public sealed record SentenceTag
{
    /// <summary>句子序号，对应 <see cref="HumanMemoryMath.SplitSentences"/> 的结果。</summary>
    public int Index { get; init; }

    public List<string> Tags { get; init; } = new();
}

/// <summary>
/// 人类式记忆条目：模仿人脑的存法——不存整段对话原文，只存关键点、印象与重要时刻。
/// 检索时按「重要性 × 时间衰减 × 复述次数 × 随机扰动」打分，而不是简单按时间倒序取 N 条。
/// </summary>
public sealed record HumanMemoryEntry
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N")[..12];
    public string CharacterId { get; init; } = "";
    public HumanMemoryKind Kind { get; init; } = HumanMemoryKind.Event;

    /// <summary>记忆内容（第一人称口语，一条不超过 40 字）。</summary>
    public string Text { get; init; } = "";

    /// <summary>图片式记忆的图片路径（相对存储根目录）；其它类型为空。</summary>
    public string ImagePath { get; init; } = "";

    /// <summary>重要性 0.05~1：越重要越不容易忘。</summary>
    public double Importance { get; init; } = 0.5;

    /// <summary>被回想起来的次数：复述过的记忆更容易再次被想起。</summary>
    public int RecallCount { get; init; }

    /// <summary>所属标签（情感/地点/人物/事件等），供标签检索与组合检索。</summary>
    public List<string> Tags { get; init; } = new();

    /// <summary>标签落在第几句：列表 UI 按这个给句子高亮，比整条挂一串 # 号一目了然。</summary>
    public List<SentenceTag> SentenceTags { get; init; } = new();

    /// <summary>
    /// 这件事的情绪配比。存的是「几分几分」而不是一个词：
    /// 只留一个词，下次想起那晚就只剩「害怕」，把最想留住的那点暖味弄丢了。
    /// </summary>
    public EmotionMix? Emotion { get; init; }

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime LastRecalledAt { get; init; } = DateTime.UtcNow;

    // ===== 核心记忆 / 情绪强度 / 关联 / 遗忘融合 / 创伤（v1.47） =====

    /// <summary>
    /// 核心记忆：TA 人格的锚点。永不衰减、不参与复述计数、每次回想都能被取到；
    /// 它不只是一个条目，还会「辐射」到与它共享标签/字词的普通记忆上，给它们加权。
    /// </summary>
    public bool IsCore { get; init; }

    /// <summary>情绪强度 0~1：由情绪配比的浓度决定。核心记忆与创伤都靠它立住。</summary>
    public double EmotionalIntensity { get; init; }

    /// <summary>记忆之间的关联词（比标签更自由，链式搜索用它搭桥）。</summary>
    public List<string> Associations { get; init; } = new();

    /// <summary>创伤：一种特殊的核心记忆，不会被遗忘；疗愈只降低情绪强度，不删除内容。</summary>
    public bool IsTrauma { get; init; }

    /// <summary>创伤阶段 0=无 1=应激（强烈生理/情绪反应）2=灼痛 3=结痂（能平静地叹口气）4=融入人格。</summary>
    public int TraumaStage { get; init; }

    /// <summary>有效疗愈次数：只在特定事件/互动里增加，时间几乎不改变它。</summary>
    public int TraumaHeals { get; init; }

    public DateTime? LastHealedAt { get; init; }

    /// <summary>已被融合进哪条记忆：遗忘不是删除，是化进周围的事里，仍然影响判断。</summary>
    public string FusedIntoId { get; init; } = "";
    public DateTime? FusedAt { get; init; }

    public bool IsForgotten => !string.IsNullOrEmpty(FusedIntoId);
}

/// <summary>链式搜索的一环：从线索出发，经由哪条记忆、凭什么连上。</summary>
public sealed record HumanChainHit(HumanMemoryEntry Entry, double Score, string Why, int Depth, string Bridge);

/// <summary>链式搜索的结果：默认 5 条，按「新的在前；同一天里更像的在前」排。</summary>
public sealed record HumanChainResult(IReadOnlyList<HumanChainHit> Hits, string Summary);

/// <summary>
/// 一次搜索命中：除条目本身，还带回「凭什么命中」和分值。
/// 搜索结果不能只给一句「相关」——用户要看得见是文字、标签、情绪还是日期对上的，
/// 才知道该不该信这条、要不要顺手改标签。
/// </summary>
public sealed record HumanMemoryHit(HumanMemoryEntry Entry, double Score, string Why);

/// <summary>
/// 人类记忆的打分与衰减：纯函数，便于单独验证。
///
/// 两条实验心理学的设定，都不是「按天数取个百分比」那种拍脑袋衰减：
/// · **叠加遗忘曲线**（Wickelgren 1974，对 Ebbinghaus 单指数的修正）：R = c + (1−c)·e^(−t/τ)。
///   单指数会把老记忆一路衰减到零，可人不是这样——反复巩固过的事留一个「永久成分」c。
///   这里让 c 由重要性决定（平方关系：重要性 1 留 35%，0.5 只留 9%，琐碎事归零），
///   所以「第一次」「承诺」这类事过很多年也想得起来，昨天随口一句会彻底沉底。
/// · **间隔效应**（spacing / distributed practice，Cepeda 等 2006）：每成功回想一次，
///   衰减时间常数被拉长 τ_eff = τ·(1 + 0.6·ln(1+n))，即「常被想起的事越来越不忘」。
///
/// 注意：衰减的起算时间**始终是最初记下的时刻**，不是最后一次回想。ACT-R 的 base-level
/// 是按最后使用时间衰减的，但这里照搬会让每轮被选中的那几条重置成「新鲜」，于是同样的
/// 几条记忆永远霸占结果、新记忆再也进不来。回想的作用只体现在拉长 τ 上。
/// </summary>
public static class HumanMemoryMath
{
    public const double DefaultWindowDays = 3;
    public const double DefaultRandomness = 0.25;

    /// <summary>低于这个权重的记忆不再进入检索结果，避免几十条陈旧记忆把上下文撑满。</summary>
    public const double MinWeight = 0.02;

    /// <summary>重要性满格时保留的「永久成分」上限（叠加曲线的下渐近线）。</summary>
    public const double MaxAsymptote = 0.35;

    /// <summary>每次回想对时间常数的拉伸强度：间隔效应，取对数所以越往后增益越小。</summary>
    public const double SpacingGain = 0.6;

    /// <summary>衰减时间常数（天）。窗口为 0（不限）时不衰减，返回无穷大。</summary>
    public static double Tau(double windowDays, int recallCount = 0)
        => windowDays <= 0 ? double.PositiveInfinity
         : windowDays * (1 + SpacingGain * Math.Log(1 + Math.Max(0, recallCount)));

    /// <summary>永久成分：重要性越高越不会彻底忘掉（平方压制，避免什么都变永久）。</summary>
    public static double Asymptote(double importance)
    {
        var i = Math.Clamp(importance, 0.05, 1.0);
        return MaxAsymptote * i * i;
    }

    /// <summary>
    /// 保留强度 0~1：<c>windowDays</c> 为 0（不限窗口）时恒为 1，只由重要性决定排序。
    /// </summary>
    public static double Decay(double ageDays, double windowDays, double importance = 1.0, int recallCount = 0)
    {
        var tau = Tau(windowDays, recallCount);
        if (double.IsPositiveInfinity(tau)) return 1.0;
        var raw = ageDays <= 0 ? 1.0 : Math.Exp(-ageDays / tau);
        var c = Asymptote(importance);
        return c + (1 - c) * raw;
    }

    /// <summary>
    /// 半衰期（天）：保留强度掉到 0.5 要多久。永久成分本身已 ≥0.5 时返回无穷大——
    /// 「这件事 TA 一辈子都不会忘到一半以下」，设置页要能这样讲给用户听。
    /// </summary>
    public static double HalfLifeDays(double windowDays, double importance, int recallCount = 0)
    {
        var tau = Tau(windowDays, recallCount);
        if (double.IsPositiveInfinity(tau)) return double.PositiveInfinity;
        var c = Asymptote(importance);
        if (c >= 0.5) return double.PositiveInfinity;
        return tau * Math.Log((1 - c) / (0.5 - c));
    }

    public static double Score(HumanMemoryEntry e, double windowDays, double randomness, DateTime now)
    {
        // 核心记忆完全绕开遗忘机制：不打折、不靠复述，什么时候想都在。
        if (e.IsCore) return 10;

        var ageDays = Math.Max(0, (now - e.CreatedAt).TotalDays);
        var importance = Math.Clamp(e.Importance, 0.05, 1.0);
        var retention = Decay(ageDays, windowDays, importance, e.RecallCount);
        var repetition = 1 + Math.Log(1 + Math.Max(0, e.RecallCount)) * 0.5;
        var emotion = 1 + Math.Clamp(e.EmotionalIntensity, 0, 1) * 0.9;
        var jitter = 1 + Math.Clamp(randomness, 0, 1) * (Random.Shared.NextDouble() * 2 - 1);
        return Math.Max(0, importance * retention * repetition * emotion * jitter);
    }

    /// <summary>
    /// 情绪强度：由配比的集中度决定（三成害怕两成安心，比「一点点难过」更抓人）。
    /// 没有情绪配比时给一个保守值，避免老数据全部变成零情绪。
    /// </summary>
    public static double EmotionalIntensityOf(EmotionMix? mix, double fallback = 0.35)
    {
        if (mix is null || mix.IsEmpty) return fallback;
        var concentration = Math.Sqrt(mix.Parts.Sum(p => p.Ratio * p.Ratio));
        var top = mix.Parts.Max(p => p.Ratio);
        return Math.Clamp(0.35 + concentration * 0.45 + top * 0.2, 0, 1);
    }

    /// <summary>该不该升为核心：重要性够高，且情绪够浓或本身就是创作伤。</summary>
    public static bool CoreEligible(double importance, double emotionalIntensity, bool isTrauma, double threshold)
    {
        if (isTrauma) return true;
        var t = Math.Clamp(threshold <= 0 ? 0.82 : threshold, 0.5, 1.0);
        return importance >= 0.85 && importance * Math.Max(0.35, emotionalIntensity) >= t;
    }

    /// <summary>创伤阶段：强度越高越靠前；疗愈次数把阶段一格一格往下推。只降强度，不删记忆。</summary>
    public static int TraumaStageOf(double intensity, int heals)
    {
        var i = Math.Clamp(intensity - heals * 0.12, 0, 1);
        if (i >= 0.7) return 1;
        if (i >= 0.45) return 2;
        if (i >= 0.2) return 3;
        return 4;
    }

    public static string TraumaStageName(int stage) => stage switch
    {
        1 => "应激（一点点相关的东西都会让身体先于理智起反应）",
        2 => "灼痛（记得很清楚，情绪还很烫）",
        3 => "结痂（能平静地提起，偶尔叹一口气）",
        4 => "融入（已经成了「我为什么是现在的我」的一部分）",
        _ => "无"
    };

    // ===== 链式搜索 =====

    /// <summary>把一段话拆成可比较的小单位：中文按单字，西文按词，数字串整体算一个词。</summary>
    public static HashSet<string> Units(string? text)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(text)) return set;
        var word = new System.Text.StringBuilder();
        foreach (var ch in text)
        {
            if (char.IsLetterOrDigit(ch))
            {
                if (ch <= 0x7F) { word.Append(ch); continue; }
                // 中文单字本身就是「共同的字」，直接入集合
                if (word.Length > 0) { set.Add(word.ToString()); word.Clear(); }
                set.Add(ch.ToString());
            }
            else if (word.Length > 0) { set.Add(word.ToString()); word.Clear(); }
        }
        if (word.Length > 0) set.Add(word.ToString());
        return set;
    }

    /// <summary>两条记忆共享的「字/词」个数（越像越大）。</summary>
    public static int SharedUnits(string? a, string? b)
    {
        var ua = Units(a);
        if (ua.Count == 0) return 0;
        var ub = Units(b);
        if (ub.Count == 0) return 0;
        return ua.Count(u => ub.Contains(u));
    }

    /// <summary>相对重叠率 0~1。</summary>
    public static double Overlap(string? a, string? b)
    {
        var ua = Units(a);
        var ub = Units(b);
        if (ua.Count == 0 || ub.Count == 0) return 0;
        var shared = ua.Count(u => ub.Contains(u));
        return shared / Math.Sqrt(ua.Count * (double)ub.Count);
    }

    /// <summary>
    /// 两个条目之间的链强度：共享字词、共享标签、时间靠近、情绪相似、事件类型一致。
    /// 这是链式搜索里「凭什么连上」的依据，也直接显示给用户看。
    /// </summary>
    public static (double Score, string Why) ChainEdge(HumanMemoryEntry a, HumanMemoryEntry b, DateTime now)
    {
        var score = 0.0;
        var why = new List<string>();

        var shared = SharedUnits(a.Text, b.Text);
        var overlap = Overlap(a.Text, b.Text);
        if (shared > 0)
        {
            score += Math.Min(0.45, overlap * 1.2);
            why.Add($"共同的字词 {shared} 个");
        }

        var sharedTags = a.Tags.Intersect(b.Tags, StringComparer.OrdinalIgnoreCase).ToList();
        if (sharedTags.Count > 0)
        {
            score += Math.Min(0.35, sharedTags.Count * 0.16);
            why.Add("都贴着「" + string.Join("、", sharedTags.Take(3)) + "」");
        }

        var tagOverlap = a.Associations.Intersect(b.Associations, StringComparer.OrdinalIgnoreCase).ToList();
        if (tagOverlap.Count > 0)
        {
            score += Math.Min(0.2, tagOverlap.Count * 0.1);
            why.Add("想起时会带出「" + string.Join("、", tagOverlap.Take(2)) + "」");
        }

        var days = Math.Abs((a.CreatedAt - b.CreatedAt).TotalDays);
        if (days <= 3)
        {
            score += 0.22 * (1 - days / 3.0);
            why.Add(days < 1 ? "就是同一阵子的事" : $"相隔不到 {Math.Ceiling(days)} 天");
        }
        else if (days <= 14)
        {
            score += 0.08 * (1 - (days - 3) / 11.0);
            why.Add($"时间上也挨着（{Math.Round(days)} 天）");
        }

        var feel = a.Emotion?.SimilarityTo(b.Emotion) ?? 0;
        if (feel >= 0.5)
        {
            score += feel * 0.12;
            why.Add("那时的情绪很像");
        }

        if (a.Kind == b.Kind)
        {
            score += 0.04;
        }

        return (Math.Clamp(score, 0, 1.5), string.Join(" · ", why));
    }

    /// <summary>
    /// 链式搜索排序：新的在前；同一天里，越像线索的越前。
    /// 这是用户要的「默认 5 条、按时间倒序、同一时间相似度优先」。
    /// </summary>
    public static IEnumerable<HumanChainHit> OrderChain(IEnumerable<HumanChainHit> hits)
        => hits
            .OrderByDescending(h => h.Entry.CreatedAt.Date)
            .ThenByDescending(h => h.Score)
            .ThenByDescending(h => h.Entry.CreatedAt);

    /// <summary>从一句记忆里估重要性：出现约定/第一次/重要/承诺这类词就抬高。</summary>
    public static double EstimateImportance(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0.2;
        var t = text;
        double score = 0.45;
        foreach (var kw in new[] { "第一次", "约定", "答应", "承诺", "永远", "重要", "生日", "纪念" })
            if (t.Contains(kw, StringComparison.Ordinal)) score += 0.25;
        foreach (var kw in new[] { "难过", "哭", "吵架", "对不起", "舍不得", "喜欢你", "担心" })
            if (t.Contains(kw, StringComparison.Ordinal)) score += 0.15;
        if (t.Length <= 20) score += 0.05;   // 短句往往是提炼过的关键点
        return Math.Clamp(score, 0.05, 1.0);
    }

    /// <summary>
    /// 按终止标点切句（标点留在句尾）。句级标签要能稳定指回「第几句」，
    /// 所以切法只认标点、不按字数硬切——否则重排一次标签就全错位。
    /// </summary>
    public static List<string> SplitSentences(string? text)
    {
        var outp = new List<string>();
        if (string.IsNullOrWhiteSpace(text)) return outp;
        const string stops = "。！？!?；;…\n\r";
        var sb = new System.Text.StringBuilder();
        foreach (var ch in text)
        {
            sb.Append(ch);
            if (stops.IndexOf(ch) < 0) continue;
            var piece = sb.ToString().Trim();
            if (piece.Length > 0) outp.Add(piece);
            sb.Clear();
        }
        var tail = sb.ToString().Trim();
        if (tail.Length > 0) outp.Add(tail);
        return outp;
    }

    /// <summary>
    /// 自动落句级标签：标签名出现在哪一句就贴哪一句；一句都没命中就贴到第 0 句
    /// （标签总得挂在条上，否则检索得到、界面上却看不见）。
    /// </summary>
    public static List<SentenceTag> AutoTag(string text, IEnumerable<string>? tags)
    {
        var sentences = SplitSentences(text);
        var result = new List<SentenceTag>();
        if (sentences.Count == 0 || tags is null) return result;
        var byIndex = new Dictionary<int, List<string>>();
        foreach (var raw in tags)
        {
            var tag = raw?.Trim() ?? "";
            if (tag.Length == 0) continue;
            var hit = -1;
            for (var i = 0; i < sentences.Count; i++)
                if (sentences[i].Contains(tag, StringComparison.OrdinalIgnoreCase)) { hit = i; break; }
            if (hit < 0) hit = 0;
            if (!byIndex.TryGetValue(hit, out var list)) byIndex[hit] = list = new List<string>();
            if (!list.Contains(tag, StringComparer.OrdinalIgnoreCase)) list.Add(tag);
        }
        foreach (var kv in byIndex.OrderBy(k => k.Key))
            result.Add(new SentenceTag { Index = kv.Key, Tags = kv.Value });
        return result;
    }

    /// <summary>猜一条记忆的类型：带图片路径算图片记忆，含「其实/原来/看来」算抽象结论。</summary>
    public static HumanMemoryKind GuessKind(string text, string imagePath)
    {
        if (!string.IsNullOrWhiteSpace(imagePath)) return HumanMemoryKind.Image;
        if (text.Contains("其实", StringComparison.Ordinal) || text.Contains("原来", StringComparison.Ordinal)
            || text.Contains("看来", StringComparison.Ordinal) || text.Contains("总觉得", StringComparison.Ordinal))
            return HumanMemoryKind.Abstraction;
        return EstimateImportance(text) >= 0.7 ? HumanMemoryKind.Important : HumanMemoryKind.Event;
    }
}
