using System.Windows.Input;
using WarmAsBefore.DesignSystem.Theme;
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
    }

    private void OnVmChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(CharacterStudioViewModel.CrossSave)
                        or nameof(CharacterStudioViewModel.AllowedInSave))
            SyncSwitches();
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
        BodyGrid.ColumnDefinitions.Clear();
        BodyGrid.RowDefinitions.Clear();

        if (narrow)
        {
            BodyGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            BodyGrid.RowDefinitions.Add(new RowDefinition(new GridLength(420)));
            BodyGrid.RowDefinitions.Add(new RowDefinition(GridLength.Star));
            Grid.SetColumn(Stage, 0);
            Grid.SetRow(Stage, 0);
            Grid.SetColumn(PaneCol, 0);
            Grid.SetRow(PaneCol, 1);
            return;
        }

        BodyGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(352)));
        BodyGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        BodyGrid.RowDefinitions.Add(new RowDefinition(GridLength.Star));
        Grid.SetColumn(Stage, 0);
        Grid.SetRow(Stage, 0);
        Grid.SetColumn(PaneCol, 1);
        Grid.SetRow(PaneCol, 0);
    }

    /// <summary>
    /// 翻开这一页：整块从左边 34px 抽进来（mech 那一条，两头快中间直），抬头那根分线同时从左擦过去；
    /// 隔一拍，立绘从档案底下站上来、右边那一栏的纸页从另一侧抽出来。
    ///
    /// 三个决定都是有理由的：
    /// · **不缩放**。缩放是弹层的说法（「这一张是从刚才那张长出来的」），而这一屏是一页 ——
    ///   翻页靠的是横向位移。上一版这里写的正是 0.96→1 的等比放大，所以它怎么看都是一层弹层。
    /// · **不碰透明度**。Opacity 归样式与禁用态管（见项目记下的动效 hazards）：进场动画一碰它，
    ///   回调被顶掉时就把内容永久留在半透明的值上——「开始游戏那颗键不见了」就是这么来的。
    /// · **位移各归各的轴**。呼吸走 Portrait.TranslationY，这里只走 TranslationX，两处不打架；
    ///   同属性抢占会把呼吸抖成抽搐。
    /// 关掉动效时三步直接落终值，内容一个不少。
    /// </summary>
    public async Task OpenAsync()
    {
        SyncSwitches();
        var ms = MotionTokens.Ms(MotionTokens.SheetMs);
        if (ms == 0)
        {
            Shell.TranslationX = 0;
            HeadSep.ScaleX = 1;
            Portrait.TranslationX = 0;
            PaneCol.TranslationX = 0;
            StartBreathing();
            return;
        }
        Shell.TranslationX = -34;
        HeadSep.ScaleX = 0;
        Portrait.TranslationX = -18;
        PaneCol.TranslationX = 22;
        await Task.WhenAll(
            Shell.TranslateTo(0, 0, (uint)ms, new Easing(MotionTokens.Mech)),
            HeadSep.ScaleTo(1, (uint)MotionTokens.Ms(MotionTokens.EnterMs), new Easing(MotionTokens.OutCubic)));
        await Task.WhenAll(
            Portrait.TranslateTo(0, 0, (uint)MotionTokens.Ms(MotionTokens.SettleMs), new Easing(MotionTokens.OutCubic)),
            PaneCol.TranslateTo(0, 0, (uint)MotionTokens.Ms(MotionTokens.SettleMs), new Easing(MotionTokens.OutCubic)));
        StartBreathing();
    }

    /// <summary>
    /// 合上这一页：往左推回去，比翻开快一档（退出大约是进入的 2/3）。
    /// 只走到 -18 就交给换页：整页已经被抽走大半时，剩下的位移没人看得见，多等那 100ms 反而像卡了一下。
    /// </summary>
    public async Task CloseAsync()
    {
        StopBreathing();
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
