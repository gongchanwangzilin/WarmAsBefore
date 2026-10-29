using WarmAsBefore.Helpers;
using WarmAsBefore.ViewModels;

namespace WarmAsBefore.Views;

/// <summary>
/// 「AI 制作桌宠」工作室页面：选角色 → 一键生成整套状态动画 → 逐帧真实进度与预览 → 完成后告知素材包已就绪。
///
/// 构造只接 ViewModel（不接别的服务、不碰路由/DI 注册）。可选地支持 <c>?id=角色Id</c> 深链：
/// 带进来就先预选那位角色——只是个可写属性，不影响「构造函数只接 VM」的约定。
/// </summary>
[QueryProperty(nameof(CharacterId), "id")]
public partial class PetStudioPage : ContentPage
{
    private readonly PetStudioViewModel _vm;
    private bool _loaded;

    public PetStudioPage(PetStudioViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = vm;
    }

    /// <summary>路由带入的角色 id（可空）；装载时预选它。</summary>
    public string CharacterId { get; set; } = "";

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (!_loaded)
        {
            _loaded = true;
            _vm.SelectCharacter(Uri.UnescapeDataString(CharacterId));
            _ = _vm.LoadAsync();
        }
        Motion.ArrivePage(this);
    }

    /// <summary>切走/离开时取消在跑的生成，不留后台任务（重进会重新装载，已出帧从素材包读回）。</summary>
    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _vm.Stop();
    }
}
