using WarmAsBefore.Models;

namespace WarmAsBefore.Services;

/// <summary>
/// 智能省钱：把「花了多少钱」和「接下来该不该总结」都算清楚。
///
/// 三个判断依据，缺一不可：
/// · 真实用量——每次请求端点返回的 usage 都记下来（不估，用真数）；
/// · 单价——用户录入的每百万 token 报价，按输入/输出分开算；
/// · 场景——编程/办公类对话里盲目折叠会丢细节，AI 往往要重新读一遍，反倒更贵，
///   所以这类场景给「现在总结」加惩罚项，倾向不总结。
///
/// 预测口径：比较「现在总结」与「不总结」在接下来两轮对话上的总花费，取更省的那个。
/// </summary>
public sealed class CostEstimatorService
{
    private const string UsageKey = "token_usage";
    private const string PriceKey = "model_prices";
    private const int MaxRecords = 2000;

    /// <summary>每轮对话自然增长的大致 token（用户提问 + 回复）。</summary>
    public const int TurnGrowthTokens = 220;

    /// <summary>折叠总结自身的输出上限（与 ChatEngine 的总结调用一致）。</summary>
    public const int SummaryOutputTokens = 400;

    /// <summary>场景判定命中的关键词（编程 / 办公）。</summary>
    private static readonly string[] WorkKeywords =
    {
        "代码", "函数", "报错", "编译", "接口", "报错信息", "日志", "调试", "重构", "文档", "表格", "会议",
        "方案", "合同", "报销", "预算", "需求", "排期", "code", "bug", "error", "compile", "api", "sql", "json"
    };

    private readonly StorageProvider _store;
    private readonly object _gate = new();
    private List<TokenUsageRecord> _usage = new();
    private List<ModelPrice> _prices = new();
    private bool _loaded;

    public CostEstimatorService(StorageProvider store) => _store = store;

    /// <summary>是否已录入任何单价：没单价就只统计 token，不显示金额。</summary>
    public bool HasPrices
    {
        get { lock (_gate) return _prices.Count > 0; }
    }

    private async Task EnsureLoadedAsync()
    {
        lock (_gate)
        {
            if (_loaded) return;
            _loaded = true;
        }
        try
        {
            var usage = await _store.Load<List<TokenUsageRecord>>(UsageKey);
            var prices = await _store.Load<List<ModelPrice>>(PriceKey);
            lock (_gate)
            {
                if (usage is not null) _usage = usage;
                if (prices is not null) _prices = prices;
            }
        }
        catch (Exception ex)
        {
            App.WriteLog("CostEstimator.Load -> " + ex.Message);
        }
    }

    /// <summary>记一次真实用量（订阅 ApiGateway.UsageReported 即可自动记账）。</summary>
    public void Record(string model, int promptTokens, int completionTokens, bool isSummary = false)
    {
        if (string.IsNullOrWhiteSpace(model)) return;
        _ = RecordAsync(new TokenUsageRecord
        {
            Model = model.Trim(),
            PromptTokens = Math.Max(0, promptTokens),
            CompletionTokens = Math.Max(0, completionTokens),
            IsSummary = isSummary
        });
    }

    private async Task RecordAsync(TokenUsageRecord record)
    {
        await EnsureLoadedAsync();
        lock (_gate)
        {
            _usage.Add(record);
            if (_usage.Count > MaxRecords) _usage.RemoveRange(0, _usage.Count - MaxRecords);
        }
        await _store.Save(UsageKey, _usage.ToList());
    }

    public async Task<List<ModelPrice>> PricesAsync()
    {
        await EnsureLoadedAsync();
        lock (_gate) return _prices.ToList();
    }

