using Microsoft.Extensions.DependencyInjection;
using WarmAsBefore.Services;

namespace WarmAsBefore.Composition;

/// <summary>角色工作流：角色包、头像裁剪、形象包与装扮素材。</summary>
public static class CharacterRegistration
{
    public static IServiceCollection AddCharacterServices(this IServiceCollection services)
    {
        // 角色包导出：列表每行的「导出」，打包在后台线程上做
        services.AddSingleton<CharacterPackService>();
        services.AddSingleton<Modules.Outfit.FigureComposer>();
        services.AddSingleton<Modules.Outfit.FigurePackService>();
        services.AddSingleton<Modules.Outfit.OutfitAssetPipeline>();
        // 头像裁剪：从相册/拍照进来就地裁成方形，返回裁剪结果路径
        // 单例：调用方在 OpenAsync 上等结果，页面必须拿到同一个实例才能把结果交回去
        services.AddSingleton<AvatarCropService>();
        return services;
    }
}
