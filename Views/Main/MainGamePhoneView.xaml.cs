using System.Collections.Specialized;
using System.ComponentModel;
using WarmAsBefore.Controls;
using WarmAsBefore.Helpers;
using WarmAsBefore.ViewModels;

namespace WarmAsBefore.Views;

public partial class MainGamePhoneView : ContentView
{
    private readonly CharacterBackdropPresenter _backdrop;
    private MainGameViewModel? _vm;

    public MainGamePhoneView()
    {
        InitializeComponent();
        _backdrop = new CharacterBackdropPresenter(PhoneCharacterBgA, PhoneCharacterBgB, PhoneCharacterBgHost);
        BindingContextChanged += OnBindingContextChangedInternal;
        // 手机布局是三段（顶栏 / 舞台 / 输入区）：刘海与手势条的留白加在整块上，
        // 舞台与聊天列表自己吃满剩下的高度，滚动内容仍能延伸到安全区边缘。
        SafeArea.Attach(PhoneRoot);
    }

    private void OnBindingContextChangedInternal(object? sender, EventArgs e)
    {
        // 换绑时把旧 VM 的两个订阅一并摘掉，避免叠订阅。
        if (_vm is not null)
        {
            _vm.PropertyChanged -= OnVmPropertyChanged;
            _vm.Messages.CollectionChanged -= OnChatMessagesChanged;
        }
        _vm = BindingContext as MainGameViewModel;
        if (_vm is null) return;
        _vm.PropertyChanged += OnVmPropertyChanged;
        // 聊天列表不再靠 KeepLastItemInView 自动滚底（同桌面版，WinUI 布局环崩点），
        // 滚底由这里监听集合后延迟执行。
        _vm.Messages.CollectionChanged += OnChatMessagesChanged;
        _ = ApplyCharacterBackdropAsync(instant: true);
    }

    // 滚底挪到插入后的下一拍（120ms 延迟），彻底离开布局 pass；120ms 内连发多条
    // 合并为一次滚动（in-flight 标志防抖），滚不到（列表未建好等）就静默放过。
    private bool _chatScrollQueued;
    private void OnChatMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_vm is null) return;
        if (e.Action is not (NotifyCollectionChangedAction.Add
            or NotifyCollectionChangedAction.Replace
            or NotifyCollectionChangedAction.Reset)) return;
        if (_vm.Messages.Count == 0 || _chatScrollQueued) return;
        _chatScrollQueued = true;
        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(120), () =>
        {
            _chatScrollQueued = false;
            try
            {
                if (_vm is { Messages.Count: > 0 })
                    PhoneChatList.ScrollTo(_vm.Messages[_vm.Messages.Count - 1], null,
                        Microsoft.Maui.Controls.ScrollToPosition.End, false);
            }
            catch { /* 故意忽略：滚动到底只是体验，列表未布局好时会失败 */ }
        });
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainGameViewModel.CharacterBackdrop))
            MainThread.BeginInvokeOnMainThread(() => _ = ApplyCharacterBackdropAsync());
    }

    /// <summary>角色背景换场；场景/地图背景接管时 VM 会给空，这里收掉露出底色。</summary>
    private async Task ApplyCharacterBackdropAsync(bool instant = false)
    {
        var state = _vm?.CharacterBackdrop;
        try
        {
            if (state is null)
            {
                await _backdrop.ApplyAsync(null, "fill", 0, 0, 1, 1, false, instant);
                return;
            }
            await _backdrop.ApplyAsync(
                state.Path, state.Fit, state.CropX, state.CropY, state.CropW, state.CropH,
                state.IsVideo, instant);
        }
        catch (Exception ex) { App.WriteLog("MainGamePhone.CharacterBackdrop -> " + ex.Message); }
    }

    /// <summary>手机版舞台尺寸变化 → 交给 VM 重算立绘位置与缩放。</summary>
    private void OnStageSizeChanged(object? sender, EventArgs e)
    {
        if (BindingContext is not MainGameViewModel vm) return;
        if (sender is not VisualElement el || !el.IsVisible || el.Width <= 0 || el.Height <= 0) return;
        // 同桌面版：星号行会被立绘的期望高度撑大，撑过窗口就再也收不回来。
        // 能信的只有容器（= 窗口）减去舞台偏移，按这个预算往回钳。
        var w = Width > 0 ? Math.Min(el.Width, Width - el.X) : el.Width;
        var h = Height > 0 ? Math.Min(el.Height, Height - el.Y) : el.Height;
        if (w < 1 || h < 1) return;
        vm.OnStageSizeChanged(w, h, true);
    }

    public void Pause()
    {
        try { if (SceneVideoPhone is { Source: not null }) SceneVideoPhone.Pause(); }
        catch { /* 故意忽略：暂停场景视频，句柄可能已换 */ }
        _backdrop.Pause();
    }

    public void Play()
    {
        try { if (SceneVideoPhone is { Source: not null, IsVisible: true }) SceneVideoPhone.Play(); }
        catch { /* 故意忽略：恢复播放，同上 */ }
        _backdrop.Resume();
    }
}
