using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WarmAsBefore.Modules.Update;

/// <summary>
/// 自动更新器：轮询 GitHub releases（正式版 v* tag；开 BetaChannel 时含 *-beta），
/// 多镜像并发测速（各下载 256KB 样张测吞吐）选最快源下载，sha256 校验后
/// 解压到待启动目录，用户确认重启替换。
/// </summary>
public sealed class UpdateService
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(60) };
    private readonly string _repo = "gongchanwangzilin/WarmAsBefore";

    /// <summary>当前版本号（从 csproj ApplicationDisplayVersion 注入）。</summary>
    public string CurrentVersion { get; set; } = "1.3.0";

    /// <summary>是否测试计划：true 时 *-beta tag 也参与更新。</summary>
    public bool BetaChannel { get; set; }

    /// <summary>额外镜像前缀（如 "https://ghproxy.com/"），在 GitHub 官方源之后并发测速。</summary>
    public List<string> MirrorPrefixes { get; } = new()
    {
        "https://github.com/",   // 官方
        "https://mirror.ghproxy.com/",
        "https://ghproxy.com/"
    };

    public record UpdateInfo(
        string Tag, string Name, string ZipUrl, string Sha256,
        DateTime PublishedAt, bool IsBeta, string DownloadNote)
    {
        public bool IsNewerThan(string current) =>
            TrySemVer(Tag, out var t) && TrySemVer(current, out var c) &&
            VersionLess(t, c);
        private static bool VersionLess(Version a, Version b) => a > b;
        private static bool TrySemVer(string tag, out Version v)
        {
            var s = tag.TrimStart('v', 'V');
            var dot = s.IndexOf('.');
            if (dot > 0) s = s[..dot];   // 去掉 -beta 后缀
            return Version.TryParse(s, out v);
        }
    }

    public record MirrorSpeed(string Mirror, long Bytes, int Ms, double MibPerSec)
    {
        public override string ToString() => $"{Mirror} · {MibPerSec:0.##} MiB/s";
    }

    public sealed record CheckResult(UpdateInfo? Latest, List<MirrorSpeed> Speeds, string Error, string CurrentVersion)
    {
        public bool HasUpdate => Latest is not null && Latest.IsNewerThan(CurrentVersion);
    }

    /// <summary>检查最新 release（可选 beta）。</summary>
    public async Task<CheckResult> CheckAsync()
    {
        try
        {
            var resp = await _http.GetAsync($"https://api.github.com/repos/{_repo}/releases");
            if (!resp.IsSuccessStatusCode)
                return new CheckResult(null, new List<MirrorSpeed>(), $"GitHub 返回 {(int)resp.StatusCode}", CurrentVersion);
            var json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var releases = doc.RootElement.EnumerateArray();
            UpdateInfo? best = null;
            foreach (var r in releases)
            {
                if (r.TryGetProperty("prerelease", out var pre) && pre.GetBoolean())
                    continue;   // 正式版路径忽略 prerelease
                var tag = r.GetProperty("tag_name").GetString() ?? "";
                var isBeta = tag.EndsWith("-beta", StringComparison.OrdinalIgnoreCase);
                if (isBeta && !BetaChannel) continue;
                // 找 win-x64 zip 资产
                string? zipUrl = null, sha = null;
                foreach (var a in r.GetProperty("assets").EnumerateArray())
                {
                    var name = a.GetProperty("name").GetString() ?? "";
                    if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                        && name.Contains("win-x64", StringComparison.OrdinalIgnoreCase))
                        zipUrl = a.GetProperty("browser_download_url").GetString();
                    else if (name.EndsWith(".zip.sha256", StringComparison.OrdinalIgnoreCase)
                        || name.Contains(".sha256", StringComparison.OrdinalIgnoreCase))
                        sha = a.GetProperty("browser_download_url").GetString();
                }
                if (zipUrl is null) continue;
                var info = new UpdateInfo(
                    Tag: tag,
                    Name: r.GetProperty("name").GetString() ?? tag,
                    ZipUrl: zipUrl,
                    Sha256: sha ?? "",
                    PublishedAt: r.TryGetProperty("published_at", out var pub) ? DateTime.Parse(pub.GetString() ?? "") : default,
                    IsBeta: isBeta,
                    DownloadNote: isBeta ? "测试版（测试计划）" : "正式版");
                if (best is null || info.IsNewerThan(best.Tag)) best = info;
            }
            return new CheckResult(best, new List<MirrorSpeed>(), "", CurrentVersion);
        }
        catch (Exception ex)
        {
            return new CheckResult(null, new List<MirrorSpeed>(), ex.Message, CurrentVersion);
        }
    }

    /// <summary>
    /// 多镜像并发测速：每个前缀下载 256KB 样张，返回按吞吐排序。
    /// 调用方取 [0] 作为最快源，拼接 {mirrorPrefix}{repo}/releases/download/{tag}/{asset}。
    /// </summary>
    public async Task<List<MirrorSpeed>> SpeedTestAsync(string zipUrl)
    {
        var host = new Uri(zipUrl).Host;
        var samplePath = new Uri(zipUrl).AbsoluteUri.Replace(
            $"https://{host}", "https://github.com");
        var results = await Task.WhenAll(MirrorPrefixes.Select(async prefix =>
        {
            var url = prefix + "gongchanwangzilin" + new Uri(zipUrl).AbsolutePath;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.Add("Range", "bytes=0-262143");   // 256KB
                var resp = await _http.SendAsync(req,
                    System.Net.Http.HttpCompletionOption.ResponseHeadersRead,
                    new CancellationTokenSource(TimeSpan.FromSeconds(15)).Token);
                long got = 0;
                if (resp.IsSuccessStatusCode)
                {
                    using var ms = new MemoryStream();
                    await resp.Content.CopyToAsync(ms,
                        new CancellationTokenSource(TimeSpan.FromSeconds(15)).Token);
                    got = ms.Length;
                }
                sw.Stop();
                var mibs = got > 0 ? (got / 1024.0 / 1024.0) / Math.Max(1, sw.ElapsedMilliseconds) * 1000 : 0;
                return new MirrorSpeed(prefix, got, (int)sw.ElapsedMilliseconds, mibs);
            }
            catch { return new MirrorSpeed(prefix, 0, int.MaxValue, 0); }
        }));
        return results.Where(r => r.Bytes > 0).OrderByDescending(r => r.MibPerSec).ToList();
    }

    /// <summary>下载更新包并校验 sha256（有 .sha256 资产时）。返回本地 zip 路径。</summary>
    public async Task<string> DownloadAsync(string zipUrl, string targetDir,
        string? expectedSha256 = null, Action<int>? onPercent = null)
    {
        Directory.CreateDirectory(targetDir);
        var fname = Path.GetFileName(new Uri(zipUrl).AbsolutePath);
        var dest = Path.Combine(targetDir, fname);
        var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        using var resp = await _http.GetAsync(zipUrl,
            System.Net.Http.HttpCompletionOption.ResponseHeadersRead, cts.Token);
        resp.EnsureSuccessStatusCode();
        var total = resp.Content.Headers.ContentLength ?? -1;
        var totalKnown = total > 0;
        await using var source = await resp.Content.ReadAsStreamAsync(cts.Token);
        await using var fs = File.Create(dest);
        var buffer = new byte[81920];
        long read = 0;
        int n;
        while ((n = await source.ReadAsync(buffer.AsMemory(), cts.Token)) > 0)
        {
            await fs.WriteAsync(buffer.AsMemory(0, n), cts.Token);
            read += n;
            if (totalKnown)
                onPercent?.Invoke((int)(read * 100 / total));
        }
        onPercent?.Invoke(100);

        if (!string.IsNullOrWhiteSpace(expectedSha256))
        {
            var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(dest)))
                .ToLowerInvariant();
            if (!string.Equals(hash, expectedSha256.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(dest);
                throw new CryptographicException("SHA256 校验失败，文件可能被篡改");
            }
        }
        return dest;
    }

    /// <summary>从 GitHub 拉取 .sha256 资产内容（若存在）。</summary>
    public async Task<string?> FetchSha256Async(string sha256Url)
    {
        if (string.IsNullOrWhiteSpace(sha256Url)) return null;
        try
        {
            var text = await _http.GetStringAsync(sha256Url);
            return text.Trim().Split(' ', '\t').First();
        }
        catch { return null; }
    }
}
