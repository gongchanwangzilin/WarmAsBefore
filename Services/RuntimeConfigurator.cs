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
    private readonly GlassOverlayService _glassOverlay;
    private readonly NotificationService _notify;
    private bool _notifyHooked;

    public RuntimeConfigurator(SettingsManager settings, ChatEngine chat, WeatherProvider weather,
        PhysiologicalTracker phys, SpeechService speech, TaskOrchestrator auto, DailyDiaryWriter diary,
        CharacterLibrary characters, OfficialChatBridge bridge, DesignSystem.Theme.ThemeManager theme,
        GlassOverlayService glassOverlay, NotificationService notify)
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
        _glassOverlay = glassOverlay;
        _notify = notify;
    }

    public void Start()
    {
        _glassOverlay.Start();
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
            _ = _characters.LoadAsync();
        });
        // 设置立即下发，保证首帧即生效
        Apply();
        _settings.Applied += Apply;
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

        _chat.Configure(new AiEndpoint
        {
            Url = s.AiUrl,
            Key = s.AiKey,
            Model = s.AiModel,
            Temperature = s.AiTemperature,
            MaxTokens = s.AiMaxTokens,
            DeepThink = s.DeepThink || s.ComplexPlot,
            DeepModel = s.DeepModel,
            MemoryTurns = s.MemoryTurns
        });

        _weather.SetCity(s.WeatherCity);
        _phys.SetCycle(s.CycleLength, s.PeriodLength);
        _speech.TtsEnabled = s.TtsEnabled;
        _speech.TtsRate = s.TtsRate;
        _speech.SttEnabled = s.SttEnabled;
        _speech.TtsEngine = string.IsNullOrWhiteSpace(s.TtsEngine) ? "system" : s.TtsEngine;
        _speech.SttEngine = string.IsNullOrWhiteSpace(s.SttEngine) ? "system" : s.SttEngine;
        _speech.VoiceApiUrl = string.IsNullOrWhiteSpace(s.VoiceApiUrl) ? "https://api.openai.com/v1" : s.VoiceApiUrl;
        _speech.VoiceApiKey = s.VoiceApiKey;
        _speech.VoiceTtsModel = string.IsNullOrWhiteSpace(s.VoiceTtsModel) ? "tts-1" : s.VoiceTtsModel;
        _speech.VoiceSttModel = string.IsNullOrWhiteSpace(s.VoiceSttModel) ? "whisper-1" : s.VoiceSttModel;
        _speech.VoiceName = string.IsNullOrWhiteSpace(s.VoiceName) ? "alloy" : s.VoiceName;

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
        _bridge.Apply();
    }
}
