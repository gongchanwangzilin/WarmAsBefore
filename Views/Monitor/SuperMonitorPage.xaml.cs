using Microsoft.Maui.Graphics;
using WarmAsBefore.ViewModels;

namespace WarmAsBefore.Views;

/// <summary>
/// 超级监测整页：进来才建数据、离开就退订停钟（见 VM.StartClock/StopClock）。
/// 画布照 MemoryGraphPage 的套路：XAML 里只放 GraphicsView，Drawable 在构造函数里从 VM 取。
/// 活流程图（ArchCanvas）+ 余辉计时器 + FlowTracer 订阅都在 VM 内，页面只管绑定 / 命中 / 生命周期。
/// 3D 向量层是纯装饰：初始化或显隐失败都保持隐藏，2D 流程图照常工作。
/// </summary>
public partial class SuperMonitorPage : ContentPage
{
    /// <summary>右栏与画布并排所需的最小宽度；比这更窄就折成一列（画布在上、读数栏在下）。</summary>
    private const double SideBySideBreakpoint = 980;

    private const double RailWidth = 360;
    private const double StackedCanvasHeight = 320;

    private readonly SuperMonitorViewModel _vm;
    private bool? _sideBySide;


    public SuperMonitorPage(SuperMonitorViewModel vm)
    {
        _vm = vm;
        try
        {
            InitializeComponent();
        }
        catch (Exception ex)
        {
            // XAML 构建失败时不能留一张空白页给用户（bugs.log 2026-11-20 那条无堆栈的
            // WinUI COMException 就是在这个页面上冒出来的：界面没了，只剩一个报错弹窗）。
            // 兜底页说明发生了什么、给一条出路，错误本体进日志但不弹窗。
            Services.Core.BugReporter.Report(ex, "Monitor", "监测页构建失败，已显示兜底页", recordOnly: true);
            Content = BuildFallbackPage();
            return;
        }
        BindingContext = vm;
        // 每个画布都过一层兜底：绘制异常只让这一帧空着，不升级成 WinUI 原生异常。
        ArchCanvas.Drawable = Helpers.SafeDrawable.Wrap(vm.ArchDrawable, "MonitorArch");
        vm.AttachInvalidate(() => ArchCanvas.Invalidate());
        ArchCanvas.SizeChanged += (_, _) => vm.SetViewport(ArchCanvas.Width, ArchCanvas.Height);

        // 向量空间也是纯 2D 画布：自己算投影、自己画点，没有原生 3D 层可以崩。
        VectorCanvas.Drawable = Helpers.SafeDrawable.Wrap(vm.VectorDrawable, "MonitorVectors");
        vm.AttachVectorInvalidate(() => VectorCanvas.Invalidate());

        SizeChanged += (_, _) => ApplyResponsiveLayout(Width);
    }


    protected override void OnNavigatedTo(NavigatedToEventArgs args)
    {
        base.OnNavigatedTo(args);
        ApplyResponsiveLayout(Width);
        _vm.StartClock();
    }

    protected override void OnNavigatedFrom(NavigatedFromEventArgs args)
    {
        _vm.StopClock();
        base.OnNavigatedFrom(args);
    }

    /// <summary>
    /// 宽窗左右并排（画布吃剩余宽度 + 360 读数栏），窄窗折成一列（画布定高 320、读数栏占余下）。
    /// 只在跨档时动一次行列定义：SizeChanged 每像素都来，逐次重排会把整页推进布局风暴。
    /// </summary>
    private void ApplyResponsiveLayout(double width)
    {
        if (!double.IsFinite(width) || width <= 0) return;
        var sideBySide = width >= SideBySideBreakpoint;
        if (_sideBySide == sideBySide) return;
        _sideBySide = sideBySide;

        BodyGrid.ColumnDefinitions.Clear();
        BodyGrid.RowDefinitions.Clear();

        if (sideBySide)
        {
            BodyGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            BodyGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(RailWidth)));
            BodyGrid.RowDefinitions.Add(new RowDefinition(GridLength.Star));
            BodyGrid.ColumnSpacing = 12;
            BodyGrid.RowSpacing = 0;

            Grid.SetRow(CanvasHost, 0); Grid.SetColumn(CanvasHost, 0);
            Grid.SetRow(RailHost, 0); Grid.SetColumn(RailHost, 1);
            CanvasHost.HeightRequest = -1;
            CanvasHost.WidthRequest = -1;
            CanvasHost.RowDefinitions[1].Height = GridLength.Star;   // 宽窗：流程图吃剩余高度
        }
        else
        {
            BodyGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            // 窄窗不能再把整块 CanvasHost 钉死在 320：它里面有流程图 + 向量卡（300）+ 注释，
            // 钉死会把流程图压成 0、把向量卡挤出可视区。改成「整块按内容量高，流程图自己定 320」。
            BodyGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            BodyGrid.RowDefinitions.Add(new RowDefinition(GridLength.Star));
            BodyGrid.ColumnSpacing = 0;
            BodyGrid.RowSpacing = 12;

            Grid.SetRow(CanvasHost, 0); Grid.SetColumn(CanvasHost, 0);
            Grid.SetRow(RailHost, 1); Grid.SetColumn(RailHost, 0);
            CanvasHost.RowDefinitions[1].Height = new GridLength(StackedCanvasHeight);
        }
    }

    private void OnCanvasEnd(object? sender, TouchEventArgs e)
    {
        var p = e.Touches.FirstOrDefault();
        _vm.CanvasTap(new PointF((float)p.X, (float)p.Y));
    }

    /// <summary>构建失败时的兜底页：一句话说明 + 一条返回的路（R7 空态：井面片 + 标题 + 说明 + 行动）。</summary>
    private View BuildFallbackPage()
    {
        var back = new Button { Text = "返回", Style = Res("BtnPrimary") };
        back.Clicked += async (_, _) =>
        {
            try { await Shell.Current.GoToAsync(".."); }
            catch (Exception ex) { App.WriteLog("SuperMonitor.FallbackBack -> " + ex.Message); }
        };

        var stack = new VerticalStackLayout
        {
            Spacing = 10,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                new Label { Text = "监测页没建起来", Style = Res("TxtTitle") },
                new Label
                {
                    Text = "这一页的界面在构建时出错了，已记进 bugs.log；对话、存档与其它页面不受影响。",
                    Style = Res("TxtMuted"),
                    HorizontalTextAlignment = TextAlignment.Center,
                    MaximumWidthRequest = 360,
                },
                back,
            },
        };
        return stack;
    }

    private static Style? Res(string key) =>
        Application.Current?.Resources.TryGetValue(key, out var v) == true ? v as Style : null;
}
