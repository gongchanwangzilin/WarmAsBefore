using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Dispatching;
using WarmAsBefore.Controls;
using WarmAsBefore.Models;
using WarmAsBefore.Models.Background;
using WarmAsBefore.Modules.AiChat;
using WarmAsBefore.Modules.Automation;
using WarmAsBefore.Modules.Outfit;
using WarmAsBefore.Modules.RealWorld;
using WarmAsBefore.Modules.SaveSystem;
using WarmAsBefore.Services;
using RealTimeProvider = WarmAsBefore.Modules.RealWorld.TimeProvider;

namespace WarmAsBefore.ViewModels;

public sealed partial class MainGameViewModel : ObservableObject
{
    private readonly GameEngine _engine;
    private readonly ChatEngine _chat;
    private readonly MemoryVault _memory;
    private readonly TaskOrchestrator _auto;
    private readonly SaveManager _save;
    private readonly WeatherProvider _weather;
    private readonly RealTimeProvider _time;
    private readonly PhysiologicalTracker _phys;
    private readonly AudioController _audio;
    private readonly SpeechService _speech;
    private readonly CharacterLibrary _chars;
    private readonly StorageProvider _store;
    private readonly FigurePackService _figures;
    private readonly PetService _pet;
    private readonly MapService _map;
    private readonly Modules.Market.GiftPanelService _gifts;
    private readonly SettingsManager _settings;
    private readonly Modules.Affection.AffectionLevelUpService _levelUp;
    private readonly Modules.Cg.CgStore _cg;
    private readonly Modules.Cg.CgViewPayload _cgView;
    private readonly Modules.Scene.SceneDirector _sceneDirector;
    private readonly Services.MapActorService _mapActors;
    private readonly Services.StickerLibrary _stickers;
    private readonly Services.BrainAnticipationService _brain;
    // 统一背景系统：配置下发由 BackgroundService 负责，天气音效由 WeatherAudioService 负责（均为 DI 注入单例）。
    private readonly Services.Background.BackgroundService _background;
    private readonly Services.Background.WeatherAudioService _weatherAudio;
    // true = 跟随真实天气：场景由实时天气驱动（此前默认 false 导致真天气拉到后不落到场景，用户反馈「没获取真实天气」）。
    // 暂不暴露 UI，先留扩展点。
    private bool _weatherSceneFollowsRealWeather = true;
    // 最近一次拿到的真实天气：界面时钟(UpdateTime)按时间重算光照时仍带上能见度/云量，不被时间覆盖掉强度。
    private WeatherReading? _lastWeather;

    [ObservableProperty] private string _locationLabel = "家";
    [ObservableProperty] private string _timeLabel = "";
    [ObservableProperty] private string _speaker = "";
    [ObservableProperty] private string _dialogue = "";
    [ObservableProperty] private bool _isAuto;
    // 好感绝对点数 0–1000：每 100 点 1 级、共 10 级。等级/进度/文案全部从它派生，
    // 不再另存一份「阶」——两套刻度并存时必然各算各的（旧版就是 0–100 好感 + 0–5000 积分）。
    [ObservableProperty] private int _affection = 300;

    /// <summary>等级（1..10），派生量。</summary>
    public int AffectionLevel => Helpers.AffectionScale.LevelOf(Affection);
    public string AffectionLevelTitle => Modules.Affection.AffectionLevel.TitleOf(AffectionLevel);
    /// <summary>「Lv.3 · 240/1000」：左栏好感读数与 HUD 读数带共用这一条原文。</summary>
    public string AffectionText => Helpers.AffectionScale.TextOf(Affection);
    /// <summary>「Lv.3 · 熟悉」：左栏「好感等级」那一行。</summary>
    public string AffectionLevelLabel => $"{Helpers.AffectionScale.LevelTextOf(Affection)} · {AffectionLevelTitle}";
    [ObservableProperty] private int _trust = 30;
    [ObservableProperty] private int _attachment;
    [ObservableProperty] private int _balance = 1000;
    [ObservableProperty] private string _weatherDesc = "晴朗";
    /// <summary>室外温度读数（HUD 的 OUTDOOR 块）。没有真实数据时为空串，块自己缩掉。</summary>
    [ObservableProperty] private string _weatherTempText = "";
    partial void OnWeatherTempTextChanged(string value) => OnPropertyChanged(nameof(HasWeatherTemp));
    /// <summary>有没有真实温度读数：HUD 的 OUTDOOR 块据此整块显示/收起。</summary>
    public bool HasWeatherTemp => !string.IsNullOrEmpty(WeatherTempText);
    /// <summary>云量 0..1（HUD 的 WEATHER 块进度线）。取不到为 -1 = 不画这条线。</summary>
    [ObservableProperty] private double _cloudCoverPct = -1;
    [ObservableProperty] private string _inputText = "";
    [ObservableProperty] private bool _isListening;
    /// <summary>启动编排（角色/地图/天气）全部收尾：页面用它掀开 LoadingVeil，就绪前先遮住还没初始化的舞台。</summary>
    [ObservableProperty] private bool _startupReady;
    // 入口进度卡：真实阶段进度（读档→立绘→地图→天气），由 RunStartup 里程碑驱动，禁假推进。
    [ObservableProperty] private bool _entryLoading = true;
    [ObservableProperty] private double _entryProgress;
    [ObservableProperty] private string _entryPhase = "准备进入";

    private void EntryStep(double progress, string phase)
    {
        EntryProgress = progress;
        EntryPhase = phase;
    }

    /// <summary>菜单浮层（存档/设置/桌宠/回标题）。功能入口在手机桌面，不在这里。</summary>
    [ObservableProperty] private bool _showMenu;
    [ObservableProperty] private bool _showMap;
    [ObservableProperty] private string _currentTimeStr = "";
    [ObservableProperty] private string _sceneBg = "#2C1810";

    /// <summary>
    /// 舞台底色：背景图之外的「空缺」用什么填。
    /// 平时用场景底色（周围的按键栏/信息栏会盖住边缘）；沉浸状态没有周围栏，改用纯黑填充。
    /// 这里用计算属性而不是缓存字段——缓存字段会在「场景色恰好等于初值」时因 setter 提前返回而
    /// 永远停在初值（曾经因此让舞台整块变黑）。
    /// </summary>
    public Color StageBackdropFill
    {
        get
        {
            if (HideDesktopPanels) return Colors.Black;
            try
            {
                return string.IsNullOrWhiteSpace(SceneBg) ? Colors.Black : Color.FromArgb(SceneBg);
            }
            catch { return Colors.Black; }
        }
    }

    partial void OnSceneBgChanged(string value) => OnPropertyChanged(nameof(StageBackdropFill));
    [ObservableProperty] private double _affectionPct;
    [ObservableProperty] private double _trustPct;
    [ObservableProperty] private ImageSource? _spriteSource;
    [ObservableProperty] private bool _spriteVisible;
    [ObservableProperty] private double _spriteOpacity = 1.0;

    /// <summary>场景亮度遮罩 0..1（由生效模式的灯光基调换算）；主界面在背景层之上叠黑色遮罩绑这个值。</summary>
    [ObservableProperty] private double _sceneDim;
    /// <summary>打雷白闪遮罩不透明度 0..1：1=全白，随后自动渐变回 0。</summary>
    [ObservableProperty] private double _flashOpacity;
    /// <summary>当前天气特效帧图（打雷/下雨的图片或动画帧）；null 表示不显示。</summary>
    [ObservableProperty] private ImageSource? _weatherFxImage;
    /// <summary>天气特效帧层是否可见。</summary>
    [ObservableProperty] private bool _weatherFxVisible;
    [ObservableProperty] private double _spriteX = 0;
    [ObservableProperty] private double _spriteY = 0;
    [ObservableProperty] private double _spriteScale = 1.0;
    [ObservableProperty] private ImageSource? _sceneBackdrop;
    [ObservableProperty] private string _sceneVideoPath = "";
    [ObservableProperty] private bool _sceneVideoOn;

    /// <summary>
    /// 场景视频自带的声音：跟「背景音乐」那档走（用户调小 BGM 就是调小所有底噪），
    /// 默认 0.7，静音到 0 时视频照放画面、只是没声。每次进场重读一次设置。
    /// </summary>
    [ObservableProperty] private double _sceneVideoVolume = 0.7;

    public void RefreshSceneVideoVolume()
        => SceneVideoVolume = Math.Clamp(_settings.Current.BgmLevel, 0, 1);

    /// <summary>
    /// 当前该显示的角色背景（场景背景缺席时才轮得到它）。
    /// 视图层监听它做 220ms 交叉淡入；null 表示回到场景色。
    /// </summary>
    [ObservableProperty] private CharacterBackdropState? _characterBackdrop;
    [ObservableProperty] private bool _isWalking;
    [ObservableProperty] private double _spriteLoadingProgress = 0;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private ObservableCollection<MapSceneOption> _sceneOptions = new();
    [ObservableProperty] private string _characterName = "小雨";
    [ObservableProperty] private bool _isThinking;
    // 聊天记录是整段替换（读档恢复），用 RangeObservableCollection 一次 Reset 通知装完：
    // 逐条 Add 会让 CollectionView 每条都排版/滚动一次，读档后那一下卡顿主要就来自这里。
    [ObservableProperty] private RangeObservableCollection<DialogueMessage> _messages = new();

    [ObservableProperty] private bool _isInMiniGame;
    // 初值必须和 IsInMiniGame 的初值（false）配套：CommunityToolkit 的 partial 回调只在
    // 值「变化」时触发，构造时初值不会走 OnIsInMiniGameChanged，写错就会出现「首次显示韩文、
    // 点一下才变中文」的错位。false = 当前 Galgame 态 → 按钮指向「切到聊天模式」。
    [ObservableProperty] private string _galgameModeLabel = "切到聊天模式";
    [ObservableProperty] private bool _showRightChat;
    [ObservableProperty] private string _lastSpeakerName = "";
    [ObservableProperty] private string _lastMessageText = "";
    [ObservableProperty] private string _dateLabel = "";
    [ObservableProperty] private int _energy = 100;
    [ObservableProperty] private double _energyPct = 1.0;
    /// <summary>大脑预判：信息面板里「TA 现在什么心情」的一句话（总开关关着就整行不显示）。</summary>
    [ObservableProperty] private string _brainStatusText = "";
    [ObservableProperty] private bool _brainVisible;

