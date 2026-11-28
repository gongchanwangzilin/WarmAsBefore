using System.Text.Json.Serialization;

namespace WarmAsBefore.Services;

/// <summary>一条「手机消息」：既可以是女仆戴上 WAB 手机后发来的，也可以是用户在假微信里自己发的。</summary>
public sealed class PhoneMessage
{
    public const string KindText = "text";
    public const string KindImage = "image";
    public const string KindSticker = "sticker";
    public const string KindFile = "file";

    public string Id { get; set; } = "";
    public string CharacterId { get; set; } = "";
    public string Text { get; set; } = "";
    public string SenderName { get; set; } = "";
    public bool IsUser { get; set; }
    public DateTime At { get; set; }

    /// <summary>
    /// text / image / sticker / file。初值就是 text：老档里没有这个字段，反序列化后仍是 text，
    /// 旧消息照旧按文字显示，不会变成空气泡。
    /// </summary>
    public string Kind { get; set; } = KindText;

    /// <summary>媒体文件在存储根下的相对路径（assets/chat/…）；文字消息是空串。</summary>
    public string MediaPath { get; set; } = "";

    /// <summary>用户选中的原文件名（文件卡片上那行标题）。</summary>
    public string FileName { get; set; } = "";

    /// <summary>缩略图相对路径，可选。现在不生成缩略图：读的时候没有就退回 MediaPath 那张原图。</summary>
    public string ThumbPath { get; set; } = "";

    /// <summary>读的时候归一：空值或不认识的 kind 一律当 text（手改过存档也不炸）。</summary>
    [JsonIgnore]
    public string EffectiveKind => string.IsNullOrWhiteSpace(Kind) ? KindText : Kind;
}

/// <summary>落盘容器。键 phone_messages，与存档无关（联系人是谁、发过什么是跨存档的本地状态）。</summary>
public sealed class PhoneMessageStore
{
    [JsonPropertyName("items")]
    public List<PhoneMessage> Items { get; set; } = new();
}

/// <summary>
/// 假微信的消息中枢。微信页的 ViewModel 是 transient（页面关掉实例就没了），
/// 所以消息必须由单例落盘，再用事件推给当前开着的页面；下次进页面按角色把历史补回来。
/// </summary>
public sealed class PhoneMessageService
{
    private const string StoreKey = "phone_messages";
    private const int MaxPerCharacter = 120;

    private readonly StorageProvider _store;
    private readonly Modules.SaveSystem.SaveManager _save;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private CancellationTokenSource? _persistCts;
    // 微信/QQ 页的 ViewModel 是 transient，页面关掉就没用了。用弱引用订阅，
    // 免得每个开过页面的 VM 都被单例服务强引用住、永远回收不掉。
    private readonly List<WeakReference<Action<PhoneMessage>>> _subs = new();

    public PhoneMessageService(StorageProvider store, Modules.SaveSystem.SaveManager save)
    {
        _store = store;
        _save = save;
    }

    /// <summary>现在是否活在一份存档里。微信只在存档内成立，没有存档时才退回老的全局列表。</summary>
    private bool InSave => !string.IsNullOrEmpty(_save.CurrentSaveId);

    /// <summary>订阅新消息。调用方要自己持有这个委托（存成字段），否则会被 GC 收走。</summary>
    public void Subscribe(Action<PhoneMessage> handler)
    {
        if (handler is null) return;
        lock (_subs) _subs.Add(new WeakReference<Action<PhoneMessage>>(handler));
    }

    private void Raise(PhoneMessage msg)
    {
        List<Action<PhoneMessage>> alive = new();
        lock (_subs)
        {
            for (var i = _subs.Count - 1; i >= 0; i--)
            {
                if (_subs[i].TryGetTarget(out var h)) alive.Add(h);
                else _subs.RemoveAt(i);
            }
        }
        foreach (var h in alive)
        {
            try { h(msg); }
            catch (Exception ex) { App.WriteLog("PhoneMessageService.Raise -> " + ex.Message); }
        }
    }

