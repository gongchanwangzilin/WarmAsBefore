using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WarmAsBefore.Modules.SaveSystem;
using WarmAsBefore.Services;

namespace WarmAsBefore.ViewModels;

/// <summary>
/// 新开一局之前的那一屏：这段关系发生在哪个世界，以及「你是谁」。
///
/// 世界树可以不绑，但绑不绑决定了这一局有没有地图：普通酒馆没有「发生在哪儿」，
/// 提示词里就不该出现一张不存在的地图。人设同理——它不直接进上下文，
/// 而是在这里被整理成一条条记忆，之后由记忆层决定什么时候想起。
///
/// 同一个页面也承担「本局设定」：已经在跑的这一局可以回来改人设、换绑世界树，
/// 改完重新整理一次记忆。
///
/// 新建走 OOBE：世界 → 人设 → 细则 → 回顾，一次只问一件事；
/// 步数账本与 SetupViewModel 同一套（Position + AtXxx + _steps），不另起节奏。
/// 「本局设定」不是首次配置，仍旧一屏到底；设置里关掉 OOBE 也回到同一屏。
/// </summary>
public sealed partial class NewRunViewModel : ObservableObject
{
    private const int StepWorld = 0, StepPersona = 1, StepRules = 2, StepReview = 3;
    private static readonly int[] StepIds = { StepWorld, StepPersona, StepRules, StepReview };
    private static readonly string[] StepNames = { "世界", "人设", "细则", "开始" };

    private readonly GameEngine _engine;
    private readonly CharacterLibrary _chars;
    private readonly SaveManager _save;
    private readonly WorldTreeService _worlds;
    private readonly PersonaMemoryService _persona;
    private readonly SettingsManager _settings;

    private string _characterId = "";
    private bool _loaded;
    private bool _editMode;

    public ObservableCollection<WorldChoiceItem> Worlds { get; } = new();

    [ObservableProperty] private string _pageTitle = "新的一局";
    [ObservableProperty] private string _characterName = "";
    [ObservableProperty] private string _subtitle = "";
    [ObservableProperty] private string _personaText = "";
    [ObservableProperty] private string _selectedWorldId = "";
    [ObservableProperty] private string _selectedWorldName = "";
    [ObservableProperty] private string _primaryText = "开始这一局";
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _isEditMode;
    [ObservableProperty] private bool _hasNoWorlds;

    // ── 分步 ──
    [ObservableProperty] private bool _isOobe = true;
    [ObservableProperty] private int _position;
    [ObservableProperty] private bool _atWorld = true, _atPersona, _atRules, _atReview;
    [ObservableProperty] private bool _canGoBack;
    [ObservableProperty] private string _stepEyebrow = "1 / 4";
    [ObservableProperty] private string _stepLabel = "世界";
    [ObservableProperty] private bool _reached1 = true, _reached2, _reached3, _reached4;

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _loadingTitle = "人格记忆初始化中";
    [ObservableProperty] private string _loadingDetail = "正在准备…";
    [ObservableProperty] private double _loadingProgress;
    [ObservableProperty] private string _loadingPercent = "0%";

    public NewRunViewModel(GameEngine engine, CharacterLibrary chars, SaveManager save,
        WorldTreeService worlds, PersonaMemoryService persona, SettingsManager settings)
    {
        _engine = engine;
        _chars = chars;
        _save = save;
        _worlds = worlds;
        _persona = persona;
        _settings = settings;
        // 构造时就定下来，切页不闪一下「分步」再退回单页
        _isOobe = settings.Current.SaveCreationOobe;
    }

    // ── 步骤视图的显隐：单页模式一次摊开全部内容，仍是同一份数据 ──
    public bool ShowWorldBlock => !IsOobe || AtWorld;
    public bool ShowPersonaBlock => !IsOobe || AtPersona;
    public bool ShowRulesBlock => !IsOobe || AtRules;
    public bool ShowReviewBlock => !IsOobe || AtReview;
    public bool IsLastStep => Position >= StepIds.Length - 1;
    public bool ShowNext => IsOobe && !AtReview;
    public bool ShowBegin => !IsOobe || AtReview;

