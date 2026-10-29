# 架构

本文描述 Warm As Before 当前的分层方式、启动路径、主要数据流与扩展约束。
具体到每个类/页面的职责见 [`COMPONENTS.md`](COMPONENTS.md)；插件与工具的外部契约见
[`PLUGINS.md`](PLUGINS.md)。

- 目标框架：`net10.0-windows10.0.19041.0`、`net10.0-android`
- UI 栈：.NET MAUI + WinUI 3（Windows）/ MAUI Android
- 架构模式：MVVM + 构造函数依赖注入
- 数据形态：本地 JSON + 素材文件；可选镜像到用户自己的 GitHub 仓库

---

## 1. 分层与依赖方向

```text
┌──────────────────────────────────────────────────────────────┐
│ Views（XAML 页面、少量平台事件）                              │
│   绑定 BindingContext，转发尺寸/指针/生命周期事件              │
└───────────────────────────────┬──────────────────────────────┘
                                │ 命令 / 属性绑定
┌───────────────────────────────▼──────────────────────────────┐
│ ViewModels（页面状态、命令、导航参数）                         │
└───────────────────────────────┬──────────────────────────────┘
                                │ 构造函数注入
┌───────────────────────────────▼──────────────────────────────┐
│ Services（跨模块能力）      Modules（业务模块）                │
│ 核心 / 角色 / 记忆 / 媒体 /   对话 / 商店 / 游戏 / 战斗 /      │
│ 世界 / 插件 / 云同步          MCP / 工具 / 桥接 / 自动化 …     │
└───────────────────────────────┬──────────────────────────────┘
                                │ 读写
┌───────────────────────────────▼──────────────────────────────┐
│ Models（纯数据） + StorageProvider（JSON 落盘）                │
└──────────────────────────────────────────────────────────────┘
```

约定：

1. **主方向**是 `Views → ViewModels → Services/Modules → Models`。
2. 跨模块协作一律走构造函数注入；服务之间不通过静态字段互相取用。
3. 页面可以注入服务处理纯 UI 事务（例如 `PluginSettingsPage` 直接读 `PluginHost` 生成表单、
   `MapPage` 订阅地图事件），但业务状态仍由 ViewModel 持有。
4. `Modules/*` 可以使用 `Services/*`，反向依赖只出现在明确的总装点
   （`RuntimeConfigurator`、`MauiProgram`/`Composition`），不写进服务的构造函数。
5. Models 不引用服务，不发起 IO。

### 命名空间与目录

目录按功能分组，命名空间保持扁平，移动文件不会改动调用点：

| 目录 | 命名空间 |
|---|---|
| `Services/` 及子目录 | `WarmAsBefore.Services` |
| `ViewModels/` 及子目录 | `WarmAsBefore.ViewModels` |
| `Views/` 及子目录 | `WarmAsBefore.Views` |
| `Models/` | `WarmAsBefore.Models` |
| `Modules/<Name>/` | `WarmAsBefore.Modules.<Name>` |
| `Composition/` | `WarmAsBefore.Composition` |

例外：`Views/Character`、`Views/Settings` 等子目录中的页面仍使用 `WarmAsBefore.Views`，
XAML 通过 `clr-namespace:WarmAsBefore.Views` 引用，不需要额外前缀。

---

## 2. 组合根与生命周期

`MauiProgram.CreateMauiApp()` 只做三件事：创建 builder、挂 MAUI/CommunityToolkit、
按顺序调用组合根扩展方法。

```csharp
builder.Services
    .AddDesignSystemServices()
    .AddCoreServices()
    .AddModuleServices()
    .AddCharacterServices()
    .AddPluginServices()
    .AddViewModels()
    .AddPages();
```

