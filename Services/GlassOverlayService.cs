using System.Runtime.CompilerServices;
using WarmAsBefore.Controls;
using WarmAsBefore.DesignSystem.Theme;

namespace WarmAsBefore.Services;

/// <summary>
/// 玻璃外观的运行时装配。
///
/// 注意：这里**不再**往页面上盖整页叠加层。
/// 早期版本给每个页面根 Grid 追加一个全屏玻璃层，后果是：
///   · 叠加层压在所有内容之上，挡掉点击 —— 下层看得到、点不到；
///   · 免责声明这类页内弹层被压到它下面；
///   · 每访问一个页面就留一个强引用，刷新成本随会话线性增长。
/// 玻璃应该是「表面材质」而不是「一层膜」：现在由 ThemeManager → <see cref="GlassTokens"/>
/// 下发画刷，卡片 / 按钮 / 上下栏 / 弹窗的样式用 {DynamicResource} 引用，各处自动跟随档位。
///
/// 本类只剩一件事：给声明了 x:Name="DecorBlur" 的页面挂真实背景模糊（目前只有标题页装饰层）。
/// </summary>
public sealed class GlassOverlayService
{
    private readonly ThemeManager _theme;

    /// <summary>弱引用：页面被 Shell 回收后随之释放，不驻留历史页面。</summary>
    private readonly ConditionalWeakTable<ContentPage, View> _decorBlurs = new();

    private ContentPage? _current;
    private readonly object _lock = new();

    public GlassOverlayService(ThemeManager theme) => _theme = theme;

    public void Start()
    {
        _theme.Changed += Refresh;
        var shell = Shell.Current;
        if (shell is not null)
        {
            shell.Navigated += (_, _) => Attach(Shell.Current?.CurrentPage);
            Attach(shell.CurrentPage);
        }
        else
        {
            // Shell 尚未就绪：延迟重试一次（首次启动时序）
            _ = Task.Delay(1500).ContinueWith(_ =>
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (Shell.Current is null) return;
                    Shell.Current.Navigated += (_, _) => Attach(Shell.Current?.CurrentPage);
                    Attach(Shell.Current.CurrentPage);
                }));
        }
    }

    private void Attach(Page? page)
    {
        if (page is not ContentPage cp) return;
        lock (_lock)
        {
            _current = cp;
            if (!_decorBlurs.TryGetValue(cp, out _))
            {
                var decor = cp.FindByName<View>("DecorBlur");
                if (decor is not null) _decorBlurs.Add(cp, decor);
            }
        }
        Refresh();
    }

    private void Refresh()
    {
        // 真实背景模糊只在毛玻璃 / 液态两档开启（磨砂只是半透明，不做模糊）
        var blurOn = _theme.Glass || _theme.Liquid;

        View? decor = null;
        lock (_lock)
        {
            if (_current is not null) _decorBlurs.TryGetValue(_current, out decor);
        }
        if (decor is null) return;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            decor.IsVisible = blurOn;
            DecorBlur.Apply(decor, blurOn);
        });
    }
}
