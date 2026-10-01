using System.Security.Cryptography;
using WarmAsBefore.Models;

namespace WarmAsBefore.Services;

/// <summary>
/// 素材库：音乐库 + 背景库。
/// - 音乐：扫描 {root}/assets/audio，支持导入/重命名/删除/试听/时长格式；内容 SHA1 去重（同一文件不重复保存）。
/// - 背景：扫描 {root}/assets/backgrounds，支持 JPG/PNG/MP4；MP4 导入时提取首帧缩略图。
/// - 声音分配：左键音 / 右键音 / 按键音 / 背景音轮播列表（持久化 audioassign）。
/// </summary>
public sealed class MaterialLibrary
{
    private readonly StorageProvider _store;
    private readonly List<MusicItem> _music = new();
    private readonly List<BackgroundItem> _backgrounds = new();
    private SoundAssign _assign = new();
    private bool _loaded;
    private static readonly object Gate = new();

    private const string MusicKey = "material_music";
    private const string BgKey = "material_backgrounds";
    private const string AssignKey = "audioassign";

    private static readonly string[] MusicExts = { ".mp3", ".wav", ".ogg", ".m4a", ".aac", ".flac", ".wma" };
    private static readonly string[] ImageExts = { ".png", ".jpg", ".jpeg", ".bmp", ".webp", ".gif" };
    private static readonly string[] VideoExts = { ".mp4", ".mov", ".m4v", ".webm" };

    public MaterialLibrary(StorageProvider store) => _store = store;

    public IReadOnlyList<MusicItem> MusicItems => _music;
    public IReadOnlyList<BackgroundItem> BackgroundItems => _backgrounds;
    public SoundAssign Assign => _assign;

    public string AudioRoot => Path.Combine(_store.Root, "assets", "audio");
    public string BgRoot => Path.Combine(_store.Root, "assets", "backgrounds");

    public string ResolveAbs(string rel)
    {
        if (string.IsNullOrWhiteSpace(rel)) return "";
        if (Path.IsPathRooted(rel)) return rel;
        return Path.Combine(_store.Root, rel);
    }

    public string ToRel(string absPath)
    {
        try { return Path.GetRelativePath(_store.Root, absPath).Replace('\\', '/'); }
        catch { return absPath; }
    }

    // ==================== 装载 / 同步磁盘 ====================

    public Task EnsureLoadedAsync()
    {
        lock (Gate)
        {
            if (_loaded) return Task.CompletedTask;
            _loaded = true;
        }
        // 装载涉及目录扫描 + 文件哈希 + 同步读取存储的 JSON，全部放到后台线程，
        // 避免在 UI 线程上同步过 async 导致卡死 / 死锁。
        return Task.Run(() =>
        {
            try
            {
                var savedMusic = _store.Exists(MusicKey)
                    ? _store.Load<List<MusicItem>>(MusicKey).GetAwaiter().GetResult()
                    : null;
                var savedBg = _store.Exists(BgKey)
                    ? _store.Load<List<BackgroundItem>>(BgKey).GetAwaiter().GetResult()
                    : null;
                var savedAssign = _store.Exists(AssignKey)
                    ? _store.Load<SoundAssign>(AssignKey).GetAwaiter().GetResult()
                    : null;
                if (savedMusic is not null) _music.AddRange(savedMusic);
                if (savedBg is not null) _backgrounds.AddRange(savedBg);
                if (savedAssign is not null) _assign = savedAssign;
                SyncWithDisk();
                App.WriteLog("MaterialLibrary: loaded " + _music.Count + " music, " + _backgrounds.Count + " backgrounds");
            }
            catch (Exception ex)
            {
                App.WriteLog("MaterialLibrary.EnsureLoaded -> " + ex);
            }
        });
    }

