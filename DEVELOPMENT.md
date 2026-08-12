# WarmAsBefore — 程序员开发文档

> 面向维护/扩展该项目的开发者。记录架构、关键系统、构建运行、常用扩展点。
> 文档位置：`DEVELOPMENT.md`（代码根目录 `src/WarmAsBefore/`）。
> `docs/` 下旧文档已过时，后续可清理。

---

## 1. 项目概览

| 项目 | 说明 |
|------|------|
| **技术栈** | .NET 9 / MAUI (WinUI 3) / CommunityToolkit.Mvvm 8.4.2 |
| **目标平台** | Windows 10 19041+ (net9.0-windows10.0.19041.0) |
| **架构模式** | MVVM + 依赖注入（Singleton Services + Transient Pages/VMs） |
| **设计系统** | 自研 DesignSystem（Tokens/ColorPalette/Neumorphic/Card/Glass/Text/Buttons） |
| **存储** | JSON 文件落盘（`%LOCALAPPDATA%\WarmAsBefore\*.json`） |
| **AI 对话** | OpenAI 兼容端点（配置 `AiEndpoint`），流式回复 + 记忆检索 + buff 注入 |

---

## 2. 目录结构（`src/WarmAsBefore/`）

```
src/WarmAsBefore/
├── App.xaml(.cs)                  # 启动入口、全局资源、日志
├── MauiProgram.cs                 # DI 注册总表（见下）
├── Models/                        # 纯数据 record/class
│   ├── ShopItem.cs                # ShopItem / CharacterBuff / GameRecord
│   └── RangeObservableCollection.cs  # 批量集合（解决 CollectionView 嵌套异常）
├── Views/                         # 纯 XAML 页面（BindingContext = VM）
│   ├── MainGamePage.xaml          # 主界面（直接聊天+地图+商店+设置）
│   ├── WeChatPage.xaml            # 微信聊天模拟页
│   ├── ShopPage.xaml              # 商店页
│   └── ... (Title/CharacterSelect/Settings/Map/Gallery/Outfit/Save/Developer/Game/Pet/Novel/Worldbook/CharacterLibrary)
├── ViewModels/                    # CommunityToolkit.Mvvm [ObservableProperty]/[RelayCommand]
│   ├── MainGameViewModel.cs       # 主界面逻辑（含送礼面板）
│   ├── ModuleViewModels.cs        # WeChatViewModel + 其他子模块 VM
│   └── ShopViewModel.cs           # 商店分页/送礼/buff/身体状态
├── Services/                      # 核心单例服务（MauiProgram 注册 Singleton）
│   ├── CoreServices.cs            # StorageProvider / SettingsManager / GameEngine / NotificationService / AudioController / SpeechService / GlassOverlayService / CharacterLibrary / PetService / MapService
│   └── NovelLibrary.cs
├── Modules/                       # 功能模块（构造函数注入 Services）
│   ├── AiChat/                    # ChatEngine + MemoryVault（对话引擎/记忆/日记/Buff钩子）
│   ├── Market/                    # ShopService + GiftPanelService（商店/送礼/使用/Buff/身体状态）
│   ├── RealChat/                  # OfficialChatBridge（真 QQ/微信官方接入桥、文本指令）
│   ├── RealWorld/                 # Weather/Time/Permission/PhysiologicalTracker（真实世界感知）
│   ├── GameModule/                # MiniGameEngine/ChessBrain/GameSkill（井字棋等小游戏）
│   ├── Automation/                # TaskOrchestrator/DailyDiaryWriter（自动化任务）
│   ├── Mcp/                       # McpOrchestrator（MCP 服务器编排）
│   ├── Worldbook/                 # WorldbookGenerator（世界书生成）
│   ├── DataPack/                  # PackImporter（数据包导入）
│   ├── SaveSystem/                # SaveManager（存档/读档/导出/自动保存）
│   ├── Storage/                   # 底层存储抽象
│   ├── NovelImport/               # NovelAnalyzer（小说导入分析）
│   └── ApiManager/                # ApiGateway（外部 API 网关）
├── Controls/                      # 自定义控件
├── Converters/                    # 值转换器
├── DesignSystem/                  # 设计系统
│   ├── Theme/ColorPalette*.xaml   # 樱花/竹/雾 三主题色板
│   ├── Styles/                    # Base/Neumorphic/Card/Glass/Text/Button
│   └── Tokens/DesignTokens.cs     # 间距/圆角/阴影/动画 Token
├── Resources/                     # 字体/图片等静态资源
├── Platforms/Windows/             # WinUI 3 宿主配置
├── Helpers/                       # 杂项工具
└── Drawables/                     # 自绘图形
```

