using System.Text;
using WarmAsBefore.Helpers;
using WarmAsBefore.Models;

namespace WarmAsBefore.Services;

/// <summary>一次导入里某个文件的解析情况：结果页逐行告诉用户「这个文件读出了几条、跳了几条、为什么」。</summary>
public sealed record ChatImportSource
{
    public string Name { get; init; } = "";
    public string Format { get; init; } = "";
    public int Messages { get; init; }
    public int Skipped { get; init; }
    public string Note { get; init; } = "";
    public string Error { get; init; } = "";
}

/// <summary>解析+归一化之后、还没写进存档的那一份预览。</summary>
public sealed record ChatImportPreview
{
    public List<ChatImportSource> Sources { get; init; } = new();
    public List<ImportedMessage> Messages { get; init; } = new();
    public int DuplicatesDropped { get; init; }
    public ChatLogDigest Digest { get; init; } = new();
    public List<string> Warnings { get; init; } = new();
    public string Error { get; init; } = "";
    public bool Ok => Messages.Count > 0;
}

/// <summary>已经带回新档、等「开始这一局」时写入的那一份。</summary>
public sealed record StagedChatImport
{
    public string CharacterId { get; init; } = "";
    public ChatLogDigest Digest { get; init; } = new();
    public string Source { get; init; } = "";
    public DateTime StagedAt { get; init; } = DateTime.UtcNow;
    public int Messages => Digest.Messages;
}

/// <summary>导入完成后的结果，给「开始这一局」的进度与结果页看。</summary>
public sealed record ChatImportOutcome
{
    public int Messages { get; init; }
    public int Skipped { get; init; }
    public int UserMessages { get; init; }
    public int CharacterMessages { get; init; }
    public int OtherMessages { get; init; }
    public int Memories { get; init; }
    public int Affection { get; init; }
    public int Trust { get; init; }
    public string Summary => $"导入 {Messages} 条（跳过 {Skipped} 处重复/空行）· 你 {UserMessages} / TA {CharacterMessages}" +
                             (OtherMessages > 0 ? $" / 其他 {OtherMessages}" : "") +
                             $" · 写入记忆 {Memories} 条";
}

/// <summary>
/// 聊天记录导入的落地层：读文件、解析、暂存，再到新档开始时一次性写进记忆与初始状态。
///
/// 为什么分「暂存」与「写入」两步：新档的存档号是在「开始这一局」那一刻才分配的，
/// 记忆作用域也跟着它走。提前写只会写进上一个档，或者写进一个不存在的档 ——
/// 这正是「导入一半坏档」最省事的来源。所以这里只把算好的结果拿在手里，
/// 真正的写入发生在 Boot / NewRun 之后、Commit 之前；失败就抛，新档不落盘，旧档不受影响。
/// </summary>
public sealed class ChatLogImportService
{
    private const string Tag = "聊天记录导入";

    private readonly HumanMemoryService _human;
    private readonly RelationNetworkService _relations;
    private readonly object _gate = new();
    private StagedChatImport? _staged;

    public ChatLogImportService(HumanMemoryService human, RelationNetworkService relations)
    {
        _human = human;
        _relations = relations;
    }

    // ==================== 读与解析 ====================

