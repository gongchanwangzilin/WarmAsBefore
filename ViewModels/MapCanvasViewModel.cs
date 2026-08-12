using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WarmAsBefore.Drawables;
using WarmAsBefore.Models;
using WarmAsBefore.Services;

namespace WarmAsBefore.ViewModels;

/// <summary>
/// 地图画布编辑器 v2：
/// - 无限画布（平移/缩放），三种模式：浏览(点卡出发)/编辑(拖卡/右键菜单)/连线(双端点+折点)
/// - 临时节点（Transit）端点、直线/曲线、长度（自动/手动/0=虚线）、备注卡、右键上下文菜单
/// </summary>
public sealed partial class MapViewModel : ObservableObject, IDisposable
{
    public enum ModeKind { View, Edit, Link, Doodle }

    private readonly GameEngine _engine;
    private readonly MapService _maps;
    public MapCanvasDrawable Drawable { get; } = new();

    [ObservableProperty] private ModeKind _mode = ModeKind.View;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private ObservableCollection<MapCanvasNode> _nodes = new();
    [ObservableProperty] private MapCanvasNode? _selectedNode;

    public bool HasSelection => SelectedNode is not null;
    public bool IsEditMode => Mode == ModeKind.Edit;
    public bool IsLinkMode => Mode == ModeKind.Link;
    public bool IsDoodleMode => Mode == ModeKind.Doodle;
    public bool IsViewMode => Mode == ModeKind.View;
    public string SelectedNodeLabel => SelectedNode is null ? "" : $"{SelectedNode.LocationName} · {SelectedNode.SceneName}";
    public string ViewModeLabel => Mode == ModeKind.View ? "● 浏览" : "浏览";
    public string EditModeLabel => Mode == ModeKind.Edit ? "● 编辑" : "编辑";
    public string LinkModeLabel => Mode == ModeKind.Link ? "● 连线" : "连线";
    public string DoodleModeLabel => Mode == ModeKind.Doodle ? "● 涂鸦" : "涂鸦";
    public string LocationLabel { get; private set; } = "";

    private Action? _invalidate;
    private bool _panning;
    private PointF _panLastScreen;
    private MapCanvasNode? _dragCard;
    private MapTransitNode? _dragTransit;
    private MapEdge? _dragEdge;
    private string? _dragLocId;  // 地点框拖拽：空/整框可整体移动（含全部后代卡片）
    private int _dragWaypoint = -1;
    private Point _dragLastWorld;
    private Point _menuWorld;   // 右键新建时菜单的世界坐标
    private bool _autoFitDone;
    private DateTime _menuOpenedAt = DateTime.MinValue;   // 右键打开菜单的时间：抑制同动作的 Touch 回声

    /// <summary>WinUI 右键按压时间戳（页面在窗口 PointerPressed 中同步标记）。</summary>
    public long LastWinPointerTick { get; private set; }

    /// <summary>同步记录右键时间戳并废除本次按压可能引起的框选/点击分支（回声会被当作用户操作误处理）。</summary>
    public void MarkRightPress()
    {
        LastWinPointerTick = Environment.TickCount64;
        _pressStamp++;            // 使空白按下时挂起的 0.6s 框选定时器作废
        _marqueePending = false;   // 右键不进入“纯点击”→ 不清空框选
        _marqueeMode = false;
        _marqueeStartWorld = default;
        _marqueeStartScreen = default;
        Drawable.MarqueeRect = null;
    }
    private bool IsPointerEcho => Environment.TickCount64 - LastWinPointerTick < 60;

    // 框选（编辑模式）：空白处按下静置 0.6s → 进入框选；若立即移动则照常平移，互不冲突
    private Point _marqueeStartWorld;
    private PointF _marqueeStartScreen;
    private bool _marqueePending;   // 按下后、尚未确认模式
    private bool _marqueeMode;      // 已进入框选模式（此后拖动画框）
    private int _pressStamp;        // 每次按下递增，防止旧定时器误触发

    // 长按左键框选（编辑模式）：空白处按下并拖动即进入框选，松开结算。点击（未拖动）= 仅取消选择

    public MapViewModel(GameEngine engine, MapService maps)
    {
        _engine = engine;
        _maps = maps;
        _maps.SceneChanged += OnSceneChanged;
        Drawable.AttachBgResolver(node => _maps.ResolveBackground(node.Scene));
        _ = InitAsync();
    }

    public void AttachInvalidate(Action invalidate) => _invalidate = invalidate;
    private void Touch() => _invalidate?.Invoke();

    public void Dispose()
    {
        _maps.SceneChanged -= OnSceneChanged;
    }

    private async Task InitAsync()
    {
        try
        {
            await _maps.InitializeAsync();
            Refresh();
            // 加载旧地图时顺带自动推开历史重叠的框（避免老数据重合不清）
            ResolveLocationOverlaps();
            _ = _maps.SaveAsync();
            Refresh();
            if (!_autoFitDone)
            {
                _autoFitDone = true;
                AutoFit();
            }
        }
        catch (Exception ex)
        {
            App.WriteLog("MapViewModel.InitAsync -> " + ex);
        }
    }

    private void OnSceneChanged(MapScene sc) =>
        MainThread.BeginInvokeOnMainThread(() =>
        {
            StatusText = "";
            Refresh();
        });

    private void Refresh()
    {
        var map = _maps.Map;
        var selId = SelectedNode?.Id;
        var selEdge = Drawable.SelectedEdgeId;
        Nodes.Clear();
        foreach (var loc in map.Locations)
            foreach (var sc in loc.Scenes)
                Nodes.Add(new MapCanvasNode(sc, loc) { IsCurrent = sc.Id == _maps.CurrentSceneId });
        foreach (var n in Nodes)
        {
            if (n.Scene.X == -1 && n.Scene.Y == -1)
            {
                AutoPlace(n);
                _ = _maps.SaveAsync();
            }
            else
            {
                n.X = n.Scene.X;
                n.Y = n.Scene.Y;
            }
        }
        Drawable.Nodes = Nodes.ToList();
        Drawable.Transits = map.Transits.Select(t => new MapTransitNode(t)).ToList();
        Drawable.Edges = map.Edges.ToList();
        Drawable.Locations = map.Locations.ToList();
        SelectedNode = selId is null ? null : Nodes.FirstOrDefault(n => n.Id == selId);
        Drawable.SelectedEdgeId = selEdge;
        LocationLabel = _maps.CurrentScene is { } cs
            ? $"{_maps.Map.LocationNameOf(cs.Id)} · {cs.Name}"
            : "未知位置";
        OnPropertyChanged(nameof(LocationLabel));
        Touch();
    }

    private void AutoPlace(MapCanvasNode n)
    {
        var col = Nodes.IndexOf(n) % 4;
        var row = Nodes.IndexOf(n) / 4;
        n.X = 60 + col * (MapCanvasDrawable.CardW + 52);
        n.Y = 40 + row * (MapCanvasDrawable.CardH + 60);
    }

    /// <summary>滚动帮助：介绍四种模式与新交互（长按框选/右键菜单/右键线等）。</summary>
    [RelayCommand]
    private async Task Help()
    {
await Shell.Current.DisplayAlert("使用说明",
            "【浏览】左键点卡片 = 出发前往\n【编辑】拖拽卡片 = 移动；空白处拖动 = 平移画布；按住空白不放 0.6 秒再拖动 = 框选地点（松开高亮）；拖折点 = 改路径\n【连线】先点卡片或临时节点设起点，再点目标完成边；点空白加折点；右键取消\n【涂鸦】按住左键自由画线，松手自动成边并显示总长；吸附卡片/节点\n\n右键：卡片/临时节点/连线/空白都有菜单。框内右键自动归属该地点。滚轮 = 缩放。",
            "知道了");
    }

    /// <summary>把全部卡片/临时节点按网格重新摆放。</summary>
    [RelayCommand]
    private void ResetLayout()
    {
        var col = 0;
        var row = 0;
        foreach (var n in Nodes)
        {
            n.X = 60 + col * (MapCanvasDrawable.CardW + 52);
            n.Y = 40 + row * (MapCanvasDrawable.CardH + 60);
            n.Scene.X = n.X;
            n.Scene.Y = n.Y;
            col++;
            if (col >= 4) { col = 0; row++; }
        }
        var tCol = 0;
        var tRow = Nodes.Count;
        foreach (var t in Drawable.Transits ?? System.Array.Empty<MapTransitNode>())
        {
            t.X = 60 + tCol * (MapCanvasDrawable.CardW + 52);
            t.Y = 40 + tRow * (MapCanvasDrawable.CardH + 60);
            t.Transit.X = t.X;
            t.Transit.Y = t.Y;
            tCol++;
            if (tCol >= 4) { tCol = 0; tRow++; }
        }
        _ = _maps.SaveAsync();
        StatusText = "已重置布局";
        Touch();
    }