    /// <summary>某个角色的全部历史消息，按时间正序。</summary>
    public async Task<IReadOnlyList<PhoneMessage>> ForAsync(string characterId)
    {
        if (string.IsNullOrEmpty(characterId)) return Array.Empty<PhoneMessage>();
        if (InSave)
        {
            // 存档内的分角色池：这一档里这个角色的全部消息。
            if (_save.WeChatLog.TryGetValue(characterId, out var mine) && mine.Count > 0)
                return WithoutFailures(mine).OrderBy(m => m.At).ToList();

            // 老数据（全局 phone_messages）只在第一次遇到这一档时搬进来一次，之后不再读它，
            // 否则同一个角色在两个档里会看到同一份历史。
            var legacy = WithoutFailures(await LegacyForAsync(characterId)).ToList();
            if (legacy.Count > 0)
            {
                _save.WeChatLog[characterId] = legacy.TakeLast(MaxPerCharacter).ToList();
                SchedulePersist();
            }
            return legacy;
        }
        // 没有档时（首页/角色库）读的是老的全局列表，同样要滤掉失败回显。
        return WithoutFailures(await LegacyForAsync(characterId)).ToList();
    }

    /// <summary>
    /// 老数据里混着一批「[API错误 …]」「[请求超时 …]」的失败回显——它们被当成了聊天内容存下来，
    /// 于是刚进微信页就看到三条报错，像是没说话她就在调接口。这一类不是对话，读的时候滤掉。
    /// </summary>
    private static IEnumerable<PhoneMessage> WithoutFailures(IEnumerable<PhoneMessage> src) =>
        src.Where(m =>
        {
            var t = (m.Text ?? "").TrimStart();
            return !(t.StartsWith("[API错误", StringComparison.Ordinal)
                  || t.StartsWith("[请求超时", StringComparison.Ordinal));
        });

    private async Task<List<PhoneMessage>> LegacyForAsync(string characterId)
    {
        try
        {
            var data = await _store.Load<PhoneMessageStore>(StoreKey) ?? new PhoneMessageStore();
            return data.Items.Where(m => m.CharacterId == characterId)
                .OrderBy(m => m.At).ToList();
        }
        catch (Exception ex)
        {
            App.WriteLog("PhoneMessageService.For -> " + ex.Message);
            return new List<PhoneMessage>();
        }
    }

    /// <summary>写一条消息并返回它。id 可由调用方给定，用来在 UI 侧去重。</summary>
    public Task<PhoneMessage> PublishAsync(string characterId, string text,
        string senderName = "", bool isUser = false, string? id = null) =>
        StoreAsync(new PhoneMessage
        {
            Id = string.IsNullOrEmpty(id) ? NewId() : id,
            CharacterId = characterId ?? "",
            Text = (text ?? "").Trim(),
            SenderName = senderName ?? "",
            IsUser = isUser,
            At = DateTime.UtcNow
        });

    /// <summary>
    /// 写一条媒体消息（图片 / 表情 / 文件）。内容在 <see cref="PhoneMessage.MediaPath"/> 指的那个文件里，
    /// 前端按 kind 画成图片气泡还是文件卡片；Text 只留一句 <see cref="PreviewOf"/> 标记给只认文字的读法。
    /// 落盘与其他消息同一条路。
    /// </summary>
    public Task<PhoneMessage> PublishMediaAsync(string characterId, string kind,
        string mediaPath, string fileName, string senderName = "", bool isUser = true) =>
        StoreAsync(new PhoneMessage
        {
            Id = NewId(),
            CharacterId = characterId ?? "",
            Text = PreviewOf(kind, fileName),
            Kind = kind,
            MediaPath = mediaPath ?? "",
            FileName = fileName ?? "",
            SenderName = senderName ?? "",
            IsUser = isUser,
            At = DateTime.UtcNow
        });

    private static string NewId() => Guid.NewGuid().ToString("N")[..12];

