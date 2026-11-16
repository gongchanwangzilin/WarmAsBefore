using Microsoft.Maui.Graphics;
using WarmAsBefore.Models.Background;

namespace WarmAsBefore.Drawables;

/// <summary>
/// 前景天气绘制层：一层覆盖在 UI 之上的"雨打玻璃"效果。
///
/// 视觉流程（每滴雨在自己的生命周期里走完整条链）：
///   自由下落 → 命中控件表面凝结成水珠 → 水珠在表面缓慢滑动 → 沿控件边缘加速滑落 →
///   滑出控件后恢复成雨滴，从周围流走。
/// 舞台避让：若设置 <see cref="StageRect"/>，落到舞台顶边的雨滴改为在顶边上方凝结、
///   沿顶边横向滑动后从近侧边缘滑走，绝不进入对话面板内部（不透明实体水珠同样遵循）。
///
/// 硬红线（与同目录 WeatherSceneDrawable 一致）：
/// - Draw 是纯函数：位置只由 <see cref="Phase"/> / <see cref="Intensity"/> / <see cref="Scene"/> /
///   <see cref="Obstacles"/> / <see cref="StageRect"/> 与 dirtyRect 决定，无任何累计状态、无 Random、无 Timer、无 Task。
///   外部推进 Phase 并 Invalidate 即触发重绘，本类不主动做任何动画。
/// - Draw 内零 new / 零堆分配：粒子数组构造时一次性预分配；所有颜色为静态只读字段；
///   所有中间量都是栈上的标量与 struct 副本。
/// - 不用 Layout 写入 / Shadow / Opacity 视图动画；只调用 ICanvas 绘制 API。
/// - 颜色一律是构造时创建的不透明灰白（Color.FromArgb + alpha=255）：雨丝是亮白实体线、
///   水珠是"浅灰主体 + 略深内核 + 纯白高光"的实心玻璃珠，不依赖主题 token，全程无半透明。
/// </summary>
public sealed class ForegroundRainDrawable : IDrawable
{
    /// <summary>粒子硬上限：构造时一次性预分配这么多，之后绝不扩容。</summary>
    private const int MaxCount = 120;

    /// <summary>黄金比：确定性分布，避免整齐的网格感。</summary>
    private const double Golden = 0.6180339887498949;

    /// <summary>雨丝的 5 档静态不透明灰白色（由深到浅），Draw 内只做数组索引，不创建 Color。</summary>
    private static readonly Color[] RainStreaks =
    {
        Color.FromArgb("#C8CED4"),
        Color.FromArgb("#D5D7DF"),
        Color.FromArgb("#E2E6EA"),
        Color.FromArgb("#EDF0F3"),
        Color.FromArgb("#F6F8FA"),
    };

    // 水珠 / 高光 / 拖尾 / 雪花的静态不透明色（同样在类型初始化时创建，Draw 内零分配）。
    private static readonly Color DropletBody = Color.FromArgb("#E0E5EA");      // 主体：不透明浅灰
    private static readonly Color DropletCore = Color.FromArgb("#BCC3CA");      // 内核：略深，制造玻璃纵深
    private static readonly Color DropletHighlight = Color.FromArgb("#FFFFFF"); // 顶部高光：纯白
    private static readonly Color DropletTrail = Color.FromArgb("#D2D8DE");     // 滑落拖尾：不透明浅灰
    private static readonly Color SnowFlake = Color.FromArgb("#EEF1F5");        // 雪花：不透明浅灰白

    /// <summary>
    /// 单个粒子的不变量种子。位置 / state / stickT / edgeDir 这些"状态"不存字段，
    /// 而是由 Phase 分段实时推导（纯函数），因此 struct 里只有常量种子。
    /// </summary>
    private struct RainDrop
    {
        public double SeedX;   // 0..1 横向黄金比分布
        public double SeedY;   // 0..1 纵向错落 / 命中高度
        public double Offset;  // 0..1 生命周期相位偏移，让各滴雨错峰走完整流程
        public double Len;     // 0..1 雨丝长度种子
        public double Drift;   // -1..1 横向漂移方向（同时决定滑向哪条控件边缘）
        public double Size;    // 水珠半径种子（像素）
    }

    private readonly RainDrop[] _drops = new RainDrop[MaxCount];

