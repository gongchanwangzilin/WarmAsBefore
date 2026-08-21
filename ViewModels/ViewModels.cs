using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WarmAsBefore.Modules.Mcp;
using WarmAsBefore.Services;
using WarmAsBefore.Models;
using System.Collections.ObjectModel;
using System.IO.Compression;

namespace WarmAsBefore.ViewModels;

public sealed partial class CharacterSelectViewModel : ObservableObject
{
    private readonly GameEngine _engine;
    private readonly CharacterLibrary _library;
    private readonly Modules.SaveSystem.SaveManager _save;
    private readonly StorageProvider _store;

    [ObservableProperty] private ObservableCollection<CharacterCardItem> _cards = new();

    public CharacterSelectViewModel(GameEngine engine, CharacterLibrary library,
        Modules.SaveSystem.SaveManager save, StorageProvider store)
    {
        _engine = engine;
        _library = library;
        _save = save;
        _store = store;
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
}

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsManager _settings;
    private readonly DesignSystem.Theme.ThemeManager _theme;
    private readonly Modules.RealChat.OfficialChatBridge _bridge;
    private readonly Modules.Mcp.McpOrchestrator _mcp;

    public SettingsViewModel(SettingsManager settings, DesignSystem.Theme.ThemeManager theme,
        Modules.RealChat.OfficialChatBridge bridge, Modules.Mcp.McpOrchestrator mcp)
    {
        _settings = settings;
        _theme = theme;
        _bridge = bridge;
        _mcp = mcp;

        var s = settings.Current;
        _bgmLevel = s.BgmLevel;
        _sfxLevel = s.SfxLevel;
        _frostOn = s.FrostEnabled;
        _glassOn = s.GlassEnabled;
        _liquidOn = s.LiquidEnabled;
        _themeName = string.IsNullOrEmpty(s.ThemeName) ? "classic" : s.ThemeName;
        _themeDisplay = DesignSystem.Theme.ThemeManager.ThemeDisplay(_themeName);
        _devMode = s.DeveloperMode;
        _complexPlot = s.ComplexPlot;
        _novelTesting = s.NovelTestingEnabled;
        _showAffection = s.ShowAllAffection;
        _menuRight = s.MenuSide == "right";
        _lang = s.Lang;
        _langDisplay = LangDisplayOf(s.Lang);
        _textSpeed = s.TextSpeed;
        _autoSave = s.AutoSaveEnabled;
        _keySfx = s.KeySfx;

        _aiUrl = s.AiUrl;
        _aiKey = s.AiKey;
        _aiModel = s.AiModel;
        _aiTemperature = s.AiTemperature;
        _aiMaxTokens = s.AiMaxTokens;
        _deepThink = s.DeepThink;
        _deepModel = s.DeepModel;
        _memoryTurns = s.MemoryTurns;

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

        _notificationsEnabled = s.NotificationsEnabled;
        _greetingEnabled = s.GreetingEnabled;

        _weatherCity = s.WeatherCity;
        _cycleLength = s.CycleLength;
        _periodLength = s.PeriodLength;

        _alwaysOnTop = s.AlwaysOnTop;
        _petIdleMinutes = s.PetIdleMinutes;

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
        _wechatEnabled = s.WechatEnabled;
        _wechatAppId = s.WechatAppId;
        _wechatAppSecret = s.WechatAppSecret;
        _wechatToken = s.WechatToken;
        _wechatPort = s.WechatPort.ToString();
        _statusText = _bridge.Status;

        // MCP 设置
        _mcpEnabled = s.McpEnabled;
        _mcpAutoApprove = s.McpAutoApprove;
        _mcp.NetworkUrl = s.McpNetworkUrl;
        _mcp.AutoApprove = _mcpAutoApprove;
        _mcpNetworkUrl = s.McpNetworkUrl;
        _mcpGitHubRepo = s.McpGitHubRepo;
        _mcpImportZipPath = s.McpImportZipPath;
        _mcpImportFolderPath = s.McpImportFolderPath;
        RefreshMcpServers();

        _bridge.StatusChanged += st =>
            MainThread.BeginInvokeOnMainThread(() => StatusText = st);
    }

    [ObservableProperty] private double _bgmLevel = 0.7;
    [ObservableProperty] private double _sfxLevel = 0.8;
    [ObservableProperty] private bool _glassOn;
    [ObservableProperty] private bool _frostOn;
    [ObservableProperty] private bool _liquidOn;
    [ObservableProperty] private string _themeName = "classic";
    [ObservableProperty] private string _themeDisplay = "经典";

    public List<string> ThemeChoices { get; } = new() { "经典", "樱花粉", "翠竹绿", "晨雾蓝灰" };

    /// <summary>毛玻璃是磨砂的高级版：必须开启磨砂后才能开启毛玻璃。</summary>
    public bool CanGlass => FrostOn;
    [ObservableProperty] private bool _devMode;
    [ObservableProperty] private bool _complexPlot;
    [ObservableProperty] private bool _novelTesting;
    [ObservableProperty] private bool _showAffection;
    [ObservableProperty] private bool _menuRight;
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

