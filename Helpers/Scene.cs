using WarmAsBefore.DesignSystem.Theme;

namespace WarmAsBefore.Helpers;

/// <summary>
/// 一块**同步时间线**：一次交互里所有会动的东西共用这一个钟。
///
/// 为什么需要它：MAUI 的 ScaleTo / TranslateTo / FadeTo 各有各的定时器，各起各的时钟。
/// 于是「按下按钮」这一下里，按钮自己开始位移、背景自己开始换色，两条动画的起点差一帧到几帧，
/// 眼睛读到的是「先动一下，再动一下」——就是我们说的「一卡一卡的」。
/// 这里改成：**发起方开一条通道，所有参与者订阅这条通道**，每一帧大家一起被推进同一个进度值，
/// 于是按钮往右移与背景变暗永远是同一拍。
///
/// 截停：<see cref="Play"/> 在同一条通道上再次调用时，从**当前值**继续算新的目标，
/// 不从 0 重来。手按住不放、松开、再按、切页打断——每一次都接着上一笔走，
/// 所以中途不会有任何回弹到起点的硬折（那才是真的「卡」）。
///
/// 时钟：一个全局 16ms 定时器，只在有通道在跑时存在；全部跑完自动停表，
/// 静止的界面不烧一次 tick。性能模式 / 系统「减少动画」(<see cref="MotionTokens.Disabled"/>)
/// 下时长归零，通道直接把值落到终点，一帧都不跑。
/// </summary>
public static class Scene
{
    private sealed class Channel
    {
        public double Value;
        public double From;
        public double To;
        public long Start;
        public int Ms;
        public Func<double, double> Ease = MotionTokens.Settle;
        public Action<double>? Tick;
        public Action? Done;
        public bool Active;
    }

    private static readonly Dictionary<string, Channel> Channels = new();
    private static readonly Dictionary<string, List<Action<double>>> Listeners = new();
    private static bool _ticking;

    /// <summary>按压通道：按钮、开关、滑杆，以及跟着一起呼吸的背景，共用这一条。</summary>
    public const string Press = "press";

    /// <summary>进页面通道：整页淡入 / 上浮与背景归位共用这一条。</summary>
    public const string Enter = "enter";

    /// <summary>
    /// 把一条通道推进到 1。<paramref name="onTick"/> 每帧收到**缓动后**的进度（0..1，
    /// 弹簧曲线会有轻微过冲，这是末段那一下「落到位」的手感）。
    /// 通道已在跑时：从它当前的值接着走，等于「新动作接管旧动作」。
    /// </summary>
    public static void Play(string key, int ms, Action<double> onTick, Action? onDone = null,
        Func<double, double>? ease = null)
    {
        var ch = Get(key);
        var from = ch.Active ? ch.Value : ch.Value;
        Begin(ch, from, 1, key, ms, onTick, onDone, ease);
    }

    /// <summary>松开 / 归零：同一套接管规则，只是目标换成 0。</summary>
    public static void Release(string key, int ms, Action<double>? onTick = null, Action? onDone = null,
        Func<double, double>? ease = null)
    {
        var ch = Get(key);
        Begin(ch, ch.Value, 0, key, ms, onTick, onDone, ease);
    }

    /// <summary>当前进度（没有在跑时是最后一次的终值）。</summary>
    public static double Value(string key)
    {
        lock (Channels) return Channels.TryGetValue(key, out var ch) ? ch.Value : 0;
    }

    public static bool Running(string key)
    {
        lock (Channels) return Channels.TryGetValue(key, out var ch) && ch.Active;
    }