    /// <summary>扫描目录：为新文件补条目，移除已删除文件的条目。</summary>
    private void SyncWithDisk()
    {
        try
        {
            foreach (var f in EnumerateFiles(AudioRoot, MusicExts))
            {
                var rel = ToRel(f);
                if (_music.Any(m => m.RelPath == rel)) continue;
                var item = new MusicItem
                {
                    Name = Path.GetFileNameWithoutExtension(f),
                    RelPath = rel,
                    Format = Path.GetExtension(f).TrimStart('.').ToLowerInvariant(),
                    Hash = HashFile(f)
                };
                _music.Add(item);
                ReadDurationAsync(f, item);
            }
            _music.RemoveAll(m => { try { return !File.Exists(ResolveAbs(m.RelPath)); } catch { return true; } });

            foreach (var f in EnumerateFiles(BgRoot, ImageExts.Concat(VideoExts)))
            {
                var rel = ToRel(f);
                if (_backgrounds.Any(b => b.RelPath == rel)) continue;
                var format = Path.GetExtension(f).TrimStart('.').ToLowerInvariant();
                var isVideo = "." + format is ".mp4" or ".mov" or ".m4v" or ".webm";
                var item = new BackgroundItem
                {
                    Name = Path.GetFileNameWithoutExtension(f),
                    RelPath = rel,
                    Format = format,
                    HashPath = HashFile(f)
                };
                _backgrounds.Add(item);
                if (isVideo) ExtractVideoThumbAsync(f, item);
            }
            _backgrounds.RemoveAll(b => { try { return !File.Exists(ResolveAbs(b.RelPath)); } catch { return true; } });

            _ = PersistAsync();
        }
        catch (Exception ex)
        {
            App.WriteLog("MaterialLibrary.SyncWithDisk -> " + ex);
        }
    }

