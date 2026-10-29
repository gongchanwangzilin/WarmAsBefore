# 组件说明

按目录逐项说明各组件的职责、关键协作者与使用边界。
分层、启动链路和数据流见 [`ARCHITECTURE.md`](ARCHITECTURE.md)；
插件与外部工具的契约见 [`PLUGINS.md`](PLUGINS.md)。

约定：

- **Singleton** 指在 `Composition/` 注册为单例；**Transient** 指每个页面/ViewModel 新实例。
- 文件路径相对仓库根目录（`src/WarmAsBefore/`）。
- 「入口」指常规调用方；没有列出的内部辅助类不单独展开。

---

## 1. 入口与组合根

| 组件 | 职责 |
|---|---|
| `App.xaml` / `App.xaml.cs` | 合并色板与样式、注册转换器、全局日志、窗口创建、退出前自动存档、`--open` 诊断路由 |
| `AppShell.xaml` / `AppShell.xaml.cs` | Shell 路由表、导航耗时打点、页面转场 |
| `MauiProgram.cs` | 组合根入口：挂 MAUI/CommunityToolkit，按层调用 `Composition/*` |
| `Composition/Registration.DesignSystem.cs` | 设计系统服务 |
| `Composition/Registration.Core.cs` | 存储、设置、引擎、角色、记忆、媒体、窗口等基础服务 |
| `Composition/Registration.Modules.cs` | 业务模块服务 |
| `Composition/Registration.Character.cs` | 角色包、形象包、换装与头像裁剪 |
| `Composition/Registration.Plugins.cs` | 插件宿主、权限、技能库、沙箱、工具管理器 |
| `Composition/Registration.ViewModels.cs` | 全部 ViewModel（Transient） |
| `Composition/Registration.Pages.cs` | 全部页面（Transient） |
| `Platforms/Windows/*` | WinUI 宿主、主窗口、桌宠分层窗口 |
| `Platforms/Android/*` | Android 宿主与 MainActivity |

---

## 2. Models

纯数据与数学辅助，不发起 IO，不引用服务。

| 文件 | 主要类型 | 内容 |
|---|---|---|
| `Models/State.cs` | `GameState`、`UserSettings`、`SavePermissions`、`SaveSlot`、`ChatRecord`、`AiEndpointProfile`、`AiProvider`、`AiProviderModel`、`WidgetPref`、`CycleRule`、`PhysiologyState`、`AffectionTick` | 全局运行时状态、设置、存档权限、AI 端点与小组件偏好 |
| `Models/Character.cs` | `CharacterProfile`、`CharacterState`、`CharacterData`、`CharacterSubProfile`、`BrainState`、`PhysiologicalData`、`BackgroundCrop`、`CharacterWorldview`、`CharacterInitialStats`、`CharacterVoice`、`CharacterModelBinding`、`SubProfileSnapshot` | 角色资料、状态、立绘/背景、副设定、语音与模型绑定 |
| `Models/CharacterOverrides.cs` | `CharacterOverrides` | 角色级设置覆写 |
| `Models/Chat.cs` | `ChatMessage`、`ChatAttachment`、`ToolCall`、`ChatSession`、`AiEndpoint` | 对话消息、附件、工具调用、会话与端点 |
| `Models/ChatMessage.cs` | `ChatItem` | 聊天界面展示项 |
| `Models/Data.cs` | `MemoryEntry`、`DiaryNote`、`CgRecord`、`AchievementDef`、`PackInfo`、`WeatherReading`、`TimeOfDayInfo`、`McpToolDef`、`ScenarioResult`、`PlotBranch`、`PlotCompleteness` | 记忆、日记、CG、数据包、天气、MCP 与小说分析 |
| `Models/HumanMemory.cs` | `HumanMemoryKind`、`HumanMemoryEntry`、`HumanChainHit`、`HumanChainResult`、`HumanMemoryHit`、`HumanMemoryMath`、`SentenceTag` | 人类记忆条目与召回排序 |
| `Models/Relation*.cs` | `RelationNode`、`RelationEdge`、`RelationGraph`、`RelationNet`、`RelationParams`、`RelationStats`、`RelationLayout` | 关系网节点/边、布局、参数与统计 |
| `Models/MemoryLayer.cs` | `MemoryLayer`、`MemoryLayers` | 记忆分层枚举与元数据 |
| `Models/MemoryTag.cs` | `MemoryTag` | 标签与指向对象 |
| `Models/MapData.cs` | `MapDefinition`、`MapLocation`、`MapScene`、`MapTransit`、`MapWaypoint`、`MapEdge` | 地图定义、场景与连线 |
| `Models/MapActor.cs` | `MapActor`、`PullRequest`、`PullArbitrationResult` | 地图实体与拉拽仲裁 |
| `Models/SceneLibrary.cs` | `SceneLibraryEntry`、`SceneMode` | 场景库与场景模式 |
| `Models/ScenePlacement.cs` | `ScenePlacement`、`NormPoint`、`NormLine`、`NormSphere`、`LandingKinds`、`ScaleMethods` | 场景落脚/缩放标定 |
| `Models/PortraitCalibration.cs` | `PortraitCanvas`、`PortraitCalibration`、`PortraitCalibrationMath` | 立绘 1080×1920 逻辑画布与标定数学 |
| `Models/ShopItem.cs` | `ShopItem`、`GameRecord`、`CharacterBuff` | 商品、游戏记录、Buff |
| `Models/Showcase.cs` | `ShowcaseScript`、`ShowcaseStep`、`ShowcaseSprite` | 展示案脚本与步骤 |
| `Models/Battle.cs` | `BattleState`、`BattleCharacter`、`SkillData`、`BattleLogEntry` | 战斗页数据 |
| `Models/BoardCell.cs` | `BoardCell` | 棋盘格 |
| `Models/EmotionMix.cs` | `EmotionMix`、`EmotionPart`、`EmotionWords` | 情绪混合与表情词 |
| `Models/HumanMemory.cs` 等 | 见上 | — |
| `Models/WorldbookData.cs` | `WorldbookEntry`、`WorldbookCharacter`、`WorldbookMode`、`WorldbookGenerationResult` | 世界树/世界书与生成结果 |
| `Models/WeChatContact.cs` | `WeChatContact`、`WeChatContacts` | 微信联系人 |
| `Models/ForceCastEntry.cs` | `ForceCastEntry` | 点名角色与初始位置 |
| `Models/LingshuImport.cs` | `LingshuMemoryImport`、`LingshuArchive` | 灵枢导入结构 |
| `Models/ModelPricing.cs` | `ModelPrice`、`TokenUsageRecord` | 模型单价与用量 |
| `Models/AudioAssets.cs` | `MusicItem`、`BackgroundItem`、`SoundAssign` | 音乐/背景素材与声音分配 |
| `Models/RangeObservableCollection.cs` | `RangeObservableCollection<T>` | 批量替换/追加，单次集合通知 |

