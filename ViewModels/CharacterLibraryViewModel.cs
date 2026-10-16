using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.Controls;
using WarmAsBefore.Models;
using WarmAsBefore.Modules.ApiManager;
using WarmAsBefore.Modules.SaveSystem;
using WarmAsBefore.Services;

namespace WarmAsBefore.ViewModels;

/// <summary>
/// 角色库：所有已创建/导入的角色集中管理。
/// 除主角外，AI 在对话里可以自由调用角色库中的其他角色；
/// 觉得某个角色不合适，可以让 AI 帮她改写人设（AI 优化人设）。
/// </summary>
public sealed partial class CharacterLibraryViewModel : ObservableObject
{
    private readonly GameEngine _engine;
    private readonly CharacterLibrary _library;
    private readonly SaveManager _save;
    private readonly StorageProvider _store;
    private readonly ApiGateway _api;
    private readonly LingshuImporter _lingshu;
    private readonly CharacterSubProfileService _subProfiles;
    private readonly SemanticNetworkService _semantic;
    private readonly MemoryTagService _tags;

    [ObservableProperty] private ObservableCollection<CharacterLibraryItem> _items = new();

    public CharacterLibraryViewModel(GameEngine engine, CharacterLibrary library,
        SaveManager save, StorageProvider store, ApiGateway api, LingshuImporter lingshu,
        CharacterSubProfileService subProfiles, SemanticNetworkService semantic, MemoryTagService tags)
    {
        _engine = engine;
        _library = library;
        _save = save;
        _store = store;
        _api = api;
        _lingshu = lingshu;
        _subProfiles = subProfiles;
        _semantic = semantic;
        _tags = tags;
    }

    [RelayCommand]
    private async Task Refresh()
    {
        var list = await _library.ListAsync();
        var allSaves = await _save.List();
        var mainId = _engine.ActiveCharacter?.Profile.Id ?? _engine.State.CharacterId;
        Items = new ObservableCollection<CharacterLibraryItem>(list.Select(ch =>
        {
            var saves = allSaves.Where(s => s.Character == ch.Profile.Id).ToList();
            ImageSource? avatar = null;
            if (!string.IsNullOrEmpty(ch.Avatar))
            {
                var full = Path.Combine(_store.Root, ch.Avatar);
                if (File.Exists(full)) avatar = ImageSource.FromFile(full);
            }
            return new CharacterLibraryItem(ch, avatar,
                isMain: ch.Profile.Id == mainId,
                savesLabel: saves.Count == 0 ? "暂无存档" : $"存档 {saves.Count} 个");
        }));
    }

    [RelayCommand]
    private async Task Tap(string id)
    {
        var item = Items.FirstOrDefault(i => i.Id == id);
        if (item is null) return;
        var act = await Shell.Current.DisplayActionSheet(
            $"「{item.Name}」", "取消", null,
            item.IsMain ? "移除主角标记" : "设为当前主角",
            "AI 优化人设", "修改性格", "编辑设定",
            "复制角色", "补充素材",
            "导入灵枢记忆");
        switch (act)
        {
            case "设为当前主角":
                _engine.SetCharacter(id);
                await Refresh();
                await Shell.Current.DisplayAlert("角色库", $"「{item.Name}」现在是你的主角了。\n回到角色选择页可以和她开始新的一局。", "好");
                break;
            case "移除主角标记":
                _engine.State.CharacterId = "";
                await Refresh();
                break;
            case "AI 优化人设":
                await OptimizeAsync(id);
                break;
            case "修改性格":
                await EditPersonalityAsync(id);
                break;
            case "编辑设定":
                await EditProfileAsync(id);
                break;
            case "复制角色":
                await DuplicateCharacterAsync(id, item.Name);
                break;
            case "补充素材":
                await ImportSupplementAsync(id, item.Name);
                break;
            case "导入灵枢记忆":
                await ImportLingshuAsync(id, item.Name);
                break;
        }
    }

