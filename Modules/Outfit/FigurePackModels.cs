namespace WarmAsBefore.Modules.Outfit;

/// <summary>
/// 新版着装形象包：一个角色（或一套画风）的全部部件、服装、配色与动作。
/// 这里是「数据」而不是「渲染结果」——同一份包可以按任意目标尺寸合成，缓存另行落盘。
/// 全部字段都有默认值：老包缺少新字段时反序列化仍能工作，缺省值把行为压回「只有基础部件」。
/// </summary>
public sealed record FigurePack
{
    /// <summary>包 id，通常等于 assets/figures 下的目录名；为空时载入方会用目录名补上。</summary>
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";

    /// <summary>画布逻辑尺寸：部件里的坐标、位移、缩放都按这个坐标系解释，与最终输出像素无关。</summary>
    public int CanvasWidth { get; init; } = 1080;
    public int CanvasHeight { get; init; } = 1920;

    public List<FigurePart> Parts { get; init; } = new();
    public List<FigureOutfit> Outfits { get; init; } = new();
    public List<FigureScheme> Schemes { get; init; } = new();
    public List<FigureMotion> Motions { get; init; } = new();
}

/// <summary>
/// 一个可独立绘制的部件（身体、头发、上衣、脸……）。
/// 贴图路径一律相对 figure.json 所在目录，包可以整体搬走而不坏。
/// </summary>
public sealed record FigurePart
{
    public string Id { get; init; } = "";

    /// <summary>图层顺序，大的盖住小的；相同 z 按在 Parts 里的先后顺序稳定绘制。</summary>
    public int Z { get; init; }

    /// <summary>贴图相对路径；空字符串表示这个部件不画（留作槽位占位）。</summary>
    public string Texture { get; init; } = "";

    /// <summary>槽位：base/hair/top/bottom/shoes/accessory……用于配色表查色与外部编辑归类。</summary>
    public string Slot { get; init; } = "";

    /// <summary>轴心在贴图内部的归一化位置（0=左边/上边，1=右边/下边）。</summary>
    public double AnchorX { get; init; } = 0.5;
    public double AnchorY { get; init; } = 0.5;

    /// <summary>轴心落在画布上的归一化位置。整张画布大小的部件保持 0.5 即可严格对齐。</summary>
    public double X { get; init; } = 0.5;
    public double Y { get; init; } = 0.5;

    /// <summary>在画布逻辑坐标系里的额外位移（像素），方便微调切图后的碎片。</summary>
    public double OffsetX { get; init; }
    public double OffsetY { get; init; }

    /// <summary>缩放：1 表示贴图像素与画布逻辑像素 1:1。</summary>
    public double Scale { get; init; } = 1;

    /// <summary>部件不透明度（0~1），叠色后乘上去。</summary>
    public double Opacity { get; init; } = 1;

    /// <summary>表情专用贴图：键是表情名（normal/smile…），命中时优先于服装覆写与基础贴图。</summary>
    public Dictionary<string, string> EmotionTextures { get; init; } = new();
}

/// <summary>
/// 一套服装。覆写以部件 id 为键而不是槽位：同一个槽位可能有多个装饰件，
/// 按部件替换才能只换「裙子」而不惊动「腰带」。值给空字符串表示把该部件藏起来。
/// </summary>
public sealed record FigureOutfit
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";

    /// <summary>partId → 贴图相对路径；空值隐藏部件。</summary>
    public Dictionary<string, string> Overrides { get; init; } = new();

    /// <summary>整体色调（#RRGGBB/#AARRGGBB，可空）：对全部部件做 multiply，模拟服装/打光统一色温。</summary>
    public string Tint { get; init; } = "";
}

/// <summary>
/// 配色表：对灰度（明度）贴图按槽位做 multiply 上色。
/// 用槽位而不是部件 id，是为了换一套配色时不用逐个部件登记。
/// </summary>
public sealed record FigureScheme
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";

    /// <summary>slot → #RRGGBB。缺的槽位按白色（不改变原图）处理。</summary>
    public Dictionary<string, string> SlotColors { get; init; } = new();

    /// <summary>整套形象的额外透明度（0~1），与部件自身不透明度相乘。</summary>
    public double Opacity { get; init; } = 1;
}

/// <summary>
/// 一个动作（idle/walk/blink…）。动画由调用方按时钟取帧，合成器只负责把某一帧画出来。
/// </summary>
public sealed record FigureMotion
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public List<FigureMotionFrame> Frames { get; init; } = new();
    public bool Loop { get; init; } = true;
}

/// <summary>
/// 动作的一帧。两种表达二选一，也可同时给：
/// Texture 是整帧完整贴图（走/跑这类整身帧）；PartOverrides 只改若干部件（眨眼、转头）。
/// </summary>
public sealed record FigureMotionFrame
{
    /// <summary>整帧贴图相对路径；非空时作为完整画面，不再拼基础部件（帧内覆写仍会叠上去）。</summary>
    public string Texture { get; init; } = "";

    /// <summary>partId → 贴图相对路径；空值隐藏部件。</summary>
    public Dictionary<string, string> PartOverrides { get; init; } = new();

    /// <summary>本帧停留时长（毫秒）。逐帧给值，允许非均匀节奏（例如眨眼最后一帧更短）。</summary>
    public int DurationMs { get; init; } = 100;
}
