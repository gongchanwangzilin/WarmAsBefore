# 温暖如初 · Warm As Before

<p align="center">
  <img src="https://img.shields.io/badge/MAUI-.NET%2010-512BD4?logo=dotnet&logoColor=white" alt=".NET 10" />
  <img src="https://img.shields.io/badge/platform-Windows%20%7C%20Android-8A4A56" alt="Platforms" />
  <img src="https://img.shields.io/badge/license-MIT%20%28Non%E2%80%93Commercial%29-B98A96" alt="License" />
  <img src="https://img.shields.io/badge/version-1.51.0-F6DCC6" alt="Version" />
</p>

> **A local-first AI companion and interactive story app built with .NET MAUI.**
> **一款本地优先的 AI 角色陪伴与互动叙事应用，支持 Windows / Android 双端。**

**温暖如初**让角色拥有可检查的记忆、随时间与天气变化的生活状态，以及一个能真正走动的世界。
对话不是唯一的界面：角色会出现在地图、手机、微信、商店、棋局与桌面宠物里；记忆、好感与关系网
都落在本地 JSON 中，换一局存档不会串味。插件系统在此之上开放了钩子、设置页、页面与受控 JS API。

| | |
|---|---|
| **开发者** | `xkxt1026` |
| **当前版本** | `1.51.0`（`ApplicationVersion` 51） |
| **运行平台** | Windows 10 19041+ / Android 7.0+（minSdk 24，targetSdk 36） |
| **技术栈** | .NET 10 · .NET MAUI · WinUI 3 · CommunityToolkit.Mvvm · Jint · SkiaSharp |
| **数据形态** | 本地 JSON + 素材文件；可导出数据包，可镜像到自己的 GitHub 仓库做云存档 |
| **协议** | MIT + 非商业条款；内置 proot 组件遵循 GPLv2（见 `LICENSE.txt`） |

---

## 这一版在做什么

- **数值由 AI 判**：好感、信任、精力不再只靠固定加减。回复里给出增减就按回复来；判不动就不动。
  精力按「清醒时长 + 本次修正」结算，修正随存档落盘。设置里保留固定增量开关。
- **大脑预判**：每轮之前估计本次互动的热度。超出预期、低于预期与意料之外分别走不同的反馈；
  预判只作为一行指令搭进本来就要跑的请求里，可单独关闭。
- **存档绑定世界树**：新开一局先选世界树，再写用户在这段故事里的身份。未绑定也能聊天，
  但地图入口会锁上并说明从哪里解绑；世界树条目可手写、可让 AI 生成或润色。
- **人设进入记忆**：创建存档时写的人设先整理成第一人称记忆，之后由记忆层按需想起，
  不再每轮把整份人设原样塞进提示词。AI 不可用时用本地分句兜底。
- **插件系统**：ZIP / 文件夹安装、启停、试跑、体检、设置页、权限台与崩溃日志。
  钩子可改人设与回复文本；JS 沙箱只暴露白名单宿主函数，能力按「声明 ∩ 授予」放行。
- **MCP 与插件分开**：MCP 是外部 stdio / HTTP 服务；插件是本地 JS 包。两者各有列表、各自导入，
  插件目录不会被 MCP 列表收编。

完整的版本条目见 [`RELEASE_NOTES.md`](RELEASE_NOTES.md)。

---

## 功能地图

