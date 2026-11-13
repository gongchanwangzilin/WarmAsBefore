# 温暖如初 全面代码审计 · 缺陷清单与处置记录

> 编成时间：2026-11-14 ｜ 触发：用户报告「插件装好后游戏一连接，宿主连接一下立刻断开并卡退」，并要求全面审计列出 bug。
> 产出来源：架构师高见远（全量审计）→ QA 严过关（逐条核实与定级、两轮回归）→ 工程师寇豆码（修复）→ 交付总监齐活林（独立复核与收口）。
> 本文件由交付总监按上述成员产出汇总编成，条目内容与定级均来自成员报告，行号以修复前位置为准（修复后可能漂移）。

---

## 一、用户实测的崩溃：根因与处置

**现象**：`wab_minecraft` 插件装入宿主后，Minecraft 模组一连接就「连上一下立刻断开」，宿主整体卡死、进程被关闭。

**根因（已修）**：`Services/Core/GameEngine.cs:11` 的角色花名册 `Roster` 用的是非线程安全的 `Dictionary`。桥接服务后台线程（握手、state 帧处理、推送统计）读它，UI 线程（`AddAsync`/`UpdateAsync`）写它，并发读写导致字典内部结构损坏：
- 遍历时桶链表成环 → 死循环 → CPU 占满 → 界面卡死 → 系统回收进程（「卡退」）
- 或抛 `InvalidOperationException` / `IndexOutOfRangeException` → 非 UI 线程未处理异常直接终止进程（「直接被关闭」）
- 死循环发生时线程正持 `_bindingOpsLock`，握手等锁 5 秒超时 → 游戏端「连接一下立刻断开」

**一个根因同时解释三个现象。** v1.58.0 新增 7 个桥接工具与大量插件设置字段拉长了并发窗口，显著放大触发概率（属既有线程安全隐患被放大，非新引入）。

**修复**：`Roster` 改 `ConcurrentDictionary`（线程安全字典），`CharacterLibrary.cs:195` 同步改 `TryRemove`。

---

## 二、审计缺陷清单（架构师 14 条，QA 已逐条核实定级）

| 编号 | 严重度 | 位置 | 问题 → 现象 | 状态 |
|---|---|---|---|---|
| C1 | 致命 | `MaidBridgeService.cs:3460-3477` | `Dispose()` 在异步任务仍持锁时释放 7 个 `SemaphoreSlim` → `ObjectDisposedException` 级联崩溃 | 已修·回归通过 |
| C2 | 致命 | `MaidBridgeService.cs:1615/1728/1829/803` | state 帧无界并发 + 等锁无超时 + 持锁走 UI 线程 + 握手 5 秒超时抛异常断连 | 已修·回归通过 |
| C3 | 致命 | `MaidBridgeService.cs:3464-3469` | 取消令牌源 `Cancel()` 后立刻 `Dispose()` → 后台 `Task.Delay` 抛 `ObjectDisposedException` 逃逸 | 已修·回归通过 |
| S1 | 严重 | `PluginHost.cs:1391-1415` | 插件设置「读-改-写」无锁并发，后写覆盖先写 → 设置丢失 | **未修**（P1） |
| S2 | 一般 | `MaidBridgeService.cs:180-209` | `FileSystemWatcher`（文件监视器）创建后从不释放 | 已修·回归通过 |
| S3 | 严重 | `MaidBridgeService.cs:2858-2875` | `CloseSession` 不释放会话读写器 → socket/Reader/Writer 泄漏 | 已修·回归通过 |
| S4 | 一般 | `MaidBridgeService.cs:1885-1915` | 绑定数据加载失败后标志位不置真 → 每次握手重复重试 | **未修**（P2） |
| M1 | 轻微 | `MaidBridgeService.cs:67` | `_voiceLock` 未释放 | 已随 C1 策略消解 |
| M2 | 一般 | `PluginHost.cs:1727-1757` | HTTP 请求体无大小上限 → 本地恶意进程可耗尽内存 | **未修**（P2） |
| M3 | 一般 | `PluginHost.cs:1727-1757` | HTTP 读取无超时 → 慢速客户端占用线程池 | **未修**（P2） |
| M4 | 一般 | `MaidBridgeService.cs:3410-3447` | UI 调度回退路径在非 UI 线程执行 UI 操作 | **未修**（P2） |
| M5 | 轻微 | `MaidBridgeService.cs:164-178` | `Dispose()` 不等待后台任务结束 | **未修**（P3） |
| L1 | 轻微 | 全局 100+ 处 | `catch { }` 静默吞异常，问题无日志可查 | **未修**（P3） |
| L2 | 轻微 | `MaidBridgeService.cs` | 信号量释放顺序问题（与 C1 同根） | 已随 C1 消解 |

