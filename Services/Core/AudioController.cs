using System.Text.Json;
using WarmAsBefore.Models;

namespace WarmAsBefore.Services;


public sealed class AudioController
{
    private readonly MaterialLibrary _materials;
    private double _bgm = 0.7;
    private double _sfx = 0.8;
    private bool _bgmMuted;

    public AudioController(MaterialLibrary materials) => _materials = materials;

    public double Bgm
    {
        get => _bgm;
        set { _bgm = Math.Clamp(value, 0, 1); ApplyBgmVolume(); }
    }

    public double Sfx
    {
        get => _sfx;
        set => _sfx = Math.Clamp(value, 0, 1);
    }

    /// <summary>按分配方案播放：left=左键音，right=右键音，key=按键音；lightning/rain/shake/bounce 为事件小音效卡槽。</summary>
    public void PlayAssigned(string kind)
    {
        _ = PlayAssignedAsync(kind);
    }

    private async Task PlayAssignedAsync(string kind)
    {
        try
        {
            await _materials.EnsureLoadedAsync();
            var a = _materials.Assign;
            var id = kind switch
            {
                "left" => a.LeftClickId,
                "right" => a.RightClickId,
                "key" => a.KeyPressId,
                "lightning" => a.LightningId,
                "rain" => a.RainId,
                "shake" => a.ShakeId,
                "bounce" => a.BounceId,
                "purr" => a.PurrId,
                _ => a.KeyPressId
            };
            if (string.IsNullOrEmpty(id)) return;
            var item = await _materials.FindMusicAsync(id);
            if (item is null) return;
            PlaySfxFile(_materials.ResolveAbs(item.RelPath));
        }
        catch (Exception ex)
        {
            App.WriteLog("AudioController.PlayAssigned -> " + ex.Message);
        }
    }

    /// <summary>
    /// 事件音效卡槽是否有装填（用于打雷/雨事件的「音效库优先、配置路径回落」判断）。
    /// </summary>
    public async Task<bool> HasAssignedAsync(string kind)
    {
        try
        {
            await _materials.EnsureLoadedAsync();
            var a = _materials.Assign;
            var id = kind switch
            {
                "left" => a.LeftClickId,
                "right" => a.RightClickId,
                "key" => a.KeyPressId,
                "lightning" => a.LightningId,
                "rain" => a.RainId,
                "shake" => a.ShakeId,
                "bounce" => a.BounceId,
                "purr" => a.PurrId,
                _ => ""
            };
            return !string.IsNullOrEmpty(id);
        }
        catch { return false; }
    }