    /// <summary>把多份「文件名 + 原始文本」合成一份预览。任何一个文件读不出都不拦住别的文件。</summary>
    public ChatImportPreview Combine(IEnumerable<(string Name, string Text)> inputs,
        string characterName, string nickname, string characterSpeaker = "")
    {
        var sources = new List<ChatImportSource>();
        var all = new List<ImportedMessage>();
        var index = 0;
        var totalSkipped = 0;

        foreach (var (name, text) in inputs)
        {
            var parsed = ChatLogParser.Parse(text, name);
            if (!parsed.Ok)
            {
                sources.Add(new ChatImportSource
                {
                    Name = name,
                    Format = ChatLogParser.NameOf(parsed.Format),
                    Error = parsed.Error.Length > 0 ? parsed.Error : "里面没有读出任何消息。",
                    Skipped = parsed.SkippedLines
                });
                totalSkipped += parsed.SkippedLines;
                continue;
            }

            var batch = parsed.Messages.Select(m => m with { Index = index++ }).ToList();
            all.AddRange(batch);
            totalSkipped += Math.Max(0, parsed.SkippedLines);
            sources.Add(new ChatImportSource
            {
                Name = name,
                Format = ChatLogParser.NameOf(parsed.Format),
                Messages = parsed.Messages.Count,
                Skipped = parsed.SkippedLines,
                Note = parsed.Note
            });
        }

        if (all.Count == 0)
        {
            var why = string.Join("；", sources.Where(s => s.Error.Length > 0)
                .Select(s => $"{s.Name}：{s.Error}").Take(3));
            return new ChatImportPreview
            {
                Sources = sources,
                Error = why.Length > 0 ? why : "没有读出任何消息。"
            };
        }

        var (kept, dropped) = ChatLogParser.Normalize(all);
        var digest = ChatLogDigest.Build(kept, characterName, nickname, characterSpeaker, totalSkipped + dropped);
        var warnings = new List<string>();
        if (digest.UserMessages == 0)
            warnings.Add("没有认出哪一方是「你」：这份记录里只有一个人说话，或名字都对不上。初始关系会按单方面记录计算。");
        if (digest.CharacterMessages == 0)
            warnings.Add($"没能把任何发言者认成 TA（{characterName}）。可以在下面「TA 在这个记录里叫」里选一下。");

        return new ChatImportPreview
        {
            Sources = sources,
            Messages = kept,
            DuplicatesDropped = dropped,
            Digest = digest,
            Warnings = warnings
        };
    }

    /// <summary>用户改了「谁是 TA」之后重算，不重新读文件。</summary>
    public ChatLogDigest RebuildDigest(IReadOnlyList<ImportedMessage> messages, string characterName,
        string nickname, string characterSpeaker, int skipped = 0)
        => ChatLogDigest.Build(messages, characterName, nickname, characterSpeaker, skipped);