| 模块 | 能做什么 |
|---|---|
| **AI 对话** | OpenAI 兼容端点、流式回复、多会话、静默协议、工具调用、角色级采样覆写 |
| **角色系统** | 角色库、角色工作室、立绘标定 / 规范化、形象包、服装与表情、头像裁剪、角色包导入导出 |
| **记忆** | 全量对话记忆、人类记忆（重要性 × 时间衰减 × 复述 × 随机扰动）、标签、关系网、世界树、人格记忆初始化 |
| **世界与场景** | 场景图、BFS 寻路、多实体地图、AI 拉拽移动、天气 / 时间 / 定位、场景导演、场景库 |
| **身体与心理** | 冷热饥渴想念、周期、心理状况目录与行为驱动、黑屏时刻 |
| **社交** | 手机桌面、微信 / QQ 论坛、真实 QQ 与微信公众号通道、表情包、识图 |
| **经济与礼物** | 亲密币钱包、1000+ 内置商品、AI 生成商品、Buff 注入、送礼 / 使用面板 |
| **小游戏与战斗** | 五子棋、国际象棋、中国象棋、斗兽棋、飞行棋、贪吃蛇；可插拔战斗驱动 |
| **内容收藏** | 好感等级动画、CG 收藏与全屏播放、每日日记、回忆录 |
| **创作工具** | 小说导入分析、世界书生成、展示案编辑与播放、数据包导入导出 |
| **存档** | 自动存档、手动槽位、退出前保存、云存档、存档规则页、跨设备记忆合并 |
| **扩展** | 插件、工具、技能（Skill）、MCP、外部 Python / Java 运行时、沙箱信任名单 |
| **桌面体验** | 四套主题色板、新拟物设计系统、亚克力 / 透视窗口、Windows 桌宠、页面转场 |

---

## 界面一览

| 页面 | 亮点 |
|---|---|
| 标题页 | 方向键选门、Enter 确认，进入前预载角色名册 |
| 主界面 | 竖 / 横双布局，顶栏与左栏小组件可配置，角色舞台、状态栏、对话气泡 |
| 角色工作室 | 左侧立绘舞台，右侧「设定 / 状态 / 素材 / 记忆 / 关联 / 说句话」六段 |
| 手机桌面 | 微信、地图、相册、商店与动态小组件 |
| 微信 / QQ | CollectionView 气泡、打字机效果、表情包、送礼面板 |
| 地图 | 场景连线、角色位置、AI 拉拽仲裁、背景图片 / 视频 |
| 记忆 | 总览、检索、关系网独立窗口、记忆管理 |
| 插件中心 | 安装、启停、设置、权限台、试跑、体检、日志、页面与 MCP 列表 |

---

## 快速开始

### 环境

| 平台 | 要求 |
|---|---|
| **Windows** | Windows 10 19041+、.NET 10 SDK、MAUI 工作负载：`dotnet workload install maui` |
| **Android** | .NET 10 SDK、`maui-android` 工作负载、Android SDK / JDK；可在 Windows 上构建 |

> Windows 目标为 WinUI 3（MAUI Windows）应用，必须在 Windows 上构建。
> Android 目标需要可用的 Android SDK；缺少 SDK 时 `net10.0-android` 分支无法还原。

### Windows 调试构建

```powershell
dotnet build WarmAsBefore.csproj -c Debug -f net10.0-windows10.0.19041.0
```

### Windows 发布（无 MSIX、自包含 WindowsAppSDK）

```powershell
dotnet publish WarmAsBefore.csproj -c Release `
  -f net10.0-windows10.0.19041.0 `
  -p:RuntimeIdentifierOverride=win10-x64 `
  -p:WindowsPackageType=None `
  -p:WindowsAppSDKSelfContained=true `
  -p:UseMonoRuntime=false
