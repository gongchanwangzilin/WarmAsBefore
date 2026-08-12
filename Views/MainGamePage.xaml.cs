using WarmAsBefore.ViewModels;

namespace WarmAsBefore.Views;

public partial class MainGamePage : ContentPage
{
    public MainGamePage(MainGameViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }

#if WINDOWS
    private bool _hooked;
    private Microsoft.UI.Xaml.UIElement? _rootEl;
    private readonly System.Text.StringBuilder _typed = new();

    /// <summary>
    /// 开发者测试向导密令：依次按 w z l n b 启动（见 MainGameViewModel.StartTestWizard）。
    /// 挂到 WinUI 窗口根 Content（KeyDown 冒泡起点的最高层），避免页面 PlatformView 收不到键盘。
    /// </summary>
    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
        HookWinUI();
    }

    private void HookWinUI()
    {
        if (_hooked) return;
        // 与 PetService.GetMainWinUIWindow 一致：从 Application.Current.Windows 拿原生窗口
        var win = Application.Current?.Windows.FirstOrDefault(w => w.Handler is not null);
        if (win?.Handler?.PlatformView is not Microsoft.UI.Xaml.Window xw) return;
        _rootEl = xw.Content;
        if (_rootEl is null) return;
        _rootEl.AddHandler(Microsoft.UI.Xaml.UIElement.KeyDownEvent,
            new Microsoft.UI.Xaml.Input.KeyEventHandler(OnWinKeyDown), true);
        _hooked = true;
        App.WriteLog("MainGamePage: wizard hotkey hooked (wzlnb)");
    }

    private void OnWinKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        var key = e.Key;
        // 只关心字母键（忽略修饰键/功能键/输入法组合）
        if (key is < Windows.System.VirtualKey.A or > Windows.System.VirtualKey.Z) return;
        var ch = (char)key;   // VirtualKey.A..Z 与 ASCII 字母一致
        _typed.Append(ch == 'W' || ch == 'Z' || ch == 'L' || ch == 'N' || ch == 'B' ? ch : '_');

        // 密令 wzlnb：保持已匹配前缀，其余清空
        const string secret = "WZLNB";
        if (_typed.Length > secret.Length) _typed.Remove(0, _typed.Length - secret.Length);
        var match = _typed.Length == secret.Length;
        if (match)
            for (var i = 0; i < secret.Length; i++)
                if (_typed[i] != secret[i]) { match = false; break; }
        if (!match)
        {
            // 保留与密令尾部重合的最长后缀（如打了一半 wzl），其余丢弃
            while (_typed.Length > 0 && !secret.StartsWith(_typed.ToString(), StringComparison.Ordinal))
                _typed.Remove(0, 1);
            return;
        }

        _typed.Clear();
        e.Handled = true;
        (BindingContext as MainGameViewModel)?.StartTestWizard();
    }
#endif
}