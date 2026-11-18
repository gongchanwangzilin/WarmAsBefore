#if ANDROID || WINDOWS
using System;
using System.Diagnostics;
using Evergine.Common.Graphics;
using Evergine.Components.Graphics3D;
using Evergine.Framework;
// MAUI / System 与 Evergine 有同名类型，显式别名消歧
using Behavior = Evergine.Framework.Behavior;
using Application = Evergine.Framework.Application;
using Color = Evergine.Common.Graphics.Color;
using Evergine.Framework.Graphics;
using Evergine.Framework.Graphics.Effects;
using Evergine.Framework.Particles;
using Evergine.Framework.Particles.Asset;
using Evergine.Framework.Particles.Components;
using Evergine.Framework.Particles.Enums;
using Evergine.Framework.Particles.Shape;
using Evergine.Framework.Particles.Spawn;
using Evergine.Framework.Services;
using Evergine.Mathematics;

namespace WarmAsBefore.Weather3D;

/// <summary>
/// 3D 天气场景（v1.63 重写）：真正的三维空间大气场。
/// - 透视相机（Camera3D + FieldOfView）+ 随风力极轻微视差，雨雪因此有近大远小的纵深；
/// - 两台 GPU 粒子发射器分布在不同 Z 深度，落在视锥内；
/// - 体积雾由多层带 fbm 噪声的半透明雾片构成（不同深度、法线朝向相机、随时间漂移、
///   底部浓顶部淡），产生"雾团/模糊云雾"的体积观感，取代旧的 2D 屏幕渐变。
/// 每个子系统独立 try/catch，任何失败只跳过该子系统，绝不让场景创建抛异常崩主界面。
/// </summary>
public sealed class WeatherScene : Scene
{
    private const string LogTag = "[WeatherScene]";

    private const float MaxRainRate = 220f;
    private const float MaxSnowRate = 70f;

    // 雾片数量与分层范围（Z 越大越靠近相机）
    private const int FogLayerCount = 7;
    private const float FogZNear = -2f;
    private const float FogZFar = -26f;

    private ParticlesComponent? _rainParticles;
    private ParticlesComponent? _snowParticles;
    private RateSpawn? _rainSpawn;
    private RateSpawn? _snowSpawn;
    private Material? _fogMaterial;
    private Transform3D? _cameraTransform;
    private readonly System.Collections.Generic.List<Transform3D> _fogLayers = new();
    private Vector3 _cameraBasePosition = new(0f, 1.2f, 9f);

    protected override void CreateScene()
    {
        var graphics = TryResolve<GraphicsContext>();
        var assets = TryResolve<AssetsService>();

        if (graphics == null || assets == null)
        {
            Debug.WriteLine(LogTag + " 缺少 GraphicsContext/AssetsService，3D 天气场跳过（降级 2D）。");
            return;
        }

        // 相机必须先建：没有相机就没有透视，粒子/雾都退化成屏幕空间的图形。
        try { CreateCamera(); }
        catch (Exception ex) { Debug.WriteLine(LogTag + " 透视相机创建失败: " + ex); }

        try { CreateRain(graphics, assets); }
        catch (Exception ex) { Debug.WriteLine(LogTag + " 雨发射器创建失败: " + ex); }

        try { CreateSnow(graphics, assets); }
        catch (Exception ex) { Debug.WriteLine(LogTag + " 雪发射器创建失败: " + ex); }

        try { CreateVolumetricFog(graphics, assets); }
        catch (Exception ex) { Debug.WriteLine(LogTag + " 体积雾创建失败: " + ex); }

        try { CreateUpdater(); }
        catch (Exception ex) { Debug.WriteLine(LogTag + " Uniform Updater 创建失败: " + ex); }
    }

    // ---------------------------------------------------------------- 透视相机

    private void CreateCamera()
    {
        _cameraTransform = new Transform3D { Position = _cameraBasePosition };
        var camera = new Camera3D
        {
            FieldOfView = 55f * (float)Math.PI / 180f, // Evergine 用弧度
            NearPlane = 0.1f,
            FarPlane = 100f,
        };
        var entity = new Entity("WeatherCamera")
            .AddComponent(_cameraTransform)
            .AddComponent(camera);
        this.Managers.EntityManager.Add(entity);

        try { _cameraTransform.LookAt(new Vector3(0f, 2.4f, -8f)); }
        catch (Exception ex) { Debug.WriteLine(LogTag + " 相机朝向失败（保留默认朝向）: " + ex); }
    }

    // ---------------------------------------------------------------- 粒子

