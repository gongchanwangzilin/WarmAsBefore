using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WarmAsBefore.Models;
using WarmAsBefore.Services;
using System.Collections.ObjectModel;
using WarmAsBefore.Controls;
using WarmAsBefore.Modules.Mcp;

namespace WarmAsBefore.ViewModels;

public sealed partial class SettingsViewModel
{
    // ============ 工具完整性检查（#2） ============

    /// <summary>最近一次完整性检查的结果文本（设置页「检查工具完整性」按钮写入）。</summary>
    [ObservableProperty] private string _toolIntegrityText = "";
    /// <summary>最近一次检查是否健康（决定结果文字颜色）。</summary>
    [ObservableProperty] private bool _toolIntegrityHealthy = true;

    /// <summary>
    /// 运行一次工具完整性检查：核对期望工具是否齐全、描述/schema 是否完整、聊天工具集能否构建。
    /// 结果写入 ToolIntegrityText + 日志，UI 一眼可读。
    /// </summary>
    [RelayCommand]
    private void VerifyTools()
    {
        try
        {
            var report = _tools.VerifyIntegrity();
            ToolIntegrityHealthy = report.Healthy;
            ToolIntegrityText = report.Summary();
            App.WriteLog("ToolIntegrity: " + report.Summary());
            foreach (var i in report.Issues.Where(i => i.Severity == "error"))
                App.WriteLog($"ToolIntegrity[error] {i.Tool}: {i.Detail}");
        }
        catch (Exception ex)
        {
            ToolIntegrityHealthy = false;
            ToolIntegrityText = "工具完整性检查失败：" + ex.Message;
            App.WriteLog("VerifyTools -> " + ex);
        }
    }

    // ============ #12 表情包库 ============

    /// <summary>表情包列表（设置页显示用）。</summary>
    public ObservableCollection<StickerRow> Stickers { get; } = new();

    /// <summary>表情包库状态文案（空库提示 / 数量）。</summary>
    [ObservableProperty] private string _stickerStatus = "";

    /// <summary>从库里刷新表情列表。</summary>
    [RelayCommand]
    private async Task RefreshStickers()
    {
        try
        {
            await _stickers.EnsureLoadedAsync();
            Stickers.Clear();
            foreach (var s in _stickers.Items)
                Stickers.Add(new StickerRow { Id = s.Id, Name = s.Name, Description = s.Description, Path = s.Path });
            StickerStatus = Stickers.Count == 0 ? "还没有表情包。点「导入表情」添加图片。" : $"共 {Stickers.Count} 个表情";
        }
        catch (Exception ex) { App.WriteLog("Settings.RefreshStickers -> " + ex.Message); }
    }

    /// <summary>导入一个或多个表情图片。</summary>
    [RelayCommand]
    private async Task ImportSticker()
    {
        try
        {
            var picks = await FilePicker.Default.PickMultipleAsync(new PickOptions
            {
                PickerTitle = "导入表情包图片",
                FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    { DevicePlatform.WinUI, new[] { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp" } },
                    { DevicePlatform.Android, new[] { "image/*" } },
                    { DevicePlatform.iOS, new[] { "public.image" } }
                })
            });
            if (picks is null) return;
            var added = 0;
            foreach (var p in picks)
            {
                var item = await _stickers.ImportAsync(p.FullPath);
                if (item is not null) added++;
            }
            await RefreshStickers();
            if (added > 0)
                Toast.Success($"导入了 {added} 个表情。给它们写句描述，AI 用起来会更准。");
        }
        catch (Exception ex) { App.WriteLog("Settings.ImportSticker -> " + ex.Message); }
    }

    /// <summary>编辑某个表情的描述（用户手填，AI 会按描述选表情）。</summary>
    [RelayCommand]
    private async Task EditStickerDesc(StickerRow? row)
    {
        if (row is null) return;
        var text = await Shell.Current.DisplayPromptAsync("表情描述",
            $"给「{row.Name}」写一句描述（AI 会按描述选表情）：", "保存", "取消", row.Description, maxLength: 120);
        if (text is null) return;
        await _stickers.UpdateDescriptionAsync(row.Id, text);
        await RefreshStickers();
    }

    /// <summary>#12/#13：让识图模型看一眼这个表情，自动写描述（一次性识别后注入给 AI）。</summary>
    [RelayCommand]
    private async Task AutoDescribeSticker(StickerRow? row)
    {
        if (row is null) return;
        if (!_vision.IsConfigured)
        {
            Toast.Warn("还没有配置识图模型。到「API 设置 → 识图模型」填好端点 / Key / 模型后再试。");
            return;
        }
        var desc = await _vision.DescribeImageAsync(row.Path,
            "这是一张聊天用的表情包。请用一句中文描述它的内容、情绪和适合的使用场景（30 字以内，直接给描述，不要客套）。");
        if (string.IsNullOrEmpty(desc))
        {
            Toast.Error("识图失败。检查识图模型配置或网络后再试。");
            return;
        }
        await _stickers.UpdateDescriptionAsync(row.Id, desc);
        await RefreshStickers();
    }

    /// <summary>删除一个表情。</summary>
    [RelayCommand]
    private async Task RemoveSticker(StickerRow? row)
    {
        if (row is null) return;
        var yes = await Shell.Current.DisplayAlert("删除表情", $"删除「{row.Name}」？", "删除", "取消");
        if (!yes) return;
        await _stickers.RemoveAsync(row.Id);
        await RefreshStickers();
    }


    // ============ #16 QQ/微信 机器人绑定角色（选存档） ============

    /// <summary>QQ 机器人绑定的陪聊角色 Id（空=用当前角色，即旧行为）。</summary>
    private string _qqBoundCharacterId = "";

