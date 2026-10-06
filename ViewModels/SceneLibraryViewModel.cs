using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.Controls;
using WarmAsBefore.Models;
using WarmAsBefore.Modules.Scene;
using WarmAsBefore.Services;

namespace WarmAsBefore.ViewModels;

/// <summary>
/// 模式列表的展示行：SceneMode 是持久化模型，不放显示逻辑，这里把字段格式化成一行可读文本。
/// 模式改动后由 SyncModes() 整体重建集合，因此这些计算属性不需要 INotifyPropertyChanged。
/// </summary>
public sealed class SceneModeRow
{
    public SceneModeRow(SceneMode mode, bool isActive)
    {
        Mode = mode;
        IsActive = isActive;
    }

    public SceneMode Mode { get; }

    /// <summary>是否为当前生效模式（切换后重建集合，所以取一次即可）。</summary>
    public bool IsActive { get; }

    public string Name => Mode.Name;

    /// <summary>时间区间文本；空 = 不受时间限制（任何时候都能被 AI 切）。</summary>
    public string TimeLabel => Mode.TimeRanges.Count == 0 ? "不限时间" : string.Join("、", Mode.TimeRanges);

    public string BackgroundLabel => string.IsNullOrEmpty(Mode.Background)
        ? "背景沿用条目"
        : Path.GetFileName(Mode.Background);

    public string BgmLabel => string.IsNullOrEmpty(Mode.Bgm) ? "不改音乐" : "已设 BGM";

    public string Detail =>
        $"灯光 {SceneDirector.LightingLabel(Mode.Lighting)}｜{TimeLabel}｜{BackgroundLabel}｜{BgmLabel}"
        + (IsActive ? "　【当前生效】" : "");
}

/// <summary>
/// 场景库页：管理库条目（场景 + 模式 + 变换规则）。
/// - 库条目：新建 / 命名 / 引用地图单场景（MapSceneRef）或直接给背景 / 备注。
/// - 模式：每个条目挂多个模式（背景/背景色/灯光/BGM/时间区间）。
/// - 变换规则（AiRules）：注入 AI 语境的「何时切换」提示文本。
/// - 时间触发 / 选项切换由 SceneDirector 执行；本页只做数据管理，并显示当前生效来源，
///   好让「AI 为什么切不动」（时间门控拦下 / 切了看不出变化）对用户可见。
/// </summary>
public sealed partial class SceneLibraryViewModel : ObservableObject
{
    private readonly SceneDirector _director;
    private readonly MapService _map;
    private readonly MaterialLibrary _materials;

    public SceneLibraryViewModel(SceneDirector director, MapService map, MaterialLibrary materials)
    {
        _director = director;
        _map = map;
        _materials = materials;
    }

    [ObservableProperty] private ObservableCollection<SceneLibraryEntry> _entries = new();

    [ObservableProperty] private SceneLibraryEntry? _selectedEntry;
    partial void OnSelectedEntryChanged(SceneLibraryEntry? value)
    {
        SyncModes();
        OnPropertyChanged(nameof(CanEdit));
    }

    [ObservableProperty] private ObservableCollection<SceneModeRow> _selectedModes = new();

    [ObservableProperty] private SceneModeRow? _selectedModeRow;
    partial void OnSelectedModeRowChanged(SceneModeRow? value)
    {
        OnPropertyChanged(nameof(SelectedMode));
        OnPropertyChanged(nameof(CanEditMode));
    }

    /// <summary>当前选中的模式（命令统一从它取数据）。</summary>
    public SceneMode? SelectedMode => SelectedModeRow?.Mode;

    public bool CanEdit => SelectedEntry is not null;
    public bool CanEditMode => SelectedModeRow is not null;

    /// <summary>当前生效来源与模式的可读摘要。</summary>
    [ObservableProperty] private string _stateText = "";

