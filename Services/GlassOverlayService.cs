using System.Runtime.CompilerServices;
using WarmAsBefore.Controls;
using WarmAsBefore.DesignSystem.Theme;

namespace WarmAsBefore.Services;

/// <summary>
/// 把玻璃叠加层挂到每个页面根 Grid 上，并跟随 ThemeManager 实时切换效果。
/// 磨砂 = 半透明磨砂；毛玻璃 = 磨砂的高级版（叠加层 + 边缘高光 + 装饰层背景模糊）。
/// 真实模糊仅作用于页面的 DecorBlur 装饰层（如标题页渐变光斑），绝不覆盖内容。
/// </summary>
public sealed class GlassOverlayService
{
    private readonly ThemeManager _theme;

    /// <summary>
    /// 弱引用表：页面被 Shell 回收后叠加层随之释放。
    /// 旧实现用 Dictionary 强引用每个访问过的页面，导致历史页面永不回收，
    /// 且每次导航都要把全部页面的叠加层重刷一遍（O(N) 随会话增长）→ 切换页面越来越卡。
    /// </summary>
    private readonly ConditionalWeakTable<ContentPage, GlassOverlay> _overlays = new();
    private readonly ConditionalWeakTable<ContentPage, View> _decorBlurs = new();

    /// <summary>当前页：只有它需要跟随主题刷新，避免对历史页面做无用布局失效。</summary>
    private ContentPage? _current;

    private readonly object _lock = new();

    public GlassOverlayService(ThemeManager theme) => _theme = theme;

    public void Start()
    {
        _theme.Changed += Refresh;
        var shell = Shell.Current;
        if (shell is not null)
        {
            shell.Navigated += (_, _) => Attach(Shell.Current?.CurrentPage);
            Attach(shell.CurrentPage);
        }
        else
        {
            // Shell 尚未就绪：延迟重试一次（首次启动时序）
            _ = Task.Delay(1500).ContinueWith(_ =>
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (Shell.Current is null) return;
                    Shell.Current.Navigated += (_, _) => Attach(Shell.Current?.CurrentPage);
                    Attach(Shell.Current.CurrentPage);
                }));
        }
    }

    private void Attach(Page? page)
    {
        if (page is not ContentPage cp) return;

        GlassOverlay overlay;
        lock (_lock)
        {
            if (_overlays.TryGetValue(cp, out var existing))
            {
                overlay = existing;
            }
            else
            {
                overlay = new GlassOverlay();
                if (cp.Content is Grid grid)
                {
                    // 必须显式跨满整行整列：根 Grid 普遍为 RowDefinitions="Auto,*"（页头 + 内容），
                    // 不加 RowSpan 时叠加层只落在第 0 行（约 56dp 的标题栏），
                    // 表现为「开了玻璃却看不到效果 / 开关没反应」。
                    Grid.SetRow(overlay, 0);
                    Grid.SetColumn(overlay, 0);
                    Grid.SetRowSpan(overlay, Math.Max(1, grid.RowDefinitions.Count));
                    Grid.SetColumnSpan(overlay, Math.Max(1, grid.ColumnDefinitions.Count));
                    grid.Children.Add(overlay);
                }
                else if (cp.Content is View old)
                {
                    var wrap = new Grid();
                    cp.Content = null;
                    wrap.Children.Add(old);
                    wrap.Children.Add(overlay);
                    cp.Content = wrap;
                }
                else
                {
                    return;
                }
                _overlays.Add(cp, overlay);
            }

            _current = cp;

            // 收集页面装饰层（x:Name="DecorBlur"）——真实模糊只作用在这里
            if (!_decorBlurs.TryGetValue(cp, out _))
            {
                var decor = cp.FindByName<View>("DecorBlur");
                if (decor is not null) _decorBlurs.Add(cp, decor);
            }
        }

        Refresh();
    }

    private void Refresh()
    {
        // 先快照主题值，避免在 UI 线程上重复读取
        var frost = _theme.Frost || _theme.Glass;
        var glass = _theme.Glass;
        var liquid = _theme.Liquid;
        var translucency = _theme.GlassTranslucency;
        var reduced = _theme.ReducedTransparency;

        GlassOverlay? overlay = null;
        View? decor = null;
        lock (_lock)
        {
            if (_current is not null)
            {
                _overlays.TryGetValue(_current, out overlay);
                _decorBlurs.TryGetValue(_current, out decor);
            }
        }
        if (overlay is null && decor is null) return;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (overlay is not null)
            {
                // 先写输入参数、后写开关，保证叠加层内部那次 Refresh 看到的是最终一致的状态
                overlay.Translucency = translucency;
                overlay.ReducedTransparency = reduced;
                overlay.Frost = frost;
                overlay.Glass = glass;
                overlay.Liquid = liquid;
            }
            if (decor is not null)
            {
                // 液态玻璃与毛玻璃都提供真实背景模糊（只作用于装饰层）
                var blurOn = glass || liquid;
                decor.IsVisible = blurOn;
                DecorBlur.Apply(decor, blurOn);
            }
        });
    }
}
