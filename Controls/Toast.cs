using WarmAsBefore.Helpers;
using WarmAsBefore.Models;
using WarmAsBefore.Services;

namespace WarmAsBefore.Controls;

public enum ToastKind { Info, Success, Warning, Error }

/// <summary>
/// 轻量通知：贴在当前页最上层，从屏幕边缘滑入、停留几秒再滑回。
/// 不用 MAUI 的 DisplayAlert —— 它必须点一下才走，还挡在正中间，正在点的地方会被它吃掉。
/// 阻断性确认（删除存档、设置里的确认）仍然用原生对话框，那些本来就要用户做决定。
/// </summary>
public static class Toast
{
    public static void Info(string text) => Show(text, ToastKind.Info);
    public static void Success(string text) => Show(text, ToastKind.Success);
    public static void Warn(string text) => Show(text, ToastKind.Warning);
    public static void Error(string text) => Show(text, ToastKind.Error);

    public static void Show(string text, ToastKind kind = ToastKind.Info)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        Ui.Post(() => ShowOnUi(text, kind));
    }

    private static void ShowOnUi(string text, ToastKind kind)
    {
        try
        {
            var page = CurrentPage();
            if (page is null) return;
            Ensure(page).Show(text, kind, Settings());
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
    private readonly Label _text;
    private readonly BoxView _dot;
    private CancellationTokenSource? _cts;

    public ToastHost()
    {
        InputTransparent = true;
        IsVisible = false;
        _dot = new BoxView { WidthRequest = 8, HeightRequest = 8, CornerRadius = new CornerRadius(4), VerticalOptions = LayoutOptions.Center };
        _text = new Label { FontSize = 12.5, LineBreakMode = LineBreakMode.WordWrap, VerticalOptions = LayoutOptions.Center };
        _text.SetDynamicResource(Label.TextColorProperty, "SurfaceFg");
        _card = new Border
        {
            Padding = new Thickness(14, 10),
            StrokeThickness = 1,
            MaximumWidthRequest = 360,
            InputTransparent = true,
            Content = new HorizontalStackLayout { Spacing = 10, Children = { _dot, _text } }
        };
        _card.SetDynamicResource(Border.BackgroundColorProperty, "SurfaceBg");
        _card.SetDynamicResource(Border.StrokeProperty, "WSepBrush");
        _card.StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 14 };
        Content = _card;
    }

    internal async void Show(string text, ToastKind kind, UserSettings settings)
    {
        try
        {
            if (!settings.ToastEnabled) return;
            _cts?.Cancel();
            var cts = new CancellationTokenSource();
            _cts = cts;

            _text.Text = text;
            _dot.Color = kind switch
            {
                ToastKind.Success => Color.FromArgb("#2E7D32"),
                ToastKind.Warning => Color.FromArgb("#B26A00"),
                ToastKind.Error => Color.FromArgb("#B3261E"),
                _ => Color.FromArgb("#6B7280"),
            };

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
        _card.Margin = p switch
        {
            "top" => new Thickness(16, 64, 16, 0),
            "bottom" => new Thickness(16, 0, 16, 24),
            "center" => new Thickness(16),
            "bottom-left" or "bottom-right" => new Thickness(16, 0, 16, 24),
            "top-left" => new Thickness(16, 64, 0, 0),
            _ => new Thickness(0, 64, 16, 0),
        };
        _entryShift = p.StartsWith("bottom") ? 16 : (p == "center" ? 0 : -16);
    }
}
