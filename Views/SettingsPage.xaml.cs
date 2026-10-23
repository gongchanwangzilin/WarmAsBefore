using WarmAsBefore.Helpers;
using WarmAsBefore.ViewModels;

namespace WarmAsBefore.Views;

/// <summary>
/// 设置首页：只列分类条目（名称 + 当前值摘要），点进 <see cref="SettingsSectionPage"/> 看具体项。
///
/// 为什么改两级：原来 24 张卡片挤在一个长滚动页里，实测元素创建 + 布局要 2.6 秒，
/// 期间 UI 线程不出帧也不响应输入（就是「进页面卡一下」）；而且项多了根本找不到。
/// </summary>
public partial class SettingsPage : ContentPage
{
    private readonly SettingsViewModel _vm;
    private double _lastSide = -1;

    public SettingsPage(SettingsViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = vm;
        SizeChanged += (_, _) => ApplyResponsiveWidth();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        ApplyResponsiveWidth();
        // 首页只建 11 行分类，构建量极小；各分类的数据装载在子页里按需做
        _vm.PrepareHome();
        Motion.ArrivePage(this);
    }

    /// <summary>宽度匹配屏幕：窄屏铺满，宽屏最多 1080（一行文字再长就难读）。</summary>
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
            SettingsBody.Margin = new Thickness(side, 0, side, 0);
        }
        catch (Exception ex) { App.WriteLog("SettingsPage.Width -> " + ex.Message); }
    }
}
