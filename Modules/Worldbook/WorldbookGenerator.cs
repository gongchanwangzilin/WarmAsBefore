using System.IO;
using System.Text;
using System.Text.Json;
using WarmAsBefore.Models;
using WarmAsBefore.Modules.ApiManager;

namespace WarmAsBefore.Modules.Worldbook;

public sealed class WorldbookGenerator
{
    private readonly ApiGateway _api;

    public WorldbookGenerator(ApiGateway api) => _api = api;

    public async Task<WorldbookGenerationResult> GenerateAsync(string description, bool generateCover = true, WorldbookMode mode = WorldbookMode.TextOnly)
    {
        var result = new WorldbookGenerationResult();
        var entry = new WorldbookEntry { UserDescription = description, Mode = mode };

        var systemPrompt = mode == WorldbookMode.WithSprites
            ? "你是一个Galgame世界书生成器。根据用户描述生成一个完整的Galgame世界观，包含角色立绘信息、场景设定、对话风格等。返回JSON格式。"
            : "你是一个文字冒险世界书生成器。根据用户描述生成一个完整的文字世界观，包含角色设定、场景描述、剧情概要等。返回JSON格式。";

        var userContent = $"{systemPrompt}\n\n用户描述：{description}\n\n请生成包含以下内容的JSON：\n{{\n  \"title\": \"世界书标题\",\n  \"description\": \"世界观描述\",\n  \"characters\": [\n    {{\n      \"name\": \"角色名\",\n      \"gender\": \"女/男/中性\",\n      \"personality\": \"性格描述\",\n      \"description\": \"角色简介\",\n      \"hasSprite\": true/false,\n      \"spritePath\": \"立绘路径（如果有立绘）\"\n    }}\n  ],\n  \"hasSprite\": true/false\n}}";

        var msgs = new List<ChatMessage>
        {
            new() { Role = "system", Content = userContent }
        };

        var reply = await _api.Chat(msgs);
        if (reply is null)
        {
            result.Warnings.Add("AI 生成失败，使用默认世界观");
            entry.Title = "未命名世界书";
            entry.Description = description;
            result.Worldbook = entry;
            return result;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<WorldbookJson>(reply, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (parsed is not null)
            {
                entry.Title = parsed.Title;
                entry.Description = parsed.Description;
                entry.HasSprite = parsed.HasSprite;
                foreach (var c in parsed.Characters ?? [])
                {
                    entry.Characters.Add(new WorldbookCharacter
                    {
                        Name = c.Name,
                        Gender = c.Gender,
                        Personality = c.Personality,
                        Description = c.Description,
                        HasSprite = c.HasSprite,
                        SpritePath = c.SpritePath ?? ""
                    });
                }
            }
        }
        catch
        {
            entry.Title = "未命名世界书";
            entry.Description = description;
            result.Warnings.Add("AI 响应解析失败，已使用默认值");
        }

        if (generateCover && entry.HasSprite)
        {
            result.CoverGenerated = await GenerateCoverAsync(entry);
            result.CoverImagePath = result.CoverGenerated ? entry.CoverImagePath : "";
        }
        else if (generateCover && !entry.HasSprite)
        {
            result.CoverGenerated = await GenerateTextCoverAsync(entry);
            result.CoverImagePath = result.CoverGenerated ? entry.CoverImagePath : "";
        }

        result.Worldbook = entry;
        result.AiReply = reply;
        return result;
    }

    private async Task<bool> GenerateCoverAsync(WorldbookEntry entry)
    {
        try
        {
            var prompt = $"为这个Galgame世界书生成一张封面图片。世界观：{entry.Description}。角色：{string.Join("、", entry.Characters.Select(c => c.Name))}。风格：Galgame封面，立绘风格。";
            var msgs = new List<ChatMessage>
            {
                new() { Role = "user", Content = prompt }
            };
            var reply = await _api.Chat(msgs);
            if (string.IsNullOrWhiteSpace(reply)) return false;

            var imagePath = Path.Combine(App.RootDirectory, "assets", "worldbook_covers", $"{entry.Id}.png");
            Directory.CreateDirectory(Path.GetDirectoryName(imagePath)!);
            await File.WriteAllBytesAsync(imagePath, Convert.FromBase64String(reply));
            entry.CoverImagePath = imagePath;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> GenerateTextCoverAsync(WorldbookEntry entry)
    {
        try
        {
            var prompt = $"为这个文字冒险世界书生成一张封面图片。世界观：{entry.Description}。风格：文字冒险风格封面，包含标题 '{entry.Title}'。";
            var msgs = new List<ChatMessage>
            {
                new() { Role = "user", Content = prompt }
            };
            var reply = await _api.Chat(msgs);
            if (string.IsNullOrWhiteSpace(reply)) return false;

            var imagePath = Path.Combine(App.RootDirectory, "assets", "worldbook_covers", $"{entry.Id}.png");
            Directory.CreateDirectory(Path.GetDirectoryName(imagePath)!);
            await File.WriteAllBytesAsync(imagePath, Convert.FromBase64String(reply));
            entry.CoverImagePath = imagePath;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public sealed class WorldbookJson
    {
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public bool HasSprite { get; set; }
        public List<CharacterJson>? Characters { get; set; }
    }

    public sealed class CharacterJson
    {
        public string Name { get; set; } = "";
        public string Gender { get; set; } = "女";
        public string Personality { get; set; } = "";
        public string Description { get; set; } = "";
        public bool HasSprite { get; set; }
        public string? SpritePath { get; set; }
    }

    public async Task<string?> ChatAsync(List<ChatMessage> messages) => await _api.Chat(messages);
}