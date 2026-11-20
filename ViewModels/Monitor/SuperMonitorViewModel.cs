using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Dispatching;
using Microsoft.Maui.Graphics;
using WarmAsBefore.Drawables;
using WarmAsBefore.Models;
using WarmAsBefore.Services;
using WarmAsBefore.Services.Core;

namespace WarmAsBefore.ViewModels;

/// <summary>
/// 超级监测页：论文五层架构的「活流程图」+ 日程 / 器官 / 记忆 / 数值 现状汇总。
/// 对话流程执行到哪个节点，FlowTracer 点亮该节点，Drawable 按 Glow 渐隐余辉重绘（数据驱动、非永续动画）。
/// 页面只在导航进来时构建，离开就退订事件、停掉两只钟；所有展示性读取都各自 try/catch，
/// 任何一路服务出错都只让那一路空着，不把整页带崩。
/// </summary>
public sealed partial class SuperMonitorViewModel : ObservableObject, IQueryAttributable
{
    private readonly GameEngine _engine;
    private readonly MemoryOverviewService _overview;

    private string _charId = "";
    private bool _pageVisible;
    private bool _loading;
    private bool _refreshPending;
    private IDispatcherTimer? _clock;      // 30s 数据刷新（保留原行为）
    private IDispatcherTimer? _glowClock;  // 120ms 余辉渐隐刷新，只在页面可见时运行
    private bool _flowHooked;

    private readonly Dictionary<string, NodeDetail> _details = new(StringComparer.Ordinal);
    // 每个节点的「仔细看」字段：LoadAsync 时连同原始领域对象一起组装好，选中时直接取，不再回读服务。
    private readonly Dictionary<string, IReadOnlyList<DetailLine>> _detailLinesById = new(StringComparer.Ordinal);
    private readonly Action<string> _sourceChangedHandler;
    private readonly Action _flowChangedHandler;

    // 架构骨架：五层节点 + 论文 Mermaid 拓扑连线，首进页面（LoadAsync）构造一次，之后复用。
    private IReadOnlyList<ArchNode>? _archNodes;
    private IReadOnlyList<ArchLink>? _archLinks;

    public ArchitectureFlowDrawable ArchDrawable { get; } = new();

    public SuperMonitorViewModel(GameEngine engine, MemoryOverviewService overview)
    {
        _engine = engine;
        _overview = overview;
        _sourceChangedHandler = OnSourceChanged;
        _flowChangedHandler = OnFlowChanged;
        // 余辉亮度直连追踪器；Drawable 只认 Func<string,float>，不反查静态类。
        ArchDrawable.GlowProvider = FlowTracer.Glow;
    }

