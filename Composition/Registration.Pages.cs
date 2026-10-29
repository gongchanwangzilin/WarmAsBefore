using Microsoft.Extensions.DependencyInjection;
using WarmAsBefore.Views;

namespace WarmAsBefore.Composition;

/// <summary>页面注册：全部 Transient；需要独立窗口的页面也在这里登记。</summary>
public static class PageRegistration
{
    public static IServiceCollection AddPages(this IServiceCollection services)
    {
        services.AddTransient<TitlePage>();
        services.AddTransient<SetupPage>();
        services.AddTransient<MainGamePage>();
        services.AddTransient<CharacterSelectPage>();
        services.AddTransient<SettingsPage>();
        services.AddTransient<SettingsSectionPage>();
        services.AddTransient<PhonePage>();
        services.AddTransient<WeChatPage>();
        // QQ 壳：复用 WeChatViewModel（构造后 SetShell("qq")）
        services.AddTransient<QqPage>();
        services.AddTransient<QqForumPage>();
        services.AddTransient<MapPage>();
        services.AddTransient<SceneCalibrationPage>();
        services.AddTransient<GalleryPage>();
        services.AddTransient<OutfitPage>();
        services.AddTransient<SavePage>();
        services.AddTransient<CloudSavesPage>();
        services.AddTransient<GamePage>();
        services.AddTransient<PetPage>();
        services.AddTransient<PetSettingsPage>();
        services.AddTransient<PetStudioPage>();
        services.AddTransient<MapCreatePage>();
        services.AddTransient<UsageStatsPage>();
        services.AddTransient<NovelSelectPage>();
        services.AddTransient<NovelWorldPage>();
        services.AddTransient<WorldbookPage>();
        services.AddTransient<NewRunPage>();
        services.AddTransient<CharacterLibraryPage>();
        services.AddTransient<NewCharacterPage>();
        services.AddTransient<AvatarCropPage>();
        services.AddTransient<CharacterStudioPage>();
        services.AddTransient<FigurePackPage>();
        services.AddTransient<ShopPage>();
        services.AddTransient<ShowcaseListPage>();
        services.AddTransient<ShowcaseEditPage>();
        services.AddTransient<ShowcasePlayPage>();
        services.AddTransient<AffectionLevelUpPage>();
        services.AddTransient<CgViewPage>();
        services.AddTransient<BattlePage>();
        services.AddTransient<MaterialsPage>();
        services.AddTransient<PortraitCalibrationPage>();
        services.AddTransient<SubProfilePage>();
        services.AddTransient<MemoryManagePage>();
        services.AddTransient<MemoryGraphPage>();
        services.AddTransient<MemorySearchPage>();
        services.AddTransient<SceneLibraryPage>();
        services.AddTransient<ForceCastPage>();
        services.AddTransient<MentalHealthPage>();
        services.AddTransient<PluginCenterPage>();
        services.AddTransient<PluginSettingsPage>();
        services.AddTransient<PluginPermissionsPage>();
        services.AddTransient<SaveRulesPage>();
        return services;
    }
}
