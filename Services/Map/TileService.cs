using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace WarmAsBefore.Services;

/// <summary>
/// 底图瓦片的取图与缓存。
///
/// 三条硬规矩，全部围绕「别卡界面、别糟蹋别人的服务器」：
/// 1. 第一次要某块瓦片只在后台排队，画布这一帧就画父级顶替，绝不 await；
/// 2. 每块瓦片最多同时在飞一次（重复请求直接丢掉），失败指数退避，404 当成「这里真没有」；
/// 3. 磁盘缓存 LRU：<c>Cache/tiles/{来源}/{z}/{x}/{y}.png</c>，超预算先扔最久没读过的。
///
/// 联网开关是总闸，读的是设置里的 <c>MapTileOnline</c>：关掉之后这里一次 HTTP 都不发，
/// 只看缓存 —— 离线用户不会看到永远转圈的底图。
/// </summary>
public sealed class TileService : IDisposable
{
    /// <summary>磁盘缓存预算：手机 128MB、桌面 256MB。</summary>
    public const long CacheBudgetBytes =
#if ANDROID
        128L * 1024 * 1024;
#else
        256L * 1024 * 1024;
#endif

    /// <summary>内存里最多留多少张 256×256（约 256KB/张）：手机 120 张 ≈ 31MB，桌面 260 张 ≈ 66MB。</summary>
    private static int MemoryCap =>
#if ANDROID
        120;
#else
        260;
#endif

