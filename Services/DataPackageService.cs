using System.IO.Compression;

using WarmAsBefore.ViewModels;

namespace WarmAsBefore.Services;

/// <summary>
/// 数据包导出/导入服务（设置页「数据管理」区）。
///
/// 数据根目录为 App.RootDirectory（&lt;AppData&gt;\WarmAsBefore\WarmAsBefore\），
/// 其下再分 Data / Settings 两个子目录。导出默认排除 Settings\settings.json
/// （其中含 AI Key、微信 AppSecret 等敏感信息），用户可勾选「包含敏感数据」。
/// </summary>
public static class DataPackageService
{
    /// <summary>压缩包内目录的根前缀（解压后的相对路径以 WarmAsBefore\ 开头）。</summary>
    public const string PackageRootName = "WarmAsBefore";

    private static readonly string[] ExcludedDirNames = { "publish", "bin", "obj", "temp_import" };

    /// <summary>导出数据包（压缩整个数据根目录到 zip）。</summary>
    /// <param name="zipPath">目标 zip 的完整路径。</param>
    /// <param name="includeSensitive">是否包含 settings.json（AI Key / 微信密钥）。</param>
    /// <param name="progress">进度回调 (已完成文件数, 总文件数, 正在处理的相对路径)。</param>
    /// <returns>导出的 zip 路径。</returns>
    public static async Task<string> ExportAsync(
        string zipPath,
        bool includeSensitive = false,
        IProgress<(int Done, int Total, string Path)>? progress = null)
    {
        var root = App.RootDirectory;
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException("数据目录不存在：" + root);

        Directory.CreateDirectory(Path.GetDirectoryName(zipPath)!);
        if (File.Exists(zipPath)) File.Delete(zipPath);

        // 预先枚举全部待导出文件，供进度分母使用
        var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(f => !IsExcluded(root, f) && (includeSensitive || !IsSensitive(f, root)))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();
        int total = files.Count;
        int done = 0;

        var dir = new FileInfo(zipPath).Directory;
        if (dir is not null) Directory.CreateDirectory(dir.FullName);
        if (File.Exists(zipPath)) File.Delete(zipPath);

        await Task.Run(() =>
        {
            using var fs = new FileStream(zipPath, FileMode.CreateNew);
            using var archive = new ZipArchive(fs, ZipArchiveMode.Create);
            foreach (var f in files)
            {
                var rel = Path.GetRelativePath(root, f).Replace(Path.DirectorySeparatorChar, '/');
                try
                {
                    archive.CreateEntryFromFile(f, PackageRootName + "/" + rel, CompressionLevel.Fastest);
                }
                catch
                {
                    // 单个文件失败（如被占用）不中断整体导出
                }
                done++;
                progress?.Report((done, total, f));
            }
        });

        return zipPath;
    }

