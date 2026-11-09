using WarmAsBefore.Models;

namespace WarmAsBefore.Services;

/// <summary>
/// 一个角色此刻的「具体化」状态：情绪如何落到身体与行为上。
///
/// 设计原则（与用户要求一致：半代码半 AI，最多用代码，不得已才用 AI）：
/// 全部判定走纯代码——本地线索词读心（<see cref="EmotionMix.FromText"/>，不花钱）得到情绪配比，
/// 再结合 <see cref="InnerLifeService"/> 此刻的内分泌池（皮质醇 / 多巴胺 / 血清素 / 疲劳），
/// 算出食欲、说话欲、走动欲，以及「躯体化负荷」的累积与阈值症状。
/// 只有最后那段提示词散文交给模型去「演」，判定本身不调一次模型。
///
/// 研究依据（脑-肠轴 / 躯体化）：
/// · 急性压力→CRF→胃酸分泌异常（灼烧感）+ 交感抑制胃蠕动（胀/隐痛）；慢性压力→内脏高敏感→无器质病变的胃痛。
/// · 压力下食欲两极：高唤起威胁感→「茶饭不思」；低能量负性情绪（难过/孤独）→偏向情绪性进食（甜/油）。
/// · 惊恐/高唤起→心悸、气短、过度换气；长期压抑者身体成为「情绪出口」，症状真实但查无实据。
/// · 暴力冲动只作为内心挣扎被看见，绝不写成对他人的伤害行为。
/// </summary>
public sealed record SomaState
{
    /// <summary>食欲偏移：-1=完全吃不下，+1=很想吃（尤其甜的/油的）。0=平常。</summary>
    public double Appetite { get; set; }

    /// <summary>说话欲：0=不想说话，1=很想说。</summary>
    public double TalkDrive { get; set; } = 0.7;

    /// <summary>走动欲：0=不想动，1=很想活动。配合 <see cref="Restless"/> 表示坐立难安地想动。</summary>
    public double MoveDrive { get; set; } = 0.6;

    /// <summary>坐立难安：高唤起下想动但动得不安稳（焦虑型来回走）。</summary>
    public bool Restless { get; set; }

    /// <summary>躯体化负荷：0~1，持续的高唤起+负性情绪会累积，平静时回落；跨过阈值触发躯体症状。</summary>
    public double SomaticLoad { get; set; }

    /// <summary>当前激活的躯体症状（中文短词：肚子疼/胃灼烧/呼吸急促/暴力倾向…）。</summary>
    public List<string> Symptoms { get; set; } = new();

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class SomaBook
{
    public Dictionary<string, SomaState> ByCharacter { get; set; } = new(StringComparer.Ordinal);
}

public sealed class SomatizationService
{
    private readonly StorageProvider _store;
    private readonly MemoryScope _scope;
    private readonly SettingsManager _settings;
    private readonly InnerLifeService _inner;
    private readonly object _gate = new();
    private SomaBook _book = new();
    private bool _loaded;
    private bool _saving;

    public SomatizationService(StorageProvider store, MemoryScope scope, SettingsManager settings, InnerLifeService inner)
    {
        _store = store;
        _scope = scope;
        _settings = settings;
        _inner = inner;
        scope.Changed += ReleaseCache;
    }

    private string Key => _scope.Key("somatization");
    private bool Enabled => _settings.Current.ConcretizationEnabled;

    // ---- 存储 ----

    private async Task EnsureLoadedAsync()
    {
        if (_loaded) return;
        try
        {
            var loaded = await _store.Load<SomaBook>(Key);
            lock (_gate) { _book = loaded ?? new SomaBook(); _loaded = true; }
        }
        catch (Exception ex)
        {
            App.WriteLog("Somatization.Load -> " + ex.Message);
            lock (_gate) _loaded = true;
        }
    }

    /// <summary>作用域换了（读档/新开局）：丢缓存重读。</summary>
    public void ReleaseCache()
    {
        lock (_gate) { _book = new SomaBook(); _loaded = false; }
    }

