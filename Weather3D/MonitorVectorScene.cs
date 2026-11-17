#if ANDROID || WINDOWS
using System;
using System.Diagnostics;
using Evergine.Common.Graphics;
using Evergine.Components.Graphics3D;
using Evergine.Framework;
using Evergine.Framework.Graphics;
using Evergine.Framework.Graphics.Effects;
using Evergine.Framework.Services;
using Evergine.Mathematics;
// MAUI / System 与 Evergine 有同名类型，显式别名消歧
using Application = Evergine.Framework.Application;

namespace WarmAsBefore.Weather3D;

/// <summary>
/// 超级监测页的 3D 向量场景：16 个悬浮方块（8 器官 + 5 记忆层 + 3 数值）按 4×4 网格排布。
/// 场景本身不认识角色；外部通过 <see cref="ApplyNodes"/> 把 (id, value, lit) 推进来更新节点的高度/缩放。
/// 节点 id 约定：organ-0..organ-7（对应 OrganKind 的整数值）、mem-0..mem-4、stat-0..stat-2。
/// 每个子系统独立 try/catch；拿不到 GraphicsContext/AssetsService 时整个场景跳过，绝不让 2D 监测页崩。
/// </summary>
public sealed class MonitorVectorScene : Scene
{
    private const string LogTag = "[MonitorVectorScene]";

    private const int Columns = 4;
    private const int Rows = 4;
    private const float Spacing = 2.2f;
    private const float CubeSize = 1f;

    private static readonly string[] NodeIds = BuildNodeIds();

    // 只存 Transform3D 引用，ApplyNodes 不需要再向 Entity 查询组件。
    private readonly System.Collections.Generic.Dictionary<string, Transform3D> _transforms =
        new(StringComparer.Ordinal);

    protected override void CreateScene()
    {
        var graphics = TryResolve<GraphicsContext>();
        var assets = TryResolve<AssetsService>();

        if (graphics == null || assets == null)
        {
            Debug.WriteLine(LogTag + " 缺少 GraphicsContext/AssetsService，3D 向量节点跳过（降级 2D）。");
            return;
        }

        try { CreateCamera(); }
        catch (Exception ex) { Debug.WriteLine(LogTag + " 相机创建失败: " + ex); }

        try { CreateNodes(graphics, assets); }
        catch (Exception ex) { Debug.WriteLine(LogTag + " 节点创建失败: " + ex); }
    }

    /// <summary>固定相机：不需要输入行为，只看这一组静态节点。</summary>
    private void CreateCamera()
    {
        var transform = new Transform3D { Position = new Vector3(0f, 6f, 11f) };
        var camera = new Entity("MonitorVectorCamera")
            .AddComponent(transform)
            .AddComponent(new Camera3D());
        this.Managers.EntityManager.Add(camera);

        try { transform.LookAt(new Vector3(0f, 0f, 0f)); }
        catch (Exception ex) { Debug.WriteLine(LogTag + " 相机朝向失败（保留默认朝向）: " + ex); }
    }

    private void CreateNodes(GraphicsContext graphics, AssetsService assets)
    {
        // 优先用引擎自带默认材质（最稳，不需要自写 shader）；失败再退回固定色 HLSL；
        // 两者都失败则 MaterialComponent 不设材质，交给引擎自己的默认材质兜底。
        var material = TryCreateMaterial(graphics, assets);

        for (var i = 0; i < NodeIds.Length; i++)
        {
            var id = NodeIds[i];
            try
            {
                var column = i % Columns;
                var row = i / Columns;
                var x = (column - (Columns - 1) * 0.5f) * Spacing;
                var z = (row - (Rows - 1) * 0.5f) * Spacing;

                var transform = new Transform3D { Position = new Vector3(x, 0f, z) };
                var materialComponent = new MaterialComponent();
                if (material is not null) materialComponent.Material = material;

                var entity = new Entity("Node-" + id)
                    .AddComponent(transform)
                    .AddComponent(new CubeMesh { Size = CubeSize })
                    .AddComponent(materialComponent)
                    .AddComponent(new MeshRenderer());

                this.Managers.EntityManager.Add(entity);
                _transforms[id] = transform;
            }
            catch (Exception ex)
            {
                Debug.WriteLine(LogTag + " 节点 " + id + " 创建失败，跳过: " + ex);
            }
        }
    }

    private static Material? TryCreateMaterial(GraphicsContext graphics, AssetsService assets)
    {
        try
        {
            var defaultMaterial = assets.Load<Material>(DefaultResourcesIDs.DefaultMaterialID);
            if (defaultMaterial is not null) return defaultMaterial;
        }
        catch (Exception ex)
        {
            Debug.WriteLine(LogTag + " 默认材质加载失败，尝试 HLSL 固定色材质: " + ex);
        }

        try
        {
            return new Material(new EffectFromCode(graphics, FixedColorHlsl));
        }
        catch (Exception ex)
        {
            // HLSL 编译失败不致命：MaterialComponent 不设材质也能走引擎默认材质。
            Debug.WriteLine(LogTag + " 固定色材质创建失败，使用引擎默认材质: " + ex);
            return null;
        }
    }

    /// <summary>
    /// 外部数据入口：按 id 更新节点高度与缩放（value 归一化到 0..1，lit 决定是否额外抬高/放大）。
    /// 找不到对应实体就跳过；任何异常都被吞掉。
    /// </summary>
    public void ApplyNodes(System.Collections.Generic.IReadOnlyList<(string id, double value, bool lit)>? nodes)
    {
        if (nodes is null) return;

        try
        {
            foreach (var node in nodes)
            {
                var id = node.id;
                if (string.IsNullOrEmpty(id) || !_transforms.TryGetValue(id, out var transform)) continue;

                var raw = node.value;
                var value = double.IsNaN(raw) || double.IsInfinity(raw)
                    ? 0d
                    : Math.Max(0d, Math.Min(1d, raw));
                var v = (float)value;

                var y = v * 2.4f + (node.lit ? 0.6f : 0f);
                var size = 0.75f + v * 0.45f + (node.lit ? 0.25f : 0f);

                transform.Position = new Vector3(transform.Position.X, y, transform.Position.Z);
                transform.Scale = new Vector3(size, size * (node.lit ? 1.35f : 1f), size);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine(LogTag + " ApplyNodes 更新失败: " + ex);
        }
    }

    private static string[] BuildNodeIds()
    {
        var ids = new string[16];
        for (var i = 0; i < 8; i++) ids[i] = "organ-" + i;
        for (var i = 0; i < 5; i++) ids[8 + i] = "mem-" + i;
        ids[13] = "stat-0";
        ids[14] = "stat-1";
        ids[15] = "stat-2";
        return ids;
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

    // ---------------------------------------------------------------- HLSL（默认材质不可用时的兜底）

    private const string FixedColorHlsl = @"
[Begin_ResourceLayout]
cbuffer PerObject : register(b0)
{
    matrix World : packoffset(c0);
    matrix ViewProjection : packoffset(c4);
}
[End_ResourceLayout]

[Begin_Pass:Default]
[Profile 11_0]
[Entrypoints VS=VS PS=PS]

struct VSInput
{
    float3 Position : POSITION;
};

struct VSOutput
{
    float4 Position : SV_POSITION;
};

VSOutput VS(VSInput input)
{
    VSOutput output;
    output.Position = mul(mul(float4(input.Position, 1.0), World), ViewProjection);
    return output;
}

float4 PS(VSOutput input) : SV_Target
{
    return float4(0.30, 0.72, 0.92, 1.0);
}
[End_Pass]
";
}
#endif