---

## 3. Services

### 3.1 Core（`Services/Core/`）

| 组件 | 职责 | 主要协作者 |
|---|---|---|
| `StorageProvider` | 所有 JSON 的唯一入口：相对键、原子写、损坏备份、`Changed` 事件 | 云同步、存档、设置 |
| `SettingsManager` | 读取/迁移/保存 `UserSettings`；读取失败时避免默认值覆盖 | `StorageProvider`、`RuntimeConfigurator` |
| `GameEngine` | 当前 `GameState`、角色名册、当前存档 id、地图解锁判断、运行状态恢复 | `SaveManager`、各 ViewModel |
| `RuntimeConfigurator` | 把设置与运行时能力下发给对话、天气、生理、语音、通知、官方通道、插件与工具 | 几乎所有模块 |
| `NotificationService` | 应用内通知与系统通知，统一走主线程 | 插件、官方通道、自动化 |
| `AudioController` | 左键/右键/按键音与事件音效；按 `audioassign` 播放 | `MaterialLibrary` |
| `PageWindowService` | 把页面搬进独立 `ToolWindow`，同一 key 只开一扇 | 关系网、记忆搜索 |
| `AppBackgroundService` | 页面背景：纯色 / 图片 / 窗口透视；维护 `PageBgBrush` | 设置页、主题 |
| `ScreenBlackoutService` | 黑屏时刻；状态挂在存档上，输入与聊天不受影响 | 主界面、聊天 |
| `WidgetRegistry` | 主界面顶栏/左栏小组件的出厂定义与顺序 | `MainGameViewModel` |
| `PerfLog` | 轻量性能打点，后台批量写 `perf.log` | 导航、页面构建 |
| `LocalizationService` | 运行时切换语言并刷新绑定文本 | `Resources/Strings/*.resx` |
| `ExternalRuntimes` | 外部运行时（Python/Java/Node 等）的台账、探测与安装建议 | 插件体检、工具管理 |
| `DownloadBackupService` | 启动/导出后把数据包复制到下载目录（Android），桌面 no-op | 数据包导出 |
| `GpuPreferenceService` | 在 Windows 图形设置里登记高性能 GPU | 设置页 |
| `WindowBackdrop` | WinUI 客户区透明 + Acrylic/毛玻璃 | 主窗口 |
| `WindowTopmost` | 持久置顶策略与一次性抬前台 | 通知、授权弹窗 |
| `WindowTaskbarFocus` | 有焦点时压在任务栏之上，失焦让位 | 主窗口 |
| `WindowCloseGuard` | 关闭主窗口前询问其他窗口如何处理 | 主窗口生命周期 |

