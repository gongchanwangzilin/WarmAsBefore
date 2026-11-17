#if ANDROID || WINDOWS
using System;
using System.Diagnostics;
using Evergine.Common.Graphics;
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
/// 3D 天气场景：两台 GPU 粒子发射器（雨 / 雪）+ 一个全屏 compute 体积雾后处理，
/// 由 WeatherUniformUpdater 每帧把风场与天气插值值写入发射器与材质 uniform。
/// 每个子系统独立 try/catch，任何失败只跳过该子系统，绝不让场景创建抛异常崩主界面。
/// </summary>
public sealed class WeatherScene : Scene
{
    private const string LogTag = "[WeatherScene]";

    // 后处理线程组必须和 HLSL 的 [numthreads(8,8,1)] 一致
    private const int ThreadGroupX = 8;
    private const int ThreadGroupY = 8;
    private const int ThreadGroupZ = 1;

    private const float MaxRainRate = 180f;
    private const float MaxSnowRate = 60f;

    private ParticlesComponent? _rainParticles;
    private ParticlesComponent? _snowParticles;
    private RateSpawn? _rainSpawn;
    private RateSpawn? _snowSpawn;
    private Material? _fogMaterial;

    protected override void CreateScene()
    {
        var graphics = TryResolve<GraphicsContext>();
        var assets = TryResolve<AssetsService>();

        if (graphics == null || assets == null)
        {
            Debug.WriteLine(LogTag + " 缺少 GraphicsContext/AssetsService，3D 天气场跳过（降级 2D）。");
            return;
        }

        try { CreateRain(graphics, assets); }
        catch (Exception ex) { Debug.WriteLine(LogTag + " 雨发射器创建失败: " + ex); }

        try { CreateSnow(graphics, assets); }
        catch (Exception ex) { Debug.WriteLine(LogTag + " 雪发射器创建失败: " + ex); }

        try { CreateFog(graphics); }
        catch (Exception ex) { Debug.WriteLine(LogTag + " 体积雾创建失败: " + ex); }

        try { CreateUpdater(); }
        catch (Exception ex) { Debug.WriteLine(LogTag + " Uniform Updater 创建失败: " + ex); }
    }

    // ---------------------------------------------------------------- 粒子

