using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WarmAsBefore.Modules.Mcp;
using WarmAsBefore.Services;
using WarmAsBefore.Models;
using WarmAsBefore.Modules.Screen;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO.Compression;

namespace WarmAsBefore.ViewModels;

public sealed partial class CharacterSelectViewModel : ObservableObject
{
    private readonly GameEngine _engine;
    private readonly CharacterLibrary _library;
    private readonly Modules.SaveSystem.SaveManager _save;
    private readonly StorageProvider _store;
    private readonly SceneCalibrationService _cal;

    [ObservableProperty] private ObservableCollection<CharacterCardItem> _cards = new();

    public CharacterSelectViewModel(GameEngine engine, CharacterLibrary library,
        Modules.SaveSystem.SaveManager save, StorageProvider store, SceneCalibrationService cal)
    {
        _engine = engine;
        _library = library;
        _save = save;
        _store = store;
        _cal = cal;
    }

    [RelayCommand]
    private async Task Refresh()
    {
        var list = await _library.ListAsync();
        var allSaves = await _save.List();
        Cards = new ObservableCollection<CharacterCardItem>(list.Select(ch =>
        {
            var saves = allSaves.Where(s => s.Character == ch.Profile.Id).ToList();
            var label = saves.Count == 0
                ? "还没有存档"
                : $"已有 {saves.Count} 个存档 · 最近 {saves.Max(s => s.SavedAt).ToLocalTime():MM-dd HH:mm}";
            ImageSource? avatar = null;
            if (!string.IsNullOrEmpty(ch.Avatar))
            {
                var full = Path.Combine(_store.Root, ch.Avatar);
                if (File.Exists(full)) avatar = ImageSource.FromFile(full);
            }
            return new CharacterCardItem(ch, avatar, label);
        }));
    }

    [RelayCommand]
    private async Task Pick(string id)
    {
        var card = Cards.FirstOrDefault(c => c.Id == id);
        if (card is null) return;
        var ch = card.Data;
        var saves = await _save.ListByCharacter(id);

        // 已有存档：可继续上次相处，也可新开一局；同一角色可有多档
        if (saves.Count > 0)
        {
            var act = await Shell.Current.DisplayActionSheet(
                $"和「{ch.Profile.Name}」再次相遇", "取消", null, "继续上次的相处", "开始新的一局");
            if (act == "继续上次的相处")
            {
                await _library.ListAsync();
                await _save.Load(saves[0].Id);
                await Shell.Current.GoToAsync("main");
                return;
            }
            if (act != "开始新的一局") return;
        }
        else
        {
            var confirm = await Shell.Current.DisplayActionSheet(
                $"和「{ch.Profile.Name}」开始新的一局？", "取消", null, "开始游戏");
            if (confirm != "开始游戏") return;
        }
        // 必须先 Boot（重置 State）再 SetCharacter（写入角色），否则角色会被 Boot 清掉
        _engine.Boot();
        _engine.SetCharacter(id);
        // 新开一局：立绘随机只在这一刻发生，之后一律读档恢复
        if (_engine.ActiveCharacter is { } fresh1) CharacterLibrary.RandomizeSpriteState(fresh1);
        _save.NewRun();
        await Shell.Current.GoToAsync("main");
    }

    [RelayCommand]
    private async Task CreateCharacter()
    {
        var name = await Shell.Current.DisplayPromptAsync("新建角色", "她的名字：", "下一步", "取消", "小雨");
        if (string.IsNullOrWhiteSpace(name)) return;
        var gender = await Shell.Current.DisplayActionSheet("角色性别", "取消", null, "女", "男");
        if (gender is not ("女" or "男")) return;
        var personality = await Shell.Current.DisplayPromptAsync("角色性格", "用几句话描述她的性格：", "创建", "取消", "温柔、害羞、喜欢黏人");
        if (personality is null) return;

        var ch = _library.CreateDefault(name, gender, personality);
        if (await _library.AddAsync(ch))
        {
            await Refresh();
            _engine.Boot();
            _engine.SetCharacter(ch.Profile.Id);
            // 新角色新一局：立绘随机只在这一刻发生
            if (_engine.ActiveCharacter is { } fresh2) CharacterLibrary.RandomizeSpriteState(fresh2);
            _save.NewRun();
            await Shell.Current.GoToAsync("main");
        }
        else
        {
            await Shell.Current.DisplayAlert("新建角色", "创建失败", "好");
        }
    }

    [RelayCommand]
    private async Task ImportCharacter()
    {
        try
        {
            var pick = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "选择角色包",
                FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    { DevicePlatform.WinUI, new[] { ".zip" } },
                    { DevicePlatform.Android, new[] { "application/zip" } },
                    { DevicePlatform.iOS, new[] { "public.zip-archive" } }
                })
            });
            if (pick is null) return;
            var import = await _library.ImportFromZipAsync(pick.FullPath);
            await Shell.Current.DisplayAlert("导入角色", import.ok ? import.message : "导入失败：" + import.message, "好");
            if (import.ok) await Refresh();

            // 素材包里的地图必须走同样的标定流程。这里把队列统一驱动一遍，
            // 而不是等用户切到那个场景时被动撞上标定页。
            if (_cal.PendingCount > 0)
            {
                var n = _cal.PendingCount;
                var go = await Shell.Current.DisplayAlert("地图标定",
                    $"这个素材包带进来 {n} 张地图背景，需要依次标定（落脚点 + 缩放）。现在开始吗？",
                    "开始标定", "稍后再说");
                if (go) await _cal.DrainAsync(SceneCalibrationService.DefaultTargetAspect);
            }
        }
        catch (Exception ex)
        {
            App.WriteLog("CharacterSelect.ImportCharacter -> " + ex);
        }
    }

    [RelayCommand]
    private async Task Back() => await Shell.Current.GoToAsync("..");
}

/// <summary>角色选择页展示项：角色 + 头像 + 该角色名下存档情况。</summary>
public sealed class CharacterCardItem
{
    public CharacterCardItem(CharacterData ch, ImageSource? avatar, string savesLabel)
    {
        Data = ch;
        AvatarSource = avatar;
        SavesLabel = savesLabel;
    }

    public CharacterData Data { get; init; }
    public string Id => Data.Profile.Id;
    public string Name => Data.Profile.Name;
    public string Personality => Data.Profile.Personality;
    public ImageSource? AvatarSource { get; init; }
    public bool HasAvatar => AvatarSource is not null;
    public string SavesLabel { get; init; }

    /// <summary>微信式列表项副标题：优先显示性格，空则给一句默认。</summary>
    public string Subtitle =>
        string.IsNullOrWhiteSpace(Personality) ? "点击开始聊天" : Personality;
}

/// <summary>设置页「表情包库」列表的一行（#12）。</summary>
public sealed class StickerRow
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public string Path { get; init; } = "";
    /// <summary>列表里的描述行文案（没写描述时给提示）。</summary>
    public string DescLabel => string.IsNullOrWhiteSpace(Description) ? "（还没写描述）" : Description;
}

/// <summary>
/// 设置页「多端点轮换」列表的一行：一条 = 供应商 + 某款 AI（模型）+ 该组合的多枚密钥。
/// 列表里可直接改启用开关；任何改动都会通过 PropertyChanged 触发防抖落盘。
/// </summary>
/// <summary>
/// 设置页里的一家厂商：端点 + 密钥 + 它下面的多款模型。
/// UI 只编辑这一层与模型列表，轮换池由 RuntimeConfigurator 展开成「厂商 × 模型」。
/// </summary>
public sealed partial class AiProviderRow : ObservableObject
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N")[..8];

    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _url = "";
    [ObservableProperty] private string _keysText = "";
    [ObservableProperty] private bool _enabled = true;
    /// <summary>该厂商的超时（秒）：留空 = 用全局。</summary>
    [ObservableProperty] private string _timeoutSeconds = "";

    /// <summary>这家厂商下要用的模型（可多款）。</summary>
    public ObservableCollection<AiModelRow> Models { get; } = new();

    public string Title => string.IsNullOrWhiteSpace(Name) ? "（未命名厂商）" : Name;
    /// <summary>模型增减后刷新副标题（模型列表在外部被改动时调用）。</summary>
    public void RefreshSummary() => OnPropertyChanged(nameof(Summary));

    public string Summary
    {
        get
        {
            var keys = SplitKeys(KeysText).Count;
            var on = Models.Count(m => m.Enabled);
            var timeout = ParseTimeout() is { } t ? $" · 超时 {t}s" : "";
            return $"{on}/{Models.Count} 款模型 · {keys} 枚密钥{timeout}";
        }
    }

    public int? ParseTimeout()
    {
        if (string.IsNullOrWhiteSpace(TimeoutSeconds)) return null;
        return int.TryParse(TimeoutSeconds.Trim(), out var v) && v > 0 ? Math.Clamp(v, 3, 600) : null;
    }

    partial void OnNameChanged(string value) => OnPropertyChanged(nameof(Title));
    partial void OnKeysTextChanged(string value) => OnPropertyChanged(nameof(Summary));
    partial void OnTimeoutSecondsChanged(string value) => OnPropertyChanged(nameof(Summary));

    public static List<string> SplitKeys(string? text) =>
        (text ?? "").Split(new[] { '\n', '\r', ',', '，', ';', '；' },
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    public AiProvider ToProvider() => new()
    {
        Id = Id,
        Name = (Name ?? "").Trim(),
        Url = (Url ?? "").Trim(),
        Keys = SplitKeys(KeysText),
        Enabled = Enabled,
        TimeoutSeconds = ParseTimeout(),
        Models = Models.Where(m => !string.IsNullOrWhiteSpace(m.Model))
            .Select(m => new AiProviderModel { Model = m.Model.Trim(), Label = (m.Label ?? "").Trim(), Enabled = m.Enabled })
            .ToList()
    };
}

/// <summary>厂商下面的一款模型（可在列表里单独启停）。</summary>
public sealed partial class AiModelRow : ObservableObject
{
    [ObservableProperty] private string _model = "";
    [ObservableProperty] private string _label = "";
    [ObservableProperty] private bool _enabled = true;

