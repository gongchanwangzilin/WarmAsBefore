using System.Text.Json.Serialization;

namespace WarmAsBefore.Models;

/// <summary>
/// 地图定义：地点（建筑）→ 内部场景；场景之间以连线（MapEdge）构成通路，
/// AI 寻路时在场景图上求最短路径（多条等长路径随机选一条）。
/// 另支持：临时节点（Transit，仅作连线端点）、边的可视化路径（折点/曲直/长度/备注）。
/// </summary>
public sealed class MapDefinition
{
    public string Name { get; set; } = "温暖地图";
    /// <summary>初始所在场景（读档/启动时的出生点）。</summary>
    public string StartSceneId { get; set; } = "";

    public List<MapLocation> Locations { get; set; } = new();
    public List<MapEdge> Edges { get; set; } = new();
    /// <summary>临时节点：不归属任何地点，仅作为连线端点/路径中间站。</summary>
    public List<MapTransit> Transits { get; set; } = new();

    [JsonIgnore] public IEnumerable<MapScene> AllScenes => Locations.SelectMany(l => l.Scenes);

    public MapScene? SceneById(string id) =>
        AllScenes.FirstOrDefault(s => s.Id == id);

    public MapTransit? TransitById(string id) =>
        Transits.FirstOrDefault(t => t.Id == id);

    /// <summary>任意顶点（场景或临时节点的名字，用于连线端点的名称解析）。</summary>
    public string? PointNameById(string id) =>
        SceneById(id)?.Name ?? TransitById(id)?.Name;

    [JsonIgnore]
    public IEnumerable<(string Id, string Name, bool IsScene)> AllPoints
    {
        get
        {
            foreach (var s in AllScenes) yield return (s.Id, s.Name, true);
            foreach (var t in Transits) yield return (t.Id, t.Name, false);
        }
    }

    /// <summary>按名称查找场景：支持完整名或包含匹配（AI 请求时的容错）。</summary>
    public MapScene? SceneByName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        name = name.Trim();
        return AllScenes.FirstOrDefault(s => s.Name == name)
            ?? AllScenes.FirstOrDefault(s => s.Name.Contains(name, StringComparison.Ordinal))
            ?? AllScenes.FirstOrDefault(s => name.Contains(s.Name, StringComparison.Ordinal));
    }

    public string LocationNameOf(string sceneId) =>
        Locations.FirstOrDefault(l => l.Scenes.Any(s => s.Id == sceneId))?.Name ?? "";

    public bool SceneExists(string id) => AllScenes.Any(s => s.Id == id);

    /// <summary>相邻顶点（按边展开；顶点可以是场景或临时节点）。</summary>
    public IEnumerable<(string Id, string Name, bool IsScene)> NeighborsOfPoint(string pointId)
    {
        foreach (var e in Edges)
        {
            if (e.A == pointId && PointNameById(e.B) is { } nb) yield return ResolvePoint(e.B);
            else if (e.B == pointId && PointNameById(e.A) is { } na) yield return ResolvePoint(e.A);
        }
    }

    private (string Id, string Name, bool IsScene) ResolvePoint(string id) =>
        SceneById(id) is { } s ? (s.Id, s.Name, true) : (id, TransitById(id)?.Name ?? "", false);

    /// <summary>相邻场景（按边展开，忽略临时节点端点）。</summary>
    public IEnumerable<MapScene> Neighbors(MapScene scene) =>
        NeighborsOfPoint(scene.Id)
            .Where(n => n.IsScene && SceneById(n.Id) is { } sc)
            .Select(n => SceneById(n.Id)!);
}

public sealed class MapLocation
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "";
    /// <summary>父地点 Id；"" 表示顶层地点。用于"一个地点套一堆子地点"的嵌套分组。</summary>
    public string ParentId { get; set; } = "";
    /// <summary>地点备注（地点组的备注卡）。</summary>
    public string Note { get; set; } = "";
    /// <summary>空地点锚点（无任何场景时画出最小框的位置）；-1 表示未摆放。</summary>
    public double X { get; set; } = -1;
    public double Y { get; set; } = -1;
    public List<MapScene> Scenes { get; set; } = new();
}

public sealed class MapScene
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "";
    /// <summary>背景图：优先存绝对路径，缺失则退用 BackgroundColor。</summary>
    public string Background { get; set; } = "";
    public string BackgroundColor { get; set; } = "#2C1810";
    /// <summary>场景备注（显示在卡片下方的备注卡）。</summary>
    public string Note { get; set; } = "";
    /// <summary>画布坐标（地图编辑器里卡片的位置）；-1 表示尚未摆放，加载时自动网格布局。</summary>
    public double X { get; set; } = -1;
    public double Y { get; set; } = -1;
}

/// <summary>临时节点：不归属任何地点，作为连线端点/路径站。</summary>
public sealed class MapTransit
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "临时节点";
    public string Note { get; set; } = "";
    public double X { get; set; } = -1;
    public double Y { get; set; } = -1;
}

/// <summary>连线的可视化路径点（折线用）或曲线控制点。</summary>
public sealed class MapWaypoint
{
    public double X { get; set; }
    public double Y { get; set; }
}

public sealed class MapEdge
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    /// <summary>端点 A/B（场景 Id 或临时节点 Id）。</summary>
    public string A { get; set; } = "";
    public string B { get; set; } = "";
    /// <summary>线型："line" 折线（waypoint 直角直连）；"curve" 贝塞尔曲线（waypoint 为控制点）。</summary>
    public string Kind { get; set; } = "line";
    /// <summary>路径点（画布世界坐标；空 = 端点直连）。</summary>
    public List<MapWaypoint> Waypoints { get; set; } = new();
    /// <summary>-1 = 自动按几何长度计算；0 = 未丈量（画虚线）；&gt;0 = 手动指定长度（米）。</summary>
    public double Length { get; set; } = -1;
    /// <summary>连线备注（显示在连线中段的备注卡）。</summary>
    public string Note { get; set; } = "";
    /// <summary>true = 只显示整条总长一个标记（涂鸦一次性生成，不做逐段拆分）。</summary>
    public bool TotalLabel { get; set; }
}