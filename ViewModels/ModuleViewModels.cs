using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WarmAsBefore.Drawables;
using WarmAsBefore.Models;
using WarmAsBefore.Modules.AiChat;
using WarmAsBefore.Modules.Automation;
using WarmAsBefore.Modules.SaveSystem;
using WarmAsBefore.Services;

namespace WarmAsBefore.ViewModels;

public sealed partial class PhoneViewModel : ObservableObject, IQueryAttributable
{
    private readonly SettingsManager _settings;
    private readonly Modules.RealChat.OfficialChatBridge _bridge;
    private readonly Modules.RealWorld.TimeProvider _time;
    private readonly GameEngine _engine;

    [ObservableProperty] private string _timeLabel = "";
    [ObservableProperty] private string _weatherText = "晴天";
    [ObservableProperty] private string _statusTime = "";
    [ObservableProperty] private string _weChatLabel = "微信";
    [ObservableProperty] private string _statusLine = "温暖如初 · 手机";
    /// <summary>战斗系统在设置里开着时，手机桌面才给「战斗」入口——入口不该比功能多。</summary>
    [ObservableProperty] private bool _showBattle;
    /// <summary>这扇手机是不是自己一扇独立窗（独立窗的「返回」= 关窗，不是翻主窗口的栈）。</summary>
    [ObservableProperty] private bool _inWindow;

    public PhoneViewModel(SettingsManager settings,
        Modules.RealChat.OfficialChatBridge bridge, Modules.RealWorld.TimeProvider time, GameEngine engine)
    {
        _settings = settings;
        _bridge = bridge;
        _time = time;
        _engine = engine;
        RefreshStatus();
        _bridge.StatusChanged += _ => MainThread.BeginInvokeOnMainThread(RefreshStatus);
        _settings.Applied += RefreshStatus;
    }

    public void OnAppearing()
    {
        var info = _time.Now();
        TimeLabel = info.Now.ToString("HH:mm");
        StatusTime = $"{info.Now:MM-dd} {info.Period switch { "morning" => "上午", "noon" => "中午", "afternoon" => "下午", "evening" => "傍晚", _ => "晚上" }}";
        RefreshStatus();
    }

    /// <summary>微信图标下的小字：真微信未开启时显示「本地」。</summary>
    private void RefreshStatus()
    {
        var real = _settings.Current.WechatEnabled
            && _bridge.Channels.Length > 1 && _bridge.Channels[1].IsRunning;
        WeChatLabel = real ? "微信 · 真" : "微信 · 本地";
        var s = _bridge.Status;
        StatusLine = s is null or "未开启" ? "温暖如初 · 手机" : $"温暖如初 · {s}";
        ShowBattle = _settings.Current.BattleEnabled;
    }

    [RelayCommand]
    private async Task OpenChat() => await SafeNav("chat");

    /// <summary>QQ 壳：与微信壳同构的青蓝换皮会话（多论坛、无礼物/支付）。</summary>
    [RelayCommand]
    private async Task OpenQq() => await SafeNav("qqchat");

    [RelayCommand]
    private async Task OpenMap()
    {
        if (!_engine.MapUnlocked)
        {
            await Shell.Current.DisplayAlert("普通酒馆",
                "这一局没有绑定世界树，所以没有地图。\n\n想解锁：游戏菜单 → 本局设定 → 选一份世界树。",
                "知道了");
            return;
        }
        await SafeNav("map");
    }

    [RelayCommand]
    private async Task OpenGallery() => await SafeNav("gallery");

    [RelayCommand]
    private async Task OpenLibrary() => await SafeNav("roster");

    [RelayCommand]
    private async Task OpenMaterials() => await SafeNav("materials");

    /// <summary>点名：人类主动点名角色，指定初始位置，强制进入对话剧情。</summary>
    [RelayCommand]
    private async Task OpenForceCast() => await SafeNav("forcecast");

    /// <summary>场景工坊：条目 / 模式（点击式切换，无需拖拽）。</summary>
    [RelayCommand]
    private async Task OpenSceneWorkshop() => await SafeNav("scenelibrary");

    [RelayCommand]
    private async Task OpenShop() => await SafeNav("shop");

    /// <summary>世界书：从小说/设定里生成世界观条目（桌面顶栏之外，手机也要能进）。</summary>
    [RelayCommand]
    private async Task OpenWorldbook() => await SafeNav("worldbook");

    /// <summary>换装：给当前角色挑一套立绘/服饰。</summary>
    [RelayCommand]
    private async Task OpenOutfit() => await SafeNav("outfit");

    [RelayCommand]
    private async Task OpenBattle() => await SafeNav("battle");

    [RelayCommand]
    private async Task OpenGame(string? kind)
    {
        var k = kind ?? "gobang";
        await SafeNav($"game?kind={k}");
    }

    [RelayCommand]
    private async Task Back()
    {
        if (InWindow) { CloseOwnWindow(); return; }
        await SafeNav("..");
    }

    /// <summary>
    /// 导航统一兜底：async void 链上任何异常都会崩 WinUI，此处拦截记录。
    /// 独立窗里点图标仍然导航主窗口（只有一整套 Shell 路由），所以先把主窗口抬到前面。
    /// </summary>
    private async Task SafeNav(string route)
    {
        try
        {
            if (InWindow) WindowTopmost.BringToFront();
            await Shell.Current.GoToAsync(route);
        }
        catch (Exception ex)
        {
            App.WriteLog($"PhoneViewModel.SafeNav({route}) -> {ex.Message}");
        }
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("inWindow", out var v) && v?.ToString() == "1") InWindow = true;
    }

    private void CloseOwnWindow()
    {
        var app = Application.Current;
        var win = app?.Windows.FirstOrDefault(w => ReferenceEquals(w.Page?.BindingContext, this));
        if (win is not null) app!.CloseWindow(win);
    }
}

public sealed partial class WeChatViewModel : ObservableObject
{
    private readonly GameEngine _engine;
    private readonly ChatEngine _chat;
    private readonly SpeechService _speech;
    private readonly Modules.RealChat.OfficialChatBridge _bridge;
    private readonly CharacterLibrary _library;
    private readonly StorageProvider _store;
    private readonly SettingsManager _settings;
    private readonly Modules.Market.GiftPanelService _gifts;
    private readonly Services.WeChatService _wechat;
    private readonly Services.StickerLibrary _stickerLib;
    private readonly Services.VisionService _vision;

