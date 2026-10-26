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

    /// <summary>地图是否可用：只有绑定了世界树的存档才有「发生在哪儿」，其余按普通酒馆走。</summary>
    public bool MapUnlocked => !string.IsNullOrWhiteSpace(State.WorldId);

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

    /// <summary>上一次读 settings.json 失败：这时内存里是默认值，写盘等于把用户的设置整份盖掉。</summary>
    private bool _loadFaulted;

    public UserSettings Current => _current;
    public event Action? Applied;

    public SettingsManager(StorageProvider store) => _store = store;

    public void Apply(UserSettings s)
    {
        _current = s;
        Applied?.Invoke();
    }

    public async Task Persist()
    {
        // 上一次没读出来 → 内存里是默认值，这时候写盘等于把用户的设置整份盖掉。先试着重读，读不通就不写。
        if (!await RetryRestoreAsync())
        {
            App.WriteLog("SettingsManager.Persist -> 已跳过落盘：settings.json 这次读不出来，不拿默认值盖用户的设置");
            return;
        }
        await _store.Save("settings", _current);
    }

    public async Task Restore()
    {
        try
        {
            var s = await _store.Load<UserSettings>("settings");
            if (s is not null) _current = s;
            _loadFaulted = false;
        }
        catch (Exception ex)
        {
            _loadFaulted = true;
            App.WriteLog("SettingsManager.Restore -> " + ex.Message);
        }
        await MigrateLegacyAsync();
    }

    /// <summary>读盘失败过就惰性重试：读回来了才允许写盘（重试成功时以磁盘那份为准）。</summary>
    private async Task<bool> RetryRestoreAsync()
    {
        if (!_loadFaulted) return true;
        try
        {
            var retry = await _store.Load<UserSettings>("settings");
            if (retry is not null) _current = retry;
            _loadFaulted = false;
            App.WriteLog("SettingsManager: settings.json 这次读出来了，恢复落盘");
            return true;
        }
        catch (Exception ex)
        {
            App.WriteLog("SettingsManager.RetryRestore -> " + ex.Message);
            return false;
        }
    }

    /// <summary>
    /// 同步读取设置：必须在构建窗口/界面之前调用。
    /// 原因是标题页要靠 <see cref="UserSettings.DisclaimerAcknowledged"/> 决定弹不弹免责声明，
    /// 异步读会晚于首帧，导致「已确认过也每次启动都弹」。文件很小，阻塞时间可忽略。
    /// </summary>
    public void RestoreBlocking()
    {
        try { Task.Run(async () => await Restore()).GetAwaiter().GetResult(); }
        catch (Exception ex) { App.WriteLog("SettingsManager.RestoreBlocking -> " + ex.Message); }
    }

    /// <summary>旧版单一 GlassStyle → 新的三个独立开关；毛玻璃隐含磨砂。</summary>
    private async Task MigrateLegacyAsync()
    {
        // 上一次没读成功时 _current 是默认值：这时候迁移完回写，等于把用户全部设置换成出厂值。
        if (_loadFaulted) return;
        var c = _current;
        var changed = false;

        if (!(string.IsNullOrEmpty(c.GlassStyle) || c.FrostEnabled || c.GlassEnabled || c.LiquidEnabled))
        {
            c = c with
            {
                FrostEnabled = c.GlassStyle is "frost" or "glass",
                GlassEnabled = c.GlassStyle == "glass",
                LiquidEnabled = c.GlassStyle == "liquid",
                GlassStyle = ""
            };
            changed = true;
        }

        // 旧的「一条档案 = 厂商+模型+密钥」迁移成「厂商 + 多模型」：
        // 同端点同密钥的档案合并成一家厂商，各自的模型并进这家厂商的模型列表。
        if (c.AiProviders.Count == 0 && c.AiProfiles.Count > 0)
        {
            var providers = new List<AiProvider>();
            foreach (var p in c.AiProfiles)
            {
                var normUrl = (p.Url ?? "").Trim();
                var keys = (p.Keys ?? new List<string>()).Where(k => !string.IsNullOrWhiteSpace(k)).ToList();
                var match = providers.FirstOrDefault(v =>
                    string.Equals(v.Url, normUrl, StringComparison.OrdinalIgnoreCase)
                    && v.Keys.SequenceEqual(keys, StringComparer.Ordinal));

                if (match is not null)
                {
                    var idx = providers.IndexOf(match);
                    providers[idx] = match with
                    {
                        Models = match.Models.Concat(new[] { new AiProviderModel { Model = (p.Model ?? "").Trim(), Label = p.Name ?? "" } })
                            .Where(m => !string.IsNullOrWhiteSpace(m.Model))
                            .GroupBy(m => m.Model, StringComparer.OrdinalIgnoreCase)
                            .Select(g => g.First())
                            .ToList()
                    };
                }
                else
                {
                    providers.Add(new AiProvider
                    {
                        Name = string.IsNullOrWhiteSpace(p.Name) ? (p.Model ?? "") : p.Name!,
                        Url = normUrl,
                        Keys = keys,
                        Enabled = p.Enabled,
                        TimeoutSeconds = p.TimeoutSeconds,
                        Models = string.IsNullOrWhiteSpace(p.Model)
                            ? new List<AiProviderModel>()
                            : new List<AiProviderModel> { new() { Model = p.Model.Trim() } }
                    });
                }
            }
            if (providers.Count > 0)
            {
                c = c with { AiProviders = providers };
                changed = true;
                App.WriteLog($"Settings.Migrate: {c.AiProfiles.Count} 条端点档案 → {providers.Count} 家厂商");
            }
        }

        // 旧的单一周期设置 → 按性别分开。判据是「字典为空」而不是「值为默认」：
        // 老存档里根本没有 CycleRules 这个键，反序列化后就是空字典，新存档则由设置页写满。
        if (c.CycleRules.Count == 0)
        {
#pragma warning disable CS0618   // 这两个属性就是为了这次搬家才留在模型里的
            var fem = new CycleRule { Enabled = true, CycleLength = c.CycleLength, PeriodLength = c.PeriodLength };
#pragma warning restore CS0618
            c = c with
            {
                CycleRules = new Dictionary<string, CycleRule>(StringComparer.Ordinal)
                {
                    ["女"] = fem,
                    ["男"] = new CycleRule { Enabled = false },
                    ["其他"] = new CycleRule { Enabled = false },
                }
            };
            changed = true;
            App.WriteLog($"Settings.Migrate: 单一周期 → 按性别（女 {fem.CycleLength}/{fem.PeriodLength}，男/其他无周期）");
        }

        if (!changed) return;
        _current = c;
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

    /// <summary>任一 JSON 落盘或删除后触发（参数是存储键）。云存档靠它收集待上传的键。</summary>
    public event Action<string>? Changed;

    public async Task<T?> Load<T>(string key) where T : class
    {
        var path = KeyPath(key);
        if (!File.Exists(path)) return null;
        var json = await ReadWithRetryAsync(path);
        try
        {
            return JsonSerializer.Deserialize<T>(json);
        }
        catch (JsonException)
        {
            BackupCorrupt(path);
            throw;
        }
    }

    /// <summary>读取原始 JSON 文本（供需要分段/多次反序列化的调用方使用，避免 Dictionary&lt;object&gt; 双重序列化）。</summary>
    public async Task<string?> LoadRawAsync(string key)
    {
        var path = KeyPath(key);
        if (!File.Exists(path)) return null;
        return await ReadWithRetryAsync(path);
    }

    /// <summary>
    /// 云同步与杀毒常常只占用文件几十毫秒，直接失败会让调用方把「读不到」当成「没有数据」。
    /// 所以先重试一次，真的读不出来再抛。
    /// </summary>
    private static async Task<string> ReadWithRetryAsync(string path)
    {
        try
        {
            return await File.ReadAllTextAsync(path);
        }
        catch (IOException)
        {
            await Task.Delay(80);
            return await File.ReadAllTextAsync(path);
        }
    }

    /// <summary>
    /// 内容解析不了就留一份带时间戳的原件再抛。调用方一律按「这份数据没有」继续跑，
    /// 下一次保存会整表覆盖——没有这份备份，用户的东西就真的没了。
    /// </summary>
    private static void BackupCorrupt(string path)
    {
        try
        {
            // 后缀留在最后（characters.corrupt-时间戳.json）：这份备份是要给用户手工救回来的，
            // 名字里还认得出是 JSON，编辑器与资源管理器才不会把它当成无关文件。
            var bak = Path.Combine(Path.GetDirectoryName(path) ?? "",
                Path.GetFileNameWithoutExtension(path) + ".corrupt-"
                + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + Path.GetExtension(path));
            if (!File.Exists(bak)) File.Copy(path, bak);
            App.WriteLog("StorageProvider: 文件读坏了，已备份原件 → " + bak);
        }
        catch (Exception ex)
        {
            App.WriteLog("StorageProvider.BackupCorrupt -> " + ex.Message);
        }
    }

    /// <summary>
    /// 这个键能不能安全地落在数据目录里。
    /// 远端云清单的键、数据包与角色 id 都是外部输入，它们不该有能力把文件写到数据目录外面。
    /// </summary>
    public static bool IsSafeKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return false;
        // 冒号是盘符/备用数据流的写法：Windows 文件名里本来就不能有，留着只会被路径语义捡走。
        if (key.Contains(':')) return false;
        if (Path.IsPathRooted(key)) return false;
        foreach (var seg in key.Split('/', '\\'))
            if (seg == ".." || seg == ".") return false;
        return true;
    }

    public async Task Save<T>(string key, T data) where T : class
    {
        var json = JsonSerializer.Serialize(data, WriteOptions);
        await WriteAtomicAsync(key, json);
        Changed?.Invoke(key);
    }

    /// <summary>原样写回一段 JSON（云存档把远端文件落到本地，避免反序列化再序列化改变内容）。</summary>
    public async Task WriteRawAsync(string key, string json)
    {
        await WriteAtomicAsync(key, json);
        Changed?.Invoke(key);
    }

    /// <summary>
    /// 同目录临时文件 + 原子替换：另一个线程（云存档合并）同时在读时，
    /// 最多读到旧文件或新文件，绝不会读到写了一半的 JSON。
    /// </summary>
    private async Task WriteAtomicAsync(string key, string json)
    {
        var path = KeyPath(key);
        var dir = Path.GetDirectoryName(path);
        // TryAdd 只在首次插入时返回 true：同一目录只建一次，省掉每次 Save 的 CreateDirectory 系统调用
        if (dir is not null && _createdDirs.TryAdd(dir, 0))
            Directory.CreateDirectory(dir);
        var tmp = path + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
        try
        {
            await File.WriteAllTextAsync(tmp, json);
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    File.Move(tmp, path, overwrite: true);
                    return;
                }
                catch (IOException) when (attempt < 5)
                {
                    await Task.Delay(30 * (attempt + 1));   // 杀毒/索引器短暂占用文件
                }
            }
        }
        finally
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
        }
    }

    /// <summary>存储键对应的绝对路径（同步工具与调试用）；越界的键返回空串，让调用处的 File.Exists 自然为 false。</summary>
    public string PathOf(string key) => IsSafeKey(key) ? KeyPath(key) : "";

    /// <summary>中文原样写出去（默认会把「妈妈」转成 \u5988\u5988，人就没法直接读这些文件了）。</summary>
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public void Delete(string key)
    {
        var p = KeyPath(key);
        if (File.Exists(p)) File.Delete(p);
        Changed?.Invoke(key);
    }

    public bool Exists(string key) => IsSafeKey(key) && File.Exists(KeyPath(key));

    private string KeyPath(string key)
    {
        // 硬边界：键必须是数据目录内的相对路径。少这一道，一个 "../settings" 就能把文件写到目录外面。
        if (!IsSafeKey(key)) throw new ArgumentException("存储键不合法（要相对路径，且不含 . / ..）：" + key);
        return Path.Combine(_root, key.Replace('/', Path.DirectorySeparatorChar) + ".json");
    }
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

    /// <summary>按分配方案播放：left=左键音，right=右键音，key=按键音；lightning/rain/shake/bounce 为事件小音效卡槽。</summary>
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
                "key" => a.KeyPressId,
                "lightning" => a.LightningId,
                "rain" => a.RainId,
                "shake" => a.ShakeId,
                "bounce" => a.BounceId,
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

    /// <summary>
    /// 事件音效卡槽是否有装填（用于打雷/雨事件的「音效库优先、配置路径回落」判断）。
    /// </summary>
    public async Task<bool> HasAssignedAsync(string kind)
    {
        try
        {
            await _materials.EnsureLoadedAsync();
            var a = _materials.Assign;
            var id = kind switch
            {
                "left" => a.LeftClickId,
                "right" => a.RightClickId,
                "key" => a.KeyPressId,
                "lightning" => a.LightningId,
                "rain" => a.RainId,
                "shake" => a.ShakeId,
                "bounce" => a.BounceId,
                _ => ""
            };
            return !string.IsNullOrEmpty(id);
        }
        catch { return false; }
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
                p.SetVolume((float)_sfx, (float)_sfx);
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
        try { lock (_androidAudioLock) { if (_androidBgmPlayer is not null) { var v = muted ? 0f : (float)_bgm; _androidBgmPlayer.SetVolume(v, v); } } } catch { }
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
                var bgmVol = _bgmMuted ? 0f : (float)_bgm;
                p.SetVolume(bgmVol, bgmVol);
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