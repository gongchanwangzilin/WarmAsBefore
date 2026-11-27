using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WarmAsBefore.Services;

namespace WarmAsBefore.ViewModels;

/// <summary>
/// 调色页：饱和度 / 明度方块 + 色相条，预览跟着手指走。
///
/// 状态只有三个量（H/S/V），其余全是算出来的：<see cref="Hex"/> 是回显与回传值，
/// <see cref="Current"/> 给预览，<see cref="PureHue"/> 给方块横向渐变的终点。
/// 所以拖动时不存在「方块和读数不同步」这类问题 —— 它们读的是同一份状态。
///
/// 取色起点：设置里存过自选色就从它开始，否则从当前地面色（跟主题走的那个 PageBg）开始，
/// 用户是在「现在的样子」上微调，而不是从一片空白里猜。
/// </summary>
public sealed partial class ColorPickerViewModel : ObservableObject
{
    private readonly SettingsManager _settings;
    private Action? _invalidate;

    /// <summary>色相，0..360。</summary>
    [ObservableProperty] private double _hue;
    /// <summary>饱和度，0..1（方块横轴）。</summary>
    [ObservableProperty] private double _saturation;
    /// <summary>明度，0..1（方块纵轴，1 = 顶边）。</summary>
    [ObservableProperty] private double _value;

    /// <summary>#RRGGBB 只读回显；确认时回传的就是这一串。</summary>
    [ObservableProperty] private string _hex = "#FFFFFF";
    /// <summary>复制回执（「已复制」/失败说明）。</summary>
    [ObservableProperty] private string _copyHint = "";

    public ColorPickerViewModel(SettingsManager settings)
    {
        _settings = settings;
        var (h, s, v) = ToHsv(StartColor());
        Hue = h;
        Saturation = s;
        Value = v;
    }

    /// <summary>当前颜色：预览与回显都读它。</summary>
    public Color Current => FromHsv(Hue, Saturation, Value);

    /// <summary>当前色相上的纯色（S=1、V=1）：方块横向渐变的右端。</summary>
    public Color PureHue => FromHsv(Hue, 1, 1);

    /// <summary>页面把三块画布的重绘挂上来：状态一变就整组重画（一处触发，三块同帧）。</summary>
    public void AttachInvalidate(Action invalidate)
    {
        _invalidate = invalidate;
        _invalidate();
    }

    /// <summary>方块里的绝对落点（控件内归一化坐标，y 从顶边算）。</summary>
    public void SetSv(double nx, double ny)
    {
        Saturation = Math.Clamp(nx, 0, 1);
        Value = Math.Clamp(1 - ny, 0, 1);
    }

    /// <summary>方块里的位移（像素）：手势给的是相对按下点的位移，换成比例再加到当前值上。</summary>
    public void NudgeSv(double dx, double dy, double w, double h)
    {
        if (w <= 0 || h <= 0) return;
        SetSv(Saturation + dx / w, (1 - Value) + dy / h);
    }

    /// <summary>色相条上的绝对落点（控件内归一化横坐标）。</summary>
    public void SetHue(double nx) => Hue = Math.Clamp(nx, 0, 1) * 360;

    /// <summary>色相条上的位移（像素）。</summary>
    public void NudgeHue(double dx, double w)
    {
        if (w <= 0) return;
        SetHue(Hue / 360 + dx / w);
    }

    /// <summary>确认：把颜色写进握手，退回设置页（不拼查询串，见 <see cref="ColorPickerHandoff"/>）。</summary>
    [RelayCommand]
    private async Task Confirm()
    {
        ColorPickerHandoff.Post(Hex);
        await Shell.Current.GoToAsync("..");
    }

    /// <summary>取消：不动设置，直接回去。</summary>
    [RelayCommand]
    private async Task Cancel() => await Shell.Current.GoToAsync("..");

