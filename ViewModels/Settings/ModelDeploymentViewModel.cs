using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.Storage;
using WarmAsBefore.Models;
using WarmAsBefore.Services;
using WarmAsBefore.Services.Model;

namespace WarmAsBefore.ViewModels.Settings;

/// <summary>
/// 模型部署向导的视图模型。所有输入（名字 / 类型 / 来源 / 安装位置 / 校验和）都由界面收集，
/// 全部来自用户，工具不内置任何默认。命令负责把表单交给 <see cref="ModelDeploymentService"/>。
/// </summary>
public partial class ModelDeploymentViewModel : ObservableObject
{
    private readonly ModelDeploymentService _deploy;
    private readonly SettingsManager _settings;
    private CancellationTokenSource? _cts;

    public ObservableCollection<DeployedModelRow> Models { get; } = new();

    [ObservableProperty] private string newName = "";
    [ObservableProperty] private ModelKind newKind = ModelKind.Embedding;
    [ObservableProperty] private string newSource = "";
    [ObservableProperty] private string newInstallPath = "";
    [ObservableProperty] private string newChecksum = "";
    [ObservableProperty] private double progressValue;     // 0..1
    [ObservableProperty] private string progressText = "";
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string statusText = "";

    public List<ModelKind> KindChoices { get; } = new()
    {
        ModelKind.Embedding, ModelKind.SmallModel, ModelKind.LLM, ModelKind.Vision
    };

    public ObservableCollection<ModelSuggestion> Suggestions { get; } = new();

    public ModelDeploymentViewModel(ModelDeploymentService deploy, SettingsManager settings)
    {
        _deploy = deploy;
        _settings = settings;
        // 仅作便利默认：用户随时可改 / 用「选择目录」覆盖。位置仍由用户决定。
        NewInstallPath = Path.Combine(FileSystem.AppDataDirectory, "WarmAsBefore", "models");
        Refresh();
    }

    public void Refresh()
    {
        Models.Clear();
        foreach (var m in _deploy.List())
            Models.Add(new DeployedModelRow(m));
    }

    [RelayCommand]
    private void LoadSuggestions()
    {
        Suggestions.Clear();
        foreach (var s in _deploy.SuggestByPerformance()) Suggestions.Add(s);
    }

    [RelayCommand]
    private void UseSuggestion(ModelSuggestion? s)
    {
        if (s is null) return;
        NewName = s.Name;
        NewKind = s.Kind;
        NewSource = s.ExampleSource;
        StatusText = "已按建议预填（名字 / 来源 / 位置你都能改）。";
    }

    [RelayCommand]
    private async Task DeployAsync()
    {
        if (IsBusy) return;
        var spec = new DeployedModel
        {
            Name = NewName.Trim(),
            Kind = NewKind,
            Source = NewSource.Trim(),
            InstallPath = NewInstallPath.Trim(),
            Checksum = string.IsNullOrWhiteSpace(NewChecksum) ? null : NewChecksum.Trim()
        };
        if (string.IsNullOrWhiteSpace(spec.Name) || string.IsNullOrWhiteSpace(spec.Source) || string.IsNullOrWhiteSpace(spec.InstallPath))
        {
            StatusText = "名字、来源、安装位置三项都要填。";
            return;
        }

        IsBusy = true;
        ProgressValue = 0;
        ProgressText = "准备中…";
        _cts = new CancellationTokenSource();
        var progress = new Progress<ModelDeployProgress>(p =>
        {
            ProgressText = p.Message ?? p.Phase.ToString();
            ProgressValue = p.Total > 0 ? System.Math.Clamp((double)p.Received / p.Total, 0, 1) : 0;
        });
        try
        {
            var result = await _deploy.DeployAsync(spec, progress, _cts.Token);
            StatusText = result.Ok ? result.Message : ("部署失败：" + result.Message);
            if (result.Ok) { NewName = ""; NewSource = ""; NewChecksum = ""; }
        }
        catch (Exception ex) { StatusText = "部署异常：" + ex.Message; }
        finally { IsBusy = false; ProgressValue = 0; Refresh(); }
    }

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    [RelayCommand]
    private async Task RemoveAsync(DeployedModelRow? row)
    {
        if (row is null || IsBusy) return;
        await _deploy.RemoveAsync(row.Id);
        Refresh();
        StatusText = "已移除登记（并删除安装目录）。";
    }

    [RelayCommand]
    private void OpenFolder(DeployedModelRow? row)
    {
        if (row is null || string.IsNullOrWhiteSpace(row.InstallPath)) return;
        try
        {
            if (Directory.Exists(row.InstallPath))
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(row.InstallPath) { UseShellExecute = true });
        }
        catch { }
    }
}

/// <summary>已部署模型行的展示模型（快照，不需要变更通知）。</summary>
public sealed class DeployedModelRow
{
    public DeployedModelRow(DeployedModel m)
    {
        Id = m.Id; Name = m.Name; Kind = m.Kind; Source = m.Source;
        InstallPath = m.InstallPath; Status = m.Status; LastError = m.LastError ?? "";
        Bytes = m.Bytes;
    }
    public string Id { get; }
    public string Name { get; }
    public ModelKind Kind { get; }
    public string Source { get; }
    public string InstallPath { get; }
    public DeployStatus Status { get; }
    public string LastError { get; }
    public long Bytes { get; }
    public string StatusText => Status switch
    {
        DeployStatus.Installed => $"已安装 · {Bytes / 1_000_000d:0.0} MB",
        DeployStatus.Failed => "失败：" + LastError,
        _ => Status.ToString()
    };
}
