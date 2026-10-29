using System.Text.Json;
using CommunityToolkit.Maui.Storage;
using WarmAsBefore.Models;

namespace WarmAsBefore.Services;

/// <summary>
/// 地图服务（场景图）：
/// - 持久化地图定义（地点→场景，场景间连线）
/// - 寻路：BFS 找出所有最短路径，多条时随机选择一条
/// - 行走：按路线逐段行进，每段耗时后切换"当前场景"，通过事件通知 UI
/// - 背景图管理：用户选择的图片/视频复制到本地 maps/ 目录，视频自动生成首帧缩略图
/// - 导出 / 导入：JSON 文件（FileSaver / FilePicker）
/// 另：对话中 AI 通过【移动:场景名】标记请求移动，经本服务执行。
/// </summary>
public sealed class MapService
{
    private readonly StorageProvider _store;
    private readonly Random _rnd = new();
    private MapDefinition _map = new();
    private string _currentSceneId = "";
    private bool _isLoaded;

    private const string MapKey = "mapdata";
    /// <summary>每经过一段场景的等待毫秒（地图越大等待越长，体现"走路"）。</summary>
    public int WalkMsPerSegment { get; set; } = 1400;

    /// <summary>角色到达新场景（已切到该场景，UI 可更新立绘/背景/位置标签）。</summary>
    public event Action<MapScene>? SceneChanged;

    /// <summary>一段路线开始行走（在等待切场景之前触发）。主界面用它启动走路动作。</summary>
    public event Action? WalkSegmentStarted;

    /// <summary>整条路线结束（含取消/异常）。主界面用它收回走路动作。</summary>
    public event Action? WalkRouteCompleted;

    /// <summary>地图数据变化（编辑/导入后刷新 UI）。</summary>
    public event Action? MapChanged;

    public MapService(StorageProvider store) => _store = store;

    public MapDefinition Map => _map;
    public bool IsLoaded => _isLoaded;
    public string CurrentSceneId => _currentSceneId;
    public MapScene? CurrentScene => _map.SceneById(_currentSceneId);

    // ==================== 初始化 ====================

    private Task? _loading;
    private readonly object _loadGate = new();

    public Task InitializeAsync()
    {
        // 「读过」以读完为准：布尔标记要等 await 之后才置位，十几个入口会同时挤进来重复载图，
        // 后完成的那份直接把还没落盘的地图编辑盖掉。
        lock (_loadGate) return _loading ??= LoadCoreAsync();
    }

    private async Task LoadCoreAsync()
    {
        var loaded = await _store.Load<MapDefinition>(MapKey);
        _map = loaded ?? BuildDefaultMap();
        Sanitize();
        _isLoaded = true;
        _currentSceneId = _map.StartSceneId;
        if (_map.SceneById(_currentSceneId) is null)
            _currentSceneId = _map.AllScenes.FirstOrDefault()?.Id ?? "";
        App.WriteLog($"MapService: initialized, scenes={_map.AllScenes.Count()}, edges={_map.Edges.Count}");
    }

    /// <summary>首次启动的默认地图：四个地点（每个一个场景）全连通，保证任意两点可达。</summary>
    public static MapDefinition BuildDefaultMap()
    {
        var home = new MapLocation { Name = "家" };
        var homeRoom = new MapScene { Name = "温馨住所", BackgroundColor = "#5D4037" };
        var park = new MapLocation { Name = "公园" };
        var parkScene = new MapScene { Name = "公园长椅", BackgroundColor = "#33691E" };
        var cafe = new MapLocation { Name = "咖啡馆" };
        var cafeScene = new MapScene { Name = "咖啡馆", BackgroundColor = "#6D4C41" };
        var beach = new MapLocation { Name = "海边" };
        var beachScene = new MapScene { Name = "海滨栈道", BackgroundColor = "#1565C0" };
        home.Scenes.Add(homeRoom);
        park.Scenes.Add(parkScene);
        cafe.Scenes.Add(cafeScene);
        beach.Scenes.Add(beachScene);

        var map = new MapDefinition
        {
            Name = "温暖地图",
            StartSceneId = homeRoom.Id
        };
        map.Locations.Add(home);
        map.Locations.Add(park);
        map.Locations.Add(cafe);
        map.Locations.Add(beach);
        map.Edges.Add(new MapEdge { A = homeRoom.Id, B = parkScene.Id });
        map.Edges.Add(new MapEdge { A = homeRoom.Id, B = cafeScene.Id });
        map.Edges.Add(new MapEdge { A = parkScene.Id, B = beachScene.Id });
        map.Edges.Add(new MapEdge { A = cafeScene.Id, B = beachScene.Id });
        return map;
    }

