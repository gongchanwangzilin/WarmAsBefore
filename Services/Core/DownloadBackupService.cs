#nullable enable
using System;
using System.IO;

namespace WarmAsBefore.Services;

/// <summary>
/// 自动备份数据包到「下载」目录（双触发：应用启动时 + 每次导出成功后）。
///
/// 实现：
///   Android 10+（API 29+）→ MediaStore.Downloads（隔离写入，无需存储权限）
///   Android 9 及以下 → 应用专属外部目录（旧版无 MediaStore.Downloads，落 /sdcard/Android/data/&lt;pkg&gt;/files/backup）
///   桌面端 → no-op（桌面有自己的数据目录，无「下载文件夹」概念）
/// </summary>
public static class DownloadBackupService
{
    static readonly object _lock = new();
    static bool _started;

    /// <summary>应用启动时调用一次：把数据目录最新内容打包并备份到下载文件夹。失败只记日志。</summary>
    public static async Task OnAppStartAsync()
    {
        lock (_lock)
        {
            if (_started) return;
            _started = true;
        }
        try
        {
            var tmpZip = Path.Combine(Path.GetTempPath(),
                "wab_autobackup_" + DateTime.Now.ToString("yyyyMMddHHmmss") + ".zip");
            await DataPackageService.ExportAsync(tmpZip, includeSensitive: false, progress: null);
            if (SaveToDownloads(tmpZip))
                App.WriteLog("DownloadBackupService: 启动备份成功 -> 下载/WarmAsBefore_backup.zip");
            else
                App.WriteLog("DownloadBackupService: 启动备份未写入下载目录（桌面 no-op 或失败）");
            try { if (File.Exists(tmpZip)) File.Delete(tmpZip); } catch { }
        }
        catch (Exception ex)
        {
            App.WriteLog("DownloadBackupService.OnAppStart -> " + ex.Message);
        }
    }

    /// <summary>导出成功后调用：把刚导出的数据包同步一份到下载文件夹。</summary>
    public static void AfterExport(string? sourceZip)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(sourceZip) || !File.Exists(sourceZip)) return;
            if (SaveToDownloads(sourceZip))
                App.WriteLog("DownloadBackupService: 导出后备份成功 -> 下载/WarmAsBefore_backup.zip");
        }
        catch (Exception ex)
        {
            App.WriteLog("DownloadBackupService.AfterExport -> " + ex.Message);
        }
    }

    /// <summary>把 zip 落到「下载」目录。桌面返回 false（no-op）；Android 返回是否成功。</summary>
    static bool SaveToDownloads(string sourceZip)
    {
        const string fileName = "WarmAsBefore_backup.zip";
#if ANDROID
        try
        {
            if (Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.Q)
            {
                // Android 10+：MediaStore.Downloads（隔离写入，无需存储权限）
                var cr = Android.App.Application.Context!.ContentResolver;
                var values = new Android.Content.ContentValues();
                values.Put(Android.Provider.MediaStore.MediaColumns.DisplayName, fileName);
                values.Put(Android.Provider.MediaStore.MediaColumns.MimeType, "application/zip");
                // Android 10+：公共下载目录 content URI（隔离写入，无需存储权限）
                var collection = Android.Net.Uri.Parse("content://downloads/public_downloads");
                var uri = cr.Insert(collection, values);
                if (uri is null)
                {
                    App.WriteLog("DownloadBackupService: MediaStore insert 失败（uri=null）");
                    return false;
                }
                using var inStream = File.OpenRead(sourceZip);
                using var outStream = cr.OpenOutputStream(uri)!;
                inStream.CopyTo(outStream);
                return true;
            }
            else
            {
                // Android 9 及以下：公共下载目录 /sdcard/Download（需 WRITE_EXTERNAL_STORAGE，Manifest 已声明）
                var extRoot = global::Android.App.Application.Context!.GetExternalFilesDir("");
                if (extRoot is null)
                {
                    App.WriteLog("DownloadBackupService: 无法获取外部目录（旧版 API 失败）");
                    return false;
                }
                // 旧版无 MediaStore.Downloads，落到应用专属目录（用户通过文件管理器可见 /sdcard/Android/data/<pkg>/files）
                var backupDir = System.IO.Directory.CreateDirectory(
                    System.IO.Path.Combine(extRoot.AbsolutePath, "backup"));
                var dest = System.IO.Path.Combine(backupDir.FullName, fileName);
                File.Copy(sourceZip, dest, overwrite: true);
                return File.Exists(dest);
            }
        }
        catch (Exception ex)
        {
            App.WriteLog("DownloadBackupService.SaveToDownloads -> " + ex.Message);
            return false;
        }
#else
        // 桌面：没有「下载文件夹」概念，返回 false（调用方按 no-op 处理）
        return false;
#endif
    }
}
