using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WarmAsBefore.Models;
using WarmAsBefore.Services;

namespace WarmAsBefore.ViewModels;

/// <summary>
/// 模型能力预设：给一款模型一键批量设置能力位。
/// 内置四档 + 用户「另存为预设」（存本地 Preferences，不碰 settings.json 的结构）。
/// </summary>
public sealed class CapabilityPreset
{
    public string Name { get; set; } = "";
    public string Hint { get; set; } = "";
    public bool Tools { get; set; }
    public bool Json { get; set; }
    public bool Vision { get; set; }
    public bool Ocr { get; set; }
    public bool Reasoning { get; set; }
    public bool ImageGen { get; set; }
    public bool AudioIn { get; set; }
    public bool AudioOut { get; set; }
    public bool VideoIn { get; set; }
    public bool VideoOut { get; set; }

    public void ApplyTo(AiModelEditRow row)
    {
        row.SupportsTools = Tools;
        row.SupportsJson = Json;
        row.SupportsVision = Vision;
        row.SupportsOcr = Ocr;
        row.SupportsReasoning = Reasoning;
        row.SupportsImageGen = ImageGen;
        row.SupportsAudioInput = AudioIn;
        row.SupportsAudioOutput = AudioOut;
        row.SupportsVideoInput = VideoIn;
        row.SupportsVideoOutput = VideoOut;
    }

    public static CapabilityPreset From(AiModelEditRow row, string name) => new()
    {
        Name = name,
        Hint = "自定义",
        Tools = row.SupportsTools,
        Json = row.SupportsJson,
        Vision = row.SupportsVision,
        Ocr = row.SupportsOcr,
        Reasoning = row.SupportsReasoning,
        ImageGen = row.SupportsImageGen,
        AudioIn = row.SupportsAudioInput,
        AudioOut = row.SupportsAudioOutput,
        VideoIn = row.SupportsVideoInput,
        VideoOut = row.SupportsVideoOutput
    };
}

/// <summary>模型管理页里的一款模型编辑行。字段齐到 State.AiProviderModel 的每一个能力位。</summary>
public sealed partial class AiModelEditRow : ObservableObject
{
    [ObservableProperty] private string _model = "";
    [ObservableProperty] private string _label = "";
    [ObservableProperty] private bool _enabled = true;
    /// <summary>上下文窗口文本：空 = 跟随全局上限。</summary>
    [ObservableProperty] private string _contextWindowText = "";

    // 文本与工具
    [ObservableProperty] private bool _supportsTools = true;
    [ObservableProperty] private bool _supportsJson;
    [ObservableProperty] private bool _supportsReasoning;
    // 图像
    [ObservableProperty] private bool _supportsVision;
    [ObservableProperty] private bool _supportsImageGen;
    [ObservableProperty] private bool _supportsOcr;
    // 音频
    [ObservableProperty] private bool _supportsAudioInput;
    [ObservableProperty] private bool _supportsAudioOutput;
    // 视频
    [ObservableProperty] private bool _supportsVideoInput;
    [ObservableProperty] private bool _supportsVideoOutput;

    /// <summary>使用顺序：数值小的优先被路由选中。Slider 绑 double，落盘时取整。</summary>
    [ObservableProperty] private double _priority;

    // 对话页显示开关（折叠区）
    [ObservableProperty] private bool _showHeatSlider = true;
    [ObservableProperty] private bool _showThinkTierBar = true;
    [ObservableProperty] private bool _showThinkToggle = true;
    [ObservableProperty] private bool _showQuickButtons = true;

    public string DisplayName => string.IsNullOrWhiteSpace(Label)
        ? (string.IsNullOrWhiteSpace(Model) ? "（未填模型）" : Model.Trim())
        : Label.Trim();

    public string PriorityText => ((int)Math.Round(Priority)).ToString();