    /// <summary>清理：去掉指向不存在顶点的边、重复边（顶点=场景或临时节点）。</summary>
    private void Sanitize()
    {
        var ids = _map.AllPoints.Select(p => p.Id).ToHashSet();
        var dedup = new HashSet<(string, string)>();
        _map.Edges.RemoveAll(e => !ids.Contains(e.A) || !ids.Contains(e.B) || !dedup.Add((e.A, e.B)));
        if (_map.Locations.Count == 0)
            _map = BuildDefaultMap();
        SanitizePlacements();
    }

    /// <summary>
    /// 清理失配的标定：标定是绑定在"哪一张背景图"上的，图片换了旧标定就没有意义。
    ///
    /// 注意这里**只清理"图不对"的情况，不清理"标定不完整"的情况**——
    /// 后者留着能让用户下次打开时接着标，而不是从零重画。
    /// 不完整的标定由 SceneCalibrationService.NeedsCalibration 负责拦。
    /// </summary>
    private void SanitizePlacements()
    {
        foreach (var scene in _map.AllScenes)
        {
            var p = scene.Placement;
            if (p is null) continue;

            var abs = ResolveBackground(scene);
            if (string.IsNullOrEmpty(abs))
            {
                // 背景文件没了，标定也就无从谈起
                scene.Placement = null;
                continue;
            }

            // 路径变了 = 换了另一张图，直接作废
            if (!string.IsNullOrWhiteSpace(p.SourceBackground) && p.SourceBackground != scene.Background)
            {
                App.WriteLog($"MapService: 场景「{scene.Name}」背景已更换，旧标定作废");
                scene.Placement = null;
                continue;
            }

            // 路径没变但文件内容变了（同路径覆盖）：靠长度+时间指纹识别
            if (string.IsNullOrWhiteSpace(p.SourceBackground))
            {
                p.SourceBackground = scene.Background;   // 老数据/素材包导入：补上来源标记
            }
            var stamp = StampOf(abs);
            if (!string.IsNullOrWhiteSpace(p.SourceStamp) && !string.IsNullOrWhiteSpace(stamp)
                && p.SourceStamp != stamp)
            {
                App.WriteLog($"MapService: 场景「{scene.Name}」背景文件已变化，旧标定作废");
                scene.Placement = null;
            }
            else if (string.IsNullOrWhiteSpace(p.SourceStamp))
            {
                p.SourceStamp = stamp;
            }
        }
    }

    public async Task SaveAsync()
    {
        InvalidateReach();
        await _store.Save(MapKey, _map);
    }

    // ==================== 寻路 ====================

    /// <summary>路径顶点（场景或临时节点）。</summary>
    public readonly record struct MapNodeRef(string Id, string Name, bool IsScene);

    /// <summary>
    /// 这条路能不能从 from 走向 to。
    /// 单行道只允许 A→B；未丈量（Length=0，画虚线）的边照走，只是没里程数。
    /// </summary>
    public static bool Passable(MapEdge e, string from, string to) =>
        !(e.OneWay && from == e.B && to == e.A);

    /// <summary>两点之间的边（有向查：单行道的反向不通）。找不到返回 null。</summary>
    public MapEdge? EdgeBetween(string from, string to)
    {
        foreach (var (nb, e) in AdjacencyOf(from))
            if (nb == to) return e;
        return null;
    }

    private Dictionary<string, List<(string Nb, MapEdge Edge)>>? _adj;

    /// <summary>
    /// 邻接表：寻路原本每条边都要全表扫一遍连线，城市级地图（几千条路）会直接扫跪。
    /// 建一次索引，图一变（保存/导入/通知变化）就作废。
    /// </summary>
    private Dictionary<string, List<(string Nb, MapEdge Edge)>> Adjacency()
    {
        if (_adj is not null) return _adj;
        var a = new Dictionary<string, List<(string, MapEdge)>>();
        void Add(string from, string to, MapEdge e)
        {
            if (_map.PointNameById(from) is null || _map.PointNameById(to) is null) return;   // 悬空连线
            if (!a.TryGetValue(from, out var list)) a[from] = list = new List<(string, MapEdge)>();
            list.Add((to, e));
        }
        foreach (var e in _map.Edges)
        {
            Add(e.A, e.B, e);
            if (!e.OneWay) Add(e.B, e.A, e);
        }
        _adj = a;
        return a;
    }

    private IEnumerable<(string Nb, MapEdge Edge)> AdjacencyOf(string from) =>
        Adjacency().TryGetValue(from, out var list) ? list : Enumerable.Empty<(string, MapEdge)>();

    private Dictionary<string, Point>? _points;

