using CommunityToolkit.Maui;
using WarmAsBefore.Services;
using WarmAsBefore.ViewModels;
using WarmAsBefore.Views;

namespace WarmAsBefore;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        App.WriteLog("CreateMauiApp: start");
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseMauiCommunityToolkit()
            .UseMauiCommunityToolkitMediaElement();

        // Design System
        builder.Services.AddSingleton<DesignSystem.Theme.ThemeManager>();
        // 中文别名包装：将已有的 ThemeManager 注入到 主题管理器（中文）中，便于中文标识符调用
        builder.Services.AddSingleton<DesignSystem.Theme.主题管理器>(sp => new DesignSystem.Theme.主题管理器(sp.GetRequiredService<DesignSystem.Theme.ThemeManager>()));

        // Core services
        builder.Services.AddSingleton<GameEngine>();
        builder.Services.AddSingleton<SettingsManager>();
        builder.Services.AddSingleton<StorageProvider>();
        builder.Services.AddSingleton<NotificationService>();
        builder.Services.AddSingleton<AudioController>();
        builder.Services.AddSingleton<SpeechService>();
        builder.Services.AddSingleton<MaterialLibrary>();
        builder.Services.AddSingleton<CharacterLibrary>();
        // 「全局设置 → 角色覆写」解析链：记忆/采样等参数按角色取值
        builder.Services.AddSingleton<CharacterSettingsResolver>();
        // 副设定：主设定为内核，副设定随经历漂移
        builder.Services.AddSingleton<CharacterSubProfileService>();
        // 立绘标定：素材按 1080×1920 逻辑画布标定一次，各处共用
        builder.Services.AddSingleton<PortraitCalibrationService>();
        // 立绘规范化：非标素材重采样成 1080×1920 标准画布文件
        builder.Services.AddSingleton<PortraitNormalizer>();
        // 记忆作用域：默认按存档隔离，打开「跨存档共用记忆」后合成一份（各记忆服务跟着它分文件）
        builder.Services.AddSingleton<MemoryScope>();
        // 人类记忆层：按重要性×时间衰减×复述×随机存取（与全量记忆库并存）
        builder.Services.AddSingleton<HumanMemoryService>();
        // 自定义性别库：素材包 JSON 导入 / 设置里手写 / JS·Python·Java 逻辑
        builder.Services.AddSingleton<GenderLibrary>();
        // 心理状态：目录 + 每个角色身上那几条（严重度/稳定度）
        builder.Services.AddSingleton<MentalHealthService>();
        // 身体感受：冷热饥渴想念，跟真实时间与天气缓慢变化
        builder.Services.AddSingleton<PhysiologyService>();
        // 画面生成：装扮 / 背景 / 立绘 / 地图背景
        builder.Services.AddSingleton<ImageGenerationService>();
        // 插件宿主：JS 钩子、设置声明、依赖表、崩溃日志、HTML 设置回调
        // 权限表要先注册：宿主的每个宿主函数都过它判定，DI 也不会替可选参数兜底。
        builder.Services.AddSingleton<PluginPermissions>();
        builder.Services.AddSingleton<PluginHost>();
        // 关系网：后台随对话自动长出来。按数值找近邻优先问量化模型，没配上就用本地统计（不花钱）
        builder.Services.AddSingleton<RelationNetworkService>();
        // 标签层：AI 打标签 → 分类检索 → 标签清单注入
        builder.Services.AddSingleton<MemoryTagService>();
        // 独立窗口：关系网 / 搜事件各开一扇，主窗口继续聊天
        builder.Services.AddSingleton<PageWindowService>();
        // 记忆分层总览：五层规模与现状汇总（界面/提示词统一从这里取）
        builder.Services.AddSingleton<MemoryOverviewService>();
        // 智能省钱：真实 token 用量 + 单价 → 花费表与「该不该总结」建议
        builder.Services.AddSingleton<CostEstimatorService>();
        // 黑屏：AI 可主动黑屏，但对话不受影响
        builder.Services.AddSingleton<ScreenBlackoutService>();
        builder.Services.AddSingleton<PetService>();
        builder.Services.AddSingleton<MapService>();
        // 强制标定的统一入口：导入路径 await 它、运行路径订阅它的事件
        builder.Services.AddSingleton<SceneCalibrationService>();
        builder.Services.AddSingleton<LingshuImporter>();

        // Module services
        builder.Services.AddSingleton<Modules.RealWorld.WeatherProvider>();
        builder.Services.AddSingleton<Modules.RealWorld.TimeProvider>();
        builder.Services.AddSingleton<Modules.RealWorld.PermissionBroker>();
        // 城市定位（GPS → IP 降级）与搜索联想：设置页「定位 / 选择城市」用
        builder.Services.AddSingleton<Modules.RealWorld.LocationService>();
        builder.Services.AddSingleton<Modules.RealWorld.PhysiologicalTracker>();
        builder.Services.AddSingleton<Modules.ApiManager.ApiGateway>();
        // 云存档：GitHub 仓库托管 + 每台设备独立目录 + 时间戳合并；合并后的记忆用对话模型去重
        builder.Services.AddSingleton<Services.CloudSync.MemoryReconcileService>();
        builder.Services.AddSingleton<Services.CloudSync.CloudSyncService>();
        builder.Services.AddSingleton<Modules.AiChat.ChatEngine>();
        builder.Services.AddSingleton<Modules.AiChat.MemoryVault>();
        builder.Services.AddSingleton<Modules.Automation.TaskOrchestrator>();
        builder.Services.AddSingleton<Modules.Automation.DailyDiaryWriter>();
        builder.Services.AddSingleton<Modules.Mcp.McpOrchestrator>();
        builder.Services.AddSingleton<Modules.Tools.RuntimeManager>();
        // 能力包（skill）：说明书渐进加载 + 可执行入口复用外部工具机制
        builder.Services.AddSingleton<SkillLibrary>();
        builder.Services.AddSingleton<Modules.Tools.ToolManager>(sp =>
            new Modules.Tools.ToolManager(
                sp.GetRequiredService<Modules.Tools.RuntimeManager>(),
                sp.GetRequiredService<Modules.Sandbox.SandboxPolicy>(),
                sp.GetRequiredService<SkillLibrary>(),
                sp.GetRequiredService<PluginHost>()));
        builder.Services.AddSingleton<Modules.Affection.AffectionLevelUpService>();
        builder.Services.AddSingleton<Modules.Battle.BattleDriverManager>();
        builder.Services.AddSingleton<Modules.Cg.CgStore>();
        builder.Services.AddSingleton<Modules.Cg.CgViewPayload>();
        builder.Services.AddSingleton<Modules.Showcase.ShowcaseStore>();
        builder.Services.AddSingleton<Modules.Worldbook.WorldbookGenerator>();
        builder.Services.AddSingleton<Modules.DataPack.PackImporter>();
        builder.Services.AddSingleton<Modules.SaveSystem.SaveManager>();
        // Minecraft 桥：UDP 发现 + TCP 会话；插件启用后才监听端口，未启用时保持安静
        builder.Services.AddSingleton<Modules.MinecraftBridge.MaidBridgeService>();
        builder.Services.AddSingleton<Modules.NovelImport.NovelAnalyzer>();
