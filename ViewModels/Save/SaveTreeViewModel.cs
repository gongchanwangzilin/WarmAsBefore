using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.Graphics;
using WarmAsBefore.Drawables;
using WarmAsBefore.Models;
using WarmAsBefore.Modules.SaveSystem;
using WarmAsBefore.Services;
using WarmAsBefore.Services.Core;

namespace WarmAsBefore.ViewModels;

/// <summary>
/// 分支树独立页的 VM：把 SaveManager.List() 的血缘数据（ParentId / BranchName / SavedAt）
/// 连同角色库的名字组装成 SaveTreeNode 交给 SaveTreeDrawable，页面只管画布命中与详情条。
/// 读档不在这里实现：点「去这个存档」只是回到 SavePage 并带 focus 参数，复用原有 LoadSlot 门禁。
/// 所有展示性读取都各自 try/catch，任何一路服务出错只让这一处空着，不把整页带崩。
/// </summary>
public sealed partial class SaveTreeViewModel : ObservableObject, IQueryAttributable
{
    private readonly SaveManager _save;
    private readonly CharacterLibrary _library;
    private readonly GameEngine _engine;

    /// <summary>供详情条取父档名 / 分叉时间：Drawable 的节点记录字段有限，原始 SaveSlot 留一份。</summary>
    private readonly Dictionary<string, SaveSlot> _slots = new(StringComparer.Ordinal);

    private Action? _invalidate;
    private bool _loading;
    private string _selectedId = "";

    /// <summary>绘制器实例：XAML 里只放 GraphicsView，Drawable 由页面从 VM 取（照 SuperMonitor 的套路）。</summary>
    public SaveTreeDrawable Drawable { get; } = new();

    [ObservableProperty] private bool _hasSelection;
    [ObservableProperty] private TreeDetail? _selected;
    [ObservableProperty] private string _statusText = "";

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

    public void SetViewport(double w, double h)
    {
        try
        {
            Drawable.SetCanvasSize(w, h);
            Redraw();
        }
        catch (Exception ex) { App.WriteLog("SaveTree.SetViewport -> " + ex.Message); }
    }

    /// <summary>画布 EndInteraction 的落点（相对画布左上角）→ 命中节点 → 详情条。</summary>
    public void CanvasTap(PointF point)
    {
        try
        {
            var id = Drawable.HitTest(point.X, point.Y);
            SelectNode(id);
        }
        catch (Exception ex) { App.WriteLog("SaveTree.CanvasTap -> " + ex.Message); }
    }

    // ==================== 数据组装 ====================

    /// <summary>
    /// 读一次权威数据：存档列表 + 角色库并行取，按 SaveSlot 的 ParentId 组出血缘节点。
    /// ParentId 指向不存在的档时按根节点处理（老档 / 坏档都会遇到），不交给 Drawable 去猜。
    /// </summary>
    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (_loading) return;
        _loading = true;
        try
        {
            var listTask = _save.List();
            var rosterTask = _library.ListAsync();
            await Task.WhenAll(listTask, rosterTask);
            var list = await listTask;
            var roster = await rosterTask;

            _slots.Clear();
            foreach (var slot in list)
                _slots[slot.Id] = slot;

            var ids = new HashSet<string>(_slots.Keys, StringComparer.Ordinal);
            var nodes = new List<SaveTreeNode>(list.Count);
            foreach (var slot in list)
            {
                // 父档不存在 = 根节点（契约「数据」节：ParentId 空/指向不存在的档）。
                var parent = string.IsNullOrWhiteSpace(slot.ParentId) || !ids.Contains(slot.ParentId!)
                    ? null : slot.ParentId;
                var branch = string.IsNullOrWhiteSpace(slot.BranchName) ? null : slot.BranchName;
                var label = string.IsNullOrWhiteSpace(slot.Label) ? "未命名存档" : slot.Label;
                var charName = roster.FirstOrDefault(c => c.Profile.Id == slot.Character)?.Profile.Name ?? "未知角色";
                nodes.Add(new SaveTreeNode(slot.Id, label, branch, slot.SavedAt, parent, charName));
            }

            Drawable.Nodes = nodes;
            try { Drawable.CurrentSlotId = _engine.CurrentSaveId; }
            catch (Exception ex) { App.WriteLog("SaveTree.CurrentSlot -> " + ex.Message); }

            var roots = nodes.Count(n => n.ParentId is null);
            StatusText = nodes.Count == 0
                ? "还没有存档分支"
                : $"共 {nodes.Count} 个存档 · {roots} 条起始分支";

            // 刷新后沿用原选中项；节点已经没了就清空，避免详情条残留旧档。
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

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        // 路由目前不带参数；预留 focus 让将来能从别处直接高亮某个档。
        var focus = query.TryGetValue("focus", out var v) && v is string s
            ? Uri.UnescapeDataString(s) : "";
        _selectedId = focus;
        _ = RefreshAsync();
    }

    // ==================== 选中与详情 ====================

    private void SelectNode(string? id)
    {
        if (string.IsNullOrEmpty(id))
        {
            _selectedId = "";
            Selected = null;
            HasSelection = false;
            Drawable.SelectedId = null;
            Redraw();
            return;
        }

        var node = Drawable.Find(id);
        if (node is null)
        {
            SelectNode(null);
            return;
        }

        _selectedId = id;
        var isCurrent = false;
        try { isCurrent = string.Equals(_engine.CurrentSaveId, id, StringComparison.Ordinal); }
        catch (Exception ex) { App.WriteLog("SaveTree.IsCurrent -> " + ex.Message); }

        _slots.TryGetValue(id, out var slot);
        var parentLabel = "";
        if (!string.IsNullOrEmpty(node.ParentId) && _slots.TryGetValue(node.ParentId!, out var parent))
            parentLabel = string.IsNullOrWhiteSpace(parent.Label) ? "未命名存档" : parent.Label;

        var forked = slot?.ForkedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "";
        Selected = new TreeDetail(
            id,
            isCurrent ? node.Label + " · 当前档" : node.Label,
            string.IsNullOrWhiteSpace(node.BranchName) ? "无分支" : node.BranchName!,
            string.IsNullOrWhiteSpace(node.CharacterName) ? "未知角色" : node.CharacterName,
            node.SavedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
            string.IsNullOrEmpty(parentLabel) ? "无（起始存档）" : parentLabel,
            forked);
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
        try { await Shell.Current.GoToAsync($"save?focus={Uri.EscapeDataString(_selectedId)}"); }
        catch (Exception ex) { App.WriteLog("SaveTree.OpenSlot -> " + ex.Message); }
    }

    [RelayCommand]
    private async Task Back()
    {
        try { await Shell.Current.GoToAsync(".."); }
        catch (Exception ex) { App.WriteLog("SaveTree.Back -> " + ex.Message); }
    }

    /// <summary>详情条的一行：所有文字在 VM 里格式化好，XAML 只负责摆位。</summary>
    public sealed record TreeDetail(
        string Id,
        string Title,
        string BranchText,
        string CharacterText,
        string SavedAtText,
        string ParentText,
        string ForkedAtText);
}
