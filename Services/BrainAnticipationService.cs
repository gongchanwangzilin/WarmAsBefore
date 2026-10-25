using System.Text.RegularExpressions;
using WarmAsBefore.Models;
using WarmAsBefore.Modules.ApiManager;

namespace WarmAsBefore.Services;

/// <summary>
/// 大脑预判：两层机制，默认都本地跑（不花钱）。
/// (1) 期待差 / 多巴胺：TA 心里估你这次会有多热，实际超预期→积极值与好感上浮，低于预期→回落。
/// (2) 意料之外：偶尔不完全顺着最顺的走向，制造「预测不到」的心动。
/// 可选：配一枚免费小模型，每 N 轮补一次预判（更准、仍省钱）。写入不另调高级模型——
/// 预判只作为一行指令搭进本来就要跑的那一轮里。
/// </summary>
public sealed class BrainAnticipationService
{
    private readonly SettingsManager _settings;
    private readonly GameEngine _engine;
    private readonly ApiGateway _api;

    /// <summary>每角色「本轮」结算倍率（PrepareTurn 写，ApplyChatGain 消费）。</summary>
    private readonly Dictionary<string, double> _pendingMultiplier = new(StringComparer.Ordinal);
    /// <summary>每角色「本轮」要用的惊喜卡（PrepareTurn 写，BuildContext 读）。</summary>
    private readonly Dictionary<string, string> _pendingCard = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public BrainAnticipationService(SettingsManager settings, GameEngine engine, ApiGateway api)
    {
        _settings = settings;
        _engine = engine;
        _api = api;
    }

    private bool MasterEnabled => _settings.Current.BrainEnabled;

    private CharacterData? Char(string charId) =>
        _engine.ActiveCharacter is { } a && a.Profile.Id == charId ? a
        : _engine.Roster.TryGetValue(charId, out var r) ? r : null;

    // ---- 本地温暖值启发式：不花钱，只按文本粗估「这次你对我有多热」(0..1) ----

    private static readonly string[] WarmWords =
        { "喜欢", "想你", "爱你", "宝贝", "宝", "亲爱的", "老公", "老婆", "贴贴", "抱抱", "亲亲",
          "乖", "可爱", "好棒", "谢谢你", "辛苦", "晚安", "早安", "在吗", "在乎", "惦记", "想见", "抱抱你" };

    private static readonly string[] ColdWords =
        { "滚", "烦", "讨厌", "别烦", "闭嘴", "算了", "无所谓", "随便", "呵", "没意思", "无聊",
          "不想", "累了", "不想说", "无所谓了", "别来了" };

    private static readonly string[] DistressedWords =
        { "难过", "哭", "崩溃", "想死", "自杀", "轻生", "好痛", "疼", "害怕", "焦虑", "抑郁",
          "绝望", "撑不住", "想消失", "不想活", "难受" };

    public bool IsDistressed(string userText) =>
        DistressedWords.Any(x => (userText ?? "").Contains(x));

    /// <summary>本地粗估这条消息的「温度」(0..1)：亲近词/长度/问句加分，冷淡词减分，明显难过判低温。</summary>
    public double ComputeWarmth(string userText)
    {
        var t = (userText ?? "").Trim();
        if (t.Length == 0) return 0.5;
        var w = 0.5;
        var len = t.Length;
        if (len <= 2) w -= 0.16;
        else if (len <= 5) w -= 0.06;
        else if (len >= 120) w += 0.14;
        else if (len >= 40) w += 0.08;
        else if (len >= 12) w += 0.03;
        w += 0.05 * CountHits(t, WarmWords);
        w -= 0.06 * CountHits(t, ColdWords);
        if (t.Contains('？') || t.Contains('?')) w += 0.03;
        if (t.Contains('！') || t.Contains('!')) w += 0.02;
        if (Regex.IsMatch(t, "(哈+|嘿+|嘻+|嘻嘻|哈哈)")) w += 0.04;
        if (IsDistressed(t)) w -= 0.2;
        return Math.Clamp(w, 0, 1);
    }

    private static int CountHits(string text, string[] words)
    {
        var n = 0;
        foreach (var w in words)
            if (w.Length > 0 && text.Contains(w)) n++;
        return n;
    }