    /// <summary>QQ 机器人绑定角色的显示名（Picker 的选中项）。</summary>
    [ObservableProperty] private string _qqBoundCharacter = "";

    /// <summary>微信机器人绑定的陪聊角色 Id（空=用当前角色）。两个通道各绑各的，同一个角色可以两边同时在线。</summary>
    private string _wechatBoundCharacterId = "";

    /// <summary>微信机器人绑定角色的显示名（Picker 的选中项）。</summary>
    [ObservableProperty] private string _wechatBoundCharacter = "";

    /// <summary>可选角色名列表（首项为「用当前角色」）。QQ 与微信用同一份名单。</summary>
    public ObservableCollection<string> QqBoundChoices { get; } = new();

    /// <summary>把角色库里的角色名填进绑定选择器，并把已绑定的 Id 翻译回显示名。</summary>
    private async Task LoadQqBoundChoicesAsync()
    {
        try
        {
            var list = await _library.ListAsync();
            QqBoundChoices.Clear();
            QqBoundChoices.Add("（用当前角色）");
            foreach (var c in list)
            {
                if (!string.IsNullOrEmpty(c.Profile.Name) && !QqBoundChoices.Contains(c.Profile.Name))
                    QqBoundChoices.Add(c.Profile.Name);
            }
            // 显示名回填走后备字段 + 手动通知：赋属性会触发 OnQqBoundCharacterChanged → PersistSettings，
            // 于是「打开设置页看一眼」就等于把整本设置重新落盘并重新下发一遍运行时配置。
            // 构造期读到的还是刚加载进来的那一份，写回去只是白做一遍，还会在进页面时抢一次 UI 线程。
            var bound = string.IsNullOrEmpty(_qqBoundCharacterId)
                ? null
                : list.FirstOrDefault(c => c.Profile.Id == _qqBoundCharacterId);
            _qqBoundCharacter = bound?.Profile.Name ?? "（用当前角色）";
            OnPropertyChanged(nameof(QqBoundCharacter));

            var wxBound = string.IsNullOrEmpty(_wechatBoundCharacterId)
                ? null
                : list.FirstOrDefault(c => c.Profile.Id == _wechatBoundCharacterId);
            _wechatBoundCharacter = wxBound?.Profile.Name ?? "（用当前角色）";
            OnPropertyChanged(nameof(WechatBoundCharacter));
        }
        catch (Exception ex) { App.WriteLog("Settings.LoadQqBoundChoices -> " + ex.Message); }
    }

    partial void OnQqBoundCharacterChanged(string value) => _ = ResolveQqBoundAsync(value);

    partial void OnWechatBoundCharacterChanged(string value) => _ = ResolveWechatBoundAsync(value);

    /// <summary>显示名 → 角色 Id（用于持久化）。</summary>
    private async Task ResolveQqBoundAsync(string name)
    {
        try
        {
            if (string.IsNullOrEmpty(name) || name == "（用当前角色）")
            {
                _qqBoundCharacterId = "";
                PersistSettings();
                return;
            }
            var list = await _library.ListAsync();
            var ch = list.FirstOrDefault(c => c.Profile.Name == name);
            _qqBoundCharacterId = ch?.Profile.Id ?? "";
            PersistSettings();
        }
        catch (Exception ex) { App.WriteLog("Settings.ResolveQqBound -> " + ex.Message); }
    }

    private async Task ResolveWechatBoundAsync(string name)
    {
        try
        {
            if (string.IsNullOrEmpty(name) || name == "（用当前角色）")
            {
                _wechatBoundCharacterId = "";
                PersistSettings();
                return;
            }
            var list = await _library.ListAsync();
            var ch = list.FirstOrDefault(c => c.Profile.Name == name);
            _wechatBoundCharacterId = ch?.Profile.Id ?? "";
            PersistSettings();
        }
        catch (Exception ex) { App.WriteLog("Settings.ResolveWechatBound -> " + ex.Message); }
    }


    // ============ 能力包（Skills） ============

    [ObservableProperty] private string _skillsSummary = "正在读取…";

    public async Task RefreshSkillsAsync()
    {
        try
        {
            var list = await _skills.ListAsync();
            if (list.Count == 0) { SkillsSummary = "还没有装能力包。"; return; }
            var enabled = list.Count(s => s.Enabled);
            SkillsSummary = $"已装 {list.Count} 个，启用 {enabled} 个："
                + string.Join("、", list.Select(s => s.Name + (s.Enabled ? "" : "（已停用）")));
        }
        catch (Exception ex)
        {
            App.WriteLog("SettingsVM.RefreshSkills -> " + ex.Message);
            SkillsSummary = "读取能力包失败。";
        }
    }

