namespace WarmAsBefore.Services;

public sealed class SpeechService
{
    private CancellationTokenSource? _listenCts;

    public bool TtsEnabled { get; set; } = true;
    public double TtsRate { get; set; } = 1.0;
    public bool SttEnabled { get; set; } = true;

    /// <summary>朗读引擎：system=系统自带语音，api=外部 TTS API。</summary>
    public string TtsEngine { get; set; } = "system";
    /// <summary>语音识别引擎：system=系统自带识别，api=外部 STT API。</summary>
    public string SttEngine { get; set; } = "system";
    public string VoiceApiUrl { get; set; } = "https://api.openai.com/v1";
    public string VoiceApiKey { get; set; } = "";
    public string VoiceTtsModel { get; set; } = "tts-1";
    public string VoiceSttModel { get; set; } = "whisper-1";
    public string VoiceName { get; set; } = "alloy";

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

    /// <summary>外部 TTS API（OpenAI 兼容 /v1/audio/speech）：合成 MP3 → 临时文件 → MediaPlayer 播放。</summary>
    private async Task SpeakApi(string text)
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
        var bytes = await resp.Content.ReadAsByteArrayAsync();
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