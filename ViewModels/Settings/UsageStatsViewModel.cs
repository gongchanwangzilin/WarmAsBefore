using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WarmAsBefore.Controls;
using WarmAsBefore.Services;

namespace WarmAsBefore.ViewModels;

/// <summary>
/// 用量统计页的模型：总览 / 成本构成 / Token 活跃度 / 时间序列 / 分组明细 / 模型价格设定。
/// 数据全在本地盘上，进页即真数；图表由页面用 BoxView + Grid 自绘，这里只喂几何与文案。
/// </summary>
public sealed partial class UsageStatsViewModel : ObservableObject
{
    private readonly CostEstimatorService _cost;

    public UsageStatsViewModel(CostEstimatorService cost)
    {
        _cost = cost;
        _ = ReloadAsync();
    }

    // ============ 颜色令牌（进页时取一次；世界换档后重进本页即跟上） ============

    private static Color Token(string key, string fallback) =>
        Application.Current is { } app && app.Resources.TryGetValue(key, out var v) && v is Color c
            ? c : Color.FromArgb(fallback);

    private static readonly Color AccentLow = Token("AccentWarm", "#F5D8B8");
    private static readonly Color AccentHigh = Token("AccentWarmFg", "#6E523A");
    private static readonly Color ChipOn = Token("AccentWarm", "#F5D8B8");

    /// <summary>信号色渐变档：从浅缓存色混到深墨色（MAUI 的 Color 没有 Lerp，通道按 float 插值）。</summary>
    private static Color Ramp(double t)
    {
        var k = (float)Math.Clamp(t, 0, 1);
        return new Color(
            (float)(AccentLow.Red + (AccentHigh.Red - AccentLow.Red) * k),
            (float)(AccentLow.Green + (AccentHigh.Green - AccentLow.Green) * k),
            (float)(AccentLow.Blue + (AccentHigh.Blue - AccentLow.Blue) * k));
    }

    // ============ 区块集合 ============

    public ObservableCollection<CostBarRow> CostBars { get; } = new();
    public ObservableCollection<HeatRow> HeatRows { get; } = new();
    public ObservableCollection<TrendBar> TrendBars { get; } = new();
    public ObservableCollection<DetailRow> DetailRows { get; } = new();
    public ObservableCollection<PriceRow> PriceRows { get; } = new();

    /// <summary>热力网格的小时刻度（24 格，只在 0/6/12/18 标数字）。</summary>
    public List<string> HourMarks { get; } = Enumerable.Range(0, 24).Select(h => h % 6 == 0 ? h.ToString() : "").ToList();

    // ============ 状态与总览 ============

    [ObservableProperty]
    private bool _isBusy = true;

    [ObservableProperty]
    private bool _hasUsage;

    [ObservableProperty]
    private bool _hasPrices;

    [ObservableProperty]
    private string _busyNote = "正在读取本地账本…";

    [ObservableProperty]
    private string _todayHead = "—";
    [ObservableProperty]
    private string _todayMeta = "";
    [ObservableProperty]
    private string _weekHead = "—";
    [ObservableProperty]
    private string _weekMeta = "";
    [ObservableProperty]
    private string _allHead = "—";
    [ObservableProperty]
    private string _allMeta = "";
    [ObservableProperty]
    private string _allSub = "";
    [ObservableProperty]
    private string _tokenBreakdown = "";

    /// <summary>没录单价时三块大数字显示的是 token，得把话说明白，别让人以为是 0 元。</summary>
    public string OverviewUnit => HasPrices ? "估算花费（按你录入的单价）" : "未录单价，只统计 token";

    public string EmptyHint => "还没有用量记录——端点返回 usage 后会自动记账（部分服务不返回 usage，则无法统计）。";

    // ============ 成本构成 ============

    [ObservableProperty]
    private string _costNote = "";

    // ============ Token 活跃度（星期 × 小时 热力网格） ============

    // ============ 时间序列 ============

    [ObservableProperty]
    private int _trendDays = 14;

    [ObservableProperty]
    private string _trendNote = "";

    [ObservableProperty]
    private string _trendStart = "";

    [ObservableProperty]
    private string _trendEnd = "";

    public Color Trend7Bg => TrendDays == 7 ? ChipOn : Colors.Transparent;
    public Color Trend14Bg => TrendDays == 14 ? ChipOn : Colors.Transparent;
    public Color Trend30Bg => TrendDays == 30 ? ChipOn : Colors.Transparent;

