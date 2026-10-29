namespace WarmAsBefore.Models;

/// <summary>
/// 模型单价：按「每百万 token」计价（各家 API 通行的报价单位）。
/// 填了单价才谈得上算钱——没填的模型只统计 token，不算金额。
/// </summary>
public sealed record ModelPrice
{
    public string Model { get; init; } = "";

    /// <summary>输入（提示）单价：每百万 token 的价格。</summary>
    public double InputPerMillion { get; init; }

    /// <summary>输出（补全）单价：每百万 token 的价格。</summary>
    public double OutputPerMillion { get; init; }

    /// <summary>缓存输入单价：命中缓存的那部分输入，各家普遍只有原价的一到两成。</summary>
    public double CachedInputPerMillion { get; init; }

    /// <summary>缓存/思考输出单价：reasoning 这类单独计费的输出档；没有就留 0。</summary>
    public double CachedOutputPerMillion { get; init; }
}

/// <summary>一次请求的真实 token 消耗记录（端点返回 usage 时才有）。</summary>
public sealed record TokenUsageRecord
{
    public string Model { get; init; } = "";
    public int PromptTokens { get; init; }
    public int CompletionTokens { get; init; }
    public DateTime At { get; init; } = DateTime.UtcNow;

    /// <summary>本次是「折叠总结」调用（用于区分总结本身的花费与正常对话）。</summary>
    public bool IsSummary { get; init; }

    /// <summary>
    /// 命中缓存的输入 token。它是 PromptTokens 的子集（账已经算在输入里了），
    /// 记下来是为了把这部分从「输入价」改按「缓存价」重算，不是再补一笔钱。
    /// 老账本没有这个字段，反序列化后按 0 处理即可。
    /// </summary>
    public int CachedInputTokens { get; init; }

    /// <summary>单独计费的缓存/思考输出 token（OpenAI 口径的 reasoning_tokens）。</summary>
    public int CachedOutputTokens { get; init; }
}
