using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WarmAsBefore.Models;
using WarmAsBefore.Controls;
using System.Globalization;

namespace WarmAsBefore.ViewModels;

public sealed partial class SettingsViewModel
{
    // ============ 超时与成本（智能省钱） ============

    /// <summary>全局请求超时（秒）。</summary>
    [ObservableProperty] private int _aiTimeoutSeconds = 25;
    public List<int> TimeoutChoices { get; } = new() { 10, 15, 25, 40, 60, 120, 180 };
    partial void OnAiTimeoutSecondsChanged(int value) => PersistSettings();

    [ObservableProperty] private string _costSummary = "点「刷新用量」看看花了多少。";

    // 钱与 token 分开看：两张表 + 两个排行榜
    [ObservableProperty] private string _priceTable = "";
    [ObservableProperty] private string _tokenTable = "";
    [ObservableProperty] private string _costRanking = "";
    [ObservableProperty] private string _tokenRanking = "";

    /// <summary>单价表快照（落盘用）：由成本服务维护，这里只取当前值。</summary>
    private List<ModelPrice> ModelPricesSnapshot
    {
        get
        {
            try { return _cost.PricesAsync().GetAwaiter().GetResult(); }
            catch { return new List<ModelPrice>(); }
        }
    }
    [ObservableProperty] private string _newPriceModel = "";
    [ObservableProperty] private string _newPriceInput = "";
    [ObservableProperty] private string _newPriceOutput = "";

    [RelayCommand]
    private async Task RefreshCost()
    {
        try
        {
            CostSummary = await _cost.ReportTextAsync();
            PriceTable = await _cost.PriceTableTextAsync();
            TokenTable = await _cost.TokenTableTextAsync();
            CostRanking = await _cost.CostRankTextAsync();
            TokenRanking = await _cost.TokenRankTextAsync();
            await RebuildChartAsync();
        }
        catch (Exception ex)
        {
            App.WriteLog("SettingsVM.RefreshCost -> " + ex.Message);
            CostSummary = "读取用量失败。";
        }
    }

    // ===== 成本图表（条形图：各模型对比；折线图：按天趋势） =====

    /// <summary>自绘图表：页面的 GraphicsView 把它挂上（见 SettingsPage.xaml.cs）。</summary>
    public Drawables.CostChartDrawable CostChart { get; } = new();

    /// <summary>页面注入的重绘回调。</summary>
    public Action? InvalidateCostChart { get; set; }

    [ObservableProperty] private bool _costChartIsLine = true;
    [ObservableProperty] private string _costChartCaption = "";

    /// <summary>按钮文案 = 点了会切到什么。</summary>
    public string CostChartModeLabel => CostChartIsLine ? "切换为条形图（各模型对比）" : "切换为折线图（按天趋势）";

    private bool _chartBusy;

    [RelayCommand]
    private void ToggleChartMode() => CostChartIsLine = !CostChartIsLine;

    partial void OnCostChartIsLineChanged(bool value)
    {
        CostChart.Mode = value ? Drawables.CostChartDrawable.ChartMode.Lines : Drawables.CostChartDrawable.ChartMode.Bars;
        OnPropertyChanged(nameof(CostChartModeLabel));
        if (!_chartBusy) _ = RebuildChartAsync();
    }

