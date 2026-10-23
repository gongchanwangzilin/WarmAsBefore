namespace WarmAsBefore.Models;

/// <summary>配比里的一项：一种情绪占几成。</summary>
public sealed record EmotionPart
{
    public string Name { get; init; } = "";

    /// <summary>0~1，整份配比加起来等于 1。</summary>
    public double Ratio { get; init; }
}

/// <summary>
/// 一份情绪：不是「开心」或「难过」一个词，而是几样掺在一起的比例。
///
/// 人本来就是这样：停电那晚躲在人后面，是害怕三成就赖两成就安心五成；
/// 只记一个词，下次想起那晚就只剩「害怕」，把最想留住的那点暖味弄丢了。
/// 所以这里存的是配比，画出来是一个分扇面的小饼，说得出「几分几分」。
///
/// 情绪词表固定 16 个（<see cref="EmotionWords"/>）：词表一散，
/// 「开心 / 高兴 / 愉快」会长成三个互不相连的点，网就碎成同义词堆了。
/// </summary>
public sealed class EmotionMix
{
    public List<EmotionPart> Parts { get; set; } = new();

    public bool IsEmpty => Parts.Count == 0 || Parts.All(p => p.Ratio <= 0.001);

    /// <summary>占得最多的那一个：列表标题、桌宠表情这些只能显示一个词的地方用它。</summary>
    public string Dominant => IsEmpty ? "" : Parts.OrderByDescending(p => p.Ratio).ThenBy(p => p.Name, StringComparer.Ordinal).First().Name;

    public double RatioOf(string name) => Parts.FirstOrDefault(p => p.Name == name)?.Ratio ?? 0;

    /// <summary>人话：安心 5 成 · 依赖 3 成 · 害怕 2 成。</summary>
    public string Text
    {
        get
        {
            if (IsEmpty) return "";
            var top = Parts.Where(p => p.Ratio > 0.001)
                .OrderByDescending(p => p.Ratio)
                .ThenBy(p => p.Name, StringComparer.Ordinal)
                .Take(3)
                .Select(p => $"{p.Name}{(int)Math.Round(p.Ratio * 10)}成");
            return string.Join(" · ", top);
        }
    }

    /// <summary>把任意一组原始分数摊成和为 1 的配比，只留前 <paramref name="keep"/> 项（其余太小，画出来是噪点）。</summary>
    public static EmotionMix Normalize(IReadOnlyDictionary<string, double> raw, int keep = 3)
    {
        var clean = (raw ?? new Dictionary<string, double>())
            .Where(kv => kv.Value > 0 && EmotionWords.Known(kv.Key))
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Take(Math.Max(1, keep))
            .ToList();
        var sum = clean.Sum(kv => kv.Value);
        if (sum <= 0) return new EmotionMix();
        return new EmotionMix
        {
            Parts = clean.Select(kv => new EmotionPart { Name = kv.Key, Ratio = Math.Round(kv.Value / sum, 3) }).ToList()
        };
    }

    /// <summary>从一段话里读出来：本地词表打分，不花钱、不依赖模型，模型在的时候可以被它覆盖。</summary>
    public static EmotionMix FromText(string? text, int keep = 3)
    {
        var score = new Dictionary<string, double>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(text)) return new EmotionMix();
        foreach (var (cue, name, w) in EmotionWords.Cues)
        {
            if (text.IndexOf(cue, StringComparison.Ordinal) < 0) continue;
            score[name] = (score.TryGetValue(name, out var old) ? old : 0) + w;
        }
        // 一句话里最常见的写法是「又 X 又 Y」：命中几个就给几个，别只留第一个
        return Normalize(score, keep);
    }

    /// <summary>几份配比按次数加权合成一份（今天一整天的心情 = 每件事的配比按大小合起来）。</summary>
    public static EmotionMix Merge(IEnumerable<(EmotionMix Mix, double Weight)> items, int keep = 3)
    {
        var acc = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var (mix, w) in items)
        {
            if (mix is null) continue;
            foreach (var p in mix.Parts) acc[p.Name] = (acc.TryGetValue(p.Name, out var old) ? old : 0) + p.Ratio * Math.Max(0, w);
        }
        return Normalize(acc, keep);
    }

    /// <summary>两份配比像不像（余弦）：用来判断「今天的情绪和那天像不像」，而不是比字符串。</summary>
    public double SimilarityTo(EmotionMix? other)
    {
        if (other is null || IsEmpty || other.IsEmpty) return 0;
        double dot = 0, na = 0, nb = 0;
        foreach (var a in Parts)
        {
            na += a.Ratio * a.Ratio;
            var b = other.RatioOf(a.Name);
            if (b > 0) dot += a.Ratio * b;
        }
        foreach (var b in other.Parts) nb += b.Ratio * b.Ratio;
        if (na <= 1e-9 || nb <= 1e-9) return 0;
        return Math.Clamp(dot / (Math.Sqrt(na) * Math.Sqrt(nb)), 0, 1);
    }
}