### 3.2 Character（`Services/Character/`）

| 组件 | 职责 |
|---|---|
| `CharacterLibrary` | 角色新建/导入/持久化；`characters.json`；注册进 `GameEngine.Roster`；支持整包与单角色包 |
| `CharacterLibrary.MemoryClone` | 复制角色时连同记忆一起复制；引用重映射、仅当前作用域 |
| `CharacterSettingsResolver` | 全局设置 → 角色覆写的解析链；未覆写时与全局一致 |
| `CharacterSubProfileService` | 主设定只读、副设定漂移、留影有界、提示词文本生成与回正 |
| `CharacterBackgroundService` | 角色背景（图片/GIF/视频）的统一取法与适配方式 |
| `CharacterMask` | 未自我介绍的角色显示 `???` |
| `CharacterPackService` | 单角色 zip 导出与分享 |
| `MaterialLibrary` | 音乐库/背景库：导入、重命名、删除、试听、SHA1 去重、声音分配 |
| `AvatarCropService` | 头像裁剪：预览、拖动/缩放、归一化偏移、落盘 |
| `PortraitCalibrationService` | 立绘标定的存取与判定；按素材相对路径索引 |
| `PortraitNormalizer` | 把非标准素材重采样成 1080×1920 PNG |
| `SceneCalibrationService` | 场景标定的统一入口；导入强制、运行路径弹遮罩、单飞 |
| `GenderLibrary` | 性别定义、称呼、周期与自定义逻辑 |
| `LibidoService` | 性欲系统；设定优先于周期；只对点名角色生效 |
| `PhysiologyService` | 冷/热/渴/饿/想念；随真实时间与天气推移，随存档走 |
| `MentalHealthService` | 心理状况目录与每角色状态；严重度随经历变化 |
| `BrainAnticipationService` | 期待差/多巴胺与意料之外；默认本地跑，可选小模型预判 |
| `LingshuImporter` | 灵枢 AI 记忆 JSON 导入世界树 |

### 3.3 Memory（`Services/Memory/`）

| 组件 | 职责 |
|---|---|
| `MemoryScope` | 记忆作用域：按存档隔离或跨存档共用；`Raise()` 通知各服务重载 |
| `HumanMemoryService` | 人类记忆的存与取；重要性 × 时间衰减 × 复述 × 扰动 |
| `MemoryTagService` | 标签层：打标签、组合检索、注入标签清单 |
| `RelationNetworkService` | 关系网：节点/边、共现生长、模型整理、JSON 视图 |
| `PersonaMemoryService` | 用户人设与世界树 → 第一人称记忆；AI 不可用时本地分句 |
| `WorldTreeService` | 世界树条目读写、AI 生成/润色、存档绑定 |
| `EmbeddingService` | 数值近邻检索；量化模型或本地统计兜底，标明来源 |
| `MemoryOverviewService` | 汇总五层记忆的规模与最近变化 |
| `CostEstimatorService` | 记录真实用量与单价，比较「现在总结/不总结」的成本 |

### 3.4 Media（`Services/Media/`）

| 组件 | 职责 |
|---|---|
| `ImageGenerationService` | OpenAI 兼容生图；结果落到 `generated/` 并返回绝对路径；未配置时明确失败 |
| `VisionService` | 独立配置的识图模型；OpenAI vision 消息格式；用于表情包描述等 |
| `SpeechService` | TTS/STT；按角色取语音覆写；当前角色嗓子 |
| `StickerLibrary` | 表情包导入、描述、按聊天入口取用 |
| `ImageProbe` | 读取图片原始像素尺寸；主线程解码，按路径+指纹缓存 |

### 3.5 World（`Services/World/`）

| 组件 | 职责 |
|---|---|
| `MapService` | 场景图持久化、BFS 最短路、逐段行走、背景图/视频、导入导出、`【移动:场景】` |
| `MapActorService` | 用户与 AI 角色位置、多实体拉拽仲裁、冲突抑制 |
| `PetService` | Windows 桌宠：收纳主窗口、置顶立绘窗、托盘菜单 |
| `WeChatService` | 已加微信角色列表（`wechat_contacts`）；常驻 NPC |
| `NovelLibrary` | 小说/平行宇宙条目列表、读取、添加、删除 |
| `DataPackageService` | 整目录 zip 导出/导入；密钥字段递归清理；临时区校验后替换 |

### 3.6 Plugins（`Services/Plugins/`）

