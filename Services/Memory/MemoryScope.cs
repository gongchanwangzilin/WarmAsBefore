namespace WarmAsBefore.Services;

/// <summary>
/// 记忆作用域：同一个人设在两个存档里，记得的事不该是同一份。
///
/// v1.65 起「按存档隔离」是唯一规则：进档后一律落到 human_memories/&lt;存档号&gt;.json 这类
/// 存档作用域文件。旧的「跨存档共用记忆」开关（UserSettings.CrossSaveMemory）已废弃，
/// 不再参与作用域计算；CharacterData.StateShared / WorldviewCrossSave 也不再由这里读取。
/// 还没进任何存档时（标题页、角色库）才落回扁平文件 —— 那里根本没有「本存档」可谈。
///
/// 老数据不丢：首次为某存档请求某份数据时，若旧的扁平共享文件在、而该档作用域文件不在，
/// 复制一份进该档作用域（只复制、绝不删原文件，别的档还能各自再迁一份）。
///
/// 换档、新开局之后必须调 <see cref="Raise"/>：各记忆服务是带缓存的单例，
/// 不丢缓存就会把上一个存档的记忆写进新存档的文件里。
/// </summary>
public sealed class MemoryScope
{
    /// <summary>未开局态的作用域代号（同时也是旧的扁平文件名）。</summary>
    public const string Shared = "shared";

    private readonly GameEngine _engine;
    private readonly SettingsManager _settings;
    private string _last = "";

    // 已处理过迁移的「store/存档号」组合：迁移只做一次，不随每次 Key() 重复查盘。
    private readonly HashSet<string> _migrated = new(StringComparer.Ordinal);
    private readonly object _migrateLock = new();

    public MemoryScope(GameEngine engine, SettingsManager settings)
    {
        _engine = engine;
        _settings = settings;
        _settings.Applied += Raise;
    }

    /// <summary>作用域变了：订阅方清掉缓存重读盘。</summary>
    public event Action? Changed;

    /// <summary>
    /// 【已废弃，恒 false】旧开关 UserSettings.CrossSaveMemory 不再作为作用域开关，
    /// 记忆永远按存档隔离。保留此属性只为兼容可能的外部读取。
    /// </summary>
    public bool CrossSave => false;

    /// <summary>
    /// 当前作用域代号：有存档号就是存档号；没有存档号（标题页/角色库）保留 <see cref="Shared"/> 仅作未开局态。
    /// 不再看任何「跨存档共用」开关。
    /// </summary>
    public string Current => string.IsNullOrEmpty(_engine.CurrentSaveId)
        ? Shared
        : _engine.CurrentSaveId;

    /// <summary>是不是尚未进入任何存档（未开局态）。</summary>
    public bool IsShared => Current == Shared;

    /// <summary>界面上一句人话：这份记忆到底归谁。三处要写同一个意思，所以只在这里说一次。</summary>
    public string Label => IsShared ? "所有存档共用" : "只在这份存档里";

    /// <summary>
    /// 某份数据在当前作用域下的存储键。未开局时保持原来的扁平键；进档后用 {store}/{存档号}，
    /// 并在首次请求时把老扁平文件复制进该档作用域（老用户记忆不丢，只复制不删原件）。
    /// </summary>
    public string Key(string store)
    {
        if (IsShared) return store;
        var scoped = $"{store}/{Current}";
        MigrateFlatIfNeeded(store, scoped);
        return scoped;
    }

    /// <summary>
    /// 老版本兼容：v1.65 之前记忆写在扁平文件（human_memories.json 等）里、跨档共用。
    /// 首次为某存档请求某份数据时，若扁平文件在、而该档作用域文件不在，复制一份过去；
    /// 原文件一律保留（别的档还能各自再迁一份）。全程 try/catch，失败不影响正常读写。
    /// </summary>
    private void MigrateFlatIfNeeded(string store, string scopedKey)
    {
        lock (_migrateLock)
        {
            if (_migrated.Contains(scopedKey)) return;
            try
            {
                var root = App.RootDirectory;
                var flat = Path.Combine(root, store.Replace('/', Path.DirectorySeparatorChar) + ".json");
                var scoped = Path.Combine(root, scopedKey.Replace('/', Path.DirectorySeparatorChar) + ".json");
                if (File.Exists(flat) && !File.Exists(scoped))
                {
                    var dir = Path.GetDirectoryName(scoped);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    File.Copy(flat, scoped, overwrite: false);   // 只复制，绝不删老文件
                    App.WriteLog($"MemoryScope: 迁移老共享记忆 {store} → {scopedKey}（原件保留）");
                }
                _migrated.Add(scopedKey);
            }
            catch (Exception ex)
            {
                App.WriteLog("MemoryScope.MigrateFlat -> " + ex.Message);
            }
        }
    }

    /// <summary>
    /// 盘被旁人改过了（克隆角色往同一份文件里追加了一批数据）：让各记忆服务丢缓存重读。
    ///
    /// 和 <see cref="Raise"/> 的区别是不问作用域有没有变 —— 这里作用域恰恰没变，
    /// 但服务们手里那份缓存已经旧了：不丢缓存，下一次它们各自的 Persist 就会把刚写进去的副本抹掉。
    /// </summary>
    public void InvalidateCaches() => Changed?.Invoke();

    /// <summary>读档 / 新开局 / 改开关之后调用；作用域没变就什么都不做。</summary>
    public void Raise()
    {
        var now = Current;
        if (now == _last) return;
        _last = now;
        App.WriteLog("MemoryScope: 记忆作用域 → " + (now == Shared ? "未开局（扁平）" : "存档 " + now));
        Changed?.Invoke();
    }
}
