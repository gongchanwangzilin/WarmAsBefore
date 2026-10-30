using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.Controls;
using WarmAsBefore.Services;

namespace WarmAsBefore.ViewModels;

/// <summary>悬停按钮目录项：id 是设置里存的键，glyph/name 只服务这一页的展示。</summary>
public sealed partial class PetHoverOption : ObservableObject
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Glyph { get; init; } = "";
    [ObservableProperty] private bool _isOn;
}

/// <summary>
/// 桌宠设置页的模型。
///
/// 写入契约：
/// · 拖动中的滑杆 → SettingsManager.Apply()（只改内存 + 触发 Applied，桌宠与预览即时跟随）；
/// · 松手 / 切走 / 离开页面 → Persist()（350ms 去抖落盘）。
/// 开关与下拉是单次事件，直接 Apply + Persist。
///
/// 读出声、音色/音高/语速、回应方式、「让她自己调桌宠设置」都在这一页里，
/// 行为入口分别在 SpeechService（朗读）、PetPage（回应节奏）与 PetService（自主配置指令）。
/// </summary>
public sealed partial class PetSettingsViewModel : ObservableObject
{
    private readonly SettingsManager _settings;
    private readonly GameEngine _engine;
    private readonly CharacterLibrary _chars;
    private readonly StorageProvider _store;
    private readonly PetStudioService _studio;
    private bool _loading = true;
    private bool _reverting;   // 「最多 6 个」驳回时压住再入
    private System.Threading.Timer? _persistTimer;
    private readonly object _persistLock = new();

    public PetSettingsViewModel(SettingsManager settings, GameEngine engine, CharacterLibrary chars,
        StorageProvider store, PetStudioService studio)
    {
        _settings = settings;
        _engine = engine;
        _chars = chars;
        _store = store;
        _studio = studio;
        LoadVoiceOptions();
        BuildHoverCatalog();
        LoadFrom(_settings.Current);
        _loading = false;
        _ = RefreshPreviewSpriteAsync();
        _ = RefreshPackStatusAsync();
        _ = RefreshBindingOptionsAsync();
    }

    // ============ 绑定角色（跟随存档 / 指定一个） ============

    private const string FollowSaveLabel = "跟随存档主角";
    private readonly List<(string Id, string Name)> _bindingChoices = new();
    private bool _suppressBindingWrite;

    public System.Collections.ObjectModel.ObservableCollection<string> PetBindingOptions { get; } = new() { FollowSaveLabel };

    [ObservableProperty] private string _petBindingDisplay = FollowSaveLabel;

    partial void OnPetBindingDisplayChanged(string value)
    {
        if (_suppressBindingWrite) return;
        var id = _bindingChoices.FirstOrDefault(x => x.Name == value).Id ?? "";
        _settings.Apply(_settings.Current with { PetCharacterId = id });
        _ = _settings.Persist();
        _ = RefreshPreviewSpriteAsync();
        // 桌面上已经在演的那只立刻换脸；不在桌宠模式时是 no-op。
        _ = Services.PetService.RefreshForActiveSaveAsync();
    }

    /// <summary>选项 = 「跟随存档主角」+ 角色库全部角色；当前值按 PetCharacterId 回填。</summary>
    public async Task RefreshBindingOptionsAsync()
    {
        try
        {
            _suppressBindingWrite = true;
            var roster = await _chars.ListAsync();
            _bindingChoices.Clear();
            PetBindingOptions.Clear();
            _bindingChoices.Add(("", FollowSaveLabel));
            PetBindingOptions.Add(FollowSaveLabel);
            foreach (var c in roster)
            {
                _bindingChoices.Add((c.Profile.Id, c.Profile.Name));
                PetBindingOptions.Add(c.Profile.Name);
            }
            var bound = _settings.Current.PetCharacterId ?? "";
            PetBindingDisplay = _bindingChoices.FirstOrDefault(x => x.Id == bound).Name ?? FollowSaveLabel;
        }
        catch (Exception ex) { App.WriteLog("PetSettingsVM.Binding -> " + ex.Message); }
        finally { _suppressBindingWrite = false; }
    }

