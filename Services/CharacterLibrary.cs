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
public sealed partial class CharacterLibrary
{
    private readonly GameEngine _engine;
    private readonly StorageProvider _store;
    private readonly PackImporter _packs;
    private readonly MemoryScope _scope;
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private Task? _loading;

    /// <summary>characters.json 没能读出来：这时任何「按当前花名册整表落盘」都会把全部角色抹掉。</summary>
    private bool _loadFaulted;

    public CharacterLibrary(GameEngine engine, StorageProvider store, PackImporter packs, MemoryScope scope)
    {
        _engine = engine;
        _store = store;
        _packs = packs;
        _scope = scope;
    }

    /// <summary>
    /// 新建存档时给角色随机一套服装 + 表情。
    ///
    /// 这是全应用**唯一**该随机立绘的地方。其余场景（读档、桌宠模式来回、重进主页面）
    /// 一律按 <see cref="CharacterState.CurrentOutfit"/> / <c>CurrentEmotion</c> 恢复 ——
    /// 随机决策一旦放到 MainGameViewModel 这种 transient 对象里，每次导航都会重掷一次。
    /// </summary>
    public static void RandomizeSpriteState(CharacterData ch)
    {
        if (ch.SpriteMap.Count == 0) return;

        var outfits = ch.SpriteMap.Keys.Select(k => k.Split('/')[0]).Distinct().ToList();
        if (outfits.Count == 0) return;
        var outfit = outfits[Random.Shared.Next(outfits.Count)];

        var emotions = ch.SpriteMap.Keys
            .Where(k => k.StartsWith(outfit + "/", StringComparison.Ordinal))
            .Select(k => k.Split('/')[1])
            .Distinct()
            .ToList();

        ch.State.CurrentOutfit = outfit;
        ch.State.CurrentEmotion = emotions.Count > 0
            ? emotions[Random.Shared.Next(emotions.Count)]
            : "normal";
        App.WriteLog($"RandomizeSpriteState: {ch.Profile.Name} -> {outfit}/{ch.State.CurrentEmotion}");
    }

    private readonly object _loadGate = new();

    /// <summary>
    /// 「读过盘」以读完为准：原来先置标记再 await，同一瞬间进来的第二个调用会以为已经加载好了，
    /// 捧着空角色表往下走；装载失败时更惨——空表会被当成花名册整表写回，全部角色消失。
    /// </summary>
    public Task LoadAsync()
    {
        lock (_loadGate) return _loading ??= LoadCoreAsync();
    }