    [ObservableProperty] private string _inputText = "";
    [ObservableProperty] private bool _isListening;
    [ObservableProperty] private bool _isTyping;
    [ObservableProperty] private int _typingDots;
    [ObservableProperty] private string _typingLabel = "";
    [ObservableProperty] private string _partnerName = "TA";
    [ObservableProperty] private ImageSource? _partnerAvatar;
    [ObservableProperty] private bool _hasPartnerAvatar;
    [ObservableProperty] private string _modeLabel = "本地微信";
    [ObservableProperty] private bool _isStickerPanelVisible;
    // ============ 联系人列表（角色"假微信"：谁加过好友） ============
    [ObservableProperty] private bool _isContactListVisible = true;
    [ObservableProperty] private bool _hasContacts;
    /// <summary>微信联系人列表（来自 WeChatService：已经加过微信的常驻角色）。</summary>
    public ObservableCollection<WeChatContactItem> ContactList { get; } = new();
    /// <summary>当前聊天对象（默认跟随主游戏角色；点联系人可切换，仅影响微信页）。</summary>
    private string _chatTargetId = "";
    // ============ 送礼 / 使用面板（微信聊天时显示） ============
    [ObservableProperty] private bool _isGiftPanelVisible;
    private CancellationTokenSource? _typingCts;
    public ObservableCollection<ChatItem> Messages { get; } = new();
    /// <summary>用户导入的表情库（全路径）：图片/动图/视频。</summary>
    public ObservableCollection<string> Stickers { get; } = new();
    public bool HasStickers => Stickers.Count > 0;
    private string StickerDir => Path.Combine(_store.Root, "Stickers");

    // ============ 壳形态（微信 / QQ 共用一个会话模型，只在品牌与功能开关上不同） ============
    /// <summary>是否 QQ 壳：QQ 壳去掉礼品/支付面板（不进 GiftPanel），加上「论坛」入口。默认微信壳。</summary>
    public bool IsQq { get; private set; }
    /// <summary>顶栏品牌字（「微信」/「QQ」）。</summary>
    public string ShellTitle { get; private set; } = "微信";

    /// <summary>由页面在构造后调用：QQPage 设 "qq"，WeChatPage 保持默认。</summary>
    public void SetShell(string shell)
    {
        IsQq = shell == "qq";
        ShellTitle = IsQq ? "QQ" : "微信";
    }

    public WeChatViewModel(GameEngine engine, ChatEngine chat, SpeechService speech,
        Modules.RealChat.OfficialChatBridge bridge, CharacterLibrary library,
        StorageProvider store, SettingsManager settings, Modules.Market.GiftPanelService gifts,
        Services.WeChatService wechat, Services.StickerLibrary stickerLib, Services.VisionService vision)
    {
        _engine = engine;
        _chat = chat;
        _speech = speech;
        _bridge = bridge;
        _library = library;
        _store = store;
        _settings = settings;
        _gifts = gifts;
        _wechat = wechat;
        _stickerLib = stickerLib;
        _vision = vision;
        _bridge.Incoming += OnRealIncoming;
        _bridge.Replied += OnRealReplied;
        _bridge.StatusChanged += _ => MainThread.BeginInvokeOnMainThread(RefreshMode);
    }

    /// <summary>进入微信页时刷新：对方头像+名字 + 真微信/假微信状态。</summary>
    public async Task LoadAsync()
    {
        await LoadContactsAsync();
        var charId = EffectiveChatId();
        // 若还没定聊天对象，取第一个联系人
        if (string.IsNullOrEmpty(charId) && ContactList.Count > 0)
        {
            _chatTargetId = ContactList[0].CharacterId;
            charId = _chatTargetId;
        }
        if (!string.IsNullOrEmpty(charId))
        {
            var list = await _library.ListAsync();
            var ch = list.FirstOrDefault(c => c.Profile.Id == charId);
            if (ch is not null)
            {
                PartnerName = ch.Profile.Name;
                ImageSource? avatar = null;
                if (!string.IsNullOrEmpty(ch.Avatar))
                {
                    var full = Path.Combine(_store.Root, ch.Avatar);
                    if (File.Exists(full)) avatar = ImageSource.FromFile(full);
                }
                PartnerAvatar = avatar;
                HasPartnerAvatar = avatar is not null;
                _chat.ConfigureCharacter(ch.Profile);
                _chat.SetRoster(_library.RosterContext(ch.Profile.Id, _engine.State.AllowedCharacters));
                await _wechat.MarkReadAsync(charId);
            }
        }
        RefreshMode();
        LoadStickers();
    }

    /// <summary>加载用户导入的表情库（AppData/WarmAsBefore/Stickers，不限文件类型）。</summary>
    private void LoadStickers()
    {
        Stickers.Clear();
        try
        {
            var dir = StickerDir;
            if (Directory.Exists(dir))
            {
                foreach (var f in Directory.EnumerateFiles(dir))
                    Stickers.Add(f);
            }
            else Directory.CreateDirectory(dir);
        }
        catch (Exception ex)
        {
            App.WriteLog("WeChatViewModel.LoadStickers -> " + ex);
        }
        OnPropertyChanged(nameof(HasStickers));
    }

    /// <summary>真微信（公众号接入）未开启时，用本地假微信：界面不变，AI 直接陪聊。</summary>
    private void RefreshMode()
    {
        if (IsQq)
        {
            var qqReal = _settings.Current.QqBotEnabled
                && _bridge.Channels.Length > 0 && _bridge.Channels[0].IsRunning;
            ModeLabel = qqReal ? "QQ 官方机器人 · 已接入" : "本地 QQ · TA 还在";
            return;
        }
        var real = _settings.Current.WechatEnabled
            && _bridge.Channels.Length > 1 && _bridge.Channels[1].IsRunning;
        ModeLabel = real ? "真微信 · 已接入" : "本地微信 · TA 还在";
    }

    // ============ 视频号 / 论坛（社交壳外链与 QQ 特性面） ============

    /// <summary>
    /// 视频号：直接跳「设置 → 外部接入 → 视频号链接」里填的 URL（应用内浏览器打开；
    /// 没填则提示去填）。纯数据跳转，不走 AI。
    /// </summary>
    [RelayCommand]
    private async Task OpenChannels()
    {
        var url = (_settings.Current.ChannelsUrl ?? "").Trim();
        if (string.IsNullOrWhiteSpace(url))
        {
            try
            {
                await Shell.Current.DisplayAlert("视频号",
                    "还没填视频号链接。到「设置 → 外部接入 → 视频号链接」里填一个 https:// 地址再试。", "好");
            }
            catch (Exception ex) { App.WriteLog("WeChatViewModel.OpenChannels(alert) -> " + ex.Message); }
            return;
        }
        try { await Browser.OpenAsync(url); }
        catch (Exception ex) { App.WriteLog("WeChatViewModel.OpenChannels -> " + ex.Message); }
    }

    /// <summary>QQ 壳的「论坛」入口（微信壳没有）：进 QQ 论坛页（纯本地数据，不走 AI）。</summary>
    [RelayCommand]
    private async Task OpenForum()
    {
        try { await Shell.Current.GoToAsync("qqforum"); }
        catch (Exception ex) { App.WriteLog("WeChatViewModel.OpenForum -> " + ex.Message); }
    }

