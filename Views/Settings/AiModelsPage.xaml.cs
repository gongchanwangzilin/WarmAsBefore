using WarmAsBefore.Services;
using WarmAsBefore.ViewModels;

namespace WarmAsBefore.Views;

/// <summary>
/// AI 模型管理页：全部编辑逻辑在 <see cref="AiModelsViewModel"/> 中。
/// 这里只负责注入 SettingsManager 并挂上 BindingContext，保留原构造签名供 DI/路由解析。
/// </summary>
public partial class AiModelsPage : ContentPage
{
    public AiModelsPage(SettingsManager settings)
    {
        InitializeComponent();
        BindingContext = new AiModelsViewModel(settings);
    }
}
