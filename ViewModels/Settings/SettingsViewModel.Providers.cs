using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WarmAsBefore.Models;
using WarmAsBefore.Services;
using System.Collections.ObjectModel;

namespace WarmAsBefore.ViewModels;

public sealed partial class SettingsViewModel
{
    // ============ 多端点轮换（厂商 × 模型） ============

    /// <summary>接入的厂商（一家 = 端点 + 密钥 + 多款模型）。</summary>
    public ObservableCollection<AiProviderRow> AiProviders { get; } = new();

    /// <summary>轮换总开关：开启后自动在多家厂商的多款模型间轮换，保证对话不间断。</summary>
    [ObservableProperty] private bool _aiRotationEnabled = true;

    /// <summary>多端点区状态文案。</summary>
    [ObservableProperty] private string _aiProviderStatus = "";

    /// <summary>上下文 Token 上限（估算值）：超过时强制把旧对话总结成第一人称记忆。</summary>
    [ObservableProperty] private int _contextTokenLimit = 32000;

    /// <summary>上限的 Picker 显示项（4K…256K）。</summary>
    [ObservableProperty] private string _contextTokenLimitDisplay = "32K";

    /// <summary>Token 上限可选项。</summary>
    public List<string> ContextLimitChoices { get; } = new() { "4K", "8K", "16K", "32K", "64K", "128K", "256K" };

    // 新增厂商输入框
    [ObservableProperty] private string _newProviderName = "";
    [ObservableProperty] private string _newProviderUrl = "";
    [ObservableProperty] private string _newProviderKeys = "";
    [ObservableProperty] private string _newProviderModel = "";
    [ObservableProperty] private string _newProviderTimeout = "";

    private void AttachAiProvider(AiProviderRow row)
    {
        row.PropertyChanged += (_, __) => PersistSettings();
        AiProviders.Add(row);
    }

    /// <summary>
    /// 从设置里重读厂商列表（管理页改完回来时调）：模型摘要、密钥数、超时都跟着刷新。
    /// 只重建这一块，不动其它设置项，避免把页面上还没保存的编辑冲掉。
    /// </summary>
    public void ReloadAiProviders()
    {
        AiProviders.Clear();
        foreach (var v in _settings.Current.AiProviders ?? new List<AiProvider>())
        {
            var row = new AiProviderRow
            {
                Id = string.IsNullOrWhiteSpace(v.Id) ? Guid.NewGuid().ToString("N")[..8] : v.Id,
                Name = v.Name ?? "",
                Url = v.Url ?? "",
                KeysText = string.Join("\n", v.Keys ?? new List<string>()),
                Enabled = v.Enabled,
                TimeoutSeconds = v.TimeoutSeconds is { } t and > 0 ? t.ToString() : ""
            };
            foreach (var m in v.Models ?? new List<AiProviderModel>())
                row.Models.Add(new AiModelRow
                {
                    Model = m.Model ?? "",
                    Label = m.Label ?? "",
                    Enabled = m.Enabled,
                    ContextWindow = m.ContextWindow > 0 ? m.ContextWindow.ToString() : "",
                    SupportsVision = m.SupportsVision,
                    SupportsTools = m.SupportsTools,
                    SupportsJson = m.SupportsJson
                });
            AttachAiProvider(row);
        }
        UpdateAiProviderStatus();
    }

    /// <summary>打开独立的厂商管理页（端点 / 密钥 / 超时 / 排序）。</summary>
    [RelayCommand]
    private async Task OpenAiProvidersAsync() => await Shell.Current.GoToAsync("aiProviders");

    /// <summary>打开独立的模型管理页（上下文窗口 / 识图 / 工具 / JSON）。</summary>
    [RelayCommand]
    private async Task OpenAiModelsAsync() => await Shell.Current.GoToAsync("aiModels");

    [RelayCommand]
    private async Task OpenModelDeploymentAsync() => await Shell.Current.GoToAsync("modelDeployment");

    // F08：立即跑一次「设置建议 AI」（忽略频率），结果写回 SettingsAdvisorResult 供 UI 展示
    [RelayCommand]
    private async Task RunSettingsAdvisorAsync()
    {
        var services = WarmAsBefore.Modules.Tools.ServicesHelper.GetServices();
        var svc = services?.GetService(typeof(WarmAsBefore.Services.Core.SettingsAdvisorService)) as WarmAsBefore.Services.Core.SettingsAdvisorService;
        if (svc is null) { SettingsAdvisorResult = "设置建议服务不可用。"; return; }
        SettingsAdvisorIsBusy = true;
        try
        {
            var r = await svc.RunNowAsync();
            SettingsAdvisorResult = r ?? "（无建议）";
        }
        catch (Exception ex) { SettingsAdvisorResult = "失败：" + ex.Message; }
        finally { SettingsAdvisorIsBusy = false; }
    }

