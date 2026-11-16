using System;
using System.Collections.Generic;
using Microsoft.Maui.Graphics;
using WarmAsBefore.Models.Background;

namespace WarmAsBefore.Services.Background;

/// <summary>
/// 背景静态目录：调色板、天气场景、字符串解析、取色与场景插值。
///
/// 设计约定：
/// 1. 所有颜色一律用资源字典里的 token key 表示，不在本文件硬编码任何 hex；
///    唯一的 hex 出现在 <see cref="ResolveColor"/> 的最后兜底，并已就地注明原因。
/// 2. 五套调色板共用同一批语义 key：运行时由 ThemeManager 替换这些 key 背后的 hex 值，
///    所以这里五套定义只有 Id / DisplayName 不同 —— 这是正确且刻意的。
/// 3. 天气场景的数值体现「太阳位置 / 云层数量与强度 / 雨滴粒子 / 整体强度」逐级变化，
///    供绘制端直接消费；Snow / Fog / Storm 为兼容场景，同样纳入表中。
/// </summary>
public static class BackgroundCatalog
{
    // ── 调色板 ──────────────────────────────────────────────────────────────

    /// <summary>
    /// 语义 token key 集中定义，避免五套调色板里的字符串重复书写导致漂移。
    /// 键名与 ColorPalette.xaml / StyleWorld 运行时资源字典保持一致。
    /// </summary>
    private const string KPageBg = "PageBg";
    private const string KSurfaceBg = "SurfaceBg";
    private const string KSurfaceFg = "SurfaceFg";
    private const string KText = "WarmDark";        // 正文墨色
    private const string KTextMuted = "MutedFg";     // 次要文字
    private const string KButtonBg = "PrimaryAction";
    private const string KButtonFg = "PrimaryFg";
    private const string KPanelBg = "WellBg";        // 凹陷面板底
    private const string KPanelBorder = "EdgeStrong";// 面板描边
    private const string KAccent = "AccentWarm";
    private const string KSignal = "Signal";          // 运行时由 StyleWorld 注入
    private const string KScrim = "OverlayDim";       // 遮罩

