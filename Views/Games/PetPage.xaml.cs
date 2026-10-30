using System.IO;
using WarmAsBefore.Helpers;
using WarmAsBefore.Modules.AiChat;
using WarmAsBefore.Services;

namespace WarmAsBefore.Views;

/// <summary>
/// 桌宠这一页在两个平台上是两种东西：
/// Windows 上它只剩「头顶那条对话小窗」（输入框 + 回复气泡），角色本体由
/// PetLayeredWindow 按像素 alpha 直接贴在桌面上；小窗靠整窗显隐出现，鼠标靠近头顶才亮。
/// 移动端它仍是全屏问答页：立绘、捏合缩放、拖动都在这张 XAML 里。
/// 单击角色=嘀咕一句，双击=回到主界面；滚轮（Windows）缩放角色。
/// </summary>
public partial class PetPage : ContentPage
{
    private readonly GameEngine _engine;
    private readonly CharacterLibrary _chars;
    private readonly StorageProvider _store;
    private readonly ChatEngine _chat;
    private readonly SettingsManager _settings;
    private readonly Modules.AiChat.MemoryVault _memory;
    private readonly SpeechService _speech;
    private Models.CharacterData? _ch;   // 回完话再看一眼她的心情：桌宠那帧表情跟着换
    private double _scale = 1.0;
    // 与 UserSettings.PetScale 同一档：两端用两套上下限，就会出现「手机上能捏到 3 倍、设置页滑杆却只到 2.6」。
    private const double MinScale = 0.4;
    private const double MaxScale = 2.6;
    private bool _thinking;
    private bool _alive = true;
    private bool _listening;
    private int _pendingSingleTap;
    private IDispatcherTimer? _hoverTimer;
    private static readonly System.Text.RegularExpressions.Regex MachineMarkers =
        new(@"【[^】]*】", System.Text.RegularExpressions.RegexOptions.Compiled);
    public PetPage(GameEngine engine, CharacterLibrary chars, StorageProvider store, ChatEngine chat,
        SettingsManager settings, Modules.AiChat.MemoryVault memory, SpeechService speech)
    {
        InitializeComponent();
        _engine = engine;
        _chars = chars;
        _store = store;
        _chat = chat;
        _settings = settings;
        _memory = memory;
        _speech = speech;
        Loaded += OnLoaded;
        Unloaded += (_, _) => { _alive = false; _hoverTimer?.Stop(); };
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        // 桌宠页没有 ViewModel，也没有「数据到位」这个时刻（立绘是 Loaded 里逐张换的），
        // 所以进场只让根浮一次；骨架留给有装载信号的页面。
        Motion.ArrivePage(this);
    }

    private async void OnLoaded(object? sender, EventArgs e)
    {
#if WINDOWS
        // 放在最前面：下面几处「没角色/没立绘」的 return 不该把小窗的显隐也一起跳过
        StartHoverReveal();
#endif
        try
        {
            // 角色解析统一走 PetService：显式绑定优先，否则跟随当前存档主角。
            var resolved = await PetService.ResolvePetCharacterAsync();
            if (resolved is not { } r) return;
            var ch = r.Character;
            _ch = ch;
#if WINDOWS
            // 立绘不进 XAML：交给分层窗口按像素贴图，角色才真的站在桌面上
            PetService.AttachSprite(r.SpritePath);
            // AI 给这个角色做过 Q 版桌宠就换成帧动画；没做过时这里什么都不做，仍是上面那张立绘
            PetService.AttachAnimation(ch.Profile.Id, ch.State.CurrentEmotion);
            AttachPetSurface();
#else
            SpriteImage.Source = ImageSource.FromFile(r.SpritePath);
            // 体型是盘上的持久值：不读它每次进页都回到 1.0，手机上就是「捏小了，下次进来又巨大」
            _scale = Math.Clamp(_settings.Current.PetScale, MinScale, MaxScale);
            SpriteImage.Scale = _scale;
#endif
            NameLabel.Text = ch.Profile.Name;
        }
        catch (Exception ex)
        {
            App.WriteLog("PetPage.OnLoaded -> " + ex);
        }
        StartThinkLoop();
    }
    private void OnDoubleTapped(object sender, TappedEventArgs e) => BackToMain();

