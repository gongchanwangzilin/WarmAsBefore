using Microsoft.Maui.Controls.Shapes;
using WarmAsBefore.Helpers;
using WarmAsBefore.Models;
using WarmAsBefore.Services;

namespace WarmAsBefore.Controls;

public enum ToastKind { Info, Success, Warning, Error }

/// <summary>
/// 轻量通知：贴在当前页最上层，从屏幕边缘滑入、停留几秒再滑回。
/// 不用 MAUI 的 DisplayAlert —— 它必须点一下才走，还挡在正中间，正在点的地方会被它吃掉。
/// 阻断性确认（删除存档、设置里的确认）仍然用原生对话框，那些本来就要用户做决定。
///
/// 2026-10-30 升级成「成就横幅」：方形徽章 + 类别眉标 + 标题 + 一句话。
/// 位置与停留时长沿用设置里的「浮动通知」两项（上/下浮现、四角滑出都在里面），
/// <see cref="TopInset"/> 由页面上报自己的顶栏高度：有顶栏就从顶栏下方出，没有就贴顶。
/// </summary>
public static class Toast
{
    /// <summary>页面顶栏高度（DIP）。默认按「有一整条顶栏」算；无顶栏页面在 OnAppearing 里改成 16。</summary>
    public static double TopInset { get; set; } = 64;

    public static void Info(string text) => Show(text, ToastKind.Info);
    public static void Success(string text) => Show(text, ToastKind.Success);
    public static void Warn(string text) => Show(text, ToastKind.Warning);
    public static void Error(string text) => Show(text, ToastKind.Error);

    /// <summary>单行快捷（沿用老签名）。</summary>
    public static void Show(string text, ToastKind kind = ToastKind.Info)
        => Show(text, "", CodeOf(kind), kind);

    /// <summary>成就横幅：类别码 + 标题 + 一句话。</summary>
    public static void Show(string title, string body, string code, ToastKind kind = ToastKind.Info, string glyph = "✦")
    {
        if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(body)) return;
        Ui.Post(() => ShowOnUi(title, body, code, kind, glyph));
    }

    private static string CodeOf(ToastKind kind) => kind switch
    {
        ToastKind.Success => "DONE",
        ToastKind.Warning => "WARN",
        ToastKind.Error => "ERROR",
        _ => "NOTICE",
    };

    private static void ShowOnUi(string title, string body, string code, ToastKind kind, string glyph)
    {
        try
        {
            var page = CurrentPage();
            if (page is null) return;
            Ensure(page).Show(title, body, code, glyph, kind, Settings());
        }
        catch (Exception ex) { App.WriteLog("Toast -> " + ex.Message); }
    }

    private static UserSettings Settings()
    {
        try
        {
            var mgr = Application.Current?.Handler?.MauiContext?.Services
                .GetService(typeof(SettingsManager)) as SettingsManager;
            return mgr?.Current ?? new UserSettings();
        }
        catch { return new UserSettings(); }
    }

    private static Page? CurrentPage()
    {
        try
        {
            var win = Application.Current?.Windows.FirstOrDefault();
            if (win?.Page is Shell shell) return shell.CurrentPage ?? win.Page;
            return win?.Page;
        }
        catch { return null; }
    }

    private static ToastHost? Ensure(Page page)
    {
        if (page is not ContentPage cp) return null;
        if (cp.Content is Grid root && root.Children.LastOrDefault() is ToastHost existing)
            return existing;

        var host = new ToastHost();
        var old = cp.Content;
        cp.Content = null;
        var wrap = new Grid();
        if (old is View v) wrap.Add(v);
        wrap.Add(host);
        cp.Content = wrap;
        return host;
    }
}

/// <summary>通知的落点。只盖在内容上、不吃点击（InputTransparent），所以不挡底下任何按键。</summary>
public sealed class ToastHost : ContentView
{
    private readonly Border _card;
    private readonly Label _code;
    private readonly Label _title;
    private readonly Label _body;
    private readonly Label _glyph;
    private readonly Border _badge;
    private CancellationTokenSource? _cts;

