using System.Text;
using WarmAsBefore.Models;
using WarmAsBefore.Modules.ApiManager;
using WarmAsBefore.Services;

namespace WarmAsBefore.Modules.AiChat;

/// <summary>
/// ChatEngine 的附件一摊：把附件清单渲染成给模型看的文本块、把音频附件转成
/// OpenAI 兼容的 input_audio 内容块、从磁盘路径登记一个附件、按扩展名判类型。
/// 全是「附件 ↔ 请求体」的纯转换，不碰会话、端点与记忆，所以单独成文件。
/// </summary>
public sealed partial class ChatEngine
{
    /// <summary>
    /// 把附件清单渲染成一段给模型看的文本块。文本附件给出正文（截断），
    /// 二进制附件只给文件名/类型/大小/路径。没有附件时返回空串。
    /// </summary>
    public static string BuildAttachmentBlock(IReadOnlyList<ChatAttachment>? attachments)
        => BuildAttachmentBlock(attachments, false, null);

    /// <summary>兼容重载：只产出文本，不收集多模态块（真正发送路径用三参版本）。</summary>
    public static string BuildAttachmentBlock(IReadOnlyList<ChatAttachment>? attachments, bool inlineAudioAsInputBlock)
        => BuildAttachmentBlock(attachments, inlineAudioAsInputBlock, null);

    /// <summary>
    /// v1.60 音频直送模式：AudioInputMode=direct 且有端点支持听音频时，
    /// 音频附件不再只写文件名，而是读文件→base64→构造 OpenAI 兼容的 input_audio 内容块，
    /// 由调用方通过 <see cref="ApiGateway.AttachContentBlocks"/> 挂到这条 user 消息上，
    /// 让 Wire 把 content 拼成 [text, ...input_audio] 真多模态数组。
    /// 收集到（成功构造）的块会加进 <paramref name="contentBlocks"/>；文件缺失/过大/编码异常则回退旧描述。
    /// </summary>
    internal static string BuildAttachmentBlock(IReadOnlyList<ChatAttachment>? attachments,
        bool inlineAudioAsInputBlock, List<object>? contentBlocks)
    {
        if (attachments is null || attachments.Count == 0) return "";
        var sb = new System.Text.StringBuilder();
        sb.Append("[携带的文件]");
        foreach (var a in attachments)
        {
            if (a is null) continue;
            sb.Append('\n').Append("- ").Append(a.FileName)
              .Append("（").Append(a.Kind).Append(", ").Append(a.Size).Append(" 字节）");
            if (!string.IsNullOrEmpty(a.TextContent))
            {
                sb.Append("\n  内容：").Append(a.TextContent);
                if (a.Truncated) sb.Append("\n  （内容过长已截断）");
            }
            else if (inlineAudioAsInputBlock && a.Kind == "audio"
                     && TryBuildInputAudioObject(a) is { } audioObject)
            {
                contentBlocks?.Add(audioObject);
                sb.Append("\n  （音频已作为 input_audio 内容块直送模型）");
            }
            else
            {
                sb.Append("\n  （二进制文件，未内联内容；路径：").Append(a.Path).Append("）");
            }
        }
        return sb.ToString();
    }

