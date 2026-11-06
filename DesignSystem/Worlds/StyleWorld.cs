using Microsoft.Maui.Controls.Shapes;

namespace WarmAsBefore.DesignSystem.Worlds;

/// <summary>
/// 五个风格世界。材质（几何、描边、填充、阴影、字号台阶）由世界决定，
/// 颜色（色相）由色板决定 —— 两条轴正交，所以 5 世界 × 明/暗 × 5 色板
/// 不需要手写 50 套字典：色板只提供色相与彩度种子，明度台阶由这里算出来。
/// </summary>
public enum StyleWorld
{
    /// <summary>
    /// 档案室（融合唯一世界，2026-11-06 收编五世界而成）：
    /// 终末地 HUD 的排版骨架（近直角、细描边、收紧的字阶、干脆的动效）
    /// + 玻璃透材质（GlassTier=glass，面透出背景）
    /// + 拟态软阴影（影子发到真实档，凹浮分明）
    /// + 砂纸/纸本的大气层（颗粒、暖光、暗角，见 2.7）—— 温馨的档案室氛围。
    /// 其余四个旧枚举值保留仅为兼容旧存档与旧引用，材质全部走同一条融合配方（见 Recipe.Of）。
    /// </summary>
    Soft,

    /// <summary>已收编进档案室（原「柔和极简」）。</summary>
    Minimal,

    /// <summary>已收编进档案室（原「柔和渐变」）。</summary>
    Gradient,

    /// <summary>已收编进档案室（原「液态玻璃」——玻璃透材质并入融合配方）。</summary>
    Liquid,

    /// <summary>已收编进档案室（原「终末地 HUD」——排版骨架即融合世界的骨架）。</summary>
    Endfield
}

/// <summary>
/// 世界令牌的计算与下发。
///
/// 为什么全部写进 Application.Current.Resources 的**顶层字典**（而不是替换 MergedDictionaries）：
/// 只有顶层 indexer setter 会触发 ValuesChanged，已注册的 {DynamicResource} 才会重新解析
/// —— 这是 ThemeManager.ApplyPalette 那段历史注释里踩过的坑，这里沿用同一结论。
///
/// 为什么连 Cream/Beige/OffWhite 这些「原始色」也一并覆盖：
/// 页面里约有 200 处内联引用的是原始色而不是语义色，深色模式下它们会留在浅色上，
/// 把界面劈成两半。世界把原始色映射到同一条台阶上，旧引用就自动跟着走。
///
/// 对比度不是调出来的，是**算出来的**：墨色用 Lock() 逼到目标对比比以上，
/// 所以「说明行和正文一个重量」那类 1.05:1 的台阶不可能再被写回来。
/// </summary>
public static class StyleWorldTokens
{
    // 世界已收敛为唯一档「档案室」（终末地骨架 + 玻璃 + 拟态 + 纸本质感）。
    // 枚举与 setter 保留：旧存档的世界键还能进来（只是一律落到同一档），避免 114 处引用.chain 改。
    public static StyleWorld World { get; private set; } = StyleWorld.Endfield;
    public static bool Dark { get; private set; }

    // ── 视口尺度 ────────────────────────────────────────────────────────
    // 设计档就是开窗口那一档（App.xaml.cs 里 1280×800 逻辑）。在此之前，窗口被拉大或缩小时
    // 唯一会发生的事是「卡片被 MaximumWidth 夹住」，字号与行高纹丝不动 ——
    // 1920 的窗里那张卡缩成一枚邮票，1024 的窗里它又顶到边上。乘子让字号、内距、圆角、
    // 行高按同一个系数一起走，比例关系才不会在别的尺寸上散架。
    //
    // 取宽高两轴的较小值：只按宽度算的话，把窗口竖着拉（分屏）字号会跟着涨，
    // 而那个方向上真正发生的事是内容被压扁。
    public const double DesignWidth = 1280;
    public const double DesignHeight = 800;

    /// <summary>尺度乘子，1.0 = 设计档。夹在 0.86–1.16：再小字就读不出了，再大一屏放不下三行。</summary>
    public static double Scale { get; private set; } = 1.0;

    /// <summary>最近一次上报的窗口逻辑宽度（内容列宽度要跟着它走，见 CardWidth）。</summary>
    public static double ViewportWidth { get; private set; } = DesignWidth;

    /// <summary>窗口逻辑尺寸变了就重算尺度档。返回 false 表示没跨档，不必重发整套令牌。</summary>
    public static bool SetViewport(double width, double height)
    {
        if (width <= 0 || height <= 0) return false;
        ViewportWidth = width;
        var k = Math.Clamp(Math.Min(width / DesignWidth, height / DesignHeight), 0.86, 1.16);
        // 量化到 0.02 一档：连续值会让拖窗口的每一像素都重发一遍全套令牌（整屏重排），
        // 而 0.01 档的字号差肉眼根本读不出来，白掉一次布局。
        var q = Math.Round(k / 0.02) * 0.02;
        if (Math.Abs(q - Scale) < 0.0001) return false;
        Scale = q;
        return true;
    }

    /// <summary>当前窗口下内容列的建议宽度（逻辑单位）：跟窗走，但封顶，行长过了 900 就读不回来了。
    /// 下限 320 是手机的宽度那一档 —— 窗口没上报过尺寸时（Android 上 Resized 不一定触发），
    /// 宁可让卡片按内容收缩，也不要顶出一个 794 的固定宽。</summary>
    public static double CardWidth(double windowWidth) =>
        Math.Clamp(Math.Round(windowWidth * 0.62), 320, 900);

    // ── 宽度令牌的小型重发 ─────────────────────────────────────────────
    // 内容列宽只跟窗口宽度走，与色板/字号无关。以前它们混在整套 Publish 里，
    // 而 ApplyViewport 只在「尺度跨 0.02 一级」才整套重发 —— 结果就是拖动窗口时
    // 内容列宽纹丝不动（用户实测：设置首页「老是固定大小」）。这三个标量单独重发，
    // 让 DynamicResource 的消费方（设置列、桌宠设置等内容列）逐像素跟手。

    /// <summary>窗口宽度变了（但尺度没跨档）时只重发这三个宽度令牌，不惊动整套调色板。</summary>
    public static void RepublishWidth()
    {
        var app = Application.Current;
        if (app is null) return;
        PublishWidth(app.Resources);
    }

