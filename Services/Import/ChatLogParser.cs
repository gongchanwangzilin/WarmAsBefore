using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WarmAsBefore.Services;

/// <summary>聊天记录来源的格式。嗅探只用于给人看与选解析器，不决定能不能导入。</summary>
public enum ChatLogFormat
{
    Unknown = 0,
    PlainText,
    WeChat,
    QqText,
    QqMht,
    Markdown,
    Json,
    Csv
}

/// <summary>一条导入的消息归属给谁。归错了比丢一条更糟，所以只有三种。</summary>
public enum ChatSpeakerRole
{
    User = 0,
    Character = 1,
    Other = 2
}

/// <summary>解析后的一条消息：时间可以缺失（很多导出只留日期），发言者与正文必须有。</summary>
public sealed record ImportedMessage
{
    public DateTime? At { get; init; }
    public string Speaker { get; init; } = "";
    public string Text { get; init; } = "";
    public string Source { get; init; } = "";
    public int Index { get; init; }
}

/// <summary>一个文件的解析结果。<see cref="Error"/> 非空时 <see cref="Messages"/> 一定是空的。</summary>
public sealed record ChatLogParseResult
{
    public ChatLogFormat Format { get; init; }
    public List<ImportedMessage> Messages { get; init; } = new();
    public int LinesSeen { get; init; }
    public int SkippedLines { get; init; }
    public string Error { get; init; } = "";
    public string Note { get; init; } = "";
    public bool Ok => Error.Length == 0 && Messages.Count > 0;
}

/// <summary>
/// 聊天记录解析：把各种导出丢进来的文本折成同一种「时间 + 发言者 + 正文」。
///
/// 为什么按内容嗅探而不是按扩展名：微信导出的 txt、QQ 导出的 mht、手写的 md、脚本吐的 json
/// 扩展名全靠用户随手改，唯一可靠的证据是文本本身长什么样。扩展名只用来提示
/// （「你说这是 mht，但里面没有 MIME 头」），不参与判定。
///
/// 纯函数：不碰 IO、不碰时间、不碰 MAUI，可以直接编进 tools 下的自测工程。
/// </summary>
public static class ChatLogParser
{
    private const string DatePart = @"(?<d>\d{4}[-/.年]\d{1,2}[-/.月]\d{1,2}日?)";
    private const string TimePart = @"(?<t>\d{1,2}:\d{2}(?::\d{2})?)";

    /// <summary>「2023-05-01 12:00:00 昵称」——微信与 QQ 文本导出最常见的一行。</summary>
    private static readonly Regex DateFirst = new(
        @"^\s*" + DatePart + @"[\sT]+" + TimePart + @"\s+(?<n>[^\r\n]{1,40}?)\s*[:：]?\s*$",
        RegexOptions.Compiled);