    /// <summary>让 AI 依据现有设定给出改进版性格，确认后应用（不满意可以反复让 AI 改）。</summary>
    private async Task OptimizeAsync(string id)
    {
        var item = Items.FirstOrDefault(i => i.Id == id);
        if (item is null) return;
        var p = item.Data.Profile;
        try
        {
            var history = new List<ChatMessage>
            {
                new() { Role = "system", Content = "你是角色设计助手。根据给出的角色现有设定，提出一版更鲜明、更有魅力的性格描述。只用中文输出改进后的性格描述本身（一到两句话，50 字以内），不要任何解释。" },
                new() { Role = "user", Content = $"角色「{p.Name}」，性别 {p.Gender}。现有性格：{p.Personality}。背景：{p.Description}" }
            };
            var suggested = await _api.Chat(history);
            if (string.IsNullOrWhiteSpace(suggested))
            {
                await Shell.Current.DisplayAlert("AI 优化人设", "AI 没有回应，请检查 AI 配置后重试。", "好");
                return;
            }
            var apply = await Shell.Current.DisplayAlert("AI 优化人设",
                $"AI 的建议（可用于「{p.Name}」）：\n\n「{suggested.Trim()}」\n\n应用这份新设定吗？", "应用", "放弃");
            if (!apply) return;
            var updated = item.Data with
            {
                Profile = p with { Personality = suggested.Trim() }
            };
            if (await _library.UpdateAsync(updated))
            {
                await Refresh();
                await Shell.Current.DisplayAlert("角色库", $"「{p.Name}」的新人设已生效。", "好");
            }
        }
        catch (Exception ex)
        {
            App.WriteLog("CharacterLibrary.Optimize -> " + ex);
            await Shell.Current.DisplayAlert("AI 优化人设", "出错了：" + ex.Message, "好");
        }
    }

    private async Task EditPersonalityAsync(string id)
    {
        var item = Items.FirstOrDefault(i => i.Id == id);
        if (item is null) return;
        var p = item.Data.Profile;
        var input = await Shell.Current.DisplayPromptAsync("修改性格",
            $"「{p.Name}」的当前性格：{p.Personality}", "保存", "取消");
        if (string.IsNullOrWhiteSpace(input)) return;
        var updated = item.Data with { Profile = p with { Personality = input.Trim() } };
        if (await _library.UpdateAsync(updated)) await Refresh();
    }

    /// <summary>编辑角色主设定/副设定（性格、背景描述、用户称呼、昵称、问候语等）。</summary>
    private async Task EditProfileAsync(string id)
    {
        var item = Items.FirstOrDefault(i => i.Id == id);
        if (item is null) return;
        var p = item.Data.Profile;
        var field = await Shell.Current.DisplayActionSheet(
            $"编辑「{p.Name}」设定", "取消", null,
            "修改性格（外在表现/内在坚定）", "修改背景/前情提要（主设定）",
            "修改用户称呼", "修改昵称", "修改问候语",
            "查看副设定（当下之我）", "副设定回正到主设定",
            "词义关系网（纠错）", "标签（查看/删除）",
            (IsAllowedInSave(item.Data.Profile.Id) ? "移出本存档可用角色" : "加入本存档可用角色"),
            "参数覆写（记忆/采样）");
        if (field is null || field == "取消") return;
        if (field.Contains("参数覆写")) { await EditOverridesAsync(item); return; }
        if (field.Contains("词义关系网")) { await EditSemanticNetworkAsync(item); return; }
        if (field.Contains("标签")) { await EditTagsAsync(item); return; }
        if (field.Contains("本存档可用角色")) { await ToggleAllowedAsync(item); return; }
        if (field.Contains("查看副设定"))
        {
            await Shell.Current.DisplayAlert($"「{p.Name}」的设定",
                _subProfiles.DiffSummary(item.Data.Profile.Id), "好");
            return;
        }
        if (field.Contains("回正"))
        {
            var yes = await Shell.Current.DisplayAlert("副设定回正",
                $"把「{p.Name}」的副设定退回主设定的副本？这会清空累积的当下心境与心事，主设定不受影响。",
                "回正", "取消");
            if (!yes) return;
            if (await _subProfiles.ResetAsync(item.Data.Profile.Id)) await Refresh();
            return;
        }

        string? promptTitle = null, initialValue = null;
        if (field.Contains("性格")) { promptTitle = "修改性格"; initialValue = p.Personality; }
        else if (field.Contains("背景")) { promptTitle = "修改背景设定"; initialValue = p.Description; }
        else if (field.Contains("称呼")) { promptTitle = "修改用户称呼"; initialValue = p.UserAddress; }
        else if (field.Contains("昵称")) { promptTitle = "修改昵称"; initialValue = p.Nickname; }
        else if (field.Contains("问候")) { promptTitle = "修改问候语"; initialValue = p.Greeting; }
        if (promptTitle is null) return;

        var input = await Shell.Current.DisplayPromptAsync(promptTitle,
            $"「{p.Name}」的{promptTitle}：", initialValue: initialValue, maxLength: 2000);
        if (input is null) return;

        var updated = item.Data with
        {
            Profile = p with
            {
                Personality = field.Contains("性格") ? input.Trim() : p.Personality,
                Description = field.Contains("背景") ? input.Trim() : p.Description,
                UserAddress = field.Contains("称呼") ? input.Trim() : p.UserAddress,
                Nickname = field.Contains("昵称") ? input.Trim() : p.Nickname,
                Greeting = field.Contains("问候") ? input.Trim() : p.Greeting
            }
        };
        if (await _library.UpdateAsync(updated)) await Refresh();
    }