    private static IEnumerable<string> EnumerateFiles(string dir, IEnumerable<string> exts)
    {
        if (!Directory.Exists(dir)) yield break;
        foreach (var f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
            if (exts.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
                yield return f;
    }

    // ==================== 导入（内容哈希去重） ====================

    public async Task<(bool ok, string message, MusicItem? item)> ImportMusicAsync(string srcPath)
    {
        try
        {
            await EnsureLoadedAsync();
            var ext = Path.GetExtension(srcPath);
            if (!MusicExts.Contains(ext, StringComparer.OrdinalIgnoreCase))
                return (false, "不支持的音频格式", null);
            var hash = HashFile(srcPath);
            var dup = _music.FirstOrDefault(m => m.Hash == hash);
            if (dup is not null)
                return (true, $"音乐库中已有「{dup.Name}」，未重复保存", dup);

            Directory.CreateDirectory(AudioRoot);
            var dest = UniqueDest(srcPath, AudioRoot);
            File.Copy(srcPath, dest);
            var item = new MusicItem
            {
                Name = Path.GetFileNameWithoutExtension(dest),
                RelPath = ToRel(dest),
                Format = ext.TrimStart('.').ToLowerInvariant(),
                Hash = hash
            };
            _music.Add(item);
            ReadDurationAsync(dest, item);
            await PersistAsync();
            return (true, $"已导入音乐「{item.Name}」", item);
        }
        catch (Exception ex)
        {
            App.WriteLog("MaterialLibrary.ImportMusic -> " + ex);
            return (false, "导入失败：" + ex.Message, null);
        }
    }

    public async Task<(bool ok, string message, BackgroundItem? item)> ImportBackgroundAsync(string srcPath)
    {
        try
        {
            await EnsureLoadedAsync();
            var ext = Path.GetExtension(srcPath);
            if (!ImageExts.Contains(ext, StringComparer.OrdinalIgnoreCase)
                && !VideoExts.Contains(ext, StringComparer.OrdinalIgnoreCase))
                return (false, "不支持的背景格式（JPG/PNG/MP4）", null);
            var hash = HashFile(srcPath);
            var dup = _backgrounds.FirstOrDefault(b => b.HashPath == hash);
            if (dup is not null)
                return (true, $"背景库中已有「{dup.Name}」，未重复保存", dup);

            Directory.CreateDirectory(BgRoot);
            var dest = UniqueDest(srcPath, BgRoot);
            File.Copy(srcPath, dest);
            var format = ext.TrimStart('.').ToLowerInvariant();
            var isVideo = "." + format is ".mp4" or ".mov" or ".m4v" or ".webm";
            var item = new BackgroundItem
            {
                Name = Path.GetFileNameWithoutExtension(dest),
                RelPath = ToRel(dest),
                Format = format,
                HashPath = hash
            };
            _backgrounds.Add(item);
            if (isVideo) ExtractVideoThumbAsync(dest, item);
            await PersistAsync();
            return (true, $"已导入背景「{item.Name}」", item);
        }
        catch (Exception ex)
        {
            App.WriteLog("MaterialLibrary.ImportBackground -> " + ex);
            return (false, "导入失败：" + ex.Message, null);
        }
    }

    /// <summary>缩略图：图片返回自身绝对路径；MP4 返回已提取的首帧 PNG（未成功返回 null）。</summary>
    public Task<string?> ThumbnailAbsAsync(BackgroundItem item)
    {
        // 视频哈希、首帧提取都较重，放后台执行，避免阻塞 UI 线程
        return Task.Run(async () =>
        {
            await EnsureLoadedAsync();
            if (item is null) return null;
            if (!string.IsNullOrWhiteSpace(item.ThumbRelPath)
                && File.Exists(ResolveAbs(item.ThumbRelPath)))
                return ResolveAbs(item.ThumbRelPath);
            if (ImageExts.Contains("." + item.Format, StringComparer.OrdinalIgnoreCase))
                return ResolveAbs(item.RelPath);
            if (string.IsNullOrEmpty(item.HashPath))
                item.HashPath = HashFile(ResolveAbs(item.RelPath));
            var framepath = await ExtractVideoFrameAsync(ResolveAbs(item.RelPath));
            if (!string.IsNullOrEmpty(framepath))
            {
                item.ThumbRelPath = ToRel(framepath);
                _ = PersistAsync();
                return framepath;
            }
            return null;
        });
    }

    // ==================== 重命名 / 删除 ====================

    public async Task<bool> RenameMusicAsync(string id, string name)
    {
        var it = _music.FirstOrDefault(m => m.Id == id);
        if (it is null) return false;
        it.Name = string.IsNullOrWhiteSpace(name) ? it.Name : name.Trim();
        await PersistAsync();
        return true;
    }

    public async Task<bool> DeleteMusicAsync(string id)
    {
        var it = _music.FirstOrDefault(m => m.Id == id);
        if (it is null) return false;
        TryDelete(ResolveAbs(it.RelPath));
        if (_assign.LeftClickId == id) _assign.LeftClickId = "";
        if (_assign.RightClickId == id) _assign.RightClickId = "";
        if (_assign.KeyPressId == id) _assign.KeyPressId = "";
        _assign.BgmList.Remove(id);
        _music.Remove(it);
        await PersistAsync();
        await PersistAssignAsync();
        return true;
    }

    public async Task<bool> RenameBackgroundAsync(string id, string name)
    {
        var it = _backgrounds.FirstOrDefault(b => b.Id == id);
        if (it is null) return false;
        it.Name = string.IsNullOrWhiteSpace(name) ? it.Name : name.Trim();
        await PersistAsync();
        return true;
    }

    public async Task<bool> DeleteBackgroundAsync(string id)
    {
        var it = _backgrounds.FirstOrDefault(b => b.Id == id);
        if (it is null) return false;
        TryDelete(ResolveAbs(it.RelPath));
        if (!string.IsNullOrWhiteSpace(it.ThumbRelPath)) TryDelete(ResolveAbs(it.ThumbRelPath));
        _backgrounds.Remove(it);
        await PersistAsync();
        return true;
    }

    // ==================== 声音分配 ====================

    public Task PersistAssignAsync() => _store.Save(AssignKey, _assign);

    public async Task<MusicItem?> FindMusicAsync(string id)
    {
        await EnsureLoadedAsync();
        return string.IsNullOrEmpty(id) ? null : _music.FirstOrDefault(m => m.Id == id);
    }

    public async Task<BackgroundItem?> FindBackgroundAsync(string id)
    {
        await EnsureLoadedAsync();
        return string.IsNullOrEmpty(id) ? null : _backgrounds.FirstOrDefault(b => b.Id == id);
    }

    public string DurationLabel(int ms) =>
        ms <= 0 ? "--:--" : $"{(int)(ms / 60000)}:{((ms / 1000) % 60):00}";

    // ==================== 内部工具 ====================

    private async Task PersistAsync()
    {
        try { await _store.Save(MusicKey, _music); }
        catch (Exception ex) { App.WriteLog("MaterialLibrary.Persist music -> " + ex); }
        try { await _store.Save(BgKey, _backgrounds); }
        catch (Exception ex) { App.WriteLog("MaterialLibrary.Persist bg -> " + ex); }
    }

    private static string UniqueDest(string srcPath, string dir)
    {
        var name = Path.GetFileName(srcPath);
        var dest = Path.Combine(dir, name);
        if (!File.Exists(dest)) return dest;
        var stem = Path.GetFileNameWithoutExtension(name);
        var ext = Path.GetExtension(name);
        for (var i = 2; ; i++)
        {
            var candidate = Path.Combine(dir, $"{stem} ({i}){ext}");
            if (!File.Exists(candidate)) return candidate;
        }
    }

    private static string HashFile(string path)
    {
        try
        {
            using var sha = SHA1.Create();
            using var fs = File.OpenRead(path);
            return Convert.ToHexString(sha.ComputeHash(fs)).ToLowerInvariant();
        }
        catch
        {
            return Guid.NewGuid().ToString("N");
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private void ReadDurationAsync(string absPath, MusicItem item)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                item.DurationMs = await ReadDurationMsAsync(absPath);
                await PersistAsync();
            }
            catch (Exception ex)
            {
                App.WriteLog("MaterialLibrary.ReadDuration -> " + ex.Message);
            }
        });
    }