    /// <summary>单击立绘：延迟 350ms 判定——没有第二击时触发一次「思考」嘀咕。</summary>
    private async void OnSingleTapped(object? sender, TappedEventArgs e) => await QueueThinkAsync();

    private async Task QueueThinkAsync()
    {
        try
        {
            var id = ++_pendingSingleTap;
            await Task.Delay(350);
            if (!_alive || _pendingSingleTap != id) return;
            _pendingSingleTap = 0;
            await DoThinkAsync();
        }
        catch (Exception ex) { App.WriteLog("PetPage.QueueThink -> " + ex); }
    }

    private void BackToMain()
    {
        _pendingSingleTap++; // 让待发的单击思考失效（双击优先）
        PetService.ShowMainWindowStatic();
    }

    // ============ #E3 桌宠思考 ============

    /// <summary>
    /// 思考循环的启动（进入桌宠时调用一次）：mode=off 不启动；random/timer 按间隔触发。
    /// 回应模式是更外面的闸：click=不主动开口，quiet=把主动开口减到偶发。
    /// </summary>
    private void StartThinkLoop()
    {
        var mode = _settings.Current.PetThinkMode;
        if (mode != "random" && mode != "timer") return;
        if (_settings.Current.PetRespondMode == "click") return;
        var minutes = Math.Max(1, _settings.Current.PetThinkIntervalMinutes);
        _ = ThinkLoopAsync(minutes);
    }

    private async Task ThinkLoopAsync(int minutes)
    {
        while (_alive)
        {
            await Task.Delay(TimeSpan.FromMinutes(minutes));
            if (!_alive) return;
            var mode = _settings.Current.PetThinkMode;
            if (mode != "random" && mode != "timer") return;
            if (_settings.Current.PetRespondMode == "click") return;
            var quiet = _settings.Current.PetRespondMode == "quiet";
            // 随机档本来就掷一半；少插话再压一档，定时档也从「必想一次」变成偶发
            var chance = quiet ? (mode == "random" ? 15 : 25) : (mode == "random" ? 50 : 100);
            if (Random.Shared.Next(100) >= chance) continue;
            await MainThread.InvokeOnMainThreadAsync(DoThinkAsync);
        }
    }

    /// <summary>把气泡亮起来；Windows 上这条决定小窗该多高、还要亮多久（鼠标离开后再留一会儿读）。</summary>
    private void ShowBubble()
    {
        ReplyBubble.IsVisible = true;
#if WINDOWS
        _bubbleUntil = DateTime.UtcNow.AddSeconds(14);
        PetService.SetChrome(true, true);
#endif
    }

    /// <summary>
    /// 写进气泡。Windows 上不是简单赋值：换行后的文字有多高，头顶小窗就长多高，
    /// 放不下才裁一段——以前固定 168 DIP + MaxLines=4，长回复被切成四行，读的人以为 TA 只说了这么多。
    /// </summary>
    private void SetReply(string text)
    {
#if WINDOWS
        FitBubble(text);
#else
        // 移动端是全屏问答页，气泡没有窗口高度可调，长回复折中行数给个上限
        ReplyLabel.Text = text.Length > 260 ? text[..260] + "…" : text;
#endif
    }

    /// <summary>答完一句之后她的心情可能变了：桌宠那帧表情跟着换。没挂 AI 桌宠时这一叫什么也不做。</summary>
    private void ReportEmotion() => PetService.SetEmotion(_ch?.State.CurrentEmotion ?? "");

    /// <summary>想一次：结合日记与当前时间，让 AI 嘀咕一句；结果显示在头顶泡泡。</summary>
    private async Task DoThinkAsync()
    {
        if (_thinking) return;
        try
        {
            var charId = _engine.State.CharacterId;
            if (string.IsNullOrEmpty(charId)) return;
            _thinking = true;
            ShowBubble();
            SetReply("……");
            string line;
            try { line = await _chat.Send(charId, await BuildThinkPromptAsync(charId)); }
            catch (Exception ex)
            {
                // 单击就是要看到反应：只回一个「……」，用户只会以为这一下没生效
                App.WriteLog("PetPage.Think -> " + ex);
                SetReply("没接上：" + ReasonOf(ex));
                return;
            }
            line = (line ?? "").Trim();
            if (line.Length > 60) line = line[..60] + "…";
            SetReply("（" + line + "）");
            ReportEmotion();
        }
        finally { _thinking = false; }
    }