    /// <summary>
    /// 角色级参数覆写：每个字段留空 = 沿用全局设置（清除该角色的覆写）。
    /// 用于让不同角色有各自的记忆与采样参数——有的记得久，有的转头就忘。
    /// </summary>
    private async Task EditOverridesAsync(CharacterLibraryItem item)
    {
        var o = item.Data.Overrides ?? new CharacterOverrides();
        var name = item.Data.Profile.Name;
        var pick = await Shell.Current.DisplayActionSheet(
            $"「{name}」参数覆写（留空=跟随全局）", "取消", null,
            $"记忆条数：{ShowOverride(o.MemoryTurns)}",
            $"上下文上限：{ShowOverride(o.ContextTokenLimit)}",
            $"温度：{ShowOverride(o.AiTemperature)}",
            $"记忆窗口天数：{ShowOverride(o.MemoryWindowDays)}",
            $"回忆随机度：{ShowOverride(o.MemoryRecallRandomness)}");
        if (pick is null || pick == "取消") return;

        var title = pick.Split('：')[0];
        var (hint, initial) = title switch
        {
            "记忆条数" => ("带进上下文的最近记忆条数（1-200），留空跟随全局", o.MemoryTurns?.ToString() ?? ""),
            "上下文上限" => ("上下文 Token 上限（如 32000），留空跟随全局", o.ContextTokenLimit?.ToString() ?? ""),
            "温度" => ("采样温度 0-2，留空跟随全局", o.AiTemperature?.ToString(CultureInfo.InvariantCulture) ?? ""),
            "记忆窗口天数" => ("人类记忆能回忆的天数，0=不限，留空跟随全局（默认 3 天）", o.MemoryWindowDays?.ToString(CultureInfo.InvariantCulture) ?? ""),
            _ => ("回忆随机扰动 0-1：越大越像人一样跳着想起，留空跟随全局", o.MemoryRecallRandomness?.ToString(CultureInfo.InvariantCulture) ?? "")
        };
        var input = await Shell.Current.DisplayPromptAsync(title, hint, initialValue: initial, maxLength: 12);
        if (input is null) return;

        var updated = title switch
        {
            "记忆条数" => ParseInt(input, v => o with { MemoryTurns = v }),
            "上下文上限" => ParseInt(input, v => o with { ContextTokenLimit = v }),
            "温度" => ParseDouble(input, v => o with { AiTemperature = v }),
            "记忆窗口天数" => ParseDouble(input, v => o with { MemoryWindowDays = v }),
            _ => ParseDouble(input, v => o with { MemoryRecallRandomness = v })
        };
        if (updated is null)
        {
            await Shell.Current.DisplayAlert("参数覆写", "请输入数字；留空表示跟随全局设置。", "好");
            return;
        }
        if (await _library.SetOverridesAsync(item.Data.Profile.Id, updated)) await Refresh();
    }

