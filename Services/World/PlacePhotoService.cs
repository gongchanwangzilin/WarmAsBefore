using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using WarmAsBefore.Models;

namespace WarmAsBefore.Services;

/// <summary>一次「从谷歌地图补地点照片」的结算：取到几张、失败几张、一共试了几张，外加一句人话。</summary>
public sealed record PlacePhotoBatchResult(int Done, int Failed, int Total, string? Message = null)
{
    public bool AllOk => Failed == 0 && Done > 0;
}

/// <summary>
/// 地点照片：从 Google Places 给导进来的真实城市地图补场景背景。
///
/// 为什么是谷歌而不是高德：高德只给地名、道路与交通，**没有地点照片接口**；
/// 谷歌 Places 有 Text Search → photo_reference → Place Photo 这一条链，
/// 而且同一个地点没照片时还能退到 Street View Static 拍一张街面。
///
/// 几点约束：
/// · 全程纯 REST（不引 SDK）：两个 GET 就够，安卓端不必多揣一坨库。
/// · 图片响应按字节封顶（12MB）：天知道那张原图多大，接进内存再 OOM 不如当场跳过。
/// · 国内坐标要先转：高德导入的是 GCJ-02，谷歌要 WGS84（见 <see cref="CoordConvert"/>）。
/// · 没填 Key 直接返回一句人话，不发请求、不抛异常 —— 用户可能只是想先看看这里能干什么。
/// · 下载下来的照片只落在本机 {数据目录}/maps/photos/ 里，与用户自己拍的照片同一套目录约定。
/// </summary>
public sealed class PlacePhotoService
{
    /// <summary>一批最多取几张：一张照片就是一次联网 + 一次落盘，城市级地图几百个场景不能一口气跑完。</summary>
    public const int MaxBatch = 12;

    /// <summary>Google Maps API Key 落在 Preferences 里（与高德 Key 同一套做法），也认环境变量方便开发。</summary>
    private const string KeyPref = "map.google.key";
    private const string EnvName = "WAB_GOOGLE_KEY";

    private const string TextSearchEndpoint = "https://maps.googleapis.com/maps/api/place/textsearch/json";
    private const string PhotoEndpoint = "https://maps.googleapis.com/maps/api/place/photo";
    private const string StreetViewEndpoint = "https://maps.googleapis.com/maps/api/streetview";

    /// <summary>单张图片的字节上限：再大就不是「取个背景」，而是把整张原图往手机内存里塞。</summary>
    private const long MaxImageBytes = 12L * 1024 * 1024;

    private readonly MapService _maps;
    private readonly StorageProvider _store;

    /// <summary>共用一条连接池（与 MapImportService 同一做法）：两次重定向也算在同一个池里。</summary>
    private static readonly HttpClient Net = new(new SocketsHttpHandler
    {
        AutomaticDecompression = DecompressionMethods.All,
        ConnectTimeout = TimeSpan.FromSeconds(10),
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        MaxConnectionsPerServer = 2,
        AllowAutoRedirect = true,   // Place Photo 会 302 到实际图片地址，必须自动跟随
    })
    { Timeout = TimeSpan.FromSeconds(30) };

    public PlacePhotoService(MapService maps, StorageProvider store)
    {
        _maps = maps;
        _store = store;
    }

    // ==================== Key ====================

    /// <summary>
    /// Google Maps API Key。也认环境变量 WAB_GOOGLE_KEY，方便开发与桌面端手工试；
    /// 设置页还没有这一栏，所以先放地图创建页顶部那条「存 Key」，落在 Preferences。
    /// </summary>
    public string ApiKey
    {
        get => ReadKey();
        set
        {
            try { Microsoft.Maui.Storage.Preferences.Default.Set(KeyPref, (value ?? "").Trim()); }
            catch (Exception ex) { App.WriteLog("PlacePhotoService: Google key 没存下 —— " + ex.Message); }
        }
    }

    public bool HasKey => !string.IsNullOrWhiteSpace(ApiKey);

    /// <summary>给 UI 的两行人话：有 Key 该期待什么、没 Key 能干什么。</summary>
    public string KeyStateText => HasKey
        ? "已填 Key：会先按地点名在谷歌 Places 里找照片，找不到再退到街景；下载的照片只存在本机地图目录。"
        : "还没填 Key：高德没有地点照片接口，谷歌 Places 有 —— 填一个启用了 Places API 的 Google Maps API Key 才能取照片"
          + "（想让没照片的地点退到街景，还要启用 Street View Static API）。";

