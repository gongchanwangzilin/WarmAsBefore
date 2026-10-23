using Microsoft.Maui.Graphics;
using WarmAsBefore.DesignSystem.Worlds;
using WarmAsBefore.Models;

namespace WarmAsBefore.Drawables;

/// <summary>
/// 关系网的调色：画布（<see cref="MemoryGraphDrawable"/>）与页面图例共用的**唯一**出处。
///
/// 为什么要单独立一个类：图例是照着画布手抄的第二份颜色，抄完就没人再对。
/// 实测已经漂了三处 —— 图例写 #8FA8C8、画布画 #7A9EC4（模型整理出来的那颗小蓝点）；
/// 图例写 #FAEBD7、画布画 #FFF2DC（记得的事那张卡片的面）；
/// 图例的三条线一律涂墨色、画布却按联系类型上色。改一处忘一处，用户看到的就是「图例在骗人」。
///
/// 现在两边都读这里：画布拿 <see cref="Node"/> / <see cref="Edge"/>，
/// XAML 拿 <c>{x:Static draw:GraphPalette.Person}</c> 这类成员。数值只在这一页出现一次。
///
/// 世界（轻质感 / 极简 / 渐变 / 液态玻璃 × 明暗 × 四套色板）来了之后，这个类做的事只改了一半：
/// 「哪一类东西是哪族颜色」还是这里说了算 —— 人是暖琥珀、标签是绿、地点是蓝、东西偏红、
/// 情绪是粉，这句话在四个世界里都不许变，否则整张网就失去了可读的形状。
/// 变的是**明度**：那些数都是在奶油纸上标定的，同一个 #E8B87A 压在暗世界上就是一块糊进页面的浅斑。
/// 所以每个颜色都过一遍 <see cref="StyleWorldTokens.Reanchor"/>：色相与彩度原封不动，
/// 明度重钉成「离当前地面多远」，深档再把这个差值翻向更亮的一测（暗面上的「凸出」只有提亮做得到）。
/// 于是这里留下的十六进制一律只是**设计稿种子**，不是最终画出来的颜色。
///
/// 地面本身（画布底、墨色、暖光）不在这里：那是世界的，drawable 每帧直接找 <see cref="StyleWorldTokens"/> 要。
/// </summary>
public static class GraphPalette
{
    // ==================== 设计稿种子（只喂给 Reanchor，不直接画） ====================

    private static readonly Color DesignPerson = Color.FromArgb("#E8B87A");
    private static readonly Color DesignTag = Color.FromArgb("#A5D6A7");
    private static readonly Color DesignPlace = Color.FromArgb("#AFCFEA");
    private static readonly Color DesignThing = Color.FromArgb("#EFB0AE");
    private static readonly Color DesignEmotion = Color.FromArgb("#F2C4D6");
    private static readonly Color DesignImageFrame = Color.FromArgb("#D9C2A6");
    private static readonly Color DesignTextCard = Color.FromArgb("#F3ECE2");
    private static readonly Color DesignWord = Color.FromArgb("#F5D8B8");

    /// <summary>「记得的事」画成小卡片：它靠彩度从页面里凸出来，不是靠明度。</summary>
    private static readonly Color DesignMemoryCard = Color.FromArgb("#FFF2DC");

    /// <summary>模型整理出来的点：挂在疙瘩右上角的一小粒蓝。</summary>
    private static readonly Color DesignAiMark = Color.FromArgb("#7A9EC4");

    private static readonly Color DesignEquivalent = Color.FromArgb("#B4794A");
    private static readonly Color DesignAssociated = Color.FromArgb("#C2A17C");
    private static readonly Color DesignBelongsTo = Color.FromArgb("#93A97F");
    private static readonly Color DesignCausal = Color.FromArgb("#A8655C");
    private static readonly Color DesignTemporal = Color.FromArgb("#7E9CB0");
    private static readonly Color DesignSpatial = Color.FromArgb("#7FA488");
    private static readonly Color DesignEmotional = Color.FromArgb("#C37F98");
    private static readonly Color DesignUnknown = Color.FromArgb("#9C9086");

    /// <summary>锚点：画布涂的是页面底，所以整套颜色都按它重钉（引擎还没下发时用奶油纪元那张纸）。</summary>
    private static readonly Color DesignGround = Color.FromArgb("#FBF3E8");

    // ==================== 锚到当前世界的一份 ====================

    /// <summary>
    /// 一份「已经钉在当前地面上」的颜色。
    /// 画布每帧要取上百次，所以只在**地面真的换了**时重算一份（切世界 / 系统翻深浅才会换）。
    /// </summary>
    private sealed class Bands
    {
        public required Color Ground;
        public required Color Person;
        public required Color Tag;
        public required Color Place;
        public required Color Thing;
        public required Color Emotion;
        public required Color ImageFrame;
        public required Color TextCard;
        public required Color Word;
        public required Color MemoryCard;
        public required Color AiMark;
        public required Color Equivalent;
        public required Color Associated;
        public required Color BelongsTo;
        public required Color Causal;
        public required Color Temporal;
        public required Color Spatial;
        public required Color Emotional;
        public required Color Unknown;
    }