    /// <summary>列表行上的一行能力摘要。</summary>
    public string CapabilityText
    {
        get
        {
            var parts = new List<string>();
            if (ParseWindow() is { } w) parts.Add($"{w / 1000.0:0.#}K 上下文");
            if (SupportsTools) parts.Add("工具");
            if (SupportsJson) parts.Add("JSON");
            if (SupportsReasoning) parts.Add("思考");
            if (SupportsVision) parts.Add("识图");
            if (SupportsImageGen) parts.Add("生图");
            if (SupportsOcr) parts.Add("OCR");
            if (SupportsAudioInput) parts.Add("听音频");
            if (SupportsAudioOutput) parts.Add("出音频");
            if (SupportsVideoInput) parts.Add("看视频");
            if (SupportsVideoOutput) parts.Add("出视频");
            if (!Enabled) parts.Add("已停用");
            return parts.Count == 0 ? "未标注能力" : string.Join(" · ", parts);
        }
    }

    // 字段变化让摘要/显示名跟着刷新（列表行与详情页共用同一份数据）。
    partial void OnModelChanged(string value) { OnPropertyChanged(nameof(DisplayName)); OnPropertyChanged(nameof(CapabilityText)); }
    partial void OnLabelChanged(string value) { OnPropertyChanged(nameof(DisplayName)); OnPropertyChanged(nameof(CapabilityText)); }
    partial void OnContextWindowTextChanged(string value) => OnPropertyChanged(nameof(CapabilityText));
    partial void OnEnabledChanged(bool value) => OnPropertyChanged(nameof(CapabilityText));
    partial void OnSupportsToolsChanged(bool value) => OnPropertyChanged(nameof(CapabilityText));
    partial void OnSupportsJsonChanged(bool value) => OnPropertyChanged(nameof(CapabilityText));
    partial void OnSupportsReasoningChanged(bool value) => OnPropertyChanged(nameof(CapabilityText));
    partial void OnSupportsVisionChanged(bool value) => OnPropertyChanged(nameof(CapabilityText));
    partial void OnSupportsImageGenChanged(bool value) => OnPropertyChanged(nameof(CapabilityText));
    partial void OnSupportsOcrChanged(bool value) => OnPropertyChanged(nameof(CapabilityText));
    partial void OnSupportsAudioInputChanged(bool value) => OnPropertyChanged(nameof(CapabilityText));
    partial void OnSupportsAudioOutputChanged(bool value) => OnPropertyChanged(nameof(CapabilityText));
    partial void OnSupportsVideoInputChanged(bool value) => OnPropertyChanged(nameof(CapabilityText));
    partial void OnSupportsVideoOutputChanged(bool value) => OnPropertyChanged(nameof(CapabilityText));
    partial void OnPriorityChanged(double value)
    {
        OnPropertyChanged(nameof(PriorityText));
        OnPropertyChanged(nameof(CapabilityText));
    }

    public int? ParseWindow()
        => int.TryParse((ContextWindowText ?? "").Trim(), out var v) && v > 0
            ? Math.Clamp(v, 1000, 2_000_000) : null;

    public static AiModelEditRow FromModel(AiProviderModel m) => new()
    {
        Model = m.Model ?? "",
        Label = m.Label ?? "",
        Enabled = m.Enabled,
        ContextWindowText = m.ContextWindow > 0 ? m.ContextWindow.ToString() : "",
        SupportsTools = m.SupportsTools,
        SupportsJson = m.SupportsJson,
        SupportsReasoning = m.SupportsReasoning,
        SupportsVision = m.SupportsVision,
        SupportsImageGen = m.SupportsImageGen,
        SupportsOcr = m.SupportsOcr,
        SupportsAudioInput = m.SupportsAudioInput,
        SupportsAudioOutput = m.SupportsAudioOutput,
        SupportsVideoInput = m.SupportsVideoInput,
        SupportsVideoOutput = m.SupportsVideoOutput,
        Priority = m.Priority,
        ShowHeatSlider = m.ShowHeatSlider,
        ShowThinkTierBar = m.ShowThinkTierBar,
        ShowThinkToggle = m.ShowThinkToggle,
        ShowQuickButtons = m.ShowQuickButtons
    };

