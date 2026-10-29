namespace WarmAsBefore.Models;

/// <summary>
/// 一个角色的桌宠素材包：把 Q 版化后的立绘拆成一整套「状态动画」，每个状态是一条帧序列。
///
/// 这是「生成 + 播放」两半之间的数据契约——<c>PetStudioService</c> 负责把它写出来，
/// <c>PetService</c>（宿主）负责按状态取帧、按 fps 播放。所有字段都有默认值：
/// 老包/半成品包缺字段时反序列化仍能用，缺省值把行为压回「只有普通这一个静态状态」。
///
/// 帧路径一律是相对存储根的形式（如 <c>assets/pet/{角色}/walk/frame_00.png</c>），
/// 与角色 SpriteMap、素材库同一套相对路径约定，包可以整体搬走而不坏；
/// 绝对化交给宿主或 <c>PetStudioService.ResolveAbs</c> 做。
/// </summary>
public sealed record PetAnimationPack
{
    /// <summary>包版本：以后帧序列结构要变时按它做迁移。当前 = 1。</summary>
    public int Version { get; init; } = 1;

    /// <summary>来源角色 Id（<c>CharacterProfile.Id</c>）——包认主人，换角色就是换包。</summary>
    public string CharacterId { get; init; } = "";

    /// <summary>来源角色显示名：只为「这是谁的桌宠」在 UI 上一眼看出来，播放不依赖它。</summary>
    public string CharacterName { get; init; } = "";

    /// <summary>
    /// 画布高度基准（物理像素）：这一整套帧是按多大的「本体高」画的，
    /// 桌宠「更小」就是把这个数字定得比主界面立绘小；宿主按 <c>PetScale</c> 在它之上再乘倍率。
    /// </summary>
    public int CanvasHeightBaseline { get; init; } = PetAnimationPack.DefaultCanvasHeight;

    /// <summary>生成时间（UTC）。</summary>
    public DateTime GeneratedAt { get; init; } = DateTime.UtcNow;

    /// <summary>全部状态。缺省空列表：读一个还没有任何状态的空包不该炸。</summary>
    public List<PetAnimationState> States { get; init; } = new();

    /// <summary>走动/跳跃这类循环的默认帧率下限；PlanWalkCycle 与宿主兜底都用它。</summary>
    public const double DefaultLoopFps = 6;

    /// <summary>桌宠本体「更小」的基准高度：约主界面立绘的一半多一点，站在桌面上不挡屏。</summary>
    public const int DefaultCanvasHeight = 320;

    // ============ 播放侧纯函数：宿主只认这几个入口 ============

    /// <summary>按状态 id 取状态定义；不存在返回 null。</summary>
    public PetAnimationState? GetState(string? stateId)
        => string.IsNullOrWhiteSpace(stateId)
            ? null
            : States.FirstOrDefault(s => string.Equals(s.Id, stateId, StringComparison.OrdinalIgnoreCase));

    /// <summary>按状态取帧序列（相对路径）。没有这个状态就是空表，宿主据此回退到静态立绘。</summary>
    public IReadOnlyList<string> FramesOf(string? stateId) => GetState(stateId)?.Frames ?? new List<string>();

    /// <summary>按状态取帧率（帧/秒）。缺状态时回落到 <see cref="DefaultLoopFps"/>。</summary>
    public double FpsOf(string? stateId)
    {
        var s = GetState(stateId);
        return s is { Fps: > 0 } ? s.Fps : DefaultLoopFps;
    }

    /// <summary>按状态判断是否循环播放（走动/呼吸=循环；坐下/惊讶=一次性）。</summary>
    public bool LoopOf(string? stateId) => GetState(stateId)?.Loop ?? false;

    /// <summary>这个状态有没有真正可用的帧（至少一张且文件登记齐全）。</summary>
    public bool HasFrames(string? stateId) => GetState(stateId)?.Frames.Count > 0;

    /// <summary>
    /// 纯函数：给定「从该状态开始已经过去多少秒」，返回该画第几帧（0 基）。
    /// - 循环状态：按帧率推进后对帧数取模，永远停在序列里。
    /// - 一次性状态（坐下/流泪这类）：播完停在最后一帧，不回卷。
    /// 单帧或空序列恒返回 0。宿主每帧用它算 index，再把对应路径贴上去即可。
    /// </summary>
    public int FrameIndexAt(string? stateId, double elapsedSeconds)
    {
        var s = GetState(stateId);
        if (s is null || s.Frames.Count == 0) return 0;
        if (s.Frames.Count == 1) return 0;

        var fps = s.Fps > 0 ? s.Fps : DefaultLoopFps;
        var pos = Math.Max(0, elapsedSeconds) * fps;
        if (!double.IsFinite(pos)) pos = 0;
        var idx = (int)Math.Floor(pos);

        if (s.Loop)
        {
            idx %= s.Frames.Count;
            return idx < 0 ? idx + s.Frames.Count : idx;
        }
        return Math.Min(idx, s.Frames.Count - 1);
    }
}

/// <summary>
/// 素材包里的一个状态（普通/开心/走动……）。一条循环：帧序列 + 帧率 + 是否循环。
/// </summary>
public sealed record PetAnimationState
{
    /// <summary>状态 id（英文小写稳定键，如 <c>walk</c>）——宿主按它点名要哪个动作。</summary>
    public string Id { get; init; } = "";

    /// <summary>中文名（走动/开心……），只为 UI 与调试可读。</summary>
    public string Name { get; init; } = "";

    /// <summary>帧路径序列（相对存储根，按播放顺序）。空表示这个状态还没出图。</summary>
    public List<string> Frames { get; init; } = new();

    /// <summary>这个状态该按多少帧/秒播放。走动由步频公式算出；单帧状态随便给个正值即可。</summary>
    public double Fps { get; init; } = PetAnimationPack.DefaultLoopFps;

    /// <summary>是否循环（走动/坐下后的呼吸循环；跳跃/惊讶一次性播完）。</summary>
    public bool Loop { get; init; }

    /// <summary>是否已出全所需帧（生成期用它区分「完整」与「缺帧的半成品」）。</summary>
    public bool IsComplete { get; init; }

    /// <summary>可用帧数。</summary>
    public int FrameCount => Frames.Count;
}
