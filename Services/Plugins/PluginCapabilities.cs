using System.Collections.Generic;
using System.Linq;

namespace WarmAsBefore.Services;

/// <summary>
/// 一个插件在运行时自报的能力快照（握手结果）。
///
/// <para>
/// 为什么要有它：模组那条链已经有能力握手的实现（<c>capabilities.protocol/methods/features</c>），
/// 但那是桥内部的一份状态，插件中心看不到、别的插件也用不上。这里把它提到插件层的公共位置——
/// 任何插件都可以在运行时上报自己能做什么，宿主按同一份数据决定「能不能露出相关入口」。
/// </para>
/// <para>
/// 两种来源：运行时上报（如 WAB 桥在 hello 帧里报 methods/features）优先；
/// 没有运行时的插件用 <c>plugin.json</c> 里声明的 <c>capabilities</c> 兜底。
/// 上报过就以上报为准——声明是作者的意图，上报是对端当前版本的事实。
/// </para>
/// </summary>
public sealed record PluginCapabilityReport
{
    public string PluginId { get; init; } = "";

    /// <summary>对端协议版本；0 表示没报。</summary>
    public int Protocol { get; init; }

    /// <summary>对端实际放行的方法名（小写、去重）。</summary>
    public IReadOnlyCollection<string> Methods { get; init; } = Array.Empty<string>();

    /// <summary>面向功能的开关名（如 hug / headpat / plan）。</summary>
    public IReadOnlyCollection<string> Features { get; init; } = Array.Empty<string>();

    /// <summary>这一份从哪来：运行时上报填对端构建号或 "runtime"，声明兜底填 "plugin.json"。</summary>
    public string Source { get; init; } = "";

    public DateTime At { get; init; } = DateTime.Now;

    /// <summary>对端是否真的报了东西。没报 ≠ 什么都不会，只表示这一版不会说。</summary>
    public bool Reported => Methods.Count > 0 || Features.Count > 0;

    /// <summary>这个方法 / 功能名对端支持吗（大小写不敏感，方法和功能两个集合都查）。</summary>
    public bool Supports(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        return Methods.Contains(name, StringComparer.OrdinalIgnoreCase)
            || Features.Contains(name, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>设置页用的一行话：说清「谁、哪一版、支持什么」，不报数字堆砌。</summary>
    public string Summary()
    {
        if (!Reported) return "对端没有上报能力（版本较旧），相关入口按不支持处理。";
        var parts = new List<string>();
        if (Protocol > 0) parts.Add($"协议 {Protocol}");
        if (Methods.Count > 0) parts.Add($"{Methods.Count} 个方法");
        if (Features.Count > 0) parts.Add("功能：" + string.Join(" / ", Features));
        var head = parts.Count == 0 ? "已上报能力" : string.Join(" · ", parts);
        return Source.Length > 0 ? $"{head}（{Source}）" : head;
    }
}

/// <summary>
/// 插件能力的运行时登记处：进程内单例，桥连上/断开时写，插件中心与工具可见性读。
/// 只用一把锁，读写都是毫秒级，不涉及 IO。
/// </summary>
public static class PluginCapabilityRegistry
{
    private static readonly object _gate = new();
    private static readonly Dictionary<string, PluginCapabilityReport> _reports =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>写一份上报（同一插件覆盖上一份）。</summary>
    public static void Set(string pluginId, PluginCapabilityReport report)
    {
        if (string.IsNullOrWhiteSpace(pluginId)) return;
        lock (_gate) _reports[pluginId] = report with { PluginId = pluginId, At = DateTime.Now };
    }

    /// <summary>会话断开：这份能力当场作废，不给「沿用上一台游戏」留窗口。</summary>
    public static void Revoke(string pluginId)
    {
        if (string.IsNullOrWhiteSpace(pluginId)) return;
        lock (_gate) _reports.Remove(pluginId);
    }

    /// <summary>读一份上报；没有就返回 null（调用方决定是按不支持还是按声明兜底）。</summary>
    public static PluginCapabilityReport? Report(string pluginId)
    {
        if (string.IsNullOrWhiteSpace(pluginId)) return null;
        lock (_gate) return _reports.TryGetValue(pluginId, out var r) ? r : null;
    }

    /// <summary>这个插件现在支持某个方法 / 功能吗。</summary>
    public static bool Supports(string pluginId, string name) => Report(pluginId)?.Supports(name) ?? false;
}
