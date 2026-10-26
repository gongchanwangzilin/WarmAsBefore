using System.Windows.Input;
using WarmAsBefore.DesignSystem.Theme;
using WarmAsBefore.Helpers;
using WarmAsBefore.ViewModels;

namespace WarmAsBefore.Views;

/// <summary>
/// 角色工作室：立绘舞台 + 六段资料（设定 / 状态 / 素材 / 记忆 / 关联 / 说句话）。
/// 它现在是一页的全部内容（CharacterStudioPage 直接放它），不再是一层弹起来的卡 ——
/// 所以进场的写法是横向抽出，不是缩放弹出；离开靠抬头那颗「&lt; 返回」。
///
/// 开关不在 XAML 里绑定 IsToggled：它们背后是命令而不是赋值 —— 一个要写全局设置并让记忆作用域丢缓存，
/// 一个要改存档白名单。绑 IsToggled 会把值写进字段却什么都不做，
/// 按钮看着拨过去了、盘上没动，那是比不接线更糟的假反馈。所以这里手动接：
/// 视觉状态永远由 VM 的属性回写，用户真正拨动时才发命令，并用一个标记挡住回写触发的那一次 Toggled。
/// 跨存档那颗在立绘下面和「记忆」栏里各摆了一颗：读写的都是 VM.CrossSave 这同一个值，
/// 由 SyncSwitches 一起回写，因此不可能各说各话。
/// </summary>
public partial class CharacterStudioPanel : ContentView
{
    private CharacterStudioViewModel? _vm;
    private bool _syncingSwitch;
    private CancellationTokenSource? _breathe;
    private bool _ready;          // 进场演完之前，换栏不动画（那时整块还在往上走，两层位移会打架）
    private bool _narrow;
    private string _tabKey = "";

    public CharacterStudioPanel()
    {
        InitializeComponent();
        BindingContextChanged += (_, _) => HookVm();
        CrossSwitch.Toggled += (_, e) => OnSwitchToggled(e.Value, () => _vm?.ToggleCrossSaveCommand);
        CrossSwitchStage.Toggled += (_, e) => OnSwitchToggled(e.Value, () => _vm?.ToggleCrossSaveCommand);
        AllowedSwitch.Toggled += (_, e) => OnSwitchToggled(e.Value, () => _vm?.ToggleAllowedCommand);
        HookVm();
    }

    private void HookVm()
    {
        var next = BindingContext as CharacterStudioViewModel;
        if (ReferenceEquals(next, _vm)) return;
        if (_vm is not null) _vm.PropertyChanged -= OnVmChanged;
        _vm = next;
        if (_vm is not null) _vm.PropertyChanged += OnVmChanged;
        SyncSwitches();
        LayoutTabs();
    }