| 组件 | 职责 |
|---|---|
| `PluginHost` | 扫描/安装/启停/删除插件；执行 JS 钩子；设置、页面、回调服务器、崩溃日志 |
| `PluginPermissions` | 14 项能力、四档风险、三态授权、询问答案与留痕 |
| `PluginDoctor` | 只读体检：身份、设置、页面、依赖、overrides、运行时、钩子与 JS 静态检查 |
| `SkillLibrary` | 技能目录：`SKILL.md` 渐进加载 + 可选 `tool.json` 可执行入口 |

插件字段与 API 见 [`PLUGINS.md`](PLUGINS.md)。

### 3.7 CloudSync（`Services/CloudSync/`）

| 组件 | 职责 |
|---|---|
| `CloudSyncService` | 把本地 JSON 镜像到用户 GitHub 仓库；按设备分目录、逐键时间戳合并 |
| `MemoryReconcileService` | 跨设备合并后让模型挑出近似重复记忆；代码按序号删除 |

### 3.8 顶层几何

| 组件 | 职责 |
|---|---|
| `PlacementMath` | 场景标定的几何与缩放换算：双线、圆球、落脚点 |
| `PlacementValidator` | 素材包定位 JSON 的校验与传输结构（camelCase） |

---

## 4. Modules

### Affection

| 组件 | 职责 |
|---|---|
| `AffectionLevel` | 好感等级：每 500 积分一级，共 10 级；与 0–100 好感门控独立 |
| `AffectionLevelUpService` | 等级提升动画载荷；单实例，避免并发重叠 |

### AiChat

| 组件 | 职责 |
|---|---|
| `ChatEngine` | 对话管线：记忆召回、上下文、流式请求、工具调用、插件挂点、回合结算 |
| `MemoryVault` | 全量对话记忆库、日记、好感记录；按 `MemoryScope` 分文件 |

### ApiManager

| 组件 | 职责 |
|---|---|
| `ApiGateway` | OpenAI 兼容端点的统一请求层；超时、流式、错误归一化、模型列表 |
| `ChatTurn` | 一次请求的临时状态（消息、工具调用、用量） |

### Automation

| 组件 | 职责 |
|---|---|
| `TaskOrchestrator` | 周期任务编排：启动/停止后台循环 |
| `DailyDiaryWriter` | 每日日记：启动补写、每分钟检查跨天、汇总对话与好感时刻 |

### Battle

| 组件 | 职责 |
|---|---|
| `BattleData` / `BattleState` | 战斗页数据模型 |
| `IBattleDriver` / `BattleBootData` | 可替换战斗驱动契约；每次响应必须带 `ended` |
| `BattleDriverManager` | 外部驱动优先（`battles/*/battle.json`），内置兜底 |
| `BuiltinBattleDriver` | 进程内回合制；带立绘、技能、好感联动 |
| `ExternalBattleDriver` | Python/Java 子进程，stdin/stdout JSON-RPC 2.0 |
| `BattleStateParser` | 把驱动 JSON 解析成 UI 模型；缺 `ended` 视为结束 |
| `BattleSystem` | 系统级极简战斗，供工具/MCP 调用；重启即丢 |
| `BattleTools` | 把战斗能力注册为内置工具 |

### Cg

| 组件 | 职责 |
|---|---|
| `CgStore` | CG 目录（`cg/catalog.json`）与解锁状态；`【CG:文件名】` 触发 |
| `CgViewPayload` | 全屏 CG 播放载荷 |

### DataPack

| 组件 | 职责 |
|---|---|
| `PackImporter` | 素材包/数据包导入；标定待办与 sidecar 校验问题一并返回 |

### GameModule

| 组件 | 职责 |
|---|---|
| `MiniGameEngine` | 六种棋牌规则：五子棋、国际象棋、中国象棋、斗兽棋、飞行棋、贪吃蛇 |
| `ChessBrainService` | 可选云端棋力脑；超时/失败回退本地启发式 |
| `GameSkillTracker` | 熟练度、认真度、难度决策与衰减 |

### Market

| 组件 | 职责 |
|---|---|
| `ShopService` | 1000+ 内置商品、钱包、购买、场景券解锁、AI 生成商品、送礼/使用与 Buff |
| `GiftPanelService` | 聊天送礼/使用面板的共享逻辑，主界面/微信/QQ/官方通道复用 |

### Mcp

| 组件 | 职责 |
|---|---|
| `McpOrchestrator` | MCP 服务器编排、工具调用、自动确认、总开关 |
| `McpProtocol` | stdio/HTTP 接入配置与 JSON-RPC 协议 |
| `McpDoctor` | 连接失败原因翻译成「改哪里、怎么改」，不写状态、不执行工具 |

### MinecraftBridge

