using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.Dispatching;
using Microsoft.Maui.Graphics;
using WarmAsBefore.Drawables;
using WarmAsBefore.DesignSystem.Worlds;
using WarmAsBefore.Models;
using WarmAsBefore.Modules.ApiManager;
using WarmAsBefore.Services;

namespace WarmAsBefore.ViewModels;

/// <summary>
/// 关系网页面：把 TA 脑子里那张网**画出来给你看它长出来**，并且能当场改、当场调参、当场让模型整理。
///
/// 三件事在这一页上是一体的：
/// · <b>看见</b>：网摊成好几片，片里近枝先伸、片间按联系的紧密挨着；看得见的不是淡入而是梢头往前长，
///   久不想起的枝会真的褪淡；屏外的点不画也不算是前提，网大了也不卡；
/// · <b>改</b>：点节点/点枝就能设等价、加联系、断开、忘掉 —— 改完立刻重长一遍，改动马上看得见；
/// · <b>调</b>：三种模型各管各的事，别混成一个东西 ——
///   <b>参数模型</b>（本地几个旋钮，决定这张网怎么算，不发网络请求）、
///   <b>量化模型</b>（把说法换算成数值，按数值找近邻、牵线、检索）、
///   <b>思考模型</b>（任何一款已配置的对话模型都能当，读网 JSON 提出整理建议，逐条采纳）。
///
/// 没有「焦点」这个概念了：网本来就是一片一片互相牵着的，硬选一个点当中心会让画面围着它转、
/// 让人误以为它最重要。页面上只有「就是它」的<b>标记</b>（从搜索/从词跳进来时钉住那个点），
/// 它只多画一圈、只用来定位，不改变网的结构。
///
/// 存的格式就是 JSON（<see cref="RelationGraph"/>），所以页面上的 JSON 视图和磁盘上的文件是同一份东西。
/// </summary>
public sealed partial class MemoryGraphViewModel : ObservableObject, IQueryAttributable
{
    private const int MaxPlacedNodes = 420;

    private readonly GameEngine _engine;
    private readonly RelationNetworkService _network;
    private readonly HumanMemoryService _human;
    private readonly SettingsManager _settings;
    private readonly ApiGateway _api;
    private readonly PageWindowService _windows;
    private readonly MemoryScope _scope;

    private string _charId = "";
    private RelationGraph _graph = new();
    private RelationParams _params = RelationParams.Default;
    private readonly Dictionary<string, string> _idByLabel = new(StringComparer.OrdinalIgnoreCase);
    private string _pendingJump = "";

    private DateTime _growStart = DateTime.UtcNow;
    private double _totalGrowMs = 1500;
    private IDispatcherTimer? _clock;
    private double _viewW, _viewH;
    private bool _busy;
    private Point _down;
    private bool _dragged;

    public MemoryGraphDrawable Drawable { get; } = new();

    public MemoryGraphViewModel(GameEngine engine, RelationNetworkService network,
        HumanMemoryService human, SettingsManager settings, ApiGateway api, PageWindowService windows,
        MemoryScope scope)
    {
        _engine = engine;
        _network = network;
        _human = human;
        _settings = settings;
        _api = api;
        _windows = windows;
        _scope = scope;
        scope.Changed += () => MainThread.BeginInvokeOnMainThread(() => ScopeNote = scope.Label);
        Drawable.AttachThumbResolver(n => AbsPathFor(n));
    }

    private Action? _invalidate;
    public void AttachInvalidate(Action invalidate) => _invalidate = invalidate;

    /// <summary> drawable 里那些异步回来的缩略图也要能叫得醒画面（地图那边漏了这一步）。</summary>
    public void AttachDrawableInvalidate() => Drawable.AttachInvalidate(() => Touch());

    private void Touch() => _invalidate?.Invoke();

    /** 页面回到前台：时钟继续，配色跟着世界。 */
    public void OnShown()
    {
        ApplyThemeColors();
        ResumeClock();
        Touch();
    }

    /// <summary>
    /// 画布自己涂底色，所以底色要先给它。
    /// 这里只是**首帧之前的兜底**：drawable 每帧会直接问世界要（切世界时页面不必重进），
    /// 取的都是同一批令牌，所以两边不可能算出两种地面。
    /// </summary>
    private void ApplyThemeColors()
    {
        Drawable.CanvasBg = StyleWorldTokens.Pick("PageBg", Color.FromArgb("#FBF3E8"));
        Drawable.Ink = StyleWorldTokens.Pick("WarmDark", Color.FromArgb("#5D4A3A"));
        Drawable.Body = StyleWorldTokens.Pick("WarmBody", Color.FromArgb("#4A3D2C"));
        Drawable.Glow = StyleWorldTokens.Pick("AccentWarm", Color.FromArgb("#F5D8B8"));
    }

    // ==================== 页面状态 ====================

    [ObservableProperty] private string _title = "关系网";
    [ObservableProperty] private string _characterName = "";
    /// <summary>独立窗口里没有「返回」这回事：那扇窗自己关掉就行，返回会去翻主窗口的导航栈。</summary>
    [ObservableProperty] private bool _showBack = true;
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private string _growthHint = "";
    [ObservableProperty] private int _nodeCount;
    [ObservableProperty] private int _edgeCount;
    [ObservableProperty] private int _netCount;
    [ObservableProperty] private int _hiddenCount;
    [ObservableProperty] private bool _hasHidden;

    [ObservableProperty] private bool _hasSelection;
    [ObservableProperty] private string _selectedLabel = "";
    [ObservableProperty] private string _selectedKind = "";
    [ObservableProperty] private string _selectedMeta = "";
    [ObservableProperty] private string _selectedDetail = "";
    [ObservableProperty] private ObservableCollection<NeighborRow> _neighbors = new();

    /// <summary>
    /// 量化模型给的近邻：数值上最接近这个说法的一批说法。
    /// 和 <see cref="Neighbors"/> 是两种东西 —— 邻居是「网里已经连上了」，这里是「活得像同一类东西」。
    /// 后者常有前者没有的，全文检索永远捞不到这一批（它们从没同框出现过）。
    /// </summary>
    [ObservableProperty] private ObservableCollection<QuantRow> _quantNear = new();

    /// <summary>近邻为空时的实话：底账没词、还是词太生僻，两种情况该做的事不一样。</summary>
    [ObservableProperty] private string _quantHint = "";

    /// <summary>这一栏在问谁：面板打开后只剩一串数字，不看这行就不知道自己刚才点了什么。</summary>
    [ObservableProperty] private string _quantSelected = "";
    [ObservableProperty] private bool _hasQuantSelected;

    /// <summary>这批数值是谁算的：量化模型（要花钱）还是本地统计（不花钱，但也只看得到同框出现过的）。</summary>
    [ObservableProperty] private string _quantBackend = "";

    /// <summary>这张网记在哪个作用域下：「这个存档里的」还是「所有存档共用的」。</summary>
    [ObservableProperty] private string _scopeNote = "";

    [ObservableProperty] private bool _linkPending;
    [ObservableProperty] private string _linkPendingText = "";
    [ObservableProperty] private string _linkFromLabel = "";

    [ObservableProperty] private bool _showJson;
    [ObservableProperty] private string _jsonText = "";

    [ObservableProperty] private bool _showParams;
    [ObservableProperty] private bool _showQuant;
    [ObservableProperty] private bool _showThink;
    [ObservableProperty] private bool _isThinking;
    [ObservableProperty] private string _thinkStatus = "";
    [ObservableProperty] private string _recallText = "";
    [ObservableProperty] private string _paramSummary = "";

    // 参数模型的七个旋钮（滑块直接绑这些）
    [ObservableProperty] private double _pSaturation = 8;
    [ObservableProperty] private double _pFan = 1.0;
    [ObservableProperty] private double _pHop = 0.55;
    [ObservableProperty] private double _pHalfLife = 60;
    [ObservableProperty] private double _pPermanent = 0.30;
    [ObservableProperty] private double _pFloor = 0.02;
    [ObservableProperty] private int _pDepth = 3;

    public ObservableCollection<string> ModelOptions { get; } = new();
    [ObservableProperty] private int _modelIndex = -1;
    private readonly List<AiEndpoint> _endpoints = new();

    public ObservableCollection<ProposalRow> Proposals { get; } = new();
    [ObservableProperty] private bool _hasProposals;