    /// <summary>
    /// 思考输入：说明「在当桌宠、没人打扰」，附当前时间与最近一篇日记，
    /// 让 TA 以自言自语的形式嘀咕一句（20 字内），不提问不客套。
    /// </summary>
    private async Task<string> BuildThinkPromptAsync(string charId)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("（系统：你在桌面当桌宠，这会儿没人跟你说话。看一眼下面的信息，自己心里嘀咕一句 20 字以内的悄悄话——像自言自语，不要提问、不要客套、不要动作描写。）\n");
        sb.Append($"现在时间：{DateTime.Now:yyyy-MM-dd HH:mm}\n");
        try
        {
            var diary = await _memory.Diary(charId);
            var latest = diary.OrderByDescending(d => d.Date).FirstOrDefault();
            if (latest is not null && !string.IsNullOrWhiteSpace(latest.Content))
            {
                var c = latest.Content.Replace('\n', ' ');
                if (c.Length > 120) c = c[..120] + "…";
                sb.Append("最近一篇日记：" + c + "\n");
            }
        }
        catch (Exception ex) { App.WriteLog("PetPage.ThinkPrompt -> " + ex.Message); }
        sb.Append("开始嘀咕：");
        return sb.ToString();
    }
    
    private void OnBack(object sender, EventArgs e) => PetService.ShowMainWindowStatic();

    /// <summary>输入条发送：走 ChatEngine（与主界面同一 AI），回复显示在头顶气泡。</summary>
    private async void OnSend(object sender, EventArgs e)
    {
        try
        {
            var text = InputEntry.Text;
            if (string.IsNullOrWhiteSpace(text)) return;
            var charId = _engine.State.CharacterId;
            if (string.IsNullOrEmpty(charId)) return;
            InputEntry.Text = "";
            ShowBubble();
            SetReply("思考中…");
            string reply;
            var ok = true;
            try
            {
                reply = await _chat.Send(charId, text);
            }
            catch (Exception ex)
            {
                App.WriteLog("PetPage.OnSend -> " + ex);
                // 以前这里只在气泡里留一个「……」——看着像 TA 在卖关子，其实请求根本没发出去。
                reply = "没接上 TA 的话：" + ReasonOf(ex);
                ok = false;
            }
            // 自主配置：开了权限她才可以在话里带 pet 指令；没开时指令会被剥掉，不显示也不执行
            reply = PetService.ApplySelfConfig(reply);
            SetReply(reply);
            ReportEmotion();
            if (ok) SpeakReply(charId, reply);
        }
        catch (Exception ex)
        {
            App.WriteLog("PetPage.OnSend -> " + ex);
        }
    }

    /// <summary>读出声：只有「回复读出声」开着才念；【…】这类机器标记不进朗读。</summary>
    private void SpeakReply(string charId, string text)
    {
        try
        {
            var s = _settings.Current;
            if (!s.PetSpeakReplies || string.IsNullOrWhiteSpace(text)) return;
            var clean = MachineMarkers.Replace(text, "").Trim();
            if (clean.Length == 0) return;
            var lang = string.IsNullOrWhiteSpace(s.PetLanguage) ? "zh-CN" : s.PetLanguage;
            _ = _speech.Speak(clean, lang, charId, s.PetVoiceRate, s.PetVoicePitch,
                string.IsNullOrWhiteSpace(s.PetVoiceName) ? null : s.PetVoiceName);
        }
        catch (Exception ex) { App.WriteLog("PetPage.SpeakReply -> " + ex); }
    }

    /// <summary>把接口异常翻译成人话。不把 ex.Message 原样贴出来：那里面可能带请求地址与参数。</summary>
    private static string ReasonOf(Exception ex)
    {
        var m = ex.Message ?? "";
        if (m.Contains("Unauthorized", StringComparison.OrdinalIgnoreCase) || m.Contains("401"))
            return "接口鉴权失败，密钥无效或已到期。\n在「设置 › 模型」里换一个可用密钥就好。";
        if (m.Contains("timeout", StringComparison.OrdinalIgnoreCase) || m.Contains("TaskCanceled", StringComparison.OrdinalIgnoreCase))
            return "网络超时了。\n检查一下网络，或者换个模型再试。";
        return "接口调用失败。\n具体原因写在日志里（warm_startup.log）。";
    }

