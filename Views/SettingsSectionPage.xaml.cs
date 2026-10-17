using WarmAsBefore.DesignSystem.Theme;
using WarmAsBefore.Helpers;
using WarmAsBefore.ViewModels;

namespace WarmAsBefore.Views;

/// <summary>
/// 设置分类子页：只承载一个分类下的 3~4 张卡片。
///
/// 为什么拆两级：设置页原来 24 张卡片在一个长滚动页里，元素创建 + 布局实测要 2.6 秒，
/// 期间 UI 线程既不出帧也不响应输入（就是「进页面卡一下」）。按分类拆开后每页只建 3~4 张卡，
/// 构建量降到十分之一量级，同时「找不到项」的问题也随分类页解决了。
/// </summary>
[QueryProperty(nameof(SectionKey), "key")]
public partial class SettingsSectionPage : ContentPage
{
    private readonly SettingsViewModel _vm;
    private double _lastSide = -1;
    private bool _built;

    public SettingsSectionPage(SettingsViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = vm;
        SizeChanged += (_, _) => ApplyResponsiveWidth();
    }

    /// <summary>路由参数：分类 key（见 SettingsCatalog）。</summary>
    public string SectionKey { get; set; } = "";

    protected override void OnAppearing()
    {
        base.OnAppearing();
        ApplyResponsiveWidth();
        _ = BuildSectionAsync();
    }

    /// <summary>卡片宽度匹配屏幕：窄屏铺满，宽屏最多 1080（一行文字再长就难读了）。</summary>
    private void ApplyResponsiveWidth()
    {
        try
        {
            var w = Width;
            if (w <= 0) return;
            // 内容最多 1200：宽屏时两侧留白把它居中；Center 会让容器按内容收缩（卡片就会变窄），
            // 所以这里用「填满 + 两侧 Margin」的写法。
            var available = Math.Max(0, w - 48);
            var side = Math.Max(24, (available - 1200) / 2);
            if (Math.Abs(side - _lastSide) > 1)
            {
                _lastSide = side;
                Services.PerfLog.Note("设置页宽度", $"Width={w:0} → 内容 {available - 2 * side:0}（两侧各留白 {side:0}）");
            }
            SectionBody.Margin = new Thickness(side, 0, side, 0);
        }
        catch (Exception ex) { App.WriteLog("SettingsSectionPage.Width -> " + ex.Message); }
    }

    private async Task BuildSectionAsync()
    {
        if (_built) return;
        var section = SettingsCatalog.ByKey(Uri.UnescapeDataString(SectionKey));
        if (section is null)
        {
            SectionTitleLabel.Text = "未知分类";
            SectionDescLabel.Text = SectionKey;
            return;
        }
        _built = true;
        SectionTitleLabel.Text = section.Title;
        SectionDescLabel.Text = section.Desc;
        try
        {
            await Task.Delay(24);   // 先让标题出来，再建卡片
            for (var i = 0; i < section.CardKeys.Length; i++)
            {
                var key = section.CardKeys[i];
                if (!Resources.TryGetValue(key, out var t) || t is not DataTemplate template)
                {
                    Services.PerfLog.Note($"设置子页 · 模板缺失", section.Key + " / " + key);
                    continue;
                }
                Microsoft.Maui.Controls.View? content = null;
                using (Services.PerfLog.Measure($"设置子页[{section.Title}] 卡片{i + 1}/{section.CardKeys.Length}"))
                    content = template.CreateContent() as Microsoft.Maui.Controls.View;
                if (content is null) continue;

                // 逐张浮起：让「一张张出来」读成有节奏的进场，而不是随机蹦出来。
                // 时长仍然很短（130ms）——这一页的首要任务是别让人觉得慢。
                // 这里不淡入：这些卡片带着阴影，透明度被多处共写容易把它们留在看不见的那一头。
                CardsHost.Add(content);
                _ = Motion.Rise(content, (uint)Math.Max(1, MotionTokens.Ms(MotionTokens.CardEnterMs)));

                var chart = content.FindByName("CostChartView") as GraphicsView;
                if (chart is not null)
                {
                    chart.Drawable = _vm.CostChart;
                    _vm.InvalidateCostChart = () => chart.Invalidate();
                }
                await Task.Delay(MotionTokens.FrameMs);   // 让出一帧，这一张先画出来
            }
            // 卡片就绪后再补该分类要用的数据（只做这一页需要的）
            _ = SeedAfterCardsAsync(section.Key);
        }
        catch (Exception ex)
        {
            App.WriteLog("SettingsSectionPage.Build -> " + ex);
        }
    }

    /// <summary>按分类补数据：只跑这一页会用到的读取，且并行。</summary>
    private async Task SeedAfterCardsAsync(string sectionKey)
    {
        await Task.Delay(120);
        var work = new List<Task>();
        switch (sectionKey)
        {
            case "integrations":
                work.Add(MeasuredAsync("子页 · 沙箱信任", () => Task.Run(() => _vm.RefreshSandboxTrustAsync())));
                break;
            case "data":
                work.Add(MeasuredAsync("子页 · 能力包", () => _vm.RefreshSkillsAsync()));
                work.Add(MeasuredAsync("子页 · 用量与花费", () => _vm.RefreshCostCommand.ExecuteAsync(null)));
                break;
            case "ai":
            case "memory":
                work.Add(MeasuredAsync("子页 · 用量与花费", () => _vm.RefreshCostCommand.ExecuteAsync(null)));
                break;
            case "interface":
                work.Add(MeasuredAsync("子页 · 表情包", () => _vm.RefreshStickersCommand.ExecuteAsync(null)));
                break;
            case "platform":
                work.Add(MeasuredAsync("子页 · 运行时检测", () => Task.CompletedTask));
                break;
        }
        if (work.Count > 0)
        {
            using (Services.PerfLog.Measure($"子页[{sectionKey}] 数据装载（{work.Count} 段并行）"))
                await Task.WhenAll(work);
        }
    }

    private static async Task MeasuredAsync(string label, Func<Task> body)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try { await body(); }
        catch (Exception ex) { App.WriteLog(label + " -> " + ex.Message); }
        Services.PerfLog.Note(label, $"{sw.ElapsedMilliseconds} ms{(sw.ElapsedMilliseconds >= 200 ? "  <<< SLOW" : "")}");
    }
}
