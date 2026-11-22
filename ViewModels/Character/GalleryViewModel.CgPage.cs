using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace WarmAsBefore.ViewModels;

// ==================== CG 收藏册：一次只铺 50 格 ====================
//
// 收藏册原来是 FlexLayout + BindableLayout 把 CgItems 整本摊开：120 张就是 120 个 220×240 的格子
// 一起测量、120 个 Image 同时进处理器 —— 跟之前那三个长列表是同一类无界测量。
// 这里跟它们用同一套解法：全量留在 CgItems，界面上的 CgPageItems 只装已经铺出来的那一截。
//
// 缩略图也因此只在「真显示」时才解码：ImageSource.FromFile 只是个包着路径的壳（不解码），
// 真正解码发生在 Image 进可视树时；一页 50 格就是同时最多 50 张。
public sealed partial class GalleryViewModel
{
    /// <summary>已经铺到界面上的那一页 CG。</summary>
    public ObservableCollection<CgCollectionItem> CgPageItems { get; } = new();

    [ObservableProperty] private bool _hasMoreCg;
    [ObservableProperty] private string _cgPageNote = "";

    /// <summary>整本 CgItems 换了一份（刷新、换档、换角色）就从第一页重新铺。</summary>
    partial void OnCgItemsChanged(ObservableCollection<CgCollectionItem> value)
    {
        CgPageItems.Clear();
        AppendCgPage();
    }

    [RelayCommand]
    private void LoadMoreCg() => AppendCgPage();

    /// <summary>续一页：只把接下来这 50 格挂上去，剩下的继续留在内存里。</summary>
    private void AppendCgPage()
    {
        foreach (var item in CgItems.Skip(CgPageItems.Count).Take(PageSize)) CgPageItems.Add(item);
        HasMoreCg = CgPageItems.Count < CgItems.Count;
        CgPageNote = HasMoreCg ? $"共 {CgItems.Count} 张，已列 {CgPageItems.Count} 张" : "";
    }
}