    public AiProviderModel ToModel() => new()
    {
        Model = (Model ?? "").Trim(),
        Label = (Label ?? "").Trim(),
        Enabled = Enabled,
        ContextWindow = ParseWindow() ?? 0,
        SupportsTools = SupportsTools,
        SupportsJson = SupportsJson,
        SupportsReasoning = SupportsReasoning,
        SupportsVision = SupportsVision,
        SupportsImageGen = SupportsImageGen,
        SupportsOcr = SupportsOcr,
        SupportsAudioInput = SupportsAudioInput,
        SupportsAudioOutput = SupportsAudioOutput,
        SupportsVideoInput = SupportsVideoInput,
        SupportsVideoOutput = SupportsVideoOutput,
        Priority = (int)Math.Round(Priority),
        ShowHeatSlider = ShowHeatSlider,
        ShowThinkTierBar = ShowThinkTierBar,
        ShowThinkToggle = ShowThinkToggle,
        ShowQuickButtons = ShowQuickButtons
    };
}

/// <summary>模型管理页里的一家厂商编辑行（只用到厂商级字段，端点/密钥仍由「厂商管理」页维护）。</summary>
public sealed partial class AiProviderEditRow : ObservableObject
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N")[..8];

    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _url = "";
    [ObservableProperty] private bool _enabled = true;

    // 这两项在本页不编辑，但必须原样带回 settings.json——否则在模型页动一下开关就会清空密钥/超时。
    public List<string> Keys { get; set; } = new();
    public int? TimeoutSeconds { get; set; }

    public ObservableCollection<AiModelEditRow> Models { get; } = new();

    public string Title => string.IsNullOrWhiteSpace(Name) ? "（未命名厂商）" : Name.Trim();
    public string ModelCountText => $"{Models.Count(m => m.Enabled)}/{Models.Count} 款启用";

    public void RefreshSummary() => OnPropertyChanged(nameof(ModelCountText));

    partial void OnNameChanged(string value) => OnPropertyChanged(nameof(Title));

    public static AiProviderEditRow FromProvider(AiProvider p)
    {
        var row = new AiProviderEditRow
        {
            Id = string.IsNullOrWhiteSpace(p.Id) ? Guid.NewGuid().ToString("N")[..8] : p.Id,
            Name = p.Name ?? "",
            Url = p.Url ?? "",
            Enabled = p.Enabled,
            Keys = new List<string>(p.Keys ?? new List<string>()),
            TimeoutSeconds = p.TimeoutSeconds
        };
        foreach (var m in p.Models ?? new List<AiProviderModel>())
            row.Models.Add(AiModelEditRow.FromModel(m));
        return row;
    }

    public AiProvider ToProvider() => new()
    {
        Id = Id,
        Name = (Name ?? "").Trim(),
        Url = (Url ?? "").Trim(),
        Keys = new List<string>(Keys ?? new List<string>()),
        Enabled = Enabled,
        TimeoutSeconds = TimeoutSeconds,
        Models = Models.Where(m => !string.IsNullOrWhiteSpace(m.Model))
            .Select(m => m.ToModel()).ToList()
    };
}

/// <summary>
/// AI 模型管理页的独立 VM：从 SettingsManager.Current.AiProviders 加载，
/// 所有增删改/启停/排序/预设都在这里落到内存与 settings.json。
/// </summary>
public sealed partial class AiModelsViewModel : ObservableObject
{
    private const string PresetKey = "ai_models_custom_presets_v1";

    private readonly SettingsManager _settings;
    private bool _loading;
    private CancellationTokenSource? _debounce;

    public ObservableCollection<AiProviderEditRow> Providers { get; } = new();

    /// <summary>内置四档能力预设。</summary>
    public IReadOnlyList<CapabilityPreset> BuiltinPresets { get; }

    /// <summary>用户「另存为预设」保存的自定义档。</summary>
    public ObservableCollection<CapabilityPreset> CustomPresets { get; } = new();

    [ObservableProperty] private bool _isDetailVisible;
    [ObservableProperty] private bool _isListVisible = true;
    [ObservableProperty] private AiModelEditRow? _selectedModel;
    [ObservableProperty] private string _detailTitle = "模型详情";
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private bool _hasStatus;
    [ObservableProperty] private bool _showDisplaySwitches;

