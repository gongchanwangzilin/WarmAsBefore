using WarmAsBefore.Models;

namespace WarmAsBefore.Services;

/// <summary>目录里的一条心理状况：名字、类别、典型严重度、常见表现、行为倾向。</summary>
public sealed record MentalCondition(
    string Id,
    string Name,
    string Category,
    int TypicalSeverity,
    string Symptoms,
    string Behavior,
    bool Safety = false);

/// <summary>某个角色身上的一条状况：严重度可以随时间与经历变化。</summary>
public sealed record MentalConditionState
{
    public string Id { get; init; } = "";
    /// <summary>1 轻度 / 2 中度 / 3 重度。</summary>
    public int Severity { get; set; } = 1;
    /// <summary>稳定度 0~1：越高越平稳，越低越容易被触发。来自被理解、被陪伴，而不是时间。</summary>
    public double Stability { get; set; } = 0.5;
    public DateTime Since { get; init; } = DateTime.UtcNow;
    public string Note { get; init; } = "";
}

public sealed class MentalHealthProfile
{
    public List<MentalConditionState> Active { get; set; } = new();
}

public sealed class MentalHealthBook
{
    public Dictionary<string, MentalHealthProfile> ByCharacter { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>
/// 心理状态：一个可编辑的目录（115 条，覆盖常见类型、脑与神经相关、创伤与焦虑、
/// 以及「高敏感」这类不算病的特质），加上每个角色身上实际带着的那几条。
///
/// 它只影响「怎么表现」，不替角色做诊断，也不把病名当标签贴在对话里。
/// 严重的精神病性症状、自伤/自杀/暴力冲动有硬性安全边界：
/// 可以写冲动与痛苦，不写方法、不美化、不教唆，始终把方向引向现实中的求助。
/// </summary>
public sealed class MentalHealthService
{
    private readonly StorageProvider _store;
    private readonly MemoryScope _scope;
    private readonly object _gate = new();
    private MentalHealthBook _book = new();
    private bool _loaded;

    public MentalHealthService(StorageProvider store, MemoryScope scope)
    {
        _store = store;
        _scope = scope;
        scope.Changed += ReleaseCache;
    }

    private string Key => _scope.Key("mental_health");

    private async Task EnsureLoadedAsync()
    {
        if (_loaded) return;
        try
        {
            var loaded = await _store.Load<MentalHealthBook>(Key);
            lock (_gate) { _book = loaded ?? new MentalHealthBook(); _loaded = true; }
        }
        catch (Exception ex)
        {
            App.WriteLog("MentalHealth.Load -> " + ex.Message);
            lock (_gate) _loaded = true;
        }
    }

    /// <summary>作用域换了（读档/新开局）：丢缓存重读。</summary>
    public void ReleaseCache()
    {
        lock (_gate) { _book = new MentalHealthBook(); _loaded = false; }
    }

    private async Task SaveAsync()
    {
        MentalHealthBook snap;
        lock (_gate) snap = new MentalHealthBook
        {
            ByCharacter = _book.ByCharacter.ToDictionary(
                kv => kv.Key,
                kv => new MentalHealthProfile { Active = kv.Value.Active.ToList() },
                StringComparer.Ordinal)
        };
        await _store.Save(Key, snap);
    }

    /// <summary>同步取某个角色的状况（未加载时为空；对话前会先 EnsureLoaded）。</summary>
    public IReadOnlyList<MentalConditionState> StatesOf(string charId)
    {
        lock (_gate)
            return _book.ByCharacter.TryGetValue(charId ?? "", out var p) ? p.Active.ToList() : Array.Empty<MentalConditionState>();
    }

    public async Task<IReadOnlyList<MentalConditionState>> ListAsync(string charId)
    {
        await EnsureLoadedAsync();
        return StatesOf(charId);
    }

    public MentalCondition? Find(string idOrName)
    {
        var key = (idOrName ?? "").Trim();
        if (key.Length == 0) return null;
        return Catalog.FirstOrDefault(c => string.Equals(c.Id, key, StringComparison.OrdinalIgnoreCase))
            ?? Catalog.FirstOrDefault(c => string.Equals(c.Name, key, StringComparison.Ordinal))
            ?? Catalog.FirstOrDefault(c => c.Name.Contains(key, StringComparison.Ordinal));
    }

    public async Task<(bool Ok, string Message)> SetAsync(string charId, string idOrName, int severity, string note = "")
    {
        await EnsureLoadedAsync();
        var c = Find(idOrName);
        if (c is null) return (false, $"目录里没有「{idOrName}」。可以先用 mental_catalog 查一下名字。");
        var s = Math.Clamp(severity <= 0 ? c.TypicalSeverity : severity, 1, 3);
        lock (_gate)
        {
            if (!_book.ByCharacter.TryGetValue(charId, out var p))
                _book.ByCharacter[charId] = p = new MentalHealthProfile();
            var existing = p.Active.FirstOrDefault(x => x.Id == c.Id);
            if (existing is not null)
            {
                existing.Severity = s;
                if (!string.IsNullOrWhiteSpace(note)) p.Active[p.Active.IndexOf(existing)] = existing with { Note = note };
            }
            else p.Active.Add(new MentalConditionState { Id = c.Id, Severity = s, Note = note ?? "" });
        }
        await SaveAsync();
        return (true, $"{charId} 的「{c.Name}」已记为{SeverityName(s)}。");
    }

    public async Task<bool> RemoveAsync(string charId, string idOrName)
    {
        await EnsureLoadedAsync();
        var c = Find(idOrName);
        if (c is null) return false;
        bool removed;
        lock (_gate)
        {
            removed = _book.ByCharacter.TryGetValue(charId, out var p)
                      && p.Active.RemoveAll(x => x.Id == c.Id) > 0;
        }
        if (removed) await SaveAsync();
        return removed;
    }

    public static string SeverityName(int severity) => severity switch
    {
        >= 3 => "重度",
        2 => "中度",
        _ => "轻度"
    };

    /// <summary>
    /// 稳定度调整：被理解/被陪伴往上走，冲突/被否定往下走。时间本身几乎不改变它
    /// —— 这是「特定的经历才会让症状松动」的落点。
    /// </summary>
    public async Task AdjustStabilityAsync(string charId, double delta)
    {
        await EnsureLoadedAsync();
        lock (_gate)
        {
            if (!_book.ByCharacter.TryGetValue(charId, out var p)) return;
            foreach (var s in p.Active)
                s.Stability = Math.Clamp(s.Stability + delta, 0, 1);
        }
        await SaveAsync();
    }

    /// <summary>从一段对话里读情绪气候：不是诊断，只是让稳定度跟着相处方式变化。</summary>
    public async Task AfterTurnAsync(string charId, string userText, string? reply)
    {
        await EnsureLoadedAsync();
        var text = (userText ?? "") + "\n" + (reply ?? "");
        double delta = 0;
        if (ContainsAny(text, "抱抱", "别怕", "我在", "陪你", "理解", "没关系", "慢慢来", "辛苦", "谢谢你")) delta += 0.03;
        if (ContainsAny(text, "滚", "讨厌你", "闭嘴", "烦死", "废物", "没用", "去死", "恶心")) delta -= 0.05;
        if (ContainsAny(text, "对不起", "是我不好", "我错了")) delta += 0.01;
        if (Math.Abs(delta) < 0.001) return;
        await AdjustStabilityAsync(charId, delta);
    }

    private static bool ContainsAny(string text, params string[] words)
        => words.Any(w => text.Contains(w, StringComparison.Ordinal));

    /// <summary>人设里的一段：只在真的有状况时出现，不把目录倒给模型。</summary>
    public string PromptText(string charId)
    {
        var states = StatesOf(charId);
        if (states.Count == 0) return "";
        var sb = new System.Text.StringBuilder("【心理状态】这是你正在承受的东西，不是标签，也不要在对话里给自己贴病名。\n");
        foreach (var st in states)
        {
            var c = Find(st.Id);
            if (c is null) continue;
            var steady = st.Stability switch
            {
                >= 0.75 => "最近比较平稳",
                >= 0.45 => "时好时坏",
                >= 0.25 => "很容易被触动",
                _ => "正处在很艰难的时候"
            };
            sb.Append("- ").Append(c.Name).Append('（').Append(SeverityName(st.Severity)).Append("，").Append(steady).Append("）：")
              .Append(c.Symptoms).Append(" 表现上：").Append(c.Behavior);
            if (!string.IsNullOrWhiteSpace(st.Note)) sb.Append(" 具体到你自己：").Append(st.Note);
            sb.Append('\n');
        }
        sb.Append("把它活成细微的反应（停顿、回避、抓紧衣角、忽然沉默），不要每句话都提；被温柔对待时会慢慢放松一点点。");
        return sb.ToString().TrimEnd();
    }

    /// <summary>硬性安全边界：存在自伤/自杀/暴力冲动或重度症状时追加。</summary>
    public string SafetyText(string charId)
    {
        var states = StatesOf(charId);
        var danger = states.Any(s => Find(s.Id) is { Safety: true } || s.Severity >= 3);
        if (!danger) return "";
        return "【安全边界】你可以描写痛苦、冲动和想消失的念头，但绝不写具体方法、步骤、剂量或工具；"
             + "不美化、不浪漫化、不把伤害说成解决问题的办法；不鼓励、不激将、不替对方做决定。"
             + "如果对方流露出伤害自己的意思，先停下来关心，明确说「我很在意你现在的安全」，"
             + "鼓励对方联系身边可信任的人或当地紧急援助；保持角色的语气，但安全优先于剧情。"
             + "暴力冲动只能作为内心挣扎被看见，不能变成对任何人的伤害行为描写。";
    }

    /// <summary>给工具用的目录文本（按类别分组，只给名字与一句表现）。</summary>
    public static string CatalogText(string? keyword = null)
    {
        var items = Catalog.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var k = keyword.Trim();
            items = items.Where(c => c.Name.Contains(k, StringComparison.Ordinal)
                || c.Category.Contains(k, StringComparison.Ordinal)
                || c.Symptoms.Contains(k, StringComparison.Ordinal));
        }
        var sb = new System.Text.StringBuilder();
        foreach (var g in items.GroupBy(c => c.Category))
        {
            sb.Append("【").Append(g.Key).Append("】\n");
            foreach (var c in g)
                sb.Append("- ").Append(c.Name).Append("（").Append(c.Id).Append("，常见")
                  .Append(SeverityName(c.TypicalSeverity)).Append("）：").Append(c.Symptoms).Append('\n');
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// 115 条目录。严重度只是「常见起点」，实际以角色身上那条为准。
    /// 不作诊断用，只作叙事与共情的素材。
    /// </summary>
    public static readonly IReadOnlyList<MentalCondition> Catalog = new List<MentalCondition>
    {
        // —— 情绪障碍 ——
        new("depression", "抑郁症", "情绪", 2, "持续低落、兴趣减退、精力下降，觉得自己是负担", "话变少，容易累，接受好意时反而局促"),
        new("mdd", "重度抑郁障碍", "情绪", 3, "几乎每天低落，睡眠与食欲紊乱，动作迟缓", "长时间沉默，起床困难，自我否定明显"),
        new("dysthymia", "持续性抑郁障碍", "情绪", 1, "长期的低落与自我怀疑，像一层灰", "习惯性压低期待，笑得很轻"),
        new("bipolar1", "双相I型障碍", "情绪", 3, "躁狂与抑郁交替，躁期睡眠需求骤减、话多", "情绪落差大，事后会为躁期的言行羞愧"),
        new("bipolar2", "双相II型障碍", "情绪", 2, "轻躁与重抑郁交替，轻躁时格外有劲", "短暂的高涨后是漫长的低落"),
        new("cyclothymia", "环性心境障碍", "情绪", 1, "情绪在轻高与轻低之间长期摆动", "状态起伏频繁，自己也能察觉"),
        new("dmdd", "破坏性心境失调障碍", "情绪", 2, "长期易怒，爆发与年龄不相称", "小事也会炸，炸完又懊悔"),
        new("pmdd", "经前烦躁障碍", "情绪", 2, "经前一周情绪剧烈波动、易怒与绝望感", "周期性地觉得一切都糟透了"),
        new("postpartum_depression", "产后抑郁", "情绪", 2, "疲惫、内疚、对婴儿既爱又无力", "怕被说不配当妈妈，因此藏起难过"),
        new("sad", "季节性情绪失调", "情绪", 1, "秋冬光照少时低落、嗜睡、想吃碳水", "一到阴天就提不起劲"),

        // —— 焦虑 ——
        new("gad", "广泛性焦虑障碍", "焦虑", 2, "长期担心各种事，肌肉紧绷，难以放松", "反复确认、提前焦虑还没发生的事"),
        new("panic_disorder", "惊恐障碍", "焦虑", 2, "反复的惊恐发作，并害怕再次发作", "避免去人多或难以离开的地方"),
        new("agoraphobia", "广场恐惧症", "焦虑", 2, "害怕拥挤、排队、公共交通等难以逃离的场合", "出门前反复规划退路"),
        new("social_anxiety", "社交焦虑障碍", "焦虑", 2, "害怕被评价，公开场合脸红、发抖", "话到嘴边又咽回去，事后反复回想"),
        new("specific_phobia", "特定恐惧症", "焦虑", 1, "对特定物体或场景的强烈恐惧", "遇到触发物会明显回避"),
        new("separation_anxiety", "分离焦虑障碍", "焦虑", 2, "害怕与依恋对象分开，独处时不安", "频繁确认你还在，不愿挂电话"),
        new("selective_mutism", "选择性缄默症", "焦虑", 2, "在某些场合完全说不出话，在家却正常", "在陌生人面前僵住，用点头代替回答"),
        new("illness_anxiety", "疾病焦虑障碍", "焦虑", 2, "反复担心自己得了重病，检查也不能安心", "频繁搜索症状，对身体感觉过度警觉"),
        new("panic_attack", "惊恐发作", "焦虑", 2, "突发的强烈恐惧，心跳加速、喘不上气、发麻", "发作时抓住身边的东西，怕自己会死"),
        new("test_anxiety", "考试焦虑", "焦虑", 1, "考前过度紧张，注意力与记忆受影响", "考前失眠，进考场手心出汗"),

        // —— 创伤与应激 ——
        new("ptsd", "创伤后应激障碍", "创伤", 3, "闪回、回避、警觉性增高，持续数月以上", "被相似的气味或声音拽回当时"),
        new("cptsd", "复杂性创伤后应激障碍", "创伤", 3, "长期人际创伤后的情绪失调、羞耻感与自我破碎", "既渴望靠近又害怕靠近"),
        new("acute_stress", "急性应激障碍", "创伤", 2, "创伤后数天内的麻木、解离与反复回想", "像隔着一层玻璃看世界"),
        new("adjustment_disorder", "适应障碍", "创伤", 1, "面对生活变化后的持续痛苦与功能下降", "换环境后久久缓不过来"),
        new("dissociative_amnesia", "解离性遗忘", "创伤", 2, "记不起重要的个人信息或某段时间", "回过神来发现时间跳过去了"),
        new("depersonalization", "人格解体/现实解体障碍", "创伤", 2, "觉得自己不真实，或世界像布景", "照镜子时认不出那是自己"),
        new("did", "解离性身份障碍", "创伤", 3, "身份状态交替，伴随记忆断层", "有时用陌生的语气说话，事后不记得"),
        new("vicarious_trauma", "替代性创伤", "创伤", 2, "反复接触他人创伤后出现类似应激反应", "听多了别人的痛，自己也睡不好"),
        new("reexperiencing", "创伤再体验发作", "创伤", 2, "被触发后短时间内重新经历当时的感受", "突然发抖、说不出话，需要很久回神"),
        new("betrayal_trauma", "背叛创伤", "创伤", 2, "被最信任的人伤害后，对亲密关系长期戒备", "越亲近越害怕，先推开再说"),

        // —— 强迫与侵入性 ——
        new("ocd", "强迫症", "强迫", 2, "反复的侵入性念头与为缓解焦虑而做的仪式", "检查、清洗、默念，明知没必要也停不下"),
        new("bdd", "躯体变形障碍", "强迫", 2, "对想象中的外貌缺陷过度关注", "反复照镜子，躲避拍照"),
        new("hoarding", "囤积障碍", "强迫", 2, "难以丢弃物品，空间被占据", "把旧东西都留着，觉得丢了会发生坏事"),
        new("trichotillomania", "拔毛癖", "强迫", 1, "反复拔自己的头发、眉毛", "紧张时不自觉地摸头发"),
        new("skin_picking", "皮肤抠抓障碍", "强迫", 1, "反复抠抓皮肤直到破损", "让伤口结不了痂"),
        new("compulsive_counting", "强迫性计数", "强迫", 1, "不做某个次数的重复就极度不安", "走路要数格子，洗手要数遍数"),
        new("compulsive_checking", "强迫性检查", "强迫", 1, "反复确认门锁、煤气、消息是否出错", "出门后又折返确认"),
        new("intrusive_thoughts", "侵入性思维", "强迫", 1, "不受欢迎的念头突然闯入，令人羞耻", "越不想想越会想，然后自责"),
        new("intrusive_violence", "侵入性暴力画面", "强迫", 2, "脑中闪过伤害他人的画面，但并无意愿", "被画面吓到自己，害怕说出来被误解"),
        new("self_fear", "自我恐惧", "强迫", 2, "害怕自己会失控、会变成不想成为的人", "对独处或持物产生莫名的戒备"),

        // —— 进食 ——
        new("anorexia", "神经性厌食症", "进食", 3, "限制进食、体重过低仍怕胖，身体机能受损", "对食物紧张，习惯性说自己不饿"),
        new("bulimia", "神经性贪食症", "进食", 2, "暴食后以催吐、运动等方式补偿", "饭后长时间待在洗手间"),
        new("binge_eating", "暴食障碍", "进食", 2, "反复无法控制的暴食，伴羞耻感", "独自吃很多，然后讨厌自己"),
        new("arfid", "回避性摄食障碍", "进食", 1, "对食物质地、气味极度敏感，进食种类很少", "对没吃过的食物本能拒绝"),
        new("pica", "异食癖", "进食", 2, "持续吃非营养性物质", "偷偷吃不该吃的东西，并为此羞耻"),
        new("rumination_disorder", "反刍障碍", "进食", 1, "反复反刍已咽下的食物", "饭后不自觉地反刍，又咽回去"),

        // —— 睡眠 ——
        new("insomnia", "失眠障碍", "睡眠", 2, "入睡困难或早醒，白天疲惫", "夜里清醒，越急越睡不着"),
        new("hypersomnia", "嗜睡障碍", "睡眠", 2, "夜间睡很久白天仍困，影响生活", "总是困，随时随地想睡"),
        new("narcolepsy", "发作性睡病", "睡眠", 3, "日间不可抑制地入睡，可能伴猝倒", "情绪激动时突然腿软"),
        new("sleep_apnea", "阻塞性睡眠呼吸暂停", "睡眠", 2, "夜间呼吸暂停、打鼾，白天头痛乏力", "睡醒了也像没睡"),
        new("circadian", "昼夜节律睡眠觉醒障碍", "睡眠", 1, "作息与社会时钟错位", "越晚越精神，早起像酷刑"),
        new("nightmare", "梦魇障碍", "睡眠", 2, "反复的痛苦梦境，醒后久久不能平静", "怕睡觉，拖到撑不住才闭眼"),
        new("sleep_terror", "睡惊症", "睡眠", 2, "深睡中突然惊叫、心跳剧烈，醒后不记得", "半夜坐起发抖，叫不醒"),
        new("sleepwalking", "睡行症", "睡眠", 1, "睡眠中起身走动，醒后无记忆", "第二天发现东西被挪动"),
        new("rls", "不宁腿综合征", "睡眠", 1, "静息时腿部难以忍受的不适，必须动", "晚上躺下就腿难受"),

        // —— 精神病性 ——
        new("schizophrenia", "精神分裂症", "精神病性", 3, "思维、知觉与情感的综合失调，可有幻觉妄想", "说话跳跃，对周围反应迟钝"),
        new("schizoaffective", "分裂情感性障碍", "精神病性", 3, "精神病性症状与情绪发作同时存在", "在幻觉与低落之间摇摆"),
        new("brief_psychotic", "短暂精神病性障碍", "精神病性", 3, "突然出现精神病性症状，持续不足一个月", "短时间里像变了一个人"),
        new("delusional", "妄想障碍", "精神病性", 3, "持续的非怪诞妄想，其他功能相对保留", "对某个信念坚信不疑，无法被说服"),
        new("paranoid_personality", "偏执型人格障碍", "精神病性", 2, "长期不信任他人，把善意解读成算计", "反复揣测你话里的意思"),
        new("auditory_hallucination", "幻听", "精神病性", 3, "听到不存在的声音，可能是评论或命令", "突然侧耳，像在听什么"),
        new("persecution_delusion", "被害妄想", "精神病性", 3, "坚信自己被迫害、被监视", "检查房间角落，回避陌生人"),
        new("cotard", "科塔尔综合征", "精神病性", 3, "坚信自己已经死去或内脏消失", "说自己不存在，拒绝进食"),
        new("capgras", "卡普格拉斯综合征", "精神病性", 3, "坚信亲近的人被替身取代", "说你像她认识的人，但又不是"),

        // —— 人格 ——
        new("bpd", "边缘型人格障碍", "人格", 3, "情绪剧烈波动、害怕被抛弃、自我认同不稳", "极度靠近又突然推开，事后强烈自责"),
        new("npd", "自恋型人格障碍", "人格", 2, "需要被仰慕，对批评反应强烈，共情困难", "被忽视时愤怒或贬低对方"),
        new("histrionic", "表演型人格障碍", "人格", 2, "情绪表达夸张，渴望成为注意中心", "用戏剧化的方式获得回应"),
        new("dependent_personality", "依赖型人格障碍", "人格", 2, "过度需要他人照顾，难以独自做决定", "反复问你「我该怎么办」"),
        new("avoidant_personality", "回避型人格障碍", "人格", 2, "害怕被拒绝，回避人际接触", "想靠近又提前退开"),
        new("ocpd", "强迫型人格障碍", "人格", 2, "追求秩序与控制，难以容忍不确定", "对细节反复掌控，很难放松"),
        new("schizoid", "分裂样人格障碍", "人格", 2, "情感疏离，偏好独处", "不擅长回应亲密，容易显得冷淡"),
        new("antisocial", "反社会型人格障碍", "人格", 3, "漠视他人权利与规则，缺乏悔意", "冲动、操纵、对后果无感"),
        new("passive_aggressive", "被动攻击型人格", "人格", 1, "用拖延、阴阳表达愤怒", "嘴上说没事，行动上处处不配合"),
        new("depressive_personality", "抑郁型人格", "人格", 1, "长期的悲观、自责与严肃", "习惯把责任往自己身上揽"),

        // —— 神经发育与认知 ——
        new("adhd", "注意缺陷多动障碍", "神经发育", 2, "注意力难集中、冲动、坐不住", "话说到一半跳去别处，常常忘记东西"),
        new("asd", "自闭谱系障碍", "神经发育", 2, "社交沟通差异、兴趣狭窄、需要规律", "对感官刺激敏感，需要提前知道安排"),
        new("tourette", "抽动秽语综合征", "神经发育", 2, "不自主的动作或发声抽动", "紧张时抽动更明显"),
        new("dyslexia", "阅读障碍", "神经发育", 1, "解码文字困难，阅读慢且易错", "看长文字会累，习惯听而不读"),
        new("dyscalculia", "计算障碍", "神经发育", 1, "数字与数量理解困难", "算账要很久，容易算错"),
        new("dcd", "发育性协调障碍", "神经发育", 1, "动作笨拙，精细动作与平衡困难", "容易撞到东西，写字歪斜"),
        new("mci", "轻度认知障碍", "神经发育", 2, "记忆与思考较同龄人下降，但尚能自理", "反复问同一件事，找词困难"),
        new("alzheimer", "阿尔茨海默病", "神经发育", 3, "进行性记忆与认知衰退", "忘记刚发生的事，认不出熟悉的人"),
        new("vascular_dementia", "血管性痴呆", "神经发育", 3, "脑血管病变导致的阶梯式认知下降", "思考变慢，情绪不稳"),
        new("ftd", "额颞叶痴呆", "神经发育", 3, "人格与语言改变早于记忆衰退", "变得不像原来的自己"),

        // —— 神经与身体 ——
        new("temporal_epilepsy", "颞叶癫痫", "神经", 2, "局灶性发作，可伴陌生感、恐惧或自动症", "突然愣神、咀嚼或摸索衣物"),
        new("migraine", "偏头痛", "神经", 1, "反复单侧搏动性头痛，畏光畏声", "发作时只想待在黑暗里"),
        new("post_concussion", "脑震荡后综合征", "神经", 2, "头部受伤后持续头痛、头晕、注意力差", "累了就头疼，想事情变慢"),
        new("chronic_fatigue", "慢性疲劳综合征", "神经", 2, "长期极度疲劳，活动后加重", "稍微用力就要缓好几天"),
        new("fibromyalgia", "纤维肌痛", "神经", 2, "广泛性疼痛、疲劳与睡眠障碍", "碰一下就疼，说不清哪里痛"),
        new("functional_neuro", "功能性神经症状障碍", "神经", 2, "神经系统功能异常但无器质性病变", "症状真实存在，检查却正常"),

        // —— 躯体 ——
        new("somatic_symptom", "躯体症状障碍", "躯体", 2, "持续的身体不适与过度担忧", "反复就医，仍不放心"),
        new("conversion", "转换障碍", "躯体", 2, "心理冲突转化为运动或感觉功能丧失", "突然看不见或走不了，检查无异常"),
        new("pain_disorder", "慢性疼痛障碍", "躯体", 2, "长期疼痛显著影响情绪与生活", "疼得说不出话，又怕被当成矫情"),
        new("pnes", "心因性非癫痫发作", "躯体", 2, "类似癫痫发作但脑电无对应放电", "发作后疲惫、困惑"),

        // —— 冲动与成瘾 ——
        new("ied", "间歇性暴怒障碍", "冲动", 2, "反复的冲动性暴怒，强度与诱因不成比例", "爆发后懊悔，但当时控制不住"),
        new("gambling", "病理性赌博", "冲动", 2, "无法控制赌博冲动，追逐损失", "输得越多越想翻本"),
        new("gaming", "游戏障碍", "冲动", 1, "游戏优先于生活其他部分，难以停止", "说好最后一局，又开了一局"),
        new("internet_addiction", "网络成瘾", "冲动", 1, "上网时间失控，影响睡眠与现实关系", "一放下手机就空"),
        new("alcohol_use", "酒精使用障碍", "冲动", 2, "难以控制饮酒，耐受与戒断", "用酒压住情绪"),
        new("substance_use", "物质使用障碍", "冲动", 3, "物质使用失控，影响健康与关系", "明知有害也停不下"),
        new("compulsive_buying", "强迫性购物", "冲动", 1, "反复冲动消费以缓解情绪", "买完就后悔，下次还是买"),
        new("sexual_impulse", "性冲动控制障碍", "冲动", 2, "反复的性冲动行为难以控制，影响生活", "事后羞耻，又难以停止"),

        // —— 敏感与轻微特质（不算病，是一种体质） ——
        new("hsp", "高敏感体质", "特质", 1, "对声音、情绪与细节的感受更强烈", "容易被环境和别人的情绪淹没"),
        new("empathy_overload", "共情过载", "特质", 1, "过度吸收他人的情绪，难以抽离", "听完别人的事，自己缓不过来"),
        new("emotional_sponge", "情绪海绵", "特质", 1, "不自觉地吸收周围人的情绪", "房间里有人不高兴，自己也会沉下去"),
        new("sound_sensitivity", "声音敏感", "特质", 1, "对特定声音难以忍受", "听到咀嚼声就烦躁"),
        new("light_sensitivity", "光敏感", "特质", 1, "强光下头痛、眼酸", "宁愿待在暗一点的房间"),
        new("touch_sensitivity", "触觉敏感", "特质", 1, "对衣物标签、拥抱力度等异常在意", "被突然碰到会一激灵"),
        new("alexithymia", "述情障碍", "特质", 1, "难以识别与描述自己的情绪", "被问感受时只会说「还行」"),
        new("synesthesia", "联觉", "特质", 1, "一种感官会带出另一种感官体验", "听到某个名字会看见颜色"),
        new("impostor", "冒名顶替感", "特质", 1, "总觉得自己是骗来的，怕被看穿", "被夸时不安，觉得只是运气"),
        new("over_empathy", "过度共情", "特质", 1, "把别人的痛苦当成自己的责任", "宁可自己难受也要让别人好过"),

        // —— 需要硬性安全边界的条目 ——
        new("suicidal_ideation", "被动自杀意念", "安全", 3, "反复觉得「消失就好了」，未必有具体计划", "望着窗外或高处时会走神", Safety: true),
        new("self_harm_urge", "自伤冲动", "安全", 3, "在强烈情绪下想用疼痛让自己安静下来", "情绪上头时攥紧手、想找尖锐物", Safety: true),
        new("aggression_impulse", "暴力冲动", "安全", 3, "在极端愤怒时闪过伤害他人的念头，随即被自己吓到", "手抖、呼吸变重，之后强烈自责", Safety: true)
    };
}
