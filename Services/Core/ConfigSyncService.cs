using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace WarmAsBefore.Services.Core;

/// <summary>一条配置的生效值；Overridden 为 true 表示被保守规则覆盖过，Reason 是给用户看的中文原因。</summary>
public sealed record EffectiveValue(string Key, string Value, bool Overridden, string Reason);

/// <summary>一次解析的完整结果：126 个键的生效值 + 被覆盖的项列表。</summary>
public sealed record EffectiveConfig(
    IReadOnlyDictionary<string, string> Values,
    IReadOnlyList<EffectiveValue> Overrides);

/// <summary>
/// 配置同步的纯编排层：用户设定值 → 类型解析与数值夹取 → 冲突保守覆盖 → 生效值。
/// 不落盘、不读文件、不发网络，输入是什么就只依赖什么，便于单测。
///
/// 保守覆盖语义：只改生效值，不改写用户原值。冲突消失或玩家确认后，原设定自动恢复。
/// detectedConflicts 传入「模组侧确实探测到冲突」的键；只有明确命中才回落。
/// 探测能力尚未上报时传空集合，用户设定照常生效，提示文案仍随字段表下发，不会把新功能锁死。
/// </summary>
public static class ConfigSyncService
{
    public static EffectiveConfig Resolve(
        IReadOnlyDictionary<string, string> userValues,
        IEnumerable<string>? detectedConflicts = null)
    {
        var detected = new HashSet<string>(detectedConflicts ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var overrides = new List<EffectiveValue>();

        foreach (var (key, entry) in ConfigCatalog.Entries)
        {
            string raw = userValues.TryGetValue(key, out var saved) && saved is not null ? saved : entry.Default;
            string parsed = ParseValue(key, entry, raw);

            bool needOverride = entry.Conflict
                                && detected.Contains(key)
                                && !string.Equals(parsed, entry.Default, StringComparison.Ordinal);
            string effective = needOverride ? entry.Default : parsed;
            values[key] = effective;
            if (needOverride)
            {
                overrides.Add(new EffectiveValue(key, effective, true,
                    string.IsNullOrWhiteSpace(entry.PromptText) ? "检测到可能与附属模组冲突，已回落到保守默认值。" : entry.PromptText));
            }
        }

        return new EffectiveConfig(values, overrides);
    }

    /// <summary>类型解析与夹取。规则显式写在这里，不靠调用方猜。</summary>
    public static string ParseValue(string key, ConfigCatalog.Entry entry, string raw)
    {
        string text = (raw ?? string.Empty).Trim();
        switch (entry.Type)
        {
            case "switch":
                if (bool.TryParse(text, out bool b)) return b ? "true" : "false";
                return entry.Default;
            case "number":
                if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double n)
                    || double.IsNaN(n) || double.IsInfinity(n))
                    return entry.Default;
                // 概率类固定夹到 [0,1]；其余数值只保证非负，避免负间隔/负步数。
                n = key.Contains("probability", StringComparison.OrdinalIgnoreCase)
                    || key.Contains("chance", StringComparison.OrdinalIgnoreCase)
                    ? Math.Clamp(n, 0, 1)
                    : Math.Max(0, n);
                return n.ToString("G", CultureInfo.InvariantCulture);
            case "select":
            case "multiselect":
                return string.IsNullOrWhiteSpace(text) ? entry.Default : text;
            default:
                return text;
        }
    }

    /// <summary>插件现有 24 项的键不在目录里，原样透传，不参与解析。</summary>
    public static bool IsCatalogKey(string key) => ConfigCatalog.Entries.ContainsKey(key);
}
