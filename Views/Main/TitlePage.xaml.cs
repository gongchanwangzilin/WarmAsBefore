using WarmAsBefore.DesignSystem.Theme;
using WarmAsBefore.Helpers;
using WarmAsBefore.Services;
using WarmAsBefore.ViewModels;

namespace WarmAsBefore.Views;

public partial class TitlePage : ContentPage
{
    public TitlePage(TitleViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is TitleViewModel vm)
            vm.RefreshStateCommand.Execute(null);

        // 启动卡排在进场之前：遮罩盖着的时候底下那四段该已经摆好了，
        // 卡收掉时读到的是「界面本来就在这儿」，而不是界面正在身后现搭。
        BuildMark();
        BuildName();
        IntroCard.Play(
            new IntroCard.Parts(IntroCurtain, IntroBand, IntroRule, IntroLockup, IntroNameRow, IntroHint,
                IntroMark.Children.OfType<VisualElement>().ToList(),
                IntroNameRow.Children.OfType<VisualElement>().ToList()),
            () =>
            {
                if (BindingContext is not TitleViewModel v) return;
                v.ShowIntro = false;
                // 卡一收就交给配置：首跑的人不该先看见标题页再被跳走一次。
                v.OpenSetupIfPending();
            });

        // 四段按同一节奏摆上来：抬头 → 入口列表 → 主行动 → 带底签名。
        // 走全站唯一的进场入口（见 Motion.Arrive）：起点、步长、只演一次都由它管，
        // 页面这里只负责交出「有哪些带」。
        Motion.Arrive(BrandBand, MenuStack, HeroBand, RailFoot);

