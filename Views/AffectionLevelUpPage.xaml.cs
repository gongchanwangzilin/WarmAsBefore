using Microsoft.Maui.Controls;
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

        // 入场：淡入 + 上移
        Opacity = 0;
        TranslationY = 40;
        await Task.WhenAll(this.FadeTo(1, 520, Easing.CubicOut),
                           this.TranslateTo(0, 0, 520, Easing.CubicOut));

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

            _ = heart.FadeTo(0, 1500, Easing.CubicIn);
            _ = heart.TranslateTo((rnd.NextDouble() - 0.5) * 120, -(rnd.Next(180, 320)), 1500, Easing.Linear);
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