    private async Task LoadCoreAsync()
    {
        _loadFaulted = false;
        try
        {
            var saved = await _store.Load<List<CharacterData>>("characters");
            if (saved is null) return;
            var seeded = false;
            foreach (var ch in saved)
            {
                // 修复已有的 SpriteMap 路径：确保带 assets/ 前缀
                var fixedCh = ch;
                if (ch.SpriteMap.Count > 0)
                {
                    var fixedMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var (k, v) in ch.SpriteMap)
                    {
                        var fixedPath = FixSpritePath(v);
                        fixedMap[k] = fixedPath;
                        if (fixedPath != v)
                            App.WriteLog($"CharacterLibrary: 修复立绘路径 {k} -> {fixedPath}");
                    }
                    fixedCh = ch with { SpriteMap = fixedMap };
                }
                // 旧存档里的角色没有副设定：按主设定补种一次（主设定本身不动）
                if (!fixedCh.SubProfile.IsSeeded)
                {
                    fixedCh = fixedCh with { SubProfile = CharacterSubProfileService.Seed(fixedCh.Profile) };
                    seeded = true;
                }
                if (!_engine.Roster.ContainsKey(fixedCh.Profile.Id))
                    _engine.Register(fixedCh);
            }
            if (seeded) await SaveRosterAsync();
        }
        catch (Exception ex)
        {
            // 读不出来就当这张表还没见过：放开重读，并拒绝任何整表落盘。
            _loadFaulted = true;
            lock (_loadGate) _loading = null;
            App.WriteLog("CharacterLibrary.Load -> " + ex);
        }
    }

    /// <summary>整表落盘。装载失败过就一律拒绝——那份「当前花名册」其实是空的。</summary>
    private async Task<bool> SaveRosterAsync()
    {
        if (_loadFaulted)
        {
            App.WriteLog("CharacterLibrary.Save -> 已跳过落盘：characters.json 这次没能读出来，不拿当前花名册盖盘");
            return false;
        }
        await _store.Save("characters", _engine.Roster.Values.ToList());
        return true;
    }

    /// <summary>
    /// 云存档把远端的 characters.json 落到本地后调用：丢掉「已加载」缓存，按磁盘重建角色表，
    /// 否则要重启应用才能看见另一台设备新建的角色。
    /// </summary>
    public async Task ReloadAsync()
    {
        if (!_store.Exists("characters")) return;
        _loadFaulted = false;
        lock (_loadGate) _loading = null;
        _engine.Roster.Clear();
        await LoadAsync();
        _engine.RestoreState(_engine.State);   // 活动角色指回重读后的新对象
    }

    /// <summary>修复立绘路径：确保带 assets/ 前缀。</summary>
    private static string FixSpritePath(string rel)
    {
        if (string.IsNullOrEmpty(rel)) return rel;
        // 已经是绝对路径则原样返回
        if (Path.IsPathRooted(rel)) return rel;
        // 已有 assets/ 前缀则原样返回
        if (rel.StartsWith("assets/", StringComparison.Ordinal)) return rel;
        // 添加 assets/ 前缀
        return rel.StartsWith("characters/", StringComparison.Ordinal)
            ? $"assets/{rel}"
            : rel;
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
            return await SaveRosterAsync();
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
            return await SaveRosterAsync();
        }
        catch (Exception ex)
        {
            App.WriteLog("CharacterLibrary.Update -> " + ex);
            return false;
        }
    }

    /// <summary>写入某角色的参数覆写（字段为 null 表示沿用全局设置），立即落盘。</summary>
    public async Task<bool> SetOverridesAsync(string charId, CharacterOverrides? overrides)
    {
        await LoadAsync();
        if (!_engine.Roster.TryGetValue(charId, out var ch)) return false;
        return await UpdateAsync(ch with { Overrides = overrides ?? new CharacterOverrides() });
    }

    /// <summary>
    /// 把某张立绘的路径换成新文件（立绘规范化后指向标准画布）。
    /// 按值匹配：所有指向旧文件的服装/表情条目一起改，避免只改一半。
    /// </summary>
    public async Task<(bool ok, string message)> ReplaceSpritePathAsync(string oldRel, string newRel)
    {
        await LoadAsync();
        if (string.IsNullOrWhiteSpace(oldRel) || string.IsNullOrWhiteSpace(newRel))
            return (false, "路径为空，无法替换。");

        var changed = 0;
        foreach (var ch in _engine.Roster.Values.ToList())
        {
            if (ch.SpriteMap.Count == 0) continue;
            if (!ch.SpriteMap.Any(kv => SamePath(kv.Value, oldRel))) continue;

            var map = new Dictionary<string, string>(ch.SpriteMap, StringComparer.OrdinalIgnoreCase);
            foreach (var (k, v) in ch.SpriteMap)
                if (SamePath(v, oldRel)) map[k] = newRel;
            if (await UpdateAsync(ch with { SpriteMap = map })) changed++;
        }
        return changed > 0
            ? (true, $"已把 {changed} 个角色的立绘指向新的标准画布文件。")
            : (false, "没有找到引用这张立绘的角色（可能它不在任何 SpriteMap 里）。");
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(a?.Replace('\\', '/').Trim(), b?.Replace('\\', '/').Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 角色库上下文：除主角外所有角色的一句话介绍，注入 AI 提示词供其自由调用。
    /// <paramref name="allowedIds"/> 非空时只列白名单里的角色——存档可以限定可用配角，
    /// 避免 AI 看到角色库里有人就疯狂加配角。
    /// </summary>
    public string RosterContext(string excludeId, IReadOnlyCollection<string>? allowedIds = null)
    {
        var others = _engine.Roster.Values
            .Where(c => c.Profile.Id != excludeId)
            .Where(c => allowedIds is null || allowedIds.Count == 0 || allowedIds.Contains(c.Profile.Id))
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

    /// <summary>导入包里没写、或写得认不出来的性别统一记成这个，而不是替角色挑一个：
    /// 猜成「女」就等于凭空给它挂上一条周期，提示词里会当真。</summary>
    public const string UnknownGender = "未知";

    /// <summary>新建角色：从手工填写的资料生成。副设定以主设定为起点播种。</summary>
    public CharacterData CreateDefault(string name, string gender, string personality)
    {
        var profile = new CharacterProfile
        {
            Id = Guid.NewGuid().ToString("N")[..10],
            Name = string.IsNullOrWhiteSpace(name) ? "小雨" : name.Trim(),
            Gender = string.IsNullOrWhiteSpace(gender) ? UnknownGender : gender.Trim(),
            Personality = string.IsNullOrWhiteSpace(personality) ? "温柔可爱" : personality.Trim(),
            UserAddress = "主人",
            Description = "刚刚诞生的伙伴，一切都是崭新的开始。"
        };
        return new CharacterData
        {
            Profile = profile,
            State = new CharacterState(),
            SubProfile = CharacterSubProfileService.Seed(profile)
        };
    }

    /// <summary>
    /// 写入某角色的副设定（漂移/回正）。主设定 <see cref="CharacterData.Profile"/> 不在此处触碰，
    /// 角色库与导入包都无法改写它以外的部分。
    /// </summary>
    public async Task<bool> SetSubProfileAsync(string charId, CharacterSubProfile? sub)
    {
        await LoadAsync();
        if (!_engine.Roster.TryGetValue(charId, out var ch)) return false;
        return await UpdateAsync(ch with { SubProfile = sub ?? CharacterSubProfileService.Seed(ch.Profile) });
    }

    /// <summary>
    /// 复制角色：仅复制资料（新 ID），共享引用所有立绘/头像文件，不重复保存素材。
    /// 可选重命名；返回新角色 ID 与显示名称。
    ///
    /// <paramref name="withMemory"/> 为真时连记忆一起带走（见 <c>CharacterLibrary.MemoryClone.cs</c>）：
    /// 只在当前记忆作用域里复制，副本与原角色共享同一个存档的记忆边界，不跨存档搬。
    /// </summary>
    public async Task<(bool ok, string message, string newId, string newName)> DuplicateAsync(
        string sourceId, string? newName = null, bool withMemory = false)
    {
        MemoryWrites? writes = null;
        try
        {
            await LoadAsync();
            if (!_engine.Roster.TryGetValue(sourceId, out var src))
                return (false, "源角色不存在", "", "");
            var newId = Guid.NewGuid().ToString("N")[..10];
            if (_engine.Roster.ContainsKey(newId))
                return (false, "副本撞上了已有角色，请再试一次", "", "");
            var displayName = string.IsNullOrWhiteSpace(newName) ? src.Profile.Name : newName.Trim();
            var destProfile = src.Profile with { Id = newId, Name = displayName };
            // 立绘/头像共享引用（路径不变，同一份文件）
            var dest = src with
            {
                Profile = destProfile,
                State = new CharacterState(),
                SpriteMap = new Dictionary<string, string>(src.SpriteMap),
                Avatar = src.Avatar,
                // 副本从主设定重新播种副设定：不继承原角色漂移出来的当下心境与心事
                SubProfile = CharacterSubProfileService.Seed(destProfile)
            };

            // 记忆先全部算完再落盘：这一步只读盘，失败的话角色还没注册、磁盘一个字没动
            if (withMemory)
            {
                var (built, buildMsg, pending) = await BuildMemoryWritesAsync(sourceId, newId);
                if (!built || pending is null)
                    return (false, buildMsg.Length > 0 ? buildMsg : "记忆复制失败（已回滚），角色没动。", "", "");
                if (!await ApplyMemoryWritesAsync(pending))
                    return (false, "记忆复制失败（已回滚），角色没动。", "", "");
                writes = pending;
            }

            var ok = await AddAsync(dest);
            if (!ok)
            {
                // 角色没登记上，刚写好的记忆不能留成没人认领的一坨
                if (writes is not null) await UndoMemoryWritesAsync(writes.Items);
                return (false, writes is null ? "保存失败" : "保存失败（记忆已撤回）", "", "");
            }
            if (writes is not null)
            {
                // 四个记忆服务都带着缓存，不叫它们重读，下一次 Persist 就把刚写的副本抹了
                _scope.InvalidateCaches();
                var c = writes.Counts;
                return (true,
                    $"已复制为「{displayName}」（共用素材，不重复保存）\n"
                    + (c.IsEmpty ? "TA 还没有记忆，副本是空白的。" : $"带走记忆：{c.Describe()}\n复制范围：{ScopeText()}"),
                    newId, displayName);
            }
            return (true, $"已复制为「{displayName}」（共用素材，不重复保存）", newId, displayName);
        }
        catch (Exception ex)
        {
            App.WriteLog("CharacterLibrary.Duplicate -> " + ex);
            // 已经落盘的记忆要是没跟着角色一起撤，下次复制同一号角色就会凭空多出一批不属于它的记忆
            if (writes is not null) await UndoMemoryWritesAsync(writes.Items);
            return (false, writes is null
                ? "复制失败：" + ex.Message
                : "复制失败：" + ex.Message + "（记忆已撤回）", "", "");
        }
    }

    /// <summary>作用域说人话：让用户知道这次复制的是哪一份记忆。</summary>
    private string ScopeText() => _scope.IsShared ? "跨存档共用的那一份" : $"本存档（{_scope.Current}）";

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".png", ".jpg", ".jpeg", ".bmp", ".webp" };

    /// <summary>
    /// 补充导入素材：从 zip 中提取图片追加到角色的立绘目录，不删除已有文件。
    /// zip 内图片按路径第一段（或 default）归入服装目录，文件名中的表情标签自动解析合并。
    /// </summary>
    public async Task<(bool ok, string message)> ImportSupplementaryZipAsync(string charId, string zipPath)
    {
        try
        {
            await LoadAsync();
            if (!_engine.Roster.TryGetValue(charId, out var ch))
                return (false, "角色不存在");
            if (!File.Exists(zipPath))
                return (false, "文件不存在");
            using var zip = ZipFile.OpenRead(zipPath);
            var images = zip.Entries
                .Where(e => !e.FullName.EndsWith('/') && ImageExtensions.Contains(Path.GetExtension(e.FullName)))
                .ToList();
            if (images.Count == 0)
                return (false, "未找到图片文件（png/jpg/jpeg/bmp/webp）");

            var spriteMap = new Dictionary<string, string>(ch.SpriteMap);
            var assetsRoot = Path.Combine(_store.Root, "assets", "characters", charId);
            string? avatarRel = null;

            foreach (var entry in images)
            {
                var parts = entry.FullName.Split('/', StringSplitOptions.RemoveEmptyEntries);
                var outfitRaw = parts.Length >= 2 ? parts[0] : "default";
                var outfitKey = NormalizeOutfit(outfitRaw);
                var fileName = Path.GetFileName(entry.FullName);
                if (string.IsNullOrEmpty(fileName)) continue;

                var destDir = Path.Combine(assetsRoot, outfitKey);
                Directory.CreateDirectory(destDir);
                var destPath = Path.Combine(destDir, fileName);
                CopyEntry(entry, destPath);

                var relPath = $"assets/characters/{charId}/{outfitKey}/{fileName}";
                foreach (var emo in EmotionsOf(Path.GetFileNameWithoutExtension(fileName)))
                    spriteMap[$"{outfitKey}/{emo}"] = relPath;

                // 自动识别头像
                var nameNoExt = Path.GetFileNameWithoutExtension(fileName);
                if (nameNoExt.Equals("avatar", StringComparison.OrdinalIgnoreCase)
                    || nameNoExt.Contains("头像"))
                    avatarRel = relPath;
            }

            var updated = ch with { SpriteMap = spriteMap };
            if (!string.IsNullOrEmpty(avatarRel))
                updated = updated with { Avatar = avatarRel };

            var ok = await UpdateAsync(updated);
            return ok
                ? (true, $"已补充 {images.Count} 张立绘到「{ch.Profile.Name}」")
                : (false, "保存失败");
        }
        catch (Exception ex)
        {
            App.WriteLog("CharacterLibrary.ImportSupplementary -> " + ex);
            return (false, "导入失败：" + ex.Message);
        }
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
            var ch = CreateDefault(name, profile?.Gender ?? "", profile?.Personality ?? "");
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
                // 优先使用原始目录名（因为文件实际在那里），找不到再用归一化名
                var actualDir = Directory.Exists(Path.Combine(_store.Root, "assets", "characters", ch.Profile.Id, outfitRaw))
                    ? outfitRaw
                    : (Directory.Exists(Path.Combine(_store.Root, "assets", "characters", ch.Profile.Id, outfitKey)) ? outfitKey : outfitRaw);
                foreach (var entry in entries.Where(e => e.FullName.StartsWith(prefix + actualDir + "/", StringComparison.Ordinal)))
                {
                    var fileName = Path.GetFileName(entry.FullName);
                    if (string.IsNullOrEmpty(fileName)) continue;
                    var relPath = $"assets/characters/{ch.Profile.Id}/{actualDir}/{fileName}";
                    CopyEntry(entry, Path.Combine(_store.Root, "assets", "characters", ch.Profile.Id, actualDir, fileName));
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
        // 只认「男/女」的老正则会把其他、自定义性别整个吞掉，然后悄悄补一个「女」。
        // 现在照原样取：性别后面到第一个标点/换行之前的这一小截就是性别，取不到留空，由 CreateDefault 记成「未知」。
        var gender = Match(text, @"性别\s*[：:]?\s*([^\s，,。；;、\n（(]{1,12})");
        var user = Match(text, @"用户扮演的人物\s*[：:]\s*([^\n]+)");
        var outer = Match(text, @"外在表现[^：:\n]*[：:]\s*([^\n]+)");
        var inner = Match(text, @"内在坚定[^：:\n]*[：:]\s*([^\n]+)");

        var personality = string.Join("；", new[] { outer, inner }.Where(s => !string.IsNullOrWhiteSpace(s)));
        var introIdx = text.IndexOf("前情提要", StringComparison.Ordinal);
        var extra = introIdx >= 0 ? text[introIdx..].Trim() : "";
        return new CharacterProfile
        {
            Name = string.IsNullOrWhiteSpace(name) ? "新角色" : name.Trim(),
            Gender = gender,
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