    // ---- 每轮结算：PrepareTurn（发送前，本地） ----

    /// <summary>
    /// 用户消息到达时调用（本地、同步、无网络）：算温暖值、结算期待差、更新 TA 的多巴胺与预期曲线，
    /// 并按需抽一张惊喜卡。结果暂存，由 BuildContext 注入提示词、ApplyChatGain 结算好感。
    /// </summary>
    public void PrepareTurn(string charId, string userText)
    {
        if (!MasterEnabled) return;
        var ch = Char(charId);
        if (ch is null) return;
        var s = _settings.Current;
        var brain = ch.State.Brain;
        var actual = ComputeWarmth(userText);

        // 新鲜时优先用免费模型的预判替代本地期望
        var expect = brain.ExpectedWarmth;
        if (brain.HintWarmth >= 0 && (brain.Turns - brain.HintTurn) <= 1)
            expect = 0.6 * brain.HintWarmth + 0.4 * brain.ExpectedWarmth;

        double multiplier = 1.0;
        if (s.BrainExpectationEnabled)
        {
            var err = actual - expect;                            // -1..1 的预测误差
            var anchor = 0.35 + 0.4 * (ch.State.Affection / 100.0);
            brain.ExpectedWarmth = Math.Clamp(0.7 * expect + 0.2 * actual + 0.1 * anchor, 0, 1);
            var target = 50 + (err >= 0 ? 65 * err : 85 * err);   // 正误差比负误差更快回落
            brain.Dopamine = Math.Clamp(0.35 * brain.Dopamine + 0.65 * target, 0, 100);
            multiplier = 1 + 0.85 * err;
        }
        else
        {
            brain.Dopamine = 50;
        }

        brain.Turns++;
        multiplier = Math.Clamp(multiplier, 0.2, 1.8);

        var card = "";
        if (s.BrainSurpriseEnabled && !IsDistressed(userText))
        {
            // 有新鲜的免费模型惊喜提示就用它，否则抽本地惊喜卡
            card = brain.Hint.Length > 0 && (brain.Turns - brain.HintTurn) <= 1
                ? brain.Hint
                : DrawCard(brain, charId);
        }

        lock (_gate)
        {
            _pendingMultiplier[charId] = multiplier;
            _pendingCard[charId] = card;
        }
        ch.State.LastSeen = DateTime.UtcNow;
    }

    /// <summary>本地惊喜卡：从一套安全的微行为里抽一张，避免连抽同一张。不花钱。</summary>
    private static readonly string[] Cards =
    {
        "先做一个很小的动作（推过来一杯水、拉一下袖口），再开口。",
        "这一轮只回很短一句，把剩下的留到下一轮。",
        "答完之后，忽然问一个你一直好奇、但和眼下话题无关的小问题。",
        "别按最顺的逻辑接话，先停半拍，说一句看似不相干的真心话。",
        "用一个具体的比喻代替评价，别讲道理。",
        "这一轮不主动安慰，只是安静地陪着；用动作或很短的话回应。",
        "把一句普通的关心说得更私人一点（提到你们之间的一个细节）。",
        "小小地唱一次反调，但留一个明显的软处。",
        "先承认自己刚才有点在意，再轻轻带过。",
        "换一个你平时不用的称呼叫对方一次，然后装作没发生。"
    };

    private static string DrawCard(BrainState brain, string charId)
    {
        var seed = (charId.GetHashCode() & 0x7fffffff) ^ (brain.Turns * 7919);
        var rng = new Random(seed);
        for (var i = 0; i < Cards.Length; i++)
        {
            var c = Cards[(rng.Next() + i) % Cards.Length];
            if (!string.Equals(c, brain.LastCard, StringComparison.Ordinal))
            {
                brain.LastCard = c;
                return c;
            }
        }
        return brain.LastCard.Length > 0 ? brain.LastCard : Cards[0];
    }

    // ---- 提示词注入：一行指令搭进本来就要跑的那一轮（不另花钱） ----