    /// <summary>顶点坐标索引（场景 + 临时节点）：算边长时不再逐个场景线性找。</summary>
    private Dictionary<string, Point> Points()
    {
        if (_points is not null) return _points;
        var d = new Dictionary<string, Point>();
        foreach (var s in _map.AllScenes) d[s.Id] = new Point(s.X < 0 ? 0 : s.X, s.Y < 0 ? 0 : s.Y);
        foreach (var t in _map.Transits) d[t.Id] = new Point(t.X < 0 ? 0 : t.X, t.Y < 0 ? 0 : t.Y);
        _points = d;
        return d;
    }

    private Dictionary<string, double>? _seconds;

    /// <summary>图变了就把三份缓存一起作废。</summary>
    private void InvalidateReach()
    {
        _reach = null;
        _reachFrom = "";
        _adj = null;
        _points = null;
        _seconds = null;
    }

    /// <summary>算距离用的米数：手量的优先；未丈量按几何长度 × 比例尺兜底。</summary>
    public double CostMeters(MapEdge e) =>
        e.Length > 0 ? e.Length : Math.Max(1, Math.Round(EdgeLengthPixels(e) * _map.MetersPerUnit));

    /// <summary>
    /// 沿这条路要走多久（秒）：里程 ÷ 限速，红绿灯多等一趟，收费站停一次车。
    /// 户型内（限速 0）不计时——那是「推门就进」，不是「走五分钟」。
    /// </summary>
    public double TraversalSeconds(MapEdge e)
    {
        if (_seconds is not null && _seconds.TryGetValue(e.Id, out var cached)) return cached;
        var kmh = e.EffectiveSpeedKmh;
        double sec;
        if (kmh <= 0) sec = 0;
        else
        {
            sec = CostMeters(e) / (kmh * 1000.0 / 3600.0);
            if (e.TrafficLight) sec += 25;
            if (e.Toll) sec += 40;
            sec = Math.Max(1, sec);
        }
        (_seconds ??= new Dictionary<string, double>())[e.Id] = sec;
        return sec;
    }

    /// <summary>
    /// 图上一趟要走的时间（秒）。不可达返回 null。
    /// </summary>
    public double? TravelSeconds(string fromId, string toId)
    {
        var route = FindShortest(fromId, toId);
        return route is null || route.Count < 2 ? null : RouteSeconds(route);
    }

    /// <summary>一条路线的总耗时（秒）。</summary>
    public double RouteSeconds(IReadOnlyList<MapNodeRef> route)
    {
        double sec = 0;
        for (var i = 0; i + 1 < route.Count; i++)
            sec += EdgeBetween(route[i].Id, route[i + 1].Id) is { } e ? TraversalSeconds(e) : 0;
        return sec;
    }

    /// <summary>最快路径（按耗时加权：单行道绕行、红绿灯与收费站计罚；多条同代价路径随机选一条）。null=不可达。</summary>
    public List<MapNodeRef>? FindShortest(string fromId, string toId)
    {
        if (fromId == toId) return new List<MapNodeRef> { Resolve(fromId) };
        var cost = ReachFrom(fromId).Sec;
        if (!cost.ContainsKey(toId)) return null;

        // 摊开所有同代价路径，随机选一条：地图上不该总走同一根线。
        var target = cost[toId];
        var paths = new List<List<MapNodeRef>>();
        var trail = new List<MapNodeRef>();
        var onTrail = new HashSet<string>();
        void Dfs(string node)
        {
            if (paths.Count >= MaxEqualPaths) return;
            trail.Add(Resolve(node));
            onTrail.Add(node);
            if (node == toId) paths.Add(new List<MapNodeRef>(trail));
            else
            {
                foreach (var (nbId, _, _) in _map.NeighborsOfPoint(node))
                {
                    // 同代价推进：这一段恰好把累计时间推到下一点的 cost，且不能绕回已在路上的点（户型内 0 秒会成环）。
                    if (onTrail.Contains(nbId) || !cost.TryGetValue(nbId, out var nc)) continue;
                    if (EdgeBetween(node, nbId) is not { } e) continue;
                    if (Math.Abs(cost[node] + TraversalSeconds(e) - nc) > 1e-6) continue;
                    if (Math.Abs(nc - target) < 1e-6 && nbId != toId) continue;
                    Dfs(nbId);
                    if (paths.Count >= MaxEqualPaths) break;
                }
            }
            onTrail.Remove(node);
            trail.RemoveAt(trail.Count - 1);
        }
        Dfs(fromId);
        return paths.Count == 0 ? null : paths[_rnd.Next(paths.Count)];
    }

