using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.Dispatching;
using WarmAsBefore.DesignSystem.Theme;
using WarmAsBefore.Drawables;
using WarmAsBefore.Models;

namespace WarmAsBefore.ViewModels;

/// <summary>搜索下拉里的一条：地点 / 场景 / 地块，点一下飞到它那儿。</summary>
public sealed class MapPlaceHit
{
    public required string Title { get; init; }
    public required string Sub { get; init; }
    /// <summary>loc / scene / region</summary>
    public required string Kind { get; init; }
    public string Id { get; init; } = "";
    public double X { get; init; }
    public double Y { get; init; }
    /// <summary>飞过去时至少拉到这个缩放（远景地点给大一点，场景点给小一点）。</summary>
    public double Scale { get; init; } = 1.0;

    public string Glyph => Kind switch
    {
        "scene" => "◉",
        "region" => "▱",
        _ => "▦",
    };
}

/// <summary>
/// 地图页的「外壳」：高德式浮层所依赖的全部状态与命令 —— 搜索、图层抽屉、底部抽屉、
/// 缩放/定位/指南针、状态条与比例尺、区域划分的顶点编辑。
/// 画布与数据层一概不动：这里只读 <see cref="MapCanvasDrawable"/> 与 <see cref="MapService"/>，
/// 把页面按钮接到既有命令上。
/// </summary>
public sealed partial class MapViewModel
{
    // ==================== 图层抽屉 ====================

    [ObservableProperty] private bool _isLayerPanelOpen;

    [RelayCommand]
    private void ToggleLayerPanel()
    {
        IsLayerPanelOpen = !IsLayerPanelOpen;
        if (IsLayerPanelOpen) RefreshTileDrawer();
    }

    [RelayCommand]
    private void CloseLayerPanel() => IsLayerPanelOpen = false;

    /// <summary>这张图的来源（手工 / 开放街道图 / 高德 / AI）——图层面板里要看得见。</summary>
    [ObservableProperty] private string _mapSourceLabel = "手工";

    private void RefreshMapSource() =>
        MapSourceLabel = _maps.Map.Source switch
        {
            "amap" => "高德（检索来的一片城）",
            "osm" => "开放街道图",
            "ai" => "AI 生成",
            _ => "手工绘制",
        };

    /// <summary>换来源：这个仓库没有在线瓦片层，真实城市是高德/开放街道图检索后「导入成一张图」，入口在创建地图页。</summary>
    [RelayCommand]
    private async Task OpenMapSource()
    {
        IsLayerPanelOpen = false;
        await Shell.Current.GoToAsync("mapcreate");
    }

    /// <summary>页面首次挂上时对齐外壳的初始态：浏览态不画编辑框，比例尺与状态条先算一遍。</summary>
    public void InitializeShell()
    {
        RefreshChrome();
        RefreshScaleBar();
        RefreshMapSource();
        StatusStripText = $"当前位置：{LocationLabel} · 缩放 {Drawable.Scale:0.0#}";
    }

    /// <summary>模式切换后画布要决定画不画「编辑器」那层框：浏览态是地图，编辑态才需要框与提示。</summary>
    private void RefreshChrome() =>
        Drawable.MapChrome = Mode != ModeKind.View || IsRegionEditMode;

    // ==================== 搜索 ====================

    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private bool _isSearchOpen;
    [ObservableProperty] private bool _isSearchEmpty;

    public ObservableCollection<MapPlaceHit> SearchHits { get; } = new();

    partial void OnSearchTextChanged(string value) => RunSearch();

    /// <summary>点进搜索框：有字就照当前字先搜一遍，没字给一句空态。</summary>
    [RelayCommand]
    private void OpenSearch()
    {
        IsLayerPanelOpen = false;
        IsSearchOpen = true;
        RunSearch();
    }

    [RelayCommand]
    private void CloseSearch() => IsSearchOpen = false;

    [RelayCommand]
    private void Search() => RunSearch();

