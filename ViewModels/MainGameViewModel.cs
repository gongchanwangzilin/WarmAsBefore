using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Dispatching;
using WarmAsBefore.Models;
using WarmAsBefore.Modules.AiChat;
using WarmAsBefore.Modules.Automation;
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
    private readonly PetService _pet;
    private readonly MapService _map;
    private readonly Modules.Market.GiftPanelService _gifts;
    private readonly SettingsManager _settings;
    private readonly Modules.Affection.AffectionLevelUpService _levelUp;
    private readonly Modules.Cg.CgStore _cg;
    private readonly Modules.Cg.CgViewPayload _cgView;
    private readonly Modules.Scene.SceneDirector _sceneDirector;

    [ObservableProperty] private string _locationLabel = "家";
    [ObservableProperty] private string _timeLabel = "";
    [ObservableProperty] private string _speaker = "";
    [ObservableProperty] private string _dialogue = "";
    [ObservableProperty] private bool _isAuto;
    [ObservableProperty] private int _affection = 30;
    [ObservableProperty] private int _affectionLevel = 1;
    [ObservableProperty] private string _affectionLevelTitle = "初识";
    public string AffectionLevelLabel => $"Lv.{AffectionLevel} {AffectionLevelTitle}";
    partial void OnAffectionLevelChanged(int value) => OnPropertyChanged(nameof(AffectionLevelLabel));
    partial void OnAffectionLevelTitleChanged(string value) => OnPropertyChanged(nameof(AffectionLevelLabel));
    [ObservableProperty] private int _trust = 30;
    [ObservableProperty] private int _attachment;
    [ObservableProperty] private int _balance = 1000;
    [ObservableProperty] private string _weatherDesc = "晴朗";
    [ObservableProperty] private string _physLabel = "正常";
    [ObservableProperty] private string _inputText = "";
    [ObservableProperty] private bool _isListening;
    [ObservableProperty] private bool _showSettings;
    [ObservableProperty] private bool _showMap;
    [ObservableProperty] private string _currentTimeStr = "";
    [ObservableProperty] private string _sceneBg = "#2C1810";
    [ObservableProperty] private double _affectionPct;
    [ObservableProperty] private double _trustPct;
    [ObservableProperty] private ImageSource? _spriteSource;
    [ObservableProperty] private bool _spriteVisible;
    [ObservableProperty] private double _spriteOpacity = 1.0;
    [ObservableProperty] private double _spriteX = 0;
    [ObservableProperty] private double _spriteY = 0;
    [ObservableProperty] private double _spriteScale = 1.0;
    [ObservableProperty] private ImageSource? _sceneBackdrop;
    [ObservableProperty] private string _sceneVideoPath = "";
    [ObservableProperty] private bool _sceneVideoOn;
    [ObservableProperty] private bool _isWalking;
    [ObservableProperty] private double _spriteLoadingProgress = 0;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private ObservableCollection<MapSceneOption> _sceneOptions = new();
    [ObservableProperty] private string _characterName = "小雨";
    [ObservableProperty] private bool _isThinking;
    [ObservableProperty] private ObservableCollection<DialogueMessage> _messages = new();
    // ============ 多角色位置：left / center / right ============
    [ObservableProperty] private string _spritePosition = "center";
    // ============ 说话者高亮 ============
    [ObservableProperty] private bool _isSpeaking;

    [ObservableProperty] private bool _isInMiniGame;
    // ============ 送礼 / 使用面板（主界面聊天时显示） ============
    [ObservableProperty] private bool _isGiftPanelVisible;
    [ObservableProperty] private string _giftPanelTitle = "送给小雨 🎁";
    [ObservableProperty] private string _galgameModeLabel = "Galgame 모드";
    [ObservableProperty] private bool _showRightChat;
    [ObservableProperty] private string _lastSpeakerName = "";
    [ObservableProperty] private string _lastMessageText = "";
    [ObservableProperty] private string _dateLabel = "";
    [ObservableProperty] private int _energy = 100;
    [ObservableProperty] private double _energyPct = 1.0;
    // ============ 多角色单场景：气泡式开关 + 沉默提示 ============
    [ObservableProperty] private bool _useBubbleChat;
    [ObservableProperty] private string _silenceHintText = "";

    public bool NoSpriteVisible => !SpriteVisible;

    public bool IsGalgamePanelVisible => !IsInMiniGame;

    /// <summary>手机 竖版布局是否显示：手机恒真；平板（按屏幕短边≥600dp 判定）走电脑版布局，恒假；桌面恒假。</summary>
    public bool IsPhoneLayout { get; } = DetectPhoneLayout();

    /// <summary>电脑版横版布局是否显示：平板（按分辨率判定）+ 桌面 都为真；手机恒假。</summary>
    public bool IsDesktopLayout { get; } = DetectDesktopLayout();

    static bool DetectDesktopLayout()
    {
        if (DeviceInfo.Platform == DevicePlatform.WinUI
            || DeviceInfo.Platform == DevicePlatform.MacCatalyst)
            return true;
        // 平板（按屏幕短边≥600dp 判定，见 DeviceInfoService）也走电脑版布局
        return WarmAsBefore.Modules.Screen.DeviceInfoService.IsTabletScreen();
    }

    static bool DetectPhoneLayout()
    {
        // 手机才用竖版手机布局；平板（分辨率判定）和桌面都不用
        return DeviceInfo.Platform == DevicePlatform.Android
            && !WarmAsBefore.Modules.Screen.DeviceInfoService.IsTabletScreen();
    }

    /// <summary>右侧介绍/对话框是否可见：仅在横版布局（桌面 / 按分辨率判定的平板）且处于小游戏模式时显示；
    /// 手机（竖版布局）隐藏——竖版布局自带底部聊天面板，右框只会挤占立绘空间。</summary>
    public bool IsRightPanelVisible
        => ShowRightChat && IsDesktopLayout;

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

    public MainGameViewModel(GameEngine engine, ChatEngine chat, MemoryVault memory,
        TaskOrchestrator auto, SaveManager save, WeatherProvider weather, RealTimeProvider time,
        PhysiologicalTracker phys, AudioController audio, SpeechService speech,
        CharacterLibrary chars, StorageProvider store, PetService pet, MapService map,
        Modules.Market.GiftPanelService gifts, SettingsManager settings,
        Modules.Affection.AffectionLevelUpService levelUp,
        Modules.Cg.CgStore cg, Modules.Cg.CgViewPayload cgView,
        Modules.Scene.SceneDirector sceneDirector)
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
        _pet = pet;
        _map = map;
        _gifts = gifts;
        _settings = settings;
        _levelUp = levelUp;
        _cg = cg;
        _cgView = cgView;
        _sceneDirector = sceneDirector;

        // 应用聊天显示风格 + 沉默许可
        UseBubbleChat = _settings.Current.ChatStyle == "bubble";
        _chat.SetAllowSilence(_settings.Current.AllowSilence);
        SilenceHintText = _settings.Current.AllowSilence
            ? "💡 已开启「允许沉默」：角色在不想说话时会选择不回复，这是正常的，不必每次提问都强求答复。"
            : "";

        _auto.GreetingReady += OnGreet;
        _auto.Start();
        UpdateTime();
        _ = FetchWeather();
        _ = LoadCharacterAsync();
        _ = InitMapAsync();
        _pet.WatchMinimize();
        _pet.WatchIdle();
        StartClock();
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
        try
        {
            await _map.InitializeAsync();
            _map.SceneChanged += OnMapSceneChanged;
            _map.MapChanged += OnMapChanged;
            _chat.SetMapContext(BuildMapContext() + "\n" + _sceneDirector.BuildContext());
            _sceneDirector.ModeApplied += OnSceneModeApplied;
            _ = _sceneDirector.EvaluateTimeNowAsync();
            ApplyScene(_map.CurrentScene);
            RefreshSceneOptions();
            MainThread.BeginInvokeOnMainThread(async () =>
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
        MainThread.BeginInvokeOnMainThread(() =>
        {
            ApplyScene(scene);
            IsWalking = true;
            LocationLabel = $"{_map.Map.LocationNameOf(scene.Id)} · {scene.Name}";
            RefreshSceneOptions();
            _ = Task.Delay(600).ContinueWith(_ =>
                MainThread.BeginInvokeOnMainThread(() => IsWalking = false));
        });
    }

    private void ApplyScene(MapScene? scene)
    {
        SceneVideoOn = false;
        SceneVideoPath = "";
        if (scene is null)
        {
            SceneBackdrop = null;
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
            }
            else
                SceneBackdrop = ImageSource.FromFile(bg);
        }
        else
            SceneBackdrop = null;
        SceneBg = scene.BackgroundColor;
    }

    /// <summary>地图被编辑/导入后刷新 AI 语境与场景列表。</summary>
    private void OnMapChanged() =>
        MainThread.BeginInvokeOnMainThread(() =>
        {
            _chat.SetMapContext(BuildMapContext());
            RefreshSceneOptions();
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

    partial void OnSpriteVisibleChanged(bool value) => OnPropertyChanged(nameof(NoSpriteVisible));

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
            _levelUp.CharacterName = _char.Profile.Name;
            _levelUp.Title = Modules.Affection.AffectionLevel.TitleOf(newLevel);
            _levelUp.SpritePath = Modules.Affection.SpritePathResolver.Resolve(
                _char, _outfitKey, _currentEmotion, _defaultEmotion, _store.Root);
            _levelUp.BackgroundPath = _map is { IsLoaded: true } && _map.CurrentScene is { } sc
                ? _map.ResolveBackground(sc)
                : null;
            _levelUp.BackgroundColor = SceneBg;
            _levelUp.HasPayload = true;

            MainThread.BeginInvokeOnMainThread(async () =>
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
        UpdateStats();

        // 服装 / 表情：一律**读档恢复**，这里绝不重新随机。
        //
        // 旧实现每次构造 VM 都 Random.Shared.Next 一次。而 MainGamePage / MainGameViewModel
        // 是 transient，每次 GoToAsync("main") 都会新建 —— 于是「进入存档」「桌宠模式回来」
        // 都会自动换立绘。随机只应发生在新建存档那一刻（见 CharacterSelectViewModel.Pick /
        // CreateCharacter），其余一律沿用上次状态。
        var outfits = ch.SpriteMap.Keys.Select(k => k.Split('/')[0]).Distinct().ToList();
        if (outfits.Count > 0)
        {
            // 存档里记过且该服装仍然存在 → 沿用；否则回退到第一个（老存档从没写过这两个字段，
            // 只能用确定性回退，至少保证此后每次进来都一样）
            var savedOutfit = ch.State.CurrentOutfit;
            _outfitKey = outfits.Contains(savedOutfit) ? savedOutfit : outfits[0];
        }
        else
        {
            _outfitKey = ch.State.CurrentOutfit;
        }

        var emotions = ch.SpriteMap.Keys
            .Where(k => k.StartsWith(_outfitKey + "/", StringComparison.Ordinal))
            .Select(k => k.Split('/')[1])
            .Distinct()
            .ToList();
        var savedEmotion = ch.State.CurrentEmotion;
        _defaultEmotion = emotions.Contains(savedEmotion)
            ? savedEmotion
            : (emotions.Count > 0 ? emotions[0] : "normal");
        _currentEmotion = _defaultEmotion;

        // 写回角色状态，随存档一起持久化 —— 这是「按上次立绘状态恢复」能成立的前提
        ch.State.CurrentOutfit = _outfitKey;
        ch.State.CurrentEmotion = _currentEmotion;

        SetEmotion(_currentEmotion);
        MainThread.BeginInvokeOnMainThread(() =>
        {
            CharacterName = ch.Profile.Name;
            _chat.ConfigureCharacter(ch.Profile);
            _chat.SetRoster(_chars.RosterContext(ch.Profile.Id));
            RestoreSession();
        });
        App.WriteLog($"LoadCharacterAsync: char={ch.Profile.Name}, outfit={_outfitKey}, emotion={_currentEmotion}, sprites={ch.SpriteMap.Count}");
        _ = _audio.StartBgmRotationAsync();
    }

    /// <summary>读档后恢复聊天界面（消息来自存档里保存的会话记录）。</summary>
    private void RestoreSession()
    {
        Messages.Clear();
        foreach (var r in _save.ChatLog)
            Messages.Add(new DialogueMessage { Role = r.Role, Text = r.Text, Time = r.At.ToString("HH:mm") });
    }

    private void SetEmotion(string emotion)
    {
        if (_char is null) return;
        _currentEmotion = _char.SpriteMap.ContainsKey($"{_outfitKey}/{emotion}") ? emotion : _defaultEmotion;
        // 表情也要写回角色状态，随存档持久化；否则下次进来又回到初次的表情
        _char.State.CurrentOutfit = _outfitKey;
        _char.State.CurrentEmotion = _currentEmotion;
        ApplySprite();
    }

    private void ApplySprite()
    {
        if (_char is null || string.IsNullOrEmpty(_outfitKey)) { SpriteVisible = false; return; }
        var key = $"{_outfitKey}/{_currentEmotion}";
        if (!_char.SpriteMap.TryGetValue(key, out var rel))
            rel = _char.SpriteMap.ContainsKey($"{_outfitKey}/{_defaultEmotion}")
                ? _char.SpriteMap[$"{_outfitKey}/{_defaultEmotion}"]
                : _char.SpriteMap.Values.FirstOrDefault();
        string? full = null;
        if (!string.IsNullOrEmpty(rel))
        {
            // 尝试多种路径组合
            var candidates = new List<string>();
            // rel 本身可能带 assets/ 前缀或不带
            candidates.Add(Path.Combine(_store.Root, rel));
            candidates.Add(Path.Combine(App.RootDirectory, rel));
            candidates.Add(Path.Combine(Directory.GetCurrentDirectory(), rel));
            // 如果 rel 没有 assets/ 前缀，添加它
            if (!rel.StartsWith("assets/", StringComparison.Ordinal)
                && !rel.StartsWith(Path.DirectorySeparatorChar.ToString()))
                candidates.Add(Path.Combine(_store.Root, "assets", rel));
            // 去掉可能的 assets/ 前缀再试
            if (rel.StartsWith("assets/", StringComparison.Ordinal))
                candidates.Add(Path.Combine(_store.Root, rel["assets/".Length..]));
            // 如果 rel 是绝对路径
            if (rel.StartsWith(Path.DirectorySeparatorChar.ToString()) || rel.StartsWith("/"))
                candidates.Add(rel);
            // 也尝试原始路径不带 assets 前缀
            if (!rel.StartsWith("assets/", StringComparison.Ordinal) && !rel.StartsWith(Path.DirectorySeparatorChar.ToString()))
            {
                var withoutAssets = rel;
                if (withoutAssets.StartsWith("characters/", StringComparison.Ordinal))
                    candidates.Add(Path.Combine(_store.Root, "assets", withoutAssets));
            }
            full = candidates
                .Where(p => !string.IsNullOrEmpty(p) && File.Exists(p))
                .FirstOrDefault();
            if (full is null)
                App.WriteLog($"ApplySprite: 未找到立绘 {rel}, 尝试了 {candidates.Count} 条路径");
        }
        if (string.IsNullOrEmpty(full)) { SpriteVisible = false; return; }
        if (MainThread.IsMainThread)
            _ = ApplySpriteCoreAsync(full);
        else
            MainThread.BeginInvokeOnMainThread(() => _ = ApplySpriteCoreAsync(full));
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
            // 根据位置设置偏移
            SpriteX = SpritePosition switch
            {
                "left" => -150,
                "right" => 150,
                _ => 0
            };
            SpriteY = 0;
            SpriteOpacity = IsSpeaking ? 1.0 : 0.5;
            SpriteScale = IsSpeaking ? 1.0 : 0.95;
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

    /// <summary>设置当前说话角色并更新立绘高亮。</summary>
    public void SetSpeakingCharacter(bool isSpeaking)
    {
        IsSpeaking = isSpeaking;
        if (SpriteVisible)
        {
            SpriteOpacity = isSpeaking ? 1.0 : 0.5;
            SpriteScale = isSpeaking ? 1.0 : 0.95;
        }
    }

    /// <summary>执行基础动作：下沉。</summary>
    public async Task SinkAnimationAsync()
    {
        SpriteY = 15;
        await Task.Delay(300);
        SpriteY = 0;
    }

    /// <summary>执行基础动作：跳跃。</summary>
    public async Task JumpAnimationAsync()
    {
        SpriteY = -30;
        await Task.Delay(400);
        SpriteY = 0;
    }

    /// <summary>执行基础动作：颤抖。</summary>
    public async Task ShakeAnimationAsync()
    {
        for (int i = 0; i < 5; i++)
        {
            SpriteX = i % 2 == 0 ? 3 : -3;
            await Task.Delay(50);
        }
        SpriteX = 0;
    }

    /// <summary>从回复文本中匹配表情：命中任一表情词（取最长）则切过去；否则按常用情绪词兜底。</summary>
    private string? ResolveEmotion(string text)
    {
        if (_char is null || string.IsNullOrEmpty(text)) return null;
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
        var emotions = _char.SpriteMap.Keys
            .Where(k => k.StartsWith(_outfitKey + "/", StringComparison.Ordinal))
            .Select(k => k.Split('/')[1])
            .Distinct()
            .ToArray();
        if (emotions.Length == 0) return null;
        return emotions[Random.Shared.Next(emotions.Length)];
    }

    private void OnGreet(string msg)
    {
        MainThread.BeginInvokeOnMainThread(() =>
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
        if (string.IsNullOrWhiteSpace(InputText)) return;
        _audio.PlayAssigned("key");
        var msg = InputText;
        InputText = "";
        AddMessage("user", msg);
        var charId = _engine.State.CharacterId;
        if (!string.IsNullOrEmpty(charId))
        {
            IsThinking = true;
            var segments = new List<string>();
            var display = "";
            try
            {
                var reply = await _chat.Send(charId, msg);
                var (segs, hasReaction) = await ProcessReplyAsync(reply);
                // 动作标记缺失 → System 级补正：有正文=重输出，近乎为空=补输出
                if (!hasReaction && !string.IsNullOrWhiteSpace(_settings.Current.AiUrl))
                {
                    var corrected = await _chat.RetryActionMarkerAsync(
                        charId, msg, reply, isReOutput: !string.IsNullOrWhiteSpace(reply.Trim()));
                    if (!ReferenceEquals(corrected, reply) && corrected != reply)
                    {
                        _chat.ReplaceLastAssistant(charId, corrected);
                        (segs, hasReaction) = await ProcessReplyAsync(corrected);
                        reply = corrected;
                    }
                }
                segments = segs;
                if (segments.Count == 0) segments.Add(reply.Trim());
                display = string.Join(" ", segments);
            }
            catch (Exception ex)
            {
                // AI 未连接：不崩溃、不空白，自动选一张立绘陪伴，并点明没收到
                App.WriteLog("MainGameViewModel.SendMessage -> " + ex);
                segments.Add("……");
                display = "……";
            }
            finally { IsThinking = false; }

            // 沉默判定：AI 允许沉默且本轮无正文（仅有动作标记/为空）→ 本回合停止，不再追加回复
            var isSilent = _settings.Current.AllowSilence &&
                           string.IsNullOrWhiteSpace(StripActionMarkers(display));
            if (isSilent)
            {
                AddMessage("assistant", "（…沉默…）");
                _ = AutoSave();
                return;
            }

            foreach (var seg in segments) AddMessage("assistant", seg);
            Affection = Math.Min(100, Affection + 1);
            Trust = Math.Min(100, Trust + 1);
            UpdateStats();
            AddAffectionPoints(1, true);
            CaptureMoment(charId, 1, "聊天");
            var e = ResolveEmotion(display);
            SetEmotion(e ?? RandomEmotion() ?? _defaultEmotion);
            _ = _speech.Speak(StripActionMarkers(display));
            _ = AutoSave();
        }
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
        AddMessage("user", $"🎁 送给你：{item.Emoji} {item.Name}");
        IsThinking = true;
        try
        {
            var reply = await _gifts.GiftAsync(item);
            reply = await ExecuteMoveMarkersAsync(reply);
            AddMessage("assistant", reply);
            CaptureMoment(_engine.State.CharacterId, item.Price >= 50 ? 5 : 3, $"送礼：{item.Name}");
            AddAffectionPoints(item.Price >= 50 ? 5 : 3, true);
            var e = ResolveEmotion(reply);
            SetEmotion(e ?? RandomEmotion() ?? _defaultEmotion);
            _ = _speech.Speak(reply);
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
        AddMessage("user", $"✨ 我用了：{item.Emoji} {item.Name}");
        IsThinking = true;
        try
        {
            var reply = await _gifts.UseAsync(item);
            reply = await ExecuteMoveMarkersAsync(reply);
            AddMessage("assistant", reply);
            var e = ResolveEmotion(reply);
            SetEmotion(e ?? RandomEmotion() ?? _defaultEmotion);
            _ = _speech.Speak(reply);
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
                var walk = await _map.MoveToAsync(sceneName);
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

    /// <summary>场景库模式被应用（地图/纯库场景）时渲染背景与 BGM。</summary>
    private void OnSceneModeApplied(Models.SceneLibraryEntry entry, Models.SceneMode? mode)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            var bg = mode?.Background is { Length: > 0 } mb ? mb : entry.Background;
            var color = mode?.BackgroundColor is { Length: > 0 } mc ? mc : entry.BackgroundColor;
            if (string.IsNullOrWhiteSpace(bg))
            {
                if (!string.IsNullOrEmpty(color)) SceneBg = color;
                SceneBackdrop = null;
            }
            else
            {
                SceneBackdrop = ImageSource.FromFile(bg);
                if (!string.IsNullOrEmpty(color)) SceneBg = color;
            }
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

            MainThread.BeginInvokeOnMainThread(async () =>
            {
                try { await Shell.Current.GoToAsync("cg-view"); }
                catch (Exception ex) { App.WriteLog("HandleCgMarkers cg-view -> " + ex); }
            });
        }
        return result;
    }

    // ============ 多段回答：按工具标记 / 动作标记切段 ============

    private static readonly HashSet<string> ActionMarkerNames = new() { "下沉", "雀跃", "颤抖" };

    private static readonly Regex ReplyMarkersRegex = new(@"【(移动:[^】]+|CG:[^】]+|下沉|雀跃|颤抖)】", RegexOptions.Compiled);

    private static readonly Regex ActionMarkerRegex = new(@"【(下沉|雀跃|颤抖)】", RegexOptions.Compiled);

    /// <summary>
    /// 把 AI 回复拆成多段：正文按标记位置切段，移动/CG 工具调用就地执行（各自算一段），
    /// 动作标记直接执行动画；开发者展示模式保留动作标记文本，普通模式隐藏。
    /// </summary>
    private async Task<(List<string> Segments, bool HasReaction)> ProcessReplyAsync(string reply)
    {
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

    /// <summary>执行一次【移动:场景名】，返回移动过程描述（作为一段回答展示）。</summary>
    private async Task<string?> ExecuteSingleMoveAsync(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName)) return null;
        try
        {
            IsWalking = true;
            return await _map.MoveToAsync(sceneName);
        }
        catch (Exception ex)
        {
            App.WriteLog("MainGame.ExecuteSingleMoveAsync -> " + ex);
            return null;
        }
        finally { IsWalking = false; }
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
            MainThread.BeginInvokeOnMainThread(async () =>
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
        MainThread.BeginInvokeOnMainThread(() =>
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
    /// <summary>截取美少女主窗口客户区（顶部以下到信息栏以上），存 PNG 并返回路径。</summary>
    private static string? CaptureWindowSnapshot()
    {
        try
        {
            var wnd = Application.Current?.Windows.FirstOrDefault(w => w.Handler is not null)
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
        Affection = Math.Min(100, Affection + 2);
        AddMessage("assistant", "小雨被摸了摸头，害羞地笑了~");
        UpdateStats();
        AddAffectionPoints(2, true);
        SetEmotionAny("害羞", "闭眼微笑", "开心");
        CaptureMoment(_engine.State.CharacterId, 2, "摸头");
        _ = SinkAnimationAsync();
        await AutoSave();
    }

    [RelayCommand]
    private async Task Hug()
    {
        Affection = Math.Min(100, Affection + 3);
        Trust = Math.Min(100, Trust + 2);
        AddMessage("assistant", "小雨轻轻抱住了你，感觉很温暖。");
        UpdateStats();
        AddAffectionPoints(3, true);
        SetEmotionAny("温柔", "闭眼微笑", "开心");
        CaptureMoment(_engine.State.CharacterId, 3, "拥抱");
        _ = JumpAnimationAsync();
        await AutoSave();
    }

    [RelayCommand]
    private async Task Kiss()
    {
        if (Affection < 40)
        {
            AddMessage("assistant", "小雨脸红了，躲开了... 好感度还不够呢。");
            return;
        }
        Affection = Math.Min(100, Affection + 5);
        Trust = Math.Min(100, Trust + 3);
        AddMessage("assistant", "小雨踮起脚尖，在你脸颊上轻轻一吻~");
        UpdateStats();
        AddAffectionPoints(5, true);
        SetEmotionAny("害羞", "惊讶", "开心");
        CaptureMoment(_engine.State.CharacterId, 5, "亲吻");
        _ = ShakeAnimationAsync();
        await AutoSave();
    }

    [RelayCommand]
    private async Task OpenPhone() => await Shell.Current.GoToAsync("phone");

    [RelayCommand]
    private async Task OpenMap() => await Shell.Current.GoToAsync("map");

    [RelayCommand]
    private async Task OpenFullMap() => await Shell.Current.GoToAsync("map");

    [RelayCommand]
    private async Task OpenWorldbook() => await Shell.Current.GoToAsync("worldbook");

    [RelayCommand]
    private async Task OpenSave() => await Shell.Current.GoToAsync("save");

    [RelayCommand]
    private async Task OpenSettings() => ShowSettings = !ShowSettings;

    [RelayCommand]
    private async Task OpenOutfit() => await Shell.Current.GoToAsync("outfit");

    [RelayCommand]
    private async Task OpenGallery() => await Shell.Current.GoToAsync("gallery");

    [RelayCommand]
    private async Task OpenRoster() => await Shell.Current.GoToAsync("roster");

    [RelayCommand]
    private async Task OpenMaterials() => await Shell.Current.GoToAsync("materials");

    /// <summary>进入战斗页面</summary>
    [RelayCommand]
    private async Task OpenBattle() => await Shell.Current.GoToAsync("battle");

    /// <summary>收纳到桌面（桌宠模式）：主窗口隐藏，桌面只留立绘，托盘可恢复。</summary>
    [RelayCommand]
    private void PetMode() => PetService.TogglePetModeStatic();

    [RelayCommand]
    private async Task QuickSave()
    {
        var ok = await _save.Commit("快速存档");
        if (ok) await Shell.Current.DisplayAlert("", "已保存", "好");
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

    [RelayCommand]
    private async Task Menu()
    {
        var act = await Shell.Current.DisplayActionSheet("菜单", "取消", null, "设置", "存档管理", "回标题");
        switch (act)
        {
            case "设置": await Shell.Current.GoToAsync("settings"); break;
            case "存档管理": await Shell.Current.GoToAsync("save"); break;
            case "回标题": await _save.Commit("存档"); _auto.Stop(); await Shell.Current.GoToAsync(".."); break;
        }
    }

    [RelayCommand]
    private void ToggleAuto()
    {
        IsAuto = !IsAuto;
        _engine.ToggleAuto();
    }

    private void AddMessage(string role, string text)
    {
        Messages.Add(new DialogueMessage
        {
            Role = role,
            Text = text,
            Time = DateTime.Now.ToString("HH:mm")
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
        AffectionPct = Affection / 100.0;
        TrustPct = Trust / 100.0;
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
        _phys.Day = (int)(info.Now - DateTime.Today).TotalMinutes / 60;
        PhysLabel = _phys.Label;
    }

    private static readonly string[] WeekCn = { "日", "一", "二", "三", "四", "五", "六" };

    /// <summary>精力值：按「今天醒来的时长」递减——从当天 7 点起算，醒得越久精力越低，午夜清零重算。</summary>
    private void UpdateEnergy(DateTime now)
    {
        var wake = now.Date.AddHours(7);          // 设定每日早上 7 点为醒来时刻
        var awakeHours = Math.Max(0, (now - wake).TotalHours);
        Energy = Math.Clamp(100 - (int)(awakeHours * 2.5), 0, 100);   // 每清醒 1 小时约掉 2.5，醒 40 小时才接近 0
        EnergyPct = Energy / 100.0;
    }

    private async Task FetchWeather()
    {
        var w = await _weather.Fetch();
        if (w is not null) WeatherDesc = w.Description;
    }
}

public sealed class DialogueMessage
{
    public string Role { get; set; } = "";
    public string Text { get; set; } = "";
    public string Time { get; set; } = "";
    public bool IsUser => Role == "user";
}

/// <summary>地图 overlay 里的一行场景（x:DataType 绑定的行模型）。</summary>
public sealed class MapSceneOption
{
    public string SceneId { get; init; } = "";
    public string Label { get; init; } = "";
    public bool IsCurrent { get; init; }
}