---

## 3. 依赖注入（`MauiProgram.cs` 关键注册）

```csharp
// Core Singleton Services
GameEngine, SettingsManager, StorageProvider, NotificationService,
AudioController, SpeechService, GlassOverlayService,
CharacterLibrary, PetService, MapService

// Module Singleton Services
WeatherProvider, TimeProvider, PermissionBroker, PhysiologicalTracker,
ApiGateway, ChatEngine, MemoryVault, TaskOrchestrator, DailyDiaryWriter,
McpOrchestrator, WorldbookGenerator, PackImporter, SaveManager,
NovelAnalyzer, MiniGameEngine, ChessBrainService, GameSkillTracker,
ShopService, GiftPanelService, OfficialChatBridge, RuntimeConfigurator, NovelLibrary

// ViewModels (Transient)
TitleViewModel, MainGameViewModel, CharacterSelectViewModel, SettingsViewModel,
PhoneViewModel, WeChatViewModel, MapViewModel, GalleryViewModel, OutfitViewModel,
SaveViewModel, DeveloperViewModel, GameViewModel, WorldbookViewModel,
NovelSelectViewModel, NovelWorldViewModel, CharacterLibraryViewModel, ShopViewModel

// Pages (Transient) — 对应每个 VM
TitlePage, MainGamePage, CharacterSelectPage, SettingsPage, PhonePage, WeChatPage,
MapPage, GalleryPage, OutfitPage, SavePage, DeveloperPage, GamePage, PetPage,
NovelSelectPage, NovelWorldPage, WorldbookPage, CharacterLibraryPage, ShopPage
```

> **规则**：Service = Singleton；Page/VM = Transient。跨模块通信通过构造函数注入 Service，禁止静态引用。

---

## 4. 核心系统详解

### 4.1 AI 对话引擎 — `Modules.AiChat.ChatEngine`

| 成员 | 说明 |
|------|------|
| `ConfigureCharacter(CharacterProfile)` | 设置当前角色人设 |
| `SetRoster(string)` / `SetMapContext(string)` | 注入名册/地图上下文 |
| `Configure(AiEndpoint)` | 切换/配置 AI 端点（BaseUrl/ApiKey/Model/温度/TopP/MaxTokens） |
| `BuffContextProvider` (`Func<string>?`) | **钩子**：每次 `Send` 前调用，返回的文本会作为 `[当前状态标记]\n{buff}` 前缀拼入 user message |
| `AfterSend` (`Action?`) | **钩子**：`Send` 成功存入记忆后调用（ShopService 用它驱动 `TickBuffs`） |
| `Send(charId, text)` | 发送对话：组装 system+memory+buff+user → 调用流式 API → 增量回调 → 存入 MemoryVault → 返回完整回复 |

**Buff 注入流程**：`ShopService.ctor` 注册 `BuffContextProvider = () => BuffContextText()` 与 `AfterSend = TickBuffs`。每轮对话 `TurnsLeft--`，归零移除。

### 4.2 记忆仓库 — `Modules.AiChat.MemoryVault`

