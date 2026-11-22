using WarmAsBefore.Helpers;

// 好感刻度自测：0–1000 点数 / 10 级制 / 旧档 ×10 迁移，全是纯函数。
// 跑法：dotnet run --project tools/affection_selftest
// 不启动应用、不联网：只把 AffectionScale 按边界值按住对一遍。

var pass = 0;
var failures = new List<string>();

void Check(bool ok, string name, string detail = "")
{
    if (ok) pass++;
    else failures.Add(name + (detail.Length > 0 ? "  " + detail : ""));
}

void Eq(int actual, int expected, string name)
    => Check(actual == expected, name, $"实际 {actual} / 期望 {expected}");

void Near(double actual, double expected, string name)
    => Check(Math.Abs(actual - expected) < 1e-9, name, $"实际 {actual} / 期望 {expected}");

void Text(string actual, string expected, string name)
    => Check(actual == expected, name, $"实际「{actual}」/ 期望「{expected}」");

// ==================== 1. 等级：边界值逐个按 ====================

Console.WriteLine("== 1. LevelOf（0 / 99 / 100 / 101 / 950 / 999 / 1000 / 1001 / -5） ==");
Eq(AffectionScale.LevelOf(0), 1, "0 点 → Lv.1（刚认识也有级）");
Eq(AffectionScale.LevelOf(99), 1, "99 点 → Lv.1");
Eq(AffectionScale.LevelOf(100), 1, "100 点 → 仍是 Lv.1，本级刚满");
Eq(AffectionScale.LevelOf(101), 2, "101 点 → Lv.2");
Eq(AffectionScale.LevelOf(240), 3, "240 点 → Lv.3（示例值）");
Eq(AffectionScale.LevelOf(950), 10, "950 点 → Lv.10");
Eq(AffectionScale.LevelOf(999), 10, "999 点 → Lv.10");
Eq(AffectionScale.LevelOf(1000), 10, "1000 点 → Lv.10（封顶，不出现 11）");
Eq(AffectionScale.LevelOf(1001), 10, "1001 点越界 → 仍 Lv.10");
Eq(AffectionScale.LevelOf(-5), 1, "-5 点 → Lv.1（不出现 0 级）");
Eq(AffectionScale.LevelOf(int.MaxValue), 10, "int.Max 不溢出、不越级");

// ==================== 2. 本级进度 ====================

Console.WriteLine("== 2. ProgressInLevel / IntoLevel（本级进度 0..1） ==");
Near(AffectionScale.ProgressInLevel(0), 0.0, "0 点 → 本级 0%");
Near(AffectionScale.ProgressInLevel(99), 0.99, "99 点 → 本级 99%");
Near(AffectionScale.ProgressInLevel(100), 1.0, "100 点 → 本级 100%");
Near(AffectionScale.ProgressInLevel(101), 0.01, "101 点 → 掉回本级 1%（换级）");
Near(AffectionScale.ProgressInLevel(240), 0.40, "240 点 → 本级 40%");
Near(AffectionScale.ProgressInLevel(950), 0.50, "950 点 → 本级 50%");
Near(AffectionScale.ProgressInLevel(999), 0.99, "999 点 → 本级 99%");
Near(AffectionScale.ProgressInLevel(1000), 1.0, "1000 点 → 满级 100%");
Near(AffectionScale.ProgressInLevel(1001), 1.0, "1001 点越界 → 仍 100%，不倒转");
Near(AffectionScale.ProgressInLevel(-5), 0.0, "-5 点 → 0%");
Eq(AffectionScale.IntoLevel(240), 40, "240 点本级已走 40 点");
Eq(AffectionScale.IntoLevel(1000), 100, "1000 点本级已走 100 点");

foreach (var p in new[] { -1000, -1, 0, 1, 50, 99, 100, 101, 499, 500, 950, 999, 1000, 1001, 123456 })
{
    var level = AffectionScale.LevelOf(p);
    var progress = AffectionScale.ProgressInLevel(p);
    Check(level is >= 1 and <= 10, $"不变式：任一点数的等级都在 1..10（{p} → {level}）");
    Check(progress is >= 0 and <= 1, $"不变式：任一点数的进度都在 0..1（{p} → {progress}）");
}

// ==================== 3. 等级上下界 ====================

Console.WriteLine("== 3. CapFor / FloorOf ==");
Eq(AffectionScale.CapFor(1), 100, "Lv.1 满值 100");
Eq(AffectionScale.CapFor(3), 300, "Lv.3 满值 300");
Eq(AffectionScale.CapFor(10), 1000, "Lv.10 满值 1000");
Eq(AffectionScale.CapFor(99), 1000, "越界等级夹到 Lv.10");
Eq(AffectionScale.CapFor(0), 100, "0 级夹到 Lv.1");
Eq(AffectionScale.FloorOf(1), 0, "Lv.1 从 0 开始");
Eq(AffectionScale.FloorOf(3), 200, "Lv.3 从 200 开始");
Eq(AffectionScale.FloorOf(10), 900, "Lv.10 从 900 开始");
for (var lv = 1; lv <= 10; lv++)
{
    // 本级起点必须落在本级；本级满值必须「本级满、下一级起点」
    Eq(AffectionScale.LevelOf(AffectionScale.FloorOf(lv) + 1), lv, $"Lv.{lv} 起点+1 点属于 Lv.{lv}");
    Eq(AffectionScale.LevelOf(AffectionScale.CapFor(lv)), lv, $"Lv.{lv} 满值 {AffectionScale.CapFor(lv)} 仍属于 Lv.{lv}");
    if (lv < AffectionScale.MaxLevel)
        Eq(AffectionScale.LevelOf(AffectionScale.CapFor(lv) + 1), lv + 1, $"Lv.{lv} 满值+1 点进 Lv.{lv + 1}");
}

