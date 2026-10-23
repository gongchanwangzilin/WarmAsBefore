using System.Text.Json;
using WarmAsBefore.Models;
using WarmAsBefore.Modules.Mcp;
using WarmAsBefore.Modules.SaveSystem;
using WarmAsBefore.Services;

namespace WarmAsBefore.Modules.Tools;

/// <summary>
/// 世界向工具：身体、钱包、心理、记忆链、画面生成与角色创建。
/// 它们不放进 RegisterSystemTools，是因为每一条都要先解析各自的服务；
/// 放在一起会让「系统工具」变成一个什么都往里塞的口袋。
/// </summary>
public sealed partial class ToolManager
{
    private void RegisterWorldTools()
    {
        RegisterBuiltin("body_state",
            "读取/改变 TA 现在的身体感受：冷、热、渴、饿、想念。"
            + "action=feel 只读；eat / drink / warm / cool 是把对应感受往舒适方向推。"
            + "只把感受写成动作和语气，不要报数值。",
            async args =>
            {
                var services = ServicesHelper.GetServices();
                var body = services?.GetService(typeof(PhysiologyService)) as PhysiologyService;
                var engine = services?.GetService(typeof(GameEngine)) as GameEngine;
                if (body is null || engine is null)
                    return JsonSerializer.Serialize(new { ok = false, message = "身体感受服务不可用" });
                var action = GetString(args, "action", "feel").Trim().ToLowerInvariant();
                switch (action)
                {
                    case "eat": body.Eat(); break;
                    case "drink": body.Drink(); break;
                    case "warm": body.WarmUp(); break;
                    case "cool": body.CoolDown(); break;
                }
                body.Tick();
                var b = engine.State.Body;
                return JsonSerializer.Serialize(new
                {
                    ok = true,
                    action,
                    warmth = Math.Round(b.Warmth, 2),
                    thirst = Math.Round(b.Thirst, 2),
                    hunger = Math.Round(b.Hunger, 2),
                    longing = Math.Round(b.Longing, 2),
                    feeling = b.Describe(),
                    hint = body.PromptText()
                });
            });

        RegisterBuiltin("wallet",
            "亲密币：action=read 查余额；add 加钱（amount 可为负，最低 0）；set 设定余额。"
            + "金钱系统关闭时不能增减，购买也不会扣钱。",
            async args =>
            {
                var services = ServicesHelper.GetServices();
                var shop = services?.GetService(typeof(Modules.Market.ShopService)) as Modules.Market.ShopService;
                var settings = services?.GetService(typeof(SettingsManager)) as SettingsManager;
                if (shop is null)
                    return JsonSerializer.Serialize(new { ok = false, message = "商店服务不可用" });
                var action = GetString(args, "action", "read").Trim().ToLowerInvariant();
                if (!(settings?.Current.MoneyEnabled ?? true))
                {
                    return JsonSerializer.Serialize(new
                    {
                        ok = false,
                        money_enabled = false,
                        coins = shop.Coins,
                        message = "金钱系统已关闭：不能加钱，买东西也不会扣钱。"
                    });
                }
                var amount = GetInt(args, "amount", 0);
                switch (action)
                {
                    case "add": await shop.AddCoinsAsync(amount); break;
                    case "set": await shop.SetCoinsAsync(amount); break;
                }
                return JsonSerializer.Serialize(new { ok = true, action, coins = shop.Coins, label = shop.WalletLabel });
            });

        RegisterBuiltin("mental_catalog",
            "查询心理状况目录（名字/类别/常见表现），可按关键词过滤。"
            + "这是叙事素材，不是诊断；不要把病名当标签贴给角色。",
            args =>
            {
                var keyword = GetString(args, "keyword", "").Trim();
                return Task.FromResult(MentalHealthService.CatalogText(keyword));
            });

        RegisterBuiltin("mental_state",
            "读取当前角色正在承受的心理状况：严重度、稳定度与具体备注。",
            async _ =>
            {
                var services = ServicesHelper.GetServices();
                var mental = services?.GetService(typeof(MentalHealthService)) as MentalHealthService;
                var engine = services?.GetService(typeof(GameEngine)) as GameEngine;
                if (mental is null || engine is null)
                    return JsonSerializer.Serialize(new { ok = false, message = "心理服务不可用" });
                var charId = engine.State.CharacterId;
                var list = await mental.ListAsync(charId);
                var rows = list.Select(s =>
                {
                    var c = mental.Find(s.Id);
                    return new
                    {
                        id = s.Id,
                        name = c?.Name ?? s.Id,
                        category = c?.Category ?? "",
                        severity = s.Severity,
                        stability = Math.Round(s.Stability, 2),
                        note = s.Note,
                        safety = c?.Safety ?? false
                    };
                }).ToList();
                return JsonSerializer.Serialize(new
                {
                    ok = true,
                    character = charId,
                    count = rows.Count,
                    conditions = rows,
                    prompt = mental.PromptText(charId),
                    safety = mental.SafetyText(charId)
                });
            });

        RegisterBuiltin("set_mental_state",
            "给当前角色设置/调整一条心理状况：condition 是目录里的名字或 id，severity 1~3（轻度~重度），"
            + "note 可写「具体到 TA 自己」的表现。remove=true 表示移除。"
            + "只影响表现，不删记忆；严重度与稳定度会随相处慢慢变化。",
            async args =>
            {
                var services = ServicesHelper.GetServices();
                var mental = services?.GetService(typeof(MentalHealthService)) as MentalHealthService;
                var engine = services?.GetService(typeof(GameEngine)) as GameEngine;
                if (mental is null || engine is null)
                    return JsonSerializer.Serialize(new { ok = false, message = "心理服务不可用" });
                var charId = engine.State.CharacterId;
                if (string.IsNullOrWhiteSpace(charId))
                    return JsonSerializer.Serialize(new { ok = false, message = "当前没有进入存档" });
                var condition = GetString(args, "condition", "").Trim();
                if (condition.Length == 0)
                    return JsonSerializer.Serialize(new { ok = false, message = "需要 condition（目录里的名字或 id）" });
                if (GetBool(args, "remove"))
                {
                    var removed = await mental.RemoveAsync(charId, condition);
                    return JsonSerializer.Serialize(new { ok = removed, message = removed ? "已移除" : "没有这条状况" });
                }
                var severity = GetInt(args, "severity", 0);
                var note = GetString(args, "note", "");
                var (ok, message) = await mental.SetAsync(charId, condition, severity, note);
                return JsonSerializer.Serialize(new { ok, message });
            });

        RegisterBuiltin("memory_chain",
            "从一条线索沿字词、时间、共同经历扩散，找回一串相关的记忆（默认 5 条，新的/更相似的在前）。"
            + "clue 是想到的词或事；limit 1~20。核心记忆永远在，普通记忆被想起会加深。",
            async args =>
            {
                var services = ServicesHelper.GetServices();
                var human = services?.GetService(typeof(HumanMemoryService)) as HumanMemoryService;
                var engine = services?.GetService(typeof(GameEngine)) as GameEngine;
                if (human is null || engine is null)
                    return JsonSerializer.Serialize(new { ok = false, message = "记忆服务不可用" });
                var clue = GetString(args, "clue", "").Trim();
                var limit = Math.Clamp(GetInt(args, "limit", 5), 1, 20);
                var result = await human.ChainSearchAsync(engine.State.CharacterId, clue, limit);
                return JsonSerializer.Serialize(new
                {
                    ok = true,
                    summary = result.Summary,
                    hits = result.Hits.Select(h => new
                    {
                        id = h.Entry.Id,
                        text = h.Entry.Text,
                        when = h.Entry.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
                        why = h.Why,
                        score = Math.Round(h.Score, 2),
                        depth = h.Depth,
                        core = h.Entry.IsCore,
                        trauma = h.Entry.IsTrauma,
                        stage = h.Entry.TraumaStage
                    }).ToList()
                });
            });

        RegisterBuiltin("set_core_memory",
            "把一条记忆设为核心/永久记忆（core=true），或取消（core=false）。"
            + "核心记忆不会淡忘、永远可回想，并会放射影响周围的记忆；用之前先用 memory_chain 找到 id。",
            async args =>
            {
                var services = ServicesHelper.GetServices();
                var human = services?.GetService(typeof(HumanMemoryService)) as HumanMemoryService;
                var engine = services?.GetService(typeof(GameEngine)) as GameEngine;
                if (human is null || engine is null)
                    return JsonSerializer.Serialize(new { ok = false, message = "记忆服务不可用" });
                var id = GetString(args, "memory_id", "").Trim();
                if (id.Length == 0) return JsonSerializer.Serialize(new { ok = false, message = "需要 memory_id" });
                var ok = await human.SetCoreAsync(engine.State.CharacterId, id, GetBool(args, "core", true));
                return JsonSerializer.Serialize(new
                {
                    ok,
                    message = ok ? "已经处理。核心记忆不会随时间变淡。" : "没有找到这条记忆"
                });
            });

        RegisterBuiltin("heal_trauma",
            "陪一件事慢慢松动：场景里发生真正被理解/被接住的经历时，给一条创伤记忆做一次疗愈。"
            + "记忆不会被删掉，情绪强度只会一点点下降。用之前先用 memory_chain 找到 id。",
            async args =>
            {
                var services = ServicesHelper.GetServices();
                var human = services?.GetService(typeof(HumanMemoryService)) as HumanMemoryService;
                var engine = services?.GetService(typeof(GameEngine)) as GameEngine;
                if (human is null || engine is null)
                    return JsonSerializer.Serialize(new { ok = false, message = "记忆服务不可用" });
                var id = GetString(args, "memory_id", "").Trim();
                var scene = GetString(args, "scene", "").Trim();
                var (ok, message, stage, intensity) = await human.HealTraumaAsync(engine.State.CharacterId, id, scene);
                return JsonSerializer.Serialize(new
                {
                    ok,
                    message,
                    stage,
                    intensity = Math.Round(intensity, 2)
                });
            });

        RegisterBuiltin("generate_image",
            "生成一张画面并落到世界里。kind=background（当前/指定场景背景）、scene（新场景+背景并接进地图）、"
            + "outfit（当前角色的新装扮立绘）、avatar（当前角色头像）、character（用给定资料创建角色，可带头像）。"
            + "prompt 写清画面内容；风格遵循角色与场景设定。未开启画面生成或没配密钥时会明确失败。",
            async args => await GenerateImageAsync(args));

        RegisterBuiltin("save_admin",
            "管理存档：action=list 列出所有存档；info 读取某档的插件/工具/角色白名单；"
            + "create 从某档分出一个新档（label 命名）；configure 改某档的白名单"
            + "（plugins / tools 数组、characters 数组，空数组=不限制，另可给 allow_all_plugins / "
            + "allow_all_tools / allow_ai_install_plugins）。不填 save_id 时用当前存档。",
            async args =>
            {
                var services = ServicesHelper.GetServices();
                var saves = services?.GetService(typeof(SaveManager)) as SaveManager;
                var engine = services?.GetService(typeof(GameEngine)) as GameEngine;
                if (saves is null || engine is null)
                    return JsonSerializer.Serialize(new { ok = false, message = "存档服务不可用" });
                var action = GetString(args, "action", "list").Trim().ToLowerInvariant();
                var saveId = GetString(args, "save_id", "").Trim();
                if (saveId.Length == 0) saveId = engine.CurrentSaveId;

                switch (action)
                {
                    case "list":
                    {
                        var list = await saves.List();
                        return JsonSerializer.Serialize(new
                        {
                            ok = true,
                            current = engine.CurrentSaveId,
                            saves = list.Select(s => new
                            {
                                id = s.Id,
                                label = s.Label,
                                character = s.Character,
                                scene = s.Scene,
                                saved_at = s.SavedAt.ToString("yyyy-MM-dd HH:mm")
                            }).ToList()
                        });
                    }
                    case "info":
                    {
                        var rules = await saves.LoadRulesAsync(saveId);
                        if (rules is null) return JsonSerializer.Serialize(new { ok = false, message = "找不到这个存档" });
                        var p = rules.Value.Permissions;
                        return JsonSerializer.Serialize(new
                        {
                            ok = true,
                            save_id = saveId,
                            allow_all_plugins = p.AllowAllPlugins,
                            allow_all_tools = p.AllowAllTools,
                            allow_ai_install_plugins = p.AllowAiInstallPlugins,
                            plugins = p.AllowedPlugins,
                            tools = p.AllowedTools,
                            characters = rules.Value.AllowedCharacters,
                            character_rule = rules.Value.AllowedCharacters.Count == 0 ? "不限制" : "只允许名单里的角色"
                        });
                    }
                    case "create":
                    {
                        if (saveId.Length == 0) return JsonSerializer.Serialize(new { ok = false, message = "还没有可复制的存档" });
                        var (ok, message, id) = await saves.BranchAsync(saveId, GetString(args, "label", "分支存档"));
                        return JsonSerializer.Serialize(new { ok, message, save_id = id });
                    }
                    case "configure":
                    {
                        var rules = await saves.LoadRulesAsync(saveId);
                        if (rules is null) return JsonSerializer.Serialize(new { ok = false, message = "找不到这个存档" });
                        var p = rules.Value.Permissions;
                        var hasPlugins = TryGetStringList(args, "plugins", out var pluginList);
                        var hasTools = TryGetStringList(args, "tools", out var toolList);
                        var hasChars = TryGetStringList(args, "characters", out var charList);
                        var updated = p with
                        {
                            AllowAllPlugins = GetBool(args, "allow_all_plugins", hasPlugins ? false : p.AllowAllPlugins),
                            AllowAllTools = GetBool(args, "allow_all_tools", hasTools ? false : p.AllowAllTools),
                            AllowAiInstallPlugins = GetBool(args, "allow_ai_install_plugins", p.AllowAiInstallPlugins),
                            AllowedPlugins = hasPlugins ? pluginList! : p.AllowedPlugins,
                            AllowedTools = hasTools ? toolList! : p.AllowedTools,
                            AllowedCharacters = hasChars ? charList! : p.AllowedCharacters
                        };
                        var allowedChars = hasChars ? charList! : rules.Value.AllowedCharacters;
                        var ok = await saves.SaveRulesAsync(saveId, updated, allowedChars);
                        if (ok && string.Equals(saveId, engine.CurrentSaveId, StringComparison.Ordinal))
                        {
                            engine.State.Permissions = updated;
                            engine.State.AllowedCharacters.Clear();
                            foreach (var c in allowedChars) engine.State.AllowedCharacters.Add(c);
                        }
                        return JsonSerializer.Serialize(new
                        {
                            ok,
                            message = ok ? "存档白名单已更新。" : "保存失败：找不到这个存档。",
                            save_id = saveId,
                            allow_all_plugins = updated.AllowAllPlugins,
                            allow_all_tools = updated.AllowAllTools,
                            allow_ai_install_plugins = updated.AllowAiInstallPlugins,
                            allowed_characters = allowedChars
                        });
                    }
                    default:
                        return JsonSerializer.Serialize(new { ok = false, message = "action 只能是 list / info / create / configure" });
                }
            });

        RegisterBuiltin("character_create",
            "在角色库里新建一个角色：name、gender、personality、description、greeting。"
            + "add_to_save=true 时同时加进当前存档的角色白名单。头像一个用 generate_image 单独生成。",
            async args =>
            {
                var services = ServicesHelper.GetServices();
                var library = services?.GetService(typeof(CharacterLibrary)) as CharacterLibrary;
                var engine = services?.GetService(typeof(GameEngine)) as GameEngine;
                if (library is null || engine is null)
                    return JsonSerializer.Serialize(new { ok = false, message = "角色服务不可用" });
                var name = GetString(args, "name", "").Trim();
                if (name.Length == 0) return JsonSerializer.Serialize(new { ok = false, message = "需要 name" });
                var created = library.CreateDefault(name, GetString(args, "gender", ""), GetString(args, "personality", ""));
                var profile = created.Profile with
                {
                    Description = GetString(args, "description", created.Profile.Description),
                    Greeting = GetString(args, "greeting", created.Profile.Greeting)
                };
                created = created with { Profile = profile };
                var ok = await library.AddAsync(created);
                if (ok && GetBool(args, "add_to_save"))
                    engine.State.AllowedCharacters.Add(profile.Id);
                return JsonSerializer.Serialize(new
                {
                    ok,
                    id = profile.Id,
                    name = profile.Name,
                    gender = profile.Gender,
                    added_to_save = ok && GetBool(args, "add_to_save"),
                    message = ok ? $"角色「{profile.Name}」已加入角色库。" : "角色保存失败"
                });
            });

        RegisterBuiltin("install_plugin",
            "安装一个插件（zip 包路径或下载地址）。只有存档高级设置允许 AI 安装插件时才可用；"
            + "安装后要提醒用户检查设置页与权限。",
            async args =>
            {
                var services = ServicesHelper.GetServices();
                var host = services?.GetService(typeof(PluginHost)) as PluginHost;
                var engine = services?.GetService(typeof(GameEngine)) as GameEngine;
                if (host is null || engine is null)
                    return JsonSerializer.Serialize(new { ok = false, message = "插件服务不可用" });
                if (!engine.State.Permissions.AllowAiInstallPlugins)
                    return JsonSerializer.Serialize(new
                    {
                        ok = false,
                        denied = true,
                        message = "当前存档不允许 AI 自行安装插件。请用户在插件中心手动安装，或在存档高级设置里放行。"
                    });
                var source = GetString(args, "source", "").Trim();
                if (source.Length == 0)
                    return JsonSerializer.Serialize(new { ok = false, message = "需要 source（zip 路径或 https 地址）" });
                string? temp = null;
                try
                {
                    if (source.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    {
                        temp = Path.Combine(Path.GetTempPath(), "wab_plugin_" + Guid.NewGuid().ToString("N")[..8] + ".zip");
                        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
                        var bytes = await http.GetByteArrayAsync(source);
                        await File.WriteAllBytesAsync(temp, bytes);
                        source = temp;
                    }
                    var (ok, message) = await host.InstallFromZipAsync(source, overwrite: false);
                    return JsonSerializer.Serialize(new { ok, message });
                }
                finally
                {
                    if (temp is not null) { try { File.Delete(temp); } catch { } }
                }
            });

        // ===== MCP（模型上下文协议）工具：工坊新建的 MCP 在这里对 AI 可见/可调 =====
        RegisterBuiltin("mcp_list",
            "列出已经装好的 MCP 工具（工坊生成的本地工具、导入的数据包与内置工具）。"
            + "要用某个外部能力、或不确定自己现在会什么时，先看这张表。",
            async _ =>
            {
                var services = ServicesHelper.GetServices();
                var mcp = services?.GetService(typeof(McpOrchestrator)) as McpOrchestrator;
                if (mcp is null) return JsonSerializer.Serialize(new { ok = false, message = "MCP 服务不可用" });
                var tools = mcp.List().Select(t => new
                {
                    name = t.Name,
                    description = t.Description,
                    server = t.Server,
                    category = t.Category,
                    kind = t.Kind
                }).ToList();
                return JsonSerializer.Serialize(new
                {
                    ok = true,
                    enabled = mcp.Enabled,
                    count = tools.Count,
                    tools,
                    note = mcp.Enabled ? "用 mcp_call 调用；name 必须是 tools 里的名字。" : "MCP 总开关已关闭，暂时不能调用。"
                });
            });

        RegisterBuiltin("mcp_call",
            "调用一个 MCP 工具。name 取自 mcp_list；arguments 是 JSON 对象（或 JSON 字符串）。"
            + "工具返回什么就如实转述，不要编造没发生的调用结果。",
            async args =>
            {
                var services = ServicesHelper.GetServices();
                var mcp = services?.GetService(typeof(McpOrchestrator)) as McpOrchestrator;
                if (mcp is null) return JsonSerializer.Serialize(new { ok = false, message = "MCP 服务不可用" });
                var name = GetString(args, "name", "").Trim();
                if (name.Length == 0) return JsonSerializer.Serialize(new { ok = false, message = "需要 name（取自 mcp_list）" });
                var arguments = "{}";
                try
                {
                    using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(args) ? "{}" : args);
                    if (doc.RootElement.TryGetProperty("arguments", out var a))
                        arguments = a.ValueKind == JsonValueKind.String
                            ? (a.GetString() ?? "{}")
                            : a.GetRawText();
                }
                catch (Exception ex) { App.WriteLog("ToolManager.mcp_call parse -> " + ex.Message); }
                var result = await mcp.Run(name, arguments);
                return JsonSerializer.Serialize(new { ok = !result.StartsWith("[MCP]", StringComparison.Ordinal), tool = name, result });
            },
            new List<ToolParameter>
            {
                new() { Name = "name", Description = "MCP 工具名（见 mcp_list）", Required = true },
                new() { Name = "arguments", Description = "传给工具的 JSON 对象，默认 {}", Required = false }
            });
    }

