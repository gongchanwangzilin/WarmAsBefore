namespace WarmAsBefore.Models;

/// <summary>
/// 角色级设置覆写：所有字段可空，null = 沿用全局设置。
/// 用于让不同角色有各自的记忆与采样参数（有的角色记性好、有的转头就忘）。
/// </summary>
public sealed record CharacterOverrides
{
    /// <summary>带进上下文的最近记忆条数（覆写「设置 → 记忆轮数」）。</summary>
    public int? MemoryTurns { get; init; }

    /// <summary>上下文 Token 上限（覆写「设置 → 上下文上限」）。</summary>
    public int? ContextTokenLimit { get; init; }

    /// <summary>采样温度（覆写「设置 → 温度」）。</summary>
    public double? AiTemperature { get; init; }

    /// <summary>人类记忆调取窗口（天）。null = 全局默认；0 表示不限（可回忆到最早）。</summary>
    public double? MemoryWindowDays { get; init; }

    /// <summary>回忆随机扰动强度 0..1：越大越像人一样跳着想起、偶尔漏掉。</summary>
    public double? MemoryRecallRandomness { get; init; }

    public bool IsEmpty =>
        MemoryTurns is null && ContextTokenLimit is null && AiTemperature is null
        && MemoryWindowDays is null && MemoryRecallRandomness is null;
}
