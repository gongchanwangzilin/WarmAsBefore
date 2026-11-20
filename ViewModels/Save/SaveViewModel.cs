using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WarmAsBefore.Controls;
using WarmAsBefore.Drawables;
using WarmAsBefore.Models;
using WarmAsBefore.Modules.AiChat;
using WarmAsBefore.Modules.Automation;
using WarmAsBefore.Modules.SaveSystem;
using WarmAsBefore.Services;
using WarmAsBefore.Services.Core;

namespace WarmAsBefore.ViewModels;

public sealed partial class SaveViewModel : ObservableObject
{
    private readonly SaveManager _save;
    private readonly SettingsManager _settings;
    private readonly Modules.NovelImport.NovelAnalyzer _novel;
    private readonly Services.CharacterLibrary _library;
    private readonly Modules.DataPack.SmartImporter _smart;
    [ObservableProperty] private List<SaveSlotItem> _slots = new();
    [ObservableProperty] private bool _novelTesting;

    // ==================== 读档状态层（P0-3，2026-11-14）====================
    // 重入门禁：连续点 5 次「读取」只放行第一次；一次性导航闸防 A/B 双链路重复跳 main。
    private int _loading;
    private int _navigated;
    // 本次加载会话令牌：SaveManager 的阶段 hook 可能在线程池回调，
    // 用它过滤掉别的链路（如 MC 桥自动打开绑定存档）误触发的阶段事件。
    private object? _activeLoadToken;
    private CancellationTokenSource? _loadCts;
    private string _pendingLoadId = "";

    [ObservableProperty] private bool _isRestoring;
    [ObservableProperty] private string _stateTitle = "";
    [ObservableProperty] private string _stateBody = "";
    [ObservableProperty] private int _statePhase;                 // 0..5，0 = 未在读取
    [ObservableProperty] private string _statePhaseText = "";
    [ObservableProperty] private bool _isError;                   // 硬错误：不进 main，页面内常驻
    [ObservableProperty] private string _failureDetail = "";
    [ObservableProperty] private bool _canCancel;                 // 只在阶段 1/2 为 true，按钮永不隐藏
    [ObservableProperty] private bool _showSpinner;
    [ObservableProperty] private bool _canLoad = true;            // 「读取」按钮 IsEnabled
    // 附加辅助属性：MotionTokens.Disabled 时 ShowSpinner=false，改显示静态 ✦（不做逐帧动画）。
    [ObservableProperty] private bool _showStaticGlyph;

    // P2：高损确认态（核心状态段重建，停在存档页等用户确认，不静默进入）。
    [ObservableProperty] private bool _isConfirming;
    [ObservableProperty] private string _confirmDetail = "";
    [ObservableProperty] private bool _hasUnreadable;
    [ObservableProperty] private string _unreadableText = "";
    private SaveRepairReport? _confirmReport;

    // P1：上次进程在读取中途被杀（残留 load_session.marker）→ 存档页一条中性补报 + 查看日志。
    [ObservableProperty] private bool _hasIncompleteLoad;
    [ObservableProperty] private string _incompleteLoadNote = "";

    // 5 行阶段轨各 3 个绑定属性：预建在 XAML，运行时只改值，绝不 Add/Remove 子控件。
    // 当前 ● / 已完成 ✓ / 未开始 ○ / 失败 ✕，符号全在单色白名单 ✓ ✕ ✦ △ ○ ● 内。
    [ObservableProperty] private string _stage1Mark = "○";
    [ObservableProperty] private Color _stage1Ink = Colors.Transparent;
    [ObservableProperty] private string _stage1Glyph = "";
    [ObservableProperty] private string _stage2Mark = "○";
    [ObservableProperty] private Color _stage2Ink = Colors.Transparent;
    [ObservableProperty] private string _stage2Glyph = "";
    [ObservableProperty] private string _stage3Mark = "○";
    [ObservableProperty] private Color _stage3Ink = Colors.Transparent;
    [ObservableProperty] private string _stage3Glyph = "";
    [ObservableProperty] private string _stage4Mark = "○";
    [ObservableProperty] private Color _stage4Ink = Colors.Transparent;
    [ObservableProperty] private string _stage4Glyph = "";
    [ObservableProperty] private string _stage5Mark = "○";
    [ObservableProperty] private Color _stage5Ink = Colors.Transparent;
    [ObservableProperty] private string _stage5Glyph = "";

