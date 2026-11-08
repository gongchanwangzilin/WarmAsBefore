using WarmAsBefore.Controls;
using WarmAsBefore.Services;

namespace WarmAsBefore.Views;

/// <summary>
/// 应用内插件页面：Windows 上就是 WebView2 内核，页面由插件中心的本机回环服务器提供。
/// 只负责显示，不碰设置——页面自己的保存按钮照旧走 callback 写回。
/// </summary>
[QueryProperty(nameof(PageUrl), "url")]
[QueryProperty(nameof(PageTitle), "title")]
public partial class PluginWebPage : ContentPage
{
    private readonly PluginHost _host;
    private bool _loaded;

    public PluginWebPage(PluginHost host)
    {
        InitializeComponent();
        _host = host;
    }

    public string PageUrl { get; set; } = "";
    public string PageTitle { get; set; } = "";

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (_loaded) return;
        _loaded = true;
        Helpers.Motion.ArrivePage(this);

        var title = Uri.UnescapeDataString(PageTitle ?? "");
        TitleLabel.Text = string.IsNullOrWhiteSpace(title) ? "插件页面" : title;
        var url = Uri.UnescapeDataString(PageUrl ?? "");
        if (string.IsNullOrWhiteSpace(url))
        {
            HintLabel.Text = "没拿到页面地址。";
            return;
        }
        Browser.Navigated += OnNavigated;
        Browser.Source = url;
    }

    private void OnNavigated(object? sender, WebNavigatedEventArgs e)
    {
        if (e.Result == WebNavigationResult.Success)
        {
            HintLabel.IsVisible = false;
            return;
        }
        HintLabel.IsVisible = true;
        HintLabel.Text = "页面没打开（" + e.Result + "）。可以点右上角改用系统浏览器。";
    }

    private async void OnBackClicked(object sender, EventArgs e)
        => await Shell.Current.GoToAsync("..");

    private void OnReloadClicked(object sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(Browser.Source?.ToString())) return;
        HintLabel.IsVisible = true;
        HintLabel.Text = "页面加载中…";
        Browser.Reload();
    }

    private async void OnOpenBrowserClicked(object sender, EventArgs e)
    {
        var url = Uri.UnescapeDataString(PageUrl ?? "");
        var (ok, message) = await _host.OpenInBrowserAsync(url);
        if (!ok) Toast.Error(message);
    }
}
