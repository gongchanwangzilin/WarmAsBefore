using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Reflection;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Graphics;
using WarmAsBefore.Services;

namespace WarmAsBefore.Controls;

/// <summary>
/// 全站唯一的「数据还没到」视觉：一组骨架条，兼作启动遮罩。
///
/// 两条硬规矩：
///   · 只用 <see cref="VisualElement.IsVisible"/> 切换显隐，永远不写 Opacity。
///     透明度是进场动画、禁用态、卡片显隐共用的输出量，谁都能写就等于谁都可能把它留在 0 上
///     （「再续情缘」按钮消失就是这么来的）。骨架的淡出交给「直接换成内容」，不靠透明度过渡。
///   · 颜色一律取自世界的令牌（骨架条 WWellBrush），不发明新色；
///     条上的静态 Opacity 只是「同一块面淡一点」，不参与任何动画。
///
/// 兜底超时：<paramref name="Attach"/> 默认 1800ms 后无条件收起。
/// 空库（真的没有存档/角色）时信号永远不「就绪」，没有这道保险就把骨架永久盖在内容上——
/// 那和「按钮消失」是同一类 bug，只是换了个方向。
/// </summary>
public sealed class LoadingVeil : Border
{
    private static readonly double[] RightPad = { 0, 68, 132, 40, 96, 20 };
    private CancellationTokenSource? _timeout;
    private INotifyPropertyChanged? _hooked;
    private readonly List<INotifyCollectionChanged> _collections = new();
    private bool _dismissed;

    /// <summary>骨架条数只在构造时决定：页面用默认 4 条，网格页多给两条。</summary>
    public LoadingVeil(int bars = 4, string? hint = null)
    {
        if (bars < 1) bars = 1;
        HorizontalOptions = LayoutOptions.Fill;
        VerticalOptions = LayoutOptions.Fill;
        StrokeThickness = 0;
        Padding = new Thickness(0);
        BackgroundColor = Colors.Transparent;
        // 遮罩不吞点击：加载期间返回键/标题栏仍然可用（遮罩是提示，不是模态锁）。
        InputTransparent = true;

        var stack = new VerticalStackLayout
        {
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Start,
        };
        stack.SetDynamicResource(Microsoft.Maui.Controls.Layout.PaddingProperty, "WPadHero");
        stack.SetDynamicResource(Microsoft.Maui.Controls.VerticalStackLayout.SpacingProperty, "WGapItem");

        // 顶部一块「缩略图/封面」占位，读起来像一张还没长出内容的卡，而不是一堆等长线条。
        var hero = new Border
        {
            HeightRequest = 72,
            StrokeThickness = 0,
            Opacity = 0.55,
            HorizontalOptions = LayoutOptions.Fill,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
        };
        hero.SetDynamicResource(BackgroundColorProperty, "WWellBrush");
        stack.Children.Add(hero);

        for (var i = 0; i < bars; i++)
        {
            var bar = new Border
            {
                HeightRequest = 13,
                StrokeThickness = 0,
                Opacity = 0.45,
                HorizontalOptions = LayoutOptions.Fill,
                Margin = new Thickness(0, 0, RightPad[i % RightPad.Length], 0),
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 6 },
            };
            bar.SetDynamicResource(BackgroundColorProperty, "WWellBrush");
            stack.Children.Add(bar);
        }

        if (!string.IsNullOrEmpty(hint))
        {
            var label = new Label
            {
                Text = hint,
                HorizontalTextAlignment = TextAlignment.Center,
                HorizontalOptions = LayoutOptions.Fill,
                FontSize = 12,
            };
            label.SetDynamicResource(Label.TextColorProperty, "WarmMuted");
            stack.Children.Add(label);
        }

