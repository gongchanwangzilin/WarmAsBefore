using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.Controls;
using WarmAsBefore.Models;
using WarmAsBefore.Modules.ApiManager;
using WarmAsBefore.Services;

namespace WarmAsBefore.ViewModels;

/// <summary>
/// 角色工作室：把一个角色的一切都收在同一屏里——左边立绘舞台（服装 × 表情 × 朝向），右边分段改。
///
/// 存在的理由是把「一堆弹窗和提示」换掉：原来改一句性格要过两层 ActionSheet 加一次
/// DisplayPromptAsync，用户根本不知道自己在改哪、改完有没有生效。这里所有反馈落在
/// <see cref="Status"/> 一行字上，改的东西一律先进字段、按「保存」才落盘，随时能放弃。
///
/// 分工要说清楚，不然是骗人：
/// · 文本/状态/覆写 = 延迟写（进字段，Save 才落盘，因为 CharacterData 是不可变记录，一次写好）；
/// · 立绘生成/素材导入/灵枢导入/复制/主角标记/白名单 = 即时写（它们要动文件、要建角色，
///   攒到「保存」那一刻再一起做只会更难解释）。
/// · 「跨存档共用记忆」这颗开关是**全局**的，不是这一角色的：记忆的存储边界由
///   <see cref="MemoryScope"/> 按存档划，一个作用域里所有角色共用同一批文件，
///   做成每角色一个开关，界面会答，盘上不会。所以这里明说是全局开关。
/// </summary>
public sealed partial class CharacterStudioViewModel : ObservableObject
{
    private readonly GameEngine _engine;
    private readonly CharacterLibrary _library;
    private readonly StorageProvider _store;
    private readonly ApiGateway _api;
    private readonly ImageGenerationService _images;
    private readonly LingshuImporter _lingshu;
    private readonly CharacterSubProfileService _subProfiles;
    private readonly MemoryScope _scope;
    private readonly SettingsManager _settings;
    private readonly MemoryOverviewService _memory;
    private readonly PageWindowService _windows;
    private readonly GenderLibrary _genders;
    private readonly Modules.SaveSystem.SaveManager _saves;

    private CharacterData _ch = new();
    private bool _loading = true;
    private readonly HashSet<string> _changed = new(StringComparer.Ordinal);

    public CharacterStudioViewModel(GameEngine engine, CharacterLibrary library, StorageProvider store,
        ApiGateway api, ImageGenerationService images, LingshuImporter lingshu,
        CharacterSubProfileService subProfiles, MemoryScope scope, SettingsManager settings,
        MemoryOverviewService memory, PageWindowService windows, GenderLibrary genders,
        Modules.SaveSystem.SaveManager saves)
    {
        _engine = engine;
        _library = library;
        _store = store;
        _api = api;
        _images = images;
        _lingshu = lingshu;
        _subProfiles = subProfiles;
        _scope = scope;
        _settings = settings;
        _memory = memory;
        _windows = windows;
        _genders = genders;
        _saves = saves;
        GenderChoices = genders.Names
            .Select(n => new StudioAxisItem(n, n)).ToList();
    }

    /// <summary>关闭请求：收缩动画由页面演，演完再把这层关掉。</summary>
    public Func<Task>? CloseRequested { get; set; }

    /// <summary>落盘成功后要通知的人（角色库网格要跟着重算标签与预览）。</summary>
    public Action? Saved { get; set; }

    /// <summary>
    /// 新建那一张真的进了名册（带着新 id）。和 <see cref="Saved"/> 分开是因为只有这一趟
    /// 值得让调用方接着做「用 TA 开一局」——保存老角色不该把你从原来那局里踢出去。
    /// </summary>
    public Action<string>? Created { get; set; }

    // ── 身份 ────────────────────────────────────────────────────────────
    [ObservableProperty] private string _characterId = "";
    [ObservableProperty] private string _displayName = "";
    [ObservableProperty] private string _headline = "";
    [ObservableProperty] private string _metaLine = "";
    [ObservableProperty] private bool _isMain;
    [ObservableProperty] private string _mainLabel = "";

    // ── 设定 ────────────────────────────────────────────────────────────
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _gender = "";
    [ObservableProperty] private string _nickname = "";
    [ObservableProperty] private string _userAddress = "";
    [ObservableProperty] private string _personality = "";
    [ObservableProperty] private string _description = "";
    [ObservableProperty] private string _greeting = "";
    [ObservableProperty] private bool _isRomance = true;

    // ── 状态与身体 ──────────────────────────────────────────────────────
    [ObservableProperty] private double _affection;
    [ObservableProperty] private double _trust;
    [ObservableProperty] private double _energy = 100;
    [ObservableProperty] private string _affectionPoints = "";
    [ObservableProperty] private string _mood = "";
    [ObservableProperty] private string _location = "";
    [ObservableProperty] private bool _cycleTracked;
    [ObservableProperty] private string _cycleDay = "";
    [ObservableProperty] private string _cyclePhase = "";
    [ObservableProperty] private string _cycleNote = "";
    [ObservableProperty] private string _subTone = "";
    [ObservableProperty] private string _subNotes = "";
    [ObservableProperty] private string _subDiff = "";

    // ── 立绘舞台 ────────────────────────────────────────────────────────
    [ObservableProperty] private ImageSource? _previewSource;
    [ObservableProperty] private bool _hasPreview;
    [ObservableProperty] private ImageSource? _avatarSource;
    [ObservableProperty] private bool _hasAvatar;
    [ObservableProperty] private string _previewCaption = "";
    [ObservableProperty] private string _previewHint = "";
    [ObservableProperty] private double _mirrorX = 1;
    [ObservableProperty] private bool _facingLeft;
    [ObservableProperty] private string _selectedOutfit = "";
    [ObservableProperty] private string _selectedEmotion = "";
    [ObservableProperty] private string _artPrompt = "";
    [ObservableProperty] private string _artHint = "";
    [ObservableProperty] private bool _canGenerateArt;

    public ObservableCollection<StudioAxisItem> Outfits { get; } = new();
    public ObservableCollection<StudioAxisItem> Emotions { get; } = new();
    public ObservableCollection<StudioArtRow> ArtRows { get; } = new();

    /// <summary>
    /// 素材矩阵还没铺（或者素材变了要重铺）。186 格、每格一整套控件，还挂着 69 条
    /// 图片源——它不能挡在「这一页出现」前面，所以进页时只记一笔，等真的翻到「03 素材」再建。
    /// </summary>
    private bool _artGridDirty = true;
    private bool _artGridBuilding;

    /// <summary>
    /// 心情档位：盘上存的是小写英文键（<c>State.Mood</c>），界面给人话标签。
    /// 做成一排可点的块而不是下拉：这一档一天要改好几次，选择器每次都要先点开再找。
    /// </summary>
    public List<StudioAxisItem> MoodChoices { get; } =
        new[] { ("normal", "平常"), ("happy", "开心"), ("shy", "害羞"), ("sad", "低落"),
                ("angry", "闹脾气"), ("tired", "疲惫"), ("worried", "不安") }
            .Select(m => new StudioAxisItem(m.Item1, m.Item2)).ToList();

    // ── 记忆 ────────────────────────────────────────────────────────────
    public ObservableCollection<StudioLayerRow> Layers { get; } = new();
    [ObservableProperty] private string _scopeLabel = "";
    [ObservableProperty] private string _scopePath = "";
    [ObservableProperty] private bool _crossSave;
    [ObservableProperty] private string _memoryHint = "";
    [ObservableProperty] private string _ovMemoryTurns = "";
    [ObservableProperty] private string _ovContextLimit = "";
    [ObservableProperty] private string _ovTemperature = "";
    [ObservableProperty] private string _ovWindowDays = "";
    [ObservableProperty] private string _ovRecall = "";

    // ── 试句话 ──────────────────────────────────────────────────────────
    [ObservableProperty] private string _talkInput = "";
    [ObservableProperty] private bool _talkBusy;
    [ObservableProperty] private string _talkHint = "";
    public ObservableCollection<TalkLine> TalkLines { get; } = new();

    /// <summary>一次试话最多带几句旧话进上下文：再多就把「听个口气」变成了重放整段历史。</summary>
    private const int TalkWindowLines = 16;