    private static async Task<string> GenerateImageAsync(string? args)
    {
        var services = ServicesHelper.GetServices();
        var images = services?.GetService(typeof(ImageGenerationService)) as ImageGenerationService;
        var engine = services?.GetService(typeof(GameEngine)) as GameEngine;
        var library = services?.GetService(typeof(CharacterLibrary)) as CharacterLibrary;
        var map = services?.GetService(typeof(MapService)) as MapService;
        if (images is null || engine is null || library is null || map is null)
            return JsonSerializer.Serialize(new { ok = false, message = "画面服务不可用" });

        var kind = GetString(args, "kind", "background").Trim().ToLowerInvariant();
        var prompt = GetString(args, "prompt", "").Trim();
        if (prompt.Length == 0)
            return JsonSerializer.Serialize(new { ok = false, message = "需要 prompt（画面描述）" });
        if (!images.Enabled)
            return JsonSerializer.Serialize(new { ok = false, message = "画面生成没有开启（设置 → AI → 画面生成）。" });

        var charId = engine.State.CharacterId;
        var ch = engine.ActiveCharacter
                 ?? (engine.Roster.TryGetValue(charId, out var found) ? found : null);

        try
        {
            switch (kind)
            {
                case "avatar":
                case "outfit":
                {
                    if (ch is null) return JsonSerializer.Serialize(new { ok = false, message = "当前没有角色" });
                    var appearance = string.IsNullOrWhiteSpace(ch.Profile.Description) ? "" : "；角色设定：" + ch.Profile.Description;
                    var size = kind == "outfit" ? "1024x1536" : "1024x1024";
                    var (path, message) = await images.GenerateAsync(
                        kind == "outfit"
                            ? $"{ch.Profile.Name} 的全身立绘，{prompt}。干净背景，完整人物，不要文字{appearance}"
                            : $"{ch.Profile.Name} 的头像，{prompt}。干净背景，不要文字{appearance}",
                        kind + "_" + charId, size);
                    if (path is null) return JsonSerializer.Serialize(new { ok = false, message });

                    var destDir = Path.Combine(App.RootDirectory, "assets", "characters", charId,
                        kind == "outfit" ? "generated" : "avatar");
                    Directory.CreateDirectory(destDir);
                    var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    var dest = Path.Combine(destDir, $"{stamp}.png");
                    File.Copy(path, dest, true);
                    var rel = $"assets/characters/{charId}/{(kind == "outfit" ? "generated" : "avatar")}/{stamp}.png";

                    if (kind == "avatar")
                    {
                        await library.UpdateAsync(ch with { Avatar = rel });
                        return JsonSerializer.Serialize(new { ok = true, kind, path = rel, message = "头像已换好。" });
                    }

                    var outfitKey = "generated_" + stamp;
                    var sprites = new Dictionary<string, string>(ch.SpriteMap, StringComparer.OrdinalIgnoreCase);
                    foreach (var emotion in new[] { "normal", "smile", "shy", "sad", "angry" })
                        sprites[$"{outfitKey}/{emotion}"] = rel;
                    ch.State.CurrentOutfit = outfitKey;
                    ch.State.CurrentEmotion = "normal";
                    await library.UpdateAsync(ch with { SpriteMap = sprites });
                    return JsonSerializer.Serialize(new
                    {
                        ok = true,
                        kind,
                        outfit = outfitKey,
                        path = rel,
                        message = $"新装扮「{outfitKey}」已穿上。"
                    });
                }

                case "scene":
                case "map":
                {
                    await map.InitializeAsync();
                    var name = GetString(args, "name", "").Trim();
                    if (name.Length == 0) name = "新场景";
                    var locationName = GetString(args, "location", "").Trim();
                    var loc = map.Map.Locations.FirstOrDefault(l =>
                                  string.Equals(l.Name, locationName, StringComparison.Ordinal))
                              ?? map.Map.Locations.FirstOrDefault();
                    if (loc is null) return JsonSerializer.Serialize(new { ok = false, message = "地图里还没有地点" });
                    var scene = map.AddScene(loc.Id, name);
                    var current = map.CurrentScene;
                    if (current is not null) map.AddEdge(current.Id, scene.Id);
                    var (path, msg) = await images.GenerateAsync(
                        $"场景背景：{name}，{prompt}。横向构图，不要人物，不要文字", "scene_" + scene.Id, "1536x1024");
                    if (path is null) return JsonSerializer.Serialize(new { ok = false, message = msg });
                    var rel = map.ImportBackgroundForScene(path, scene);
                    if (rel is null) return JsonSerializer.Serialize(new { ok = false, message = "背景复制失败" });
                    scene.Background = rel;
                    scene.Placement = null;
                    await map.SaveAsync();
                    map.NotifyChanged();
                    return JsonSerializer.Serialize(new
                    {
                        ok = true,
                        kind,
                        scene = scene.Name,
                        scene_id = scene.Id,
                        location = loc.Name,
                        path = rel,
                        message = $"已在「{loc.Name}」新增场景「{scene.Name}」并接上当前场景。背景还没标定，第一次进入时会请你标一下。"
                    });
                }

                case "character":
                {
                    var name = GetString(args, "name", "").Trim();
                    if (name.Length == 0) name = "新角色";
                    var personality = GetString(args, "personality", "").Trim();
                    var description = GetString(args, "description", "").Trim();
                    var gender = GetString(args, "gender", "").Trim();
                    var created = library.CreateDefault(name, gender, personality);
                    var profile = created.Profile with
                    {
                        Description = string.IsNullOrWhiteSpace(description) ? prompt : description,
                        Greeting = GetString(args, "greeting", created.Profile.Greeting)
                    };
                    created = created with { Profile = profile };
                    var (path, msg) = await images.GenerateAsync(
                        $"{name} 的头像，{prompt}。干净背景，不要文字", "character_" + profile.Id);
                    if (path is not null)
                    {
                        var dir = Path.Combine(App.RootDirectory, "assets", "characters", profile.Id, "avatar");
                        Directory.CreateDirectory(dir);
                        var dest = Path.Combine(dir, "avatar.png");
                        File.Copy(path, dest, true);
                        created = created with { Avatar = $"assets/characters/{profile.Id}/avatar/avatar.png" };
                    }
                    var ok = await library.AddAsync(created);
                    return JsonSerializer.Serialize(new
                    {
                        ok,
                        kind,
                        id = profile.Id,
                        name = profile.Name,
                        avatar = created.Avatar,
                        message = ok ? $"角色「{profile.Name}」已加入角色库。" + (path is null ? "头像没生成：" + msg : "")
                                     : "角色保存失败"
                    });
                }

                default:
                {
                    await map.InitializeAsync();
                    var sceneName = GetString(args, "scene", "").Trim();
                    var scene = sceneName.Length == 0 ? map.CurrentScene : map.Map.SceneByName(sceneName);
                    if (scene is null) return JsonSerializer.Serialize(new { ok = false, message = "没有找到这个场景" });
                    var (path, msg) = await images.GenerateAsync(
                        $"场景背景：{scene.Name}，{prompt}。横向构图，不要人物，不要文字", "bg_" + scene.Id, "1536x1024");
                    if (path is null) return JsonSerializer.Serialize(new { ok = false, message = msg });
                    var rel = map.ImportBackgroundForScene(path, scene);
                    if (rel is null) return JsonSerializer.Serialize(new { ok = false, message = "背景复制失败" });
                    scene.Background = rel;
                    scene.Placement = null;
                    await map.SaveAsync();
                    map.NotifyChanged();
                    return JsonSerializer.Serialize(new
                    {
                        ok = true,
                        kind = "background",
                        scene = scene.Name,
                        path = rel,
                        message = $"「{scene.Name}」的背景已换好，尚未标定，第一次进入时会请你标一下。"
                    });
                }
            }
        }
        catch (Exception ex)
        {
            App.WriteLog("ToolManager.generate_image -> " + ex);
            return JsonSerializer.Serialize(new { ok = false, message = "生成失败：" + ex.Message });
        }
    }

