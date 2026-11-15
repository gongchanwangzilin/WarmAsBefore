using System.Collections.Specialized;
using WarmAsBefore.Controls;
using WarmAsBefore.DesignSystem.Theme;
using WarmAsBefore.Helpers;
using WarmAsBefore.ViewModels;

namespace WarmAsBefore.Views;

public partial class MainGamePage : ContentPage
{
    private readonly MainGameViewModel _vm;
    private readonly CharacterBackdropPresenter _backdrop;
    private MainGamePhoneView? _phone;
    private bool _started;
    private bool _backdropSynced;
    private bool _veilAttached;

    // ===== 临时诊断开关（实机二分定位布局环用；定位完成后整段移除）=====
    // WAB_DIAG=chat,stage,minimap,top,left,noscroll,noveil（逗号分隔）
    // 命中 tag = 对应子树整块 IsVisible=false（不参与测量）或跳过对应行为。
    private static readonly System.Collections.Generic.HashSet<string> Diag = new(StringComparer.OrdinalIgnoreCase);
    private bool _diagNoScroll;
    private bool _diagVeilOn;
    private bool _diagNoGalgameDefer;
    private bool _galgameDeferred;

    public MainGamePage(MainGameViewModel vm)
    {
        using (WarmAsBefore.Services.PerfLog.Measure("页 · 主界面 · XAML 膨胀"))
            InitializeComponent();
        BindingContext = vm;
        _vm = vm;
        _backdrop = new CharacterBackdropPresenter(CharacterBgA, CharacterBgB, CharacterBgHost);
        Diag.UnionWith((Environment.GetEnvironmentVariable("WAB_DIAG") ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        App.TraceStep("MainGamePage.Diag: [" + string.Join(",", Diag) + "]");
        if (Diag.Contains("chat")) RightChatPanel.IsVisible = false;
        if (Diag.Contains("stage")) DesktopStage.IsVisible = false;
        if (Diag.Contains("minimap")) MinimapHost.IsVisible = false;
        if (Diag.Contains("top")) { LegacyTopBar.IsVisible = false; WidgetTopBarStack.IsVisible = false; }
        if (Diag.Contains("left")) { LegacyLeftPanel.IsVisible = false; WidgetLeftPanel.IsVisible = false; }
        // 二级开关：stage 与 top 已被矩阵定为共存必要条件，往里再细分。
        if (Diag.Contains("sprite")) SpriteLayer.IsVisible = false;
        if (Diag.Contains("backdrop")) CharacterBgHost.IsVisible = false;
        if (Diag.Contains("fx")) WeatherFx.IsVisible = false;
        if (Diag.Contains("navbtns")) { PrevSceneBtn.IsVisible = false; NextSceneBtn.IsVisible = false; }
        if (Diag.Contains("galgame")) GalgamePanel.IsVisible = false;
        if (Diag.Contains("legacytop")) LegacyTopBar.IsVisible = false;
        if (Diag.Contains("widgettop")) WidgetTopBarStack.IsVisible = false;
        // 三级开关：galgame 面板内部（残余布局环定位）。
        if (Diag.Contains("galtitle")) GalTitleRow.IsVisible = false;
        if (Diag.Contains("galhistory")) GalHistoryBorder.IsVisible = false;
        if (Diag.Contains("galsilence")) GalSilenceHint.IsVisible = false;
        if (Diag.Contains("galinput")) GalInputRow.IsVisible = false;
        _diagNoScroll = Diag.Contains("noscroll");
        _diagVeilOn = !Diag.Contains("noveil");
        _diagNoGalgameDefer = Diag.Contains("nodefer");
        // ===== Galgame 面板延迟挂载（2026-11-15）=====
        // 实测矩阵：整面板摘除必存活、其内部任何单块摘除仍崩、且同版本非确定性复现——
        // 残余布局环在「面板整体参与进场测量风暴」的窗口里。进场期把它从可视树摘下，
        // StartupReady（界面本体/立绘就绪）后再挂回：元素实例与全部绑定原样保留，
        // 只是不在崩溃窗口里参与 Measure/Arrange，也就是「按需加载」的一环。
        if (!_diagNoGalgameDefer)
        {
            DesktopGrid.Children.Remove(GalgamePanel);
            _galgameDeferred = true;
        }
        // 舞台预算只有两个触发源：页面窗口尺寸变化、面板显隐属性变化。二者都只改
        // 「窗口 − 舞台偏移 − 可见右栏」这份预算，不写回任何布局属性。
        SizeChanged += OnPageSizeChanged;
        vm.PropertyChanged += OnVmPropertyChanged;
        // 手机竖版整块只在手机版式下构建：Windows 上它永远不显示，却会和桌面版一起建完
        // （两条 MediaElement、两个 CollectionView、两百来颗控件），那就是"进存档卡一下"的大头。
        if (vm.IsPhoneLayout)
        {
            using var _ = WarmAsBefore.Services.PerfLog.Measure("页 · 主界面 · 手机版构建");
            _phone = new MainGamePhoneView();
            PhoneHost.Content = _phone;
        }
        // 聊天列表不再靠 KeepLastItemInView 自动滚底（那会在布局 pass 内强制滚动，
        // 是 WinUI 布局环崩点），滚底由这里监听集合后延迟执行。
        if (vm.IsDesktopLayout && !_diagNoScroll) HookChatListScroll();
    }

    // ============ 聊天列表滚底（替代 KeepLastItemInView）============
    // Messages 是 RangeObservableCollection：整段替换（读档恢复）发 Reset、单条新消息发 Add。
    // 这里只把「滚到最新一条」挪到插入后的下一拍（120ms 延迟），彻底离开布局 pass；
    // 120ms 内连发多条时合并为一次滚动（in-flight 标志防抖），滚不到（列表未建好等）就静默放过。
    private bool _chatScrollQueued;
    private void HookChatListScroll()
    {
        _vm.Messages.CollectionChanged += OnChatMessagesChanged;
    }

    private void OnChatMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action is not (NotifyCollectionChangedAction.Add
            or NotifyCollectionChangedAction.Replace
            or NotifyCollectionChangedAction.Reset)) return;
        if (_vm.Messages.Count == 0 || _chatScrollQueued) return;
        _chatScrollQueued = true;
        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(120), () =>
        {
            _chatScrollQueued = false;
            try
            {
                if (_vm.Messages.Count > 0)
                    ChatList.ScrollTo(_vm.Messages[_vm.Messages.Count - 1], null,
                        Microsoft.Maui.Controls.ScrollToPosition.End, false);
            }
            catch { }
        });
    }

    private void RestoreDeferredPanels()
    {
        if (!_galgameDeferred) return;
        _galgameDeferred = false;
        try
        {
            if (!DesktopGrid.Children.Contains(GalgamePanel))
                DesktopGrid.Children.Add(GalgamePanel);
        }
        catch (Exception ex) { App.WriteLog("MainGame.RestoreGalgame -> " + ex.Message); }
    }

    private void OnPageSizeChanged(object? sender, EventArgs e)
    {
        if (BindingContext is MainGameViewModel { IsDesktopLayout: true }) ScheduleReportStageSize();
    }

    // 页面尺寸变化与面板显隐属性变化往往连发多轮通知，用防抖令牌把它们合并成
    // 一次延迟重算：预算只在空闲帧里算一次交给 VM，不做任何布局写回。
    private CancellationTokenSource? _stageSizeDebounce;
    private void ScheduleReportStageSize()
    {
        _stageSizeDebounce?.Cancel();
        var cts = new CancellationTokenSource();
        _stageSizeDebounce = cts;
        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(32), () =>
        {
            if (cts.IsCancellationRequested) return;
            ReportStageSize(DesktopStage);
        });
    }

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainGameViewModel.CharacterBackdrop))
        {
            // 背景通知可能来自线程池：统一走 Ui.Post 回 UI 线程（WinUI 下线程池取不到 DispatcherQueue 会崩进程）。
            Ui.Post(() => _ = ApplyCharacterBackdropAsync());
            return;
        }
        if (e.PropertyName == nameof(MainGameViewModel.StartupReady))
        {
            // 进场风暴结束（界面本体/立绘就绪）后再过 2 秒，把延迟挂载的 Galgame 面板
            // 放回可视树。实测 StartupReady 后立即挂回仍会撞上首帧渲染期的布局环窗口
            //（异常在 Begin.Exit 后约 1 秒抛出），2 秒把挂载挪到窗口之外。
            // 摘除/挂回都不重建元素，绑定与 Grid 附加属性原样保留。
            Ui.Post(() => Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(2000), RestoreDeferredPanels));
            return;
        }
        if (e.PropertyName == nameof(MainGameViewModel.EntryProgress))
        {
            // 入口进度卡的比例进度线：真实阶段值（VM EntryProgress 0..1）→ 比例 bounds。
            // 放在 Ui.Post 兜一次线程（进度也可能从后台推进）。
            Ui.Post(() =>
            {
                try
                {
                    var p = Math.Clamp(_vm?.EntryProgress ?? 0, 0, 1);
                    AbsoluteLayout.SetLayoutBounds(EntryBarFill, new Microsoft.Maui.Graphics.Rect(0, 0, p, 1));
                }
                catch { }
            });
            return;
        }
        if (e.PropertyName is not (nameof(MainGameViewModel.HideDesktopPanels)
            or nameof(MainGameViewModel.IsRightPanelVisible)
            or nameof(MainGameViewModel.WidgetModeEnabled)
            or nameof(MainGameViewModel.IsWidgetPanelVisible))) return;
        if (!_vm.IsDesktopLayout) return;
        // 面板显隐会改舞台预算：与页面尺寸变化共用同一条防抖延迟链，合并成一次重算。
        ScheduleReportStageSize();
    }

    /// <summary>角色背景换场：两层图片交叉淡入；没有新背景时收掉，露出场景底色。</summary>
    private async Task ApplyCharacterBackdropAsync(bool instant = false)
    {
        if (!_vm.IsDesktopLayout) return;
        var state = _vm.CharacterBackdrop;
        try
        {
            if (state is null)
            {
                await _backdrop.ApplyAsync(null, "fill", 0, 0, 1, 1, false, instant);
                return;
            }
            await _backdrop.ApplyAsync(
                state.Path, state.Fit, state.CropX, state.CropY, state.CropW, state.CropH,
                state.IsVideo, instant);
        }
        catch (Exception ex) { App.WriteLog("MainGame.CharacterBackdrop -> " + ex.Message); }
    }

    /// <summary>
    /// 舞台预算只从「页面窗口尺寸 − 舞台 X/Y 偏移 − 可见右栏宽度」推，绝不读舞台自身
    /// 被量出来的 Width/Height：那会被立绘的期望尺寸反向撑大，形成 measure→arrange 回环。
    /// el.X/el.Y 只由固定栏决定，与立绘解耦，可安全当输入。
    /// </summary>
    private double _lastStageW = -1;
    private double _lastStageH = -1;

    private void ReportStageSize(VisualElement el)
    {
        if (BindingContext is not MainGameViewModel vm) return;
        if (!el.IsVisible) return;
        // 所有输入先挡 NaN/Inf：NaN 与任何数比较都为 false，一旦流进 VM 会把 Scale 算成
        // NaN，WinUI 拿到 NaN 是原生崩溃。窗口尺寸还必须是正数。
        if (!double.IsFinite(Width) || !double.IsFinite(Height)
            || !double.IsFinite(el.X) || !double.IsFinite(el.Y)
            || Width <= 0 || Height <= 0) return;
        // 右栏宽度不再量出来：列定义固定 300，面板也不设 WidthRequest，直接用常量扣预算，
        // 杜绝「读了被布局影响的宽度、又把结果写回布局」的回环。
        // 右栏宽度以 VM 属性为准：进场早期 Border 的绑定可能还没落，读控件会把
        // 可见右栏算成 0，预算虚大 300px（立绘基准偏移的老毛病）。
        var right = vm.IsRightPanelVisible ? 300 : 0;
        // 窗口极小时固定栏会把星号格挤成负可用空间：夹到 0，不把负数传给 VM。
        var w = Math.Max(0, Width - el.X - right);
        var h = Math.Max(0, Height - el.Y);
        if (!double.IsFinite(w) || !double.IsFinite(h) || w < 1 || h < 1) return;
        // 预算没实质变化就不重复通知 VM，避免多余重算。
        if (Math.Abs(w - _lastStageW) < 0.5 && Math.Abs(h - _lastStageH) < 0.5) return;
        _lastStageW = w;
        _lastStageH = h;
        App.TraceStep($"StageSize.Report: page={Width:F0}x{Height:F0}, stageXY=({el.X:F0},{el.Y:F0}), stageMeasured={el.Width:F0}x{el.Height:F0}, right={right:F0}, out={w:F0}x{h:F0}");
        vm.OnStageSizeChanged(w, h, false);
    }

    /// <summary>
    /// 未标定遮罩期间吃掉安卓返回键：遮罩本来就是"不选一个出口不让走"，
    /// 返回键绕过去等于把强制标定变成可以随手跳过。
    /// </summary>
    protected override bool OnBackButtonPressed()
    {
        if (BindingContext is MainGameViewModel { IsCalibrationBlocked: true })
            return true;
        return base.OnBackButtonPressed();
    }

    /// <summary>离开主页（如进 CG/设置）暂停场景视频，避免声音与画面串场；返回后从当前位置恢复。</summary>
    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _vm.DetachRuntimeEvents();
        PauseSceneVideo();
        // 小地图跟着离场：取材要跑一遍全图最短路，人在别的页面时没东西看，订阅纯属白算。
        _vm.Minimap.Detach();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        App.TraceStep("MainGamePage.OnAppearing.Begin");
        // Singleton 服务上的运行期事件跟页面进出场挂摘（审计 A4）：
        // VM 是 Transient，不摘的话每进出一次主界面就叠一份订阅。
        _vm.AttachRuntimeEvents();
        // 音量可能刚在设置里调过：进场先重读一次，视频不必等下一次换场景才跟上
        _vm.RefreshSceneVideoVolume();
        ResumeSceneVideo();
        _vm.Minimap.Attach();
        _vm.SyncWidgetModeFromSettings();
        // 「是否启用 Galgame」可能在设置页里改过：每次进场重读一次
        _vm.RefreshBackendMode();
        if (!_backdropSynced)
        {
            _backdropSynced = true;
            _ = ApplyCharacterBackdropAsync(instant: true);
        }
        else
        {
            _backdrop.Resume();
        }
        HookDesktopArrowKeys();
        // 主页也纳入全站进场契约：只演一次（Arrive 的弱表），从 CG/设置返回不会重播。
        Motion.ArrivePage(this);
        EnsureHudTopStrip();
        // 入口反馈由 XAML 里的 EntryProgressHost 进度卡承担（真实阶段进度，绑定 EntryLoading）；
        // 诊断 noveil 时整卡隐藏（IsVisible 直接赋值会替换绑定，仅诊断路径使用）。
        if (Diag.Contains("noveil")) EntryProgressHost.IsVisible = false;
        App.TraceStep("MainGamePage.OnAppearing.BeforeBegin");
        StartGameOnce();
        App.TraceStep("MainGamePage.OnAppearing.AfterBegin");
    }

    /// <summary>
    /// 启动重活（读角色/立绘、地图、天气）等过场演完再跑：进门这段只留给画面。
    /// 压在构造函数里会和 190ms 的进场位移抢主线程，读起来就是「卡一下、动画也没了」。
    /// </summary>
    private void StartGameOnce()
    {
        if (_started) return;
        _started = true;
        if (MotionTokens.Disabled) { _vm.Begin(); return; }
        Dispatcher.DispatchDelayed(
            TimeSpan.FromMilliseconds(MotionTokens.PageSlideMs + MotionTokens.FrameMs),
            _vm.Begin);
    }

    // ============ #9 电脑端键盘方向键切换地点 ============
    // MAUI 的 Button 没有 KeyboardAccelerators 属性（那只在 MenuFlyoutItem/ToolbarItem 上），
    // 所以按项目已有做法（见 WeChatPage）在平台层挂按键：WinUI 窗口级 KeyDown 里接左右方向键。
    // 无地点（CanGoPrev/NextScene=false）时命令内部静默忽略，与按钮置灰一致。
    /// <summary>
    /// 终末地 HUD 顶栏带：只在 HUD 世界建（其它世界整块不建，零开销）。
    ///
    /// 这块不是「换了个颜色的旧顶栏」，而是 HUD 的读数带：时间 / 天气 / 好感 / 精力四块方板一字排开，
    /// 下沿压一条 45° 斜纹。数据走 VM 属性，读数变化由 HudReadout 自己播「咔哒」。
    /// </summary>
    private void EnsureHudTopStrip()
    {
        if (_hudStripBuilt) return;
        _hudStripBuilt = true;
        try
        {
            if (!Controls.HudSkin.IsActive)
            {
                HudTopStrip.IsVisible = false;
                return;
            }
            var vm = BindingContext as MainGameViewModel;
            // 窄屏（手机/窗口拖窄）：读数带要能横滑，而不是把「精力」挤出屏幕右边。
            // 用横向 ScrollView 包住整排：HUD 的读数多于屏宽时手指一划就看完，版式不塌。
            var row = new HorizontalStackLayout { Spacing = 10, Padding = new Thickness(16, 10, 16, 6) };
            var scroller = new ScrollView
            {
                Orientation = ScrollOrientation.Horizontal,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Never,
                // 定高：顶栏行现在是常量测量，横滚带高度不随读数内容变化，掐断 Auto 行测量反馈。
                HeightRequest = 64,
                Content = row,
            };

            // 读数块：终末地世界的「标签 / 大方块数字 / 黄线」，标签是 8 号全大写小字（HUD 的眉标）。
            Controls.HudReadout Make(string label, string path, bool withProgress = false, string? format = null)
            {
                var r = new Controls.HudReadout
                {
                    Label = label,
                    Framed = true,
                    MinimumWidthRequest = withProgress ? 128 : 96,
                    BindingContext = vm,
                };
                if (format is null) r.SetBinding(Controls.HudReadout.ValueProperty, path);
                else r.SetBinding(Controls.HudReadout.ValueProperty, path, stringFormat: format);
                if (withProgress) r.SetBinding(Controls.HudReadout.ProgressProperty, nameof(MainGameViewModel.AffectionPct));
                r.Compose();
                return r;
            }

            row.Add(Make("SYSTEM/CLOCK", nameof(MainGameViewModel.CurrentTimeStr)));
            row.Add(Make("WEATHER", nameof(MainGameViewModel.WeatherDesc)));

            // 好感这一块要「数值 + 进度线」：单独建一次，把百分比也绑上。
            var affection = new Controls.HudReadout { Label = "AFFECTION", Framed = true, MinimumWidthRequest = 128, BindingContext = vm };
            affection.SetBinding(Controls.HudReadout.ValueProperty, nameof(MainGameViewModel.Affection), stringFormat: "{0} / 100");
            affection.SetBinding(Controls.HudReadout.ProgressProperty, nameof(MainGameViewModel.AffectionPct));
            affection.Compose();
            row.Add(affection);

            var energy = new Controls.HudReadout { Label = "ENERGY", Framed = true, MinimumWidthRequest = 110, BindingContext = vm };
            energy.SetBinding(Controls.HudReadout.ValueProperty, nameof(MainGameViewModel.Energy), stringFormat: "{0}%");
            energy.SetBinding(Controls.HudReadout.ProgressProperty, nameof(MainGameViewModel.EnergyPct));
            energy.Compose();
            row.Add(energy);

            // 右上角系统节奏：小字标签 + 黄色方块指示灯（静态，不闪 —— 闪的灯会成为新的卡顿源）
            var badge = new VerticalStackLayout { Spacing = 3, VerticalOptions = LayoutOptions.Center, Margin = new Thickness(12, 0, 4, 0) };
            var badgeLabel = new Label { Text = "TALOS//II", FontSize = 9, CharacterSpacing = 2.2, HorizontalOptions = LayoutOptions.End };
            badgeLabel.SetDynamicResource(Microsoft.Maui.Controls.Label.TextColorProperty, "MutedFg");
            var dots = new HorizontalStackLayout { Spacing = 3, HorizontalOptions = LayoutOptions.End };
            for (var i = 0; i < 4; i++)
            {
                var sq = new BoxView { WidthRequest = i == 3 ? 14 : 5, HeightRequest = 5 };
                sq.SetDynamicResource(BoxView.ColorProperty, "Signal");
                if (i < 3) sq.Opacity = 0.35 + i * 0.15;
                dots.Add(sq);
            }
            badge.Add(badgeLabel);
            badge.Add(dots);
            row.Add(badge);

            var hatch = Controls.HudSkin.Hatch(4, 8, 1.1f);
            HudTopStrip.Children.Clear();
            HudTopStrip.RowDefinitions.Clear();
            HudTopStrip.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            HudTopStrip.RowDefinitions.Add(new RowDefinition(new GridLength(4)));
            HudTopStrip.Add(scroller, 0, 0);
            HudTopStrip.Add(hatch, 0, 1);
            HudTopStrip.IsVisible = true;
        }
        catch (Exception ex)
        {
            App.WriteLog("MainGamePage.HudStrip -> " + ex);
            HudTopStrip.IsVisible = false;
        }
    }

    private bool _hudStripBuilt;