    /// <summary>同代价路径最多枚举这么多条——大地图上等长路线会组合爆炸，够用就收手。</summary>
    private const int MaxEqualPaths = 24;

    /// <summary>一次遍历得到的可达结果：从起点到每个顶点的耗时（秒）与里程（米），外加来路。</summary>
    public sealed class Reach
    {
        internal readonly Dictionary<string, double> Sec = new();
        internal readonly Dictionary<string, double> Meters = new();
        internal readonly Dictionary<string, string> Prev = new();

        public bool CanReach(string id) => Sec.ContainsKey(id);
        public double? SecondsTo(string id) => Sec.TryGetValue(id, out var s) && s > 0 ? s : null;
        public double? MetersTo(string id) => Meters.TryGetValue(id, out var m) && m > 0 ? m : null;

        /// <summary>来路顶点链（起点→终点）；不可达返回空链。</summary>
        public List<string> PathTo(string id)
        {
            var chain = new List<string>();
            if (!Sec.ContainsKey(id)) return chain;
            var cur = id;
            while (Prev.TryGetValue(cur, out var p))
            {
                chain.Add(cur);
                cur = p;
            }
            chain.Add(cur);
            chain.Reverse();
            return chain;
        }
    }

    private Reach? _reach;
    private string _reachFrom = "";

    /// <summary>
    /// 从一个顶点跑一遍 Dijkstra（按耗时），结果缓存一份。
    /// 城市级地图动辄几百个场景，AI 上下文要「到每一处的距离」——只跑这一次，别一个目标跑一遍。
    /// </summary>
    public Reach ReachFrom(string fromId)
    {
        if (_reach is not null && _reachFrom == fromId) return _reach;

        var r = new Reach();
        r.Sec[fromId] = 0;
        r.Meters[fromId] = 0;
        var done = new HashSet<string>();
        var cmp = Comparer<(double Sec, string Id)>.Create((a, b) =>
        {
            var c = a.Sec.CompareTo(b.Sec);
            return c != 0 ? c : string.CompareOrdinal(a.Id, b.Id);
        });
        var frontier = new SortedSet<(double Sec, string Id)>(cmp) { (0, fromId) };
        while (frontier.Count > 0)
        {
            var cur = frontier.Min;
            frontier.Remove(cur);
            if (!done.Add(cur.Id)) continue;
            foreach (var (nbId, _, _) in _map.NeighborsOfPoint(cur.Id))
            {
                if (done.Contains(nbId) || EdgeBetween(cur.Id, nbId) is not { } e) continue;
                var via = r.Sec[cur.Id] + TraversalSeconds(e);
                if (r.Sec.TryGetValue(nbId, out var old) && old <= via) continue;
                r.Sec[nbId] = via;
                r.Meters[nbId] = r.Meters[cur.Id] + DisplayLengthMeters(e);
                r.Prev[nbId] = cur.Id;
                frontier.RemoveWhere(x => x.Id == nbId);
                frontier.Add((via, nbId));
            }
        }
        _reach = r;
        _reachFrom = fromId;
        return r;
    }

    private MapNodeRef Resolve(string id) =>
        _map.SceneById(id) is { } s ? new MapNodeRef(s.Id, s.Name, true)
        : _map.TransitById(id) is { } t ? new MapNodeRef(t.Id, t.Name, false)
        : new MapNodeRef(id, "", false);

    /// <summary>沿线行走：只把目标为场景的顶点切换为"当前场景"（临时节点仅作通路，不停留）。</summary>
    public async Task WalkRouteAsync(IReadOnlyList<MapNodeRef> route, CancellationToken ct = default)
    {
        if (route is null || route.Count == 0) return;
        try
        {
            double pendingMs = 0;
            var prevId = _currentSceneId;
            foreach (var node in route)
            {
                if (node.Id != prevId) pendingMs += WalkAnimMsFor(EdgeBetween(prevId, node.Id));
                prevId = node.Id;
                if (!node.IsScene) continue;   // 临时节点：只路过，这段时间攒到下一站
                if (node.Id == _currentSceneId) continue;
                ct.ThrowIfCancellationRequested();
                WalkSegmentStarted?.Invoke();
                await Task.Delay((int)Math.Clamp(pendingMs, 260, 2400), ct);
                pendingMs = 0;
                _currentSceneId = node.Id;
                SceneChanged?.Invoke(_map.SceneById(node.Id)!);
            }
        }
        finally
        {
            WalkRouteCompleted?.Invoke();
        }
    }

