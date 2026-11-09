using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace WarmAsBefore.Services.Core;

/// <summary>从不支持 function calling 的小模型输出里解析出的一次工具调用。</summary>
public sealed record ParsedToolCall(string Tool, string ArgumentsJson, string Format);

/// <summary>
/// 小模型强兼容：把 XML 或 {@code {工具名, 参数...}} 格式的自由文本解析成工具调用。
/// 确定性文法 + 白名单收敛，解析失败不重试（重试会重复烧 Token），由调用方回落成普通对话。
/// </summary>
public static class SmallModelToolParser
{
    private static readonly Regex XmlTool = new(
        @"<tool\s+name\s*=\s*[""'](?<n>[^""']+)[""']\s*>(?<a>.*?)</tool>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex XmlArg = new(
        @"<arg\s+name\s*=\s*[""'](?<n>[^""']+)[""']\s*>(?<v>.*?)</arg>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex BraceTool = new(
        @"\{\s*(?<n>[A-Za-z_][A-Za-z0-9_]*)\s*(?:,\s*(?<a>.*?))?\}",
        RegexOptions.Singleline | RegexOptions.Compiled);

    /// <summary>只放行 maid_* 白名单里的工具；世界动作类另由 MaidBridgeService 的权限闸门再判一次。</summary>
    public static IReadOnlyList<ParsedToolCall> Parse(string text, string format, IReadOnlySet<string>? whitelist = null)
    {
        var result = new List<ParsedToolCall>();
        if (string.IsNullOrWhiteSpace(text)) return result;
        string mode = (format ?? "xml").ToLowerInvariant();
        if (mode == "xml")
        {
            foreach (Match m in XmlTool.Matches(text))
            {
                string tool = m.Groups["n"].Value.Trim();
                if (!Allowed(tool, whitelist)) continue;
                result.Add(new ParsedToolCall(tool, ArgsToJson(m.Groups["a"].Value), "xml"));
            }
        }
        else if (mode == "json_brace" || mode == "brace")
        {
            foreach (Match m in BraceTool.Matches(text))
            {
                string tool = m.Groups["n"].Value.Trim();
                if (!Allowed(tool, whitelist)) continue;
                result.Add(new ParsedToolCall(tool, ArgsToJson(m.Groups["a"].Value), "json_brace"));
            }
        }
        return result;
    }

    /// <summary>注入到系统提示词里的格式引导；明确列出可用工具，避免模型瞎编工具名。</summary>
    public static string PromptHint(string format, IEnumerable<string> allowedTools)
    {
        string tools = string.Join("、", (allowedTools ?? Array.Empty<string>()).Take(24));
        string mode = (format ?? "xml").ToLowerInvariant();
        string example = mode == "json_brace" || mode == "brace"
            ? "{maid_status, maid:\"小灵\"}"
            : "<tool name=\"maid_status\"><arg name=\"maid\">小灵</arg></tool>";
        return $"需要调用工具时，只输出这种格式：{example}。可用工具：{tools}。"
               + "不要输出其它文字，也不要输出未列出的工具名；不需要工具时正常说话即可。";
    }

    private static bool Allowed(string tool, IReadOnlySet<string>? whitelist)
    {
        if (whitelist is null || whitelist.Count == 0) return tool.StartsWith("maid_", StringComparison.Ordinal);
        return whitelist.Contains(tool);
    }

    /// <summary>把 key=value 或 XML arg 片段转成最小 JSON 对象；无法解析的值按字符串处理。</summary>
    private static string ArgsToJson(string raw)
    {
        var pairs = new List<(string Key, string Value)>();
        foreach (Match m in XmlArg.Matches(raw))
        {
            pairs.Add((m.Groups["n"].Value.Trim(), m.Groups["v"].Value.Trim()));
        }
        if (pairs.Count == 0 && !string.IsNullOrWhiteSpace(raw))
        {
            foreach (string part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = part.IndexOf('=');
                if (eq <= 0) continue;
                pairs.Add((part[..eq].Trim().Trim('"', '\''), part[(eq + 1)..].Trim().Trim('"', '\'')));
            }
        }
        var sb = new StringBuilder("{");
        for (int i = 0; i < pairs.Count; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(JsonString(pairs[i].Key)).Append(':').Append(JsonString(pairs[i].Value));
        }
        return sb.Append('}').ToString();
    }

    private static string JsonString(string value)
    {
        // 手写最小转义，避免为了一个字符串引入额外序列化依赖。
        string escaped = (value ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"")
            .Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");
        return "\"" + escaped + "\"";
    }
}
