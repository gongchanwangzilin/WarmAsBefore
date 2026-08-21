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

public sealed partial class PhoneViewModel : ObservableObject
{
    private readonly SettingsManager _settings;
    private readonly Modules.RealChat.OfficialChatBridge _bridge;
    private readonly Modules.RealWorld.TimeProvider _time;

    [ObservableProperty] private string _timeLabel = "";
    [ObservableProperty] private string _weatherText = "晴天";
    [ObservableProperty] private string _statusTime = "";
    [ObservableProperty] private string _weChatLabel = "微信";
    [ObservableProperty] private string _statusLine = "温暖如初 · 手机";

    public PhoneViewModel(SettingsManager settings,
        Modules.RealChat.OfficialChatBridge bridge, Modules.RealWorld.TimeProvider time)
    {
        _settings = settings;
        _bridge = bridge;
        _time = time;
        RefreshStatus();
        _bridge.StatusChanged += _ => MainThread.BeginInvokeOnMainThread(RefreshStatus);
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
    }

    [RelayCommand]
    private async Task OpenChat() => await SafeNav("chat");

    [RelayCommand]
    private async Task OpenMap() => await SafeNav("map");

    [RelayCommand]
    private async Task OpenGallery() => await SafeNav("gallery");

    [RelayCommand]
    private async Task OpenLibrary() => await SafeNav("roster");

    [RelayCommand]
    private async Task OpenShop() => await SafeNav("shop");

    [RelayCommand]
    private async Task OpenGame(string? kind)
    {
        var k = kind ?? "gobang";
        await SafeNav($"game?kind={k}");
    }

    [RelayCommand]
    private async Task Back() => await SafeNav("..");