    /// <summary>路线耗时的人话（"约 6 分钟"）；全是户型内那种不计时的小路就返回空串。</summary>
    public string RouteTimeText(IReadOnlyList<MapNodeRef> route)
    {
        var sec = RouteSeconds(route);
        if (sec <= 0) return "";
        if (sec < 60) return $"约 {sec:0} 秒";
        var min = sec / 60;
        return min < 60 ? $"约 {min:0.#} 分钟" : $"约 {min / 60:0.#} 小时";
    }

    /// <summary>按场景名或 ID 移动（AI/UI 入口）：解析目标 → 寻路 → 逐段行走。</summary>
    /// <returns>描述移动过程的文本（供会话展示）；返回空串表示原地或失败。</returns>
    public async Task<string> MoveToAsync(string nameOrId, CancellationToken ct = default)
    {
        if (!_isLoaded) await InitializeAsync();
        var cur = _map.SceneById(_currentSceneId) ?? _map.AllScenes.FirstOrDefault();
        var target = _map.SceneById(nameOrId) ?? _map.SceneByName(nameOrId);
        if (cur is null || target is null) return "";
        if (cur.Id == target.Id) return $"（已经身在 {target.Name}）";

        var route = FindShortest(cur.Id, target.Id);
        if (route is null) return $"（{cur.Name} 与 {target.Name} 之间还没有开通路线）";

        var when = RouteTimeText(route);
        await WalkRouteAsync(route, ct);
        var pathText = string.Join(" → ", route.Select(r => r.Name));
        return $"（已沿路线走到 {target.Name}：{pathText}{(when.Length > 0 ? $"，{when}" : "")}）";
    }

    /// <summary>顶点（场景/临时节点）的画布坐标。</summary>
    public Point WorldPointOf(string nodeId) => Points().TryGetValue(nodeId, out var p) ? p : default;

    /// <summary>边的几何长度（px，按路径折点求和），再换算成米。</summary>
    public double EdgeLengthPixels(MapEdge e)
    {
        var pts = Points();
        if (!pts.TryGetValue(e.A, out var start) || !pts.TryGetValue(e.B, out var finish)) return 0;
        double px = 0;
        var prev = start;
        foreach (var w in e.Waypoints)
        {
            px += Math.Sqrt(Math.Pow(w.X - prev.X, 2) + Math.Pow(w.Y - prev.Y, 2));
            prev = new Point(w.X, w.Y);
        }
        px += Math.Sqrt(Math.Pow(finish.X - prev.X, 2) + Math.Pow(finish.Y - prev.Y, 2));
        return px;
    }

    /// <summary>显示用长度（米）：手动值 &gt; 0 用之；=0 表示未丈量（虚线）；-1 自动按几何换算。</summary>
    public double DisplayLengthMeters(MapEdge e)
    {
        if (e.Length > 0) return e.Length;
        if (e.Length == 0) return 0;
        return Math.Max(1, Math.Round(EdgeLengthPixels(e) * _map.MetersPerUnit));
    }

    /// <summary>
    /// 动画上一段该演多久（毫秒）：按真实通行时间压缩，不是按段数硬等。
    /// 户型内不计时，只给一个「推门就进」的短帧；最长 2.4 秒，免得一条高速把界面卡住。
    /// </summary>
    public int WalkAnimMsFor(MapEdge? e)
    {
        if (e is null) return WalkMsPerSegment;
        if (e.EffectiveSpeedKmh <= 0) return 260;
        return (int)Math.Clamp(TraversalSeconds(e) * 130, 320, 2400);
    }

    /// <summary>该边是否以虚线绘制（长度=0 → 未丈量）。</summary>
    public bool IsDashed(MapEdge e) => e.Length == 0;

    /// <summary>从当前场景到指定场景的里程（米）。不可达或没丈量返回 null。</summary>
    public double? DistanceTo(string targetSceneId)
    {
        if (!_isLoaded) return null;
        return ReachFrom(_currentSceneId).MetersTo(targetSceneId);
    }

    /// <summary>从当前场景到指定场景要走多久（人话）。不可达或不计时返回空串。</summary>
    public string TravelTimeTextTo(string targetSceneId)
    {
        if (!_isLoaded) return "";
        var sec = ReachFrom(_currentSceneId).SecondsTo(targetSceneId);
        if (sec is not double s) return "";
        return s < 60 ? $"约 {s:0} 秒" : s < 3600 ? $"约 {s / 60:0.#} 分钟" : $"约 {s / 3600:0.#} 小时";
    }