        Root.SizeChanged += (_, _) => PlaceRing();
        HookKeys();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        // 键挂在窗口根上，不属于这一页：不摘掉的话，人已经进了主界面，
        // 方向键还在替一页看不见的标题页挪光标（e.Handled 也会把键吃掉）。
        UnhookKeys();
        SetCaret(-1, -1);
    }

    /// <summary>点卡片任意处收掉：等不及的人要的是「进去」，不是「看完整版」。</summary>
    private void OnIntroTapped(object? sender, TappedEventArgs e) => IntroCard.Skip();

    /// <summary>同住体验腿上的第三条路：直通桌宠设置台（petsettings 路由，仅桌面端可用）。</summary>
    private async void OnPetSettingsTapped(object? sender, EventArgs e)
    {
        try { await Shell.Current.GoToAsync("petsettings"); }
        catch (Exception ex) { App.WriteLog("TitlePage.PetSettings -> " + ex.Message); }
    }

    /// <summary>
    /// 名牌按当前语言的 Brand_Mark 逐字建出来。一个 Label 一个字是必需的：逐字揭示要的是
    /// 每个字有自己的透明度，而 Label 内部 Span 没有这个可调量。
    /// 起始态一律 Opacity=0（不是靠时间轴的第 0 帧才归零）——「动画还没起跑」的那一帧也得被盖住。
    /// </summary>
    private void BuildMark()
    {
        IntroMark.Children.Clear();
        var brand = LocalizationService.Current["Brand_Mark"];
        var size = Application.Current?.Resources.TryGetValue("WSizeBrand", out var v) == true && v is double d ? d : 40;
        IntroMark.Spacing = Math.Round(size * 0.16);
        foreach (var ch in brand)
        {
            var glyph = new Label { Text = ch.ToString(), FontAttributes = FontAttributes.Bold, Opacity = 0 };
            glyph.SetDynamicResource(Label.FontSizeProperty, "WSizeBrand");
            glyph.SetDynamicResource(Label.TextColorProperty, "WarmDark");
            IntroMark.Children.Add(glyph);
        }
    }

    /// <summary>
    /// 本地化名同样逐字建，但用途不同：这里要的是每个字能被单独平移（收字距），
    /// 淡入则整行一起做（Opacity 落在 IntroNameRow 这个容器上，所以字本身一律 1）。
    /// 空格得自己给宽度：一个只含空白的 Label 在 WinUI 上量出来是 0 宽，
    /// 「Warm As Before」会直接挤成「WarmAsBefore」—— 按字号补一个词宽（0.30 em）。
    /// </summary>
    private void BuildName()
    {
        IntroNameRow.Children.Clear();
        var size = Application.Current?.Resources.TryGetValue("WSizeDisplay", out var v) == true && v is double d ? d : 32;
        foreach (var ch in LocalizationService.Current["Brand_Name"])
        {
            var space = char.IsWhiteSpace(ch);
            var glyph = new Label
            {
                Text = space ? "" : ch.ToString(),
                WidthRequest = space ? Math.Round(size * 0.30) : -1,
                Opacity = 1
            };
            if (!space)
            {
                glyph.SetDynamicResource(Label.FontSizeProperty, "WSizeDisplay");
                glyph.SetDynamicResource(Label.TextColorProperty, "WarmBody");
            }
            IntroNameRow.Children.Add(glyph);
        }
    }

    // ============ 科技风操作模式：方向键选 + Enter 确认 ============
    // 版式、颜色、字号一律没动 —— 改的只是「怎么选」。
    // MAUI 的 ContentPage 没有 KeyDown，按项目已有做法（见 MainGamePage.HookDesktopArrowKeys）
    // 在 WinUI 窗口根上挂；不同的是这里在离开时摘掉，不让一页把键占死。
    // 光标只在按过方向键之后才现身，所以鼠标用户看到的还是原来那一屏。

    private List<List<NavTarget>>? _cols;
    private int _col = -1;
    private int _row;
    private int _lastRailRow;

    private sealed record NavTarget(VisualElement Element, Action Run);

    private void EnsureTargets()
    {
        if (_cols is not null || BindingContext is not TitleViewModel vm) return;
        _cols = new List<List<NavTarget>>
        {
            new()
            {
                new(RowSave, () => vm.OpenSaveCommand.Execute(null)),
                new(RowNovels, () => vm.OpenNovelsCommand.Execute(null)),
                new(RowRoster, () => vm.OpenRosterCommand.Execute(null)),
                new(RowSettings, () => vm.OpenSettingsCommand.Execute(null)),
                new(ExitButton, () => vm.ExitGameCommand.Execute(null))
            },
            new() { new(EnterButton, () => vm.StartGameCommand.Execute(null)) }
        };
    }

    /// <summary>选中这一格；越界一律夹住不回绕 —— 回绕的指针在人眼里是「怎么又跳回去了」。</summary>
    private void MoveCaret(int col, int row)
    {
        EnsureTargets();
        if (_cols is null) return;
        _col = Math.Clamp(col, 0, _cols.Count - 1);
        var count = _cols[_col].Count;
        if (_col == 0)
        {
            _row = Math.Clamp(row < 0 ? _lastRailRow : row, 0, count - 1);
            _lastRailRow = _row;
        }
        else
        {
            _row = Math.Clamp(row < 0 ? _row : row, 0, count - 1);
        }
        PlaceRing();
    }

    private void SetCaret(int col, int row)
    {
        _col = col;
        _row = row;
        FocusRing.IsVisible = false;
    }

    /// <summary>
    /// 把圈摆到当前那一格外面，四周各让出 4px。位置沿父链一路加 Bounds 量出来：
    /// 平台那套「取元素屏幕坐标」的 API 在 MAUI 各版本间换过名字，而 Bounds 每版都跟着排版走。
    /// 尺寸直接给（这一层是个空心的 Border，重排它不牵动别人），位移走动画 ——
    /// 光标一跳会读成「界面自己动了」，滑过去才读成「是我在移动选择」。
    /// </summary>
    private void PlaceRing()
    {
        var target = CurrentTarget();
        if (target is null || _col < 0) { FocusRing.IsVisible = false; return; }
        var (x, y) = OffsetIn(target.Element, Root);
        FocusRing.WidthRequest = Math.Max(24, target.Element.Width + 8);
        FocusRing.HeightRequest = Math.Max(20, target.Element.Height + 8);
        var toX = Math.Max(0, x - 4);
        var toY = Math.Max(0, y - 4);
        if (!FocusRing.IsVisible)
        {
            FocusRing.TranslationX = toX;
            FocusRing.TranslationY = toY;
            FocusRing.IsVisible = true;
            return;
        }
        var ms = MotionTokens.Ms(130);
        if (ms <= 0)
        {
            FocusRing.TranslationX = toX;
            FocusRing.TranslationY = toY;
            return;
        }
        _ = FocusRing.TranslateTo(toX, toY, (uint)ms, Easing.CubicOut);
    }

    private NavTarget? CurrentTarget() =>
        _cols is not null && _col >= 0 && _col < _cols.Count && _row >= 0 && _row < _cols[_col].Count
            ? _cols[_col][_row]
            : null;

    private static (double X, double Y) OffsetIn(VisualElement v, VisualElement ancestor)
    {
        double x = 0, y = 0;
        for (VisualElement? c = v; c is not null && !ReferenceEquals(c, ancestor); c = c.Parent as VisualElement)
        {
            x += c.Bounds.X;
            y += c.Bounds.Y;
        }
        return (x, y);
    }

    /// <summary>
    /// 平台无关的按键解释，返回 true 表示这一键已经被吃掉。
    /// 两层挡在前面：启动卡（此时底下还看不见，任何键都先去收卡）、条款（模态，不替它选人）。
    /// </summary>
    private bool OnKey(TerminalKey key)
    {
        if (BindingContext is not TitleViewModel vm) return false;
        if (IntroCurtain.IsVisible && key != TerminalKey.None)
        {
            IntroCard.Skip();
            return true;
        }
        if (vm.ShowDisclaimer) return false;

        switch (key)
        {
            case TerminalKey.Up: MoveCaret(_col < 0 ? 0 : _col, _row - 1); return true;
            case TerminalKey.Down: MoveCaret(_col < 0 ? 0 : _col, _row + 1); return true;
            case TerminalKey.Left: MoveCaret(0, _col == 1 ? -1 : _row - 1); return true;
            case TerminalKey.Right: MoveCaret(1, _col == 1 ? _row : -1); return true;
            case TerminalKey.Enter:
                if (CurrentTarget() is not { } t) return false;
                t.Run();
                return true;
            case TerminalKey.Escape:
                SetCaret(-1, -1);
                return true;
            default:
                return false;
        }
    }

