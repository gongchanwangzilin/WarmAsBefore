using System.Text.Json;
using WarmAsBefore.Models;

namespace WarmAsBefore.Services;

/// <summary>
/// Skills：可导入的「能力包」，一个目录 = 一个 skill。
///
/// 一个 skill 目录里可以有两样东西，都是可选的，但至少要有一样：
/// · <c>SKILL.md</c>——写给 AI 看的说明书（何时用、怎么做、注意事项）。
///   采用**渐进加载**：人设里只放名字与一句话用途，AI 需要时用 <c>read_skill</c> 把正文拉进上下文，
///   这样装十几个 skill 也不会把每轮提示词撑爆。
/// · <c>tool.json</c>——可执行入口（复用现有外部工具机制：python / java，stdin/stdout JSON-RPC）。
///   只有启用且运行时可用时才真正成为工具。
///
/// 手机端沙箱未信任时，可执行入口由 ToolManager 的沙箱策略拦截，说明书仍然可用。
/// </summary>
public sealed class SkillLibrary
{
    private const string StateKey = "skills_state";
    private const int MaxInstructionChars = 6000;

    private readonly StorageProvider _store;
    private readonly object _gate = new();
    private Dictionary<string, bool> _enabled = new(StringComparer.OrdinalIgnoreCase);
    private bool _loaded;

    public SkillLibrary(StorageProvider store) => _store = store;

    /// <summary>技能目录：{root}/skills/{技能名}/。</summary>
    public static string SkillsDir => Path.Combine(App.RootDirectory, "skills");

    public sealed record SkillInfo(
        string Name, string Title, string Description, string Dir,
        bool HasInstructions, bool HasExecutable, bool Enabled);

    private async Task EnsureLoadedAsync()
    {
        lock (_gate)
        {
            if (_loaded) return;
            _loaded = true;
        }
        try
        {
            var saved = await _store.Load<Dictionary<string, bool>>(StateKey);
            lock (_gate)
            {
                if (saved is not null) _enabled = new Dictionary<string, bool>(saved, StringComparer.OrdinalIgnoreCase);
            }
        }
        catch (Exception ex)
        {
            App.WriteLog("SkillLibrary.Load -> " + ex.Message);
        }
    }

    private Task PersistAsync()
    {
        Dictionary<string, bool> snapshot;
        lock (_gate) snapshot = new Dictionary<string, bool>(_enabled, StringComparer.OrdinalIgnoreCase);
        return _store.Save(StateKey, snapshot);
    }