    /// <summary>
    /// 媒体消息的 <see cref="PhoneMessage.Text"/> 留一句标记：那是老读法唯一认得的字段，
    /// 只认文字的地方（原生微信页、名册里的「最近一条」）看到的就是这句，而不是一个空气泡；
    /// QV 按 kind 画真图，根本不读它。
    /// </summary>
    public static string PreviewOf(string kind, string fileName = "") => kind switch
    {
        PhoneMessage.KindImage => "[图片]",
        PhoneMessage.KindSticker => "[表情]",
        PhoneMessage.KindFile => string.IsNullOrWhiteSpace(fileName) ? "[文件]" : fileName,
        _ => "",
    };

    private async Task<PhoneMessage> StoreAsync(PhoneMessage msg)
    {
        await _lock.WaitAsync();
        try
        {
            if (InSave)
            {
                if (!_save.WeChatLog.TryGetValue(msg.CharacterId, out var list))
                    _save.WeChatLog[msg.CharacterId] = list = new List<PhoneMessage>();
                list.Add(msg);
                if (list.Count > MaxPerCharacter)
                    _save.WeChatLog[msg.CharacterId] = list.OrderBy(m => m.At).TakeLast(MaxPerCharacter).ToList();
                SchedulePersist();
            }
            else
            {
                var data = await _store.Load<PhoneMessageStore>(StoreKey) ?? new PhoneMessageStore();
                data.Items.Add(msg);
                // 每个角色只留最近若干条，别让这个文件无限长。
                data.Items = data.Items.GroupBy(m => m.CharacterId)
                    .SelectMany(g => g.OrderBy(m => m.At).TakeLast(MaxPerCharacter))
                    .OrderBy(m => m.At).ToList();
                await _store.Save(StoreKey, data);
            }
        }
        catch (Exception ex)
        {
            App.WriteLog("PhoneMessageService.Publish -> " + ex.Message);
        }
        finally { _lock.Release(); }

        Raise(msg);
        if (!msg.IsUser) NotifyArrived(msg);
        return msg;
    }

    /// <summary>
    /// 「她发来一条」对前端的到达感：<c>chat.arrived { characterId, preview, name }</c>。
    /// 前端据此刷新角标，并在那一格会话没开着时弹一条提示（正文取消息本身的截断，不另编文案）。
    /// 事件是通知不是合同：没有窗的时候 EmitOrQueue 先攒着，前端挂上来再补发。
    /// </summary>
    private static void NotifyArrived(PhoneMessage msg)
    {
        var text = (msg.Text ?? "").Replace('\r', ' ').Replace('\n', ' ').Trim();
        var preview = text.Length > 40 ? text[..40] + "…" : text;
        Services.Web.WebBridge.EmitOrQueue("chat.arrived", new
        {
            characterId = msg.CharacterId,
            preview,
            name = msg.SenderName ?? "",
        });
    }

    // ==================== 媒体素材 ====================

    /// <summary>
    /// 聊天素材夹：<c>assets/chat/{角色}/{yyyyMM}/</c>。与立绘、头像同一个存储根，
    /// 桥那边的 <c>AssetUrl</c>（https://wab.assets/&lt;相对路径&gt;）直接认得，不用另开一条素材通道。
    /// </summary>
    private string MediaDirOf(string characterId, string stamp) =>
        Path.Combine(_store.Root, "assets", "chat", SafeId(characterId), stamp);

    /// <summary>角色 id 进路径前先滤一遍：只留字母数字与 _ -，免得 id 里带斜杠把素材写到别处去。</summary>
    private static string SafeId(string? id)
    {
        var chars = (id ?? "").Where(c => char.IsLetterOrDigit(c) || c is '_' or '-').ToArray();
        return chars.Length == 0 ? "unknown" : new string(chars);
    }