    public bool NoSpriteVisible => !SpriteVisible;

    public bool IsGalgamePanelVisible => !IsInMiniGame;

    /// <summary>手机 竖版布局是否显示：手机恒真；平板（MAUI 原生 Idiom 判定）走电脑版布局，恒假；桌面恒假。</summary>
    public bool IsPhoneLayout { get; } = DetectPhoneLayout();

    /// <summary>电脑版横版布局是否显示：平板（MAUI 原生 Idiom 判定）+ 桌面 都为真；手机恒假。</summary>
    public bool IsDesktopLayout { get; } = DetectDesktopLayout();

    /// <summary>右上角小地图：换场景自动重取材；订阅由页面进出场挂摘（见 MainGamePage）。</summary>
    public MinimapViewModel Minimap { get; }

    static bool DetectDesktopLayout()
    {
        if (DeviceInfo.Platform == DevicePlatform.WinUI
            || DeviceInfo.Platform == DevicePlatform.MacCatalyst)
            return true;
        // 平板（MAUI 原生 DeviceInfo.Idiom 判定，见 DeviceInfoService）也走电脑版布局
        return WarmAsBefore.Modules.Screen.DeviceInfoService.IsTabletScreen();
    }

    static bool DetectPhoneLayout()
    {
        // 手机才用竖版手机布局；平板（MAUI 原生 Idiom 判定）和桌面都不用
        return DeviceInfo.Platform == DevicePlatform.Android
            && !WarmAsBefore.Modules.Screen.DeviceInfoService.IsTabletScreen();
    }

    /// <summary>右侧介绍/对话框是否可见：仅在横版桌面布局、当前模式正处于聊天区并且用户确实展开时显示；
    /// 这是根本修复，避免“非对话模式里右栏仍然空占一条空间”的状态残留。</summary>
    public bool IsRightPanelVisible
        => IsDesktopLayout && IsInMiniGame && ShowRightChat;

    partial void OnIsInMiniGameChanged(bool value)
    {
        ShowRightChat = value;
        // 按钮文案 = 点击后进入的目标模式（value=IsInMiniGame）
        GalgameModeLabel = value ? "切回 Galgame" : "切到聊天模式";
        OnPropertyChanged(nameof(IsGalgamePanelVisible));
        OnPropertyChanged(nameof(IsRightPanelVisible));
    }

    [RelayCommand]
    private void ToggleGalgameMode()
    {
        IsInMiniGame = !IsInMiniGame;
    }

    /// <summary>桌面布局：展开/收起右侧对话拉达。用两个无参命令，避免 RelayCommand&lt;bool&gt; 被 XAML 字符串参数坑。</summary>
    [RelayCommand]
    private void ExpandRightPanel()
    {
        ShowRightChat = true;
        OnPropertyChanged(nameof(IsRightPanelVisible));
    }

    [RelayCommand]
    private void CollapseRightPanel()
    {
        ShowRightChat = false;
        OnPropertyChanged(nameof(IsRightPanelVisible));
    }

    private CharacterData? _char;
    private string _outfitKey = "";
    private string _defaultEmotion = "";
    private string _currentEmotion = "";
    private DateTime _lastAutoSave = DateTime.MinValue;
    private readonly SceneCalibrationService _cal;
    private readonly PortraitCalibrationService _portraits;
    private readonly ScreenBlackoutService _blackout;
    private readonly Modules.RealChat.OfficialChatBridge _bridge;
    private readonly Modules.ApiManager.ApiGateway _api;
    private readonly Services.CharacterSettingsResolver _charSettings;
    private readonly PageWindowService _pageWindows;
    private readonly Services.SaveRuntimeService _runtime;
    private readonly Modules.MinecraftBridge.MaidBridgeService _maidBridge;
    private MapScene? _currentScene;

    /// <summary>AI 主动黑屏：舞台全黑，但聊天与输入照常可用。</summary>
    [ObservableProperty] private bool _isBlackout;
    [ObservableProperty] private string _blackoutReason = "";

    public MainGameViewModel(GameEngine engine, ChatEngine chat, MemoryVault memory,
        TaskOrchestrator auto, SaveManager save, WeatherProvider weather, RealTimeProvider time,
        PhysiologicalTracker phys, AudioController audio, SpeechService speech,
        CharacterLibrary chars, StorageProvider store, PetService pet, MapService map,
        FigurePackService figures, Modules.Market.GiftPanelService gifts, SettingsManager settings,
        Modules.Affection.AffectionLevelUpService levelUp,
        Modules.Cg.CgStore cg, Modules.Cg.CgViewPayload cgView,
        Modules.Scene.SceneDirector sceneDirector, SceneCalibrationService cal,
        Services.MapActorService mapActors, Services.StickerLibrary stickers,
        Services.BrainAnticipationService brain,
        PortraitCalibrationService portraits, Services.ScreenBlackoutService blackout,
        Modules.RealChat.OfficialChatBridge bridge, Modules.ApiManager.ApiGateway api,
        Services.CharacterSettingsResolver charSettings,
        PageWindowService pageWindows, Services.SaveRuntimeService runtime,
        Services.MinimapService minimap, Services.StateService states, Services.FlowService flows,
        Modules.MinecraftBridge.MaidBridgeService maidBridge,
        Services.Background.BackgroundService background,
        Services.Background.WeatherAudioService weatherAudio)
    {
        _engine = engine;
        _chat = chat;
        _memory = memory;
        _auto = auto;
        _save = save;
        _weather = weather;
        _time = time;
        _phys = phys;
        _audio = audio;
        _speech = speech;
        _chars = chars;
        _store = store;
        _figures = figures;
        _pet = pet;
        _map = map;
        _gifts = gifts;
        _settings = settings;
        _levelUp = levelUp;
        _cg = cg;
        _cgView = cgView;
        _sceneDirector = sceneDirector;
        _cal = cal;
        _mapActors = mapActors;
        _stickers = stickers;
        _brain = brain;
        _portraits = portraits;
        _blackout = blackout;
        _bridge = bridge;
        _api = api;
        _charSettings = charSettings;
        _pageWindows = pageWindows;
        _runtime = runtime;
        _maidBridge = maidBridge;
        _background = background;
        _weatherAudio = weatherAudio;
        // 右上角的小地图：电脑默认摊开、手机默认收成方片（拧过一次就记住拧成的样子）。
        // 订阅由页面进出场挂摘（见 MainGamePage.OnAppearing / OnDisappearing），别在这儿常驻。
        Minimap = new MinimapViewModel(map, minimap, !IsPhoneLayout);
        HookFlows(flows);
        HookStates(states);
        // 角色想给用户手机发消息时：主界面弹出确认入口（AI 不能未经同意往外发）
        _bridge.OutboundPendingChanged += () => Helpers.Ui.Post(RefreshOutbound);
        // 黑屏状态变化 → 舞台全黑（聊天面板不受影响）。
        // 订阅统一挪到 AttachRuntimeEvents（审计 A4）：VM 是 Transient、服务是 Singleton，
        // 在构造里常驻订阅，每进出一次主界面就叠一份，切场景会被重复执行。
        IsBlackout = _blackout.IsBlack;
        BlackoutReason = _blackout.Reason;

        // 应用聊天显示风格 + 沉默许可
        UseBubbleChat = _settings.Current.ChatStyle == "bubble";
        _chat.SetAllowSilence(_settings.Current.AllowSilence);
        // 小组件配置先合并好：顶栏/左栏进门时就该是用户摆过的样子，而不是先闪一下旧排版。
        InitWidgets();
        // 设置变更时把天气事件参数同步进 SceneDirector（改概率/开关后无需重启即可生效）。
        // 订阅挪到 AttachRuntimeEvents（审计 A4）。
        SilenceHintText = _settings.Current.AllowSilence
            ? "已开启「允许沉默」：角色在不想说话时会选择不回复，这是正常的，不必每次提问都强求答复。"
            : "";

        // 订阅挪到 AttachRuntimeEvents（审计 A4）。
        // 时间/日期先有值，顶栏进门时就不是空白（纯计算，不碰盘）。
        UpdateTime();
    }

    private bool _begun;

    /// <summary>
    /// 启动重活：读角色与立绘、初始化地图、拉天气、起时钟。
    /// 由页面在进场过场之后调用一次——压在构造函数里会和进场动画抢同一颗主线程，
    /// 结果就是「进存档卡一下、动画还没播」。
    /// </summary>
    public void Begin()
    {
        if (_begun) return;
        _begun = true;
        App.TraceStep("MainGame.Begin.Enter");
        _auto.Start();
        // 启动重活收敛成一条编排线（审计 P7）：原来三个 fire-and-forget 并发跑、
        // 异常只能进日志，用户看到「界面出来了但数据是空的」。现在统一等待、统一兜底。
        _ = RunStartupAsync();
        _pet.WatchMinimize();
        _pet.WatchIdle();
        StartClock();
        // 存档一打开，所有内部角色开始运行（本地推演 + 低频 AI 生活 + 小游戏 + 可选主动消息）
        _runtime.Start();
        App.TraceStep("MainGame.Begin.Exit");
    }