    /// <summary>点十六进制那一行就复制（系统剪贴板，没有自己的 helper）。</summary>
    [RelayCommand]
    private async Task CopyHex()
    {
        try
        {
            await Clipboard.Default.SetTextAsync(Hex);
            CopyHint = "已复制";
        }
        catch (Exception ex)
        {
            App.WriteLog("ColorPicker.CopyHex -> " + ex.Message);
            CopyHint = "复制失败";
        }
    }

    partial void OnHueChanged(double value) => Refresh();
    partial void OnSaturationChanged(double value) => Refresh();
    partial void OnValueChanged(double value) => Refresh();

    private void Refresh()
    {
        Hex = HexOf(Current);
        _invalidate?.Invoke();
    }

    /// <summary>
    /// 起点：这条线自己存过的自选色 → 当前地面色 → 兜底暖白。
    /// 给页面挑色时读 PageTintHex，否则会拿背景色当起点，一确认就把背景色抄进了页面色。
    /// </summary>
    private Color StartColor()
    {
        var saved = ColorPickerHandoff.Current == ColorPickerHandoff.Target.Page
            ? _settings.Current.PageTintHex
            : _settings.Current.BackgroundSolidHex;
        if (!string.IsNullOrWhiteSpace(saved) && Color.TryParse(saved.Trim(), out var picked)) return picked;
        if (Application.Current?.Resources.TryGetValue("PageBg", out var res) == true && res is Color pageBg) return pageBg;
        return Color.FromRgb(0.99f, 0.97f, 0.94f);
    }

    /// <summary>
    /// 预览说明跟着这次的线走：两块色都要看同一张「地 + 半透明卡面」的预览，
    /// 但用户得先知道自己改的是哪一层 —— 否则挑了蓝色回来发现只是玻璃后面变了，会当成没生效。
    /// </summary>
    public string PreviewHint => ColorPickerHandoff.Current == ColorPickerHandoff.Target.Page
        ? "你看到的是这块色当页面底时的样子：卡片与页面自己的底色由它决定，玻璃背后透出什么另由背景色决定。"
        : "你看到的是这块色当地面时的样子：底下那层是它，上面压的是一张半透明的卡面。玻璃透出来的颜色由它决定。";

    /// <summary>HSV → RGB。不用 HSL：SV 方块的横纵两轴就是 S 与 V，换一套色空间会对不上。</summary>
    private static Color FromHsv(double h, double s, double v)
    {
        s = Math.Clamp(s, 0, 1);
        v = Math.Clamp(v, 0, 1);
        var hh = ((h % 360) + 360) % 360 / 60.0;
        var c = v * s;
        var x = c * (1 - Math.Abs(hh % 2 - 1));
        var m = v - c;
        var (r, g, b) = hh switch
        {
            < 1 => (c, x, 0d),
            < 2 => (x, c, 0d),
            < 3 => (0d, c, x),
            < 4 => (0d, x, c),
            < 5 => (x, 0d, c),
            _ => (c, 0d, x),
        };
        return Color.FromRgb((float)(r + m), (float)(g + m), (float)(b + m));
    }

    /// <summary>RGB → HSV，只用来把起点色翻译成方块里的落点。</summary>
    private static (double H, double S, double V) ToHsv(Color c)
    {
        double r = c.Red, g = c.Green, b = c.Blue;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var d = max - min;
        double h;
        if (d <= 0) h = 0;
        else if (max == r) h = 60 * (((g - b) / d) % 6);
        else if (max == g) h = 60 * ((b - r) / d + 2);
        else h = 60 * ((r - g) / d + 4);
        if (h < 0) h += 360;
        var s = max <= 0 ? 0 : d / max;
        return (h, s, max);
    }

    private static string HexOf(Color c)
    {
        var r = (int)Math.Round(c.Red * 255);
        var g = (int)Math.Round(c.Green * 255);
        var b = (int)Math.Round(c.Blue * 255);
        return $"#{r:X2}{g:X2}{b:X2}";
    }
}
