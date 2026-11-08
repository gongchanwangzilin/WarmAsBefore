using System.IO.Compression;

namespace WarmAsBefore.Services;

/// <summary>
/// 带资源上限的 ZIP 解压（审计 S8）。
///
/// 素材包 / 存档包 / 插件包都是「别人给的文件」，`ZipFile.ExtractToDirectory` 本身不限制
/// 条目数与解压后体积，几十 KB 的压缩包可以展开成几十 GB（zip bomb）。
/// 这里统一加三道闸：条目数、单文件字节、总字节；同时保留路径穿越防护。
/// </summary>
public static class SafeZip
{
    public const int MaxEntries = 4096;
    public const long MaxEntryBytes = 256L * 1024 * 1024;      // 单文件 256 MB
    public const long MaxTotalBytes = 1024L * 1024 * 1024;     // 整包解压上限 1 GB

    public static void ExtractToDirectory(string zipPath, string destDir,
        int maxEntries = MaxEntries, long maxEntryBytes = MaxEntryBytes, long maxTotalBytes = MaxTotalBytes)
    {
        Directory.CreateDirectory(destDir);
        var destRoot = Path.GetFullPath(destDir) + Path.DirectorySeparatorChar;

        using var zip = ZipFile.OpenRead(zipPath);
        if (zip.Entries.Count > maxEntries)
            throw new InvalidDataException($"压缩包条目过多（{zip.Entries.Count} > {maxEntries}），已中止导入。");

        long total = 0;
        long actualTotal = 0;
        foreach (var entry in zip.Entries)
        {
            // 目录条目没有名字结尾以外的内容，跳过
            if (string.IsNullOrEmpty(entry.Name)) continue;

            // 声明大小就超限的直接拒；声明可以造假，所以下面复制时还会再数一遍真实字节
            if (entry.Length > maxEntryBytes)
                throw new InvalidDataException($"压缩包内文件过大：{entry.FullName}");
            total += entry.Length;
            if (total > maxTotalBytes)
                throw new InvalidDataException("压缩包解压后总体积超限，已中止导入（可能是压缩炸弹）。");

            var target = Path.GetFullPath(Path.Combine(destDir, entry.FullName));
            if (!target.StartsWith(destRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("压缩包含有越界路径：" + entry.FullName);

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);

            using var input = entry.Open();
            using var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None);
            CopyCapped(input, output, maxEntryBytes, ref actualTotal, maxTotalBytes, entry.FullName);
        }
    }

    /// <summary>边复制边数真实字节：声明的 Length 可以造假，只有实际读出来的才算数。</summary>
    private static void CopyCapped(Stream input, Stream output, long maxEntryBytes,
        ref long actualTotal, long maxTotalBytes, string name)
    {
        var buffer = new byte[81920];
        long written = 0;
        int n;
        while ((n = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            written += n;
            if (written > maxEntryBytes)
                throw new InvalidDataException($"压缩包内文件实际体积超限：{name}");
            actualTotal += n;
            output.Write(buffer, 0, n);
            if (actualTotal > maxTotalBytes)
                throw new InvalidDataException("压缩包解压后总体积超限，已中止导入（可能是压缩炸弹）。");
        }
    }
}