#if WINDOWS
    // ============ 头顶小窗的显隐 ============

    /// <summary>头顶上下各多少物理像素算「靠近」。太窄就得把鼠标精确压在一条线上，不好用。</summary>
    private const int HoverBandPx = 80;

    private DateTime _bubbleUntil = DateTime.MinValue;
    private bool _surfaceHooked;

    /// <summary>气泡文字宽度 = 小窗 340 减去左右内边距 10+10（DIP，WinUI 量的就是 DIP）。</summary>
    private const double BubbleInnerDip = 320;
    private const double BubblePadDip = 12;
    private const double BubbleMaxDip = 300;

    /// <summary>换行后的文字有多高，小窗就长多高；高过上限才裁一段，而不是让末尾被切在窗口外面。</summary>
    private void FitBubble(string text)
    {
        if (ReplyLabel.Handler?.PlatformView is not Microsoft.UI.Xaml.Controls.TextBlock tb)
        {
            ReplyLabel.Text = text;
            return;
        }
        var h = MeasureBubble(tb, text);
        if (h > BubbleMaxDip && text.Length > 40)
        {
            var cut = Math.Clamp((int)(text.Length * (BubbleMaxDip / h)) - 8, 20, text.Length);
            text = text[..cut].TrimEnd() + "…";
            h = MeasureBubble(tb, text);
        }
        App.WriteLog($"PetPage.FitBubble len={text.Length} measured={h:F0}");
        ReplyLabel.Text = text;
        PetService.SetBubbleHeight(h + BubblePadDip);
    }

    private static double MeasureBubble(Microsoft.UI.Xaml.Controls.TextBlock tb, string text)
    {
        tb.Text = text;
        tb.Measure(new Windows.Foundation.Size(BubbleInnerDip, double.PositiveInfinity));
        return tb.DesiredSize.Height;
    }

    /// <summary>
    /// 轮询鼠标位置来显隐小窗，而不是挂 PointerMoved：
    /// 角色没被悬停时收不到任何指针事件，「靠近才出现」就会退化成「点进去才出现」。
    /// 收放的是整条小窗（窗口级 Show/Hide），因为 XAML 里怎么写都盖不掉那层不透明表面。
    /// </summary>
    private void StartHoverReveal()
    {
        var timer = Dispatcher.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(200);
        var last = false;
        var logged = false;
        timer.Tick += (_, _) =>
        {
            // 正在打字或草稿没发出去：不许收起来，否则焦点和写了一半的话一起没了
            var keepTyping = InputEntry.IsFocused || !string.IsNullOrWhiteSpace(InputEntry.Text);
            var near = PetService.IsCursorNearHead(HoverBandPx);
            var bubble = DateTime.UtcNow < _bubbleUntil;
            // 鼠标压在小窗自己身上：长回复要读，读到哪儿读到哪儿，不能被「离开头顶」这条规则收走
            var onSelf = PetService.IsCursorOnChrome();
            var show = keepTyping || near || bubble || onSelf;
            if (!logged)
            {
                // 只在第一次和每次翻转时写：这条时间隔 200ms，全记会把日志刷爆。
                logged = true;
                App.WriteLog($"PetPage: hover 起转 typing={keepTyping} near={near} bubble={bubble} onSelf={onSelf}");
            }
            else if (show != last)
            {
                App.WriteLog($"PetPage: hover -> {(show ? "显示" : "收起")} (typing={keepTyping} near={near} bubble={bubble} onSelf={onSelf})");
                PetService.LogChromeProbe(show ? "显示" : "收起");
            }
            last = show;
            if (!show && ReplyBubble.IsVisible)
            {
                ReplyBubble.IsVisible = false;
                _bubbleUntil = DateTime.MinValue;
            }
            PetService.SetChrome(show, ReplyBubble.IsVisible);
        };
        timer.Start();
        _hoverTimer = timer;
    }

    /// <summary>把分层窗口的鼠标事件接到这一页：单击嘀咕、双击回家、滚轮缩放、拖走之后小窗跟着走。</summary>
    private void AttachPetSurface()
    {
        if (_surfaceHooked) return;
        _surfaceHooked = true;
        Platforms.Windows.PetLayeredWindow.Pressed += () => _ = QueueThinkAsync();
        Platforms.Windows.PetLayeredWindow.DoublePressed += BackToMain;
        Platforms.Windows.PetLayeredWindow.WheelTurned += OnSurfaceWheel;
        Platforms.Windows.PetLayeredWindow.Moved += PetService.RepositionChrome;
    }

    private void OnSurfaceWheel(int delta)
    {
        Platforms.Windows.PetLayeredWindow.Zoom(delta > 0 ? 1.12 : 1 / 1.12);
        PetService.RepositionChrome();
        PetService.CommitScale();
    }
