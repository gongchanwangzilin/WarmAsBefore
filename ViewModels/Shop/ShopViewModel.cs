using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WarmAsBefore.Models;
using WarmAsBefore.Modules.Market;

namespace WarmAsBefore.ViewModels;

/// <summary>分类标签（Name + 选中态，供筛选 chips 渲染）。</summary>
public sealed partial class ShopCategoryTag : ObservableObject
{
    public string Name { get; init; } = "";

    [ObservableProperty] private bool _isSelected;
}

public sealed partial class ShopViewModel : ObservableObject
{
    private const int PageSize = 40;   // 每页条数：BindableLayout 无虚拟化，分批渲染不卡

    private readonly ShopService _shop;
    private List<ShopItem> _all = new();   // 当前筛选后的完整列表（内存）
    private int _shown;                     // 已展示条数

    [ObservableProperty] private string _walletLabel = "";
    [ObservableProperty] private string _recordLabel = "";
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private string _aiPrompt = "";
    [ObservableProperty] private string _aiStatus = "";
    [ObservableProperty] private string _selectedCategory = "全部";
    [ObservableProperty] private string _resultMessage = "";
    [ObservableProperty] private string _syncRepo = "";
    [ObservableProperty] private string _syncStatus = "";
    [ObservableProperty] private string _buffsLabel = "";
    [ObservableProperty] private string _bodyStatusText = "";
    [ObservableProperty] private bool _showBodyStatus;
    [ObservableProperty] private bool _busy;
    [ObservableProperty] private bool _isSyncing;
    [ObservableProperty] private bool _hasMore;

    public ObservableCollection<ShopCategoryTag> Categories { get; } = new();
    /// <summary>商品列表：RangeObservableCollection —— ReplaceRange 只发一次 Reset 事件，
    /// 避免 CollectionView 在 Reset 处理中收到追加操作而抛 "Cannot change ObservableCollection during a CollectionChanged event"。</summary>
    public RangeObservableCollection<ShopItem> Items { get; } = new();

    public ShopViewModel(ShopService shop)
    {
        _shop = shop;
        _shop.Changed += Reload;
        Reload();
    }

    public async void OnAppearing()
    {
        try
        {
            // 先给即时反馈：立即用种子目录渲染（不等待任何 IO）
            Reload();
            await _shop.InitializeAsync();
            // 转圈由 IsCatalogLoading 驱动：补货进行中保持显示，完成时 Changed→Reload 自动停
            Reload();
        }
        catch (Exception ex)
        {
            IsSyncing = false;
            App.WriteLog($"ShopViewModel.OnAppearing -> {ex.Message}");
        }
    }

    private void Reload()
    {
        WalletLabel = _shop.WalletLabel;
        RecordLabel = _shop.RecordLabel;
        ResultMessage = "";
        if (string.IsNullOrEmpty(SyncRepo)) SyncRepo = _shop.SyncRepo;
        IsSyncing = _shop.IsCatalogLoading;   // 后台补货中 → 转圈反馈
        RefreshBuffs();
        RefreshCategories();
        RefreshItems();
    }

    private void RefreshBuffs()
    {
        var buffs = _shop.ActiveBuffs;
        BuffsLabel = buffs.Count == 0
            ? ""
            : "✨ 小雨的标记：" + string.Join("  ", buffs.Select(b => $"{b.Emoji}{b.Name}×{b.TurnsLeft}"));
    }

    private void RefreshCategories()
    {
        var cats = _shop.Catalog.Select(x => x.Category).Distinct().OrderBy(x => x).ToList();
        cats.Insert(0, "全部");
        var current = SelectedCategory;
        foreach (var tag in Categories) tag.IsSelected = false;
        foreach (var c in cats)
        {
            var tag = Categories.FirstOrDefault(x => x.Name == c);
            if (tag is null) { tag = new ShopCategoryTag { Name = c }; Categories.Add(tag); }
            tag.IsSelected = c == current;
        }
        // 移除已不存在的分类
        for (int i = Categories.Count - 1; i >= 0; i--)
            if (!cats.Contains(Categories[i].Name)) Categories.RemoveAt(i);
    }

