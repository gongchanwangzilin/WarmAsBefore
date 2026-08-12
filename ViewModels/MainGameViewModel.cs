using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

    [ObservableProperty] private string _locationLabel = "家";
    [ObservableProperty] private string _timeLabel = "";
    [ObservableProperty] private string _speaker = "";
    [ObservableProperty] private string _dialogue = "";
    [ObservableProperty] private bool _isAuto;
    [ObservableProperty] private int _affection = 30;
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
    [ObservableProperty] private ImageSource? _sceneBackdrop;
    [ObservableProperty] private bool _isWalking;
    [ObservableProperty] private ObservableCollection<MapSceneOption> _sceneOptions = new();
    [ObservableProperty] private string _characterName = "小雨";
    [ObservableProperty] private bool _isThinking;
    [ObservableProperty] private ObservableCollection<DialogueMessage> _messages = new();

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

    // ============ 开发者测试向导（右下角文字指挥，逐步验证全功能） ============
    [ObservableProperty] private bool _testWizardVisible;
    [ObservableProperty] private string _testWizardTitle = "开发者测试向导";
    [ObservableProperty] private string _testWizardPrompt = "";   // 当前步骤要求用户做的动作
    [ObservableProperty] private string _testWizardDetail = "";   // 当前检测的实时反馈
    [ObservableProperty] private bool _testWizardDone;            // 全部步骤完成
    [ObservableProperty] private int _testWizardStep;             // 当前步骤号
    [ObservableProperty] private int _testWizardTotal;            // 总步骤数
    [ObservableProperty] private string _testWizardResult = "";   // 汇总：通过/失败列表
    private int _wizardPassed, _wizardFailed;
    private readonly List<string> _wizardChecks = new();
    private SemaphoreSlim _wizardLock = new(1, 1);

    public bool NoSpriteVisible => !SpriteVisible;

    public bool IsGalgamePanelVisible => !IsInMiniGame;

    public double TestWizardProgress => TestWizardTotal == 0 ? 0 : (double)TestWizardStep / TestWizardTotal;
    public string TestWizardProgressText => $"{TestWizardStep}/{TestWizardTotal} · 已通过 {_wizardPassed} · 失败 {_wizardFailed}";
    partial void OnTestWizardStepChanged(int value) { OnPropertyChanged(nameof(TestWizardProgress)); OnPropertyChanged(nameof(TestWizardProgressText)); }
    partial void OnTestWizardTotalChanged(int value) { OnPropertyChanged(nameof(TestWizardProgress)); OnPropertyChanged(nameof(TestWizardProgressText)); }

    partial void OnIsInMiniGameChanged(bool value)
    {
        ShowRightChat = value;
        // 按钮文案 = 点击后进入的目标模式（value=IsInMiniGame）
        GalgameModeLabel = value ? "切回 Galgame" : "切到聊天模式";
        OnPropertyChanged(nameof(IsGalgamePanelVisible));
    }

    [RelayCommand]
    private void ToggleGalgameMode()
    {
        IsInMiniGame = !IsInMiniGame;
    }

    /// <summary>桌面布局：展开/收起右侧对话拉达。用两个无参命令，避免 RelayCommand&lt;bool&gt; 被 XAML 字符串参数坑。</summary>
    [RelayCommand]
    private void ExpandRightPanel() => ShowRightChat = true;

    [RelayCommand]
    private void CollapseRightPanel() => ShowRightChat = false;

    private CharacterData? _char;
    private string _outfitKey = "";
    private string _defaultEmotion = "";
    private string _currentEmotion = "";
    private DateTime _lastAutoSave = DateTime.MinValue;

    public MainGameViewModel(GameEngine engine, ChatEngine chat, MemoryVault memory,
        TaskOrchestrator auto, SaveManager save, WeatherProvider weather, RealTimeProvider time,
        PhysiologicalTracker phys, AudioController audio, SpeechService speech,
        CharacterLibrary chars, StorageProvider store, PetService pet, MapService map,
        Modules.Market.GiftPanelService gifts)
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
            _chat.SetMapContext(BuildMapContext());
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
        var scenes = string.Join("、", _map.Map.AllScenes.Select(s => s.Name));
        if (string.IsNullOrWhiteSpace(scenes)) return "";
        return $"你生活在一座城市里，可以前往这些场景：{scenes}。" +
               "当你觉得应该换个地方（回家、散步、喝咖啡等）时，" +
               "在回复的开头或结尾加上【移动:场景名】标记（场景名必须严格来自上面的列表）。" +
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
        if (scene is null)
        {
            SceneBackdrop = null;
            return;
        }
        var bg = _map.ResolveBackground(scene);
        if (bg is not null)
            SceneBackdrop = ImageSource.FromFile(bg);
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

    private async Task LoadCharacterAsync()
    {
        var charId = _engine.State.CharacterId;
        if (string.IsNullOrEmpty(charId)) return;
        var chars = await _chars.ListAsync();
        var ch = chars.FirstOrDefault(c => c.Profile.Id == charId);
        if (ch is null || ch.SpriteMap.Count == 0) return;
        _char = ch;
        Affection = ch.State.Affection;
        Trust = ch.State.Trust;
        Balance = Math.Max(0, ch.State.Energy);
        UpdateStats();
        var outfit = ch.State.CurrentOutfit;
        if (!ch.SpriteMap.Keys.Any(k => k.StartsWith(outfit + "/", StringComparison.Ordinal)))
            outfit = ch.SpriteMap.Keys.First().Split('/')[0];
        _outfitKey = outfit;
        _defaultEmotion = ch.SpriteMap.Keys.First(k => k.StartsWith(_outfitKey + "/", StringComparison.Ordinal)).Split('/')[1];
        SetEmotion(_defaultEmotion);
        MainThread.BeginInvokeOnMainThread(() =>
        {
            CharacterName = ch.Profile.Name;
            _chat.ConfigureCharacter(ch.Profile);
            _chat.SetRoster(_chars.RosterContext(ch.Profile.Id));
            RestoreSession();
        });
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
        ApplySprite();
    }

    private void ApplySprite()
    {
        if (_char is null || string.IsNullOrEmpty(_outfitKey)) return;
        var key = $"{_outfitKey}/{_currentEmotion}";
        if (!_char.SpriteMap.TryGetValue(key, out var rel))
            rel = _char.SpriteMap[$"{_outfitKey}/{_defaultEmotion}"];
        var full = Path.Combine(_store.Root, rel);
        if (!File.Exists(full)) { SpriteVisible = false; return; }
        if (MainThread.IsMainThread) ApplySpriteCore(full);
        else MainThread.BeginInvokeOnMainThread(() => ApplySpriteCore(full));
    }

    private void ApplySpriteCore(string full)
    {
        SpriteSource = ImageSource.FromFile(full);
        SpriteVisible = true;
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
        var msg = InputText;
        InputText = "";
        AddMessage("user", msg);
        var charId = _engine.State.CharacterId;
        if (!string.IsNullOrEmpty(charId))
        {
            IsThinking = true;
            string reply;
            try
            {
                reply = await _chat.Send(charId, msg);
                reply = await ExecuteMoveMarkersAsync(reply);
            }
            catch (Exception ex)
            {
                // AI 未连接：不崩溃、不空白，自动选一张立绘陪伴，并点明没收到
                App.WriteLog("MainGameViewModel.SendMessage -> " + ex);
                AddMessage("assistant", "……");
                SetEmotionAny("委屈", "难过", "伤心", "低头");
                return;
            }
            finally { IsThinking = false; }
            AddMessage("assistant", reply);
            Affection = Math.Min(100, Affection + 1);
            Trust = Math.Min(100, Trust + 1);
            UpdateStats();
            CaptureMoment(charId, 1, "聊天");
            var e = ResolveEmotion(reply);
            SetEmotion(e ?? RandomEmotion() ?? _defaultEmotion);
            _ = _speech.Speak(reply);
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
        return result;
    }

    [RelayCommand]
    private async Task VoiceInput()
    {
        IsListening = true;
        _speech.OnRecognized += OnSpeechResult;
        await _speech.StartListening();
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
            string? img = null;
#if WINDOWS
            img = Task.Run(() => CaptureWindowSnapshot()).Result;
#endif
            _ = _memory.LogAffection(charId, delta, reason, img);
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
        SetEmotionAny("害羞", "闭眼微笑", "开心");
        CaptureMoment(_engine.State.CharacterId, 2, "摸头");
        _ = AutoSave();
    }

    [RelayCommand]
    private async Task Hug()
    {
        Affection = Math.Min(100, Affection + 3);
        Trust = Math.Min(100, Trust + 2);
        AddMessage("assistant", "小雨轻轻抱住了你，感觉很温暖。");
        UpdateStats();
        SetEmotionAny("温柔", "闭眼微笑", "开心");
        CaptureMoment(_engine.State.CharacterId, 3, "拥抱");
        _ = AutoSave();
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
        SetEmotionAny("害羞", "惊讶", "开心");
        CaptureMoment(_engine.State.CharacterId, 5, "亲吻");
        _ = AutoSave();
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

    /// <summary>收纳到桌面（桌宠模式）：主窗口隐藏，桌面只留立绘，托盘可恢复。</summary>
    [RelayCommand]
    private void PetMode() => _pet.TogglePetMode();

    [RelayCommand]
    private async Task QuickSave()
    {
        _saveCheckCount++;                       // 开发者测试向导步骤 13 检测
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
        var act = await Shell.Current.DisplayActionSheet("菜单", "取消", null, "设置", "存档管理", "开发者模式", "回标题");
        switch (act)
        {
            case "设置": await Shell.Current.GoToAsync("settings"); break;
            case "存档管理": await Shell.Current.GoToAsync("save"); break;
            case "开发者模式": await Shell.Current.GoToAsync("dev"); break;
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

    // ==================== 开发者测试向导 ====================

    /// <summary>页面键盘密令 wzlnb → 启动。逐步骤在右下角用文字指挥用户操作，自动核对状态。</summary>
    public void StartTestWizard()
    {
        if (TestWizardVisible) return;          // 已在跑，防重复
        if (!_wizardLock.Wait(0)) return;
        try
        {
            TestWizardVisible = true;
            TestWizardDone = false;
            TestWizardResult = "";
            _wizardPassed = 0;
            _wizardFailed = 0;
            _wizardChecks.Clear();
            _ = RunWizardAsync();
        }
        finally { _wizardLock.Release(); }
    }

    [RelayCommand]
    private void CloseTestWizard()
    {
        TestWizardVisible = false;
        TestWizardDone = false;
        _wizardRunId++;                          // 使跑动的向导循环作废
    }

    private int _wizardRunId;

    /// <summary>读取属性的线程安全包装（ObservableCollection 不能跨线程读，统一走主线程）。</summary>
    private T ReadUi<T>(Func<T> get) =>
        MainThread.IsMainThread ? get() : MainThread.InvokeOnMainThreadAsync(get).GetAwaiter().GetResult();

    private async Task<bool> WaitForAsync(Func<bool> condition, int timeoutSec, string what)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.Elapsed.TotalSeconds < timeoutSec)
        {
            if (ReadUi(condition)) return true;
            await Task.Delay(300);
        }
        TestWizardDetail = $"超时：{what}（{timeoutSec}s 内未达成）";
        return false;
    }

    /// <summary>向导主循环：每步 = 文字指令 + 状态核对，全部通过 = 全功能正常。</summary>
    private async Task RunWizardAsync()
    {
        var runId = ++_wizardRunId;
        var steps = new (string Title, string Prompt, Func<bool> Check, int TimeoutSec)[]
        {
            // index 0：时钟
            ("时钟显示", "观察：顶部显示当前时间（HH:mm）与日期（M月d日 周X）。",
                () => !string.IsNullOrEmpty(CurrentTimeStr) && CurrentTimeStr.Contains(":") && !string.IsNullOrEmpty(DateLabel), 5),
            // 1：精力值
            ("精力值机制", "观察：顶部/侧栏有「精力 x%」胶囊与进度条，数值 0-100。",
                () => Energy is >= 0 and <= 100 && Math.Abs(EnergyPct - Energy / 100.0) < 0.02, 5),
            // 2：好感度
            ("好感度与信任度", "观察：左侧栏好感度/信任度进度条，数值 0-100。",
                () => Affection is >= 0 and <= 100 && Trust is >= 0 and <= 100, 5),
            // 3：发送消息
            ("发送消息", "请在聊天输入框输入一句话（如：你好呀），然后点击【发送】。",
                () => Messages.Count > _msgsBeforeSend && !string.IsNullOrEmpty(LastMessageText), 90),
            // 4：AI 回复
            ("AI 回复", "等待角色回复：发送的内容应得到一条助手消息（对话卡显示最近发言）。",
                () => Messages.Count > _msgsAfterSend && Messages[^1].Role == "assistant", 120),
            // 5：摸头
            ("摸头互动", "请点击动作区的【摸头】按钮。", () => Messages.Count > _msgsAfterAction, 60),
            // 6：抱抱
            ("抱抱互动", "请点击动作区的【抱抱】按钮。", () => Messages.Count > _msgsAfterHug, 60),
            // 7：亲吻
            ("亲吻互动", "请点击动作区的【亲吻】按钮（若提示好感不足属正常分支）。", () => Messages.Count > _msgsAfterKiss, 60),
            // 8：收起面板
            ("收起对话面板", "请点击 Galgame 卡的【收起对话】按钮。", () => !ShowRightChat, 60),
            // 9：展开面板
            ("展开对话面板", "请点击 Galgame 卡的【展开对话】按钮。", () => ShowRightChat, 60),
            // 10：模式切换
            ("模式切换", "请点击右上角 Galgame 卡区域的【切到聊天模式】按钮，再点回【切回 Galgame】。",
                () => ReadUi(() => IsInMiniGame) == _miniGameOrigi && _miniGameToggled, 60),
            // 11：打开设置
            ("打开设置", "请点击顶栏【设置】按钮，面板应弹出。", () => ShowSettings, 60),
            // 12：关闭设置
            ("关闭设置", "请点击设置面板里的【关闭】按钮。", () => !ShowSettings, 60),
            // 13：快速存档
            ("快速存档", "请点击顶栏【存档】：应弹出「已保存」提示，点击【好】。", () => _saveCheckCount >= 1, 60)
        };
        TestWizardTotal = steps.Length;

        var baseMsgs = ReadUi(() => Messages.Count);
        _msgsBeforeSend = baseMsgs;
        _msgsAfterSend = baseMsgs;
        _msgsAfterAction = baseMsgs;
        _msgsAfterHug = baseMsgs;
        _msgsAfterKiss = baseMsgs;
        _saveCheckCount = 0;
        _settingsOpened = false;
        _miniGameToggled = false;
        _miniGameOrigi = ReadUi(() => IsInMiniGame);

        for (var i = 0; i < steps.Length; i++)
        {
            if (runId != _wizardRunId) return;   // 被关闭
            // 每步执行前刷新基准快照
            switch (i)
            {
                case 0: break;
                case 3: _msgsBeforeSend = ReadUi(() => Messages.Count); break;
                case 4: _msgsAfterSend = ReadUi(() => Messages.Count); break;
                case 5: _msgsAfterAction = ReadUi(() => Messages.Count); break;
                case 6: _msgsAfterHug = ReadUi(() => Messages.Count); break;
                case 7: _msgsAfterKiss = ReadUi(() => Messages.Count); break;
                case 8: break;
                case 9: break;
                case 10: break;
                case 11: break;
                case 12: _saveCheckCount = 0; break;
            }
            // 随时观察设置是否已被打开过（供第 12 步判定）
            if (i == 11 && ReadUi(() => ShowSettings)) _settingsOpened = true;
            // 模式切换：出现一次翻转即认为用户执行过
            if (i == 10 && ReadUi(() => IsInMiniGame) != _miniGameOrigi) _miniGameToggled = true;

            var (title, prompt, check, timeout) = steps[i];
            TestWizardStep = i + 1;
            TestWizardTitle = $"第 {i + 1}/{steps.Length} 步 · {title}";
            TestWizardPrompt = prompt;
            TestWizardDetail = "等待操作…";

            var ok = await WaitForAsync(check, timeout, title);
            if (runId != _wizardRunId) return;
            if (ok)
            {
                _wizardPassed++;
                _wizardChecks.Add($"✓ {title}");
                TestWizardDetail = "通过";
            }
            else
            {
                _wizardFailed++;
                _wizardChecks.Add($"✗ {title}");
                TestWizardDetail = "超时/失败";
            }
            OnPropertyChanged(nameof(TestWizardProgressText));
            await Task.Delay(400);
        }

        TestWizardResult = _wizardFailed == 0
            ? $"全部测试通过（{_wizardPassed}/{steps.Length}）—— 全功能正常 ✓"
            : $"通过 {_wizardPassed}/{steps.Length}，失败 {_wizardFailed} 项：\n{string.Join("\n", _wizardChecks.Where(c => c.StartsWith("✗")))}";
        TestWizardDone = true;
        TestWizardPrompt = _wizardFailed == 0 ? "全部功能验证完毕！" : "有失败项，见右侧结果。";
    }

    private int _msgsBeforeSend, _msgsAfterSend, _msgsAfterAction, _msgsAfterHug, _msgsAfterKiss;
    private bool _miniGameOrigi, _miniGameToggled, _settingsOpened;
    private int _saveCheckCount;
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