    /// <summary>音频附件 → OpenAI 兼容的 input_audio 匿名内容块。任何失败返回 null 让上层回退旧描述。</summary>
    private static object? TryBuildInputAudioObject(ChatAttachment a)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(a.Path) || !File.Exists(a.Path)) return null;
            var bytes = File.ReadAllBytes(a.Path);
            if (bytes.Length == 0) return null;
            var b64 = Convert.ToBase64String(bytes);
            if (b64.Length > 2_000_000) return null;   // 太大不内联，免得一份音频把上下文吃光
            var format = Path.GetExtension(a.Path).TrimStart('.').ToLowerInvariant();
            if (format is not ("wav" or "mp3" or "ogg" or "m4a" or "flac" or "aac" or "webm" or "mp4"))
                format = "wav";
            return new { type = "input_audio", input_audio = new { data = b64, format } };
        }
        catch (Exception ex)
        {
            App.WriteLog("ChatEngine.TryBuildInputAudioObject -> " + ex.Message);
            return null;
        }
    }

    /// <summary>
    /// 是否启用音频直送：设置 AudioInputMode=direct（直送大模型，不走内部 STT）。
    /// 同时要求当前端点或轮换池里有一条声明 SupportsAudioInput；否则回退内部 STT 的旧行为。
    /// 读设置/查能力全程 try/catch，任何异常都返回 false，保持旧消息结构。
    /// </summary>
    private static bool ApiDirectAudioInline(AiEndpoint? cfg)
    {
        try
        {
            if (cfg is null) return false;
            var services = Modules.Tools.ServicesHelper.GetServices();
            var sm = services?.GetService(typeof(SettingsManager)) as SettingsManager;
            var cur = sm?.Current;
            if (cur is null) return false;
            if (!string.Equals(cur.AudioInputMode, "direct", StringComparison.OrdinalIgnoreCase))
                return false;

            // 主配置通常不带能力位（RuntimeConfigurator 只给轮换池下发），
            // 所以轮换池里有能听音频的端点也算命中，免得这个模式形同虚设。
            if (cfg.SupportsAudioInput) return true;
            var speech = services?.GetService(typeof(SpeechService)) as SpeechService;
            return speech?.ResolveCapabilityEndpoint(e => e.SupportsAudioInput) is not null;
        }
        catch (Exception ex)
        {
            App.WriteLog("ChatEngine.ApiDirectAudioInline -> " + ex.Message);
            return false;
        }
    }

    /// <summary>
    /// #10：从磁盘路径构造一个附件。文本类（txt/md/json/csv/log/xml/code 等）会读入正文并按
    /// <see cref="MaxAttachmentChars"/> 截断；图片/音频等二进制只登记元信息。
    /// 读失败也返回一个"占位附件"，不抛异常（附件不该拖垮整轮对话）。
    /// </summary>
    public static ChatAttachment BuildAttachmentFromPath(string path)
    {
        var name = System.IO.Path.GetFileName(path ?? "");
        var kind = GuessAttachmentKind(name);
        long size = 0;
        try { if (System.IO.File.Exists(path)) size = new System.IO.FileInfo(path).Length; }
        catch { }
        string? text = null;
        var truncated = false;
        if (kind == "text" && !string.IsNullOrEmpty(path))
        {
            try
            {
                var raw = System.IO.File.ReadAllText(path);
                if (raw.Length > MaxAttachmentChars)
                {
                    raw = raw.Substring(0, MaxAttachmentChars);
                    truncated = true;
                }
                text = raw;
            }
            catch (Exception ex)
            {
                App.WriteLog("ChatAttachment.Read -> " + ex.Message);
                text = "（文件读取失败）";
            }
        }
        return new ChatAttachment
        {
            FileName = name,
            Path = path ?? "",
            Kind = kind,
            Size = size,
            TextContent = text,
            Truncated = truncated
        };
    }

    /// <summary>按扩展名粗判附件类型：text / image / audio / video / other。</summary>
    private static string GuessAttachmentKind(string fileName)
    {
        var ext = (System.IO.Path.GetExtension(fileName ?? "") ?? "").ToLowerInvariant();
        return ext switch
        {
            ".txt" or ".md" or ".json" or ".csv" or ".log" or ".xml" or ".yml" or ".yaml"
                or ".ini" or ".cfg" or ".cs" or ".js" or ".ts" or ".py" or ".java" or ".html"
                or ".css" or ".sh" or ".bat" or ".sql" or ".xaml" => "text",
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".bmp" or ".svg" => "image",
            ".mp3" or ".wav" or ".ogg" or ".m4a" or ".flac" or ".aac" => "audio",
            ".mp4" or ".mov" or ".avi" or ".mkv" or ".webm" => "video",
            _ => "other"
        };
    }
}
