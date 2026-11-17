namespace WarmAsBefore.Services;

/// <summary>
/// 语音韵律分析结果：响度（dBFS）、基频（Hz）、节奏起伏（0~1）与建议情绪。
/// <para>
/// <see cref="LoudnessDb"/> 为 20·log10(RMS)，满刻度正弦约 0 dB，静音约 -120 dB；
/// <see cref="PitchHz"/> 为过零率 + 自相关估出的基频，噪声/清音或无基频时为 0；
/// <see cref="RhythmVariance"/> 为帧间能量的变异系数（标准差/均值），钳制到 0~1，越大表示节奏起伏越剧烈；
/// <see cref="SuggestedEmotion"/> 由确定性规则给出（兴奋/紧张/平静/低落/中性/静音），不含任何随机性。
/// </para>
/// </summary>
public sealed class VoiceProsodyResult
{
    /// <summary>响度，单位 dBFS，范围约 [-120, 0]。</summary>
    public double LoudnessDb { get; set; } = -120;

    /// <summary>估计基频（Hz）；判不清时为 0。</summary>
    public double PitchHz { get; set; }

    /// <summary>帧间能量起伏程度，范围 [0, 1]。</summary>
    public double RhythmVariance { get; set; }

    /// <summary>按确定性规则给出的情绪建议（中文标签）。</summary>
    public string SuggestedEmotion { get; set; } = "静音";
}

/// <summary>
/// 语音韵律情绪服务：纯数学、无 NuGet 依赖、纯函数、不抛异常。
/// <para>
/// 输入约定：16-bit 有符号小端 PCM、单声道（若手头是别的格式需先转换）。
/// 三条核心指标：
/// <list type="bullet">
/// <item><b>响度</b>：逐样本 RMS → 20·log10 转 dBFS，钳制到 [-120, 0]。</item>
/// <item><b>基频</b>：先用过零率（ZCR）筛掉清音/噪声，再在 60~400 Hz 对应 lag 上做归一化自相关，
/// 取相关峰值对应的 lag 换算成 Hz，低相关则判为无基频（0）。</item>
/// <item><b>节奏</b>：把音频切成 20 ms 小帧求各帧线性 RMS，取其变异系数（标准差/均值）作为起伏度，
/// 对整体音量不敏感，钳制到 [0, 1]。</item>
/// </list>
/// </para>
/// <para>
/// <b>情绪判定规则（确定性、可解释）：</b>
/// <list type="number">
/// <item>响度≥0.65 且音调≥0.55：高响度高音调 → <c>兴奋</c>；若同时节奏起伏≥0.45 → <c>紧张</c>。</item>
/// <item>响度≤0.30 且节奏起伏≤0.25：低响度低变化 → <c>平静</c>；若响度≤0.15 → <c>低落</c>。</item>
/// <item>其余情况 → <c>中性</c>；全静音（无有效采样）→ <c>静音</c>。</item>
/// </list>
/// 其中“响度”是把 dBFS 从 [-60,0] 线性映射到 [0,1]，“音调”是把 Hz 从 [80,500] 映射到 [0,1]。
/// </para>
/// <para>
/// 所有公开入口都对空输入、异常采样率、奇数字节长度等边界做了钳制，任何情况下都返回安全结果而非抛出异常。
/// </para>
/// </summary>
public sealed class VoiceProsodyService
{
    /// <summary>采样率无效（≤0）时采用的兜底值。</summary>
    private const int DefaultSampleRate = 16000;

    /// <summary>节奏分析的内部帧长（毫秒）。</summary>
    private const double FrameLengthMs = 20.0;

    /// <summary>过零率高于此阈值视为清音/噪声，不估基频。</summary>
    private const double VoicedZcrThreshold = 0.35;

    /// <summary>归一化自相关低于此阈值视为无基频。</summary>
    private const double VoicedCorrelationThreshold = 0.60;

    /// <summary>
    /// 分析一段 PCM：算响度、基频、节奏并给出情绪建议。
    /// </summary>
    /// <param name="pcm">16-bit 小端单声道 PCM 字节；null / 过短视为静音。</param>
    /// <param name="sampleRate">采样率；≤0 时按 16000 处理。</param>
    public VoiceProsodyResult Analyze(byte[]? pcm, int sampleRate)
    {
        try
        {
            if (sampleRate <= 0) sampleRate = DefaultSampleRate;
            var samples = ToSamples(pcm);
            if (samples.Length < 2) return Silence();

            var rms = ComputeRms(samples, 0, samples.Length);
            var loudnessDb = ToDb(rms);
            var pitchHz = EstimatePitch(samples, sampleRate);
            var rhythm = ComputeRhythmVariance(samples, sampleRate);
            var emotion = SuggestEmotion(loudnessDb, pitchHz, rhythm);

            return new VoiceProsodyResult
            {
                LoudnessDb = ClampDb(loudnessDb),
                PitchHz = Math.Clamp(pitchHz, 0, 1000),
                RhythmVariance = Clamp01(rhythm),
                SuggestedEmotion = emotion,
            };
        }
        catch
        {
            // 纯函数承诺：任何意外都退化为静音结果，绝不向调用方抛异常。
            return Silence();
        }
    }

