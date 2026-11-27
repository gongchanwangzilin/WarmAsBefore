using WarmAsBefore.Controls;
using WarmAsBefore.Helpers;
using WarmAsBefore.Services.Core;
using WarmAsBefore.ViewModels;

namespace WarmAsBefore.Views;

public partial class WeChatPage : ContentPage
{
    /// <summary>比这更窄就收成一栏 + 抽屉：64 竖栏 + 300 会话栏 + 聊天区在 360~430dp 的手机上放不下。</summary>
    private const double CompactBreakpoint = 620;

    private bool _enterHooked;
    private bool _compact;
    private bool _drawerOpen;
    private double _drawerWidth;
    private WeChatViewModel? _viewModel;

    /// <summary>三栏里的竖栏与会话栏。ColumnDefinition 不能在 XAML 里 x:Name（不是 VisualElement），按索引取。</summary>
    private ColumnDefinition RailColumn => RootGrid.ColumnDefinitions[0];
    private ColumnDefinition ListColumn => RootGrid.ColumnDefinitions[1];

    public WeChatPage(WeChatViewModel vm, ThinkTierRuntime thinkTier)
    {
        InitializeComponent();
        BindingContext = vm;
        // 第十四项：思考条/快速按钮绑定到单例 ThinkTierRuntime。
        ThinkBar.BindingContext = thinkTier;
        // 会话列表与消息都要现拉：两个集合任一非空就收骨架。
        LoadingVeil.Attach(this, vm, "ContactList", "Messages");

        // 窄屏收栏 / 宽屏展开：只由页宽决定，窗口拖窄、手机竖屏都在这里收口。
        SizeChanged += (_, _) => ApplyChrome();
        BindingContextChanged += (_, _) =>
        {
            if (_viewModel is not null) _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel = BindingContext as WeChatViewModel;
            if (_viewModel is not null) _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        };

        SafeArea.Attach(RootGrid);
        // 左缘右滑返回：抽屉拉着的时候不接管（那一下该关抽屉，而抽屉压在感应带之上）。
        EdgeBack.Attach(this, EdgeBackZone, () => !_drawerOpen, GoBackAsync);
    }

    /// <summary>
    /// 三栏的响应式形态。宽度只改列宽与抽屉的几何，不动任何一栏内部的版式：
    ///   · 宽（≥620dp）：竖栏 64 / 会话栏 300 / 聊天区吃剩下（列宽仍由 VM 的 RailWidth/ListWidth 说话，
    ///     这样「全屏对话」那条老逻辑不用重写）。
    ///   · 窄：两栏归 0，会话列表变成浮在聊天区之上的抽屉（84% 屏宽，上限 340），顶栏多一枚抽屉键。
    /// </summary>
    private void ApplyChrome()
    {
        if (Width <= 1) return;
        var compact = Width < CompactBreakpoint;
        _compact = compact;
        if (compact)
        {
            RailColumn.RemoveBinding(ColumnDefinition.WidthProperty);
            ListColumn.RemoveBinding(ColumnDefinition.WidthProperty);
            RailColumn.Width = new GridLength(0);
            ListColumn.Width = new GridLength(0);

            _drawerWidth = Math.Clamp(Width * 0.84, 260, 340);
            // 抽屉落在聊天区那一列（* 列 = 满宽），靠左对齐 + 显式宽度浮在上面；
            // 不能放回原来那列 —— 那一列此刻宽 0，孩子会被排成 0 宽，抽屉永远看不见。
            Grid.SetColumn(ListHost, 2);
            Grid.SetColumnSpan(ListHost, 1);
            ListHost.WidthRequest = _drawerWidth;
            ListHost.HorizontalOptions = LayoutOptions.Start;
            DrawerButton.IsVisible = true;
            // 从宽拖到窄时抽屉应当先收着：用户没点过它，就不该占着半个屏幕。
            if (!_drawerOpen) MoveDrawer(false);
        }
        else
        {
            // 交还给 VM：全屏对话时那两栏要收成 0 宽，这条规则只写在 VM 里。
            RailColumn.SetBinding(ColumnDefinition.WidthProperty, nameof(WeChatViewModel.RailWidth));
            ListColumn.SetBinding(ColumnDefinition.WidthProperty, nameof(WeChatViewModel.ListWidth));
            Grid.SetColumn(ListHost, 1);
            Grid.SetColumnSpan(ListHost, 1);
            ListHost.WidthRequest = -1;
            ListHost.HorizontalOptions = LayoutOptions.Fill;
            ListHost.TranslationX = 0;
            DrawerButton.IsVisible = false;
            SetScrim(false);
            _drawerOpen = false;
        }
    }

    private async void OnDrawerButtonClicked(object? sender, EventArgs e) => await ToggleDrawerAsync(!_drawerOpen);

    private void OnScrimTapped(object? sender, TappedEventArgs e) => _ = ToggleDrawerAsync(false);

    private void OnBackClicked(object? sender, EventArgs e) => UiGuard.Run(GoBackAsync, "WeChatPage.Back");

    /// <summary>抽屉开合：弹簧推进 / 收回，压暗层跟着淡入淡出（同一组曲线，不另起一套）。</summary>
    private async Task ToggleDrawerAsync(bool open)
    {
        if (!_compact || _drawerOpen == open) return;
        _drawerOpen = open;
        SetScrim(open);
        var ms = IosMotion.Sheet.Ms();
        try
        {
            await Task.WhenAll(
                IosMotion.SlideToX(ListHost, open ? 0 : -_drawerWidth, 0, IosMotion.Sheet),
                DrawerScrim.FadeTo(open ? 1 : 0, ms, IosMotion.Standard.Ease(0)));
        }
        catch (Exception ex) { App.WriteLog("WeChatPage.Drawer -> " + ex.Message); }
        finally
        {
            ListHost.TranslationX = open ? 0 : -_drawerWidth;
            if (!open) SetScrim(false);
        }
    }

    private void MoveDrawer(bool open)
    {
        ListHost.AbortAnimation("TranslateTo");
        ListHost.TranslationX = open ? 0 : -_drawerWidth;
        DrawerScrim.AbortAnimation("FadeTo");
        SetScrim(open);
        DrawerScrim.Opacity = open ? 1 : 0;
        _drawerOpen = open;
    }

    private void SetScrim(bool visible)
    {
        DrawerScrim.IsVisible = visible;
        if (!visible) DrawerScrim.Opacity = 0;
    }

    /// <summary>点了别的会话：抽屉自己收回去（iOS 上选完人就该看见聊天）。</summary>
    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(WeChatViewModel.PartnerName) or nameof(WeChatViewModel.IsFullScreen))
        {
            if (_compact && _drawerOpen) _ = ToggleDrawerAsync(false);
        }
    }

    private async Task GoBackAsync()
    {
        try
        {
            // 全屏对话时先退出全屏（iOS 的「退一层」），否则离开微信页。
            // 注意这里不走 VM 的 BackCommand：那条命令会去翻 IsContactListVisible ——
            // 三栏版里它已经不决定任何一栏的显隐，点了什么也不会发生。
            if (BindingContext is WeChatViewModel { IsFullScreen: true } fs)
            {
                fs.ToggleFullScreenCommand.Execute(null);
                return;
            }
            await Shell.Current.GoToAsync("..");
        }
        catch (Exception ex) { App.WriteLog("WeChatPage.GoBack -> " + ex.Message); }
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