    [RelayCommand]
    private async Task ImportSkill()
    {
        try
        {
            var picked = await FilePicker.PickAsync(new PickOptions
            {
                FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    { DevicePlatform.WinUI, new[] { ".zip" } },
                    { DevicePlatform.Android, new[] { "application/zip", "application/octet-stream" } },
                    { DevicePlatform.iOS, new[] { "public.zip-archive" } }
                }),
                PickerTitle = "选择能力包（zip，内含 SKILL.md 与/或 tool.json）"
            });
            if (picked is null) return;
            var (ok, message) = await _skills.ImportAsync(picked.FullPath);
            if (ok) Toast.Success(message);
            else Toast.Error(message);
            if (ok)
            {
                _tools.RescanExternalTools();
                await _tools.RefreshSkillHintAsync();
                await RefreshSkillsAsync();
            }
        }
        catch (Exception ex)
        {
            App.WriteLog("SettingsVM.ImportSkill -> " + ex);
            Toast.Error("导入失败：" + ex.Message);
        }
    }

    /// <summary>管理已装能力包：启停 / 看说明书 / 删除。</summary>
    [RelayCommand]
    private async Task ManageSkills()
    {
        var list = await _skills.ListAsync();
        if (list.Count == 0)
        {
            Toast.Info("还没有装能力包。用「导入 zip / 目录」装上第一个。");
            return;
        }
        var pick = await Shell.Current.DisplayActionSheet("管理能力包", "取消", null,
            list.Select(s => $"{s.Name}｜{(s.Enabled ? "已启用" : "已停用")}").ToArray());
        if (pick is null || pick == "取消") return;
        var name = pick.Split('｜')[0].Trim();
        var skill = list.FirstOrDefault(s => s.Name == name);
        if (skill is null) return;

        var action = await Shell.Current.DisplayActionSheet(
            $"「{skill.Name}」{skill.Description}", "取消", null,
            skill.Enabled ? "停用" : "启用", "查看说明书", "删除");
        if (action is null || action == "取消") return;

        switch (action)
        {
            case "停用":
            case "启用":
                await _skills.SetEnabledAsync(skill.Name, action == "启用");
                break;
            case "查看说明书":
                {
                    var text = _skills.ReadInstructions(skill.Name);
                    await Shell.Current.DisplayAlert(skill.Name, string.IsNullOrWhiteSpace(text) ? "这个能力包没有说明书。" : text, "好");
                    return;
                }
            case "删除":
                {
                    var yes = await Shell.Current.DisplayAlert("删除能力包", $"删除「{skill.Name}」及其全部文件？", "删除", "取消");
                    if (!yes) return;
                    await _skills.RemoveAsync(skill.Name);
                    break;
                }
        }
        _tools.RescanExternalTools();
        await _tools.RefreshSkillHintAsync();
        await RefreshSkillsAsync();
    }


    [ObservableProperty] private bool _qqBotEnabled;
    [ObservableProperty] private string _qqAppId = "";

    // ===== QQ 桥增强：群聊 / 用户识别 / 主动外发 =====

    /// <summary>是否回复群聊消息（默认否）。</summary>
    [ObservableProperty] private bool _qqReplyGroup;
    /// <summary>用来认出「用户本人」的 openid（留空则记最近私聊过的人）。</summary>
    [ObservableProperty] private string _qqUserOpenId = "";
    /// <summary>角色对用户的称呼（识别到本人时使用）。</summary>
    [ObservableProperty] private string _qqUserName = "";
    /// <summary>允许角色主动发消息到用户手机。</summary>
    [ObservableProperty] private bool _qqProactiveEnabled;
    /// <summary>外发前是否需要人工确认。</summary>
    [ObservableProperty] private bool _qqProactiveRequireConfirm = true;
    /// <summary>外发每小时上限。</summary>
    [ObservableProperty] private int _qqProactivePerHour = 3;
    public List<int> QqProactiveRateChoices { get; } = new() { 1, 3, 5, 10 };

    /// <summary>主动外发走哪个通道：自动 / 只走 QQ / 只走微信。</summary>
    [ObservableProperty] private string _outboundChannelDisplay = "自动";
    public List<string> OutboundChannelChoices { get; } = new() { "自动", "只走 QQ", "只走微信" };
    partial void OnOutboundChannelDisplayChanged(string value) => PersistSettings();
    private string OutboundChannelValue => OutboundChannelDisplay switch
    {
        "只走 QQ" => "qq",
        "只走微信" => "wechat",
        _ => "auto"
    };

    partial void OnQqReplyGroupChanged(bool value) => PersistSettings();
    partial void OnQqUserOpenIdChanged(string value) => PersistSettings();
    partial void OnQqUserNameChanged(string value) => PersistSettings();
    partial void OnQqProactiveEnabledChanged(bool value) => PersistSettings();
    partial void OnQqProactiveRequireConfirmChanged(bool value) => PersistSettings();
    partial void OnQqProactivePerHourChanged(int value) => PersistSettings();
    [ObservableProperty] private string _qqAppSecret = "";
    [ObservableProperty] private bool _wechatEnabled;
    [ObservableProperty] private string _wechatAppId = "";
    [ObservableProperty] private string _wechatAppSecret = "";
    [ObservableProperty] private string _wechatToken = "";
    [ObservableProperty] private string _wechatPort = "8012";
    /// <summary>视频号外链（社交壳里点「视频号」直接跳转；留空则壳里提示去填）。</summary>
    [ObservableProperty] private string _channelsUrl = "";
    [ObservableProperty] private string _statusText = "";

    // ===== 云存档（GitHub 仓库托管）=====
    [ObservableProperty] private bool _cloudSaveEnabled;
    [ObservableProperty] private string _cloudRepo = "";
    [ObservableProperty] private string _cloudToken = "";
    [ObservableProperty] private string _cloudBranch = "";
    [ObservableProperty] private string _cloudDeviceName = "";
    [ObservableProperty] private bool _cloudAutoUpload = true;
    [ObservableProperty] private bool _cloudReconcileMemory = true;
    [ObservableProperty] private string _cloudStatus = "";
    [ObservableProperty] private bool _cloudBusy;

    partial void OnCloudSaveEnabledChanged(bool value)
    {
        PersistSettings();
        if (value) _ = RunCloudSyncAsync(justEnabled: true);
    }
    partial void OnCloudRepoChanged(string value) => PersistSettings();
    partial void OnCloudTokenChanged(string value) => PersistSettings();
    partial void OnCloudBranchChanged(string value) => PersistSettings();
    partial void OnCloudDeviceNameChanged(string value) => PersistSettings();
    partial void OnCloudAutoUploadChanged(bool value) => PersistSettings();
    partial void OnCloudReconcileMemoryChanged(bool value) => PersistSettings();

    [RelayCommand]
    private async Task TestCloudAsync()
    {
        if (CloudBusy) return;
        CloudBusy = true;
        CloudStatus = "正在连接…";
        try
        {
            await PersistNowAsync();
            var (ok, message) = await _cloud.TestAsync();
            CloudStatus = (ok ? "✓ " : "✕ ") + message;
        }
        catch (Exception ex) { CloudStatus = "测试失败：" + ex.Message; }
        finally { CloudBusy = false; }
    }

    [RelayCommand]
    private async Task SyncCloudNowAsync() => await RunCloudSyncAsync(justEnabled: false);

    /// <summary>先落盘设置再同步：否则服务读到的仍是改动前的仓库/令牌。</summary>
    private async Task RunCloudSyncAsync(bool justEnabled)
    {
        if (CloudBusy) return;
        CloudBusy = true;
        CloudStatus = justEnabled ? "已开启，正在做首次同步…" : "正在同步…";
        try
        {
            await PersistNowAsync();
            CloudStatus = await _cloud.SyncNowAsync();
        }
        catch (Exception ex)
        {
            CloudStatus = "同步失败：" + ex.Message;
            App.WriteLog("SettingsViewModel.CloudSync -> " + ex);
        }
        finally { CloudBusy = false; }
    }


    // MCP 配置
    [ObservableProperty] private bool _mcpEnabled;
    [ObservableProperty] private bool _mcpAutoApprove = true;
    /// <summary>MCP 服务与数据包列表。</summary>
    public ObservableCollection<Modules.Mcp.McpServerItem> McpServers { get; } = new();
    /// <summary>MCP 列表加载中标记（true 时设置页显示 loading 圈圈，防止 UI 动画停滞）。</summary>
    [ObservableProperty] private bool _mcpServersLoading = true;
    /// <summary>设置页级 loading 遮罩（首帧渲染前盖住内容；OnAppearing 后收起，让转场动画完整播放）。</summary>
    [ObservableProperty] private bool _pageLoading = true;
    /// <summary>MCP 体检进行中：握手要起进程、等回话，期间禁用其他「体检」按钮。</summary>
    [ObservableProperty] private bool _mcpDoctorBusy;
    /// <summary>
    /// 体检结论按服务 key 存着。列表每刷新一次就重建一批 McpServerItem 对象，
    /// 结论不落在 VM 上就会在下一次刷新时凭空消失。
    /// </summary>
    private readonly Dictionary<string, string> _mcpDoctorText = new(StringComparer.OrdinalIgnoreCase);

    // ---- 工具模式运行时（Java / Python）----
    [ObservableProperty] private string _pythonStatus = "未检测（点击下方「检测运行时」）";
    [ObservableProperty] private string _javaStatus = "未检测（点击下方「检测运行时」）";
    [ObservableProperty] private string _pythonManualPath = "";
    [ObservableProperty] private string _javaManualPath = "";
    [ObservableProperty] private bool _isRuntimeBusy;
    [ObservableProperty] private string _runtimeBusyText = "";
    public bool RuntimeOsSupported => _runtimes is { } r && r.IsDesktop;
    /// <summary>Android 沙箱路线是否可用（proot 安装向导入口显示条件）。</summary>
    public bool SandboxAvailable => _runtimes is { } r && r.UseSandbox;

    // ---- 开发者展示模式 ----
    [ObservableProperty] private bool _showcaseUnlocked;
    /// <summary>设置页是否已滚动到底部（隐藏入口只在最底部生效）。</summary>
    [ObservableProperty] private bool _atScrollBottom;
    /// <summary>连击点按计数：翻到设置最底部连续点按 114514 次后解锁。</summary>
    [ObservableProperty] private int _secretTapCount;
    private const int SecretTapsNeeded = 114514;
    private int _secretStamp;
    private readonly object SecretGate = new();


    /// <summary>后台线程扫描 MCP 数据包目录，再回 UI 线程回填集合（首次进入设置页不再阻塞）。</summary>
    private async Task LoadMcpServersAsync()
    {
        List<Modules.Mcp.McpServerItem> items;
        try
        {
            items = await Task.Run(() => _mcp.ListServers());
        }
        catch (Exception ex)
        {
            App.WriteLog("SettingsViewModel.LoadMcpServers -> " + ex);
            return;
        }

        MainThread.BeginInvokeOnMainThread(() =>
        {
            McpServers.Clear();
            foreach (var item in items) McpServers.Add(item);
            OnPropertyChanged(nameof(HasMcpServers));
            McpServersLoading = false;   // 加载完成，隐藏圈圈、显示列表
        });
    }

    /// <summary>列表是否为空（空态提示用；列表本身改成 BindableLayout，外层页面统一滚动）。</summary>
    public bool HasMcpServers => McpServers.Count > 0;

    /// <summary>手动重扫：接一个服务、删一个目录之后不必重启设置页。</summary>
    [RelayCommand]
    private Task RefreshMcp() => RefreshMcpServersAsync();

    /// <summary>
    /// 刷新 MCP 数据包列表（从 McpPacks 目录扫描）。
    /// 扫描递归读目录、逐文件取长度，必须离开 UI 线程——在列表里点一次「刷新」不该卡住整页。
    /// </summary>
    private async Task RefreshMcpServersAsync()
    {
        try
        {
            var items = await Task.Run(() => _mcp.ListServers());
            McpServers.Clear();
            foreach (var item in items)
            {
                if (_mcpDoctorText.TryGetValue(item.Key, out var saved))
                    item.DoctorText = saved;
                McpServers.Add(item);
            }
            OnPropertyChanged(nameof(HasMcpServers));
            McpServersLoading = false;
        }
        catch (Exception ex)
        {
            App.WriteLog("SettingsViewModel.RefreshMcpServers -> " + ex);
        }
    }

    // ---- 插件：与 MCP 同级的内联列表 ----
    // MCP 是「外部服务工具」，插件是「应用内全栈扩展」，两套系统各自安装、各自管理。
    // 两者并排展示，避免插件看起来比 MCP 低一级。
    public ObservableCollection<PluginRow> PluginRows { get; } = new();
    [ObservableProperty] private bool _pluginsLoading = true;
    public bool HasPlugins => PluginRows.Count > 0;

    /// <summary>后台线程扫插件目录，再回 UI 线程回填（与 MCP 列表同样的装载方式）。</summary>
    private async Task LoadPluginsAsync()
    {
        List<PluginRow> rows;
        try
        {
            var all = await Task.Run(async () => await _plugins.AllAsync());
            rows = all.Select(p => new PluginRow
            {
                Id = p.Manifest.Id,
                Name = string.IsNullOrWhiteSpace(p.Manifest.Name) ? p.Manifest.Id : p.Manifest.Name,
                Version = p.Manifest.Version,
                Author = p.Manifest.Author,
                Description = p.Manifest.Description,
                Enabled = p.Enabled,
                CanRun = p.Enabled,
                Issue = p.SettingsIssue ?? ""
            }).ToList();
        }
        catch (Exception ex)
        {
            App.WriteLog("SettingsViewModel.LoadPlugins -> " + ex);
            rows = new();
        }

        MainThread.BeginInvokeOnMainThread(() =>
        {
            PluginRows.Clear();
            foreach (var row in rows) PluginRows.Add(row);
            OnPropertyChanged(nameof(HasPlugins));
            PluginsLoading = false;
        });
    }

    [RelayCommand]
    private async Task TogglePlugin(PluginRow? row)
    {
        if (row is null) return;
        try
        {
            await _plugins.SetEnabledAsync(row.Id, !row.Enabled);
            await LoadPluginsAsync();
        }
        catch (Exception ex)
        {
            App.WriteLog("SettingsViewModel.TogglePlugin -> " + ex);
            Toast.Error("切换失败：" + ex.Message);
        }
    }

    [RelayCommand]
    private async Task RefreshPlugins()
    {
        PluginsLoading = true;
        await LoadPluginsAsync();
    }


    /// <summary>查看某条 MCP 服务器的简介与属性详情。</summary>
    [RelayCommand]
    private async Task ShowMcpServerDetails(Modules.Mcp.McpServerItem? item)
    {
        try
        {
            if (item is null) return;
            await Shell.Current.DisplayAlert($"MCP 服务器 · {item.Name}", item.Detail, "好");
        }
        catch (Exception ex)
        {
            App.WriteLog("SettingsViewModel.ShowMcpServerDetails -> " + ex);
        }
    }

    /// <summary>
    /// 接入一个外部 MCP 服务。MCP 的本体是「服务」，不是压缩包：要么本机起一个进程用 stdio 说话，
    /// 要么连一个 HTTP 地址。所以这里问的是接入方式与启动命令，而不是文件从哪儿来 ——
    /// 数据包（tools.json 那种）的导入已经搬去插件中心，那里才是「装东西」的地方。
    /// </summary>
    [RelayCommand]
    private async Task AddMcpService()
    {
        try
        {
            var name = await Shell.Current.DisplayPromptAsync("接入 MCP 服务",
                "给这个服务起个名字（也用作 McpPacks 下的目录名）", "下一步", "取消", "例如: 天气服务");
            if (string.IsNullOrWhiteSpace(name)) return;
            var pick = await Shell.Current.DisplayActionSheet("接入方式", "取消", null,
                "本机启动一个进程（stdio）", "连接一个网络地址（HTTP）");
            if (string.IsNullOrWhiteSpace(pick)) return;
            var stdio = pick.StartsWith("本机");
            var target = await Shell.Current.DisplayPromptAsync(stdio ? "启动命令" : "服务地址",
                stdio
                    ? "整行写：可执行文件在最前，参数用空格分开；路径里有空格给整段加引号。\n例如 npx -y @modelcontextprotocol/server-filesystem \"D:\\共享\""
                    : "以 http:// 或 https:// 开头。\n例如 http://127.0.0.1:8787/mcp",
                "下一步", "取消");
            if (string.IsNullOrWhiteSpace(target)) return;
            var desc = await Shell.Current.DisplayPromptAsync("接入 MCP 服务", "简介（可以留空，只在列表里显示）", "登记", "取消");

            var cfg = _mcp.AddService(name, stdio ? "stdio" : "http", target, desc ?? "");
            if (cfg is null)
            {
                Toast.Error("没能登记：接入方式只认 stdio 与 http，命令行或地址也不能是空的。具体原因看应用日志。");
                return;
            }
            await RefreshMcpServersAsync();
            var go = await Shell.Current.DisplayAlert("接入 MCP 服务",
                $"已登记「{cfg.Name}」（{cfg.Transport}）。\n现在握手一次吗？握手会跑 initialize 和 tools/list，之后 AI 才看得到它的工具。",
                "连接", "先不");
            if (go) await ConnectByKey(cfg.Key, cfg.Name, stdio);
        }
        catch (Exception ex)
        {
            App.WriteLog("SettingsViewModel.AddMcpService -> " + ex);
        }
    }

    /// <summary>握手一次。stdio 要先让用户点头才允许在这台机器上启动进程。</summary>
    private async Task ConnectByKey(string key, string name, bool stdio)
    {
        if (stdio)
        {
            var cfg = _mcp.LoadServerConfig(key);
            if (cfg is null)
            {
                Toast.Error("没读回这份接入配置，重新登记一次再试。");
                return;
            }
            if (!cfg.Allowed)
            {
                var line = (cfg.Command + " " + string.Join(" ", cfg.Args)).Trim();
                var grant = await Shell.Current.DisplayAlert("允许在本机启动",
                    $"「{name}」要用这条命令启动一个进程：\n{line}\n\n这个进程能读这台机器上的东西，也能联网。允许吗？",
                    "允许", "这次不");
                if (!grant)
                {
                    Toast.Info("没放行就不会启动它。列表里那颗「允许启动」随时可以点。");
                    return;
                }
                _mcp.AllowServer(key);
            }
        }

        var (ok, message, count) = await _mcp.ConnectServerAsync(key);
        await RefreshMcpServersAsync();
        if (ok) Toast.Success($"{message} 拿到 {count} 个工具，AI 用 mcp_list 就能看到它们。");
        else Toast.Error(message);
    }

    [RelayCommand]
    private async Task ConnectMcpServer(Modules.Mcp.McpServerItem? item)
    {
        if (item is null) return;
        await ConnectByKey(item.Key, item.Name, item.IsStdio);
    }

    [RelayCommand]
    private async Task DisconnectMcpServer(Modules.Mcp.McpServerItem? item)
    {
        if (item is null) return;
        await _mcp.DisconnectServerAsync(item.Key);
        await RefreshMcpServersAsync();
    }

    /// <summary>stdio 的同意闸：放行与收回都在这一颗按钮上，改完立刻落盘。</summary>
    [RelayCommand]
    private async Task ToggleMcpAllow(Modules.Mcp.McpServerItem? item)
    {
        if (item is null || !item.IsStdio) return;
        if (item.Allowed)
        {
            var back = await Shell.Current.DisplayAlert("收回启动许可",
                $"收回之后「{item.Name}」不再能启动进程，它的工具会在调用时被拒绝。收回吗？", "收回", "先留着");
            if (!back) return;
            _mcp.RevokeServer(item.Key);
        }
        else _mcp.AllowServer(item.Key);
        await RefreshMcpServersAsync();
    }

    /// <summary>
    /// 体检一个 MCP 服务：静态项（配置、运行时、工作目录、同意闸）先过一遍，再把上次失败的原文
    /// 翻成一句「怎么改」，最后真的握一次手，看它此刻答不答应。
    ///
    /// 结论就地写在列表里而不是弹窗：查错时人要一边看结论一边改上面那条命令，
    /// 弹窗把两边隔开，只能凭记忆抄。
    /// </summary>
    [RelayCommand]
    private async Task DiagnoseMcpServer(Modules.Mcp.McpServerItem? item)
    {
        if (item is null || McpDoctorBusy) return;
        McpDoctorBusy = true;
        try
        {
            var cfg = _mcp.LoadServerConfig(item.Key);
            if (cfg is null)
            {
                _mcpDoctorText[item.Key] = "这一条是 MCP 数据包（只有 tools.json），没有外部服务可体检。"
                    + "它的工具能不能跑，去插件中心点每条工具的「试跑」验；这张卡只管本机进程与网络地址。";
                await RefreshMcpServersAsync();
                return;
            }

            // 启动命令属于哪个运行时，就去探哪一个：扫不到 Node 直接说「没装、去哪装」，
            // 比等一次注定失败的启动再读它的错误原文快，也更准。
            var stdio = McpProtocol.UsesStdio(cfg);
            var spec = stdio ? RuntimeCatalog.ByCommand(cfg.Command) : null;
            var runtimes = spec is null ? null : await RuntimeProbe.ProbeAsync(new[] { spec.Id });

            // 没放行的 stdio 不替用户点头：那条闸就是给他拦的，体检只报「还没允许」。
            var findings = await McpDoctor.DiagnoseAsync(cfg, probe: !stdio || cfg.Allowed, runtimes);
            _mcpDoctorText[item.Key] = findings.Count == 0
                ? "没查出问题：配置完整，握手也通了。"
                : PluginDoctor.Render(findings);
            await RefreshMcpServersAsync();
        }
        catch (Exception ex)
        {
            App.WriteLog("SettingsViewModel.DiagnoseMcp -> " + ex);
            _mcpDoctorText[item.Key] = "体检出错：" + ex.Message;
            await RefreshMcpServersAsync();
        }
        finally
        {
            McpDoctorBusy = false;
        }
    }

    [RelayCommand]
    private async Task DeleteMcpServer(Modules.Mcp.McpServerItem? item)
    {
        if (item is null) return;
        var go = await Shell.Current.DisplayAlert("删除 MCP 服务",
            $"会连 McpPacks\\{item.Key} 这个目录一起删掉——数据包里手写的 tools.json 也一并没了。删除吗？", "删除", "取消");
        if (!go) return;
        await _mcp.DisconnectServerAsync(item.Key);
        var ok = _mcp.RemoveServer(item.Key);
        await RefreshMcpServersAsync();
        if (!ok) Toast.Error("没能删掉，目录可能正被占用。");
    }

    [RelayCommand]
    private async Task GoBack() => await Shell.Current.GoToAsync("..");

    // ============ 工具模式运行时 ============

    /// <summary>检测 Python / Java 运行时，并回填手动路径。</summary>
    [RelayCommand]
    private async Task DetectRuntimes()
    {
        if (!_runtimes.IsDesktop)
        {
            Toast.Warn("外部 Java/Python 工具仅桌面端可用（当前设备为移动端）。");
            return;
        }
        IsRuntimeBusy = true;
        RuntimeBusyText = "正在检测…";
        PythonStatus = "检测中…";
        JavaStatus = "检测中…";
        try
        {
            var st = await _runtimes.DetectAsync();
            PythonManualPath = _runtimes.ManualPythonPath;
            JavaManualPath = _runtimes.ManualJavaPath;
            PythonStatus = st.PythonOk ? "✓ " + st.PythonDetail : "✕ " + st.PythonDetail;
            JavaStatus = st.JavaOk ? "✓ " + st.JavaDetail : "✕ " + st.JavaDetail;
            if (string.IsNullOrEmpty(st.WindowsOnlyNote))
                RuntimeBusyText = "检测完成";
        }
        catch (Exception ex)
        {
            App.WriteLog("SettingsViewModel.DetectRuntimes -> " + ex);
            RuntimeBusyText = "检测出错：" + ex.Message;
        }
        finally
        {
            IsRuntimeBusy = false;
        }
    }

    /// <summary>
    /// MCP 服务与插件真正依赖的那批运行时（Node / npx / uvx / WebView2 / 系统浏览器…）。
    /// 上面那颗「检测」只管工具模式要用的 Python 与 Java，扫不到这些——所以插件装上了却一声不响时，
    /// 这里才能看见「这台机器没有 Node」。
    /// </summary>
    public ObservableCollection<Services.RuntimeState> ExternalRuntimes { get; } = new();
    [ObservableProperty] private bool _externalRuntimeBusy;
    [ObservableProperty] private string _externalRuntimeNote = "未检测：接 MCP 服务或装带网页的插件之前点一次。";
    public bool HasExternalRuntimes => ExternalRuntimes.Count > 0;

    [RelayCommand]
    private async Task DetectExternalRuntimes()
    {
        if (ExternalRuntimeBusy) return;
        ExternalRuntimeBusy = true;
        try
        {
            RuntimeProbe.Invalidate();
            // 扫 PATH 是同步磁盘活，别把它放在 UI 线程上：一次十几个命令 × 每个 PATH 目录，
            // 慢机器上够卡一下帧了。
            var states = await Task.Run(() => RuntimeProbe.ProbeAsync(RuntimeCatalog.All.Select(s => s.Id)));
            ExternalRuntimes.Clear();
            foreach (var s in states) ExternalRuntimes.Add(s);
            var missing = states.Count(s => !s.Present);
            ExternalRuntimeNote = missing == 0
                ? $"都齐了（{states.Count} 项）。接 MCP 与装插件时不用再装东西。"
                : $"缺 {missing} 项：下面每一条都写了去哪装；本应用能自带的（Python / Java）用上面那两颗下载按钮。";
            OnPropertyChanged(nameof(HasExternalRuntimes));
        }
        catch (Exception ex)
        {
            App.WriteLog("SettingsViewModel.DetectExternalRuntimes -> " + ex);
            ExternalRuntimeNote = "检测出错：" + ex.Message;
        }
        finally
        {
            ExternalRuntimeBusy = false;
        }
    }

    /// <summary>直接把官方下载页递到用户面前，比让他自己搜「nodejs 下载」少一步猜。</summary>
    [RelayCommand]
    private async Task OpenRuntimeDownload(Services.RuntimeState? state)
    {
        if (state is null || string.IsNullOrWhiteSpace(state.InstallUrl)) return;
        try { await Launcher.Default.OpenAsync(state.InstallUrl); }
        catch (Exception ex)
        {
            ExternalRuntimeNote = "打不开 " + state.InstallUrl + "：" + ex.Message;
        }
    }

    [RelayCommand]
    private async Task SetPythonPath()
    {        var input = await Shell.Current.DisplayPromptAsync("Python 路径",
            "输入 python.exe 的完整路径，或包含 python.exe 的目录（清空可解除）：", "确定", "取消", _runtimes.ManualPythonPath);
        if (input is null) return;
        _runtimes.SetManualPythonPath(input);
        PythonManualPath = _runtimes.ManualPythonPath;
        await DetectRuntimes();
    }

    [RelayCommand]
    private async Task SetJavaPath()
    {
        var input = await Shell.Current.DisplayPromptAsync("Java 路径",
            "输入 java.exe 的完整路径，或包含 bin/java.exe 的目录（清空可解除）：", "确定", "取消", _runtimes.ManualJavaPath);
        if (input is null) return;
        _runtimes.SetManualJavaPath(input);
        JavaManualPath = _runtimes.ManualJavaPath;
        await DetectRuntimes();
    }

    [RelayCommand]
    private async Task DownloadPython()
    {
        if (IsRuntimeBusy) return;
        IsRuntimeBusy = true;
        RuntimeBusyText = "准备下载 Python 便携版…";
        try
        {
            var progress = new Progress<string>(s =>
                MainThread.BeginInvokeOnMainThread(() => RuntimeBusyText = s));
            var result = await _runtimes.DownloadPythonAsync(progress);
            RuntimeBusyText = result;
            await DetectRuntimes();
        }
        catch (Exception ex)
        {
            App.WriteLog("SettingsViewModel.DownloadPython -> " + ex);
            RuntimeBusyText = "下载失败：" + ex.Message;
        }
        finally
        {
            IsRuntimeBusy = false;
        }
    }

    [RelayCommand]
    private async Task DownloadJava()
    {
        if (IsRuntimeBusy) return;
        IsRuntimeBusy = true;
        RuntimeBusyText = "准备下载 Java 便携版…";
        try
        {
            var progress = new Progress<string>(s =>
                MainThread.BeginInvokeOnMainThread(() => RuntimeBusyText = s));
            var result = await _runtimes.DownloadJavaAsync(progress);
            RuntimeBusyText = result;
            await DetectRuntimes();
        }
        catch (Exception ex)
        {
            App.WriteLog("SettingsViewModel.DownloadJava -> " + ex);
            RuntimeBusyText = "下载失败：" + ex.Message;
        }
        finally
        {
            IsRuntimeBusy = false;
        }
    }

    /// <summary>点击「检测运行时」按钮的包装（XAML 里也行，但保留同步调用的快捷入口）。</summary>
    [RelayCommand]
    private async Task RefreshRuntimes() => await DetectRuntimes();

    // ============ Android 沙箱安装向导 ============

    /// <summary>Android：一键安装 Linux 沙箱 rootfs + Python/Java 解释器（约 80MB）。</summary>
    [RelayCommand]
    private async Task InstallSandboxRuntime()
    {
        if (IsRuntimeBusy) return;
        var installer = _runtimes.SandboxInstaller;
        if (installer is null)
        {
            Toast.Warn("当前设备不支持 Linux 沙箱（仅 Android）。");
            return;
        }
        IsRuntimeBusy = true;
        RuntimeBusyText = "正在安装 Linux 沙箱运行时（Python + Java）…";
        try
        {
            var progress = new Progress<string>(s =>
                MainThread.BeginInvokeOnMainThread(() => RuntimeBusyText = s));
            var (ok, note) = await installer.InstallAsync(progress);
            RuntimeBusyText = ok ? "沙箱安装完成" : "部分失败：" + note;
            await DetectRuntimes();
            if (ok)
            {
                // 验证沙箱可用
                var sandbox = _runtimes.SandboxRuntime!;
                var diag = await sandbox.BuildDiagnosticAsync("python3 --version && java -version 2>&1 | head -1");
                if (diag is not null)
                {
                    var (code, out_) = await sandbox.RunGuestCommandAsync(diag.Value.Exe, diag.Value.Args, 45);
                    RuntimeBusyText = "沙箱验证：" + (code == 0 ? "✓" : "✕") + "\n" + out_;
                }
            }
        }
        catch (Exception ex)
        {
            App.WriteLog("SettingsViewModel.InstallSandboxRuntime -> " + ex);
            RuntimeBusyText = "安装失败：" + ex.Message;
        }
        finally
        {
            IsRuntimeBusy = false;
        }
    }

    /// <summary>Android：卸载沙箱（删 rootfs + native 文件，保留已下载的工具包）。</summary>
    [RelayCommand]
    private async Task RemoveSandboxRuntime()
    {
        var installer = _runtimes.SandboxInstaller;
        if (installer is null) return;
        var confirm = await Shell.Current.DisplayAlert(
            "卸载沙箱",
            "将删除已下载的 Python/Java 解释器（约 80MB），工具包本身保留。",
            "删除", "取消");
        if (!confirm) return;
        IsRuntimeBusy = true;
        RuntimeBusyText = "正在卸载沙箱…";
        await Task.Run(() => installer.Remove());
        RuntimeBusyText = "沙箱已删除";
        await DetectRuntimes();
        IsRuntimeBusy = false;
    }

    // ============ 开发者展示模式（114514 连击解锁） ============

    /// <summary>
    /// 翻到设置最底部后，对解锁按钮连续点按 114514 次即解锁（无需输入框，不弹密码框）。
    /// 达成开启成功提示；连击被打断（超过 4 秒未继续）则提示密码是错误并重置。
    /// </summary>
    [RelayCommand]
    private async Task SecretTap()
    {
        if (ShowcaseUnlocked) return;
        if (!AtScrollBottom) return;   // 隐藏入口：只有滚到最底部才计数
        SecretTapCount++;
        RestartSecretTimer();
        if (SecretTapCount >= SecretTapsNeeded)
        {
            lock (SecretGate) { _secretStamp++; }
            ShowcaseUnlocked = true;
            _settings.Apply(_settings.Current with { DeveloperShowcaseUnlocked = true });
            await _settings.Persist();
            SecretTapCount = 0;
            Toast.Success("开发者展示模式已开启：新增「开发者展示」入口（可编辑 / 播放展示案，含多立绘支持）。");
        }
    }

    private void RestartSecretTimer()
    {
        int stamp;
        lock (SecretGate) stamp = ++_secretStamp;
        _ = CountSecretTimeoutAsync(stamp);
    }

    /// <summary>连击计数超过 4 秒未续按：视为密码输入中断，提示错误并重置。</summary>
    private async Task CountSecretTimeoutAsync(int stamp)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(4));
            if (stamp != _secretStamp) return;
            if (ShowcaseUnlocked || SecretTapCount <= 0) return;
            SecretTapCount = 0;
            Toast.Error("连击中断，未达到 114514 次，计数已重置。");
        }
        catch (Exception ex)
        {
            App.WriteLog("SettingsViewModel.CountSecretTimeout -> " + ex.Message);
        }
    }

    [RelayCommand]
    private async Task OpenShowcase() => await Shell.Current.GoToAsync("showcase-list");

    /// <summary>打开素材库（背景 / 音乐管理）。</summary>
    [RelayCommand]
    private async Task OpenMaterials() => await Shell.Current.GoToAsync("materials");

    /// <summary>打开爱发电赞助页面。</summary>
    [RelayCommand]
    private async Task OpenSponsor()
    {
        try
        {
            await Launcher.Default.OpenAsync("https://ifdian.net/a/jqyhxkxt1145141026");
        }
        catch (Exception ex)
        {
            App.WriteLog("SettingsViewModel.OpenSponsor -> " + ex);
            Toast.Error("无法打开赞助页面：" + ex.Message);
        }
    }

    /// <summary>打开B站作者主页。</summary>
    [RelayCommand]
    private async Task OpenBilibili()
    {
        try
        {
            await Launcher.Default.OpenAsync("https://space.bilibili.com/3546745275419060?spm_id_from=333.1007.0.0");
        }
        catch (Exception ex)
        {
            App.WriteLog("SettingsViewModel.OpenBilibili -> " + ex);
            Toast.Error("无法打开B站主页：" + ex.Message);
        }
    }

}