    public void PlaySfxFile(string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return;
#if WINDOWS
        try
        {
            var p = new Windows.Media.Playback.MediaPlayer { Volume = _sfx };
            p.MediaEnded += (_, _) => { try { p.Dispose(); } catch { } };
            p.MediaFailed += (_, _) => { try { p.Dispose(); } catch { } };
            p.Source = Windows.Media.Core.MediaSource.CreateFromUri(new Uri(filePath));
            p.Play();
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[SFX] {filePath}: {ex.Message}"); }
#elif ANDROID
        try
        {
            lock (_androidAudioLock)
            {
                try { _androidSfxPlayer?.Stop(); _androidSfxPlayer?.Release(); } catch { }
                var p = new Android.Media.MediaPlayer();
                var uri = Android.Net.Uri.FromFile(new Java.IO.File(filePath));
                p.SetDataSource(Platform.AppContext, uri);
                p.Prepare();
                p.SetVolume((float)_sfx, (float)_sfx);
                p.Start();
                p.SetOnCompletionListener(new AndroidSfxCompletion(p));
                _androidSfxPlayer = p;
            }
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[SFX] {filePath}: {ex.Message}"); }
#else
        System.Diagnostics.Debug.WriteLine($"[SFX] {filePath}");
#endif
    }

#if ANDROID
    /// <summary>Android 音效播完自动释放 MediaPlayer。</summary>
    private sealed class AndroidSfxCompletion : Java.Lang.Object,
        Android.Media.MediaPlayer.IOnCompletionListener
    {
        private readonly Android.Media.MediaPlayer _mp;
        public AndroidSfxCompletion(Android.Media.MediaPlayer mp) => _mp = mp;
        public void OnCompletion(Android.Media.MediaPlayer m)
        {
            try { m.Release(); } catch { }
        }
    }

    /// <summary>Android 背景音乐：播完当前曲自动推进到列表下一首（与 Windows 端 AdvanceInner 对齐）。</summary>
    private sealed class AndroidBgmCompletion : Java.Lang.Object,
        Android.Media.MediaPlayer.IOnCompletionListener
    {
        private readonly AudioController _owner;
        public AndroidBgmCompletion(AudioController owner) => _owner = owner;
        public void OnCompletion(Android.Media.MediaPlayer m)
        {
            try { _owner.BgmNext(); } catch { }
        }
    }
#endif

    // ==================== 背景音乐轮播 ====================

    /// <summary>启动/刷新背景音乐轮播（内容来自声音分配的 BgmList，未分配则不播放）。</summary>
    public async Task StartBgmRotationAsync()
    {
        try
        {
            await _materials.EnsureLoadedAsync();
            var a = _materials.Assign;
            var files = new List<string>();
            foreach (var id in a.BgmList)
            {
                var item = await _materials.FindMusicAsync(id);
                if (item is null) continue;
                var abs = _materials.ResolveAbs(item.RelPath);
                if (File.Exists(abs)) files.Add(abs);
            }
            SetBgmPlaylist(files);
        }
        catch (Exception ex)
        {
            App.WriteLog("AudioController.StartBgmRotation -> " + ex.Message);
        }
    }

    private readonly List<string> _bgmFiles = new();
    private int _bgmIndex;

#if WINDOWS
    private Windows.Media.Playback.MediaPlayer? _bgmPlayer;
#else
    private object? _bgmPlayer;
#endif

#if ANDROID
    private Android.Media.MediaPlayer? _androidBgmPlayer;
    private Android.Media.MediaPlayer? _androidSfxPlayer;
    private readonly object _androidAudioLock = new();
#endif

    public void SetBgmPlaylist(IReadOnlyList<string> files)
    {
        lock (_bgmFiles)
        {
            _bgmFiles.Clear();
            if (files is not null) _bgmFiles.AddRange(files);
            _bgmIndex = 0;
        }
        if (_bgmFiles.Count == 0)
        {
            StopBgm();
            return;
        }
        PlayBgmFile(_bgmFiles[0]);
    }

    public void BgmNext()
    {
        lock (_bgmFiles)
        {
            if (_bgmFiles.Count == 0) return;
            _bgmIndex = (_bgmIndex + 1) % _bgmFiles.Count;
            PlayBgmFile(_bgmFiles[_bgmIndex]);
        }
    }

    public void BgmPrev()
    {
        lock (_bgmFiles)
        {
            if (_bgmFiles.Count == 0) return;
            _bgmIndex = (_bgmIndex - 1 + _bgmFiles.Count) % _bgmFiles.Count;
            PlayBgmFile(_bgmFiles[_bgmIndex]);
        }
    }

    /// <summary>CG 等全屏页：静音当前背景音乐；退出后调用恢复。</summary>
    public void SetBgmMuted(bool muted)
    {
        _bgmMuted = muted;
#if WINDOWS
        if (_bgmPlayer is not null) _bgmPlayer.IsMuted = muted;
#elif ANDROID
        try { lock (_androidAudioLock) { if (_androidBgmPlayer is not null) { var v = muted ? 0f : (float)_bgm; _androidBgmPlayer.SetVolume(v, v); } } } catch { }
#endif
    }

    public void StopBgm()
    {
#if WINDOWS
        try
        {
            _bgmPlayer?.Pause();
            _bgmPlayer?.Dispose();
            _bgmPlayer = null;
        }
        catch { }
#elif ANDROID
        try
        {
            lock (_androidAudioLock)
            {
                _androidBgmPlayer?.Stop();
                _androidBgmPlayer?.Release();
                _androidBgmPlayer = null;
            }
        }
        catch { }
#endif
    }

    public void StopAll()
    {
        StopBgm();
#if WINDOWS
        System.Diagnostics.Debug.WriteLine("[AUDIO] stop");
#elif ANDROID
        try
        {
            lock (_androidAudioLock)
            {
                _androidSfxPlayer?.Stop();
                _androidSfxPlayer?.Release();
                _androidSfxPlayer = null;
            }
        }
        catch { }
#endif
    }

    private void PlayBgmFile(string file)
    {
        #if WINDOWS
        try
        {
            StopBgm();
            var p = new Windows.Media.Playback.MediaPlayer { Volume = _bgm, IsMuted = _bgmMuted };
            p.MediaEnded += (_, _) => AdvanceInner(p);
            p.MediaFailed += (_, _) => AdvanceInner(p);
            p.Source = Windows.Media.Core.MediaSource.CreateFromUri(new Uri(file));
            p.Play();
            _bgmPlayer = p;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[BGM] {file}: {ex.Message}");
        }
        #elif ANDROID
        try
        {
            lock (_androidAudioLock)
            {
                _androidBgmPlayer?.Stop();
                _androidBgmPlayer?.Release();
                var p = new Android.Media.MediaPlayer();
                var uri = Android.Net.Uri.FromFile(new Java.IO.File(file));
                p.SetDataSource(Platform.AppContext, uri);
                p.Prepare();
                var bgmVol = _bgmMuted ? 0f : (float)_bgm;
                p.SetVolume(bgmVol, bgmVol);
                // 播完当前曲自动推进到列表下一首（与 Windows 端 AdvanceInner 行为对齐）
                p.SetOnCompletionListener(new AndroidBgmCompletion(this));
                p.Start();
                _androidBgmPlayer = p;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[BGM] {file}: {ex.Message}");
        }
        #else
        System.Diagnostics.Debug.WriteLine($"[BGM] {file}");
        #endif
    }

#if WINDOWS
    /// <summary>当前曲目自然结束/失败：同一播放器直接切换到下一首（避免在事件里 Dispose 自己）。</summary>
    private void AdvanceInner(Windows.Media.Playback.MediaPlayer p)
    {
        try
        {
            lock (_bgmFiles)
            {
                if (_bgmFiles.Count == 0) { p.Source = null; return; }
                _bgmIndex = (_bgmIndex + 1) % _bgmFiles.Count;
                p.Source = Windows.Media.Core.MediaSource.CreateFromUri(new Uri(_bgmFiles[_bgmIndex]));
                p.Play();
            }
        }
        catch { }
    }
#endif

    private void ApplyBgmVolume()
    {
        #if WINDOWS
        try { if (_bgmPlayer is not null) _bgmPlayer.Volume = _bgm; } catch { }
        #endif
    }
}