#if WINDOWS
    private bool _arrowsHooked;
    private void HookDesktopArrowKeys()
    {
        if (_arrowsHooked) return;
        try
        {
            if (Application.Current?.Windows.Count > 0
                && Application.Current.Windows[0].Handler?.PlatformView is Microsoft.UI.Xaml.Window w
                && w.Content is Microsoft.UI.Xaml.UIElement root)
            {
                root.KeyDown += (_, e) =>
                {
                    if (BindingContext is not MainGameViewModel vm) return;
                    switch (e.Key)
                    {
                        case Windows.System.VirtualKey.Left:
                            e.Handled = true;
                            if (vm.PrevSceneCommand.CanExecute(null)) vm.PrevSceneCommand.Execute(null);
                            break;
                        case Windows.System.VirtualKey.Right:
                            e.Handled = true;
                            if (vm.NextSceneCommand.CanExecute(null)) vm.NextSceneCommand.Execute(null);
                            break;
                        // #E4：F11 全屏（完全全屏、覆盖任务栏）/ 退出全屏
                        case Windows.System.VirtualKey.F11:
                            e.Handled = true;
                            ToggleFullScreen();
                            break;
                    }
                };
                // #E4：ESC 收起/恢复信息栏与顶部。单独走 handledEventsToo 通道——
                // 焦点在聊天输入框里时 KeyDown 会先被 Entry 消费，普通订阅根本收不到，
                // 表现就是「明明可以收起，按 ESC 却没反应」。
                root.AddHandler(
                    Microsoft.UI.Xaml.UIElement.KeyDownEvent,
                    new Microsoft.UI.Xaml.Input.KeyEventHandler((_, e) =>
                    {
                        if (e.Key != Windows.System.VirtualKey.Escape) return;
                        if (BindingContext is not MainGameViewModel vm) return;
                        vm.ToggleHidePanelsCommand.Execute(null);
                        e.Handled = true;
                    }),
                    handledEventsToo: true);
                _arrowsHooked = true;
            }
        }
        catch (Exception ex) { App.WriteLog("MainGame.HookDesktopArrowKeys -> " + ex); }
    }

    /// <summary>#E4：WinUI 全屏切换——全屏时用 FullScreen presenter（覆盖任务栏），再按恢复窗口。</summary>
    private void ToggleFullScreen()
    {
        try
        {
            if (Application.Current?.Windows.Count > 0
                && Application.Current.Windows[0].Handler?.PlatformView is Microsoft.UI.Xaml.Window w
                && w.AppWindow is { } app)
            {
                if (app.Presenter.Kind == Microsoft.UI.Windowing.AppWindowPresenterKind.FullScreen)
                    app.SetPresenter(Microsoft.UI.Windowing.AppWindowPresenterKind.Overlapped);
                else
                    app.SetPresenter(Microsoft.UI.Windowing.AppWindowPresenterKind.FullScreen);
            }
        }
        catch (Exception ex) { App.WriteLog("MainGame.ToggleFullScreen -> " + ex); }
    }
#else
    private void HookDesktopArrowKeys() { }
#endif


    private void PauseSceneVideo()
    {
        try
        {
            _phone?.Pause();
            if (SceneVideoDesktop is { Source: not null }) SceneVideoDesktop.Pause();
            _backdrop.Pause();
        }
        catch { }
    }

    private void ResumeSceneVideo()
    {
        try
        {
            _phone?.Play();
            if (SceneVideoDesktop is { Source: not null, IsVisible: true }) SceneVideoDesktop.Play();
        }
        catch { }
    }
}
