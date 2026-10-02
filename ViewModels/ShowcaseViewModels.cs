using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WarmAsBefore.Models;
using WarmAsBefore.Services;

namespace WarmAsBefore.ViewModels;

/// <summary>展示案例表页：管理展示案例列表，新建/编辑/播放/删除。</summary>
public sealed partial class ShowcaseListViewModel : ObservableObject
{
    private readonly Modules.Showcase.ShowcaseStore _store;

    public ObservableCollection<ShowcaseScript> Scripts { get; } = new();

    public ShowcaseListViewModel(Modules.Showcase.ShowcaseStore store)
    {
        _store = store;
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        var all = await _store.LoadAsync();
        Scripts.Clear();
        foreach (var s in all.OrderByDescending(s => s.UpdatedAt))
            Scripts.Add(s);
    }

    [RelayCommand]
    private async Task NewAsync()
    {
        var name = await Shell.Current.DisplayPromptAsync("新建展示案", "展示案名称：", "创建", "取消", "我的第一段演出");
        if (string.IsNullOrWhiteSpace(name)) return;
        var script = new ShowcaseScript
        {
            Name = name.Trim(),
            Description = "",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        if (!await _store.UpsertAsync(script))
        {
            await Shell.Current.DisplayAlert("新建失败", "写入失败，重试", "好");
            return;
        }
        await LoadAsync();
        ShowcaseEditViewModel.PendingScriptId = script.Id;
        await Shell.Current.GoToAsync("showcase-edit");
    }

    [RelayCommand]
    private async Task EditAsync(ShowcaseScript? script)
    {
        if (script is null) return;
        ShowcaseEditViewModel.PendingScriptId = script.Id;
        await Shell.Current.GoToAsync("showcase-edit");
    }

    [RelayCommand]
    private async Task PlayAsync(ShowcaseScript? script)
    {
        if (script is null) return;
        if (script.Steps.Count == 0)
        {
            await Shell.Current.DisplayAlert("播放", "该展示案还没有任何步骤，无法播放。", "好");
            return;
        }
        ShowcasePlayViewModel.PendingScriptId = script.Id;
        await Shell.Current.GoToAsync("showcase-play");
    }

    [RelayCommand]
    private async Task DeleteAsync(ShowcaseScript? script)
    {
        if (script is null) return;
        var confirm = await Shell.Current.DisplayActionSheet($"确定删除「{script.Name}」？", "取消", null, "删除");
        if (confirm != "删除") return;
        await _store.DeleteAsync(script.Id);
        await LoadAsync();
    }

    [RelayCommand]
    private async Task BackAsync() => await Shell.Current.GoToAsync("..");
}

/// <summary>展示案例步骤：编辑器中用于回显/上移/下移/删除的包装。</summary>
public sealed partial class ShowcaseStepDisplay : ObservableObject
{
    public ShowcaseStep Step { get; init; } = new();
    [ObservableProperty] private string _label = "";
    [ObservableProperty] private string _sub = "";

    /// <summary>重新计算显示文本。</summary>
    public void RefreshLabel(string characterName)
    {
        Label = Step.Type switch
        {
            "dialogue" => Step.Speaker == "user" ? $"💬 你：{Truncate(Step.Text,30)}" : $"💬 {characterName}：{Truncate(Step.Text,30)}",
            "cg" => $"🖼️ CG · {Path.GetFileName(Step.CgPath)}",
            "sprites" => $"✨ 立绘 ×{Step.Sprites.Count}",
            _ => Step.Type
        };
        Sub = Step.Type switch
        {
            "dialogue" => "",
            "cg" => Step.CgPath,
            "sprites" => string.Join(" + ", Step.Sprites.Select(s => $"{PosCn(s.Position)}×1")),
            _ => ""
        };
    }

    private static string PosCn(string p) => p switch { "left" => "左", "right" => "右", _ => "中" };
    private static string Truncate(string s, int n) => s.Length <= n ? s : s[..n] + "…";
}

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
            await Shell.Current.DisplayAlert("角色", "角色库为空，请先在角色库中创建或导入角色。", "好");
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
        var promptText = speaker == "character" ? "她想说的话：" : "你想说的话：";
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
            await Shell.Current.DisplayAlert("导入失败", "素材复制失败。", "好");
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
            await Shell.Current.DisplayAlert("立绘", "请先选择一个拥有立绘素材的角色。", "好");
            return;
        }
        var key = await Shell.Current.DisplayActionSheet("选择立绘", "取消", null, SpriteKeys.ToArray());
        if (key == null) return;
        var pos = await Shell.Current.DisplayActionSheet("立绘位置", "取消", null, "左", "中", "右");
        if (pos == null) return;
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
        await Shell.Current.DisplayAlert("导入完成", rel is null ? "失败" : $"已导入：{Path.GetFileName(rel)}", "好");
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
        await Shell.Current.DisplayAlert("保存", "展示案已保存", "好");
    }

    [RelayCommand]
    private async Task BackAsync() => await Shell.Current.GoToAsync("..");
}

