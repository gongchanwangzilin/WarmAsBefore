namespace WarmAsBefore.Modules.Outfit;

/// <summary>
/// 一次行走节拍计算的结果。所有字段都是纯数据，调用方可以直接拿去驱动 <see cref="FigureMotion"/>。
/// </summary>
public readonly record struct WalkCyclePlan
{
    public bool IsValid { get; init; }

    /// <summary>输入速度，单位与输入一致（像素/秒或归一化距离/秒）。</summary>
    public double SpeedPerSecond { get; init; }

    /// <summary>相邻两次落脚（左右脚交替）之间的位移，单位与速度一致。</summary>
    public double StepLength { get; init; }

    /// <summary>一个完整动画循环的位移 = 步长 × 每循环步数。</summary>
    public double StrideLength { get; init; }

    public int FrameCount { get; init; }
    public int StepsPerCycle { get; init; }
    public double BaseFrameDurationMs { get; init; }

    /// <summary>为了不滑步而必须采用的播放帧率；0 表示静止（保持相位冻结）。</summary>
    public double Fps { get; init; }

    /// <summary>按 Fps 换算的单帧时长；静止时回落到动作本身的基准帧时长。</summary>
    public double FrameDurationMs { get; init; }

    /// <summary>相对动作基准速度的倍率（Fps ÷ 基准 Fps）。仅用于播放器调速，不参与相位计算。</summary>
    public double PlaybackRate { get; init; }

    public double CyclesPerSecond { get; init; }

    /// <summary>本次采样对应的循环相位（0~1），由「起始相位 + 已走距离 / 步幅」推得。</summary>
    public double Phase { get; init; }

    /// <summary>把相位对齐到第 0 帧的偏移：播放时钟 = (PhaseOffset + 距离 / StrideLength) mod 1。</summary>
    public double PhaseOffset { get; init; }

    /// <summary>当前相位应该显示的帧号（0..FrameCount-1）。</summary>
    public int FrameIndex { get; init; }
}

/// <summary>
/// 行走节奏协调：只做数学，不碰文件、不改全局状态，方便在移动逻辑里每帧调用，也方便单测。
///
/// 关键原则：**相位由位移驱动，而不是由时间驱动**。时间驱动在变速时会漏掉/补上位移，
/// 脚就会在地上滑；位移驱动则每一段距离都严格对应固定相位，从待机起步时只要把上一次的
/// 相位（通常是静止时冻结的那一帧）传进来，就不会跳回第 0 帧。
/// </summary>
public static class WalkCycle
{
    public const int DefaultStepsPerCycle = 2;

    /// <summary>
    /// 按「像素/秒」规划步频。stepLengthPixels 是相邻两次落脚的位移，
    /// 一个完整循环默认包含左右两步（<paramref name="stepsPerCycle"/> = 2）。
    /// 速度与步长必须使用同一单位；归一化输入请用 <see cref="PlanNormalized"/>。
    /// </summary>
    public static WalkCyclePlan Plan(
        double speedPixelsPerSecond,
        double stepLengthPixels,
        int frameCount,
        double baseFrameDurationMs,
        double startPhase = 0,
        int stepsPerCycle = DefaultStepsPerCycle)
    {
        if (!IsFinite(speedPixelsPerSecond) || speedPixelsPerSecond < 0 ||
            !IsFinite(stepLengthPixels) || stepLengthPixels <= 0 ||
            frameCount <= 0 ||
            !IsFinite(baseFrameDurationMs) || baseFrameDurationMs <= 0 ||
            stepsPerCycle <= 0)
            return default;

        var phase = NormalizePhase(startPhase);
        var stride = stepLengthPixels * stepsPerCycle;
        var cyclesPerSecond = speedPixelsPerSecond / stride;
        var fps = cyclesPerSecond * frameCount;
        var baseFps = 1000.0 / baseFrameDurationMs;

        return new WalkCyclePlan
        {
            IsValid = true,
            SpeedPerSecond = speedPixelsPerSecond,
            StepLength = stepLengthPixels,
            StrideLength = stride,
            FrameCount = frameCount,
            StepsPerCycle = stepsPerCycle,
            BaseFrameDurationMs = baseFrameDurationMs,
            Fps = fps,
            // 静止时没有「正确帧率」，保留基准时长，播放器可以停在当前相位。
            FrameDurationMs = fps > 0 ? 1000.0 / fps : baseFrameDurationMs,
            PlaybackRate = fps / baseFps,
            CyclesPerSecond = cyclesPerSecond,
            Phase = phase,
            PhaseOffset = phase,
            FrameIndex = FrameIndexAt(phase, frameCount),
        };
    }