    public List<MapService.MapNodeRef> MapScenes =>
        _map.IsLoaded
            ? _map.Map.AllScenes.Select(s => new MapService.MapNodeRef(s.Id, s.Name, true)).ToList()
            : new List<MapService.MapNodeRef>();

    /// <summary>加载库到 UI（页面 OnAppearing 与每次改动后调用）。</summary>
    [RelayCommand]
    public void Refresh()
    {
        var keepId = SelectedEntry?.Id;
        Entries = new ObservableCollection<SceneLibraryEntry>(_director.Library);
        SelectedEntry = Entries.FirstOrDefault(e => e.Id == keepId) ?? Entries.FirstOrDefault();
        // SelectedEntry 没变时不会触发 OnSelectedEntryChanged，这里显式同步一次
        SyncModes();
        RefreshStateText();
    }

    /// <summary>
    /// 重建模式列表。旧实现只在选中条目时拷贝一次，模式改动后 UI 不刷新（新增/改名看不见），
    /// 这里每次改动后整表重建，并按 Id 还原选中项。
    /// </summary>
    private void SyncModes()
    {
        var keepId = SelectedModeRow?.Mode.Id;
        var active = _director.ActiveMode;
        var rows = (SelectedEntry?.Modes ?? new List<SceneMode>())
            .Select(m => new SceneModeRow(m, ReferenceEquals(active, m)))
            .ToList();
        SelectedModes = new ObservableCollection<SceneModeRow>(rows);
        SelectedModeRow = rows.FirstOrDefault(r => r.Mode.Id == keepId) ?? rows.FirstOrDefault();
    }

    /// <summary>顶部状态：当前生效来源与模式，解释「AI 为什么切不动」。</summary>
    private void RefreshStateText()
    {
        var e = _director.Active;
        if (e is null)
        {
            StateText = "当前无生效场景。时间命中或 AI 切换后会显示在这里。";
            return;
        }
        var name = _director.ActiveMode is null ? e.Name : $"{e.Name} · {_director.ActiveMode.Name}";
        var src = _director.Source switch
        {
            SceneApplySource.Time => $"时间规则 {_director.AppliedAt:HH:mm}",
            SceneApplySource.Ai => $"AI/指令 {_director.AppliedAt:HH:mm}",
            _ => "—"
        };
        var dim = _director.ActiveDim > 0.001 ? $"，亮度遮罩 {_director.ActiveDim:0.00}" : "";
        var ranges = _director.ActiveMode is { TimeRanges.Count: > 0 } am
            ? $"，受时间限制：{string.Join("、", am.TimeRanges)}"
            : "";
        StateText = $"当前生效：{name}（来源：{src}{dim}{ranges}）";
    }

    /// <summary>模式改动后统一收尾：落盘 + 重建模式列表 + 刷新状态文本。</summary>
    private async Task AfterModeEdit()
    {
        await _director.SaveAsync();
        SyncModes();
        RefreshStateText();
    }

    private SceneMode? ModeOf(SceneModeRow? row) => (row ?? SelectedModeRow)?.Mode;

    // ============ 条目 ============

    [RelayCommand]
    private async Task AddEntry()
    {
        var name = await Shell.Current.DisplayPromptAsync("新场景库条目", "命名", "未命名场景");
        if (string.IsNullOrWhiteSpace(name)) return;
        _director.AddEntry(name.Trim());
        await _director.SaveAsync();
        Refresh();
    }

    [RelayCommand]
    private async Task RenameEntry()
    {
        if (SelectedEntry is not { } e) return;
        var name = await Shell.Current.DisplayPromptAsync("重命名", "名称", e.Name);
        if (string.IsNullOrWhiteSpace(name)) return;
        e.Name = name.Trim();
        await _director.SaveAsync();
        Refresh();
    }

    [RelayCommand]
    private async Task RemoveEntry()
    {
        if (SelectedEntry is not { } e) return;
        if (!await Shell.Current.DisplayAlert("删除条目", $"确定删除「{e.Name}」及其全部模式吗？", "删除", "取消")) return;
        _director.RemoveEntry(e.Id);
        await _director.SaveAsync();
        Refresh();
    }