    /// <summary>桌宠行为的 Windows 专有项（发光/帧率/悬停/位置）在移动端没有对应实现，整卡收起。</summary>
    public bool IsDesktop => DeviceInfo.Idiom == DeviceIdiom.Desktop;

    // ============ 装载 / 回写 ============

    private void LoadFrom(Models.UserSettings s)
    {
        _loading = true;
        PetScale = Math.Clamp(s.PetScale, 0.4, 2.6);
        PetFontSize = s.PetFontSize;
        PetGlow = Math.Clamp(s.PetGlow, 0, 1);
        PetFps = Math.Clamp(s.PetFps, 10, 60);
        PetFpsLock = s.PetFpsLock;
        PetBubbleSpeed = Math.Clamp(s.PetBubbleSpeed, 0.2, 6);
        PetStatusBubble = s.PetStatusBubble;
        PetInputBubble = s.PetInputBubble;
        PetTypingSound = s.PetTypingSound;
        foreach (var o in HoverOptions)
            o.IsOn = s.PetHoverButtons.Contains(o.Id);
        // 已选顺序按设置里的顺序重排：目录里排前但用户后选的，应排在后头
        var ordered = s.PetHoverButtons
            .Where(id => HoverOptions.Any(o => o.Id == id))
            .Select(id => HoverOptions.First(o => o.Id == id))
            .ToList();
        var rest = HoverOptions.Where(o => !ordered.Contains(o)).ToList();
        foreach (var o in ordered) HoverOptions.Remove(o);
        foreach (var o in ordered) HoverOptions.Add(o);
        foreach (var o in rest) HoverOptions.Add(o);
        DoubleClickDisplay = DisplayOf(DoubleClickChoices, s.PetDoubleClick);
        WalkingDisplay = DisplayOf(WalkingChoices, s.PetWalking);
        TapLevel = s.PetSfxLevels.TryGetValue("tap", out var lvTap) ? Math.Clamp(lvTap, 0, 1) : 1.0;
        TypingLevel = s.PetSfxLevels.TryGetValue("typing", out var lvKey) ? Math.Clamp(lvKey, 0, 1) : 1.0;
        PetPurrSeconds = Math.Clamp(s.PetPurrSeconds, 2, 20);
        PetRememberPosition = s.PetRememberPosition;
        PetHideOnFullscreen = s.PetHideOnFullscreen;
        PetChromeCrossScreen = s.PetChromeCrossScreen;
        LanguageDisplay = DisplayOf(LanguageChoices, s.PetLanguage);
        PetIdleMinutes = s.PetIdleMinutes;
        ThinkDisplay = DisplayOf(ThinkChoices, s.PetThinkMode);
        PetThinkIntervalMinutes = s.PetThinkIntervalMinutes;
        PetSpeakReplies = s.PetSpeakReplies;
        VoiceDisplay = string.IsNullOrWhiteSpace(s.PetVoiceName) || !VoiceOptions.Contains(s.PetVoiceName)
            ? "跟随全局" : s.PetVoiceName;
        PetVoicePitch = Math.Clamp(s.PetVoicePitch, 0.5, 2);
        PetVoiceRate = Math.Clamp(s.PetVoiceRate, 0.5, 2);
        RespondDisplay = DisplayOf(RespondChoices, s.PetRespondMode);
        PetSelfConfig = s.PetSelfConfig;
        _loading = false;
        RebuildActive();
        OnPropertyChanged(nameof(StageReadout));
        OnPropertyChanged(nameof(AnchorReadout));
    }