    private void OnRealIncoming(Modules.RealChat.RealChatMessage msg)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            var item = new ChatItem { Text = msg.Content, DisplayText = msg.Content, IsUser = true };
            if (!Messages.Any(m => m.Text == item.Text && m.IsUser && m.DisplayText == item.Text))
                Messages.Add(item);
        });
    }

    private void OnRealReplied(Modules.RealChat.RealChatMessage msg, string reply)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            // #12：剥离 [表情:名字] 标记再展示（表情以图片消息形式发出）
            var replyShown = StripStickerMarkers(reply, out var stickerNames);
            var aiMsg = new ChatItem { Text = replyShown, DisplayText = "", IsUser = false };
            Messages.Add(aiMsg);
            _ = Typewrite(aiMsg, replyShown);
            MaybeAiSticker(replyShown, stickerNames);
        });
    }

    [RelayCommand]
    private async Task Send()
    {
        if (string.IsNullOrWhiteSpace(InputText)) return;
        var msg = InputText.Trim();
        InputText = "";
        var charId = EffectiveChatId();
        if (string.IsNullOrEmpty(charId)) return;

        Messages.Add(new ChatItem { Text = msg, DisplayText = msg, IsUser = true });
        IsTyping = true;
        _typingCts = new CancellationTokenSource();
        _ = AnimateTypingDots(_typingCts.Token);
        // #12：把最新表情包清单同步给 AI（同一套库，主对话/假微信都能用表情）
        await _stickerLib.EnsureLoadedAsync();
        _chat.SetStickerContext(_stickerLib.BuildContextForAi());
        var reply = await _chat.Send(charId, msg);
        _typingCts.Cancel();
        IsTyping = false;
        // #12：剥离 [表情:名字] 标记再展示（表情以图片消息形式发出）
        var replyShown = StripStickerMarkers(reply, out var stickerNames);
        var aiMsg = new ChatItem { Text = replyShown, DisplayText = "", IsUser = false };
        Messages.Add(aiMsg);
        _ = Typewrite(aiMsg, replyShown);
        MaybeAiSticker(replyShown, stickerNames);
        await _wechat.TouchLastMessageAsync(charId, msg.Length > 20 ? msg[..20] : msg);
    }

    // ============ 送礼 / 使用面板（微信聊天） ============

    [RelayCommand]
    private void ToggleGiftPanel()
    {
        if (IsQq) return;   // QQ 壳砍掉了支付/送礼：不进 GiftPanel，UI 上也没有入口
        IsGiftPanelVisible = !IsGiftPanelVisible;
        if (IsGiftPanelVisible) OnPropertyChanged(nameof(GiftItems));
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

    [RelayCommand]
    private async Task GiftItem(Models.ShopItem item)
    {
        if (item is null) return;
        IsGiftPanelVisible = false;
        Messages.Add(new ChatItem { Text = $"🎁 送给你：{item.Emoji} {item.Name}", DisplayText = $"🎁 送给你：{item.Emoji} {item.Name}", IsUser = true });
        var reply = await _gifts.GiftAsync(item);
        var aiMsg = new ChatItem { Text = reply, DisplayText = "", IsUser = false };
        Messages.Add(aiMsg);
        _ = Typewrite(aiMsg, reply);
    }

    [RelayCommand]
    private async Task UseItem(Models.ShopItem item)
    {
        if (item is null) return;
        IsGiftPanelVisible = false;
        Messages.Add(new ChatItem { Text = $"✨ 我用了：{item.Emoji} {item.Name}", DisplayText = $"✨ 我用了：{item.Emoji} {item.Name}", IsUser = true });
        var reply = await _gifts.UseAsync(item);
        var aiMsg = new ChatItem { Text = reply, DisplayText = "", IsUser = false };
        Messages.Add(aiMsg);
        _ = Typewrite(aiMsg, reply);
    }

    // ============ 表情包 & 文件发送 ============

    [RelayCommand]
    private void ToggleStickerPanel() => IsStickerPanelVisible = !IsStickerPanelVisible;    /// <summary>点表情库里的表情：按类型发图片消息或文件卡片，然后让 AI 也回应一句。</summary>
    [RelayCommand]
    private void SendSticker(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
        AddMediaMessage(path, isUser: true);
        IsStickerPanelVisible = false;
        _ = ReplyToStickerAsync();
    }

    /// <summary>导入表情：选任何文件 → 复制进表情库 → 立即出现在面板。
    /// 图片类且配了识图模型时顺手 OCR 一句描述，登记进统一表情库（AI 按描述选表情）。</summary>
    [RelayCommand]
    private async Task ImportSticker()
    {
        var path = await PickFileAsync();
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            var dir = StickerDir;
            Directory.CreateDirectory(dir);
            var originalName = Path.GetFileNameWithoutExtension(path);
            var dest = Path.Combine(dir, $"{DateTime.Now:yyyyMMddHHmmss}_{Path.GetFileName(path)}");
            File.Copy(path, dest, overwrite: true);
            Stickers.Add(dest);
            OnPropertyChanged(nameof(HasStickers));
            await RegisterStickerIntoLibraryAsync(dest, originalName);
        }
        catch (Exception ex)
        {
            App.WriteLog("WeChatViewModel.ImportSticker -> " + ex);
        }
    }

    /// <summary>把壳里导入的表情登记进统一表情库（不复制文件）；图片且识图模型可用时先 OCR 描述。</summary>
    private async Task RegisterStickerIntoLibraryAsync(string path, string name)
    {
        try
        {
            var desc = "";
            if (_vision.IsConfigured && IsStickerImagePath(path))
            {
                desc = (await _vision.DescribeImageAsync(path, StickerOcrPrompt)) ?? "";
            }
            await _stickerLib.RegisterAsync(path, name, desc);
        }
        catch (Exception ex)
        {
            App.WriteLog("WeChatViewModel.RegisterSticker -> " + ex.Message);
        }
    }

    private static bool IsStickerImagePath(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".webp" or ".bmp";

    /// <summary>识图提示词（与设置页「表情自动描述」同款：一句中文，30 字内）。</summary>
    private const string StickerOcrPrompt =
        "这是一张聊天用的表情包。请用一句中文描述它的内容、情绪和适合的使用场景（30 字以内，直接给描述，不要客套）。";

    /// <summary>
    /// 收到的表情（AI 发来的）若还没描述且识图模型可用，补一次 OCR 存进表情库 ——
    /// 让 AI 之后也「认得」这张图。未配置模型时完全 no-op（纯数据面，不强制走 AI）。
    /// </summary>
    private void MaybeOcrStickerAsync(string name)
    {
        var item = _stickerLib.Find(name);
        if (item is null || !string.IsNullOrWhiteSpace(item.Description)) return;
        if (!_vision.IsConfigured || !IsStickerImagePath(item.Path)) return;
        _ = OcrStickerBackgroundAsync(item, name);
    }

    private async Task OcrStickerBackgroundAsync(StickerItem item, string name)
    {
        try
        {
            var desc = await _vision.DescribeImageAsync(item.Path, StickerOcrPrompt);
            if (string.IsNullOrWhiteSpace(desc)) return;
            var fresh = _stickerLib.Find(name);
            if (fresh is null || !string.IsNullOrWhiteSpace(fresh.Description)) return;
            await _stickerLib.UpdateDescriptionAsync(fresh.Id, desc);
        }
        catch (Exception ex)
        {
            App.WriteLog("WeChatViewModel.OcrSticker -> " + ex.Message);
        }
    }

    /// <summary>发文件：图片/动图直接作为图片消息；视频内联播放；其他文件作为可点击打开的文件卡片。</summary>
    [RelayCommand]
    private async Task PickFile()
    {
        var path = await PickFileAsync();
        if (string.IsNullOrEmpty(path)) return;
        AddMediaMessage(path, isUser: true);
    }

    /// <summary>点文件卡片：用系统默认程序打开（视频会调起播放器）。</summary>
    [RelayCommand]
    private async Task OpenFile(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
        try
        {
#if WINDOWS
            var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(path);
            if (file is not null) await Windows.System.Launcher.LaunchFileAsync(file);
#endif
        }
        catch (Exception ex)
        {
            App.WriteLog("WeChatViewModel.OpenFile -> " + ex);
        }
    }

    /// <summary>用户发完表情后，让 AI 也回应一句（表情当输入喂给它）。</summary>
    private async Task ReplyToStickerAsync()
    {
        var charId = _engine.State.CharacterId;
        if (string.IsNullOrEmpty(charId)) return;
        IsTyping = true;
        _typingCts = new CancellationTokenSource();
        _ = AnimateTypingDots(_typingCts.Token);
        string reply;
        try
        {
            reply = await _chat.Send(charId, "[对方发来一个表情包]");
        }
        catch (Exception ex)
        {
            App.WriteLog("WeChatViewModel.ReplyToSticker -> " + ex);
            reply = "……";
        }
        _typingCts.Cancel();
        IsTyping = false;
        // #12：剥离 [表情:名字] 标记再展示（表情以图片消息形式发出）
        var replyShown = StripStickerMarkers(reply, out var stickerNames);
        var aiMsg = new ChatItem { Text = replyShown, DisplayText = "", IsUser = false };
        Messages.Add(aiMsg);
        _ = Typewrite(aiMsg, replyShown);
        MaybeAiSticker(replyShown, stickerNames);
    }

    /// <summary>按扩展名分流发送：图片→ImageSource；视频→内联播放；其他→文件卡片。</summary>
    private void AddMediaMessage(string path, bool isUser)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        var isImage = ext is ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".bmp";
        var isVideo = ext is ".mp4" or ".mov" or ".mkv" or ".webm" or ".avi" or ".wmv" or ".flv";
        var fileName = isImage || isVideo ? "" : $"📄 {Path.GetFileName(path)}";
        MainThread.BeginInvokeOnMainThread(() =>
            Messages.Add(new ChatItem
            {
                Text = path,
                DisplayText = "",
                IsUser = isUser,
                ImageSource = isImage ? ImageSource.FromFile(path) : null,
                VideoPath = isVideo ? path : "",
                FileName = fileName
            }));
    }

    /// <summary>
    /// #12 升级：AI 回复里带 [表情:名字] 标记时，按名字找到图片发出（统一表情库优先，旧目录兜底）；
    /// 一个都没命中时，保留原来的「小概率随机自发一张」行为，让聊天有生气。
    /// </summary>
    private void MaybeAiSticker(string reply, List<string>? stickerNames = null)
    {
        var hit = false;
        if (stickerNames is { Count: > 0 })
        {
            foreach (var nm in stickerNames)
            {
                var path = FindStickerByName(nm);
                if (path is null) continue;
                AddMediaMessage(path, isUser: false);
                hit = true;
                MaybeOcrStickerAsync(nm);   // 识图模型可用时补 OCR，让 AI 之后也「认得」这张表情
            }
        }
        if (hit) return;
        if (string.IsNullOrEmpty(reply) || Stickers.Count == 0) return;
        if (Random.Shared.Next(100) < 35)
        {
            var pick = Stickers[Random.Shared.Next(Stickers.Count)];
            AddMediaMessage(pick, isUser: false);
        }
    }

    /// <summary>按名字找表情图片：统一表情库优先，旧 Stickers 目录兜底；找不到返回 null。</summary>
    private string? FindStickerByName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        try
        {
            var lib = _stickerLib.Find(name);
            if (lib is not null && File.Exists(lib.Path)) return lib.Path;
        }
        catch { }
        try
        {
            var noExt = Path.GetFileNameWithoutExtension(name);
            foreach (var f in Stickers)
            {
                var fn = Path.GetFileNameWithoutExtension(f);
                if (string.Equals(fn, name, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(fn, noExt, StringComparison.OrdinalIgnoreCase))
                    return f;
            }
        }
        catch { }
        return null;
    }

    /// <summary>把 AI 正文里的 [表情:名字] 标记剥离，返回干净文本；标记清单经 out 带给调用方。</summary>
    private static string StripStickerMarkers(string reply, out List<string> stickerNames)
    {
        var (clean, names) = Services.StickerLibrary.SplitMarkers(reply);
        stickerNames = names;
        return clean;
    }

