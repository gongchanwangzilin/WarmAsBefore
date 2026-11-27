using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WarmAsBefore.Controls;
using WarmAsBefore.Modules.Mcp;
using WarmAsBefore.Services;
using WarmAsBefore.Models;
using WarmAsBefore.Models.Background;
using BgMode = WarmAsBefore.Models.Background.BackgroundMode; // 本类已有同名 string 属性 BackgroundMode，枚举须用别名区分
using WarmAsBefore.Services.Background;
using WarmAsBefore.Modules.Screen;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO.Compression;
using System.Windows.Input;

namespace WarmAsBefore.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsManager _settings;
    private readonly DesignSystem.Theme.ThemeManager _theme;
    private readonly Modules.RealChat.OfficialChatBridge _bridge;
    private readonly Modules.Mcp.McpOrchestrator _mcp;
    private readonly PluginHost _plugins;
    private readonly Modules.Tools.RuntimeManager _runtimes;
    private readonly Modules.Tools.ToolManager _tools;
    private readonly SpeechService _speech;
    private readonly Services.StickerLibrary _stickers;
    private readonly CharacterLibrary _library;
    private readonly Services.VisionService _vision;
    private readonly Services.SkillLibrary _skills;
    private readonly Services.CostEstimatorService _cost;
    private readonly Modules.RealWorld.LocationService _cities;
    private readonly Modules.RealWorld.WeatherProvider _weather;
    private readonly Services.CloudSync.CloudSyncService _cloud;
    private readonly Modules.Market.ShopService _shop;
    private readonly SaveRuntimeService _runtime;
    private readonly Modules.DataPack.SmartImporter _smart;
    private readonly BackgroundService _bg;
    private readonly TileService _tiles;

    public SettingsViewModel(SettingsManager settings, DesignSystem.Theme.ThemeManager theme,
        Modules.RealChat.OfficialChatBridge bridge, Modules.Mcp.McpOrchestrator mcp,
        PluginHost plugins,
        Modules.Tools.RuntimeManager runtimes, Modules.Sandbox.SandboxPolicy sandbox,
        Modules.Tools.ToolManager tools, SpeechService speech,
        Services.StickerLibrary stickers, CharacterLibrary library, Services.VisionService vision,
        Services.SkillLibrary skills, Services.CostEstimatorService cost,
        Modules.RealWorld.LocationService cities, Modules.RealWorld.WeatherProvider weather,
        Services.CloudSync.CloudSyncService cloud, Services.GenderLibrary genders,
        Modules.Market.ShopService shop, SaveRuntimeService runtime,
        Modules.DataPack.SmartImporter smart, BackgroundService bg, TileService tiles)
    {
        _settings = settings;
        _theme = theme;
        _bridge = bridge;
        _mcp = mcp;
        _plugins = plugins;
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
        _cities = cities;
        _weather = weather;
        _cloud = cloud;
        _genders = genders;
        _shop = shop;
        _runtime = runtime;
        _smart = smart;
        _bg = bg;
        _tiles = tiles;
        _ = shop.InitializeAsync();
        _cloudStatus = cloud.Status;
        _showcaseUnlocked = settings.Current.DeveloperShowcaseUnlocked;
        PythonManualPath = _runtimes.ManualPythonPath;
        JavaManualPath = _runtimes.ManualJavaPath;

        var s = settings.Current;
        _bgmLevel = s.BgmLevel;
        _sfxLevel = s.SfxLevel;
        _uiSoundOn = s.UiSoundEnabled;
        _uiSoundLevel = Math.Clamp(s.UiSoundLevel, 0, 1);
        _frostOn = s.FrostEnabled;
        _glassOn = s.GlassEnabled;
        _liquidOn = s.LiquidEnabled;
        // 世界已收敛为唯一档「档案室」（2.7）：旧存档里不管存过哪个风格键，落图都是同一档。
        _selectedWorld = WarmAsBefore.DesignSystem.Worlds.StyleWorld.Endfield;
        _theme.World = _selectedWorld;
        _glassTranslucency = s.GlassTranslucency;
        _glassReducedTransparency = s.GlassReducedTransparency;
        _glassFrost = s.GlassFrost;
        _glassHeight = s.GlassHeight;
        _backgroundMode = string.IsNullOrEmpty(s.BackgroundMode) ? "none" : s.BackgroundMode;
        _backgroundModeDisplay = BackgroundModeDisplayOf(_backgroundMode);
        _backgroundImagePath = s.BackgroundImagePath ?? "";
        _backgroundSolidHex = NormalizeSolidHex(s.BackgroundSolidHex);
        SolidSwatches = SolidPalette
            .Select(t => new SolidSwatch(t.Hex, t.Name) { PickCommand = PickSolidColorCommand })
            .ToArray();
        SyncSolidSwatches();
        _mapTileOnline = s.MapTileOnline;
        _mapTileMbTilesPath = s.MapTileMbTilesPath;
        RefreshTileCacheText();
        RefreshMapTileStatus();
        _experimentalSceneFxEnabled = s.ExperimentalSceneFxEnabled;
        _weatherSceneOverlayEnabled = s.WeatherSceneOverlayEnabled;
        _sceneLightEnabled = s.SceneLightEnabled;
        _weatherSceneAudioEnabled = s.WeatherSceneAudioEnabled;
        _weatherSceneAudioLevel = s.WeatherSceneAudioLevel;
        _glassLightX = s.GlassLightX;
        _glassLightY = s.GlassLightY;
        _glassLightZ = s.GlassLightZ;
        _glassLightWidth = s.GlassLightWidth;
        // 色板轴锁定终末地：旧存档里存过 classic/sakura/bamboo/mist 的人，打开也是终末地。
        // 材质（StyleWorld）是另一条轴，不在这里动 —— 它仍由「风格」那一步决定。
        _themeName = DesignSystem.Theme.ThemeManager.LockedPalette;
        _theme.ThemeName = _themeName;
        _themeDisplay = DesignSystem.Theme.ThemeManager.ThemeDisplay(_themeName);
        _complexPlot = s.ComplexPlot;
        _novelTesting = s.NovelTestingEnabled;
        _showAffection = s.ShowAllAffection;
        _affectionLevelUp = s.AffectionLevelUpEnabled;
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
        _phoneStandaloneWindow = s.PhoneStandaloneWindow;
        _mainWindowCloseBehavior = string.IsNullOrWhiteSpace(s.MainWindowCloseBehavior) ? "ask" : s.MainWindowCloseBehavior;
        _mainWindowCloseBehaviorDisplay = MainCloseBehaviorDisplayOf(_mainWindowCloseBehavior);
        _narratorAiEnabled = s.NarratorAiEnabled;
        _userGender = s.UserGender ?? "";
        _userGenderDisplay = string.IsNullOrWhiteSpace(_userGender) ? "未设置" : _userGender;
        _userBirthday = CharacterAgeService.NormalizeBirthday(s.UserBirthday);
        _userBirthdayDisplay = _userBirthday.Length == 0 ? "未设置" : CharacterAgeService.BirthdayDisplay(_userBirthday);
        _lowMemoryMode = s.LowMemoryMode;
        // 性能模式与超省内存分开：前者管观感（动效/阴影），后者管内存与磁盘读取，
        // 弱机上两者常常一起开，但语义不同，不能合成一个开关。
        _performanceMode = s.PerformanceMode;
        _coreMemoryEnabled = s.CoreMemoryEnabled;
        _coreMemoryThreshold = s.CoreMemoryThreshold;
        _traumaHealingEnabled = s.TraumaHealingEnabled;
        _chainSearchLimit = s.ChainSearchLimit;
        _mentalHealthEnabled = s.MentalHealthEnabled;
        _psychologyBehaviorEnabled = s.PsychologyBehaviorEnabled;
        _physiologyEnabled = s.PhysiologyEnabled;
        _innerLifeEnabled = s.InnerLifeEnabled;
        _concretizationEnabled = s.ConcretizationEnabled;
        _innerDriveEnabled = s.InnerDriveEnabled;
        _showToolCalls = s.ShowToolCalls;
        _chatFullScreen = s.ChatFullScreen;
        _brainEnabled = s.BrainEnabled;
        _brainExpectationEnabled = s.BrainExpectationEnabled;
        _brainSurpriseEnabled = s.BrainSurpriseEnabled;
        _brainAiAssistEnabled = s.BrainAiAssistEnabled;
        _brainAiUrl = s.BrainAiUrl;
        _brainAiKey = s.BrainAiKey;
        _brainAiModel = s.BrainAiModel;
        _brainAiEveryTurns = s.BrainAiEveryTurns;
        _statAiControl = s.StatAiControl;
        _moneyEnabled = s.MoneyEnabled;
        _imageGenEnabled = s.ImageGenEnabled;
        _imageGenUrl = s.ImageGenUrl;
        _imageGenKey = s.ImageGenKey;
        _imageGenModel = s.ImageGenModel;
        _imageGenSize = s.ImageGenSize;
        _saveBundleWithPlugins = s.SaveBundleWithPlugins;
        _pluginHooksEnabled = s.PluginHooksEnabled;
        _pluginCrashLogs = s.PluginCrashLogs;
        _ = LoadGenderChoicesAsync();
        _lang = s.Lang;
        _langDisplay = LangDisplayOf(s.Lang);
        _textSpeed = s.TextSpeed;
        _autoSave = s.AutoSaveEnabled;
        _keySfx = s.KeySfx;

        _aiUrl = s.AiUrl;
        _aiKey = s.AiKey;
        _aiModel = s.AiModel;
        _smallModelFirst = s.SmallModelFirst;
        _smallAiUrl = s.SmallAiUrl;
        _smallAiKey = s.SmallAiKey;
        _smallAiModel = s.SmallAiModel;
        _customParamsJson = s.CustomParamsJson;
        _customParamsFormat = s.CustomParamsFormat;
        _thinkBarEnabled = s.ThinkBarEnabled;
        _quickButtonsEnabled = s.QuickButtonsEnabled;
        _thinkTierCount = Math.Clamp(s.ThinkTierCount, 1, 5);
        _thinkTierLabels = string.IsNullOrWhiteSpace(s.ThinkTierLabels) ? "轻度,标准,深度" : s.ThinkTierLabels;
        _activeThinkTier = Math.Clamp(s.ActiveThinkTier, 0, Math.Clamp(s.ThinkTierCount, 1, 5));
        _thinkParamKey = string.IsNullOrWhiteSpace(s.ThinkParamKey) ? "reasoning_effort" : s.ThinkParamKey;
        _thinkParamValues = string.IsNullOrWhiteSpace(s.ThinkParamValues) ? "low,medium,high" : s.ThinkParamValues;
        _aiPreset = PresetDisplayOf(s.AiPreset);
        _visionUrl = s.VisionUrl;
        _visionKey = s.VisionKey;
        _visionModel = s.VisionModel;
        _embeddingEnabled = s.EmbeddingEnabled;
        _embeddingUrl = s.EmbeddingUrl;
        _embeddingKey = s.EmbeddingKey;
        _embeddingModel = s.EmbeddingModel;
        _embeddingDimensions = EmbeddingDimensionChoices.Contains(s.EmbeddingDimensions) ? s.EmbeddingDimensions : 0;
        foreach (var p in AiPresets) AiPresetChoices.Add(p.Display);
        _aiTemperature = s.AiTemperature;
        _aiMaxTokens = s.AiMaxTokens;
        _deepThink = s.DeepThink;
        _deepModel = s.DeepModel;
        _aiAutonomousThink = s.AiAutonomousThink;
        _aiPlanFlowEnabled = s.AiPlanFlowEnabled;
        _planMaxSteps = Math.Clamp(s.PlanMaxSteps, 1, 32);
        _settingsAdvisorEnabled = s.SettingsAdvisorEnabled;
        _settingsAdvisorFrequency = string.IsNullOrWhiteSpace(s.SettingsAdvisorFrequency) ? "never" : s.SettingsAdvisorFrequency;
        _memoryTurns = s.MemoryTurns;
        _aiTimeoutSeconds = s.AiTimeoutSeconds > 0 ? s.AiTimeoutSeconds : 25;
        _memoryWindowDays = MemoryWindowChoices.Contains(s.MemoryWindowDays) ? s.MemoryWindowDays : HumanMemoryMath.DefaultWindowDays;
        SyncWindowDisplay();
        _memoryRecallRandomness = Math.Clamp(s.MemoryRecallRandomness, 0, 1);
        _subProfileAiRewrite = s.SubProfileAiRewrite;
        _semanticAiAssist = s.SemanticAiAssist;
        _crossSaveMemory = s.CrossSaveMemory;
        _smartSavingEnabled = s.SmartSavingEnabled;
        _lightMode = s.LightMode;
        _preferDiscreteGpu = s.PreferDiscreteGpu;
        _gpuStatusText = Services.GpuPreferenceService.DescribeState();

        // 多端点轮换 + 上下文 Token 上限
        _aiRotationEnabled = s.AiRotationEnabled;
        _contextTokenLimit = Math.Clamp(s.ContextTokenLimit > 0 ? s.ContextTokenLimit : 32000, 4000, 256000);
        _contextTokenLimitDisplay = ContextLimitDisplayOf(_contextTokenLimit);
        AiProviders.Clear();
        ReloadAiProviders();

        _ttsEnabled = s.TtsEnabled;
        _ttsRate = s.TtsRate;
        _sttEnabled = s.SttEnabled;
        _ttsEngine = string.IsNullOrWhiteSpace(s.TtsEngine) ? "system" : s.TtsEngine;
        _sttEngine = string.IsNullOrWhiteSpace(s.SttEngine) ? "system" : s.SttEngine;
        _advancedAnalysis = s.AdvancedAnalysis;
        // 摄像头总开关以独立配置为权威（FaceAnalysisService 也会写它），设置只做镜像。
        try { _cameraFaceEnabled = new FaceAnalysisService().CameraEnabled || s.CameraFaceEnabled; } catch { _cameraFaceEnabled = s.CameraFaceEnabled; }
        _audioInputMode = string.IsNullOrWhiteSpace(s.AudioInputMode) ? "internal" : s.AudioInputMode;
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

        // 天气：直接写后备字段，避开属性变更钩子（构造期一触发就把旧设置又写回盘）
        _weatherCity = s.WeatherCity;
        _weatherLatitude = s.WeatherLatitude;
        _weatherLongitude = s.WeatherLongitude;
        _weatherSource = Modules.RealWorld.WeatherSources.Normalize(s.WeatherSource);
        _weatherSourceChoice = ChoiceOf(_weatherSource);
        _isWeatherSourceCustom = _weatherSource == Modules.RealWorld.WeatherSources.Custom;
        _weatherApiUrl = s.WeatherApiUrl ?? "";
        _weatherApiKey = s.WeatherApiKey ?? "";
        LoadCycleRules(s);

        _alwaysOnTop = s.AlwaysOnTop;
        _petIdleMinutes = s.PetIdleMinutes;
        _petThinkModeDisplay = ThinkModeDisplayOf(s.PetThinkMode);
        _petThinkIntervalMinutes = Math.Max(1, s.PetThinkIntervalMinutes);
        _petChromeCrossScreen = s.PetChromeCrossScreen;
        // 直接写字段：走属性会把「读盘」当成「用户改动」再落一次盘。
        // 但百分比那个读数挂在 PetScale 的通知上，字段赋值不会触发，所以补一次通知。
        _petScale = Math.Clamp(s.PetScale, 0.4, 2.6);
        OnPropertyChanged(nameof(PetScaleText));

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
        _wechatBoundCharacterId = s.WechatBoundCharacterId;
        _channelsUrl = s.ChannelsUrl ?? "";
        _cloudSaveEnabled = s.CloudSaveEnabled;
        _cloudRepo = s.CloudRepo ?? "";
        _cloudToken = s.CloudToken ?? "";
        _cloudBranch = s.CloudBranch ?? "";
        _cloudDeviceName = s.CloudDeviceName ?? "";
        _cloudAutoUpload = s.CloudAutoUpload;
        _cloudReconcileMemory = s.CloudReconcileMemory;
        _statusText = _bridge.Status;
        _betaChannel = s.BetaChannel;
        LoadExtraOptions(s);

        // MCP 设置
        _mcpEnabled = s.McpEnabled;
        _mcpAutoApprove = s.McpAutoApprove;
        _mcp.AutoApprove = _mcpAutoApprove;
        // 这里不能同步 RefreshMcpServers()：McpOrchestrator.ListServers() 会递归
        // Directory.GetFiles(dir, "*", SearchOption.AllDirectories) 并对每个文件取
        // FileInfo.Length —— 全部跑在 UI 线程上。设置页是 transient，每次点「设置」
        // 都会重新构造 ViewModel，于是每次导航都同步卡一次（「点设置卡死」的真实阻塞点）。
        _ = LoadMcpServersAsync();
        // 插件与 MCP 同级：两边都是「往机器里装东西」，列表一起在「外部接入」里出现。
        _ = LoadPluginsAsync();
        _ = LoadQqBoundChoicesAsync();
        _suppressPresetApply = false;

        _bridge.StatusChanged += st =>
            MainThread.BeginInvokeOnMainThread(() => StatusText = st);
    }

    [ObservableProperty] private double _bgmLevel = 0.7;
    [ObservableProperty] private double _sfxLevel = 0.8;
    /// <summary>界面小音效：与素材音效分开的开关与音量，默认 0.5。</summary>
    [ObservableProperty] private bool _uiSoundOn = true;
    [ObservableProperty] private double _uiSoundLevel = 0.5;
    // 风格世界：三个互相牵制的玻璃开关合并为一个选择。
    // 世界已收敛为唯一档「档案室」（2.7）：风格下拉与五档选项表退役，只剩玻璃项微调。
    [ObservableProperty] private WarmAsBefore.DesignSystem.Worlds.StyleWorld _selectedWorld
        = WarmAsBefore.DesignSystem.Worlds.StyleWorld.Endfield;

    partial void OnSelectedWorldChanged(WarmAsBefore.DesignSystem.Worlds.StyleWorld value)
    {
        _theme.World = value;
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
    /// <summary>地图底图的联网总闸（地图页图层抽屉里也有同一个开关，两处写的是同一项设置）。</summary>
    [ObservableProperty] private bool _mapTileOnline = true;

    partial void OnMapTileOnlineChanged(bool value) => PersistSettings();

    // ---- MBTiles 离线包 ----

    /// <summary>当前离线包路径（设置里存的那份）。</summary>
    [ObservableProperty] private string _mapTileMbTilesPath = "";

    /// <summary>给设置页看的一句话：选了哪个包、包是什么结构、档位范围。</summary>
    [ObservableProperty] private string _mbTilesStatusText = "";

    /// <summary>导入 / 复制大文件时置位，按钮跟着禁用。</summary>
    [ObservableProperty] private bool _mbTilesBusy;

    public bool HasMbTiles => !string.IsNullOrWhiteSpace(MapTileMbTilesPath);

    /// <summary>进设置页 / 换包后刷新那一行状态（没选、文件不在、读不出都要说清楚）。</summary>
    internal void RefreshMapTileStatus()
    {
        MbTilesStatusText = _tiles.OfflinePackage.Message;
        OnPropertyChanged(nameof(HasMbTiles));
    }

    /// <summary>选一个 .mbtiles：复制进应用的 Offline 目录，打开成功后把路径写进设置。</summary>
    [RelayCommand]
    private async Task PickMbTilesAsync()
    {
        if (MbTilesBusy) return;
        try
        {
            var pick = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "选择 MBTiles 离线包",
                FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    { DevicePlatform.WinUI, new[] { ".mbtiles" } },
                    { DevicePlatform.Android, new[] { "application/octet-stream", "application/vnd.sqlite3", "application/x-sqlite3", "*/*" } }
                })
            });
            if (pick is null) return;

            MbTilesBusy = true;
            MbTilesStatusText = "正在导入离线包（大文件要复制一会儿）…";
            var status = await _tiles.ImportMbTilesAsync(pick.FullPath);
            if (status.State is MbTilesState.Ready)
            {
                MapTileMbTilesPath = _tiles.MbTilesPath;
                _settings.Apply(_settings.Current with { MapTileMbTilesPath = MapTileMbTilesPath });
                await _settings.Persist();
            }
            RefreshMapTileStatus();
            if (status.IsReady) Toast.Info("离线包已就绪。");
            else Toast.Error(status.Message);
        }
        catch (Exception ex)
        {
            App.WriteLog("SettingsViewModel.PickMbTiles -> " + ex);
            MbTilesStatusText = "选离线包失败：" + ex.Message;
        }
        finally
        {
            MbTilesBusy = false;
        }
    }

    /// <summary>取消离线包：断开连接、清掉解码缓存，设置里的路径也一起清空。</summary>
    [RelayCommand]
    private async Task ClearMbTilesAsync()
    {
        try
        {
            await _tiles.ConfigureMbTilesAsync("");
            MapTileMbTilesPath = "";
            _settings.Apply(_settings.Current with { MapTileMbTilesPath = "" });
            await _settings.Persist();
            RefreshMapTileStatus();
            Toast.Info("已取消离线包。");
        }
        catch (Exception ex)
        {
            App.WriteLog("SettingsViewModel.ClearMbTiles -> " + ex);
            Toast.Error("取消离线包没成功：" + ex.Message);
        }
    }

    /// <summary>瓦片缓存现在占多少 —— 给「清空瓦片缓存」那一行一句交代。</summary>
    [ObservableProperty] private string _tileCacheText = "";

    [RelayCommand]
    private async Task ClearTileCacheAsync()
    {
        try
        {
            await _tiles.ClearAsync();
            RefreshTileCacheText();
            Toast.Info("瓦片缓存已清空。");
        }
        catch (Exception ex)
        {
            App.WriteLog("SettingsViewModel.ClearTileCache -> " + ex);
            Toast.Error("清缓存没成功：" + ex.Message);
        }
    }

    private void RefreshTileCacheText()
    {
        var mb = _tiles.DiskBytes / 1024.0 / 1024.0;
        TileCacheText = mb <= 0
            ? "本地还没有缓存瓦片。"
            : $"本地缓存约 {mb:0.#}MB。清空后离线包要看的地方得重新联网走一遍。";
    }

    [ObservableProperty] private bool _experimentalSceneFxEnabled;
    [ObservableProperty] private bool _weatherSceneOverlayEnabled = true;
    /// <summary>天气场景光照开关，关闭后主界面即时关掉光柱/折射。</summary>
    [ObservableProperty] private bool _sceneLightEnabled = true;
    [ObservableProperty] private bool _weatherSceneAudioEnabled = true;
    [ObservableProperty] private double _weatherSceneAudioLevel = 0.6;
    public List<string> BackgroundModeChoices { get; } = new() { "纯色（默认）", "自定义图片", "窗口透视（看穿到桌面）" };
    public bool BackgroundImageVisible => BackgroundMode == "image";

    /// <summary>纯色档自选底色的可见性：图片档与透视档背后不是这块地面，色板在那儿没有意义。</summary>
    public bool BackgroundSolidVisible => BackgroundMode is not ("image" or "clear");

    /// <summary>
    /// 纯色档的十颗底色：四颗中性、两颗冷、两颗暖、两颗深。
    /// 它们不是装饰色 —— 纯色档的糊底就是从这颗色合成的，所以换色等于换整间屋子的光。
    /// 想要色板外的颜色走「选个颜色…」进调色页；这十颗只是「不用想也知道不会难看」的起点。
    ///
    /// 命令挂在数据项自己身上（<see cref="SolidSwatch.PickCommand"/>），模板里不再写
    /// RelativeSource 祖先查找：卡片是 BindableLayout 现场建的模板，那一层祖先查找是
    /// 「点了没反应」可能的两个断点之一（另一个是 Border 上的 TapGestureRecognizer），
    /// 都换掉之后点击只剩「数据项自己的命令」这一条路。
    /// </summary>
    public IReadOnlyList<SolidSwatch> SolidSwatches { get; }

    /// <summary>十颗底色的原始表；命令在构造时统一挂上（init-only，只能在建立时给）。</summary>
    private static readonly (string Hex, string Name)[] SolidPalette =
    {
        ("#FDF8F0", "奶油纸"), ("#E8E6E1", "素灰"), ("#DDE6F0", "晨雾蓝"), ("#CBDCD8", "青瓷"),
        ("#F3E3CF", "暖砂"), ("#F6E1E6", "樱雾"), ("#3A3D42", "石墨"), ("#2C3A4E", "幽蓝"),
        ("#4A3527", "赭石"), ("#22322A", "墨绿"),
    };
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
        OnPropertyChanged(nameof(BackgroundSolidVisible));
        PersistSettings();
    }

    partial void OnBackgroundImagePathChanged(string value)
    {
        OnPropertyChanged(nameof(BackgroundImageLabel));
        PersistSettings();
    }

    /// <summary>选中的底色（空串 = 跟主题走）。改它就换整层背景，并且落盘。</summary>
    [ObservableProperty] private string _backgroundSolidHex = "";
    /// <summary>色板那行的回执：换色成功后说清现在是什么色，跟主题时也明说。</summary>
    [ObservableProperty] private string _backgroundSolidHint = "";

    partial void OnBackgroundSolidHexChanged(string value)
    {
        var hex = NormalizeSolidHex(value);
        if (!string.Equals(hex, value, StringComparison.Ordinal))
        {
            // 规范化会再进来一次，这里直接写字段断掉递归。
            SetProperty(ref _backgroundSolidHex, hex, nameof(BackgroundSolidHex));
            return;
        }
        SyncSolidSwatches();
        ApplySolidNow();
        PersistSettings();
    }

    /// <summary>点色板：立即生效（不等 300ms 的落盘防抖），再落盘。</summary>
    [RelayCommand]
    private void PickSolidColor(SolidSwatch? swatch)
    {
        if (swatch is null) return;
        BackgroundSolidHex = swatch.Hex;
        BackgroundSolidHint = $"背景：{swatch.Name} {swatch.Hex}";
    }

    /// <summary>开调色页。颜色走 <see cref="ColorPickerHandoff"/> 回传，不走 Shell 查询串（转义容易出错）。</summary>
    [RelayCommand]
    private async Task OpenColorPicker() => await Shell.Current.GoToAsync("colorpicker");

    /// <summary>
    /// 调色页回来：把结果接到与色板同一条链路上（当场换底 + 刷新选中态 + 落盘）。
    /// 空串 = 用户在那儿回到了「跟主题走」，也照办。
    /// </summary>
    public void ApplyPickedSolid(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) { FollowTheme(); return; }
        var norm = NormalizeSolidHex(hex);
        if (norm.Length == 0) return;
        BackgroundSolidHex = norm;
        BackgroundSolidHint = $"背景：{norm}";
    }

    /// <summary>回到跟主题走（PageBg）。</summary>
    [RelayCommand]
    private void FollowTheme()
    {
        BackgroundSolidHex = "";
        BackgroundSolidHint = "背景跟着主题走";
    }

    /// <summary>把选中的那颗色标出来。色板只有十颗，直接扫一遍比维护一份索引更省事。</summary>
    private void SyncSolidSwatches()
    {
        foreach (var sw in SolidSwatches)
            sw.Selected = string.Equals(sw.Hex, BackgroundSolidHex, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 当场换背景：薄纱同步换色，窗口层那块糊底在后台按新色重合成（key 里含色值）。
    /// 走 <see cref="AppBackgroundService"/> 而不是等设置下发 —— 用户点一下要看到那一下的变化。
    /// </summary>
    private void ApplySolidNow()
    {
        try
        {
            AppBackgroundService.SetSolidTint(Color.TryParse(BackgroundSolidHex, out var c) ? c : null);
        }
        catch (Exception ex) { App.WriteLog("Settings.ApplySolidNow -> " + ex.Message); }
    }

    /// <summary>规范成空串（跟主题走）或 #RRGGBB / #AARRGGBB；认不出来的当没选过。</summary>
    private static string NormalizeSolidHex(string? hex)
    {
        var raw = (hex ?? "").Trim();
        if (raw.Length == 0) return "";
        return Color.TryParse(raw.StartsWith('#') ? raw : "#" + raw, out var c) ? HexOf(c) : "";
    }

    private static string HexOf(Color c)
    {
        var r = (int)Math.Round(c.Red * 255);
        var g = (int)Math.Round(c.Green * 255);
        var b = (int)Math.Round(c.Blue * 255);
        var a = (int)Math.Round(c.Alpha * 255);
        return a >= 255 ? $"#{r:X2}{g:X2}{b:X2}" : $"#{a:X2}{r:X2}{g:X2}{b:X2}";
    }

    partial void OnExperimentalSceneFxEnabledChanged(bool value) { PersistSettings(); PushBackgroundConfig(); }
    partial void OnWeatherSceneOverlayEnabledChanged(bool value) { PersistSettings(); PushBackgroundConfig(); }
    partial void OnSceneLightEnabledChanged(bool value) { PersistSettings(); PushBackgroundConfig(); }
    partial void OnWeatherSceneAudioEnabledChanged(bool value) { PersistSettings(); PushBackgroundConfig(); }
    partial void OnWeatherSceneAudioLevelChanged(double value) { PersistSettings(); PushBackgroundConfig(); }

    /// <summary>把当前背景相关设置合成一份统一配置，下发到单例背景服务即时生效。</summary>
    private void PushBackgroundConfig()
    {
        var cfg = BackgroundService.ConfigFromSettings(
            BackgroundMode, BackgroundImagePath, DesignSystem.Theme.ThemeManager.LockedPalette,
            null, WeatherSceneAudioEnabled, WeatherSceneAudioLevel, SceneLightEnabled, ExperimentalSceneFxEnabled);
        // 天气叠加开启 → 强制天气模式；关闭 → 若用户选了图片则用图片，否则纯色
        var mode = WeatherSceneOverlayEnabled
            ? BgMode.Weather
            : (BackgroundMode == "image" ? BgMode.Image : BgMode.Solid);
        // 测试版总开关关闭：设置页自己算出的天气模式也要降级为纯色，与服务工厂保持一致。
        if (!ExperimentalSceneFxEnabled && mode == BgMode.Weather) mode = BgMode.Solid;
        cfg = cfg with
        {
            Mode = mode,
            PaletteId = DesignSystem.Theme.ThemeManager.LockedPalette,
            // 天气场景由天气数据自动推导（BackgroundService.ApplyWeatherDescription），
            // 这里只沿用当前生效的场景，不再用设置页的手动覆盖把它打回某个固定值。
            Scene = _bg.Current.Scene,
            ExperimentalEnabled = ExperimentalSceneFxEnabled
        };
        _bg.Apply(cfg);
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
    /// <summary>色板轴（锁定的终末地）：ThemeName 仍是持久化字段，旧存档读进来也会被改写成它。</summary>
    [ObservableProperty] private string _themeName = DesignSystem.Theme.ThemeManager.LockedPalette;
    [ObservableProperty] private string _themeDisplay = "终末地";

    /// <summary>
    /// 调色板只剩终末地一档：UI 仍然保留原来那条下拉（长条）的形状，只是选项唯一——
    /// 用户要的是「调色板锁终末地」，不是「把这一行删掉换成一行字」。
    /// </summary>
    public IReadOnlyList<string> PaletteChoices { get; } = new[] { "终末地" };

    public string PaletteDisplay => DesignSystem.Theme.ThemeManager.ThemeDisplay(DesignSystem.Theme.ThemeManager.LockedPalette);

    // 工具沙箱
    [ObservableProperty] private ObservableCollection<Modules.Sandbox.TrustEntry> _trustedTools = new();
    [ObservableProperty] private string _sandboxHint = "";

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
        const string cancel = "取消";
        var dirs = Directory.Exists(Modules.Tools.ToolManager.ToolsDir)
            ? Directory.GetDirectories(Modules.Tools.ToolManager.ToolsDir).Select(System.IO.Path.GetFileName).ToArray()
            : Array.Empty<string>();
        if (dirs.Length == 0)
        {
            Toast.Info("工具目录里还没有任何工具，没什么可信任的。");
            return;
        }
        var dir = await Microsoft.Maui.Controls.Shell.Current.DisplayActionSheet(
            "选择要信任的工具目录", cancel, null, dirs);
        // DisplayActionSheet 点「取消」返回的是这个按钮的文字本身，不是 null。
        // 少这一句判断，「取消」就会被当成目录名写进信任名单：一个并不存在的目录拿到了读模型数据的权限。
        if (string.IsNullOrWhiteSpace(dir) || dir == cancel) return;
        var full = System.IO.Path.Combine(Modules.Tools.ToolManager.ToolsDir, dir);
        // 信任名单是给真实目录放行的，路径不存在的一律不记；这也是上面那句判断的兜底。
        if (!Directory.Exists(full))
        {
            App.WriteLog($"Sandbox.Trust: 拒绝「{dir}」，目录不存在：{full}");
            Toast.Error($"找不到目录：{dir}");
            return;
        }
        _sandbox.Trust.Add(dir, full, "设置页手动放开");
        _sandbox.RefreshKeyFingerprints();
        App.WriteLog($"Sandbox.Trust: 已信任 {full}");
        RefreshSandboxTrust();
    }

    [RelayCommand]
    private void RemoveTrustedTool(Modules.Sandbox.TrustEntry tool)
    {
        _sandbox.Trust.Remove(tool.ToolName, tool.ToolPath);
        App.WriteLog($"Sandbox.Trust: 已移除 {tool.ToolPath}");
        RefreshSandboxTrust();
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

    // ============ 窗口 / 旁白 / 性别 / 低内存 / 记忆 / 心理 / 身体 / 金钱 / 画面 / 插件 ============

    /// <summary>手机页开成独立窗口（关掉则仍跳转应用内页面）。</summary>
    [ObservableProperty] private bool _phoneStandaloneWindow = true;
    /// <summary>关主窗口时：ask 每次问 / closeOthers 连其他窗口一起关 / keepOthers 只关主窗口。</summary>
    [ObservableProperty] private string _mainWindowCloseBehavior = "ask";
    [ObservableProperty] private string _mainWindowCloseBehaviorDisplay = "每次询问";
    /// <summary>旁白 AI：回复里允许出现简短的第三人称环境/动作描写。</summary>
    [ObservableProperty] private bool _narratorAiEnabled;
    /// <summary>用户（你）的性别；空 = 未设置。称呼与相处方式会参考它，但不刻板化。</summary>
    [ObservableProperty] private string _userGender = "";
    [ObservableProperty] private string _userGenderDisplay = "未设置";
    /// <summary>用户自己的生日（"MM-dd"）。接受 M-d / yyyy-MM-dd / M月d日 输入，落盘前归一。</summary>
    [ObservableProperty] private string _userBirthday = "";
    [ObservableProperty] private string _userBirthdayDisplay = "";
    [ObservableProperty] private List<string> _genderChoices = new();
    /// <summary>超省内存：记忆/关系网读后就丢缓存，闲置会话清空，后台预热不做。</summary>
    [ObservableProperty] private bool _lowMemoryMode;
    /// <summary>性能模式（设置→实验卡片）：减少动效 + 卡片阴影改平，只影响观感。</summary>
    [ObservableProperty] private bool _performanceMode;
    [ObservableProperty] private bool _coreMemoryEnabled = true;
    [ObservableProperty] private double _coreMemoryThreshold = 0.82;
    [ObservableProperty] private bool _traumaHealingEnabled = true;
    [ObservableProperty] private double _chainSearchLimit = 5;
    [ObservableProperty] private bool _mentalHealthEnabled = true;
    [ObservableProperty] private bool _psychologyBehaviorEnabled = true;
    [ObservableProperty] private bool _physiologyEnabled = true;
    [ObservableProperty] private bool _innerLifeEnabled = true;
    [ObservableProperty] private bool _concretizationEnabled = true;
    [ObservableProperty] private bool _innerDriveEnabled = true;
    /// <summary>F22：对话里显示工具调用气泡（可展开）。</summary>
    [ObservableProperty] private bool _showToolCalls;
    /// <summary>F22：对话信息全屏。</summary>
    [ObservableProperty] private bool _chatFullScreen;
    // 大脑预判：期待差 + 意料之外两层，默认本地跑；可选配一枚免费小模型每 N 轮补一次预判
    [ObservableProperty] private bool _brainEnabled = true;
    [ObservableProperty] private bool _brainExpectationEnabled = true;
    [ObservableProperty] private bool _brainSurpriseEnabled = true;
    [ObservableProperty] private bool _brainAiAssistEnabled;
    [ObservableProperty] private string _brainAiUrl = "";
    [ObservableProperty] private string _brainAiKey = "";
    [ObservableProperty] private string _brainAiModel = "";
    [ObservableProperty] private int _brainAiEveryTurns = 8;
    [ObservableProperty] private bool _statAiControl = true;
    [ObservableProperty] private bool _moneyEnabled = true;
    [ObservableProperty] private bool _imageGenEnabled;
    [ObservableProperty] private string _imageGenUrl = "https://api.openai.com/v1";
    [ObservableProperty] private string _imageGenKey = "";
    [ObservableProperty] private string _imageGenModel = "gpt-image-1";
    [ObservableProperty] private string _imageGenSize = "1024x1024";
    [ObservableProperty] private bool _saveBundleWithPlugins = true;
    [ObservableProperty] private bool _pluginHooksEnabled = true;
    [ObservableProperty] private bool _pluginCrashLogs = true;
    [ObservableProperty] private string _genderNewName = "";
    [ObservableProperty] private string _genderNewDescription = "";
    [ObservableProperty] private string _genderNewPronoun = "TA";
    [ObservableProperty] private string _genderNewScript = "";
    [ObservableProperty] private string _genderNewScriptLanguage = "js";
    private readonly Services.GenderLibrary? _genders;

    /// <summary>关窗行为的中文档（与 <see cref="MainWindowCloseBehavior"/> 一一对应）。</summary>
    public IReadOnlyList<string> WindowCloseChoices { get; } = new[] { "每次询问", "连其他窗口一起关", "只关主窗口" };
    public IReadOnlyList<string> GenderScriptLanguageChoices { get; } = new[] { "js", "python", "java" };
    /// <summary>自定义性别（内置的不列在这里，删也删不掉内置的）。</summary>
    [ObservableProperty] private List<string> _customGenderNames = new();
    [ObservableProperty] private string _selectedCustomGender = "";
    [ObservableProperty] private string _walletText = "";
    [ObservableProperty] private string _moneyInput = "100";

    private static string MainCloseBehaviorDisplayOf(string? key) => key switch
    {
        "closeOthers" => "连其他窗口一起关",
        "keepOthers" => "只关主窗口",
        _ => "每次询问"
    };

    private async Task LoadGenderChoicesAsync()
    {
        try
        {
            if (_genders is null) return;
            var all = await _genders.AllAsync();
            var names = all.Select(g => g.Name).Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
            var customs = all.Where(g => !g.Builtin).Select(g => g.Name).Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
            MainThread.BeginInvokeOnMainThread(() =>
            {
                GenderChoices = names;
                CustomGenderNames = customs;
                if (!customs.Contains(SelectedCustomGender))
                    SelectedCustomGender = customs.FirstOrDefault() ?? "";
                WalletText = _shop.WalletLabel;
            });
        }
        catch (Exception ex) { App.WriteLog("SettingsViewModel.Genders -> " + ex.Message); }
    }

    [ObservableProperty] private string _lang = "zh-CN";
    [ObservableProperty] private string _langDisplay = "简体中文";
    [ObservableProperty] private double _textSpeed = 1.0;
    [ObservableProperty] private bool _autoSave = true;
    [ObservableProperty] private string _keySfx = "default";

    [ObservableProperty] private string _aiUrl = "https://api.openai.com/v1/chat/completions";
    [ObservableProperty] private string _aiKey = "";
    [ObservableProperty] private string _aiModel = "gpt-4o";

    /// <summary>小模型优先：所有对话先交小模型，处理不来再升级大模型（仅 Minecraft 桥未连接时生效）。</summary>
    [ObservableProperty] private bool _smallModelFirst;
    partial void OnSmallModelFirstChanged(bool value) => PersistSettings();
    [ObservableProperty] private string _smallAiUrl = "";
    partial void OnSmallAiUrlChanged(string value) => PersistSettings();
    [ObservableProperty] private string _smallAiKey = "";
    partial void OnSmallAiKeyChanged(string value) => PersistSettings();
    [ObservableProperty] private string _smallAiModel = "";
    partial void OnSmallAiModelChanged(string value) => PersistSettings();
    [ObservableProperty] private double _aiTemperature = 0.8;
    [ObservableProperty] private double _aiMaxTokens = 500;
    [ObservableProperty] private bool _deepThink;
    [ObservableProperty] private string _deepModel = "";

    // F21：AI 自主深度思考 / 多步
    [ObservableProperty] private bool _aiAutonomousThink;
    partial void OnAiAutonomousThinkChanged(bool value) => PersistSettings();
    [ObservableProperty] private bool _aiPlanFlowEnabled;
    partial void OnAiPlanFlowEnabledChanged(bool value) => PersistSettings();
    [ObservableProperty] private int _planMaxSteps = 8;
    partial void OnPlanMaxStepsChanged(int value) => PersistSettings();

    // F08：设置调节 AI（与角色隔离，仅临时用；默认「永不」打扰）
    [ObservableProperty] private bool _settingsAdvisorEnabled;
    partial void OnSettingsAdvisorEnabledChanged(bool value) => PersistSettings();
    [ObservableProperty] private string _settingsAdvisorFrequency = "never";
    partial void OnSettingsAdvisorFrequencyChanged(string value) => PersistSettings();
    /// <summary>F08：设置建议 AI 的运行频率选项（never/daily/3days/weekly）。</summary>
    public ObservableCollection<string> SettingsAdvisorFrequencyChoices { get; } = new() { "never", "daily", "3days", "weekly" };
    /// <summary>F08：最近一次设置建议结果（供设置页展示）。</summary>
    [ObservableProperty] private string _settingsAdvisorResult = "";
    /// <summary>F08：设置建议 AI 是否正在运行（UI 可绑定禁用按钮）。</summary>
    [ObservableProperty] private bool _settingsAdvisorIsBusy;

    [ObservableProperty] private string _customParamsJson = "";
    partial void OnCustomParamsJsonChanged(string value) => PersistSettings();
    [ObservableProperty] private string _customParamsFormat = "openai_flat";
    partial void OnCustomParamsFormatChanged(string value) => PersistSettings();
    /// <summary>第十二项：自定义参数的四格式选项。</summary>
    public ObservableCollection<string> CustomParamsFormatChoices { get; } = new()
    {
        "openai_flat", "nested_by_role", "vendor_ext", "widget_binding"
    };

    // 第十四项：思考档位（思考条 + 快速按钮）
    /// <summary>是否显示「正在思考 / 深度思考中」进度条。</summary>
    [ObservableProperty] private bool _thinkBarEnabled;
    partial void OnThinkBarEnabledChanged(bool value) => PersistSettings();
    /// <summary>是否在输入条上方显示切换思考档位的快速按钮组。</summary>
    [ObservableProperty] private bool _quickButtonsEnabled;
    partial void OnQuickButtonsEnabledChanged(bool value) => PersistSettings();
    /// <summary>思考档位数（不含关闭档），钳到 1~5。</summary>
    [ObservableProperty] private int _thinkTierCount = 3;
    partial void OnThinkTierCountChanged(int value)
    {
        _thinkTierCount = Math.Clamp(value, 1, 5);
        PersistSettings();
    }
    /// <summary>每档标签（逗号分隔，不含关闭档）。</summary>
    [ObservableProperty] private string _thinkTierLabels = "轻度,标准,深度";
    partial void OnThinkTierLabelsChanged(string value) => PersistSettings();
    /// <summary>当前生效的思考档位（0=关闭，1..ThinkTierCount）。</summary>
    [ObservableProperty] private int _activeThinkTier;
    partial void OnActiveThinkTierChanged(int value) => PersistSettings();
    /// <summary>注入请求体的推理强度参数键名。</summary>
    [ObservableProperty] private string _thinkParamKey = "reasoning_effort";
    partial void OnThinkParamKeyChanged(string value) => PersistSettings();
    /// <summary>每档对应的参数值（逗号分隔，共 ThinkTierCount 个）。</summary>
    [ObservableProperty] private string _thinkParamValues = "low,medium,high";
    partial void OnThinkParamValuesChanged(string value) => PersistSettings();
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

    /// <summary>是否让模型辅助建立关系网（默认关闭：默认只靠共现统计）。</summary>
    [ObservableProperty] private bool _semanticAiAssist;
    partial void OnSemanticAiAssistChanged(bool value) => PersistSettings();

    /// <summary>是否让同一个角色跨存档共用一份记忆（默认关闭 = 每个存档各记各的）。</summary>
    [ObservableProperty] private bool _crossSaveMemory;
    partial void OnCrossSaveMemoryChanged(bool value) => PersistSettings();

    /// <summary>API 可用的模型列表（由 FetchModelsAsync 填充）。</summary>
    public ObservableCollection<string> AiModelChoices { get; } = new();
    [ObservableProperty] private bool _isLoadingModels;
    [ObservableProperty] private string _modelListStatus = "";

    [ObservableProperty] private bool _ttsEnabled = true;
    [ObservableProperty] private double _ttsRate = 1.0;
    [ObservableProperty] private bool _sttEnabled = true;
    [ObservableProperty] private string _ttsEngine = "system";
    [ObservableProperty] private string _sttEngine = "system";
    // v1.60：高级分析（大模型深度解读本地情绪/年龄结果）+ 音频直送大模型开关。
    [ObservableProperty] private bool _advancedAnalysis;
    [ObservableProperty] private bool _cameraFaceEnabled;
    [ObservableProperty] private string _audioInputMode = "internal";
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
    /// <summary>音频输入模式：internal=内部 STT 转文字，direct=直送支持听音频的大模型。</summary>
    public List<string> AudioInputModeChoices { get; } = new() { "internal", "direct" };
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
        Toast.Success($"已锁定为「{display}」。");
    }

    public List<string> Langs { get; } = new() { "简体中文", "繁體中文", "English", "日本語" };
    public List<string> KeySfxChoices { get; } = new() { "default", "soft", "typewriter", "none" };
    public List<int> MemoryTurnsChoices { get; } = new() { 3, 5, 8, 10, 12, 15, 20 };

    /// <summary>
    /// 遗忘档位（天）：0 = 不限（能想起最早的记忆）。0.25 / 0.5 是低值档，
    /// 用于演示「TA 转头就忘」。
    /// </summary>
    public List<double> MemoryWindowChoices { get; } = new() { 0.25, 0.5, 1, 3, 7, 14, 30, 90, 0 };

    /// <summary>档位可读名：Picker 直接绑小数会显示成「0.25」，看不出那是 6 小时。</summary>
    public List<string> MemoryWindowLabels { get; } = new()
        { "6 小时", "12 小时", "1 天", "3 天", "1 周", "2 周", "1 个月", "3 个月", "不限" };

    /// <summary>设置页显示的档位名，与 <see cref="MemoryWindowDays"/> 互转。</summary>
    [ObservableProperty] private string _memoryWindowDisplay = "3 天";

    /// <summary>当前档位下的大白话说明（半衰期 + 永久成分），随档位重算。</summary>
    [ObservableProperty] private string _memoryWindowExplain = "";

    partial void OnMemoryWindowDisplayChanged(string value)
    {
        var idx = MemoryWindowLabels.IndexOf(value);
        if (idx >= 0 && Math.Abs(MemoryWindowDays - MemoryWindowChoices[idx]) > 1e-9)
            MemoryWindowDays = MemoryWindowChoices[idx];
    }
    /// <summary>桌宠闲置时长可选值（分钟）：0=关闭闲置自动桌宠。</summary>
    public List<int> PetIdleChoices { get; } = new() { 0, 1, 3, 5, 10, 15, 30, 60 };

    partial void OnBgmLevelChanged(double value) => PersistSettings();
    partial void OnSfxLevelChanged(double value) => PersistSettings();
    partial void OnUiSoundOnChanged(bool value) => PersistSettings();
    partial void OnUiSoundLevelChanged(double value)
    {
        PersistSettings();
        // 拖音量时当场试听一记轻击：调音量的人要立刻听到自己刚设的那一档。
        if (UiSoundOn) Services.UiSounds.Preview(Services.UiSoundKind.Tap, value);
    }
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
    partial void OnComplexPlotChanged(bool value) => PersistSettings();
    partial void OnNovelTestingChanged(bool value) => PersistSettings();
    partial void OnShowAffectionChanged(bool value) => PersistSettings();
    partial void OnAffectionLevelUpChanged(bool value) => PersistSettings();
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

    partial void OnPhoneStandaloneWindowChanged(bool value) => PersistSettings();
    partial void OnMainWindowCloseBehaviorChanged(string value)
    {
        MainWindowCloseBehaviorDisplay = MainCloseBehaviorDisplayOf(value);
        PersistSettings();
    }
    partial void OnNarratorAiEnabledChanged(bool value) => PersistSettings();
    partial void OnUserGenderChanged(string value)
    {
        UserGenderDisplay = string.IsNullOrWhiteSpace(value) ? "未设置" : value;
        PersistSettings();
    }
    /// <summary>生日边输入边回显：归一留给落盘，输入过程中不替换用户正在打的字。</summary>
    partial void OnUserBirthdayChanged(string value)
    {
        var normalized = CharacterAgeService.NormalizeBirthday(value);
        UserBirthdayDisplay = normalized.Length == 0 ? "未设置" : CharacterAgeService.BirthdayDisplay(normalized);
        PersistSettings();
    }
    partial void OnLowMemoryModeChanged(bool value) => PersistSettings();
    partial void OnPerformanceModeChanged(bool value) => PersistSettings();
    partial void OnCoreMemoryEnabledChanged(bool value) => PersistSettings();
    partial void OnCoreMemoryThresholdChanged(double value) => PersistSettings();
    partial void OnTraumaHealingEnabledChanged(bool value) => PersistSettings();
    partial void OnChainSearchLimitChanged(double value) => PersistSettings();
    partial void OnMentalHealthEnabledChanged(bool value) => PersistSettings();
    partial void OnPsychologyBehaviorEnabledChanged(bool value) => PersistSettings();
    partial void OnPhysiologyEnabledChanged(bool value) => PersistSettings();
    partial void OnInnerLifeEnabledChanged(bool value) => PersistSettings();
    partial void OnConcretizationEnabledChanged(bool value) => PersistSettings();
    partial void OnInnerDriveEnabledChanged(bool value) => PersistSettings();
    partial void OnShowToolCallsChanged(bool value) => PersistSettings();
    partial void OnChatFullScreenChanged(bool value) => PersistSettings();
    partial void OnBrainEnabledChanged(bool value) => PersistSettings();
    partial void OnBrainExpectationEnabledChanged(bool value) => PersistSettings();
    partial void OnBrainSurpriseEnabledChanged(bool value) => PersistSettings();
    partial void OnBrainAiAssistEnabledChanged(bool value) => PersistSettings();
    partial void OnBrainAiUrlChanged(string value) => PersistSettings();
    partial void OnBrainAiKeyChanged(string value) => PersistSettings();
    partial void OnBrainAiModelChanged(string value) => PersistSettings();
    partial void OnBrainAiEveryTurnsChanged(int value) => PersistSettings();
    partial void OnStatAiControlChanged(bool value) => PersistSettings();
    partial void OnImageGenEnabledChanged(bool value) => PersistSettings();
    partial void OnImageGenUrlChanged(string value) => PersistSettings();
    partial void OnImageGenKeyChanged(string value) => PersistSettings();
    partial void OnImageGenModelChanged(string value) => PersistSettings();
    partial void OnImageGenSizeChanged(string value) => PersistSettings();
    partial void OnSaveBundleWithPluginsChanged(bool value) => PersistSettings();
    partial void OnPluginHooksEnabledChanged(bool value) => PersistSettings();
    partial void OnPluginCrashLogsChanged(bool value) => PersistSettings();

    /// <summary>Picker 选的是中文，存的是稳定键（下次启动还能读回来）。</summary>
    partial void OnMainWindowCloseBehaviorDisplayChanged(string value)
    {
        var key = value switch
        {
            "连其他窗口一起关" => "closeOthers",
            "只关主窗口" => "keepOthers",
            _ => "ask"
        };
        if (key != MainWindowCloseBehavior) MainWindowCloseBehavior = key;
    }

    partial void OnMoneyEnabledChanged(bool value)
    {
        WalletText = _shop.WalletLabel;
        PersistSettings();
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
    partial void OnMemoryWindowDaysChanged(double value)
    {
        SyncWindowDisplay();
        PersistSettings();
    }

    /// <summary>档位数字 → Picker 上的可读名 + 一句半衰期说明（载入时也走这里，避免显示成 0.25）。</summary>
    private void SyncWindowDisplay()
    {
        var idx = MemoryWindowChoices.IndexOf(MemoryWindowDays);
        var label = idx >= 0 ? MemoryWindowLabels[idx] : WindowText(MemoryWindowDays);
        if (_memoryWindowDisplay != label) _memoryWindowDisplay = label;   // 直写后备字段：不再绕回 Display 的 changed
        MemoryWindowExplain = BuildWindowExplain(MemoryWindowDays);
    }

    /// <summary>把当前档位翻译成人话：普通事多久剩一半、重要的事最后停在哪儿。</summary>
    private static string BuildWindowExplain(double days)
    {
        if (days <= 0) return "不限窗口：记忆不随时间淡掉，只按重要性与被想起的次数排序。";
        var half = HumanMemoryMath.HalfLifeDays(days, 0.5);
        var keep = HumanMemoryMath.Asymptote(1.0);
        return $"普通事（重要性 0.5）约 {WindowText(half)}后只剩一半清晰，久了就沉底；"
             + $"记得很牢的事（重要性 1）最后会停在 {keep:P0} 左右，不会彻底忘掉。";
    }
    partial void OnMemoryRecallRandomnessChanged(double value) => PersistSettings();

    partial void OnTtsEnabledChanged(bool value) => PersistSettings();
    partial void OnTtsRateChanged(double value) => PersistSettings();
    partial void OnSttEnabledChanged(bool value) => PersistSettings();
    partial void OnTtsEngineChanged(string value) { OnPropertyChanged(nameof(NeedsVoiceApi)); PersistSettings(); }
    partial void OnSttEngineChanged(string value) { OnPropertyChanged(nameof(NeedsVoiceApi)); PersistSettings(); }
    /// <summary>摄像头人脸识别总开关：同步到独立配置供 FaceAnalysisService 与开场询问读取。</summary>
    partial void OnCameraFaceEnabledChanged(bool value)
    {
        try { new FaceAnalysisService().CameraEnabled = value; } catch (Exception ex) { App.WriteLog("Settings.CameraFaceEnabled -> " + ex.Message); }
        PersistSettings();
    }
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

    partial void OnWeatherCityChanged(string value)
    {
        PersistSettings();
        OnPropertyChanged(nameof(WeatherCityDisplay));
    }

    partial void OnAlwaysOnTopChanged(bool value) => PersistSettings();
    partial void OnPetIdleMinutesChanged(int value) => PersistSettings();

    partial void OnPetChromeCrossScreenChanged(bool value)
    {
        PersistSettings();
#if WINDOWS
        // 正在当桌宠时拨这个开关，头顶小窗立刻按新边界重排——不然要等下次鼠标靠近才见效，
        // 用户没法确认自己刚才那一下有没有生效。
        PetService.RepositionChrome();
#endif
    }

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
    partial void OnChannelsUrlChanged(string value) => PersistSettings();

    partial void OnMcpEnabledChanged(bool value) { _mcp.Enabled = value; PersistSettings(); }
    partial void OnMcpAutoApproveChanged(bool value) { _mcp.AutoApprove = value; PersistSettings(); }

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
    // 连续操作会叠加成数秒卡死。这里只在停手后跑一次。
    private System.Threading.Timer? _persistTimer;
    private readonly object _persistLock = new();

    private void PersistSettings()
    {
        lock (_persistLock)
        {
            _persistTimer ??= new System.Threading.Timer(
                _ => Helpers.Ui.Post(PersistNow), null,
                Timeout.Infinite, Timeout.Infinite);
            _persistTimer.Change(300, Timeout.Infinite);
        }
    }

    private async void PersistNow()
    {
        try
        {
            using var _scope = Services.PerfLog.Measure("保存设置（序列化 + 下发）");
            try
            {
                var port = int.TryParse(WechatPort, out var p) && p > 0 && p < 65536 ? p : 8012;
                // 必须以「当前设置」为基底：用 new UserSettings{...} 重建整份时，
                // 未列进初始化器的字段（免责声明确认标记、开发者模式解锁等）
                // 会被默认值冲掉，免责声明就会每次启动都弹。
                var s = _settings.Current with
                {
                    BgmLevel = BgmLevel,
                    SfxLevel = SfxLevel,
                    UiSoundEnabled = UiSoundOn,
                    UiSoundLevel = Math.Clamp(UiSoundLevel, 0, 1),
                    ComplexPlot = ComplexPlot,
                    NovelTestingEnabled = NovelTesting,
                    ShowAllAffection = ShowAffection,
                    AffectionLevelUpEnabled = AffectionLevelUp,
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
                    PhoneStandaloneWindow = PhoneStandaloneWindow,
                    MainWindowCloseBehavior = MainWindowCloseBehavior,
                    NarratorAiEnabled = NarratorAiEnabled,
                    UserGender = (UserGender ?? "").Trim(),
                    UserBirthday = CharacterAgeService.NormalizeBirthday(UserBirthday),
                    LowMemoryMode = LowMemoryMode,
                    PerformanceMode = PerformanceMode,
                    CoreMemoryEnabled = CoreMemoryEnabled,
                    CoreMemoryThreshold = Math.Clamp(CoreMemoryThreshold, 0.5, 0.99),
                    TraumaHealingEnabled = TraumaHealingEnabled,
                    ChainSearchLimit = Math.Clamp((int)ChainSearchLimit, 1, 30),
                    MentalHealthEnabled = MentalHealthEnabled,
                    PsychologyBehaviorEnabled = PsychologyBehaviorEnabled,
                    PhysiologyEnabled = PhysiologyEnabled,
                    InnerLifeEnabled = InnerLifeEnabled,
                    ConcretizationEnabled = ConcretizationEnabled,
                    InnerDriveEnabled = InnerDriveEnabled,
                    ShowToolCalls = ShowToolCalls,
                    ChatFullScreen = ChatFullScreen,
                    BrainEnabled = BrainEnabled,
                    BrainExpectationEnabled = BrainExpectationEnabled,
                    BrainSurpriseEnabled = BrainSurpriseEnabled,
                    BrainAiAssistEnabled = BrainAiAssistEnabled,
                    BrainAiUrl = (BrainAiUrl ?? "").Trim(),
                    BrainAiKey = (BrainAiKey ?? "").Trim(),
                    BrainAiModel = (BrainAiModel ?? "").Trim(),
                    BrainAiEveryTurns = Math.Clamp(BrainAiEveryTurns, 2, 999),
                    StatAiControl = StatAiControl,
                    MoneyEnabled = MoneyEnabled,
                    ImageGenEnabled = ImageGenEnabled,
                    ImageGenUrl = (ImageGenUrl ?? "").Trim(),
                    ImageGenKey = (ImageGenKey ?? "").Trim(),
                    ImageGenModel = (ImageGenModel ?? "").Trim(),
                    ImageGenSize = string.IsNullOrWhiteSpace(ImageGenSize) ? "1024x1024" : ImageGenSize.Trim(),
                    SaveBundleWithPlugins = SaveBundleWithPlugins,
                    PluginHooksEnabled = PluginHooksEnabled,
                    PluginCrashLogs = PluginCrashLogs,
                    FrostEnabled = FrostOn,
                    GlassEnabled = GlassOn,
                    LiquidEnabled = LiquidOn,
                    StyleWorldName = WarmAsBefore.DesignSystem.Worlds.StyleWorldTokens.NameOf(SelectedWorld),
                    GlassTranslucency = GlassTranslucency,
                    GlassReducedTransparency = GlassReducedTransparency,
    GlassFrost = GlassFrost,
    GlassHeight = GlassHeight,
    BackgroundMode = BackgroundMode,
    BackgroundImagePath = BackgroundImagePath,
    BackgroundSolidHex = BackgroundSolidHex,
    BackgroundPaletteId = DesignSystem.Theme.ThemeManager.LockedPalette,
    MapTileOnline = MapTileOnline,
                    MapTileMbTilesPath = MapTileMbTilesPath,
    WeatherSceneOverlayEnabled = WeatherSceneOverlayEnabled,
                    ExperimentalSceneFxEnabled = ExperimentalSceneFxEnabled,
    SceneLightEnabled = SceneLightEnabled,
    WeatherSceneAudioEnabled = WeatherSceneAudioEnabled,
    WeatherSceneAudioLevel = WeatherSceneAudioLevel,
    GlassLightX = GlassLightX,
    GlassLightY = GlassLightY,
    GlassLightZ = GlassLightZ,
    GlassLightWidth = GlassLightWidth,
                    ThemeName = DesignSystem.Theme.ThemeManager.LockedPalette,
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
                    CustomParamsJson = (CustomParamsJson ?? "").Trim(),
                    CustomParamsFormat = CustomParamsFormatChoices.Contains(CustomParamsFormat) ? CustomParamsFormat : "openai_flat",
                    ThinkBarEnabled = ThinkBarEnabled,
                    QuickButtonsEnabled = QuickButtonsEnabled,
                    ThinkTierCount = Math.Clamp(ThinkTierCount, 1, 5),
                    ThinkTierLabels = (ThinkTierLabels ?? "").Trim(),
                    ActiveThinkTier = Math.Clamp(ActiveThinkTier, 0, Math.Clamp(ThinkTierCount, 1, 5)),
                    ThinkParamKey = (ThinkParamKey ?? "").Trim(),
                    ThinkParamValues = (ThinkParamValues ?? "").Trim(),
                    AiPreset = PresetKeyOf(_aiPreset),
                    VisionUrl = VisionUrl.Trim(),
                    VisionKey = VisionKey.Trim(),
                    VisionModel = VisionModel.Trim(),
                    EmbeddingEnabled = EmbeddingEnabled,
                    EmbeddingUrl = EmbeddingUrl.Trim(),
                    EmbeddingKey = EmbeddingKey.Trim(),
                    EmbeddingModel = EmbeddingModel.Trim(),
                    EmbeddingDimensions = EmbeddingDimensionChoices.Contains(EmbeddingDimensions) ? EmbeddingDimensions : 0,
                    AiTemperature = AiTemperature,
                    AiMaxTokens = Math.Clamp((int)AiMaxTokens, 100, 8000),
                    DeepThink = DeepThink,
                    DeepModel = DeepModel.Trim(),
                    AiAutonomousThink = AiAutonomousThink,
                    AiPlanFlowEnabled = AiPlanFlowEnabled,
                    PlanMaxSteps = Math.Clamp(PlanMaxSteps, 1, 32),
                    SettingsAdvisorEnabled = SettingsAdvisorEnabled,
                    SettingsAdvisorFrequency = SettingsAdvisorFrequencyChoices.Contains(SettingsAdvisorFrequency) ? SettingsAdvisorFrequency : "never",
                    MemoryTurns = MemoryTurns,
                    MemoryWindowDays = MemoryWindowDays,
                    MemoryRecallRandomness = Math.Clamp(MemoryRecallRandomness, 0, 1),
                    SubProfileAiRewrite = SubProfileAiRewrite,
                    SemanticAiAssist = SemanticAiAssist,
                    CrossSaveMemory = CrossSaveMemory,
                    SmallModelFirst = SmallModelFirst,
                    SmallAiUrl = SmallAiUrl.Trim(),
                    SmallAiKey = SmallAiKey.Trim(),
                    SmallAiModel = SmallAiModel.Trim(),
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
                    AdvancedAnalysis = AdvancedAnalysis,
                    CameraFaceEnabled = CameraFaceEnabled,
                    AudioInputMode = string.IsNullOrWhiteSpace(AudioInputMode) ? "internal" : AudioInputMode,
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
                    WeatherLatitude = WeatherLatitude,
                    WeatherLongitude = WeatherLongitude,
                    WeatherSource = WeatherSource,
                    WeatherApiUrl = WeatherApiUrl.Trim(),
                    WeatherApiKey = WeatherApiKey.Trim(),
                    CycleRules = new Dictionary<string, CycleRule>(_cycleRules, StringComparer.Ordinal),

                    AlwaysOnTop = AlwaysOnTop,
                    PetIdleMinutes = Math.Max(0, PetIdleMinutes),
                    PetThinkMode = ThinkModeKeyOf(_petThinkModeDisplay),
                    PetThinkIntervalMinutes = Math.Max(1, PetThinkIntervalMinutes),
                    PetChromeCrossScreen = PetChromeCrossScreen,
                    PetScale = Math.Clamp(PetScale, 0.4, 2.6),
                    ScreenOrientation = ScreenOrientation,

                    QqBotEnabled = QqBotEnabled,
                    QqAppId = QqAppId.Trim(),
                    QqAppSecret = QqAppSecret.Trim(),
                    QqBoundCharacterId = _qqBoundCharacterId,
                    WechatBoundCharacterId = _wechatBoundCharacterId,
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
                     ChannelsUrl = ChannelsUrl.Trim(),

                     CloudSaveEnabled = CloudSaveEnabled,
                     CloudRepo = Services.CloudSync.CloudSyncService.NormalizeRepo(CloudRepo),
                     CloudToken = CloudToken.Trim(),
                     CloudBranch = CloudBranch.Trim(),
                     CloudDeviceName = CloudDeviceName.Trim(),
                     CloudAutoUpload = CloudAutoUpload,
                     CloudReconcileMemory = CloudReconcileMemory,

                     McpEnabled = McpEnabled,
                     McpAutoApprove = McpAutoApprove,

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
                s = ApplyExtraOptions(s);
                _settings.Apply(s);
                await _settings.Persist();
            }
            catch (Exception ex)
            {
                App.WriteLog("SettingsViewModel.PersistSettings EX -> " + ex);
            }
        }
        catch (Exception ex) { App.WriteLog("Settings.PersistNow -> " + ex); }
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
            Toast.Info(result);
        }
        catch (Exception ex)
        {
            App.WriteLog("SettingsViewModel.TestConnection -> " + ex);
        }
    }
}

/// <summary>
/// 纯色档的一颗色板：一颗底色 + 「它是不是当前选中的那一颗」+ 点它的那条命令。
///
/// 选中态不用转换器、也不在代码里算画刷：外面一圈强调色的描边由 XAML 的
/// <c>IsVisible="{Binding Selected}"</c> 控制（选中 = 多套一圈 2px 的高光环），
/// 这样换主题时那一圈仍然跟着 DynamicResource 走，不会钉死在旧色上。
///
/// <see cref="PickCommand"/> 由 <see cref="SettingsViewModel"/> 在构造色板时挂进来：
/// 绑定走 <c>{Binding PickCommand}</c> 就是读这一项自己的属性，不经过祖先查找。
/// 上一版写成 <c>RelativeSource AncestorType=SettingsViewModel</c> —— 同一种写法在本页
/// 其它卡片里能用，唯独色板点了没反应，所以这一处干脆不依赖「模板能不能找到祖先」。
/// </summary>
public sealed partial class SolidSwatch : ObservableObject
{
    public SolidSwatch(string hex, string name)
    {
        Hex = hex;
        Name = name;
        Fill = Color.FromArgb(hex);
    }

    /// <summary>#RRGGBB。设置里存的就是这个字符串。</summary>
    public string Hex { get; }

    /// <summary>中文名（色板里给读屏和回执用）。</summary>
    public string Name { get; }

    /// <summary>色块本身的颜色。</summary>
    public Color Fill { get; }

    /// <summary>点这颗色的命令：参数就是这一项自己（<c>CommandParameter="{Binding .}"</c>）。</summary>
    public ICommand? PickCommand { get; init; }

    [ObservableProperty] private bool _selected;
}
