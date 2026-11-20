using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using WarmAsBefore.Models;
using WarmAsBefore.Modules.Screen;
using WarmAsBefore.Services;

namespace WarmAsBefore.Modules.SaveSystem;

public sealed class SaveManager
{
    private readonly StorageProvider _store;
    private readonly GameEngine _engine;
    private readonly CharacterLibrary _library;
    private readonly MemoryScope _scope;
    private readonly PluginHost _plugins;

    static SaveManager()
    {
        // CPU 并行读档多段解析的饥饿防护：5 段 Task.Run 并行，保证线程池有足够工作线程，
        // 避免读档线程被其它任务挤到排队而拖慢「读取」进度（也避免并行退化成串行）。
        try { System.Threading.ThreadPool.SetMinThreads(8, 8); } catch { }
    }

    public SaveManager(StorageProvider store, GameEngine engine, CharacterLibrary library, MemoryScope scope,
        PluginHost plugins)
    {
        _store = store;
        _engine = engine;
        _library = library;
        _scope = scope;
        _plugins = plugins;
    }

    /// <summary>换档后刷新桌宠形象。由 RuntimeConfigurator 注入（替代原先的 PetService 静态调用）。</summary>
    public Func<Task>? RefreshPetForActiveSave { get; set; }

    /// <summary>当前会话的聊天记录：随每次保存写入存档，读档后恢复聊天界面。</summary>
    public List<ChatRecord> ChatLog { get; } = new();

    /// <summary>
    /// 上一次 List() 时发现读不出来的存档文件（审计 U4）。
    /// 损坏的存档不能再静默从列表里消失——对用户来说「存档没了」比「存档坏了」更伤信任。
    /// </summary>
    public IReadOnlyList<string> UnreadableSaves => _unreadableSaves;
    private readonly List<string> _unreadableSaves = new();

    /// <summary>存档结构版本：旧档（缺此字段 = 0）在加载时自动迁移修复。
    /// v2（v1.65）：新增分支血缘字段，并把旧的跨存档共享角色状态定格进各档 char_states。</summary>
    public const int SchemaVersion = 2;

    /// <summary>上一次读档时的自动修复说明（空 = 无需修复）。</summary>
    public string LastLoadNote { get; private set; } = "";

    /// <summary>上一次保存失败的真实原因（空 = 成功或还没保存过）。别再把异常吞成一句「写不进去」。</summary>
    public string LastCommitError { get; private set; } = "";

    /// <summary>新开一局：分配新的存档槽位（同一角色可开多局，各自成档）。</summary>
    public string NewRun()
    {
        _engine.CurrentSaveId = Guid.NewGuid().ToString("N")[..12];
        ChatLog.Clear();
        _scope.Raise();      // 新的一局 = 新的一份记忆，不能接着写上一个存档的记忆
        return _engine.CurrentSaveId;
    }

    /// <summary>保存当前进度：写入当前槽位（不产生新档）；无当前槽位时新建。</summary>
    public async Task<bool> Commit(string label, string mode = "para")
    {
        try
        {
            LastCommitError = "";
            if (string.IsNullOrEmpty(_engine.CurrentSaveId)) NewRun();
            // 分支血缘跟着档走：分支档再次保存时不能被覆盖成普通档。
            // 优先走内存索引（List/BranchAsync 已把带血缘的 meta 缓存进去，零额外 IO），
            // 只有索引还没加载时才读盘兜底；读不到一律当普通档。
            _index.TryGetValue(_engine.CurrentSaveId, out var prev);
            prev ??= await ReadCurrentSlotAsync(_engine.CurrentSaveId);
            var slot = new SaveSlot
            {
                Id = _engine.CurrentSaveId,
                Label = label,
                Character = _engine.State.CharacterId,
                Scene = _engine.State.Location,
                World = _engine.State.WorldId,
                ParentId = prev?.ParentId,
                BranchName = prev?.BranchName ?? "",
                ForkedAt = prev?.ForkedAt
            };
            var data = new Dictionary<string, object>
            {
                ["state"] = _engine.State,
                ["meta"] = slot,
                ["mode"] = mode,
                ["version"] = SchemaVersion,
                ["char_state"] = _engine.ActiveCharacter?.State,
                // v1.65 起所有角色一律按存档隔离：不再因为 StateShared=true 就不写。
                // 老机制的「好感/信任全局共享」已在读档时定格进各档（见 LoadCore 的 SeedNonShared 分支），
                // 这里把每个角色的当前状态都随存档落盘，A 档好感 80 不会再串到 B 档。
                ["char_states"] = _engine.Roster.Values
                    .ToDictionary(c => c.Profile.Id, c => c.State),
                // 副设定同理：每档一份「此刻之我」，不再区分共享与否。
                ["sub_profiles"] = _engine.Roster.Values
                    .ToDictionary(c => c.Profile.Id, c => c.SubProfile),
                ["chat"] = ChatLog.TakeLast(60).ToList()
            };
            await _store.Save($"saves/{slot.Id}", data);
            // 刚写完这一档：把 meta 直接记进列表缓存。自动存档会反复重写同一个文件，
            // 下一次 List()（角色库/存档页/工作室进场都要）若只按 mtime 判断就会把整份存档再读一遍——
            // 实测这一下就是「读角色 350ms」的大头。
            RememberSlotInListCache(slot);
            return true;
        }
        catch (Exception ex)
        {
            LastCommitError = ex.Message;
            App.WriteLog("SaveManager.Commit -> " + ex);
            return false;
        }
    }

    /// <summary>
    /// 读当前档已有的 meta（只为继承分支血缘）。读不到/损坏一律当普通档处理，绝不阻断保存；
    /// 全程 try/catch —— 保存路径不能被一次额外的读取拖垮。
    /// </summary>
    private async Task<SaveSlot?> ReadCurrentSlotAsync(string id)
    {
        try
        {
            var json = await _store.LoadRawAsync($"saves/{id}");
            if (string.IsNullOrEmpty(json)) return null;
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("meta", out var m) ? m.Deserialize<SaveSlot>() : null;
        }
        catch (Exception ex)
        {
            App.WriteLog("SaveManager.ReadCurrentSlot -> " + ex.Message);
            return null;
        }
    }

    public bool HasImportedNovel()
    {
        return _store.Exists("imports/novel");
    }

    public bool HasImportedCharacter()
    {
        return _store.Exists("imports/character");
    }

    /// <summary>
    /// 读档阶段推进（2026-11-14）：由 SaveViewModel 在 LoadSlot 期间注入，LoadCore 在**真实节点**处触发。
    /// 参数是「当前进入的阶段号 1..5 与阶段名」，绝不按时间片假推进
    /// （docs/ARCHITECTURE.md:378、COMPONENTS.md:605 的「不做假加载」是项目成文禁令）。
    /// 除通知 UI 外，这里同步写 PerfLog.SetPhase（Volatile.Write，不依赖主线程）与
    /// App.TraceStep（同步落盘到 warm_startup.log）—— UI 线程被占死时，
    /// 最后一条 SaveLoad.Phase.* 就是「卡在第几步」的唯一证据。
    /// </summary>
    public Action<int, string>? LoadPhaseHook { get; set; }

