namespace WarmAsBefore.Services;

/// <summary>界面小动静的四种。语义只有一句：按下、确认、返回、提示。</summary>
public enum UiSoundKind
{
    /// <summary>玻璃轻击：手指/鼠标落到可点的东西上。</summary>
    Tap = 0,
    /// <summary>选择确认：上行两音，事情办成了。</summary>
    Confirm = 1,
    /// <summary>返回/取消：下行一音。</summary>
    Back = 2,
    /// <summary>提示：柔和的一声「叮」。</summary>
    Notice = 3
}

/// <summary>
/// 界面音效的合成器：不下载、不打包外部素材，几个正弦叠一叠就有质感。
///
/// 纯函数、无 IO：<see cref="Wav"/> 直接给出可以写盘的 wav 字节，
/// <see cref="Samples"/> 给自测与调参用。采样率 44.1kHz / 单声道 / 16bit，
/// 手机与桌面都认，体积每个不到 20KB。
///
/// 设计上的两个讲究：
/// · 每一条都带 2~12ms 的起音与指数衰减，避免「啪」一下的爆音（爆音比没有声音更难听）。
/// · 玻璃轻击用两个高频正弦（≈2.4k / 3.1k）叠出「叮——」的金属余味，
///   不是单频正弦那种电子表提示音。
/// </summary>
public static class UiSoundSynth
{
    public const int SampleRate = 44100;

    /// <summary>这个音效对应的 wav 文件名（缓存键）。</summary>
    public static string FileNameOf(UiSoundKind kind) => kind switch
    {
        UiSoundKind.Tap => "ui-tap.wav",
        UiSoundKind.Confirm => "ui-confirm.wav",
        UiSoundKind.Back => "ui-back.wav",
        _ => "ui-notice.wav"
    };

    public static byte[] Wav(UiSoundKind kind) => ToWav(Samples(kind));

    /// <summary>单声道浮点采样（-1..1）。</summary>
    public static float[] Samples(UiSoundKind kind) => kind switch
    {
        UiSoundKind.Tap => Tap(),
        UiSoundKind.Confirm => Confirm(),
        UiSoundKind.Back => Back(),
        _ => Notice()
    };

    private static float[] Tap()
    {
        var ms = 46;
        var n = SamplesFor(ms);
        var buf = new float[n];
        for (var i = 0; i < n; i++)
        {
            var t = i / (double)SampleRate;
            var env = AttackDecay(t, 0.0012, 120);
            var a = Math.Sin(2 * Math.PI * 2400 * t);
            var b = Math.Sin(2 * Math.PI * 3100 * t) * 0.55;
            var c = Math.Sin(2 * Math.PI * 1850 * t) * 0.18;   // 低一点的一层给一点厚度
            buf[i] = (float)((a + b + c) * env * 0.22);
        }
        FadeOut(buf, 0.004);
        return buf;
    }

    private static float[] Confirm()
    {
        var n = SamplesFor(150);
        var buf = new float[n];
        var firstEnd = SamplesFor(62);
        for (var i = 0; i < n; i++)
        {
            var t = i / (double)SampleRate;
            double v;
            if (i < firstEnd)
                v = Math.Sin(2 * Math.PI * 784 * t) * AttackDecay(t, 0.002, 42) * 0.30;
            else
            {
                var t2 = (i - firstEnd) / (double)SampleRate;
                v = Math.Sin(2 * Math.PI * 1175 * t2) * AttackDecay(t2, 0.002, 30) * 0.30;
                v += Math.Sin(2 * Math.PI * 2350 * t2) * AttackDecay(t2, 0.002, 38) * 0.06;
            }
            buf[i] = (float)v;
        }
        FadeOut(buf, 0.006);
        return buf;
    }

    private static float[] Back()
    {
        var ms = 110;
        var n = SamplesFor(ms);
        var buf = new float[n];
        var phase = 0.0;
        for (var i = 0; i < n; i++)
        {
            var p = i / (double)n;
            var freq = 660 - 240 * p;                 // 660 → 420 Hz：一步往下走
            phase += 2 * Math.PI * freq / SampleRate;
            var env = AttackDecay(i / (double)SampleRate, 0.002, 24);
            buf[i] = (float)(Math.Sin(phase) * env * 0.26);
        }
        FadeOut(buf, 0.006);
        return buf;
    }

    private static float[] Notice()
    {
        var ms = 190;
        var n = SamplesFor(ms);
        var buf = new float[n];
        for (var i = 0; i < n; i++)
        {
            var t = i / (double)SampleRate;
            var env = AttackDecay(t, 0.012, 12);
            var v = Math.Sin(2 * Math.PI * 1568 * t)
                    + Math.Sin(2 * Math.PI * 3136 * t) * 0.34
                    + Math.Sin(2 * Math.PI * 4704 * t) * 0.12;
            buf[i] = (float)(v * env * 0.16);
        }
        FadeOut(buf, 0.010);
        return buf;
    }

    private static int SamplesFor(int ms) => Math.Max(1, SampleRate * ms / 1000);

    /// <summary>起音线性、之后指数衰减：起音够短就不会糊，衰减够快就不会互相盖住。</summary>
    private static double AttackDecay(double t, double attack, double decay)
        => t < attack ? t / attack : Math.Exp(-(t - attack) * decay);

    /// <summary>收尾 4~10ms 渐弱，消掉最后一个采样被硬切出来的咔哒声。</summary>
    private static void FadeOut(float[] buf, double seconds)
    {
        var n = Math.Min(buf.Length, (int)(seconds * SampleRate));
        for (var i = 0; i < n; i++)
        {
            var k = 1.0 - i / (double)n;
            buf[buf.Length - 1 - i] *= (float)k;
        }
    }

    private static byte[] ToWav(float[] samples)
    {
        var dataBytes = samples.Length * 2;
        var bytes = new byte[44 + dataBytes];
        WriteAscii(bytes, 0, "RIFF");
        WriteInt(bytes, 4, 36 + dataBytes);
        WriteAscii(bytes, 8, "WAVE");
        WriteAscii(bytes, 12, "fmt ");
        WriteInt(bytes, 16, 16);          // PCM 头长度
        WriteShort(bytes, 20, 1);         // PCM
        WriteShort(bytes, 22, 1);         // 单声道
        WriteInt(bytes, 24, SampleRate);
        WriteInt(bytes, 28, SampleRate * 2);
        WriteShort(bytes, 32, 2);
        WriteShort(bytes, 34, 16);
        WriteAscii(bytes, 36, "data");
        WriteInt(bytes, 40, dataBytes);

        for (var i = 0; i < samples.Length; i++)
        {
            var v = Math.Clamp(samples[i], -1f, 1f);
            var s = (short)Math.Round(v * short.MaxValue);
            bytes[44 + i * 2] = (byte)(s & 0xFF);
            bytes[45 + i * 2] = (byte)((s >> 8) & 0xFF);
        }
        return bytes;
    }

    private static void WriteAscii(byte[] b, int at, string s)
    {
        for (var i = 0; i < s.Length; i++) b[at + i] = (byte)s[i];
    }

    private static void WriteInt(byte[] b, int at, int v)
    {
        b[at] = (byte)(v & 0xFF);
        b[at + 1] = (byte)((v >> 8) & 0xFF);
        b[at + 2] = (byte)((v >> 16) & 0xFF);
        b[at + 3] = (byte)((v >> 24) & 0xFF);
    }

    private static void WriteShort(byte[] b, int at, int v)
    {
        b[at] = (byte)(v & 0xFF);
        b[at + 1] = (byte)((v >> 8) & 0xFF);
    }
}