| 组件 | 职责 |
|---|---|
| `MaidBridgeService` | 与《东方小女仆》模组的桥：UDP 发现 + TCP NDJSON；token 一次性、默认只回环 |
| `MaidBridgeService.Think` | 场景触发思考；是否触发与冷却由插件设置决定 |
| `MaidBridgeService.World` | 把 Minecraft 见闻接入当前存档绑定的世界树 |
| `MaidBridgeModels` | 插件设置、世界快照、绑定关系 |
| `MaidBridgeTools` | 把桥能力暴露成 `maid_*` 聊天工具 |

### NovelImport

| 组件 | 职责 |
|---|---|
| `NovelAnalyzer` | 小说 → 平行宇宙：分段分析、剧情完整性、关键事件时间线 |

### Outfit

| 组件 | 职责 |
|---|---|
| `FigurePackModels` | 形象包：部件、服装、配色、动作与帧 |
| `FigurePackService` | 形象包列表、导入/删除、绑定到角色 |
| `FigureComposer` | 按目标尺寸合成当前服装/表情/动作 |
| `FigureMotionPlayback` | 动作查找与按时间取帧 |
| `FigureCompat` | 新形象包不可用时回退旧 `SpriteMap` 立绘 |
| `OutfitAssetPipeline` | 素材表切分、部件种子、动作导入 |
| `WalkCycle` | 行走节拍与位移 |

### RealChat

| 组件 | 职责 |
|---|---|
| `OfficialChatBridge` | 根据设置启停 QQ/微信通道；消息交给 `ChatEngine`；文本指令「送礼/使用」 |
| `QqBotChannel` | QQ 官方机器人 WebSocket 收、HTTP 发；仅单聊 |
| `WeChatOfficialChannel` | 微信公众号回调端口 + 客服消息 API；仅单聊 |
| `IOfficialChannel` | 通道契约与消息模型 |

### RealWorld

| 组件 | 职责 |
|---|---|
| `WeatherProvider` | 天气数据源（Open-Meteo / wttr / 自定义）；密钥只随自定义源 |
| `TimeProvider` | 游戏内时间、时段与真实时间映射 |
| `LocationService` | GPS 定位，失败降级 IP 估算；城市搜索 |
| `CityCatalog` | 可选城市（名字给人看，天气按坐标） |
| `PhysiologicalTracker` | 把真实时间/天气推入生理状态 |
| `PermissionBroker` | 定位等运行时权限的申请与状态 |
| `WeatherFormat` | 天气源标识与展示格式 |

### Sandbox

| 组件 | 职责 |
|---|---|
| `SandboxPolicy` | 工具执行链的门面：敏感词、信任名单、加密落盘 |
| `SensitiveFilter` | 密钥/凭据特征与已配置密钥指纹过滤 |
| `TrustStore` | 工具信任名单（按路径哈希），默认不允许读模型数据 |
| `CryptoStore` | AES-GCM + HMAC-SHA256 加密落盘；Windows 用 DPAPI 保护主密钥 |

### SaveSystem

| 组件 | 职责 |
|---|---|
| `SaveManager` | 新局、提交、读档、退出前保存、聊天记录、结构版本迁移 |

### Scene

| 组件 | 职责 |
|---|---|
| `SceneDirector` | 场景切换：AI 标记、手动、时间规则、天气规则；记录生效来源 |
| `SceneTimeGate` / `SceneWeatherGate` | 被动触发的条件门 |
| `SceneOption` / `SceneApplyResult` | 候选场景与生效结果 |

### Screen

| 组件 | 职责 |
|---|---|
| `ScreenOrientationService` | 屏幕方向：auto/landscape/portrait；Android 实际锁屏；3 秒确认防呆 |
| `DeviceInfoService` | 设备/平板判定；Windows 恒非平板 |

### Showcase

| 组件 | 职责 |
|---|---|
| `ShowcaseStore` | 展示案脚本与临时素材；`showcase/scripts.json` |

### Tools

| 组件 | 职责 |
|---|---|
| `ToolManager` | 内置/外部工具统一入口；JSON-RPC、可见性、工具结果广播 |
| `ToolManager.World` | 世界向工具：身体、钱包、心理、记忆链、生图、建角色 |
| `ToolDefinition` | 工具语言、参数 schema、外部清单 |
| `ToolSession` | 长驻 Python/Java 子进程；newline 分帧 JSON-RPC 2.0 |
| `RuntimeManager` | Python/Java 运行时检测与状态 |
| `LinuxSandboxRuntime` | Android proot 用户态沙箱；guest 解释器 |
| `RootfsInstaller` | 下载并安装 Python/JRE rootfs，进度上报 |
| `SandboxNativeHelper` | 把 APK assets 里的 proot 复制到可执行目录并 chmod |

### Update

| 组件 | 职责 |
|---|---|
| `UpdateService` | GitHub releases 轮询、镜像测速、sha256 校验、重启替换 |