    private static readonly Lazy<HttpClient> SharedHttp = new(() =>
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        return client;
    });

    private readonly SettingsManager _settings;
    private readonly string _root;
    private readonly string _offlineRoot;

    // ---- MBTiles 离线包：单例连接 + 64 块字节缓存都在 MbTilesSource 里，这里只管换包与统计 ----
    private readonly object _mbGate = new();
    private MbTilesSource? _mbTiles;
    private string _mbTilesPath = "";
    private MbTilesStatus _mbStatus = MbTilesStatus.Unset;

    // ---- 内存缓存（含 LRU 顺序） ----
    private readonly Dictionary<string, Microsoft.Maui.Graphics.IImage> _images = new();
    private readonly List<string> _order = new();
    private readonly object _imageGate = new();

    // ---- 在飞 / 退避 ----
    private readonly HashSet<string> _inFlight = new();
    private readonly Dictionary<string, (int Fails, DateTime NextTry)> _backoff = new();
    private readonly object _flightGate = new();

    // ---- 并发闸：按来源各一个（高德 6，OSM 2） ----
    private readonly Dictionary<string, SemaphoreSlim> _sourceGates = new();
    private readonly object _gateLock = new();

    private readonly CancellationTokenSource _cts = new();
    private readonly object _bytesGate = new();
    private long _bytes = -1;            // -1 = 还没数过
    private int _pruning;

    /// <summary>一块瓦片到货（或确认失败）：VM 拿它触发一次重绘。</summary>
    public event Action? TileArrived;

    public TileService(StorageProvider store, SettingsManager settings)
    {
        _settings = settings;
        _root = Path.Combine(store.Root, "Cache", "tiles");
        _offlineRoot = Path.Combine(store.Root, "Offline");
        try { Directory.CreateDirectory(_root); }
        catch (Exception ex) { App.WriteLog("TileService.Root -> " + ex.Message); }
        // 上次选的离线包：开机就接着用，不必等人再进设置页点一次。
        ConfigureMbTiles(settings.Current.MapTileMbTilesPath);
    }

    public string Root => _root;

    /// <summary>导入的 .mbtiles 都放这里：手机上的 FilePicker 只给缓存副本，重启后可能就没了。</summary>
    public string OfflineRoot => _offlineRoot;

    /// <summary>设置里的联网总闸；关掉后这个服务只读缓存。</summary>
    public bool OnlineEnabled => _settings.Current.MapTileOnline;

    /// <summary>内存里现在有多少张；状态条用它。</summary>
    public int MemoryCount
    {
        get { lock (_imageGate) return _images.Count; }
    }

    // ==================== MBTiles 离线包 ====================

    /// <summary>当前离线包的状态（没选 / 文件不在 / 读不出 / 就绪），设置页与状态条都读它。</summary>
    public MbTilesStatus OfflinePackage
    {
        get
        {
            lock (_mbGate)
                return _mbTiles?.Snapshot() ?? _mbStatus;
        }
    }

    public string MbTilesPath
    {
        get { lock (_mbGate) return _mbTilesPath; }
    }

    /// <summary>换离线包（空串 = 取消）。同步返回只是「开始换」，打开结果看 <see cref="MbTilesStatus"/>。</summary>
    public void ConfigureMbTiles(string? path) => _ = ConfigureMbTilesAsync(path);

    /// <summary>
    /// 换包：旧连接先关，旧图片缓存（mbtiles/ 前缀）一起清掉 —— 否则同一个 z/x/y 会画出上一个包的地图。
    /// 打开过程异步，UI 线程只负责把状态行刷新一次。
    /// </summary>
    public async Task<MbTilesStatus> ConfigureMbTilesAsync(string? path)
    {
        var next = (path ?? "").Trim();
        MbTilesSource? previous;
        lock (_mbGate)
        {
            if (string.Equals(next, _mbTilesPath, StringComparison.Ordinal) && (_mbTiles is not null || next.Length == 0))
                return _mbTiles?.Snapshot() ?? _mbStatus;
            previous = _mbTiles;
            _mbTiles = null;
            _mbTilesPath = next;
            _mbStatus = next.Length == 0
                ? MbTilesStatus.Unset
                : new MbTilesStatus(MbTilesState.NotConfigured, "正在打开离线包…", next, null, 0, 0, 0, 0);
        }

        previous?.Dispose();
        DropImages(MapTiles.MbTilesId + "/");
        lock (_flightGate)
        {
            foreach (var key in new List<string>(_backoff.Keys))
                if (key.StartsWith(MapTiles.MbTilesId + "/", StringComparison.Ordinal)) _backoff.Remove(key);
        }

        if (next.Length == 0)
        {
            TileArrived?.Invoke();
            return OfflinePackage;
        }

        var source = new MbTilesSource(next);
        var status = await source.OpenAsync(_cts.Token).ConfigureAwait(false);
        var current = false;
        lock (_mbGate)
        {
            if (string.Equals(_mbTilesPath, next, StringComparison.Ordinal))
            {
                _mbTiles = source;
                _mbStatus = status;
                current = true;
            }
        }
        if (!current) source.Dispose();
        TileArrived?.Invoke();
        return status;
    }

    /// <summary>
    /// 从文件选择器拿到的路径：先验一遍，再复制进应用自己的 Offline 目录（已在里面就不复制），
    /// 最后切换并等它打开。返回的 Status 就是设置页要显示的那一行。
    /// </summary>
    public async Task<MbTilesStatus> ImportMbTilesAsync(string pickedPath)
    {
        var picked = (pickedPath ?? "").Trim();
        var (state, message) = MbTilesInfo.CheckPath(picked, picked.Length > 0 && File.Exists(picked));
        if (state != MbTilesPathState.Ok)
        {
            var failed = new MbTilesStatus(
                state == MbTilesPathState.Missing ? MbTilesState.Missing : MbTilesState.Unreadable,
                message, picked, null, 0, 0, 0, 0);
            lock (_mbGate) _mbStatus = failed;
            TileArrived?.Invoke();
            return failed;
        }

        try
        {
            Directory.CreateDirectory(_offlineRoot);
            var name = SanitizeSource(Path.GetFileNameWithoutExtension(picked)) + MbTilesInfo.Extension;
            var target = Path.Combine(_offlineRoot, name);
            var same = string.Equals(Path.GetFullPath(picked), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase);
            if (!same)
                await Task.Run(() => File.Copy(picked, target, overwrite: true), _cts.Token).ConfigureAwait(false);
            return await ConfigureMbTilesAsync(target).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return OfflinePackage;
        }
        catch (Exception ex)
        {
            App.WriteLog("TileService.ImportMbTiles -> " + ex.Message);
            var failed = new MbTilesStatus(
                MbTilesState.Unreadable, $"离线包导入失败：{ex.Message}", picked, null, 0, 0, 0, 0);
            lock (_mbGate) _mbStatus = failed;
            TileArrived?.Invoke();
            return failed;
        }
    }

    /// <summary>换包时把上一个包的解码图整批扔掉（键前缀匹配，只动 mbtiles/ 那一撮）。</summary>
    private void DropImages(string prefix)
    {
        lock (_imageGate)
        {
            var victims = new List<string>();
            foreach (var key in _images.Keys)
                if (key.StartsWith(prefix, StringComparison.Ordinal)) victims.Add(key);
            foreach (var key in victims)
            {
                if (_images.Remove(key, out var img)) (img as IDisposable)?.Dispose();
                _order.Remove(key);
            }
        }
    }

    /// <summary>失败数：还在退避里的块数；状态行读它。</summary>
    public int FailureCount
    {
        get
        {
            var now = DateTime.UtcNow;
            lock (_flightGate)
            {
                var n = 0;
                foreach (var v in _backoff.Values) if (v.NextTry > now) n++;
                return n;
            }
        }
    }

    /// <summary>最长还要退避几秒：状态行读它，0 = 没有在等的块。</summary>
    public int BackoffSeconds
    {
        get
        {
            var now = DateTime.UtcNow;
            lock (_flightGate)
            {
                var max = TimeSpan.Zero;
                foreach (var v in _backoff.Values)
                {
                    var left = v.NextTry - now;
                    if (left > max) max = left;
                }
                return (int)Math.Ceiling(Math.Max(0, max.TotalSeconds));
            }
        }
    }

    // ==================== 读 ====================

    /// <summary>内存里有就直接给；没有就返回 null 并在后台排一次取图。UI 线程上随便调。</summary>
    public Microsoft.Maui.Graphics.IImage? Peek(MapTileSource source, int z, int x, int y)
    {
        var key = KeyOf(source.Id, z, x, y);
        lock (_imageGate)
        {
            if (!_images.TryGetValue(key, out var img)) return null;
            TouchLocked(key);
            return img;
        }
    }

    /// <summary>
    /// 顶到上限才值得记这次「用过」：没压力时 IndexOf 是白花的。
    /// 一屏上百块瓦片、每帧各来一次，这里省下的是每帧几万次字符串比较。
    /// </summary>
    private void TouchLocked(string key)
    {
        if (_order.Count < MemoryCap) return;
        var i = _order.IndexOf(key);
        if (i < 0 || i == _order.Count - 1) return;
        _order.RemoveAt(i);
        _order.Add(key);
    }

    /// <summary>
    /// 只查内存、不动 LRU：预取每帧要问几十上百块，不该让「问一下」把整个缓存排一遍序。
    /// </summary>
    public bool Cached(MapTileSource source, int z, int x, int y)
    {
        lock (_imageGate) return _images.ContainsKey(KeyOf(source.Id, z, x, y));
    }

    /// <summary>要一块瓦片：内存/磁盘/网络按顺序找。重复请求与退避期内的请求直接忽略。</summary>
    public void Request(MapTileSource source, int z, int x, int y)
    {
        var key = KeyOf(source.Id, z, x, y);
        lock (_flightGate)
        {
            if (_inFlight.Contains(key)) return;
            if (_backoff.TryGetValue(key, out var b) && b.NextTry > DateTime.UtcNow) return;
            _inFlight.Add(key);
        }
        _ = LoadAsync(source, z, x, y, key);
    }

    private async Task LoadAsync(MapTileSource source, int z, int x, int y, string key)
    {
        byte[]? bytes = null;
        try
        {
            if (source.IsMbTiles)
            {
                // 离线包不走磁盘缓存目录、也不发请求：文件本身已经是完整的一份。
                MbTilesSource? pack;
                lock (_mbGate) pack = _mbTiles;
                // 包外的档位直接当「没有」，不必去查一张注定空的表。
                if (pack is { } p && (p.Snapshot().Meta?.Covers(z) ?? true))
                    bytes = await p.GetTileAsync(z, x, y, _cts.Token).ConfigureAwait(false);
                if (bytes is null)
                {
                    lock (_flightGate) _backoff[key] = NextBackoff(key);
                    return;
                }
            }
            else
            {
                bytes = await ReadDiskAsync(source.Id, z, x, y);
                if (bytes is null)
                {
                    if (!OnlineEnabled || !source.IsOnline)
                    {
                        // 离线：这次没有就是没有。记 30 秒退避，免得画布每帧都去 stat 一次文件。
                        lock (_flightGate) _backoff[key] = (1, DateTime.UtcNow.AddSeconds(30));
                        return;
                    }
                    bytes = await FetchAsync(source, z, x, y);
                    if (bytes is null)
                    {
                        lock (_flightGate) _backoff[key] = NextBackoff(key);
                        return;
                    }
                    await WriteDiskAsync(source.Id, z, x, y, bytes);
                }
            }

            var img = await DecodeAsync(bytes);
            lock (_flightGate) _backoff.Remove(key);
            if (img is null) return;
            lock (_imageGate)
            {
                _images[key] = img;
                _order.Remove(key);
                _order.Add(key);
                while (_order.Count > MemoryCap)
                {
                    var drop = _order[0];
                    _order.RemoveAt(0);
                    if (_images.Remove(drop, out var victim)) (victim as IDisposable)?.Dispose();
                }
            }
            TileArrived?.Invoke();
        }
        catch (Exception ex)
        {
            App.WriteLog($"TileService.Load({source.Id}/{z}/{x}/{y}) -> {ex.GetType().Name}: {ex.Message}");
            lock (_flightGate) _backoff[key] = NextBackoff(key);
        }
        finally
        {
            lock (_flightGate) _inFlight.Remove(key);
        }
    }

    /// <summary>失败退避：1s、2s、4s……封顶 60s。404 是大面积没有瓦片，退 30 分钟。</summary>
    private (int, DateTime) NextBackoff(string key)
    {
        var fails = _backoff.TryGetValue(key, out var old) ? old.Fails + 1 : 1;
        var waitSeconds = Math.Min(60, Math.Pow(2, Math.Min(6, fails - 1)));
        return (fails, DateTime.UtcNow.AddSeconds(waitSeconds));
    }

    private (int, DateTime) NextBackoffPermanent()
    {
        return (1, DateTime.UtcNow.AddMinutes(30));
    }

    // ==================== 网络 ====================

    private async Task<byte[]?> FetchAsync(MapTileSource source, int z, int x, int y)
    {
        var url = source.BuildUrl(z, x, y);
        if (url.Length == 0) return null;

        var gate = GateFor(source);
        await gate.WaitAsync(_cts.Token).ConfigureAwait(false);
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.TryAddWithoutValidation("User-Agent", source.UserAgent);
            if (source.Crs == MapTileCrs.Gcj02)
            {
                // 高德对没有来源页的直连请求更挑剔，带上自家站点，别装成浏览器。
                req.Headers.TryAddWithoutValidation("Referer", "https://www.amap.com/");
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(8));
            using var resp = await SharedHttp.Value.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                .ConfigureAwait(false);

            if (resp.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.Forbidden)
            {
                lock (_flightGate) _backoff[KeyOf(source.Id, z, x, y)] = NextBackoffPermanent();
                return null;
            }
            if (!resp.IsSuccessStatusCode) return null;

            var type = resp.Content.Headers.ContentType?.MediaType ?? "";
            if (type.Length > 0 && !type.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) return null;

            return await resp.Content.ReadAsByteArrayAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            App.WriteLog($"TileService.Fetch({source.Id}/{z}/{x}/{y}) -> {ex.Message}");
            return null;
        }
        finally
        {
            gate.Release();
        }
    }

    private SemaphoreSlim GateFor(MapTileSource source)
    {
        lock (_gateLock)
        {
            if (_sourceGates.TryGetValue(source.Id, out var g)) return g;
            return _sourceGates[source.Id] = new SemaphoreSlim(Math.Clamp(source.MaxConcurrency, 1, 6));
        }
    }

    // ==================== 磁盘（LRU） ====================

    private string PathOf(string sourceId, int z, int x, int y) =>
        Path.Combine(_root, SanitizeSource(sourceId), z.ToString(), x.ToString(), y + ".png");

    /// <summary>来源 id 是写死的常量，但拼路径前再滤一遍：缓存目录不该能被存档里的字符串指到别处去。</summary>
    private static string SanitizeSource(string sourceId)
    {
        var chars = new char[sourceId.Length];
        var n = 0;
        foreach (var c in sourceId)
            if (char.IsLetterOrDigit(c) || c is '-' or '_') chars[n++] = c;
        return n == 0 ? "misc" : new string(chars, 0, n);
    }

    private async Task<byte[]?> ReadDiskAsync(string sourceId, int z, int x, int y)
    {
        var path = PathOf(sourceId, z, x, y);
        try
        {
            var bytes = await Task.Run(() =>
            {
                if (!File.Exists(path)) return (byte[]?)null;
                return File.ReadAllBytes(path);
            }, _cts.Token).ConfigureAwait(false);
            if (bytes is null) return null;
            // LRU 的「最近用过」：文件时间戳就是最省事的账本，十分钟内不重复写同一张。
            try
            {
                if (DateTime.UtcNow - File.GetLastAccessTimeUtc(path) > TimeSpan.FromMinutes(10))
                    File.SetLastAccessTimeUtc(path, DateTime.UtcNow);
            }
            catch { /* 文件系统不记账就算了，别为它丢帧 */ }
            return bytes;
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            App.WriteLog($"TileService.ReadDisk({path}) -> {ex.Message}");
            return null;
        }
    }

    private async Task WriteDiskAsync(string sourceId, int z, int x, int y, byte[] bytes)
    {
        var path = PathOf(sourceId, z, x, y);
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (dir is not null) await Task.Run(() => Directory.CreateDirectory(dir), _cts.Token).ConfigureAwait(false);
            // 先写临时文件再换名：半截写坏的文件会被当成「有瓦片」，画出来是一条灰带。
            var tmp = path + ".part";
            await Task.Run(() => File.WriteAllBytes(tmp, bytes), _cts.Token).ConfigureAwait(false);
            await Task.Run(() => File.Move(tmp, path, overwrite: true), _cts.Token).ConfigureAwait(false);
            lock (_bytesGate)
            {
                if (_bytes >= 0) _bytes += bytes.Length;
            }
            MaybePrune();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            App.WriteLog($"TileService.WriteDisk({path}) -> {ex.Message}");
        }
    }

    private async Task<Microsoft.Maui.Graphics.IImage?> DecodeAsync(byte[] bytes)
    {
        try
        {
            return await Task.Run(() =>
            {
                using var ms = new MemoryStream(bytes);
                return (Microsoft.Maui.Graphics.IImage?)Microsoft.Maui.Graphics.Platform.PlatformImage.FromStream(ms);
            }, _cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            App.WriteLog("TileService.Decode -> " + ex.Message);
            return null;
        }
    }

    /// <summary>磁盘现在占多少：状态条与设置页用，值缓存住了，不每帧扫目录。</summary>
    public long DiskBytes
    {
        get
        {
            lock (_bytesGate)
            {
                if (_bytes >= 0) return _bytes;
                _bytes = -2;            // -2 = 正在数
            }
            _ = Task.Run(() =>
            {
                long total = 0;
                try
                {
                    foreach (var f in EnumerateTiles())
                    {
                        try { total += new FileInfo(f).Length; } catch { }
                    }
                }
                catch (Exception ex) { App.WriteLog("TileService.DiskBytes -> " + ex.Message); }
                lock (_bytesGate) _bytes = total;
                TileArrived?.Invoke();
            });
            return 0;
        }
    }

    public int DiskFiles
    {
        get
        {
            try
            {
                var n = 0;
                foreach (var _ in EnumerateTiles()) n++;
                return n;
            }
            catch { return 0; }
        }
    }

    private IEnumerable<string> EnumerateTiles()
    {
        if (!Directory.Exists(_root)) return Array.Empty<string>();
        return Directory.EnumerateFiles(_root, "*.png", SearchOption.AllDirectories);
    }

    private void MaybePrune()
    {
        if (Interlocked.Exchange(ref _pruning, 1) == 1) return;
        _ = Task.Run(() =>
        {
            try { Prune(); }
            catch (Exception ex) { App.WriteLog("TileService.Prune -> " + ex.Message); }
            finally { Interlocked.Exchange(ref _pruning, 0); }
        });
    }

    /// <summary>超预算就按「最久没读过」删到 90%，一次删完，别每写一张就扫一遍。</summary>
    private void Prune()
    {
        if (!Directory.Exists(_root)) return;
        var files = new List<(string Path, long Size, DateTime Used)>();
        long total = 0;
        foreach (var f in EnumerateTiles())
        {
            try
            {
                var info = new FileInfo(f);
                files.Add((f, info.Length, info.LastAccessTimeUtc));
                total += info.Length;
            }
            catch { }
        }
        lock (_bytesGate) _bytes = total;
        if (total <= CacheBudgetBytes) return;

        files.Sort((a, b) => a.Used.CompareTo(b.Used));
        var target = (long)(CacheBudgetBytes * 0.9);
        foreach (var f in files)
        {
            if (total <= target) break;
            try
            {
                File.Delete(f.Path);
                total -= f.Size;
            }
            catch { }
        }
        lock (_bytesGate) _bytes = total;
        App.WriteLog($"TileService.Prune -> 缓存收到 {total / 1024 / 1024}MB");
    }

    /// <summary>设置页「清空瓦片缓存」。</summary>
    public Task ClearAsync() => Task.Run(() =>
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
            Directory.CreateDirectory(_root);
            lock (_imageGate)
            {
                foreach (var img in _images.Values) (img as IDisposable)?.Dispose();
                _images.Clear();
                _order.Clear();
            }
            lock (_flightGate) _backoff.Clear();
            lock (_bytesGate) _bytes = 0;
        }
        catch (Exception ex) { App.WriteLog("TileService.Clear -> " + ex.Message); }
    });

    private static string KeyOf(string sourceId, int z, int x, int y) =>
        $"{sourceId}/{z}/{x}/{y}";

    public void Dispose()
    {
        try { _cts.Cancel(); } catch { }
        MbTilesSource? pack;
        lock (_mbGate) { pack = _mbTiles; _mbTiles = null; }
        pack?.Dispose();
        lock (_imageGate)
        {
            foreach (var img in _images.Values) (img as IDisposable)?.Dispose();
            _images.Clear();
            _order.Clear();
        }
        lock (_gateLock)
        {
            foreach (var g in _sourceGates.Values) g.Dispose();
            _sourceGates.Clear();
        }
        _cts.Dispose();
    }
}
