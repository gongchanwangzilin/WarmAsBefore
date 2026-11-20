using System.Text.Json;
using WarmAsBefore.Models;

namespace WarmAsBefore.Services;

/// <summary>某个角色当前的大脑档位 + 专注训练数据。</summary>
public sealed record CognitionProfile
{
    public CognitionMode Mode { get; set; } = CognitionMode.Wandering;
    public FocusTraining Training { get; set; } = new();
}

/// <summary>按角色 id 分簿的认知状态（落盘到 scope.Key("cognition_mode")）。</summary>
public sealed record CognitionBook
{
    public Dictionary<string, CognitionProfile> ByCharacter { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>
/// v1.65 契约 A 节：专注 / 神游双系统大脑状态。
///
/// 神游（Wandering）：低精度、无专属精力消耗、直接用总精力；负责反刍 / 整理 / 纠错 / 新想法 / 回忆连线。
/// 专注（Focused）：高精度交给大模型、高精力消耗（主 Agent 在 ChatEngine 扣总精力）+ 专属精力 Reserve 消耗。
///
/// 依赖不注入：沿用 OrganSimulationService 的静态 Instance + 服务定位器（StorageProvider / MemoryScope /
/// HumanMemoryService 都是 DI 单例），因此无需改 DI 注册即可用。全程 try/catch，任何失败只降级为安全默认值，
/// 绝不抛异常给调用方；数值钳制与半衰期回归全在 CognitionRules（零 IO、可单测）。
/// </summary>
public sealed class CognitionModeService
{
    /// <summary>全局唯一实例（无参构造；依赖在首次读取时通过服务定位器解析）。</summary>
    public static CognitionModeService Instance { get; } = new();

    private CognitionModeService() { }

    /// <summary>认知档位/训练数据发生变化时触发（参数是角色 id）。监测页/提示词出口订阅后刷新。</summary>
    public event Action<string>? Changed;

    private readonly object _gate = new();
    private readonly object _rngGate = new();
    private readonly Random _rng = new();
    private CognitionBook _book = new();
    private bool _loaded;
    private bool _scopeHooked;
    private StorageProvider? _store;
    private MemoryScope? _scope;

    // 每次 Tick 的基准时刻（按角色）；不落盘，首帧只记基准不算 dt，避免换档/重启后的大跳变。
    private readonly Dictionary<string, DateTime> _lastTicks = new(StringComparer.Ordinal);
    // 上次对外广播的签名，避免 Reserve 每帧微调都触发 Changed。
    private readonly Dictionary<string, string> _lastSig = new(StringComparer.Ordinal);

    private string Key => _scope?.Key("cognition_mode") ?? "cognition_mode";

    // ---- 依赖获取：照抄 OrganSimulationService / ServicesHelper.GetServices() 的服务定位链 ----

    private static IServiceProvider? Services()
    {
        try
        {
            return Microsoft.Maui.IPlatformApplication.Current?.Services
                ?? Microsoft.Maui.Controls.Application.Current?.Handler?.MauiContext?.Services;
        }
        catch { return null; }
    }

    private static T? Resolve<T>() where T : class
    {
        try { return Services()?.GetService(typeof(T)) as T; }
        catch { return null; }
    }

    /// <summary>首次使用时解析依赖、订阅作用域变更、同步读盘（用 LoadRawBlocking 同步取，因为对外 API 全是同步的）。</summary>
    private void EnsureLoaded()
    {
        try
        {
            _store ??= Resolve<StorageProvider>();
            _scope ??= Resolve<MemoryScope>();
        }
        catch (Exception ex) { App.WriteLog("Cognition.Resolve -> " + ex.Message); }

        if (_scope is not null && !_scopeHooked)
        {
            try { _scope.Changed += ReleaseCache; }
            catch (Exception ex) { App.WriteLog("Cognition.Hook -> " + ex.Message); }
            _scopeHooked = true;
        }

        var store = _store;
        if (store is null) return; // 宿主还没起来：保持内存默认值，下次调用再试。
        lock (_gate)
        {
            if (_loaded) return;
            try
            {
                var raw = store.LoadRawBlocking(Key);
                _book = string.IsNullOrWhiteSpace(raw)
                    ? new CognitionBook()
                    : JsonSerializer.Deserialize<CognitionBook>(raw) ?? new CognitionBook();
                _book.ByCharacter ??= new Dictionary<string, CognitionProfile>(StringComparer.Ordinal);
            }
            catch (Exception ex)
            {
                App.WriteLog("Cognition.Load -> " + ex.Message);
                _book = new CognitionBook();
            }
            _loaded = true;
        }
    }

    /// <summary>作用域换了（读档/新开局）：丢缓存重读。</summary>
    public void ReleaseCache()
    {
        lock (_gate)
        {
            _book = new CognitionBook();
            _loaded = false;
            _lastTicks.Clear();
            _lastSig.Clear();
        }
    }

    private CognitionProfile StateOf(string charId)
    {
        if (!_book.ByCharacter.TryGetValue(charId, out var p))
        {
            p = new CognitionProfile();
            _book.ByCharacter[charId] = p;
        }
        p.Training ??= new FocusTraining();
        // 版本兼容 + 稳态：缺字段/越界/NaN 一律钳回合法区间。
        var t = p.Training;
        t.Capacity = CognitionRules.CapacityOf(t.Capacity);
        t.Reserve = CognitionRules.ReserveOf(t.Reserve, t.Capacity);
        if (t.Sessions < 0) t.Sessions = 0;
        if (t.LastFocusAt == default) t.LastFocusAt = DateTime.UtcNow;
        return p;
    }

    // ---- 对外只读 API ----

    public CognitionMode Current(string charId)
    {
        if (string.IsNullOrEmpty(charId)) return CognitionMode.Wandering;
        try { EnsureLoaded(); lock (_gate) return StateOf(charId).Mode; }
        catch { return CognitionMode.Wandering; }
    }

    public double FocusReserve(string charId)
    {
        if (string.IsNullOrEmpty(charId)) return 0;
        try
        {
            EnsureLoaded();
            lock (_gate)
            {
                var t = StateOf(charId).Training;
                return CognitionRules.ReserveOf(t.Reserve, t.Capacity);
            }
        }
        catch { return 0; }
    }

    public double Capacity(string charId)
    {
        if (string.IsNullOrEmpty(charId)) return CognitionRules.DefaultCapacity;
        try
        {
            EnsureLoaded();
            lock (_gate) return CognitionRules.CapacityOf(StateOf(charId).Training.Capacity);
        }
        catch { return CognitionRules.DefaultCapacity; }
    }

    /// <summary>专注中 Reserve 低于 25% 但仍在撑 → 神游该介入。</summary>
    public bool ShouldWanderIntervene(string charId)
    {
        if (string.IsNullOrEmpty(charId)) return false;
        try
        {
            EnsureLoaded();
            lock (_gate)
            {
                var p = StateOf(charId);
                return CognitionRules.ShouldIntervene(p.Mode, p.Training.Reserve, p.Training.Capacity);
            }
        }
        catch { return false; }
    }

    // ---- 对外变更 API ----

    /// <summary>
    /// 进入专注：多巴胺越高进入越快（返回 enteringSpeed 0..1），扣一次性专属精力 Reserve 并记一次 Session。
    /// 总精力的高消耗由主 Agent 在 ChatEngine 侧扣，这里只管专属 Reserve。
    /// </summary>
    public double EnterFocus(string charId, double dopamine)
    {
        var speed = CognitionRules.EnteringSpeedOf(double.IsNaN(dopamine) ? 0 : dopamine);
        if (string.IsNullOrEmpty(charId)) return speed;
        try
        {
            EnsureLoaded();
            lock (_gate)
            {
                var p = StateOf(charId);
                var t = p.Training;
                t.Reserve = CognitionRules.ReserveOf(t.Reserve - CognitionRules.EnterReserveCost, t.Capacity);
                t.Sessions = Math.Max(0, t.Sessions) + 1;
                t.LastFocusAt = DateTime.UtcNow;
                p.Mode = CognitionMode.Focused;
                _lastTicks.Remove(charId); // 下一次 Tick 只记基准，不把切档前的时间算进专注消耗。
            }
            RaiseIfChanged(charId);
            QueuePersist();
        }
        catch (Exception ex) { App.WriteLog("Cognition.EnterFocus -> " + ex.Message); }
        return speed;
    }

    /// <summary>
    /// 游戏时钟跳动：专注时 Reserve 按时间衰减，&lt;=0 强制回退 Wandering（并顺带练一次上限）；
    /// 神游时 Reserve 按半衰期回满。dt 最多结算 6 小时，防换档/重启跳变。
    /// </summary>
    public void Tick(string charId, DateTime gameTime)
    {
        if (string.IsNullOrEmpty(charId)) return;
        try
        {
            EnsureLoaded();
            double minutes;
            lock (_gate)
            {
                if (!_lastTicks.TryGetValue(charId, out var last))
                {
                    _lastTicks[charId] = gameTime;
                    return; // 首帧只记基准。
                }
                _lastTicks[charId] = gameTime;
                minutes = (gameTime - last).TotalMinutes;
            }
            if (double.IsNaN(minutes) || minutes <= 0) return;
            if (minutes > 360) minutes = 360;

            lock (_gate)
            {
                var p = StateOf(charId);
                var t = p.Training;
                if (p.Mode == CognitionMode.Focused)
                {
                    t.Reserve = CognitionRules.DrainDuringFocus(t.Reserve, t.Capacity, minutes);
                    if (t.Reserve <= 0)
                    {
                        t.Reserve = 0;
                        p.Mode = CognitionMode.Wandering; // 专属精力耗尽，强制回退。
                        // 撑完一整段专注就是一次训练：上限涨一点，Reserve 跟着补一点。
                        t.Capacity = CognitionRules.Train(t.Capacity);
                        t.Reserve = CognitionRules.ReserveOf(t.Reserve + CognitionRules.CapacityTrainStep, t.Capacity);
                    }
                }
                else
                {
                    t.Reserve = CognitionRules.RestToward(t.Reserve, t.Capacity, minutes / 60.0);
                }
            }
            RaiseIfChanged(charId);
            QueuePersist();
        }
        catch (Exception ex) { App.WriteLog("Cognition.Tick -> " + ex.Message); }
    }

    /// <summary>用得越多上限越大（封顶 200），并补一点专属精力。供主 Agent 在专注会话结束时调用。</summary>
    public void TrainCapacity(string charId)
    {
        if (string.IsNullOrEmpty(charId)) return;
        try
        {
            EnsureLoaded();
            lock (_gate)
            {
                var t = StateOf(charId).Training;
                t.Capacity = CognitionRules.Train(t.Capacity);
                t.Reserve = CognitionRules.ReserveOf(t.Reserve + CognitionRules.CapacityTrainStep, t.Capacity);
            }
            RaiseIfChanged(charId);
            QueuePersist();
        }
        catch (Exception ex) { App.WriteLog("Cognition.TrainCapacity -> " + ex.Message); }
    }

    /// <summary>
    /// 神游介入：从 HumanMemoryService 随机取一条旧记忆文本，供主 Agent 注入 prompt（反刍/回忆连线）。
    /// 核心记忆优先，没有就退回全部记忆；同步 API 内部把异步读取放到线程池执行，避免 UI 上下文死锁。
    /// 取不到返回 null（主 Agent 应容忍），永不抛异常。
    /// </summary>
    public string? WanderInject(string charId)
    {
        if (string.IsNullOrEmpty(charId)) return null;
        try
        {
            var svc = Resolve<HumanMemoryService>();
            if (svc is null) return null;

            var pool = Task.Run(() => svc.CoreAsync(charId)).GetAwaiter().GetResult();
            if (pool is null || pool.Count == 0)
                pool = Task.Run(() => svc.AllAsync(charId)).GetAwaiter().GetResult();
            if (pool is null || pool.Count == 0) return null;

            var candidates = pool.Where(m => !string.IsNullOrWhiteSpace(m.Text)).ToList();
            if (candidates.Count == 0) return null;
            HumanMemoryEntry pick;
            lock (_rngGate) pick = candidates[_rng.Next(candidates.Count)];
            return pick.Text.Trim();
        }
        catch (Exception ex)
        {
            App.WriteLog("Cognition.WanderInject -> " + ex.Message);
            return null;
        }
    }

    // ---- 事件 + 落盘 ----

    private void RaiseIfChanged(string charId)
    {
        string sig;
        lock (_gate)
        {
            var p = StateOf(charId);
            var t = p.Training;
            sig = $"{p.Mode}|{(int)t.Reserve}|{(int)t.Capacity}|{t.Sessions}";
            if (_lastSig.TryGetValue(charId, out var prev) && prev == sig) return;
            _lastSig[charId] = sig;
        }
        try { Changed?.Invoke(charId); }
        catch (Exception ex) { App.WriteLog("Cognition.Changed -> " + ex.Message); }
    }

    private bool _saving;
    private bool _dirty;

    /// <summary>改动后排队落盘：同一时刻只写一次，写入期间的新改动合并到下一轮，不阻塞调用线程。</summary>
    private void QueuePersist()
    {
        lock (_gate)
        {
            if (_saving) { _dirty = true; return; }
            _saving = true;
        }
        _ = Task.Run(async () =>
        {
            try { await PersistOnceAsync(); }
            catch (Exception ex) { App.WriteLog("Cognition.Persist -> " + ex.Message); }
            finally
            {
                bool again;
                lock (_gate) { again = _dirty; _dirty = false; _saving = false; }
                if (again) QueuePersist();
            }
        });
    }

    private async Task PersistOnceAsync()
    {
        var store = _store;
        if (store is null) return;
        CognitionBook snap;
        lock (_gate)
        {
            if (!_loaded) return;
            snap = new CognitionBook
            {
                ByCharacter = _book.ByCharacter.ToDictionary(
                    kv => kv.Key,
                    kv => new CognitionProfile
                    {
                        Mode = kv.Value.Mode,
                        Training = new FocusTraining
                        {
                            Reserve = kv.Value.Training.Reserve,
                            Capacity = kv.Value.Training.Capacity,
                            Sessions = kv.Value.Training.Sessions,
                            LastFocusAt = kv.Value.Training.LastFocusAt
                        }
                    },
                    StringComparer.Ordinal)
            };
        }
        try { await store.Save(Key, snap); }
        catch (Exception ex) { App.WriteLog("Cognition.Save -> " + ex.Message); }
    }

    /// <summary>给 ChatEngine / 监测页读的结构化快照（匿名对象，字段稳定）。</summary>
    public object Snapshot(string charId)
    {
        try
        {
            EnsureLoaded();
            CognitionProfile p;
            lock (_gate) p = StateOf(charId);
            var t = p.Training;
            var cap = CognitionRules.CapacityOf(t.Capacity);
            var reserve = CognitionRules.ReserveOf(t.Reserve, cap);
            return new
            {
                characterId = charId,
                mode = p.Mode.ToString(),
                isFocused = p.Mode == CognitionMode.Focused,
                reserve = Math.Round(reserve, 1),
                capacity = Math.Round(cap, 1),
                ratio = cap > 0 ? Math.Round(reserve / cap, 3) : 0d,
                sessions = Math.Max(0, t.Sessions),
                lastFocusAt = t.LastFocusAt,
                shouldWanderIntervene = CognitionRules.ShouldIntervene(p.Mode, reserve, cap),
                updatedAt = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            App.WriteLog("Cognition.Snapshot -> " + ex.Message);
            return new
            {
                characterId = charId,
                mode = CognitionMode.Wandering.ToString(),
                isFocused = false,
                reserve = 0d,
                capacity = CognitionRules.DefaultCapacity,
                ratio = 0d,
                sessions = 0,
                lastFocusAt = DateTime.MinValue,
                shouldWanderIntervene = false,
                updatedAt = DateTime.UtcNow
            };
        }
    }
}
