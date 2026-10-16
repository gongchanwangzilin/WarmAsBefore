using WarmAsBefore.Modules.Screen;
using WarmAsBefore.ViewModels;

namespace WarmAsBefore.Views;

public partial class SettingsPage : ContentPage
{
    private SettingsViewModel Vm => (SettingsViewModel)BindingContext;

    public SettingsPage(SettingsViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
#if ANDROID
        // 3 秒防呆到点（服务已自动回弹）→ 弹一次确认框，用户选「保留新方向」则重新锁定
        ScreenOrientationService.DebouncedRevert += OnDebouncedRevert;
        Unloaded += (_, _) => ScreenOrientationService.DebouncedRevert -= OnDebouncedRevert;
#endif
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _ = BuildCardsProgressivelyAsync();
        // 延迟重活到首帧渲染之后，避免转场动画期间阻塞 UI 线程
        var vm = BindingContext as SettingsViewModel;
        if (vm is not null)
        {
            // 页面先出来，数据后填：原来在 OnAppearing 里同步把「沙箱信任 / 表情包 / 能力包 / 用量」
            // 全跑一遍，实测其中沙箱一段就要 0.6~1.3 秒，直接算进「点开设置」的等待里。
            // 现在等页面真正可交互后再补数据，并且只补当前可见分类的数据。
            _ = SeedAfterSettledAsync(vm);
            // 首帧渲染后再收起页面级 loading 遮罩（让转场动画完整播放）
            // 延迟 32ms（两帧）再收起遮罩，确保转场动画首帧已渲染、布局已就绪
            _ = System.Threading.Tasks.Task.Delay(32).ContinueWith(_ =>
                MainThread.BeginInvokeOnMainThread(() => vm.PageLoading = false));
        }
    }

    private bool _cardsBuilt;

    /// <summary>
    /// 分组构建卡片：24 张卡一次性实例化实测约 2.6 秒（页面会明显僵一下），
    /// 按 4 组逐批做、每批之间让消息循环喘口气，页面约 0.5 秒即可交互，其余内容陆续补齐。
    /// 每组耗时都记进 perf.log，方便继续优化最重的那一组。
    /// </summary>
    private async Task BuildCardsProgressivelyAsync()
    {
        if (_cardsBuilt) return;
        _cardsBuilt = true;
        try
        {
            await Task.Delay(24);   // 一帧：先把顶栏与搜索框画出来
            for (var i = 1; i <= 4; i++)
            {
                var key = $"SettingsCards{i}";
                if (!Resources.TryGetValue(key, out var t) || t is not DataTemplate template)
                {
                    Services.PerfLog.Note($"设置页 · 卡片第 {i} 组缺失", key + " 没找到");
                    continue;
                }
                Microsoft.Maui.Controls.View? content = null;
                using (Services.PerfLog.Measure($"设置页 · 卡片第 {i} 组"))
                    content = template.CreateContent() as Microsoft.Maui.Controls.View;
                if (content is null) continue;
                CardsHost.Add(content);

                // 成本图表在卡片模板里，名字要在这个实例的名字域里取
                var chart = content.FindByName("CostChartView") as GraphicsView;
                if (chart is not null && BindingContext is SettingsViewModel vm)
                {
                    chart.Drawable = vm.CostChart;
                    vm.InvalidateCostChart = () => chart.Invalidate();
                }
                await Task.Delay(32);   // 让出一次消息循环，卡片能真正画出来再做下一组
            }
        }
        catch (Exception ex)
        {
            App.WriteLog("SettingsPage.BuildCardsProgressively -> " + ex);
        }
    }

    /// <summary>等页面渲染稳定后再装数据，避免把装载时间算进「打开设置」的等待。</summary>
    private async Task SeedAfterSettledAsync(SettingsViewModel vm)
    {
        await Task.Delay(600);
        await SeedSettingsPageAsync(vm);
    }

    /// <summary>设置页的数据装载：串行 + 计时 + 跳过被搜索隐藏的分类（性能日志见 {数据根}/perf.log）。</summary>
    private async Task SeedSettingsPageAsync(SettingsViewModel vm)
    {
        var f = vm.Filter;
        if (f.Sandbox)
        {
            using (Services.PerfLog.Measure("设置页 · 沙箱信任"))
                await Task.Run(() => vm.RefreshSandboxTrustAsync());
        }
        if (f.Interface)
        {
            using (Services.PerfLog.Measure("设置页 · 表情包"))
                await vm.RefreshStickersCommand.ExecuteAsync(null);
        }
        if (f.Skills)
        {
            using (Services.PerfLog.Measure("设置页 · 能力包"))
                await vm.RefreshSkillsAsync();
        }
        if (f.Memory || f.Ai)
        {
            using (Services.PerfLog.Measure("设置页 · 用量与花费"))
                await vm.RefreshCostCommand.ExecuteAsync(null);
        }
    }

#if ANDROID
    private void OnDebouncedRevert(string revertedTo)
    {
        // 事件可能在线程池线程上触发（ScreenOrientationService 的 Task.Delay.ContinueWith），
        // 一律投回主线程再碰 UI；用 try/catch 兜住，避免 async void 内异常进 UnhandledException 崩进程。
        try
        {
            var display = revertedTo switch
            {
                "landscape" => "横屏",
                "portrait" => "竖屏",
                _ => "自动（竖屏）",
            };
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                try
                {
                    var vm = BindingContext as SettingsViewModel;
                    if (vm is null) return;
                    // 页面若已不在导航栈（卸载中/转场中），不弹框，仅同步 Picker 状态
                    vm.ScreenOrientationDisplay = display;
                    var act = await DisplayActionSheet(
                        "3 秒内未确认，已自动变回原方向。", "取消", null, "保留新方向", "仍用原方向");
                    if (act == "保留新方向")
                    {
                        await vm.ConfirmOrientationKeepNewAsync();
                    }
                }
                catch (Exception ex)
                {
                    // 弹窗/确认失败（如 Shell 已空）不应崩进程
                    App.WriteLog("OnDebouncedRevert: " + ex);
                }
            });
        }
        catch (Exception ex)
        {
            App.WriteLog("OnDebouncedRevert (schedule): " + ex);
        }
    }
#endif

    /// <summary>滚动到底部时显示开发者展示隐藏入口。</summary>
    private void OnScrollViewScrolled(object? sender, ScrolledEventArgs e)
    {
        if (BindingContext is not SettingsViewModel vm) return;
        if (sender is not ScrollView sv || sv.Content is not VerticalStackLayout layout) return;
        var remaining = layout.Height - sv.Height - e.ScrollY;
        vm.AtScrollBottom = remaining <= 24;
    }
}
