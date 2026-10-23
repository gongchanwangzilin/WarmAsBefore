namespace WarmAsBefore.Services;

/// <summary>
/// 记忆作用域：同一个人设在两个存档里，记得的事不该是同一份。
///
/// 默认按存档隔离（落到 <c>human_memories/&lt;存档号&gt;.json</c>）。用户在设置里打开
/// 「跨存档共用记忆」后回到 <c>human_memories.json</c> —— 那正是这条规则之前唯一的文件名，
/// 所以老数据一个字都不用搬；还没进任何存档时（标题页、角色库）也落回这一份，
/// 因为那里根本没有「本存档」可谈。
///
/// 换档、新开局、开关切换之后必须调 <see cref="Raise"/>：各记忆服务是带缓存的单例，
/// 不丢缓存就会把上一个存档的记忆写进新存档的文件里。
/// </summary>
public sealed class MemoryScope
{
    /// <summary>共用作用域的代号（同时也是旧的扁平文件名）。</summary>
    public const string Shared = "shared";

    private readonly GameEngine _engine;
    private readonly SettingsManager _settings;
    private string _last = "";

    public MemoryScope(GameEngine engine, SettingsManager settings)
    {
        _engine = engine;
        _settings = settings;
        _settings.Applied += Raise;
    }

    /// <summary>作用域变了：订阅方清掉缓存重读盘。</summary>
    public event Action? Changed;

    /// <summary>跨存档共用是否打开。</summary>
    public bool CrossSave => _settings.Current.CrossSaveMemory;

    /// <summary>当前作用域代号：存档号，或 <see cref="Shared"/>。</summary>
    public string Current => CrossSave || string.IsNullOrEmpty(_engine.CurrentSaveId)
        ? Shared
        : _engine.CurrentSaveId;

    /// <summary>是不是所有存档共用一份（界面要照着这个说人话）。</summary>
    public bool IsShared => Current == Shared;

    /// <summary>界面上一句人话：这份记忆到底归谁。三处要写同一个意思，所以只在这里说一次。</summary>
    public string Label => IsShared ? "所有存档共用" : "只在这份存档里";

    /// <summary>某份数据在当前作用域下的存储键。共用时保持原来的扁平键，省掉一次迁移。</summary>
    public string Key(string store) => IsShared ? store : $"{store}/{Current}";

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
        App.WriteLog($"MemoryScope: 记忆作用域 → {(now == Shared ? "跨存档共用" : "存档 " + now)}");
        Changed?.Invoke();
    }
}
