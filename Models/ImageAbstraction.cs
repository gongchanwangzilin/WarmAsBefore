namespace WarmAsBefore.Models;

/// <summary>
/// F27 ①：一张图片在进入记忆时被抽象成的三元组 —— 具体的「图形 / 词汇 / 联系」。
///
/// 论文口径：人记住的不是像素，而是「有什么形状、能叫什么、和什么连着」。
/// 这里是纯代码抽象（不调视觉模型、不读原始像素做语义识别）：
/// · 图形：从文本线索词表认出形状/主色，配图片宽高比给出「横幅/竖幅/方幅」；
/// · 词汇：从这句话与标签里抽出的实体词；
/// · 联系：这条记忆与同角色已有记忆共享的关联词。
///
/// 原图 <see cref="HumanMemoryEntry.ImagePath"/> 保留为可追溯附件，随时能翻回原片。
/// 旧存档没有这个字段时为 null，读档自动兼容。
/// </summary>
public sealed record ImageAbstraction
{
    /// <summary>图形描述：如「横幅」「圆形」「暖红」。纯代码从线索词与宽高比得出。</summary>
    public List<string> Shapes { get; init; } = new();

    /// <summary>词汇：这句话里的实体词 / 标签词，供链式搜索搭桥。</summary>
    public List<string> Words { get; init; } = new();

    /// <summary>联系：与已有记忆共享的关联词（按权重排序，最多 8 个）。</summary>
    public List<string> Links { get; init; } = new();

    /// <summary>抽象置信度 0~1：线索越多越具体，供 UI 与衰减用。</summary>
    public double Confidence { get; init; }
}
