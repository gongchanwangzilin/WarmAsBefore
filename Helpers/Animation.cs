namespace WarmAsBefore.Helpers;

public static class Motion
{
    public static async Task Press(VisualElement e, uint d = 120)
    {
        await e.ScaleTo(0.94, d, Easing.CubicOut);
        await e.ScaleTo(1.0, (uint)(d * 1.4), Easing.SpringOut);
    }

    public static async Task FadeSlide(VisualElement e, uint d = 280, double off = 24)
    {
        e.Opacity = 0; e.TranslationY = off;
        await Task.WhenAll(e.FadeTo(1, d, Easing.CubicOut), e.TranslateTo(0, 0, d, Easing.CubicOut));
    }

    public static async Task Pulse(VisualElement e, uint d = 2400)
    {
        while (true)
        {
            await e.ScaleTo(1.04, d / 2, Easing.SinInOut);
            await e.ScaleTo(1.0, d / 2, Easing.SinInOut);
        }
    }

    public static async Task ShakeX(VisualElement e, uint d = 280)
    {
        var o = e.TranslationX;
        foreach (var _ in Enumerable.Range(0, 3))
        {
            await e.TranslateTo(o - 6, 0, d / 6, Easing.CubicOut);
            await e.TranslateTo(o + 6, 0, d / 6, Easing.CubicOut);
        }
        await e.TranslateTo(o, 0, d / 6, Easing.CubicOut);
    }

    public static async Task Bounce(VisualElement e, uint d = 360)
    {
        e.Scale = 0; e.Opacity = 0;
        await Task.WhenAll(e.ScaleTo(1.06, d, Easing.CubicOut), e.FadeTo(1, d - 60, Easing.CubicOut));
        await e.ScaleTo(1.0, d / 4, Easing.SpringOut);
    }

    public static async Task SlideUp(VisualElement e, uint d = 240)
    {
        e.TranslationY = 40; e.Opacity = 0;
        await Task.WhenAll(e.TranslateTo(0, 0, d, Easing.CubicOut), e.FadeTo(1, d, Easing.CubicOut));
    }
}