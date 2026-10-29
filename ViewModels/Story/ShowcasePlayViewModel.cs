using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WarmAsBefore.Controls;
using WarmAsBefore.Models;
using WarmAsBefore.Services;

namespace WarmAsBefore.ViewModels;

/// <summary>
/// 展示案播放页：依次执行步骤，打字机显示对话、立绘叠加、CG 全屏覆盖，自动模拟。
/// </summary>
public sealed partial class ShowcasePlayViewModel : ObservableObject
{
    private readonly Modules.Showcase.ShowcaseStore _store;
    public ShowcaseScript Script { get; private set; } = new();
    private int _index;

    /// <summary>列表页导航到播放页前写入的待播放脚本 id。</summary>
    public static string? PendingScriptId;

    [ObservableProperty] private bool _dialogueVisible;
    [ObservableProperty] private string _speakerName = "";
    [ObservableProperty] private string _dialogueText = "";
    [ObservableProperty] private bool _cgVisible;
    [ObservableProperty] private ImageSource? _cgSource;
    [ObservableProperty] private string _progressText = "";
    [ObservableProperty] private bool _isFinished;
    [ObservableProperty] private bool _bgVisible;
    [ObservableProperty] private ImageSource? _bgSource;
    [ObservableProperty] private bool _isTyping;
    private CancellationTokenSource? _typeCts;
    private bool _skipTyping;

    public ObservableCollection<ShowcaseSprite> Sprites { get; } = new();
    /// <summary>三列立绘渲染用的子集合（LeftSprites / CenterSprites / RightSprites）。</summary>
    public ObservableCollection<ShowcaseSprite> LeftSprites { get; } = new();
    public ObservableCollection<ShowcaseSprite> CenterSprites { get; } = new();
    public ObservableCollection<ShowcaseSprite> RightSprites { get; } = new();

    public ShowcasePlayViewModel(Modules.Showcase.ShowcaseStore store) => _store = store;

    [RelayCommand]
    private async Task LoadAsync(string? scriptId)
    {
        if (string.IsNullOrEmpty(scriptId)) return;
        Script = await _store.GetAsync(scriptId) ?? new ShowcaseScript();
        _index = -1;
        DialogueVisible = false;
        CgVisible = false;
        IsFinished = false;
        Sprites.Clear(); UpdatePositional();
        ProgressText = $"0/{Script.Steps.Count}";
        if (!string.IsNullOrEmpty(Script.BackgroundPath))
        {
            var bg = Modules.Showcase.ShowcaseStore.FullPath(Script.BackgroundPath);
            if (File.Exists(bg)) { BgSource = ImageSource.FromFile(bg); BgVisible = true; }
        }
        await AdvanceAsync();
    }

    [RelayCommand]
    private async Task AdvanceAsync()
    {
        if (IsFinished) return;
        // 结束上一步
        CgVisible = false;
        DialogueVisible = false;
        // 如果正在打字，先跳过到结尾
        if (_skipTyping) { _skipTyping = false; return; }

        _index++;
        if (_index >= Script.Steps.Count)
        {
            IsFinished = true;
            ProgressText = "播放结束";
            return;
        }

        var step = Script.Steps[_index];
        ProgressText = $"{_index + 1}/{Script.Steps.Count}";
        switch (step.Type)
        {
            case "dialogue":
                DialogueVisible = true;
                CgVisible = false;
                SpeakerName = step.Speaker == "user" ? "你" : (string.IsNullOrEmpty(Script.CharacterName) ? "角色" : Script.CharacterName);
                await TypewriteAsync(step.Text);
                break;
            case "cg":
                DialogueVisible = false;
                CgVisible = true;
                var cgPath = Modules.Showcase.ShowcaseStore.FullPath(step.CgPath);
                CgSource = File.Exists(cgPath) ? ImageSource.FromFile(cgPath) : null;
                break;
            case "sprites":
                Sprites.Clear();
                foreach (var sp in step.Sprites) Sprites.Add(sp);
                UpdatePositional();
                break;
        }

        // 对话结束后稍作等待自动进入下一步，或用户点任意处触发下一步
    }

    [RelayCommand]
    private void AdvanceTap()
    {
        if (IsTyping)
        {
            _skipTyping = true;
        }
        else if (!IsFinished)
        {
            _ = AdvanceAsync();
        }
    }

    private async Task TypewriteAsync(string text)
    {
        _typeCts?.Cancel();
        var cts = new CancellationTokenSource();
        _typeCts = cts;
        IsTyping = true;
        DialogueText = "";
        _skipTyping = false;
        var chs = text.ToCharArray();
        for (int i = 0; i < chs.Length; i++)
        {
            if (_skipTyping || cts.Token.IsCancellationRequested)
            {
                DialogueText = text;
                break;
            }
            DialogueText += chs[i];
            try { await Task.Delay(30, cts.Token); }
            catch (OperationCanceledException) { break; }
        }
        IsTyping = false;
    }

    private void UpdatePositional()
    {
        LeftSprites.Clear(); CenterSprites.Clear(); RightSprites.Clear();
        foreach (var sp in Sprites)
        {
            (sp.Position switch { "left" => LeftSprites, "right" => RightSprites, _ => CenterSprites }).Add(sp);
        }
    }

    [RelayCommand]
    private async Task BackAsync() => await Shell.Current.GoToAsync("..");
}