    /// <summary>获取当前场景到其他所有场景的距离摘要（供 AI 上下文使用）。</summary>
    public string BuildDistanceContext()
    {
        if (!_isLoaded || _map.AllScenes.Count() <= 1) return "";
        // 一次遍历拿到全图可达性：城市级地图几百个场景，逐个跑 Dijkstra 会把回合卡在提示词前面。
        var reach = ReachFrom(_currentSceneId);
        // 场景→地点名先摊平成一张表：原先每个场景都要重扫一遍地点树，几百个场景时这段本身就卡。
        var ownerName = new Dictionary<string, string>();
        foreach (var loc in _map.Locations)
            foreach (var sc in loc.Scenes)
                ownerName[sc.Id] = loc.Name;
        var parts = new List<string>();
        foreach (var sc in _map.AllScenes)
        {
            if (sc.Id == _currentSceneId) continue;
            var d = reach.MetersTo(sc.Id);
            var t = reach.SecondsTo(sc.Id);
            var locName = ownerName.TryGetValue(sc.Id, out var ln) ? ln : "";
            if (d is double m && m > 0)
            {
                var dist = m >= 1000 ? $"{m / 1000:F1}公里" : $"{(int)m}米";
                var time = t is double s2 ? (s2 < 60 ? $"{s2:0}秒" : $"{s2 / 60:0.#}分钟") : "";
                parts.Add($"{locName}·{sc.Name}({dist}{(time.Length > 0 ? $"，{time}" : "")})");
            }
            else if (reach.CanReach(sc.Id))
                parts.Add($"{locName}·{sc.Name}(推门就到)");
            else
                parts.Add($"{locName}·{sc.Name}(无法到达)");
        }
        return parts.Count > 0 ? $"当前位置距离：{string.Join("，", parts)}。" : "";
    }

    // ==================== 编辑 ====================

    public MapLocation AddLocation(string name, string parentId = "")
    {
        var loc = new MapLocation
        {
            Name = string.IsNullOrWhiteSpace(name) ? "新地点" : name.Trim(),
            ParentId = string.IsNullOrWhiteSpace(parentId) ? "" : parentId
        };
        _map.Locations.Add(loc);
        _ = SaveAsync();
        return loc;
    }

    /// <summary>
    /// 按名字取地点，不存在才新建。
    /// （<see cref="AddLocation"/> 总是新建，用它做素材包合并会每次都多出一个同名地点。）
    /// </summary>
    public MapLocation EnsureLocation(string name, string parentId = "")
    {
        var loc = _map.Locations.FirstOrDefault(l => l.Name == name);
        return loc ?? AddLocation(name, parentId);
    }

    public MapScene AddScene(string locationId, string name)
    {
        var loc = _map.Locations.FirstOrDefault(l => l.Id == locationId);
        if (loc is null) return AddScene(_map.Locations.FirstOrDefault()?.Id, name);
        var scene = new MapScene { Name = string.IsNullOrWhiteSpace(name) ? "新房间" : name.Trim() };
        loc.Scenes.Add(scene);
        _ = SaveAsync();
        return scene;
    }

    public void RemoveScene(string sceneId)
    {
        _map.Edges.RemoveAll(e => e.A == sceneId || e.B == sceneId);
        foreach (var l in _map.Locations)
            l.Scenes.RemoveAll(s => s.Id == sceneId);
        if (_currentSceneId == sceneId)
            _currentSceneId = _map.AllScenes.FirstOrDefault()?.Id ?? "";
        _ = SaveAsync();
    }

    public void RemoveLocation(string locationId)
    {
        // 递归删除所有后代地点（子地点及其场景/连线）
        var toRemove = new List<MapLocation>();
        void Collect(MapLocation loc)
        {
            toRemove.Add(loc);
            foreach (var child in _map.Locations.Where(l => l.ParentId == loc.Id).ToList())
                Collect(child);
        }
        var root = _map.Locations.FirstOrDefault(l => l.Id == locationId);
        if (root is null) return;
        Collect(root);
        foreach (var loc in toRemove)
            foreach (var sc in loc.Scenes)
                _map.Edges.RemoveAll(e => e.A == sc.Id || e.B == sc.Id);
        _map.Locations.RemoveAll(l => toRemove.Contains(l));
        if (!_map.SceneExists(_currentSceneId))
            _currentSceneId = _map.AllScenes.FirstOrDefault()?.Id ?? "";
        _ = SaveAsync();
    }

    public void AddEdge(string a, string b)
    {
        if (a == b) return;
        if (_map.Edges.Any(e => (e.A == a && e.B == b) || (e.A == b && e.B == a))) return;
        _map.Edges.Add(new MapEdge { A = a, B = b });
        _ = SaveAsync();
    }

    /// <summary>通知地图 UI 刷新（场景券解锁等外部变更时调用）。</summary>
    public void NotifyChanged()
    {
        InvalidateReach();
        MapChanged?.Invoke();
    }

