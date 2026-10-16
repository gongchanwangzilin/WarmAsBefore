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
}