    /// <summary>
    /// 逐帧（可理解为逐音节）分析一组 PCM 帧后聚合成整体韵律结果。
    /// <para>
    /// 每帧单独算响度与基频，能量序列的变异系数作为整体 <see cref="VoiceProsodyResult.RhythmVariance"/>；
    /// 整体响度取各帧 dB 的算术平均，整体基频取有效帧基频的中位数（忽略判为清音的 0 值）。
    /// </para>
    /// </summary>
    /// <param name="frames">帧列表；null / 空列表视为静音。</param>
    /// <param name="sampleRate">采样率；≤0 时按 16000 处理。</param>
    public VoiceProsodyResult AnalyzeFrames(IReadOnlyList<byte[]>? frames, int sampleRate)
    {
        try
        {
            if (sampleRate <= 0) sampleRate = DefaultSampleRate;
            if (frames == null || frames.Count == 0) return Silence();

            var loudnessLevels = new List<double>(frames.Count);
            var pitches = new List<double>(frames.Count);
            var energies = new List<double>(frames.Count);

            foreach (var frame in frames)
            {
                if (frame is null || frame.Length < 2) continue;
                var one = Analyze(frame, sampleRate);
                loudnessLevels.Add(one.LoudnessDb);
                if (one.PitchHz > 0) pitches.Add(one.PitchHz);
                // 由 dB 反推线性 RMS，作为该帧能量参与帧间起伏计算。
                energies.Add(DbToLinear(one.LoudnessDb));
            }

            if (loudnessLevels.Count == 0) return Silence();

            var avgDb = loudnessLevels.Average();
            var medianPitch = pitches.Count == 0 ? 0 : Median(pitches);
            var rhythm = ComputeVariation(energies);
            var emotion = SuggestEmotion(avgDb, medianPitch, rhythm);

            return new VoiceProsodyResult
            {
                LoudnessDb = ClampDb(avgDb),
                PitchHz = Math.Clamp(medianPitch, 0, 1000),
                RhythmVariance = Clamp01(rhythm),
                SuggestedEmotion = emotion,
            };
        }
        catch
        {
            return Silence();
        }
    }

    #region 指标计算

    /// <summary>把 16-bit 小端 PCM 字节转成归一化前的 short 采样；奇数长度丢弃最后一个字节。</summary>
    private static short[] ToSamples(byte[]? pcm)
    {
        if (pcm is null || pcm.Length < 2) return Array.Empty<short>();
        var count = pcm.Length / 2;
        var samples = new short[count];
        for (var i = 0; i < count; i++)
            samples[i] = BitConverter.ToInt16(pcm, i * 2);
        return samples;
    }

    /// <summary>计算 [start, start+count) 区间的 RMS，结果归一化到 [0,1]（除以 32768）。</summary>
    private static double ComputeRms(short[] samples, int start, int count)
    {
        if (count <= 0) return 0;
        var sum = 0.0;
        var end = Math.Min(start + count, samples.Length);
        for (var i = start; i < end; i++)
        {
            var v = samples[i] / 32768.0;
            sum += v * v;
        }
        return Math.Sqrt(sum / Math.Max(1, end - start));
    }

    /// <summary>RMS → dBFS，下限 -120 dB。</summary>
    private static double ToDb(double rms)
        => 20.0 * Math.Log10(Math.Max(rms, 1e-6));

    /// <summary>dBFS → 线性 RMS（AnalyzeFrames 聚合时用）。</summary>
    private static double DbToLinear(double db)
        => Math.Pow(10.0, ClampDb(db) / 20.0);

    /// <summary>过零率：相邻采样符号翻转次数 / 间隔数，范围 [0,1]。</summary>
    private static double ZeroCrossingRate(short[] samples)
    {
        if (samples.Length < 2) return 0;
        var zc = 0;
        for (var i = 1; i < samples.Length; i++)
            if ((samples[i - 1] >= 0) != (samples[i] >= 0)) zc++;
        return (double)zc / (samples.Length - 1);
    }