    /// <summary>导航统一兜底：async void 链上任何异常都会崩 WinUI，此处拦截记录。</summary>
    private async Task SafeNav(string route)
    {
        try
        {
            await Shell.Current.GoToAsync(route);
        }
        catch (Exception ex)
        {
            App.WriteLog($"PhoneViewModel.SafeNav({route}) -> {ex.Message}");
        }
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

    [ObservableProperty] private string _inputText = "";
    [ObservableProperty] private bool _isListening;
    [ObservableProperty] private bool _isTyping;
    [ObservableProperty] private int _typingDots;
    [ObservableProperty] private string _typingLabel = "";
    [ObservableProperty] private string _partnerName = "她";
    [ObservableProperty] private ImageSource? _partnerAvatar;
    [ObservableProperty] private bool _hasPartnerAvatar;
    [ObservableProperty] private string _modeLabel = "本地微信";
    [ObservableProperty] private bool _isStickerPanelVisible;
    // ============ 送礼 / 使用面板（微信聊天时显示） ============
    [ObservableProperty] private bool _isGiftPanelVisible;
    private CancellationTokenSource? _typingCts;
    public ObservableCollection<ChatItem> Messages { get; } = new();
    /// <summary>用户导入的表情库（全路径）：图片/动图/视频。</summary>
    public ObservableCollection<string> Stickers { get; } = new();
    public bool HasStickers => Stickers.Count > 0;
    private string StickerDir => Path.Combine(_store.Root, "Stickers");

    public WeChatViewModel(GameEngine engine, ChatEngine chat, SpeechService speech,
        Modules.RealChat.OfficialChatBridge bridge, CharacterLibrary library,
        StorageProvider store, SettingsManager settings, Modules.Market.GiftPanelService gifts)
    {
        _engine = engine;
        _chat = chat;
        _speech = speech;
        _bridge = bridge;
        _library = library;
        _store = store;
        _settings = settings;
        _gifts = gifts;
        _bridge.Incoming += OnRealIncoming;
        _bridge.Replied += OnRealReplied;
        _bridge.StatusChanged += _ => MainThread.BeginInvokeOnMainThread(RefreshMode);
    }

    /// <summary>进入微信页时刷新：对方头像+名字 + 真微信/假微信状态。</summary>
    public async Task LoadAsync()
    {
        var charId = _engine.State.CharacterId;
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
                _chat.SetRoster(_library.RosterContext(ch.Profile.Id));
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
        var real = _settings.Current.WechatEnabled
            && _bridge.Channels.Length > 1 && _bridge.Channels[1].IsRunning;
        ModeLabel = real ? "真微信 · 已接入" : "本地微信 · 她还在";
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
            var aiMsg = new ChatItem { Text = reply, DisplayText = "", IsUser = false };
            Messages.Add(aiMsg);
            _ = Typewrite(aiMsg, reply);
            MaybeAiSticker(reply);
        });
    }

    [RelayCommand]
    private async Task Send()
    {
        if (string.IsNullOrWhiteSpace(InputText)) return;
        var msg = InputText.Trim();
        InputText = "";
        var charId = _engine.State.CharacterId;
        if (string.IsNullOrEmpty(charId)) return;

        Messages.Add(new ChatItem { Text = msg, DisplayText = msg, IsUser = true });

        IsTyping = true;
        _typingCts = new CancellationTokenSource();
        _ = AnimateTypingDots(_typingCts.Token);
        var reply = await _chat.Send(charId, msg);
        _typingCts.Cancel();
        IsTyping = false;

        var aiMsg = new ChatItem { Text = reply, DisplayText = "", IsUser = false };
        Messages.Add(aiMsg);
        _ = Typewrite(aiMsg, reply);
        MaybeAiSticker(reply);
    }

    // ============ 送礼 / 使用面板（微信聊天） ============

    [RelayCommand]
    private void ToggleGiftPanel()
    {
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

    /// <summary>导入表情：选任何文件 → 复制进表情库 → 立即出现在面板。</summary>
    [RelayCommand]
    private async Task ImportSticker()
    {
        var path = await PickFileAsync();
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            var dir = StickerDir;
            Directory.CreateDirectory(dir);
            var dest = Path.Combine(dir, $"{DateTime.Now:yyyyMMddHHmmss}_{Path.GetFileName(path)}");
            File.Copy(path, dest, overwrite: true);
            Stickers.Add(dest);
            OnPropertyChanged(nameof(HasStickers));
        }
        catch (Exception ex)
        {
            App.WriteLog("WeChatViewModel.ImportSticker -> " + ex);
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
        var aiMsg = new ChatItem { Text = reply, DisplayText = "", IsUser = false };
        Messages.Add(aiMsg);
        _ = Typewrite(aiMsg, reply);
        MaybeAiSticker(reply);
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

    /// <summary>AI 回复后自发表情包：从用户表情库随机挑一张（库为空则不发）。</summary>
    private void MaybeAiSticker(string reply)
    {
        if (string.IsNullOrEmpty(reply) || Stickers.Count == 0) return;
        if (Random.Shared.Next(100) < 35)
        {
            var pick = Stickers[Random.Shared.Next(Stickers.Count)];
            AddMediaMessage(pick, isUser: false);
        }
    }

#if WINDOWS
    private async Task<string?> PickFileAsync()
    {
        try
        {
            var win = Application.Current?.Windows.FirstOrDefault(w => w.Handler is not null);
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

    [RelayCommand]
    private async Task Back() => await Shell.Current.GoToAsync("..");
}

public sealed partial class GalleryViewModel : ObservableObject
{
    private readonly Modules.AiChat.MemoryVault _memory;
    private readonly GameEngine _engine;
    private readonly Modules.Automation.DailyDiaryWriter _diary;

    [ObservableProperty] private bool _isMemoirSelected = true;
    [ObservableProperty] private bool _isDiarySelected;
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private ObservableCollection<AffectionMemoryItem> _affectionItems = new();
    [ObservableProperty] private ObservableCollection<DialogueMemoryItem> _memoryItems = new();
    [ObservableProperty] private ObservableCollection<DiaryDayItem> _diaryItems = new();
    [ObservableProperty] private bool _hasAffection;
    [ObservableProperty] private bool _hasMemory;
    [ObservableProperty] private bool _hasDiary;

    public GalleryViewModel(Modules.AiChat.MemoryVault memory, GameEngine engine,
        Modules.Automation.DailyDiaryWriter diary)
    {
        _memory = memory;
        _engine = engine;
        _diary = diary;
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
    }

    [RelayCommand]
    private void SelectTab(string tab)
    {
        IsMemoirSelected = tab == "memoir";
        IsDiarySelected = tab == "diary";
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

public sealed partial class OutfitViewModel : ObservableObject
{
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
        await Shell.Current.GoToAsync("main");
    }

    [RelayCommand]
    private async Task ExportSlot(string id)
    {
        try
        {
            var data = await _save.ExportStream(id);
            if (data is null) { await Shell.Current.DisplayAlert("导出", "存档不存在", "好"); return; }
            using var ms = new MemoryStream(data.Value.bytes);
            var result = await CommunityToolkit.Maui.Storage.FileSaver.Default.SaveAsync(data.Value.name, ms);
            if (result.IsSuccessful)
                await Shell.Current.DisplayAlert("导出", "已导出存档", "好");
        }
        catch (Exception ex)
        {
            App.WriteLog("SaveViewModel.ExportSlot -> " + ex);
        }
    }

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
                PickerTitle = "选择存档文件",
                FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    { DevicePlatform.WinUI, new[] { ".json" } },
                    { DevicePlatform.Android, new[] { "application/json", "text/plain" } },
                    { DevicePlatform.iOS, new[] { "public.json" } }
                })
            });
            if (pick is null) return;
            var ok = await _save.Import(pick.FullPath);
            await Shell.Current.DisplayAlert("导入存档", ok ? "导入成功" : "导入失败：文件格式不正确", "好");
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

public sealed partial class DeveloperViewModel : ObservableObject
{
    [ObservableProperty] private string _promptLog = "等待 AI 请求…";
    [ObservableProperty] private string _toolLog = "等待工具调用…";

    [RelayCommand]
    private void Clear()
    {
        PromptLog = "";
        ToolLog = "";
    }
}