#if WINDOWS
    private Microsoft.UI.Xaml.UIElement? _keySink;
    private Microsoft.UI.Xaml.Input.KeyEventHandler? _keyHandler;

    private void HookKeys()
    {
        if (_keySink is not null) return;
        try
        {
            if (Application.Current?.Windows.Count > 0
                && Application.Current.Windows[0].Handler?.PlatformView is Microsoft.UI.Xaml.Window w
                && w.Content is Microsoft.UI.Xaml.UIElement root)
            {
                _keyHandler = (_, e) =>
                {
                    if (OnKey(Map(e.Key))) e.Handled = true;
                };
                _keySink = root;
                root.KeyDown += _keyHandler;
            }
        }
        catch (Exception ex) { App.WriteLog("Title.HookKeys -> " + ex); }
    }

    private void UnhookKeys()
    {
        try
        {
            if (_keySink is not null && _keyHandler is not null) _keySink.KeyDown -= _keyHandler;
        }
        catch (Exception ex) { App.WriteLog("Title.UnhookKeys -> " + ex); }
        _keySink = null;
        _keyHandler = null;
    }

    private static TerminalKey Map(Windows.System.VirtualKey k) => k switch
    {
        Windows.System.VirtualKey.Up or Windows.System.VirtualKey.K => TerminalKey.Up,
        Windows.System.VirtualKey.Down or Windows.System.VirtualKey.J => TerminalKey.Down,
        Windows.System.VirtualKey.Left or Windows.System.VirtualKey.H => TerminalKey.Left,
        Windows.System.VirtualKey.Right or Windows.System.VirtualKey.L => TerminalKey.Right,
        Windows.System.VirtualKey.Enter or Windows.System.VirtualKey.Space => TerminalKey.Enter,
        Windows.System.VirtualKey.Escape => TerminalKey.Escape,
        _ => TerminalKey.None
    };
#else
    private void HookKeys() { }
    private void UnhookKeys() { }
#endif
}

/// <summary>按键到「这一屏听得懂的动作」的翻译：平台层只负责报键名，含义归页面。</summary>
internal enum TerminalKey
{
    None,
    Up,
    Down,
    Left,
    Right,
    Enter,
    Escape
}
