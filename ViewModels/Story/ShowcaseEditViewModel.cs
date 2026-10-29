using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WarmAsBefore.Controls;
using WarmAsBefore.Models;
using WarmAsBefore.Services;

namespace WarmAsBefore.ViewModels;

/// <summary>
/// 展示案编辑页：选角色、添加步骤（对话 / CG / 立绘多立绘）、临时素材导入。
/// 立绘编辑采用「舞台快照」策略：维护当前在场立绘集合，每次修改立绘时追加一步 sprites。
/// </summary>
public sealed partial class ShowcaseEditViewModel : ObservableObject
{
    private readonly Modules.Showcase.ShowcaseStore _store;
    private readonly CharacterLibrary _chars;
    private ShowcaseScript _script = new();
    private readonly List<ShowcaseSprite> _onScreen = new();
    private readonly List<CharacterData> _allCharacters = new();

    /// <summary>列表页导航到编辑页前写入的待载入脚本 id。</summary>
    public static string? PendingScriptId;

    [ObservableProperty] private string _scriptName = "";
    [ObservableProperty] private string _scriptDescription = "";
    [ObservableProperty] private string _characterName = "（未选择）";
    [ObservableProperty] private string _characterId = "";

    public ObservableCollection<ShowcaseStepDisplay> Steps { get; } = new();

    /// <summary>可供选择的立绘 key（outfit/emotion），由当前选定角色的 SpriteMap 键提供。</summary>
    public List<string> SpriteKeys { get; private set; } = new();

    public ShowcaseEditViewModel(Modules.Showcase.ShowcaseStore store, CharacterLibrary chars)
    {
        _store = store;
        _chars = chars;
    }

    [RelayCommand]
    private async Task LoadAsync(string? scriptId)
    {
        _allCharacters.Clear();
        _allCharacters.AddRange(await _chars.ListAsync());
        if (string.IsNullOrEmpty(scriptId))
        {
            _script = new ShowcaseScript();
            Steps.Clear();
            return;
        }
        var s = await _store.GetAsync(scriptId);
        _script = s ?? new ShowcaseScript();
        ScriptName = _script.Name;
        ScriptDescription = _script.Description;
        CharacterId = _script.CharacterId;
        CharacterName = string.IsNullOrWhiteSpace(_script.CharacterName)
            ? "（未选择）"
            : _script.CharacterName;
        _onScreen.Clear();
        Steps.Clear();
        foreach (var st in _script.Steps)
            AddStepDisplay(st);
        RefreshSpriteKeys();
    }

    [RelayCommand]
    private async Task PickCharacterAsync()
    {
        var names = _allCharacters.Select(c => c.Profile.Name).ToList();
        if (names.Count == 0)
        {
            Toast.Warn("角色库为空，请先在角色库中创建或导入角色。");
            return;
        }
        var choice = await Shell.Current.DisplayActionSheet("选择展示案主角", "取消", null, names.ToArray());
        if (choice == null) return;
        var idx = names.IndexOf(choice);
        if (idx < 0) return;
        var ch = _allCharacters[idx];
        _script.CharacterId = ch.Profile.Id;
        _script.CharacterName = ch.Profile.Name;
        CharacterId = ch.Profile.Id;
        CharacterName = ch.Profile.Name;
        RefreshSpriteKeys();
    }

    private void RefreshSpriteKeys()
    {
        var ch = _allCharacters.FirstOrDefault(c => c.Profile.Id == CharacterId);
        SpriteKeys = ch?.SpriteMap.Keys.OrderBy(k => k).ToList() ?? new List<string>();
        OnPropertyChanged(nameof(SpriteKeys));
    }

    [RelayCommand]
    private async Task AddDialogueAsync()
    {
        var who = await Shell.Current.DisplayActionSheet("谁说话？", "取消", null, $"角色（{CharacterName}）", "用户（你）");
        if (who == null) return;
        var speaker = who.Contains("角色") ? "character" : "user";
        var promptText = speaker == "character" ? "TA 想说的话：" : "你想说的话：";
        var text = await Shell.Current.DisplayPromptAsync("对话内容", promptText, "添加", "取消");
        if (string.IsNullOrWhiteSpace(text)) return;
        var step = new ShowcaseStep { Type = "dialogue", Speaker = speaker, Text = text.Trim() };
        AppendStep(step);
    }

