using System.Globalization;
using WarmAsBefore.Models;
using WarmAsBefore.Services.Core;

namespace WarmAsBefore.Services;

/// <summary>
/// 抽象器官模拟层：不新增任何权威数值，只把 SomatizationService / PhysiologyService /
/// InnerLifeService / BrainAnticipationService（BrainState 挂在 GameEngine 的角色状态上）
/// 的现有字段，包装成 8 个带「状态灯 + 症状 + 影响说明」的器官节点，供超级监测页读取。
///
/// 设计约束（与契约 A 节一致）：
/// · 全程 try/catch；任一权威服务取不到，只把对应器官降级为 Inactive / Sleeping，绝不抛异常、绝不崩主流程。
/// · Tick 只做「刷新 + 比对 + 触发 Changed」，不落盘、不回写权威服务。
/// · Influences 是纯展示/驱动亮灯的连锁规则表，永不修改权威数值。
///
/// 依赖获取：沿用项目里现成的服务定位链（MaidBridgeService / ServicesHelper.GetServices 同款），
/// 因此本类用静态 Instance + 私有无参构造，无需构造函数注入、无需改 DI 注册。
/// </summary>
public sealed class OrganSimulationService
{
    /// <summary>全局唯一实例（无参构造；依赖在读取时通过服务定位器解析）。</summary>
    public static OrganSimulationService Instance { get; } = new();

    private OrganSimulationService() { }

    /// <summary>器官状态发生变化时触发（参数是角色 id）。监测页订阅后刷新亮灯。</summary>
    public event Action<string>? Changed;

    private readonly object _gate = new();
    private readonly Dictionary<string, IReadOnlyList<OrganState>> _cache = new(StringComparer.Ordinal);

    // ---- 依赖获取：照抄项目里 MaidBridgeService 的链，和 ServicesHelper.GetServices() 等价 ----

    private static IServiceProvider? Services()
    {
        try
        {
            return Microsoft.Maui.IPlatformApplication.Current?.Services
                ?? Microsoft.Maui.Controls.Application.Current?.Handler?.MauiContext?.Services;
        }
        catch { return null; }
    }

    private static T? Resolve<T>() where T : class
    {
        try { return Services()?.GetService(typeof(T)) as T; }
        catch { return null; }
    }

    // ---- 读取匿名快照的通用小工具（各权威服务的 Snapshot 返回匿名对象，不能直接强类型化） ----

    private static bool Has(object? snap, string name)
    {
        try { return snap?.GetType().GetProperty(name) is not null; }
        catch { return false; }
    }

    private static double Num(object? snap, string name, double fallback = 0)
    {
        try
        {
            var p = snap?.GetType().GetProperty(name);
            if (p is null) return fallback;
            var v = p.GetValue(snap);
            return v is null ? fallback : Convert.ToDouble(v, CultureInfo.InvariantCulture);
        }
        catch { return fallback; }
    }

    private static bool Flag(object? snap, string name, bool fallback = false)
    {
        try
        {
            var p = snap?.GetType().GetProperty(name);
            if (p is null) return fallback;
            return p.GetValue(snap) is bool b ? b : fallback;
        }
        catch { return fallback; }
    }

    private static IReadOnlyList<string> Words(object? snap, string name)
    {
        try
        {
            var p = snap?.GetType().GetProperty(name);
            if (p?.GetValue(snap) is not System.Collections.IEnumerable seq) return Array.Empty<string>();
            return seq.Cast<object>()
                      .Where(x => x is not null)
                      .Select(x => x!.ToString() ?? "")
                      .Where(s => s.Length > 0)
                      .ToList();
        }
        catch { return Array.Empty<string>(); }
    }

    private static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;