    private void RefreshItems()
    {
        var q = SearchText?.Trim() ?? "";
        _all = _shop.Catalog
            .Where(x => SelectedCategory == "全部" || x.Category == SelectedCategory)
            .Where(x => string.IsNullOrEmpty(q) ||
                        x.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                        x.Desc.Contains(q, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.AiGenerated ? 0 : 1)
            .ThenBy(x => x.Price)
            .ToList();
        _shown = 0;
        // ReplaceRange：单次 Reset 事件，避免 Clear+Add 触发嵌套 CollectionChanged
        Items.ReplaceRange(NextPage());
        HasMore = _shown < _all.Count;
    }

    private List<ShopItem> NextPage()
    {
        var take = Math.Min(PageSize, _all.Count - _shown);
        var page = take > 0 ? _all.Skip(_shown).Take(take).ToList() : new List<ShopItem>();
        _shown += take;
        return page;
    }

    /// <summary>追加一页到 Items（加载更多）。</summary>
    private void LoadNextPage()
    {
        var page = NextPage();
        if (page.Count == 0)
        {
            HasMore = false;
            return;
        }
        Items.AddRange(page);
        HasMore = _shown < _all.Count;
    }

    partial void OnSearchTextChanged(string value) => RefreshItems();
    partial void OnSelectedCategoryChanged(string value) => RefreshItems();

    [RelayCommand]
    private void SelectCategory(ShopCategoryTag tag)
    {
        if (tag is null) return;
        SelectedCategory = tag.Name;
        foreach (var t in Categories) t.IsSelected = t == tag;
        RefreshItems();
    }

    [RelayCommand]
    private void LoadMore() => LoadNextPage();

    [RelayCommand]
    private async Task Buy(ShopItem item)
    {
        if (_shop.Coins < item.Price)
        {
            ResultMessage = "亲密币不够啦，陪小雨聊天、赢几局游戏赚币吧！";
            return;
        }
        var (ok, msg) = await _shop.BuyAsync(item);
        ResultMessage = msg;
        RefreshItems();
    }

    /// <summary>送礼：消耗库存 1 件，小雨 AI 回应，挂 buff 标记，加好感。</summary>
    [RelayCommand]
    private async Task Gift(ShopItem item)
    {
        if (item is null || Busy) return;
        if (item.Owned <= 0)
        {
            ResultMessage = $"还没买过「{item.Name}」，先点购买，再送小雨吧～";
            return;
        }
        Busy = true;
        var (ok, msg) = await _shop.GiftAsync(item);
        ResultMessage = msg;
        RefreshItems();
        Busy = false;
    }

    /// <summary>使用：消耗库存 1 件，小雨 AI 回应，挂 buff 标记。</summary>
    [RelayCommand]
    private async Task Use(ShopItem item)
    {
        if (item is null || Busy) return;
        if (item.Owned <= 0)
        {
            ResultMessage = $"还没买过「{item.Name}」，先点购买，再使用吧～";
            return;
        }
        Busy = true;
        var (ok, msg) = await _shop.UseAsync(item);
        ResultMessage = msg;
        RefreshItems();
        Busy = false;
    }

    /// <summary>查看身体状态：拉取小雨身体/状态总览并弹出面板。</summary>
    [RelayCommand]
    private void OpenBodyStatus()
    {
        BodyStatusText = _shop.BodyStatusText() ?? "还没有进入游戏，先选择角色开始陪伴吧";
        ShowBodyStatus = true;
    }

    [RelayCommand]
    private void CloseBodyStatus() => ShowBodyStatus = false;

    [RelayCommand]
    private async Task AiGenerate()
    {
        if (Busy) return;
        Busy = true;
        AiStatus = "AI 选品中…";
        var (ok, msg) = await _shop.GenerateItemAsync(AiPrompt);
        AiStatus = "";
        ResultMessage = msg;
        if (ok) RefreshCategories();
        RefreshItems();
        Busy = false;
    }

    /// <summary>从 GitHub 仓库同步商品（owner/repo）。点击立即转圈反馈，结果消息收尾。</summary>
    [RelayCommand]
    private async Task SyncGitHub()
    {
        if (IsSyncing) return;
        IsSyncing = true;
        SyncStatus = "正在从 GitHub 拉取商品…";
        var (ok, msg) = await _shop.SyncFromGitHubAsync(SyncRepo);
        IsSyncing = false;
        SyncStatus = "";
        ResultMessage = msg;
        if (ok)
        {
            SyncRepo = _shop.SyncRepo;
            RefreshCategories();
            RefreshItems();
        }
    }

    [RelayCommand]
    private async Task GoBack() => await Shell.Current.GoToAsync("..");
}