    private static Bands _bands = Build(DesignGround);

    private static Bands Build(Color ground)
    {
        Color Re(Color design) => StyleWorldTokens.Reanchor(design, ground);
        _emotionCache.Clear();        // 情绪点名 → 颜色也锚在这份地上，换世界必须重来
        return new Bands
        {
            Ground = ground,
            Person = Re(DesignPerson),
            Tag = Re(DesignTag),
            Place = Re(DesignPlace),
            Thing = Re(DesignThing),
            Emotion = Re(DesignEmotion),
            ImageFrame = Re(DesignImageFrame),
            TextCard = Re(DesignTextCard),
            Word = Re(DesignWord),
            MemoryCard = Re(DesignMemoryCard),
            AiMark = Re(DesignAiMark),
            Equivalent = Re(DesignEquivalent),
            Associated = Re(DesignAssociated),
            BelongsTo = Re(DesignBelongsTo),
            Causal = Re(DesignCausal),
            Temporal = Re(DesignTemporal),
            Spatial = Re(DesignSpatial),
            Emotional = Re(DesignEmotional),
            Unknown = Re(DesignUnknown)
        };
    }

    /// <summary>地面换了才重算这一份：每帧都 Build 会把 HSL 换算做上百次。</summary>
    private static Bands Current()
    {
        var ground = StyleWorldTokens.Pick("PageBg", DesignGround);
        if (!Same(_bands.Ground, ground)) _bands = Build(ground);
        return _bands;
    }

    private static bool Same(Color a, Color b) =>
        a.Red == b.Red && a.Green == b.Green && a.Blue == b.Blue && a.Alpha == b.Alpha;

    // ==================== 节点 ====================

    /// <summary>一个人：暖琥珀。</summary>
    public static Color Person => Current().Person;
    /// <summary>一个标签：绿。</summary>
    public static Color Tag => Current().Tag;
    /// <summary>一个地点：蓝。</summary>
    public static Color Place => Current().Place;
    /// <summary>一个东西：偏红。</summary>
    public static Color Thing => Current().Thing;
    /// <summary>一种情绪：粉。</summary>
    public static Color Emotion => Current().Emotion;
    /// <summary>照片的相框。</summary>
    public static Color ImageFrame => Current().ImageFrame;
    /// <summary>一句原文的卡片。</summary>
    public static Color TextCard => Current().TextCard;
    /// <summary>一个自己长出来的词。</summary>
    public static Color Word => Current().Word;
    /// <summary>「记得的事」那张卡片的面。</summary>
    public static Color MemoryCard => Current().MemoryCard;
    /// <summary>模型整理出来的那一小粒蓝。</summary>
    public static Color AiMark => Current().AiMark;

    public static Color Node(NodeKind kind) => kind switch
    {
        NodeKind.Person => Person,
        NodeKind.Memory => MemoryCard,
        NodeKind.Tag => Tag,
        NodeKind.Image => ImageFrame,
        NodeKind.Text => TextCard,
        NodeKind.Place => Place,
        NodeKind.Thing => Thing,
        NodeKind.Emotion => Emotion,
        _ => Word
    };

    // ==================== 联系 ====================

    public static Color Equivalent => Current().Equivalent;
    public static Color Associated => Current().Associated;
    public static Color BelongsTo => Current().BelongsTo;
    public static Color Causal => Current().Causal;
    public static Color Temporal => Current().Temporal;
    public static Color Spatial => Current().Spatial;
    public static Color Emotional => Current().Emotional;
    public static Color Unknown => Current().Unknown;

    public static Color Edge(RelationKind kind) => kind switch
    {
        RelationKind.Equivalent => Equivalent,
        RelationKind.Associated => Associated,
        RelationKind.BelongsTo => BelongsTo,
        RelationKind.Causal => Causal,
        RelationKind.Temporal => Temporal,
        RelationKind.Spatial => Spatial,
        RelationKind.Emotional => Emotional,
        _ => Unknown
    };

    /// <summary>
    /// 情绪点染成它自己的颜色；底账里没有这个名字时退回「连着情绪」那一档。
    /// 情绪名有几十个，逐个现算太浪费：按名字缓存，换世界时整份丢掉。
    /// </summary>
    private static readonly Dictionary<string, Color> _emotionCache = new(StringComparer.Ordinal);

    public static Color EmotionTint(string name)
    {
        var bands = Current();       // 先确认锚的是哪块地（换世界时已经清了缓存）
        if (_emotionCache.TryGetValue(name, out var hit)) return hit;
        Color made;
        try { made = StyleWorldTokens.Reanchor(Color.FromArgb(EmotionWords.ColorOf(name)), bands.Ground); }
        catch { made = bands.Emotional; }
        _emotionCache[name] = made;
        return made;
    }

    // ==================== 浓淡（画布与图例同一个数） ====================

