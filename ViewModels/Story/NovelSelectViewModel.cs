using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using WarmAsBefore.Controls;
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
                Toast.Error("解析失败：请检查 AI 配置或文本内容");
                return;
            }
            var entry = await _library.AddAsync(result);
            await Refresh();

            // 展示完整性报告：让用户在上手前就知道「这个世界的剧情讲得全不全」。
            var c = entry.Completeness;
            if (c is not null)
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"「{entry.Title}」");
                sb.AppendLine();
                sb.AppendLine($"完整度：{c.Score}/100（原文 {c.SourceLength} 字，切 {c.SegmentCount} 段）");
                sb.AppendLine(c.Verdict);
                if (c.Issues.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("需要注意：");
                    foreach (var issue in c.Issues.Take(6)) sb.AppendLine("· " + issue);
                }
                await Shell.Current.DisplayAlert(
                    c.CanEnter ? "平行宇宙创建成功" : "已创建（剧情不完整）",
                    sb.ToString().TrimEnd(), "好");
            }
            else
            {
                await Shell.Current.DisplayAlert("平行宇宙创建成功",
                    $"「{entry.Title}」\n\n{entry.Summary}", "好");
            }
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
