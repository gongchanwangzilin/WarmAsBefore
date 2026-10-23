using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WarmAsBefore.Services;

namespace WarmAsBefore.ViewModels;

/// <summary>
/// QQ 壳 · 论坛：话题面（纯本地数据，不走 AI）。
/// 种子帖子是写死的静态内容；用户自己发的帖子落盘到 storage 的 "qq_forum_posts"，
/// 与种子合并按时间展示。
/// </summary>
public sealed partial class QqForumViewModel : ObservableObject
{
    private const string PostsKey = "qq_forum_posts";

    private readonly StorageProvider _store;

    /// <summary>一个话题。</summary>
    public sealed class ForumTopicItem
    {
        public string Id { get; }
        public string Title { get; }
        public string Icon { get; }
        public bool IsSelected { get; set; }
        public ForumTopicItem(string id, string title, string icon)
        {
            Id = id; Title = title; Icon = icon;
        }
    }

    /// <summary>一条帖子的展示行。</summary>
    public sealed class ForumPostRow
    {
        public string Author { get; init; } = "";
        public string Text { get; init; } = "";
        public string Time { get; init; } = "";
        public bool IsMe { get; init; }
        public string Initial => string.IsNullOrEmpty(Author) ? "?" : Author[..1];
        public string Header => $"{Author} · {Time}";
    }

    /// <summary>落盘模型（与 WeChatContacts 同款的「外层 record + Items」形式）。</summary>
    public sealed record ForumPost(string TopicId, string Author, string Text, DateTime At);
    public sealed record ForumPosts
    {
        public List<ForumPost> Items { get; init; } = new();
    }

    private static Services.LocalizationService L => Services.LocalizationService.Current;

    /// <summary>话题（标题走 i18n；纯数据，不随 AI 生成）。</summary>
    public List<ForumTopicItem> Topics { get; } = new()
    {
        new("daily",  L["Forum_TopicDaily"],  "🌤"),
        new("wish",   L["Forum_TopicWish"],   "🌠"),
        new("today",  L["Forum_TopicToday"],  "✏️"),
    };

    [ObservableProperty] private ForumTopicItem _selected;
    [ObservableProperty] private ObservableCollection<ForumPostRow> _posts = new();
    [ObservableProperty] private string _inputText = "";
    [ObservableProperty] private bool _hasPosts;

    // 种子帖子：写死的静态内容（作者为常驻角色的常见称呼）
    private static readonly (string Topic, string Author, string Text)[] SeedPosts =
    {
        ("daily", "小雨", "今天在想给你做红豆糕，做好了叫你来尝一口。"),
        ("daily", "小林", "图书馆还完书了，靠窗的位置晒到太阳，一会可以过来。"),
        ("wish",  "小雨", "愿望：下次一起去江边看日落，你把相机带上。"),
        ("today", "小林", "今天想做点什么？我都可以，泡茶也行，陪你说说话也行。"),
    };

    public QqForumViewModel(StorageProvider store)
    {
        _store = store;
        Selected = Topics[0];
        Selected.IsSelected = true;
    }

    public async Task LoadAsync()
    {
        await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        var userPosts = new List<ForumPost>();
        try
        {
            var data = await _store.Load<ForumPosts>(PostsKey);
            if (data is not null) userPosts.AddRange(data.Items);
        }
        catch (Exception ex)
        {
            App.WriteLog("QqForumViewModel.Reload -> " + ex.Message);
        }

        var seed = SeedPosts
            .Where(s => s.Topic == Selected.Id)
            .Select(s => new ForumPostRow
            {
                Author = s.Author,
                Text = s.Text,
                Time = "置顶",
                IsMe = false
            })
            .ToList();
        var mine = userPosts
            .Where(p => p.TopicId == Selected.Id)
            .OrderByDescending(p => p.At)
            .Select(p => new ForumPostRow
            {
                Author = p.Author,
                Text = p.Text,
                Time = p.At.ToLocalTime().ToString("MM-dd HH:mm"),
                IsMe = true
            })
            .ToList();

        Posts = new ObservableCollection<ForumPostRow>(seed.Concat(mine));
        HasPosts = Posts.Count > 0;
        InputText = "";
    }

    /// <summary>切话题。</summary>
    [RelayCommand]
    public void SelectTopic(string id)
    {
        var t = Topics.FirstOrDefault(x => x.Id == id);
        if (t is null || t == Selected) return;
        foreach (var x in Topics) x.IsSelected = x == t;
        Selected = t;
        _ = ReloadAsync();
    }

    /// <summary>发帖：纯本地落盘（当前话题 + 「我」），不走 AI。</summary>
    [RelayCommand]
    private async Task Post()
    {
        var text = (InputText ?? "").Trim();
        if (string.IsNullOrEmpty(text) || Selected is null) return;
        try
        {
            var data = await _store.Load<ForumPosts>(PostsKey) ?? new ForumPosts();
            data.Items.Add(new ForumPost(Selected.Id, "我", text, DateTime.UtcNow));
            await _store.Save(PostsKey, data);
        }
        catch (Exception ex)
        {
            App.WriteLog("QqForumViewModel.Post -> " + ex.Message);
        }
        await ReloadAsync();
    }

    [RelayCommand]
    private async Task Back()
    {
        await Shell.Current.GoToAsync("..");
    }
}
