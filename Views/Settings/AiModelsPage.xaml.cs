using System.Collections.ObjectModel;
using WarmAsBefore.Models;
using WarmAsBefore.Services;
using WarmAsBefore.ViewModels;

namespace WarmAsBefore.Views;

/// <summary>
/// AI 模型管理页：按厂商列出所有模型，每个模型单独配上下文窗口与三项能力。
/// 这些值会随轮换池下发到运行时：窗口 → 历史折叠阈值；工具 → 请求里带不带 tools；
/// 识图 → 没配独立识图端点时优先用谁看图。
/// </summary>
public partial class AiModelsPage : ContentPage
{
    private readonly SettingsManager _settings;
    private bool _loading;

    public ObservableCollection<AiProviderRow> Providers { get; } = new();

    public AiModelsPage(SettingsManager settings)
    {
        InitializeComponent();
        _settings = settings;
        BindingContext = this;
        Load();
    }

    private void Load()
    {
        _loading = true;
        try
        {
            Providers.Clear();
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
                row.RefreshSummary();
                Providers.Add(row);
            }
        }
        finally { _loading = false; }
    }

    private async Task SaveAsync(string? note = null)
    {
        if (_loading) return;
        var list = Providers.Select(p => p.ToProvider()).ToList();
        _settings.Apply(_settings.Current with { AiProviders = list });
        await _settings.Persist();
        if (!string.IsNullOrEmpty(note))
        {
            StatusLabel.Text = note;
            StatusLabel.IsVisible = true;
        }
    }

    private static AiProviderRow? ProviderOf(object? sender)
        => (sender as BindableObject)?.BindingContext as AiProviderRow;

    private static AiModelRow? ModelOf(object? sender)
        => (sender as BindableObject)?.BindingContext as AiModelRow;

    private async void OnBack(object? sender, EventArgs e) => await Shell.Current.GoToAsync("..");

    private async void OnOpenProviders(object? sender, EventArgs e) => await Shell.Current.GoToAsync("aiProviders");

    private async void OnAddModel(object? sender, EventArgs e)
    {
        var provider = ProviderOf(sender);
        if (provider is null) return;
        var model = await DisplayPromptAsync($"给「{provider.Title}」加模型",
            "模型名（如 deepseek-chat / gpt-4o-mini）。", maxLength: 60);
        if (string.IsNullOrWhiteSpace(model)) return;
        var row = new AiModelRow { Model = model.Trim() };
        provider.Models.Add(row);
        provider.RefreshSummary();
        await SaveAsync("已加模型。点它的「上下文窗口」和下面的开关补参数。");
    }

    private async void OnDeleteModel(object? sender, EventArgs e)
    {
        var model = ModelOf(sender);
        if (model is null) return;
        var owner = Providers.FirstOrDefault(v => v.Models.Contains(model));
        if (owner is null) return;
        var ok = await DisplayAlert("删除模型", $"删掉「{model.DisplayName}」？", "删除", "取消");
        if (!ok) return;
        owner.Models.Remove(model);
        owner.RefreshSummary();
        await SaveAsync("已删除模型。");
    }

    private async void OnModelToggled(object? sender, ToggledEventArgs e)
    {
        if (_loading) return;
        await SaveAsync(e.Value ? "模型已启用。" : "模型已停用：轮换池里跳过它。");
    }

    private async void OnCapabilityToggled(object? sender, ToggledEventArgs e)
    {
        if (_loading) return;
        await SaveAsync("能力已保存：下一轮请求起生效。");
    }

    private async void OnWindowUnfocused(object? sender, FocusEventArgs e)
    {
        if (_loading) return;
        if (sender is Entry entry && entry.BindingContext is AiModelRow row)
        {
            // 输入框里只留数字；非法值直接清成「跟随全局」。
            var digits = new string((entry.Text ?? "").Where(char.IsDigit).ToArray());
            if (digits != (entry.Text ?? "")) entry.Text = digits;
            row.ContextWindow = digits;
        }
        await SaveAsync("上下文窗口已保存。");
    }
}
