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

/// <summary>角色选择页展示项：角色 + 头像 + 该角色名下存档情况。</summary>
public sealed class CharacterCardItem
{
    public CharacterCardItem(CharacterData ch, ImageSource? avatar, string savesLabel)
    {
        Data = ch;
        AvatarSource = avatar;
        SavesLabel = savesLabel;
    }

    public CharacterData Data { get; init; }
    public string Id => Data.Profile.Id;
    public string Name => Data.Profile.Name;
    public string Personality => Data.Profile.Personality;
    public ImageSource? AvatarSource { get; init; }
    public bool HasAvatar => AvatarSource is not null;
    public string SavesLabel { get; init; }

    /// <summary>微信式列表项副标题：优先显示性格，空则给一句默认。</summary>
    public string Subtitle =>
        string.IsNullOrWhiteSpace(Personality) ? "点击开始聊天" : Personality;
}

/// <summary>角色工坊的 AI 产物：只用来填表单，落盘仍走 CharacterLibrary 的正常路径。</summary>
public sealed class CharacterWorkshopDraft
{
    public string Name { get; set; } = "";
    public string Gender { get; set; } = "";
    public string Personality { get; set; } = "";
    public string Description { get; set; } = "";
    public string Greeting { get; set; } = "";
    public string UserAddress { get; set; } = "";
    public List<CharacterWorkshopScene> Scenes { get; set; } = new();
}

public sealed class CharacterWorkshopScene
{
    public string Name { get; set; } = "";
    public string Prompt { get; set; } = "";
}

/// <summary>设置页「表情包库」列表的一行（#12）。</summary>
public sealed class StickerRow
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public string Path { get; init; } = "";
    /// <summary>列表里的描述行文案（没写描述时给提示）。</summary>
    public string DescLabel => string.IsNullOrWhiteSpace(Description) ? "（还没写描述）" : Description;
}

/// <summary>
/// 设置页「多端点轮换」列表的一行：一条 = 供应商 + 某款 AI（模型）+ 该组合的多枚密钥。
/// 列表里可直接改启用开关；任何改动都会通过 PropertyChanged 触发防抖落盘。
/// </summary>
/// <summary>
/// 设置页里的一家厂商：端点 + 密钥 + 它下面的多款模型。
/// UI 只编辑这一层与模型列表，轮换池由 RuntimeConfigurator 展开成「厂商 × 模型」。
/// </summary>
public sealed partial class AiProviderRow : ObservableObject
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N")[..8];

    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _url = "";
    [ObservableProperty] private string _keysText = "";
    [ObservableProperty] private bool _enabled = true;
    /// <summary>该厂商的超时（秒）：留空 = 用全局。</summary>
    [ObservableProperty] private string _timeoutSeconds = "";

    /// <summary>这家厂商下要用的模型（可多款）。</summary>
    public ObservableCollection<AiModelRow> Models { get; } = new();

    public string Title => string.IsNullOrWhiteSpace(Name) ? "（未命名厂商）" : Name;
    /// <summary>模型增删/启停后刷新副标题与模型摘要（模型列表在外部被改动时调用）。</summary>
    public void RefreshSummary()
    {
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(ModelsHint));
    }

    /// <summary>模型摘要：管理页上一眼看见这家挂了哪些模型、谁被停用。</summary>
    public string ModelsHint => Models.Count == 0
        ? "还没有模型：到「模型管理」里加一款。"
        : "模型：" + string.Join("、", Models.Select(m => m.DisplayName + (m.Enabled ? "" : "（停用）")));

    public string Summary
    {
        get
        {
            var keys = SplitKeys(KeysText).Count;
            var on = Models.Count(m => m.Enabled);
            var timeout = ParseTimeout() is { } t ? $" · 超时 {t}s" : "";
            return $"{on}/{Models.Count} 款模型 · {keys} 枚密钥{timeout}";
        }
    }

    public int? ParseTimeout()
    {
        if (string.IsNullOrWhiteSpace(TimeoutSeconds)) return null;
        return int.TryParse(TimeoutSeconds.Trim(), out var v) && v > 0 ? Math.Clamp(v, 3, 600) : null;
    }

    partial void OnNameChanged(string value) => OnPropertyChanged(nameof(Title));
    partial void OnKeysTextChanged(string value) => OnPropertyChanged(nameof(Summary));
    partial void OnTimeoutSecondsChanged(string value) => OnPropertyChanged(nameof(Summary));

    public static List<string> SplitKeys(string? text) =>
        (text ?? "").Split(new[] { '\n', '\r', ',', '，', ';', '；' },
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    public AiProvider ToProvider() => new()
    {
        Id = Id,
        Name = (Name ?? "").Trim(),
        Url = (Url ?? "").Trim(),
        Keys = SplitKeys(KeysText),
        Enabled = Enabled,
        TimeoutSeconds = ParseTimeout(),
        Models = Models.Where(m => !string.IsNullOrWhiteSpace(m.Model))
            .Select(m => m.ToModel())
            .ToList()
    };
}