    private async Task RunStartupAsync()
    {
        App.TraceStep("MainGame.RunStartup.Enter");
        // 启动即把设置里的散字段聚合成统一配置并下发一次，让主界面进场就拿到正确的场景/音效。
        // 异常吞进日志：背景初始化失败不该连累读档/立绘/地图等主流程。
        try
        {
            var s = _settings.Current;
            var cfg = Services.Background.BackgroundService.ConfigFromSettings(
                s.BackgroundMode, s.BackgroundImagePath, s.ThemeName, WeatherDesc,
                s.WeatherSceneAudioEnabled, s.WeatherSceneAudioLevel, s.SceneLightEnabled, s.ExperimentalSceneFxEnabled);
            // v1.63：天气设置项已删除，场景完全由存档/实时天气自动反推（_weatherSceneFollowsRealWeather
            // 默认 true）。不再读旧设置里的 WeatherSceneName 覆盖，否则会把自动天气钉死成手动旧值。
            _background.Apply(cfg);
            SyncSceneFromBackground(cfg);
        }
        catch (Exception ex) { App.WriteLog("MainGame.Background init -> " + ex); }
        var failures = new List<string>();
        EntryStep(0.15, "恢复角色与立绘");
        try { await LoadCharacterAsync(); }
        catch (Exception ex) { failures.Add("角色加载失败：" + ex.Message); App.WriteLog("MainGame.LoadCharacter -> " + ex); }
        App.TraceStep("MainGame.RunStartup.AfterLoadCharacter");
        // 渐进入口（2026-11-15）：立刻要用的（界面本体、立绘、聊天）到位即掀骨架遮罩，
        // 地图与天气（联网 2~8 秒）在遮罩掀开后由进度卡继续反馈，后台慢慢来。
        StartupReady = true;
        _ = FlushChatBacklogAsync();
        EntryStep(0.55, "界面就绪，装载地图");
        try { await InitMapAsync(); }
        catch (Exception ex) { failures.Add("地图初始化失败：" + ex.Message); App.WriteLog("MainGame.InitMap -> " + ex); }
        App.TraceStep("MainGame.RunStartup.AfterInitMap");
        EntryStep(0.75, "拉取天气");
        try { await FetchWeather(); }
        catch (Exception ex) { failures.Add("天气获取失败：" + ex.Message); App.WriteLog("MainGame.FetchWeather -> " + ex); }
        App.TraceStep("MainGame.RunStartup.AfterWeather");
        if (failures.Count > 0)
            Helpers.Ui.Post(() => Toast.Warn("部分功能没就绪：" + string.Join("；", failures)));
        App.TraceStep("MainGame.RunStartup.Exit");
        // 全部真实里程碑走完：进度卡收场（只切 IsVisible）。
        EntryStep(1.0, "完成");
        EntryLoading = false;
    }

    /// <summary>界面时钟：每 30 秒刷新时间/日期/精力，让顶部大时间真实走字。</summary>
    private IDispatcherTimer? _clock;
    private void StartClock()
    {
        if (_clock is not null) return;
        var dispatcher = Application.Current?.Dispatcher ?? Dispatcher.GetForCurrentThread();
        if (dispatcher is null) return;
        _clock = dispatcher.CreateTimer();
        _clock.Interval = TimeSpan.FromSeconds(30);
        _clock.Tick += (_, _) => UpdateTime();
        _clock.Start();
    }

    /// <summary>加载地图 → 注入 AI 地图语境 → 应用当前场景背景/位置。</summary>
    private async Task InitMapAsync()
    {
        // 普通酒馆（未绑定世界树）：没有世界就没有地图。整块跳过，也不给 AI 注入场景与移动协议，
        // 否则它会开始描述一个根本不存在的地方，用户点地图却什么也打不开。
        if (!_engine.MapUnlocked)
        {
            _chat.SetMapContext("");
            RefreshSceneOptions();
            Minimap.Visible = false;
            App.WriteLog("MainGame.InitMap -> 未绑定世界树，地图与移动协议已锁定");
            return;
        }
        try
        {
            await _map.InitializeAsync();
            Minimap.Visible = true;
            // 地图一读进来就先按它的「天气来源」设置接上天气（导入的城市图默认按当地算）。
            ApplyMapWeather();
            HookMapEvents();   // 统一走挂/摘通道，支持页面离场解绑（审计 A4）
            _chat.SetMapContext(BuildMapContext() + "\n" + _sceneDirector.BuildContext());
            SyncWeatherEventConfig();
            _ = _sceneDirector.EvaluateTimeNowAsync();
            ApplyScene(_map.CurrentScene);
            RefreshSceneOptions();
            _ = Helpers.Ui.PostAsync(async () =>
            {
                // 地图有默认出生点时，主播移动到出生场景
                if (!string.IsNullOrEmpty(_map.CurrentSceneId))
                    await _map.MoveToAsync(_map.CurrentSceneId);
            });
        }
        catch (Exception ex)
        {
            App.WriteLog("MainGame.InitMapAsync -> " + ex);
        }
    }

    private string BuildMapContext()
    {
        if (!_engine.MapUnlocked) return "";
        if (!_map.IsLoaded || _map.Map.AllScenes.Count() == 0) return "";
        var scenes = string.Join("、", _map.Map.AllScenes.Select(s =>
        {
            var loc = _map.Map.LocationNameOf(s.Id);
            return string.IsNullOrEmpty(loc) ? s.Name : $"{loc}·{s.Name}";
        }));
        var distanceContext = _map.BuildDistanceContext();
        return $"你生活在一座城市里，当前可以前往的场景：{scenes}。" +
               distanceContext +
               "当你觉得应该换个地方（回家、散步、喝咖啡等）时，" +
               "在回复的开头或结尾加上【移动:场景名】标记（场景名必须严格来自上面的列表，含地点前缀如'家·温馨住所'）。" +
               "注意：【移动:场景名】会真实消耗时间与体力，远距离移动（超过1公里）需要较长时间，" +
               "请根据实际距离合理选择目的地，不要短时间内出现在数千公里外的地方。" +
               "这个标记会被自动执行，你无需真的描述路线。";
    }

    private void OnMapSceneChanged(MapScene scene)
    {
        Helpers.Ui.Post(() =>
        {
            // 到达一站先收回走路帧，再换背景；下一段由 WalkSegmentStarted 重新起拍。
            StopWalkMotion();
            ApplyScene(scene);
            IsWalking = true;
            LocationLabel = $"{_map.Map.LocationNameOf(scene.Id)} · {scene.Name}";
            RefreshSceneOptions();
            RefreshStateButtons();
            _ = Task.Delay(600).ContinueWith(_ =>
                Helpers.Ui.Post(() => IsWalking = false));
        });
    }

    private void ApplyScene(MapScene? scene)
    {
        _currentScene = scene;
        SceneVideoOn = false;
        SceneVideoPath = "";
        if (scene is null)
        {
            SceneBackdrop = null;
            ClearCalibrationGate();
            RefreshCharacterBackdrop();
            return;
        }
        var bg = _map.ResolveBackground(scene);
        if (bg is not null)
        {
            if (MapService.IsVideoExt(Path.GetExtension(bg)))
            {
                SceneBackdrop = null;
                SceneVideoPath = bg;
                SceneVideoOn = true;
                RefreshSceneVideoVolume();
            }
            else
                SceneBackdrop = ImageSource.FromFile(bg);
        }
        else
            SceneBackdrop = null;
        SceneBg = scene.BackgroundColor;

        UpdateCalibrationGate(scene);
        _ = RefreshSpriteGeometryAsync(scene);
        // 场景背景缺席时，角色自己的背景才有资格出现；有场景就一定压过角色背景。
        RefreshCharacterBackdrop();
    }

    /// <summary>
    /// 角色背景只在「场景模式背景 / 地图背景 / 场景视频都没有」时接管舞台。
    /// 优先级是 场景背景 &gt; 角色背景 &gt; 纯色，所以这里不跟场景争抢，只负责算一个可显示项。
    /// </summary>
    private void RefreshCharacterBackdrop()
    {
        if (SceneBackdrop is not null || SceneVideoOn)
        {
            CharacterBackdrop = null;
            return;
        }
        var ch = _char ?? _engine.ActiveCharacter;
        var path = CharacterBackgroundService.Resolve(ch, _store.Root, inGame: true);
        if (string.IsNullOrWhiteSpace(path))
        {
            CharacterBackdrop = null;
            return;
        }
        var fit = CharacterBackgroundService.NormalizeFit(ch?.BackgroundFit);
        var crop = ch?.BackgroundCrop ?? new BackgroundCrop();
        CharacterBackdrop = new CharacterBackdropState(
            path, fit, crop.X, crop.Y, crop.W, crop.H,
            CharacterBackgroundService.IsVideo(path));
    }

    /// <summary>地图被编辑/导入后刷新 AI 语境与场景列表。</summary>
    private void OnMapChanged() =>
        Helpers.Ui.Post(() =>
        {
            _chat.SetMapContext(BuildMapContext());
            RefreshSceneOptions();
            // 地图换了、或「天气来源」被改过：主界面的天气要跟着换到新位置（例如导入杭州后显示杭州天气）。
            ApplyMapWeather();
        });

    /// <summary>地图 overlay 的场景列表（当前场景高亮）。</summary>
    private void RefreshSceneOptions()
    {
        if (!_map.IsLoaded) return;
        var current = _map.CurrentSceneId;
        var items = _map.Map.AllScenes.Select(sc => new MapSceneOption
        {
            SceneId = sc.Id,
            Label = $"{_map.Map.LocationNameOf(sc.Id)} · {sc.Name}",
            IsCurrent = sc.Id == current
        }).ToList();
        SceneOptions.Clear();
        foreach (var it in items) SceneOptions.Add(it);
        // 地点集合变了，左右切换按钮的可用态/相邻名要跟着重算（#9）。
        RefreshSceneSwitch();
    }

