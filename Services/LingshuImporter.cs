using System.Text.Json;
using WarmAsBefore.Models;

namespace WarmAsBefore.Services;

/// <summary>
/// 灵枢 AI 导入服务：解析灵枢导出的 JSON 文件，导入角色设定和记忆
/// </summary>
public sealed class LingshuImporter
{
    private readonly CharacterLibrary _charLibrary;
    private readonly GameEngine _engine;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public LingshuImporter(CharacterLibrary charLibrary, GameEngine engine)
    {
        _charLibrary = charLibrary;
        _engine = engine;
    }

    /// <summary>
    /// 导入灵枢 JSON 文件
    /// </summary>
    /// <param name="filePath">JSON 文件路径</param>
    /// <param name="mergeMode">导入模式：overwrite=覆盖，append=追加，create_new=创建新角色</param>
    /// <returns>导入结果</returns>
    public async Task<ImportResult> ImportAsync(string filePath, ImportMode mergeMode = ImportMode.CreateNew)
    {
        var result = new ImportResult { Success = false };

        try
        {
            if (!File.Exists(filePath))
            {
                result.ErrorMessage = "文件不存在";
                return result;
            }

            var json = await File.ReadAllTextAsync(filePath);
            var importData = JsonSerializer.Deserialize<LingshuCharacterImport>(json, JsonOptions);

            if (importData is null)
            {
                result.ErrorMessage = "JSON 格式无效";
                return result;
            }

            // 验证必要字段
            if (string.IsNullOrWhiteSpace(importData.Name))
            {
                result.ErrorMessage = "缺少角色名称";
                return result;
            }

            // 查找匹配的角色
            CharacterData? targetChar = null;
            if (mergeMode == ImportMode.Overwrite && _engine.ActiveCharacter is not null)
            {
                targetChar = _engine.ActiveCharacter;
            }
            else if (mergeMode == ImportMode.Append)
            {
                // 尝试查找同名角色
                targetChar = _engine.Roster.Values
                    .FirstOrDefault(c => c.Profile.Name.Equals(importData.Name, StringComparison.OrdinalIgnoreCase));
            }

            if (targetChar is null && mergeMode != ImportMode.CreateNew)
            {
                result.ErrorMessage = $"未找到角色「{importData.Name}」，请切换到该角色后重试，或选择「创建新角色」模式";
                return result;
            }

            // 创建或更新角色
            var profile = targetChar?.Profile ?? CreateProfileFromLingshu(importData);
            var charData = targetChar ?? new CharacterData { Profile = profile };

            // 更新角色资料
            charData = charData with
            {
                Profile = profile with
                {
                    Name = importData.Name,
                    Gender = importData.Gender,
                    Personality = importData.Personality,
                    Description = importData.Description,
                    Greeting = importData.Greeting,
                    UserAddress = importData.UserAddress
                }
            };

            // 保存角色
            var saved = await _charLibrary.AddAsync(charData);
            if (!saved)
            {
                result.ErrorMessage = "保存角色失败";
                return result;
            }

            // 导入记忆
            if (importData.Memories.Any())
            {
                await SaveMemoriesAsync(charData.Profile.Id, importData.Memories);
                result.ImportedMemories = importData.Memories.Count;
            }

            // 导入对话历史
            if (importData.Dialogues.Any())
            {
                await ImportDialoguesAsync(charData.Profile.Id, importData.Dialogues);
                result.ImportedDialogues = importData.Dialogues.Count;
            }

            result.Success = true;
            result.CharacterName = importData.Name;
            result.ImportMode = mergeMode;

            App.WriteLog($"LingshuImporter: 成功导入角色「{importData.Name}」，记忆 {result.ImportedMemories} 条，对话 {result.ImportedDialogues} 条");
        }
        catch (Exception ex)
        {
            App.WriteLog($"LingshuImporter.ImportAsync -> {ex}");
            result.ErrorMessage = $"导入失败：{ex.Message}";
        }

        return result;
    }

