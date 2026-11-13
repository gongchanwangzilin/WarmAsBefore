using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using WarmAsBefore.Models;
using WarmAsBefore.Services;

namespace WarmAsBefore.Services.Model;

/// <summary>
/// F04：用户驱动的模型部署工具。模型名、来源（链接或本机文件）、安装位置**全部由用户决定**，
/// 工具只负责「下载 → 校验(SHA256，可选) → 解压(zip) → 落盘 → 登记到配置」这一条全自动链路。
/// 不内置任何模型名、不内置任何默认路径。
/// </summary>
public sealed record ModelDeployResult(string Id, bool Ok, string Message);

public sealed class ModelDeploymentService
{
    private readonly SettingsManager _settings;
    private readonly HttpClient _http = new() { Timeout = Timeout.InfiniteTimeSpan };

    public ModelDeploymentService(SettingsManager settings) => _settings = settings;

    // ============ 登记表的读写（落盘到 UserSettings.DeployedModels） ============

    private List<DeployedModel> Read() => _settings.Current.DeployedModels ?? new();

    private async Task WriteAsync(List<DeployedModel> list)
    {
        _settings.Apply(_settings.Current with { DeployedModels = list });
        await _settings.Persist();
    }

    /// <summary>当前登记的全部模型（最新在前）。</summary>
    public List<DeployedModel> List() => Read().OrderByDescending(x => x.AddedAt).ToList();

    private async Task UpsertAsync(DeployedModel m)
    {
        var list = Read();
        var i = list.FindIndex(x => x.Id == m.Id);
        if (i >= 0) list[i] = m; else list.Add(m);
        await WriteAsync(list);
    }

    /// <summary>移除一条登记，同时删掉它的安装目录（仅该子目录）。</summary>
    public async Task RemoveAsync(string id)
    {
        var list = Read();
        var m = list.FirstOrDefault(x => x.Id == id);
        if (m is null) return;
        try
        {
            if (!string.IsNullOrWhiteSpace(m.InstallPath) && Directory.Exists(m.InstallPath))
                Directory.Delete(m.InstallPath, recursive: true);
        }
        catch (Exception ex) { App.WriteLog("ModelDeployment.Remove -> " + ex.Message); }
        list.RemoveAll(x => x.Id == id);
        await WriteAsync(list);
    }

    // ============ 核心：下载 / 校验 / 解压 / 落盘 / 登记 ============

    public async Task<ModelDeployResult> DeployAsync(DeployedModel spec,
        IProgress<ModelDeployProgress>? progress = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(spec.Name))
            return new(spec.Id, false, "模型名不能为空：名字由你起，随便一个也行。");
        if (string.IsNullOrWhiteSpace(spec.Source))
            return new(spec.Id, false, "来源不能为空：填一个 http(s) 链接，或本机文件路径。");
        if (string.IsNullOrWhiteSpace(spec.InstallPath))
            return new(spec.Id, false, "安装位置不能为空：用「选择目录」挑一个。");

        var work = Path.Combine(Path.GetTempPath(), "wab_model_" + spec.Id);
        try { Directory.CreateDirectory(work); }
        catch (Exception ex) { return new(spec.Id, false, "建临时目录失败：" + ex.Message); }

        var isRemote = spec.Source.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                     || spec.Source.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        string localFile;
        try
        {
            if (isRemote)
            {
                progress?.Report(new ModelDeployProgress { Id = spec.Id, Phase = DeployStatus.Downloading, Message = "开始下载…" });
                localFile = await DownloadAsync(spec.Source, work, spec.Id, progress, ct);
            }
            else
            {
                if (!File.Exists(spec.Source))
                    return new(spec.Id, false, "本地来源文件不存在：" + spec.Source);
                localFile = Path.Combine(work, Path.GetFileName(spec.Source));
                File.Copy(spec.Source, localFile, overwrite: true);
            }
        }
        catch (OperationCanceledException) { return new(spec.Id, false, "已取消。"); }
        catch (Exception ex) { return new(spec.Id, false, "取不到模型文件：" + ex.Message); }

        if (!string.IsNullOrWhiteSpace(spec.Checksum))
        {
            progress?.Report(new ModelDeployProgress { Id = spec.Id, Phase = DeployStatus.Verifying, Message = "校验完整性…" });
            var hash = await Sha256HexAsync(localFile);
            if (!string.Equals(hash, spec.Checksum.Trim(), StringComparison.OrdinalIgnoreCase))
                return new(spec.Id, false, "校验和不匹配，已停止安装（你填的与下载文件不一致）。");
        }

