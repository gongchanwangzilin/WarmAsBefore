using System.Text.Json;

#if WAB_ONNX
// 仅当定义了 WAB_ONNX 编译符号且项目引用了 Microsoft.ML.OnnxRuntime 时，
// 下面这些 using 与真正的推理实现才会参与编译；默认（未装 NuGet）时整段被编译器剔除，
// 因此本项目当前不会因缺少 OnnxRuntime 而编译失败。
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SkiaSharp;
#endif

namespace WarmAsBefore.Services;

/// <summary>
/// 人脸分析结果：情绪概率字典、主导情绪与年龄估计，以及本次分析的可用性状态。
/// <para>
/// <see cref="Emotions"/> 的键为情绪名（如 neutral / happy / sad ...），值为 0~1 的置信度；
/// <see cref="DominantEmotion"/> 取置信度最高者；<see cref="AgeEstimate"/> 为估计年龄（岁），
/// 模型未给出年龄输出时为 null。
/// </para>
/// <para>
/// <see cref="Status"/> 为 "ok" 表示真正跑通了模型；其余值（"模型未安装" / "未授权" /
/// "图像数据为空" 等）表示降级，此时 <see cref="Success"/> 为 false，业务方应据此给用户人话提示。
/// </para>
/// </summary>
public sealed class FaceAnalysisResult
{
    /// <summary>情绪名 → 置信度（0~1）。</summary>
    public Dictionary<string, double> Emotions { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>置信度最高的情绪名；无可信结果时为空串。</summary>
    public string DominantEmotion { get; set; } = "";

    /// <summary>估计年龄（岁）；模型未提供年龄输出或推理失败时为 null。</summary>
    public double? AgeEstimate { get; set; }

    /// <summary>本次状态："ok" 或某种降级原因（人话，可直接展示）。</summary>
    public string Status { get; set; } = "模型未安装";

    /// <summary>是否真正得到了模型推理结果。</summary>
    public bool Success => string.Equals(Status, "ok", StringComparison.OrdinalIgnoreCase);

    /// <summary>构造一个降级结果（带原因）。</summary>
    public static FaceAnalysisResult NotAvailable(string reason) => new() { Status = reason ?? "不可用" };
}

/// <summary>
/// 本地人脸情绪/年龄分析服务（ONNX 推理，纯离线，不上传任何图像）。
/// <para>
/// <b>启用 ONNX 推理的办法（当前仓库未安装相关 NuGet，默认走降级占位）：</b>
/// <list type="number">
/// <item>给 WarmAsBefore.csproj 添加包引用：<c>Microsoft.ML.OnnxRuntime</c>
/// （移动端按需换用对应平台的 OnnxRuntime 包）；图像预处理依赖已存在的 <c>SkiaSharp</c>。</item>
/// <item>在 csproj 的 &lt;DefineConstants&gt; 中追加 <c>WAB_ONNX</c>，例如
/// &lt;DefineConstants&gt;$(DefineConstants);WAB_ONNX&lt;/DefineConstants&gt;。</item>
/// <item>把情绪模型放到 <c>App.RootDirectory/Models/face_emotion.onnx</c>；
/// 若有独立年龄模型，另放 <c>App.RootDirectory/Models/face_age.onnx</c>（可选）。</item>
/// </list>
/// 未满足以上条件时，所有 <c>Analyze*</c> 入口都会优雅返回带“模型未安装”状态的
/// <see cref="FaceAnalysisResult"/>，绝不抛异常、绝不崩溃。
/// </para>
/// <para>
/// <b>模型输入输出约定：</b>推理实现按典型 FER 表情模型示范——单输入 1×1×48×48 灰度归一化张量，
/// 第一路输出为 7 类 softmax（neutral/happy/sad/angry/fearful/disgusted/surprised），
/// 可选第二路为年龄回归值。换成自己的模型时只需调整 <c>EmotionLabels</c>、输入名、
/// 张量形状与输出解析，其余流程无需改动。
/// </para>
/// <para>
/// <b>按存档授权：</b>摄像头/人脸分析须逐存档授权，状态落在独立 JSON
/// <c>App.RootDirectory/wab_vision_settings.json</c>，不碰 UserSettings，默认未授权。
/// </para>
/// </summary>
public sealed class FaceAnalysisService
{
    private static readonly object SettingsLock = new();
    private static VisionSettings? _settingsCache;

#if WAB_ONNX
    // 下面常量仅在启用 ONNX 时存在；按实际模型修改即可。
    private const string EmotionInputName = "input";
    private const int EmotionInputSize = 48;
    private static readonly string[] EmotionLabels =
        { "neutral", "happy", "sad", "angry", "fearful", "disgusted", "surprised" };
#endif