    public SaveViewModel(SaveManager save, SettingsManager settings, Modules.NovelImport.NovelAnalyzer novel,
        Services.CharacterLibrary library, Modules.DataPack.SmartImporter smart)
    {
        _save = save;
        _settings = settings;
        _novel = novel;
        _library = library;
        _smart = smart;
        NovelTesting = settings.Current.NovelTestingEnabled;
    }

    [RelayCommand]
    private async Task Refresh()
    {
        // P1：残留 marker = 上次进程在读取中途被杀，进存档页时给一条中性补报（一次性）。
        // 放在这里而不是 App.OnStart：本项目没有 OnStart override，且 Refresh 每次进页面都会跑，
        // 时序上比启动期的静态标志更可靠，也不需要跨组件事件。
        ConsumeIncompleteLoadMarker();

        // 两件事互不依赖：存档目录读盘（List）与角色库读盘（ListAsync）并行做。
        // 串行时用户要多等一个来回——这就是「进存档页卡 1 秒」里能白拿回来的那一段。
        List<SaveSlot> list;
        List<CharacterData> roster;
        using (PerfLog.Measure("页 · 存档 · 读取列表"))
        {
            var listTask = _save.List();
            var rosterTask = _library.ListAsync();
            await Task.WhenAll(listTask, rosterTask);
            list = await listTask;
            roster = await rosterTask;
        }

        // 坏档隔离区：列表扫描会记录读不出来的文件；进存档页时把「坏档数量 + 备份提示」曝光给用户。
        var bad = _save.UnreadableSaves;
        HasUnreadable = bad.Count > 0;
        UnreadableText = bad.Count == 1
            ? $"{bad[0]} 无法读取，已备份原件并移出列表；可在数据目录手工找回。"
            : $"{bad.Count} 份存档暂时无法读取，已备份原件并移出列表；可在数据目录手工找回。";

        // 槽位集合本来就是整体替换（List 赋值只发一次属性通知），不存在逐条 Add 的通知风暴。
        // 这里保持不变：集合类型是 List，换成 ObservableCollection 反而多一次 CollectionChanged 重建。
        using (PerfLog.Measure("页 · 存档 · 构造槽位"))
        {
            Slots = list.Select(s => new SaveSlotItem
            {
                Slot = s,
                CharacterName = roster.FirstOrDefault(c => c.Profile.Id == s.Character)?.Profile.Name ?? "未知角色"
            }).ToList();
            NovelTesting = _settings.Current.NovelTestingEnabled;
        }
    }

