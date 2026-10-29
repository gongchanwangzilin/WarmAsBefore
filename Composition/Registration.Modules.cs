using Microsoft.Extensions.DependencyInjection;
using WarmAsBefore.Services;

namespace WarmAsBefore.Composition;

/// <summary>业务模块注册：对话、记忆、商店、存档、场景、MCP、小游戏等。</summary>
public static class ModuleRegistration
{
    public static IServiceCollection AddModuleServices(this IServiceCollection services)
    {
        // 真实世界感知
        services.AddSingleton<Modules.RealWorld.WeatherProvider>();
        services.AddSingleton<Modules.RealWorld.TimeProvider>();
        services.AddSingleton<Modules.RealWorld.PermissionBroker>();
        // 城市定位（GPS → IP 降级）与搜索联想
        services.AddSingleton<Modules.RealWorld.LocationService>();
        services.AddSingleton<Modules.RealWorld.PhysiologicalTracker>();

        // 外部 API 与云存档
        services.AddSingleton<Modules.ApiManager.ApiGateway>();
        services.AddSingleton<Services.CloudSync.MemoryReconcileService>();
        services.AddSingleton<Services.CloudSync.CloudSyncService>();

        // 对话与记忆
        services.AddSingleton<Modules.AiChat.ChatEngine>();
        services.AddSingleton<Modules.AiChat.MemoryVault>();
        services.AddSingleton<Modules.Automation.TaskOrchestrator>();
        services.AddSingleton<Modules.Automation.DailyDiaryWriter>();
        services.AddSingleton<Modules.Mcp.McpOrchestrator>();
        services.AddSingleton<Modules.Tools.RuntimeManager>();
        services.AddSingleton<Modules.Affection.AffectionLevelUpService>();
        services.AddSingleton<Modules.Battle.BattleDriverManager>();
        services.AddSingleton<Modules.Cg.CgStore>();
        services.AddSingleton<Modules.Cg.CgViewPayload>();
        services.AddSingleton<Modules.Showcase.ShowcaseStore>();
        services.AddSingleton<Modules.Worldbook.WorldbookGenerator>();
        // 世界树：存档绑定的那一份设定；旧世界书在首次读取时并进来
        services.AddSingleton<WorldTreeService>();
        // 人格记忆初始化：人设与世界树先整理成记忆
        services.AddSingleton<PersonaMemoryService>();

        // 存档、数据包与外部桥接
        services.AddSingleton<Modules.DataPack.PackImporter>();
        services.AddSingleton<Modules.SaveSystem.SaveManager>();
        services.AddSingleton<Modules.MinecraftBridge.MaidBridgeService>();
        services.AddSingleton<Modules.NovelImport.NovelAnalyzer>();
        services.AddSingleton<Modules.GameModule.MiniGameEngine>();
        services.AddSingleton<Modules.GameModule.ChessBrainService>();
        services.AddSingleton<Modules.GameModule.GameSkillTracker>();
        services.AddSingleton<Modules.Market.ShopService>();
        services.AddSingleton<Modules.Market.GiftPanelService>();
        services.AddSingleton<Modules.Scene.SceneDirector>();
        // 更新器要知道当前安装版本；版本号来自 csproj 的 ApplicationDisplayVersion
        services.AddSingleton(_ => new Modules.Update.UpdateService
        {
            CurrentVersion = Microsoft.Maui.ApplicationModel.AppInfo.Current.VersionString
        });

        // 官方接入（真微信 / QQ）
        services.AddSingleton<Modules.RealChat.OfficialChatBridge>();
        return services;
    }
}
