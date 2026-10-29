using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WarmAsBefore.Controls;
using WarmAsBefore.Modules.Mcp;
using WarmAsBefore.Services;
using WarmAsBefore.Models;
using WarmAsBefore.Modules.Screen;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO.Compression;

namespace WarmAsBefore.ViewModels;

public sealed partial class CharacterSelectViewModel : ObservableObject
{
    private readonly CharacterLibrary _library;
    private readonly Modules.SaveSystem.SaveManager _save;
    private readonly StorageProvider _store;
    private readonly SceneCalibrationService _cal;
    private readonly Modules.ApiManager.ApiGateway _api;
    private readonly ImageGenerationService _images;
    private readonly MapService _maps;

    [ObservableProperty] private ObservableCollection<CharacterCardItem> _cards = new();
    [ObservableProperty] private bool _aiBusy;

    public CharacterSelectViewModel(CharacterLibrary library,
        Modules.SaveSystem.SaveManager save, StorageProvider store, SceneCalibrationService cal,
        Modules.ApiManager.ApiGateway api, ImageGenerationService images, MapService maps)
    {
        _library = library;
        _library = library;
        _save = save;
        _store = store;
        _cal = cal;
        _api = api;
        _images = images;
        _maps = maps;
    }

    [RelayCommand]
    private async Task Refresh()
    {
        var list = await _library.ListAsync();
        var allSaves = await _save.List();
        Cards = new ObservableCollection<CharacterCardItem>(list.Select(ch =>
        {
            var saves = allSaves.Where(s => s.Character == ch.Profile.Id).ToList();
            var label = saves.Count == 0
                ? "还没有存档"
                : $"已有 {saves.Count} 个存档 · 最近 {saves.Max(s => s.SavedAt).ToLocalTime():MM-dd HH:mm}";
            ImageSource? avatar = null;
            if (!string.IsNullOrEmpty(ch.Avatar))
            {
                var full = Path.Combine(_store.Root, ch.Avatar);
                if (File.Exists(full)) avatar = ImageSource.FromFile(full);
            }
            return new CharacterCardItem(ch, avatar, label);
        }));
    }

    [RelayCommand]
    private async Task Pick(string id)
    {
        var card = Cards.FirstOrDefault(c => c.Id == id);
        if (card is null) return;
        var ch = card.Data;
        var saves = await _save.ListByCharacter(id);

        // 已有存档：可继续上次相处，也可新开一局；同一角色可有多档
        if (saves.Count > 0)
        {
            var act = await Shell.Current.DisplayActionSheet(
                $"和「{ch.Profile.Name}」再次相遇", "取消", null, "继续上次的相处", "开始新的一局");
            if (act == "继续上次的相处")
            {
                await _library.ListAsync();
                await _save.Load(saves[0].Id);
                await Shell.Current.GoToAsync("main");
                return;
            }
            if (act != "开始新的一局") return;
        }
        else
        {
            var confirm = await Shell.Current.DisplayActionSheet(
                $"和「{ch.Profile.Name}」开始新的一局？", "取消", null, "开始游戏");
            if (confirm != "开始游戏") return;
        }
        // 新开一局先过「世界树 + 人设」这一屏：不绑世界树也能进，但地图会锁上。
        // Boot / SetCharacter / NewRun 都搬到那一页，用户中途返回时不会留下半开的空档。
        await Shell.Current.GoToAsync($"newrun?character={Uri.EscapeDataString(id)}");
    }

    /// <summary>
    /// 新建角色 = 进那一页的空白草稿（带 new=1）。这一颗从前连弹三个输入框：名字 → 性别 → 性格，
    /// 全程看不见 TA 长什么样，写完直接落盘、想改只能再去列表里翻那层菜单。
    /// start=1 是「建好就按 TA 开一局」：草稿进名册那一刻由那一页接手 Boot / SetCharacter / NewRun。
    /// </summary>
    [RelayCommand]
    private async Task CreateCharacter() => await Shell.Current.GoToAsync("studio?new=1&start=1");