    /// <summary>
    /// 从灵枢数据创建角色资料
    /// </summary>
    private static CharacterProfile CreateProfileFromLingshu(LingshuCharacterImport data)
    {
        return new CharacterProfile
        {
            Id = Guid.NewGuid().ToString("N")[..10],
            Name = data.Name.Trim(),
            Gender = data.Gender,
            Personality = data.Personality.Trim(),
            Description = data.Description.Trim(),
            Greeting = data.Greeting.Trim(),
            UserAddress = data.UserAddress.Trim()
        };
    }

    /// <summary>
    /// 保存记忆到文件
    /// </summary>
    private async Task SaveMemoriesAsync(string characterId, List<LingshuMemory> memories)
    {
        try
        {
            var store = new StorageProvider();
            await store.Save($"memories_{characterId}", memories);
        }
        catch (Exception ex)
        {
            App.WriteLog($"LingshuImporter.SaveMemories -> {ex}");
        }
    }

    /// <summary>
    /// 导入对话历史
    /// </summary>
    private async Task ImportDialoguesAsync(string characterId, List<LingshuDialogue> dialogues)
    {
        try
        {
            var messages = dialogues.Select(d => new ChatMessage
            {
                Role = d.Role.ToLower() == "user" ? "user" : "assistant",
                Content = d.Content,
                Stamp = d.Timestamp
            }).ToList();

            var session = new ChatSession
            {
                CharacterId = characterId,
                Messages = messages,
                Created = dialogues.OrderBy(d => d.Timestamp).FirstOrDefault()?.Timestamp ?? DateTime.UtcNow
            };

            var store = new StorageProvider();
            await store.Save($"chat_{characterId}", session);
        }
        catch (Exception ex)
        {
            App.WriteLog($"LingshuImporter.ImportDialogues -> {ex}");
        }
    }

    /// <summary>
    /// 验证 JSON 文件格式（不导入，仅检查）
    /// </summary>
    public async Task<ValidationResult> ValidateAsync(string filePath)
    {
        var result = new ValidationResult { Valid = false };

        try
        {
            if (!File.Exists(filePath))
            {
                result.ErrorMessage = "文件不存在";
                return result;
            }

            var json = await File.ReadAllTextAsync(filePath);
            var data = JsonSerializer.Deserialize<LingshuCharacterImport>(json, JsonOptions);

            if (data is null)
            {
                result.ErrorMessage = "JSON 格式无效";
                return result;
            }

            result.Valid = !string.IsNullOrWhiteSpace(data.Name);
            result.CharacterName = data.Name;
            result.HasMemories = data.Memories.Any();
            result.HasDialogues = data.Dialogues.Any();
            result.MemoryCount = data.Memories.Count;
            result.DialogueCount = data.Dialogues.Count;

            if (!result.Valid)
                result.ErrorMessage = "缺少必要字段：name";
        }
        catch (Exception ex)
        {
            result.ErrorMessage = $"验证失败：{ex.Message}";
        }

        return result;
    }

    public enum ImportMode
    {
        CreateNew,  // 创建新角色
        Overwrite,  // 覆盖当前角色
        Append      // 追加到同名角色
    }

    public sealed class ImportResult
    {
        public bool Success { get; set; }
        public string? CharacterName { get; set; }
        public string? ErrorMessage { get; set; }
        public ImportMode ImportMode { get; set; }
        public int ImportedMemories { get; set; }
        public int ImportedDialogues { get; set; }
    }

    public sealed class ValidationResult
    {
        public bool Valid { get; set; }
        public string? CharacterName { get; set; }
        public string? ErrorMessage { get; set; }
        public bool HasMemories { get; set; }
        public bool HasDialogues { get; set; }
        public int MemoryCount { get; set; }
        public int DialogueCount { get; set; }
    }
}