    /// <summary>
    /// 解析细步通知（2026-11-15）：忙碌卡的详细信息展开栏用。
    /// 每个解析子步骤完成时带一句人话（「正在解析存档五段…」），让用户看见卡在哪一步、
    /// 是真的在动还是停住了。UI 线程订阅方自行调度。
    /// </summary>
    public Action<string>? LoadDetailHook { get; set; }

    private void MarkLoadPhase(int phase, string name)
    {
        WarmAsBefore.Services.PerfLog.SetPhase($"读档·P{phase}·{name}");
        App.TraceStep($"SaveLoad.Phase.{phase} {name}");
        try { LoadPhaseHook?.Invoke(phase, name); }
        catch (Exception ex) { App.WriteLog("SaveManager.LoadPhaseHook -> " + ex.Message); }
    }

    // ==================== 读档容错（2026-11-14） ====================

    /// <summary>
    /// 上一次读档的结构化修复结果（null = 还没读过档 / 无需报告）。
    /// 保持 <see cref="Load"/> 的 <c>Task&lt;bool&gt;</c> 签名不破坏（调用方 SaveViewModel / MaidBridgeService），
    /// 细节走这个属性。两类结局：<see cref="SaveRepairReport.CanEnter"/> 可修复（修复后自动进入）/
    /// 不可修复（备份 + 隔离，不自动进入、不捏造空档覆盖原件）。
    /// </summary>
    public SaveRepairReport? LastRepairReport { get; private set; }

    /// <summary>
    /// 把一份读不出来的存档隔离：记入 <see cref="_unreadableSaves"/>（坏档隔离区的唯一数据源）
    /// 并清掉它的列表缓存与 meta 索引——不清缓存的话 <see cref="List"/> 命中 <c>_slotCache</c>/<c>_index</c>
    /// 就不再重读本体，磁盘被截断后列表仍显示正常（漏检）。
    /// </summary>
    private void QuarantineSave(string id)
    {
        try
        {
            var path = Path.Combine(_store.Root, "saves", id + ".json");
            if (!_unreadableSaves.Contains(path)) _unreadableSaves.Add(path);
            _slotCache.Remove(path);
            _index.Remove(id);
            _indexDirty = true;
        }
        catch (Exception ex) { App.WriteLog("SaveManager.QuarantineSave -> " + ex.Message); }
    }

    /// <summary>
    /// 集合显式 null 归一化：GameState/SavePermissions 的集合属性是 <c>{ get; init; } = new()</c>，
    /// JSON 里显式 <c>null</c> 会让它们变 null，RepairAsync 的 <c>AllowedCharacters.Count</c> 等处会 NRE。
    /// record 的 with 表达式可以给 init-only 属性赋值，这里就地重建，不动 Models 的可变性。
    /// </summary>
    private static GameState NormalizeCollections(GameState s)
    {
        if (s.Flags is not null && s.CgUnlocked is not null && s.Achievements is not null
            && s.AffectionLog is not null && s.AllowedCharacters is not null
            && s.ScopedWorldbookIds is not null && s.LibidoCharacters is not null
            && s.Presence is not null && s.Permissions is not null && s.Body is not null)
            return s;
        return s with
        {
            Flags = s.Flags is null ? new() : s.Flags,
            CgUnlocked = s.CgUnlocked is null ? new() : s.CgUnlocked,
            Achievements = s.Achievements is null ? new() : s.Achievements,
            AffectionLog = s.AffectionLog is null ? new() : s.AffectionLog,
            AllowedCharacters = s.AllowedCharacters is null ? new() : s.AllowedCharacters,
            ScopedWorldbookIds = s.ScopedWorldbookIds is null ? new() : s.ScopedWorldbookIds,
            LibidoCharacters = s.LibidoCharacters is null ? new() : s.LibidoCharacters,
            Presence = s.Presence is null ? new() : s.Presence,
            Permissions = NormalizePermissions(s.Permissions),
            Body = s.Body is null ? new() : s.Body
        };
    }

    private static SavePermissions NormalizePermissions(SavePermissions? p)
    {
        if (p is null) return new SavePermissions();
        if (p.AllowedPlugins is not null && p.AllowedTools is not null && p.AllowedCharacters is not null)
            return p;
        return p with
        {
            AllowedPlugins = p.AllowedPlugins is null ? new() : p.AllowedPlugins,
            AllowedTools = p.AllowedTools is null ? new() : p.AllowedTools,
            AllowedCharacters = p.AllowedCharacters is null ? new() : p.AllowedCharacters
        };
    }

    /// <summary>单段反序列化的独立结果：失败只影响自己那一段，其余段照常恢复。</summary>
    private sealed record SegmentResult<T>(string Name, T? Value, string? Error) where T : class;

    /// <summary>
    /// 反序列化一个顶层段。<c>JsonDocument</c>/<c>JsonElement</c> 是只读线程安全的，
    /// 所以 5 段可以并行解析（CPU 并行）；解析结果再按固定顺序在调用线程串行 Apply。
    /// 段缺失（TryGetProperty=false）不算失败——旧档本就没有那一段。
    /// </summary>
    private static SegmentResult<T> ParseSegment<T>(JsonDocument doc, string name) where T : class
    {
        try
        {
            if (!doc.RootElement.TryGetProperty(name, out var el)) return new(name, null, null);
            return new(name, el.Deserialize<T>(), null);
        }
        catch (Exception ex)
        {
            return new(name, null, ex.Message);
        }
    }

    public async Task<bool> Load(string id)
    {
        // 坏档不能把读档流程直接炸穿：返回失败并留下可展示的原因（审计 A3/U4）。
        try { return await LoadCore(id); }
        catch (Exception ex)
        {
            App.WriteLog("SaveManager.Load(" + id + ") -> " + ex);
            LastLoadNote = "这份存档读取失败：" + ex.Message;
            return false;
        }
    }