    public AiModelsViewModel(SettingsManager settings)
    {
        _settings = settings;
        BuiltinPresets = new[]
        {
            new CapabilityPreset { Name = "通用对话", Hint = "工具 + JSON", Tools = true, Json = true },
            new CapabilityPreset { Name = "视觉模型", Hint = "识图 + OCR", Vision = true, Ocr = true },
            new CapabilityPreset
            {
                Name = "全模态", Hint = "十项能力全开",
                Tools = true, Json = true, Reasoning = true, Vision = true, ImageGen = true,
                Ocr = true, AudioIn = true, AudioOut = true, VideoIn = true, VideoOut = true
            },
            new CapabilityPreset { Name = "纯文本小模型", Hint = "能力全关，做便宜兜底" }
        };
        LoadCustomPresets();
        Load();
    }

    private void Load()
    {
        _loading = true;
        try
        {
            Providers.Clear();
            foreach (var p in _settings.Current.AiProviders ?? new List<AiProvider>())
            {
                var row = AiProviderEditRow.FromProvider(p);
                foreach (var m in row.Models) Wire(m);
                Wire(row);
                Providers.Add(row);
            }
        }
        finally { _loading = false; }
    }

    // ===== 改动防抖落盘：行里任何字段变化都自动写回，UI 不必逐个挂 Toggled 事件 =====
    private void Wire(INotifyPropertyChanged row) => row.PropertyChanged += OnRowChanged;