    /// <summary>
    /// 基频估计：过零率筛清音 + 归一化自相关找周期。
    /// <para>
    /// 只在人声常见 60~400 Hz 对应的 lag 区间搜索；用平方和前缀和把归一化做到 O(1)，
    /// 整体复杂度 O(lagRange × n)。相关峰值低于阈值或 lag 非法时返回 0（无基频）。
    /// </para>
    /// </summary>
    private static double EstimatePitch(short[] samples, int sampleRate)
    {
        if (samples.Length < 8) return 0;
        if (ZeroCrossingRate(samples) > VoicedZcrThreshold) return 0; // 清音/噪声

        var minLag = Math.Max(2, sampleRate / 400);
        var maxLag = Math.Min(samples.Length - 1, sampleRate / 60);
        if (maxLag <= minLag) return 0;

        // 限制参与相关的样本数，避免长音频出现 O(n²) 卡顿。
        var n = Math.Min(samples.Length, maxLag * 3);
        var prefix = new double[n + 1];
        for (var i = 0; i < n; i++)
        {
            var v = samples[i] / 32768.0;
            prefix[i + 1] = prefix[i] + v * v;
        }
        if (prefix[n] <= 1e-9) return 0; // 全静音

        var bestScore = double.NegativeInfinity;
        var bestLag = 0;
        for (var lag = minLag; lag <= maxLag; lag++)
        {
            var corr = 0.0;
            for (var i = 0; i + lag < n; i++)
                corr += (samples[i] / 32768.0) * (samples[i + lag] / 32768.0);

            // 两段能量的几何平均，做归一化（利用前缀和 O(1) 取值）。
            var e1 = prefix[n - lag];                 // i = 0 .. n-lag-1
            var e2 = prefix[n] - prefix[lag];         // j = lag .. n-1
            var norm = Math.Sqrt(e1 * e2);
            if (norm <= 1e-9) continue;

            var score = corr / norm;
            if (score > bestScore) { bestScore = score; bestLag = lag; }
        }

        if (bestLag == 0 || bestScore < VoicedCorrelationThreshold) return 0;
        return (double)sampleRate / bestLag;
    }

    /// <summary>
    /// 节奏起伏：把音频切成 20 ms 帧求各帧线性 RMS，再取变异系数（标准差/均值）。
    /// 变异系数对整体音量不敏感，比裸方差更适合“平静 vs 起伏”的判定。
    /// </summary>
    private static double ComputeRhythmVariance(short[] samples, int sampleRate)
    {
        var frameLen = Math.Max(16, (int)(sampleRate * FrameLengthMs / 1000.0));
        var energies = new List<double>();
        for (var start = 0; start < samples.Length; start += frameLen)
        {
            var len = Math.Min(frameLen, samples.Length - start);
            if (len < 16) break; // 过短残帧忽略
            energies.Add(ComputeRms(samples, start, len));
        }
        return ComputeVariation(energies);
    }

    /// <summary>计算能量序列的变异系数并钳制到 [0,1]；样本不足或均值≈0 时返回 0。</summary>
    private static double ComputeVariation(IReadOnlyList<double> energies)
    {
        if (energies.Count < 2) return 0;
        var mean = 0.0;
        foreach (var e in energies) mean += e;
        mean /= energies.Count;
        if (mean <= 1e-6) return 0;

        var sumSq = 0.0;
        foreach (var e in energies) { var d = e - mean; sumSq += d * d; }
        var sd = Math.Sqrt(sumSq / energies.Count);
        return Clamp01(sd / mean);
    }

    #endregion

    #region 情绪规则与工具

    /// <summary>
    /// 确定性情绪规则（无随机、无机器学习）：
    /// 响度从 [-60,0] dB 映射为 [0,1]，音调从 [80,500] Hz 映射为 [0,1]；
    /// 高响度高音调 → 兴奋（起伏大则紧张），低响度低变化 → 平静（极低则低落），其余中性。
    /// </summary>
    private static string SuggestEmotion(double loudnessDb, double pitchHz, double rhythmVariance)
    {
        var loud = Clamp01((loudnessDb + 60.0) / 60.0);
        var pitch = pitchHz <= 0 ? 0 : Clamp01((pitchHz - 80.0) / 420.0);
        var rhythm = Clamp01(rhythmVariance);

        if (loud >= 0.65 && pitch >= 0.55)
            return rhythm >= 0.45 ? "紧张" : "兴奋";
        if (loud <= 0.30 && rhythm <= 0.25)
            return loud <= 0.15 ? "低落" : "平静";
        return "中性";
    }

    /// <summary>安静/无效输入的安全结果。</summary>
    private static VoiceProsodyResult Silence() => new()
    {
        LoudnessDb = -120,
        PitchHz = 0,
        RhythmVariance = 0,
        SuggestedEmotion = "静音",
    };

    /// <summary>把 dBFS 钳制到 [-120,0]，并挡掉 NaN/Infinity。</summary>
    private static double ClampDb(double db)
        => double.IsFinite(db) ? Math.Clamp(db, -120, 0) : -120;

    /// <summary>钳制到 [0,1]，并挡掉 NaN/Infinity。</summary>
    private static double Clamp01(double v)
        => double.IsFinite(v) ? Math.Clamp(v, 0, 1) : 0;

    /// <summary>取中位数（先排序，奇数取中、偶数取均值）。</summary>
    private static double Median(List<double> values)
    {
        if (values.Count == 0) return 0;
        var sorted = new List<double>(values);
        sorted.Sort();
        var mid = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2.0;
    }

    #endregion
}