    [RelayCommand]
    private async Task AddMode()
    {
        if (SelectedEntry is not { } e) return;
        var name = await Shell.Current.DisplayPromptAsync("新模式", "模式名（如 夜灯 / 雨天）", "");
        if (string.IsNullOrWhiteSpace(name)) return;
        _director.AddMode(e, name.Trim());
        await AfterModeEdit();
    }

    /// <summary>引用地图单场景（MapSceneRef）：库条目直接复用地图里的场景。</summary>
    [RelayCommand]
    private async Task LinkMapScene()
    {
        if (SelectedEntry is not { } e) return;
        var scenes = MapScenes;
        if (scenes.Count == 0)
        {
            await Shell.Current.DisplayAlert("", "地图尚未加载", "好");
            return;
        }
        var options = scenes.Select(s => s.Name).Append("(取消)").ToArray();
        var pick = await Shell.Current.DisplayActionSheet("选择要引用的地图场景", "取消", null,
            options[..^1]);
        if (pick is null) return;
        var target = scenes.FirstOrDefault(s => s.Name == pick);
        if (!string.IsNullOrEmpty(target.Id))
        {
            e.MapSceneRef = target.Id;
            await _director.SaveAsync();
            Refresh();
        }
    }

    /// <summary>编辑变换规则（AiRules）：注入 AI 语境的「何时切换」提示。</summary>
    [RelayCommand]
    private async Task EditRules()
    {
        if (SelectedEntry is not { } e) return;
        var cur = e.AiRules ?? "";
        var text = await Shell.Current.DisplayPromptAsync(
            "变换规则", "例：下雨时切到室内；深夜自动关灯。", cur);
        if (text is null) return;
        e.AiRules = text.Trim();
        await _director.SaveAsync();
        OnPropertyChanged(nameof(Entries));
    }

    // ============ 模式 ============

    [RelayCommand]
    private async Task EditModeName(SceneModeRow? row)
    {
        if (ModeOf(row) is not { } m) return;
        var name = await Shell.Current.DisplayPromptAsync("模式命名", "如：夜灯 / 雨天 / 派对", m.Name);
        if (string.IsNullOrWhiteSpace(name)) return;
        m.Name = name.Trim();
        await AfterModeEdit();
    }

    /// <summary>选素材库背景；「沿用条目背景」= 清空（模式不覆盖背景）。</summary>
    [RelayCommand]
    private async Task EditModeBg(SceneModeRow? row)
    {
        if (ModeOf(row) is not { } m) return;
        await _materials.EnsureLoadedAsync();
        var items = _materials.BackgroundItems;
        if (items.Count == 0)
        {
            await Shell.Current.DisplayAlert("背景", "素材库里还没有背景，请先在「素材库」页导入。", "好");
            return;
        }
        const string clear = "（清除，沿用条目背景）";
        var pick = await Shell.Current.DisplayActionSheet("选择模式背景", "取消", null,
            items.Select(b => b.Name).Append(clear).ToArray());
        if (pick is null || pick == "取消") return;
        if (pick == clear) m.Background = "";
        else
        {
            var hit = items.FirstOrDefault(b => b.Name == pick);
            if (hit is null) return;
            // 存绝对路径：MainGamePage 用 ImageSource.FromFile 直接渲染（与地图场景背景一致）
            m.Background = _materials.ResolveAbs(hit.RelPath);
        }
        await AfterModeEdit();
    }

