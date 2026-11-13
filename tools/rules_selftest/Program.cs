using WarmAsBefore.Models;
using WarmAsBefore.Services;
using WarmAsBefore.Services.Core;

// B3 纯函数规则层边界自测：零模型调用、零 IO、零时间源，全部由调用方注入。
// 与模组侧 tools/wab_rules_selftest/RulesSelfTest.java 同一约定：断言失败即非零退出。
int passed = 0;
var failures = new List<string>();

void Check(bool ok, string name)
{
    if (ok) passed++;
    else failures.Add(name);
}

void Near(double actual, double expect, double eps, string name)
    => Check(Math.Abs(actual - expect) <= eps, $"{name} (got {actual:F4}, want {expect:F4})");

void Eq(int actual, int expect, string name)
    => Check(actual == expect, $"{name} (got {actual}, want {expect})");

// ---------- F16 EnergyRules ----------
Near(EnergyRules.TalkCost(1), 0.15, 1e-9, "F16 说话每轮只耗很小值");
Check(EnergyRules.TalkCost(1) < EnergyRules.ThinkCost(1), "F16 说话比一档思考更省");
Check(EnergyRules.ThinkCost(3, true) > EnergyRules.ThinkCost(3, false), "F16 深度思考更耗神");
Eq((int)EnergyRules.ThinkCost(0), 0, "F16 关思考不扣");
Check(EnergyRules.WorkCost("mine") > EnergyRules.WorkCost("feed"), "F16 挖矿比被投喂耗体力");
Check(EnergyRules.WorkIntensityOf("chop") == 1.0, "F16 砍树是重活");
Check(EnergyRules.WorkIntensityOf("unknown-xyz") <= 0.15, "F16 未知事件按最轻处理");
Near(EnergyRules.SleepRecovery(8), 88, 1e-9, "F16 睡 8 小时恢复大头");
Near(EnergyRules.RestToward(40, 12), 70, 1e-9, "F16 休息一个半衰期回一半");
Near(EnergyRules.RestToward(40, 0), 40, 1e-9, "F16 零时间不回落");
Check(EnergyRules.Settle(20, 0, 0, 0, 0, 0) >= 19.9, "F16 无事件时保持");
Check(EnergyRules.Settle(20, 0, 8, 0, 0, 0) > 90, "F16 睡眠回血");
Check(EnergyRules.Settle(90, 0, 0, EnergyRules.WorkCost("mine", 4), 0, 0) < 90, "F16 长时间重活扣精力");
Near(EnergyRules.Settle(50, 0, 0, 999, 999, 999), 0, 1e-9, "F16 下界钳制 0");
Near(EnergyRules.Settle(50, 0, 99, 0, 0, 0), 100, 1e-9, "F16 上界钳制 100");
Near(EnergyRules.Clamp(double.NaN, 0, 100), 0, 1e-9, "F16 NaN 安全回落");

// ---------- F28 HormoneRules ----------
Check(HormoneRules.AdrenalineAfterDanger(0, 1) > 0, "F28 危急抬肾上腺素");
Check(HormoneRules.AdrenalineAfterDanger(0.9, 1) < 1.0, "F28 高位再加不会打满");
Check(HormoneRules.ShouldTriggerPtsd(0.9, false), "F28 胆固醇过量且未消化触发 PTSD");
Check(!HormoneRules.ShouldTriggerPtsd(0.9, true), "F28 打击被消化不触发");
Check(!HormoneRules.ShouldTriggerPtsd(0.2, false), "F28 未过量不触发");
Check(HormoneRules.CholesterolAfterSetback(0.2, 0.2, 0.8, 1.0, false) > 0.2, "F28 颓废遭打击抬胆固醇");
Check(HormoneRules.CholesterolAfterSetback(0.2, 0.9, 0.1, 1.0, false) == 0.2, "F28 不颓废不中和");
Check(HormoneRules.AppetiteSuppressionOf(0.9) < 0, "F28 胆固醇抑制食欲");
var organCost = HormoneRules.OrganCostOfAdrenaline(1.0);
Check(organCost.Serotonin < 0 && organCost.Cortisol > 0, "F28 肾上腺素过量有器官代价");
Near(HormoneRules.Settle(1.0, 0, 1, 1), 0.5, 1e-9, "F28 半衰期结算");
Check(HormoneRules.HasCrash(0.8, 0.2), "F28 虚脱感判定");

