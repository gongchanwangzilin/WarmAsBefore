using WarmAsBefore.Models;

namespace WarmAsBefore.Services;

/// <summary>
/// 微信好友服务：管理"已加微信"的角色列表（落盘到 storage 的 wechat_contacts 键）。
///
/// 使用流程（与需求一致）：
///   1) AI 在剧情里用到了角色库里的某个角色；
///   2) AI 通过「加微信」工具（ToolManager 的 add_wechat）确认加好友；
///   3) 加过之后该角色成为常驻 NPC，出现在微信联系人列表里，可随时点进去聊天。
/// </summary>
public sealed class WeChatService
{
    private const string ContactsKey = "wechat_contacts";

    private readonly StorageProvider _store;
    private readonly CharacterLibrary _library;
    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <summary>
    /// 落盘失败时的内存副本：这一局仍然能看见、能聊，下次成功落盘时并回去。
    /// 磁盘写不进去（权限/杀软/低完整性）不该让「她已经在你通讯录里」这件事凭空消失。
    /// </summary>
    private readonly List<WeChatContact> _ghost = new();

    public WeChatService(StorageProvider store, CharacterLibrary library)
    {
        _store = store;
        _library = library;
    }

    /// <summary>联系人列表变化（加/删/更新）时通知 UI。</summary>
    public event Action? ContactsChanged;

    /// <summary>读全部联系人：磁盘那份 + 这一局没能落盘的内存那份（按角色去重，磁盘优先）。</summary>
    private async Task<List<WeChatContact>> AllAsync()
    {
        var data = await _store.Load<WeChatContacts>(ContactsKey) ?? new WeChatContacts();
        var list = data.Items;
        lock (_ghost)
        {
            foreach (var g in _ghost)
                if (!list.Any(c => c.CharacterId == g.CharacterId))
                    list.Add(g);
        }
        return list;
    }

    /// <summary>写盘；失败不抛，只记日志——调用方照常把联系人当成功处理。</summary>
    private async Task<bool> TrySaveAsync(List<WeChatContact> items)
    {
        try
        {
            await _store.Save(ContactsKey, new WeChatContacts { Items = items });
            return true;
        }
        catch (Exception ex)
        {
            App.WriteLog("WeChat.Save -> " + ex.Message);
            return false;
        }
    }

    /// <summary>读取全部微信联系人（按最近消息时间倒序）。</summary>
    public async Task<List<WeChatContact>> ListAsync()
    {
        var items = await AllAsync();
        return items
            .OrderByDescending(c => c.LastMessageAt)
            .ThenByDescending(c => c.AddedAt)
            .ToList();
    }

    /// <summary>是否已经是微信好友。</summary>
    public async Task<bool> IsFriendAsync(string characterId)
    {
        if (string.IsNullOrEmpty(characterId)) return false;
        var list = await ListAsync();
        return list.Any(c => c.CharacterId == characterId);
    }

    /// <summary>
    /// 加微信：把角色库里的一个角色加入微信联系人（幂等）。
    /// 角色必须存在于角色库中（即剧情里真的用过这个角色）。
    /// </summary>
    /// <returns>(是否成功, 提示文本, 是否新加)。</returns>
    public async Task<(bool ok, string message, bool added)> AddContactAsync(string characterIdOrName)
    {
        var raw = (characterIdOrName ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(raw)) return (false, "请给出要加微信的角色名或 Id", false);

        await _lock.WaitAsync();
        try
        {
            // 1) 找到角色库里的目标角色（支持按 Id 或名字匹配）
            var chars = await _library.ListAsync();
            var ch = chars.FirstOrDefault(c => c.Profile.Id == raw)
                     ?? chars.FirstOrDefault(c => c.Profile.Name == raw)
                     ?? chars.FirstOrDefault(c => !string.IsNullOrEmpty(c.Profile.Nickname) && c.Profile.Nickname == raw);
            if (ch is null)
                return (false, $"角色库里没有找到「{raw}」——只有剧情里用过的角色才能加微信", false);

            var data = await _store.Load<WeChatContacts>(ContactsKey) ?? new WeChatContacts();
            var list = data.Items;

            // 2) 已存在则幂等返回
            if (list.Any(c => c.CharacterId == ch.Profile.Id))
                return (true, $"「{ch.Profile.Name}」已经是你的微信好友了", false);

            // 3) 新加：标记为常驻 NPC
            var contact = new WeChatContact
            {
                CharacterId = ch.Profile.Id,
                AddedAt = DateTime.UtcNow,
                LastMessageAt = DateTime.UtcNow,
                LastMessage = "",
                IsResident = true,
                Unread = false
            };
            list.Add(contact);
            if (!await TrySaveAsync(list))
            {
                // 写不进去也要留在内存里：这一局她照样在通讯录里，下次落盘成功时自动并回去。
                lock (_ghost)
                    if (!_ghost.Any(c => c.CharacterId == contact.CharacterId))
                        _ghost.Add(contact);
                ContactsChanged?.Invoke();
                return (true, $"已加上「{ch.Profile.Name}」的微信（本次未能写入磁盘，重开应用前仍可聊天）", true);
            }
            ContactsChanged?.Invoke();
            return (true, $"已加上「{ch.Profile.Name}」的微信，TA 现在是常驻好友了", true);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>删除一个微信联系人。</summary>
    public async Task<(bool ok, string message)> RemoveAsync(string characterId)
    {
        if (string.IsNullOrEmpty(characterId)) return (false, "请给出要删除的联系人");
        await _lock.WaitAsync();
        try
        {
            var items = await AllAsync();
            var removed = items.RemoveAll(c => c.CharacterId == characterId);
            if (removed == 0) return (false, "该联系人不存在");
            lock (_ghost) _ghost.RemoveAll(c => c.CharacterId == characterId);
            await TrySaveAsync(items);
            ContactsChanged?.Invoke();
            return (true, "已删除该微信联系人");
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>更新联系人最近一条消息（聊天后调用，用于列表摘要与排序）。</summary>
    public async Task TouchLastMessageAsync(string characterId, string lastMessage, bool unread = false)
    {
        if (string.IsNullOrEmpty(characterId)) return;
        await _lock.WaitAsync();
        try
        {
            var items = await AllAsync();
            var c = items.FirstOrDefault(x => x.CharacterId == characterId);
            if (c is null) return;
            c.LastMessage = lastMessage ?? string.Empty;
            c.LastMessageAt = DateTime.UtcNow;
            c.Unread = unread;
            await TrySaveAsync(items);
            ContactsChanged?.Invoke();
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>标记联系人已读（点进去聊天时调用）。</summary>
    public async Task MarkReadAsync(string characterId)
    {
        if (string.IsNullOrEmpty(characterId)) return;
        await _lock.WaitAsync();
        try
        {
            var items = await AllAsync();
            var c = items.FirstOrDefault(x => x.CharacterId == characterId);
            if (c is null || !c.Unread) return;
            c.Unread = false;
            await TrySaveAsync(items);
            ContactsChanged?.Invoke();
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>生成给 AI 的微信好友上下文（谁在我的微信好友里）。</summary>
    public async Task<string> BuildContactsContextAsync()
    {
        var list = await ListAsync();
        if (list.Count == 0) return "";
        var chars = await _library.ListAsync();
        var names = list
            .Select(c => chars.FirstOrDefault(x => x.Profile.Id == c.CharacterId)?.Profile.Name ?? c.CharacterId)
            .Where(n => !string.IsNullOrEmpty(n));
        return $"【微信好友】{string.Join("、", names)}。这些是已经加上你微信、可以每天用手机聊天的常驻好友。";
    }
}