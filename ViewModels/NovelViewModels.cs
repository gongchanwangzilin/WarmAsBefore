using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using WarmAsBefore.Models;
using WarmAsBefore.Modules.ApiManager;
using WarmAsBefore.Modules.NovelImport;
using WarmAsBefore.Services;

namespace WarmAsBefore.ViewModels;

/// <summary>平行宇宙选择页：已导入的小说世界列表，可导入新小说、进入世界。</summary>
public sealed partial class NovelSelectViewModel : ObservableObject
{
    private readonly NovelLibrary _library;
    private readonly NovelAnalyzer _novel;

    [ObservableProperty] private ObservableCollection<NovelItem> _novels = new();

    public NovelSelectViewModel(NovelLibrary library, NovelAnalyzer novel)
    {
        _library = library;
        _novel = novel;
    }

    [RelayCommand]
    private async Task Refresh()
    {
        var list = await _library.ListAsync();
        Novels = new ObservableCollection<NovelItem>(list.Select(n => new NovelItem(n)));
    }

    [RelayCommand]
    private async Task EnterNovel(string id) => await Shell.Current.GoToAsync($"novelworld?id={id}");

    [RelayCommand]
    private async Task DeleteNovel(string id)
    {
        var item = Novels.FirstOrDefault(n => n.Id == id);
        var ok = await Shell.Current.DisplayAlert("删除平行宇宙",
            $"确定删除「{item?.Title ?? id}」吗？", "删除", "取消");
        if (!ok) return;
        await _library.DeleteAsync(id);
        await Refresh();
    }

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
                await Shell.Current.DisplayAlert("导入失败", "解析失败：请检查 AI 配置或文本内容", "好");
                return;
            }
            var entry = await _library.AddAsync(result);
            await Refresh();
            await Shell.Current.DisplayAlert("平行宇宙创建成功",
                $"「{entry.Title}」\n\n{entry.Summary}", "好");
        }
        catch (Exception ex)
        {
            App.WriteLog("NovelSelectViewModel.ImportNovel -> " + ex);
        }
    }

    [RelayCommand]
    private async Task GoBack() => await Shell.Current.GoToAsync("..");
}

public sealed class NovelItem
{
    public NovelItem(NovelEntry n)
    {
        Id = n.Id;
        Title = n.Title;
        Summary = n.Summary;
        ImportedAt = n.ImportedAt;
        CharactersLabel = n.Characters.Count > 0 ? "角色：" + string.Join("、", n.Characters) : "角色：—";
    }

    public string Id { get; init; }
    public string Title { get; init; }
    public string Summary { get; init; }
    public DateTime ImportedAt { get; init; }
    public string CharactersLabel { get; init; }
}

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
                ? $"欢迎来到《{novel.Title}》的平行宇宙。你是误入这个世界的旅人，\n{(novel.Characters.Count > 0 ? "这里生活着：" + string.Join("、", novel.Characters) : "")}。她们会把你当成这个世界的一部分，和你一起经历接下来的故事。"
                : $"欢迎来到《{novel.Title}》的平行宇宙。\n\n{novel.Summary}");
    }

    private static string BuildSystemPrompt(NovelEntry novel)
    {
        var chars = novel.Characters.Count > 0 ? string.Join("、", novel.Characters) : "（未知角色）";
        var summary = string.IsNullOrWhiteSpace(novel.Summary) ? "（无简介）" : novel.Summary;
        return $"你正在扮演小说《{novel.Title}》的平行宇宙。世界设定：{summary}\n" +
               $"这个世界的角色：{chars}\n" +
               $"用户是穿越进入这个世界的局外人，真实存在。你以这个世界的角色视角、按小说风格与他对话、推进剧情，" +
               $"回应要符合世界观与人物性格，保持沉浸感，但不要替用户做决定。用中文回复。";
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
        Messages.Add(new NovelMessage
        {
            Text = text,
            Align = isUser ? LayoutOptions.End : LayoutOptions.Start,
            BgColor = isUser ? new Color(0.86f, 0.78f, 0.63f) : new Color(1f, 0.97f, 0.94f),
            TextColor = isUser ? Color.FromArgb("#4A3D2C") : Color.FromArgb("#5D4A3A")
        });
    }

    [RelayCommand]
    private async Task GoBack() => await Shell.Current.GoToAsync("..");
}

public sealed class NovelMessage
{
    public string Text { get; init; } = "";
    public LayoutOptions Align { get; init; }
    public Color BgColor { get; init; } = Colors.White;
    public Color TextColor { get; init; } = Colors.Black;
}
