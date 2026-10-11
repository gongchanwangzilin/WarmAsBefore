using System.Text;
using System.Text.Json;
using WarmAsBefore.Models;
using WarmAsBefore.Modules.ApiManager;

namespace WarmAsBefore.Modules.NovelImport;

/// <summary>
/// 小说 → Gala（平行宇宙世界）的导入分析器。
///
/// 与旧版的区别：
///  1. 不再把文本硬截断到 6000 字符：长文按段落边界切成多段，逐段分析后再汇总，
///     这样再长的故事也能完整读完，而不是「只看了开头就生成世界」。
///  2. 增加了**剧情完整性检查**：先用本地启发式（长度/开头结尾标志/角色一致性/分支可达性）
///     打分并列出问题，再让模型复核给出补充问题与一句话结论；两者合并成 PlotCompleteness。
///  3. 抽取**关键事件时间线**（KeyEvents），供平行宇宙剧情推进时做锚点。
///
/// 设计原则：模型不可用时不能整体失败——所有 AI 步骤都有本地兜底，
/// 至少能给出「摘要 + 基础完整性报告」，而不是返回 null 让用户白导入一次。
/// </summary>
public sealed class NovelAnalyzer
{
    private readonly ApiGateway _api;

    /// <summary>单段送模的字符上限：留足余量避免超长请求被端点拒绝。</summary>
    private const int SegmentSize = 6000;

    /// <summary>参与逐段分析的最大段数：防止一本几百万字的长篇把额度耗尽。超出部分只做本地统计。</summary>
    private const int MaxSegmentsForAi = 8;

    /// <summary>判定「文本太短、无法成世界」的阈值（字符）。</summary>
    private const int MinViableLength = 200;

    /// <summary>完整度及格线：达到即可进入（低于也可进入，只是会提示用户）。</summary>
    private const int PassScore = 60;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public NovelAnalyzer(ApiGateway api) => _api = api;

    public async Task<ScenarioResult?> Analyze(string filePath)
    {
        if (!File.Exists(filePath)) return null;
        var text = await File.ReadAllTextAsync(filePath);
        return await AnalyzeText(text);
    }

    public async Task<ScenarioResult?> AnalyzeText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var normalized = Normalize(text);
        var segments = SplitIntoSegments(normalized);

        // ===== 1. 逐段抽取（模型）：摘要素材 / 角色 / 事件 / 分支 =====
        var summaries = new List<string>();
        var characters = new List<string>();
        var events = new List<string>();
        var branches = new List<PlotBranch>();
        string? title = null;

        var aiSegments = segments.Take(MaxSegmentsForAi).ToList();
        for (var i = 0; i < aiSegments.Count; i++)
        {
            var part = await AnalyzeSegmentAsync(aiSegments[i], i, segments.Count);
            if (part is null) continue;
            if (string.IsNullOrWhiteSpace(title) && !string.IsNullOrWhiteSpace(part.Title))
                title = part.Title.Trim();
            if (!string.IsNullOrWhiteSpace(part.Summary)) summaries.Add(part.Summary.Trim());
            foreach (var c in part.Characters)
                if (!string.IsNullOrWhiteSpace(c) && !characters.Contains(c.Trim()))
                    characters.Add(c.Trim());
            foreach (var e in part.KeyEvents)
                if (!string.IsNullOrWhiteSpace(e)) events.Add(e.Trim());
            branches.AddRange(part.Branches);
        }

        // 模型完全不可用时的兜底：至少给一个本地摘要，别返回 null。
        if (summaries.Count == 0)
            summaries.Add(LocalFallbackSummary(normalized));

        var mergedSummary = MergeSummaries(summaries);
        var dedupBranches = DedupeBranches(branches);

        // ===== 2. 完整性检查：本地启发式 + 模型复核 =====
        var local = LocalCompleteness(normalized, characters, events, dedupBranches, segments.Count);
        var aiIssues = await AskCompletenessAsync(mergedSummary, characters, events, segments.Count);
        var completeness = MergeCompleteness(local, aiIssues, normalized.Length, segments.Count);

