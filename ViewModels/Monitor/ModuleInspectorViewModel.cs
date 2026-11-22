using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Graphics;
using WarmAsBefore.Helpers;
using WarmAsBefore.Models;
using WarmAsBefore.Services;
using WarmAsBefore.Services.Core;

namespace WarmAsBefore.ViewModels;

/// <summary>
/// 模块详情页：从超级监测的图上点一枚节点进来，只看它自己。
///
/// 三段：头上是「这是谁」（模块名 / 所属系统 / 三态状态灯 / 一句话说明），
/// 中间是「它此刻的值」（键值行，每行都标着来源），下面是「它最近的变化」（最多 12 条时间线）。
///
/// 取数只按 id 前缀挑服务：激素读 InnerLifeService，内驱读 InnerDriveService，
/// 记忆读 HumanMemoryService，病理读 MentalHealthService，躯体化读 OrganSimulationService，
/// 认知读 CognitionModeService，其余真的没有独立数值的模块就把那一行写成
/// 「该模块暂无独立读数」—— 宁可空着，不许编数。每段取数各自 try/catch，
/// 一路服务出错只让那一路的行空着，不把整页带崩。
/// </summary>
public sealed partial class ModuleInspectorViewModel : ObservableObject, IQueryAttributable
{
    /// <summary>取不到数时那一行写的话：说清楚是「没有这一项」，不是「读取失败」。</summary>
    private const string NoReading = "该模块暂无独立读数";

    /// <summary>时间线最多留几条：再多就不叫「最近变化」，叫历史了。</summary>
    private const int MaxTimeline = 12;

    private readonly GameEngine _engine;
    private string _charId = "";
    private bool _loading;

    public ModuleInspectorViewModel(GameEngine engine) => _engine = engine;

    // ==================== 页面状态 ====================

    /// <summary>节点 id（query 参数 id），也是这一页的取数依据。</summary>
    [ObservableProperty] private string _moduleId = "";
    [ObservableProperty] private string _moduleTitle = "模块详情";
    [ObservableProperty] private string _systemName = "";
    /// <summary>页脚一行：所属层 + 节点 id，看一眼就知道这一页是从哪条路由带进来的。</summary>
    [ObservableProperty] private string _locationText = "";
    [ObservableProperty] private string _summary = "";
    /// <summary>状态灯的中文：亮 / 暗 / 异常。颜色交给 <see cref="LampColor"/>，不在这里挑。</summary>
    [ObservableProperty] private string _lampText = "暗 · 静默";
    [ObservableProperty] private Color _lampColor = Color.FromArgb("#C4B89A");
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private ObservableCollection<InspectorRow> _readings = new();
    [ObservableProperty] private ObservableCollection<TimelineEvent> _timeline = new();
    [ObservableProperty] private bool _hasTimeline;