| 文件 | 注册内容 |
|---|---|
| `Composition/Registration.DesignSystem.cs` | `ThemeManager`、主题中文名映射 |
| `Composition/Registration.Core.cs` | 存储、设置、引擎、通知、音频、语音、素材、角色库、记忆、媒体、窗口等基础服务 |
| `Composition/Registration.Modules.cs` | 对话、天气、定位、生理、MCP、工具、商店、战斗、CG、展示案、存档、云同步等模块服务 |
| `Composition/Registration.Character.cs` | 角色包、形象包、换装流水线、头像裁剪 |
| `Composition/Registration.Plugins.cs` | 权限表、插件宿主、技能库、沙箱策略、工具管理器 |
| `Composition/Registration.ViewModels.cs` | 全部页面 ViewModel |
| `Composition/Registration.Pages.cs` | 全部页面 |

生命周期：

- **Singleton**：无状态或全局唯一的基础设施与模块服务（`StorageProvider`、`SettingsManager`、
  `GameEngine`、`CharacterLibrary`、`ChatEngine`、`PluginHost`、`SaveManager`、`CloudSyncService` 等）。
- **Transient**：全部 ViewModel 与页面。页面关闭即释放，页面状态不跨实例保存。
- 依赖顺序按「被依赖方先注册」书写，但 Microsoft DI 在首次解析时构造单例，
  因此注册顺序本身不构成循环依赖的解决方案；真正的循环由事件或委托打断。

> 新增服务的落点：只被一个模块使用 → 放进该模块；被多个模块使用 → 放进 `Services/<功能>`；
> 需要总装接线 → 在 `RuntimeConfigurator` 注入并下发，不要从页面直接改别的服务状态。

---

## 3. 启动链路

```text
Platforms/Windows/App.xaml.cs 或 Platforms/Android/MainApplication.cs
  └─ MauiProgram.CreateMauiApp()
       ├─ builder.Build()
       └─ App 构造
            ├─ 注册未处理异常与未观察任务日志
            ├─ InitializeComponent()（合并色板、样式、转换器）
            ├─ StyleWorldTokens.Publish(Soft, 系统深浅色, classic)
            ├─ ThemeManager.FollowSystemTheme()
            └─ 全局页面背景走 PageBgBrush

App.CreateWindow()
  ├─ SettingsManager.Restore()（读取 settings.json，执行旧字段迁移）
  ├─ 构建 AppShell + Window（默认 1280×800）
  ├─ Windows：挂 WindowCloseGuard、WindowTaskbarFocus，监听 WinUI SizeChanged
  ├─ 计算视口并下发 StyleWorldTokens.SetViewport
  ├─ Window.Destroying → 桌宠收尾 + 退出前自动存档
  └─ AfterWindowStartedAsync()
       ├─ ScreenOrientationService.ApplyOnStart()
       ├─ DownloadBackupService.OnAppStartAsync()
       ├─ WindowTopmost.Force() / BringToFront()
       ├─ RuntimeConfigurator.Start()
       ├─ CloudSyncService.Start()
       └─ 解析 --open=<route> 并跳转（诊断入口）
```

`AppShell` 的默认内容是 `TitlePage`（路由 `title`），其余页面通过
`Routing.RegisterRoute` 注册，由 Shell 以 `GoToAsync("route?...")` 导航。
`AppShell.Navigated` 同时承担两件事：记录导航耗时到 `perf.log`、播放 26px 定向滑入过场。

### 启动时的设置恢复

`SettingsManager.Restore()` 从 `settings.json` 读取 `UserSettings`。如果读取失败，
内存保留默认值并置 `_loadFaulted`，后续 `Persist()` 会先重试恢复，避免把用户的设置整份覆盖。
`RuntimeConfigurator.Start()` 再把设置下发给对话、天气、生理、语音、通知、玻璃效果、
官方通道、插件宿主与工具管理器；设置页保存后调用同一入口，因此运行时行为与落盘设置一致。

---

## 4. 导航与页面宿主

- **主窗口**：`AppShell` + `TitlePage`，常规页面在 Shell 导航栈内。
- **独立工具窗**：`PageWindowService` 把关系网、记忆搜索等页面包进 `ToolWindow`，
  同一 key 只开一扇。这样 `Application.Current.Windows.FirstOrDefault(...)` 这类
  「找主窗口」的代码不会误拿工具窗。