    private void RunSearch()
    {
        SearchHits.Clear();
        var q = (SearchText ?? "").Trim();
        if (q.Length == 0)
        {
            IsSearchEmpty = true;
            return;
        }

        var map = _maps.Map;
        var found = new List<(MapPlaceHit Hit, int Rank)>();

        foreach (var loc in map.Locations)
        {
            var hay = loc.Name + " " + loc.Address + " " + loc.RealName;
            var rank = RankOf(loc.Name, q, hay);
            if (rank < 0) continue;
            var frame = Drawable.LocationFrame(loc);
            var x = frame is { } f ? f.Center.X : (loc.X < 0 ? 0 : loc.X);
            var y = frame is { } g ? g.Center.Y : (loc.Y < 0 ? 0 : loc.Y);
            found.Add((new MapPlaceHit
            {
                Title = loc.Name,
                Sub = string.IsNullOrWhiteSpace(loc.Address)
                    ? $"地点 · {loc.Scenes.Count} 个场景"
                    : "地点 · " + loc.Address,
                Kind = "loc",
                Id = loc.Id,
                X = x,
                Y = y,
                Scale = 1.0,
            }, rank));
        }

        foreach (var n in Nodes)
        {
            var rank = RankOf(n.SceneName, q, n.SceneName + " " + n.LocationName);
            if (rank < 0) continue;
            found.Add((new MapPlaceHit
            {
                Title = n.SceneName,
                Sub = "场景 · " + n.LocationName,
                Kind = "scene",
                Id = n.Id,
                X = n.X + MapCanvasDrawable.CardW / 2,
                Y = n.Y + MapCanvasDrawable.CardH / 2,
                Scale = 1.2,
            }, rank));
        }

        foreach (var r in map.Regions)
        {
            if (!r.HasShape) continue;
            var label = string.IsNullOrWhiteSpace(r.Name) ? RegionKinds.Display(r.Kind) : r.Name;
            var rank = RankOf(label, q, label + " " + RegionKinds.Display(r.Kind));
            if (rank < 0) continue;
            double sx = 0, sy = 0;
            foreach (var p in r.Points) { sx += p.X; sy += p.Y; }
            found.Add((new MapPlaceHit
            {
                Title = label,
                Sub = "区域 · " + RegionKinds.Display(r.Kind),
                Kind = "region",
                Id = r.Id,
                X = sx / r.Points.Count,
                Y = sy / r.Points.Count,
                Scale = 0.9,
            }, rank));
        }

        foreach (var item in found.OrderBy(x => x.Rank).ThenBy(x => x.Hit.Title.Length).Take(8))
            SearchHits.Add(item.Hit);

        IsSearchEmpty = SearchHits.Count == 0;
    }

    /// <summary>0=名字就是它，1=名字以它开头，2=名字含它，3=地址/备注含它；-1 不命中。</summary>
    private static int RankOf(string name, string q, string haystack)
    {
        if (string.Equals(name, q, StringComparison.OrdinalIgnoreCase)) return 0;
        if (name.StartsWith(q, StringComparison.OrdinalIgnoreCase)) return 1;
        if (name.Contains(q, StringComparison.OrdinalIgnoreCase)) return 2;
        return haystack.Contains(q, StringComparison.OrdinalIgnoreCase) ? 3 : -1;
    }

    [RelayCommand]
    private void PickSearchResult(MapPlaceHit? hit)
    {
        if (hit is null) return;
        IsSearchOpen = false;
        IsSearchEmpty = false;
        IsLayerPanelOpen = false;
        SearchText = hit.Title;

        if (hit.Kind == "scene" && Nodes.FirstOrDefault(n => n.Id == hit.Id) is { } node)
        {
            SelectedNode = node;
            FlyToWorld(node.X + MapCanvasDrawable.CardW / 2, node.Y + MapCanvasDrawable.CardH / 2,
                Math.Max(Drawable.Scale, hit.Scale));
        }
        else if (hit.Kind == "region" && _maps.Map.RegionById(hit.Id) is { } region)
        {
            SelectedNode = null;
            Drawable.SelectedRegionId = region.Id;
            FlyToWorld(hit.X, hit.Y, Math.Max(Drawable.Scale, hit.Scale));
            if (IsRegionEditMode) BeginRegionEdit(region);
            else OpenRegionSheet(region);
        }
        else
        {
            SelectedNode = null;
            FlyToWorld(hit.X, hit.Y, Math.Max(Drawable.Scale, hit.Scale));
        }
        PingStatusStrip();
    }