    /// <summary>列出已安装的 skill（扫描目录，不依赖启用状态）。</summary>
    public async Task<List<SkillInfo>> ListAsync()
    {
        await EnsureLoadedAsync();
        var result = new List<SkillInfo>();
        try
        {
            if (!Directory.Exists(SkillsDir)) return result;
            foreach (var dir in Directory.GetDirectories(SkillsDir))
            {
                var name = Path.GetFileName(dir);
                var md = Path.Combine(dir, "SKILL.md");
                var tool = Path.Combine(dir, "tool.json");
                var hasMd = File.Exists(md);
                var hasTool = File.Exists(tool);
                if (!hasMd && !hasTool) continue;

                var (title, desc) = hasMd ? ParseFrontMatter(File.ReadAllText(md), name) : (name, "可执行技能");
                if (hasTool && string.IsNullOrWhiteSpace(desc))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(File.ReadAllText(tool));
                        if (doc.RootElement.TryGetProperty("description", out var d)) desc = d.GetString() ?? desc;
                    }
                    catch { /* 清单坏了不影响列出 */ }
                }
                bool enabled;
                lock (_gate) enabled = !_enabled.TryGetValue(name, out var e) || e;   // 默认启用
                result.Add(new SkillInfo(name, title, desc, dir, hasMd, hasTool, enabled));
            }
        }
        catch (Exception ex)
        {
            App.WriteLog("SkillLibrary.List -> " + ex.Message);
        }
        return result.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<List<SkillInfo>> EnabledAsync() => (await ListAsync()).Where(s => s.Enabled).ToList();

    public async Task SetEnabledAsync(string name, bool enabled)
    {
        await EnsureLoadedAsync();
        lock (_gate) _enabled[name] = enabled;
        await PersistAsync();
    }

    /// <summary>删除一个 skill（整目录移除）。</summary>
    public async Task<bool> RemoveAsync(string name)
    {
        try
        {
            var dir = Path.Combine(SkillsDir, name);
            if (!Directory.Exists(dir)) return false;
            Directory.Delete(dir, true);
            await EnsureLoadedAsync();
            lock (_gate) _enabled.Remove(name);
            await PersistAsync();
            return true;
        }
        catch (Exception ex)
        {
            App.WriteLog("SkillLibrary.Remove -> " + ex.Message);
            return false;
        }
    }

    /// <summary>取说明书正文（渐进加载：AI 主动来读，而不是每次对话都塞进去）。</summary>
    public string ReadInstructions(string name)
    {
        try
        {
            var path = Path.Combine(SkillsDir, name, "SKILL.md");
            if (!File.Exists(path)) return "";
            var text = File.ReadAllText(path);
            return text.Length > MaxInstructionChars ? text[..MaxInstructionChars] + "\n…（说明过长已截断）" : text;
        }
        catch (Exception ex)
        {
            App.WriteLog("SkillLibrary.ReadInstructions -> " + ex.Message);
            return "";
        }
    }

    /// <summary>人设里的一行提示：只列名字与用途（渐进加载的关键）。</summary>
    public async Task<string> HintTextAsync()
    {
        var list = await EnabledAsync();
        if (list.Count == 0) return "";
        var lines = list.Select(s => $"· {s.Name}：{s.Description}");
        return "你装了这些能力包（skill）：\n" + string.Join("\n", lines)
            + "\n需要用到某个能力时，先用 read_skill 读它的说明书，再按说明书操作；不确定有什么能力时可以调 list_skills。";
    }

    /// <summary>
    /// 导入：支持 zip 包，或直接指向一个 skill 目录（复制进技能目录）。
    /// zip 里可以是 {技能名}/SKILL.md，也可以是根目录直接放 SKILL.md（用文件名兜底命名）。
    /// </summary>
    public async Task<(bool ok, string message)> ImportAsync(string path)
    {
        try
        {
            Directory.CreateDirectory(SkillsDir);
            if (Directory.Exists(path))
            {
                var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, '/'));
                var dest = Path.Combine(SkillsDir, name);
                CopyDirectory(path, dest);
                return Validate(dest);
            }
            if (!File.Exists(path)) return (false, "文件不存在。");

            var fileName = Path.GetFileNameWithoutExtension(path);
            var temp = Path.Combine(Path.GetTempPath(), "wab_skill_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(temp);
            System.IO.Compression.ZipFile.ExtractToDirectory(path, temp, overwriteFiles: true);

            // 包里可能多套一层目录：向下找到真正含 SKILL.md / tool.json 的那一层
            var root = temp;
            while (!File.Exists(Path.Combine(root, "SKILL.md")) && !File.Exists(Path.Combine(root, "tool.json")))
            {
                var sub = Directory.GetDirectories(root);
                var files = Directory.GetFiles(root);
                if (sub.Length == 1 && files.Length == 0) { root = sub[0]; continue; }
                break;
            }
            if (!File.Exists(Path.Combine(root, "SKILL.md")) && !File.Exists(Path.Combine(root, "tool.json")))
                return (false, "包里没有找到 SKILL.md 或 tool.json，不像是 skill。");

            var skillName = Path.GetFileName(root) == Path.GetFileName(temp) ? fileName : Path.GetFileName(root);
            var target = Path.Combine(SkillsDir, skillName);
            if (Directory.Exists(target)) Directory.Delete(target, true);
            Directory.Move(root, target);
            try { Directory.Delete(temp, true); } catch { /* 临时目录残留无妨 */ }
            return Validate(target);
        }
        catch (Exception ex)
        {
            App.WriteLog("SkillLibrary.Import -> " + ex);
            return (false, "导入失败：" + ex.Message);
        }
    }

    private (bool ok, string message) Validate(string dir)
    {
        var name = Path.GetFileName(dir);
        var hasMd = File.Exists(Path.Combine(dir, "SKILL.md"));
        var hasTool = File.Exists(Path.Combine(dir, "tool.json"));
        if (!hasMd && !hasTool) return (false, "目录里没有 SKILL.md 或 tool.json。");
        var kind = hasMd && hasTool ? "说明书 + 可执行入口" : hasMd ? "仅说明书" : "仅可执行入口";
        return (true, $"已导入 skill「{name}」（{kind}）。" + (hasTool ? "可执行入口需运行时可用才会出现在工具列表里。" : ""));
    }

    /// <summary>SKILL.md 可选的 YAML 头（--- 之间的 name/title/description）：解析出来给人看。</summary>
    private static (string Title, string Description) ParseFrontMatter(string text, string fallbackName)
    {
        var title = fallbackName;
        var desc = "";
        try
        {
            var lines = text.Replace("\r\n", "\n").Split('\n');
            var inHeader = false;
            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (line == "---")
                {
                    if (!inHeader) { inHeader = true; continue; }
                    break;
                }
                if (!inHeader) continue;
                var idx = line.IndexOf(':');
                if (idx <= 0) continue;
                var key = line[..idx].Trim().ToLowerInvariant();
                var value = line[(idx + 1)..].Trim().Trim('"', '\'');
                if (key is "name" or "title") title = value;
                else if (key is "description" or "desc" or "when_to_use") desc = value;
            }
            if (desc.Length == 0)
            {
                // 没有头就取第一段非空正文当描述
                var body = lines.SkipWhile(l => l.Trim().Length == 0 || l.Trim() == "---").FirstOrDefault(l => l.Trim().Length > 0);
                desc = body?.TrimStart('#', ' ', '\t') ?? "";
            }
        }
        catch { /* 解析失败就用文件名兜底 */ }
        if (desc.Length > 120) desc = desc[..120] + "…";
        return (title, desc);
    }

    private static void CopyDirectory(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.GetFiles(from))
            File.Copy(file, Path.Combine(to, Path.GetFileName(file)), overwrite: true);
        foreach (var dir in Directory.GetDirectories(from))
            CopyDirectory(dir, Path.Combine(to, Path.GetFileName(dir)));
    }
}
