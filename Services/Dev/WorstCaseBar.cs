using System;
using System.Collections.Generic;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;

namespace WarmAsBefore.Services.Dev;

/// <summary>
/// 压测条：Demo / Worst / Empty / One / Huge 五段，一点就换档并让当前页重读数据。
///
/// 只在这条命令启动时才存在（<c>--worst=…</c> / <c>WAB_WORST</c>，且仅 Debug）：
/// 没带参数时它 <see cref="IsVisible"/> 为假、宽高为 0，正式构建里连一粒像素都不占。
/// 它是压测的脚手架而不是被测设计，所以刻意长得像普通开发工具：灰底、系统字体、无动画。
/// </summary>
public sealed class WorstCaseBar : ContentView
{
    private static readonly Color TrackBg = Color.FromArgb("#E62B2B2B");
    private static readonly Color TrackStroke = Color.FromArgb("#66000000");
    private static readonly Color IdleInk = Color.FromArgb("#DDDDDD");
    private static readonly Color PillBg = Color.FromArgb("#FFF7F3EC");
    private static readonly Color PillInk = Color.FromArgb("#2B2B2B");

    // 弱引用登记：页面是 transient，来回导航会造出很多条压测条；换档只用叫醒还活着的那几条。
    private static readonly List<WeakReference<WorstCaseBar>> Live = new();

    static WorstCaseBar() => WorstCaseProvider.ModeChanged += RefreshAll;

    private readonly Dictionary<WorstCaseMode, (Border Pill, Label Text)> _segments = new();
    private bool _built;

    public WorstCaseBar()
    {
        IsVisible = WorstCaseProvider.EnabledAtLaunch;
        if (!IsVisible)
        {
            HeightRequest = 0;
            WidthRequest = 0;
            return;
        }
        HorizontalOptions = LayoutOptions.Center;
        VerticalOptions = LayoutOptions.End;
        Margin = new Thickness(0, 0, 0, 6);
        Live.Add(new WeakReference<WorstCaseBar>(this));
    }

    /// <summary>竖排：微信左栏只有 64 宽，横排放不下五段。</summary>
    public bool Vertical { get; set; }

    protected override void OnParentSet()
    {
        base.OnParentSet();
        if (!IsVisible || _built) return;
        Build();
    }

    private void Build()
    {
        _built = true;
        Layout stack = Vertical
            ? new VerticalStackLayout { Spacing = 2 }
            : new HorizontalStackLayout { Spacing = 2 };

        foreach (var mode in WorstCaseProvider.AllModes)
        {
            var text = new Label
            {
                Text = WorstCaseProvider.Name(mode),
                FontSize = 10,
                TextColor = IdleInk,
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center
            };
            var pill = new Border
            {
                Padding = Vertical ? new Thickness(4, 2) : new Thickness(9, 2),
                StrokeThickness = 0,
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(7) },
                BackgroundColor = Colors.Transparent,
                Content = text,
                HorizontalOptions = Vertical ? LayoutOptions.Fill : LayoutOptions.Center,
                MinimumWidthRequest = Vertical ? 48 : 0
            };
            var tap = new TapGestureRecognizer();
            var captured = mode;
            tap.Tapped += (_, _) => Choose(captured);
            pill.GestureRecognizers.Add(tap);

            _segments[mode] = (pill, text);
            stack.Add(pill);
        }

        Content = new Border
        {
            BackgroundColor = TrackBg,
            StrokeThickness = 1,
            Stroke = new SolidColorBrush(TrackStroke),
            Padding = new Thickness(3),
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(10) },
            Content = stack
        };
        Refresh();
    }

    private void Choose(WorstCaseMode mode)
    {
        if (WorstCaseProvider.Mode == mode) return;
        WorstCaseProvider.SetMode(mode);
    }

    private void Refresh()
    {
        foreach (var (mode, (pill, text)) in _segments)
        {
            var active = WorstCaseProvider.Mode == mode;
            pill.BackgroundColor = active ? PillBg : Colors.Transparent;
            text.TextColor = active ? PillInk : IdleInk;
            text.FontAttributes = active ? FontAttributes.Bold : FontAttributes.None;
        }
    }

    private static void RefreshAll()
    {
        void Run()
        {
            foreach (var weak in Live.ToArray())
            {
                if (!weak.TryGetTarget(out var bar)) { Live.Remove(weak); continue; }
                bar.Refresh();
                if (bar.HostPage()?.BindingContext is IWorstCaseAware aware) aware.ReloadWorstCase();
            }
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null) Run();
        else dispatcher.Dispatch(Run);
    }

    private Page? HostPage()
    {
        Element? node = this;
        while (node is not null and not Page) node = node.Parent;
        return node as Page;
    }
}