    private static string ShowOverride<T>(T? v) where T : struct =>
        v?.ToString() is { Length: > 0 } s ? s : "跟随全局";

    /// <summary>空串 = 清除覆写（返回去掉该字段的副本）；非法输入 = null。</summary>
    private static CharacterOverrides? ParseInt(string raw, Func<int?, CharacterOverrides> apply)
    {
        var t = raw?.Trim() ?? "";
        if (t.Length == 0) return apply(null);
        return int.TryParse(t, out var v) ? apply(v) : null;
    }

    private static CharacterOverrides? ParseDouble(string raw, Func<double?, CharacterOverrides> apply)
    {
        var t = raw?.Trim() ?? "";
        if (t.Length == 0) return apply(null);
        return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? apply(v) : null;
    }

    /// <summary>
    /// 词义关系网的人工纠错：看联系最紧的若干对词，可以加一条或删一条。
    /// AI 也能改这张网（tag_memory 之外的联想纠正走工具），这里给人一个直接下手的入口。
    /// </summary>
    private async Task EditSemanticNetworkAsync(CharacterLibraryItem item)
    {
        var id = item.Data.Profile.Id;
        var count = await _semantic.LinkCountAsync(id);
        if (count == 0)
        {
            await Shell.Current.DisplayAlert("词义关系网",
                $"「{item.Data.Profile.Name}」的关联网还没长出来——多聊几句就会自动积累（不花模型额度）。", "好");
            return;
        }

        var preview = await BuildSemanticPreviewAsync(id);
        var action = await Shell.Current.DisplayActionSheet(
            $"「{item.Data.Profile.Name}」的词义关系网（{count} 条联系）", "取消", null,
            "添加/加强一条联系", "删除一条联系");
        if (action is null || action == "取消") return;

        if (action.StartsWith("添加"))
        {
            await Shell.Current.DisplayAlert("当前联系最紧的词对", preview, "好");
            var input = await Shell.Current.DisplayPromptAsync("添加联系",
                "输入两个词，用逗号或顿号分隔（例如「咖啡、加班」）：", maxLength: 40);
            if (string.IsNullOrWhiteSpace(input)) return;
            var parts = input.Split(new[] { ',', '，', '、', ' ', '|' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) { await Shell.Current.DisplayAlert("添加联系", "请给出两个词。", "好"); return; }
            await _semantic.LinkAsync(id, parts[0].Trim(), parts[1].Trim(), 5, "human");
            await Shell.Current.DisplayAlert("添加联系", $"已建立「{parts[0].Trim()} ↔ {parts[1].Trim()}」。", "好");
        }
        else
        {
            await Shell.Current.DisplayAlert("当前联系最紧的词对", preview, "好");
            var input = await Shell.Current.DisplayPromptAsync("删除联系",
                "输入要断开的那两个词，逗号或顿号分隔：", maxLength: 40);
            if (string.IsNullOrWhiteSpace(input)) return;
            var parts = input.Split(new[] { ',', '，', '、', ' ', '|' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) { await Shell.Current.DisplayAlert("删除联系", "请给出两个词。", "好"); return; }
            await _semantic.UnlinkAsync(id, parts[0].Trim(), parts[1].Trim());
            await Shell.Current.DisplayAlert("删除联系", "已断开这条联系。", "好");
        }
    }

    /// <summary>取联系最紧的若干词对做成预览文本（给纠错时参照）。</summary>
    private async Task<string> BuildSemanticPreviewAsync(string charId)
    {
        var links = await _semantic.TopLinksAsync(charId, 12);
        if (links.Count == 0) return "（暂无联系最紧的词对）";
        return string.Join("\n", links.Select(l => $"{l.A} ↔ {l.B}（{l.Weight:0}，{l.Source}）"));
    }

    /// <summary>标签层的人工侧：看这个角色给人和事打了哪些标签，可删掉不合适的。</summary>
    private async Task EditTagsAsync(CharacterLibraryItem item)
    {
        var id = item.Data.Profile.Id;
        var tags = await _tags.ListAsync(id);
        if (tags.Count == 0)
        {
            await Shell.Current.DisplayAlert("标签",
                $"「{item.Data.Profile.Name}」还没有打过标签。标签由 AI 在对话中自己打（也可以让她「记一下这件事」）。", "好");
            return;
        }

        var text = string.Join("\n", tags.Select(t =>
            $"· {t.Name}（{(t.Subject switch { "person" => "人", "talk" => "话题", "place" => "地点", "thing" => "物", _ => "事" })}，用过 {t.Count} 次）"));
        var action = await Shell.Current.DisplayActionSheet($"「{item.Data.Profile.Name}」的标签（{tags.Count} 个）", "取消", null,
            "查看全部", "删除一个标签");
        if (action is null || action == "取消") return;

        if (action == "查看全部")
        {
            await Shell.Current.DisplayAlert("标签", text, "好");
            return;
        }
        await Shell.Current.DisplayAlert("标签", text, "好");
        var name = await Shell.Current.DisplayPromptAsync("删除标签", "输入要删掉的标签名：", maxLength: 12);
        if (string.IsNullOrWhiteSpace(name)) return;
        var removed = await _tags.RemoveAsync(id, name.Trim());
        await Shell.Current.DisplayAlert("删除标签", removed > 0 ? $"已删除「{name.Trim()}」。" : "没有找到这个标签。", "好");
    }

    /// <summary>
    /// 存档可用角色白名单：不设限时 AI 会看到角色库里所有人，容易疯狂加配角。
    /// 加入白名单后，只有名单里的人会出现在提示词里（空名单 = 不限制）。
    /// </summary>
    private async Task ToggleAllowedAsync(CharacterLibraryItem item)
    {
        var id = item.Data.Profile.Id;
        var allowed = _engine.State.AllowedCharacters;
        var name = item.Data.Profile.Name;
        if (allowed.Contains(id))
        {
            allowed.Remove(id);
            await Shell.Current.DisplayAlert("存档可用角色",
                $"已把「{name}」移出本存档的可用角色。\n" + WhitelistSummary(), "好");
        }
        else
        {
            allowed.Add(id);
            await Shell.Current.DisplayAlert("存档可用角色",
                $"已把「{name}」加入本存档的可用角色。\n" + WhitelistSummary(), "好");
        }
    }

    private bool IsAllowedInSave(string id) => _engine.State.AllowedCharacters.Contains(id);

    private string WhitelistSummary()
    {
        var allowed = _engine.State.AllowedCharacters;
        if (allowed.Count == 0) return "当前名单为空 = 不限制（AI 可以使用角色库里任何角色当配角）。";
        var names = _engine.Roster.Values
            .Where(c => allowed.Contains(c.Profile.Id))
            .Select(c => c.Profile.Name);
        return $"当前可用角色（{allowed.Count} 个）：{string.Join("、", names)}。";
    }

    /// <summary>复制角色（共享引用所有立绘，不重复保存素材文件）。</summary>
    private async Task DuplicateCharacterAsync(string sourceId, string sourceName)
    {
        var newName = await Shell.Current.DisplayPromptAsync("复制角色",
            $"为「{sourceName}」的副本命名：", initialValue: sourceName + "（副本）", maxLength: 30);
        if (string.IsNullOrWhiteSpace(newName)) return;
        var (ok, msg, _, _) = await _library.DuplicateAsync(sourceId, newName);
        await Shell.Current.DisplayAlert("复制角色", msg, "好");
        if (ok) await Refresh();
    }

    /// <summary>补充导入角色素材（zip 中的图片自动归入服装目录并解析表情标签）。</summary>
    private async Task ImportSupplementAsync(string charId, string charName)
    {
        try
        {
            var file = await FilePicker.PickAsync(new PickOptions
            {
                FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    { DevicePlatform.WinUI, new[] { ".zip" } },
                    { DevicePlatform.Android, new[] { "application/zip" } },
                    { DevicePlatform.iOS, new[] { "public.zip-archive" } }
                }),
                PickerTitle = $"选择「{charName}」的补充素材包（zip）"
            });
            if (file is null) return;
            var (ok, msg) = await _library.ImportSupplementaryZipAsync(charId, file.FullPath);
            await Shell.Current.DisplayAlert("补充素材", msg, "好");
            if (ok) await Refresh();
        }
        catch (Exception ex)
        {
            App.WriteLog("CharacterLibraryVM.ImportSupplement -> " + ex);
            await Shell.Current.DisplayAlert("补充素材", "导入失败：" + ex.Message, "好");
        }
    }