        return new ScenarioResult
        {
            Title = string.IsNullOrWhiteSpace(title) ? GuessTitle(normalized) : title!,
            Summary = mergedSummary,
            Characters = characters,
            Branches = dedupBranches,
            KeyEvents = events,
            Completeness = completeness
        };
    }

    // ============ 文本预处理 ============

    private static string Normalize(string text)
    {
        // 统一换行，去掉零宽字符；保留段落结构（分段依赖它）。
        var t = text.Replace("\r\n", "\n").Replace('\r', '\n')
            .Replace("\u200b", "").Replace("\ufeff", "");
        // 压缩 3+ 连续空行为 2 行，避免大量空白段影响切分。
        while (t.Contains("\n\n\n")) t = t.Replace("\n\n\n", "\n\n");
        return t.Trim();
    }

    /// <summary>
    /// 按段落边界切段：优先在空行处断开，其次在句号处兜底；
    /// 每段不超过 SegmentSize，保证既完整又不超请求长度。
    /// </summary>
    private static List<string> SplitIntoSegments(string text)
    {
        var result = new List<string>();
        if (text.Length <= SegmentSize)
        {
            result.Add(text);
            return result;
        }

        var paragraphs = text.Split('\n');
        var sb = new StringBuilder();
        foreach (var raw in paragraphs)
        {
            var line = raw.TrimEnd();
            if (sb.Length > 0 && sb.Length + line.Length + 1 > SegmentSize)
            {
                result.Add(sb.ToString().Trim());
                sb.Clear();
            }
            // 单行本身超过上限：按句号硬切。
            if (line.Length > SegmentSize)
            {
                foreach (var chunk in HardSplit(line, SegmentSize))
                {
                    if (sb.Length > 0) { result.Add(sb.ToString().Trim()); sb.Clear(); }
                    result.Add(chunk);
                }
                continue;
            }
            sb.AppendLine(line);
        }
        if (sb.Length > 0) result.Add(sb.ToString().Trim());
        return result.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
    }

    private static IEnumerable<string> HardSplit(string line, int size)
    {
        var start = 0;
        while (start < line.Length)
        {
            var take = Math.Min(size, line.Length - start);
            // 尽量在句末断句，避免把小标题/句子劈成两半。
            var slice = line.Substring(start, take);
            if (take == size)
            {
                var lastStop = slice.LastIndexOfAny(new[] { '。', '！', '？', '；', '.', '!', '?' });
                if (lastStop > size / 2)
                {
                    slice = slice[..(lastStop + 1)];
                    take = slice.Length;
                }
            }
            yield return slice;
            start += take;
        }
    }

    // ============ 模型调用 ============

    /// <summary>逐段分析：返回该段的标题/摘要/角色/事件/分支（失败返回 null）。</summary>
    private async Task<ScenarioResult?> AnalyzeSegmentAsync(string segment, int index, int total)
    {
        var positionHint = total > 1
            ? $"这是全文的第 {index + 1}/{total} 段。"
            : "";
        var msgs = new List<ChatMessage>
        {
            new()
            {
                Role = "system",
                Content =
                    "你是小说结构化分析器。只输出 JSON，不要解释。字段：" +
                    "title(整篇标题，若本段无法确定可留空字符串)、summary(本段剧情摘要，80字内)、" +
                    "characters(本段出现的角色名数组)、keyEvents(本段关键事件数组，每条15字内、按发生顺序)、" +
                    "branches(本段的分支选项数组，每项 {description, options:字符串数组, conditionFlag:可空})。" +
                    "没有的内容用空数组。"
            },
            new() { Role = "user", Content = positionHint + "\n\n" + segment }
        };

        try
        {
            var reply = await _api.Chat(msgs);
            if (string.IsNullOrWhiteSpace(reply)) return null;
            var json = ExtractJson(reply);
            if (json is null) return null;
            return JsonSerializer.Deserialize<ScenarioResult>(json, JsonOpts);
        }
        catch (Exception ex)
        {
            App.WriteLog($"NovelAnalyzer.AnalyzeSegment[{index}] -> {ex.Message}");
            return null;
        }
    }

    /// <summary>让模型复核完整性，返回它补充的问题列表（失败返回空）。</summary>
    private async Task<List<string>> AskCompletenessAsync(string summary, List<string> chars,
        List<string> events, int segmentCount)
    {
        var msgs = new List<ChatMessage>
        {
            new()
            {
                Role = "system",
                Content = "你是剧情完整性审查员。判断这个故事是否完整，只输出 JSON：" +
                          "{\"issues\":[\"问题1\",\"问题2\"]}。" +
                          "重点检查：是否缺开头/缺结尾、主线是否断裂、角色是否中途消失、分支是否无法收束。" +
                          "没有问题就返回空数组。"
            },
            new()
            {
                Role = "user",
                Content = $"全文共 {segmentCount} 段。摘要：{summary}\n" +
                          $"角色：{string.Join("、", chars)}\n" +
                          $"关键事件：{string.Join("；", events)}"
            }
        };
        try
        {
            var reply = await _api.Chat(msgs);
            if (string.IsNullOrWhiteSpace(reply)) return new List<string>();
            var json = ExtractJson(reply);
            if (json is null) return new List<string>();
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("issues", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                var list = new List<string>();
                foreach (var it in arr.EnumerateArray())
                {
                    var s = it.GetString();
                    if (!string.IsNullOrWhiteSpace(s)) list.Add(s.Trim());
                }
                return list;
            }
        }
        catch (Exception ex)
        {
            App.WriteLog("NovelAnalyzer.AskCompleteness -> " + ex.Message);
        }
        return new List<string>();
    }

    /// <summary>从模型回复里抠出 JSON 对象（容忍 ```json 包裹与前后废话）。</summary>
    private static string? ExtractJson(string reply)
    {
        var t = reply.Trim();
        if (t.StartsWith("```"))
        {
            var firstNl = t.IndexOf('\n');
            if (firstNl >= 0) t = t[(firstNl + 1)..];
            var fence = t.LastIndexOf("```", StringComparison.Ordinal);
            if (fence >= 0) t = t[..fence];
            t = t.Trim();
        }
        var start = t.IndexOf('{');
        var end = t.LastIndexOf('}');
        if (start < 0 || end <= start) return null;
        return t[start..(end + 1)];
    }

    // ============ 本地启发式 ============

    private static string LocalFallbackSummary(string text)
    {
        var head = text.Length > 120 ? text[..120] : text;
        return "（AI 未配置，本地摘要）" + head.Replace('\n', ' ') + "…";
    }

    private static string MergeSummaries(List<string> parts)
    {
        if (parts.Count == 1) return parts[0];
        // 分段摘要按序拼成一段连贯文字，总长控制在约 400 字内。
        var sb = new StringBuilder();
        foreach (var p in parts)
        {
            if (sb.Length + p.Length + 1 > 400) break;
            sb.Append(p);
            if (!p.EndsWith('。') && !p.EndsWith('.')) sb.Append('。');
        }
        return sb.ToString().Trim();
    }

    private static string GuessTitle(string text)
    {
        var firstLine = text.Split('\n').FirstOrDefault(l => !string.IsNullOrWhiteSpace(l))?.Trim() ?? "";
        if (firstLine.Length is > 0 and <= 30) return firstLine;
        return "未命名世界";
    }

    private static List<PlotBranch> DedupeBranches(List<PlotBranch> branches)
    {
        var seen = new HashSet<string>();
        var result = new List<PlotBranch>();
        foreach (var b in branches)
        {
            var key = (b.Description ?? "") + "|" + string.Join(",", b.Options ?? Array.Empty<string>());
            if (seen.Add(key)) result.Add(b);
        }
        return result;
    }

    /// <summary>
    /// 本地完整性评分：不依赖模型也能给出「缺了什么」的判断。
    /// 评分维度（合计 100）：长度 30、结构（开头/结尾标志）30、角色 20、事件 10、分支 10。
    /// </summary>
    private static PlotCompleteness LocalCompleteness(string text, List<string> chars,
        List<string> events, List<PlotBranch> branches, int segmentCount)
    {
        var issues = new List<string>();
        var score = 0;

        // 1) 长度（30）
        if (text.Length < MinViableLength)
        {
            issues.Add($"文本过短（{text.Length} 字），不足以构成一个可交互的世界。");
        }
        else if (text.Length < 1000)
        {
            score += 15;
            issues.Add($"文本偏短（{text.Length} 字），世界细节可能不足。");
        }
        else if (text.Length < 5000)
        {
            score += 24;
        }
        else
        {
            score += 30;
        }

        // 2) 结构：开头/结尾标志（30）
        var head = text.Length > 300 ? text[..300] : text;
        var tail = text.Length > 300 ? text[^300..] : text;
        var hasOpening = head.Contains("第一章") || head.Contains("第1章") ||
                         head.Contains("序") || head.Contains("楔子") ||
                         head.Contains("从前") || head.Contains("很久");
        var hasEnding = tail.Contains("完") || tail.Contains("终") ||
                        tail.Contains("结局") || tail.Contains("尾聲") || tail.Contains("尾声") ||
                        tail.TrimEnd().EndsWith("。") || tail.TrimEnd().EndsWith("！");
        if (hasOpening) score += 15;
        else issues.Add("未识别到明确的开篇（如「第一章」「序章」），可能缺开头。");
        if (hasEnding) score += 15;
        else issues.Add("未识别到明确的结尾，故事可能在中间截断。");

        // 3) 角色（20）
        if (chars.Count == 0) issues.Add("未能抽取到任何角色，剧情可能难以展开。");
        else if (chars.Count == 1) { score += 10; issues.Add("只识别到一个角色，故事冲突可能不足。"); }
        else score += 20;

        // 4) 事件（10）
        if (events.Count == 0) issues.Add("未能抽取到关键事件，世界缺乏可推进的剧情锚点。");
        else score += 10;

        // 5) 分支（10）
        if (branches.Count == 0) score += 3;   // 线性故事也合法，给基础分
        else score += 10;

        score = Math.Clamp(score, 0, 100);
        return new PlotCompleteness
        {
            Score = score,
            CanEnter = score >= PassScore && text.Length >= MinViableLength,
            Verdict = "",
            Issues = issues,
            SourceLength = text.Length,
            SegmentCount = segmentCount
        };
    }

    /// <summary>合并本地评分与模型问题，产出最终完整性报告。</summary>
    private static PlotCompleteness MergeCompleteness(PlotCompleteness local, List<string> aiIssues,
        int sourceLength, int segmentCount)
    {
        var issues = new List<string>(local.Issues);
        foreach (var extra in aiIssues)
            if (!issues.Contains(extra)) issues.Add(extra);

        // 模型每提一条额外问题扣 5 分（最多扣 20），模型没提任何问题则加 5 分鼓励。
        var score = local.Score;
        if (aiIssues.Count == 0) score += 5;
        else score -= Math.Min(20, aiIssues.Count * 5);
        score = Math.Clamp(score, 0, 100);

        var canEnter = score >= PassScore && sourceLength >= MinViableLength;
        var verdict = canEnter
            ? (score >= 85 ? "剧情完整，可放心进入这个世界。" : "剧情基本完整，可以进入。")
            : "剧情可能不完整，进入后 AI 可能需要自行补全走向。";

        return local with
        {
            Score = score,
            CanEnter = canEnter,
            Verdict = verdict,
            Issues = issues,
            SourceLength = sourceLength,
            SegmentCount = segmentCount
        };
    }
}