    /// <summary>视图自动适配（首次打开）。</summary>
    private void AutoFit()
    {
        var pts = new List<Point>();
        foreach (var n in Nodes)
        {
            pts.Add(new Point(n.X, n.Y));
            pts.Add(new Point(n.X + MapCanvasDrawable.CardW, n.Y + MapCanvasDrawable.CardH));
        }
        foreach (var t in Drawable.Transits ?? new List<MapTransitNode>())
        {
            pts.Add(new Point(t.X - 40, t.Y - 40));
            pts.Add(new Point(t.X + 40, t.Y + 40));
        }
        if (pts.Count == 0) return;
        var minX = pts.Min(p => p.X);
        var minY = pts.Min(p => p.Y);
        var maxX = pts.Max(p => p.X);
        var maxY = pts.Max(p => p.Y);
        const double viewW = 1200, viewH = 720;
        var scale = Math.Min(viewW / (maxX - minX + 80), viewH / (maxY - minY + 80));
        scale = Math.Clamp(scale, 0.35, 1.6);
        Drawable.Scale = scale;
        Drawable.OffsetX = (viewW - (maxX - minX + 80) * scale) / 2 - minX * scale + 40 * scale;
        Drawable.OffsetY = (viewH - (maxY - minY + 80) * scale) / 2 - minY * scale + 40 * scale;
    }

    // ==================== 模式 ====================

    partial void OnModeChanged(ModeKind value)
    {
        OnPropertyChanged(nameof(IsEditMode));
        OnPropertyChanged(nameof(IsLinkMode));
        OnPropertyChanged(nameof(IsDoodleMode));
        OnPropertyChanged(nameof(IsViewMode));
        OnPropertyChanged(nameof(ViewModeLabel));
        OnPropertyChanged(nameof(EditModeLabel));
        OnPropertyChanged(nameof(LinkModeLabel));
        OnPropertyChanged(nameof(DoodleModeLabel));
    }

    [RelayCommand]
    private void SetMode(string mode)
    {
        if (mode == "view") Mode = ModeKind.View;
        else if (mode == "edit") Mode = ModeKind.Edit;
        else if (mode == "link") Mode = ModeKind.Link;
        else if (mode == "doodle") Mode = ModeKind.Doodle;
        // 切模式时清空框选残留（高亮/批量菜单只在编辑模式下有意义）
        Drawable.MarqueeSelected.Clear();
        Drawable.MarqueeRect = null;
        CancelLink();
        // 模式/状态提示文本更新
        OnPropertyChanged(nameof(ViewModeLabel));
        OnPropertyChanged(nameof(EditModeLabel));
        OnPropertyChanged(nameof(LinkModeLabel));
        OnPropertyChanged(nameof(DoodleModeLabel));
        StatusText = Mode switch
        {
            ModeKind.View => "左键点击卡片 = 出发前往",
            ModeKind.Edit => "左键拖动卡片，右键=菜单（新建/删除/编辑/详情/备注）",
            ModeKind.Doodle => "涂鸦：按住拖动自由画线，松手自动补端点临时节点；点卡片/节点吸附",
            _ => "连线：先点卡片/临时节点设起点，点目标连线，点空白加折点"
        };
        Touch();
    }

    /// <summary>连线线型切换（直线/曲线）。</summary>
    [RelayCommand]
    private void ToggleLinkKind()
    {
        Drawable.LinkCurve = !Drawable.LinkCurve;
        StatusText = Drawable.LinkCurve ? "当前连线的线型：贝塞尔曲线" : "当前连线的线型：直线";
        OnPropertyChanged(nameof(LinkKindText));
        Touch();
    }

    public string LinkKindText => Drawable.LinkCurve ? "曲线" : "直线";

    [RelayCommand]
    private async Task ExportMap() => StatusText = await _maps.ExportAsync();

    [RelayCommand]
    private async Task ImportMap()
    {
        var note = await _maps.ImportAsync();
        await _maps.InitializeAsync();
        Refresh();
        StatusText = note;
    }

    [RelayCommand]
    private async Task Back() => await Shell.Current.GoToAsync("..");

    [RelayCommand]
    private async Task AddLocation()
    {
        var name = await Shell.Current.DisplayPromptAsync("新建地点", "地点（建筑）的名字？", "确定", "取消");
        if (string.IsNullOrWhiteSpace(name)) return;
        _maps.AddLocation(name.Trim());
        await _maps.SaveAsync();
        StatusText = $"已添加地点：{name.Trim()}";
        Refresh();
    }

    [RelayCommand]
    private async Task AddScene()
    {
        var map = _maps.Map;
        if (map.Locations.Count == 0)
        {
            StatusText = "请先新建一个地点";
            return;
        }
        MapLocation target;
        if (map.Locations.Count == 1) target = map.Locations[0];
        else
        {
            var pick = await Shell.Current.DisplayActionSheet("新场景属于哪个地点？", "取消", null,
                map.Locations.Select(l => l.Name).ToArray());
            if (string.IsNullOrWhiteSpace(pick)) return;
            target = map.Locations.FirstOrDefault(l => l.Name == pick) ?? map.Locations[0];
        }
        var name = await Shell.Current.DisplayPromptAsync("新场景", "场景的名字？", "确定", "取消");
        if (string.IsNullOrWhiteSpace(name)) return;
        var scene = _maps.AddScene(target.Id, name.Trim());
        scene.X = _menuWorld == default ? -1 : _menuWorld.X;
        scene.Y = _menuWorld == default ? -1 : _menuWorld.Y;
        await _maps.SaveAsync();
        StatusText = $"已添加场景：{name.Trim()}（拖拽可摆位，右键可设背景/备注）";
        await PickSceneBackgroundAsync(scene);
        Refresh();
    }

    [RelayCommand]
    private async Task RemoveScene()
    {
        if (SelectedNode is null) return;
        var ok = await Shell.Current.DisplayAlert("删除场景",
            $"确定删除「{SelectedNode.SceneName}」吗？其连线也会一并删除。", "删除", "取消");
        if (!ok) return;
        _maps.RemoveScene(SelectedNode.Id);
        SelectedNode = null;
        Refresh();
        StatusText = "已删除场景";
    }

