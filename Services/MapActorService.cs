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

    /// <summary>
    /// 位置变动广播（#5）：一次拉拽/移动发生后，向「同场景的其它实体」广播这件事，
    /// 让在场者都"知道"谁被带去哪了。参数为一句人类可读的广播文本（已含在场者名单）。
    /// </summary>
    public event Action<string>? ActorsBroadcast;

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

        // 过滤有效请求：puller 存在、与目标同场景、指定了目标场景、且目标场景可达
        var sameScene = new List<PullRequest>();
        var rejected = new List<string>();
        foreach (var req in requests ?? Array.Empty<PullRequest>())
        {
            if (req is null) continue;
            MapActor? puller;
            lock (_lock) _actors.TryGetValue(req.PullerId ?? "", out puller);
            if (puller is null)
            {
                rejected.Add($"{req.PullerName ?? req.PullerId}（不在场）");
                continue;
            }
            if (!string.IsNullOrEmpty(puller.SceneId) && puller.SceneId != target.SceneId)
            {
                rejected.Add($"{Name(req)}（和目标不在同一场景）");
                continue;   // 不在同一场景，拉不动
            }
            if (string.IsNullOrEmpty(req.TargetSceneId))
            {
                rejected.Add($"{Name(req)}（没指定目的地）");
                continue;
            }
            // 可达性：目的地必须在地图里且与目标当前场景连通（同场景也算可达，用于「拉不动」提示）。
            if (!CanReach(target.SceneId, req.TargetSceneId))
            {
                rejected.Add($"{Name(req)}（「{SceneName(req.TargetSceneId)}」不可达）");
                continue;
            }
            sameScene.Add(req);
        }
        result.Pulls = sameScene;
        result.Rejected = rejected;
        result.FinalSceneId = target.SceneId;
        if (sameScene.Count == 0)
        {
            result.Message = rejected.Count > 0
                ? $"没有有效的拉拽：{string.Join("、", rejected)}。"
                : "没有有效的拉拽（拉拽者必须和目标在同一场景，且目的地要可达）。";
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
            // #5 广播：拉拽发生时，原场景里的其它实体都"知道"这件事（含被拉者名单）。
            BroadcastPositionChange(oldScene, target, result.Message);
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
        // #5 广播：拉拽冲突（没拉动）本身也是在场者都该知道的事。
        BroadcastPositionChange(target.SceneId, target, result.Message);
        return Task.FromResult(result);
    }

    /// <summary>
    /// #5：把一次位置变动广播给「变动发生场景里的其它实体」，让在场者都知道谁去了哪。
    /// 场景里除事件主角外没有别的实体、且主角也不是用户时，不广播（没人可被通知）。
    /// </summary>
    private void BroadcastPositionChange(string sceneId, MapActor subject, string message)
    {
        if (string.IsNullOrEmpty(sceneId)) return;
        List<string> witnesses;
        lock (_lock)
        {
            witnesses = _actors.Values
                .Where(a => a.SceneId == sceneId && !ReferenceEquals(a, subject))
                .Select(a => a.Name)
                .Where(n => !string.IsNullOrEmpty(n))
                .Distinct()
                .ToList();
        }
        // 在场者列表去重后并入广播文本，AI 侧一眼能看到"谁在场、谁被告知了"。
        var line = witnesses.Count > 0
            ? message + $"（在场并被告知：{string.Join("、", witnesses)}）"
            : message;
        ActorsBroadcast?.Invoke(line);
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

    /// <summary>
    /// 批量拉拽：一次处理「多个不同目标」的拉拽（每个目标各自仲裁，互不干涉）。
    /// 用于一个回合里多个 AI 同时拉不同的人——各自按 PullAsync 的规则判定。
    /// </summary>
    public async Task<Dictionary<string, PullArbitrationResult>> PullManyAsync(
        IReadOnlyList<PullRequest> requests, CancellationToken ct = default)
    {
        var results = new Dictionary<string, PullArbitrationResult>(StringComparer.Ordinal);
        foreach (var group in (requests ?? Array.Empty<PullRequest>())
                     .Where(r => r is not null && !string.IsNullOrEmpty(r.TargetId))
                     .GroupBy(r => r.TargetId, StringComparer.Ordinal))
        {
            var list = group.ToList();
            results[group.Key] = await PullAsync(group.Key, list, ct);
        }
        return results;
    }

    /// <summary>本地图/场景是否可达（拉拽前的合法性检查）。</summary>
    public bool CanReach(string fromSceneId, string toSceneId)
        => fromSceneId == toSceneId || _map.FindShortest(fromSceneId, toSceneId) is not null;

    /// <summary>
    /// 让「某个实体」单独移动到目标场景（不牵扯其它实体，包括用户）。
    /// 用于 #9 行进扩展：AI 可选不带用户单独走到某地——此时只改 AI 自己的位置，
    /// 用户视角（MapService.CurrentSceneId）保持不动，由用户在 UI 里选择「跟上」。
    /// </summary>
    /// <returns>移动过程描述文本；找不到实体或目标不可达时返回空串。</returns>
    public string MoveActorToScene(string actorId, string sceneNameOrId)
    {
        if (string.IsNullOrEmpty(actorId) || string.IsNullOrEmpty(sceneNameOrId)) return "";
        MapActor? actor;
        lock (_lock) _actors.TryGetValue(actorId, out actor);
        if (actor is null) return "";
        var target = _map.Map.SceneById(sceneNameOrId) ?? _map.Map.SceneByName(sceneNameOrId);
        if (target is null) return "";
        var from = string.IsNullOrEmpty(actor.SceneId) ? _map.CurrentSceneId : actor.SceneId;
        if (from == target.Id) return $"（{actor.Name} 已经在 {target.Name}）";
        var route = _map.FindShortest(from, target.Id);
        if (route is null) return $"（{actor.Name} 从当前位置到 {target.Name} 还没有开通路线）";
        lock (_lock) actor.SceneId = target.Id;
        ActorMoved?.Invoke(actor);
        var pathText = string.Join(" → ", route.Select(r => r.Name));
        return $"（{actor.Name} 独自走到了 {target.Name}：{pathText}）";
    }

    /// <summary>取某实体当前的场景 Id；找不到返回空串。</summary>
    public string SceneOfActor(string actorId)
    {
        if (string.IsNullOrEmpty(actorId)) return "";
        lock (_lock) return _actors.TryGetValue(actorId, out var a) ? a.SceneId : "";
    }

    /// <summary>
    /// #4：AI 行动时「可选择是否带上用户」。
    /// 让 actorId 走到目标场景；carryUser=true 时，若用户此刻与 AI 同场景，则一并把用户带过去
    /// （用户不在同场景则不勉强，返回里说明）。移动后向两个场景的在场者广播。
    /// </summary>
    /// <returns>(移动描述, 是否带上了用户, 广播文本)。</returns>
    public (string Note, bool CarriedUser, string Broadcast) MoveWithUser(
        string actorId, string sceneNameOrId, bool carryUser)
    {
        if (string.IsNullOrEmpty(actorId) || string.IsNullOrEmpty(sceneNameOrId))
            return ("", false, "");
        MapActor? actor;
        lock (_lock) _actors.TryGetValue(actorId, out actor);
        if (actor is null) return ("", false, "");
        var target = _map.Map.SceneById(sceneNameOrId) ?? _map.Map.SceneByName(sceneNameOrId);
        if (target is null) return ("", false, "");
        var from = string.IsNullOrEmpty(actor.SceneId) ? _map.CurrentSceneId : actor.SceneId;
        if (from == target.Id) return ($"（{actor.Name} 已经在 {target.Name}）", false, "");
        var route = _map.FindShortest(from, target.Id);
        if (route is null) return ($"（{actor.Name} 从当前位置到 {target.Name} 还没有开通路线）", false, "");

        MapActor? user = null;
        if (carryUser)
        {
            lock (_lock) _actors.TryGetValue(UserId, out user);
            // 只有用户确实跟 AI 同场景才带得走；否则不勉强。
            if (user is not null && !string.Equals(user.SceneId, from, StringComparison.Ordinal))
                user = null;
        }

        lock (_lock) actor.SceneId = target.Id;
        ActorMoved?.Invoke(actor);
        var pathText = string.Join(" → ", route.Select(r => r.Name));
        var note = carryUser && user is not null
            ? $"（{actor.Name} 带着你从 {pathText} 走了过去）"
            : $"（{actor.Name} 独自走到了 {target.Name}：{pathText}）";

        var broadcast = "";
        if (carryUser && user is not null)
        {
            lock (_lock) user.SceneId = target.Id;
            ActorMoved?.Invoke(user);
            broadcast = $"{actor.Name} 带走了 {user.Name}，一起去了「{target.Name}」。";
        }
        else if (carryUser)
        {
            broadcast = $"{actor.Name} 独自去了「{target.Name}」（你没有和 TA 待在同一处，没能跟上）。";
        }
        BroadcastPositionChange(from, actor, broadcast.Length > 0 ? broadcast : $"{actor.Name} 去了「{target.Name}」。");
        return (note, carryUser && user is not null, broadcast);
    }

    private static string NamesOf(IReadOnlyList<PullRequest> reqs)
        => string.Join("、", reqs.Select(r => string.IsNullOrEmpty(r.PullerName) ? r.PullerId : r.PullerName)
            .Distinct());
    /// <summary>单个拉拽者的显示名（无名字则退回 Id）。</summary>
    private static string Name(PullRequest req)
        => string.IsNullOrEmpty(req.PullerName) ? req.PullerId : req.PullerName;

    private string SceneName(string sceneId)
        => _map.Map.SceneById(sceneId)?.Name ?? sceneId;
}