    /// <summary>新增一家厂商（可同时填第一款模型；之后还能继续加）。</summary>
    [RelayCommand]
    private void AddProvider()
    {
        if (string.IsNullOrWhiteSpace(NewProviderUrl))
        {
            AiProviderStatus = "先填端点地址（OpenAI 兼容），再点「＋添加厂商」。";
            return;
        }
        var row = new AiProviderRow
        {
            Name = (NewProviderName ?? "").Trim(),
            Url = (NewProviderUrl ?? "").Trim(),
            KeysText = (NewProviderKeys ?? "").Trim(),
            TimeoutSeconds = (NewProviderTimeout ?? "").Trim()
        };
        if (!string.IsNullOrWhiteSpace(NewProviderModel))
            row.Models.Add(new AiModelRow { Model = NewProviderModel.Trim() });
        AttachAiProvider(row);

        NewProviderName = "";
        NewProviderUrl = "";
        NewProviderKeys = "";
        NewProviderModel = "";
        NewProviderTimeout = "";
        UpdateAiProviderStatus();
        PersistSettings();
    }

    /// <summary>用当前主配置预填厂商信息（同一家厂商只需填一次端点与密钥）。</summary>
    [RelayCommand]
    private void FillProviderFromCurrent()
    {
        if (string.IsNullOrWhiteSpace(NewProviderUrl) && !string.IsNullOrWhiteSpace(AiUrl)) NewProviderUrl = AiUrl.Trim();
        if (string.IsNullOrWhiteSpace(NewProviderKeys) && !string.IsNullOrWhiteSpace(AiKey)) NewProviderKeys = AiKey.Trim();
        if (string.IsNullOrWhiteSpace(NewProviderModel) && !string.IsNullOrWhiteSpace(AiModel)) NewProviderModel = AiModel.Trim();
        AiProviderStatus = "已用当前配置预填，点「＋添加厂商」。";
    }

    /// <summary>给某家厂商加一款模型（同一厂商多款模型不必重复填端点密钥）。</summary>
    [RelayCommand]
    private async Task AddModelToProvider(AiProviderRow? provider)
    {
        if (provider is null) return;
        var model = await Shell.Current.DisplayPromptAsync($"给「{provider.Title}」加模型",
            "模型名（如 deepseek-chat / gpt-4o-mini）。同一家厂商可以挂多款。", maxLength: 60);
        if (string.IsNullOrWhiteSpace(model)) return;
        var label = await Shell.Current.DisplayPromptAsync("显示名（可留空）",
            "想给这款模型起个好认的名字就填；留空则直接用模型名。", maxLength: 20);
        provider.Models.Add(new AiModelRow { Model = model.Trim(), Label = (label ?? "").Trim() });
        provider.RefreshSummary();
        PersistSettings();
    }

    /// <summary>改厂商名与端点（同一家厂商的地址只填一次）。</summary>
    [RelayCommand]
    private async Task EditProviderBasics(AiProviderRow? provider)
    {
        if (provider is null) return;
        var name = await Shell.Current.DisplayPromptAsync("厂商名称",
            "好认的名字，如「硅基流动」「DeepSeek 官方」。", initialValue: provider.Name, maxLength: 30);
        if (name is null) return;
        var url = await Shell.Current.DisplayPromptAsync("端点地址",
            "OpenAI 兼容的 chat/completions 地址。", initialValue: provider.Url, maxLength: 300);
        if (url is null) return;
        provider.Name = name.Trim();
        provider.Url = url.Trim();
        PersistSettings();
    }

    /// <summary>点一下模型：启用 / 停用（停用的模型不参与轮换）。</summary>
    [RelayCommand]
    private void ToggleModel(AiModelRow? model)
    {
        if (model is null) return;
        model.Enabled = !model.Enabled;
        var owner = AiProviders.FirstOrDefault(v => v.Models.Contains(model));
        owner?.RefreshSummary();
        PersistSettings();
    }

    /// <summary>识图现在实际会用谁：独立端点 / 标了能看图的厂商模型 / 大模型兜底。</summary>
    public string VisionHint => _vision.TargetDescription;