- **桌宠窗（Windows）**：`PetLayeredWindow` 是无边框分层窗口，按像素 alpha 把立绘贴到桌面；
  `PetPage` 在 Windows 上只保留头顶对话小窗，Android 上仍是全屏问答页。
- **设置页两级结构**：`SettingsPage` 只列分类，`SettingsSectionPage` 承载一个分类下的卡片。
  `SettingsCatalog` 是分类/卡片结构的唯一事实来源。
- **页面转场**：`AppShell` 在 `Navigated` 后调用 `Helpers.Motion.PageIn`；
  首次进入标题页不播放。前进从右、后退从左，时长压在 260ms 以内。

导航参数统一放在查询串里（如 `newrun?edit=1`、`studio?id=...`、`pluginSettings?id=...`）。
ViewModel 通过 `IQueryAttributable` 或页面的 `ApplyQueryAttributes` 接收，
JSON 参数需要 URL 转义。

---

## 5. 存储层

`StorageProvider` 是所有 JSON 的唯一落盘入口：

- 根目录：`FileSystem.AppDataDirectory/WarmAsBefore`，与 `App.RootDirectory` 同值。
  Windows 通常位于 `%LOCALAPPDATA%` 下；Android 位于应用私有目录。
- 存储键是**相对键**，例如 `settings`、`saves/<id>`、`plugins/<id>/settings`；
  `StorageProvider` 自动补 `.json`，并拒绝绝对路径、盘符、`.` 与 `..`。
- 写入使用「同目录临时文件 + 原子替换」，重试 5 次，避免读到半截 JSON。
- 读取失败（JSON 损坏）时先把原文件复制为 `<name>.corrupt-<UTC 时间>.json`，再抛出；
  调用方决定是否使用默认值。
- 每次保存/删除都会触发 `Changed` 事件，云存档据此收集待上传的键。

### 主要存储布局

| 键 / 目录 | 内容 |
|---|---|
| `settings` | `UserSettings`：AI 端点、主题、窗口、语音、天气、插件钩子开关等 |
| `characters` | `CharacterLibrary` 的角色资料与状态 |
| `gameengine` | `GameEngine` 的运行时状态 |
| `saves/<saveId>` | 存档快照：`state`、`meta`、`char_state`、`char_states`、`sub_profiles`、最近 60 条聊天 |
| `memories/<scope>` | 全量对话记忆库（`MemoryVault`） |
| `human_memories` / `human_memories/<saveId>` | 人类记忆；按存档隔离，开启共用时回到单文件 |
| `diary_<charId>` | 日记条目 |
| `plugins/<id>/plugin.json` | 插件清单（文件，不经 `StorageProvider`） |
| `plugins/<id>/settings` | 插件自己的设置（`settings.json`） |
| `plugins/<id>/files/` | 插件文件区（单文件 1MB、合计 8MB） |
| `plugins/state` | 插件启停状态 |
| `plugins/permissions` | 授权策略、询问答案、权限留痕 |
| `plugin_logs/<id>.log` | 插件崩溃、权限拦截与 JS 异常日志 |
| `assets/` | 立绘、背景、音频、表情包、形象包 |
| `cg/` | CG 目录与解锁状态 |
| `showcase/` | 展示案脚本与素材 |
| `McpPacks/` | MCP 服务配置与 `tools.json` |
| `battles/` | 外部战斗驱动 |
| `sandbox-native/` | Android 的 proot rootfs 与解释器 |
| `warm_startup.log` | 启动日志（Windows 在 `Documents/`） |
| `perf.log` | 性能打点 |

素材与插件目录直接使用文件系统；其余状态优先走 `StorageProvider`，以便统一迁移与云同步。

---

## 6. 对话管线

`ChatEngine.Send(charId, text, attachments)` 是唯一的对话入口。`RuntimeConfigurator`
在启动与设置变更时把记忆、人设、地图、标签、插件、预判、数值结算等能力接线进来。

