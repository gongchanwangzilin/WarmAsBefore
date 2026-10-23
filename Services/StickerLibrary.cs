namespace WarmAsBefore.Services;

/// <summary>
/// 一个已导入的表情包条目：图片复制进应用数据目录，随时可被对话/假微信/机器人通道取用。
/// </summary>
public sealed record StickerItem
{
    public string Id { get; init; } = "";
    /// <summary>展示名（通常是导入时的原文件名，去掉扩展名后作为 AI 引用名）。</summary>
    public string Name { get; init; } = "";
    /// <summary>库内绝对路径。</summary>
    public string Path { get; init; } = "";
    /// <summary>描述：由用户自己填写，或由 AI 识别后填入（两者都行）。</summary>
    public string Description { get; init; } = "";
    public DateTime ImportedAt { get; init; } = DateTime.Now;
}

/// <summary>
/// #12 表情包库：导入图片、管理描述、生成给 AI 的可用表情清单。
/// 描述可以两种方式获得：用户手填；或后续让「识图模型」看一眼后填入（调用方负责识别，本库只存结果）。
///
/// #18 使用范围：库本身不区分使用处 —— 主对话、假微信、QQ/微信官方机器人、
/// 兼容性机器人（NapCat 等）都从同一个库取图，由各通道决定发图还是文字降级。
/// </summary>
public sealed class StickerLibrary
{
    private readonly StorageProvider _store;
    private readonly string _dir;
    private List<StickerItem> _items = new();
    private bool _loaded;
    private readonly object _lock = new();

    /// <summary>库内容变化（导入/删除/改名）时触发，供各页面刷新列表。</summary>
    public event Action? Changed;

    public StickerLibrary(StorageProvider store)
    {
        _store = store;
        _dir = Path.Combine(store.Root, "stickers");
    }

    /// <summary>当前库内容（可能尚未加载，调用 EnsureLoadedAsync 后再取）。</summary>
    public IReadOnlyList<StickerItem> Items
    {
        get { lock (_lock) return _items.ToList(); }
    }

    public async Task EnsureLoadedAsync()
    {
        if (_loaded) return;
        try
        {
            var list = await _store.Load<List<StickerItem>>("stickers");
            lock (_lock)
            {
                _items = list ?? new List<StickerItem>();
                _loaded = true;
            }
        }
        catch (Exception ex)
        {
            App.WriteLog("StickerLibrary.Load -> " + ex.Message);
            lock (_lock) _loaded = true;
        }
    }

    /// <summary>
    /// 导入一个表情图片：文件复制进库目录，登记条目（可带初始描述）。
    /// 同名（同名不同图）也允许共存——以 Id 区分，Name 用于 AI 引用（重名时以后者为先）。
    /// </summary>
    public async Task<StickerItem?> ImportAsync(string sourceFile, string description = "")
    {
        if (string.IsNullOrWhiteSpace(sourceFile) || !File.Exists(sourceFile)) return null;
        try
        {
            Directory.CreateDirectory(_dir);
            var ext = Path.GetExtension(sourceFile);
            var id = Guid.NewGuid().ToString("N")[..10];
            var name = Path.GetFileNameWithoutExtension(sourceFile);
            if (string.IsNullOrWhiteSpace(name)) name = "sticker";
            var target = Path.Combine(_dir, $"{id}{ext}");
            File.Copy(sourceFile, target, overwrite: true);

            var item = new StickerItem
            {
                Id = id,
                Name = name,
                Path = target,
                Description = description ?? ""
            };
            await EnsureLoadedAsync();
            lock (_lock) _items.Add(item);
            await _store.Save("stickers", _items);
            Changed?.Invoke();
            return item;
        }
        catch (Exception ex)
        {
            App.WriteLog("StickerLibrary.Import -> " + ex.Message);
            return null;
        }
    }

