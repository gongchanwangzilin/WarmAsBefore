using System.Windows.Input;
using WarmAsBefore.Controls;
using WarmAsBefore.DesignSystem.Theme;
using WarmAsBefore.Helpers;
using WarmAsBefore.ViewModels;

namespace WarmAsBefore.Views;

public partial class PhonePage : ContentPage
{
    /// <summary>机身内边：宽屏时屏幕四周那圈深色边框的厚度。</summary>
    private const double Bezel = 9;
    /// <summary>机身比例，取主流直板机 9:19.5。</summary>
    private const double BodyRatio = 19.5 / 9.0;
    /// <summary>比这更窄就当成真机/小窗：假机身整个撤掉，屏幕铺满。</summary>
    private const double CompactWidth = 620;

    private readonly PhoneViewModel? _vm;
    private readonly Shadow? _bodyShadow;
    private readonly Brush? _bodyStroke;
    private double _screenWidth;
    private double _panStartX;

    public PhonePage(PhoneViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
        _vm = vm;
        // 机身与接地影的静止值先收好：窄屏要把它们摘掉，回到宽屏时恢复保存的那一份。
        _bodyShadow = Body.Shadow;
        _bodyStroke = Body.Stroke;
        // 状态栏/时钟要等 OnAppearing 才落值：TimeLabel 初始为空，正好当就绪信号。
        LoadingVeil.Attach(this, vm, "TimeLabel");
        SizeChanged += (_, _) => ApplyFrame();
        if (DeviceInfo.Platform == DevicePlatform.WinUI)
        {
            // 鼠标拖不动 SwipeGestureRecognizer：桌面单独补一条拖拽。
            // 触屏只留 Swipe，免得这里把纵向滚动抢走。
            var pan = new PanGestureRecognizer();
            pan.PanUpdated += OnPagerPan;
            PagerClip.GestureRecognizers.Add(pan);
        }
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is PhoneViewModel vm) vm.OnAppearing();
        ApplyFrame();
        // 机身是一整件：只让它自己进场，影子不单独晚一拍落地。
        Motion.Arrive(Body);
    }

    /// <summary>
    /// 机身尺寸只能在这里算：XAML 没有「按宽高比限宽」的布局，
    /// 横向拉满的手机不像手机，像一块屏幕。
    /// </summary>
    private void ApplyFrame()
    {
        try
        {
            var w = Width;
            var h = Height;
            if (w <= 1 || h <= 1) return;

            var compact = w < CompactWidth;
            if (_vm is not null && _vm.IsCompactScreen != compact) _vm.IsCompactScreen = compact;

            double screenWidth;
            if (compact)
            {
                Body.Padding = 0;
                Body.StrokeThickness = 0;
                Body.Stroke = null!;
                Body.Shadow = null!;
                BodyShape.CornerRadius = 0;
                ScreenShape.CornerRadius = 0;
                Body.HorizontalOptions = LayoutOptions.Fill;
                Body.VerticalOptions = LayoutOptions.Fill;
                Body.WidthRequest = -1;
                Body.HeightRequest = -1;
                screenWidth = w;
            }
            else
            {
                const double margin = 26;
                var maxHeight = Math.Max(380, h - margin * 2);
                var width = Math.Min(408, Math.Min(w - margin * 2, maxHeight / BodyRatio));
                var height = width * BodyRatio;
                Body.Padding = new Thickness(Bezel);
                Body.StrokeThickness = 1;
                Body.Stroke = _bodyStroke;
                Body.Shadow = _bodyShadow;
                BodyShape.CornerRadius = 52;
                ScreenShape.CornerRadius = 44;
                Body.HorizontalOptions = LayoutOptions.Center;
                Body.VerticalOptions = LayoutOptions.Center;
                Body.WidthRequest = width;
                Body.HeightRequest = height;
                GroundShade.WidthRequest = width * 0.82;
                // 影子落在机身下沿，离屏底还有一段：这是「摆在地上」，不是「浮在空中」。
                GroundShade.TranslationY = (h - height) / 2 - 12;
                screenWidth = width - Bezel * 2;
            }

            if (Math.Abs(screenWidth - _screenWidth) > 0.5)
            {
                _screenWidth = screenWidth;
                PagerTrack.WidthRequest = screenWidth * 2;
                SetPage(_vm?.PageIndex ?? 0, false);
            }
        }
        catch (Exception ex) { App.WriteLog("PhonePage.ApplyFrame -> " + ex.Message); }
    }

    private void SetPage(int index, bool animate)
    {
        if (_vm is null || _screenWidth <= 0) return;
        index = Math.Clamp(index, 0, _vm.PageCount - 1);
        _vm.PageIndex = index;
        // 页码点只改浓淡，不改尺寸：底下的桌面不能因为换页跟着挪一下。
        DotOne.Opacity = index == 0 ? 1 : 0.26;
        DotTwo.Opacity = index == 1 ? 1 : 0.26;

        var target = -index * _screenWidth;
        if (!animate || MotionTokens.Disabled)
        {
            PagerTrack.AbortAnimation("TranslateTo");
            PagerTrack.TranslationX = target;
            return;
        }
        _ = SlidePageAsync(target);
    }

    private async Task SlidePageAsync(double target)
    {
        try
        {
            await PagerTrack.TranslateTo(target, 0, (uint)MotionTokens.Ms(200), new Easing(MotionTokens.Settle));
        }
        catch (Exception ex) { App.WriteLog("PhonePage.SlidePage -> " + ex.Message); }
        finally { PagerTrack.TranslationX = target; }
    }

    private void OnSwipeLeft(object? sender, SwipedEventArgs e) => SetPage((_vm?.PageIndex ?? 0) + 1, true);

    private void OnSwipeRight(object? sender, SwipedEventArgs e) => SetPage((_vm?.PageIndex ?? 0) - 1, true);

    private void OnDotTapped(object? sender, TappedEventArgs e)
    {
        if (int.TryParse(e.Parameter?.ToString(), out var index)) SetPage(index, true);
    }

    /// <summary>
    /// 桌面上的拖拽翻页（只接了 Windows 鼠标）。拖过四成屏宽才翻页，
    /// 指尖抖一下不该把桌面甩到下一页。
    /// </summary>
    private void OnPagerPan(object? sender, PanUpdatedEventArgs e)
    {
        if (_vm is null || _screenWidth <= 0) return;
        var index = _vm.PageIndex;
        try
        {
            switch (e.StatusType)
            {
                case GestureStatus.Started:
                    _panStartX = -index * _screenWidth;
                    PagerTrack.AbortAnimation("TranslateTo");
                    break;
                case GestureStatus.Running:
                    var x = _panStartX + e.TotalX;
                    // 到头再拖只给一点阻尼：读起来是「到头了」，不是「卡住了」。
                    var over = x > 0 ? x : x < -_screenWidth ? x + _screenWidth : 0;
                    PagerTrack.TranslationX = Math.Clamp(x, -_screenWidth, 0) + over * 0.28;
                    break;
                default:
                    var dx = PagerTrack.TranslationX - _panStartX;
                    var next = index;
                    if (dx < -_screenWidth * 0.4) next = index + 1;
                    else if (dx > _screenWidth * 0.4) next = index - 1;
                    SetPage(next, true);
                    break;
            }
        }
        catch (Exception ex) { App.WriteLog("PhonePage.OnPagerPan -> " + ex.Message); }
    }

    /// <summary>
    /// 点图标：先让图标缩一下再弹回（140ms 以内），然后执行对应命令。
    /// 命令不挂在手势的 Command 上而是走 CommandParameter —— 否则手势会在 Tapped 之前就执行，
    /// 动画只能演给已经翻走的那一页看。小游戏的 kind 放 StyleId：参数位被命令本身占住了。
    /// </summary>
    private async void OnAppTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not View host || e.Parameter is not ICommand command) return;
        try
        {
            if (!MotionTokens.Disabled)
            {
                host.AbortAnimation("ScaleTo");
                await host.ScaleTo(0.93, 55, Easing.CubicOut);
                await host.ScaleTo(1, 85, new Easing(MotionTokens.Settle));
            }
        }
        catch { /* 动画被打断也要导航 */ }
        finally { host.Scale = 1; }

        try
        {
            var kind = host.StyleId;
            if (command.CanExecute(kind)) command.Execute(kind);
        }
        catch (Exception ex) { App.WriteLog("PhonePage.OnAppTapped -> " + ex.Message); }
    }
}