```

产物：`bin/Release/net10.0-windows10.0.19041.0/win10-x64/publish/WarmAsBefore.exe`

> 不要用 `-r win-x64` / `-p:RuntimeIdentifier=win-x64` 替代
> `-p:RuntimeIdentifierOverride=win10-x64`。那条路线会触发 WindowsAppSDK #3337：
> 产物找不到 XAML 主题资源，启动后没有窗口。`-p:UseMonoRuntime=false` 也必须保留。

### Android

```powershell
dotnet build WarmAsBefore.csproj -c Debug -f net10.0-android
```

产物：`bin/Debug/net10.0-android/com.companyname.warmasbefore-Signed.apk`

### 构建注意

- 页面 XAML 的填充优先用 `BackgroundColor` 或内层 `BoxView.Color`；
  `Border.Background="{DynamicResource ...}"` 会触发 `XC0009`。
- 如果 XAML 编译器锁住 `obj/.../intermediatexaml/WarmAsBefore.dll`，
  关闭 `Microsoft.UI.Xaml.Markup.Compiler` 编译器服务器后重新构建。
- 仓库里的 `LICENSE.txt` 是 proot 组件的 GPLv2 文本，不是应用本身的协议。

---

## 项目结构

```text
.
├── App.xaml(.cs)                 # 应用入口、全局资源、启动日志、窗口生命周期
├── AppShell.xaml(.cs)            # Shell 路由表、导航打点、页面转场
├── MauiProgram.cs                # 组合根：按层调用 Composition/*
├── Composition/                  # DI 注册表（设计系统 / 核心 / 模块 / 角色 / 插件 / VM / 页面）
├── Models/                       # 纯数据模型：状态、角色、聊天、记忆、地图、商店、插件权限等
├── Services/                     # 跨模块服务（按功能分组）
│   ├── Core/                     # 游戏状态、设置、存储、运行时配置、窗口、日志、本地化
│   ├── Character/                # 角色库、立绘、素材、心理、生理、性别、角色设置解析
│   ├── Memory/                   # 人类记忆、标签、关系网、世界树、Embedding、成本
│   ├── Media/                    # 生图、识图、语音、表情包
│   ├── Plugins/                  # 插件宿主、权限、体检、技能库
│   ├── World/                    # 地图、地图角色、桌宠、微信、小说库、数据包
│   └── CloudSync/                # 云存档与跨设备记忆合并
├── Modules/                      # 功能模块（对话、商店、游戏、战斗、MCP、桥接等）
├── ViewModels/                   # 页面模型（按 Main / Character / Settings / Memory / Story / Chat / Games / Save / Shop 分组）
├── Views/                        # XAML 页面（与 ViewModels 同构分组）
├── DesignSystem/                 # 色板、Token、新拟物 / 卡片 / 玻璃 / 文本 / 按钮样式
├── Controls/                     # 自定义控件
├── Converters/                   # 值转换器
├── Drawables/                    # 棋盘、地图等绘制器
├── Helpers/                      # 杂项工具
├── Platforms/                    # Windows / Android 平台宿主
├── Resources/                    # 字体、图片、样式、本地化资源
├── plugins/                      # 插件目录：plugins/<id>/plugin.json
├── docs/                         # 架构、组件、插件、桥接与需求文档
└── WarmAsBefore.csproj
```

> 目录按功能分组，但命名空间保持扁平：`WarmAsBefore.Services`、`WarmAsBefore.ViewModels`、
> `WarmAsBefore.Views`；模块使用 `WarmAsBefore.Modules.<模块名>`。移动文件不会改变调用点。

---

## 架构一句话

`Views → ViewModels → Services / Modules → Models`。跨模块只走构造函数注入，
组合根集中在 `MauiProgram.cs` 与 `Composition/`；运行时由 `RuntimeConfigurator`
把设置下发给对话、天气、生理、语音、插件与官方通道。详细分层、启动链路与数据流见
[`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md)。

### AI 对话管线（简化）

```text
用户输入
  └─► ChatEngine.Send(charId, text)
        ├─ 记忆层检索（全量记忆 / 人类记忆 / 标签 / 关系网）
        ├─ 人设 + 世界树 + 动态上下文 + Buff
        ├─ 插件 persona / context 钩子
        ├─ beforeSend 钩子改写用户文本
        ├─ OpenAI 兼容流式请求
        ├─ afterReply 钩子改写回复
        ├─ 写入记忆、标签、关系网、日记与数值结算
        └─ afterTurn / onToolResult / onSceneChange 广播
```

---

## 数据位置

应用数据根目录由 `App.RootDirectory` 决定：