    /// <summary>把这一页的每一项原样写回 UserSettings（record 只能 with 整份重建）。</summary>
    private void Commit(bool persist)
    {
        if (_loading) return;
        // 呼噜/说话的音量这一档这里没有：音效库里还没有那两格卡槽。
        // 它们原来的值原样带回去，不在这页里悄悄清掉。
        var sfx = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var kv in _settings.Current.PetSfxLevels)
            sfx[kv.Key] = Math.Clamp(kv.Value, 0, 1);
        sfx["tap"] = Math.Round(TapLevel, 3);
        sfx["typing"] = Math.Round(TypingLevel, 3);
        var hover = HoverOptions.Where(o => o.IsOn).Select(o => o.Id).Take(6).ToList();
        var next = _settings.Current with
        {
            PetScale = Math.Round(PetScale, 3),
            PetFontSize = (int)Math.Round(PetFontSize),
            PetGlow = Math.Round(PetGlow, 3),
            PetFps = (int)Math.Round(PetFps),
            PetFpsLock = PetFpsLock,
            PetBubbleSpeed = Math.Round(PetBubbleSpeed, 2),
            PetStatusBubble = PetStatusBubble,
            PetInputBubble = PetInputBubble,
            PetTypingSound = PetTypingSound,
            PetHoverButtons = hover,
            PetDoubleClick = KeyOf(DoubleClickChoices, DoubleClickDisplay),
            PetWalking = KeyOf(WalkingChoices, WalkingDisplay),
            PetSfxLevels = sfx,
            PetPurrSeconds = (int)Math.Round(PetPurrSeconds),
            PetRememberPosition = PetRememberPosition,
            PetHideOnFullscreen = PetHideOnFullscreen,
            PetChromeCrossScreen = PetChromeCrossScreen,
            PetLanguage = KeyOf(LanguageChoices, LanguageDisplay),
            PetIdleMinutes = PetIdleMinutes,
            PetThinkMode = KeyOf(ThinkChoices, ThinkDisplay),
            PetThinkIntervalMinutes = PetThinkIntervalMinutes,
            PetSpeakReplies = PetSpeakReplies,
            PetVoiceName = VoiceDisplay == "跟随全局" ? "" : VoiceDisplay,
            PetVoicePitch = Math.Round(PetVoicePitch, 2),
            PetVoiceRate = Math.Round(PetVoiceRate, 2),
            PetRespondMode = KeyOf(RespondChoices, RespondDisplay),
            PetSelfConfig = PetSelfConfig,
        };
        // Apply 会触发 SettingsManager.Applied → PetService 把运行时项推给分层窗口与头顶小窗，
        // 这里不需要（也不该）再手动调一遍，否则每次 Applied 都会重复推。
        _settings.Apply(next);
        if (persist) _ = _settings.Persist();
        else SchedulePersist();
    }

    private void SchedulePersist()
    {
        lock (_persistLock)
        {
            _persistTimer ??= new System.Threading.Timer(
                _ => Helpers.Ui.Post(() => _ = _settings.Persist()), null, Timeout.Infinite, Timeout.Infinite);
            _persistTimer.Change(350, Timeout.Infinite);
        }
    }

    /// <summary>离开页面时把拖动中攒下的改动立刻落盘。</summary>
    public void Flush()
    {
        lock (_persistLock) _persistTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        _ = _settings.Persist();
    }

    // ============ 选项目录（显示名 ↔ 存储键） ============

    private static readonly (string Display, string Key)[] DoubleClickChoices =
        { ("回到主窗口", "home"), ("说句话", "talk"), ("什么都不做", "none") };
    private static readonly (string Display, string Key)[] WalkingChoices =
        { ("站着不动", "off"), ("沿屏幕底沿散步", "roam") };
    private static readonly (string Display, string Key)[] LanguageChoices =
        { ("跟随全局", ""), ("简体中文", "zh-CN"), ("繁體中文", "zh-TW"), ("English", "en"), ("日本語", "ja") };
    private static readonly (string Display, string Key)[] ThinkChoices =
        { ("不主动思考", "off"), ("随机嘀咕", "random"), ("定时嘀咕", "timer") };
    private static readonly (string Display, string Key)[] RespondChoices =
        { ("有话就说", "always"), ("被点到才答", "click"), ("少插话", "quiet") };

    private static string DisplayOf((string Display, string Key)[] table, string key) =>
        table.FirstOrDefault(t => t.Key == key).Display ?? table[0].Display;
    private static string KeyOf((string Display, string Key)[] table, string display) =>
        table.FirstOrDefault(t => t.Display == display).Key ?? table[0].Key;

    // Picker 绑定的显示名清单（一次建好，避免每次绑定都新造列表）
    public List<string> DoubleClickOptions { get; } = DoubleClickChoices.Select(c => c.Display).ToList();
    public List<string> WalkingOptions { get; } = WalkingChoices.Select(c => c.Display).ToList();
    public List<string> LanguageOptions { get; } = LanguageChoices.Select(c => c.Display).ToList();
    public List<string> ThinkOptions { get; } = ThinkChoices.Select(c => c.Display).ToList();
    public List<string> RespondOptions { get; } = RespondChoices.Select(c => c.Display).ToList();

    /// <summary>
    /// 音色清单：第一项是「跟随全局」，其后是朗读通道认得的名字——
    /// 桌面端列系统声音，API 模式列 OpenAI 那几个常见音色；两边都列，换引擎时不用重进页面。
    /// </summary>
    public ObservableCollection<string> VoiceOptions { get; } = new() { "跟随全局" };

    private void LoadVoiceOptions()
    {
        foreach (var n in new[] { "alloy", "echo", "fable", "onyx", "nova", "shimmer" })
            VoiceOptions.Add(n);
#if WINDOWS
        try
        {
            foreach (var v in Windows.Media.SpeechSynthesis.SpeechSynthesizer.AllVoices)
                if (!string.IsNullOrWhiteSpace(v.DisplayName) && !VoiceOptions.Contains(v.DisplayName))
                    VoiceOptions.Add(v.DisplayName);
        }
        catch (Exception ex) { App.WriteLog("PetSettings.LoadVoices -> " + ex.Message); }
#endif
    }

    // ============ 悬停按钮目录 ============

    public ObservableCollection<PetHoverOption> HoverOptions { get; } = new();
    public ObservableCollection<PetHoverOption> ActiveHoverOptions { get; } = new();

    private void BuildHoverCatalog()
    {
        // sleep（睡觉）没有现成的休眠表现，暂不列出；接线后加回
        foreach (var (id, name, glyph) in new[]
                 {
                     ("talk", "说话", "💬"), ("think", "想一次", "💭"), ("pet", "摸摸头", "🤚"),
                     ("purr", "呼噜", "💗"), ("walk", "走走", "🚶"), ("settings", "设置", "⚙"), ("home", "回窗口", "🏠"),
                 })
        {
            var opt = new PetHoverOption { Id = id, Name = name, Glyph = glyph };
            opt.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName != nameof(PetHoverOption.IsOn)) return;
                OnHoverToggled(opt);
            };
            HoverOptions.Add(opt);
        }
    }

    private void OnHoverToggled(PetHoverOption opt)
    {
        if (_loading || _reverting) return;
        var count = HoverOptions.Count(o => o.IsOn);
        if (opt.IsOn && count > 6)
        {
            _reverting = true;
            opt.IsOn = false;
            _reverting = false;
            HoverHint = "悬停按钮最多 6 个——先移除一个再换。";
            return;
        }
        HoverHint = "";
        RebuildActive();
        // Commit 触发 Applied → PetService 把新按钮排推给分层窗口，预览与桌面同一次改动里一起跟上
        Commit(true);
    }

    private void RebuildActive()
    {
        ActiveHoverOptions.Clear();
        foreach (var o in HoverOptions.Where(o => o.IsOn)) ActiveHoverOptions.Add(o);
        OnPropertyChanged(nameof(HasHoverButtons));
    }

    public bool HasHoverButtons => ActiveHoverOptions.Count > 0;

    [RelayCommand]
    private void MoveHoverUp(PetHoverOption? opt)
    {
        if (opt is null) return;
        var i = HoverOptions.IndexOf(opt);
        if (i <= 0) return;
        HoverOptions.Move(i, i - 1);
        RebuildActive();
        Commit(true);
    }

    [RelayCommand]
    private void MoveHoverDown(PetHoverOption? opt)
    {
        if (opt is null) return;
        var i = HoverOptions.IndexOf(opt);
        if (i < 0 || i >= HoverOptions.Count - 1) return;
        HoverOptions.Move(i, i + 1);
        RebuildActive();
        Commit(true);
    }

    // ============ 可观察设置项 ============

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PetScaleText))]
    [NotifyPropertyChangedFor(nameof(PreviewBodyHeight))]
    [NotifyPropertyChangedFor(nameof(PreviewGlowSize))]
    [NotifyPropertyChangedFor(nameof(StageReadout))]
    private double _petScale = 1.0;
    partial void OnPetScaleChanged(double value) => Commit(false);
    public string PetScaleText => ((int)Math.Round(Math.Clamp(PetScale, 0.4, 2.6) * 100)) + "%";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StageFontText))]
    [NotifyPropertyChangedFor(nameof(StageReadout))]
    private double _petFontSize = 12;
    partial void OnPetFontSizeChanged(double value) => Commit(false);
    public string StageFontText => ((int)Math.Round(PetFontSize)) + " px";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StageGlowOpacity))]
    [NotifyPropertyChangedFor(nameof(PreviewGlowSize))]
    [NotifyPropertyChangedFor(nameof(StageReadout))]
    private double _petGlow;
    partial void OnPetGlowChanged(double value) => Commit(false);
    public double StageGlowOpacity => Math.Clamp(PetGlow, 0, 1) * 0.85;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FpsText))]
    [NotifyPropertyChangedFor(nameof(StageReadout))]
    private double _petFps = 30;
    partial void OnPetFpsChanged(double value) => Commit(false);
    public string FpsText => ((int)Math.Round(PetFps)) + " fps";

    [ObservableProperty] private bool _petFpsLock = true;
    partial void OnPetFpsLockChanged(bool value) => Commit(true);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SpeedText))]
    private double _petBubbleSpeed = 1.0;
    partial void OnPetBubbleSpeedChanged(double value) => Commit(false);
    public string SpeedText => "×" + PetBubbleSpeed.ToString("0.#");

    [ObservableProperty] private bool _petStatusBubble = true;
    partial void OnPetStatusBubbleChanged(bool value) => Commit(true);

    [ObservableProperty] private bool _petInputBubble = true;
    partial void OnPetInputBubbleChanged(bool value) => Commit(true);

    [ObservableProperty] private bool _petTypingSound;
    partial void OnPetTypingSoundChanged(bool value) => Commit(true);

    [ObservableProperty] private string _doubleClickDisplay = "回到主窗口";
    partial void OnDoubleClickDisplayChanged(string value) => Commit(true);

    [ObservableProperty] private string _walkingDisplay = "站着不动";
    partial void OnWalkingDisplayChanged(string value) => Commit(true);

    [ObservableProperty] private double _tapLevel = 1;
    partial void OnTapLevelChanged(double value) => Commit(false);
    [ObservableProperty] private double _typingLevel = 1;
    partial void OnTypingLevelChanged(double value) => Commit(false);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PurrText))]
    private double _petPurrSeconds = 6;
    partial void OnPetPurrSecondsChanged(double value) => Commit(false);
    public string PurrText => ((int)Math.Round(PetPurrSeconds)) + " 秒";

    [ObservableProperty] private bool _petRememberPosition = true;
    partial void OnPetRememberPositionChanged(bool value) => Commit(true);

    [ObservableProperty] private bool _petHideOnFullscreen;
    partial void OnPetHideOnFullscreenChanged(bool value) => Commit(true);

    [ObservableProperty] private bool _petChromeCrossScreen;
    partial void OnPetChromeCrossScreenChanged(bool value) => Commit(true);

    [ObservableProperty] private string _languageDisplay = "跟随全局";
    partial void OnLanguageDisplayChanged(string value) => Commit(true);

    public List<int> IdleChoices { get; } = new() { 0, 2, 5, 10, 15, 30, 60 };
    [ObservableProperty] private int _petIdleMinutes;
    partial void OnPetIdleMinutesChanged(int value) => Commit(true);

    [ObservableProperty] private string _thinkDisplay = "不主动思考";
    partial void OnThinkDisplayChanged(string value) => Commit(true);

    public List<int> ThinkIntervalChoices { get; } = new() { 5, 10, 15, 30, 60 };
    [ObservableProperty] private int _petThinkIntervalMinutes = 10;
    partial void OnPetThinkIntervalMinutesChanged(int value) => Commit(true);

    // ============ 说话声 ============

    [ObservableProperty] private bool _petSpeakReplies;
    partial void OnPetSpeakRepliesChanged(bool value) => Commit(true);

    [ObservableProperty] private string _voiceDisplay = "跟随全局";
    partial void OnVoiceDisplayChanged(string value) => Commit(true);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VoicePitchText))]
    private double _petVoicePitch = 1.0;
    partial void OnPetVoicePitchChanged(double value) => Commit(false);
    public string VoicePitchText => "×" + PetVoicePitch.ToString("0.00");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VoiceRateText))]
    private double _petVoiceRate = 1.0;
    partial void OnPetVoiceRateChanged(double value) => Commit(false);
    public string VoiceRateText => "×" + PetVoiceRate.ToString("0.00");

    // ============ 回应方式 / 自主配置 ============

    [ObservableProperty] private string _respondDisplay = "有话就说";
    partial void OnRespondDisplayChanged(string value) => Commit(true);

    [ObservableProperty] private bool _petSelfConfig;
    partial void OnPetSelfConfigChanged(bool value) => Commit(true);

    [ObservableProperty] private string _hoverHint = "";

    // ============ 位置：记住 / 清除锚点 ============

    public string AnchorReadout
    {
        get
        {
            var s = _settings.Current;
            if (s.PetAnchorX is int x && s.PetAnchorY is int y)
                return $"已记住：({x}, {y})（虚拟桌面物理像素）";
            return "还没记过位置——拖到喜欢的地方再点「记住当前位置」。";
        }
    }

    [RelayCommand]
    private void RememberPosition()
    {
        if (!Services.PetService.CaptureAnchor())
        {
            HoverHint = "角色没在桌面上——先进桌宠，再点「记住当前位置」。";
            return;
        }
        HoverHint = "";
        OnPropertyChanged(nameof(AnchorReadout));
    }

    [RelayCommand]
    private void ClearAnchor()
    {
        _settings.Apply(_settings.Current with { PetAnchorX = null, PetAnchorY = null });
        _ = _settings.Persist();
        OnPropertyChanged(nameof(AnchorReadout));
    }

    // ============ 预览 ============

    /// <summary>预览立绘：与桌宠同一张 PNG（角色当前服装/心情对应的那帧）。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPreviewFallback))]
    private ImageSource? _previewSpriteSource;

    /// <summary>没有立绘才画那张兜底笑脸——两张叠在同一格，兜底是实心的，会把人整个盖住。</summary>
    public bool ShowPreviewFallback => PreviewSpriteSource is null;
    [ObservableProperty] private string _previewFace = "(˶ᵔ ᵕ ᔔ˶)";
    [ObservableProperty] private string _previewTyped = "";

    public double PreviewBodyHeight => Math.Clamp(168 * PetScale, 56, 264);
    public double PreviewGlowSize => PreviewBodyHeight * (1.15 + 0.5 * Math.Clamp(PetGlow, 0, 1));

    public string StageReadout =>
        $"体型 {PetScaleText} · 字号 {StageFontText} · 发光 {(int)Math.Round(PetGlow * 100)}% · {FpsText}{(PetFpsLock ? "（锁定）" : "（放开跑）")} · 悬停 {ActiveHoverOptions.Count} 键 · 气泡 ×{PetBubbleSpeed.ToString("0.#")}";

    /// <summary>预览打字机用：每字毫秒（1× ≈ 正文阅读速度）。</summary>
    public double TypedIntervalMs => Math.Clamp(60 / Math.Clamp(PetBubbleSpeed, 0.2, 6), 8, 320);

    private static readonly string[] PreviewLines =
    {
        "今天也要好好吃饭呀。",
        "你在旁边，干活都慢了半拍——不过我不嫌。",
        "窗台上那盆草又冒新芽了，我一直记着浇水的。",
        "累了就歇会儿，桌面上的东西我帮你看着。",
    };
    private int _previewLineIdx;

    /// <summary>预览打字机取一句（代码里循环取）。</summary>
    public string NextPreviewLine()
    {
        var line = PreviewLines[_previewLineIdx % PreviewLines.Length];
        _previewLineIdx++;
        return line;
    }

    /// <summary>音效试听：走桌宠真实播放通道（类别音量与素材解析全在同一处）。</summary>
    [RelayCommand]
    private void PreviewSfx(string? category)
    {
        Services.PetService.PlayPetSfx(string.IsNullOrWhiteSpace(category) ? "typing" : category!);
    }

    private async Task RefreshPreviewSpriteAsync()
    {
        try
        {
            // 与桌面上那只同一套解析：显式绑定优先，否则跟随当前存档主角。
            var resolved = await Services.PetService.ResolvePetCharacterAsync();
            if (resolved is not { } r) return;
            MainThread.BeginInvokeOnMainThread(() => PreviewSpriteSource = ImageSource.FromFile(r.SpritePath));
        }
        catch (Exception ex) { App.WriteLog("PetSettingsVM.PreviewSprite -> " + ex.Message); }
    }

    // ============ Q 版桌宠素材包 ============

    [ObservableProperty] private string _animationPackStatus = "正在看 TA 做过没有…";

    /// <summary>素材包在不在、能播多少——只报盘上的事实；演不演得动是桌面那一侧的事。</summary>
    public async Task RefreshPackStatusAsync()
    {
        try
        {
            var id = _engine.State.CharacterId;
            if (string.IsNullOrEmpty(id))
            {
                AnimationPackStatus = "还没进存档，不知道该看谁的素材包";
                return;
            }
            var pack = await _studio.LoadPackAsync(id);
            if (pack is null || pack.States.Count == 0)
            {
                AnimationPackStatus = "还没做过——站在桌面上的她还是立绘那一张";
                return;
            }
            var withFrames = pack.States.Count(s => s.FrameCount > 0);
            var frames = pack.States.Sum(s => s.FrameCount);
            AnimationPackStatus = withFrames == 0
                ? "包是空的：一帧都没出成，桌宠继续用立绘"
                : $"已就绪：{withFrames} 个状态 · {frames} 帧";
        }
        catch (Exception ex) { App.WriteLog("PetSettingsVM.PackStatus -> " + ex.Message); }
    }

    [RelayCommand]
    private async Task OpenStudio() => await Shell.Current.GoToAsync("petstudio");

    [RelayCommand]
    private async Task OpenUsageStats() => await Shell.Current.GoToAsync("usagestats");

    [RelayCommand]
    private async Task GoBack() => await Shell.Current.GoToAsync("..");
}
