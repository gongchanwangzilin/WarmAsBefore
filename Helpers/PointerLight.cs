using Microsoft.Maui.Graphics;

namespace WarmAsBefore.Helpers;

/// <summary>
/// 全窗口一个**指针光源**：鼠标在哪，光就在哪，离得远的玻璃也跟着暗下去。
///
/// 为什么要做全局而不是逐控件的悬停：用户要的是「不管鼠标在哪个位置，它都能检测到改动并做出反应，
/// 光标离它的远近也会改变光照强度」。玻璃是一块被一盏灯照着的材料——灯在房间里的位置只有一个，
/// 不是每块玻璃自己有一盏。所以这里在窗口层收一次指针，广播给所有订阅的玻璃，
/// 由每块玻璃自己算「我在它的哪个方向、多远、照到我还有几分」。
///
/// 节流：8ms（约 120fps）。光不再是「烘进位图」的东西，而是每块玻璃在绘制回调里现画的一层，
/// 所以它能跟到高刷新率 —— 代价只是一次径向渐变，和游戏里画一盏灯的 halo 同级。
/// </summary>
public static class PointerLight
{
    private static readonly object Gate = new();
    private static readonly List<Action<double, double>> Listeners = new();
    private static readonly List<Action> Moved = new();

    private static double _x = -1;
    private static double _y = -1;
    private static bool _hooked;
    private static long _lastTick;
    private static bool _pendingFlush;

    /// <summary>指针在窗口坐标里的位置；-1 表示指针不在窗口里。</summary>
    public static Point Position
    {
        get { lock (Gate) return new Point(_x, _y); }
    }

    public static bool Active
    {
        get { lock (Gate) return _x >= 0; }
    }

    /// <summary>订阅「指针动了」：每帧最多一次，参数是窗口坐标。</summary>
    public static void Subscribe(Action<double, double> handler)
    {
        lock (Gate)
        {
            if (!Listeners.Contains(handler)) Listeners.Add(handler);
        }
    }

    public static void Unsubscribe(Action<double, double> handler)
    {
        lock (Gate) Listeners.Remove(handler);
    }

    /// <summary>
    /// 挂上窗口层的指针事件。窗口还没建好时什么都不做 —— 由 App 在窗口创建后再调一次。
    /// 幂等：同一个根元素只挂一次。
    /// </summary>
    public static void Hook()
    {
#if WINDOWS
        if (_hooked) return;
        try
        {
            var win = Microsoft.Maui.Controls.Application.Current?.Windows
                .FirstOrDefault(w => w.Handler is not null && w is not Views.ToolWindow);
            if (win?.Handler?.PlatformView is not Microsoft.UI.Xaml.Window w) return;
            if (w.Content is not Microsoft.UI.Xaml.UIElement root) return;

            _hooked = true;
            root.PointerMoved += (_, e) =>
            {
                var p = e.GetCurrentPoint(root).Position;
                Push(p.X, p.Y);
            };
            root.PointerExited += (_, _) => Push(-1, -1);
            root.PointerEntered += (_, e) =>
            {
                var p = e.GetCurrentPoint(root).Position;
                Push(p.X, p.Y);
            };
        }
        catch (Exception ex)
        {
            App.WriteLog("PointerLight.Hook -> " + ex.Message);
        }
#endif
    }

    private static void Push(double x, double y)
    {
        var now = Environment.TickCount64;
        Action<double, double>[] copy;
        lock (Gate)
        {
            // 位置没动就不广播（WinUI 每帧都会报同一个点）。
            if (Math.Abs(x - _x) < 0.25 && Math.Abs(y - _y) < 0.25) return;
            _x = x;
            _y = y;
            if (now - _lastTick < 8)
            {
                // 这一帧还在冷却里：记下「稍后补一帧」，免得快速划过时丢掉最后一个位置。
                if (_pendingFlush) return;
                _pendingFlush = true;
                var dispatcher = Microsoft.Maui.Controls.Application.Current?.Dispatcher;
                dispatcher?.DispatchDelayed(TimeSpan.FromMilliseconds(9), () =>
                {
                    lock (Gate) _pendingFlush = false;
                    Flush();
                });
                return;
            }
            _lastTick = now;
            copy = Listeners.ToArray();
        }
        foreach (var handler in copy)
        {
            try { handler(x, y); }
            catch (Exception ex) { App.WriteLog("PointerLight.Tick -> " + ex.Message); }
        }
    }

    private static void Flush()
    {
        double x, y;
        Action<double, double>[] copy;
        lock (Gate)
        {
            if (_x < 0) return;
            x = _x; y = _y;
            _lastTick = Environment.TickCount64;
            copy = Listeners.ToArray();
        }
        foreach (var handler in copy)
        {
            try { handler(x, y); }
            catch (Exception ex) { App.WriteLog("PointerLight.Flush -> " + ex.Message); }
        }
    }
}