    partial void OnTrendDaysChanged(int value)
    {
        OnPropertyChanged(nameof(Trend7Bg));
        OnPropertyChanged(nameof(Trend14Bg));
        OnPropertyChanged(nameof(Trend30Bg));
        _ = RebuildTrendAsync();
    }

    // XAML 传进来的参数是字符串，这里统一按字符串收（项目里既有命令都这么做）
    [RelayCommand]
    private void SetTrendDays(string? days)
    {
        if (int.TryParse(days, out var n)) TrendDays = Math.Clamp(n, 7, 30);
    }

    // ============ 分组明细 ============

    /// <summary>明细分组口径：model / day / character（三选一）。</summary>
    [ObservableProperty]
    private string _detailGroupMode = "model";

    public bool DetailByModel => DetailGroupMode == "model";
    public bool DetailByCharacter => DetailGroupMode == "character";

    public Color DetailModelBg => DetailGroupMode == "model" ? ChipOn : Colors.Transparent;
    public Color DetailDayBg => DetailGroupMode == "day" ? ChipOn : Colors.Transparent;
    public Color DetailCharacterBg => DetailGroupMode == "character" ? ChipOn : Colors.Transparent;

    partial void OnDetailGroupModeChanged(string value)
    {
        OnPropertyChanged(nameof(DetailByModel));
        OnPropertyChanged(nameof(DetailByCharacter));
        OnPropertyChanged(nameof(DetailModelBg));
        OnPropertyChanged(nameof(DetailDayBg));
        OnPropertyChanged(nameof(DetailCharacterBg));
        if (value == "character" && _characterRows.Count == 0) _ = LoadCharacterRowsAsync();
        RebuildDetail();
    }

    [RelayCommand]
    private void SetDetailGroup(string? mode)
        => DetailGroupMode = mode is "day" or "character" ? mode! : "model";

    // ============ 命令 ============

    [RelayCommand]
    private Task Reload() => ReloadAsync();

    [RelayCommand]
    private void ShowHeatCell(HeatCell? cell)
    {
        if (cell is not null) Toast.Info(cell.Hint);
    }

    [RelayCommand]
    private void ShowTrendBar(TrendBar? bar)
    {
        if (bar is not null) Toast.Info(bar.Hint);
    }

    /// <summary>导出 CSV（含缓存两列）。复用成本服务，和设置页导的是同一份账。</summary>
    [RelayCommand]
    private async Task ExportCsv()
    {
        try
        {
            var path = await _cost.ExportCsvAsync();
            Toast.Success($"已导出到：{path}");
        }
        catch (Exception ex)
        {
            App.WriteLog("UsageStatsVM.ExportCsv -> " + ex);
            Toast.Error("导出失败：" + ex.Message);
        }
    }

    // ============ 模型价格设定 ============

    [ObservableProperty]
    private string _priceNote = "";

    [ObservableProperty]
    private string _newPriceModel = "";
    [ObservableProperty]
    private string _newPriceInput = "";
    [ObservableProperty]
    private string _newPriceOutput = "";
    [ObservableProperty]
    private string _newPriceCachedInput = "";
    [ObservableProperty]
    private string _newPriceCachedOutput = "";

