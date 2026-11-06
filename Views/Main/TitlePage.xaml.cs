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
        SizeChanged += (_, _) =>
        {
            PlaceRing();
            ApplyResponsiveLayout();
        };
    }

    /// <summary>窄屏（手机、竖拿的平板）与宽屏的分界：侧栏 262 之外右栏还要放得下标语与存档卡。</summary>
    private const double CompactBreakpoint = 820;
    private bool? _compactMode;

    /// <summary>
    /// 按「这一屏有多宽」排，不按设备类型排：Android 手机与平板原先共用一套单列列定义，
    /// 侧栏和主行动区却仍占第 0/1 列，落进同一格互相压住。
    /// 窄屏时把同一批控件搬进滚动容器上下叠，宽屏再搬回两栏 —— 内容只有一份。
    /// </summary>
    private void ApplyResponsiveLayout()
    {
        if (Width <= 0) return;
        var compact = Width < CompactBreakpoint;
        // 弹层每帧都跟屏收口：高度变了（横竖屏、分屏）也要重新封顶，否则底部按钮会滑出屏幕。
        DisclaimerCard.Margin = new Thickness(compact ? 20 : 48, 0);
        if (Height > 0)
            DisclaimerCard.MaximumHeightRequest = Math.Min(640, Math.Max(280, Height - 32));
        if (_compactMode == compact) return;
        _compactMode = compact;
        if (compact) EnterCompact(); else EnterWide();
    }

    private static void Move(Layout from, Layout to, View child)
    {
        if (ReferenceEquals(child.Parent, to)) return;
        (child.Parent as Layout)?.Remove(child);
        to.Add(child);
    }

    private void EnterCompact()
    {
        ShellGrid.IsVisible = false;
        CompactScroll.IsVisible = true;

        // 顺序：抬头与入口 → 主行动 → 页脚。页脚不能留在入口后面被读成第五个入口。
        CompactStack.Children.Clear();
        Move(RailGrid, CompactStack, RailTop);
        Move(ShellGrid, CompactStack, HeroGrid);
        Move(RailGrid, CompactStack, RailBottom);

        HeroGrid.RowDefinitions.Clear();
        HeroGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));   // 0：右上读数角 / 配色小字（二选一）
        HeroGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));   // 1：配色小字（HUD）/ 主行动带
        HeroGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));   // 2：主行动带（HUD）/ 上次存档
        HeroGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));   // 3：上次存档（HUD）
        Grid.SetRow(HudCornerReadout, 0);
        Grid.SetRow(ThemeLegend, 0);
        Grid.SetRow(HeroBand, 1);
        Grid.SetRow(RecentCard, 2);
        HeroBand.VerticalOptions = LayoutOptions.Start;
        RecentCard.VerticalOptions = LayoutOptions.Start;
        RecentCard.HorizontalOptions = LayoutOptions.Fill;
    }

    private void EnterWide()
    {
        CompactScroll.IsVisible = false;
        ShellGrid.IsVisible = true;

        Move(CompactStack, RailGrid, RailTop);
        Grid.SetRow(RailTop, 0);
        Move(CompactStack, RailGrid, RailBottom);
        Grid.SetRow(RailBottom, 2);
        Move(CompactStack, ShellGrid, HeroGrid);
        Grid.SetColumn(HeroGrid, 1);
        Grid.SetRow(HeroGrid, 0);

        HeroGrid.RowDefinitions.Clear();
        HeroGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));   // 0：配色小字（HUD 世界另有右上读数角同住）
        HeroGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));   // 1：HUD 世界的配色小字行（非 HUD 为空行，Auto 零成本）
        HeroGrid.RowDefinitions.Add(new RowDefinition(GridLength.Star));   // 2：主行动带 —— 独占 Star 行，垂直居中
        HeroGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));   // 3：上次存档 —— 独占一行，物理上不可能压到标语
        Grid.SetRow(HudCornerReadout, 0);
        Grid.SetRow(ThemeLegend, 0);
        Grid.SetRow(HeroBand, 2);
        Grid.SetRow(RecentCard, 3);
        HeroBand.VerticalOptions = LayoutOptions.Center;
        RecentCard.VerticalOptions = LayoutOptions.End;
        RecentCard.HorizontalOptions = LayoutOptions.Start;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        // 先按屏宽把版式摆定，再让进场动画去量：反过来的话动画会从两栏的坐标起步。
        ApplyResponsiveLayout();
        // 标题页没有页面顶栏：成就横幅直接贴顶滑出（设置页那些页面保留默认 64 = 顶栏下方）。
        Controls.Toast.TopInset = 16;
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

        // 终末地 HUD 的专属进场：面板扫描线扫一遍、标题「上电」闪一下、括号亮起。
        // 只在 HUD 世界且没关动效时播（内部自己判断），其它世界这几句是空操作。
        PlayHudIntro();

        HookKeys();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        // 键挂在窗口根上，不属于这一页：不摘掉的话，人已经进了主界面，
        // 方向键还在替一页看不见的标题页挪光标（e.Handled 也会把键吃掉）。
        UnhookKeys();
        SetCaret(-1, -1);
        _hudClock?.Stop();
    }

    /// <summary>
    /// 终末地 HUD 的进场机关：扫描线从上扫到下、标题上电（压扁一档再回弹 + 两下小抖）、
    /// 面板边框轻微「通电」。三件事并行起跑，互不等待；非 HUD 世界或关掉动效时什么也不做。
    /// 只动合成层属性，所以不会把这一屏的排版再触发一遍。
    /// </summary>
    private void PlayHudIntro()
    {
        // 行位由这边统一摆：HUD 世界第 1 行让给「配色小字」、读数角占第 0 行；主行动带与存档卡
        // 保持宽屏的 2/3 不动 —— 两套布局共用同一个无重叠的行方案（与 EnterWide 一致）。
        // 注意：结构（读数角、行位）只跟「是不是档案室世界」走；「动效开关」只管机关动作。
        // 旧版把两者绑在一起，用户一关动效，右上角时钟就整块消失 —— 结构不该依赖动画。
        if (!WarmAsBefore.Controls.HudSkin.IsActive)
        {
            Grid.SetRow(ThemeLegend, 0);
            Grid.SetRow(HeroBand, 2);
            Grid.SetRow(RecentCard, 3);
            return;
        }

        Grid.SetRow(ThemeLegend, 1);
        Grid.SetRow(HeroBand, 2);
        Grid.SetRow(RecentCard, 3);
        if (HudCornerReadout is not null)
        {
            HudCornerReadout.IsVisible = true;
            HudCornerTime.Text = DateTime.Now.ToString("HH:mm:ss");
            _hudClock ??= Dispatcher.CreateTimer();
            _hudClock.Interval = TimeSpan.FromSeconds(1);
            _hudClock.Tick += (_, _) => HudCornerTime.Text = DateTime.Now.ToString("HH:mm:ss");
            _hudClock.Start();
        }

        if (!Helpers.HudMotion.IsHud) return;
        try
        {
            _ = Helpers.HudMotion.ScanAsync(HudScan, ShellFrame);
            _ = Helpers.HudMotion.PowerOnAsync(HeroTagline);
            // 边框「通电」：整块面板先从 0.992 收齐，读起来像 HUD 面板被点亮了一下
            if (ShellFrame is not null)
            {
                ShellFrame.Opacity = 0.72;
                _ = ShellFrame.FadeTo(1, 320, Easing.CubicOut);
            }
        }
        catch (Exception ex) { App.WriteLog("TitlePage.HudIntro -> " + ex.Message); }
    }

    private IDispatcherTimer? _hudClock;

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
    ///
    /// 尺寸只在**真的变了**才写 WidthRequest/HeightRequest：每次按键都重写会让这颗 Border
    /// 重新测量、重画一圈，四行同高时那一步纯属白掉一帧 —— 键盘移动时的卡顿就出在这。
    /// 位移仍然走动画：光标一跳会读成「界面自己动了」，滑过去才读成「是我在移动选择」。
    /// </summary>
    private double _ringW = -1, _ringH = -1;

    private void PlaceRing()
    {
        var target = CurrentTarget();
        if (target is null || _col < 0) { FocusRing.IsVisible = false; return; }
        var (x, y) = OffsetIn(target.Element, Root);
        var w = Math.Max(24, target.Element.Width + 8);
        var h = Math.Max(20, target.Element.Height + 8);
        if (Math.Abs(w - _ringW) > 0.5 || Math.Abs(h - _ringH) > 0.5)
        {
            _ringW = w;
            _ringH = h;
            FocusRing.WidthRequest = w;
            FocusRing.HeightRequest = h;
        }
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
