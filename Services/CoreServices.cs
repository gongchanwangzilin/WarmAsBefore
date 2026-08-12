using System.Text.Json;
using WarmAsBefore.Models;

namespace WarmAsBefore.Services;

public sealed class GameEngine
{
    public GameState State { get; private set; } = new();
    public CharacterData? ActiveCharacter { get; private set; }
    public Dictionary<string, CharacterData> Roster { get; } = new();

    /// <summary>当前会话的存档槽位：新开一局时创建，读取存档时恢复。快速存档/自动存档写入该槽位。</summary>
    public string CurrentSaveId { get; set; } = "";

    public event Action<string>? ScreenChange;
    public event Action<ChatMessage>? MessageEmitted;

    public void Boot()
    {
        State = new GameState();
    }

    /// <summary>读取存档后恢复游戏状态。</summary>
    public void RestoreState(GameState state)
    {
        if (state is null) return;
        State = state;
        if (!string.IsNullOrEmpty(state.CharacterId) && Roster.TryGetValue(state.CharacterId, out var ch))
            ActiveCharacter = ch;
    }

    public void SetCharacter(string id)
    {
        if (!Roster.TryGetValue(id, out var ch)) return;
        ActiveCharacter = ch;
        State.CharacterId = id;
        ScreenChange?.Invoke("character");
    }

    public void MoveTo(string location)
    {
        State.Location = location;
        State.Background = $"{location}_{State.GameTime:HHmm}_xxx";
        ScreenChange?.Invoke("location");
    }

    public void Emit(ChatMessage msg) => MessageEmitted?.Invoke(msg);

    public void ToggleAuto() => State.AutoPlay = !State.AutoPlay;

    public void Register(CharacterData ch) => Roster[ch.Profile.Id] = ch;
}

public sealed class SettingsManager
{
    private readonly StorageProvider _store;
    private UserSettings _current = new();

    public UserSettings Current => _current;
    public event Action? Applied;

    public SettingsManager(StorageProvider store) => _store = store;

    public void Apply(UserSettings s)
    {
        _current = s;
        Applied?.Invoke();
    }

    public async Task Persist() => await _store.Save("settings", _current);
    public async Task Restore()
    {
        var s = await _store.Load<UserSettings>("settings");
        if (s is not null) _current = s;
        await MigrateLegacyAsync();
    }

    /// <summary>旧版单一 GlassStyle → 新的三个独立开关；毛玻璃隐含磨砂。</summary>
    private async Task MigrateLegacyAsync()
    {
        var c = _current;
        if (string.IsNullOrEmpty(c.GlassStyle) || c.FrostEnabled || c.GlassEnabled || c.LiquidEnabled)
            return;
        var migrated = c with
        {
            FrostEnabled = c.GlassStyle is "frost" or "glass",
            GlassEnabled = c.GlassStyle == "glass",
            LiquidEnabled = c.GlassStyle == "liquid",
            GlassStyle = ""
        };
        _current = migrated;
        await _store.Save("settings", _current);
    }
}

public sealed class StorageProvider
{
    private readonly string _root;

    public StorageProvider()
    {
        _root = Path.Combine(FileSystem.AppDataDirectory, "WarmAsBefore");
        Directory.CreateDirectory(_root);
    }

    public string Root => _root;

    public async Task<T?> Load<T>(string key) where T : class
    {
        var path = KeyPath(key);
        if (!File.Exists(path)) return null;
        var json = await File.ReadAllTextAsync(path);
        return JsonSerializer.Deserialize<T>(json);
    }

    /// <summary>读取原始 JSON 文本（供需要分段/多次反序列化的调用方使用，避免 Dictionary&lt;object&gt; 双重序列化）。</summary>
    public async Task<string?> LoadRawAsync(string key)
    {
        var path = KeyPath(key);
        if (!File.Exists(path)) return null;
        return await File.ReadAllTextAsync(path);
    }

    public async Task Save<T>(string key, T data) where T : class
    {
        var path = KeyPath(key);
        var dir = Path.GetDirectoryName(path);
        if (dir is not null) Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(data);
        await File.WriteAllTextAsync(path, json);
    }

    public void Delete(string key)
    {
        var p = KeyPath(key);
        if (File.Exists(p)) File.Delete(p);
    }

    public bool Exists(string key) => File.Exists(KeyPath(key));

    private string KeyPath(string key) =>
        Path.Combine(_root, key.Replace('/', Path.DirectorySeparatorChar) + ".json");
}

public sealed class NotificationService
{
    public event Action<string, string>? Notify;
    public void Show(string title, string msg) => Notify?.Invoke(title, msg);
}

public sealed class AudioController
{
    private double _bgm = 0.7;
    private double _sfx = 0.8;

    public double Bgm { get => _bgm; set => _bgm = Math.Clamp(value, 0, 1); }
    public double Sfx { get => _sfx; set => _sfx = Math.Clamp(value, 0, 1); }

    public void PlayBgm(string name) => Trace($"[BGM] {name}");
    public void PlaySfx(string name) => Trace($"[SFX] {name}");
    public void StopAll() => Trace("[AUDIO] stop");

    private static void Trace(string msg) =>
        System.Diagnostics.Debug.WriteLine(msg);
}