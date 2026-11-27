using System.Collections.Concurrent;
using WarmAsBefore.Models;

namespace WarmAsBefore.Services;

/// <summary>
/// 界面小音效：按下 / 确认 / 返回 / 提示。
///
/// 三条硬要求，也决定了它为什么不是「每次 new 一个 Player」：
/// 1. **不占 UI 线程**：<see cref="Play"/> 只做两次判断与一次入队，合成、写盘、播放全在后台队列里跑。
///    按下按钮那一刻的主线程一点活都不多干。
/// 2. **不重复造播放器**：桌面端每种音效一个 MediaPlayer、移动端一个 SoundPool，创建一次反复用；
///    重放同一颗只把进度拨回 0，不新建实例。
/// 3. **拿不到音频设备就闭嘴**：平台调用失败只记一行日志并停止重试，绝不抛到调用方、更不弹窗。
///
/// 合成结果第一次用到时写成 wav 缓存到应用数据目录，之后只读文件。
/// 开关与音量读设置里的 <see cref="UserSettings.UiSoundEnabled"/> / <see cref="UserSettings.UiSoundLevel"/>，
/// 默认 0.5；没有设置服务时按默认值走，静态页（比如全局按钮反馈）也能响。
/// </summary>
public sealed class UiSoundService
{
    private const double DefaultVolume = 0.5;
    private const int MaxQueued = 6;

    private readonly SettingsManager? _settings;
    private readonly ConcurrentQueue<(UiSoundKind Kind, float Volume)> _queue = new();
    private readonly object _gate = new();

    private int _workerRunning;
    private long _lastAnyTicks;
    private long _lastSameTicks;
    private UiSoundKind _lastKind;
    private volatile bool _broken;

    public UiSoundService(SettingsManager? settings = null) => _settings = settings;

    /// <summary>声音总开关：关掉后一个采样都不合成、也不碰播放器。</summary>
    public bool Enabled
    {
        get => _settings?.Current.UiSoundEnabled ?? true;
        set => Apply(s => s with { UiSoundEnabled = value });
    }

    /// <summary>音量 0~1。</summary>
    public double Volume
    {
        get => Math.Clamp(_settings?.Current.UiSoundLevel ?? DefaultVolume, 0, 1);
        set => Apply(s => s with { UiSoundLevel = Math.Clamp(value, 0, 1) });
    }

    private void Apply(Func<UserSettings, UserSettings> mutate)
    {
        if (_settings is null) return;
        try
        {
            _settings.Apply(mutate(_settings.Current));
            _ = _settings.Persist();
        }
        catch (Exception ex) { App.WriteLog("UiSound.Apply -> " + ex.Message); }
    }

    /// <summary>
    /// 播一个界面音效。非阻塞；同一颗在 30ms 内连点只响一次（快速连点不该变成一串机关枪）。
    /// </summary>
    public void Play(UiSoundKind kind) => Play(kind, null);

    /// <summary>
    /// 试听：用给定的音量响一次，不经过已保存设置 —— 拖音量滑杆时设置还没落盘，
    /// 读旧值就会「拖到 20% 还是按 50% 响」。
    /// </summary>
    public void Preview(UiSoundKind kind, double volume) => Play(kind, Math.Clamp(volume, 0, 1));

    private void Play(UiSoundKind kind, double? overrideVolume)
    {
        if (_broken) return;
        var s = _settings?.Current;
        if (s is not null && !s.UiSoundEnabled) return;
        var volume = (float)Math.Clamp(overrideVolume ?? s?.UiSoundLevel ?? DefaultVolume, 0, 1);
        if (volume <= 0.001f) return;

        var now = Environment.TickCount64;
        lock (_gate)
        {
            if (now - _lastAnyTicks < 8) return;
            if (kind == _lastKind && now - _lastSameTicks < 30) return;
            if (_queue.Count >= MaxQueued) return;
            _lastAnyTicks = now;
            _lastSameTicks = now;
            _lastKind = kind;
        }
        _queue.Enqueue((kind, volume));
        if (Interlocked.Exchange(ref _workerRunning, 1) == 0)
            _ = Task.Run(DrainAsync);
    }

    private async Task DrainAsync()
    {
        try
        {
            while (_queue.TryDequeue(out var item))
            {
                try { await PlayOnceAsync(item.Kind, item.Volume); }
                catch (Exception ex)
                {
                    App.WriteLog("UiSound.Play -> " + ex.Message);
                    _broken = true;      // 设备/解码出问题就彻底闭麦，不反复刷日志
                }
            }
        }
        finally
        {
            Interlocked.Exchange(ref _workerRunning, 0);
            if (!_queue.IsEmpty && Interlocked.Exchange(ref _workerRunning, 1) == 0)
                _ = Task.Run(DrainAsync);
        }
    }

    private async Task PlayOnceAsync(UiSoundKind kind, float volume)
    {
        var path = await EnsureWavAsync(kind);
        if (path is null) return;
#if WINDOWS
        PlayWindows(kind, path, volume);
#elif ANDROID
        PlayAndroid(kind, path, volume);
#else
        await Task.CompletedTask;
#endif
    }

