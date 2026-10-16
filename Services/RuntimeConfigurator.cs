using WarmAsBefore.Modules.AiChat;
using WarmAsBefore.Modules.Automation;
using WarmAsBefore.Modules.RealChat;
using WarmAsBefore.Modules.RealWorld;
using WarmAsBefore.Models;

namespace WarmAsBefore.Services;

/// <summary>
/// 运行时配置器：把用户设置实时下发到 AI、天气、生理、语音、问候、玻璃效果与官方接入桥。
/// 启动时与设置变更时各执行一次。
/// </summary>
public sealed class RuntimeConfigurator
{
    private readonly SettingsManager _settings;
    private readonly ChatEngine _chat;
    private readonly WeatherProvider _weather;
    private readonly PhysiologicalTracker _phys;
    private readonly SpeechService _speech;
    private readonly TaskOrchestrator _auto;
    private readonly DailyDiaryWriter _diary;
    private readonly CharacterLibrary _characters;
    private readonly OfficialChatBridge _bridge;
    private readonly DesignSystem.Theme.ThemeManager _theme;
    private readonly NotificationService _notify;
    private readonly CharacterSettingsResolver _charSettings;
    private readonly CharacterSubProfileService _subProfiles;
    private readonly HumanMemoryService _human;
    private readonly SemanticNetworkService _semantic;
    private readonly MemoryTagService _tags;
    private readonly Modules.AiChat.MemoryVault _memory;
    private readonly Modules.Tools.ToolManager _tools;
    private bool _notifyHooked;

    public RuntimeConfigurator(SettingsManager settings, ChatEngine chat, WeatherProvider weather,
        PhysiologicalTracker phys, SpeechService speech, TaskOrchestrator auto, DailyDiaryWriter diary,
        CharacterLibrary characters, OfficialChatBridge bridge, DesignSystem.Theme.ThemeManager theme,
        NotificationService notify, CharacterSettingsResolver charSettings,
        CharacterSubProfileService subProfiles, HumanMemoryService human,
        SemanticNetworkService semantic, MemoryTagService tags,
        Modules.AiChat.MemoryVault memory, Modules.Tools.ToolManager tools)
    {
        _settings = settings;
        _chat = chat;
        _weather = weather;
        _phys = phys;
        _speech = speech;
        _auto = auto;
        _diary = diary;
        _characters = characters;
        _bridge = bridge;
        _theme = theme;
        _notify = notify;
        _charSettings = charSettings;
        _subProfiles = subProfiles;
        _human = human;
        _semantic = semantic;
        _tags = tags;
        _memory = memory;
        _tools = tools;
    }

    public void Start()
    {
        // 通知服务此前没有任何订阅者：OfficialChatBridge 调用的 Show() 全部丢进了空事件，
        // 所以「通知」根本不出现。这里接上唯一的消费端。
        if (!_notifyHooked)
        {
            _notifyHooked = true;
            _notify.Notify += OnNotify;
        }
        // 每日日记与角色库加载不阻塞首帧：延迟到首帧后再执行
        MainThread.BeginInvokeOnMainThread(() =>
        {
            _diary.Start();
            _ = LoadCharactersAsync();
        });
        // 设置立即下发，保证首帧即生效
        Apply();
        _settings.Applied += Apply;
        // 能力包清单（skill）：只放名字与用途，正文由 AI 按需 read_skill
        _ = _tools.RefreshSkillHintAsync();
    }

    /// <summary>加载角色库并给旧存档里的角色补种副设定（幂等）。</summary>
    private async Task LoadCharactersAsync()
    {
        await _characters.LoadAsync();
        try { await _subProfiles.EnsureSeededAsync(); }
        catch (Exception ex) { App.WriteLog("RuntimeConfigurator.EnsureSubProfiles -> " + ex.Message); }
    }

    /// <summary>
    /// 通知落地：先让窗口真的可见、可点，再弹对话框。
    /// 桌宠模式下主窗口是 Hide 状态，若不先恢复，DisplayAlert 会挂在一个不可见的窗口上
    /// —— 这正是「通知/对话框沉底、点不到」的成因。
    /// </summary>
    private void OnNotify(string title, string msg)
    {
        if (!_settings.Current.NotificationsEnabled) return;

        MainThread.BeginInvokeOnMainThread(async () =>
        {
            try
            {
                PetService.ShowMainWindowStatic();
                WindowTopmost.BringToFront();
                var shell = Shell.Current;
                if (shell is not null) await shell.DisplayAlert(title, msg, "好");
            }
            catch (Exception ex)
            {
                App.WriteLog("NotificationService.Show -> " + ex.Message);
            }
        });
    }