    private static IReadOnlyList<string> Clean(IEnumerable<string> xs) =>
        xs.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.Ordinal).ToList();

    /// <summary>一次读取拿到的全部权威数据；bool 标记用来区分「服务拿不到」和「服务在但没数据」。</summary>
    private sealed class Pull
    {
        public object? Soma;
        public object? Body;
        public InnerChemicals? Chem;
        public BrainState? Brain;
        public bool SomaService;
        public bool BodyService;
        public bool ChemService;
        public bool BrainService;
    }

    private static Pull ReadAuthoritative(string charId)
    {
        var d = new Pull();

        // 胃/肠/肺/肌肉 ← SomatizationService：食欲、走动欲、说话欲、躯体化负荷与中文症状。
        try
        {
            var svc = Resolve<SomatizationService>();
            d.SomaService = svc is not null;
            if (svc is not null) d.Soma = svc.Snapshot(charId);
        }
        catch (Exception ex) { App.WriteLog("OrganSim.Soma -> " + ex.Message); }

        // 皮肤 ← PhysiologyService：Warmth（冷/热）。Tick 是幂等的，只结算一次已过的时间。
        try
        {
            var svc = Resolve<PhysiologyService>();
            d.BodyService = svc is not null;
            if (svc is not null) { svc.Tick(); d.Body = svc.Snapshot(); }
        }
        catch (Exception ex) { App.WriteLog("OrganSim.Body -> " + ex.Message); }

        // 心/内分泌 ← InnerLifeService：七项内分泌。未开启或还没数据时返回 null（→ Sleeping）。
        try
        {
            var svc = Resolve<InnerLifeService>();
            d.ChemService = svc is not null;
            if (svc is not null) d.Chem = svc.PeekChemicals(charId);
        }
        catch (Exception ex) { App.WriteLog("OrganSim.Chem -> " + ex.Message); }

        // 脑 ← BrainState：挂在角色状态上（BrainAnticipationService 读写它），经 GameEngine 取。
        try
        {
            var engine = Resolve<GameEngine>();
            d.BrainService = engine is not null;
            if (engine is not null)
            {
                CharacterData? ch = null;
                if (engine.ActiveCharacter is { } a && a.Profile.Id == charId) ch = a;
                else if (engine.Roster.TryGetValue(charId ?? "", out var r)) ch = r;
                d.Brain = ch?.State.Brain;
            }
        }
        catch (Exception ex) { App.WriteLog("OrganSim.Brain -> " + ex.Message); }

        return d;
    }

    /// <summary>
    /// 按契约的「器官→数值映射」组装 8 个器官节点，顺序固定为胃→心→肺→肌肉→脑→肠→皮肤→内分泌。
    /// 任一服务缺失只降级该器官，不影响其余器官。
    /// </summary>
    public IReadOnlyList<OrganState> Observe(string charId)
    {
        var byKind = new Dictionary<OrganKind, OrganState>();
        void Put(OrganState s) => byKind[s.Kind] = s;

        try
        {
            var d = ReadAuthoritative(charId);

            // SomatizationService.Snapshot 在无状态/功能关闭时只给 { enabled = false }。
            var somaOn = d.Soma is not null && Flag(d.Soma, "enabled", true) && Has(d.Soma, "appetite");
            IReadOnlyList<string> somaSymptoms = somaOn ? Words(d.Soma, "symptoms") : Array.Empty<string>();
            double appetite = somaOn ? Num(d.Soma, "appetite", 0) : 0;
            double talk = somaOn ? Num(d.Soma, "talkDrive", 0.7) : 0.7;
            double move = somaOn ? Num(d.Soma, "moveDrive", 0.6) : 0.6;
            double load = somaOn ? Num(d.Soma, "somaticLoad", 0) : 0;
            bool restless = somaOn && Flag(d.Soma, "restless", false);

            var chem = d.Chem;
            double cort = chem?.Cortisol ?? 0;
            double adr = chem?.Adrenaline ?? 0;
            double fatigue = chem?.Fatigue ?? 0;

            // ---- 胃 Stomach ----
            // 数据源：SomatizationService.Appetite + 症状（胃灼烧/绞痛）。
            // 触发：愤怒烦躁(negArousal 高)或高皮质醇 → 胃灼烧 → 食欲 -0.45（Feed 里的规则）。
            if (!d.SomaService)
                Put(Node(OrganKind.Stomach, 0, OrganStatus.Inactive));
            else if (!somaOn)
                Put(Node(OrganKind.Stomach, 0, OrganStatus.Sleeping));
            else
            {
                var syms = somaSymptoms.Where(s => s.Contains("胃") || s.Contains("灼烧") || s.Contains("绞")).ToList();
                if (appetite < -0.4) syms.Add("没什么胃口");
                if (appetite > 0.4) syms.Add("想吃甜的油的解馋");
                var status = (syms.Any(s => s.Contains("灼烧") || s.Contains("绞")) || load > 0.68)
                    ? OrganStatus.Alert
                    : (appetite < -0.35 || appetite > 0.55 || load > 0.45)
                        ? OrganStatus.Warn
                        : OrganStatus.Normal;
                // 单纯「情绪性进食」不是坏事，记为正激活而不是告警。
                if (status == OrganStatus.Warn && appetite > 0.4 && load <= 0.45) status = OrganStatus.Active;
                Put(Node(OrganKind.Stomach, 0.55 * load + 0.45 * Math.Max(0, -appetite), status, syms,
                    "愤怒/紧张→胃灼烧→食欲下降", "胃灼烧/绞痛→脑烦躁↑→更难平静", "肠(压力)→胃不适→睡眠/心情波动"));
            }

            // ---- 心 Heart ----
            // 数据源：InnerChemicals.Adrenaline + 高唤起（呼吸急/胃灼烧症状作为唤起近似）。
            // 触发：高肾上腺素/高唤起 → 心悸；血清素高+皮质醇低+多巴胺高 → 想念/安心般的「暖」。
            // （EmotionMix 的想念/安心在 SomatizationService 内部消费、不单独暴露，这里用内分泌正向组合近似。）
            if (!d.ChemService)
                Put(Node(OrganKind.Heart, 0, OrganStatus.Inactive));
            else if (chem is null)
                Put(Node(OrganKind.Heart, 0, OrganStatus.Sleeping));
            else
            {
                bool highArousal = somaSymptoms.Any(s => s.Contains("灼烧") || s.Contains("呼吸"));
                bool palpitate = adr > 0.62 || (highArousal && adr > 0.45);
                bool warm = chem.Serotonin > 0.7 && cort < 0.35 && chem.Dopamine > 0.6;
                var syms = new List<string>();
                if (palpitate) syms.Add("心口突突地跳（心悸）");
                if (warm) syms.Add("心里踏实发暖");
                var status = palpitate ? OrganStatus.Alert
                           : warm ? OrganStatus.Active
                           : adr > 0.5 || cort > 0.6 ? OrganStatus.Warn
                           : OrganStatus.Normal;
                Put(Node(OrganKind.Heart, Math.Max(adr, highArousal ? 0.7 : 0), status, syms,
                    "高肾上腺素→肺呼吸急→坐立难安", "想念/安心→心里发暖→语气更软"));
            }

            // ---- 肺 Lungs ----
            // 数据源：SomatizationService 的躯体化负荷 + 症状。
            // 触发：SomaticLoad > 0.8 或 negArousal > 0.45（且皮质醇高）→ 症状「呼吸有点急」，由症状直接读出。
            if (!d.SomaService)
                Put(Node(OrganKind.Lungs, 0, OrganStatus.Inactive));
            else if (!somaOn)
                Put(Node(OrganKind.Lungs, 0, OrganStatus.Sleeping));
            else
            {
                var syms = somaSymptoms.Where(s => s.Contains("呼吸") || s.Contains("气")).ToList();
                bool breathless = syms.Count > 0;
                var status = breathless || load > 0.8 ? OrganStatus.Alert
                           : load > 0.5 || cort > 0.6 ? OrganStatus.Warn
                           : OrganStatus.Normal;
                Put(Node(OrganKind.Lungs, Math.Max(load, breathless ? 0.85 : 0), status, syms,
                    "心(高肾上腺素)→肺呼吸急→坐立难安", "高唤起→呼吸急促→躯体负荷上升"));
            }

            // ---- 肌肉 Muscle ----
            // 数据源：SomatizationService.MoveDrive / Restless / TalkDrive + InnerLifeService.Fatigue。
            // 触发：悲伤(negLow)、疲劳 → 不想动、不想说话；高唤起 → 坐立难安地想动（Active，也点亮）。
            if (!d.SomaService)
                Put(Node(OrganKind.Muscle, 0, OrganStatus.Inactive));
            else if (!somaOn)
                Put(Node(OrganKind.Muscle, 0, OrganStatus.Sleeping));
            else
            {
                var syms = new List<string>();
                if (move < 0.3) syms.Add("身上懒懒的不想动");
                if (talk < 0.3) syms.Add("不太想说话");
                if (restless) syms.Add("坐立难安想走动");
                var status = move < 0.3 || talk < 0.3 ? OrganStatus.Alert
                           : restless ? OrganStatus.Active
                           : 1 - move + 0.3 * fatigue > 0.6 ? OrganStatus.Warn
                           : OrganStatus.Normal;
                Put(Node(OrganKind.Muscle, 1 - move + 0.3 * fatigue, status, syms,
                    "悲伤/疲劳→走动欲低→不想说话→社交减少", "疲劳→走动欲低"));
            }

            // ---- 脑 Brain ----
            // 数据源：BrainState.Dopamine（0..100，50 平静）+ ExpectedWarmth + InnerChemicals.Drive。
            // 触发：>62 被打动、<38 小失落、高皮质醇 → 心里发紧更难平静。
            if (!d.BrainService)
                Put(Node(OrganKind.Brain, 0, OrganStatus.Inactive));
            else if (d.Brain is null)
                Put(Node(OrganKind.Brain, 0, OrganStatus.Sleeping));
            else
            {
                double dopamine = d.Brain.Dopamine / 100.0;
                double drive = chem?.Drive ?? 0;
                var syms = new List<string>();
                var status = OrganStatus.Normal;
                if (dopamine >= 0.62) { syms.Add("被回应打动，心里发亮"); status = OrganStatus.Active; }
                else if (dopamine <= 0.38) { syms.Add("有点小失落，热情收了收"); status = OrganStatus.Warn; }
                else syms.Add("心里平静，好感照常");
                if (drive > 0.72) syms.Add("挺有干劲，说话利落");
                if (cort > 0.6) { syms.Add("心里发紧，更难平静"); status = OrganStatus.Alert; }
                Put(Node(OrganKind.Brain, Math.Max(dopamine, drive), status, syms,
                    "高皮质醇→胃灼烧→食欲下降", "胃灼烧/绞痛→脑烦躁↑→更难平静", "干劲↑→说话更利落"));
            }

            // ---- 肠 Gut ----
            // 数据源：SomatizationService.SomaticLoad + 症状「肚子隐隐发紧」。
            // 触发：持续压力/高唤起 → 负荷累积 → 肚子发紧，牵连睡眠与心情。
            if (!d.SomaService)
                Put(Node(OrganKind.Gut, 0, OrganStatus.Inactive));
            else if (!somaOn)
                Put(Node(OrganKind.Gut, 0, OrganStatus.Sleeping));
            else
            {
                var syms = somaSymptoms.Where(s => s.Contains("肚子") || s.Contains("紧")).ToList();
                var status = load > 0.8 ? OrganStatus.Alert : load > 0.5 ? OrganStatus.Warn : OrganStatus.Normal;
                Put(Node(OrganKind.Gut, load, status, syms,
                    "压力→肚子隐隐发紧→胃不适→睡眠/心情波动"));
            }

            // ---- 皮肤 Skin ----
            // 数据源：PhysiologyService.Snapshot().Warmth（0 冷 / 1 热）。
            // 触发：<0.3 想靠近暖处；>0.75 想扇风喝凉的；中间区舒适（偏高记 Active，点亮）。
            if (!d.BodyService)
                Put(Node(OrganKind.Skin, 0, OrganStatus.Inactive));
            else if (d.Body is null)
                Put(Node(OrganKind.Skin, 0, OrganStatus.Sleeping));
            else
            {
                double warmth = Num(d.Body, "warmth", 0.5);
                var syms = new List<string>();
                var status = OrganStatus.Normal;
                if (warmth < 0.3) { syms.Add("发冷，想靠近暖和的地方"); status = OrganStatus.Warn; }
                else if (warmth > 0.75) { syms.Add("发热，想扇风喝凉的"); status = OrganStatus.Warn; }
                else if (warmth >= 0.6) status = OrganStatus.Active;
                Put(Node(OrganKind.Skin, warmth, status, syms,
                    "冷→想靠近暖处→蜷缩少动", "暖→放松→更愿意交流"));
            }

            // ---- 内分泌 Endocrine ----
            // 数据源：InnerChemicals 七项 + 派生 Drive。
            // 触发：皮质醇/肾上腺素/疲劳过高 → 告警；长期性激励素高 → 负荷累积变慢（全身更稳）。
            if (!d.ChemService)
                Put(Node(OrganKind.Endocrine, 0, OrganStatus.Inactive));
            else if (chem is null)
                Put(Node(OrganKind.Endocrine, 0, OrganStatus.Sleeping));
            else
            {
                double avg = (chem.Dopamine + chem.Cortisol + chem.Serotonin + chem.Fatigue
                            + chem.Adrenaline + chem.Cholesterol + chem.LongTermDrive) / 7.0;
                var syms = new List<string>();
                if (cort > 0.6) syms.Add("皮质醇偏高：心里发紧");
                if (fatigue > 0.65) syms.Add("疲劳：身体发沉");
                if (adr > 0.62) syms.Add("肾上腺素高：绷着");
                if (chem.Cholesterol > 0.60) syms.Add("胆固醇高：不太想吃东西");
                if (chem.LongTermDrive > 0.72) syms.Add("长期性激励素高：踏实");
                var status = cort > 0.6 || adr > 0.62 || fatigue > 0.7 ? OrganStatus.Alert
                           : syms.Count > 0 ? OrganStatus.Warn
                           : chem.Drive > 0.72 ? OrganStatus.Active
                           : OrganStatus.Normal;
                Put(Node(OrganKind.Endocrine, Math.Max(avg, chem.Drive * 0.7), status, syms,
                    "长期性激励素↑→躯体化负荷累积变慢→全身更稳", "皮质醇高→胃灼烧→食欲下降"));
            }
        }
        catch (Exception ex)
        {
            App.WriteLog("OrganSim.Observe -> " + ex.Message);
        }

        // 兜底：无论如何都按固定顺序交出 8 个节点，缺的标 Inactive，界面不会拿到半张表。
        var result = new List<OrganState>(8);
        foreach (var kind in OrganKinds.All)
            result.Add(byKind.TryGetValue(kind, out var s) ? s : Node(kind, 0, OrganStatus.Inactive));
        return result;
    }

    private static OrganState Node(OrganKind kind, double value, OrganStatus status,
        IEnumerable<string>? symptoms = null, params string[] influences)
        => new()
        {
            Kind = kind,
            Value = Clamp01(value),
            Status = status,
            Symptoms = Clean(symptoms ?? Array.Empty<string>()),
            Influences = Clean(influences),
            UpdatedAt = DateTime.UtcNow
        };

    // ---- 器官连锁规则表（契约 A 节 6 条；纯展示 + 驱动亮灯，不改权威数值） ----

    private static readonly IReadOnlyList<OrganInfluence> Rules = new[]
    {
        new OrganInfluence
        {
            From = "脑", To = "胃", DelayMinutes = 30,
            Effect = "高皮质醇让胃发紧灼烧，食欲下降（茶饭不思）。"
        },
        new OrganInfluence
        {
            From = "胃", To = "脑", DelayMinutes = 20,
            Effect = "胃灼烧/绞痛反过来让脑子烦躁，更难平静。"
        },
        new OrganInfluence
        {
            From = "心", To = "肺", DelayMinutes = 5,
            Effect = "肾上腺素高时心悸带得呼吸急促，整个人坐立难安。"
        },
        new OrganInfluence
        {
            From = "肌肉", To = "脑", DelayMinutes = 15,
            Effect = "疲劳/悲伤让肌肉没劲，连带说话欲下降，社交减少。"
        },
        new OrganInfluence
        {
            From = "肠", To = "胃", DelayMinutes = 45,
            Effect = "压力让肚子隐隐发紧，牵连胃不舒服，睡眠和心情跟着波动。"
        },
        new OrganInfluence
        {
            From = "内分泌", To = "全身", DelayMinutes = 240,
            Effect = "长期性激励素高时，躯体化负荷累积变慢，全身状态更稳。"
        }
    };

    public IReadOnlyList<OrganInfluence> Influences() => Rules;

    // ---- 刷新与事件 ----

    /// <summary>从权威服务刷新器官状态；与上次签名不同就触发 <see cref="Changed"/>。</summary>
    public void Tick(string charId)
    {
        if (string.IsNullOrEmpty(charId)) return;
        try
        {
            var next = Observe(charId);
            var sig = Signature(next);
            bool changed;
            lock (_gate)
            {
                changed = !_cache.TryGetValue(charId, out var prev) || Signature(prev) != sig;
                if (changed) _cache[charId] = next;
            }
            if (changed)
            {
                try { Changed?.Invoke(charId); }
                catch (Exception ex) { App.WriteLog("OrganSim.Changed -> " + ex.Message); }
            }
        }
        catch (Exception ex)
        {
            App.WriteLog("OrganSim.Tick -> " + ex.Message);
        }
    }

    private static string Signature(IReadOnlyList<OrganState> states) =>
        string.Join("|", states.Select(s =>
            $"{s.Kind}:{s.Status}:{s.Value:0.00}:{string.Join(',', s.Symptoms)}"));

    /// <summary>给超级监测页用的结构化快照（匿名对象，字段稳定，绑定即可）。</summary>
    public object Snapshot(string charId)
    {
        try
        {
            var nodes = Observe(charId).Select(o => new
            {
                kind = o.Kind.ToString(),
                name = o.Name,
                value = Math.Round(o.Value, 2),
                status = o.Status.ToString(),
                statusText = o.StatusText,
                lit = o.IsLit,
                symptoms = o.Symptoms.ToList(),
                influences = o.Influences.ToList(),
                updatedAt = o.UpdatedAt
            }).ToList();
            var links = Rules.Select(i => new
            {
                from = i.From,
                to = i.To,
                effect = i.Effect,
                delayMinutes = i.DelayMinutes
            }).ToList();
            return new { characterId = charId, organs = nodes, influences = links };
        }
        catch (Exception ex)
        {
            App.WriteLog("OrganSim.Snapshot -> " + ex.Message);
            return new { characterId = charId, organs = new List<object>(), influences = new List<object>() };
        }
    }
}