- Windows：`%LOCALAPPDATA%\WarmAsBefore\`
- Android：应用私有 / 外部文件目录

| 路径 / 键 | 内容 |
|---|---|
| `settings.json` | AI 端点、主题、窗口、语音、天气、插件钩子开关等全局设置 |
| `characters.json` | 角色库与角色状态 |
| `gameengine.json` | 引擎运行时状态 |
| `saves/` | 存档槽位与聊天记录 |
| `human_memories/` | 按存档隔离的人类记忆；开启共用后回到 `human_memories.json` |
| `memories/` | 全量对话记忆库（按作用域） |
| `assets/` | 立绘、背景、音频、表情包等素材 |
| `cg/` | CG 目录与解锁状态 |
| `showcase/` | 展示案脚本与素材 |
| `plugins/<id>/` | 插件清单、设置、文件区 |
| `plugins/state.json` | 插件启停状态 |
| `plugins/permissions.json` | 插件授权、询问答案与留痕 |
| `plugin_logs/<id>.log` | 插件崩溃与权限拦截日志 |
| `warm_startup.log` | 启动日志（Windows 在 `Documents/`） |
| `perf.log` | 性能打点日志 |

数据包导出会递归清理密钥字段；云存档按设备分目录、逐键时间戳合并，`settings.json` 与图片不入库。

---

## 插件与 MCP

| 系统 | 形态 | 入口 | 适合做什么 |
|---|---|---|---|
| **插件** | 本地 JS 包，`plugins/<id>/plugin.json` | 插件中心 | 改人设 / 回复、记事件、读写自己的设置与文件、调工具、发通知 |
| **MCP** | 外部服务（stdio / HTTP） | 插件中心的 MCP 列表 | 接外部工具服务，握手取回工具清单 |
| **工具** | 内置 C# / 外部 Python / Java | 工具管理 | 让 AI 执行具体动作，受沙箱与信任名单约束 |
| **技能** | `SKILL.md` + 可选 `tool.json` | 技能库 | 渐进加载的说明书与可执行入口 |

插件清单、钩子、JS API、权限模型、HTML / native 设置页与排错见
[`docs/PLUGINS.md`](docs/PLUGINS.md)。

---

## 文档索引

| 文件 | 内容 |
|---|---|
| [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) | 分层、组合根、启动链路、对话 / 记忆 / 存档数据流、线程与平台差异 |
| [`docs/COMPONENTS.md`](docs/COMPONENTS.md) | 各 Services / Modules / ViewModels / Views / DesignSystem 组件的职责与协作 |
| [`docs/PLUGINS.md`](docs/PLUGINS.md) | 插件安装、清单、设置、页面、钩子、JS API、权限、沙箱与排错 |
| [`DEVELOPMENT.md`](DEVELOPMENT.md) | 维护者手册：环境、构建、目录、扩展点与验证清单 |
| [`RELEASE_NOTES.md`](RELEASE_NOTES.md) | 版本条目 |
| [`PRODUCT.md`](PRODUCT.md) | 产品定位、用户与设计约束 |
| [`docs/minecraft-maid-bridge.md`](docs/minecraft-maid-bridge.md) | Minecraft 女仆桥的协议与配置 |
| [`docs/requirements-2026-10-16.md`](docs/requirements-2026-10-16.md) | 需求记录 |

---

## 贡献

1. 从 `master` 建功能分支，保持提交范围单一。
2. 新服务注册到 `Composition/` 对应文件；服务用 Singleton，页面与 ViewModel 用 Transient。
3. 跨模块只走构造函数注入；确需运行时取用时，把入口限制在工具执行链等明确边界内。
4. 新 UI 使用 `DesignSystem` 的样式键与 Token；悬停只变色，按下位移 2.4px。
5. 新功能优先放进 `Modules/<Name>/`；只被一个模块使用的服务不要上升为全局服务。
6. 架构、存储格式或插件清单变化时，同步更新 `docs/` 与 `DEVELOPMENT.md`。

> 提交说明保持分条简写：修改了……、更改……为……、优化……。

---

## 协议

应用本体采用 **MIT + 非商业条款**：允许使用、修改与分发；未经版权持有者书面许可不得用于商业用途。
内置的 proot / CARE 组件遵循 **GPLv2**，完整文本见 [`LICENSE.txt`](LICENSE.txt)。

## 社区与支持

| 平台 | 链接 |
|---|---|
| GitHub | [gongchanwangzilin/WarmAsBefore](https://github.com/gongchanwangzilin/WarmAsBefore) |
| Bilibili | [space.bilibili.com/3546745275419060](https://space.bilibili.com/3546745275419060) |
| 赞助（爱发电） | [ifdian.net/a/jqyhxkxt1145141026](https://ifdian.net/a/jqyhxkxt1145141026) |
