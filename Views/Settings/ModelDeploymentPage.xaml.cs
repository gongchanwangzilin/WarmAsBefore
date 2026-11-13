using WarmAsBefore.Services;
using WarmAsBefore.Services.Model;
using WarmAsBefore.ViewModels.Settings;

namespace WarmAsBefore.Views;

/// <summary>
/// 模型部署向导页：表单收集用户决定的模型名 / 来源 / 安装位置，交给 ModelDeploymentService 全自动下载安裝。
/// </summary>
public partial class ModelDeploymentPage : ContentPage
{
    private readonly ModelDeploymentViewModel _vm;

    public ModelDeploymentPage(SettingsManager settings, ModelDeploymentService deploy)
    {
        InitializeComponent();
        _vm = new ModelDeploymentViewModel(deploy, settings);
        BindingContext = _vm;
    }

    private async void OnBack(object? sender, EventArgs e) => await Shell.Current.GoToAsync("..");
}
