using System.Collections.ObjectModel;
using WarmAsBefore.Models;
using WarmAsBefore.Services;
using WarmAsBefore.ViewModels;

namespace WarmAsBefore.Views;

/// <summary>
/// AI 厂商管理页：一家厂商 = 端点 + 密钥 + 超时 + 多款模型。
/// 这一页只管「接哪家、用什么密钥」，模型各自的上下文窗口与能力在「模型管理」里配。
/// 每次改动都立刻落盘，并触发运行时重新展开轮换池（不用重启）。
/// </summary>
public partial class AiProvidersPage : ContentPage
{
    private readonly SettingsManager _settings;
    private bool _loading;

    public ObservableCollection<AiProviderRow> Providers { get; } = new();

    public AiProvidersPage(SettingsManager settings)
    {
        InitializeComponent();
        _settings = settings;
        BindingContext = this;
        Load();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        Load();   // 从模型页回来时刷新「模型：…」摘要
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
        if (!string.IsNullOrEmpty(note)) ShowStatus(note);
    }

    private void ShowStatus(string text)
    {
        StatusLabel.Text = text;
        StatusLabel.IsVisible = true;
    }

    private static AiProviderRow? RowOf(object? sender)
        => (sender as BindableObject)?.BindingContext as AiProviderRow;

    private async void OnBack(object? sender, EventArgs e) => await Shell.Current.GoToAsync("..");

    private async void OnOpenModels(object? sender, EventArgs e) => await Shell.Current.GoToAsync("aiModels");

    private async void OnProviderToggled(object? sender, ToggledEventArgs e)
    {
        if (_loading) return;
        await SaveAsync(e.Value ? "已启用这家厂商。" : "已停用这家厂商：轮换池里不再用它。");
    }

    private async void OnEditBasics(object? sender, EventArgs e)
    {
        var row = RowOf(sender);
        if (row is null) return;
        var name = await DisplayPromptAsync("厂商名称", "好认的名字，如「硅基流动」「DeepSeek 官方」。",
            initialValue: row.Name, maxLength: 30);
        if (name is null) return;
        var url = await DisplayPromptAsync("端点地址", "OpenAI 兼容的 chat/completions 地址。",
            initialValue: row.Url, maxLength: 300);
        if (url is null) return;
        row.Name = name.Trim();
        row.Url = url.Trim();
        await SaveAsync("厂商信息已保存。");
    }

    private async void OnEditKeys(object? sender, EventArgs e)
    {
        var row = RowOf(sender);
        if (row is null) return;
        var keys = await DisplayPromptAsync($"「{row.Title}」的密钥",
            "一行一枚，可多枚轮流用（也可用逗号分隔）。", initialValue: row.KeysText, maxLength: 4000);
        if (keys is null) return;
        row.KeysText = keys;
        await SaveAsync("密钥已保存。");
    }

    private async void OnEditTimeout(object? sender, EventArgs e)
    {
        var row = RowOf(sender);
        if (row is null) return;
        var t = await DisplayPromptAsync($"「{row.Title}」的超时",
            "秒数（3~600），留空 = 用全局超时。", initialValue: row.TimeoutSeconds,
            maxLength: 4, keyboard: Keyboard.Numeric);
        if (t is null) return;
        row.TimeoutSeconds = t.Trim();
        await SaveAsync("超时已保存。");
    }

    private async void OnMoveUp(object? sender, EventArgs e)
    {
        var row = RowOf(sender);
        if (row is null) return;
        var i = Providers.IndexOf(row);
        if (i <= 0) return;
        Providers.Move(i, i - 1);
        await SaveAsync("顺序已调整：靠前的先用。");
    }

    private async void OnMoveDown(object? sender, EventArgs e)
    {
        var row = RowOf(sender);
        if (row is null) return;
        var i = Providers.IndexOf(row);
        if (i < 0 || i >= Providers.Count - 1) return;
        Providers.Move(i, i + 1);
        await SaveAsync("顺序已调整。");
    }

    private async void OnDeleteProvider(object? sender, EventArgs e)
    {
        var row = RowOf(sender);
        if (row is null) return;
        var ok = await DisplayAlert("删除厂商", $"删掉「{row.Title}」？它下面的模型也一起没了。", "删除", "取消");
        if (!ok) return;
        Providers.Remove(row);
        await SaveAsync("已删除。");
    }

    private async void OnAddProvider(object? sender, EventArgs e)
    {
        var url = (NewUrl.Text ?? "").Trim();
        if (url.Length == 0)
        {
            ShowStatus("先填端点地址（OpenAI 兼容），再点「添加厂商」。");
            return;
        }
        var row = new AiProviderRow
        {
            Name = (NewName.Text ?? "").Trim(),
            Url = url,
            KeysText = (NewKeys.Text ?? "").Trim(),
            TimeoutSeconds = (NewTimeout.Text ?? "").Trim()
        };
        var model = (NewModel.Text ?? "").Trim();
        if (model.Length > 0) row.Models.Add(new AiModelRow { Model = model });
        row.RefreshSummary();
        Providers.Add(row);

        NewName.Text = "";
        NewUrl.Text = "";
        NewKeys.Text = "";
        NewModel.Text = "";
        NewTimeout.Text = "";
        await SaveAsync("已添加厂商。到「模型管理」里补上下文窗口和能力。");
    }
}