    /// <summary>「[2023-05-01 12:00:00] 昵称」——方括号包住时间的一类导出。</summary>
    private static readonly Regex BracketFirst = new(
        @"^\s*\[\s*" + DatePart + @"[\sT]+" + TimePart + @"\s*\]\s*(?<n>[^

]{1,40}?)\s*[:：]?\s*$",
        RegexOptions.Compiled);

    /// <summary>「昵称 2023-05-01 12:00:00」——部分导出工具把名字放前面。</summary>
    private static readonly Regex NameFirst = new(
        @"^\s*(?<n>[^\s:：\[\]]{1,30}?)\s+" + DatePart + @"\s+" + TimePart + @"\s*$",
        RegexOptions.Compiled);

    /// <summary>「[12:00:00] 昵称」——按天导出的文件只在第一条带日期，后面只带时刻。</summary>
    private static readonly Regex TimeFirst = new(
        @"^\s*\[?" + TimePart + @"\]?[\s,，]+(?<n>[^\r\n]{1,40}?)\s*[:：]?\s*$",
        RegexOptions.Compiled);

    /// <summary>没有时间戳的「昵称: 内容」。</summary>
    private static readonly Regex ColonPair = new(
        @"^\s*(?<n>[^:：\r\n]{1,20})[:：]\s*(?<t>\S.*)$",
        RegexOptions.Compiled);

    /// <summary>QQ 导出里的分隔线与信封行，不该被当成消息。</summary>
    private static readonly Regex NoiseLine = new(
        @"^\s*(={3,}|[-—*]{5,}|消息记录.*|消息分组.*|消息对象.*|日期\s*:.*)\s*$",
        RegexOptions.Compiled);

    private static readonly Regex CsvHeader = new(
        @"时间|日期|发送者|发送人|昵称|说话人|内容|消息|time|date|timestamp|sender|from|speaker|name|content|text|message|msg|body",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static string NameOf(ChatLogFormat format) => format switch
    {
        ChatLogFormat.WeChat => "微信导出的 txt",
        ChatLogFormat.QqText => "QQ 导出的 txt",
        ChatLogFormat.QqMht => "QQ 导出的 mht",
        ChatLogFormat.Markdown => "Markdown",
        ChatLogFormat.Json => "JSON",
        ChatLogFormat.Csv => "CSV",
        ChatLogFormat.PlainText => "通用文本",
        _ => "未知"
    };

    /// <summary>解析一个文件（或一段粘贴文本）。<paramref name="fileName"/> 只用于提示与去噪，不决定格式。</summary>
    public static ChatLogParseResult Parse(string? raw, string fileName = "")
    {
        var text = (raw ?? "").TrimStart('\uFEFF', '\u200B');
        if (string.IsNullOrWhiteSpace(text))
            return Fail(ChatLogFormat.Unknown, "文件是空的，没有可导入的内容。");

        var lines = SplitLines(text).Count;

        // ① MHT：QQ 导出的 .mht 是一个 MIME 多段文件，正文常常是 base64 或 quoted-printable 的 HTML
        if (LooksLikeMht(text))
        {
            var decoded = MhtText.Extract(text);
            if (string.IsNullOrWhiteSpace(decoded))
                return Fail(ChatLogFormat.QqMht,
                    "这个 mht 里没抽出文字。它可能是纯图片（截图）导出的，或者使用了本机不支持的字符编码（常见为 GBK）。" +
                    "可以先用记事本另存为 UTF-8 的 txt 再导入。", lines);
            var inner = ParseTextual(StripMarkup(decoded), fileName, ChatLogFormat.QqMht);
            return inner with { Note = "已从 mht 的 HTML 正文里抽出纯文本" };
        }

        // ② JSON：数组或 {messages:[...]}
        if (LooksLikeJson(text))
        {
            var json = ParseJson(text, fileName);
            if (json.Ok) return json;
            if (json.Error.Length > 0 && json.Messages.Count == 0)
                return json;   // 真的是 JSON，但结构不认识：把原因说清，不退回文本解析
        }

        // ③ CSV：第一行是表头，或者每行都像「时间,发送者,内容」
        if (LooksLikeCsv(text))
        {
            var csv = ParseCsv(text, fileName);
            if (csv.Ok) return csv;
        }

        var stripped = StripMarkup(text);
        var textual = ParseTextual(stripped, fileName, GuessTextFormat(text));
        if (textual.Ok) return textual;

        return Fail(ChatLogFormat.Unknown,
            "没认出聊天格式。需要至少一种：「2023-05-01 12:00:00 昵称」这样的时间+昵称行、" +
            "「昵称: 内容」这样的对话行，或者 json/csv 的「时间,发送者,内容」三列。" +
            "目前支持微信/QQ 导出的 txt/mht、通用 txt、md、json、csv。", lines);
    }

    /// <summary>
    /// 归一化：按时间推进、丢掉同一个人在同一时刻重复发的同一句话（多选文件时重叠段很常见）。
    /// 没有时间的消息跟随前一条的时间行走，不会被挤到最前面去。
    /// </summary>
    public static (List<ImportedMessage> Kept, int Dropped) Normalize(IEnumerable<ImportedMessage> messages)
    {
        var all = messages.Where(m => !string.IsNullOrWhiteSpace(m.Speaker) || !string.IsNullOrWhiteSpace(m.Text))
            .Where(m => !string.IsNullOrWhiteSpace(m.Text))
            .OrderBy(m => m.Index)
            .ToList();

        var dropped = 0;
        var kept = new List<ImportedMessage>(all.Count);
        var seen = new Dictionary<string, List<DateTime>>(StringComparer.Ordinal);
        var recentKeys = new Queue<string>();
        DateTime? cursor = null;

        foreach (var m in all)
        {
            var key = KeyOf(m);
            var at = m.At;
            var duplicate = false;
            if (at is null)
            {
                // 没有时间：只挡住紧挨着的前三条里出现过的同一句。
                // 整段都按「出现过就丢」会误删隔了很久又说的「嗯」「好」。
                if (recentKeys.Contains(key)) duplicate = true;
            }
            else if (seen.TryGetValue(key, out var stamps))
            {
                foreach (var prev in stamps)
                    if (Math.Abs((at.Value - prev).TotalMinutes) <= 5) { duplicate = true; break; }
            }
            if (duplicate) { dropped++; continue; }

            var effective = at ?? cursor;
            if (at.HasValue) cursor = at;
            if (!seen.TryGetValue(key, out var list)) seen[key] = list = new List<DateTime>();
            list.Add(effective ?? DateTime.MinValue);
            recentKeys.Enqueue(key);
            while (recentKeys.Count > 3) recentKeys.Dequeue();
            kept.Add(m with { At = effective });
        }

        var ordered = kept
            .Select((m, i) => (Message: m, Key: m.At ?? DateTime.MinValue, Index: i))
            .OrderBy(x => x.Key)
            .ThenBy(x => x.Index)
            .Select(x => x.Message)
            .ToList();
        return (ordered, dropped);
    }

    private static DateTime? ParseDate(string raw)
        => ChatLogTime.TryParse(raw, out var d) ? d.Date : null;

    private static TimeSpan? ParseClock(string raw)
        => ChatLogTime.ParseClockOnly(raw) ?? (ChatLogTime.TryParse(raw, out var t) ? t.TimeOfDay : null);

    private static string KeyOf(ImportedMessage m)
        => Norm(m.Speaker) + "\u0001" + Norm(m.Text);

    private static string Norm(string? s)
        => Regex.Replace((s ?? "").Trim().ToLowerInvariant(), @"\s+", " ");

    private static ChatLogParseResult Fail(ChatLogFormat format, string error, int lines = 0)
        => new() { Format = format, Error = error, LinesSeen = lines };

    private static List<string> SplitLines(string text)
        => text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').ToList();

    // ==================== 文本（微信 / QQ / 通用 / md） ====================

    private static ChatLogParseResult ParseTextual(string text, string source, ChatLogFormat? forceFormat = null)
    {
        var lines = SplitLines(text);
        var messages = new List<ImportedMessage>();
        var skipped = 0;
        var day = (DateTime?)null;

        string? speaker = null;
        DateTime? at = null;
        var body = new List<string>();

        void Flush()
        {
            if (speaker is null) return;
            var content = string.Join("\n", body).Trim();
            body.Clear();
            if (content.Length == 0) { skipped++; return; }
            messages.Add(new ImportedMessage
            {
                At = at,
                Speaker = speaker,
                Text = content,
                Source = source,
                Index = messages.Count
            });
        }

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd();
            if (NoiseLine.IsMatch(line)) { skipped++; continue; }

            if (TryHeader(line, ref day, out var lineSpeaker, out var lineAt))
            {
                Flush();
                speaker = lineSpeaker;
                at = lineAt;
                continue;
            }

            if (speaker is null) { if (line.Trim().Length > 0) skipped++; continue; }
            body.Add(line);
        }
        Flush();

        if (messages.Count == 0)
            return ParseColonPairs(text, source, forceFormat ?? ChatLogFormat.PlainText, lines.Count);

        if (messages.Count < 2)
            return Fail(forceFormat ?? ChatLogFormat.PlainText,
                $"只解析出 1 条消息。如果这不是一对一的聊天记录，请检查文件里是否包含多行「时间 昵称」。" , lines.Count);

        var format = forceFormat ?? DetectChatFlavor(text);
        return new ChatLogParseResult
        {
            Format = format,
            Messages = messages,
            LinesSeen = lines.Count,
            SkippedLines = skipped,
            Note = skipped > 0 ? $"跳过了 {skipped} 行信封/空行" : ""
        };
    }

    /// <summary>一个「时间 + 昵称」抬头行。日期只在第一条带一次的文件里，<paramref name="day"/> 会往下沿用。</summary>
    private static bool TryHeader(string line, ref DateTime? day, out string speaker, out DateTime? at)
    {
        speaker = "";
        at = null;
        var t = line.Trim();
        if (t.Length == 0) return false;

        var m = DateFirst.Match(t);
        if (m.Success && CleanName(m.Groups["n"].Value) is { Length: > 0 } name1)
        {
            var d = ParseDate(m.Groups["d"].Value);
            var time = ParseClock(m.Groups["t"].Value);
            if (d is not null && time is not null)
            {
                day = d;
                at = d.Value.Date + time.Value;
                speaker = name1;
                return true;
            }
        }

        m = BracketFirst.Match(t);
        if (m.Success && CleanName(m.Groups["n"].Value) is { Length: > 0 } nameBracket)
        {
            var d = ParseDate(m.Groups["d"].Value);
            var time = ParseClock(m.Groups["t"].Value);
            if (d is not null && time is not null)
            {
                day = d;
                at = d.Value.Date + time.Value;
                speaker = nameBracket;
                return true;
            }
        }

        m = NameFirst.Match(t);
        if (m.Success && CleanName(m.Groups["n"].Value) is { Length: > 0 } name2)
        {
            var d = ParseDate(m.Groups["d"].Value);
            var time = ParseClock(m.Groups["t"].Value);
            if (d is not null && time is not null)
            {
                day = d;
                at = d.Value.Date + time.Value;
                speaker = name2;
                return true;
            }
        }

        // 时刻开头只在已经出现过日期之后才认：否则「12:00 我们吃饭吧」会被误当成发言者
        if (day is not null)
        {
            m = TimeFirst.Match(t);
            if (m.Success && CleanName(m.Groups["n"].Value) is { Length: > 0 } name3)
            {
                var time = ParseClock(m.Groups["t"].Value);
                if (time is not null)
                {
                    at = day.Value.Date + time.Value;
                    speaker = name3;
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>「昵称(123456)」→「昵称」；去掉首尾引号、冒号与多余空白。</summary>
    private static string CleanName(string raw)
    {
        var s = (raw ?? "").Trim().Trim('“', '”', '"', '\'', '：', ':').Trim();
        var paren = Regex.Match(s, @"^(?<n>.+?)\s*[（(]\d{3,}[)）]\s*$");
        if (paren.Success) s = paren.Groups["n"].Value.Trim();
        if (s.Length > 24) return "";      // 一长串说明这行其实是正文，不是抬头
        return s;
    }

    private static ChatLogParseResult ParseColonPairs(string text, string source, ChatLogFormat format, int totalLines)
    {
        var messages = new List<ImportedMessage>();
        string? speaker = null;
        var body = new List<string>();
        var seenNames = new HashSet<string>(StringComparer.Ordinal);

        void Flush()
        {
            if (speaker is null) return;
            var content = string.Join("\n", body).Trim();
            body.Clear();
            if (content.Length == 0) return;
            messages.Add(new ImportedMessage
            {
                Speaker = speaker, Text = content, Source = source, Index = messages.Count
            });
        }

        foreach (var rawLine in SplitLines(text))
        {
            var line = rawLine.TrimEnd();
            var m = ColonPair.Match(line);
            if (m.Success)
            {
                var name = CleanName(m.Groups["n"].Value);
                if (name.Length > 0)
                {
                    Flush();
                    speaker = name;
                    seenNames.Add(name);
                    var rest = m.Groups["t"].Value.Trim();
                    if (rest.Length > 0) body.Add(rest);
                    continue;
                }
            }
            if (speaker is null) continue;
            body.Add(line);
        }
        Flush();

        if (messages.Count < 3 || seenNames.Count < 2)
            return Fail(format, "", totalLines);

        return new ChatLogParseResult
        {
            Format = format == ChatLogFormat.Markdown ? ChatLogFormat.Markdown : ChatLogFormat.PlainText,
            Messages = messages,
            LinesSeen = totalLines,
            Note = "没有时间戳：已按「昵称: 内容」的顺序导入，时间排序不可用"
        };
    }

    private static ChatLogFormat DetectChatFlavor(string text)
    {
        if (text.Contains("消息记录") || text.Contains("消息分组") || text.Contains("消息对象"))
            return ChatLogFormat.QqText;
        if (Regex.IsMatch(text, @"[（(]\d{5,}[)）]"))
            return ChatLogFormat.QqText;
        if (Regex.IsMatch(text, @"^#{1,6}\s", RegexOptions.Multiline) || text.Contains("**"))
            return ChatLogFormat.Markdown;
        return ChatLogFormat.WeChat;
    }

    private static ChatLogFormat GuessTextFormat(string text) => DetectChatFlavor(text);

    // ==================== JSON ====================

    private static bool LooksLikeJson(string text)
    {
        var t = text.TrimStart();
        if (t.StartsWith('{')) return true;
        if (!t.StartsWith('[')) return false;
        // 「[2023-05-01 12:00:00] 昵称」这种带方括号的聊天行也以 [ 开头，
        // 只有后面紧跟对象/字符串/空数组才算 JSON，避免把正常记录判成坏 JSON。
        var rest = t[1..].TrimStart();
        return rest.Length == 0 || rest.StartsWith('{') || rest.StartsWith('"') || rest.StartsWith(']');
    }

    private static ChatLogParseResult ParseJson(string text, string source)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            var rows = new List<JsonElement>();
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                rows.AddRange(doc.RootElement.EnumerateArray());
            }
            else if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                JsonElement? array = null;
                foreach (var name in new[] { "messages", "data", "items", "list", "chat", "records" })
                {
                    if (doc.RootElement.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array)
                    {
                        array = v;
                        break;
                    }
                }
                if (array is null)
                    return Fail(ChatLogFormat.Json,
                        "JSON 里没找到消息数组：需要顶层是数组，或者 {\"messages\": [...]}。");
                rows.AddRange(array.Value.EnumerateArray());
            }

            var messages = new List<ImportedMessage>();
            var skipped = 0;
            foreach (var row in rows)
            {
                if (row.ValueKind != JsonValueKind.Object) { skipped++; continue; }
                var at = JsonTime(row);
                var speaker = JsonString(row, "sender", "from", "name", "speaker", "user", "nickname", "author");
                var body = JsonString(row, "content", "text", "message", "msg", "body", "html");
                if (string.IsNullOrWhiteSpace(body)) { skipped++; continue; }
                messages.Add(new ImportedMessage
                {
                    At = at,
                    Speaker = string.IsNullOrWhiteSpace(speaker) ? "对方" : speaker.Trim(),
                    Text = body.Trim(),
                    Source = source,
                    Index = messages.Count
                });
            }
            if (messages.Count == 0)
                return Fail(ChatLogFormat.Json,
                    "JSON 数组里没有可用的消息：每条至少要有 content/text/message 之一，发送者字段可省略。");
            return new ChatLogParseResult
            {
                Format = ChatLogFormat.Json,
                Messages = messages,
                SkippedLines = skipped,
                Note = skipped > 0 ? $"跳过了 {skipped} 条缺正文的记录" : ""
            };
        }
        catch (JsonException ex)
        {
            return Fail(ChatLogFormat.Json, "这段内容看起来是 JSON，但解析失败：" + ex.Message);
        }
    }

    private static string JsonString(JsonElement row, params string[] names)
    {
        foreach (var p in row.EnumerateObject())
            foreach (var name in names)
                if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase) &&
                    p.Value.ValueKind == JsonValueKind.String)
                    return p.Value.GetString() ?? "";
        return "";
    }

    private static DateTime? JsonTime(JsonElement row)
    {
        foreach (var p in row.EnumerateObject())
        {
            if (!p.Name.Equals("time", StringComparison.OrdinalIgnoreCase) &&
                !p.Name.Equals("date", StringComparison.OrdinalIgnoreCase) &&
                !p.Name.Equals("timestamp", StringComparison.OrdinalIgnoreCase) &&
                !p.Name.Equals("datetime", StringComparison.OrdinalIgnoreCase) &&
                !p.Name.Equals("at", StringComparison.OrdinalIgnoreCase) &&
                !p.Name.Equals("createdAt", StringComparison.OrdinalIgnoreCase))
                continue;

            if (p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetDouble(out var num))
                return FromEpoch(num);
            if (p.Value.ValueKind == JsonValueKind.String)
            {
                var s = p.Value.GetString() ?? "";
                if (double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) && d > 1e8)
                    return FromEpoch(d);
                if (ChatLogTime.TryParse(s, out var at)) return at;
            }
        }
        return null;
    }

    private static DateTime? FromEpoch(double v)
    {
        try
        {
            var ms = v > 1e11 ? v : v * 1000;
            return DateTimeOffset.FromUnixTimeMilliseconds((long)ms).LocalDateTime;
        }
        catch { return null; }
    }

    // ==================== CSV ====================

    private static bool LooksLikeCsv(string text)
    {
        var lines = SplitLines(text).Where(l => l.Trim().Length > 0).Take(6).ToList();
        if (lines.Count < 2) return false;
        var withCommas = lines.Count(l => CountOutsideQuotes(l, ',') >= 2 || CountOutsideQuotes(l, '，') >= 2);
        if (withCommas < 2) return false;
        if (CsvHeader.IsMatch(lines[0])) return true;
        // 没有表头：至少一半的行里有一格能认成时间
        var timeish = lines.Count(l =>
        {
            var f = SplitCsvLine(l);
            return f.Count >= 3 && ChatLogTime.TryParse(f[0], out _);
        });
        return timeish * 2 >= lines.Count;
    }

    private static int CountOutsideQuotes(string line, char sep)
    {
        var n = 0;
        var quoted = false;
        foreach (var c in line)
        {
            if (c == '"') quoted = !quoted;
            else if (!quoted && c == sep) n++;
        }
        return n;
    }

    private static ChatLogParseResult ParseCsv(string text, string source)
    {
        var lines = SplitLines(text).Where(l => l.Trim().Length > 0).ToList();
        if (lines.Count < 2) return Fail(ChatLogFormat.Csv, "CSV 只有一行，无法判断列的含义。");

        var sep = CountOutsideQuotes(lines[0], ',') >= CountOutsideQuotes(lines[0], '，') ? ',' : '，';
        var header = SplitCsvLine(lines[0], sep);
        int timeCol = -1, senderCol = -1, contentCol = -1;
        for (var i = 0; i < header.Count; i++)
        {
            var h = header[i].Trim().ToLowerInvariant();
            if (timeCol < 0 && Regex.IsMatch(h, @"^(时间|日期|time|date|timestamp|datetime|at)$")) timeCol = i;
            else if (senderCol < 0 && Regex.IsMatch(h, @"^(发送者|发送人|昵称|说话人|sender|from|name|speaker|user|author)$")) senderCol = i;
            else if (contentCol < 0 && Regex.IsMatch(h, @"^(内容|消息|文本|content|text|message|msg|body)$")) contentCol = i;
        }
        var hasHeader = timeCol >= 0 || senderCol >= 0 || contentCol >= 0;
        if (!hasHeader)
        {
            timeCol = 0;
            senderCol = 1;
            contentCol = 2;
        }
        if (contentCol < 0) contentCol = 2;
        if (senderCol < 0) senderCol = 1;

        var messages = new List<ImportedMessage>();
        var skipped = 0;
        for (var li = hasHeader ? 1 : 0; li < lines.Count; li++)
        {
            var f = SplitCsvLine(lines[li], sep);
            if (f.Count <= contentCol) { skipped++; continue; }
            var body = string.Join(sep == '，' ? "，" : ",",
                f.Skip(contentCol).Select(x => x.Trim()).Where(x => x.Length > 0));
            if (body.Length == 0) { skipped++; continue; }
            DateTime? at = timeCol >= 0 && timeCol < f.Count && ChatLogTime.TryParse(f[timeCol], out var t) ? t : null;
            var speaker = senderCol >= 0 && senderCol < f.Count ? f[senderCol].Trim() : "";
            messages.Add(new ImportedMessage
            {
                At = at,
                Speaker = speaker.Length == 0 ? "对方" : speaker,
                Text = body,
                Source = source,
                Index = messages.Count
            });
        }
        if (messages.Count == 0)
            return Fail(ChatLogFormat.Csv, "CSV 里没有可用行：需要「时间,发送者,内容」三列（带不带表头都行）。");
        return new ChatLogParseResult
        {
            Format = ChatLogFormat.Csv,
            Messages = messages,
            SkippedLines = skipped,
            Note = hasHeader ? "按表头识别列" : "没表头：按「时间,发送者,内容」的列序读"
        };
    }

    private static List<string> SplitCsvLine(string line, char sep = ',')
    {
        var cells = new List<string>();
        var sb = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"')
            {
                if (quoted && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                else quoted = !quoted;
            }
            else if (!quoted && c == sep)
            {
                cells.Add(sb.ToString());
                sb.Clear();
            }
            else sb.Append(c);
        }
        cells.Add(sb.ToString());
        return cells;
    }

    // ==================== MHT / HTML ====================

    private static bool LooksLikeMht(string text)
        => Regex.IsMatch(text, @"^MIME-Version\s*:", RegexOptions.IgnoreCase | RegexOptions.Multiline)
           || Regex.IsMatch(text, @"Content-Type\s*:\s*multipart/", RegexOptions.IgnoreCase);

    /// <summary>最小的 MIME 解码器：只做 QQ 导出用到的 base64 / quoted-printable 与 HTML 抽字。</summary>
    private static class MhtText
    {
        public static string Extract(string mht)
        {
            var boundaryMatch = Regex.Match(mht, @"boundary\s*=\s*""?([^"";\r\n]+)""?", RegexOptions.IgnoreCase);
            var fallback = "";
            if (boundaryMatch.Success)
            {
                var boundary = boundaryMatch.Groups[1].Value.Trim();
                foreach (var part in mht.Split(new[] { "--" + boundary }, StringSplitOptions.None))
                    if (DecodePart(part) is { Length: > 0 } decoded && decoded.Length > fallback.Length)
                        fallback = decoded;
            }
            if (fallback.Length == 0) fallback = StripHtml(mht);
            return fallback;
        }

        private static string DecodePart(string part)
        {
            var split = part.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            var sepLen = 4;
            if (split < 0) { split = part.IndexOf("\n\n", StringComparison.Ordinal); sepLen = 2; }
            if (split < 0) return "";
            var head = part[..split];
            var body = part[(split + sepLen)..];
            if (!Regex.IsMatch(head, @"text/", RegexOptions.IgnoreCase)) return "";

            var ct = Regex.Match(head, @"Content-Type\s*:\s*([^;\r\n]+)", RegexOptions.IgnoreCase);
            var isHtml = ct.Success && ct.Groups[1].Value.Contains("html", StringComparison.OrdinalIgnoreCase);
            var charset = Regex.Match(head, @"charset\s*=\s*""?([^"";\r\n]+)", RegexOptions.IgnoreCase).Groups[1].Value.Trim();
            var enc = Regex.Match(head, @"Content-Transfer-Encoding\s*:\s*([^\r\n;]+)", RegexOptions.IgnoreCase).Groups[1].Value.Trim();

            byte[] bytes;
            if (enc.Contains("base64", StringComparison.OrdinalIgnoreCase))
            {
                try { bytes = Convert.FromBase64String(Regex.Replace(body, @"\s", "")); }
                catch { return ""; }
            }
            else if (enc.Contains("quoted-printable", StringComparison.OrdinalIgnoreCase))
            {
                bytes = QuotedPrintable(body);
            }
            else bytes = Encoding.UTF8.GetBytes(body);

            var text = Decode(bytes, charset);
            return isHtml ? StripHtml(text) : text;
        }

        private static byte[] QuotedPrintable(string body)
        {
            using var ms = new MemoryStream();
            for (var i = 0; i < body.Length; i++)
            {
                var c = body[i];
                if (c == '=' && i + 2 < body.Length)
                {
                    if (body[i + 1] == '\r' && body[i + 2] == '\n') { i += 2; continue; }
                    if (body[i + 1] == '\n') { i += 1; continue; }
                    if (Uri.IsHexDigit(body[i + 1]) && Uri.IsHexDigit(body[i + 2]))
                    {
                        ms.WriteByte((byte)Convert.ToInt32(body.Substring(i + 1, 2), 16));
                        i += 2;
                        continue;
                    }
                }
                var b = Encoding.UTF8.GetBytes(c.ToString());
                ms.Write(b, 0, b.Length);
            }
            return ms.ToArray();
        }

        private static string Decode(byte[] bytes, string charset)
        {
            if (!string.IsNullOrWhiteSpace(charset))
            {
                try { return Encoding.GetEncoding(charset).GetString(bytes); } catch { }
            }
            try { return new UTF8Encoding(false, true).GetString(bytes); }
            catch { return Encoding.UTF8.GetString(bytes); }
        }

        private static string StripHtml(string html)
        {
            var s = Regex.Replace(html, @"<(script|style)[^>]*>.*?</\1>", " ",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            s = Regex.Replace(s, @"<br\s*/?>|</p>|</div>|</tr>|</li>", "\n", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, "<[^>]+>", "");
            s = s.Replace("&nbsp;", " ").Replace("&lt;", "<").Replace("&gt;", ">")
                .Replace("&quot;", "\"").Replace("&#39;", "'").Replace("&amp;", "&");
            s = Regex.Replace(s, @"&#x([0-9a-fA-F]+);", m =>
            {
                var code = Convert.ToInt32(m.Groups[1].Value, 16);
                return code is > 0 and < 0x110000 ? char.ConvertFromUtf32(code) : "";
            });
            s = Regex.Replace(s, @"&#(\d+);", m =>
            {
                var code = int.Parse(m.Groups[1].Value);
                return code is > 0 and < 0x110000 ? char.ConvertFromUtf32(code) : "";
            });
            return s;
        }
    }

    /// <summary>去掉 md / HTML 的排版记号，让「时间 + 昵称」这类抬头重新落回行首。</summary>
    private static string StripMarkup(string text)
    {
        var s = Regex.Replace(text, @"<(script|style)[^>]*>.*?</\1>", " ",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        s = Regex.Replace(s, @"<br\s*/?>|</p>|</div>|</tr>", "\n", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, "<[^>]+>", "");
        s = Regex.Replace(s, @"^[\s>]*#{1,6}\s*", "", RegexOptions.Multiline);
        s = Regex.Replace(s, @"^\s*[-*+]\s+", "", RegexOptions.Multiline);
        s = s.Replace("**", "").Replace("__", "").Replace("`", "");
        return s;
    }
}

/// <summary>时间解析：这些格式覆盖了微信/QQ/常见导出工具与手写 md 的写法。</summary>
public static class ChatLogTime
{
    private static readonly string[] Formats =
    {
        "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm", "yyyy-MM-dd H:mm:ss", "yyyy-MM-dd H:mm",
        "yyyy/M/d HH:mm:ss", "yyyy/M/d HH:mm", "yyyy/M/d H:mm:ss", "yyyy/M/d H:mm",
        "yyyy.M.d HH:mm:ss", "yyyy.M.d H:mm",
        "yyyyMMdd HH:mm:ss", "yyyyMMdd HH:mm",
        "yyyy年M月d日 HH:mm:ss", "yyyy年M月d日 HH:mm", "yyyy年M月d日 H:mm:ss", "yyyy年M月d日 H:mm",
        "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-ddTHH:mm:ss.FFFFFFF",
        "M/d/yyyy h:mm:ss tt", "M/d/yyyy h:mm tt", "M/d/yyyy H:mm:ss", "M/d/yyyy H:mm",
        "yyyy-MM-dd", "yyyy/M/d", "yyyy年M月d日"
    };

    public static bool TryParse(string? raw, out DateTime at)
    {
        at = default;
        var s = (raw ?? "").Trim().Trim('"');
        if (s.Length == 0) return false;

        s = Regex.Replace(s, @"\s+", " ");
        s = s.Replace("上午", "AM").Replace("下午", "PM");

        if (DateTime.TryParseExact(s, Formats, CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal, out at))
            return true;
        if (DateTime.TryParse(s, CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal, out at))
            return true;
        return false;
    }

    /// <summary>只有时刻的一格（12:00 或 12:00:00）。</summary>
    internal static TimeSpan? ParseClockOnly(string raw)
    {
        var s = (raw ?? "").Trim();
        if (TimeSpan.TryParseExact(s, new[] { @"h\:mm", @"hh\:mm", @"h\:mm\:ss", @"hh\:mm\:ss" },
                CultureInfo.InvariantCulture, out var t))
            return t;
        return null;
    }
}