### Worldbook

| 组件 | 职责 |
|---|---|
| `WorldbookGenerator` | 根据描述生成世界树/角色 JSON；可生成封面 |

---

## 5. ViewModels

### Main

| 组件 | 职责 |
|---|---|
| `TitleViewModel` | 标题页选门、启动预载、一次性开机动画 |
| `SetupViewModel` | 首次配置：语言、主题、AI 端点等分步选项 |
| `CharacterSelectViewModel` | 角色选择、存档摘要、新建/开局入口 |
| `MainGameViewModel` | 主界面：对话、立绘、状态栏、送礼面板、自动保存、导航 |
| `MainGameViewModel.Widgets` | 顶栏/左栏小组件显示、顺序与落盘 |
| `PhoneViewModel` | 手机桌面：微信、地图、相册、商店、战斗入口 |
| `PhoneViewModel.Home` | 手机桌面小组件与布局 |

### Character

| 组件 | 职责 |
|---|---|
| `CharacterLibraryViewModel` | 角色库：列表 + 预览，进入工作室 |
| `CharacterSelectModels` | 角色选择卡片展示模型 |
| `CharacterStudioViewModel` | 工作室：立绘舞台 + 设定/状态/素材/记忆/关联/说句话 |
| `CharacterStudioViewModel.Bindings` | 共享数值、CG、世界树、语音/模型绑定 |
| `NewCharacterViewModel` | 新建角色向导：基本 → 人设 → 形象 → 世界观 |
| `CharacterLibraryViewModel` | 角色库列表与预览 |
| `AvatarCropViewModel` | 头像裁剪取景框、缩放、偏移 |
| `FigurePackViewModel` | 形象包列表、导入/删除、绑定 |
| `GalleryViewModel` | 相册/回忆录：CG、好感时刻、对话、日记、多层记忆 |
| `MaterialsViewModel` | 背景库/音乐库与声音分配 |
| `OutfitViewModel` | 换装列表与 AI 生成立绘 |
| `PortraitCalibrationViewModel` | 立绘标定预览与保存 |
| `SubProfileViewModel` | 主/副设定对比与回正 |

### Settings

| 组件 | 职责 |
|---|---|
| `SettingsViewModel` | 设置页状态与分组卡片 |
| `SettingsViewModel.Extras` | 通知、界面组件、角色内容开关 |
| `SettingsCatalog` | 分类/卡片结构的唯一事实来源 |
| `SettingsCardFilter` | 搜索时按卡片过滤 |
| `PluginCenterViewModel` | 安装/启停/删除/试跑/体检/日志/页面/AI 工坊 |
| `PluginPermissionsViewModel` | 权限台：能力 × 声明 × 策略 × 留痕 |
| `SaveRulesViewModel` | 存档高级设置：插件/工具/角色白名单 |
| `MentalHealthViewModel` | 心理状况目录与角色状态 |

### Memory

| 组件 | 职责 |
|---|---|
| `MemoryGraphViewModel` | 关系网绘制、节点/边编辑、参数调整、模型整理 |
| `MemorySearchViewModel` | 按标签、感觉、日期、全文检索记忆，并说明命中理由 |
| `MemoryManageViewModel` | 关系网入口、标签、人类记忆、副设定的集中管理 |

### Story

| 组件 | 职责 |
|---|---|
| `NewRunViewModel` | 新开一局/本局设定：世界树、用户人设、人格记忆初始化 |
| `ForceCastViewModel` | 点名角色、指定初始场景、强制加微信与进入剧情 |
| `WorldbookViewModel` | 世界树库：手写、AI 生成、润色、绑定 |
| `NovelSelectViewModel` | 小说库列表 |
| `NovelWorldViewModel` | 平行宇宙聊天页 |
| `SceneLibraryViewModel` | 场景库与场景模式 |
| `SceneCalibrationViewModel` | 场景标定：落脚几何/缩放/平移 |
| `ShowcaseListViewModel` | 展示案列表 |
| `ShowcaseEditViewModel` | 展示案编辑：角色、步骤、临时素材 |
| `ShowcasePlayViewModel` | 展示案播放：打字机、立绘、CG |

### Chat

| 组件 | 职责 |
|---|---|
| `WeChatViewModel` | 微信/QQ 会话：联系人、消息、表情包、送礼面板 |
| `QqForumViewModel` | QQ 论坛：本地种子帖 + 用户帖子 |

### Games

| 组件 | 职责 |
|---|---|
| `GameViewModel` | 六种小游戏：棋盘、走子、AI、技能/难度、聊天气泡 |
| `MapCanvasViewModel` | 地图画布：无限画布、浏览/编辑/连线、折点与备注 |
| `BattleViewModel` | 战斗页：驱动状态、技能、日志、90 秒看门狗 |
| `AffectionLevelUpViewModel` | 好感升级动画 |
| `CgViewViewModel` | 全屏 CG 播放 |