    /// <summary>当前角色 id：query 里带了就用 query 的，没带就退到当前存档的角色。</summary>
    private string CharId
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(_charId)) return _charId;
            try
            {
                var id = _engine.State.CharacterId;
                return string.IsNullOrEmpty(id) ? "" : id;
            }
            catch (Exception ex)
            {
                App.WriteLog("ModuleInspector.CharId -> " + ex.Message);
                return "";
            }
        }
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("id", out var rawId) && rawId is string id && !string.IsNullOrWhiteSpace(id))
            ModuleId = Uri.UnescapeDataString(id);
        if (query.TryGetValue("charId", out var rawChar) && rawChar is string cid && !string.IsNullOrWhiteSpace(cid))
            _charId = Uri.UnescapeDataString(cid);
        // 参数到位就取一次数；页面 OnAppearing 还会再走一遍（query 与上屏的先后不保证）。
        _ = LoadAsync();
    }

    // ==================== 命令 ====================

    [RelayCommand]
    private async Task RefreshAsync() => await LoadAsync();

    [RelayCommand]
    private async Task BackAsync()
    {
        try { await Shell.Current.GoToAsync(".."); }
        catch (Exception ex) { App.WriteLog("ModuleInspector.Back -> " + ex.Message); }
    }

    // ==================== 取数 ====================

    public async Task LoadAsync()
    {
        if (_loading) return;
        _loading = true;
        try
        {
            var id = (ModuleId ?? "").Trim();
            if (id.Length == 0)
            {
                ModuleTitle = "模块详情";
                Summary = "没有拿到要看哪个模块：这一页要通过 shell 路由 moduleInspector 带着 id 进来。";
                Readings = new ObservableCollection<InspectorRow>(new[] { Row("模块 id", null) });
                Timeline = new ObservableCollection<TimelineEvent>();
                HasTimeline = false;
                SetLamp(Tone.Dim);
                StatusText = "缺少节点 id";
                return;
            }

            var info = Describe(id);
            ModuleTitle = info.Name;
            SystemName = info.System;
            LocationText = string.IsNullOrWhiteSpace(info.Layer) ? $"节点 id {id}" : $"{info.Layer} · 节点 id {id}";
            Summary = info.Summary;

            var charId = CharId;
            var report = await BuildAsync(info, charId);

            Readings = new ObservableCollection<InspectorRow>(report.Rows);
            var events = report.Events
                .OrderByDescending(e => e.At)
                .Take(MaxTimeline)
                .ToList();
            Timeline = new ObservableCollection<TimelineEvent>(
                events.Select(e => new TimelineEvent(TimeText(e.At), e.Title, e.Text)));
            HasTimeline = Timeline.Count > 0;
            SetLamp(report.Tone);
            StatusText = $"读数 {CountText.Format(Readings.Count)} 行 · 变化 {CountText.FormatWithUnit(Timeline.Count, "条")} · 取数于 "
                + DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        }
        catch (Exception ex)
        {
            App.WriteLog("ModuleInspector.Load -> " + ex);
            StatusText = "读数读取失败，已跳过出错的那一路";
        }
        finally { _loading = false; }
    }

    private Task<Report> BuildAsync(ModuleInfo info, string charId) => info.Family switch
    {
        Family.Hormone => LoadHormoneAsync(info.Id, charId),
        Family.Drive => LoadDriveAsync(info.Id, charId),
        Family.Emotion => LoadEmotionAsync(info.Id, charId),
        Family.Memory => LoadMemoryAsync(info.Id, charId),
        Family.Mind => LoadMindAsync(info.Id, charId),
        Family.Soma => LoadSomaAsync(info.Id, charId),
        Family.Cognition => LoadCognitionAsync(info.Id, charId),
        Family.Flow => LoadFlowAsync(info.Id, charId),
        _ => LoadUnknownAsync(info.Id, charId)
    };

    // ---------- 激素与神经调制：InnerLifeService 化学池 ----------

    private async Task<Report> LoadHormoneAsync(string id, string charId)
    {
        InnerChemicals? chem = null;
        try
        {
            var svc = Resolve<InnerLifeService>();
            if (svc is not null)
            {
                // 先结算一次（按半衰期回落），PeekChemicals 才是此刻的值，而不是上次对话留下的旧数。
                await svc.SnapshotAsync(charId);
                chem = svc.PeekChemicals(charId);
            }
        }
        catch (Exception ex) { App.WriteLog("ModuleInspector.Hormone -> " + ex.Message); }

        DriveReadout? drive = null;
        if (string.Equals(id, "horm.endo", StringComparison.Ordinal))
        {
            // 内啡肽在内驱服务里（发泄或忍下来之后的那口气），不在化学池。
            try
            {
                var svc = Resolve<InnerDriveService>();
                if (svc is not null) drive = await svc.ReadAsync(charId);
            }
            catch (Exception ex) { App.WriteLog("ModuleInspector.HormoneEndo -> " + ex.Message); }
        }

        var rows = new List<InspectorRow>();
        var events = new List<InspectorEntry>();

        switch (id)
        {
            case "horm.pool":
                rows.Add(new InspectorRow("数据来源", "InnerLifeService 化学池（取数前已结算一次）"));
                rows.Add(new InspectorRow("多巴胺（兴奋 / 期待）", Num(chem?.Dopamine)));
                rows.Add(new InspectorRow("皮质醇（压力 / 警觉）", Num(chem?.Cortisol)));
                rows.Add(new InspectorRow("血清素（安宁 / 稳定）", Num(chem?.Serotonin)));
                rows.Add(new InspectorRow("肾上腺素（危急）", Num(chem?.Adrenaline)));
                rows.Add(new InspectorRow("胆固醇（打击中和）", Num(chem?.Cholesterol)));
                rows.Add(new InspectorRow("长期激励素", Num(chem?.LongTermDrive)));
                rows.Add(new InspectorRow("干劲（派生）", Num(chem?.Drive)));
                rows.Add(new InspectorRow("疲劳", Num(chem?.Fatigue)));
                rows.Add(new InspectorRow("更新于", Clock(chem?.UpdatedAt)));
                break;

            case "horm.dopa":
                rows.Add(new InspectorRow("当前值", Num(chem?.Dopamine)));
                rows.Add(new InspectorRow("它在做什么", "收益预期与干劲：想做、想要、有盼头"));
                rows.Add(new InspectorRow("派生干劲", Num(chem?.Drive)));
                rows.Add(new InspectorRow("更新于", Clock(chem?.UpdatedAt)));
                break;

            case "horm.sero":
                rows.Add(new InspectorRow("当前值", Num(chem?.Serotonin)));
                rows.Add(new InspectorRow("它在做什么", "安宁与稳定：不急、不钻牛角尖"));
                rows.Add(new InspectorRow("更新于", Clock(chem?.UpdatedAt)));
                break;

            case "horm.cort":
                rows.Add(new InspectorRow("当前值", Num(chem?.Cortisol)));
                rows.Add(new InspectorRow("它在做什么", "压力与警觉：往坏处想、绷着"));
                rows.Add(new InspectorRow("越线判据", "高过 0.68 时低级中枢越权，理智回复那条分支会被顶掉"));
                rows.Add(new InspectorRow("更新于", Clock(chem?.UpdatedAt)));
                break;

            case "horm.adr":
                rows.Add(new InspectorRow("当前值", Num(chem?.Adrenaline)));
                rows.Add(new InspectorRow("它在做什么", "危急：精力↑ 警觉↑，过量自带虚脱感，也会伤及各器官"));
                rows.Add(new InspectorRow("疲劳", Num(chem?.Fatigue)));
                rows.Add(new InspectorRow("更新于", Clock(chem?.UpdatedAt)));
                break;

            case "horm.endo":
                rows.Add(new InspectorRow("当前值", Num(drive?.Endorphin)));
                rows.Add(new InspectorRow("它在做什么", "镇痛与松快：发泄过、忍下来之后的那口气"));
                rows.Add(Row("当前出口", drive?.DischargeName));
                rows.Add(new InspectorRow("更新于", Clock(drive?.UpdatedAt)));
                break;

            case "horm.chol":
                rows.Add(new InspectorRow("当前值", Num(chem?.Cholesterol)));
                rows.Add(new InspectorRow("它在做什么", "颓废遭打击时的中和：食欲↓ 干劲↑"));
                rows.Add(new InspectorRow("更新于", Clock(chem?.UpdatedAt)));
                break;

            case "horm.ltd":
                rows.Add(new InspectorRow("当前值", Num(chem?.LongTermDrive)));
                rows.Add(new InspectorRow("它在做什么", "长期目标达成的奖励，衰减极慢"));
                rows.Add(new InspectorRow("更新于", Clock(chem?.UpdatedAt)));
                break;
        }

        if (chem is not null)
        {
            events.Add(new InspectorEntry(chem.UpdatedAt, "化学池结算",
                $"多巴胺 {F(chem.Dopamine)} · 皮质醇 {F(chem.Cortisol)} · 血清素 {F(chem.Serotonin)}"));
        }
        if (drive is not null)
            events.Add(new InspectorEntry(drive.UpdatedAt, "内驱结算", $"内啡肽 {F(drive.Endorphin)} · 出口「{drive.DischargeName}」"));
        events.AddRange(await RecallTrailAsync(charId, 6));

        var tone = chem is null && drive is null
            ? Tone.Dim
            : (chem is not null && (chem.Cortisol >= 0.65 || chem.Adrenaline >= 0.75) ? Tone.Fault : Tone.Lit);
        return new Report(tone, rows, events);
    }

    // ---------- 内驱系统：InnerDriveService ----------

    private async Task<Report> LoadDriveAsync(string id, string charId)
    {
        DriveReadout? drive = null;
        try
        {
            var svc = Resolve<InnerDriveService>();
            if (svc is not null) drive = await svc.ReadAsync(charId);
        }
        catch (Exception ex) { App.WriteLog("ModuleInspector.Drive -> " + ex.Message); }

        var rows = new List<InspectorRow>();
        var events = new List<InspectorEntry>();

        switch (id)
        {
            case "core.driveSystem":
                rows.Add(new InspectorRow("内驱系统", drive is null ? "未开启（或这个角色还没有驱动记录）" : "运行中 · 可整体停用"));
                rows.Add(new InspectorRow("最强一股", drive is null ? NoReading : $"{DriveRules.NameOf(DominantOf(drive))} {F(DominantValueOf(drive))}"));
                rows.Add(new InspectorRow("火气", Num(drive?.Anger)));
                rows.Add(new InspectorRow("更新于", Clock(drive?.UpdatedAt)));
                break;

            case "core.drivePower":
                rows.Add(new InspectorRow("攻击性（推着去争）", Num(drive?.Aggression)));
                rows.Add(new InspectorRow("性（连接与亲密）", Num(drive?.Sexuality)));
                rows.Add(new InspectorRow("自恋（被看见）", Num(drive?.Narcissism)));
                rows.Add(new InspectorRow("三者的角色", "力的来源：决定她多容易被推动"));
                break;

            case "core.driveSeven":
                rows.Add(new InspectorRow("贪欲", drive is null ? NoReading : $"{F(drive.Greed)} · 名利 / 金钱 / 掌控感"));
                rows.Add(new InspectorRow("暴食", Num(drive?.Gluttony)));
                rows.Add(new InspectorRow("色欲", Num(drive?.Lust)));
                rows.Add(new InspectorRow("嫉妒", Num(drive?.Envy)));
                rows.Add(new InspectorRow("傲慢", Num(drive?.Pride)));
                rows.Add(new InspectorRow("怠惰", Num(drive?.Sloth)));
                rows.Add(new InspectorRow("暴怒", drive is null ? NoReading : $"{F(drive.Wrath)} · 与火气同源"));
                rows.Add(new InspectorRow("越过 0.42", "会化成一句「想做点什么」进提示词"));
                break;

            case "core.driveGate":
                rows.Add(new InspectorRow("自律", drive is null ? NoReading : $"{F(drive.Discipline)} · 闸门的力道，可被锻炼"));
                rows.Add(Row("道德观", drive?.MoralName));
                rows.Add(Row("人生观", drive?.LifeName));
                rows.Add(Row("思维主义", drive?.ThinkingName));
                rows.Add(new InspectorRow("关系安全度", drive is null ? NoReading : $"{F(drive.RelationSafety)} · 低于 0.30 时火不往外放，只往内消"));
                break;

            case "core.discharge":
                rows.Add(new InspectorRow("火气", drive is null ? NoReading : $"{F(drive.Anger)} · 0.30 要找出口，0.88 过顶硬跳最强一档"));
                rows.Add(Row("当前出口", drive?.DischargeName));
                rows.Add(Row("具体表现", drive?.DischargeBehavior));
                rows.Add(new InspectorRow("内啡肽", Num(drive?.Endorphin)));
                rows.Add(new InspectorRow("八种出口", "叛逆 > 撒泼 > 怄气 > 任性 > 郁闷 > 拖延 > 厌烦 > 退缩"));
                break;
        }

        if (drive is not null)
        {
            events.Add(new InspectorEntry(drive.UpdatedAt, "内驱结算",
                $"最强 {DriveRules.NameOf(DominantOf(drive))} {F(DominantValueOf(drive))} · 火气 {F(drive.Anger)}"));
        }
        events.AddRange(await RecallTrailAsync(charId, 6));

        var tone = drive is null
            ? Tone.Dim
            : (drive.Anger >= DriveRules.OverflowAnger || drive.RelationSafety < 0.30 ? Tone.Fault : Tone.Lit);
        return new Report(tone, rows, events);
    }

    // ---------- 情绪与病理：InnerLifeService 化学池 + HumanMemoryService 情绪配比 ----------

    private async Task<Report> LoadEmotionAsync(string id, string charId)
    {
        InnerChemicals? chem = null;
        try
        {
            var svc = Resolve<InnerLifeService>();
            if (svc is not null)
            {
                await svc.SnapshotAsync(charId);
                chem = svc.PeekChemicals(charId);
            }
        }
        catch (Exception ex) { App.WriteLog("ModuleInspector.Emotion -> " + ex.Message); }

        List<HumanMemoryEntry> recent = new();
        if (string.Equals(id, "emo.coupling", StringComparison.Ordinal))
        {
            try
            {
                var mem = Resolve<HumanMemoryService>();
                if (mem is not null)
                    recent = (await mem.AllAsync(charId))
                        .Where(m => !m.IsForgotten && m.Emotion is { IsEmpty: false })
                        .OrderByDescending(m => m.LastRecalledAt)
                        .Take(3)
                        .ToList();
            }
            catch (Exception ex) { App.WriteLog("ModuleInspector.EmotionCoupling -> " + ex.Message); }
        }

        // 三维都从化学池派生：效价 = 多巴胺 − 皮质醇，唤醒 = 肾上腺素，控制感 = 血清素 − 皮质醇。
        // 这套映射与监测页角标用的是同一套，两处读出来才对得上。
        var valence = chem is null ? (double?)null : chem.Dopamine - chem.Cortisol;
        var arousal = chem?.Adrenaline;
        var dominance = chem is null ? (double?)null : chem.Serotonin - chem.Cortisol;

        var rows = new List<InspectorRow>();
        var events = new List<InspectorEntry>();

        switch (id)
        {
            case "emo.state":
                rows.Add(new InspectorRow("效价（-1..1）", Num(valence)));
                rows.Add(new InspectorRow("唤醒度（0..1）", Num(arousal)));
                rows.Add(new InspectorRow("控制感", Num(dominance)));
                rows.Add(new InspectorRow("数据来源", "InnerLifeService 化学池派生（与监测页角标同一套映射）"));
                rows.Add(new InspectorRow("更新于", Clock(chem?.UpdatedAt)));
                break;

            case "emo.valence":
                rows.Add(new InspectorRow("效价（多巴胺 − 皮质醇）", Num(valence)));
                rows.Add(new InspectorRow("多巴胺", Num(chem?.Dopamine)));
                rows.Add(new InspectorRow("皮质醇", Num(chem?.Cortisol)));
                rows.Add(new InspectorRow("更新于", Clock(chem?.UpdatedAt)));
                break;

            case "emo.arousal":
                rows.Add(new InspectorRow("唤醒度（肾上腺素）", Num(arousal)));
                rows.Add(new InspectorRow("肾上腺素", Num(chem?.Adrenaline)));
                rows.Add(new InspectorRow("疲劳", Num(chem?.Fatigue)));
                rows.Add(new InspectorRow("更新于", Clock(chem?.UpdatedAt)));
                break;

            case "emo.dominance":
                rows.Add(new InspectorRow("控制感（血清素 − 皮质醇）", Num(dominance)));
                rows.Add(new InspectorRow("血清素", Num(chem?.Serotonin)));
                rows.Add(new InspectorRow("皮质醇", Num(chem?.Cortisol)));
                rows.Add(new InspectorRow("更新于", Clock(chem?.UpdatedAt)));
                break;

            case "emo.coupling":
                rows.Add(new InspectorRow("多巴胺", Num(chem?.Dopamine)));
                rows.Add(new InspectorRow("血清素", Num(chem?.Serotonin)));
                rows.Add(new InspectorRow("带情绪配比的记忆", recent.Count == 0 ? NoReading : CountText.FormatWithUnit(recent.Count, "条最近回想")));
                foreach (var m in recent)
                    rows.Add(new InspectorRow($"回想 {Clock(m.CreatedAt)}", m.Emotion?.Text ?? NoReading));
                rows.Add(new InspectorRow("耦合方式", "回想时的情绪偏置 moodBias 会按配比改写记忆检索的权重"));
                break;
        }

        if (chem is not null)
        {
            events.Add(new InspectorEntry(chem.UpdatedAt, "化学池结算",
                $"效价 {F(chem.Dopamine - chem.Cortisol)} · 唤醒 {F(chem.Adrenaline)}"));
        }
        if (recent.Count > 0)
        {
            foreach (var m in recent)
                events.Add(new InspectorEntry(m.LastRecalledAt, "记忆回想", TextOf(m)));
        }
        else events.AddRange(await RecallTrailAsync(charId, 6));

        var tone = chem is null
            ? Tone.Dim
            : (valence is not null && valence <= -0.45 ? Tone.Fault : Tone.Lit);
        return new Report(tone, rows, events);
    }

    // ---------- 五层记忆：HumanMemoryService ----------

    private async Task<Report> LoadMemoryAsync(string id, string charId)
    {
        List<HumanMemoryEntry> core = new();
        List<HumanMemoryEntry> all = new();
        try
        {
            var svc = Resolve<HumanMemoryService>();
            if (svc is not null)
            {
                core = await svc.CoreAsync(charId);
                all = await svc.AllAsync(charId);
            }
        }
        catch (Exception ex) { App.WriteLog("ModuleInspector.Memory -> " + ex.Message); }

        double? moodBias = null;
        if (string.Equals(id, "mem.retrieval", StringComparison.Ordinal))
        {
            try
            {
                var life = Resolve<InnerLifeService>();
                if (life is not null) moodBias = await life.RecallBiasAsync(charId);
            }
            catch (Exception ex) { App.WriteLog("ModuleInspector.MemoryBias -> " + ex.Message); }
        }

        var live = all.Where(m => !m.IsForgotten).ToList();
        var episodic = live.Where(m => m.Kind == HumanMemoryKind.Event).ToList();
        var abstracted = live.Where(m => m.Kind == HumanMemoryKind.Abstraction).ToList();
        var traumatic = live.Where(m => m.IsTrauma).ToList();
        var latest = all.FirstOrDefault();
        var lastRecalled = live.OrderByDescending(m => m.LastRecalledAt).FirstOrDefault();

        var rows = new List<InspectorRow>();
        var events = new List<InspectorEntry>();

        switch (id)
        {
            case "mem.shallow":
                rows.Add(new InspectorRow("数据来源", "HumanMemoryService 记忆库"));
                rows.Add(new InspectorRow("在场记忆", all.Count == 0 ? NoReading : CountText.FormatWithUnit(live.Count, "条")));
                rows.Add(new InspectorRow("核心记忆", core.Count == 0 ? NoReading : CountText.FormatWithUnit(core.Count, "条")));
                rows.Add(new InspectorRow("最近写入", latest is null ? NoReading : $"{MemoryLabel(latest)} · {Clock(latest.CreatedAt)}"));
                rows.Add(new InspectorRow("最近回想", lastRecalled is null ? NoReading : $"{MemoryLabel(lastRecalled)} · {Clock(lastRecalled.LastRecalledAt)}"));
                break;

            case "mem.deepen":
                rows.Add(new InspectorRow("核心记忆", core.Count == 0 ? NoReading : CountText.FormatWithUnit(core.Count, "条")));
                rows.Add(new InspectorRow("平均重要性", all.Count == 0 ? NoReading : F(all.Average(m => m.Importance))));
                rows.Add(new InspectorRow("平均回想次数", all.Count == 0 ? NoReading : F(all.Average(m => (double)m.RecallCount))));
                rows.Add(new InspectorRow("深化依据", "重要性 × 回想次数 × 唤醒度 × 时间衰减共同决定下不下去"));
                break;

            case "mem.deep":
                rows.Add(new InspectorRow("深层记忆", all.Count == 0 ? NoReading : CountText.FormatWithUnit(live.Count, "条")));
                rows.Add(new InspectorRow("情景", all.Count == 0 ? NoReading : CountText.FormatWithUnit(episodic.Count, "条")));
                rows.Add(new InspectorRow("语义", all.Count == 0 ? NoReading : CountText.FormatWithUnit(abstracted.Count, "条")));
                rows.Add(new InspectorRow("核心 / 创伤", all.Count == 0 ? NoReading : $"{CountText.Format(core.Count)} / {CountText.FormatWithUnit(traumatic.Count, "条")}"));
                break;

            case "mem.episodic":
                rows.Add(new InspectorRow("情景记忆", all.Count == 0 ? NoReading : CountText.FormatWithUnit(episodic.Count, "条")));
                foreach (var m in episodic.OrderByDescending(m => m.CreatedAt).Take(4))
                    rows.Add(new InspectorRow(Clock(m.CreatedAt), TextOf(m)));
                break;

            case "mem.semanticM":
                rows.Add(new InspectorRow("语义记忆", all.Count == 0 ? NoReading : CountText.FormatWithUnit(abstracted.Count, "条")));
                foreach (var m in abstracted.OrderByDescending(m => m.CreatedAt).Take(4))
                    rows.Add(new InspectorRow(Clock(m.CreatedAt), TextOf(m)));
                break;

            case "mem.procedural":
                rows.Add(new InspectorRow("程序记忆", NoReading));
                rows.Add(new InspectorRow("可用的抽象印象", all.Count == 0 ? NoReading : CountText.FormatWithUnit(abstracted.Count, "条")));
                rows.Add(new InspectorRow("机制", "程序记忆是习惯化的应对套路，由深层记忆塑造；它现在没有独立的数值化服务"));
                break;

            case "mem.retrieval":
                rows.Add(new InspectorRow("可回想的核心记忆", core.Count == 0 ? NoReading : CountText.FormatWithUnit(core.Count, "条")));
                rows.Add(new InspectorRow("情绪偏置 moodBias", Num(moodBias)));
                rows.Add(new InspectorRow("最近回想", lastRecalled is null ? NoReading : $"{MemoryLabel(lastRecalled)} · {Clock(lastRecalled.LastRecalledAt)}"));
                rows.Add(new InspectorRow("检索方式", "按窗口 / 线索打分：重要性 × 时间衰减 × 复述次数 × 随机扰动，再乘情绪偏置"));
                break;

            case "mem.rumination":
                rows.Add(new InspectorRow("创伤记忆", all.Count == 0 ? NoReading : CountText.FormatWithUnit(traumatic.Count, "条")));
                rows.Add(new InspectorRow("反刍链读数", NoReading));
                rows.Add(new InspectorRow("最近回想", lastRecalled is null ? NoReading : $"{MemoryLabel(lastRecalled)} · {Clock(lastRecalled.LastRecalledAt)}"));
                rows.Add(new InspectorRow("机制", "负面回忆顺着关联词串开，越串越重；这条链只有「被走了几次」，没有独立数值"));
                break;
        }

        foreach (var m in live.OrderByDescending(m => m.LastRecalledAt).Take(6))
            events.Add(new InspectorEntry(m.LastRecalledAt, "回想", TextOf(m)));
        foreach (var m in all.OrderByDescending(m => m.CreatedAt).Take(6))
            events.Add(new InspectorEntry(m.CreatedAt, m.IsCore ? "写入核心记忆" : "写入记忆", TextOf(m)));

        var tone = all.Count == 0
            ? Tone.Dim
            : (string.Equals(id, "mem.rumination", StringComparison.Ordinal) && traumatic.Count > 0 ? Tone.Fault
                : (FlowTracer.IsLit(id) ? Tone.Lit : Tone.Dim));
        return new Report(tone, rows, events);
    }

    // ---------- 核心心智与病理：MentalHealthService + HumanMemoryService 的创伤记忆 ----------

    private async Task<Report> LoadMindAsync(string id, string charId)
    {
        MentalHealthService? health = null;
        IReadOnlyList<MentalConditionState> states = Array.Empty<MentalConditionState>();
        try
        {
            health = Resolve<MentalHealthService>();
            if (health is not null) states = await health.ListAsync(charId);
        }
        catch (Exception ex) { App.WriteLog("ModuleInspector.Mental -> " + ex.Message); }

        List<HumanMemoryEntry> core = new();
        try
        {
            var mem = Resolve<HumanMemoryService>();
            if (mem is not null) core = await mem.CoreAsync(charId);
        }
        catch (Exception ex) { App.WriteLog("ModuleInspector.MindMemory -> " + ex.Message); }

        var trauma = core.Where(m => m.IsTrauma).ToList();
        var rows = new List<InspectorRow>();
        var events = new List<InspectorEntry>();
        var tone = Tone.Dim;

        switch (id)
        {
            case "path.state":
                rows.Add(new InspectorRow("携带状况", states.Count == 0 ? "未携带任何目录条目" : CountText.FormatWithUnit(states.Count, "条")));
                foreach (var st in states.Take(6))
                    rows.Add(new InspectorRow(ConditionName(health, st.Id),
                        $"{MentalHealthService.SeverityName(st.Severity)} · 稳定度 {F(st.Stability)}"));
                rows.Add(new InspectorRow("更新于", Clock(states.Count == 0 ? null : states.Max(s => s.Since))));
                tone = states.Any(IsUnstable) ? Tone.Fault : (states.Count > 0 ? Tone.Lit : Tone.Dim);
                if (states.Count > 0)
                    events.AddRange(states.Select(s => new InspectorEntry(s.Since, "开始携带", ConditionName(health, s.Id))));
                break;

            case "path.ptsd":
            case "path.depression":
            case "path.anxiety":
            case "path.bpd":
                {
                    var key = id switch
                    {
                        "path.ptsd" => "ptsd",
                        "path.depression" => "depress",
                        "path.anxiety" => "anxi",
                        _ => "bpd"
                    };
                    var hit = states.Where(s => s.Id.Contains(key, StringComparison.OrdinalIgnoreCase)).ToList();
                    var cond = hit.Select(s => health?.Find(s.Id)).FirstOrDefault(c => c is not null);
                    rows.Add(new InspectorRow("是否携带", hit.Count == 0 ? "未携带（这条状态机目前没有条目）" : $"携带 {hit.Count} 条"));
                    rows.Add(new InspectorRow("严重度", hit.Count == 0 ? NoReading : string.Join("、", hit.Select(s => MentalHealthService.SeverityName(s.Severity)))));
                    rows.Add(new InspectorRow("稳定度", hit.Count == 0 ? NoReading : string.Join("、", hit.Select(s => F(s.Stability)))));
                    rows.Add(new InspectorRow("起始于", hit.Count == 0 ? NoReading : Clock(hit.Min(s => s.Since))));
                    rows.Add(Row("目录症状", cond?.Symptoms));
                    rows.Add(Row("行为倾向", cond?.Behavior));
                    rows.Add(Row("备注", hit.Select(s => s.Note).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n))));
                    if (string.Equals(id, "path.ptsd", StringComparison.Ordinal))
                        rows.Add(new InspectorRow("创伤记忆", core.Count == 0 ? NoReading : CountText.FormatWithUnit(trauma.Count, "条")));

                    tone = hit.Any(IsUnstable) ? Tone.Fault
                        : (hit.Count > 0 || (id == "path.ptsd" && trauma.Count > 0) ? Tone.Lit : Tone.Dim);
                    events.AddRange(hit.Select(s => new InspectorEntry(s.Since, "开始携带", ConditionName(health, s.Id))));
                    break;
                }

            case "mind.trauma":
                {
                    var traumaStates = states.Where(s =>
                    {
                        var c = health?.Find(s.Id);
                        return c is not null && (string.Equals(c.Category, "创伤", StringComparison.Ordinal)
                            || s.Id.Contains("ptsd", StringComparison.OrdinalIgnoreCase)
                            || s.Id.Contains("trauma", StringComparison.OrdinalIgnoreCase));
                    }).ToList();
                    rows.Add(new InspectorRow("创伤类状况", traumaStates.Count == 0 ? "无" : CountText.FormatWithUnit(traumaStates.Count, "条")));
                    foreach (var st in traumaStates.Take(4))
                        rows.Add(new InspectorRow(ConditionName(health, st.Id),
                            $"{MentalHealthService.SeverityName(st.Severity)} · 稳定度 {F(st.Stability)}"));
                    rows.Add(new InspectorRow("核心创伤记忆", core.Count == 0 ? NoReading : CountText.FormatWithUnit(trauma.Count, "条")));
                    rows.Add(new InspectorRow("最深的阶段", trauma.Count == 0 ? NoReading
                        : HumanMemoryMath.TraumaStageName(trauma.Min(m => m.TraumaStage))));
                    foreach (var m in trauma.OrderByDescending(m => m.EmotionalIntensity).Take(3))
                        rows.Add(new InspectorRow("创伤记忆", $"{m.Text}（强度 {F(m.EmotionalIntensity)}）"));

                    tone = trauma.Any(m => m.TraumaStage is >= 1 and <= 2) || traumaStates.Any(IsUnstable)
                        ? Tone.Fault
                        : (trauma.Count > 0 || traumaStates.Count > 0 ? Tone.Lit : Tone.Dim);
                    events.AddRange(trauma.Select(m => new InspectorEntry(m.CreatedAt, "创伤记忆",
                        $"{m.Text} · {HumanMemoryMath.TraumaStageName(m.TraumaStage)}")));
                    events.AddRange(traumaStates.Select(s => new InspectorEntry(s.Since, "开始携带", ConditionName(health, s.Id))));
                    break;
                }

            case "mind.intrusive":
                {
                    var intrusive = states
                        .Where(s => s.Id.Contains("intrusive", StringComparison.OrdinalIgnoreCase))
                        .ToList();
                    rows.Add(new InspectorRow("侵入性思维", intrusive.Count == 0 ? "当前无侵入性思维类状况" : CountText.FormatWithUnit(intrusive.Count, "条")));
                    foreach (var st in intrusive)
                    {
                        var c = health?.Find(st.Id);
                        rows.Add(new InspectorRow(c?.Name ?? st.Id,
                            $"{MentalHealthService.SeverityName(st.Severity)} · 稳定度 {F(st.Stability)}"
                            + (string.IsNullOrWhiteSpace(st.Note) ? "" : $" · 备注：{st.Note}")));
                    }
                    tone = intrusive.Any(IsUnstable) ? Tone.Fault : (intrusive.Count > 0 ? Tone.Lit : Tone.Dim);
                    events.AddRange(intrusive.Select(s => new InspectorEntry(s.Since, "开始携带", ConditionName(health, s.Id))));
                    break;
                }

            case "mind.persona":
                rows.Add(new InspectorRow("人格读数", NoReading));
                rows.Add(new InspectorRow("人格锚点（核心记忆）", core.Count == 0 ? NoReading : CountText.FormatWithUnit(core.Count, "条")));
                rows.Add(new InspectorRow("携带状况", states.Count == 0 ? "无" : CountText.FormatWithUnit(states.Count, "条")));
                rows.Add(new InspectorRow("说明", "核心记忆与情绪基调共同设这个人格；它自己没有单独的数值"));
                tone = states.Count > 0 || core.Count > 0 ? Tone.Lit : Tone.Dim;
                break;

            case "mind.pattern":
                {
                    var abstracted = core.Count(m => m.Kind == HumanMemoryKind.Abstraction);
                    rows.Add(new InspectorRow("行为模式读数", NoReading));
                    rows.Add(new InspectorRow("抽象印象", core.Count == 0 ? NoReading : CountText.FormatWithUnit(abstracted, "条")));
                    rows.Add(new InspectorRow("机制", "程序记忆：由深层记忆塑造的习惯化应对套路（对应图上 mem.procedural）"));
                    rows.Add(new InspectorRow("建模状态", "没有专门的数值化服务，这一页不显示推测数值"));
                    tone = core.Count > 0 ? Tone.Lit : Tone.Dim;
                    break;
                }

            case "mind.summary":
                rows.Add(new InspectorRow("总结评估读数", NoReading));
                rows.Add(new InspectorRow("携带状况", states.Count == 0 ? "无" : CountText.FormatWithUnit(states.Count, "条")));
                rows.Add(new InspectorRow("说明", "总结评估是对当前情境的认知评价，每次回复都会重算，不落盘成单独数值"));
                tone = FlowTracer.IsLit(id) ? Tone.Lit : Tone.Dim;
                break;
        }

        events.AddRange(await RecallTrailAsync(charId, 4));
        return new Report(tone, rows, events);
    }

    // ---------- 低级中枢与躯体化：OrganSimulationService ----------

    private async Task<Report> LoadSomaAsync(string id, string charId)
    {
        IReadOnlyList<OrganState> organs = Array.Empty<OrganState>();
        IReadOnlyList<OrganInfluence> influences = Array.Empty<OrganInfluence>();
        try { organs = OrganSimulationService.Instance.Observe(charId); }
        catch (Exception ex) { App.WriteLog("ModuleInspector.Soma -> " + ex.Message); }
        try { influences = OrganSimulationService.Instance.Influences(); }
        catch (Exception ex) { App.WriteLog("ModuleInspector.SomaRules -> " + ex.Message); }

        InnerChemicals? chem = null;
        if (id is "soma.coping" or "soma.bypass")
        {
            try
            {
                var life = Resolve<InnerLifeService>();
                if (life is not null) chem = life.PeekChemicals(charId);
            }
            catch (Exception ex) { App.WriteLog("ModuleInspector.SomaChem -> " + ex.Message); }
        }

        var lit = organs.Where(o => o.IsLit).ToList();
        var hottest = organs.OrderByDescending(o => o.Value).FirstOrDefault();
        var symptoms = organs
            .SelectMany(o => o.Symptoms)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.Ordinal)
            .Take(4)
            .ToList();

        var rows = new List<InspectorRow>
        {
            new("器官读数覆盖", organs.Count == 0 ? NoReading : CountText.FormatWithUnit(organs.Count, "个器官")),
            new("点亮的器官", lit.Count == 0 ? "全部平静" : string.Join("、", lit.Select(o => o.Name))),
            new("读数最高的一处", hottest is null ? NoReading : $"{hottest.Name} {F(hottest.Value)} · {hottest.StatusText}"),
            new("当前症状", symptoms.Count == 0 ? "无明显症状" : string.Join("、", symptoms)),
            new("连锁规则", influences.Count == 0 ? NoReading : CountText.FormatWithUnit(influences.Count, "条")),
            new("更新于", Clock(organs.Count == 0 ? null : organs.Max(o => o.UpdatedAt)))
        };

        // 每一条分支自己有没有读数：有就读，没有就明说没有，不拿器官整体读数冒充分支读数。
        switch (id)
        {
            case "soma.coping":
                rows.Insert(0, new InspectorRow("应对分支读数", NoReading));
                rows.Insert(1, new InspectorRow("当前皮质醇", Num(chem?.Cortisol)));
                rows.Insert(2, new InspectorRow("分支判定", "战 / 逃 / 僵由唤醒度与控制感共同决定，没有单独的落盘数值"));
                break;
            case "soma.fight":
            case "soma.freeze":
            case "soma.reply":
                rows.Insert(0, new InspectorRow("分支读数", NoReading));
                rows.Insert(1, new InspectorRow("说明", "这一分支只在当轮执行里走过一次，图上亮灯即代表走过，没有独立数值"));
                break;
            case "soma.bypass":
                rows.Insert(0, new InspectorRow("当前皮质醇", Num(chem?.Cortisol)));
                rows.Insert(1, new InspectorRow("越线判据", "皮质醇高过 0.68 时低级中枢越权，理智回复那条分支被顶掉"));
                break;
            default:
                rows.Insert(0, new InspectorRow("本节点独立读数", NoReading));
                break;
        }

        var events = organs
            .OrderByDescending(o => o.UpdatedAt)
            .Take(8)
            .Select(o => new InspectorEntry(o.UpdatedAt, o.Name,
                $"{o.StatusText} · 值 {F(o.Value)}"
                + (o.Symptoms.Count == 0 ? "" : " · " + string.Join("、", o.Symptoms))))
            .ToList();

        var tone = organs.Count == 0
            ? Tone.Dim
            : (organs.Any(o => o.Status == OrganStatus.Alert) ? Tone.Fault
                : (lit.Count > 0 || FlowTracer.IsLit(id) ? Tone.Lit : Tone.Dim));
        return new Report(tone, rows, events);
    }

    // ---------- 认知与注意：CognitionModeService ----------

    private async Task<Report> LoadCognitionAsync(string id, string charId)
    {
        var ok = false;
        var mode = CognitionMode.Wandering;
        double reserve = 0, capacity = 0;
        var intervene = false;
        try
        {
            var svc = Resolve<CognitionModeService>() ?? CognitionModeService.Instance;
            mode = svc.Current(charId);
            reserve = svc.FocusReserve(charId);
            capacity = svc.Capacity(charId);
            intervene = svc.ShouldWanderIntervene(charId);
            ok = true;
        }
        catch (Exception ex) { App.WriteLog("ModuleInspector.Cognition -> " + ex.Message); }

        var rows = new List<InspectorRow>();
        if (id == "cognition.intervene")
        {
            rows.Add(new InspectorRow("神游是否该介入", ok ? (intervene ? "该介入了（专注储备快见底）" : "还不到介入的时候") : NoReading));
            rows.Add(new InspectorRow("当前档位", ok ? ModeName(mode) : NoReading));
        }
        else
        {
            rows.Add(new InspectorRow("当前档位", ok ? ModeName(mode) : NoReading));
            rows.Add(new InspectorRow("专注储备", ok ? $"{F(reserve)} / {F(capacity)}" : NoReading));
            rows.Add(new InspectorRow("介入判据", ok ? (intervene ? "储备低于 25% 仍在撑：神游会往脑海塞旧记忆" : "储备还够，神游不介入") : NoReading));
        }
        rows.Add(new InspectorRow("专注燃料", "多巴胺越高，进入专注越快"));

        var events = await RecallTrailAsync(charId, 6);
        var tone = !ok ? Tone.Dim : (intervene ? Tone.Fault : Tone.Lit);
        return new Report(tone, rows, events);
    }

    // ---------- 感知输入 / 工程落地：只有执行痕迹可读 ----------

    private async Task<Report> LoadFlowAsync(string id, string charId)
    {
        var lit = false;
        var glow = 0f;
        try
        {
            lit = FlowTracer.IsLit(id);
            glow = FlowTracer.Glow(id);
        }
        catch (Exception ex) { App.WriteLog("ModuleInspector.Flow -> " + ex.Message); }

        int? total = null;
        HumanMemoryEntry? latest = null;
        try
        {
            var svc = Resolve<HumanMemoryService>();
            if (svc is not null)
            {
                var all = await svc.AllAsync(charId);
                total = all.Count;
                latest = all.FirstOrDefault();
            }
        }
        catch (Exception ex) { App.WriteLog("ModuleInspector.FlowMemory -> " + ex.Message); }

        var rows = new List<InspectorRow>
        {
            new("执行状态", lit ? "✓ 正在流经" : "○ 静默"),
            new("余辉", lit ? F(glow) : "0.00"),
            new("本节点读数", NoReading),
            new("写入的记忆", total is null ? NoReading : CountText.FormatWithUnit(total.Value, "条")),
            new("最近写入", latest is null ? NoReading : $"{Clock(latest.CreatedAt)} · {TextOf(latest)}")
        };

        var events = new List<InspectorEntry>();
        if (lit) events.Add(new InspectorEntry(DateTime.UtcNow, "流经", "刚刚有一轮对话走过这个节点"));
        events.AddRange(await RecallTrailAsync(charId, 8));
        return new Report(lit ? Tone.Lit : Tone.Dim, rows, events);
    }

    private Task<Report> LoadUnknownAsync(string id, string charId) =>
        LoadFlowAsync(id, charId);

    // ==================== 取数小工具 ====================

    /// <summary>最近回想：给没有独立时间戳的模块当「最近变化」用，只取真的被回想过的条目。</summary>
    private async Task<List<InspectorEntry>> RecallTrailAsync(string charId, int take)
    {
        var result = new List<InspectorEntry>();
        try
        {
            var svc = Resolve<HumanMemoryService>();
            if (svc is null) return result;
            var all = await svc.AllAsync(charId);
            foreach (var m in all.Where(m => !m.IsForgotten)
                         .OrderByDescending(m => m.LastRecalledAt)
                         .Take(take))
                result.Add(new InspectorEntry(m.LastRecalledAt, "记忆回想", TextOf(m)));
        }
        catch (Exception ex) { App.WriteLog("ModuleInspector.RecallTrail -> " + ex.Message); }
        return result;
    }

    private static DriveImpulse DominantOf(DriveReadout d) => PairsOf(d).OrderByDescending(p => p.Value).First().Kind;

    private static double DominantValueOf(DriveReadout d) => PairsOf(d).OrderByDescending(p => p.Value).First().Value;

    private static IEnumerable<(DriveImpulse Kind, double Value)> PairsOf(DriveReadout d) => new[]
    {
        (DriveImpulse.Greed, d.Greed), (DriveImpulse.Gluttony, d.Gluttony), (DriveImpulse.Lust, d.Lust),
        (DriveImpulse.Envy, d.Envy), (DriveImpulse.Pride, d.Pride), (DriveImpulse.Sloth, d.Sloth),
        (DriveImpulse.Wrath, d.Anger)
    };

    /// <summary>同一条状况：严重度两档以上，或稳定度已经低到容易被点燃，就是「异常」而不是「亮」。</summary>
    private static bool IsUnstable(MentalConditionState s) => s.Severity >= 2 || s.Stability < 0.35;

    private string ConditionName(MentalHealthService? health, string id)
        => health?.Find(id)?.Name ?? id;

    private static string ModeName(CognitionMode mode)
        => mode == CognitionMode.Focused ? "专注（高精度、高耗）" : "神游（低精度默认态）";

    private static string MemoryLabel(HumanMemoryEntry m) => m.Kind switch
    {
        HumanMemoryKind.Abstraction => "抽象印象",
        HumanMemoryKind.Important => "重要时刻",
        HumanMemoryKind.Image => "图片记忆",
        _ => "情景记忆"
    };

    private static string TextOf(HumanMemoryEntry m)
    {
        var text = string.IsNullOrWhiteSpace(m.Text) ? "（无正文）" : m.Text;
        return m.Emotion is { IsEmpty: false } mix ? $"{text} · {mix.Text}" : text;
    }

    private static InspectorRow Row(string label, string? value)
        => new(label, string.IsNullOrWhiteSpace(value) ? NoReading : value);

    /// <summary>数值统一两位小数；拿不到就是 null，交给 <see cref="Row"/> 写成「暂无独立读数」。</summary>
    private static string Num(double? value) => value is null ? NoReading : F(value.Value);

    private static string F(double value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    private static string Clock(DateTime? at)
        => at is null ? NoReading : at.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    /// <summary>今天只说时分，往前的带上日期：时间线上「今天 14:02」比「11-22 14:02」好读。</summary>
    private static string TimeText(DateTime at)
    {
        var local = at.ToLocalTime();
        return local.Date == DateTime.Now.Date
            ? "今天 " + local.ToString("HH:mm", CultureInfo.InvariantCulture)
            : local.ToString("MM-dd HH:mm", CultureInfo.InvariantCulture);
    }

    /// <summary>从 MAUI 服务容器取单例服务（与监测页同一取法）；取不到就返回 null，让那一路空着。</summary>
    private static T? Resolve<T>() where T : class
    {
        try
        {
            var services = Microsoft.Maui.IPlatformApplication.Current?.Services
                ?? Application.Current?.Handler?.MauiContext?.Services;
            return services?.GetService(typeof(T)) as T;
        }
        catch (Exception ex)
        {
            App.WriteLog("ModuleInspector.Resolve -> " + ex.Message);
            return null;
        }
    }

    private static Color ThemeColor(string key, string fallback)
    {
        try
        {
            if (Application.Current?.Resources.TryGetValue(key, out var v) == true && v is Color c) return c;
        }
        catch { /* 主题资源取不到就用兜底色 */ }
        return Color.FromArgb(fallback);
    }

    // ==================== 状态灯 ====================

    private enum Tone { Lit, Dim, Fault }

    private void SetLamp(Tone tone)
    {
        switch (tone)
        {
            case Tone.Lit:
                LampText = "亮 · 正在参与";
                LampColor = ThemeColor("GreenSoft", "#A5D6A7");
                break;
            case Tone.Fault:
                LampText = "异常 · 需要留意";
                LampColor = ThemeColor("RedSoft", "#EF9A9A");
                break;
            default:
                LampText = "暗 · 静默";
                LampColor = ThemeColor("WarmFaint", "#C4B89A");
                break;
        }
    }

    // ==================== 模块目录：id → 名字 / 系统 / 一句话说明 ====================

    private enum Family { Input, Memory, Emotion, Cognition, Mind, Soma, Drive, Hormone, Flow, Unknown }

    private sealed record ModuleInfo(string Id, string Name, string System, string Layer, string Summary, Family Family);

    private sealed record InspectorEntry(DateTime At, string Title, string Text);

    private sealed record Report(Tone Tone, List<InspectorRow> Rows, List<InspectorEntry> Events);

    private static ModuleInfo Describe(string id)
    {
        var (name, layer, summary) = Catalog.TryGetValue(id, out var meta)
            ? meta
            : (id, "", "这个节点不在监测图的骨架里（多半是日程 / 器官 / 记忆条目），只能读到它执行时的痕迹。");
        return new ModuleInfo(id, name, SystemOf(id), LayerOf(layer), summary, FamilyOf(id));
    }

    private static Family FamilyOf(string id) => id switch
    {
        var s when s.StartsWith("in.", StringComparison.Ordinal) => Family.Input,
        var s when s.StartsWith("mem.", StringComparison.Ordinal) => Family.Memory,
        var s when s.StartsWith("emo.", StringComparison.Ordinal) => Family.Emotion,
        var s when s.StartsWith("cognition.", StringComparison.Ordinal) => Family.Cognition,
        var s when s.StartsWith("mind.", StringComparison.Ordinal) => Family.Mind,
        var s when s.StartsWith("path.", StringComparison.Ordinal) => Family.Mind,
        var s when s.StartsWith("soma.", StringComparison.Ordinal) => Family.Soma,
        var s when s.StartsWith("core.", StringComparison.Ordinal) => Family.Drive,
        var s when s.StartsWith("horm.", StringComparison.Ordinal) => Family.Hormone,
        var s when s.StartsWith("eng.", StringComparison.Ordinal) => Family.Flow,
        _ => Family.Unknown
    };

    private static string SystemOf(string id) => id switch
    {
        var s when s.StartsWith("in.", StringComparison.Ordinal) => "感知输入 · 特征解析",
        var s when s.StartsWith("mem.", StringComparison.Ordinal) => "五层记忆",
        var s when s.StartsWith("emo.", StringComparison.Ordinal) => "情绪与病理",
        var s when s.StartsWith("path.", StringComparison.Ordinal) => "情绪与病理",
        var s when s.StartsWith("cognition.", StringComparison.Ordinal) => "认知与注意",
        var s when s.StartsWith("mind.", StringComparison.Ordinal) => "自我与人格",
        var s when s.StartsWith("core.", StringComparison.Ordinal) => "内驱系统",
        var s when s.StartsWith("soma.", StringComparison.Ordinal) => "低级中枢 · 躯体化",
        var s when s.StartsWith("eng.", StringComparison.Ordinal) => "装配与路由 · 生成与输出",
        var s when s.StartsWith("horm.", StringComparison.Ordinal) => "激素与神经调制",
        _ => "未登记模块"
    };

    private static string LayerOf(string layer) => layer switch
    {
        "1" => "第 1 层 · 感知与输入抽象层",
        "2" => "第 2 层 · 记忆与情绪双向耦合",
        "3" => "第 3 层 · 核心心智与病理",
        "4" => "第 4 层 · 低级中枢熔断与躯体化",
        "5" => "第 5 层 · .NET 工程落地",
        "H" => "介质层 · 激素与神经调制",
        _ => ""
    };

    /// <summary>与监测页骨架同一套 id、名字与说明：点进来的节点，标题必须和图上写的一致。</summary>
    private static readonly Dictionary<string, (string Name, string Layer, string Summary)> Catalog =
        new(StringComparer.Ordinal)
        {
            ["in.text"] = ("文字输入", "1", "Send 入口记录用户文本，整条执行链的起点"),
            ["in.image"] = ("图像输入", "1", "附件图片入口，交给视觉特征提取"),
            ["in.voice"] = ("语音输入", "1", "语音转写文本入口，交给声学分析"),
            ["in.fusion"] = ("多模态融合", "1", "文字 / 图像 / 语音特征融合后进入具身锚定"),
            ["in.semantic"] = ("语义解析", "1", "文本 → 语义特征"),
            ["in.visual"] = ("视觉特征", "1", "图像附件的视觉特征提取"),
            ["in.audio"] = ("声学分析", "1", "语音附件的声学特征分析"),
            ["in.imagery"] = ("意象唤起", "1", "语义特征触发记忆意象"),
            ["in.intent"] = ("意图识别", "1", "识别意图，参与后续路由预判"),
            ["in.edge"] = ("边缘检测", "1", "图形线条 / 边缘检测"),
            ["in.entity"] = ("实体识别", "1", "识别实体并关联记忆条目"),
            ["in.tone"] = ("语调情绪", "1", "语调 / 情绪分析，写入情绪基调"),
            ["in.embodied"] = ("具身锚定", "1", "具身认知 / 情感锚定：多模态汇聚到心智入口"),

            ["mem.shallow"] = ("浅记忆", "2", "浅记忆 / 前记忆：近期对话缓冲"),
            ["mem.deepen"] = ("记忆深化", "2", "重要性 / 回想次数 / 唤醒度 / 时间衰减共同深化"),
            ["mem.deep"] = ("深层记忆", "2", "深层记忆：情景 / 语义 / 程序三类"),
            ["mem.episodic"] = ("情景记忆", "2", "带时间地点的经历"),
            ["mem.semanticM"] = ("语义记忆", "2", "概念与事实"),
            ["mem.procedural"] = ("程序记忆", "2", "习惯化的应对套路"),
            ["mem.retrieval"] = ("记忆检索器", "2", "按窗口 / 线索回想，受情绪偏置 moodBias 影响"),
            ["mem.rumination"] = ("反刍循环", "2", "负面回忆链延展，加剧情绪内耗"),
            ["emo.state"] = ("情绪状态", "2", "当前情绪：效价 / 唤醒度 / 控制感三维"),
            ["emo.valence"] = ("情绪效价", "2", "Valence（-1..1）"),
            ["emo.arousal"] = ("唤醒度", "2", "Arousal（0..1）"),
            ["emo.dominance"] = ("控制感", "2", "Dominance"),
            ["emo.coupling"] = ("记忆情绪耦合", "2", "记忆 ↔ 情绪双向耦合，moodBias 生效时点亮"),
            ["cognition.focus"] = ("专注模式", "2", "多巴胺驱动、大模型高精度；耗专属精力，用得越多停留越久"),
            ["cognition.wander"] = ("神游模式", "2", "低精度默认态；反刍 / 整理 / 纠错 / 新想法 / 回忆连线"),
            ["cognition.intervene"] = ("神游介入", "2", "专注快撑不住时，神游往脑海随机塞旧记忆"),

            ["mind.trauma"] = ("创伤节点", "3", "核心信念与旧伤的锚点"),
            ["mind.persona"] = ("人格系统", "3", "人格 / 核心信念：设定情绪基调"),
            ["mind.pattern"] = ("行为模式", "3", "由深层记忆塑造的行为模式"),
            ["mind.intrusive"] = ("侵入性思维", "3", "不受控的念头"),
            ["mind.summary"] = ("总结评估", "3", "对当前情境的认知评价"),
            ["path.state"] = ("病理状态机", "3", "PTSD / 抑郁 / 焦虑 / BPD 状态机"),
            ["path.ptsd"] = ("PTSD", "3", "降低创伤阈值，杏仁核劫持"),
            ["path.depression"] = ("抑郁", "3", "反刍死锁，情绪基线极低"),
            ["path.anxiety"] = ("焦虑", "3", "联想过度发散"),
            ["path.bpd"] = ("BPD", "3", "防御机制极化"),
            ["core.driveSystem"] = ("内驱动系统", "3", "可整体停用：关掉后数值冻结、不进提示词，也不再产驱动力"),
            ["core.drivePower"] = ("三种基本动力", "3", "攻击性 / 性 / 自恋：力的来源"),
            ["core.driveSeven"] = ("七个驱动", "3", "贪欲 / 暴食 / 色欲 / 嫉妒 / 傲慢 / 怠惰 / 暴怒"),
            ["core.driveGate"] = ("自律闸门", "3", "道德观 × 人生观 × 思维主义决定的压制力道"),
            ["core.discharge"] = ("八级发泄", "3", "叛逆 → 撒泼 → 怄气 → 任性 → 郁闷 → 拖延 → 厌烦 → 退缩"),

            ["soma.coping"] = ("应对机制", "4", "应对决策：战 / 逃 / 僵三分支"),
            ["soma.fight"] = ("反击防御", "4", "高唤醒下的对峙分支"),
            ["soma.reply"] = ("理智回复", "4", "常态输出分支"),
            ["soma.freeze"] = ("僵直宕机", "4", "低控制感时的冻结分支"),
            ["soma.bypass"] = ("低级中枢越权", "4", "低级中枢越权 / 熔断，绕过理智"),
            ["soma.response"] = ("器官响应", "4", "器官响应 / 躯体化：脏器状态外显"),
            ["soma.delay"] = ("输出延迟", "4", "输出延迟 / 反应迟钝"),
            ["soma.stutter"] = ("字符碎裂", "4", "字符碎裂 / 结巴 / 颤抖"),
            ["soma.downgrade"] = ("逻辑降级", "4", "逻辑降级 / 算力熔断"),
            ["soma.panic"] = ("惊恐发作", "4", "惊恐发作 / 呼吸急促模拟"),
            ["soma.recovery"] = ("器官恢复期", "4", "恢复期：情绪反复 / 迟滞"),

            ["eng.statepack"] = ("状态打包", "5", "把心智 / 器官状态打成模型输入"),
            ["eng.router"] = ("意图路由", "5", "意图路由分发：本地轻量 / 云端重量"),
            ["eng.redis"] = ("Redis 缓存", "5", "Redis 状态缓存"),
            ["eng.qdrant"] = ("Qdrant 向量", "5", "Qdrant 向量记忆库"),
            ["eng.sqlite"] = ("SQLite 结构化", "5", "结构化数据（日程 / 存档）"),
            ["eng.model.light"] = ("轻量模型", "5", "轻量本地模型"),
            ["eng.model.heavy"] = ("云端模型", "5", "云端大模型"),
            ["eng.prompt"] = ("Prompt 组装", "5", "System Prompt 动态组装"),
            ["eng.postprocess"] = ("后处理", "5", "文本修饰器 / 流式拦截中间件"),
            ["eng.output"] = ("流式输出", "5", "SSE 流式输出给用户"),
            ["eng.loop"] = ("循环反馈", "5", "输出回灌为下一轮输入"),

            ["horm.pool"] = ("内分泌池", "H", "六项化学值按半衰期向气质基线回落"),
            ["horm.dopa"] = ("多巴胺", "H", "收益预期与干劲：想做、想要、有盼头"),
            ["horm.sero"] = ("血清素", "H", "安宁与稳定：不急、不钻牛角尖"),
            ["horm.cort"] = ("皮质醇", "H", "压力与警觉：往坏处想、绷着"),
            ["horm.adr"] = ("肾上腺素", "H", "危急：精力↑ 警觉↑，过量有器官代价"),
            ["horm.endo"] = ("内啡肽", "H", "镇痛与松快：发泄过、忍下来之后的那口气"),
            ["horm.chol"] = ("胆固醇", "H", "颓废遭打击时的中和：食欲↓ 干劲↑"),
            ["horm.ltd"] = ("长期激励素", "H", "长期目标达成的奖励，衰减极慢")
        };

    // ==================== 给 XAML 绑定的行模型 ====================

    /// <summary>读数的一行：标签 + 值。值可以是长句，交给 XAML 换行。</summary>
    public sealed record InspectorRow(string Label, string Value);

    /// <summary>时间线的一条：时间 + 标题 + 一句话。</summary>
    public sealed record TimelineEvent(string Time, string Title, string Text);
}