    // ==================== 底部抽屉 ====================

    [ObservableProperty] private bool _isSheetOpen;
    [ObservableProperty] private bool _hasSheetTarget;
    [ObservableProperty] private string _sheetTitle = "";
    [ObservableProperty] private string _sheetSub = "";
    [ObservableProperty] private string _sheetMeta = "";
    [ObservableProperty] private bool _sheetIsScene;
    [ObservableProperty] private bool _sheetIsRegion;

    private MapRegion? _sheetRegion;

    /// <summary>选中一张场景卡 → 抽屉里报名字、一句话、距离。</summary>
    private void SyncSheetFromSelection()
    {
        if (SelectedNode is { } n)
        {
            _sheetRegion = null;
            SheetIsScene = true;
            SheetIsRegion = false;
            HasSheetTarget = true;
            SheetTitle = n.SceneName;
            SheetSub = string.IsNullOrWhiteSpace(n.Scene.Note)
                ? $"{n.LocationName} · {n.Scene.Name}"
                : n.Scene.Note;
            var here = string.Equals(n.Id, _maps.CurrentSceneId, StringComparison.Ordinal);
            SheetMeta = here ? "她就在这里" : $"距你约 {DistanceText(n)}";
            IsSheetOpen = true;
        }
        else if (!SheetIsRegion)
        {
            IsSheetOpen = false;
            HasSheetTarget = false;
        }
    }

    private void OpenRegionSheet(MapRegion r)
    {
        _sheetRegion = r;
        SheetIsScene = false;
        SheetIsRegion = true;
        HasSheetTarget = true;
        SheetTitle = string.IsNullOrWhiteSpace(r.Name) ? RegionKinds.Display(r.Kind) : r.Name;
        SheetSub = $"{RegionKinds.Display(r.Kind)} · {RegionSizeText(r)}";
        SheetMeta = $"顶点 {r.Points.Count} 个";
        IsSheetOpen = true;
    }

    private string DistanceText(MapCanvasNode n)
    {
        var here = Nodes.FirstOrDefault(x => string.Equals(x.Id, _maps.CurrentSceneId, StringComparison.Ordinal));
        if (here is null) return "不知道多远";
        var dx = n.X + MapCanvasDrawable.CardW / 2 - (here.X + MapCanvasDrawable.CardW / 2);
        var dy = n.Y + MapCanvasDrawable.CardH / 2 - (here.Y + MapCanvasDrawable.CardH / 2);
        var m = Math.Sqrt(dx * dx + dy * dy) * _maps.Map.MetersPerUnit;
        return m >= 1000 ? $"{m / 1000:0.0} 公里" : $"{m:0} 米";
    }

    [RelayCommand]
    private void CloseSheet()
    {
        IsSheetOpen = false;
        if (SheetIsRegion) { SheetIsRegion = false; HasSheetTarget = false; }
    }

    [RelayCommand]
    private void ExpandSheet() => IsSheetOpen = true;

    /// <summary>「进这里」：与浏览模式点卡片同一条路（最短路径前往该场景）。</summary>
    [RelayCommand]
    private async Task EnterHere()
    {
        if (SelectedNode is not { } node) return;
        IsSheetOpen = false;
        await GoToSceneAsync(node);
        PingStatusStrip();
    }

    /// <summary>「和她说话」：回到主界面（聊天的那个页面）。</summary>
    [RelayCommand]
    private async Task TalkToHer()
    {
        IsSheetOpen = false;
        await Shell.Current.GoToAsync("..");
    }