    /// <summary>建会话 / 刷新人设时拼一次的稳定规则（很短，随会话复用）。</summary>
    public string BuildPersona(string charId)
    {
        if (!MasterEnabled) return "";
        var s = _settings.Current;
        var parts = new List<string>();
        if (s.BrainExpectationEnabled)
            parts.Add("你会下意识估对方待会儿会怎么接你的话：对方比你想的更热，你会受用、更放得开；比你想的冷，你会把热情收一收。这个预估只体现在语气里，别把「预判」「心里估」这些词说出口。");
        if (s.BrainSurpriseEnabled)
            parts.Add("你偶尔会不完全顺着最顺的走向走——换一个角度、停半拍、提一件你一直好奇的小事。只做一点点，不破坏人设；对方情绪低落时别用。");
        return string.Join("\n", parts);
    }

    /// <summary>每轮动态上下文：把本轮的期待差与惊喜卡各压成一句，拼在用户消息前（AI 可见、不算用户说的话）。</summary>
    public string BuildContext(string charId)
    {
        if (!MasterEnabled) return "";
        var s = _settings.Current;
        double mult;
        string card;
        lock (_gate)
        {
            if (!_pendingMultiplier.TryGetValue(charId, out mult)) return "";
            _pendingCard.TryGetValue(charId, out card);
        }
        var parts = new List<string>();
        if (s.BrainExpectationEnabled)
        {
            parts.Add(mult > 1.05
                ? "你原以为对方这次会淡一点，结果比你想的热络，你心里被轻轻点了一下（好感会自然往上走）。别把「预判」说出口。"
                : mult < 0.95
                    ? "你原以为对方会比现在更热，这次反而淡了些，你把热情收着点（好感涨得慢些）。别把「预判」说出口。"
                    : "对方这次跟你想的差不多，你心里没什么波澜。");
        }
        if (s.BrainSurpriseEnabled && card.Length > 0)
            parts.Add($"【意外】这一轮，只做一处出乎意料的：{card} 只做这一处，别解释，也别破坏人设。");
        return string.Join("\n", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
    }

    // ---- 好感结算：把本轮倍率兑现成好感/信任/积分增量 ----

    /// <summary>聊天好感：吃掉本轮倍率，用小数累积把 0.2~1.8 的浮动均摊到跨轮，返回本轮实际加多少。</summary>
    public int ApplyChatGain(string charId, int baseGain = 1)
    {
        double mult = 1.0;
        lock (_gate)
        {
            _pendingMultiplier.TryGetValue(charId, out mult);
            _pendingMultiplier.Remove(charId);
            _pendingCard.Remove(charId);
        }
        if (!MasterEnabled || !_settings.Current.BrainExpectationEnabled) return Math.Max(0, baseGain);
        var ch = Char(charId);
        if (ch is null) return baseGain;
        var brain = ch.State.Brain;
        var acc = brain.Carry + mult * baseGain;
        var gain = (int)Math.Floor(acc);
        brain.Carry = Math.Clamp(acc - gain, 0, 1.99);
        return Math.Clamp(gain, 0, 3);
    }

    /// <summary>礼物/摸头/拥抱/亲吻等动作：不走聊天倍率，用当前多巴胺换算一个温和倍率（0.6~1.4）。</summary>
    public int ScaleGain(string charId, int baseGain, bool minOne = true)
    {
        if (!MasterEnabled || !_settings.Current.BrainExpectationEnabled) return baseGain;
        var ch = Char(charId);
        if (ch is null) return baseGain;
        var d = ch.State.Brain.Dopamine;
        var mult = 1 + (d - 50) / 100.0 * 0.8;
        var v = (int)Math.Round(baseGain * mult);
        return minOne ? Math.Max(1, v) : Math.Max(0, v);
    }

    /// <summary>信息面板用的一句话状态（读得到「TA 现在什么心情」）。</summary>
    public string StatusText(string charId)
    {
        if (!MasterEnabled) return "";
        if (Char(charId) is not { } ch) return "";
        var s = _settings.Current;
        var text = s.BrainExpectationEnabled
            ? ch.State.Brain.Dopamine switch
            {
                >= 62 => "被你的回应打动，好感涨得快了些",
                <= 38 => "有点小失落，好感涨得慢了些",
                _ => "心里平静，好感照常"
            }
            : "心里平静";
        if (s.BrainSurpriseEnabled) text += " · 时不时给你一点意外";
        return text;
    }

    public bool IsVisible(string charId) => MasterEnabled && Char(charId) is not null;

    // ---- 可选：免费小模型每 N 轮补一次预判（缓存进存档、节流、只发最近一句） ----

    /// <summary>回合结束后调用（异步、非阻塞）：到点才问一次免费模型，结果写进 Brain.Hint / HintWarmth 供后续几轮复用。</summary>
    public async Task AfterTurnAsync(string charId, string userText, string reply)
    {
        var s = _settings.Current;
        if (!MasterEnabled || !s.BrainAiAssistEnabled) return;
        if (string.IsNullOrWhiteSpace(s.BrainAiModel)) return;
        var ch = Char(charId);
        if (ch is null) return;
        var brain = ch.State.Brain;
        var interval = Math.Max(2, s.BrainAiEveryTurns);
        if (brain.Turns < brain.HintTurn + interval) return;      // 还在缓存区间内，不重复花钱
        brain.HintTurn = brain.Turns;                              // 先占坑，防并发重复请求
        if (userText.Trim().Length < 4) return;                    // 空话不花模型钱
        try
        {
            var ep = ResolveAssistEndpoint(s);
            if (ep is null) return;
            var hit = await AskAssistAsync(ep, ch, brain, userText, reply);
            if (hit is not null)
            {
                brain.HintWarmth = hit.Value.expect / 100.0;
                brain.Hint = hit.Value.hint;
            }
        }
        catch (Exception ex) { App.WriteLog("BrainAnticipation.AfterTurn -> " + ex.Message); }
    }

    private AiEndpoint? ResolveAssistEndpoint(UserSettings s)
    {
        var model = s.BrainAiModel.Trim();
        if (model.Length == 0) return null;
        var url = string.IsNullOrWhiteSpace(s.BrainAiUrl) ? s.AiUrl : s.BrainAiUrl;
        if (url.Length == 0) return null;
        var key = string.IsNullOrWhiteSpace(s.BrainAiKey) ? s.AiKey : s.BrainAiKey;
        if (key.Length == 0) return null;
        return new AiEndpoint
        {
            Provider = "大脑预判",
            Url = url,
            Key = key,
            Model = model,
            Temperature = 0.3,
            MaxTokens = 80
        };
    }

    private async Task<(double expect, string hint)?> AskAssistAsync(
        AiEndpoint ep, CharacterData ch, BrainState brain, string user, string reply)
    {
        var who = string.IsNullOrWhiteSpace(ch.Profile.Name) ? "我" : ch.Profile.Name;
        var persona = (ch.Profile.Personality ?? "").Trim();
        if (persona.Length > 80) persona = persona[..80];
        var u = user.Trim(); if (u.Length > 160) u = u[^160..];
        var r = reply.Trim(); if (r.Length > 160) r = r[^160..];
        var personaLine = persona.Length > 0 ? $"。TA 是：{persona}" : "";
        var msg =
            $"你是「{who}」{personaLine}。\n" +
            $"对方刚说：{u}\n你回了：{r}\n" +
            $"此刻好感 {ch.State.Affection}/100，信任 {ch.State.Trust}/100。\n" +
            "请预判对方下一条会有多热（0-100），并给一个下一步「意料之外但符合人设」的小动作（14字内）。" +
            "只输出一行，格式：expect=数字;hint=小动作";
        var raw = await _api.Chat(new List<ChatMessage> { new() { Role = "user", Content = msg } }, ep);
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var expect = brain.ExpectedWarmth * 100;
        var mExp = Regex.Match(raw, "expect\\s*[=:：]\\s*(\\d{1,3})");
        if (mExp.Success && int.TryParse(mExp.Groups[1].Value, out var e))
            expect = Math.Clamp(e, 0, 100);
        var hint = "";
        var mHint = Regex.Match(raw, "hint\\s*[=:：]\\s*[\"“]?([^\"”\\n]{2,40})");
        if (mHint.Success) hint = mHint.Groups[1].Value.Trim();
        return (expect, hint.Length > 0 ? hint : "");
    }
}
