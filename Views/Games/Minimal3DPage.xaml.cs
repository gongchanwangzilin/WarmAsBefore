using WarmAsBefore.Drawables;
using WarmAsBefore.Services;

namespace WarmAsBefore.Views;

/// <summary>
/// 灰度 3D 视图页：进页按当前地图重建快照（编辑过再看也新鲜），空图给提示。
/// 相机初始 = 鸟瞰；「回鸟瞰」一键回到全图机位——后面批次再接街面进入与全景旋钮。
/// </summary>
public partial class Minimal3DPage : ContentPage
{
    private readonly MapService _maps;
    private Minimal3DScene _scene = new();

    public string SceneTitle { get; private set; } = "";

    public Minimal3DPage(MapService maps)
    {
        InitializeComponent();
        _maps = maps;
        BindingContext = this;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        BuildScene();
    }

    private void BuildScene()
    {
        var map = _maps.Map;
        _scene = Minimal3DBuilder.Build(map);
        SceneTitle = string.IsNullOrWhiteSpace(map.Name) ? "3D 灰度视图" : $"3D 灰度 · {map.Name}";
        OnPropertyChanged(nameof(SceneTitle));

        View3D.SetScene(_scene);
        EmptyHint.IsVisible = _scene.Empty;
        Toolbar.IsVisible = !_scene.Empty;
    }

    private void OnBackNav(object? sender, EventArgs e) => Shell.Current.GoToAsync("..");

    private void OnOverview(object? sender, EventArgs e)
    {
        View3D.SetScene(_scene);
    }

    private void OnTurnLeft(object? sender, EventArgs e) => View3D.YawBy(-Math.PI / 14);
    private void OnTurnRight(object? sender, EventArgs e) => View3D.YawBy(Math.PI / 14);
    private void OnForward(object? sender, EventArgs e) => View3D.WalkForward(8);
    private void OnBackward(object? sender, EventArgs e) => View3D.WalkForward(-5);
}