    public string SelectedWorldDisplay => string.IsNullOrEmpty(SelectedWorldId)
        ? "不绑定 · 普通酒馆"
        : string.IsNullOrWhiteSpace(SelectedWorldName) ? "已选的世界" : SelectedWorldName;

    public bool HasPersona => !string.IsNullOrWhiteSpace(PersonaText);
    public string PersonaCount
    {
        get
        {
            var n = (PersonaText ?? "").Trim().Length;
            return n == 0 ? "留空也可以" : $"{n} 字";
        }
    }

    /// <summary>这一步决定的不只是地点：没绑世界树就没有地图，这句话在细则与回顾里都要看得见。</summary>
    public string MapHint => string.IsNullOrEmpty(SelectedWorldId)
        ? "地图锁定 · 这一局只有对话"
        : "地图可用 · 地点来自世界书";

    public async Task LoadAsync(string characterId, bool edit)
    {
        if (_loaded)
        {
            if (!IsBusy) await LoadWorldsAsync();
            return;
        }
        _loaded = true;
        _editMode = edit;
        IsEditMode = edit;
        // 本局设定是回来改两样东西，不是首次配置：不分步
        IsOobe = !edit && _settings.Current.SaveCreationOobe;
        _characterId = string.IsNullOrWhiteSpace(characterId) ? _engine.State.CharacterId : characterId;

        var data = (await _chars.ListAsync()).FirstOrDefault(c => c.Profile.Id == _characterId);
        CharacterName = data?.Profile.Name ?? "TA";
        PageTitle = edit ? "本局设定" : "新的一局";
        PrimaryText = edit ? "保存本局设定" : "开始这一局";
        Subtitle = edit
            ? "改完会重新整理一次记忆；已经聊过的内容不会丢。"
            : $"和「{CharacterName}」的这段相处，从哪里开始？";

        if (edit)
        {
            PersonaText = _engine.State.UserPersona;
            SelectedWorldId = _engine.State.WorldId;
            SelectedWorldName = _engine.State.WorldName;
        }
        await LoadWorldsAsync();
    }

    private async Task LoadWorldsAsync()
    {
        var keep = SelectedWorldId;
        Worlds.Clear();
        Worlds.Add(new WorldChoiceItem
        {
            Id = "",
            Title = "不绑定 · 普通酒馆",
            Summary = "只聊天，没有地图。TA 不会假装知道你们在哪儿。",
            Marker = "无地图",
            IsSelected = string.IsNullOrEmpty(keep)
        });
        foreach (var w in await _worlds.ListAsync())
        {
            Worlds.Add(new WorldChoiceItem
            {
                Id = w.Id,
                Title = string.IsNullOrWhiteSpace(w.Title) ? "未命名世界" : w.Title,
                Summary = WorldTreeService.SummaryOf(w),
                Marker = w.Characters.Count > 0 ? $"{w.Characters.Count} 位角色" : "世界书",
                IsSelected = string.Equals(w.Id, keep, StringComparison.Ordinal)
            });
        }
        HasNoWorlds = Worlds.Count <= 1;
        if (Worlds.All(w => !w.IsSelected))
        {
            Worlds[0].IsSelected = true;
            SelectedWorldId = "";
        }
        SyncWorldName();
    }

    /// <summary>选中行与底部摘要读同一个名字：换选之后摘要不能还停在上一个世界。</summary>
    private void SyncWorldName()
    {
        var hit = Worlds.FirstOrDefault(w => string.Equals(w.Id, SelectedWorldId, StringComparison.Ordinal));
        SelectedWorldName = hit is null || string.IsNullOrEmpty(hit.Id) ? "" : hit.Title;
    }

