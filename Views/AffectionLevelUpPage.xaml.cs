using Microsoft.Maui.Controls;
using WarmAsBefore.DesignSystem.Theme;
using WarmAsBefore.Helpers;
using WarmAsBefore.Modules.Affection;
using WarmAsBefore.ViewModels;

namespace WarmAsBefore.Views;

public partial class AffectionLevelUpPage : ContentPage
{
    private readonly AffectionLevelUpService _service;
    private bool _animating;

    public AffectionLevelUpPage(AffectionLevelUpService service)
    {
        InitializeComponent();
        _service = service;
        BindingContext = new AffectionLevelUpViewModel();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        PlayLevelUp();
    }

    /// <summary>入场 + 爱心动画；播放完成后由用户轻触退出。</summary>
    private async void PlayLevelUp()
    {
        if (_service is not { HasPayload: true })
        {
            if (Navigation.NavigationStack.Count > 1) await Shell.Current.GoToAsync("..");
            return;
        }

        var v = (AffectionLevelUpViewModel)BindingContext;
        v.Level = _service.Level;
        v.Title = _service.Title;
        v.CharacterName = _service.CharacterName;
        v.SpritePath = _service.SpritePath;
        v.BackgroundPath = _service.BackgroundPath;
        v.Refresh();

        // 入场：整页一次「上浮 6px」，不再把页面 Opacity 归 0 后淡入。
        // 用 Arrive(this) 而不是 ArrivePage：根 Grid 的孩子里有满屏背景，
        // 逐个错峰会把背景一层层揭开，看着像资源没加载完。
        // Arrive 自带「演过一次就不再演」，从别的页面返回时不会重播。
        Motion.Arrive(this);

        _animating = true;
        _ = LaunchHeartsAsync();
    }

    /// <summary>心形/花朵从画面中段不断飘散上升。</summary>
    private async Task LaunchHeartsAsync()
    {
        var rnd = Random.Shared;
        while (_animating)
        {
            var heart = new Label
            {
                Text = rnd.Next(4) switch { 0 => "💗", 1 => "💖", 2 => "🌷", _ => "💕" },
                FontSize = rnd.Next(20, 38),
                HorizontalOptions = LayoutOptions.Start,
                VerticalOptions = LayoutOptions.Start
            };
            var w = Width > 0 ? Width : 460;
            var h = Height > 0 ? Height : 800;
            var x = rnd.NextDouble() * w;
            var y = rnd.NextDouble() * h * 0.55 + 70;
            AbsoluteLayout.SetLayoutBounds(heart, new Rect(x, y, -1, -1));
            HeartsLayer.Children.Add(heart);

            // 飘散粒子的寿命：走令牌（减少动画时整段为 0，粒子直接停在原地而不是慢慢淡掉）。
            // 这里的 FadeTo 是粒子自己的生命周期（生出来→消亡→被清掉），不是页面进场，所以保留透明度。
            var life = (uint)Math.Max(1, MotionTokens.Ms(MotionTokens.EnterMs * 6));
            _ = heart.FadeTo(0, life, Easing.CubicIn);
            _ = heart.TranslateTo((rnd.NextDouble() - 0.5) * 120, -(rnd.Next(180, 320)), life, Easing.Linear);
            await Task.Delay(rnd.Next(180, 360));
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _animating = false;
        HeartsLayer.Children.Clear();
    }
}