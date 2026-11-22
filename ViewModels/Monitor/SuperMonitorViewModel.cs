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
using WarmAsBefore.Services.Dev;

namespace WarmAsBefore.ViewModels;

/// <summary>
/// 超级监测页：论文五层架构的「活流程图」+ 日程 / 器官 / 记忆 / 数值 现状汇总。
/// 对话流程执行到哪个节点，FlowTracer 点亮该节点，Drawable 按 Glow 渐隐余辉重绘（数据驱动、非永续动画）。
/// 页面只在导航进来时构建，离开就退订事件、停掉两只钟；所有展示性读取都各自 try/catch，
/// 任何一路服务出错都只让那一路空着，不把整页带崩。
/// </summary>
public sealed partial class SuperMonitorViewModel : ObservableObject, IQueryAttributable, IWorstCaseAware
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
    // 病理特效与断链：每轮取数后重算，drawable 只读这两份。
    private readonly Dictionary<string, NodeFx> _fx = new(StringComparer.Ordinal);
    private readonly HashSet<string> _severed = new(StringComparer.Ordinal);

    public ArchitectureFlowDrawable ArchDrawable { get; } = new();

    /// <summary>流程图的自然尺寸：绑到 GraphicsView 的请求宽高上，外面套 ScrollView 滚动。
    /// 以前是硬塞进一个固定框里等比缩小，65 个节点挤成一团看不清线条，这里改成按内容给尺寸。</summary>
    [ObservableProperty] private double _canvasWidth = 1200;
    [ObservableProperty] private double _canvasHeight = 900;

    /// <summary>还没进存档：监测页只给一句提示，不去取任何数据（没存档就没人可监测）。</summary>
    [ObservableProperty] private bool _noSave;

    // ---- 向量空间：把记忆的数值向量投影到三维坐标系上，坐标与词语并排读 ----
    public Drawables.VectorCloudDrawable VectorDrawable { get; } = new();
    public ObservableCollection<VectorCloudRow> VectorRows { get; } = new();
    [ObservableProperty] private string _vectorNote = "向量空间还没读出来。";

    private Action? _vectorInvalidate;
    public void AttachVectorInvalidate(Action invalidate) => _vectorInvalidate = invalidate;

    /// <summary>回 UI 线程重画：MemoryVectorsAsync 的续体不保证在 UI 线程上，直接 Invalidate 会静默失效。</summary>
    private void InvalidateVectors() => Helpers.Ui.Post(() => _vectorInvalidate?.Invoke());

    // ---- 整页状态灯：专注 / 神游 / 即将失效，是三盏灯而不是某一格的属性 ----
    [ObservableProperty] private string _ambientTitle = "神游";
    [ObservableProperty] private string _ambientDetail = "";
    [ObservableProperty] private string _focusLampText = "专注 0%";
    [ObservableProperty] private string _wanderLampText = "神游";
    [ObservableProperty] private string _alarmLampText = "—";
    [ObservableProperty] private Color _focusLampColor = LampOff;
    [ObservableProperty] private Color _wanderLampColor = LampCold;
    [ObservableProperty] private Color _alarmLampColor = LampOff;

    private static readonly Color LampOff = Color.FromArgb("#3A3226");
    private static readonly Color LampWarm = Color.FromArgb("#FFB454");
    private static readonly Color LampCold = Color.FromArgb("#5FD3C4");
    private static readonly Color LampAlarm = Color.FromArgb("#FF5A5A");
    private static readonly Color LampAlarmDim = Color.FromArgb("#6B2A2A");

    /// <summary>点进选中模块自己的页面：图上只给一眼，细节在那页里看（含多档等级与时间线）。</summary>
    [RelayCommand]
    private async Task OpenInspector()
    {
        var id = SelectedNode?.Id;
        if (string.IsNullOrWhiteSpace(id)) return;
        try
        {
            await Shell.Current.GoToAsync($"moduleInspector?id={Uri.EscapeDataString(id)}&charId={Uri.EscapeDataString(CharId)}");
        }
        catch (Exception ex) { App.WriteLog("SuperMonitor.OpenInspector -> " + ex.Message); }
    }

    public SuperMonitorViewModel(GameEngine engine, MemoryOverviewService overview)
    {
        _engine = engine;
        _overview = overview;
        _sourceChangedHandler = OnSourceChanged;
        _flowChangedHandler = OnFlowChanged;
        // 余辉亮度直连追踪器；Drawable 只认 Func<string,float>，不反查静态类。
        ArchDrawable.GlowProvider = FlowTracer.Glow;
        // 布局算完把自然尺寸交回页面，页面据此给 GraphicsView 请求宽高，靠外层滚动而不是压缩。
        // 子系统框的标题：图上一块一块，读得出来「这一坨是记忆」「这一坨是内驱」。
        ArchDrawable.GroupTitles = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["sense"] = "感知输入",
            ["parse"] = "特征解析",
            ["memory"] = "五层记忆",
            ["cognition"] = "认知与注意",
            ["emotion"] = "情绪与病理",
            ["hormone"] = "激素与神经调制",
            ["drive"] = "内驱系统",
            ["self"] = "自我与人格",
            ["bodylow"] = "低级中枢",
            ["somatize"] = "躯体化",
            ["assembly"] = "装配与路由",
            ["output"] = "生成与输出"
        };
        // 谁挨着谁由这里定：激素在中间，其余系统围在四周，同类的挨在一起（感知↔解析、中枢↔躯体化）。
        ArchDrawable.GroupCells = new Dictionary<string, (int Col, int Row)>(StringComparer.Ordinal)
        {
            ["sense"] = (0, 0),
            ["parse"] = (1, 0),
            ["memory"] = (2, 0),
            ["cognition"] = (3, 0),
            ["emotion"] = (0, 1),
            ["hormone"] = (1, 1),
            ["drive"] = (2, 1),
            ["self"] = (3, 1),
            ["bodylow"] = (0, 2),
            ["somatize"] = (1, 2),
            ["assembly"] = (2, 2),
            ["output"] = (3, 2)
        };
        ArchDrawable.HubGroup = "hormone";
        ArchDrawable.EffectProvider = id => _fx.TryGetValue(id, out var fx) ? fx : NodeFx.None;
        ArchDrawable.SeveredProvider = link => _severed.Contains(link.From + ">" + link.To);
        ArchDrawable.ContentSizeChanged += (w, h) =>
        {
            if (w > 0 && Math.Abs(CanvasWidth - w) > 0.5) CanvasWidth = w;
            if (h > 0 && Math.Abs(CanvasHeight - h) > 0.5) CanvasHeight = h;
        };
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

    // ==================== 页面状态 ====================

    [ObservableProperty] private string _characterName = "";
    [ObservableProperty] private string _dateText = "";
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private bool _hasSelection;
    [ObservableProperty] private NodeDetail? _selectedNode;
    /// <summary>选中节点的逐字段详情（标签,值），供 XAML BindableLayout 逐行展示，能「仔细看」。</summary>
    [ObservableProperty] private ObservableCollection<DetailLine> _detailLines = new();
    [ObservableProperty] private ObservableCollection<TimelineRow> _timeline = new();
    [ObservableProperty] private ObservableCollection<OrganRow> _organs = new();
    [ObservableProperty] private ObservableCollection<LayerRow> _memoryLayers = new();
    [ObservableProperty] private ObservableCollection<StatRow> _stats = new();

    /// <summary>右栏三块列表各自有没有内容：没有就把「为什么空」写在标题下面，而不是留一片空白。</summary>
    public bool HasTimeline => Timeline.Count > 0;
    public bool HasOrgans => Organs.Count > 0;
    public bool HasLayers => MemoryLayers.Count > 0;

    partial void OnSelectedNodeChanged(NodeDetail? value)
    {
        HasSelection = value is not null;
        // 取消选中（如定时重载）时把详情清空，避免残留上一个节点的字段。
        if (value is null) DetailLines = new ObservableCollection<DetailLine>();
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
            // 整页氛围一直有呼吸：相位匀速走，每 4 个 tick（约 480ms）重画一帧。
            // 8fps 对「慢慢呼吸」够用，又不至于为了背景把画布烧满。
            ArchDrawable.AmbientPhase = (ArchDrawable.AmbientPhase + 0.055) % 1;
            _ambientSkip = (_ambientSkip + 1) % 4;
            var beat = _ambientSkip == 0;
            if (beat) RefreshAmbient();
            if (beat)
            {
                Redraw();
                InvalidateVectors();
            }
            else if (FlowTracer.LitNodes().Count > 0) Redraw();
        }
        catch { /* 装饰性刷新失败不打扰主流程 */ }
    }

    private int _ambientSkip;

    /// <summary>
    /// 读一次「她此刻是什么模式」并把整页氛围与三根状态灯定下来。
    /// 专注快撑不住（Reserve 掉到 25% 以下）时神游就要介入，这时候红灯会跟着相位闪 ——
    /// 这是失效前的提示，靠画布边缘泛红和这盏灯一起说，而不是弹个对话框。
    /// </summary>
    private void RefreshAmbient()
    {
        try
        {
            var id = CharId;
            var focused = CognitionModeService.Instance.Current(id) == CognitionMode.Focused;
            var intervene = CognitionModeService.Instance.ShouldWanderIntervene(id);
            var reserve = CognitionModeService.Instance.FocusReserve(id);
            var capacity = CognitionModeService.Instance.Capacity(id);
            var ratio = capacity > 0 ? Math.Clamp(reserve / capacity, 0, 1) : 0;

            var patho = 0.0;
            foreach (var fx in _fx.Values)
                if (fx.Pathological && fx.Severity > patho) patho = fx.Severity;

            var alarm = focused && intervene;
            ArchDrawable.AmbientMode = alarm ? 2 : focused ? 1 : 0;
            ArchDrawable.AmbientSeverity = Math.Clamp(patho, 0, 1);
            // 神游压暗、专注全亮：一眼就能看出她是在认真想还是在飘。
            ArchDrawable.AmbientOpacity = focused ? 1.0 : 0.84 - 0.10 * Math.Clamp(patho, 0, 1);

            VectorDrawable.Phase = ArchDrawable.AmbientPhase;
            var blinkOn = ArchDrawable.AmbientPhase < 0.5;
            AmbientTitle = alarm ? "专注即将失效" : focused ? "专注" : "神游";
            AmbientDetail = focused
                ? $"专属精力 {Math.Round(reserve)}/{Math.Round(capacity)}（{ratio * 100:0}%）"
                  + (alarm ? " · 神游正在敲门，随时会塞一段旧记忆进来" : " · 高精度思考，耗专属精力")
                : "低精度默认态 · 反刍、整理、纠错、回忆连线都在这里发生";

            FocusLampText = $"专注 {ratio * 100:0}%";
            WanderLampText = "神游";
            AlarmLampText = alarm ? "神游介入" : "—";
            FocusLampColor = focused ? LampWarm : LampOff;
            WanderLampColor = focused ? LampOff : LampCold;
            AlarmLampColor = alarm ? (blinkOn ? LampAlarm : LampAlarmDim) : LampOff;
        }
        catch (Exception ex) { App.WriteLog("SuperMonitor.Ambient -> " + ex.Message); }
    }

    /// <summary>
    /// 把记忆条目的数值向量投到三维坐标系上：一条记忆一个点，坐标是它向量的分块均值，
    /// 词就是它自己那句话。没有量化模型时不编坐标 —— 只把「为什么空着」写清楚。
    /// </summary>
    private async Task LoadVectorCloudAsync(
        string id, List<HumanMemoryEntry> core, List<HumanMemoryEntry> all)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(id)) return;

            if (WorstCaseProvider.IsActive)
            {
                var worst = WorstCaseFixtures.Monitor.Vectors(WorstCaseProvider.Mode);
                SetVectorCloud(worst, worst.Count == 0
                    ? "这个角色还没有可投影的记忆。"
                    : $"量化模型算出的 {worst.Count} 条记忆向量；三轴是向量三段均值，越亮的点越靠近你。");
                return;
            }

            var entries = core.Concat(all)
                .GroupBy(m => m.Id, StringComparer.Ordinal)
                .Select(g => g.First())
                .Where(m => !string.IsNullOrWhiteSpace(m.Text))
                .Take(60)
                .ToList();

            if (entries.Count == 0)
            {
                SetVectorCloud(Array.Empty<VectorPoint>(), "这个角色还没有可投影的记忆。");
                return;
            }

            var embed = ResolveService<EmbeddingService>();
            if (embed is null)
            {
                SetVectorCloud(Array.Empty<VectorPoint>(), "取不到量化服务，不显示推测坐标。");
                return;
            }

            // 第一次算向量要真去问一次模型（AiTimeoutSeconds × 最多 4 枚密钥），这期间图上不会有任何点；
            // 先把这句话写出来，免得用户对着一个空坐标系等。已经有点的重复刷新不再写，免得多余闪烁。
            if (VectorDrawable.Points.Count == 0)
                SetVectorCloud(Array.Empty<VectorPoint>(), "正在算向量，第一次可能要几十秒。");

            var vectors = await embed.MemoryVectorsAsync(id, entries);
            if (vectors.Count == 0)
            {
                SetVectorCloud(Array.Empty<VectorPoint>(),
                    "还没有向量：先配一个 OpenAI 兼容的 embedding 模型，这里才有真坐标可算。");
                return;
            }

            var points = new List<VectorPoint>(vectors.Count);
            foreach (var entry in entries)
            {
                if (!vectors.TryGetValue(entry.Id, out var v) || v.Length < 3) continue;
                // 分块均值：把向量均分成三段各取平均，得到三个能算、能复算的坐标。
                // 这不是「语义主成分」，所以界面上就照实叫「分块 Ⅰ/Ⅱ/Ⅲ」。
                var (x, y, z) = BlockMeans(v);
                var weight = Math.Clamp(0.30 + 0.55 * entry.Importance
                    + 0.10 * Math.Min(1.0, entry.RecallCount / 5.0), 0, 1);
                points.Add(new VectorPoint(entry.Id, Clip(entry.Text, 14), x, y, z, weight));
            }

            if (points.Count == 0)
            {
                SetVectorCloud(Array.Empty<VectorPoint>(), "有向量但维度不足三维，跳过了。");
                return;
            }
            SetVectorCloud(points, $"量化模型算出的 {points.Count} 条记忆向量；三轴是向量三段均值，越亮的点越靠近你。");
        }
        catch (Exception ex)
        {
            App.WriteLog("SuperMonitor.VectorCloud -> " + ex.Message);
        }
    }

    /// <summary>向量均分成三段取均值，再按全体绝对值归一到 -1..1，保证点在轴内。</summary>
    private static (double X, double Y, double Z) BlockMeans(float[] v)
    {
        var third = Math.Max(1, v.Length / 3);
        double Mean(int from, int to)
        {
            var sum = 0.0;
            var n = 0;
            for (var i = from; i < Math.Min(to, v.Length); i++) { sum += v[i]; n++; }
            return n == 0 ? 0 : sum / n;
        }
        var x = Mean(0, third);
        var y = Mean(third, third * 2);
        var z = Mean(third * 2, v.Length);
        var m = Math.Max(1e-6, Math.Max(Math.Abs(x), Math.Max(Math.Abs(y), Math.Abs(z))));
        return (Math.Clamp(x / m, -1, 1), Math.Clamp(y / m, -1, 1), Math.Clamp(z / m, -1, 1));
    }

    private static string Clip(string text, int max)
    {
        var t = (text ?? string.Empty).Replace((char)13, ' ').Replace((char)10, ' ').Trim();
        return t.Length <= max ? t : t[..max] + "…";
    }

    private void SetVectorCloud(IReadOnlyList<VectorPoint> points, string note)
    {
        VectorDrawable.Points = points;
        VectorDrawable.EmptyText = note;
        VectorNote = note;

        VectorRows.Clear();
        foreach (var p in points.OrderByDescending(p => p.Weight).Take(24))
            VectorRows.Add(new VectorCloudRow(p.Label, p.X, p.Y, p.Z));

        InvalidateVectors();
    }

    /// <summary>
    /// 从权威服务推进一次状态：Changed 由 Tick 驱动，Observe 只读不广播。
    /// 进页、定时刷新、手动刷新、Changed 后的重读都会走到这里，不需要调用方各自记得。
    /// </summary>
    private static void TickSources(string id)
    {
        if (WorstCaseProvider.IsActive) return;   // 压测档只读 fixture，不让服务 Tick 落盘
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
        try
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
        catch (Exception ex) { App.WriteLog("SuperMonitor.ScheduleRefresh -> " + ex); }
    }

    // ==================== 并行加载 ====================

    /// <summary>压测条换了档：整页重新取一次数（fixture 或正式数据，都由取数边界的开关决定）。</summary>
    public void ReloadWorstCase() => _ = LoadAsync();

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (_loading) return;
        _loading = true;
        try
        {
            // 监测只在存档里成立：没进存档时这片土地上没有「她」，取任何数都是空的。
            var scopeNow = ResolveService<MemoryScope>();
            if (scopeNow is not null && scopeNow.IsShared)
            {
                NoSave = true;
                StatusText = "还没进入存档 —— 监测读的是存档里那个人的当下，先去开一份存档。";
                return;
            }
            NoSave = false;

            var id = CharId;
            DateText = DateTime.Now.ToString("yyyy-MM-dd");
            TickSources(id);

            var organTask = Task.Run(() =>
            {
                if (WorstCaseProvider.IsActive)
                    return (IReadOnlyList<OrganState>)WorstCaseFixtures.Monitor.Organs(WorstCaseProvider.Mode);
                try { return OrganSimulationService.Instance.Observe(id); }
                catch { return (IReadOnlyList<OrganState>)Array.Empty<OrganState>(); }
            });
            var influenceTask = Task.Run(() =>
            {
                if (WorstCaseProvider.IsActive)
                    return (IReadOnlyList<OrganInfluence>)WorstCaseFixtures.Monitor.Influences(WorstCaseProvider.Mode);
                try { return OrganSimulationService.Instance.Influences(); }
                catch { return (IReadOnlyList<OrganInfluence>)Array.Empty<OrganInfluence>(); }
            });
            var planTask = Task.Run(async () =>
            {
                if (WorstCaseProvider.IsActive) return WorstCaseFixtures.Monitor.Plan(WorstCaseProvider.Mode);
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
            var driveTask = Task.Run<DriveReadout?>(async () =>
            {
                try
                {
                    var svc = ResolveService<InnerDriveService>();
                    return svc is null ? null : await svc.ReadAsync(id);
                }
                catch { return null; }
            });
            var coreTask = Task.Run<(List<HumanMemoryEntry> Core, List<HumanMemoryEntry> All)>(async () =>
            {
                if (WorstCaseProvider.IsActive)
                {
                    var all = WorstCaseFixtures.Monitor.Memories(WorstCaseProvider.Mode);
                    return (all.Where(m => m.IsCore).ToList(), all);
                }
                try
                {
                    var svc = ResolveService<HumanMemoryService>();
                    if (svc is null) return (new List<HumanMemoryEntry>(), new List<HumanMemoryEntry>());
                    return (await svc.CoreAsync(id), await svc.AllAsync(id));
                }
                catch { return (new List<HumanMemoryEntry>(), new List<HumanMemoryEntry>()); }
            });

            await Task.WhenAll(organTask, influenceTask, planTask, memoryTask, numericTask,
                mentalTask, innerTask, driveTask, coreTask);

            var organs = await organTask ?? Array.Empty<OrganState>();
            var influences = await influenceTask ?? Array.Empty<OrganInfluence>();
            var plan = await planTask;
            var layers = await memoryTask ?? new List<MemoryOverviewService.LayerStat>();
            var numeric = await numericTask;
            var mentalStates = await mentalTask ?? Array.Empty<MentalConditionState>();
            var chem = await innerTask;
            var drive = await driveTask;
            var (coreMemories, allMemories) = await coreTask;

            // ---- 图上的实时读数（角标）、病理特效与被强制切断的路径 ----
            // 角标：把这一刻的真值压到节点上，不用逐点开面板也能看出她在什么状态。
            var badges = new Dictionary<string, string>(StringComparer.Ordinal);
            if (chem is not null)
            {
                badges["emo.state"] = $"效价 {chem.Dopamine - chem.Cortisol:0.00} · 唤醒 {chem.Adrenaline:0.00}";
                badges["emo.coupling"] = $"多巴胺 {chem.Dopamine:0.00} · 血清素 {chem.Serotonin:0.00}";
            }
            if (drive is not null)
            {
                badges["core.driveSystem"] = "运行中 · 可停用";
                badges["core.drivePower"] = $"攻{drive.Aggression:0.00} 性{drive.Sexuality:0.00} 自{drive.Narcissism:0.00}";
                badges["core.driveSeven"] = $"{DriveRules.NameOf(DominantOf(drive))} {DominantValueOf(drive):0.00}";
                badges["core.driveGate"] = $"自律 {drive.Discipline:0.00} · {drive.MoralName}";
                badges["core.discharge"] = $"火气 {drive.Anger:0.00} · {drive.DischargeName}";
            }
            badges["mem.shallow"] = $"近期 {coreMemories.Count + allMemories.Count} 条在场";
            badges["mem.deep"] = $"深层 {allMemories.Count} 条";
            badges["mem.retrieval"] = coreMemories.Count > 0
                ? $"核心 {coreMemories.Count} 条可回想"
                : "按线索回想";

            // 病理特效：身上真带着的那几条，对应的节点画成呼吸的警示环（越重越急、越稳越淡）。
            _fx.Clear();
            foreach (var st in mentalStates)
            {
                var key = st.Id.ToLowerInvariant();
                var nodeId = key.Contains("ptsd") ? "path.ptsd"
                    : key.Contains("depress") ? "path.depression"
                    : key.Contains("anxi") ? "path.anxiety"
                    : key.Contains("bpd") ? "path.bpd"
                    : null;
                if (nodeId is null) continue;
                var severity = Math.Clamp(0.35 + 0.28 * st.Severity - 0.25 * st.Stability, 0.08, 1.0);
                _fx[nodeId] = new NodeFx(severity, true);
            }

            RefreshAmbient();
            _ = LoadVectorCloudAsync(id, coreMemories, allMemories);

            // 被强制切断的路径：不是「没走」，是被顶掉了——所以图上断开并打叉。
            _severed.Clear();
            if (chem is not null && chem.Cortisol > 0.68)
                _severed.Add("soma.coping>soma.reply");     // 皮质醇过顶：低级中枢越权，理智回复被切
            if (_fx.ContainsKey("path.depression"))
                _severed.Add("mem.deepen>mem.deep");        // 反刍死锁：记忆深化下不去
            if (_fx.ContainsKey("path.ptsd"))
                _severed.Add("in.tone>mind.trauma");        // 创伤路径被直接触发，绕过了常规解析

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
                        string.IsNullOrWhiteSpace(item.Title) ? "（未命名）" : item.Title,
                        $"{KindText(item.Kind)} · {item.Detail}", item.Done));
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

            // ③ 内驱动：三种基本动力、七个驱动、自律闸门与火气的当前出口。
            // 四块共用同一份结算后的快照，各自挂到自己的架构节点上；拿不到就只让这一路空着。
            try
            {
                var powerLines = new List<DetailLine>();
                var sevenLines = new List<DetailLine>();
                var gateLines = new List<DetailLine>();
                var dischargeLines = new List<DetailLine>();
                if (drive is null)
                {
                    foreach (var rows in new[] { powerLines, sevenLines, gateLines, dischargeLines })
                        rows.Add(new DetailLine("状态", "内驱动系统未开启，或这个角色还没有驱动记录"));
                }
                else
                {
                    // 这层状态跟存档走：换档就是另一副心情，所以把归属写在最前面。
                    var scopeSvc = ResolveService<MemoryScope>();
                    powerLines.Add(new DetailLine("归属存档",
                        scopeSvc is null ? "本存档" : $"{scopeSvc.Label}（{scopeSvc.Current}）"));
                    powerLines.Add(new DetailLine("数据来源", "InnerDriveService（已按半衰期结算）"));
                    powerLines.Add(new DetailLine("攻击性（向外的力）", $"{drive.Aggression:0.00} · 越高越容易把火往外放"));
                    powerLines.Add(new DetailLine("性（连接与亲密）", $"{drive.Sexuality:0.00} · 被满足时镇痛最强"));
                    powerLines.Add(new DetailLine("自恋（被看见）", $"{drive.Narcissism:0.00} · 受挫时掉得最快"));
                    powerLines.Add(new DetailLine("更新于", drive.UpdatedAt.ToLocalTime().ToString("HH:mm:ss")));

                    sevenLines.Add(new DetailLine("数据来源", "InnerDriveService 当前七股驱动的压力（0~1）"));
                    sevenLines.Add(new DetailLine("贪欲", $"{drive.Greed:0.00} · 名利 / 金钱 / 掌控感"));
                    sevenLines.Add(new DetailLine("暴食", $"{drive.Gluttony:0.00}"));
                    sevenLines.Add(new DetailLine("色欲", $"{drive.Lust:0.00}"));
                    sevenLines.Add(new DetailLine("嫉妒", $"{drive.Envy:0.00}"));
                    sevenLines.Add(new DetailLine("傲慢", $"{drive.Pride:0.00}"));
                    sevenLines.Add(new DetailLine("怠惰", $"{drive.Sloth:0.00}"));
                    sevenLines.Add(new DetailLine("暴怒", $"{drive.Wrath:0.00} · 与火气同源"));
                    sevenLines.Add(new DetailLine("越过 0.42", "会化成一句「想做点什么」进提示词"));

                    gateLines.Add(new DetailLine("自律", $"{drive.Discipline:0.00} · 闸门的力道，可被锻炼"));
                    gateLines.Add(new DetailLine("道德观", drive.MoralName));
                    gateLines.Add(new DetailLine("人生观", drive.LifeName));
                    gateLines.Add(new DetailLine("思维主义", drive.ThinkingName));
                    gateLines.Add(new DetailLine("关系安全度", $"{drive.RelationSafety:0.00} · 低于 0.30 时火不往外放，只往内消"));

                    dischargeLines.Add(new DetailLine("火气", $"{drive.Anger:0.00} · 到 0.30 要找出口，0.88 过顶硬跳最强的一档"));
                    dischargeLines.Add(new DetailLine("当前出口", drive.DischargeName));
                    dischargeLines.Add(new DetailLine("具体表现", drive.DischargeBehavior));
                    dischargeLines.Add(new DetailLine("内啡肽", $"{drive.Endorphin:0.00} · 发泄或忍住之后的那口松快"));
                    dischargeLines.Add(new DetailLine("八种出口", "叛逆 > 撒泼 > 怄气 > 任性 > 郁闷 > 拖延 > 厌烦 > 退缩"));
                }
                Remember("core.drivePower", "三种基本动力", "核心心智与病理", drive is null ? "未启用" : "监测",
                    drive is null ? "内驱动未开启"
                        : $"攻 {drive.Aggression:0.00} · 性 {drive.Sexuality:0.00} · 自恋 {drive.Narcissism:0.00}",
                    "三种基本动力是力的来源，决定她多容易被推动", powerLines);
                Remember("core.driveSeven", "七个驱动", "核心心智与病理", drive is null ? "未启用" : "监测",
                    drive is null ? "内驱动未开启"
                        : $"最强一股 {DriveRules.NameOf(DominantOf(drive))} {DominantValueOf(drive):0.00}",
                    "七股驱动决定她此刻想做什么，做成后释放多巴胺与内啡肽", sevenLines);
                Remember("core.driveGate", "自律闸门", "核心心智与病理", drive is null ? "未启用" : "监测",
                    drive is null ? "内驱动未开启"
                        : $"自律 {drive.Discipline:0.00}（{drive.MoralName} / {drive.LifeName} / {drive.ThinkingName}）",
                    "闸门不消灭驱动，只决定这次给不给出口、给哪一个出口", gateLines);
                Remember("core.discharge", "八级发泄", "核心心智与病理", drive is null ? "未启用" : "监测",
                    drive is null ? "内驱动未开启" : $"火气 {drive.Anger:0.00} · 出口「{drive.DischargeName}」",
                    "火气落到哪个出口由火气、攻击性、自律与关系安全度共同决定", dischargeLines);
            }
            catch (Exception ex) { App.WriteLog("SuperMonitor.Drive -> " + ex.Message); }

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
                new StatRow("好感", $"{numeric.Affection:N0}"),
                new StatRow("信任", $"{numeric.Trust:N0}"),
                new StatRow("精力", $"{numeric.Energy:N0}")
            });
            OnPropertyChanged(nameof(HasTimeline));
            OnPropertyChanged(nameof(HasOrgans));
            OnPropertyChanged(nameof(HasLayers));

            // 架构骨架：首进页面构造一次五层节点与 Mermaid 拓扑连线。
            EnsureArchitecture();
            // 角标随每轮取数更新：rebuild 一份带 Badge 的节点表再交给 drawable（记录式，代价很小）。
            var badgeMap = badges;
            ArchDrawable.Nodes = _archNodes?
                .Select(n => badgeMap.TryGetValue(n.Id, out var b) ? n with { Badge = b } : n)
                .ToList() ?? _archNodes;
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
        if (WorstCaseProvider.IsActive) return WorstCaseFixtures.Monitor.Layers(WorstCaseProvider.Mode);
        try { return await _overview.StatsAsync(id); }
        catch (Exception ex) { App.WriteLog("SuperMonitor.Memory -> " + ex.Message); return new(); }
    }

    private (string Name, double Affection, double Trust, double Energy) ReadNumeric(string id)
    {
        if (WorstCaseProvider.IsActive) return WorstCaseFixtures.Monitor.Numeric(WorstCaseProvider.Mode);
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

    /// <summary>面板用：此刻最强的那股驱动（暴怒与火气同源，已经算在里面）。</summary>
    private static DriveImpulse DominantOf(DriveReadout d)
        => PairsOf(d).OrderByDescending(p => p.Value).First().Kind;

    private static double DominantValueOf(DriveReadout d)
        => PairsOf(d).OrderByDescending(p => p.Value).First().Value;

    private static IEnumerable<(DriveImpulse Kind, double Value)> PairsOf(DriveReadout d) => new[]
    {
        (DriveImpulse.Greed, d.Greed), (DriveImpulse.Gluttony, d.Gluttony), (DriveImpulse.Lust, d.Lust),
        (DriveImpulse.Envy, d.Envy), (DriveImpulse.Pride, d.Pride), (DriveImpulse.Sloth, d.Sloth),
        (DriveImpulse.Wrath, d.Anger)
    };

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

    private static string KindText(object? kind) => kind?.ToString() switch
    {
        "Wear" => "穿衣",
        "Chat" => "聊天",
        "Work" => "工作",
        "Rest" => "休息",
        "Move" => "出行",
        "Eat" => "吃饭",
        "Sleep" => "睡觉",
        "Mood" => "情绪",
        "Social" => "社交",
        "Misc" => "杂事",
        _ => "事项"
    };

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
            void N(string id, string label, string layer, string module, string detail,
                   ArchNodeKind kind = ArchNodeKind.Step, string? group = null) =>
                nodes.Add(new ArchNode(id, label, layer, module, detail, kind, group));

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
            N("mem.shallow", "浅记忆", "2", "HumanMemoryService.RecallAsync", "浅记忆 / 前记忆：近期对话缓冲", group: "memory");
            N("mem.deepen", "记忆深化", "2", "HumanMemoryService", "重要性 / 回想次数 / 唤醒度 / 时间衰减共同深化", group: "memory");
            N("mem.deep", "深层记忆", "2", "HumanMemoryService", "深层记忆：情景 / 语义 / 程序三类", group: "memory");
            N("mem.episodic", "情景记忆", "2", "HumanMemoryService.RecallTextAsync", "带时间地点的经历", group: "memory");
            N("mem.semanticM", "语义记忆", "2", "HumanMemoryService", "概念与事实");
            N("mem.procedural", "程序记忆", "2", "HumanMemoryService", "习惯化的应对套路", group: "memory");
            N("mem.retrieval", "记忆检索器", "2", "HumanMemoryService.RecallTextAsync", "按窗口 / 线索回想，受情绪偏置 moodBias 影响");
            N("emo.state", "情绪状态", "2", "EmotionMix / ChatEngine", "当前情绪：效价 / 唤醒度 / 控制感三维");
            N("emo.valence", "情绪效价", "2", "EmotionMix", "Valence（-1..1）");
            N("emo.arousal", "唤醒度", "2", "EmotionMix", "Arousal（0..1）");
            N("emo.dominance", "控制感", "2", "EmotionMix", "Dominance");
            N("emo.coupling", "记忆情绪耦合", "2", "ChatEngine.SendCore", "记忆 ↔ 情绪双向耦合，moodBias 生效时点亮");
            N("mem.rumination", "反刍循环", "2", "HumanMemoryService.ChainSearchAsync", "负面回忆链延展，加剧情绪内耗");
            // v1.65 专注 / 神游双系统：多巴胺驱动的本地状态机，专注重时神游会随机塞旧记忆介入。
            N("cognition.focus", "专注模式", "2", "CognitionModeService.EnterFocus", "多巴胺驱动、大模型高精度；耗专属精力，用得越多停留越久", ArchNodeKind.Mode);
            N("cognition.wander", "神游模式", "2", "CognitionModeService.WanderInject", "低精度默认态；反刍/整理/纠错/新想法/回忆连线", ArchNodeKind.Mode);
            N("cognition.intervene", "神游介入", "2", "CognitionModeService.ShouldWanderIntervene", "专注快撑不住时，神游往脑海随机塞旧记忆");

            // ---- 层 3 核心心智与病理（14）----
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
            // v1.66 内驱动系统：整套可以整体停用（Mode），四个子系统挂在它下面（Module）。
            N("core.driveSystem", "内驱动系统", "3", "InnerDriveService", "可整体停用：关掉后数值冻结、不进提示词，也不再产驱动力", ArchNodeKind.Mode);
            N("core.drivePower", "三种基本动力", "3", "InnerDriveService", "攻击性 / 性 / 自恋：力的来源", ArchNodeKind.Module);
            N("core.driveSeven", "七个驱动", "3", "InnerDriveService", "贪欲 / 暴食 / 色欲 / 嫉妒 / 傲慢 / 怠惰 / 暴怒", ArchNodeKind.Module);
            N("core.driveGate", "自律闸门", "3", "DriveRules.ConstraintOn", "道德观 × 人生观 × 思维主义决定的压制力道", ArchNodeKind.Module);
            N("core.discharge", "八级发泄", "3", "DriveRules.PickForm", "叛逆 → 撒泼 → 怄气 → 任性 → 郁闷 → 拖延 → 厌烦 → 退缩", ArchNodeKind.Module);

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
            N("eng.output", "流式输出", "5", "ChatEngine", "SSE 流式输出给用户", ArchNodeKind.Output);
            N("eng.loop", "循环反馈", "5", "ChatEngine.SendCore", "输出回灌为下一轮输入");

            // ---- 激素与神经调制：不是又一层的节点，而是所有系统共享的通信介质 ----
            // 参考文献线索：Panksepp 的初级情绪回路是并行系统（SEEKING / RAGE / FEAR / LUST / CARE /
            // PANIC / PLAY），神经调质则通过弥散投射对多个系统做广播与慢性调制——所以激素在这张图上
            // 占据中间一块，连向其余系统，而不是挂在哪一层下面。
            N("horm.pool", "内分泌池", "H", "InnerLifeService", "六项化学值按半衰期向气质基线回落");
            N("horm.dopa", "多巴胺", "H", "InnerLifeService", "收益预期与干劲：想做、想要、有盼头");
            N("horm.sero", "血清素", "H", "InnerLifeService", "安宁与稳定：不急、不钻牛角尖");
            N("horm.cort", "皮质醇", "H", "InnerLifeService", "压力与警觉：往坏处想、绷着");
            N("horm.adr", "肾上腺素", "H", "HormoneRules", "危急：精力↑警觉↑，过量有器官代价");
            N("horm.endo", "内啡肽", "H", "InnerDriveService", "镇痛与松快：发泄过、忍下来之后的那口气");
            N("horm.chol", "胆固醇", "H", "HormoneRules", "颓废遭打击时的中和：食欲↓干劲↑");
            N("horm.ltd", "长期激励素", "H", "HormoneRules", "长期目标达成的奖励，衰减极慢");

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

            // ---- 调控线（模式 → 它调控的那几块）：虚线，不进数据流，画在流程下面 ----
            // 内驱动是整套可停的内部系统：总开关关掉，四个子系统与它对情绪的推手一起停。
            L("core.driveSystem", "core.drivePower", LinkKind.Control);
            L("core.driveSystem", "core.driveSeven", LinkKind.Control);
            L("core.driveSystem", "core.driveGate", LinkKind.Control);
            L("core.driveSystem", "core.discharge", LinkKind.Control);
            L("core.driveSystem", "emo.state", LinkKind.Control);
            // 专注 / 神游是整体模式：切换时启用的模块跟着变，不是流程里多走一步。
            L("cognition.focus", "mem.retrieval", LinkKind.Control);
            L("cognition.focus", "eng.router", LinkKind.Control);
            L("cognition.wander", "mem.deepen", LinkKind.Control);
            L("cognition.wander", "mem.episodic", LinkKind.Control);

            // 神经调质对系统的广播：不是「流过去」，是「同时影响一片」，所以一律画调控线。
            L("horm.pool", "horm.dopa", LinkKind.Bidirectional);
            L("horm.dopa", "core.drivePower", LinkKind.Control);
            L("horm.dopa", "cognition.focus", LinkKind.Control);
            L("horm.sero", "emo.state", LinkKind.Control);
            L("horm.cort", "emo.state", LinkKind.Control);
            L("horm.cort", "soma.coping", LinkKind.Control);
            L("horm.adr", "soma.fight", LinkKind.Control);
            L("horm.endo", "core.discharge", LinkKind.Control);

            // ---- 系统归属：并行子系统，不是层级 ----
            // 情绪、内驱、认知、记忆、感知、躯体、执行是同时存在的回路，激素是它们之间的介质。
            // 图上不分上下，只分「谁在哪一块、谁连谁」。
            static string SystemOf(string id) => id switch
            {
                "in.text" or "in.image" or "in.voice" or "in.fusion" or "in.embodied" => "sense",
                "mem.shallow" or "mem.deepen" or "mem.deep" or "mem.episodic" or "mem.semanticM"
                    or "mem.procedural" or "mem.retrieval" or "mem.rumination" => "memory",
                "emo.state" or "emo.valence" or "emo.arousal" or "emo.dominance" or "emo.coupling"
                    or "path.state" or "path.ptsd" or "path.depression" or "path.anxiety" or "path.bpd" => "emotion",
                "cognition.focus" or "cognition.wander" or "cognition.intervene"
                    or "mind.summary" or "mind.intrusive" => "cognition",
                "mind.trauma" or "mind.persona" or "mind.pattern" => "self",
                "core.driveSystem" or "core.drivePower" or "core.driveSeven" or "core.driveGate"
                    or "core.discharge" => "drive",
                "soma.coping" or "soma.fight" or "soma.reply" or "soma.freeze" or "soma.bypass" => "bodylow",
                "soma.response" or "soma.delay" or "soma.stutter" or "soma.downgrade"
                    or "soma.panic" or "soma.recovery" => "somatize",
                "eng.statepack" or "eng.router" or "eng.redis" or "eng.qdrant" or "eng.sqlite" => "assembly",
                var s when s.StartsWith("horm.", StringComparison.Ordinal) => "hormone",
                var s when s.StartsWith("in.", StringComparison.Ordinal) => "parse",
                _ => "output"
            };
            for (var i = 0; i < nodes.Count; i++) nodes[i] = nodes[i] with { Group = SystemOf(nodes[i].Id) };

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

/// <summary>右侧列表的一行：词语 + 它的三个坐标，读的时候不用去图上量。</summary>
public sealed record VectorCloudRow(string Word, double X, double Y, double Z)
{
    public string Coords => $"({X:0.00}, {Y:0.00}, {Z:0.00})";
}