    private static readonly HashSet<string> ImageExts = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp"
    };

    /// <summary>
    /// 把用户选中的文件拷进聊天素材夹，返回要存进消息里的相对路径（<c>assets/chat/…</c>，正斜杠）。
    /// 文件名取内容 SHA-256 的前 16 位：同一张图连发两次只占一份，也不会把原文件名塞进 URL。
    /// 读不出来、写不进去、图片类型对不上都返回 null —— 调用方据此报错，不让前端以为已经发出去了。
    /// </summary>
    public async Task<string?> ImportMediaAsync(string characterId, string sourcePath, string kind)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath)) return null;
            var ext = Path.GetExtension(sourcePath).ToLowerInvariant();
            if (ext.Length == 0) return null;
            if (kind is PhoneMessage.KindImage or PhoneMessage.KindSticker && !ImageExts.Contains(ext)) return null;

            var stamp = DateTime.Now.ToString("yyyyMM");
            var hash = await Task.Run(() => HashOf(sourcePath));
            if (hash is null) return null;

            var dir = MediaDirOf(characterId, stamp);
            var dest = Path.Combine(dir, hash + ext);
            await Task.Run(() =>
            {
                Directory.CreateDirectory(dir);
                File.Copy(sourcePath, dest, overwrite: true);
            });
            return $"assets/chat/{SafeId(characterId)}/{stamp}/{hash}{ext}";
        }
        catch (Exception ex)
        {
            App.WriteLog("PhoneMessageService.ImportMedia -> " + ex.Message);
            return null;
        }
    }

    private static string? HashOf(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var sha = System.Security.Cryptography.SHA256.Create();
            return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant()[..16];
        }
        catch (Exception ex)
        {
            App.WriteLog("PhoneMessageService.HashOf -> " + ex.Message);
            return null;
        }
    }

    /// <summary>消息里的相对路径 → 盘上完整路径。越出存储根、文件已经不在都返回 null。</summary>
    public string? ResolveMediaPath(string? mediaPath)
    {
        if (string.IsNullOrWhiteSpace(mediaPath)) return null;
        try
        {
            var rel = mediaPath.Replace('\\', '/').TrimStart('/');
            if (rel.Contains("..", StringComparison.Ordinal)) return null;
            var full = Path.GetFullPath(Path.Combine(_store.Root, rel.Replace('/', Path.DirectorySeparatorChar)));
            var root = Path.GetFullPath(_store.Root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return null;
            return File.Exists(full) ? full : null;
        }
        catch (Exception ex)
        {
            App.WriteLog("PhoneMessageService.ResolveMediaPath -> " + ex.Message);
            return null;
        }
    }

    /// <summary>媒体文件的字节数；文件不在就是 0（前端的文件卡片据此显示大小，读不到就不编一个）。</summary>
    public long MediaSizeOf(string? mediaPath)
    {
        var full = ResolveMediaPath(mediaPath);
        if (full is null) return 0;
        try { return new FileInfo(full).Length; }
        catch { return 0; }
    }

    /// <summary>
    /// 攒 1.8 秒再写盘：连发几条消息时不会每条都整档重写一次。
    /// 写的是「当前档」——换档之前必须已经落盘，否则消息跟着上一档走了。
    /// </summary>
    private void SchedulePersist()
    {
        _persistCts?.Cancel();
        var cts = new CancellationTokenSource();
        _persistCts = cts;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(1800, cts.Token);
                if (cts.IsCancellationRequested || !InSave) return;
                await _save.Commit("微信", "para");
            }
            catch (OperationCanceledException) { /* 故意忽略：落盘去抖被取消即放弃这次写，下次变动再排 */ }
            catch (Exception ex) { App.WriteLog("PhoneMessageService.Persist -> " + ex.Message); }
        });
    }

    /// <summary>换档/退出前把还没落盘的微信消息写进去。</summary>
    public async Task FlushAsync()
    {
        _persistCts?.Cancel();
        if (!InSave) return;
        try { await _save.Commit("微信", "para"); }
        catch (Exception ex) { App.WriteLog("PhoneMessageService.Flush -> " + ex.Message); }
    }
}