    /// <summary>录入/覆盖某模型的单价（每百万 token）。</summary>
    public async Task SetPriceAsync(string model, double inputPerMillion, double outputPerMillion)
    {
        if (string.IsNullOrWhiteSpace(model)) return;
        await EnsureLoadedAsync();
        lock (_gate)
        {
            _prices.RemoveAll(p => string.Equals(p.Model, model.Trim(), StringComparison.OrdinalIgnoreCase));
            if (inputPerMillion > 0 || outputPerMillion > 0)
                _prices.Add(new ModelPrice
                {
                    Model = model.Trim(),
                    InputPerMillion = Math.Max(0, inputPerMillion),
                    OutputPerMillion = Math.Max(0, outputPerMillion)
                });
        }
        await _store.Save(PriceKey, _prices.ToList());
    }

    public async Task RemovePriceAsync(string model)
    {
        await EnsureLoadedAsync();
        lock (_gate) _prices.RemoveAll(p => string.Equals(p.Model, model.Trim(), StringComparison.OrdinalIgnoreCase));
        await _store.Save(PriceKey, _prices.ToList());
    }

    private ModelPrice? PriceOf(string model)
    {
        lock (_gate)
            return _prices.FirstOrDefault(p => string.Equals(p.Model, model, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>按当前单价算一次调用的花费。</summary>
    public double CostOf(string model, int promptTokens, int completionTokens)
    {
        var price = PriceOf(model);
        if (price is null) return 0;
        return promptTokens / 1_000_000.0 * price.InputPerMillion
             + completionTokens / 1_000_000.0 * price.OutputPerMillion;
    }

    /// <summary>用量与花费表（每个模型一行 + 合计）。没录单价时只给 token 数。</summary>
    public async Task<string> ReportTextAsync()
    {
        await EnsureLoadedAsync();
        List<TokenUsageRecord> usage;
        lock (_gate) usage = _usage.ToList();
        if (usage.Count == 0) return "还没有用量记录——端点返回 usage 后会自动记账（部分服务不返回 usage，则无法统计）。";

        var rows = usage.GroupBy(u => u.Model, StringComparer.OrdinalIgnoreCase)
            .Select(g => new
            {
                Model = g.Key,
                Calls = g.Count(),
                SummaryCalls = g.Count(x => x.IsSummary),
                Prompt = g.Sum(x => (long)x.PromptTokens),
                Completion = g.Sum(x => (long)x.CompletionTokens),
                Cost = g.Sum(x => CostOf(x.Model, x.PromptTokens, x.CompletionTokens))
            })
            .OrderByDescending(r => r.Cost)
            .ThenByDescending(r => r.Prompt + r.Completion)
            .ToList();

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("模型              调用   输入tok   输出tok     花费");
        foreach (var r in rows)
        {
            sb.AppendLine($"{Pad(r.Model, 16)} {r.Calls,5} {r.Prompt,9} {r.Completion,9} {r.Cost,9:0.####}"
                + (r.SummaryCalls > 0 ? $"（其中总结 {r.SummaryCalls} 次）" : ""));
        }
        sb.AppendLine("─────────────────────────────────────────────");
        sb.AppendLine($"合计：{rows.Sum(r => r.Calls)} 次调用，输入 {rows.Sum(r => r.Prompt)} tok，"
            + $"输出 {rows.Sum(r => r.Completion)} tok，花费 {rows.Sum(r => r.Cost):0.####}");
        if (!HasPrices) sb.AppendLine("提示：还没录入单价，只统计了 token。在下方按「模型 输入价 输出价」录入即可算钱。");
        return sb.ToString();
    }

    private static string Pad(string s, int width) =>
        s.Length >= width ? s[..width] : s + new string(' ', width - s.Length);

    /// <summary>这类内容是不是编程/办公：是的话盲目总结会丢细节，AI 往往要重读，反倒更贵。</summary>
    public static bool LooksLikeWork(IEnumerable<string>? recentTexts)
    {
        if (recentTexts is null) return false;
        var joined = string.Join("\n", recentTexts.Where(t => !string.IsNullOrWhiteSpace(t)));
        if (joined.Length == 0) return false;
        var hits = WorkKeywords.Count(k => joined.Contains(k, StringComparison.OrdinalIgnoreCase));
        return hits >= 2;
    }

    /// <summary>预测结论：现在总结 / 轻总结 / 不总结，哪个更省，以及省多少。</summary>
    public sealed record Advice(string Choice, string Reason, double SummarizeNowCost, double SummarizeLaterCost, double NoSummaryCost);

    /// <summary>
    /// 比较两条路线在**接下来两轮**上的总花费：
    /// · 路线 A（现在总结）：一次总结调用 + 两轮「缩小后的上下文」；
    /// · 路线 B（不总结）：两轮「持续变大的上下文」，外加到后面被强制总结时更贵的一次总结。
    /// 结果相差不到 5% 就不总结——省那点钱不值得打断连续性。
    /// </summary>
    public async Task<Advice> AdviseAsync(string model, int currentTokens, int keepTokens, int droppedTokens, bool isWork)
    {
        await EnsureLoadedAsync();
        var price = PriceOf(model);
        var inPrice = price?.InputPerMillion ?? 0;
        var outPrice = price?.OutputPerMillion ?? 0;
        double Call(int prompt, int completion) => prompt / 1_000_000.0 * inPrice + completion / 1_000_000.0 * outPrice;

        // 现在总结：喂进去的是被折叠掉的那段，吐出来一段摘要
        var summaryCall = Call(Math.Max(0, droppedTokens), SummaryOutputTokens);
        var afterSummary = Math.Max(200, keepTokens + SummaryOutputTokens);
        var routeA = summaryCall
            + Call(afterSummary, 400)                       // 第 1 轮
            + Call(afterSummary + TurnGrowthTokens, 400);   // 第 2 轮

        // 不总结：上下文继续长；两轮后又顶上上限，那时再总结，喂进去的更多
        var t1 = Math.Max(0, currentTokens);
        var t2 = t1 + TurnGrowthTokens;
        var laterSummary = Call(Math.Max(0, t2 + TurnGrowthTokens - keepTokens), SummaryOutputTokens);
        var routeB = Call(t1, 400) + Call(t2, 400) + laterSummary;

        // 编程/办公：折叠会丢细节，AI 往往要重读一遍（多一次带长输入的调用）
        if (isWork) routeA += Call(Math.Max(0, droppedTokens) / 2, 400);

        var best = Math.Min(Math.Min(routeA, routeB), routeA);
        var cheaper = routeA < routeB ? "summarize" : "none";
        var saving = Math.Abs(routeA - routeB);
        var ratio = Math.Max(routeA, routeB) > 0 ? saving / Math.Max(routeA, routeB) : 0;

        if (price is null)
            return new Advice("unknown",
                $"还没录入「{model}」的单价，只能比 token 不能比钱。按 token 看："
                + $"现在总结后两轮约 {afterSummary + afterSummary + TurnGrowthTokens} tok，"
                + $"不总结约 {t1 + t2 + Math.Max(0, t2 + TurnGrowthTokens)} tok。",
                routeA, routeB, routeB);

        if (ratio < 0.05)
            return new Advice("none",
                $"两条路线花费相差不到 5%（现在总结 {routeA:0.####}，不总结 {routeB:0.####}），"
                + "不值得为这点钱打断上下文的连续性，建议先不总结。",
                routeA, routeB, routeB);

        if (cheaper == "summarize")
        {
            var level = isWork ? "轻总结" : "正常总结";
            return new Advice("summarize",
                $"现在是{Facing(isWork)}对话。现在做{level}：总结调用 {summaryCall:0.####} + 之后两轮 {afterSummary} tok ≈ {routeA:0.####}；"
                + $"不总结则两轮后要喂更多（约 {routeB:0.####}）。省的约 {saving:0.####}。",
                routeA, routeB, routeB);
        }
        return new Advice("none",
            $"现在是{Facing(isWork)}对话。不总结更省：两轮约 {routeB:0.####}，"
            + $"现在总结要 {routeA:0.####}（总结本身 + 缩小的上下文）。",
            routeA, routeB, routeB);
    }

    private static string Facing(bool isWork) => isWork ? "编程/办公类" : "日常聊天";
}
