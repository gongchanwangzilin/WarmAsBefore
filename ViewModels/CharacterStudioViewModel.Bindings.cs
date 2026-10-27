using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.Controls;
using WarmAsBefore.Controls;
using WarmAsBefore.Models;
using WarmAsBefore.Services;

namespace WarmAsBefore.ViewModels;

/// <summary>
/// 工作室的第二层：共享/非共享数值、CG 收藏、世界书与角色世界观、按角色的语音与模型。
///
/// 落盘口径分两类：
/// · 离散的「绑定」即时写（世界书勾选、世界观新建/启用/删除、语音引擎、模型厂商）——
///   这些是一次性选择，攒到「保存」会让用户看不到操作是否生效；
/// · 成段的文字仍走页头「保存」（世界观的名字与描述也一并收进去）——
///   一行没打完就落盘，改到一半的那句话会变成盘上的正式版本。
/// 两条路都通过 <see cref="Toast"/> 反馈成功或失败。
/// </summary>
public sealed partial class CharacterStudioViewModel
{
    // ══ ① 共享 / 非共享数值 ════════════════════════════════════════════

    [ObservableProperty] private bool _stateShared = true;
    [ObservableProperty] private string _stateSharedHint = "";

    /// <summary>共享角色的实时性欲（State.Libido）。非共享时这一栏不显示，改的是 Initial.Libido。</summary>
    [ObservableProperty] private double _libido = 20;
    [ObservableProperty] private bool _libidoLocked;
    [ObservableProperty] private double _libidoLockValue = 20;

    // 非共享角色的「建档初始值」。它们只决定新建存档时的起点，所以单独一组字段，
    // 不与上面那三根滑杆共用 —— 共用就一定会有人以为拉一下等于改了这一档的进度。
    [ObservableProperty] private double _initAffection;
    [ObservableProperty] private double _initTrust;
    [ObservableProperty] private double _initEnergy = 100;
    [ObservableProperty] private double _initLibido = 20;
    [ObservableProperty] private string _initAffectionPoints = "0";

    /// <summary>非共享且正在某个存档里时才为真：那时才有一份「这一档现在的实际值」可以对照。</summary>
    [ObservableProperty] private bool _hasSaveStatePreview;
    [ObservableProperty] private string _saveStatePreview = "";

    /// <summary>全局性欲系统关着时，工作室要给一句说明，而不是静默摆着两根不参与游戏的滑杆。</summary>
    public bool LibidoSystemOff => !_settings.Current.LibidoSystemEnabled;

    partial void OnLibidoChanged(double value) => Note("性欲");
    partial void OnLibidoLockedChanged(bool value) => Note("性欲设定锁");
    partial void OnLibidoLockValueChanged(double value) => Note("性欲设定锁");
    partial void OnInitAffectionChanged(double value) => Note("初始好感");
    partial void OnInitTrustChanged(double value) => Note("初始信任");
    partial void OnInitEnergyChanged(double value) => Note("初始精力");
    partial void OnInitLibidoChanged(double value) => Note("初始性欲");
    partial void OnInitAffectionPointsChanged(string value) => Note("初始好感积分");

    /// <summary>
    /// 共享 ↔ 非共享。切过去之后立刻落盘：这颗开关换的是「盘上数值属于谁」，
    /// 拖到「保存」再写，用户可能已经关掉这一页，看到的却是旧口径。
    /// 两边的解释都要说清「已有存档不会因此倒回去」——这是最容易被误解的一步。
    /// </summary>
    [RelayCommand]
    private async Task ToggleStateShared()
    {
        var next = !StateShared;
        CharacterData updated;
        if (next)
        {
            updated = _ch with { StateShared = true };
            if (!await PersistExtendedAsync(updated, "已改为共享：这个角色的数值只有一份（现实值），所有存档共用。")) return;
            Toast.Info("已有存档里各自存过的那份数字不会再被读出来，但不会被删掉；它们下次读取时改用这一份现实值。");
        }
        else
        {
            // 新建存档的起点先照抄这一刻的现实值：改口径不应把以后每一档都打回 0。
            var seeded = new CharacterInitialStats
            {
                Affection = Clamp(Affection, 0, 100),
                AffectionPoints = Math.Clamp(ParseInt(AffectionPoints, _ch.State.AffectionPoints), 0, 5000),
                Trust = Clamp(Trust, 0, 100),
                Energy = Clamp(Energy, 0, 100),
                Libido = Clamp(Libido, 0, 100)
            };
            updated = _ch with { StateShared = false, Initial = seeded };
            if (!await PersistExtendedAsync(updated, "已改为非共享：之后新建的存档从「建档初始值」开始。")) return;
            Toast.Info("已有存档的数值不会倒回初始值——它们各自留着这一档的进度，下面那条只用来对照。");
        }

        StateShared = _ch.StateShared;
        LoadInitialFields(_ch);
        RefreshStateDerived();
    }

    private void LoadInitialFields(CharacterData ch)
    {
        var init = ch.Initial ?? new CharacterInitialStats();
        InitAffection = Math.Clamp(init.Affection, 0, 100);
        InitTrust = Math.Clamp(init.Trust, 0, 100);
        InitEnergy = Math.Clamp(init.Energy, 0, 100);
        InitLibido = Math.Clamp(init.Libido, 0, 100);
        InitAffectionPoints = Math.Max(0, init.AffectionPoints).ToString(CultureInfo.InvariantCulture);
    }

