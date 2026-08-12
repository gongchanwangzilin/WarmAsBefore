using System.Text.Json;
using CommunityToolkit.Maui.Storage;
using WarmAsBefore.Models;

namespace WarmAsBefore.Services;

/// <summary>
/// 地图服务（场景图）：
/// - 持久化地图定义（地点→场景，场景间连线）
/// - 寻路：BFS 找出所有最短路径，多条时随机选择一条
/// - 行走：按路线逐段行进，每段耗时后切换"当前场景"，通过事件通知 UI
/// - 背景图管理：用户选择的图片复制到本地 maps/ 目录，存相对路径
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

    /// <summary>地图数据变化（编辑/导入后刷新 UI）。</summary>
    public event Action? MapChanged;

    public MapService(StorageProvider store) => _store = store;

    public MapDefinition Map => _map;
    public bool IsLoaded => _isLoaded;
    public string CurrentSceneId => _currentSceneId;
    public MapScene? CurrentScene => _map.SceneById(_currentSceneId);

    // ==================== 初始化 ====================

    public async Task InitializeAsync()
    {
        if (_isLoaded) return;
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
    }

    public async Task SaveAsync() => await _store.Save(MapKey, _map);

    // ==================== 寻路 ====================

    /// <summary>路径顶点（场景或临时节点）。</summary>
    public readonly record struct MapNodeRef(string Id, string Name, bool IsScene);

    /// <summary>图上最短路径（BFS，顶点含临时节点），多条等长路径随机返回一条；null=不可达。</summary>
    public List<MapNodeRef>? FindShortest(string fromId, string toId)
    {
        if (fromId == toId) return new List<MapNodeRef> { Resolve(fromId) };
        var dist = new Dictionary<string, int> { [fromId] = 0 };
        var queue = new Queue<string>();
        queue.Enqueue(fromId);
        while (queue.Count > 0)
        {
            var cur = queue.Dequeue();
            foreach (var (nbId, _, _) in _map.NeighborsOfPoint(cur))
            {
                if (dist.ContainsKey(nbId)) continue;
                dist[nbId] = dist[cur] + 1;
                queue.Enqueue(nbId);
            }
        }
        if (!dist.ContainsKey(toId)) return null;

        // 收集所有等长最短路径，随机选一
        var paths = new List<List<MapNodeRef>>();
        void Dfs(string node, List<MapNodeRef> trail)
        {
            trail.Add(Resolve(node));
            if (node == toId)
            {
                paths.Add(new List<MapNodeRef>(trail));
            }
            else
            {
                foreach (var (nbId, _, _) in _map.NeighborsOfPoint(node))
                    if (dist.TryGetValue(nbId, out var d) && d == dist[node] + 1)
                        Dfs(nbId, trail);
            }
            trail.RemoveAt(trail.Count - 1);
        }
        Dfs(fromId, new List<MapNodeRef>());
        return paths.Count == 0 ? null : paths[_rnd.Next(paths.Count)];
    }

    private MapNodeRef Resolve(string id) =>
        _map.SceneById(id) is { } s ? new MapNodeRef(s.Id, s.Name, true)
        : _map.TransitById(id) is { } t ? new MapNodeRef(t.Id, t.Name, false)
        : new MapNodeRef(id, "", false);

    /// <summary>沿线行走：只把目标为场景的顶点切换为"当前场景"（临时节点仅作通路，不停留）。</summary>
    public async Task WalkRouteAsync(IReadOnlyList<MapNodeRef> route, CancellationToken ct = default)
    {
        if (route is null || route.Count == 0) return;
        foreach (var node in route)
        {
            if (!node.IsScene) continue;   // 临时节点：只路过
            if (node.Id == _currentSceneId) continue;
            ct.ThrowIfCancellationRequested();
            await Task.Delay(WalkMsPerSegment, ct);
            _currentSceneId = node.Id;
            SceneChanged?.Invoke(_map.SceneById(node.Id)!);
        }
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

        await WalkRouteAsync(route, ct);
        var pathText = string.Join(" → ", route.Select(r => r.Name));
        return $"（已沿路线走到 {target.Name}：{pathText}）";
    }

    /// <summary>顶点（场景/临时节点）的画布坐标。</summary>
    public Point WorldPointOf(string nodeId)
    {
        if (_map.SceneById(nodeId) is { } s) return new Point(s.X < 0 ? 0 : s.X, s.Y < 0 ? 0 : s.Y);
        if (_map.TransitById(nodeId) is { } t) return new Point(t.X < 0 ? 0 : t.X, t.Y < 0 ? 0 : t.Y);
        return default;
    }

    /// <summary>边的几何长度（px，按路径折点求和），再换算成米。</summary>
    public double EdgeLengthPixels(MapEdge e)
    {
        var pts = new List<Point> { WorldPointOf(e.A) };
        foreach (var w in e.Waypoints) pts.Add(new Point(w.X, w.Y));
        pts.Add(WorldPointOf(e.B));
        double px = 0;
        for (var i = 0; i + 1 < pts.Count; i++)
            px += Math.Sqrt(Math.Pow(pts[i + 1].X - pts[i].X, 2) + Math.Pow(pts[i + 1].Y - pts[i].Y, 2));
        return px;
    }

    /// <summary>显示用长度（米）：手动值 > 0 用之；=0 表示未丈量（虚线）；-1 自动按几何换算。</summary>
    public double DisplayLengthMeters(MapEdge e)
    {
        if (e.Length > 0) return e.Length;
        if (e.Length == 0) return 0;
        return Math.Max(1, Math.Round(EdgeLengthPixels(e) * 0.5));
    }

    /// <summary>该边是否以虚线绘制（长度=0 → 未丈量）。</summary>
    public bool IsDashed(MapEdge e) => e.Length == 0;

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
    public void NotifyChanged() => MapChanged?.Invoke();

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

    /// <summary>把用户选择的背景图复制进 maps/ 目录，返回相对路径（失败返回原路径兜底颜色方案）。</summary>
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
            return $"maps/{sceneId}{ext}";
        }
        catch (Exception ex)
        {
            App.WriteLog("MapService.ImportBackground -> " + ex);
            return null;
        }
    }

    /// <summary>把相对背景路径解析为绝对路径（图片不存在返回 null，UI 用颜色兜底）。</summary>
    public string? ResolveBackground(MapScene scene)
    {
        if (string.IsNullOrWhiteSpace(scene.Background)) return null;
        var full = Path.IsPathRooted(scene.Background)
            ? scene.Background
            : Path.Combine(_store.Root, scene.Background);
        return File.Exists(full) ? full : null;
    }

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
            _map = map;
            Sanitize();
            _currentSceneId = map.StartSceneId;
            if (_map.SceneById(_currentSceneId) is null)
                _currentSceneId = _map.AllScenes.FirstOrDefault()?.Id ?? "";
            await SaveAsync();
            MapChanged?.Invoke();
            await MoveToAsync(_currentSceneId, ct);
            return $"已导入地图：{map.Name}（{map.AllScenes.Count()} 个场景）";
        }
        catch (Exception ex)
        {
            App.WriteLog("MapService.ImportAsync -> " + ex);
            return "导入失败：" + ex.Message;
        }
    }
}