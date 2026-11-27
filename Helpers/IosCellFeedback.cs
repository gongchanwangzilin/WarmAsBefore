using System.Runtime.CompilerServices;
using WarmAsBefore.DesignSystem.Theme;

namespace WarmAsBefore.Helpers;

/// <summary>
/// 列表行的「按下」手感，照 iOS 的 UITableViewCell 来：
///
///   · 按下**立刻**缩一点点（0.975）。这里不用动画：先补 55ms 的中间帧再缩小，
///     用户读到的就是「点了没反应、过一会儿才动」，手指已经抬起来了。抬起才走弹簧回 1。
///   · 高亮**延迟 80ms** 才亮。iOS 的 cell highlight 本来就不是即时的 —— 手指划过列表时
///     每一行都闪一下才是廉价感；停够 80ms 才算「这一下是冲我来的」。
///   · 手指在 80ms 内移动超过 10dp 就取消高亮（拖走 / 在滚列表），缩放在松手时照常回弹。
///
/// 为什么高亮画在一层覆盖层上，而不是改 BackgroundColor：
/// 全仓的行背景有的是样式给的、有的是内联的 DynamicResource（见 HoverFeel 的注释）——
/// 往 Forms 侧的 Background 写一个本地值，那条动态链接就再也接不回来，换风格世界时这一行会钉死在旧色上。
/// 覆盖层只是叠在上面的一层 BoxView，卸掉时什么也不留。
///
/// 平台侧只观察、不消费：Windows 用 PointerPressed/Moved/Released，Android 用 Touch 事件，
/// 都不写 Handled —— 手势、滚动、点击照原样走，我们只借事件的时间点。
/// </summary>
public static class IosCellFeedback
{
    public static readonly BindableProperty EnabledProperty = BindableProperty.CreateAttached(
        "Enabled", typeof(bool), typeof(IosCellFeedback), false, propertyChanged: OnEnabledChanged);

    public static bool GetEnabled(VisualElement element) => (bool)element.GetValue(EnabledProperty);
    public static void SetEnabled(VisualElement element, bool value) => element.SetValue(EnabledProperty, value);

    /// <summary>按下缩到多小。0.975 在 300dp 宽的会话行上是 7dp 的收边，看得见但不跳。</summary>
    public static readonly BindableProperty ScaleProperty = BindableProperty.CreateAttached(
        "Scale", typeof(double), typeof(IosCellFeedback), 0.975);

    public static double GetScale(VisualElement element) => (double)element.GetValue(ScaleProperty);
    public static void SetScale(VisualElement element, double value) => element.SetValue(ScaleProperty, value);

    /// <summary>高亮色。默认是一层极淡的墨 —— iOS 的灰，不是主题色。</summary>
    public static readonly BindableProperty HighlightProperty = BindableProperty.CreateAttached(
        "Highlight", typeof(Color), typeof(IosCellFeedback), Color.FromRgba(0, 0, 0, 0.07));

    public static Color GetHighlight(VisualElement element) => (Color)element.GetValue(HighlightProperty);
    public static void SetHighlight(VisualElement element, Color value) => element.SetValue(HighlightProperty, value);

    /// <summary>高亮延迟（毫秒）。</summary>
    public const int HighlightDelayMs = 80;

    /// <summary>超过这个位移（dp）就算「手在动」，高亮取消。</summary>
    public const double MoveSlop = 10;

    private static readonly ConditionalWeakTable<View, Driver> Drivers = new();