```text
ViewModel / 官方通道 / 桥接工具
  └─ ChatEngine.Send
       ├─ Brain.PrepareTurn（可选：本轮预判）
       ├─ 取最近记忆（MemoryVault.Recent）
       ├─ 人类记忆召回（重要性 × 时间衰减 × 复述 × 扰动）
       ├─ Buff / 动态状态 / 异步上下文
       ├─ 插件 beforeSend 改写用户文本
       ├─ 组装 [上下文] + 用户文本 + 附件清单
       ├─ ApiGateway 流式请求（OpenAI 兼容）
       ├─ 插件 afterReply 改写回复
       ├─ 写入 MemoryVault（dialogue）
       ├─ AfterSend（Buff 计时等）
       └─ RunAfterTurnAsync
            ├─ Brain.AfterTurn（期待差/意料之外）
            ├─ 日记、标签、关系网、数值结算
            └─ AfterTurn 委托（插件 afterTurn、场景等）
```

关键边界：

- `beforeSend` 只能改用户侧文本；`afterReply` 只能改模型回复。两者返回空/异常时保留原文。
- 插件 `persona` / `context` 只往提示词里加文字；只有 `persona` 的 `overrides` 会被真正兑现。
- API 未配置时走本地离线回复，不阻塞界面；API 报错时返回可读错误文案，不写入会话历史。
- 会话按角色 id 缓存在 `ChatEngine` 内，切换存档时由 `MemoryScope` 与 `SaveManager` 负责边界。

### 提示词组成

`RuntimeConfigurator` 提供的 `PersonaText` 与动态上下文按以下顺序拼接：

1. 角色主设定、副设定、世界树/世界观；
2. 用户人设整理出的记忆（由记忆层按需召回，不原样塞入）；
3. 地图、场景、天气、时间、身体状态、标签、关系网；
4. 插件 `persona` 与 `context` 钩子的追加文本；
5. 本轮 Buff 与动态状态；
6. 最近对话与人类记忆召回；
7. 用户输入与附件清单。

具体实现以 `RuntimeConfigurator` 的 `PersonaText`/`ExtraContext` 接线为准；
新增上下文应加在同一处，避免在多个 ViewModel 里各拼一份。

---

## 7. 记忆分层

| 层 | 组件 | 作用 |
|---|---|---|
| 全量对话记忆 | `Modules.AiChat.MemoryVault` | 保存对话、日记、好感记录；按作用域分文件 |
| 人类记忆 | `Services.Memory.HumanMemoryService` | 只存关键点、印象、重要时刻与图片；按重要性、衰减、复述与扰动召回 |
| 标签 | `Services.Memory.MemoryTagService` | 人 / 对话 / 事件 / 地点 / 物的标签与组合检索 |
| 关系网 | `Services.Memory.RelationNetworkService` | 节点与边（等价/联想），后台随对话生长，模型整理需手动触发 |
| 人格记忆 | `Services.Memory.PersonaMemoryService` | 把用户人设与世界树整理成第一人称记忆；AI 不可用时本地分句兜底 |
| 世界树 | `Services.Memory.WorldTreeService` | 世界设定；存档可绑定一份，未绑定时地图入口锁定 |

`MemoryScope` 决定「这份记忆属于哪一局」：默认按 `CurrentSaveId` 隔离；
开启跨存档共用后回到单文件；标题页/角色库没有当前存档时使用共用文件。
换档、新开局、开关切换后必须调用 `MemoryScope.Raise()`，各记忆服务据此清缓存并重载。

---

## 8. 存档、读档与云存档

### 本地存档

`Modules.SaveSystem.SaveManager` 负责：

- `NewRun()`：生成 12 位 `saveId`，清空聊天记录并切换记忆作用域；
- `Commit(label, mode)`：把 `GameState`、角色状态、副设定与最近 60 条聊天写入 `saves/<id>`；
- 读档：恢复 `GameState` 与角色状态，重建聊天记录，`LastLoadNote` 记录自动修复说明；
- 退出前保存：`App.Window.Destroying` 触发，避免异常退出丢进度。

存档结构带 `SchemaVersion`；缺字段的旧档在加载时补默认值并记录迁移说明，不静默失败。

### 云存档

`Services.CloudSync.CloudSyncService` 把本地 JSON 镜像到用户自己的 GitHub 仓库：