    private static string ReadKey()
    {
        var env = Environment.GetEnvironmentVariable(EnvName);
        if (!string.IsNullOrWhiteSpace(env)) return env.Trim();
        try { return Microsoft.Maui.Storage.Preferences.Default.Get(KeyPref, "") ?? ""; }
        catch (Exception ex)
        {
            App.WriteLog("PlacePhotoService: Google key 读不到 —— " + ex.Message);
            return "";
        }
    }

    // ==================== 批量取照片 ====================

    /// <summary>
    /// 给「还缺背景」的场景逐个从谷歌取照片，最多 <see cref="MaxBatch"/> 张，全程可取消。
    /// 每张之间检查一次 ct：单张请求本身没法掐断，但至少不会在用户点了取消之后还继续取。
    /// </summary>
    public async Task<PlacePhotoBatchResult> FillMissingFromGoogleAsync(MapDefinition map, int limit,
        IProgress<string>? progress, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(map);
        var key = ApiKey;
        if (string.IsNullOrWhiteSpace(key))
            return new PlacePhotoBatchResult(0, 0, 0,
                "还没填 Google Maps API Key：高德没有地点照片接口，谷歌 Places 有，填上 Key 才能取照片。");

        var targets = map.AllScenes
            .Where(s => string.IsNullOrWhiteSpace(s.Background))
            .Take(Math.Clamp(limit, 1, MaxBatch))
            .ToList();
        if (targets.Count == 0)
            return new PlacePhotoBatchResult(0, 0, 0, "这张图的场景都已经有了背景。");

        App.WriteLog($"PlacePhotoService: 开始补地点照片，缺 {map.AllScenes.Count(s => string.IsNullOrWhiteSpace(s.Background))} 个，本次取 {targets.Count} 张");

        var done = 0;
        var failed = 0;
        var changed = false;
        string? firstError = null;

        foreach (var scene in targets)
        {
            ct.ThrowIfCancellationRequested();
            var owner = map.LocationOfScene(scene.Id);
            var label = FirstNonEmpty(owner?.RealName, owner?.Name, scene.Name);
            progress?.Report($"正在取 {done + failed + 1}/{targets.Count}：{label}");

            var r = await FillOneAsync(map, scene, owner, key, ct).ConfigureAwait(false);
            if (r.Ok && r.RelPath is { Length: > 0 } rel)
            {
                scene.Background = rel;
                // 换图 = 旧标定作废：落脚点是绑在「哪一张图」上的，换一张就不成立了。
                scene.Placement = null;
                map.Photos.Add(new ScenePhoto
                {
                    RelPath = rel,
                    // 来源照片先记「未绑」暂存态：它不像用户自己拍的能当场知道是哪条街、哪户。
                    Binding = "",
                    Source = "google",
                    Note = label,
                    CreatedAt = DateTime.Now,
                    Order = NextPhotoOrder(map),
                });
                done++;
                changed = true;
            }
            else
            {
                failed++;
                firstError ??= r.Message;
                App.WriteLog($"PlacePhotoService -> 「{label}」没取到：{r.Message}");
                // Key 被拒 / 额度用完 / 连不上谷歌：后面每一张都会是同一句话，直接收手。
                if (r.Fatal) break;
            }
        }

        if (changed)
        {
            await _maps.SaveAsync().ConfigureAwait(false);
            _maps.NotifyChanged();
        }

        var note = failed == 0
            ? $"从谷歌地图取好了 {done} 张照片。"
            : done > 0
                ? $"取好 {done} 张，失败 {failed} 张：{firstError}"
                : $"一张也没取到：{firstError}";
        return new PlacePhotoBatchResult(done, failed, targets.Count, note);
    }

    /// <summary>单张的结算：成功带相对路径；Fatal = 再试下一张也是同一句话（Key 被拒、额度、断网）。</summary>
    private sealed record OneResult(bool Ok, string Message, bool Fatal, string? RelPath);

