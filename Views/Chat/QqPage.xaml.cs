using WarmAsBefore.Controls;
using WarmAsBefore.Helpers;
using WarmAsBefore.ViewModels;

namespace WarmAsBefore.Views;

/// <summary>
/// QQ 壳 · 单聊会话：与 WeChatPage 同构，Qq* 青蓝换皮；
/// 去掉礼物/支付面板，多一个「论坛」入口（会话顶栏）与「视频号」外链入口（联系人列表顶栏）。
/// 复用 WeChatViewModel，构造后 SetShell("qq")。
/// </summary>
public partial class QqPage : ContentPage
{
    private bool _enterHooked;

    public QqPage(WeChatViewModel vm)
    {
        InitializeComponent();
        vm.SetShell("qq");
        BindingContext = vm;
        LoadingVeil.Attach(this, vm, "ContactList", "Messages");
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is WeChatViewModel vm) _ = vm.LoadAsync();
        Motion.ArrivePage(this);
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