/// <summary>厂商下面的一款模型（可在列表里单独启停，也能单独标注上下文窗口与能力）。</summary>
public sealed partial class AiModelRow : ObservableObject
{
    [ObservableProperty] private string _model = "";
    [ObservableProperty] private string _label = "";
    [ObservableProperty] private bool _enabled = true;

    /// <summary>上下文窗口（token）文本；空 = 不覆盖，用全局上限。</summary>
    [ObservableProperty] private string _contextWindow = "";
    [ObservableProperty] private bool _supportsVision;
    [ObservableProperty] private bool _supportsTools = true;
    [ObservableProperty] private bool _supportsJson;
    /// <summary>F25：这款模型在对话页显示哪些控件（模型内部设置）。</summary>
    [ObservableProperty] private bool _showHeatSlider = true;
    [ObservableProperty] private bool _showThinkTierBar = true;
    [ObservableProperty] private bool _showThinkToggle = true;
    [ObservableProperty] private bool _showQuickButtons = true;

    public string DisplayName => string.IsNullOrWhiteSpace(Label) ? (string.IsNullOrWhiteSpace(Model) ? "（未填模型）" : Model) : Label;
    partial void OnModelChanged(string value) { OnPropertyChanged(nameof(DisplayName)); OnPropertyChanged(nameof(CapabilityText)); }
    partial void OnLabelChanged(string value) { OnPropertyChanged(nameof(DisplayName)); OnPropertyChanged(nameof(CapabilityText)); }
    partial void OnContextWindowChanged(string value) => OnPropertyChanged(nameof(CapabilityText));
    partial void OnSupportsVisionChanged(bool value) => OnPropertyChanged(nameof(CapabilityText));
    partial void OnSupportsToolsChanged(bool value) => OnPropertyChanged(nameof(CapabilityText));
    partial void OnSupportsJsonChanged(bool value) => OnPropertyChanged(nameof(CapabilityText));
    partial void OnShowHeatSliderChanged(bool value) => OnPropertyChanged(nameof(CapabilityText));
    partial void OnShowThinkTierBarChanged(bool value) => OnPropertyChanged(nameof(CapabilityText));
    partial void OnShowThinkToggleChanged(bool value) => OnPropertyChanged(nameof(CapabilityText));
    partial void OnShowQuickButtonsChanged(bool value) => OnPropertyChanged(nameof(CapabilityText));

    /// <summary>小字：3.2K 上下文 · 识图 · 工具。</summary>
    public string CapabilityText
    {
        get
        {
            var parts = new List<string>();
            if (ParseWindow() is { } w) parts.Add($"{w / 1000.0:0.#}K 上下文");
            if (SupportsVision) parts.Add("识图");
            if (SupportsTools) parts.Add("工具");
            if (SupportsJson) parts.Add("JSON");
            if (ShowHeatSlider) parts.Add("热度条");
            if (ShowThinkTierBar) parts.Add("思考条");
            if (ShowThinkToggle) parts.Add("思考开关");
            if (ShowQuickButtons) parts.Add("快速钮");
            return parts.Count == 0 ? "未标注能力" : string.Join(" · ", parts);
        }
    }

    public int ParseWindow()
        => int.TryParse((ContextWindow ?? "").Trim(), out var v) && v > 0 ? Math.Clamp(v, 1000, 2_000_000) : 0;

    public AiProviderModel ToModel() => new()
    {
        Model = (Model ?? "").Trim(),
        Label = (Label ?? "").Trim(),
        Enabled = Enabled,
        ContextWindow = ParseWindow(),
        SupportsVision = SupportsVision,
        SupportsTools = SupportsTools,
        SupportsJson = SupportsJson,
        ShowHeatSlider = ShowHeatSlider,
        ShowThinkTierBar = ShowThinkTierBar,
        ShowThinkToggle = ShowThinkToggle,
        ShowQuickButtons = ShowQuickButtons
    };
}