    private static void PublishWidth(ResourceDictionary res)
    {
        res["WCardWidth"] = CardWidth(ViewportWidth);
        // 双栏编辑/桌面伴侣一类的页面要用比正文列宽一档的内容列（桌宠设置 440 预览 + 设置列）。
        // 上限放到 1600（0.9×窗宽）：1920 全屏 1240 封顶会把「内容列跟窗走」读成又一块固定邮票。
        res["WCardWidthWide"] = Math.Clamp(Math.Round(ViewportWidth * 0.9), 320, 1600);
        // 导航条宽度：窄到一列都放不下时（手机）交回 -1 = 不请求宽度，让那一列自己塌成一行。
        // 这个判断只能在这里做：XAML 里 OnPlatform 拿不到窗口尺寸，而尺度档知道。
        res["WRailWidth"] = CardWidth(ViewportWidth) < 480 ? -1d : Math.Round(262 * Scale);
    }

    public static string Display(StyleWorld w) => "档案室";

    /// <summary>
    /// 旧显示名 ↔ 世界。世界已收敛，任何旧名都落到档案室（包括旧存档里存的「液态玻璃」）——
    /// 函数保留只为兼容旧调用点，不许再拿它做「换风格」。
    /// </summary>
    public static StyleWorld FromDisplay(string? text) => StyleWorld.Endfield;

    /// <summary>旧英文键 ↔ 世界：同上，全部落到档案室。</summary>
    public static StyleWorld FromName(string? name) => StyleWorld.Endfield;

    /// <summary>存进设置的英文键名。新世界统一写 "endfield"（档案室的键名沿用终末地的历史键）。</summary>
    public static string NameOf(StyleWorld w) => "endfield";

    /// <summary>
    /// 旧玻璃三开关的等价档位。世界已收敛为档案室：玻璃全开（面透、发光边、磨砂可调）。
    /// 函数保留，因为这三个布尔仍是持久化字段，设置页还拿它们判断控件摆不摆出来。
    /// </summary>
    public static (bool Liquid, bool Glass, bool Frost) GlassFlags(StyleWorld w) => (true, true, true);

    // ── 每个世界的材质配方 ──────────────────────────────────────────────
    // Depth = 页面底 / 卡面 / 井 三档之间的**明度台阶倍数**。这是「层次」的唯一来源：
    // 描边和影子只是将同一台阶换个说法，台阶本身为 0 时怎么加边都读不出高低。
    private readonly record struct Recipe(
        double Depth, double RadiusCard, double RadiusWell, double RadiusDialog, double RadiusButton,
        double EdgeWidth, double PadCard, double PadWell,
        double SizeDisplay, double SizeTitle, double SizeSubhead, double SizeBody, double SizeSmall, double SizeCaption,
        bool Seam, bool Shadow, bool FillGradient, string GlassTier)
    {
        public static Recipe Of(StyleWorld w) => new(
            // 档案室（融合唯一配方）——各源的贡献：
            //   Depth 0.62          终末地：台阶定性但不过「雾里看块」
            //   圆角 8/6/8/6        终末地近直角放宽到 6–8（档案柜的圆铁角，比 2–4 多一点温度）
            //   EdgeWidth 1.2       终末地细描边（玻璃面的发光边由 GlassTokens 接管时会盖过这条，不冲突）
            //   Pad 16/10           终末地的紧身内距（仪表盘的行距，不学杂志）
            //   字阶 30/20/15.5/14.5/13/11.5  终末地收紧的字阶（HUD 的信息密度）
            //   Seam true           轻质感的那道缝保留（纸本分层的根）
            //   Shadow true         拟态：软阴影发到真实档（浮卡有影、按下去有沉）
            //   FillGradient true   玻璃面：面是一档浅渐变（Face() 的受光渐变主要分支）
            //   GlassTier "glass"   GlassTokens 接管卡面：透出背景、发光边、可磨砂
            Depth: 0.62, RadiusCard: 8, RadiusWell: 6, RadiusDialog: 8, RadiusButton: 6,
            EdgeWidth: 1.2, PadCard: 16, PadWell: 10,
            SizeDisplay: 30, SizeTitle: 20, SizeSubhead: 15.5, SizeBody: 14.5, SizeSmall: 13, SizeCaption: 11.5,
            Seam: true, Shadow: true, FillGradient: true, GlassTier: "glass");
    }

    /// <summary>
    /// 把配方里的尺寸（字号、内距、圆角）整体乘一档视口尺度。
    /// 描边宽度**不乘**：发丝线在哪个尺寸下都还是一根发丝，乘到 1.16px 只会开始发灰。
    /// 台阶、缝、影子、材质档也不乘 —— 它们是「这是什么世界」，不是「这有多大」。
    /// </summary>
    private static Recipe Scaled(Recipe r, double k) => k == 1.0 ? r : r with
    {
        RadiusCard = r.RadiusCard * k,
        RadiusWell = r.RadiusWell * k,
        RadiusDialog = r.RadiusDialog * k,
        RadiusButton = r.RadiusButton * k,
        PadCard = r.PadCard * k,
        PadWell = r.PadWell * k,
        SizeDisplay = r.SizeDisplay * k,
        SizeTitle = r.SizeTitle * k,
        SizeSubhead = r.SizeSubhead * k,
        SizeBody = r.SizeBody * k,
        SizeSmall = r.SizeSmall * k,
        SizeCaption = r.SizeCaption * k
    };

    /// <summary>GlassTokens 该走哪一档材质（由世界决定，用户不再直接选档位）。</summary>
    public static string GlassTier => Recipe.Of(World).GlassTier;

    /// <summary>
    /// 世界对玻璃调制量的取向：设置里那三个开关已经退役成「风格」一个下拉，
    /// 透明度/磨砂/高度仍是用户可调的乘子，但上限由世界给 ——
    /// 轻质感不该被调成全透，极简档压根不该有磨砂。
    /// </summary>
    public static (double Translucency, double Frost, double Height) GlassBias(StyleWorld w) => (0.85, 0.16, 0.80);

    // ── 对外取色口（自绘画布、页面底）──────────────────────────────────

    private static Color? _surface, _well;

    /// <summary>读当前世界的颜色令牌；读不到时用 fallback。自绘画布每帧重画，直接跟世界走。</summary>
    public static Color Pick(string key, Color fallback) =>
        Application.Current?.Resources.TryGetValue(key, out var v) == true && v is Color c ? c : fallback;