    private void ExtractVideoThumbAsync(string absPath, BackgroundItem item)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var frame = await ExtractVideoFrameAsync(absPath);
                if (string.IsNullOrEmpty(frame)) return;
                item.ThumbRelPath = ToRel(frame);
                await PersistAsync();
            }
            catch (Exception ex)
            {
                App.WriteLog("MaterialLibrary.ExtractVideoThumb -> " + ex.Message);
            }
        });
    }

#if WINDOWS
    private static async Task<int> ReadDurationMsAsync(string absPath)
    {
        var player = new Windows.Media.Playback.MediaPlayer();
        try
        {
            var tcs = new TaskCompletionSource<int>();
            player.MediaOpened += (_, _) =>
            {
                try
                {
                    var d = player.NaturalDuration;
                    tcs.TrySetResult(d > TimeSpan.Zero ? (int)d.TotalMilliseconds : 0);
                }
                catch { tcs.TrySetResult(0); }
                try { player.Dispose(); } catch { }
            };
            player.MediaFailed += (_, _) => { tcs.TrySetResult(0); try { player.Dispose(); } catch { } };
            player.Source = Windows.Media.Core.MediaSource.CreateFromUri(new Uri(absPath));
            return await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch { return 0; }
        finally { try { player.Dispose(); } catch { } }
    }

    private static async Task<string?> ExtractVideoFrameAsync(string absPath)
    {
        try
        {
            if (!File.Exists(absPath)) return null;
            var hash = HashFile(absPath);
            var thumbDir = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(absPath))!, ".thumbs");
            Directory.CreateDirectory(thumbDir);
            var dest = Path.Combine(thumbDir, hash + ".png");
            if (File.Exists(dest)) return dest;

            var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(absPath);
            var clip = await Windows.Media.Editing.MediaClip.CreateFromFileAsync(file);
            var comp = new Windows.Media.Editing.MediaComposition();
            comp.Clips.Add(clip);
            using (var thumb = await comp.GetThumbnailAsync(TimeSpan.Zero, 360, 210, Windows.Media.Editing.VideoFramePrecision.NearestFrame))
            {
                if (thumb is null) return null;
                thumb.Seek(0);
                using var fs = File.Create(dest);
                using var s = thumb.AsStreamForRead();
                await s.CopyToAsync(fs);
            }
            comp.Clips.Clear();
            return dest;
        }
        catch (Exception ex)
        {
            App.WriteLog("MaterialLibrary.ExtractVideoFrame -> " + ex.Message);
            return null;
        }
    }
#else
    private static Task<int> ReadDurationMsAsync(string absPath) => Task.FromResult(0);
    private static Task<string?> ExtractVideoFrameAsync(string absPath) => Task.FromResult<string?>(null);
#endif
}