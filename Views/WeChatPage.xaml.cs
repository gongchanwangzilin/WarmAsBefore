using WarmAsBefore.ViewModels;

namespace WarmAsBefore.Views;

public partial class WeChatPage : ContentPage
{
    private bool _enterHooked;

    public WeChatPage(WeChatViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is WeChatViewModel vm) _ = vm.LoadAsync();
    }

    /// <summary>多行输入框上按回车 = 发送（复用 SendCommand）。</summary>
    private void OnEditorCompleted(object sender, EventArgs e)
    {
        if (BindingContext is WeChatViewModel vm && vm.SendCommand.CanExecute(null))
            vm.SendCommand.Execute(null);
    }

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
        HookEnterToSend();
    }

    /// <summary>
    /// WinUI 的 Editor（TextBox）默认 AcceptsReturn=true，回车是换行、不触发 Completed，
    /// 所以在平台层挂 KeyDown：回车（非 Shift 组合）→ 发送；Shift+回车 → 换行。
    /// </summary>
    private void HookEnterToSend()
    {
#if WINDOWS
        if (_enterHooked) return;
        var editor = FindByName("TextEditor");
        if (editor is not Editor ed) return;
        var tb = ed.Handler?.PlatformView as Microsoft.UI.Xaml.Controls.TextBox;
        if (tb is null) return;
        tb.KeyDown += (_, e) =>
        {
            if (e.Key != Windows.System.VirtualKey.Enter) return;
            var shift = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(
                Windows.System.VirtualKey.Shift).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
            if (shift) return;   // Shift+回车：换行
            e.Handled = true;
            if (BindingContext is WeChatViewModel vm && vm.SendCommand.CanExecute(null))
                vm.SendCommand.Execute(null);
        };
        _enterHooked = true;
#endif
    }
}
