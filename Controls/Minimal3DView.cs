using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using WarmAsBefore.Drawables;

namespace WarmAsBefore.Controls;

/// <summary>
/// 灰度 3D 视图容器：GraphicsView + 单指拖拽环视（水平 = 偏航，竖直 = 俯仰）。
/// 这里只管「看」——前进、街面进入、全景旋钮这些动作由外面的页面调相机 API 组合。
/// </summary>
public sealed class Minimal3DView : ContentView
{
    private readonly GraphicsView _surface;
    private float _panX, _panY;

    public Minimal3DDrawable Drawable { get; } = new();
    public Minimal3DCamera Camera => Drawable.Camera;

    public Minimal3DView()
    {
        _surface = new GraphicsView { Drawable = Drawable };
        GestureRecognizers.Add(new PanGestureRecognizer());
        var gesture = (PanGestureRecognizer)GestureRecognizers[0];
        gesture.PanUpdated += OnPan;
        Content = _surface;
    }

    /// <summary>换一张场景快照并切到鸟瞰机位（空图也照设——画面交给页面提示）。</summary>
    public void SetScene(Minimal3DScene scene)
    {
        Drawable.Scene = scene;
        Camera.OverviewFrom(scene);
        Refresh();
    }

    public void Refresh() => _surface.Invalidate();

    /// <summary>转向：deltaRad 为正 = 向右看。</summary>
    public void YawBy(double deltaRad)
    {
        Camera.YawRad += deltaRad;
        Refresh();
    }

    /// <summary>俯仰：deltaRad 为正 = 更低头。范围见 Minimal3DCamera.</summary>
    public void PitchBy(double deltaRad)
    {
        Camera.PitchRad = Math.Clamp(Camera.PitchRad + deltaRad, Minimal3DCamera.PitchMin, Minimal3DCamera.PitchMax);
        Refresh();
    }

    /// <summary>沿当前朝向的地面前进（负值 = 退）。世界 y 轴向下，朝北走 y 减。</summary>
    public void WalkForward(double meters)
    {
        Camera.X += Math.Sin(Camera.YawRad) * meters;
        Camera.Y -= Math.Cos(Camera.YawRad) * meters;
        Refresh();
    }

    /// <summary>站到街面的一个点上（进入视角用；往哪个方向看由调用方给）。</summary>
    public void PoseStreet(Point pos, double yawRad)
    {
        Camera.StreetPose(pos, yawRad);
        Refresh();
    }

    private void OnPan(object? sender, PanUpdatedEventArgs e)
    {
        switch (e.StatusType)
        {
            case GestureStatus.Started:
                _panX = 0;
                _panY = 0;
                break;
            case GestureStatus.Running:
            {
                // 增量差分：TotalX 是自按下以来的累计值
                var dx = (float)e.TotalX - _panX;
                var dy = (float)e.TotalY - _panY;
                _panX = (float)e.TotalX;
                _panY = (float)e.TotalY;
                // 「抓着世界」的手感：手指右拉 = 世界右移 = 视线向左；下拉 = 地平线上抬 = 视线抬起来
                Camera.YawRad -= dx * 0.004;
                Camera.PitchRad = Math.Clamp(Camera.PitchRad - dy * 0.003,
                    Minimal3DCamera.PitchMin, Minimal3DCamera.PitchMax);
                _surface.Invalidate();
                break;
            }
            case GestureStatus.Completed:
            case GestureStatus.Canceled:
                _panX = 0;
                _panY = 0;
                break;
        }
    }
}