    /// <summary>
    /// 归一化输入版：速度与步长都以「同一参考高度（画布高/角色高）的倍数/秒」为单位。
    /// 两者单位一致时参考高度会在除法里约掉，所以不需要再传画布尺寸。
    /// </summary>
    public static WalkCyclePlan PlanNormalized(
        double speedNormalizedPerSecond,
        double stepLengthNormalized,
        int frameCount,
        double baseFrameDurationMs,
        double startPhase = 0,
        int stepsPerCycle = DefaultStepsPerCycle)
        => Plan(speedNormalizedPerSecond, stepLengthNormalized, frameCount, baseFrameDurationMs, startPhase, stepsPerCycle);

    /// <summary>
    /// 已知「这段距离走了多久」时直接算步频；负数距离按绝对值处理（只关心位移大小）。
    /// 时长非正时返回无效结果，不抛异常，移动逻辑可以静默跳过这一帧。
    /// </summary>
    public static WalkCyclePlan PlanFromDistance(
        double distancePixels,
        double elapsedSeconds,
        double stepLengthPixels,
        int frameCount,
        double baseFrameDurationMs,
        double startPhase = 0,
        int stepsPerCycle = DefaultStepsPerCycle)
    {
        if (!IsFinite(distancePixels) || !IsFinite(elapsedSeconds) || elapsedSeconds <= 0)
            return default;
        var speed = Math.Abs(distancePixels) / elapsedSeconds;
        return Plan(speed, stepLengthPixels, frameCount, baseFrameDurationMs, startPhase, stepsPerCycle);
    }

    /// <summary>
    /// 速度变化时沿用旧相位重新规划。只改 Fps/帧时长，绝不把 FrameIndex 归零——
    /// 从 idle（speed=0，相位冻结）切到 walk 时同样走这里。
    /// </summary>
    public static WalkCyclePlan Retarget(
        WalkCyclePlan previous,
        double newSpeedPixelsPerSecond,
        double stepLengthPixels,
        int frameCount,
        double baseFrameDurationMs,
        int stepsPerCycle = DefaultStepsPerCycle)
    {
        var phase = previous.IsValid ? previous.Phase : 0;
        return Plan(newSpeedPixelsPerSecond, stepLengthPixels, frameCount, baseFrameDurationMs, phase, stepsPerCycle);
    }

    /// <summary>
    /// 从待机过渡到行走：把待机时冻结的相位原样带过去。
    /// 单独留一个名字是为了让调用处一眼看出「这里有意不重置动画」。
    /// </summary>
    public static WalkCyclePlan TransitionFromIdle(
        double idlePhase,
        double targetSpeedPixelsPerSecond,
        double stepLengthPixels,
        int frameCount,
        double baseFrameDurationMs,
        int stepsPerCycle = DefaultStepsPerCycle)
        => Plan(targetSpeedPixelsPerSecond, stepLengthPixels, frameCount, baseFrameDurationMs, idlePhase, stepsPerCycle);

    /// <summary>
    /// 按实际移动距离推进相位：phase = (phase + distance / stride) mod 1。
    /// 这是与移动逻辑绑定的核心；无论帧率怎么变，走过的距离与画面相位始终一一对应。
    /// </summary>
    public static double AdvancePhase(
        double phase,
        double distanceDeltaPixels,
        double stepLengthPixels,
        int stepsPerCycle = DefaultStepsPerCycle)
    {
        var current = NormalizePhase(phase);
        if (!IsFinite(distanceDeltaPixels) || distanceDeltaPixels == 0 ||
            !IsFinite(stepLengthPixels) || stepLengthPixels <= 0 ||
            stepsPerCycle <= 0)
            return current;

        var stride = stepLengthPixels * stepsPerCycle;
        return NormalizePhase(current + distanceDeltaPixels / stride);
    }

    /// <summary>由「已走距离」直接求相位，适合从存档/路径位置恢复行走画面。</summary>
    public static double PhaseForDistance(
        double distancePixels,
        double stepLengthPixels,
        int stepsPerCycle = DefaultStepsPerCycle,
        double startPhase = 0)
    {
        var current = NormalizePhase(startPhase);
        if (!IsFinite(distancePixels) || !IsFinite(stepLengthPixels) || stepLengthPixels <= 0 || stepsPerCycle <= 0)
            return current;

        return NormalizePhase(current + distancePixels / (stepLengthPixels * stepsPerCycle));
    }

    /// <summary>相位取帧：phase 按循环归一化（1 与 0 等价），结果夹在 0..frameCount-1。</summary>
    public static int FrameIndexAt(double phase, int frameCount)
    {
        if (frameCount <= 0) return 0;
        var p = NormalizePhase(phase);
        var index = (int)Math.Floor(p * frameCount);
        return Math.Clamp(index, 0, frameCount - 1);
    }

    private static double NormalizePhase(double phase)
    {
        if (!IsFinite(phase) || phase == 0) return 0;
        var wrapped = phase % 1.0;
        return wrapped < 0 ? wrapped + 1.0 : wrapped;
    }

    private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
}