    [RelayCommand]
    private async Task InsertCgAsync()
    {
        var pick = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "选择 CG 图片" });
        if (pick is null) return;
        // 需要先确保存在以素材导入
        if (string.IsNullOrEmpty(_script.Id))
            _script = _script with { Id = Guid.NewGuid().ToString("N")[..10], CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        var rel = await _store.ImportMaterialAsync(_script.Id, pick.FullPath);
        if (rel == null)
        {
            Toast.Error("素材复制失败。");
            return;
        }
        var step = new ShowcaseStep { Type = "cg", CgPath = rel };
        AppendStep(step);
    }

    [RelayCommand]
    private async Task AddSpriteAsync()
    {
        if (SpriteKeys.Count == 0)
        {
            Toast.Info("请先选择一个拥有立绘素材的角色。");
            return;
        }
        var key = await Shell.Current.DisplayActionSheet("选择立绘", "取消", null, SpriteKeys.ToArray());
        // ActionSheet 点「取消」返回的是字符串「取消」，不是 null：不判这句，
        // 取消会一路走下去，往展示案里塞一张空图片的立绘。
        if (string.IsNullOrEmpty(key) || key == "取消") return;
        var pos = await Shell.Current.DisplayActionSheet("立绘位置", "取消", null, "左", "中", "右");
        if (string.IsNullOrEmpty(pos) || pos == "取消") return;
        var ch = _allCharacters.FirstOrDefault(c => c.Profile.Id == CharacterId);
        var path = ch?.SpriteMap.TryGetValue(key, out var p) == true ? p : "";
        var position = pos switch { "左" => "left", "右" => "right", _ => "center" };
        var sprite = new ShowcaseSprite { Path = path, Position = position, Opacity = 1.0 };
        // 同位置替换，多立绘不堆叠同一位置
        _onScreen.RemoveAll(s => s.Position == position);
        _onScreen.Add(sprite);
        var step = new ShowcaseStep { Type = "sprites", Sprites = new List<ShowcaseSprite>(_onScreen) };
        AppendStep(step);
    }

    [RelayCommand]
    private void ClearSprites()
    {
        if (_onScreen.Count == 0) return;
        _onScreen.Clear();
        AppendStep(new ShowcaseStep { Type = "sprites", Sprites = new List<ShowcaseSprite>() });
    }

    [RelayCommand]
    private async Task ImportTempMaterialAsync()
    {
        var pick = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "导入临时素材（用于背景或自定义 CG）" });
        if (pick is null) return;
        if (string.IsNullOrEmpty(_script.Id))
            _script = _script with { Id = Guid.NewGuid().ToString("N")[..10], CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        var rel = await _store.ImportMaterialAsync(_script.Id, pick.FullPath);
        if (rel is null) Toast.Error("导入失败。");
        else Toast.Success($"已导入：{Path.GetFileName(rel)}");
    }

    private void AppendStep(ShowcaseStep step)
    {
        _script.Steps.Add(step);
        AddStepDisplay(step);
    }

    private void AddStepDisplay(ShowcaseStep step)
    {
        var disp = new ShowcaseStepDisplay { Step = step };
        disp.RefreshLabel(_script.CharacterName);
        Steps.Add(disp);
    }

    [RelayCommand]
    private async Task MoveUpAsync(ShowcaseStepDisplay? disp)
    {
        if (disp is null) return;
        var idx = Steps.IndexOf(disp);
        if (idx <= 0) return;
        Steps.Move(idx, idx - 1);
        SyncStepsFromDisplays();
    }

    [RelayCommand]
    private async Task MoveDownAsync(ShowcaseStepDisplay? disp)
    {
        if (disp is null) return;
        var idx = Steps.IndexOf(disp);
        if (idx < 0 || idx >= Steps.Count - 1) return;
        Steps.Move(idx, idx + 1);
        SyncStepsFromDisplays();
    }

    [RelayCommand]
    private async Task DeleteStepAsync(ShowcaseStepDisplay? disp)
    {
        if (disp is null) return;
        var idx = Steps.IndexOf(disp);
        if (idx < 0) return;
        Steps.RemoveAt(idx);
        SyncStepsFromDisplays();
    }

    private void SyncStepsFromDisplays()
    {
        _script.Steps = Steps.Select(d => d.Step).ToList();
        // 刷新显示索引（可选）
        for (int i = 0; i < Steps.Count; i++)
            Steps[i].RefreshLabel(_script.CharacterName);
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        _script.Name = ScriptName;
        _script.Description = ScriptDescription;
        _script.Steps = Steps.Select(d => d.Step).ToList();
        _script.UpdatedAt = DateTime.UtcNow;
        await _store.UpsertAsync(_script);
        Toast.Success("展示案已保存");
    }

    [RelayCommand]
    private async Task BackAsync() => await Shell.Current.GoToAsync("..");
}
