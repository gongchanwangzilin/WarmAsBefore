using System.Globalization;
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
            // 门禁：仓库不是私人的就一个字节都不推（公开仓库里的存档等于公开）
            var privacy = await RequirePrivateRepoAsync();
            if (!privacy.ok)
            {
                App.WriteLog("CloudSync.Push -> 已拦截：" + privacy.message);
                SetStatus(privacy.message);
                return;
            }
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
        // 退出路径不能阻塞窗口关闭：门禁放到后台做，未通过时连一个远端请求都不会发出
        _ = FlushOnExitAsync();
    }

    private async Task FlushOnExitAsync()
    {
        try
        {
            var privacy = await RequirePrivateRepoAsync();
            if (!privacy.ok)
            {
                App.WriteLog("CloudSync.FlushOnExit -> 已拦截：" + privacy.message);
                return;
            }
            await PushPendingAsync();
        }
        catch (Exception ex)
        {
            App.WriteLog("CloudSync.FlushOnExit -> " + ex.Message);
        }
    }

    // ==================== 手动同步 ====================

    /// <summary>测试仓库与令牌是否可用（只读一次目录树，不写任何东西）。</summary>
    public async Task<(bool ok, string message)> TestAsync()
    {
        if (Repo.Length == 0) return (false, "请先填写仓库（owner/repo）");
        if (Token.Length == 0) return (false, "请先填写访问令牌");

        // 先确认仓库是私人的：公开仓库上的任何一次上传都等于把存档公开。
        // 「测试连接」本来就要打 API，借这一次强制重查，用户改完可见性后点它就能立刻看到结果。
        var privacy = await VerifyRepoPrivacyAsync();
        if (!privacy.ok) return privacy;

        var branch = await ResolveBranchAsync(force: true);
        if (branch is null) return (false, LastError ?? "连不上仓库：检查仓库名、令牌权限（Contents 读写）与分支");
        var tree = await GetTreeAsync(branch);
        if (!tree.ok) return (false, "连不上仓库：" + (tree.error ?? "未知错误"));
        var count = tree.nodes.Count(n => n.Path.StartsWith(DevRoot + "/", StringComparison.Ordinal));
        SetStatus($"已连通 {Repo}（分支 {branch} · 私人仓库已确认）");
        return (true, $"已连通 {Repo}（分支 {branch}）· 私人仓库已确认 · 云端已有 {count} 个文件");
    }

    // ==================== 仓库私有性门禁 ====================
    // 需求：云存档只能写进用户自己的私人仓库，否则填一个公开仓库就等于把存档公开。
    // GitHub API 的 /repos/{owner}/{repo} 会返回 private / visibility，用现有令牌请求即可，
    // 不需要 OAuth 登录：访问令牌本身就是登录凭据，只需要 repo（私有仓库读写）权限。
    //
    // 缓存按「仓库 + 令牌」签名：换仓库或换令牌必然重查；签名不变时通过的结果缓存 10 分钟，
    // 避免每次自动上传都打一次 API，同时仓库被改成公开后也能在 10 分钟内发现并拦住。

    private readonly SemaphoreSlim _privacyGate = new(1, 1);
    private readonly object _privacyLock = new();
    private string _privacySignature = "";
    private bool _privacyVerified;
    private bool _privacyPublic;
    private string _privacyNote = "";
    private DateTime _privacyCheckedAtUtc = DateTime.MinValue;
    private static readonly TimeSpan PrivacyPassCacheTtl = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan PrivacyFailRetryDelay = TimeSpan.FromSeconds(30);

    /// <summary>最近一次校验是否通过（仓库确实存在且是私人的）。</summary>
    public bool IsRepoPrivacyVerified
    {
        get
        {
            lock (_privacyLock) return _privacyVerified && _privacySignature == CurrentPrivacySignature;
        }
    }

    /// <summary>空 = 还没校验过；否则是可以直接展示给用户的人话（通过或未通过都算）。</summary>
    public string RepoPrivacyNote
    {
        get
        {
            lock (_privacyLock) return _privacySignature == CurrentPrivacySignature ? _privacyNote : "";
        }
    }

    /// <summary>已校验且确为私人仓库（internal 也算不对外的仓库）。</summary>
    public bool IsPrivateRepo => IsRepoPrivacyVerified;

    /// <summary>
    /// 已确认填的是公开仓库。只用于区分「明确公开」与「连不上」：
    /// 公开时要连本机副本的恢复一起拦住（恢复出来的内容迟早会被推到那个公开仓库），
    /// 连不上时本机副本不涉及任何远端请求，照常可看可恢复，真正上传时仍会被门禁挡住。
    /// </summary>
    private bool IsRepoKnownPublic
    {
        get
        {
            lock (_privacyLock) return !_privacyVerified && _privacyPublic && _privacySignature == CurrentPrivacySignature;
        }
    }

    /// <summary>强制校验一次仓库私有性；页面上的「验证私人仓库」按钮与 TestAsync 都走这里。</summary>
    public async Task<(bool ok, string message)> VerifyRepoPrivacyAsync()
    {
        var result = await RequirePrivateRepoAsync(force: true);
        SetStatus(result.message);
        App.WriteLog("CloudSync.VerifyRepoPrivacy -> " + (result.ok ? "通过：" : "未通过：") + result.message);
        return result;
    }

    /// <summary>所有会读写远端的入口先过这道门；通过（含缓存）才允许继续。</summary>
    private async Task<(bool ok, string message)> RequirePrivateRepoAsync(bool force = false)
    {
        var repo = Repo;
        if (repo.Length == 0) return StorePrivacy("", false, false, "请先填写仓库（owner/repo）");
        if (Token.Length == 0) return StorePrivacy("", false, false, "请先填写访问令牌");

        var signature = repo + "\u0001" + Token;
        if (!force && TryGetFreshPrivacy(signature, out var cached)) return cached;

        await _privacyGate.WaitAsync();
        try
        {
            // 排队期间别的入口可能已经查完了：再判一次，避免并发重复打 API
            if (!force && TryGetFreshPrivacy(signature, out cached)) return cached;

            var (ok, isPublic, message) = await FetchRepoPrivacyAsync(repo);
            App.WriteLog("CloudSync.Privacy -> " + repo + "：" + (ok ? "已确认私人仓库" : message));
            return StorePrivacy(signature, ok, isPublic, message);
        }
        finally
        {
            _privacyGate.Release();
        }
    }

    /// <summary>缓存命中判断：通过的结果缓存久一点，失败只挡几十秒，让用户改完就能重试。</summary>
    private bool TryGetFreshPrivacy(string signature, out (bool ok, string message) result)
    {
        lock (_privacyLock)
        {
            if (_privacySignature == signature && _privacyNote.Length > 0)
            {
                var age = DateTime.UtcNow - _privacyCheckedAtUtc;
                var ttl = _privacyVerified ? PrivacyPassCacheTtl : PrivacyFailRetryDelay;
                if (age >= TimeSpan.Zero && age < ttl)
                {
                    result = (_privacyVerified, _privacyNote);
                    return true;
                }
            }
            result = default;
            return false;
        }
    }

    private (bool ok, string message) StorePrivacy(string signature, bool ok, bool isPublic, string message)
    {
        lock (_privacyLock)
        {
            _privacySignature = signature;
            _privacyVerified = ok;
            _privacyPublic = isPublic;
            _privacyNote = message;
            _privacyCheckedAtUtc = DateTime.UtcNow;
        }
        return (ok, message);
    }

    /// <summary>当前设置对应的缓存指纹；令牌不进日志、不落盘，只在这个字段里做比较。</summary>
    private string CurrentPrivacySignature
    {
        get
        {
            var repo = Repo;
            var token = Token;
            return repo.Length == 0 || token.Length == 0 ? "" : repo + "\u0001" + token;
        }
    }

    /// <summary>读 GET /repos/{owner}/{repo} 判断可见性；失败一律折成能直接展示的中文。</summary>
    private async Task<(bool ok, bool isPublic, string message)> FetchRepoPrivacyAsync(string repo)
    {
        var cantReach = $"连不上「{repo}」：确认仓库名拼写，并确认访问令牌有 repo（私有仓库读写）权限。";
        try
        {
            using var req = Request(HttpMethod.Get, ApiUrl(""));
            using var resp = await _http.SendAsync(req);
            var body = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode) return (false, false, cantReach);

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var visibility = root.TryGetProperty("visibility", out var v) ? (v.GetString() ?? "").Trim() : "";
            var isPrivate = root.TryGetProperty("private", out var p) && p.ValueKind == JsonValueKind.True;

            if (isPrivate
                || visibility.Equals("private", StringComparison.OrdinalIgnoreCase)
                || visibility.Equals("internal", StringComparison.OrdinalIgnoreCase))
                return (true, false, $"已确认「{repo}」是私人仓库：存档只写进你自己的仓库。");

            // 明确读到公开：给出能照着改的步骤，不要只说「校验失败」
            if (visibility.Equals("public", StringComparison.OrdinalIgnoreCase)
                || (root.TryGetProperty("private", out var p2) && p2.ValueKind == JsonValueKind.False))
                return (false, true,
                    $"云存档只写私人仓库：「{repo}」是公开的。请到 GitHub → Settings → 把仓库改为 Private，或换一个私人仓库。");

            // 200 但读不到可见性字段（GitHub 改版/代理改写）：不能证明是私人的，就按不通过处理
            return (false, false, cantReach);
        }
        catch (Exception ex)
        {
            App.WriteLog("CloudSync.Privacy -> " + ex.Message);
            return (false, false, cantReach);
        }
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
            // 门禁：同步同时读写远端，仓库不是私人的就直接拒绝，并把人话留在状态行
            var privacy = await RequirePrivateRepoAsync();
            if (!privacy.ok)
            {
                App.WriteLog("CloudSync.SyncNow -> 仓库私有性未通过：" + privacy.message);
                return SetStatus(privacy.message);
            }
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
            // 定时拉取同样先过门禁：公开仓库里的存档路径也不想被这台设备读下来
            var privacy = await RequirePrivateRepoAsync();
            if (!privacy.ok) { SetStatus(privacy.message); return; }
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

    // ==================== 只读浏览：云端存档与历史副本 ====================
    // 下面这一组只读：不写 manifest、不触发上传，失败一律折成中文原因返回，不往 UI 线程抛。

    /// <summary>仓库与令牌是否齐全：页面靠它区分「没配置」与「令牌失效」。</summary>
    public bool IsCloudConfigured => IsConfigured;

    /// <summary>本机在云端使用的设备名（只用于展示）。</summary>
    public string CurrentDeviceName => DeviceName();

    /// <summary>最近一次成功拉取或上传的时间（UTC）；从没同步过是 null。</summary>
    public DateTime? LastSyncUtc
    {
        get
        {
            lock (_stateLock)
            {
                var at = _state.LastPullUtc > _state.LastPushUtc ? _state.LastPullUtc : _state.LastPushUtc;
                return at == default ? null : at;
            }
        }
    }

    /// <summary>
    /// 列出云端所有设备写过的存档。每个设备读一次清单，每份存档再读一次本体拿存档名与体积
    /// —— 清单里只有键和时间，没有「叫什么」，只靠它列表会全是随机 id。
    /// </summary>
    public async Task<CloudSaveSnapshot> ListRemoteSavesAsync(CancellationToken ct = default)
    {
        var deviceId = "";
        try
        {
            await EnsureReadyAsync();
            deviceId = EnsureDeviceId();
            if (!IsEnabled)
                return MakeSnapshot(CloudReadState.NotConfigured, deviceId,
                    "云存档开关没打开。到 设置 → 数据 → 云存档 打开它，再回来点「刷新」。");
            if (!IsConfigured)
                return MakeSnapshot(CloudReadState.NotConfigured, deviceId,
                    "还没填仓库与令牌。到 设置 → 数据 → 云存档 填好仓库（owner/repo）与访问令牌，再回来点「刷新」。");

            // 门禁：连列表都不读，公开仓库里的存档路径同样不该被当成云存档使用。
            // 区分「确认为公开」与「连不上」：前者是配置问题（换私人仓库），后者是网络/令牌问题。
            var privacy = await RequirePrivateRepoAsync();
            if (!privacy.ok)
            {
                SetStatus(privacy.message);
                return MakeSnapshot(IsRepoKnownPublic ? CloudReadState.NotPrivate : CloudReadState.Offline,
                    deviceId, privacy.message);
            }

            var branch = await ResolveBranchAsync();
            if (branch is null)
            {
                var err = LastError;
                return MakeSnapshot(ClassifyIssue(err), deviceId, DescribeIssue(err));
            }

            var tree = await GetTreeAsync(branch);
            if (!tree.ok)
            {
                var err = tree.error ?? LastError;
                return MakeSnapshot(ClassifyIssue(err), deviceId, DescribeIssue(err));
            }

            var local = await LocalSaveInfosAsync();
            var saves = new List<CloudSaveEntry>();
            var groups = tree.nodes
                .Where(n => n.Path.StartsWith(DevRoot + "/", StringComparison.Ordinal))
                .GroupBy(n => n.Path.Split('/').Length > 2 ? n.Path.Split('/')[2] : "")
                .Where(g => g.Key.Length > 0);

            foreach (var group in groups)
            {
                ct.ThrowIfCancellationRequested();
                var manNode = group.FirstOrDefault(n => n.Path.EndsWith("/manifest.json", StringComparison.Ordinal));
                if (manNode is null) continue;
                var man = await ReadJsonAsync<CloudManifest>(manNode.Sha);
                if (man is null) continue;

                var devId = man.DeviceId.Length > 0 ? man.DeviceId : group.Key;
                var devName = string.IsNullOrWhiteSpace(man.DeviceName) ? devId : man.DeviceName.Trim();
                foreach (var (key, entry) in man.Files)
                {
                    if (entry.Deleted || !IsSaveKey(key) || !StorageProvider.IsSafeKey(key)) continue;

                    // 路径以目录树为准（清单里的 DeviceId 只是它自称），否则老仓库里改过设备名的目录对不上
                    var remotePath = RemotePathOf(group.Key, key);
                    var node = group.FirstOrDefault(n => n.Path == remotePath);
                    if (node is null) continue;

                    var json = await ReadBlobAsync(node.Sha);
                    var label = SaveLabelOf(json);
                    var exists = local.Any(l => l.key.Equals(key, StringComparison.Ordinal));
                    var sameName = label.Length > 0 && local.Any(l =>
                        !l.key.Equals(key, StringComparison.Ordinal) && l.label.Equals(label, StringComparison.Ordinal));

                    saves.Add(new CloudSaveEntry
                    {
                        Key = key,
                        RemotePath = remotePath,
                        Sha = node.Sha,
                        SaveId = key["saves/".Length..],
                        SaveName = label,
                        DeviceId = devId,
                        DeviceName = devName,
                        SavedAtUtc = ToUtc(entry.SavedAt),
                        SizeBytes = json is null ? 0 : Encoding.UTF8.GetByteCount(json),
                        IsCurrentDevice = devId == deviceId || group.Key == deviceId,
                        ExistsLocally = exists,
                        HasLocalSameName = sameName
                    });
                }
            }

            var ordered = saves
                .OrderByDescending(s => s.IsCurrentDevice)
                .ThenByDescending(s => s.SavedAtUtc)
                .ToList();
            return MakeSnapshot(CloudReadState.Ok, deviceId, "", ordered);
        }
        catch (OperationCanceledException)
        {
            return MakeSnapshot(CloudReadState.Failed, deviceId, "读取已取消，点「刷新」重新来一次。");
        }
        catch (Exception ex)
        {
            App.WriteLog("CloudSync.ListRemoteSaves -> " + ex);
            return MakeSnapshot(CloudReadState.Failed, deviceId,
                "读取云端失败：" + ex.Message + "。检查网络后点右上角「刷新」。");
        }
    }

    /// <summary>把某台设备的一份存档取回本机；覆盖前先把本机旧版本留进历史副本。</summary>
    public async Task<CloudActionResult> DownloadSaveAsync(CloudSaveEntry entry, CancellationToken ct = default)
    {
        try
        {
            if (entry is null || !IsSaveKey(entry.Key) || !StorageProvider.IsSafeKey(entry.Key))
                return new CloudActionResult(false, "这份记录不是存档，无法下载。");
            await EnsureReadyAsync();
            if (!IsEnabled) return new CloudActionResult(false, "云存档开关没打开：到 设置 → 数据 → 云存档 打开它。");
            if (!IsConfigured) return new CloudActionResult(false, "还没填仓库与令牌：到 设置 → 数据 → 云存档 填好再试。");

            // 门禁：下载会把远端内容写回本机，仓库不是私人的就没必要碰它
            var privacy = await RequirePrivateRepoAsync();
            if (!privacy.ok) return new CloudActionResult(false, privacy.message);

            ct.ThrowIfCancellationRequested();
            var json = entry.Sha.Length > 0 ? await ReadBlobAsync(entry.Sha) : null;
            if (string.IsNullOrEmpty(json))
            {
                // sha 可能已过期：按路径再找一次，别让一次刷新把这次点击废掉
                var branch = await ResolveBranchAsync();
                if (branch is null) return new CloudActionResult(false, DescribeIssue(LastError));
                var tree = await GetTreeAsync(branch);
                if (!tree.ok) return new CloudActionResult(false, DescribeIssue(tree.error ?? LastError));
                var node = tree.nodes.FirstOrDefault(n => n.Path == entry.RemotePath);
                if (node is null) return new CloudActionResult(false, "云端这份存档已经不在了，点「刷新」看看最新列表。");
                json = await ReadBlobAsync(node.Sha);
            }
            if (string.IsNullOrEmpty(json))
                return new CloudActionResult(false, "下载失败：云端文件读不到（网络或权限），稍后再试。");

            if (_store.Exists(entry.Key)) await BackupLocalAsync(entry.Key, entry.DeviceName);
            await ApplyRawAsync(entry.Key, json, DateTime.UtcNow);
            lock (_stateLock) _state.Deleted.Remove(entry.Key);
            await SaveStateAsync();
            await RefreshAfterPullAsync(NewPullResult(entry.Key));

            var name = SaveLabelOf(json);
            return new CloudActionResult(true, $"已下载「{(name.Length > 0 ? name : entry.SaveId)}」到本机存档。");
        }
        catch (OperationCanceledException)
        {
            return new CloudActionResult(false, "下载已取消。");
        }
        catch (Exception ex)
        {
            App.WriteLog("CloudSync.DownloadSave -> " + ex);
            return new CloudActionResult(false, "下载失败：" + ex.Message);
        }
    }

    /// <summary>列出同步覆盖前自动留下的历史副本（cloud/conflicts，只在本机）。</summary>
    public async Task<CloudConflictList> ListConflictsAsync()
    {
        var items = new List<CloudConflictEntry>();
        try
        {
            // 历史副本本身在本机，但恢复后会被推回云端：已确认是公开仓库时统一返回原因，
            // 避免「云端读不了、历史副本却照常可恢复」看起来像两套逻辑。
            // 只是连不上/令牌失效时，本机副本不涉及任何远端请求，仍允许查看与恢复（上传侧另有关禁）。
            if (IsConfigured)
            {
                var privacy = await RequirePrivateRepoAsync();
                if (!privacy.ok && IsRepoKnownPublic)
                    return new CloudConflictList { Ok = false, Message = privacy.message };
            }

            var dir = Path.Combine(_store.Root, "cloud", "conflicts");
            if (!Directory.Exists(dir)) return new CloudConflictList { Ok = true, Items = items };

            var localKeys = ListLocalKeys();
            var files = new DirectoryInfo(dir).GetFiles("*.json")
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Take(MaxConflictFiles)
                .ToList();

            foreach (var file in files)
            {
                var name = Path.GetFileNameWithoutExtension(file.Name);
                var targetKey = ResolveConflictTarget(name, localKeys);
                var label = "";
                try { label = SaveLabelOf(await File.ReadAllTextAsync(file.FullName)); } catch { }
                if (label.Length == 0 && targetKey.Length > 0)
                {
                    try { label = SaveLabelOf(await _store.LoadRawAsync(targetKey)); } catch { }
                }

                var at = ConflictTimeOf(name);
                items.Add(new CloudConflictEntry
                {
                    ConflictKey = $"{ConflictRoot}/{name}",
                    TargetKey = targetKey,
                    TargetName = label.Length > 0 ? label
                        : targetKey.Length > 0 ? targetKey[(targetKey.LastIndexOf('/') + 1)..]
                        : name,
                    DeviceName = ConflictDeviceOf(name, targetKey),
                    SavedAtUtc = at == DateTime.MinValue ? file.LastWriteTimeUtc : at,
                    SizeBytes = file.Length,
                    TargetExists = targetKey.Length > 0 && _store.Exists(targetKey)
                });
            }
            return new CloudConflictList { Ok = true, Items = items };
        }
        catch (Exception ex)
        {
            App.WriteLog("CloudSync.ListConflicts -> " + ex);
            return new CloudConflictList { Ok = false, Message = "读取历史副本失败：" + ex.Message, Items = items };
        }
    }

    /// <summary>把一份历史副本写回它原来的键；恢复出来的内容会在下一次同步时推回云端。</summary>
    public async Task<CloudActionResult> RestoreConflictAsync(CloudConflictEntry entry, CancellationToken ct = default)
    {
        try
        {
            if (entry is null || !entry.CanRestore || !StorageProvider.IsSafeKey(entry.TargetKey)
                || !StorageProvider.IsSafeKey(entry.ConflictKey))
                return new CloudActionResult(false, "这份副本找不到对应的存档位置，无法恢复。");

            await EnsureReadyAsync();

            // 恢复只写本机，但恢复出来的内容下一次同步会推回云端：已确认是公开仓库时提前拦住；
            // 只是连不上/令牌失效时照常恢复，等网络或令牌好了再同步（上传门禁不会把内容推到公开仓库）。
            if (IsConfigured)
            {
                var privacy = await RequirePrivateRepoAsync();
                if (!privacy.ok && IsRepoKnownPublic)
                    return new CloudActionResult(false, privacy.message);
            }

            ct.ThrowIfCancellationRequested();
            var json = await _store.LoadRawAsync(entry.ConflictKey);
            if (string.IsNullOrEmpty(json))
                return new CloudActionResult(false, "这份历史副本已经不在了（可能被自动清理），点「刷新」看看列表。");

            if (_store.Exists(entry.TargetKey)) await BackupLocalAsync(entry.TargetKey, "恢复前");
            await ApplyRawAsync(entry.TargetKey, json, DateTime.UtcNow);
            lock (_stateLock)
            {
                _state.Deleted.Remove(entry.TargetKey);
                // 恢复出的内容与云端不同：抹掉「已推送」标记，下一次同步才会真的推上去
                _state.Pushed.Remove(entry.TargetKey);
            }
            await SaveStateAsync();
            await RefreshAfterPullAsync(NewPullResult(entry.TargetKey));
            if (IsEnabled && IsConfigured && _settings.Current.CloudAutoUpload) ScheduleUpload(1200);

            return new CloudActionResult(true, $"已恢复「{entry.TargetName}」到本机，下次同步会推回云端。");
        }
        catch (OperationCanceledException)
        {
            return new CloudActionResult(false, "恢复已取消。");
        }
        catch (Exception ex)
        {
            App.WriteLog("CloudSync.RestoreConflict -> " + ex);
            return new CloudActionResult(false, "恢复失败：" + ex.Message);
        }
    }

    private CloudSaveSnapshot MakeSnapshot(CloudReadState state, string deviceId, string message,
        List<CloudSaveEntry>? saves = null) => new()
    {
        State = state,
        Message = message,
        DeviceId = deviceId,
        DeviceName = DeviceName(),
        LastSyncUtc = this.LastSyncUtc,
        Saves = saves ?? new List<CloudSaveEntry>()
    };

    private static PullResult NewPullResult(string key)
    {
        var result = new PullResult { applied = 1 };
        result.appliedKeys.Add(key);
        return result;
    }

    /// <summary>清单里的时间是 UTC；早期手写清单可能没有时区，按 UTC 读，免得显示成几十年前。</summary>
    private static DateTime ToUtc(DateTime t) => t.Kind switch
    {
        DateTimeKind.Utc => t,
        DateTimeKind.Local => t.ToUniversalTime(),
        _ => DateTime.SpecifyKind(t, DateTimeKind.Utc)
    };

    /// <summary>从存档 JSON 的 meta.Label 取名字；读不出来返回空串，由调用方退回 id。</summary>
    private static string SaveLabelOf(string? json)
    {
        if (string.IsNullOrEmpty(json)) return "";
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("meta", out var meta)
                && meta.TryGetProperty("Label", out var label))
                return (label.GetString() ?? "").Trim();
        }
        catch { }
        return "";
    }

    private async Task<List<(string key, string label)>> LocalSaveInfosAsync()
    {
        var list = new List<(string key, string label)>();
        foreach (var key in ListLocalKeys())
        {
            if (!IsSaveKey(key)) continue;
            var label = "";
            try { label = SaveLabelOf(await _store.LoadRawAsync(key)); } catch { }
            list.Add((key, label));
        }
        return list;
    }

    /// <summary>把「401/403」「超时/拒连」折成页面上能读懂的三种失败，其余归为未知。</summary>
    private static CloudReadState ClassifyIssue(string? error)
    {
        var e = error ?? "";
        if (e.Contains("401", StringComparison.Ordinal) || e.Contains("403", StringComparison.Ordinal)
            || e.Contains("Bad credentials", StringComparison.OrdinalIgnoreCase)
            || e.Contains("Unauthorized", StringComparison.OrdinalIgnoreCase)
            || e.Contains("Forbidden", StringComparison.OrdinalIgnoreCase))
            return CloudReadState.NotSignedIn;
        if (e.Contains("timeout", StringComparison.OrdinalIgnoreCase)
            || e.Contains("timed out", StringComparison.OrdinalIgnoreCase)
            || e.Contains("network", StringComparison.OrdinalIgnoreCase)
            || e.Contains("connection", StringComparison.OrdinalIgnoreCase)
            || e.Contains("No such host", StringComparison.OrdinalIgnoreCase)
            || e.Contains("actively refused", StringComparison.OrdinalIgnoreCase)
            || e.Contains("SSL", StringComparison.OrdinalIgnoreCase)
            || e.Contains("certificate", StringComparison.OrdinalIgnoreCase)
            || e.Contains("canceled", StringComparison.OrdinalIgnoreCase)
            // 中文系统上 Socket/HttpClient 的异常消息是本地化的，上面那串英文一个都对不上
            || e.Contains("网络", StringComparison.Ordinal)
            || e.Contains("连接", StringComparison.Ordinal)
            || e.Contains("超时", StringComparison.Ordinal)
            || e.Contains("主机", StringComparison.Ordinal)
            || e.Contains("证书", StringComparison.Ordinal)
            || e.Contains("远程", StringComparison.Ordinal)
            || e.Contains("拒绝", StringComparison.Ordinal))
            return CloudReadState.Offline;
        return CloudReadState.Failed;
    }

    private static string DescribeIssue(string? error)
    {
        var e = error ?? "";
        return ClassifyIssue(e) switch
        {
            CloudReadState.NotSignedIn =>
                "令牌没通过 GitHub 校验（或没有 Contents 读写权限）。到 设置 → 数据 → 云存档 换一枚令牌，再回来点「刷新」。",
            CloudReadState.Offline =>
                "连不上 GitHub。检查网络（或代理）后点右上角「刷新」；历史副本在本机，断网也能恢复。",
            _ when e.StartsWith("404", StringComparison.Ordinal) =>
                "仓库不存在或令牌看不到它。到 设置 → 数据 → 云存档 核对仓库名（owner/repo）与分支。",
            _ =>
                "这次没读到云端：" + (e.Length == 0 ? "未知原因" : e)
                + "。到 设置 → 数据 → 云存档 核对仓库与分支，或稍后刷新。"
        };
    }

    /// <summary>
    /// 冲突文件名是「时间_设备_键（斜杠换下划线）」，本机键表能反推出原键；
    /// 存档已被删除时名字里仍留着 saves_ 段，按 12 位无下划线 id 兜底。
    /// </summary>
    private static string ResolveConflictTarget(string file, List<string> knownKeys)
    {
        foreach (var key in knownKeys.OrderByDescending(k => k.Length))
        {
            var safe = key.Replace('/', '_').Replace('\\', '_');
            if (file.EndsWith("_" + safe, StringComparison.Ordinal)) return key;
        }
        const string marker = "_saves_";
        var idx = file.IndexOf(marker, StringComparison.Ordinal);
        if (idx >= 0)
        {
            var id = file[(idx + marker.Length)..];
            if (id.Length > 0 && !id.Contains('_') && StorageProvider.IsSafeKey(id)) return "saves/" + id;
        }
        return "";
    }

    private static string ConflictDeviceOf(string file, string key)
    {
        var rest = file.Length > 16 ? file[16..] : "";
        if (key.Length > 0)
        {
            var safe = key.Replace('/', '_').Replace('\\', '_');
            if (rest.EndsWith("_" + safe, StringComparison.Ordinal))
                rest = rest[..^(safe.Length + 1)];
        }
        rest = rest.Trim('_');
        return rest.Length == 0 ? "历史副本" : rest;
    }

    private static DateTime ConflictTimeOf(string name)
    {
        if (name.Length <= 15) return DateTime.MinValue;
        return DateTime.TryParseExact(name[..15], "yyyyMMdd-HHmmss", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeLocal, out var local)
            ? local.ToUniversalTime()
            : DateTime.MinValue;
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
        _privacyGate.Dispose();
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

/// <summary>只读浏览的结果类型：够页面区分多种状态，不把异常带进 UI。</summary>
public enum CloudReadState
{
    Ok,
    NotConfigured,
    NotSignedIn,
    Offline,
    /// <summary>仓库是公开的，或私有性没能验证通过：云存档拒绝工作。</summary>
    NotPrivate,
    Failed
}

public sealed class CloudSaveSnapshot
{
    public CloudReadState State { get; init; } = CloudReadState.Ok;
    public string Message { get; init; } = "";
    public string DeviceId { get; init; } = "";
    public string DeviceName { get; init; } = "";
    public DateTime? LastSyncUtc { get; init; }
    public IReadOnlyList<CloudSaveEntry> Saves { get; init; } = Array.Empty<CloudSaveEntry>();
    public bool Ok => State == CloudReadState.Ok;
}

/// <summary>云端某台设备的一份存档。SaveName 是存档里的 Label，读不出来时用 SaveId。</summary>
public sealed class CloudSaveEntry
{
    public string Key { get; init; } = "";
    public string RemotePath { get; init; } = "";
    public string Sha { get; init; } = "";
    public string SaveId { get; init; } = "";
    public string SaveName { get; init; } = "";
    public string DeviceId { get; init; } = "";
    public string DeviceName { get; init; } = "";
    public DateTime SavedAtUtc { get; init; }
    public long SizeBytes { get; init; }
    public bool IsCurrentDevice { get; init; }
    /// <summary>本机是否已有同一槽位（下载会覆盖它，覆盖前留底）。</summary>
    public bool ExistsLocally { get; init; }
    /// <summary>本机另一份存档是否与它同名（给「来源不同、名字相同」提个醒）。</summary>
    public bool HasLocalSameName { get; init; }
}

public sealed class CloudConflictList
{
    public bool Ok { get; init; } = true;
    public string Message { get; init; } = "";
    public IReadOnlyList<CloudConflictEntry> Items { get; init; } = Array.Empty<CloudConflictEntry>();
}

/// <summary>一份同步覆盖前留下的历史副本。ConflictKey 指向备份文件，TargetKey 是它原来的位置。</summary>
public sealed class CloudConflictEntry
{
    public string ConflictKey { get; init; } = "";
    public string TargetKey { get; init; } = "";
    public string TargetName { get; init; } = "";
    public string DeviceName { get; init; } = "";
    public DateTime SavedAtUtc { get; init; }
    public long SizeBytes { get; init; }
    public bool TargetExists { get; init; }
    public bool CanRestore => TargetKey.Length > 0;
}

public sealed record CloudActionResult(bool Ok, string Message);