    /// <summary>读一个用户选中的文件。UTF-8 优先，读不通再试 GBK（微信/QQ 的 Windows 导出常见）。</summary>
    public static async Task<string> ReadTextAsync(FileResult file)
    {
        await using var stream = await file.OpenReadAsync();
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);
        return Decode(ms.ToArray());
    }

    private static string Decode(byte[] bytes)
    {
        if (bytes.Length == 0) return "";
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        try
        {
            return new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            foreach (var name in new[] { "gb18030", "gbk", "gb2312" })
            {
                try { return Encoding.GetEncoding(name).GetString(bytes); }
                catch { }
            }
            return Encoding.UTF8.GetString(bytes);   // 实在认不出：保留原文，交给嗅探去报错
        }
    }

    // ==================== 暂存 ====================

    public void Stage(StagedChatImport staged)
    {
        lock (_gate) _staged = staged;
    }

    public StagedChatImport? Peek(string characterId)
    {
        lock (_gate)
            return _staged is not null &&
                   (string.IsNullOrEmpty(characterId) || string.Equals(_staged.CharacterId, characterId, StringComparison.Ordinal))
                ? _staged : null;
    }

    public StagedChatImport? Take(string characterId)
    {
        lock (_gate)
        {
            if (_staged is null) return null;
            if (!string.IsNullOrEmpty(characterId) &&
                !string.Equals(_staged.CharacterId, characterId, StringComparison.Ordinal))
            {
                _staged = null;   // 换了角色开局：这份暂存已不属于任何一局，不能悄悄留到下一次
                return null;
            }
            var taken = _staged;
            _staged = null;
            return taken;
        }
    }

    public void Clear()
    {
        lock (_gate) _staged = null;
    }

    // ==================== 写入 ====================

    /// <summary>
    /// 把暂存的一份写进当前新档：先记忆、再关系网、最后数值。顺序反过来就会出现
    /// 「记忆没写成、数值已经改了」的半成品；而任何一步抛错，调用方都不会 Commit，这一档等于没开始。
    /// </summary>
    public async Task<ChatImportOutcome> ApplyAsync(StagedChatImport staged, GameState state, CharacterData ch,
        IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var digest = staged.Digest;
        var charId = ch.Profile.Id;
        var added = new List<string>();

        try
        {
            progress?.Report("正在把聊天记录整理成记忆…");
            var existing = await _human.AllAsync(charId);
            var have = existing.Select(e => e.Text).ToHashSet(StringComparer.Ordinal);

            foreach (var seed in digest.MemorySeeds())
            {
                ct.ThrowIfCancellationRequested();
                if (seed.Text.Length == 0 || have.Contains(seed.Text)) continue;
                await _human.StoreAsync(charId, seed.Text,
                    seed.Important ? HumanMemoryKind.Important : HumanMemoryKind.Event,
                    seed.Importance,
                    tags: new[] { Tag, "导入" , MonthTag(digest.LastAt) });
                have.Add(seed.Text);
                added.Add(seed.Text);
            }

            progress?.Report("正在把你们的过去接进关系网…");
            await LearnAsync(charId, digest, ct);

            progress?.Report("正在评定这一局的初始关系…");
            var seedModel = new ChatHistorySeed
            {
                Messages = digest.Messages,
                Skipped = digest.Skipped,
                UserMessages = digest.UserMessages,
                CharacterMessages = digest.CharacterMessages,
                OtherMessages = digest.OtherMessages,
                UserInitiatedRatio = Math.Round(digest.UserInitiatedRatio, 3),
                SpanDays = digest.SpanDays,
                FirstAt = digest.FirstAt,
                LastAt = digest.LastAt,
                Affection = digest.Affection,
                Trust = digest.Trust,
                RecentSummary = digest.RecentSummary,
                Notables = digest.Notables,
                Source = staged.Source,
                ImportedAt = staged.StagedAt
            };
            ApplySeed(seedModel, state, ch);

            progress?.Report($"导入完成：{digest.Messages} 条消息，{added.Count} 条记忆。");
            return new ChatImportOutcome
            {
                Messages = digest.Messages,
                Skipped = digest.Skipped,
                UserMessages = digest.UserMessages,
                CharacterMessages = digest.CharacterMessages,
                OtherMessages = digest.OtherMessages,
                Memories = added.Count,
                Affection = seedModel.Affection,
                Trust = seedModel.Trust
            };
        }
        catch
        {
            await RollbackMemoriesAsync(charId, added);
            throw;
        }
    }

    /// <summary>
    /// 初始数值的落点：只改这一档里的角色状态（随 Commit 写进 char_states），
    /// 再夹回现有合法范围 —— 好感走 <see cref="AffectionScale"/> 的唯一刻度（0–1000），
    /// 与 SaveManager 的迁移/修复边界完全一致，越界的导入数据不会变成「读档即修复」的怪档。
    /// </summary>
    public static void ApplySeed(ChatHistorySeed seed, GameState state, CharacterData ch)
    {
        ch.State.Affection = AffectionScale.Clamp(seed.Affection);
        ch.State.Trust = Math.Clamp(seed.Trust, 0, 100);
        ch.State.LastSeen = DateTime.UtcNow;
        state.HistorySeed = seed;
        state.AffectionLog.Add(new AffectionTick
        {
            Value = ch.State.Affection,
            Reason = $"聊天记录导入（{seed.Messages} 条）",
            When = DateTime.UtcNow
        });
    }

    /// <summary>把整段记录按「一次聊天」分批喂给关系网：共现统计要的是一段段语境，不是逐条。</summary>
    private async Task LearnAsync(string charId, ChatLogDigest digest, CancellationToken ct)
    {
        var batches = digest.Episodes
            .Select(e => string.Join("\n", digest.Turns
                .Skip(e.StartIndex).Take(e.EndIndex - e.StartIndex)
                .Select(t => t.Text)))
            .Where(t => t.Length > 0)
            .ToList();

        if (batches.Count == 0)
            batches.Add(string.Join("\n", digest.Turns.Select(t => t.Text)));

        // 最多 16 批：每批都会落一次盘，几千条记录不该换来越导越慢。
        var step = Math.Max(1, (int)Math.Ceiling(batches.Count / 16.0));
        for (var i = 0; i < batches.Count; i += step)
        {
            ct.ThrowIfCancellationRequested();
            var merged = string.Join("\n", batches.Skip(i).Take(step));
            await _relations.LearnAsync(charId, merged.Length > 4000 ? merged[..4000] : merged);
        }
    }

    private async Task RollbackMemoriesAsync(string charId, List<string> texts)
    {
        if (texts.Count == 0) return;
        try
        {
            var all = await _human.AllAsync(charId);
            foreach (var e in all.Where(e => e.CharacterId == charId && texts.Contains(e.Text)))
                await _human.ForgetAsync(charId, e.Id);
        }
        catch (Exception ex)
        {
            App.WriteLog("ChatLogImport.Rollback -> " + ex.Message);
        }
    }

    private static string MonthTag(DateTime? at)
        => at is null ? "导入" : at.Value.ToString("yyyy-MM");
}
