#if ANDROID || WINDOWS
using System;

namespace WarmAsBefore.Weather3D;

/// <summary>天气种类。</summary>
public enum WeatherKind
{
    Clear,
    Rain,
    Snow
}

/// <summary>
/// 天气状态单例。Set(kind) 启动 1.5 秒的平滑过渡，逐帧插值由场景里的
/// WeatherUniformUpdater（Behavior）调用 <see cref="Tick"/> 推进；插值的中间值
/// （雨/雪发射强度、雾密度）由该 Behavior 读取并写入实际发射器与材质 uniform。
/// 中间值用属性而非委托注入，避免场景重建后持有失效的对象引用。
/// </summary>
public sealed class WeatherController
{
    private const float TransitionSeconds = 1.5f;
    private const float ClearCloud = 0.15f;
    private const float RainCloud = 0.85f;
    private const float SnowCloud = 0.70f;

    private static readonly WeatherController _instance = new();

    private WeatherKind _current = WeatherKind.Clear;

    // 插值起点/终点/当前值
    private float _fromCloud = ClearCloud;
    private float _toCloud = ClearCloud;
    private float _fromRain;
    private float _toRain;
    private float _fromSnow;
    private float _toSnow;
    private float _elapsed;
    private bool _transitioning;

    private WeatherController()
    {
    }

    public static WeatherController Instance => _instance;

    public WeatherKind Current => _current;

    /// <summary>天气切换完成时触发（在 Set 调用时立即触发）。</summary>
    public event Action<WeatherKind>? Changed;

    /// <summary>当前插值出的雾密度（0..1）。由场景 Behavior 读取。</summary>
    internal float CloudDensity { get; private set; } = ClearCloud;

    /// <summary>当前插值出的雨发射强度（0..1）。由场景 Behavior 读取。</summary>
    internal float RainRate { get; private set; }

    /// <summary>当前插值出的雪发射强度（0..1）。由场景 Behavior 读取。</summary>
    internal float SnowRate { get; private set; }

    /// <summary>设置目标天气，触发 1.5 秒过渡。</summary>
    public void Set(WeatherKind kind)
    {
        _fromCloud = CloudDensity;
        _fromRain = RainRate;
        _fromSnow = SnowRate;

        switch (kind)
        {
            case WeatherKind.Rain:
                _toCloud = RainCloud;
                _toRain = 1f;
                _toSnow = 0f;
                break;
            case WeatherKind.Snow:
                _toCloud = SnowCloud;
                _toRain = 0f;
                _toSnow = 1f;
                break;
            default:
                _toCloud = ClearCloud;
                _toRain = 0f;
                _toSnow = 0f;
                break;
        }

        _current = kind;
        _elapsed = 0f;
        _transitioning = true;

        Changed?.Invoke(kind);
    }

    /// <summary>由场景 Behavior 每帧调用，用 TimeSpan 累加推进插值。</summary>
    internal void Tick(TimeSpan gameTime)
    {
        if (_transitioning)
        {
            _elapsed += (float)gameTime.TotalSeconds;
            float t = _elapsed / TransitionSeconds;
            if (t >= 1f)
            {
                t = 1f;
                _transitioning = false;
            }

            // smoothstep，避免起止点的突变感
            float s = t * t * (3f - 2f * t);
            CloudDensity = Lerp(_fromCloud, _toCloud, s);
            RainRate = Lerp(_fromRain, _toRain, s);
            SnowRate = Lerp(_fromSnow, _toSnow, s);
        }
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;
}
#endif
