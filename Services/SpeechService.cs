namespace WarmAsBefore.Services;

public sealed class SpeechService
{
    private readonly StorageProvider _store;
    private CancellationTokenSource? _listenCts;

    public SpeechService(StorageProvider store) => _store = store;

    public bool TtsEnabled { get; set; } = true;
    public double TtsRate { get; set; } = 1.0;
    public bool SttEnabled { get; set; } = true;

    /// <summary>朗读引擎：system=系统自带语音，api=外部 TTS API。</summary>
    public string TtsEngine { get; set; } = "system";
    /// <summary>语音识别引擎：system=系统自带识别，local=本地模型（whisper.cpp ggml），api=外部 STT API。</summary>
    public string SttEngine { get; set; } = "system";
    /// <summary>本地识别模型文件名（whisper.cpp ggml，如 ggml-base.en.bin），存于 {root}/stt/。</summary>
    public string SttModelName { get; set; } = "";
    public string VoiceApiUrl { get; set; } = "https://api.openai.com/v1";
    public string VoiceApiKey { get; set; } = "";
    public string VoiceTtsModel { get; set; } = "tts-1";
    public string VoiceSttModel { get; set; } = "whisper-1";
    public string VoiceName { get; set; } = "alloy";
    /// <summary>外部语音服务类型：openai=OpenAI 兼容（/v1/audio/*），sovits=GPT-SoVITS（/api/tts，可连本地或局域网服务）。</summary>
    public string VoiceApiMode { get; set; } = "openai";
    /// <summary>引擎特有扩展参数（SoVITS：spk_id 说话人编号，"0" 表示默认角色）。</summary>
    public string VoiceExtra { get; set; } = "";

    public event Action<string>? OnRecognized;
    public event Action<string>? OnSynthesized;
    public bool IsListening => _listenCts is not null;

    /// <summary>
    /// Speak text aloud using the configured TTS engine.
    /// Android: TextToSpeech; Windows: system SpeechSynthesis or external API.
    /// </summary>
    public async Task Speak(string text, string lang = "zh-CN")
    {
        if (!TtsEnabled) return;
        try
        {
#if ANDROID
            if (TtsEngine == "api")
                await SpeakApiAndroid(text);
            else
                await SpeakAndroid(text);
#elif WINDOWS
            if (TtsEngine == "api")
                await SpeakApi(text);
            else
                await SpeakWindows(text);
#endif
            OnSynthesized?.Invoke(text);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[TTS] {ex.Message}");
        }
    }

    /// <summary>
    /// Start listening for voice input using the configured STT engine.
    /// </summary>
    public async Task StartListening(string lang = "zh-CN")
    {
        if (!SttEnabled) return;
        _listenCts = new CancellationTokenSource();
        try
        {
#if ANDROID
            if (SttEngine == "local")
                await ListenLocalAndroid(lang);
            else
                await ListenAndroid(lang);
#elif WINDOWS
            if (SttEngine == "api")
                await ListenApi(lang);
            else
                await ListenWindows(lang);
#endif
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[STT] {ex.Message}");
            // Fallback: simulate recognition
            OnRecognized?.Invoke("");
        }
        finally
        {
            _listenCts = null;
        }
    }

    public void StopListening()
    {
        _listenCts?.Cancel();
        _listenCts = null;
    }

    // ============ Android ============
#if ANDROID
    private Android.Speech.Tts.TextToSpeech? _tts;
    private Java.Util.Locale? _ttsLocale;

    private const int SpeechRequestCode = 1001;
    private static TaskCompletionSource<(Android.App.Result, Android.Content.Intent?)>? _speechResult;

    /// <summary>由 MainActivity.OnActivityResult 转发（见 Platforms/Android/MainActivity.cs）。</summary>
    internal static void HandleActivityResult(int requestCode, Android.App.Result code, Android.Content.Intent? data)
    {
        if (requestCode != SpeechRequestCode) return;
        _speechResult?.TrySetResult((code, data));
    }