    /// <summary>
    /// 把设计稿上的颜色重新钉到当前地面上：色相与彩度留着（那才叫「这一类东西是什么颜色」），
    /// 明度改写成「离地面多远」，并在深档把这个差值翻向更亮的一侧——暗面上的「凸出」只能靠提亮。
    /// 自绘画布用它，免得给四套色板 × 明暗各抄一份颜色表；手抄的那份一定会漂。
    /// </summary>
    public static Color Reanchor(Color design, Color ground)
    {
        var hd = RgbToHsl(design);
        var hg = RgbToHsl(ground);
        var step = hd.L - 0.995;                       // 设计稿都在白卡上标定（面 L≈0.995）
        var l = hg.L < 0.5 ? hg.L - step * 0.75 : hg.L + step;
        return Tint(hd.H, Chroma(design) * (hg.L < 0.5 ? 1.7 : 1.0), Math.Clamp(l, 0.05, 0.99));
    }

    /// <summary>
    /// 页面底：档案室永远给地面一层斜向浅渐变 —— 半透明表面背后若是一整块纯色，
    /// 玻璃就永远只是一块浅色板，这是「玻璃看不出是玻璃」的根因。
    /// 明度只在地面附近几档内浮动，不去抢卡面与墨色的对比。
    /// </summary>
    public static Brush GroundBrush(Color page)
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0.08, 0), EndPoint = new Point(0.92, 1) };
        brush.GradientStops.Add(new GradientStop { Color = Lift(page, Dark ? 0.075f : 0.045f), Offset = 0f });
        brush.GradientStops.Add(new GradientStop { Color = page, Offset = 0.45f });
        brush.GradientStops.Add(new GradientStop { Color = Mix(page, _well ?? Sink(page, 0.06f), 0.24), Offset = 1f });
        return brush;
    }

    /// <summary>
    /// 玻璃档把「卡面怎么做」整个交给 GlassTokens：通透的面、发光边、随高度散开的影子都由它算。
    /// 只换渐变档与液态档 —— 轻奢与极简的面是实心纸，它们的层次在台阶与缝上；
    /// 把每个世界的面都换成玻璃，正是过去「三个开关互相牵制、最后哪个都不像」的根因。
    /// 必须在 GlassTokens.Publish 之后调用：这里只是把它们算好的东西接到世界的名字上。
    ///
    /// 为什么要接名字：页面用的是同一个 CardSolid 样式，样式里的面是 {DynamicResource WSurfaceBrush}。
    /// 世界不改这个键，「液态玻璃」就只是把圆角调大了一点，用户看不出材质换了。
    /// </summary>
    public static void ApplyFace()
    {
        var res = Application.Current?.Resources;
        if (res is null || GlassTier is not ("glass" or "liquid")) return;
        if (res.TryGetValue("GlassCardBg", out var bg) == true && bg is Brush face) res["WSurfaceBrush"] = face;
        if (res.TryGetValue("GlassCardStroke", out var stroke) == true && stroke is Brush edge)
        {
            res["WEdgeBrush"] = edge;
            if (res.TryGetValue("GlassCardStrokeThickness", out var w) == true && w is double width)
                res["WEdgeWidth"] = width;
        }
        if (res.TryGetValue("GlassBarBg", out var bar) == true && bar is Brush barBrush) res["WBarBrush"] = barBrush;
    }

    /// <summary>
    /// 世界的节奏与按压行程：档案室取终末地的仪表按键 —— 干脆、行程浅、余韵短。
    /// 行程不超过 2px —— 再深就不是「材料被压下去」而是「按钮在跳」。
    /// </summary>
    public static (double Scale, double Press) MotionOf(StyleWorld w) => (0.9, 2.0);

    // ── 下发 ────────────────────────────────────────────────────────────

    /// <summary>
    /// 色板种子。C* 是**彩度差**（该色 max-min 的通道跨度），不是 HSL 的 S：
    /// 近白的高明度色在 HSL 里 S 能冲到 0.76 而肉眼几乎看不出色偏，
    /// 拿它当强度用会把四个色板算成同一个颜色。彩度差才线性对应「看得出来 tint」。
    /// </summary>
    private readonly record struct Seed(
        double GroundH, double GroundC, double AccentH, double AccentC,
        double GreenH, double GreenC, double RedH, double RedC, double BlueH, double BlueC);

    private static Seed _seed;
    private static string _seedTheme = "";

    /// <summary>
    /// themeKey 决定种子要不要重取：只有色板真的重新落盘时才重读。
    /// 世界会把算好的颜色写回同一批资源键，若下一轮从资源读就是读自己的输出，
    /// 每轮再乘一次彩度系数，连切几次世界颜色就漂向灰了。
    /// </summary>
    public static void Publish(StyleWorld world, bool dark, string themeKey)
    {
        var app = Application.Current;
        if (app is null) return;
        World = world;
        Dark = dark;
        var res = app.Resources;
        var r = Scaled(Recipe.Of(world), Scale);

        if (_seedTheme != themeKey)
        {
            _seed = ReadSeed(res);
            _seedTheme = themeKey;
        }
        var hue = _seed.GroundH;
        // 深色档染色要更重：同量彩度差压在暗底上几乎读不出来（明度低了 tint 的相对可见度掉得很快）。
        var tint = _seed.GroundC * (dark ? 1.7 : 1.0);

        // 三档地：页面 / 卡面 / 井。台阶宽度由 Depth 决定，方向由明暗决定。
        // 浅档的页面钉在 0.968：参考窗实测地面 #F3F3F3（L 0.953）、卡纯白。
        // 地面一旦沉到 0.90，白卡就变成「灰纸上贴一块糖」，整屏开始发脏 —— 这是「丑」的主因之一。
        // 卡面给到近纯白并把彩度压到最低（白卡带黄会读成旧纸），井单独沉一档。
        var d = r.Depth - 0.55;
        var page = Tint(hue, tint, dark ? 0.115 - 0.030 * d : 0.968 - 0.032 * d);
        var surface = Tint(hue, tint * 0.55, dark ? 0.175 + 0.020 * d : 0.995);
        var well = Tint(hue, tint * 1.3, dark ? 0.082 - 0.015 * d : 0.930 - 0.028 * d);
        _surface = surface;
        _well = well;

        // 墨阶：起点用页面色（已经带上这个世界的色偏），目标对比比一律由 Lock 锁死。
        var inkDisplay = Lock(SinkOrLift(page, dark, 0.86, 0.94), surface, dark ? 9.0 : 11.0);
        var inkBody = Lock(SinkOrLift(page, dark, 0.74, 0.88), surface, dark ? 7.6 : 8.4);
        var inkLabel = Lock(SinkOrLift(page, dark, 0.62, 0.80), surface, dark ? 6.4 : 7.0);
        var inkMuted = Lock(SinkOrLift(page, dark, 0.46, 0.66), surface, dark ? 5.0 : 5.4);
        var inkFaint = Lock(SinkOrLift(page, dark, 0.28, 0.44), surface, dark ? 3.1 : 3.4);

        // 边：浅色档靠「比卡面沉一档」，深色档靠「比卡面浮一档」——
        // 暗底上画一条更暗的线等于没画，这是深色模式最容易犯的错。
        // 台阶按「发丝线」标定（对照卡面约 1.1 / 1.3 / 1.7）：再重就变成描边盒子，
        // 卡与卡之间要靠面和影子分层次，不是靠轮廓线。
        var line = SinkOrLift(surface, dark, 0.055, 0.055);
        var seam = SinkOrLift(surface, dark, 0.11, 0.10);
        var edge = SinkOrLift(surface, dark, 0.21, 0.18);

        // 强调色：色相与彩度都跟着色板自己的 AccentWarm（地面只借色相，不抢强调的位置）。
        // 深档的明度钉在 0.37 而不是更高：这是「白字压在强调面上」能到 5:1 的上限位置——
        // 再亮一档，纯白也压不住，Lock 会饱和在 4.7。
        var accent = Tint(_seed.AccentH, _seed.AccentC * (dark ? 1.5 : 1.15), dark ? 0.37 : 0.80);
        // 强调面上的字：浅档的强调是淡奶油，字必须往黑沉；深档的强调是中调，字往白提。
        // 方向写反时 Lock 也会把对比补齐，结果就是「白底上的字被推到灰、看着像禁用」，所以方向要自己对。
        var accentInk = Lock(SinkOrLift(accent, dark, 0.74, 0.72), accent, 5.0);

        // 档案室的信号色：黄铜 / 琥珀（钉死色相 40°）。这间屋子里唯一的饱和物是那盏
        // 黄铜台灯与桌面的印章 —— 信号色跟着色板换五种，就不叫信号了（沿用旧版钉靛紫的逻辑）。
        // 端末地色板本来就是黄强调，因此五套色板在这里第一次指向同一个「档案室」性格。
        var isEndfield = string.Equals(themeKey, "endfield", StringComparison.OrdinalIgnoreCase);
        var signal = isEndfield
            ? Lock(Hsl(_seed.AccentH, 0.95, dark ? 0.62 : 0.40), surface, dark ? 5.0 : 4.8)
            : Lock(Hsl(38, 0.85, dark ? 0.60 : 0.42), surface, dark ? 5.0 : 4.8);

        PublishColors(res, page, surface, well, inkDisplay, inkBody, inkLabel, inkMuted, inkFaint,
            seam, edge, line, accent, accentInk, signal, dark);
        PublishMaterial(res, r, surface, well, page, seam, edge, accent, accentInk, dark);
        var (scale, press) = MotionOf(world);
        Theme.MotionTokens.Scale = scale;
        Theme.MotionTokens.Press = press;
    }

    private static void PublishColors(ResourceDictionary res,
        Color page, Color surface, Color well,
        Color inkDisplay, Color inkBody, Color inkLabel, Color inkMuted, Color inkFaint,
        Color seam, Color edge, Color line, Color accent, Color accentInk, Color signal, bool dark)
    {
        // 语义色（页面/卡面/井/输入）
        res["PageBg"] = page;
        res["SurfaceBg"] = surface;
        res["WellBg"] = well;
        res["InputBg"] = dark ? Sink(surface, 0.12) : Lift(page, 0.02);
        res["SecondaryBg"] = dark ? Lift(surface, 0.05) : Sink(page, 0.05);
        res["OverlayDim"] = new Color(0f, 0f, 0f, (float)(dark ? 0.62 : 0.42));

        // 主界面那层「暖纸半透明」：顶栏药丸、右侧面板、立绘框底。
        // 原先在 MainGamePage 里写死 #B8FDF8F0 / #D9FDF8F0 这一族奶油色，
        // 深色档下它们会亮在半屏的黑里。现在一律由当前档的 page/surface 派生，透明度只表达「叠了几层」。
        Color Alpha(Color c, byte a) => new(c.Red, c.Green, c.Blue, a / 255f);
        res["TopBarChipBg"] = Alpha(page, 0xB8);
        res["PanelGlass"] = Alpha(page, 0xD9);
        res["PanelGlassSoft"] = Alpha(page, 0xCC);
        res["PanelGlassStrong"] = Alpha(page, 0xB3);
        res["PanelGlassStage"] = Alpha(surface, 0xD9);
        res["ScrimStrong"] = new Color(0f, 0f, 0f, 0.80f);
        res["OnScrimInk"] = new Color(1f, 1f, 1f, 0.60f);
        res["OnScrimInkSoft"] = new Color(1f, 1f, 1f, 0.40f);
        res["OnScrimChip"] = new Color(1f, 1f, 1f, 0.20f);
        res["DangerBanner"] = new Color(0.70f, 0.15f, 0.12f, 0.90f);

        // 墨阶（含旧命名：WarmDark/WarmBody/WarmMuted 在页面里被当文字色用了上百处）
        res["WarmDark"] = inkDisplay;
        res["SurfaceFg"] = inkBody;
        res["WarmBody"] = inkLabel;
        res["MutedFg"] = inkMuted;
        res["WarmMuted"] = inkMuted;
        res["WarmFaint"] = inkFaint;
        res["PrimaryFg"] = inkBody;

        // 边界三档：line < seam < edge（原来只有一档，平档与凹档看不出高低）
        res["BorderLine"] = line;
        res["EdgeStrong"] = edge;
        res["ShadowDim"] = seam;
        res["ShadowDeep"] = dark ? Sink(surface, 0.55) : Sink(surface, 0.24);
        res["ShadowLight"] = dark ? Lift(surface, 0.10) : Lift(surface, 0.30);

        // 强调与功能色
        res["AccentWarm"] = accent;
        res["AccentWarmFg"] = accentInk;
        res["PrimaryAction"] = accent;
        res["PrimaryHover"] = SinkOrLift(accent, dark, 0.12, 0.12);

        // 信号族：块（导航选中行左缘的胶囊）、字（当前项/链接）、字压在其上的墨、极浅的一颗色偏（悬停底）。
        // 字与浅底跟着 signal 的实际色相走：四套普通色板仍是靛紫（与原值几乎一致），
        // 终末地则是黄 —— 否则黄块旁边立着一行靛紫字，像两套主题叠在一起。
        var signalHue = RgbToHsl(signal).H;
        var signalChroma = Chroma(signal);
        res["Signal"] = signal;
        res["SignalFg"] = Lock(Hsl(signalHue, Math.Min(1, signalChroma + 0.10), dark ? 0.76 : 0.36),
            surface, dark ? 5.0 : 4.8);
        // 墨的方向必须手写：浅档的信号钉在 L 0.46（白字 5.2:1，黑字只有 2.4:1），
        // 深档提到 L 0.64 才压得住暗地（此时反过来，黑字 6.5:1）。
        // Lock 只会兜底补对比，不会替你判断方向 —— 方向写错就是一团脏灰。
        res["SignalInk"] = Lock(dark ? Colors.Black : Colors.White, signal, 4.6);
        res["SignalTint"] = Tint(signalHue, Math.Min(0.16, signalChroma * 0.35 + 0.06), dark ? 0.215 : 0.958);
        // 信号钮的三级台阶：静止 → 悬停 → 按下。方向跟着地面走（浅档往黑沉、深档往白提），
        // 因为 SignalInk 在两地是反的：浅档白字配更深的蓝、深档黑字配更亮的蓝，对比只会越按越高。
        res["SignalHover"] = SinkOrLift(signal, dark, 0.16, 0.16);
        res["SignalPressed"] = SinkOrLift(signal, dark, 0.30, 0.30);
        res["WSignalBrush"] = new SolidColorBrush(signal);
        res["DangerInk"] = Lock(Hsl(_seed.RedH, 0.72, dark ? 0.62 : 0.34), surface, dark ? 5.2 : 4.8);
        res["GreenSoft"] = Status(_seed.GreenH, _seed.GreenC, surface, dark);
        res["RedSoft"] = Status(_seed.RedH, _seed.RedC, surface, dark);
        res["BlueSoft"] = Status(_seed.BlueH, _seed.BlueC, surface, dark);

        // 导航/列表的选中行：比地面再沉一档就够（实测参考窗：地 #F3F3F3、选中块 #E3E3E3）。
        // 立起来靠那颗胶囊和加粗的字，不靠整块彩色 —— 一整条饱和色块更响，但也更廉价。
        res["WNavSelBrush"] = new SolidColorBrush(SinkOrLift(page, dark, 0.06, 0.12));
        // 卡内发丝线：极简档的卡片没有边框，但卡内的行必须有分界 ——
        // 否则一屏十几行设置项会连成一片文字墙，读不出「这是一张卡里的几条」。
        // 线用 BoxView 画，它的 Color 只吃 Color，所以这里同时下发画刷版和纯色版。
        res["WSep"] = line;
        res["WSepBrush"] = new SolidColorBrush(line);
        res["WSepWidth"] = dark ? 0.8 : 1.0;

        // 状态淡底（徽章、提示条的填充色）：色板里原来是四个写死的奶油淡彩，
        // 换到深色档就变成挂在暗面上的几片浅纸。现在跟地面走：浅档提白、深档沉下去。
        Color Tinted(double hue, double chroma) =>
            Tint(hue, chroma * (dark ? 1.3 : 1.0) + 0.05, dark ? 0.235 : 0.945);
        res["DangerTint"] = Tinted(_seed.RedH, _seed.RedC);
        res["SuccessTint"] = Tinted(_seed.GreenH, _seed.GreenC);
        res["InfoTint"] = Tinted(_seed.BlueH, _seed.BlueC);

        // 气泡与页签
        res["BubbleMine"] = SinkOrLift(accent, dark, 0.04, 0.06);
        res["BubbleTheirs"] = surface;
        res["TabActive"] = dark ? Lift(surface, 0.16) : Sink(accent, 0.18);
        res["TabInactive"] = page;

        // 原始色别名：页面里那 200 处内联引用走的是这些名字，
        // 不把它们的明度钉到当前地上，深色模式会有一半界面留在浅色里。
        res["WarmWhite"] = Lift(surface, 0.30);
        res["Cream50"] = Lift(surface, 0.20);
        res["Cream100"] = Lift(surface, 0.12);
        res["Cream150"] = surface;
        res["Cream200"] = dark ? Sink(surface, 0.18) : Lift(page, 0.30);
        res["Cream250"] = dark ? Sink(surface, 0.26) : Sink(page, 0.08);
        res["Cream300"] = dark ? Sink(surface, 0.34) : Sink(page, 0.14);
        res["OffWhite50"] = Lift(surface, 0.26);
        res["OffWhite100"] = surface;
        res["OffWhite200"] = page;
        res["OffWhite300"] = well;
        res["Beige50"] = Lift(surface, 0.14);
        res["Beige100"] = dark ? Sink(surface, 0.10) : Lift(page, 0.14);
        res["Beige150"] = dark ? Sink(surface, 0.18) : Sink(page, 0.04);
        res["Beige200"] = dark ? Sink(surface, 0.26) : Sink(page, 0.10);
        res["Beige250"] = dark ? Sink(surface, 0.34) : Sink(page, 0.16);
        res["Beige300"] = dark ? Sink(surface, 0.42) : Sink(page, 0.22);
        res["BeigeAccent50"] = dark ? Lift(accent, 0.06) : Lift(accent, 0.30);
        res["BeigeAccent100"] = dark ? accent : Lift(accent, 0.10);
        res["BeigeAccent200"] = dark ? Sink(accent, 0.16) : Sink(accent, 0.06);
        res["BeigeAccent300"] = dark ? Sink(accent, 0.30) : Sink(accent, 0.16);

        // 世界签名层（2.7）· 页首印记：形态在 PublishMaterial 的 WMarkRadius/Size，
        // 颜色在这里——直接用 Signal（档案室的印记就是这间屋子的那颗信号弹，跟色板的黄/靛紫走同一规则）。
        res["WMarkColor"] = signal;
    }

    /// <summary>
    /// 材质令牌：几何、缝、影、内距、字号台阶。共享样式字典引用这些键，
    /// 世界一换，全站卡片/按钮/文字的「手感」一起换。
    /// </summary>
    private static void PublishMaterial(ResourceDictionary res, Recipe r,
        Color surface, Color well, Color page, Color seam, Color edge, Color accent, Color accentInk, bool dark)
    {
        // 面：档案室的面走玻璃主分支（FillGradient=true 且 GlassTier=glass 时 GlassTokens 接管；
        // 语义键未接管前这里先用一套受光渐变兜底：顶提 0.045，像纸对着窗光）。
        Brush Face(Color baseColor, bool topLight)
        {
            var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            b.GradientStops.Add(new GradientStop { Color = topLight ? Lift(baseColor, 0.045f) : Sink(baseColor, 0.03f), Offset = 0f });
            b.GradientStops.Add(new GradientStop { Color = baseColor, Offset = 0.62f });
            b.GradientStops.Add(new GradientStop { Color = topLight ? Sink(baseColor, 0.02f) : Lift(baseColor, 0.03f), Offset = 1f });
            return b;
        }

        res["WSurfaceBrush"] = Face(surface, !dark);
        res["WSurfaceRaised"] = Face(SinkOrLift(surface, dark, 0.06, 0.06), !dark);
        res["WWellBrush"] = Face(well, dark);
        res["WBarBrush"] = Face(SinkOrLift(page, dark, 0.03, 0.03), !dark);
        res["WAccentBrush"] = new SolidColorBrush(accent);
        res["WAccentInk"] = accentInk;

        // 缝：极简把描边宽度设成 0（配方里 EdgeWidth=0），颜色留着不影响。
        res["WEdgeBrush"] = new SolidColorBrush(r.Seam ? seam : edge);
        res["WEdgeStrongBrush"] = new SolidColorBrush(edge);
        res["WEdgeWidth"] = r.EdgeWidth;
        // 控件的边**不跟卡一起归零**。参考窗里卡确实没有边，但下拉框、输入框、幽灵按钮各有一圈发丝边：
        // 卡和地面对比够了，它的身份由「一块白浮在灰地上 + 位置」说清楚；控件和它压着的卡同色，
        // 「能点」这个信息只能由那条边成立 —— 去掉就退回用户报过的「按钮和背景合为一体」。
        // 取缝那一档（不是边）：实测参考控件边 ≈ #E3E3E3 压在 #FFFFFF 上，正好是 0.11 档。
        res["WControlEdge"] = seam;
        res["WControlEdgeBrush"] = new SolidColorBrush(seam);
        res["WControlEdgeWidth"] = r.EdgeWidth > 0 ? r.EdgeWidth : 1.0;
        // 眉栏下的那条线（方向契约里的「缝，不是影」）：比缝重一档，比边轻一档。
        res["WRule"] = SinkOrLift(surface, dark, 0.15, 0.13);
        res["WRuleBrush"] = new SolidColorBrush(SinkOrLift(surface, dark, 0.15, 0.13));
        res["WRuleWidth"] = dark ? 0.7 : 0.9;

        res["WRadiusCard"] = Shape(r.RadiusCard);
        // 浮档比平档大一圈：圆角本身就是「离读者更近」的线索，光靠影子说不服人。
        res["WRadiusRaised"] = Shape(r.RadiusCard + 4);
        res["WRadiusWell"] = Shape(r.RadiusWell);
        res["WRadiusDialog"] = Shape(r.RadiusDialog);
        res["WRadiusPill"] = Shape(9999);
        res["WRadiusButton"] = (int)Math.Round(r.RadiusButton);
        res["WPadCard"] = new Thickness(r.PadCard);
        res["WPadWell"] = new Thickness(r.PadWell, r.PadWell * 0.55, r.PadWell, r.PadWell * 0.55);
        // 行的纵向内距压到横向的 0.62 档（原来是 0.72）：行高 = 上下内距 + 一行字。
        // 0.72 那档实测把一行推到 51~52，而标签只有 15~17 号 —— 「一行字浮在一条带子中间」
        // 就是用户说的「大小不合理」里最直观的那一半。收到 0.62 后行落在 44 上下，
        // 和参考窗（水杉设置页实测行高 ≈ 40）同一档密度。
        res["WPadRow"] = new Thickness(r.PadCard, r.PadCard * 0.62, r.PadCard, r.PadCard * 0.62);
        res["WPadRowInset"] = new Thickness(r.PadCard, 0, 0, 0);
        // 发丝线要贴到卡边，而卡自己带内距 —— 用负的横向 Margin 把它抵消掉（Thickness 允许负值）。
        // 为什么不动卡的内距：行里不写内距、由卡统一管，改一处就全页对齐；线是唯一的例外，
        // 因为它跨的是「两行之间」，那不属于任何一行。
        res["WPadCardBleed"] = new Thickness(-r.PadCard, 0, -r.PadCard, 0);

        // 影子只下发「颜色」，不下发 Shadow 对象：样式里是内联的 <Shadow>，它的 Brush 才是动态的。
        // 把整个 Shadow 对象挂成 DynamicResource 会换掉对象身份 = 重建一次合成层，
        // 实测症状就是「改完设置回来，按钮位置还在、一个像素不画」（这条在 HEAD 的注释里记过，
        // 我在令牌化时把它违反了，所以按钮又消失了一次）。不要影子的世界把颜色发到全透明，对象不动。
        res["WShadowCardBrush"] = ShadowInk(dark, r.Shadow && !FlatShadows ? 0.30 : 0);
        res["WShadowDialogBrush"] = ShadowInk(dark, FlatShadows ? 0 : 0.34);

        res["WSizeDisplay"] = r.SizeDisplay;
        res["WSizeTitle"] = r.SizeTitle;
        res["WSizeSubhead"] = r.SizeSubhead;
        res["WSizeBody"] = r.SizeBody;
        res["WSizeSmall"] = r.SizeSmall;
        res["WSizeCaption"] = r.SizeCaption;
        // 名牌档：启动卡上那四个字唯一的正式尺寸。不在页面里自己乘一个数：
        // 四档世界的字阶不同（极简 28、玻璃 36），写死的字号只会在一档上成立。
        res["WSizeBrand"] = Math.Round(r.SizeDisplay * 2.15);
        res["WGapSection"] = r.PadCard + 8;

        // 尺寸类令牌里那三个「非字号」的：行高、标识块、主胶囊。之前它们只以字面量的形式
        // 散在样式和页面里（CardShapes 的 52、ButtonShapes 的 46/52、TitlePage 的 34），
        // 于是窗口变了谁都不动 —— 尺度系统要能真的管到它们，得先有名字。
        res["WRowHeight"] = Math.Round(r.PadCard * 0.62 * 2 + r.SizeBody * 1.45);
        res["WMarkSize"] = Math.Round(r.SizeTitle * 1.85);
        res["WPillHeight"] = Math.Round(r.SizeBody * 2.9);
        res["WPillRadius"] = (int)Math.Round(r.SizeBody * 2.9 / 2);
        PublishWidth(res);
        res["WGapItem"] = Math.Round(r.PadCard * 0.62);
        // 主行动带的气口：横向比行宽一档，纵向给到 1.55 档。抬头/页脚用 WPadCard，
        // 只有这一带需要「比列表松」，因为它是整屏唯一要被先看到的东西。
        res["WPadHero"] = new Thickness(r.PadCard * 1.45, r.PadCard * 1.55, r.PadCard * 1.45, r.PadCard * 1.7);
        // 页面留白：卡片浮在地面上时四周那一圈。原来各处写死 28，窗口缩到 900 时
        // 留白反而比内容还宽 —— 它是唯一应该跟着窗口瘦身的常量。
        res["WPageMargin"] = new Thickness(r.PadCard * 1.5);

        // 演出暗色（战斗 / CG / 升好感）：这几页永远在暗底上演，不随明暗档翻转——
        // 但色值必须有名字。无名的手写色在换主题时是全应用唯一不动的那一块，
        // 而且深色档下它们会和页面其余部分形成两套黑。
        res["BattleNight"] = Color.FromArgb("#1A1A2E");
        res["BattlePanel"] = Color.FromArgb("#16213E");
        res["BattlePurple"] = Color.FromArgb("#533483");
        res["BattleAccent"] = Color.FromArgb("#E94560");
        res["BattleHeal"] = Color.FromArgb("#4ECCA3");
        res["BattleInk"] = Color.FromArgb("#FFFFFF");
        res["BattleInkDim"] = Color.FromArgb("#9A9AB4");
        res["BattleScrim"] = Color.FromArgb("#19000000");
        res["CgInk"] = Color.FromArgb("#FFFFFF");
        res["CgInkDim"] = Color.FromArgb("#CCFFFFFF");
        res["CgChip"] = Color.FromArgb("#80FFFFFF");
        res["AffectionRose"] = Color.FromArgb("#E8508D");
        res["AffectionRoseSoft"] = Color.FromArgb("#EE6FA5");
        res["AffectionInk"] = Color.FromArgb("#FFF0F6");
        res["AffectionInkSoft"] = Color.FromArgb("#FFE3F0");
        res["AffectionScrim"] = Color.FromArgb("#55331F66");

        // 玻璃档的三个色标：GlassTokens 原来把 #FDF8F0 / #FFFFFF / #8B7D6B 这些奶油系的值
        // 直接写死在画刷里，换到晨雾蓝灰或深色档时，玻璃卡片会变成一块不属于这个世界的奶油。
        // 现在它只保留「哪一档多透、边多亮」这套材质算法，颜色一律由世界给。
        res["GTintTop"] = Lift(surface, dark ? 0.16 : 0.58);
        res["GTintMid"] = surface;
        res["GTintLow"] = Sink(surface, dark ? 0.06 : 0.10);
        res["GEdgeHi"] = dark ? Lift(surface, 0.60) : Colors.White;
        res["GEdgeMid"] = SinkOrLift(surface, dark, 0.70, 0.30);
        res["GEdgeLo"] = SinkOrLift(surface, dark, 0.30, 0.55);

        // ── 世界签名层（UI 标准书 2.7）：大气与印记 ──────────────────────
        // 大气层是一整页的纹理叠层（WorldAtmosphere 控件消费这三个透明度），
        // 每个世界按性格取不同的处方；Endfield 的颗粒与 spot 停用 —— HUD 世界自带舞台灯。
        // 印记（页首 Eyebrow 前 10-12px 的小色块）：极简是朱印直角、轻质感是奶油圆点、
        // 渐变/液态是accent园角、终末地就是信号黄本身。
        // ── 世界签名层（UI 标准书 2.7）：大气层 ──────────────────────────
        // 档案室唯一处方：砂纸颗粒（墙）、头顶一盏暖光（灯）、四周沉进阴影（暗角）。
        // 2026-11-06 第二版：第一版 0.04–0.10 实测「看不出区别」，这一轮直接拉到
        // 肉眼可辨的档 —— 玻璃卡面背后要有东西可透，才读得出玻璃。
        res["WAtmoGrainOpacity"] = 0.07;
        res["WAtmoSpotOpacity"] = 0.24;
        res["WAtmoVignetteOpacity"] = 0.16;

        // 印记形态（颜色在 PublishColors 的 WMarkColor，那边才看得到 signal）：
        // 信号色近直角小块，HUD 角标 notch 感。
        res["WMarkRadius"] = 2;
        res["WMarkSize"] = 12.0;
    }

    private static RoundRectangle Shape(double radius) => new() { CornerRadius = new CornerRadius(radius) };

    /// <summary>
    /// 影子的颜色：黑 + alpha。alpha 为 0 即「这个世界没有影子」，但 Shadow 对象照样存在、不换，
    /// 这样换世界不会重建合成层。深色档压到五成：底已经够暗，落影读不出来，深浅靠台阶。
    /// </summary>
    private static Color ShadowInk(bool dark, double strength) =>
        new(0f, 0f, 0f, (float)(dark ? strength * 0.55 : strength));

    /// <summary>
    /// 诊断开关：环境变量 WARM_NO_SHADOW=1 时无论设置怎样都不发影子。
    /// 单独立成只读字段，是因为它和「性能模式」的写入方向相反：性能模式可以开也可以关，
    /// 而排障开关一旦置位就应该一直压着。RuntimeConfigurator 下发性能模式时按
    /// <c>性能模式 || ForceFlatShadows</c> 取或，关掉性能模式不会把排障开关一起关掉。
    /// </summary>
    public static bool ForceFlatShadows { get; } = Environment.GetEnvironmentVariable("WARM_NO_SHADOW") == "1";

    /// <summary>轻量模式（设置里可开）与诊断开关：一律不发影子，省掉每个元素一层合成层。</summary>
    public static bool FlatShadows { get; set; } = ForceFlatShadows;

    // ── 色算 ────────────────────────────────────────────────────────────
    private static Color FromHex(string hex) => Color.FromArgb(hex);

    /// <summary>通道跨度 max−min：这一个世界用来表示「看得出有色偏」的程度。</summary>
    private static double Chroma(Color c) =>
        Math.Max(c.Red, Math.Max(c.Green, c.Blue)) - Math.Min(c.Red, Math.Min(c.Green, c.Blue));

    /// <summary>色相种子：地/强调/三个状态色各取色相与彩度差，全部从色板原始值读一次。</summary>
    private static Seed ReadSeed(ResourceDictionary res)
    {
        var ground = Get(res, "SurfaceBg", "#FDF8F0");
        var accent = Get(res, "AccentWarm", "#F5D8B8");
        var green = Get(res, "GreenSoft", "#A5D6A7");
        var red = Get(res, "RedSoft", "#EF9A9A");
        var blue = Get(res, "BlueSoft", "#90CAF9");
        var gh = RgbToHsl(ground).H;
        // 页面底几乎无彩时（纯灰阶色板），退到强调色的色相，否则 tint 会整条消失。
        if (Chroma(ground) < 0.006) gh = RgbToHsl(accent).H;
        return new Seed(gh, Chroma(ground), RgbToHsl(accent).H, Chroma(accent),
            RgbToHsl(green).H, Chroma(green), RgbToHsl(red).H, Chroma(red),
            RgbToHsl(blue).H, Chroma(blue));
    }

    private static Color Get(ResourceDictionary res, string key, string fallback) =>
        res.TryGetValue(key, out var v) && v is Color c ? c : FromHex(fallback);

    /// <summary>
    /// 有彩的中性阶：先取目标明度的纯灰，再往这个色相的纯色按比例混一点。
    /// 用「混纯色相」而不是直接设 HSL 的 S，是因为 HSL 在近白/近黑处把彩度挤成一丁点，
    /// 四个色板会算出几乎一样的地；混色相的强度是线性的，台阶之间 tint 也一致。
    /// 混入 L=0.5 的纯色会顺手把明度拉低一点，量控制在 0.18 以内，
    /// 相对三档之间 3~5% 的台阶来说可以忽略，且三档受的影响同向、不会把台阶翻过来。
    /// </summary>
    private static Color Tint(double hue, double amount, double lightness)
    {
        var l = (float)Math.Clamp(lightness, 0.02, 0.995);
        var grey = new Color(l, l, l, 1f);
        return Mix(grey, Hsl(hue, 1.0, 0.5), Math.Clamp(amount, 0, 0.18));
    }

    /// <summary>
    /// 状态色（成功/危险/信息）：色相跟着色板，明度钉在当前地面上。
    /// 它们常被当小字或图标色用，所以最后兜一道 3.2:1 —— 淡彩在深色档最容易糊成一团。
    /// </summary>
    private static Color Status(double hue, double chroma, Color ground, bool dark)
    {
        var c = Tint(hue, chroma * (dark ? 1.6 : 1.2) + 0.03, dark ? 0.58 : 0.42);
        return Ratio(c, ground) < 3.0 ? Lock(c, ground, 3.2) : c;
    }

    /// <summary>同一个动作的两个方向：浅档向黑沉、深档向白提，参数分别给。</summary>
    private static Color SinkOrLift(Color c, bool dark, double sink, double lift) =>
        dark ? Lift(c, lift) : Sink(c, sink);

    /// <summary>向黑沉：k=1 时到纯黑。用于浅色档的墨与井。</summary>
    private static Color Sink(Color c, double k) => Mix(c, Colors.Black, k);
    /// <summary>向白提：k=1 时到纯白。用于深色档的墨。</summary>
    private static Color Lift(Color c, double k) => Mix(c, Colors.White, k);

    /// <summary>
    /// 悬停/按下的一档，从元素**自己这张面**算出来，而不是发一个固定令牌：
    /// 同一条行可能站在页底上，也可能站在卡面或淡彩徽章上，固定值必然在其中一处
    /// 变成「鼠标移上去没变化」—— 参照物只能是它自己。方向跟着这张面的明度走：
    /// 亮面往黑沉、暗面往白提，两边都是「更亮一档的注意」，不是「变灰」。
    /// </summary>
    public static Color Step(Color face, double k) =>
        Lum(face) > 0.5 ? Sink(face, k) : Lift(face, Math.Clamp(k * 1.2, 0, 1));

    /// <summary>
    /// 透明底（静止时什么都不画、鼠标进来才盖一层）用的那一档：不换成实色，而是往父面上
    /// 压一层极浅的色偏 —— 这样它盖在什么颜色上都成立，也不必知道底下是什么。
    /// 浅档压黑、深档压白（暗地上压黑等于什么都没发生）；深档要多给一点，
    /// 因为白光在黑地上被瞳孔读成的台阶比黑光在白地上短。
    /// </summary>
    public static Color Wash(double k) => Dark
        ? new Color(1f, 1f, 1f, (float)Math.Clamp(k * 1.1, 0, 1))
        : new Color(0f, 0f, 0f, (float)Math.Clamp(k, 0, 1));

    private static Color Mix(Color a, Color b, double k)
    {
        var t = (float)Math.Clamp(k, 0, 1);
        return new Color(a.Red + (b.Red - a.Red) * t, a.Green + (b.Green - a.Green) * t,
            a.Blue + (b.Blue - a.Blue) * t, a.Alpha);
    }

    private static double Lum(Color c)
    {
        double Ch(float v) => v <= 0.03928f ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        return 0.2126 * Ch(c.Red) + 0.7152 * Ch(c.Green) + 0.0722 * Ch(c.Blue);
    }

    private static double Ratio(Color a, Color b)
    {
        var la = Lum(a);
        var lb = Lum(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    /// <summary>
    /// 把前景色推向「与地面相反的那一极」直到对比比达标。
    /// 这是**构造性保证**：以后任何人调世界台阶，都不可能再调出 1.05:1 的说明行。
    /// </summary>
    private static Color Lock(Color fg, Color ground, double target)
    {
        if (Ratio(fg, ground) >= target) return fg;
        var toward = Lum(ground) > 0.5 ? Colors.Black : Colors.White;
        double lo = 0, hi = 1;
        for (var i = 0; i < 24; i++)
        {
            var mid = (lo + hi) / 2;
            if (Ratio(Mix(fg, toward, mid), ground) >= target) hi = mid;
            else lo = mid;
        }
        return Mix(fg, toward, hi);
    }

    private static (double H, double S, double L) RgbToHsl(Color c)
    {
        var max = Math.Max(c.Red, Math.Max(c.Green, c.Blue));
        var min = Math.Min(c.Red, Math.Min(c.Green, c.Blue));
        var l = (max + min) / 2;
        var d = max - min;
        if (d < 1e-6) return (0, 0, l);
        var s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
        double h;
        if (max == c.Red) h = ((c.Green - c.Blue) / d + (c.Green < c.Blue ? 6 : 0)) * 60;
        else if (max == c.Green) h = ((c.Blue - c.Red) / d + 2) * 60;
        else h = ((c.Red - c.Green) / d + 4) * 60;
        return (h, s, l);
    }

    private static Color Hsl(double h, double s, double l)
    {
        h = ((h % 360 + 360) % 360) / 360;
        if (s < 1e-6) return new Color((float)l, (float)l, (float)l, 1f);
        double f(double p, double q, double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            if (t < 1 / 6.0) return p + (q - p) * 6 * t;
            if (t < 1 / 2.0) return q;
            if (t < 2 / 3.0) return p + (q - p) * (2 / 3.0 - t) * 6;
            return p;
        }
        var q = l < 0.5 ? l * (1 + s) : l + s - l * s;
        var p = 2 * l - q;
        return new Color((float)f(p, q, h + 1 / 3.0), (float)f(p, q, h), (float)f(p, q, h - 1 / 3.0), 1f);
    }
}