- `Store(MemoryEntry)`：写入记忆（category=chat/diary/affection/buff）
- `All(charId, category?)`：检索上下文（ChatEngine.Send 内部自动按相关性取 Top-K）
- `LogAffection(charId, delta, reason, imagePath?)`：好感度变更记录
- `WriteDiary(charId, content, mood)`：每日日记生成（Automation.DailyDiaryWriter 定时触发）

### 4.3 商店与送礼 — `Modules.Market.ShopService`

**状态持久化**：`ShopState`（Coins/Owned/AiItems/Buffs/GameRecords）JSON 落盘。

| 成员 | 说明 |
|------|------|
| `Catalog` / `OwnedItems` | 全量商品 / 已购商品（Owned>0） |
| `ActiveBuffs` | 当前生效的 CharacterBuff 列表（来源=送礼/使用） |
| `BuffContextText()` | 供 ChatEngine 注入：`【当前状态标记】\n- {emoji} {name}（{source}，剩余 {n} 轮）：{desc}` |
| `BodyStatusText()` | 身体状态面板文本：好感/信任/精力/心情/位置/生理周期/当前标记 |
| `BuyAsync(item)` | 扣币 + Owned++ + 存档 |
| `GiftAsync(item)` | 送礼：Owned--，好感 +5(≥50币)/+3，AddBuff(source="送礼")，返回小雨回应文本 |
| `UseAsync(item)` | 使用：Owned--，AddBuff(source="使用")，返回小雨回应文本 |
| `TickBuffs()` | 每轮对话后调用：TurnsLeft--，清理≤0，存档 + UI 通知 |

**内置商品种子**：`BuildSeedCatalog()` 约 20 条（食物/饮品/零食/游戏/玩具），`Id = "s{seq}"`，含 Name/Category/Desc/Price/Emoji/IsTicket/TicketScene/TicketLocation。

**AI 生成商品**：运行时可通过 AI 扩充，标记 `AiGenerated=true`，优先插在 Catalog 顶部。

### 4.4 送礼面板共享服务 — `Modules.Market.GiftPanelService`

| 成员 | 说明 |
|------|------|
| `OwnedItems` | 已购商品只读列表（来自 ShopService） |
| `FindOwnedByName(string)` | 模糊匹配已购商品（用于官方接入文本指令） |
| `GiftAsync(item)` / `UseAsync(item)` | 代理 ShopService，返回回应文本 |

**用途**：MainGameViewModel / WeChatViewModel / OfficialChatBridge 共用同一套逻辑。

### 4.5 官方接入桥 — `Modules.RealChat.OfficialChatBridge`

- 注入 `GiftPanelService`，监听 QQ/微信官方通道消息。
- **文本指令识别**（正则）：
  - `送礼 xxx` / `送礼物 xxx` → `GiftAsync(FindOwnedByName(xxx))`
  - `使用 xxx` → `UseAsync(FindOwnedByName(xxx))`
- 非指令走正常 `ChatEngine.Send`。
- 单聊消息经 `SemaphoreSlim` 串行，回复经对应 Channel 回发。

> **注意**：外部 QQ/微信无 UI 按钮，仅支持文本指令消耗库存。

### 4.6 游戏引擎 — `Services.GameEngine`

- `State`：当前角色/地图/金币/好感/信任/精力/心情/生理周期/背包/Buff 等运行时状态。
- `ExecuteMoveAsync(input)`：处理玩家行动，推进时间/触发事件/返回叙事文本。
- `SaveGameAsync/LoadGameAsync`：配合 `SaveManager` 全量存档。

### 4.7 批量集合 — `Models.RangeObservableCollection<T>`

```csharp
public void ReplaceRange(IEnumerable<T> items) // 单次 Reset 事件，解决 CollectionView 嵌套改集合异常
public void AddRange(IEnumerable<T> items)      // 单次 Add 事件，分页追加用
```

**用处**：`ShopViewModel.Items` 由 `ObservableCollection` 改为 `RangeObservableCollection`，`RefreshItems` 用 `ReplaceRange`，`LoadNextPage` 用 `AddRange`。