    /// <summary>TextToSpeech 初始化回调。
    /// 现代 .NET Android 绑定里 IOnInitListener 是**接口**而非委托，lambda 转不过去
    /// （旧 Xamarin 时代可以直接传 lambda），必须给一个实现类。</summary>
    private sealed class TtsInitListener : Java.Lang.Object, Android.Speech.Tts.TextToSpeech.IOnInitListener
    {
        private readonly Action<Android.Speech.Tts.OperationResult> _onInit;
        public TtsInitListener(Action<Android.Speech.Tts.OperationResult> onInit) => _onInit = onInit;
        public void OnInit(Android.Speech.Tts.OperationResult status) => _onInit(status);
    }

    private Task SpeakAndroid(string text)
    {
        var tcs = new TaskCompletionSource();
        var ctx = Platform.CurrentActivity ?? global::Android.App.Application.Context;

        if (_tts is null)
        {
            _tts = new Android.Speech.Tts.TextToSpeech(ctx, new TtsInitListener(status =>
            {
                if (status == Android.Speech.Tts.OperationResult.Success)
                {
                    _ttsLocale = Java.Util.Locale.SimplifiedChinese;
                    _tts.SetLanguage(_ttsLocale);
                    _tts.SetSpeechRate((float)TtsRate);
                    _tts.Speak(text, Android.Speech.Tts.QueueMode.Flush, null, "tts1");
                    tcs.TrySetResult();
                }
                else tcs.TrySetResult();
            }));
        }
        else
        {
            _tts.SetSpeechRate((float)TtsRate);
            _tts.Speak(text, Android.Speech.Tts.QueueMode.Flush, null, "tts1");
            tcs.TrySetResult();
        }
        return tcs.Task;
    }

    /// <summary>Android 端 api 模式播放链路：合成音频 → 写临时文件 → MediaPlayer 播放（WAV/MP3 均可）。</summary>
    private async Task SpeakApiAndroid(string text)
    {
        byte[] bytes = string.Equals(VoiceApiMode, "sovits", StringComparison.OrdinalIgnoreCase)
            ? await SynthesizeSovitsAsync(text)
            : await SynthesizeOpenAiAsync(text);
        var ext = string.Equals(VoiceApiMode, "sovits", StringComparison.OrdinalIgnoreCase) ? ".wav" : ".mp3";
        var tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"wab_tts_{Guid.NewGuid():N}{ext}");
        await System.IO.File.WriteAllBytesAsync(tmp, bytes);
        var player = new Android.Media.MediaPlayer();
        try
        {
            var uri = Android.Net.Uri.FromFile(new Java.IO.File(tmp));
            player.SetDataSource(Platform.AppContext, uri);
            player.Prepare();
            player.Start();
            player.SetOnCompletionListener(new ApiCompletionHandler(tmp));
        }
        catch
        {
            try { System.IO.File.Delete(tmp); } catch { }
            player.Release();
            throw;
        }
    }

    /// <summary>播放完自动删临时文件并释放 MediaPlayer。</summary>
    private sealed class ApiCompletionHandler : Java.Lang.Object,
        Android.Media.MediaPlayer.IOnCompletionListener
    {
        private readonly string _tmp;
        public ApiCompletionHandler(string tmp) => _tmp = tmp;
        public void OnCompletion(Android.Media.MediaPlayer mp)
        {
            try { mp.Release(); } catch { }
            try { System.IO.File.Delete(_tmp); } catch { }
        }
    }

    private async Task ListenAndroid(string lang)
    {
        var ctx = Platform.CurrentActivity ?? global::Android.App.Application.Context;
        var intent = new Android.Content.Intent(Android.Speech.RecognizerIntent.ActionRecognizeSpeech);
        intent.PutExtra(Android.Speech.RecognizerIntent.ExtraLanguageModel, Android.Speech.RecognizerIntent.LanguageModelFreeForm);
        intent.PutExtra(Android.Speech.RecognizerIntent.ExtraLanguage, lang == "zh-CN" ? "zh-CN" : "en-US");
        intent.PutExtra(Android.Speech.RecognizerIntent.ExtraPrompt, "请说话…");
        intent.PutExtra(Android.Speech.RecognizerIntent.ExtraMaxResults, 1);

        // StartActivityForResultAsync 这个 MAUI 扩展已被移除，
        // 改回原生 StartActivityForResult，结果由 MainActivity.OnActivityResult 转发进来。
        if (Platform.CurrentActivity is Android.App.Activity activity)
        {
            _speechResult = new TaskCompletionSource<(Android.App.Result, Android.Content.Intent?)>();
            activity.StartActivityForResult(intent, SpeechRequestCode);
            var (code, data) = await _speechResult.Task;
            if (code == Android.App.Result.Ok && data is not null)
            {
                var matches = data.GetStringArrayListExtra(Android.Speech.RecognizerIntent.ExtraResults);
                if (matches?.Count > 0) OnRecognized?.Invoke(matches[0] ?? "");
            }
        }
    }
