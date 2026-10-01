namespace WarmAsBefore.Services;

/// <summary>
/// 页面背景：纯色 / 自定义图片 / 窗口透视。
///
/// 这是玻璃能否「看得出是玻璃」的前提 —— 半透明表面必须有**背后可透出的内容**。
/// 压在一块纯色窗口背景上时，无论怎么调 alpha、加高光，出来都只是一块浅色板。
///
/// 做法：
///   · 所有 ContentPage 的根背景统一走 {DynamicResource PageBgBrush}（见各页面 XAML），
///     这里决定它是纯色还是不透明 → 页面层先透出去；
///   · 图片本身画在 WinUI 窗口根节点上（MAUI 的 ImageBrush 在 .NET 8+ 已不公开，绕开它）；
///   · 「窗口透视」再用 DWM 把客户区交还给合成器，连桌面都能透上来。
/// </summary>
public static class AppBackgroundService
{
    public const string PageBgBrushKey = "PageBgBrush";

    private static Brush? _current;
    private static string _imagePath = "";

    /// <summary>由 RuntimeConfigurator 在设置下发时调用。</summary>
    public static void Apply(string mode, string imagePath, Color pageBg)
    {
        var transparent = false;
        Brush pageBrush;
        var paintImage = false;

        if (mode == "image" && !string.IsNullOrWhiteSpace(imagePath) && File.Exists(imagePath))
        {
            // 页面层让开，图片画在更下面的窗口层
            pageBrush = new SolidColorBrush(Colors.Transparent);
            _imagePath = imagePath;
            paintImage = true;
            transparent = true;
        }
        else if (mode == "clear")
        {
            pageBrush = new SolidColorBrush(Colors.Transparent);
            _imagePath = "";
            transparent = true;
        }
        else
        {
            pageBrush = new SolidColorBrush(pageBg);
            _imagePath = "";
        }

        _current = pageBrush;
        _transparent = transparent;
        Publish();
        ApplyPlatform();
    }

    /// <summary>把当前背景回写到资源字典（换主题后要重放，否则会被纯色覆盖）。</summary>
    public static void Publish()
    {
        if (_current is null || Application.Current is null) return;
        Application.Current.Resources[PageBgBrushKey] = _current;
    }

    private static bool _transparent;

    private static void ApplyPlatform()
    {
        var mode = _imagePath.Length > 0 ? "image" : (_transparent ? "clear" : "none");
        WindowBackdrop.SetTransparent(_transparent);

#if WINDOWS
        MainThread.BeginInvokeOnMainThread(() =>
        {
            try
            {
                var win = Application.Current?.Windows.FirstOrDefault(w => w.Handler is not null);
                if (win?.Handler?.PlatformView is not Microsoft.UI.Xaml.Window w) return;
                if (w.Content is not Microsoft.UI.Xaml.Controls.Panel root) return;

                if (mode == "image")
                {
                    root.Background = new Microsoft.UI.Xaml.Media.ImageBrush
                    {
                        ImageSource = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(_imagePath)),
                        Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill
                    };
                }
                else if (mode == "clear")
                {
                    root.Background = null;   // 全透明 → 桌面透上来
                }
                else
                {
                    root.ClearValue(Microsoft.UI.Xaml.Controls.Panel.BackgroundProperty);
                }
            }
            catch (Exception ex)
            {
                App.WriteLog("AppBackgroundService.ApplyPlatform -> " + ex.Message);
            }
        });
#endif
    }
}
