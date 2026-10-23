using WarmAsBefore.Views;

namespace WarmAsBefore.Services;

/// <summary>
/// 把一页搬进独立窗口：关系网开一扇、搜索开一扇，主窗口继续聊天，两边同时看得见。
///
/// 桌宠那扇是无边框分层窗口（<see cref="PetService"/> 自己管的），这里要的是普通带标题栏的工具窗，
/// 所以统一包成 <see cref="ToolWindow"/> —— 既有正常窗口行为，又能让「找主窗口」的那些代码认出谁不是主窗口。
///
/// 同一扇只开一个（按 key 记账）：点两下「独立窗口」就摊出四张网，不是多窗口，是刷屏。
/// 手机上没有多窗口，调用方拿到 false 之后自己退回页面内导航，不能点了没反应。
/// </summary>
public sealed class PageWindowService
{
    private readonly Dictionary<string, Window> _open = new(StringComparer.Ordinal);

    /// <summary>这台设备能不能真的另开一扇窗（手机上的多窗口是空话，只能退回页内导航）。</summary>
    public static bool Supported => DeviceInfo.Platform == DevicePlatform.WinUI;

    /// <summary>开一扇工具窗；已经开着就把它唤到前面。返回 false = 这平台开不了。</summary>
    public bool Open(string key, string title, Type pageType,
        IDictionary<string, object>? query = null, double width = 980, double height = 700)
    {
        if (Application.Current is not { } app) return false;
        if (Exists(key))
        {
            BringToFront(_open[key]);
            return true;
        }
        if (app.Handler?.MauiContext?.Services is not { } services) return false;
        if (services.GetService(pageType) is not Page page)
        {
            App.WriteLog($"PageWindow: {pageType.Name} 没注册到 DI，开不了窗");
            return false;
        }

        var q = query is null ? new Dictionary<string, object>() : new Dictionary<string, object>(query);
        // 告诉页面「你在这回是自己一扇窗」：窗里的「←」不该去翻主窗口的导航栈，直接不显示它。
        q["inWindow"] = "1";
        // Shell 导航时页面和它的 BindingContext 都会收到参数，自己开窗也得照做：
        // 这几个页面的参数是视图模型在接的，只递给 Page 就等于谁也没接到，页面会拿当前主角去猜。
        if (page is IQueryAttributable qa) qa.ApplyQueryAttributes(q);
        if (page.BindingContext is IQueryAttributable vmq && !ReferenceEquals(vmq, page as IQueryAttributable))
            vmq.ApplyQueryAttributes(q);

        var win = new ToolWindow(page)
        {
            Title = title,
            Width = Math.Max(420, width),
            Height = Math.Max(360, height)
        };
        win.Destroying += (_, _) => _open.Remove(key);
        app.OpenWindow(win);
        _open[key] = win;
        App.WriteLog($"PageWindow: 打开 {title}（{pageType.Name}）");
        return true;
    }

    private bool Exists(string key)
        => _open.TryGetValue(key, out var w) && Application.Current?.Windows.Contains(w) == true;

    /// <summary>
    /// 唤到前面：MAUI 的 <see cref="Window"/> 没有「激活」这个动作，它在平台窗口上（桌宠找回主窗口也是这么做的）。
    /// 最小化过的窗只 Activate 不会自己弹回来，得先 Show。
    /// </summary>
    private static void BringToFront(Window w)
    {
#if WINDOWS
        if (w.Handler?.PlatformView is Microsoft.UI.Xaml.Window xw)
        {
            xw.AppWindow.Show();
            xw.Activate();
        }
#endif
    }

    /// <summary>开不了窗时的退路：走 Shell 导航，功能不缺，只是少一扇窗。</summary>
    public async Task<bool> OpenOrNavigateAsync(string key, string title, Type pageType, string route,
        IDictionary<string, object>? query = null, double width = 980, double height = 700)
    {
        if (Supported && Open(key, title, pageType, query, width, height)) return true;
        var qs = BuildQuery(route, query);
        await Shell.Current.GoToAsync(qs);
        return true;
    }

    /// <summary>把 query 拼成 Shell 的 route?a=1&amp;b=2（值全部转义，路径由调用方给死的常量）。</summary>
    private static string BuildQuery(string route, IDictionary<string, object>? query)
    {
        if (query is null || query.Count == 0) return route;
        var parts = query.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value?.ToString() ?? "")}");
        return route + "?" + string.Join("&", parts);
    }

    /// <summary>关掉某一扇（页面自己要求收起时用）。</summary>
    public void Close(string key)
    {
        if (!_open.TryGetValue(key, out var w)) return;
        _open.Remove(key);
        if (Application.Current is { } app && app.Windows.Contains(w)) app.CloseWindow(w);
    }
}

/// <summary>
/// 工具窗的记账键，同时也是 Shell 路由名（<c>AppShell</c> 里注册的那两个）。
/// 两处必须同一个字符串：键和路由各写一份，就会变成「点两下开两扇」和「手机上跳不到页」。
/// </summary>
public static class PageWindowKeys
{
    public const string Graph = "memoryGraph";
    public const string Search = "memorySearch";
}