        Content = stack;
    }

    /// <summary>收起：只切 IsVisible，幂等，收起后不再回来（同一次装载不该演第二遍）。</summary>
    public void Dismiss()
    {
        if (_dismissed) return;
        _dismissed = true;
        try { _timeout?.Cancel(); } catch { }
        _timeout = null;
        if (_hooked is not null) _hooked.PropertyChanged -= OnSourceChanged;
        foreach (var c in _collections) c.CollectionChanged -= OnCollectionChanged;
        _collections.Clear();
        IsVisible = false;
    }

    /// <summary>已本地化的「加载中」文案（四个语言包都有 Common_Loading 键）。</summary>
    public static string LoadingText => LocalizationService.Current["Common_Loading"];

    /// <summary>
    /// 遮罩：铺在页面上并立即显示，返回引用给调用方自己 <see cref="Dismiss"/>。
    /// 启动遮罩走这条——装载方（App/首页）决定什么时候收。
    /// </summary>
    public static LoadingVeil Show(ContentPage page, int bars = 4, string? hint = null)
    {
        var veil = new LoadingVeil(bars, hint ?? LoadingText);
        PlaceOnto(page, veil);
        return veil;
    }

    /// <summary>
    /// 页级骨架：<paramref name="readySignals"/> 是 ViewModel 上已有的属性名，
    /// 任一信号「到位」（bool 为真 / 集合非空 / 字符串非空 / 对象非 null）就收起骨架。
    /// 页面因此不需要为加载态新增状态；没有可用信号时才在 VM 上加最小的 bool。
    /// </summary>
    public static LoadingVeil? Attach(ContentPage page, object? source, params string[] readySignals)
        => Attach(page, source, 1800, readySignals);

    public static LoadingVeil? Attach(ContentPage page, object? source, int timeoutMs, params string[] readySignals)
    {
        if (page is null || source is null || readySignals is null || readySignals.Length == 0) return null;
        // 同一个页面实例只铺一层：构造函数里铺好后，再次 OnAppearing 不该多叠一张骨架。
        if (FindOn(page) is { } existing) return existing;
        var veil = new LoadingVeil();
        PlaceOnto(page, veil);
        veil._hooked = source as INotifyPropertyChanged;
        if (veil._hooked is not null) veil._hooked.PropertyChanged += veil.OnSourceChanged;
        veil._Evaluate(source, readySignals);
        // 保险丝：数据永远不来（空库、加载抛异常）也不能让骨架一直盖着。
        veil._timeout = new CancellationTokenSource();
        Task.Delay(timeoutMs, veil._timeout.Token).ContinueWith(t =>
        {
            if (t.IsCompletedSuccessfully) MainThread.BeginInvokeOnMainThread(veil.Dismiss);
        }, TaskScheduler.Default);
        return veil;
    }

    private void OnSourceChanged(object? sender, PropertyChangedEventArgs e) => _Recheck(sender);

    private void _Recheck(object? sender)
    {
        if (_dismissed || sender is null) return;
        MainThread.BeginInvokeOnMainThread(() => _Evaluate(sender, _Signals));
    }

    private string[] _Signals = Array.Empty<string>();

    private void _Evaluate(object source, string[] signals)
    {
        _Signals = signals;
        foreach (var name in signals)
        {
            object? value = null;
            try { value = source.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(source); }
            catch { }
            if (value is INotifyCollectionChanged inc && !_collections.Contains(inc))
            {
                _collections.Add(inc);
                inc.CollectionChanged += OnCollectionChanged;
            }
            if (IsReady(value)) { Dismiss(); return; }
        }
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => _Recheck(sender);

    /// <summary>「这条信号到位了吗」：布尔看真值，集合看有没有元素，字符串看有没有内容，其余对象看非 null。</summary>
    private static bool IsReady(object? value) => value switch
    {
        null => false,
        bool b => b,
        string s => s.Length > 0,
        IEnumerable e => e.Cast<object?>().Any(),
        _ => true,
    };

    /// <summary>页面上已有的骨架层（没有则 null）。</summary>
    private static LoadingVeil? FindOn(ContentPage page) =>
        page.Content is Layout l ? l.Children.OfType<LoadingVeil>().FirstOrDefault() : null;

    /// <summary>
    /// 把遮罩盖到页面最上层：根是 Grid 就跨满所有行，否则外面套一层 Grid。
    /// 套 Grid 是安全的——Grid 填满父容器，页面原本的布局不受影响。
    /// </summary>
    private static void PlaceOnto(ContentPage page, LoadingVeil veil)
    {
        var root = page.Content;
        if (root is Grid g)
        {
            if (g.Children.Any(c => c is LoadingVeil)) return;
            Grid.SetRow(veil, 0);
            Grid.SetRowSpan(veil, Math.Max(1, g.RowDefinitions.Count));
            g.Children.Add(veil);
            return;
        }
        if (root is Layout l && l.Children.Any(c => c is LoadingVeil)) return;
        if (root is null) { page.Content = veil; return; }
        var wrap = new Grid();
        wrap.Children.Add(root);
        wrap.Children.Add(veil);
        page.Content = wrap;
    }
}