    public bool HasTalkLines => TalkLines.Count > 0;
    public bool CanTalk => !_talkBusy;

    partial void OnTalkBusyChanged(bool value) => OnPropertyChanged(nameof(CanTalk));

    // ── 关联 ────────────────────────────────────────────────────────────
    [ObservableProperty] private bool _allowedInSave;
    [ObservableProperty] private string _allowedHint = "";
    [ObservableProperty] private string _duplicateName = "";
    [ObservableProperty] private string _saveCountLabel = "";

    // ── AI ──────────────────────────────────────────────────────────────
    [ObservableProperty] private string _aiBrief = "";
    [ObservableProperty] private string _aiDraft = "";
    [ObservableProperty] private string _aiTarget = "";
    [ObservableProperty] private bool _aiBusy;
    [ObservableProperty] private bool _hasAiDraft;

    // ── 页面状态 ────────────────────────────────────────────────────────
    [ObservableProperty] private bool _isTabSet = true;
    [ObservableProperty] private bool _isTabState;
    [ObservableProperty] private bool _isTabArt;
    [ObservableProperty] private bool _isTabMem;
    [ObservableProperty] private bool _isTabLink;
    [ObservableProperty] private bool _isTabTalk;
    [ObservableProperty] private bool _busy;
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _isDirty;
    [ObservableProperty] private string _dirtyLabel = "";

    /// <summary>性别候选：出厂在前、自定义在后，取自本机的性别库（不写死三个）。</summary>
    public List<StudioAxisItem> GenderChoices { get; }

    public bool CanAi => !_aiBusy;
    public bool CanSave => IsDirty && !Busy;

    /// <summary>这一屏对着的是草稿还是盘上的人：同一颗按钮，做的是两件不同的事，就该写两个名字。</summary>
    public bool IsNew => _isNew;
    public string SaveLabel => _isNew ? "建好角色" : "保存";

    /// <summary>
    /// 这一屏此刻是暗档世界：缺立绘时垫的那张图记是黑色印刷件，暗底上要换反白那一版。
    /// 只在这一页打开时取一次（换世界要回设置页，这一页会重建）。
    /// </summary>
    public bool IsDarkWorld => WarmAsBefore.DesignSystem.Worlds.StyleWorldTokens.Dark;