    /// <summary>好感/信任变化时同步回角色状态，保证自动保存真正落盘；好感顺带重算本级进度与派生文案。</summary>
    partial void OnAffectionChanged(int value)
    {
        SyncStatsToState();
        AffectionPct = Helpers.AffectionScale.ProgressInLevel(value);
        OnPropertyChanged(nameof(AffectionLevel));
        OnPropertyChanged(nameof(AffectionLevelTitle));
        OnPropertyChanged(nameof(AffectionText));
        OnPropertyChanged(nameof(AffectionLevelLabel));
    }
    partial void OnTrustChanged(int value) => SyncStatsToState();
    partial void OnBalanceChanged(int value) => SyncStatsToState();

    private void SyncStatsToState()
    {
        if (_engine.ActiveCharacter is not { } ch) return;
        var s = ch.State;
        if (s.Affection != Affection || s.Trust != Trust || s.Energy != Balance)
        {
            s.Affection = Affection;
            s.Trust = Trust;
            s.Energy = Balance;
        }
    }

    partial void OnSpriteVisibleChanged(bool value)
    {
        OnPropertyChanged(nameof(NoSpriteVisible));
    }

    /// <summary>
    /// 好感的唯一加点入口：聊天结算、场景判定、CG 奖励都走这里。
    /// 传进来的是旧 0–100 刻度的增减（AI 判的 ±n、固定的 +2/+3/+5 都没变），在这里 ×10 折成点数 ——
    /// 手感与旧版一致，只是存量换了刻度。跨过整 100 点（= 跨级）时播好感等级提升动画；
    /// 开关：设置 › 好感度 › 等级提升动画。
    /// </summary>
    private void AwardAffection(int legacyDelta)
    {
        if (_char is null) return;
        // 安全感整合：安全感低时好感增益打折（×0.6～1.0），安全感高时衰减更慢（×1.25～0.75）。
        // 折扣是纯函数（Modules.Affection.SecurityScale），数值本身由 SecurityService 按事件维护。
        var delta = Modules.Affection.SecurityScale.ScaleAffection(legacyDelta, _char.State.Security);
        var levelBefore = Helpers.AffectionScale.LevelOf(_char.State.Affection);
        var after = Helpers.AffectionScale.Clamp(
            _char.State.Affection + Helpers.AffectionScale.FromLegacy(delta));
        _char.State.Affection = after;
        Affection = after;      // 触发 OnAffectionChanged：落回状态 + 重算本级进度与文案

        var newLevel = Helpers.AffectionScale.LevelOf(after);
        if (newLevel > levelBefore && _settings.Current.AffectionLevelUpEnabled)
            TriggerAffectionLevelUp(newLevel);
    }

    /// <summary>好感等级提升：在后台组装动画页数据，主线程导航播放。</summary>
    private void TriggerAffectionLevelUp(int newLevel)
    {
        if (_char is null) return;
        try
        {
            _levelUp.Level = newLevel;
            _levelUp.Points = _char.State.Affection;
            _levelUp.CharacterName = CharacterMask.DisplayName(_char, _settings.Current.MaskUnintroducedNames);
            _levelUp.Title = Modules.Affection.AffectionLevel.TitleOf(newLevel);
            _levelUp.SpritePath = Modules.Affection.SpritePathResolver.Resolve(
                _char, _outfitKey, _currentEmotion, _defaultEmotion, _store.Root);
            _levelUp.BackgroundPath = _map is { IsLoaded: true } && _map.CurrentScene is { } sc
                ? _map.ResolveBackground(sc)
                : null;
            _levelUp.BackgroundColor = SceneBg;
            _levelUp.HasPayload = true;

            _ = Helpers.Ui.PostAsync(async () =>
            {
                try { await Shell.Current.GoToAsync("affection-level-up"); }
                catch (Exception ex) { App.WriteLog("TriggerAffectionLevelUp -> " + ex); }
            });
        }
        catch (Exception ex)
        {
            App.WriteLog("TriggerAffectionLevelUp -> " + ex);
        }
    }

    private async Task LoadCharacterAsync()
    {
        // 进存档 / 切存档 / 新建角色：解除 API 错误对被动 AI 的封停。
        Services.Core.BugReporter.Reset("AI");
        // 天气即时拉取一次（用户约定：进存档 / 每5分钟 / AI对话各一次）。
        _ = _sceneDirector.RefreshWeatherNowAsync();
        var charId = _engine.State.CharacterId;
        if (string.IsNullOrEmpty(charId)) return;
        var chars = await _chars.ListAsync();
        var ch = chars.FirstOrDefault(c => c.Profile.Id == charId);
        if (ch is null) return;
        _char = ch;
        // 读档时夹一次：手改档 / 老档的越界值（负数、9999）在这里就归零或封顶，界面永远看不到 -1。
        Affection = Helpers.AffectionScale.Clamp(ch.State.Affection);
        Trust = ch.State.Trust;
        Balance = Math.Max(0, ch.State.Energy);
        UpdateStats();
        RefreshBrainStatus();

        // 服装 / 表情：一律**读档恢复**，这里绝不重新随机。
        //
        // 随机只在新建存档那一刻发生（见 CharacterSelectViewModel.Pick / CreateCharacter），
        // 其余一律沿用上次状态。MainGamePage / MainGameViewModel 是 transient，
        // 每次 GoToAsync("main") 都会新建；若在构造函数里随机，「进入存档」「桌宠模式回来」
        // 都会自动换立绘。
        var savedOutfit = ch.State.CurrentOutfit;
        var savedEmotion = ch.State.CurrentEmotion;
        _figureActive = await TryLoadFigurePackAsync(ch);
        // 读档装扮诊断（双写到数据根 warm_startup.log——"查看日志"能看到；
        // 原来这条走 App.WriteLog 只写 Documents 侧，本机该目录不存在，证据会丢）。
        // 用于定位"再续情缘后衣着和上次不同"：看存档记的装扮与实际选中值是否一致。
        App.TraceStep($"OutfitRestore: char={ch.Profile.Name}, savedOutfit={savedOutfit}, " +
                      $"savedEmotion={savedEmotion}, figureActive={_figureActive}, " +
                      $"figurePackId={ch.FigurePackId}, spriteMapCount={ch.SpriteMap.Count}");
        if (_figureActive)
        {
            // 形象包的服装/表情是合成器的两条输入轴，不再依赖 SpriteMap 的键。
            // 存档里记过且包里仍然存在 → 沿用；否则确定性回退，避免每次读档换一张脸。
            _outfitKey = _figureOutfits.FirstOrDefault(o =>
                            string.Equals(o, savedOutfit, StringComparison.OrdinalIgnoreCase))
                         ?? _figureOutfits.FirstOrDefault()
                         ?? "default";
            _defaultEmotion = _figureEmotions.FirstOrDefault(e =>
                                  string.Equals(e, savedEmotion, StringComparison.OrdinalIgnoreCase))
                              ?? _figureEmotions.FirstOrDefault()
                              ?? "normal";
        }
        else
        {
            var outfits = ch.SpriteMap.Keys.Select(k => k.Split('/')[0]).Distinct().ToList();
            if (outfits.Count > 0)
            {
                // 存档里记过且该服装仍然存在 → 沿用；否则回退到第一个（老存档从没写过这两个字段，
                // 只能用确定性回退，至少保证此后每次进来都一样）
                _outfitKey = outfits.Contains(savedOutfit) ? savedOutfit : outfits[0];
            }
            else
            {
                _outfitKey = savedOutfit;
            }

            var emotions = ch.SpriteMap.Keys
                .Where(k => k.StartsWith(_outfitKey + "/", StringComparison.Ordinal))
                .Select(k => k.Split('/')[1])
                .Distinct()
                .ToList();
            _defaultEmotion = emotions.Contains(savedEmotion)
                ? savedEmotion
                : (emotions.Count > 0 ? emotions[0] : "normal");
        }
        _currentEmotion = _defaultEmotion;

        var outfitChanged = !string.IsNullOrEmpty(savedOutfit)
                            && !string.Equals(savedOutfit, _outfitKey, StringComparison.OrdinalIgnoreCase);
        App.TraceStep($"OutfitRestore.Result: resolvedOutfit={_outfitKey}, resolvedEmotion={_currentEmotion}, " +
                      $"changed={outfitChanged} (saved={savedOutfit}); " +
                      $"figureOutfits=[{string.Join(",", _figureOutfits)}]");

        // 写回角色状态，随存档一起持久化 —— 这是「按上次立绘状态恢复」能成立的前提
        ch.State.CurrentOutfit = _outfitKey;
        ch.State.CurrentEmotion = _currentEmotion;

        SetEmotion(_currentEmotion);
        Helpers.Ui.Post(() =>
        {
            RefreshCharacterName();
            _chat.ConfigureCharacter(ch.Profile);
            _chat.SetRoster(_chars.RosterContext(ch.Profile.Id, _engine.State.AllowedCharacters));
            RestoreSession();
            // 读档后同步黑屏状态（黑屏是存档的一部分，重启也该保持）
            _blackout.SyncFromState();
        });
        App.TraceStep($"LoadCharacterAsync: char={ch.Profile.Name}, outfit={_outfitKey}, emotion={_currentEmotion}, sprites={ch.SpriteMap.Count}");
        // 角色读进来后才知道有没有专属背景；切角色/读档都走这里，视图会按新旧两层交叉淡入。
        Helpers.Ui.Post(RefreshCharacterBackdrop);
        _ = _audio.StartBgmRotationAsync();
    }

    /// <summary>读档后恢复聊天界面（消息来自存档里保存的会话记录）。</summary>
    private List<DialogueMessage>? _pendingChatBacklog;

    private void RestoreSession()
    {
        // 按需加载（2026-11-15）：进档首帧的布局风暴里一次性实例化 60 条变高气泡
        // 是布局环高发窗口（历史崩溃全部落在恢复聊天后 ~200ms 的首次虚拟化布局）。
        // 先把会话暂存 backlog，等 StartupReady 后 2 秒由 FlushChatBacklogAsync 灌入列表；
        // 历史对话晚两秒可见，换进场不卡不崩。
        _pendingChatBacklog = _save.ChatLog.Select(r => new DialogueMessage
        {
            Role = r.Role,
            Text = r.Text,
            Time = r.At.ToString("HH:mm")
        }).ToList();
    }