---

## 5. UI 设计系统

- **色板**：`DesignSystem/Theme/ColorPaletteSakura.xaml`（默认）+ Bamboo + Mist。关键键：
  - `SurfaceBg #FEF8F9` `SurfaceFg #7A4A56` `PrimaryAction #ECA9BB` `PrimaryFg #7A4A56`
  - `SecondaryBg #F9DFE5` `MutedFg #B98A96` `BorderLine #F3CBD5` `OverlayDim #C87A4A56`
  - `AccentWarm #F6DCC6` `AccentWarmFg #8A6A4F` `BeigeAccent200 #ECA9BB` `WarmMuted #B98A96`
  - `ShadowDim #F0CFD8`（弹窗阴影）
- **样式**：`NeumorphicStyles`（按钮/卡片/输入框），`CardShapes`，`GlassEffects`，`ButtonShapes`，`TextTypography`。
- **转换器**：`InverseBool`、`StringNotEmpty`、`GreaterThanZero`、`IsEmptyConverter` 等（`Converters/` + `App.xaml` 注册）。

---

## 6. 数据存储

| 文件 | 路径 | 内容 |
|------|------|------|
| `settings.json` | `%LOCALAPPDATA%\WarmAsBefore\settings.json` | `UserSettings`（AI端点/通知/主题/窗口置顶/自动保存/数据包/官方接入配置） |
| `shop.json` | `%LOCALAPPDATA%\WarmAsBefore\shop.json` | `ShopState`（Coins/Owned/AiItems/Buffs/GameRecords） |
| `memory_*.json` | `%LOCALAPPDATA%\WarmAsBefore\memory_*.json` | `MemoryEntry` 列表（按角色分文件） |
| `diary_*.json` | 同上 | 日记条目 |
| `save_*.json` | 同上 | 游戏存档（GameState 完整快照） |
| `gameengine.json` | 同上 | GameEngine 运行时状态 |
| `warm_startup.log` | `C:\Users\<User>\Documents\warm_startup.log` | 启动日志（`App.WriteLog`） |

---

## 7. 构建 / 运行 / 发布

```powershell
# 构建（Release，Win10 x64，非自包含，增量）
dotnet build WarmAsBefore.csproj `
  -c Release -f net9.0-windows10.0.19041.0 `
  -p:RuntimeIdentifierOverride=win10-x64 --self-contained false -v q -nologo

# 发布（自包含，多文件）
dotnet publish WarmAsBefore.csproj `
  -c Release -f net9.0-windows10.0.19041.0 `
  -p:RuntimeIdentifierOverride=win10-x64 --self-contained true -p:PublishSingleFile=false -v q -nologo

# EXE 位置
bin\Release\net9.0-windows10.0.19041.0\win10-x64\publish\WarmAsBefore.exe

# 重启验证
Get-Process -Name WarmAsBefore -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep 2
Start-Process "上述EXE路径"
Start-Sleep 10
Get-Process -Name WarmAsBefore | Select-Object Id,@{N='MB';E={[Math]::Round($_.WorkingSet64/1MB)}}
```

> **PowerShell 5.1 注意**：UTF-8 文件改写必须用 `edit` 工具（或 C# 读写），禁止 `-Raw -replace` 直接写回。

---

## 8. 常用扩展点

### 8.1 新增商品
在 `ShopService.BuildSeedCatalog()` 的 `Add(...)` 列表追加，或运行时让 AI 生成（返回 `ShopItem`，`AiGenerated=true`）。

### 8.2 新增 Buff 类型
`CharacterBuff` 已含 `Source` 区分“送礼/使用”、`TurnsLeft` 轮数、`Desc` 注入 AI。如需新机制（如永久/叠加层数），在 `CharacterBuff` 加字段，并在 `ShopService.AddBuff/TickBuffs/BuffContextText` 对应处理。