    private void CreateRain(GraphicsContext graphics, AssetsService assets)
    {
        var desc = new ParticleEmitterDescription
        {
            ParticleTexture = DefaultResourcesIDs.ParticleTextureID,
            ParticleSampler = DefaultResourcesIDs.LinearClampSamplerID,
            RenderLayer = DefaultResourcesIDs.AlphaRenderLayerID,
            MaxParticles = 1500,
            InitLife = 1.5f,
            InitSpeed = 14f,
            InitSize = 0.06f,
            InitColor = Color.White,
            Gravity = -18f,
            SimulationSpace = SimulationSpaceMode.World
        };

        var emitter = new ParticlesEmitter(desc, graphics, assets);

        // BoxShapeEmitter / RateSpawn 的细配置按已核实签名构造，但若此版本运行时行为
        // 有出入就退回上面的保守 desc（仅 Init* 字段），保证不崩。
        try
        {
            var box = new ShapeBoxDescription
            {
                Size = new Vector3(24f, 0.2f, 24f),
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
            .AddComponent(new Transform3D())
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
            MaxParticles = 900,
            InitLife = 7f,
            InitSpeed = 1.2f,
            InitSize = 0.12f,
            InitColor = Color.White,
            Gravity = -1.2f,
            SimulationSpace = SimulationSpaceMode.World
        };

        var emitter = new ParticlesEmitter(desc, graphics, assets);

        try
        {
            var box = new ShapeBoxDescription
            {
                Size = new Vector3(24f, 0.2f, 24f),
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
            .AddComponent(new Transform3D())
            .AddComponent(_snowParticles)
            .AddComponent(new ParticlesRenderer());
        this.Managers.EntityManager.Add(entity);
    }

    // ---------------------------------------------------------------- 体积雾

    private void CreateFog(GraphicsContext graphics)
    {
        var effect = new EffectFromCode(graphics, FogHlsl);
        _fogMaterial = new Material(effect);

        // 全屏 compute 后处理图。节点/端口按本版本已核实的 API 构造；
        // 端口值类型用泛型 PostProcessingNodePortParameterType<float> 占位，
        // 真实纹理绑定交给引擎的默认 scene-color 通道。
        var description = new PostProcessingGraphDescription();

        var node = new PostProcessingNode(effect, "VolumetricFog")
        {
            // ComputeTask 由 PostProcessingNode(effect, name) 构造时依据 effect 自动建立，属性只读。
            ThreadGroupDivisorX = ThreadGroupX,
            ThreadGroupDivisorY = ThreadGroupY,
            ThreadGroupDivisorZ = ThreadGroupZ
        };
        node.AddInputPort(new PostProcessingInputNodePort(
            "input", new PostProcessingNodePortParameterType<float>()));
        node.AddOutputPort(new PostProcessingOutputNodePort(
            "output", new PostProcessingNodePortParameterType<float>()));

        description.AddNode(node);
        description.StartNode = node;
        description.EndNode = node;

        var graph = new PostProcessingGraph(description);
        var renderer = new Evergine.Components.Graphics3D.PostProcessingGraphRenderer
        {
            ppGraph = graph,
            IsGlobal = true
        };

        var entity = new Entity("WeatherFog")
            .AddComponent(new Transform3D())
            .AddComponent(renderer);
        this.Managers.EntityManager.Add(entity);
    }

    private void CreateUpdater()
    {
        var entity = new Entity("WeatherUniformUpdater")
            .AddComponent(new WeatherUniformUpdater(
                _rainParticles, _snowParticles, _rainSpawn, _snowSpawn, _fogMaterial));
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

        public WeatherUniformUpdater(
            ParticlesComponent? rain,
            ParticlesComponent? snow,
            RateSpawn? rainSpawn,
            RateSpawn? snowSpawn,
            Material? fog)
        {
            _rain = rain;
            _snow = snow;
            _rainSpawn = rainSpawn;
            _snowSpawn = snowSpawn;
            _fog = fog;
        }

        protected override void Update(TimeSpan gameTime)
        {
            var controller = WeatherController.Instance;
            controller.Tick(gameTime);

            ApplyParticles(_rain, _rainSpawn, controller.RainRate, MaxRainRate);
            ApplyParticles(_snow, _snowSpawn, controller.SnowRate, MaxSnowRate);

            var fog = _fog;
            if (fog == null)
            {
                return;
            }

            try
            {
                SetFloat(fog, "Density", controller.CloudDensity);
                var wind = WindSystem.Instance.Direction * WindSystem.Instance.Strength;
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
            if (component == null)
            {
                return;
            }

            try
            {
                if (spawn != null)
                {
                    // 有发射率 setter：直接按比例插值发射率。
                    spawn.Rate = intensity * maxRate;
                    if (intensity <= 0.001f)
                    {
                        component.StopEmitting();
                    }
                    else
                    {
                        component.StartEmitting();
                    }
                }
                else
                {
                    // 简化：拿不到发射率 setter 时，用 Start/StopEmitting 做可见性开关，
                    // 只保留天气切换的开关效果，放弃 1.5s 内的发射率渐变。
                    if (intensity > 0.05f)
                    {
                        component.StartEmitting();
                    }
                    else
                    {
                        component.StopEmitting();
                    }
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
            // float3 按 X/Y/Z 各 4 字节顺序拼成 12 字节。
            var bytes = new byte[12];
            System.Buffer.BlockCopy(BitConverter.GetBytes(value.X), 0, bytes, 0, 4);
            System.Buffer.BlockCopy(BitConverter.GetBytes(value.Y), 0, bytes, 4, 4);
            System.Buffer.BlockCopy(BitConverter.GetBytes(value.Z), 0, bytes, 8, 4);
            material.SetParameterValue(name, typeof(Vector3), bytes);
        }
    }

    // ---------------------------------------------------------------- HLSL

    private const string FogHlsl = @"
[Begin_ResourceLayout]
cbuffer FogParameters : register(b0)
{
    float Density : packoffset(c0.x);
    float3 Wind : packoffset(c0.y);
}
SamplerState LinearSampler : register(s0);
Texture2D<float4> InputTexture : register(t0);
RWTexture2D<float4> OutputTexture : register(u0);
[End_ResourceLayout]

[Begin_Pass:Default]
[Profile 11_0]
[Entrypoints CS=CS]
[numthreads(8,8,1)]
void CS(uint3 threadId : SV_DispatchThreadID)
{
    uint2 dim;
    InputTexture.GetDimensions(dim.x, dim.y);
    uint2 uv = min(threadId.xy, dim - 1);

    float4 scene = InputTexture.Load(uint3(uv, 0));

    // 屏幕下方（y 大）雾更浓，上方更淡
    float height = dim.y > 1 ? (float)uv.y / (float)(dim.y - 1) : 0.0;
    float fog = saturate(Density * lerp(1.0, 0.35, height));

    float3 fogColor = float3(0.52, 0.62, 0.74) + Wind * 0.03;
    OutputTexture[uv] = float4(lerp(scene.rgb, fogColor, fog), scene.a);
}
[End_Pass]
";
}
#endif
