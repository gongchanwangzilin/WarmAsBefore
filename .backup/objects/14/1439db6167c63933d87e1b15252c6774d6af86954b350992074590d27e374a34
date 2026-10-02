using System.Text.Json;
using WarmAsBefore.Models;

namespace WarmAsBefore.Services;

/// <summary>
/// 灵枢 AI 导入服务：将导出的记忆 JSON 注入到角色的世界书中
/// </summary>
public sealed class LingshuImporter
{
    private readonly StorageProvider _store;
    
    public LingshuImporter(StorageProvider store)
    {
        _store = store;
    }
    
    /// <summary>
    /// 导入灵枢记忆到指定角色
    /// </summary>
    /// <param name="charId">目标角色 ID</param>
    /// <param name="jsonPath">JSON 文件路径</param>
    /// <param name="mode">导入模式：append=追加, replace=替换</param>
    public async Task<(bool ok, string message)> ImportToCharacterAsync(
        string charId, 
        string jsonPath,
        string mode = "append")
    {
        try
        {
            if (!File.Exists(jsonPath))
                return (false, "文件不存在");
            
            var json = await File.ReadAllTextAsync(jsonPath);
            var archive = JsonSerializer.Deserialize<LingshuArchive>(json);
            
            if (archive == null || archive.Memories.Count == 0)
                return (false, "未找到有效的记忆数据");
            
            // 加载角色现有世界书
            var worldbook = await LoadWorldbookAsync(charId);
            
            if (mode == "replace")
            {
                // 清空现有记忆
                worldbook.Characters.Clear();
            }
            
            // 导入每条记忆作为角色
            var imported = 0;
            foreach (var memory in archive.Memories)
            {
                if (string.IsNullOrWhiteSpace(memory.Content))
                    continue;
                
                var character = new WorldbookCharacter
                {
                    Name = string.IsNullOrWhiteSpace(memory.Role) ? "记忆片段" : memory.Role,
                    Description = memory.Content,
                    Gender = "未知"
                };
                
                worldbook.Characters.Add(character);
                imported++;
            }
            
            // 保存世界书
            await SaveWorldbookAsync(charId, worldbook);
            
            return (true, $"成功导入 {imported} 条记忆");
        }
        catch (Exception ex)
        {
            return (false, $"导入失败: {ex.Message}");
        }
    }
    
    /// <summary>
    /// 验证文件格式
    /// </summary>
    public async Task<bool> ValidateAsync(string jsonPath)
    {
        try
        {
            if (!File.Exists(jsonPath))
                return false;
            
            var json = await File.ReadAllTextAsync(jsonPath);
            var archive = JsonSerializer.Deserialize<LingshuArchive>(json);
            
            return archive != null && archive.Memories.Count > 0;
        }
        catch
        {
            return false;
        }
    }
    
    private async Task<WorldbookEntry> LoadWorldbookAsync(string charId)
    {
        var worldbooks = await _store.Load<List<WorldbookEntry>>("worldbooks");
        var existing = worldbooks?.FirstOrDefault(w => w.Characters.Any(c => c.Name.Contains(charId))) 
            ?? new WorldbookEntry { Title = charId };
        return existing;
    }
    
    private async Task SaveWorldbookAsync(string charId, WorldbookEntry worldbook)
    {
        var worldbooks = await _store.Load<List<WorldbookEntry>>("worldbooks") ?? new();
        var idx = worldbooks.FindIndex(w => w.Characters.Any(c => c.Name.Contains(charId)));
        
        if (idx >= 0)
            worldbooks[idx] = worldbook;
        else
            worldbooks.Add(worldbook);
        
        await _store.Save("worldbooks", worldbooks);
    }
}