using WarmAsBefore.Helpers;
using WarmAsBefore.ViewModels;

namespace WarmAsBefore.Views;

/// <summary>QQ 壳 · 论坛页：话题 + 帖子（纯本地数据面，不走 AI）。</summary>
public partial class QqForumPage : ContentPage
{
    private bool _enterHooked;

    public QqForumPage(QqForumViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is QqForumViewModel vm) _ = vm.LoadAsync();
        Motion.ArrivePage(this);
    }

    /// <summary>输入框回车 = 发帖（Windows 下平台层另挂 KeyDown，见 OnHandlerChanged）。</summary>
    private void OnEditorCompleted(object sender, EventArgs e)
    {
        if (BindingContext is QqForumViewModel vm && vm.PostCommand.CanExecute(null))
            vm.PostCommand.Execute(null);
    }

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
#if WINDOWS
        if (_enterHooked) return;
        var editor = FindByName("ForumEditor");
        if (editor is not Editor ed) return;
        var tb = ed.Handler?.PlatformView as Microsoft.UI.Xaml.Controls.TextBox;
        if (tb is null) return;
        tb.KeyDown += (_, e) =>
        {
            if (e.Key != Windows.System.VirtualKey.Enter) return;
            var shift = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(
                Windows.System.VirtualKey.Shift).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
            if (shift) return;
            e.Handled = true;
            if (BindingContext is QqForumViewModel vm && vm.PostCommand.CanExecute(null))
                vm.PostCommand.Execute(null);
        };
        _enterHooked = true;
#endif
    }
}
