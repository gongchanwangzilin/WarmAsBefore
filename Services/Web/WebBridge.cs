using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Maui.ApplicationModel.DataTransfer;
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
    private readonly CharacterLibrary _characters;
    private readonly PageWindowService _windows;
    private readonly PluginHost _plugins;
    private readonly WorldTreeService _worlds;
    private readonly MemoryTagService _tags;
    private readonly Modules.Showcase.ShowcaseStore _showcases;
    private readonly NovelLibrary _novels;
    private readonly GameEngine _engine;
    private readonly Modules.AiChat.ChatEngine _chat;
    private readonly Modules.RealChat.OfficialChatBridge _official;
    private readonly PhoneMessageService _phone;
    private readonly WeChatService _wechat;
    private readonly StickerLibrary _stickers;
    private readonly Modules.Tools.ToolManager _tools;
    private readonly Modules.MinecraftBridge.MaidBridgeService _maid;
    private readonly PersonaMemoryService _persona;
    /// <summary>统一背景服务：天气场景（Kind + 强度）是它的运行时值，前端天气层只认这一份。</summary>
    private readonly BackgroundService _background;
    /// <summary>语音：识别走这台设备已有的引擎与麦克风（与主界面语音按钮同一条路），朗读同理。</summary>
    private readonly SpeechService _speech;

    /// <summary>一次 <c>voice.listen</c> 的取消令牌：<c>voice.cancel</c> 与它接的正是引擎自己的 StopListening。</summary>
    private CancellationTokenSource? _voiceCts;

    /// <summary>串行闸：settings.set 是「读→改→落盘」，两条消息挤在一起会互相覆盖。</summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// 活着的桥（一个 WebView 一条）。提示、数据变更这类「所有窗都该知道」的消息要推给全部，
    /// 不能只推最后挂上的那扇 —— 否则主窗弹的提示在手机窗里看不见。
    /// </summary>
    private static readonly List<WebBridge> Live = new();

    /// <summary>还有 Web 宿主在跑：原生提示控件据此让位，改由前端自己弹。</summary>
    public static bool AnyLive { get { lock (Live) return Live.Count > 0; } }

    /// <summary>把一条事件推给所有活着的 Web 宿主。</summary>
    public static void EmitAll(string name, object? payload)
    {
        WebBridge[] all;
        lock (Live) all = Live.ToArray();
        foreach (var b in all)
        {
            try { b.Emit(name, payload); }
            catch (Exception ex) { App.WriteLog("WebBridge.EmitAll -> " + ex.Message); }
        }
    }

    public WebBridge()
    {
        lock (Live) Live.Add(this);
    }

    public WebBridge(SettingsManager settings, SaveManager saves, CharacterLibrary characters, PageWindowService windows,
        PluginHost plugins, WorldTreeService worlds, MemoryTagService tags,
        Modules.Showcase.ShowcaseStore showcases, NovelLibrary novels,
        GameEngine engine, Modules.AiChat.ChatEngine chat, Modules.RealChat.OfficialChatBridge official,
        PhoneMessageService phone, WeChatService wechat, StickerLibrary stickers,
        Modules.Tools.ToolManager tools, Modules.MinecraftBridge.MaidBridgeService maid,
        PersonaMemoryService persona, BackgroundService background, SpeechService speech) : this()
    {
        _settings = settings;
        _saves = saves;
        _characters = characters;
        _windows = windows;
        _plugins = plugins;
        _worlds = worlds;
        _tags = tags;
        _showcases = showcases;
        _novels = novels;
        _engine = engine;
        _chat = chat;
        _official = official;
        _phone = phone;
        _wechat = wechat;
        _stickers = stickers;
        _tools = tools;
        _maid = maid;
        _persona = persona;
        _background = background;
        _speech = speech;

        // 天气轮询改的是背景服务里的场景：它一变，整份外观就重新推一次，
        // 前端天气层（web/src/lib/atmosphere.ts）据此换雨/雪/尘，不用自己猜。
        _background.Changed += _ => EmitAll("appearance.changed", new { appearance = AppearanceOf(_settings.Current) });
    }

    /// <summary>宿主 → 前端的发送通道；页面拿到 WebView 后挂上（见 WebShellPage）。</summary>
    /// <summary>
    /// 发往前端的通道。一扇窗挂一条 —— 手机那扇独立窗与主窗是两个 WebView，
    /// 只有一个 ToWeb 时后挂的会把先挂的顶掉，于是「主窗改设置、手机窗不变」。
    /// </summary>
    private readonly List<Func<string, Task>> _sinks = new();

    /// <summary>还没挂上前端时攒下的事件（启动期的提示就是这样丢的：宿主比 WebView 起得早）。</summary>
    private static readonly List<(string name, object? payload)> Pending = new();

    public void AttachSink(Func<string, Task> sink)
    {
        lock (_sinks) if (!_sinks.Contains(sink)) _sinks.Add(sink);
    }

    /// <summary>
    /// 前端挂好了（React 挂载后才发 `ui.ready`）：这时补发启动期攒下的事件。
    /// 不能在 AttachSink 里补 —— 那时 WebView 还没加载，发出去就丢了，
    /// 启动提示于是「原生那条被让位、前端那条也没来」。
    /// </summary>
    public static void FlushPending()
    {
        (string name, object? payload)[] backlog;
        lock (Pending) { backlog = Pending.ToArray(); Pending.Clear(); }
        App.WriteLog($"WebBridge.FlushPending -> {backlog.Length} 条");
        foreach (var (name, payload) in backlog)
        {
            try { EmitAll(name, payload); }
            catch (Exception ex) { App.WriteLog("WebBridge.flush -> " + ex.Message); }
        }
    }

    /// <summary>有窗就广播，没窗就先攒着（第一个窗挂上时补发）。</summary>
    public static void EmitOrQueue(string name, object? payload)
    {
        if (AnyLive) { EmitAll(name, payload); return; }
        lock (Pending) Pending.Add((name, payload));
        App.WriteLog($"WebBridge.queue {name}（暂无窗）");
    }

    public void DetachSink(Func<string, Task> sink)
    {
        lock (_sinks) _sinks.Remove(sink);
    }

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
                // chat.send 要等一整轮模型回复（常常几十秒）。占着闸的话，别扇窗的存档、设置、
                // 令牌全排在它后面 —— 主窗一边聊天，手机窗就整片「没反应」。
                var gated = !BypassesGate(method);
                if (gated) await _gate.WaitAsync();
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
                    if (gated) _gate.Release();
                }
            });
        }
        catch (Exception ex)
        {
            // 走到这里说明连 UI 线程都投递不进去（进程正在退出）：记一笔，不再往上抛。
            App.WriteLog("WebBridge.ReceiveAsync -> " + ex);
        }
    }

    private static bool BypassesGate(string? method) => method is "chat.send" or "voice.listen" or "voice.speak";

    /// <summary>方法白名单。新增方法只在这里加一行，前端拿不到别的任何入口。</summary>
    private async Task<object?> DispatchAsync(string? method, JsonElement p) => method switch
    {
        "ui.tokens" => TokensPayload(),
        "ui.ready" => WebReady(),
        "settings.get" => await SettingsGetAsync(),
        "settings.set" => await SettingsSetAsync(p),
        "settings.patch" => await SettingsPatchAsync(p),
        "appearance.get" => AppearanceGet(),
        "appearance.set" => await AppearanceSetAsync(p),
        "palette.get" => PaletteGet(),
        "palette.swatches" => PaletteSwatches(),
        "palette.set" => await PaletteSetAsync(p),
        "nav.open" => NavOpen(p),
        "nav.back" => NavBack(p),
        "window.open" => WindowOpen(p),
        "window.close" => WindowClose(),
        "saves.list" => await SavesListAsync(),
        "save.load" => await SaveLoadAsync(p),
        "save.new" => await SaveNewAsync(p),
        "save.rename" => await SaveRenameAsync(p),
        "save.delete" => await SaveDeleteAsync(p),
        "save.rules.get" => await SaveRulesGetAsync(p),
        "save.rules.set" => await SaveRulesSetAsync(p),
        "chat.contacts" => await ChatContactsAsync(p),
        "chat.history" => await ChatHistoryAsync(p),
        "chat.send" => await ChatSendAsync(p),
        "characters.list" => await CharactersListAsync(),
        "characters.get" => CharactersGet(p),
        "characters.importAvatar" => await CharactersImportAvatarAsync(p),
        "character.create" => await CharacterCreateAsync(p),
        "character.save" => await CharacterSaveAsync(p),
        "character.delete" => await CharacterDeleteAsync(p),
        "character.export" => await CharacterExportAsync(p),
        "ai.providers" => AiProviders(),
        "plugins.list" => await PluginsListAsync(),
        "worldbook.list" => await WorldbookListAsync(),
        "memory.tags" => await MemoryTagsAsync(p),
        "showcases.list" => await ShowcasesListAsync(),
        "novels.list" => await NovelsListAsync(),
        "app.info" => AppInfoJson(),
        "voice.status" => VoiceStatusOf(),
        "voice.listen" => await VoiceListenAsync(p),
        "voice.cancel" => VoiceCancel(),
        "voice.speak" => await VoiceSpeakAsync(p),
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
        // 前端还没挂好就别发：那条 WebView 里的消息通道此时是空的，
        // 发出去等于丢（启动提示就是这么丢的）。攒着，等 ui.ready 一起补发。
        if (!WebFrontendReady)
        {
            lock (Pending) Pending.Add((name, payload));
            return;
        }

        var envelope = new JsonObject
        {
            ["event"] = name,
            ["payload"] = payload is null ? new JsonObject() : JsonSerializer.SerializeToNode(payload),
        };
        if (name == "ui.toast")
        {
            int sinks;
            lock (_sinks) sinks = _sinks.Count;
            App.WriteLog($"WebBridge.emit {name} -> sinks={sinks} ready={WebFrontendReady}");
        }
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
        Func<string, Task>[] sinks;
        lock (_sinks) sinks = _sinks.ToArray();
        if (sinks.Length == 0) { App.WriteLog("WebBridge.SendAsync -> 没有挂发送通道，丢掉一条消息"); return; }
        foreach (var sink in sinks)
        {
            try { await sink(json); }
            catch (Exception ex) { App.WriteLog("WebBridge.SendAsync -> " + ex.Message); }
        }
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
        var providers = s.AiProviders.Count(p => p.Enabled);
        return new
        {
            appearance = new
            {
                liquid = s.LiquidEnabled,
                glass = s.GlassEnabled,
                frost = s.FrostEnabled,
                theme = string.IsNullOrWhiteSpace(s.ThemeName) ? "endfield" : s.ThemeName,
            },
            sound = new { bgm = s.BgmLevel, sfx = s.SfxLevel },
            text = new { scale = s.TextSpeed },
            story = new { autoSave = s.AutoSaveEnabled },
            language = new { locale = LocaleOf(s.Lang) },
            ai = new
            {
                providers,
                smartSaving = s.SmartSavingEnabled,
                timeoutSeconds = s.AiTimeoutSeconds,
                think = new
                {
                    mode = Services.Core.ThinkTierRuntime.NormalizeMode(s.ThinkMode),
                    bar = s.ThinkBarEnabled,
                    quick = s.QuickButtonsEnabled,
                    tiers = Math.Clamp(s.ThinkTierCount, 1, 5),
                    labels = s.ThinkTierLabels,
                    paramKey = s.ThinkParamKey,
                    paramValues = s.ThinkParamValues,
                },
            },
            memory = new
            {
                windowDays = s.MemoryWindowDays,
                contextLimit = s.ContextTokenLimit >= 1024 ? $"{s.ContextTokenLimit / 1024}K" : s.ContextTokenLimit.ToString(),
            },
            ui = new
            {
                orientation = s.ScreenOrientation,
                chatStyle = s.ChatStyle,
                difficulty = s.GameDifficulty,
            },
            platform = new { petIdleMinutes = s.PetIdleMinutes, preferDiscreteGpu = s.PreferDiscreteGpu },
            integrations = new { qq = s.QqBotEnabled, wechat = s.WechatEnabled, mcp = s.McpEnabled },
            life = new
            {
                notifications = s.NotificationsEnabled,
                weatherCity = s.WeatherCity,
                customWeatherSource = string.Equals(s.WeatherSource, "custom", StringComparison.OrdinalIgnoreCase),
            },
            data = new { skillsInstalled = Directory.Exists(Path.Combine(AppContext.BaseDirectory, "skills")) },
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
                id = slot.Id,
                label = slot.Label,
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

    /// <summary><c>characters.get?id</c>：一页角色卡要的全部东西（档案 + 状态 + 立绘清单）。</summary>
    private object? CharactersGet(JsonElement p)
    {
        var id = p.ValueKind == JsonValueKind.Object && p.TryGetProperty("id", out var el) ? el.GetString() ?? "" : "";
        if (string.IsNullOrWhiteSpace(id)) throw new WebBridgeException("E_BAD_PARAMS", "characters.get 需要 {id}。");

        var all = _characters.ListAsync().GetAwaiter().GetResult();
        var c = all.FirstOrDefault(x => x.Profile.Id == id);
        if (c is null) return null;

        return new
        {
            id = c.Profile.Id,
            name = c.Profile.Name,
            nickname = c.Profile.Nickname,
            gender = c.Profile.Gender,
            age = c.Profile.Age,
            catchphrase = c.Profile.Catchphrase,
            avatarUrl = AssetUrl(c.Avatar),
            outfit = ResolveLook(c).outfit,
            emotion = ResolveLook(c).emotion,
            affection = c.State.Affection,
            trust = c.State.Trust,
            energy = c.State.Energy,
            security = SecurityOf(c),
            emotions = c.SpriteMap.Keys
                .Select(k => k.Split('/'))
                .Where(parts => parts.Length > 1)
                .Select(parts => new { outfit = parts[0], emotion = parts[1], url = AssetUrl(c.SpriteMap[string.Join('/', parts)]) })
                .ToList(),
        };
    }

    /// <summary>
    /// <c>characters.importAvatar</c>：点头像就是「换一张图」。宿主弹系统的选图框（前端没有文件系统的门），
    /// 选完拷进角色的 avatar 目录并写回 <c>Avatar</c>，再把新地址回给前端。
    /// 取消返回 <c>canceled=true</c>，不弹错误 —— 用户点掉了不算失败。
    /// </summary>
    private async Task<object?> CharactersImportAvatarAsync(JsonElement p)
    {
        var id = p.ValueKind == JsonValueKind.Object && p.TryGetProperty("id", out var el) ? el.GetString() ?? "" : "";
        if (string.IsNullOrWhiteSpace(id)) throw new WebBridgeException("E_BAD_PARAMS", "characters.importAvatar 需要 {id}。");

        var pick = await MainThread.InvokeOnMainThreadAsync(async () => await FilePicker.Default.PickAsync(
            new PickOptions { PickerTitle = "选一张图片当头像", FileTypes = FilePickerFileType.Images }));
        if (pick is null) return new { canceled = true, avatarUrl = "" };

        var rel = await _characters.SetAvatarFromFileAsync(id, pick.FullPath);
        if (rel is null) throw new WebBridgeException("E_BACKEND", "这张图没能写成头像（角色不存在或格式不支持）。");
        Emit("data.changed", new { what = "characters" });
        return new { canceled = false, avatarUrl = AssetUrl(rel) };
    }

    // ==================== 角色写盘：建 / 存 / 删 / 导出 ====================

    /// <summary>
    /// <c>character.create</c>：新建一个角色并立刻落盘。
    ///
    /// 校验与默认值照 NewCharacterViewModel.Create 那一路：名字与性别是唯一门槛
    /// （空名字进名册会在各处显示「（无名）」，空性别会一路按「未知」拼提示词，替用户猜一个不如直说）；
    /// 其余字段留空就落 <see cref="CharacterLibrary.CreateDefault"/> 的出厂值。副设定按最终的主设定重新播种。
    /// </summary>
    private async Task<object?> CharacterCreateAsync(JsonElement p)
    {
        if (p.ValueKind != JsonValueKind.Object)
            throw new WebBridgeException("E_BAD_PARAMS", "character.create 需要 {name, gender}。");

        var name = RequiredText(p, "name").Trim();
        var gender = RequiredText(p, "gender").Trim();
        if (name.Length == 0) throw new WebBridgeException("E_REJECTED", "名字不能空着：先给 TA 起个名字。");
        if (gender.Length == 0) throw new WebBridgeException("E_REJECTED", "选一个性别：称呼与周期规则都从这里来。");

        var draft = _characters.CreateDefault(name, gender, OptionalText(p, "personality"));
        var profile = draft.Profile with
        {
            Nickname = OptionalText(p, "nickname"),
            Greeting = OptionalText(p, "greeting"),
            UserAddress = Fallback(OptionalText(p, "userAddress"), draft.Profile.UserAddress),
            Description = Fallback(OptionalText(p, "description"), draft.Profile.Description),
            Birthday = CharacterAgeService.NormalizeBirthday(OptionalText(p, "birthday")),
            NaturalAging = OptionalBool(p, "naturalAging") ?? draft.Profile.NaturalAging,
            IsRomanceTarget = OptionalBool(p, "romance") ?? draft.Profile.IsRomanceTarget,
        };

        var data = draft with
        {
            Profile = profile,
            SpriteMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            SubProfile = CharacterSubProfileService.Seed(profile),
            Voice = new CharacterVoice(),
            Model = new CharacterModelBinding(),
        };

        if (!await _characters.AddAsync(data))
            throw new WebBridgeException("E_BACKEND", "角色表这次没能写进盘，这个角色没有建立。");

        Emit("data.changed", new { what = "characters" });
        return new { id = profile.Id, name = profile.Name, gender = profile.Gender };
    }

    /// <summary>
    /// <c>character.save</c>：把工作室表单改过的字段写回角色，立即落盘。
    ///
    /// 只认 <see cref="SaveFields"/> 里那批键（工作室表单真能改的那些），按「传了哪个就写哪个」合并到盘上那一份，
    /// 没传的字段一个都不碰 —— 前端不整份覆盖，页面上没从盘上读出来的字段就不会被空值冲掉。
    /// 认不出的键一律 E_BAD_PARAMS：不猜着写，也不假装写过了。
    /// </summary>
    private async Task<object?> CharacterSaveAsync(JsonElement p)
    {
        var id = ReadId(p, "character.save");
        if (!p.TryGetProperty("fields", out var fields) || fields.ValueKind != JsonValueKind.Object)
            throw new WebBridgeException("E_BAD_PARAMS", "character.save 需要 {id, fields}。");

        var all = await _characters.ListAsync();
        var ch = all.FirstOrDefault(x => x.Profile.Id == id);
        if (ch is null) throw new WebBridgeException("E_REJECTED", "角色库里没有这个人，没有可保存的对象。");

        var profile = ch.Profile;
        var state = ch.State;
        var overrides = ch.Overrides ?? new CharacterOverrides();
        var voice = ch.Voice ?? new CharacterVoice();
        var ageTouched = false;
        var written = new List<string>();

        foreach (var kv in fields.EnumerateObject())
        {
            var key = kv.Name;
            var value = kv.Value;
            switch (key)
            {
                // ── 设定 ──────────────────────────────────────────────
                case "name":
                {
                    var v = Text(value, key).Trim();
                    if (v.Length > 0) profile = profile with { Name = v };
                    break;
                }
                case "nickname": profile = profile with { Nickname = Text(value, key).Trim() }; break;
                case "userAddress":
                {
                    var v = Text(value, key).Trim();
                    if (v.Length > 0) profile = profile with { UserAddress = v };
                    break;
                }
                case "gender":
                {
                    var v = Text(value, key).Trim();
                    if (v.Length > 0) profile = profile with { Gender = v };
                    break;
                }
                case "personality":
                {
                    var v = Text(value, key).Trim();
                    if (v.Length > 0) profile = profile with { Personality = v };
                    break;
                }
                case "description": profile = profile with { Description = Text(value, key).Trim() }; break;
                case "greeting": profile = profile with { Greeting = Text(value, key).Trim() }; break;
                case "moral": profile = profile with { MoralOutlook = Text(value, key).Trim() }; break;
                case "life": profile = profile with { LifeOutlook = Text(value, key).Trim() }; break;
                case "biography": profile = profile with { Biography = Text(value, key).Trim() }; break;
                case "political": profile = profile with { PoliticalOutlook = Text(value, key).Trim() }; break;
                case "ideology": profile = profile with { Ideology = Text(value, key).Trim() }; break;
                case "deepMemories": profile = profile with { DeepMemories = Text(value, key).Trim() }; break;
                case "trauma": profile = profile with { TraumaBackground = Text(value, key).Trim() }; break;
                case "age":
                    profile = profile with { Age = NullableInt(value, "年龄") };
                    ageTouched = true;
                    break;
                case "birthday":
                    profile = profile with { Birthday = CharacterAgeService.NormalizeBirthday(Text(value, key)) };
                    break;
                case "naturalAging": profile = profile with { NaturalAging = Boolean(value, key) }; break;
                case "romance": profile = profile with { IsRomanceTarget = Boolean(value, key) }; break;

                // ── 状态 ──────────────────────────────────────────────
                case "affection":
                    state = state with { Affection = Helpers.AffectionScale.Clamp(Percent(value, key)) };
                    break;
                case "trust": state = state with { Trust = Percent(value, key) }; break;
                case "energy": state = state with { Energy = Percent(value, key) }; break;
                case "libido": state = state with { Libido = Percent(value, key) }; break;
                case "libidoLocked":
                    state = state with
                    {
                        LibidoLock = Boolean(value, key) ? Math.Clamp(state.LibidoLock ?? state.Libido, 0, 100) : null,
                    };
                    break;
                case "libidoLockValue": state = state with { LibidoLock = Percent(value, key) }; break;
                case "mood":
                {
                    var v = Text(value, key).Trim().ToLowerInvariant();
                    state = state with { Mood = v.Length == 0 ? "normal" : v };
                    break;
                }
                case "location":
                {
                    var v = Text(value, key).Trim();
                    if (v.Length > 0) state = state with { Location = v };
                    break;
                }
                case "cycleTracked":
                    state = state with { Cycle = state.Cycle with { Tracked = Boolean(value, key) } };
                    break;
                case "cycleDay":
                {
                    var day = NullableInt(value, "周期第几天");
                    if (day is not null) state = state with { Cycle = state.Cycle with { CycleDay = Math.Max(0, day.Value) } };
                    break;
                }
                case "cyclePhase":
                {
                    var v = Text(value, key).Trim();
                    if (v.Length > 0) state = state with { Cycle = state.Cycle with { Phase = v } };
                    break;
                }

                // ── 这一个角色的 AI 参数（空 = 跟随全局设置）───────────
                case "ovMemoryTurns": overrides = overrides with { MemoryTurns = NullableInt(value, "记忆条数") }; break;
                case "ovContextLimit": overrides = overrides with { ContextTokenLimit = NullableInt(value, "上下文上限") }; break;
                case "ovTemperature": overrides = overrides with { AiTemperature = NullableDouble(value, "温度") }; break;
                case "ovWindowDays": overrides = overrides with { MemoryWindowDays = NullableDouble(value, "遗忘指数") }; break;
                case "ovRecall": overrides = overrides with { MemoryRecallRandomness = NullableDouble(value, "回忆随机度") }; break;

                // ── 这一个角色的声音 ──────────────────────────────────
                case "voiceEnabled": voice = voice with { Enabled = Boolean(value, key) }; break;
                case "voiceEngine": voice = voice with { Engine = Text(value, key).Trim() }; break;
                case "voiceName": voice = voice with { VoiceName = Text(value, key).Trim() }; break;
                case "voiceExtra": voice = voice with { Extra = Text(value, key).Trim() }; break;
                case "voiceApiUrl": voice = voice with { ApiUrl = Text(value, key).Trim() }; break;
                case "voiceApiKey": voice = voice with { ApiKey = Text(value, key).Trim() }; break;
                case "voiceApiMode": voice = voice with { ApiMode = Text(value, key).Trim() }; break;
                case "voiceTtsModel": voice = voice with { TtsModel = Text(value, key).Trim() }; break;

                default:
                    throw new WebBridgeException("E_BAD_PARAMS", "character.save 没有 " + key + " 这个字段，不敢猜着写。");
            }
            written.Add(key);
        }

        // 只有年龄真的变了（或第一次填年龄）才刷新起算日：只改生日/开关不该重置自然增长。
        if (ageTouched)
        {
            profile = profile with
            {
                AgeAsOf = profile.Age != ch.Profile.Age || (ch.Profile.AgeAsOf is null && profile.Age is not null)
                    ? DateTime.Today
                    : ch.Profile.AgeAsOf,
            };
        }

        var updated = ch with { Profile = profile, State = state, Overrides = overrides, Voice = voice };
        if (!await _characters.UpdateAsync(updated))
            throw new WebBridgeException("E_BACKEND", "保存没有落盘：角色表这次没能写进去。");

        Emit("data.changed", new { what = "characters" });
        return new { id, name = updated.Profile.Name, written };
    }

    /// <summary>
    /// <c>character.delete</c>：从名册移除、删掉立绘目录，立即落盘（<see cref="CharacterLibrary.DeleteAsync"/>）。
    /// 存档里引用过它的人不会一起删 —— 那件事要先由前端在确认框里说清楚。
    /// </summary>
    private async Task<object?> CharacterDeleteAsync(JsonElement p)
    {
        var id = ReadId(p, "character.delete");
        var (ok, message) = await _characters.DeleteAsync(id);
        if (!ok) throw new WebBridgeException("E_BACKEND", message);
        Emit("data.changed", new { what = "characters" });
        return new { id, message };
    }

    /// <summary>
    /// <c>character.export</c>：把一个人打成单角色包（<see cref="CharacterPackService"/>，与角色库那颗「导出」
    /// 同一条路），再交给系统的分享面板。包打好了但分享面板没弹出来时如实说包在哪，不谎报「已分享」。
    /// </summary>
    private async Task<object?> CharacterExportAsync(JsonElement p)
    {
        var id = ReadId(p, "character.export");

        var services = Application.Current?.Handler?.MauiContext?.Services;
        if (services?.GetService(typeof(CharacterPackService)) is not CharacterPackService packs)
            throw new WebBridgeException("E_BACKEND", "角色包服务没有注册到 DI，导出不了。");

        var all = await _characters.ListAsync();
        var ch = all.FirstOrDefault(x => x.Profile.Id == id);
        if (ch is null) throw new WebBridgeException("E_REJECTED", "角色库里没有这个人，无法导出。");

        var result = await packs.ExportAsync(ch);
        if (!result.Ok) throw new WebBridgeException("E_BACKEND", result.Message);

        var fileName = Path.GetFileName(result.ZipPath);
        var size = DataPackageService.FormatSize(result.Size);
        var shared = false;
        try
        {
            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = $"分享「{ch.Profile.Name}」的角色包",
                File = new ShareFile(result.ZipPath),
            });
            shared = true;
        }
        catch (Exception ex)
        {
            // 包已经打好了，分享面板没打开：把位置说清楚，至少能自己去取。
            App.WriteLog("WebBridge.CharacterExport share -> " + ex);
        }

        return new
        {
            fileName,
            path = result.ZipPath,
            sizeLabel = size,
            shared,
            message = shared
                ? $"角色包已生成并分享：{fileName}（{size}）"
                : $"角色包已生成，但分享面板没打开：{result.ZipPath}",
        };
    }

    /// <summary><c>ui.ready</c>：前端事件通道已挂好，把启动期攒下的提示补发出去。</summary>
    private object WebReady()
    {
        FlushPending();
        return new { ok = true };
    }

    /// <summary><c>ai.providers</c>：设置里的厂商与模型（不含任何密钥）。</summary>
    private object AiProviders()
    {
        var s = _settings.Current;
        return new
        {
            providers = s.AiProviders.Select(p => new
            {
                name = p.Name,
                endpoint = p.Url,
                enabled = p.Enabled,
                timeoutSeconds = p.TimeoutSeconds ?? s.AiTimeoutSeconds,
                keyCount = p.Keys?.Count ?? 0,
                models = p.Models.Select(m => new
                {
                    model = m.Model,
                    label = string.IsNullOrWhiteSpace(m.Label) ? m.Model : m.Label,
                    enabled = m.Enabled,
                    vision = m.SupportsVision,
                    tools = m.SupportsTools,
                    json = m.SupportsJson,
                    contextWindow = m.ContextWindow,
                }).ToList(),
            }).ToList(),
        };
    }

    /// <summary><c>plugins.list</c>：已装插件（清单 + 启停 + 缺依赖）。</summary>
    private async Task<object?> PluginsListAsync()
    {
        try
        {
            var all = await _plugins.AllAsync();
            return new
            {
                plugins = all.Select(e => new
                {
                    id = e.Manifest.Id,
                    name = e.Manifest.Name,
                    version = e.Manifest.Version,
                    description = e.Manifest.Description,
                    author = e.Manifest.Author,
                    enabled = e.Enabled,
                    permissions = e.Manifest.Permissions,
                    settingsIssue = e.SettingsIssue,
                    missing = e.MissingDependencies.Select(d => d.Id).ToList(),
                }).ToList(),
            };
        }
        catch (Exception ex)
        {
            App.WriteLog("WebBridge.PluginsList -> " + ex.Message);
            return new { plugins = Array.Empty<object>() };
        }
    }

    /// <summary><c>worldbook.list</c>：世界书（世界树）列表。</summary>
    private async Task<object?> WorldbookListAsync()
    {
        try
        {
            var worlds = await _worlds.ListAsync();
            return new
            {
                worlds = worlds.Select(w => new
                {
                    id = w.Id,
                    title = w.Title,
                    description = w.Description,
                    mode = w.Mode.ToString(),
                    characters = w.Characters.Count,
                    createdAt = w.CreatedAt.ToUniversalTime().ToString("o"),
                }).ToList(),
            };
        }
        catch (Exception ex)
        {
            App.WriteLog("WebBridge.WorldbookList -> " + ex.Message);
            return new { worlds = Array.Empty<object>() };
        }
    }

    /// <summary><c>memory.tags?charId</c>：某个角色身上的记忆标签（人/话题/事/地点/物）。</summary>
    private async Task<object?> MemoryTagsAsync(JsonElement p)
    {
        var charId = p.ValueKind == JsonValueKind.Object && p.TryGetProperty("charId", out var el) ? el.GetString() ?? "" : "";
        if (string.IsNullOrWhiteSpace(charId)) return new { tags = Array.Empty<object>() };
        try
        {
            var tags = await _tags.ListAsync(charId!);
            return new
            {
                tags = tags.Select(t => new { name = t.Name, subject = t.Subject, count = t.Count }).ToList(),
            };
        }
        catch (Exception ex)
        {
            App.WriteLog("WebBridge.MemoryTags -> " + ex.Message);
            return new { tags = Array.Empty<object>() };
        }
    }

    /// <summary><c>showcases.list</c>：开发者展示案。</summary>
    private async Task<object?> ShowcasesListAsync()
    {
        try
        {
            var all = await _showcases.LoadAsync();
            return new
            {
                showcases = all
                    .OrderByDescending(s => s.UpdatedAt)
                    .Select(s => new
                    {
                        id = s.Id,
                        name = s.Name,
                        description = s.Description,
                        characterName = s.CharacterName,
                        steps = s.Steps.Count,
                        backgroundUrl = AssetUrl(s.BackgroundPath),
                        updatedAt = s.UpdatedAt.ToUniversalTime().ToString("o"),
                    })
                    .ToList(),
            };
        }
        catch (Exception ex)
        {
            App.WriteLog("WebBridge.ShowcasesList -> " + ex.Message);
            return new { showcases = Array.Empty<object>() };
        }
    }

    /// <summary><c>novels.list</c>：平行宇宙（导入的小说世界）。</summary>
    private async Task<object?> NovelsListAsync()
    {
        try
        {
            var all = await _novels.ListAsync();
            return new
            {
                novels = all.Select(n => new
                {
                    id = n.Id,
                    title = n.Title,
                    summary = n.Summary,
                    characters = n.Characters,
                    branches = n.Branches.Count,
                    importedAt = n.ImportedAt.ToUniversalTime().ToString("o"),
                }).ToList(),
            };
        }
        catch (Exception ex)
        {
            App.WriteLog("WebBridge.NovelsList -> " + ex.Message);
            return new { novels = Array.Empty<object>() };
        }
    }

    /// <summary>素材 URL：存储根挂在 wab.assets 上，相对路径直接拼过来。空路径 = 没有素材。</summary>
    private static string AssetUrl(string? rel)
    {
        if (string.IsNullOrWhiteSpace(rel)) return "";
        var clean = rel.Replace('\\', '/').TrimStart('/');
        return $"https://{Views.Web.WebShellPage.AssetHostName}/{clean}";
    }

    /// <summary>
    /// 这台角色真正该用的服装/表情。存档里的 CurrentOutfit 可能是 default，
    /// 而形象包里只有「女仆装/偶像/校园」这类真名 —— 认不出就取第一套当默认装扮。
    /// 这就是「有的角色看着没立绘」的真因：状态里的名字对不上图，前端按状态去挑就挑空了。
    /// </summary>
    private static (string outfit, string emotion) ResolveLook(CharacterData c)
    {
        if (c.SpriteMap.Count == 0) return ("", "");
        var keys = c.SpriteMap.Keys.Select(k => k.Split('/')).Where(parts => parts.Length > 1).ToList();
        if (keys.Count == 0) return ("", "");
        var outfit = keys.Any(parts => parts[0] == c.State.CurrentOutfit)
            ? c.State.CurrentOutfit
            : keys.Select(parts => parts[0]).FirstOrDefault() ?? "";
        var emotion = keys.Any(parts => parts[0] == outfit && parts[1] == c.State.CurrentEmotion)
            ? c.State.CurrentEmotion
            : keys.Where(parts => parts[0] == outfit).Select(parts => parts[1]).FirstOrDefault() ?? "";
        return (outfit, emotion);
    }

    /// <summary>
    /// 安全感读数（0–1000，与好感同刻度）。旧档这一格是 -1（没写过）——
    /// 读档时补 600 是读档逻辑的事，这里只按同一个口径对外，别让前端拿 -1 去画。
    /// </summary>
    private static int SecurityOf(CharacterData c)
        => c.State.Security < 0 ? 600 : c.State.Security;

    /// <summary>当前该显示哪张立绘：先按「服装 + 情绪」，退到同服装任一张，再退到任意一张。</summary>
    private static string CurrentSprite(CharacterData c)
    {
        if (c.SpriteMap.Count == 0) return "";
        var (outfit, emotion) = ResolveLook(c);
        // 形象包里只有一层（键里没有「服装/表情」那种分段）时，ResolveLook 给的是空串：
        // 那不是「没有立绘」，是这张图没有服装概念 —— 直接用它。
        if (string.IsNullOrEmpty(outfit))
            return c.SpriteMap.Values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? "";
        if (c.SpriteMap.TryGetValue($"{outfit}/{emotion}", out var exact) && !string.IsNullOrWhiteSpace(exact)) return exact;
        var sameOutfit = c.SpriteMap.FirstOrDefault(kv => kv.Key.StartsWith(outfit + "/", StringComparison.Ordinal));
        if (!string.IsNullOrWhiteSpace(sameOutfit.Value)) return sameOutfit.Value;
        return c.SpriteMap.Values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? "";
    }

    // ==================== 独立窗口 ====================

    /// <summary>
    /// <c>window.open</c>：把某个路由搬进自己的窗口（手机、关系网这类「旁边摊一扇」的页）。
    /// 窗里装的是同一份前端、同一座桥 —— 所以主窗改设置，那扇窗立刻跟着变（事件是广播的）。
    /// 手机上开不了窗时回 <c>opened=false</c>，前端自己退回页内导航，不会点了没反应。
    /// </summary>
    private object WindowOpen(JsonElement p)
    {
        var route = p.ValueKind == JsonValueKind.Object && p.TryGetProperty("route", out var r) && r.ValueKind == JsonValueKind.String
            ? r.GetString() ?? ""
            : "";
        if (string.IsNullOrWhiteSpace(route))
            throw new WebBridgeException("E_BAD_PARAMS", "window.open 需要 {route}。");

        var title = p.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.String
            ? t.GetString() ?? "温暖如初"
            : "温暖如初";
        var width = p.TryGetProperty("width", out var w) ? Number(w, "width") : 420;
        var height = p.TryGetProperty("height", out var h) ? Number(h, "height") : 820;

        if (!PageWindowService.Supported) return new { opened = false, reason = "这台设备不支持多窗口" };

        try
        {
            var services = Application.Current?.Handler?.MauiContext?.Services;
            if (services?.GetService(typeof(Views.Web.WebShellPage)) is not Views.Web.WebShellPage page)
                return new { opened = false, reason = "WebShellPage 没有注册到 DI" };
            page.StartAt(route!);
            var opened = _windows.OpenPage("web:" + route, title!, page, width, height);
            return new { opened, route };
        }
        catch (Exception ex)
        {
            App.WriteLog("WebBridge.WindowOpen -> " + ex.Message);
            return new { opened = false, reason = ex.Message };
        }
    }

    /// <summary><c>window.close</c>：窗里的页面自己要收起时用（手机页的「返回」）。主窗不关。</summary>
    private object WindowClose()
    {
        try
        {
            var window = Application.Current?.Windows.FirstOrDefault(x => x.Page is Views.Web.WebShellPage p && p.IsDetachedWindow);
            if (window is not null) Application.Current!.CloseWindow(window);
        }
        catch (Exception ex) { App.WriteLog("WebBridge.WindowClose -> " + ex.Message); }
        return new { closed = true };
    }

    // ==================== 真实数据：存档 / 角色 ====================

    /// <summary><c>saves.list</c>：真的存档表（按保存时间倒序），前端存档页与标题页都用这一份。</summary>
    private async Task<object?> SavesListAsync()
    {
        try
        {
            var slots = await _saves.List();
            return new
            {
                slots = slots
                    .OrderByDescending(s => s.SavedAt)
                    .Select(s => new
                    {
                        id = s.Id,
                        label = s.Label,
                        character = s.Character,
                        scene = s.Scene,
                        savedAt = s.SavedAt.ToUniversalTime().ToString("o"),
                        minutes = s.PlayMinutes,
                        world = s.WorldName.Length > 0 ? s.WorldName : s.World,
                        parentId = s.ParentId,
                        branch = s.BranchName,
                        historyNote = s.HistoryNote,
                    })
                    .ToList(),
            };
        }
        catch (Exception ex)
        {
            App.WriteLog("WebBridge.SavesList -> " + ex.Message);
            return new { slots = Array.Empty<object>() };
        }
    }

    // ==================== 存档：读取 / 新建 / 改名 / 删除 / 权限 ====================

    /// <summary>
    /// <c>save.load</c>：真的把这一档载入当前局 —— <see cref="SaveManager.Load"/>，与存档页那颗「读取」
    /// 走同一条路（先确保角色库读进来，读档时角色状态才恢复得了）。
    ///
    /// 三种结局分开回：载入成功 / 能进但核心状态重建过（要用户确认）/ 读不出来（抛，带原话）。
    /// 这里只报告结果，导航由前端自己跳 —— 桥不改前端的路由。
    /// </summary>
    private async Task<object?> SaveLoadAsync(JsonElement p)
    {
        var id = ReadId(p, "save.load");
        await _characters.ListAsync();
        var ok = await _saves.Load(id);
        var note = _saves.LastLoadNote ?? "";
        var report = _saves.LastRepairReport;

        if (!ok)
            throw new WebBridgeException("E_REJECTED",
                string.IsNullOrWhiteSpace(note) ? "这份存档读不出来：文件不存在、为空或格式不兼容。" : note);

        Emit("data.changed", new { what = "saves" });

        // 高损确认：核心状态段（state）整段重建，先停在存档页让用户确认，不静默进游戏。
        if (report is not null && report.NeedsConfirm)
            return new { id, loaded = false, needsConfirm = true, note = report.ReceiptSummary };

        return new
        {
            id,
            loaded = true,
            needsConfirm = false,
            note = report is { NeedsReceipt: true } ? report.ReceiptSummary : note,
        };
    }

    /// <summary>
    /// <c>save.new</c>：开新的一局 —— 与 <c>NewRunViewModel.Begin</c> 的核心同一串：
    /// 重置局面 → 定主角 → 摇一套初始立绘 → 分新档号 → 把这一档的人设/世界整理成记忆 → 落盘。
    /// Commit 返回 false 就是没写进盘，不许宣布「开始了」。
    /// </summary>
    private async Task<object?> SaveNewAsync(JsonElement p)
    {
        var charId = OptionalText(p, "characterId");
        if (charId.Length == 0) throw new WebBridgeException("E_BAD_PARAMS", "save.new 需要 {characterId}：这一局要跟谁开始。");
        var worldId = OptionalText(p, "worldId");
        var worldName = OptionalText(p, "worldName");
        var persona = OptionalText(p, "persona");

        var all = await _characters.ListAsync();
        var ch = all.FirstOrDefault(c => c.Profile.Id == charId);
        if (ch is null) throw new WebBridgeException("E_REJECTED", "角色库里没有这个人，这一局开不起来。");

        // 顺序不能反：Boot 会重置整份状态，角色必须在它之后写进去。
        _engine.Boot();
        _engine.SetCharacter(charId);
        if (_engine.ActiveCharacter is { } fresh) CharacterLibrary.RandomizeSpriteState(fresh);
        _engine.State.WorldId = worldId;
        _engine.State.WorldName = worldName;
        _engine.State.UserPersona = persona;
        var id = _saves.NewRun();

        try { await _persona.InitializeAsync(_engine.State, charId); }
        catch (Exception ex) { App.WriteLog("WebBridge.save.new persona -> " + ex.Message); }

        var label = OptionalText(p, "label");
        if (label.Length == 0) label = ch.Profile.Name;
        if (!await _saves.Commit(label))
            throw new WebBridgeException("E_REJECTED", "新存档没能写进磁盘：" + SaveErrorText());

        Emit("data.changed", new { what = "saves" });
        return new { id, characterId = charId, character = ch.Profile.Name, label };
    }

    /// <summary><c>save.rename</c>：改写这一档的 meta.Label 并原子回写（<see cref="SaveManager.Rename"/>）。</summary>
    private async Task<object?> SaveRenameAsync(JsonElement p)
    {
        var id = ReadId(p, "save.rename");
        var label = OptionalText(p, "label").Trim();
        if (label.Length == 0) throw new WebBridgeException("E_BAD_PARAMS", "save.rename 需要 {label}：新的存档名。");
        if (!await _saves.Rename(id, label))
            throw new WebBridgeException("E_BACKEND", "没能改名：这份存档文件读不出来，盘上那一份没动。");
        Emit("data.changed", new { what = "saves" });
        return new { id, label };
    }

    /// <summary><c>save.delete</c>：删掉这一档（含它自己的文件名）；文件本来就不在时如实说。</summary>
    private async Task<object?> SaveDeleteAsync(JsonElement p)
    {
        var id = ReadId(p, "save.delete");
        if (!await _saves.Delete(id))
            throw new WebBridgeException("E_REJECTED", "这份存档不在盘上（可能已经被删了），没有可删的东西。");
        Emit("data.changed", new { what = "saves" });
        return new { id, deleted = true };
    }

    /// <summary>
    /// <c>save.rules.get?id</c>：这一档的插件/工具/角色白名单（<see cref="SaveManager.LoadRulesAsync"/>，
    /// 与「存档高级设置」同一份读法）。id 不给就用当前这一档；两者都没有时 E_BAD_PARAMS。
    /// </summary>
    private async Task<object?> SaveRulesGetAsync(JsonElement p)
    {
        var id = OptionalText(p, "id");
        if (id.Length == 0) id = _saves.CurrentSaveId;
        if (id.Length == 0) throw new WebBridgeException("E_BAD_PARAMS", "save.rules.get 需要 {id}：还没有当前存档可以读。");

        var rules = await _saves.LoadRulesAsync(id)
            ?? throw new WebBridgeException("E_REJECTED", "这份存档的文件读不出来，白名单无从读取。");
        var perm = rules.Permissions;
        var allowedChars = rules.AllowedCharacters;

        var slot = (await _saves.List()).FirstOrDefault(s => s.Id == id);
        var people = (await _characters.ListAsync())
            .Select(c => new { id = c.Profile.Id, name = c.Profile.Name, main = string.Equals(c.Profile.Id, slot?.Character, StringComparison.Ordinal) })
            .ToList();
        var plugins = (await _plugins.AllAsync())
            .Select(e => new { id = e.Manifest.Id, name = e.Manifest.Name, version = e.Manifest.Version, enabled = e.Enabled })
            .ToList();
        var tools = _tools.AllTools().Select(t => new { name = t.Name, description = t.Description }).ToList();

        return new
        {
            id,
            label = slot?.Label ?? "",
            character = slot?.Character ?? "",
            rules = new
            {
                allowAllPlugins = perm.AllowAllPlugins,
                allowAllTools = perm.AllowAllTools,
                allowAiInstallPlugins = perm.AllowAiInstallPlugins,
                allowedPlugins = perm.AllowedPlugins,
                allowedTools = perm.AllowedTools,
                allowAllCharacters = allowedChars.Count == 0,
                allowedCharacters = allowedChars,
            },
            characters = people,
            plugins,
            tools,
        };
    }

    /// <summary>
    /// <c>save.rules.set</c>：把白名单写进这一档（<see cref="SaveManager.SaveRulesAsync"/>）。
    /// 写回 false 就是没写进去 —— 抛，不报「已保存」。这一档正好开着时内存里的状态一起换，
    /// 否则要重进存档才生效（与 <c>SaveRulesViewModel.Save</c> 同一段）。
    /// </summary>
    private async Task<object?> SaveRulesSetAsync(JsonElement p)
    {
        var id = OptionalText(p, "id");
        if (id.Length == 0) id = _saves.CurrentSaveId;
        if (id.Length == 0) throw new WebBridgeException("E_BAD_PARAMS", "save.rules.set 需要 {id}：还没有当前存档可以写。");
        if (!p.TryGetProperty("rules", out var r) || r.ValueKind != JsonValueKind.Object)
            throw new WebBridgeException("E_BAD_PARAMS", "save.rules.set 需要 {rules:{…}}。");

        var previous = await _saves.LoadRulesAsync(id)
            ?? throw new WebBridgeException("E_REJECTED", "这份存档的文件读不出来，规则无从写起。");

        var allowAllPlugins = OptionalBool(r, "allowAllPlugins") ?? previous.Permissions.AllowAllPlugins;
        var allowAllTools = OptionalBool(r, "allowAllTools") ?? previous.Permissions.AllowAllTools;
        var allowAiInstall = OptionalBool(r, "allowAiInstallPlugins") ?? previous.Permissions.AllowAiInstallPlugins;
        var allowAllCharacters = OptionalBool(r, "allowAllCharacters") ?? (previous.AllowedCharacters.Count == 0);
        var allowedPlugins = StringList(r, "allowedPlugins") ?? previous.Permissions.AllowedPlugins;
        var allowedTools = StringList(r, "allowedTools") ?? previous.Permissions.AllowedTools;
        var allowedCharacters = allowAllCharacters ? new List<string>() : StringList(r, "allowedCharacters") ?? previous.AllowedCharacters;

        var permissions = new SavePermissions
        {
            AllowAllPlugins = allowAllPlugins,
            AllowAllTools = allowAllTools,
            AllowAiInstallPlugins = allowAiInstall,
            AllowedPlugins = allowedPlugins,
            AllowedTools = allowedTools,
            AllowedCharacters = allowedCharacters,
        };

        if (!await _saves.SaveRulesAsync(id, permissions, allowedCharacters))
            throw new WebBridgeException("E_BACKEND", "规则没能写进存档：盘上那一份读不出来或写不进去，原有规则没变。");

        if (string.Equals(_engine.CurrentSaveId, id, StringComparison.Ordinal))
        {
            _engine.State.Permissions = permissions;
            _engine.State.AllowedCharacters.Clear();
            foreach (var c in allowedCharacters) _engine.State.AllowedCharacters.Add(c);
        }

        return new
        {
            id,
            saved = true,
            rules = new
            {
                allowAllPlugins,
                allowAllTools,
                allowAiInstallPlugins = allowAiInstall,
                allowedPlugins,
                allowedTools,
                allowAllCharacters,
                allowedCharacters,
            },
        };
    }

    private static List<string>? StringList(JsonElement obj, string key)
    {
        if (!obj.TryGetProperty(key, out var el) || el.ValueKind == JsonValueKind.Null) return null;
        if (el.ValueKind != JsonValueKind.Array)
            throw new WebBridgeException("E_BAD_PARAMS", key + " 需要一串字符串。");
        return el.EnumerateArray()
            .Where(x => x.ValueKind == JsonValueKind.String)
            .Select(x => (x.GetString() ?? "").Trim())
            .Where(x => x.Length > 0)
            .ToList();
    }

    private string SaveErrorText()
        => string.IsNullOrWhiteSpace(_saves.LastCommitError) ? "磁盘没有回执。" : _saves.LastCommitError;

    // ==================== 会话：联系人 / 历史 / 发送 ====================

    /// <summary>
    /// <c>chat.contacts</c>：微信 / QQ 的会话名册。组成照 <c>WeChatViewModel.LoadContactsAsync</c>：
    /// 显式加过好友的在前，这一档其余角色补在后面；名字、头像与「最后一条」都取盘上的真数据。
    /// 一条都没有时返回空表 —— 前端照源码说「这一档还没有角色」，不编会话。
    /// </summary>
    private async Task<object?> ChatContactsAsync(JsonElement p)
    {
        var shell = ChatShellOf(p);
        var friends = await _wechat.ListAsync();
        var all = await _characters.ListAsync();

        var ids = new List<string>();
        foreach (var c in friends)
            if (!ids.Contains(c.CharacterId)) ids.Add(c.CharacterId);
        foreach (var id in _engine.Roster.Keys)
            if (!ids.Contains(id)) ids.Add(id);

        var rows = new List<object>();
        string partnerId = "", partnerName = "";
        foreach (var id in ids)
        {
            var ch = all.FirstOrDefault(x => x.Profile.Id == id);
            if (ch is null) continue;   // 好友表里指着一个角色库里没有的人：不摆一行空壳
            var c = friends.FirstOrDefault(x => x.CharacterId == id);
            rows.Add(new
            {
                id,
                name = ch.Profile.Name,
                avatarUrl = AssetUrl(ch.Avatar),
                last = string.IsNullOrWhiteSpace(c?.LastMessage) ? "还没有聊过" : c!.LastMessage,
                time = c is null ? "" : TimeLabelOf(c.LastMessageAt),
                unread = c?.Unread ?? false,
            });
            if (partnerId.Length == 0) { partnerId = id; partnerName = ch.Profile.Name; }
        }

        return new
        {
            contacts = rows,
            partner = partnerId.Length == 0 ? null : new { id = partnerId, name = partnerName },
            modeLabel = ChatModeLabel(shell),
        };
    }

    /// <summary>
    /// <c>chat.history</c>：一段会话的历史，倒着分页（<c>before</c> = 已拿到的最后一条的时间，
    /// 空 = 最新一页）。微信 / QQ 读盘上的手机消息（<see cref="PhoneMessageService.ForAsync"/>），
    /// 主界面读存档里的会话记录（<see cref="SaveManager.ChatLog"/>）。
    /// 打开会话顺手标已读 —— 与微信页进会话时同一段。
    /// </summary>
    private async Task<object?> ChatHistoryAsync(JsonElement p)
    {
        var shell = ChatShellOf(p);
        var charId = await ChatTargetOfAsync(p);
        if (charId.Length == 0)
            return new { characterId = "", messages = Array.Empty<object>(), hasMore = false, nextBefore = (string?)null };

        var limit = p.TryGetProperty("limit", out var l) && l.ValueKind == JsonValueKind.Number
            ? Math.Clamp((int)Number(l, "limit"), 1, 200)
            : 30;

        if (shell == "main")
        {
            // 主界面的会话记录本来就只有最近 80 条（AddMessage 的截断），一页给完。
            var log = _saves.ChatLog.TakeLast(80).ToList();
            var page = log.TakeLast(limit).ToList();
            var partner = await PartnerNameOfAsync(charId);
            var messages = page.Select((r, i) => (object)new
            {
                id = $"main-{log.Count - page.Count + i}",
                text = r.Text,
                mine = r.Role == "user",
                sender = r.Role == "user" ? "我" : partner,
                at = r.At.ToUniversalTime().ToString("o"),
            }).ToList();
            return new { characterId = charId, messages, hasMore = log.Count > page.Count, nextBefore = (string?)null };
        }

        await _wechat.MarkReadAsync(charId);
        var all = await _phone.ForAsync(charId);
        var cursor = BeforeCursor(p);
        var pool = cursor is null ? all.ToList() : all.Where(m => m.At < cursor.Value).ToList();
        var pageRows = pool.TakeLast(limit).ToList();
        var chatPartner = await PartnerNameOfAsync(charId);
        var rows = pageRows.Select(m => (object)new
        {
            id = m.Id,
            text = m.Text,
            mine = m.IsUser,
            sender = m.IsUser
                ? (string.IsNullOrWhiteSpace(m.SenderName) ? "我" : m.SenderName)
                : (string.IsNullOrWhiteSpace(m.SenderName) ? chatPartner : m.SenderName),
            at = m.At.ToUniversalTime().ToString("o"),
        }).ToList();

        return new
        {
            characterId = charId,
            messages = rows,
            hasMore = pool.Count > pageRows.Count,
            nextBefore = pageRows.Count > 0 ? pageRows[0].At.ToUniversalTime().ToString("o") : null,
        };
    }

    /// <summary>分页游标：上一次最后一条的时间（ISO-8601）；给了却不是时间就明说，不悄悄当「最新一页」。</summary>
    private static DateTime? BeforeCursor(JsonElement p)
    {
        var text = OptionalText(p, "before");
        if (text.Length == 0) return null;
        if (DateTime.TryParse(text, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind, out var at))
            return at.ToUniversalTime();
        throw new WebBridgeException("E_BAD_PARAMS", "chat.history 的 before 要给上一次最后一条的时间（ISO-8601）。");
    }

    /// <summary>
    /// <c>chat.send</c>：真的把这句话送进聊天引擎（<see cref="Modules.AiChat.ChatEngine.Send"/>），
    /// 拿到回复再落盘。三条路各落各的盘：
    ///  · main    —— 主界面会话：写存档的 chat 段（<see cref="SaveManager.ChatLog"/>）+ Commit；
    ///  · wechat / qq —— 手机消息：<see cref="PhoneMessageService.PublishAsync"/> + 更新「最后一条」+ Commit，
    ///    并顺手同步给游戏里的女仆（与 WeChatViewModel.Send 同一段）。
    /// 引擎把「API 错误 / 超时」编码成 <c>[API错误…]</c> 这类字符串回显，那是失败不是台词：原样抛给前端。
    /// </summary>
    private async Task<object?> ChatSendAsync(JsonElement p)
    {
        var text = OptionalText(p, "text").Trim();
        if (text.Length == 0) throw new WebBridgeException("E_BAD_PARAMS", "chat.send 需要一段不为空的 {text}。");
        var shell = ChatShellOf(p);
        var charId = await ChatTargetOfAsync(p);
        if (charId.Length == 0)
            throw new WebBridgeException("E_REJECTED",
                shell == "main"
                    ? "还没有进入存档：读一份存档之后，主界面才有可以说话的人。"
                    : "这一档里没有能聊的角色：微信只在存档内成立，先读档或加一个角色。");
        // 会话要随存档落盘：没有当前档就没有地方写，这时候「发出去」只能是假的。
        if (string.IsNullOrEmpty(_saves.CurrentSaveId))
            throw new WebBridgeException("E_REJECTED", "还没有进入存档：这一轮对话没有地方落盘，先读一份存档再说话。");

        // 会话上下文照两个 ViewModel 的发送路径准备：名册（这档里还有谁）+ 表情库清单。
        try
        {
            _chat.SetRoster(_characters.RosterContext(charId, _engine.State.AllowedCharacters));
            await _stickers.EnsureLoadedAsync();
            _chat.SetStickerContext(_stickers.BuildContextForAi());
        }
        catch (Exception ex) { App.WriteLog("WebBridge.chat.send context -> " + ex.Message); }

        if (shell == "main")
        {
            var at0 = DateTime.Now;
            AddChatRecord("user", text);
            var reply = await SendToEngineAsync(charId, text);
            var (clean, _) = Services.StickerLibrary.SplitMarkers(reply);
            clean = clean.Trim();
            AddChatRecord("assistant", clean);
            await CommitChatAsync("自动存档");
            return new
            {
                characterId = charId,
                user = new { id = Guid.NewGuid().ToString("N")[..12], text, mine = true, sender = "我", at = at0.ToUniversalTime().ToString("o") },
                reply = new { id = Guid.NewGuid().ToString("N")[..12], text = clean, mine = false, sender = await PartnerNameOfAsync(charId), at = DateTime.Now.ToUniversalTime().ToString("o") },
            };
        }

        var user = await _phone.PublishAsync(charId, text, "我", isUser: true);
        var raw = await SendToEngineAsync(charId, text);
        var (shown, _) = Services.StickerLibrary.SplitMarkers(raw);
        shown = shown.Trim();
        var partner = await PartnerNameOfAsync(charId);
        var ai = await _phone.PublishAsync(charId, shown, partner, isUser: false);
        await _wechat.TouchLastMessageAsync(charId, text.Length > 20 ? text[..20] : text);
        _ = _maid.NotifyWabDialogAsync(charId, text, shown);
        await CommitChatAsync(shell == "qq" ? "QQ" : "微信");

        return new { characterId = charId, user = MessageRow(user, partner), reply = MessageRow(ai, partner) };
    }

    private object MessageRow(PhoneMessage m, string partner) => new
    {
        id = m.Id,
        text = m.Text,
        mine = m.IsUser,
        sender = m.IsUser ? (string.IsNullOrWhiteSpace(m.SenderName) ? "我" : m.SenderName) : (string.IsNullOrWhiteSpace(m.SenderName) ? partner : m.SenderName),
        at = m.At.ToUniversalTime().ToString("o"),
    };

    /// <summary>一轮模型回复；网关的失败回显（[API错误…] 等）按失败抛，不当台词回给前端。</summary>
    private async Task<string> SendToEngineAsync(string charId, string text)
    {
        string reply;
        try { reply = await _chat.Send(charId, text); }
        catch (Exception ex)
        {
            App.WriteLog("WebBridge.chat.send -> " + ex);
            throw new WebBridgeException("E_BACKEND", "这一轮没能送到她那边：" + ex.Message);
        }
        if (IsFailedReply(reply))
            throw new WebBridgeException("E_BACKEND", reply.Trim());
        return reply;
    }

    /// <summary>网关对失败的编码（与 MainGameViewModel.IsApiErrorText 同一组前缀）：这不是 AI 的台词。</summary>
    private static bool IsFailedReply(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var t = text.TrimStart();
        return t.StartsWith("[API错误", StringComparison.Ordinal)
            || t.StartsWith("[网络连接失败", StringComparison.Ordinal)
            || t.StartsWith("[请求超时", StringComparison.Ordinal)
            || t.StartsWith("[请求排队超时", StringComparison.Ordinal);
    }

    /// <summary>把一轮对话写进存档；写不进去就抛 —— 前端不能看到一个「已发送」但盘上什么都没有。</summary>
    private async Task CommitChatAsync(string label)
    {
        if (string.IsNullOrEmpty(_saves.CurrentSaveId)) return;
        if (!await _saves.Commit(label))
            throw new WebBridgeException("E_REJECTED", "这一段对话没能写进存档：" + SaveErrorText() + "（退出后可能看不到它）");
    }

    /// <summary>主界面会话记录的落盘那一半（与 <c>MainGameViewModel.AddMessage</c> 的 chat 段同一规则：只留最近 80 条）。</summary>
    private void AddChatRecord(string role, string text)
    {
        _saves.ChatLog.Add(new ChatRecord { Role = role, Text = text, At = DateTime.Now });
        if (_saves.ChatLog.Count > 80) _saves.ChatLog.RemoveRange(0, _saves.ChatLog.Count - 80);
    }

    /// <summary>这一次要跟谁聊：请求点名的 → 存档主角 → 电话簿第一位。</summary>
    private async Task<string> ChatTargetOfAsync(JsonElement p)
    {
        var asked = OptionalText(p, "characterId");
        if (asked.Length > 0) return asked;
        if (!string.IsNullOrWhiteSpace(_engine.State.CharacterId)) return _engine.State.CharacterId;
        var contacts = await _wechat.ListAsync();
        return contacts.FirstOrDefault()?.CharacterId ?? "";
    }

    // ==================== 语音 ====================

    /// <summary>
    /// <c>voice.status</c>：这台设备现在能不能听、能不能说。
    /// 前端据此把麦克风按钮画成可点或禁用，并把「为什么不能点」原样摆出来 —— 灰着不说原因，等于假装没这功能。
    /// </summary>
    private object VoiceStatusOf()
    {
        var availability = _speech.ListenAvailability();
        return new
        {
            sttEnabled = _speech.SttEnabled,
            sttEngine = _speech.SttEngine,
            ttsEnabled = _speech.TtsEnabled,
            ttsEngine = _speech.TtsEngine,
            canListen = availability.CanListen,
            note = availability.Reason,
        };
    }

    /// <summary>
    /// <c>voice.listen</c>：开一次麦，等一句话，把识别出的文字交回来。
    /// 引擎与时限全用 <see cref="SpeechService.ListenOnceAsync"/> 那一条（与主界面语音按钮同一个 SAPI/WinRT/API 链路），
    /// 桥只负责「把结果原样回给前端」这一层，不重写识别。没听出字时 text 为空、note 说原因。
    /// </summary>
    private async Task<object?> VoiceListenAsync(JsonElement p)
    {
        var availability = _speech.ListenAvailability();
        if (!availability.CanListen)
            throw new WebBridgeException("E_UNAVAILABLE", availability.Reason);
        if (_voiceCts is not null)
            throw new WebBridgeException("E_BUSY", "上一句还在听：先说完，或者按停止。");

        var charId = await ChatTargetOfAsync(p);
        var lang = LocaleOf(_settings.Current.Lang) == "en-US" ? "en-US" : "zh-CN";
        _voiceCts = new CancellationTokenSource();
        try
        {
            var heard = await _speech.ListenOnceAsync(lang, _voiceCts.Token);
            return new { characterId = charId, text = heard.Text, note = heard.Note };
        }
        finally
        {
            _voiceCts.Dispose();
            _voiceCts = null;
        }
    }

    /// <summary><c>voice.cancel</c>：停掉正在进行的这一句。走引擎自己的 StopListening，与界面上「停止」是同一条路。</summary>
    private object VoiceCancel()
    {
        try { _voiceCts?.Cancel(); }
        catch (ObjectDisposedException) { /* 故意忽略：这一句刚好自己结束了，没什么可停的 */ }
        _speech.StopListening();
        return new { stopped = true };
    }

    /// <summary>
    /// <c>voice.speak</c>：用配置好的朗读引擎念出这段文字（TTS 只接这一处：朗读最后一条回复）。
    /// Speak 自己吞异常只为不崩界面，所以这里看 LastSpeakNote 判断是不是真读出来了；没读出来就抛，不谎报成功。
    /// </summary>
    private async Task<object?> VoiceSpeakAsync(JsonElement p)
    {
        var text = OptionalText(p, "text").Trim();
        if (text.Length == 0) throw new WebBridgeException("E_BAD_PARAMS", "voice.speak 需要一段不为空的 {text}。");
        if (!_speech.TtsEnabled) throw new WebBridgeException("E_UNAVAILABLE", "朗读已在设置里关闭（设置 → 声音与语音）。");
        var charId = OptionalText(p, "characterId");
        var lang = LocaleOf(_settings.Current.Lang) == "en-US" ? "en-US" : "zh-CN";
        await _speech.Speak(text, lang, charId.Length > 0 ? charId : null);
        if (!string.IsNullOrWhiteSpace(_speech.LastSpeakNote))
            throw new WebBridgeException("E_BACKEND", _speech.LastSpeakNote);
        return new { ok = true };
    }

    /// <summary>壳：main（主界面会话）/ wechat / qq。认不出的壳一律按微信（默认那一面）。</summary>
    private static string ChatShellOf(JsonElement p) => OptionalText(p, "shell") switch
    {
        "main" => "main",
        "qq" => "qq",
        _ => "wechat",
    };

    /// <summary>顶栏那句模式：与 <c>WeChatViewModel.RefreshMode</c> 同一条判断（真接入 / 本地陪聊）。</summary>
    private string ChatModeLabel(string shell)
    {
        var s = _settings.Current;
        var channels = _official.Channels;
        if (shell == "qq")
            return s.QqBotEnabled && channels.Length > 0 && channels[0].IsRunning
                ? "QQ 官方机器人 · 已接入" : "本地 QQ · TA 还在";
        return s.WechatEnabled && channels.Length > 1 && channels[1].IsRunning
            ? "真微信 · 已接入" : "本地微信 · TA 还在";
    }

    private async Task<string> PartnerNameOfAsync(string charId)
    {
        var all = await _characters.ListAsync();
        return all.FirstOrDefault(c => c.Profile.Id == charId)?.Profile.Name ?? "TA";
    }

    /// <summary>列表行右上角的时间（与 WeChatViewModel.RelativeTime 同一套口径）：今天给时分，昨天给「昨天」。</summary>
    private static string TimeLabelOf(DateTime at)
    {
        if (at == default) return "";
        var local = at.ToLocalTime();
        var today = DateTime.Today;
        if (local.Date == today) return local.ToString("HH:mm");
        if (local.Date == today.AddDays(-1)) return "昨天";
        return local.Year == today.Year ? local.ToString("M月d日") : local.ToString("yyyy/M/d");
    }

    /// <summary><c>characters.list</c>：角色库真表。桥不搬密钥与文件路径以外的私密字段。</summary>
    private async Task<object?> CharactersListAsync()
    {
        try
        {
            var all = await _characters.ListAsync();
            return new
            {
                characters = all.Select(c => new
                {
                    id = c.Profile.Id,
                    name = c.Profile.Name,
                    nickname = c.Profile.Nickname,
                    gender = c.Profile.Gender,
                    age = c.Profile.Age,
                    catchphrase = c.Profile.Catchphrase,
                    avatar = c.Avatar,
                    avatarUrl = AssetUrl(c.Avatar),
                    spriteCount = c.SpriteMap.Count,
                    outfits = c.SpriteMap.Keys.Select(k => k.Split('/')[0]).Distinct().ToList(),
                    currentSpriteUrl = AssetUrl(CurrentSprite(c)),
                    outfit = ResolveLook(c).outfit,
                    emotion = ResolveLook(c).emotion,
                    affection = c.State.Affection,
                    trust = c.State.Trust,
                    energy = c.State.Energy,
                    security = SecurityOf(c),
                    mood = c.State.CurrentEmotion,
                }).ToList(),
            };
        }
        catch (Exception ex)
        {
            App.WriteLog("WebBridge.CharactersList -> " + ex.Message);
            return new { characters = Array.Empty<object>() };
        }
    }

    /// <summary>
    /// <c>palette.swatches</c>：纯色底那十颗色板，直接取 <c>DesignSystem/Theme/ColorPaletteEndfield.xaml</c>
    /// 里已经有的色阶（Cream/OffWhite/Beige/BeigeAccent…）。前端不再自带一份十六进制 ——
    /// 色板只有一处定义，改 XAML 前端自动跟着变。
    /// </summary>
    private static object PaletteSwatches()
    {
        var picks = new (string Key, string Name)[]
        {
            ("Cream50", "奶油纸"), ("OffWhite100", "素灰"), ("BlueSoft", "晨雾蓝"), ("GreenSoft", "青瓷"),
            ("Beige100", "暖砂"), ("RedSoft", "樱雾"), ("WarmDark", "石墨"), ("ShadowLight", "幽蓝"),
            ("Beige250", "赭石"), ("BeigeAccent300", "墨绿"),
        };

        var list = new List<object>(picks.Length);
        foreach (var (key, name) in picks)
        {
            var hex = ResourceHex(key);
            if (hex is not null) list.Add(new { hex, name });
        }
        return new { swatches = list };
    }

    /// <summary>把资源里的颜色读成 #RRGGBB（Color / SolidColorBrush / 字符串三种都认）。</summary>
    private static string? ResourceHex(string key)
    {
        try
        {
            if (Application.Current?.Resources.TryGetValue(key, out var value) != true) return null;
            var color = value switch
            {
                Color c => c,
                SolidColorBrush b => b.Color,
                string s when s.StartsWith('#') => Color.FromArgb(s),
                _ => Colors.Transparent,
            };
            if (color == Colors.Transparent && value is not string) return null;
            return $"#{color.Red * 255:0}{color.Green * 255:0}{color.Blue * 255:0}";
        }
        catch { return null; }
    }

    // ==================== appearance（整屏材质 + 背景）====================

    /// <summary>
    /// <c>appearance.get</c>：前端那份 <c>Appearance</c>（见 web/src/bridge/protocol.ts）。
    /// 一个字段都不新造 —— 每一项都是 <see cref="UserSettings"/> 里已经很有的东西，
    /// 所以「网页里调外观」与「原生设置页里调外观」改的是同一份存档，不会两套观感。
    /// </summary>
    // 前端要的是 { appearance }；直接给裸对象会让它以为后端没下发（走「没有可调的外观」空态）。
    private object AppearanceGet() => new { appearance = AppearanceOf(_settings.Current) };

    /// <summary>
    /// 天气场景从 <see cref="BackgroundService.CurrentScene"/> 现取，不从设置里翻旧字段：
    /// 场景是实时天气轮询解析出来的（<see cref="BackgroundService.ApplyWeatherDescription"/>），
    /// 只有这份运行时值才和原生背景层画的是同一档。
    /// </summary>
    private object AppearanceOf(UserSettings s)
    {
        var scene = _background.CurrentScene;
        return new
        {
            alpha = Math.Clamp(s.GlassTranslucency, 0, 1),
            frost = Math.Clamp(s.GlassFrost, 0, 1),
            liquid = Math.Clamp(s.GlassHeight, 0, 1),
            lightweight = s.PerformanceMode,
            reducedTransparency = s.GlassReducedTransparency,
            background = new
            {
                mode = BackgroundModeOf(s.BackgroundMode),
                solidHex = s.BackgroundSolidHex,
                pageTintHex = s.PageTintHex,
                imageLabel = string.IsNullOrWhiteSpace(s.BackgroundImagePath)
                    ? "尚未选择图片"
                    : System.IO.Path.GetFileName(s.BackgroundImagePath),
            },
            scene = new
            {
                experimental = s.ExperimentalSceneFxEnabled,
                weather = s.WeatherSceneOverlayEnabled,
                light = s.SceneLightEnabled,
                audio = s.WeatherSceneAudioEnabled,
                audioLevel = Math.Clamp(s.WeatherSceneAudioLevel, 0, 1),
                // 名字就是 WeatherSceneKind 的枚举名：前端那份 WeatherKind 与它逐字对齐。
                weatherKind = scene.Kind.ToString(),
                weatherIntensity = Math.Round(Math.Clamp(scene.Intensity, 0, 1), 3),
            },
        };
    }

    /// <summary>存档里存的是 none/image/clear（纯色/图片/透视），前端叫 solid/image/clear。</summary>
    private static string BackgroundModeOf(string? mode) => mode switch
    {
        "image" => "image",
        "clear" => "clear",
        _ => "solid",
    };

    private static string StoredBackgroundMode(string? mode) => mode switch
    {
        "image" => "image",
        "clear" => "clear",
        _ => "none",
    };

    /// <summary>params 是 <c>{ appearance }</c>；写回走与 settings.set 同一条「下发 → 落盘」。</summary>
    private async Task<object?> AppearanceSetAsync(JsonElement p)
    {
        if (p.ValueKind != JsonValueKind.Object
            || !p.TryGetProperty("appearance", out var a) || a.ValueKind != JsonValueKind.Object)
            throw new WebBridgeException("E_BAD_PARAMS", "appearance.set 需要 {appearance}。");

        var s = _settings.Current;

        if (a.TryGetProperty("alpha", out var alpha)) s = s with { GlassTranslucency = Math.Clamp(Number(alpha, "alpha"), 0, 1) };
        if (a.TryGetProperty("frost", out var frost)) s = s with { GlassFrost = Math.Clamp(Number(frost, "frost"), 0, 1) };
        if (a.TryGetProperty("liquid", out var liquid)) s = s with { GlassHeight = Math.Clamp(Number(liquid, "liquid"), 0, 1) };
        if (a.TryGetProperty("lightweight", out var lightweight)) s = s with { PerformanceMode = Boolean(lightweight, "lightweight") };
        if (a.TryGetProperty("reducedTransparency", out var reduced))
            s = s with { GlassReducedTransparency = Boolean(reduced, "reducedTransparency") };

        if (a.TryGetProperty("background", out var bg) && bg.ValueKind == JsonValueKind.Object)
        {
            if (bg.TryGetProperty("mode", out var mode) && mode.ValueKind == JsonValueKind.String)
                s = s with { BackgroundMode = StoredBackgroundMode(mode.GetString()) };
            if (bg.TryGetProperty("solidHex", out var solid) && solid.ValueKind == JsonValueKind.String)
                s = s with { BackgroundSolidHex = Hex(solid.GetString()) };
            if (bg.TryGetProperty("pageTintHex", out var tint) && tint.ValueKind == JsonValueKind.String)
                s = s with { PageTintHex = Hex(tint.GetString()) };
        }

        if (a.TryGetProperty("scene", out var scene) && scene.ValueKind == JsonValueKind.Object)
        {
            if (scene.TryGetProperty("experimental", out var experimental))
                s = s with { ExperimentalSceneFxEnabled = Boolean(experimental, "scene.experimental") };
            if (scene.TryGetProperty("weather", out var weather))
                s = s with { WeatherSceneOverlayEnabled = Boolean(weather, "scene.weather") };
            if (scene.TryGetProperty("light", out var light))
                s = s with { SceneLightEnabled = Boolean(light, "scene.light") };
            if (scene.TryGetProperty("audio", out var audio))
                s = s with { WeatherSceneAudioEnabled = Boolean(audio, "scene.audio") };
            if (scene.TryGetProperty("audioLevel", out var level))
                s = s with { WeatherSceneAudioLevel = Math.Clamp(Number(level, "scene.audioLevel"), 0, 1) };
        }

        await _gate.WaitAsync();
        try
        {
            _settings.Apply(s);
            await _settings.Persist();
        }
        finally
        {
            _gate.Release();
        }

        var appearance = AppearanceOf(_settings.Current);
        Emit("appearance.changed", new { appearance });
        return new { appearance };
    }

    /// <summary>颜色只收 #RRGGBB / #AARRGGBB，别的原样退回空串（不让任意字符串进存档）。</summary>
    private static string Hex(string? value)
    {
        var text = (value ?? "").Trim();
        if (text.Length is 7 or 9 && text[0] == '#' && text.Skip(1).All(Uri.IsHexDigit)) return text.ToUpperInvariant();
        return "";
    }

    /// <summary>
    /// <c>settings.patch</c>：一次改多组设置（{patch:{ "sound.bgm":0.6, "ui.chatStyle":"bubble" }}）。
    ///
    /// 设置页有很多行是就地改的；每行各开一个方法会让桥表长到没法读，
    /// 所以这里是**同一条路**：路径白名单 → 改写 UserSettings → Apply + Persist → 广播 settings.changed。
    /// 白名单之外的键一律 E_BAD_PARAMS，不猜、不写。
    /// </summary>
    private async Task<object?> SettingsPatchAsync(JsonElement p)
    {
        if (p.ValueKind != JsonValueKind.Object
            || !p.TryGetProperty("patch", out var patch) || patch.ValueKind != JsonValueKind.Object)
            throw new WebBridgeException("E_BAD_PARAMS", "settings.patch 需要 {patch:{...}}。");

        var s = _settings.Current;
        var langChanged = false;

        foreach (var kv in patch.EnumerateObject())
        {
            var path = kv.Name;
            var value = kv.Value;
            switch (path)
            {
                case "sound.bgm": s = s with { BgmLevel = Math.Clamp(Number(value, path), 0, 1) }; break;
                case "sound.sfx": s = s with { SfxLevel = Math.Clamp(Number(value, path), 0, 1) }; break;
                case "text.scale": s = s with { TextSpeed = Math.Clamp(Number(value, path), 0.5, 2) }; break;
                case "story.autoSave": s = s with { AutoSaveEnabled = Boolean(value, path) }; break;
                case "ui.orientation": s = s with { ScreenOrientation = Text(value, path) }; break;
                case "ui.chatStyle": s = s with { ChatStyle = Text(value, path) }; break;
                case "ui.difficulty": s = s with { GameDifficulty = Text(value, path) }; break;
                case "platform.petIdleMinutes": s = s with { PetIdleMinutes = Math.Clamp((int)Number(value, path), 0, 240) }; break;
                case "platform.preferDiscreteGpu": s = s with { PreferDiscreteGpu = Boolean(value, path) }; break;
                case "integrations.qq": s = s with { QqBotEnabled = Boolean(value, path) }; break;
                case "integrations.wechat": s = s with { WechatEnabled = Boolean(value, path) }; break;
                case "integrations.mcp": s = s with { McpEnabled = Boolean(value, path) }; break;
                case "life.notifications": s = s with { NotificationsEnabled = Boolean(value, path) }; break;
                case "life.weatherCity": s = s with { WeatherCity = Text(value, path) }; break;
                case "memory.windowDays": s = s with { MemoryWindowDays = Math.Clamp(Number(value, path), 0, 3650) }; break;
                case "ai.smartSaving": s = s with { SmartSavingEnabled = Boolean(value, path) }; break;
                case "ai.timeoutSeconds": s = s with { AiTimeoutSeconds = Math.Clamp((int)Number(value, path), 3, 600) }; break;
                case "ai.thinkMode": s = s with { ThinkMode = Services.Core.ThinkTierRuntime.NormalizeMode(Text(value, path)) }; break;
                case "ai.thinkBar": s = s with { ThinkBarEnabled = Boolean(value, path) }; break;
                case "ai.quickButtons": s = s with { QuickButtonsEnabled = Boolean(value, path) }; break;
                case "ai.thinkTierCount": s = s with { ThinkTierCount = Math.Clamp((int)Number(value, path), 1, 5) }; break;
                case "ai.thinkTierLabels": s = s with { ThinkTierLabels = Text(value, path).Trim() }; break;
                case "ai.thinkParamKey": s = s with { ThinkParamKey = Text(value, path).Trim() }; break;
                case "ai.thinkParamValues": s = s with { ThinkParamValues = Text(value, path).Trim() }; break;
                case "appearance.liquid": s = s with { LiquidEnabled = Boolean(value, path) }; break;
                case "appearance.glass": s = s with { GlassEnabled = Boolean(value, path) }; break;
                case "appearance.frost": s = s with { FrostEnabled = Boolean(value, path) }; break;
                case "language.locale":
                {
                    var locale = Text(value, path);
                    if (locale is not ("zh-CN" or "en-US"))
                        throw new WebBridgeException("E_BAD_PARAMS", "language.locale 只支持 zh-CN / en-US。");
                    langChanged = !string.Equals(locale, s.Lang, StringComparison.Ordinal);
                    s = s with { Lang = locale };
                    break;
                }
                default:
                    throw new WebBridgeException("E_BAD_PARAMS", "unknown setting path: " + path);
            }
        }

        await _gate.WaitAsync();
        try
        {
            _settings.Apply(s);
            if (langChanged) LocalizationService.Current.SetCulture(s.Lang);
            await _settings.Persist();
        }
        finally
        {
            _gate.Release();
        }

        var values = SettingsValues();
        Emit("settings.changed", new { values });
        return new { values };
    }

    private static string Text(JsonElement value, string path)
        => value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : throw new WebBridgeException("E_BAD_PARAMS", path + " 需要一个字符串。");

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
    /// 另外三族是视觉语言的骨架：<c>--w-accent*</c>（印墨/赭）、<c>--w-status*</c>（信号/铜绿，只给读数）、
    /// <c>--w-ramp-1..6</c>（六级中性阶：1 标题 → 6 发丝线）。
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

        // 色：地面 / 面 / 墨 / 印墨 / 信号 / 边界
        Set("--w-bg", Color("PageBg"));
        Set("--w-surface-tint", Color("SurfaceBg"));
        // 墨阶起点取中间档（InkMid）：前端从它往两极混出五档，起点太高会把五档压成一级。
        Set("--w-ink-seed", Color("InkMid"));
        Set("--w-ink-signal-seed", Color("AccentWarmFg"));

        // 印墨（赭）：主行动、当前项、图形笔画。它同时当填充和当墨，所以对卡面已经过 4.5。
        Set("--w-accent", Color("AccentWarm"));
        Set("--w-accent-hi", Color("PrimaryHover"));
        Set("--w-accent-lo", Color("BeigeAccent200"));
        Set("--w-accent-ink", Color("AccentWarmFg"));
        Set("--w-accent-edge", WithAlpha("AccentWarm", 0.5));
        Set("--w-accent-glow", WithAlpha("AccentWarm", 0.24));

        // 信号（铜绿）：只给状态与读数。
        Set("--w-status", Color("Signal"));
        Set("--w-status-fg", Color("SignalFg"));
        Set("--w-status-ink", Color("SignalInk"));
        Set("--w-status-edge", WithAlpha("Signal", 0.42));
        Set("--w-status-glow", WithAlpha("Signal", 0.18));
        Set("--w-status-tint", WithAlpha("Signal", 0.12));

        // 旧名 --w-signal* 一直指的是印墨（不是信号色）：页面里六十处装饰用它，改名会把它们
        // 一起染成铜绿。保留成印墨的别名，语义正确的名字是 --w-accent* 与 --w-status*。
        Set("--w-signal", Color("AccentWarm"));
        Set("--w-signal-hi", Color("PrimaryHover"));
        Set("--w-signal-lo", Color("BeigeAccent200"));
        Set("--w-signal-edge", WithAlpha("AccentWarm", 0.5));
        Set("--w-signal-glow", WithAlpha("AccentWarm", 0.24));

        // 六级中性阶：1 标题 → 6 发丝线。1–5 由 StyleWorld 的 Lock() 钉死层数，第 6 级只做结构。
        Set("--w-ramp-1", Color("WarmDark"));
        Set("--w-ramp-2", Color("SurfaceFg"));
        Set("--w-ramp-3", Color("WarmBody"));
        Set("--w-ramp-4", Color("MutedFg"));
        Set("--w-ramp-5", Color("WarmFaint"));
        Set("--w-ramp-6", Color("BorderLine"));

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
    /// <summary>
    /// <c>app.info</c>：应用与宿主信息，外加「现在有没有进过存档」。
    /// 后面这条决定系在存档上的东西（QQ、监测、角色状态）该不该出现在界面上 ——
    /// 没有存档就没有角色、没有状态，那些入口只是空壳。
    /// </summary>
    private object AppInfoJson()
    {
        var saveId = _engine.CurrentSaveId ?? "";
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
            dev = string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WARM_WEB_URL")?.Trim())
                ? DevUrl
                : Environment.GetEnvironmentVariable("WARM_WEB_URL")!.Trim(),
            debug = IsDebugBuild,
            run = new
            {
                active = !string.IsNullOrWhiteSpace(saveId),
                saveId,
                character = "",
            },
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

    /// <summary>参数里的 <c>id</c>：写盘/导出都按它找人，缺了就是 E_BAD_PARAMS（不猜最近一个）。</summary>
    private static string ReadId(JsonElement p, string method)
    {
        if (p.ValueKind != JsonValueKind.Object || !p.TryGetProperty("id", out var el)
            || el.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(el.GetString()))
            throw new WebBridgeException("E_BAD_PARAMS", method + " 需要 {id}。");
        return (el.GetString() ?? "").Trim();
    }

    /// <summary>必填字符串字段。</summary>
    private static string RequiredText(JsonElement obj, string key)
        => obj.TryGetProperty(key, out var el) ? Text(el, key) : throw new WebBridgeException("E_BAD_PARAMS", "缺少 " + key + "。");

    /// <summary>可选字符串字段：没有这个键、或给了 null，都当空串。</summary>
    private static string OptionalText(JsonElement obj, string key)
        => !obj.TryGetProperty(key, out var el) || el.ValueKind == JsonValueKind.Null ? "" : Text(el, key);

    /// <summary>可选布尔：没有这个键就是「没传」，不等于 false。</summary>
    private static bool? OptionalBool(JsonElement obj, string key)
        => !obj.TryGetProperty(key, out var el) || el.ValueKind == JsonValueKind.Null ? null : Boolean(el, key);

    /// <summary>
    /// 可空整数（工作室的「留空 = 跟随全局」就靠它）：空串 / null 是清除，
    /// 写了不是数字的东西就明说，不悄悄当成空把用户填的值抹掉。
    /// </summary>
    private static int? NullableInt(JsonElement v, string what)
    {
        var text = v.ValueKind switch
        {
            JsonValueKind.Null => "",
            JsonValueKind.Number => v.GetRawText(),
            JsonValueKind.String => (v.GetString() ?? "").Trim(),
            _ => throw new WebBridgeException("E_BAD_PARAMS", what + " 需要数字或空值。"),
        };
        if (text.Length == 0) return null;
        if (int.TryParse(text, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var i)) return i;
        throw new WebBridgeException("E_REJECTED", what + " 要填一个整数，留空才是「跟随全局」。");
    }

    private static double? NullableDouble(JsonElement v, string what)
    {
        var text = v.ValueKind switch
        {
            JsonValueKind.Null => "",
            JsonValueKind.Number => v.GetRawText(),
            JsonValueKind.String => (v.GetString() ?? "").Trim(),
            _ => throw new WebBridgeException("E_BAD_PARAMS", what + " 需要数字或空值。"),
        };
        if (text.Length == 0) return null;
        if (double.TryParse(text, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var d) && double.IsFinite(d)) return d;
        throw new WebBridgeException("E_REJECTED", what + " 要填一个数字，留空才是「跟随全局」。");
    }

    /// <summary>0..100 的滑杆读数折成整数并夹住（信任/精力/性欲走这条）。</summary>
    private static int Percent(JsonElement v, string what)
        => Math.Clamp((int)Math.Round(Number(v, what)), 0, 100);

    /// <summary>空白即沿用旧值：改一处别处不该被空串冲掉。</summary>
    private static string Fallback(string value, string previous) => value.Length > 0 ? value : previous;
}

/// <summary>桥方法里「这条路走不通」的专用异常：<see cref="Code"/> 原样回给前端，不打完整堆栈。</summary>
public sealed class WebBridgeException : Exception
{
    public string Code { get; }

    public WebBridgeException(string code, string message) : base(message) => Code = code;
}