    /// <summary>顶栏「＋」：手动新建与 AI 造角色放同一张菜单，不把两个入口都塞进顶栏。</summary>
    [RelayCommand]
    private async Task AddMenu()
    {
        var act = await Shell.Current.DisplayActionSheet("添加角色", "取消", null, "手动新建", "AI 造角色");
        if (act == "手动新建") await CreateCharacter();
        else if (act == "AI 造角色") await AiCreateCharacter();
    }

    /// <summary>
    /// 角色工坊：一句描述 → AI 交出角色设定与配景描述 → 建角色、画头像、按需把配景画成背景接进地图。
    /// 生成失败只提示，不落半成品；画面生成没开时仍然可以只建角色。
    /// </summary>
    [RelayCommand]
    private async Task AiCreateCharacter()
    {
        var prompt = await Shell.Current.DisplayPromptAsync("AI 造角色",
            "想要一个什么样的角色？（外貌、性格、和你的关系、世界背景都可以写）",
            "生成", "取消", placeholder: "例如：住在海边的旧书店老板，安静，喜欢在雨天煮茶");
        if (string.IsNullOrWhiteSpace(prompt)) return;

        AiBusy = true;
        CharacterWorkshopDraft? draft;
        try
        {
            const string system =
                "你是角色设计师。根据用户描述设计一个角色，只输出一个 JSON 对象，不要 markdown 代码块，不要解释。"
                + "字段：name（名字）、gender（必须是 女/男/其他/保密 之一）、personality（性格，80 字内）、"
                + "description（外貌与背景，200 字内）、greeting（初次见面说的一句话）、"
                + "userAddress（TA 怎么称呼用户）、scenes（2~3 个场景，每项 {\"name\":\"场景名\",\"prompt\":\"背景画面描述\"}，"
                + "prompt 只写环境、光线、气氛，不要出现人物）。所有文本用中文。";
            var raw = await _api.Chat(new List<ChatMessage>
            {
                new() { Role = "system", Content = system },
                new() { Role = "user", Content = prompt.Trim() }
            });
            draft = ParseWorkshop(raw);
        }
        catch (Exception ex)
        {
            App.WriteLog("CharacterSelect.AiCreate -> " + ex);
            draft = null;
        }
        finally { AiBusy = false; }

        if (draft is null || string.IsNullOrWhiteSpace(draft.Name))
        {
            Toast.Error("这次没有生成出可用的角色设定，请稍后再试或换个描述。");
            return;
        }

        var scenes = draft.Scenes.Where(s => !string.IsNullOrWhiteSpace(s.Name)).Take(3).ToList();
        var detail = $"性别：{draft.Gender}\n性格：{draft.Personality}\n\n{draft.Description}";
        if (scenes.Count > 0) detail += "\n\n配景：" + string.Join("、", scenes.Select(s => s.Name));
        if (!await Shell.Current.DisplayAlert($"「{draft.Name}」", detail, "创建", "取消")) return;

        var created = _library.CreateDefault(draft.Name, draft.Gender, draft.Personality);
        var profile = created.Profile with
        {
            Description = string.IsNullOrWhiteSpace(draft.Description) ? prompt.Trim() : draft.Description.Trim(),
            Greeting = string.IsNullOrWhiteSpace(draft.Greeting) ? created.Profile.Greeting : draft.Greeting.Trim(),
            UserAddress = string.IsNullOrWhiteSpace(draft.UserAddress) ? created.Profile.UserAddress : draft.UserAddress.Trim()
        };
        created = created with { Profile = profile };
        if (!await _library.AddAsync(created))
        {
            Toast.Error("角色保存失败。");
            return;
        }

        // 头像：有画面生成时一并生成一张；失败不影响角色本身
        if (_images.Enabled)
        {
            var look = string.IsNullOrWhiteSpace(profile.Description) ? profile.Personality : profile.Description;
            var (path, _) = await _images.GenerateAsync(
                $"{profile.Name} 的头像，{look}。干净背景，不要文字", "character_" + profile.Id);
            if (path is not null)
            {
                var dir = Path.Combine(_store.Root, "assets", "characters", profile.Id, "avatar");
                Directory.CreateDirectory(dir);
                var dest = Path.Combine(dir, "avatar.png");
                File.Copy(path, dest, true);
                created = created with { Avatar = $"assets/characters/{profile.Id}/avatar/avatar.png" };
                await _library.UpdateAsync(created);
            }
        }

        // 配景：经用户同意才画，毕竟每张都要花时间与额度
        var madeScenes = 0;
        if (scenes.Count > 0 && _images.Enabled
            && await Shell.Current.DisplayAlert("配景",
                $"要现在为 TA 生成 {scenes.Count} 张场景背景并接进地图吗？", "生成", "稍后"))
        {
            try
            {
                await _maps.InitializeAsync();
                var location = _maps.Map.Locations.FirstOrDefault();
                if (location is not null)
                {
                    foreach (var s in scenes)
                    {
                        var scene = _maps.AddScene(location.Id, s.Name.Trim());
                        var current = _maps.CurrentScene;
                        if (current is not null) _maps.AddEdge(current.Id, scene.Id);
                        var (path, _) = await _images.GenerateAsync(
                            $"场景背景：{s.Name}，{s.Prompt}。横向构图，不要人物，不要文字",
                            "scene_" + scene.Id, "1536x1024");
                        if (path is null) continue;
                        var rel = _maps.ImportBackgroundForScene(path, scene);
                        if (rel is null) continue;
                        scene.Background = rel;
                        scene.Placement = null;
                        madeScenes++;
                    }
                    await _maps.SaveAsync();
                    _maps.NotifyChanged();
                }
            }
            catch (Exception ex)
            {
                App.WriteLog("CharacterSelect.WorkshopScenes -> " + ex);
            }
        }

        await Refresh();
        var tail = madeScenes > 0 ? $"\n已生成 {madeScenes} 个场景，第一次进入地图时会请你标定。" : "";
        if (await Shell.Current.DisplayAlert("AI 造角色",
                $"「{profile.Name}」已加入角色库。{tail}\n\n现在和 TA 开始一局吗？", "开始", "稍后"))
        {
            await Shell.Current.GoToAsync($"newrun?character={Uri.EscapeDataString(profile.Id)}");
        }
    }