#endif

    // ============ 语音输入 ============

    /// <summary>麦克风：识别结果填进输入框，不自动发送——说错了还能改。</summary>
    private async void OnMicClicked(object? sender, EventArgs e)
    {
        if (_listening) return;
        if (!_speech.SttEnabled)
        {
            // 识别总开关在设置里，关着的时候点麦克风必须说清楚为什么没反应，
            // 否则在用户看来就是「这按钮坏了」。
            ShowBubble();
            SetReply("语音识别已在设置里关闭");
            App.WriteLog("PetPage: 麦克风按下，但 SttEnabled=false");
            return;
        }
        _listening = true;
        MicButton.Text = "…";
        _speech.OnRecognized += OnSpeechRecognized;
        try
        {
            await _speech.StartListening();
        }
        catch (Exception ex)
        {
            App.WriteLog("PetPage.OnMicClicked -> " + ex);
        }
        // 识别被关掉时 OnRecognized 根本不会触发，这里兜一手，否则按钮永远停在「…」
        FinishSpeech("");
    }

    private void OnSpeechRecognized(string text) => FinishSpeech(text);

    private void FinishSpeech(string text)
    {
        _speech.OnRecognized -= OnSpeechRecognized;
        if (!_listening) return;
        _listening = false;
        var note = _speech.LastRecognitionNote;
        MainThread.BeginInvokeOnMainThread(() =>
        {
            MicButton.Text = "🎤";
            if (!string.IsNullOrWhiteSpace(text))
            {
                InputEntry.Text = text;
                return;
            }
            // 空文本必须说原因：只把按钮复位、输入框一个字没有，在用户看来就是麦克风坏了。
            if (!string.IsNullOrWhiteSpace(note))
            {
                ShowBubble();
                SetReply(note);
            }
        });
    }

    private void ZoomBy(double delta)
    {
        _scale = Math.Clamp(_scale + delta, MinScale, MaxScale);
        SpriteImage.Scale = _scale;
        CommitScale();
    }

    /// <summary>
    /// 把页内缩放写回设置。捏合过程中每帧都调会把 settings.json 刷爆，
    /// 所以只在手势收尾时落一次盘。
    /// </summary>
    private void CommitScale()
    {
        try
        {
            _settings.Apply(_settings.Current with { PetScale = Math.Round(_scale, 3) });
            _ = _settings.Persist();
        }
        catch (Exception ex) { App.WriteLog("PetPage.CommitScale -> " + ex); }
    }

    // ============ #E4 手机端：捏合缩放 + 拖动移动 ============

    private double _pinchStartScale = 1;
    private double _panStartX, _panStartY;

    /// <summary>双指捏合缩放立绘。</summary>
    private void OnPinchUpdated(object? sender, PinchGestureUpdatedEventArgs e)
    {
        try
        {
            if (e.Status == GestureStatus.Started) _pinchStartScale = _scale;
            else if (e.Status == GestureStatus.Running)
            {
                _scale = Math.Clamp(_pinchStartScale * e.Scale, MinScale, MaxScale);
                SpriteImage.Scale = _scale;
            }
            else CommitScale();
        }
        catch (Exception ex) { App.WriteLog("PetPage.OnPinch -> " + ex); }
    }

    /// <summary>拖动立绘（页内移动；松手后记住位置）。</summary>
    private void OnPanUpdated(object? sender, PanUpdatedEventArgs e)
    {
        try
        {
            switch (e.StatusType)
            {
                case GestureStatus.Running:
                    SpriteImage.TranslationX = _panStartX + e.TotalX;
                    SpriteImage.TranslationY = _panStartY + e.TotalY;
                    break;
                case GestureStatus.Completed:
                    _panStartX = SpriteImage.TranslationX;
                    _panStartY = SpriteImage.TranslationY;
                    break;
            }
        }
        catch (Exception ex) { App.WriteLog("PetPage.OnPan -> " + ex); }
    }
}