#if WINDOWS
    private async Task<string?> PickFileAsync()
    {
        try
        {
            var win = Application.Current?.Windows.FirstOrDefault(w => w.Handler is not null && w is not WarmAsBefore.Views.ToolWindow);
            if (win?.Handler?.PlatformView is not Microsoft.UI.Xaml.Window xw) return null;
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            picker.FileTypeFilter.Add("*");   // 什么文件都可以选
            WinRT.Interop.InitializeWithWindow.Initialize(picker,
                WinRT.Interop.WindowNative.GetWindowHandle(xw));
            var file = await picker.PickSingleFileAsync();
            return file?.Path;
        }
        catch (Exception ex)
        {
            App.WriteLog("WeChatViewModel.PickFileAsync -> " + ex);
            return null;
        }
    }
#else
    private Task<string?> PickFileAsync()
    {
        // 非 Windows 平台暂不支持系统文件选择器
        return Task.FromResult<string?>(null);
    }
#endif

    private async Task Typewrite(ChatItem msg, string full, int delayMs = 30)
    {
        // 性能优化：跳过逐字显示，直接显示完整文本
        // 如果确实需要逐字效果，可以使用更快的间隔
        msg.DisplayText = full;
        await Task.Delay(Math.Min(delayMs * 2, 100)); // 最短延迟100ms，避免过快
    }

    private async Task AnimateTypingDots(CancellationToken ct)
    {
        var dots = new[] { "", ".", "..", "..." };
        int idx = 0;
        while (!ct.IsCancellationRequested)
        {
            TypingLabel = $"对方输入中{dots[idx % dots.Length]}";
            idx++;
            try { await Task.Delay(400, ct); } catch { break; }
        }
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

    // ============ 联系人列表（角色"假微信"） ============

    /// <summary>当前聊天对象的 Id：优先微信页内选的，其次主游戏当前角色。</summary>
    private string EffectiveChatId() =>
        string.IsNullOrEmpty(_chatTargetId) ? _engine.State.CharacterId : _chatTargetId;

    /// <summary>从 WeChatService 加载联系人（已加微信的常驻角色）。</summary>
    public async Task LoadContactsAsync()
    {
        try
        {
            var contacts = await _wechat.ListAsync();
            var chars = await _library.ListAsync();
            var items = new List<WeChatContactItem>();
            foreach (var c in contacts)
            {
                var ch = chars.FirstOrDefault(x => x.Profile.Id == c.CharacterId);
                if (ch is null) continue;
                var avatarPath = "";
                if (!string.IsNullOrEmpty(ch.Avatar))
                {
                    var full = Path.Combine(_store.Root, ch.Avatar);
                    if (File.Exists(full)) avatarPath = full;
                }
                items.Add(new WeChatContactItem
                {
                    CharacterId = c.CharacterId,
                    Name = ch.Profile.Name,
                    Avatar = avatarPath,
                    LastMessage = c.LastMessage,
                    Unread = c.Unread
                });
            }
            MainThread.BeginInvokeOnMainThread(() =>
            {
                ContactList.Clear();
                foreach (var it in items) ContactList.Add(it);
                HasContacts = ContactList.Count > 0;
            });
        }
        catch (Exception ex)
        {
            App.WriteLog("WeChatViewModel.LoadContactsAsync -> " + ex);
        }
    }

    /// <summary>点联系人：切换聊天对象并进入聊天视图。</summary>
    [RelayCommand]
    private async Task OpenContact(WeChatContactItem item)
    {
        if (item is null) return;
        _chatTargetId = item.CharacterId;
        IsContactListVisible = false;
        Messages.Clear();
        await LoadAsync();
    }

    /// <summary>在聊天视图与联系人列表之间切换。</summary>
    [RelayCommand]
    private async Task ToggleContactList()
    {
        IsContactListVisible = !IsContactListVisible;
        if (IsContactListVisible) await LoadContactsAsync();
    }

    [RelayCommand]
    private async Task Back()
    {
        // 聊天视图下按返回：先回联系人列表；已在列表才退出页面
        if (!IsContactListVisible && HasContacts)
        {
            IsContactListVisible = true;
            await LoadContactsAsync();
            return;
        }
        await Shell.Current.GoToAsync("..");
    }
}