    partial void OnAiBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanAi));
        OnPropertyChanged(nameof(CanSave));
    }

    partial void OnBusyChanged(bool value) => OnPropertyChanged(nameof(CanSave));
    partial void OnIsDirtyChanged(bool value) => OnPropertyChanged(nameof(CanSave));

    /// <summary>字段进脏名单：所有可编辑属性都走这里，「有没有改过」只剩一个判据。</summary>
    private void Note(string field)
    {
        if (_loading) return;
        _changed.Add(field);
        IsDirty = true;
        DirtyLabel = $"有 {_changed.Count} 处没保存";
    }

    partial void OnNameChanged(string value) => Note("姓名");
    partial void OnGenderChanged(string value) { Note("性别"); SyncChoice(GenderChoices, value); }
    partial void OnNicknameChanged(string value) => Note("昵称");
    partial void OnUserAddressChanged(string value) => Note("称呼");
    partial void OnPersonalityChanged(string value) => Note("性格");
    partial void OnDescriptionChanged(string value) => Note("背景");
    partial void OnGreetingChanged(string value) => Note("问候语");
    partial void OnIsRomanceChanged(bool value) => Note("恋爱对象");
    partial void OnAffectionChanged(double value) => Note("好感");
    partial void OnTrustChanged(double value) => Note("信任");
    partial void OnEnergyChanged(double value) => Note("精力");
    partial void OnAffectionPointsChanged(string value) => Note("好感积分");
    partial void OnMoodChanged(string value) { Note("心情"); SyncChoice(MoodChoices, value); }
    partial void OnLocationChanged(string value) => Note("位置");
    partial void OnCycleTrackedChanged(bool value) => Note("周期记录");
    partial void OnCycleDayChanged(string value) => Note("周期天数");
    partial void OnCyclePhaseChanged(string value) => Note("周期阶段");
    partial void OnOvMemoryTurnsChanged(string value) => Note("记忆条数");
    partial void OnOvContextLimitChanged(string value) => Note("上下文上限");
    partial void OnOvTemperatureChanged(string value) => Note("温度");
    partial void OnOvWindowDaysChanged(string value) => Note("遗忘指数");
    partial void OnOvRecallChanged(string value) => Note("回忆随机度");

    partial void OnFacingLeftChanged(bool value)
    {
        MirrorX = value ? -1 : 1;
        Note("朝向");
    }

    // ── 选项块（性别 / 心情）─────────────────────────────────────────────

    /// <summary>
    /// 选中态写回选项本身：XAML 只能触发「绑定值等于某个固定值」，比不了「这一项等不等于当前选的」。
    /// 所以谁被选中由这里算，界面只认 IsOn —— 和角色库的分类标签用的是同一套办法。
    /// </summary>
    private static void SyncChoice(IEnumerable<StudioAxisItem> items, string? key)
    {
        foreach (var i in items)
            i.IsOn = string.Equals(i.Key, (key ?? "").Trim(), StringComparison.OrdinalIgnoreCase);
    }

    [RelayCommand]
    private void PickGender(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        Gender = name.Trim();
    }

    [RelayCommand]
    private void PickMood(string? mood)
    {
        if (string.IsNullOrWhiteSpace(mood)) return;
        Mood = mood.Trim().ToLowerInvariant();
    }

    // ── 打开一个角色 ────────────────────────────────────────────────────

    /// <summary>把某个角色铺进这一屏。<paramref name="savesLabel"/> 由角色库算好递进来（它才有一整份存档表）。</summary>
    public async Task<bool> OpenAsync(string charId)
    {
        var found = (await _library.ListAsync()).FirstOrDefault(c => c.Profile.Id == charId);
        if (found is null)
        {
            Status = "这个角色的资料读不出来了。";
            return false;
        }
        // 存档数自己数，不从调用方要：以前是列表页顺手递一句字符串进来，
        // 于是「这一页能开在谁的几个存档上」只有从列表点进去才说得清，另开一扇窗就成了空白句。
        var runs = (await _saves.List()).Count(s => s.Character == charId);
        Apply(found, runs == 0 ? "暂无存档" : $"存档 {runs} 个", false);
        return true;
    }

    /// <summary>
    /// 新建角色：开一张空白卡，不再连弹三个输入框。
    /// 姓名、性别、性格就写在右边的栏里，想省事用「让 AI 写一整份设定」一句话生成，
    /// 看对了才按「建好角色」进名册——在那之前，这一切都只在草稿上。
    ///
    /// id 在卡片打开那一刻定下且不再换：中途画的立绘、换的头像都挂在这个 id 上，
    /// 「放弃改动」要是换成另一个 id，那些文件就成了没人认领的孤儿。
    /// </summary>
    public void OpenNew()
    {
        _draft ??= BlankDraft();
        Apply(_draft, "还没进名册", true);
    }

    private CharacterData? _draft;
    private bool _isNew;

    /// <summary>一张谁也不是的底：只有「必须由人来定的那四栏」留空，其余照出厂默认显示出来——框里写的就是盘上要写的。</summary>
    private CharacterData BlankDraft()
    {
        var seed = _library.CreateDefault("", "", "");
        return seed with
        {
            Profile = seed.Profile with { Name = "", Gender = "", Nickname = "", Greeting = "" }
        };
    }

    private void Apply(CharacterData found, string savesLabel, bool isNew)
    {
        _isNew = isNew;
        _ch = found;
        _loading = true;
        _changed.Clear();
        // 换一个人（或从草稿切回盘上的人）时，上一段试话要跟着清空：
        // 那些气泡是按这一屏的人设答出来的，留着读起来像是「TA 记得这些」。
        TalkLines.Clear();
        TalkInput = "";
        TalkHint = "";
        OnPropertyChanged(nameof(HasTalkLines));

        CharacterId = _ch.Profile.Id;
        DisplayName = _ch.Profile.Name;
        Headline = isNew ? "新建角色"
            : (string.IsNullOrWhiteSpace(_ch.Profile.Nickname) ? _ch.Profile.Name : $"{_ch.Profile.Name}·{_ch.Profile.Nickname}");
        MetaLine = isNew ? "先起个名字、选个性别，其余都能之后补"
            : StudioCopy.Meta(_ch.Profile.Gender, _ch.Profile.UserAddress, _ch.Profile.IsRomanceTarget);
        IsMain = IsMainCharacter(_ch.Profile.Id);
        MainLabel = IsMain ? "当前主角" : "设为主角";
        SaveCountLabel = savesLabel;
        DuplicateName = _ch.Profile.Name + "（副本）";

        Name = _ch.Profile.Name;
        Gender = _ch.Profile.Gender;
        Nickname = _ch.Profile.Nickname;
        UserAddress = _ch.Profile.UserAddress;
        Personality = _ch.Profile.Personality;
        Description = _ch.Profile.Description;
        Greeting = _ch.Profile.Greeting;
        IsRomance = _ch.Profile.IsRomanceTarget;

        var st = _ch.State;
        Affection = st.Affection;
        Trust = st.Trust;
        Energy = st.Energy;
        AffectionPoints = st.AffectionPoints.ToString(CultureInfo.InvariantCulture);
        Mood = st.Mood;
        Location = st.Location;
        CycleTracked = st.Cycle.Tracked;
        CycleDay = st.Cycle.CycleDay.ToString(CultureInfo.InvariantCulture);
        CyclePhase = st.Cycle.Phase;
        CycleNote = StudioCopy.CycleNote(st.Cycle);
        FacingLeft = st.IsFacingLeft;
        SelectedOutfit = string.IsNullOrWhiteSpace(st.CurrentOutfit) ? "default" : st.CurrentOutfit;
        SelectedEmotion = string.IsNullOrWhiteSpace(st.CurrentEmotion) ? "normal" : st.CurrentEmotion;

        var sub = CharacterSubProfileService.Seed(_ch.Profile, _ch.SubProfile);
        SubTone = sub.Tone;
        SubNotes = sub.RecentNotes.Count == 0 ? "（还没有记下的心事）" : string.Join("；", sub.RecentNotes);
        SubDiff = _subProfiles.DiffSummary(_ch.Profile.Id);

        var o = _ch.Overrides ?? new CharacterOverrides();
        OvMemoryTurns = Show(o.MemoryTurns);
        OvContextLimit = Show(o.ContextTokenLimit);
        OvTemperature = Show(o.AiTemperature);
        OvWindowDays = Show(o.MemoryWindowDays);
        OvRecall = Show(o.MemoryRecallRandomness);

        AllowedInSave = AllowedNow(_ch.Profile.Id);
        AllowedHint = StudioCopy.Whitelist(AllowedInSave, _engine.State.AllowedCharacters.Count);
        CrossSave = _settings.Current.CrossSaveMemory;
        ScopeLabel = _scope.IsShared ? "所有存档共用这一份记忆" : $"只在这份存档里（{_scope.Current}）";
        ScopePath = _scope.Key("memories") + ".json";
        MemoryHint = StudioCopy.MemoryScopeHint(_scope.IsShared);

        RebuildArtAxes();
        SyncChoice(GenderChoices, Gender);
        SyncChoice(MoodChoices, Mood);
        AiDraft = "";
        HasAiDraft = false;
        AiTarget = "";
        ArtPrompt = "";
        CanGenerateArt = _images.Enabled;
        ArtHint = _images.Enabled
            ? isNew
                ? "写下衣服与场景，画出来的这一格跟着这张草稿走；「建好角色」之后它就正式属于 TA。"
                : "写下衣服与场景，生成的是这一格的全身立绘，会直接登记进这个角色。"
            : "画面生成没开启：设置 → AI → 画面生成里填好接口与密钥，这一格才能生成。";
        // 打开谁都在「设定」这一栏落地。新建那张只有这一栏能用（关联对着一个还不存在的人没意义），
        // 老角色也从这里起：整页五栏都靠 IsTab* 里恰好有一颗 true 才显示，
        // 不在这儿定一颗，读出来就是一张只有抬头的空卡。
        GoTab("set");
        Status = isNew ? "先填姓名和性别，其余都能之后再补——按「建好角色」才会进名册。" : "";
        IsDirty = false;
        DirtyLabel = "";
        _loading = false;

        RaiseAll();
        // 记忆层数的统计不挡在「亮出这一页」前面：读盘那几十毫秒让整张卡点了没反应，
        // 比数字晚到一帧难看得多。Layers 一进来就清空，所以也不会把上一个角色的数字留在这。
        _ = RefreshLayersAsync();
    }

    private static string Show<T>(T? v) where T : struct =>
        v?.ToString() is { Length: > 0 } s ? s : "";

    private bool IsMainCharacter(string id)
    {
        var mainId = _engine.ActiveCharacter?.Profile.Id ?? _engine.State.CharacterId;
        return !string.IsNullOrEmpty(mainId) && mainId == id;
    }

    private bool AllowedNow(string id)
    {
        var allowed = _engine.State.AllowedCharacters;
        return allowed.Count == 0 || allowed.Contains(id);
    }

    // ── 页签 ────────────────────────────────────────────────────────────

    [RelayCommand]
    private void GoTab(string tab)
    {
        IsTabSet = tab == "set";
        IsTabState = tab == "state";
        IsTabArt = tab == "art";
        IsTabMem = tab == "mem";
        IsTabLink = tab == "link";
        IsTabTalk = tab == "talk";
        // 素材矩阵第一次被翻到时才铺（见 _artGridDirty）。不 await：页签与标题要先落地，
        // 铺格子是这一栏自己的事，慢一点也只慢在它下面。
        if (tab == "art") _ = EnsureArtGridAsync();
    }

    // ── 立绘：服装 × 表情 × 朝向 ────────────────────────────────────────

    /// <summary>
    /// 服装与表情是从这个角色的 SpriteMap 现算的两根轴（键就是「服装/表情」）。
    /// 表情名是从素材文件名里解析出来的自由词（可能是「微笑」也可能是 smile），
    /// 所以这里不假设固定五档，缺的格子如实标出来。
    ///
    /// 只建两根轴与预览，**不建素材矩阵**：那 186 个格子是进页卡顿的主因，
    /// 而它所在的「03 素材」一开始根本看不见。矩阵由 <see cref="EnsureArtGridAsync"/>
    /// 在第一次翻到那一栏时铺。轴按名字稳定排序，不把选中的排到最前——
    /// 选中一颗就整排重排，读起来像按错了东西。
    /// </summary>
    private void RebuildArtAxes()
    {
        Outfits.Clear();
        Emotions.Clear();
        ArtRows.Clear();
        _artGridDirty = true;

        var keys = _ch.SpriteMap.Keys
            .Where(k => k.Contains('/') && !string.IsNullOrWhiteSpace(_ch.SpriteMap[k]))
            .ToList();
        var outfits = keys.Select(k => k.Split('/')[0])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(k => k, StringComparer.CurrentCulture)
            .ToList();
        var emotions = keys.Select(k => k.Split('/')[1])
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(e => e, StringComparer.CurrentCulture)
            .ToList();

        if (outfits.Count == 0) outfits.Add(string.IsNullOrWhiteSpace(SelectedOutfit) ? "default" : SelectedOutfit);
        if (emotions.Count == 0) emotions.Add(string.IsNullOrWhiteSpace(SelectedEmotion) ? "normal" : SelectedEmotion);

        foreach (var o in outfits) Outfits.Add(new StudioAxisItem(o, StudioCopy.OutfitName(o)) { IsOn = o == SelectedOutfit });
        foreach (var e in emotions) Emotions.Add(new StudioAxisItem(e, e) { IsOn = e == SelectedEmotion });
        RebuildPreview();
    }

    /// <summary>
    /// 铺素材矩阵。分拍卖出：一次建 186 格会把这一下压成半秒空白，
    /// 每铺完一套服装（一行）就把线程让回给界面，页签与标题先落地、矩阵一行行长出来。
    /// 已经铺过且素材没变就直接返回；重复切栏不会重建。
    /// </summary>
    private async Task EnsureArtGridAsync()
    {
        if (_artGridBuilding || (!_artGridDirty && ArtRows.Count > 0)) return;
        _artGridBuilding = true;
        try
        {
            ArtRows.Clear();
            _artGridDirty = false;
            // 先让换栏这一帧画出去，再开始铺。16ms 是一帧，用户看到的是「内容已到位，矩阵随后」。
            await Task.Delay(16);
            var outfits = Outfits.ToList();
            var emotions = Emotions.ToList();
            foreach (var o in outfits)
            {
                var row = new StudioArtRow(o.Key, o.Label);
                foreach (var e in emotions)
                {
                    var rel = FindSprite(o.Key, e.Key);
                    row.Cells.Add(new StudioArtCell(o.Key, e.Key, rel is null ? null : ImageFromFile(rel),
                        rel is not null, IsCurrentCell(o.Key, e.Key)));
                }
                var have = row.Cells.Count(c => c.HasImage);
                row.FillLabel = $"{have}/{row.Cells.Count} 格有图";
                ArtRows.Add(row);
                await Task.Yield();
            }
        }
        finally { _artGridBuilding = false; }
    }

    private string? FindSprite(string outfit, string emotion)
    {
        if (_ch.SpriteMap.TryGetValue($"{outfit}/{emotion}", out var exact) && !string.IsNullOrWhiteSpace(exact))
            return exact;
        var any = _ch.SpriteMap.Keys.FirstOrDefault(k =>
            k.StartsWith(outfit + "/", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(_ch.SpriteMap[k]));
        return any is null ? null : _ch.SpriteMap[any];
    }

    private bool IsCurrentCell(string outfit, string emotion) =>
        string.Equals(outfit, SelectedOutfit, StringComparison.OrdinalIgnoreCase)
        && string.Equals(emotion, SelectedEmotion, StringComparison.OrdinalIgnoreCase);

    private ImageSource? ImageFromFile(string rel)
    {
        var full = Modules.Affection.SpritePathResolver.ResolveFile(rel, _store.Root);
        return full is null ? null : ImageSource.FromFile(full);
    }

    [RelayCommand]
    private void Wear(string? outfit)
    {
        if (string.IsNullOrWhiteSpace(outfit)) return;
        SelectedOutfit = outfit;
        // 换上这套后挑它自己的第一个表情：否则选中的表情可能压根不在这套衣服上，预览会空。
        if (!HasSprite(outfit, SelectedEmotion))
            SelectedEmotion = Emotions.FirstOrDefault(e => HasSprite(outfit, e.Key))?.Key ?? SelectedEmotion;
        Note("服装/表情");
        // 只换预览与两颗选项的选中态，不重铺素材矩阵：矩阵里没有「选中」这一笔，
        // 而重铺一次要重建 186 个格子 —— 点一下换装卡半秒，就是从这里来的。
        SyncChoice(Outfits, SelectedOutfit);
        SyncChoice(Emotions, SelectedEmotion);
        RebuildPreview();
        Status = $"已换上「{StudioCopy.OutfitName(outfit)}」——还没保存，按「保存」才落到角色身上。";
    }

    [RelayCommand]
    private void Face(string? emotion)
    {
        if (string.IsNullOrWhiteSpace(emotion)) return;
        SelectedEmotion = emotion;
        Note("服装/表情");
        SyncChoice(Outfits, SelectedOutfit);
        SyncChoice(Emotions, SelectedEmotion);
        RebuildPreview();
        Status = HasSprite(SelectedOutfit, emotion)
            ? $"表情切到「{emotion}」。"
            : $"「{StudioCopy.OutfitName(SelectedOutfit)}」没有「{emotion}」这一格：预览先用这套衣服的其它图，补齐它去素材页生成。";
    }

    [RelayCommand]
    private void PickCell(StudioArtCell? cell)
    {
        if (cell is null) return;
        SelectedOutfit = cell.Outfit;
        SelectedEmotion = cell.Emotion;
        if (cell.HasImage)
        {
            Note("服装/表情");
            SyncChoice(Outfits, SelectedOutfit);
            SyncChoice(Emotions, SelectedEmotion);
            RebuildPreview();
            Status = $"已选「{cell.OutfitName} / {cell.Emotion}」。";
            return;
        }
        GoTab("art");
        SyncChoice(Outfits, SelectedOutfit);
        SyncChoice(Emotions, SelectedEmotion);
        RebuildPreview();
        Status = $"缺「{cell.OutfitName} / {cell.Emotion}」这一格：在下面的描述里写下这套衣服与神情，生成立绘补上。";
    }

    private bool HasSprite(string outfit, string emotion) =>
        _ch.SpriteMap.TryGetValue($"{outfit}/{emotion}", out var rel) && !string.IsNullOrWhiteSpace(rel);

    private void RebuildPreview()
    {
        // 小头像是这一屏的「谁」，和立绘是两件事：立绘可以缺，头像缺了就露一个空框等人来放，
        // 不拿姓名首字顶上 —— 一颗 190px 的大字读起来只是占位，不像一个人。
        AvatarSource = string.IsNullOrWhiteSpace(_ch.Avatar) ? null : ImageFromFile(_ch.Avatar);
        HasAvatar = AvatarSource is not null;

        var rel = FindSprite(SelectedOutfit, SelectedEmotion);
        var src = rel is null ? null : ImageFromFile(rel);
        var fromAvatar = false;
        if (src is null && AvatarSource is not null)
        {
            src = AvatarSource;
            fromAvatar = true;
        }
        if (src is null && rel is not null)
            // 「看不到预览」到底是没图还是没读到图，得能从日志里分出来：前者要生成，后者是路径的事。
            App.WriteLog($"Studio: 未找到立绘 {rel}（头像 {_ch.Avatar}），试了 "
                + Modules.Affection.SpritePathResolver.Candidates(rel, _store.Root).Count + " 条路径");
        PreviewSource = src;
        HasPreview = src is not null;
        PreviewCaption = fromAvatar
            ? "头像 · 还没有立绘"
            : $"{StudioCopy.OutfitName(SelectedOutfit)} · {SelectedEmotion} · {(FacingLeft ? "朝左" : "朝右")}";
        PreviewHint = HasPreview
            ? ""
            : "这个角色还没有任何立绘：先用一句话生成一套，或从 zip 补充素材。";
    }

    /// <summary>
    /// 朝向是角色状态里存着的一档（<c>State.IsFacingLeft</c>），走路人、桌宠、对话立绘都该照它站：
    /// 以前它在盘上躺着，界面一处都不认，等于白存。这一屏是它第一次看得见、也第一次改得动。
    /// </summary>
    [RelayCommand]
    private void Flip()
    {
        FacingLeft = !FacingLeft;
        RebuildPreview();
        Status = (FacingLeft ? "TA 现在朝左站" : "TA 现在朝右站") + "（还没保存）";
    }

    [RelayCommand]
    private async Task GenerateArt()
    {
        var prompt = (ArtPrompt ?? "").Trim();
        if (prompt.Length == 0)
        {
            Status = "先写一句画面描述（例如：白色针织衫配浅色长裙，站在午后窗边，微微笑）。";
            return;
        }
        if (!_images.Enabled)
        {
            CanGenerateArt = false;
            ArtHint = "画面生成没开启：设置 → AI → 画面生成里填接口与密钥。";
            Status = "画面生成还没配置，先去做上面那一步。";
            return;
        }
        Busy = true;
        Status = "正在画这一格立绘…（画面模型通常要几十秒）";
        try
        {
            var appearance = string.IsNullOrWhiteSpace(_ch.Profile.Description)
                ? "" : "；角色设定：" + _ch.Profile.Description;
            var (path, message) = await _images.GenerateAsync(
                $"{_ch.Profile.Name} 的全身立绘，{prompt}。干净背景，完整人物，不要文字{appearance}",
                "studio_" + _ch.Profile.Id, "1024x1536");
            if (path is null)
            {
                Status = message;
                return;
            }
            var dir = Path.Combine(_store.Root, "assets", "characters", _ch.Profile.Id, SelectedOutfit);
            Directory.CreateDirectory(dir);
            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var fileName = $"{SelectedEmotion}_{stamp}.png";
            var dest = Path.Combine(dir, fileName);
            File.Copy(path, dest, true);
            var rel = $"assets/characters/{_ch.Profile.Id}/{SelectedOutfit}/{fileName}";

            var map = new Dictionary<string, string>(_ch.SpriteMap, StringComparer.OrdinalIgnoreCase)
            {
                [$"{SelectedOutfit}/{SelectedEmotion}"] = rel
            };
            _ch = _ch with { SpriteMap = map };
            // 草稿阶段画的东西只记在草稿上：这时候把人登记进名册，等于「还没建好就已经存在」，
            // 名册里会冒出一个空名字的陌生人。
            if (_isNew) _draft = _ch;
            else await _library.UpdateAsync(_ch);
            RebuildArtAxes();
            if (IsTabArt) _ = EnsureArtGridAsync();
            ArtPrompt = "";
            Status = $"「{StudioCopy.OutfitName(SelectedOutfit)} / {SelectedEmotion}」这一格已经补上" +
                     (_isNew ? "，建好角色时就正式属于 TA。" : "并登记。");
        }
        catch (Exception ex)
        {
            Status = "生成失败：" + ex.Message;
            App.WriteLog("Studio.GenerateArt -> " + ex);
        }
        finally { Busy = false; }
    }

    [RelayCommand]
    private async Task ImportArtZip()
    {
        // 解包是对着名册里的那个人做的（找到 TA、把 SpriteMap 写回 TA），草稿还不算一个人。
        if (_isNew)
        {
            Status = "素材包要补给已经存在的人：先按「建好角色」，再回来解这个 zip。";
            return;
        }
        try
        {
            var file = await FilePicker.PickAsync(new PickOptions
            {
                FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    { DevicePlatform.WinUI, new[] { ".zip" } },
                    { DevicePlatform.Android, new[] { "application/zip" } },
                    { DevicePlatform.iOS, new[] { "public.zip-archive" } }
                }),
                PickerTitle = $"给「{_ch.Profile.Name}」补充素材包（zip）"
            });
            if (file is null) return;
            Busy = true;
            Status = "正在解包素材…";
            var (ok, message) = await _library.ImportSupplementaryZipAsync(_ch.Profile.Id, file.FullPath);
            _ch = (await _library.ListAsync()).FirstOrDefault(c => c.Profile.Id == CharacterId) ?? _ch;
            RebuildArtAxes();
            if (IsTabArt) _ = EnsureArtGridAsync();
            Status = ok ? message : "导入失败：" + message;
        }
        catch (Exception ex)
        {
            Status = "导入失败：" + ex.Message;
            App.WriteLog("Studio.ImportArtZip -> " + ex);
        }
        finally { Busy = false; }
    }

    [RelayCommand]
    private async Task PickAvatar()
    {
        try
        {
            var file = await FilePicker.PickAsync(new PickOptions
            {
                FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    { DevicePlatform.WinUI, new[] { ".png", ".jpg", ".jpeg", ".webp", ".bmp" } },
                    { DevicePlatform.Android, new[] { "image/*" } },
                    { DevicePlatform.iOS, new[] { "public.image" } }
                }),
                PickerTitle = $"给「{_ch.Profile.Name}」挑一张头像"
            });
            if (file is null) return;
            var ext = Path.GetExtension(file.FullPath);
            if (string.IsNullOrWhiteSpace(ext)) ext = ".png";
            var dir = Path.Combine(_store.Root, "assets", "characters", _ch.Profile.Id);
            Directory.CreateDirectory(dir);
            var rel = $"assets/characters/{_ch.Profile.Id}/avatar{ext}";
            File.Copy(file.FullPath, Path.Combine(_store.Root, rel), true);
            _ch = _ch with { Avatar = rel };
            if (_isNew) _draft = _ch;
            else await _library.UpdateAsync(_ch);
            RebuildPreview();
            Status = _isNew
                ? "头像已经挑好，还在草稿上——建好角色后角色库与对话里都跟着换。"
                : "头像已换，角色库与对话里的头像都跟着变。";
            Saved?.Invoke();
        }
        catch (Exception ex)
        {
            Status = "换头像失败：" + ex.Message;
            App.WriteLog("Studio.PickAvatar -> " + ex);
        }
    }

    // ── AI 生成与改写 ───────────────────────────────────────────────────

    /// <summary>
    /// 让 AI 就着现有设定改一段：性格 / 背景 / 开场白。
    /// 结果只进草稿区，不直接盖字段——AI 说一句就改一次，用户来不及看就变成「被 AI 代写了」。
    /// </summary>
    [RelayCommand]
    private async Task Ai(string? target)
    {
        if (AiBusy) return;
        var (field, instruction) = target switch
        {
            "性格" => (Personality, "提出一版更鲜明、更有魅力的性格描述（一到两句话，60 字以内）"),
            "背景" => (Description, "补一段前情提要／背景（两三句话，90 字以内），要与现有设定连得上"),
            "开场白" => (Greeting, "写一句 TA 见到「{称呼}」时会说的开场白（40 字以内，带 TA 的口气）"),
            _ => ("", "")
        };
        if (string.IsNullOrEmpty(instruction)) return;
        AiBusy = true;
        HasAiDraft = false;
        AiTarget = target!;
        Status = $"AI 正在重写{target}…";
        try
        {
            var history = new List<ChatMessage>
            {
                new() { Role = "system", Content = "你是角色设计助手。只输出改进后的文本本身，不要解释、不要引号、不要前后缀。" },
                new()
                {
                    Role = "user",
                    Content = $"角色「{Name}」，性别 {Gender}，称呼对方「{UserAddress}」。\n" +
                              $"现有性格：{Personality}\n现有背景：{Description}\n现有开场白：{Greeting}\n" +
                              (string.IsNullOrWhiteSpace(field) ? "" : $"要改的那一项现在的内容：{field}\n") +
                              instruction
                }
            };
            var draft = await _api.Chat(history);
            if (string.IsNullOrWhiteSpace(draft))
            {
                Status = "AI 没有回应：检查设置 → AI 里的接口与密钥。";
                return;
            }
            AiDraft = draft.Trim();
            HasAiDraft = true;
            Status = $"AI 给了一版新的{target}，满意就「用这一版」。";
        }
        catch (Exception ex)
        {
            Status = "AI 出错了：" + ex.Message;
            App.WriteLog($"Studio.Ai({target}) -> " + ex);
        }
        finally { AiBusy = false; }
    }

    /// <summary>一句话生成整套设定：把「我想有个什么样的人」直接变成一份可用角色卡。</summary>
    [RelayCommand]
    private async Task AiWholeCard()
    {
        var brief = (AiBrief ?? "").Trim();
        if (brief.Length == 0)
        {
            Status = "先用一句话说想要什么样的角色（例如：青梅竹马的邻居姐姐，学医，嘴上嫌弃其实很惦记人）。";
            return;
        }
        AiBusy = true;
        HasAiDraft = false;
        AiTarget = "整套设定";
        Status = "AI 正在写整套设定…";
        try
        {
            var history = new List<ChatMessage>
            {
                new() { Role = "system", Content = "你是角色卡作者。按行输出，不要 markdown、不要解释，行首标签必须用中文冒号：\n" +
                    "名字：\n性别：\n昵称：\n称呼：\n性格：\n背景：\n开场白：\n" +
                    "性格两三句，背景两三句，开场白一句。称呼是 TA 对使用者的叫法。" },
                new() { Role = "user", Content = "按这个描述写一份角色卡：" + brief }
            };
            var text = await _api.Chat(history);
            if (string.IsNullOrWhiteSpace(text))
            {
                Status = "AI 没有回应：检查设置 → AI 里的接口与密钥。";
                return;
            }
            AiDraft = text.Trim();
            HasAiDraft = true;
            Status = "整套设定写好了：「用这一版」会把它填进字段（还要按保存才落盘）。";
        }
        catch (Exception ex)
        {
            Status = "AI 出错了：" + ex.Message;
            App.WriteLog("Studio.AiWholeCard -> " + ex);
        }
        finally { AiBusy = false; }
    }

    [RelayCommand]
    private void ApplyAi()
    {
        var draft = (AiDraft ?? "").Trim();
        if (draft.Length == 0) return;
        switch (AiTarget)
        {
            case "性格": Personality = draft; break;
            case "背景": Description = draft; break;
            case "开场白": Greeting = draft; break;
            case "整套设定":
                foreach (var (key, value) in StudioCopy.ParseCard(draft))
                {
                    switch (key)
                    {
                        case "名字": Name = value; break;
                        case "性别": Gender = value; break;
                        case "昵称": Nickname = value; break;
                        case "称呼": UserAddress = value; break;
                        case "性格": Personality = value; break;
                        case "背景": Description = value; break;
                        case "开场白": Greeting = value; break;
                    }
                }
                break;
        }
        DisplayName = Name;
        Headline = string.IsNullOrWhiteSpace(Nickname) ? Name : $"{Name}·{Nickname}";
        HasAiDraft = false;
        AiDraft = "";
        Status = $"已把 AI 的{AiTarget}填进字段，按「保存」落盘。";
    }

    [RelayCommand]
    private void DiscardAi()
    {
        HasAiDraft = false;
        AiDraft = "";
        Status = "这一版 AI 的稿子丢掉了。";
    }

    [RelayCommand]
    private Task RegenerateAi() => Ai(AiTarget);

    // ── 试句话 ──────────────────────────────────────────────────────────

    /// <summary>
    /// 先听听 TA 怎么接话：拿**这一屏此刻填着的设定**（包括还没保存的改动）发一轮对话。
    ///
    /// 走 ApiGateway 而不是 ChatEngine，是有意为之的两条理由：
    /// · ChatEngine 会把这一句写进 TA 的记忆、关系网和子人格，还会用盘上那份人设——
    ///   试话要问的是「我刚改的这一句有没有味道」，不该污染存档，更不该在一张还没建好的草稿上
    ///   凭空攒出一份记忆；
    /// · 所以这几句是**读过就算**的：界面上也照实这么写，不让用户以为聊过就记得了。
    /// 接口没配、没回应都如实回一句，不假装聊上了。
    /// </summary>
    [RelayCommand]
    private async Task Talk()
    {
        var line = (TalkInput ?? "").Trim();
        if (line.Length == 0)
        {
            TalkHint = "先写一句话——比如「今天怎么这么早」。";
            return;
        }
        if (TalkBusy) return;
        TalkBusy = true;
        TalkInput = "";
        TalkHint = "";
        var who = string.IsNullOrWhiteSpace(Name) ? "TA" : Name.Trim();
        TalkLines.Add(new TalkLine("我", line, true));
        TalkLines.Add(new TalkLine(who, "在想…", false, true));
        OnPropertyChanged(nameof(HasTalkLines));
        Status = $"正在听「{who}」怎么接…";
        try
        {
            var history = new List<ChatMessage>
            {
                new() { Role = "system", Content = TalkPersona(who) }
            };
            history.AddRange(TalkLines.Where(t => !t.IsPending).TakeLast(TalkWindowLines)
                .Select(t => new ChatMessage { Role = t.FromMe ? "user" : "assistant", Content = t.Text }));
            var reply = await _api.Chat(history);
            var slot = TalkLines.Count - 1;
            if (string.IsNullOrWhiteSpace(reply))
            {
                TalkLines[slot] = new TalkLine(who, "没接到话：检查设置 → AI 里的接口与密钥。", false);
                Status = "TA 没接话——这一句试话没成功。";
            }
            else
            {
                TalkLines[slot] = new TalkLine(who, reply.Trim(), false);
                Status = $"「{who}」回了一句（这几句只在这一屏，不进 TA 的记忆）。";
            }
        }
        catch (Exception ex)
        {
            TalkLines[TalkLines.Count - 1] = new TalkLine(who, "出错了：" + ex.Message, false);
            Status = "试句话出错：" + ex.Message;
            App.WriteLog("Studio.Talk -> " + ex);
        }
        finally
        {
            TalkBusy = false;
            OnPropertyChanged(nameof(HasTalkLines));
        }
    }

    [RelayCommand]
    private void ClearTalk()
    {
        TalkLines.Clear();
        OnPropertyChanged(nameof(HasTalkLines));
        TalkHint = "这几句擦掉了——本来也没写进 TA 的记忆。";
    }

    /// <summary>
    /// 人设从当前字段现拼，不读 <c>_ch</c>：这一屏上刚敲进去的性格要立刻生效，
    /// 否则「试句话」试的还是上一次保存的那份人，用户看不出自己改了什么。
    /// </summary>
    private string TalkPersona(string who)
    {
        var rows = new List<string>
        {
            $"你现在扮演「{who}」"
            + (string.IsNullOrWhiteSpace(Gender) ? "" : $"，{Gender}")
            + (string.IsNullOrWhiteSpace(UserAddress) ? "" : $"，你称呼对方「{UserAddress}」") + "。"
        };
        if (!string.IsNullOrWhiteSpace(Nickname)) rows.Add("小名：" + Nickname);
        if (!string.IsNullOrWhiteSpace(Personality)) rows.Add("性格：" + Personality);
        if (!string.IsNullOrWhiteSpace(Description)) rows.Add("背景：" + Description);
        rows.Add($"与对方的关系：好感 {Affection:0}/100，信任 {Trust:0}/100");
        if (!string.IsNullOrWhiteSpace(Mood))
            rows.Add("此刻心情：" + (MoodChoices.FirstOrDefault(m =>
                string.Equals(m.Key, Mood, StringComparison.OrdinalIgnoreCase))?.Label ?? Mood));
        if (IsRomance) rows.Add("你把对方当成在意的人。");
        rows.Add("怎么回话：用第一人称，像真人一样一到三句，有口头禅和脾气；"
                 + "不要解释自己在扮演、不要提 AI、不要写成旁白。"
                 + "这只是一次试话，对方说的不进入你的记忆。");
        return string.Join("\n", rows);
    }

    // ── 记忆 ────────────────────────────────────────────────────────────

    private async Task RefreshLayersAsync()
    {
        Layers.Clear();
        try
        {
            foreach (var s in await _memory.StatsAsync(_ch.Profile.Id))
                Layers.Add(new StudioLayerRow(s.Name, s.Count + " " + s.Unit, s.Detail));
        }
        catch (Exception ex)
        {
            App.WriteLog("Studio.RefreshLayers -> " + ex);
            Layers.Add(new StudioLayerRow("读不出来", "—", ex.Message));
        }
    }

    /// <summary>
    /// 跨存档共用：写全局设置（这颗开关本来就是全局的，界面上也照实这么说）。
    /// 一改动就要通知作用域丢缓存：各记忆服务是带缓存的单例，不丢会把这份存档的记忆写进另一份的文件里。
    /// </summary>
    [RelayCommand]
    private async Task ToggleCrossSave()
    {
        var next = !CrossSave;
        _settings.Apply(_settings.Current with { CrossSaveMemory = next });
        await _settings.Persist();
        _scope.Raise();
        CrossSave = next;
        ScopeLabel = _scope.IsShared ? "所有存档共用这一份记忆" : $"只在这份存档里（{_scope.Current}）";
        ScopePath = _scope.Key("memories") + ".json";
        MemoryHint = StudioCopy.MemoryScopeHint(_scope.IsShared);
        await RefreshLayersAsync();
        Status = next
            ? "已改成所有存档共用：换存档看到的也是同一批记忆。"
            : "已改回按存档隔离：每份存档各记各的。";
    }

    [RelayCommand]
    private async Task ImportLingshu(string? mode)
    {
        if (mode is not ("append" or "replace")) return;
        // 记忆是按 id 落文件的：往一个还没进名册的 id 上搬，放弃改动后就剩一份没人认领的 json。
        if (_isNew)
        {
            Status = "记忆要写进一个已经存在的人：先按「建好角色」，再搬记忆。";
            return;
        }
        try
        {
            var file = await FilePicker.PickAsync(new PickOptions
            {
                FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    { DevicePlatform.WinUI, new[] { ".json" } },
                    { DevicePlatform.Android, new[] { "application/json" } },
                    { DevicePlatform.iOS, new[] { "public.json" } }
                }),
                PickerTitle = "选择灵枢导出的记忆 JSON"
            });
            if (file is null) return;
            if (!await _lingshu.ValidateAsync(file.FullPath))
            {
                Status = "这份 JSON 认不出来：要的是灵枢导出的记忆文件。";
                return;
            }
            Busy = true;
            Status = mode == "replace" ? "正在替换 TA 记得的一切…" : "正在把记忆追加进去…";
            var (ok, message) = await _lingshu.ImportToCharacterAsync(_ch.Profile.Id, file.FullPath, mode);
            Status = (ok ? "导入成功：" : "导入失败：") + message;
            if (ok) await RefreshLayersAsync();
        }
        catch (Exception ex)
        {
            Status = "导入失败：" + ex.Message;
            App.WriteLog("Studio.ImportLingshu -> " + ex);
        }
        finally { Busy = false; }
    }

    [RelayCommand]
    private Task OpenMemoryManage() => Shell.Current.GoToAsync($"memoryManage?charId={Uri.EscapeDataString(CharacterId)}");

    [RelayCommand]
    private Task OpenSubProfile() => Shell.Current.GoToAsync($"subProfile?charId={Uri.EscapeDataString(CharacterId)}");

    [RelayCommand]
    private Task OpenGraph() => _windows.OpenOrNavigateAsync(PageWindowKeys.Graph, $"关系网 · {DisplayName}",
        typeof(Views.MemoryGraphPage), PageWindowKeys.Graph, new Dictionary<string, object> { ["charId"] = CharacterId });

    [RelayCommand]
    private Task OpenSearch() => _windows.OpenOrNavigateAsync(PageWindowKeys.Search, $"搜事件 · {DisplayName}",
        typeof(Views.MemorySearchPage), PageWindowKeys.Search, new Dictionary<string, object> { ["charId"] = CharacterId });

    [RelayCommand]
    private Task OpenMentalHealth() => Shell.Current.GoToAsync("mentalHealth");

    [RelayCommand]
    private async Task ResetSubProfile()
    {
        var yes = await Shell.Current.DisplayAlert("副设定回正",
            $"把「{Name}」的副设定退回主设定的副本？这会清空累积的当下心境与心事，主设定不受影响。",
            "回正", "取消");
        if (!yes) return;
        if (await _subProfiles.ResetAsync(CharacterId))
        {
            var sub = CharacterSubProfileService.Seed(_ch.Profile, _ch.SubProfile);
            SubTone = sub.Tone;
            SubNotes = "（刚回正，还没有新的心事）";
            SubDiff = _subProfiles.DiffSummary(CharacterId);
            Status = "副设定已回正到主设定。";
        }
        else Status = "回正没成功：这个角色可能不在当前名单里。";
    }

    // ── 关联 ────────────────────────────────────────────────────────────

    [RelayCommand]
    private void ToggleMain()
    {
        if (IsMain)
        {
            _engine.State.CharacterId = "";
            IsMain = false;
            MainLabel = "设为主角";
            Status = $"已经取消「{Name}」的主角标记。";
        }
        else
        {
            _engine.SetCharacter(CharacterId);
            IsMain = true;
            MainLabel = "当前主角";
            Status = $"「{Name}」现在是主角，回到角色选择页可以和 TA 开始新的一局。";
        }
        MetaLine = StudioCopy.Meta(Gender, UserAddress, IsRomance);
        Saved?.Invoke();
    }

    [RelayCommand]
    private void ToggleAllowed()
    {
        var allowed = _engine.State.AllowedCharacters;
        var id = CharacterId;
        if (allowed.Contains(id))
        {
            allowed.Remove(id);
            // 空名单的语义是「不限制」：原来不限制时把一个人移出去，得把其余所有人都写进名单，
            // 否则移除反而变成「所有人都能用」。
            if (allowed.Count == 0)
                foreach (var c in _engine.Roster.Values.Where(c => c.Profile.Id != id))
                    allowed.Add(c.Profile.Id);
            AllowedInSave = false;
            Status = $"已把「{Name}」移出本存档的可用配角。";
        }
        else
        {
            allowed.Add(id);
            AllowedInSave = true;
            Status = $"已把「{Name}」加进本存档的可用配角。";
        }
        AllowedHint = StudioCopy.Whitelist(AllowedInSave, _engine.State.AllowedCharacters.Count);
        Saved?.Invoke();
    }

    [RelayCommand]
    private async Task Duplicate(string? withMemory)
    {
        var takeMemory = withMemory == "1";
        var name = string.IsNullOrWhiteSpace(DuplicateName) ? Name + "（副本）" : DuplicateName.Trim();
        var (ok, message, _, _) = await _library.DuplicateAsync(CharacterId, name, takeMemory);
        Status = message;
        if (ok) Saved?.Invoke();
    }

    // ── 保存 / 放弃 ─────────────────────────────────────────────────────

    /// <summary>把字段一次性写回角色并落盘；只写改动过的部分由「谁被 Note 过」决定。新建那一张走登记，不走覆盖。</summary>
    [RelayCommand]
    private async Task Save()
    {
        if (!IsDirty) { Status = "没有要保存的改动。"; return; }
        // 空名字进名册，得到处都是「（无名）」；性别留空则会一路按「未知」拼提示词。
        // 这两栏是新建的唯一门槛，说清楚比默默补一个「小雨」诚实。
        if (_isNew && string.IsNullOrWhiteSpace(Name))
        {
            Status = "先给 TA 起个名字——没有名字进不了名册。";
            return;
        }
        if (_isNew && string.IsNullOrWhiteSpace(Gender))
        {
            Status = "性别也挑一颗（想要别的，设置 → 性别库里加一个，这里立刻多一颗）。";
            return;
        }
        Busy = true;
        try
        {
            var p = _ch.Profile;
            // 状态必须整份换新而不是就地改：CharacterData 的 with 只复制引用，
            // 而 _ch 就是名单里那一份——就地写等于没点「保存」已经改了内存里的角色，
            // 「放弃修改」会原样读回那被改过的一份，等于按钮是假的。
            var st = _ch.State with
            {
                Affection = Clamp(Affection, 0, 100),
                Trust = Clamp(Trust, 0, 100),
                Energy = Clamp(Energy, 0, 100),
                AffectionPoints = ParseInt(AffectionPoints, _ch.State.AffectionPoints),
                Mood = string.IsNullOrWhiteSpace(Mood) ? "normal" : Mood.Trim().ToLowerInvariant(),
                Location = Fallback(Location, _ch.State.Location),
                CurrentOutfit = Fallback(SelectedOutfit, _ch.State.CurrentOutfit),
                CurrentEmotion = Fallback(SelectedEmotion, _ch.State.CurrentEmotion),
                IsFacingLeft = FacingLeft,
                Cycle = _ch.State.Cycle with
                {
                    Tracked = CycleTracked,
                    CycleDay = ParseInt(CycleDay, _ch.State.Cycle.CycleDay),
                    Phase = Fallback(CyclePhase, _ch.State.Cycle.Phase)
                }
            };
            var updated = _ch with
            {
                Profile = p with
                {
                    Name = Fallback(Name, p.Name),
                    Gender = Fallback(Gender, p.Gender),
                    Nickname = Nickname?.Trim() ?? "",
                    UserAddress = Fallback(UserAddress, p.UserAddress),
                    Personality = Fallback(Personality, p.Personality),
                    Description = Description?.Trim() ?? "",
                    Greeting = Greeting?.Trim() ?? "",
                    IsRomanceTarget = IsRomance
                },
                State = st,
                Overrides = BuildOverrides()
            };

            // 同一份「写盘」动作，对人是两件：草稿要登记，盘上的人要覆盖。
            var wrote = _isNew ? await _library.AddAsync(updated) : await _library.UpdateAsync(updated);
            if (!wrote)
            {
                Status = _isNew ? "建不了：名册没能写进盘（详见日志）。" : "保存失败：角色资料没能写进盘（详见日志）。";
                return;
            }
            var justCreated = _isNew;
            // 进过名册就不再是草稿了：之后这颗按钮做的事回到「保存」，关联那一栏也才终于有意义。
            _isNew = false;
            _draft = null;
            _ch = updated;
            CharacterId = updated.Profile.Id;
            DisplayName = updated.Profile.Name;
            Headline = string.IsNullOrWhiteSpace(updated.Profile.Nickname)
                ? updated.Profile.Name : $"{updated.Profile.Name}·{updated.Profile.Nickname}";
            MetaLine = StudioCopy.Meta(updated.Profile.Gender, updated.Profile.UserAddress, updated.Profile.IsRomanceTarget);
            CycleNote = StudioCopy.CycleNote(updated.State.Cycle);
            SubDiff = _subProfiles.DiffSummary(CharacterId);
            AllowedInSave = AllowedNow(CharacterId);
            AllowedHint = StudioCopy.Whitelist(AllowedInSave, _engine.State.AllowedCharacters.Count);
            IsDirty = false;
            DirtyLabel = "";
            _changed.Clear();
            Status = justCreated
                ? $"「{updated.Profile.Name}」已经进名册。之后改哪儿，还按这一颗按钮。"
                : $"已保存「{updated.Profile.Name}」的一切改动。";
            RaiseAll();
            Saved?.Invoke();
            if (justCreated) Created?.Invoke(CharacterId);
        }
        catch (Exception ex)
        {
            Status = "保存出错：" + ex.Message;
            App.WriteLog("Studio.Save -> " + ex);
        }
        finally { Busy = false; }
    }

    private CharacterOverrides BuildOverrides() => new()
    {
        MemoryTurns = ParseNullableInt(OvMemoryTurns),
        ContextTokenLimit = ParseNullableInt(OvContextLimit),
        AiTemperature = ParseNullableDouble(OvTemperature),
        MemoryWindowDays = ParseNullableDouble(OvWindowDays),
        MemoryRecallRandomness = ParseNullableDouble(OvRecall)
    };

    [RelayCommand]
    private async Task Revert()
    {
        // 草稿没有「盘上那一版」可回：回到刚打开那张空白卡（id 不换，已经画的立绘还认得这个人）。
        if (_isNew) { OpenNew(); Status = "改动已经放弃，回到空白那一张。"; return; }
        await OpenAsync(CharacterId);
        Status = "改动已经放弃，回到盘上那一版。";
    }

    /// <summary>关闭这一层。收缩动画由页面接管（<see cref="CloseRequested"/>），没人接管就直接关。</summary>
    [RelayCommand]
    private async Task Close()
    {
        if (CloseRequested is { } request) await request();
    }

    /// <summary>让只读派生文本（预览标题、脏标、按钮可用性）跟着字段刷新。</summary>
    private void RaiseAll()
    {
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(CanAi));
        OnPropertyChanged(nameof(SaveLabel));
        OnPropertyChanged(nameof(IsNew));
    }

    private static string Fallback(string? candidate, string fallback) =>
        string.IsNullOrWhiteSpace(candidate) ? fallback : candidate.Trim();

    private static int Clamp(double v, int min, int max) => Math.Clamp((int)Math.Round(v), min, max);

    private static int ParseInt(string? raw, int fallback) =>
        int.TryParse((raw ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    private static int? ParseNullableInt(string? raw)
    {
        var t = (raw ?? "").Trim();
        if (t.Length == 0) return null;
        return int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    private static double? ParseNullableDouble(string? raw)
    {
        var t = (raw ?? "").Trim();
        if (t.Length == 0) return null;
        return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
    }
}

/// <summary>
/// 工作室里说人话的那一层：三处界面要表达同一个意思（记忆归谁、白名单现在是什么状态、
/// 周期走到哪一天），写在 XAML 里迟早各说各的，所以收在这里一次说清。
/// </summary>
public static class StudioCopy
{
    public static string Meta(string gender, string userAddress, bool romance)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(gender)) parts.Add(gender);
        parts.Add($"叫对方「{userAddress}」");
        parts.Add(romance ? "恋爱对象" : "不恋爱");
        return string.Join(" · ", parts);
    }

    public static string OutfitName(string key) => key switch
    {
        "default" => "默认装扮",
        "" => "（没有服装）",
        _ => key
    };

    public static string CycleNote(PhysiologicalData cycle)
    {
        if (!cycle.Tracked) return "没有开周期记录：TA 的提示词里不会出现身体周期。";
        var day = cycle.CycleDay > 0 ? $"第 {cycle.CycleDay} 天" : "还没记到天数";
        return $"{day} · 阶段 {cycle.Phase}" +
               (cycle.LastPeriod == default ? "" : $" · 上次经期 {cycle.LastPeriod.ToLocalTime():MM-dd}");
    }

    public static string MemoryScopeHint(bool shared) => shared
        ? "现在所有存档共用同一份记忆：在这份存档里聊过的事，换个存档 TA 也记得。想要「换了存档就重新认识」，把下面那颗开关关掉。"
        : "现在记忆按存档隔离：这一份存档里发生的事只属于这一份。想让它跟着角色跨存档走，打开下面那颗开关（对所有人都生效）。";

    /// <summary>白名单那句话要说全：空名单的语义是「不限制」，只说「TA 在名单里」会让人以为别人不能用。</summary>
    public static string Whitelist(bool allowed, int rosterCount)
    {
        var first = allowed
            ? "TA 在这份存档的可用配角里，聊天时 AI 能找到 TA。"
            : "TA 已被移出这份存档：AI 不会再随手把 TA 拉进对话。";
        var second = rosterCount == 0
            ? "当前名单是空的 = 不限制，角色库里所有人都能被 AI 调用。"
            : $"这份存档已经限定可用配角（名单里 {rosterCount} 人），不在名单里的人不会被叫出来。";
        return first + second;
    }

    /// <summary>解析 AI 给的整套设定：只认「标签：值」这种行，标签用中文冒号结尾。</summary>
    public static IEnumerable<(string Key, string Value)> ParseCard(string text)
    {
        foreach (var rawLine in (text ?? "").Split('\n'))
        {
            var line = rawLine.Trim().TrimStart('-', '·', ' ');
            if (line.Length == 0) continue;
            var cut = line.IndexOf('：');
            if (cut <= 0) continue;
            var key = line[..cut].Trim();
            var value = line[(cut + 1)..].Trim();
            if (value.Length == 0) continue;
            yield return (key, value);
        }
    }
}