    private void Apply()
    {
        var s = _settings.Current;

        // 多端点轮换池（供应商 × AI × 密钥）：从设置里的端点档案展开，
        // 每条端点的密钥池会轮流使用；轮换开启时任一失灵自动切下一条，保证对话不间断。
        var rotationPool = new List<AiEndpoint>();
        foreach (var p in s.AiProfiles ?? new List<AiEndpointProfile>())
        {
            if (!p.Enabled) continue;
            if (string.IsNullOrWhiteSpace(p.Url) || string.IsNullOrWhiteSpace(p.Model)) continue;
            var keys = (p.Keys ?? new List<string>())
                .Where(k => !string.IsNullOrWhiteSpace(k)).ToList();
            if (keys.Count == 0) continue;
            rotationPool.Add(new AiEndpoint
            {
                Provider = string.IsNullOrWhiteSpace(p.Name) ? "轮换端点" : p.Name,
                Url = p.Url.Trim(),
                Model = p.Model.Trim(),
                Key = keys[0],
                KeyPool = keys,
                Temperature = s.AiTemperature,
                MaxTokens = s.AiMaxTokens,
                DeepThink = s.DeepThink || s.ComplexPlot,
                DeepModel = s.DeepModel,
                MemoryTurns = s.MemoryTurns,
                ContextTokenLimit = s.ContextTokenLimit
            });
        }

        _chat.Configure(new AiEndpoint
        {
            Url = s.AiUrl,
            Key = s.AiKey,
            Model = s.AiModel,
            Temperature = s.AiTemperature,
            MaxTokens = s.AiMaxTokens,
            DeepThink = s.DeepThink || s.ComplexPlot,
            DeepModel = s.DeepModel,
            MemoryTurns = s.MemoryTurns,
            ContextTokenLimit = s.ContextTokenLimit > 0 ? s.ContextTokenLimit : 32000
        }, rotationPool, s.AiRotationEnabled && rotationPool.Count > 0);
        // 角色级覆写：Send/Retry 时按当前角色叠加，未设置覆写的角色行为不变
        _chat.Resolver = _charSettings;
        // 副设定：人设里带上「当下之我」，每回合结束后按状态漂移一次
        _chat.SubProfileProvider = _subProfiles.Describe;
        _chat.AfterTurn = (charId, note) => _subProfiles.DriftAsync(charId, "对话之后", note);
        // 人类记忆层：回合结束把折叠出的记忆记进去，每轮对话前按人类方式回想
        _chat.Human = _human;
        // 词义关系网与标签层：对话自动长网、标签清单注入人设
        _chat.Semantic = _semantic;
        _chat.Tags = _tags;
        // 好感时刻/带截图的时刻也进人类记忆层（照片式记忆）
        _memory.Human = _human;

        _weather.SetCity(s.WeatherCity);
        _phys.SetCycle(s.CycleLength, s.PeriodLength);
        _speech.TtsEnabled = s.TtsEnabled;
        _speech.TtsRate = s.TtsRate;
        _speech.SttEnabled = s.SttEnabled;
        _speech.TtsEngine = string.IsNullOrWhiteSpace(s.TtsEngine) ? "system" : s.TtsEngine;
        _speech.SttEngine = string.IsNullOrWhiteSpace(s.SttEngine) ? "system" : s.SttEngine;
        _speech.SttModelName = s.SttModelName ?? "";
        _speech.VoiceApiUrl = string.IsNullOrWhiteSpace(s.VoiceApiUrl) ? "https://api.openai.com/v1" : s.VoiceApiUrl;
        _speech.VoiceApiKey = s.VoiceApiKey;
        _speech.VoiceTtsModel = string.IsNullOrWhiteSpace(s.VoiceTtsModel) ? "tts-1" : s.VoiceTtsModel;
        _speech.VoiceSttModel = string.IsNullOrWhiteSpace(s.VoiceSttModel) ? "whisper-1" : s.VoiceSttModel;
        _speech.VoiceName = string.IsNullOrWhiteSpace(s.VoiceName) ? "alloy" : s.VoiceName;
        _speech.VoiceApiMode = string.IsNullOrWhiteSpace(s.VoiceApiMode) ? "openai" : s.VoiceApiMode;
        _speech.VoiceExtra = s.VoiceExtra ?? "";

        _auto.Enabled = s.GreetingEnabled;
        if (s.GreetingEnabled && !_auto.Running) _auto.Start();
        else if (!s.GreetingEnabled && _auto.Running) _auto.Stop();

        // 玻璃效果：毛玻璃是磨砂的高级版，ThemeManager 内部已保证依赖
        _theme.Frost = s.FrostEnabled;
        _theme.Glass = s.GlassEnabled;
        _theme.Liquid = s.LiquidEnabled;
        _theme.GlassTranslucency = s.GlassTranslucency;
        _theme.ReducedTransparency = s.GlassReducedTransparency;
        _theme.ThemeName = string.IsNullOrEmpty(s.ThemeName) ? "classic" : s.ThemeName;

        // 置顶：开启 AlwaysOnTop 时持续强制常驻顶层（3 秒重断言防被抢占）；关闭则回落
        if (s.AlwaysOnTop)
            WindowTopmost.Force();
        else
            WindowTopmost.Release();
        // 背景：纯色 / 自定义图片 / 窗口透视 —— 玻璃需要有东西可透
        AppBackgroundService.Apply(s.BackgroundMode, s.BackgroundImagePath,
            Application.Current?.Resources.TryGetValue("PageBg", out var pv) == true && pv is Color pc
                ? pc : Colors.White);

        _bridge.Apply();
    }
}
