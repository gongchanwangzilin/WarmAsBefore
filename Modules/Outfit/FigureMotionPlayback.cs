namespace WarmAsBefore.Modules.Outfit;

/// <summary>
/// 动作的查找与按时间取帧。真正的行走节拍仍由 <see cref="WalkCycle"/> 按位移计算；
/// 这里只负责回答「哪个动作叫 walk」「非行走动作播到第几帧」这类与画面无关的问题，
/// 让主界面、工作室预览和形象包页共用同一套判定。
/// </summary>
public static class FigureMotionPlayback
{
    private static readonly string[] WalkIds = { "walk", "walking", "走路", "行走" };

    /// <summary>找包里的行走动作；没有整身走路帧时返回 null，调用方保持静态立绘。</summary>
    public static FigureMotion? FindWalk(FigurePack? pack)
    {
        if (pack?.Motions is null) return null;
        return pack.Motions.FirstOrDefault(m =>
            m is not null && m.Frames is { Count: > 0 } && IsWalk(m));
    }

    public static bool IsWalk(FigureMotion? motion)
    {
        if (motion is null) return false;
        var id = (motion.Id ?? "").Trim();
        var name = (motion.Name ?? "").Trim();
        return WalkIds.Any(k => id.Equals(k, StringComparison.OrdinalIgnoreCase))
               || WalkIds.Any(k => name.Equals(k, StringComparison.OrdinalIgnoreCase))
               || id.Contains("walk", StringComparison.OrdinalIgnoreCase)
               || name.Contains("walk", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>动作帧的基准时长：逐帧时长通常一致，取平均给 WalkCycle 作基准帧率。</summary>
    public static double AverageFrameDurationMs(FigureMotion? motion)
    {
        var frames = motion?.Frames;
        if (frames is null || frames.Count == 0) return 100;
        var total = frames.Sum(f => Math.Clamp(f?.DurationMs ?? 100, 16, 10_000));
        return Math.Clamp(total / (double)frames.Count, 16, 10_000);
    }

    /// <summary>
    /// 非行走动作按累计时长取帧。循环动作对总时长取模；一次性动作播完停在最后一帧。
    /// </summary>
    public static int FrameIndexAtElapsedMs(FigureMotion? motion, double elapsedMs)
    {
        var frames = motion?.Frames;
        if (frames is null || frames.Count == 0) return 0;
        if (!double.IsFinite(elapsedMs) || elapsedMs <= 0) return 0;

        var durations = frames
            .Select(f => Math.Clamp(f?.DurationMs ?? 100, 16, 10_000))
            .ToList();
        var total = durations.Sum();
        if (total <= 0) return 0;

        var at = motion!.Loop ? elapsedMs % total : Math.Min(elapsedMs, total);
        var acc = 0d;
        for (var i = 0; i < durations.Count; i++)
        {
            acc += durations[i];
            if (at < acc) return i;
        }
        return frames.Count - 1;
    }
}