    private static void OnEnabledChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (newValue is not true || bindable is not View view) return;
        _ = Drivers.GetValue(view, static v => new Driver(v));
    }

    private sealed class Driver
    {
        private readonly View _view;
        private BoxView? _wash;
        private bool _pressed;
        private bool _highlighted;
        private bool _hooked;
        private double _downX, _downY;

        public Driver(View view)
        {
            _view = view;
            // 手势与平台视图要等进树才有；压测里这一页可能被反复重挂，Loaded 每次补挂一遍也安全（有 _hooked）。
            view.Loaded += (_, _) => Hook();
            view.Unloaded += (_, _) => Reset();
        }

        private void Hook()
        {
            if (_hooked) return;
            var platform = _view.Handler?.PlatformView;
            if (platform is null) return;
            _view.HandlerChanged += (_, _) => { _hooked = false; Hook(); };
            _hooked = true;

#if WINDOWS
            if (platform is Microsoft.UI.Xaml.FrameworkElement fe)
            {
                fe.PointerPressed += (_, e) => Down(e.GetCurrentPoint(fe).Position.X, e.GetCurrentPoint(fe).Position.Y);
                fe.PointerMoved += (_, e) =>
                {
                    if (!_pressed) return;
                    var p = e.GetCurrentPoint(fe).Position;
                    Moved(p.X - _downX, p.Y - _downY);
                };
                fe.PointerReleased += (_, _) => Up();
                fe.PointerCanceled += (_, _) => Up();
                fe.PointerExited += (_, _) => Up();
            }
#elif ANDROID
            if (platform is Android.Views.View av)
            {
                av.Touch += (_, e) =>
                {
                    if (e.Event is null) return;
                    switch (e.Event.ActionMasked)
                    {
                        case Android.Views.MotionEventActions.Down:
                            Down(e.Event.GetX(), e.Event.GetY());
                            break;
                        case Android.Views.MotionEventActions.Move:
                            if (!_pressed) return;
                            Moved(e.Event.GetX() - _downX, e.Event.GetY() - _downY);
                            break;
                        case Android.Views.MotionEventActions.Up:
                        case Android.Views.MotionEventActions.Cancel:
                            Up();
                            break;
                    }
                    // 不消费：e.Handled 保持原样，滚动与点击都照旧。
                };
            }
#endif
        }

        private void Down(double x, double y)
        {
            _downX = x;
            _downY = y;
            _pressed = true;
            _highlighted = false;
            // 缩放在按下这一帧就落下去：这是「即时反馈」，不是一段动画。关掉动效时更该直接落。
            if (!MotionTokens.Disabled)
            {
                _view.AbortAnimation("ScaleTo");
                _view.Scale = GetScale(_view);
            }
            var token = ++_generation;
            _view.Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(HighlightDelayMs), () =>
            {
                if (token != _generation || !_pressed || _view.Window is null) return;
                ShowWash(true);
            });
        }

        private int _generation;

        private void Moved(double dx, double dy)
        {
            if (Math.Abs(dx) > MoveSlop || Math.Abs(dy) > MoveSlop)
            {
                // 拖走了：高亮取消，缩放留到松手再回弹（iOS 也是手指离开后才弹回来）。
                _generation++;
                ShowWash(false);
            }
        }

        private void Up()
        {
            if (!_pressed) return;
            _pressed = false;
            _generation++;
            ShowWash(false);
            if (_view.Scale != 1) _ = IosMotion.ScaleTo(_view, 1, IosMotion.Press);
        }

        private void Reset()
        {
            _pressed = false;
            _highlighted = false;
            _generation++;
            if (_wash is not null) _wash.Opacity = 0;
            _view.Scale = 1;
        }

        /// <summary>覆盖层只建一次。行根节点不是 Grid 时不画高亮（没有能叠上去的位置），缩放照旧。</summary>
        private void ShowWash(bool on)
        {
            if (on == _highlighted) return;
            if (_wash is null)
            {
                if (_view is not Grid grid) return;
                _wash = new BoxView
                {
                    Color = GetHighlight(_view),
                    InputTransparent = true,
                    Opacity = 0,
                    HorizontalOptions = LayoutOptions.Fill,
                    VerticalOptions = LayoutOptions.Fill
                };
                Grid.SetRowSpan(_wash, Math.Max(1, grid.RowDefinitions.Count));
                Grid.SetColumnSpan(_wash, Math.Max(1, grid.ColumnDefinitions.Count));
                grid.Children.Add(_wash);
            }
            _highlighted = on;
            if (MotionTokens.Disabled) { _wash.Opacity = on ? 1 : 0; return; }
            _wash.AbortAnimation("FadeTo");
            _ = _wash.FadeTo(on ? 1 : 0, (uint)(on ? 110 : 160), IosMotion.Standard.Ease(0));
        }
    }
}
