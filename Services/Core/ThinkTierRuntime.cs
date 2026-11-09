using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace WarmAsBefore.Services.Core;

/// <summary>
/// 第十四项 · 思考档位的运行时状态（单例）。思考条与快速按钮都绑定到它：
/// 它持有「是否显示思考条 / 是否显示快速按钮 / 档位数 / 各档标签 / 当前档位 / 是否正在深度思考」等可绑定状态，
/// 并把用户在对话里切换档位的行为通过 <see cref="OnTierSelected"/> 回传给宿主（更新请求端点 + 持久化）。
/// 与设置页是「配置」关系：设置页改的是档位数/标签/开关，运行时改的是当前档位。
/// </summary>
public sealed partial class ThinkTierRuntime : ObservableObject
{
    /// <summary>是否显示思考条（正在思考 / 深度思考中 的进度指示）。</summary>
    [ObservableProperty] private bool _isBarVisible;

    /// <summary>是否显示快速按钮（切换思考档位）。</summary>
    [ObservableProperty] private bool _isButtonsVisible;

    /// <summary>思考档位数（不含关闭档），钳到 1~5。</summary>
    [ObservableProperty] private int _tierCount = 3;

    /// <summary>当前生效的思考档位（0=关闭，1..TierCount）。</summary>
    [ObservableProperty] private int _activeTier;

    /// <summary>是否正在深度思考（档位>0 且请求进行中）。供思考条展示进度/转动。</summary>
    [ObservableProperty] private bool _isDeepThinking;

    /// <summary>当前档位的展示名（如「标准」/「关闭」），供思考条直接显示。</summary>
    [ObservableProperty] private string _currentTierLabel = "关闭";

    /// <summary>注入请求体的推理强度参数键名（如 reasoning_effort）。</summary>
    [ObservableProperty] private string _paramKey = "reasoning_effort";

    /// <summary>每档对应的参数值（逗号分隔，共 TierCount 个），按 (档位-1) 取用。</summary>
    [ObservableProperty] private string _paramValues = "low,medium,high";

    /// <summary>按钮列表：第 0 项永远为「关闭」，其后依次为各档标签；IsActive 随 ActiveTier 变化。</summary>
    public ObservableCollection<ThinkTierItem> TierItems { get; } = new();

    /// <summary>档位被用户（快速按钮）切换时回传宿主：更新请求端点 + 持久化当前档位。</summary>
    public Action<int>? OnTierSelected { get; set; }

    public ThinkTierRuntime() => SetTierCommand = new RelayCommand<int>(SetTier);

    /// <summary>快速按钮点击：把 {index} 作为目标档位（0=关闭）。</summary>
    public IRelayCommand<int> SetTierCommand { get; }

    /// <summary>
    /// 由 RuntimeConfigurator.Apply() 在每次设置生效时调用：把「设置页配置」灌进运行时状态。
    /// 这里只更新状态，不触发 <see cref="OnTierSelected"/>（那代表用户在对话里主动切档，而非配置加载）。
    /// </summary>
    public void Configure(bool barVisible, bool buttonsVisible, int tierCount, string labels,
        int activeTier, string paramKey, string paramValues)
    {
        TierCount = Math.Clamp(tierCount, 1, 5);
        ParamKey = string.IsNullOrWhiteSpace(paramKey) ? "reasoning_effort" : paramKey;
        ParamValues = string.IsNullOrWhiteSpace(paramValues) ? "low,medium,high" : paramValues;
        IsBarVisible = barVisible;
        IsButtonsVisible = buttonsVisible;
        RebuildItems(labels);
        ActiveTier = Math.Clamp(activeTier, 0, TierCount);
    }

    private void RebuildItems(string labels)
    {
        var list = new System.Collections.Generic.List<string> { "关闭" };
        if (!string.IsNullOrWhiteSpace(labels))
        {
            foreach (var p in labels.Split(',', System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries))
                if (p.Length > 0) list.Add(p);
        }
        while (list.Count - 1 < TierCount) list.Add($"档{list.Count}");
        while (list.Count - 1 > TierCount) list.RemoveAt(list.Count - 1);

        TierItems.Clear();
        for (var i = 0; i < list.Count; i++)
            TierItems.Add(new ThinkTierItem { Index = i, Label = list[i] });
        SyncActive();
    }

    private void SyncActive()
    {
        foreach (var it in TierItems) it.IsActive = it.Index == ActiveTier;
        CurrentTierLabel = ActiveTier >= 0 && ActiveTier < TierItems.Count ? TierItems[ActiveTier].Label : "关闭";
    }

    /// <summary>用户在对话里点按钮切档：更新当前档位并回传宿主。</summary>
    public void SetTier(int tier)
    {
        var clamped = Math.Clamp(tier, 0, TierCount);
        if (clamped == ActiveTier) return;
        ActiveTier = clamped;
        OnTierSelected?.Invoke(clamped);
    }

    /// <summary>请求开始/结束时由 ChatEngine 调用，驱动思考条的进度/转动指示。</summary>
    public void SetDeepThinking(bool value) => IsDeepThinking = value;

    /// <summary>给定档位，算出要注入请求体的参数值（档位≤0 返回 null）。</summary>
    public static string? ParamValueFor(int tier, string paramValues)
    {
        if (tier <= 0) return null;
        var parts = (paramValues ?? "").Split(',',
            System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return null;
        return parts[(tier - 1) % parts.Length];
    }

    partial void OnActiveTierChanged(int value) => SyncActive();
}

/// <summary>思考条上的一个档位按钮（「关闭」+ 各档）。IsActive 决定高亮。</summary>
public sealed partial class ThinkTierItem : ObservableObject
{
    [ObservableProperty] private string _label = "";
    [ObservableProperty] private int _index;
    [ObservableProperty] private bool _isActive;
}