    /// <summary>控件几何快照（相对宿主页的矩形块），由 View 采集后一次性传入；null 表示无遮挡。</summary>
    public IReadOnlyList<RectF>? Obstacles { get; set; }

    /// <summary>舞台矩形（Galgame 对话面板区），雨落到其顶边即凝结滑走，不进入内部。屏幕坐标。</summary>
    public RectF? StageRect { get; set; }

    /// <summary>0..1 循环相位，由外部低频推进。注意必须用 (Phase + Offset) % 1，回绕处才连续。</summary>
    public double Phase { get; set; }

    /// <summary>0..1 雨量，决定本帧绘制的粒子数量与雨丝粗细。</summary>
    public double Intensity { get; set; }

    /// <summary>决定雨 / 雪形态，以及 Sunny / Cloudy / Fog 时完全不画。</summary>
    public WeatherSceneKind Scene { get; set; }

    public ForegroundRainDrawable()
    {
        // 一次性预分配：无任何 Random，全部由索引的确定性哈希推导。
        for (int i = 0; i < MaxCount; i++)
        {
            _drops[i] = new RainDrop
            {
                SeedX = Frac(i * Golden),
                SeedY = Frac(i * 0.3819660112501051),
                Offset = Frac(i * 0.7548776662466927),
                Len = Frac(i * 0.201524 + 0.13),
                Drift = (i % 2 == 0 ? 1.0 : -1.0) * (0.45 + Frac(i * 0.41421356) * 0.55),
                Size = 1.8 + Frac(i * 0.932815) * 1.4, // 1.8 ~ 3.2 px
            };
        }
    }

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        float w = dirtyRect.Width;
        float h = dirtyRect.Height;
        if (w <= 0 || h <= 0) return;

        bool isRain = Scene is WeatherSceneKind.Drizzle
            or WeatherSceneKind.LightRain
            or WeatherSceneKind.HeavyRain
            or WeatherSceneKind.Storm;
        bool isSnow = Scene == WeatherSceneKind.Snow;
        if (!isRain && !isSnow) return; // 晴 / 多云 / 雾：前景完全透明

        // 低分辨率减半粒子，控制绘制开销。
        int max = w < 300 ? MaxCount / 2 : MaxCount;
        double intensity = Clamp(Intensity, 0, 1);
        int count = (int)(intensity * max);
        if (count <= 0) return;
        if (count > max) count = max;

        var obstacles = Obstacles;
        int obstacleCount = obstacles?.Count ?? 0;

        // 雨丝粗细一次设好：不透明实体雨滴，线宽 1.5~2.5；雪分支不描边，无副作用。
        canvas.StrokeSize = 1.5f + (float)intensity * 1.0f;