    /// <summary>抽屉里的改名：场景走既有 rename 菜单命令，地块直接改 Region.Name。</summary>
    [RelayCommand]
    private async Task RenameSheetTarget()
    {
        if (SheetIsScene && SelectedNode is { } node)
        {
            await DispatchAsync("scene", node.Id, "rename");
            return;
        }
        if (_sheetRegion is not { } region) return;
        var name = await Shell.Current.DisplayPromptAsync("区域名字",
            "这块区域叫什么？", "保存", "取消", initialValue: region.Name);
        if (name is null) return;
        region.Name = name.Trim();
        await _maps.SaveAsync();
        StatusText = string.IsNullOrWhiteSpace(region.Name) ? "已清空区域名" : $"区域改名为：{region.Name}";
        Refresh();
        OpenRegionSheet(region);
        PingStatusStrip();
    }

    // ==================== 缩放 / 定位 / 指南针 ====================

    /// <summary>右下「＋」「−」：以视口中心为锚，与滚轮用同一套缩放手感。</summary>
    [RelayCommand]
    private void ZoomIn() => ZoomAtViewCenter(1);

    [RelayCommand]
    private void ZoomOut() => ZoomAtViewCenter(-1);

    private void ZoomAtViewCenter(double direction)
    {
        Zoom(direction, new PointF((float)(_viewW / 2), (float)(_viewH / 2)));
        PingStatusStrip();
    }

    /// <summary>定位：飞回当前角色所在的场景；没有当前位置就把整张图放进视口。</summary>
    [RelayCommand]
    private void LocateMe()
    {
        var node = Nodes.FirstOrDefault(n => string.Equals(n.Id, _maps.CurrentSceneId, StringComparison.Ordinal));
        if (node is null)
        {
            AutoFit();
            StatusText = "还没定当前位置：已把整张图放进视口";
            Touch();
            PingStatusStrip();
            return;
        }
        SelectedNode = node;
        FlyToWorld(node.X + MapCanvasDrawable.CardW / 2, node.Y + MapCanvasDrawable.CardH / 2,
            Math.Max(Drawable.Scale, 1.1));
        PingStatusStrip();
    }

    /// <summary>指南针：这张图不旋转，回正 = 正北朝上 + 全图取景。</summary>
    [RelayCommand]
    private void Compass()
    {
        AutoFit();
        StatusText = "已回正：正北朝上";
        Touch();
        PingStatusStrip();
    }

    // ==================== 飞行动画 ====================

    private IDispatcherTimer? _flyClock;
    private DateTime _flyStart;
    private double _flyMs = 1;
    private double _flyCX0, _flyCY0, _flyS0, _flyCX1, _flyCY1, _flyS1;

    private void FlyToWorld(double worldX, double worldY, double scale)
    {
        StopFloorAnim();
        StopFly();
        var (minScale, maxScale) = ScaleRange();
        var target = Math.Clamp(scale, minScale, maxScale);
        _flyS0 = Drawable.Scale;
        _flyCX0 = (_viewW / 2 - Drawable.OffsetX) / _flyS0;
        _flyCY0 = (_viewH / 2 - Drawable.OffsetY) / _flyS0;
        _flyCX1 = worldX;
        _flyCY1 = worldY;
        _flyS1 = target;
        _flyMs = Math.Max(1, MotionTokens.Ms(MotionTokens.PageSweepMs));
        var clock = Application.Current?.Dispatcher.CreateTimer();
        if (clock is null)
        {
            LandFly();
            return;
        }
        _flyStart = DateTime.UtcNow;
        _flyClock = clock;
        clock.Interval = TimeSpan.FromMilliseconds(MotionTokens.FrameMs);
        clock.Tick += OnFlyTick;
        clock.Start();
    }

    private void OnFlyTick(object? sender, EventArgs e)
    {
        var t = (DateTime.UtcNow - _flyStart).TotalMilliseconds / _flyMs;
        if (t >= 1 || _flyClock is null)
        {
            LandFly();
            return;
        }
        var p = MotionTokens.Mech(Math.Clamp(t, 0, 1));
        var s = _flyS0 + (_flyS1 - _flyS0) * p;
        var cx = _flyCX0 + (_flyCX1 - _flyCX0) * p;
        var cy = _flyCY0 + (_flyCY1 - _flyCY0) * p;
        Drawable.Scale = s;
        Drawable.OffsetX = _viewW / 2 - cx * s;
        Drawable.OffsetY = _viewH / 2 - cy * s;
        Touch();
    }