    private static double? ParsePrice(string? s) =>
        double.TryParse((s ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v >= 0 ? v : null;

    [RelayCommand]
    private async Task SavePriceRow(PriceRow? row)
    {
        if (row is null) return;
        await ApplyPriceAsync(row.Model, row.InputText, row.OutputText, row.CachedInputText, row.CachedOutputText);
    }

    [RelayCommand]
    private async Task RemovePriceRow(PriceRow? row)
    {
        if (row is null) return;
        try
        {
            await _cost.RemovePriceAsync(row.Model);
            Toast.Info($"已删除「{row.Model}」的单价。");
            await ReloadAsync();
        }
        catch (Exception ex) { App.WriteLog("UsageStatsVM.RemovePrice -> " + ex.Message); }
    }

    [RelayCommand]
    private async Task AddNewPrice()
    {
        var model = (NewPriceModel ?? "").Trim();
        if (model.Length == 0) { PriceNote = "先填模型名（要和请求里用的模型名一致）。"; return; }
        var ok = await ApplyPriceAsync(model, NewPriceInput, NewPriceOutput, NewPriceCachedInput, NewPriceCachedOutput);
        if (ok)
        {
            NewPriceModel = ""; NewPriceInput = ""; NewPriceOutput = "";
            NewPriceCachedInput = ""; NewPriceCachedOutput = "";
        }
    }

    /// <summary>写一侧缓存价即可：没填的一侧由服务层保留盘上旧值。</summary>
    private async Task<bool> ApplyPriceAsync(string model, string? inp, string? outp, string? cin, string? cout)
    {
        try
        {
            var (ok, message) = await _cost.SetPriceAsync(model, ParsePrice(inp), ParsePrice(outp),
                ParsePrice(cin) ?? 0, ParsePrice(cout) ?? 0);
            PriceNote = message;
            if (ok) await ReloadAsync();
            return ok;
        }
        catch (Exception ex)
        {
            App.WriteLog("UsageStatsVM.SetPrice -> " + ex.Message);
            PriceNote = "保存失败：" + ex.Message;
            return false;
        }
    }

    // ============ 装载与重建 ============

    private List<CostEstimatorService.DayUsageRow> _daily = new();
    private List<CostEstimatorService.ModelUsageRow> _modelRows = new();
    private List<CostEstimatorService.CharacterUsageRow> _characterRows = new();

    private async Task LoadCharacterRowsAsync()
    {
        try
        {
            _characterRows = await _cost.CharacterUsageRowsAsync();
            if (DetailGroupMode == "character") RebuildDetail();
        }
        catch (Exception ex) { App.WriteLog("UsageStatsVM.CharacterRows -> " + ex.Message); }
    }

    private async Task ReloadAsync()
    {
        try
        {
            var overview = await _cost.OverviewAsync();
            _modelRows = await _cost.ModelUsageRowsAsync();
            var heat = await _cost.HeatmapAsync();
            _daily = await _cost.DayUsageRowsAsync(TrendDays);
            if (DetailGroupMode == "character") _characterRows = await _cost.CharacterUsageRowsAsync();
            var prices = await _cost.PricesAsync();

            HasPrices = _cost.HasPrices;
            HasUsage = overview.All.Calls > 0;
            OnPropertyChanged(nameof(HasPrices));
            OnPropertyChanged(nameof(OverviewUnit));

            FillOverview(overview);
            FillCostBars(_modelRows);
            FillHeat(heat);
            await RebuildTrendAsync();
            RebuildDetail();
            FillPriceRows(_modelRows, prices);
            BusyNote = "";
        }
        catch (Exception ex)
        {
            App.WriteLog("UsageStatsVM.Reload -> " + ex.Message);
            BusyNote = "读取本地账本失败：" + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void FillOverview(CostEstimatorService.UsageOverview o)
    {
        TodayHead = Headline(o.Today.Cost, o.Today.BilledTokens);
        TodayMeta = Sub(o.Today.Calls, o.Today.BilledTokens);
        WeekHead = Headline(o.Last7.Cost, o.Last7.BilledTokens);
        WeekMeta = Sub(o.Last7.Calls, o.Last7.BilledTokens);
        AllHead = Headline(o.All.Cost, o.All.BilledTokens);
        AllMeta = Sub(o.All.Calls, o.All.BilledTokens);
        AllSub = HasPrices && o.All.BilledTokens > 0
            ? $"每百万 token 均价约 {o.All.PricePerMillion:0.####}"
            : "录入单价后这里显示均价";
        // 缓存输入是输入的子集（「其中」），缓存输出是另起一笔（「外加」）——文案跟着算钱口径走
        TokenBreakdown = $"累计输入 {Fmt(o.All.Prompt)}（其中缓存命中 {Fmt(o.All.CachedInput)}） · "
                       + $"累计输出 {Fmt(o.All.Completion)}（外加思考/缓存 {Fmt(o.All.CachedOutput)}）";
    }

    private string Headline(double cost, long tokens) => HasPrices ? cost.ToString("0.####") : Fmt(tokens);

    private static string Sub(int calls, long tokens) => $"{calls} 次调用 · {Fmt(tokens)} tok";

    private void FillCostBars(List<CostEstimatorService.ModelUsageRow> rows)
    {
        CostBars.Clear();
        CostNote = HasPrices
            ? "按已录单价估算：缓存输入按缓存价重算，思考/缓存输出另计一笔。"
            : "未录单价，只统计 token——在下方「模型价格设定」录入后这里自动变成成本构成。";
        if (rows.Count == 0) return;

        var max = rows.Max(r => Metric(r));
        var sum = rows.Sum(r => Metric(r));
        foreach (var r in rows.Where(r => Metric(r) > 0))
        {
            var v = Metric(r);
            var share = sum > 0 ? (double)v / sum : 0;
            CostBars.Add(new CostBarRow
            {
                Model = r.Model,
                ValueLabel = HasPrices ? v.ToString("0.####") : Fmt((long)v) + " tok",
                ShareLabel = share.ToString("P1"),
                // 20 档列宽：ColumnSpan 取整到 5% 一格，至少占 1 格免得极小份额整条消失
                FillCols = Math.Clamp((int)Math.Round(share * 20), 1, 20),
                Fill = Ramp(max > 0 ? 0.25 + 0.7 * v / max : 0.5)
            });
        }
    }

    private double Metric(CostEstimatorService.ModelUsageRow r) =>
        HasPrices ? r.Cost : r.Prompt + r.Completion + r.CachedOutput;

    private void FillHeat(List<CostEstimatorService.HeatBucket> cells)
    {
        HeatRows.Clear();
        var max = cells.Count > 0 ? cells.Max(c => c.Tokens) : 0;
        var dayNames = new[] { "周一", "周二", "周三", "周四", "周五", "周六", "周日" };
        for (var wd = 0; wd < 7; wd++)
        {
            var row = new HeatRow { DayLabel = dayNames[wd] };
            for (var h = 0; h < 24; h++)
            {
                var c = cells[wd * 24 + h];
                var t = max > 0 ? (double)c.Tokens / max : 0;
                row.Cells.Add(new HeatCell
                {
                    // 空格也占位：给一档极浅的底色，网格才数得出 24×7
                    Fill = c.Tokens > 0 ? Ramp(0.18 + 0.82 * t) : Ramp(0).WithAlpha(0.1f),
                    Hint = $"{dayNames[wd]} {h}:00–{(h + 1) % 24}:00 · {c.Calls} 次 · {Fmt(c.Tokens)} tok"
                });
            }
            HeatRows.Add(row);
        }
    }

    private async Task RebuildTrendAsync()
    {
        var daily = await _cost.DayUsageRowsAsync(TrendDays);
        _daily = daily;

        TrendBars.Clear();
        var n = daily.Count;
        var (barW, gap) = n >= 24 ? (8.0, 3.0) : n >= 12 ? (16.0, 4.0) : (34.0, 6.0);
        var max = daily.Max(d => Billed(d));
        for (var i = 0; i < n; i++)
        {
            var d = daily[i];
            var billed = Billed(d);
            var totalH = max > 0 ? 112.0 * billed / max : 0;
            var cacheH = max > 0 ? 112.0 * Math.Min(d.CachedInput, billed) / max : 0;
            TrendBars.Add(new TrendBar
            {
                WidthPx = barW + gap,
                BarPx = barW,
                // 同槽里两段堆叠：上段浅色=非缓存部分，下段深色=缓存命中——柱子越深，这天越多内容走了缓存价
                RestHeightPx = Math.Max(0, totalH - cacheH),
                CacheHeightPx = cacheH,
                Fill = Ramp(0.22),
                CacheFill = Ramp(0.75),
                Hint = $"{d.Day:MM-dd} · {d.Calls} 次 · 输入 {Fmt(d.Prompt)}（缓存 {Fmt(d.CachedInput)}） · "
                     + $"输出 {Fmt(d.Completion)}（思考/缓存 {Fmt(d.CachedOutput)}）"
                     + (HasPrices ? $" · {d.Cost:0.####}" : "")
            });
        }
        TrendStart = n > 0 ? daily[0].Day.ToString("MM-dd") : "";
        TrendEnd = n > 0 ? daily[^1].Day.ToString("MM-dd") : "";

        TrendNote = $"最近 {TrendDays} 天：{daily.Sum(d => d.Calls)} 次 · {Fmt(daily.Sum(Billed))} tok · "
                  + $"缓存命中 {Fmt(daily.Sum(d => d.CachedInput))} tok";
        RebuildDetail();
    }

    private static long Billed(CostEstimatorService.DayUsageRow d) => d.Prompt + d.Completion + d.CachedOutput;

    private void RebuildDetail()
    {
        DetailRows.Clear();
        if (DetailByCharacter)
        {
            foreach (var r in _characterRows)
                DetailRows.Add(new DetailRow
                {
                    Name = r.Name,
                    Calls = r.Calls.ToString(),
                    Input = Fmt(r.Prompt),
                    Output = Fmt(r.Completion),
                    CachedInput = Fmt(r.CachedInput),
                    CachedOutput = Fmt(r.CachedOutput),
                    Cost = HasPrices ? r.Cost.ToString("0.####") : "—"
                });
            return;
        }
        if (DetailByModel)
        {
            foreach (var r in _modelRows)
                DetailRows.Add(new DetailRow
                {
                    Name = r.Model,
                    Calls = r.Calls.ToString(),
                    Input = Fmt(r.Prompt),
                    Output = Fmt(r.Completion),
                    CachedInput = Fmt(r.CachedInput),
                    CachedOutput = Fmt(r.CachedOutput),
                    Cost = HasPrices ? r.Cost.ToString("0.####") : "—"
                });
        }
        else
        {
            foreach (var d in _daily.Where(d => d.Calls > 0).OrderByDescending(d => d.Day))
                DetailRows.Add(new DetailRow
                {
                    Name = d.Day.ToString("yyyy-MM-dd"),
                    Calls = d.Calls.ToString(),
                    Input = Fmt(d.Prompt),
                    Output = Fmt(d.Completion),
                    CachedInput = Fmt(d.CachedInput),
                    CachedOutput = Fmt(d.CachedOutput),
                    Cost = HasPrices ? d.Cost.ToString("0.####") : "—"
                });
        }
    }

    private void FillPriceRows(List<CostEstimatorService.ModelUsageRow> used, List<Models.ModelPrice> prices)
    {
        PriceRows.Clear();
        var names = used.Select(u => u.Model)
            .Concat(prices.Select(p => p.Model))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase);
        foreach (var name in names)
        {
            var p = prices.FirstOrDefault(x => string.Equals(x.Model, name, StringComparison.OrdinalIgnoreCase));
            PriceRows.Add(new PriceRow
            {
                Model = name,
                Seen = used.Any(u => string.Equals(u.Model, name, StringComparison.OrdinalIgnoreCase)),
                InputText = p is null or { InputPerMillion: 0 } ? "" : p.InputPerMillion.ToString("0.####"),
                OutputText = p is null or { OutputPerMillion: 0 } ? "" : p.OutputPerMillion.ToString("0.####"),
                CachedInputText = p is null or { CachedInputPerMillion: 0 } ? "" : p.CachedInputPerMillion.ToString("0.####"),
                CachedOutputText = p is null or { CachedOutputPerMillion: 0 } ? "" : p.CachedOutputPerMillion.ToString("0.####"),
            });
        }
    }

    private static string Fmt(long v) => v switch
    {
        >= 1_000_000 => (v / 1_000_000.0).ToString("0.#") + "M",
        >= 1_000 => (v / 1_000.0).ToString("0.#") + "k",
        _ => v.ToString()
    };

    // ============ 图表行模型（XAML 模板的绑定对象） ============

    public sealed class CostBarRow
    {
        public string Model { get; init; } = "";
        public string ValueLabel { get; init; } = "";
        public string ShareLabel { get; init; } = "";
        /// <summary>20 星列网格里的占列数（把百分比量化到列，纯 XAML 就能画比例条）。</summary>
        public int FillCols { get; init; }
        public Color Fill { get; init; } = Colors.Transparent;
    }

    public sealed class HeatCell
    {
        public Color Fill { get; init; } = Colors.Transparent;
        public string Hint { get; init; } = "";
    }

    public sealed class HeatRow
    {
        public string DayLabel { get; init; } = "";
        public List<HeatCell> Cells { get; } = new();
    }

    public sealed class TrendBar
    {
        /// <summary>槽宽（含间隙）。</summary>
        public double WidthPx { get; init; }
        /// <summary>柱身宽度 = 槽宽减去间隙。</summary>
        public double BarPx { get; init; }
        public double RestHeightPx { get; init; }
        public double CacheHeightPx { get; init; }
        public Color Fill { get; init; } = Colors.Transparent;
        public Color CacheFill { get; init; } = Colors.Transparent;
        public string Hint { get; init; } = "";
    }

    public sealed class DetailRow
    {
        public string Name { get; init; } = "";
        public string Calls { get; init; } = "";
        public string Input { get; init; } = "";
        public string Output { get; init; } = "";
        public string CachedInput { get; init; } = "";
        public string CachedOutput { get; init; } = "";
        public string Cost { get; init; } = "";
    }

    public sealed class PriceRow
    {
        public string Model { get; init; } = "";
        /// <summary>是否在用量记录里出现过（出现过=「用过」，没出现过=只存了价）。</summary>
        public bool Seen { get; init; }
        public string InputText { get; set; } = "";
        public string OutputText { get; set; } = "";
        public string CachedInputText { get; set; } = "";
        public string CachedOutputText { get; set; } = "";
    }
}