    /// <summary>
    /// 给一个场景取一张：Text Search 找地点 → photo_reference 下载照片；
    /// 没照片就退到街景。返回的 RelPath 由调用方挂到场景上并记账。
    /// </summary>
    private async Task<OneResult> FillOneAsync(MapDefinition map, MapScene scene, MapLocation? owner,
        string key, CancellationToken ct)
    {
        var label = FirstNonEmpty(owner?.RealName, owner?.Name, scene.Name);
        try
        {
            var query = BuildQuery(map, scene, owner);
            var (lat, lon, hasCoord) = Locate(map, owner);
            // 高德导入的坐标是 GCJ-02，谷歌用 WGS84：先转再问，否则搜到的是隔壁那条街。
            if (hasCoord && string.Equals(map.Source, "amap", StringComparison.Ordinal))
                (lat, lon) = CoordConvert.Gcj02ToWgs84(lat, lon);

            var text = await TextSearchAsync(key, query, lat, lon, hasCoord, ct).ConfigureAwait(false);
            if (text.Fatal) return new OneResult(false, text.Message, true, null);

            if (text.PhotoReference.Length > 0)
            {
                var img = await DownloadPlacePhotoAsync(key, text.PhotoReference, ct).ConfigureAwait(false);
                if (img is not null)
                {
                    var rel = SaveImage(img.Bytes, scene.Id, img.Ext);
                    if (rel is not null)
                    {
                        App.WriteLog($"PlacePhotoService -> 「{label}」取到 Places 照片：{rel}");
                        return new OneResult(true, "", false, rel);
                    }
                }
                // 照片链接失效（photo_reference 会过期）不致命：继续试街景。
            }

            if (hasCoord)
            {
                var sv = await DownloadStreetViewAsync(key, lat, lon, ct).ConfigureAwait(false);
                if (sv is not null)
                {
                    var rel = SaveImage(sv.Bytes, scene.Id, sv.Ext);
                    if (rel is not null)
                    {
                        App.WriteLog($"PlacePhotoService -> 「{label}」取到街景：{rel}");
                        return new OneResult(true, "", false, rel);
                    }
                }
            }

            var why = text.Status switch
            {
                "ZERO_RESULTS" => $"谷歌上没找到「{label}」这个地点，也没有街景。",
                "OK" => $"谷歌上「{label}」没有照片，也没有街景。",
                _ => text.Message,
            };
            return new OneResult(false, why, false, null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            return new OneResult(false, "连不上谷歌地图：" + Brief(ex.Message) + "（国内直连谷歌一般要代理，检查一下网络。）", true, null);
        }
        catch (TaskCanceledException)
        {
            return new OneResult(false, "谷歌那边 30 秒没回话（超时），过一会儿再试。", true, null);
        }
        catch (Exception ex)
        {
            App.WriteLog($"PlacePhotoService -> 「{label}」出错：{ex}");
            return new OneResult(false, "取照片时出错：" + Brief(ex.Message), false, null);
        }
    }

    /// <summary>
    /// 检索用的关键词：真实店名优先，其次地图上的显示名，再补门牌与城市。
    /// 地点名往往被用户改过（「家」），所以 RealName 比 Name 值钱。
    /// </summary>
    private static string BuildQuery(MapDefinition map, MapScene scene, MapLocation? owner)
    {
        var parts = new List<string>();
        void Add(string? v)
        {
            v = (v ?? "").Trim();
            if (v.Length > 0 && !parts.Contains(v)) parts.Add(v);
        }
        Add(owner?.RealName);
        Add(owner?.Name);
        Add(scene.Name);
        Add(owner?.Address);
        Add(map.City);
        return parts.Count > 0 ? string.Join(" ", parts) : FirstNonEmpty(map.City, map.Name, "地点");
    }

    /// <summary>查询中心点：地点自己的坐标优先，其次整图中心；0,0 当没有（大西洋上没有我们的地）。</summary>
    private static (double Lat, double Lon, bool Ok) Locate(MapDefinition map, MapLocation? owner)
    {
        if (owner is not null && HasCoordinate(owner.Latitude, owner.Longitude))
            return (owner.Latitude, owner.Longitude, true);
        if (HasCoordinate(map.CenterLatitude, map.CenterLongitude))
            return (map.CenterLatitude, map.CenterLongitude, true);
        return (0, 0, false);
    }

    /// <summary>有效坐标：两轴都不能是 0（0,0 是「没填」的默认值，不是真的在几内亚湾）。</summary>
    public static bool HasCoordinate(double lat, double lon) =>
        Math.Abs(lat) > 1e-6 && Math.Abs(lon) > 1e-6
        && lat is >= -90 and <= 90 && lon is >= -180 and <= 180;

    // ==================== 请求 ====================

    private sealed record TextSearchOutcome(string Status, string Message, string PhotoReference, bool Fatal);

    /// <summary>Text Search：有坐标就带 location + radius 3 公里，让结果落在这一片。</summary>
    private static async Task<TextSearchOutcome> TextSearchAsync(string key, string query,
        double lat, double lon, bool hasCoord, CancellationToken ct)
    {
        var url = new StringBuilder(TextSearchEndpoint);
        url.Append("?query=").Append(Uri.EscapeDataString(query));
        if (hasCoord) url.Append("&location=").Append(N(lat)).Append(',').Append(N(lon)).Append("&radius=3000");
        url.Append("&language=zh-CN&key=").Append(Uri.EscapeDataString(key));

        using var resp = await Net.GetAsync(url.ToString(), ct).ConfigureAwait(false);
        var json = await ReadCappedTextAsync(resp, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            return new TextSearchOutcome("HTTP", "谷歌 Places 返回 HTTP " + (int)resp.StatusCode + "：" + Brief(json), "", false);
        return ParseTextSearch(json);
    }

    private static TextSearchOutcome ParseTextSearch(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var status = GetStr(root, "status");
            var error = GetStr(root, "error_message");
            // Key 被拒 / 额度用完：后面的每一处都会是同一句，批量要立即收手。
            var fatal = status is "REQUEST_DENIED" or "OVER_QUERY_LIMIT";
            var message = DescribeStatus(status, error);

            var photo = "";
            if (status == "OK"
                && root.TryGetProperty("results", out var results) && results.ValueKind == JsonValueKind.Array
                && results.GetArrayLength() > 0
                && results[0].TryGetProperty("photos", out var photos) && photos.ValueKind == JsonValueKind.Array
                && photos.GetArrayLength() > 0
                && photos[0].TryGetProperty("photo_reference", out var pr) && pr.ValueKind == JsonValueKind.String)
                photo = pr.GetString() ?? "";

            return new TextSearchOutcome(status, message, photo, fatal);
        }
        catch (Exception ex)
        {
            App.WriteLog("PlacePhotoService: Places 返回读不懂 —— " + ex.Message);
            return new TextSearchOutcome("BAD_JSON", "谷歌 Places 的返回读不懂（可能被网络中间层改过）。", "", false);
        }
    }

    /// <summary>把 Places 的 status 翻成人话。谷歌的 status 是字符串码，光看码用户不知道下一步做什么。</summary>
    private static string DescribeStatus(string status, string error)
    {
        var tail = string.IsNullOrWhiteSpace(error) ? "" : "（谷歌原话：" + Brief(error) + "）";
        return status switch
        {
            "OK" => "",
            "REQUEST_DENIED" => "谷歌拒绝了这次请求（REQUEST_DENIED）：Key 不对、没启用 Places API，或这个 Key 设了 IP / 应用限制。" + tail,
            "OVER_QUERY_LIMIT" => "谷歌的额度用完了或请求太快（OVER_QUERY_LIMIT）：等一会儿再试，或换一个 Key。" + tail,
            "INVALID_REQUEST" => "谷歌说这次请求参数不对（INVALID_REQUEST），这一处跳过。" + tail,
            "ZERO_RESULTS" => "谷歌上没找到这个地点（ZERO_RESULTS）。" + tail,
            "UNKNOWN_ERROR" => "谷歌那边暂时出错（UNKNOWN_ERROR），过一会儿再试。" + tail,
            _ => "谷歌返回了没见过的状态：" + (status.Length == 0 ? "空" : status) + "。" + tail,
        };
    }

    private sealed record DownloadedImage(byte[] Bytes, string Ext);

    /// <summary>Place Photo：maxwidth=1600 足够铺一屏背景；302 由 HttpClient 自动跟随。</summary>
    private static async Task<DownloadedImage?> DownloadPlacePhotoAsync(string key, string photoReference,
        CancellationToken ct)
    {
        var url = $"{PhotoEndpoint}?maxwidth=1600&photo_reference={Uri.EscapeDataString(photoReference)}"
                  + $"&key={Uri.EscapeDataString(key)}";
        return await DownloadImageAsync(url, ct).ConfigureAwait(false);
    }

    /// <summary>街景：地点没照片时按坐标拍一张 1600×1000、视野 80° 的街面。</summary>
    private static async Task<DownloadedImage?> DownloadStreetViewAsync(string key, double lat, double lon,
        CancellationToken ct)
    {
        var url = $"{StreetViewEndpoint}?size=1600x1000&location={N(lat)},{N(lon)}&fov=80"
                  + $"&key={Uri.EscapeDataString(key)}";
        return await DownloadImageAsync(url, ct).ConfigureAwait(false);
    }

    /// <summary>下载图片：成功码 + 确实是 image/* 才收，体积超上限直接跳（不把手机内存赌在那张图上）。</summary>
    private static async Task<DownloadedImage?> DownloadImageAsync(string url, CancellationToken ct)
    {
        using var resp = await Net.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        // 街景没有影像时返回 404；照片链接过期也是非 2xx —— 都当「这一处没有」，交给上层换下一招。
        if (!resp.IsSuccessStatusCode) return null;
        var media = resp.Content.Headers.ContentType?.MediaType;
        if (string.IsNullOrWhiteSpace(media) || !media.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            App.WriteLog($"PlacePhotoService: 返回的不是图片（{media ?? "没有 Content-Type"}），跳过");
            return null;
        }

        await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var ms = new MemoryStream();
        var buf = new byte[64 * 1024];
        long total = 0;
        int read;
        while ((read = await stream.ReadAsync(buf, ct).ConfigureAwait(false)) > 0)
        {
            total += read;
            if (total > MaxImageBytes)
            {
                App.WriteLog($"PlacePhotoService: 图片超过 {MaxImageBytes / 1024 / 1024}MB 上限，跳过");
                return null;
            }
            ms.Write(buf, 0, read);
        }
        if (ms.Length == 0) return null;
        return new DownloadedImage(ms.ToArray(), ExtFor(media));
    }

    private static string ExtFor(string? mediaType) => mediaType?.ToLowerInvariant() switch
    {
        "image/png" => ".png",
        "image/webp" => ".webp",
        "image/gif" => ".gif",
        _ => ".jpg",
    };

    // ==================== 落盘 ====================

    /// <summary>
    /// 写进 {数据目录}/maps/photos/（与 MapService.ImportPhotoFile 同一套目录约定），返回相对存储根路径。
    /// 文件名带 google_ 前缀 + 场景 Id：重取一次就原地覆盖，不会在 photos/ 里堆一串没用的旧图。
    /// </summary>
    private string? SaveImage(byte[] bytes, string sceneId, string ext)
    {
        try
        {
            var dir = Path.Combine(_store.Root, "maps", "photos");
            Directory.CreateDirectory(dir);
            var rel = $"maps/photos/google_{sceneId}{ext}";
            var abs = Path.Combine(_store.Root, rel.Replace('/', Path.DirectorySeparatorChar));
            File.WriteAllBytes(abs, bytes);
            return rel;
        }
        catch (Exception ex)
        {
            App.WriteLog("PlacePhotoService.SaveImage -> " + ex);
            return null;
        }
    }

    /// <summary>照片库里的拍摄顺序：新来的排在队尾。</summary>
    private static int NextPhotoOrder(MapDefinition map) =>
        map.Photos.Count == 0 ? 1 : map.Photos.Max(p => p.Order) + 1;

    // ==================== 小工具 ====================

    private static string N(double v) => v.ToString("0.######", CultureInfo.InvariantCulture);

    private static async Task<string> ReadCappedTextAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var ms = new MemoryStream();
        var buf = new byte[16 * 1024];
        long total = 0;
        int read;
        while ((read = await stream.ReadAsync(buf, ct).ConfigureAwait(false)) > 0)
        {
            total += read;
            // 检索结果是 JSON，几百 KB 顶天；超过 2MB 一律当异常响应，别继续吃内存。
            if (total > 2L * 1024 * 1024) throw new InvalidOperationException("谷歌返回的内容太大，已中止。");
            ms.Write(buf, 0, read);
        }
        return Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
    }

    private static string Brief(string? s)
    {
        s = (s ?? "").Trim();
        if (s.Length > 160) s = s[..160] + "…";
        return s.Length == 0 ? "对方没给原因" : s;
    }

    private static string FirstNonEmpty(params string?[] vs)
    {
        foreach (var v in vs) if (!string.IsNullOrWhiteSpace(v)) return v!.Trim();
        return "";
    }

    private static string GetStr(JsonElement el, string key) =>
        el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? (v.GetString() ?? "") : "";
}