/// <summary>
/// 展示案播放页：依次执行步骤，打字机显示对话、立绘叠加、CG 全屏覆盖，自动模拟。
/// </summary>
public sealed partial class ShowcasePlayViewModel : ObservableObject
{
    private readonly Modules.Showcase.ShowcaseStore _store;
    public ShowcaseScript Script { get; private set; } = new();
    private int _index;

    /// <summary>列表页导航到播放页前写入的待播放脚本 id。</summary>
    public static string? PendingScriptId;

    [ObservableProperty] private bool _dialogueVisible;
    [ObservableProperty] private string _speakerName = "";
    [ObservableProperty] private string _dialogueText = "";
    [ObservableProperty] private bool _cgVisible;
    [ObservableProperty] private ImageSource? _cgSource;
    [ObservableProperty] private string _progressText = "";
    [ObservableProperty] private bool _isFinished;
    [ObservableProperty] private bool _bgVisible;
    [ObservableProperty] private ImageSource? _bgSource;
    [ObservableProperty] private bool _isTyping;
    private CancellationTokenSource? _typeCts;
    private bool _skipTyping;

    public ObservableCollection<ShowcaseSprite> Sprites { get; } = new();
    /// <summary>三列立绘渲染用的子集合（LeftSprites / CenterSprites / RightSprites）。</summary>
    public ObservableCollection<ShowcaseSprite> LeftSprites { get; } = new();
    public ObservableCollection<ShowcaseSprite> CenterSprites { get; } = new();
    public ObservableCollection<ShowcaseSprite> RightSprites { get; } = new();

    public ShowcasePlayViewModel(Modules.Showcase.ShowcaseStore store) => _store = store;

    [RelayCommand]
    private async Task LoadAsync(string? scriptId)
    {
        if (string.IsNullOrEmpty(scriptId)) return;
        Script = await _store.GetAsync(scriptId) ?? new ShowcaseScript();
        _index = -1;
        DialogueVisible = false;
        CgVisible = false;
        IsFinished = false;
        Sprites.Clear(); UpdatePositional();
        ProgressText = $"0/{Script.Steps.Count}";
        if (!string.IsNullOrEmpty(Script.BackgroundPath))
        {
            var bg = Modules.Showcase.ShowcaseStore.FullPath(Script.BackgroundPath);
            if (File.Exists(bg)) { BgSource = ImageSource.FromFile(bg); BgVisible = true; }
        }
        await AdvanceAsync();
    }

    [RelayCommand]
    private async Task AdvanceAsync()
    {
        if (IsFinished) return;
        // 结束上一步
        CgVisible = false;
        DialogueVisible = false;
        // 如果正在打字，先跳过到结尾
        if (_skipTyping) { _skipTyping = false; return; }

        _index++;
        if (_index >= Script.Steps.Count)
        {
            IsFinished = true;
            ProgressText = "播放结束";
            return;
        }

        var step = Script.Steps[_index];
        ProgressText = $"{_index + 1}/{Script.Steps.Count}";
        switch (step.Type)
        {
            case "dialogue":
                DialogueVisible = true;
                CgVisible = false;
                SpeakerName = step.Speaker == "user" ? "你" : (string.IsNullOrEmpty(Script.CharacterName) ? "角色" : Script.CharacterName);
                await TypewriteAsync(step.Text);
                break;
            case "cg":
                DialogueVisible = false;
                CgVisible = true;
                var cgPath = Modules.Showcase.ShowcaseStore.FullPath(step.CgPath);
                CgSource = File.Exists(cgPath) ? ImageSource.FromFile(cgPath) : null;
                break;
            case "sprites":
                Sprites.Clear();
                foreach (var sp in step.Sprites) Sprites.Add(sp);
                UpdatePositional();
                break;
        }

        // 对话结束后稍作等待自动进入下一步，或用户点任意处触发下一步
    }

    [RelayCommand]
    private void AdvanceTap()
    {
        if (IsTyping)
        {
            _skipTyping = true;
        }
        else if (!IsFinished)
        {
            _ = AdvanceAsync();
        }
    }

    private async Task TypewriteAsync(string text)
    {
        _typeCts?.Cancel();
        var cts = new CancellationTokenSource();
        _typeCts = cts;
        IsTyping = true;
        DialogueText = "";
        _skipTyping = false;
        var chs = text.ToCharArray();
        for (int i = 0; i < chs.Length; i++)
        {
            if (_skipTyping || cts.Token.IsCancellationRequested)
            {
                DialogueText = text;
                break;
            }
            DialogueText += chs[i];
            try { await Task.Delay(30, cts.Token); }
            catch (OperationCanceledException) { break; }
        }
        IsTyping = false;
    }

    private void UpdatePositional()
    {
        LeftSprites.Clear(); CenterSprites.Clear(); RightSprites.Clear();
        foreach (var sp in Sprites)
        {
            (sp.Position switch { "left" => LeftSprites, "right" => RightSprites, _ => CenterSprites }).Add(sp);
        }
    }

    [RelayCommand]
    private async Task BackAsync() => await Shell.Current.GoToAsync("..");
}