    private void OnRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_loading) return;
        if (e.PropertyName is nameof(AiModelEditRow.DisplayName)
            or nameof(AiModelEditRow.CapabilityText)
            or nameof(AiModelEditRow.PriorityText)
            or nameof(AiProviderEditRow.Title)
            or nameof(AiProviderEditRow.ModelCountText)) return;
        QueueCommit();
    }

    private void QueueCommit()
    {
        if (_loading) return;
        _debounce?.Cancel();
        var cts = new CancellationTokenSource();
        _debounce = cts;
        _ = CommitDebouncedAsync(cts.Token);
    }

    private async Task CommitDebouncedAsync(CancellationToken token)
    {
        try { await Task.Delay(350, token); }
        catch (OperationCanceledException) { return; }
        await CommitAsync();
    }

    private async Task CommitAsync(string? note = null)
    {
        if (_loading) return;
        var list = Providers.Select(p => p.ToProvider()).ToList();
        _settings.Apply(_settings.Current with { AiProviders = list });
        await _settings.Persist();
        if (!string.IsNullOrEmpty(note)) SetStatus(note);
    }

    private void SetStatus(string text)
    {
        StatusText = text;
        HasStatus = !string.IsNullOrWhiteSpace(text);
    }

    private static async Task<bool> ConfirmDeleteAsync(string title, string message)
    {
        var page = Shell.Current?.CurrentPage ?? Application.Current?.MainPage;
        return page is not null && await page.DisplayAlert(title, message, "删除", "取消");
    }

    private void LoadCustomPresets()
    {
        try
        {
            var json = Preferences.Get(PresetKey, "");
            if (string.IsNullOrWhiteSpace(json)) return;
            var list = JsonSerializer.Deserialize<List<CapabilityPreset>>(json);
            if (list is null) return;
            foreach (var p in list.Where(p => !string.IsNullOrWhiteSpace(p.Name))) CustomPresets.Add(p);
        }
        catch (Exception ex)
        {
            App.WriteLog("AiModelsViewModel.LoadCustomPresets -> " + ex.Message);
        }
    }

    private void SaveCustomPresets()
    {
        try { Preferences.Set(PresetKey, JsonSerializer.Serialize(CustomPresets)); }
        catch (Exception ex) { App.WriteLog("AiModelsViewModel.SaveCustomPresets -> " + ex.Message); }
    }

    // ===== 导航 =====

    [RelayCommand]
    private async Task BackAsync() => await Shell.Current.GoToAsync("..");

    [RelayCommand]
    private async Task OpenProvidersAsync() => await Shell.Current.GoToAsync("aiProviders");

    [RelayCommand]
    private async Task OpenDeploymentAsync() => await Shell.Current.GoToAsync("modelDeployment");

    // ===== 详情页切换 =====

    [RelayCommand]
    private void EditModel(AiModelEditRow? row)
    {
        if (row is null) return;
        SelectedModel = row;
        DetailTitle = row.DisplayName;
        IsDetailVisible = true;
        IsListVisible = false;
    }

    [RelayCommand]
    private void BackToList()
    {
        IsDetailVisible = false;
        IsListVisible = true;
        SelectedModel = null;
    }

    // ===== 厂商增删改 / 启停 / 排序 =====

    [RelayCommand]
    private async Task AddProviderAsync()
    {
        var row = new AiProviderEditRow { Name = "新厂商", Url = "" };
        Wire(row);
        Providers.Add(row);
        await CommitAsync("已加厂商，去「厂商管理」补端点与密钥。");
    }

    [RelayCommand]
    private async Task DeleteProviderAsync(AiProviderEditRow? row)
    {
        if (row is null) return;
        if (!await ConfirmDeleteAsync("删除厂商", $"删掉「{row.Title}」及其下所有模型？"))
            return;
        Providers.Remove(row);
        if (SelectedModel is not null && row.Models.Contains(SelectedModel)) BackToList();
        await CommitAsync("已删除厂商。");
    }

    [RelayCommand]
    private async Task MoveProviderUpAsync(AiProviderEditRow? row) => await MoveProviderAsync(row, -1);

    [RelayCommand]
    private async Task MoveProviderDownAsync(AiProviderEditRow? row) => await MoveProviderAsync(row, 1);

    private async Task MoveProviderAsync(AiProviderEditRow? row, int delta)
    {
        if (row is null) return;
        var i = Providers.IndexOf(row);
        var j = i + delta;
        if (i < 0 || j < 0 || j >= Providers.Count) return;
        Providers.Move(i, j);
        await CommitAsync("顺序已保存：排在前面的厂商优先被轮换。");
    }

    // ===== 模型增删改 / 排序 =====

    [RelayCommand]
    private async Task AddModelAsync(AiProviderEditRow? provider)
    {
        if (provider is null) return;
        var row = new AiModelEditRow { Model = "new-model" };
        Wire(row);
        provider.Models.Add(row);
        provider.RefreshSummary();
        await CommitAsync("已加模型，进入详情填写模型 id 与能力。");
        EditModel(row);
    }

    [RelayCommand]
    private async Task DeleteModelAsync(AiModelEditRow? row)
    {
        if (row is null) return;
        var owner = Providers.FirstOrDefault(p => p.Models.Contains(row));
        if (owner is null) return;
        if (!await ConfirmDeleteAsync("删除模型", $"删掉「{row.DisplayName}」？"))
            return;
        owner.Models.Remove(row);
        owner.RefreshSummary();
        if (ReferenceEquals(SelectedModel, row)) BackToList();
        await CommitAsync("已删除模型。");
    }

    [RelayCommand]
    private async Task MoveModelUpAsync(AiModelEditRow? row) => await MoveModelAsync(row, -1);

    [RelayCommand]
    private async Task MoveModelDownAsync(AiModelEditRow? row) => await MoveModelAsync(row, 1);

    private async Task MoveModelAsync(AiModelEditRow? row, int delta)
    {
        if (row is null) return;
        var owner = Providers.FirstOrDefault(p => p.Models.Contains(row));
        if (owner is null) return;
        var i = owner.Models.IndexOf(row);
        var j = i + delta;
        if (j < 0 || j >= owner.Models.Count) return;
        owner.Models.Move(i, j);
        await CommitAsync("顺序已保存：同能力下排在前面的模型优先被选中。");
    }

    // ===== 预设 =====

    [RelayCommand]
    private void ApplyPreset(CapabilityPreset? preset)
    {
        if (preset is null || SelectedModel is null) return;
        preset.ApplyTo(SelectedModel);
        SetStatus($"已套用「{preset.Name}」：{preset.Hint}");
    }

    [RelayCommand]
    private void SaveAsPreset()
    {
        if (SelectedModel is null) { SetStatus("先选一款模型再另存为预设。"); return; }
        var name = $"{SelectedModel.DisplayName} · 预设";
        var preset = CapabilityPreset.From(SelectedModel, name);
        CustomPresets.Add(preset);
        SaveCustomPresets();
        SetStatus($"已保存预设「{name}」。");
    }

    [RelayCommand]
    private void DeletePreset(CapabilityPreset? preset)
    {
        if (preset is null) return;
        CustomPresets.Remove(preset);
        SaveCustomPresets();
        SetStatus($"已删除预设「{preset.Name}」。");
    }
}
