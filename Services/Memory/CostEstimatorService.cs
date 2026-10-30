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
        => Record(model, promptTokens, completionTokens, 0, 0, isSummary);

    /// <summary>记一次真实用量（带缓存输入/缓存输出）。缓存输入是 prompt 的子集，缓存输出单独一笔。</summary>
    public void Record(string model, int promptTokens, int completionTokens,
        int cachedInputTokens, int cachedOutputTokens, bool isSummary = false,
        string characterId = "", string characterName = "")
    {
        if (string.IsNullOrWhiteSpace(model)) return;
        var prompt = Math.Max(0, promptTokens);
        _ = RecordAsync(new TokenUsageRecord
        {
            Model = model.Trim(),
            PromptTokens = prompt,
            CompletionTokens = Math.Max(0, completionTokens),
            CachedInputTokens = Math.Clamp(cachedInputTokens, 0, prompt),
            CachedOutputTokens = Math.Max(0, cachedOutputTokens),
            IsSummary = isSummary,
            CharacterId = characterId ?? "",
            CharacterName = characterName ?? "",
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

    /// <summary>
    /// 录入/覆盖某模型的单价（每百万 token）。只填输入/输出其中之一也接受。
    /// 缓存价不传（null）时保留盘上已有的——设置页的旧同步只带两档价，不能把缓存价抹掉。
    /// </summary>
    public async Task<(bool ok, string message)> SetPriceAsync(string model, double? inputPerMillion, double? outputPerMillion,
        double? cachedInputPerMillion = null, double? cachedOutputPerMillion = null)
    {
        if (string.IsNullOrWhiteSpace(model)) return (false, "模型名不能为空。");
        var inp = Math.Max(0, inputPerMillion ?? 0);
        var outp = Math.Max(0, outputPerMillion ?? 0);
        if (inp <= 0 && outp <= 0) return (false, "输入价与输出价至少要填一个（另一个可以留空）。");

        await EnsureLoadedAsync();
        double cachedIn, cachedOut;
        lock (_gate)
        {
            var cur = _prices.FirstOrDefault(p => string.Equals(p.Model, model.Trim(), StringComparison.OrdinalIgnoreCase));
            cachedIn = cachedInputPerMillion is { } ci ? Math.Max(0, ci) : Math.Max(0, cur?.CachedInputPerMillion ?? 0);
            cachedOut = cachedOutputPerMillion is { } co ? Math.Max(0, co) : Math.Max(0, cur?.CachedOutputPerMillion ?? 0);
            _prices.RemoveAll(p => string.Equals(p.Model, model.Trim(), StringComparison.OrdinalIgnoreCase));
            _prices.Add(new ModelPrice
            {
                Model = model.Trim(),
                InputPerMillion = inp,
                OutputPerMillion = outp,
                CachedInputPerMillion = cachedIn,
                CachedOutputPerMillion = cachedOut
            });
        }
        await _store.Save(PriceKey, _prices.ToList());
        return (true, $"已记录「{model.Trim()}」的单价（只填一侧也可以，另一侧按 0 计）。");
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

    /// <summary>按当前单价算一次调用的花费（无缓存量时退化为输入/输出两档）。</summary>
    public double CostOf(string model, int promptTokens, int completionTokens)
        => CostOf(model, promptTokens, completionTokens, 0, 0);

    /// <summary>
    /// 按当前单价算一次调用的花费（含缓存档）。口径：
    /// (输入 − 缓存输入) × 输入价 ＋ 缓存输入 × 缓存输入价 ＋ 输出 × 输出价 ＋ 缓存输出 × 缓存输出价。
    /// 输入侧用减法：cached_tokens 是 prompt_tokens 里已按输入价记过账的子集，
    /// 再乘一次缓存价就是同一批 token 收两遍钱——必须先把这部分从输入价里挪出来（改价，不是加量）。
    /// 输出侧用加法：缓存/思考输出是端点单独计费的一笔，不在 completion_tokens 里，不加就漏账。
    /// </summary>
    public double CostOf(string model, int promptTokens, int completionTokens, int cachedInputTokens, int cachedOutputTokens)
    {
        var price = PriceOf(model);
        if (price is null) return 0;
        var hit = Math.Clamp(cachedInputTokens, 0, promptTokens);
        return (promptTokens - hit) / 1_000_000.0 * price.InputPerMillion
             + hit / 1_000_000.0 * price.CachedInputPerMillion
             + completionTokens / 1_000_000.0 * price.OutputPerMillion
             + Math.Max(0, cachedOutputTokens) / 1_000_000.0 * price.CachedOutputPerMillion;
    }

    /// <summary>用量与花费总览（一句话摘要）。</summary>
    public async Task<string> ReportTextAsync()
    {
        await EnsureLoadedAsync();
        List<TokenUsageRecord> usage;
        lock (_gate) usage = _usage.ToList();
        if (usage.Count == 0) return "还没有用量记录——端点返回 usage 后会自动记账（部分服务不返回 usage，则无法统计）。";

        var totals = await ModelTotalsAsync();
        var cost = totals.Sum(t => t.Cost);
        var prompt = totals.Sum(t => t.Prompt);
        var completion = totals.Sum(t => t.Completion);
        return $"共 {totals.Sum(t => t.Calls)} 次调用 · 输入 {prompt} tok · 输出 {completion} tok · "
             + (HasPrices ? $"花费 {cost:0.####}" : "未录单价（只统计 token）");
    }

    /// <summary>价钱表：模型 × 单价（每百万 token）。钱的事单独一张表。</summary>
    public async Task<string> PriceTableTextAsync()
    {
        var prices = await PricesAsync();
        if (prices.Count == 0) return "还没有录入单价。按「模型 输入价/百万 输出价/百万」录入后，这里会出现价钱表。";

        var totals = await ModelTotalsAsync();
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("模型               输入价/百万   输出价/百万     累计花费");
        foreach (var p in prices.OrderByDescending(p => p.InputPerMillion + p.OutputPerMillion))
        {
            var used = totals.FirstOrDefault(t => string.Equals(t.Model, p.Model, StringComparison.OrdinalIgnoreCase));
            sb.AppendLine($"{Pad(p.Model, 16)} {p.InputPerMillion,10:0.####} {p.OutputPerMillion,12:0.####} "
                + $"{(used is null ? "—" : used.Cost.ToString("0.####")),12}");
        }
        sb.AppendLine("─────────────────────────────────────────────");
        sb.Append($"合计花费：{totals.Sum(t => t.Cost):0.####}");
        return sb.ToString();
    }

    /// <summary>token 表：模型 × 输入/输出/合计 token。token 的事单独一张表，不与钱混。</summary>
    public async Task<string> TokenTableTextAsync()
    {
        var totals = await ModelTotalsAsync();
        if (totals.Count == 0) return "还没有用量记录——端点返回 usage 后会自动记账。";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("模型                 调用     输入tok     输出tok      合计tok");
        foreach (var t in totals.OrderByDescending(t => t.Prompt + t.Completion))
        {
            sb.AppendLine($"{Pad(t.Model, 16)} {t.Calls,6} {t.Prompt,11} {t.Completion,11} {t.Prompt + t.Completion,12}");
        }
        sb.AppendLine("──────────────────────────────────────────────────────────");
        sb.Append($"合计：{totals.Sum(t => t.Calls)} 次 · 输入 {totals.Sum(t => t.Prompt)} · "
                + $"输出 {totals.Sum(t => t.Completion)} · 合计 {totals.Sum(t => t.Prompt + t.Completion)}");
        return sb.ToString();
    }

    /// <summary>花钱排行榜（按累计花费从高到低）。没录单价时提示无法排名。</summary>
    public async Task<string> CostRankTextAsync(int top = 5)
    {
        if (!HasPrices) return "还没录单价，排不出花钱榜。";
        var totals = await ModelTotalsAsync();
        var ranked = totals.Where(t => t.Cost > 0).OrderByDescending(t => t.Cost).Take(Math.Max(1, top)).ToList();
        if (ranked.Count == 0) return "还没有产生花费。";

        var total = totals.Sum(t => t.Cost);
        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < ranked.Count; i++)
        {
            var t = ranked[i];
            var share = total > 0 ? t.Cost / total : 0;
            sb.AppendLine($"{i + 1}. {t.Model} —— {t.Cost:0.####}"
                + $"（占 {share:P0}，{Bar(share)}）");
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>token 排行榜（按累计 token 从高到低）。</summary>
    public async Task<string> TokenRankTextAsync(int top = 5)
    {
        var totals = await ModelTotalsAsync();
        var ranked = totals.OrderByDescending(t => t.Prompt + t.Completion).Take(Math.Max(1, top)).ToList();
        if (ranked.Count == 0) return "还没有用量记录。";

        var total = totals.Sum(t => t.Prompt + t.Completion);
        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < ranked.Count; i++)
        {
            var t = ranked[i];
            var sum = t.Prompt + t.Completion;
            var share = total > 0 ? (double)sum / total : 0;
            sb.AppendLine($"{i + 1}. {t.Model} —— {sum} tok"
                + $"（占 {share:P0}，{Bar(share)}）");
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>排行榜里的横条：纯文本画，够直观又不用控件。</summary>
    private static string Bar(double share)
    {
        var n = (int)Math.Round(Math.Clamp(share, 0, 1) * 10);
        return new string('█', Math.Max(1, n)) + new string('·', 10 - Math.Max(1, n));
    }

    private static string Pad(string s, int width) =>
        s.Length >= width ? s[..width] : s + new string(' ', width - s.Length);

    // ============ 图表与导出用的聚合 ============

    /// <summary>按当前单价算一条记录的花费（含缓存两档）。</summary>
    private double CostOfRecord(TokenUsageRecord u) =>
        CostOf(u.Model, u.PromptTokens, u.CompletionTokens, u.CachedInputTokens, u.CachedOutputTokens);

    /// <summary>按模型汇总（条形图用）。</summary>
    public sealed record ModelTotal(string Model, int Calls, long Prompt, long Completion, double Cost);

    public async Task<List<ModelTotal>> ModelTotalsAsync()
    {
        await EnsureLoadedAsync();
        List<TokenUsageRecord> usage;
        lock (_gate) usage = _usage.ToList();
        return usage.GroupBy(u => u.Model, StringComparer.OrdinalIgnoreCase)
            .Select(g => new ModelTotal(g.Key, g.Count(),
                g.Sum(x => (long)x.PromptTokens), g.Sum(x => (long)x.CompletionTokens),
                g.Sum(x => CostOfRecord(x))))
            .OrderByDescending(m => m.Cost)
            .ThenByDescending(m => m.Prompt + m.Completion)
            .ToList();
    }

    /// <summary>按天汇总（折线图用）：最近 <paramref name="days"/> 天，没有用量的日子也补 0，折线才连得上。</summary>
    public sealed record DailyPoint(DateTime Day, int Calls, long Prompt, long Completion, double Cost);

    public async Task<List<DailyPoint>> DailySeriesAsync(int days = 14)
    {
        await EnsureLoadedAsync();
        List<TokenUsageRecord> usage;
        lock (_gate) usage = _usage.ToList();

        var today = DateTime.Now.Date;
        var from = today.AddDays(-(Math.Max(2, days) - 1));
        var points = new List<DailyPoint>();
        for (var d = from; d <= today; d = d.AddDays(1))
        {
            var bucket = usage.Where(u => u.At.ToLocalTime().Date == d).ToList();
            points.Add(new DailyPoint(d, bucket.Count,
                bucket.Sum(x => (long)x.PromptTokens),
                bucket.Sum(x => (long)x.CompletionTokens),
                bucket.Sum(CostOfRecord)));
        }
        return points;
    }

    // ============ 用量统计页用的聚合（含缓存两档） ============

    /// <summary>一段窗口内的用量合计。计价 token = 输入 + 输出 + 缓存输出（缓存输入已含在输入里）。</summary>
    public sealed record UsageSpan(int Calls, long Prompt, long Completion, long CachedInput, long CachedOutput, double Cost)
    {
        public long BilledTokens => Prompt + Completion + CachedOutput;
        public double PricePerMillion => BilledTokens > 0 ? Cost * 1_000_000.0 / BilledTokens : 0;
    }

    public sealed record UsageOverview(UsageSpan Today, UsageSpan Last7, UsageSpan All);

    /// <summary>今日 / 近 7 天（含今天） / 累计 三档总览。</summary>
    public async Task<UsageOverview> OverviewAsync()
    {
        await EnsureLoadedAsync();
        List<TokenUsageRecord> usage;
        lock (_gate) usage = _usage.ToList();

        UsageSpan Of(IReadOnlyList<TokenUsageRecord> rs) => new(
            rs.Count,
            rs.Sum(r => (long)r.PromptTokens),
            rs.Sum(r => (long)r.CompletionTokens),
            rs.Sum(r => (long)r.CachedInputTokens),
            rs.Sum(r => (long)r.CachedOutputTokens),
            rs.Sum(CostOfRecord));

        var today = DateTime.Now.Date;
        var local = usage.Select(u => (Rec: u, Local: u.At.ToLocalTime())).ToList();
        return new UsageOverview(
            Of(local.Where(x => x.Local.Date == today).Select(x => x.Rec).ToList()),
            Of(local.Where(x => x.Local.Date > today.AddDays(-7)).Select(x => x.Rec).ToList()),
            Of(local.Select(x => x.Rec).ToList()));
    }

    /// <summary>按模型分组的明细行（成本构成与分组明细表用）。</summary>
    public sealed record ModelUsageRow(string Model, int Calls, long Prompt, long Completion,
        long CachedInput, long CachedOutput, double Cost);    public async Task<List<ModelUsageRow>> ModelUsageRowsAsync()
    {
        await EnsureLoadedAsync();
        List<TokenUsageRecord> usage;
        lock (_gate) usage = _usage.ToList();
        return usage.GroupBy(u => u.Model, StringComparer.OrdinalIgnoreCase)
            .Select(g => new ModelUsageRow(g.Key, g.Count(),
                g.Sum(x => (long)x.PromptTokens), g.Sum(x => (long)x.CompletionTokens),
                g.Sum(x => (long)x.CachedInputTokens), g.Sum(x => (long)x.CachedOutputTokens),
                g.Sum(CostOfRecord)))
            .OrderByDescending(r => r.Cost).ThenByDescending(r => r.Prompt + r.Completion)
            .ToList();
    }

    /// <summary>按天分组的明细行；时间序列要 0 占位（没有用的日子也是轴上的一天），过滤交给调用方。</summary>
    public sealed record DayUsageRow(DateTime Day, int Calls, long Prompt, long Completion,
        long CachedInput, long CachedOutput, double Cost);

    /// <summary>按角色分组的明细行：这笔 token 与花费算在谁头上（老账本没有角色字段的归「（未标记）」）。</summary>
    public sealed record CharacterUsageRow(string Name, string Id, int Calls, long Prompt, long Completion,
        long CachedInput, long CachedOutput, double Cost);

    public async Task<List<CharacterUsageRow>> CharacterUsageRowsAsync()
    {
        await EnsureLoadedAsync();
        List<TokenUsageRecord> usage;
        lock (_gate) usage = _usage.ToList();
        return usage
            .GroupBy(u =>
            {
                if (!string.IsNullOrWhiteSpace(u.CharacterName)) return u.CharacterName;
                return string.IsNullOrWhiteSpace(u.CharacterId) ? "（未标记）" : u.CharacterId;
            }, StringComparer.Ordinal)
            .Select(g => new CharacterUsageRow(g.Key,
                g.Select(x => x.CharacterId).FirstOrDefault(i => !string.IsNullOrWhiteSpace(i)) ?? "",
                g.Count(),
                g.Sum(x => (long)x.PromptTokens), g.Sum(x => (long)x.CompletionTokens),
                g.Sum(x => (long)x.CachedInputTokens), g.Sum(x => (long)x.CachedOutputTokens),
                g.Sum(CostOfRecord)))
            .OrderByDescending(r => r.Cost).ThenByDescending(r => r.Prompt + r.Completion)
            .ToList();
    }

    public async Task<List<DayUsageRow>> DayUsageRowsAsync(int days)
    {
        await EnsureLoadedAsync();
        List<TokenUsageRecord> usage;
        lock (_gate) usage = _usage.ToList();

        var today = DateTime.Now.Date;
        var from = today.AddDays(-(Math.Max(2, days) - 1));
        var rows = new List<DayUsageRow>();
        for (var d = from; d <= today; d = d.AddDays(1))
        {
            var bucket = usage.Where(u => u.At.ToLocalTime().Date == d).ToList();
            rows.Add(new DayUsageRow(d, bucket.Count,
                bucket.Sum(x => (long)x.PromptTokens),
                bucket.Sum(x => (long)x.CompletionTokens),
                bucket.Sum(x => (long)x.CachedInputTokens),
                bucket.Sum(x => (long)x.CachedOutputTokens),
                bucket.Sum(CostOfRecord)));
        }
        return rows;
    }

    /// <summary>「星期 × 小时」活跃度桶（热力网格用）。Weekday：0=周一。按 token 计强度，168 格全给，空格也占位。</summary>
    public sealed record HeatBucket(int Weekday, int Hour, long Tokens, int Calls);

    public async Task<List<HeatBucket>> HeatmapAsync()
    {
        await EnsureLoadedAsync();
        List<TokenUsageRecord> usage;
        lock (_gate) usage = _usage.ToList();

        var grid = new long[7, 24];
        var calls = new int[7, 24];
        foreach (var u in usage)
        {
            var local = u.At.ToLocalTime();
            // DayOfWeek 是 0=周日；热力表习惯从周一起排
            var wd = ((int)local.DayOfWeek + 6) % 7;
            grid[wd, local.Hour] += Math.Max(0, u.PromptTokens) + Math.Max(0, u.CompletionTokens);
            calls[wd, local.Hour]++;
        }
        var cells = new List<HeatBucket>(7 * 24);
        for (var wd = 0; wd < 7; wd++)
            for (var h = 0; h < 24; h++)
                cells.Add(new HeatBucket(wd, h, grid[wd, h], calls[wd, h]));
        return cells;
    }

    /// <summary>导出 CSV（UTF-8 带 BOM，Excel 直接打开不乱码）。返回文件绝对路径。</summary>
    public async Task<string> ExportCsvAsync()
    {
        await EnsureLoadedAsync();
        List<TokenUsageRecord> usage;
        List<ModelPrice> prices;
        lock (_gate) { usage = _usage.ToList(); prices = _prices.ToList(); }

        var dir = Path.Combine(_store.Root, "exports");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"cost-{DateTime.Now:yyyyMMdd-HHmmss}.csv");

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("时间,角色,模型,输入tok,输出tok,缓存输入tok,缓存输出tok,本次花费,是否总结");
        foreach (var u in usage.OrderBy(u => u.At))
        {
            sb.Append(u.At.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")).Append(',')
              .Append(Escape(u.CharacterName ?? "")).Append(',')
              .Append(Escape(u.Model)).Append(',')
              .Append(u.PromptTokens).Append(',')
              .Append(u.CompletionTokens).Append(',')
              .Append(u.CachedInputTokens).Append(',')
              .Append(u.CachedOutputTokens).Append(',')
              .Append(CostOfRecord(u).ToString("0.######", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
              .Append(u.IsSummary ? "是" : "否").AppendLine();
        }

        sb.AppendLine();
        sb.AppendLine("模型,单价(输入/百万),单价(输出/百万),单价(缓存输入/百万),单价(缓存输出/百万)");
        foreach (var p in prices)
            sb.Append(Escape(p.Model)).Append(',')
              .Append(p.InputPerMillion.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
              .Append(p.OutputPerMillion.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
              .Append(p.CachedInputPerMillion.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
              .Append(p.CachedOutputPerMillion.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)).AppendLine();

        await File.WriteAllTextAsync(path, sb.ToString(), new System.Text.UTF8Encoding(true));
        return path;
    }

    private static string Escape(string s) =>
        s.Contains(',') || s.Contains('"') ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;

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
