using Microsoft.Extensions.DependencyInjection;
using WarmAsBefore.ViewModels;

namespace WarmAsBefore.Composition;

/// <summary>页面模型注册：全部 Transient，页面关闭即释放。</summary>
public static class ViewModelRegistration
{
    public static IServiceCollection AddViewModels(this IServiceCollection services)
    {
        services.AddTransient<TitleViewModel>();
        services.AddTransient<SetupViewModel>();
        services.AddTransient<MainGameViewModel>();
        services.AddTransient<CharacterSelectViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<PhoneViewModel>();
        services.AddTransient<WeChatViewModel>();
        services.AddTransient<QqForumViewModel>();
        services.AddTransient<MapViewModel>();
        services.AddTransient<GalleryViewModel>();
        services.AddTransient<OutfitViewModel>();
        services.AddTransient<SaveViewModel>();
        services.AddTransient<CloudSavesViewModel>();
        services.AddTransient<GameViewModel>();
        services.AddTransient<PetSettingsViewModel>();
        services.AddTransient<PetStudioViewModel>();
        services.AddTransient<MapCreateViewModel>();
        services.AddTransient<MapCaptureViewModel>();
        services.AddTransient<UsageStatsViewModel>();
        services.AddTransient<WorldbookViewModel>();
        services.AddTransient<NewRunViewModel>();
        services.AddTransient<ForceCastViewModel>();
        services.AddTransient<SceneCalibrationViewModel>();
        services.AddTransient<NewCharacterViewModel>();
        services.AddTransient<AvatarCropViewModel>();
        services.AddTransient<CharacterStudioViewModel>();
        services.AddTransient<FigurePackViewModel>();
        services.AddTransient<MemoryGraphViewModel>();
        services.AddTransient<MemorySearchViewModel>();
        services.AddTransient<MemoryManageViewModel>();
        services.AddTransient<SceneLibraryViewModel>();
        services.AddTransient<NovelSelectViewModel>();
        services.AddTransient<NovelWorldViewModel>();
        services.AddTransient<CharacterLibraryViewModel>();
        services.AddTransient<ShopViewModel>();
        services.AddTransient<ShowcaseListViewModel>();
        services.AddTransient<ShowcaseEditViewModel>();
        services.AddTransient<ShowcasePlayViewModel>();
        services.AddTransient<AffectionLevelUpViewModel>();
        services.AddTransient<CgViewViewModel>();
        services.AddTransient<MaterialsViewModel>();
        services.AddTransient<PortraitCalibrationViewModel>();
        services.AddTransient<SubProfileViewModel>();
        services.AddTransient<MentalHealthViewModel>();
        services.AddTransient<PluginCenterViewModel>();
        services.AddTransient<PluginPermissionsViewModel>();
        services.AddTransient<SaveRulesViewModel>();
        return services;
    }
}
