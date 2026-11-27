using WarmAsBefore.ViewModels;

namespace WarmAsBefore.Views;

/// <summary>
/// 模块详情页：从超级监测的图上点一枚节点进来，看它自己的那一页。
/// 页面只管绑定与生命周期：取数、分组合成、状态灯都在 VM 里（ModuleInspectorViewModel）。
/// 构造失败时不留一张空白页给用户——兜底页说明发生了什么并给一条返回的路。
/// </summary>
public partial class ModuleInspectorPage : ContentPage
{
    private readonly ModuleInspectorViewModel _vm;

    public ModuleInspectorPage(ModuleInspectorViewModel vm)
    {
        _vm = vm;
        try
        {
            InitializeComponent();
        }
        catch (Exception ex)
        {
            Services.Core.BugReporter.Report(ex, "ModuleInspector", "模块详情页构建失败，已显示兜底页", recordOnly: true);
            Content = BuildFallbackPage();
            return;
        }
        BindingContext = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        // OnAppearing 只能是 async void，异常没有调用方能接住，统一交给 UiGuard：
        // 记日志、给一句能懂的话，页面继续可用（query 参数可能比上屏晚到，这里再取一次数）。
        // 人脸面板的拉帧表放在取数之后起：只有「摄像头人脸」这一页才需要它。
        Helpers.UiGuard.Run(async () =>
        {
            await _vm.LoadAsync();
            _vm.BeginFaceMonitor();
        }, "ModuleInspector.Load", "这个模块的读数没读上来");
    }

    /// <summary>离开页面：人脸图像流跟着停（停表 + 交还拉帧资格），不在后台留着空转。</summary>
    protected override void OnDisappearing()
    {
        _vm.EndFaceMonitor();
        base.OnDisappearing();
    }

    /// <summary>构建失败时的兜底页：一句话说明 + 一条返回的路（R7 空态：标题 + 说明 + 行动）。</summary>
    private View BuildFallbackPage()
    {
        var back = new Button { Text = "返回", Style = Res("BtnPrimary") };
        back.Clicked += async (_, _) =>
        {
            try { await Shell.Current.GoToAsync(".."); }
            catch (Exception ex) { App.WriteLog("ModuleInspector.FallbackBack -> " + ex.Message); }
        };

        return new VerticalStackLayout
        {
            Spacing = 10,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                new Label { Text = "模块详情没建起来", Style = Res("TxtTitle") },
                new Label
                {
                    Text = "这一页的界面在构建时出错了，已记进 bugs.log；监测页的其它部分不受影响。",
                    Style = Res("TxtMuted"),
                    HorizontalTextAlignment = TextAlignment.Center,
                    MaximumWidthRequest = 360,
                },
                back,
            },
        };
    }

    private static Style? Res(string key) =>
        Application.Current?.Resources.TryGetValue(key, out var v) == true ? v as Style : null;
}
