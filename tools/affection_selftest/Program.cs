using WarmAsBefore.Helpers;
using WarmAsBefore.Modules.Affection;
using SecKind = WarmAsBefore.Modules.Affection.SecurityEventKind;
using SecurityScale = WarmAsBefore.Modules.Affection.SecurityScale;

// 好感与安全感刻度自测：0–1000 点数 / 10 级制 / 旧档迁移 / 事件分与夹取，全是纯函数。
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

int D(SecKind kind, bool deficit) => SecurityScale.DeltaOf(kind, deficit);
int Mig(int stored) => SecurityScale.Migrate(stored);

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


// ==================== 7. 安全感：事件 → 分值 ====================

Console.WriteLine("== 7. 安全感事件基础分与特质倍率 ==");
Eq(SecurityScale.Events.Count, 8, "事件表共 8 类（安慰/倾听/忽视/拒绝触碰/目睹亲密/辱骂/独处/陪伴）");

Eq(D(SecKind.Comforted, false), 18, "被安慰 +18（无特质）");
Eq(D(SecKind.Listened, false), 12, "被倾听 +12");
Eq(D(SecKind.Ignored, false), -20, "被忽视 −20");
Eq(D(SecKind.TouchRejected, false), -28, "被拒绝抱抱/触碰 −28");
Eq(D(SecKind.RivalIntimacy, false), -40, "目睹主人对别人亲密 −40");
Eq(D(SecKind.Abuse, false), -65, "被辱骂/威胁 −65（负得最狠）");
Eq(D(SecKind.Solitude, false), -4, "独处过久每段 −4");
Eq(D(SecKind.Company, false), 5, "稳定陪伴每段 +5");

Eq(D(SecKind.Abuse, true), -104, "安全感缺失：流失 ×1.6（−65 → −104）");
Eq(D(SecKind.Ignored, true), -32, "安全感缺失：−20 → −32");
Eq(D(SecKind.Comforted, true), 11, "安全感缺失：回复 ×0.6（+18 → +11）");
Eq(D(SecKind.Company, true), 3, "安全感缺失：+5 → +3");
Near(SecurityScale.LossRate(true), 1.6, "流失倍率常量 1.6");
Near(SecurityScale.RecoverRate(true), 0.6, "回复倍率常量 0.6");
Near(SecurityScale.LossRate(false), 1.0, "没有特质时流失 1.0");

// ==================== 8. 安全感：迁移与夹取 ====================

Console.WriteLine("== 8. 安全感迁移（旧档 600）与边界夹取 ==");
Eq(Mig(SecurityScale.Unset), 600, "没写过（-1）→ 默认 600，不是满值");
Eq(SecurityScale.DefaultOnMigrate, 600, "默认值常量就是 600");
Eq(Mig(0), 0, "0 是真值（彻底塌了），不当缺失");
Eq(Mig(600), 600, "600 原样保留");
Eq(Mig(1000), 1000, "满值不动");
Eq(Mig(1001), 1000, "越界夹到 1000");
Eq(Mig(-5), 600, "更小的负值一律按未写入补 600");
Eq(SecurityScale.Clamp(-1), 0, "Clamp 与好感同一个边界（-1 → 0）");

Eq(SecurityScale.Apply(990, 40), 1000, "990 + 40 夹到 1000");
Eq(SecurityScale.Apply(10, -40), 0, "10 − 40 夹到 0");
Eq(SecurityScale.Apply(600, 11), 611, "正常增量直接累加");
Eq(SecurityScale.Apply(SecurityScale.Unset, -100), 500, "未写入的值先补 600 再落增量");

Eq(SecurityScale.LevelOf(600), AffectionScale.LevelOf(600), "等级换算与好感同一套（600 → Lv.6）");
Eq(SecurityScale.LevelOf(600), 6, "600 点 = Lv.6");
Eq(SecurityScale.LevelOf(0), 1, "0 点也是 Lv.1（没有 0 级）");
Text(SecurityScale.TextOf(600), "Lv.6 · 600/1000", "显示与好感同一格式");
Eq(SecurityScale.MaxPoints, AffectionScale.MaxPoints, "上限与好感一致");

// ==================== 9. 信任 / 经历作为中间量 ====================

Console.WriteLine("== 9. 信任与经历的权重（中间量，不新增可见数值） ==");
Near(SecurityScale.TrustWeight(10, 0), 0.8, "信任 0 → 权重 0.8");
Near(SecurityScale.TrustWeight(10, 50), 1.0, "信任 50 → 权重 1.0");
Near(SecurityScale.TrustWeight(-10, 100), 1.2, "信任 100 → 权重 1.2（好坏都放大）");
Near(SecurityScale.TrustWeight(0, 100), 1.0, "零增量不放大");
Near(SecurityScale.MemoryWeight(10, 0), 1.0, "还没归纳过经历 → 权重 1.0");
Check(SecurityScale.MemoryWeight(10, 6) < 1.0, "经历越多，单次事件的冲击越小");
Near(SecurityScale.MemoryWeight(-10, 100), 0.7, "经历再多也有下限 0.7（不会被惯性吃掉）");

// ==================== 10. 好感整合：增益打折 / 衰减更慢 ====================

