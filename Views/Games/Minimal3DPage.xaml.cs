using WarmAsBefore.Drawables;
using WarmAsBefore.Services;

namespace WarmAsBefore.Views;

/// <summary>
/// 灰度 3D 视图页：附近参数（x/y/yaw——地图页点街进来）= 街面机位；不带参数 = 鸟瞰。
/// 视角交互：拖拽环视（View 自己带）+ 全景旋钮（360° 一根拉杆）+ 电脑方向键 + 可选陀螺仪。
/// </summary>
[QueryProperty(nameof(EntryX), "x")]
[QueryProperty(nameof(EntryY), "y")]
[QueryProperty(nameof(EntryYaw), "yaw")]
public partial class Minimal3DPage : ContentPage
{
    private readonly MapService _maps;
    private Minimal3DScene _scene = new();

    public string SceneTitle { get; private set; } = "";
    public string Heading { get; private set; } = "北";

    /// <summary>街面进入参数：x/y 是世界米，yaw 是度（偏东为正、北 = 0/360）。NaN = 没带。</summary>
    public double EntryX { get; set; } = double.NaN;
    public double EntryY { get; set; } = double.NaN;
    public double EntryYaw { get; set; }

    /// <summary>陀螺仪是否可用（支持姿态传感器的设备；手机为主）。</summary>
    public bool GyroAvailable { get; } =
        Microsoft.Maui.Devices.Sensors.OrientationSensor.Default.IsSupported;

#if WINDOWS
    private Microsoft.UI.Xaml.Input.KeyEventHandler? _keyHandler;
#endif

    public Minimal3DPage(MapService maps)
    {
        InitializeComponent();
        _maps = maps;
        BindingContext = this;
        GyroButton.IsVisible = GyroAvailable;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        BuildScene();
#if WINDOWS
        HookDesktopArrowKeys(true);
#endif
    }

    protected override void OnDisappearing()
    {
#if WINDOWS
        HookDesktopArrowKeys(false);
#endif
        StopGyro();
        base.OnDisappearing();
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

        if (!_scene.Empty)
        {
            // 带了街面参数 = 从地图页的街道点跳进来的：站到那个点上（先后于 SetScene 的鸟瞰复位）
            if (!double.IsNaN(EntryX) && !double.IsNaN(EntryY))
                View3D.PoseStreet(new Microsoft.Maui.Graphics.Point(EntryX, EntryY), EntryYaw * Math.PI / 180);
            SyncKnob();
        }
    }

    // ============ 全景旋钮（360° 拉杆 + 罗盘字） ============

    private bool _knobSync;

    private void OnPanKnobChanged(object? sender, ValueChangedEventArgs e)
    {
        if (_knobSync) return;
        View3D.YawTo(e.NewValue * Math.PI / 180);
        SyncHeading();
    }

    /// <summary>旋钮值追相机：YawBy/PoseStreet 之后调一次，别让两处说法打架。</summary>
    private void SyncKnob()
    {
        _knobSync = true;
        PanKnob.Value = Math.Clamp(HeadingDeg(), 0, 360);
        _knobSync = false;
        SyncHeading();
    }

    private double HeadingDeg()
    {
        var deg = View3D.Camera.YawRad * 180 / Math.PI % 360;
        if (deg < 0) deg += 360;
        return deg;
    }

    private void SyncHeading()
    {
        var deg = HeadingDeg();
        Heading = deg switch
        {
            < 22.5 or >= 337.5 => "北",
            < 67.5 => "东北",
            < 112.5 => "东",
            < 157.5 => "东南",
            < 202.5 => "南",
            < 247.5 => "西南",
            < 292.5 => "西",
            _ => "西北",
        };
        OnPropertyChanged(nameof(Heading));
    }

    // ============ 按钮 ============

    private void OnBackNav(object? sender, EventArgs e) => Shell.Current.GoToAsync("..");

    private void OnOverview(object? sender, EventArgs e)
    {
        View3D.SetScene(_scene);
        SyncKnob();
    }

    private void OnTurnLeft(object? sender, EventArgs e)
    {
        View3D.YawBy(-Math.PI / 14);
        SyncKnob();
    }

    private void OnTurnRight(object? sender, EventArgs e)
    {
        View3D.YawBy(Math.PI / 14);
        SyncKnob();
    }

    private void OnForward(object? sender, EventArgs e) => View3D.WalkForward(8);
    private void OnBackward(object? sender, EventArgs e) => View3D.WalkForward(-5);

