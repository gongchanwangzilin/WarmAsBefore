using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using WarmAsBefore.Controls;
using WarmAsBefore.Models;
using WarmAsBefore.Modules.ApiManager;
using WarmAsBefore.Modules.NovelImport;
using WarmAsBefore.Services;

namespace WarmAsBefore.ViewModels;

/// <summary>
/// 平行宇宙聊天页：AI 扮演小说世界中的角色，与玩家在小说世界观里互动。
/// </summary>
public sealed partial class NovelWorldViewModel : ObservableObject
{
    private readonly NovelLibrary _library;
    private readonly ApiGateway _api;
    private readonly List<ChatMessage> _history = new();
    private const int MaxHistory = 24;

    [ObservableProperty] private string _title = "平行宇宙";
    [ObservableProperty] private string _worldLabel = "";
    [ObservableProperty] private string _inputText = "";
    [ObservableProperty] private bool _isThinking;
    [ObservableProperty] private ObservableCollection<NovelMessage> _messages = new();

    public NovelWorldViewModel(NovelLibrary library, ApiGateway api)
    {
        _library = library;
        _api = api;
    }

    public async Task LoadAsync(string novelId)
    {
        var novel = await _library.GetAsync(novelId);
        if (novel is null) return;
        Title = novel.Title;
        WorldLabel = novel.Characters.Count > 0 ? "世界角色：" + string.Join("、", novel.Characters) : "未知世界";
        var system = BuildSystemPrompt(novel);
        _history.Clear();
        _history.Add(new ChatMessage { Role = "system", Content = system });
        AddMessage("assistant",
            novel.Characters.Count > 0
                ? $"欢迎来到《{novel.Title}》的平行宇宙。你是误入这个世界的旅人，\n{(novel.Characters.Count > 0 ? "这里生活着：" + string.Join("、", novel.Characters) : "")}。这里的角色会把你当成这个世界的一部分，和你一起经历接下来的故事。"
                : $"欢迎来到《{novel.Title}》的平行宇宙。\n\n{novel.Summary}");
    }

    private static string BuildSystemPrompt(NovelEntry novel)
    {
        var chars = novel.Characters.Count > 0 ? string.Join("、", novel.Characters) : "（未知角色）";
        var summary = string.IsNullOrWhiteSpace(novel.Summary) ? "（无简介）" : novel.Summary;
        var sb = new System.Text.StringBuilder();
        sb.Append($"你正在扮演小说《{novel.Title}》的平行宇宙。世界设定：{summary}\n");
        if (novel.KeyEvents.Count > 0)
        {
            sb.Append("这个世界已经发生过的事件（按顺序，供你推进时保持连贯）：\n");
            foreach (var e in novel.KeyEvents.Take(20)) sb.Append("· ").Append(e).Append('\n');
        }
        if (novel.Completeness is { Issues.Count: > 0 } c)
        {
            sb.Append($"注意：这份世界设定的剧情并不完整（完整度 {c.Score}/100）。");
            sb.Append("如果用户推进到缺失的部分，请合理补全，不要让剧情突然断掉。\n");
        }
        sb.Append($"这个世界的角色：{chars}\n");
        sb.Append("用户是穿越进入这个世界的局外人，真实存在。你以这个世界的角色视角、按小说风格与他对话、推进剧情，");
        sb.Append("回应要符合世界观与人物性格，保持沉浸感，但不要替用户做决定。用中文回复。");
        return sb.ToString();
    }

    [RelayCommand]
    private async Task Send()
    {
        if (string.IsNullOrWhiteSpace(InputText) || IsThinking) return;
        var msg = InputText;
        InputText = "";
        AddMessage("user", msg);
        IsThinking = true;
        try
        {
            _history.Add(new ChatMessage { Role = "user", Content = msg });
            var reply = await _api.Chat(_history);
            if (reply is null)
            {
                AddMessage("assistant", "（世界短暂沉默了一下…）");
            }
            else
            {
                AddMessage("assistant", reply);
                _history.Add(new ChatMessage { Role = "assistant", Content = reply });
                while (_history.Count > MaxHistory)
                    _history.RemoveAt(1); // 保留 system 首条
            }
        }
        catch (Exception ex)
        {
            App.WriteLog("NovelWorld.Send -> " + ex);
            AddMessage("assistant", "（这个世界似乎连接中断了…）");
        }
        finally
        {
            IsThinking = false;
        }
    }

    private void AddMessage(string role, string text)
    {
        var isUser = role == "user";
        // 颜色与对齐不再由视图模型写死：气泡底/墨归风格世界令牌（BubbleMine / BubbleTheirs），
        // VM 只回答「这句是谁说的」。旧写法把奶油 hex 写在这里，换世界后气泡仍停在旧色。
        Messages.Add(new NovelMessage { Text = text, IsMine = isUser });
    }

    [RelayCommand]
    private async Task GoBack() => await Shell.Current.GoToAsync("..");
}

public sealed class NovelMessage
{
    public string Text { get; init; } = "";
    public bool IsMine { get; init; }
    public bool ShowMine => IsMine;
    public bool ShowTheirs => !IsMine;
}