    /// <summary>
    /// 五套调色板（classic / sakura / bamboo / mist / endfield）。
    /// 字段值全部相同——切换由 ThemeManager 换 hex 实现，这里统一消费同一批语义 key。
    /// 使用 OrdinalIgnoreCase，方便上层按大小写不敏感的 id 取用。
    /// </summary>
    public static IReadOnlyDictionary<string, PaletteDefinition> Palettes { get; } =
        new Dictionary<string, PaletteDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["classic"] = Shared("classic", "经典"),
            ["sakura"] = Shared("sakura", "樱花粉"),
            ["bamboo"] = Shared("bamboo", "翠竹绿"),
            ["mist"] = Shared("mist", "晨雾蓝灰"),
            ["endfield"] = Shared("endfield", "终末地"),
        };

    /// <summary>构造共享同一批 token key 的调色板定义；只有 id / 显示名不同。</summary>
    private static PaletteDefinition Shared(string id, string displayName) => new(
        Id: id,
        DisplayName: displayName,
        PageBgKey: KPageBg,
        SurfaceBgKey: KSurfaceBg,
        SurfaceFgKey: KSurfaceFg,
        TextKey: KText,
        TextMutedKey: KTextMuted,
        ButtonBgKey: KButtonBg,
        ButtonFgKey: KButtonFg,
        PanelBgKey: KPanelBg,
        PanelBorderKey: KPanelBorder,
        AccentKey: KAccent,
        SignalKey: KSignal,
        ScrimKey: KScrim);

    // ── 天气场景 ────────────────────────────────────────────────────────────

    // 云 / 雨 / 太阳的 token 全场景共用（任务仅对 Sunny 显式给出，其余沿用同一语义）。
    private const string DefaultCloudToken = "OffWhite100";
    private const string DefaultRainToken = "BlueSoft";
    private const string DefaultSunToken = "AccentWarm";

    /// <summary>
    /// 天气场景表：5 种必需（Sunny / Cloudy / Drizzle / LightRain / HeavyRain）
    /// 加 3 种兼容（Storm / Snow / Fog）。
    /// 无雨场景（Sunny / Cloudy / Fog）的雨参数给最小档默认值，绘制端按 RainCount=0 不画雨，
    /// 这些值仅在 <see cref="Blend"/> 插值时保持数值连续，不影响画面。
    /// </summary>
    public static IReadOnlyDictionary<WeatherSceneKind, WeatherSceneDefinition> Scenes { get; } =
        new Dictionary<WeatherSceneKind, WeatherSceneDefinition>
        {
            // 晴朗：太阳高、小而亮，几乎无云无雨，整体强度最低。
            [WeatherSceneKind.Sunny] = new(
                WeatherSceneKind.Sunny, "晴朗",
                0.15,
                0.78, 0.20, 0.16, 0.95,
                1, 0.18, 0.9,
                0, 0.02, 0.16, 0.35,
                "Cream50", "Beige150", DefaultCloudToken, DefaultRainToken, DefaultSunToken,
                false),

            // 阴天：太阳变暗变小，云量升到 4，仍无雨。
            [WeatherSceneKind.Cloudy] = new(
                WeatherSceneKind.Cloudy, "阴天",
                0.35,
                0.7, 0.25, 0.11, 0.35,
                4, 0.42, 1.15,
                0, 0.02, 0.16, 0.35,
                "Beige150", "Beige250", DefaultCloudToken, DefaultRainToken, DefaultSunToken,
                false),

            // 毛毛雨：云 5 层，28 条细小雨丝，速度慢、透明度低。
            [WeatherSceneKind.Drizzle] = new(
                WeatherSceneKind.Drizzle, "毛毛雨",
                0.5,
                0.68, 0.28, 0.09, 0.2,
                5, 0.5, 1.2,
                28, 0.02, 0.16, 0.35,
                "Beige200", "Beige300", DefaultCloudToken, DefaultRainToken, DefaultSunToken,
                false),

            // 小雨：云 6 层，55 条雨滴，长度与速度上调。
            [WeatherSceneKind.LightRain] = new(
                WeatherSceneKind.LightRain, "小雨",
                0.68,
                0.65, 0.3, 0.07, 0.12,
                6, 0.58, 1.3,
                55, 0.035, 0.26, 0.5,
                "Beige250", "MutedFg", DefaultCloudToken, DefaultRainToken, DefaultSunToken,
                false),

            // 大雨：云 7 层，95 条长而快的雨，首次出现闪电。
            [WeatherSceneKind.HeavyRain] = new(
                WeatherSceneKind.HeavyRain, "大雨",
                0.9,
                0.6, 0.32, 0.05, 0.06,
                7, 0.7, 1.4,
                95, 0.05, 0.42, 0.68,
                "Beige300", "WarmMuted", DefaultCloudToken, DefaultRainToken, DefaultSunToken,
                true),

            // 雷暴（兼容）：强度封顶，云 8 层，120 条急雨，闪电常开。
            [WeatherSceneKind.Storm] = new(
                WeatherSceneKind.Storm, "雷暴",
                1.0,
                0.6, 0.34, 0.04, 0.04,
                8, 0.78, 1.5,
                120, 0.06, 0.5, 0.75,
                "WarmMuted", "MutedFg", DefaultCloudToken, DefaultRainToken, DefaultSunToken,
                true),

            // 雪（兼容）：云 5 层、40 个粒子；用雨参数承载雪花，绘制端按 Kind 区分形状。
            [WeatherSceneKind.Snow] = new(
                WeatherSceneKind.Snow, "雪",
                0.45,
                0.72, 0.22, 0.1, 0.4,
                5, 0.5, 1.2,
                40, 0.015, 0.09, 0.6,
                "OffWhite100", "Beige150", DefaultCloudToken, DefaultRainToken, DefaultSunToken,
                false),

            // 雾（兼容）：云 6 层但无雨，低强度、能见度低。
            [WeatherSceneKind.Fog] = new(
                WeatherSceneKind.Fog, "雾",
                0.3,
                0.7, 0.3, 0.08, 0.25,
                6, 0.55, 1.25,
                0, 0.02, 0.16, 0.35,
                "OffWhite200", "Beige200", DefaultCloudToken, DefaultRainToken, DefaultSunToken,
                false),
        };

    // ── 字符串解析 ──────────────────────────────────────────────────────────

    /// <summary>
    /// 把天气描述（英文 condition 或中文 Description，或两者混合）解析为场景。
    /// 大小写不敏感；按下方优先级自上而下匹配，命中即返回。
    ///
    /// 优先级（先英文后中文）：
    ///   1 storm/雷 → 2 drizzle/毛毛雨 → 3 snow/雪 → 4 fog/雾/mist →
    ///   5 showers/阵雨/小雨 → 6 rain/雨（含"大"为 HeavyRain，否则 LightRain）→
    ///   7 overcast/阴 → 8 cloud/多云 → 9 sun/晴/clear/mostly → 10 空/未知默认晴。
    /// </summary>
    public static WeatherSceneKind Resolve(string? descriptionOrCondition)
    {
        if (string.IsNullOrWhiteSpace(descriptionOrCondition))
            return WeatherSceneKind.Sunny; // 空描述：默认晴天，避免空白场景

        // 统一转小写：英文关键词已全部按小写书写，中文不受影响，从而做到大小写不敏感。
        var text = descriptionOrCondition.Trim().ToLowerInvariant();

        // 组内先英文后中文列出，纯逻辑为「任一命中即为真」。
        static bool Has(string text, params string[] words)
        {
            foreach (var word in words)
                if (text.Contains(word, StringComparison.Ordinal)) return true;
            return false;
        }

        // 1. 雷暴
        if (Has(text, "storm", "雷")) return WeatherSceneKind.Storm;
        // 2. 毛毛雨
        if (Has(text, "drizzle", "毛毛雨")) return WeatherSceneKind.Drizzle;
        // 3. 雪
        if (Has(text, "snow", "雪")) return WeatherSceneKind.Snow;
        // 4. 雾
        if (Has(text, "fog", "mist", "雾")) return WeatherSceneKind.Fog;
        // 5. 阵雨 / 小雨（排在 rain 之前，保证「小雨」不会被误判为大雨档）
        if (Has(text, "showers", "阵雨", "小雨")) return WeatherSceneKind.LightRain;
        // 6. 雨：中文「大雨 / 下雨」中的"大"决定是否升到 HeavyRain
        if (Has(text, "rain", "雨"))
            return Has(text, "大") ? WeatherSceneKind.HeavyRain : WeatherSceneKind.LightRain;
        // 7. 阴
        if (Has(text, "overcast", "阴")) return WeatherSceneKind.Cloudy;
        // 8. 云
        if (Has(text, "cloud", "多云")) return WeatherSceneKind.Cloudy;
        // 9. 晴
        if (Has(text, "sun", "晴", "clear", "mostly")) return WeatherSceneKind.Sunny;
        // 10. 未知：默认晴天
        return WeatherSceneKind.Sunny;
    }

    // ── 取色 ────────────────────────────────────────────────────────────────

    /// <summary>
    /// 按 token key 从当前应用资源字典取 Color；取不到（或 key 为空、资源异常）返回 fallback。
    /// </summary>
    public static Color ResolveColor(string? tokenKey, Color fallback)
    {
        if (!string.IsNullOrWhiteSpace(tokenKey))
        {
            try
            {
                if (Application.Current?.Resources.TryGetValue(tokenKey, out var value) == true
                    && value is Color color)
                    return color;
            }
            catch
            {
                // 资源字典尚未就绪等情况：静默回退，不打断绘制。
            }
        }

        // 全文件唯一允许出现 hex 的地方：调用方未给 fallback 时的最后兜底色。
        // 用 OffWhite100 同值，保证即使资源完全缺失也能画出可见背景，而不是透明/黑屏。
        return fallback ?? Color.FromArgb("#FDF8F0");
    }

    // ── 场景插值 ────────────────────────────────────────────────────────────

    /// <summary>
    /// 两个场景之间按 t 线性插值，用于天气无缝渐变过渡。
    /// t=0 返回 from，t=1 返回 to；中间对所有 double/int 数值字段线性插值（int 四舍五入），
    /// bool / token 字段以 0.5 为界取 from 或 to；Kind、DisplayName 始终取 to。
    /// </summary>
    public static WeatherSceneDefinition Blend(WeatherSceneDefinition from, WeatherSceneDefinition to, double t)
    {
        if (t <= 0) return from;
        if (t >= 1) return to;

        // 线性插值（double）
        static double Lerp(double a, double b, double t) => a + (b - a) * t;
        // 整数插值：四舍五入（ midpoint 远离零），避免 banker's rounding 带来的偏差
        static int LerpInt(int a, int b, double t) =>
            (int)Math.Round(a + (b - a) * t, MidpointRounding.AwayFromZero);
        // 离散字段：以 0.5 为阈值切换
        static bool Pick(bool a, bool b, double t) => t < 0.5 ? a : b;
        static string PickStr(string a, string b, double t) => t < 0.5 ? a : b;

        // 用 to 作为基底，Kind / DisplayName 自动保留 to 的值；其余字段逐项覆盖。
        return to with
        {
            // 强度与太阳
            Intensity = Lerp(from.Intensity, to.Intensity, t),
            SunX = Lerp(from.SunX, to.SunX, t),
            SunY = Lerp(from.SunY, to.SunY, t),
            SunSize = Lerp(from.SunSize, to.SunSize, t),
            SunAlpha = Lerp(from.SunAlpha, to.SunAlpha, t),
            // 云层
            CloudCount = LerpInt(from.CloudCount, to.CloudCount, t),
            CloudAlpha = Lerp(from.CloudAlpha, to.CloudAlpha, t),
            CloudScale = Lerp(from.CloudScale, to.CloudScale, t),
            // 雨滴 / 雪花粒子
            RainCount = LerpInt(from.RainCount, to.RainCount, t),
            RainLength = Lerp(from.RainLength, to.RainLength, t),
            RainSpeed = Lerp(from.RainSpeed, to.RainSpeed, t),
            RainAlpha = Lerp(from.RainAlpha, to.RainAlpha, t),
            // 颜色 token（天空两档 + 云 / 雨 / 太阳）
            SkyTopToken = PickStr(from.SkyTopToken, to.SkyTopToken, t),
            SkyBottomToken = PickStr(from.SkyBottomToken, to.SkyBottomToken, t),
            CloudToken = PickStr(from.CloudToken, to.CloudToken, t),
            RainToken = PickStr(from.RainToken, to.RainToken, t),
            SunToken = PickStr(from.SunToken, to.SunToken, t),
            // 闪电开关
            ShowLightning = Pick(from.ShowLightning, to.ShowLightning, t),
        };
    }
}