    private void CreateRain(GraphicsContext graphics, AssetsService assets)
    {
        var desc = new ParticleEmitterDescription
        {
            ParticleTexture = DefaultResourcesIDs.ParticleTextureID,
            ParticleSampler = DefaultResourcesIDs.LinearClampSamplerID,
            RenderLayer = DefaultResourcesIDs.AlphaRenderLayerID,
            MaxParticles = 2200,
            InitLife = 2.4f,
            InitSpeed = 16f,
            InitSize = 0.05f,
            InitColor = new Color(0.78f, 0.86f, 0.98f, 0.85f),
            Gravity = -22f,
            SimulationSpace = SimulationSpaceMode.World
        };

        var emitter = new ParticlesEmitter(desc, graphics, assets);

        // BoxShapeEmitter / RateSpawn 的细配置按已核实签名构造；运行时若有出入就退回保守 desc。
        try
        {
            var box = new ShapeBoxDescription
            {
                Size = new Vector3(30f, 0.2f, 34f), // 在 X/Z 大范围铺开，落入相机视锥形成纵深
                FromCenter = true
            };
            emitter.ShapeEmitter = new BoxShapeEmitter(box);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(LogTag + " 雨形状细配置失败，退回保守发射器: " + ex);
        }

        try
        {
            var rate = new RateSpawn(new SpawnRateDescription()) { Rate = MaxRainRate };
            emitter.Spawn = rate;
            _rainSpawn = rate;
        }
        catch (Exception ex)
        {
            Debug.WriteLine(LogTag + " 雨发射率细配置失败，退回保守发射器: " + ex);
        }

        var system = new ParticleSystem();
        system.AddEmitter(emitter);

        _rainParticles = new ParticlesComponent { ParticleSystem = system };
        var entity = new Entity("WeatherRain")
            // 发射器抬高到相机上方，雨从视野上方落下
            .AddComponent(new Transform3D { Position = new Vector3(0f, 11f, -8f) })
            .AddComponent(_rainParticles)
            .AddComponent(new ParticlesRenderer());
        this.Managers.EntityManager.Add(entity);
    }

    private void CreateSnow(GraphicsContext graphics, AssetsService assets)
    {
        var desc = new ParticleEmitterDescription
        {
            ParticleTexture = DefaultResourcesIDs.ParticleTextureID,
            ParticleSampler = DefaultResourcesIDs.LinearClampSamplerID,
            RenderLayer = DefaultResourcesIDs.AlphaRenderLayerID,
            MaxParticles = 1200,
            InitLife = 9f,
            InitSpeed = 1.1f,
            InitSize = 0.11f,
            InitColor = new Color(1f, 1f, 1f, 0.95f),
            Gravity = -1.1f,
            SimulationSpace = SimulationSpaceMode.World
        };

        var emitter = new ParticlesEmitter(desc, graphics, assets);

        try
        {
            var box = new ShapeBoxDescription
            {
                Size = new Vector3(30f, 0.2f, 34f),
                FromCenter = true
            };
            emitter.ShapeEmitter = new BoxShapeEmitter(box);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(LogTag + " 雪形状细配置失败，退回保守发射器: " + ex);
        }

        try
        {
            var rate = new RateSpawn(new SpawnRateDescription()) { Rate = MaxSnowRate };
            emitter.Spawn = rate;
            _snowSpawn = rate;
        }
        catch (Exception ex)
        {
            Debug.WriteLine(LogTag + " 雪发射率细配置失败，退回保守发射器: " + ex);
        }

        var system = new ParticleSystem();
        system.AddEmitter(emitter);

        _snowParticles = new ParticlesComponent { ParticleSystem = system };
        var entity = new Entity("WeatherSnow")
            .AddComponent(new Transform3D { Position = new Vector3(0f, 12f, -8f) })
            .AddComponent(_snowParticles)
            .AddComponent(new ParticlesRenderer());
        this.Managers.EntityManager.Add(entity);
    }

    // ---------------------------------------------------------------- 体积雾

