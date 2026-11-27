using WarmAsBefore.DesignSystem.Theme;

namespace WarmAsBefore.Controls;

/// <summary>
/// 开关的统一入口：现在它就是 <see cref="GlassSwitch"/> —— 玻璃胶囊 + 玻璃球。
///
/// 为什么保留了这个名字：全仓有 125 处 <c>controls:ViewSwitch</c>（设置、角色工作室、地图、聊天……），
/// 它们原来指向的是「一条纯色胶囊 + 一颗纯色圆球」，跟按钮、滑杆放在同一行里，
/// 一眼就能看出「这个控件不是这套材质」。用户的原话是「凭什么开关就不是玻璃，只有按键拖动是玻璃」。
///
/// 做法是把这一层变成继承而不是逐页替换：改一处，125 处一起换成玻璃，
/// 页面 XAML 一个字都不用动（绑定路径、Toggled 事件、IsToggled 全部同名同义）。
///
/// <see cref="OnColor"/> / <see cref="ThumbColor"/> 是原生 Switch 时代的兼容属性：
/// 12 处页面写了它们。玻璃开关的颜色由世界与强调色决定，所以这两个值**接受但不生效** ——
/// 保留是为了不打断那 12 处页面，而不是为了真的去染一颗玻璃球。
/// </summary>
public sealed class ViewSwitch : GlassSwitch
{
    public static readonly BindableProperty OnColorProperty = BindableProperty.Create(
        nameof(OnColor), typeof(Color), typeof(ViewSwitch), null);

    public static readonly BindableProperty ThumbColorProperty = BindableProperty.Create(
        nameof(ThumbColor), typeof(Color), typeof(ViewSwitch), null);

    /// <summary>开态颜色（兼容原生 Switch 签名；玻璃开关由世界着色，此值不参与绘制）。</summary>
    public Color? OnColor
    {
        get => (Color?)GetValue(OnColorProperty);
        set => SetValue(OnColorProperty, value);
    }

    /// <summary>拇指颜色（同上，兼容用）。</summary>
    public Color? ThumbColor
    {
        get => (Color?)GetValue(ThumbColorProperty);
        set => SetValue(ThumbColorProperty, value);
    }
}
