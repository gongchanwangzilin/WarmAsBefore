namespace WarmAsBefore.Models;

/// <summary>
/// 关系网里的一个节点。旧版只有「词」，所以这张网只能画一条线连两个词；
/// 现在节点本身可以是一段记忆、一张照片、一个标签、一个人——线的两端不再是同一类东西。
/// </summary>
public enum NodeKind
{
    /// <summary>一个词（共现学来的最小单位）。</summary>
    Word = 0,
    /// <summary>一个人（TA 记得的某个人，通常是用户或用户提到的人）。</summary>
    Person = 1,
    /// <summary>一段记得的事（指向人类记忆条目）。</summary>
    Memory = 2,
    /// <summary>一个标签（句级/整条标签都算）。</summary>
    Tag = 3,
    /// <summary>一张图片（照片式记忆）。</summary>
    Image = 4,
    /// <summary>一句话说原文（文本节点）。</summary>
    Text = 5,
    /// <summary>一个地点。</summary>
    Place = 6,
    /// <summary>一个东西。</summary>
    Thing = 7,
    /// <summary>一种情绪。</summary>
    Emotion = 8
}

/// <summary>
/// 联系方式。「等价」和「联系」必须分开：
/// 人把「妈妈」和「老妈」当成同一个东西（读到任一个都会想起另一个），
/// 但「妈妈」和「厨房」只是有关联——混成一种边，扩散检索就会顺着弱关联跑偏。
/// </summary>
public enum RelationKind
{
    /// <summary>等价：同一个东西的两个名字，扩散时按同一条处理。</summary>
    Equivalent = 0,
    /// <summary>一般联系（联想）。</summary>
    Associated = 1,
    /// <summary>归属：具体 → 类别（下雨 → 天气）。</summary>
    BelongsTo = 2,
    /// <summary>因果：A 导致 B（停电 → 你躲在我后面）。</summary>
    Causal = 3,
    /// <summary>时间上的先后 / 同一场合。</summary>
    Temporal = 4,
    /// <summary>空间：在某地发生、和某物在一起。</summary>
    Spatial = 5,
    /// <summary>情绪：这件事连着某种感受。</summary>
    Emotional = 6,
    /// <summary>对立：一提到 A 就想到不是 B。</summary>
    Contrasts = 7
}

/// <summary>
/// 节点：Id 由「类型前缀 + 规范化名字」决定（<see cref="RelationIds"/>），
/// 所以同一个人在不同轮对话里只会长出一个点，不会每提一次就多一个疙瘩。
/// </summary>
public sealed record RelationNode
{
    public string Id { get; init; } = "";
    public string Label { get; init; } = "";
    public NodeKind Kind { get; init; } = NodeKind.Word;

    /// <summary>指回原物：记忆条目 Id / 图片相对路径 / 标签的分类；没有就空。</summary>
    public string Ref { get; init; } = "";

    /// <summary>显著性 0~1：决定画出来多大、多亮（由权重、被想起次数、重要性汇成）。</summary>
    public double Salience { get; init; } = 0.35;

    /// <summary>来源：auto 共现长出来 / human 人工 / ai 模型整理。</summary>
    public string Source { get; init; } = "auto";

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; init; } = DateTime.UtcNow;
}

/// <summary>一条有向的联系方式（等价边会双向存一份，扩散时才好走）。</summary>
public sealed record RelationEdge
{
    public string From { get; init; } = "";
    public string To { get; init; } = "";
    public RelationKind Kind { get; init; } = RelationKind.Associated;

    /// <summary>联系强度 0.1~500，沿用旧关联网的量纲，累积与纠错的阈值不用重调。</summary>
    public double Weight { get; init; } = 1;

    public string Source { get; init; } = "auto";
    public DateTime UpdatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>边在表里的唯一键。计算出来的，不进 JSON——不然每条联系都要把同样的信息写两遍。</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string Key => RelationIds.EdgeKey(From, To, Kind);
}

/// <summary>
/// 一个人脑子里的那张网。存储就按这个结构直接序列化成 JSON
/// （<c>relation_networks.json</c>：一个角色一张网），界面上的 JSON 视图显示的就是它本身。
/// </summary>
public sealed class RelationGraph
{
    /// <summary>结构版本：从 0（只有词与词的旧格式）升上来时用来判断要不要迁移。</summary>
    public int Version { get; set; } = 2;