    public List<string> VoiceEngineChoices { get; } = new() { "system", "api" };

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
    }

    [ObservableProperty] private bool _alwaysOnTop;
    [ObservableProperty] private int _petIdleMinutes;

    [ObservableProperty] private bool _qqBotEnabled;
    [ObservableProperty] private string _qqAppId = "";
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

    public List<string> Langs { get; } = new() { "简体中文", "繁體中文", "English", "日本語" };
    public List<string> KeySfxChoices { get; } = new() { "default", "soft", "typewriter", "none" };
    public List<int> MemoryTurnsChoices { get; } = new() { 3, 5, 8, 10, 12, 15, 20 };
    public List<int> CycleChoices { get; } = Enumerable.Range(21, 15).ToList();
    public List<int> PeriodChoices { get; } = Enumerable.Range(3, 5).ToList();
    /// <summary>桌宠闲置时长可选值（分钟）：0=关闭闲置自动桌宠。</summary>
    public List<int> PetIdleChoices { get; } = new() { 0, 1, 3, 5, 10, 15, 30, 60 };

    partial void OnBgmLevelChanged(double value) => PersistSettings();
    partial void OnSfxLevelChanged(double value) => PersistSettings();
    partial void OnGlassOnChanged(bool value)
    {
        if (value && !FrostOn) FrostOn = true;
        _theme.Glass = value;
        PersistSettings();
    }
    partial void OnFrostOnChanged(bool value)
    {
        if (!value) GlassOn = false;
        _theme.Frost = value;
        OnPropertyChanged(nameof(CanGlass));
        PersistSettings();
    }
    partial void OnLiquidOnChanged(bool value) { _theme.Liquid = value; PersistSettings(); }
    partial void OnThemeDisplayChanged(string value) { ThemeName = ThemeKeyOf(value); _theme.ThemeName = ThemeName; PersistSettings(); }
    partial void OnDevModeChanged(bool value) => PersistSettings();
    partial void OnComplexPlotChanged(bool value) => PersistSettings();
    partial void OnNovelTestingChanged(bool value) => PersistSettings();
    partial void OnShowAffectionChanged(bool value) => PersistSettings();
    partial void OnMenuRightChanged(bool value) => PersistSettings();
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

    /// <summary>朗读或识别选了 API 引擎时，显示 API 语音配置区。</summary>
    public bool NeedsVoiceApi => TtsEngine == "api" || SttEngine == "api";

    partial void OnNotificationsEnabledChanged(bool value) => PersistSettings();
    partial void OnGreetingEnabledChanged(bool value) => PersistSettings();

    partial void OnWeatherCityChanged(string value) => PersistSettings();
    partial void OnCycleLengthChanged(int value) => PersistSettings();
    partial void OnPeriodLengthChanged(int value) => PersistSettings();

    partial void OnAlwaysOnTopChanged(bool value) => PersistSettings();
    partial void OnPetIdleMinutesChanged(int value) => PersistSettings();

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

    private async void PersistSettings()
    {
        try
        {
            var port = int.TryParse(WechatPort, out var p) && p > 0 && p < 65536 ? p : 8012;
            var s = new UserSettings
            {
                BgmLevel = BgmLevel,
                SfxLevel = SfxLevel,
                DeveloperMode = DevMode,
                ComplexPlot = ComplexPlot,
                NovelTestingEnabled = NovelTesting,
                ShowAllAffection = ShowAffection,
                FrostEnabled = FrostOn,
                GlassEnabled = GlassOn,
                LiquidEnabled = LiquidOn,
                ThemeName = ThemeKeyOf(ThemeDisplay),
                MenuSide = MenuRight ? "right" : "left",
                Lang = Lang,
                TextSpeed = TextSpeed,
                AutoSaveEnabled = AutoSave,
                KeySfx = KeySfx,

                AiUrl = string.IsNullOrWhiteSpace(AiUrl) ? "https://api.openai.com/v1/chat/completions" : AiUrl.Trim(),
                AiKey = AiKey.Trim(),
                AiModel = string.IsNullOrWhiteSpace(AiModel) ? "gpt-4o" : AiModel.Trim(),
                AiTemperature = AiTemperature,
                AiMaxTokens = Math.Clamp((int)AiMaxTokens, 100, 8000),
                DeepThink = DeepThink,
                DeepModel = DeepModel.Trim(),
                MemoryTurns = MemoryTurns,

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

                NotificationsEnabled = NotificationsEnabled,
                GreetingEnabled = GreetingEnabled,

                WeatherCity = WeatherCity.Trim(),
                CycleLength = CycleLength,
                PeriodLength = PeriodLength,

                AlwaysOnTop = AlwaysOnTop,
                PetIdleMinutes = Math.Max(0, PetIdleMinutes),

                QqBotEnabled = QqBotEnabled,
                QqAppId = QqAppId.Trim(),
                QqAppSecret = QqAppSecret.Trim(),
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
                 ChessApiModel = string.IsNullOrWhiteSpace(ChessApiModel) ? "gpt-4o-mini" : ChessApiModel.Trim()
             };
            _settings.Apply(s);
            await _settings.Persist();
        }
        catch (Exception ex)
        {
            App.WriteLog("SettingsViewModel.PersistSettings EX -> " + ex);
        }
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
            var folder = await FolderPicker.Default.PickAsync();
            if (folder?.Folder is null) return;
            McpImportFolderPath = folder.Folder.Path;
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

    /// <summary>刷新 MCP 数据包列表（从 McpPacks 目录扫描）。</summary>
    private void RefreshMcpServers()
    {
        try
        {
            McpServers.Clear();
            foreach (var item in _mcp.ListServers())
                McpServers.Add(item);
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
}