    public ToastHost()
    {
        InputTransparent = true;
        IsVisible = false;

        _glyph = new Label { FontSize = 15, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center };
        _glyph.SetDynamicResource(Label.TextColorProperty, "AccentWarmFg");
        _badge = new Border
        {
            WidthRequest = 34,
            HeightRequest = 34,
            StrokeThickness = 1.5,
            VerticalOptions = LayoutOptions.Center,
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            Content = _glyph,
        };
        _badge.SetDynamicResource(Border.StrokeProperty, "AccentWarmFg");

        _code = new Label();
        _code.SetDynamicResource(Label.TextColorProperty, "WarmFaint");
        _code.FontFamily = "Bahnschrift";
        _code.CharacterSpacing = 3;
        _code.FontSize = 10;

        _title = new Label { LineBreakMode = LineBreakMode.TailTruncation, MaxLines = 1 };
        _title.SetDynamicResource(Label.TextColorProperty, "WarmDark");
        _title.FontAttributes = FontAttributes.Bold;
        _title.FontSize = 14;

        _body = new Label { LineBreakMode = LineBreakMode.WordWrap, MaxLines = 2, IsVisible = false };
        _body.SetDynamicResource(Label.TextColorProperty, "MutedFg");
        _body.FontSize = 12;

        var text = new VerticalStackLayout { Spacing = 1, VerticalOptions = LayoutOptions.Center };
        text.Add(_code);
        text.Add(_title);
        text.Add(_body);

        var grid = new Grid
        {
            ColumnSpacing = 10,
            ColumnDefinitions = { new ColumnDefinition(new GridLength(34)), new ColumnDefinition(GridLength.Star) },
        };
        grid.Add(_badge, 0, 0);
        grid.Add(text, 1, 0);

        _card = new Border
        {
            Padding = new Thickness(12, 10),
            StrokeThickness = 1,
            MaximumWidthRequest = 380,
            InputTransparent = true,
            Content = grid,
        };
        _card.SetDynamicResource(Border.BackgroundColorProperty, "SurfaceBg");
        _card.SetDynamicResource(Border.StrokeProperty, "WSepBrush");
        _card.StrokeShape = new RoundRectangle { CornerRadius = 14 };
        Content = _card;
    }

    internal async void Show(string title, string body, string code, string glyph, ToastKind kind, UserSettings settings)
    {
        try
        {
            if (!settings.ToastEnabled) return;
            _cts?.Cancel();
            var cts = new CancellationTokenSource();
            _cts = cts;

            _code.Text = "// " + code;
            _title.Text = title ?? "";
            _body.Text = body ?? "";
            _body.IsVisible = !string.IsNullOrWhiteSpace(body);
            _glyph.Text = string.IsNullOrWhiteSpace(glyph) ? "✦" : glyph;
            _glyph.TextColor = kind switch
            {
                ToastKind.Success => Color.FromArgb("#2E7D32"),
                ToastKind.Warning => Color.FromArgb("#B26A00"),
                ToastKind.Error => Color.FromArgb("#B3261E"),
                _ => (Color)(Application.Current?.Resources["AccentWarmFg"] ?? Colors.Gray),
            };
            _badge.Stroke = _glyph.TextColor;

            ApplyPosition(settings.NotificationPosition);
            IsVisible = true;
            Opacity = 0;
            TranslationY = _entryShift;
            TranslationX = 0;
            await Task.WhenAll(this.FadeTo(1, 160, Easing.CubicOut), this.TranslateTo(0, 0, 200, Easing.CubicOut));
            if (cts.IsCancellationRequested) return;

            var hold = (int)(Math.Clamp(settings.NotificationSeconds, 1.5, 30.0) * 1000);
            try { await Task.Delay(hold, cts.Token); }
            catch (TaskCanceledException) { return; }
            if (cts.IsCancellationRequested) return;

            await Task.WhenAll(this.FadeTo(0, 160, Easing.CubicIn), this.TranslateTo(0, _entryShift, 190, Easing.CubicIn));
            if (!cts.IsCancellationRequested) IsVisible = false;
        }
        catch (Exception ex) { App.WriteLog("ToastHost.Show -> " + ex.Message); }
    }

    private double _entryShift = -16;

    private void ApplyPosition(string? pos)
    {
        var p = (pos ?? "top-right").ToLowerInvariant();
        var (h, v) = p switch
        {
            "top-left" => (LayoutOptions.Start, LayoutOptions.Start),
            "bottom-left" => (LayoutOptions.Start, LayoutOptions.End),
            "bottom-right" => (LayoutOptions.End, LayoutOptions.End),
            "top" => (LayoutOptions.Center, LayoutOptions.Start),
            "bottom" => (LayoutOptions.Center, LayoutOptions.End),
            "center" => (LayoutOptions.Center, LayoutOptions.Center),
            _ => (LayoutOptions.End, LayoutOptions.Start),
        };
        _card.HorizontalOptions = h;
        _card.VerticalOptions = v;
        var top = Toast.TopInset;
        _card.Margin = p switch
        {
            "top" => new Thickness(16, top, 16, 0),
            "bottom" => new Thickness(16, 0, 16, 24),
            "center" => new Thickness(16),
            "bottom-left" or "bottom-right" => new Thickness(16, 0, 16, 24),
            "top-left" => new Thickness(16, top, 0, 0),
            _ => new Thickness(0, top, 16, 0),
        };
        _entryShift = p.StartsWith("bottom") ? 16 : (p == "center" ? 0 : -16);
    }
}