### Save

| 组件 | 职责 |
|---|---|
| `SaveViewModel` | 存档槽位：保存/读取/删除/导出/导入/自动存档 |
| `CloudSavesViewModel` | 云存档浏览器：本机/其他设备/历史，取回与错误提示 |

### Shop

| 组件 | 职责 |
|---|---|
| `ShopViewModel` | 商店分类、分页、购买、AI 生成商品、送礼/使用、Buff 与身体状态 |

---

## 6. Views

页面与 ViewModel 同构分组；页面只做布局、平台事件与导航，业务状态在 VM。

### Main

| 页面 | 说明 |
|---|---|
| `TitlePage` | 方向键选门、Enter 确认、预载名册；点卡片跳过介绍 |
| `SetupPage` | 首次配置：地面上浮一张卡，一次一屏 |
| `CharacterSelectPage` | 角色卡片与开局 |
| `MainGamePage` | 桌面主界面：背景、立绘、状态栏、对话、送礼面板 |
| `MainGamePhoneView` | 手机布局的主界面；上报容器尺寸 |
| `PhonePage` | 手机桌面与机身适配 |

### Character

| 页面 | 说明 |
|---|---|
| `CharacterLibraryPage` | 左列表右预览 |
| `CharacterStudioPage` / `CharacterStudioPanel` | 工作室与六段资料面板 |
| `NewCharacterPage` | 四步向导；窄屏叠列 |
| `AvatarCropPage` | 头像裁剪 |
| `FigurePackPage` | 形象包管理 |
| `GalleryPage` | 相册/回忆录 |
| `MaterialsPage` | 背景/音乐素材库 |
| `OutfitPage` | 换装 |
| `PortraitCalibrationPage` | 立绘标定 |
| `SubProfilePage` | 副设定对比 |

### Settings

| 页面 | 说明 |
|---|---|
| `SettingsPage` | 设置首页：只列分类 |
| `SettingsSectionPage` | 分类子页：3–4 张卡片 |
| `PluginCenterPage` | 插件中心 |
| `PluginPermissionsPage` | 权限台 |
| `PluginSettingsPage` | native 设置表单（按 fields 现场生成） |
| `SaveRulesPage` | 存档白名单 |
| `CloudSavesPage` | 云存档 |
| `MentalHealthPage` | 心理状况 |

### Memory

| 页面 | 说明 |
|---|---|
| `MemoryGraphPage` | Skia 关系网画布 |
| `MemorySearchPage` | 记忆检索 |
| `MemoryManagePage` | 记忆管理分区 |

### Story

| 页面 | 说明 |
|---|---|
| `NewRunPage` | 新局/本局设定；真实进度分段条 |
| `ForceCastPage` | 点名 |
| `WorldbookPage` | 世界树库 |
| `NovelSelectPage` / `NovelWorldPage` | 小说库与平行宇宙聊天 |
| `SceneLibraryPage` / `SceneCalibrationPage` | 场景库与标定 |
| `ShowcaseListPage` / `ShowcaseEditPage` / `ShowcasePlayPage` | 展示案 |

### Chat

| 页面 | 说明 |
|---|---|
| `WeChatPage` | 微信会话；回车发送 |
| `QqPage` | QQ 换皮会话，复用 `WeChatViewModel` |
| `QqForumPage` | QQ 论坛 |

### Games

| 页面 | 说明 |
|---|---|
| `GamePage` | 六种小游戏 |
| `MapPage` | 地图；桌面右键/滚轮事件源 |
| `BattlePage` | 战斗 |
| `AffectionLevelUpPage` | 升级动画 |
| `CgViewPage` | CG 全屏 |
| `PetPage` | Windows 头顶小窗 / Android 全屏问答 |
| `ShopPage` | 商店 |

### Save / Common

| 页面 | 说明 |
|---|---|
| `SavePage` | 存档槽位 |
| `ToolWindow` | 独立工具窗宿主；让「找主窗口」的代码能排除它 |

---

## 7. DesignSystem