    [RelayCommand]
    private async Task LoadSlot(string id)
    {
        // 重入门禁：连点 5 次「读取」只触发 1 次 Load（体验契约的防呆）。
        if (Interlocked.CompareExchange(ref _loading, 1, 0) != 0)
        {
            App.TraceStep("SaveVM.LoadSlot.Rejected " + id);
            return;
        }
        var token = new object();
        _activeLoadToken = token;
        _pendingLoadId = id;
        Interlocked.Exchange(ref _navigated, 0);
        using var cts = new CancellationTokenSource();
        _loadCts = cts;
        // 局部终态标记：不依赖「UI 属性是否已刷到」来判断错误态，避免 Ui.Post 排队造成竞态。
        var hardError = false;

        App.TraceStep("SaveVM.LoadSlot.Begin " + id);
        // P1：同步写会话标记（saveId / 起始阶段 / 时间戳）；任一终态由 finally 删除。
        App.WriteLoadMarker(id, 1);
        IsRestoring = true;
        IsError = false;
        FailureDetail = "";
        CanLoad = false;
        StateTitle = "正在读取";
        StateBody = "正在打开这份存档；随时可以返回列表，不必等待。";
        // 真实节点：进入「01 读取文件」（接下来 _library.ListAsync + SaveManager.LoadRawAsync）。
        MarkPhase(1, "读取文件");

        try
        {
            await _library.ListAsync();   // 确保角色库已载入，读档时才能恢复角色状态
            // SaveManager.LoadCore 在真实节点处推进 P2/P3/P4；hook 只把阶段轨更新切回 UI 线程。
            _save.LoadPhaseHook = (phase, name) =>
            {
                if (!ReferenceEquals(token, _activeLoadToken)) return;
                WarmAsBefore.Helpers.Ui.Post(() => ApplyPhase(phase, name));
            };
            cts.Token.ThrowIfCancellationRequested();
            App.TraceStep("SaveVM.LoadSlot.BeforeLoad " + id);
            var ok = await _save.Load(id);          // ★ bool 不再丢弃
            App.TraceStep("SaveVM.LoadSlot.Loaded " + id + " ok=" + ok);
            var note = _save.LastLoadNote ?? "";

            var report = _save.LastRepairReport;

            if (!ok)
            {
                // 硬错误（不可修复档：空文件 / 整体语法损坏）：页面内常驻错误态，绝不跳 main。
                cts.Token.ThrowIfCancellationRequested();
                ShowHardError(string.IsNullOrWhiteSpace(note) ? "存档文件读取失败或格式不兼容。" : note, report);
                hardError = true;
                return;
            }

            // 高损确认：核心状态段（state）无法解析、已用默认值重建 —— 影响大，先停在存档页让用户确认。
            if (report is not null && report.CanEnter && report.NeedsConfirm)
            {
                try { ShowConfirm(report); }
                catch (Exception ex) { App.WriteLog("SaveVM.ShowConfirm -> " + ex.Message); }
                return;
            }

            // 可修复（含部分恢复 / 过期档自动修复）：把修复回执交给目标页常驻回执条——
            // 替代会自动消失的 Toast（用户诉求：修复后直接进入，并被告知修了什么、可能丢了什么）。
            if (report is not null && report.CanEnter && report.NeedsReceipt)
            {
                try { RepairReportService.Instance.Show(report); }
                catch (Exception ex) { App.WriteLog("SaveVM.RepairReport.Show -> " + ex.Message); }
            }

            // Load 成功但 LastLoadNote 非空 = 部分恢复（过期档自动修复）：中性提示、允许进入。
            if (!string.IsNullOrWhiteSpace(note))
            {
                StateTitle = "已部分恢复";
                StateBody = "存档能打开，部分旧数据已自动补齐；已为你直接进入，可在游戏中查看修复详情。";
                FailureDetail = note;
            }

            cts.Token.ThrowIfCancellationRequested();
            // 真实节点：Load 返回（含 EnterSaveLandscape / RepairAsync）→ 进入「05 进入场景」。
            MarkPhase(5, "进入场景");
            App.TraceStep("SaveVM.LoadSlot.BeforeGoToMain " + id);
            // 一次性导航闸：防 A/B 双链路（手动读取 + MC 桥自动打开）重复跳 main。
            if (Interlocked.CompareExchange(ref _navigated, 1, 0) != 0)
            {
                App.TraceStep("SaveVM.LoadSlot.NavSkipped " + id);
                return;
            }
            await Shell.Current.GoToAsync("main");
            App.TraceStep("SaveVM.LoadSlot.AfterGoToMain " + id);
            WarmAsBefore.Services.PerfLog.SetPhase("读档·完成");
            App.TraceStep("SaveLoad.Done " + id);
        }
        catch (OperationCanceledException)
        {
            App.TraceStep("SaveVM.LoadSlot.Cancelled " + id);
            WarmAsBefore.Services.PerfLog.Note("读档已取消", id);
            ResetState();
        }
        catch (Exception ex)
        {
            App.WriteLog("SaveVM.LoadSlot -> " + ex);
            ShowHardError(ex.Message);
            hardError = true;
            App.TraceStep("SaveVM.LoadSlot.Exception " + id);
        }
        finally
        {
            // 只清自己那一会话，避免迟到的旧 finally 抹掉新一次加载的 hook。
            if (ReferenceEquals(_activeLoadToken, token))
            {
                _save.LoadPhaseHook = null;
                _loadCts = null;
                _activeLoadToken = null;
            }
            // P1：任一终态（成功 / 硬错误 / 取消 / 异常）都删掉会话标记。
            App.ClearLoadMarker();
            Interlocked.Exchange(ref _loading, 0);
            // 状态调整统一走 Helpers.Ui.Post：读档 continuation 可能落回线程池，
            // 不能假定「这里一定在 UI 线程」。
            WarmAsBefore.Helpers.Ui.Post(() =>
            {
                if (hardError)
                {
                    // 硬错误：状态层常驻，按钮可用于重试 / 返回列表 / 看日志。
                    CanLoad = true;
                    CanCancel = false;
                    ShowSpinner = false;
                    ShowStaticGlyph = false;
                }
                else if (IsRestoring)
                {
                    // 成功进入或已取消：收起状态层。
                    IsRestoring = false;
                    CanLoad = true;
                    CanCancel = false;
                    ShowSpinner = false;
                    ShowStaticGlyph = false;
                }
            });
        }
    }