    private void LandFly()
    {
        _flyClock?.Stop();
        if (_flyClock is not null) _flyClock.Tick -= OnFlyTick;
        _flyClock = null;
        Drawable.Scale = _flyS1;
        Drawable.OffsetX = _viewW / 2 - _flyCX1 * _flyS1;
        Drawable.OffsetY = _viewH / 2 - _flyCY1 * _flyS1;
        RefreshScaleBar();
        Touch();
    }

    private void StopFly()
    {
        if (_flyClock is null) return;
        _flyClock.Stop();
        _flyClock.Tick -= OnFlyTick;
        _flyClock = null;
    }

    // ==================== 状态条与比例尺 ====================

    [ObservableProperty] private bool _isStatusStripVisible;
    [ObservableProperty] private string _statusStripText = "";
    [ObservableProperty] private string _scaleBarLabel = "50 米";
    [ObservableProperty] private double _scaleBarWidth = 80;

    private IDispatcherTimer? _statusClock;

    /// <summary>拖动/缩放的这一下亮一条状态条，停手 1.6 秒后自己淡掉。</summary>
    private void PingStatusStrip()
    {
        StatusStripText = $"当前位置：{LocationLabel} · 缩放 {Drawable.Scale:0.0#}";
        IsStatusStripVisible = true;
        RefreshScaleBar();
        _statusClock?.Stop();
        _statusClock ??= Application.Current?.Dispatcher.CreateTimer();
        if (_statusClock is null) return;
        _statusClock.Interval = TimeSpan.FromMilliseconds(1600);
        _statusClock.Tick -= OnStatusTick;
        _statusClock.Tick += OnStatusTick;
        _statusClock.Start();
    }

    private void OnStatusTick(object? sender, EventArgs e)
    {
        _statusClock?.Stop();
        IsStatusStripVisible = false;
    }

    /// <summary>比例尺：挑一个 1/2/5 档的整数米，让黑条落在 36–160 像素之间。</summary>
    private void RefreshScaleBar()
    {
        var metersPerPx = ScaleBarMetersPerPixel();
        // 底图能拉到 z3（整块大陆），所以档位要一路铺到几千公里：否则条宽被钳到 36px，
        // 写的却是 10 公里 —— 成了一个错误的比例尺。
        var steps = new[]
        {
            1d, 2, 5, 10, 20, 50, 100, 200, 500, 1000, 2000, 5000,
            10000, 20000, 50000, 100000, 200000, 500000, 1000000, 2000000, 5000000
        };
        var target = metersPerPx * 96;
        var pick = steps[^1];
        foreach (var s in steps)
        {
            if (s < target) continue;
            pick = s;
            break;
        }
        ScaleBarWidth = Math.Clamp(pick / metersPerPx, 36, 160);
        ScaleBarLabel = pick >= 1000
            ? $"{pick / 1000:0.#} 公里"
            : $"{pick.ToString(CultureInfo.InvariantCulture)} 米";
        RefreshTileStatus();
    }

    // ==================== 区域划分：顶点编辑 ====================

    [ObservableProperty] private bool _isRegionEditMode;
    [ObservableProperty] private string _regionEditLabel = "区域划分";

    private MapRegion? _editRegion;
    private int _editVertex = -1;
    private bool _editDragged;
    private int _lastVertex = -1;
    private DateTime _lastVertexAt = DateTime.MinValue;

    // 双击放大：判的是「同一处、350ms 内两下」
    private PointF _tapDownScreen;
    private PointF? _lastTapScreen;
    private DateTime _lastTapAt = DateTime.MinValue;

