using Microsoft.Maui.Controls;
using Microsoft.Maui.Dispatching;
using WarmAsBefore.Drawables;
using WarmAsBefore.Models.Background;
using WarmAsBefore.Services.Background;

namespace WarmAsBefore.Controls;

/// <summary>
/// 天气场景背景视图：自绘场景 + 静态可读性遮罩，整层输入透传。
///
/// 设计红线（项目刚从「逐帧换源 → GC 风暴 → LayoutCycle 卡死」里爬出来）：
/// 1. 场景的「雨滴运动」只用 GraphicsView 自绘 + 低频 Invalidate 实现。
///    Invalidate 只触发 GPU 重绘，不走 Measure/Arrange，是唯一安全的动效通道；
///    绝不能用 Timer 改任何 Layout 属性或 Image.Source。
/// 2. 显隐只写 IsVisible，不做 Opacity 动画；本类遮罩的 Opacity=0.12 是静态常量，不是动画。
/// 3. 不新增合成层 / Shadow，不做 Scale / TranslationY，不做循环视图动画。
///    下面的相位泵是「重绘泵」，不是视图动画。
///
/// Z 序（从底到顶，四层）：
///   ① 根容器 Grid：同一格叠放，Fill 铺满父容器。
///   ② 场景层 GraphicsView：自绘天空 / 云 / 雨 / 雪 / 闪电。
///   ③ UI 遮罩层 BoxView：语义色 token 压暗，保证其上文字可读。
///   ④ 宿主页面内容：在本控件之外叠加；本控件整体输入透传，绝不拦截点击。
/// </summary>
public sealed class WeatherSceneView : ContentView
{
    /// <summary>相位泵周期：100ms ≈ 10fps，低频、GPU 重绘、不触发布局。</summary>
    private static readonly TimeSpan PumpInterval = TimeSpan.FromMilliseconds(100);

    /// <summary>闪电衰减一步的间隔（与 MainGameViewModel 打雷白闪节奏一致）。</summary>
    private static readonly TimeSpan FlashStepInterval = TimeSpan.FromMilliseconds(45);

    /// <summary>闪电衰减步数：Lightning = 1 - i/8，第 8 步归零。</summary>
    private const int FlashSteps = 8;

    private readonly WeatherSceneDrawable _drawable = new();
    private readonly GraphicsView _sceneLayer;

    private IDispatcherTimer? _pump;
    private WeatherSceneKind _sceneKind;
    private bool _loaded;

    /// <summary>用于丢弃上一次尚未播完的闪电回调，保证每次闪电都是独立一次性播放。</summary>
    private long _flashToken;

