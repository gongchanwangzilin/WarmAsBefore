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
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _createdDirs = new(StringComparer.OrdinalIgnoreCase);

    public StorageProvider()
    {
        _root = Path.Combine(FileSystem.AppDataDirectory, "WarmAsBefore");
        Directory.CreateDirectory(_root);
        _createdDirs[_root] = 0;
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
        if (dir is not null && _createdDirs.AddOrUpdate(dir, 0, (_, _) => 0) is null)
            Directory.CreateDirectory(dir);
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
    private readonly MaterialLibrary _materials;
    private double _bgm = 0.7;
    private double _sfx = 0.8;
    private bool _bgmMuted;

    public AudioController(MaterialLibrary materials) => _materials = materials;

    public double Bgm
    {
        get => _bgm;
        set { _bgm = Math.Clamp(value, 0, 1); ApplyBgmVolume(); }
    }

    public double Sfx
    {
        get => _sfx;
        set => _sfx = Math.Clamp(value, 0, 1);
    }

    /// <summary>按分配方案播放：left=左键音，right=右键音，key=按键音。</summary>
    public void PlayAssigned(string kind)
    {
        _ = PlayAssignedAsync(kind);
    }

    private async Task PlayAssignedAsync(string kind)
    {
        try
        {
            await _materials.EnsureLoadedAsync();
            var a = _materials.Assign;
            var id = kind switch
            {
                "left" => a.LeftClickId,
                "right" => a.RightClickId,
                _ => a.KeyPressId
            };
            if (string.IsNullOrEmpty(id)) return;
            var item = await _materials.FindMusicAsync(id);
            if (item is null) return;
            PlaySfxFile(_materials.ResolveAbs(item.RelPath));
        }
        catch (Exception ex)
        {
            App.WriteLog("AudioController.PlayAssigned -> " + ex.Message);
        }
    }

    public void PlaySfxFile(string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return;
#if WINDOWS
        try
        {
            var p = new Windows.Media.Playback.MediaPlayer { Volume = _sfx };
            p.MediaEnded += (_, _) => { try { p.Dispose(); } catch { } };
            p.MediaFailed += (_, _) => { try { p.Dispose(); } catch { } };
            p.Source = Windows.Media.Core.MediaSource.CreateFromUri(new Uri(filePath));
            p.Play();
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[SFX] {filePath}: {ex.Message}"); }
#elif ANDROID
        try
        {
            lock (_androidAudioLock)
            {
                try { _androidSfxPlayer?.Stop(); _androidSfxPlayer?.Release(); } catch { }
                var p = new Android.Media.MediaPlayer();
                var uri = Android.Net.Uri.FromFile(new Java.IO.File(filePath));
                p.SetDataSource(Platform.AppContext, uri);
                p.Prepare();
                p.Volume = (float)_sfx;
                p.Start();
                p.SetOnCompletionListener(new AndroidSfxCompletion(p));
                _androidSfxPlayer = p;
            }
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[SFX] {filePath}: {ex.Message}"); }
#else
        System.Diagnostics.Debug.WriteLine($"[SFX] {filePath}");
#endif
    }

#if ANDROID
    /// <summary>Android 音效播完自动释放 MediaPlayer。</summary>
    private sealed class AndroidSfxCompletion : Java.Lang.Object,
        Android.Media.MediaPlayer.IOnCompletionListener
    {
        private readonly Android.Media.MediaPlayer _mp;
        public AndroidSfxCompletion(Android.Media.MediaPlayer mp) => _mp = mp;
        public void OnCompletion(Android.Media.MediaPlayer m)
        {
            try { m.Release(); } catch { }
        }
    }

    /// <summary>Android 背景音乐：播完当前曲自动推进到列表下一首（与 Windows 端 AdvanceInner 对齐）。</summary>
    private sealed class AndroidBgmCompletion : Java.Lang.Object,
        Android.Media.MediaPlayer.IOnCompletionListener
    {
        private readonly AudioController _owner;
        public AndroidBgmCompletion(AudioController owner) => _owner = owner;
        public void OnCompletion(Android.Media.MediaPlayer m)
        {
            try { _owner.BgmNext(); } catch { }
        }
    }
#endif

    // ==================== 背景音乐轮播 ====================

    /// <summary>启动/刷新背景音乐轮播（内容来自声音分配的 BgmList，未分配则不播放）。</summary>
    public async Task StartBgmRotationAsync()
    {
        try
        {
            await _materials.EnsureLoadedAsync();
            var a = _materials.Assign;
            var files = new List<string>();
            foreach (var id in a.BgmList)
            {
                var item = await _materials.FindMusicAsync(id);
                if (item is null) continue;
                var abs = _materials.ResolveAbs(item.RelPath);
                if (File.Exists(abs)) files.Add(abs);
            }
            SetBgmPlaylist(files);
        }
        catch (Exception ex)
        {
            App.WriteLog("AudioController.StartBgmRotation -> " + ex.Message);
        }
    }

    private readonly List<string> _bgmFiles = new();
    private int _bgmIndex;

#if WINDOWS
    private Windows.Media.Playback.MediaPlayer? _bgmPlayer;
#else
    private object? _bgmPlayer;
#endif

#if ANDROID
    private Android.Media.MediaPlayer? _androidBgmPlayer;
    private Android.Media.MediaPlayer? _androidSfxPlayer;
    private readonly object _androidAudioLock = new();
#endif

    public void SetBgmPlaylist(IReadOnlyList<string> files)
    {
        lock (_bgmFiles)
        {
            _bgmFiles.Clear();
            if (files is not null) _bgmFiles.AddRange(files);
            _bgmIndex = 0;
        }
        if (_bgmFiles.Count == 0)
        {
            StopBgm();
            return;
        }
        PlayBgmFile(_bgmFiles[0]);
    }

    public void BgmNext()
    {
        lock (_bgmFiles)
        {
            if (_bgmFiles.Count == 0) return;
            _bgmIndex = (_bgmIndex + 1) % _bgmFiles.Count;
            PlayBgmFile(_bgmFiles[_bgmIndex]);
        }
    }

    public void BgmPrev()
    {
        lock (_bgmFiles)
        {
            if (_bgmFiles.Count == 0) return;
            _bgmIndex = (_bgmIndex - 1 + _bgmFiles.Count) % _bgmFiles.Count;
            PlayBgmFile(_bgmFiles[_bgmIndex]);
        }
    }

    /// <summary>CG 等全屏页：静音当前背景音乐；退出后调用恢复。</summary>
    public void SetBgmMuted(bool muted)
    {
        _bgmMuted = muted;
#if WINDOWS
        if (_bgmPlayer is not null) _bgmPlayer.IsMuted = muted;
#elif ANDROID
        try { lock (_androidAudioLock) { if (_androidBgmPlayer is not null) _androidBgmPlayer.Volume = muted ? 0f : (float)_bgm; } } catch { }
#endif
    }

    public void StopBgm()
    {
#if WINDOWS
        try
        {
            _bgmPlayer?.Pause();
            _bgmPlayer?.Dispose();
            _bgmPlayer = null;
        }
        catch { }
#elif ANDROID
        try
        {
            lock (_androidAudioLock)
            {
                _androidBgmPlayer?.Stop();
                _androidBgmPlayer?.Release();
                _androidBgmPlayer = null;
            }
        }
        catch { }
#endif
    }

    public void StopAll()
    {
        StopBgm();
#if WINDOWS
        System.Diagnostics.Debug.WriteLine("[AUDIO] stop");
#elif ANDROID
        try
        {
            lock (_androidAudioLock)
            {
                _androidSfxPlayer?.Stop();
                _androidSfxPlayer?.Release();
                _androidSfxPlayer = null;
            }
        }
        catch { }
#endif
    }

    private void PlayBgmFile(string file)
    {
        #if WINDOWS
        try
        {
            StopBgm();
            var p = new Windows.Media.Playback.MediaPlayer { Volume = _bgm, IsMuted = _bgmMuted };
            p.MediaEnded += (_, _) => AdvanceInner(p);
            p.MediaFailed += (_, _) => AdvanceInner(p);
            p.Source = Windows.Media.Core.MediaSource.CreateFromUri(new Uri(file));
            p.Play();
            _bgmPlayer = p;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[BGM] {file}: {ex.Message}");
        }
        #elif ANDROID
        try
        {
            lock (_androidAudioLock)
            {
                _androidBgmPlayer?.Stop();
                _androidBgmPlayer?.Release();
                var p = new Android.Media.MediaPlayer();
                var uri = Android.Net.Uri.FromFile(new Java.IO.File(file));
                p.SetDataSource(Platform.AppContext, uri);
                p.Prepare();
                p.Volume = _bgmMuted ? 0f : (float)_bgm;
                // 播完当前曲自动推进到列表下一首（与 Windows 端 AdvanceInner 行为对齐）
                p.SetOnCompletionListener(new AndroidBgmCompletion(this));
                p.Start();
                _androidBgmPlayer = p;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[BGM] {file}: {ex.Message}");
        }
        #else
        System.Diagnostics.Debug.WriteLine($"[BGM] {file}");
        #endif
    }

#if WINDOWS
    /// <summary>当前曲目自然结束/失败：同一播放器直接切换到下一首（避免在事件里 Dispose 自己）。</summary>
    private void AdvanceInner(Windows.Media.Playback.MediaPlayer p)
    {
        try
        {
            lock (_bgmFiles)
            {
                if (_bgmFiles.Count == 0) { p.Source = null; return; }
                _bgmIndex = (_bgmIndex + 1) % _bgmFiles.Count;
                p.Source = Windows.Media.Core.MediaSource.CreateFromUri(new Uri(_bgmFiles[_bgmIndex]));
                p.Play();
            }
        }
        catch { }
    }
#endif

    private void ApplyBgmVolume()
    {
        #if WINDOWS
        try { if (_bgmPlayer is not null) _bgmPlayer.Volume = _bgm; } catch { }
        #endif
    }
}