    private string CharId
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(_charId)) return _charId;
            try
            {
                var id = _engine.State.CharacterId;
                return string.IsNullOrEmpty(id) ? "小雨" : id;
            }
            catch { return "小雨"; }
        }
    }

    // ==================== 静态入口：两个入口页的按钮只靠 XAML 就能跳，不改它们的 VM ====================

    /// <summary>
    /// SavePage / CharacterLibraryPage 的「监测」按钮直接绑这个静态命令：
    /// 传了 charId 就用传的，没传就取当前存档角色。主 Agent 不必改这两个页面的 code-behind / VM。
    /// </summary>
    public static ICommand NavigateToMonitor { get; } = new Command<object?>(parameter =>
    {
        var id = parameter as string;
        if (string.IsNullOrWhiteSpace(id))
        {
            try
            {
                var services = Application.Current?.Handler?.MauiContext?.Services;
                id = (services?.GetService(typeof(GameEngine)) as GameEngine)?.State.CharacterId;
            }
            catch { /* 取不到就退到空，VM 自己会兜到当前角色 */ }
        }
        try
        {
            _ = Shell.Current.GoToAsync($"superMonitor?charId={Uri.EscapeDataString(id ?? "")}");
        }
        catch (Exception ex) { App.WriteLog("SuperMonitor.Navigate -> " + ex.Message); }
    });

    /// <summary>
    /// 3D 向量显示的预留口：主 Agent 接 Evergine 时把一个 (可见, charId) -> void 的回调挂到这里，
    /// 开关一拨就会通知场景显隐；不挂也不会崩，只是纯 2D 流程图照常工作。
    /// </summary>
    public static Action<bool, string>? VectorOverlayChanged;

    // ==================== 页面状态 ====================

    [ObservableProperty] private string _characterName = "";
    [ObservableProperty] private string _dateText = "";
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private bool _show3D;
    [ObservableProperty] private bool _hasSelection;
    [ObservableProperty] private NodeDetail? _selectedNode;
    /// <summary>选中节点的逐字段详情（标签,值），供 XAML BindableLayout 逐行展示，能「仔细看」。</summary>
    [ObservableProperty] private ObservableCollection<DetailLine> _detailLines = new();
    [ObservableProperty] private ObservableCollection<TimelineRow> _timeline = new();
    [ObservableProperty] private ObservableCollection<OrganRow> _organs = new();
    [ObservableProperty] private ObservableCollection<LayerRow> _memoryLayers = new();
    [ObservableProperty] private ObservableCollection<StatRow> _stats = new();

    partial void OnSelectedNodeChanged(NodeDetail? value)
    {
        HasSelection = value is not null;
        // 取消选中（如定时重载）时把详情清空，避免残留上一个节点的字段。
        if (value is null) DetailLines = new ObservableCollection<DetailLine>();
    }

    /// <summary>3D 开关只改状态 + 通知外部桥；显隐由页面 IsVisible 负责，不做任何循环动画。</summary>
    partial void OnShow3DChanged(bool value)
    {
        try { VectorOverlayChanged?.Invoke(value, CharId); }
        catch (Exception ex) { App.WriteLog("SuperMonitor.VectorOverlay -> " + ex.Message); }
        Redraw();
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("charId", out var v) && v is string id && !string.IsNullOrWhiteSpace(id))
        {
            var decoded = Uri.UnescapeDataString(id);
            if (!string.Equals(decoded, _charId, StringComparison.Ordinal))
            {
                _charId = decoded;
                // 换角色/进新存档：清掉上一轮残留的亮灯。
                try { FlowTracer.Clear(); } catch { /* 追踪器永不外泄异常，这里仅作保险 */ }
            }
        }
        _ = LoadAsync();
    }

    // ==================== 生命周期 ====================

    private Action? _invalidate;
    public void AttachInvalidate(Action invalidate) => _invalidate = invalidate;

    private void Redraw()
    {
        var invalidate = _invalidate;
        if (!_pageVisible || invalidate is null) return;
        try { Helpers.Ui.Post(invalidate); }
        catch (Exception ex) { App.WriteLog("SuperMonitor.Redraw -> " + ex.Message); }
    }

    public void SetViewport(double w, double h)
    {
        ArchDrawable.SetCanvasSize(w, h);
        Redraw();
    }

    public void StartClock()
    {
        _pageVisible = true;
        HookSourceEvents();
        HookFlow();
        // Changed 只在 Tick 真的改变了状态签名时才广播；只订阅不 Tick 事件永远不会来。
        TickSources(CharId);
        if (_clock is null)
        {
            try
            {
                _clock = Application.Current?.Dispatcher.CreateTimer();
                if (_clock is not null)
                {
                    _clock.Interval = TimeSpan.FromSeconds(30);
                    _clock.Tick += OnClockTick;
                    _clock.Start();
                }
            }
            catch (Exception ex) { App.WriteLog("SuperMonitor.StartClock -> " + ex.Message); }
        }
        // 余辉钟：120ms 触发一次「按需 Invalidate」，只在页可见时跑；无亮灯则什么都不画。
        if (_glowClock is null)
        {
            try
            {
                _glowClock = Application.Current?.Dispatcher.CreateTimer();
                if (_glowClock is not null)
                {
                    _glowClock.Interval = TimeSpan.FromMilliseconds(120);
                    _glowClock.Tick += OnGlowTick;
                    _glowClock.Start();
                }
            }
            catch (Exception ex) { App.WriteLog("SuperMonitor.GlowClock -> " + ex.Message); }
        }
        if (Timeline.Count == 0 && Organs.Count == 0) _ = LoadAsync();
        else Redraw();
    }

    /// <summary>离开页面：退订事件、停钟、不再重画。下次导航进来重新构建。</summary>
    public void StopClock()
    {
        _pageVisible = false;
        try
        {
            if (_clock is not null) { _clock.Stop(); _clock.Tick -= OnClockTick; _clock = null; }
        }
        catch (Exception ex) { App.WriteLog("SuperMonitor.StopClock -> " + ex.Message); }
        try
        {
            if (_glowClock is not null) { _glowClock.Stop(); _glowClock.Tick -= OnGlowTick; _glowClock = null; }
        }
        catch (Exception ex) { App.WriteLog("SuperMonitor.StopGlow -> " + ex.Message); }
        UnhookSourceEvents();
        UnhookFlow();
    }

    private void OnClockTick(object? sender, EventArgs e)
    {
        if (_pageVisible && !_loading) _ = LoadAsync();
    }

    private void OnGlowTick(object? sender, EventArgs e)
    {
        if (!_pageVisible) return;
        try
        {
            // 数据驱动：只有余辉窗口内还有节点时才刷新，避免变成永续动画。
            if (FlowTracer.LitNodes().Count > 0) Redraw();
        }
        catch { /* 装饰性刷新失败不打扰主流程 */ }
    }

    /// <summary>
    /// 从权威服务推进一次状态：Changed 由 Tick 驱动，Observe 只读不广播。
    /// 进页、定时刷新、手动刷新、Changed 后的重读都会走到这里，不需要调用方各自记得。
    /// </summary>
    private static void TickSources(string id)
    {
        try { OrganSimulationService.Instance.Tick(id); }
        catch (Exception ex) { App.WriteLog("SuperMonitor.OrganTick -> " + ex.Message); }
        try { DailyPlanService.Instance.Tick(id); }
        catch (Exception ex) { App.WriteLog("SuperMonitor.PlanTick -> " + ex.Message); }
    }

    // ==================== 事件订阅：源 Changed 是 Action<string>，FlowTracer.Changed 是 Action ====================

    private void HookSourceEvents()
    {
        UnhookSourceEvents();
        try
        {
            OrganSimulationService.Instance.Changed += _sourceChangedHandler;
            DailyPlanService.Instance.Changed += _sourceChangedHandler;
        }
        catch (Exception ex) { App.WriteLog("SuperMonitor.Hook -> " + ex.Message); }
    }

    private void UnhookSourceEvents()
    {
        try
        {
            OrganSimulationService.Instance.Changed -= _sourceChangedHandler;
            DailyPlanService.Instance.Changed -= _sourceChangedHandler;
        }
        catch { /* 源已经释放：忽略 */ }
    }

    private void OnSourceChanged(string charId)
    {
        // 单例服务会给所有角色广播，只理当前角色的那一条。
        if (!string.IsNullOrEmpty(charId) && !string.Equals(charId, CharId, StringComparison.Ordinal)) return;
        ScheduleRefresh();
    }

    private void HookFlow()
    {
        if (_flowHooked) return;
        try { FlowTracer.Changed += _flowChangedHandler; _flowHooked = true; }
        catch (Exception ex) { App.WriteLog("SuperMonitor.HookFlow -> " + ex.Message); }
    }

    private void UnhookFlow()
    {
        if (!_flowHooked) return;
        try { FlowTracer.Changed -= _flowChangedHandler; }
        catch { /* 追踪器已释放：忽略 */ }
        _flowHooked = false;
    }

    /// <summary>
    /// 执行打点回调：FlowTracer.Changed 可能从 ChatEngine 的后台线程触发，
    /// 角色过滤后统一走 Helpers.Ui.Post 回到 UI 线程再 Invalidate（数据驱动的按需重绘）。
    /// </summary>
    private void OnFlowChanged()
    {
        try
        {
            var last = FlowTracer.LastCharId;
            if (!string.IsNullOrEmpty(last) && !string.Equals(last, CharId, StringComparison.Ordinal)) return;
        }
        catch { /* 过滤失败就不过滤，宁可多画一次 */ }
        Helpers.Ui.Post(Redraw);
    }

    private async void ScheduleRefresh()
    {
        if (!_pageVisible || _refreshPending) return;
        _refreshPending = true;
        try
        {
            await Task.Delay(250);
            if (_pageVisible && !_loading) await LoadAsync();
        }
        catch { /* 装饰性刷新失败不打扰主流程 */ }
        finally { _refreshPending = false; }
    }

    // ==================== 并行加载 ====================

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (_loading) return;
        _loading = true;
        try
        {
            var id = CharId;
            DateText = DateTime.Now.ToString("yyyy-MM-dd");
            TickSources(id);

            var organTask = Task.Run(() =>
            {
                try { return OrganSimulationService.Instance.Observe(id); }
                catch { return (IReadOnlyList<OrganState>)Array.Empty<OrganState>(); }
            });
            var influenceTask = Task.Run(() =>
            {
                try { return OrganSimulationService.Instance.Influences(); }
                catch { return (IReadOnlyList<OrganInfluence>)Array.Empty<OrganInfluence>(); }
            });
            var planTask = Task.Run(async () =>
            {
                try
                {
                    // 先确保今天这份日程已生成（AI 失败也有本地模板兜底），再同步取。
                    await DailyPlanService.Instance.EnsureTodayAsync(id);
                    return DailyPlanService.Instance.GetToday(id);
                }
                catch { return null; }
            });
            var memoryTask = LoadMemoryAsync(id);
            var numericTask = Task.Run(() => ReadNumeric(id));

            // 三种监测的真实数据源：心理健康目录 / 内分泌池 / 记忆核心。
            // 各自独立取数、独立 try/catch，任一服务拿不到只让那一路空着，不把整页带崩。
            var mentalTask = Task.Run(async () =>
            {
                try
                {
                    var svc = ResolveService<MentalHealthService>();
                    return svc is null
                        ? (IReadOnlyList<MentalConditionState>)Array.Empty<MentalConditionState>()
                        : await svc.ListAsync(id);
                }
                catch { return (IReadOnlyList<MentalConditionState>)Array.Empty<MentalConditionState>(); }
            });
            var innerTask = Task.Run<InnerChemicals?>(async () =>
            {
                try
                {
                    var svc = ResolveService<InnerLifeService>();
                    if (svc is null) return null;
                    await svc.SnapshotAsync(id); // 先结算一次，PeekChemicals 才拿得到当前值
                    return svc.PeekChemicals(id);
                }
                catch { return null; }
            });
            var coreTask = Task.Run<(List<HumanMemoryEntry> Core, List<HumanMemoryEntry> All)>(async () =>
            {
                try
                {
                    var svc = ResolveService<HumanMemoryService>();
                    if (svc is null) return (new List<HumanMemoryEntry>(), new List<HumanMemoryEntry>());
                    return (await svc.CoreAsync(id), await svc.AllAsync(id));
                }
                catch { return (new List<HumanMemoryEntry>(), new List<HumanMemoryEntry>()); }
            });

            await Task.WhenAll(organTask, influenceTask, planTask, memoryTask, numericTask,
                mentalTask, innerTask, coreTask);

            var organs = organTask.Result ?? Array.Empty<OrganState>();
            var influences = influenceTask.Result ?? Array.Empty<OrganInfluence>();
            var plan = planTask.Result;
            var layers = memoryTask.Result ?? new List<MemoryOverviewService.LayerStat>();
            var numeric = numericTask.Result;
            var mentalStates = mentalTask.Result ?? Array.Empty<MentalConditionState>();
            var chem = innerTask.Result;
            var (coreMemories, allMemories) = coreTask.Result;

            if (!string.IsNullOrWhiteSpace(numeric.Name)) CharacterName = numeric.Name;
            else CharacterName = _engine.Roster.TryGetValue(id, out var rosterCh) ? rosterCh.Profile.Name : id;

            var timelineRows = new List<TimelineRow>();
            _details.Clear();
            _detailLinesById.Clear();

            if (plan is not null && plan.Items is { Count: > 0 } items)
            {
                foreach (var item in items.OrderBy(i => i.Hour).ThenBy(i => i.Minute))
                {
                    var time = item.TimeText;
                    timelineRows.Add(new TimelineRow(item.Id, time, item.Done ? "✓" : "○",
                        item.Title, $"{KindText(item.Kind)} · {item.Detail}", item.Done));
                    Remember($"plan-{item.Id}", item.Title, "日程",
                        item.Done ? "Active" : "Inactive", $"{time} · {item.Detail}", "",
                        new[]
                        {
                            new DetailLine("时间", time),
                            new DetailLine("类别", KindText(item.Kind)),
                            new DetailLine("标题", item.Title),
                            new DetailLine("说明", string.IsNullOrWhiteSpace(item.Detail) ? "无" : item.Detail),
                            new DetailLine("状态", item.Done ? "✓ 已完成" : "○ 未完成"),
                            new DetailLine("完成于", item.ResultAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "未完成"),
                        });
                }
            }

            foreach (var o in organs)
            {
                var key = $"organ-{o.Kind}";
                var symptoms = Text(o.Symptoms);
                var influence = Text(o.Influences);
                // 6 条连锁规则里与该器官相关的（From/To 命中中文名），拼成一行人话。
                var ruleText = string.Join("；", influences
                    .Where(i => string.Equals(i.From, o.Name, StringComparison.Ordinal)
                             || string.Equals(i.To, o.Name, StringComparison.Ordinal))
                    .Select(i => $"{i.From} → {i.To}：{i.Effect}（{i.DelayMinutes} 分钟）"));
                var influenceAll = string.IsNullOrWhiteSpace(ruleText)
                    ? influence
                    : (string.IsNullOrWhiteSpace(influence) ? ruleText : influence + "；" + ruleText);
                // Status 传枚举名给 drawable 判亮灯；详情里直接显示服务给好的中文 StatusText。
                Remember(key, o.Name, "器官", o.Status.ToString(),
                    $"{o.StatusText} · {(string.IsNullOrWhiteSpace(symptoms) ? influence : symptoms)}", influence,
                    new[]
                    {
                        new DetailLine("器官", o.Name),
                        new DetailLine("当前值", $"{o.Value:0.##}"),
                        new DetailLine("状态", o.StatusText),
                        new DetailLine("症状", string.IsNullOrWhiteSpace(symptoms) ? "无" : symptoms),
                        new DetailLine("连锁影响", string.IsNullOrWhiteSpace(influenceAll) ? "无" : influenceAll),
                        new DetailLine("更新于", o.UpdatedAt.ToLocalTime().ToString("HH:mm:ss")),
                    });
            }

            foreach (var layer in layers)
            {
                Remember($"mem-{layer.Layer}", layer.Name, "记忆", "",
                    $"{layer.Count} {layer.Unit} · {layer.Detail}", layer.Describe,
                    new[]
                    {
                        new DetailLine("名称", layer.Name),
                        new DetailLine("说明", layer.Describe),
                        new DetailLine("数量", $"{layer.Count}"),
                        new DetailLine("单位", layer.Unit),
                        new DetailLine("详情", layer.Detail),
                    });
            }

            Remember("stat-affection", $"好感 {numeric.Affection:0}", "数值", "Active",
                "关系亲近程度", "好感越高，越愿意主动靠近你",
                new[]
                {
                    new DetailLine("数值", $"{numeric.Affection:0.##}"),
                    new DetailLine("说明", "关系亲近程度"),
                    new DetailLine("影响", "好感越高，越愿意主动靠近你"),
                });
            Remember("stat-trust", $"信任 {numeric.Trust:0}", "数值", "Active",
                "愿意把软弱露出来的程度", "信任够高才会说出真心话",
                new[]
                {
                    new DetailLine("数值", $"{numeric.Trust:0.##}"),
                    new DetailLine("说明", "愿意把软弱露出来的程度"),
                    new DetailLine("影响", "信任够高才会说出真心话"),
                });
            Remember("stat-energy", $"精力 {numeric.Energy:0}", "数值", "Active",
                "今天还剩多少力气", "精力低时不想动、不想说话",
                new[]
                {
                    new DetailLine("数值", $"{numeric.Energy:0.##}"),
                    new DetailLine("说明", "今天还剩多少力气"),
                    new DetailLine("影响", "精力低时不想动、不想说话"),
                });

            // ========== 三种监测的真实数据：挂到架构节点上，选中节点即在详情区逐行显示 ==========
            var healthSvc = ResolveService<MentalHealthService>();

            // ① 侵入性思维：心理健康目录里 id 含 intrusive 的真实条目（症状 / 严重度 / 稳定性）。
            try
            {
                var intrusive = mentalStates
                    .Where(s => s.Id.Contains("intrusive", StringComparison.OrdinalIgnoreCase))
                    .Select(s => (State: s, Cond: healthSvc?.Find(s.Id)))
                    .ToList();
                var lines = new List<DetailLine>
                {
                    new("监测项", "侵入性思维（心理健康目录中 id 含 intrusive 的条目）")
                };
                if (intrusive.Count == 0)
                {
                    lines.Add(new DetailLine("当前记录", "无——该角色尚未配置侵入性思维类状况"));
                }
                else
                {
                    lines.Add(new DetailLine("当前条数", $"{intrusive.Count}"));
                    foreach (var (st, cond) in intrusive)
                    {
                        var symptoms = string.IsNullOrWhiteSpace(cond?.Symptoms) ? "无" : cond!.Symptoms;
                        var note = string.IsNullOrWhiteSpace(st.Note) ? "" : $" · 备注：{st.Note}";
                        lines.Add(new DetailLine(cond?.Name ?? st.Id,
                            $"{MentalHealthService.SeverityName(st.Severity)} · 稳定度 {st.Stability:0.00} · 症状：{symptoms}{note}"));
                    }
                }
                Remember("mind.intrusive", "侵入性思维", "核心心智与病理", "监测",
                    intrusive.Count == 0 ? "当前无侵入性思维类状况" : $"已记录 {intrusive.Count} 条侵入性思维相关状况",
                    "", lines);
            }
            catch (Exception ex) { App.WriteLog("SuperMonitor.Intrusive -> " + ex.Message); }

            // ② 互相调制：内分泌当前值（InnerLifeService）+ 器官连锁规则（Influences 的 6 条）。
            try
            {
                var lines = new List<DetailLine>();
                if (chem is null)
                {
                    lines.Add(new DetailLine("内分泌", "内心生活未开启或尚未结算，暂无化学池读数"));
                }
                else
                {
                    lines.Add(new DetailLine("数据来源", "InnerLifeService 当前化学池（已按半衰期结算）"));
                    lines.Add(new DetailLine("多巴胺（兴奋/快乐）", $"{chem.Dopamine:0.00}"));
                    lines.Add(new DetailLine("皮质醇（压力/警觉）", $"{chem.Cortisol:0.00}"));
                    lines.Add(new DetailLine("血清素（安宁/稳定）", $"{chem.Serotonin:0.00}"));
                    lines.Add(new DetailLine("肾上腺素（危急）", $"{chem.Adrenaline:0.00}"));
                    lines.Add(new DetailLine("胆固醇（打击中和）", $"{chem.Cholesterol:0.00}"));
                    lines.Add(new DetailLine("长期性激励素（长期）", $"{chem.LongTermDrive:0.00}"));
                    lines.Add(new DetailLine("干劲（派生）", $"{chem.Drive:0.00}"));
                    lines.Add(new DetailLine("更新于", chem.UpdatedAt.ToLocalTime().ToString("HH:mm:ss")));
                }
                if (influences.Count == 0)
                {
                    lines.Add(new DetailLine("连锁规则", "OrganSimulationService 暂无连锁规则"));
                }
                else
                {
                    lines.Add(new DetailLine("· 连锁规则", $"共 {influences.Count} 条（器官 → 器官 / 全身）"));
                    foreach (var i in influences)
                        lines.Add(new DetailLine($"{i.From} → {i.To}（{i.DelayMinutes} 分）", i.Effect));
                }
                Remember("emo.coupling", "记忆情绪耦合", "记忆与情绪双向耦合", "监测",
                    $"内分泌读数 + {influences.Count} 条连锁规则", "", lines);
            }
            catch (Exception ex) { App.WriteLog("SuperMonitor.Coupling -> " + ex.Message); }

            // ③ 创伤：心理健康目录的创伤类 + 核心记忆里 IsTrauma 的条目（阶段用 TraumaStageName）。
            try
            {
                var traumaStates = mentalStates.Where(s =>
                {
                    var c = healthSvc?.Find(s.Id);
                    return c is not null && (string.Equals(c.Category, "创伤", StringComparison.Ordinal)
                        || s.Id.Contains("ptsd", StringComparison.OrdinalIgnoreCase)
                        || s.Id.Contains("trauma", StringComparison.OrdinalIgnoreCase));
                }).ToList();
                var traumaMems = coreMemories.Where(m => m.IsTrauma).Take(6).ToList();
                var lines = new List<DetailLine>
                {
                    new("数据来源", "心理健康目录（创伤类）+ 核心记忆 IsTrauma 条目")
                };
                if (traumaStates.Count == 0)
                {
                    lines.Add(new DetailLine("心理状况", "无——未配置创伤类状况"));
                }
                else
                {
                    foreach (var st in traumaStates)
                    {
                        var c = healthSvc?.Find(st.Id);
                        var symptoms = string.IsNullOrWhiteSpace(c?.Symptoms) ? "" : c!.Symptoms + " · ";
                        var behavior = string.IsNullOrWhiteSpace(c?.Behavior) ? "无" : c!.Behavior;
                        lines.Add(new DetailLine(c?.Name ?? st.Id,
                            $"{MentalHealthService.SeverityName(st.Severity)} · 稳定度 {st.Stability:0.00} · {symptoms}表现：{behavior}"));
                    }
                }
                if (traumaMems.Count == 0)
                {
                    lines.Add(new DetailLine("创伤记忆", "无核心创伤记忆"));
                }
                else
                {
                    foreach (var m in traumaMems)
                    {
                        var stage = HumanMemoryMath.TraumaStageName(m.TraumaStage);
                        lines.Add(new DetailLine($"记忆 · {stage}",
                            $"{m.Text}（情绪强度 {m.EmotionalIntensity:0.00}，{stage}）"));
                    }
                }
                Remember("mind.trauma", "创伤监测", "核心心智与病理", "监测",
                    $"创伤类状况 {traumaStates.Count} 条 · 核心创伤记忆 {traumaMems.Count} 条", "", lines);
            }
            catch (Exception ex) { App.WriteLog("SuperMonitor.Trauma -> " + ex.Message); }

            // ④ 行为模式：目前没有专门的数值化服务，只给真实记忆计数 + 程序记忆机制说明，不编造数值。
            try
            {
                var coreCount = coreMemories.Count;
                var abstractionCount = allMemories.Count(m => m.Kind == HumanMemoryKind.Abstraction);
                Remember("mind.pattern", "行为模式", "核心心智与病理", "监测",
                    "程序记忆 / 习惯化应对套路（暂无专门数值化服务）", "",
                    new[]
                    {
                        new DetailLine("机制说明", "程序记忆：由深层记忆塑造的习惯化应对套路（对应架构节点 mem.procedural）"),
                        new DetailLine("建模状态", "当前无专门的数值化服务，不显示推测数值"),
                        new DetailLine("相关记忆", $"核心记忆 {coreCount} 条 · 抽象印象 {abstractionCount} 条（来自 HumanMemoryService）")
                    });
            }
            catch (Exception ex) { App.WriteLog("SuperMonitor.Pattern -> " + ex.Message); }

            if (influences.Count > 0)
            {
                InfluenceNote = string.Join("、", influences.Select(i =>
                    $"{i.From} → {i.To}：{i.Effect}（{i.DelayMinutes} 分钟）"));
            }

            Timeline = new ObservableCollection<TimelineRow>(timelineRows);
            Organs = new ObservableCollection<OrganRow>(organs.Select(o => new OrganRow(
                o.Name, o.StatusText, $"{o.Value:0.##}",
                Text(o.Symptoms), Text(o.Influences), $"{o.UpdatedAt:HH:mm}")));
            MemoryLayers = new ObservableCollection<LayerRow>(layers.Select(l => new LayerRow(
                l.Name, $"{l.Count} {l.Unit}", l.Detail)));
            Stats = new ObservableCollection<StatRow>(new[]
            {
                new StatRow("好感", $"{numeric.Affection:0}"),
                new StatRow("信任", $"{numeric.Trust:0}"),
                new StatRow("精力", $"{numeric.Energy:0}")
            });

            // 架构骨架：首进页面构造一次五层节点与 Mermaid 拓扑连线。
            EnsureArchitecture();
            ArchDrawable.Nodes = _archNodes;
            ArchDrawable.Links = _archLinks;
            ArchDrawable.SelectedId = null;
            SelectedNode = null;
            StatusText = $"日程 {timelineRows.Count} · 器官 {organs.Count} · 记忆 {layers.Count} · 数值 3 · 架构 {_archNodes?.Count ?? 0} 节点";

            if (!string.IsNullOrWhiteSpace(plan?.Outfit))
                StatusText += $" · 今日穿搭：{plan.Outfit}";
            Redraw();
        }
        catch (Exception ex)
        {
            StatusText = "监测数据读取失败，已跳过出错的那一路";
            App.WriteLog("SuperMonitor.Load -> " + ex.Message);
        }
        finally { _loading = false; }
    }

    [ObservableProperty] private string _influenceNote = "";

    private async Task<List<MemoryOverviewService.LayerStat>> LoadMemoryAsync(string id)
    {
        try { return await _overview.StatsAsync(id); }
        catch (Exception ex) { App.WriteLog("SuperMonitor.Memory -> " + ex.Message); return new(); }
    }

    private (string Name, double Affection, double Trust, double Energy) ReadNumeric(string id)
    {
        try
        {
            var ch = _engine.ActiveCharacter;
            if (ch is not null && (string.IsNullOrWhiteSpace(id) || ch.Profile.Id == id))
                return (ch.Profile.Name, ch.State.Affection, ch.State.Trust, ch.State.Energy);
            if (_engine.Roster.TryGetValue(id, out var other))
                return (other.Profile.Name, other.State.Affection, other.State.Trust, other.State.Energy);
        }
        catch (Exception ex) { App.WriteLog("SuperMonitor.Numeric -> " + ex.Message); }
        return ("", 0, 0, 0);
    }

    private void Remember(string id, string label, string group, string status, string detail, string influence,
        IReadOnlyList<DetailLine>? lines = null)
    {
        _details[id] = new NodeDetail(id, label, group, status, detail, influence);
        if (lines is not null) _detailLinesById[id] = lines;
    }

    /// <summary>从 MAUI 服务容器取单例服务（与 OrganSimulationService / 工具层的取法一致）。</summary>
    private static T? ResolveService<T>() where T : class
    {
        try
        {
            var services = Microsoft.Maui.IPlatformApplication.Current?.Services
                ?? Application.Current?.Handler?.MauiContext?.Services;
            return services?.GetService(typeof(T)) as T;
        }
        catch { return null; }
    }

    // 服务模型里 Symptoms / Influences 可能是字符串也可能是列表，这里统一成人话，免得绑定类型一变就崩。
    private static string Text(object? value) => value switch
    {
        null => "",
        string s => s,
        System.Collections.IEnumerable en => string.Join("、", en.Cast<object>().Select(x => x?.ToString()).Where(x => !string.IsNullOrEmpty(x))),
        _ => value.ToString() ?? ""
    };

    private static string KindText(object? kind) => string.IsNullOrEmpty(kind?.ToString()) ? "事项" : kind!.ToString()!;

    // ==================== 命令 ====================

    [RelayCommand]
    private void SelectNode(object? parameter)
    {
        try
        {
            var id = parameter as string;
            if (string.IsNullOrEmpty(id) && parameter is PointF p) id = ArchDrawable.HitTest(p.X, p.Y);
            if (string.IsNullOrEmpty(id)) return;

            var node = ArchDrawable.Find(id);
            if (node is null)
            {
                // 兼容仍留在领域数据里的旧 id（日程 / 器官 / 记忆 / 数值）。
                if (_detailLinesById.TryGetValue(id, out var legacy))
                {
                    SelectedNode = _details.TryGetValue(id, out var d)
                        ? d : new NodeDetail(id, id, "", "", "", "");
                    DetailLines = new ObservableCollection<DetailLine>(legacy);
                    ArchDrawable.SelectedId = id;
                    Redraw();
                }
                return;
            }

            var lines = new List<DetailLine> { new("所在层", LayerTitle(node.Layer)) };
            if (!string.IsNullOrWhiteSpace(node.Module)) lines.Add(new DetailLine("实现模块", node.Module!));
            if (!string.IsNullOrWhiteSpace(node.Detail)) lines.Add(new DetailLine("机制说明", node.Detail!));
            var lit = FlowTracer.IsLit(id);
            lines.Add(new DetailLine("执行状态", lit ? "✓ 正在流经" : "○ 静默"));
            try
            {
                var extra = RealTimeLines(id);
                if (extra.Count > 0)
                {
                    lines.Add(new DetailLine("· 实时关联", "同刻的领域数据"));
                    lines.AddRange(extra);
                }
            }
            catch (Exception ex) { App.WriteLog("SuperMonitor.RealTime -> " + ex.Message); }

            SelectedNode = new NodeDetail(id, node.Label, LayerTitle(node.Layer),
                lit ? "流经中" : "静默", node.Detail ?? "", "");
            DetailLines = new ObservableCollection<DetailLine>(lines);
            ArchDrawable.SelectedId = id;
            Redraw();
        }
        catch (Exception ex) { App.WriteLog("SuperMonitor.SelectNode -> " + ex.Message); }
    }

    /// <summary>若节点 id 能在现有器官 / 记忆 / 日程 / 数值数据里找到对应项，附上实时值。</summary>
    private IReadOnlyList<DetailLine> RealTimeLines(string id)
    {
        var result = new List<DetailLine>();
        if (string.IsNullOrEmpty(id)) return result;
        if (_detailLinesById.TryGetValue(id, out var exact) && exact.Count > 0)
        {
            result.AddRange(exact);
            return result;
        }
        if (id.StartsWith("soma.", StringComparison.Ordinal))
        {
            foreach (var o in Organs.Take(8))
                result.Add(new DetailLine(o.Name, $"{o.Value} · {o.Status}"));
        }
        else if (id.StartsWith("mem.", StringComparison.Ordinal) || id.StartsWith("emo.", StringComparison.Ordinal))
        {
            foreach (var l in MemoryLayers)
                result.Add(new DetailLine(l.Name, $"{l.Count} · {l.Detail}"));
        }
        else if (id.StartsWith("eng.", StringComparison.Ordinal))
        {
            foreach (var s in Stats)
                result.Add(new DetailLine(s.Name, s.Value));
        }
        else if (id.StartsWith("in.", StringComparison.Ordinal) || id.StartsWith("plan", StringComparison.Ordinal))
        {
            result.Add(new DetailLine("今日日程", $"{Timeline.Count} 项"));
            foreach (var t in Timeline.Take(3))
                result.Add(new DetailLine($"{t.Time} {t.Title}", t.Detail));
        }
        return result;
    }

    /// <summary>画布 EndInteraction 的落点（相对画布左上角）。</summary>
    public void CanvasTap(PointF point) => SelectNode(point);

    [RelayCommand]
    private void ToggleDone(object? parameter)
    {
        var id = parameter as string;
        if (string.IsNullOrEmpty(id)) return;
        try
        {
            // 只走服务：MarkDone 会写盘、更新 ResultAt 并广播 Changed，勾选后才刷得回来。
            DailyPlanService.Instance.MarkDone(CharId, id);
        }
        catch (Exception ex) { App.WriteLog("SuperMonitor.MarkDone -> " + ex.Message); }
    }

    [RelayCommand]
    private void Toggle3D() => Show3D = !Show3D;

    [RelayCommand]
    private async Task Back() => await Shell.Current.GoToAsync("..");

    // ==================== 架构骨架：论文五层 ~50 节点 + Mermaid 拓扑 ====================

    private static string LayerTitle(string layer) => layer switch
    {
        "1" => "1 感知与输入抽象层",
        "2" => "2 记忆与情绪双向耦合",
        "3" => "3 核心心智与病理",
        "4" => "4 低级中枢熔断与躯体化",
        "5" => "5 .NET 工程落地",
        _ => "层 " + layer
    };

    private void EnsureArchitecture()
    {
        if (_archNodes is not null) return;
        try
        {
            var nodes = new List<ArchNode>();
            void N(string id, string label, string layer, string module, string detail) =>
                nodes.Add(new ArchNode(id, label, layer, module, detail));

            // ---- 层 1 感知与输入抽象层（13）----
            N("in.text", "文字输入", "1", "ChatEngine.SendCore", "Send 入口记录用户文本，整条执行链的起点");
            N("in.image", "图像输入", "1", "ChatEngine.SendCore", "附件图片入口，交给视觉特征提取");
            N("in.voice", "语音输入", "1", "ChatEngine.SendCore", "语音转写文本入口，交给声学分析");
            N("in.fusion", "多模态融合", "1", "ChatEngine.SendCore", "文字/图像/语音特征融合后进入具身锚定");
            N("in.semantic", "语义解析", "1", "ChatEngine.SendCore", "文本 → 语义特征");
            N("in.visual", "视觉特征", "1", "ChatEngine.SendCore", "图像附件的视觉特征提取");
            N("in.audio", "声学分析", "1", "ChatEngine.SendCore", "语音附件的声学特征分析");
            N("in.imagery", "意象唤起", "1", "HumanMemoryService", "语义特征触发记忆意象");
            N("in.intent", "意图识别", "1", "ChatEngine.SendCore", "识别意图，参与后续路由预判");
            N("in.edge", "边缘检测", "1", "ChatEngine.SendCore", "图形线条 / 边缘检测");
            N("in.entity", "实体识别", "1", "HumanMemoryService", "识别实体并关联记忆条目");
            N("in.tone", "语调情绪", "1", "EmotionMix", "语调 / 情绪分析，写入情绪基调");
            N("in.embodied", "具身锚定", "1", "EmotionMix / ChatEngine", "具身认知 / 情感锚定：多模态汇聚到心智入口");

            // ---- 层 2 记忆与情绪双向耦合（13）----
            N("mem.shallow", "浅记忆", "2", "HumanMemoryService.RecallAsync", "浅记忆 / 前记忆：近期对话缓冲");
            N("mem.deepen", "记忆深化", "2", "HumanMemoryService", "重要性 / 回想次数 / 唤醒度 / 时间衰减共同深化");
            N("mem.deep", "深层记忆", "2", "HumanMemoryService", "深层记忆：情景 / 语义 / 程序三类");
            N("mem.episodic", "情景记忆", "2", "HumanMemoryService.RecallTextAsync", "带时间地点的经历");
            N("mem.semanticM", "语义记忆", "2", "HumanMemoryService", "概念与事实");
            N("mem.procedural", "程序记忆", "2", "HumanMemoryService", "习惯化的应对套路");
            N("mem.retrieval", "记忆检索器", "2", "HumanMemoryService.RecallTextAsync", "按窗口 / 线索回想，受情绪偏置 moodBias 影响");
            N("emo.state", "情绪状态", "2", "EmotionMix / ChatEngine", "当前情绪：效价 / 唤醒度 / 控制感三维");
            N("emo.valence", "情绪效价", "2", "EmotionMix", "Valence（-1..1）");
            N("emo.arousal", "唤醒度", "2", "EmotionMix", "Arousal（0..1）");
            N("emo.dominance", "控制感", "2", "EmotionMix", "Dominance");
            N("emo.coupling", "记忆情绪耦合", "2", "ChatEngine.SendCore", "记忆 ↔ 情绪双向耦合，moodBias 生效时点亮");
            N("mem.rumination", "反刍循环", "2", "HumanMemoryService.ChainSearchAsync", "负面回忆链延展，加剧情绪内耗");
            // v1.65 专注 / 神游双系统：多巴胺驱动的本地状态机，专注重时神游会随机塞旧记忆介入。
            N("cognition.focus", "专注模式", "2", "CognitionModeService.EnterFocus", "多巴胺驱动、大模型高精度；耗专属精力，用得越多停留越久");
            N("cognition.wander", "神游模式", "2", "CognitionModeService.WanderInject", "低精度默认态；反刍/整理/纠错/新想法/回忆连线");
            N("cognition.intervene", "神游介入", "2", "CognitionModeService.ShouldWanderIntervene", "专注快撑不住时，神游往脑海随机塞旧记忆");

            // ---- 层 3 核心心智与病理（10）----
            N("mind.trauma", "创伤节点", "3", "ChatEngine", "核心信念与旧伤的锚点");
            N("mind.persona", "人格系统", "3", "ChatEngine", "人格 / 核心信念：设定情绪基调");
            N("mind.pattern", "行为模式", "3", "ChatEngine", "由深层记忆塑造的行为模式");
            N("mind.intrusive", "侵入性思维", "3", "ChatEngine", "不受控的念头");
            N("mind.summary", "总结评估", "3", "ChatEngine.ProcessReplyAsync", "对当前情境的认知评价");
            N("path.state", "病理状态机", "3", "ChatEngine", "PTSD / 抑郁 / 焦虑 / BPD 状态机");
            N("path.ptsd", "PTSD", "3", "ChatEngine", "降低创伤阈值，杏仁核劫持");
            N("path.depression", "抑郁", "3", "ChatEngine", "反刍死锁，情绪基线极低");
            N("path.anxiety", "焦虑", "3", "ChatEngine", "联想过度发散");
            N("path.bpd", "BPD", "3", "ChatEngine", "防御机制极化");

            // ---- 层 4 低级中枢熔断与躯体化（11）----
            N("soma.coping", "应对机制", "4", "OrganSimulationService", "应对决策：战 / 逃 / 僵三分支");
            N("soma.fight", "反击防御", "4", "OrganSimulationService", "高唤醒下的对峙分支");
            N("soma.reply", "理智回复", "4", "ChatEngine", "常态输出分支");
            N("soma.freeze", "僵直宕机", "4", "OrganSimulationService", "低控制感时的冻结分支");
            N("soma.bypass", "低级中枢越权", "4", "ChatEngine", "低级中枢越权 / 熔断，绕过理智");
            N("soma.response", "器官响应", "4", "OrganSimulationService.Observe", "器官响应 / 躯体化：脏器状态外显");
            N("soma.delay", "输出延迟", "4", "ChatEngine", "输出延迟 / 反应迟钝（Task.Delay）");
            N("soma.stutter", "字符碎裂", "4", "ChatEngine", "字符碎裂 / 结巴 / 颤抖（正则打断）");
            N("soma.downgrade", "逻辑降级", "4", "ApiGateway", "逻辑降级 / 算力熔断（MaxTokens 限制）");
            N("soma.panic", "惊恐发作", "4", "OrganSimulationService", "惊恐发作 / 呼吸急促模拟");
            N("soma.recovery", "器官恢复期", "4", "OrganSimulationService", "恢复期：情绪反复 / 迟滞");

            // ---- 层 5 .NET 工程落地（11）----
            N("eng.statepack", "状态打包", "5", "ChatEngine.RunTurnAsync", "把心智 / 器官状态打成模型输入");
            N("eng.router", "意图路由", "5", "ApiGateway", "意图路由分发：本地轻量 / 云端重量");
            N("eng.redis", "Redis 缓存", "5", "ApiGateway", "Redis 状态缓存（MindState）");
            N("eng.qdrant", "Qdrant 向量", "5", "HumanMemoryService", "Qdrant 向量记忆库（Vectors）");
            N("eng.sqlite", "SQLite 结构化", "5", "DailyPlanService", "SQLite / PostgreSQL 结构化数据");
            N("eng.model.light", "轻量模型", "5", "ApiGateway", "轻量本地模型（LLamaSharp / Qwen）");
            N("eng.model.heavy", "云端模型", "5", "ApiGateway.Chat", "云端大模型（Azure OpenAI / GPT-4o）");
            N("eng.prompt", "Prompt 组装", "5", "ApiGateway", "System Prompt 动态组装（Semantic Kernel）");
            N("eng.postprocess", "后处理", "5", "ChatEngine.ProcessReplyAsync", "文本修饰器 / 流式拦截中间件");
            N("eng.output", "流式输出", "5", "ChatEngine", "SSE 流式输出给用户");
            N("eng.loop", "循环反馈", "5", "ChatEngine.SendCore", "输出回灌为下一轮输入");

            var links = new List<ArchLink>();
            void L(string from, string to, LinkKind kind = LinkKind.Normal) =>
                links.Add(new ArchLink(from, to, kind));

            // ---- 层 1 层内主干 ----
            L("in.text", "in.semantic");
            L("in.image", "in.visual");
            L("in.voice", "in.audio");
            L("in.fusion", "in.embodied");
            L("in.semantic", "in.imagery");
            L("in.semantic", "in.intent");
            L("in.visual", "in.edge");
            L("in.visual", "in.entity");
            L("in.audio", "in.tone");
            L("in.imagery", "in.embodied");
            L("in.intent", "in.embodied");
            L("in.entity", "in.embodied");
            L("in.tone", "in.embodied");
            // 层 1 → 层 2
            L("in.embodied", "mem.shallow");

            // ---- 层 2 层内主干 + 双向 / 反馈 ----
            L("mem.shallow", "mem.deepen");
            L("mem.deepen", "mem.deep");
            L("mem.deep", "mem.episodic");
            L("mem.deep", "mem.semanticM");
            L("mem.deep", "mem.procedural");
            L("emo.state", "emo.valence");
            L("emo.state", "emo.arousal");
            L("emo.state", "emo.dominance");
            L("mem.deep", "emo.state", LinkKind.Bidirectional);      // 自下而上唤醒 / 自上而下
            L("emo.state", "mem.retrieval", LinkKind.Bidirectional); // 心境一致性过滤
            L("mem.deep", "mem.retrieval");
            L("mem.retrieval", "emo.coupling");
            L("emo.coupling", "emo.state", LinkKind.Bidirectional);
            L("mem.rumination", "mem.retrieval", LinkKind.Feedback);
            L("mem.rumination", "emo.state", LinkKind.Feedback);
            // 双系统：专注 ⇄ 神游，快撑不住时神游介入（反馈到专注）
            L("cognition.focus", "cognition.wander", LinkKind.Bidirectional);
            L("cognition.focus", "cognition.intervene", LinkKind.Feedback);
            L("cognition.intervene", "cognition.wander");
            L("cognition.wander", "mem.rumination", LinkKind.Feedback);

            // ---- 层 2 → 层 3 ----
            L("mem.retrieval", "mind.trauma");
            L("mem.deep", "mind.pattern");
            L("emo.state", "mind.trauma", LinkKind.Bidirectional);
            L("path.ptsd", "mind.trauma", LinkKind.Feedback);

            // ---- 层 3 层内主干 + 病理反馈 ----
            L("mind.trauma", "mind.persona");
            L("mind.pattern", "mind.intrusive");
            L("mind.pattern", "mind.summary");
            L("mind.intrusive", "mind.summary");
            L("mind.summary", "mind.persona", LinkKind.Bidirectional);
            L("path.state", "path.ptsd");
            L("path.state", "path.depression");
            L("path.state", "path.anxiety");
            L("path.state", "path.bpd");
            L("path.depression", "mem.rumination", LinkKind.Feedback);
            L("path.anxiety", "mem.deep", LinkKind.Feedback);
            L("path.bpd", "mind.persona", LinkKind.Feedback);

            // ---- 层 3 → 层 2 反向固化 ----
            L("mind.persona", "emo.state", LinkKind.Bidirectional);
            L("mind.summary", "emo.state", LinkKind.Bidirectional);

            // ---- 层 3 → 层 4 ----
            L("mind.persona", "soma.coping");
            L("emo.state", "soma.coping");
            L("mind.summary", "soma.coping");
            L("mind.trauma", "soma.bypass", LinkKind.Bidirectional);
            L("path.ptsd", "soma.bypass", LinkKind.Feedback);

            // ---- 层 4 层内主干 + 恢复反馈 ----
            L("soma.coping", "soma.fight");
            L("soma.coping", "soma.reply");
            L("soma.coping", "soma.freeze");
            L("soma.bypass", "soma.response");
            L("soma.response", "soma.delay");
            L("soma.response", "soma.stutter");
            L("soma.response", "soma.downgrade");
            L("soma.response", "soma.panic");
            L("soma.response", "soma.recovery");
            L("soma.recovery", "emo.state", LinkKind.Feedback);

            // ---- 层 4 / 层 2 → 层 5 ----
            L("soma.fight", "eng.statepack");
            L("soma.reply", "eng.statepack");
            L("soma.freeze", "eng.statepack");
            L("soma.delay", "eng.statepack");
            L("soma.stutter", "eng.statepack");
            L("mem.rumination", "eng.statepack");

            // ---- 层 5 层内主干 + 数据双向 + 循环反馈 ----
            L("eng.statepack", "eng.router");
            L("eng.router", "eng.redis", LinkKind.Bidirectional);
            L("eng.router", "eng.qdrant", LinkKind.Bidirectional);
            L("eng.router", "eng.sqlite", LinkKind.Bidirectional);
            L("eng.statepack", "eng.prompt");
            L("eng.prompt", "eng.model.light");
            L("eng.prompt", "eng.model.heavy");
            L("eng.router", "eng.model.light");
            L("eng.router", "eng.model.heavy");
            L("eng.model.light", "eng.postprocess");
            L("eng.model.heavy", "eng.postprocess");
            L("eng.postprocess", "eng.output");
            L("eng.output", "eng.loop");
            L("eng.loop", "in.text", LinkKind.Feedback);

            _archNodes = nodes;
            _archLinks = links;
        }
        catch (Exception ex)
        {
            App.WriteLog("SuperMonitor.Arch -> " + ex.Message);
            _archNodes ??= new List<ArchNode>();
            _archLinks ??= new List<ArchLink>();
        }
    }

    // ==================== 给 XAML 绑定的行模型 ====================

    public sealed record TimelineRow(string Id, string Time, string Mark, string Title, string Detail, bool IsDone);
    public sealed record OrganRow(string Name, string Status, string Value, string Symptoms, string Influence, string UpdatedAt);
    public sealed record LayerRow(string Name, string Count, string Detail);
    public sealed record StatRow(string Name, string Value);
    public sealed record NodeDetail(string Id, string Title, string Group, string Status, string Detail, string Influence);
    /// <summary>详情区的一行：标签 + 值。值可以很长，交给 XAML 换行显示。</summary>
    public sealed record DetailLine(string Label, string Value);
}