    public string DisplayName => string.IsNullOrWhiteSpace(Label) ? (string.IsNullOrWhiteSpace(Model) ? "（未填模型）" : Model) : Label;
    partial void OnModelChanged(string value) => OnPropertyChanged(nameof(DisplayName));
    partial void OnLabelChanged(string value) => OnPropertyChanged(nameof(DisplayName));
}

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsManager _settings;
    private readonly DesignSystem.Theme.ThemeManager _theme;
    private readonly Modules.RealChat.OfficialChatBridge _bridge;
    private readonly Modules.Mcp.McpOrchestrator _mcp;
    private readonly Modules.Tools.RuntimeManager _runtimes;
    private readonly Modules.Tools.ToolManager _tools;
    private readonly SpeechService _speech;
    private readonly Services.StickerLibrary _stickers;
    private readonly CharacterLibrary _library;
    private readonly Services.VisionService _vision;
    private readonly Services.SkillLibrary _skills;
    private readonly Services.CostEstimatorService _cost;

    public SettingsViewModel(SettingsManager settings, DesignSystem.Theme.ThemeManager theme,
        Modules.RealChat.OfficialChatBridge bridge, Modules.Mcp.McpOrchestrator mcp,
        Modules.Tools.RuntimeManager runtimes, Modules.Sandbox.SandboxPolicy sandbox,
        Modules.Tools.ToolManager tools, SpeechService speech,
        Services.StickerLibrary stickers, CharacterLibrary library, Services.VisionService vision,
        Services.SkillLibrary skills, Services.CostEstimatorService cost)
    {
        _settings = settings;
        _theme = theme;
        _bridge = bridge;
        _mcp = mcp;
        _runtimes = runtimes;
        _tools = tools;
        _skills = skills;
        _cost = cost;
        // 注入 DI 单例（已在 MauiProgram 注册），不再每次 new StorageProvider + 触碰 DPAPI
        _sandbox = sandbox;
        _speech = speech;
        _stickers = stickers;
        _library = library;
        _vision = vision;
        _showcaseUnlocked = settings.Current.DeveloperShowcaseUnlocked;
        PythonManualPath = _runtimes.ManualPythonPath;
        JavaManualPath = _runtimes.ManualJavaPath;

        var s = settings.Current;
        _bgmLevel = s.BgmLevel;
        _sfxLevel = s.SfxLevel;
        _frostOn = s.FrostEnabled;
        _glassOn = s.GlassEnabled;
        _liquidOn = s.LiquidEnabled;
        _glassTranslucency = s.GlassTranslucency;
        _glassReducedTransparency = s.GlassReducedTransparency;
        _glassFrost = s.GlassFrost;
        _glassHeight = s.GlassHeight;
        _backgroundMode = string.IsNullOrEmpty(s.BackgroundMode) ? "none" : s.BackgroundMode;
        _backgroundModeDisplay = BackgroundModeDisplayOf(_backgroundMode);
        _backgroundImagePath = s.BackgroundImagePath ?? "";
        _glassLightX = s.GlassLightX;
        _glassLightY = s.GlassLightY;
        _glassLightZ = s.GlassLightZ;
        _glassLightWidth = s.GlassLightWidth;
        _themeName = string.IsNullOrEmpty(s.ThemeName) ? "classic" : s.ThemeName;
        _themeDisplay = DesignSystem.Theme.ThemeManager.ThemeDisplay(_themeName);
        _complexPlot = s.ComplexPlot;
        _novelTesting = s.NovelTestingEnabled;
        _showAffection = s.ShowAllAffection;
        _affectionLevelUp = s.AffectionLevelUpEnabled;
        _battleEnabled = s.BattleEnabled;
        _battleAiCanStart = s.BattleAiCanStart;
        _lightningEnabled = s.LightningEnabled;
        _lightningProbability = s.LightningProbability.ToString("0.###");
        _rainEventEnabled = s.RainEventEnabled;
        _rainProbability = s.RainProbability.ToString("0.###");
        _lightningSfxPath = s.LightningSfxPath;
        _rainSfxPath = s.RainSfxPath;
        _lightningFramesText = string.Join("\n", s.LightningFrames ?? new List<string>());
        _rainFramesText = string.Join("\n", s.RainFrames ?? new List<string>());
        _weatherEffectFrameMs = s.WeatherEffectFrameMs.ToString();
        _aiTravelAlone = s.AiTravelAlone;
        _allowFollowAi = s.AllowFollowAi;
        _menuRight = s.MenuSide == "right";
        _chatStyle = string.IsNullOrEmpty(s.ChatStyle) ? "galgame" : s.ChatStyle;
        _chatStyleDisplay = ChatStyleDisplayOf(_chatStyle);
        _allowSilence = s.AllowSilence;
        _lang = s.Lang;
        _langDisplay = LangDisplayOf(s.Lang);
        _textSpeed = s.TextSpeed;
        _autoSave = s.AutoSaveEnabled;
        _keySfx = s.KeySfx;

        _aiUrl = s.AiUrl;
        _aiKey = s.AiKey;
        _aiModel = s.AiModel;
        _aiPreset = PresetDisplayOf(s.AiPreset);
        _visionUrl = s.VisionUrl;
        _visionKey = s.VisionKey;
        _visionModel = s.VisionModel;
        foreach (var p in AiPresets) AiPresetChoices.Add(p.Display);
        _aiTemperature = s.AiTemperature;
        _aiMaxTokens = s.AiMaxTokens;
        _deepThink = s.DeepThink;
        _deepModel = s.DeepModel;
        _memoryTurns = s.MemoryTurns;
        _aiTimeoutSeconds = s.AiTimeoutSeconds > 0 ? s.AiTimeoutSeconds : 25;
        _memoryWindowDays = MemoryWindowChoices.Contains(s.MemoryWindowDays) ? s.MemoryWindowDays : HumanMemoryMath.DefaultWindowDays;
        _memoryRecallRandomness = Math.Clamp(s.MemoryRecallRandomness, 0, 1);
        _subProfileAiRewrite = s.SubProfileAiRewrite;
        _semanticAiAssist = s.SemanticAiAssist;
        _smartSavingEnabled = s.SmartSavingEnabled;
        _lightMode = s.LightMode;
        _preferDiscreteGpu = s.PreferDiscreteGpu;
        _gpuStatusText = Services.GpuPreferenceService.IsHighPerformancePreferred()
            ? "当前已登记为「高性能」（重启程序后生效）" : "当前由 Windows 自动选择显卡";

        // 多端点轮换 + 上下文 Token 上限
        _aiRotationEnabled = s.AiRotationEnabled;
        _contextTokenLimit = Math.Clamp(s.ContextTokenLimit > 0 ? s.ContextTokenLimit : 32000, 4000, 256000);
        _contextTokenLimitDisplay = ContextLimitDisplayOf(_contextTokenLimit);
        AiProviders.Clear();
        foreach (var v in s.AiProviders ?? new List<AiProvider>())
        {
            var row = new AiProviderRow
            {
                Id = string.IsNullOrWhiteSpace(v.Id) ? Guid.NewGuid().ToString("N")[..8] : v.Id,
                Name = v.Name ?? "",
                Url = v.Url ?? "",
                KeysText = string.Join("\n", v.Keys ?? new List<string>()),
                Enabled = v.Enabled,
                TimeoutSeconds = v.TimeoutSeconds is { } t and > 0 ? t.ToString() : ""
            };
            foreach (var m in v.Models ?? new List<AiProviderModel>())
                row.Models.Add(new AiModelRow { Model = m.Model ?? "", Label = m.Label ?? "", Enabled = m.Enabled });
            AttachAiProvider(row);
        }
        UpdateAiProviderStatus();

        _ttsEnabled = s.TtsEnabled;
        _ttsRate = s.TtsRate;
        _sttEnabled = s.SttEnabled;
        _ttsEngine = string.IsNullOrWhiteSpace(s.TtsEngine) ? "system" : s.TtsEngine;
        _sttEngine = string.IsNullOrWhiteSpace(s.SttEngine) ? "system" : s.SttEngine;
        _voiceApiUrl = string.IsNullOrWhiteSpace(s.VoiceApiUrl) ? "https://api.openai.com/v1" : s.VoiceApiUrl;
        _voiceApiKey = s.VoiceApiKey ?? "";
        _voiceTtsModel = string.IsNullOrWhiteSpace(s.VoiceTtsModel) ? "tts-1" : s.VoiceTtsModel;
        _voiceSttModel = string.IsNullOrWhiteSpace(s.VoiceSttModel) ? "whisper-1" : s.VoiceSttModel;
        _voiceName = string.IsNullOrWhiteSpace(s.VoiceName) ? "alloy" : s.VoiceName;
        _voiceApiMode = string.IsNullOrWhiteSpace(s.VoiceApiMode) ? "openai" : s.VoiceApiMode;
        _voiceExtra = s.VoiceExtra ?? "";
        // 本地 STT 模型：由显示名（"文件名（大小）"）映射 Picker 选中项
        _sttModelDisplay = SttModelDisplayOf(s.SttModelName ?? "");
        _voiceModeDisplay = _voiceApiMode switch
        {
            "sovits" => "GPT-SoVITS（本地/局域网）",
            _ => "OpenAI 兼容（网络 API）",
        };

        _notificationsEnabled = s.NotificationsEnabled;
        _greetingEnabled = s.GreetingEnabled;

        _weatherCity = s.WeatherCity;
        _cycleLength = s.CycleLength;
        _periodLength = s.PeriodLength;

        _alwaysOnTop = s.AlwaysOnTop;
        _petIdleMinutes = s.PetIdleMinutes;
        _petThinkModeDisplay = ThinkModeDisplayOf(s.PetThinkMode);
        _petThinkIntervalMinutes = Math.Max(1, s.PetThinkIntervalMinutes);

        // 屏幕方向（auto / landscape / portrait）
        _screenOrientation = string.IsNullOrWhiteSpace(s.ScreenOrientation) ? "auto" : s.ScreenOrientation;
        _screenOrientationDisplay = _screenOrientation switch
        {
            "landscape" => "横屏",
            "portrait" => "竖屏",
            _ => "自动（竖屏）",
        };

        // 小游戏难度
        _gameDifficulty = string.IsNullOrWhiteSpace(s.GameDifficulty) ? "normal" : s.GameDifficulty;
        _gameDifficultyDisplay = GameDifficultyDisplayOf(_gameDifficulty);
        _aiAutoDifficulty = s.AiAutoDifficulty;

        // 云端棋力
        _chessApiEnabled = s.ChessApiEnabled;
        _chessApiUrl = s.ChessApiUrl ?? "";
        _chessApiKey = s.ChessApiKey ?? "";
        _chessApiModel = string.IsNullOrWhiteSpace(s.ChessApiModel) ? "gpt-4o-mini" : s.ChessApiModel;

        _qqBotEnabled = s.QqBotEnabled;
        _qqAppId = s.QqAppId;
        _qqAppSecret = s.QqAppSecret;
        _qqBoundCharacterId = s.QqBoundCharacterId;
        _qqReplyGroup = s.QqReplyGroup;
        _qqUserOpenId = s.QqUserOpenId;
        _qqUserName = s.QqUserName;
        _qqProactiveEnabled = s.QqProactiveEnabled;
        _qqProactiveRequireConfirm = s.QqProactiveRequireConfirm;
        _qqProactivePerHour = s.QqProactivePerHour > 0 ? s.QqProactivePerHour : 3;
        _outboundChannelDisplay = (s.RealOutboundChannel ?? "auto").ToLowerInvariant() switch
        {
            "qq" => "只走 QQ",
            "wechat" => "只走微信",
            _ => "自动"
        };
        _wechatEnabled = s.WechatEnabled;
        _wechatAppId = s.WechatAppId;
        _wechatAppSecret = s.WechatAppSecret;
        _wechatToken = s.WechatToken;
        _wechatPort = s.WechatPort.ToString();
        _statusText = _bridge.Status;
        _betaChannel = s.BetaChannel;

        // MCP 设置
        _mcpEnabled = s.McpEnabled;
        _mcpAutoApprove = s.McpAutoApprove;
        _mcp.NetworkUrl = s.McpNetworkUrl;
        _mcp.AutoApprove = _mcpAutoApprove;
        _mcpNetworkUrl = s.McpNetworkUrl;
        _mcpGitHubRepo = s.McpGitHubRepo;
        _mcpImportZipPath = s.McpImportZipPath;
        _mcpImportFolderPath = s.McpImportFolderPath;
        // 这里不能同步 RefreshMcpServers()：McpOrchestrator.ListServers() 会递归
        // Directory.GetFiles(dir, "*", SearchOption.AllDirectories) 并对每个文件取
        // FileInfo.Length —— 全部跑在 UI 线程上。设置页是 transient，每次点「设置」
        // 都会重新构造 ViewModel，于是每次导航都同步卡一次（「点设置卡死」的真实阻塞点）。
        _ = LoadMcpServersAsync();
        _ = LoadQqBoundChoicesAsync();
        _suppressPresetApply = false;

        _bridge.StatusChanged += st =>
            MainThread.BeginInvokeOnMainThread(() => StatusText = st);
    }

    [ObservableProperty] private double _bgmLevel = 0.7;
    [ObservableProperty] private double _sfxLevel = 0.8;
    // 玻璃模式：磨砂/毛玻璃/液态本来是三个互相牵制的开关，用户要的是一句话选一种——
    // 合成一个下拉，顺手把「开关禁用了却点不动」那类困惑一起去掉。
    public List<string> GlassModeChoices { get; } = new() { "关闭", "磨砂玻璃", "毛玻璃", "液态玻璃" };

    /// <summary>玻璃模式（下拉显示值）：由三个开关推导，选择时反向设置。</summary>
    public string GlassModeDisplay
    {
        get => _liquidOn ? "液态玻璃" : _glassOn ? "毛玻璃" : _frostOn ? "磨砂玻璃" : "关闭";
        set
        {
            var want = value ?? "关闭";
            if (want == GlassModeDisplay) return;
            switch (want)
            {
                case "液态玻璃":
                    LiquidOn = true; GlassOn = true; FrostOn = true;
                    break;
                case "毛玻璃":
                    LiquidOn = false; GlassOn = true; FrostOn = true;
                    break;
                case "磨砂玻璃":
                    LiquidOn = false; GlassOn = false; FrostOn = true;
                    break;
                default:
                    LiquidOn = false; GlassOn = false; FrostOn = false;
                    break;
            }
            OnPropertyChanged(nameof(GlassModeDisplay));
        }
    }

    /// <summary>光源方向那四个滑杆属于微调，默认收起（用户反馈：一排近似设置太占地方）。</summary>
    [ObservableProperty] private bool _showLightAdvanced;

    [ObservableProperty] private bool _glassOn;
    [ObservableProperty] private bool _frostOn;
    [ObservableProperty] private bool _liquidOn;
    [ObservableProperty] private double _glassTranslucency = 1.0;
    [ObservableProperty] private bool _glassReducedTransparency;
    [ObservableProperty] private double _glassFrost = 0.5;
    [ObservableProperty] private double _glassHeight = 0.5;

    // 背景：玻璃需要有东西可透 —— 纯色窗口背景下永远看不出玻璃
    // 光源：X/Y 方向、Z 高度、光宽
    [ObservableProperty] private double _glassLightX = -0.7;
    [ObservableProperty] private double _glassLightY = -0.7;
    [ObservableProperty] private double _glassLightZ = 0.3;
    [ObservableProperty] private double _glassLightWidth = 0.4;

    [ObservableProperty] private string _backgroundMode = "none";
    [ObservableProperty] private string _backgroundModeDisplay = "纯色（默认）";
    [ObservableProperty] private string _backgroundImagePath = "";
    public List<string> BackgroundModeChoices { get; } = new() { "纯色（默认）", "自定义图片", "窗口透视（看穿到桌面）" };
    public bool BackgroundImageVisible => BackgroundMode == "image";
    public string BackgroundImageLabel => string.IsNullOrWhiteSpace(BackgroundImagePath)
        ? "尚未选择图片" : System.IO.Path.GetFileName(BackgroundImagePath);

    private static string BackgroundModeDisplayOf(string m) => m switch
    {
        "image" => "自定义图片",
        "clear" => "窗口透视（看穿到桌面）",
        _ => "纯色（默认）"
    };

    partial void OnBackgroundModeDisplayChanged(string value)
    {
        BackgroundMode = value switch
        {
            "自定义图片" => "image",
            "窗口透视（看穿到桌面）" => "clear",
            _ => "none"
        };
        OnPropertyChanged(nameof(BackgroundImageVisible));
        PersistSettings();
    }

    partial void OnBackgroundImagePathChanged(string value)
    {
        OnPropertyChanged(nameof(BackgroundImageLabel));
        PersistSettings();
    }

    /// <summary>选一张背景图；PNG 里的透明像素会穿透到窗口下层。</summary>
    [RelayCommand]
    private async Task PickBackgroundImage()
    {
        try
        {
            var pick = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "选择背景图片",
                FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    { DevicePlatform.WinUI, new[] { ".png", ".jpg", ".jpeg", ".webp", ".bmp" } },
                    { DevicePlatform.Android, new[] { "image/*" } },
                    { DevicePlatform.iOS, new[] { "public.image" } }
                })
            });
            if (pick is null) return;
            BackgroundImagePath = pick.FullPath;
            BackgroundModeDisplay = "自定义图片";
        }
        catch (Exception ex) { App.WriteLog("PickBackgroundImage -> " + ex.Message); }
    }
    [ObservableProperty] private string _themeName = "classic";
    [ObservableProperty] private string _themeDisplay = "经典";

    // 工具沙箱
    [ObservableProperty] private ObservableCollection<Modules.Sandbox.TrustEntry> _trustedTools = new();
    [ObservableProperty] private string _sandboxHint = "";

    public List<string> ThemeChoices { get; } = new() { "经典", "樱花粉", "翠竹绿", "晨雾蓝灰" };
    public List<string> ChatStyleChoices { get; } = new() { "剧本式", "气泡式" };

    private readonly Modules.Sandbox.SandboxPolicy _sandbox;

    /// <summary>刷新信任工具列表（设置页「工具安全」区）。</summary>
    public void RefreshSandboxTrust()
    {
        TrustedTools = new ObservableCollection<Modules.Sandbox.TrustEntry>(_sandbox.Trust.All());
        SandboxHint = TrustedTools.Count == 0
            ? "默认所有外部工具都不能读取模型数据（API Key 等）。需要时在此处加入信任。"
            : $"已信任 {TrustedTools.Count} 个工具。";
    }

    /// <summary>后台刷新信任工具列表（同步 IO 放线程池，完成后回主线程更新绑定，避免阻塞 UI）。
    /// 供 OnAppearing 等需要异步刷新的场景用；原同步版 RefreshSandboxTrust 保留给命令回调（已在后台上下文或可接受同步）。</summary>
    public void RefreshSandboxTrustAsync()
    {
        try
        {
            var items = _sandbox.Trust.All();
            MainThread.BeginInvokeOnMainThread(() =>
            {
                TrustedTools = new ObservableCollection<Modules.Sandbox.TrustEntry>(items);
                SandboxHint = TrustedTools.Count == 0
                    ? "默认所有外部工具都不能读取模型数据（API Key 等）。需要时在此处加入信任。"
                    : $"已信任 {TrustedTools.Count} 个工具。";
            });
        }
        catch (Exception ex)
        {
            App.WriteLog("RefreshSandboxTrustAsync: " + ex);
        }
    }

    /// <summary>把工具目录加入信任名单（按目录路径）。</summary>
    [RelayCommand]
    private async Task TrustToolDir()
    {
        var dir = await Microsoft.Maui.Controls.Shell.Current.DisplayActionSheet(
            "选择要信任的工具目录", "取消", null,
            Directory.Exists(Modules.Tools.ToolManager.ToolsDir)
                ? Directory.GetDirectories(Modules.Tools.ToolManager.ToolsDir).Select(System.IO.Path.GetFileName).ToArray()
                : new string[] { "（工具目录为空）" });
        if (dir is null || dir == "（工具目录为空）") return;
        var full = System.IO.Path.Combine(Modules.Tools.ToolManager.ToolsDir, dir);
        _sandbox.Trust.Add(dir, full, "设置页手动放开");
        _sandbox.RefreshKeyFingerprints();
        await Task.CompletedTask;
        RefreshSandboxTrust();
    }

    [RelayCommand]
    private void RemoveTrustedTool(Modules.Sandbox.TrustEntry tool)
    {
        _sandbox.Trust.Remove(tool.ToolName, tool.ToolPath);
        RefreshSandboxTrust();
    }

    // ============ 工具完整性检查（#2） ============

    /// <summary>最近一次完整性检查的结果文本（设置页「检查工具完整性」按钮写入）。</summary>
    [ObservableProperty] private string _toolIntegrityText = "";
    /// <summary>最近一次检查是否健康（决定结果文字颜色）。</summary>
    [ObservableProperty] private bool _toolIntegrityHealthy = true;

    /// <summary>
    /// 运行一次工具完整性检查：核对期望工具是否齐全、描述/schema 是否完整、聊天工具集能否构建。
    /// 结果写入 ToolIntegrityText + 日志，UI 一眼可读。
    /// </summary>
    [RelayCommand]
    private void VerifyTools()
    {
        try
        {
            var report = _tools.VerifyIntegrity();
            ToolIntegrityHealthy = report.Healthy;
            ToolIntegrityText = report.Summary();
            App.WriteLog("ToolIntegrity: " + report.Summary());
            foreach (var i in report.Issues.Where(i => i.Severity == "error"))
                App.WriteLog($"ToolIntegrity[error] {i.Tool}: {i.Detail}");
        }
        catch (Exception ex)
        {
            ToolIntegrityHealthy = false;
            ToolIntegrityText = "工具完整性检查失败：" + ex.Message;
            App.WriteLog("VerifyTools -> " + ex);
        }
    }

    // ============ #12 表情包库 ============

    /// <summary>表情包列表（设置页显示用）。</summary>
    public ObservableCollection<StickerRow> Stickers { get; } = new();

    /// <summary>表情包库状态文案（空库提示 / 数量）。</summary>
    [ObservableProperty] private string _stickerStatus = "";

    /// <summary>从库里刷新表情列表。</summary>
    [RelayCommand]
    private async Task RefreshStickers()
    {
        try
        {
            await _stickers.EnsureLoadedAsync();
            Stickers.Clear();
            foreach (var s in _stickers.Items)
                Stickers.Add(new StickerRow { Id = s.Id, Name = s.Name, Description = s.Description, Path = s.Path });
            StickerStatus = Stickers.Count == 0 ? "还没有表情包。点「导入表情」添加图片。" : $"共 {Stickers.Count} 个表情";
        }
        catch (Exception ex) { App.WriteLog("Settings.RefreshStickers -> " + ex.Message); }
    }

    /// <summary>导入一个或多个表情图片。</summary>
    [RelayCommand]
    private async Task ImportSticker()
    {
        try
        {
            var picks = await FilePicker.Default.PickMultipleAsync(new PickOptions
            {
                PickerTitle = "导入表情包图片",
                FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    { DevicePlatform.WinUI, new[] { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp" } },
                    { DevicePlatform.Android, new[] { "image/*" } },
                    { DevicePlatform.iOS, new[] { "public.image" } }
                })
            });
            if (picks is null) return;
            var added = 0;
            foreach (var p in picks)
            {
                var item = await _stickers.ImportAsync(p.FullPath);
                if (item is not null) added++;
            }
            await RefreshStickers();
            if (added > 0)
                await Shell.Current.DisplayAlert("表情包", $"导入了 {added} 个表情。给它们写句描述，AI 用起来会更准。", "好");
        }
        catch (Exception ex) { App.WriteLog("Settings.ImportSticker -> " + ex.Message); }
    }

    /// <summary>编辑某个表情的描述（用户手填，AI 会按描述选表情）。</summary>
    [RelayCommand]
    private async Task EditStickerDesc(StickerRow? row)
    {
        if (row is null) return;
        var text = await Shell.Current.DisplayPromptAsync("表情描述",
            $"给「{row.Name}」写一句描述（AI 会按描述选表情）：", "保存", "取消", row.Description, maxLength: 120);
        if (text is null) return;
        await _stickers.UpdateDescriptionAsync(row.Id, text);
        await RefreshStickers();
    }

    /// <summary>#12/#13：让识图模型看一眼这个表情，自动写描述（一次性识别后注入给 AI）。</summary>
    [RelayCommand]
    private async Task AutoDescribeSticker(StickerRow? row)
    {
        if (row is null) return;
        if (!_vision.IsConfigured)
        {
            await Shell.Current.DisplayAlert("识图", "还没有配置识图模型。到「API 设置 → 识图模型」填好端点 / Key / 模型后再试。", "好");
            return;
        }
        var desc = await _vision.DescribeImageAsync(row.Path,
            "这是一张聊天用的表情包。请用一句中文描述它的内容、情绪和适合的使用场景（30 字以内，直接给描述，不要客套）。");
        if (string.IsNullOrEmpty(desc))
        {
            await Shell.Current.DisplayAlert("识图", "识图失败。检查识图模型配置或网络后再试。", "好");
            return;
        }
        await _stickers.UpdateDescriptionAsync(row.Id, desc);
        await RefreshStickers();
    }

    /// <summary>删除一个表情。</summary>
    [RelayCommand]
    private async Task RemoveSticker(StickerRow? row)
    {
        if (row is null) return;
        var yes = await Shell.Current.DisplayAlert("删除表情", $"删除「{row.Name}」？", "删除", "取消");
        if (!yes) return;
        await _stickers.RemoveAsync(row.Id);
        await RefreshStickers();
    }

    // ============ 多端点轮换（厂商 × 模型） ============

    /// <summary>接入的厂商（一家 = 端点 + 密钥 + 多款模型）。</summary>
    public ObservableCollection<AiProviderRow> AiProviders { get; } = new();

    /// <summary>轮换总开关：开启后自动在多家厂商的多款模型间轮换，保证对话不间断。</summary>
    [ObservableProperty] private bool _aiRotationEnabled = true;

    /// <summary>多端点区状态文案。</summary>
    [ObservableProperty] private string _aiProviderStatus = "";

    /// <summary>上下文 Token 上限（估算值）：超过时强制把旧对话总结成第一人称记忆。</summary>
    [ObservableProperty] private int _contextTokenLimit = 32000;

    /// <summary>上限的 Picker 显示项（4K…256K）。</summary>
    [ObservableProperty] private string _contextTokenLimitDisplay = "32K";

    /// <summary>Token 上限可选项。</summary>
    public List<string> ContextLimitChoices { get; } = new() { "4K", "8K", "16K", "32K", "64K", "128K", "256K" };

    // 新增厂商输入框
    [ObservableProperty] private string _newProviderName = "";
    [ObservableProperty] private string _newProviderUrl = "";
    [ObservableProperty] private string _newProviderKeys = "";
    [ObservableProperty] private string _newProviderModel = "";
    [ObservableProperty] private string _newProviderTimeout = "";

    private void AttachAiProvider(AiProviderRow row)
    {
        row.PropertyChanged += (_, __) => PersistSettings();
        AiProviders.Add(row);
    }

    /// <summary>新增一家厂商（可顺手填第一款模型；之后还能继续加）。</summary>
    [RelayCommand]
    private void AddProvider()
    {
        if (string.IsNullOrWhiteSpace(NewProviderUrl))
        {
            AiProviderStatus = "先填端点地址（OpenAI 兼容），再点「＋添加厂商」。";
            return;
        }
        var row = new AiProviderRow
        {
            Name = (NewProviderName ?? "").Trim(),
            Url = (NewProviderUrl ?? "").Trim(),
            KeysText = (NewProviderKeys ?? "").Trim(),
            TimeoutSeconds = (NewProviderTimeout ?? "").Trim()
        };
        if (!string.IsNullOrWhiteSpace(NewProviderModel))
            row.Models.Add(new AiModelRow { Model = NewProviderModel.Trim() });
        AttachAiProvider(row);

        NewProviderName = "";
        NewProviderUrl = "";
        NewProviderKeys = "";
        NewProviderModel = "";
        NewProviderTimeout = "";
        UpdateAiProviderStatus();
        PersistSettings();
    }

    /// <summary>用当前主配置预填厂商信息（同一家厂商只需填一次端点与密钥）。</summary>
    [RelayCommand]
    private void FillProviderFromCurrent()
    {
        if (string.IsNullOrWhiteSpace(NewProviderUrl) && !string.IsNullOrWhiteSpace(AiUrl)) NewProviderUrl = AiUrl.Trim();
        if (string.IsNullOrWhiteSpace(NewProviderKeys) && !string.IsNullOrWhiteSpace(AiKey)) NewProviderKeys = AiKey.Trim();
        if (string.IsNullOrWhiteSpace(NewProviderModel) && !string.IsNullOrWhiteSpace(AiModel)) NewProviderModel = AiModel.Trim();
        AiProviderStatus = "已用当前配置预填，点「＋添加厂商」。";
    }

    /// <summary>给某家厂商加一款模型（同一厂商多款模型不必重复填端点密钥）。</summary>
    [RelayCommand]
    private async Task AddModelToProvider(AiProviderRow? provider)
    {
        if (provider is null) return;
        var model = await Shell.Current.DisplayPromptAsync($"给「{provider.Title}」加模型",
            "模型名（如 deepseek-chat / gpt-4o-mini）。同一家厂商可以挂多款。", maxLength: 60);
        if (string.IsNullOrWhiteSpace(model)) return;
        var label = await Shell.Current.DisplayPromptAsync("显示名（可留空）",
            "想给这款模型起个好认的名字就填；留空则直接用模型名。", maxLength: 20);
        provider.Models.Add(new AiModelRow { Model = model.Trim(), Label = (label ?? "").Trim() });
        provider.RefreshSummary();
        PersistSettings();
    }

    /// <summary>改厂商名与端点（同一家厂商的地址只填一次）。</summary>
    [RelayCommand]
    private async Task EditProviderBasics(AiProviderRow? provider)
    {
        if (provider is null) return;
        var name = await Shell.Current.DisplayPromptAsync("厂商名称",
            "好认的名字，如「硅基流动」「DeepSeek 官方」。", initialValue: provider.Name, maxLength: 30);
        if (name is null) return;
        var url = await Shell.Current.DisplayPromptAsync("端点地址",
            "OpenAI 兼容的 chat/completions 地址。", initialValue: provider.Url, maxLength: 300);
        if (url is null) return;
        provider.Name = name.Trim();
        provider.Url = url.Trim();
        PersistSettings();
    }

    /// <summary>点一下模型：启用 / 停用（停用的模型不参与轮换）。</summary>
    [RelayCommand]
    private void ToggleModel(AiModelRow? model)
    {
        if (model is null) return;
        model.Enabled = !model.Enabled;
        var owner = AiProviders.FirstOrDefault(v => v.Models.Contains(model));
        owner?.RefreshSummary();
        PersistSettings();
    }

    /// <summary>改某家厂商的密钥（一行一枚）。</summary>
    [RelayCommand]
    private async Task EditProviderKeys(AiProviderRow? provider)
    {
        if (provider is null) return;
        var keys = await Shell.Current.DisplayPromptAsync($"「{provider.Title}」的密钥",
            "一行一枚，可多枚轮流用（也可用逗号分隔）。", initialValue: provider.KeysText, maxLength: 4000);
        if (keys is null) return;
        provider.KeysText = keys;
        PersistSettings();
    }

    /// <summary>改某家厂商的超时（秒，留空 = 跟随全局）。</summary>
    [RelayCommand]
    private async Task EditProviderTimeout(AiProviderRow? provider)
    {
        if (provider is null) return;
        var t = await Shell.Current.DisplayPromptAsync($"「{provider.Title}」的超时",
            "秒数（3~600），留空 = 用设置里的全局超时。", initialValue: provider.TimeoutSeconds, maxLength: 4);
        if (t is null) return;
        provider.TimeoutSeconds = t.Trim();
        PersistSettings();
    }

    [RelayCommand]
    private void RemoveModelFromProvider(AiModelRow? model)
    {
        if (model is null) return;
        var owner = AiProviders.FirstOrDefault(v => v.Models.Contains(model));
        if (owner is null) return;
        owner.Models.Remove(model);
        owner.RefreshSummary();
        PersistSettings();
    }

    /// <summary>删除一家厂商。</summary>
    [RelayCommand]
    private void RemoveProvider(AiProviderRow? row)
    {
        if (row is null) return;
        AiProviders.Remove(row);
        UpdateAiProviderStatus();
        PersistSettings();
    }

    /// <summary>厂商顺序即优先顺序：靠前的先用，失灵自动换下一家/下一款。</summary>
    [RelayCommand]
    private void MoveProviderUp(AiProviderRow? row)
    {
        if (row is null) return;
        var idx = AiProviders.IndexOf(row);
        if (idx <= 0) return;
        AiProviders.Move(idx, idx - 1);
        PersistSettings();
    }

    [RelayCommand]
    private void MoveProviderDown(AiProviderRow? row)
    {
        if (row is null) return;
        var idx = AiProviders.IndexOf(row);
        if (idx < 0 || idx >= AiProviders.Count - 1) return;
        AiProviders.Move(idx, idx + 1);
        PersistSettings();
    }

    /// <summary>刷新多端点区的状态文案。</summary>
    private void UpdateAiProviderStatus()
    {
        AiProviderStatus = AiProviders.Count == 0
            ? "还没有接入厂商。填端点与密钥后点「＋添加厂商」，再往里加模型。"
            : $"已接入 {AiProviders.Count} 家厂商，共 {AiProviders.Sum(v => v.Models.Count)} 款模型。"
              + "按列表顺序使用：靠前的先用，失灵自动换下一家/下一款（可用 ↑↓ 调整）。";
    }

    partial void OnAiRotationEnabledChanged(bool value) => PersistSettings();

    partial void OnContextTokenLimitDisplayChanged(string value)
    {
        _contextTokenLimit = ParseContextLimit(value);
        PersistSettings();
    }

    /// <summary>"32K"之类的显示名 → 数字。</summary>
    private static int ParseContextLimit(string? display)
    {
        var t = (display ?? "").Trim().TrimEnd('K', 'k');
        if (int.TryParse(t, out var v) && v >= 1) return v * 1000;
        return 32000;
    }

    /// <summary>数字 → 最接近的显示名。</summary>
    private static string ContextLimitDisplayOf(int limit)
    {
        var k = Math.Max(1, limit / 1000);
        int[] candidates = { 4, 8, 16, 32, 64, 128, 256 };
        var best = candidates[0];
        foreach (var c in candidates)
            if (Math.Abs(c - k) < Math.Abs(best - k)) best = c;
        return $"{best}K";
    }

    // ============ #16 QQ 机器人绑定角色（选存档） ============

    /// <summary>QQ 机器人绑定的陪聊角色 Id（空=用当前角色，即旧行为）。</summary>
    private string _qqBoundCharacterId = "";

    /// <summary>QQ 机器人绑定角色的显示名（Picker 的选中项）。</summary>
    [ObservableProperty] private string _qqBoundCharacter = "";

    /// <summary>可选角色名列表（首项为「用当前角色」）。</summary>
    public ObservableCollection<string> QqBoundChoices { get; } = new();

    /// <summary>把角色库里的角色名填进绑定选择器，并把已绑定的 Id 翻译回显示名。</summary>
    private async Task LoadQqBoundChoicesAsync()
    {
        try
        {
            var list = await _library.ListAsync();
            QqBoundChoices.Clear();
            QqBoundChoices.Add("（用当前角色）");
            foreach (var c in list)
            {
                if (!string.IsNullOrEmpty(c.Profile.Name) && !QqBoundChoices.Contains(c.Profile.Name))
                    QqBoundChoices.Add(c.Profile.Name);
            }
            if (!string.IsNullOrEmpty(_qqBoundCharacterId))
            {
                var bound = list.FirstOrDefault(c => c.Profile.Id == _qqBoundCharacterId);
                QqBoundCharacter = bound is null ? "（用当前角色）" : bound.Profile.Name;
            }
            else
            {
                QqBoundCharacter = "（用当前角色）";
            }
        }
        catch (Exception ex) { App.WriteLog("Settings.LoadQqBoundChoices -> " + ex.Message); }
    }

    partial void OnQqBoundCharacterChanged(string value) => _ = ResolveQqBoundAsync(value);

    /// <summary>显示名 → 角色 Id（用于持久化）。</summary>
    private async Task ResolveQqBoundAsync(string name)
    {
        try
        {
            if (string.IsNullOrEmpty(name) || name == "（用当前角色）")
            {
                _qqBoundCharacterId = "";
                PersistSettings();
                return;
            }
            var list = await _library.ListAsync();
            var ch = list.FirstOrDefault(c => c.Profile.Name == name);
            _qqBoundCharacterId = ch?.Profile.Id ?? "";
            PersistSettings();
        }
        catch (Exception ex) { App.WriteLog("Settings.ResolveQqBound -> " + ex.Message); }
    }

    // ============ #13 API 预设 & 识图模型 ============

    /// <summary>预设显示名（Picker 绑定）。</summary>
    [ObservableProperty] private string _aiPreset = "自定义";
    /// <summary>识图模型端点（独立于主对话模型）。</summary>
    [ObservableProperty] private string _visionUrl = "";
    [ObservableProperty] private string _visionKey = "";
    [ObservableProperty] private string _visionModel = "";

    /// <summary>预设列表（Picker 数据源）。</summary>
    public ObservableCollection<string> AiPresetChoices { get; } = new();

    /// <summary>构造初始化期间不触发预设覆盖（防止恢复设置时把用户 URL 冲掉）。</summary>
    private bool _suppressPresetApply = true;

    /// <summary>常见 API 服务的端点预设：显示名 / 键 / URL。</summary>
    private static readonly (string Display, string Key, string Url)[] AiPresets =
    {
        ("通用（OpenAI兼容）", "openai", "https://api.openai.com/v1/chat/completions"),
        ("DeepSeek", "deepseek", "https://api.deepseek.com/v1/chat/completions"),
        ("MiMo（小米）", "mimo", "https://api.xiaomimimo.com/v1/chat/completions"),
        ("Kimi（月之暗面）", "kimi", "https://api.moonshot.cn/v1/chat/completions"),
        ("豆包（火山方舟）", "doubao", "https://ark.cn-beijing.volces.com/api/v3/chat/completions"),
        ("智谱 AI", "zhipu", "https://open.bigmodel.cn/api/paas/v4/chat/completions"),
        ("硅基流动", "siliconflow", "https://api.siliconflow.cn/v1/chat/completions"),
        ("通义千问（DashScope）", "qwen", "https://dashscope.aliyuncs.com/compatible-mode/v1/chat/completions"),
        ("自定义", "custom", ""),
    };

    private static string PresetDisplayOf(string key)
    {
        foreach (var p in AiPresets) if (p.Key == key) return p.Display;
        return "自定义";
    }

    private static string PresetKeyOf(string display)
    {
        foreach (var p in AiPresets) if (p.Display == display) return p.Key;
        return "custom";
    }

    private static string PresetUrlOf(string display)
    {
        foreach (var p in AiPresets) if (p.Display == display) return p.Url;
        return "";
    }

    /// <summary>切换预设：把端点填成该服务的默认值（模型名由用户填或点「拉取模型」）。</summary>
    partial void OnAiPresetChanged(string value)
    {
        if (_suppressPresetApply) return;
        var url = PresetUrlOf(value);
        if (!string.IsNullOrWhiteSpace(url)) AiUrl = url;
        PersistSettings();
    }

    partial void OnVisionUrlChanged(string value) => PersistSettings();
    partial void OnVisionKeyChanged(string value) => PersistSettings();
    partial void OnVisionModelChanged(string value) => PersistSettings();

    // 自动更新
    [ObservableProperty] private bool _betaChannel;
    [ObservableProperty] private bool _isCheckingUpdate;
    [ObservableProperty] private string _updateStatus = "";
    [ObservableProperty] private string _latestUpdateVersion = "";

    private readonly Modules.Update.UpdateService _updater = new();

    [RelayCommand]
    private async Task CheckUpdate()
    {
        IsCheckingUpdate = true;
        _latestUpdateVersion = "";
        UpdateStatus = "正在检查更新（多镜像测速中）…";
        try
        {
            _updater.BetaChannel = BetaChannel;
            var r = await _updater.CheckAsync();
            if (r.Error is not null)
            {
                UpdateStatus = "检查失败：" + r.Error;
                return;
            }
            if (r.Latest is not null && r.HasUpdate)
            {
                _latestUpdateVersion = r.Latest.Tag;
                // 测速选最快镜像
                var speeds = await _updater.SpeedTestAsync(r.Latest.ZipUrl);
                var fastest = speeds.Count > 0 ? speeds[0] : null;
                UpdateStatus = $"发现新版本 {r.Latest.Tag}（{r.Latest.DownloadNote}）"
                    + (fastest is not null ? $" · 最快源 {fastest.Mirror.TrimEnd('/')}（{fastest.MibPerSec:0.#} MiB/s）" : "");
            }
            else
            {
                UpdateStatus = r.Latest is null ? "已是最新版本" : $"当前为最新版本 {r.Latest.Tag}";
            }
        }
        catch (Exception ex)
        {
            App.WriteLog("Settings.CheckUpdate -> " + ex.Message);
            UpdateStatus = "检查失败：" + ex.Message;
        }
        finally
        {
            IsCheckingUpdate = false;
        }
    }

    [RelayCommand]
    private async Task DownloadUpdate()
    {
        if (string.IsNullOrEmpty(_latestUpdateVersion))
        {
            UpdateStatus = "先点击「检查更新」发现新版本。";
            return;
        }
        IsCheckingUpdate = true;
        UpdateStatus = "正在下载更新包…";
        try
        {
            _updater.BetaChannel = BetaChannel;
            var r = await _updater.CheckAsync();
            if (r.Latest is null) { UpdateStatus = "未发现可下载版本。"; return; }
            var speeds = await _updater.SpeedTestAsync(r.Latest.ZipUrl);
            var url = speeds.Count > 0
                ? speeds[0].Mirror + "gongchanwangzilin/WarmAsBefore/releases/download/" + r.Latest.Tag + "/" + System.IO.Path.GetFileName(r.Latest.ZipUrl)
                : r.Latest.ZipUrl;
            var sha = await _updater.FetchSha256Async(r.Latest.Sha256);
            var dir = System.IO.Path.Combine(Microsoft.Maui.Storage.FileSystem.AppDataDirectory, "WarmAsBefore", "updates");
            var zip = await _updater.DownloadAsync(url, dir, sha);
            UpdateStatus = $"已下载到 {zip}（SHA256 {(string.IsNullOrEmpty(sha) ? "未校验" : "已校验")}），重启后替换。";
        }
        catch (Exception ex)
        {
            App.WriteLog("Settings.DownloadUpdate -> " + ex.Message);
            UpdateStatus = "下载失败：" + ex.Message;
        }
        finally
        {
            IsCheckingUpdate = false;
        }
    }

    /// <summary>毛玻璃是磨砂的高级版：必须开启磨砂后才能开启毛玻璃。</summary>
    public bool CanGlass => FrostOn;

    /// <summary>透明度 > 0 才算真的开了玻璃；为 0 时表面不透明，等同于关闭。</summary>
    public bool GlassActive => GlassTranslucency > 0.01;
    public bool GlassInactive => !GlassActive;
    [ObservableProperty] private bool _complexPlot;
    [ObservableProperty] private bool _novelTesting;
    [ObservableProperty] private bool _showAffection;
    [ObservableProperty] private bool _affectionLevelUp = true;
    // 战斗系统开关：关闭后 AI 不能开战、battle_* 工具被剔除
    [ObservableProperty] private bool _battleEnabled = true;
    [ObservableProperty] private bool _battleAiCanStart = true;
    // 天气事件（打雷/下雨）：开关 + 概率 + 音效/帧图
    [ObservableProperty] private bool _lightningEnabled = true;
    /// <summary>打雷概率（0-1）：用字符串承接 Entry.Text，落盘时再解析。</summary>
    [ObservableProperty] private string _lightningProbability = "0.06";
    [ObservableProperty] private bool _rainEventEnabled = true;
    /// <summary>下雨概率（0-1）：同上，字符串承接。</summary>
    [ObservableProperty] private string _rainProbability = "0.15";
    [ObservableProperty] private string _lightningSfxPath = "";
    [ObservableProperty] private string _rainSfxPath = "";
    /// <summary>打雷帧图路径（每行一个；空则用默认白闪）。</summary>
    [ObservableProperty] private string _lightningFramesText = "";
    /// <summary>下雨帧图路径（每行一个；空则不显示雨视觉）。</summary>
    [ObservableProperty] private string _rainFramesText = "";
    /// <summary>特效帧切换间隔（毫秒）：字符串承接 Entry.Text。</summary>
    [ObservableProperty] private string _weatherEffectFrameMs = "120";
    // 行进扩展（#9）：AI 可单独出行 + 用户可选跟上
    /// <summary>AI 是否可单独出行（不带用户）：开启后【移动:】只移动 AI 自己，用户视角停原地。</summary>
    [ObservableProperty] private bool _aiTravelAlone;
    /// <summary>AI 单独出行时用户是否可「跟上」（主界面出现跟上按钮）。</summary>
    [ObservableProperty] private bool _allowFollowAi = true;
    [ObservableProperty] private bool _menuRight;
    [ObservableProperty] private string _chatStyle = "galgame";
    [ObservableProperty] private string _chatStyleDisplay = "剧本式";
    [ObservableProperty] private bool _allowSilence;
    [ObservableProperty] private string _lang = "zh-CN";
    [ObservableProperty] private string _langDisplay = "简体中文";
    [ObservableProperty] private double _textSpeed = 1.0;
    [ObservableProperty] private bool _autoSave = true;
    [ObservableProperty] private string _keySfx = "default";

    [ObservableProperty] private string _aiUrl = "https://api.openai.com/v1/chat/completions";
    [ObservableProperty] private string _aiKey = "";
    [ObservableProperty] private string _aiModel = "gpt-4o";
    [ObservableProperty] private double _aiTemperature = 0.8;
    [ObservableProperty] private double _aiMaxTokens = 500;
    [ObservableProperty] private bool _deepThink;
    [ObservableProperty] private string _deepModel = "";
    [ObservableProperty] private int _memoryTurns = 5;

    /// <summary>人类记忆调取窗口（天）：默认 3 天，0 = 不限。可按角色在角色库里覆写。</summary>
    [ObservableProperty] private double _memoryWindowDays = HumanMemoryMath.DefaultWindowDays;

    /// <summary>回忆随机扰动 0~1：越大越像人一样跳着想起、偶尔漏掉。</summary>
    [ObservableProperty] private double _memoryRecallRandomness = HumanMemoryMath.DefaultRandomness;

    /// <summary>是否让模型参与副设定改写（默认关闭：低频但要花钱）。</summary>
    [ObservableProperty] private bool _subProfileAiRewrite;
    partial void OnSubProfileAiRewriteChanged(bool value) => PersistSettings();

    // ============ 设置首页：分类条目 ============

    /// <summary>首页的一个分类条目（名称 + 一句话说明 + 当前值摘要）。</summary>
    public sealed partial class SettingsSectionRow : ObservableObject
    {
        public string Key { get; init; } = "";
        public string Icon { get; init; } = "";
        public string Title { get; init; } = "";
        public string Desc { get; init; } = "";
        public string[] Keywords { get; init; } = Array.Empty<string>();
        [ObservableProperty] private string _summary = "";
        [ObservableProperty] private bool _visible = true;
    }

    public ObservableCollection<SettingsSectionRow> SectionRows { get; } = new();

    /// <summary>按当前设置值刷新每个分类的「当前值」摘要（首页一眼能看到关键状态）。</summary>
    private void RefreshSectionSummaries()
    {
        foreach (var row in SectionRows)
        {
            row.Summary = row.Key switch
            {
                "appearance" => $"{(_liquidOn ? "液态玻璃" : _glassOn ? "毛玻璃" : _frostOn ? "磨砂" : "纯色")} · {ThemeDisplayName()}",
                "sound" => $"BGM {BgmLevel:P0} · 音效 {SfxLevel:P0}",
                "ai" => $"{AiProviders.Count(v => v.Enabled)} 家厂商 · {(SmartSavingEnabled ? "省钱开" : "省钱关")} · 超时 {AiTimeoutSeconds}s",
                "memory" => $"回想 {WindowText(MemoryWindowDays)} · 上下文 {_contextTokenLimitDisplay}",
                "voice" => $"{(TtsEnabled ? "朗读开" : "朗读关")} · {(SttEnabled ? "识别开" : "识别关")} · {TtsEngine}",
                "interface" => $"{ScreenOrientationDisplay} · {ChatStyleDisplay} · 难度 {GameDifficultyDisplayOf(_gameDifficulty)}",
                "platform" => $"桌宠闲置 {(PetIdleMinutes == 0 ? "关" : PetIdleMinutes + " 分")} · {(PreferDiscreteGpu ? "独显" : "自动显卡")}",
                "integrations" => $"{(QqBotEnabled ? "QQ 开" : "QQ 关")} · {(WechatEnabled ? "微信开" : "微信关")} · MCP {(McpEnabled ? "开" : "关")}",
                "life" => $"{(NotificationsEnabled ? "通知开" : "通知关")} · {(string.IsNullOrWhiteSpace(WeatherCity) ? "未设城市" : WeatherCity)}",
                "data" => $"{MaterialsSummary()} · 能力包 {(string.IsNullOrWhiteSpace(SkillsSummary) ? "未装" : "已装")}",
                _ => row.Desc
            };
        }
    }

    private string ThemeDisplayName() => (_themeName ?? "classic") switch
    {
        "sakura" => "樱花粉",
        "bamboo" => "翠竹绿",
        "mist" => "晨雾蓝灰",
        _ => "经典"
    };

    private static string WindowText(double days) => days <= 0 ? "不限" : $"{days:0.#} 天";

    private string MaterialsSummary()
    {
        try { return "素材库"; }
        catch { return "素材库"; }
    }

    /// <summary>建分类条目（首次进设置页时一次）。</summary>
    private void BuildSectionRows()
    {
        if (SectionRows.Count > 0) { RefreshSectionSummaries(); return; }
        foreach (var sec in SettingsCatalog.All)
        {
            SectionRows.Add(new SettingsSectionRow
            {
                Key = sec.Key, Icon = sec.Icon, Title = sec.Title, Desc = sec.Desc, Keywords = sec.Keywords
            });
        }
        RefreshSectionSummaries();
    }

    /// <summary>按搜索词过滤分类条目（同时把卡片级过滤也应用上，子页里才不会显示无关卡片）。</summary>
    private void ApplySearch(string? query)
    {
        Filter.Apply(query);
        var q = (query ?? "").Trim().ToLowerInvariant();
        foreach (var row in SectionRows)
        {
            row.Visible = q.Length == 0
                || row.Keywords.Any(k => k.Contains(q, StringComparison.OrdinalIgnoreCase) || q.Contains(k, StringComparison.OrdinalIgnoreCase))
                || row.Title.Contains(q, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>首页出现时准备：建分类条目并刷新摘要。</summary>
    public void PrepareHome()
    {
        BuildSectionRows();
        RefreshSectionSummaries();
        ApplySearch(SettingsSearchText);
    }

    /// <summary>进入某个分类子页。</summary>
    [RelayCommand]
    private async Task OpenSection(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return;
        await Shell.Current.GoToAsync($"settingssection?key={Uri.EscapeDataString(key)}");
    }

    // ============ 设置搜索 ============

    /// <summary>卡片级过滤：搜索时只留下命中的卡片（设置页太长，找不到项是常态）。</summary>
    public SettingsCardFilter Filter { get; } = new();

    [ObservableProperty] private string _settingsSearchText = "";

    private System.Threading.Timer? _searchTimer;

    /// <summary>
    /// 搜索防抖 250ms：每敲一个字就切 24 张卡片的可见性会让长页面反复重排，
    /// 打字时就会一卡一卡；等手停一下再过滤，观感顺得多。
    /// </summary>
    partial void OnSettingsSearchTextChanged(string value)
    {
        lock (_persistLock)
        {
            _searchTimer ??= new System.Threading.Timer(
                _ => MainThread.BeginInvokeOnMainThread(() => ApplySearch(SettingsSearchText)), null,
                Timeout.Infinite, Timeout.Infinite);
            _searchTimer.Change(250, Timeout.Infinite);
        }
    }

    /// <summary>清空搜索，恢复全部分类。</summary>
    [RelayCommand]
    private void ClearSettingsSearch()
    {
        SettingsSearchText = "";
        ApplySearch("");   // 清空立即生效，不用等防抖
    }

    /// <summary>优先使用独显（Windows 图形设置里的「高性能」），改完需重启程序生效。</summary>
    [ObservableProperty] private bool _preferDiscreteGpu;
    [ObservableProperty] private string _gpuStatusText = "";

    partial void OnPreferDiscreteGpuChanged(bool value)
    {
        var (ok, msg) = Services.GpuPreferenceService.SetHighPerformance(value);
        GpuStatusText = msg;
        if (ok) PersistSettings();
        _ = ok;
    }

    /// <summary>轻量模式：关闭卡片阴影（低配机器流畅优先）。</summary>
    [ObservableProperty] private bool _lightMode;
    partial void OnLightModeChanged(bool value) => PersistSettings();

    /// <summary>智能省钱总开关（默认开）。</summary>
    [ObservableProperty] private bool _smartSavingEnabled = true;
    partial void OnSmartSavingEnabledChanged(bool value) => PersistSettings();

    /// <summary>是否让模型辅助建立词义关系网（默认关闭：默认只靠共现统计）。</summary>
    [ObservableProperty] private bool _semanticAiAssist;
    partial void OnSemanticAiAssistChanged(bool value) => PersistSettings();

    /// <summary>API 可用的模型列表（由 FetchModelsAsync 填充）。</summary>
    public ObservableCollection<string> AiModelChoices { get; } = new();
    [ObservableProperty] private bool _isLoadingModels;
    [ObservableProperty] private string _modelListStatus = "";

    [ObservableProperty] private bool _ttsEnabled = true;
    [ObservableProperty] private double _ttsRate = 1.0;
    [ObservableProperty] private bool _sttEnabled = true;
    [ObservableProperty] private string _ttsEngine = "system";
    [ObservableProperty] private string _sttEngine = "system";
    [ObservableProperty] private string _voiceApiUrl = "https://api.openai.com/v1";
    [ObservableProperty] private string _voiceApiKey = "";
    [ObservableProperty] private string _voiceTtsModel = "tts-1";
    [ObservableProperty] private string _voiceSttModel = "whisper-1";
    [ObservableProperty] private string _voiceName = "alloy";
    /// <summary>外部语音服务类型：openai / sovits。</summary>
    [ObservableProperty] private string _voiceApiMode = "openai";
    [ObservableProperty] private string _voiceModeDisplay = "OpenAI 兼容（网络 API）";
    /// <summary>引擎特有扩展参数（SoVITS：说话人编号 spk_id）。</summary>
    [ObservableProperty] private string _voiceExtra = "";
    [ObservableProperty] private string _testVoiceStatus = "";
    /// <summary>试听用文本（设置页"试听当前音色"输入框）。</summary>
    [ObservableProperty] private string _previewVoiceText = "你好，我是温暖如初。";
    /// <summary>本地 STT 模型显示名（Picker 选中项，"名称（大小）"格式）。</summary>
    [ObservableProperty] private string _sttModelDisplay = "";
    [ObservableProperty] private int _sttModelProgress;
    [ObservableProperty] private string _sttModelStatus = "未选择本地模型";
    [ObservableProperty] private bool _isDownloadingSttModel;

    public List<string> VoiceEngineChoices { get; } = new() { "system", "api", "local" };
    /// <summary>本地识别模型选项（Picker 用，显示名 = "文件名（大小）"）。</summary>
    public List<string> SttModelChoices { get; } = SpeechService.SttLocalModels
        .Select(m => $"{m.Name}（{m.SizeLabel}）").ToList();
    /// <summary>外部语音服务类型选项（Picker 用，绑定显示名）。</summary>
    public List<string> VoiceModeChoices { get; } = new()
    {
        "OpenAI 兼容（网络 API）",
        "GPT-SoVITS（本地/局域网）",
    };

    /// <summary>从 Picker 显示名（"文件名（大小）"）解析出模型文件名。</summary>
    private static string SttModelNameOf(string display) =>
        display is null ? "" : display.Split('（')[0].Trim();

    /// <summary>由模型文件名反查 Picker 显示名（用于初始化选中项）。</summary>
    private static string SttModelDisplayOf(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        var hit = SpeechService.SttLocalModels.FirstOrDefault(m => m.Name == name);
        return hit.Name is not null ? $"{hit.Name}（{hit.SizeLabel}）" : name;
    }

    /// <summary>同步本地模型状态到 UI。</summary>
    private void RefreshSttModelStatus()
    {
        var name = SttModelNameOf(SttModelDisplay);
        _speech.SttModelName = name;
        SttModelStatus = _speech.LocalModelStatus;
    }

    /// <summary>下载当前选中的本地识别模型（带进度）。</summary>
    [RelayCommand]
    private async Task DownloadSttModel()
    {
        if (IsDownloadingSttModel) return;
        var name = SttModelNameOf(SttModelDisplay);
        if (string.IsNullOrEmpty(name))
        {
            SttModelStatus = "请先选择本地模型";
            return;
        }
        IsDownloadingSttModel = true;
        SttModelStatus = "正在下载…";
        try
        {
            var result = await _speech.DownloadLocalModelAsync(name, p => SttModelProgress = p);
            SttModelStatus = result;
            if (!result.Contains("失败"))
            {
                _speech.SttModelName = name;
                SttModelStatus = _speech.LocalModelStatus;
            }
        }
        catch (Exception ex)
        {
            SttModelStatus = $"下载失败：{ex.Message}";
            App.WriteLog("SettingsViewModel.DownloadSttModel -> " + ex);
        }
        finally
        {
            IsDownloadingSttModel = false;
        }
    }

    /// <summary>删除已下载的本地模型。</summary>
    [RelayCommand]
    private void DeleteSttModel()
    {
        _speech.DeleteLocalModel();
        SttModelStatus = _speech.LocalModelStatus;
    }

    /// <summary>试听当前音色：按当前设置合成试听文本并播放（走系统 TTS 或外部 API，双端均可用）。</summary>
    [RelayCommand]
    private async Task PreviewVoice()
    {
        var text = string.IsNullOrWhiteSpace(PreviewVoiceText) ? "你好，我是温暖如初。" : PreviewVoiceText.Trim();
        try
        {
            // 同步当前界面设置到 SpeechService（RuntimeConfigurator 只在上次 Persist 时下发过）
            var s = _settings.Current;
            _speech.VoiceApiMode = string.IsNullOrWhiteSpace(s.VoiceApiMode) ? "openai" : s.VoiceApiMode;
            _speech.VoiceExtra = s.VoiceExtra ?? "";
            _speech.VoiceApiUrl = s.VoiceApiUrl;
            _speech.VoiceApiKey = s.VoiceApiKey;
            _speech.VoiceTtsModel = s.VoiceTtsModel;
            _speech.VoiceName = s.VoiceName;
            TestVoiceStatus = "正在试听…";
            await _speech.Speak(text);
            TestVoiceStatus = "试听完成。";
        }
        catch (Exception ex)
        {
            TestVoiceStatus = $"试听失败：{ex.Message}";
            App.WriteLog("SettingsViewModel.PreviewVoice -> " + ex);
        }
    }

    /// <summary>测试外部语音服务：按当前设置（可先落盘）合成试听音频并播放，结果写入 <see cref="TestVoiceStatus"/>。</summary>
    [RelayCommand]
    private async Task TestVoiceConnection()
    {
        try
        {
            // 先落盘，保证用户刚填的参数立刻生效
            PersistSettings();
            var s = _settings.Current;
            _speech.VoiceApiMode = string.IsNullOrWhiteSpace(s.VoiceApiMode) ? "openai" : s.VoiceApiMode;
            _speech.VoiceExtra = s.VoiceExtra ?? "";
            _speech.VoiceApiUrl = s.VoiceApiUrl;
            _speech.VoiceApiKey = s.VoiceApiKey;
            _speech.VoiceTtsModel = s.VoiceTtsModel;
            _speech.VoiceSttModel = s.VoiceSttModel;
            _speech.VoiceName = s.VoiceName;
            TestVoiceStatus = "正在测试…";
            var result = await _speech.TestVoiceConnectionAsync();
            TestVoiceStatus = result;
        }
        catch (Exception ex)
        {
            TestVoiceStatus = $"测试失败：{ex.Message}";
            App.WriteLog("SettingsViewModel.TestVoiceConnection -> " + ex);
        }
    }

    [ObservableProperty] private bool _notificationsEnabled = true;
    [ObservableProperty] private bool _greetingEnabled = true;

    [ObservableProperty] private string _weatherCity = "";
    [ObservableProperty] private int _cycleLength = 28;
    [ObservableProperty] private int _periodLength = 5;

    // 小游戏 AI 难度
    [ObservableProperty] private string _gameDifficulty = "normal";
    [ObservableProperty] private string _gameDifficultyDisplay = "普通（默认）";
    [ObservableProperty] private bool _aiAutoDifficulty;
    public List<string> GameDifficultyChoices { get; } = new() { "简单（新手练手）", "普通（默认）", "困难（步步紧逼）" };

    // 云端棋力脑
    [ObservableProperty] private bool _chessApiEnabled;
    [ObservableProperty] private string _chessApiUrl = "";
    [ObservableProperty] private string _chessApiKey = "";
    [ObservableProperty] private string _chessApiModel = "gpt-4o-mini";
    private static string GameDifficultyDisplayOf(string v) => v switch
    {
        "easy" => "简单（新手练手）",
        "hard" => "困难（步步紧逼）",
        _ => "普通（默认）"
    };
    partial void OnGameDifficultyDisplayChanged(string value)
    {
        _gameDifficulty = value switch
        {
            "简单（新手练手）" => "easy",
            "困难（步步紧逼）" => "hard",
            _ => "normal"
        };
        // 旧实现只改字段不落盘：设置页显示「困难」，服务读到的仍是 normal，UI 与设置不一致
        PersistSettings();
    }

    [ObservableProperty] private bool _alwaysOnTop;
    [ObservableProperty] private int _petIdleMinutes;

    // ============ 超时与成本（智能省钱） ============

    /// <summary>全局请求超时（秒）。</summary>
    [ObservableProperty] private int _aiTimeoutSeconds = 25;
    public List<int> TimeoutChoices { get; } = new() { 10, 15, 25, 40, 60, 120, 180 };
    partial void OnAiTimeoutSecondsChanged(int value) => PersistSettings();

    [ObservableProperty] private string _costSummary = "点「刷新用量」看看花了多少。";

    // 钱与 token 分开看：两张表 + 两个排行榜
    [ObservableProperty] private string _priceTable = "";
    [ObservableProperty] private string _tokenTable = "";
    [ObservableProperty] private string _costRanking = "";
    [ObservableProperty] private string _tokenRanking = "";

    /// <summary>单价表快照（落盘用）：由成本服务维护，这里只取当前值。</summary>
    private List<ModelPrice> ModelPricesSnapshot
    {
        get
        {
            try { return _cost.PricesAsync().GetAwaiter().GetResult(); }
            catch { return new List<ModelPrice>(); }
        }
    }
    [ObservableProperty] private string _newPriceModel = "";
    [ObservableProperty] private string _newPriceInput = "";
    [ObservableProperty] private string _newPriceOutput = "";

    [RelayCommand]
    private async Task RefreshCost()
    {
        try
        {
            CostSummary = await _cost.ReportTextAsync();
            PriceTable = await _cost.PriceTableTextAsync();
            TokenTable = await _cost.TokenTableTextAsync();
            CostRanking = await _cost.CostRankTextAsync();
            TokenRanking = await _cost.TokenRankTextAsync();
            await RebuildChartAsync();
        }
        catch (Exception ex)
        {
            App.WriteLog("SettingsVM.RefreshCost -> " + ex.Message);
            CostSummary = "读取用量失败。";
        }
    }

    // ===== 成本图表（条形图：各模型对比；折线图：按天趋势） =====

    /// <summary>自绘图表：页面的 GraphicsView 把它挂上（见 SettingsPage.xaml.cs）。</summary>
    public Drawables.CostChartDrawable CostChart { get; } = new();

    /// <summary>页面注入的重绘回调。</summary>
    public Action? InvalidateCostChart { get; set; }

    [ObservableProperty] private bool _costChartIsLine = true;
    [ObservableProperty] private string _costChartCaption = "";

    /// <summary>按钮文案 = 点了会切到什么。</summary>
    public string CostChartModeLabel => CostChartIsLine ? "切换为条形图（各模型对比）" : "切换为折线图（按天趋势）";

    private bool _chartBusy;

    [RelayCommand]
    private void ToggleChartMode() => CostChartIsLine = !CostChartIsLine;

    partial void OnCostChartIsLineChanged(bool value)
    {
        CostChart.Mode = value ? Drawables.CostChartDrawable.ChartMode.Lines : Drawables.CostChartDrawable.ChartMode.Bars;
        OnPropertyChanged(nameof(CostChartModeLabel));
        if (!_chartBusy) _ = RebuildChartAsync();
    }

    /// <summary>按当前模式重建图表数据。</summary>
    private async Task RebuildChartAsync()
    {
        if (_chartBusy) return;
        _chartBusy = true;
        try
        {
            var prices = await _cost.PricesAsync();
            var hasPrice = prices.Count > 0;
            var totals = await _cost.ModelTotalsAsync();

            // 条形图：各模型的「花费」对比（没录单价就退化成 token 对比）
            var bars = totals.Select(t => new Drawables.CostChartDrawable.Bar(
                    t.Model,
                    hasPrice ? t.Cost : t.Prompt + t.Completion,
                    hasPrice ? $"{t.Cost:0.###}" : Fmt(t.Prompt + t.Completion)))
                .ToList();

            // 折线图：最近 14 天的输入/输出 token
            var daily = await _cost.DailySeriesAsync(14);
            var line = new List<Drawables.CostChartDrawable.Series>
            {
                new("输入", Color.FromArgb("#7BA7D7"), daily.Select(d => (double)d.Prompt).ToList()),
                new("输出", Color.FromArgb("#D79B7B"), daily.Select(d => (double)d.Completion).ToList())
            };
            var labels = daily.Select(d => d.Day.ToString("MM-dd")).ToList();

            CostChart.Bars = bars;
            CostChart.SeriesList = line;
            CostChart.XLabels = labels;
            CostChart.Unit = hasPrice ? "元" : "tok";
            CostChart.EmptyHint = "还没有用量记录（端点返回 usage 后自动记账）";
            CostChart.Mode = CostChartIsLine
                ? Drawables.CostChartDrawable.ChartMode.Lines
                : Drawables.CostChartDrawable.ChartMode.Bars;

            var total = daily.Sum(d => d.Cost);
            var calls = daily.Sum(d => d.Calls);
            CostChartCaption = hasPrice
                ? $"最近 14 天：{calls} 次调用，共 {Fmt(daily.Sum(d => d.Prompt + d.Completion))} tok，花费 {total:0.###}"
                : $"最近 14 天：{calls} 次调用，共 {Fmt(daily.Sum(d => d.Prompt + d.Completion))} tok（录入单价后显示金额）";

            InvalidateCostChart?.Invoke();
        }
        catch (Exception ex)
        {
            App.WriteLog("SettingsVM.RebuildChart -> " + ex.Message);
        }
        finally { _chartBusy = false; }
    }

    private static string Fmt(long v) => v switch
    {
        >= 1_000_000 => (v / 1_000_000.0).ToString("0.#") + "M",
        >= 1_000 => (v / 1_000.0).ToString("0.#") + "k",
        _ => v.ToString()
    };

    /// <summary>把用量明细与单价表导出成 CSV（Excel 可直接打开）。</summary>
    [RelayCommand]
    private async Task ExportCostCsv()
    {
        try
        {
            var path = await _cost.ExportCsvAsync();
            await Shell.Current.DisplayAlert("导出完成", $"已导出到：\n{path}", "好");
        }
        catch (Exception ex)
        {
            App.WriteLog("SettingsVM.ExportCostCsv -> " + ex);
            await Shell.Current.DisplayAlert("导出失败", ex.Message, "好");
        }
    }

    /// <summary>录入单价：按「每百万 token」填输入价与输出价。</summary>
    [RelayCommand]
    private async Task AddModelPrice()
    {
        var model = (NewPriceModel ?? "").Trim();
        if (model.Length == 0) { CostSummary = "先填模型名（要和请求里用的模型名一致）。"; return; }
        double? Parse(string s)
            => double.TryParse((s ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v >= 0 ? v : null;

        var input = Parse(NewPriceInput);
        var output = Parse(NewPriceOutput);
        // 只填一侧也接受：很多服务只关心输入价，强制两边都填是多余的负担
        var (ok, message) = await _cost.SetPriceAsync(model, input, output);
        if (!ok) { CostSummary = message; return; }

        _settings.Apply(_settings.Current with
        {
            ModelPrices = (await _cost.PricesAsync()).ToList()
        });
        await _settings.Persist();
        NewPriceModel = "";
        NewPriceInput = "";
        NewPriceOutput = "";
        await RefreshCost();
    }

    /// <summary>按当前上下文上限试算一次「现在总结 vs 不总结」（真实决策发生在对话里，这里是预估）。</summary>
    [RelayCommand]
    private async Task TryAdvice()
    {
        try
        {
            var limit = ContextTokenLimit > 0 ? ContextTokenLimit : 32000;
            var keep = Math.Max(500, limit / 3);
            var dropped = Math.Max(500, limit - keep);
            var advice = await _cost.AdviseAsync(AiModel, limit, keep, dropped, isWork: false);
            CostSummary = advice.Reason;
        }
        catch (Exception ex)
        {
            App.WriteLog("SettingsVM.TryAdvice -> " + ex.Message);
            CostSummary = "试算失败：" + ex.Message;
        }
    }

    // ============ 能力包（Skills） ============

    [ObservableProperty] private string _skillsSummary = "正在读取…";

    public async Task RefreshSkillsAsync()
    {
        try
        {
            var list = await _skills.ListAsync();
            if (list.Count == 0) { SkillsSummary = "还没有装能力包。"; return; }
            var enabled = list.Count(s => s.Enabled);
            SkillsSummary = $"已装 {list.Count} 个，启用 {enabled} 个："
                + string.Join("、", list.Select(s => s.Name + (s.Enabled ? "" : "（已停用）")));
        }
        catch (Exception ex)
        {
            App.WriteLog("SettingsVM.RefreshSkills -> " + ex.Message);
            SkillsSummary = "读取能力包失败。";
        }
    }

    [RelayCommand]
    private async Task ImportSkill()
    {
        try
        {
            var picked = await FilePicker.PickAsync(new PickOptions
            {
                FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    { DevicePlatform.WinUI, new[] { ".zip" } },
                    { DevicePlatform.Android, new[] { "application/zip", "application/octet-stream" } },
                    { DevicePlatform.iOS, new[] { "public.zip-archive" } }
                }),
                PickerTitle = "选择能力包（zip，内含 SKILL.md 与/或 tool.json）"
            });
            if (picked is null) return;
            var (ok, message) = await _skills.ImportAsync(picked.FullPath);
            await Shell.Current.DisplayAlert("能力包", message, "好");
            if (ok)
            {
                await _tools.RefreshSkillHintAsync();
                await RefreshSkillsAsync();
            }
        }
        catch (Exception ex)
        {
            App.WriteLog("SettingsVM.ImportSkill -> " + ex);
            await Shell.Current.DisplayAlert("能力包", "导入失败：" + ex.Message, "好");
        }
    }

    /// <summary>管理已装能力包：启停 / 看说明书 / 删除。</summary>
    [RelayCommand]
    private async Task ManageSkills()
    {
        var list = await _skills.ListAsync();
        if (list.Count == 0)
        {
            await Shell.Current.DisplayAlert("能力包", "还没有装能力包。用「导入 zip / 目录」装上第一个。", "好");
            return;
        }
        var pick = await Shell.Current.DisplayActionSheet("管理能力包", "取消", null,
            list.Select(s => $"{s.Name}｜{(s.Enabled ? "已启用" : "已停用")}").ToArray());
        if (pick is null || pick == "取消") return;
        var name = pick.Split('｜')[0].Trim();
        var skill = list.FirstOrDefault(s => s.Name == name);
        if (skill is null) return;

        var action = await Shell.Current.DisplayActionSheet(
            $"「{skill.Name}」{skill.Description}", "取消", null,
            skill.Enabled ? "停用" : "启用", "查看说明书", "删除");
        if (action is null || action == "取消") return;

        switch (action)
        {
            case "停用":
            case "启用":
                await _skills.SetEnabledAsync(skill.Name, action == "启用");
                break;
            case "查看说明书":
                {
                    var text = _skills.ReadInstructions(skill.Name);
                    await Shell.Current.DisplayAlert(skill.Name, string.IsNullOrWhiteSpace(text) ? "这个能力包没有说明书。" : text, "好");
                    return;
                }
            case "删除":
                {
                    var yes = await Shell.Current.DisplayAlert("删除能力包", $"删除「{skill.Name}」及其全部文件？", "删除", "取消");
                    if (!yes) return;
                    await _skills.RemoveAsync(skill.Name);
                    break;
                }
        }
        await _tools.RefreshSkillHintAsync();
        await RefreshSkillsAsync();
    }

    // 移动端屏幕方向
    [ObservableProperty] private string _screenOrientation = "auto";    [ObservableProperty] private string _screenOrientationDisplay = "自动（竖屏）";
    public List<string> ScreenOrientationChoices { get; } = new()
        { "自动（竖屏）", "横屏", "竖屏" };

    /// <summary>是否移动设备（手机/平板）：桌面端隐藏屏幕方向设置项。</summary>
    public bool IsMobile => DeviceInfo.Platform == DevicePlatform.Android || DeviceInfo.Platform == DevicePlatform.iOS;
    partial void OnScreenOrientationDisplayChanged(string value)
    {
        _screenOrientation = value switch
        {
            "横屏" => "landscape",
            "竖屏" => "portrait",
            _ => "auto",
        };
        PersistSettings();
        // 实际锁屏 + 3 秒防呆回弹（防呆弹框由页面层负责；确认"保留新方向"时 UI 同步 Picker）
        ScreenOrientationService.Set(_screenOrientation);
    }
    /// <summary>防呆回弹后用户选「保留新方向」时调用：重新锁定新方向，并把 UI 同步回新值。</summary>
    public async Task ConfirmOrientationKeepNewAsync()
    {
        var display = _screenOrientation switch
        {
            "landscape" => "横屏",
            "portrait" => "竖屏",
            _ => "自动（竖屏）",
        };
        ScreenOrientationService.Set(_screenOrientation);
        // UI 同步（Picker 此时停留在旧方向显示，切回新方向）
        ScreenOrientationDisplay = display;
        await Shell.Current.DisplayAlert("屏幕方向", $"已锁定为「{display}」。", "好");
    }

    [ObservableProperty] private bool _qqBotEnabled;
    [ObservableProperty] private string _qqAppId = "";

    // ===== QQ 桥增强：群聊 / 用户识别 / 主动外发 =====

    /// <summary>是否回复群聊消息（默认否）。</summary>
    [ObservableProperty] private bool _qqReplyGroup;
    /// <summary>用来认出「用户本人」的 openid（留空则记最近私聊过的人）。</summary>
    [ObservableProperty] private string _qqUserOpenId = "";
    /// <summary>角色对用户的称呼（识别到本人时使用）。</summary>
    [ObservableProperty] private string _qqUserName = "";
    /// <summary>允许角色主动发消息到用户手机。</summary>
    [ObservableProperty] private bool _qqProactiveEnabled;
    /// <summary>外发前是否需要人工确认。</summary>
    [ObservableProperty] private bool _qqProactiveRequireConfirm = true;
    /// <summary>外发每小时上限。</summary>
    [ObservableProperty] private int _qqProactivePerHour = 3;
    public List<int> QqProactiveRateChoices { get; } = new() { 1, 3, 5, 10 };

    /// <summary>主动外发走哪个通道：自动 / 只走 QQ / 只走微信。</summary>
    [ObservableProperty] private string _outboundChannelDisplay = "自动";
    public List<string> OutboundChannelChoices { get; } = new() { "自动", "只走 QQ", "只走微信" };
    partial void OnOutboundChannelDisplayChanged(string value) => PersistSettings();
    private string OutboundChannelValue => OutboundChannelDisplay switch
    {
        "只走 QQ" => "qq",
        "只走微信" => "wechat",
        _ => "auto"
    };

    partial void OnQqReplyGroupChanged(bool value) => PersistSettings();
    partial void OnQqUserOpenIdChanged(string value) => PersistSettings();
    partial void OnQqUserNameChanged(string value) => PersistSettings();
    partial void OnQqProactiveEnabledChanged(bool value) => PersistSettings();
    partial void OnQqProactiveRequireConfirmChanged(bool value) => PersistSettings();
    partial void OnQqProactivePerHourChanged(int value) => PersistSettings();
    [ObservableProperty] private string _qqAppSecret = "";
    [ObservableProperty] private bool _wechatEnabled;
    [ObservableProperty] private string _wechatAppId = "";
    [ObservableProperty] private string _wechatAppSecret = "";
    [ObservableProperty] private string _wechatToken = "";
    [ObservableProperty] private string _wechatPort = "8012";
    [ObservableProperty] private string _statusText = "";

    // MCP 配置
    [ObservableProperty] private bool _mcpEnabled;
    [ObservableProperty] private bool _mcpAutoApprove = true;
    [ObservableProperty] private string _mcpNetworkUrl = "";
    [ObservableProperty] private string _mcpGitHubRepo = "";
    [ObservableProperty] private string _mcpImportZipPath = "";
    [ObservableProperty] private string _mcpImportFolderPath = "";
    [ObservableProperty] private bool _isImporting;
    [ObservableProperty] private double _importProgress;
    [ObservableProperty] private string _importStatus = "";
    /// <summary>MCP 数据包列表（已导入 / 新建）。</summary>
    public ObservableCollection<Modules.Mcp.McpServerItem> McpServers { get; } = new();
    /// <summary>MCP 列表加载中标记（true 时设置页显示 loading 圈圈，防止 UI 动画停滞）。</summary>
    [ObservableProperty] private bool _mcpServersLoading = true;
    /// <summary>设置页级 loading 遮罩（首帧渲染前盖住内容；OnAppearing 后收起，让转场动画完整播放）。</summary>
    [ObservableProperty] private bool _pageLoading = true;

    // ---- 工具模式运行时（Java / Python）----
    [ObservableProperty] private string _pythonStatus = "未检测（点击下方「检测运行时」）";
    [ObservableProperty] private string _javaStatus = "未检测（点击下方「检测运行时」）";
    [ObservableProperty] private string _pythonManualPath = "";
    [ObservableProperty] private string _javaManualPath = "";
    [ObservableProperty] private bool _isRuntimeBusy;
    [ObservableProperty] private string _runtimeBusyText = "";
    public bool RuntimeOsSupported => _runtimes is { } r && r.IsDesktop;
    /// <summary>Android 沙箱路线是否可用（proot 安装向导入口显示条件）。</summary>
    public bool SandboxAvailable => _runtimes is { } r && r.UseSandbox;

    // ---- 开发者展示模式 ----
    [ObservableProperty] private bool _showcaseUnlocked;
    /// <summary>设置页是否已滚动到底部（隐藏入口只在最底部生效）。</summary>
    [ObservableProperty] private bool _atScrollBottom;
    /// <summary>连击点按计数：翻到设置最底部连续点按 114514 次后解锁。</summary>
    [ObservableProperty] private int _secretTapCount;
    private const int SecretTapsNeeded = 114514;
    private int _secretStamp;
    private readonly object SecretGate = new();

    public List<string> Langs { get; } = new() { "简体中文", "繁體中文", "English", "日本語" };
    public List<string> KeySfxChoices { get; } = new() { "default", "soft", "typewriter", "none" };
    public List<int> MemoryTurnsChoices { get; } = new() { 3, 5, 8, 10, 12, 15, 20 };

    /// <summary>人类记忆调取窗口（天）：0 = 不限（能想起最早的记忆）。</summary>
    public List<double> MemoryWindowChoices { get; } = new() { 1, 3, 7, 14, 30, 90, 0 };
    public List<int> CycleChoices { get; } = Enumerable.Range(21, 15).ToList();
    public List<int> PeriodChoices { get; } = Enumerable.Range(3, 5).ToList();
    /// <summary>桌宠闲置时长可选值（分钟）：0=关闭闲置自动桌宠。</summary>
    public List<int> PetIdleChoices { get; } = new() { 0, 1, 3, 5, 10, 15, 30, 60 };

    // ============ #E3 桌宠思考 ============

    /// <summary>桌宠思考模式（显示名：关闭 / 随机 / 定时）。</summary>
    [ObservableProperty] private string _petThinkModeDisplay = "关闭";
    public List<string> PetThinkModeChoices { get; } = new() { "关闭", "随机", "定时" };
    /// <summary>思考间隔（分钟）。</summary>
    [ObservableProperty] private int _petThinkIntervalMinutes = 10;
    public List<int> PetThinkIntervalChoices { get; } = new() { 5, 10, 15, 30, 60 };

    private static string ThinkModeDisplayOf(string key) => key switch
    {
        "random" => "随机",
        "timer" => "定时",
        _ => "关闭"
    };

    private static string ThinkModeKeyOf(string display) => display switch
    {
        "随机" => "random",
        "定时" => "timer",
        _ => "off"
    };

    partial void OnPetThinkModeDisplayChanged(string value) => PersistSettings();
    partial void OnPetThinkIntervalMinutesChanged(int value) => PersistSettings();

    partial void OnBgmLevelChanged(double value) => PersistSettings();
    partial void OnSfxLevelChanged(double value) => PersistSettings();
    partial void OnGlassOnChanged(bool value)
    {
        OnPropertyChanged(nameof(GlassModeDisplay));
        if (value && !FrostOn) FrostOn = true;
        _theme.Glass = value;
        PersistSettings();
    }
    partial void OnFrostOnChanged(bool value)
    {
        OnPropertyChanged(nameof(GlassModeDisplay));
        if (!value) GlassOn = false;
        _theme.Frost = value;
        OnPropertyChanged(nameof(CanGlass));
        PersistSettings();
    }
    partial void OnLiquidOnChanged(bool value) { _theme.Liquid = value; PersistSettings();  OnPropertyChanged(nameof(GlassModeDisplay)); }
    partial void OnGlassTranslucencyChanged(double value)
    {
        _theme.GlassTranslucency = value;
        OnPropertyChanged(nameof(GlassActive));
        OnPropertyChanged(nameof(GlassInactive));
        PersistSettings();
    }
    partial void OnGlassReducedTransparencyChanged(bool value)
    {
        _theme.ReducedTransparency = value;
        PersistSettings();
    }
    partial void OnGlassFrostChanged(double value)
    {
        _theme.GlassFrost = value;
        PersistSettings();
    }
    partial void OnGlassHeightChanged(double value)
    {
        _theme.GlassHeight = value;
        PersistSettings();
    }
    partial void OnGlassLightXChanged(double value) { _theme.GlassLightX = value; PersistSettings(); }
    partial void OnGlassLightYChanged(double value) { _theme.GlassLightY = value; PersistSettings(); }
    partial void OnGlassLightZChanged(double value) { _theme.GlassLightZ = value; PersistSettings(); }
    partial void OnGlassLightWidthChanged(double value) { _theme.GlassLightWidth = value; PersistSettings(); }
    partial void OnThemeDisplayChanged(string value) { ThemeName = ThemeKeyOf(value); _theme.ThemeName = ThemeName; PersistSettings(); }
    partial void OnComplexPlotChanged(bool value) => PersistSettings();
    partial void OnNovelTestingChanged(bool value) => PersistSettings();
    partial void OnShowAffectionChanged(bool value) => PersistSettings();
    partial void OnAffectionLevelUpChanged(bool value) => PersistSettings();
    partial void OnBattleEnabledChanged(bool value) => PersistSettings();
    partial void OnBattleAiCanStartChanged(bool value) => PersistSettings();
    partial void OnLightningEnabledChanged(bool value) => PersistSettings();
    partial void OnLightningProbabilityChanged(string value) => PersistSettings();
    partial void OnRainEventEnabledChanged(bool value) => PersistSettings();
    partial void OnRainProbabilityChanged(string value) => PersistSettings();
    partial void OnLightningSfxPathChanged(string value) => PersistSettings();
    partial void OnRainSfxPathChanged(string value) => PersistSettings();
    partial void OnLightningFramesTextChanged(string value) => PersistSettings();
    partial void OnRainFramesTextChanged(string value) => PersistSettings();
    partial void OnWeatherEffectFrameMsChanged(string value) => PersistSettings();
    partial void OnAiTravelAloneChanged(bool value) => PersistSettings();
    partial void OnAllowFollowAiChanged(bool value) => PersistSettings();

    /// <summary>把多行文本按行拆成路径列表（去空行与首尾空白）。设置页「每行一个路径」用。</summary>
    private static List<string> SplitLines(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return new List<string>();
        return text.Split('\n', '\r')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToList();
    }

    /// <summary>解析 0-1 概率文本；非法或越界则回退默认值。</summary>
    private static double ParseProb(string? text, double fallback)
    {
        if (double.TryParse((text ?? "").Trim(), out var v)) return Math.Clamp(v, 0, 1);
        return fallback;
    }

    /// <summary>解析帧间隔毫秒文本；非法则回退 120，并夹在 30..5000。</summary>
    private static int ParseFrameMs(string? text)
    {
        if (int.TryParse((text ?? "").Trim(), out var v)) return Math.Clamp(v, 30, 5000);
        return 120;
    }
    partial void OnMenuRightChanged(bool value) => PersistSettings();
    partial void OnChatStyleDisplayChanged(string value) { ChatStyle = ChatStyleKeyOf(value); PersistSettings(); }
    partial void OnAllowSilenceChanged(bool value) => PersistSettings();
    partial void OnLangChanged(string value)
    {
        PersistSettings();
        LocalizationService.Current.SetCulture(value);
    }

    partial void OnLangDisplayChanged(string value) => Lang = LangCodeOf(value);
    partial void OnTextSpeedChanged(double value) => PersistSettings();
    partial void OnAutoSaveChanged(bool value) => PersistSettings();
    partial void OnKeySfxChanged(string value) => PersistSettings();

    partial void OnAiUrlChanged(string value)
    {
        PersistSettings();
        _ = FetchModelsAsync();
    }
    partial void OnAiKeyChanged(string value)
    {
        PersistSettings();
        _ = FetchModelsAsync();
    }
    partial void OnAiModelChanged(string value) => PersistSettings();
    partial void OnAiTemperatureChanged(double value) => PersistSettings();
    partial void OnAiMaxTokensChanged(double value) => PersistSettings();
    partial void OnDeepThinkChanged(bool value) => PersistSettings();
    partial void OnDeepModelChanged(string value) => PersistSettings();
    partial void OnMemoryTurnsChanged(int value) => PersistSettings();
    partial void OnMemoryWindowDaysChanged(double value) => PersistSettings();
    partial void OnMemoryRecallRandomnessChanged(double value) => PersistSettings();

    partial void OnTtsEnabledChanged(bool value) => PersistSettings();
    partial void OnTtsRateChanged(double value) => PersistSettings();
    partial void OnSttEnabledChanged(bool value) => PersistSettings();
    partial void OnTtsEngineChanged(string value) { OnPropertyChanged(nameof(NeedsVoiceApi)); PersistSettings(); }
    partial void OnSttEngineChanged(string value) { OnPropertyChanged(nameof(NeedsVoiceApi)); PersistSettings(); }
    partial void OnVoiceApiUrlChanged(string value) => PersistSettings();
    partial void OnVoiceApiKeyChanged(string value) => PersistSettings();
    partial void OnVoiceTtsModelChanged(string value) => PersistSettings();
    partial void OnVoiceSttModelChanged(string value) => PersistSettings();
    partial void OnVoiceNameChanged(string value) => PersistSettings();
    partial void OnVoiceExtraChanged(string value) => PersistSettings();
    partial void OnVoiceApiModeChanged(string value)
    {
        VoiceModeDisplay = value == "sovits" ? "GPT-SoVITS（本地/局域网）" : "OpenAI 兼容（网络 API）";
        PersistSettings();
    }
    partial void OnSttModelDisplayChanged(string value)
    {
        RefreshSttModelStatus();
        PersistSettings();
    }

    /// <summary>朗读或识别选了 API 引擎时，显示 API 语音配置区。</summary>
    public bool NeedsVoiceApi => TtsEngine == "api" || SttEngine == "api";

    partial void OnNotificationsEnabledChanged(bool value) => PersistSettings();
    partial void OnGreetingEnabledChanged(bool value) => PersistSettings();

    partial void OnWeatherCityChanged(string value) => PersistSettings();
    partial void OnCycleLengthChanged(int value) => PersistSettings();
    partial void OnPeriodLengthChanged(int value) => PersistSettings();

    partial void OnAlwaysOnTopChanged(bool value) => PersistSettings();
    partial void OnPetIdleMinutesChanged(int value) => PersistSettings();

    // 以下四项原先没有任何变更处理器：改了既不落盘、也不下发，
    // 而小游戏/棋力服务读的是已落盘的 UserSettings —— 于是设置页显示与真实生效值不一致。
    partial void OnAiAutoDifficultyChanged(bool value) => PersistSettings();
    partial void OnChessApiEnabledChanged(bool value) => PersistSettings();
    partial void OnChessApiUrlChanged(string value) => PersistSettings();
    partial void OnChessApiKeyChanged(string value) => PersistSettings();
    partial void OnChessApiModelChanged(string value) => PersistSettings();
    // 自动更新通道：此前无处理器且模型里没有该字段，重开设置页永远是「正式版」
    partial void OnBetaChannelChanged(bool value) => PersistSettings();

    partial void OnQqBotEnabledChanged(bool value) => PersistSettings();
    partial void OnQqAppIdChanged(string value) => PersistSettings();
    partial void OnQqAppSecretChanged(string value) => PersistSettings();
    partial void OnWechatEnabledChanged(bool value) => PersistSettings();
    partial void OnWechatAppIdChanged(string value) => PersistSettings();
    partial void OnWechatAppSecretChanged(string value) => PersistSettings();
    partial void OnWechatTokenChanged(string value) => PersistSettings();
    partial void OnWechatPortChanged(string value) => PersistSettings();

    partial void OnMcpEnabledChanged(bool value) => PersistSettings();
    partial void OnMcpNetworkUrlChanged(string value) { _mcp.NetworkUrl = value; PersistSettings(); }
    partial void OnMcpAutoApproveChanged(bool value) { _mcp.AutoApprove = value; PersistSettings(); }
    partial void OnMcpGitHubRepoChanged(string value) => PersistSettings();
    partial void OnMcpImportZipPathChanged(string value) => PersistSettings();
    partial void OnMcpImportFolderPathChanged(string value) => PersistSettings();

    private static string ThemeKeyOf(string display) => display switch
    {
        "樱花粉" => "sakura",
        "翠竹绿" => "bamboo",
        "晨雾蓝灰" => "mist",
        _ => "classic"
    };

    private static string ChatStyleDisplayOf(string key) => key switch
    {
        "bubble" => "气泡式",
        _ => "剧本式"
    };

    private static string ChatStyleKeyOf(string display) => display switch
    {
        "气泡式" => "bubble",
        _ => "galgame"
    };

    private static string LangCodeOf(string display) => display switch
    {
        "繁體中文" => "zh-TW",
        "English" => "en-US",
        "日本語" => "ja-JP",
        _ => "zh-CN"
    };

    private static string LangDisplayOf(string code) => code switch
    {
        "zh-TW" => "繁體中文",
        "en-US" => "English",
        "ja-JP" => "日本語",
        _ => "简体中文"
    };

    // 防抖：拖动滑杆 / 连续改设置时，每次变更都要跑一遍
    // 「序列化整个 UserSettings + 落盘 + _settings.Apply → RuntimeConfigurator.Apply」
    // （Apply 里还会重配 AI / 天气 / 语音 / 问候 / 官方接入桥），代价很高，
    // 连续操作会叠加成数秒卡死。这里改成只在停手后跑一次。
    private System.Threading.Timer? _persistTimer;
    private readonly object _persistLock = new();

    private void PersistSettings()
    {
        lock (_persistLock)
        {
            _persistTimer ??= new System.Threading.Timer(
                _ => MainThread.BeginInvokeOnMainThread(PersistNow), null,
                Timeout.Infinite, Timeout.Infinite);
            _persistTimer.Change(300, Timeout.Infinite);
        }
    }

    private async void PersistNow()
    {
        using var _scope = Services.PerfLog.Measure("保存设置（序列化 + 下发）");
        try
        {
            var port = int.TryParse(WechatPort, out var p) && p > 0 && p < 65536 ? p : 8012;
            // 必须以「当前设置」为基底：原来用 new UserSettings{...} 重建整份，
            // 凡是没列在这个初始化器里的字段（免责声明确认标记、开发者模式解锁等）
            // 每次保存设置都会被默认值冲掉——这正是「免责声明每次启动都弹」的真凶。
            var s = _settings.Current with
            {
                BgmLevel = BgmLevel,
                SfxLevel = SfxLevel,
                ComplexPlot = ComplexPlot,
                NovelTestingEnabled = NovelTesting,
                ShowAllAffection = ShowAffection,
                AffectionLevelUpEnabled = AffectionLevelUp,
                BattleEnabled = BattleEnabled,
                BattleAiCanStart = BattleAiCanStart,
                LightningEnabled = LightningEnabled,
                LightningProbability = ParseProb(LightningProbability, 0.06),
                RainEventEnabled = RainEventEnabled,
                RainProbability = ParseProb(RainProbability, 0.15),
                LightningSfxPath = (LightningSfxPath ?? "").Trim(),
                RainSfxPath = (RainSfxPath ?? "").Trim(),
                LightningFrames = SplitLines(LightningFramesText),
                RainFrames = SplitLines(RainFramesText),
                WeatherEffectFrameMs = ParseFrameMs(WeatherEffectFrameMs),
                AiTravelAlone = AiTravelAlone,
                AllowFollowAi = AllowFollowAi,
                FrostEnabled = FrostOn,
                GlassEnabled = GlassOn,
                LiquidEnabled = LiquidOn,
                GlassTranslucency = GlassTranslucency,
                GlassReducedTransparency = GlassReducedTransparency,
GlassFrost = GlassFrost,
GlassHeight = GlassHeight,
BackgroundMode = BackgroundMode,
BackgroundImagePath = BackgroundImagePath,
GlassLightX = GlassLightX,
GlassLightY = GlassLightY,
GlassLightZ = GlassLightZ,
GlassLightWidth = GlassLightWidth,
                ThemeName = ThemeKeyOf(ThemeDisplay),
                MenuSide = MenuRight ? "right" : "left",
                Lang = Lang,
                TextSpeed = TextSpeed,
                AutoSaveEnabled = AutoSave,
                KeySfx = KeySfx,
                ChatStyle = ChatStyle,
                AllowSilence = AllowSilence,

                AiUrl = string.IsNullOrWhiteSpace(AiUrl) ? "https://api.openai.com/v1/chat/completions" : AiUrl.Trim(),
                AiKey = AiKey.Trim(),
                AiModel = string.IsNullOrWhiteSpace(AiModel) ? "gpt-4o" : AiModel.Trim(),
                AiPreset = PresetKeyOf(_aiPreset),
                VisionUrl = VisionUrl.Trim(),
                VisionKey = VisionKey.Trim(),
                VisionModel = VisionModel.Trim(),
                AiTemperature = AiTemperature,
                AiMaxTokens = Math.Clamp((int)AiMaxTokens, 100, 8000),
                DeepThink = DeepThink,
                DeepModel = DeepModel.Trim(),
                MemoryTurns = MemoryTurns,
                MemoryWindowDays = MemoryWindowDays,
                MemoryRecallRandomness = Math.Clamp(MemoryRecallRandomness, 0, 1),
                SubProfileAiRewrite = SubProfileAiRewrite,
                SemanticAiAssist = SemanticAiAssist,
                SmartSavingEnabled = SmartSavingEnabled,
                LightMode = LightMode,
                PreferDiscreteGpu = PreferDiscreteGpu,
                AiProviders = AiProviders.Select(v => v.ToProvider())
                    .Where(v => !string.IsNullOrWhiteSpace(v.Url) || v.Keys.Count > 0)
                    .ToList(),
                AiRotationEnabled = AiRotationEnabled,
                AiTimeoutSeconds = AiTimeoutSeconds > 0 ? AiTimeoutSeconds : 25,
                ModelPrices = ModelPricesSnapshot,
                ContextTokenLimit = Math.Clamp(_contextTokenLimit > 0 ? _contextTokenLimit : 32000, 4000, 256000),

                TtsEnabled = TtsEnabled,
                TtsRate = TtsRate,
                SttEnabled = SttEnabled,
                TtsEngine = string.IsNullOrWhiteSpace(TtsEngine) ? "system" : TtsEngine,
                SttEngine = string.IsNullOrWhiteSpace(SttEngine) ? "system" : SttEngine,
                VoiceApiUrl = string.IsNullOrWhiteSpace(VoiceApiUrl) ? "https://api.openai.com/v1" : VoiceApiUrl.Trim(),
                VoiceApiKey = (VoiceApiKey ?? "").Trim(),
                VoiceTtsModel = string.IsNullOrWhiteSpace(VoiceTtsModel) ? "tts-1" : VoiceTtsModel.Trim(),
                VoiceSttModel = string.IsNullOrWhiteSpace(VoiceSttModel) ? "whisper-1" : VoiceSttModel.Trim(),
                VoiceName = string.IsNullOrWhiteSpace(VoiceName) ? "alloy" : VoiceName.Trim(),
                VoiceApiMode = string.IsNullOrWhiteSpace(VoiceApiMode) ? "openai" : VoiceApiMode.Trim().ToLowerInvariant(),
                VoiceExtra = (VoiceExtra ?? "").Trim(),
                SttModelName = SttModelNameOf(SttModelDisplay),

                NotificationsEnabled = NotificationsEnabled,
                GreetingEnabled = GreetingEnabled,

                WeatherCity = WeatherCity.Trim(),
                CycleLength = CycleLength,
                PeriodLength = PeriodLength,

                AlwaysOnTop = AlwaysOnTop,
                PetIdleMinutes = Math.Max(0, PetIdleMinutes),
                PetThinkMode = ThinkModeKeyOf(_petThinkModeDisplay),
                PetThinkIntervalMinutes = Math.Max(1, PetThinkIntervalMinutes),
                ScreenOrientation = ScreenOrientation,

                QqBotEnabled = QqBotEnabled,
                QqAppId = QqAppId.Trim(),
                QqAppSecret = QqAppSecret.Trim(),
                QqBoundCharacterId = _qqBoundCharacterId,
                QqReplyGroup = QqReplyGroup,
                QqUserOpenId = (QqUserOpenId ?? "").Trim(),
                QqUserName = (QqUserName ?? "").Trim(),
                QqProactiveEnabled = QqProactiveEnabled,
                QqProactiveRequireConfirm = QqProactiveRequireConfirm,
                QqProactivePerHour = Math.Clamp(QqProactivePerHour, 1, 30),
                RealOutboundChannel = OutboundChannelValue,
                WechatEnabled = WechatEnabled,
                WechatAppId = WechatAppId.Trim(),
                WechatAppSecret = WechatAppSecret.Trim(),
                WechatToken = WechatToken.Trim(),
                 WechatPort = port,

                 McpEnabled = McpEnabled,
                 McpAutoApprove = McpAutoApprove,
                 McpNetworkUrl = McpNetworkUrl.Trim(),
                 McpGitHubRepo = McpGitHubRepo.Trim(),
                 McpImportZipPath = McpImportZipPath.Trim(),
                 McpImportFolderPath = McpImportFolderPath.Trim(),

                 GameDifficulty = GameDifficulty,
                 AiAutoDifficulty = AiAutoDifficulty,
                 ChessApiEnabled = ChessApiEnabled,
                 ChessApiUrl = ChessApiUrl.Trim(),
                 ChessApiKey = ChessApiKey.Trim(),
                 ChessApiModel = string.IsNullOrWhiteSpace(ChessApiModel) ? "gpt-4o-mini" : ChessApiModel.Trim(),

                 BetaChannel = BetaChannel,
DeveloperShowcaseUnlocked = ShowcaseUnlocked,
                 PythonPath = _runtimes.ManualPythonPath,
                 JavaPath = _runtimes.ManualJavaPath
             };
            _settings.Apply(s);
            await _settings.Persist();
        }
        catch (Exception ex)
        {
            App.WriteLog("SettingsViewModel.PersistSettings EX -> " + ex);
        }
    }

    // 与 PersistNow 同体（主线程执行，落盘失败只记日志不阻塞），但返回 Task 供导出前 await
    private async Task PersistNowAsync()
    {
        await Task.Run(PersistNow);
    }

    /// <summary>检测 API 并获取可用模型列表。</summary>
    [RelayCommand]
    private async Task FetchModelsAsync()
    {
        if (string.IsNullOrWhiteSpace(AiUrl) || string.IsNullOrWhiteSpace(AiKey))
        {
            AiModelChoices.Clear();
            ModelListStatus = "请先填写 API 地址和密钥";
            return;
        }
        IsLoadingModels = true;
        ModelListStatus = "检测中...";
        try
        {
            var services = Application.Current?.Handler?.MauiContext?.Services;
            if (services is null) { ModelListStatus = "无法获取服务"; return; }
            var api = services.GetService(typeof(Modules.ApiManager.ApiGateway)) as Modules.ApiManager.ApiGateway;
            if (api is null) { ModelListStatus = "API 服务不可用"; return; }

            // 先配置 API
            api.Configure(new AiEndpoint
            {
                Url = AiUrl.Trim(),
                Key = AiKey.Trim(),
                Model = AiModel.Trim(),
                Temperature = AiTemperature,
                MaxTokens = (int)AiMaxTokens
            });

            var models = await api.ListModels();
            if (models is not null && models.Count > 0)
            {
                AiModelChoices.Clear();
                foreach (var m in models.OrderBy(x => x))
                    AiModelChoices.Add(m);
                ModelListStatus = $"已加载 {models.Count} 个模型";
                // 如果当前模型不在列表中，自动选择第一个
                if (!AiModelChoices.Contains(AiModel))
                    AiModel = AiModelChoices[0];
            }
            else
            {
                AiModelChoices.Clear();
                ModelListStatus = "无法获取模型列表（可能 API 密钥无效或端点不支持）";
            }
        }
        catch (Exception ex)
        {
            AiModelChoices.Clear();
            ModelListStatus = $"检测失败: {ex.Message}";
        }
        finally
        {
            IsLoadingModels = false;
        }
    }

    [RelayCommand]
    private async Task TestConnection()
    {
        try
        {
            var result = await _bridge.TestAsync();
            await Shell.Current.DisplayAlert("官方接入测试", result, "好的");
        }
        catch (Exception ex)
        {
            App.WriteLog("SettingsViewModel.TestConnection -> " + ex);
        }
    }

    [RelayCommand]
    private async Task ImportMcpFromGitHub()
    {
        if (IsImporting) return;
        try
        {
            if (string.IsNullOrWhiteSpace(McpGitHubRepo))
            {
                await Shell.Current.DisplayAlert("MCP", "请先输入 GitHub 仓库 URL", "好");
                return;
            }
            IsImporting = true;
            ImportProgress = 0;
            ImportStatus = LocalizationService.Current["Settings_ImportReady"];
            var progress = new Progress<string>(s => ImportStatus = s);
            var result = await _mcp.ImportFromGitHub(McpGitHubRepo, _mcp.McpPackDir, progress);
            RefreshMcpServers();
            await Shell.Current.DisplayAlert("MCP GitHub 导入", result, "好");
        }
        catch (Exception ex)
        {
            App.WriteLog("SettingsViewModel.ImportMcpFromGitHub -> " + ex);
        }
        finally
        {
            IsImporting = false;
            ImportProgress = 0;
        }
    }

    [RelayCommand]
    private async Task ImportMcpZip()
    {
        if (IsImporting) return;
        try
        {
            var pick = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "选择 MCP 数据包 ZIP",
                FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    { DevicePlatform.WinUI, new[] { ".zip" } },
                    { DevicePlatform.Android, new[] { "application/zip" } },
                    { DevicePlatform.iOS, new[] { "public.zip-archive" } }
                })
            });
            if (pick is null) return;
            McpImportZipPath = pick.FullPath;
            IsImporting = true;
            ImportProgress = 0;
            ImportStatus = LocalizationService.Current["Settings_ImportUnzip"];
            var progress = new Progress<(int done, int total)>(t =>
                ImportProgress = t.total == 0 ? 0 : (double)t.done / t.total);
            var result = await _mcp.ImportZip(pick.FullPath, _mcp.McpPackDir, progress);
            RefreshMcpServers();
            await Shell.Current.DisplayAlert("MCP ZIP 导入", result, "好");
        }
        catch (Exception ex)
        {
            App.WriteLog("SettingsViewModel.ImportMcpZip -> " + ex);
        }
        finally
        {
            IsImporting = false;
            ImportProgress = 0;
        }
    }

    [RelayCommand]
    private async Task ImportMcpFolder()
    {
        if (IsImporting) return;
        try
        {
#if WINDOWS
            var wnd = Application.Current?.Windows.FirstOrDefault(w => w.Handler is not null)?.Handler?.PlatformView
                as Microsoft.UI.Xaml.Window;
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(wnd);
            var picker = new Windows.Storage.Pickers.FolderPicker();
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            picker.FileTypeFilter.Add("*");
            var picked = await picker.PickSingleFolderAsync();
            if (picked is null) return;
            McpImportFolderPath = picked.Path;
#else
            // MAUI 的 FolderPicker 只在 Windows / macOS 上存在，Android 没有文件夹选择器。
            // 之前这里直接调 FolderPicker.Default，Android 目标因此编译不过。
            await Shell.Current.DisplayAlert("导入 MCP 文件夹",
                "Android 端暂不支持选择文件夹，请改用「导入 zip」。", "好");
            return;
#endif
            IsImporting = true;
            ImportProgress = 0;
            ImportStatus = LocalizationService.Current["Settings_ImportCopy"];
            var progress = new Progress<(int done, int total)>(t =>
                ImportProgress = t.total == 0 ? 0 : (double)t.done / t.total);
            var result = await _mcp.ImportFolder(McpImportFolderPath, _mcp.McpPackDir, progress);
            RefreshMcpServers();
            await Shell.Current.DisplayAlert("MCP 文件夹导入", result, "好");
        }
        catch (Exception ex)
        {
            App.WriteLog("SettingsViewModel.ImportMcpFolder -> " + ex);
        }
        finally
        {
            IsImporting = false;
            ImportProgress = 0;
        }
    }

    /// <summary>后台线程扫描 MCP 数据包目录，再回 UI 线程回填集合（首次进入设置页不再阻塞）。</summary>
    private async Task LoadMcpServersAsync()
    {
        List<Modules.Mcp.McpServerItem> items;
        try
        {
            items = await Task.Run(() => _mcp.ListServers());
        }
        catch (Exception ex)
        {
            App.WriteLog("SettingsViewModel.LoadMcpServers -> " + ex);
            return;
        }

        MainThread.BeginInvokeOnMainThread(() =>
        {
            McpServers.Clear();
            foreach (var item in items) McpServers.Add(item);
            OnPropertyChanged(nameof(HasMcpServers));
            McpServersLoading = false;   // 加载完成，隐藏圈圈、显示列表
        });
    }

    /// <summary>列表是否为空（空态提示用；列表本身改成 BindableLayout，外层页面统一滚动）。</summary>
    public bool HasMcpServers => McpServers.Count > 0;

    /// <summary>刷新 MCP 数据包列表（从 McpPacks 目录扫描）。</summary>
    private void RefreshMcpServers()
    {
        try
        {
            McpServers.Clear();
            foreach (var item in _mcp.ListServers())
                McpServers.Add(item);
            OnPropertyChanged(nameof(HasMcpServers));
        }
        catch (Exception ex)
        {
            App.WriteLog("SettingsViewModel.RefreshMcpServers -> " + ex);
        }
    }

    /// <summary>新建 MCP 服务器：名称 + 简介 + 可选地址。</summary>
    [RelayCommand]
    private async Task NewMcpServer()
    {
        try
        {
            var name = await Shell.Current.DisplayPromptAsync("新建 MCP 服务器", "输入名称（将作为数据包目录名）", "下一步", "取消", "例如: 天气服务");
            if (string.IsNullOrWhiteSpace(name)) return;
            var desc = await Shell.Current.DisplayPromptAsync("新建 MCP 服务器", "简介（说明这个服务器的用途）", "下一步", "取消", "例如: 提供实时天气查询");
            if (string.IsNullOrWhiteSpace(desc)) return;
            var url = await Shell.Current.DisplayPromptAsync("新建 MCP 服务器", "接入地址（可选，留空则仅本地数据包）", "创建", "取消", "https://…");
            var item = _mcp.CreateServer(name, desc, url);
            if (item is null)
            {
                await Shell.Current.DisplayAlert("新建 MCP 服务器", "创建失败（名称可能包含非法字符）", "好");
                return;
            }
            RefreshMcpServers();
            await Shell.Current.DisplayAlert("新建 MCP 服务器", $"已创建: {item.Name}\n{item.Detail}", "好");
        }
        catch (Exception ex)
        {
            App.WriteLog("SettingsViewModel.NewMcpServer -> " + ex);
        }
    }

    /// <summary>查看某条 MCP 服务器的简介与属性详情。</summary>
    [RelayCommand]
    private async Task ShowMcpServerDetails(Modules.Mcp.McpServerItem? item)
    {
        try
        {
            if (item is null) return;
            await Shell.Current.DisplayAlert($"MCP 服务器 · {item.Name}", item.Detail, "好");
        }
        catch (Exception ex)
        {
            App.WriteLog("SettingsViewModel.ShowMcpServerDetails -> " + ex);
        }
    }

    [RelayCommand]
    private async Task GoBack() => await Shell.Current.GoToAsync("..");

    // ============ 工具模式运行时 ============

    /// <summary>检测 Python / Java 运行时，并回填手动路径。</summary>
    [RelayCommand]
    private async Task DetectRuntimes()
    {
        if (!_runtimes.IsDesktop)
        {
            await Shell.Current.DisplayAlert("工具模式", "外部 Java/Python 工具仅桌面端可用（当前设备为移动端）。", "好");
            return;
        }
        IsRuntimeBusy = true;
        RuntimeBusyText = "正在检测…";
        PythonStatus = "检测中…";
        JavaStatus = "检测中…";
        try
        {
            var st = await _runtimes.DetectAsync();
            PythonManualPath = _runtimes.ManualPythonPath;
            JavaManualPath = _runtimes.ManualJavaPath;
            PythonStatus = st.PythonOk ? "✅ " + st.PythonDetail : "❌ " + st.PythonDetail;
            JavaStatus = st.JavaOk ? "✅ " + st.JavaDetail : "❌ " + st.JavaDetail;
            if (string.IsNullOrEmpty(st.WindowsOnlyNote))
                RuntimeBusyText = "检测完成";
        }
        catch (Exception ex)
        {
            App.WriteLog("SettingsViewModel.DetectRuntimes -> " + ex);
            RuntimeBusyText = "检测出错：" + ex.Message;
        }
        finally
        {
            IsRuntimeBusy = false;
        }
    }

    [RelayCommand]
    private async Task SetPythonPath()
    {
        var input = await Shell.Current.DisplayPromptAsync("Python 路径",
            "输入 python.exe 的完整路径，或包含 python.exe 的目录（清空可解除）：", "确定", "取消", _runtimes.ManualPythonPath);
        if (input is null) return;
        _runtimes.SetManualPythonPath(input);
        PythonManualPath = _runtimes.ManualPythonPath;
        await DetectRuntimes();
    }

    [RelayCommand]
    private async Task SetJavaPath()
    {
        var input = await Shell.Current.DisplayPromptAsync("Java 路径",
            "输入 java.exe 的完整路径，或包含 bin/java.exe 的目录（清空可解除）：", "确定", "取消", _runtimes.ManualJavaPath);
        if (input is null) return;
        _runtimes.SetManualJavaPath(input);
        JavaManualPath = _runtimes.ManualJavaPath;
        await DetectRuntimes();
    }

    [RelayCommand]
    private async Task DownloadPython()
    {
        if (IsRuntimeBusy) return;
        IsRuntimeBusy = true;
        RuntimeBusyText = "准备下载 Python 便携版…";
        try
        {
            var progress = new Progress<string>(s =>
                MainThread.BeginInvokeOnMainThread(() => RuntimeBusyText = s));
            var result = await _runtimes.DownloadPythonAsync(progress);
            RuntimeBusyText = result;
            await DetectRuntimes();
        }
        catch (Exception ex)
        {
            App.WriteLog("SettingsViewModel.DownloadPython -> " + ex);
            RuntimeBusyText = "下载失败：" + ex.Message;
        }
        finally
        {
            IsRuntimeBusy = false;
        }
    }

    [RelayCommand]
    private async Task DownloadJava()
    {
        if (IsRuntimeBusy) return;
        IsRuntimeBusy = true;
        RuntimeBusyText = "准备下载 Java 便携版…";
        try
        {
            var progress = new Progress<string>(s =>
                MainThread.BeginInvokeOnMainThread(() => RuntimeBusyText = s));
            var result = await _runtimes.DownloadJavaAsync(progress);
            RuntimeBusyText = result;
            await DetectRuntimes();
        }
        catch (Exception ex)
        {
            App.WriteLog("SettingsViewModel.DownloadJava -> " + ex);
            RuntimeBusyText = "下载失败：" + ex.Message;
        }
        finally
        {
            IsRuntimeBusy = false;
        }
    }

    /// <summary>点击「检测运行时」按钮的包装（XAML 里也行，但保留同步调用的快捷入口）。</summary>
    [RelayCommand]
    private async Task RefreshRuntimes() => await DetectRuntimes();

    // ============ Android 沙箱安装向导 ============

    /// <summary>Android：一键安装 Linux 沙箱 rootfs + Python/Java 解释器（约 80MB）。</summary>
    [RelayCommand]
    private async Task InstallSandboxRuntime()
    {
        if (IsRuntimeBusy) return;
        var installer = _runtimes.SandboxInstaller;
        if (installer is null)
        {
            await Shell.Current.DisplayAlert("沙箱", "当前设备不支持 Linux 沙箱（仅 Android）。", "好");
            return;
        }
        IsRuntimeBusy = true;
        RuntimeBusyText = "正在安装 Linux 沙箱运行时（Python + Java）…";
        try
        {
            var progress = new Progress<string>(s =>
                MainThread.BeginInvokeOnMainThread(() => RuntimeBusyText = s));
            var (ok, note) = await installer.InstallAsync(progress);
            RuntimeBusyText = ok ? "沙箱安装完成" : "部分失败：" + note;
            await DetectRuntimes();
            if (ok)
            {
                // 验证沙箱可用
                var sandbox = _runtimes.SandboxRuntime!;
                var diag = await sandbox.BuildDiagnosticAsync("python3 --version && java -version 2>&1 | head -1");
                if (diag is not null)
                {
                    var (code, out_) = await sandbox.RunGuestCommandAsync(diag.Value.Exe, diag.Value.Args, 45);
                    RuntimeBusyText = "沙箱验证：" + (code == 0 ? "✅" : "❌") + "\n" + out_;
                }
            }
        }
        catch (Exception ex)
        {
            App.WriteLog("SettingsViewModel.InstallSandboxRuntime -> " + ex);
            RuntimeBusyText = "安装失败：" + ex.Message;
        }
        finally
        {
            IsRuntimeBusy = false;
        }
    }

    /// <summary>Android：卸载沙箱（删 rootfs + native 文件，保留已下载的工具包）。</summary>
    [RelayCommand]
    private async Task RemoveSandboxRuntime()
    {
        var installer = _runtimes.SandboxInstaller;
        if (installer is null) return;
        var confirm = await Shell.Current.DisplayAlert(
            "卸载沙箱",
            "将删除已下载的 Python/Java 解释器（约 80MB），工具包本身保留。",
            "删除", "取消");
        if (!confirm) return;
        IsRuntimeBusy = true;
        RuntimeBusyText = "正在卸载沙箱…";
        await Task.Run(() => installer.Remove());
        RuntimeBusyText = "沙箱已删除";
        await DetectRuntimes();
        IsRuntimeBusy = false;
    }

    // ============ 开发者展示模式（114514 连击解锁） ============

    /// <summary>
    /// 翻到设置最底部后，对解锁按钮连续点按 114514 次即解锁（无需输入框，不弹密码框）。
    /// 达成开启成功提示；连击被打断（超过 4 秒未继续）则提示密码是错误并重置。
    /// </summary>
    [RelayCommand]
    private async Task SecretTap()
    {
        if (ShowcaseUnlocked) return;
        if (!AtScrollBottom) return;   // 隐藏入口：只有滚到最底部才计数
        SecretTapCount++;
        RestartSecretTimer();
        if (SecretTapCount >= SecretTapsNeeded)
        {
            lock (SecretGate) { _secretStamp++; }
            ShowcaseUnlocked = true;
            _settings.Apply(_settings.Current with { DeveloperShowcaseUnlocked = true });
            await _settings.Persist();
            SecretTapCount = 0;
            await Shell.Current.DisplayAlert("🔓 开启成功", "开发者展示模式已开启：新增「开发者展示」入口（可编辑 / 播放展示案，含多立绘支持）。", "好");
        }
    }

    private void RestartSecretTimer()
    {
        int stamp;
        lock (SecretGate) stamp = ++_secretStamp;
        _ = CountSecretTimeoutAsync(stamp);
    }

    /// <summary>连击计数超过 4 秒未续按：视为密码输入中断，提示错误并重置。</summary>
    private async Task CountSecretTimeoutAsync(int stamp)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(4));
            if (stamp != _secretStamp) return;
            if (ShowcaseUnlocked || SecretTapCount <= 0) return;
            SecretTapCount = 0;
            await Shell.Current.DisplayAlert("密码是错误", "连击中断，未达到 114514 次，计数已重置。", "好");
        }
        catch (Exception ex)
        {
            App.WriteLog("SettingsViewModel.CountSecretTimeout -> " + ex.Message);
        }
    }

    [RelayCommand]
    private async Task OpenShowcase() => await Shell.Current.GoToAsync("showcase-list");

    /// <summary>打开素材库（背景 / 音乐管理）。</summary>
    [RelayCommand]
    private async Task OpenMaterials() => await Shell.Current.GoToAsync("materials");

    /// <summary>打开爱发电赞助页面。</summary>
    [RelayCommand]
    private async Task OpenSponsor()
    {
        try
        {
            await Launcher.Default.OpenAsync("https://ifdian.net/a/jqyhxkxt1145141026");
        }
        catch (Exception ex)
        {
            App.WriteLog("SettingsViewModel.OpenSponsor -> " + ex);
            await Shell.Current.DisplayAlert("赞助", "无法打开赞助页面：" + ex.Message, "好");
        }
    }

    /// <summary>打开B站作者主页。</summary>
    [RelayCommand]
    private async Task OpenBilibili()
    {
        try
        {
            await Launcher.Default.OpenAsync("https://space.bilibili.com/3546745275419060?spm_id_from=333.1007.0.0");
        }
        catch (Exception ex)
        {
            App.WriteLog("SettingsViewModel.OpenBilibili -> " + ex);
            await Shell.Current.DisplayAlert("B站", "无法打开B站主页：" + ex.Message, "好");
        }
    }
    // ============ 数据包导出 / 导入 ============
    // 注意：这里不要复用 MCP 区域的 ImportStatus / ImportProgress，两者在同一个类里，
    // 重复声明会直接编译失败；分开也避免两块卡片的状态互相串。
    [ObservableProperty] private string _exportStatus = "";
    [ObservableProperty] private double _exportProgress;
    [ObservableProperty] private string _dataImportStatus = "";
    [ObservableProperty] private double _dataImportProgress;
    [ObservableProperty] private bool _isDataBusy;
    /// <summary>导出时是否把 API 密钥 / 机器人密钥一并写入数据包（默认否）。</summary>
    [ObservableProperty] private bool _includeSecrets;

    /// <summary>导出全部应用数据为一个 zip（默认清空 settings.json 里的密钥）。</summary>
    [RelayCommand]
    private async Task ExportData()
    {
        if (IsDataBusy) return;
        IsDataBusy = true;
        string? tmp = null;
        try
        {
            // 设置是 300ms 防抖落盘的：先把当前值刷到磁盘，否则导出的是改动前的旧设置
            await PersistNowAsync();

            var fileName = $"温暖如初_数据包_{DateTime.Now:yyyyMMdd_HHmm}.zip";
            // 先打到临时文件，再交给 FileSaver 让用户选位置（Android 上也只有 FileSaver 能落盘）
            tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                "wab_export_" + Guid.NewGuid().ToString("N") + ".zip");

            ExportStatus = "正在打包…";
            ExportProgress = 0;
            var progress = new Progress<(int Done, int Total, string Path)>(t =>
            {
                ExportProgress = t.Total == 0 ? 0 : (double)t.Done / t.Total;
                ExportStatus = $"正在打包 {t.Done}/{t.Total} 个文件…";
            });

            await DataPackageService.ExportAsync(tmp, IncludeSecrets, progress);
            var size = new FileInfo(tmp).Length;

            ExportStatus = "请选择保存位置…";
            using var fs = File.Open(tmp, FileMode.Open, FileAccess.Read, FileShare.Read);
            var result = await CommunityToolkit.Maui.Storage.FileSaver.Default.SaveAsync(fileName, fs);

            ExportStatus = result.IsSuccessful
                ? $"导出成功（{DataPackageService.FormatSize(size)}）"
                : "已取消保存";
            if (result.IsSuccessful)
            {
                // 同步一份到下载文件夹（安卓自动备份；桌面 no-op）
                Services.DownloadBackupService.AfterExport(tmp);
                await Shell.Current.DisplayAlert("导出成功",
                    $"数据包已保存（{DataPackageService.FormatSize(size)}）。"
                    + (IncludeSecrets
                        ? "包含 API 密钥与机器人密钥，请妥善保管。"
                        : "已排除 API 密钥与机器人密钥。"),
                    "好");
            }
        }
        catch (Exception ex)
        {
            ExportStatus = "导出失败：" + ex.Message;
            App.WriteLog("Settings.ExportData -> " + ex);
        }
        finally
        {
            IsDataBusy = false;
            ExportProgress = 0;
            try { if (tmp is not null && File.Exists(tmp)) File.Delete(tmp); } catch { }
        }
    }

    /// <summary>导入数据包：解压到临时目录校验 → 用户确认 → 替换数据目录 → 提示重启。</summary>
    [RelayCommand]
    private async Task ImportData()
    {
        if (IsDataBusy) return;
        IsDataBusy = true;
        string? tempDir = null;
        string? picked = null;
        try
        {
            var pick = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "选择数据包（.zip）",
                FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    { DevicePlatform.WinUI, new[] { ".zip" } },
                    { DevicePlatform.Android, new[] { "application/zip" } },
                    { DevicePlatform.iOS, new[] { "public.zip-archive" } }
                })
            });
            if (pick is null) return;

            DataImportStatus = "正在读取文件…";
            DataImportProgress = 0;

            // Android 上 FullPath 是 content:// URI，不能直接喂给 ZipFile；先落到本地临时文件
            picked = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                "wab_pick_" + Guid.NewGuid().ToString("N") + ".zip");
            await using (var src = await pick.OpenReadAsync())
            await using (var dst = File.Create(picked))
                await src.CopyToAsync(dst);

            if (!DataPackageService.IsValidPackage(picked))
            {
                DataImportStatus = "导入失败：不是有效的数据包";
                await Shell.Current.DisplayAlert("导入失败",
                    "这个文件不像是「温暖如初」的数据包（缺少 characters.json / settings.json）。", "好");
                return;
            }

            DataImportStatus = "正在解压校验…";
            DataImportProgress = 0;
            var progress = new Progress<(int Done, int Total, string Path)>(t =>
            {
                DataImportProgress = t.Total == 0 ? 0 : (double)t.Done / t.Total;
                DataImportStatus = $"正在解压校验 {t.Done}/{t.Total} 个文件…";
            });
            tempDir = await DataPackageService.UnpackForImportAsync(picked, progress);

            var items = await Task.Run(() => DataPackageService.ListImportTargets(tempDir));
            var summary = items.Count == 0
                ? "（空包）"
                : string.Join("、", items.Take(8).Select(i =>
                    i.Size < 0 ? i.Name : $"{i.Name}（{DataPackageService.FormatSize(i.Size)}）"));
            var ok = await Shell.Current.DisplayActionSheet(
                $"将用数据包内容整体替换当前全部数据（共 {items.Count} 项：{summary}），完成后需要重启应用。",
                "取消", null, "确认导入") == "确认导入";
            if (!ok) { DataImportStatus = "已取消导入"; return; }

            DataImportStatus = "正在写入数据目录…";
            var (success, message) = await Task.Run(() => DataPackageService.ReplaceDataDir(tempDir!));
            DataImportStatus = success ? "导入完成，请重启应用生效。" : "导入失败：" + message;
            await Shell.Current.DisplayAlert(success ? "导入完成" : "导入失败",
                success ? "数据已导入。请完全关闭并重新打开应用，改动才会生效。" : message, "好");
            if (success) App.WriteLog("Settings.ImportData: success");
        }
        catch (Exception ex)
        {
            DataImportStatus = "导入失败：" + ex.Message;
            App.WriteLog("Settings.ImportData -> " + ex);
        }
        finally
        {
            IsDataBusy = false;
            DataImportProgress = 0;
            if (tempDir is not null) DataPackageService.CleanupImport(tempDir);
            try { if (picked is not null && File.Exists(picked)) File.Delete(picked); } catch { }
        }
    }
}