    /// <summary>进场风暴过后把暂存的历史会话灌入聊天列表；期间新发的消息合并在后，不丢。</summary>
    private async Task FlushChatBacklogAsync()
    {
        try
        {
            await Task.Delay(2000);
            var backlog = _pendingChatBacklog;
            _pendingChatBacklog = null;
            if (backlog is null || backlog.Count == 0) return;
            Helpers.Ui.Post(() =>
            {
                using (PerfLog.Measure("页 · 主界面 · 延迟恢复聊天记录"))
                {
                    // backlog 在前、期间新消息在后：ReplaceRange 一次 Reset 通知完成合并。
                    var merged = backlog.Concat(Messages.ToList()).ToList();
                    Messages.ReplaceRange(merged);
                }
            });
        }
        catch (Exception ex) { App.WriteLog("MainGame.FlushChatBacklog -> " + ex); }
    }

    /// <summary>
    /// 主界面上的名字：AI 自行加进来的人还没自我介绍时先显示「？？？」。
    /// 这一层只改显示，不动角色档案里的真名。
    /// </summary>
    private void RefreshCharacterName()
    {
        if (_char is null) return;
        var wasCharacter = !string.IsNullOrEmpty(LastSpeakerName) && LastSpeakerName != "你";
        CharacterName = CharacterMask.DisplayName(_char, _settings.Current.MaskUnintroducedNames);
        if (wasCharacter) LastSpeakerName = CharacterName;
        // 换人就等于换了一套「这次由谁回答」的生效规则，顶栏那句话得跟着重算
        RefreshAnswerAiLabel();
    }

    private void OnGreet(string msg)
    {
        Helpers.Ui.Post(() =>
        {
            Speaker = _engine.State.CharacterId;
            Dialogue = msg;
            AddMessage("assistant", msg);
            var e = ResolveEmotion(msg);
            SetEmotion(e ?? RandomEmotion() ?? _defaultEmotion);
        });
    }

    [RelayCommand]
    private async Task SendMessage()
    {
        // 未标定遮罩期间禁止交互：这时角色位置无从确定，剧情推进下去也没有意义
        if (IsCalibrationBlocked) return;
        // #10：允许「只发文件不带文字」——只要附件非空就发
        if (string.IsNullOrWhiteSpace(InputText) && PendingAttachments.Count == 0) return;
        _audio.PlayAssigned("key");
        var msg = InputText;
        var files = PendingAttachments.ToList();
        InputText = "";
        ClearAttachmentsInternal();
        AddMessage("user", files.Count > 0 ? $"{msg}（附 {files.Count} 个文件）".Trim() : msg);
        // #12：把最新表情包清单同步给 AI（库没变时内部直接跳过，开销可忽略）
        await _stickers.EnsureLoadedAsync();
        _chat.SetStickerContext(_stickers.BuildContextForAi());
        var charId = _engine.State.CharacterId;
        if (!string.IsNullOrEmpty(charId))
        {
            IsThinking = true;
            var segments = new List<string>();
            var display = "";
            var rawForStats = "";
            var stickerNames = new List<string>(); // #12：AI 本轮要发的表情（稍后渲染成图）
            try
            {
                // #10：有附件时走带附件的发送重载
                var reply = files.Count > 0
                    ? await _chat.Send(charId, msg, files)
                    : await _chat.Send(charId, msg);
                // API 失败时网关返回 [API错误…]/[网络…]/[请求超时…] 串。错误已经走
                // OnApiError 的红色提示 + 分析窗，这里绝不能把它当 AI 台词塞进气泡。
                // 返回空串让下游走「本轮没有可显示的正文」分支，不新增助手气泡。
                var apiFailed = IsApiErrorText(reply);
                if (apiFailed) reply = "";
                // 天气跟随 AI 对话即时拉取一次（用户约定：进存档 / 每5分钟 / AI对话各一次）。
                // 失败无妨，SceneDirector 内部已吞异常；异步不阻塞本轮对话渲染。
                if (!apiFailed) _ = _sceneDirector.RefreshWeatherNowAsync();
                // #12：先剥离 [表情:名字] 标记，正文照常处理
                var (replyClean, foundStickers) = Services.StickerLibrary.SplitMarkers(reply);
                reply = replyClean;
                stickerNames.AddRange(foundStickers);
                var (segs, hasReaction) = await ProcessReplyAsync(reply);
                // 动作标记缺失 → System 级补正：有正文=重输出，近乎为空=补输出
                if (!hasReaction && !string.IsNullOrWhiteSpace(_settings.Current.AiUrl))
                {
                    var corrected = await _chat.RetryActionMarkerAsync(
                        charId, msg, reply, isReOutput: !string.IsNullOrWhiteSpace(reply.Trim()));
                    if (!ReferenceEquals(corrected, reply) && corrected != reply)
                    {
                        _chat.ReplaceLastAssistant(charId, corrected);
                        var (corrClean, moreStickers) = Services.StickerLibrary.SplitMarkers(corrected);
                        stickerNames.AddRange(moreStickers);
                        (segs, hasReaction) = await ProcessReplyAsync(corrClean);
                        reply = corrClean;
                    }
                }
                segments = segs;
                if (segments.Count == 0) segments.Add(reply.Trim());
                display = string.Join(" ", segments);
                rawForStats = reply;
            }
            catch (Exception ex)
            {
                // AI 未连接：不崩溃、不空白，自动选一张立绘陪伴，并点明没收到
                App.WriteLog("MainGameViewModel.SendMessage -> " + ex);
                segments.Add("……");
                display = "……";
            }
            finally { IsThinking = false; }
            // 架构流程图打点：一轮输出完成，循环反馈回到输入（第5层 eng）。
            Services.Core.FlowTracer.Trace("eng.output", _engine?.State?.CharacterId);

            // 双端互通：把 WAB 主对话这一轮同步到游戏里的女仆（没连桥 / 没绑角色时内部直接返回）。
            _ = _maidBridge.NotifyWabDialogAsync(charId, msg, display);

            // 这一轮里 TA 可能在回复中报上了名字（RuntimeConfigurator 已落盘），
            // 主界面与这一条气泡的署名要立刻从「？？？」换回真名。
            RefreshCharacterName();
            FireAiCall();

            // 沉默判定：AI 允许沉默且本轮无正文（仅有动作标记/为空）→ 本回合停止，不再追加回复
            var isSilent = _settings.Current.AllowSilence && stickerNames.Count == 0 &&
                           string.IsNullOrWhiteSpace(StripActionMarkers(display));
            if (isSilent)
            {
                AddMessage("assistant", "（…沉默…）");
                _ = AutoSave();
                return;
            }

            foreach (var seg in segments) AddMessage("assistant", seg);
            // #12：AI 发的表情挂成图片消息（找不到对应表情就静默跳过）
            foreach (var sn in stickerNames)
            {
                var sticker = _stickers.Find(sn);
                if (sticker is not null) AddMessage("assistant", "", sticker.Path);
            }
            _ = ApplyChatAffectionAsync(charId, msg, rawForStats, display);
            CaptureMoment(charId, 1, "聊天");
            var e = ResolveEmotion(display);
            SetEmotion(e ?? RandomEmotion() ?? _defaultEmotion);
            _ = _speech.Speak(StripActionMarkers(display));
            _ = AutoSave();
        }
    }

    /// <summary>
    /// CG 触发协议：AI 回复含【CG:文件名】→ 解锁并全屏播放该 CG。
    /// 每个 CG 标记解锁后都会向 AI 结构化询问这一幕带来的好感积分增量。
    /// </summary>
    private async Task<string> HandleCgMarkersAsync(string reply)
    {
        if (string.IsNullOrWhiteSpace(reply) || !reply.Contains("【CG:", StringComparison.Ordinal))
            return reply;

        var triggered = new List<(string File, string Title)>();
        foreach (Match m in Regex.Matches(reply, @"【CG:([^】]+)】"))
        {
            var file = m.Groups[1].Value.Trim();
            if (string.IsNullOrEmpty(file) || triggered.Any(t => t.File == file)) continue;
            var rec = await _cg.UnlockAsync(file, Path.GetFileNameWithoutExtension(file));
            triggered.Add((file, rec?.Title ?? Path.GetFileNameWithoutExtension(file)));
        }
        var result = Regex.Replace(reply, @"【CG:[^】]+】", "").Trim();

        if (triggered.Count > 0)
        {
            var first = triggered[0];
            var path = _cg.ResolvePath(new Models.CgRecord { File = first.File });
            _cgView.ImagePath = path ?? "";
            _cgView.Title = first.Title;
            _cgView.HasPayload = path is not null;

            // 问 AI：这一幕好感积分增加多少（结构化请求）
            foreach (var (file, title) in triggered)
                _ = AskCgAffectionAsync(file, title);

            _ = Helpers.Ui.PostAsync(async () =>
            {
                try { await Shell.Current.GoToAsync("cg-view"); }
                catch (Exception ex) { App.WriteLog("HandleCgMarkers cg-view -> " + ex); }
            });
        }
        return result;
    }

    /// <summary>触发单个 CG 标记：解锁 + 全屏播放 + 好感积分询问。</summary>
    private async Task TriggerSingleCgAsync(string file)
    {
        if (string.IsNullOrEmpty(file)) return;
        try
        {
            var rec = await _cg.UnlockAsync(file, Path.GetFileNameWithoutExtension(file));
            if (rec is null) return;
            var title = rec.Title ?? Path.GetFileNameWithoutExtension(file);
            var path = _cg.ResolvePath(new Models.CgRecord { File = file });
            _cgView.ImagePath = path ?? "";
            _cgView.Title = title;
            _cgView.HasPayload = path is not null;
            _ = AskCgAffectionAsync(file, title);
            _ = Helpers.Ui.PostAsync(async () =>
            {
                try { await Shell.Current.GoToAsync("cg-view"); }
                catch (Exception ex) { App.WriteLog("TriggerSingleCg cg-view -> " + ex); }
            });
        }
        catch (Exception ex)
        {
            App.WriteLog("MainGame.TriggerSingleCg -> " + ex);
        }
    }

