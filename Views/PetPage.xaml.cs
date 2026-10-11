using System.IO;
using WarmAsBefore.Modules.AiChat;
using WarmAsBefore.Services;

namespace WarmAsBefore.Views;

/// <summary>
/// 桌宠窗口：无边框、置顶、只显示角色立绘。
/// 拖动窗口：Win32 模拟标题栏拖拽（按下移动时调用 PetService.BeginDrag）。
/// 缩放：右上角 +/- 按钮，或鼠标滚轮（WinUI 平台 PointerWheelChanged）。
/// 双击：回到主窗口。底部输入行：直接在桌宠上对话（ChatEngine 单例）。
/// </summary>
public partial class PetPage : ContentPage
{
    private readonly GameEngine _engine;
    private readonly CharacterLibrary _chars;
    private readonly StorageProvider _store;
    private readonly ChatEngine _chat;
    private readonly SettingsManager _settings;
    private readonly Modules.AiChat.MemoryVault _memory;
    private double _scale = 1.0;
    private const double MinScale = 0.5;
    private const double MaxScale = 3.0;
    private bool _thinking;
    private bool _alive = true;
    private int _pendingSingleTap;
    public PetPage(GameEngine engine, CharacterLibrary chars, StorageProvider store, ChatEngine chat,
        SettingsManager settings, Modules.AiChat.MemoryVault memory)
    {
        InitializeComponent();
        _engine = engine;
        _chars = chars;
        _store = store;
        _chat = chat;
        _settings = settings;
        _memory = memory;
        Loaded += OnLoaded;
        Unloaded += (_, _) => _alive = false;
    }

    private async void OnLoaded(object? sender, EventArgs e)
    {
        try
        {
            var charId = _engine.State.CharacterId;
            if (string.IsNullOrEmpty(charId)) return;
            var roster = await _chars.ListAsync();
            var ch = roster.FirstOrDefault(c => c.Profile.Id == charId);
            if (ch is null || ch.SpriteMap.Count == 0) return;

            // 用角色当前状态，而不是 SpriteMap.Keys.First()。
            // 取第一个键会让桌宠显示的立绘和主页面不一致 —— 看起来就像「进桌宠自动换了立绘」。
            var key = $"{ch.State.CurrentOutfit}/{ch.State.CurrentEmotion}";
            if (!ch.SpriteMap.ContainsKey(key))
            {
                var outfitEmotions = ch.SpriteMap.Keys
                    .Where(k => k.StartsWith(ch.State.CurrentOutfit + "/", StringComparison.Ordinal))
                    .ToList();
                key = outfitEmotions.Count > 0 ? outfitEmotions[0] : ch.SpriteMap.Keys.First();
            }

            if (ch.SpriteMap.TryGetValue(key, out var rel))
            {
                var full = Path.Combine(_store.Root, rel);
                if (File.Exists(full))
                {
                    SpriteImage.Source = ImageSource.FromFile(full);
                    NameLabel.Text = ch.Profile.Name;
#if WINDOWS
                    AttachWheelZoom();   // 只存在于 Windows 分支
#endif
                }
            }
        }
        catch (Exception ex)
        {
            App.WriteLog("PetPage.OnLoaded -> " + ex);
        }
        StartThinkLoop();
    }
    private void OnDoubleTapped(object sender, TappedEventArgs e)
    {
        _pendingSingleTap++; // 让待发的单击思考失效（双击优先）
        PetService.ShowMainWindowStatic();
    }

    /// <summary>单击立绘：延迟 350ms 判定——没有第二击时触发一次「思考」嘀咕。</summary>
    private async void OnSingleTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            var id = ++_pendingSingleTap;
            await Task.Delay(350);
            if (!_alive || _pendingSingleTap != id) return;
            _pendingSingleTap = 0;
            await DoThinkAsync();
        }
        catch (Exception ex) { App.WriteLog("PetPage.OnSingleTapped -> " + ex); }
    }

    // ============ #E3 桌宠思考 ============

    /// <summary>思考循环的启动（进入桌宠时调用一次）：mode=off 不启动；random/timer 按间隔触发。</summary>
    private void StartThinkLoop()
    {
        var mode = _settings.Current.PetThinkMode;
        if (mode != "random" && mode != "timer") return;
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
            if (mode == "random" && Random.Shared.Next(100) >= 50) continue;
            await MainThread.InvokeOnMainThreadAsync(DoThinkAsync);
        }
    }

    /// <summary>想一次：结合日记与当前时间，让 AI 嘀咕一句；结果显示在头顶泡泡。</summary>
    private async Task DoThinkAsync()
    {
        if (_thinking) return;
        try
        {
            var charId = _engine.State.CharacterId;
            if (string.IsNullOrEmpty(charId)) return;
            _thinking = true;
            ReplyBubble.IsVisible = true;
            ReplyLabel.Text = "……";
            string line;
            try { line = await _chat.Send(charId, await BuildThinkPromptAsync(charId)); }
            catch (Exception ex) { App.WriteLog("PetPage.Think -> " + ex); line = "……"; }
            line = (line ?? "").Trim();
            if (line.Length > 60) line = line[..60] + "…";
            ReplyLabel.Text = "（" + line + "）";
        }
        finally { _thinking = false; }
    }

    /// <summary>
    /// 思考输入：说明「在当桌宠、没人打扰」，附当前时间与最近一篇日记，
    /// 让她以自言自语的形式嘀咕一句（20 字内），不提问不客套。
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

    /// <summary>底部输入行发送：走 ChatEngine（与主界面同一 AI），回复显示在顶部气泡。</summary>
    private async void OnSend(object sender, EventArgs e)
    {
        try
        {
            var text = InputEntry.Text;
            if (string.IsNullOrWhiteSpace(text)) return;
            var charId = _engine.State.CharacterId;
            if (string.IsNullOrEmpty(charId)) return;
            InputEntry.Text = "";
            ReplyBubble.IsVisible = true;
            ReplyLabel.Text = "思考中…";
            string reply;
            try
            {
                reply = await _chat.Send(charId, text);
            }
            catch (Exception ex)
            {
                App.WriteLog("PetPage.OnSend -> " + ex);
                reply = "……";
            }
            ReplyLabel.Text = reply;
        }
        catch (Exception ex)
        {
            App.WriteLog("PetPage.OnSend -> " + ex);
        }
    }

#if WINDOWS
    private void OnZoomIn(object sender, EventArgs e) => ZoomBy(+0.1);

    private void OnZoomOut(object sender, EventArgs e) => ZoomBy(-0.1);
#endif

    private void ZoomBy(double delta)
    {
        _scale = Math.Clamp(_scale + delta, MinScale, MaxScale);
        SpriteImage.Scale = _scale;
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

#if WINDOWS
    /// <summary>在 WinUI 平台层挂接滚轮事件（MAUI 手势不提供滚轮回调）。</summary>
    private void AttachWheelZoom()
    {
        try
        {
            var handler = SpriteImage.Handler;
            var platformView = handler?.PlatformView;
            if (platformView is not Microsoft.UI.Xaml.Controls.Image img) return;
            if (_wheelHooked) return;
            _wheelHooked = true;
            img.PointerWheelChanged += (_, ev) =>
            {
                var delta = ev.GetCurrentPoint(null).Properties.MouseWheelDelta;
                if (delta != 0) ZoomBy(delta > 0 ? 0.1 : -0.1);
            };
        }
        catch (Exception ex)
        {
            App.WriteLog("PetPage.AttachWheelZoom -> " + ex);
        }
    }

    private bool _wheelHooked;
#endif
}