    [RelayCommand]
    private async Task ExportSlot(string id)
    {
        try
        {
            var bundle = _settings.Current.SaveBundleWithPlugins;
            var data = await _save.ExportStream(id, bundle);
            if (data is null) { Toast.Error("存档不存在"); return; }
            using var ms = new MemoryStream(data.Value.bytes);
            var result = await CommunityToolkit.Maui.Storage.FileSaver.Default.SaveAsync(data.Value.name, ms);
            if (result.IsSuccessful)
                Toast.Success(bundle ? "已导出存档包（含所用插件代码，不含插件设置里的密钥）。" : "已导出存档。");
        }
        catch (Exception ex)
        {
            App.WriteLog("SaveViewModel.ExportSlot -> " + ex);
        }
    }

    /// <summary>存档高级设置：插件/工具/角色白名单，不进入游戏也能改。</summary>
    [RelayCommand]
    private async Task OpenRules(string id)
        => await Shell.Current.GoToAsync($"saveRules?id={Uri.EscapeDataString(id)}");

    [RelayCommand]
    private async Task DeleteSlot(string id)
    {
        var slot = Slots.FirstOrDefault(s => s.Id == id);
        var ok = await Shell.Current.DisplayAlert("删除存档",
            $"确定删除「{slot?.Label ?? id}」吗？删除后不可恢复", "删除", "取消");
        if (!ok) return;
        await _save.Delete(id);
        await Refresh();
    }

    [RelayCommand]
    private async Task RenameSlot(string id)
    {
        var slot = Slots.FirstOrDefault(s => s.Id == id);
        var input = await Shell.Current.DisplayPromptAsync("重命名存档",
            "新的存档名称？", "保存", "取消", slot?.Label ?? "");
        if (string.IsNullOrWhiteSpace(input)) return;
        var ok = await _save.Rename(id, input.Trim());
        if (!ok) Toast.Error("重命名失败");
        await Refresh();
    }

    /// <summary>
    /// 创建分支：从选中的存档复制一份进度（含角色私有状态），原档不动。
    /// 分支会记下父档血缘与分叉时刻；旧机制下全局共享的角色状态在分支里各存一份，从此不再串档。
    /// </summary>
    [RelayCommand]
    private async Task BranchSlot(string id)
    {
        try
        {
            var slot = Slots.FirstOrDefault(s => s.Id == id);
            var input = await Shell.Current.DisplayPromptAsync("创建分支",
                "给分支起个名字？原存档不会被改动。", "创建", "取消",
                string.IsNullOrWhiteSpace(slot?.Label) ? "分支存档" : slot!.Label + " · 分支");
            if (input is null) return;   // 用户取消
            var label = string.IsNullOrWhiteSpace(input) ? "分支存档" : input.Trim();
            var (ok, message, _) = await _save.BranchAsync(id, label);
            if (ok) { Toast.Success(message); await Refresh(); }
            else Toast.Error(message);
        }
        catch (Exception ex)
        {
            App.WriteLog("SaveViewModel.BranchSlot -> " + ex);
            Toast.Error("创建分支失败");
        }
    }