    /// <summary>画布空白点创建临时节点（连线端点）。</summary>
    public MapTransit AddTransit(double x, double y)
    {
        var t = new MapTransit { X = x, Y = y };
        _map.Transits.Add(t);
        _ = SaveAsync();
        return t;
    }

    public void RemoveTransit(string id)
    {
        _map.Edges.RemoveAll(e => e.A == id || e.B == id);
        _map.Transits.RemoveAll(t => t.Id == id);
        _ = SaveAsync();
    }

    public void RemoveEdge(string a, string b) =>
        _map.Edges.RemoveAll(e => (e.A == a && e.B == b) || (e.A == b && e.B == a));

    /// <summary>把用户选择的背景图/视频复制进 maps/ 目录；视频自动生成首帧缩略图。
    /// 返回相对路径（用于 scene.Background）；失败返回 null。</summary>
    public string? ImportBackground(string srcPath, string sceneId)
    {
        try
        {
            var dir = Path.Combine(_store.Root, "maps");
            Directory.CreateDirectory(dir);
            var ext = Path.GetExtension(srcPath);
            if (string.IsNullOrEmpty(ext)) ext = ".png";
            var dest = Path.Combine(dir, sceneId + ext);
            File.Copy(srcPath, dest, true);
            // 视频自动生成首帧缩略图（卡片绘制时显示）
            if (IsVideoExt(ext))
                _ = TryGenerateVideoThumb(dest, sceneId);
            return $"maps/{sceneId}{ext}";
        }
        catch (Exception ex)
        {
            App.WriteLog("MapService.ImportBackground -> " + ex);
            return null;
        }
    }

    /// <summary>
    /// 背景换了就必须重新标定：同一张图上标出来的落脚点与缩放关系，换一张图就不成立了。
    /// 这里只负责作废，不负责触发标定流程（那是 SceneCalibrationService 的事，
    /// 服务层不该直接导航）。
    /// </summary>
    public string? ImportBackgroundForScene(string srcPath, MapScene scene)
    {
        var rel = ImportBackground(srcPath, scene.Id);
        if (rel is not null) InvalidatePlacement(scene);
        return rel;
    }

    /// <summary>从相对背景路径解析为绝对路径（图片不存在返回 null，UI 用颜色兜底）。</summary>
    public string? ResolveBackground(MapScene scene)
    {
        if (string.IsNullOrWhiteSpace(scene.Background)) return null;
        var paths = new List<string>();
        var rel = scene.Background;
        // 直接尝试
        if (Path.IsPathRooted(rel)) { paths.Add(rel); }
        else
        {
            paths.Add(Path.Combine(_store.Root, rel));
            paths.Add(Path.Combine(App.RootDirectory, rel));
            paths.Add(Path.Combine(Directory.GetCurrentDirectory(), rel));
            // 添加 assets/ 前缀
            if (!rel.StartsWith("assets/", StringComparison.Ordinal))
                paths.Add(Path.Combine(_store.Root, "assets", rel));
            paths.Add(Path.Combine(App.RootDirectory, "assets", rel));
            // 去掉 assets/ 前缀
            if (rel.StartsWith("assets/", StringComparison.Ordinal))
                paths.Add(Path.Combine(_store.Root, rel["assets/".Length..]));
        }
        return paths.Where(p => !string.IsNullOrEmpty(p) && File.Exists(p)).FirstOrDefault();
    }

