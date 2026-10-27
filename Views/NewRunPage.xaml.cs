using System.ComponentModel;
using WarmAsBefore.DesignSystem.Theme;
using WarmAsBefore.Helpers;
using WarmAsBefore.ViewModels;

namespace WarmAsBefore.Views;

/// <summary>
/// 新的一局 / 本局设定。进场只演一次；从世界树页返回只刷新列表，不重置已经选好的东西。
/// 初始化遮罩里的分段条按真实进度点亮——它不是循环动画，进度停在哪就是哪。
///
/// 分步时换步要读得出方向：新露出的一块从前进方向（右）或后退方向（左）短促滑入并淡入，
/// 时长压在 200ms——过场是「翻过去一页」，不是「内容在飞」。已填内容全在 VM 上，
/// 隐藏只是 IsVisible，不销毁控件，所以回头一步字还在。
/// </summary>
[QueryProperty(nameof(Character), "character")]
[QueryProperty(nameof(Edit), "edit")]
public partial class NewRunPage : ContentPage
{
    private readonly NewRunViewModel _vm;
    private readonly List<BoxView> _segments = new();
    private bool _loaded;
    private int _lastPosition;

    public NewRunPage(NewRunViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = vm;
        _vm.PropertyChanged += OnVmChanged;
        _lastPosition = vm.Position;
    }

    public string Character { get; set; } = "";
    public string Edit { get; set; } = "";

    protected override void OnAppearing()
    {
        base.OnAppearing();
        BuildSegments();
        if (!_loaded)
        {
            _loaded = true;
            _ = _vm.LoadAsync(Uri.UnescapeDataString(Character ?? ""), Edit == "1");
        }
        else if (!_vm.IsBusy)
        {
            _ = _vm.LoadAsync("", _vm.IsEditMode);
        }
        Motion.ArrivePage(this);
    }

    protected override bool OnBackButtonPressed()
    {
        if (_vm.IsBusy) return true;
        // 安卓返回键：分步里先退回上一步，退到第一步再离开页面
        if (_vm.IsOobe && _vm.Position > 0)
        {
            _vm.PrevStepCommand.Execute(null);
            return true;
        }
        return base.OnBackButtonPressed();
    }

    private void BuildSegments()
    {
        if (_segments.Count > 0) return;
        LoadSegments.ColumnDefinitions.Clear();
        for (var i = 0; i < 28; i++)
        {
            LoadSegments.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            var seg = new BoxView { HeightRequest = 6, VerticalOptions = LayoutOptions.Fill };
            LoadSegments.Add(seg, i, 0);
            _segments.Add(seg);
        }
        PaintSegments(_vm.LoadingProgress);
    }

    private void PaintSegments(double progress)
    {
        var accent = Token("BeigeAccent300", Colors.SteelBlue);
        var rest = Token("WRule", Colors.LightGray);
        var active = (int)Math.Round(Math.Clamp(progress, 0, 1) * _segments.Count);
        for (var i = 0; i < _segments.Count; i++)
            _segments[i].Color = i < active ? accent : rest;
    }

    private static Color Token(string key, Color fallback)
        => Application.Current?.Resources.TryGetValue(key, out var value) == true && value is Color color
            ? color
            : fallback;

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NewRunViewModel.LoadingProgress))
        {
            PaintSegments(_vm.LoadingProgress);
        }
        else if (e.PropertyName == nameof(NewRunViewModel.IsBusy) && _vm.IsBusy)
        {
            _ = Motion.SlideUp(LoadCard);
        }
        else if (e.PropertyName == nameof(NewRunViewModel.Position))
        {
            var forward = _vm.Position > _lastPosition;
            _lastPosition = _vm.Position;
            _ = StepScroll.ScrollToAsync(0, 0, false);
            if (!_vm.IsOobe) return;
            var block = _vm.Position switch
            {
                0 => (VisualElement)StepWorld,
                1 => StepPersona,
                2 => StepRules,
                _ => StepReview
            };
            _ = PlayStepAsync(block, forward);
        }
        else if (e.PropertyName == nameof(NewRunViewModel.IsOobe))
        {
            // 单页 <-> 分步切换时把四块都落回终值，别把上一次被打断的过场留在半透明上
            foreach (var block in new VisualElement[] { StepWorld, StepPersona, StepRules, StepReview })
            {
                block.CancelAnimations();
                block.Opacity = 1;
                block.TranslationX = 0;
            }
        }
    }

    /// <summary>
    /// 换步的过场。只碰这一块自己的 Opacity/TranslationX，并在收尾强制落回终值：
    /// 连点时上一条动画被取消，不会把某一步永久留在半透明或偏移上。
    /// </summary>
    private static async Task PlayStepAsync(VisualElement block, bool forward)
    {
        block.CancelAnimations();
        if (MotionTokens.Disabled)
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
