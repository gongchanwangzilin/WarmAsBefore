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
    [ObservableProperty] private int _affection = 30;
    [ObservableProperty] private int _affectionLevel = 1;
    [ObservableProperty] private string _affectionLevelTitle = "初识";
    // 好感累计积分（与 0-100 好感度门控正交）：HUD 用「阶 + 积分/5000」显示，
    // 不再把细分刻度压在满 100 上；门控仍读 Affection，绝不动。
    [ObservableProperty] private int _affectionPoints;
    /// <summary>HUD 好感显示：第N阶 · 称号 · 累计积分/5000（多级细分，不 clamp 到 100）。</summary>
    public string AffectionLevelLabel => $"第{AffectionLevel}阶 · {AffectionLevelTitle} · {AffectionPoints}/{Modules.Affection.AffectionLevel.MaxPoints}点";
    partial void OnAffectionLevelChanged(int value) => OnPropertyChanged(nameof(AffectionLevelLabel));
    partial void OnAffectionLevelTitleChanged(string value) => OnPropertyChanged(nameof(AffectionLevelLabel));
    partial void OnAffectionPointsChanged(int value)
    {
        OnPropertyChanged(nameof(AffectionLevelLabel));
        AffectionPct = Modules.Affection.AffectionLevel.LevelProgress(value);
    }
    [ObservableProperty] private int _trust = 30;
    [ObservableProperty] private int _attachment;
    [ObservableProperty] private int _balance = 1000;
    [ObservableProperty] private string _weatherDesc = "晴朗";
    [ObservableProperty] private bool _weatherBackgroundEnabled = true;
    [ObservableProperty] private bool _weatherBackgroundVisible;
    [ObservableProperty] private double _weatherBackgroundOpacity;
    [ObservableProperty] private string _weatherBackgroundColor = "#00000000";
    // ===== 统一背景系统：场景种类 / 可见性 / 音效 / 闪电信号（由 BackgroundService.Changed 同步） =====
    [ObservableProperty] private WeatherSceneKind _sceneKind = WeatherSceneKind.Sunny;
    [ObservableProperty] private bool _weatherSceneVisible = false;
    [ObservableProperty] private bool _sceneAudioEnabled = true;
    [ObservableProperty] private double _sceneAudioLevel = 0.6;
    /// <summary>闪电信号：每次打雷自增，视图监听它触发一次场景层闪光（不透明度/缩放动画由视图决定）。</summary>
    [ObservableProperty] private int _lightningFlashSignal;
    [ObservableProperty] private string _inputText = "";
    [ObservableProperty] private bool _isListening;
    /// <summary>启动编排（角色/地图/天气）全部收尾：页面用它掀开 LoadingVeil，就绪前先遮住还没初始化的舞台。</summary>
    [ObservableProperty] private bool _startupReady;
    // 入口进度卡：真实阶段进度（读档→立绘→地图→天气），由 RunStartup 里程碑驱动，禁假推进。
    [ObservableProperty] private bool _entryLoading = true;
    [ObservableProperty] private double _entryProgress;
    [ObservableProperty] private string _entryPhase = "准备进入";

    // ===== 场景光照：由「当前时间 + 真实天气的能见度/云量」算出，暴露给页面驱动 WeatherSceneLayer.SetLighting =====
    /// <summary>光源水平方向：-1 偏左(东/上午) → 1 偏右(西/下午)。</summary>
    [ObservableProperty] private double _sceneLightDirX = -0.7;
    /// <summary>光源仰角：0 近地平线，1 正当天顶（正午最高，夜间月光低仰角）。</summary>
    [ObservableProperty] private double _sceneLightElevation = 0.5;
    /// <summary>光照强度 0..1：由能见度 + 云量决定，夜间再压暗。</summary>
    [ObservableProperty] private double _sceneLightIntensity = 0.7;
    /// <summary>光柱/折射等光效开关：总开关关闭时为 false（基础照明仍保留）。</summary>
    [ObservableProperty] private bool _sceneLightEffectsEnabled = true;
    /// <summary>光照总开关：由统一背景配置下发；关闭后只关光柱/折射、保留基础光。</summary>
    [ObservableProperty] private bool _sceneLightEnabled = true;

    private void EntryStep(double progress, string phase)
    {
        EntryProgress = progress;
        EntryPhase = phase;
    }

    /// <summary>
    /// #10 待发送的文件附件（用户点了"带文件"后先挂在这里，随下一条消息一起发出）。
    /// 为空时发送路径与旧行为完全一致。
    /// </summary>
    public ObservableCollection<Models.ChatAttachment> PendingAttachments { get; } = new();
    /// <summary>待发附件数量标签（"带文件 · 2"），0 时隐藏。</summary>
    [ObservableProperty] private string _pendingAttachmentsLabel = "";
    /// <summary>是否有待发附件（控制按钮高亮/清除按钮显隐）。</summary>
    [ObservableProperty] private bool _hasPendingAttachments;
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
    // ============ 多角色位置：left / center / right ============
    [ObservableProperty] private string _spritePosition = "center";
    // ============ 说话者高亮 ============
    [ObservableProperty] private bool _isSpeaking;

    [ObservableProperty] private bool _isInMiniGame;
    // ============ 送礼 / 使用面板（主界面聊天时显示） ============
    [ObservableProperty] private bool _isGiftPanelVisible;
    [ObservableProperty] private string _giftPanelTitle = "送给小雨";
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
    // ============ 多角色单场景：气泡式开关 + 沉默提示 ============
    [ObservableProperty] private bool _useBubbleChat;
    [ObservableProperty] private string _silenceHintText = "";

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

    partial void OnWeatherBackgroundEnabledChanged(bool value)
    {
        if (!value)
        {
            WeatherBackgroundVisible = false;
            WeatherBackgroundOpacity = 0;
            WeatherBackgroundColor = "#00000000";
            return;
        }

        ApplyWeatherBackground(WeatherDesc);
    }

    partial void OnWeatherDescChanged(string value)
    {
        // 旧层（底色/透明度）照常跟随真实天气，避免大回归。
        ApplyWeatherBackground(value);
        // 新层：默认「手动选的场景优先」，真实天气不覆盖用户在设置里选的场景。
        // 只有显式打开开关（暂未暴露 UI，留作扩展点）时才让真实天气推给统一背景系统。
        if (_weatherSceneFollowsRealWeather) _background.ApplyWeatherDescription(value);
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

    // ---- 关闭 Galgame：WAB 只做我的世界的 AI / 通讯后端 ----
    [ObservableProperty] private bool _isBackendMode;
    [ObservableProperty] private string _backendStatusText = "";

    /// <summary>进主界面时刷新：设置可能在运行中被改过。</summary>
    public void RefreshBackendMode()
    {
        IsBackendMode = !_maidBridge.GalgameEnabled;
        BackendStatusText = _maidBridge.ConnectionSummary;
    }

    // ---- 标定闸门 ----
    /// <summary>当前场景没标定，界面被全屏遮罩拦住（不可关闭，必须选一个出口）。</summary>
    [ObservableProperty] private bool _isCalibrationBlocked;
    [ObservableProperty] private string _calibrationHint = "";
    /// <summary>用户选了「暂时用居中显示继续」：放行游玩，但界面常驻未标定警告。</summary>
    [ObservableProperty] private bool _isUncalibrated;
    [ObservableProperty] private string _uncalibratedHint = "";

    // ---- 标定驱动的角色变换 ----
    private ScenePlacement? _placement;
    private (int W, int H) _bgImageSize;
    private (int W, int H) _spritePixels;
    private double _stageW, _stageH;
    private string? _spriteAbsPath;
    /// <summary>当前立绘在 SpriteMap 里的键（存储相对路径）：立绘标定按它存取。</summary>
    private string? _spriteKey;
    private PortraitCalibration? _portraitCal;
    /// <summary>标定算出的缩放（说话强调在此之上乘系数，而不是覆盖它）。</summary>
    private double _calibratedScale = 1.0;
    /// <summary>标定算出的基准位移；动画在它之上做偏移，动画结束回到它。</summary>
    private double _baseSpriteX, _baseSpriteY;

    // ---- 新版着装（形象包） ----
    /// <summary>当前角色是否真的走形象包：开关开着、绑了包、包能读出来，三者缺一就回退 SpriteMap。</summary>
    private bool _figureActive;
    private FigurePack? _figurePack;
    private List<string> _figureOutfits = new();
    private List<string> _figureEmotions = new();
    /// <summary>连续切表情时只认最后一次合成结果，防止慢的那一张盖掉新的一张。</summary>
    private int _figureRenderVersion;
    /// <summary>静态立绘的缓存路径：走路动画结束后回到这一张，不重新合成。</summary>
    private string? _figureStaticPath;
    /// <summary>行走动作的取消源；每次 IsWalking 切换都换一代，旧循环不会把旧帧贴回来。</summary>
    private CancellationTokenSource? _walkMotionCts;
    /// <summary>上一次采样到的行走相位（0~1）。从待机起步时沿用，避免跳回第 0 帧。</summary>
    private double _walkPhase;
    private int _walkFrameIndex = -1;
    /// <summary>
    /// 行走帧的 ImageSource 缓存（键 "{motion.Id}:{帧号}:{宽度}"）。
    /// 循环回到同一帧时回赋同一个实例：setter 的相等检查直接短路，不触发 PropertyChanged，
    /// 也就不会让 Image 换源走一遍 Measure 失效——这是 GC 风暴与布局环的共犯。每次行走开始时清空。
    /// </summary>
    private readonly System.Collections.Generic.Dictionary<string, Microsoft.Maui.Controls.ImageSource> _walkFrameCache = new();
    /// <summary>主界面行走帧的落盘尺寸；手机端降到 720×1280，避免多帧同时解码吃爆内存。</summary>
    private const int WalkFrameDesktopWidth = 1080;
    private const int WalkFramePhoneWidth = 720;

    [ObservableProperty] private double _spriteFitW = 260;
    [ObservableProperty] private double _spriteFitH = 420;

    /// <summary>水平翻转（-1 = 镜像）：方向不对的素材由标定决定，而不是靠文件名约定。</summary>
    [ObservableProperty] private double _spriteFlipScaleX = 1;

    /// <summary>立绘标定问题（非空时主界面顶部给出提示与「去标定」入口）。</summary>
    [ObservableProperty] private string _spriteCalibrationIssue = "";
    partial void OnSpriteCalibrationIssueChanged(string value)
        => OnPropertyChanged(nameof(HasSpriteCalibrationIssue));
    public bool HasSpriteCalibrationIssue => !string.IsNullOrEmpty(SpriteCalibrationIssue);

    /// <summary>AI 主动黑屏：舞台全黑，但聊天与输入照常可用。</summary>
    [ObservableProperty] private bool _isBlackout;
    [ObservableProperty] private string _blackoutReason = "";

    // ===== 主动外发确认（角色要给你的手机发消息） =====

    /// <summary>待确认的主动消息文本（空 = 没有待确认的）。</summary>
    [ObservableProperty] private string _outboundPendingText = "";
    private string _outboundPendingId = "";
    public bool HasOutboundPending => !string.IsNullOrEmpty(OutboundPendingText);
    partial void OnOutboundPendingTextChanged(string value) => OnPropertyChanged(nameof(HasOutboundPending));

    private void RefreshOutbound()
    {
        var first = _bridge.PendingOutbound.FirstOrDefault();
        _outboundPendingId = first?.Id ?? "";
        OutboundPendingText = first?.Text ?? "";
    }

    [RelayCommand]
    private async Task ConfirmOutbound()
    {
        var id = _outboundPendingId;
        if (string.IsNullOrEmpty(id)) return;
        var ok = await _bridge.ConfirmOutboundAsync(id);
        if (!ok) Toast.Warn("发送失败：可能是超过 48 小时没互动，QQ 不允许机器人主动发起会话；让对方先给机器人发条消息即可。");
        RefreshOutbound();
    }

    [RelayCommand]
    private void CancelOutbound()
    {
        if (string.IsNullOrEmpty(_outboundPendingId)) return;
        _bridge.CancelOutbound(_outboundPendingId);
        RefreshOutbound();
    }

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

    // ==================== 单例服务事件挂/摘（审计 A4） ====================
    // VM 是 Transient，而 Map/SceneDirector/Settings/Automation/Blackout 都是 Singleton：
    // 订阅后不解绑，每进出一次主界面就叠一份，表现为「切一次场景触发多次 ApplyScene」
    // 与内存持续上涨。页面 OnAppearing 调 Attach、OnDisappearing 调 Detach。
    private bool _runtimeEventsAttached;
    private bool _mapEventsAttached;

    private void OnBlackoutChanged(bool on, string reason) =>
        Helpers.Ui.Post(() => { IsBlackout = on; BlackoutReason = reason; });

    private void HookMapEvents()
    {
        if (_mapEventsAttached) return;
        _mapEventsAttached = true;
        _map.SceneChanged += OnMapSceneChanged;
        _map.WalkSegmentStarted += OnWalkSegmentStarted;
        _map.WalkRouteCompleted += OnWalkRouteCompleted;
        _map.MapChanged += OnMapChanged;
    }

    private void UnhookMapEvents()
    {
        if (!_mapEventsAttached) return;
        _mapEventsAttached = false;
        _map.SceneChanged -= OnMapSceneChanged;
        _map.WalkSegmentStarted -= OnWalkSegmentStarted;
        _map.WalkRouteCompleted -= OnWalkRouteCompleted;
        _map.MapChanged -= OnMapChanged;
    }

    /// <summary>页面进场调用；重复调用无副作用。</summary>
    public void AttachRuntimeEvents()
    {
        // 进主界面（含从设置页修好 API 后返回）：解除 API 错误对被动 AI 的封停。
        Services.Core.BugReporter.Reset("AI");
        if (_runtimeEventsAttached) return;
        _runtimeEventsAttached = true;
        if (_map.IsLoaded) HookMapEvents();
        _sceneDirector.ModeApplied += OnSceneModeApplied;
        _sceneDirector.WeatherEventTriggered += OnWeatherEvent;
        _settings.Applied += SyncWeatherEventConfig;
        _auto.GreetingReady += OnGreet;
        _blackout.Changed += OnBlackoutChanged;
        // 统一背景配置变化 → 同步场景/音效到 VM（服务内部已用 Ui.Post 切回 UI 线程）。
        // 订阅必须走 Attach/Detach（审计 A4）：VM 是 Transient、BackgroundService 是 Singleton，
        // 放构造里会每进出一次主界面叠一份，这里随页面进出场挂摘。
        _background.Changed += OnBackgroundChanged;
        // API 错误独立通道：ChatEngine 把端点失败走这里，不再伪装成 AI 台词。
        // 收到后红色 Toast + 封停被动 AI（不再走 BugReporter 分析窗），错误本身绝不进聊天气泡。
        _chat.ErrorOccurred += OnApiError;
        // 测试版总开关可能在设置页里改过：那时本 VM 已随离场退订 Changed，收不到通知。
        // 进场重订阅后必须主动重读一次当前配置，否则天气氛围层的 IsVisible 绑定会停在旧值
        // （雨层由页面 OnAppearing 自己同步，底层场景与音效靠这里跟上）。同步是幂等的。
        SyncSceneFromBackground(_background.Current);
        _clock?.Start();
    }

    /// <summary>页面离场调用；把 Singleton 上的订阅全部摘掉，界面时钟一并停走。</summary>
    public void DetachRuntimeEvents()
    {
        if (!_runtimeEventsAttached) return;
        _runtimeEventsAttached = false;
        UnhookMapEvents();
        _sceneDirector.ModeApplied -= OnSceneModeApplied;
        _sceneDirector.WeatherEventTriggered -= OnWeatherEvent;
        _settings.Applied -= SyncWeatherEventConfig;
        _auto.GreetingReady -= OnGreet;
        _blackout.Changed -= OnBlackoutChanged;
        _background.Changed -= OnBackgroundChanged;
        _chat.ErrorOccurred -= OnApiError;
        _clock?.Stop();
    }

    /// <summary>
    /// API 调用失败：红色 Toast 立即告知，并封停「AI」子系统，暂停一切被动 AI 调用
    /// （主动对话 / 心跳 AI / 后台生成；各调用点须检查 BugReporter.IsStopped("AI") 跳过），
    /// 直到下次进存档 / 切存档 / 新建角色 / 用户重新指定 AI 时 Reset("AI")。
    /// 这是可恢复的请求级错误，不再走 BugReporter.Report 异常管线（不弹分析窗、不写 bugs.log）；
    /// 真正的非 API 异常仍照常由各子系统 Report。message 是网关编码的错误文本，绝不写进会话或记忆。
    /// </summary>
    private void OnApiError(string charId, string message)
    {
        // 先立即封停，再弹提示：即便 UI 线程正忙，后台的被动 AI 也不会继续排队重试/烧 token。
        Services.Core.BugReporter.Stop("AI");
        Helpers.Ui.Post(() =>
        {
            try
            {
                var who = _engine.Roster.TryGetValue(charId, out var ch) ? ch.Profile.Name : "角色";
                Controls.Toast.Error($"AI 暂时连不上（{who}）：{ShortApiError(message)}。请检查 API 设置，重新进存档或换一条端点后恢复。");
            }
            catch (Exception ex) { App.WriteLog("MainGameViewModel.OnApiError -> " + ex.Message); }
        });
    }

    /// <summary>识别网关的错误串：这些前缀是 ApiGateway/ChatEngine 对失败的编码，不是 AI 台词。</summary>
    private static bool IsApiErrorText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var t = text.TrimStart();
        return t.StartsWith("[API错误", StringComparison.Ordinal)
            || t.StartsWith("[网络连接失败", StringComparison.Ordinal)
            || t.StartsWith("[请求超时", StringComparison.Ordinal)
            || t.StartsWith("[请求排队超时", StringComparison.Ordinal);
    }

    /// <summary>错误文本截短给 Toast（分析窗里保留完整原文）。</summary>
    private static string ShortApiError(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return "未知错误";
        var one = message.Replace("\r", " ").Replace("\n", " ").Trim();
        return one.Length <= 60 ? one : one[..60] + "…";
    }

    /// <summary>统一背景配置变化回调：服务已在 UI 线程发事件，这里再做一次 Post 兜底线程纪律。</summary>
    private void OnBackgroundChanged(BackgroundConfig cfg)
        => Helpers.Ui.Post(() => SyncSceneFromBackground(cfg));

    /// <summary>
    /// 把统一配置里的场景种类 / 可见性 / 音效 / 光照开关同步到 VM 展示属性，
    /// 并把音效参数下发给 WeatherAudioService（启动与 Changed 订阅共用同一条路径）。
    /// </summary>
    private void SyncSceneFromBackground(BackgroundConfig cfg)
    {
        SceneKind = cfg.Scene;
        WeatherSceneVisible = cfg.ExperimentalEnabled && cfg.Mode == BackgroundMode.Weather;
        SceneAudioEnabled = cfg.AudioEnabled;
        SceneAudioLevel = cfg.AudioLevel;
        SceneLightEnabled = cfg.SceneLightEnabled;
        _weatherAudio.Apply(cfg.Scene, cfg.ExperimentalEnabled && cfg.AudioEnabled, cfg.AudioLevel);
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

    /// <summary>承载背景与立绘的容器尺寸变化（页面 SizeChanged 转发）。</summary>
    public void OnStageSizeChanged(double w, double h, bool isPhone)
    {
        // 桌面版与手机版两条上报路径都汇到这里：统一挡一次非有限值。
        // NaN 与任何数比较都为 false，只写 <= 0 会把它放过去，随后 UpdateSpriteTransform
        // 会把 SpriteFitW/H / SpriteScale 算成 NaN 写回立绘的 WidthRequest / Scale。
        if (!double.IsFinite(w) || !double.IsFinite(h) || w <= 0 || h <= 0) return;
        _stageW = w;
        _stageH = h;
        _isPhoneStage = isPhone;
        UpdateSpriteTransform();
    }

    private bool _isPhoneStage = true;

    /// <summary>取地图场景背景图与立绘的原图像素尺寸，然后重算立绘变换。</summary>
    private async Task RefreshSpriteGeometryAsync(MapScene? scene)
    {
        var bgAbs = scene is null ? null : _map.ResolveBackground(scene);
        if (!string.IsNullOrEmpty(bgAbs) && MapService.IsVideoExt(Path.GetExtension(bgAbs)))
            bgAbs = scene is null ? null : _map.ThumbnailAbsFor(scene);
        await RefreshSpriteGeometryForAsync(bgAbs, scene?.Placement);
    }

    /// <summary>按"任意背景路径 + 标定数据"重算立绘变换（场景模式覆盖背景时走这条）。</summary>
    private async Task RefreshSpriteGeometryForAsync(string? bgAbs, ScenePlacement? placement)
    {
        _placement = placement;
        _bgImageSize = await ImageProbe.SizeOfAsync(
            !string.IsNullOrEmpty(bgAbs) && File.Exists(bgAbs) ? bgAbs : null);
        _spritePixels = await ImageProbe.SizeOfAsync(_spriteAbsPath);
        await RefreshPortraitCalibrationAsync();
        UpdateSpriteTransform();
    }

    /// <summary>
    /// 取当前立绘的标定，并判断它是否需要进调试界面。
    /// 不是 1080×1920 逻辑画布的素材会被标记出来（主界面给「去标定」入口），
    /// 但不会被静默拉伸——大小与底部对齐只认标定结果。
    /// </summary>
    private async Task RefreshPortraitCalibrationAsync()
    {
        var key = _spriteKey ?? _spriteAbsPath ?? "";
        _portraitCal = await _portraits.GetAsync(key);

        List<string> problems;
        try { problems = await _portraits.ProblemsAsync(key, _spritePixels.W, _spritePixels.H); }
        catch (Exception ex)
        {
            App.WriteLog("MainGameVM.PortraitCalibration -> " + ex.Message);
            problems = new List<string>();
        }
        SpriteCalibrationIssue = problems.Count > 0 ? problems[0] : "";
    }

    /// <summary>
    /// 场景模式换背景时同步换标定。
    ///
    /// 规格里这块是"可以选择沿用或单图微调"，两种都支持：
    ///   · <see cref="Models.SceneMode.Placement"/> 有值 = 这张模式背景自己标过（单图微调）；
    ///   · 为空 = 沿用所属库条目引用的地图场景的标定。
    /// </summary>
    private void ApplyModePlacement(Models.SceneLibraryEntry entry, Models.SceneMode? mode)
    {
        var placement = mode?.Placement;
        if (placement is null && !string.IsNullOrEmpty(entry.MapSceneRef))
            placement = _map.Map.SceneById(entry.MapSceneRef!)?.Placement;

        var bgAbs = mode?.Background is { Length: > 0 } mb ? mb : entry.Background;
        _ = RefreshSpriteGeometryForAsync(bgAbs, placement);
    }

    /// <summary>
    /// 由标定数据算出立绘的尺寸、缩放与位移。
    ///
    /// 关键点是背景用的是 AspectFill（会裁切），而标定坐标是**原图归一化**坐标，
    /// 所以必须先把归一化坐标映射到 AspectFill 之后的实际显示矩形上，
    /// 否则竖图放横屏时会整体偏掉。
    /// </summary>
    private void UpdateSpriteTransform()
    {
        if (_stageW <= 0 || _stageH <= 0) return;

        // 立绘基准尺寸：按「标定后的有效像素」AspectFit 进容器。
        // 用有效尺寸而不是原始像素，才能让 1080×1920 的画布语义对任何素材都成立
        // （裁剪修掉留白、旋转修正方向后，角色的实际占比才是对的）。
        var (effW, effH) = PortraitCalibrationMath.EffectiveSize(_spritePixels.W, _spritePixels.H, _portraitCal);
        var sw = effW > 0 ? effW : 400.0;
        var sh = effH > 0 ? effH : 640.0;
        var fit = Math.Min(_stageW / sw, _stageH / sh);
        var baseW = sw * fit;
        var baseH = sh * fit;
        // SpriteFitW/H 仍供手机端 XAML 绑定与行走动画步长使用；桌面立绘改由 AspectFit 按舞台自适应，
        // 不再写回布局测量。本方法只产出渲染变换值（Scale/Translation/Opacity/Flip），不参与父容器 Measure。
        SpriteFitW = baseW;
        SpriteFitH = baseH;

        // 没有可用标定：退回旧的居中显示，**不凭空推断位置与缩放**
        if (_placement is null || !PlacementMath.Validate(_placement).Ok)
        {
            _calibratedScale = 1.0;
            _baseSpriteX = 0;
            _baseSpriteY = 0;
            SpriteX = 0;
            SpriteY = 0;
            ApplyPortraitCalibrationOffsets(baseH);
            ApplySpeakingEmphasis();
            return;
        }

        var desired = DesiredNormalized(baseW);
        var (anchor, k) = PlacementMath.ResolveAnchor(_placement, desired);

        // 标定给出的 k 是"相对基准大小"，乘上参考身高占比与容器高得到目标像素高
        var visibleH = _stageH * _placement.RefHeightRatio * k;
        _calibratedScale = Math.Clamp(visibleH / baseH, 0.02, 8.0);
        ApplySpeakingEmphasis();

        // AspectFill 映射：算出原图在容器里实际被铺成了多大、偏移多少
        var iw = _bgImageSize.W > 0 ? _bgImageSize.W : 16.0;
        var ih = _bgImageSize.H > 0 ? _bgImageSize.H : 9.0;
        var s = Math.Max(_stageW / iw, _stageH / ih);
        var dw = iw * s;
        var dh = ih * s;
        var ox = (_stageW - dw) / 2;
        var oy = (_stageH - dh) / 2;

        _baseSpriteX = ox + anchor.X * dw - _stageW / 2;
        // 让"脚底"落在锚点上：立绘实际显示高度是 baseH * Scale
        _baseSpriteY = oy + anchor.Y * dh - _stageH / 2 - baseH * SpriteScale / 2;

        ApplyPortraitCalibrationOffsets(baseH);

        SpriteX = _baseSpriteX;
        SpriteY = _baseSpriteY;
    }

    /// <summary>
    /// 把立绘标定叠加到基准变换上：方向（镜像）、溢出幅度（缩放）、着色偏移、
    /// 以及「底部对齐」——脚底线不在图像底边时，把图整体下移，让脚踩在锚点上。
    /// </summary>
    private void ApplyPortraitCalibrationOffsets(double baseH)
    {
        SpriteFlipScaleX = 1;
        if (_portraitCal is not { Calibrated: true } pc) return;

        SpriteFlipScaleX = pc.FlipHorizontal ? -1 : 1;
        _calibratedScale = Math.Clamp(_calibratedScale * pc.Scale, 0.02, 8.0);
        ApplySpeakingEmphasis();

        // 底部对齐：脚底线归一化高度 footLine 处的像素要落在锚点上
        _baseSpriteY += baseH * SpriteScale * (1 - pc.FootLine);
        _baseSpriteX += pc.OffsetX * _stageW;
        _baseSpriteY += pc.OffsetY * _stageH;
    }

    /// <summary>把 left/center/right 换算成归一化落脚位置——按立绘宽度成比例，而不是写死 150px。</summary>
    private NormPoint DesiredNormalized(double baseW)
    {
        var iw = _bgImageSize.W > 0 ? _bgImageSize.W : 16.0;
        var ih = _bgImageSize.H > 0 ? _bgImageSize.H : 9.0;
        var dw = iw * Math.Max(_stageW / iw, _stageH / ih);
        var dir = SpritePosition switch { "left" => -1.0, "right" => 1.0, _ => 0.0 };
        var nx = dw > 0 ? 0.5 + dir * baseW * 1.05 / dw : 0.5;
        return new NormPoint(Math.Clamp(nx, 0.02, 0.98), 0.9);
    }

    /// <summary>说话强调：乘在标定缩放之上，而不是把它覆盖掉。</summary>
    private void ApplySpeakingEmphasis() => SpriteScale = _calibratedScale * (IsSpeaking ? 1.0 : 0.95);

    /// <summary>
    /// 标定闸门。
    ///
    /// 落到哪个位置、画多大，全靠标定数据算出来；没有标定就不能凭空生成角色位置，
    /// 所以这里弹一个不可关闭的遮罩。
    ///
    /// 但**必须留出口**：用户完全可能只是选错了一张图、想换一张，此时若不给路走就会被永久卡住。
    /// 三个出口分别是「去标定」「移除这张背景」「暂时用居中显示继续」。
    /// </summary>
    private void UpdateCalibrationGate(MapScene scene)
    {
        if (!_cal.NeedsCalibration(scene))
        {
            ClearCalibrationGate();
            return;
        }
        CalibrationHint = $"「{scene.Name}」的背景还没有标定，无法确定角色该站在哪、应该多大。\n\n"
                          + _cal.DescribeMissing(scene);
        IsCalibrationBlocked = true;
    }

    private void ClearCalibrationGate()
    {
        IsCalibrationBlocked = false;
        CalibrationHint = "";
        IsUncalibrated = false;
        UncalibratedHint = "";
    }

    /// <summary>出口①：去标定。标完回来重跑 ApplyScene 解除遮罩。</summary>
    [RelayCommand]
    private async Task GoCalibrateAsync()
    {
        if (_currentScene is null) return;
        var ok = await _cal.EnsureCalibratedAsync(_currentScene, SceneCalibrationService.DefaultTargetAspect);
        if (ok) ApplyScene(_currentScene);
    }

    /// <summary>
    /// 出口②：移除这张背景。场景退回纯色 —— 没有图也就不需要标定，
    /// 这是合法出口而不是"跳过标定"（跳过标定指的是带着图却不标）。
    /// </summary>
    [RelayCommand]
    private async Task RemoveSceneBackgroundAsync()
    {
        var scene = _currentScene;
        if (scene is null) return;
        scene.Background = "";
        scene.Placement = null;
        await _map.SaveAsync();
        _map.NotifyChanged();
        ApplyScene(scene);
    }

    /// <summary>出口③：暂时用居中显示继续玩。放行但常驻警告，直到补上标定。</summary>
    [RelayCommand]
    private void ContinueCentered()
    {
        IsCalibrationBlocked = false;
        IsUncalibrated = true;
        UncalibratedHint = $"「{_currentScene?.Name}」背景未标定，角色位置已回退为居中显示，点此补标定";
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

    /// <summary>
    /// 上一次应用过的天气来源指纹：mode|城市|坐标。
    /// NotifyChanged 不只天气设置会发（改标定、装素材包都会发），而每次应用都要联网拉一次天气，
    /// 所以先比指纹，没变就不重复请求、也不重复刷 SceneDirector 的天气门控。
    /// </summary>
    private string _mapWeatherKey = "";
    private bool _mapWeatherApplied;

    /// <summary>
    /// 把「这张图按哪儿算天气」告诉 <see cref="WeatherProvider"/>：
    /// user = 跟随用户（清掉覆盖）；map = 用抓取位置的城市 / 中心坐标；custom = 用户填的地点。
    /// 之后立刻重拉一次天气并刷新场景天气门控 —— 否则主界面会继续显示上一张图那座城市的天气。
    /// </summary>
    private void ApplyMapWeather()
    {
        try
        {
            if (!_map.IsLoaded) return;
            var map = _map.Map;
            var mode = MapWeatherModes.Normalize(map.WeatherMode);
            var city = mode == MapWeatherModes.Map
                ? (!string.IsNullOrWhiteSpace(map.WeatherCity) ? map.WeatherCity : map.City)
                : mode == MapWeatherModes.Custom ? map.WeatherCity : "";
            var key = string.Create(CultureInfo.InvariantCulture,
                $"{mode}|{city}|{map.CenterLatitude:0.######}|{map.CenterLongitude:0.######}");
            if (_mapWeatherApplied && key == _mapWeatherKey) return;
            _mapWeatherKey = key;
            _mapWeatherApplied = true;

            switch (mode)
            {
                case MapWeatherModes.Map:
                {
                    // 0,0 是「这张图没有抓取位置」的默认值，不能当坐标拿去查天气。
                    var hasCoord = Math.Abs(map.CenterLatitude) > 1e-6 && Math.Abs(map.CenterLongitude) > 1e-6;
                    _weather.SetMapWeather(MapWeatherModes.Map, city,
                        hasCoord ? map.CenterLatitude : null,
                        hasCoord ? map.CenterLongitude : null);
                    break;
                }
                case MapWeatherModes.Custom:
                    // 自定义地点只有名字：坐标交给 WeatherProvider 首次拉天气时自己地理编码。
                    _weather.SetMapWeather(MapWeatherModes.Custom, city, null, null);
                    break;
                default:
                    _weather.ClearMapWeather();
                    break;
            }

            App.WriteLog($"MainGame.ApplyMapWeather -> mode={mode}, override={_weather.HasMapWeatherOverride}, city={city}");
            _ = FetchWeather();
            _ = _sceneDirector.RefreshWeatherNowAsync();
        }
        catch (Exception ex)
        {
            // 天气是锦上添花：这里出错只记日志，绝不能把主界面拖垮。
            App.WriteLog("MainGame.ApplyMapWeather -> " + ex);
        }
    }

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

    /// <summary>地图 overlay 里点场景直接走过去。</summary>
    [RelayCommand]
    private async Task WalkToScene(string sceneId)
    {
        if (string.IsNullOrEmpty(sceneId) || sceneId == _map.CurrentSceneId) return;
        try
        {
            IsWalking = true;
            var walk = await _map.MoveToAsync(sceneId);
            if (!string.IsNullOrWhiteSpace(walk)) AddMessage("assistant", walk);
        }
        catch (Exception ex) { App.WriteLog("MainGame.WalkToScene -> " + ex); }
        finally { IsWalking = false; }
    }

    // ============ 左右切换地点（#9） ============

    /// <summary>当前可切换的地点数量（不含当前所在）。为 0 时左右按键置灰。</summary>
    [ObservableProperty] private int _sceneSwitchCount;
    /// <summary>是否存在可切换的目标地点；false 时左右切换按键全部置灰。</summary>
    [ObservableProperty] private bool _hasSceneNeighbors;
    /// <summary>左键是否可用；无目标时为 false（置灰）。</summary>
    [ObservableProperty] private bool _canGoPrevScene;
    /// <summary>右键是否可用；无目标时为 false（置灰）。</summary>
    [ObservableProperty] private bool _canGoNextScene;
    /// <summary>切换按钮上显示的「上一个地点」名（用于提示，可空）。</summary>
    [ObservableProperty] private string _prevSceneLabel = "";
    /// <summary>切换按钮上显示的「下一个地点」名（用于提示，可空）。</summary>
    [ObservableProperty] private string _nextSceneLabel = "";

    /// <summary>当前在 SceneOptions 中的索引；找不到时取 0。</summary>
    private int CurrentSceneIndex()
    {
        var id = _map.CurrentSceneId;
        for (var i = 0; i < SceneOptions.Count; i++)
            if (SceneOptions[i].SceneId == id) return i;
        return SceneOptions.Count > 0 ? 0 : -1;
    }

    /// <summary>
    /// 刷新左右切换按钮的可用态与相邻地点名。呼据点：
    /// 场景列表变化、走到新地点、存档载入 —— 只要地点集合或当前位置变了就要重算，否则按钮状态会滞后。
    /// </summary>
    private void RefreshSceneSwitch()
    {
        try
        {
            var n = SceneOptions.Count;
            SceneSwitchCount = n;
            if (n <= 1)
            {
                // 只有一个（或零个）地点：没有可切的目标，左右都置灰。
                HasSceneNeighbors = false;
                CanGoPrevScene = false;
                CanGoNextScene = false;
                PrevSceneLabel = NextSceneLabel = "";
                return;
            }
            var idx = CurrentSceneIndex();
            if (idx < 0)
            {
                HasSceneNeighbors = false; CanGoPrevScene = false; CanGoNextScene = false;
                PrevSceneLabel = NextSceneLabel = ""; return;
            }
            // 循环切换：首尾相接，永远有相邻项（因为 n>1）。
            var prev = SceneOptions[(idx - 1 + n) % n];
            var next = SceneOptions[(idx + 1) % n];
            HasSceneNeighbors = true;
            CanGoPrevScene = true;
            CanGoNextScene = true;
            PrevSceneLabel = prev.Label;
            NextSceneLabel = next.Label;
        }
        catch (Exception ex) { App.WriteLog("MainGame.RefreshSceneSwitch -> " + ex); }
    }

    /// <summary>切换到上一个地点（首尾循环）。无目标时静默忽略。</summary>
    [RelayCommand]
    private async Task PrevSceneAsync()
    {
        var n = SceneOptions.Count;
        if (n <= 1) return;
        var idx = CurrentSceneIndex();
        if (idx < 0) return;
        await WalkToScene(SceneOptions[(idx - 1 + n) % n].SceneId);
    }

    /// <summary>切换到下一个地点（首尾循环）。无目标时静默忽略。</summary>
    [RelayCommand]
    private async Task NextSceneAsync()
    {
        var n = SceneOptions.Count;
        if (n <= 1) return;
        var idx = CurrentSceneIndex();
        if (idx < 0) return;
        await WalkToScene(SceneOptions[(idx + 1) % n].SceneId);
    }

    /// <summary>好感/信任变化时同步回角色状态，保证自动保存真正落盘。</summary>
    partial void OnAffectionChanged(int value) => SyncStatsToState();
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
    /// 累计好感积分（独立于 0-100 好感度）：跨过整 500 边界时触发好感等级提升动画。
    /// 开关：设置 › 好感度 › 等级提升动画。
    /// </summary>
    private void AddAffectionPoints(int delta, bool withAnimation)
    {
        if (_char is null) return;
        var before = _char.State.AffectionPoints;
        var after = Math.Min(Modules.Affection.AffectionLevel.MaxPoints, before + Math.Max(0, delta));
        _char.State.AffectionPoints = after;
        AffectionPoints = after;

        var newLevel = Modules.Affection.AffectionLevel.LevelOf(after);
        if (AffectionLevel != newLevel)
        {
            AffectionLevel = newLevel;
            AffectionLevelTitle = Modules.Affection.AffectionLevel.TitleOf(newLevel);
        }
        if (withAnimation
            && after > 0
            && newLevel > Modules.Affection.AffectionLevel.LevelOf(before)
            && _settings.Current.AffectionLevelUpEnabled)
        {
            TriggerAffectionLevelUp(newLevel);
        }
    }

    /// <summary>好感等级提升：在后台组装动画页数据，主线程导航播放。</summary>
    private void TriggerAffectionLevelUp(int newLevel)
    {
        if (_char is null) return;
        try
        {
            _levelUp.Level = newLevel;
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
        Affection = ch.State.Affection;
        Trust = ch.State.Trust;
        Balance = Math.Max(0, ch.State.Energy);
        AffectionLevel = Modules.Affection.AffectionLevel.LevelOf(ch.State.AffectionPoints);
        AffectionLevelTitle = Modules.Affection.AffectionLevel.TitleOf(AffectionLevel);
        AffectionPoints = ch.State.AffectionPoints;
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

    private void SetEmotion(string emotion)
    {
        if (_char is null) return;
        _currentEmotion = _figureActive
            ? (_figureEmotions.Contains(emotion, StringComparer.OrdinalIgnoreCase) ? emotion : _defaultEmotion)
            : (_char.SpriteMap.ContainsKey($"{_outfitKey}/{emotion}") ? emotion : _defaultEmotion);
        // 表情也要写回角色状态，随存档持久化；否则下次进来又回到初次的表情
        _char.State.CurrentOutfit = _outfitKey;
        _char.State.CurrentEmotion = _currentEmotion;
        ApplySprite();
    }

    /// <summary>
    /// 选图顺序：新版形象包（开关开着、绑了包、包能读出来）优先；否则走旧的 SpriteMap。
    /// 合成失败时同一次调用立刻回退旧路径，屏幕不会空着——旧版换装始终可用。
    /// </summary>
    private void ApplySprite()
    {
        if (_char is null || string.IsNullOrEmpty(_outfitKey)) { SpriteVisible = false; return; }
        if (_figureActive)
        {
            _ = ApplyFigurePackSpriteAsync();
            return;
        }
        // 新版分支已经失效时，别让旧的行走循环继续往舞台上贴合成帧。
        StopWalkMotion(restoreStatic: false);
        ApplySpriteMap();
    }

    private void ApplySpriteMap()
    {
        if (_char is null || string.IsNullOrEmpty(_outfitKey)) { SpriteVisible = false; return; }
        var key = $"{_outfitKey}/{_currentEmotion}";
        if (!_char.SpriteMap.TryGetValue(key, out var rel))
            rel = _char.SpriteMap.ContainsKey($"{_outfitKey}/{_defaultEmotion}")
                ? _char.SpriteMap[$"{_outfitKey}/{_defaultEmotion}"]
                : _char.SpriteMap.Values.FirstOrDefault();
        _spriteKey = rel;   // 立绘标定按 SpriteMap 的存储路径存取，与角色库/素材库共用同一份
        string? full = null;
        if (!string.IsNullOrEmpty(rel))
        {
            // 候选路径和工作室、好感动画共用同一份（SpritePathResolver）：
            // 各处各猜各的，就会出现「同一张图这一屏有、那一屏没有」。
            var candidates = Modules.Affection.SpritePathResolver.Candidates(rel, _store.Root);
            full = candidates
                .Where(p => !string.IsNullOrEmpty(p) && File.Exists(p))
                .FirstOrDefault();
            if (full is null)
                App.WriteLog($"ApplySprite: 未找到立绘 {rel}, 尝试了 {candidates.Count} 条路径");
        }
        if (string.IsNullOrEmpty(full)) { SpriteVisible = false; return; }
        // Ui.Post 在 UI 线程上同步执行，与原先两分支的行为一致。
        Helpers.Ui.Post(() => _ = ApplySpriteCoreAsync(full));
    }

    /// <summary>
    /// 读形象包的服装轴与表情轴。失败不抛：调用方据此回退 SpriteMap。
    /// 表情键来自部件的 EmotionTextures；包没给表情时至少保留 normal，合成器按基础表情出图。
    /// </summary>
    private async Task<bool> TryLoadFigurePackAsync(CharacterData ch)
    {
        try
        {
            if (FigureCompat.ShouldFallbackToSpriteMap(_settings, ch)) return false;
            var pack = await _figures.LoadAsync(ch.FigurePackId);
            if (pack is null) return false;

            _figurePack = pack;
            _figureOutfits = pack.Outfits
                .Select(o => o.Id)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (_figureOutfits.Count == 0) _figureOutfits.Add("default");

            _figureEmotions = new List<string> { "normal" };
            foreach (var part in pack.Parts)
            {
                foreach (var emotion in part.EmotionTextures.Keys)
                {
                    if (string.IsNullOrWhiteSpace(emotion)) continue;
                    if (!_figureEmotions.Contains(emotion, StringComparer.OrdinalIgnoreCase))
                        _figureEmotions.Add(emotion);
                }
            }
            return true;
        }
        catch (Exception ex)
        {
            App.WriteLog("MainGameVM.TryLoadFigurePack -> " + ex);
            return false;
        }
    }

    /// <summary>
    /// 新版着装出图：按当前服装/表情合成一张 1080×1920 的透明 PNG。
    /// 手机上同样走这张 PNG（不做透明视频解码）——合成一次，主界面、桌宠、工作室共用缓存。
    /// </summary>
    private async Task ApplyFigurePackSpriteAsync()
    {
        var ch = _char;
        if (ch is null || !_figureActive) return;
        var version = ++_figureRenderVersion;
        try
        {
            var outfit = _figureOutfits.FirstOrDefault(o =>
                string.Equals(o, _outfitKey, StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrWhiteSpace(outfit) && _figurePack is { Outfits.Count: > 0 })
                outfit = _figurePack.Outfits[0].Id;

            var emotion = _figureEmotions.FirstOrDefault(e =>
                string.Equals(e, _currentEmotion, StringComparison.OrdinalIgnoreCase)) ?? "normal";

            var path = await _figures.RenderAsync(
                ch.FigurePackId, outfit ?? "", "", emotion, 1080, 1920);

            if (version != _figureRenderVersion
                || _char is null
                || !string.Equals(ch.Profile.Id, _char.Profile.Id, StringComparison.Ordinal))
                return;

            if (string.IsNullOrWhiteSpace(path))
            {
                App.WriteLog("MainGameVM.ApplyFigurePack: 合成失败，回退 SpriteMap -> " + ch.FigurePackId);
                _figureActive = false;
                StopWalkMotion(restoreStatic: false);
                ApplySpriteMap();
                return;
            }

            // 合成图本来就是 1080×1920 标准画布；用缓存路径做标定键，改素材后换新键，
            // 不会把旧图的偏移套到新图上。
            _figureStaticPath = path;
            _spriteKey = path;
            // 行走中换服装/表情：静态图先备好，再让行走循环用新参数重新取帧；
            // 不把当前行走帧盖成静态图，否则每换一次表情都会闪一下。
            if (IsWalking)
            {
                StartWalkMotion();
                return;
            }
            // Ui.PostAsync 在 UI 线程上就地执行并返回原 Task，与原先两分支的 await 行为一致。
            await Helpers.Ui.PostAsync(() => ApplySpriteCoreAsync(path));
        }
        catch (Exception ex)
        {
            App.WriteLog("MainGameVM.ApplyFigurePack -> " + ex);
            if (version == _figureRenderVersion)
            {
                _figureActive = false;
                StopWalkMotion(restoreStatic: false);
                ApplySpriteMap();
            }
        }
    }

    private async Task ApplySpriteCoreAsync(string full)
    {
        SpriteLoadingProgress = 0.1;
        StatusText = "加载立绘...";
        await Task.Delay(50);
        try
        {
            SpriteSource = ImageSource.FromFile(full);
            SpriteVisible = true;
            _spriteAbsPath = full;
            SpriteOpacity = IsSpeaking ? 1.0 : 0.5;
            // 位置与缩放一律由标定算出来；没有标定时回退居中显示。
            // 这里不再有"-150/150"这类写死的偏移——那会让远近不同的场景全都错位。
            await RefreshSpriteGeometryAsync(_currentScene);
            SpriteLoadingProgress = 1.0;
            StatusText = "";
        }
        catch (Exception ex)
        {
            App.WriteLog($"ApplySpriteCoreAsync: {ex.Message}");
            SpriteLoadingProgress = 0;
            StatusText = "立绘加载失败";
        }
    }

    // ============ 新版着装：行走动作 ============

    /// <summary>
    /// 一段路线开始行走时切到 walk 动作；到达/取消时由 WalkRouteCompleted 收回静态立绘。
    /// 节拍仍由 <see cref="AnimateWalkAsync"/> 里的 WalkCycle 按距离推进。
    /// </summary>
    private void OnWalkSegmentStarted()
    {
        Helpers.Ui.Post(StartWalkMotion);
    }

    private void OnWalkRouteCompleted()
    {
        Helpers.Ui.Post(() => StopWalkMotion());
    }

    private void StartWalkMotion()
    {
        StopWalkMotion(restoreStatic: false);
        if (!_figureActive || _figurePack is null || _char is null) return;
        var motion = FigureMotionPlayback.FindWalk(_figurePack);
        if (motion?.Frames is not { Count: > 0 }) return;

        var cts = new CancellationTokenSource();
        _walkMotionCts = cts;
        _walkFrameIndex = -1;
        _ = AnimateWalkAsync(motion, cts.Token);
    }

    private void StopWalkMotion(bool restoreStatic = true)
    {
        var cts = _walkMotionCts;
        _walkMotionCts = null;
        if (cts is not null)
        {
            try { cts.Cancel(); } catch { }
            cts.Dispose();
        }
        _walkFrameIndex = -1;

        if (!restoreStatic || !_figureActive) return;
        var staticPath = _figureStaticPath;
        if (string.IsNullOrWhiteSpace(staticPath) || !File.Exists(staticPath)) return;
        // 静态立绘的单发赋值：统一走 Ui.Post，不再区分是否已在 UI 线程。
        Helpers.Ui.Post(() => SpriteSource = ImageSource.FromFile(staticPath));
    }

    /// <summary>
    /// 行走节拍的唯一驱动：每帧把「真实经过的时间 × 步行速度」换算成位移，
    /// 再用 <see cref="WalkCycle.PhaseForDistance"/> 求相位。这样帧号只跟走过的距离有关，
    /// 帧率抖动、变速、从待机起步都不会让脚在地上滑。
    /// </summary>
    private async Task AnimateWalkAsync(FigureMotion motion, CancellationToken ct)
    {
        try
        {
            var packId = _char?.FigurePackId ?? "";
            var outfit = _outfitKey ?? "";
            var frames = motion.Frames;
            if (string.IsNullOrWhiteSpace(packId) || frames.Count == 0) return;
            // 新一段行走开始：上一段的帧缓存作废（着装/表情可能已经换了）。
            _walkFrameCache.Clear();

            // 以角色显示高度作为「一个身长」：步长约 0.38 身长，速度约 0.9 身长/秒。
            // 参数集中在 WalkCycle 的单位里，改速度时不必去改播放器。
            var bodyHeight = Math.Max(120, SpriteFitH * Math.Max(0.1, SpriteScale));
            var stepLength = Math.Max(24, bodyHeight * 0.38);
            var speed = Math.Max(40, bodyHeight * 0.9);
            var startPhase = _walkPhase;
            var width = _isPhoneStage ? WalkFramePhoneWidth : WalkFrameDesktopWidth;
            var height = (int)Math.Round(width * 1920.0 / 1080.0);

            var clock = System.Diagnostics.Stopwatch.StartNew();
            var lastSeconds = 0d;
            var distance = 0d;

            while (!ct.IsCancellationRequested)
            {
                var now = clock.Elapsed.TotalSeconds;
                var dt = Math.Clamp(now - lastSeconds, 0, 0.12);
                lastSeconds = now;
                distance += speed * dt;

                var phase = WalkCycle.PhaseForDistance(distance, stepLength, WalkCycle.DefaultStepsPerCycle, startPhase);
                _walkPhase = phase;
                var index = WalkCycle.FrameIndexAt(phase, frames.Count);
                if (index != _walkFrameIndex && !ct.IsCancellationRequested)
                {
                    _walkFrameIndex = index;
                    // 帧图只构建一次 ImageSource、之后全程复用：逐帧 FromFile 会不停分配大对象，
                    // 而且每次换新实例都会触发 Image 的 Measure 失效（GC 风暴 + 布局环的共犯）。
                    var key = $"{motion.Id}:{index}:{width}";
                    if (_walkFrameCache.TryGetValue(key, out var cached))
                    {
                        Helpers.Ui.Post(() =>
                        {
                            if (ct.IsCancellationRequested) return;
                            SpriteSource = cached;
                            SpriteVisible = true;
                        });
                    }
                    else
                    {
                        var path = await _figures.RenderMotionAsync(
                            packId, outfit, "", motion.Id, index, width, height).ConfigureAwait(false);
                        if (ct.IsCancellationRequested) return;
                        if (!string.IsNullOrWhiteSpace(path))
                        {
                            var src = ImageSource.FromFile(path);
                            _walkFrameCache[key] = src;
                            Helpers.Ui.Post(() =>
                            {
                                if (ct.IsCancellationRequested) return;
                                SpriteSource = src;
                                SpriteVisible = true;
                            });
                        }
                    }
                }

                await Task.Delay(33, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            App.WriteLog("MainGameVM.AnimateWalk -> " + ex);
        }
    }


    /// <summary>设置当前说话角色并更新立绘高亮。</summary>
    public void SetSpeakingCharacter(bool isSpeaking)
    {
        IsSpeaking = isSpeaking;
        if (SpriteVisible)
        {
            SpriteOpacity = isSpeaking ? 1.0 : 0.5;
            ApplySpeakingEmphasis();
        }
    }

    /// <summary>执行基础动作：下沉。位移叠加在标定算出的基准位置上，动画结束回到基准。</summary>
    public async Task SinkAnimationAsync()
    {
        SpriteY = _baseSpriteY + 15;
        await Task.Delay(300);
        SpriteY = _baseSpriteY;
    }

    /// <summary>执行基础动作：跳跃。</summary>
    public async Task JumpAnimationAsync()
    {
        SpriteY = _baseSpriteY - 30;
        await Task.Delay(400);
        SpriteY = _baseSpriteY;
    }

    /// <summary>执行基础动作：颤抖。</summary>
    public async Task ShakeAnimationAsync()
    {
        for (int i = 0; i < 5; i++)
        {
            SpriteX = _baseSpriteX + (i % 2 == 0 ? 3 : -3);
            await Task.Delay(50);
        }
        SpriteX = _baseSpriteX;
    }

    /// <summary>从回复文本中匹配表情：命中任一表情词（取最长）则切过去；否则按常用情绪词兜底。</summary>
    private string? ResolveEmotion(string text)
    {
        if (_char is null || string.IsNullOrEmpty(text)) return null;
        if (_figureActive)
        {
            var hit = _figureEmotions
                .Where(e => e.Length > 0 && text.Contains(e, StringComparison.Ordinal))
                .OrderByDescending(e => e.Length)
                .FirstOrDefault();
            if (hit is not null) return hit;
            foreach (var f in new[] { "开心", "高兴", "微笑", "害羞", "温柔", "平静", "惊讶", "伤心" })
            {
                var m = _figureEmotions.FirstOrDefault(e => e.Contains(f, StringComparison.Ordinal));
                if (m is not null) return m;
            }
            return null;
        }
        var best = "";
        foreach (var key in _char.SpriteMap.Keys)
        {
            var parts = key.Split('/');
            if (parts.Length != 2 || parts[0] != _outfitKey) continue;
            if (parts[1].Length > best.Length && text.Contains(parts[1], StringComparison.Ordinal))
                best = parts[1];
        }
        if (best.Length > 0) return best;
        foreach (var f in new[] { "开心", "高兴", "微笑", "害羞", "温柔", "平静", "惊讶", "伤心" })
        {
            var m = _char.SpriteMap.Keys.FirstOrDefault(k =>
                k.StartsWith(_outfitKey + "/", StringComparison.Ordinal) && k.Split('/')[1].Contains(f, StringComparison.Ordinal));
            if (m is not null) return m.Split('/')[1];
        }
        return null;
    }

    /// <summary>按提示词列表切表情：第一个命中的优先，全不中则随机挑一个当前服装已有的情绪。</summary>
    private void SetEmotionAny(params string[] hints)
    {
        if (_char is null) return;
        if (_figureActive)
        {
            foreach (var hint in hints)
            {
                var m = _figureEmotions.FirstOrDefault(e => e.Contains(hint, StringComparison.Ordinal));
                if (m is not null) { SetEmotion(m); return; }
            }
            SetEmotion(RandomEmotion() ?? _defaultEmotion);
            return;
        }
        foreach (var hint in hints)
        {
            var m = _char.SpriteMap.Keys.FirstOrDefault(k =>
                k.StartsWith(_outfitKey + "/", StringComparison.Ordinal) && k.Split('/')[1].Contains(hint, StringComparison.Ordinal));
            if (m is not null) { SetEmotion(m.Split('/')[1]); return; }
        }
        SetEmotion(RandomEmotion() ?? _defaultEmotion);
    }

    /// <summary>"随机立绘"：当情绪解析无果、不确定用哪个时，从当前服装的情绪里随机挑一个。</summary>
    private string? RandomEmotion()
    {
        if (_char is null || string.IsNullOrEmpty(_outfitKey)) return null;
        var emotions = _figureActive
            ? _figureEmotions.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
            : _char.SpriteMap.Keys
                .Where(k => k.StartsWith(_outfitKey + "/", StringComparison.Ordinal))
                .Select(k => k.Split('/')[1])
                .Distinct()
                .ToArray();
        if (emotions.Length == 0) return null;
        return emotions[Random.Shared.Next(emotions.Length)];
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

    // ============ #10 对话携带文件 ============

    /// <summary>选一个文件挂到「待发附件」，下一条消息会带着它一起发给 AI。</summary>
    [RelayCommand]
    private async Task PickAttachment()
    {
        if (IsCalibrationBlocked) return;
        try
        {
            var pick = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "选择要带给 TA 的文件",
                FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    { DevicePlatform.WinUI, new[] { ".txt", ".md", ".json", ".csv", ".log", ".xml", ".pdf",
                        ".png", ".jpg", ".jpeg", ".gif", ".webp", ".mp3", ".wav", ".mp4", "*" } },
                    { DevicePlatform.Android, new[] { "*/*" } },
                    { DevicePlatform.iOS, new[] { "public.item" } }
                })
            });
            if (pick is null) return;
            var att = Modules.AiChat.ChatEngine.BuildAttachmentFromPath(pick.FullPath);
            PendingAttachments.Add(att);
            RefreshAttachmentState();
        }
        catch (Exception ex)
        {
            App.WriteLog("MainGameViewModel.PickAttachment -> " + ex);
        }
    }

    /// <summary>清空待发附件。</summary>
    [RelayCommand]
    private void ClearAttachments() => ClearAttachmentsInternal();

    private void ClearAttachmentsInternal()
    {
        PendingAttachments.Clear();
        RefreshAttachmentState();
    }

    /// <summary>刷新待发附件相关的展示状态（数量标签、按钮高亮）。</summary>
    private void RefreshAttachmentState()
    {
        var n = PendingAttachments.Count;
        HasPendingAttachments = n > 0;
        PendingAttachmentsLabel = n > 0 ? $"带文件 · {n}" : "";
    }

    /// <summary>#12：挑一个已导入的表情发出去（用户侧）。</summary>
    [RelayCommand]
    private async Task SendSticker()
    {
        if (IsCalibrationBlocked) return;
        await _stickers.EnsureLoadedAsync();
        var items = _stickers.Items;
        if (items.Count == 0)
        {
            Toast.Info("还没有导入表情。到「设置 → 表情包库」导入几张吧。");
            return;
        }
        var names = items.Select(i => i.Name).Distinct().Take(24).ToArray();
        var choice = await Shell.Current.DisplayActionSheet("发一个表情", "取消", null, names);
        if (string.IsNullOrEmpty(choice) || choice == "取消") return;
        var item = _stickers.Find(choice);
        if (item is null) return;
        // 界面上先把自己发的表情挂出来
        AddMessage("user", "", item.Path);
        var charId = _engine.State.CharacterId;
        if (string.IsNullOrEmpty(charId)) return;
        IsThinking = true;
        var stickerNames = new List<string>();
        try
        {
            _chat.SetStickerContext(_stickers.BuildContextForAi());
            var desc = string.IsNullOrWhiteSpace(item.Description) ? "" : $"，{item.Description}";
            var prompt = $"(用户发来一个表情包：「{item.Name}」{desc})";
            var reply = await _chat.Send(charId, prompt);
            var rawForStats = reply;
            var (replyClean, found) = Services.StickerLibrary.SplitMarkers(reply);
            reply = replyClean;
            stickerNames.AddRange(found);
            var (segs, _) = await ProcessReplyAsync(reply);
            if (segs.Count == 0) segs.Add(reply.Trim());
            RefreshCharacterName();
            foreach (var seg in segs) AddMessage("assistant", seg);
            foreach (var sn in stickerNames)
            {
                var st = _stickers.Find(sn);
                if (st is not null) AddMessage("assistant", "", st.Path);
            }
            _ = ApplyChatAffectionAsync(charId, prompt, reply, string.Join(" ", segs));
            _ = AutoSave();
        }
        catch (Exception ex)
        {
            App.WriteLog("MainGameViewModel.SendSticker -> " + ex);
        }
        finally { IsThinking = false; }
    }

    // ============ 送礼 / 使用面板 ============

    /// <summary>展开/收起送礼面板，打开时刷新已购商品。</summary>
    [RelayCommand]
    private void ToggleGiftPanel()
    {
        IsGiftPanelVisible = !IsGiftPanelVisible;
        if (IsGiftPanelVisible)
            OnPropertyChanged(nameof(GiftItems));
    }

    /// <summary>送礼面板模式：true=送礼给小雨，false=自己使用。</summary>
    [ObservableProperty] private bool _isGiftMode = true;

    /// <summary>切换送礼面板模式（送礼 ⇄ 使用）。</summary>
    [RelayCommand]
    private void ToggleGiftMode() => IsGiftMode = !IsGiftMode;

    /// <summary>已购商品（面板数据源）。</summary>
    public IReadOnlyList<Models.ShopItem> GiftItems => _gifts.OwnedItems;

    /// <summary>是否已有已购商品（空态提示用）。</summary>
    public bool HasGiftItems => GiftItems.Count > 0;

    /// <summary>送礼：消耗库存，小雨回应显示为一条聊天消息。</summary>
    [RelayCommand]
    private async Task GiftItem(Models.ShopItem item)
    {
        if (item is null) return;
        IsGiftPanelVisible = false;
        AddMessage("user", $"送给你：{item.Emoji} {item.Name}");
        IsThinking = true;
        try
        {
            var charId = _engine.State.CharacterId;
            var raw = await _gifts.GiftAsync(item);
            raw = await ExecuteMoveMarkersAsync(raw);
            var clean = StripStatDirective(raw);
            AddMessage("assistant", clean);
            _ = ApplyChatAffectionAsync(charId, $"你送 TA 了一份「{item.Name}」", raw, clean);
            CaptureMoment(charId, item.Price >= 50 ? 5 : 3, $"送礼：{item.Name}");
            var e = ResolveEmotion(clean);
            SetEmotion(e ?? RandomEmotion() ?? _defaultEmotion);
            _ = _speech.Speak(clean);
            _ = AutoSave();
        }
        catch (Exception ex)
        {
            App.WriteLog("MainGame.GiftItem -> " + ex);
            AddMessage("assistant", "……");
        }
        finally { IsThinking = false; }
    }

    /// <summary>使用：消耗库存，小雨回应显示为一条聊天消息。</summary>
    [RelayCommand]
    private async Task UseItem(Models.ShopItem item)
    {
        if (item is null) return;
        IsGiftPanelVisible = false;
        AddMessage("user", $"我用了：{item.Emoji} {item.Name}");
        IsThinking = true;
        try
        {
            var charId = _engine.State.CharacterId;
            var raw = await _gifts.UseAsync(item);
            raw = await ExecuteMoveMarkersAsync(raw);
            var clean = StripStatDirective(raw);
            AddMessage("assistant", clean);
            _ = ApplyChatAffectionAsync(charId, $"你自己用了「{item.Name}」。", raw, clean);
            var e = ResolveEmotion(clean);
            SetEmotion(e ?? RandomEmotion() ?? _defaultEmotion);
            _ = _speech.Speak(clean);
            _ = AutoSave();
        }
        catch (Exception ex)
        {
            App.WriteLog("MainGame.UseItem -> " + ex);
            AddMessage("assistant", "……");
        }
        finally { IsThinking = false; }
    }

    /// <summary>解析 AI 回复中的【移动:场景名】标记并执行移动，移动结果附在回复末尾。</summary>
    private async Task<string> ExecuteMoveMarkersAsync(string reply)
    {
        if (string.IsNullOrWhiteSpace(reply) || !reply.Contains("【移动:", StringComparison.Ordinal))
            return reply;
        var result = reply;
        var notes = new List<string>();
        foreach (Match m in Regex.Matches(reply, @"【移动:([^】]+)】"))
        {
            var sceneName = m.Groups[1].Value.Trim();
            if (string.IsNullOrEmpty(sceneName)) continue;
            try
            {
                IsWalking = true;
                var walk = await ExecuteSingleMoveAsync(sceneName);
                if (!string.IsNullOrWhiteSpace(walk)) notes.Add(walk);
            }
            catch (Exception ex)
            {
                App.WriteLog("MainGame.ExecuteMoveMarkersAsync -> " + ex);
            }
            finally { IsWalking = false; }
        }
        result = Regex.Replace(result, @"【移动:[^】]+】", "").Trim();
        if (notes.Count > 0)
            result = $"{result}\n{string.Join("\n", notes)}";
        // 场景库【场景:条目/模式】标记（时间关灯 / AI 指令切换）
        var (cleanReply, sceneNote) = await _sceneDirector.ExecuteMarkersAsync(result);
        if (!string.IsNullOrEmpty(sceneNote))
            cleanReply = cleanReply + "\n" + sceneNote;
        return cleanReply;
    }

    /// <summary>把设置里的天气事件参数同步进 SceneDirector（设置变更与启动时各调一次）。</summary>
    private void SyncWeatherEventConfig()
    {
        var s = _settings.Current;
        _sceneDirector.SetWeatherEventConfig(new Modules.Scene.SceneDirector.WeatherEventConfig
        {
            LightningEnabled = s.LightningEnabled,
            LightningProbability = s.LightningProbability,
            RainEventEnabled = s.RainEventEnabled,
            RainProbability = s.RainProbability
        });
    }

    /// <summary>
    /// 天气事件回调（打雷/下雨，可能在 Timer 线程）：
    ///  - 打雷：若配了帧图则按序播放（播放期间叠图片），否则默认「屏幕白一下再慢慢恢复」；
    ///    同时按配置播放雷声；白闪时长与帧切换间隔取 WeatherEffectFrameMs。
    ///  - 下雨：播放雨声（若配了）；若配了雨帧则循环显示。
    /// 全程切到 UI 线程渲染，异常吞掉不影响主对话。
    /// </summary>
    private async void OnWeatherEvent(Modules.Scene.SceneDirector.WeatherEventKind kind, string? weather)
    {
        try
        {
            // 统一背景系统联动：打雷时播放雷声音效并自增闪电信号，交给视图层做场景闪光。
            // 下面原有的白闪/帧特效是独立机制，保留不动，两种表现并存。
            if (kind == Modules.Scene.SceneDirector.WeatherEventKind.Lightning)
            {
                _weatherAudio.PlayThunder();
                Helpers.Ui.Post(() => LightningFlashSignal++);
            }

            var s = _settings.Current;
            var frames = kind == Modules.Scene.SceneDirector.WeatherEventKind.Lightning
                ? s.LightningFrames
                : s.RainFrames;
            var sfx = kind == Modules.Scene.SceneDirector.WeatherEventKind.Lightning
                ? s.LightningSfxPath
                : s.RainSfxPath;
            var frameMs = Math.Clamp(s.WeatherEffectFrameMs, 30, 5000);

            // 该回调从 Timer 线程进来：原先的 BeginInvoke 在 WinUI 线程池上取不到 DispatcherQueue 会直接抛
            // （见 Helpers/Ui.cs 的实证记录），这里统一走 Ui.PostAsync 回 UI 线程。
            await Helpers.Ui.PostAsync(async () =>
            {
                try
                {
                    // 音效：优先取「音效库卡槽」（素材库长按 → 设为打雷音 / 雨声音）；没装填再回落设置里的路径。
                    // 两条都没有就不放音（画面照常闪）。
                    var slotKind = kind == Modules.Scene.SceneDirector.WeatherEventKind.Lightning ? "lightning" : "rain";
                    if (await _audio.HasAssignedAsync(slotKind))
                        _audio.PlayAssigned(slotKind);
                    else if (!string.IsNullOrWhiteSpace(sfx))
                        _audio.PlaySfxFile(sfx);

                    // 视觉：有帧图则按序切换；打雷且无帧图 → 默认白闪。
                    // 帧图一次性预建成 ImageSource 实例（装载失败的跳过），循环里只在这批缓存实例间轮换：
                    // 不再逐帧 FromFile —— 那会不停分配大对象，且每次换新实例都触发一次换源 Measure 失效。
                    var usable = frames.Where(f => !string.IsNullOrWhiteSpace(f)).ToList();
                    var sources = new List<ImageSource>();
                    foreach (var f in usable)
                    {
                        try { sources.Add(ImageSource.FromFile(f)); } catch { }
                    }
                    if (sources.Count > 0)
                    {
                        WeatherFxVisible = true;
                        var loops = kind == Modules.Scene.SceneDirector.WeatherEventKind.Lightning
                            ? 1 // 打雷只播一遍
                            : 3; // 下雨循环几轮（受雨声时长约束，简单起见固定 3 轮）
                        for (var loop = 0; loop < loops; loop++)
                        {
                            foreach (var src in sources)
                            {
                                WeatherFxImage = src;
                                await Task.Delay(frameMs);
                            }
                            if (kind == Modules.Scene.SceneDirector.WeatherEventKind.Lightning) break;
                        }
                        WeatherFxVisible = false;
                        WeatherFxImage = null;
                    }
                    else if (kind == Modules.Scene.SceneDirector.WeatherEventKind.Lightning)
                    {
                        // 默认表现：屏幕白一下，再慢慢恢复。
                        FlashOpacity = 1.0;
                        await Task.Delay(frameMs);
                        var steps = 12;
                        for (var i = steps; i >= 0; i--)
                        {
                            FlashOpacity = i / (double)steps * 0.9;
                            await Task.Delay(45);
                        }
                        FlashOpacity = 0;
                    }
                }
                catch (Exception ex) { App.WriteLog("MainGame.OnWeatherEvent(render) -> " + ex); }
            });
        }
        catch (Exception ex)
        {
            App.WriteLog("MainGame.OnWeatherEvent -> " + ex);
        }
    }

    /// <summary>场景库模式被应用（地图/纯库场景）时渲染背景与 BGM。</summary>
    private void OnSceneModeApplied(Models.SceneLibraryEntry entry, Models.SceneMode? mode)
    {
        Helpers.Ui.Post(() =>
        {
            // 亮度遮罩跟着生效模式走；「早上开灯看不出变化」已由 SceneDirector 判定并写进给 AI 的回报里
            SceneDim = _sceneDirector.ActiveDim;
            var bg = mode?.Background is { Length: > 0 } mb ? mb : entry.Background;
            var color = mode?.BackgroundColor is { Length: > 0 } mc ? mc : entry.BackgroundColor;
            if (string.IsNullOrWhiteSpace(bg))
            {
                if (!string.IsNullOrEmpty(color)) SceneBg = color;
                // 纯库场景才清空背景。地图引用条目（entry.Background 为空）是"背景交给地图场景去画"，
                // 这里清空会把刚由 SceneChanged 渲染好的地图背景抹掉。
                if (string.IsNullOrEmpty(entry.MapSceneRef)) SceneBackdrop = null;
            }
            else
            {
                SceneBackdrop = ImageSource.FromFile(bg);
                if (!string.IsNullOrEmpty(color)) SceneBg = color;
            }
            // 背景换了，标定也得跟着换，否则角色会按上一张图的尺度站到错的位置
            ApplyModePlacement(entry, mode);
            // 场景库模式也可能把背景让回纯色：这时角色背景要顶上来，反之则继续让位。
            RefreshCharacterBackdrop();
        });
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

    // ============ 多段回答：按工具标记 / 动作标记切段 ============

    private static readonly HashSet<string> ActionMarkerNames = new() { "下沉", "雀跃", "颤抖" };

    private static readonly Regex ReplyMarkersRegex = new(@"【(移动:[^】]+|CG:[^】]+|状态:[^】]+|下沉|雀跃|颤抖)】", RegexOptions.Compiled);

    private static readonly Regex ActionMarkerRegex = new(@"【(下沉|雀跃|颤抖)】", RegexOptions.Compiled);

    /// <summary>数值指令标记：给机器读的（送礼/自用等直接 AddMessage 的路径要用它把标记从展示文本里剥掉）。</summary>
    private static readonly Regex StatDirectiveRegex = new(@"【\s*状态[^】]*】", RegexOptions.Compiled);

    /// <summary>
    /// 把 AI 回复拆成多段：正文按标记位置切段，移动/CG 工具调用就地执行（各自算一段），
    /// 动作标记直接执行动画；开发者展示模式保留动作标记文本，普通模式隐藏。
    /// </summary>
    private async Task<(List<string> Segments, bool HasReaction)> ProcessReplyAsync(string reply)
    {
        // 架构流程图打点：模型文本进入后处理管线（第5层 eng）。
        Services.Core.FlowTracer.Trace("eng.postprocess", _engine?.State?.CharacterId);
        if (string.IsNullOrWhiteSpace(reply))
            return (new List<string>(), false);

        var matches = ReplyMarkersRegex.Matches(reply).Cast<Match>().ToList();
        var hasReaction = matches.Any(m => ActionMarkerNames.Contains(m.Groups[1].Value));
        var segments = new List<string>();
        if (matches.Count == 0)
        {
            segments.Add(reply.Trim());
            return (segments, false);
        }

        var devShow = _settings.Current.DeveloperShowcaseUnlocked;
        var last = 0;
        foreach (var m in matches)
        {
            var head = reply[last..m.Index];
            if (!string.IsNullOrWhiteSpace(head)) segments.Add(head.Trim());
            last = m.Index + m.Length;
            var inner = m.Groups[1].Value;

            if (inner.StartsWith("移动:", StringComparison.Ordinal))
            {
                var note = await ExecuteSingleMoveAsync(inner[3..].Trim());
                if (!string.IsNullOrWhiteSpace(note)) segments.Add(note.Trim());
            }
            else if (inner.StartsWith("CG:", StringComparison.Ordinal))
            {
                await TriggerSingleCgAsync(inner[3..].Trim());
            }
            else if (ActionMarkerNames.Contains(inner))
            {
                RunActionMarker(inner);
                if (devShow)
                {
                    if (segments.Count == 0) segments.Add($"【{inner}】");
                    else segments[^1] += $"【{inner}】";
                }
            }
        }
        var tail = reply[last..];
        if (!string.IsNullOrWhiteSpace(tail)) segments.Add(tail.Trim());
        return (segments, hasReaction);
    }

    /// <summary>
    /// 执行一次【移动:场景名】，返回移动过程描述（作为一段回答展示）。
    /// #9 行进扩展：
    ///  - 设置「AI 单独出行」关闭（默认）→ 维持旧行为：移动用户视角，AI 走到哪用户跟到哪。
    ///  - 设置「AI 单独出行」开启 → 只移动 AI 自己的 actor 位置，用户视角停原地；
    ///    随后若「允许跟上」，主界面出现「跟上」入口（FollowAiCommand），由用户决定是否跟上。
    /// </summary>
    private async Task<string?> ExecuteSingleMoveAsync(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName)) return null;
        if (!_engine.MapUnlocked) return null;
        try
        {
            IsWalking = true;
            var alone = _settings.Current.AiTravelAlone;

            if (!alone)
            {
                // 旧行为：AI 带着用户一起走（移动用户视角）。
                var walk = await _map.MoveToAsync(sceneName);
                ClearFollowOffer();
                return walk;
            }

            // 新行为：AI 单独走。先把当前 AI 注册成 actor（若尚未注册），再单独移动它。
            var actorId = EnsureAiActor();
            var target = _map.Map.SceneById(sceneName) ?? _map.Map.SceneByName(sceneName);
            if (target is null) return null;

            var note = _mapActors.MoveActorToScene(actorId, target.Id);
            if (string.IsNullOrWhiteSpace(note))
            {
                // 无法单独移动（不可达等）：退回旧行为，保证 AI 仍能到达目的地。
                ClearFollowOffer();
                return await _map.MoveToAsync(sceneName);
            }
            // 成功单独出行：用户视角没动。若允许跟上且用户不在同一场景，亮出「跟上」入口。
            var userScene = _map.CurrentSceneId;
            if (_settings.Current.AllowFollowAi && userScene != target.Id)
            {
                PendingFollowSceneId = target.Id;
                PendingFollowSceneName = target.Name;
                CanFollowAi = true;
            }
            else
            {
                ClearFollowOffer();
            }
            return note;
        }
        catch (Exception ex)
        {
            App.WriteLog("MainGame.ExecuteSingleMoveAsync -> " + ex);
            return null;
        }
        finally { IsWalking = false; }
    }

    // ============ 行进扩展：AI 单独出行 / 用户跟上（#9） ============

    /// <summary>「跟上」入口是否可用：AI 单独走到别处且用户没跟上时为 true。</summary>
    [ObservableProperty] private bool _canFollowAi;
    /// <summary>待跟上的目标场景名（按钮/提示用）。</summary>
    [ObservableProperty] private string _pendingFollowSceneName = "";
    /// <summary>待跟上的目标场景 Id（内部用）。</summary>
    private string PendingFollowSceneId { get; set; } = "";

    /// <summary>确保当前 AI 角色已在地图 actor 里注册，返回其 actor Id。</summary>
    private string EnsureAiActor()
    {
        var ch = _engine.ActiveCharacter;
        var id = ch?.Profile.Id;
        var name = ch?.Profile.Name ?? CharacterName;
        if (string.IsNullOrEmpty(id)) id = "ai_active";
        if (_mapActors.Get(id) is null)
            _mapActors.Register(id, name, isUser: false, sceneId: _map.CurrentSceneId);
        return id;
    }

    /// <summary>清掉「跟上」提示（AI 未单独出行 / 已跟上 / 用户已同场景时调用）。</summary>
    private void ClearFollowOffer()
    {
        CanFollowAi = false;
        PendingFollowSceneId = "";
        PendingFollowSceneName = "";
    }

    /// <summary>用户选择「跟上」AI：把用户视角移动到 AI 所在的场景。</summary>
    [RelayCommand]
    private async Task FollowAiAsync()
    {
        if (!CanFollowAi || string.IsNullOrEmpty(PendingFollowSceneId)) return;
        var sceneId = PendingFollowSceneId;
        try
        {
            IsWalking = true;
            var walk = await _map.MoveToAsync(sceneId);
            if (!string.IsNullOrWhiteSpace(walk)) AddMessage("assistant", walk);
        }
        catch (Exception ex) { App.WriteLog("MainGame.FollowAi -> " + ex); }
        finally
        {
            IsWalking = false;
            ClearFollowOffer();
        }
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
            AddAffectionPoints(delta, true);
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
        if (Affection < 40)
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
        // 好感进度条改用累计积分的「阶内进度」细分刻度，而不是被 clamp 到 100 的 Affection。
        AffectionPct = Modules.Affection.AffectionLevel.LevelProgress(AffectionPoints);
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

    /// <summary>把 AI 判出来的增减落到三条数值上：好感/信任可涨可跌，积分只涨不退；精力只累进「AI 修正项」，
    /// 基线仍随清醒时长走（时间基线 + AI 修正）。</summary>
    private void Settle(int affectionDelta, int trustDelta, int energyDelta)
    {
        Affection = Math.Clamp(Affection + affectionDelta, 0, 100);
        Trust = Math.Clamp(Trust + trustDelta, 0, 100);
        if (energyDelta != 0 && _engine.ActiveCharacter is { } ch)
        {
            ch.State.EnergyOffset = Math.Clamp(ch.State.EnergyOffset + energyDelta, -60, 40);
            UpdateEnergy(_time.Now().Now);
        }
        UpdateStats();
        if (affectionDelta > 0) AddAffectionPoints(affectionDelta, true);
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

    private void ApplyWeatherBackground(string? condition)
    {
        if (!WeatherBackgroundEnabled)
        {
            WeatherBackgroundVisible = false;
            WeatherBackgroundOpacity = 0;
            WeatherBackgroundColor = "#00000000";
            return;
        }

        var normalized = (condition ?? "").Trim();
        var tuple = normalized.ToLowerInvariant() switch
        {
            var c when c.Contains("storm") || c.Contains("雷") => ("#4a5674", 0.28),
            var c when c.Contains("rain") || c.Contains("阵雨") || c.Contains("雨") => ("#536a7d", 0.24),
            var c when c.Contains("snow") || c.Contains("雪") => ("#dfeaf7", 0.18),
            var c when c.Contains("fog") || c.Contains("雾") || c.Contains("mist") => ("#b5b1a8", 0.16),
            var c when c.Contains("cloud") || c.Contains("阴") || c.Contains("多云") => ("#8d99a4", 0.14),
            var c when c.Contains("sun") || c.Contains("晴") || c.Contains("clear") => ("#f7d791", 0.10),
            _ => ("#00000000", 0.0)
        };

        WeatherBackgroundColor = tuple.Item1;
        WeatherBackgroundOpacity = tuple.Item2;
        WeatherBackgroundVisible = tuple.Item2 > 0.01;
    }

    /// <summary>
    /// 按「当前时间 + 最近一次真实天气」算场景光照，赋给 4 个 ObservableProperty，
    /// 供页面订阅后驱动 WeatherSceneLayer.SetLighting(dirX, elevation, intensity, lightFxEnabled)。
    /// 时间定太阳/月亮方向与仰角，能见度 + 云量定强度；FetchWeather 可能在后台线程，赋值统一走 Ui.Post 回 UI 线程。
    /// </summary>
    private void ComputeLighting(WeatherReading? w)
    {
        Helpers.Ui.Post(() =>
        {
            if (!_background.Current.ExperimentalEnabled) return; // 测试版总开关关闭：光照参数不下发，避免空闲的属性变化驱动场景层。
            var hour = _time.Now().Now.Hour; // 0..23
            bool isNight = hour < 6 || hour > 18;

            // 时间定方向：白天太阳 6 点东升(仰角≈0) → 12 点正午最高(1) → 18 点西落(≈0)；夜间月光低仰角、偏右。
            double elevation = isNight
                ? 0.25
                : Math.Clamp(Math.Sin((hour - 6) / 12.0 * Math.PI), 0.0, 1.0);
            double dirX = isNight
                ? 0.5
                : Math.Clamp((hour - 12) / 6.0, -1.0, 1.0);

            // 能见度 + 云量定强度：能见度越高越亮(10000 米封顶、最低留 0.15)，云量 0..100 最多削弱 60%。
            double intensity;
            if (w is not null && w.VisibilityMeters >= 0)
            {
                double visFactor = Math.Clamp(w.VisibilityMeters / 10000.0, 0.15, 1.0);
                // 云量未知(-1)按无云处理。
                double cloudFactor = w.CloudCover >= 0
                    ? 1.0 - Math.Clamp(w.CloudCover, 0.0, 100.0) / 100.0 * 0.6
                    : 1.0;
                intensity = Math.Clamp(0.35 + 0.65 * visFactor * cloudFactor, 0.0, 1.0);
            }
            else
            {
                intensity = 0.6; // 无数据 / 能见度未知：兜底强度
            }
            if (isNight) intensity *= 0.35; // 夜间整体压暗

            // 总开关关掉时只关光柱/折射特效，基础照明保留（云仍受光）。
            SceneLightDirX = dirX;
            SceneLightElevation = elevation;
            SceneLightIntensity = Math.Clamp(intensity, 0.0, 1.0);
            SceneLightEffectsEnabled = SceneLightEnabled;
        });
    }

    private async Task FetchWeather()
    {
        var w = await _weather.Fetch();
        if (w is not null)
        {
            _lastWeather = w; // 缓存，供 UpdateTime 按时间重算时仍带上能见度/云量
            WeatherDesc = w.Description;
            ApplyWeatherBackground(w.Description);
            if (_weatherSceneFollowsRealWeather && _background.Current.ExperimentalEnabled) _background.ApplyWeatherDescription(w.Description);
            ComputeLighting(w); // 真天气驱动：按能见度 + 时间算光
        }
        else
        {
            _lastWeather = null;
            ApplyWeatherBackground(null);
            ComputeLighting(null); // 无数据也按时间算一次光
        }
    }
}

public sealed class DialogueMessage
{
    public string Role { get; set; } = "";
    public string Text { get; set; } = "";
    public string Time { get; set; } = "";
    public bool IsUser => Role == "user";
    /// <summary>#12：这是一条表情消息时，指向表情图片的路径；普通文字消息为 null。</summary>
    public string? StickerPath { get; set; }
    /// <summary>是否显示为表情（模板里用它切换显示形态）。</summary>
    public bool HasSticker => !string.IsNullOrEmpty(StickerPath);
    /// <summary>文本气泡的显示条件（表情消息不显示空气泡）。</summary>
    public bool ShowBubbleMine => IsUser && !HasSticker;
    public bool ShowBubbleTheirs => !IsUser && !HasSticker;
    /// <summary>表情图片的显示条件（用户发的靠右、AI 发的靠左）。</summary>
    public bool ShowStickerMine => IsUser && HasSticker;
    public bool ShowStickerTheirs => !IsUser && HasSticker;
}

/// <summary>地图 overlay 里的一行场景（x:DataType 绑定的行模型）。</summary>
public sealed class MapSceneOption
{
    public string SceneId { get; init; } = "";
    public string Label { get; init; } = "";
    public bool IsCurrent { get; init; }
}

/// <summary>
/// 舞台要显示的角色背景：路径 + 适配方式 + 归一化裁剪框。
/// 单独成一条记录，视图层只在它真正变化时切一场，而不是每次刷新都重放。
/// </summary>
public sealed record CharacterBackdropState(
    string Path,
    string Fit,
    double CropX,
    double CropY,
    double CropW,
    double CropH,
    bool IsVideo);