    /// <summary>
    /// 新建存档：进角色选择页（select），选人确认后由那一页带着角色 id 跳进
    /// 「世界 → 人设 → 细则 → 开始」四步向导（newrun）。与标题页「创建新欢」同一条链，
    /// 不直接裸跳 newrun——缺角色参数时会回落到引擎里残存的旧角色，不是干净的新一局。
    /// </summary>
    [RelayCommand]
    private async Task NewSave()
    {
        try { await Shell.Current.GoToAsync("select"); }
        catch (Exception ex)
        {
            App.WriteLog("SaveViewModel.NewSave -> " + ex);
            Toast.Warn("没能打开新建存档向导，请稍后再试。");
        }
    }

    [RelayCommand]
    private async Task ImportSave()
    {
        try
        {
            var pick = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "选择存档文件（.json / .wbsave）",
                FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    { DevicePlatform.WinUI, new[] { ".json", ".wbsave", ".zip" } },
                    { DevicePlatform.Android, new[] { "application/json", "application/zip", "application/octet-stream", "text/plain" } },
                    { DevicePlatform.iOS, new[] { "public.json", "public.zip-archive", "public.data" } }
                })
            });
            if (pick is null) return;
            // 自动识别路由（存档页不再只认存档包）：插件包/角色包/素材包拿错入口也能正常导入
            var result = await _smart.ImportAsync(pick.FullPath);
            if (result.Ok) Toast.Success($"【{result.KindName}】{result.Message}");
            else Toast.Error(result.Message);
            await Refresh();
        }
        catch (Exception ex)
        {
            App.WriteLog("SaveViewModel.ImportSave -> " + ex);
        }
    }

    [RelayCommand]
    private async Task BackupAll()
    {
        try
        {
            var folder = await CommunityToolkit.Maui.Storage.FolderPicker.Default.PickAsync();
            if (folder?.Folder is null) return;
            var n = await _save.Backup(folder.Folder.Path);
            if (n > 0) Toast.Success($"已备份 {n} 个存档到 {folder.Folder.Path}");
            else Toast.Info("没有可备份的存档");
        }
        catch (Exception ex)
        {
            App.WriteLog("SaveViewModel.BackupAll -> " + ex);
        }
    }

    /// <summary>小说功能（测试）：仅当设置在实验性里开启后显示入口。</summary>
    [RelayCommand]
    private async Task ImportNovel()
    {
        try
        {
            var pick = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "选择小说文本",
                FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    { DevicePlatform.WinUI, new[] { ".txt", ".md" } },
                    { DevicePlatform.Android, new[] { "text/plain" } },
                    { DevicePlatform.iOS, new[] { "public.plain-text" } }
                })
            });
            if (pick is null) return;
            var result = await _novel.Analyze(pick.FullPath);
            if (result is null)
            {
                Toast.Error("解析失败：请检查 AI 配置或文本内容");
                return;
            }
            var chars = result.Characters.Count > 0 ? "角色：" + string.Join("、", result.Characters) : "角色：无";
            await Shell.Current.DisplayAlert($"小说导入（测试）· {result.Title}",
                $"{chars}\n\n摘要：{result.Summary}", "好");
        }
        catch (Exception ex)
        {
            App.WriteLog("SaveViewModel.ImportNovel -> " + ex);
        }
    }

    [RelayCommand]
    private async Task GoBack() => await Shell.Current.GoToAsync("..");

    // ==================== 读档状态层：命令与阶段轨（P0-3，2026-11-14）====================

    private static readonly string[] StageNames =
        { "读取文件", "校验数据", "恢复角色", "唤醒插件", "进入场景" };

    /// <summary>
    /// 阶段推进：由真实节点驱动（禁止按时间片假推进，docs/ARCHITECTURE.md:378）。
    /// PerfLog.SetPhase 用 Volatile.Write、App.TraceStep 同步落盘到 warm_startup.log，
    /// 都不依赖主线程 —— UI 被占死时，最后一条 SaveLoad.Phase.* 就是「卡在第几步」。
    /// </summary>
    private void MarkPhase(int phase, string name)
    {
        WarmAsBefore.Services.PerfLog.SetPhase($"读档·P{phase}·{name}");
        App.TraceStep($"SaveLoad.Phase.{phase} {name}");
        // 只把「改绑定属性」切回主线程；日志打点留在当前线程，卡死时也写得出去。
        WarmAsBefore.Helpers.Ui.Post(() => ApplyPhase(phase, name));
    }

    /// <summary>只改 5 行阶段轨的 Text / TextColor 令牌值，运行时绝不 Add/Remove 子控件。</summary>
    private void ApplyPhase(int phase, string name, bool failed = false)
    {
        phase = Math.Clamp(phase, 1, 5);
        var doneInk = TokenColor("SignalFg");
        var activeInk = TokenColor("Signal");      // 全站唯一饱和元素，只给当前阶段
        var idleInk = TokenColor("MutedFg");
        var failInk = TokenColor("DangerInk");
        // 性能模式 / 系统「减少动画」：不做逐帧动画，ShowSpinner=false 改静态 ✦。
        var motionDisabled = WarmAsBefore.DesignSystem.Theme.MotionTokens.Disabled;
        var staticGlyph = motionDisabled ? "✦" : "";
        for (var i = 1; i <= 5; i++)
        {
            string mark; Color ink; string glyph;
            if (failed && i == phase) { mark = "✕"; ink = failInk; glyph = "△"; }
            else if (i < phase) { mark = "✓"; ink = doneInk; glyph = ""; }
            else if (i == phase) { mark = "●"; ink = activeInk; glyph = staticGlyph; }
            else { mark = "○"; ink = idleInk; glyph = ""; }
            SetStage(i, mark, ink, glyph);
        }
        StatePhase = phase;
        StatePhaseText = $"第 {phase} 步 / 5 · {name}";
        CanCancel = !failed && phase <= 2 && IsRestoring;   // 阶段 3 起 false，但按钮不隐藏
        ShowSpinner = IsRestoring && !failed && !motionDisabled;
        ShowStaticGlyph = IsRestoring && !failed && motionDisabled;
    }

    private void SetStage(int n, string mark, Color ink, string glyph)
    {
        switch (n)
        {
            case 1: Stage1Mark = mark; Stage1Ink = ink; Stage1Glyph = glyph; break;
            case 2: Stage2Mark = mark; Stage2Ink = ink; Stage2Glyph = glyph; break;
            case 3: Stage3Mark = mark; Stage3Ink = ink; Stage3Glyph = glyph; break;
            case 4: Stage4Mark = mark; Stage4Ink = ink; Stage4Glyph = glyph; break;
            case 5: Stage5Mark = mark; Stage5Ink = ink; Stage5Glyph = glyph; break;
        }
    }

    /// <summary>从应用级资源字典取当前世界语义色（零新增 Token）；取不到时不绘制，绝不写死 HEX。</summary>
    private static Color TokenColor(string key)
    {
        try
        {
            if (Application.Current?.Resources.TryGetValue(key, out var v) == true && v is Color c) return c;
        }
        catch { }
        return Colors.Transparent;
    }

    /// <summary>硬错误：标题 ≤8 汉字、正文 ≤2 行，含「发生了什么 / 现在什么状态 / 能做什么」。</summary>
    private void ShowHardError(string detail, SaveRepairReport? report = null)
    {
        WarmAsBefore.Services.PerfLog.SetPhase("读档·失败");
        App.TraceStep("SaveLoad.Failed " + detail);
        WarmAsBefore.Helpers.Ui.Post(() =>
        {
            IsError = true;
            StateTitle = "读取失败";
            StateBody = "这份存档没能打开，仍停在存档列表，没有进入场景。";
            var extra = "";
            if (report is not null)
            {
                if (!string.IsNullOrEmpty(report.IncidentSummary))
                    extra += "\n" + report.IncidentSummary;
                if (report.LostItems.Count > 0)
                    extra += "\n可能丢失：" + string.Join("；", report.LostItems);
                if (!string.IsNullOrEmpty(report.BackupPath))
                    extra += "\n原件已备份：" + report.BackupPath;
            }
            FailureDetail = "可以重试、返回列表，或查看日志定位原因。\n" + detail + extra;
            var p = Math.Clamp(StatePhase, 1, 5);
            ApplyPhase(p, StageNames[p - 1], failed: true);
            CanLoad = true;   // 允许重试 / 改读别的存档
        });
    }

    /// <summary>
    /// 高损确认态：核心状态段（state）整段重建，影响大，停在存档页展示修复报告并等用户决定。
    /// 不自动导航；用户点「仍要进入」才走 <see cref="ConfirmEnterCommand"/>（复用一次性导航闸防重复跳 main）。
    /// </summary>
    private void ShowConfirm(SaveRepairReport report)
    {
        WarmAsBefore.Services.PerfLog.SetPhase("读档·需确认");
        App.TraceStep("SaveLoad.NeedsConfirm " + report.SaveId);
        _confirmReport = report;
        var sb = new System.Text.StringBuilder();
        if (!string.IsNullOrEmpty(report.IncidentSummary)) sb.AppendLine(report.IncidentSummary);
        if (report.LostItems.Count > 0)
        {
            sb.AppendLine("可能丢失：");
            foreach (var l in report.LostItems) sb.AppendLine("  · " + l);
        }
        if (report.FixedItems.Count > 0)
        {
            sb.AppendLine("已自动修复：");
            foreach (var f in report.FixedItems) sb.AppendLine("  · " + f);
        }
        var detail = sb.ToString().TrimEnd();
        WarmAsBefore.Helpers.Ui.Post(() =>
        {
            ConfirmDetail = detail;
            IsConfirming = true;
            IsRestoring = false;
            CanLoad = true;
            CanCancel = false;
            ShowSpinner = false;
            ShowStaticGlyph = false;
        });
    }

    /// <summary>高损确认后用户仍要进入：展示回执条并导航到 main（复用一次性导航闸）。</summary>
    [RelayCommand]
    private async Task ConfirmEnter()
    {
        var report = _confirmReport;
        if (report is null) return;
        _confirmReport = null;
        IsConfirming = false;
        if (Interlocked.CompareExchange(ref _navigated, 1, 0) != 0)
        {
            App.TraceStep("SaveVM.ConfirmEnter.NavSkipped");
            return;
        }
        try { RepairReportService.Instance.Show(report); }
        catch (Exception ex) { App.WriteLog("SaveVM.RepairReport.Show2 -> " + ex.Message); }
        await Shell.Current.GoToAsync("main");
        App.TraceStep("SaveVM.ConfirmEnter.AfterGoToMain");
    }

    /// <summary>
    /// P1：消费残留的 load_session.marker。进程被判无响应杀掉时日志队列可能来不及 flush，
    /// 这份同步写的小文件是「上次读取未完成」在下次启动仍成立的唯一路径。
    /// 一次性消费：读完即删，避免每次进页面重复补报；正在读取时不动（那是本次会话自己写的）。
    /// </summary>
    private void ConsumeIncompleteLoadMarker()
    {
        if (Volatile.Read(ref _loading) != 0) return;
        var raw = App.PeekLoadMarker();
        if (raw is null) { HasIncompleteLoad = false; IncompleteLoadNote = ""; return; }
        var saveId = "";
        foreach (var line in raw.Split('\n'))
            if (line.StartsWith("saveId=", StringComparison.Ordinal)) saveId = line[7..].Trim();
        IncompleteLoadNote = string.IsNullOrEmpty(saveId)
            ? "上次读取未完成，可能已记入启动日志；可查看日志了解当时停在哪一步。"
            : $"上次读取存档「{saveId}」未完成，可能已记入启动日志；可查看日志了解当时停在哪一步。";
        HasIncompleteLoad = true;
        App.WriteLog("SaveVM.IncompleteLoadMarker -> " + raw.Replace('\n', ' '));
        WarmAsBefore.Services.PerfLog.Note("上次读取未完成", saveId);
        App.ClearLoadMarker();
    }

    /// <summary>复位：仅由 UI 动作命令（取消/返回/重试）在 UI 线程调用，直接赋值保证顺序确定。</summary>
    private void ResetState()
    {
        IsRestoring = false;
        IsError = false;
        StateTitle = "";
        StateBody = "";
        FailureDetail = "";
        StatePhase = 0;
        StatePhaseText = "";
        CanCancel = false;
        ShowSpinner = false;
        ShowStaticGlyph = false;
        CanLoad = true;
        Interlocked.Exchange(ref _loading, 0);
        Interlocked.Exchange(ref _navigated, 0);
    }

    /// <summary>
    /// 取消：只在阶段 1/2 生效（CanCancel 变 false 后按钮不隐藏、只禁用）。
    /// cts 能停掉本链路后续检查点；SaveManager.Load 本身不支持取消，若它已在深水区，
    /// 取消保证「不导航 / 收起状态层 / 可重试」，如实说明而不谎称已中断读盘。
    /// </summary>
    [RelayCommand]
    private void CancelLoad()
    {
        if (!CanCancel) return;
        App.TraceStep("SaveVM.CancelLoad phase=" + StatePhase);
        WarmAsBefore.Services.PerfLog.Note("读档已取消", _pendingLoadId);
        try { _loadCts?.Cancel(); } catch { }
        ResetState();
    }

    [RelayCommand]
    private async Task RetryLoad()
    {
        var id = _pendingLoadId;
        if (string.IsNullOrEmpty(id)) return;
        App.TraceStep("SaveVM.RetryLoad " + id);
        ResetState();
        await LoadSlot(id);
    }

    /// <summary>返回列表：存档页本身就是列表，收起状态层即可，不做导航以免与在途 GoToAsync 打架。</summary>
    [RelayCommand]
    private void BackToList()
    {
        App.TraceStep("SaveVM.BackToList");
        try { _loadCts?.Cancel(); } catch { }
        ResetState();
    }

    /// <summary>
    /// 查看诊断：把最近启动日志读进页面内常驻的 FailureDetail（不弹模态、不用会自动消失的 Toast 承载正文），
    /// 同时给日志绝对路径并在 perf.log 留痕 —— 「打开/导出日志」的可达路径。
    /// </summary>
    [RelayCommand]
    private void ViewDiagnostics()
    {
        try
        {
            var path = Path.Combine(App.RootDirectory, "warm_startup.log");
            var raw = File.Exists(path) ? File.ReadAllText(path) : "";
            var lines = raw.Split('\n');
            var tail = lines.Length > 14 ? string.Join("\n", lines[^14..]) : raw;
            FailureDetail = (FailureDetail.Length > 0 ? FailureDetail + "\n\n" : "")
                + "最近启动日志（" + path + "）：\n" + tail.TrimEnd();
            WarmAsBefore.Services.PerfLog.Note("读档诊断", "查看日志 len=" + raw.Length);
            App.TraceStep("SaveVM.ViewDiagnostics len=" + raw.Length);
            HasIncompleteLoad = false;   // 补报看过即收起
            Toast.Info("诊断日志：" + path);
        }
        catch (Exception ex) { App.WriteLog("SaveVM.ViewDiagnostics -> " + ex); }
    }
}

/// <summary>存档列表展示项：补充角色名（存档里只存角色 id）。</summary>
public sealed class SaveSlotItem
{
    public SaveSlot Slot { get; init; } = new();
    public string CharacterName { get; init; } = "";
    public string Id => Slot.Id;
    public string Label => Slot.Label;
    public string Scene => Slot.Scene;
    public DateTime SavedAt => Slot.SavedAt;
}
