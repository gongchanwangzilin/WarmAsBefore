# 温暖如初 · Warm As Before

<p align="center">
  <img src="https://img.shields.io/badge/MAUI-.NET%2010-512BD4?logo=dotnet&logoColor=white" alt=".NET 10" />
  <img src="https://img.shields.io/badge/platform-Windows%20%7C%20Android-8A4A56" alt="Platforms" />
  <img src="https://img.shields.io/badge/license-MIT%20%28Non–Commercial%29-B98A96" alt="License" />
  <img src="https://img.shields.io/badge/version-1.3.0-F6DCC6" alt="Version" />
</p>

> **An open-source interactive AI role-playing game built with .NET MAUI + WinUI 3**
> **一款基于 .NET MAUI 的开源交互式 AI 角色扮演游戏，支持 Android / Windows 双端**

| | |
|---|---|
| **Developer / 开发者** | `xkxt1026` |
| **License / 协议** | MIT + non-commercial clause · [LICENSE](LICENSE) |
| **Latest version / 版本** | 1.3.0 |

---

<details open>
<summary><strong>🇺🇸 English</strong></summary>

## Overview

**Warm As Before** is an interactive role-playing game where you build an ongoing, personalized relationship with an AI companion. The game spans a living world — the character moves across a map, responds to weather and time, accepts gifts, plays games with you, and remembers what you talk about — with an in-game economy, an affection system, and a pluggable battle layer.

The same codebase targets **Windows 10 (WinUI 3)** and **Android** from a single .NET MAUI project.

### Features

| Feature | Description |
|---|---|
| **AI chat** | OpenAI-compatible endpoints, streaming replies, per-character long-term memory, offline-friendly fallback when no API key is set |
| **Character system** | Multi-character support, randomized outfit & expression, position-aware animations |
| **Map exploration** | Scene graph with distance calculation, anti-teleport rules, weather/time awareness via Open-Meteo |
| **Affection & CG collection** | Affection level system with unlock animations; CG gallery populated by milestones and screenshots |
| **Battle system** | Pluggable driver architecture — swap the built-in driver for a custom AI-driven one |
| **Shop & gifting** | Coin economy, seed catalog + AI-generated items, buffs that are injected into the AI's context each turn |
| **Themes** | Classic / Sakura / Bamboo / Mist palettes, switchable at runtime |
| **Desktop pet** | Minimize to a floating character avatar that keeps the companion on-screen |
| **Novel import** | Feed the game a novel; an AI analyzer converts it into playable scenes |
| **Data packs** | Import/export game state as ZIP data packs |
| **Save system** | Auto-save, manual slots, JSON import/export |
| **Tool / plugin system** | Runtime tool manager with trust sandbox and crypto store |

### Snapshot tour

| Screen | Highlights |
|---|---|
| Title | Neumorphic style, sibling-role select, 4 entry points |
| Main game | Portrait/landscape dual layout, affection/trust/time/weather status bar, character CG area, chat bubbles |
| WeChat-style chat | `CollectionView` bubbles, user right / AI left, typewriter effect, "typing…" indicator |
| Phone home | Cream neumorphic icons: WeChat / Map / Gallery / Shop |
| Game list | 6 board games — Gomoku, Animal Chess, Flying Chess, International & Chinese Chess, Snake |
| Save manager | Card-style slots, new-save sheet with novel/character import checks + mode picker |

---

## Prerequisites

| Platform | Requires |
|---|---|
| **Windows** | .NET 10 SDK + MAUI workload — `dotnet workload install maui` |
| **macOS** (cross-building Android only) | .NET 10 SDK + `maui-android` workload |
| **Linux** (cross-building Android only) | .NET 10 SDK + `maui-android` workload |

> The Windows target is a WinUI 3 (MAUI Windows) app and must be **built on Windows**.
> Android targets can be cross-compiled from any host.

---

## Build

### Windows (EXE, unpacked, self-contained WindowsAppSDK)

```bash
cd src/WarmAsBefore

dotnet publish -f net10.0-windows10.0.19041.0 -c Release \
  -p:RuntimeIdentifierOverride=win10-x64 \
  -p:WindowsPackageType=None \
  -p:WindowsAppSDKSelfContained=true \
  -p:UseMonoRuntime=false
```