    [RelayCommand]
    private void SelectWorld(string? id)
    {
        SelectedWorldId = id ?? "";
        foreach (var w in Worlds)
            w.IsSelected = string.Equals(w.Id, SelectedWorldId, StringComparison.Ordinal);
        SyncWorldName();
    }

    [RelayCommand]
    private async Task ManageWorlds() => await Shell.Current.GoToAsync("worldbook");

    [RelayCommand]
    private async Task Back()
    {
        if (IsBusy) return;
        await Shell.Current.GoToAsync("..");
    }

    // ── 走 ──────────────────────────────────────────────────────────────────

    [RelayCommand]
    private void NextStep()
    {
        if (!IsOobe) return;
        if (Position < StepIds.Length - 1) Position++;
    }

    [RelayCommand]
    private void PrevStep()
    {
        if (Position > 0) Position--;
    }

    /// <summary>细则里那两个「改」：只换步，不提交任何东西。</summary>
    [RelayCommand]
    private void JumpStep(string? index)
    {
        if (!int.TryParse(index, out var i)) return;
        Position = Math.Clamp(i, 0, StepIds.Length - 1);
    }

    partial void OnPositionChanged(int value)
    {
        var id = StepIds[Math.Clamp(value, 0, StepIds.Length - 1)];
        AtWorld = id == StepWorld;
        AtPersona = id == StepPersona;
        AtRules = id == StepRules;
        AtReview = id == StepReview;
        CanGoBack = value > 0;
        StepEyebrow = $"{value + 1} / {StepIds.Length}";
        StepLabel = StepNames[Math.Clamp(value, 0, StepNames.Length - 1)];
        Reached1 = value >= 0;
        Reached2 = value >= 1;
        Reached3 = value >= 2;
        Reached4 = value >= 3;
        NotifyStepState();
    }

    partial void OnIsOobeChanged(bool value) => NotifyStepState();

    partial void OnSelectedWorldIdChanged(string value)
    {
        OnPropertyChanged(nameof(SelectedWorldDisplay));
        OnPropertyChanged(nameof(MapHint));
    }

    partial void OnSelectedWorldNameChanged(string value) => OnPropertyChanged(nameof(SelectedWorldDisplay));

    partial void OnPersonaTextChanged(string value)
    {
        OnPropertyChanged(nameof(HasPersona));
        OnPropertyChanged(nameof(PersonaCount));
    }

    private void NotifyStepState()
    {
        OnPropertyChanged(nameof(ShowWorldBlock));
        OnPropertyChanged(nameof(ShowPersonaBlock));
        OnPropertyChanged(nameof(ShowRulesBlock));
        OnPropertyChanged(nameof(ShowReviewBlock));
        OnPropertyChanged(nameof(IsLastStep));
        OnPropertyChanged(nameof(ShowNext));
        OnPropertyChanged(nameof(ShowBegin));
    }

