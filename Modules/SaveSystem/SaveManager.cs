using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using WarmAsBefore.Models;
using WarmAsBefore.Modules.Screen;
using WarmAsBefore.Services;

namespace WarmAsBefore.Modules.SaveSystem;

public sealed class SaveManager
{
    private readonly StorageProvider _store;
    private readonly GameEngine _engine;
    private readonly CharacterLibrary _library;
    private readonly MemoryScope _scope;
    private readonly PluginHost _plugins;

    public SaveManager(StorageProvider store, GameEngine engine, CharacterLibrary library, MemoryScope scope,
        PluginHost plugins)
    {
        _store = store;
        _engine = engine;
        _library = library;
        _scope = scope;
        _plugins = plugins;
    }

    /// <summary>当前会话的聊天记录：随每次保存写入存档，读档后恢复聊天界面。</summary>
    public List<ChatRecord> ChatLog { get; } = new();

    /// <summary>存档结构版本：旧档（缺此字段 = 0）在加载时自动迁移修复。</summary>
    public const int SchemaVersion = 1;

    /// <summary>上一次读档时的自动修复说明（空 = 无需修复）。</summary>
    public string LastLoadNote { get; private set; } = "";

    /// <summary>新开一局：分配新的存档槽位（同一角色可开多局，各自成档）。</summary>
    public string NewRun()
    {
        _engine.CurrentSaveId = Guid.NewGuid().ToString("N")[..12];
        ChatLog.Clear();
        _scope.Raise();      // 新的一局 = 新的一份记忆，不能接着写上一个存档的记忆
        return _engine.CurrentSaveId;
    }

    /// <summary>保存当前进度：写入当前槽位（不产生新档）；无当前槽位时新建。</summary>
    public async Task<bool> Commit(string label, string mode = "para")
    {
        try
        {
            if (string.IsNullOrEmpty(_engine.CurrentSaveId)) NewRun();
            var slot = new SaveSlot
            {
                Id = _engine.CurrentSaveId,
                Label = label,
                Character = _engine.State.CharacterId,
                Scene = _engine.State.Location
            };
            var data = new Dictionary<string, object>
            {
                ["state"] = _engine.State,
                ["meta"] = slot,
                ["mode"] = mode,
                ["version"] = SchemaVersion,
                ["char_state"] = _engine.ActiveCharacter?.State,
                ["chat"] = ChatLog.TakeLast(60).ToList()
            };
            await _store.Save($"saves/{slot.Id}", data);
            return true;
        }
        catch { return false; }
    }

    public bool HasImportedNovel()
    {
        return _store.Exists("imports/novel");
    }

    public bool HasImportedCharacter()
    {
        return _store.Exists("imports/character");
    }

    public async Task<bool> Load(string id)
    {
        var json = await _store.LoadRawAsync($"saves/{id}");
        if (string.IsNullOrEmpty(json)) return false;
        // 角色库载入与读档反序列化并行，缩短启动读档耗时
        var libTask = _library.ListAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.TryGetProperty("state", out var st))
        {
            var state = st.Deserialize<GameState>();
            if (state is not null) _engine.RestoreState(state);
        }
        await libTask;   // 恢复角色状态前确保角色库已载入
        // 恢复角色状态（好感/信任等）与聊天记录
        if (!string.IsNullOrEmpty(_engine.State.CharacterId)
            && root.TryGetProperty("char_state", out var cs)
            && _engine.Roster.TryGetValue(_engine.State.CharacterId, out var ch))
        {
            var cstate = cs.Deserialize<CharacterState>();
            if (cstate is not null) ch.State = cstate;
        }
        ChatLog.Clear();
        if (root.TryGetProperty("chat", out var chat))
        {
            var records = chat.Deserialize<List<ChatRecord>>();
            if (records is not null) ChatLog.AddRange(records);
        }
        _engine.CurrentSaveId = id;
        _scope.Raise();      // 换档之后，各记忆服务必须重读这个档自己的那一份

        // 过期存档自动修复：旧档缺版本字段（=0）或结构漂移时，就地补齐并回写。
        var version = root.TryGetProperty("version", out var v) && v.TryGetInt32(out var ver) ? ver : 0;
        LastLoadNote = version >= SchemaVersion ? "" : await RepairAsync(id, version);
        if (!string.IsNullOrEmpty(LastLoadNote)) App.WriteLog("SaveManager.Repair -> " + LastLoadNote);