    /// <summary>是否命中排除目录（publish / bin / obj / temp_import）。</summary>
    private static bool IsExcluded(string root, string file)
    {
        var rel = Path.GetRelativePath(root, file);
        var parts = rel.Split(new[] { Path.DirectorySeparatorChar, '/' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var p in parts)
            if (ExcludedDirNames.Contains(p, StringComparer.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static bool IsSensitive(string file, string root)
    {
        var rel = Path.GetRelativePath(root, file).Replace('\\', '/');
        // Settings\settings.json 含 AI Key 与微信 AppSecret
        return rel.StartsWith("Settings/", StringComparison.OrdinalIgnoreCase)
            && rel.EndsWith("/settings.json", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>从 zip 解压到临时导入目录（&lt;数据根&gt;\temp_import\）。</summary>
    /// <param name="zipPath">zip 完整路径。</param>
    /// <param name="progress">进度回调 (已解压文件数, 总文件数, 正在解压的相对路径)。</param>
    /// <returns>解压后的临时目录完整路径。</returns>
    public static async Task<string> UnpackForImportAsync(
        string zipPath,
        IProgress<(int Done, int Total, string Path)>? progress = null)
    {
        var root = App.RootDirectory;
        Directory.CreateDirectory(root);

        var entries = await Task.Run(() =>
        {
            using var z = ZipFile.OpenRead(zipPath);
            return z.Entries.ToList();
        });

        var tempDir = Path.Combine(root, "temp_import");
        if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        Directory.CreateDirectory(tempDir);

        // 确定解压基准：若 zip 内全部条目共享 PackageRootName 前缀，则剥掉这一层
        string prefix = "";
        if (entries.Count > 0 && entries.All(e => e.FullName.StartsWith(PackageRootName + "/", StringComparison.OrdinalIgnoreCase)))
            prefix = PackageRootName + "/";

        int total = entries.Count;
        int done = 0;

        await Task.Run(() =>
        {
            using var z = ZipFile.OpenRead(zipPath);
            foreach (var entry in z.Entries)
            {
                var rel = entry.FullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                    ? entry.FullName[prefix.Length..]
                    : entry.FullName;
                if (rel.Length == 0 || entry.FullName.EndsWith("/")) continue; // 目录条目或空前缀

                // 防 zip-slip：解析后的目标必须仍在 tempDir 内
                var target = Path.GetFullPath(Path.Combine(tempDir, rel));
                if (!target.StartsWith(Path.GetFullPath(tempDir) + Path.DirectorySeparatorChar,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new IOException("压缩包包含非法路径（越出导入目录）：" + entry.FullName);
                }

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                entry.ExtractToFile(target, overwrite: true);
                done++;
                progress?.Report((done, total, entry.FullName));
            }
        });

        return tempDir;
    }

    /// <summary>校验压缩包是否符合 WarmAsBefore 数据包结构（须含 settings.json 或 characters.json）。</summary>
    public static bool IsValidPackage(string zipPath)
    {
        try
        {
            using var z = ZipFile.OpenRead(zipPath);
            var names = z.Entries.Select(e => e.FullName.TrimStart('/')).ToList();
            bool Has(string name) =>
                names.Any(n => n.EndsWith("/" + name, StringComparison.OrdinalIgnoreCase)
                    || n.EndsWith(name, StringComparison.OrdinalIgnoreCase));
            return Has("settings.json") || Has("characters.json");
        }
        catch
        {
            return false;
        }
    }

    /// <summary>列出解压目录的顶层内容（目录树摘要），用于导入前展示「将覆盖什么」。</summary>
    public static List<(string Name, long Size)> ListImportTargets(string unpackedDir)
    {
        var result = new List<(string Name, long Size)>();
        if (!Directory.Exists(unpackedDir)) return result;
        foreach (var p in Directory.EnumerateFileSystemEntries(unpackedDir))
        {
            if (Directory.Exists(p))
                result.Add((Path.GetFileName(p) + "  (目录)", -1));
            else
                result.Add((Path.GetFileName(p), new FileInfo(p).Length));
        }
        return result;
    }

    /// <summary>把解压目录的内容移动到正式数据目录（覆盖同名文件）。</summary>
    public static void MergeIntoDataDir(string unpackedDir)
    {
        var root = App.RootDirectory;
        foreach (var src in Directory.EnumerateFiles(unpackedDir, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(unpackedDir, src);
            var dst = Path.Combine(root, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            File.Copy(src, dst, overwrite: true);
        }
    }

    /// <summary>导入完成后清理临时解压目录。</summary>
    public static void CleanupImport(string unpackedDir)
    {
        try
        {
            if (Directory.Exists(unpackedDir)) Directory.Delete(unpackedDir, true);
        }
        catch { /* 忽略：下次导入会重建 */ }
    }

    /// <summary>格式化文件大小（用于 UI 展示）。</summary>
    public static string FormatSize(long bytes)
    {
        if (bytes < 1024) return bytes + " B";
        if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("0.#") + " KB";
        if (bytes < 1024L * 1024 * 1024) return (bytes / 1024.0 / 1024.0).ToString("0.#") + " MB";
        return (bytes / 1024.0 / 1024.0 / 1024.0).ToString("0.#") + " GB";
    }
}