        try
        {
            Directory.CreateDirectory(spec.InstallPath);
            if (localFile.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                progress?.Report(new ModelDeployProgress { Id = spec.Id, Phase = DeployStatus.Verifying, Message = "解压…" });
                ZipFile.ExtractToDirectory(localFile, spec.InstallPath, overwriteFiles: true);
            }
            else
            {
                File.Copy(localFile, Path.Combine(spec.InstallPath, Path.GetFileName(localFile)), overwrite: true);
            }
        }
        catch (Exception ex) { return new(spec.Id, false, "安装落盘失败：" + ex.Message); }

        var bytes = new FileInfo(localFile).Length;
        var installed = spec with
        {
            Status = DeployStatus.Installed,
            Bytes = bytes,
            LastError = null,
            AddedAt = DateTime.UtcNow
        };
        await UpsertAsync(installed);
        try { Directory.Delete(work, recursive: true); } catch { }
        return new(spec.Id, true, "已装到 " + spec.InstallPath);
    }

    private async Task<string> DownloadAsync(string url, string dir, string id,
        IProgress<ModelDeployProgress>? progress, CancellationToken ct)
    {
        using var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!resp.IsSuccessStatusCode)
            throw new IOException("下载返回 " + (int)resp.StatusCode);
        var total = resp.Content.Headers.ContentLength ?? -1;
        var dest = Path.Combine(dir, SanitizeFileName(url));
        await using var fs = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
        await using var src = await resp.Content.ReadAsStreamAsync(ct);
        var buf = new byte[81920];
        long received = 0;
        int n;
        while ((n = await src.ReadAsync(buf, 0, buf.Length, ct)) > 0)
        {
            await fs.WriteAsync(buf, 0, n, ct);
            received += n;
            if (total > 0)
                progress?.Report(new ModelDeployProgress
                {
                    Id = id,
                    Phase = DeployStatus.Downloading,
                    Received = received,
                    Total = total,
                    Message = $"{received / 1_000_000d:0.0} / {total / 1_000_000d:0.0} MB"
                });
        }
        return dest;
    }

    private static async Task<string> Sha256HexAsync(string file)
    {
        await using var fs = File.OpenRead(file);
        var hash = await SHA256.HashDataAsync(fs);
        return Convert.ToHexString(hash);
    }

    private static string SanitizeFileName(string url)
    {
        var name = url.Split('?')[0].TrimEnd('/');
        name = name[(name.LastIndexOf('/') + 1)..];
        if (name.Length == 0) name = "model.bin";
        return Path.GetInvalidFileNameChars().Aggregate(name, (s, c) => s.Replace(c, '_'));
    }

    // ============ F26 启动自检 ============

    /// <summary>
    /// 现在有没有可用模型（量化向量 / 小模型 / 任一厂商模型）？都没有 = 需要引导去部署向导。
    /// </summary>
    public bool IsModelConfigured()
    {
        var s = _settings.Current;
        if (s.EmbeddingEnabled && !string.IsNullOrWhiteSpace(s.EmbeddingModel)) return true;
        if (s.SmallModelFirst && !string.IsNullOrWhiteSpace(s.SmallAiModel)) return true;
        foreach (var p in s.AiProviders ?? new List<AiProvider>())
            if (p.Enabled && p.Models is { Count: > 0 }) return true;
        return false;
    }

    // ============ 按性能推荐（仅示例，用户决定） ============

    /// <summary>
    /// 按本机 CPU 核数 / 内存给几条**示例建议**。名字、来源占位、安装位置全部仍是用户自己定，
    /// 工具不会替你选好任何模型。点「用这条」只是把示例预填进表单，你可以改任意字段。
    /// </summary>
    public List<ModelSuggestion> SuggestByPerformance()
    {
        var cores = Math.Max(1, Environment.ProcessorCount);
        long mem = 0;
        try { mem = (long)GC.GetGCMemoryInfo().TotalAvailableMemoryBytes; } catch { }
        var ramGb = mem > 0 ? mem / 1_073_741_824d : 0;
        var tier = cores >= 8 && ramGb >= 16 ? "高" : cores >= 4 ? "中" : "低";
        return new()
        {
            new() { Kind = ModelKind.Embedding, Name = "你定的向量模型名", ExampleSource = "https://你的镜像/模型.gguf 或 .onnx",
                Note = $"近义检索用；{tier}配机建议先试小尺寸（约 100MB 级）", ApproxBytes = 120_000_000 },
            new() { Kind = ModelKind.SmallModel, Name = "你定的小模型名", ExampleSource = "https://你的镜像/小模型.gguf",
                Note = "情绪表演 / 工具解析 / 状态包压缩；便宜优先", ApproxBytes = 500_000_000 },
            new() { Kind = ModelKind.LLM, Name = "你定的大模型名", ExampleSource = "https://你的镜像/大模型",
                Note = $"主对话；当前约 {ramGb:0}GB 内存、{cores} 核", ApproxBytes = 4_000_000_000 },
        };
    }
}
