using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WarmAsBefore.Models;
using WarmAsBefore.Services;

namespace WarmAsBefore.ViewModels;

/// <summary>
/// 世界树库：一份份「这个世界是什么样」。可以人写、可以让 AI 造、可以再让 AI 润色；
/// 改动即时落盘，下一次开局或「本局设定」里重新绑定就能用上。
///
/// 旧版世界书页只有「描述 → 生成」一条路：生成完看不全、也改不了。
/// 现在列表和编辑分开：列表负责挑，编辑负责改，AI 只是其中一个按钮。
/// </summary>
public sealed partial class WorldbookViewModel : ObservableObject
{
    private readonly WorldTreeService _worlds;
    private readonly GameEngine _engine;
    private readonly Modules.SaveSystem.SaveManager _save;
    private readonly HumanMemoryService _human;

    private bool _loaded;
    private WorldbookEntry? _editing;

    public ObservableCollection<WorldTreeCardItem> Items { get; } = new();
    public ObservableCollection<WorldCharacterDraft> EditorCharacters { get; } = new();
    public List<string> ModeChoices { get; } = new() { "纯文字", "带立绘" };

    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isEditing;
    [ObservableProperty] private bool _hasNoWorlds;
    [ObservableProperty] private string _aiPrompt = "";
    [ObservableProperty] private string _modeChoice = "纯文字";

    [ObservableProperty] private string _editTitle = "";
    [ObservableProperty] private string _editDescription = "";
    [ObservableProperty] private string _editIdea = "";
    [ObservableProperty] private string _editStatus = "";

    public bool HasNoEditorCharacters => EditorCharacters.Count == 0;

    public WorldbookViewModel(WorldTreeService worlds, GameEngine engine,
        Modules.SaveSystem.SaveManager save, HumanMemoryService human)
    {
        _worlds = worlds;
        _engine = engine;
        _save = save;
        _human = human;
    }

    public async Task LoadAsync()
    {
        if (_loaded && IsEditing) return;
        _loaded = true;
        await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        var list = await _worlds.ListAsync();
        Items.Clear();
        foreach (var w in list)
        {
            var bits = new List<string>();
            bits.Add(w.Characters.Count > 0 ? $"{w.Characters.Count} 位角色" : "没有角色");
            bits.Add(w.Mode == WorldbookMode.WithSprites ? "带立绘" : "纯文字");
            if (w.AiPolished) bits.Add("AI 润色过");
            Items.Add(new WorldTreeCardItem { Entry = w, Meta = string.Join(" · ", bits) });
        }
        HasNoWorlds = Items.Count == 0;
    }