    // ============ 电脑方向键（沿用 MainGamePage 的窗口级挂法） ============

#if WINDOWS
    private void HookDesktopArrowKeys(bool attach)
    {
        try
        {
            if (Application.Current?.Windows.Count > 0
                && Application.Current.Windows[0].Handler?.PlatformView is Microsoft.UI.Xaml.Window w
                && w.Content is Microsoft.UI.Xaml.UIElement root)
            {
                if (attach)
                {
                    _keyHandler ??= (_, e) =>
                    {
                        switch (e.Key)
                        {
                            case Windows.System.VirtualKey.Left:
                                e.Handled = true;
                                View3D.YawBy(-Math.PI / 18);
                                SyncKnob();
                                break;
                            case Windows.System.VirtualKey.Right:
                                e.Handled = true;
                                View3D.YawBy(Math.PI / 18);
                                SyncKnob();
                                break;
                            case Windows.System.VirtualKey.Up:
                                e.Handled = true;
                                View3D.WalkForward(6);
                                break;
                            case Windows.System.VirtualKey.Down:
                                e.Handled = true;
                                View3D.WalkForward(-4);
                                break;
                            case Windows.System.VirtualKey.PageUp:
                                e.Handled = true;
                                View3D.PitchBy(-8 * Math.PI / 180);
                                break;
                            case Windows.System.VirtualKey.PageDown:
                                e.Handled = true;
                                View3D.PitchBy(8 * Math.PI / 180);
                                break;
                        }
                    };
                    root.KeyDown -= _keyHandler;
                    root.KeyDown += _keyHandler;
                }
                else if (_keyHandler is not null)
                {
                    root.KeyDown -= _keyHandler;
                }
            }
        }
        catch (Exception ex) { App.WriteLog("Min3D.HookDesktopArrowKeys -> " + ex); }
    }
#endif

    // ============ 陀螺仪（可选）：相对开启时的姿态变化 → 相机转向/俯仰 ============

    private bool _gyroOn;
    private System.Numerics.Quaternion? _gyroRef;
    private double _gyroBaseYaw, _gyroBasePitch;

    private void OnGyro(object? sender, EventArgs e)
    {
        if (_gyroOn) { StopGyro(); return; }
        if (!GyroAvailable) return;
        try
        {
            _gyroRef = null;
            _gyroBaseYaw = View3D.Camera.YawRad;
            _gyroBasePitch = View3D.Camera.PitchRad;
            Microsoft.Maui.Devices.Sensors.OrientationSensor.Default.ReadingChanged += OnGyroRead;
            Microsoft.Maui.Devices.Sensors.OrientationSensor.Default.Start(
                Microsoft.Maui.Devices.Sensors.SensorSpeed.UI);
            _gyroOn = true;
            GyroButton.Text = "📱 陀螺仪 ●";
        }
        catch (Exception ex)
        {
            App.WriteLog("Min3D.Gyro.On -> " + ex);
            StopGyro();
        }
    }

    private void StopGyro()
    {
        if (!_gyroOn) return;
        try
        {
            Microsoft.Maui.Devices.Sensors.OrientationSensor.Default.ReadingChanged -= OnGyroRead;
            Microsoft.Maui.Devices.Sensors.OrientationSensor.Default.Stop();
        }
        catch { /* 关不掉就让它自己睡 */ }
        _gyroOn = false;
        GyroButton.Text = "📱 陀螺仪";
    }

    private void OnGyroRead(object? sender, Microsoft.Maui.Devices.Sensors.OrientationSensorChangedEventArgs e)
    {
        if (!_gyroOn) return;
        var q = e.Reading.Orientation;
        if (_gyroRef is null) { _gyroRef = q; return; }

        // delta = 当前 * 参考共轭：转手机多少、视角跟着转多少（够玩的近似，不做 IMU 级校准）
        var c = _gyroRef.Value;
        var dw = q.W * c.W + q.X * c.X + q.Y * c.Y + q.Z * c.Z;
        var dx = -q.W * c.X + q.X * c.W - q.Y * c.Z + q.Z * c.Y;
        var dy = -q.W * c.Y + q.Y * c.W - q.Z * c.X + q.X * c.Z;
        var dz = -q.W * c.Z + q.Z * c.W - q.X * c.Y + q.Y * c.X;

        var head = Math.Atan2(2 * (dx * dy + dw * dz), 1 - 2 * (dy * dy + dz * dz));
        var pitch = Math.Clamp(2 * (dw * dy - dz * dx), -1, 1);
        View3D.Camera.YawRad = _gyroBaseYaw - head;
        View3D.Camera.PitchRad = Math.Clamp(_gyroBasePitch - Math.Asin(pitch),
            Minimal3DCamera.PitchMin, Minimal3DCamera.PitchMax);
        SyncKnob();
        View3D.Refresh();
    }
}
