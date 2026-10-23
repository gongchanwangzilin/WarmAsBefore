using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using WarmAsBefore.Models;

namespace WarmAsBefore.Services.CloudSync;

/// <summary>
/// 云存档：把本地 JSON 数据镜像到用户自己的 GitHub 仓库（默认方案）。
///
/// 布局（每台设备只写自己的目录，永不覆盖别人的文件）：
///   wab/devices/{deviceId}/manifest.json     本设备每个键的最后写入时间 + 是否已删除
///   wab/devices/{deviceId}/files/{key}.json  数据本体
///
/// 合并规则：同一个键，取 manifest 里时间较新的那一份；存档再按 meta.SavedAt 复核一次，
/// 剧情进度更旧的远端存档不会覆盖本地。合并前把本地旧文件抄进 cloud/conflicts 留底，
/// 所以两端同时使用最坏也只是「有一份被放进备份」，不会被写坏。
///
/// settings.json 永远不参与镜像：访问令牌、设备身份只留在本机。
/// </summary>
public sealed class CloudSyncService : IDisposable
{
    private const string ApiRoot = "https://api.github.com";
    private const string DevRoot = "wab/devices";
    private const string StateKey = "cloud/state";
    private const string ConflictRoot = "cloud/conflicts";
    private const string SettingsKey = "settings";
    private const int MaxFileBytes = 16 * 1024 * 1024;
    private const int MaxUploadsPerRun = 80;
    private const int MaxConflictFiles = 40;

    private readonly StorageProvider _store;
    private readonly SettingsManager _settings;
    private readonly MemoryScope _scope;
    private readonly MemoryReconcileService _reconcile;
    private readonly CharacterLibrary _library;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _stateLock = new();

    private CloudState _state = new();
    private System.Threading.Timer? _debounce;
    private System.Threading.Timer? _periodicPull;
    private Task? _startTask;
    private string _branch = "";
    private string? _applyingKey;
    private bool _started;
    private bool _disposed;

    public CloudSyncService(StorageProvider store, SettingsManager settings, MemoryScope scope,
        MemoryReconcileService reconcile, CharacterLibrary library)
    {
        _store = store;
        _settings = settings;
        _scope = scope;
        _reconcile = reconcile;
        _library = library;
    }

    /// <summary>状态行变化（后台线程触发，订阅方自己切回 UI 线程）。</summary>
    public event Action<string>? StatusChanged;

    /// <summary>远端数据已合并进本地（页面可据此刷新）。</summary>
    public event Action? RemoteApplied;

    public string Status { get; private set; } = "云存档未启用";
    public bool IsBusy { get; private set; }
    public bool IsEnabled => _settings.Current.CloudSaveEnabled;

    private string Repo => NormalizeRepo(_settings.Current.CloudRepo);
    private string Token => (_settings.Current.CloudToken ?? "").Trim();
    private bool IsConfigured => Repo.Length > 0 && Token.Length > 0;

    // ==================== 启动与自动上传 ====================

    public void Start()
    {
        if (_started || _disposed) return;
        _started = true;
        _startTask = StartAsync();
    }

    /// <summary>设置页可能比启动流程更早触发同步：先等本地状态读完，避免设备目录被覆盖。</summary>
    private async Task EnsureReadyAsync()
    {
        if (_startTask is { } task)
        {
            try { await task; } catch { }
        }
    }

