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

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime LastRecalledAt { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// 人类记忆的打分与衰减：纯函数，便于单独验证。
///
/// 衰减尺度取「调取窗口 / 3」：默认窗口 3 天时，3 天前的记忆权重约降到 37%，
/// 9 天前约 5%，符合「越久越模糊但不会彻底消失」；窗口设为 0（不限）时衰减不生效。
/// </summary>
public static class HumanMemoryMath
{
    public const double DefaultWindowDays = 3;
    public const double DefaultRandomness = 0.25;

    /// <summary>低于这个权重的记忆不再进入检索结果，避免几十条陈旧记忆把上下文撑满。</summary>
    public const double MinWeight = 0.02;

    public static double Decay(double ageDays, double windowDays)
    {
        if (windowDays <= 0) return 1.0;              // 不限窗口：只由重要性决定
        if (ageDays <= 0) return 1.0;
        // 时间常数取「调取窗口」本身：默认 3 天时，3 天前约 37%、9 天前约 5%——
        // 也就是刚过窗口还记着，久了才淡。（曾经写成 windowDays/3，衰减快 3 倍，记忆几乎不留。）
        var tau = windowDays;
        return Math.Exp(-ageDays / tau);
    }

    public static double Score(HumanMemoryEntry e, double windowDays, double randomness, DateTime now)
    {
        var ageDays = Math.Max(0, (now - e.CreatedAt).TotalDays);
        var importance = Math.Clamp(e.Importance, 0.05, 1.0);
        var repetition = 1 + Math.Log(1 + Math.Max(0, e.RecallCount)) * 0.5;
        var jitter = 1 + Math.Clamp(randomness, 0, 1) * (Random.Shared.NextDouble() * 2 - 1);
        return Math.Max(0, importance * Decay(ageDays, windowDays) * repetition * jitter);
    }

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
