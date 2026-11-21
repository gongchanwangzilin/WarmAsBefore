namespace WarmAsBefore.Helpers;

/// <summary>
/// 事件处理器的异常兜底。
///
/// 为什么需要：XAML 里的事件处理器只能是 <c>async void</c>，它没有调用方能接住异常——
/// await 之后的任何一次抛出都会直接落到 UI 线程，轻则页面半死，重则进程退出。
/// 页面级处理器统一走这里：异常记日志、给用户一句能懂的话，界面继续可用。
/// </summary>
internal static class UiGuard
{
    public static async void Run(Func<Task> action, string tag, string? userMessage = null)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            App.WriteLog($"{tag} -> {ex}");
            if (!string.IsNullOrEmpty(userMessage))
            {
                try { Controls.Toast.Error(userMessage!); } catch { /* 提示本身失败不该再抛 */ }
            }
        }
    }
}
