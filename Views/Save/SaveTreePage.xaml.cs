using Microsoft.Maui.Graphics;
using WarmAsBefore.ViewModels;

namespace WarmAsBefore.Views;

/// <summary>
/// 分支树独立页：XAML 里只放 GraphicsView，Drawable / Invalidate / 命中都在 code-behind 接好，
/// 数据组装与选中状态在 VM。页面不碰 SaveManager，也不实现读档 —— 那部分仍归 SavePage。
/// </summary>
public partial class SaveTreePage : ContentPage
{
    private readonly SaveTreeViewModel _vm;

    public SaveTreePage(SaveTreeViewModel vm)
    {
        _vm = vm;
        try
        {
            InitializeComponent();
        }
        catch (Exception ex)
        {
            // XAML 构建失败时留一条出路，错误本体进日志但不弹窗（照 SuperMonitorPage 的兜底）。
            Services.Core.BugReporter.Report(ex, "SaveTree", "分支树页面构建失败，已显示兜底页", recordOnly: true);
            Content = BuildFallbackPage();
            return;
        }
        BindingContext = vm;
        // 绘制异常只让这一帧空着，不升级成 WinUI 原生异常。
        TreeCanvas.Drawable = Helpers.SafeDrawable.Wrap(vm.Drawable, "SaveTree");
        vm.AttachInvalidate(() =>
        {
            TreeCanvas.Invalidate();
            SyncCanvasHeight();
        });
        TreeCanvas.SizeChanged += (_, _) => vm.SetViewport(TreeCanvas.Width, TreeCanvas.Height);
    }

    protected override async void OnAppearing()
    {
        try
        {
            base.OnAppearing();
            try { await _vm.RefreshAsync(); }
            catch (Exception ex) { App.WriteLog("SaveTree.OnAppearing -> " + ex.Message); }
        }
        catch (Exception ex) { App.WriteLog("SaveTree.OnAppearing -> " + ex); }
    }

    /// <summary>
    /// 画布高度跟随泳道数：节点多到超出视口时靠外层 ScrollView 滚过去。
    /// 只在真的变了才写，避免「设高度 → SizeChanged → 重排 → 再设高度」自激。
    /// </summary>
    private void SyncCanvasHeight()
    {
        try
        {
            var want = Math.Max(TreeCanvas.MinimumHeightRequest, _vm.Drawable.ContentHeight);
            if (double.IsNaN(TreeCanvas.HeightRequest) || Math.Abs(TreeCanvas.HeightRequest - want) > 0.5)
                TreeCanvas.HeightRequest = want;
        }
        catch (Exception ex) { App.WriteLog("SaveTree.SyncCanvasHeight -> " + ex.Message); }
    }

    private void OnCanvasEnd(object? sender, TouchEventArgs e)
    {
        try
        {
            var p = e.Touches.FirstOrDefault();
            _vm.CanvasTap(new PointF((float)p.X, (float)p.Y));
        }
        catch (Exception ex) { App.WriteLog("SaveTree.CanvasEnd -> " + ex.Message); }
    }

    private View BuildFallbackPage()
    {
        var back = new Button { Text = "返回存档", Style = Res("BtnPrimary") };
        back.Clicked += async (_, _) =>
        {
            try { await Shell.Current.GoToAsync(".."); }
            catch (Exception ex) { App.WriteLog("SaveTree.FallbackBack -> " + ex.Message); }
        };
        return new VerticalStackLayout
        {
            Spacing = 10,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                new Label { Text = "分支树没建起来", Style = Res("TxtTitle") },
                new Label
                {
                    Text = "这一页的界面在构建时出错了，已记进 bugs.log；存档与其它页面不受影响。",
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