    /// <summary>
    /// 改一款模型自己的参数：上下文窗口 + 三项能力。
    /// 窗口决定这条端点的历史折叠阈值；能力决定要不要带 tools、识图时会不会选它。
    /// </summary>
    [RelayCommand]
    private async Task EditModelSettings(AiModelRow? model)
    {
        if (model is null) return;
        var win = await Shell.Current.DisplayPromptAsync(
            $"「{model.DisplayName}」的上下文窗口",
            "这款模型一次能收多少 token（如 128000 / 32000）。留空 = 用设置里的全局上下文上限。",
            initialValue: model.ContextWindow, maxLength: 8, keyboard: Keyboard.Numeric);
        if (win is null) return;
        model.ContextWindow = win.Trim();

        model.SupportsVision = await Shell.Current.DisplayAlert(
            "能力 · 识图",
            "这款模型能看图吗？\n没配独立识图端点时，会优先用标注了「能」的模型来识图。",
            "能看图", "不能");
        model.SupportsTools = await Shell.Current.DisplayAlert(
            "能力 · 工具调用",
            "支持 function calling 吗？\n选「不支持」后，走这条端点时请求里不会带 tools（小模型常见）。",
            "支持", "不支持");
        model.SupportsJson = await Shell.Current.DisplayAlert(
            "能力 · JSON",
            "能稳定输出 JSON 吗？用于结构化任务时的路由判断。",
            "能", "不能");

        PersistSettings();
    }

    /// <summary>改某家厂商的密钥（一行一枚）。</summary>
    [RelayCommand]
    private async Task EditProviderKeys(AiProviderRow? provider)
    {
        if (provider is null) return;
        var keys = await Shell.Current.DisplayPromptAsync($"「{provider.Title}」的密钥",
            "一行一枚，可多枚轮流用（也可用逗号分隔）。", initialValue: provider.KeysText, maxLength: 4000);
        if (keys is null) return;
        provider.KeysText = keys;
        PersistSettings();
    }

    /// <summary>改某家厂商的超时（秒，留空 = 跟随全局）。</summary>
    [RelayCommand]
    private async Task EditProviderTimeout(AiProviderRow? provider)
    {
        if (provider is null) return;
        var t = await Shell.Current.DisplayPromptAsync($"「{provider.Title}」的超时",
            "秒数（3~600），留空 = 用设置里的全局超时。", initialValue: provider.TimeoutSeconds, maxLength: 4);
        if (t is null) return;
        provider.TimeoutSeconds = t.Trim();
        PersistSettings();
    }

    [RelayCommand]
    private void RemoveModelFromProvider(AiModelRow? model)
    {
        if (model is null) return;
        var owner = AiProviders.FirstOrDefault(v => v.Models.Contains(model));
        if (owner is null) return;
        owner.Models.Remove(model);
        owner.RefreshSummary();
        PersistSettings();
    }

    /// <summary>删除一家厂商。</summary>
    [RelayCommand]
    private void RemoveProvider(AiProviderRow? row)
    {
        if (row is null) return;
        AiProviders.Remove(row);
        UpdateAiProviderStatus();
        PersistSettings();
    }

    /// <summary>厂商顺序即优先顺序：靠前的先用，失灵自动换下一家/下一款。</summary>
    [RelayCommand]
    private void MoveProviderUp(AiProviderRow? row)
    {
        if (row is null) return;
        var idx = AiProviders.IndexOf(row);
        if (idx <= 0) return;
        AiProviders.Move(idx, idx - 1);
        PersistSettings();
    }

    [RelayCommand]
    private void MoveProviderDown(AiProviderRow? row)
    {
        if (row is null) return;
        var idx = AiProviders.IndexOf(row);
        if (idx < 0 || idx >= AiProviders.Count - 1) return;
        AiProviders.Move(idx, idx + 1);
        PersistSettings();
    }

    /// <summary>刷新多端点区的状态文案。</summary>
    private void UpdateAiProviderStatus()
    {
        AiProviderStatus = AiProviders.Count == 0
            ? "还没有接入厂商。填端点与密钥后点「＋添加厂商」，再往里加模型。"
            : $"已接入 {AiProviders.Count} 家厂商，共 {AiProviders.Sum(v => v.Models.Count)} 款模型。"
              + "按列表顺序使用：靠前的先用，失灵自动换下一家/下一款（可用 ↑↓ 调整）。";
    }

    partial void OnAiRotationEnabledChanged(bool value) => PersistSettings();

    partial void OnContextTokenLimitDisplayChanged(string value)
    {
        _contextTokenLimit = ParseContextLimit(value);
        PersistSettings();
    }

    /// <summary>"32K"之类的显示名 → 数字。</summary>
    private static int ParseContextLimit(string? display)
    {
        var t = (display ?? "").Trim().TrimEnd('K', 'k');
        if (int.TryParse(t, out var v) && v >= 1) return v * 1000;
        return 32000;
    }

