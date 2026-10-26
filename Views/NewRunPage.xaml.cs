using System.ComponentModel;
using WarmAsBefore.Helpers;
using WarmAsBefore.ViewModels;

namespace WarmAsBefore.Views;

/// <summary>
/// 新的一局 / 本局设定。进场只演一次；从世界树页返回只刷新列表，不重置已经选好的东西。
/// 初始化遮罩里的分段条按真实进度点亮——它不是循环动画，进度停在哪就是哪。
/// </summary>
[QueryProperty(nameof(Character), "character")]
[QueryProperty(nameof(Edit), "edit")]
public partial class NewRunPage : ContentPage
{
    private readonly NewRunViewModel _vm;
    private readonly List<BoxView> _segments = new();
    private bool _loaded;

    public NewRunPage(NewRunViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = vm;
        _vm.PropertyChanged += OnVmChanged;
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
    }
}
