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
    private readonly Modules.ApiManager.ApiGateway _api;
    private readonly CostEstimatorService _cost;
    private bool _notifyHooked;

    public RuntimeConfigurator(SettingsManager settings, ChatEngine chat, WeatherProvider weather,
        PhysiologicalTracker phys, SpeechService speech, TaskOrchestrator auto, DailyDiaryWriter diary,
        CharacterLibrary characters, OfficialChatBridge bridge, DesignSystem.Theme.ThemeManager theme,
        NotificationService notify, CharacterSettingsResolver charSettings,
        CharacterSubProfileService subProfiles, HumanMemoryService human,
        SemanticNetworkService semantic, MemoryTagService tags,
        Modules.AiChat.MemoryVault memory, Modules.Tools.ToolManager tools,
        Modules.ApiManager.ApiGateway api, CostEstimatorService cost)
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
        _api = api;
        _cost = cost;
    }

    /// <summary>把设置里的单价表同步进成本服务，并把真实 usage 接到成本账上。</summary>
    private async Task SyncModelPricesAsync(List<ModelPrice>? prices)
    {
        try
        {
            if (!_usageHooked)
            {
                _usageHooked = true;
                _api.UsageReported += (model, prompt, completion) => _cost.Record(model, prompt, completion);
            }
            if (prices is null) return;
            var existing = await _cost.PricesAsync();
            foreach (var p in prices.Where(p => !string.IsNullOrWhiteSpace(p.Model)))
            {
                var cur = existing.FirstOrDefault(e => string.Equals(e.Model, p.Model, StringComparison.OrdinalIgnoreCase));
                if (cur is not null && Math.Abs(cur.InputPerMillion - p.InputPerMillion) < 1e-9
                    && Math.Abs(cur.OutputPerMillion - p.OutputPerMillion) < 1e-9) continue;
                await _cost.SetPriceAsync(p.Model, p.InputPerMillion, p.OutputPerMillion);
            }
        }
        catch (Exception ex) { App.WriteLog("RuntimeConfigurator.SyncModelPrices -> " + ex.Message); }
    }

    private bool _usageHooked;

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

    /// <summary>上一份已下发的设置：用来跳过「这次没变」的分区（每次点击都会走一遍 Apply）。</summary>
    private UserSettings? _lastApplied;
    private string _lastPriceSignature = "";

    private void Apply()
    {
        var s = _settings.Current;
        using var _total = PerfLog.Measure("Apply 总计");
        var prev = _lastApplied;
        _lastApplied = s;

        // 多端点轮换池（厂商 × 模型）：一家厂商可挂多款模型，按「启用的厂商 × 启用的模型」展开；
        // 每条端点的密钥池会轮流使用；轮换开启时任一失灵自动切下一条，保证对话不间断。
        // 老设置里只有 AiProfiles（一条 = 一个模型）时，用它兜底（启动时已迁移成厂商，这里只是保底）。
        var rotationPool = new List<AiEndpoint>();
        var providers = s.AiProviders ?? new List<AiProvider>();
        if (providers.Count > 0)
        {
            foreach (var v in providers)
            {
                if (!v.Enabled) continue;
                if (string.IsNullOrWhiteSpace(v.Url)) continue;
                var keys = (v.Keys ?? new List<string>()).Where(k => !string.IsNullOrWhiteSpace(k)).ToList();
                if (keys.Count == 0) continue;
                foreach (var m in (v.Models ?? new List<AiProviderModel>()).Where(m => m.Enabled && !string.IsNullOrWhiteSpace(m.Model)))
                {
                    rotationPool.Add(new AiEndpoint
                    {
                        Provider = string.IsNullOrWhiteSpace(v.Name) ? m.Model : v.Name,
                        Url = v.Url.Trim(),
                        Model = m.Model.Trim(),
                        Key = keys[0],
                        KeyPool = keys,
                        Temperature = s.AiTemperature,
                        MaxTokens = s.AiMaxTokens,
                        DeepThink = s.DeepThink || s.ComplexPlot,
                        DeepModel = s.DeepModel,
                        MemoryTurns = s.MemoryTurns,
                        ContextTokenLimit = s.ContextTokenLimit,
                        TimeoutSeconds = v.TimeoutSeconds
                    });
                }
            }
        }
        else
        {
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
                    ContextTokenLimit = s.ContextTokenLimit,
                    TimeoutSeconds = p.TimeoutSeconds
                });
            }
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
        // 超时：全局值（端点可在上面的档案里单独覆写）
        _api.GlobalTimeoutSeconds = s.AiTimeoutSeconds > 0 ? s.AiTimeoutSeconds : 25;
        // 成本：单价表由设置页维护，用量由 ApiGateway 的真实 usage 记账。
        // 表没变就不重复读文件（每次点击都会走 Apply）。
        var priceSig = string.Join("|", (s.ModelPrices ?? new List<ModelPrice>())
            .Select(x => $"{x.Model}:{x.InputPerMillion}:{x.OutputPerMillion}"));
        if (priceSig != _lastPriceSignature)
        {
            _lastPriceSignature = priceSig;
            _ = SyncModelPricesAsync(s.ModelPrices);
        }
        // 角色级覆写：Send/Retry 时按当前角色叠加，未设置覆写的角色行为不变
        _chat.Resolver = _charSettings;
        // 副设定：人设里带上「当下之我」，每回合结束后按状态漂移一次
        _chat.SubProfileProvider = _subProfiles.Describe;
        _chat.AfterTurn = (charId, note) => _subProfiles.DriftAsync(charId, "对话之后", note);
        // 副设定的模型改写（默认关闭）：每 12 轮才问一次模型「她现在是怎样的人」
        _chat.SubProfileAiRewriteEnabled = s.SubProfileAiRewrite;
        _chat.SubProfileMaterial = _subProfiles.BuildRewriteMaterialAsync;
        _chat.SubProfileRewritten = (charId, personality, description) =>
            _subProfiles.RewriteAsync(charId, personality, description, "经历之后，她对自己的看法变了");
        // 人类记忆层：回合结束把折叠出的记忆记进去，每轮对话前按人类方式回想
        _chat.Human = _human;
        // 智能省钱：总结前先算「现在总结 vs 不总结」哪个更省（关掉则恢复老行为）
        _chat.Cost = s.SmartSavingEnabled ? _cost : null;
        // 词义关系网与标签层：对话自动长网、标签清单注入人设
        _chat.Semantic = _semantic;
        _chat.Tags = _tags;
        // 关系网的模型辅助（默认关闭）：每 20 轮补几条关联
        _chat.SemanticAiAssistEnabled = s.SemanticAiAssist;
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

        // 玻璃效果：毛玻璃是磨砂的高级版，ThemeManager 内部已保证依赖。
        // 只有相关设置真的变了才下发——主题一变会重算画刷并通知订阅者，整页都要重排。
        var themeChanged = prev is null
            || prev.FrostEnabled != s.FrostEnabled || prev.GlassEnabled != s.GlassEnabled
            || prev.LiquidEnabled != s.LiquidEnabled || Math.Abs(prev.GlassTranslucency - s.GlassTranslucency) > 1e-6
            || prev.GlassReducedTransparency != s.GlassReducedTransparency
            || prev.GlassFrost != s.GlassFrost || prev.GlassHeight != s.GlassHeight
            || prev.GlassLightX != s.GlassLightX || prev.GlassLightY != s.GlassLightY
            || prev.GlassLightZ != s.GlassLightZ || prev.GlassLightWidth != s.GlassLightWidth
            || prev.LightMode != s.LightMode
            || !string.Equals(prev.ThemeName, s.ThemeName, StringComparison.Ordinal);
        if (themeChanged)
        {
            using var _themeScope = PerfLog.Measure("  · 主题下发");
            WarmAsBefore.DesignSystem.Theme.GlassTokens.LightMode = s.LightMode;
            _theme.Frost = s.FrostEnabled;
            _theme.Glass = s.GlassEnabled;
            _theme.Liquid = s.LiquidEnabled;
            _theme.GlassTranslucency = s.GlassTranslucency;
            _theme.ReducedTransparency = s.GlassReducedTransparency;
            _theme.ThemeName = string.IsNullOrEmpty(s.ThemeName) ? "classic" : s.ThemeName;
        }

        // 置顶：开启 AlwaysOnTop 时持续强制常驻顶层（3 秒重断言防被抢占）；关闭则回落。
        // 未变化时不重复调用 Win32（每次点击都调会让窗口 z-order 反复变动）。
        if (prev is null || prev.AlwaysOnTop != s.AlwaysOnTop)
        {
            using var _topScope = PerfLog.Measure("  · 置顶下发");
            if (s.AlwaysOnTop)
                WindowTopmost.Force();
            else
                WindowTopmost.Release();
        }
        // 背景：纯色 / 自定义图片 / 窗口透视 —— 玻璃需要有东西可透
        var bgChanged = prev is null
            || !string.Equals(prev.BackgroundMode, s.BackgroundMode, StringComparison.Ordinal)
            || !string.Equals(prev.BackgroundImagePath, s.BackgroundImagePath, StringComparison.Ordinal);
        if (bgChanged)
        {
            using var _bgScope = PerfLog.Measure("  · 背景下发");
            AppBackgroundService.Apply(s.BackgroundMode, s.BackgroundImagePath,
                Application.Current?.Resources.TryGetValue("PageBg", out var pv) == true && pv is Color pc
                    ? pc : Colors.White);
        }

        // 官方接入：只有相关配置变了才重配（否则每次点击都会去启动/停止机器人通道）
        var bridgeChanged = prev is null
            || !string.Equals(prev.QqAppId, s.QqAppId, StringComparison.Ordinal)
            || !string.Equals(prev.QqAppSecret, s.QqAppSecret, StringComparison.Ordinal)
            || prev.QqBotEnabled != s.QqBotEnabled
            || !string.Equals(prev.WechatAppId, s.WechatAppId, StringComparison.Ordinal)
            || !string.Equals(prev.WechatAppSecret, s.WechatAppSecret, StringComparison.Ordinal)
            || !string.Equals(prev.WechatToken, s.WechatToken, StringComparison.Ordinal)
            || prev.WechatPort != s.WechatPort
            || prev.WechatEnabled != s.WechatEnabled;
        if (bridgeChanged)
        {
            using var _bridgeScope = PerfLog.Measure("  · 官方接入下发");
            _bridge.Apply();
        }
    }
}