    private CharacterInitialStats BuildInitial() => new()
    {
        Affection = Clamp(InitAffection, 0, 100),
        AffectionPoints = Math.Clamp(ParseInt(InitAffectionPoints, 0), 0, 5000),
        Trust = Clamp(InitTrust, 0, 100),
        Energy = Clamp(InitEnergy, 0, 100),
        Libido = Clamp(InitLibido, 0, 100)
    };

    /// <summary>
    /// 只读对照：这一档现在实际是多少。取的是 <c>_ch.State</c> —— 非共享角色读档时
    /// SaveManager 已经把这一档的那份快照装进内存里的 State，所以它就是「这一档现在的值」。
    /// 有当前存档才显示：没有档的时候那串数字只是初始化出来的默认，摆出来会被当成进度。
    /// </summary>
    private void RefreshStateDerived()
    {
        StateSharedHint = StateShared
            ? "现实值，所有存档共用：这里改的是 TA 真实的进度。"
            : "只决定新建存档时的初始状态，不影响已有存档。";
        HasSaveStatePreview = !StateShared && !string.IsNullOrEmpty(_engine.CurrentSaveId);
        SaveStatePreview = HasSaveStatePreview ? BuildSaveCompare(_ch) : "";
        OnPropertyChanged(nameof(LibidoSystemOff));
    }

    private static string BuildSaveCompare(CharacterData ch)
    {
        var st = ch.State;
        var parts = new List<string>
        {
            $"好感 {Math.Clamp(st.Affection, 0, 100)}",
            $"信任 {Math.Clamp(st.Trust, 0, 100)}",
            $"精力 {Math.Clamp(st.Energy, 0, 100)}",
            $"性欲 {Math.Clamp(st.Libido, 0, 100)}"
        };
        if (st.LibidoLock is { } locked) parts.Add($"设定锁 {locked}");
        return string.Join(" · ", parts);
    }

    // ══ ② CG 收藏 ═════════════════════════════════════════════════════

    public ObservableCollection<StudioCgItem> CgItems { get; } = new();
    [ObservableProperty] private bool _hasCgItems;
    [ObservableProperty] private string _cgSummary = "";

    /// <summary>场景 id → 名字。地图场景与场景库条目共用一个表：CG 的 SceneId 两种都指过。</summary>
    private readonly Dictionary<string, string> _sceneNames = new(StringComparer.Ordinal);
    private bool _sceneNamesLoaded;

    private void ResetCg()
    {
        CgItems.Clear();
        HasCgItems = false;
        CgSummary = "";
    }

    [RelayCommand]
    private Task RefreshCg() => RefreshCgCoreAsync();

    /// <summary>
    /// 铺这一栏的 CG。每次翻到这一栏都重读一次目录：CG 是剧情里现解锁的，
    /// 进来时缓存里那一份可能比盘上旧。
    /// </summary>
    private async Task RefreshCgCoreAsync()
    {
        var charId = CharacterId;
        if (string.IsNullOrWhiteSpace(charId)) { ResetCg(); return; }
        try
        {
            await _cg.RefreshAsync();
            await EnsureSceneNamesAsync();
            var items = _cg.Catalog
                .Where(c => string.Equals((c.CharacterId ?? "").Trim(), charId, StringComparison.Ordinal))
                .OrderBy(c => CategoryRank(c.Category))
                .ThenBy(c => c.Title, StringComparer.CurrentCulture)
                .Select(BuildCgItem)
                .ToList();
            // 补图期间换了人：这一批属于上一位，别铺到这一位名下。
            if (!string.Equals(CharacterId, charId, StringComparison.Ordinal)) return;

            CgItems.Clear();
            foreach (var item in items) CgItems.Add(item);
            HasCgItems = CgItems.Count > 0;
            var unlocked = items.Count(i => i.Unlocked);
            CgSummary = HasCgItems ? $"共 {items.Count} 张 · 已解锁 {unlocked} 张" : "";
        }
        catch (Exception ex)
        {
            App.WriteLog("Studio.RefreshCg -> " + ex);
            ResetCg();
            Toast.Error("CG 目录没读出来：" + ex.Message);
        }
    }