    /// <summary>按当前模式重建图表数据。</summary>
    private async Task RebuildChartAsync()
    {
        if (_chartBusy) return;
        _chartBusy = true;
        try
        {
            var prices = await _cost.PricesAsync();
            var hasPrice = prices.Count > 0;
            var totals = await _cost.ModelTotalsAsync();

            // 条形图：各模型的「花费」对比（没录单价就退化成 token 对比）
            var bars = totals.Select(t => new Drawables.CostChartDrawable.Bar(
                    t.Model,
                    hasPrice ? t.Cost : t.Prompt + t.Completion,
                    hasPrice ? $"{t.Cost:0.###}" : Fmt(t.Prompt + t.Completion)))
                .ToList();

            // 折线图：最近 14 天的输入/输出 token
            var daily = await _cost.DailySeriesAsync(14);
            var line = new List<Drawables.CostChartDrawable.Series>
            {
                new("输入", Color.FromArgb("#7BA7D7"), daily.Select(d => (double)d.Prompt).ToList()),
                new("输出", Color.FromArgb("#D79B7B"), daily.Select(d => (double)d.Completion).ToList())
            };
            var labels = daily.Select(d => d.Day.ToString("MM-dd")).ToList();

            CostChart.Bars = bars;
            CostChart.SeriesList = line;
            CostChart.XLabels = labels;
            CostChart.Unit = hasPrice ? "元" : "tok";
            CostChart.EmptyHint = "还没有用量记录（端点返回 usage 后自动记账）";
            CostChart.Mode = CostChartIsLine
                ? Drawables.CostChartDrawable.ChartMode.Lines
                : Drawables.CostChartDrawable.ChartMode.Bars;

            var total = daily.Sum(d => d.Cost);
            var calls = daily.Sum(d => d.Calls);
            CostChartCaption = hasPrice
                ? $"最近 14 天：{calls} 次调用，共 {Fmt(daily.Sum(d => d.Prompt + d.Completion))} tok，花费 {total:0.###}"
                : $"最近 14 天：{calls} 次调用，共 {Fmt(daily.Sum(d => d.Prompt + d.Completion))} tok（录入单价后显示金额）";

            InvalidateCostChart?.Invoke();
        }
        catch (Exception ex)
        {
            App.WriteLog("SettingsVM.RebuildChart -> " + ex.Message);
        }
        finally { _chartBusy = false; }
    }

    private static string Fmt(long v) => v switch
    {
        >= 1_000_000 => (v / 1_000_000.0).ToString("0.#") + "M",
        >= 1_000 => (v / 1_000.0).ToString("0.#") + "k",
        _ => v.ToString()
    };

    /// <summary>把用量明细与单价表导出成 CSV（Excel 可直接打开）。</summary>
    [RelayCommand]
    private async Task ExportCostCsv()
    {
        try
        {
            var path = await _cost.ExportCsvAsync();
            Toast.Success($"已导出到：{path}");
        }
        catch (Exception ex)
        {
            App.WriteLog("SettingsVM.ExportCostCsv -> " + ex);
            Toast.Error("导出失败：" + ex.Message);
        }
    }

    /// <summary>录入单价：按「每百万 token」填输入价与输出价。</summary>
    [RelayCommand]
    private async Task AddModelPrice()
    {
        var model = (NewPriceModel ?? "").Trim();
        if (model.Length == 0) { CostSummary = "先填模型名（要和请求里用的模型名一致）。"; return; }
        double? Parse(string s)
            => double.TryParse((s ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v >= 0 ? v : null;

        var input = Parse(NewPriceInput);
        var output = Parse(NewPriceOutput);
        // 只填一侧也接受：很多服务只关心输入价，强制两边都填是多余的负担
        var (ok, message) = await _cost.SetPriceAsync(model, input, output);
        if (!ok) { CostSummary = message; return; }

        _settings.Apply(_settings.Current with
        {
            ModelPrices = (await _cost.PricesAsync()).ToList()
        });
        await _settings.Persist();
        NewPriceModel = "";
        NewPriceInput = "";
        NewPriceOutput = "";
        await RefreshCost();
    }

    /// <summary>按当前上下文上限试算一次「现在总结 vs 不总结」（真实决策发生在对话里，这里是预估）。</summary>
    [RelayCommand]
    private async Task TryAdvice()
    {
        try
        {
            var limit = ContextTokenLimit > 0 ? ContextTokenLimit : 32000;
            var keep = Math.Max(500, limit / 3);
            var dropped = Math.Max(500, limit - keep);
            var advice = await _cost.AdviseAsync(AiModel, limit, keep, dropped, isWork: false);
            CostSummary = advice.Reason;
        }
        catch (Exception ex)
        {
            App.WriteLog("SettingsVM.TryAdvice -> " + ex.Message);
            CostSummary = "试算失败：" + ex.Message;
        }
    }

}
