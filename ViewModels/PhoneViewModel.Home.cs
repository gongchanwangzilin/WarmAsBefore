using CommunityToolkit.Mvvm.ComponentModel;

namespace WarmAsBefore.ViewModels;

/// <summary>手机桌面的增量成员：主体在 ModuleViewModels.cs，这里只放桌面相关的部分。</summary>
public sealed partial class PhoneViewModel
{
    /// <summary>桌面当前页：0 = TA 的世界，1 = 一起玩。页码点跟着它走。</summary>
    [ObservableProperty] private int _pageIndex;

    /// <summary>真机/窄窗：屏幕直接铺满，不再套一层假机身。</summary>
    [ObservableProperty] private bool _isCompactScreen;

    public int PageCount => 2;
}
