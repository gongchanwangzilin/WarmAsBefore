using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WarmAsBefore.Models;
using WarmAsBefore.Modules.Market;
using WarmAsBefore.Services.Dev;

namespace WarmAsBefore.ViewModels;

/// <summary>分类标签（Name + 选中态，供筛选 chips 渲染）。</summary>
public sealed partial class ShopCategoryTag : ObservableObject
{
    public string Name { get; init; } = "";

    [ObservableProperty] private bool _isSelected;
}

public sealed partial class ShopViewModel : ObservableObject, IWorstCaseAware
{
    private const int PageSize = 40;   // 每页条数：BindableLayout 无虚拟化，分批渲染不卡

    private readonly ShopService _shop;
    private List<ShopItem> _all = new();   // 当前筛选后的完整列表（内存）
    private int _shown;                     // 已展示条数
    private List<ShopItem> _worst = new();  // 压测档的货架（只读 fixture，不从服务读）

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
            if (WorstCaseProvider.IsActive) { ReloadWorstCase(); return; }   // 压测档只读 fixture，不碰服务
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
        catch (Exception ex) { App.WriteLog("Shop.OnAppearing -> " + ex); }
    }

    private void Reload()
    {
        if (WorstCaseProvider.IsActive) { ReloadWorstCase(); return; }   // 换档 / 服务事件都走同一条
        WalletLabel = _shop.WalletLabel;
        RecordLabel = _shop.RecordLabel;
        ResultMessage = "";
        if (string.IsNullOrEmpty(SyncRepo)) SyncRepo = _shop.SyncRepo;
        IsSyncing = _shop.IsCatalogLoading;   // 后台补货中 → 转圈反馈
        RefreshBuffs();
        RefreshCategories();
        RefreshItems();
    }

    /// <summary>压测档的数据源：fixture 货架 / 真货架。所有筛选、分类、分页都只看这一个入口。</summary>
    private IReadOnlyList<ShopItem> Source => WorstCaseProvider.IsActive ? _worst : _shop.Catalog;

    private void RefreshBuffs()
    {
        var buffs = WorstCaseProvider.IsActive
            ? WorstCaseFixtures.Shop.Buffs(WorstCaseProvider.Mode)
            : (IReadOnlyList<CharacterBuff>)_shop.ActiveBuffs;
        // 一个角色身上可以同时挂几十个标记：全列出来一行放不下，只报前三个 + 总数。
        BuffsLabel = buffs.Count == 0
            ? ""
            : "小雨的标记：" + string.Join("  ", buffs.Take(3).Select(b => $"{b.Emoji}{b.Name}×{b.TurnsLeft}"))
              + (buffs.Count > 3 ? $"  等 {buffs.Count} 个" : "");
    }

    private void RefreshCategories()
    {
        var cats = Source.Select(CategoryKey).Distinct().OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
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

    /// <summary>品类留空的同步商品归到「未分类」：空白 chip 既点不明白，也筛不出东西。</summary>
    private static string CategoryKey(ShopItem item) =>
        string.IsNullOrWhiteSpace(item.Category) ? "未分类" : item.Category;

    private void RefreshItems()
    {
        var q = SearchText?.Trim() ?? "";
        _all = Source
            .Where(x => SelectedCategory == "全部" || CategoryKey(x) == SelectedCategory)
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

    /// <summary>压测档：整个货架换成 fixture，钱包/战绩/标记/仓库也一并换，不写盘、不调模型。</summary>
    public void ReloadWorstCase()
    {
        var mode = WorstCaseProvider.Mode;
        _worst = WorstCaseFixtures.Shop.Catalog(mode);
        // 换档后上一档的品类/搜索词可能已经不存在：不重置就会停在一个筛不出东西的空列表上。
        SearchText = "";
        SelectedCategory = "全部";
        WalletLabel = WorstCaseFixtures.Shop.Wallet(mode);
        RecordLabel = WorstCaseFixtures.Shop.RecordLabel(mode);
        SyncRepo = WorstCaseFixtures.Shop.SyncRepo(mode);
        IsSyncing = false;
        Busy = false;
        AiStatus = "";
        ResultMessage = "";
        RefreshBuffs();
        RefreshCategories();
        RefreshItems();
    }

    /// <summary>压测档挡掉所有会写盘 / 联网 / 调模型的入口，只留一条可读的说明。</summary>
    private bool BlockedByWorstCase()
    {
        if (!WorstCaseProvider.IsActive) return false;
        ResultMessage = "压测档只读 fixture：不买、不送、不上新、不同步，正式存档一个字节都不会被碰。";
        return true;
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
        if (BlockedByWorstCase()) return;
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
        if (BlockedByWorstCase()) return;
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
        if (BlockedByWorstCase()) return;
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
        BodyStatusText = WorstCaseProvider.IsActive
            ? WorstCaseFixtures.Shop.BodyStatus(WorstCaseProvider.Mode)
            : _shop.BodyStatusText() ?? "还没有进入游戏，先选择角色开始陪伴吧";
        ShowBodyStatus = true;
    }

    [RelayCommand]
    private void CloseBodyStatus() => ShowBodyStatus = false;

    [RelayCommand]
    private async Task AiGenerate()
    {
        if (BlockedByWorstCase()) return;
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
        if (BlockedByWorstCase()) return;
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
