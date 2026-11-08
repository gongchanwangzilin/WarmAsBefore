using System.Text.Json.Serialization;

namespace WarmAsBefore.Services;

/// <summary>一条「手机消息」：既可以是女仆戴上 WAB 手机后发来的，也可以是用户在假微信里自己发的。</summary>
public sealed class PhoneMessage
{
    public string Id { get; set; } = "";
    public string CharacterId { get; set; } = "";
    public string Text { get; set; } = "";
    public string SenderName { get; set; } = "";
    public bool IsUser { get; set; }
    public DateTime At { get; set; }
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
    private readonly SemaphoreSlim _lock = new(1, 1);
    // 微信/QQ 页的 ViewModel 是 transient，页面关掉就没用了。用弱引用订阅，
    // 免得每个开过页面的 VM 都被单例服务强引用住、永远回收不掉。
    private readonly List<WeakReference<Action<PhoneMessage>>> _subs = new();

    public PhoneMessageService(StorageProvider store) => _store = store;

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
        try
        {
            var data = await _store.Load<PhoneMessageStore>(StoreKey) ?? new PhoneMessageStore();
            return data.Items.Where(m => m.CharacterId == characterId)
                .OrderBy(m => m.At).ToList();
        }
        catch (Exception ex)
        {
            App.WriteLog("PhoneMessageService.For -> " + ex.Message);
            return Array.Empty<PhoneMessage>();
        }
    }

    /// <summary>写一条消息并返回它。id 可由调用方给定，用来在 UI 侧去重。</summary>
    public async Task<PhoneMessage> PublishAsync(string characterId, string text,
        string senderName = "", bool isUser = false, string? id = null)
    {
        var msg = new PhoneMessage
        {
            Id = string.IsNullOrEmpty(id) ? Guid.NewGuid().ToString("N")[..12] : id,
            CharacterId = characterId ?? "",
            Text = (text ?? "").Trim(),
            SenderName = senderName ?? "",
            IsUser = isUser,
            At = DateTime.UtcNow
        };
        await _lock.WaitAsync();
        try
        {
            var data = await _store.Load<PhoneMessageStore>(StoreKey) ?? new PhoneMessageStore();
            data.Items.Add(msg);
            // 每个角色只留最近若干条，别让这个文件无限长。
            data.Items = data.Items.GroupBy(m => m.CharacterId)
                .SelectMany(g => g.OrderBy(m => m.At).TakeLast(MaxPerCharacter))
                .OrderBy(m => m.At).ToList();
            await _store.Save(StoreKey, data);
        }
        catch (Exception ex)
        {
            App.WriteLog("PhoneMessageService.Publish -> " + ex.Message);
        }
        finally { _lock.Release(); }

        Raise(msg);
        return msg;
    }
}