    private static string GetString(string? json, string key, string fallback)
    {
        if (string.IsNullOrWhiteSpace(json)) return fallback;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString() ?? fallback
                : fallback;
        }
        catch { return fallback; }
    }

    private static int GetInt(string? json, string key, int fallback)
    {
        if (string.IsNullOrWhiteSpace(json)) return fallback;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty(key, out var v)) return fallback;
            if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i)) return i;
            if (v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), out var s)) return s;
            return fallback;
        }
        catch { return fallback; }
    }

    private static bool TryGetStringList(string? json, string key, out List<string>? list)
    {
        list = null;
        if (string.IsNullOrWhiteSpace(json)) return false;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty(key, out var v) || v.ValueKind != JsonValueKind.Array) return false;
            list = v.EnumerateArray()
                .Where(x => x.ValueKind == JsonValueKind.String)
                .Select(x => (x.GetString() ?? "").Trim())
                .Where(s => s.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            return true;
        }
        catch { return false; }
    }

    private static bool GetBool(string? json, string key, bool fallback = false)
    {
        if (string.IsNullOrWhiteSpace(json)) return fallback;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty(key, out var v)) return fallback;
            return v.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String => bool.TryParse(v.GetString(), out var b) && b,
                _ => fallback
            };
        }
        catch { return fallback; }
    }
}
