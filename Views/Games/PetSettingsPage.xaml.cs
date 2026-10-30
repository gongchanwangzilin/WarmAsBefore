using WarmAsBefore.DesignSystem.Theme;
using WarmAsBefore.Helpers;
using WarmAsBefore.ViewModels;

namespace WarmAsBefore.Views;

/// <summary>
/// 桌宠设置页：左实时预览 + 右分组设置。
/// 预览的两条心跳都挂在代码里：打字机（速度跟着 PetBubbleSpeed 走）与呼吸（节奏就是帧率档位），
/// 让「看不见的设置」在这一页里先看得见。
/// </summary>
public partial class PetSettingsPage : ContentPage
{
    private readonly PetSettingsViewModel _vm;
    private IDispatcherTimer? _typeTimer;
    private IDispatcherTimer? _breathTimer;
    private string _typeFull = "";
    private int _typeIdx;
    private int _holdTicks;           // 打完一句后停多久（按节拍数，不按毫秒，免得起两张表）
    private double _typeAccum;        // 毫秒累积：慢档时一个字要跨好几个 tick，不能靠「每 tick 至少一字」硬走
    private double _phase;
    private bool _wide = true;
    private bool _lockedLast = true;

    public PetSettingsPage(PetSettingsViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = vm;
        SetSeg(0);
        SizeChanged += (_, _) => ApplyResponsiveLayout();
        vm.PropertyChanged += (_, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(PetSettingsViewModel.PetBubbleSpeed):
                    // 拖动速度滑杆：立刻重打一遍当前句，改动才「看得见」
                    StartTypingDemo();
                    break;
                case nameof(PetSettingsViewModel.PetFps):
                case nameof(PetSettingsViewModel.PetFpsLock):
                    ReconcileBreathTimer();
                    break;
                case nameof(PetSettingsViewModel.HoverHint):
                    HintLabel.Text = _vm.HoverHint;
                    HintLabel.IsVisible = !string.IsNullOrEmpty(_vm.HoverHint);
                    break;
            }
        };
        Unloaded += (_, _) => { _typeTimer?.Stop(); _breathTimer?.Stop(); };
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        ApplyResponsiveLayout();
        // 卡片进场：先摆到起点再统一错峰上浮，沿用站里的契约（不接管 Opacity）
        var cards = new VisualElement[] { StageCard, CardPack, CardLook, CardMotion, CardInteract, CardBubble, CardSound, CardVoice, CardPos, CardOther };
        foreach (var c in cards) c.TranslationY = MotionTokens.EnterRisePx;
        Motion.Arrive(cards, (uint)Math.Max(1, MotionTokens.Ms(MotionTokens.CardEnterMs)));
        // 从工作室回来要把新出的包报出来：这条读的是盘上那份 animation.json，只在进页时一次
        _ = _vm.RefreshPackStatusAsync();
        StartTypingDemo();
        ReconcileBreathTimer();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _typeTimer?.Stop();
        _breathTimer?.Stop();
        // 离开页面：把拖动中攒下的改动落盘
        _vm.Flush();
    }

    private void OnReplayPreview(object? sender, EventArgs e) => StartTypingDemo();

    // ============ 打字机演示 ============

    private void StartTypingDemo()
    {
        _typeFull = _vm.NextPreviewLine();
        _typeIdx = 0;
        _holdTicks = 0;
        _typeAccum = 0;
        _vm.PreviewTyped = "";
        if (_typeTimer is null)
        {
            var t = Dispatcher.CreateTimer();
            // 表的节拍取最快速度档；慢速靠每 tick 数字数，避免一张表拖到尾改不动
            t.Interval = TimeSpan.FromMilliseconds(16);
            t.Tick += (_, _) => TypeTick();
            _typeTimer = t;
        }
        _typeTimer.Stop();
        _typeTimer.Start();
    }

    private void TypeTick()
    {
        if (_typeIdx < _typeFull.Length)
        {
            _typeAccum += 16;
            var per = _vm.TypedIntervalMs;
            if (_typeAccum < per) return;
            var chars = (int)(_typeAccum / per);
            _typeAccum -= chars * per;
            _typeIdx = Math.Min(_typeFull.Length, _typeIdx + chars);
            _vm.PreviewTyped = _typeFull[.._typeIdx];
            _holdTicks = 0;
            return;
        }
        // 打完了：停两拍半再换下一句，看得见「说完了」这件事
        _holdTicks++;
        if (_holdTicks > 160) StartTypingDemo();
    }

    // ============ 呼吸演示：节奏 = 帧率档 ============

    private void ReconcileBreathTimer()
    {
        var locked = _vm.PetFpsLock;
        var interval = locked ? Math.Max(16, 1000 / Math.Max(10, _vm.PetFps)) : 16;
        if (_breathTimer is null)
        {
            var t = Dispatcher.CreateTimer();
            t.Tick += (_, _) => BreathTick();
            _breathTimer = t;
        }
        if (_lockedLast != locked || Math.Abs(_breathTimer.Interval.TotalMilliseconds - interval) > 1)
        {
            _lockedLast = locked;
            _breathTimer.Interval = TimeSpan.FromMilliseconds(interval);
            _breathTimer.Stop();
            _breathTimer.Start();
        }
        else if (!_breathTimer.IsRunning)
            _breathTimer.Start();
    }

    private void BreathTick()
    {
        // 真实桌宠在桌面不常驻呼吸（省电）；预览里让它一直在呼吸，帧率档位一眼可辨
        _phase += 0.045;
        var amp = 2.2 * Math.Clamp(_vm.PetScale, 0.4, 2.6) * 0.5;
        PreviewSprite.TranslationY = -amp * (1 - Math.Cos(_phase));
    }

        // ============ 分段标签：外观/动作/气泡/声音/语音/位置/自主 ============

        private void OnSegClicked(object? sender, EventArgs e)
        {
            if (sender is Button b && int.TryParse(b.ClassId, out var idx)) SetSeg(idx);
        }

        /// <summary>七段互斥点亮：选中的段换主行动钮皮，其余回幽灵钮；内容列同步显隐。</summary>
        private void SetSeg(int idx)
        {
            var idxSafe = Math.Clamp(idx, 0, 6);
            var tabs = new VisualElement[] { TabLook, TabMotion, TabBubble, TabSound, TabVoice, TabPos, TabSelf };
            for (var i = 0; i < tabs.Length; i++)
            {
                tabs[i].IsVisible = i == idxSafe;
            }
            var seq = 0;
            foreach (var child in SegBar.Children)
            {
                if (child is Button b)
                {
                    var key = seq == idxSafe ? "BtnPrimarySm" : "BtnGhostSm";
                    if (Application.Current?.Resources.TryGetValue(key, out var v) == true && v is Style s)
                    {
                        b.Style = s;
                    }
                    seq++;
                }
            }
        }

    // ============ 响应式：宽屏左舞台右设置，窄屏上下堆叠 ============

    private void ApplyResponsiveLayout()
    {
        var wide = Width >= 900;
        if (wide == _wide && BodyGrid.ColumnDefinitions.Count > 0) return;
        _wide = wide;
        if (wide)
        {
            BodyGrid.RowDefinitions.Clear();
            BodyGrid.RowDefinitions.Add(new RowDefinition(GridLength.Star));
            BodyGrid.ColumnDefinitions.Clear();
            BodyGrid.ColumnDefinitions.Add(new ColumnDefinition(440));
            BodyGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            Grid.SetRow(StageCard, 0); Grid.SetColumn(StageCard, 0);
            Grid.SetRow(SettingsScroll, 0); Grid.SetColumn(SettingsScroll, 1);
            StageCard.HeightRequest = -1;
            StageCard.MinimumHeightRequest = 420;
        }
        else
        {
            BodyGrid.ColumnDefinitions.Clear();
            BodyGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            BodyGrid.RowDefinitions.Clear();
            BodyGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            BodyGrid.RowDefinitions.Add(new RowDefinition(GridLength.Star));
            Grid.SetRow(StageCard, 0); Grid.SetColumn(StageCard, 0);
            Grid.SetRow(SettingsScroll, 1); Grid.SetColumn(SettingsScroll, 0);
            StageCard.HeightRequest = 400;
            StageCard.MinimumHeightRequest = 0;
        }
    }
}
