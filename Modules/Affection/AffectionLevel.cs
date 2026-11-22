using WarmAsBefore.Helpers;

namespace WarmAsBefore.Modules.Affection;

/// <summary>
/// 好感等级：10 级制，每 100 点 1 级（0–1000）。刻度算法唯一实现在
/// <see cref="AffectionScale"/>（纯函数、可自测），这里只留等级称号，并把老调用点转过去——
/// 曾经这里是「500 点 1 级 / 上限 5000」的第二套刻度，两套刻度并存必然各算各的。
///
/// 原有 0–100 好感门控（亲吻 ≥40 等）不按等级重调：它们仍按旧百分比说话，
/// 只在边界上用 <see cref="AffectionScale.FromLegacy"/> 换成点数。
/// </summary>
public static class AffectionLevel
{
    public const int PointsPerLevel = AffectionScale.PerLevel;
    public const int MaxLevel = AffectionScale.MaxLevel;
    public const int MaxPoints = AffectionScale.MaxPoints;

    /// <summary>点数 → 等级（1..10）。</summary>
    public static int LevelOf(int points) => AffectionScale.LevelOf(points);

    /// <summary>下一级需要的累计点数（达到则该级升级动画触发）。100/200/…/1000。</summary>
    public static int NextLevelThreshold(int currentLevel) => AffectionScale.CapFor(currentLevel + 1);

    /// <summary>距离下一级还差多少点；10 级封顶返回 0。</summary>
    public static int PointsToNext(int points)
    {
        var level = AffectionScale.LevelOf(points);
        return level >= MaxLevel ? 0 : Math.Max(0, AffectionScale.CapFor(level) - AffectionScale.Clamp(points));
    }

    /// <summary>当前等级内的进度 (0..1)。</summary>
    public static double LevelProgress(int points) => AffectionScale.ProgressInLevel(points);

    /// <summary>等级称号（用于动画/收藏展示）。</summary>
    public static string TitleOf(int level) => level switch
    {
        1 => "初识",
        2 => "相识",
        3 => "熟悉",
        4 => "亲近",
        5 => "心动",
        6 => "温柔",
        7 => "依恋",
        8 => "深情",
        9 => "挚爱",
        10 => "永恒",
        _ => "羁绊"
    };
}