    [RelayCommand]
    private void ToggleRegionEdit()
    {
        IsLayerPanelOpen = false;
        if (IsRegionEditMode)
        {
            FinishRegionEdit();
            return;
        }
        IsRegionEditMode = true;
        RegionEditLabel = "● 区域划分";
        Drawable.RegionEditMode = true;
        RefreshChrome();
        var picked = Drawable.SelectedRegionId is { } id ? _maps.Map.RegionById(id) : null;
        if (picked is not null) BeginRegionEdit(picked);
        else StatusText = "区域划分：点一块地（绿地/水面/分区）开始编辑；拖动顶点改形状";
        Touch();
        PingStatusStrip();
    }

    [RelayCommand]
    private void FinishRegionEdit()
    {
        _editVertex = -1;
        _editRegion = null;
        Drawable.RegionEditId = null;
        IsRegionEditMode = false;
        RegionEditLabel = "区域划分";
        Drawable.RegionEditMode = false;
        RefreshChrome();
        StatusText = "区域划分已结束";
        Touch();
        PingStatusStrip();
    }

    private void BeginRegionEdit(MapRegion r)
    {
        _editRegion = r;
        _editVertex = -1;
        Drawable.RegionEditId = r.Id;
        Drawable.SelectedRegionId = r.Id;
        SelectedNode = null;
        OpenRegionSheet(r);
        StatusText = $"正在改「{(string.IsNullOrWhiteSpace(r.Name) ? RegionKinds.Display(r.Kind) : r.Name)}」：拖顶点 · 点边中点加点 · 双击顶点删点";
        Touch();
    }

    /// <summary>画布按下时先问这里：区域划分中的顶点拖动优先于一切。</summary>
    private bool RegionEditStart(PointF screen)
    {
        if (!IsRegionEditMode) return false;
        var world = Drawable.ScreenToWorld(new Point(screen.X, screen.Y));
        var region = _editRegion;
        if (region is not null)
        {
            var vi = Drawable.HitRegionHandle(region, new Point(screen.X, screen.Y));
            if (vi >= 0)
            {
                var now = DateTime.UtcNow;
                if (_lastVertex == vi && (now - _lastVertexAt).TotalMilliseconds < 400)
                {
                    _lastVertex = -1;
                    DeleteRegionVertex(region, vi);
                    return true;
                }
                _lastVertex = vi;
                _lastVertexAt = now;
                _editVertex = vi;
                _editDragged = false;
                return true;
            }
            var seg = Drawable.HitRegionEdge(region, new Point(screen.X, screen.Y));
            if (seg >= 0)
            {
                var at = seg + 1;
                region.Points.Insert(at, new MapWaypoint { X = world.X, Y = world.Y });
                _editVertex = at;
                _editDragged = true;
                StatusText = "已加一个顶点（松手落盘）";
                Touch();
                return true;
            }
        }

        var under = Drawable.HitRegion(world);
        if (under is null) return false;      // 空白仍然可以平移看图
        BeginRegionEdit(under);
        return true;
    }

    private bool RegionEditDrag(PointF screen)
    {
        if (!IsRegionEditMode || _editVertex < 0 || _editRegion is not { } r) return false;
        if (_editVertex >= r.Points.Count) return false;
        var world = Drawable.ScreenToWorld(new Point(screen.X, screen.Y));
        var p = r.Points[_editVertex];
        p.X = world.X;
        p.Y = world.Y;
        _editDragged = true;
        Touch();
        return true;
    }

    private bool RegionEditEnd()
    {
        if (!IsRegionEditMode) return false;
        if (_editVertex < 0) return false;
        _editVertex = -1;
        if (_editDragged)
        {
            _editDragged = false;
            _ = _maps.SaveAsync();
            if (_editRegion is { } r) OpenRegionSheet(r);
        }
        Touch();
        return true;
    }

    private void DeleteRegionVertex(MapRegion r, int index)
    {
        var min = RegionKinds.IsLinear(r.Kind) ? 2 : 3;
        if (r.Points.Count <= min)
        {
            StatusText = $"再删就不成形了（最少 {min} 个顶点）";
            Touch();
            return;
        }
        r.Points.RemoveAt(index);
        _ = _maps.SaveAsync();
        StatusText = $"已删掉一个顶点（还剩 {r.Points.Count} 个）";
        OpenRegionSheet(r);
        Touch();
    }
}