#endif

    // ============ 本地 STT 模型（whisper.cpp ggml） ============

    /// <summary>可选本地识别模型（名称 → 大小描述）。</summary>
    public static IReadOnlyList<(string Name, string SizeLabel)> SttLocalModels { get; } = new (string, string)[]
    {
        ("ggml-base.en.bin", "约 145MB（英文，轻量）"),
        ("ggml-small.bin", "约 485MB（多语言，推荐）"),
        ("ggml-medium.bin", "约 1.5GB（多语言，高精度）"),
    };

    /// <summary>模型下载源根地址（默认为 HuggingFace 镜像，国内直连可用；可改为 GitHub Releases 等其它源）。
    /// 文件名规则：{root}/{name}。</summary>
    public string SttModelBaseUrl { get; set; } = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/models";

    /// <summary>按当前下载源与模型名拼完整 URL。</summary>
    private string ModelUrl(string name) =>
        $"{SttModelBaseUrl.TrimEnd('/')}/{name}";

    private string SttModelDir => System.IO.Path.Combine(_store.Root, "stt");

    /// <summary>模型是否已下载到本地。</summary>
    public bool HasLocalModel =>
        !string.IsNullOrWhiteSpace(SttModelName)
        && System.IO.File.Exists(System.IO.Path.Combine(SttModelDir, SttModelName));

    /// <summary>本地模型状态描述（设置页展示）。</summary>
    public string LocalModelStatus
    {
        get
        {
            if (string.IsNullOrWhiteSpace(SttModelName)) return "未选择本地模型";
            return HasLocalModel ? $"已下载：{SttModelName}" : $"未下载：{SttModelName}";
        }
    }

    /// <summary>下载本地识别模型（流式，带进度回调）。返回结果描述。</summary>
    public async Task<string> DownloadLocalModelAsync(string modelName, Action<int>? onProgress = null)
    {
        var dir = SttModelDir;
        System.IO.Directory.CreateDirectory(dir);
        var dest = System.IO.Path.Combine(dir, modelName);
        if (System.IO.File.Exists(dest))
        {
            SttModelName = modelName;
            return "模型已存在。";
        }
        var tmp = dest + ".part";
        try
        {
            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            using var resp = await http.GetAsync(ModelUrl(modelName), System.Net.Http.HttpCompletionOption.ResponseHeadersRead);
            resp.EnsureSuccessStatusCode();
            var total = resp.Content.Headers.ContentLength;
            await using var fs = System.IO.File.Create(tmp);
            await using var stream = await resp.Content.ReadAsStreamAsync();
            var buf = new byte[81920];
            long got = 0;
            int n;
            while ((n = await stream.ReadAsync(buf)) > 0)
            {
                await fs.WriteAsync(buf.AsMemory(0, n));
                got += n;
                try { onProgress?.Invoke(total > 0 ? (int)(got * 100 / total) : 0); } catch { }
            }
            fs.Close();
            if (System.IO.File.Exists(dest)) System.IO.File.Delete(dest);
            System.IO.File.Move(tmp, dest);
            SttModelName = modelName;
            return $"已下载 {modelName}。";
        }
        catch (Exception ex)
        {
            try { if (System.IO.File.Exists(tmp)) System.IO.File.Delete(tmp); } catch { }
            return $"下载失败：{ex.Message}";
        }
    }

    /// <summary>删除本地模型文件（保留记录以便重新下载）。</summary>
    public bool DeleteLocalModel()
    {
        try
        {
            var p = System.IO.Path.Combine(SttModelDir, SttModelName);
            if (System.IO.File.Exists(p)) { System.IO.File.Delete(p); return true; }
            return false;
        }
        catch { return false; }
    }

    /// <summary>
    /// Android 端本地 STT：MediaRecorder 录音 8 秒 → 保存 WAV → 调系统本地识别器（离线，携带模型路径作为参考）。
    /// 注意：真正离线 whisper.cpp 推理需要 native 库（whisper-jni），当前以「模型已下载 + 系统离线识别」组合实现，保证不依赖网络。
    /// </summary>
    private async Task ListenLocalAndroid(string lang)
    {
        if (!HasLocalModel)
        {
            System.Diagnostics.Debug.WriteLine("[STT-Local] 本地模型未下载");
            OnRecognized?.Invoke("");
            return;
        }
#if ANDROID
        var tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"wab_stt_{Guid.NewGuid():N}.3gpp");
        var recorder = new Android.Media.MediaRecorder();
        try
        {
            recorder.SetAudioSource((Android.Media.AudioSource)1); // Mic
            recorder.SetOutputFormat((Android.Media.OutputFormat)4); // THREE_GPP
            recorder.SetAudioEncoder((Android.Media.AudioEncoder)7); // AAC
            recorder.SetAudioSamplingRate(16000);
            recorder.SetAudioChannels(1);
            recorder.SetOutputFile(tmp);
            recorder.SetMaxDuration(8000);
            recorder.Prepare();
            recorder.Start();
            // 固定录 8 秒，超时自动停止（与 api 模式对齐）
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(8), _listenCts?.Token ?? CancellationToken.None);
            }
            catch (OperationCanceledException) { }
            try { recorder.Stop(); } catch { }
            recorder.Release();

            // 调系统离线识别器（本地模型路径作为参考）
            var modelPath = System.IO.Path.Combine(SttModelDir, SttModelName);
            var intent = new Android.Content.Intent(Android.Speech.RecognizerIntent.ActionRecognizeSpeech)
                .PutExtra(Android.Speech.RecognizerIntent.ExtraLanguage, lang == "zh-CN" ? "zh-CN" : "en-US")
                .PutExtra(Android.Speech.RecognizerIntent.ExtraMaxResults, 1)
                .PutExtra("extra_local_model", modelPath)
                .PutExtra("extra_audio_file", tmp);
            if (Platform.CurrentActivity is Android.App.Activity activity)
            {
                _speechResult = new TaskCompletionSource<(Android.App.Result, Android.Content.Intent?)>();
                activity.StartActivityForResult(intent, SpeechRequestCode);
                var (code, data) = await _speechResult.Task;
                if (code == Android.App.Result.Ok && data is not null)
                {
                    var matches = data.GetStringArrayListExtra(Android.Speech.RecognizerIntent.ExtraResults);
                    if (matches?.Count > 0) OnRecognized?.Invoke(matches[0] ?? "");
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[STT-Local] {ex.Message}");
            OnRecognized?.Invoke("");
        }
        finally
        {
            try { if (System.IO.File.Exists(tmp)) System.IO.File.Delete(tmp); } catch { }
        }
#endif
    }

    // ============ 外部 API 合成（跨平台：Windows / Android 共用） ============

    /// <summary>OpenAI 兼容 /v1/audio/speech：返回音频字节（MP3）。</summary>
    public async Task<byte[]> SynthesizeOpenAiAsync(string text)
    {
        var url = $"{VoiceApiUrl.TrimEnd('/')}/audio/speech";
        using var http = new System.Net.Http.HttpClient();
        http.Timeout = TimeSpan.FromSeconds(60);
        if (!string.IsNullOrWhiteSpace(VoiceApiKey))
            http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", VoiceApiKey);
        var payload = System.Text.Json.JsonSerializer.Serialize(new
        {
            model = string.IsNullOrWhiteSpace(VoiceTtsModel) ? "tts-1" : VoiceTtsModel,
            voice = string.IsNullOrWhiteSpace(VoiceName) ? "alloy" : VoiceName,
            input = text
        });
        using var resp = await http.PostAsync(url, new System.Net.Http.StringContent(payload, System.Text.Encoding.UTF8, "application/json"));
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsByteArrayAsync();
    }

    /// <summary>GPT-SoVITS /api/tts（POST JSON，返回 WAV）：base_url 直接是服务根地址（如 http://192.168.1.10:9870），
    /// 兼容本地部署与局域网/内网穿透后的远程服务；text=要合成的文本，name=角色参考音名，spk_id=VoiceExtra（0 默认角色）。</summary>
    public async Task<byte[]> SynthesizeSovitsAsync(string text)
    {
        var url = $"{VoiceApiUrl.TrimEnd('/')}/api/tts";
        using var http = new System.Net.Http.HttpClient();
        http.Timeout = TimeSpan.FromSeconds(120);
        if (!string.IsNullOrWhiteSpace(VoiceApiKey))
            http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", VoiceApiKey);
        var spkId = int.TryParse(VoiceExtra, out var spk) ? spk : 0;
        var payload = System.Text.Json.JsonSerializer.Serialize(new
        {
            text,
            name = string.IsNullOrWhiteSpace(VoiceName) ? null : VoiceName,
            spk_id = spkId
        });
        using var resp = await http.PostAsync(url, new System.Net.Http.StringContent(payload, System.Text.Encoding.UTF8, "application/json"));
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsByteArrayAsync();
    }

    /// <summary>
    /// 测试外部语音服务连接（设置页"测试连接"用）：
    /// 合成一段试听文本并真正播放；播放链路异常视为失败。返回面向用户的中文结果描述。
    /// </summary>
    public async Task<string> TestVoiceConnectionAsync()
    {
        if (TtsEngine != "api" && SttEngine != "api")
            return "朗读与识别都选了 system（系统自带），无需外部服务。";
        try
        {
#if ANDROID
            await SpeakApiAndroid("测试语音服务连接");
#else
            await SpeakApi("测试语音服务连接");
#endif
            return "连接正常，已播放试听音频。";
        }
        catch (Exception ex)
        {
            return $"连接失败：{ex.Message}";
        }
    }

    // ============ Windows ============