    private async Task SaveAsync()
    {
        if (_saving) return;
        SomaBook snap;
        lock (_gate)
        {
            if (!_loaded) return;
            _saving = true;
            snap = new SomaBook
            {
                ByCharacter = _book.ByCharacter.ToDictionary(
                    kv => kv.Key,
                    kv => new SomaState
                    {
                        Appetite = kv.Value.Appetite,
                        TalkDrive = kv.Value.TalkDrive,
                        MoveDrive = kv.Value.MoveDrive,
                        Restless = kv.Value.Restless,
                        SomaticLoad = kv.Value.SomaticLoad,
                        Symptoms = kv.Value.Symptoms.ToList(),
                        UpdatedAt = kv.Value.UpdatedAt
                    },
                    StringComparer.Ordinal)
            };
        }
        try { await _store.Save(Key, snap); }
        catch (Exception ex) { App.WriteLog("Somatization.Save -> " + ex.Message); }
        finally { lock (_gate) _saving = false; }
    }

    private SomaState StateOf(string charId)
    {
        if (!_book.ByCharacter.TryGetValue(charId ?? "", out var s))
        {
            s = new SomaState();
            _book.ByCharacter[charId ?? ""] = s;
        }
        return s;
    }

    private static double Clamp(double v, double lo, double hi) => v < lo ? lo : v > hi ? hi : v;

    // ---- 输入：每轮对话 ----

    /// <summary>
    /// 用户说了一句话：用纯代码本地读心得到情绪配比，结合此刻内分泌池，重算具体化状态。
    /// 异步落盘用后台任务，不阻塞对话。
    /// </summary>
    public void Feed(string charId, string? userText)
    {
        if (!Enabled || string.IsNullOrEmpty(charId)) return;
        _ = Task.Run(async () =>
        {
            await EnsureLoadedAsync();
            var chem = _inner.PeekChemicals(charId);
            var mix = EmotionMix.FromText(userText);
            var s = StateOf(charId);
            lock (_gate)
            {
                var now = DateTime.UtcNow;
                var hours = Clamp((now - s.UpdatedAt).TotalHours, 0, 24 * 30);

                // 情绪信号（16 词配比里取有意义的分组）
                double negArousal = mix.RatioOf("害怕") + mix.RatioOf("紧张") + mix.RatioOf("不安")
                                  + 0.7 * mix.RatioOf("委屈") + 0.6 * mix.RatioOf("愧疚");
                double negLow = mix.RatioOf("难过") + mix.RatioOf("孤独") + mix.RatioOf("心疼")
                              + 0.5 * mix.RatioOf("委屈");
                double posConn = mix.RatioOf("想念") + mix.RatioOf("依赖") + mix.RatioOf("安心")
                              + mix.RatioOf("感激") + mix.RatioOf("开心") + mix.RatioOf("期待") + mix.RatioOf("平静");
                double shy = mix.RatioOf("害羞");

                // 内分泌池（来自 InnerLifeService，纯代码累积；未开启时取气质基线）
                double cort = chem?.Cortisol ?? 0.28;
                double dop = chem?.Dopamine ?? 0.5;
                double ser = chem?.Serotonin ?? 0.6;
                double fatigue = chem?.Fatigue ?? 0;

                // 食欲：高唤起（焦虑/恐惧）抑制（茶饭不思、胃紧）；低能量负性（难过/孤独）偏向情绪性进食（甜/油）；
                //       持续高压皮质醇也偏向安慰性进食。
                double ap = 0;
                ap -= 0.45 * negArousal;
                ap += 0.35 * negLow;
                ap += 0.10 * posConn;
                ap += 0.20 * Math.Max(0, cort - 0.6);
                s.Appetite = Clamp(ap, -1, 1);

                // 说话欲：想念/依赖→想找人说话；轻度难过想倾诉；害羞/紧张/高唤起→话少；严重躯体化/疲劳→不想说话。
                double td = 0.7;
                td += 0.22 * Math.Min(1, mix.RatioOf("想念") + mix.RatioOf("依赖"));
                td += 0.10 * Math.Min(0.5, negLow);
                td -= 0.18 * shy;
                td -= 0.08 * negArousal;
                td -= 0.35 * Math.Max(0, s.SomaticLoad - 0.6);
                td -= 0.30 * fatigue;
                s.TalkDrive = Clamp(td, 0, 1);

                // 走动欲：多巴胺→想活动；高唤起→坐立难安地想动；难过/孤独/疲劳/严重躯体化→不想动。
                double md = 0.6;
                md += 0.20 * dop;
                md += 0.15 * cort;
                md -= 0.40 * Math.Max(0, negLow - 0.3);
                md -= 0.35 * fatigue;
                md -= 0.30 * Math.Max(0, s.SomaticLoad - 0.6);
                s.MoveDrive = Clamp(md, 0, 1);
                s.Restless = cort > 0.55 && s.MoveDrive > 0.5;

                // 躯体化负荷（泄漏积分器）：高唤起+负性情绪+疲劳持续累积，平静时回落。
                double stress = 0.7 * Math.Max(0, cort - 0.4) + 0.5 * negArousal + 0.3 * negLow + 0.2 * fatigue;
                stress = Clamp(stress, 0, 1.2);
                double gain = 0.10 * stress;   // 每小时累积
                double decay = 0.045;          // 每小时自然恢复
                s.SomaticLoad = Clamp(s.SomaticLoad + (gain - decay) * hours, 0, 1);

                // 症状（阈值 + 情绪特异，纯代码）
                var symptoms = new List<string>();
                if (s.SomaticLoad > 0.5) symptoms.Add("肚子隐隐发紧");
                if (s.SomaticLoad > 0.68) symptoms.Add("胃里有灼烧感");
                if (s.SomaticLoad > 0.8 || (negArousal > 0.45 && cort > 0.6)) symptoms.Add("呼吸有点急");
                if ((mix.RatioOf("委屈") + mix.RatioOf("难过")) > 0.4 && s.SomaticLoad > 0.45)
                    symptoms.Add("胃一阵阵地绞着疼");
                if (s.SomaticLoad > 0.92 && cort > 0.7) symptoms.Add("暴力倾向");
                s.Symptoms = symptoms;

                s.UpdatedAt = now;
            }
            _ = SaveAsync();
        });
    }