    /// <summary>独立授权配置文件路径（与 UserSettings 完全隔离）。</summary>
    public static string SettingsPath => Path.Combine(App.RootDirectory, "wab_vision_settings.json");

    /// <summary>情绪模型约定路径：{App.RootDirectory}/Models/face_emotion.onnx。</summary>
    public static string ModelPath => Path.Combine(App.RootDirectory, "Models", "face_emotion.onnx");

    /// <summary>可选年龄模型约定路径：{App.RootDirectory}/Models/face_age.onnx。</summary>
    public static string AgeModelPath => Path.Combine(App.RootDirectory, "Models", "face_age.onnx");

    #region 授权状态（独立 JSON，默认未授权）

    /// <summary>指定存档是否已授权摄像头/人脸分析。未授权（含空 saveId）一律返回 false。</summary>
    public bool IsAuthorized(string? saveId)
    {
        if (string.IsNullOrWhiteSpace(saveId)) return false;
        try
        {
            var s = LoadSettings();
            return s.AuthorizedBySave.TryGetValue(saveId!, out var ok) && ok;
        }
        catch (Exception ex) { SafeLog("FaceAnalysisService.IsAuthorized -> " + ex.Message); return false; }
    }

    /// <summary>给指定存档授予人脸分析权限，并立即落盘。</summary>
    public void Grant(string saveId)
    {
        if (string.IsNullOrWhiteSpace(saveId)) return;
        try
        {
            LoadSettings().AuthorizedBySave[saveId] = true;
            SaveSettings();
        }
        catch (Exception ex) { SafeLog("FaceAnalysisService.Grant -> " + ex.Message); }
    }

    /// <summary>撤销指定存档的人脸分析权限，并立即落盘。</summary>
    public void Revoke(string saveId)
    {
        if (string.IsNullOrWhiteSpace(saveId)) return;
        try
        {
            LoadSettings().AuthorizedBySave[saveId] = false;
            SaveSettings();
        }
        catch (Exception ex) { SafeLog("FaceAnalysisService.Revoke -> " + ex.Message); }
    }

    /// <summary>
    /// 启动前是否应询问授权：仅当“未授权且尚未询问过”时为 true。
    /// 调用方弹窗后无论用户同意与否都应调用 <see cref="MarkAsked"/> 记账，
    /// 避免每次启动反复骚扰。
    /// </summary>
    public bool ShouldAskBeforeStart(string? saveId)
    {
        if (string.IsNullOrWhiteSpace(saveId)) return false;
        try
        {
            var s = LoadSettings();
            var authorized = s.AuthorizedBySave.TryGetValue(saveId!, out var a) && a;
            var asked = s.AskedBySave.TryGetValue(saveId!, out var q) && q;
            return !authorized && !asked;
        }
        catch (Exception ex) { SafeLog("FaceAnalysisService.ShouldAskBeforeStart -> " + ex.Message); return false; }
    }

    /// <summary>标记“已就本存档询问过授权”，并立即落盘。</summary>
    public void MarkAsked(string saveId)
    {
        if (string.IsNullOrWhiteSpace(saveId)) return;
        try
        {
            LoadSettings().AskedBySave[saveId] = true;
            SaveSettings();
        }
        catch (Exception ex) { SafeLog("FaceAnalysisService.MarkAsked -> " + ex.Message); }
    }

    /// <summary>摄像头总开关（独立配置，默认关闭）。读失败时返回 false。</summary>
    public bool CameraEnabled
    {
        get
        {
            try { return LoadSettings().CameraEnabled; }
            catch (Exception ex) { SafeLog("FaceAnalysisService.CameraEnabled.get -> " + ex.Message); return false; }
        }
        set
        {
            try { LoadSettings().CameraEnabled = value; SaveSettings(); }
            catch (Exception ex) { SafeLog("FaceAnalysisService.CameraEnabled.set -> " + ex.Message); }
        }
    }

    #endregion

    #region 图像 → 情绪/年龄分析入口