> 定级口径：S2/S4 由「严重」降为「一般」、M1/M5 由「一般」降为「轻微」，理由是暴露面实测小于审计假设、C1–C3 修复摘掉了放大器；其余维持架构师原判。

---

## 三、修复过程中新发现的缺陷（4 条，均已修）

| 编号 | 严重度 | 位置 | 问题 | 状态 |
|---|---|---|---|---|
| N1 | 致命 | `MaidBridgeService.cs` / `Phone.cs` / `Bindings.cs` | `world.Maids` 是普通 `Dictionary`，锁外写入与持锁写者并发 → 与 Roster 崩溃同族 | 已修（16/16 访问点全持 `lock (_bindingsGate)`） |
| N1b | 严重 | `CharacterLibrary.cs:130` | `SaveRosterAsync` 快照+落盘无互斥 → 并发建角色时后写者用旧快照盖盘、角色从磁盘丢失（不损坏文件，是丢更新） | 已修（`_saveLock` 临界区，快照在锁内取） |
| N2 | 致命 | `MaidBridgeService.cs:2038-2045` | 为在锁内 await 落盘而用 `Monitor.Enter/Exit` 跨 `await`。`Monitor` 线程绑定，续延换线程后 `Monitor.Exit` 抛 `SynchronizationLockException` 且锁永远释放不掉 → 全绑定面死锁（宿主卡死家族复活）。**实证**：真异步文件 IO 下 `enter=4 exit=6 same=False → SynchronizationLockException`，随后 `TryEnter other thread: BLOCKED -> deadlock` | 已修（`StorageProvider.SaveWithGateAsync`：锁内同步序列化取快照、锁外异步写盘） |
| F2 | 一般 | `MaidBridgeService.cs:3494` | `Context` 构建处 `table.Maids` 读取与 `RecentFeeds` 枚举在锁外 → 与持锁写者读写竞态 | 已修（锁内取快照） |

**默认值口径不一致（历史项，已拍板修）**：`MaidBridgeModels.cs:12` 的 `AutoCreateCharacters`（自动为新女仆建角色）初始化器为 `true`，而 `plugin.json:14` 声明 `"default": "false"` 并注明「默认关」、`From()` 回退也是 `false`。12 个布尔默认值逐个三方比对仅此一处不一致，判定为笔误失同步。按「AI 主动行为默认关」原则改代码跟契约：初始化器改 `false`，并同步修正期望值相反的测试断言。

---

## 四、本轮改动总览（7 个文件）

| 文件 | 改动要点 |
|---|---|
| `Services/Core/GameEngine.cs` | `Roster` 改 `ConcurrentDictionary`（崩溃根因） |
| `Services/Character/CharacterLibrary.cs` | `Remove`→`TryRemove`；`SaveRosterAsync` 加 `_saveLock` 防丢更新 |
| `Services/Core/StorageProvider.cs` | 新增 `SaveWithGateAsync`（锁内同步序列化、锁外异步写盘），消除 Monitor 跨 await 死锁 |
| `Modules/MinecraftBridge/MaidBridgeService.cs` | `_stateGate` 并发闸；8 处绑定锁加 5/10 秒超时；握手超时改降级保连接；`BindMaidsCore` 锁内纯逻辑 / `FlushPendingCharactersAsync` 锁外注册；`Dispose()` 不再释放信号量与取消令牌源；补文件监视器与会话读写器释放；`world.Maids` 全部访问点持 `_bindingsGate` |
| `Modules/MinecraftBridge/MaidBridgeService.Phone.cs` | 手机配角色路径的 `world.Maids` 读写加锁；去掉 `OnUiAsync` 包装 |
| `Modules/MinecraftBridge/MaidBridgeService.Bindings.cs` | 绑定表增删/查询加锁；锁等待加超时 |
| `Modules/MinecraftBridge/MaidBridgeModels.cs` | `Maids` 字典补锁契约注释；`AutoCreateCharacters` 默认改 `false` |

