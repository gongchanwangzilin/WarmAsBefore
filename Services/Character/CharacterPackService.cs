using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using WarmAsBefore.Models;

namespace WarmAsBefore.Services;

/// <summary>一次单角色导出的结果。失败时 <see cref="ZipPath"/> 为空，<see cref="Message"/> 是给人看的那一句。</summary>
public sealed record CharacterPackResult(bool Ok, string Message, string ZipPath, long Size);

/// <summary>
/// 单角色包：把「一个人」连同 TA 自己的素材打成一个 zip，交给系统分享出去。
///
/// 与整包导出（<see cref="DataPackageService"/>）的分工：
///   · 整包导出面向「换机搬家」，所以整目录打包；
///   · 这里只面向「把 TA 送给别人」，所以只收这一个角色自己的文件 —— 头像、SpriteMap 里
///     所有路径、背景，以及 <c>assets/characters/{id}/</c> 目录下属于 TA 的那一批。
/// 包内路径保持「相对存储根」的原样（如 <c>assets/characters/{id}/avatar.png</c>），
/// 导入侧拿到就能直接落回同一个位置，不需要再猜目录。
///
/// 密钥：先确认过 <see cref="CharacterData"/> 目前没有任何 API Key / Token 字段；
/// 但序列化前仍然过一遍按名字清扫，防的是以后往角色上加了密钥字段却没人记得这里。
/// </summary>
public sealed class CharacterPackService
{
    public const string ManifestFileName = "manifest.json";
    public const string CharacterFileName = "character.json";
    public const string FormatName = "wab.character";
    public const int FormatVersion = 1;

    private static readonly JsonSerializerOptions PrettyJson = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly string[] SecretNameWords =
    {
        "Key", "Keys", "Token", "Secret", "Password", "Passphrase", "Authorization"
    };

    private readonly StorageProvider _store;

    public CharacterPackService(StorageProvider store) => _store = store;

    /// <summary>导出单个角色。I/O 全部在线程池上跑，调用方只 await 结果。</summary>
    public Task<CharacterPackResult> ExportAsync(CharacterData ch) => Task.Run(() => ExportCore(ch));