    /// <summary>分析本地图片文件。文件不存在 / 未授权 / 无模型时返回降级状态，不抛异常。</summary>
    public FaceAnalysisResult Analyze(string? imagePath, string? saveId = null)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
                return FaceAnalysisResult.NotAvailable("图像文件不存在");
            return AnalyzeCore(File.ReadAllBytes(imagePath!), saveId);
        }
        catch (Exception ex) { SafeLog("FaceAnalysisService.Analyze(path) -> " + ex.Message); return FaceAnalysisResult.NotAvailable("读取图像失败：" + ex.Message); }
    }

    /// <summary>分析内存中的图像字节（摄像头抓帧、socket 画面等走这里）。</summary>
    public FaceAnalysisResult Analyze(byte[]? imageBytes, string? saveId = null)
    {
        try
        {
            if (imageBytes is null || imageBytes.Length == 0)
                return FaceAnalysisResult.NotAvailable("图像数据为空");
            return AnalyzeCore(imageBytes, saveId);
        }
        catch (Exception ex) { SafeLog("FaceAnalysisService.Analyze(bytes) -> " + ex.Message); return FaceAnalysisResult.NotAvailable("分析失败：" + ex.Message); }
    }

    /// <summary>分析图像流；读取失败返回降级状态，不抛异常。</summary>
    public FaceAnalysisResult Analyze(Stream? imageStream, string? saveId = null)
    {
        try
        {
            if (imageStream is null || !imageStream.CanRead)
                return FaceAnalysisResult.NotAvailable("图像流不可读");
            using var ms = new MemoryStream();
            imageStream.CopyTo(ms);
            return AnalyzeCore(ms.ToArray(), saveId);
        }
        catch (Exception ex) { SafeLog("FaceAnalysisService.Analyze(stream) -> " + ex.Message); return FaceAnalysisResult.NotAvailable("读取图像流失败：" + ex.Message); }
    }

    /// <summary>字节数组的异步入口（推理是 CPU 密集，丢到线程池，避免卡 UI）。</summary>
    public Task<FaceAnalysisResult> AnalyzeAsync(byte[]? imageBytes, string? saveId = null)
        => Task.Run(() => Analyze(imageBytes, saveId));

    /// <summary>
    /// 统一核心流程：先按存档校验授权，再校验模型文件，最后才尝试推理。
    /// 任一环节不满足都返回带人话原因的降级结果，全程不向调用方抛异常。
    /// </summary>
    private FaceAnalysisResult AnalyzeCore(byte[] bytes, string? saveId)
    {
        try
        {
            // 传了 saveId 就按存档授权把关；不传则视为调用方已自行确认（如仅分析本地图库照片）。
            if (!string.IsNullOrWhiteSpace(saveId) && !IsAuthorized(saveId))
                return FaceAnalysisResult.NotAvailable("该存档未授权人脸分析");

            if (!File.Exists(ModelPath))
                return FaceAnalysisResult.NotAvailable("模型未安装（缺少 face_emotion.onnx）");

#if WAB_ONNX
            return RunOnnxInference(bytes);
#else
            // 降级占位：当前未引用 Microsoft.ML.OnnxRuntime / 未定义 WAB_ONNX，
            // 推理代码未参与编译，统一返回“模型未安装”。详见类注释的启用步骤。
            return FaceAnalysisResult.NotAvailable("模型未安装（未启用本地 ONNX 推理）");
#endif
        }
        catch (Exception ex) { SafeLog("FaceAnalysisService.AnalyzeCore -> " + ex.Message); return FaceAnalysisResult.NotAvailable("分析异常：" + ex.Message); }
    }