    /// <summary>
    /// 订阅一条通道。写在订阅方自己的生命周期里（页面 Appearing / 控件 Loaded），
    /// 别忘了解除——一条通道的订阅者会一直被推进，页面已经走了还去动它的元素只会变慢。
    /// </summary>
    public static void Listen(string key, Action<double> handler)
    {
        lock (Listeners)
        {
            if (!Listeners.TryGetValue(key, out var list)) Listeners[key] = list = new List<Action<double>>();
            if (!list.Contains(handler)) list.Add(handler);
        }
        // 迟到的订阅者也要落在当前状态上，否则它会在通道下一次 Tick 之前停在旧值。
        var now = Value(key);
        if (now > 0.0005) Safe(handler, now);
    }

    public static void Unlisten(string key, Action<double> handler)
    {
        lock (Listeners)
        {
            if (Listeners.TryGetValue(key, out var list)) list.Remove(handler);
        }
    }

    private static Channel Get(string key)
    {
        lock (Channels)
        {
            if (!Channels.TryGetValue(key, out var ch)) Channels[key] = ch = new Channel();
            return ch;
        }
    }

    private static void Begin(Channel ch, double from, double to, string key, int ms,
        Action<double>? onTick, Action? onDone, Func<double, double>? ease)
    {
        if (onTick is not null) ch.Tick = onTick;
        if (onDone is not null) ch.Done = onDone;
        ch.Ease = ease ?? MotionTokens.Settle;
        ch.From = from;
        ch.To = to;

        var duration = MotionTokens.Disabled ? 0 : MotionTokens.Ms(ms);
        if (duration <= 0)
        {
            ch.Value = to;
            ch.Active = false;
            Safe(ch.Tick, to);
            Broadcast(key, to);
            var done = ch.Done;
            ch.Done = null;
            done?.Invoke();
            return;
        }

        ch.Ms = duration;
        ch.Start = Environment.TickCount64;
        ch.Active = true;
        Start();
    }

    private static void Start()
    {
        if (_ticking) return;
        _ticking = true;
        Application.Current?.Dispatcher.StartTimer(TimeSpan.FromMilliseconds(16), Step);
    }

    /// <summary>一帧：推进所有活着的通道，广播给订阅者；没有活通道了就停表。</summary>
    private static bool Step()
    {
        List<(string Key, double Value, bool Done)>? finished = null;
        List<(string Key, double Value)>? live = null;
        lock (Channels)
        {
            var now = Environment.TickCount64;
            foreach (var (key, ch) in Channels)
            {
                if (!ch.Active) continue;
                var t = ch.Ms <= 0 ? 1.0 : Math.Clamp((now - ch.Start) / (double)ch.Ms, 0, 1);
                var eased = ch.Ease(t);
                ch.Value = ch.From + (ch.To - ch.From) * eased;
                if (t >= 1)
                {
                    ch.Value = ch.To;
                    ch.Active = false;
                    (finished ??= new()).Add((key, ch.Value, true));
                }
                else
                {
                    (live ??= new()).Add((key, ch.Value));
                }
            }
        }

        if (live is not null)
        {
            foreach (var (key, v) in live)
            {
                Safe(Get(key).Tick, v);
                Broadcast(key, v);
            }
        }

        if (finished is not null)
        {
            foreach (var (key, v, _) in finished)
            {
                var ch = Get(key);
                Safe(ch.Tick, v);
                Broadcast(key, v);
                var done = ch.Done;
                ch.Done = null;
                done?.Invoke();
            }
        }

        var any = false;
        lock (Channels)
        {
            foreach (var ch in Channels.Values)
                if (ch.Active) { any = true; break; }
        }
        _ticking = any;
        return any;
    }

    private static void Broadcast(string key, double value)
    {
        Action<double>[] copy;
        lock (Listeners)
        {
            if (!Listeners.TryGetValue(key, out var list) || list.Count == 0) return;
            copy = list.ToArray();
        }
        foreach (var handler in copy) Safe(handler, value);
    }

    private static void Safe(Action<double>? handler, double value)
    {
        if (handler is null) return;
        try { handler(value); }
        catch (Exception ex) { App.WriteLog("Scene.Tick(" + value.ToString("F2") + ") -> " + ex.Message); }
    }
}