    /// <summary>第一次用到时合成并写盘；之后只 stat 一下文件。目录不可写就自己降级为「合成但不缓存」。</summary>
    private static async Task<string?> EnsureWavAsync(UiSoundKind kind)
    {
        try
        {
            var dir = Path.Combine(FileSystem.Current.AppDataDirectory, "UiSounds");
            var path = Path.Combine(dir, UiSoundSynth.FileNameOf(kind));
            if (File.Exists(path) && new FileInfo(path).Length > 44) return path;

            Directory.CreateDirectory(dir);
            var bytes = UiSoundSynth.Wav(kind);
            var tmp = path + ".tmp";
            await File.WriteAllBytesAsync(tmp, bytes);
            File.Move(tmp, path, overwrite: true);
            return path;
        }
        catch (Exception ex)
        {
            App.WriteLog("UiSound.EnsureWav -> " + ex.Message);
            return null;
        }
    }

#if WINDOWS
    private readonly Dictionary<UiSoundKind, Windows.Media.Playback.MediaPlayer> _players = new();

    /// <summary>
    /// 每种音效一个 MediaPlayer，来源只设一次；重放只拨回起点，不新建实例。
    /// 播放动作投回主线程：MediaPlayer/WinRT 与窗口同一个 COM 单元（工作线程里首次创建偶发 COMException），
    /// 而这里只是「设音量 + Play」两行非阻塞调用，排队一次的开销远小于几十毫秒的音效本身。
    /// </summary>
    private void PlayWindows(UiSoundKind kind, string path, float volume)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            try
            {
                if (!_players.TryGetValue(kind, out var player) || player is null)
                {
                    player = new Windows.Media.Playback.MediaPlayer
                    {
                        Volume = volume,
                        IsLoopingEnabled = false
                    };
                    player.Source = Windows.Media.Core.MediaSource.CreateFromUri(new Uri(path));
                    _players[kind] = player;
                }
                player.Volume = volume;
                try { player.PlaybackSession.Position = TimeSpan.Zero; } catch { }
                player.Play();
            }
            catch (Exception ex)
            {
                App.WriteLog("UiSound.Windows -> " + ex.Message);
                _broken = true;
            }
        });
    }
#endif

#if ANDROID
    private Android.Media.SoundPool? _pool;
    private readonly Dictionary<UiSoundKind, int> _soundIds = new();
    private readonly HashSet<int> _loaded = new();

    /// <summary>SoundPool 就是为这种几十毫秒的短音准备的：加载一次、反复 Play，不占主线程。</summary>
    private void PlayAndroid(UiSoundKind kind, string path, float volume)
    {
        if (_pool is null)
        {
            var attrs = new Android.Media.AudioAttributes.Builder()
                .SetUsage(Android.Media.AudioUsageKind.Game)!
                .SetContentType(Android.Media.AudioContentType.Sonification)!
                .Build();
            _pool = new Android.Media.SoundPool.Builder()
                .SetMaxStreams(2)!
                .SetAudioAttributes(attrs)!
                .Build();
            _pool.SetOnLoadCompleteListener(new LoadListener(this));
        }

        if (!_soundIds.TryGetValue(kind, out var id))
        {
            id = _pool.Load(path, 1);
            _soundIds[kind] = id;
            return;      // 第一次只负责加载；这一声留白，后续点击就有了
        }
        lock (_gate)
            if (!_loaded.Contains(id)) return;
        _pool.Play(id, volume, volume, 1, 0, 1f);
    }

    private void MarkLoaded(int sampleId)
    {
        lock (_gate) _loaded.Add(sampleId);
    }

    private sealed class LoadListener : Java.Lang.Object, Android.Media.SoundPool.IOnLoadCompleteListener
    {
        private readonly UiSoundService _owner;
        public LoadListener(UiSoundService owner) => _owner = owner;
        public void OnLoadComplete(Android.Media.SoundPool? soundPool, int sampleId, int status)
        {
            if (status == 0) _owner.MarkLoaded(sampleId);
        }
    }
#endif
}

/// <summary>
/// 静态入口：按钮反馈与 Toast 都在静态代码里（PressFeel、Toast），拿不到构造注入。
/// 这里只负责从 MAUI 容器取那一个单例；容器还没起来时退回一个按默认设置走的实例。
/// </summary>
public static class UiSounds
{
    private static readonly object Gate = new();
    private static UiSoundService? _service;

    public static UiSoundService Service
    {
        get
        {
            if (_service is not null) return _service;
            lock (Gate)
            {
                if (_service is not null) return _service;
                try
                {
                    var services = Microsoft.Maui.IPlatformApplication.Current?.Services;
                    _service = services?.GetService(typeof(UiSoundService)) as UiSoundService;
                }
                catch (Exception ex) { App.WriteLog("UiSounds.Resolve -> " + ex.Message); }
                return _service ??= new UiSoundService();
            }
        }
    }

    public static void Tap() => Play(UiSoundKind.Tap);
    public static void Confirm() => Play(UiSoundKind.Confirm);
    public static void Back() => Play(UiSoundKind.Back);
    public static void Notice() => Play(UiSoundKind.Notice);

    public static void Play(UiSoundKind kind)
    {
        try { Service.Play(kind); }
        catch (Exception ex) { App.WriteLog("UiSounds.Play -> " + ex.Message); }
    }

    /// <summary>试听（设置页拖音量时用）：按给定音量响，不读已保存的值。</summary>
    public static void Preview(UiSoundKind kind, double volume)
    {
        try { Service.Preview(kind, volume); }
        catch (Exception ex) { App.WriteLog("UiSounds.Preview -> " + ex.Message); }
    }
}
