# 开发与维护

> 面向维护、扩展和排查问题的人。功能与使用说明见 [`README.md`](README.md)；
> 分层、启动链路与数据流见 [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md)；
> 各组件职责见 [`docs/COMPONENTS.md`](docs/COMPONENTS.md)；
> 插件与外部工具契约见 [`docs/PLUGINS.md`](docs/PLUGINS.md)。

---

## 1. 环境与版本

| 项目 | 值 |
|---|---|
| 当前版本 | `1.55.0`（`ApplicationVersion` 56） |
| SDK | .NET 10 SDK |
| MAUI 工作负载 | `maui`；只构建 Android 时可用 `maui-android` |
| Windows | Windows 10 19041+，目标框架 `net10.0-windows10.0.19041.0` |
| Android | Android 7.0+（`minSdk 24`，`targetSdk 36`），目标框架 `net10.0-android` |
| UI 栈 | .NET MAUI + WinUI 3（Windows）/ MAUI Android |
| 架构 | MVVM + 构造函数依赖注入；Service 单例，页面与 ViewModel 瞬时 |
| 主要依赖 | CommunityToolkit.Mvvm、CommunityToolkit.Maui、Jint、SkiaSharp、System.Speech |

NuGet 版本以 `WarmAsBefore.csproj` 为准，不在本文档复制版本号。

### 版本号同步

发布新版本时同时修改：

1. `WarmAsBefore.csproj` 的 `ApplicationDisplayVersion`（如 `1.51.0`）与 `ApplicationVersion`（整数，如 `51`）。
2. `README.md` 顶部徽章、版本表与「这一版在做什么」。
3. `RELEASE_NOTES.md` 增加对应条目。
4. `docs/` 中涉及版本或目录结构的说明。

---

## 2. 构建与运行

### Windows 调试构建

```powershell
dotnet build WarmAsBefore.csproj -c Debug `
  -f net10.0-windows10.0.19041.0 `
  -p:RuntimeIdentifierOverride=win-x64 `
  -v:q -nologo
```

产物：

```text
bin/Debug/net10.0-windows10.0.19041.0/win-x64/WarmAsBefore.exe
```

直接启动该 EXE 即可；调试时用支持 .NET 10 的 Visual Studio / Rider 附加，
或使用 `dotnet build` + 手动启动。应用启动日志写入 `Documents/warm_startup.log`
（Windows），性能打点写入数据根目录的 `perf.log`。

### Windows 发布

```powershell
dotnet publish WarmAsBefore.csproj -c Release `
  -f net10.0-windows10.0.19041.0 `
  -p:RuntimeIdentifierOverride=win10-x64 `
  -p:WindowsPackageType=None `
  -p:WindowsAppSDKSelfContained=true `
  -p:UseMonoRuntime=false `
  -v:q -nologo