    /// <summary>选素材库音乐；「不改音乐」= 清空。存素材 Id，改名/移动文件都不会失联。</summary>
    [RelayCommand]
    private async Task EditModeBgm(SceneModeRow? row)
    {
        if (ModeOf(row) is not { } m) return;
        await _materials.EnsureLoadedAsync();
        var items = _materials.MusicItems;
        if (items.Count == 0)
        {
            await Shell.Current.DisplayAlert("BGM", "素材库里还没有音乐，请先在「素材库」页导入。", "好");
            return;
        }
        const string clear = "（清除，不改音乐）";
        var pick = await Shell.Current.DisplayActionSheet("选择模式 BGM", "取消", null,
            items.Select(x => x.Name).Append(clear).ToArray());
        if (pick is null || pick == "取消") return;
        if (pick == clear) m.Bgm = "";
        else
        {
            var hit = items.FirstOrDefault(x => x.Name == pick);
            if (hit is null) return;
            m.Bgm = hit.Id;
        }
        await AfterModeEdit();
    }

    /// <summary>灯光基调：dim 会压暗画面（其余两档不加遮罩，见 SceneDirector.DimForLighting）。</summary>
    [RelayCommand]
    private async Task EditModeLighting(SceneModeRow? row)
    {
        if (ModeOf(row) is not { } m) return;
        var labels = new[] { "暗 dim（压暗画面）", "正常 normal（不加遮罩）", "亮 bright（与正常等价）" };
        var cur = m.Lighting.Trim().ToLowerInvariant() switch
        {
            "dim" => labels[0],
            "bright" => labels[2],
            _ => labels[1]
        };
        var pick = await Shell.Current.DisplayActionSheet($"灯光基调（当前：{cur}）", "取消", null, labels);
        if (pick is null || pick == "取消") return;
        m.Lighting = pick switch
        {
            var p when p == labels[0] => "dim",
            var p when p == labels[2] => "bright",
            _ => "normal"
        };
        await AfterModeEdit();
    }

    /// <summary>
    /// 编辑时间区间：「22:00-06:00,12:00-13:00」（留空 = 不受时间限制）。
    /// 逐条校验并明确告知第几条错了；只要有一条不合法就整批不保存，避免半套规则写进去。
    /// </summary>
    [RelayCommand]
    private async Task EditModeTimes(SceneModeRow? row)
    {
        if (ModeOf(row) is not { } m) return;
        var cur = string.Join(",", m.TimeRanges);
        var text = await Shell.Current.DisplayPromptAsync(
            "时间区间", "逗号分隔，如 22:00-06:00,12:00-13:00（留空=不受时间限制）", cur);
        if (text is null) return;

        var raw = text.Trim();
        if (raw.Length == 0)
        {
            m.TimeRanges.Clear();
            await AfterModeEdit();
            return;
        }

        var items = raw.Split(new[] { ',', '，' }, StringSplitOptions.RemoveEmptyEntries)
                       .Select(s => s.Trim())
                       .Where(s => s.Length > 0)
                       .ToList();
        for (var i = 0; i < items.Count; i++)
        {
            if (SceneDirector.TryParseRange(items[i], out var from, out var to))
            {
                if (from != to) continue; // 起止相同 = 永远不生效，属于写错
                await Shell.Current.DisplayAlert("时间区间有误",
                    $"第 {i + 1} 条「{items[i]}」起止时间相同，永远不会生效。", "好");
                return;
            }
            await Shell.Current.DisplayAlert("时间区间有误",
                $"第 {i + 1} 条「{items[i]}」不是合法区间。正确写法：开始-结束（如 22:00-06:00），支持跨午夜。", "好");
            return;
        }
        m.TimeRanges = items;
        await AfterModeEdit();
    }

    [RelayCommand]
    private async Task RemoveMode(SceneModeRow? row)
    {
        if (ModeOf(row) is not { } m || SelectedEntry is not { } e) return;
        if (!await Shell.Current.DisplayAlert("删除模式", $"确定删除模式「{m.Name}」吗？", "删除", "取消")) return;
        _director.RemoveMode(e, m);
        await AfterModeEdit();
    }

    [RelayCommand]
    private void Back() => Shell.Current.GoToAsync("..");
}
