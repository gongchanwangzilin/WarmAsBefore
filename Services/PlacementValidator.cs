using System.Text.Json;
using System.Text.Json.Serialization;
using WarmAsBefore.Models;

namespace WarmAsBefore.Services;

/// <summary>
/// 素材包里「定位 json」的传输结构。
///
/// 刻意与 <see cref="ScenePlacement"/> 分开：素材包清单用的是 **camelCase**
/// （与 <c>manifest.json</c> 同族），而 GameSettings/mapdata 一路走的是默认 PascalCase，
/// 两套命名规则混用会埋下"字段名对不上但反序列化不报错、值全是默认值"的坑。
/// </summary>
public sealed class PlacementSidecarDto
{
    [JsonPropertyName("schema")] public string? Schema { get; set; }
    [JsonPropertyName("image")] public string? Image { get; set; }
    [JsonPropertyName("imageWidth")] public int? ImageWidth { get; set; }
    [JsonPropertyName("imageHeight")] public int? ImageHeight { get; set; }
    [JsonPropertyName("landing")] public string? Landing { get; set; }
    [JsonPropertyName("scaleMethod")] public string? ScaleMethod { get; set; }
    [JsonPropertyName("globalFactor")] public double? GlobalFactor { get; set; }
    [JsonPropertyName("refHeightRatio")] public double? RefHeightRatio { get; set; }
    [JsonPropertyName("points")] public List<NormPoint>? Points { get; set; }
    [JsonPropertyName("lines")] public List<NormLine>? Lines { get; set; }
    [JsonPropertyName("scaleLines")] public List<NormLine>? ScaleLines { get; set; }
    [JsonPropertyName("spheres")] public List<NormSphere>? Spheres { get; set; }
    [JsonPropertyName("note")] public string? Note { get; set; }
}

/// <summary>
/// 素材包 sidecar 的校验。
///
/// 规格要求"素材包里带定位 json 的可以跳过手动标定，但必须校验"——
/// 所以这里的职责是：**能证明它可信就采纳，不能证明就拒绝并说明理由**，
/// 绝不静默忽略、也绝不"看着差不多就用"。
///
/// 几何规则复用 <see cref="PlacementMath.Validate"/>，与 App 内手画标定同一套口径，
/// 保证"包里给的标定"和"人画的标定"在被信任程度上一视同仁。
/// </summary>
public static class PlacementValidator
{
    public const string SchemaId = "wab.placement/1";

    /// <summary>宽高比允许的相对误差：归一化坐标只在保比例变换下才等价，比例变了坐标就全错。</summary>
    private const double AspectTolerance = 0.01;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <summary>解析并校验 sidecar 文本。返回 (标定数据, 错误列表)；有错时标定为 null。</summary>
    public static (ScenePlacement? Placement, List<string> Errors) FromJson(
        string json, string imageFileName, (int W, int H) actualSize)
    {
        PlacementSidecarDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<PlacementSidecarDto>(json, Json);
        }
        catch (Exception ex)
        {
            return (null, new List<string> { $"定位 json 解析失败：{ex.Message}" });
        }
        return Validate(dto, imageFileName, actualSize);
    }

    public static (ScenePlacement? Placement, List<string> Errors) Validate(
        PlacementSidecarDto? dto, string imageFileName, (int W, int H) actualSize)
    {
        var errors = new List<string>();
        if (dto is null) return (null, new List<string> { "定位 json 内容为空" });

        // 1) schema 必须严格匹配——不认识的版本不猜，直接拒绝
        if (!string.Equals(dto.Schema, SchemaId, StringComparison.Ordinal))
        {
            errors.Add($"未知的定位 json 版本（schema=\"{dto.Schema}\"，本程序只认 \"{SchemaId}\"）");
            return (null, errors);
        }

        // 2) 图的身份必须对得上：防止 sidecar 配错了图
        if (!string.IsNullOrWhiteSpace(dto.Image))
        {
            var want = Path.GetFileNameWithoutExtension(dto.Image);
            var got = Path.GetFileNameWithoutExtension(imageFileName);
            if (!string.Equals(want, got, StringComparison.OrdinalIgnoreCase))
                errors.Add($"定位 json 里写的图是「{dto.Image}」，但同目录下的图是「{imageFileName}」，两者对不上");
        }

        // 3) 声明尺寸与实测尺寸的**宽高比**必须一致（分辨率不同没关系，比例不同则坐标全错）
        if (dto.ImageWidth is > 0 && dto.ImageHeight is > 0 && actualSize.W > 0 && actualSize.H > 0)
        {
            var declared = dto.ImageWidth.Value / (double)dto.ImageHeight.Value;
            var actual = actualSize.W / (double)actualSize.H;
            if (Math.Abs(declared - actual) / actual > AspectTolerance)
                errors.Add($"定位 json 声明的尺寸 {dto.ImageWidth}×{dto.ImageHeight}（比例 {declared:0.###}）" +
                           $"与实际图片 {actualSize.W}×{actualSize.H}（比例 {actual:0.###}）不一致，坐标无法对齐");
        }

        if (errors.Count > 0) return (null, errors);

        var placement = new ScenePlacement
        {
            Landing = string.IsNullOrWhiteSpace(dto.Landing) ? LandingKinds.Point : dto.Landing!,
            ScaleMethod = string.IsNullOrWhiteSpace(dto.ScaleMethod) ? ScaleMethods.Spheres : dto.ScaleMethod!,
            GlobalFactor = dto.GlobalFactor ?? 1.0,
            RefHeightRatio = dto.RefHeightRatio ?? 0.5,
            Points = dto.Points ?? new List<NormPoint>(),
            Lines = dto.Lines ?? new List<NormLine>(),
            ScaleLines = dto.ScaleLines ?? new List<NormLine>(),
            Spheres = dto.Spheres ?? new List<NormSphere>(),
            Note = dto.Note ?? "",
            SourceWidth = dto.ImageWidth ?? actualSize.W,
            SourceHeight = dto.ImageHeight ?? actualSize.H
        };

        // 4) 几何与取值范围走与 App 内标定完全相同的校验
        var (ok, missing) = PlacementMath.Validate(placement);
        if (!ok)
        {
            errors.AddRange(missing.Select(m => "定位 json 的标定数据不可用：" + m));
            return (null, errors);
        }

        return (placement, errors);
    }

    /// <summary>把标定数据导出成 sidecar 文本（地图导出/素材包制作时用）。</summary>
    public static string ToJson(ScenePlacement p, string imageFileName) =>
        JsonSerializer.Serialize(new PlacementSidecarDto
        {
            Schema = SchemaId,
            Image = imageFileName,
            ImageWidth = p.SourceWidth,
            ImageHeight = p.SourceHeight,
            Landing = p.Landing,
            ScaleMethod = p.ScaleMethod,
            GlobalFactor = p.GlobalFactor,
            RefHeightRatio = p.RefHeightRatio,
            Points = p.Points,
            Lines = p.Lines,
            ScaleLines = p.ScaleLines,
            Spheres = p.Spheres,
            Note = p.Note
        }, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        });

    /// <summary>sidecar 的文件名约定：与图片同名、同目录，后缀 .placement.json。</summary>
    public static string SidecarPathFor(string imageAbsPath) =>
        Path.Combine(
            Path.GetDirectoryName(imageAbsPath) ?? "",
            Path.GetFileNameWithoutExtension(imageAbsPath) + ".placement.json");
}
