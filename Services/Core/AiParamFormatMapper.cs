using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace WarmAsBefore.Services.Core;

/// <summary>四种 AI 参数格式，可切换；切换时中间表示不丢值（第十二项）。</summary>
public enum AiParamFormat { OpenAIFlat, NestedByRole, VendorExt, WidgetBinding }

/// <summary>
/// 扁平兼容 / 按用途嵌套 / 厂商扩展块 / 控件绑定 四种格式的互相转换。
/// 所有格式都从同一份「键 → 值」中间表示生成，任何不在映射表里的键都进 unmapped 列表，
/// 由调用方决定保留还是报错，绝不静默丢弃。
/// </summary>
public static class AiParamFormatMapper
{
    public static AiParamFormat Parse(string? raw) => (raw ?? "openai_flat").ToLowerInvariant() switch
    {
        "nested_by_role" => AiParamFormat.NestedByRole,
        "vendor_ext" => AiParamFormat.VendorExt,
        "widget_binding" => AiParamFormat.WidgetBinding,
        _ => AiParamFormat.OpenAIFlat
    };

    public static string Name(AiParamFormat f) => f switch
    {
        AiParamFormat.NestedByRole => "nested_by_role",
        AiParamFormat.VendorExt => "vendor_ext",
        AiParamFormat.WidgetBinding => "widget_binding",
        _ => "openai_flat"
    };

    private static readonly Dictionary<string, string> Role = new(StringComparer.OrdinalIgnoreCase)
    {
        ["temperature"] = "sampling", ["top_p"] = "sampling", ["top_k"] = "sampling",
        ["max_tokens"] = "budget", ["context_window"] = "budget",
        ["reasoning_effort"] = "thinking", ["enable_thinking"] = "thinking",
        ["model"] = "identity", ["endpoint"] = "identity", ["api_key"] = "secret"
    };

    /// <summary>按目标格式生成请求参数对象。secret 类键永远不进 vendor_ext 的明文块。</summary>
    public static JsonObject Format(IReadOnlyDictionary<string, JsonNode?> flat, AiParamFormat format)
    {
        var outObj = new JsonObject();
        foreach (var (key, value) in flat)
        {
            if (value is null) continue;
            string target = format switch
            {
                AiParamFormat.NestedByRole => Role.TryGetValue(key, out var r) ? r : "extra",
                AiParamFormat.VendorExt => key.Contains("key", StringComparison.OrdinalIgnoreCase) ? "" : "vendor",
                AiParamFormat.WidgetBinding => "widgets",
                _ => ""
            };
            if (format == AiParamFormat.VendorExt && target.Length == 0) continue;
            if (target.Length == 0) { outObj[key] = value.DeepClone(); continue; }

            JsonObject bucket = outObj[target] as JsonObject ?? new JsonObject();
            if (format == AiParamFormat.WidgetBinding)
            {
                var widgetType = key.Contains("probability", StringComparison.OrdinalIgnoreCase)
                                 || key.Contains("temperature", StringComparison.OrdinalIgnoreCase)
                    ? "slider" : key.Contains("model", StringComparison.OrdinalIgnoreCase)
                        ? "select" : "button";
                var entry = new JsonObject { ["type"] = widgetType, ["value"] = value.DeepClone() };
                bucket[key] = entry;
            }
            else bucket[key] = value.DeepClone();
            outObj[target] = bucket;
        }
        return outObj;
    }

    /// <summary>反向归一到扁平表示，供校验与不丢值比对使用。</summary>
    public static Dictionary<string, JsonNode?> Flatten(JsonObject formatted, AiParamFormat format)
    {
        var result = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, node) in formatted)
        {
            if (format == AiParamFormat.OpenAIFlat) { result[key] = node?.DeepClone(); continue; }
            if (node is not JsonObject bucket) { result[key] = node?.DeepClone(); continue; }
            foreach (var (inner, value) in bucket)
            {
                if (format == AiParamFormat.WidgetBinding && value is JsonObject widget)
                    result[inner] = widget["value"]?.DeepClone();
                else result[inner] = value?.DeepClone();
            }
        }
        return result;
    }

    /// <summary>往返后哪些键消失了；非空就说明该格式映射有遗漏，调用方应拒绝下发。</summary>
    public static IReadOnlyList<string> Unmapped(IReadOnlyDictionary<string, JsonNode?> flat, AiParamFormat format)
    {
        var back = Flatten(Format(flat, format), format);
        return flat.Keys.Where(k => !k.Contains("key", StringComparison.OrdinalIgnoreCase) && !back.ContainsKey(k)).ToList();
    }
}