#if WAB_ONNX
    /// <summary>
    /// 真正跑 ONNX 推理（仅 WAB_ONNX 下编译）。按典型 FER 模型示范，按自己的模型调整标签与形状。
    /// </summary>
    private static FaceAnalysisResult RunOnnxInference(byte[] bytes)
    {
        using var session = new InferenceSession(ModelPath);

        // 1) 预处理：解码 → 缩放到 48×48 → 灰度 → 归一化到 [0,1] → 布局 1×1×48×48。
        var gray = PreprocessGray(bytes, EmotionInputSize);
        var tensor = new DenseTensor<float>(gray, new[] { 1, 1, EmotionInputSize, EmotionInputSize });
        var inputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor(EmotionInputName, tensor) };

        // 2) 推理。
        using var outputs = session.Run(inputs);

        // 3) 解析第一路输出为情绪概率。
        var emotions = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var o in outputs)
        {
            if (o.Value is not IEnumerable<float> values) continue;
            var arr = values.ToArray();
            if (arr.Length == 0) continue;
            for (var i = 0; i < EmotionLabels.Length && i < arr.Length; i++)
                emotions[EmotionLabels[i]] = Math.Clamp(arr[i], 0, 1);
            break;
        }

        // 归一化概率（softmax 模型一般已归一，这里兜底钳制到总和 1）。
        var sum = emotions.Values.Sum();
        if (sum > 0)
            foreach (var k in emotions.Keys.ToList()) emotions[k] = emotions[k] / sum;

        var dominant = emotions.Count == 0
            ? ""
            : emotions.Aggregate((a, b) => a.Value >= b.Value ? a : b).Key;

        // 4) 可选年龄模型：存在 face_age.onnx 时再跑一次取回归值。
        double? age = null;
        if (File.Exists(AgeModelPath))
        {
            try
            {
                using var ageSession = new InferenceSession(AgeModelPath);
                using var ageOutputs = ageSession.Run(inputs);
                foreach (var o in ageOutputs)
                {
                    if (o.Value is IEnumerable<float> av && av.Any())
                    {
                        var v = av.First();
                        if (double.IsFinite(v)) age = Math.Clamp(v, 0, 120);
                        break;
                    }
                }
            }
            catch (Exception ex) { SafeLog("FaceAnalysisService age model -> " + ex.Message); }
        }

        return new FaceAnalysisResult
        {
            Status = "ok",
            Emotions = emotions,
            DominantEmotion = dominant,
            AgeEstimate = age,
        };
    }

    /// <summary>解码图像字节并转成 size×size 灰度浮点数组（0~1）。失败时返回全 0 数组。</summary>
    private static float[] PreprocessGray(byte[] bytes, int size)
    {
        var result = new float[size * size];
        try
        {
            using var bitmap = SKBitmap.Decode(bytes);
            if (bitmap is null) return result;
            using var resized = bitmap.Resize(
                new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul),
                SKFilterQuality.Medium);
            var src = resized ?? bitmap;
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var c = src.GetPixel(x, y);
                var gray = (0.299f * c.Red + 0.587f * c.Green + 0.114f * c.Blue) / 255f;
                result[y * size + x] = Math.Clamp(gray, 0f, 1f);
            }
        }
        catch (Exception ex) { SafeLog("FaceAnalysisService.PreprocessGray -> " + ex.Message); }
        return result;
    }
#endif

    #endregion

    #region 配置读写与日志

    /// <summary>独立 JSON 的强类型结构。</summary>
    private sealed class VisionSettings
    {
        /// <summary>存档 ID → 是否已授权人脸分析。</summary>
        public Dictionary<string, bool> AuthorizedBySave { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>存档 ID → 是否已询问过授权（避免反复弹窗）。</summary>
        public Dictionary<string, bool> AskedBySave { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>摄像头总开关。</summary>
        public bool CameraEnabled { get; set; }
    }

    /// <summary>读取配置（带内存缓存与锁）；文件损坏/读失败一律返回默认结构。</summary>
    private static VisionSettings LoadSettings()
    {
        lock (SettingsLock)
        {
            if (_settingsCache is not null) return _settingsCache;

            var s = new VisionSettings();
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var json = File.ReadAllText(SettingsPath);
                    var loaded = JsonSerializer.Deserialize<VisionSettings>(json);
                    if (loaded is not null) s = loaded;
                }
            }
            catch (Exception ex) { SafeLog("FaceAnalysisService.LoadSettings -> " + ex.Message); s = new VisionSettings(); }

            // System.Text.Json 反序列化出的字典区分大小写，这里统一规整成不区分大小写。
            s.AuthorizedBySave = NormalizeMap(s.AuthorizedBySave);
            s.AskedBySave = NormalizeMap(s.AskedBySave);
            _settingsCache = s;
            return s;
        }
    }

    /// <summary>写入配置（带锁）；目录不存在自动创建，写失败只记日志不影响主流程。</summary>
    private static void SaveSettings()
    {
        lock (SettingsLock)
        {
            try
            {
                var dir = Path.GetDirectoryName(SettingsPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                var json = JsonSerializer.Serialize(_settingsCache ?? new VisionSettings(),
                    new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsPath, json);
            }
            catch (Exception ex) { SafeLog("FaceAnalysisService.SaveSettings -> " + ex.Message); }
        }
    }

    /// <summary>把字典规整为不区分大小写、丢弃空键的新字典。</summary>
    private static Dictionary<string, bool> NormalizeMap(Dictionary<string, bool>? src)
    {
        var d = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        if (src is null) return d;
        foreach (var kv in src)
            if (!string.IsNullOrEmpty(kv.Key)) d[kv.Key] = kv.Value;
        return d;
    }

    /// <summary>日志失败绝不影响主流程。</summary>
    private static void SafeLog(string message)
    {
        try { App.WriteLog(message); } catch { /* 忽略：本服务保证不崩 */ }
    }

    #endregion
}