    /// <summary>新建角色（无需素材包，仅创建角色资料，之后可随时补充立绘）。</summary>
    [RelayCommand]
    private async Task CreateCharacter()
    {
        var name = await Shell.Current.DisplayPromptAsync("新建角色", "角色姓名：", maxLength: 30);
        if (string.IsNullOrWhiteSpace(name)) return;
        var gender = await Shell.Current.DisplayActionSheet("角色性别", "取消", null, "女", "男", "其他");
        if (gender is null || gender == "取消") return;
        var personality = await Shell.Current.DisplayPromptAsync("新建角色", "性格描述（一句话）：", maxLength: 100);
        if (personality is null) return;
        var ch = _library.CreateDefault(name.Trim(), gender, string.IsNullOrWhiteSpace(personality) ? "温柔可爱" : personality.Trim());
        var ok = await _library.AddAsync(ch);
        await Shell.Current.DisplayAlert("新建角色",
            ok ? $"「{name.Trim()}」已创建（暂无立绘，可在角色库右键补充素材）" : "创建失败", "好");
        if (ok) await Refresh();
    }

    [RelayCommand]
    private async Task Back() => await Shell.Current.GoToAsync("..");

    /// <summary>导入灵枢 AI 记忆 JSON 到角色世界书。</summary>
    private async Task ImportLingshuAsync(string charId, string charName)
    {
        try
        {
            var file = await FilePicker.PickAsync(new PickOptions
            {
                FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    { DevicePlatform.WinUI, new[] { ".json" } },
                    { DevicePlatform.Android, new[] { "application/json" } },
                    { DevicePlatform.iOS, new[] { "public.json" } }
                }),
                PickerTitle = "选择灵枢记忆 JSON 文件"
            });
            
