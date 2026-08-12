using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using WarmAsBefore.Models;
using WarmAsBefore.Modules.DataPack;

namespace WarmAsBefore.Services;

/// <summary>
/// 角色库：角色的新建 / 导入 / 持久化。
/// 新建角色：手工填写资料生成角色；导入角色：zip 包（见下）导入。
/// 所有角色持久化到 characters.json，并注册进 GameEngine.Roster。
///
/// 支持的包结构：
/// 1) 常规数据包：manifest.json + 角色/ + 背景/ + 音频/ + CG/
/// 2) 角色包（纯角色）：角色/&lt;角色名&gt;/ 下放 角色主设定.txt 或 character.json，
///    以及若干服装文件夹（内含表情立绘 png，文件名带 （表情Aor表情B） 标签）
/// </summary>
public sealed class CharacterLibrary
{
    private readonly GameEngine _engine;
    private readonly StorageProvider _store;
    private readonly PackImporter _packs;
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private bool _loaded;

    public CharacterLibrary(GameEngine engine, StorageProvider store, PackImporter packs)
    {
        _engine = engine;
        _store = store;
        _packs = packs;
    }

    public async Task LoadAsync()
    {
        if (_loaded) return;
        _loaded = true;
        try
        {
            var saved = await _store.Load<List<CharacterData>>("characters");
            if (saved is null) return;
            foreach (var ch in saved)
                if (!_engine.Roster.ContainsKey(ch.Profile.Id))
                    _engine.Register(ch);
        }
        catch (Exception ex)
        {
            App.WriteLog("CharacterLibrary.Load -> " + ex);
        }
    }

    public async Task<List<CharacterData>> ListAsync()
    {
        await LoadAsync();
        return _engine.Roster.Values.OrderBy(c => c.Profile.Name).ToList();
    }

    public async Task<bool> AddAsync(CharacterData ch)
    {
        try
        {
            await LoadAsync();
            _engine.Register(ch);
            await _store.Save("characters", _engine.Roster.Values.ToList());
            return true;
        }
        catch (Exception ex)
        {
            App.WriteLog("CharacterLibrary.Add -> " + ex);
            return false;
        }
    }

    /// <summary>更新角色资料（改名/改性格/改设定），立即落盘。</summary>
    public async Task<bool> UpdateAsync(CharacterData updated)
    {
        try
        {
            await LoadAsync();
            _engine.Register(updated);
            await _store.Save("characters", _engine.Roster.Values.ToList());
            return true;
        }
        catch (Exception ex)
        {
            App.WriteLog("CharacterLibrary.Update -> " + ex);
            return false;
        }
    }

    /// <summary>角色库上下文：除主角外所有角色的一句话介绍，注入 AI 提示词供其自由调用。</summary>
    public string RosterContext(string excludeId)
    {
        var others = _engine.Roster.Values
            .Where(c => c.Profile.Id != excludeId)
            .OrderBy(c => c.Profile.Name)
            .ToList();
        if (others.Count == 0) return "";
        return string.Join("；", others.Select(c =>
            $"{c.Profile.Name}（{ShortPersonality(c.Profile.Personality)}）"));
    }

    private static string ShortPersonality(string p)
    {
        if (string.IsNullOrWhiteSpace(p)) return "性格未知";
        var cut = p.IndexOfAny(new[] { '。', '；', ';', '\n' });
        return cut > 0 ? p[..cut] : (p.Length > 30 ? p[..30] + "…" : p);
    }

    /// <summary>新建角色：从手工填写的资料生成。</summary>
    public CharacterData CreateDefault(string name, string gender, string personality)
    {
        var profile = new CharacterProfile
        {
            Id = Guid.NewGuid().ToString("N")[..10],
            Name = string.IsNullOrWhiteSpace(name) ? "小雨" : name.Trim(),
            Gender = gender,
            Personality = string.IsNullOrWhiteSpace(personality) ? "温柔可爱" : personality.Trim(),
            UserAddress = "主人",
            Description = "刚刚诞生的伙伴，一切都是崭新的开始。"
        };
        return new CharacterData { Profile = profile, State = new CharacterState() };
    }

