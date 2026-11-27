using System.Text.Json;
using System.Text.Json.Nodes;
using WarmAsBefore.DesignSystem.Theme;
using WarmAsBefore.Models;
using WarmAsBefore.Modules.SaveSystem;
using WarmAsBefore.Services.Background;

namespace WarmAsBefore.Services.Web;

/// <summary>
/// 前端 ↔ 宿主的消息桥（协议唯一出处在 <c>web/src/bridge/protocol.ts</c>）。
///
/// 协议：
///   前端 → 宿主：<c>{"id":number,"method":string,"params":object}</c>
///   宿主 → 前端：<c>{"id":number,"ok":true,"result":object}</c> 或
///                <c>{"id":number,"ok":false,"error":{"code":"E_*","message":string,"detail"?:string}}</c>
///   宿主 → 前端事件：<c>{"event":string,"payload":object}</c>
///
/// 三条硬约束：
///  1. **方法白名单**：只有 <see cref="DispatchAsync"/> 里列出名字的方法能跑，其余一律
///     <c>E_UNKNOWN_METHOD</c>。前端页面会被第三方内容影响（插件页面等），桥不能变成
///     「宿主任意 API 的反射入口」。
///  2. **异常不越界**：任何托管异常都折成一条 <c>E_BACKEND</c> 回给前端，宿主不崩。
///     这条不是保守：WebView 的消息回调跑在 UI 线程上，漏一个异常就是整窗退出。
///  3. **令牌只读既有的一份**：<c>ui.tokens</c> 从 <see cref="Application.Current"/>.Resources
///     按名取值，再按前端那份词汇表（<c>--w-*</c>）改名发出去；不在这里手写第二份配色。
///
/// 传输层在页面里：Windows 走 WebView2 的 WebMessageReceived / PostWebMessageAsJson，
/// Android 走 JavascriptInterface / EvaluateJavascript。本类只吃字符串、只吐字符串。
/// </summary>
public sealed class WebBridge
{
    /// <summary>开发期前端地址（前端 dev server 的默认端口）：另有 <c>WARM_WEB_URL</c> 环境变量可覆盖。</summary>
    public const string DevUrl = "http://127.0.0.1:5189";

    private readonly SettingsManager _settings;
    private readonly SaveManager _saves;

    /// <summary>串行闸：settings.set 是「读→改→落盘」，两条消息挤在一起会互相覆盖。</summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    public WebBridge(SettingsManager settings, SaveManager saves)
    {
        _settings = settings;
        _saves = saves;
    }

    /// <summary>宿主 → 前端的发送通道；页面拿到 WebView 后挂上（见 WebShellPage）。</summary>
    public Func<string, Task>? ToWeb { get; set; }

    /// <summary>
    /// 前端已经接管宿主的显示（<c>host.ready</c> 发出之后）。由 <c>WebShellPage</c> 在导航成功时置真，
    /// 导航失败或重试时复位。它决定 <c>nav.open</c> 的应答形态。
    /// </summary>
    public bool WebFrontendReady { get; set; }

    /// <summary>前端报上来的当前路由（它每次导航都会调 <c>nav.open</c>）。宿主侧的返回栈/任务栏可以据此对齐。</summary>
    public string? CurrentRoute { get; private set; }

    // ==================== 收 ====================