    private void CreateVolumetricFog(GraphicsContext graphics, AssetsService assets)
    {
        // 带 fbm 噪声的半透明材质：每层雾片采样同一 shader，靠世界坐标 + 时间偏移产生
        // "云团漂移 + 距离衰减 + 底部浓顶部淡"的体积观感，而非纯色屏幕渐变。
        var effect = new EffectFromCode(graphics, FogHlsl);
        _fogMaterial = new Material(effect);
        try
        {
            var alphaLayer = assets.Load<RenderLayerDescription>(DefaultResourcesIDs.AlphaRenderLayerID, false);
            if (alphaLayer is not null) _fogMaterial.LayerDescription = alphaLayer;
        }
        catch (Exception ex)
        {
            Debug.WriteLine(LogTag + " 雾材质透明层设置失败（用默认层）: " + ex);
        }

        for (var i = 0; i < FogLayerCount; i++)
        {
            try
            {
                var t = FogLayerCount <= 1 ? 0f : (float)i / (FogLayerCount - 1);
                var z = FogZNear + (FogZFar - FogZNear) * t; // 由近及远
                // 远层更大更淡，近层更小更透，制造纵深叠雾
                var scale = 1.6f + t * 3.4f;
                var transform = new Transform3D
                {
                    Position = new Vector3(0f, 1.5f + t * 0.8f, z),
                    Scale = new Vector3(scale, scale, scale),
                };

                var entity = new Entity("FogLayer-" + i)
                    .AddComponent(transform)
                    // PlaneMesh 默认法线朝 +Y，这里转成朝向相机（相机在 +Z 看向 -Z，用 ZPositive）
                    .AddComponent(new PlaneMesh
                    {
                        Width = 16f,
                        Height = 9f,
                        PlaneNormal = PlaneMesh.NormalAxis.ZPositive,
                    })
                    .AddComponent(new MaterialComponent { Material = _fogMaterial })
                    .AddComponent(new MeshRenderer());
                this.Managers.EntityManager.Add(entity);
                _fogLayers.Add(transform);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(LogTag + " 雾片 " + i + " 创建失败，跳过: " + ex);
            }
        }
    }

    private void CreateUpdater()
    {
        var entity = new Entity("WeatherUniformUpdater")
            .AddComponent(new WeatherUniformUpdater(
                _rainParticles, _snowParticles, _rainSpawn, _snowSpawn,
                _fogMaterial, _cameraTransform, _cameraBasePosition));
        this.Managers.EntityManager.Add(entity);
    }

    private static T? TryResolve<T>() where T : class
    {
        try
        {
            return Application.Current?.Container.Resolve<T>();
        }
        catch (Exception ex)
        {
            Debug.WriteLine(LogTag + " 解析服务 " + typeof(T).Name + " 失败: " + ex);
            return null;
        }
    }

    // ---------------------------------------------------------------- 每帧更新

    private sealed class WeatherUniformUpdater : Behavior
    {
        private readonly ParticlesComponent? _rain;
        private readonly ParticlesComponent? _snow;
        private readonly RateSpawn? _rainSpawn;
        private readonly RateSpawn? _snowSpawn;
        private readonly Material? _fog;
        private readonly Transform3D? _camera;
        private readonly Vector3 _cameraBase;
        private float _time;

        public WeatherUniformUpdater(
            ParticlesComponent? rain,
            ParticlesComponent? snow,
            RateSpawn? rainSpawn,
            RateSpawn? snowSpawn,
            Material? fog,
            Transform3D? camera,
            Vector3 cameraBase)
        {
            _rain = rain;
            _snow = snow;
            _rainSpawn = rainSpawn;
            _snowSpawn = snowSpawn;
            _fog = fog;
            _camera = camera;
            _cameraBase = cameraBase;
        }

        protected override void Update(TimeSpan gameTime)
        {
            var controller = WeatherController.Instance;
            controller.Tick(gameTime);
            _time += (float)gameTime.TotalSeconds;

            ApplyParticles(_rain, _rainSpawn, controller.RainRate, MaxRainRate);
            ApplyParticles(_snow, _snowSpawn, controller.SnowRate, MaxSnowRate);

            var wind = WindSystem.Instance.Direction * WindSystem.Instance.Strength;

            // 相机随风力极轻微视差，保证 UI 可读的同时给出纵深
            if (_camera is not null)
            {
                try
                {
                    _camera.Position = _cameraBase + new Vector3(
                        wind.X * 0.35f,
                        (float)Math.Sin(_time * 0.25f) * 0.08f,
                        wind.Z * 0.2f);
                    _camera.LookAt(new Vector3(wind.X * 0.6f, 2.4f, -8f));
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(LogTag + " 相机视差更新失败: " + ex);
                }
            }

            var fog = _fog;
            if (fog == null) return;

            try
            {
                SetFloat(fog, "Time", _time);
                SetFloat(fog, "Density", controller.CloudDensity);
                SetVector3(fog, "Wind", wind);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(LogTag + " 雾 uniform 更新失败: " + ex);
            }
        }

