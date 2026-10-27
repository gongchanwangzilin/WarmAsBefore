using System.ComponentModel;
using WarmAsBefore.Helpers;
using WarmAsBefore.ViewModels;

namespace WarmAsBefore.Views;

/// <summary>
/// 新建角色向导。宽屏左预览右表单，窗口窄于 860 时上下叠成一列；
/// 换步只把当前那一块从前进/后退方向短促滑入，已填内容全在 VM 上，回头一步字还在。
/// 返回键先走 <see cref="NewCharacterViewModel.LeaveAsync"/>：没创建就把草稿目录收掉。
/// </summary>
public partial class NewCharacterPage : ContentPage
{
    private readonly NewCharacterViewModel _vm;
    private bool _loaded;
    private int _layoutColumns = -1;
    private int _lastStep;

    public NewCharacterPage(NewCharacterViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        _lastStep = vm.Step;
        BindingContext = vm;
        _vm.PropertyChanged += OnVmChanged;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        Motion.ArrivePage(this);
        if (_loaded) return;
        _loaded = true;
        _ = _vm.LoadAsync();
    }

    protected override bool OnBackButtonPressed()
    {
        if (_vm.IsBusy) return true;
        _ = _vm.LeaveAsync();
        return true;
    }

    /// <summary>两栏/单栏只切换一次几何：每帧重建列定义会让正在编辑的输入框丢焦点。</summary>
    private void OnBodyResized(object? sender, EventArgs e)
    {
        if (BodyGrid.Width <= 0) return;
        var columns = BodyGrid.Width >= 860 ? 2 : 1;
        if (columns == _layoutColumns) return;
        _layoutColumns = columns;

        BodyGrid.ColumnDefinitions.Clear();
        BodyGrid.RowDefinitions.Clear();

        if (columns == 2)
        {
            BodyGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(340)));
            BodyGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            BodyGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            Grid.SetColumn(PreviewCard, 0);
            Grid.SetRow(PreviewCard, 0);
            Grid.SetColumn(FormCard, 1);
            Grid.SetRow(FormCard, 0);
            PreviewStage.HeightRequest = 360;
        }
        else
        {
            BodyGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            BodyGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            BodyGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            Grid.SetColumn(PreviewCard, 0);
            Grid.SetRow(PreviewCard, 0);
            Grid.SetColumn(FormCard, 0);
            Grid.SetRow(FormCard, 1);
            PreviewStage.HeightRequest = 260;
        }
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(NewCharacterViewModel.Step)) return;

        var forward = _vm.Step > _lastStep;
        _lastStep = _vm.Step;
        _ = StepScroll.ScrollToAsync(0, 0, false);

        var block = _vm.Step switch
        {
            0 => StepBasics,
            1 => StepPersona,
            2 => StepLooks,
            _ => StepWorld
        };
        // 绑定把 IsVisible 翻过来之后才看得见这一块；这一帧之后再演，避免对着隐形元素做动画。
        Dispatcher.Dispatch(() => _ = PlayStepAsync(block, forward));
    }

    /// <summary>只碰这一块自己的 Opacity/TranslationX，收尾强制落回终值，连点不会把某一步留在半透明上。</summary>
    private static async Task PlayStepAsync(VisualElement block, bool forward)
    {
        block.CancelAnimations();
        if (DesignSystem.Theme.MotionTokens.Disabled)
        {
            block.Opacity = 1;
            block.TranslationX = 0;
            return;
        }
        block.Opacity = 0;
        block.TranslationX = forward ? 26 : -26;
        try
        {
            await Task.WhenAll(
                block.FadeTo(1, 190, Easing.CubicOut),
                block.TranslateTo(0, 0, 190, Easing.CubicOut));
        }
        finally
        {
            block.Opacity = 1;
            block.TranslationX = 0;
        }
    }
}
