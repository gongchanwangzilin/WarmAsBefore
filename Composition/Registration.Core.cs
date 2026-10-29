using Microsoft.Extensions.DependencyInjection;
using WarmAsBefore.Services;

namespace WarmAsBefore.Composition;

/// <summary>应用基础设施：存储、设置、运行时、窗口、平台适配与跨模块服务。</summary>
public static class CoreRegistration
{
    public static IServiceCollection AddCoreServices(this IServiceCollection services)
    {
        // 运行时状态与持久化
        services.AddSingleton<GameEngine>();
        services.AddSingleton<SettingsManager>();
        services.AddSingleton<StorageProvider>();
        services.AddSingleton<NotificationService>();
        services.AddSingleton<AudioController>();
        services.AddSingleton<SpeechService>();

        // 素材与角色基础
        services.AddSingleton<MaterialLibrary>();
        services.AddSingleton<CharacterLibrary>();
        // 「全局设置 → 角色覆写」解析链：记忆/采样等参数按角色取值
        services.AddSingleton<CharacterSettingsResolver>();
        // 副设定：主设定为内核，副设定随经历漂移
        services.AddSingleton<CharacterSubProfileService>();
        // 立绘标定：素材按 1080×1920 逻辑画布标定一次，各处共用
        services.AddSingleton<PortraitCalibrationService>();
        // 立绘规范化：非标素材重采样成 1080×1920 标准画布文件
        services.AddSingleton<PortraitNormalizer>();
        // 记忆作用域：默认按存档隔离，打开「跨存档共用记忆」后合成一份
        services.AddSingleton<MemoryScope>();
        // 人类记忆层：按重要性×时间衰减×复述×随机存取
        services.AddSingleton<HumanMemoryService>();
        // 自定义性别库：素材包 JSON 导入 / 设置里手写 / JS·Python·Java 逻辑
        services.AddSingleton<GenderLibrary>();
        // 心理状态：目录 + 每个角色身上那几条（严重度/稳定度）
        services.AddSingleton<MentalHealthService>();
        // 身体感受：冷热饥渴想念，跟真实时间与天气缓慢变化
        services.AddSingleton<PhysiologyService>();
        // 画面生成：装扮 / 背景 / 立绘 / 地图背景
        services.AddSingleton<ImageGenerationService>();
        // 关系网：后台随对话自动长出来
        services.AddSingleton<RelationNetworkService>();
        // 标签层：AI 打标签 → 分类检索 → 标签清单注入
        services.AddSingleton<MemoryTagService>();
        // 独立窗口：关系网 / 搜事件各开一扇，主窗口继续聊天
        services.AddSingleton<PageWindowService>();
        // 记忆分层总览：五层规模与现状汇总
        services.AddSingleton<MemoryOverviewService>();
        // 智能省钱：真实 token 用量 + 单价 → 花费表与总结建议
        services.AddSingleton<CostEstimatorService>();
        // 黑屏：AI 可主动黑屏，但对话不受影响
        services.AddSingleton<ScreenBlackoutService>();
        services.AddSingleton<PetService>();
        // AI 制作桌宠：Q 版化 + 逐状态出帧 + 按步频算走动循环
        services.AddSingleton<PetStudioService>();
        services.AddSingleton<MapService>();
        // 真实城市地图：检索（高德/OSM）→ Overpass 拉道路与建筑 → 拼成游戏里的地图
        services.AddSingleton<MapImportService>();
        // 地图背景：大地图底图 + 缺背景的场景逐张补
        services.AddSingleton<MapBackgroundService>();
        // 强制标定的统一入口：导入路径 await 它、运行路径订阅它的事件
        services.AddSingleton<SceneCalibrationService>();
        services.AddSingleton<LingshuImporter>();

        // 运行时配置与跨模块接线
        services.AddSingleton<RuntimeConfigurator>();
        services.AddSingleton<BrainAnticipationService>();
        services.AddSingleton<NovelLibrary>();
        // 微信好友：谁加过微信、谁是常驻聊天对象
        services.AddSingleton<WeChatService>(sp =>
            new WeChatService(
                sp.GetRequiredService<StorageProvider>(),
                sp.GetRequiredService<CharacterLibrary>()));
        // 多实体地图：用户 + 各 AI 角色各自的位置，以及 AI 拉拽移动的仲裁
        services.AddSingleton<MapActorService>(sp =>
            new MapActorService(sp.GetRequiredService<MapService>()));
        // 表情包库：导入/描述/给 AI 的清单；主对话、假微信、机器人通道共用
        services.AddSingleton<StickerLibrary>(sp =>
            new StickerLibrary(sp.GetRequiredService<StorageProvider>()));
        // 识图服务：独立配置的识图模型
        services.AddSingleton<VisionService>(sp =>
            new VisionService(sp.GetRequiredService<SettingsManager>()));
        // 量化模型（embedding）：把说法与记忆换成数值后按近邻检索
        services.AddSingleton<EmbeddingService>(sp =>
            new EmbeddingService(sp.GetRequiredService<StorageProvider>(),
                sp.GetRequiredService<SettingsManager>(),
                sp.GetRequiredService<HumanMemoryService>()));

        return services;
    }
}