```text
wab/devices/{deviceId}/manifest.json     每个键的最后写入时间 + 是否删除
wab/devices/{deviceId}/files/{key}.json  数据本体
```

合并规则：同一个键取 `manifest` 中时间较新的一份；存档再按 `meta.SavedAt` 复核。
每台设备只写自己的目录，不覆盖其他设备的文件。`settings.json`、图片和密钥字段不入库。
`StorageProvider.Changed` 事件是上传触发器，启动后先做一次全量对齐，之后按变更增量上传。

---

## 9. 角色、世界与地图

- `CharacterLibrary`：角色的新建、导入、持久化；所有角色写入 `characters.json`，
  并注册进 `GameEngine.Roster`。
- `CharacterSettingsResolver`：全局设置 → 角色覆写的解析链。需要按角色微调的参数都从这里取值。
- `CharacterSubProfileService`：主设定只读，副设定随时间与状态漂移，留影有界。
- `WorldTreeService`：世界树条目可手写、可由 AI 生成/润色；存档绑定后地图才可用。
- `MapService`：场景图、BFS 最短路、逐段行走、背景图/视频管理；
  `MapActorService` 维护用户与各 AI 角色的位置，并处理多实体拉拽仲裁。
- `SceneDirector`：按 AI 标记、手动选择、时间规则、天气规则切换场景，记录生效来源。

地图与角色位置属于存档状态，换档即换世界。素材文件按角色/场景存放，不写进 JSON。

---

## 10. 插件、工具、技能与 MCP 的边界

| 系统 | 运行位置 | 入口 | 能力边界 |
|---|---|---|---|
| 插件 | 本机 Jint 沙箱 | 插件中心 | 钩子改文本/加人设；宿主函数受「声明 ∩ 授予」限制 |
| 工具 | 内置 C# / 外部 Python/Java 子进程 | 工具管理 | JSON-RPC 2.0；参数与输出过敏感词过滤与信任名单 |
| 技能 | 本地目录（`SKILL.md` + 可选 `tool.json`） | 技能库 | 渐进加载说明书；可执行入口复用外部工具机制 |
| MCP | 外部 stdio / HTTP 服务 | 插件中心的 MCP 列表 | 只认真正的数据包；握手取回工具清单 |

四者共用 `ToolManager` 的执行链，但导入、列表与授权彼此独立。插件目录不会被 MCP 列表收编；
ZIP/文件夹导入会先验身，认成插件就拒绝并指路插件中心。

插件系统的完整字段、钩子、JS API 与权限模型见 [`PLUGINS.md`](PLUGINS.md)。

---

## 11. 线程、窗口与 UI 规则

- **UI 线程**：页面绑定、集合变更、弹窗、导航必须在主线程；`MainThread.BeginInvokeOnMainThread`
  用于从后台回调回 UI。
- **后台线程**：文件 IO、网络、图片解码（除 WinUI/WIC 明确要求主线程的部分）、
  插件 JS 执行、外部工具子进程。
- **日志**：`App.WriteLog` 只入队，后台批量落盘；`PerfLog` 同样只入队，避免打点本身造成卡顿。
- **原子写**：`StorageProvider` 的临时文件 + 替换策略保证读到的永远是完整 JSON。
- **窗口**：主窗口透明/毛玻璃由 `WindowBackdrop` 处理；置顶策略与一次性抬前台分开；
  关闭主窗口由 `WindowCloseGuard` 在 WinUI `AppWindow.Closing` 阶段拦截并询问其他窗口。
- **动效**：悬停只变色；按下位移 2.4px；不做假加载；设置类弹窗保持原有样式。
  页面转场统一走 `Helpers.Motion`，不在页面里各写一套。

### XAML 约束

- `Border.Background="{DynamicResource ...}"` 会触发 `XC0009`；
  填充用 `BackgroundColor` 或内层 `BoxView.Color`。
- 页面根背景统一使用 `{DynamicResource PageBgBrush}`，否则窗口透视/背景图会被页面盖住。
- `CollectionView` 批量替换使用 `RangeObservableCollection.ReplaceRange/AddRange`，
  避免嵌套改集合异常与多次布局。

