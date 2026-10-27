namespace WarmAsBefore.Helpers;

/// <summary>
/// 从任何线程把工作送回 UI 线程。
///
/// 不用 <see cref="MainThread.BeginInvokeOnMainThread"/>：它在 Windows 上找主线程的顺序是
/// 「当前线程的 DispatcherQueue ?? 当前活动窗口的 DispatcherQueue」，线程池回调、托盘消息线程上
/// 两者都可能取不到，然后直接抛 InvalidOperationException —— 这是进程级崩溃（实测：置顶定时器
/// 在切到设置页后把整个程序带崩，栈顶就是 MainThread.PlatformBeginInvokeOnMainThread）。
/// Application 的 Dispatcher 自应用创建起就固定存在；只有进程正在退出时才会取不到，
/// 那一刻没有界面值得更新，安静丢掉即可。
/// </summary>
public static class Ui
{
    public static void Post(Action action)
    {
        if (action is null) return;
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null) return;
        // 已经在 UI 线程上就同步执行：改回 MainThread 之前的行为，不在事件处理里多插一层排队。
        if (!dispatcher.IsDispatchRequired) { action(); return; }
        try { dispatcher.Dispatch(action); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine("Ui.Post -> " + ex.Message); }
    }
}