    public WeatherSceneView()
    {
        // ① 根容器：无显式行列定义时 Grid 只有一个隐式 * 格，多个子视图同格叠放，后加者在上。
        var root = new Grid
        {
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            InputTransparent = true,
            CascadeInputTransparent = true,
        };

        // ② 场景层：Fill 铺满，由 drawable 按画布比例绘制；输入透传，GraphicsView 在 Windows 上是原生视图，
        //    不透传会吞掉父级手势。
        _sceneLayer = new GraphicsView
        {
            Drawable = Helpers.SafeDrawable.Wrap(_drawable, "WeatherScene"),
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            InputTransparent = true,
        };

        // ③ UI 遮罩层：颜色走 DynamicResource 语义 token（换肤自动跟随），
        //    Opacity=0.12 是静态常量（不是动画），仅用于保证场景之上的文字可读。
        var scrim = new BoxView
        {
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            InputTransparent = true,
            Opacity = 0.12,
        };
        scrim.SetDynamicResource(BackgroundColorProperty, "OverlayDim");

        root.Add(_sceneLayer);
        root.Add(scrim);
        Content = root;

        // ④ 整体输入透传策略：容器与全部子层都不接收输入，点击一律交给宿主页面。
        InputTransparent = true;
        CascadeInputTransparent = true;

        // 默认场景（Sunny）也要先灌进 drawable，否则首帧画布无参数可画。
        ApplyScene(_sceneKind);

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    /// <summary>当前场景种类；setter 取 BackgroundCatalog.Scenes 的参数表交给 drawable 并重绘一帧。</summary>
    public WeatherSceneKind SceneKind
    {
        get => _sceneKind;
        set
        {
            _sceneKind = value;
            ApplyScene(value);
            // 天气运行时可能从雨转晴：同步相位泵，保证静态场景零开销。
            SyncPump();
        }
    }

    /// <summary>相位泵是否在运行（只读）。</summary>
    public bool IsSceneRunning => _pump?.IsRunning == true;

    /// <summary>与 <see cref="SceneKind"/> setter 等价的显式方法。</summary>
    public void SetScene(WeatherSceneKind kind) => SceneKind = kind;

    /// <summary>
    /// 下发由能见度 + 时间算出的光照参数；lightFxEnabled=false 关闭光柱与折射。
    /// 调用顺序约定：先 SetScene 定场景（会写入场景默认光强），再调本方法覆盖。
    /// </summary>
    public void SetLighting(double dirX, double elevation, double intensity, bool lightFxEnabled)
    {
        _drawable.LightDirX = dirX;
        _drawable.LightElevation = elevation;
        _drawable.LightIntensity = intensity;
        _drawable.LightEffectsEnabled = lightFxEnabled;
        _sceneLayer.Invalidate();
    }

    /// <summary>
    /// 开启相位泵。只有带粒子的场景（雨 / 雪 / 雷暴）才真正启动；
    /// Sunny / Cloudy / Fog 是静态场景，零开销。已运行则幂等返回。
    /// </summary>
    public void StartAnimation()
    {
        if (!HasParticles(_sceneKind)) return;

        _pump ??= CreatePump();
        if (_pump.IsRunning) return;
        _pump.Start();
    }

    /// <summary>停止相位泵并把相位归零；幂等。</summary>
    public void StopAnimation()
    {
        if (_pump is { IsRunning: true }) _pump.Stop();
        _drawable.Phase = 0;
        _sceneLayer.Invalidate();
    }

    /// <summary>
    /// 触发一次闪电：Lightning=1，随后用 DispatchDelayed 分 8 步、每步 45ms 递减到 0。
    /// 一次性播放，播完停，绝不循环。
    /// </summary>
    public void FlashLightning()
    {
        // 递增令牌：若上一次还没播完又被触发，旧回调会因为令牌不匹配直接退出，不会把新闪电拽低。
        var token = ++_flashToken;
        _drawable.Lightning = 1;
        _sceneLayer.Invalidate();
        StepFlash(token, 1);
    }

    /// <summary>测试用：直接设相位并重绘一帧，不启停泵。</summary>
    public void SetPhaseForTest(double phase)
    {
        _drawable.Phase = phase;
        _sceneLayer.Invalidate();
    }

    /// <summary>窗口 / handler 被摘掉时停泵，避免后台空转。</summary>
    protected override void OnHandlerChanging(HandlerChangingEventArgs args)
    {
        if (args.NewHandler is null) StopAnimation();
        base.OnHandlerChanging(args);
    }

    private IDispatcherTimer CreatePump()
    {
        var timer = Dispatcher.CreateTimer();
        timer.Interval = PumpInterval;
        timer.Tick += OnPumpTick;
        return timer;
    }

    /// <summary>相位泵 Tick：只有两行，只改 drawable 数据 + 触发 GPU 重绘，绝不碰任何 Layout / View 属性。</summary>
    private void OnPumpTick(object? sender, EventArgs e)
    {
        _drawable.Phase = (_drawable.Phase + 0.02) % 1.0;
        _sceneLayer.Invalidate();
    }

    /// <summary>闪电单步：过期令牌直接退出；到第 8 步归零并停止递归。</summary>
    private void StepFlash(long token, int step)
    {
        if (token != _flashToken) return;

        _drawable.Lightning = step >= FlashSteps ? 0 : 1.0 - step / (double)FlashSteps;
        _sceneLayer.Invalidate();
        if (step >= FlashSteps) return;

        Dispatcher.DispatchDelayed(FlashStepInterval, () => StepFlash(token, step + 1));
    }

    private void ApplyScene(WeatherSceneKind kind)
    {
        var def = BackgroundCatalog.Scenes[kind];
        _drawable.Scene = def;
        // 场景自带强度作为默认光强；外部若需精确控制，请在 SetScene 之后调 SetLighting 覆盖。
        _drawable.LightIntensity = 0.35 + 0.55 * Math.Clamp(def.Intensity, 0.0, 1.0);
        _sceneLayer.Invalidate();
    }

    private void OnLoaded(object? sender, EventArgs e)
    {
        _loaded = true;
        if (HasParticles(_sceneKind)) StartAnimation();
    }

    private void OnUnloaded(object? sender, EventArgs e)
    {
        _loaded = false;
        StopAnimation();
    }

    /// <summary>根据当前场景与挂载状态同步相位泵：有粒子且已加载才开，否则停。</summary>
    private void SyncPump()
    {
        if (_loaded && HasParticles(_sceneKind)) StartAnimation();
        else StopAnimation();
    }

    /// <summary>只有会下落的元素（雨丝 / 雪花）才需要重绘泵；晴 / 多云 / 雾是静态的。</summary>
    private static bool HasParticles(WeatherSceneKind kind) => kind is
        WeatherSceneKind.Drizzle or WeatherSceneKind.LightRain or
        WeatherSceneKind.HeavyRain or WeatherSceneKind.Storm or
        WeatherSceneKind.Snow;
}
