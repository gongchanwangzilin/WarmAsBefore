using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.Graphics;
using WarmAsBefore.Drawables;
using WarmAsBefore.Models;
using WarmAsBefore.Modules.SaveSystem;
using WarmAsBefore.Services;
using WarmAsBefore.Services.Core;
using WarmAsBefore.Services.Dev;

namespace WarmAsBefore.ViewModels;

/// <summary>
/// 分支树独立页的 VM：把 SaveManager.List() 的血缘数据（ParentId / BranchName / SavedAt）
/// 连同角色库的名字组装成 SaveTreeNode 交给 SaveTreeDrawable。
///
/// 一棵树 = 一份初始存档（ParentId 空或指向不存在的档）以及它的全部后代。
/// 页面顶部有一个根档选择，切换时只把那一棵交给 Drawable，布局随之重算；
/// 默认选最新的一份初始存档（玩家最近开的那一局）。
///
/// 读档不在这里实现：点「去这个存档」只是回到 SavePage 并带 focus 参数，复用原有 LoadSlot 门禁。
/// 所有展示性读取都各自 try/catch，任何一路服务出错只让这一处空着，不把整页带崩。
/// </summary>
public sealed partial class SaveTreeViewModel : ObservableObject, IQueryAttributable, IWorstCaseAware
{
    private readonly SaveManager _save;
    private readonly CharacterLibrary _library;
    private readonly GameEngine _engine;

    /// <summary>供详情面板取父档名：Drawable 只拿到格式化好的字符串，原始 SaveSlot 留一份。</summary>
    private readonly Dictionary<string, SaveSlot> _slots = new(StringComparer.Ordinal);

    /// <summary>每棵树的节点表：键 = 根档 Id。切换根档时直接把对应的那一份交给 Drawable。</summary>
    private readonly Dictionary<string, List<SaveTreeNode>> _trees = new(StringComparer.Ordinal);

    private Action? _invalidate;
    private bool _loading;
    private string _selectedId = "";
    private string _preferredRootId = "";
    private bool _suppressRootChange;

    /// <summary>绘制器实例：XAML 里只放 GraphicsView，Drawable 由页面从 VM 取。</summary>
    public SaveTreeDrawable Drawable { get; } = new();

    [ObservableProperty] private bool _hasSelection;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private string _selectedTitle = "";

    /// <summary>页面顶部的根档选择（下拉）。顺序：最新的初始存档在前。</summary>
    [ObservableProperty] private ObservableCollection<RootOption> _roots = new();
    [ObservableProperty] private RootOption? _selectedRoot;

    /// <summary>「去这个存档」在没有选中时禁用，而不是点了没反应。</summary>
    public bool CanOpenSlot => HasSelection;
    partial void OnHasSelectionChanged(bool value) => OnPropertyChanged(nameof(CanOpenSlot));

    public SaveTreeViewModel(SaveManager save, CharacterLibrary library, GameEngine engine)
    {
        _save = save;
        _library = library;
        _engine = engine;
    }

    // ==================== 静态入口：SavePage 顶栏的「分支树」按钮直接在 XAML 里绑 ====================

    /// <summary>存档页 / 其它入口的按钮只靠 XAML 就能跳，不改它们的 VM / code-behind。</summary>
    public static ICommand NavigateToTree { get; } = new Command(async () =>
    {
        try { await Shell.Current.GoToAsync("saveTree"); }
        catch (Exception ex) { App.WriteLog("SaveTree.Navigate -> " + ex.Message); }
    });

    // ==================== 页面与画布的桥 ====================

    public void AttachInvalidate(Action invalidate) => _invalidate = invalidate;

    private void Redraw()
    {
        var invalidate = _invalidate;
        if (invalidate is null) return;
        try { Helpers.Ui.Post(invalidate); }
        catch (Exception ex) { App.WriteLog("SaveTree.Redraw -> " + ex.Message); }
    }

    /// <summary>可视视口尺寸（ScrollView 的尺寸，不是画布内容尺寸）。</summary>
    public void SetViewport(double w, double h)
    {
        try
        {
            Drawable.SetViewport(w, h);
            Redraw();
        }
        catch (Exception ex) { App.WriteLog("SaveTree.SetViewport -> " + ex.Message); }
    }