    private void OnVmChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(CharacterStudioViewModel.CrossSave)
                        or nameof(CharacterStudioViewModel.AllowedInSave))
        {
            SyncSwitches();
            return;
        }
        if (e.PropertyName == nameof(CharacterStudioViewModel.IsNew))
        {
            // 草稿没有「05 关联」：列数跟着真正会显示的那几颗走，别在中间留一个空槽。
            LayoutTabs();
            return;
        }
        if (e.PropertyName is nameof(CharacterStudioViewModel.IsTabSet)
                        or nameof(CharacterStudioViewModel.IsTabState)
                        or nameof(CharacterStudioViewModel.IsTabArt)
                        or nameof(CharacterStudioViewModel.IsTabMem)
                        or nameof(CharacterStudioViewModel.IsTabLink)
                        or nameof(CharacterStudioViewModel.IsTabTalk))
        {
            var key = TabKey();
            if (key == _tabKey) return;
            var forward = TabIndex(key) > TabIndex(_tabKey);
            _tabKey = key;
            if (!_ready || key.Length == 0) return;
            _ = PlayTabAsync(forward);
        }
    }

    private string TabKey()
    {
        if (_vm is null) return "";
        if (_vm.IsTabSet) return "set";
        if (_vm.IsTabState) return "state";
        if (_vm.IsTabArt) return "art";
        if (_vm.IsTabMem) return "mem";
        if (_vm.IsTabLink) return "link";
        if (_vm.IsTabTalk) return "talk";
        return "";
    }

    private static int TabIndex(string key) => key switch
    {
        "set" => 0,
        "state" => 1,
        "art" => 2,
        "mem" => 3,
        "link" => 4,
        "talk" => 5,
        _ => 0,
    };

    /// <summary>
    /// 页签排成等分格：宽屏一行六颗（草稿五颗），窄屏三列两行。
    /// 以前是 FlexLayout 按内容宽排，四字与五字差出一截；选中时再加粗一次，
    /// 整行还会被推一下 —— 用户看到的就是「大小不均、点一下抖」。
    /// 现在每颗等宽、字重不变，选中只换底与字色，位置从进页到离开一直不动。
    /// 放在代码里重排而不是在 XAML 里写死，是因为「草稿少一颗」与「窄屏换行」两个条件会互相打架。
    /// </summary>
    private void LayoutTabs()
    {
        if (TabGrid is null) return;
        var all = new[] { TabSet, TabState, TabArt, TabMem, TabLink, TabTalk };
        var shown = _vm?.IsNew == true ? all.Where(t => !ReferenceEquals(t, TabLink)).ToArray() : all;
        var cols = _narrow ? 3 : shown.Length;
        var rows = (shown.Length + cols - 1) / cols;

        TabGrid.ColumnDefinitions.Clear();
        TabGrid.RowDefinitions.Clear();
        for (var c = 0; c < cols; c++)
            TabGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        for (var r = 0; r < rows; r++)
            TabGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        for (var i = 0; i < shown.Length; i++)
        {
            Grid.SetColumn(shown[i], i % cols);
            Grid.SetRow(shown[i], i / cols);
        }
    }

    private void SyncSwitches()
    {
        if (_vm is null) return;
        _syncingSwitch = true;
        CrossSwitch.IsToggled = _vm.CrossSave;
        CrossSwitchStage.IsToggled = _vm.CrossSave;
        AllowedSwitch.IsToggled = _vm.AllowedInSave;
        _syncingSwitch = false;
    }

    private void OnSwitchToggled(bool value, Func<ICommand?> command)
    {
        if (_syncingSwitch) return;
        if (command() is not { } cmd) return;
        // 命令里如果没接受这一档（例如角色不在名单里），回写会把开关按回去。
        cmd.Execute(null);
        SyncSwitches();
    }

    // ── 过场 ────────────────────────────────────────────────────────────

    /// <summary>
    /// 按「这一屏有多宽」排，不按设备类型排：桌面上把窗口拖窄时，左边那条 352px 的立绘
    /// 会把右边的表单挤成一列字，这时它要的是竖排 —— 和手机同一套。
    ///
    /// 列宽/行高在这里重写成硬值，而不是去调子元素的 Request：以前用 FlexLayout，
    /// 右栏的长句会把基准宽度撑到几千像素，舞台被 Shrink 挤成一半，整栏滑到窗外。
    /// Grid 的两列只有「352/剩余」与「全宽上下两行」两种，跨档时清空重加即可。
    /// </summary>
    public void SetNarrow(bool narrow)
    {
        _narrow = narrow;
        LayoutTabs();

        BodyGrid.ColumnDefinitions.Clear();
        BodyGrid.RowDefinitions.Clear();

        if (narrow)
        {
            BodyGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            BodyGrid.RowDefinitions.Add(new RowDefinition(new GridLength(420)));
            BodyGrid.RowDefinitions.Add(new RowDefinition(GridLength.Star));
            Grid.SetColumn(Stage, 0);
            Grid.SetRow(Stage, 0);
            Grid.SetColumn(PaneCard, 0);
            Grid.SetRow(PaneCard, 1);
            return;
        }

        BodyGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(352)));
        BodyGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        BodyGrid.RowDefinitions.Add(new RowDefinition(GridLength.Star));
        Grid.SetColumn(Stage, 0);
        Grid.SetRow(Stage, 0);
        Grid.SetColumn(PaneCard, 1);
        Grid.SetRow(PaneCard, 0);
    }

    /// <summary>
    /// 翻开这一页。
    ///
    /// 上一版这里和 Shell 的过场各走各的横向位移（页面 +26、面板 -34），两层同轴叠起来
    /// 只剩 8px，等于没有；而且两边都在 Navigated 那一刻起跑，UI 线程正忙着首屏排版，
    /// 190ms 的行程在第一次 tick 就跳到终点 —— 用户看到的就只是「突然出现」。
    /// 现在横向交给页面级的 PageIn（它等首帧落地才走），这里只做纵向的「分层摆上来」：
    /// 抬头落 → 分线擦开 → 舞台升起 → 资料卡升起，一拍一层（终末地那种机械节拍）。
    ///
    /// 三条老规矩不变：不缩放（那是弹层的说法）、不碰透明度（Opacity 归样式与禁用态管）、
    /// 位移各归各的轴（呼吸走 Portrait.TranslationY，这里只碰 X 和容器的 Y）。
    /// 关掉动效时直接落终值，内容一个不少。
    /// </summary>
    public async Task OpenAsync()
    {
        SyncSwitches();
        _ready = false;
        _tabKey = TabKey();
        var ms = MotionTokens.Ms(MotionTokens.SheetMs);
        if (ms == 0)
        {
            ResetEntrance();
            StartBreathing();
            _ready = true;
            return;
        }

        PrepareEntrance();

        await WaitFirstFrameAsync();

        var rise = (uint)MotionTokens.Ms(MotionTokens.CardEnterMs);
        var settle = (uint)MotionTokens.Ms(MotionTokens.SettleMs);
        var stagger = (uint)MotionTokens.StaggerMs;

        _ = HeadBand.TranslateTo(0, 0, rise, new Easing(MotionTokens.OutCubic));
        _ = HeadSep.ScaleTo(1, settle, new Easing(MotionTokens.Mech));
        await Task.Delay((int)stagger);
        _ = Stage.TranslateTo(0, 0, rise, new Easing(MotionTokens.OutCubic));
        _ = Portrait.TranslateTo(0, 0, settle, new Easing(MotionTokens.OutCubic));
        _ = PaneCard.TranslateTo(0, 0, rise, new Easing(MotionTokens.OutCubic));
        await Task.Delay((int)(rise + stagger));
        StartBreathing();
        _ready = true;
    }

    /// <summary>
    /// 摆好进场起点。必须在上屏前调用（页面的 OnAppearing 一进来就调）：
    /// 读盘那几百毫秒里这一页已经画出来了，等读完再摆起点，用户会看到内容先到位、
    /// 再跳回去重演一遍。重复调用无副作用，OpenAsync 里也会兜一次。
    /// </summary>
    public void PrepareEntrance()
    {
        if (MotionTokens.Disabled) return;
        Shell.TranslationX = 0;
        HeadBand.TranslationY = -8;
        HeadSep.ScaleX = 0;
        Portrait.TranslationX = -18;
        Stage.TranslationY = 14;
        PaneCard.TranslationY = 18;
    }

    /// <summary>
    /// 导航完成不等于首帧落地：工作室要读盘、装图，Navigated 之后还要排几百毫秒版。
    /// 在排版途中起跑，动画会在第一次 tick 直接跳到终点。等这块真的 Loaded、
    /// 再等它真正合成过两帧（<see cref="Motion.WaitPaintedAsync"/>），位移才是一段看得见的路程。
    /// （等待有上限：页面在装载途中被换掉时不能把这条路挂死。）
    /// </summary>
    private async Task WaitFirstFrameAsync()
    {
        if (!IsLoaded)
        {
            var tcs = new TaskCompletionSource();
            void OnLoaded(object? sender, EventArgs e)
            {
                Loaded -= OnLoaded;
                tcs.TrySetResult();
            }
            Loaded += OnLoaded;
            if (await Task.WhenAny(tcs.Task, Task.Delay(900)) != tcs.Task) Loaded -= OnLoaded;
        }
        await Motion.WaitPaintedAsync();
    }

    private void ResetEntrance()
    {
        Shell.TranslationX = 0;
        HeadBand.TranslationY = 0;
        HeadSep.ScaleX = 1;
        Portrait.TranslationX = 0;
        Stage.TranslationY = 0;
        PaneCard.TranslationY = 0;
        TabRule.ScaleX = 1;
    }

    /// <summary>
    /// 换栏：内容从翻页的方向浮进来（往后翻从下、往前翻从上），滚动回到顶上。
    ///
    /// 上一版是「整块 PaneScroll 横向滑 16px + 切口线从左擦到右」。两件事都不划算：
    /// 横向位移要重绘整棵子树（这一栏有六个大段），切口线又是一次从左到右的整行扫，
    /// 两者叠起来，用户读到的就是「从左到右切换状态，很卡」。现在只做 8px 的纵向浮起，
    /// 只动 TranslateY（合成层能直接搬，不触发重排），190ms 收回去；页签本身的选中态即时切换。
    /// 素材矩阵的 186 格由 VM 延到这一帧之后再铺，所以第一次翻到「03 素材」也不会压住这一段动效。
    /// </summary>
    private async Task PlayTabAsync(bool forward)
    {
        var ms = (uint)MotionTokens.Ms(MotionTokens.PageSlideMs);
        if (ms == 0 || MotionTokens.Disabled)
        {
            TabRule.ScaleX = 1;
            PaneScroll.TranslationY = 0;
            return;
        }
        _ = PaneScroll.ScrollToAsync(0, 0, false);
        TabRule.ScaleX = 1;
        PaneScroll.TranslationY = forward ? 8 : -8;
        try
        {
            await PaneScroll.TranslateTo(0, 0, ms, new Easing(MotionTokens.OutCubic));
        }
        finally
        {
            // 动画回调被别的导航丢掉时也要落回原位：位移只是提示，不该把内容留在半路。
            PaneScroll.TranslationY = 0;
        }
    }

    /// <summary>
    /// 合上这一页：往左推回去，比翻开快一档（退出大约是进入的 2/3）。
    /// 只走到 -18 就交给换页：整页已经被抽走大半时，剩下的位移没人看得见，多等那 100ms 反而像卡了一下。
    /// </summary>
    public async Task CloseAsync()
    {
        StopBreathing();
        _ready = false;
        var ms = MotionTokens.Ms((int)(MotionTokens.SheetMs * 0.6));
        if (ms == 0) { Shell.TranslationX = 0; return; }
        await Shell.TranslateTo(-18, 0, (uint)ms, new Easing(MotionTokens.Mech));
        Shell.TranslationX = 0;
    }

    /// <summary>
    /// 呼吸：立绘每 4 秒上下走 1.5px。只在这一张卡开着时跑，关掉就停 ——
    /// 一张没人看的图不该占着一条动画 ticker（这条 ticker 曾经把手机页的帧率拖下去过）。
    /// </summary>
    private void StartBreathing()
    {
        if (MotionTokens.Disabled || Portrait is null) return;
        StopBreathing();
        var token = new CancellationTokenSource();
        _breathe = token;
        _ = BreatheLoop(token.Token);
    }

    private void StopBreathing()
    {
        try { _breathe?.Cancel(); } catch { }
        _breathe = null;
        if (Portrait is not null) Portrait.TranslationY = 0;
    }

    private async Task BreatheLoop(CancellationToken token)
    {
        // 每一程 2 秒、来回一趟 4 秒：再快就变成抖，再慢读起来像没动。
        while (!token.IsCancellationRequested)
        {
            await Portrait.TranslateTo(0, -1.5, 2000, Easing.SinInOut);
            if (token.IsCancellationRequested) break;
            await Portrait.TranslateTo(0, 0, 2000, Easing.SinInOut);
        }
        Portrait.TranslationY = 0;
    }
}
