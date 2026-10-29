using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WarmAsBefore.Controls;
using WarmAsBefore.Drawables;
using WarmAsBefore.Models;
using WarmAsBefore.Modules.AiChat;
using WarmAsBefore.Modules.Automation;
using WarmAsBefore.Modules.SaveSystem;
using WarmAsBefore.Services;

namespace WarmAsBefore.ViewModels;

/// <summary>
/// 换装：列出角色现有的装扮，也能用一句描述让画面模型生成一套新的。
/// 生成的是「立绘素材」：落到角色目录并登记进 SpriteMap，之后走路人、桌宠、对话立绘都用同一份。
/// </summary>
public sealed partial class OutfitViewModel : ObservableObject
{
    private readonly GameEngine _engine;
    private readonly CharacterLibrary _library;
    private readonly ImageGenerationService _images;
    private readonly StorageProvider _store;

    public ObservableCollection<OutfitItem> Outfits { get; } = new();

    [ObservableProperty] private string _characterName = "";
    [ObservableProperty] private string _currentOutfit = "";
    [ObservableProperty] private string _prompt = "";
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _hasCharacter;
    [ObservableProperty] private bool _busy;
    [ObservableProperty] private ImageSource? _currentImage;
    [ObservableProperty] private bool _hasCurrentImage;

    public OutfitViewModel(GameEngine engine, CharacterLibrary library,
        ImageGenerationService images, StorageProvider store)
    {
        _engine = engine;
        _library = library;
        _images = images;
        _store = store;
    }

    public async Task RefreshAsync()
    {
        try
        {
            var ch = CurrentCharacter();
            HasCharacter = ch is not null;
            Outfits.Clear();
            CurrentImage = null;
            HasCurrentImage = false;
            if (ch is null)
            {
                CharacterName = "还没有进入角色";
                CurrentOutfit = "";
                Status = "先在角色选择页开始一局，或把某个角色设为主角。";
                return;
            }
            CharacterName = ch.Profile.Name;
            CurrentOutfit = ch.State.CurrentOutfit;
            var outfitKeys = ch.SpriteMap.Keys
                .Select(k => k.Split('/')[0])
                .Where(k => !string.IsNullOrWhiteSpace(k))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(k => k == ch.State.CurrentOutfit ? 0 : 1)
                .ThenBy(k => k, StringComparer.CurrentCulture)
                .ToList();
            foreach (var key in outfitKeys)
            {
                var preview = PreviewFor(ch, key);
                Outfits.Add(new OutfitItem
                {
                    Key = key,
                    Name = key == "default" ? "默认装扮" : key,
                    Image = preview,
                    HasImage = preview is not null,
                    IsCurrent = string.Equals(key, ch.State.CurrentOutfit, StringComparison.OrdinalIgnoreCase)
                });
            }
            var current = PreviewFor(ch, ch.State.CurrentOutfit);
            if (current is null && !string.IsNullOrWhiteSpace(ch.Avatar))
            {
                var full = Path.Combine(_store.Root, ch.Avatar);
                if (File.Exists(full)) current = ImageSource.FromFile(full);
            }
            CurrentImage = current;
            HasCurrentImage = current is not null;
            Status = Outfits.Count == 0
                ? "这个角色还没有立绘。可以在下面用一句描述生成第一套装扮。"
                : $"共 {Outfits.Count} 套装扮；换装后，对话与桌宠会一起用新立绘。";
        }
        catch (Exception ex)
        {
            Status = "读取失败：" + ex.Message;
            App.WriteLog("OutfitViewModel.Refresh -> " + ex);
        }
    }

    private CharacterData? CurrentCharacter()
    {
        if (_engine.ActiveCharacter is { } active) return active;
        var id = _engine.State.CharacterId;
        return !string.IsNullOrEmpty(id) && _engine.Roster.TryGetValue(id, out var ch) ? ch : null;
    }