    /// <summary>执行动作标记的动画（三种反应即时触发，不占展示文本）。</summary>
    private void RunActionMarker(string name)
    {
        switch (name)
        {
            case "下沉": _ = SinkAnimationAsync(); break;
            case "雀跃": _ = JumpAnimationAsync(); break;
            case "颤抖": _ = ShakeAnimationAsync(); break;
        }
    }

    /// <summary>从语音朗读文本里去掉动作标记。</summary>
    private static string StripActionMarkers(string text)
        => ActionMarkerRegex.Replace(text, "");

    /// <summary>去掉数值指令标记（【状态:...】）：它是给机器读的，不该出现在展示/朗读文本里。</summary>
    private static string StripStatDirective(string text)
        => StatDirectiveRegex.Replace(text ?? "", "").Trim();

    /// <summary>CG 好感增量：AI 评定 → 累加积分 → 写入回忆录。</summary>
    private async Task AskCgAffectionAsync(string file, string title)
    {
        try
        {
            var delta = Math.Clamp(await _chat.AskAffectionDeltaAsync(title), 0, 20);
            AwardAffection(delta);
            if (_engine.ActiveCharacter is { } ch)
                _ = _memory.LogAffection(ch.Profile.Id, delta, $"CG「{title}」");
        }
        catch (Exception ex)
        {
            App.WriteLog("MainGame.AskCgAffection -> " + ex);
        }
    }

    [RelayCommand]
    private async Task VoiceInput()
    {
        IsListening = true;
        _speech.OnRecognized += OnSpeechResult;
        try
        {
            await _speech.StartListening();
        }
        catch (Exception ex)
        {
            App.WriteLog("MainGameViewModel.VoiceInput -> " + ex);
            IsListening = false;
        }
    }

    private void OnSpeechResult(string text)
    {
        _speech.OnRecognized -= OnSpeechResult;
        Helpers.Ui.Post(() =>
        {
            IsListening = false;
            if (!string.IsNullOrWhiteSpace(text))
                InputText = text;
        });
    }

