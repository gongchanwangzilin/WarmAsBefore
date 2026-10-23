using System.ComponentModel;
using WarmAsBefore.Helpers;
using WarmAsBefore.ViewModels;

namespace WarmAsBefore.Views;

/// <summary>
/// 首次配置页。版式是「地面上浮一张卡」，卡里一次只出现一屏。
///
/// 换步时只让新露出来的那一块重新浮起：整张卡跟着动会让人以为回到了同一个起点，
/// 而这里的起点确实在变（步数在涨），动的是内容，不是容器。
/// </summary>
public partial class SetupPage : ContentPage
{
    private readonly SetupViewModel _vm;

    public SetupPage(SetupViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = vm;
        vm.PropertyChanged += OnVmChanged;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _ = Motion.Enter(SetupCard);
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        VisualElement? step = e.PropertyName switch
        {
            nameof(SetupViewModel.AtLang) => _vm.AtLang ? StepLang : null,
            nameof(SetupViewModel.AtStyle) => _vm.AtStyle ? StepStyle : null,
            nameof(SetupViewModel.AtReside) => _vm.AtReside ? StepReside : null,
            nameof(SetupViewModel.AtAi) => _vm.AtAi ? StepAi : null,
            nameof(SetupViewModel.AtDone) => _vm.AtDone ? StepDone : null,
            _ => null
        };
        if (step is not null) _ = Motion.Enter(step);
    }
}