> ⚠️ **Do not** replace the flags above with `-r win-x64` / `-p:RuntimeIdentifier=win-x64`.
> That route triggers [WindowsAppSDK #3337](https://github.com/CommunityToolkit/WindowsAppSDK/issues/3337):
> the produced EXE silently fails to find its XAML theme resources and never shows a window.
> `-p:UseMonoRuntime=false` is also required — the Mono Android pack breaks restore otherwise.

Output: `bin/Release/net10.0-windows10.0.19041.0/win10-x64/publish/WarmAsBefore.exe`

### Android (APK)

```bash
cd src/WarmAsBefore
dotnet build -f net10.0-android
```

Output: `bin/Debug/net10.0-android/com.companyname.warmasbefore-Signed.apk`

### One-click publish scripts

| Script | Purpose |
|---|---|
| `publish_windows.bat` / `publish_windows.sh` | Windows EXE, self-contained |
| `publish_android.sh` | Android APK |
| `docker-build/` | Docker-based Android build (cross-compile from Linux/CI) |

A full step-by-step guide (SDK install, workload setup, troubleshooting) lives in
[`BUILD_GUIDE.md`](BUILD_GUIDE.md).

---

## Architecture at a glance

```
WarmAsBefore/
├── BUILD_GUIDE.md
├── LICENSE
├── README.md
├── publish_windows.{bat,sh}
├── publish_android.sh
├── docker-build/
└── src/WarmAsBefore/
    ├── App.xaml(.cs)              # entry point, global resources, startup log
    ├── AppShell.xaml(.cs)         # Shell navigation + route table
    ├── MauiProgram.cs             # DI registration (all services / modules / pages)
    │
    ├── Models/                    # POCOs & special collections
    ├── Views/                     # XAML pages (MVVM, BindingContext = VM)
    ├── ViewModels/               # CommunityToolkit.Mvvm VMs
    │
    ├── Services/                 # Core singletons
    │   └── CoreServices.cs        # GameEngine, SettingsManager, StorageProvider,
    │                              #   NotificationService, AudioController,
    │                              #   SpeechService, GlassOverlayService, …
    │
    ├── Modules/                  # Feature modules (constructor-injected)
    │   ├── AiChat/              # ChatEngine + MemoryVault (chat / memory / diary / buff hooks)
    │   ├── Affection/           # Affection levels & unlock animations
    │   ├── ApiManager/          # OpenAI-compatible API gateway
    │   ├── Automation/          # Task orchestrator, daily diary writer
    │   ├── Battle/              # Pluggable battle driver + built-in driver
    │   ├── Cg/                  # CG collection & unlock logic
    │   ├── DataPack/            # ZIP data pack import / export
    │   ├── GameModule/          # 6 board games + chess AI
    │   ├── Market/              # Shop & gift panel services
    │   ├── Mcp/                 # MCP server orchestration
    │   ├── NovelImport/         # Novel → scene analysis
    │   ├── RealChat/            # Official QQ / WeChat channel bridge
    │   ├── RealWorld/           # Weather, time, permissions, physiology tracker
    │   ├── SaveSystem/          # Save / load / export
    │   ├── Sandbox/             # Trust store + crypto store
    │   ├── Scene/               # Scene director
    │   ├── Showcase/            # Dev showcase mode
    │   ├── Tools/               # Runtime tool manager
    │   ├── Update/              # Update check
    │   └── Worldbook/           # Worldbook generation
    │
    ├── Controls/                # Custom XAML controls
    ├── Converters/              # Value converters
    ├── DesignSystem/            # Self-contained design system
    │   ├── Theme/ColorPalette*.xaml  # 4 palettes (Classic/Sakura/Bamboo/Mist)
    │   ├── Styles/              # Neumorphic / Card / Glass / Text / Buttons
    │   └── Tokens/              # Spacing, radius, shadow, animation tokens
    ├── Helpers/                 # Misc utilities
    ├── Resources/               # Fonts, images, raw assets
    └── Platforms/Windows/       # WinUI 3 host configuration
```

**DI rules** (enforced by convention, not the compiler):
- Services & module engines → **Singleton**
- Pages & ViewModels → **Transient**
- Cross-module communication → constructor injection only; static references are banned

### AI chat pipeline

```
User input
  └─► ChatEngine.Send(charId, text)
        ├─ MemoryVault.All(charId)         # top-K relevant memories
        ├─ BuffContextProvider()            # prepends [Current State] block
        ├─ system + memory + buff + user    # assembled
        ├─ OpenAI-compatible streaming API
        ├─ incremental callback → UI
        ├─ store in MemoryVault
        └─ AfterSend hook                   # ShopService.TickBuffs decrements buff turns
```

---

## Where the data lives

All user data is plain JSON under the app's user directory:

| File | Contents |
|---|---|
| `settings.json` | AI endpoint, theme, notifications, pin-to-top, auto-save, data pack, chat bridge config |
| `shop.json` | Coins, owned items, AI-generated items, active buffs, game records |
| `memory_<charId>.json` | Per-character memory entries (chat / diary / affection / buff history) |
| `diary_<charId>.json` | Diary entries |
| `save_<slot>.json` | Full game-state snapshot per slot |
| `gameengine.json` | Runtime engine state |
| `warm_startup.log` | Startup log (`C:\Users\<user>\Documents\` on Windows) |

Windows: `%LOCALAPPDATA%\WarmAsBefore\` · Android: app external files dir.

---

## Design system

The UI is built on a self-contained design system — no third-party UI kit.

**Palettes** (one embedded XAML resource each, switchable at runtime):

| Palette | Mood |
|---|---|
| `ColorPalette` (default) | Cream / off-white / beige, warm neutrals |
| `ColorPaletteSakura` | Sakura pink on warm neutrals |
| `ColorPaletteBamboo` | Bamboo green on light neutrals |
| `ColorPaletteMist` | Blue-grey, misty |

**Neumorphism rules** (applied across all `NeumorphicStyles`):
- Control and background share a base color; depth comes from shadow + border only
- Raised: dark shadow bottom-right + light highlight top-left
- Pressed / inset: shadow removed + dark border bottom-left + 1.5 px offset
- Unified 20 px corner radius

**Token set** (`DesignSystem/Tokens/DesignTokens.cs`): spacing scale, corner radius,
shadow presets, animation durations.

---

## Contributing

1. **Fork** the repo and create a feature branch from `master`.
2. **Follow the DI rules** — register new services in `MauiProgram.cs` (Singleton),
   new pages/VMs as Transient. Constructor-inject; never reference another module statically.
3. **Use the design system** — new UI must use the existing style keys and tokens,
   not ad-hoc colors or spacing.
4. **Keep the scope a module** — new features should live under `Modules/<Name>/`
   with its own engine/service, following the pattern of `Modules/AiChat` or `Modules/Battle`.
5. **Update `DEVELOPMENT.md`** — any architectural or storage-format change must be
   documented there in the same PR.
6. **Open a PR** with a clear description of what changed and why.

We welcome bug reports, feature requests, and new module implementations.

### Extension points (common tasks)

| Task | Where |
|---|---|
| Add a shop item | `Modules/Market` → `ShopService.BuildSeedCatalog()`, or let the AI generate one at runtime |
| Add a new buff type | Extend `CharacterBuff` fields; handle in `ShopService.AddBuff` / `TickBuffs` / `BuffContextText` |
| Add a new chat surface | New Transient Page + VM, inject `GiftPanelService`, copy the gift panel XAML block |
| Add a new official-channel command | `Modules/RealChat` → `OfficialChatBridge.TryHandleCommand` |
| Add a mini-game | New engine under `Modules/GameModule`, register in `MauiProgram`, expose in `GameViewModel` |
| Add a battle driver | Implement `BattleDriver`, register in `BattleDriverManager` |

---

## Documentation map

| File | What it covers |
|---|---|
| `BUILD_GUIDE.md` | Step-by-step build on Windows / macOS / Linux + troubleshooting |
| `src/WarmAsBefore/DEVELOPMENT.md` | Full architecture reference for maintainers — module APIs, DI table, data files, extension points, known issues |
| `docs/ARCHITECTURE.md` | (Legacy) high-level architecture diagram — verify against `DEVELOPMENT.md` before relying on it |
| `docs/DESIGN_SYSTEM.md` | (Legacy) design system notes |
| `docs/DATA_PACK.md` | Data pack format example |
| `docs/TOOLS.md` | Tool / MCP notes |

> `DEVELOPMENT.md` is the authoritative architecture reference; entries under `docs/`
> may be outdated.

---

## License

[GNU General Public License v2](LICENSE) · © 2026 xkxt1026

本项目采用 GPLv2（继承 proot 的 GPLv2）。完整条款：[LICENSE](LICENSE)。

## Community & support

| | |
|---|---|
| Bilibili | [space.bilibili.com/3546745275419060](https://space.bilibili.com/3546745275419060) |
| Sponsor (爱发电) | [ifdian.net/a/jqyhxkxt1145141026](https://ifdian.net/a/jqyhxkxt1145141026) |

---

</details>

<details>
<summary><strong>🇨🇳 中文</strong></summary>

## 项目简介

**温暖如初** 是一款开放式互动 AI 角色扮演游戏：你与一位 AI 角色建立持续而个性化的关系。
角色在一个有生活感的世界中移动——响应天气、时间、接受礼物、与你下棋、记住你聊过的事——
并配有游戏内经济系统、好感度系统与可插拔战斗层。

同一份代码支持 **Windows 10（WinUI 3）** 与 **Android** 双端。

### 功能

| 功能 | 说明 |
|---|---|
| **AI 对话** | OpenAI 兼容端点，流式回复，按角色的长期记忆，无密钥时离线友好兜底 |
| **角色系统** | 多角色支持，随机服装与表情，位置感知动画 |
| **地图探索** | 场景图 + 距离计算，防 teleport 规则，Open-Meteo 天气/时间感知 |
| **好感度 & CG 收藏** | 好感等级 + 解锁动画；CG 相册由里程碑与截图累积 |
| **战斗系统** | 可插拔驱动架构，内置驱动可替换为自定义 AI 驱动 |
| **商店 & 送礼** | 金币经济、种子目录 + AI 生成商品、Buff 逐轮注入 AI 上下文 |
| **主题** | 经典 / 樱花 / 翠竹 / 晨雾四套色板，运行时可切换 |
| **桌宠模式** | 最小化为浮动角色头像 |
| **小说导入** | 喂入小说文本，AI 分析器生成可玩场景 |
| **数据包** | ZIP 导入 / 导出游戏状态 |
| **存档系统** | 自动存档、手动槽位、JSON 导入导出 |
| **工具 / 插件** | 运行时工具管理器 + 信任沙箱 + 加密存储 |

### 界面一览

| 页面 | 亮点 |
|---|---|
| 标题画面 | 新拟物风格、身份选择（哥哥/姐姐）、4 个入口 |
| 主界面 | 竖/横双布局、好感/信任/时间/天气状态栏、角色 CG 区、对话气泡 |
| 微信聊天 | CollectionView 气泡（用户右 / AI 左）、打字机效果、"对方输入中…" |
| 手机桌面 | 奶油色新拟物图标：微信 / 地图 / 相册 / 商店 |
| 游戏列表 | 五子棋 / 斗兽棋 / 飞行棋 / 国际象棋 / 中国象棋 / 贪吃蛇 |
| 存档管理 | 卡片式存档位、新建存档浮层（小说/角色导入检查 + 模式选择） |

---

## 环境要求

| 平台 | 要求 |
|---|---|
| **Windows** | .NET 10 SDK + MAUI 工作负载 — `dotnet workload install maui` |
| **macOS**（仅交叉编译 Android） | .NET 10 SDK + `maui-android` 工作负载 |
| **Linux**（仅交叉编译 Android） | .NET 10 SDK + `maui-android` 工作负载 |

> Windows 目标为 WinUI 3（MAUI Windows）应用，**必须在 Windows 上构建**。
> Android 目标可在任意宿主上交叉编译。

---

## 构建

### Windows（EXE，无 MSIX 打包，自包含 WindowsAppSDK）

```bash
cd src/WarmAsBefore

dotnet publish -f net10.0-windows10.0.19041.0 -c Release ^
  -p:RuntimeIdentifierOverride=win10-x64 ^
  -p:WindowsPackageType=None ^
  -p:WindowsAppSDKSelfContained=true ^
  -p:UseMonoRuntime=false
```

> ⚠️ **不要**用 `-r win-x64` / `-p:RuntimeIdentifier=win-x64` 替代上面这组参数。
> 那条路线会触发 [WindowsAppSDK #3337](https://github.com/CommunityToolkit/WindowsAppSDK/issues/3337)：
> 产物 EXE 找不到 XAML 主题资源，启动后无任何窗口。
> `-p:UseMonoRuntime=false` 也必不可少，否则 Mono Android 包会让还原失败。

产物位置：`bin/Release/net10.0-windows10.0.19041.0/win10-x64/publish/WarmAsBefore.exe`

### Android（APK）

```bash
cd src/WarmAsBefore
dotnet build -f net10.0-android
```

产物位置：`bin/Debug/net10.0-android/com.companyname.warmasbefore-Signed.apk`

### 一键发布脚本

| 脚本 | 用途 |
|---|---|
| `publish_windows.bat` / `publish_windows.sh` | Windows EXE，自包含 |
| `publish_android.sh` | Android APK |
| `docker-build/` | Docker 构建（Linux / CI 上交叉编译 Android） |

完整分步指南（SDK 安装、工作负载配置、排错）见 [`BUILD_GUIDE.md`](BUILD_GUIDE.md)。

---

## 架构速览

```
WarmAsBefore/
├── BUILD_GUIDE.md
├── LICENSE
├── README.md
├── publish_windows.{bat,sh}
├── publish_android.sh
├── docker-build/
└── src/WarmAsBefore/
    ├── App.xaml(.cs)              # 入口、全局资源、启动日志
    ├── AppShell.xaml(.cs)         # Shell 导航 + 路由表
    ├── MauiProgram.cs             # DI 注册总表（服务 / 模块 / 页面）
    │
    ├── Models/                    # 数据 POCO 与特殊集合
    ├── Views/                     # XAML 页面（MVVM，BindingContext = VM）
    ├── ViewModels/               # CommunityToolkit.Mvvm VM
    │
    ├── Services/                 # 核心单例
    │   └── CoreServices.cs        # GameEngine、SettingsManager、StorageProvider、
    │                              #   NotificationService、AudioController、
    │                              #   SpeechService、GlassOverlayService …
    │
    ├── Modules/                  # 功能模块（构造函数注入）
    │   ├── AiChat/              # 对话引擎 + 记忆仓库（对话/记忆/日记/Buff 钩子）
    │   ├── Affection/           # 好感等级与解锁动画
    │   ├── ApiManager/          # OpenAI 兼容 API 网关
    │   ├── Automation/          # 任务编排、每日日记
    │   ├── Battle/              # 可插拔战斗驱动 + 内置驱动
    │   ├── Cg/                  # CG 收藏与解锁逻辑
    │   ├── DataPack/            # ZIP 数据包导入 / 导出
    │   ├── GameModule/          # 6 种棋类游戏 + 象棋 AI
    │   ├── Market/              # 商店与送礼面板服务
    │   ├── Mcp/                 # MCP 服务器编排
    │   ├── NovelImport/         # 小说 → 场景分析
    │   ├── RealChat/            # QQ / 微信官方通道桥
    │   ├── RealWorld/           # 天气、时间、权限、生理追踪
    │   ├── SaveSystem/          # 存档 / 读档 / 导出
    │   ├── Sandbox/             # 信任存储 + 加密存储
    │   ├── Scene/               # 场景导演
    │   ├── Showcase/            # 开发者展示模式
    │   ├── Tools/               # 运行时工具管理器
    │   ├── Update/              # 更新检查
    │   └── Worldbook/           # 世界书生成
    │
    ├── Controls/                # 自定义 XAML 控件
    ├── Converters/              # 值转换器
    ├── DesignSystem/            # 自研设计系统
    │   ├── Theme/ColorPalette*.xaml  # 4 套色板（经典/樱花/翠竹/晨雾）
    │   ├── Styles/              # Neumorphic / Card / Glass / Text / Buttons
    │   └── Tokens/              # 间距、圆角、阴影、动画 Token
    ├── Helpers/                 # 杂项工具
    ├── Resources/               # 字体、图片、原始资源
    └── Platforms/Windows/       # WinUI 3 宿主配置
```

**DI 规则**（约定而非编译器强制）：
- Service 与模块引擎 → **Singleton**
- Page 与 ViewModel → **Transient**
- 跨模块通信只走构造函数注入，禁止静态引用

### AI 对话管线

```
用户输入
  └─► ChatEngine.Send(charId, text)
        ├─ MemoryVault.All(charId)         # 取 Top-K 相关记忆
        ├─ BuffContextProvider()           # 前置 [当前状态标记] 块
        ├─ system + 记忆 + Buff + 用户     # 组装
        ├─ OpenAI 兼容流式 API
        ├─ 增量回调 → UI
        ├─ 存入 MemoryVault
        └─ AfterSend 钩子                   # ShopService.TickBuffs 递减 Buff 轮数
```

---

## 数据存储位置

所有用户数据均为 JSON，位于应用用户目录：

| 文件 | 内容 |
|---|---|
| `settings.json` | AI 端点、主题、通知、窗口置顶、自动保存、数据包、聊天桥配置 |
| `shop.json` | 金币、已购商品、AI 生成商品、当前 Buff、游戏记录 |
| `memory_<charId>.json` | 每角色记忆条目（聊天 / 日记 / 好感 / Buff 历史） |
| `diary_<charId>.json` | 日记条目 |
| `save_<slot>.json` | 每槽位完整游戏存档 |
| `gameengine.json` | 引擎运行时状态 |
| `warm_startup.log` | 启动日志（Windows：`C:\Users\<user>\Documents\`） |

Windows 路径：`%LOCALAPPDATA%\WarmAsBefore\` · Android：应用外部文件目录。

---

## 设计系统

UI 基于自研设计系统，无第三方 UI 库。

**色板**（每套为一个嵌入 XAML 资源，运行时可切换）：

| 色板 | 风格 |
|---|---|
| `ColorPalette`（默认） | 奶油 / 米白 / 米黄，暖中性色 |
| `ColorPaletteSakura` | 樱花粉 + 暖中性色 |
| `ColorPaletteBamboo` | 翠竹绿 + 浅中性色 |
| `ColorPaletteMist` | 蓝灰晨雾 |

**新拟物规则**（应用于全部 `NeumorphicStyles`）：
- 控件与背景同基色，层次仅靠阴影 + 描边表现
- 凸起：右下暗阴影 + 左上白描边
- 按下 / 凹陷：去阴影 + 左下暗描边 + 1.5 px 位移
- 统一 20 px 圆角

**Token 集**（`DesignSystem/Tokens/DesignTokens.cs`）：间距梯度、圆角、阴影预设、动画时长。

---

## 贡献

1. **Fork** 仓库，基于 `master` 创建功能分支。
2. **遵守 DI 规则** — 新服务在 `MauiProgram.cs` 注册为 Singleton，新 Page/VM 为 Transient；
   只走构造函数注入，禁止跨模块静态引用。
3. **使用设计系统** — 新 UI 必须使用既有样式键与 Token，不引入临时颜色或间距。
4. **保持模块边界** — 新功能放入 `Modules/<Name>/`，自带 engine/service，
   参照 `Modules/AiChat` 或 `Modules/Battle` 的模式。
5. **同步更新 `DEVELOPMENT.md`** — 任何架构或存储格式变更需在同一个 PR 中记录。
6. **提交 PR**，清楚描述改了什么、为什么改。

欢迎提交 bug 报告、功能建议与新模块实现。

### 常见扩展点

| 任务 | 位置 |
|---|---|
| 新增商品 | `Modules/Market` → `ShopService.BuildSeedCatalog()`，或运行时由 AI 生成 |
| 新增 Buff 类型 | 扩展 `CharacterBuff` 字段；在 `ShopService.AddBuff` / `TickBuffs` / `BuffContextText` 处理 |
| 新增聊天入口 | 新建 Transient Page + VM，注入 `GiftPanelService`，复制送礼面板 XAML 块 |
| 新增官方通道指令 | `Modules/RealChat` → `OfficialChatBridge.TryHandleCommand` |
| 新增小游戏 | `Modules/GameModule` 新建引擎，`MauiProgram` 注册，`GameViewModel` 暴露 |
| 新增战斗驱动 | 实现 `BattleDriver`，在 `BattleDriverManager` 注册 |

---

## 文档索引

| 文件 | 内容 |
|---|---|
| `BUILD_GUIDE.md` | Windows / macOS / Linux 分步构建 + 排错 |
| `src/WarmAsBefore/DEVELOPMENT.md` | 维护者权威架构参考 — 模块 API、DI 表、数据文件、扩展点、已知问题 |
| `docs/ARCHITECTURE.md` | （旧版）高层架构图 — 使用前先对照 `DEVELOPMENT.md` 确认 |
| `docs/DESIGN_SYSTEM.md` | （旧版）设计系统说明 |
| `docs/DATA_PACK.md` | 数据包格式示例 |
| `docs/TOOLS.md` | 工具 / MCP 说明 |

> `DEVELOPMENT.md` 为权威架构参考；`docs/` 下内容可能过时。

---

## 协议

[GNU General Public License v2](LICENSE) · © 2026 xkxt1026

本项目采用 GPLv2（继承 proot 的 GPLv2）。完整条款：[LICENSE](LICENSE)。

## 社区与支持

| 平台 | 链接 |
|---|---|
| Bilibili | [space.bilibili.com/3546745275419060](https://space.bilibili.com/3546745275419060) |
| 赞助（爱发电） | [ifdian.net/a/jqyhxkxt1145141026](https://ifdian.net/a/jqyhxkxt1145141026) |

</details>
本项目支持AI提交PR
