using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.Dispatching;
using WarmAsBefore.Controls;
using WarmAsBefore.DesignSystem.Theme;
using WarmAsBefore.Drawables;
using WarmAsBefore.Helpers;
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
    private readonly MaterialLibrary _materials;
    private readonly AudioController _audio;
    private readonly SceneCalibrationService _cal;
    private readonly ImageGenerationService _images;
    private readonly VisionService _vision;
    private readonly TrafficSignalService _signals;
    public MapCanvasDrawable Drawable { get; } = new();

    [ObservableProperty] private ModeKind _mode = ModeKind.View;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private ObservableCollection<MapCanvasNode> _nodes = new();
    [ObservableProperty] private MapCanvasNode? _selectedNode;
    /// <summary>顶栏「天气」按钮的文字：当前这张图按哪儿算天气。</summary>
    [ObservableProperty] private string _weatherModeLabel = "天气";

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
    private Point _panLastScreen;
    private PointF _panStartScreen;   // 这次平移从哪块屏幕坐标起的：点按 vs 拖拽的分界
    private MapCanvasNode? _dragCard;
    private MapTransitNode? _dragTransit;
    private MapEdge? _dragEdge;
    private string? _dragLocId;  // 地点框拖拽：空/整框可整体移动（含全部后代卡片）
    private int _dragWaypoint = -1;
    private Point _dragLastWorld;
    private Point _menuWorld;   // 右键新建时菜单的世界坐标
    // 「圈一块地」进行中的角点（世界坐标）；null = 没在圈。类别与名字在起手时就问定，收边时直接落档。
    private List<Point>? _plotRing;
    private string _plotKind = RegionKinds.Green;
    private string _plotName = "";
    // 红绿灯每秒一跳：画布自己不会刷新，靠这个小定时器催它重画
    private IDispatcherTimer? _signalClock;
    private bool _liveLightsRunning;
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

    public MapViewModel(GameEngine engine, MapService maps, MaterialLibrary materials, AudioController audio,
        SceneCalibrationService cal, ImageGenerationService images, VisionService vision, TrafficSignalService signals,
        TileService tiles, SettingsManager settings)
    {
        _engine = engine;
        _maps = maps;
        _materials = materials;
        _audio = audio;
        _cal = cal;
        _images = images;
        _vision = vision;
        _signals = signals;
        _maps.SceneChanged += OnSceneChanged;
        // 底图瓦片是「铺在纸面底下的一层」：挂在画布上，来源与联网开关都由设置决定。
        AttachTileLayer(tiles, settings);
        Drawable.AttachBgResolver(node => _maps.ResolveBackground(node.Scene));
        Drawable.AttachLocationThumbResolver(loc =>
        {
            var firstBg = loc.Scenes?.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s.Background));
            return firstBg is null ? null : _maps.ThumbnailAbsFor(firstBg);
        });
        _ = InitAsync();
    }

    public void AttachInvalidate(Action invalidate) => _invalidate = invalidate;

    // 重绘合并：拖拽/平移时每个指针事件都要求一次全画布重绘，一秒能到几百次——重绘次数远超 60fps，
    // 画得比屏幕刷得还勤，多出来的全是白烧的帧。这里把「请求重绘」合并成最多 16ms 一次（≈60fps）：
    // 排队期间只置脏、不重复排；落地后若又来过请求就再补一帧，保证松手/滚轮停下时的终值一定画出来。
    private bool _invalidateQueued;
    private bool _invalidateDirty;

    private void Touch()
    {
        if (_invalidate is null) return;
        _invalidateDirty = true;
        if (_invalidateQueued) return;
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null) { FlushInvalidate(); return; }   // 还没有 UI 调度器（极早期/无窗口）：直接画，别把这一帧吞了
        _invalidateQueued = true;
        try { dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(16), FlushInvalidate); }
        catch { FlushInvalidate(); }
    }

    private void FlushInvalidate()
    {
        _invalidateQueued = false;
        _invalidateDirty = false;
        _invalidate?.Invoke();
        // 这次落地之前又改过画面（比如拖拽的最后一帧）：那一次请求不能丢，紧接着再排一帧。
        if (_invalidateDirty) Touch();
    }

    public void Dispose()
    {
        StopFloorAnim();
        StopSignalTicker();
        _maps.SceneChanged -= OnSceneChanged;
        if (_tiles is not null) _tiles.TileArrived -= OnTileArrived;
        if (_settings is not null) _settings.Applied -= OnSettingsApplied;
    }

    /// <summary>
    /// 图上有灯就每秒重画一次：灯的色是「此刻几点」算出来的，画布不自己刷新就会冻在打开那一眼。
    /// 一张没有灯的图不挂这个定时器——白耗一次全表重绘。
    /// </summary>
    private void EnsureSignalTicker()
    {
        var needed = _maps.Map.Edges.Any(e => e.TrafficLight);
        if (!needed)
        {
            StopSignalTicker();
            return;
        }
        if (_signalClock is not null) return;
        var clock = Application.Current?.Dispatcher.CreateTimer();
        if (clock is null) return;
        clock.Interval = TimeSpan.FromSeconds(1);
        clock.Tick += (_, _) => { Touch(); PumpLiveLights(); };
        clock.Start();
        _signalClock = clock;
    }

    private void StopSignalTicker()
    {
        if (_signalClock is null) return;
        _signalClock.Stop();
        _signalClock = null;
    }

    /// <summary>
    /// 开着「红绿灯看高德路况」才去问实时：一轮问答没回来之前不发第二轮——心跳是每秒一跳，
    /// 一屏十几盏灯挨个问，自己就能把自己敲死（服务里同一条路还缓存 60 秒）。
    /// 关着就把手上那份实时色撤掉，灯退回各自的本地周期。
    /// </summary>
    private void PumpLiveLights()
    {
        if (!TrafficSignalService.UseAmap)
        {
            if (Drawable.LiveLights is not null)
            {
                Drawable.LiveLights = null;
                Touch();
            }
            return;
        }
        if (_liveLightsRunning) return;
        var lit = _maps.Map.Edges.Where(e => e.TrafficLight).ToList();
        if (lit.Count == 0) return;
        _liveLightsRunning = true;
        _ = FillLiveLightsAsync(lit);
    }

    private async Task FillLiveLightsAsync(List<MapEdge> lit)    {
        try
        {
            var table = new Dictionary<string, string>(lit.Count);
            foreach (var e in lit)
            {
                // 一次问一盏，串行：并发敲高德那台免费接口不体面，也让心跳有一轮能插进来的空档
                if (!TrafficSignalService.UseAmap) return;
                var state = await _signals.CurrentAsync(e, true);
                table[e.Id] = state.Kind;
            }
            Ui.Post(() =>
            {
                Drawable.LiveLights = table;
                Touch();
            });
        }
        catch (Exception ex) { App.WriteLog("MapViewModel.liveLights -> " + ex); }
        finally { _liveLightsRunning = false; }
    }

    /// <summary>菜单里那句「现在什么色」：实时那份已经到手就用它（秒数是本地周期说的，不跟路况混），否则照本地倒计时。</summary>
    private string LightNowText(MapEdge edge)
    {
        if (Drawable.LiveLights is { } table && table.TryGetValue(edge.Id, out var kind) && kind.Length > 0)
            return SignalKinds.Display(kind) + "·实时路况";
        return SignalClock.Now(edge, true).Display;
    }

    private async Task InitAsync()
    {
        try
        {
            await _maps.InitializeAsync();
            RefreshWeatherModeLabel();
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
        Drawable.Regions = map.Regions.ToList();
        Drawable.SelectedRegionId = map.Regions.Any(r => r.Id == Drawable.SelectedRegionId)
            ? Drawable.SelectedRegionId : null;
        Drawable.MetersPerUnit = map.MetersPerUnit;   // 1 单位=几米，唯一来源是地图定义
        SelectedNode = selId is null ? null : Nodes.FirstOrDefault(n => n.Id == selId);
        Drawable.SelectedEdgeId = selEdge;
        LocationLabel = _maps.CurrentScene is { } cs
            ? $"{_maps.Map.LocationNameOf(cs.Id)} · {cs.Name}"
            : "未知位置";
        OnPropertyChanged(nameof(LocationLabel));
        UpdateFloorBar();
        EnsureSignalTicker();
        TryLandPending();
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
        // 触屏版（手机 / 平板）文案：把"左键/右键/滚轮"换成点按 / 长按 / 双指缩放
        bool touch = DeviceInfo.Platform == DevicePlatform.Android || DeviceInfo.Platform == DevicePlatform.iOS;
        var hint = touch
            ? "【浏览】点卡片 = 出发前往\n【编辑】拖卡片 = 移动；空白处拖动 = 平移画布；长按空白 0.6 秒再拖 = 框选地点（松开高亮）；拖折点 = 改路径\n【连线】先点卡片或临时节点设起点，再点目标完成边；点空白加折点；长按取消\n【涂鸦】按住拖动自由画线，松手自动成边并显示总长；吸附卡片/节点\n\n长按：卡片 / 临时节点 / 连线 / 空白都有菜单。双指 = 缩放。"
            : "【浏览】左键点卡片 = 出发前往\n【编辑】拖拽卡片 = 移动；空白处拖动 = 平移画布；按住空白不放 0.6 秒再拖动 = 框选地点（松开高亮）；拖折点 = 改路径\n【连线】先点卡片或临时节点设起点，再点目标完成边；点空白加折点；右键取消\n【涂鸦】按住左键自由画线，松手自动成边并显示总长；吸附卡片/节点\n\n右键：卡片/临时节点/连线/空白都有菜单。框内右键自动归属该地点。滚轮 = 缩放。";
        await Shell.Current.DisplayAlert("使用说明", hint, "知道了");
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
}