    /// <summary>导入角色 zip：支持 character.json / 角色主设定.txt / 数据包 manifest 三种来源。</summary>
    public async Task<(bool ok, string message)> ImportFromZipAsync(string zipPath)
    {
        try
        {
            if (!File.Exists(zipPath)) return (false, "文件不存在");
            using var zip = ZipFile.OpenRead(zipPath);
            var charDirs = zip.Entries
                .Select(e => e.FullName)
                .Where(n => n.StartsWith("角色/", StringComparison.Ordinal) && n.Count(c => c == '/') >= 2)
                .Select(n => n.Split('/')[1])
                .Distinct()
                .ToList();
            if (charDirs.Count == 0) return await ImportManifestPackAsync(zip, zipPath);

            var okCount = 0;
            var names = new List<string>();
            foreach (var dir in charDirs)
            {
                var r = await ImportCharacterFolderAsync(zip, dir);
                if (r.ok) { okCount++; names.Add(r.name); }
            }
            if (okCount == 0) return (false, "未找到可识别的角色资料（需要 角色主设定.txt 或 character.json）");
            return (true, $"已导入 {okCount} 个角色：{string.Join("、", names)}");
        }
        catch (Exception ex)
        {
            App.WriteLog("CharacterLibrary.ImportFromZip -> " + ex);
            return (false, "导入失败：" + ex.Message);
        }
    }

