using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace WarmAsBefore.Services;

/// <summary>
/// 数据包导出 / 导入（设置页「数据管理」）。
///
/// 应用的全部状态都在同一个目录里：<see cref="App.RootDirectory"/>（与 <see cref="StorageProvider.Root"/> 同值），
/// 其下是 assets\ 立绘、saves\ 存档、characters.json、settings.json 等。
/// 所以导出 = 把该目录整体压成一个 zip；导入 = 解压到临时目录校验后替换回该目录。
///
/// 密钥处理：导出时默认把 settings.json 里 4 个敏感字段（AiKey / QqAppSecret /
/// WechatAppSecret / VoiceApiKey）清成空串，其余设置原样保留；只有用户显式勾选
/// 「包含 API 密钥与机器人密钥」时才原样写入。
/// </summary>
public static class DataPackageService
{
    /// <summary>数据包标记文件（写在 zip 根目录），导入端据此识别「这是温暖如初数据包」。</summary>
    public const string MarkerFileName = "datapack.json";

    /// <summary>旧版数据包外层多套的那一层目录名，导入时自动剥掉以兼容。</summary>
    public const string PackageRootName = "WarmAsBefore";

    /// <summary>导出时会被清空的 settings.json 敏感键。</summary>
    public static readonly string[] SensitiveKeys =
        { "AiKey", "QqAppSecret", "WechatAppSecret", "VoiceApiKey" };

    /// <summary>导出时跳过的目录名（构建产物 / 上次导入的临时目录）。</summary>
    private static readonly string[] ExcludedDirNames = { "publish", "bin", "obj", "temp_import" };

    private static readonly JsonSerializerOptions PrettyJson = new() { WriteIndented = true };

    /// <summary>进度上报间隔（文件数）：上万个立绘时逐文件回调会把 UI 线程刷爆。</summary>
    private const int ReportEveryNFiles = 25;

