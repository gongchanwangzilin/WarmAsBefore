using System.Text.Json;
using WarmAsBefore.Models;

namespace WarmAsBefore.Services;

/// <summary>一个角色当前结算到哪一步：精力值 + 上次结算时刻。</summary>
public sealed record EnergyLedgerState
{
    public double Energy { get; set; } = 100;
    public DateTime LastSettledAt { get; set; } = DateTime.UtcNow;
}

public sealed record EnergyBook
{
    public Dictionary<string, EnergyLedgerState> ByCharacter { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>
/// F16 精力台账：把「睡眠 / 干的活 / 思考的内容 / 说话」记成有界的纯代码状态，
/// 结算规则全在 <see cref="EnergyRules"/>（零 IO、零模型、可单测）。
///
/// 放在宿主：模组只通过 stats 帧拿到结果、只显示，不参与结算。
/// 接线：ChatEngine 每轮对话记说话与思考档位；MaidBridgeService 记劳作与主人睡眠；
/// MainGameViewModel.UpdateEnergy 每帧调 <see cref="Settle"/> 取权威值。
///
/// 存档按 MemoryScope 隔离；旧档没有这份数据时从满值起步，不影响既有存档。
/// </summary>
public sealed class EnergyLedgerService
{
    private readonly StorageProvider _store;
    private readonly MemoryScope _scope;
    private readonly object _gate = new();
    private EnergyBook _book = new();
    private bool _loaded;
    private bool _saving;

    public EnergyLedgerService(StorageProvider store, MemoryScope scope)
    {
        _store = store;
        _scope = scope;
        scope.Changed += ReleaseCache;
    }

    private string Key => _scope.Key("energy_ledger");

    /// <summary>同步首次加载：UpdateEnergy 在时钟跳动里同步取数，不能 await。
    /// StorageProvider.LoadRawBlocking 就是给启动路径用的同步读取。</summary>
    private void EnsureLoaded()
    {
        if (_loaded) return;
        lock (_gate)
        {
            if (_loaded) return;
            try
            {
                var raw = _store.LoadRawBlocking(Key);
                _book = string.IsNullOrWhiteSpace(raw)
                    ? new EnergyBook()
                    : JsonSerializer.Deserialize<EnergyBook>(raw) ?? new EnergyBook();
                _book.ByCharacter ??= new Dictionary<string, EnergyLedgerState>(StringComparer.Ordinal);
            }
            catch (Exception ex)
            {
                App.WriteLog("EnergyLedger.Load -> " + ex.Message);
                _book = new EnergyBook();
            }
            _loaded = true;
        }
    }

    /// <summary>作用域换了（读档/新开局）：丢缓存重读。</summary>
    public void ReleaseCache()
    {
        lock (_gate) { _book = new EnergyBook(); _loaded = false; }
    }

    private EnergyLedgerState StateOf(string charId)
    {
        if (!_book.ByCharacter.TryGetValue(charId, out var s))
        {
            s = new EnergyLedgerState();
            _book.ByCharacter[charId] = s;
        }
        s.LastSettledAt = s.LastSettledAt == default ? DateTime.UtcNow : s.LastSettledAt;
        return s;
    }

    private async Task SaveAsync()
    {
        if (_saving) return;
        EnergyBook snap;
        lock (_gate)
        {
            if (!_loaded) return;
            _saving = true;
            snap = new EnergyBook
            {
                ByCharacter = _book.ByCharacter.ToDictionary(
                    kv => kv.Key,
                    kv => new EnergyLedgerState { Energy = kv.Value.Energy, LastSettledAt = kv.Value.LastSettledAt },
                    StringComparer.Ordinal)
            };
        }
        try { await _store.Save(Key, snap); }
        catch (Exception ex) { App.WriteLog("EnergyLedger.Save -> " + ex.Message); }
        finally { lock (_gate) _saving = false; }
    }

    /// <summary>
    /// 取此刻权威精力（0~100）：按距上次结算的时间自然休息回落，再叠加 AI 修正。
    /// 每帧调用也只在内存里算，不落盘（落盘由事件驱动的 Note* 负责）。
    /// </summary>
    public int Settle(string charId, DateTime now, int aiOffset = 0)
    {
        if (string.IsNullOrEmpty(charId)) return 100;
        EnsureLoaded();
        lock (_gate)
        {
            var s = StateOf(charId);
            var hours = Math.Max(0, (now - s.LastSettledAt).TotalHours);
            // 长时间未上线不该一次回到满：最多按 48 小时结算，防「睡一觉满血」的突兀跳变。
            if (hours > 48) hours = 48;
            s.Energy = EnergyRules.RestToward(s.Energy, hours);
            s.LastSettledAt = now;
            return (int)Math.Round(EnergyRules.Clamp(s.Energy + aiOffset, 0, 100));
        }
    }

    /// <summary>一轮对话：说话扣很小值，思考按档位/深度扣。后台落盘，不阻塞回复。</summary>
    public void NoteTalk(string charId, int thinkTier, bool deepThink)
    {
        if (string.IsNullOrEmpty(charId)) return;
        _ = Task.Run(async () =>
        {
            EnsureLoaded();
            lock (_gate)
            {
                var s = StateOf(charId);
                s.Energy = EnergyRules.Clamp(s.Energy
                    - EnergyRules.TalkCost(1)
                    - EnergyRules.ThinkCost(thinkTier, deepThink), 0, 100);
            }
            await SaveAsync();
        });
    }

    /// <summary>一次劳作/游戏事件：按 Job 强度与时长扣。</summary>
    public void NoteWork(string charId, string? kind, double hours = 0.1)
    {
        if (string.IsNullOrEmpty(charId)) return;
        _ = Task.Run(async () =>
        {
            EnsureLoaded();
            lock (_gate)
            {
                var s = StateOf(charId);
                s.Energy = EnergyRules.Clamp(s.Energy - EnergyRules.WorkCost(kind, hours), 0, 100);
            }
            await SaveAsync();
        });
    }

    /// <summary>睡了一觉（含 F05 抱睡）：按小时恢复，是唯一的大额回血。</summary>
    public void NoteSleep(string charId, double hours)
    {
        if (string.IsNullOrEmpty(charId) || hours <= 0) return;
        _ = Task.Run(async () =>
        {
            EnsureLoaded();
            lock (_gate)
            {
                var s = StateOf(charId);
                s.Energy = EnergyRules.Clamp(s.Energy + EnergyRules.SleepRecovery(hours), 0, 100);
            }
            await SaveAsync();
        });
    }

    /// <summary>给工具/面板读的快照。</summary>
    public object Snapshot(string charId)
    {
        EnsureLoaded();
        lock (_gate)
        {
            var s = StateOf(charId);
            return new { enabled = true, energy = Math.Round(s.Energy, 1), updatedAt = s.LastSettledAt };
        }
    }
}