    private StudioCgItem BuildCgItem(CgRecord cg)
    {
        var path = _cg.ResolvePath(cg);
        var size = cg.SizeBytes;
        if (size <= 0 && !string.IsNullOrWhiteSpace(path))
        {
            try { size = new FileInfo(path).Length; } catch { }
        }

        var thumb = ThumbOf(path);
        var unlocked = _cg.IsUnlocked(cg.File) || cg.Unlocked;
        var category = string.IsNullOrWhiteSpace(cg.Category) ? "其他" : CategoryLabel(cg.Category);

        var duration = cg.DurationMs > 0
            ? (cg.DurationMs / 1000.0).ToString("0.0", CultureInfo.InvariantCulture) + " 秒"
            : "静止图";
        var sizeLabel = size > 0 ? DataPackageService.FormatSize(size) : "大小未探测";

        var scene = string.IsNullOrWhiteSpace(cg.SceneId) ? "" : SceneNameOf(cg.SceneId.Trim());
        var background = string.IsNullOrWhiteSpace(cg.BackgroundPath)
            ? "" : Path.GetFileName(cg.BackgroundPath.Trim().Replace('\\', '/'));
        var sceneLine = scene.Length > 0 ? "场景 · " + scene : "场景 · 未关联";
        if (background.Length > 0) sceneLine += " · 背景 " + background;

        // 引入条件优先给人话（UnlockHint）；没有就把机器条件原样摆出来 —— 看不懂也比空着强。
        var condition = !string.IsNullOrWhiteSpace(cg.UnlockHint)
            ? cg.UnlockHint.Trim()
            : !string.IsNullOrWhiteSpace(cg.Condition)
                ? cg.Condition.Trim()
                : "一直可见";

        return new StudioCgItem
        {
            Title = string.IsNullOrWhiteSpace(cg.Title) ? "未命名 CG" : cg.Title.Trim(),
            File = cg.File ?? "",
            Path = path ?? "",
            Thumb = thumb,
            MediaLabel = thumb is not null ? "" : path is null ? "缺文件" : "影像",
            CategoryLabel = category,
            MetaLine = category + " · " + duration + " · " + sizeLabel,
            SceneLine = sceneLine,
            ConditionLine = "引入条件 · " + condition,
            UnlockLabel = unlocked ? "已解锁" : "未解锁",
            Unlocked = unlocked,
            LockedHint = unlocked ? "" : "还没在剧情里触发过"
        };
    }