    /// <summary>数字 → 最接近的显示名。</summary>
    private static string ContextLimitDisplayOf(int limit)
    {
        var k = Math.Max(1, limit / 1000);
        int[] candidates = { 4, 8, 16, 32, 64, 128, 256 };
        var best = candidates[0];
        foreach (var c in candidates)
            if (Math.Abs(c - k) < Math.Abs(best - k)) best = c;
        return $"{best}K";
    }


    // ============ #13 API 预设 & 识图模型 & 量化模型 ============

    /// <summary>预设显示名（Picker 绑定）。</summary>
    [ObservableProperty] private string _aiPreset = "自定义";
    /// <summary>识图模型端点（独立于主对话模型）。</summary>
    [ObservableProperty] private string _visionUrl = "";
    [ObservableProperty] private string _visionKey = "";
    [ObservableProperty] private string _visionModel = "";

    /// <summary>
    /// 量化模型（embedding）：把说法换成数值向量的那个角色。
    /// 地址与密钥留空就复用对话那套（量化模型通常和对话模型在同一家厂商）。
    /// </summary>
    [ObservableProperty] private bool _embeddingEnabled = true;
    [ObservableProperty] private string _embeddingUrl = "";
    [ObservableProperty] private string _embeddingKey = "";
    [ObservableProperty] private string _embeddingModel = "";
    /// <summary>向量维度：0 = 由模型自己定。</summary>
    [ObservableProperty] private int _embeddingDimensions;

    /// <summary>可选维度（Picker 数据源；0 表示不传 dimensions）。</summary>
    public List<int> EmbeddingDimensionChoices { get; } = new() { 0, 256, 512, 768, 1024, 1536, 2048 };

    /// <summary>预设列表（Picker 数据源）。</summary>
    public ObservableCollection<string> AiPresetChoices { get; } = new();

    /// <summary>构造初始化期间不触发预设覆盖（防止恢复设置时把用户 URL 冲掉）。</summary>
    private bool _suppressPresetApply = true;

    /// <summary>常见 API 服务的端点预设：显示名 / 键 / URL。</summary>
    private static readonly (string Display, string Key, string Url)[] AiPresets =
    {
        ("通用（OpenAI兼容）", "openai", "https://api.openai.com/v1/chat/completions"),
        ("DeepSeek", "deepseek", "https://api.deepseek.com/v1/chat/completions"),
        ("MiMo（小米）", "mimo", "https://api.xiaomimimo.com/v1/chat/completions"),
        ("Kimi（月之暗面）", "kimi", "https://api.moonshot.cn/v1/chat/completions"),
        ("豆包（火山方舟）", "doubao", "https://ark.cn-beijing.volces.com/api/v3/chat/completions"),
        ("智谱 AI", "zhipu", "https://open.bigmodel.cn/api/paas/v4/chat/completions"),
        ("硅基流动", "siliconflow", "https://api.siliconflow.cn/v1/chat/completions"),
        ("通义千问（DashScope）", "qwen", "https://dashscope.aliyuncs.com/compatible-mode/v1/chat/completions"),
        ("自定义", "custom", ""),
    };

    private static string PresetDisplayOf(string key)
    {
        foreach (var p in AiPresets) if (p.Key == key) return p.Display;
        return "自定义";
    }

    private static string PresetKeyOf(string display)
    {
        foreach (var p in AiPresets) if (p.Display == display) return p.Key;
        return "custom";
    }

    private static string PresetUrlOf(string display)
    {
        foreach (var p in AiPresets) if (p.Display == display) return p.Url;
        return "";
    }

    /// <summary>切换预设：把端点填成该服务的默认值（模型名由用户填或点「拉取模型」）。</summary>
    partial void OnAiPresetChanged(string value)
    {
        if (_suppressPresetApply) return;
        var url = PresetUrlOf(value);
        if (!string.IsNullOrWhiteSpace(url)) AiUrl = url;
        PersistSettings();
    }

    partial void OnVisionUrlChanged(string value) => PersistSettings();
    partial void OnVisionKeyChanged(string value) => PersistSettings();
    partial void OnVisionModelChanged(string value) => PersistSettings();

    partial void OnEmbeddingEnabledChanged(bool value) => PersistSettings();
    partial void OnEmbeddingUrlChanged(string value) => PersistSettings();
    partial void OnEmbeddingKeyChanged(string value) => PersistSettings();
    partial void OnEmbeddingModelChanged(string value) => PersistSettings();
    partial void OnEmbeddingDimensionsChanged(int value) => PersistSettings();

}
