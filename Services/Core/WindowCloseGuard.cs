#if WINDOWS
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace WarmAsBefore.Services;

/// <summary>
/// 关主窗口时先问一句「其他窗口怎么办」。
///
/// 为什么不用 MAUI 的 <see cref="Microsoft.Maui.Controls.Window.Destroying"/>：它在窗口已经关掉之后才触发，拦不住；
/// WinUI 的 <c>AppWindow.Closing</c> 才能 <c>Cancel</c>。MAUI 自己只挂了 WinUI 的 <c>Closed</c>，
/// 所以在这里取消不会提前触发自动保存/桌宠收尾 —— 真正关的时候照常走 Destroying。
///
/// 选择可记住（closeOthers / keepOthers），之后在设置里改回 ask 就会重新询问。
/// 取消不是一个可记住的选项：记住「取消」等于以后关不掉窗口。
/// </summary>
public static class WindowCloseGuard
{
    private static Microsoft.Maui.Controls.Window? _main;
    private static Microsoft.UI.Xaml.Window? _platform;
    private static bool _allowClose;
    private static bool _dialogOpen;

    public static void Attach(Microsoft.Maui.Controls.Window main, Microsoft.UI.Xaml.Window platform)
    {
        if (_platform is not null) return;
        _main = main;
        _platform = platform;
        platform.AppWindow.Closing += OnClosing;
    }

    private static void OnClosing(Microsoft.UI.Windowing.AppWindow sender,
        Microsoft.UI.Windowing.AppWindowClosingEventArgs args)
    {
        if (_allowClose) return;
        try
        {
            var app = Microsoft.Maui.Controls.Application.Current;
            if (app is null || _main is null) return;
            var others = app.Windows.Where(w => !ReferenceEquals(w, _main)).ToList();
            if (others.Count == 0) return;   // 只有主窗口：没什么可问的

            var settings = app.Handler?.MauiContext?.Services?
                .GetService(typeof(SettingsManager)) as SettingsManager;
            var mode = settings?.Current.MainWindowCloseBehavior ?? "ask";
            if (mode == "closeOthers") { CloseOthers(app); return; }
            if (mode == "keepOthers") return;

            // 先拦下这一下，问完再决定要不要真的关
            args.Cancel = true;
            if (_dialogOpen) return;
            _dialogOpen = true;
            _ = AskAsync(others.Count, settings);
        }
        catch (Exception ex)
        {
            App.WriteLog("WindowCloseGuard.OnClosing -> " + ex);
        }
    }

    private static async Task AskAsync(int count, SettingsManager? settings)
    {
        try
        {
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                if (_platform?.Content?.XamlRoot is not { } root) return;

                var remember = new Microsoft.UI.Xaml.Controls.CheckBox
                {
                    Content = "记住这次选择（可在「设置 → 窗口与多开」里改）",
                    Margin = new Microsoft.UI.Xaml.Thickness(0, 14, 0, 0),
                    IsChecked = false
                };
                var body = new StackPanel { Spacing = 4 };
                body.Children.Add(new TextBlock
                {
                    Text = $"还开着 {count} 个窗口（手机、关系网、搜事件……）。关掉主窗口时，它们要一起关吗？",
                    TextWrapping = TextWrapping.Wrap
                });
                body.Children.Add(new TextBlock
                {
                    Text = "选「取消」则什么都不关。",
                    Opacity = 0.7,
                    FontSize = 12
                });
                body.Children.Add(remember);

                var dialog = new ContentDialog
                {
                    Title = "关闭主窗口",
                    Content = body,
                    PrimaryButtonText = "连其他窗口一起关",
                    SecondaryButtonText = "只关主窗口",
                    CloseButtonText = "取消",
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = root
                };

                var result = await dialog.ShowAsync();
                if (result == ContentDialogResult.None) return;

                var key = result == ContentDialogResult.Primary ? "closeOthers" : "keepOthers";
                if (remember.IsChecked == true && settings is not null)
                {
                    settings.Apply(settings.Current with { MainWindowCloseBehavior = key });
                    await settings.Persist();
                }

                var app = Microsoft.Maui.Controls.Application.Current;
                if (app is null) return;
                if (result == ContentDialogResult.Primary) CloseOthers(app);
                ConfirmClose();
            });
        }
        catch (Exception ex)
        {
            // 另一个对话框正开着时 ShowAsync 会抛：不关窗口是最安全的落点
            App.WriteLog("WindowCloseGuard.Ask -> " + ex);
        }
        finally
        {
            _dialogOpen = false;
        }
    }

    private static void CloseOthers(Microsoft.Maui.Controls.Application app)
    {
        foreach (var w in app.Windows.Where(w => !ReferenceEquals(w, _main)).ToList())
        {
            try { app.CloseWindow(w); }
            catch (Exception ex) { App.WriteLog("WindowCloseGuard.CloseOthers -> " + ex.Message); }
        }
    }

    private static void ConfirmClose()
    {
        _allowClose = true;
        try { _platform?.Close(); }
        catch (Exception ex)
        {
            _allowClose = false;
            App.WriteLog("WindowCloseGuard.ConfirmClose -> " + ex);
        }
    }
}
#endif