        for (int i = 0; i < count; i++)
        {
            RainDrop d = _drops[i];

            // 生命周期相位：Phase 从 1 回绕到 0 时 Frac(Phase+Offset) 连续，无跳帧。
            double t = Frac(Phase + d.Offset);

            // 自由下落的基础轨迹（斜向左下，画布越窄斜线越陡）。
            float baseX = (float)(d.SeedX * w);
            float baseY = (float)(t * h);
            float lineLen = (float)(8 + d.Len * 26 + intensity * 10);
            float lineSlant = lineLen * 0.26f * (float)Clamp(w / 420.0, 0.25, 1.0);
            float fallX = baseX - lineSlant * (float)t;

            // ---------- 雪：小圆点 + 正弦横摆，视觉更缓 ----------
            if (isSnow)
            {
                double swing = Math.Sin((t * 1.6 + d.SeedY) * Math.PI * 2) * 0.018 * w;
                canvas.FillColor = SnowFlake;
                canvas.FillCircle((float)(fallX + swing), baseY, (float)d.Size * 0.8f);
                continue;
            }

            // ---------- 舞台避让：雨落到舞台顶边即凝结，沿顶边滑走，绝不进入对话面板内部 ----------
            // 仅拦截"到达顶边时横向位置落在舞台宽度内"的雨滴——这类雨滴若不加干预必然穿过面板。
            // 全程把水珠绘制在顶边上方（y 不越过 st.Top），纯函数、零分配；无 StageRect 时退化为下方原逻辑。
            if (StageRect is RectF st && st.Width > 20 && st.Height > 12)
            {
                double tStage = st.Top / h; // 到达舞台顶边的相位
                if (tStage > 0 && tStage < 1)
                {
                    float stageHitX = (float)(d.SeedX * w - lineSlant * tStage);
                    if (stageHitX >= st.Left && stageHitX <= st.Right)
                    {
                        float size = (float)d.Size;
                        float restY = st.Top - size; // 水珠中心：整体贴在顶边上方，不侵入面板
                        float slideSpan = st.Right - st.Left;
                        float stEndX = ClampF(stageHitX + (float)(d.Drift * slideSpan * 0.16), st.Left + size, st.Right - size);
                        // 近侧边缘外侧：离开横向范围之后 y 才能增加，保证绝不进入面板内部。
                        float edgeOutX = (stageHitX - st.Left) <= (st.Right - stageHitX)
                            ? st.Left - size
                            : st.Right + size;

                        if (t < tStage)
                        {
                            // 命中前：自由下落；钳制的是雨丝下端（滴头），使其停在顶边上方悬停，
                            // 而非钳制起点——否则斜线末端会越过 st.Top 侵入面板。
                            DrawStreak(canvas, fallX, Math.Min(baseY, restY - lineLen), lineLen, lineSlant,
                                RainStreaks[(i + 2) % RainStreaks.Length]);
                            continue;
                        }

                        double stCondEnd = tStage + (1 - tStage) * 0.35;
                        if (t <= stCondEnd)
                        {
                            // ① 凝结态：水珠悬在顶边上方，沿顶边横向缓慢滑动一小段。
                            double u = Smooth((t - tStage) / (stCondEnd - tStage));
                            DrawDroplet(canvas, (float)Lerp(stageHitX, stEndX, u), restY, size);
                            continue;
                        }

                        // ② 滑落态：前半段滑到近侧边缘外，后半段沿边缘外侧下落。
                        //    x 抵达 edgeOutX（已在舞台横向范围外）之后 y 才开始增加，绝不进入内部。
                        double v = Smooth((t - stCondEnd) / (1 - stCondEnd));
                        float slideX = (float)Lerp(stEndX, edgeOutX, Smooth(Math.Min(1, v * 2)));
                        float dropY = restY + (float)(Smooth(Math.Max(0, v * 2 - 1)) * (h - restY + 12));
                        DrawSlidingDrop(canvas, slideX, dropY, size, lineLen * 0.4f);
                        continue;
                    }
                }
            }

            // ---------- 命中时刻：每滴雨有自己的撞击高度（0.25h ~ 0.5h）----------
            double tHit = 0.25 + d.SeedY * 0.25;
            if (t < tHit || obstacleCount == 0)
            {
                DrawStreak(canvas, fallX, baseY, lineLen, lineSlant, RainStreaks[(i + (int)(intensity * 3)) % RainStreaks.Length]);
                continue;
            }

            // 撞击点是"时刻 tHit 的位置"，与当前 t 无关，保证凝结/滑落阶段目标一致。
            float hitY = (float)(tHit * h);
            float hitX = baseX - lineSlant * (float)tHit;

            // 遍历几何快照：只认足够大的控件，并找出包含撞击点的那一块。
            RectF target = default;
            bool found = false;
            for (int j = 0; j < obstacleCount; j++)
            {
                RectF r = obstacles![j];
                if (r.Width <= 20 || r.Height <= 12) continue;
                if (hitX >= r.Left && hitX <= r.Right && hitY >= r.Top && hitY <= r.Bottom)
                {
                    target = r;
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                // 没有撞到控件：保持全程自由下落，位置与命中前完全一致（无空洞）。
                DrawStreak(canvas, fallX, baseY, lineLen, lineSlant, RainStreaks[(i + 2) % RainStreaks.Length]);
                continue;
            }

            float left = target.Left;
            float top = target.Top;
            float right = target.Right;
            float bottom = target.Bottom;

            const double condLen = 0.22; // 凝结滑动占生命周期比例
            const double slideLen = 0.18; // 沿边缘滑落占生命周期比例
            double tCondEnd = tHit + condLen;
            double tSlideEnd = tCondEnd + slideLen;

            // 凝结阶段结束时水珠在控件表面的落点（滑落阶段的起点，边界处完全重合）。
            float endX = ClampF(hitX + (float)(d.Drift * (right - left) * 0.18), left + 2, right - 2);
            float endY = ClampF(hitY + (float)((bottom - top) * 0.32), top + 2, bottom - 2);
            float edgeX = d.Drift > 0 ? right - 1.5f : left + 1.5f;

            if (t <= tCondEnd)
            {
                // ① 凝结态：水珠停留在控件表面，缓慢横向漂移 + 下移（smoothstep 平滑）。
                double u = Smooth((t - tHit) / condLen);
                float px = ClampF(hitX + (float)(d.Drift * (right - left) * 0.18 * u), left + 2, right - 2);
                float py = ClampF(hitY + (float)((bottom - top) * 0.32 * u), top + 2, bottom - 2);
                DrawDroplet(canvas, px, py, (float)d.Size);
                continue;
            }

            if (t <= tSlideEnd)
            {
                // ② 边缘滑落态：先平移到最近的左右边缘，再沿边缘向下加速，最终越过底边。
                double v = Smooth((t - tCondEnd) / slideLen);
                float px = (float)Lerp(endX, edgeX, v);
                float py = (float)(endY + v * v * (bottom - endY + 6));
                DrawSlidingDrop(canvas, px, py, (float)d.Size, lineLen * 0.45f);
                continue;
            }

            // ③ 滑出控件：恢复自由下落雨滴，横向从边缘流走，直到屏幕底部。
            double outT = (t - tSlideEnd) / (1 - tSlideEnd);
            float flowX = edgeX + (float)(d.Drift * outT * 14);
            float flowY = bottom + 6 + (float)(outT * (h - bottom + 16));
            DrawStreak(canvas, flowX, flowY, lineLen * 0.8f, lineSlant * 0.5f, RainStreaks[i % RainStreaks.Length]);
        }
    }