/// <summary>
/// 服装 / 表情 / 心情 / 分类标签这些「一根轴上的一个值」。
/// IsOn 要能通知：筛选选中态在点一下之后就变了，而 facet 本身没被重建。
/// </summary>
public sealed class StudioAxisItem : System.ComponentModel.INotifyPropertyChanged
{
    public StudioAxisItem(string key, string label) { Key = key; Label = label; }
    public string Key { get; init; }
    public string Label { get; init; }

    private bool _isOn;
    public bool IsOn
    {
        get => _isOn;
        set
        {
            if (_isOn == value) return;
            _isOn = value;
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsOn)));
        }
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>素材矩阵的一行：一套服装 × 所有已知表情。</summary>
public sealed class StudioArtRow
{
    public StudioArtRow(string outfitKey, string outfitName)
    {
        OutfitKey = outfitKey;
        OutfitName = outfitName;
    }

    public string OutfitKey { get; }
    public string OutfitName { get; }
    public List<StudioArtCell> Cells { get; } = new();
    public string FillLabel { get; set; } = "";
}

/// <summary>素材矩阵的一格：有图能点，缺图点进去是把用户领去生成那一格。</summary>
public sealed class StudioArtCell
{
    public StudioArtCell(string outfit, string emotion, ImageSource? image, bool hasImage, bool isCurrent)
    {
        Outfit = outfit;
        Emotion = emotion;
        OutfitName = StudioCopy.OutfitName(outfit);
        Image = image;
        HasImage = hasImage;
        IsCurrent = isCurrent;
    }

    public string Outfit { get; }
    public string Emotion { get; }
    public string OutfitName { get; }
    public ImageSource? Image { get; }
    public bool HasImage { get; }
    public bool IsCurrent { get; }
    public string CornerLabel => HasImage ? "" : "缺";
}

/// <summary>记忆五层的一行（名字 / 规模 / 最近发生了什么）。</summary>
public sealed class StudioLayerRow
{
    public StudioLayerRow(string name, string count, string detail)
    {
        Name = name;
        Count = count;
        Detail = detail;
    }

    public string Name { get; }
    public string Count { get; }
    public string Detail { get; }
}

/// <summary>
/// 试句话的一口气：谁说的、说了什么。回合换完就整条替换（不原地改文本），
/// 集合里的元素因此是不可变的——绑定不用通知任何东西，永远不会出现「气泡里还挂着『在想…』」。
/// </summary>
public sealed class TalkLine
{
    public TalkLine(string who, string text, bool fromMe, bool pending = false)
    {
        Who = who;
        Text = text;
        FromMe = fromMe;
        IsPending = pending;
    }

    public string Who { get; }
    public string Text { get; }
    public bool FromMe { get; }
    public bool IsPending { get; }
}
