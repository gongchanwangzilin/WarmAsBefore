using WarmAsBefore.Models;

namespace WarmAsBefore.Services.Core;

/// <summary>
/// F25：一个模型「可选使用」的界面能力位。
/// 热度调节条、思考等级条、思考开关按钮、快速按钮 —— 有的模型有，有的模型没有，
/// 在「设置 → 模型管理 → 某款模型」里逐款勾选；没勾就不在对话页显示。
///
/// 默认全 true：旧存档 / 手填模型名（不在模型库里）不会突然少控件。
/// </summary>
public sealed record ModelCapabilitySet
{
    public bool HeatSlider { get; init; } = true;
    public bool ThinkTierBar { get; init; } = true;
    public bool ThinkToggle { get; init; } = true;
    public bool QuickButtons { get; init; } = true;

    public static readonly ModelCapabilitySet All = new();

    public string Summary
    {
        get
        {
            var parts = new List<string>();
            if (HeatSlider) parts.Add("热度条");
            if (ThinkTierBar) parts.Add("思考条");
            if (ThinkToggle) parts.Add("思考开关");
            if (QuickButtons) parts.Add("快速钮");
            return parts.Count == 0 ? "不显示对话控件" : string.Join(" · ", parts);
        }
    }
}

/// <summary>按当前主对话模型名，从模型库里解析能力位。纯函数，零 IO，可单测。</summary>
public static class ModelCapabilities
{
    /// <summary>
    /// modelName 为空、或模型不在 <paramref name="providers"/> 里（用户手填的名字）时返回全开，
    /// 保持旧行为；只有模型库里明确勾掉的位才隐藏对应控件。
    /// </summary>
    public static ModelCapabilitySet Resolve(string? modelName, IReadOnlyList<AiProvider>? providers)
    {
        if (string.IsNullOrWhiteSpace(modelName) || providers is null || providers.Count == 0)
            return ModelCapabilitySet.All;
        foreach (var p in providers)
        {
            if (p?.Models is null) continue;
            foreach (var m in p.Models)
            {
                if (m is null || !string.Equals(m.Model, modelName.Trim(), StringComparison.OrdinalIgnoreCase))
                    continue;
                return new ModelCapabilitySet
                {
                    HeatSlider = m.ShowHeatSlider,
                    ThinkTierBar = m.ShowThinkTierBar,
                    ThinkToggle = m.ShowThinkToggle,
                    QuickButtons = m.ShowQuickButtons
                };
            }
        }
        return ModelCapabilitySet.All;
    }
}
