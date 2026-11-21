using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Dispatching;
using Microsoft.Maui.Graphics;
using WarmAsBefore.Drawables;
using WarmAsBefore.Helpers;
using WarmAsBefore.Models.Background;

namespace WarmAsBefore.Controls;

/// <summary>
/// 前景天气视图：一层全屏透明、输入完全透传的 GraphicsView，覆盖在应用 UI 之上。
/// 雨滴落到具体控件表面时凝结成水珠、停留滑动、沿边缘滑落，滑出后恢复雨滴流走。
///
/// 硬红线（项目刚从「逐帧换源 → GC 风暴 → LayoutCycle 卡死」里爬出来）：
/// 1. 唯一动画通道是 GraphicsView.Invalidate() 重绘；相位泵只改 drawable 数据 + Invalidate，
///    绝不碰任何 Layout 属性、Image.Source、Shadow、Scale / TranslationY、Opacity 视图动画。
/// 2. 本控件整体 InputTransparent + CascadeInputTransparent，绝不拦截宿主页面的点击。
/// 3. 几何采集是一次性快照：仅在 Loaded / 外部显式调用时做一次，不在 Tick 里遍历可视树。
///    所有布局读取都在 UI 线程，且用 try/catch 兜底——装饰层绝不能崩掉主流程。
/// </summary>
public sealed class ForegroundRainView : ContentView
{
    /// <summary>几何快照上限：避免碰撞遍历过重。</summary>
    private const int MaxObstacles = 120;

    /// <summary>相位泵周期：90ms ≈ 11fps，低频、只触达 GPU 表面。</summary>
    private static readonly TimeSpan PumpInterval = TimeSpan.FromMilliseconds(90);

    private readonly ForegroundRainDrawable _drawable = new();
    private readonly GraphicsView _graphics;

    private IDispatcherTimer? _pump;