        // 手机端进入存档自动横屏（桌面/平板无方向锁概念）
        try { ScreenOrientationService.EnterSaveLandscape(); }
        catch (Exception ex) { App.WriteLog("SaveManager.EnterLandscape -> " + ex.Message); }
        return true;
    }

    /// <summary>
    /// 过期存档修复：把结构落后的存档补齐到当前版本。
    /// 只做「能确定的修正」——越界数值夹回范围、坏行丢弃、缺失的引用补种，绝不猜测剧情内容。
    /// </summary>
    private async Task<string> RepairAsync(string id, int fromVersion)
    {
        var fixes = new List<string>();

        var state = _engine.State;
        // 白名单里指向已不存在的角色：清掉，否则提示词里会出现空引用
        if (state.AllowedCharacters.Count > 0)
        {
            var known = _engine.Roster.Keys.ToHashSet(StringComparer.Ordinal);
            var droppedIds = state.AllowedCharacters.Where(c => !known.Contains(c)).ToList();
            foreach (var d in droppedIds) state.AllowedCharacters.Remove(d);
            if (droppedIds.Count > 0) fixes.Add($"清理了 {droppedIds.Count} 个已不存在的可用角色");
        }

        // 聊天记录里的空行（旧版异常退出可能留下）
        var badChat = ChatLog.Count(r => string.IsNullOrWhiteSpace(r.Text));
        if (badChat > 0)
        {
            ChatLog.RemoveAll(r => string.IsNullOrWhiteSpace(r.Text));
            fixes.Add($"丢弃了 {badChat} 条空聊天记录");
        }

        // 角色数值越界（旧版/手改档）：夹回合法范围
        if (_engine.ActiveCharacter is { } ch)
        {
            var st = ch.State;
            var before = (st.Affection, st.AffectionPoints, st.Trust, st.Energy, st.EnergyOffset);
            st.Affection = Math.Clamp(st.Affection, 0, 100);
            st.AffectionPoints = Math.Clamp(st.AffectionPoints, 0, 5000);
            st.Trust = Math.Clamp(st.Trust, 0, 100);
            st.Energy = Math.Clamp(st.Energy, 0, 100);
            st.EnergyOffset = Math.Clamp(st.EnergyOffset, -60, 40);
            if (before != (st.Affection, st.AffectionPoints, st.Trust, st.Energy, st.EnergyOffset))
                fixes.Add("把越界的好感/信任/精力数值夹回合法范围");
        }

        var note = fixes.Count == 0
            ? ""
            : $"存档已自动修复（结构版本 {fromVersion} → {SchemaVersion}）：" + string.Join("；", fixes) + "。";

        // 回写：升级后的版本号与修复结果一并落盘，避免每次读档都重复修
        await Commit(string.IsNullOrWhiteSpace(_engine.ActiveCharacter?.Profile.Name)
            ? "自动修复" : _engine.ActiveCharacter!.Profile.Name, "repair");
        return note;
    }

    /// <summary>给存档重命名：改写 meta.Label 并回写文件。</summary>
    public async Task<bool> Rename(string id, string newLabel)
    {
        try
        {
            var json = await _store.LoadRawAsync($"saves/{id}");
            if (string.IsNullOrEmpty(json)) return false;
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("meta", out var m)) return false;
            var slot = m.Deserialize<SaveSlot>();
            if (slot is null) return false;
            // 只替换 meta 段，其余原样保留
            using var ms = new MemoryStream();
            using (var writer = new Utf8JsonWriter(ms))
            {
                writer.WriteStartObject();
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (prop.Name == "meta")
                    {
                        writer.WritePropertyName("meta");
                        writer.WriteStartObject();
                        foreach (var mp in m.EnumerateObject())
                        {
                            if (mp.Name == nameof(SaveSlot.Label))
                                writer.WriteString(nameof(SaveSlot.Label), newLabel.Trim());
                            else
                                mp.WriteTo(writer);
                        }
                        writer.WriteEndObject();
                    }
                    else
                    {
                        prop.WriteTo(writer);
                    }
                }
                writer.WriteEndObject();
            }
            await File.WriteAllBytesAsync(Path.Combine(_store.Root, "saves", id + ".json"), ms.ToArray());
            return true;
        }
        catch (Exception ex)
        {
            App.WriteLog("SaveManager.Rename -> " + ex);
            return false;
        }
    }

    /// <summary>
    /// 从已有存档分出一个新档（插件 / AI 的「新增存档」）：复制同一份进度，换新 id 与新名字，
    /// 不改动原档，也不打断当前进度。
    /// </summary>
    public async Task<(bool Ok, string Message, string Id)> BranchAsync(string sourceId, string label)
    {
        try
        {
            var json = await _store.LoadRawAsync($"saves/{sourceId}");
            if (string.IsNullOrEmpty(json)) return (false, "原存档不存在。", "");
            if (JsonNode.Parse(json) is not JsonObject root || root["meta"] is not JsonObject meta)
                return (false, "原存档结构不完整。", "");
            var newId = Guid.NewGuid().ToString("N")[..12];
            meta["Id"] = newId;
            meta["Label"] = string.IsNullOrWhiteSpace(label) ? "分支存档" : label.Trim();
            meta["SavedAt"] = JsonSerializer.SerializeToNode(DateTime.Now);
            await _store.WriteRawAsync($"saves/{newId}", root.ToJsonString(WriteJson));
            return (true, $"已从当前进度新增存档「{meta["Label"]}」。", newId);
        }
        catch (Exception ex)
        {
            App.WriteLog("SaveManager.Branch -> " + ex);
            return (false, "新增存档失败：" + ex.Message, "");
        }
    }

    /// <summary>某角色名下的全部存档（用于角色选择页显示「继续」入口）。</summary>
    public async Task<List<SaveSlot>> ListByCharacter(string charId)
    {
        var all = await List();
        return all.Where(s => s.Character == charId).ToList();
    }

    /// <summary>读取最后一次游玩的存档（按保存时间最新的一个）并恢复游戏状态。</summary>
    public async Task<bool> LoadLatest()
    {
        var slots = await List();
        // 跳过没有绑定角色的空档（如旧版本产生的坏档）
        var valid = slots.Where(s => !string.IsNullOrEmpty(s.Character)).ToList();
        if (valid.Count == 0) return false;
        return await Load(valid[0].Id);
    }

    public async Task<List<SaveSlot>> List()
    {
        var dir = Path.Combine(_store.Root, "saves");
        if (!Directory.Exists(dir)) return new();
        var list = new List<SaveSlot>();
        foreach (var f in Directory.GetFiles(dir, "*.json"))
        {
            try
            {
                var json = await File.ReadAllTextAsync(f);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("meta", out var m))
                {
                    var slot = m.Deserialize<SaveSlot>();
                    if (slot is not null) list.Add(slot);
                }
            }
            catch { }
        }
        return list.OrderByDescending(s => s.SavedAt).ToList();
    }

    public async Task<bool> Export(string id, string path)
    {
        var json = await _store.LoadRawAsync($"saves/{id}");
        if (string.IsNullOrEmpty(json)) return false;
        await File.WriteAllTextAsync(path, json);
        return true;
    }

    /// <summary>
    /// 导出单个存档（文件名 + 字节流，供系统保存对话框使用）。
    /// includePlugins = true 时打成 .wbsave：存档 JSON + 所用插件代码，防止「有数据没代码」。
    /// 插件设置（settings.json）不随包外带——里面可能有密钥，用户把存档发给别人时不该泄露。
    /// </summary>
    public async Task<(string name, byte[] bytes)?> ExportStream(string id, bool includePlugins = false)
    {
        var json = await _store.LoadRawAsync($"saves/{id}");
        if (string.IsNullOrEmpty(json)) return null;
        var label = id;
        var characterId = "";
        using (var doc = JsonDocument.Parse(json))
        {
            if (doc.RootElement.TryGetProperty("meta", out var m))
            {
                var slot = m.Deserialize<SaveSlot>();
                if (slot is not null)
                {
                    label = slot.Label;
                    characterId = slot.Character;
                }
            }
        }
        if (!includePlugins)
            return ($"存档_{label}.json", System.Text.Encoding.UTF8.GetBytes(json));

        try
        {
            using var ms = new MemoryStream();
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
            {
                WriteZipText(zip, "save.json", json);
                var used = await PluginsUsedByAsync(characterId, json);
                var manifest = new
                {
                    format = "wab-save-bundle",
                    bundleVersion = 1,
                    app = "WarmAsBefore",
                    exportedAt = DateTime.Now,
                    saveId = id,
                    label,
                    character = characterId,
                    pluginSettingsIncluded = false,
                    plugins = used.Select(p => new { p.Manifest.Id, p.Manifest.Name, p.Manifest.Version })
                };
                WriteZipText(zip, "manifest.json", JsonSerializer.Serialize(manifest, WriteJson));
                foreach (var p in used)
                {
                    var baseDir = Path.GetFullPath(p.Dir);
                    foreach (var file in Directory.GetFiles(baseDir, "*", SearchOption.AllDirectories))
                    {
                        var rel = Path.GetRelativePath(baseDir, file).Replace('\\', '/');
                        // 插件设置不随存档外带（里面可能有用户填的密钥）；
                        // 旧版本误把绝对路径当存储键，落盘成 settings.json.json，那种残留文件同样不能带。
                        if (Path.GetFileName(rel).StartsWith("settings.json", StringComparison.OrdinalIgnoreCase)) continue;
                        var entry = zip.CreateEntry($"plugins/{p.Manifest.Id}/{rel}", CompressionLevel.Optimal);
                        await using var es = entry.Open();
                        await using var fs = File.OpenRead(file);
                        await fs.CopyToAsync(es);
                    }
                }
            }
            var safeLabel = string.Concat(label.Where(ch => !Path.GetInvalidFileNameChars().Contains(ch)));
            if (string.IsNullOrWhiteSpace(safeLabel)) safeLabel = id;
            return ($"存档_{safeLabel}.wbsave", ms.ToArray());
        }
        catch (Exception ex)
        {
            App.WriteLog("SaveManager.ExportBundle -> " + ex);
            return null;
        }
    }

    /// <summary>这份存档实际用到的插件：存档权限白名单里点名的，加上启用中的全部。</summary>
    private async Task<List<PluginEntry>> PluginsUsedByAsync(string characterId, string saveJson)
    {
        var all = await _plugins.AllAsync();
        if (all.Count == 0) return new();
        SavePermissions? permissions = null;
        try
        {
            using var doc = JsonDocument.Parse(saveJson);
            if (doc.RootElement.TryGetProperty("state", out var st)
                && st.TryGetProperty(nameof(GameState.Permissions), out var perm))
                permissions = perm.Deserialize<SavePermissions>();
        }
        catch { }
        return all
            .Where(p => p.Enabled)
            .Where(p => permissions is null || permissions.AllowAllPlugins
                        || permissions.AllowedPlugins.Contains(p.Manifest.Id, StringComparer.OrdinalIgnoreCase))
            .ToList();
    }

    private static void WriteZipText(ZipArchive zip, string path, string text)
    {
        var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new System.Text.UTF8Encoding(false));
        writer.Write(text);
    }

    private static readonly JsonSerializerOptions WriteJson = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true
    };

    /// <summary>备份全部存档到指定文件夹（复制 saves 目录）。</summary>
    public async Task<int> Backup(string destFolder)
    {
        var src = Path.Combine(_store.Root, "saves");
        if (!Directory.Exists(src)) return 0;
        Directory.CreateDirectory(destFolder);
        var n = 0;
        foreach (var f in Directory.GetFiles(src, "*.json"))
        {
            File.Copy(f, Path.Combine(destFolder, Path.GetFileName(f)), overwrite: true);
            n++;
        }
        return n;
    }

    /// <summary>导入存档：普通 .json 直接写入；.wbsave 是 zip，先取存档，再补齐缺失的插件。</summary>
    public async Task<(bool Ok, string Message)> Import(string path)
    {
        try
        {
            if (IsZip(path)) return await ImportBundleAsync(path);
            var json = await File.ReadAllTextAsync(path);
            var (ok, label) = await ImportSaveJsonAsync(json);
            return ok ? (true, $"已导入存档「{label}」。") : (false, "导入失败：文件格式不正确。");
        }
        catch (Exception ex)
        {
            App.WriteLog("SaveManager.Import -> " + ex);
            return (false, "导入失败：" + ex.Message);
        }
    }

    private static bool IsZip(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            Span<byte> head = stackalloc byte[4];
            return fs.Read(head) == 4 && head[0] == 0x50 && head[1] == 0x4B
                   && head[2] is 0x03 or 0x05 or 0x07;
        }
        catch { return false; }
    }

    private async Task<(bool Ok, string Label)> ImportSaveJsonAsync(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("meta", out var m)) return (false, "");
        var slot = m.Deserialize<SaveSlot>();
        if (slot is null || string.IsNullOrWhiteSpace(slot.Id)) return (false, "");
        var dest = Path.Combine(_store.Root, "saves", slot.Id + ".json");
        var dir = Path.GetDirectoryName(dest);
        if (dir is not null) Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(dest, json);   // 原样写入，不做二次序列化
        return (true, slot.Label);
    }

    /// <summary>
    /// 导入 .wbsave：存档本体优先，插件只补缺、不覆盖已装版本（避免别人的旧插件盖掉本地新版本）。
    /// 缺依赖时不失败，交给插件中心显示安装入口。
    /// </summary>
    private async Task<(bool Ok, string Message)> ImportBundleAsync(string path)
    {
        var tmp = Path.Combine(Path.GetTempPath(), "wab_save_" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            Directory.CreateDirectory(tmp);
            ZipFile.ExtractToDirectory(path, tmp, true);
            var saveFile = Directory.GetFiles(tmp, "save.json", SearchOption.AllDirectories).FirstOrDefault();
            if (saveFile is null) return (false, "这个包里没有 save.json，不是有效的存档包。");
            var (ok, label) = await ImportSaveJsonAsync(await File.ReadAllTextAsync(saveFile));
            if (!ok) return (false, "包里的存档格式不正确。");

            var installed = 0;
            var skipped = 0;
            var failed = 0;
            var pluginsRoot = Path.Combine(Path.GetDirectoryName(saveFile)!, "plugins");
            if (Directory.Exists(pluginsRoot))
            {
                foreach (var dir in Directory.GetDirectories(pluginsRoot))
                {
                    if (!File.Exists(Path.Combine(dir, "plugin.json"))) continue;
                    try
                    {
                        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(dir, "plugin.json")));
                        var id = doc.RootElement.TryGetProperty("id", out var idv) && idv.ValueKind == JsonValueKind.String
                            ? idv.GetString() ?? Path.GetFileName(dir)
                            : Path.GetFileName(dir);
                        if (await _plugins.FindAsync(id) is not null)
                        {
                            skipped++;
                            continue;
                        }
                    }
                    catch { }
                    var result = await _plugins.InstallFromFolderAsync(dir);
                    if (result.Ok) installed++;
                    else failed++;
                }
            }

            var pluginsNote = installed == 0 && skipped == 0 && failed == 0
                ? "这个包没有附带插件。"
                : $"附带插件：新装 {installed} 个，已存在跳过 {skipped} 个" + (failed > 0 ? $"，失败 {failed} 个" : "") + "。";
            return (true, $"已导入存档「{label}」。{pluginsNote}");
        }
        catch (Exception ex)
        {
            App.WriteLog("SaveManager.ImportBundle -> " + ex);
            return (false, "导入失败：" + ex.Message);
        }
        finally
        {
            try { Directory.Delete(tmp, true); } catch { }
        }
    }

    /// <summary>存档高级设置：不进入游戏也能读写这份存档的插件/工具/角色白名单。</summary>
    public async Task<(SavePermissions Permissions, List<string> AllowedCharacters)?> LoadRulesAsync(string id)
    {
        var json = await _store.LoadRawAsync($"saves/{id}");
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("state", out var st)) return null;
            var state = st.Deserialize<GameState>();
            if (state is null) return null;
            return (state.Permissions, state.AllowedCharacters.ToList());
        }
        catch { return null; }
    }

    public async Task<bool> SaveRulesAsync(string id, SavePermissions permissions, IEnumerable<string> allowedCharacters)
    {
        var json = await _store.LoadRawAsync($"saves/{id}");
        if (string.IsNullOrEmpty(json)) return false;
        try
        {
            if (JsonNode.Parse(json) is not JsonObject root) return false;
            if (root["state"] is not JsonObject stateNode) return false;
            var state = stateNode.Deserialize<GameState>();
            if (state is null) return false;
            state.Permissions = permissions;
            state.AllowedCharacters.Clear();
            foreach (var c in allowedCharacters) state.AllowedCharacters.Add(c);
            root["state"] = JsonSerializer.SerializeToNode(state);
            await _store.WriteRawAsync($"saves/{id}", root.ToJsonString(WriteJson));
            return true;
        }
        catch (Exception ex)
        {
            App.WriteLog("SaveManager.SaveRules -> " + ex);
            return false;
        }
    }

    public Task<bool> Delete(string id)
    {
        if (!_store.Exists($"saves/{id}")) return Task.FromResult(false);
        _store.Delete($"saves/{id}");
        return Task.FromResult(true);
    }
}