    private ImageSource? PreviewFor(CharacterData ch, string outfit)
    {
        var key = ch.SpriteMap.Keys.FirstOrDefault(k =>
            k.StartsWith(outfit + "/", StringComparison.OrdinalIgnoreCase));
        if (key is null || !ch.SpriteMap.TryGetValue(key, out var rel) || string.IsNullOrWhiteSpace(rel))
            return null;
        var full = Path.Combine(_store.Root, rel);
        return File.Exists(full) ? ImageSource.FromFile(full) : null;
    }

    [RelayCommand]
    private async Task Wear(OutfitItem? item)
    {
        if (item is null) return;
        var ch = CurrentCharacter();
        if (ch is null) return;
        try
        {
            ch.State.CurrentOutfit = item.Key;
            var firstEmotion = ch.SpriteMap.Keys
                .FirstOrDefault(k => k.StartsWith(item.Key + "/", StringComparison.OrdinalIgnoreCase));
            if (firstEmotion is not null) ch.State.CurrentEmotion = firstEmotion.Split('/')[1];
            await _library.UpdateAsync(ch);
            await RefreshAsync();
            Status = $"已换上「{item.Name}」。";
        }
        catch (Exception ex)
        {
            Status = "换装失败：" + ex.Message;
            App.WriteLog("OutfitViewModel.Wear -> " + ex);
        }
    }

    [RelayCommand]
    private async Task Generate()
    {
        var ch = CurrentCharacter();
        if (ch is null) return;
        var prompt = (Prompt ?? "").Trim();
        if (prompt.Length == 0) { Status = "先写一句画面描述（例如：白色针织衫与浅色长裙，站在窗边）。"; return; }
        if (!_images.Enabled)
        {
            Status = "画面生成没有开启：设置 → AI → 画面生成里填接口与密钥后就能用。";
            return;
        }
        Busy = true;
        Status = "正在生成立绘…";
        try
        {
            var appearance = string.IsNullOrWhiteSpace(ch.Profile.Description) ? "" : "；角色设定：" + ch.Profile.Description;
            var (path, message) = await _images.GenerateAsync(
                $"{ch.Profile.Name} 的全身立绘，{prompt}。干净背景，完整人物，不要文字{appearance}",
                "outfit_" + ch.Profile.Id, "1024x1536");
            if (path is null) { Status = message; return; }

            var dir = Path.Combine(StorageRoot, "assets", "characters", ch.Profile.Id, "generated");
            Directory.CreateDirectory(dir);
            var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var dest = Path.Combine(dir, stamp + ".png");
            File.Copy(path, dest, true);
            var rel = $"assets/characters/{ch.Profile.Id}/generated/{stamp}.png";

            var outfitKey = "generated_" + stamp;
            var sprites = new Dictionary<string, string>(ch.SpriteMap, StringComparer.OrdinalIgnoreCase);
            foreach (var emotion in new[] { "normal", "smile", "shy", "sad", "angry" })
                sprites[outfitKey + "/" + emotion] = rel;
            ch.State.CurrentOutfit = outfitKey;
            ch.State.CurrentEmotion = "normal";
            await _library.UpdateAsync(ch with { SpriteMap = sprites });
            Prompt = "";
            await RefreshAsync();
            Status = $"新装扮「{outfitKey}」已生成并穿上。";
        }
        catch (Exception ex)
        {
            Status = "生成失败：" + ex.Message;
            App.WriteLog("OutfitViewModel.Generate -> " + ex);
        }
        finally { Busy = false; }
    }

    private string StorageRoot => _store.Root;

    [RelayCommand]
    private async Task Back() => await Shell.Current.GoToAsync("..");
}

public sealed class OutfitItem
{
    public string Key { get; init; } = "";
    public string Name { get; init; } = "";
    public ImageSource? Image { get; init; }
    public bool HasImage { get; init; }
    public bool IsCurrent { get; init; }
    public string CurrentLabel => IsCurrent ? "穿着中" : "";
}