/// <summary>微信联系人列表项（显示用）。</summary>
public sealed class WeChatContactItem
{
    public string CharacterId { get; init; } = "";
    public string Name { get; init; } = "";
    public string Avatar { get; init; } = "";
    public string LastMessage { get; init; } = "";
    public bool Unread { get; init; }
    public bool HasAvatar => !string.IsNullOrEmpty(Avatar);
    public string Initial => string.IsNullOrEmpty(Name) ? "?" : Name[..1];
}

public sealed partial class GalleryViewModel : ObservableObject
{
    private readonly Modules.AiChat.MemoryVault _memory;
    private readonly GameEngine _engine;
    private readonly Modules.Automation.DailyDiaryWriter _diary;
    private readonly Modules.Cg.CgStore _cg;
    private readonly Modules.Cg.CgViewPayload _cgView;

    private readonly Services.MemoryOverviewService _overview;
    private readonly Services.HumanMemoryService _human;
    private readonly Services.RelationNetworkService _network;
    private readonly Services.MemoryTagService _tags;
    private readonly Services.PageWindowService _windows;
    [ObservableProperty] private bool _isMemoirSelected = true;
    [ObservableProperty] private bool _isDiarySelected;
    [ObservableProperty] private bool _isCgSelected;
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private ObservableCollection<AffectionMemoryItem> _affectionItems = new();
    [ObservableProperty] private ObservableCollection<DialogueMemoryItem> _memoryItems = new();
    [ObservableProperty] private ObservableCollection<DiaryDayItem> _diaryItems = new();
    [ObservableProperty] private ObservableCollection<CgCollectionItem> _cgItems = new();
    [ObservableProperty] private bool _hasAffection;
    [ObservableProperty] private bool _hasMemory;
    [ObservableProperty] private bool _hasDiary;
    [ObservableProperty] private bool _hasCg;

    public GalleryViewModel(Modules.AiChat.MemoryVault memory, GameEngine engine,
        Modules.Automation.DailyDiaryWriter diary, Modules.Cg.CgStore cg,
        Modules.Cg.CgViewPayload cgView, Services.MemoryOverviewService overview,
        Services.HumanMemoryService human, Services.RelationNetworkService semantic,
        Services.MemoryTagService tags, Services.PageWindowService windows)
    {
        _memory = memory;
        _engine = engine;
        _diary = diary;
        _cg = cg;
        _cgView = cgView;
        _overview = overview;
        _human = human;
        _network = semantic;
        _tags = tags;
        _windows = windows;
    }

    private string CharacterId
    {
        get
        {
            var id = _engine.State.CharacterId;
            return string.IsNullOrEmpty(id) ? "小雨" : id;
        }
    }

    [RelayCommand]
    private async Task Refresh()
    {
        // 日记是每日自动生成的核心机制：打开回忆录时确保当天总结已存档
        await _diary.EnsureTodayAsync();
        await ReloadDiaryAsync();
        await ReloadMemoirAsync();
        await ReloadCgAsync();
        await ReloadLayersAsync();
        await ReloadRememberedAsync();
    }

    // ===== 记忆分层总览（B）：五层各自的规模一眼可见 =====

    [ObservableProperty] private ObservableCollection<MemoryLayerItem> _layerStats = new();
    [ObservableProperty] private bool _hasLayerStats;

    // ===== 「记得的事」（C）：人类记忆层 + 标签 + 关系网 =====

    [ObservableProperty] private bool _isRememberedSelected;
    [ObservableProperty] private ObservableCollection<HumanMemoryItem> _humanMemoryItems = new();
    [ObservableProperty] private string _tagSummary = "";
    [ObservableProperty] private bool _hasTags;
    [ObservableProperty] private string _semanticSummary = "";
    [ObservableProperty] private bool _hasSemantic;

    /// <summary>打开记忆管理页（关系网 / 标签 / 记得的事 / 副设定）。</summary>
    [RelayCommand]
    private async Task OpenMemoryManage() =>
        await Shell.Current.GoToAsync($"memoryManage?charId={Uri.EscapeDataString(CharacterId)}");

    /// <summary>关系网、搜事件各自另开一扇窗：这边翻记录，那边同时看着网。</summary>
    [RelayCommand]
    private async Task OpenGraph() =>
        await _windows.OpenOrNavigateAsync(PageWindowKeys.Graph, "关系网",
            typeof(Views.MemoryGraphPage), PageWindowKeys.Graph,
            new Dictionary<string, object> { ["charId"] = CharacterId });

    [RelayCommand]
    private async Task OpenSearch() =>
        await _windows.OpenOrNavigateAsync(PageWindowKeys.Search, "搜事件",
            typeof(Views.MemorySearchPage), PageWindowKeys.Search,
            new Dictionary<string, object> { ["charId"] = CharacterId });

