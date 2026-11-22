namespace WarmAsBefore.Helpers;

/// <summary>
/// 好感刻度：内部一律存绝对点数（0–1000），对外显示成「Lv.3 · 240/1000」。
///
/// 规则（唯一事实来源，所有显示/判定都从这里取，不许各算一套）：
/// · 每满 100 点升 1 级，共 10 级，所以 1000 点 = 满级。
/// · 正好 100 点仍算 1 级且本级进度 100%；101 点才进第 2 级（本级进度 1%）。
///   这样「本级还差多少」在边界上是连续的：满 100 就该看见满格，下一跳才换级。
/// · 1 级区间 [0,100]，N 级区间 ((N-1)×100, N×100]。
///
/// 旧刻度（0–100 的「百分比好感」）没有消失，而是变成边界换算：
/// <see cref="FromLegacy"/> 把旧值/旧增量（×10）折成点数，<see cref="ToLegacy"/> 把点数折回 0–100，
/// 于是 AI 判定的 ±n、心意门槛（亲吻 ≥40）、流程条件（好感 &gt;= 80）都保持原意，不必重调数值。
///
/// 零依赖纯函数：不碰 IO、不碰时间、不碰 MAUI，可直接编进 tools 下的自测工程。
/// </summary>
public static class AffectionScale
{
    /// <summary>一个等级要多少点。</summary>
    public const int PerLevel = 100;
    /// <summary>最高等级。</summary>
    public const int MaxLevel = 10;
    /// <summary>点数上限（满级）。</summary>
    public const int MaxPoints = PerLevel * MaxLevel;

    /// <summary>旧档的满值：0–100 的百分比刻度。</summary>
    public const int LegacyMax = 100;

    /// <summary>折成合法点数：负数按 0、超过 1000 按 1000。</summary>
    public static int Clamp(int points) => Math.Clamp(points, 0, MaxPoints);

    /// <summary>点数 → 等级（1..10）。0 点也算第 1 级 —— 刚认识不是「0 级」。</summary>
    public static int LevelOf(int points)
    {
        var p = Clamp(points);
        return p <= 0 ? 1 : Math.Min(MaxLevel, (p - 1) / PerLevel + 1);
    }

    /// <summary>这一级满值对应的累计点数：Lv.1→100、Lv.3→300、Lv.10→1000。越界等级先被夹。</summary>
    public static int CapFor(int level) => Math.Clamp(level, 1, MaxLevel) * PerLevel;

    /// <summary>这一级从多少点开始：Lv.1→0、Lv.3→200、Lv.10→900。</summary>
    public static int FloorOf(int level) => (Math.Clamp(level, 1, MaxLevel) - 1) * PerLevel;

    /// <summary>已经走掉的本级点数（0..100）。</summary>
    public static int IntoLevel(int points) => Clamp(points) - FloorOf(LevelOf(points));

    /// <summary>本级进度（0..1），满级满点时正好 1.0。</summary>
    public static double ProgressInLevel(int points) => IntoLevel(points) / (double)PerLevel;

    /// <summary>「Lv.3 · 240/1000」—— 主界面 / 监测 / 角色库 / 升级页 / 回忆录共用这一条。</summary>
    public static string TextOf(int points) => $"Lv.{LevelOf(points)} · {Clamp(points)}/{MaxPoints}";

    /// <summary>「Lv.3」短标（空间只够放级数的地方，比如游戏页的方块角标）。</summary>
    public static string LevelTextOf(int points) => $"Lv.{LevelOf(points)}";

    /// <summary>
    /// 旧 0–100 刻度 → 点数（×10，不夹）。两处用它：
    /// ① 读旧档时把存量的 0–100 折成 0–1000（<see cref="Migrate"/> 再夹一次）；
    /// ② 边界上的旧增量/旧阈值（AI 判定的 ±n、亲吻 ≥40、流程条件 ≥80）保持原数值，只在这里换刻度。
    /// </summary>
    public static int FromLegacy(int legacy) => (int)Math.Clamp(legacy * 10L, int.MinValue, int.MaxValue);

    /// <summary>点数 → 旧 0–100 刻度（向下取整）。给流程条件、AI 提示词这些仍按百分比说话的地方。</summary>
    public static int ToLegacy(int points) => Clamp(points) / 10;

    /// <summary>
    /// 旧档迁移：0–100 的存量点数 ×10 并夹进 0–1000。旧 100（原本满）→ 1000（Lv.10，仍然满），
    /// 旧 50 → 500（Lv.5），旧的「一半」不会因为换了刻度变成「百分之五」。
    /// </summary>
    public static int Migrate(int legacyPoints) => Clamp(FromLegacy(legacyPoints));
}