/// <summary>
/// 情绪基表：16 个词，每个配一个颜色（画饼用）和一组线索词（本地读心用）。
/// 词表小是故意的 —— 网里的每个情绪点都要能被认出来、被数出来，词一多全是孤点。
/// </summary>
public static class EmotionWords
{
    /// <summary>名字 → 颜色（#RRGGBB）。</summary>
    public static readonly IReadOnlyDictionary<string, string> Colors = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["安心"] = "#8FBF9F", ["开心"] = "#E8B87A", ["害羞"] = "#F2C4D6", ["心疼"] = "#C37F98",
        ["想念"] = "#A78BD0", ["害怕"] = "#7C8CA8", ["委屈"] = "#B5838D", ["依赖"] = "#E0A899",
        ["紧张"] = "#C9A227", ["难过"] = "#8A9BA8", ["感激"] = "#7FB069", ["愧疚"] = "#9C8AA6",
        ["孤独"] = "#6E7F80", ["期待"] = "#F0A868", ["平静"] = "#9CB4B8", ["不安"] = "#A88B7A"
    };

    public static bool Known(string name) => Colors.ContainsKey(name);

    public static string ColorOf(string name) => Colors.TryGetValue(name, out var c) ? c : "#C37F98";

    /// <summary>线索词 → 情绪：（说法, 情绪, 权重）。命中多个词就多个情绪叠加，这就是配比的来源。</summary>
    public static readonly IReadOnlyList<(string Cue, string Name, double Weight)> Cues = new (string, string, double)[]
    {
        ("安心", "安心", 1.0), ("踏实", "安心", 0.9), ("放心", "安心", 0.9), ("怀里", "安心", 0.6), ("抱着睡", "安心", 0.8),
        ("开心", "开心", 1.0), ("高兴", "开心", 0.9), ("笑", "开心", 0.7), ("好玩", "开心", 0.7), ("甜", "开心", 0.5),
        ("害羞", "害羞", 1.0), ("脸红", "害羞", 0.9), ("不好意思", "害羞", 0.8),
        ("心疼", "心疼", 1.0), ("舍不得", "心疼", 0.9), ("累坏", "心疼", 0.6),
        ("想你", "想念", 0.9), ("想念", "想念", 1.0), ("好久没", "想念", 0.8), ("等你", "想念", 0.6), ("见不到", "想念", 0.9),
        ("害怕", "害怕", 1.0), ("怕", "害怕", 0.8), ("吓", "害怕", 0.9), ("不敢", "害怕", 0.6), ("黑", "害怕", 0.3), ("打雷", "害怕", 0.6),
        ("委屈", "委屈", 1.0), ("凭什么", "委屈", 0.8), ("被骂", "委屈", 0.8), ("冤枉", "委屈", 0.9),
        ("依赖", "依赖", 1.0), ("黏", "依赖", 0.9), ("只有你", "依赖", 0.8), ("躲在我后面", "依赖", 0.9), ("拉着我", "依赖", 0.7),
        ("紧张", "紧张", 1.0), ("手心出汗", "紧张", 0.9), ("心跳很快", "紧张", 0.8), ("考试", "紧张", 0.5),
        ("难过", "难过", 1.0), ("哭", "难过", 0.9), ("伤心", "难过", 0.9), ("眼泪", "难过", 0.8), ("下雨", "难过", 0.2),
        ("谢谢", "感激", 0.9), ("亏欠", "感激", 0.6), ("帮我", "感激", 0.5),
        ("对不起", "愧疚", 0.9), ("是我不好", "愧疚", 1.0), ("搞砸", "愧疚", 0.8), ("忘了", "愧疚", 0.5),
        ("一个人", "孤独", 0.7), ("没人", "孤独", 0.8), ("空荡", "孤独", 0.7), ("孤单", "孤独", 1.0),
        ("想要", "期待", 0.5), ("期待", "期待", 1.0), ("明天", "期待", 0.4), ("终于", "期待", 0.5), ("约定", "期待", 0.6),
        ("平静", "平静", 1.0), ("安静", "平静", 0.7), ("平常", "平静", 0.5), ("慢慢", "平静", 0.4),
        ("不安", "不安", 1.0), ("担心", "不安", 0.9), ("慌", "不安", 0.8), ("不对劲", "不安", 0.7)
    };
}