| 组件 | 职责 |
|---|---|
| `Theme/ColorPalette.xaml` | 默认色板（经典暖色） |
| `Theme/ColorPaletteSakura.xaml` | 樱花粉 |
| `Theme/ColorPaletteBamboo.xaml` | 翠竹绿 |
| `Theme/ColorPaletteMist.xaml` | 晨雾蓝灰 |
| `Theme/WeChatBrand.xaml` | 微信/QQ 品牌色与聊天页配色 |
| `Theme/ThemeManager` | 切换色板、跟随系统深浅色、刷新资源 |
| `Theme/ThemeManager.Chinese` | 主题中文名映射 |
| `Theme/GlassTokens` | 玻璃/透明度 Token |
| `Theme/MotionPrefs` | 动效偏好（减少动态等） |
| `Theme/MotionTokens` | 动效时长与缓动 |
| `Tokens/DesignTokens` | 间距、尺寸、圆角、时间、排版、触控目标 |
| `Worlds/StyleWorld` | 风格世界：Soft 等整体风格与 Token 发布 |
| `Styles/BaseStyles.xaml` | 基础文本/页面/容器样式 |
| `Styles/NeumorphicStyles.xaml` | 新拟物：凸起/按下/凹陷 |
| `Styles/CardShapes.xaml` | 卡片形状与阴影 |
| `Styles/ButtonShapes.xaml` | 按钮形状 |
| `Styles/TextTypography.xaml` | 标题/正文/说明排版 |

新 UI 必须复用这些键；不要在页面里写死颜色、圆角与阴影。

---

## 8. Controls / Converters / Drawables / Helpers

### Controls

| 组件 | 职责 |
|---|---|
| `AnimatedImage` | 多帧图片/GIF 播放；`TileDrawable`、`CharacterBackdropPresenter` 支持背景平铺与立绘 |
| `LoadingVeil` | 加载遮罩；按真实进度显示，不做假加载 |
| `Toast` / `ToastHost` | 应用内轻提示；`ToastKind` 区分信息/成功/错误 |

### Converters

| 组件 | 职责 |
|---|---|
| `Converters.cs` | `BoolOpacity`、`InverseBool`、`IsNotNull`、`AffectionTint`、`CharInitials`、`YesNoLabel`、`BoolGreenRed`、`SelectedBg` |
| `StringConverters.cs` | `StringNotEmptyConverter`、`StringEmptyConverter` |
| `SpritePathToImageSourceConverter.cs` | 立绘路径 → `ImageSource`；`InvertedBoolConverter`、`ZeroToFalseConverter` |
| `BattleConverters.cs` | `HpColorConverter`、`SkillTypeColorConverter`、`CriticalColorConverter` |

### Drawables

| 组件 | 职责 |
|---|---|
| `GameBoardDrawable`（`Views/Games/`） | 棋盘自绘：线制/格制 |
| `MapCanvasDrawable` | 地图画布：场景卡、连线、折点、背景 |
| `MemoryGraphDrawable` | 关系网：节点、边、生长动画、淡化 |
| `SceneCalibrationDrawable` | 场景标定几何 |
| `CostChartDrawable` | 成本图表 |
| `GraphPalette` | 关系网配色 |

### Helpers

| 组件 | 职责 |
|---|---|
| `Motion` | 页面转场、淡入/滑入 |
| `HoverFeel` | 悬停只变色 |
| `PressFeel` | 按下位移 2.4px |
| `IntroCard` | 标题页介绍卡 |
| `MemoryHighlight` | 记忆命中高亮 |
| `Ui` | 通用 UI 小工具（尺寸、DPI、颜色等） |

---

## 9. 组件协作速查

| 场景 | 调用链 |
|---|---|
| 发一条聊天 | `MainGameViewModel` → `ChatEngine.Send` → `ApiGateway` → `MemoryVault` → `AfterTurn` |
| 打开设置并保存 | `SettingsPage` → `SettingsViewModel` → `SettingsManager.Persist` → `RuntimeConfigurator.Apply` |
| 进入新局 | `NewRunViewModel` → `WorldTreeService` / `PersonaMemoryService` → `SaveManager.NewRun` → `MemoryScope.Raise` |
| 保存进度 | `MainGameViewModel`/`SaveViewModel` → `SaveManager.Commit` → `StorageProvider.Save` → `CloudSyncService` 收集 |
| 插件改人设 | `RuntimeConfigurator.PersonaText` → `PluginHost.PersonaTextAsync` → Jint 钩子 |
| 插件改回复 | `ChatEngine.BeforeSend/AfterReply` → `PluginHost.TransformAsync` → Jint 钩子 |
| 工具执行 | `ToolManager` → `SandboxPolicy` → 内置实现/`ToolSession` → `onToolResult` 广播 |
| 地图移动 | `ChatEngine` 解析 `【移动:场景】` → `MapService` 寻路 → `MapActorService` 仲裁 → `MapPage` 重绘 |
| 关系网生长 | 对话/记忆写入 → `RelationNetworkService` 共现统计 → `MemoryGraphViewModel` 绘制 |
| 生成图片 | `ImageGenerationService` → `generated/` → 业务复制到角色/场景素材 |
| 云存档 | `StorageProvider.Changed` → `CloudSyncService` 增量上传；启动时全量对齐 |