    public List<RelationNode> Nodes { get; set; } = new();
    public List<RelationEdge> Edges { get; set; } = new();

    /// <summary>
    /// 被人断过的联系（边 Key 名单）。自动织边每次同步都会跑，没有这份名单，
    /// 「这两个词其实没关系」当场断开、下次进页面又长回来——人工纠正就等于没纠正。
    /// </summary>
    public List<string> Pruned { get; set; } = new();
}

/// <summary>节点 Id 的算法：同类型 + 同名字必须得到同一个 Id，否则网络会越长越肿。</summary>
public static class RelationIds
{
    public static string For(NodeKind kind, string nameOrRef)
    {
        var prefix = kind switch
        {
            NodeKind.Person => "p",
            NodeKind.Memory => "m",
            NodeKind.Tag => "t",
            NodeKind.Image => "i",
            NodeKind.Text => "x",
            NodeKind.Place => "l",
            NodeKind.Thing => "g",
            NodeKind.Emotion => "e",
            _ => "w"
        };
        return prefix + ":" + Normalize(kind, nameOrRef);
    }

    /// <summary>
    /// 记忆/图片按引用原文（一条记忆就是一个点，名字重不重复无所谓）；
    /// 其它类型按名字合并，并做全角→半角、去空白，「妈妈 」和「妈妈」才是同一个点。
    /// </summary>
    private static string Normalize(NodeKind kind, string raw)
    {
        var s = (raw ?? "").Trim();
        if (kind is NodeKind.Memory or NodeKind.Image or NodeKind.Text)
            return s.Length <= 24 ? s : s[..24];
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (var c in s.ToLowerInvariant())
        {
            if (char.IsWhiteSpace(c)) continue;
            sb.Append(c);
        }
        var norm = sb.ToString();
        return norm.Length <= 24 ? norm : norm[..24];
    }

    public static string EdgeKey(string from, string to, RelationKind kind) => $"{from}|{kind}|{to}";
}

/// <summary>关系列表的一行（两个节点的名字 + 联系方式），给列表界面和自检用。</summary>
public sealed record LinkView(string A, string B, double Weight, string Source, RelationKind Kind);

/// <summary>
/// 量化模型给出的「数值上最接近」的一行：语境余弦（像不像）、nPMI（有没有同框证据）、见过几轮，
/// 以及<b>是靠哪个词算出来的</b>（选中一整句时，句子本身不在底账里，取的是句中的词）。
/// 三个数一起给，才看得出这条近义是「说的同一类东西」还是「只碰巧一起出现过一次」。
/// </summary>
public sealed record QuantMatch(string Term, double Cosine, double Npmi, int Turns, string Via);

/// <summary>关系统计：画图和列表都从这里取话术，别再各自 switch 一遍。</summary>
public static class Relations
{
    public static string KindText(RelationKind k) => k switch
    {
        RelationKind.Equivalent => "等价",
        RelationKind.Associated => "联系",
        RelationKind.BelongsTo => "属于",
        RelationKind.Causal => "导致",
        RelationKind.Temporal => "先后",
        RelationKind.Spatial => "在一处",
        RelationKind.Emotional => "连着情绪",
        RelationKind.Contrasts => "相反",
        _ => "联系"
    };

    public static string NodeText(NodeKind k) => k switch
    {
        NodeKind.Person => "人",
        NodeKind.Memory => "记得的事",
        NodeKind.Tag => "标签",
        NodeKind.Image => "照片",
        NodeKind.Text => "一句话",
        NodeKind.Place => "地点",
        NodeKind.Thing => "东西",
        NodeKind.Emotion => "情绪",
        _ => "词"
    };

    /// <summary>
    /// 等价关系在扩散时几乎不衰减（想到「老妈」等于想到「妈妈」），
    /// 其它关系按强度摊到对方的出边上（fan effect：一个概念连得越散，单条联系分到的激活越少）。
    /// </summary>
    public static double SpreadFactor(RelationKind k) => k switch
    {
        RelationKind.Equivalent => 0.95,
        RelationKind.Causal => 0.6,
        RelationKind.Emotional => 0.5,
        RelationKind.BelongsTo => 0.45,
        RelationKind.Contrasts => 0.25,
        _ => 0.4
    };
}