        private static void ApplyParticles(
            ParticlesComponent? component, RateSpawn? spawn, float intensity, float maxRate)
        {
            if (component == null) return;

            try
            {
                if (spawn != null)
                {
                    spawn.Rate = intensity * maxRate;
                    if (intensity <= 0.001f) component.StopEmitting();
                    else component.StartEmitting();
                }
                else
                {
                    if (intensity > 0.05f) component.StartEmitting();
                    else component.StopEmitting();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(LogTag + " 发射器更新失败: " + ex);
            }
        }

        private static void SetFloat(Material material, string name, float value)
        {
            material.SetParameterValue(name, typeof(float), BitConverter.GetBytes(value));
        }

        private static void SetVector3(Material material, string name, Vector3 value)
        {
            var bytes = new byte[12];
            System.Buffer.BlockCopy(BitConverter.GetBytes(value.X), 0, bytes, 0, 4);
            System.Buffer.BlockCopy(BitConverter.GetBytes(value.Y), 0, bytes, 4, 4);
            System.Buffer.BlockCopy(BitConverter.GetBytes(value.Z), 0, bytes, 8, 4);
            material.SetParameterValue(name, typeof(Vector3), bytes);
        }
    }

    // ---------------------------------------------------------------- HLSL（fbm 云雾）

    // 透视顶点变换（与已验证的 MonitorVectorScene 同款）+ fbm 噪声 alpha，
    // 产生云团漂移/聚散的模糊体积感；片元按世界高度底部浓顶部淡，受风力平移。
    private const string FogHlsl = @"
[Begin_ResourceLayout]
cbuffer PerObject : register(b0)
{
    matrix World : packoffset(c0);
    matrix ViewProjection : packoffset(c4);
}
cbuffer FogParameters : register(b1)
{
    float Time : packoffset(c0.x);
    float Density : packoffset(c0.y);
    float2 _pad0 : packoffset(c0.z);
    float3 Wind : packoffset(c1.x);
    float _pad1 : packoffset(c1.w);
}
[End_ResourceLayout]

[Begin_Pass:Default]
[Profile 11_0]
[Entrypoints VS=VS PS=PS]

struct VSInput
{
    float3 Position : POSITION;
    float2 UV : TEXCOORD0;
};

struct VSOutput
{
    float4 Position : SV_POSITION;
    float2 UV : TEXCOORD0;
    float3 WorldPos : TEXCOORD1;
};

VSOutput VS(VSInput input)
{
    VSOutput o;
    float4 world = mul(float4(input.Position, 1.0), World);
    o.Position = mul(world, ViewProjection);
    o.UV = input.UV;
    o.WorldPos = world.xyz;
    return o;
}

float Hash21(float2 p)
{
    p = frac(p * float2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return frac(p.x * p.y);
}

float Noise(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    float a = Hash21(i);
    float b = Hash21(i + float2(1.0, 0.0));
    float c = Hash21(i + float2(0.0, 1.0));
    float d = Hash21(i + float2(1.0, 1.0));
    float2 u = f * f * (3.0 - 2.0 * f);
    return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
}

float Fbm(float2 p)
{
    float v = 0.0;
    float amp = 0.5;
    for (int i = 0; i < 5; i++)
    {
        v += amp * Noise(p);
        p = p * 2.02 + float2(11.3, 7.7);
        amp *= 0.5;
    }
    return v;
}

float4 PS(VSOutput input) : SV_Target
{
    // 两层噪声反向漂移，产生翻滚/聚散的云雾感
    float2 drift = float2(Wind.x, Wind.z) * Time * 0.25;
    float2 p = input.UV * 3.2 + drift;
    float n = Fbm(p);
    float n2 = Fbm(p * 1.9 - drift * 0.6 + float2(5.2, 1.3));
    float cloud = saturate(n * 0.72 + n2 * 0.38 - 0.22);

    // 底部浓、顶部淡；边缘羽化避免硬边（体积云雾观感而非硬边图形）
    float vertical = saturate(1.0 - input.UV.y);
    float edge = smoothstep(0.0, 0.22, input.UV.x) * smoothstep(1.0, 0.78, input.UV.x)
               * smoothstep(0.0, 0.18, input.UV.y) * smoothstep(1.0, 0.82, input.UV.y);
    float a = saturate(cloud * Density * lerp(0.35, 1.0, vertical) * edge * 0.85);

    // 冷灰蓝雾色，受风力微调；噪声亮部略提亮，形成体积明暗
    float3 fogColor = float3(0.58, 0.66, 0.76) + float3(0.08, 0.09, 0.10) * cloud
                    + float3(Wind.x, 0.0, Wind.z) * 0.02;
    return float4(fogColor, a);
}
[End_Pass]
";
}
#endif