    /// <summary>
    /// 导出数据包：把 <see cref="App.RootDirectory"/> 整体压成 zip。
    /// </summary>
    /// <param name="zipPath">目标 zip 完整路径（建议放临时目录，写完后交给 FileSaver 让用户选位置）。</param>
    /// <param name="includeSensitive">true 时连密钥一起写入；false（默认）清空 settings.json 里的敏感字段。</param>
    /// <param name="progress">进度回调 (已完成文件数, 总文件数, 当前相对路径)。</param>
    /// <returns>导出的 zip 完整路径。</returns>
    public static async Task<string> ExportAsync(
        string zipPath,
        bool includeSensitive = false,
        IProgress<(int Done, int Total, string Path)>? progress = null)
    {
        var root = App.RootDirectory;
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException("数据目录不存在：" + root);

        var zipFull = System.IO.Path.GetFullPath(zipPath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(zipFull)!);

        // 预先枚举待导出文件，作为进度分母；同时排除 zip 自身（万一用户把包存进数据目录）
        var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(f => !IsExcluded(root, f))
            .Where(f => !string.Equals(System.IO.Path.GetFullPath(f), zipFull, StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();
        int total = files.Count;
        int done = 0;
        bool settingsSkipped = false;

        await Task.Run(() =>
        {
            using var fs = new FileStream(zipFull, FileMode.Create, FileAccess.Write, FileShare.None);
            using var archive = new ZipArchive(fs, ZipArchiveMode.Create);

            // 标记文件：导入端凭它确认这是本应用的数据包
            WriteTextEntry(archive, MarkerFileName, JsonSerializer.Serialize(new
            {
                app = "WarmAsBefore",
                kind = "datapack",
                version = 1,
                exportedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                includesSecrets = includeSensitive
            }, PrettyJson));

            foreach (var f in files)
            {
                var rel = System.IO.Path.GetRelativePath(root, f).Replace(System.IO.Path.DirectorySeparatorChar, '/');
                try
                {
                    if (!includeSensitive && rel.Equals("settings.json", StringComparison.OrdinalIgnoreCase))
                    {
                        // 不能整目录打包：settings.json 里混着普通设置和密钥，要逐字段清空
                        var sanitized = SanitizeSettings(File.ReadAllText(f));
                        if (sanitized is null) settingsSkipped = true;   // 解析失败：宁可不带，也不外泄
                        else WriteTextEntry(archive, rel, sanitized);
                    }
                    else
                    {
                        archive.CreateEntryFromFile(f, rel, CompressionLevel.Fastest);
                    }
                }
                catch
                {
                    // 单个文件失败（被占用等）不中断整体导出
                }

                done++;
                if (progress is not null && (done % ReportEveryNFiles == 0 || done == total))
                    progress.Report((done, total, rel));
            }

            if (progress is not null && total == 0) progress.Report((0, 0, ""));
        });

        if (settingsSkipped)
            App.WriteLog("DataPackageService: settings.json 解析失败，已从数据包中跳过（避免密钥外泄）");

        return zipFull;
    }

    /// <summary>把 settings.json 里的敏感字段清成 ""，其余设置原样保留。解析失败返回 null。</summary>
    private static string? SanitizeSettings(string json)
    {
        try
        {
            if (JsonNode.Parse(json) is not JsonObject obj) return null;
            foreach (var key in SensitiveKeys)
            {
                // 只清已存在的键，不给旧版本设置文件凭空造字段
                if (obj.ContainsKey(key)) obj[key] = string.Empty;
            }
            return obj.ToJsonString(PrettyJson);
        }
        catch
        {
            return null;
        }
    }

    private static void WriteTextEntry(ZipArchive archive, string entryName, string text)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using var s = entry.Open();
        using var w = new StreamWriter(s, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        w.Write(text);
    }

    private static bool IsExcluded(string root, string file)
    {
        var rel = System.IO.Path.GetRelativePath(root, file);
        var parts = rel.Split(new[] { System.IO.Path.DirectorySeparatorChar, '/' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var p in parts)
            if (ExcludedDirNames.Contains(p, StringComparer.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>快速校验：能打开、且含标记文件 / characters.json / settings.json 之一。</summary>
    public static bool IsValidPackage(string zipPath)
    {
        try
        {
            using var z = ZipFile.OpenRead(zipPath);
            var names = z.Entries
                .Select(e => e.FullName.Replace('\\', '/').TrimStart('/'))
                .Where(n => n.Length > 0)
                .ToList();
            if (names.Count == 0) return false;

            bool HasFile(string name) => names.Any(n =>
                n.Equals(name, StringComparison.OrdinalIgnoreCase) ||
                n.EndsWith("/" + name, StringComparison.OrdinalIgnoreCase));

            // 新包有标记文件；旧包没有标记，退化为「有角色库或设置」
            return HasFile(MarkerFileName) || HasFile("characters.json") || HasFile("settings.json");
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 解压到临时目录（不碰正式数据目录）供校验与预览。
    /// 旧版数据包外层多套了一层 WarmAsBefore\，这里会自动剥掉。
    /// </summary>
    /// <returns>解压后的临时目录完整路径；调用方用完需 <see cref="CleanupImport"/>。</returns>
    public static async Task<string> UnpackForImportAsync(
        string zipPath,
        IProgress<(int Done, int Total, string Path)>? progress = null)
    {
        var tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "wab_import_" + Guid.NewGuid().ToString("N"));
        var tempFull = System.IO.Path.GetFullPath(tempDir);
        Directory.CreateDirectory(tempDir);

        try
        {
            await Task.Run(() =>
            {
                using var z = ZipFile.OpenRead(zipPath);

                var fileNames = z.Entries.Where(e => e.Name.Length > 0).Select(e => e.FullName).ToList();
                var prefix = fileNames.Count > 0 && fileNames.All(n =>
                        n.Replace('\\', '/').StartsWith(PackageRootName + "/", StringComparison.OrdinalIgnoreCase))
                    ? PackageRootName + "/"
                    : "";

                int total = fileNames.Count;
                int done = 0;

                foreach (var entry in z.Entries)
                {
                    if (entry.Name.Length == 0) continue;   // 目录条目

                    var rel = entry.FullName.Replace('\\', '/');
                    if (prefix.Length > 0 && rel.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        rel = rel[prefix.Length..];
                    rel = rel.TrimStart('/');
                    if (rel.Length == 0) continue;

                    // 防 zip-slip：解压目标必须仍在临时目录内
                    var target = System.IO.Path.GetFullPath(System.IO.Path.Combine(tempFull, rel));
                    if (!target.StartsWith(tempFull + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                        throw new IOException("数据包含有非法路径（越出导入目录）：" + entry.FullName);

                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!);
                    entry.ExtractToFile(target, overwrite: true);

                    done++;
                    if (progress is not null && (done % ReportEveryNFiles == 0 || done == total))
                        progress.Report((done, total, rel));
                }
            });
        }
        catch
        {
            CleanupImport(tempDir);   // 解压失败不留半个临时目录
            throw;
        }

        return tempDir;
    }

    /// <summary>列出解压目录的顶层内容，用于导入前展示「将替换什么」。目录项 Size 为 -1。</summary>
    public static List<(string Name, long Size)> ListImportTargets(string unpackedDir)
    {
        var result = new List<(string Name, long Size)>();
        if (!Directory.Exists(unpackedDir)) return result;

        foreach (var p in Directory.EnumerateFileSystemEntries(unpackedDir)
                     .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            if (Directory.Exists(p))
                result.Add((System.IO.Path.GetFileName(p) + "/  (目录)", -1));
            else
                result.Add((System.IO.Path.GetFileName(p), new FileInfo(p).Length));
        }
        return result;
    }

    /// <summary>
    /// 用解压目录替换正式数据目录，返回 (是否成功, 说明)。
    ///
    /// 优先把旧目录整体挪到同级的 WarmAsBefore.bak_* 再拷入新数据 —— 干净替换，不留旧文件。
    /// 旧目录被占用（立绘 / 音频句柄未释放）挪不动时，退化为逐文件覆盖导入。
    /// </summary>
    public static (bool Ok, string Message) ReplaceDataDir(string unpackedDir)
    {
        if (!Directory.Exists(unpackedDir))
            return (false, "临时目录不存在");

        var root = App.RootDirectory;
        var parent = System.IO.Path.GetDirectoryName(root) ?? root;
        var backup = System.IO.Path.Combine(parent,
            "WarmAsBefore.bak_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));

        bool moved = false;
        try
        {
            if (Directory.Exists(root)) Directory.Move(root, backup);
            moved = true;
        }
        catch (Exception ex)
        {
            App.WriteLog("DataPackageService: 旧数据目录无法移动，改用覆盖导入 -> " + ex.Message);
        }

        if (!moved)
        {
            MergeCopy(unpackedDir, root, skipMarker: true);
            return (true, "旧数据目录被占用，已覆盖导入（个别旧文件可能残留）");
        }

        try
        {
            Directory.CreateDirectory(root);
            MergeCopy(unpackedDir, root, skipMarker: true);
        }
        catch (Exception ex)
        {
            // 写入失败：把旧目录搬回来，尽量不留半成品
            try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { }
            try { if (Directory.Exists(backup)) Directory.Move(backup, root); } catch { }
            return (false, "写入数据目录失败：" + ex.Message);
        }

        // 旧数据已无用；若仍有文件被占用删不掉，就保留备份目录，重启后可手动删除
        try
        {
            Directory.Delete(backup, true);
        }
        catch (Exception ex)
        {
            App.WriteLog("DataPackageService: 备份目录未能删除（重启后可手动删）：" + backup + " -> " + ex.Message);
        }

        return (true, "已替换全部数据");
    }

    /// <summary>逐文件覆盖复制（源目录内容拷进目标目录，同名覆盖）。</summary>
    private static void MergeCopy(string src, string dst, bool skipMarker)
    {
        Directory.CreateDirectory(dst);
        foreach (var f in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
        {
            var rel = System.IO.Path.GetRelativePath(src, f);
            if (skipMarker && rel.Equals(MarkerFileName, StringComparison.OrdinalIgnoreCase)) continue;

            var dest = System.IO.Path.Combine(dst, rel);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(dest)!);
            File.Copy(f, dest, overwrite: true);
        }
    }

    /// <summary>清理临时解压目录（失败则忽略，下次导入会重建）。</summary>
    public static void CleanupImport(string unpackedDir)
    {
        try
        {
            if (Directory.Exists(unpackedDir)) Directory.Delete(unpackedDir, true);
        }
        catch { }
    }

    /// <summary>格式化文件大小（用于 UI 展示）。</summary>
    public static string FormatSize(long bytes)
    {
        if (bytes < 0) return "-";
        if (bytes < 1024) return bytes + " B";
        if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("0.#", CultureInfo.InvariantCulture) + " KB";
        if (bytes < 1024L * 1024 * 1024) return (bytes / 1024.0 / 1024.0).ToString("0.#", CultureInfo.InvariantCulture) + " MB";
        return (bytes / 1024.0 / 1024.0 / 1024.0).ToString("0.#", CultureInfo.InvariantCulture) + " GB";
    }
}