    /// <summary>
    /// 登记一个「库外」的表情文件（不复制、不接管生命周期）：
    /// 社交壳（假微信/QQ）自己的表情目录里导入的文件，也登记进来，
    /// 描述（手填或识图模型 OCR）才能进 BuildContextForAi，AI 才「认得」这张表情。
    /// 同名已存在（库内或旧登记）则只更新描述，不重复建档。
    /// </summary>
    public async Task<StickerItem?> RegisterAsync(string path, string name, string description = "")
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        var displayName = string.IsNullOrWhiteSpace(name)
            ? Path.GetFileNameWithoutExtension(path)
            : name;
        await EnsureLoadedAsync();
        StickerItem? item = null;
        lock (_lock)
        {
            item = _items.FirstOrDefault(i => string.Equals(i.Name, displayName, StringComparison.OrdinalIgnoreCase));
            if (item is null)
            {
                item = new StickerItem
                {
                    Id = "ext_" + Guid.NewGuid().ToString("N")[..10],
                    Name = displayName,
                    Path = path,
                    Description = description ?? ""
                };
                _items.Add(item);
            }
            else if (!string.IsNullOrWhiteSpace(description))
            {
                item = item with { Description = description };
                var idx = _items.FindIndex(i => i.Id == item.Id);
                _items[idx] = item;
            }
        }
        await _store.Save("stickers", _items);
        Changed?.Invoke();
        return item;
    }

    public async Task RemoveAsync(string id)
    {
        await EnsureLoadedAsync();
        StickerItem? gone = null;
        lock (_lock)
        {
            gone = _items.FirstOrDefault(i => i.Id == id);
            if (gone is not null) _items.Remove(gone);
        }
        if (gone is not null)
        {
            try { if (File.Exists(gone.Path)) File.Delete(gone.Path); } catch { }
            await _store.Save("stickers", _items);
            Changed?.Invoke();
        }
    }

    public async Task UpdateDescriptionAsync(string id, string description)
    {
        await EnsureLoadedAsync();
        lock (_lock)
        {
            var idx = _items.FindIndex(i => i.Id == id);
            if (idx < 0) return;
            _items[idx] = _items[idx] with { Description = description ?? "" };
        }
        await _store.Save("stickers", _items);
        Changed?.Invoke();
    }

    /// <summary>按名字（或 Id、或带扩展名的文件名）找表情；找不到返回 null。</summary>
    public StickerItem? Find(string nameOrId)
    {
        if (string.IsNullOrWhiteSpace(nameOrId)) return null;
        var key = nameOrId.Trim();
        var keyNoExt = Path.GetFileNameWithoutExtension(key);
        lock (_lock)
        {
            return _items.FirstOrDefault(i => i.Id == key)
                ?? _items.LastOrDefault(i => string.Equals(i.Name, key, StringComparison.OrdinalIgnoreCase))
                ?? _items.LastOrDefault(i => string.Equals(i.Name, keyNoExt, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>
    /// 生成注入给 AI 的「可用表情包」清单。AI 想发表情时，回复里写 [表情:名字] 即可。
    /// 没有表情时返回空串（不注入，不影响请求）。
    /// </summary>
    public string BuildContextForAi()
    {
        List<StickerItem> list;
        lock (_lock) list = _items.ToList();
        if (list.Count == 0) return "";
        var sb = new System.Text.StringBuilder();
        sb.Append("表情包库（你可以在回复里直接写 [表情:名字] 来发表情，系统会把它渲染成图片；只挑贴切的用，不要滥用）：");
        foreach (var s in list.Take(60))
        {
            sb.Append("\n- ").Append(s.Name);
            if (!string.IsNullOrWhiteSpace(s.Description)) sb.Append("：").Append(s.Description);
        }
        return sb.ToString();
    }

    /// <summary>
    /// 从一段文本里抽出所有 [表情:名字] / 【表情:名字】 标记，返回标记外的剩余文本与名字清单。
    /// 供主对话、假微信、机器人通道统一使用。
    /// </summary>
    public static (string Text, List<string> Stickers) SplitMarkers(string input)
    {
        var names = new List<string>();
        if (string.IsNullOrEmpty(input)) return (input ?? "", names);
        var sb = new System.Text.StringBuilder();
        var i = 0;
        while (i < input.Length)
        {
            var m = FindMarker(input, i);
            if (m is var (start, end, name) && start >= 0)
            {
                sb.Append(input, i, start - i);
                names.Add(name);
                i = end;
            }
            else
            {
                sb.Append(input, i, input.Length - i);
                break;
            }
        }
        return (sb.ToString(), names);
    }

    /// <summary>在 input 的 from 位置起找下一个表情标记，返回 (起始,结束,名字)；没有则 start=-1。</summary>
    private static (int Start, int End, string Name) FindMarker(string input, int from)
    {
        var idx = input.IndexOf("[表情:", from, StringComparison.Ordinal);
        var idx2 = input.IndexOf("【表情:", from, StringComparison.Ordinal);
        var idx3 = input.IndexOf("[表情：", from, StringComparison.Ordinal);
        var idx4 = input.IndexOf("【表情：", from, StringComparison.Ordinal);
        var best = new[] { idx, idx2, idx3, idx4 }.Where(v => v >= 0).DefaultIfEmpty(-1).Min();
        if (best < 0) return (-1, -1, "");
        var closeIdx = input.IndexOfAny(new[] { ']', '】' }, best);
        if (closeIdx < 0) return (-1, -1, "");
        var nameStart = input.IndexOfAny(new[] { ':', '：' }, best);
        if (nameStart < 0 || nameStart > closeIdx) return (-1, -1, "");
        var name = input.Substring(nameStart + 1, closeIdx - nameStart - 1).Trim();
        return (best, closeIdx + 1, name);
    }

    /// <summary>
    /// #18：把文本里的表情标记换成「文字降级」表述（通道不支持发图时使用）。
    /// 返回处理后的文本；标记里的名字被保留为「（表情：名字）」。
    /// </summary>
    public static string ToPlainFallback(string input)
    {
        var (text, stickers) = SplitMarkers(input);
        if (stickers.Count == 0) return text;
        var suffix = string.Join("", stickers.Select(n => $"（表情：{n}）"));
        return text.Trim() + suffix;
    }
}