```

产物：

```text
bin/Release/net10.0-windows10.0.19041.0/win10-x64/publish/WarmAsBefore.exe
```

构建约束：

- 必须使用 `-p:RuntimeIdentifierOverride=...`，不要改成 `-r win-x64` 或
  `-p:RuntimeIdentifier=win-x64`。后者会触发 WindowsAppSDK #3337：产物找不到
  XAML 主题资源，启动后没有窗口。
- `WindowsPackageType=None` 表示不生成 MSIX，直接产出 EXE。
- `WindowsAppSDKSelfContained=true` 与 `UseMonoRuntime=false` 是当前 Windows
  产物的运行前提，不要删除。
- Windows 目标由自定义 `Program.cs` 接管入口；`DISABLE_XAML_GENERATED_MAIN`
  必须保留，否则会出现重复 `Main`。

### Android

```powershell
dotnet build WarmAsBefore.csproj -c Debug -f net10.0-android
```

产物：

```text
bin/Debug/net10.0-android/com.companyname.warmasbefore-Signed.apk
```

Android 构建需要可用的 Android SDK、JDK 与 `maui-android` 工作负载。
本机缺少 Android SDK 时会报 `XA5300`，此时只能验证 Windows 分支。

### 构建故障

| 现象 | 处理 |
|---|---|
| XAML 编译器锁住 `obj/.../intermediatexaml/WarmAsBefore.dll` | 关闭 `Microsoft.UI.Xaml.Markup.Compiler` 编译器服务器后重新构建 |
| `XC0009` | 不要给 `Border.Background` 绑 `{DynamicResource ...}`；用 `BackgroundColor` 或内层 `BoxView.Color` |
| `XA5300` / 找不到 Android SDK | 安装 Android SDK 与 JDK，或只构建 Windows 目标 |
| Windows 产物启动无窗口 | 检查是否误用了 `-r win-x64`，改回 `RuntimeIdentifierOverride` |
| 中文日志/源码乱码 | 编辑与读取统一用 UTF-8；PowerShell 管道可能破坏编码，diff 用 `git diff --output=文件` |

---

## 3. 目录与组合根

```text
src/WarmAsBefore/
├── App.xaml(.cs)              # 全局资源、窗口生命周期、启动日志、退出前保存
├── AppShell.xaml(.cs)         # Shell 路由、导航打点、页面转场
├── MauiProgram.cs             # 组合根：按顺序调用 Composition/*
├── Composition/               # DI 注册（设计系统 / 核心 / 模块 / 角色 / 插件 / VM / 页面）
├── Models/                    # 纯数据与数学辅助，不发起 IO
├── Services/
│   ├── Core/                  # 存储、设置、引擎、运行时配置、窗口、日志、本地化
│   ├── Character/             # 角色库、立绘、素材、心理、生理、性别、设置解析
│   ├── Memory/                # 人类记忆、标签、关系网、世界树、Embedding、成本
│   ├── Media/                 # 生图、识图、语音、表情包
│   ├── Plugins/               # 插件宿主、权限、体检、技能库
│   ├── World/                 # 地图、地图角色、桌宠、微信、小说库、数据包
│   └── CloudSync/             # 云存档与跨设备记忆合并
├── Modules/                   # 业务模块：对话、商店、游戏、战斗、MCP、桥接、自动化等
├── ViewModels/                # Main / Character / Settings / Memory / Story / Chat / Games / Save / Shop
├── Views/                     # 与 ViewModels 同构分组；页面只做布局、平台事件与导航
├── DesignSystem/              # 色板、Token、新拟物 / 卡片 / 玻璃 / 文本 / 按钮样式
├── Controls/                  # 自定义控件
├── Converters/                # 值转换器
├── Drawables/                 # 棋盘、地图、关系网等绘制器
├── Helpers/                   # 动效、悬停/按下、通用 UI 工具
├── Platforms/                 # Windows / Android 宿主与平台专属实现
├── Resources/                 # 字体、图片、本地化资源
├── plugins/                   # 仓库内示例插件；运行时装到数据目录
└── docs/                      # 架构、组件、插件、桥接与需求文档
```

命名空间保持扁平，移动文件不改变调用点：

| 目录 | 命名空间 |
|---|---|
| `Services/**` | `WarmAsBefore.Services` |
| `ViewModels/**` | `WarmAsBefore.ViewModels` |
| `Views/**` | `WarmAsBefore.Views` |
| `Models/**` | `WarmAsBefore.Models` |
| `Modules/<Name>/**` | `WarmAsBefore.Modules.<Name>` |
| `Composition/**` | `WarmAsBefore.Composition` |

### 注册规则

`MauiProgram.CreateMauiApp()` 只负责创建 builder 并按层调用：

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

| 文件 | 放什么 |
|---|---|
| `Composition/Registration.DesignSystem.cs` | 主题、设计系统服务 |
| `Composition/Registration.Core.cs` | 存储、设置、引擎、通知、音频、角色库、记忆、媒体、窗口 |
| `Composition/Registration.Modules.cs` | 对话、天气、定位、生理、MCP、工具、商店、战斗、存档、云同步 |
| `Composition/Registration.Character.cs` | 角色包、形象包、换装、头像裁剪 |
| `Composition/Registration.Plugins.cs` | 插件宿主、权限、技能库、沙箱、工具管理器 |
| `Composition/Registration.ViewModels.cs` | 全部 ViewModel（Transient） |
| `Composition/Registration.Pages.cs` | 全部页面（Transient） |

规则：

- 无状态或全局唯一的服务用 Singleton；页面与 ViewModel 用 Transient。
- 跨模块只走构造函数注入，不使用静态字段互相取用。
- 需要把设置或运行时能力下发给多个模块时，走 `RuntimeConfigurator`，
  不要在页面里直接改别的服务状态。
- 新页面还要在 `AppShell.RegisterRoutes()` 注册路由；参数放查询串并做 URL 转义。

---

## 4. 常见改动

### 新增服务

1. 只服务一个模块 → 放进 `Modules/<Name>/`；服务多个模块 → 放进 `Services/<功能>/`。
2. 在对应 `Composition/Registration.*.cs` 注册。
3. 需要设置、通知、工具或插件钩子时，在 `RuntimeConfigurator` 接线。
4. 需要落盘时使用 `StorageProvider` 的相对键，并考虑云同步与数据包导出。

### 新增页面

1. `Views/<分组>/` 建 XAML + code-behind，`ViewModels/<分组>/` 建 VM。
2. 分别注册到 `Registration.Pages.cs` 与 `Registration.ViewModels.cs`。
3. 在 `AppShell.RegisterRoutes()` 注册路由；导航参数通过 `IQueryAttributable`
   或页面的 `ApplyQueryAttributes` 接收。
4. 页面根背景使用 `{DynamicResource PageBgBrush}`；颜色、圆角、阴影与动效
   使用 `DesignSystem` 的键与 Token。
5. 页面只做布局、平台事件与导航；业务状态放 VM。
6. 悬停只变色，按下位移 2.4px；不做假加载；设置类弹窗保持原有样式。

### 新增模块

1. 在 `Modules/<Name>/` 建服务与模型；模块内部可互相引用，跨模块走注入。
2. 在 `Registration.Modules.cs` 注册。
3. 需要工具、插件钩子、对话上下文或通知时，在 `RuntimeConfigurator` 接线。
4. 独立存储使用自己的键前缀（如 `showcase/`、`battles/`），不要与全局键混用。
5. 有外部进程或协议时，先定义契约与失败兜底，再写 UI。

### 修改存储格式

1. 新字段给默认值，保证旧档可以反序列化。
2. 需要迁移时在对应服务的 `Restore` / `Load` 路径做一次性修复，并记录
   `LastLoadNote` 或启动日志。
3. 同步更新 `docs/ARCHITECTURE.md` 的存储布局与本文档的验证清单。
4. 数据包导出会递归清理密钥字段；新增密钥字段使用可识别的命名
   （`*Key`、`*Secret` 等）。

### 修改插件系统

插件清单、钩子、JS API、权限与回调协议都有外部契约。修改前先读
[`docs/PLUGINS.md`](docs/PLUGINS.md)，并同步更新：

- `Services/Plugins/PluginHost.cs`：清单解析、钩子执行、宿主函数、回调服务器。
- `Services/Plugins/PluginPermissions.cs`：能力目录、风险档、默认策略。
- `Services/Plugins/PluginDoctor.cs`：体检规则与钩子白名单。
- `ViewModels/Settings/PluginCenterViewModel.cs`、`PluginPermissionsViewModel.cs`：
  插件中心与权限台。
- `Views/Settings/PluginSettingsPage.xaml.cs`：`native` 字段渲染。

---

### v1.54.0 新增落点（年龄 / 定位 / 云存档 / 地图照片与天气）

| 功能 | 主要文件 | 要点 |
|---|---|---|
| 角色年龄、生日、自然增长 | `Models/Character.cs`、`Services/Character/CharacterAgeService.cs`、`ViewModels/Character/CharacterStudioViewModel.cs`、`Views/Character/CharacterStudioPanel.xaml` | 档里只存 `Age` 与 `AgeAsOf`；自然增长读取时按生日次数现算，不每年写盘；旧档默认 `null/""/true` 可反序列化 |
| 用户生日与 AI 人设 | `Models/State.cs`（`UserBirthday`）、`Services/Core/RuntimeConfigurator.cs`、`ViewModels/Settings/SettingsViewModel.cs`、`Views/Settings/SettingsSectionPage.xaml`（Card28） | 生日归一在 `CharacterAgeService.NormalizeBirthday`；人设段落失败只记日志 |
| AI 工具 `character_age` | `Modules/Tools/ToolManager.Age.cs`、`ToolManager.cs` | 改年龄才刷新 `AgeAsOf`；走 `CharacterLibrary.UpdateAsync` 落盘 |
| 云存档私人仓库门禁 | `Services/CloudSync/CloudSyncService.cs`、`ViewModels/Save/CloudSavesViewModel.cs`、`Views/Settings/CloudSavesPage.xaml` | `GET /repos/{owner}/{repo}` 读 `private/visibility`；按仓库+令牌缓存 10 分钟；九个远端入口统一走 `RequirePrivateRepoAsync` |
| 设备定位 / IP 估算 | `Modules/RealWorld/LocationService.cs`、`Modules/RealWorld/PermissionBroker.cs`、`Platforms/Windows/WindowsDeviceLocation.cs`（整文件 `#if WINDOWS`） | 设备定位失败返回 `null` 并写 `LastDeviceError`；IP 永远 `IsEstimate=true`，由 UI 让用户确认，禁止静默降级 |
| 谷歌地点照片 | `Services/World/PlacePhotoService.cs`、`Services/World/CoordConvert.cs`、`Views/Games/MapCreatePage.xaml`、`MapCreateViewModel.cs` | Places Text Search → Place Photo（12MB 封顶、image/* 校验），无照片回退 Street View Static；高德来源先 GCJ-02→WGS84；Key 在 Preferences `map.google.key` 或 `WAB_GOOGLE_KEY` |
| 地图天气来源 | `Models/MapData.cs`（`WeatherMode/WeatherCity`、`MapWeatherModes`）、`Modules/RealWorld/WeatherProvider.cs`、`ViewModels/Main/MainGameViewModel.cs`、`MapImportService.cs`、`MapCreateViewModel/Page`、`MapCanvasViewModel/MapPage` | 导入图默认 `map`；`WeatherProvider` 地图覆盖与用户位置两套状态独立；`FetchForUserAsync` 永远按用户设置；主界面在 `MapChanged` 按指纹重应用 |



### v1.55.0 性能与诊断落点

| 主题 | 主要文件 | 要点 |
|---|---|---|
| UI 卡顿看门狗 | `Services/Core/UiStallMonitor.cs`、`Services/Core/PerfLog.cs` | Dispatcher 100ms 心跳；间隔 >200ms 记 `UI 卡顿`（>5s 视为挂起）；`PerfLog.Phase` 由导航/页面边界写入，日志自带「当时在哪一步」 |
| 慢帧诊断 | `Drawables/MapCanvasDrawable.cs`、`Drawables/MemoryGraphDrawable.cs` | 单帧 >33ms 记 `画布 · 慢帧` / `记忆图 · 慢帧`，同标签 2 秒节流 |
| 性能模式 | `Models/State.cs`（`PerformanceMode`）、`DesignSystem/Theme/MotionTokens.cs`、`MotionPrefs.cs`、`DesignSystem/Worlds/StyleWorld.cs`、`ThemeManager.Republish()` | `MotionTokens.Disabled = SystemDisabled || PerformanceMode`；`FlatShadows` 改平阴影；`ThemeManager.Republish()` 重发 DynamicResource；只在开关变化时下发 |
| 启动预热 | `Services/Core/RuntimeConfigurator.cs`（`WarmUpAsync`） | 启动 4 秒后并行读：存档/记忆/关系网/标签/用量/能力包 + 角色库 + 地图；低内存模式跳过 |
| 存档列表缓存 | `Modules/SaveSystem/SaveManager.cs` | `Commit`/`Rename` 把 meta 回填 `_slotCache`，避免自动存档后 `List()` 重读整份存档 |
| 并行读盘与批量列表 | `CharacterStudioViewModel.OpenAsync`、`SaveViewModel`、`CharacterLibraryViewModel`、`WeChatViewModel`、`MainGameViewModel` | 互不依赖的两次读用 `Task.WhenAll`；历史/联系人用 `RangeObservableCollection.ReplaceRange` 一次通知 |
| 天气缓存 | `Modules/RealWorld/WeatherProvider.cs` | 成功 5 分钟 / 失败回退 30 秒 TTL；缓存键含位置+源+BaseUrl+Key 摘要；同 key 并发共享一个 Task；位置/源变化失效；`PeekAsync` 不缓存 |



## 5. 存储与数据

`StorageProvider` 是所有 JSON 的唯一落盘入口：

- 根目录：`FileSystem.AppDataDirectory/WarmAsBefore`，与 `App.RootDirectory` 同值。
  Windows 通常在 `%LOCALAPPDATA%\WarmAsBefore\`；Android 在应用私有目录。
- 键是相对键，例如 `settings`、`saves/<id>`、`plugins/<id>/settings`；
  自动补 `.json`，拒绝绝对路径、盘符、`.` 与 `..`。
- 写入使用「同目录临时文件 + 原子替换」，重试 5 次，避免读到半截 JSON。
- JSON 损坏时先备份为 `<name>.corrupt-<UTC 时间>.json`，再抛出；调用方决定是否用默认值。
- 保存/删除触发 `Changed` 事件，云存档据此收集待上传的键。

| 路径 / 键 | 内容 |
|---|---|
| `settings.json` | AI 端点、主题、窗口、语音、天气、插件钩子开关等全局设置；`UserBirthday`（用户生日，`MM-dd`） |
| `characters.json` | 角色库与角色状态；`CharacterProfile.Age/Birthday/AgeAsOf/NaturalAging`（年龄/生日/自然增长） |
| `gameengine.json` | 引擎运行时状态 |
| `saves/<saveId>` | 存档快照：状态、角色状态、副设定、最近聊天 |
| `memories/<scope>` | 全量对话记忆库 |
| `human_memories/` | 按存档隔离的人类记忆；开启共用后回到单文件 |
| `assets/` | 立绘、背景、音频、表情包等素材 |
| `maps/` | 地图背景；`maps/photos/` 为取景照片（手机随拍、电脑导入与谷歌地点照片） |
| `cg/` | CG 目录与解锁状态 |
| `showcase/` | 展示案脚本与素材 |
| `plugins/<id>/plugin.json` | 插件清单（文件，不经 `StorageProvider`） |
| `plugins/<id>/settings.json` | 插件自己的设置 |
| `plugins/<id>/files/` | 插件文件区（单文件 1MB、合计 8MB） |
| `plugins/state.json` | 插件启停状态 |
| `plugins/permissions.json` | 插件授权、询问答案与留痕 |
| `plugin_logs/<id>.log` | 插件崩溃、权限拦截与 JS 异常 |
| `warm_startup.log` | 启动日志（Windows 在 `Documents/`） |
| `perf.log` | 性能打点 |

素材、插件与日志直接走文件系统；其余状态优先走 `StorageProvider`，
以便统一迁移、导出与云同步。

---

## 6. 验证清单

仓库没有纳入版本管理的自动化测试工程；`.local/` 下的临时测试项目只用于本地排查，
不作为验证依据。改动后按下面顺序验证：

1. **构建**：用第 2 节的 Windows 调试命令构建，确认没有新增 error；
   既有警告（如 `MVVMTK0045`）不应因本次改动增加。
2. **启动**：运行 EXE，确认窗口出现、标题页可进入、启动日志无未处理异常。
3. **主流程**：角色选择 → 新开一局 → 主界面 → 发送一条消息。
   未配置 AI 端点时应走离线回复；配置端点时确认流式回复与错误提示。
4. **设置**：修改主题、窗口、语音或插件开关，保存后确认立即生效；
   重启后确认设置仍在。
5. **存档**：手动保存 → 读档 → 退出应用，确认退出前自动保存与 `LastLoadNote` 正常。
6. **记忆**：新建第二份存档，确认人类记忆与对话记忆按作用域隔离；
   切换「跨存档共用」后确认作用域重载。
7. **插件**：安装一个示例插件 → 体检无 Error → 在权限台逐项授权 →
   试跑 → 观察钩子返回值与 `plugin_logs/<id>.log`。
8. **UI 约束**：新页面根背景为 `PageBgBrush`；悬停只变色；按下位移 2.4px；
   `Border.Background` 没有绑定 `DynamicResource`。
9. **平台**：Windows 必测；具备 Android SDK 时再构建并安装 Android 包，
   重点看屏幕方向、定位权限、文件选择与 proot 沙箱。
10. **年龄与生日（v1.54）**：角色工作室填年龄/生日并保存 → 重开角色确认回填；
    填一个「明天」的生日并把 `AgeAsOf` 临时改到去年，确认主界面提示词里的岁数 +1；
    关掉自然增长后确认岁数冻结；设置 → 身份与心理填用户生日，确认对话人设里出现。
11. **定位（v1.54）**：Windows 打开定位服务后点「定位」，确认状态显示「设备定位」与精度；
    关掉系统定位再点，确认显示具体设置指引而不是自动跳到 IP；IP 候选出现时必须点「采用」才写入。
12. **云存档私人仓库（v1.54）**：填一个公开仓库 → 测试/同步/刷新均应被拒绝并提示改 Private；
    改成私人仓库（令牌有 `repo` 权限）后点「验证私人仓库 / 测试连接」确认通过。
13. **地图照片与天气（v1.54）**：在创建页填 Google Key（启用 Places API）→ 取照片，确认场景背景与
    `maps/photos/` 里有文件；无 Key 时应直接返回人话、不发请求。改地图天气为三种模式，
    确认主界面天气与场景天气门控跟着变；重新导入一张高德/OSM 地图，确认默认按当地天气。
14. **性能诊断（v1.55）**：正常操作后看 `perf.log`：应出现 `UI 看门狗 已启动`；卡顿时有 `UI 卡顿 … · <阶段>`
    与 `画布 · 慢帧` / `记忆图 · 慢帧`（若有）。导航/页面装载行应带毫秒数，SLOW 行能直接定位到阶段。
15. **性能模式（v1.55）**：设置 → 平台与运行 → 实验卡打开「性能模式」→ 页面/卡片进场应立刻变为直落
    （无补间）、卡片阴影变平；关掉后恢复；重启后开关保持；系统「减少动画」开启时关掉性能模式仍不应恢复动效。
16. **热路径复查（v1.55）**：自动保存几次后进角色工作室/存档页/角色库，`perf.log` 中「读角色与存档列表」
    与「读取列表」应明显低于旧值；天气行 `WeatherProvider.缓存未命中` 在 5 分钟内不应重复出现
    （设置页「测试天气」每次都应出现）。地图拖拽/平移仍应跟手，松手后终态不残留。
17. **提交前**：`git status --porcelain` 只包含本次要提交的路径；
    不提交密钥、日志、`bin/`、`obj/`、`.local/`、用户数据或未完成的插件包。

---

## 7. 已知限制

- **Android 构建依赖本机 SDK**：缺少 Android SDK / JDK 时只能验证 Windows 分支，
  Android 行为需在具备 SDK 的环境复测。
- **AI 能力依赖端点**：端点不可用或令牌失效时，对话走离线回复；
  生图、识图、语音与真实模型回话不能标记为已验证。
- **没有内嵌 WebView**：插件页面与 HTML 设置页一律用系统浏览器打开；
  页面回调只监听 `127.0.0.1`，token 只在当前进程有效。
- **插件 JS 是同步沙箱**：不支持 `await` / `require` / `import`；
  长任务不要放在 `persona` 这种每轮都跑的钩子里。
- **插件不是进程隔离**：插件与应用同进程运行，只应安装可信来源的插件；
  能力边界由宿主函数与「声明 ∩ 授予」控制。
- **WindowsAppSDK #3337**：发布必须走 `RuntimeIdentifierOverride`；
  直接指定 `RuntimeIdentifier` 会得到无窗口的产物。
- **XAML 编译器偶发锁文件**：关闭 `Microsoft.UI.Xaml.Markup.Compiler`
  编译器服务器后重建。
- **云存档依赖用户仓库**：同步使用用户自己的 GitHub 私人仓库与令牌；
  令牌失效只影响同步，不影响本地存档。公开仓库会在所有远端入口被门禁拒绝，
  私有性缓存 10 分钟，改完可见性需点「测试连接 / 验证 / 刷新」立即重查。
- **Windows 定位依赖系统授权**：无 MSIX 的 EXE 走 `Windows.Devices.Geolocation`，
  `RequestAccessAsync` 的行为随 Windows 版本有差异；系统定位关闭或未允许桌面应用访问位置时，
  设置页会显示具体开启路径，首次打开定位服务后可能需重试一次（12 秒超时）。
- **IP 定位只到城市级**：这是网络出口决定的，不能当精确位置；设置页只把它当候选，必须用户确认。
- **谷歌地点照片需要自备 Key**：Places API（街景回退另需 Street View Static API），
  且各地区的覆盖与配额由 Google 决定；受限网络下无法端到端验证。
- **天气有 5 分钟缓存**：主界面/场景/生理共用同一份缓存，5 分钟内天气变化不会立即体现；
  设置页「测试天气」强制实时。断网回退值只缓存 30 秒。若需要立刻刷新，调用 `Fetch(force: true)`。
- **性能模式只改观感**：减少动效与卡片阴影，不改布局、功能或数据；系统「减少动画」偏好优先于它。
- **UI 卡顿看门狗是诊断而不是修复**：它只在 `perf.log` 留下 `UI 卡顿` 行；卡顿本身要靠慢帧/阶段标记
  继续定位。窗口最小化/挂起超过 5 秒的间隔会被当成挂起，不记卡顿。
- **外部战斗/工具驱动是可选路径**：驱动缺失或响应不合契约时回退内置实现；
  外部队列与协议变更需要同时更新文档与体检规则。

---

## 8. 文档与提交

文档分工：

| 文件 | 维护内容 |
|---|---|
| `README.md` | 项目入口、功能地图、快速开始、数据位置、文档索引 |
| `docs/ARCHITECTURE.md` | 分层、组合根、启动链路、数据流、线程与平台差异 |
| `docs/COMPONENTS.md` | 按目录的组件职责与协作速查 |
| `docs/PLUGINS.md` | 插件清单、钩子、JS API、权限、沙箱、回调与排错 |
| `DEVELOPMENT.md` | 环境、构建、目录、常见改动、验证清单、已知限制 |
| `PRODUCT.md` | 产品定位、用户与设计约束 |
| `RELEASE_NOTES.md` | 版本条目 |

提交约定：

- 说明分条简写：修改了……、更改……为……、优化……。
- 只暂存明确路径，不使用 `git add -A` / `git add .`。
- 已推送的提交不改写；后续修改另起提交，不 force-push。
- 架构、存储格式、插件清单或权限模型变化时，同步更新对应文档。

---

## 9. 快速索引

| 要找 | 文件 |
|---|---|
| DI 注册 | `MauiProgram.cs`、`Composition/Registration.*.cs` |
| 路由与转场 | `AppShell.xaml.cs` |
| 设置恢复与下发 | `Services/Core/SettingsManager.cs`、`Services/Core/RuntimeConfigurator.cs` |
| JSON 落盘 | `Services/Core/StorageProvider.cs` |
| 游戏状态 | `Services/Core/GameEngine.cs` |
| 对话管线 | `Modules/AiChat/ChatEngine.cs` |
| 对话记忆/日记 | `Modules/AiChat/MemoryVault.cs` |
| 存档/读档 | `Modules/SaveSystem/SaveManager.cs` |
| 性能诊断 / 卡顿日志 | `Services/Core/PerfLog.cs`、`Services/Core/UiStallMonitor.cs` |
| 性能模式（减动效/平阴影） | `Models/State.cs`、`DesignSystem/Theme/MotionTokens.cs`、`DesignSystem/Worlds/StyleWorld.cs`、`DesignSystem/Theme/ThemeManager.cs` |
| 年龄 / 生日 / 自然增长 | `Services/Character/CharacterAgeService.cs`、`Models/Character.cs` |
| 设备定位 / IP 估算 | `Modules/RealWorld/LocationService.cs`、`Platforms/Windows/WindowsDeviceLocation.cs` |
| 云存档私人仓库门禁 | `Services/CloudSync/CloudSyncService.cs` |
| 地图天气来源 | `Models/MapData.cs`、`Modules/RealWorld/WeatherProvider.cs`、`ViewModels/Main/MainGameViewModel.cs` |
| 谷歌地点照片 | `Services/World/PlacePhotoService.cs`、`Services/World/CoordConvert.cs` |
| 商店与 Buff | `Modules/Market/ShopService.cs` |
| 送礼面板 | `Modules/Market/GiftPanelService.cs` |
| QQ/微信通道 | `Modules/RealChat/OfficialChatBridge.cs` |
| 插件宿主 | `Services/Plugins/PluginHost.cs` |
| 插件权限 | `Services/Plugins/PluginPermissions.cs` |
| 插件体检 | `Services/Plugins/PluginDoctor.cs` |
| 插件中心 | `ViewModels/Settings/PluginCenterViewModel.cs` |
| 设计系统 Token | `DesignSystem/Tokens/DesignTokens.cs` |
| 色板 | `DesignSystem/Theme/ColorPalette*.xaml` |
| 批量集合 | `Models/RangeObservableCollection.cs` |

> 重大架构变更（新模块、存储格式、跨模块契约、插件清单或权限模型）必须同步修改本文档与 `docs/` 对应页面。