    [RelayCommand]
    private async Task Begin()
    {
        if (IsBusy) return;
        if (string.IsNullOrEmpty(_characterId))
        {
            Status = "没有可进入的角色。";
            return;
        }

        var worldId = SelectedWorldId ?? "";
        var worldName = Worlds.FirstOrDefault(w => string.Equals(w.Id, worldId, StringComparison.Ordinal))?.Title ?? "";
        if (string.Equals(worldName, "不绑定 · 普通酒馆", StringComparison.Ordinal)) worldName = "";
        var persona = (PersonaText ?? "").Trim();

        if (_editMode)
        {
            var worldChanged = !string.Equals(_engine.State.WorldId, worldId, StringComparison.Ordinal);
            var personaChanged = !string.Equals((_engine.State.UserPersona ?? "").Trim(), persona, StringComparison.Ordinal);
            if (!worldChanged && !personaChanged)
            {
                await Shell.Current.GoToAsync("..");
                return;
            }
            if (worldChanged && string.IsNullOrEmpty(worldId) && _engine.MapUnlocked)
            {
                var sure = await Shell.Current.DisplayAlert("解绑世界书？",
                    "解绑之后这份存档会变成普通酒馆：地图与「移动」会立刻锁上。走过的地方不会丢，重新绑回来还能用。",
                    "解绑", "算了");
                if (!sure) return;
            }

            _engine.State.WorldId = worldId;
            _engine.State.WorldName = worldName;
            _engine.State.UserPersona = persona;

            await RunAsync(async progress =>
            {
                if (worldChanged)
                    await _persona.InitializeAsync(_engine.State, _characterId, progress);
                else
                    await _persona.RefreshPersonaAsync(_engine.State, _characterId, progress);
                progress.Report("正在保存本局设定…");
                await _save.Commit(CharacterName);
            }, "人格记忆重新整理中", "设定已更新。");
            return;
        }

        await RunAsync(async progress =>
        {
            // 顺序不能反：Boot 会重置整份状态，角色必须在它之后写进去。
            _engine.Boot();
            _engine.SetCharacter(_characterId);
            if (_engine.ActiveCharacter is { } fresh) CharacterLibrary.RandomizeSpriteState(fresh);
            _engine.State.WorldId = worldId;
            _engine.State.WorldName = worldName;
            _engine.State.UserPersona = persona;
            _save.NewRun();

            await _persona.InitializeAsync(_engine.State, _characterId, progress);
            progress.Report("正在落盘这一局…");
            await _save.Commit(CharacterName);
            progress.Report("记忆已就位，正在进入…");
        }, "人格记忆初始化中", "");
    }

    /// <summary>
    /// 初始化与落盘都在这一层跑：遮罩只负责把真实进度显示出来。
    /// 中途出错也要留下能解释的结果 —— 新档已经写进去了才算开始，没写进去就停在原地。
    /// </summary>
    private async Task RunAsync(Func<IProgress<string>, Task> work, string title, string doneText)
    {
        IsBusy = true;
        LoadingTitle = title;
        LoadingDetail = "正在准备…";
        LoadingProgress = 0.04;
        LoadingPercent = "4%";
        try
        {
            var progress = new Progress<string>(OnProgress);
            await work(progress);
            LoadingProgress = 1;
            LoadingPercent = "100%";
            LoadingDetail = "记忆已就位";
            await Task.Delay(260);

            if (_editMode)
            {
                Status = doneText;
                await Shell.Current.GoToAsync("..");
            }
            else
            {
                await Shell.Current.GoToAsync("main");
            }
        }
        catch (Exception ex)
        {
            App.WriteLog("NewRun.Begin -> " + ex);
            Status = "没能完成：" + ex.Message;
            LoadingDetail = "出错了";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void OnProgress(string message)
    {
        LoadingDetail = message;
        double step = message switch
        {
            _ when message.Contains("读取世界书", StringComparison.Ordinal) => 0.18,
            _ when message.Contains("整理成记忆", StringComparison.Ordinal) => 0.42,
            _ when message.Contains("写入记忆", StringComparison.Ordinal) => 0.80,
            _ when message.Contains("没有要整理", StringComparison.Ordinal) => 0.82,
            _ when message.Contains("落盘", StringComparison.Ordinal) => 0.92,
            _ when message.Contains("进入", StringComparison.Ordinal) => 0.98,
            _ => LoadingProgress
        };
        if (step > LoadingProgress)
        {
            LoadingProgress = step;
            LoadingPercent = $"{Math.Round(step * 100)}%";
        }
    }
}

/// <summary>世界树选择行：不绑定也是一行，地图锁不锁写在那一行的说明里。</summary>
public sealed partial class WorldChoiceItem : ObservableObject
{
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public string Summary { get; init; } = "";
    public string Marker { get; init; } = "";
    public bool HasSummary => !string.IsNullOrWhiteSpace(Summary);
    [ObservableProperty] private bool _isSelected;
    public string Mark => IsSelected ? "已选" : "";

    partial void OnIsSelectedChanged(bool value) => OnPropertyChanged(nameof(Mark));
}
