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
    private readonly Dictionary<ContentPage, GlassOverlay> _overlays = new();
    private readonly Dictionary<ContentPage, View> _decorBlurs = new();
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
        lock (_lock)
        {
            if (!_overlays.ContainsKey(cp))
            {
                var overlay = new GlassOverlay();
                if (cp.Content is Grid grid)
                {
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
                _overlays[cp] = overlay;
            }
            // 收集页面装饰层（x:Name="DecorBlur"）——真实模糊只作用在这里
            if (!_decorBlurs.ContainsKey(cp))
            {
                var decor = cp.FindByName<View>("DecorBlur");
                if (decor is not null) _decorBlurs[cp] = decor;
            }
        }
        Refresh();
    }

    private void Refresh()
    {
        var frost = _theme.Frost || _theme.Glass;
        var glass = _theme.Glass;
        var liquid = _theme.Liquid;
        List<GlassOverlay> overlays;
        List<View> decorBlurs;
        lock (_lock)
        {
            overlays = _overlays.Values.ToList();
            decorBlurs = _decorBlurs.Values.ToList();
        }
        MainThread.BeginInvokeOnMainThread(() =>
        {
            foreach (var o in overlays)
            {
                o.Frost = frost;
                o.Glass = glass;
                o.Liquid = liquid;
            }
            foreach (var d in decorBlurs)
            {
                // 液态玻璃与毛玻璃都提供真实背景模糊（只作用于装饰层）
                d.IsVisible = glass || liquid;
                DecorBlur.Apply(d, glass || liquid);
            }
        });
    }
}