builder.Services.AddSingleton<Modules.GameModule.MiniGameEngine>();
builder.Services.AddSingleton<Modules.GameModule.ChessBrainService>();
builder.Services.AddSingleton<Modules.GameModule.GameSkillTracker>();
        builder.Services.AddSingleton<Modules.Market.ShopService>();
        builder.Services.AddSingleton<Modules.Market.GiftPanelService>();
        builder.Services.AddSingleton<Modules.Scene.SceneDirector>();
        // 更新器必须知道自己装的是哪一版，否则新版本会把自己当成 1.3.0，
        // 反过来把已成为历史的旧版当更新推给用户。版本号在 csproj 的 ApplicationDisplayVersion。
        builder.Services.AddSingleton(_ => new Modules.Update.UpdateService
        {
            CurrentVersion = Microsoft.Maui.ApplicationModel.AppInfo.Current.VersionString
        });
        builder.Services.AddSingleton<Modules.Sandbox.SandboxPolicy>(sp =>
            new Modules.Sandbox.SandboxPolicy(
                sp.GetRequiredService<StorageProvider>(),
                sp.GetRequiredService<SettingsManager>()));

        // 官方接入（真微信 / QQ）与运行时配置
        builder.Services.AddSingleton<Modules.RealChat.OfficialChatBridge>();
        builder.Services.AddSingleton<RuntimeConfigurator>();
        builder.Services.AddSingleton<Services.NovelLibrary>();
        // 微信好友：谁加过微信、谁是常驻聊天对象（角色"假微信"联系人列表）
        builder.Services.AddSingleton<Services.WeChatService>(sp =>
            new Services.WeChatService(
                sp.GetRequiredService<StorageProvider>(),
                sp.GetRequiredService<CharacterLibrary>()));

        // 多实体地图：用户 + 各 AI 角色各自的位置，以及 AI 拉拽移动的仲裁
        builder.Services.AddSingleton<Services.MapActorService>(sp =>
            new Services.MapActorService(sp.GetRequiredService<MapService>()));
        // #12 表情包库：导入/描述/给 AI 的清单；主对话、假微信、机器人通道共用同一个库
        builder.Services.AddSingleton<Services.StickerLibrary>(sp =>
            new Services.StickerLibrary(sp.GetRequiredService<StorageProvider>()));
        // #13 识图服务：独立配置的识图模型（表情包自动描述等看图任务）
        builder.Services.AddSingleton<Services.VisionService>(sp =>
            new Services.VisionService(sp.GetRequiredService<SettingsManager>()));
        // 量化模型（embedding）：把说法与记忆换成数值，近似的说法数值就近，按数值搜近邻
        // 没配置 / 调用失败 / 额度用完时各页自动退回本地共现统计（不花钱的那条路）
        builder.Services.AddSingleton<Services.EmbeddingService>(sp =>
            new Services.EmbeddingService(sp.GetRequiredService<StorageProvider>(),
                sp.GetRequiredService<SettingsManager>(),
                sp.GetRequiredService<HumanMemoryService>()));

        // ViewModels
        builder.Services.AddTransient<TitleViewModel>();
        builder.Services.AddTransient<SetupViewModel>();
        builder.Services.AddTransient<MainGameViewModel>();
        builder.Services.AddTransient<CharacterSelectViewModel>();
        builder.Services.AddTransient<SettingsViewModel>();
        builder.Services.AddTransient<PhoneViewModel>();
        builder.Services.AddTransient<WeChatViewModel>();
        builder.Services.AddTransient<MapViewModel>();
        builder.Services.AddTransient<GalleryViewModel>();
        builder.Services.AddTransient<OutfitViewModel>();
        builder.Services.AddTransient<SaveViewModel>();
        builder.Services.AddTransient<GameViewModel>();
        builder.Services.AddTransient<WorldbookViewModel>();
        builder.Services.AddTransient<ForceCastViewModel>();

        // Pages
        builder.Services.AddTransient<TitlePage>();
        builder.Services.AddTransient<SetupPage>();
        builder.Services.AddTransient<MainGamePage>();
        builder.Services.AddTransient<CharacterSelectPage>();
        builder.Services.AddTransient<SettingsPage>();
        builder.Services.AddTransient<SettingsSectionPage>();
        builder.Services.AddTransient<PhonePage>();
        builder.Services.AddTransient<WeChatPage>();
        // QQ 壳：复用 WeChatViewModel（构造后 SetShell("qq")）；论坛页另有本地数据 VM
        builder.Services.AddTransient<Views.QqPage>();
        builder.Services.AddTransient<Views.QqForumPage>();
        builder.Services.AddTransient<QqForumViewModel>();
        builder.Services.AddTransient<MapPage>();
        builder.Services.AddTransient<SceneCalibrationViewModel>();
        builder.Services.AddTransient<SceneCalibrationPage>();
        builder.Services.AddTransient<GalleryPage>();
        builder.Services.AddTransient<OutfitPage>();
        builder.Services.AddTransient<OutfitViewModel>();
        builder.Services.AddTransient<SavePage>();
        builder.Services.AddTransient<GamePage>();
        builder.Services.AddTransient<PetPage>();
        builder.Services.AddTransient<NovelSelectPage>();
        builder.Services.AddTransient<NovelWorldPage>();
        builder.Services.AddTransient<WorldbookPage>();
        builder.Services.AddTransient<CharacterLibraryPage>();
        // 角色工作室：列表那一行点进来的一页
        builder.Services.AddTransient<CharacterStudioPage>();
        builder.Services.AddTransient<CharacterStudioViewModel>();
        builder.Services.AddTransient<ShopPage>();
        builder.Services.AddTransient<ShowcaseListPage>();
        builder.Services.AddTransient<ShowcaseEditPage>();
        builder.Services.AddTransient<ShowcasePlayPage>();
        builder.Services.AddTransient<AffectionLevelUpPage>();
        builder.Services.AddTransient<CgViewPage>();
        builder.Services.AddTransient<BattlePage>();
        builder.Services.AddTransient<MaterialsPage>();
        builder.Services.AddTransient<PortraitCalibrationPage>();
        builder.Services.AddTransient<SubProfilePage>();
        builder.Services.AddTransient<MemoryManagePage>();
        // 关系网：会生长的枝条视图 + 参数模型 + 思考模型
        builder.Services.AddTransient<Views.MemoryGraphPage>();
        builder.Services.AddTransient<MemoryGraphViewModel>();
        // 搜事件：可另开一扇窗（PageWindowService 按类型从 DI 取页，所以这页必须注册）
        builder.Services.AddTransient<Views.MemorySearchPage>();
        builder.Services.AddTransient<MemorySearchViewModel>();
        builder.Services.AddTransient<Views.SceneLibraryPage>();
        builder.Services.AddTransient<SceneLibraryViewModel>();
        // 中文页面别名已由 global using 类型别名提供，无需额外在 DI 中注册
        builder.Services.AddTransient<NovelSelectViewModel>();
        builder.Services.AddTransient<NovelWorldViewModel>();
        builder.Services.AddTransient<CharacterLibraryViewModel>();
        builder.Services.AddTransient<ShopViewModel>();
        builder.Services.AddTransient<ShowcaseListViewModel>();
        builder.Services.AddTransient<ShowcaseEditViewModel>();
        builder.Services.AddTransient<ShowcasePlayViewModel>();
        builder.Services.AddTransient<AffectionLevelUpViewModel>();
        builder.Services.AddTransient<CgViewViewModel>();
        builder.Services.AddTransient<MaterialsViewModel>();
        builder.Services.AddTransient<PortraitCalibrationViewModel>();
        builder.Services.AddTransient<SubProfileViewModel>();
        builder.Services.AddTransient<MemoryManageViewModel>();
        builder.Services.AddTransient<Views.ForceCastPage>();
        // 心理状况：按当前角色编辑轻重与稳定度
        builder.Services.AddTransient<Views.MentalHealthPage>();
        builder.Services.AddTransient<MentalHealthViewModel>();
        // 插件中心：启停 / 依赖 / 原生设置 / 崩溃日志 / 工坊
        builder.Services.AddTransient<Views.PluginCenterPage>();
        builder.Services.AddTransient<PluginCenterViewModel>();
        builder.Services.AddTransient<Views.PluginSettingsPage>();
        // 插件权限台：一个插件一整套「声明 ∩ 授予」的开关与留痕
        builder.Services.AddTransient<Views.PluginPermissionsPage>();
        builder.Services.AddTransient<PluginPermissionsViewModel>();
        // 存档高级设置：按存档编辑插件 / 工具 / 角色白名单
        builder.Services.AddTransient<Views.SaveRulesPage>();
        builder.Services.AddTransient<SaveRulesViewModel>();
        // 中文视图模型别名已由 global using 类型别名提供，无需额外在 DI 中注册

        App.WriteLog("CreateMauiApp: building");
        return builder.Build();
    }
}