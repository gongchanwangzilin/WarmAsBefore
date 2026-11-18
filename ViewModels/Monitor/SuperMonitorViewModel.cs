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

namespace WarmAsBefore.ViewModels;

/// <summary>
/// 超级监测页：日程 / 器官 / 记忆 / 数值 四路现状汇总到一页，点流程图的节点展开说明。
/// 页面只在导航进来时构建，离开就退订事件、停掉刷新钟；所有展示性读取都各自 try/catch，
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
    private IDispatcherTimer? _clock;

    private readonly Dictionary<string, NodeDetail> _details = new(StringComparer.Ordinal);
    // 每个节点的「仔细看」字段：LoadAsync 时连同原始领域对象一起组装好，选中时直接取，不再回读服务。
    private readonly Dictionary<string, IReadOnlyList<DetailLine>> _detailLinesById = new(StringComparer.Ordinal);
    private readonly Action<string> _sourceChangedHandler;

    public MonitorFlowDrawable Drawable { get; } = new();

    public SuperMonitorViewModel(GameEngine engine, MemoryOverviewService overview)
    {
        _engine = engine;
        _overview = overview;
        _sourceChangedHandler = OnSourceChanged;
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
            _charId = Uri.UnescapeDataString(id);
        _ = LoadAsync();
    }

    // ==================== 生命周期 ====================

    private Action? _invalidate;
    public void AttachInvalidate(Action invalidate) => _invalidate = invalidate;

    private void Redraw()
    {
        if (!_pageVisible || _invalidate is null) return;
        try { MainThread.BeginInvokeOnMainThread(_invalidate); }
        catch (Exception ex) { App.WriteLog("SuperMonitor.Redraw -> " + ex.Message); }
    }

    public void SetViewport(double w, double h)
    {
        Drawable.SetCanvasSize(w, h);
        Redraw();
    }

    public void StartClock()
    {
        _pageVisible = true;
        HookSourceEvents();
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
        UnhookSourceEvents();
    }

    private void OnClockTick(object? sender, EventArgs e)
    {
        if (_pageVisible && !_loading) _ = LoadAsync();
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

    // ==================== 事件订阅：两个 Changed 都是 Action<string>（参数 charId） ====================

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

            await Task.WhenAll(organTask, influenceTask, planTask, memoryTask, numericTask);

            var organs = organTask.Result ?? Array.Empty<OrganState>();
            var influences = influenceTask.Result ?? Array.Empty<OrganInfluence>();
            var plan = planTask.Result;
            var layers = memoryTask.Result ?? new List<MemoryOverviewService.LayerStat>();
            var numeric = numericTask.Result;

            if (!string.IsNullOrWhiteSpace(numeric.Name)) CharacterName = numeric.Name;
            else CharacterName = _engine.Roster.TryGetValue(id, out var rosterCh) ? rosterCh.Profile.Name : id;

            var timelineRows = new List<TimelineRow>();
            var flowNodes = new List<MonitorFlowNode>();
            var flowLinks = new List<MonitorFlowLink>();
            _details.Clear();
            _detailLinesById.Clear();

            if (plan is not null && plan.Items is { Count: > 0 } items)
            {
                foreach (var item in items.OrderBy(i => i.Hour).ThenBy(i => i.Minute))
                {
                    var time = item.TimeText;
                    timelineRows.Add(new TimelineRow(item.Id, time, item.Done ? "✓" : "○",
                        item.Title, $"{KindText(item.Kind)} · {item.Detail}", item.Done));
                    AddNode(flowNodes, $"plan-{item.Id}", item.Title, "日程",
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
                AddNode(flowNodes, key, o.Name, "器官", o.Status.ToString(),
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
                AddNode(flowNodes, $"mem-{layer.Layer}", layer.Name, "记忆", "",
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

            AddNode(flowNodes, "stat-affection", $"好感 {numeric.Affection:0}", "数值", "Active",
                "关系亲近程度", "好感越高，越愿意主动靠近你",
                new[]
                {
                    new DetailLine("数值", $"{numeric.Affection:0.##}"),
                    new DetailLine("说明", "关系亲近程度"),
                    new DetailLine("影响", "好感越高，越愿意主动靠近你"),
                });
            AddNode(flowNodes, "stat-trust", $"信任 {numeric.Trust:0}", "数值", "Active",
                "愿意把软弱露出来的程度", "信任够高才会说出真心话",
                new[]
                {
                    new DetailLine("数值", $"{numeric.Trust:0.##}"),
                    new DetailLine("说明", "愿意把软弱露出来的程度"),
                    new DetailLine("影响", "信任够高才会说出真心话"),
                });
            AddNode(flowNodes, "stat-energy", $"精力 {numeric.Energy:0}", "数值", "Active",
                "今天还剩多少力气", "精力低时不想动、不想说话",
                new[]
                {
                    new DetailLine("数值", $"{numeric.Energy:0.##}"),
                    new DetailLine("说明", "今天还剩多少力气"),
                    new DetailLine("影响", "精力低时不想动、不想说话"),
                });

            // 四组各取第一枚连一条示意线，告诉用户「这些是同一时刻的同一件事」。
            LinkGroups(flowNodes, flowLinks, "日程", "器官");
            LinkGroups(flowNodes, flowLinks, "器官", "记忆");
            LinkGroups(flowNodes, flowLinks, "记忆", "数值");

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

            Drawable.Nodes = flowNodes;
            Drawable.Links = flowLinks;
            Drawable.SelectedId = null;
            SelectedNode = null;
            StatusText = $"日程 {timelineRows.Count} · 器官 {organs.Count} · 记忆 {layers.Count} · 数值 3";

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

    private void AddNode(List<MonitorFlowNode> nodes, string id, string label, string group,
        string status, string detail, string influence, IReadOnlyList<DetailLine>? lines = null)
    {
        nodes.Add(new MonitorFlowNode { Id = id, Label = label, Group = group, Status = status, Detail = detail });
        _details[id] = new NodeDetail(id, label, group, status, detail, influence);
        if (lines is not null) _detailLinesById[id] = lines;
    }

    private static void LinkGroups(List<MonitorFlowNode> nodes, List<MonitorFlowLink> links, string from, string to)
    {
        var a = nodes.FirstOrDefault(n => n.Group == from);
        var b = nodes.FirstOrDefault(n => n.Group == to);
        if (a is not null && b is not null) links.Add(new MonitorFlowLink { From = a.Id, To = b.Id });
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
        var id = parameter as string;
        if (string.IsNullOrEmpty(id))
        {
            // 画布点击走同一条路：把坐标交给 drawable 的命中测试。
            if (parameter is PointF p) id = Drawable.HitTest(p.X, p.Y);
        }
        if (string.IsNullOrEmpty(id) || !_details.TryGetValue(id, out var detail)) return;
        SelectedNode = detail;
        // 按节点类型取出当初随原始对象一起组装好的逐字段详情；取不到就留空，不让装饰性详情崩页。
        try
        {
            DetailLines = new ObservableCollection<DetailLine>(
                _detailLinesById.TryGetValue(id, out var lines) ? lines : Array.Empty<DetailLine>());
        }
        catch (Exception ex) { App.WriteLog("SuperMonitor.DetailLines -> " + ex.Message); }
        Drawable.SelectedId = id;
        Redraw();
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

    // ==================== 给 XAML 绑定的行模型 ====================

    public sealed record TimelineRow(string Id, string Time, string Mark, string Title, string Detail, bool IsDone);
    public sealed record OrganRow(string Name, string Status, string Value, string Symptoms, string Influence, string UpdatedAt);
    public sealed record LayerRow(string Name, string Count, string Detail);
    public sealed record StatRow(string Name, string Value);
    public sealed record NodeDetail(string Id, string Title, string Group, string Status, string Detail, string Influence);
    /// <summary>详情区的一行：标签 + 值。值可以很长，交给 XAML 换行显示。</summary>
    public sealed record DetailLine(string Label, string Value);
}