    // ---- 输出：提示词 ----

    /// <summary>给提示词的一段「身体的反应」。只描述感受与倾向，不给数字；平常心时返回空串。</summary>
    public string PromptText(string charId)
    {
        if (!Enabled || string.IsNullOrEmpty(charId)) return "";
        SomaState? s = null;
        lock (_gate) if (_book.ByCharacter.TryGetValue(charId, out var x)) s = x;
        if (s is null) return "";

        var parts = new List<string>();
        // 食欲
        if (s.Appetite < -0.4) parts.Add("这会儿没什么胃口，看着吃的也没兴致，胃里像拧着");
        else if (s.Appetite > 0.4) parts.Add("心里空落落的，莫名想找点甜的油的解解，不一定是饿");
        // 说话
        if (s.TalkDrive < 0.3) parts.Add("不太想说话，只想安静待着，旁人问多了会烦");
        else if (s.TalkDrive > 0.85) parts.Add("这会儿特别想找人唠两句，一个人待着发慌");
        // 走动
        if (s.MoveDrive < 0.3) parts.Add("身上懒懒的，一点都不想动，就想缩着");
        else if (s.Restless) parts.Add("坐不住，腿脚老想换个地方，来回踱两圈才安心");
        else if (s.MoveDrive > 0.8) parts.Add("闲不住，想出去走走、动一动");
        // 症状
        if (s.Symptoms.Count > 0)
        {
            var sb = new System.Text.StringBuilder("身体不舒服：");
            for (int i = 0; i < s.Symptoms.Count; i++)
            {
                var sym = s.Symptoms[i];
                if (sym == "暴力倾向")
                    // 暴力冲动只作为内心挣扎被看见，且立刻自我抑制——绝不写成对他人的伤害行为。
                    sb.Append("有那么一下，胸口堵得想摔点什么——可手抬起来又放下了，她不会那样做的");
                else
                    sb.Append(sym);
                if (i < s.Symptoms.Count - 1) sb.Append('、');
            }
            parts.Add(sb.ToString());
        }

        if (parts.Count == 0) return "";
        return "【身体的反应】" + string.Join("；", parts)
             + "。这些只是此刻的感受，自然带进语气和动作里，别一条条报出来，也不要每句都提。";
    }

    // ---- 输出：工具 / 面板 ----

    public object Snapshot(string charId)
    {
        SomaState? s = null;
        lock (_gate) if (_book.ByCharacter.TryGetValue(charId ?? "", out var x)) s = x;
        if (s is null) return new { enabled = Enabled };
        lock (_gate)
            return new
            {
                enabled = Enabled,
                appetite = Math.Round(s.Appetite, 2),
                talkDrive = Math.Round(s.TalkDrive, 2),
                moveDrive = Math.Round(s.MoveDrive, 2),
                restless = s.Restless,
                somaticLoad = Math.Round(s.SomaticLoad, 2),
                symptoms = s.Symptoms.ToList()
            };
    }
}