    private static CharacterWorkshopDraft? ParseWorkshop(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var start = raw.IndexOf('{');
        var end = raw.LastIndexOf('}');
        if (start < 0 || end <= start) return null;
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<CharacterWorkshopDraft>(
                raw[start..(end + 1)],
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex)
        {
            App.WriteLog("CharacterSelect.ParseWorkshop -> " + ex.Message);
            return null;
        }
    }

    [RelayCommand]
    private async Task ImportCharacter()
    {
        try
        {
            var pick = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "选择角色包",
                FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    { DevicePlatform.WinUI, new[] { ".zip" } },
                    { DevicePlatform.Android, new[] { "application/zip" } },
                    { DevicePlatform.iOS, new[] { "public.zip-archive" } }
                })
            });
            if (pick is null) return;
            var import = await _library.ImportFromZipAsync(pick.FullPath);
            if (import.ok) Toast.Success(import.message);
            else Toast.Error("导入失败：" + import.message);
            if (import.ok) await Refresh();

            // 素材包里的地图必须走同样的标定流程。这里把队列统一驱动一遍，
            // 而不是等用户切到那个场景时被动撞上标定页。
            if (_cal.PendingCount > 0)
            {
                var n = _cal.PendingCount;
                var go = await Shell.Current.DisplayAlert("地图标定",
                    $"这个素材包带进来 {n} 张地图背景，需要依次标定（落脚点 + 缩放）。现在开始吗？",
                    "开始标定", "稍后再说");
                if (go) await _cal.DrainAsync(SceneCalibrationService.DefaultTargetAspect);
            }
        }
        catch (Exception ex)
        {
            App.WriteLog("CharacterSelect.ImportCharacter -> " + ex);
        }
    }

    [RelayCommand]
    private async Task Back() => await Shell.Current.GoToAsync("..");
}
