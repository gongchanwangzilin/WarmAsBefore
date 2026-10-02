namespace WarmAsBefore.DesignSystem.Theme;

public class 主题管理器
{
    private readonly ThemeManager _inner;

    public 主题管理器(ThemeManager inner)
    {
        _inner = inner;
    }

    public bool 毛玻璃
    {
        get => _inner.Glass;
        set => _inner.Glass = value;
    }

    public bool 磨砂
    {
        get => _inner.Frost;
        set => _inner.Frost = value;
    }

    public bool 液体
    {
        get => _inner.Liquid;
        set => _inner.Liquid = value;
    }

    public string 活动效果 => _inner.ActiveEffect;

    public string 主题名
    {
        get => _inner.ThemeName;
        set => _inner.ThemeName = value;
    }

    public static string[] 主题名列表 => ThemeManager.ThemeNames;

    public static string 主题显示(string 名称) => ThemeManager.ThemeDisplay(名称);

    public void 重置() => _inner.Reset();

    public event Action? 更改
    {
        add => _inner.Changed += value;
        remove => _inner.Changed -= value;
    }
}