    /// <summary>
    /// 好感提升瞬间：截屏主窗口画面（不含底部信息栏）存档，并写入回忆录。
    /// 截屏在后台线程执行，失败不阻塞好感互动。
    /// </summary>
    private void CaptureMoment(string charId, int delta, string reason)
    {
        try
        {
#if WINDOWS
            // 性能优化：延迟500ms后异步截屏，避免阻塞主线程
            _ = Task.Delay(500).ContinueWith(_ =>
            {
                try
                {
                    var img = CaptureWindowSnapshot();
                    _ = _memory.LogAffection(charId, delta, reason, img);
                }
                catch (Exception ex)
                {
                    App.WriteLog("MainGameViewModel.CaptureMoment -> " + ex);
                }
            }, TaskScheduler.Default);
#else
            _ = _memory.LogAffection(charId, delta, reason, null);
#endif
        }
        catch (Exception ex)
        {
            App.WriteLog("MainGameViewModel.CaptureMoment -> " + ex);
        }
    }

#if WINDOWS
    /// <summary>截取主窗口客户区（顶部以下到信息栏以上），存 PNG 并返回路径。</summary>
    private static string? CaptureWindowSnapshot()
    {
        try
        {
            var wnd = Application.Current?.Windows.FirstOrDefault(w => w.Handler is not null && w is not WarmAsBefore.Views.ToolWindow)
                ?.Handler?.PlatformView as Microsoft.UI.Xaml.Window;
            if (wnd is null) return null;
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(wnd);

            GetClientRect(hwnd, out var cr);
            var w = cr.Right - cr.Left;
            var h = cr.Bottom - cr.Top;
            if (w <= 0 || h <= 0) return null;

            var pt = new WinPoint { X = 0, Y = 0 };
            ClientToScreen(hwnd, ref pt);

            // 底部信息栏（对话输入区约 90px，含 DPI 换算）裁掉，只留展示画面
            var scale = GetDpiForWindow(hwnd) / 96.0;
            var infoBar = (int)Math.Ceiling(96 * scale);
            var cutH = Math.Max(h - infoBar, 60);

            using var bmp = new System.Drawing.Bitmap(w, cutH);
            using (var g = System.Drawing.Graphics.FromImage(bmp))
                g.CopyFromScreen(pt.X, pt.Y, 0, 0, new System.Drawing.Size(w, cutH));

            var dir = Path.Combine(App.RootDirectory, "Memories");
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, $"aff_{DateTime.Now:yyyyMMdd_HHmmss_fff}.png");
            bmp.Save(file, System.Drawing.Imaging.ImageFormat.Png);
            return file;
        }
        catch (Exception ex)
        {
            App.WriteLog("MainGameViewModel.CaptureWindowSnapshot -> " + ex);
            return null;
        }
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct WinRect { public int Left, Top, Right, Bottom; }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct WinPoint { public int X, Y; }
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hWnd, out WinRect lpRect);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ClientToScreen(IntPtr hWnd, ref WinPoint lpPoint);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hWnd);
#endif

    [RelayCommand]
    private async Task Headpat()
    {
        AddMessage("assistant", "小雨被摸了摸头，害羞地笑了~");
        SetEmotionAny("害羞", "闭眼微笑", "开心");
        CaptureMoment(_engine.State.CharacterId, 2, "摸头");
        _ = SinkAnimationAsync();
        await JudgeSceneAsync("你轻轻摸了摸 TA 的头，TA 害羞地笑了笑。", 2, 0);
    }

    [RelayCommand]
    private async Task Hug()
    {
        AddMessage("assistant", "小雨轻轻抱住了你，感觉很温暖。");
        SetEmotionAny("温柔", "闭眼微笑", "开心");
        CaptureMoment(_engine.State.CharacterId, 3, "拥抱");
        _ = JumpAnimationAsync();
        await JudgeSceneAsync("你轻轻抱住了 TA，TA 靠在你怀里，很安心。", 3, 2);
    }

    [RelayCommand]
    private async Task Kiss()
    {
        // 门槛仍是旧刻度的 40（好感 40%）：换算一次保持原意，别把 40 直接当 40 点（那只有 4%）。
        if (Affection < Helpers.AffectionScale.FromLegacy(40))
        {
            AddMessage("assistant", "小雨脸红了，躲开了... 好感度还不够呢。");
            return;
        }
        AddMessage("assistant", "小雨踮起脚尖，在你脸颊上轻轻一吻~");
        SetEmotionAny("害羞", "惊讶", "开心");
        CaptureMoment(_engine.State.CharacterId, 5, "亲吻");
        _ = ShakeAnimationAsync();
        await JudgeSceneAsync("你踮起脚，在 TA 脸颊上轻轻一吻，TA 害羞地红了脸。", 5, 3);
    }

    /// <summary>
    /// 手机桌面：设置里开着「手机独立窗口」且平台支持时另开一扇（主窗口继续聊天）。
    /// 窗口里的图标点下去仍然导航主窗口 —— 只有一整套 Shell 路由，另起一套会让所有页的返回键失灵；
    /// 所以点图标时先把主窗口抬到前面，用户知道内容去哪了。
    /// </summary>
    [RelayCommand]
    private async Task OpenPhone()
    {
        if (_settings.Current.PhoneStandaloneWindow)
        {
            await _pageWindows.OpenOrNavigateAsync(
                PageWindowKeys.Phone, "手机", typeof(Views.PhonePage), "phone", null, 460, 900);
            return;
        }
        await Shell.Current.GoToAsync("phone");
    }

    /// <summary>地图按钮的文案：未绑定世界树时直接写「锁」，点之前就知道打不开。</summary>
    public string MapButtonText => _engine.MapUnlocked ? "地图" : "地图·锁";

    [RelayCommand]
    private async Task OpenMap()
    {
        if (!_engine.MapUnlocked)
        {
            Toast.Info("这一局没有绑定世界书，所以没有地图。想解锁：菜单 → 本局设定 → 选一份世界书。");
            return;
        }
        await Shell.Current.GoToAsync("map");
    }

    [RelayCommand]
    private async Task OpenFullMap() => await OpenMap();

    /// <summary>
    /// 超级监测入口：从「正在玩的这份存档」进入，charId 取当前存档角色。
    /// 导航失败只记日志，不让主界面因为一个装饰性入口崩掉。
    /// </summary>
    [RelayCommand]
    private async Task OpenMonitor()
    {
        var charId = _engine.State.CharacterId;
        try
        {
            await Shell.Current.GoToAsync($"superMonitor?charId={Uri.EscapeDataString(charId ?? "")}");
        }
        catch (Exception ex) { App.WriteLog("MainGame.OpenMonitor -> " + ex.Message); }
    }

    // ============ #E4 电脑端：隐藏顶栏/侧栏（ESC）与全屏（F11，code-behind 处理） ============

    /// <summary>
    /// 电脑端隐藏左侧信息栏与顶部状态栏（ESC 或顶栏「收起」再按恢复）；手机端不生效。
    /// 记在 Preferences 里：VM 是 transient，换页回来 / 重启客户端后收起状态不该自己弹开。
    /// </summary>
    [ObservableProperty] private bool _hideDesktopPanels =
        Microsoft.Maui.Storage.Preferences.Default.Get("game.hideDesktopPanels", false);

    [RelayCommand]
    private void ToggleHidePanels() => HideDesktopPanels = !HideDesktopPanels;

    /// <summary>沉浸状态切换：周围栏隐藏后，空缺改由纯黑填充。</summary>
    partial void OnHideDesktopPanelsChanged(bool value)
    {
        // 偏好落盘是「尽力而为」：preferences.dat 偶发被占用/只读时，File.Create 会抛
        // UnauthorizedAccessException。沉浸态只是会话内的视图状态，写盘失败顶多「重启后不保留
        // 收起状态」，绝不该冒泡成未处理异常——实测会被 BugReporter 捕获、直接停掉整个 WinUI
        // 子系统并弹出错误框（bugs.log 2026-11-15 16:27:57）。故此处吞掉并记日志。
        try { Microsoft.Maui.Storage.Preferences.Default.Set("game.hideDesktopPanels", value); }
        catch (Exception ex) { App.WriteLogNow("Save hideDesktopPanels ignored: " + ex.Message); }
        OnPropertyChanged(nameof(StageBackdropFill));
        OnPropertyChanged(nameof(IsImmersive));
        NotifyPanelVisibility();
    }

    /// <summary>沉浸状态：周围栏（状态栏/信息栏）已隐藏。</summary>
    public bool IsImmersive => HideDesktopPanels;

    /// <summary>带着当前立绘去标定（不合规素材的调试入口）。</summary>
    [RelayCommand]
    private async Task OpenPortraitCalibration()
    {
        var key = _spriteKey ?? "";
        if (string.IsNullOrEmpty(key)) return;
        await Shell.Current.GoToAsync($"portraitCalibration?key={Uri.EscapeDataString(key)}");
    }

    /// <summary>用户手动退出黑屏（AI 选了黑屏时也留个出口，免得被卡在黑屏里）。</summary>
    [RelayCommand]
    private void ExitBlackout()
    {
        _blackout.Set(false);
        IsBlackout = false;
        BlackoutReason = "";
    }

    // ===== 临时指定这次由哪个 AI 回答（不写入设置，退出即失效） =====

    [ObservableProperty] private string _answerAiLabel = "AI：自动";
    [ObservableProperty] private bool _hasTemporaryAi;

    /// <summary>
    /// 顶栏「AI」按钮上那句话。角色绑了模型时直接报绑定的那个：
    /// 绑定在 ApiGateway 里压过临时指定，不写清楚用户就会以为「点了没生效」。
    /// </summary>
    private void RefreshAnswerAiLabel()
    {
        var bound = _charSettings.BoundModelLabel(_engine.State.CharacterId);
        var temp = _api.TemporaryEndpointLabel;
        HasTemporaryAi = !string.IsNullOrEmpty(temp);
        AnswerAiLabel = bound.Length > 0
            ? $"AI：{CharacterName} 用绑定的 {bound}"
              + (HasTemporaryAi ? "（这里指定的只对没绑模型的角色生效）" : "")
            : HasTemporaryAi ? $"AI：{temp}" : "AI：自动";
    }

    /// <summary>
    /// 弹一个列表让用户临时指定回答的 AI：自动（按设置顺序）/ 某条端点。
    /// 这是「我现在就想用这个模型」的快捷入口，不动全局设置。
    /// 当前角色绑过模型时它压不过绑定，所以标题与提示都要把这件事说明白。
    /// </summary>
    [RelayCommand]
    private async Task PickAnswerAi()
    {
        try
        {
            var s = _settings.Current;
            var bound = _charSettings.BoundModelLabel(_engine.State.CharacterId);
            var options = new List<string> { "自动（按设置里的顺序）" };
            var profiles = (s.AiProfiles ?? new List<AiEndpointProfile>())
                .Where(p => p.Enabled && !string.IsNullOrWhiteSpace(p.Url) && !string.IsNullOrWhiteSpace(p.Model))
                .ToList();
            foreach (var p in profiles)
                options.Add($"{(string.IsNullOrWhiteSpace(p.Name) ? p.Model : p.Name)}（{p.Model}）");
            if (!string.IsNullOrWhiteSpace(s.AiModel))
                options.Add($"主配置（{s.AiModel}）");

            var title = bound.Length > 0
                ? $"{CharacterName} 绑的是「{bound}」——这里指定只对没绑模型的角色生效"
                : "这次由哪个 AI 回答";
            var pick = await Shell.Current.DisplayActionSheet(title, "取消", null, options.ToArray());
            if (pick is null || pick == "取消") return;

            if (pick.StartsWith("自动", StringComparison.Ordinal))
            {
                _api.SetTemporaryEndpoint(null);
            }
            else if (pick.StartsWith("主配置", StringComparison.Ordinal))
            {
                _api.SetTemporaryEndpoint(new AiEndpoint
                {
                    Provider = "主配置",
                    Url = s.AiUrl,
                    Key = s.AiKey,
                    Model = s.AiModel,
                    Temperature = s.AiTemperature,
                    MaxTokens = s.AiMaxTokens,
                    DeepThink = s.DeepThink || s.ComplexPlot,
                    DeepModel = s.DeepModel,
                    MemoryTurns = s.MemoryTurns,
                    ContextTokenLimit = s.ContextTokenLimit > 0 ? s.ContextTokenLimit : 32000
                });
            }
            else
            {
                var idx = options.IndexOf(pick) - 1;
                if (idx < 0 || idx >= profiles.Count) return;
                var p = profiles[idx];
                var keys = (p.Keys ?? new List<string>()).Where(k => !string.IsNullOrWhiteSpace(k)).ToList();
                if (keys.Count == 0) { Toast.Warn("这条端点没有可用密钥。"); return; }
                _api.SetTemporaryEndpoint(new AiEndpoint
                {
                    Provider = string.IsNullOrWhiteSpace(p.Name) ? p.Model : p.Name!,
                    Url = p.Url.Trim(),
                    Key = keys[0],
                    KeyPool = keys,
                    Model = p.Model.Trim(),
                    Temperature = s.AiTemperature,
                    MaxTokens = s.AiMaxTokens,
                    DeepThink = s.DeepThink || s.ComplexPlot,
                    DeepModel = s.DeepModel,
                    MemoryTurns = s.MemoryTurns,
                    ContextTokenLimit = s.ContextTokenLimit > 0 ? s.ContextTokenLimit : 32000,
                    TimeoutSeconds = p.TimeoutSeconds
                });
            }

            // 用户重新指定了本局的 AI：视为一次手动修复，解除 API 错误对被动 AI 的封停。
            Services.Core.BugReporter.Reset("AI");
            RefreshAnswerAiLabel();
            if (bound.Length > 0 && HasTemporaryAi)
                Toast.Info($"{CharacterName} 仍用绑定的「{bound}」，这次指定只对没绑模型的角色生效。");
        }
        catch (Exception ex)
        {
            App.WriteLog("MainGameVM.PickAnswerAi -> " + ex);
        }
    }

    [RelayCommand]
    private async Task OpenSave() => await Shell.Current.GoToAsync("save");

    /// <summary>本局设定：改人设、换绑世界树。改完会重新整理一次记忆。</summary>
    [RelayCommand]
    private async Task OpenRunSettings()
    {
        ShowMenu = false;
        await Shell.Current.GoToAsync("newrun?edit=1");
    }

    /// <summary>开关菜单浮层。名字不叫 settings：它开的不是设置，是这一局的菜单。</summary>
    [RelayCommand]
    private void ToggleMenu() => ShowMenu = !ShowMenu;

    [RelayCommand]
    private async Task OpenSettings() => await Shell.Current.GoToAsync("settings");

    /// <summary>收纳到桌面（桌宠模式）：主窗口隐藏，桌面只留立绘，托盘可恢复。</summary>
    [RelayCommand]
    private void PetMode() => PetService.TogglePetModeStatic();

    [RelayCommand]
    private async Task QuickSave()
    {
        var ok = await _save.Commit("快速存档");
        if (ok) Toast.Success("已保存");
    }

    /// <summary>自动保存（防抖 5 秒）：聊天/互动后把进度写入当前槽位，不产生新档。</summary>
    private async Task AutoSave()
    {
        if (string.IsNullOrEmpty(_engine.CurrentSaveId)) return;
        if ((DateTime.UtcNow - _lastAutoSave).TotalSeconds < 5) return;
        _lastAutoSave = DateTime.UtcNow;
        try { await _save.Commit("自动存档"); }
        catch (Exception ex) { App.WriteLog("MainGame.AutoSave -> " + ex); }
    }

    /// <summary>回标题：先落盘再停自动存档，否则这一局聊的天会丢。</summary>
    [RelayCommand]
    private async Task BackToTitle()
    {
        // 先停运行时再落盘：角色们最后的活动状态才写得进这一档
        _runtime.Stop();
        await _save.Commit("存档");
        _auto.Stop();
        await Shell.Current.GoToAsync("..");
    }

    [RelayCommand]
    private void ToggleAuto()
    {
        IsAuto = !IsAuto;
        _engine.ToggleAuto();
    }

    private void AddMessage(string role, string text, string? stickerPath = null)
    {
        Messages.Add(new DialogueMessage
        {
            Role = role,
            Text = text,
            Time = DateTime.Now.ToString("HH:mm"),
            StickerPath = stickerPath
        });
        // 对话卡剧情文本区：始终展示最近一条
        LastSpeakerName = role == "user" ? "你" : (string.IsNullOrEmpty(CharacterName) ? "角色" : CharacterName);
        LastMessageText = text;
        _save.ChatLog.Add(new ChatRecord { Role = role, Text = text, At = DateTime.Now });
        if (_save.ChatLog.Count > 80) _save.ChatLog.RemoveRange(0, _save.ChatLog.Count - 80);
        // 更新说话者高亮：assistant说话时高亮立绘
        IsSpeaking = role != "user";
        if (SpriteVisible)
        {
            SpriteOpacity = IsSpeaking ? 1.0 : 0.5;
            SpriteScale = IsSpeaking ? 1.0 : 0.95;
        }
    }

    private void UpdateStats()
    {
        // 好感进度条读的是「本级进度」(0..1)，不是 0–1000 的绝对量。
        AffectionPct = Helpers.AffectionScale.ProgressInLevel(Affection);
        TrustPct = Trust / 100.0;
    }

    /// <summary>
    /// 聊天好感结算，数值全走 AI 判定（混合）：
    /// ① 内联指令：AI 在回复里直接给出 <c>【状态:好感±n 信任±n 精力±n】</c> 就照它来（零额外调用）；
    /// ② 兜底：没带指令、但配了模型 → 结构化问一次 AskFeelingDelta；
    /// ③ 无 AI / 问不出：安全默认，一句不动（不假装涨）。
    /// 关掉「数值由 AI 控制」时不判 AI，回到旧的固定增量（聊天 +1 好感 +1 信任，精力不动）。
    /// <paramref name="rawReply"/> 保留机器标记用于解析指令；<paramref name="displayReply"/> 是剥掉标记的正文，供兜底问句。
    /// </summary>
    private async Task ApplyChatAffectionAsync(string charId, string text, string rawReply, string displayReply)
    {
        static string Clip(string s) => s.Length <= 120 ? s : s[..120];

        // 0) 关掉 AI 数值控制：不判 AI，回旧固定增量
        if (!_settings.Current.StatAiControl)
        {
            Settle(1, 1, 0);
            return;
        }

        // ① 内联指令（零额外调用）
        var inline = _chat.ParseStatDirective(rawReply);
        if (inline.Has)
        {
            Settle(inline.Affection, inline.Trust, inline.Energy);
            return;
        }

        // ② 兜底：配了模型才问；问不出 → ③
        if (_api.HasAnyEndpoint)
        {
            (int Affection, int Trust, string Note)? judged = null;
            try
            {
                judged = await _chat.AskFeelingDeltaAsync($"用户说：{Clip(text)}\nTA 回答：{Clip(displayReply)}");
            }
            catch (Exception ex) { App.WriteLog("MainGameViewModel.Judge -> " + ex.Message); }
            if (judged is not null)
            {
                Settle(judged.Value.Affection, judged.Value.Trust, 0);
                if (!string.IsNullOrWhiteSpace(judged.Value.Note))
                    App.WriteLog("MainGameViewModel.Feeling -> " + judged.Value.Affection + "/" + judged.Value.Trust + "：" + judged.Value.Note);
                return;
            }
        }

        // ③ 无 AI：不假装涨，只刷新大脑状态面板
        RefreshBrainStatus();
    }

    /// <summary>
    /// 动作类（摸头/拥抱/亲吻）没有 AI 回复可解析，走「兜底问一次 / 无 AI 不动」——数值仍由 AI 判，不写死。
    /// 关掉「数值由 AI 控制」时按 <paramref name="fixedAffection"/>/<paramref name="fixedTrust"/> 的固定增量结算（摸头 +2、拥抱 +3/+2、亲吻 +5/+3），精力不动。
    /// </summary>
    private async Task JudgeSceneAsync(string scene, int fixedAffection = 0, int fixedTrust = 0)
    {
        if (!_settings.Current.StatAiControl)
        {
            Settle(fixedAffection, fixedTrust, 0);
            return;
        }
        if (_api.HasAnyEndpoint)
        {
            try
            {
                // 判不动时返回 null（没配端点、答非所问）：那就一句不动，别拿「没判」冒充「判成 0」。
                var judged = await _chat.AskFeelingDeltaAsync(scene);
                if (judged is null)
                {
                    RefreshBrainStatus();
                    return;
                }
                Settle(judged.Value.Affection, judged.Value.Trust, 0);
                return;
            }
            catch (Exception ex) { App.WriteLog("MainGameViewModel.JudgeScene -> " + ex.Message); }
        }
        RefreshBrainStatus();
    }

    /// <summary>把 AI 判出来的增减落到三条数值上：好感/信任可涨可跌，精力只累进「AI 修正项」，
    /// 基线仍随清醒时长走（时间基线 + AI 修正）。好感的增减在 AwardAffection 里换成点数。</summary>
    private void Settle(int affectionDelta, int trustDelta, int energyDelta)
    {
        AwardAffection(affectionDelta);
        Trust = Math.Clamp(Trust + trustDelta, 0, 100);
        if (energyDelta != 0 && _engine.ActiveCharacter is { } ch)
        {
            ch.State.EnergyOffset = Math.Clamp(ch.State.EnergyOffset + energyDelta, -60, 40);
            UpdateEnergy(_time.Now().Now);
        }
        UpdateStats();
        RefreshBrainStatus();
        _ = AutoSave();
    }

    /// <summary>刷新信息面板「TA 现在什么心情」一句话；总开关关着就整行隐藏。</summary>
    private void RefreshBrainStatus()
    {
        var charId = _engine.State.CharacterId;
        if (string.IsNullOrEmpty(charId)) { BrainVisible = false; BrainStatusText = ""; return; }
        try
        {
            BrainVisible = _brain.IsVisible(charId);
            BrainStatusText = _brain.StatusText(charId);
        }
        catch (Exception ex)
        {
            BrainVisible = false; BrainStatusText = "";
            App.WriteLog("MainGameViewModel.BrainStatus -> " + ex.Message);
        }
    }

    private void UpdateTime()
    {
        var info = _time.Now();
        CurrentTimeStr = info.Now.ToString("HH:mm");
        TimeLabel = $"{info.Now:HH:mm}";
        DateLabel = $"{info.Now.Month}月{info.Now.Day}日 周{WeekCn[(int)info.Now.DayOfWeek]}";
        UpdateEnergy(info.Now);
        if (_map is { IsLoaded: true } && _map.CurrentScene is { } sc)
        {
            LocationLabel = $"{_map.Map.LocationNameOf(sc.Id)} · {sc.Name}";
        }
        else
        {
            LocationLabel = _engine.State.Location switch
            {
                "home" => "家",
                "park" => "公园",
                "cafe" => "咖啡厅",
                "school" => "学校",
                "mall" => "商场",
                _ => "家"
            };
        }
        // 周期跟着「当前角色的性别」走：换一个没有周期的角色，经期表述要当场消失，
        // 不能等到下次设置变更才纠正。
        RefreshCycle();
        // 时段变化(30 秒时钟)重算光照：带上最近一次真实天气，方向跟时间走、强度仍用能见度/云量，不被时间覆盖。
        ComputeLighting(_lastWeather);
    }

    /// <summary>
    /// 把设置里按性别存的「规则」换算成当前角色身上的「读数」（第几天、哪一期）。
    /// 商店的「身体状态」面板读的是 <see cref="CharacterState.Cycle"/>，这一步不跑，
    /// 用户改的周期就永远只躺在设置里，面板上那一行连出现都不会出现。
    /// </summary>
    private void RefreshCycle()
    {
        var ch = _engine.ActiveCharacter;
        var rule = _settings.Current.CycleRuleFor(ch?.Profile.Gender);
        _phys.SetCycle(rule);
        if (ch is null) return;

        var cycle = ch.State.Cycle;
        if (!rule.Enabled)
        {
            if (cycle.Tracked) ch.State.Cycle = cycle with { Tracked = false, Phase = "normal" };
            return;
        }

        // 起点是「上次经期的第一天」，首次跟踪时把今天记下来，之后按自然日累加。
        // 不能用 (now - 今天零点) 折算：那实际是「现在几点」，会让角色每天 0 点进入经期、
        // 14 点排卵、午夜清零，周期跟着钟表一天转一圈。
        var today = _time.Now().Now.Date;
        var anchor = cycle.LastPeriod == default ? today : cycle.LastPeriod.Date;
        // 长度按出厂区间夹一手：设置页写入时会夹，但存档 JSON 是用户能自己改的文件，
        // 这里 0 就会在每次时钟跳动时抛一次除零。
        var len = Math.Clamp(rule.CycleLength, 7, 60);
        var day = Math.Max(0, (int)(today - anchor).TotalDays) % len;
        _phys.Day = day;
        ch.State.Cycle = cycle with { Tracked = true, CycleDay = day, Phase = _phys.Phase, LastPeriod = anchor };
    }

    private static readonly string[] WeekCn = { "日", "一", "二", "三", "四", "五", "六" };

    // 无台账兜底路径用：记住上一次已叠加进 ch.State.Energy 的 EnergyOffset，
    // 之后只叠加「变化量」，避免把同一个修正每个时钟 tick 重复累加（那会伪装成空转掉精力）。
    private int _lastAppliedEnergyOffset;

    /// <summary>F16 精力值：由「睡眠 / 劳作 / 思考 / 说话」四项纯代码台账结算（宿主算、模组只显示），
    /// 再叠上 AI 对本次互动判的修正（EnergyOffset，有界 -60..40，随存档落盘）。
    /// 无台账（服务未注入）时用存档权威值 + 修正变化量兜底，绝不按清醒时长空转扣减。</summary>
    private void UpdateEnergy(DateTime now)
    {
        var ch = _engine.ActiveCharacter;
        var offset = ch?.State.EnergyOffset ?? 0;
        int value;
        // ChatEngine 是单例，台账由 RuntimeConfigurator 注入在它身上（避免给本 VM 的长构造再添参数）
        var ledger = _chat.Energy;
        if (ledger is not null && ch is not null)
        {
            value = ledger.Settle(ch.Profile.Id, now, offset);
            _lastAppliedEnergyOffset = offset;
        }
        else
        {
            // 无台账时不再按时间空转扣减：权威规则只允许真实行为（说话/思考/劳作/睡眠）改变精力。
            // 只叠加「修正量的变化」，避免每个时钟 tick 把同一个 EnergyOffset 重复累加。
            var delta = offset - _lastAppliedEnergyOffset;
            _lastAppliedEnergyOffset = offset;
            value = Math.Clamp((ch?.State.Energy ?? 100) + delta, 0, 100);
        }
        Energy = value;
        EnergyPct = value / 100.0;
        // 权威值写回角色状态：模组 stats 帧（PushStatsAsync）读的就是它，只显示不结算。
        if (ch is not null && ch.State.Energy != value) ch.State.Energy = value;
    }

}
