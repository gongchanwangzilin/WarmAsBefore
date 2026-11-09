using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace WarmAsBefore.Services.Core;

/// <summary>套餐只覆盖端点/模型/推荐参数，绝不预置密钥，也不代用户选择（默认 custom）。</summary>
public sealed record ModelPackage(string Id, string Label, string Endpoint, string Model,
                                  double Temperature, int ContextWindow, bool SupportsTools,
                                  bool SupportsVision, bool SupportsReasoningEffort);

public static class ModelPackagePresets
{
    public const string DefaultId = "custom";

    private static readonly Dictionary<string, ModelPackage> All = new(StringComparer.OrdinalIgnoreCase)
    {
        ["custom"] = new("custom", "自定义（不动已填配置）", "", "", 0.7, 8192, true, false, false),
        ["gpt"] = new("gpt", "GPT 套餐", "https://api.openai.com/v1", "gpt-4o-mini", 0.7, 128000, true, true, true),
        ["claude"] = new("claude", "Claude 套餐", "https://api.anthropic.com/v1", "claude-3-5-sonnet", 0.7, 200000, true, true, false),
        ["openai_compatible"] = new("openai_compatible", "OpenAI 兼容端点", "", "", 0.7, 8192, true, false, false),
        ["deepseek"] = new("deepseek", "DeepSeek 套餐", "https://api.deepseek.com/v1", "deepseek-chat", 0.7, 64000, true, false, false)
    };

    public static ModelPackage Get(string? id) =>
        id is not null && All.TryGetValue(id, out var p) ? p : All[DefaultId];

    public static IReadOnlyList<ModelPackage> List() => new List<ModelPackage>(All.Values);

    /// <summary>套餐里为空串的端点/模型不覆盖用户已填值；密钥永远不写入。</summary>
    public static JsonObject Apply(string? id, JsonObject current)
    {
        var p = Get(id);
        var next = (JsonObject)current.DeepClone();
        if (p.Endpoint.Length > 0) next["endpoint"] = p.Endpoint;
        if (p.Model.Length > 0) next["model"] = p.Model;
        next["temperature"] = p.Temperature;
        next["contextWindow"] = p.ContextWindow;
        next["supportsTools"] = p.SupportsTools;
        next["supportsVision"] = p.SupportsVision;
        next["supportsReasoningEffort"] = p.SupportsReasoningEffort;
        next["package"] = p.Id;
        return next;
    }
}