    private async Task<bool> LoadCore(string id)
    {
        LastRepairReport = null;
        var lost = new List<string>();       // 可能丢失（推断，措辞必含「可能」）
        var fixedItems = new List<string>(); // 已自动修复（实证）
        var sectionFailed = false;           // 任一段解析失败 → 严禁整档回写（防二次损坏）
        string? failedSegment = null;        // 失败的是哪一段（state 段失败 = 高损需确认）

        string? json;
        try { json = await _store.LoadRawAsync($"saves/{id}"); }
        catch (Exception ex)
        {
            json = null;
            App.WriteLog("SaveManager.LoadCore.Read " + id + " -> " + ex);
        }
        // 真实节点 01：文件已读回（SaveVM 的 _library.ListAsync 也已完成）→ 进入「解析数据」。
        // 「校验」改叫「解析」（2026-11-15，用户反馈）：这一步做的不是查坏——是把 JSON 文本
        // 变成内存对象，没坏也必须做；真正的查坏/修复已整体延后到进档之后（见下方 deferred）。
        MarkLoadPhase(2, "解析数据");

        if (string.IsNullOrEmpty(json))
        {
            // 空文件 / 读不出：不可修复。备份 + 隔离，绝不捏造空档覆盖原件。
            var bakEmpty = _store.BackupCorruptFile($"saves/{id}");
            QuarantineSave(id);
            lost.Add("可能丢失：整份存档内容（已备份原件，可手工找回）");
            LastRepairReport = new SaveRepairReport
            {
                SaveId = id,
                IncidentSummary = "存档文件为空或无法读取",
                LostItems = lost,
                FixedItems = fixedItems,
                BackupPath = bakEmpty,
                CanEnter = false,
                Committed = false,
                FailedPhase = 2,
                ErrorKind = "empty",
                WriteBackNote = "未回写，原件保留"
            };
            App.TraceStep($"SaveLoad.Corrupt kind=empty {id} bak={(bakEmpty ?? "none")}");
            LastLoadNote = "这份存档读取失败：文件为空或无法读取。";
            return false;
        }

        // 整体 JSON 语法损坏：不可修复（备份 + 隔离），绝不整档覆盖。
        // 【2026-11-15】Parse + 五段反序列化合并为单个后台任务：此前是 1+5 个 Task.Run 分片，
        // 启动期线程池饥饿时分片会排队数秒（进度条卡在解析的元凶，时快时慢）；
        // 单任务单 worker 用时确定。JsonDocument 是只读解析产物，线程池构建安全。
        JsonDocument doc;
        try { doc = await Task.Run(() => JsonDocument.Parse(json)); }
        catch (JsonException ex)
        {
            var bakSyn = _store.BackupCorruptFile($"saves/{id}");
            QuarantineSave(id);
            lost.Add("可能丢失：整份存档内容（已备份原件，可手工找回）");
            LastRepairReport = new SaveRepairReport
            {
                SaveId = id,
                IncidentSummary = "存档文件语法损坏，无法解析",
                LostItems = lost,
                FixedItems = fixedItems,
                BackupPath = bakSyn,
                CanEnter = false,
                Committed = false,
                FailedPhase = 2,
                ErrorKind = "syntax",
                WriteBackNote = "未回写，原件保留"
            };
            App.TraceStep($"SaveLoad.Corrupt kind=syntax {id} bak={(bakSyn ?? "none")}");
            LastLoadNote = "这份存档读取失败：" + ex.Message;
            return false;
        }

        var version = 0;
        using (doc)
        {
            // 角色库载入先起（后台）；五段反序列化合并为单个后台任务，不再分片排队。
            LoadDetailHook?.Invoke("正在解析角色库…");
            var libTask = _library.ListAsync();
            LoadDetailHook?.Invoke("正在解析存档五段（状态/角色/对话）…");
            var parsed = await Task.Run(() =>
            {
                var pState = ParseSegment<GameState>(doc, "state");
                var pCharState = ParseSegment<CharacterState>(doc, "char_state");
                var pCharStates = ParseSegment<Dictionary<string, CharacterState>>(doc, "char_states");
                var pSubProfiles = ParseSegment<Dictionary<string, CharacterSubProfile>>(doc, "sub_profiles");
                var pChat = ParseSegment<List<ChatRecord>>(doc, "chat");
                var v = doc.RootElement.TryGetProperty("version", out var vv) && vv.TryGetInt32(out var ver) ? ver : 0;
                return (pState, pCharState, pCharStates, pSubProfiles, pChat, v);
            });
            var sState = parsed.pState;
            var sCharState = parsed.pCharState;
            var sCharStates = parsed.pCharStates;
            var sSubProfiles = parsed.pSubProfiles;
            var sChat = parsed.pChat;
            LoadDetailHook?.Invoke("五段解析完成，等待角色库…");

            // 分段失败只降级自己那一段（用默认值），其余段照常恢复；记录「段名 + 原因」。
            if (sState.Error is not null) { sectionFailed = true; failedSegment = "state"; lost.Add("可能丢失：游戏状态 state（" + sState.Error + "）"); }
            if (sCharState.Error is not null) { sectionFailed = true; lost.Add("可能丢失：主角状态 char_state（" + sCharState.Error + "）"); }
            if (sCharStates.Error is not null) { sectionFailed = true; lost.Add("可能丢失：角色状态 char_states（" + sCharStates.Error + "）"); }
            if (sSubProfiles.Error is not null) { sectionFailed = true; lost.Add("可能丢失：角色副设定 sub_profiles（" + sSubProfiles.Error + "）"); }
            if (sChat.Error is not null) { sectionFailed = true; lost.Add("可能丢失：最近 60 条对话 chat（" + sChat.Error + "）"); }

            await libTask;   // 恢复角色状态前确保角色库已载入
            LoadDetailHook?.Invoke("角色库就绪，恢复游戏状态…");

            // ---- 串行 Apply（固定顺序）：RestoreState / Roster / ChatLog / _scope.Raise 必须串行 ----
            GameState? state = null;
            if (sState.Value is not null)
            {
                state = NormalizeCollections(sState.Value);
                if (!ReferenceEquals(state, sState.Value))
                    fixedItems.Add("补齐了状态里为 null 的集合字段");
                _engine.RestoreState(state);
            }

            if (state is not null && !string.IsNullOrEmpty(state.CharacterId)
                && _engine.Roster.TryGetValue(state.CharacterId, out var ch)
                && sCharState.Value is not null)
                ch.State = sCharState.Value;

            var savedIds = new HashSet<string>(StringComparer.Ordinal);
            if (sCharStates.Value is not null)
            {
                foreach (var (cid, cst) in sCharStates.Value)
                {
                    if (cst is null) continue;
                    savedIds.Add(cid);
                    if (_engine.Roster.TryGetValue(cid, out var c)) c.State = cst;
                }
            }
            foreach (var c in _engine.Roster.Values)
            {
                if (savedIds.Contains(c.Profile.Id)) continue;
                // v1.65 迁移：老档里 StateShared=true 的角色从不写 char_states，只留一份全局实时值。
                // 现在按存档隔离——把这份全局值就地定格为本档起点，记入修复项让 RepairAsync 回写落盘，
                // 从此各档各存一份、不再串档。不能照旧 SeedNonShared：那会把玩家养久的角色打回建档初始值。
                if (c.StateShared)
                    fixedItems.Add($"把共享角色「{c.Profile.Name}」的当前状态定格进本存档");
                else
                    c.State = SeedNonShared(c);
            }

            var savedSubIds = new HashSet<string>(StringComparer.Ordinal);
            if (sSubProfiles.Value is not null)
            {
                foreach (var (cid, sub) in sSubProfiles.Value)
                {
                    if (sub is null) continue;
                    savedSubIds.Add(cid);
                    if (_engine.Roster.TryGetValue(cid, out var c)) c.SubProfile = sub;
                }
            }
            foreach (var c in _engine.Roster.Values)
            {
                if (savedSubIds.Contains(c.Profile.Id)) continue;
                // 副设定同理：不再因 StateShared=true 跳过。老共享角色只有一份全局值，
                // 这里就地定格进本档（不重置），随后 RepairAsync 回写落盘；缺字段的新角色照旧补种。
                if (!c.StateShared)
                    c.SubProfile = new CharacterSubProfile();
            }

            ChatLog.Clear();
            if (sChat.Value is not null) ChatLog.AddRange(sChat.Value);

            version = doc.RootElement.TryGetProperty("version", out var v) && v.TryGetInt32(out var ver) ? ver : 0;
        }

        // 真实节点 02：JsonDocument.Parse + 五处 Deserialize 完成 → 进入「恢复角色」。
        MarkLoadPhase(3, "恢复角色");
        _engine.CurrentSaveId = id;
        _scope.Raise();      // 换档之后，各记忆服务必须重读这个档自己的那一份
        // 真实节点 03：作用域 Raise 完成 → 进入「唤醒插件」（桌宠刷新 / 过期档修复 / 横屏）。
        MarkLoadPhase(4, "唤醒插件");

        // 桌宠跟着存档走：换档后把桌面上的形象换成这一档的主角（不在桌宠模式时是 no-op）。
        // 由外部注入（审计 A2）：不再跨模块静态调用，且异常必须被观察，不能无声消失。
        if (RefreshPetForActiveSave is not null)
        {
            _ = Task.Run(async () =>
            {
                try { await RefreshPetForActiveSave(); }
                catch (Exception ex) { App.WriteLog("SaveManager.RefreshPet -> " + ex.Message); }
            });
        }

        // 修复延后（2026-11-15，用户决策「先进档、后台校验」）：旧版在这里同步等
        // RepairAsync（版本迁移 + 三类内存态修正 + 回写），旧档一次拖住进入数秒——
        // 而这些修复没有一件是「进档前必须完成」的。现在进档立即返回，修复延后
        // 3 秒在 UI 线程串行补跑（与界面的状态修改天然互斥，不引入并发），完成后
        // 直接经 RepairReportService 补发回执横幅（R9 常驻语义不变，只是晚几秒出现）。
        // 只有「空文件 / 整档语法损坏」这类真问题才在进入前阻断（上面已处理）。
        var needsRepair = version < SchemaVersion || sectionFailed;
        LastRepairReport = new SaveRepairReport
        {
            SaveId = id,
            IncidentSummary = sectionFailed ? "部分存档段解析失败，已用默认值恢复"
                : (fixedItems.Count > 0 ? "存档结构落后，已自动修复" : ""),
            LostItems = lost,
            FixedItems = fixedItems,
            BackupPath = null,
            // 段失败仍可进入（降级默认值）；整体语法损坏/空文件才是不可修复。
            CanEnter = true,
            Committed = false,
            FailedSegment = failedSegment,
            FailedPhase = sectionFailed ? 2 : 0,
            ErrorKind = sectionFailed ? "section" : (version < SchemaVersion ? "stale" : (fixedItems.Count > 0 ? "refs" : "none")),
            WriteBackNote = "未回写，原件保留"
        };
        if (needsRepair)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(3000);
                    var repairNote = string.Empty;
                    var repairCommitted = false;
                    await Helpers.Ui.PostAsync(async () =>
                    {
                        var repair = await RepairAsync(id, version, allowWriteBack: !sectionFailed, lost, fixedItems);
                        repairNote = repair.Note;
                        repairCommitted = repair.Committed;
                    });
                    if (!string.IsNullOrEmpty(repairNote))
                    {
                        LastLoadNote = repairNote;
                        App.WriteLog("SaveManager.Repair(deferred) -> " + repairNote);
                    }
                    LastRepairReport = new SaveRepairReport
                    {
                        SaveId = id,
                        IncidentSummary = sectionFailed ? "部分存档段解析失败，已用默认值恢复"
                            : (fixedItems.Count > 0 ? "存档结构落后，已自动修复" : ""),
                        LostItems = lost,
                        FixedItems = fixedItems,
                        BackupPath = null,
                        CanEnter = true,
                        Committed = repairCommitted,
                        FailedSegment = failedSegment,
                        FailedPhase = sectionFailed ? 2 : 0,
                        ErrorKind = sectionFailed ? "section" : (version < SchemaVersion ? "stale" : (fixedItems.Count > 0 ? "refs" : "none")),
                        WriteBackNote = !repairCommitted ? "未回写，原件保留" : ""
                    };
                    try { Services.Core.RepairReportService.Instance.Show(LastRepairReport); }
                    catch (Exception ex) { App.WriteLog("SaveManager.RepairReceipt(deferred) -> " + ex.Message); }
                }
                catch (Exception ex) { App.WriteLog("SaveManager.RepairDeferred -> " + ex); }
            });
        }

        // 手机端进入存档自动横屏（桌面/平板无方向锁概念）
        try { ScreenOrientationService.EnterSaveLandscape(); }
        catch (Exception ex) { App.WriteLog("SaveManager.EnterLandscape -> " + ex.Message); }
        return true;
    }

    /// <summary>
    /// 非共享角色在新档里的起点：数值取自角色的「建档初始值」，其余（心情、表情、精力修正、预判）
    /// 一律回到新角色该有的样子——把上一个档的心理曲线带过来，会让 TA 一见面就带着不属于这段关系的情绪。
    /// </summary>
    private static CharacterState SeedNonShared(CharacterData ch) => new()
    {
        Affection = ch.Initial.Affection,
        AffectionPoints = ch.Initial.AffectionPoints,
        Trust = ch.Initial.Trust,
        Energy = ch.Initial.Energy,
        Libido = ch.Initial.Libido,
        Mood = "normal",
        CurrentOutfit = ch.State.CurrentOutfit,
        CurrentEmotion = "normal",
        Location = ch.State.Location,
        LastSeen = DateTime.UtcNow,
        Cycle = ch.State.Cycle,
    };

    /// <summary>
    /// 过期存档修复：把结构落后的存档补齐到当前版本。
    /// 只做「能确定的修正」——越界数值夹回范围、坏行丢弃、缺失的引用补种，绝不猜测剧情内容。
    /// 回写安全（硬性）：<paramref name="allowWriteBack"/>=false（任一段解析失败）时严禁整档 Commit，
    /// 防二次损坏，只做内存态修复。实证的修复同时记入 <paramref name="fixedItems"/> 供回执条展示。
    /// </summary>
    private async Task<(string Note, bool Committed)> RepairAsync(string id, int fromVersion, bool allowWriteBack, List<string> lost, List<string> fixedItems)
    {
        var fixes = new List<string>();

        var state = _engine.State;
        // 白名单里指向已不存在的角色：清掉，否则提示词里会出现空引用
        if (state.AllowedCharacters.Count > 0)
        {
            var known = _engine.Roster.Keys.ToHashSet(StringComparer.Ordinal);
            var droppedIds = state.AllowedCharacters.Where(c => !known.Contains(c)).ToList();
            foreach (var d in droppedIds) state.AllowedCharacters.Remove(d);
            if (droppedIds.Count > 0)
            {
                fixes.Add($"清理了 {droppedIds.Count} 个已不存在的可用角色");
                fixedItems.Add($"清理了 {droppedIds.Count} 个已不存在的可用角色");
            }
        }

        // 聊天记录里的空行（旧版异常退出可能留下）
        var badChat = ChatLog.Count(r => string.IsNullOrWhiteSpace(r.Text));
        if (badChat > 0)
        {
            ChatLog.RemoveAll(r => string.IsNullOrWhiteSpace(r.Text));
            fixes.Add($"丢弃了 {badChat} 条空聊天记录");
            fixedItems.Add($"丢弃了 {badChat} 条空聊天记录");
        }

        // 角色数值越界（旧版/手改档）：夹回合法范围
        if (_engine.ActiveCharacter is { } ch)
        {
            var st = ch.State;
            var before = (st.Affection, st.AffectionPoints, st.Trust, st.Energy, st.EnergyOffset);
            st.Affection = Math.Clamp(st.Affection, 0, 100);
            st.AffectionPoints = Math.Clamp(st.AffectionPoints, 0, 5000);
            st.Trust = Math.Clamp(st.Trust, 0, 100);
            st.Energy = Math.Clamp(st.Energy, 0, 100);
            st.EnergyOffset = Math.Clamp(st.EnergyOffset, -60, 40);
            if (before != (st.Affection, st.AffectionPoints, st.Trust, st.Energy, st.EnergyOffset))
            {
                fixes.Add("把越界的好感/信任/精力数值夹回合法范围");
                fixedItems.Add("把越界的好感/信任/精力数值夹回合法范围");
            }
        }

        var note = fixes.Count == 0
            ? ""
            : $"存档已自动修复（结构版本 {fromVersion} → {SchemaVersion}）：" + string.IsNullOrWhiteSpace(string.Join("；", fixes)) + "。";

        // 回写安全：段解析失败时（allowWriteBack=false）严禁整档 Commit，防二次损坏，只做内存态修复；
        // 即使允许回写，也如实记录磁盘回写是否成功（极端情形下回写失败不得谎称已提交）。
        var committed = false;
        if (allowWriteBack)
            committed = await Commit(string.IsNullOrWhiteSpace(_engine.ActiveCharacter?.Profile.Name)
                ? "自动修复" : _engine.ActiveCharacter!.Profile.Name, "repair");
        return (note, committed);
    }

    /// <summary>给存档重命名：改写 meta.Label 并回写文件。</summary>
    public async Task<bool> Rename(string id, string newLabel)
    {
        try
        {
            var json = await _store.LoadRawAsync($"saves/{id}");
            if (string.IsNullOrEmpty(json)) return false;
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("meta", out var m)) return false;
            var slot = m.Deserialize<SaveSlot>();
            if (slot is null) return false;
            // 只替换 meta 段，其余原样保留
            using var ms = new MemoryStream();
            using (var writer = new Utf8JsonWriter(ms))
            {
                writer.WriteStartObject();
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (prop.Name == "meta")
                    {
                        writer.WritePropertyName("meta");
                        writer.WriteStartObject();
                        foreach (var mp in m.EnumerateObject())
                        {
                            if (mp.Name == nameof(SaveSlot.Label))
                                writer.WriteString(nameof(SaveSlot.Label), newLabel.Trim());
                            else
                                mp.WriteTo(writer);
                        }
                        writer.WriteEndObject();
                    }
                    else
                    {
                        prop.WriteTo(writer);
                    }
                }
                writer.WriteEndObject();
            }
            // 原子写回，避免改名过程中崩溃把存档截断（审计 P5）
            await _store.WriteRawAsync($"saves/{id}", System.Text.Encoding.UTF8.GetString(ms.ToArray()));
            RememberSlotInListCache(slot with { Label = newLabel.Trim() });
            return true;
        }
        catch (Exception ex)
        {
            App.WriteLog("SaveManager.Rename -> " + ex);
            return false;
        }
    }

    /// <summary>
    /// 从已有存档分出一个新档（Git 式分支 / 插件 / AI 的「新增存档」）：复制同一份进度，换新 id 与新名字，
    /// 记下父档血缘与分叉时刻，并把旧机制下全局共享的角色状态播种成新档的私有副本（分道扬镳）。
    /// 不改动原档，也不打断当前进度。签名保持不变，兼容 ToolManager.World.cs / MaidBridgeService.cs 的调用。
    /// </summary>
    public async Task<(bool Ok, string Message, string Id)> BranchAsync(string sourceId, string label)
    {
        try
        {
            var json = await _store.LoadRawAsync($"saves/{sourceId}");
            if (string.IsNullOrEmpty(json)) return (false, "原存档不存在。", "");
            if (JsonNode.Parse(json) is not JsonObject root || root["meta"] is not JsonObject meta)
                return (false, "原存档结构不完整。", "");
            var newId = Guid.NewGuid().ToString("N")[..12];
            var branchName = string.IsNullOrWhiteSpace(label) ? "分支存档" : label.Trim();
            var now = DateTime.Now;

            // Git 式血缘：父档 id + 分支名 + 分叉时刻。老档缺这三个字段时读档端走默认值，不影响展示。
            meta["Id"] = newId;
            meta["Label"] = branchName;
            meta["ParentId"] = sourceId;
            meta["BranchName"] = branchName;
            meta["ForkedAt"] = JsonSerializer.SerializeToNode(now);
            meta["SavedAt"] = JsonSerializer.SerializeToNode(now);

            // 源档 JSON 深拷贝已带来它落盘的角色状态（旧机制里只有非共享角色在 char_states）。
            // 那些 StateShared=true 的角色旧机制只留一份全局状态、从不写档 —— 分叉时补一份进新档，
            // 父子两档从此各走各的，互不影响（见 SeedSharedCharactersForBranch）。
            SeedSharedCharactersForBranch(root);

            await _store.WriteRawAsync($"saves/{newId}", root.ToJsonString(WriteJson));
            // 新档立刻进列表缓存，存档页刷新时能马上看到刚分出来的分支。
            if (meta.Deserialize<SaveSlot>() is { } slot) RememberSlotInListCache(slot);
            return (true, $"已从当前进度分出分支「{branchName}」。", newId);
        }
        catch (Exception ex)
        {
            App.WriteLog("SaveManager.Branch -> " + ex);
            return (false, "新增存档失败：" + ex.Message, "");
        }
    }

    /// <summary>
    /// 分支出生时给「旧机制下全局共享、从不进存档」的角色补一份私有状态（分道扬镳）。
    /// 只补 char_states / sub_profiles 里还缺的 id，绝不覆盖源档已落盘的角色状态；全程 try/catch。
    /// </summary>
    private void SeedSharedCharactersForBranch(JsonObject root)
    {
        try
        {
            var shared = _engine.Roster.Values.Where(c => c.StateShared).ToList();
            if (shared.Count == 0) return;   // 没有共享角色就不凭空造空段，保持源档结构原样

            var states = root["char_states"] as JsonObject;
            var subs = root["sub_profiles"] as JsonObject;
            if (states is null) { states = new JsonObject(); root["char_states"] = states; }
            if (subs is null) { subs = new JsonObject(); root["sub_profiles"] = subs; }

            foreach (var c in shared)
            {
                var id = c.Profile.Id;
                if (!states.ContainsKey(id))
                    states[id] = JsonSerializer.SerializeToNode(c.State, WriteJson);
                if (!subs.ContainsKey(id))
                    subs[id] = JsonSerializer.SerializeToNode(c.SubProfile, WriteJson);
            }
        }
        catch (Exception ex)
        {
            App.WriteLog("SaveManager.SeedSharedForBranch -> " + ex.Message);
        }
    }

    /// <summary>某角色名下的全部存档（用于角色选择页显示「继续」入口）。</summary>
    public async Task<List<SaveSlot>> ListByCharacter(string charId)
    {
        var all = await List();
        return all.Where(s => s.Character == charId).ToList();
    }

    /// <summary>读取最后一次游玩的存档（按保存时间最新的一个）并恢复游戏状态。</summary>
    public async Task<bool> LoadLatest()
    {
        var slots = await List();
        // 跳过没有绑定角色的空档（如旧版本产生的坏档）
        var valid = slots.Where(s => !string.IsNullOrEmpty(s.Character)).ToList();
        if (valid.Count == 0) return false;
        return await Load(valid[0].Id);
    }

    /// <summary>存档列表的 meta 缓存：角色库、存档页每次出现都要列表，整档 JSON 里只有 meta 有用。</summary>
    private readonly Dictionary<string, (long Ticks, long Length, SaveSlot Slot)> _slotCache =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>把「刚写完/刚改名」的这一档 meta 同步进列表缓存，下一次 List() 不必重读整份存档。</summary>
    private void RememberSlotInListCache(SaveSlot slot)
    {
        try
        {
            var path = Path.Combine(_store.Root, "saves", slot.Id + ".json");
            var info = new FileInfo(path);
            if (info.Exists)
                _slotCache[path] = (info.LastWriteTimeUtc.Ticks, info.Length, slot);
            _index[slot.Id] = slot;
            _indexDirty = true;
        }
        catch (Exception ex)
        {
            App.WriteLog("SaveManager.RememberSlot -> " + ex.Message);
        }
    }

    // ==================== 存档 meta 索引（审计 P4） ====================
    // 存档文件里有最近 60 条聊天与全部角色状态，为了显示一个列表就整档读一遍是明显的浪费。
    // 这里把 meta 单独存一份索引：命中索引就完全不用打开存档本体。
    private const string IndexKey = "saves/_index";
    private readonly Dictionary<string, SaveSlot> _index = new(StringComparer.Ordinal);
    private bool _indexLoaded;
    private bool _indexDirty;

    private async Task EnsureIndexLoadedAsync()
    {
        if (_indexLoaded) return;
        try
        {
            var saved = await _store.Load<Dictionary<string, SaveSlot>>(IndexKey);
            if (saved is not null)
                foreach (var (k, v) in saved) _index[k] = v;
        }
        catch (Exception ex)
        {
            // 索引坏了不等于存档坏了：退回整档扫描即可，索引会在下一次 List() 重建。
            App.WriteLog("SaveManager.LoadIndex -> " + ex.Message);
        }
        _indexLoaded = true;
    }

    public async Task<List<SaveSlot>> List()
    {
        var dir = Path.Combine(_store.Root, "saves");
        if (!Directory.Exists(dir)) return new();
        await EnsureIndexLoadedAsync();

        var list = new List<SaveSlot>();
        _unreadableSaves.Clear();
        foreach (var f in Directory.GetFiles(dir, "*.json"))
        {
            if (string.Equals(Path.GetFileName(f), "_index.json", StringComparison.OrdinalIgnoreCase))
                continue;
            try
            {
                var info = new FileInfo(f);
                var ticks = info.LastWriteTimeUtc.Ticks;
                if (_slotCache.TryGetValue(f, out var hit) && hit.Ticks == ticks && hit.Length == info.Length)
                {
                    list.Add(hit.Slot);
                    continue;
                }

                // 命中索引就不必打开整份存档（这是 List() 提速的主要来源）
                var id = Path.GetFileNameWithoutExtension(f);
                if (_index.TryGetValue(id, out var indexed) && indexed is not null)
                {
                    list.Add(indexed);
                    _slotCache[f] = (ticks, info.Length, indexed);
                    continue;
                }

                var json = await File.ReadAllTextAsync(f);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("meta", out var m))
                {
                    var slot = m.Deserialize<SaveSlot>();
                    if (slot is not null)
                    {
                        list.Add(slot);
                        _slotCache[f] = (ticks, info.Length, slot);
                        _index[id] = slot;
                        _indexDirty = true;
                    }
                }
            }
            catch (Exception ex)
            {
                // 坏档必须可见（审计 U4/A3）：记录下来并留日志，不再静默跳过。
                _unreadableSaves.Add(f);
                App.WriteLog("SaveManager.List: 存档无法读取 " + f + " -> " + ex.Message);
            }
        }

        if (_indexDirty)
        {
            try { await _store.Save(IndexKey, _index); _indexDirty = false; }
            catch (Exception ex) { App.WriteLog("SaveManager.SaveIndex -> " + ex.Message); }
        }
        return list.OrderByDescending(s => s.SavedAt).ToList();
    }

    public async Task<bool> Export(string id, string path)
    {
        var json = await _store.LoadRawAsync($"saves/{id}");
        if (string.IsNullOrEmpty(json)) return false;
        await File.WriteAllTextAsync(path, json);
        return true;
    }

    /// <summary>
    /// 导出单个存档（文件名 + 字节流，供系统保存对话框使用）。
    /// includePlugins = true 时打成 .wbsave：存档 JSON + 所用插件代码，防止「有数据没代码」。
    /// 插件设置（settings.json）不随包外带——里面可能有密钥，用户把存档发给别人时不该泄露。
    /// </summary>
    public async Task<(string name, byte[] bytes)?> ExportStream(string id, bool includePlugins = false)
    {
        var json = await _store.LoadRawAsync($"saves/{id}");
        if (string.IsNullOrEmpty(json)) return null;
        var label = id;
        var characterId = "";
        using (var doc = JsonDocument.Parse(json))
        {
            if (doc.RootElement.TryGetProperty("meta", out var m))
            {
                var slot = m.Deserialize<SaveSlot>();
                if (slot is not null)
                {
                    label = slot.Label;
                    characterId = slot.Character;
                }
            }
        }
        if (!includePlugins)
            return ($"存档_{label}.json", System.Text.Encoding.UTF8.GetBytes(json));

        try
        {
            using var ms = new MemoryStream();
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
            {
                WriteZipText(zip, "save.json", json);
                var used = await PluginsUsedByAsync(characterId, json);
                var manifest = new
                {
                    format = "wab-save-bundle",
                    bundleVersion = 1,
                    app = "WarmAsBefore",
                    exportedAt = DateTime.Now,
                    saveId = id,
                    label,
                    character = characterId,
                    pluginSettingsIncluded = false,
                    plugins = used.Select(p => new { p.Manifest.Id, p.Manifest.Name, p.Manifest.Version })
                };
                WriteZipText(zip, "manifest.json", JsonSerializer.Serialize(manifest, WriteJson));
                foreach (var p in used)
                {
                    var baseDir = Path.GetFullPath(p.Dir);
                    foreach (var file in Directory.GetFiles(baseDir, "*", SearchOption.AllDirectories))
                    {
                        var rel = Path.GetRelativePath(baseDir, file).Replace('\\', '/');
                        // 插件设置不随存档外带（里面可能有用户填的密钥）；
                        // 旧版本误把绝对路径当存储键，落盘成 settings.json.json，那种残留文件同样不能带。
                        if (Path.GetFileName(rel).StartsWith("settings.json", StringComparison.OrdinalIgnoreCase)) continue;
                        var entry = zip.CreateEntry($"plugins/{p.Manifest.Id}/{rel}", CompressionLevel.Optimal);
                        await using var es = entry.Open();
                        await using var fs = File.OpenRead(file);
                        await fs.CopyToAsync(es);
                    }
                }
            }
            var safeLabel = string.Concat(label.Where(ch => !Path.GetInvalidFileNameChars().Contains(ch)));
            if (string.IsNullOrWhiteSpace(safeLabel)) safeLabel = id;
            return ($"存档_{safeLabel}.wbsave", ms.ToArray());
        }
        catch (Exception ex)
        {
            App.WriteLog("SaveManager.ExportBundle -> " + ex);
            return null;
        }
    }

    /// <summary>这份存档实际用到的插件：存档权限白名单里点名的，加上启用中的全部。</summary>
    private async Task<List<PluginEntry>> PluginsUsedByAsync(string characterId, string saveJson)
    {
        var all = await _plugins.AllAsync();
        if (all.Count == 0) return new();
        SavePermissions? permissions = null;
        try
        {
            using var doc = JsonDocument.Parse(saveJson);
            if (doc.RootElement.TryGetProperty("state", out var st)
                && st.TryGetProperty(nameof(GameState.Permissions), out var perm))
                permissions = perm.Deserialize<SavePermissions>();
        }
        catch (Exception ex) { App.WriteLog("SaveManager.PluginsUsedBy -> " + ex.Message); }
        return all
            .Where(p => p.Enabled)
            .Where(p => permissions is null || permissions.AllowAllPlugins
                        || permissions.AllowedPlugins.Contains(p.Manifest.Id, StringComparer.OrdinalIgnoreCase))
            .ToList();
    }

    private static void WriteZipText(ZipArchive zip, string path, string text)
    {
        var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new System.Text.UTF8Encoding(false));
        writer.Write(text);
    }

    private static readonly JsonSerializerOptions WriteJson = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true
    };

    /// <summary>备份全部存档到指定文件夹（复制 saves 目录）。</summary>
    public async Task<int> Backup(string destFolder)
    {
        var src = Path.Combine(_store.Root, "saves");
        if (!Directory.Exists(src)) return 0;
        Directory.CreateDirectory(destFolder);
        var n = 0;
        foreach (var f in Directory.GetFiles(src, "*.json"))
        {
            File.Copy(f, Path.Combine(destFolder, Path.GetFileName(f)), overwrite: true);
            n++;
        }
        return n;
    }

    /// <summary>导入存档：普通 .json 直接写入；.wbsave 是 zip，先取存档，再补齐缺失的插件。</summary>
    public async Task<(bool Ok, string Message)> Import(string path)
    {
        try
        {
            if (IsZip(path)) return await ImportBundleAsync(path);
            var json = await File.ReadAllTextAsync(path);
            var (ok, label) = await ImportSaveJsonAsync(json);
            return ok ? (true, $"已导入存档「{label}」。") : (false, "导入失败：文件格式不正确。");
        }
        catch (Exception ex)
        {
            App.WriteLog("SaveManager.Import -> " + ex);
            return (false, "导入失败：" + ex.Message);
        }
    }

    private static bool IsZip(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            Span<byte> head = stackalloc byte[4];
            return fs.Read(head) == 4 && head[0] == 0x50 && head[1] == 0x4B
                   && head[2] is 0x03 or 0x05 or 0x07;
        }
        catch { return false; }
    }

    /// <summary>
    /// 存档 id 只能是单层文件名。导入的 .json / .wbsave 都是外部输入，
    /// 不过这道闸，"../../evil" 就能把文件写到数据目录之外（审计 S4）。
    /// </summary>
    private static bool IsValidSaveId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;
        if (!StorageProvider.IsSafeKey(id)) return false;
        return !id.Contains('/') && !id.Contains('\\');
    }

    private async Task<(bool Ok, string Label)> ImportSaveJsonAsync(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("meta", out var m)) return (false, "");
        var slot = m.Deserialize<SaveSlot>();
        if (slot is null || !IsValidSaveId(slot.Id))
        {
            App.WriteLog("SaveManager.Import: 存档 id 不合法，已拒绝导入");
            return (false, "");
        }
        // 走 StorageProvider：原子写（tmp+替换），不再直接覆盖目标文件（审计 P5）。
        await _store.WriteRawAsync($"saves/{slot.Id}", json);
        RememberSlotInListCache(slot);
        return (true, slot.Label);
    }

    /// <summary>
    /// 导入 .wbsave：存档本体优先，插件只补缺、不覆盖已装版本（避免别人的旧插件盖掉本地新版本）。
    /// 缺依赖时不失败，交给插件中心显示安装入口。
    /// </summary>
    private async Task<(bool Ok, string Message)> ImportBundleAsync(string path)
    {
        var tmp = Path.Combine(Path.GetTempPath(), "wab_save_" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            Directory.CreateDirectory(tmp);
            SafeZip.ExtractToDirectory(path, tmp);
            var saveFile = Directory.GetFiles(tmp, "save.json", SearchOption.AllDirectories).FirstOrDefault();
            if (saveFile is null) return (false, "这个包里没有 save.json，不是有效的存档包。");
            var (ok, label) = await ImportSaveJsonAsync(await File.ReadAllTextAsync(saveFile));
            if (!ok) return (false, "包里的存档格式不正确。");

            var installed = 0;
            var skipped = 0;
            var failed = 0;
            var pluginsRoot = Path.Combine(Path.GetDirectoryName(saveFile)!, "plugins");
            if (Directory.Exists(pluginsRoot))
            {
                foreach (var dir in Directory.GetDirectories(pluginsRoot))
                {
                    if (!File.Exists(Path.Combine(dir, "plugin.json"))) continue;
                    try
                    {
                        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(dir, "plugin.json")));
                        var id = doc.RootElement.TryGetProperty("id", out var idv) && idv.ValueKind == JsonValueKind.String
                            ? idv.GetString() ?? Path.GetFileName(dir)
                            : Path.GetFileName(dir);
                        if (await _plugins.FindAsync(id) is not null)
                        {
                            skipped++;
                            continue;
                        }
                    }
                    catch (Exception ex) { App.WriteLog("SaveManager.ImportBundle.PluginManifest -> " + ex.Message); }
                    var result = await _plugins.InstallFromFolderAsync(dir);
                    if (result.Ok) installed++;
                    else failed++;
                }
            }

            var pluginsNote = installed == 0 && skipped == 0 && failed == 0
                ? "这个包没有附带插件。"
                : $"附带插件：新装 {installed} 个，已存在跳过 {skipped} 个" + (failed > 0 ? $"，失败 {failed} 个" : "") + "。";
            return (true, $"已导入存档「{label}」。{pluginsNote}");
        }
        catch (Exception ex)
        {
            App.WriteLog("SaveManager.ImportBundle -> " + ex);
            return (false, "导入失败：" + ex.Message);
        }
        finally
        {
            try { Directory.Delete(tmp, true); } catch { }
        }
    }

    /// <summary>存档高级设置：不进入游戏也能读写这份存档的插件/工具/角色白名单。</summary>
    public async Task<(SavePermissions Permissions, List<string> AllowedCharacters)?> LoadRulesAsync(string id)
    {
        var json = await _store.LoadRawAsync($"saves/{id}");
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("state", out var st)) return null;
            var state = st.Deserialize<GameState>();
            if (state is null) return null;
            return (state.Permissions, state.AllowedCharacters.ToList());
        }
        catch (Exception ex) { App.WriteLog("SaveManager.ReadSaveSection -> " + ex.Message); return null; }
    }

    public async Task<bool> SaveRulesAsync(string id, SavePermissions permissions, IEnumerable<string> allowedCharacters)
    {
        var json = await _store.LoadRawAsync($"saves/{id}");
        if (string.IsNullOrEmpty(json)) return false;
        try
        {
            if (JsonNode.Parse(json) is not JsonObject root) return false;
            if (root["state"] is not JsonObject stateNode) return false;
            var state = stateNode.Deserialize<GameState>();
            if (state is null) return false;
            state.Permissions = permissions;
            state.AllowedCharacters.Clear();
            foreach (var c in allowedCharacters) state.AllowedCharacters.Add(c);
            root["state"] = JsonSerializer.SerializeToNode(state);
            await _store.WriteRawAsync($"saves/{id}", root.ToJsonString(WriteJson));
            return true;
        }
        catch (Exception ex)
        {
            App.WriteLog("SaveManager.SaveRules -> " + ex);
            return false;
        }
    }

    /// <summary>
    /// 换这一档的主角。「主角」只属于存档：改的是这份档的 meta 与 state.CharacterId，
    /// 角色库里的角色本身不动；正在玩的这份存档会同步切过去。
    /// </summary>
    public async Task<(bool Ok, string Message)> SetMainCharacterAsync(string id, string charId)
    {
        try
        {
            if (string.IsNullOrEmpty(charId)) return (false, "没有指定角色。");
            var json = await _store.LoadRawAsync($"saves/{id}");
            if (string.IsNullOrEmpty(json)) return (false, "存档不存在。");
            if (JsonNode.Parse(json) is not JsonObject root) return (false, "存档结构不完整。");
            if (root["state"] is not JsonObject stateNode) return (false, "存档缺少运行状态。");
            var state = stateNode.Deserialize<GameState>();
            if (state is null) return (false, "存档缺少运行状态。");
            state.CharacterId = charId;
            root["state"] = JsonSerializer.SerializeToNode(state);
            if (root["meta"] is JsonObject meta)
            {
                meta["Character"] = charId;
                if (meta.Deserialize<SaveSlot>() is { } slot) RememberSlotInListCache(slot);
            }
            await _store.WriteRawAsync($"saves/{id}", root.ToJsonString(WriteJson));
            if (string.Equals(_engine.CurrentSaveId, id, StringComparison.Ordinal) && _engine.Roster.ContainsKey(charId))
                _engine.SetCharacter(charId);
            var name = _engine.Roster.TryGetValue(charId, out var ch) ? ch.Profile.Name : charId;
            return (true, $"这一档的主角改成了「{name}」。");
        }
        catch (Exception ex)
        {
            App.WriteLog("SaveManager.SetMain -> " + ex);
            return (false, "换主角失败：" + ex.Message);
        }
    }

    /// <summary>这份存档里点名参与性欲系统的角色（不进存档也能读）。</summary>
    public async Task<List<string>?> LoadLibidoCharactersAsync(string id)
    {
        var json = await _store.LoadRawAsync($"saves/{id}");
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("state", out var st)) return null;
            return st.Deserialize<GameState>()?.LibidoCharacters.ToList();
        }
        catch (Exception ex) { App.WriteLog("SaveManager.ReadSaveSection -> " + ex.Message); return null; }
    }

    /// <summary>只改「谁参与性欲系统」，存档里其余内容原样保留。</summary>
    public async Task<bool> SaveLibidoCharactersAsync(string id, IEnumerable<string> characters)
    {
        var json = await _store.LoadRawAsync($"saves/{id}");
        if (string.IsNullOrEmpty(json)) return false;
        try
        {
            if (JsonNode.Parse(json) is not JsonObject root) return false;
            if (root["state"] is not JsonObject stateNode) return false;
            var state = stateNode.Deserialize<GameState>();
            if (state is null) return false;
            state.LibidoCharacters.Clear();
            foreach (var c in characters.Where(c => !string.IsNullOrWhiteSpace(c)).Distinct(StringComparer.Ordinal))
                state.LibidoCharacters.Add(c);
            root["state"] = JsonSerializer.SerializeToNode(state);
            await _store.WriteRawAsync($"saves/{id}", root.ToJsonString(WriteJson));
            return true;
        }
        catch (Exception ex)
        {
            App.WriteLog("SaveManager.SaveLibido -> " + ex);
            return false;
        }
    }

    /// <summary>
    /// 这份存档选用的角色世界观（不进存档也能读）。空 id = 用角色自己的默认。
    /// 同时带回这份存档的主角 id，调用方才知道该把「跨存档」的修改写回谁。
    /// </summary>
    public async Task<(string CharacterId, string WorldviewId, bool ScopedToSave, List<string> ScopedBooks)?>
        LoadWorldviewAsync(string id)
    {
        var json = await _store.LoadRawAsync($"saves/{id}");
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("state", out var st)) return null;
            var state = st.Deserialize<GameState>();
            if (state is null) return null;
            return (state.CharacterId ?? "", (state.CharacterWorldviewId ?? "").Trim(),
                state.WorldviewScopedToSave, state.ScopedWorldbookIds.ToList());
        }
        catch (Exception ex) { App.WriteLog("SaveManager.ReadSaveSection -> " + ex.Message); return null; }
    }

    /// <summary>
    /// 只改「这份存档用哪套角色世界观」与它复合进来的额外世界书。
    /// 跨存档开关为真时，角色自己的 ActiveWorldviewId 由调用方另行写回 —— 服务只认存档这一侧，
    /// 避免「读存档规则」同时改动角色库。
    /// </summary>
    public async Task<bool> SaveWorldviewAsync(string id, string worldviewId, bool scopedToSave,
        IEnumerable<string> scopedBooks)
    {
        var json = await _store.LoadRawAsync($"saves/{id}");
        if (string.IsNullOrEmpty(json)) return false;
        try
        {
            if (JsonNode.Parse(json) is not JsonObject root) return false;
            if (root["state"] is not JsonObject stateNode) return false;
            var state = stateNode.Deserialize<GameState>();
            if (state is null) return false;
            state.CharacterWorldviewId = (worldviewId ?? "").Trim();
            state.WorldviewScopedToSave = scopedToSave;
            state.ScopedWorldbookIds.Clear();
            foreach (var book in scopedBooks.Where(b => !string.IsNullOrWhiteSpace(b))
                         .Distinct(StringComparer.Ordinal))
                state.ScopedWorldbookIds.Add(book);
            root["state"] = JsonSerializer.SerializeToNode(state);
            await _store.WriteRawAsync($"saves/{id}", root.ToJsonString(WriteJson));
            return true;
        }
        catch (Exception ex)
        {
            App.WriteLog("SaveManager.SaveWorldview -> " + ex);
            return false;
        }
    }

    public Task<bool> Delete(string id)
    {
        if (!_store.Exists($"saves/{id}")) return Task.FromResult(false);
        _store.Delete($"saves/{id}");
        _slotCache.Remove(Path.Combine(_store.Root, "saves", id + ".json"));
        _index.Remove(id);
        _indexDirty = true;
        return Task.FromResult(true);
    }

    /// <summary>
    /// 给已有存档换绑/解绑世界树（不进入游戏也能改）。只动 state 里的两个字段，其余原样保留。
    /// 世界树自带的地图与常识在下次进游戏时生效；解绑时内存里的世界常识一并撤掉。
    /// </summary>
    public async Task<bool> BindWorldAsync(string id, string worldId, string worldName)
    {
        var json = await _store.LoadRawAsync($"saves/{id}");
        if (string.IsNullOrEmpty(json)) return false;
        try
        {
            if (JsonNode.Parse(json) is not JsonObject root || root["state"] is not JsonObject stateNode) return false;
            stateNode["WorldId"] = worldId ?? "";
            stateNode["WorldName"] = worldName ?? "";
            await _store.WriteRawAsync($"saves/{id}", root.ToJsonString(WriteJson));
            _slotCache.Remove(Path.Combine(_store.Root, "saves", id + ".json"));
            return true;
        }
        catch (Exception ex)
        {
            App.WriteLog("SaveManager.BindWorld -> " + ex);
            return false;
        }
    }
}