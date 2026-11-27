namespace WarmAsBefore.Helpers;

/// <summary>
/// 左缘右滑返回（iOS 的 interactive pop）：手指从屏幕左缘往里拖，页面就跟着走 1:1；
/// 拖过三成屏宽、或者松手时的甩速够快，才真的返回；否则**按当前位移与速度弹簧收敛**回原位。
///
/// 为什么感应带是一块真实存在的窄条，而不是「在页面根节点上判断按下点」：
/// MAUI 的 PanGestureRecognizer 只给累计位移，不给按下坐标 —— 想在半路判断「这一下是不是从左缘起的」
/// 根本拿不到数。所以每页在 XAML 里显式放一条 18dp 宽、贴左缘的透明条（x:Name 自己起），
/// 本类只给它挂手势。好处是：竖滑、聊天区的横向手势、列表滚动都不受影响，只有真的从左缘起手才进这条链路。
///
/// 跟手与松手的三个数（都是 iOS 上量得出来的量级，不是拍的）：
///   · 1:1 —— 位移直接等于 e.TotalX，不打折、不加阻尼；打折就会读成「页面比手指慢」。
///   · 三成 —— 半屏宽屏上约 120dp，比系统返回手势的边界略宽：这里返回的是整个页面，误触代价大。
///   · 800dp/s —— 短促快甩同样算数；停下来再松手（速度已衰减到 0）则只看位移。
/// 松手永远走 <see cref="IosMotion.Back"/> 弹簧，把「松手那一刻的速度」作为初速带进去 ——
/// 于是往回拖到一半撒手，页面是「被拉回去」，不是「啪一下跳回去」。
/// </summary>
public static class EdgeBack
{
    /// <summary>感应带宽度（dp）。再宽会挡住左栏图标与列表头像的点击。</summary>
    public const double ZoneWidth = 18;

    /// <summary>走完的距离占屏宽的比例。</summary>
    public const double CommitRatio = 0.30;

    /// <summary>甩速阈值（dp/s）。</summary>
    public const double FlingVelocity = 800;

    private sealed class Session
    {
        public required ContentPage Page;
        public required VisualElement Zone;
        public required VisualElement Root;
        public Func<bool>? CanGoBack;
        public required Func<Task> GoBack;
        public readonly DragVelocity Velocity = new();
        public bool Dragging;
        public bool Committing;

    }

    /// <summary>
    /// 挂上返回手势。<paramref name="zone"/> 是 XAML 里那条贴左缘的透明条；
    /// <paramref name="canGoBack"/> 在按下那一刻问一次（抽屉开着、没有上一页时返回 false，整条链路就不动）。
    /// </summary>
    public static void Attach(ContentPage page, View zone, Func<bool>? canGoBack, Func<Task> goBack)
    {
        if (page?.Content is not VisualElement root || zone is null || goBack is null) return;

        var s = new Session { Page = page, Zone = zone, Root = root, CanGoBack = canGoBack, GoBack = goBack };
        var pan = new PanGestureRecognizer();
        pan.PanUpdated += (_, e) => OnPan(s, e);
        zone.GestureRecognizers.Add(pan);

        // 返回后这一页可能还在栈里（换页动画期间）：再出现时必须回到原位，否则整页会偏出去。
        page.Appearing += (_, _) =>
        {
            s.Dragging = false;
            s.Committing = false;
            root.AbortAnimation("TranslateTo");
            root.TranslationX = 0;
        };
    }

    private static void OnPan(Session s, PanUpdatedEventArgs e)
    {
        try
        {
            switch (e.StatusType)
            {
                case GestureStatus.Started:
                    if (s.CanGoBack is { } gate && !gate()) return;
                    if (s.Committing) return;
                    s.Dragging = true;
                    s.Velocity.Reset(0);
                    s.Root.AbortAnimation("TranslateTo");
                    break;

                case GestureStatus.Running:
                    if (!s.Dragging) return;
                    var w = Width(s.Page);
                    // 只认往右：往左拖是「没有这个手势」，页面不能跟着往左跑。
                    var dx = Math.Clamp(e.TotalX, 0, w);
                    s.Velocity.Push(dx);
                    // 往下滑得比横着多 → 手在滚列表，这一下不算返回。
                    if (Math.Abs(e.TotalY) > Math.Abs(e.TotalX) * 1.4 && Math.Abs(e.TotalY) > 14)
                    {
                        Cancel(s);
                        return;
                    }
                    s.Root.TranslationX = dx;

                    break;

                case GestureStatus.Completed:
                case GestureStatus.Canceled:
                    if (!s.Dragging) return;
                    s.Dragging = false;
                    var width = Width(s.Page);
                    var moved = s.Root.TranslationX;
                    var speed = s.Velocity.Resolve();
                    var commit = e.StatusType == GestureStatus.Completed
                                 && (moved >= width * CommitRatio || speed >= FlingVelocity);
                    if (commit) _ = CommitAsync(s, width, speed);
                    else _ = SettleAsync(s, speed);
                    break;
            }
        }
        catch (Exception ex) { App.WriteLog("EdgeBack.OnPan -> " + ex.Message); }
    }

    /// <summary>手在纵向滚，那一下不是返回：归位，并等下一次从左缘重新起手。</summary>
    private static void Cancel(Session s)
    {
        s.Dragging = false;
        _ = SettleAsync(s, 0);
    }

    private static double Width(Page page)
    {
        var w = page.Width;
        if (w > 1) return w;
        w = page.Window?.Width ?? 0;
        return w > 1 ? w : 360;
    }

    /// <summary>没走完：带回弹的归位。速度是负的（往回），弹簧会先往回带一点再过冲收住。</summary>
    private static async Task SettleAsync(Session s, double speed)
    {
        try
        {
            await IosMotion.SlideToX(s.Root, 0, speed, IosMotion.Back);
        }
        catch (Exception ex) { App.WriteLog("EdgeBack.Settle -> " + ex.Message); }
        finally
        {
            if (!s.Dragging) s.Root.TranslationX = 0;
        }
    }

    /// <summary>
    /// 走完：滑到略过半屏（视觉上「这一页真的被推走了」），再执行返回。
    /// 滑动与导航同时发起 —— 等动画跑完再切页，中间会露出一段静止的空白屏。
    /// </summary>
    private static async Task CommitAsync(Session s, double width, double speed)
    {
        s.Committing = true;
        try
        {
            _ = IosMotion.SlideToX(s.Root, width, Math.Max(speed, 1200), IosMotion.Sheet);
            await s.GoBack();
        }
        catch (Exception ex) { App.WriteLog("EdgeBack.Commit -> " + ex.Message); }
        finally
        {
            s.Committing = false;
            // 返回失败（没有上一页）时页面还在这儿：必须立刻归位，不能停在半路。
            if (s.Page.Width > 1 && s.Root.TranslationX != 0) s.Root.TranslationX = 0;
        }
    }
}