    private async Task StartAsync()
    {
        try
        {
            _state = await _store.Load<CloudState>(StateKey) ?? new CloudState();
            SeedTouchedFromDisk();
            _store.Changed += OnLocalChanged;
            // 每 5 分钟拉一次：不必实时，但另一台设备写下的内容不该等到下次重启才出现
            _periodicPull = new System.Threading.Timer(_ => _ = PullOnlyAsync(), null,
                TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
            if (!IsEnabled)
            {
                SetStatus("云存档未启用");
                return;
            }
            if (!IsConfigured)
            {
                SetStatus("请先在设置里填写仓库与访问令牌");
                return;
            }
            SetStatus("正在连接云端…");
            _ = PullOnlyAsync();
        }
        catch (Exception ex)
        {
            App.WriteLog("CloudSync.Start -> " + ex);
            SetStatus("云存档启动失败：" + ex.Message);
        }
    }

    /// <summary>本地改动（任何走 StorageProvider 的落盘）都会走到这里，停手两秒后合并上传。</summary>
    private void OnLocalChanged(string key)
    {
        if (_disposed || !IsSyncable(key)) return;
        if (key == Volatile.Read(ref _applyingKey)) return;   // 远端内容刚落到本地，不算「本地新改动」
        var now = DateTime.UtcNow;
        lock (_stateLock)
        {
            if (_store.Exists(key))
            {
                _state.Touched[key] = now;
                _state.Deleted.Remove(key);
            }
            else
            {
                _state.Deleted[key] = now;
                _state.Touched.Remove(key);
            }
        }
        if (_settings.Current.CloudAutoUpload && IsEnabled && IsConfigured) ScheduleUpload(1800);
    }

    private void ScheduleUpload(int delayMs)
    {
        if (_disposed) return;
        lock (_stateLock)
        {
            _debounce ??= new System.Threading.Timer(_ => _ = PushPendingAsync(), null,
                Timeout.Infinite, Timeout.Infinite);
        }
        try { _debounce?.Change(delayMs, Timeout.Infinite); } catch { }
    }

    private async Task PushPendingAsync()
    {
        if (!IsEnabled || !IsConfigured || !_settings.Current.CloudAutoUpload) return;
        await EnsureReadyAsync();
        if (!await _gate.WaitAsync(0)) { ScheduleUpload(4000); return; }
        try
        {
            SetBusy(true);
            var branch = await ResolveBranchAsync();
            if (branch is null) { SetStatus("云端连接失败：仓库或令牌不可用"); return; }
            var (ok, failed, error) = await PushAsync(branch, forceAll: false);
            lock (_stateLock) _state.LastPushUtc = DateTime.UtcNow;
            await SaveStateAsync();
            if (failed > 0) SetStatus($"已上传 {ok} 个文件，{failed} 个失败：{error}");
            else if (ok > 0) SetStatus($"已自动上传 {ok} 个文件 · {DateTime.Now:HH:mm:ss}");
        }
        catch (Exception ex)
        {
            App.WriteLog("CloudSync.Push -> " + ex.Message);
            SetStatus("自动上传失败：" + ex.Message);
        }
        finally
        {
            SetBusy(false);
            _gate.Release();
        }
    }

    /// <summary>退出前尽量把队列里剩下的改动推上去（窗口可能在请求完成前关闭）。</summary>
    public void FlushOnExit()
    {
        if (!IsEnabled || !IsConfigured) return;
        _ = PushPendingAsync();
    }

    // ==================== 手动同步 ====================

    /// <summary>测试仓库与令牌是否可用（只读一次目录树，不写任何东西）。</summary>
    public async Task<(bool ok, string message)> TestAsync()
    {
        if (Repo.Length == 0) return (false, "请先填写仓库（owner/repo）");
        if (Token.Length == 0) return (false, "请先填写访问令牌");
        var branch = await ResolveBranchAsync(force: true);
        if (branch is null) return (false, LastError ?? "连不上仓库：检查仓库名、令牌权限（Contents 读写）与分支");
        var tree = await GetTreeAsync(branch);
        if (!tree.ok) return (false, "连不上仓库：" + (tree.error ?? "未知错误"));
        var count = tree.nodes.Count(n => n.Path.StartsWith(DevRoot + "/", StringComparison.Ordinal));
        SetStatus($"已连通 {Repo}（分支 {branch}）");
        return (true, $"已连通 {Repo}（分支 {branch}）· 云端已有 {count} 个文件");
    }

    /// <summary>立即同步：先合并远端较新的文件，再把本地改动全部推上去。</summary>
    public async Task<string> SyncNowAsync()
    {
        if (!IsEnabled) return SetStatus("云存档未启用");
        if (!IsConfigured) return SetStatus("请先填写仓库与访问令牌");
        await EnsureReadyAsync();
        if (!await _gate.WaitAsync(0)) return SetStatus("正在同步，请稍候");
        try
        {
            SetBusy(true);
            SetStatus("正在同步…");
            var branch = await ResolveBranchAsync();
            if (branch is null) return SetStatus("云端连接失败：" + (LastError ?? "仓库或令牌不可用"));

            var pull = await PullAsync(branch);
            var (pushed, failed, error) = await PushAsync(branch, forceAll: true);
            lock (_stateLock)
            {
                _state.LastPullUtc = DateTime.UtcNow;
                _state.LastPushUtc = DateTime.UtcNow;
            }
            await SaveStateAsync();

            if (pull.applied > 0)
            {
                await RefreshAfterPullAsync(pull);
                RemoteApplied?.Invoke();
            }

            var msg = $"同步完成 · 合并 {pull.applied} 个文件 · 上传 {pushed} 个";
            if (failed > 0) msg += $" · {failed} 个失败（{error}）";
            SetStatus(msg);
            App.WriteLog("CloudSync.SyncNow -> " + msg);

            // 模型后处理：只整理这次真的跨设备合并过的记忆文件
            if (pull.appliedKeys.Count > 0 && _settings.Current.CloudReconcileMemory)
            {
                var note = await _reconcile.ReconcileAsync(pull.appliedKeys);
                if (!string.IsNullOrWhiteSpace(note)) SetStatus(msg + " · " + note);
            }
            return Status;
        }
        catch (Exception ex)
        {
            App.WriteLog("CloudSync.SyncNow -> " + ex);
            return SetStatus("同步失败：" + ex.Message);
        }
        finally
        {
            SetBusy(false);
            _gate.Release();
        }
    }

    private async Task PullOnlyAsync()
    {
        if (!IsEnabled || !IsConfigured) return;
        if (!await _gate.WaitAsync(0)) return;
        try
        {
            SetBusy(true);
            var branch = await ResolveBranchAsync();
            if (branch is null) return;
            var pull = await PullAsync(branch);
            if (pull.applied > 0)
            {
                await RefreshAfterPullAsync(pull);
                RemoteApplied?.Invoke();
                lock (_stateLock) _state.LastPullUtc = DateTime.UtcNow;
                await SaveStateAsync();
                SetStatus($"已从云端合并 {pull.applied} 个文件 · {DateTime.Now:HH:mm}");
                if (_settings.Current.CloudReconcileMemory)
                {
                    var note = await _reconcile.ReconcileAsync(pull.appliedKeys);
                    if (!string.IsNullOrWhiteSpace(note)) SetStatus($"已从云端合并 {pull.applied} 个文件 · {note}");
                }
            }
        }
        catch (Exception ex)
        {
            App.WriteLog("CloudSync.Pull -> " + ex.Message);
        }
        finally
        {
            SetBusy(false);
            _gate.Release();
        }
    }

    // ==================== 拉取与合并 ====================

    private sealed class PullResult
    {
        public int applied;
        public List<string> appliedKeys = new();
    }

    private async Task<PullResult> PullAsync(string branch)
    {
        var result = new PullResult();
        var tree = await GetTreeAsync(branch);
        if (!tree.ok)
        {
            App.WriteLog("CloudSync.Pull: 目录树读取失败 " + tree.error);
            return result;
        }

        var deviceId = EnsureDeviceId();
        var devices = tree.nodes
            .Where(n => n.Path.StartsWith(DevRoot + "/", StringComparison.Ordinal))
            .GroupBy(n => n.Path.Split('/').Length > 2 ? n.Path.Split('/')[2] : "")
            .Where(g => g.Key.Length > 0)
            .ToList();

        foreach (var group in devices)
        {
            if (group.Key == deviceId) continue;   // 本设备以本地为准
            var manNode = group.FirstOrDefault(n => n.Path.EndsWith("/manifest.json", StringComparison.Ordinal));
            if (manNode is null) continue;

            string? known;
            lock (_stateLock) _state.RemoteSha.TryGetValue(manNode.Path, out known);
            if (known == manNode.Sha) continue;    // 这份清单自上次拉取后没变过

            var man = await ReadJsonAsync<CloudManifest>(manNode.Sha);
            if (man is null) continue;

            foreach (var (key, entry) in man.Files)
            {
                if (!IsSyncable(key)) continue;
                var localAt = TouchedOf(key);
                if (entry.SavedAt <= localAt) continue;          // 本地更新或相同，保留本地

                if (entry.Deleted)
                {
                    if (_store.Exists(key))
                    {
                        await BackupLocalAsync(key, man.DeviceName);
                        await ApplyDeleteAsync(key, entry.SavedAt);
                        result.applied++;
                        result.appliedKeys.Add(key);
                    }
                    else
                    {
                        lock (_stateLock) _state.Touched[key] = entry.SavedAt;
                    }
                    continue;
                }

                var remotePath = RemotePathOf(man.DeviceId.Length > 0 ? man.DeviceId : group.Key, key);
                var fileNode = tree.nodes.FirstOrDefault(n => n.Path == remotePath);
                if (fileNode is null) continue;

                var json = await ReadBlobAsync(fileNode.Sha);
                if (string.IsNullOrEmpty(json)) continue;
                if (IsSaveKey(key) && !RemoteSaveIsNewer(key, json)) continue;

                await BackupLocalAsync(key, man.DeviceName);
                await ApplyRawAsync(key, json, entry.SavedAt);
                result.applied++;
                result.appliedKeys.Add(key);
            }

            lock (_stateLock) _state.RemoteSha[manNode.Path] = manNode.Sha;
        }

        await SaveStateAsync();   // 清单 sha 无论有没有合并都要落盘，下次才知道远端变没变
        return result;
    }

    /// <summary>本地存档的剧情时间不早于远端时，拒绝用远端覆盖（上传时间晚不等于进度新）。</summary>
    private bool RemoteSaveIsNewer(string key, string remoteJson)
    {
        try
        {
            var localJson = _store.PathOf(key) is { } p && File.Exists(p) ? File.ReadAllText(p) : null;
            if (string.IsNullOrEmpty(localJson)) return true;
            var local = SaveStampOf(localJson);
            var remote = SaveStampOf(remoteJson);
            if (local is null || remote is null) return true;
            return remote > local;
        }
        catch { return true; }
    }

    private static DateTime? SaveStampOf(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("meta", out var meta)
                && meta.TryGetProperty("SavedAt", out var at)
                && at.TryGetDateTime(out var stamp))
                return stamp.Kind == DateTimeKind.Utc ? stamp : stamp.ToUniversalTime();
        }
        catch { }
        return null;
    }

    /// <summary>
    /// 合并后让内存缓存跟上磁盘：记忆服务、角色库（否则另一台设备新建的角色要重启才出现）。
    /// </summary>
    private async Task RefreshAfterPullAsync(PullResult pull)
    {
        _scope.InvalidateCaches();
        if (pull.appliedKeys.Any(k => k.Equals("characters", StringComparison.OrdinalIgnoreCase)))
            await _library.ReloadAsync();
    }

    private async Task ApplyRawAsync(string key, string json, DateTime appliedAt)
    {
        Volatile.Write(ref _applyingKey, key);
        try { await _store.WriteRawAsync(key, json); }
        finally { Volatile.Write(ref _applyingKey, null); }
        lock (_stateLock)
        {
            // 沿用远端清单的时间戳：两端收敛到同一个值，否则会把同一份内容来回当成「对方更新」乒乓上传
            _state.Touched[key] = appliedAt;
            _state.Pushed[key] = appliedAt;
        }
    }

    private async Task ApplyDeleteAsync(string key, DateTime appliedAt)
    {
        Volatile.Write(ref _applyingKey, key);
        try { _store.Delete(key); }
        finally { Volatile.Write(ref _applyingKey, null); }
        lock (_stateLock)
        {
            _state.Touched[key] = appliedAt;
            _state.Pushed[key] = appliedAt;
            _state.Deleted.Remove(key);
        }
        await Task.CompletedTask;
    }

    /// <summary>覆盖前留底：双端同时用最坏也只是旧文件被挪进 cloud/conflicts，不会写坏。</summary>
    private async Task BackupLocalAsync(string key, string fromDevice)
    {
        try
        {
            var path = _store.PathOf(key);
            if (!File.Exists(path)) return;
            var json = await File.ReadAllTextAsync(path);
            var safeKey = key.Replace('/', '_').Replace('\\', '_');
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var name = $"{ConflictRoot}/{stamp}_{Sanitize(fromDevice)}_{safeKey}";
            await _store.WriteRawAsync(name, json);
            PruneConflicts();
        }
        catch (Exception ex) { App.WriteLog("CloudSync.Backup -> " + ex.Message); }
    }

    private void PruneConflicts()
    {
        try
        {
            var dir = Path.Combine(_store.Root, "cloud", "conflicts");
            if (!Directory.Exists(dir)) return;
            var files = new DirectoryInfo(dir).GetFiles("*.json").OrderByDescending(f => f.LastWriteTimeUtc).ToList();
            foreach (var f in files.Skip(MaxConflictFiles))
            {
                try { f.Delete(); } catch { }
            }
        }
        catch { }
    }

    private static string Sanitize(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "device";
        var chars = s.Trim().Where(ch => char.IsLetterOrDigit(ch) || ch == '-' || ch == '_').Take(24).ToArray();
        return chars.Length == 0 ? "device" : new string(chars);
    }

    // ==================== 推送 ====================

    private async Task<(int ok, int failed, string? error)> PushAsync(string branch, bool forceAll)
    {
        var deviceId = EnsureDeviceId();
        var keys = ListLocalKeys();
        var todo = new List<(string key, string raw, DateTime at)>();
        var skipped = 0;

        foreach (var key in keys)
        {
            string? raw;
            try { raw = await _store.LoadRawAsync(key); } catch { continue; }
            if (raw is null) continue;
            if (Encoding.UTF8.GetByteCount(raw) > MaxFileBytes)
            {
                skipped++;
                App.WriteLog($"CloudSync: 跳过过大文件 {key}");
                continue;
            }

            DateTime at;
            lock (_stateLock)
            {
                at = _state.Touched.TryGetValue(key, out var t) ? t : default;
                if (!forceAll && at != default && _state.Pushed.TryGetValue(key, out var pushed) && pushed >= at)
                    continue;
                if (at == default)
                {
                    try { at = File.GetLastWriteTimeUtc(_store.PathOf(key)); } catch { at = DateTime.UtcNow; }
                    _state.Touched[key] = at;
                }
            }
            todo.Add((key, raw, at));
        }

        // 本地已删除的键：只在远端也删掉，并写进清单墓碑，让别的设备跟着删（删前会留底）
        var tombstones = new List<string>();
        lock (_stateLock)
        {
            foreach (var key in _state.Deleted.Keys)
                if (!_store.Exists(key)) tombstones.Add(key);
        }

        var batch = todo.Take(MaxUploadsPerRun).ToList();
        var ok = 0;
        var failed = 0;
        string? lastError = null;
        foreach (var (key, raw, _) in batch)
        {
            var (success, error) = await PutFileAsync(branch, RemotePathOf(deviceId, key), raw,
                $"wab: {DeviceName()} 更新 {key} @{DateTime.UtcNow:u}");
            if (success)
            {
                ok++;
                lock (_stateLock) _state.Pushed[key] = _state.Touched.TryGetValue(key, out var t) ? t : DateTime.UtcNow;
            }
            else
            {
                failed++;
                lastError = error;
            }
        }

        foreach (var key in tombstones)
        {
            await DeleteRemoteAsync(branch, RemotePathOf(deviceId, key));
        }

        await UploadManifestAsync(branch, deviceId);

        if (todo.Count > batch.Count) ScheduleUpload(4000);   // 剩下的下一轮继续
        if (skipped > 0) App.WriteLog($"CloudSync.Push: {skipped} 个文件因体积过大跳过");
        return (ok, failed, lastError);
    }

    private async Task UploadManifestAsync(string branch, string deviceId)
    {
        var manifest = new CloudManifest
        {
            DeviceId = deviceId,
            DeviceName = DeviceName(),
            UpdatedAt = DateTime.UtcNow
        };
        lock (_stateLock)
        {
            foreach (var key in ListLocalKeys())
            {
                var at = _state.Touched.TryGetValue(key, out var t) ? t : default;
                if (at == default)
                {
                    try { at = File.GetLastWriteTimeUtc(_store.PathOf(key)); } catch { at = DateTime.UtcNow; }
                }
                manifest.Files[key] = new CloudManifestEntry { SavedAt = at };
            }
            foreach (var (key, at) in _state.Deleted)
                if (!_store.Exists(key))
                    manifest.Files[key] = new CloudManifestEntry { SavedAt = at, Deleted = true };
        }
        var json = JsonSerializer.Serialize(manifest);
        var (success, error) = await PutFileAsync(branch, ManifestPathOf(deviceId), json,
            $"wab: {DeviceName()} 清单 @{DateTime.UtcNow:u}");
        if (!success) App.WriteLog("CloudSync.Manifest -> " + error);
    }

    // ==================== GitHub 客户端 ====================

    private sealed record TreeResult(bool ok, string? error, List<TreeNode> nodes);
    private sealed record TreeNode(string Path, string Sha);

    private string LastError { get; set; } = "";

    private async Task<string?> ResolveBranchAsync(bool force = false)
    {
        if (!force && _branch.Length > 0) return _branch;
        _branch = "";
        var want = (_settings.Current.CloudBranch ?? "").Trim();
        if (want.Length == 0) want = "main";
        if (await GetTreeAsync(want) is { ok: true })
        {
            _branch = want;
            return want;
        }
        // 配的分支不存在时退回仓库默认分支（老仓库常见 master）
        var def = await GetDefaultBranchAsync();
        if (!string.IsNullOrEmpty(def) && def != want && await GetTreeAsync(def) is { ok: true })
        {
            _branch = def!;
            return _branch;
        }
        return null;
    }

    private async Task<TreeResult> GetTreeAsync(string branch)
    {
        try
        {
            using var req = Request(HttpMethod.Get, ApiUrl($"/git/trees/{Uri.EscapeDataString(branch)}?recursive=1"));
            using var resp = await _http.SendAsync(req);
            if (!resp.IsSuccessStatusCode)
            {
                LastError = await BriefErrorAsync(resp);
                return new TreeResult(false, LastError, new());
            }
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var nodes = new List<TreeNode>();
            if (doc.RootElement.TryGetProperty("tree", out var tree))
            {
                foreach (var item in tree.EnumerateArray())
                {
                    if (item.TryGetProperty("type", out var type) && type.GetString() != "blob") continue;
                    if (!item.TryGetProperty("path", out var p) || !item.TryGetProperty("sha", out var sha)) continue;
                    var path = p.GetString();
                    var hash = sha.GetString();
                    if (!string.IsNullOrEmpty(path) && !string.IsNullOrEmpty(hash))
                        nodes.Add(new TreeNode(path!, hash!));
                }
            }
            return new TreeResult(true, null, nodes);
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return new TreeResult(false, ex.Message, new());
        }
    }

    private async Task<string?> GetDefaultBranchAsync()
    {
        try
        {
            using var req = Request(HttpMethod.Get, ApiUrl(""));
            using var resp = await _http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            return doc.RootElement.TryGetProperty("default_branch", out var b) ? b.GetString() : null;
        }
        catch { return null; }
    }

    private async Task<string?> ReadBlobAsync(string sha)
    {
        try
        {
            using var req = Request(HttpMethod.Get, ApiUrl($"/git/blobs/{sha}"));
            using var resp = await _http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            if (!doc.RootElement.TryGetProperty("content", out var content)) return null;
            var b64 = (content.GetString() ?? "").Replace("\n", "").Replace("\r", "");
            if (b64.Length == 0) return null;
            var bytes = Convert.FromBase64String(b64);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (Exception ex)
        {
            App.WriteLog("CloudSync.ReadBlob -> " + ex.Message);
            return null;
        }
    }

    private async Task<T?> ReadJsonAsync<T>(string sha) where T : class
    {
        var json = await ReadBlobAsync(sha);
        if (string.IsNullOrEmpty(json)) return null;
        try { return JsonSerializer.Deserialize<T>(json); }
        catch (Exception ex)
        {
            App.WriteLog("CloudSync.ReadJson -> " + ex.Message);
            return null;
        }
    }

    private async Task<(bool ok, string? error)> PutFileAsync(string branch, string path, string content, string message)
    {
        var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(content));
        string? lastError = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var sha = await GetShaAsync(branch, path);
            try
            {
                var body = new Dictionary<string, object?>
                {
                    ["message"] = message,
                    ["content"] = b64,
                    ["branch"] = branch
                };
                if (!string.IsNullOrEmpty(sha)) body["sha"] = sha;

                using var req = Request(HttpMethod.Put, ApiUrl($"/contents/{EscapePath(path)}"));
                req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
                using var resp = await _http.SendAsync(req);
                if (resp.IsSuccessStatusCode) return (true, null);
                lastError = await BriefErrorAsync(resp);
                if (resp.StatusCode is not (HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity))
                    return (false, lastError);
            }
            catch (Exception ex) { lastError = ex.Message; }
            await Task.Delay(250 * (attempt + 1));
        }
        return (false, lastError);
    }

    private async Task<string?> GetShaAsync(string branch, string path)
    {
        try
        {
            using var req = Request(HttpMethod.Get, ApiUrl($"/contents/{EscapePath(path)}?ref={Uri.EscapeDataString(branch)}"));
            using var resp = await _http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            return doc.RootElement.TryGetProperty("sha", out var sha) ? sha.GetString() : null;
        }
        catch { return null; }
    }

    private async Task DeleteRemoteAsync(string branch, string path)
    {
        try
        {
            var sha = await GetShaAsync(branch, path);
            if (string.IsNullOrEmpty(sha)) return;
            var body = new Dictionary<string, object?>
            {
                ["message"] = $"wab: {DeviceName()} 删除 {path}",
                ["sha"] = sha,
                ["branch"] = branch
            };
            using var req = Request(HttpMethod.Delete, ApiUrl($"/contents/{EscapePath(path)}"));
            req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            using var resp = await _http.SendAsync(req);
            if (!resp.IsSuccessStatusCode)
                App.WriteLog("CloudSync.DeleteRemote -> " + await BriefErrorAsync(resp));
        }
        catch (Exception ex) { App.WriteLog("CloudSync.DeleteRemote -> " + ex.Message); }
    }

    private HttpRequestMessage Request(HttpMethod method, string url)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        req.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
        req.Headers.TryAddWithoutValidation("User-Agent", "WarmAsBefore-CloudSave");
        return req;
    }

    private string ApiUrl(string suffix) => $"{ApiRoot}/repos/{Repo}{suffix}";

    private static string EscapePath(string path) =>
        string.Join('/', path.Split('/').Select(Uri.EscapeDataString));

    private static async Task<string> BriefErrorAsync(HttpResponseMessage resp)
    {
        try
        {
            var body = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("message", out var m))
                return $"{(int)resp.StatusCode} {m.GetString()}";
        }
        catch { }
        return $"{(int)resp.StatusCode} {resp.ReasonPhrase}";
    }

    // ==================== 本地键与状态 ====================

    private static bool IsSyncable(string key)
    {
        // 清单是远端发来的：键必须是数据目录内的相对路径。
        // 少这一道，一个 "../settings" 就能把文件写到数据目录外面，还能绕过下面那条 settings 排除。
        if (!StorageProvider.IsSafeKey(key)) return false;
        if (string.IsNullOrWhiteSpace(key)) return false;
        if (key.Equals(SettingsKey, StringComparison.OrdinalIgnoreCase)) return false;   // 令牌/设备身份不外传
        var top = key.Split('/')[0];
        if (top.Equals("cloud", StringComparison.OrdinalIgnoreCase)) return false;       // 同步状态与冲突备份
        if (top.Equals("perf", StringComparison.OrdinalIgnoreCase)) return false;
        // 读坏时的时间戳备份不上云：坏掉的历史副本既不是任何键的当前内容，
        // 多一份就等于把同一份私密数据（可能含 settings 的旧密钥）多推一个分支到仓库里。
        if (Path.GetFileName(key).Contains(".corrupt-", StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }

    private static bool IsSaveKey(string key) =>
        key.StartsWith("saves/", StringComparison.OrdinalIgnoreCase);

    private List<string> ListLocalKeys()
    {
        try
        {
            var root = _store.Root;
            if (!Directory.Exists(root)) return new List<string>();
            return Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(root, f))
                .Where(p => p.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                .Select(p => p[..^5].Replace('\\', '/'))
                .Where(IsSyncable)
                .ToList();
        }
        catch (Exception ex)
        {
            App.WriteLog("CloudSync.ListKeys -> " + ex.Message);
            return new List<string>();
        }
    }

    private void SeedTouchedFromDisk()
    {
        lock (_stateLock)
        {
            foreach (var key in ListLocalKeys())
            {
                if (_state.Touched.ContainsKey(key)) continue;
                try { _state.Touched[key] = File.GetLastWriteTimeUtc(_store.PathOf(key)); }
                catch { _state.Touched[key] = DateTime.UtcNow; }
            }
        }
    }

    private DateTime TouchedOf(string key)
    {
        lock (_stateLock)
        {
            if (_state.Touched.TryGetValue(key, out var t)) return t;
            try
            {
                var at = File.GetLastWriteTimeUtc(_store.PathOf(key));
                _state.Touched[key] = at;
                return at;
            }
            catch { return DateTime.MinValue; }
        }
    }

    private string EnsureDeviceId()
    {
        lock (_stateLock)
        {
            if (string.IsNullOrEmpty(_state.DeviceId))
            {
                _state.DeviceId = Guid.NewGuid().ToString("N")[..10];
                App.WriteLog("CloudSync: 新设备目录 " + _state.DeviceId);
            }
            return _state.DeviceId;
        }
    }

    private static string DeviceDir(string deviceId) => $"{DevRoot}/{deviceId}";
    private static string RemotePathOf(string deviceId, string key) => $"{DeviceDir(deviceId)}/files/{key}.json";
    private static string ManifestPathOf(string deviceId) => $"{DeviceDir(deviceId)}/manifest.json";

    private async Task SaveStateAsync()
    {
        CloudState copy;
        lock (_stateLock)
        {
            copy = new CloudState
            {
                DeviceId = _state.DeviceId,
                Touched = new Dictionary<string, DateTime>(_state.Touched, StringComparer.Ordinal),
                Pushed = new Dictionary<string, DateTime>(_state.Pushed, StringComparer.Ordinal),
                Deleted = new Dictionary<string, DateTime>(_state.Deleted, StringComparer.Ordinal),
                RemoteSha = new Dictionary<string, string>(_state.RemoteSha, StringComparer.Ordinal),
                LastPullUtc = _state.LastPullUtc,
                LastPushUtc = _state.LastPushUtc
            };
        }
        try { await _store.Save(StateKey, copy); }
        catch (Exception ex) { App.WriteLog("CloudSync.SaveState -> " + ex.Message); }
    }

    private string DeviceName()
    {
        var configured = (_settings.Current.CloudDeviceName ?? "").Trim();
        if (configured.Length > 0) return configured;
        try { return Environment.MachineName; } catch { return "device"; }
    }

    private string SetStatus(string text)
    {
        Status = text;
        try { StatusChanged?.Invoke(text); } catch { }
        return text;
    }

    private void SetBusy(bool busy) => IsBusy = busy;

    /// <summary>仓库写成完整链接也认：https://github.com/owner/repo → owner/repo。</summary>
    public static string NormalizeRepo(string? raw)
    {
        var r = (raw ?? "").Trim();
        if (r.Length == 0) return "";
        if (r.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || r.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            if (Uri.TryCreate(r, UriKind.Absolute, out var uri))
            {
                var parts = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2) r = parts[0] + "/" + parts[1];
            }
        }
        r = r.Trim().Trim('/');
        if (r.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) r = r[..^4];
        var segs = r.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segs.Length == 2 ? segs[0] + "/" + segs[1] : "";
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _store.Changed -= OnLocalChanged; } catch { }
        _debounce?.Dispose();
        _periodicPull?.Dispose();
        _http.Dispose();
        _gate.Dispose();
    }

    private sealed class CloudState
    {
        public string DeviceId { get; set; } = "";
        /// <summary>本机每个键最后一次写入的时间（合并时用它和远端比新旧）。</summary>
        public Dictionary<string, DateTime> Touched { get; set; } = new(StringComparer.Ordinal);
        /// <summary>本机每个键最后一次成功上传的时间。</summary>
        public Dictionary<string, DateTime> Pushed { get; set; } = new(StringComparer.Ordinal);
        /// <summary>本机删掉的键 → 删除时间（以墓碑同步给别的设备）。</summary>
        public Dictionary<string, DateTime> Deleted { get; set; } = new(StringComparer.Ordinal);
        /// <summary>远端清单路径 → 上次见到的 sha（没变就不重复下载）。</summary>
        public Dictionary<string, string> RemoteSha { get; set; } = new(StringComparer.Ordinal);
        public DateTime LastPullUtc { get; set; }
        public DateTime LastPushUtc { get; set; }
    }
}

/// <summary>一台设备的清单：列出它写过的每个键与写入时间，拉取时靠它判断谁更新。</summary>
public sealed class CloudManifest
{
    public string DeviceId { get; set; } = "";
    public string DeviceName { get; set; } = "";
    public DateTime UpdatedAt { get; set; }
    public Dictionary<string, CloudManifestEntry> Files { get; set; } = new(StringComparer.Ordinal);
}

public sealed class CloudManifestEntry
{
    public DateTime SavedAt { get; set; }
    public bool Deleted { get; set; }
}