    /// <summary>自由下落的实体雨滴：稍粗的亮灰斜线 + 下端纯白滴头，呈"实心水珠"质感。</summary>
    private static void DrawStreak(ICanvas canvas, float x, float y, float len, float slant, Color color)
    {
        float tipX = x - slant;
        float tipY = y + len;
        canvas.StrokeColor = color;
        canvas.DrawLine(x, y, tipX, tipY);
        // 下端滴头用纯白小圆点强调实体水珠，而非半透明线段。
        canvas.FillColor = DropletHighlight;
        canvas.FillCircle(tipX, tipY, 0.9f);
    }

    /// <summary>凝结水珠（不透明实体玻璃珠）：不透明浅灰主体 + 略深内核 + 顶部纯白高光。</summary>
    private static void DrawDroplet(ICanvas canvas, float x, float y, float size)
    {
        canvas.FillColor = DropletBody;
        canvas.FillEllipse(x - size * 0.55f, y - size * 0.85f, size * 1.1f, size * 1.7f);
        canvas.FillColor = DropletCore;
        canvas.FillEllipse(x - size * 0.32f, y - size * 0.5f, size * 0.64f, size * 1.0f);
        canvas.FillColor = DropletHighlight;
        canvas.FillCircle(x - size * 0.12f, y - size * 0.5f, Math.Max(0.6f, size * 0.16f));
    }

    /// <summary>沿边缘滑落的水珠（不透明）：细椭圆主体 + 拖尾短线 + 纯白高光。</summary>
    private static void DrawSlidingDrop(ICanvas canvas, float x, float y, float size, float trail)
    {
        canvas.StrokeColor = DropletTrail;
        canvas.DrawLine(x, y - trail, x, y - size * 0.6f);
        canvas.FillColor = DropletBody;
        canvas.FillEllipse(x - size * 0.5f, y - size * 0.8f, size, size * 1.6f);
        canvas.FillColor = DropletHighlight;
        canvas.FillCircle(x - size * 0.1f, y - size * 0.35f, Math.Max(0.5f, size * 0.14f));
    }

    // 以下纯数学工具均为无分配静态函数。
    private static double Frac(double v) => v - Math.Floor(v);
    private static double Clamp(double v, double lo, double hi) => v < lo ? lo : v > hi ? hi : v;
    private static float ClampF(float v, float lo, float hi) => v < lo ? lo : v > hi ? hi : v;
    private static double Lerp(double a, double b, double t) => a + (b - a) * t;
    private static double Smooth(double t)
    {
        t = Clamp(t, 0, 1);
        return t * t * (3 - 2 * t);
    }
}
