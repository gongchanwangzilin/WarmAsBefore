using System.Text.Json.Serialization;

namespace WarmAsBefore.Models;

/// <summary>
/// 地图上的一个实体（用户或某个 AI 角色）在地图上所处的位置。
/// 与 MapService 的「单一当前位置」不同：多实体系统里每个实体各有一个位置，
/// 允许 AI 拉拽用户/其他 AI 移动。
/// </summary>
public sealed class MapActor
{
    /// <summary>实体 Id：用户固定为 "user"，AI 角色用角色库里的 CharacterProfile.Id。</summary>
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsUser { get; set; }
    /// <summary>当前所在场景 Id。</summary>
    public string SceneId { get; set; } = "";
    /// <summary>是否正在被拉拽（用于 UI 表现与仲裁锁）。</summary>
    public bool IsBeingPulled { get; set; }
}

/// <summary>
/// 一次拉拽请求：某个 AI（PullerId）想把某个目标（TargetId）拉向某个场景（TargetSceneId）。
/// 用于多 AI 同时拉同一目标时的仲裁与"谁拉了我"的追溯。
/// </summary>
public sealed class PullRequest
{
    public string PullerId { get; set; } = "";
    public string PullerName { get; set; } = "";
    public string TargetId { get; set; } = "";
    /// <summary>要去的目标场景 Id（可为空 = 只是拉扯但没指定方向）。</summary>
    public string TargetSceneId { get; set; } = "";
}

/// <summary>
/// 多 AI 拉拽同一目标的仲裁结果。
/// 约定：方向一致 → 目标跟着走；方向冲突 → 目标不动（返回所有人，说明为什么不动）。
/// </summary>
public sealed class PullArbitrationResult
{
    /// <summary>目标是否真的移动了。</summary>
    public bool Moved { get; set; }
    /// <summary>仲裁结论文本（给 AI / 用户看的解释）。</summary>
    public string Message { get; set; } = "";
    /// <summary>本次涉及的所有拉拽发起者（用于"返回给哪些人拉了用户"）。</summary>
    public List<PullRequest> Pulls { get; set; } = new();
    /// <summary>
    /// 被拒绝的拉拽及原因（人不在场 / 不同场景 / 没指定目的地 / 目的地不可达）。
    /// 用于向 AI 解释「为什么你没拉动」。
    /// </summary>
    public List<string> Rejected { get; set; } = new();
    /// <summary>目标最终所在场景 Id（未移动时为原场景）。</summary>
    public string FinalSceneId { get; set; } = "";
    /// <summary>目标是否发生了抖动（多方向冲突但被抑制；只有用户目标才会抖）。</summary>
    public bool ShakeSuppressed { get; set; }
}
