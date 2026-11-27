namespace WarmAsBefore.Helpers;

/// <summary>
/// 安全区留白：刘海/挖孔、状态栏、底部手势条与圆角。
///
/// 只做一件事 —— 把系统给的 inset 换成宿主布局的 Padding，并且**不重复留白**：
/// Android 上 MAUI 9 默认并不铺到系统栏底下（Activity 内容区本来就在栏内），
/// 这时 RootWindowInsets 照样是非零的，照着它再加一圈，底部就会多出一条谁也不该占的空白。
/// 所以先判断「这一页是不是真的铺满了整个窗口」：页高 ≈ 窗高（差 8dp 以内）才说明内容压到了栏底下，
/// 才需要自己补；否则交给系统，返回 0。
///
/// 滚动内容能延伸到安全区下、但行与行之间有 padding —— 那是各页自己的 ScrollView 内边距（22px 那类），
/// 这里只保证最后一行不会被手势条切掉。
/// </summary>
public static class SafeArea
{
    /// <summary>判定「铺满窗口」的容差（dp）。小于它就算同一块高度。</summary>
    private const double EdgeToEdgeSlop = 8;

    /// <summary>
    /// 给 <paramref name="target"/> 挂上安全区留白。会记住它原本的 Padding，inset 只是在原值上叠加，
    /// 所以同一页里别处写死的留白不会被抹掉。可重复调用（同一实例只挂一次）。
    /// </summary>
    public static void Attach(Layout target, double extraBottom = 0)
    {
        if (target is null) return;
        var state = new State(target, extraBottom);
        _states.Add(target, state);
        target.Loaded += (_, _) => state.Apply();
        target.SizeChanged += (_, _) => state.Apply();
        target.HandlerChanged += (_, _) => state.Apply();
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Layout, State> _states = new();

    private sealed class State
    {
        private readonly Layout _target;
        private readonly double _extraBottom;
        private readonly Thickness _base;
        private bool _lastSet;

        public State(Layout target, double extraBottom)
        {
            _target = target;
            _extraBottom = extraBottom;
            _base = target.Padding;
        }

        public void Apply()
        {
            try
            {
                var (top, bottom) = Insets(_target);
                if (top <= 0 && bottom <= 0 && _extraBottom <= 0)
                {
                    if (_lastSet) { _target.Padding = _base; _lastSet = false; }
                    return;
                }
                var pad = new Thickness(
                    _base.Left,
                    _base.Top + top,
                    _base.Right,
                    _base.Bottom + bottom + _extraBottom);
                if (pad.Equals(_target.Padding)) return;
                _target.Padding = pad;
                _lastSet = true;
            }
            catch (Exception ex) { App.WriteLog("SafeArea.Apply -> " + ex.Message); }
        }
    }

    /// <summary>当前窗口的安全区（dp）。</summary>
    public static (double Top, double Bottom) Insets(VisualElement element)
    {
#if ANDROID
        var activity = Platforms.Android.MainActivity.CurrentActivity;
        var window = activity?.Window;
        var decor = window?.DecorView;
        var insets = decor?.RootWindowInsets;
        if (decor is null || insets is null) return (0, 0);

        var density = element.Density > 0 ? element.Density : (decor.Context?.Resources?.DisplayMetrics?.Density ?? 1);
        if (density <= 0) density = 1;

        int pxTop, pxBottom;
        if (OperatingSystem.IsAndroidVersionAtLeast(30))
        {
            var bars = insets.GetInsets(Android.Views.WindowInsets.Type.SystemBars());
            pxTop = bars.Top;
            pxBottom = bars.Bottom;
        }
        else
        {
#pragma warning disable CA1422 // 低版本分支，只在 30 以下走到
            pxTop = insets.SystemWindowInsetTop;
            pxBottom = insets.SystemWindowInsetBottom;
#pragma warning restore CA1422
        }
        if (pxTop <= 0 && pxBottom <= 0) return (0, 0);

        // 内容没铺到栏底下时，系统已经让出过位置，这里再加就是双份。
        var windowHeight = decor.Height / density;
        var page = element.Window?.Page;
        var contentHeight = page?.Height > 1 ? page.Height : element.Window?.Height ?? 0;
        if (windowHeight > 1 && contentHeight > 1 && contentHeight < windowHeight - EdgeToEdgeSlop) return (0, 0);

        return (pxTop / density, pxBottom / density);
#else
        return (0, 0);
#endif
    }
}