    [RelayCommand]
    private async Task Generate()
    {
        if (IsBusy) return;
        if (string.IsNullOrWhiteSpace(AiPrompt))
        {
            Status = "先写一句你想要的世界。";
            return;
        }
        IsBusy = true;
        Status = "AI 正在造这个世界…";
        try
        {
            var mode = ModeChoice == "带立绘" ? WorldbookMode.WithSprites : WorldbookMode.TextOnly;
            var entry = await _worlds.GenerateAsync(AiPrompt.Trim(), mode);
            AiPrompt = "";
            await RefreshAsync();
            OpenEditor(entry);
            Status = $"「{entry.Title}」已建好。看完可以直接改，或者让 AI 再润色一遍。";
        }
        catch (Exception ex)
        {
            Status = "生成失败：" + ex.Message;
            App.WriteLog("Worldbook.Generate -> " + ex);
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private void NewManual()
    {
        var entry = new WorldbookEntry { Title = "未命名世界" };
        OpenEditor(entry);
        EditStatus = "还没有落盘。写好标题与设定，按「保存」。";
    }

    [RelayCommand]
    private async Task Edit(string? id)
    {
        if (string.IsNullOrEmpty(id)) return;
        var entry = await _worlds.GetAsync(id);
        if (entry is null)
        {
            Status = "这份世界树已经不在了。";
            await RefreshAsync();
            return;
        }
        OpenEditor(entry);
    }

    private void OpenEditor(WorldbookEntry entry)
    {
        _editing = entry;
        EditTitle = entry.Title;
        EditDescription = entry.Description;
        EditIdea = entry.UserDescription;
        EditStatus = "";
        EditorCharacters.Clear();
        foreach (var c in entry.Characters)
            EditorCharacters.Add(WorldCharacterDraft.From(c));
        OnPropertyChanged(nameof(HasNoEditorCharacters));
        IsEditing = true;
        Status = "";
    }

    [RelayCommand]
    private void AddCharacter()
    {
        EditorCharacters.Add(new WorldCharacterDraft { Gender = "女" });
        OnPropertyChanged(nameof(HasNoEditorCharacters));
    }

    [RelayCommand]
    private void RemoveCharacter(WorldCharacterDraft? draft)
    {
        if (draft is null) return;
        EditorCharacters.Remove(draft);
        OnPropertyChanged(nameof(HasNoEditorCharacters));
    }

    [RelayCommand]
    private void CloseEditor()
    {
        IsEditing = false;
        _editing = null;
        EditorCharacters.Clear();
        EditStatus = "";
    }

    [RelayCommand]
    private async Task SaveEdit()
    {
        if (_editing is null || IsBusy) return;
        var title = (EditTitle ?? "").Trim();
        if (title.Length == 0)
        {
            EditStatus = "标题不能空着——存档列表要靠它认出这一份世界。";
            return;
        }
        _editing.Title = title;
        _editing.Description = (EditDescription ?? "").Trim();
        _editing.UserDescription = (EditIdea ?? "").Trim();
        _editing.Characters.Clear();
        foreach (var d in EditorCharacters)
        {
            if (string.IsNullOrWhiteSpace(d.Name)) continue;
            _editing.Characters.Add(d.ToModel());
        }
        await _worlds.SaveAsync(_editing);
        await RefreshAsync();
        IsEditing = false;
        _editing = null;
        Status = "已保存。绑定它的存档会在下一次整理记忆时用上新设定。";
    }

    [RelayCommand]
    private async Task PolishEdit()
    {
        if (_editing is null || IsBusy) return;
        var title = (EditTitle ?? "").Trim();
        if (title.Length == 0)
        {
            EditStatus = "先给它一个标题。";
            return;
        }
        // 润色前先把当前手写的内容收进条目，AI 是在你写的基础上改，不是覆盖你的版本
        _editing.Title = title;
        _editing.Description = (EditDescription ?? "").Trim();
        _editing.UserDescription = (EditIdea ?? "").Trim();
        _editing.Characters.Clear();
        foreach (var d in EditorCharacters)
        {
            if (string.IsNullOrWhiteSpace(d.Name)) continue;
            _editing.Characters.Add(d.ToModel());
        }

        IsBusy = true;
        EditStatus = "AI 正在润色…没配模型时会原样保留。";
        try
        {
            var (ok, message) = await _worlds.PolishAsync(_editing);
            EditStatus = message;
            if (ok)
            {
                EditTitle = _editing.Title;
                EditDescription = _editing.Description;
                EditorCharacters.Clear();
                foreach (var c in _editing.Characters)
                    EditorCharacters.Add(WorldCharacterDraft.From(c));
                OnPropertyChanged(nameof(HasNoEditorCharacters));
                await RefreshAsync();
            }
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task Delete(string? id)
    {
        if (string.IsNullOrEmpty(id)) return;
        var entry = await _worlds.GetAsync(id);
        if (entry is null) return;
        var bound = string.Equals(_engine.State.WorldId, id, StringComparison.Ordinal);
        var text = bound
            ? $"「{entry.Title}」正绑在当前这一局上。删掉之后这一局会变回普通酒馆：地图锁上，世界里整理出的记忆也会清掉。"
            : $"「{entry.Title}」会从世界树库里删掉。已经绑定它的其它存档将失去世界设定。";
        if (!await Shell.Current.DisplayAlert("删除世界树", text, "删除", "算了")) return;

        if (bound)
        {
            _engine.State.WorldId = "";
            _engine.State.WorldName = "";
            try
            {
                await _human.RemoveTaggedCoreAsync(_engine.State.CharacterId, PersonaMemoryService.WorldTag);
                await _save.Commit("世界树解绑");
            }
            catch (Exception ex) { App.WriteLog("Worldbook.Delete.Unbind -> " + ex.Message); }
        }
        await _worlds.DeleteAsync(id);
        if (IsEditing && string.Equals(_editing?.Id, id, StringComparison.Ordinal))
        {
            IsEditing = false;
            _editing = null;
            EditorCharacters.Clear();
        }
        await RefreshAsync();
        Status = "已删除。";
    }

    [RelayCommand]
    private async Task GoBack()
    {
        if (IsEditing)
        {
            CloseEditor();
            return;
        }
        await Shell.Current.GoToAsync("..");
    }
}

/// <summary>列表上的一张世界树卡。字段是快照：列表刷新时才重算，滚动时不碰存储。</summary>
public sealed class WorldTreeCardItem
{
    public WorldbookEntry Entry { get; init; } = new();
    public string Id => Entry.Id;
    public string Title => string.IsNullOrWhiteSpace(Entry.Title) ? "未命名世界" : Entry.Title;
    public string Summary => WorldTreeService.SummaryOf(Entry);
    public bool HasSummary => !string.IsNullOrWhiteSpace(Summary);
    public string Meta { get; init; } = "";
}

/// <summary>编辑器里的一行角色：可改、可删；保存时才写回世界树。</summary>
public sealed partial class WorldCharacterDraft : ObservableObject
{
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _gender = "女";
    [ObservableProperty] private string _personality = "";
    [ObservableProperty] private string _description = "";

    public static WorldCharacterDraft From(WorldbookCharacter c) => new()
    {
        Name = c.Name,
        Gender = string.IsNullOrWhiteSpace(c.Gender) ? "女" : c.Gender,
        Personality = c.Personality,
        Description = c.Description
    };

    public WorldbookCharacter ToModel() => new()
    {
        Name = (Name ?? "").Trim(),
        Gender = string.IsNullOrWhiteSpace(Gender) ? "女" : Gender.Trim(),
        Personality = (Personality ?? "").Trim(),
        Description = (Description ?? "").Trim()
    };
}