            if (file == null) return;
            
            var valid = await _lingshu.ValidateAsync(file.FullPath);
            if (!valid)
            {
                await Shell.Current.DisplayAlert("导入失败", "文件格式不正确，请选择灵枢导出的 JSON 文件。", "好");
                return;
            }
            
            var mode = await Shell.Current.DisplayActionSheet(
                $"导入到「{charName}」", "取消", null,
                "追加到现有记忆", "替换所有记忆");
            
            if (mode == "取消") return;
            
            var result = await _lingshu.ImportToCharacterAsync(
                charId, 
                file.FullPath, 
                mode == "替换所有记忆" ? "replace" : "append");
            
            await Shell.Current.DisplayAlert(
                result.ok ? "导入成功" : "导入失败", 
                result.message, 
                "好");
            
            if (result.ok) await Refresh();
        }
        catch (Exception ex)
        {
            App.WriteLog("CharacterLibrary.ImportLingshu -> " + ex);
            await Shell.Current.DisplayAlert("导入失败", ex.Message, "好");
        }
    }
}

/// <summary>角色库展示项。</summary>
public sealed class CharacterLibraryItem
{
    public CharacterLibraryItem(CharacterData ch, ImageSource? avatar, bool isMain, string savesLabel)
    {
        Data = ch;
        AvatarSource = avatar;
        IsMain = isMain;
        SavesLabel = savesLabel;
    }

    public CharacterData Data { get; init; }
    public string Id => Data.Profile.Id;
    public string Name => Data.Profile.Name;
    public string Personality => Data.Profile.Personality;
    public string Description => Data.Profile.Description;
    public ImageSource? AvatarSource { get; init; }
    public bool HasAvatar => AvatarSource is not null;
    public bool IsMain { get; init; }
    public string IsMainLabel => IsMain ? "当前主角" : "";
    public string SavesLabel { get; init; }
}