    public ForegroundRainView()
    {
        // GraphicsView 铺满，Drawable 负责全部绘制；Windows 上是原生视图，必须输入透传。
        _graphics = new GraphicsView
        {
            Drawable = Helpers.SafeDrawable.Wrap(_drawable, "ForegroundRain"),
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            InputTransparent = true,
        };

        // 根容器单格叠放 Fill 铺满；这样 GraphicsView 的 dirtyRect 与页面本地坐标 1:1，
        // 采集到的控件绝对坐标可直接用于碰撞。
        var root = new Grid
        {
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            InputTransparent = true,
            CascadeInputTransparent = true,
        };
        root.Add(_graphics);
        Content = root;

        // 整层（含全部子层）不吃任何输入。
        InputTransparent = true;
        CascadeInputTransparent = true;
        BackgroundColor = Colors.Transparent;

        // 默认晴天无粒子：先把参数灌进 drawable，首帧不会因缺参而出错。
        _drawable.Scene = WeatherSceneKind.Sunny;
        _drawable.Intensity = 0;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    /// <summary>设置天气场景与雨量；有降水的场景开泵并刷新一次几何，晴/多云/雾停泵清屏。</summary>
    public void SetScene(WeatherSceneKind kind, double intensity)
    {
        _drawable.Scene = kind;
        _drawable.Intensity = Clamp01(intensity);

        if (HasPrecipitation(kind))
        {
            Start();
            RefreshGeometry();
        }
        else
        {
            Stop();
            // 静态场景清掉旧遮挡，并画一帧空白把前景擦干净。
            _drawable.Obstacles = null;
            _graphics.Invalidate();
        }
    }

    /// <summary>设置舞台避让矩形（由页面按 Galgame 面板位置传入，页面坐标）。传 null 取消避让。</summary>
    public void SetStageRect(RectF? rect)
    {
        _drawable.StageRect = rect;
        _graphics.Invalidate();
    }

    /// <summary>
    /// 重新采集控件几何快照。一次性操作：UI 线程上遍历宿主页可视树，取最多 120 块矩形。
    /// 供外部在页面 SizeChanged / 导航完成后调用一次；本视图首次 Loaded 也会自动调一次。
    /// </summary>
    public void RefreshGeometry()
    {
        // 遍历 / 布局读取必须在 UI 线程；Ui.Post 在 UI 线程上同步执行。
        Ui.Post(() =>
        {
            try
            {
                Page? page = FindHostPage();
                if (page is null) return;

                var snapshot = new List<RectF>(MaxObstacles);
                Collect(page, page, snapshot);

                _drawable.Obstacles = snapshot.Count > 0 ? snapshot : null;
                _graphics.Invalidate();
            }
            catch (Exception ex)
            {
                // 前置装饰层绝不把异常抛给主流程。
                System.Diagnostics.Debug.WriteLine("ForegroundRainView.RefreshGeometry -> " + ex.Message);
            }
        });
    }

    /// <summary>
    /// 开启相位泵。只有带粒子的场景（雨 / 雪）才启动；已运行则幂等返回。
    /// </summary>
    public void Start()
    {
        if (!HasPrecipitation(_drawable.Scene)) return;

        _pump ??= CreatePump();
        if (_pump.IsRunning) return;
        _pump.Start();
    }

    /// <summary>停止相位泵；幂等。</summary>
    public void Stop()
    {
        if (_pump is { IsRunning: true }) _pump.Stop();
    }

    /// <summary>相位泵是否在运行（只读）。</summary>
    public bool IsRunning => _pump?.IsRunning == true;

    /// <summary>handler 被摘掉时停泵，避免窗口关闭后后台空转。</summary>
    protected override void OnHandlerChanging(HandlerChangingEventArgs args)
    {
        if (args.NewHandler is null) Stop();
        base.OnHandlerChanging(args);
    }

    private IDispatcherTimer CreatePump()
    {
        var timer = Dispatcher.CreateTimer();
        timer.Interval = PumpInterval;
        timer.Tick += OnPumpTick;
        return timer;
    }

    /// <summary>相位泵 Tick：只有两行，只改 drawable 数据 + 触发 GPU 重绘，绝不碰任何 View / Layout 属性。</summary>
    private void OnPumpTick(object? sender, EventArgs e)
    {
        _drawable.Phase = (_drawable.Phase + 0.02) % 1.0;
        _graphics.Invalidate();
    }

    private void OnLoaded(object? sender, EventArgs e)
    {
        if (HasPrecipitation(_drawable.Scene)) Start();
        RefreshGeometry();
    }

    private void OnUnloaded(object? sender, EventArgs e)
    {
        Stop();
    }

    /// <summary>沿 Parent 链向上找到最近的宿主 Page（拿不到则回退到当前窗口的 Page）。</summary>
    private Page? FindHostPage()
    {
        for (Element? c = this; c is not null; c = c.Parent)
        {
            if (c is Page page) return page;
        }
        return Application.Current?.Windows.FirstOrDefault()?.Page;
    }

    /// <summary>
    /// 递归 IVisualTreeElement.GetVisualChildren()（Microsoft.Maui.Core，跨平台可用），
    /// 逐层筛选用户可见控件；达到上限即停。
    /// </summary>
    private void Collect(IVisualTreeElement node, Page page, List<RectF> output)
    {
        if (output.Count >= MaxObstacles) return;

        IReadOnlyList<IVisualTreeElement> children;
        try { children = node.GetVisualChildren(); }
        catch { return; }
        if (children is null) return;

        for (int i = 0; i < children.Count && output.Count < MaxObstacles; i++)
        {
            IVisualTreeElement child = children[i];

            // 非 VisualElement 的树节点仍可继续下钻。
            if (child is not VisualElement ve)
            {
                Collect(child, page, output);
                continue;
            }

            // 排除本视图自身及其子树（GraphicsView 也在其中，避免自碰撞）。
            if (IsSelfOrDescendant(ve)) continue;

            if (ve.IsVisible && ve.Width > 20 && ve.Height > 12 && IsTargetType(ve) && !IsFullScreenScrim(ve, page))
            {
                // 与项目现成范例 TitlePage.xaml.cs 的 OffsetIn 一致：沿父链累加 Bounds.X/Y。
                (double x, double y) = OffsetIn(ve, page);
                output.Add(new RectF((float)x, (float)y, (float)ve.Width, (float)ve.Height));
            }

            // 即使本节点不合格，子元素仍可能合格，继续递归。
            Collect(ve, page, output);
        }
    }

    /// <summary>只采集用户可见的实体控件；GraphicsView / 本视图不在类型清单内。</summary>
    private static bool IsTargetType(VisualElement ve) =>
        ve is Border or Button or Label or Microsoft.Maui.Controls.Frame or BoxView or Entry or Editor;

    /// <summary>排除输入透传的全屏遮罩层（这类层往往盖满页面，不该被当成控件表面）。</summary>
    private static bool IsFullScreenScrim(VisualElement ve, Page page)
    {
        if (!ve.InputTransparent || page.Width <= 0 || page.Height <= 0) return false;
        return ve.Width >= page.Width * 0.9 && ve.Height >= page.Height * 0.9;
    }

    private bool IsSelfOrDescendant(VisualElement ve)
    {
        for (Element? c = ve; c is not null; c = c.Parent)
        {
            if (ReferenceEquals(c, this)) return true;
        }
        return false;
    }

    private static (double X, double Y) OffsetIn(VisualElement v, VisualElement ancestor)
    {
        double x = 0, y = 0;
        for (VisualElement? c = v; c is not null && !ReferenceEquals(c, ancestor); c = c.Parent as VisualElement)
        {
            x += c.Bounds.X;
            y += c.Bounds.Y;
        }
        return (x, y);
    }

    private static bool HasPrecipitation(WeatherSceneKind kind) => kind is
        WeatherSceneKind.Drizzle or WeatherSceneKind.LightRain or
        WeatherSceneKind.HeavyRain or WeatherSceneKind.Storm or
        WeatherSceneKind.Snow;

    private static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;
}
