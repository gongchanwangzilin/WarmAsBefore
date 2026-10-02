#if WINDOWS
using System.Runtime.InteropServices;

namespace WarmAsBefore;

public static class Program
{
    // FirstChance 异常处理器重入门闩：任何时刻只允许一层活跃日志，防无限递归
    private static int _firstChanceActive;

    [DllImport("Microsoft.ui.xaml.dll")]
    private static extern void XamlCheckProcessRequirements();

    [STAThread]
    static void Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            App.WriteLog("UnhandledException -> " + e.ExceptionObject);
        // 注意：FirstChance 处理器内任何抛出的异常都会再次触发 FirstChance，导致无限递归直至栈溢出(0xc00000fd)。
        // 必须用互斥门闩保证重入时立即返回，处理器本体绝不能做可能抛异常的操作。
        AppDomain.CurrentDomain.FirstChanceException += (s, e) =>
        {
            if (Interlocked.CompareExchange(ref _firstChanceActive, 1, 0) != 0) return;
            try { App.WriteLog("FirstChance -> " + e.Exception.Message); }
            catch { }
            finally { Interlocked.Exchange(ref _firstChanceActive, 0); }
        };
        TaskScheduler.UnobservedTaskException += (s, e) =>
            App.WriteLog("UnobservedTask -> " + e.Exception);

        App.WriteLog("Program.Main: start");
        XamlCheckProcessRequirements();
        WinRT.ComWrappersSupport.InitializeComWrappers();
        Microsoft.UI.Xaml.Application.Start(p =>
        {
            App.WriteLog("Program.Main: Application.Start callback");
            SynchronizationContext.SetSynchronizationContext(
                new Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(
                    Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()));
            WarmAsBefore.WinUI.App app = new();
            App.WriteLog("Program.Main: app instance created");
        });
        App.WriteLog("Program.Main: Start returned");
    }
}
#endif