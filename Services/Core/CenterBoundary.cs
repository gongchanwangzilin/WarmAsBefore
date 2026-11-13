namespace WarmAsBefore.Services.Core;

/// <summary>
/// F29「中枢分层」的编译期边界。论文把心智分成两级：
///
/// · <b>低级中枢</b> = 程序规则（纯 C# 确定性算法）+ 内置本地小模型。它算情绪、激素、
///   躯体化、精力、记忆衰减、病理状态机，<b>只能改状态包与给建议</b>。
/// · <b>高级中枢</b> = 云端大模型（含其内部的旁支小模型）。只有它能把状态包演成
///   <b>最终回复</b>。
///
/// 边界靠类型落实：<see cref="ILowLevelCenter.Observe"/> 的返回类型
/// <see cref="StatePack"/> 里根本不存在「最终回复」字段，低级中枢在编译期就无法越权。
/// 唯一例外是<see cref="StatePack.CircuitBreak"/>（低级中枢熔断，如 PTSD 急性发作），
/// 只允许出一句短句、且必须由高级中枢决定是否采纳——不绕过它直接发给用户。
///
/// 注意：项目里的 <c>SmallModelFirst</c> 指的是「云端便宜快模型，先答再升级」，
/// 属于高级中枢内部的旁支，不是本文件说的低级中枢；论文里那套自研量化小模型
/// 是内置在构建里的低级中枢，不下载、不经过 <c>ApiGateway</c>。
/// </summary>
public static class CenterBoundary
{
    /// <summary>一条状态行允许的最大长度：防止低级中枢把整段回复塞进「状态」里越权。</summary>
    public const int MaxLineChars = 160;

    /// <summary>熔断短句的最大长度：熔断只能是一句，不能变成绕开高级中枢的回复通道。</summary>
    public const int MaxCircuitBreakChars = 48;

    /// <summary>
    /// 状态包校验：低级中枢交上来的东西必须是「描述 / 建议」，不能是成品回复。
    /// 返回可直接进提示词的安全文本；违规行被丢弃（并可由调用方写日志）。
    /// </summary>
    public static IReadOnlyList<string> SafeLines(StatePack pack)
    {
        if (pack is null) return Array.Empty<string>();
        var result = new List<string>();
        foreach (var line in pack.Lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var s = line.Trim();
            if (LooksLikeFinalReply(s)) continue;
            result.Add(s.Length <= MaxLineChars ? s : s[..MaxLineChars]);
        }
        return result;
    }

    /// <summary>低级中枢熔断短句：同样限长、同样不许是成品回复。</summary>
    public static string? SafeCircuitBreak(StatePack pack)
    {
        var s = pack?.CircuitBreakLine?.Trim();
        if (string.IsNullOrWhiteSpace(s) || pack?.CircuitBreak != true) return null;
        if (LooksLikeFinalReply(s)) return null;
        return s.Length <= MaxCircuitBreakChars ? s : s[..MaxCircuitBreakChars];
    }

    private static bool LooksLikeFinalReply(string s)
        // 状态描述不带称呼/第一人称成句寒暄；这里只拦最明显的「把回复塞进状态包」形态。
        => s.Length > MaxLineChars * 2
           || s.StartsWith("【回复】", StringComparison.Ordinal)
           || s.StartsWith("assistant:", StringComparison.OrdinalIgnoreCase);
}

/// <summary>低级中枢的输入：只读上下文，不含任何可用于发消息的句柄。</summary>
public sealed record CenterContext(string CharId, DateTime Now);

/// <summary>
/// 状态包：低级中枢<b>唯一</b>被允许产出的东西。
/// 只有描述、建议与熔断标记；没有最终回复、没有工具调用、没有网络/存储句柄。
/// </summary>
public sealed record StatePack
{
    /// <summary>渗进提示词的状态描述（「胃里有点发紧」「有点提不起劲」），不是给用户看的话。</summary>
    public List<string> Lines { get; init; } = new();

    /// <summary>给高级中枢的行为建议（「这轮话少一点」），由高级中枢自行决定采不采纳。</summary>
    public List<string> Advices { get; init; } = new();

    /// <summary>
    /// 低级中枢熔断：病理状态机急性发作时允许提一句短句，但只是<paramref name="CircuitBreakLine"/>，
    /// 仍要经高级中枢组织语言。PTSD/惊恐发作走这里，不直接发消息。
    /// </summary>
    public bool CircuitBreak { get; init; }

    public string? CircuitBreakLine { get; init; }

    public string Source { get; init; } = "";
}

/// <summary>
/// 低级中枢：程序规则 + 内置本地小模型。零模型调用优先，全部判定确定性可单测。
/// 契约上只能返回 <see cref="StatePack"/>——不可能产出最终回复。
/// </summary>
public interface ILowLevelCenter
{
    string Id { get; }
    StatePack Observe(CenterContext ctx);
}

/// <summary>
/// 高级中枢：云端大模型（及其内部旁支小模型）。<b>唯一</b>被允许生成最终回复的一层。
/// </summary>
public interface IHighLevelCenter
{
    /// <summary>把用户这句话，连同低级中枢的状态包，演成最终回复。</summary>
    Task<string> ReplyAsync(string charId, string text, StatePack state, CancellationToken ct = default);
}