Console.WriteLine("== 10. 安全感 → 好感增益折扣与衰减 ==");
Near(SecurityScale.AffectionGainFactor(0), 0.6, "安全感 0 → 好感增益 ×0.6");
Near(SecurityScale.AffectionGainFactor(500), 0.8, "安全感 500 → ×0.8");
Near(SecurityScale.AffectionGainFactor(1000), 1.0, "安全感 1000 → ×1.0（全速）");
Near(SecurityScale.AffectionGainFactor(-50), 0.6, "越界安全感按 0 算");
Near(SecurityScale.AffectionGainFactor(5000), 1.0, "越界安全感按满值算");
Near(SecurityScale.AffectionDecayFactor(0), 1.25, "安全感 0 → 好感掉得更快 ×1.25");
Near(SecurityScale.AffectionDecayFactor(1000), 0.75, "安全感满 → 好感掉得更慢 ×0.75");
Eq(SecurityScale.ScaleAffection(10, 1000), 10, "+10 在满安全感下全额");
Eq(SecurityScale.ScaleAffection(10, 0), 6, "+10 在零安全感下只剩 6");
Eq(SecurityScale.ScaleAffection(-12, 1000), -9, "−12 在满安全感下只掉 9");
Eq(SecurityScale.ScaleAffection(-12, 0), -15, "−12 在零安全感下掉 15");
Eq(SecurityScale.ScaleAffection(0, 0), 0, "零增量不动");

// ==================== 11. 时间漂移（独处 ↓ / 陪伴 ↑） ====================

Console.WriteLine("== 11. 时间漂移的段数与上限 ==");
Eq(SecurityScale.TimedChunks(SecKind.Solitude, 2.9), 0, "独处不足 3 小时不算一段");
Eq(SecurityScale.TimedChunks(SecKind.Solitude, 7), 2, "独处 7 小时 = 2 段");
Eq(SecurityScale.TimedChunks(SecKind.Solitude, 24 * 5), 3, "独处再久也只结算 3 段（不能挂机推到底）");
Eq(SecurityScale.TimedChunks(SecKind.Company, 3.9), 0, "陪伴不足 4 小时不算一段");
Eq(SecurityScale.TimedChunks(SecKind.Company, 9), 2, "陪伴 9 小时 = 2 段");
Eq(SecurityScale.TimedChunks(SecKind.Company, 100), 2, "陪伴最多 2 段");
Eq(SecurityScale.TimedDelta(SecKind.Solitude, 7, false), -8, "独处 7 小时 → −8");
Eq(SecurityScale.TimedDelta(SecKind.Solitude, 7, true), -12, "安全感缺失：独处 7 小时 → −12（×1.6）");
Eq(SecurityScale.TimedDelta(SecKind.Company, 9, false), 10, "陪伴 9 小时 → +10");
Eq(SecurityScale.TimedDelta(SecKind.Company, 9, true), 6, "安全感缺失：陪伴 9 小时 → +6（×0.6）");
Eq(SecurityScale.TimedDelta(SecKind.Ignored, 9, false), 0, "非时间类事件不走时间漂移");

// ==================== 12. 漂移复核与小模型夹取 ====================

Console.WriteLine("== 12. 漂移复核阈值 / 小模型结论夹取 ==");
Check(!SecurityScale.NeedsDriftReview(4, 100), "条数不够（4 < 5）不复核");
Check(!SecurityScale.NeedsDriftReview(5, 59), "净变化不够（59 < 60）不复核");
Check(SecurityScale.NeedsDriftReview(5, 60), "5 条同向且净 −60 才复核");
Check(SecurityScale.NeedsDriftReview(-6, 80), "上行漂移同样复核（按绝对值）");
Check(!SecurityScale.NeedsDriftReview(6, -40), "条数够但净变化不够也不复核");

Eq(SecurityScale.ClampAiSuggestion(600, 2000), 660, "复核对 2000 的建议只许挪 +60");
Eq(SecurityScale.ClampAiSuggestion(600, -500), 540, "复核对 -500 的建议只许挪 −60");
Eq(SecurityScale.ClampAiSuggestion(600, 620), 620, "小改照原样落地（+20）");
Eq(SecurityScale.ClampAiSuggestion(980, 1000), 1000, "夹取后再夹边界（不超过 1000）");
Eq(SecurityScale.ClampAiSuggestion(SecurityScale.Unset, 900), 660, "未写入的值先补 600 再夹");
Eq(SecurityScale.ClampAiStep(50), 20, "归纳加成步长最多 ±20");
Eq(SecurityScale.ClampAiStep(-50), -20, "步长下限 −20");
Eq(SecurityScale.ClampAiStep(7), 7, "小步长原样");

// ==================== 13. 回避倾向 & 事件表完整性 ====================

Console.WriteLine("== 13. 回避倾向与事件表自洽 ==");
Near(SecurityScale.AvoidanceBias(1000), 0, "满安全感 → 不动回避");
Near(SecurityScale.AvoidanceBias(0), 1, "零安全感 → 回避拉满");
Near(SecurityScale.AvoidanceBias(250), 0.5, "250 点 → 回避 0.5");
Check(SecurityScale.AvoidanceBias(250, true) > SecurityScale.AvoidanceBias(250, false), "带安全感缺失时回避再抬一档");
Near(SecurityScale.AvoidanceBias(250, true), 0.65, "抬一档是 +0.15");

Check(SecurityScale.Events.Select(e => e.Id).Distinct().Count() == SecurityScale.Events.Count, "事件 id 不重复");
Check(SecurityScale.Events.All(e => e.CooldownMinutes > 0), "每类事件都有冷却（防刷分）");
Check(SecurityScale.Events.All(e => e.DailyCap >= 1), "每类事件都有每日上限");
Check(SecurityScale.Events.All(e => e.BaseDelta != 0), "每类事件都有非零基础分");
Check(SecurityScale.RuleOf(SecKind.Abuse).BaseDelta < SecurityScale.RuleOf(SecKind.Ignored).BaseDelta, "辱骂比忽视更伤（↓↓ vs ↓）");
Check(SecurityScale.Events.Where(e => e.BaseDelta > 0).All(e => e.Kind != SecKind.Solitude), "独处不会给正分");

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