---

## 12. 平台差异

| 能力 | Windows | Android |
|---|---|---|
| 窗口 | WinUI 3、无 MSIX、自包含 WindowsAppSDK | 单 Activity + MAUI 页面 |
| 桌宠 | 分层窗口贴桌面 + 头顶小窗 | 全屏问答页 |
| 多窗口 | `ToolWindow`、关系网/搜索独立窗 | 无多窗口，退回页内导航 |
| 透明/毛玻璃 | `WindowBackdrop`（DWM + Acrylic） | 不支持，退回纯色/图片背景 |
| 外部运行时 | Python/Java 由系统安装 | proot + 自带 rootfs |
| 系统定位 | 经常不可用，降级 IP 估算 | GPS/网络定位 |
| 文件选择 | WinRT FolderPicker（可装文件夹） | 只能选 zip 安装 |
| 浏览器 | 系统默认浏览器 | 系统浏览器 / Custom Tabs |

平台判断优先使用 `OperatingSystem.IsWindows()` / `DeviceInfo.Idiom`，
不要用屏幕宽度猜设备类型。

---

## 13. 扩展步骤

### 新增一个服务

1. 放到 `Services/<功能>/` 或对应 `Modules/<Name>/`。
2. 在 `Composition/Registration.*.cs` 注册；跨模块共享用 Singleton，页面级用 Transient。
3. 需要设置/运行时状态时，在 `RuntimeConfigurator` 注入并下发，不在页面里直接改别的服务。
4. 需要落盘时使用 `StorageProvider` 的相对键，并考虑云同步与数据包导出。

### 新增一个页面

1. 在 `Views/<分组>/` 建 XAML + code-behind，`ViewModels/<分组>/` 建 VM。
2. 页面与 VM 分别注册到 `Registration.Pages.cs` / `Registration.ViewModels.cs`（Transient）。
3. 在 `AppShell.RegisterRoutes()` 注册路由；参数走查询串并做 URL 转义。
4. 页面根背景用 `PageBgBrush`；新 UI 使用 `DesignSystem` 的样式键与 Token。
5. 导航离开需要确认时，在页面 `OnBackButtonPressed`/关闭事件里处理，不在 VM 里直接弹窗。

### 新增一个模块

1. 在 `Modules/<Name>/` 建服务与模型；模块内部可以互相引用，跨模块走注入。
2. 在 `Registration.Modules.cs` 注册；如需工具、插件钩子或对话上下文，在 `RuntimeConfigurator` 接线。
3. 需要独立存储时定义自己的键前缀（如 `showcase/`、`battles/`），不要与全局键混用。
4. 有外部进程/协议时，先定义契约与失败兜底，再写 UI。

### 修改存储格式

1. 在模型上新增字段并给默认值，保证旧档可反序列化。
2. 需要迁移时在对应服务的 `Restore/Load` 路径做一次性修复，并记录 `LastLoadNote`。
3. 更新 `docs/ARCHITECTURE.md` 的存储布局与 `DEVELOPMENT.md` 的验证清单。
4. 数据包导出会递归清理密钥字段；新增密钥字段必须使用可识别的命名（`*Key`、`*Secret` 等）。

---

## 14. 已知限制

- Windows 构建必须使用 `-p:RuntimeIdentifierOverride=win10-x64`（或 `win-x64` 的调试变体），
  直接 `-r win-x64` 会触发 WindowsAppSDK #3337，产物启动后没有窗口。
- 本机缺少 Android SDK 时 `net10.0-android` 分支无法还原；Android 行为只能在具备 SDK 的环境验证。
- AI 端点不可用时，对话走离线回复，生图/识图/语音等依赖真实模型的功能不可用。
- 插件 JS 是同步沙箱：不支持 `await`/`require`/`import`，网络与工具调用按同步语义返回。
- 本应用不内嵌 WebView：插件页面与 HTML 设置页一律交给系统浏览器。
- 云存档依赖用户自己的 GitHub 仓库与令牌；令牌失效时只影响同步，不影响本地存档。
