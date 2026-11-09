using System;

namespace WarmAsBefore.Services.Core;

/// <summary>思考模式与深度思考自动关闭（第十一项）：纯代码判定，不调模型，省 Token。</summary>
public static class ThinkModeController
{
    /// <summary>返回 0=off / 1=fast / 2=deep；模型不支持推理强度时永不返回 deep。</summary>
    public static int Resolve(string? mode, bool modelSupportsDeep, double heat, double threshold)
    {
        return (mode ?? "off").ToLowerInvariant() switch
        {
            "fast" => 1,
            "deep" => modelSupportsDeep ? 2 : 1,
            "auto" => modelSupportsDeep && heat >= threshold ? 2 : 1,
            _ => 0
        };
    }

    /// <summary>热度低于阈值自动关深度思考；AI 热度由机制值给，不让模型自己判断。</summary>
    public static bool AutoDisableDeepThink(double heat, double threshold) => heat < threshold;

    /// <summary>AI 热度来源：mechanism（机制值，默认）/ ai（额外一次模型调用）/ manual（玩家手填）。</summary>
    public static double ResolveHeat(string? source, double mechanismHeat, double aiHeat, double manualHeat) =>
        (source ?? "code").ToLowerInvariant() switch
        {
            "ai" => Math.Clamp(aiHeat, 0, 1),
            "manual" => Math.Clamp(manualHeat, 0, 1),
            _ => Math.Clamp(mechanismHeat, 0, 1)
        };
}