    /// <summary>
    /// 收一条前端消息并回一条响应。永不抛：解析失败、未知方法、方法内部异常都变成 error。
    /// 全程在 UI 线程上跑（Android 的 JavascriptInterface 回调在线程池上，而设置下发与令牌读取都只认 UI 线程）。
    /// </summary>
    public async Task ReceiveAsync(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return;

        JsonNode? id = null;
        string? method = null;
        JsonElement args = default;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                await ReplyErrorAsync(null, "E_BAD_PARAMS", "请求不是对象。");
                return;
            }
            // JsonElement 活得比 doc 短，必须 Clone 才能带过 await。
            if (root.TryGetProperty("id", out var idEl)) id = JsonNode.Parse(idEl.GetRawText());
            if (root.TryGetProperty("method", out var mEl) && mEl.ValueKind == JsonValueKind.String)
                method = mEl.GetString();
            if (root.TryGetProperty("params", out var pEl)) args = pEl.Clone();
        }
        catch (JsonException ex)
        {
            await ReplyErrorAsync(null, "E_BAD_PARAMS", "请求不是合法 JSON。", ex.Message);
            return;
        }

        try
        {
            await Helpers.Ui.PostAsync(async () =>
            {
                await _gate.WaitAsync();
                try
                {
                    var result = await DispatchAsync(method, args);
                    await ReplyAsync(id, result);
                }
                catch (WebBridgeException ex)
                {
                    await ReplyErrorAsync(id, ex.Code, ex.Message);
                }
                catch (Exception ex)
                {
                    // 方法体里抛错是「这条路走不通」，不是「宿主坏了」：日志留一份，前端拿一句话。
                    App.WriteLog($"WebBridge {method} -> {ex}");
                    await ReplyErrorAsync(id, "E_BACKEND", "宿主处理这条请求时出错了。", ex.GetType().Name + ": " + ex.Message);
                }
                finally
                {
                    _gate.Release();
                }
            });
        }
        catch (Exception ex)
        {
            // 走到这里说明连 UI 线程都投递不进去（进程正在退出）：记一笔，不再往上抛。
            App.WriteLog("WebBridge.ReceiveAsync -> " + ex);
        }
    }

    /// <summary>方法白名单。新增方法只在这里加一行，前端拿不到别的任何入口。</summary>
    private async Task<object?> DispatchAsync(string? method, JsonElement p) => method switch
    {
        "ui.tokens" => TokensPayload(),
        "settings.get" => await SettingsGetAsync(),
        "settings.set" => await SettingsSetAsync(p),
        "palette.get" => PaletteGet(),
        "palette.set" => await PaletteSetAsync(p),
        "nav.open" => NavOpen(p),
        "nav.back" => NavBack(p),
        "app.info" => AppInfoPayload(),
        _ => throw new WebBridgeException("E_UNKNOWN_METHOD", "unknown method"),
    };

    // ==================== 发 ====================

    private async Task ReplyAsync(JsonNode? id, object? result)
    {
        var envelope = new JsonObject
        {
            ["id"] = id?.DeepClone(),
            ["ok"] = true,
            ["result"] = result is null ? new JsonObject() : JsonSerializer.SerializeToNode(result),
        };
        await SendAsync(envelope.ToJsonString());
    }

    private async Task ReplyErrorAsync(JsonNode? id, string code, string message, string? detail = null)
    {
        var error = new JsonObject { ["code"] = code, ["message"] = message };
        if (!string.IsNullOrWhiteSpace(detail)) error["detail"] = detail;
        var envelope = new JsonObject
        {
            ["id"] = id?.DeepClone(),
            ["ok"] = false,
            ["error"] = error,
        };
        await SendAsync(envelope.ToJsonString());
    }

    /// <summary>宿主 → 前端事件。发不出去只记日志：事件是通知，不是合同。</summary>
    public void Emit(string name, object? payload)
    {
        var envelope = new JsonObject
        {
            ["event"] = name,
            ["payload"] = payload is null ? new JsonObject() : JsonSerializer.SerializeToNode(payload),
        };
        _ = SendAsync(envelope.ToJsonString());
    }

    /// <summary>
    /// 宿主 → 前端的导航请求（原生侧想让前端去某个路由时用，比如原生菜单点了「设置」）。
    ///
    /// 注意方向：<c>nav.open</c> 是**前端**告诉宿主「我到了这个路由」，所以它只记录、不回灌
    /// <c>nav.request</c> —— 回灌会让前端再导航一次同一个路由，白白多跳一趟。
    /// </summary>
    public void Navigate(string route) => Emit("nav.request", new { route });

    private async Task SendAsync(string json)
    {
        var sink = ToWeb;
        if (sink is null) { App.WriteLog("WebBridge.SendAsync -> 没有挂发送通道，丢掉一条消息"); return; }
        try { await sink(json); }
        catch (Exception ex) { App.WriteLog("WebBridge.SendAsync -> " + ex.Message); }
    }

    // ==================== settings ====================

    /// <summary>
    /// <c>settings.get</c>：前端设置页要的两块 —— 五组设置值 + 「上次的存档」摘要。
    /// 字段名与 <c>web/src/bridge/protocol.ts</c> 的 <c>SettingsValues</c> / <c>SaveSummary</c> 一一对应。
    /// 不搬任何密钥/令牌字段（AiKey、VoiceApiKey、CloudToken…）——桥不搬秘密。
    /// </summary>
    private async Task<object?> SettingsGetAsync()
        => new { values = SettingsValues(), save = await SaveSummaryAsync() };

    private object SettingsValues()
    {
        var s = _settings.Current;
        return new
        {
            glass = GlassPalette(),
            text = new { scale = s.TextSpeed },
            sound = new { enabled = s.UiSoundEnabled },
            story = new { autoSave = s.AutoSaveEnabled },
            language = new { locale = LocaleOf(s.Lang) },
        };
    }

    /// <summary>标题页「上次的存档」：取最近一份存档的元信息；没有存档就是 null（前端走空状态）。</summary>
    private async Task<object?> SaveSummaryAsync()
    {
        try
        {
            var slots = await _saves.List();
            var slot = slots.FirstOrDefault();
            if (slot is null) return null;
            return new
            {
                character = slot.Character,
                scene = slot.Scene,
                savedAt = slot.SavedAt.ToUniversalTime().ToString("o"),
                minutes = slot.PlayMinutes,
            };
        }
        catch (Exception ex)
        {
            App.WriteLog("WebBridge.SaveSummary -> " + ex.Message);
            return null;
        }
    }

    /// <summary>
    /// <c>settings.set</c>：params 是 <c>{key, value}</c>（前端协议按 key 收窄）。
    /// 写回走既有的「下发 → 落盘」两段：<see cref="SettingsManager.Apply"/> 会触发 RuntimeConfigurator
    /// 等全部订阅方，与设置页那条路完全一致。
    /// </summary>
    private async Task<object?> SettingsSetAsync(JsonElement p)
    {
        if (p.ValueKind != JsonValueKind.Object
            || !p.TryGetProperty("key", out var keyEl) || keyEl.ValueKind != JsonValueKind.String
            || !p.TryGetProperty("value", out var valueEl))
            throw new WebBridgeException("E_BAD_PARAMS", "settings.set 需要 {key, value}。");

        var key = keyEl.GetString() ?? "";
        var s = _settings.Current;
        var langChanged = false;
        switch (key)
        {
            case "glass":
            {
                var g = ReadGlass(valueEl);
                s = s with
                {
                    GlassTranslucency = g.Alpha,
                    GlassFrost = g.Frost,
                    GlassHeight = g.Liquid,
                    PerformanceMode = g.Lightweight,
                };
                break;
            }
            case "text.scale":
                s = s with { TextSpeed = Math.Clamp(Number(valueEl, key), 0.5, 2) };
                break;
            case "sound.enabled":
                s = s with { UiSoundEnabled = Boolean(valueEl, key) };
                break;
            case "story.autoSave":
                s = s with { AutoSaveEnabled = Boolean(valueEl, key) };
                break;
            case "language.locale":
            {
                var locale = valueEl.ValueKind == JsonValueKind.String ? valueEl.GetString() ?? "" : "";
                if (locale is not ("zh-CN" or "en-US"))
                    throw new WebBridgeException("E_BAD_PARAMS", "language.locale 只支持 zh-CN / en-US。");
                langChanged = !string.Equals(locale, s.Lang, StringComparison.Ordinal);
                s = s with { Lang = locale };
                break;
            }
            default:
                throw new WebBridgeException("E_BAD_PARAMS", "unknown setting key: " + key);
        }

        _settings.Apply(s);
        if (langChanged) LocalizationService.Current.SetCulture(s.Lang);
        await _settings.Persist();
        var values = SettingsValues();
        Emit("settings.changed", new { values });
        return new { values };
    }

    private static string LocaleOf(string? lang)
        => string.Equals(lang, "en-US", StringComparison.OrdinalIgnoreCase) ? "en-US" : "zh-CN";

    // ==================== palette（玻璃三档 + 轻量）====================

    /// <summary>前端那份 <c>GlassPalette</c>：三个 0..1 的滑杆值 + 轻量开关，全部来自当前设置。</summary>
    private object GlassPalette()
    {
        var s = _settings.Current;
        return new
        {
            alpha = s.GlassTranslucency,
            frost = s.GlassFrost,
            liquid = s.GlassHeight,
            lightweight = s.PerformanceMode,
        };
    }

    private static (double Alpha, double Frost, double Liquid, bool Lightweight) ReadGlass(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object)
            throw new WebBridgeException("E_BAD_PARAMS", "glass 的值必须是 {alpha, frost, liquid, lightweight}。");
        var alpha = value.TryGetProperty("alpha", out var a) ? Number(a, "glass.alpha") : 0.85;
        var frost = value.TryGetProperty("frost", out var f) ? Number(f, "glass.frost") : 0.5;
        var liquid = value.TryGetProperty("liquid", out var l) ? Number(l, "glass.liquid") : 0.4;
        var lightweight = value.TryGetProperty("lightweight", out var g) && Boolean(g, "glass.lightweight");
        return (Math.Clamp(alpha, 0, 1), Math.Clamp(frost, 0, 1), Math.Clamp(liquid, 0, 1), lightweight);
    }

    private object PaletteGet() => new { palette = GlassPalette() };

    private async Task<object?> PaletteSetAsync(JsonElement p)
    {
        if (p.ValueKind != JsonValueKind.Object || !p.TryGetProperty("palette", out var paletteEl))
            throw new WebBridgeException("E_BAD_PARAMS", "palette.set 需要 {palette}。");

        var g = ReadGlass(paletteEl);
        _settings.Apply(_settings.Current with
        {
            GlassTranslucency = g.Alpha,
            GlassFrost = g.Frost,
            GlassHeight = g.Liquid,
            PerformanceMode = g.Lightweight,
        });
        await _settings.Persist();

        var result = new { palette = GlassPalette() };
        Emit("palette.changed", result);
        return result;
    }

    // ==================== nav ====================

    /// <summary>
    /// <c>nav.open</c>：前端每次路由变化都会报一次，像是「我到了这个路由」。
    /// 宿主据此对齐自己的返回栈/任务栏（见 <see cref="CurrentRoute"/>），并回答 <c>{accepted:true, mode}</c>：
    ///  · <c>web</c>      —— 前端正挂在宿主上（<see cref="WebFrontendReady"/>）：路由归前端，宿主照单记下；
    ///  · <c>shell</c>    —— 宿主窗口是 Shell（原生界面形态）：真去 GoToAsync；
    ///  · <c>deferred</c> —— 两种都没有：记进 <see cref="PendingRoute"/>，路由暂时没人接。
    /// </summary>
    private object? NavOpen(JsonElement p)
    {
        if (p.ValueKind != JsonValueKind.Object || !p.TryGetProperty("route", out var routeEl) || routeEl.ValueKind != JsonValueKind.String)
            throw new WebBridgeException("E_BAD_PARAMS", "nav.open 需要 params.route。");

        var route = (routeEl.GetString() ?? "").Trim();
        if (route.Length == 0) throw new WebBridgeException("E_BAD_PARAMS", "nav.open 的 route 不能为空。");

        CurrentRoute = route;
        if (WebFrontendReady)
        {
            PendingRoute = null;
            return new { accepted = true, route, mode = "web" };
        }

        var shell = Shell.Current;
        if (shell is null)
        {
            PendingRoute = route;
            App.WriteLog("WebBridge.nav.open -> 无宿主导航，记下待接手路由 " + route);
            return new { accepted = false, route, mode = "deferred" };
        }

        // Shell 形态下由宿主真的带路（前端此时不在屏上，不需要再发 nav.request）。
        _ = GoToAsync(shell, route);
        return new { accepted = true, route, mode = "shell" };
    }

    private static async Task GoToAsync(Shell shell, string route)
    {
        try { await shell.GoToAsync(route); }
        catch (Exception ex) { App.WriteLog("WebBridge.GoToAsync " + route + " -> " + ex.Message); }
    }

    /// <summary>最近一次没地方可跳的路由（宿主既不是 Shell、也没有前端在跑）。留给正式接线时消费。</summary>
    public string? PendingRoute { get; private set; }

    /// <summary>
    /// <c>nav.back</c>：前端自己退完页之后报一声（它已经调过 back()）。
    /// 宿主只回答 <c>{accepted:true, mode}</c>；Shell 形态下顺带退一层栈。
    /// </summary>
    private object? NavBack(JsonElement p)
    {
        var from = p.ValueKind == JsonValueKind.Object && p.TryGetProperty("from", out var f) && f.ValueKind == JsonValueKind.String
            ? f.GetString()
            : null;

        if (WebFrontendReady)
        {
            CurrentRoute = from;
            return new { accepted = true, mode = "web" };
        }

        var shell = Shell.Current;
        if (shell is null) return new { accepted = false, mode = "deferred" };
        if (shell.Navigation.NavigationStack.Count > 1) _ = GoToAsync(shell, "..");
        return new { accepted = true, mode = "shell" };
    }

    // ==================== ui ====================

    /// <summary><c>ui.tokens</c> 的返回：<c>{ tokens, theme }</c>，与前端 <c>Requests['ui.tokens']</c> 对齐。</summary>
    private object TokensPayload() => new { tokens = Tokens(), theme = IsDark() ? "dark" : "light" };

    /// <summary><c>ui.tokens</c> 的同一份令牌。页面在主题变化时直接推给前端，省一次往返。</summary>
    public object TokensSnapshot() => Tokens();

    /// <summary>
    /// 把<strong>正在生效</strong>的设计令牌翻成前端那份词汇表（<c>--w-*</c>）。
    ///
    /// 全部取自 <see cref="Application.Current"/>.Resources 与 <see cref="MotionTokens"/>；
    /// 需要透明度的档位由同一个颜色算 alpha（rgba），不引入第二种色值。
    /// 前端自己从这几块推墨阶与玻璃（<c>lib/ink.ts</c>）：<c>--w-surface-tint</c>、<c>--w-signal</c>、
    /// <c>--w-ink-seed</c>、<c>--w-ink-signal-seed</c> 是它真正要的四个种子，其余是字阶/间距/时长。
    /// </summary>
    private JsonObject Tokens()
    {
        var res = Application.Current?.Resources;
        var tokens = new JsonObject();

        void Set(string name, string? value)
        {
            if (!string.IsNullOrEmpty(value)) tokens[name] = value;
        }

        string? Color(string key) => res?.TryGetValue(key, out var v) == true && v is Color c ? CssColor(c) : null;
        string? WithAlpha(string key, double alpha)
            => res?.TryGetValue(key, out var v) == true && v is Color c ? Rgba(c, alpha) : null;
        string? Length(string key) => res?.TryGetValue(key, out var v) == true ? CssLength(v) : null;
        string? FirstOfThickness(string key)
            => res?.TryGetValue(key, out var v) == true && v is Thickness t ? Px(t.Left) : null;

        // 字体：MAUI 用系统 UI 字体，这里给同一条系统栈（不是配色，也不是第二份字阶）
        const string fontStack =
            "'Segoe UI Variable Text', 'Segoe UI', 'PingFang SC', 'Microsoft YaHei UI', 'Noto Sans SC', system-ui, sans-serif";
        Set("--w-font-ui", fontStack);
        Set("--w-font-num", fontStack);

        // 字阶 / 间距 / 圆角 / 行高
        Set("--w-size-display", Length("WSizeDisplay"));
        Set("--w-size-title", Length("WSizeTitle"));
        Set("--w-size-subhead", Length("WSizeSubhead"));
        Set("--w-size-body", Length("WSizeBody"));
        Set("--w-size-small", Length("WSizeSmall"));
        Set("--w-size-caption", Length("WSizeCaption"));
        Set("--w-space-1", Length("WSpaceTight"));
        Set("--w-space-2", Length("WSpaceRegular"));
        Set("--w-space-3", Length("WSpaceLoose"));
        Set("--w-space-4", Length("WSpaceSection"));
        Set("--w-pad-card", FirstOfThickness("WPadCard"));
        Set("--w-pad-page", FirstOfThickness("WPageMargin"));
        Set("--w-row-h", Length("WRowHeight"));
        Set("--w-header-h", Length("WHeaderHeight"));
        Set("--w-radius-well", Length("WRadiusWell"));
        Set("--w-radius-card", Length("WRadiusCard"));
        Set("--w-radius-button", Length("WRadiusButton"));

        // 动效：时长与曲线都取 MotionTokens；设计系统只有一条曲线（Spring），两个槽位都用它
        Set("--w-dur-press", MotionTokens.TapMs + "ms");
        Set("--w-dur-state", MotionTokens.SettleMs + "ms");
        Set("--w-dur-page", MotionTokens.PageSweepMs + "ms");
        var spring = string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"cubic-bezier({MotionTokens.SpringX1}, {MotionTokens.SpringY1}, {MotionTokens.SpringX2}, {MotionTokens.SpringY2})");
        Set("--w-ease-out", spring);
        Set("--w-ease-settle", spring);

        // 色：地面 / 面 / 墨 / 信号 / 边界
        Set("--w-bg", Color("PageBg"));
        Set("--w-surface-tint", Color("SurfaceBg"));
        Set("--w-ink-seed", Color("WarmDark"));
        Set("--w-ink-signal-seed", Color("AccentWarmFg"));
        Set("--w-signal", Color("AccentWarm"));
        Set("--w-signal-hi", Color("PrimaryHover"));
        Set("--w-signal-lo", Color("BeigeAccent200"));
        Set("--w-signal-edge", WithAlpha("AccentWarm", 0.5));
        Set("--w-signal-glow", WithAlpha("AccentWarm", 0.24));
        Set("--w-hairline", Color("BorderLine"));
        Set("--w-edge", Color("EdgeStrong"));
        Set("--w-focus-ring", Color("Signal"));

        //  tints：都从玻璃色标 / 墨阶推透明度，不另选色
        Set("--w-tint-card", WithAlpha("GTintMid", 0.05));
        Set("--w-tint-bar", WithAlpha("GTintTop", 0.04));
        Set("--w-tint-button", WithAlpha("GTintMid", 0.08));
        Set("--w-tint-signal", WithAlpha("AccentWarm", 0.90));
        Set("--w-tint-track", WithAlpha("MutedFg", 0.16));
        Set("--w-tint-knob", WithAlpha("SurfaceFg", 0.12));
        Set("--w-tint-thumb", WithAlpha("SurfaceFg", 0.06));

        // 地面渐变的三档：面 → 页 → 井（同一套令牌的明度台阶）
        Set("--w-ground-a", Color("SurfaceBg"));
        Set("--w-ground-b", Color("PageBg"));
        Set("--w-ground-c", Color("WellBg"));
        Set("--w-ground-warm", WithAlpha("AccentWarm", 0.20));
        Set("--w-ground-warm-mid", WithAlpha("AccentWarm", 0.10));
        Set("--w-ground-warm-end", WithAlpha("AccentWarm", 0));
        Set("--w-ground-cool", WithAlpha("Signal", 0.26));
        Set("--w-ground-cool-mid", WithAlpha("Signal", 0.09));
        Set("--w-ground-cool-end", WithAlpha("Signal", 0));
        Set("--w-ground-light", WithAlpha("GEdgeHi", 0.18));
        Set("--w-ground-light-end", WithAlpha("GEdgeHi", 0));
        Set("--w-vignette", Color("OverlayDim"));

        if (!_tokensLogged)
        {
            _tokensLogged = true;
            App.WriteLogNow($"WebBridge.ui.tokens -> {tokens.Count} 个 --w-* 键: {string.Join(" ", tokens.Select(kv => kv.Key))}");
        }
        return tokens;
    }

    /// <summary>键集合只记一次日志：每次换主题都写一整行会把启动日志淹掉。</summary>
    private bool _tokensLogged;

    private static bool IsDark()
        => ThemeManager.Current?.Dark ?? Application.Current?.RequestedTheme == AppTheme.Dark;

    /// <summary>MAUI 的尺寸令牌有四种壳（double/int/Thickness/RoundRectangle），统一折成 CSS 的 px。</summary>
    private static string? CssLength(object? v) => v switch
    {
        double d when double.IsFinite(d) => Px(d),
        float f when float.IsFinite(f) => Px(f),
        int i => Px(i),
        Thickness t => $"{Px(t.Top)} {Px(t.Right)} {Px(t.Bottom)} {Px(t.Left)}",
        CornerRadius cr => Px(cr.TopLeft),
        Microsoft.Maui.Controls.Shapes.RoundRectangle rr => Px(rr.CornerRadius.TopLeft),
        _ => null,
    };

    private static string Px(double d)
        => Math.Round(d, 2).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "px";

    /// <summary>Color → <c>#rrggbb</c> / <c>rgba(r, g, b, a)</c>。带 alpha 的用 rgba，否则十六进制更好读。</summary>
    private static string CssColor(Color c)
    {
        if (c.Alpha >= 0.999f)
            return $"#{ToByte(c.Red):x2}{ToByte(c.Green):x2}{ToByte(c.Blue):x2}";
        return Rgba(c, c.Alpha);
    }

    /// <summary>同一个颜色换一个 alpha：令牌里那些「同一颗色的半透明档」都这么做，不另选色值。</summary>
    private static string Rgba(Color c, double alpha)
    {
        var a = Math.Round(Math.Clamp(alpha, 0, 1), 3).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        return $"rgba({ToByte(c.Red)}, {ToByte(c.Green)}, {ToByte(c.Blue)}, {a})";
    }

    private static int ToByte(float channel) => (int)Math.Round(Math.Clamp(channel, 0f, 1f) * 255);

    // ==================== app ====================

    /// <summary>
    /// <c>app.info</c>（前端协议之外的补充）：它在启动时问「我在哪、我是什么版本、内核是什么」。
    /// WebView2 版本走既有的注册表探测（不启动进程、不联网）；其余平台为空串。
    /// </summary>
    private static object AppInfoPayload()
    {
        var devUrl = Environment.GetEnvironmentVariable("WARM_WEB_URL")?.Trim();
        return new
        {
            name = AppInfo.Current.Name,
            version = AppInfo.Current.VersionString,
            build = AppInfo.Current.BuildString,
            platform = DeviceInfo.Current.Platform.ToString(),
            kind = DeviceInfo.Current.Idiom.ToString(),
            webView = new
            {
                engine = OperatingSystem.IsWindows() ? "webview2"
                    : OperatingSystem.IsAndroid() ? "android-webview"
                    : "unknown",
                version = RuntimeProbe.WebView2Present() ?? "",
            },
            dev = string.IsNullOrEmpty(devUrl) ? DevUrl : devUrl,
            debug = IsDebugBuild,
        };
    }

    private static bool IsDebugBuild
    {
        get
        {
#if DEBUG
            return true;
#else
            return false;
#endif
        }
    }

    // ==================== 参数读取 ====================

    private static double Number(JsonElement v, string what)
        => v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d) && double.IsFinite(d)
            ? d
            : throw new WebBridgeException("E_BAD_PARAMS", $"{what} 必须是数字。");

    private static bool Boolean(JsonElement v, string what) => v.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => throw new WebBridgeException("E_BAD_PARAMS", $"{what} 必须是布尔值。"),
    };
}

/// <summary>桥方法里「这条路走不通」的专用异常：<see cref="Code"/> 原样回给前端，不打完整堆栈。</summary>
public sealed class WebBridgeException : Exception
{
    public string Code { get; }

    public WebBridgeException(string code, string message) : base(message) => Code = code;
}