    private async Task ReloadRememberedAsync()
    {        try
        {
            var entries = await _human.AllAsync(CharacterId);
            var items = new List<HumanMemoryItem>();
            foreach (var e in entries)
            {
                ImageSource? img = null;
                if (!string.IsNullOrEmpty(e.ImagePath))
                {
                    var abs = Path.IsPathRooted(e.ImagePath) ? e.ImagePath : Path.Combine(App.RootDirectory, e.ImagePath);
                    if (File.Exists(abs)) img = ImageSource.FromFile(abs);
                }
                items.Add(new HumanMemoryItem
                {
                    Kind = e.Kind switch
                    {
                        HumanMemoryKind.Image => "照片",
                        HumanMemoryKind.Important => "重要时刻",
                        HumanMemoryKind.Abstraction => "印象",
                        _ => "事件"
                    },
                    Text = e.Text,
                    Time = e.CreatedAt.ToLocalTime().ToString("MM-dd HH:mm"),
                    RecallText = e.RecallCount > 0 ? $" · 被想起 {e.RecallCount} 次" : "",
                    TagText = e.Tags.Count == 0 ? "" : "#" + string.Join(" #", e.Tags),
                    Image = img
                });
            }
            HumanMemoryItems = new ObservableCollection<HumanMemoryItem>(items);

            var tags = await _tags.ListAsync(CharacterId);
            HasTags = tags.Count > 0;
            TagSummary = HasTags
                ? string.Join("　", tags.Select(t => $"{t.Name}×{t.Count}"))
                : "";

            var links = await _network.TopLinksAsync(CharacterId, 8);
            HasSemantic = links.Count > 0;
            SemanticSummary = HasSemantic
                ? string.Join("\n", links.Select(l => $"{l.A} ↔ {l.B}（{l.Weight:0}，{SourceText(l.Source)}）"))
                : "";
        }
        catch (Exception ex)
        {
            App.WriteLog("GalleryVM.ReloadRemembered -> " + ex.Message);
        }
    }

    private static string SourceText(string source) => source switch
    {
        "human" => "人工改过",
        "ai" => "AI 改过",
        _ => "自动积累"
    };

    private async Task ReloadLayersAsync()
    {
        try
        {
            var stats = await _overview.StatsAsync(CharacterId);
            LayerStats = new ObservableCollection<MemoryLayerItem>(stats.Select(s => new MemoryLayerItem
            {
                Name = s.Name,
                Describe = s.Describe,
                CountText = $"{s.Count} {s.Unit}",
                Detail = s.Detail
            }));
            HasLayerStats = LayerStats.Count > 0;
        }
        catch (Exception ex)
        {
            App.WriteLog("GalleryVM.ReloadLayers -> " + ex.Message);
            HasLayerStats = false;
        }
    }

    [RelayCommand]
    private void SelectTab(string tab)
    {
        IsMemoirSelected = tab == "memoir";
        IsRememberedSelected = tab == "remembered";
        IsDiarySelected = tab == "diary";
        IsCgSelected = tab == "cg";
    }

    partial void OnSearchTextChanged(string value) => _ = ReloadMemoirAsync();

    [RelayCommand]
    private void ClearSearch() => SearchText = "";

    private async Task ReloadMemoirAsync()
    {
        var charId = CharacterId;
        var aff = await _memory.All(charId, "affection");
        AffectionItems = new ObservableCollection<AffectionMemoryItem>(aff.Select(m =>
        {
            ImageSource? img = null;
            if (!string.IsNullOrEmpty(m.ImagePath) && File.Exists(m.ImagePath))
            {
                try { img = ImageSource.FromFile(m.ImagePath); } catch { img = null; }
            }
            return new AffectionMemoryItem
            {
                Delta = m.Weight > 0 ? $"+{m.Weight}" : m.Weight.ToString(),
                Reason = m.Keywords ?? "互动",
                Time = m.At.ToLocalTime().ToString("MM-dd HH:mm"),
                Image = img
            };
        }));
        HasAffection = AffectionItems.Count > 0;

        var all = await _memory.All(charId);
        var query = SearchText?.Trim();
        IEnumerable<Models.MemoryEntry> hits = all;
        if (!string.IsNullOrEmpty(query))
        {
            hits = all.Where(m => m.Content.Contains(query, StringComparison.OrdinalIgnoreCase)
                || (m.Keywords is not null && m.Keywords.Contains(query, StringComparison.OrdinalIgnoreCase)));
        }
        MemoryItems = new ObservableCollection<DialogueMemoryItem>(hits.Take(200).Select(m => new DialogueMemoryItem
        {
            Text = m.Content.Length > 120 ? m.Content[..120] + "…" : m.Content,
            Time = m.At.ToLocalTime().ToString("MM-dd HH:mm")
        }));
        HasMemory = MemoryItems.Count > 0;
    }

    private async Task ReloadDiaryAsync()
    {
        var notes = await _memory.Diary(CharacterId);
        DiaryItems = new ObservableCollection<DiaryDayItem>(notes.OrderByDescending(n => n.Date)
            .Select(n => new DiaryDayItem
            {
                Date = n.Date.ToLocalTime().ToString("yyyy年M月d日"),
                Mood = n.Mood,
                Content = n.Content
            }));
        HasDiary = DiaryItems.Count > 0;
    }

    private async Task ReloadCgAsync()
    {
        await _cg.RefreshAsync();
        var items = new List<CgCollectionItem>();
        foreach (var cg in _cg.AllUnlocked())
        {
            var path = _cg.ResolvePath(cg);
            ImageSource? img = null;
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                try { img = ImageSource.FromFile(path); } catch { img = null; }
            }
            items.Add(new CgCollectionItem
            {
                Title = string.IsNullOrWhiteSpace(cg.Title) ? "CG" : cg.Title,
                File = cg.File,
                Path = path ?? "",
                Image = img
            });
        }
        CgItems = new ObservableCollection<CgCollectionItem>(items);
        HasCg = CgItems.Count > 0;
    }

    /// <summary>收藏册点开一张 CG：全屏回看（左上角可跳过）。</summary>
    [RelayCommand]
    private async Task OpenCg(CgCollectionItem item)
    {
        if (item?.HasImage is not true) return;
        _cgView.ImagePath = item.Path;
        _cgView.Title = item.Title;
        _cgView.HasPayload = true;
        await Shell.Current.GoToAsync("cg-view");
    }
}

public sealed class CgCollectionItem
{
    public string Title { get; init; } = "";
    public string File { get; init; } = "";
    public string Path { get; init; } = "";
    public ImageSource? Image { get; init; }
    public bool HasImage => Image is not null;
}

public sealed class AffectionMemoryItem
{
    public string Delta { get; init; } = "";
    public string Reason { get; init; } = "";
    public string Time { get; init; } = "";
    public string Display => $"+{Delta} · {Reason} · {Time}";
    /// <summary>好感提升瞬间截屏，若有则显示。</summary>
    public ImageSource? Image { get; init; }
    public bool HasImage => Image is not null;
}

public sealed class DialogueMemoryItem
{
    public string Text { get; init; } = "";
    public string Time { get; init; } = "";
    public string Display => $"[{Time}] {Text}";
}

public sealed class DiaryDayItem
{
    public string Date { get; init; } = "";
    public string Mood { get; init; } = "";
    public string Content { get; init; } = "";
    public string Header => $"{Date} · {Mood}";
}

/// <summary>记忆分层总览里的一层。</summary>
public sealed class MemoryLayerItem
{
    public string Name { get; init; } = "";
    public string Describe { get; init; } = "";
    public string CountText { get; init; } = "";
    public string Detail { get; init; } = "";
}

/// <summary>人类记忆层里的一条「记得的事」。</summary>
public sealed class HumanMemoryItem
{
    public string Kind { get; init; } = "";
    public string Text { get; init; } = "";
    public string Time { get; init; } = "";
    public string RecallText { get; init; } = "";
    public string TagText { get; init; } = "";
    public bool HasTags => !string.IsNullOrEmpty(TagText);
    public ImageSource? Image { get; init; }
    public bool HasImage => Image is not null;
    public string Header => $"{Kind} · {Time}{RecallText}";
}

