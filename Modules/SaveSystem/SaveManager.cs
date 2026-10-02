using System.Text.Json;
using WarmAsBefore.Models;
using WarmAsBefore.Services;

namespace WarmAsBefore.Modules.SaveSystem;

public sealed class SaveManager
{
    private readonly StorageProvider _store;
    private readonly GameEngine _engine;
    private readonly CharacterLibrary _library;

    public SaveManager(StorageProvider store, GameEngine engine, CharacterLibrary library)
    {
        _store = store;
        _engine = engine;
        _library = library;
    }

    /// <summary>当前会话的聊天记录：随每次保存写入存档，读档后恢复聊天界面。</summary>
    public List<ChatRecord> ChatLog { get; } = new();

    /// <summary>新开一局：分配新的存档槽位（同一角色可开多局，各自成档）。</summary>
    public string NewRun()
    {
        _engine.CurrentSaveId = Guid.NewGuid().ToString("N")[..12];
        ChatLog.Clear();
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
        return true;
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

    /// <summary>导出单个存档（文件名 + 字节流，供系统保存对话框使用）。</summary>
    public async Task<(string name, byte[] bytes)?> ExportStream(string id)
    {
        var json = await _store.LoadRawAsync($"saves/{id}");
        if (string.IsNullOrEmpty(json)) return null;
        var label = id;
        using (var doc = JsonDocument.Parse(json))
        {
            if (doc.RootElement.TryGetProperty("meta", out var m))
            {
                var slot = m.Deserialize<SaveSlot>();
                if (slot is not null) label = slot.Label;
            }
        }
        return ($"存档_{label}.json", System.Text.Encoding.UTF8.GetBytes(json));
    }

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

    public async Task<bool> Import(string path)
    {
        try
        {
            var json = await File.ReadAllTextAsync(path);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("meta", out var m)) return false;
            var slot = m.Deserialize<SaveSlot>();
            if (slot is null) return false;
            var dest = Path.Combine(_store.Root, "saves", slot.Id + ".json");
            var dir = Path.GetDirectoryName(dest);
            if (dir is not null) Directory.CreateDirectory(dir);
            await File.WriteAllTextAsync(dest, json);   // 原样写入，不做二次序列化
            return true;
        }
        catch { return false; }
    }

    public Task<bool> Delete(string id)
    {
        if (!_store.Exists($"saves/{id}")) return Task.FromResult(false);
        _store.Delete($"saves/{id}");
        return Task.FromResult(true);
    }
}