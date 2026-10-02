namespace WarmAsBefore.Modules.Affection;

/// <summary>
/// 好感度等级：独立于 0-100 好感度的累计积分体系。
/// 每 500 积分升 1 级，共 10 级（上限 5000 积分）。
/// 亲密度及格线等原有 0-100 好感度门控（亲吻≥40 / 结婚≥…）完全不受影响。
/// </summary>
public static class AffectionLevel
{
    public const int PointsPerLevel = 500;
    public const int MaxLevel = 10;
    public const int MaxPoints = PointsPerLevel * MaxLevel;   // 5000

    /// <summary>积分 → 等级（1..10）。</summary>
    public static int LevelOf(int points) => Math.Clamp(points / PointsPerLevel + 1, 1, MaxLevel);

    /// <summary>下一级需要的累计积分（达到则该级升级动画触发）。500/1000/…/5000。</summary>
    public static int NextLevelThreshold(int currentLevel) => Math.Clamp(currentLevel + 1, 1, MaxLevel) * PointsPerLevel;

    /// <summary>距离下一级还差多少积分；10 级封顶返回 0。</summary>
    public static int PointsToNext(int points)
    {
        var level = LevelOf(points);
        if (level >= MaxLevel) return 0;
        return Math.Max(0, level * PointsPerLevel - points);
    }

    /// <summary>当前等级内的进度 (0..1)。</summary>
    public static double LevelProgress(int points)
    {
        var level = LevelOf(points);
        var basePoints = (level - 1) * PointsPerLevel;
        return Math.Clamp((double)(points - basePoints) / PointsPerLevel, 0, 1);
    }

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