/// <summary>
/// 换装：列出角色现有的装扮，也能用一句描述让画面模型生成一套新的。
/// 生成的是「立绘素材」：落到角色目录并登记进 SpriteMap，之后走路人、桌宠、对话立绘都用同一份。
/// </summary>
public sealed partial class OutfitViewModel : ObservableObject
{
    private readonly GameEngine _engine;
    private readonly CharacterLibrary _library;
    private readonly ImageGenerationService _images;
    private readonly StorageProvider _store;

    public ObservableCollection<OutfitItem> Outfits { get; } = new();

    [ObservableProperty] private string _characterName = "";
    [ObservableProperty] private string _currentOutfit = "";
    [ObservableProperty] private string _prompt = "";
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _hasCharacter;
    [ObservableProperty] private bool _busy;
    [ObservableProperty] private ImageSource? _currentImage;
    [ObservableProperty] private bool _hasCurrentImage;

    public OutfitViewModel(GameEngine engine, CharacterLibrary library,
        ImageGenerationService images, StorageProvider store)
    {
        _engine = engine;
        _library = library;
        _images = images;
        _store = store;
    }

    public async Task RefreshAsync()
    {
        try
        {
            var ch = CurrentCharacter();
            HasCharacter = ch is not null;
            Outfits.Clear();
            CurrentImage = null;
            HasCurrentImage = false;
            if (ch is null)
            {
                CharacterName = "还没有进入角色";
                CurrentOutfit = "";
                Status = "先在角色选择页开始一局，或把某个角色设为主角。";
                return;
            }
            CharacterName = ch.Profile.Name;
            CurrentOutfit = ch.State.CurrentOutfit;
            var outfitKeys = ch.SpriteMap.Keys
                .Select(k => k.Split('/')[0])
                .Where(k => !string.IsNullOrWhiteSpace(k))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(k => k == ch.State.CurrentOutfit ? 0 : 1)
                .ThenBy(k => k, StringComparer.CurrentCulture)
                .ToList();
            foreach (var key in outfitKeys)
            {
                var preview = PreviewFor(ch, key);
                Outfits.Add(new OutfitItem
                {
                    Key = key,
                    Name = key == "default" ? "默认装扮" : key,
                    Image = preview,
                    HasImage = preview is not null,
                    IsCurrent = string.Equals(key, ch.State.CurrentOutfit, StringComparison.OrdinalIgnoreCase)
                });
            }
            var current = PreviewFor(ch, ch.State.CurrentOutfit);
            if (current is null && !string.IsNullOrWhiteSpace(ch.Avatar))
            {
                var full = Path.Combine(_store.Root, ch.Avatar);
                if (File.Exists(full)) current = ImageSource.FromFile(full);
            }
            CurrentImage = current;
            HasCurrentImage = current is not null;
            Status = Outfits.Count == 0
                ? "这个角色还没有立绘。可以在下面用一句描述生成第一套装扮。"
                : $"共 {Outfits.Count} 套装扮；换装后，对话与桌宠会一起用新立绘。";
        }
        catch (Exception ex)
        {
            Status = "读取失败：" + ex.Message;
            App.WriteLog("OutfitViewModel.Refresh -> " + ex);
        }
    }

    private CharacterData? CurrentCharacter()
    {
        if (_engine.ActiveCharacter is { } active) return active;
        var id = _engine.State.CharacterId;
        return !string.IsNullOrEmpty(id) && _engine.Roster.TryGetValue(id, out var ch) ? ch : null;
    }

    private ImageSource? PreviewFor(CharacterData ch, string outfit)
    {
        var key = ch.SpriteMap.Keys.FirstOrDefault(k =>
            k.StartsWith(outfit + "/", StringComparison.OrdinalIgnoreCase));
        if (key is null || !ch.SpriteMap.TryGetValue(key, out var rel) || string.IsNullOrWhiteSpace(rel))
            return null;
        var full = Path.Combine(_store.Root, rel);
        return File.Exists(full) ? ImageSource.FromFile(full) : null;
    }

    [RelayCommand]
    private async Task Wear(OutfitItem? item)
    {
        if (item is null) return;
        var ch = CurrentCharacter();
        if (ch is null) return;
        try
        {
            ch.State.CurrentOutfit = item.Key;
            var firstEmotion = ch.SpriteMap.Keys
                .FirstOrDefault(k => k.StartsWith(item.Key + "/", StringComparison.OrdinalIgnoreCase));
            if (firstEmotion is not null) ch.State.CurrentEmotion = firstEmotion.Split('/')[1];
            await _library.UpdateAsync(ch);
            await RefreshAsync();
            Status = $"已换上「{item.Name}」。";
        }
        catch (Exception ex)
        {
            Status = "换装失败：" + ex.Message;
            App.WriteLog("OutfitViewModel.Wear -> " + ex);
        }
    }

    [RelayCommand]
    private async Task Generate()
    {
        var ch = CurrentCharacter();
        if (ch is null) return;
        var prompt = (Prompt ?? "").Trim();
        if (prompt.Length == 0) { Status = "先写一句画面描述（例如：白色针织衫与浅色长裙，站在窗边）。"; return; }
        if (!_images.Enabled)
        {
            Status = "画面生成没有开启：设置 → AI → 画面生成里填接口与密钥后就能用。";
            return;
        }
        Busy = true;
        Status = "正在生成立绘…";
        try
        {
            var appearance = string.IsNullOrWhiteSpace(ch.Profile.Description) ? "" : "；角色设定：" + ch.Profile.Description;
            var (path, message) = await _images.GenerateAsync(
                $"{ch.Profile.Name} 的全身立绘，{prompt}。干净背景，完整人物，不要文字{appearance}",
                "outfit_" + ch.Profile.Id, "1024x1536");
            if (path is null) { Status = message; return; }

            var dir = Path.Combine(StorageRoot, "assets", "characters", ch.Profile.Id, "generated");
            Directory.CreateDirectory(dir);
            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var dest = Path.Combine(dir, stamp + ".png");
            File.Copy(path, dest, true);
            var rel = $"assets/characters/{ch.Profile.Id}/generated/{stamp}.png";

            var outfitKey = "generated_" + stamp;
            var sprites = new Dictionary<string, string>(ch.SpriteMap, StringComparer.OrdinalIgnoreCase);
            foreach (var emotion in new[] { "normal", "smile", "shy", "sad", "angry" })
                sprites[outfitKey + "/" + emotion] = rel;
            ch.State.CurrentOutfit = outfitKey;
            ch.State.CurrentEmotion = "normal";
            await _library.UpdateAsync(ch with { SpriteMap = sprites });
            Prompt = "";
            await RefreshAsync();
            Status = $"新装扮「{outfitKey}」已生成并穿上。";
        }
        catch (Exception ex)
        {
            Status = "生成失败：" + ex.Message;
            App.WriteLog("OutfitViewModel.Generate -> " + ex);
        }
        finally { Busy = false; }
    }

    private string StorageRoot => _store.Root;

    [RelayCommand]
    private async Task Back() => await Shell.Current.GoToAsync("..");
}

