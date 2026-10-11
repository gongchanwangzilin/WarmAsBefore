using WarmAsBefore.Models;

namespace WarmAsBefore.Services;

/// <summary>
/// 多实体地图服务：在地图（MapService/MapDefinition）之上维护「用户 + 各 AI 角色」各自的位置，
/// 并处理 AI 拉拽移动：
///  - AI 可带用户走，也可带其他 AI 走；一次可拉多人（同场景内）
///  - 多 AI 拉同一目标且方向一致 → 目标跟着走
///  - 多 AI 拉同一目标且方向不同 → 目标不动，返回所有拉拽者，说明为什么不动
///  - 目标是用户但方向冲突 → 抑制画面抖动（ShakeSuppressed），位置保持不动
///  - 目标是 AI 且方向冲突 → 同样不动，但不再产生抖动
///
/// 本服务与 MapService 解耦：不改变单角色地图的既有行为，只在其上叠加多实体位置。
/// </summary>
public sealed class MapActorService
{
    /// <summary>用户实体的固定 Id。</summary>
    public const string UserId = "user";

    private readonly MapService _map;
    private readonly Dictionary<string, MapActor> _actors = new(StringComparer.Ordinal);
    private readonly object _lock = new();

    /// <summary>某个实体的位置变化（用于 UI 刷新立绘/位置标签）。</summary>
    public event Action<MapActor>? ActorMoved;
    /// <summary>多 AI 拉拽导致用户目标被抑制抖动时触发（UI 可做一次"轻微回弹"表现）。</summary>
    public event Action<string>? ShakeSuppressed;

    public MapActorService(MapService map) => _map = map;

    /// <summary>当前所有实体（快照）。</summary>
    public IReadOnlyCollection<MapActor> Actors
    {
        get { lock (_lock) return _actors.Values.ToList(); }
    }

    /// <summary>取实体；不存在返回 null。</summary>
    public MapActor? Get(string id)
    {
        lock (_lock) return _actors.TryGetValue(id ?? "", out var a) ? a : null;
    }

    /// <summary>注册/更新一个实体（用户或 AI 角色）到指定场景；sceneId 为空则用当前场景。</summary>
    public MapActor Register(string id, string name, bool isUser, string sceneId = "")
    {
        if (string.IsNullOrEmpty(id)) id = isUser ? UserId : Guid.NewGuid().ToString("N")[..8];
        lock (_lock)
        {
            if (!_actors.TryGetValue(id, out var actor))
            {
                actor = new MapActor { Id = id, Name = name, IsUser = isUser };
                _actors[id] = actor;
            }
            actor.Name = string.IsNullOrEmpty(name) ? actor.Name : name;
            actor.IsUser = isUser;
            if (string.IsNullOrEmpty(actor.SceneId))
                actor.SceneId = string.IsNullOrEmpty(sceneId) ? _map.CurrentSceneId : sceneId;
            return actor;
        }
    }

    /// <summary>把实体直接放到某场景（不经过拉拽仲裁；用于初始化/读档）。</summary>
    public bool Place(string id, string sceneId)
    {
        lock (_lock)
        {
            if (!_actors.TryGetValue(id, out var actor)) return false;
            if (string.IsNullOrEmpty(sceneId)) return false;
            actor.SceneId = sceneId;
            ActorMoved?.Invoke(actor);
            return true;
        }
    }

    /// <summary>
    /// 多 AI 拉拽同一目标的仲裁与执行。
    /// 规则（与需求一致）：
    ///  1. 所有 puller 必须与目标在同一场景，否则该次拉拽无效（记入 pulls，但标忽略）。
    ///  2. 有效方向集合去重后：
    ///     - 只 1 个方向 → 目标移动过去。
    ///     - 多个方向 → 目标不动；若目标是用户则抑制抖动。
    ///  3. 返回所有拉拽者，说明"谁拉了、为什么不动"。
    /// </summary>
    public Task<PullArbitrationResult> PullAsync(string targetId, IReadOnlyList<PullRequest> requests,
        CancellationToken ct = default)
    {
        var result = new PullArbitrationResult();
        MapActor? target;
        lock (_lock) _actors.TryGetValue(targetId ?? "", out target);
        if (target is null)
        {
            result.Message = "找不到被拉拽的目标。";
            return Task.FromResult(result);
        }

        // 过滤有效请求：puller 存在、与目标同场景、指定了目标场景
        var sameScene = new List<PullRequest>();
        foreach (var req in requests ?? Array.Empty<PullRequest>())
        {
            if (req is null) continue;
            MapActor? puller;
            lock (_lock) _actors.TryGetValue(req.PullerId ?? "", out puller);
            if (puller is null) continue;
            if (!string.IsNullOrEmpty(puller.SceneId) && puller.SceneId != target.SceneId)
                continue;   // 不在同一场景，拉不动
            if (string.IsNullOrEmpty(req.TargetSceneId)) continue;
            sameScene.Add(req);
        }

        result.Pulls = sameScene;
        result.FinalSceneId = target.SceneId;

        if (sameScene.Count == 0)
        {
            result.Message = "没有有效的拉拽（拉拽者必须和目标在同一场景）。";
            return Task.FromResult(result);
        }

        // 方向去重
        var directions = sameScene.Select(r => r.TargetSceneId).Distinct(StringComparer.Ordinal).ToList();

        if (directions.Count == 1)
        {
            var dest = directions[0];
            var oldScene = target.SceneId;
            if (dest == oldScene)
            {
                result.Moved = false;
                result.Message = $"{NamesOf(sameScene)} 都在拉 {target.Name}，但 TA 已经在目的地了。";
                return Task.FromResult(result);
            }
            lock (_lock) target.SceneId = dest;
            result.Moved = true;
            result.FinalSceneId = dest;
            ActorMoved?.Invoke(target);
            result.Message = $"{NamesOf(sameScene)} 把 {target.Name} 拉去了「{SceneName(dest)}」。";
            return Task.FromResult(result);
        }

        // 多方向冲突：目标不动
        result.Moved = false;
        result.ShakeSuppressed = true;
        var detail = string.Join("、", sameScene.Select(r =>
            $"{r.PullerName}想拉去「{SceneName(r.TargetSceneId)}」"));
        result.Message = $"{NamesOf(sameScene)} 同时在拉 {target.Name}，方向不同（{detail}），"
            + $"所以 {target.Name} 站在原地没有动。";
        if (target.IsUser) ShakeSuppressed?.Invoke(target.Id);
        return Task.FromResult(result);
    }

    /// <summary>便捷版：单个 AI 拉单个目标。</summary>
    public Task<PullArbitrationResult> PullAsync(string pullerId, string pullerName,
        string targetId, string targetSceneId, CancellationToken ct = default)
        => PullAsync(targetId, new[]
        {
            new PullRequest
            {
                PullerId = pullerId,
                PullerName = pullerName,
                TargetId = targetId,
                TargetSceneId = targetSceneId
            }
        }, ct);

    /// <summary>本地图/场景是否可达（拉拽前的合法性检查）。</summary>
    public bool CanReach(string fromSceneId, string toSceneId)
        => fromSceneId == toSceneId || _map.FindShortest(fromSceneId, toSceneId) is not null;

    private static string NamesOf(IReadOnlyList<PullRequest> reqs)
        => string.Join("、", reqs.Select(r => string.IsNullOrEmpty(r.PullerName) ? r.PullerId : r.PullerName)
            .Distinct());

    private string SceneName(string sceneId)
        => _map.Map.SceneById(sceneId)?.Name ?? sceneId;
}