    private CharacterPackResult ExportCore(CharacterData ch)
    {
        string? zipPath = null;
        try
        {
            if (ch is null || string.IsNullOrWhiteSpace(ch.Profile.Id))
                return new CharacterPackResult(false, "这个角色没有 id，无法打包。", "", 0);

            var root = Path.GetFullPath(_store.Root);
            if (!Directory.Exists(root))
                return new CharacterPackResult(false, "存储目录不存在。", "", 0);

            var id = ch.Profile.Id.Trim();
            var name = string.IsNullOrWhiteSpace(ch.Profile.Name) ? "未命名角色" : ch.Profile.Name.Trim();

            // (包内相对路径, 磁盘绝对路径)：先枚举再写，方便失败时一句都不写。
            var assets = new List<(string Rel, string Full)>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void AddAsset(string? path)
            {
                if (string.IsNullOrWhiteSpace(path)) return;
                var raw = path.Trim().Replace('\\', '/');
                var full = Path.IsPathRooted(raw)
                    ? Path.GetFullPath(raw)
                    : Path.GetFullPath(Path.Combine(root, raw.Replace('/', Path.DirectorySeparatorChar)));

                // 只收存储根以内的文件：包内路径必须是「相对存储根」的那一段，
                // 根外的绝对路径（用户临时挑的图）没有可落地的相对位置。
                var rootPrefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                                 + Path.DirectorySeparatorChar;
                if (!full.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase)) return;
                if (!File.Exists(full)) return;

                var rel = Path.GetRelativePath(root, full).Replace('\\', '/');
                if (rel.Length == 0 || rel.StartsWith("../", StringComparison.Ordinal)) return;
                if (!seen.Add(rel)) return;
                assets.Add((rel, full));
            }

            AddAsset(ch.Avatar);
            foreach (var sprite in ch.SpriteMap.Values) AddAsset(sprite);
            AddAsset(ch.Background);

            // 这个角色自己的素材目录整棵收下：立绘、服装、生成但还没写进字段的中间产物都算 TA 的。
            var ownDir = Path.Combine(root, "assets", "characters", id);
            if (Directory.Exists(ownDir))
            {
                foreach (var file in Directory.EnumerateFiles(ownDir, "*", SearchOption.AllDirectories)
                             .OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                {
                    AddAsset(file);
                }
            }

            var cacheDir = Path.Combine(FileSystem.CacheDirectory, "character-packs");
            Directory.CreateDirectory(cacheDir);
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            zipPath = Path.Combine(cacheDir, $"{SafeFileName(name)}-{SafeFileName(id)}-{stamp}.zip");

            using (var fs = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var archive = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                WriteTextEntry(archive, ManifestFileName, BuildManifest(ch, name));
                WriteTextEntry(archive, CharacterFileName, BuildCharacterJson(ch));

                // 排序后写：同一份角色两次导出的条目顺序一致，排查包内容时好比对。
                foreach (var (rel, full) in assets.OrderBy(a => a.Rel, StringComparer.OrdinalIgnoreCase))
                {
                    var entry = archive.CreateEntry(rel, CompressionLevel.Fastest);
                    using var src = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    using var dst = entry.Open();
                    src.CopyTo(dst);
                }
            }

            var size = new FileInfo(zipPath).Length;
            return new CharacterPackResult(true,
                $"角色包已生成：{Path.GetFileName(zipPath)}（{DataPackageService.FormatSize(size)}）",
                zipPath, size);
        }
        catch (Exception ex)
        {
            App.WriteLog("CharacterPackService.Export -> " + ex);
            // 写坏的半个 zip 不留给用户：它会被当成「打得开但缺文件」的包传来传去。
            try { if (zipPath is not null && File.Exists(zipPath)) File.Delete(zipPath); } catch { }
            return new CharacterPackResult(false, "导出失败：" + ex.Message, "", 0);
        }
    }

    private static string BuildManifest(CharacterData ch, string name)
    {
        var manifest = new JsonObject
        {
            ["app"] = "WarmAsBefore",
            ["format"] = FormatName,
            ["version"] = FormatVersion,
            ["exportedAt"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            ["characterId"] = ch.Profile.Id,
            ["name"] = name
        };
        return manifest.ToJsonString(PrettyJson);
    }

    /// <summary>角色本体。先把密钥类字段清空再写盘，理由见类型注释。</summary>
    private static string BuildCharacterJson(CharacterData ch)
    {
        if (JsonSerializer.SerializeToNode(ch, PrettyJson) is not JsonObject node) return "{}";
        Scrub(node);
        return node.ToJsonString(PrettyJson);
    }

    /// <summary>按名字递归清密钥（与整包导出同一口径：只认结尾，避免误伤 KeyPressId / Keywords）。</summary>
    private static void Scrub(JsonNode node)
    {
        if (node is JsonArray array)
        {
            foreach (var item in array)
                if (item is not null) Scrub(item);
            return;
        }
        if (node is not JsonObject obj) return;

        foreach (var name in obj.Select(p => p.Key).ToList())
        {
            if (IsSecretName(name))
            {
                obj[name] = obj[name] is JsonArray ? new JsonArray() : JsonValue.Create(string.Empty);
                continue;
            }
            if (obj[name] is { } child) Scrub(child);
        }
    }

    private static bool IsSecretName(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        foreach (var word in SecretNameWords)
            if (name.EndsWith(word, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static void WriteTextEntry(ZipArchive archive, string entryName, string text)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(text);
    }

    /// <summary>文件名里只留安全字符：角色名可能带斜杠、冒号，不能让它把包写到别的目录。</summary>
    private static string SafeFileName(string raw)
    {
        var bad = Path.GetInvalidFileNameChars();
        var clean = new string((raw ?? "").Where(c => Array.IndexOf(bad, c) < 0 && c != '/').ToArray()).Trim();
        if (clean.Length == 0) clean = "character";
        return clean.Length > 48 ? clean[..48] : clean;
    }
}