    /// <summary>角色包：角色/&lt;角色名&gt;/ 结构，人设来自 角色主设定.txt 或 character.json，服装子目录全部解包。</summary>
    private async Task<(bool ok, string name)> ImportCharacterFolderAsync(ZipArchive zip, string dir)
    {
        try
        {
            var prefix = $"角色/{dir}/";
            var entries = zip.Entries
                .Where(e => e.FullName.StartsWith(prefix, StringComparison.Ordinal)
                            && !e.FullName.EndsWith("/", StringComparison.Ordinal))   // 跳过目录条目
                .ToList();

            var txtEntry = entries.FirstOrDefault(e => e.FullName.EndsWith("角色主设定.txt", StringComparison.Ordinal));
            var jsonEntry = entries.FirstOrDefault(e => e.FullName.EndsWith("character.json", StringComparison.Ordinal));
            var profile = jsonEntry is not null
                ? ParseProfileJson(jsonEntry)
                : txtEntry is not null ? ParseSettingTxt(ReadEntry(txtEntry)) : null;
            var name = string.IsNullOrWhiteSpace(profile?.Name) ? dir : profile!.Name;
            var ch = CreateDefault(name, profile?.Gender ?? "女", profile?.Personality ?? "");
            if (profile is not null)
            {
                var p = ch.Profile;
                ch = ch with
                {
                    Profile = p with
                    {
                        Nickname = string.IsNullOrEmpty(profile.Nickname) ? p.Nickname : profile.Nickname,
                        Description = string.IsNullOrEmpty(profile.Description) ? p.Description : profile.Description,
                        Greeting = string.IsNullOrEmpty(profile.Greeting) ? p.Greeting : profile.Greeting,
                        UserAddress = string.IsNullOrEmpty(profile.UserAddress) ? p.UserAddress : profile.UserAddress
                    }
                };
            }

            // 先清掉旧目录，保证可重复导入
            var assetsRoot = Path.Combine(_store.Root, "assets", "characters", ch.Profile.Id);
            if (Directory.Exists(assetsRoot)) Directory.Delete(assetsRoot, recursive: true);

            // 头像：角色文件夹直接子级中命名为 头像/avatar/icon/portrait 的图片（不放服装子目录里）
            var avatarRel = "";
            {
                var avatarCandidates = entries
                    .Where(e => !e.FullName[prefix.Length..].Contains('/')
                                && Path.GetExtension(e.FullName).Equals(".png", StringComparison.OrdinalIgnoreCase)
                                || !e.FullName[prefix.Length..].Contains('/')
                                && Path.GetExtension(e.FullName).Equals(".jpg", StringComparison.OrdinalIgnoreCase)
                                || !e.FullName[prefix.Length..].Contains('/')
                                && Path.GetExtension(e.FullName).Equals(".jpeg", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                var avatarEntry = avatarCandidates.FirstOrDefault(e =>
                {
                    var f = Path.GetFileNameWithoutExtension(e.FullName);
                    return f.Contains("头像") || f.Contains("avatar", StringComparison.OrdinalIgnoreCase)
                        || f.Contains("icon", StringComparison.OrdinalIgnoreCase)
                        || f.Contains("portrait", StringComparison.OrdinalIgnoreCase);
                }) ?? (avatarCandidates.Count == 1 ? avatarCandidates[0] : null);
                if (avatarEntry is not null)
                {
                    var dest = Path.Combine(_store.Root, "assets", "characters", ch.Profile.Id, "avatar.png");
                    CopyEntry(avatarEntry, dest);
                    avatarRel = $"characters/{ch.Profile.Id}/avatar.png";
                }
            }

            // 服装子目录 → assets/characters/{id}/{服装}/，并建立表情映射（文件名中的 （表情Aor表情B） 标签）
            var spriteMap = new Dictionary<string, string>();
            var outfitDirs = entries
                .Select(e => e.FullName[prefix.Length..])
                .Where(rel => rel.Contains('/'))
                .Select(rel => rel.Split('/')[0])
                .Distinct()
                .ToList();
            foreach (var outfitRaw in outfitDirs)
            {
                var outfitKey = NormalizeOutfit(outfitRaw);
                var outfitFiles = entries
                    .Where(e => e.FullName.StartsWith(prefix + outfitRaw + "/", StringComparison.Ordinal))
                    .ToList();
                foreach (var f in outfitFiles)
                {
                    var fileName = Path.GetFileName(f.FullName);
                    if (string.IsNullOrEmpty(fileName)) continue;
                    var relPath = $"characters/{ch.Profile.Id}/{outfitRaw}/{fileName}";
                    CopyEntry(f, Path.Combine(_store.Root, "assets", "characters", ch.Profile.Id, outfitRaw, fileName));
                    foreach (var emotion in EmotionsOf(Path.GetFileNameWithoutExtension(fileName)))
                        spriteMap[$"{outfitKey}/{emotion}"] = relPath;
                }
            }
            if (spriteMap.Count > 0)
                ch = ch with { SpriteMap = spriteMap };
            if (!string.IsNullOrEmpty(avatarRel))
                ch = ch with { Avatar = avatarRel };

            var ok = await AddAsync(ch);
            return ok ? (true, name) : (false, name);
        }
        catch (Exception ex)
        {
            App.WriteLog($"CharacterLibrary.ImportCharacterFolder({dir}) -> " + ex);
            return (false, dir);
        }
    }

    /// <summary>数据包（manifest.json）：素材解包后按 manifest 角色建资料。</summary>
    private async Task<(bool ok, string message)> ImportManifestPackAsync(ZipArchive zip, string zipPath)
    {
        var profile = zip.Entries.FirstOrDefault(e => e.FullName == "character.json") is { } e
            ? ParseProfileJson(e) : null;
        await _packs.Import(zipPath);   // 素材解包（角色立绘/背景/音频/CG）
        if (profile is null)
        {
            var info = await _packs.Peek(zipPath);
            if (info is null) return (false, "未找到可识别的角色资料");
            profile = new CharacterProfile
            {
                Name = info.Characters.Length > 0 ? info.Characters[0] : info.Name
            };
        }
        var ch = CreateDefault(profile.Name, profile.Gender, profile.Personality);
        var ok = await AddAsync(ch);
        return ok ? (true, $"已导入角色「{profile.Name}」") : (false, "保存失败");
    }

    private static CharacterProfile? ParseProfileJson(ZipArchiveEntry entry)
    {
        try { return JsonSerializer.Deserialize<CharacterProfile>(ReadEntry(entry), Json); }
        catch (Exception ex) { App.WriteLog("CharacterLibrary.ParseProfileJson -> " + ex); return null; }
    }

    /// <summary>解析 角色主设定.txt：姓名/性别/用户扮演人物/外在表现/内在坚定/前情提要。</summary>
    private static CharacterProfile? ParseSettingTxt(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var name = Match(text, @"姓名\s*[：:]\s*([^\s（(]+)");
        var gender = Match(text, @"性别\s*(男|女)");
        var user = Match(text, @"用户扮演的人物\s*[：:]\s*([^\n]+)");
        var outer = Match(text, @"外在表现[^：:\n]*[：:]\s*([^\n]+)");
        var inner = Match(text, @"内在坚定[^：:\n]*[：:]\s*([^\n]+)");

        var personality = string.Join("；", new[] { outer, inner }.Where(s => !string.IsNullOrWhiteSpace(s)));
        var introIdx = text.IndexOf("前情提要", StringComparison.Ordinal);
        var extra = introIdx >= 0 ? text[introIdx..].Trim() : "";
        return new CharacterProfile
        {
            Name = string.IsNullOrWhiteSpace(name) ? "新角色" : name.Trim(),
            Gender = string.IsNullOrEmpty(gender) ? "女" : gender,
            UserAddress = string.IsNullOrWhiteSpace(user) ? "主人" : user.Trim(),
            Personality = string.IsNullOrWhiteSpace(personality) ? "温柔可爱" : personality.Trim(),
            Description = extra.Length > 800 ? extra[..800] : extra
        };
    }

    private static string Match(string text, string pattern)
    {
        var m = Regex.Match(text, pattern);
        return m.Success ? m.Groups[1].Value.Trim() : "";
    }

    /// <summary>服装目录名 → 短名：去掉 CHxxxx_spr / TURN / mari_spr 前缀和（...）描述；只剩括号内容时取其第一段。</summary>
    private static string NormalizeOutfit(string raw)
    {
        var s = Regex.Replace(raw, @"^TURN", "");
        s = Regex.Replace(s, @"^\S*?_spr\s*", "");
        var paren = Regex.Match(s, @"[（(]([^（）()]*)[）)]");
        s = Regex.Replace(s, @"[（(][^（）()]*[）)]", "").Trim();
        if (s.Length == 0 && paren.Success)
        {
            s = Regex.Split(paren.Groups[1].Value, @"[，,、\s]+")
                .FirstOrDefault(x => x.Length > 0) ?? "";
        }
        return string.IsNullOrWhiteSpace(s) ? raw.Trim() : s.Trim();
    }

    /// <summary>从文件名提取表情标签：优先取最后一个完整的（…）内容，按 or 拆分；缺右括号时退化为取（…开头部分。</summary>
    private static IEnumerable<string> EmotionsOf(string fileNameWithoutExt)
    {
        var m = Regex.Match(fileNameWithoutExt, @"[（(]([^（）()]+)[）)]\s*$");
        if (!m.Success)
        {
            var open = Regex.Match(fileNameWithoutExt, @"[（(]([^（）()]*)");
            if (open.Success) m = open;
        }
        var tag = m.Success ? m.Groups[1].Value : fileNameWithoutExt;
        var emotions = Regex.Split(tag, @"\s*or\s*").Where(e => !string.IsNullOrWhiteSpace(e)).ToList();
        return emotions.Count > 0 ? emotions : new[] { fileNameWithoutExt };
    }

    private static string ReadEntry(ZipArchiveEntry entry)
    {
        using var r = new StreamReader(entry.Open(), System.Text.Encoding.UTF8);
        return r.ReadToEnd();
    }

    private static void CopyEntry(ZipArchiveEntry entry, string destPath)
    {
        if (entry.FullName.EndsWith("/", StringComparison.Ordinal) || entry.FullName.EndsWith("\\", StringComparison.Ordinal)) return;
        var dir = Path.GetDirectoryName(destPath);
        if (dir is not null)
        {
            if (File.Exists(dir)) File.Delete(dir);
            Directory.CreateDirectory(dir);
        }
        using var src = entry.Open();
        using var dst = File.Create(destPath);
        src.CopyTo(dst);
    }
}