public sealed class OutfitItem
{
    public string Key { get; init; } = "";
    public string Name { get; init; } = "";
    public ImageSource? Image { get; init; }
    public bool HasImage { get; init; }
    public bool IsCurrent { get; init; }
    public string CurrentLabel => IsCurrent ? "穿着中" : "";
}

public sealed partial class SaveViewModel : ObservableObject
{
    private readonly SaveManager _save;
    private readonly SettingsManager _settings;
    private readonly Modules.NovelImport.NovelAnalyzer _novel;
    private readonly Services.CharacterLibrary _library;
    [ObservableProperty] private List<SaveSlotItem> _slots = new();
    [ObservableProperty] private bool _novelTesting;

    public SaveViewModel(SaveManager save, SettingsManager settings, Modules.NovelImport.NovelAnalyzer novel,
        Services.CharacterLibrary library)
    {
        _save = save;
        _settings = settings;
        _novel = novel;
        _library = library;
        NovelTesting = settings.Current.NovelTestingEnabled;
    }

    [RelayCommand]
    private async Task Refresh()
    {
        var list = await _save.List();
        var roster = await _library.ListAsync();
        Slots = list.Select(s => new SaveSlotItem
        {
            Slot = s,
            CharacterName = roster.FirstOrDefault(c => c.Profile.Id == s.Character)?.Profile.Name ?? "未知角色"
        }).ToList();
        NovelTesting = _settings.Current.NovelTestingEnabled;
    }

    [RelayCommand]
    private async Task LoadSlot(string id)
    {
        await _library.ListAsync();   // 确保角色库已载入，读档时才能恢复角色状态
        await _save.Load(id);
        // 过期存档被自动修复过就告知用户（不是静默改动数据）
        if (!string.IsNullOrWhiteSpace(_save.LastLoadNote))
        {
            var note = _save.LastLoadNote;
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                try { await Shell.Current.DisplayAlert("存档已自动修复", note, "好"); }
                catch (Exception ex) { App.WriteLog("SaveVM.RepairNote -> " + ex.Message); }
            });
        }
        await Shell.Current.GoToAsync("main");
    }

    [RelayCommand]
    private async Task ExportSlot(string id)
    {
        try
        {
            var bundle = _settings.Current.SaveBundleWithPlugins;
            var data = await _save.ExportStream(id, bundle);
            if (data is null) { await Shell.Current.DisplayAlert("导出", "存档不存在", "好"); return; }
            using var ms = new MemoryStream(data.Value.bytes);
            var result = await CommunityToolkit.Maui.Storage.FileSaver.Default.SaveAsync(data.Value.name, ms);
            if (result.IsSuccessful)
                await Shell.Current.DisplayAlert("导出",
                    bundle ? "已导出存档包（含所用插件代码，不含插件设置里的密钥）。" : "已导出存档。", "好");
        }
        catch (Exception ex)
        {
            App.WriteLog("SaveViewModel.ExportSlot -> " + ex);
        }
    }

    /// <summary>存档高级设置：插件/工具/角色白名单，不进入游戏也能改。</summary>
    [RelayCommand]
    private async Task OpenRules(string id)
        => await Shell.Current.GoToAsync($"saveRules?id={Uri.EscapeDataString(id)}");

    [RelayCommand]
    private async Task DeleteSlot(string id)
    {
        var slot = Slots.FirstOrDefault(s => s.Id == id);
        var ok = await Shell.Current.DisplayAlert("删除存档",
            $"确定删除「{slot?.Label ?? id}」吗？删除后不可恢复", "删除", "取消");
        if (!ok) return;
        await _save.Delete(id);
        await Refresh();
    }

    [RelayCommand]
    private async Task RenameSlot(string id)
    {
        var slot = Slots.FirstOrDefault(s => s.Id == id);
        var input = await Shell.Current.DisplayPromptAsync("重命名存档",
            "新的存档名称？", "保存", "取消", slot?.Label ?? "");
        if (string.IsNullOrWhiteSpace(input)) return;
        var ok = await _save.Rename(id, input.Trim());
        if (!ok) await Shell.Current.DisplayAlert("重命名", "重命名失败", "好");
        await Refresh();
    }

    [RelayCommand]
    private async Task ImportSave()
    {
        try
        {
            var pick = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "选择存档文件（.json / .wbsave）",
                FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    { DevicePlatform.WinUI, new[] { ".json", ".wbsave", ".zip" } },
                    { DevicePlatform.Android, new[] { "application/json", "application/zip", "application/octet-stream", "text/plain" } },
                    { DevicePlatform.iOS, new[] { "public.json", "public.zip-archive", "public.data" } }
                })
            });
            if (pick is null) return;
            var (_, message) = await _save.Import(pick.FullPath);
            await Shell.Current.DisplayAlert("导入存档", message, "好");
            await Refresh();
        }
        catch (Exception ex)
        {
            App.WriteLog("SaveViewModel.ImportSave -> " + ex);
        }
    }

    [RelayCommand]
    private async Task BackupAll()
    {
        try
        {
            var folder = await CommunityToolkit.Maui.Storage.FolderPicker.Default.PickAsync();
            if (folder?.Folder is null) return;
            var n = await _save.Backup(folder.Folder.Path);
            await Shell.Current.DisplayAlert("备份", n > 0 ? $"已备份 {n} 个存档到 {folder.Folder.Path}" : "没有可备份的存档", "好");
        }
        catch (Exception ex)
        {
            App.WriteLog("SaveViewModel.BackupAll -> " + ex);
        }
    }

    /// <summary>小说功能（测试）：仅当设置在实验性里开启后显示入口。</summary>
    [RelayCommand]
    private async Task ImportNovel()
    {
        try
        {
            var pick = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "选择小说文本",
                FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    { DevicePlatform.WinUI, new[] { ".txt", ".md" } },
                    { DevicePlatform.Android, new[] { "text/plain" } },
                    { DevicePlatform.iOS, new[] { "public.plain-text" } }
                })
            });
            if (pick is null) return;
            var result = await _novel.Analyze(pick.FullPath);
            if (result is null)
            {
                await Shell.Current.DisplayAlert("小说导入（测试）", "解析失败：请检查 AI 配置或文本内容", "好");
                return;
            }
            var chars = result.Characters.Count > 0 ? "角色：" + string.Join("、", result.Characters) : "角色：无";
            await Shell.Current.DisplayAlert($"小说导入（测试）· {result.Title}",
                $"{chars}\n\n摘要：{result.Summary}", "好");
        }
        catch (Exception ex)
        {
            App.WriteLog("SaveViewModel.ImportNovel -> " + ex);
        }
    }

    [RelayCommand]
    private async Task GoBack() => await Shell.Current.GoToAsync("..");
}

/// <summary>存档列表展示项：补充角色名（存档里只存角色 id）。</summary>
public sealed class SaveSlotItem
{
    public SaveSlot Slot { get; init; } = new();
    public string CharacterName { get; init; } = "";
    public string Id => Slot.Id;
    public string Label => Slot.Label;
    public string Scene => Slot.Scene;
    public DateTime SavedAt => Slot.SavedAt;
}