---

## 五、验证结论

- **编译**：`dotnet build --no-restore -f net10.0-windows10.0.19041.0` → 0 警告 0 错误
- **回归自检**：插件包段 49/49、角色表段 11/11、桥接段 22/22、插件段 24/24
- **QA 两轮回归**：Round 1 通过（发现 N1）；Round 2 七项（死锁消除、锁覆盖、UI 依赖反证、落盘互斥、异常路径、跑测、编译）全部通过
- **锁覆盖**：`world.Maids` 16/16 访问点全部持 `lock (_bindingsGate)`；`_bindings.Worlds` 增删点同锁；锁序无反转（`_bindingOpsLock` 外 → `_bindingsGate` 内）
- **死锁消除**：产品代码 `Monitor.Enter/Exit` 清零；`SaveWithGateAsync` 的 `lock` 内仅同步序列化

---

## 六、覆盖范围与未覆盖部分（如实）

**已覆盖**：`Modules/MinecraftBridge/` 全部 7 个文件完整精读（含 3629 行主文件）、`Services/Plugins/PluginHost.cs` 全文（1939 行）、插件 3 个 JS 钩子、`SettingsManager`/`StorageProvider`/`CharacterLibrary`/`GameEngine`，并做全局并发模式搜索（`async void`、空 `catch`、`.Result`、锁、事件订阅、`Monitor`）。

**未覆盖**：`Modules/Mcp`、`Modules/Tools`、`Modules/DataPack`、`Services/Character`（角色库之外）、`Services/Memory`、`Services/World`、`Services/CloudSync`、`Services/Media`、`ViewModels`/`Views`（仅搜 `async void`）、模组 Java 侧。**本清单是桥接与插件链路的深挖结果，不等于整个应用只有这些问题。**

---

## 七、遗留记录（不阻塞交付）

| 项 | 级别 | 说明 |
|---|---|---|
| S1 插件设置并发写丢失 | P1 | `PluginHost.cs:1391-1415` 读-改-写无互斥，最小修法：per-plugin `SemaphoreSlim` 包住整段 |
| S4 绑定加载失败后标志位不置真 | P2 | `catch` 里补 `_bindingsLoaded = true` 一行 |
| M2/M3 HTTP 请求体无上限、读取无超时 | P2 | `ReadHttpAsync` 加大小上限与取消令牌（注意 `TcpClient.ReceiveTimeout` 对 async 无效） |
| M4 UI 调度回退在非 UI 线程执行 UI 操作 | P2 | `OnUiAsync` 三重载的 catch 改为记日志后跳过 |
| M5 Dispose 不等后台任务 | P3 | 末尾 `Task.WhenAll(...).Wait(3s)` 一行收尾 |
| L1 空 catch 100+ 处 | P3 | 关键路径补 `App.WriteLog`，资源释放类可保留 |
| `table.Taste` 无锁读 | P3 | 与 `world.Maids` 同点位，概率更低 |
| Reload `Roster.Clear()` 窗口 | P3 | 极窄窗口，终解是新字典构建完再原子换引用 |
| `Count` 后接 `First()` 的 TOCTOU | P3 | 理论窗口，旧实现同病且更重 |
| 5 处非绑定锁等锁无超时 | P3 | 持有者均短临界区，下版本统一补 |
| `UpdateAsync` 三处 `OnUiAsync` 可去未去 | P3 | 功能正确无害；`FlushPending` 注释里「（UI 线程）」三字已过时 |