    /// <summary>
    /// 缩略图只认静态图片格式：视频/GIF 没有可用的首帧（CgStore 不做抽帧），
    /// 拿 ImageSource.FromFile 去喂它们只会得到一张破图，不如如实写「影像」。
    /// </summary>
    private static ImageSource? ThumbOf(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext is not (".png" or ".jpg" or ".jpeg" or ".webp" or ".bmp" or ".gif")) return null;
        try { return ImageSource.FromFile(path); } catch { return null; }
    }

    private static int CategoryRank(string? category) => (category ?? "").Trim().ToLowerInvariant() switch
    {
        "main" => 0,
        "event" => 1,
        "scene" => 2,
        "ending" => 3,
        _ => 4
    };

    private static string CategoryLabel(string? category) => (category ?? "").Trim().ToLowerInvariant() switch
    {
        "main" => "主线",
        "event" => "事件",
        "scene" => "场景",
        "ending" => "结局",
        _ => string.IsNullOrWhiteSpace(category) ? "其他" : category.Trim()
    };

    private string SceneNameOf(string sceneId) =>
        _sceneNames.TryGetValue(sceneId, out var name) && !string.IsNullOrWhiteSpace(name) ? name : sceneId;

    /// <summary>
    /// 场景名解析：地图与场景库都从盘上读一次（只读、不初始化那两个服务）。
    /// 走 MapService/SceneDirector 会顺带启动它们的定时器与天气刷新，为了显示一个名字不值得。
    /// </summary>
    private async Task EnsureSceneNamesAsync()
    {
        if (_sceneNamesLoaded) return;
        _sceneNamesLoaded = true;
        try
        {
            var map = await _store.Load<MapDefinition>("mapdata");
            if (map is not null)
            {
                foreach (var scene in map.AllScenes)
                    if (!string.IsNullOrWhiteSpace(scene.Id) && !string.IsNullOrWhiteSpace(scene.Name))
                        _sceneNames[scene.Id] = scene.Name;
            }
            var library = await _store.Load<List<SceneLibraryEntry>>("scene_library");
            if (library is not null)
            {
                foreach (var entry in library)
                    if (!string.IsNullOrWhiteSpace(entry.Id) && !string.IsNullOrWhiteSpace(entry.Name)
                        && !_sceneNames.ContainsKey(entry.Id))
                        _sceneNames[entry.Id] = entry.Name;
            }
        }
        catch (Exception ex)
        {
            App.WriteLog("Studio.SceneNames -> " + ex);
        }
    }

    /// <summary>解锁过的静态 CG 才给打开：解锁之前把它放大看一遍，等于绕开了收藏册本身。</summary>
    [RelayCommand]
    private async Task OpenCg(StudioCgItem? item)
    {
        if (item is null || !item.Unlocked || item.Thumb is null) return;
        _cgView.ImagePath = item.Path;
        _cgView.Title = item.Title;
        _cgView.HasPayload = true;
        await Shell.Current.GoToAsync("cg-view");
    }

    // ══ ③ 世界书 / 角色世界观 ═════════════════════════════════════════

    public ObservableCollection<StudioAxisItem> Worldbooks { get; } = new();
    public ObservableCollection<StudioWorldviewItem> Worldviews { get; } = new();
    public ObservableCollection<StudioAxisItem> WorldviewBooks { get; } = new();

    [ObservableProperty] private bool _hasWorldviews;
    [ObservableProperty] private bool _hasWorldviewEditor;
    [ObservableProperty] private string _worldbookHint = "";
    [ObservableProperty] private string _activeWorldviewName = "";
    [ObservableProperty] private string _worldviewName = "";
    [ObservableProperty] private string _worldviewDescription = "";
    [ObservableProperty] private bool _worldviewCrossSave = true;
    [ObservableProperty] private string _worldviewCrossSaveHint = "";

    /// <summary>世界书 id → 标题。绑定了但已经被删掉的书也要留一条，否则它会静默从清单里消失。</summary>
    private readonly Dictionary<string, string> _worldbookTitles = new(StringComparer.Ordinal);
    private string _editingWorldviewId = "";
    private bool _editingSync;

    partial void OnWorldviewNameChanged(string value) { if (!_editingSync) Note("世界观"); }
    partial void OnWorldviewDescriptionChanged(string value) { if (!_editingSync) Note("世界观"); }

    /// <summary>
    /// 读世界书清单。放在后台：这是唯一一次要读「世界树」整表的动作，
    /// 不能挡在「这一页先亮出来」前面，否则读盘的几十毫秒里页面点了没有反应。
    /// </summary>
    private async Task LoadWorldbookListAsync(IReadOnlyList<string> boundIds, string charId)
    {
        try
        {
            var list = await _worldTree.ListAsync();
            _worldbookTitles.Clear();
            foreach (var entry in list)
            {
                if (string.IsNullOrWhiteSpace(entry.Id)) continue;
                _worldbookTitles[entry.Id] = string.IsNullOrWhiteSpace(entry.Title) ? entry.Id : entry.Title.Trim();
            }
            foreach (var id in boundIds)
                if (!string.IsNullOrWhiteSpace(id) && !_worldbookTitles.ContainsKey(id))
                    _worldbookTitles[id] = "（已删除的世界书 " + id + "）";

            if (!string.Equals(CharacterId, charId, StringComparison.Ordinal)) return;
            FillWorldbooks(boundIds);
            RebuildWorldviewBooks();
        }
        catch (Exception ex)
        {
            App.WriteLog("Studio.LoadWorldbooks -> " + ex);
            WorldbookHint = "世界书清单没读出来（详见日志）：这一栏暂时只剩绑定关系。";
        }
    }

    private void FillWorldbooks(IReadOnlyList<string> selectedIds)
    {
        var selected = new HashSet<string>(selectedIds.Where(i => !string.IsNullOrWhiteSpace(i)), StringComparer.Ordinal);
        Worldbooks.Clear();
        foreach (var (id, title) in _worldbookTitles)
            Worldbooks.Add(new StudioAxisItem(id, title) { IsOn = selected.Contains(id) });
        WorldbookHint = selected.Count == 0
            ? "还没选世界书：TA 的世界观只靠设定文字。可多本复合，作为生成 TA 世界观的语料。"
            : $"已选 {selected.Count} 本 · 可多本复合，作为生成 TA 世界观的语料。";
    }

    [RelayCommand]
    private async Task ToggleWorldbook(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return;
        var current = _ch.WorldbookIds ?? new List<string>();
        var next = new List<string>(current);
        if (!next.Remove(id)) next.Add(id);

        var updated = _ch with { WorldbookIds = next };
        if (await PersistExtendedAsync(updated, next.Contains(id) ? "已把这本世界书绑给 TA。" : "已把这本世界书从 TA 身上解绑。"))
        {
            FillWorldbooks(_ch.WorldbookIds ?? new List<string>());
        }
    }

    /// <summary>
    /// 把角色上的世界观清单铺成单选行。启用的那一套用 <c>ActiveWorldviewId</c> 认，
    /// 不拿「第一套」兜底 —— 用户没选过就是没选过，替 TA 挑一套会让存档读到别的世界。
    /// </summary>
    private void SyncWorldviews(CharacterData ch)
    {
        var list = ch.Worldviews ?? new List<CharacterWorldview>();
        var active = (ch.ActiveWorldviewId ?? "").Trim();
        if (active.Length > 0 && list.All(w => w.Id != active)) active = "";

        _editingWorldviewId = active.Length > 0
            ? active
            : list.FirstOrDefault()?.Id ?? "";

        Worldviews.Clear();
        foreach (var w in list)
            Worldviews.Add(new StudioWorldviewItem(
                w.Id,
                string.IsNullOrWhiteSpace(w.Name) ? "（未命名世界观）" : w.Name.Trim(),
                w.Id == active,
                WorldviewSummary(w)));

        HasWorldviews = Worldviews.Count > 0;
        WorldviewCrossSave = ch.WorldviewCrossSave;
        ActiveWorldviewName = active.Length == 0
            ? (HasWorldviews ? "还没指定默认世界观" : "没有角色世界观")
            : Worldviews.FirstOrDefault(v => v.Id == active)?.Name ?? "还没指定默认世界观";
        RefreshWorldviewCrossSaveHint();
        LoadWorldviewEditor(ch);
    }

    private static string WorldviewSummary(CharacterWorldview w)
    {
        var books = (w.WorldbookIds ?? new List<string>()).Count(i => !string.IsNullOrWhiteSpace(i));
        var text = $"{books} 本世界书";
        var desc = (w.Description ?? "").Trim().Replace('\n', ' ');
        if (desc.Length == 0) return text + " · 还没有描述";
        return text + " · " + (desc.Length <= 32 ? desc : desc[..32] + "…");
    }

    private void LoadWorldviewEditor(CharacterData ch)
    {
        var w = (ch.Worldviews ?? new List<CharacterWorldview>()).FirstOrDefault(x => x.Id == _editingWorldviewId);
        _editingSync = true;
        try
        {
            HasWorldviewEditor = w is not null;
            WorldviewName = w?.Name ?? "";
            WorldviewDescription = w?.Description ?? "";
        }
        finally { _editingSync = false; }
        RebuildWorldviewBooks();
    }

    private void RebuildWorldviewBooks()
    {
        var w = _ch.Worldviews?.FirstOrDefault(x => x.Id == _editingWorldviewId);
        var selected = new HashSet<string>(w?.WorldbookIds ?? new List<string>(), StringComparer.Ordinal);
        WorldviewBooks.Clear();
        foreach (var (id, title) in _worldbookTitles)
            WorldviewBooks.Add(new StudioAxisItem(id, title) { IsOn = selected.Contains(id) });
    }

    private void RefreshWorldviewCrossSaveHint() => WorldviewCrossSaveHint = WorldviewCrossSave
        ? "开：在存档里改世界观，会写回角色本身，所有存档一起变。"
        : "关：在存档里改世界观只影响那一档，角色本身不动，换一份存档还是这一套。";

    [RelayCommand]
    private async Task ToggleWorldviewCrossSave()
    {
        var next = !WorldviewCrossSave;
        if (!await PersistExtendedAsync(_ch with { WorldviewCrossSave = next },
                next ? "世界观改动将跨存档生效。" : "世界观改动只在本存档内生效。"))
        {
            WorldviewCrossSave = _ch.WorldviewCrossSave;
            RefreshWorldviewCrossSaveHint();
            return;
        }
        WorldviewCrossSave = _ch.WorldviewCrossSave;
        RefreshWorldviewCrossSaveHint();
        Toast.Info(next
            ? "之后在游戏里换世界观，改的是角色上的这一套：别的存档也看得到。"
            : "之后在游戏里换世界观，只在当前这一档生效：角色本身的世界观不会跟着动。");
    }

    [RelayCommand]
    private async Task NewWorldview()
    {
        var list = new List<CharacterWorldview>(_ch.Worldviews ?? new List<CharacterWorldview>());
        var n = 1;
        while (list.Any(w => string.Equals(w.Name, $"世界观 {n}", StringComparison.Ordinal))) n++;
        var created = new CharacterWorldview
        {
            Name = $"世界观 {n}",
            // 新的一套先抄上面勾好的世界书：多数时候「再开一套」是在同一批语料上换个说法。
            WorldbookIds = new List<string>(_ch.WorldbookIds ?? new List<string>())
        };
        list.Add(created);

        if (await PersistExtendedAsync(_ch with { Worldviews = list, ActiveWorldviewId = created.Id },
                $"已新建并启用「{created.Name}」。"))
        {
            SyncWorldviews(_ch);
        }
    }

    [RelayCommand]
    private async Task SelectWorldview(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return;
        if (string.Equals((_ch.ActiveWorldviewId ?? "").Trim(), id, StringComparison.Ordinal))
        {
            _editingWorldviewId = id;
            LoadWorldviewEditor(_ch);
            return;
        }
        if (await PersistExtendedAsync(_ch with { ActiveWorldviewId = id }, "已把这一套设为 TA 的默认世界观。"))
            SyncWorldviews(_ch);
    }

    /// <summary>
    /// 删除是不可逆的（这套的书单与描述一起没了，世界书本身不受影响），所以这里仍然用
    /// 阻断式确认而不是 Toast —— 那一下需要用户真的做决定。
    /// </summary>
    [RelayCommand]
    private async Task DeleteWorldview()
    {
        var w = (_ch.Worldviews ?? new List<CharacterWorldview>()).FirstOrDefault(x => x.Id == _editingWorldviewId);
        if (w is null) return;
        var name = string.IsNullOrWhiteSpace(w.Name) ? "这套世界观" : w.Name.Trim();
        var yes = await Shell.Current.DisplayAlert("删除这一套世界观",
            $"「{name}」会从角色上删掉，它用到的世界书本身不受影响。", "删掉", "留下");
        if (!yes) return;

        var list = (_ch.Worldviews ?? new List<CharacterWorldview>()).Where(x => x.Id != w.Id).ToList();
        var active = string.Equals((_ch.ActiveWorldviewId ?? "").Trim(), w.Id, StringComparison.Ordinal)
            ? "" : _ch.ActiveWorldviewId;
        if (await PersistExtendedAsync(_ch with { Worldviews = list, ActiveWorldviewId = active },
                $"已删掉「{name}」。"))
        {
            SyncWorldviews(_ch);
        }
    }

    [RelayCommand]
    private async Task ToggleWorldviewBook(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return;
        var item = WorldviewBooks.FirstOrDefault(b => b.Key == id);
        if (item is null) return;
        item.IsOn = !item.IsOn;
        await PersistWorldviewEditsAsync(item.IsOn ? "已把这本书加进这套世界观。" : "已把这本书从这套世界观里拿掉。");
    }

    [RelayCommand]
    private Task SaveWorldview() => PersistWorldviewEditsAsync("这一套世界观已保存。");

    /// <summary>
    /// 把编辑区里正在写的那一套写回角色。名字/描述/书单一次收齐：
    /// 勾一本书时也走这里，所以「刚敲到一半的描述」不会因为点了一下勾就被丢掉。
    /// </summary>
    private async Task<bool> PersistWorldviewEditsAsync(string message)
    {
        if (string.IsNullOrWhiteSpace(_editingWorldviewId)) return false;
        var current = (_ch.Worldviews ?? new List<CharacterWorldview>())
            .FirstOrDefault(x => x.Id == _editingWorldviewId);
        if (current is null) return false;

        var books = WorldviewBooks.Where(b => b.IsOn).Select(b => b.Key).ToList();
        var name = (WorldviewName ?? "").Trim();
        var description = (WorldviewDescription ?? "").Trim();
        var unchanged = (name.Length == 0 || name == current.Name)
                        && description == (current.Description ?? "").Trim()
                        && books.SequenceEqual(current.WorldbookIds ?? new List<string>(), StringComparer.Ordinal);
        if (unchanged)
        {
            Toast.Info("这一套没有改动。");
            return false;
        }

        var fresh = current with
        {
            Name = name.Length == 0 ? current.Name : name,
            Description = description,
            WorldbookIds = books,
            UpdatedAt = DateTime.UtcNow
        };
        var list = (_ch.Worldviews ?? new List<CharacterWorldview>())
            .Select(x => x.Id == fresh.Id ? fresh : x).ToList();
        if (!await PersistExtendedAsync(_ch with { Worldviews = list }, message)) return false;
        SyncWorldviews(_ch);
        return true;
    }

    // ══ ④ 按角色的语音与模型 ═══════════════════════════════════════════

    [ObservableProperty] private bool _voiceEnabled = true;
    [ObservableProperty] private string _voiceNameText = "";
    [ObservableProperty] private string _voiceExtraText = "";
    [ObservableProperty] private string _voiceEngine = "";
    [ObservableProperty] private string _voiceSummary = "";

    public List<StudioAxisItem> VoiceEngines { get; } = new()
    {
        new("", "跟随全局"),
        new("system", "系统语音"),
        new("api", "外部 TTS 接口"),
        new("sovits", "GPT-SoVITS")
    };

    partial void OnVoiceNameTextChanged(string value) => Note("音色");
    partial void OnVoiceExtraTextChanged(string value) => Note("语音参数");

    public ObservableCollection<StudioAxisItem> ModelProviders { get; } = new();
    public ObservableCollection<StudioAxisItem> ModelChoices { get; } = new();
    [ObservableProperty] private string _modelHint = "";

    private string _selectedProviderId = "";
    private string _selectedModel = "";

    private void LoadVoiceAndModel(CharacterData ch)
    {
        var voice = ch.Voice ?? new CharacterVoice();
        VoiceEnabled = voice.Enabled;
        VoiceNameText = voice.VoiceName ?? "";
        VoiceExtraText = voice.Extra ?? "";
        VoiceEngine = (voice.Engine ?? "").Trim();
        SyncChoice(VoiceEngines, VoiceEngine);
        RefreshVoiceSummary();

        var bind = ch.Model ?? new CharacterModelBinding();
        _selectedProviderId = (bind.ProviderId ?? "").Trim();
        _selectedModel = (bind.Model ?? "").Trim();
        RebuildModelProviders();
    }

    private CharacterVoice BuildVoice(bool enabled) => new()
    {
        Enabled = enabled,
        VoiceName = (VoiceNameText ?? "").Trim(),
        Engine = (VoiceEngine ?? "").Trim(),
        Extra = (VoiceExtraText ?? "").Trim()
    };

    private void RefreshVoiceSummary()
    {
        var voice = _ch.Voice ?? new CharacterVoice();
        if (!voice.Enabled)
        {
            VoiceSummary = "这个角色不出声：朗读与配音都会跳过 TA。";
            return;
        }
        var engine = (voice.Engine ?? "").Trim() switch
        {
            "" => "引擎跟随全局",
            "system" => "系统语音",
            "api" => "外部 TTS 接口",
            "sovits" => "GPT-SoVITS",
            var other => other
        };
        var name = string.IsNullOrWhiteSpace(voice.VoiceName) ? "音色跟随全局" : "音色 " + voice.VoiceName.Trim();
        var extra = string.IsNullOrWhiteSpace(voice.Extra) ? "" : " · 参数 " + voice.Extra.Trim();
        VoiceSummary = engine + " · " + name + extra;
    }

    [RelayCommand]
    private async Task ToggleVoiceEnabled()
    {
        var next = !VoiceEnabled;
        var updated = _ch with { Voice = BuildVoice(next) };
        if (!await PersistExtendedAsync(updated, next ? "这个角色开声了。" : "这个角色不出声了。"))
        {
            VoiceEnabled = _ch.Voice?.Enabled ?? true;
            return;
        }
        VoiceEnabled = _ch.Voice?.Enabled ?? true;
        RefreshVoiceSummary();
    }

    [RelayCommand]
    private async Task PickVoiceEngine(string? engine)
    {
        VoiceEngine = (engine ?? "").Trim();
        SyncChoice(VoiceEngines, VoiceEngine);
        if (await PersistExtendedAsync(_ch with { Voice = BuildVoice(VoiceEnabled) }, "语音引擎已按这一档保存。"))
            RefreshVoiceSummary();
    }

    [RelayCommand]
    private async Task SaveVoice()
    {
        if (await PersistExtendedAsync(_ch with { Voice = BuildVoice(VoiceEnabled) }, "这个角色的语音设置已保存。"))
        {
            RefreshVoiceSummary();
            RaiseAll();
        }
    }

    /// <summary>
    /// 厂商清单只收「启用且填了地址」的那些：这正是轮换池会去用的集合
    /// （与 CharacterSettingsResolver.BoundEndpoint 同一口径），列出一家永远连不上的厂商只会误导。
    /// </summary>
    private void RebuildModelProviders()
    {
        var providers = (_settings.Current.AiProviders ?? new List<AiProvider>())
            .Where(p => p.Enabled && !string.IsNullOrWhiteSpace(p.Url))
            .ToList();

        ModelProviders.Clear();
        ModelProviders.Add(new StudioAxisItem("", "跟随全局"));
        foreach (var p in providers)
            ModelProviders.Add(new StudioAxisItem(p.Id, string.IsNullOrWhiteSpace(p.Name) ? p.Id : p.Name.Trim()));

        SyncChoice(ModelProviders, _selectedProviderId);
        RebuildModelChoices(providers);
    }

    private void RebuildModelChoices(List<AiProvider>? providers = null)
    {
        providers ??= (_settings.Current.AiProviders ?? new List<AiProvider>())
            .Where(p => p.Enabled && !string.IsNullOrWhiteSpace(p.Url))
            .ToList();

        ModelChoices.Clear();
        if (providers.Count == 0)
        {
            ModelHint = "设置 → AI 里还没有可用的厂商：填好地址与密钥并打开，这里才有得选。现在 TA 走全局设置。";
            return;
        }
        if (_selectedProviderId.Length == 0)
        {
            ModelHint = "现在跟随全局轮换池：设置里哪一家先启用，TA 就走哪一家。";
            return;
        }

        var provider = providers.FirstOrDefault(p => string.Equals(p.Id, _selectedProviderId, StringComparison.Ordinal));
        if (provider is null)
        {
            ModelHint = "绑定的那家厂商已经不在了（被删掉或关掉了）：TA 会回到全局轮换。";
            return;
        }

        ModelChoices.Add(new StudioAxisItem("", "厂商默认模型"));
        foreach (var m in (provider.Models ?? new List<AiProviderModel>()).Where(m => m.Enabled))
            ModelChoices.Add(new StudioAxisItem(m.Model, m.DisplayName));
        SyncChoice(ModelChoices, _selectedModel);

        var picked = _selectedModel.Length == 0
            ? (provider.Models ?? new List<AiProviderModel>()).FirstOrDefault(m => m.Enabled)?.Model ?? "厂商第一个启用的模型"
            : _selectedModel;
        ModelHint = $"绑在「{(string.IsNullOrWhiteSpace(provider.Name) ? provider.Id : provider.Name.Trim())}」上，模型 {picked}。";
    }

    [RelayCommand]
    private async Task PickModelProvider(string? providerId)
    {
        var next = (providerId ?? "").Trim();
        if (string.Equals(next, _selectedProviderId, StringComparison.Ordinal)) return;
        _selectedProviderId = next;
        _selectedModel = "";      // 换厂商就回到这家自己的默认模型：旧模型名多半不存在于新厂商
        RebuildModelProviders();

        var name = ModelProviders.FirstOrDefault(p => p.Key == next)?.Label ?? next;
        var updated = _ch with { Model = new CharacterModelBinding { ProviderId = next, Model = "" } };
        await PersistExtendedAsync(updated,
            next.Length == 0 ? "这个角色改回跟随全局模型。" : $"已把「{name}」的模型绑给这个角色。");
    }

    [RelayCommand]
    private async Task PickModel(string? model)
    {
        var next = (model ?? "").Trim();
        if (string.Equals(next, _selectedModel, StringComparison.Ordinal)) return;
        _selectedModel = next;
        SyncChoice(ModelChoices, _selectedModel);

        var updated = _ch with
        {
            Model = new CharacterModelBinding { ProviderId = _selectedProviderId, Model = next }
        };
        if (await PersistExtendedAsync(updated, next.Length == 0 ? "已改回这家厂商的默认模型。" : $"模型已换成 {next}。"))
            RebuildModelProviders();
    }

    // ══ 共同的落盘口 ═══════════════════════════════════════════════════

    /// <summary>
    /// 即时写。草稿（还没进名册）只记在草稿上：那时候把它写进名册，
    /// 等于「还没建好就已经存在」，角色库里会冒出一个空名字的陌生人。
    /// 成功/失败都回一句话，失败时 <c>_ch</c> 不动 —— 界面与盘上必须一直是同一份。
    /// </summary>
    private async Task<bool> PersistExtendedAsync(CharacterData updated, string success)
    {
        if (_isNew)
        {
            _ch = updated;
            _draft = _ch;
            Toast.Info("已经记在草稿上：按「建好角色」才落到盘上。");
            return true;
        }
        var ok = await _library.UpdateAsync(updated);
        if (!ok)
        {
            Toast.Error("没能写进盘：详见日志。");
            App.WriteLog("Studio.PersistExtended -> " + success);
            return false;
        }
        _ch = updated;
        Toast.Success(success);
        Saved?.Invoke();
        return true;
    }

    /// <summary>
    /// 打开/换人时的装载。由主文件的 <c>Apply</c> 调一次，字段与提示都在这里成对就位。
    /// </summary>
    private void LoadExtended(CharacterData ch)
    {
        var st = ch.State;
        StateShared = ch.StateShared;
        Libido = Math.Clamp(st.Libido, 0, 100);
        LibidoLocked = st.LibidoLock is not null;
        LibidoLockValue = Math.Clamp(st.LibidoLock ?? st.Libido, 0, 100);
        LoadInitialFields(ch);
        RefreshStateDerived();

        SyncWorldviews(ch);
        LoadVoiceAndModel(ch);
        ResetCg();

        var bound = (ch.WorldbookIds ?? new List<string>()).ToList();
        Worldbooks.Clear();
        WorldviewBooks.Clear();
        WorldbookHint = "正在读世界书清单…";
        _ = LoadWorldbookListAsync(bound, ch.Profile.Id);
    }

    /// <summary>
    /// 保存时把这些字段并进新的那份记录。主文件的 Save 只认识「设定 + 状态」，
    /// 这里补上另外四块 —— 一处合并，才不会有哪一块被 Save 悄悄丢掉。
    /// </summary>
    private CharacterData WithExtended(CharacterData updated)
    {
        updated = updated with
        {
            StateShared = StateShared,
            Initial = StateShared ? updated.Initial : BuildInitial(),
            WorldviewCrossSave = WorldviewCrossSave,
            Voice = BuildVoice(VoiceEnabled),
            Model = new CharacterModelBinding { ProviderId = _selectedProviderId, Model = _selectedModel }
        };

        if (!HasWorldviewEditor || string.IsNullOrWhiteSpace(_editingWorldviewId)) return updated;
        var books = WorldviewBooks.Where(b => b.IsOn).Select(b => b.Key).ToList();
        var name = (WorldviewName ?? "").Trim();
        var list = (updated.Worldviews ?? new List<CharacterWorldview>())
            .Select(w => w.Id != _editingWorldviewId ? w : w with
            {
                Name = name.Length == 0 ? w.Name : name,
                Description = (WorldviewDescription ?? "").Trim(),
                WorldbookIds = books,
                UpdatedAt = DateTime.UtcNow
            })
            .ToList();
        return updated with { Worldviews = list };
    }

    /// <summary>保存成功后把只读的那几行重算一遍（口径提示、对照值、语音摘要、世界观单选）。</summary>
    private void RefreshExtendedAfterSave()
    {
        RefreshStateDerived();
        RefreshVoiceSummary();
        RebuildModelProviders();
        SyncWorldviews(_ch);
        // 世界书清单还没读到（后台那趟没回来）时不要拿空表重铺：那会把整排选项清掉，
        // 看起来像「绑定没了」，实际只是尚未读上来。
        if (_worldbookTitles.Count > 0) FillWorldbooks(_ch.WorldbookIds ?? new List<string>());
        if (IsTabCg) _ = RefreshCgCoreAsync();
    }
}