// ==================== 4. 文本 ====================

Console.WriteLine("== 4. TextOf ==");
Text(AffectionScale.TextOf(0), "Lv.1 · 0/1000", "0 点显示");
Text(AffectionScale.TextOf(240), "Lv.3 · 240/1000", "240 点显示（需求里的示例）");
Text(AffectionScale.TextOf(1000), "Lv.10 · 1000/1000", "满点显示");
Text(AffectionScale.TextOf(1001), "Lv.10 · 1000/1000", "越界不显示 1001");
Text(AffectionScale.TextOf(-5), "Lv.1 · 0/1000", "负数不显示 -5");
Text(AffectionScale.LevelTextOf(240), "Lv.3", "短标");

// ==================== 5. 旧档迁移（0–100 → 0–1000） ====================

Console.WriteLine("== 5. Migrate / FromLegacy / ToLegacy ==");
Eq(AffectionScale.Migrate(0), 0, "旧 0 → 0");
Eq(AffectionScale.Migrate(1), 10, "旧 1 → 10");
Eq(AffectionScale.Migrate(50), 500, "旧 50（一半）→ 500（仍是第 5 级的一半）");
Eq(AffectionScale.Migrate(99), 990, "旧 99 → 990");
Eq(AffectionScale.Migrate(100), 1000, "旧 100（原本满）→ 1000（仍满）");
Eq(AffectionScale.Migrate(180), 1000, "旧 180 越界 → 夹到 1000");
Eq(AffectionScale.Migrate(-3), 0, "旧 -3 → 0");
Eq(AffectionScale.Migrate(int.MaxValue / 3), 1000, "极大旧值不溢出、夹到 1000");
Eq(AffectionScale.LevelOf(AffectionScale.Migrate(100)), 10, "迁移后满级仍是满级");
Eq(AffectionScale.LevelOf(AffectionScale.Migrate(1)), 1, "旧 1 迁移后 Lv.1");
Eq(AffectionScale.ToLegacy(240), 24, "240 点折回旧刻度 24");
Eq(AffectionScale.ToLegacy(1000), 100, "满点折回旧刻度 100");
Eq(AffectionScale.ToLegacy(1001), 100, "越界折回不超 100");
Eq(AffectionScale.ToLegacy(-5), 0, "负数折回 0");
Eq(AffectionScale.FromLegacy(40), 400, "旧阈值 40（亲吻门槛）→ 400 点");
Eq(AffectionScale.FromLegacy(-3), -30, "旧负增量 -3 → -30 点（增量不夹，由累加方夹）");
Eq(AffectionScale.LevelOf(AffectionScale.FromLegacy(40)), 4, "亲吻门槛 40% → 400 点 = 整整 4 级走满");

// 迁移幂等边界：迁移结果必须已经在合法区间里，二次夹不会变。
foreach (var legacy in new[] { -10, 0, 1, 10, 37, 50, 73, 99, 100 })
{
    var once = AffectionScale.Migrate(legacy);
    Eq(AffectionScale.Clamp(once), once, $"迁移结果本身合法（旧 {legacy} → {once}）");
    Check(AffectionScale.LevelOf(once) >= 1 && AffectionScale.LevelOf(once) <= 10,
        $"迁移后等级合法（旧 {legacy} → {once} → Lv.{AffectionScale.LevelOf(once)}）");
}

// ==================== 6. Clamp ====================

Console.WriteLine("== 6. Clamp ==");
Eq(AffectionScale.Clamp(-5), 0, "-5 夹到 0");
Eq(AffectionScale.Clamp(0), 0, "0 不动");
Eq(AffectionScale.Clamp(1000), 1000, "1000 不动");
Eq(AffectionScale.Clamp(1001), 1000, "1001 夹到 1000");
Eq(AffectionScale.Clamp(9999), 1000, "9999 夹到 1000");
Eq(AffectionScale.Clamp(int.MinValue), 0, "int.Min 夹到 0");

// ==================== 收尾 ====================

Console.WriteLine();
if (failures.Count == 0)
{
    Console.WriteLine($"PASS  {pass} 项全部通过");
    return 0;
}
Console.WriteLine($"FAIL  {failures.Count} 项失败 / 共 {pass + failures.Count} 项");
foreach (var f in failures) Console.WriteLine("  - " + f);
return 1;
