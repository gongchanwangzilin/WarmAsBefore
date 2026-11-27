namespace WarmAsBefore.Services;

using WarmAsBefore.DesignSystem.Theme;

/// <summary>界面小动静的五种。语义只有一句：按下、确认、返回、提示，外加一声摩擦。</summary>
public enum UiSoundKind
{
    /// <summary>玻璃轻击：手指/鼠标落到可点的东西上。</summary>
    Tap = 0,
    /// <summary>选择确认：上行两音，事情办成了。</summary>
    Confirm = 1,
    /// <summary>返回/取消：下行一音。</summary>
    Back = 2,
    /// <summary>提示：柔和的一声「叮」。</summary>
    Notice = 3,
    /// <summary>
    /// 玻璃摩擦：两块板擦过去的一下。**厚度不同，声音不同** ——
    /// 薄（上下栏）偏高偏短，厚（弹窗）偏低、尾巴长一点（见 <see cref="UiSoundSynth.Friction"/>）。
    /// </summary>
    Friction = 4
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
///
/// 摩擦音是这一版新加的一族（<see cref="Friction"/>）：带通噪声 + 一点点低频撞击。
/// 它不是一个固定的声音，而是**按厚度生成**的 —— 音高、时长、尾巴都跟着
/// <see cref="GlassTokens.ThicknessOf"/> 走，所以「厚的那块板擦过去，声音更沉更长」这件事
/// 和卡片上那道更宽的倒角是同一个数的两个投影，不是两张表。
/// 同一个档位、同一个厚度永远合成出同一段噪声（种子由档位与厚度算出来，不是随机）。
/// </summary>
public static class UiSoundSynth
{
    public const int SampleRate = 44100;

    /// <summary>这个音效对应的 wav 文件名（缓存键）。摩擦音按厚度分档，见 <see cref="FrictionFileName"/>。</summary>
    public static string FileNameOf(UiSoundKind kind) => kind switch
    {
        UiSoundKind.Tap => "ui-tap.wav",
        UiSoundKind.Confirm => "ui-confirm.wav",
        UiSoundKind.Back => "ui-back.wav",
        UiSoundKind.Friction => "ui-friction-card-56.wav",
        _ => "ui-notice.wav"
    };

    public static byte[] Wav(UiSoundKind kind) => ToWav(Samples(kind));

    /// <summary>单声道浮点采样（-1..1）。</summary>
    public static float[] Samples(UiSoundKind kind) => kind switch
    {
        UiSoundKind.Tap => Tap(),
        UiSoundKind.Confirm => Confirm(),
        UiSoundKind.Back => Back(),
        UiSoundKind.Friction => Friction(GlassKind.Card),
        _ => Notice()
    };

    /// <summary>
    /// 玻璃摩擦音的 wav 缓存名。厚度量化到 1% 进文件名 ——
    /// 高度一改就换一颗文件，缓存里不会留下「旧的薄声音配新的厚板」。
    /// </summary>
    public static string FrictionFileName(GlassKind kind)
        => $"ui-friction-{kind.ToString().ToLowerInvariant()}-{Bucket(GlassTokens.ThicknessOf(kind)):D2}.wav";

    /// <summary>玻璃摩擦音的可播字节（与 <see cref="FrictionFileName"/> 一一对应）。</summary>
    public static byte[] FrictionWav(GlassKind kind) => ToWav(Friction(kind));

    /// <summary>
    /// 玻璃摩擦：短促的带通噪声（22~38ms），尾巴上带一点点低频撞击。
    ///
    /// 厚度 → 声音，四条都只读同一个厚度值：
    ///   音高   带通中心 3100Hz（薄）→ 1600Hz（厚）
    ///   时长   22ms（薄）→ 38ms（厚）
    ///   衰减   快（薄，一下就没了）→ 慢（厚，尾巴多留一点）
    ///   撞击   低频 118Hz（薄）→ 92Hz（厚），幅度也跟着厚度长
    /// 幅度最后归一化到同一个峰值：厚薄是音色差别，不该变成音量差别。
    /// </summary>
    public static float[] Friction(GlassKind kind)
    {
        var thick = (float)GlassTokens.ThicknessOf(kind);
        var n = SamplesFor(22 + (int)Math.Round(16 * thick));
        var buf = new float[n];

        var center = 3100 - 1500 * thick;              // 带通中心：薄的高、厚的低
        var w0 = 2 * Math.PI * center / SampleRate;
        var q = 1.6 + 1.4 * thick;                     // 越厚带越窄（更干、更像「一擦」）
        var alpha = Math.Sin(w0) / (2 * q);
        var a0 = 1 + alpha;
        var b0 = alpha / a0;
        var b2 = -alpha / a0;
        var a1 = -2 * Math.Cos(w0) / a0;
        var a2 = (1 - alpha) / a0;

        var decay = 150 - 70 * thick;                  // 每秒衰减率：薄的一下就收，厚的留尾巴
        var thump = 118 - 26 * thick;                  // 低频撞击：厚板更沉
        var attack = 0.0015 + 0.0010 * thick;

        // 固定种子：档位 + 厚度决定这一段噪声，同一块板永远是同一声摩擦
        var rnd = new Random(20261127 + ((int)kind + 1) * 977 + Bucket(thick));
        double x1 = 0, x2 = 0, y1 = 0, y2 = 0;
        var peak = 0.0;
        for (var i = 0; i < n; i++)
        {
            var t = i / (double)SampleRate;
            var x0 = rnd.NextDouble() * 2 - 1;
            var y0 = b0 * x0 + b2 * x2 - a1 * y1 - a2 * y2;
            x2 = x1; x1 = x0; y2 = y1; y1 = y0;

            var env = AttackDecay(t, attack, decay);
            var hit = Math.Sin(2 * Math.PI * thump * t) * Math.Exp(-t * 95) * (0.10 + 0.12 * thick);
            var v = y0 * env + hit;
            buf[i] = (float)v;
            peak = Math.Max(peak, Math.Abs(v));
        }

        // 归一化：厚度只改音色，不改音量（音量归 UiSoundLevel）。
        // 带通出来的噪声峰值随中心频率与 Q 漂，不归一化的话「薄的一档比厚的一档响一倍」。
        if (peak > 0.0001)
        {
            var k = (float)(0.30 / peak);
            for (var i = 0; i < n; i++) buf[i] *= k;
        }
        FadeOut(buf, 0.004);
        return buf;
    }

    /// <summary>厚度量化到百分位：文件名的缓存键与噪声种子都取它，两者永远对得上。</summary>
    private static int Bucket(double thickness) => (int)Math.Round(Math.Clamp(thickness, 0, 1) * 100);

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