// ---------- F27 MemoryAbstractionRules ----------
var abs = MemoryAbstractionRules.Abstract("海边的圆形落日，暖红色", "sunset_圆.png",
    new[] { "我的世界", "傍晚" }, 1600, 900, new[] { "傍晚", "傍晚", "海边" });
Check(abs.Shapes.Contains("横幅"), "F27 宽高比抽象成横幅");
Check(abs.Shapes.Contains("圆形"), "F27 文字线索认出圆形");
Check(abs.Shapes.Any(s => s.Contains("红色")), "F27 主色线索");
Check(abs.Words.Count > 0, "F27 抽出词汇");
Check(abs.Links.Count > 0 && abs.Links[0] == "傍晚", "F27 联系按共享权重排序");
Check(abs.Confidence > 0, "F27 抽象置信度");
var absTall = MemoryAbstractionRules.Abstract("塔", null, null, 600, 1200, null);
Check(absTall.Shapes.Contains("竖幅"), "F27 竖幅判定");
var absNoCue = MemoryAbstractionRules.Abstract("", "", null, 0, 0, null);
Check(absNoCue.Shapes.Count == 0 && absNoCue.Words.Count == 0, "F27 无线索不臆造图形");
var faded = MemoryAbstractionRules.SpreadFade(0.1,
    new[] { new KeyValuePair<string, double>("a", 1.0), new KeyValuePair<string, double>("b", 0.25) });
Check(faded["a"] > faded["b"], "F27 联系越强传得越多");
Check(faded["a"] <= MemoryAbstractionRules.SpreadCap, "F27 单次扩散封顶");
Check(MemoryAbstractionRules.SpreadFade(1.0,
        new[] { new KeyValuePair<string, double>("a", 1.0) }).Count == 0,
    "F27 中心还鲜明时完全不扩散");
Near(MemoryAbstractionRules.ApplyFade(0.06, 0.5), MemoryAbstractionRules.Floor, 1e-9,
    "F27 淡忘有下界防锁死");
Check(!MemoryAbstractionRules.CenterMayFade(0.8, true), "F27 周围鲜明时中心不消失");
Check(MemoryAbstractionRules.CenterMayFade(0.2, true), "F27 周围淡下去后中心才消失");
Check(MemoryAbstractionRules.CenterMayFade(1.0, false), "F27 无邻居时中心直接可淡忘");

// ---------- F29 CenterBoundary ----------
var pack = new StatePack { Source = "somatization" };
pack.Lines.Add("胃里有点发紧");
pack.Lines.Add("【回复】你好呀，我是女仆");
pack.Lines.Add(new string('x', 250));  // 超长但仍在限长区间内（>320 会被当作成品回复整行丢弃）
var safe = CenterBoundary.SafeLines(pack);
Eq(safe.Count, 2, "F29 越权成品回复被拦下");
Check(safe[0] == "胃里有点发紧", "F29 正常状态描述保留");
Eq(safe[1].Length, CenterBoundary.MaxLineChars, "F29 超长状态行被限长");
Check(CenterBoundary.SafeCircuitBreak(new StatePack { CircuitBreak = false, CircuitBreakLine = "别怕" }) is null,
    "F29 未熔断不出短句");
Check(CenterBoundary.SafeCircuitBreak(new StatePack { CircuitBreak = true, CircuitBreakLine = "别怕" }) == "别怕",
    "F29 熔断短句放行");
Check(!new StatePack().GetType().GetProperties().Any(p => p.Name.Contains("FinalReply") || p.Name.Contains("Reply")),
    "F29 状态包类型上不存在最终回复字段");
Check(typeof(ILowLevelCenter).GetMethod("Observe")!.ReturnType == typeof(StatePack),
    "F29 低级中枢编译期只能返回状态包");

// ---------- 结果 ----------
Console.WriteLine($"B3 纯函数自测：{passed} 条通过，{failures.Count} 条失败");
foreach (var f in failures) Console.WriteLine("  FAIL " + f);
return failures.Count == 0 ? 0 : 1;