    /// <summary>滚动位置：浮动详情面板靠它夹进可见区域。</summary>
    public void SetScroll(double x, double y)
    {
        try { Drawable.SetScrollOffset(x, y); }
        catch (Exception ex) { App.WriteLog("SaveTree.SetScroll -> " + ex.Message); }
    }

    /// <summary>画布 EndInteraction 的落点（相对画布左上角，已扣掉滚动）→ 命中节点 → 选中。</summary>
    public void CanvasTap(PointF point)
    {
        try
        {
            var id = Drawable.HitTest(point.X, point.Y);
            SelectNode(id);
        }
        catch (Exception ex) { App.WriteLog("SaveTree.CanvasTap -> " + ex.Message); }
    }

    /// <summary>鼠标悬停（null = 离开画布）：命中哪枚就在它旁边浮详情面板，不改变选中态。</summary>
    public void CanvasHover(PointF? point)
    {
        try
        {
            Drawable.HoverId = point is { } p ? Drawable.HitTest(p.X, p.Y) : null;
        }
        catch (Exception ex) { App.WriteLog("SaveTree.CanvasHover -> " + ex.Message); }
    }

    // ==================== 数据组装 ====================

    /// <summary>
    /// 读一次权威数据：存档列表 + 角色库并行取，按 SaveSlot 的 ParentId 组出血缘树。
    /// ParentId 指向不存在的档时按根节点处理（老档 / 坏档都会遇到），不交给 Drawable 去猜。
    /// </summary>
    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (_loading) return;
        _loading = true;
        try
        {
            // 压测档与正式档的行长得一样：一条存档 + 它的角色名，后面组树的代码不必分叉。
            List<(SaveSlot Slot, string CharacterName)> rows;
            if (WorstCaseProvider.IsActive)
            {
                rows = WorstCaseFixtures.Archive.Slots(WorstCaseProvider.Mode);
            }
            else
            {
                var listTask = _save.List();
                var rosterTask = _library.ListAsync();
                await Task.WhenAll(listTask, rosterTask);
                var list = await listTask;
                var roster = await rosterTask;
                rows = list.Select(s => (s,
                    roster.FirstOrDefault(c => c.Profile.Id == s.Character)?.Profile.Name ?? "未知角色")).ToList();
            }

            BuildTrees(rows);

            if (WorstCaseProvider.IsActive) { Drawable.CurrentSlotId = null; }
            else
            {
                try { Drawable.CurrentSlotId = _engine.CurrentSaveId; }
                catch (Exception ex) { App.WriteLog("SaveTree.CurrentSlot -> " + ex.Message); }
            }

            // 带 focus 进来时，先认它属于哪棵树：根档选择跟着那个节点走，而不是跟着上次的选择。
            if (_selectedId.Length > 0)
            {
                foreach (var (rootId, list) in _trees)
                {
                    if (!list.Any(n => n.Id == _selectedId)) continue;
                    _preferredRootId = rootId;
                    break;
                }
            }

            // 根档选择：优先沿用上次选的（还在就继续选它），否则落回最新的一份初始存档。
            _suppressRootChange = true;
            var options = new ObservableCollection<RootOption>(_rootOrder);
            Roots = options;
            var keep = options.FirstOrDefault(o => o.Id == _preferredRootId) ?? options.FirstOrDefault();
            SelectedRoot = keep;
            _suppressRootChange = false;

            ApplyTree(keep?.Id);

            StatusText = rows.Count == 0
                ? "还没有存档分支"
                : $"共 {rows.Count} 个存档 · {options.Count} 条起始存档" +
                  (keep is null ? "" : $" · 当前树 {keep.Count} 个");

            // 刷新后沿用原选中项；节点已经不在这一棵树里就清空，避免面板残留旧档。
            if (!string.IsNullOrEmpty(_selectedId) && Drawable.Find(_selectedId) is not null)
                SelectNode(_selectedId);
            else
                SelectNode(null);
        }
        catch (Exception ex)
        {
            StatusText = "分支树数据读取失败，已跳过";
            App.WriteLog("SaveTree.Refresh -> " + ex.Message);
        }
        finally { _loading = false; }
    }

    private readonly List<RootOption> _rootOrder = new();

    /// <summary>
    /// 把存档行分成若干棵树：每份「初始存档」一棵，后代靠 ParentId 挂在下面。
    /// 环 / 断链的孤儿各自成一棵（树根选单里也会出现），保证任何一份存档都看得到。
    /// </summary>
    private void BuildTrees(List<(SaveSlot Slot, string CharacterName)> rows)
    {
        _slots.Clear();
        _trees.Clear();
        _rootOrder.Clear();

        foreach (var (slot, _) in rows) _slots[slot.Id] = slot;
        var ids = new HashSet<string>(_slots.Keys, StringComparer.Ordinal);

        // 父档合法才认这条血缘：指向不存在的档 / 指向自己 一律当根。
        var parentOf = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (slot, _) in rows)
        {
            var p = slot.ParentId;
            parentOf[slot.Id] = string.IsNullOrWhiteSpace(p) || p == slot.Id || !ids.Contains(p!) ? "" : p!;
        }

        var childrenOf = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var (id, parent) in parentOf)
        {
            if (parent.Length == 0) continue;
            if (!childrenOf.TryGetValue(parent, out var list)) childrenOf[parent] = list = new List<string>();
            list.Add(id);
        }

        var bySlot = rows.ToDictionary(r => r.Slot.Id, r => r, StringComparer.Ordinal);

        // 拿一棵树：根 + BFS 后代。visited 保证环不会重复也不会死循环。
        List<SaveTreeNode> Collect(string rootId, HashSet<string> visited)
        {
            var picked = new List<SaveTreeNode>();
            var queue = new Queue<string>();
            queue.Enqueue(rootId);
            visited.Add(rootId);
            while (queue.Count > 0)
            {
                var id = queue.Dequeue();
                if (bySlot.TryGetValue(id, out var row)) picked.Add(ToNode(row.Slot, row.CharacterName, parentOf[id]));
                if (!childrenOf.TryGetValue(id, out var kids)) continue;
                foreach (var k in kids)
                    if (visited.Add(k)) queue.Enqueue(k);
            }
            picked.Sort((a, b) =>
            {
                var c = a.SavedAt.CompareTo(b.SavedAt);
                return c != 0 ? c : string.CompareOrdinal(a.Id, b.Id);
            });
            return picked;
        }

        // 根 = 没有合法父档的存档，最新的排在前面（默认选它就等于默认选最近开的局）。
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var rootIds = rows.Where(r => parentOf[r.Slot.Id].Length == 0)
            .Select(r => r.Slot)
            .OrderByDescending(s => s.SavedAt)
            .Select(s => s.Id)
            .ToList();
        foreach (var id in rootIds)
        {
            var nodes = Collect(id, visited);
            _trees[id] = nodes;
            _rootOrder.Add(new RootOption(id, RootLabel(_slots[id]), nodes.Count));
        }

        // 环 / 完全断链的孤儿：按时间各成一棵，一样看得见、一样能切过去。
        foreach (var (slot, charName) in rows.OrderBy(r => r.Slot.SavedAt))
        {
            if (visited.Contains(slot.Id)) continue;
            var nodes = Collect(slot.Id, visited);
            _trees[slot.Id] = nodes;
            _rootOrder.Add(new RootOption(slot.Id, RootLabel(slot), nodes.Count));
        }
    }

    private static string RootLabel(SaveSlot slot)
    {
        var label = string.IsNullOrWhiteSpace(slot.Label) ? "未命名存档" : slot.Label;
        return $"{label} · {slot.SavedAt.ToLocalTime():MM-dd HH:mm}";
    }

    private SaveTreeNode ToNode(SaveSlot slot, string charName, string parentId)
    {
        var parentLabel = "";
        if (parentId.Length > 0 && _slots.TryGetValue(parentId, out var parent))
            parentLabel = string.IsNullOrWhiteSpace(parent.Label) ? "未命名存档" : parent.Label;

        return new SaveTreeNode(
            slot.Id,
            string.IsNullOrWhiteSpace(slot.Label) ? "未命名存档" : slot.Label,
            string.IsNullOrWhiteSpace(slot.BranchName) ? null : slot.BranchName,
            slot.SavedAt,
            parentId.Length == 0 ? null : parentId,
            string.IsNullOrWhiteSpace(charName) ? "未知角色" : charName,
            SceneText: string.IsNullOrWhiteSpace(slot.Scene) ? "—" : slot.Scene,
            WorldText: string.IsNullOrWhiteSpace(slot.WorldName)
                ? (string.IsNullOrWhiteSpace(slot.World) ? "普通酒馆（无地图）" : slot.World)
                : slot.WorldName,
            DurationText: FormatDuration(slot.PlayMinutes),
            SavedAtText: slot.SavedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
            ParentText: parentLabel.Length == 0 ? "无（起始存档）" : parentLabel);
    }

    private static string FormatDuration(int minutes)
    {
        if (minutes <= 0) return "还没记录";
        if (minutes < 60) return $"{minutes} 分钟";
        var h = minutes / 60;
        var m = minutes % 60;
        return m == 0 ? $"{h} 小时" : $"{h} 小时 {m} 分";
    }

    /// <summary>把某一棵树交给 Drawable：节点换一份，布局（列/泳道/内容尺寸）随之重算。</summary>
    private void ApplyTree(string? rootId)
    {
        if (string.IsNullOrEmpty(rootId) || !_trees.TryGetValue(rootId, out var nodes))
        {
            Drawable.Nodes = Array.Empty<SaveTreeNode>();
            return;
        }
        Drawable.Nodes = nodes;
    }

    partial void OnSelectedRootChanged(RootOption? value)
    {
        // ItemsSource 被换成新集合时，Picker 会先把 SelectedItem 置空 —— 那不是用户换树，别理它。
        if (value is null || _suppressRootChange) return;
        _preferredRootId = value?.Id ?? "";
        ApplyTree(value?.Id);
        if (!string.IsNullOrEmpty(_selectedId) && Drawable.Find(_selectedId) is null) SelectNode(null);
        // 切树后画布内容尺寸变了，页面要重设 GraphicsView 的宽高（由 AttachInvalidate 的回调负责）。
        Redraw();
    }

    /// <summary>压测条换了档：整棵树重读。</summary>
    public void ReloadWorstCase() => _ = RefreshAsync();

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        // 从存档页带 focus 进来时，选中那一档并切到它所属的那棵树。
        var focus = query.TryGetValue("focus", out var v) && v is string s
            ? Uri.UnescapeDataString(s) : "";
        _selectedId = focus;
        _ = RefreshAsync();
    }

    // ==================== 选中 ====================

    /// <summary>
    /// 选中一枚节点：详情面板由 Drawable 自己画在卡片旁（选中的那枚也算「有面板」），
    /// VM 只留标题给顶栏 / 无障碍朗读。
    /// </summary>
    private void SelectNode(string? id)
    {
        if (string.IsNullOrEmpty(id))
        {
            _selectedId = "";
            SelectedTitle = "";
            HasSelection = false;
            Drawable.SelectedId = null;
            Redraw();
            return;
        }

        var node = Drawable.Find(id);
        if (node is null) { SelectNode(null); return; }

        _selectedId = id;
        var isCurrent = false;
        try { isCurrent = string.Equals(_engine.CurrentSaveId, id, StringComparison.Ordinal); }
        catch (Exception ex) { App.WriteLog("SaveTree.IsCurrent -> " + ex.Message); }

        SelectedTitle = isCurrent ? node.Label + " · 当前档" : node.Label;
        HasSelection = true;
        Drawable.SelectedId = id;
        Redraw();
    }

    // ==================== 命令 ====================

    /// <summary>回存档页高亮这一档：读档门禁继续由 SavePage / SaveViewModel 负责，这里不重复实现。</summary>
    [RelayCommand]
    private async Task OpenSlot()
    {
        if (string.IsNullOrEmpty(_selectedId)) return;
        if (WorstCaseProvider.IsActive)
        {
            StatusText = "压测档只读 fixture：不跳存档页、不读真实档。";
            return;
        }
        try { await Shell.Current.GoToAsync($"save?focus={Uri.EscapeDataString(_selectedId)}"); }
        catch (Exception ex) { App.WriteLog("SaveTree.OpenSlot -> " + ex.Message); }
    }

    [RelayCommand]
    private async Task Back()
    {
        try { await Shell.Current.GoToAsync(".."); }
        catch (Exception ex) { App.WriteLog("SaveTree.Back -> " + ex.Message); }
    }

    /// <summary>顶栏根档下拉的一项：一棵树 = 一份初始存档。</summary>
    public sealed record RootOption(string Id, string Label, int Count)
    {
        public override string ToString() => Label;
    }
}