### 8.3 新增聊天入口
1. 新建 Page + VM（Transient 注册）。
2. VM 构造注入 `GiftPanelService`，复制 `IsGiftPanelVisible` / `IsGiftMode` / `ToggleGiftPanelCommand` / `ToggleGiftModeCommand` / `GiftItems` / `HasGiftItems` / `GiftItemCommand` / `UseItemCommand`。
3. XAML 复制 MainGamePage/WeChatPage 的送礼面板块（已抽象为同构结构），绑定同上命令。

### 8.4 新增官方通道文本指令
在 `OfficialChatBridge.TryHandleCommand` 追加正则分支，调用 `GiftPanelService.FindOwnedByName` + `GiftAsync/UseAsync`。

### 8.5 新增小游戏
在 `Modules.GameModule` 加新 Engine/Service，`MauiProgram` 注册 Singleton，`GameViewModel` 暴露入口。

---

## 9. 已知问题与技术债

| 问题 | 状态 | 备注 |
|------|------|------|
| `ObservableRangeCollection` 不存在于 CommunityToolkit.Mvvm 8.4.2 | **已绕过** | 自制 `RangeObservableCollection` 替代 |
| CollectionView 嵌套改集合异常 | **已修** | `ReplaceRange`/`AddRange` 单事件 |
| 送礼面板原版“卡片套卡片+双按钮挤” | **已重构** | 模式切换+行式列表+单按钮（MainGamePage/WeChatPage） |
| 官方 QQ/微信通道仅文本指令，无按钮 | **设计决定** | 外部聊天软件无法植入 UI，Buff 已注入 AI 上下文 |
| `ShopService` 与 `GameEngine` 双向引用 `_state` 并发竞争 | **接受风险** | UI 单线程 + 存档频率低，未加锁 |
| `docs/` 下文档过时（架构图缺模块、DATA_PACK 仅示例） | **待清理** | 以本文档为准 |

---

## 10. 快速导航索引

| 要找 | 文件 |
|------|------|
| AI 端点配置/人设/上下文注入 | `Modules/AiChat/Services.cs` (`ChatEngine`) |
| 记忆存取/好感/日记 | `Modules/AiChat/Services.cs` (`MemoryVault`) |
| 商品目录/购买/送礼/使用/Buff/身体状态 | `Modules/Market/ShopService.cs` |
| 送礼面板共享逻辑 | `Modules/Market/GiftPanelService.cs` |
| 真 QQ/微信桥接/文本指令 | `Modules/RealChat/OfficialChatBridge.cs` |
| 游戏状态/行动/存档 | `Services/CoreServices.cs` (`GameEngine`) |
| 设置持久化 | `Services/CoreServices.cs` (`SettingsManager`) |
| JSON 本地存储 | `Services/CoreServices.cs` (`StorageProvider`) |
| 主界面 VM/送礼面板命令 | `ViewModels/MainGameViewModel.cs` |
| 微信聊天 VM/送礼面板命令 | `ViewModels/ModuleViewModels.cs` (`WeChatViewModel`) |
| 商店页 VM/分页/Buff/身体状态 | `ViewModels/ShopViewModel.cs` |
| 主界面送礼面板 XAML | `Views/MainGamePage.xaml` (末尾 Grid `IsGiftPanelVisible`) |
| 微信页送礼面板 XAML | `Views/WeChatPage.xaml` (末尾 Grid `RowSpan=5`) |
| 商店页 XAML | `Views/ShopPage.xaml` |
| 设计系统色板 | `DesignSystem/Theme/ColorPaletteSakura.xaml` |
| 内置商品种子 | `Modules/Market/ShopService.cs` `BuildSeedCatalog()` |
| 批量集合 | `Models/RangeObservableCollection.cs` |

---

> 维护者：按需更新本文档。重大架构变更（新模块/存储格式变更/跨模块契约变更）必须同步修改此文件。