    partial void OnSelectedNodeChanged(MapCanvasNode? value)
    {
        Drawable.SelectedNode = value;
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectedNodeLabel));
        Touch();
    }

    private void CancelLink()
    {
        Drawable.LinkStartNode = null;
        Drawable.LinkStartTransit = null;
        Drawable.LinkPreview.Clear();
        Drawable.Mouse = null;
    }

    /// <summary>浏览模式下点击卡片：走最短路径前往该场景。</summary>
    private async Task GoToSceneAsync(MapCanvasNode card)
    {
        if (card is null) return;
        StatusText = $"前往「{card.SceneName}」…";
        try
        {
            var note = await _maps.MoveToAsync(card.Id);
            StatusText = string.IsNullOrWhiteSpace(note) ? "" : note.Trim('（', '）');
            Refresh();
        }
        catch (Exception ex)
        {
            App.WriteLog("MapViewModel.GoToScene -> " + ex);
            StatusText = "出发失败";
        }
    }

    // ==================== 画布交互（页面转发，屏幕坐标） ====================

    public void CanvasStart(PointF screen)
    {
        var world = Drawable.ScreenToWorld(new Point(screen.X, screen.Y));

        // 右键菜单打开时：先响应菜单点击（菜单面板为屏幕坐标，直接用屏幕点命中）
        if (Drawable.Menu is not null)
        {
            // 抑制回声：WinUI 右键打开菜单后，同一按压还会触发一次 Touch Start；
            // 若与菜单打开时间过近（<150ms），视为同动作回声，不当作“点菜单”，避免菜单闪关。
            if ((DateTime.Now - _menuOpenedAt).TotalMilliseconds < 150)
            {
                _menuOpenedAt = DateTime.MinValue;
                Touch();
                return;
            }
            var idx = Drawable.HitMenu(new Point(screen.X, screen.Y));
            var items = Drawable.Menu.Items;
            var item = idx >= 0 && idx < items.Count ? items[idx] : default;
            var kind = Drawable.Menu.TargetKind;
            var targetId = Drawable.Menu.TargetId;
            Drawable.Menu = null;
            Touch();
            if (item.Cmd is not null) _ = DispatchAsync(kind, targetId, item.Cmd);
            return;
        }

        // 精准回声闸：右键的同一输入批次（毫秒级）内，Touch Started 不进入任何交互分支。
        // 60ms 窄窗口：吞掉回声的同时不影响真实拖拽（拖拽的按下与右键相隔 >60ms，否则人做不到）。
        if (IsPointerEcho)
        {
            Touch();
            return;
        }

        if (Mode == ModeKind.Doodle)
        {
            if ((Drawable.LinkStartNode is not null || Drawable.LinkStartTransit is not null)
                && (Drawable.HitCard(world) is not null || Drawable.HitTransit(world) is not null))
            {
                FinishDoodle(world);
                return;
            }
            StartDoodleOrFinish(world);
            return;
        }

        if (Mode == ModeKind.Link)
        {
            LinkClick(world);
            return;
        }

        var card = Drawable.HitCard(world);
        if (card is not null)
        {
            if (Mode == ModeKind.View)
            {
                _ = GoToSceneAsync(card);
                return;
            }
            // 编辑模式：选中 + 可拖
            SelectedNode = card;
            Drawable.SelectedEdgeId = null;
            _dragCard = card;
            _dragLastWorld = world;
            StatusText = "";
            Touch();
            return;
        }

        var transit = Drawable.HitTransit(world);
        if (transit is not null)
        {
            if (Mode == ModeKind.View) { StatusText = $"临时节点「{transit.Name}」：仅作路径端点"; return; }
            _dragTransit = transit;
            _dragLastWorld = world;
            Drawable.SelectedEdgeId = null;
            Touch();
            return;
        }

        var edge = Drawable.HitEdge(world);
        if (edge is not null && Mode == ModeKind.Edit)
        {
            Drawable.SelectedEdgeId = edge.Id;
            SelectedNode = null;
            var wp = Drawable.HitWaypoint(edge, world);
            if (wp >= 0)
            {
                _dragEdge = edge;
                _dragWaypoint = wp;
                _dragLastWorld = world;
            }
            Touch();
            return;
        }

        // 空白：取消选择 + 预备（静置=框选；立即拖动=平移）
        SelectedNode = null;
        Drawable.SelectedNode = null;
        Drawable.SelectedEdgeId = null;
        if (Mode == ModeKind.Edit)
        {
            // 地点框拖拽（一环套一环）：命中任意地点框（含空框）→ 整体移动该地点及全部后代
            // 点卡片优先拖卡片；点框内空白 = 整框拖；点框外空白 = 平移/框选
            if (Drawable.HitLocationFrame(world) is { } loc
                && Drawable.LocationFrame(loc) is { } f
                && world.X >= f.X && world.X <= f.X + f.Width && world.Y >= f.Y && world.Y <= f.Y + f.Height)
            {
                _dragLocId = loc.Id;
                _dragLastWorld = world;
                StatusText = "";
                Touch();
                return;
            }
            _marqueeStartWorld = world;
            _marqueeStartScreen = screen;
            Drawable.MarqueeRect = null;
            _marqueePending = true;
            _marqueeMode = false;
            _panning = false;
            var stamp = ++_pressStamp;
            _ = TryEnterMarqueeAsync(stamp);
            Touch();
            return;
        }
        _panning = true;
        _panLastScreen = screen;
        Touch();
    }

    public void CanvasDrag(PointF screen)
    {
        if (Mode == ModeKind.Doodle)
        {
            // 涂鸦模式：按住拖动时不断追加折点；非按下拖动实际由 Start/End 驱动
            var w = Drawable.ScreenToWorld(new Point(screen.X, screen.Y));
            if (Drawable.LinkStartNode is not null || Drawable.LinkStartTransit is not null)
            {
                var last = Drawable.LinkPreview.Count > 0 ? Drawable.LinkPreview[^1] : WorldPointOfStart();
                if (Math.Abs(w.X - last.X) > 6 || Math.Abs(w.Y - last.Y) > 6)
                    Drawable.LinkPreview.Add(w);
            }
            Drawable.Mouse = new Point(screen.X, screen.Y);
            Touch();
            return;
        }
        if (Mode == ModeKind.Link)
        {
            Drawable.Mouse = new Point(screen.X, screen.Y);
            Touch();
            return;
        }
        if (_dragLocId is not null)
        {
            var w = Drawable.ScreenToWorld(new Point(screen.X, screen.Y));
            TranslateSubtree(_dragLocId, w.X - _dragLastWorld.X, w.Y - _dragLastWorld.Y);
            _dragLastWorld = w;
            SyncNodesInSubtree(_dragLocId);
            Touch();
            return;
        }
        if (_dragCard is not null)
        {
            var w = Drawable.ScreenToWorld(new Point(screen.X, screen.Y));
            _dragCard.X = _dragCard.X + w.X - _dragLastWorld.X;
            _dragCard.Y = _dragCard.Y + w.Y - _dragLastWorld.Y;
            _dragLastWorld = w;
            Touch();
            return;
        }
        if (_dragTransit is not null)
        {
            var w = Drawable.ScreenToWorld(new Point(screen.X, screen.Y));
            _dragTransit.X = _dragTransit.X + w.X - _dragLastWorld.X;
            _dragTransit.Y = _dragTransit.Y + w.Y - _dragLastWorld.Y;
            _dragLastWorld = w;
            Touch();
            return;
        }
        if (_dragEdge is not null && _dragWaypoint >= 0)
        {
            var w = Drawable.ScreenToWorld(new Point(screen.X, screen.Y));
            var wp = _dragEdge.Waypoints[_dragWaypoint];
            wp.X = w.X;
            wp.Y = w.Y;
            Touch();
            return;
        }
        if (_marqueeMode)
        {
            // 框选模式：更新矩形
            var w = Drawable.ScreenToWorld(new Point(screen.X, screen.Y));
            var x0 = Math.Min(_marqueeStartWorld.X, w.X);
            var y0 = Math.Min(_marqueeStartWorld.Y, w.Y);
            Drawable.MarqueeRect = new Microsoft.Maui.Graphics.RectF((float)x0, (float)y0,
                (float)Math.Abs(w.X - _marqueeStartWorld.X), (float)Math.Abs(w.Y - _marqueeStartWorld.Y));
            Touch();
            return;
        }
        if (_marqueePending)
        {
            // 尚未进入框选：移动即取消候补 → 恢复平移
            if (Math.Abs(screen.X - _marqueeStartScreen.X) > 6 || Math.Abs(screen.Y - _marqueeStartScreen.Y) > 6)
            {
                _marqueePending = false;
                _panning = true;
                _panLastScreen = screen;
            }
            else
            {
                Touch();
                return;
            }
        }
        if (_panning)
        {
            Drawable.OffsetX += screen.X - _panLastScreen.X;
            Drawable.OffsetY += screen.Y - _panLastScreen.Y;
            _panLastScreen = screen;
            Touch();
        }
    }

    public void CanvasEnd(PointF screen)
    {
        // 右键回声：同一按压的 Touch End 不当作“纯点击”/结算框选，避免清空已有框选
        if (IsPointerEcho)
        {
            Touch();
            return;
        }
        if (Mode == ModeKind.Doodle)
        {
            var endW = Drawable.ScreenToWorld(new Point(screen.X, screen.Y));
            FinishDoodle(endW);
            return;
        }
        if (_marqueeMode)
        {
            // 结算框选：命中矩形相交的地点，高亮
            Drawable.MarqueeSelected.Clear();
            if (Drawable.MarqueeRect is { } rect && rect.Width > 1 && rect.Height > 1)
            {
                foreach (var loc in Drawable.Locations ?? new List<MapLocation>())
                    if (Drawable.LocationFrame(loc) is { } f
                        && f.X < rect.Right && f.X + f.Width > rect.Left
                        && f.Y < rect.Bottom && f.Y + f.Height > rect.Top)
                        Drawable.MarqueeSelected.Add(loc.Id);
                StatusText = Drawable.MarqueeSelected.Count == 0
                    ? "框选未命中任何地点"
                    : $"已框选 {Drawable.MarqueeSelected.Count} 个地点，右键可新建场景/自动归属";
            }
            else
            {
                StatusText = "";
            }
            Drawable.MarqueeRect = null;
            _marqueeMode = false;
            _marqueePending = false;
            Touch();
            return;
        }
        if (_marqueePending)
        {
            // 纯点击：仅取消选择
            _marqueePending = false;
            Drawable.MarqueeSelected.Clear();
            Touch();
            return;
        }
        if (_dragCard is not null)
        {
            _dragCard.Scene.X = _dragCard.X;
            _dragCard.Scene.Y = _dragCard.Y;
            _ = _maps.SaveAsync();
        }
        else if (_dragTransit is not null)
        {
            _dragTransit.Transit.X = _dragTransit.X;
            _dragTransit.Transit.Y = _dragTransit.Y;
            _ = _maps.SaveAsync();
        }
        else if (_dragEdge is not null)
        {
            _ = _maps.SaveAsync();
        }
        _dragCard = null;
        _dragTransit = null;
        _dragEdge = null;
        _dragWaypoint = -1;
        if (_dragLocId is not null)
        {
            _ = _maps.SaveAsync();
            _dragLocId = null;
        }
        _panning = false;
        _marqueePending = false;
        _marqueeMode = false;
        if (Mode == ModeKind.Link) Drawable.Mouse = null;
    }

    /// <summary>空白按下后静置 0.6s：若仍未移动、未变平移，则进入框选模式。</summary>
    private async Task TryEnterMarqueeAsync(int stamp)
    {
        try
        {
            await Task.Delay(600);
            if (stamp != _pressStamp) return;   // 期间有过新的按下/新模式，作废
            if (Mode == ModeKind.Edit && _marqueePending && !_panning && !_marqueeMode)
            {
                _marqueeMode = true;
                Drawable.MarqueeRect = new Microsoft.Maui.Graphics.RectF(
                    (float)_marqueeStartWorld.X, (float)_marqueeStartWorld.Y, 0, 0);
                StatusText = "框选中：拖动选择地点；松开完成";
                Touch();
            }
        }
        catch (Exception ex)
        {
            App.WriteLog("MapViewModel.TryEnterMarquee -> " + ex);
        }
    }

    /// <summary>滚轮缩放（屏幕点为锚）。</summary>
    public void Zoom(double delta, PointF screen)
    {
        var oldScale = Drawable.Scale;
        var factor = delta > 0 ? 1.15 : 1 / 1.15;
        var newScale = Math.Clamp(oldScale * factor, 0.3, 2.5);
        if (Math.Abs(newScale - oldScale) < 0.001) return;
        // 保持鼠标指向的世界点不动
        var wx = (screen.X - Drawable.OffsetX) / oldScale;
        var wy = (screen.Y - Drawable.OffsetY) / oldScale;
        Drawable.OffsetX = screen.X - wx * newScale;
        Drawable.OffsetY = screen.Y - wy * newScale;
        Drawable.Scale = newScale;
        Touch();
    }

    /// <summary>右键（屏幕坐标）→ 上下文菜单。</summary>
    public void RightClick(PointF screen)
    {
        var world = Drawable.ScreenToWorld(new Point(screen.X, screen.Y));
        _menuOpenedAt = DateTime.Now;   // 标记本次右键将打开菜单，抑制随后的 Touch 回声
        if (Drawable.Menu is not null) { Drawable.Menu = null; Touch(); return; }
        if ((Mode == ModeKind.Link || Mode == ModeKind.Doodle)
            && (Drawable.LinkStartNode is not null || Drawable.LinkStartTransit is not null))
        {
            CancelLink();
            StatusText = "已取消连线/涂鸦";
            Touch();
            return;
        }

        var card = Drawable.HitCard(world);
        if (card is not null)
        {
            Drawable.Menu = new ContextMenuState
            {
                Pos = world,
                Title = card.SceneName,
                TargetKind = "scene",
                TargetId = card.Id,
                Items = new()
                {
                    ("🚶 出发", "go"),
                    ("✎ 编辑名称", "rename"),
                    ("🖼 背景图", "bg"),
                    ("📝 备注", "note"),
                    ("ℹ 详细信息", "info"),
                    ("🗑 删除", "del")
                }
            };
            Touch();
            return;
        }

        var transit = Drawable.HitTransit(world);
        if (transit is not null)
        {
            Drawable.Menu = new ContextMenuState
            {
                Pos = world,
                Title = transit.Name,
                TargetKind = "transit",
                TargetId = transit.Id,
                Items = new()
                {
                    ("✎ 编辑名称", "edit"),
                    ("📝 备注", "note"),
                    ("ℹ 详细信息", "info"),
                    ("🗑 删除", "del")
                }
            };
            Touch();
            return;
        }

        var edge = Drawable.HitEdge(world);
        if (edge is not null)
        {
            Drawable.SelectedEdgeId = edge.Id;
            Drawable.Menu = new ContextMenuState
            {
                Pos = world,
                Title = "连线",
                TargetKind = "edge",
                TargetId = edge.Id,
                Items = new()
                {
                    ("📏 编辑长度", "length"),
                    ($"线型: {edge.Kind}", "kind"),
                    ("📝 备注", "note"),
                    ("ℹ 详细信息", "info"),
                    ("🗑 删除", "del")
                }
            };
            Touch();
            return;
        }

        // 空白：新建。目标地点优先级：右键落点命中的最深框 → 框选命中的地点（仅落点不在任何框内时兜底）
        _menuWorld = world;
        var hitLoc = Drawable.HitLocationFrame(world);
        var marqueeLoc = hitLoc is null && Drawable.MarqueeSelected.Count == 1
            ? _maps.Map.Locations.FirstOrDefault(l => Drawable.MarqueeSelected.Contains(l.Id))
            : null;
        var inLoc = hitLoc ?? marqueeLoc;
        var menuItems = new List<(string Label, string Cmd)>
        {
            ("🏠 新建地点", "newloc")
        };
        if (inLoc is not null)
        {
            // 明确告知归属：框选时以框选地点为归属，右键落点不再覆盖
            var titlePart = marqueeLoc is not null ? "（框选）" : "";
            menuItems.Add(("📍 新建场景" + titlePart, "newscene"));
            menuItems.Add(("⛺ 新建子地点", "newchild"));
            menuItems.Add(("◉ 新建临时节点", "newtransit"));
            menuItems.Add(("✏️ 重命名此地", "renloc"));
            menuItems.Add(("🗑️ 删除此地点", "delloc"));
        }
        else
        {
            menuItems.Add(("📍 新建场景", "newscene"));
            menuItems.Add(("◉ 新建临时节点", "newtransit"));
        }
        // 批量嵌套：只要有框选，就提供“批量归入”入口（新建或已有父地点）
        if (Drawable.MarqueeSelected.Count > 0)
        {
            menuItems.Add(($"📦 批量归入新建地点（{Drawable.MarqueeSelected.Count}）", "nestnew"));
            menuItems.Add(($"📦 批量归入已有地点（{Drawable.MarqueeSelected.Count}）", "nestinto"));
        }
        Drawable.Menu = new ContextMenuState
        {
            Pos = world,
            Title = inLoc is not null ? $"{(inLoc.Name)}" : "新建…",
            TargetKind = inLoc is not null ? "loc" : "blank",
            TargetId = inLoc?.Id ?? "",
            Items = menuItems
        };
        Touch();
    }

    // ==================== 连线逻辑 ====================

    private void LinkClick(Point world)
    {
        var startNode = Drawable.LinkStartNode;
        var startTransit = Drawable.LinkStartTransit;
        if (startNode is null && startTransit is null)
        {
            var card = Drawable.HitCard(world);
            if (card is not null)
            {
                Drawable.LinkStartNode = card;
                StatusText = $"起点：{card.SceneName} —— 点击目标卡片/临时节点；点空白=加折点；右键=取消";
            }
            else if (Drawable.HitTransit(world) is { } tr)
            {
                Drawable.LinkStartTransit = tr;
                StatusText = $"起点：临时节点「{tr.Name}」 —— 点击目标卡片/临时节点；点空白=加折点；右键=取消";
            }
            else
            {
                StatusText = "请先点击一张卡片或一个临时节点作为起点";
            }
            Touch();
            return;
        }

        var endCard = Drawable.HitCard(world);
        var endTransit = Drawable.HitTransit(world);
        var endId = endCard?.Id ?? endTransit?.Id;
        var startId = startNode?.Id ?? startTransit!.Id;
        if (endId is null)
        {
            // 空白：加点折点
            Drawable.LinkPreview.Add(world);
            StatusText = $"已添加折点（共 {Drawable.LinkPreview.Count} 个），点击目标卡片/节点完成连线";
            Touch();
            return;
        }
        if (endId == startId)
        {
            CancelLink();
            StatusText = "已取消";
            Touch();
            return;
        }

        var existed = _maps.Map.Edges.Any(e =>
            (e.A == startId && e.B == endId) || (e.A == endId && e.B == startId));
        if (existed)
        {
            CancelLink();
            StatusText = "这两个端点已有连线（右键可管理它）";
            Touch();
            return;
        }

        var edge = new MapEdge
        {
            A = startId,
            B = endId,
            Kind = Drawable.LinkCurve ? "curve" : "line"
        };
        foreach (var p in Drawable.LinkPreview) edge.Waypoints.Add(new MapWaypoint { X = p.X, Y = p.Y });
        _maps.Map.Edges.Add(edge);
        _ = _maps.SaveAsync();
        var endName = endCard?.SceneName ?? endTransit!.Name;
        StatusText = $"已连线：{Drawable.LinkStartNode?.SceneName ?? Drawable.LinkStartTransit!.Name} ↔ {endName}"
                     + (edge.Waypoints.Count > 0 ? $"（{edge.Waypoints.Count} 个中继点）" : "");
        CancelLink();
        Refresh();
    }

    // ==================== 涂鸦模式 ====================

    private Point WorldPointOfStart()
    {
        if (Drawable.LinkStartNode is { } n) return new Point(n.X, n.Y);
        if (Drawable.LinkStartTransit is { } t) return new Point(t.X, t.Y);
        return default;
    }

    /// <summary>涂鸦起点：点击卡片/临时节点=吸附；点空白=自动创建临时节点作为端点。</summary>
    private void StartDoodleOrFinish(Point world)
    {
        if (Drawable.LinkStartNode is null && Drawable.LinkStartTransit is null)
        {
            var card = Drawable.HitCard(world);
            if (card is not null)
            {
                Drawable.LinkStartNode = card;
                StatusText = $"涂鸦起点：{card.SceneName} —— 按住拖动画线，松手完成；点空白可再次吸附";
            }
            else if (Drawable.HitTransit(world) is { } tr)
            {
                Drawable.LinkStartTransit = tr;
                StatusText = $"涂鸦起点：临时节点「{tr.Name}」 —— 按住拖动画线，松手完成";
            }
            else
            {
                var transit = _maps.AddTransit(world.X, world.Y);
                Drawable.LinkStartTransit = new MapTransitNode(transit);
                Refresh();
                StatusText = $"已自动创建端点「{transit.Name}」—— 按住拖动涂鸦，松手完成";
            }
            Drawable.LinkPreview.Clear();
            Touch();
            return;
        }

        // 已有点：点在卡片/临时节点上 = 直接完成（吸附终点）
        var endCard = Drawable.HitCard(world);
        var endTransit = Drawable.HitTransit(world);
        if (endCard is not null || endTransit is not null)
            FinishDoodle(world);
        else
            StatusText = "按住拖动涂鸦，或点击卡片/临时节点吸附终点；右键=取消";
        Touch();
    }

    /// <summary>结束涂鸦：自动补齐端点临时节点并生成连线。</summary>
    private void FinishDoodle(Point releaseWorld)
    {
        var startNode = Drawable.LinkStartNode;
        var startTransit = Drawable.LinkStartTransit;
        if (startNode is null && startTransit is null) return;

        // 追加松手点作为末尾折点
        if (Drawable.LinkPreview.Count == 0
            || Math.Abs(releaseWorld.X - Drawable.LinkPreview[^1].X) > 4
            || Math.Abs(releaseWorld.Y - Drawable.LinkPreview[^1].Y) > 4)
            Drawable.LinkPreview.Add(releaseWorld);

        var startId = startNode?.Id ?? startTransit!.Id;
        var pts = Drawable.LinkPreview.ToList();
        // 终点：最后一个折点吸附卡片/临时节点，否则自动创建临时节点
        var endWorld = pts.Count > 0 ? pts[^1] : WorldPointOfStart();
        var endCard = Drawable.HitCard(endWorld);
        var endTransit = Drawable.HitTransit(endWorld);
        string endId;
        string endName;
        if (endCard is not null)
        {
            endId = endCard.Id;
            endName = endCard.SceneName;
        }
        else if (endTransit is not null)
        {
            endId = endTransit.Id;
            endName = endTransit.Name;
        }
        else
        {
            var t = _maps.AddTransit(endWorld.X, endWorld.Y);
            endId = t.Id;
            endName = t.Name;
        }

        if (endId == startId)
        {
            // 空画（未动、也未吸附到目标）：回收自动创建的临时节点
            if (pts.Count == 0 && endCard is null && endTransit is null && startTransit is not null)
                _maps.RemoveTransit(startId);
            CancelLink();
            StatusText = "涂鸦未形成有效的线，已取消";
            Refresh();
            return;
        }
        if (_maps.Map.Edges.Any(e => (e.A == startId && e.B == endId) || (e.A == endId && e.B == startId)))
        {
            CancelLink();
            StatusText = "这两个端点已有连线，请选其他目标";
            Refresh();
            return;
        }

        var edge = new MapEdge { A = startId, B = endId, Kind = "line" };
        // 涂鸦：只显示整条总长一个标记（TotalLabel=true），不做逐段拆分
        // 原始点很密：道格拉斯-普克抽稀只保留明显拐弯处，线条干净
        var simp = SimplifyPoints(pts, 12);
        foreach (var p in simp) edge.Waypoints.Add(new MapWaypoint { X = p.X, Y = p.Y });
        edge.TotalLabel = true;
        _maps.Map.Edges.Add(edge);
        _ = _maps.SaveAsync();
        var startName = startNode?.SceneName ?? startTransit!.Name;
        StatusText = $"涂鸦完成：{startName} ↔ {endName}（总长 {_maps.DisplayLengthMeters(edge):F0} 米）";
        CancelLink();
        Refresh();
    }

    /// <summary>Ramer–Douglas–Peucker 抽稀：保留明显拐弯点，去掉近似直线上的密集采样。</summary>
    private static List<Point> SimplifyPoints(List<Point> pts, double tolerance)
    {
        if (pts.Count <= 2) return pts.ToList();

        double PerpDist(Point a, Point b, Point c)
        {
            var dx = b.X - a.X;
            var dy = b.Y - a.Y;
            var l2 = dx * dx + dy * dy;
            if (l2 < 1e-9) return Math.Sqrt(Math.Pow(c.X - a.X, 2) + Math.Pow(c.Y - a.Y, 2));
            var t = ((c.X - a.X) * dx + (c.Y - a.Y) * dy) / l2;
            t = Math.Clamp(t, 0, 1);
            var px = a.X + dx * t;
            var py = a.Y + dy * t;
            return Math.Sqrt(Math.Pow(c.X - px, 2) + Math.Pow(c.Y - py, 2));
        }

        void Rdp(int first, int last, List<bool> keep)
        {
            if (last <= first + 1) return;
            var maxD = 0.0;
            var maxI = first;
            for (var i = first + 1; i < last; i++)
            {
                var d = PerpDist(pts[first], pts[last], pts[i]);
                if (d > maxD) { maxD = d; maxI = i; }
            }
            if (maxD > tolerance)
            {
                keep[maxI] = true;
                Rdp(first, maxI, keep);
                Rdp(maxI, last, keep);
            }
        }

        var keep = new List<bool>(new bool[pts.Count]);
        keep[0] = true;
        keep[^1] = true;
        Rdp(0, pts.Count - 1, keep);
        var out_ = new List<Point>();
        for (var i = 0; i < pts.Count; i++)
            if (keep[i]) out_.Add(pts[i]);
        return out_;
    }

    // ==================== 菜单命令 ====================

    private async Task DispatchAsync(string kind, string targetId, string cmd)
    {
        try
        {
            switch (kind)
            {
                case "scene":
                    await DispatchSceneAsync(targetId, cmd);
                    break;
                case "transit":
                    await DispatchTransitAsync(targetId, cmd);
                    break;
                case "edge":
                    await DispatchEdgeAsync(targetId, cmd);
                    break;
                case "loc":
                    await DispatchLocAsync(targetId, cmd);
                    break;
                default:
                    await DispatchBlankAsync(cmd);
                    break;
            }
        }
        catch (Exception ex)
        {
            App.WriteLog("MapViewModel.Dispatch -> " + ex);
        }
    }

    private async Task DispatchSceneAsync(string id, string cmd)
    {
        var scene = _maps.Map.SceneById(id);
        switch (cmd)
        {
            case "go":
            {
                if (scene is null) return;
                var note = await _maps.MoveToAsync(scene.Id);
                if (!string.IsNullOrWhiteSpace(note)) StatusText = note.Trim('（', '）');
                Refresh();
                break;
            }
            case "rename":
            {
                if (scene is null) return;
                var name = await Shell.Current.DisplayPromptAsync("编辑名称", "新的场景名称？", "确定", "取消",
                    initialValue: scene.Name);
                if (string.IsNullOrWhiteSpace(name)) return;
                scene.Name = name.Trim();
                await _maps.SaveAsync();
                StatusText = $"已重命名：{name.Trim()}";
                Refresh();
                break;
            }
            case "bg":
                await PickSceneBackgroundAsync(scene);
                Refresh();
                break;
            case "note":
                if (scene is null) return;
                var note2 = await Shell.Current.DisplayPromptAsync("备注", "为这个场景添加备注（可留空清除）", "确定", "取消",
                    initialValue: scene.Note);
                if (note2 is null) return;
                scene.Note = note2.Trim();
                await _maps.SaveAsync();
                StatusText = string.IsNullOrWhiteSpace(scene.Note) ? "已清除备注" : "已保存备注";
                Refresh();
                break;
            case "info":
                if (scene is null) return;
                await Shell.Current.DisplayAlert("场景信息",
                    $"场景：{scene.Name}\n地点：{_maps.Map.LocationNameOf(scene.Id)}\n背景：{(string.IsNullOrWhiteSpace(scene.Background) ? "无" : "✓")}\n备注：{(string.IsNullOrWhiteSpace(scene.Note) ? "无" : scene.Note)}",
                    "知道了");
                break;
            case "del":
            {
                if (scene is null) return;
                var ok = await Shell.Current.DisplayAlert("删除场景",
                    $"确定删除「{scene.Name}」吗？其连线也会一并删除。", "删除", "取消");
                if (!ok) return;
                _maps.RemoveScene(scene.Id);
                StatusText = "已删除场景";
                Refresh();
                break;
            }
        }
    }

    private async Task DispatchTransitAsync(string id, string cmd)
    {
        var transit = _maps.Map.TransitById(id);
        switch (cmd)
        {
            case "edit":
            {
                if (transit is null) return;
                var name = await Shell.Current.DisplayPromptAsync("编辑名称", "临时节点的名称？", "确定", "取消",
                    initialValue: transit.Name);
                if (string.IsNullOrWhiteSpace(name)) return;
                transit.Name = name.Trim();
                await _maps.SaveAsync();
                StatusText = $"已重命名：{name.Trim()}";
                Refresh();
                break;
            }
            case "note":
                if (transit is null) return;
                var note = await Shell.Current.DisplayPromptAsync("编辑备注", "备注内容（可留空清除）", "确定", "取消",
                    initialValue: transit.Note);
                if (note is null) return;
                transit.Note = note.Trim();
                await _maps.SaveAsync();
                Refresh();
                break;
            case "info":
                if (transit is null) return;
                await Shell.Current.DisplayAlert("临时节点信息",
                    $"名称：{transit.Name}\n坐标：({transit.X:F0}, {transit.Y:F0})\n备注：{(string.IsNullOrWhiteSpace(transit.Note) ? "无" : transit.Note)}",
                    "知道了");
                break;
            case "del":
            {
                if (transit is null) return;
                var ok = await Shell.Current.DisplayAlert("删除临时节点",
                    $"确定删除「{transit.Name}」吗？其连线也会一并删除。", "删除", "取消");
                if (!ok) return;
                _maps.RemoveTransit(transit.Id);
                StatusText = "已删除临时节点";
                Refresh();
                break;
            }
        }
    }

    private async Task DispatchEdgeAsync(string id, string cmd)
    {
        var edge = _maps.Map.Edges.FirstOrDefault(e => e.Id == id);
        switch (cmd)
        {
            case "length":
            {
                if (edge is null) return;
                var current = edge.Length < 0 ? "" : edge.Length.ToString("F0");
                var input = await Shell.Current.DisplayPromptAsync("编辑长度（米）",
                    "填入长度（米）；留空=按路径自动计算；填 0=虚线（不计算）", "确定", "取消",
                    initialValue: current, keyboard: Keyboard.Numeric);
                if (input is null) return;
                if (string.IsNullOrWhiteSpace(input)) { edge.Length = -1; }
                else if (double.TryParse(input.Trim(), out var len)) edge.Length = Math.Max(0, len);
                else StatusText = "无效的数值";
                await _maps.SaveAsync();
                StatusText = edge.Length == 0 ? "该连线将显示为虚线" :
                    edge.Length < 0 ? "该连线使用自动计算长度" : $"该连线长度为 {edge.Length:F0} 米";
                Refresh();
                break;
            }
            case "kind":
            {
                if (edge is null) return;
                edge.Kind = edge.Kind == "line" ? "curve" : "line";
                await _maps.SaveAsync();
                StatusText = $"线型已切换为：{(edge.Kind == "line" ? "直线" : "曲线")}";
                Refresh();
                break;
            }
            case "note":
                if (edge is null) return;
                var note = await Shell.Current.DisplayPromptAsync("编辑备注", "连线备注（可留空清除）", "确定", "取消",
                    initialValue: edge.Note);
                if (note is null) return;
                edge.Note = note.Trim();
                await _maps.SaveAsync();
                Refresh();
                break;
            case "info":
                if (edge is null) return;
                var aName = _maps.Map.PointNameById(edge.A) ?? edge.A;
                var bName = _maps.Map.PointNameById(edge.B) ?? edge.B;
                await Shell.Current.DisplayAlert("连线信息",
                    $"从：{aName}\n到：{bName}\n线型：{(edge.Kind == "line" ? "直线" : "曲线")}\n折点：{edge.Waypoints.Count}\n长度：{LengthText(edge)}\n备注：{(string.IsNullOrWhiteSpace(edge.Note) ? "无" : edge.Note)}",
                    "知道了");
                break;
            case "del":
            {
                if (edge is null) return;
                var ok = await Shell.Current.DisplayAlert("删除连线",
                    $"确定删除「{_maps.Map.PointNameById(edge.A) ?? edge.A} ↔ {_maps.Map.PointNameById(edge.B) ?? edge.B}」吗？", "删除", "取消");
                if (!ok) return;
                _maps.RemoveEdge(edge.A, edge.B);
                StatusText = "已删除连线";
                Refresh();
                break;
            }
        }
    }

    private async Task DispatchLocAsync(string locId, string cmd)
    {
        switch (cmd)
        {
            case "renloc":
            {
                var loc = _maps.Map.Locations.FirstOrDefault(l => l.Id == locId);
                if (loc is null) return;
                var name = await Shell.Current.DisplayPromptAsync("重命名地点", "地点（建筑/区域）的名字？", "确定", "取消",
                    initialValue: loc.Name);
                if (string.IsNullOrWhiteSpace(name)) return;
                loc.Name = name.Trim();
                await _maps.SaveAsync();
                StatusText = $"地点已重命名：{name.Trim()}";
                Refresh();
                break;
            }
            case "newscene":
            {
                var loc = _maps.Map.Locations.FirstOrDefault(l => l.Id == locId);
                if (loc is null) return;
                var sname = await Shell.Current.DisplayPromptAsync("新场景", "此地点下新场景的名字？", "确定", "取消");
                if (string.IsNullOrWhiteSpace(sname)) return;
                var scene = _maps.AddScene(loc.Id, sname.Trim());
                scene.X = _menuWorld.X;
                scene.Y = _menuWorld.Y;
                await _maps.SaveAsync();
                Drawable.MarqueeSelected.Clear();
                StatusText = $"已在「{loc.Name}」中新建场景：{sname.Trim()}";
                Refresh();
                break;
            }
            case "newchild":
            {
                var parent = _maps.Map.Locations.FirstOrDefault(l => l.Id == locId);
                if (parent is null) return;
                var cname = await Shell.Current.DisplayPromptAsync("新建子地点",
                    $"「{parent.Name}」下的子地点名字？", "确定", "取消");
                if (string.IsNullOrWhiteSpace(cname)) return;
                var child = _maps.AddLocation(cname.Trim(), parent.Id);
                // 落点若在父框外，锚点自动收进父框内（一环套一环：子框必须由父框包住）
                var pf = Drawable.LocationFrame(parent);
                if (pf is { } pfv)
                {
                    var cx = _menuWorld.X < pfv.Left || _menuWorld.X > pfv.Right
                        ? pfv.Center.X : _menuWorld.X;
                    var cy = _menuWorld.Y < pfv.Top || _menuWorld.Y > pfv.Bottom
                        ? pfv.Center.Y : _menuWorld.Y;
                    child.X = cx;
                    child.Y = cy;
                }
                else
                {
                    child.X = _menuWorld.X;
                    child.Y = _menuWorld.Y;
                }
                ResolveLocationOverlaps();
                await _maps.SaveAsync();
                StatusText = $"已添加子地点：{cname.Trim()}（在「{parent.Name}」内）——可右键其内部新建场景";
                Refresh();
                break;
            }
            case "delloc":
            {
                var loc = _maps.Map.Locations.FirstOrDefault(l => l.Id == locId);
                if (loc is null) return;
                var subCount = _maps.Map.Locations.Count(l => l.ParentId == loc.Id);
                var ok = await Shell.Current.DisplayAlert("删除地点",
                    $"确定删除「{loc.Name}」吗？其全部场景、连线{(subCount > 0 ? $"及 {subCount} 个子地点" : "")}将一并删除。", "删除", "取消");
                if (!ok) return;
                _maps.RemoveLocation(loc.Id);
                StatusText = $"已删除地点「{loc.Name}」";
                Refresh();
                break;
            }
            case "stats":
                break;
            default:
                break;
        }
    }

    private async Task DispatchBlankAsync(string cmd)
    {
        switch (cmd)
        {
            case "newloc":
            {
                var name = await Shell.Current.DisplayPromptAsync("新建地点", "地点（建筑）的名字？", "确定", "取消");
                if (string.IsNullOrWhiteSpace(name)) return;
                var loc = _maps.AddLocation(name.Trim());
                loc.X = _menuWorld.X;
                loc.Y = _menuWorld.Y;
                await _maps.SaveAsync();
                StatusText = $"已添加地点：{name.Trim()}（可右键空白继续新建场景）";
                Refresh();
                break;
            }
            case "newscene":
            {
                var map = _maps.Map;
                if (map.Locations.Count == 0)
                {
                    StatusText = "请先新建一个地点";
                    break;
                }
                MapLocation target;
                // 框选唯一地点优先（右键空白但已框选时）
                var boxed = Drawable.MarqueeSelected
                    .Select(id => map.Locations.FirstOrDefault(l => l.Id == id))
                    .Where(l => l is not null)
                    .Select(l => l!)
                    .ToList();
                if (boxed.Count == 1) target = boxed[0];
                else if (boxed.Count > 1)
                {
                    // 多处框选时也直接弹选择器（与 pickloc 一致）
                    var pick = await Shell.Current.DisplayActionSheet("框选地点中选择场景归属", "取消", null,
                        boxed.Select(l => l.Name).ToArray());
                    if (string.IsNullOrWhiteSpace(pick)) break;
                    target = boxed.FirstOrDefault(l => l.Name == pick) ?? boxed[0];
                }
                else if (map.Locations.Count == 1) target = map.Locations[0];
                else
                {
                    var pick = await Shell.Current.DisplayActionSheet("新场景属于哪个地点？", "取消", null,
                        map.Locations.Select(l => l.Name).ToArray());
                    if (string.IsNullOrWhiteSpace(pick)) break;
                    target = map.Locations.FirstOrDefault(l => l.Name == pick) ?? map.Locations[0];
                }
                var sname = await Shell.Current.DisplayPromptAsync("新场景", "场景的名字？", "确定", "取消");
                if (string.IsNullOrWhiteSpace(sname)) return;
                var scene = _maps.AddScene(target.Id, sname.Trim());
                scene.X = _menuWorld.X;
                scene.Y = _menuWorld.Y;
                await _maps.SaveAsync();
                StatusText = $"已添加场景：{sname.Trim()}（拖拽可摆放位置），右键可设背景图";
                Refresh();
                break;
            }
                        case "pickloc":
            {
                // 多选框选：在框选地点中选一个归属，然后新建场景
                var picked = Drawable.MarqueeSelected
                    .Select(id => _maps.Map.Locations.FirstOrDefault(l => l.Id == id))
                    .Where(l => l is not null)
                    .ToList();
                if (picked.Count == 0) { StatusText = "框选地点已失效，请重新框选"; break; }
                MapLocation target;
                if (picked.Count == 1) target = picked[0]!;
                else
                {
                    var pick = await Shell.Current.DisplayActionSheet("框选地点中选择场景归属", "取消", null,
                        picked.Select(l => l!.Name).ToArray());
                    if (string.IsNullOrWhiteSpace(pick)) break;
                    target = picked.FirstOrDefault(l => l!.Name == pick) ?? picked[0]!;
                }
                var sname = await Shell.Current.DisplayPromptAsync("新场景", $"「{target.Name}」下场景的名字？", "确定", "取消");
                if (string.IsNullOrWhiteSpace(sname)) return;
                var scene = _maps.AddScene(target.Id, sname.Trim());
                scene.X = _menuWorld.X;
                scene.Y = _menuWorld.Y;
                await _maps.SaveAsync();
                Drawable.MarqueeSelected.Clear();
                StatusText = $"已添加场景：{sname.Trim()}（归属「{target.Name}」）";
                Refresh();
                break;
            }
            case "newtransit":
            {
                var transit = _maps.AddTransit(_menuWorld.X, _menuWorld.Y);
                await _maps.SaveAsync();
                StatusText = $"已添加临时节点「{transit.Name}」—— 连线模式中可作为端点";
                Refresh();
                break;
            }
            case "nestnew":
            case "nestinto":
            {
                await NestSelectedLocationsAsync(cmd == "nestnew");
                break;
            }
            default:
                break;
        }
    }

    /// <summary>批量嵌套：把当前框选的地点整体归入新建（或已有）父地点。</summary>
    private async Task NestSelectedLocationsAsync(bool createParent)
    {
        var map = _maps.Map;
        var selected = Drawable.MarqueeSelected
            .Select(id => map.Locations.FirstOrDefault(l => l.Id == id))
            .Where(l => l is not null)
            .Select(l => l!)
            .ToList();
        if (selected.Count == 0)
        {
            StatusText = "框选地点已失效，请重新框选";
            return;
        }

        // 目标父地点：新建 or 从已有地点中挑选（排除被框选者及其父链，避免循环嵌套）
        MapLocation? parent;
        if (createParent)
        {
            var pname = await Shell.Current.DisplayPromptAsync("新建父地点", "用来收纳这批地点的父地点名字？", "确定", "取消",
                initialValue: $"{selected[0].Name}群");
            if (string.IsNullOrWhiteSpace(pname)) return;
            parent = _maps.AddLocation(pname.Trim());
            parent.X = _menuWorld.X;
            parent.Y = _menuWorld.Y;
        }
        else
        {
            var selectedIds = selected.Select(l => l.Id).ToHashSet();
            var candidates = map.Locations.Where(l => !selectedIds.Contains(l.Id)).ToList();
            if (candidates.Count == 0)
            {
                StatusText = "没有可归入的已有地点（请先新建地点，或框选其它地点）";
                return;
            }
            var pick = await Shell.Current.DisplayActionSheet("归入哪个已有地点？", "取消", null,
                candidates.Select(l => l.Name).ToArray());
            if (string.IsNullOrWhiteSpace(pick)) return;
            parent = candidates.FirstOrDefault(l => l.Name == pick) ?? candidates[0];
        }
        if (parent is null) return;

        var selfChain = new HashSet<string>();
        for (var p = parent; p is not null; p = map.Locations.FirstOrDefault(l => l.Id == p.ParentId))
            selfChain.Add(p.Id);

        // 记下每个子地点子树当前的世界包围盒（含其全部后代卡片）
        var placements = new List<(MapLocation Loc, RectF Box)>();
        foreach (var loc in selected)
        {
            if (selfChain.Contains(loc.Id)) continue; // 跳过父链上的自己/祖先
            loc.ParentId = parent.Id;
            if (Drawable.LocationFrame(loc) is { } f) placements.Add((loc, f));
        }

        // —— 旧方案（网格平移）已废弃，被下方 ResolveLocationOverlaps 的碰撞推开取代 ——
        // 子地点仍停原位，相交/过近的地方由碰撞检测统一推开，保证全局不重合。

await _maps.SaveAsync();
        Drawable.MarqueeSelected.Clear();
        // 碰撞检测推开：本批归入的新子框若与其它框重叠，整体让开
        ResolveLocationOverlaps();
        await _maps.SaveAsync();
        StatusText = $"已把 {placements.Count} 个地点归入「{parent.Name}」——重叠自动推开";
        Refresh();
    }

    /// <summary>是否 self 是 target 的自身或子孙（用于跳过父子/祖孙重叠判定）。</summary>
    private bool IsSelfOrDescendant(string self, string target)
    {
        var map = _maps.Map;
        for (var cur = target; !string.IsNullOrEmpty(cur); cur = map.Locations.FirstOrDefault(l => l.Id == cur)?.ParentId ?? "")
            if (cur == self) return true;
        return false;
    }

    /// <summary>全局地点框碰撞检测：任何非父子关系的地点框相交或间距过小时，
    /// 沿移动量较小的轴把后者（含其子树全部卡片）推开；迭代至无重叠或达到上限。
    /// 全程在模型层计算框（读 MapScene.X/Y 真实坐标），每轮基于最新位置重算，铺开后可收敛。</summary>
    private void ResolveLocationOverlaps()
    {
        var map = _maps.Map;
        if (map.Locations.Count < 2) return;
        const float minGap = 44;          // 框间最小间距（世界坐标）
        const int maxPass = 40;           // 每轮重算所有框，最多迭代次数

        for (var pass = 0; pass < maxPass; pass++)
        {
            var moved = false;
            // —— 关键：每轮都从模型重算框（TranslateSubtree 已改模型坐标，下一轮必然拿到新位置）——
            var frames = map.Locations
                .Select(l => (Loc: l, R: ModelBox(l)))
                .ToList();
            for (var i = 0; i < frames.Count && !moved; i++)
            {
                for (var j = i + 1; j < frames.Count && !moved; j++)
                {
                    var (a, ra) = frames[i];
                    var (b, rb) = frames[j];
                    if (IsSelfOrDescendant(a.Id, b.Id) || IsSelfOrDescendant(b.Id, a.Id)) continue; // 父子天然嵌套
                    // 重叠/过近的横纵方向所需推开量
                    var dx = Math.Min(ra.Right, rb.Right) - Math.Max(ra.Left, rb.Left) + minGap;
                    var dy = Math.Min(ra.Bottom, rb.Bottom) - Math.Max(ra.Top, rb.Top) + minGap;
                    if (dx > 0 && dy > 0)
                    {
                        // 选最便宜的方向推开 b：水平往远离 a 中心的方向，垂直同理
                        var pushRight = rb.Center.X >= ra.Center.X;
                        var pushDown = rb.Center.Y >= ra.Center.Y;
                        if (dx <= dy) TranslateSubtree(b.Id, pushRight ? dx : -dx, 0);
                        else TranslateSubtree(b.Id, 0, pushDown ? dy : -dy);
                        moved = true;
                    }
                }
            }
            if (!moved) break;
        }
    }

    /// <summary>模型层地点框（世界坐标，含留白）：递归聚合该地点及其全部后代地点的场景卡片（含空后代锚点框）。
    /// 与 Drawable.LocationFrame 保持一致——父框必须几何包住空子框（一环套一环）。</summary>
    private Microsoft.Maui.Graphics.RectF ModelBox(MapLocation loc)
    {
        var map = _maps.Map;
        // 该地点 + 全部后代地点 id
        var ids = new List<string> { loc.Id };
        bool added;
        do
        {
            added = false;
            foreach (var l in map.Locations)
                if (!string.IsNullOrEmpty(l.ParentId) && !ids.Contains(l.Id) && ids.Contains(l.ParentId))
                {
                    ids.Add(l.Id);
                    added = true;
                }
        } while (added);

        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        var found = false;
        foreach (var l in map.Locations)
        {
            if (!ids.Contains(l.Id)) continue;
            foreach (var s in l.Scenes)
            {
                if (s.X < 0 || s.Y < 0) continue; // 未摆放的卡片不参与
                minX = Math.Min(minX, s.X);
                minY = Math.Min(minY, s.Y);
                maxX = Math.Max(maxX, s.X + MapCanvasDrawable.CardW);
                maxY = Math.Max(maxY, s.Y + MapCanvasDrawable.CardH);
                found = true;
            }
        }
        // 空后代（无卡片）也要计入父框包围盒（与绘制一致）
        const double emptyW = 230, emptyH = 100;
        foreach (var l in map.Locations)
        {
            if (!ids.Contains(l.Id)) continue;
            var hasCard = l.Scenes.Count > 0;
            if (hasCard) continue;
            var a = new Point(l.X < 0 ? 0 : l.X, l.Y < 0 ? 0 : l.Y);
            minX = Math.Min(minX, a.X - emptyW / 2);
            minY = Math.Min(minY, a.Y - emptyH / 2);
            maxX = Math.Max(maxX, a.X + emptyW / 2);
            maxY = Math.Max(maxY, a.Y + emptyH / 2);
            found = true;
        }
        // 完全空的地点：以锚点画最小框
        if (!found)
        {
            var anchor = new Point(loc.X < 0 ? 0 : loc.X, loc.Y < 0 ? 0 : loc.Y);
            return new Microsoft.Maui.Graphics.RectF((float)(anchor.X - emptyW / 2), (float)(anchor.Y - emptyH / 2),
                (float)emptyW, (float)emptyH);
        }
        // 与 Drawable.LocationFrame 保持一致：父框顶部多留标题带，避免父子框上沿重合
        const double pad = 56, title = 44;
        var extraHead = ids.Count > 1 ? title + pad : 0;
        return new Microsoft.Maui.Graphics.RectF((float)(minX - pad), (float)(minY - pad - title - extraHead),
            (float)(maxX - minX + pad * 2), (float)(maxY - minY + pad * 2 + title + extraHead));
    }

    /// <summary>地点子树（含全部后代地点）中的卡片同步到画布 node 副本（拖拽整框时实时跟随）。</summary>
    private void SyncNodesInSubtree(string rootId)
    {
        var ids = new HashSet<string> { rootId };
        var map = _maps.Map;
        bool added;
        do
        {
            added = false;
            foreach (var l in map.Locations)
                if (!string.IsNullOrEmpty(l.ParentId) && !ids.Contains(l.Id) && ids.Contains(l.ParentId))
                {
                    ids.Add(l.Id);
                    added = true;
                }
        } while (added);
        foreach (var n in Drawable.Nodes ?? new List<MapCanvasNode>())
            if (ids.Contains(n.Location.Id))
            {
                n.X = n.Scene.X;
                n.Y = n.Scene.Y;
            }
    }

    /// <summary>把某地点及其全部后代地点（含所有卡片位置、锚点）整体平移 dx,dy。</summary>
    private void TranslateSubtree(string rootId, double dx, double dy)
    {
        var map = _maps.Map;
        // 找子树全部地点 id（含自身）
        var ids = new List<string> { rootId };
        bool added;
        do
        {
            added = false;
            foreach (var l in map.Locations)
                if (!string.IsNullOrEmpty(l.ParentId) && !ids.Contains(l.Id) && ids.Contains(l.ParentId))
                {
                    ids.Add(l.Id);
                    added = true;
                }
        } while (added);

        // 平移地点锚点（空地点以锚点定位）
        foreach (var l in map.Locations)
            if (ids.Contains(l.Id))
            {
                l.X += dx;
                l.Y += dy;
                // 地点下所有场景卡片跟随平移
                foreach (var s in l.Scenes)
                {
                    s.X += dx;
                    s.Y += dy;
                }
            }
    }

    /// <summary>计算连线当前应显示的长度文本（用于详情）。</summary>
    private string LengthText(MapEdge e)
    {
        var m = _maps.DisplayLengthMeters(e);
        return m <= 0 ? "虚线（未丈量）" : $"~{(int)m} 米";
    }

    private async Task PickSceneBackgroundAsync(MapScene scene)
    {
        try
        {
            var pick = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = $"为「{scene?.Name}」选择背景图",
                FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    { DevicePlatform.WinUI, new[] { ".png", ".jpg", ".jpeg", ".bmp", ".webp" } },
                    { DevicePlatform.Android, new[] { "image/*" } },
                    { DevicePlatform.iOS, new[] { "public.image" } }
                })
            });
            if (pick is null || scene is null) return;
            var rel = _maps.ImportBackground(pick.FullPath, scene.Id);
            if (rel is null) { StatusText = "背景图复制失败"; return; }
            scene.Background = rel;
            await _maps.SaveAsync();
            StatusText = $"已更新「{scene.Name}」背景图";
        }
        catch (Exception ex)
        {
            App.WriteLog("MapViewModel.PickBackground -> " + ex);
        }
    }
}

/// <summary>画布上的一张场景卡片（位置由 Scene.X/Y 持久化），按地点分组配色。</summary>
public sealed partial class MapCanvasNode : ObservableObject
{
    public MapScene Scene { get; }
    public MapLocation Location { get; }

    public string GroupKey => Location.Id;
    public string Id => Scene.Id;
    public string SceneName => Scene.Name;
    public string LocationName => Location.Name;

    [ObservableProperty] private double _x;
    [ObservableProperty] private double _y;
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _isCurrent;

    public MapCanvasNode(MapScene scene, MapLocation location)
    {
        Scene = scene;
        Location = location;
    }
}

/// <summary>画布上的一个临时节点（可双击为连线端点），坐标由 MapTransit.X/Y 持久化。</summary>
public sealed partial class MapTransitNode : ObservableObject
{
    public MapTransit Transit { get; }

    public string Id => Transit.Id;
    public string Name => Transit.Name;
    public string Note => Transit.Note;

    [ObservableProperty] private double _x;
    [ObservableProperty] private double _y;

    public MapTransitNode(MapTransit transit)
    {
        Transit = transit;
        _x = transit.X;
        _y = transit.Y;
    }
}