using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.IO;
using System.Text.Json;
using WarmAsBefore.Modules.Worldbook;
using WarmAsBefore.Models;
using WarmAsBefore.Services;

namespace WarmAsBefore.ViewModels;

public sealed partial class WorldbookViewModel : ObservableObject
{
    private readonly WorldbookGenerator _generator;
    private readonly StorageProvider _store;

    [ObservableProperty] private string _description = "";
    [ObservableProperty] private bool _generateCover = true;
    [ObservableProperty] private WorldbookMode _mode = WorldbookMode.TextOnly;
    [ObservableProperty] private bool _isThinking;
    [ObservableProperty] private WorldbookGenerationResult? _result;
    [ObservableProperty] private string _statusText = "输入描述，AI 将生成世界书";

    public List<WorldbookMode> ModeChoices { get; } = new() { WorldbookMode.TextOnly, WorldbookMode.WithSprites };

    public WorldbookViewModel(WorldbookGenerator generator, StorageProvider store)
    {
        _generator = generator;
        _store = store;
    }

    [RelayCommand]
    private async Task Generate()
    {
        if (string.IsNullOrWhiteSpace(Description))
        {
            StatusText = "请输入描述";
            return;
        }

        IsThinking = true;
        StatusText = "AI 正在生成世界书…";

        try
        {
            var result = await _generator.GenerateAsync(Description, GenerateCover, Mode);
            Result = result;
            StatusText = result.CoverGenerated ? "世界书已生成，封面已创建" : "世界书已生成（未生成封面）";

            if (result.Worldbook.Characters.Count > 0)
            {
                await SaveWorldbookAsync(result.Worldbook);
            }
        }
        catch (Exception ex)
        {
            StatusText = "生成失败：" + ex.Message;
            App.WriteLog("WorldbookViewModel.Generate -> " + ex);
        }
        finally
        {
            IsThinking = false;
        }
    }

    [RelayCommand]
    private async Task Polish()
    {
        if (Result is null || Result.Worldbook is null) return;

        IsThinking = true;
        StatusText = "AI 正在润色世界书…";

        try
        {
            var wb = Result.Worldbook;
            var prompt = $"请润色以下世界书内容，使其更加丰富和生动：\n\n标题：{wb.Title}\n描述：{wb.Description}\n角色：{string.Join("、", wb.Characters.Select(c => c.Name))}\n\n请返回润色后的JSON。";
            var msgs = new List<ChatMessage>
            {
                new() { Role = "user", Content = prompt }
            };
            var reply = await _generator.ChatAsync(msgs);
            if (reply is not null)
            {
                try
                {
                    var parsed = JsonSerializer.Deserialize<WorldbookGenerator.WorldbookJson>(reply, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (parsed is not null)
                    {
                        wb.Title = parsed.Title;
                        wb.Description = parsed.Description;
                        wb.Characters.Clear();
                        foreach (var c in parsed.Characters ?? [])
                        {
                            wb.Characters.Add(new WorldbookCharacter
                            {
                                Name = c.Name,
                                Gender = c.Gender,
                                Personality = c.Personality,
                                Description = c.Description,
                                HasSprite = c.HasSprite,
                                SpritePath = c.SpritePath ?? ""
                            });
                        }
                        wb.AiPolished = true;
                        StatusText = "世界书已润色";
                    }
                }
                catch
                {
                    StatusText = "润色失败，格式解析错误";
                }
            }
        }
        catch (Exception ex)
        {
            StatusText = "润色失败：" + ex.Message;
        }
        finally
        {
            IsThinking = false;
        }
    }

    [RelayCommand]
    private async Task ConvertToTextOnly()
    {
        if (Result is null || Result.Worldbook is null) return;
        var wb = Result.Worldbook;
        wb.Mode = WorldbookMode.TextOnly;
        foreach (var c in wb.Characters)
        {
            c.HasSprite = false;
            c.SpritePath = "";
        }
        wb.HasSprite = false;
        StatusText = "已转换为纯文字模式";
    }

    [RelayCommand]
    private async Task ConvertToSpriteMode()
    {
        if (Result is null || Result.Worldbook is null) return;
        var wb = Result.Worldbook;
        wb.Mode = WorldbookMode.WithSprites;
        wb.HasSprite = true;
        foreach (var c in wb.Characters)
        {
            c.HasSprite = true;
            if (string.IsNullOrWhiteSpace(c.SpritePath))
                c.SpritePath = $"assets/characters/{c.Name}/default.png";
        }
        StatusText = "已转换为立绘模式";
    }

    private async Task SaveWorldbookAsync(WorldbookEntry entry)
    {
        try
        {
            var path = Path.Combine(_store.Root, "worldbooks");
            Directory.CreateDirectory(path);
            var file = Path.Combine(path, $"{entry.Id}.json");
            var json = JsonSerializer.Serialize(entry, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(file, json);
        }
        catch (Exception ex)
        {
            App.WriteLog("WorldbookViewModel.SaveWorldbook -> " + ex);
        }
    }

    [RelayCommand]
    private async Task GoBack() => await Shell.Current.GoToAsync("..");
}