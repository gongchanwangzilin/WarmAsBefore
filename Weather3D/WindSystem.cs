#if ANDROID || WINDOWS
using Evergine.Mathematics;

namespace WarmAsBefore.Weather3D;

/// <summary>
/// 全局风场单例。WeatherScene 的雾 uniform 每帧从这里读取方向与强度。
/// </summary>
public sealed class WindSystem
{
    private static readonly WindSystem _instance = new();

    private Vector3 _direction = new Vector3(1f, 0f, 0f);
    private float _strength = 0.5f;

    private WindSystem()
    {
    }

    public static WindSystem Instance => _instance;

    /// <summary>风向（世界空间方向向量）。</summary>
    public Vector3 Direction
    {
        get => _direction;
        set => _direction = value;
    }

    /// <summary>风力，set 时钳制到 [0,1]。</summary>
    public float Strength
    {
        get => _strength;
        set => _strength = value < 0f ? 0f : (value > 1f ? 1f : value);
    }
}
#endif