/// <summary>一套世界观在清单里的一行：启用的是单选，点整行就切过去。</summary>
public sealed class StudioWorldviewItem : INotifyPropertyChanged
{
    public StudioWorldviewItem(string id, string name, bool isOn, string summary)
    {
        Id = id;
        Name = name;
        Summary = summary;
        _isOn = isOn;
    }

    public string Id { get; }
    public string Name { get; }
    public string Summary { get; }

    private bool _isOn;
    public bool IsOn
    {
        get => _isOn;
        set
        {
            if (_isOn == value) return;
            _isOn = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsOn)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>
/// 一张 CG 在工作室里要说的几行字。属性全部预先算好，
/// 模板只负责显示，避免一半是绑定一半是逻辑。
/// </summary>
public sealed class StudioCgItem
{
    public string Title { get; init; } = "";
    public string File { get; init; } = "";
    public string Path { get; init; } = "";
    public ImageSource? Thumb { get; init; }
    public bool HasThumb => Thumb is not null;
    /// <summary>没有缩略图时那一格里写什么（影像 / 缺文件）。</summary>
    public string MediaLabel { get; init; } = "";
    public string CategoryLabel { get; init; } = "";
    public string MetaLine { get; init; } = "";
    public string SceneLine { get; init; } = "";
    public string ConditionLine { get; init; } = "";
    public string UnlockLabel { get; init; } = "";
    public string LockedHint { get; init; } = "";
    public bool Unlocked { get; init; }
}
