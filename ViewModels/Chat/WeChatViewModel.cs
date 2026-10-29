using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WarmAsBefore.Controls;
using WarmAsBefore.Drawables;
using WarmAsBefore.Models;
using WarmAsBefore.Modules.AiChat;
using WarmAsBefore.Modules.Automation;
using WarmAsBefore.Modules.SaveSystem;
using WarmAsBefore.Services;

namespace WarmAsBefore.ViewModels;

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
                Toast.Info("还没填视频号链接。到「设置 → 外部接入 → 视频号链接」里填一个 https:// 地址再试。");
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
    /// 图片类且配了识图模型时同时 OCR 一句描述，登记进统一表情库（AI 按描述选表情）。</summary>
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
    /// 一个都没命中时，按小概率随机自发一张，让聊天有生气。
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