    public static bool IsVideoExt(string ext) =>
        ext.Equals(".mp4", StringComparison.OrdinalIgnoreCase)
        || ext.Equals(".mov", StringComparison.OrdinalIgnoreCase)
        || ext.Equals(".m4v", StringComparison.OrdinalIgnoreCase)
        || ext.Equals(".webm", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 背景文件的指纹（"{字节数}@{最后写入时间}"）。
    ///
    /// 背景统一存在 {root}/maps/{sceneId}{ext}，**换图是同路径覆盖**——
    /// 只比较相对路径的话，换了张图但路径没变，旧标定会静默地继续生效。
    /// 用长度 + mtime 做指纹就能识别出"这张图已经不是当初标定的那张了"。
    /// </summary>
    public static string StampOf(string? absPath)
    {
        try
        {
            if (string.IsNullOrEmpty(absPath) || !File.Exists(absPath)) return "";
            var fi = new FileInfo(absPath);
            return $"{fi.Length}@{fi.LastWriteTimeUtc.Ticks}";
        }
        catch
        {
            return "";
        }
    }

    /// <summary>背景图/视频已变化（或从未标定）时清空标定，强制重新走标定流程。返回是否真的清掉了。</summary>
    public bool InvalidatePlacement(MapScene scene)
    {
        if (scene.Placement is null) return false;
        scene.Placement = null;
        return true;
    }

    /// <summary>视频背景的首帧缩略图绝对路径（{场景名}_thumb.png）；视频不存在/未生成则返回 null。</summary>
    public string? ThumbnailAbsFor(MapScene scene)
    {
        if (string.IsNullOrWhiteSpace(scene?.Background)) return null;
        var abs = ResolveBackground(scene);
        if (abs is null) return null;
        if (!IsVideoExt(Path.GetExtension(abs))) return abs;   // 图片直接用作缩略图
        var thumb = Path.Combine(Path.GetDirectoryName(abs) ?? "", Path.GetFileNameWithoutExtension(abs) + "_thumb.png");
        return File.Exists(thumb) ? thumb : null;
    }

    /// <summary>生成视频首帧缩略图（仅映射目录内已有视频；WINDOWS 用 MediaComposition，其它平台忽略）。</summary>
    private Task TryGenerateVideoThumb(string videoAbsPath, string sceneId)
        => Task.Run(async () =>
        {
            try
            {
                var thumb = Path.Combine(Path.GetDirectoryName(videoAbsPath) ?? "", sceneId + "_thumb.png");
                if (File.Exists(thumb)) return;
#if WINDOWS
                var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(videoAbsPath);
                var clip = await Windows.Media.Editing.MediaClip.CreateFromFileAsync(file);
                var comp = new Windows.Media.Editing.MediaComposition();
                comp.Clips.Add(clip);
                using var stream = await comp.GetThumbnailAsync(TimeSpan.Zero, 360, 210, Windows.Media.Editing.VideoFramePrecision.NearestFrame);
                if (stream is null) return;
                stream.Seek(0);
                await using var fs = File.Create(thumb);
                await stream.AsStreamForRead().CopyToAsync(fs);
                App.WriteLog($"MapService: video thumb {thumb}");
#endif
            }
            catch (Exception ex)
            {
                App.WriteLog("MapService.TryGenerateVideoThumb -> " + ex.Message);
            }
        });

    // ==================== 导出 / 导入 ====================

    public async Task<string> ExportAsync()
    {
        try
        {
            await SaveAsync();
            var json = JsonSerializer.Serialize(_map, new JsonSerializerOptions { WriteIndented = true });
            using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
            var result = await FileSaver.Default.SaveAsync($"{_map.Name}.json", stream);
            return result.IsSuccessful ? $"已导出：{result.FilePath}" : "已取消导出";
        }
        catch (Exception ex)
        {
            App.WriteLog("MapService.ExportAsync -> " + ex);
            return "导出失败：" + ex.Message;
        }
    }

    public async Task<string> ImportAsync(CancellationToken ct = default)
    {
        try
        {
            var pick = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "选择地图 JSON",
                FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    { DevicePlatform.WinUI, new[] { ".json" } },
                    { DevicePlatform.Android, new[] { "application/json" } },
                    { DevicePlatform.iOS, new[] { "public.json" } }
                })
            });
            if (pick is null) return "已取消导入";
            var json = await File.ReadAllTextAsync(pick.FullPath, ct);
            var map = JsonSerializer.Deserialize<MapDefinition>(json);
            if (map is null || !map.AllScenes.Any()) return "无效的地图文件";
            return await AdoptAsync(map, ct);
        }
        catch (Exception ex)
        {
            App.WriteLog("MapService.ImportAsync -> " + ex);
            return "导入失败：" + ex.Message;
        }
    }

    /// <summary>
    /// 接过一张外部来的地图（JSON 导入 / 真实城市检索 / AI 生成都走这里）：整张换上，落盘，并走到出生点。
    /// 背景没标定的场景会先进标定流程，这里提前把话说清楚，免得用户莫名其妙撞上标定页。
    /// </summary>
    public async Task<string> AdoptAsync(MapDefinition map, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (!map.AllScenes.Any()) return "这张图里一个场景都没有，先让它长出地点再导入。";
        _map = map;
        Sanitize();
        _currentSceneId = map.StartSceneId;
        if (_map.SceneById(_currentSceneId) is null)
            _currentSceneId = _map.AllScenes.FirstOrDefault()?.Id ?? "";
        await SaveAsync();
        NotifyChanged();
        await MoveToAsync(_currentSceneId, ct);

        var needCal = _map.AllScenes.Count(s =>
            !string.IsNullOrWhiteSpace(s.Background)
            && ResolveBackground(s) is not null
            && !PlacementMath.Validate(s.Placement).Ok);
        var tail = needCal > 0 ? $"，其中 {needCal} 个场景需要标定背景" : "";
        return $"已导入地图：{map.Name}（{map.AllScenes.Count()} 个场景{tail}）";
    }
}