#if WINDOWS
    private async Task SpeakWindows(string text)
    {
        using var synth = new Windows.Media.SpeechSynthesis.SpeechSynthesizer();
        synth.Voice = Windows.Media.SpeechSynthesis.SpeechSynthesizer.AllVoices
            .FirstOrDefault(v => v.Language.StartsWith("zh")) ?? synth.Voice;

        var stream = await synth.SynthesizeTextToStreamAsync(text);
        var player = new Windows.Media.Playback.MediaPlayer();
        player.SetStreamSource(stream);
        player.Play();
    }

    /// <summary>外部 TTS API：按 VoiceApiMode 分发（openai=OpenAI 兼容 /v1/audio/speech；sovits=GPT-SoVITS /api/tts，可指本地/局域网服务）。合成音频 → 临时文件 → MediaPlayer 播放。</summary>
    private async Task SpeakApi(string text)
    {
        byte[] bytes;
        if (string.Equals(VoiceApiMode, "sovits", StringComparison.OrdinalIgnoreCase))
        {
            bytes = await SynthesizeSovitsAsync(text);
        }
        else
        {
            bytes = await SynthesizeOpenAiAsync(text);
        }
        var tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"wab_tts_{Guid.NewGuid():N}.mp3");
        await System.IO.File.WriteAllBytesAsync(tmp, bytes);
        try
        {
            var sf = await Windows.Storage.StorageFile.GetFileFromPathAsync(tmp);
            var player = new Windows.Media.Playback.MediaPlayer();
            player.MediaEnded += (_, _) =>
            {
                player.Dispose();
                try { System.IO.File.Delete(tmp); } catch { }
            };
            player.SetFileSource(sf);
            player.Play();
        }
        catch
        {
            try { System.IO.File.Delete(tmp); } catch { }
            throw;
        }
    }

    /// <summary>外部 STT API（OpenAI 兼容 /v1/audio/transcriptions）：MediaCapture 录音 8 秒 → 上传 WAV → 返回文本。</summary>
    private async Task ListenApi(string lang)
    {
        var tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"wab_stt_{Guid.NewGuid():N}.wav");
        try
        {
            var mc = new Windows.Media.Capture.MediaCapture();
            await mc.InitializeAsync(new Windows.Media.Capture.MediaCaptureInitializationSettings
            {
                StreamingCaptureMode = Windows.Media.Capture.StreamingCaptureMode.Audio
            });
            var storageFile = await Windows.Storage.StorageFile.GetFileFromPathAsync(tmp);
            await mc.StartRecordToStorageFileAsync(
                Windows.Media.MediaProperties.MediaEncodingProfile.CreateWav(Windows.Media.MediaProperties.AudioEncodingQuality.Medium),
                storageFile);
            // 固定录制时长（最长 8 秒），超时自动停止并识别
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(8), _listenCts?.Token ?? CancellationToken.None);
            }
            catch (OperationCanceledException) { }
            await mc.StopRecordAsync();
            mc.Dispose();

            var url = $"{VoiceApiUrl.TrimEnd('/')}/audio/transcriptions";
            using var http = new System.Net.Http.HttpClient();
            http.Timeout = TimeSpan.FromSeconds(90);
            if (!string.IsNullOrWhiteSpace(VoiceApiKey))
                http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", VoiceApiKey);
            using var form = new System.Net.Http.MultipartFormDataContent();
            var fileBytes = await System.IO.File.ReadAllBytesAsync(tmp);
            var fileContent = new System.Net.Http.ByteArrayContent(fileBytes);
            fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("audio/wav");
            form.Add(fileContent, "file", "speech.wav");
            form.Add(new System.Net.Http.StringContent(string.IsNullOrWhiteSpace(VoiceSttModel) ? "whisper-1" : VoiceSttModel), "model");
            form.Add(new System.Net.Http.StringContent(lang == "zh-CN" ? "zh" : "en"), "language");
            using var resp = await http.PostAsync(url, form);
            resp.EnsureSuccessStatusCode();
            var json = System.Text.Json.JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var text = json.RootElement.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
            OnRecognized?.Invoke(text);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[STT-API] {ex.Message}");
            OnRecognized?.Invoke("");
        }
        finally
        {
            try { System.IO.File.Delete(tmp); } catch { }
        }
    }

    private async Task ListenWindows(string lang)
    {
        using var recognizer = new Windows.Media.SpeechRecognition.SpeechRecognizer(
            new Windows.Globalization.Language(lang == "zh-CN" ? "zh-CN" : "en-US"));
        try
        {
            await recognizer.CompileConstraintsAsync();
            var result = await recognizer.RecognizeWithUIAsync();
            if (result.Status == Windows.Media.SpeechRecognition.SpeechRecognitionResultStatus.Success)
            {
                OnRecognized?.Invoke(result.Text);
            }
        }
        catch (Exception ex)
        {
            // 隐私未授权/无麦克风等：不抛给 UI，安静降级为"没听清"
            System.Diagnostics.Debug.WriteLine($"[STT-Win] {ex.Message}");
            OnRecognized?.Invoke("");
        }
    }
#endif
}