    /// <summary>枝条的浓淡上下限：live=1 时最浓，久不想起的枝条退到下限（不是消失）。</summary>
    public const float BranchAlphaFresh = 0.92f;
    public const float BranchAlphaFading = 0.30f;

    /// <summary>跨片的细藤本来就比枝含蓄：等价最实，其余一律淡一档。</summary>
    public const float VineAlphaDesign = 0.34f;
    public const float EquivalentVineAlphaDesign = 0.60f;

    /// <summary>「浅景深」里被让位的家伙们退到多远：只降浓淡与饱和，不藏。</summary>
    public const float OutOfFocusAlphaDesign = 0.30f;
    public const float OutOfFocusBlend = 0.55f;

    /// <summary>选中点的那圈光晕叠几层（ICanvas 没有径向渐变，一层层叠出来各家才一致）。</summary>
    public const int GlowLayers = 3;

    /// <summary>
    /// 淡的那一头在暗世界上要抬一档。
    /// 这些数是浅纸上量的「淡到哪儿就读不出这是一根枝了」：浅地上面还有整段向黑的路可退，
    /// 暗地上往下的余量只有那么一丁点，同样三成浓的细线直接沉进页面 —— 用户会以为网断了。
    /// 只抬淡端、浓端不动，「藤 &lt; 枝 &lt; 新枝」这个次序一点没变。
    /// </summary>
    private static float FadeEnd(float k) =>
        StyleWorldTokens.Dark ? (float)Math.Min(1.0, k + (1.0 - k) * 0.30) : k;

    /// <summary>枝条浓淡 = 这两档之间按 live 插值。</summary>
    public static float BranchAlpha(double live)
    {
        var fading = FadeEnd(BranchAlphaFading);
        return (float)Math.Clamp(fading + (BranchAlphaFresh - fading) * Math.Clamp(live, 0.0, 1.0),
            fading, BranchAlphaFresh);
    }

    public static float VineAlpha => FadeEnd(VineAlphaDesign);
    public static float EquivalentVineAlpha => FadeEnd(EquivalentVineAlphaDesign);
    public static float OutOfFocusAlpha => FadeEnd(OutOfFocusAlphaDesign);

    /// <summary>
    /// 光晕里层这一档的浓淡：往外一层层按层数递减。
    /// 浅档的 0.13 是照着「几层叠在同一处才 ≈0.24」标定的：奶油纸本身亮，暖色压上去一点点就够。
    /// 暗地上同样的三层几乎叠不出东西 —— 底色已经把亮度用光了，所以这一档深档单独给一个更大的数。
    /// </summary>
    public static float GlowInnerAlpha => StyleWorldTokens.Dark ? 0.22f : 0.13f;

    // 下面几个是**给图例涂色**用的：画布画多淡，图例就涂多淡。
    // 枝条取 live≈0.5 那一档，才是平时一眼扫过去看到的浓淡。

    public static Color BranchSwatch => Associated.WithAlpha(0.62f);
    public static Color VineSwatch => Associated.WithAlpha(VineAlpha);
    public static Color EquivalentSwatch => Equivalent.WithAlpha(EquivalentVineAlpha);
    public static Color FadingSwatch => Associated.WithAlpha(FadeEnd(BranchAlphaFading));

    /// <summary>被让到景深外的东西有多淡。画布还会顺手把它往地面里拉一点，图例给得出浓淡这一半。</summary>
    public static Color OutOfFocusSwatch => Associated.WithAlpha(OutOfFocusAlpha);

    /// <summary>
    /// 正在伸的梢头上那粒高光。
    /// 这一颗**不走 Reanchor**：它的设计值比纸面还亮（L=1），而 Reanchor 把「比设计面亮」
    /// 翻到深档时是往更暗的一侧挪的 —— 梢头那点光会直接变成一个洞。
    /// 高光就是「这个世界里最亮的那一档」，所以取 ShadowLight（世界给的最浅的一份），
    /// 暗世界上再往白提一点：暗地的 ShadowLight 只比地面浮一线，压不住「力气正往这儿走」。
    /// </summary>
    public static Color TipSpark
    {
        get
        {
            var lift = StyleWorldTokens.Pick("ShadowLight", Color.FromArgb("#FFFFFF"));
            return StyleWorldTokens.Dark ? Blend(lift, Color.FromArgb("#FFFFFF"), 0.82) : lift;
        }
    }

    // ==================== 工具 ====================

    /// <summary>
    /// 两个颜色之间插一步。调用方拿它把「退到景深外」的东西往**当前地面**方向拉
    /// （地面是画布底，由 drawable 每帧从世界取，不在这里写死）。
    /// 不用现成的插值方法是因为各版本 Graphics 的 API 不一样，而这件事就三步算术。
    /// </summary>
    public static Color Blend(Color from, Color to, double k)
    {
        var t = (float)Math.Clamp(k, 0, 1);
        return new Color(
            from.Red + (to.Red - from.Red) * t,
            from.Green + (to.Green - from.Green) * t,
            from.Blue + (to.Blue - from.Blue) * t,
            from.Alpha + (to.Alpha - from.Alpha) * t);
    }
}