    /// <summary>联系类型的可选项（ActionSheet 与「这是什么关系」的问法都用它）。</summary>
    private static readonly string[] KindNames =
        { "等价", "联系", "属于", "导致", "先后", "在一处", "连着情绪", "相反" };

    private static readonly RelationKind[] KindValues =
    {
        RelationKind.Equivalent, RelationKind.Associated, RelationKind.BelongsTo, RelationKind.Causal,
        RelationKind.Temporal, RelationKind.Spatial, RelationKind.Emotional, RelationKind.Contrasts
    };

    // ==================== 进入 ====================

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("charId", out var v) && v is string id && !string.IsNullOrWhiteSpace(id))
            _charId = Uri.UnescapeDataString(id);
        // jump：搜索页递来的「就是哪一个点」——可以是记忆 id（节点的 Ref），也可以是节点名字。
        if (query.TryGetValue("jump", out var j) && j is string jump && !string.IsNullOrWhiteSpace(jump))
            _pendingJump = Uri.UnescapeDataString(jump);
        // 独立窗口里不放「←」：那扇窗自己关掉就行，返回只会去翻主窗口的导航栈。
        if (query.TryGetValue("inWindow", out var w) && w is string win && win == "1") ShowBack = false;
        if (string.IsNullOrWhiteSpace(_charId)) _charId = _engine.State.CharacterId;
        _ = EnterAsync();
    }

    /// <summary>先装载、再跳：点得等网长出来才找得到，顺序反了就成了「网里还没有这个说法」。</summary>
    private async Task EnterAsync()
    {
        await LoadAsync();
        var jump = _pendingJump;
        _pendingJump = "";
        if (!string.IsNullOrWhiteSpace(jump)) await JumpToAsync(jump);
    }

    public async Task LoadAsync()
    {
        if (_busy || string.IsNullOrWhiteSpace(_charId)) return;
        _busy = true;
        try
        {
            await LoadMemoryTextsAsync();
            CharacterName = _engine.Roster.TryGetValue(_charId, out var ch) ? ch.Profile.Name : "";
            Title = "关系网" + (string.IsNullOrWhiteSpace(CharacterName) ? "" : $" · {CharacterName}");
            ScopeNote = _scope.Label;
            Status = "正在长…";

            await _network.SyncFromMemoriesAsync(_charId);
            _graph = await _network.SnapshotAsync(_charId);
            _params = await _network.ParamsAsync(_charId);
            ParamSummary = _params.Summary;
            PushParamsToSliders();
            RefreshModels();

            _idByLabel.Clear();
            foreach (var n in _graph.Nodes) _idByLabel[n.Id] = n.Label;
            foreach (var n in _graph.Nodes) _idByLabel.TryAdd(n.Label, n.Id);

            NodeCount = _graph.Nodes.Count;
            EdgeCount = _graph.Edges.Count;

            if (_graph.Nodes.Count == 0)
            {
                Status = "这张网还是空的：先和 TA 聊几句，枝会自己长出来。";
                Drawable.Nodes = new List<GraphNodeView>();
                Drawable.Edges = new List<GraphEdgeView>();
                Drawable.Nets = Array.Empty<NetPlace>();
                GrowthHint = "";
                return;
            }

            await RelayoutAsync(replay: true);
            Status = $"{NodeCount} 个概念 · {EdgeCount} 条联系 · {_nets.Count} 片";
            await RefreshJsonAsync();
            await PreviewRecallAsync();
        }
        catch (Exception ex)
        {
            Status = "读网失败：" + ex.Message;
            App.WriteLog("MemoryGraph.Load -> " + ex.Message);
        }
        finally
        {
            _busy = false;
        }
    }

    // ==================== 布局与生长 ====================

    /// <summary>
    /// 「就是它」的标记点：跳过去、要从它开始联想的那个。它**不是焦点**：
    /// 画面不围着它重排，它只多画一圈、给「只看这一片」定位、给参数预览当种子。
    /// </summary>
    [ObservableProperty] private string? _markId;
    [ObservableProperty] private double _maxDepth = 3;

    partial void OnMarkIdChanged(string? value)
    {
        Drawable.MarkId = value;
        Touch();
    }

    /// <summary>只看某一片（null = 全都看）。别片收成淡淡的轮廓，不至于点错就丢上下文。</summary>
    [ObservableProperty] private string? _onlyNetId;
    [ObservableProperty] private string _onlyNetName = "";
    [ObservableProperty] private bool _hasNetFilter;

    /// <summary>片名条：每一片一个按钮，点一下只看这一片，再点回去看整张。</summary>
    [ObservableProperty] private ObservableCollection<NetChip> _netChips = new();

    private List<RelationNet> _nets = new();
    private Dictionary<string, string> _netOf = new(StringComparer.Ordinal);
    /// <summary>片名条按「大的一片在前」排；用的是布局算出来的那一版，Count 只有布局才知道。</summary>
    private List<NetPlace> _chipNets = new();

    /// <summary>
    /// 片名条重铺一遍：胶囊是 record，选中态换了只能整条重建（一片一枚，最多十几枚，不贵）。
    /// </summary>
    private void RebuildChips()
    {
        NetChips = new ObservableCollection<NetChip>(_chipNets.Select(n =>
            new NetChip(n.Id, string.IsNullOrWhiteSpace(n.Name) ? "未命名" : n.Name,
                n.Count, n.IsLoose, n.Id == OnlyNetId)));
    }

    partial void OnMaxDepthChanged(double value) => _ = RelayoutAsync(true);

    partial void OnOnlyNetIdChanged(string? value)
    {
        Drawable.OnlyNetId = value;
        OnlyNetName = _nets.FirstOrDefault(n => n.Id == value)?.Name ?? "";
        HasNetFilter = !string.IsNullOrEmpty(value);
        RebuildChips();
        // 只盯一片时那片就该铺满屏幕：缩小着看全盘是「概览」，点开一片是「细看」，两件事
        FitToContent();
        Touch();
    }

    [RelayCommand]
    private void ShowOnlyNet(NetChip? chip)
    {
        if (chip is null) return;
        OnlyNetId = OnlyNetId == chip.Id ? null : chip.Id;
        Status = OnlyNetId is null ? "回到整张网。" : $"只看「{chip.Name}」这一片，其余收成轮廓。";
    }

    [RelayCommand]
    private void ShowAllNets() => OnlyNetId = null;

    /// <summary>
    /// 重摊一次网：先用标签传播切出几片，再把每片摊成「从地标往外长的树」，
    /// 最后按片与片之间的联系强度把它们挨着摆。没有谁是焦点 —— 关系网本来就不是。
    /// </summary>
    private async Task RelayoutAsync(bool replay)
    {
        if (_graph.Nodes.Count == 0) return;
        var now = DateTime.UtcNow;

        _nets = await _network.NetsAsync(_charId);
        _netOf = RelationNets.Membership(_nets);
        var depth = Math.Clamp((int)Math.Round(MaxDepth), 1, RelationLayout.MaxDepth);
        var mesh = RelationLayout.BuildMesh(_graph.Edges, _nets, _graph.Nodes.Count, MaxPlacedNodes, depth);

        var nodeById = _graph.Nodes.ToDictionary(n => n.Id);
        var nodes = new List<GraphNodeView>(mesh.Places.Count);
        foreach (var kv in mesh.Places)
        {
            if (!nodeById.TryGetValue(kv.Key, out var src)) continue;
            var live = _params.Live(1, src.UpdatedAt, now);
            nodes.Add(new GraphNodeView
            {
                Id = src.Id,
                Label = src.Label,
                Kind = src.Kind,
                Ref = src.Ref,
                Source = src.Source,
                X = kv.Value.X,
                Y = kv.Value.Y,
                Depth = kv.Value.Depth,
                ParentId = kv.Value.ParentId,
                Salience = src.Salience,
                Live = kv.Value.Depth == 0 ? 1 : live,
                GrowAtMs = kv.Value.GrowAtMs,
                NetId = _netOf.TryGetValue(src.Id, out var nid) ? nid : "",
                Emotion = src.Kind == NodeKind.Memory ? EmotionPartsFor(src.Ref) : null
            });
        }

        var edges = new List<GraphEdgeView>(_graph.Edges.Count);
        foreach (var e in _graph.Edges)
        {
            if (!mesh.Places.TryGetValue(e.From, out var from)) continue;
            if (!mesh.Places.TryGetValue(e.To, out var to)) continue;
            var isTree = to.ParentId == e.From && to.Depth == from.Depth + 1;
            edges.Add(new GraphEdgeView
            {
                From = e.From, To = e.To, Kind = e.Kind, Weight = e.Weight,
                Source = e.Source, IsTree = isTree,
                Live = _params.Live(e.Weight, e.UpdatedAt, now)
            });
        }

        Drawable.Nodes = nodes;
        Drawable.Edges = edges;
        Drawable.Nets = mesh.Nets;
        HiddenCount = mesh.Hidden;
        HasHidden = mesh.Hidden > 0;

        var chips = _chipNets = mesh.Nets
            .OrderByDescending(n => n.Count)
            .ThenBy(n => n.Id, StringComparer.Ordinal)
            .ToList();
        NetCount = chips.Count;
        if (OnlyNetId is not null && !chips.Any(n => n.Id == OnlyNetId)) OnlyNetId = null;
        RebuildChips();

        if (replay)
        {
            // 网大的时候先出图：几百个点还按顺序一片片长，等的人会以为页面卡死了。
            // 阈值与开关都在设置里，默认 240 个点以上直接展开。
            var auto = _settings.Current;
            var skip = auto.GraphAutoSkipAnim && nodes.Count >= Math.Max(20, auto.GraphSkipAnimNodes);
            _totalGrowMs = skip ? 1 : Math.Max(600, mesh.TotalGrowMs);
            _growStart = DateTime.UtcNow;
            Drawable.Grown = skip;
            Drawable.ElapsedMs = skip ? _totalGrowMs : 0;
            GrowthHint = skip
                ? $"已直接展开 · {nodes.Count} 个概念"
                : $"正在长出 {chips.Count} 片 · 每片 {depth} 圈";
            // 跳过展开时连时钟都不开：静态出一张完整的网，也不必为呼吸效果空转。
            if (skip) StopClock(); else ResumeClock();
        }
        FitToContent();
        Touch();
    }

    /// <summary>
    /// 把当前该看的东西**整个**塞进窗口：长到屏幕外的那几圈，人只会以为「网断了」。
    /// 只看某一片时按那片的外框算，不然一片点就缩成指甲盖大。
    /// </summary>
    private void FitToContent()
    {
        if (_viewW < 40 || _viewH < 40) return;
        var nodes = Drawable.Nodes;
        if (nodes is null || nodes.Count == 0)
        {
            Drawable.Scale = 1.0;
            Drawable.OffsetX = _viewW / 2;
            Drawable.OffsetY = _viewH / 2;
            return;
        }

        double l = double.MaxValue, t = double.MaxValue, r = double.MinValue, b = double.MinValue;
        void Box(double x, double y, double hw, double hh)
        {
            if (x - hw < l) l = x - hw;
            if (x + hw > r) r = x + hw;
            if (y - hh < t) t = y - hh;
            if (y + hh > b) b = y + hh;
        }

        foreach (var n in nodes)
        {
            if (OnlyNetId is not null && n.NetId != OnlyNetId && n.Id != SelectedId) continue;
            var rad = MemoryGraphDrawable.Radius(n);
            // 记忆是张卡片、照片是个方块：包围盒按宽的那条边量，不然右侧永远被裁
            var hw = n.Kind is NodeKind.Memory or NodeKind.Image ? rad * 2.1 : rad;
            Box(n.X, n.Y, hw + 10, rad + 30);      // 下面多留 20 世界单位给挂在疙瘩下的字
        }
        if (OnlyNetId is null && Drawable.Nets is { } nets)
            foreach (var net in nets) Box(net.X, net.Y, net.Radius, net.Radius);
        if (l > r || t > b) return;

        var bw = Math.Max(80, r - l);
        var bh = Math.Max(80, b - t);
        // 0.92 是呼吸位：参数、思考、JSON 卡片浮在画布上，贴着边会被盖住
        var s = Math.Clamp(Math.Min(_viewW * 0.92 / bw, _viewH * 0.86 / bh), 0.22, 1.6);
        Drawable.Scale = s;
        Drawable.OffsetX = _viewW / 2 - (l + r) / 2 * s;
        Drawable.OffsetY = _viewH / 2 - (t + b) / 2 * s;
    }

    public void SetViewport(double w, double h)
    {
        var first = _viewW <= 0;
        var resized = Math.Abs(_viewW - w) > 1 || Math.Abs(_viewH - h) > 1;
        _viewW = w;
        _viewH = h;
        // 屏外不渲染的前提是 drawable 知道屏幕到底多大：dirtyRect 各平台给得不一样，不能只靠它
        Drawable.SetCanvasSize(w, h);
        if (first || resized) FitToContent();
        Touch();
    }

    // ==================== 生长时钟 ====================

    public void ResumeClock()
    {
        StopClock();
        _clock = Application.Current?.Dispatcher.CreateTimer();
        if (_clock is null) return;
        _clock.Interval = TimeSpan.FromMilliseconds(33);
        _clock.Tick += OnClockTick;
        _clock.Start();
    }

    public void StopClock()
    {
        if (_clock is null) return;
        _clock.Stop();
        _clock.Tick -= OnClockTick;
        _clock = null;
    }

    private int _lastHintPct = -1;

    private void OnClockTick(object? sender, EventArgs e)
    {
        var elapsed = (DateTime.UtcNow - _growStart).TotalMilliseconds;
        Drawable.ElapsedMs = elapsed;
        if (!Drawable.Grown && elapsed >= _totalGrowMs)
        {
            Drawable.Grown = true;
            _clock?.Interval = TimeSpan.FromMilliseconds(120);   // 长完只留极轻的摆动，别一直全速刷
            GrowthHint = $"长好了 · {NodeCount} 个概念";
            _lastHintPct = -1;
        }
        else if (!Drawable.Grown && _totalGrowMs > 1)
        {
            var pct = (int)Math.Clamp(elapsed / _totalGrowMs * 100, 0, 99);
            if (pct / 10 != _lastHintPct / 10)
            {
                _lastHintPct = pct;
                GrowthHint = $"正在长出来… {pct}%";
            }
        }
        Touch();
    }

    [RelayCommand]
    private void Replay()
    {
        _growStart = DateTime.UtcNow;
        Drawable.Grown = false;
        Drawable.ElapsedMs = 0;
        GrowthHint = "正在重长…";
        ResumeClock();
        Touch();
    }

    // ==================== 手势 ====================

    public void CanvasStart(PointF p)
    {
        _down = new Point(p.X, p.Y);
        _dragged = false;
    }

    public void CanvasDrag(PointF p)
    {
        var dx = p.X - _down.X;
        var dy = p.Y - _down.Y;
        if (Math.Abs(dx) + Math.Abs(dy) > 1.5) _dragged = true;
        if (!_dragged) return;
        Drawable.OffsetX += dx;
        Drawable.OffsetY += dy;
        _down = new Point(p.X, p.Y);
        Touch();
    }

    public void CanvasEnd(PointF p)
    {
        if (_dragged) return;
        OnTap(new Point(p.X, p.Y));
    }

    public void Hover(PointF p)
    {
        Drawable.Mouse = p;
        Touch();
    }

    public void Zoom(double delta, PointF screen)
    {
        var old = Drawable.Scale;
        var factor = delta > 0 ? 1.14 : 1 / 1.14;
        var next = Math.Clamp(old * factor, 0.25, 3.2);
        if (Math.Abs(next - old) < 0.001) return;
        var wx = (screen.X - Drawable.OffsetX) / old;
        var wy = (screen.Y - Drawable.OffsetY) / old;
        Drawable.OffsetX = screen.X - wx * next;
        Drawable.OffsetY = screen.Y - wy * next;
        Drawable.Scale = next;
        Touch();
    }

    [RelayCommand] private void ZoomIn() => ZoomAtCenter(1.2);
    [RelayCommand] private void ZoomOut() => ZoomAtCenter(1 / 1.2);

    private void ZoomAtCenter(double factor)
    {
        var cx = _viewW / 2;
        var cy = _viewH / 2;
        Zoom(factor > 1 ? 1 : -1, new PointF((float)cx, (float)cy));
        Touch();
    }

    [RelayCommand]
    private void FitView()
    {
        FitToContent();
        Touch();
    }

    private void OnTap(Point screen)
    {
        var world = Drawable.ScreenToWorld(screen);
        var node = Drawable.HitNode(world);
        if (node is null)
        {
            var edge = Drawable.HitEdge(world);
            if (edge is not null) { SelectEdge(edge); return; }
            if (LinkPending) CancelLink();
            else { HasSelection = false; SelectedId = null; Touch(); }
            return;
        }
        HandleNodeTap(node);
    }

    private void HandleNodeTap(GraphNodeView node)
    {
        if (LinkPending && Drawable.LinkFromId is not null && Drawable.LinkFromId != node.Id)
        {
            _ = CompleteLinkAsync(node);
            return;
        }
        SelectedId = node.Id;
        ShowNode(node);
        Touch();
    }

    [ObservableProperty] private string? _selectedId;

    /// <summary>选中态要立刻同步到画布，不能只更新卡片。</summary>
    partial void OnSelectedIdChanged(string? value)
    {
        Drawable.SelectedId = value;
        Touch();
    }

    private void ShowNode(GraphNodeView n)
    {
        _pendingEdge = null;
        HasSelection = true;
        SelectedLabel = n.Label;
        SelectedKind = Relations.NodeText(n.Kind);
        var degree = _graph.Edges.Count(e => e.From == n.Id || e.To == n.Id);
        var netName = _nets.FirstOrDefault(x => x.Id == n.NetId)?.Name;
        SelectedMeta = $"{degree} 条联系 · {SourceText(n.Source)}" +
                       (string.IsNullOrWhiteSpace(netName) ? "" : $" · 「{netName}」这一片") +
                       (n.Depth > 0 ? $" · 离片心 {n.Depth} 跳" : " · 这片的地标");
        SelectedDetail = n.Kind == NodeKind.Memory ? MemoryTextFor(n.Ref) :
            n.Kind == NodeKind.Image ? "这是一张照片式记忆。" :
            n.Kind == NodeKind.Person ? "TA 记得的一个人。" :
            n.Kind == NodeKind.Place ? "TA 记得的一个地点。" :
            n.Kind == NodeKind.Thing ? "TA 记得的一个东西。" :
            n.Kind == NodeKind.Tag ? "一个标签：句子上的 # 号落到网里就是一个点。" :
            n.Kind == NodeKind.Emotion ? "一种情绪：连着它的那枝是感受，不是事实。" :
            n.Kind == NodeKind.Text ? "一句原文。" :
            "一个词：聊天里反复出现的说法，自己长成的点。";

        var rows = new List<NeighborRow>();
        foreach (var e in _graph.Edges.Where(x => x.From == n.Id || x.To == n.Id)
                     .OrderByDescending(x => x.Weight).Take(14))
        {
            var otherId = e.From == n.Id ? e.To : e.From;
            if (!_idByLabel.TryGetValue(otherId, out var otherLabel)) continue;
            var live = _params.Live(e.Weight, e.UpdatedAt, DateTime.UtcNow);
            rows.Add(new NeighborRow(otherId, otherLabel, Relations.KindText(e.Kind), e.Weight, live));
        }
        Neighbors = new ObservableCollection<NeighborRow>(rows);
        _ = PreviewRecallAsync();
        _ = LoadQuantAsync(n.Kind == NodeKind.Memory ? MemoryTextFor(n.Ref) : n.Label);
    }

    /// <summary>
    /// 问量化模型：这个说法（或这段记得的事）在数值上最接近谁。
    /// 整句要交给库管自己去取词 —— 句子本身不在底账里，底账记的是说法。
    /// 手快点两个点时，慢的那次结果必须丢掉，不然卡片上会显示上一个点的近邻。
    /// </summary>
    private async Task LoadQuantAsync(string query)
    {
        var seq = ++_quantSeq;
        QuantNear = new ObservableCollection<QuantRow>();
        QuantSelected = "正在问：「" + Shorten(SelectedLabel) + "」";
        HasQuantSelected = true;
        QuantBackend = "";
        QuantHint = "在算数值上谁和它最像…";
        try
        {
            var hits = await _network.NearestForAsync(_charId, query, 9);
            if (seq != _quantSeq) return;
            // 每条近邻都要能追溯：钱花没花、由哪个说法算出来的，用户有权知道。
            QuantBackend = _network.QuantBackendText;
            if (hits.Count == 0)
            {
                QuantHint = "数值上没找到像的：这个说法还没攒够语境。多聊几轮再来看。";
                return;
            }
            QuantNear = new ObservableCollection<QuantRow>(hits.Select(x => new QuantRow(
                x.Term, x.Cosine, x.Npmi, x.Turns,
                // 选中一个词时种子就是它自己，再写一遍「由「雨伞」算出」是废话；只有从整句里取词才需要交代来源。
                string.Equals(x.Via, SelectedLabel, StringComparison.Ordinal) ? "" : x.Via)));
            QuantHint = "";
        }
        catch (Exception ex)
        {
            if (seq != _quantSeq) return;
            QuantHint = "算近邻失败：" + ex.Message;
            App.WriteLog("MemoryGraph.Quant -> " + ex.Message);
        }
    }

    private static string Shorten(string s) => s.Length <= 18 ? s : s[..18] + "…";

    private int _quantSeq;

    private string MemoryTextFor(string memoryId)
    {
        var hit = _memoryTexts.TryGetValue(memoryId, out var t) ? t : "";
        return string.IsNullOrWhiteSpace(hit) ? "一段记得的事。" : hit;
    }

    private readonly Dictionary<string, string> _memoryTexts = new(StringComparer.Ordinal);

    /// <summary>记忆的复合情绪配比：卡片下沿那条比例尺要它，网里的「情绪点」也用它定粗细。</summary>
    private readonly Dictionary<string, IReadOnlyList<EmotionPart>> _memoryEmotions = new(StringComparer.Ordinal);

    private IReadOnlyList<EmotionPart>? EmotionPartsFor(string memoryId) =>
        _memoryEmotions.TryGetValue(memoryId, out var mix) ? mix : null;

    private void SelectEdge(GraphEdgeView e)
    {
        var a = _idByLabel.TryGetValue(e.From, out var la) ? la : e.From;
        var b = _idByLabel.TryGetValue(e.To, out var lb) ? lb : e.To;
        SelectedId = null;                       // 选中的是这条联系，不是两端任何一个点
        HasSelection = true;
        SelectedLabel = $"{a} — {b}";
        SelectedKind = Relations.KindText(e.Kind);
        SelectedMeta = $"强度 {e.Weight:0.#} · {SourceText(e.Source)}";
        SelectedDetail = "这是一条联系本身。可以调弱它、也可以直接断开 —— 断开之后这条枝就从网里收回去了。";
        Neighbors = new ObservableCollection<NeighborRow>();
        ++_quantSeq;                        // 选中的是枝不是点：近邻列表不再属于任何东西
        QuantNear = new ObservableCollection<QuantRow>();
        QuantHint = "";
        _pendingEdge = e;
        Touch();
    }

    private GraphEdgeView? _pendingEdge;

    private static string SourceText(string s) => s switch
    {
        "human" => "你亲手改过",
        "ai" => "模型整理过",
        _ => "自己长出来的"
    };

    // ==================== 点两下就改：跳到那个点 / 建联系 ====================

    /// <summary>把画面挪到那个点（不重排：网不再围着谁长，跳过去看就够了）。</summary>
    private void PanTo(string id)
    {
        if (Drawable.Find(id) is not { } n) return;
        Drawable.OffsetX = _viewW / 2 - n.X * Drawable.Scale;
        Drawable.OffsetY = _viewH / 2 - n.Y * Drawable.Scale;
        Touch();
    }

    /// <summary>把当前选中的点钉住：画面挪到它，并只显示它那一片。</summary>
    [RelayCommand]
    private void MarkSelected()
    {
        if (SelectedId is not { } id) return;
        MarkId = id;
        PanTo(id);
        var net = _netOf.TryGetValue(id, out var nid) ? nid : null;
        if (net is not null && OnlyNetId != net) OnlyNetId = net;
    }

    /// <summary>「跳到这个说法」：网里没有就报一句，有就跳过去选中它。</summary>
    [RelayCommand]
    private async Task JumpToTermAsync()
    {
        var input = await Shell.Current.DisplayPromptAsync("跳到一个说法",
            "输入网里有的说法（词、标签、人名都行）。画面会挪过去，它那一片会亮起来。", maxLength: 20);
        var name = input?.Trim() ?? "";
        if (name.Length == 0) return;
        await JumpToAsync(name);
    }

    /// <summary>按名字跳：还没画进画面就先往外多长一圈，再挪过去。</summary>
    private async Task JumpToAsync(string name)
    {
        var id = ResolveId(name);
        if (id is null)
        {
            Status = $"网里还没有「{name}」这个说法。";
            return;
        }
        SelectedId = id;
        MarkId = id;
        if (Drawable.Find(id) is { } n) ShowNode(n); else await GrowOneRingAsync(id, name);
        PanTo(id);
        Touch();
    }

    /// <summary>
    /// 名字可能是节点 Id、Label，也可能是那条记忆的原文 id（搜索页就是把 <see cref="RelationNode.Ref"/> 递过来）。
    /// 三种都认，跳进来的人才不需要知道网内部怎么编号。
    /// </summary>
    private string? ResolveId(string name)
    {
        var hit = _graph.Nodes.FirstOrDefault(n => n.Id == name
            || n.Ref == name
            || string.Equals(n.Label, name, StringComparison.OrdinalIgnoreCase));
        return hit?.Id;
    }

    [RelayCommand]
    private async Task LinkFromSelected()
    {
        if (SelectedId is null) return;
        var pick = await Shell.Current.DisplayActionSheet("「" + SelectedLabel + "」和什么是哪种关系？",
            "取消", null, KindNames);
        if (string.IsNullOrEmpty(pick) || pick == "取消") return;
        var idx = Array.IndexOf(KindNames, pick);
        if (idx < 0) return;
        PendingKind = KindValues[idx];
        Drawable.LinkFromId = SelectedId;
        LinkFromLabel = SelectedLabel;
        LinkPending = true;
        LinkPendingText = $"再点一个点，就把它和「{SelectedLabel}」连成「{pick}」";
        Touch();
    }

    private RelationKind PendingKind = RelationKind.Associated;

    [RelayCommand]
    private void CancelLink()
    {
        Drawable.LinkFromId = null;
        LinkPending = false;
        LinkPendingText = "";
        Touch();
    }

    private async Task CompleteLinkAsync(GraphNodeView target)
    {
        var from = Drawable.LinkFromId;
        if (from is null) return;
        var origin = LinkFromLabel;
        await _network.LinkNodesAsync(_charId, from, target.Id, PendingKind, 5, "human");
        CancelLink();
        Status = $"记下了：{origin} {Relations.KindText(PendingKind)} {target.Label}";
        await ReloadQuietAsync();
    }

    [RelayCommand]
    private async Task WeakenSelectedEdge()
    {
        if (_pendingEdge is not { } e) return;
        await _network.SetEdgeWeightAsync(_charId, e.From, e.To, Math.Max(0.2, e.Weight / 3), "human");
        Status = "这条联系调弱了。";
        await ReloadQuietAsync();
    }

    [RelayCommand]
    private async Task BreakSelectedEdge()
    {
        if (_pendingEdge is not { } e)
        {
            Status = "先点一下那根枝（不是点疙瘩），再断开。";
            return;
        }
        var a = _idByLabel.TryGetValue(e.From, out var la) ? la : e.From;
        var b = _idByLabel.TryGetValue(e.To, out var lb) ? lb : e.To;
        if (!await _network.RemoveBetweenAsync(_charId, e.From, e.To)) { Status = "这条已经不在了。"; return; }
        _pendingEdge = null;
        HasSelection = false;
        Status = $"断开了：{a} — {b}";
        await ReloadQuietAsync();
    }

    [RelayCommand]
    private async Task ForgetNode()
    {
        if (SelectedId is not { } id) return;
        var confirm = await Shell.Current.DisplayAlert("从网里忘掉",
            "它身上所有的联系会一起断掉。聊到相关的事时，还会再长回来。", "忘掉", "留着");
        if (!confirm) return;
        await _network.RemoveNodeAsync(_charId, id);
        HasSelection = false;
        SelectedId = null;
        Status = $"忘掉了「{SelectedLabel}」。";
        MarkId = null;
        await LoadAsync();
    }

    [RelayCommand]
    private async Task PickNeighbor(NeighborRow? row)
    {
        if (row is null) return;
        if (!EnsurePlaced(row.NodeId)) { await GrowOneRingAsync(row.NodeId, row.Label); return; }
        SelectedId = row.NodeId;
        if (Drawable.Find(row.NodeId) is { } n) { ShowNode(n); Touch(); }
    }

    /// <summary>跳到那个近邻说法：网里已经有它就去选中它；还没有就照实说一句，下一颗钮就是为这句话准备的。</summary>
    [RelayCommand]
    private async Task JumpToQuant(QuantRow? row)
    {
        if (row is null) return;
        await JumpToAsync(row.Term);
    }

    /// <summary>
    /// 把「数值上像」落成网里真实的一条联系。
    /// 这一步是量化模型该有的出口：只会列出近邻的库管等于没在岗，词换算成数值之后必须能反过来改网。
    /// </summary>
    [RelayCommand]
    private async Task WeaveQuant(QuantRow? row)
    {
        if (row is null) return;
        var a = string.IsNullOrWhiteSpace(row.Via) ? SelectedLabel : row.Via;
        if (!await _network.WeavePairAsync(_charId, a, row.Term))
        {
            Status = $"「{a}」和「{row.Term}」本来就牵着。";
            return;
        }
        Status = $"按数值牵上了：{a} — {row.Term}（像 {row.Cosine:P0} · {_network.QuantBackendText}）";
        await ReloadQuietAsync();
        await JumpToAsync(row.Term);
    }

    /// <summary>整张网按数值再织一遍：共现看得见「同框」，这一遍补的是「同场景」，两者不重合。</summary>
    [RelayCommand]
    private async Task WeaveByNumbers()
    {
        if (_busy || string.IsNullOrWhiteSpace(_charId)) return;
        _busy = true;
        try
        {
            Status = "在按数值补线…";
            var written = await _network.WeaveAsync(_charId);
            await ReloadQuietAsync();
            var by = _network.QuantBackendText;
            Status = written == 0
                ? $"又按数值看了一遍（{by}）：该连的都连上了，没补出新线。"
                : $"按数值补了 {written} 条线 · {NodeCount} 个概念 · {EdgeCount} 条联系 · {by}";
        }
        catch (Exception ex)
        {
            Status = "补线失败：" + ex.Message;
            App.WriteLog("MemoryGraph.Weave -> " + ex.Message);
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>那个邻居还没画进来（它比当前圈数更深）：往外多长一圈，再跳过去。</summary>
    private async Task GrowOneRingAsync(string id, string label)
    {
        MarkId = id;
        SelectedId = id;
        HasSelection = true;
        SelectedLabel = label;
        if (MaxDepth >= RelationLayout.MaxDepth)
        {
            Status = $"「{label}」在这张网的最外圈之外，画面只画到 {RelationLayout.MaxDepth} 圈。";
            return;
        }
        _maxDepth = Math.Min(RelationLayout.MaxDepth, MaxDepth + 1);
        OnPropertyChanged(nameof(MaxDepth));
        Status = $"「{label}」还没画出来：往外多长一圈。";
        await RelayoutAsync(false);
        if (EnsurePlaced(id)) PanTo(id);
    }

    private bool EnsurePlaced(string id) => Drawable.Find(id) is not null;

    private async Task ReloadQuietAsync()
    {
        _graph = await _network.SnapshotAsync(_charId);
        NodeCount = _graph.Nodes.Count;
        EdgeCount = _graph.Edges.Count;
        _idByLabel.Clear();
        foreach (var n in _graph.Nodes) _idByLabel[n.Id] = n.Label;
        foreach (var n in _graph.Nodes) _idByLabel.TryAdd(n.Label, n.Id);
        if (MarkId is not null && !_graph.Nodes.Any(n => n.Id == MarkId)) MarkId = null;
        await RelayoutAsync(replay: true);
        await RefreshJsonAsync();
        await PreviewRecallAsync();
    }

    // ==================== JSON 视图 ====================

    [RelayCommand]
    private void TogglePanels(string? which)
    {
        // 三种模型各自一栏：它们干的活不同，不该塞进同一个面板让人以为是一个东西的两种叫法。
        ShowJson = which == "json" && !ShowJson;
        ShowParams = which == "params" && !ShowParams;
        ShowQuant = which == "quant" && !ShowQuant;
        ShowThink = which == "think" && !ShowThink;
        if (ShowJson && string.IsNullOrWhiteSpace(JsonText)) _ = RefreshJsonAsync();
        // 模型是刚在设置里填的：打开面板时再数一遍，不然清单一直是空的
        if (ShowThink) RefreshModels();
        if (ShowQuant && SelectedId is null)
            QuantHint = "先在网里点一个点（或一段记得的事），这里就列出数值上最接近它的一批说法。";
        Touch();
    }

    private async Task RefreshJsonAsync()
    {
        try
        {
            var raw = await _network.ToJsonAsync(_charId);
            JsonText = raw;
        }
        catch (Exception ex)
        {
            JsonText = "读 JSON 失败：" + ex.Message;
        }
    }

    // ==================== 参数模型 ====================

    private void PushParamsToSliders()
    {
        PSaturation = _params.SaturationK;
        PFan = _params.FanExponent;
        PHop = _params.HopDamping;
        PHalfLife = _params.ForgetHalfLifeDays;
        PPermanent = _params.PermanentFraction;
        PFloor = _params.ActivationFloor;
    }

    partial void OnPSaturationChanged(double value) => DraftChanged();
    partial void OnPFanChanged(double value) => DraftChanged();
    partial void OnPHopChanged(double value) => DraftChanged();
    partial void OnPHalfLifeChanged(double value) => DraftChanged();
    partial void OnPPermanentChanged(double value) => DraftChanged();
    partial void OnPFloorChanged(double value) => DraftChanged();

    private RelationParams Draft() => new()
    {
        SaturationK = PSaturation,
        FanExponent = PFan,
        HopDamping = PHop,
        ForgetHalfLifeDays = PHalfLife,
        PermanentFraction = PPermanent,
        ActivationFloor = PFloor
    };

    private bool _previewQueued;

    private void DraftChanged()
    {
        if (_graph.Nodes.Count == 0 || _busy) return;
        _params = Draft().Clamped();
        ParamSummary = _params.Summary;
        ApplyLiveToDrawing();
        Touch();
        if (_previewQueued) return;
        _previewQueued = true;
        _ = ThrottledPreviewAsync();
    }

    private async Task ThrottledPreviewAsync()
    {
        await Task.Delay(220);
        _previewQueued = false;
        await PreviewRecallAsync();
    }

    /// <summary>只重算「今天还剩几分力气」，位置不动 —— 拖滑块时画面才不会乱跳。</summary>
    private void ApplyLiveToDrawing()
    {
        if (Drawable.Nodes is not { } nodes) return;
        var now = DateTime.UtcNow;
        var byId = _graph.Nodes.ToDictionary(n => n.Id);
        Drawable.Nodes = nodes.Select(n =>
        {
            var src = byId.TryGetValue(n.Id, out var s) ? s : null;
            var live = n.Depth == 0 ? 1 : _params.Live(1, src?.UpdatedAt ?? now, now);
            return n with { Live = live };
        }).ToList();
        if (Drawable.Edges is { } edges)
        {
            var map = new Dictionary<string, DateTime>(StringComparer.Ordinal);
            foreach (var e in _graph.Edges) map[EdgeKey(e.From, e.To, e.Kind)] = e.UpdatedAt;
            Drawable.Edges = edges.Select(e =>
            {
                var at = map.TryGetValue(EdgeKey(e.From, e.To, e.Kind), out var t) ? t : DateTime.UtcNow;
                return e with { Live = _params.Live(e.Weight, at, now) };
            }).ToList();
        }
    }

    private static string EdgeKey(string a, string b, RelationKind k) =>
        string.CompareOrdinal(a, b) <= 0 ? $"{a}|{b}|{(int)k}" : $"{b}|{a}|{(int)k}";

    /// <summary>
    /// 按当前参数「想起」点什么：以选中的那个概念为种子做一次扩散。
    /// 没有焦点之后，预览的起点就只能是手上点的那个东西 —— 参数一动就能看到结果，
    /// 否则滑块调到哪算哪，根本看不出是阈值太高还是联系断了。
    /// </summary>
    private async Task PreviewRecallAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedLabel)) { RecallText = ""; return; }
        try
        {
            var hit = await _network.ExpandAsync(_charId, new[] { SelectedLabel }, 10, Math.Clamp(PDepth + 1, 1, 3));
            var outside = hit.Where(h => !string.Equals(h.Term, SelectedLabel, StringComparison.OrdinalIgnoreCase)).ToList();
            RecallText = outside.Count == 0
                ? "这样调，什么都想不起来了（阈值太高或联系都断了）。"
                : string.Join("　", outside.Select(h => $"{h.Term} {h.Score:0.00}"));
        }
        catch (Exception ex)
        {
            RecallText = "预览失败：" + ex.Message;
        }
    }

    [RelayCommand]
    private async Task SaveParams()
    {
        await _network.SaveParamsAsync(_charId, Draft());
        ParamSummary = _params.Summary;
        Status = "参数存好了：以后 TA 联想、检索记忆都按这套来。";
        await ReloadQuietAsync();
    }

    [RelayCommand]
    private async Task ResetParams()
    {
        _params = RelationParams.Default;
        PushParamsToSliders();
        ParamSummary = _params.Summary;
        await _network.SaveParamsAsync(_charId, _params);
        Status = "参数回到默认。";
        await ReloadQuietAsync();
    }

    // ==================== 思考模型 ====================

    [RelayCommand]
    private async Task RunThink()
    {
        if (_graph.Nodes.Count == 0) { ThinkStatus = "网还是空的，没什么可整理。"; return; }
        var over = ModelIndex >= 0 && ModelIndex < _endpoints.Count ? _endpoints[ModelIndex] : null;
        if (over is null || string.IsNullOrWhiteSpace(over.Key))
        {
            ThinkStatus = "还没有可用的模型：去设置里填 API Key，或在上面选一个。";
            return;
        }
        if (!await ConfirmCostAsync(over)) return;

        IsThinking = true;
        ThinkStatus = $"「{over.Model}」正在读这张网…（几十秒）";
        Proposals.Clear();
        HasProposals = false;
        try
        {
            var raw = await _api.Chat(BuildHistory(over.DeepThink), over);
            if (string.IsNullOrWhiteSpace(raw)) { ThinkStatus = "模型没回话。"; return; }
            if (raw.StartsWith('[')) { ThinkStatus = raw; return; }
            var parsed = ParseProposals(raw);
            foreach (var p in parsed) Proposals.Add(p);
            HasProposals = Proposals.Count > 0;
            ThinkStatus = HasProposals
                ? $"想了 {parsed.Count} 条建议。逐条看，采纳的那几条才落地。"
                : "这次没提出靠得住的建议（或者它回了不认识的格式）。";
            if (!HasProposals) App.WriteLog("MemoryGraph.Think raw -> " + raw[..Math.Min(200, raw.Length)]);
        }
        catch (Exception ex)
        {
            ThinkStatus = "失败：" + ex.Message;
            App.WriteLog("MemoryGraph.Think -> " + ex.Message);
        }
        finally
        {
            IsThinking = false;
        }
    }

    private async Task<bool> ConfirmCostAsync(AiEndpoint over)
    {
        var label = over.DeepThink ? $"{over.Model}（会想）" : over.Model;
        return await Shell.Current.DisplayAlert("要花模型的钱",
            $"用「{label}」读一遍网并出建议。网络请求会算进你的额度。", "跑", "先不");
    }

    private List<ChatMessage> BuildHistory(bool deep)
    {
        var s = _settings.Current;
        var system =
            "你是记忆整理器。给你的是一个 AI 角色脑子里那张「关系网」的 JSON，和 TA 记得的事的原文。\n" +
            "nodes 里 kind：word 词、person 人、place 地点、thing 东西、tag 标签、memory 一段记忆、image 照片、emotion 情绪；\n" +
            "edges 里 kind：equivalent 等价（同一个东西的两个名字）、associated 一般联想、belongs 归属、causal 因果、" +
            "temporal 先后、spatial 同处一地、emotional 连着情绪、contrasts 相反；weight 是强度，source 是来历。\n" +
            "只提**改这张网**的建议，规则按人类记忆的样子来：\n" +
            "1) equivalent：两个名字在任何句子里都指同一个东西（老妈/妈妈、你/用户），该焊成一个概念；\n" +
            "2) 缺的隐含联系：原文里明显一起发生、或有因果/时间/空间/情绪关系，可网里没这条边；\n" +
            "3) unlink：一眼就是共现噪音的边（两个不相关的东西只因同批出现就连上了），该断；\n" +
            "4) weaken：方向对但权重虚高（其实是同一句话重复说了几遍）；\n" +
            "5) strengthen：人工/AI 定过但其实很牢的联系，可以给回强度。\n" +
            "不要提加新记忆、不要改写原文、不要评价人。最多 20 条，按把握从高到低排。\n" +
            "只输出 JSON 数组，别的一个字都不要多说：\n" +
            "[{\"op\":\"equivalent\",\"a\":\"老妈\",\"b\":\"妈妈\",\"weight\":8,\"why\":\"同一人，两批句子里都指妈妈\",\"confidence\":0.9}]";

        var graphJson = CompactGraphJson();
        var memories = MemoryDigest();
        var user = $"关系网 JSON：\n{graphJson}\n\nTA 记得的事：\n{memories}\n\n现在给出建议数组。";

        var list = new List<ChatMessage>
        {
            new() { Role = "system", Content = system },
            new() { Role = "user", Content = user }
        };
        if (deep || s.DeepThink)
            list.Add(new ChatMessage { Role = "user", Content = "先想清楚哪些是真同义、哪些只是碰巧同批出现，再输出 JSON 数组。" });
        return list;
    }

    private string CompactGraphJson()
    {
        var sb = new StringBuilder();
        sb.Append("{\"nodes\":[");
        var first = true;
        foreach (var n in _graph.Nodes.OrderByDescending(x => x.Salience).Take(160))
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append("{\"label\":").Append(JsonSerializer.Serialize(n.Label))
              .Append(",\"kind\":\"").Append(n.Kind.ToString().ToLowerInvariant())
              .Append("\",\"salience\":").Append(n.Salience.ToString("0.###"))
              .Append(",\"source\":\"").Append(n.Source).Append("\"}");
        }
        sb.Append("],\"edges\":[");
        first = true;
        foreach (var e in _graph.Edges.OrderByDescending(x => x.Weight).Take(200))
        {
            if (!_idByLabel.TryGetValue(e.From, out var a)) continue;
            if (!_idByLabel.TryGetValue(e.To, out var b)) continue;
            if (!first) sb.Append(',');
            first = false;
            sb.Append("{\"a\":").Append(JsonSerializer.Serialize(a))
              .Append(",\"b\":").Append(JsonSerializer.Serialize(b))
              .Append(",\"kind\":\"").Append(e.Kind.ToString().ToLowerInvariant())
              .Append("\",\"weight\":").Append(e.Weight.ToString("0.##"))
              .Append(",\"source\":\"").Append(e.Source).Append("\"}");
        }
        sb.Append("]}");
        var text = sb.ToString();
        return text.Length > 14000 ? text[..14000] + "…" : text;
    }

    private string MemoryDigest()
    {
        var lines = _memoryTexts.Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
            .Select(kv => kv.Value)
            .Take(40);
        var text = string.Join("\n", lines);
        return string.IsNullOrWhiteSpace(text) ? "（还没读到原文，只按网的结构整理）"
            : text.Length > 6000 ? text[..6000] : text;
    }

    private static List<ProposalRow> ParseProposals(string raw)
    {
        var list = new List<ProposalRow>();
        var start = raw.IndexOf('[');
        var end = raw.LastIndexOf(']');
        if (start < 0 || end <= start) return list;
        try
        {
            using var doc = JsonDocument.Parse(raw[start..(end + 1)]);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return list;
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.Object) continue;
                var op = Str(el, "op");
                var a = Str(el, "a");
                var b = Str(el, "b");
                if (op.Length == 0 || a.Length == 0 || b.Length == 0) continue;
                if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) continue;
                var weight = Num(el, "weight", 5);
                var why = Str(el, "why");
                var conf = Math.Clamp(Num(el, "confidence", 0.6), 0, 1);
                var kind = KindFromOp(op);
                if (kind is null) continue;
                list.Add(new ProposalRow
                {
                    Op = op.ToLowerInvariant(),
                    Kind = kind.Value,
                    A = a,
                    B = b,
                    Weight = Math.Clamp(weight, 0.1, 10),
                    Why = why,
                    Confidence = conf
                });
                if (list.Count >= 20) break;
            }
        }
        catch (Exception ex)
        {
            App.WriteLog("MemoryGraph.Parse -> " + ex.Message);
        }
        return list.OrderByDescending(p => p.Confidence).ToList();
    }

    private static string Str(JsonElement el, string key) =>
        el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()?.Trim() ?? "" : "";

    private static double Num(JsonElement el, string key, double fallback)
    {
        if (!el.TryGetProperty(key, out var v)) return fallback;
        if (v.ValueKind == JsonValueKind.Number) return v.GetDouble();
        if (v.ValueKind == JsonValueKind.String && double.TryParse(v.GetString(), out var d)) return d;
        return fallback;
    }

    private static RelationKind? KindFromOp(string op) => op.ToLowerInvariant() switch
    {
        "equivalent" or "merge" or "merge_nodes" or "same" => RelationKind.Equivalent,
        "associate" or "association" or "related" or "link" => RelationKind.Associated,
        "belongs" or "belongsto" or "category" => RelationKind.BelongsTo,
        "causal" or "cause" or "causes" => RelationKind.Causal,
        "temporal" or "before" or "then" => RelationKind.Temporal,
        "spatial" or "place" or "together" => RelationKind.Spatial,
        "emotional" or "emotion" or "feeling" => RelationKind.Emotional,
        "contrasts" or "contrast" or "opposite" => RelationKind.Contrasts,
        "weaken" or "strengthen" => RelationKind.Associated,
        "unlink" or "forget" or "remove" => null,
        _ => null
    };

    [RelayCommand]
    private async Task Apply(ProposalRow? row)
    {
        if (row is null || row.State != 0) return;
        var a = ResolveId(row.A);
        var b = ResolveId(row.B);
        // 模型给的新词网里还没有：LinkAsync 按名字建 word 节点，正好把新联系长出来
        if (a is null || b is null)
        {
            if (row.Op is "unlink" or "forget") { row.Mark("网里本来就没这条，不用断"); return; }
            if (row.Op is "weaken" or "strengthen") { row.Mark("网里还没有这两个点，跳过"); return; }
            await _network.LinkAsync(_charId, row.A, row.B, row.Weight, "ai", row.Kind);
            row.Mark("已长出来（新词）");
            await ReloadQuietAsync();
            return;
        }
        switch (row.Op)
        {
            case "unlink":
            case "forget":
                row.Mark(await _network.RemoveBetweenAsync(_charId, a, b) ? "已断开" : "本来就没有这条");
                break;
            case "weaken":
                await _network.SetEdgeWeightAsync(_charId, a, b, Math.Max(0.2, row.Weight * 0.35), "ai");
                row.Mark("已调弱");
                break;
            case "strengthen":
                await _network.SetEdgeWeightAsync(_charId, a, b, Math.Min(10, row.Weight), "ai");
                row.Mark("已加强");
                break;
            default:
                await _network.LinkNodesAsync(_charId, a, b, row.Kind, row.Weight, "ai");
                row.Mark($"已记为「{Relations.KindText(row.Kind)}」");
                break;
        }
        await ReloadQuietAsync();
    }

    [RelayCommand]
    private void Ignore(ProposalRow? row)
    {
        if (row is null) return;
        row.Mark("忽略");
        Touch();
    }

    [RelayCommand]
    private async Task ApplyAll()
    {
        var pending = Proposals.Where(p => p.State == 0 && p.Confidence >= 0.7).ToList();
        if (pending.Count == 0) { ThinkStatus = "没有把握 ≥0.7 的建议可一次性采纳。"; return; }
        foreach (var p in pending) await ApplyAsyncSafe(p);
        ThinkStatus = $"一次采纳了 {pending.Count} 条。";
        await ReloadQuietAsync();
    }

    private async Task ApplyAsyncSafe(ProposalRow p)
    {
        try { await Apply(p); }
        catch (Exception ex) { p.Mark("失败：" + ex.Message); }
    }

    [RelayCommand]
    private void ClearProposals()
    {
        Proposals.Clear();
        HasProposals = false;
        ThinkStatus = "";
    }

    // ==================== 模型清单 ====================

    private void RefreshModels()
    {
        var s = _settings.Current;
        ModelOptions.Clear();
        _endpoints.Clear();

        // 思考模型不是「只有深度模型才能当」：任何一款对话模型都能读这张网，
        // 区别只在要不要先想一步。所以这里列全部已配置的模型，深度只是加一句提示、放宽一点额度。
        void Add(string label, string url, IReadOnlyList<string> keys, string model, bool deep, int? timeout)
        {
            if (string.IsNullOrWhiteSpace(url) || keys.Count == 0 || string.IsNullOrWhiteSpace(model)) return;
            var shown = label;
            for (var dup = 2; ModelOptions.Contains(shown); dup++) shown = label + " (" + dup + ")";
            ModelOptions.Add(shown);
            _endpoints.Add(new AiEndpoint
            {
                Url = url.Trim(),
                Key = keys[0],
                KeyPool = keys,
                Model = model.Trim(),
                Provider = deep ? "思考模型（会想一步）" : "思考模型",
                Temperature = deep ? 0.3 : 0.5,
                // 读一张网要输出的建议比一轮对话长得多；额度按设置给，但下限得兜住，
                // 否则截在半路 JSON 解析不了，人只看到「它回了不认识的格式」。
                MaxTokens = Math.Max(deep ? 2400 : 1600, s.AiMaxTokens),
                DeepThink = deep,
                TimeoutSeconds = timeout is > 0 ? timeout : deep ? 150 : 90
            });
        }

        var mainKeys = new[] { s.AiKey }.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
        Add($"对话 · {s.AiModel}", s.AiUrl, mainKeys, s.AiModel, false, null);
        if (!string.IsNullOrWhiteSpace(s.DeepModel))
            Add($"深度 · {s.DeepModel}", s.AiUrl, mainKeys, s.DeepModel, true, null);

        foreach (var p in s.AiProviders.Where(x => x.Enabled && !string.IsNullOrWhiteSpace(x.Url)))
        {
            var keys = (p.Keys ?? new List<string>()).Where(k => !string.IsNullOrWhiteSpace(k)).ToList();
            if (keys.Count == 0) keys = mainKeys;
            foreach (var m in p.Models.Where(m => m.Enabled && !string.IsNullOrWhiteSpace(m.Model)))
            {
                var name = string.IsNullOrWhiteSpace(m.DisplayName) ? m.Model : m.DisplayName;
                // 名字里带 r1/think/reason 的确实更可能自己会想，但那只当提示，不当门槛。
                Add($"{p.Name} · {name}{(LooksDeep(m.Model) ? "（会想）" : "")}", p.Url, keys, m.Model,
                    LooksDeep(m.Model), p.TimeoutSeconds);
            }
        }

        ModelIndex = _endpoints.Count == 0 ? -1 : 0;
        ThinkStatus = _endpoints.Count == 0
            ? "还没有可选的思考模型：设置 → AI 里填端点、密钥和模型名。"
            : $"可以选 {_endpoints.Count} 个模型。任何一款都能读这张网，标「会想」的会先想一步再答。";
    }

    /// <summary>从模型名猜它会不会先想一步 —— 只用来加一句提示，不做筛选。</summary>
    private static bool LooksDeep(string model) =>
        model.Contains("r1", StringComparison.OrdinalIgnoreCase) ||
        model.Contains("think", StringComparison.OrdinalIgnoreCase) ||
        model.Contains("reason", StringComparison.OrdinalIgnoreCase) ||
        model.StartsWith("o1", StringComparison.OrdinalIgnoreCase) ||
        model.StartsWith("o3", StringComparison.OrdinalIgnoreCase);

    // ==================== 杂项 ====================

    private string? AbsPathFor(GraphNodeView n)
    {
        if (n.Kind != NodeKind.Image || string.IsNullOrWhiteSpace(n.Ref)) return null;
        var p = n.Ref;
        var abs = Path.IsPathRooted(p) ? p : Path.Combine(App.RootDirectory, p);
        return File.Exists(abs) ? abs : null;
    }

    [RelayCommand]
    private async Task Back() => await Shell.Current.GoToAsync("..");

    /// <summary>
    /// 搜事件另开一扇：搜和看网是两件事，挤在同一页里来回切等于什么都没打开。
    /// 手机上没有多窗口，<see cref="PageWindowService"/> 会退回页内导航。
    /// </summary>
    [RelayCommand]
    private async Task OpenSearch() =>
        await _windows.OpenOrNavigateAsync(PageWindowKeys.Search, "搜事件", typeof(Views.MemorySearchPage),
            PageWindowKeys.Search, new Dictionary<string, object> { ["charId"] = _charId });

    [RelayCommand]
    private async Task Resync()
    {
        Status = "正在把最近记得的事摊进网里…";
        await _network.SyncFromMemoriesAsync(_charId);
        await LoadAsync();
    }

    /// <summary>记忆原文要在建网之前拿到，思考模型才有东西可读。</summary>
    private async Task LoadMemoryTextsAsync()
    {
        try
        {
            var entries = await _human.AllAsync(_charId);
            _memoryTexts.Clear();
            _memoryEmotions.Clear();
            foreach (var e in entries)
            {
                _memoryTexts[e.Id] = e.Text;
                if (e.Emotion is { IsEmpty: false } mix && mix.Parts.Count > 0)
                    _memoryEmotions[e.Id] = mix.Parts;
            }
        }
        catch (Exception ex)
        {
            App.WriteLog("MemoryGraph.Texts -> " + ex.Message);
        }
    }

    public sealed record NeighborRow(string NodeId, string Label, string Relation, double Weight, double Live)
    {
        public string Text => Label;
        public string Meta => $"{Relation} · {Weight:0.#}" + (Live < 0.98 ? $" · 还剩 {Live:P0}" : "");
    }

    /// <summary>
    /// 量化模型给的一条近邻：这个说法换算成数值之后，谁和它最接近。
    /// <paramref name="Via"/> 是问它时用的那个种子（选中一条记忆时是从句里取出的词，不是整句）。
    /// <paramref name="Npmi"/> 单独摆出来是有用处的：共现高、nPMI 低 = 只是常见词碰巧同框，不是真的有关。
    /// </summary>
    public sealed record QuantRow(string Term, double Cosine, double Npmi, int Turns, string Via)
    {
        public string Text => Term;

        /// <summary>选中一条记忆时问的其实是句里取出的词 —— 不写出来，人不知道这个近邻是从哪儿冒出来的。</summary>
        public string Meta => $"像 {Cosine:P0} · {Turns} 轮" +
                              (string.IsNullOrWhiteSpace(Via) ? "" : $" · 由「{Via}」算出");
    }

    /// <summary>
    /// 片名条上的一颗胶囊：片名由这片最中心的说法决定，不用人再取一遍。
    /// 底色和墨色一起带出来，「只看这一片」才看得出来哪枚是按下去的那枚。
    /// </summary>
    public sealed record NetChip(string Id, string Name, int Count, bool IsLoose, bool Active)
    {
        public string Text => $"{Name} {Count}";

        // 按下去的那枚是「强调面」，配给它的那份墨（AccentWarmFg）才是有保证的一对：
        // 世界把这一对锁在 5:1 以上，浅档往黑沉、深档往白提，方向不会写反。
        public Color Fill => Active ? Res("AccentWarm", "#F5D8B8") : Res("InputBg", "#FFFBF3");
        public Color Ink => Active ? Res("AccentWarmFg", "#5D4A3A") : Res("WarmBody", "#4A3D2C");

        private static Color Res(string key, string fallback) =>
            StyleWorldTokens.Pick(key, Color.FromArgb(fallback));
    }

    public sealed partial class ProposalRow : ObservableObject
    {
        public string Op { get; init; } = "associate";
        public RelationKind Kind { get; init; } = RelationKind.Associated;
        public string A { get; init; } = "";
        public string B { get; init; } = "";
        public double Weight { get; init; } = 5;
        public double Confidence { get; init; } = 0.6;
        public string Why { get; init; } = "";

        public string Head => $"{OpText}｜{A} → {B}" + (Weight > 0 ? $"（{Weight:0.#}）" : "");
        public string Reason => string.IsNullOrWhiteSpace(Why) ? "模型没给理由" : Why;
        public string ConfText => $"把握 {Confidence:P0}";

        [ObservableProperty] private int _state;
        [ObservableProperty] private string _result = "";

        private string OpText => Op switch
        {
            "equivalent" or "merge" or "merge_nodes" or "same" => "焊成一个概念（等价）",
            "unlink" or "forget" => "该忘：断掉",
            "weaken" => "调弱",
            "strengthen" => "加强",
            _ => "补一条" + Relations.KindText(Kind)
        };

        public void Mark(string text)
        {
            Result = text;
            State = 1;
        }
    }
}
