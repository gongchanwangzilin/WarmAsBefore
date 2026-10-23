using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Jint;
using Jint.Runtime;
using WarmAsBefore.Models;
using WarmAsBefore.Modules.Tools;

namespace WarmAsBefore.Services;

/// <summary>一个性别的定义：不只是名字，还带称呼、周期与一段可执行的自定义逻辑。</summary>
public sealed class GenderDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "";
    /// <summary>一句话说明（会进提示词，也会显示在设置里）。</summary>
    public string Description { get; set; } = "";
    /// <summary>第三人称代词：她 / 他 / TA / 自定义。</summary>
    public string Pronoun { get; set; } = "TA";
    /// <summary>这个性别默认的周期规则；null = 无周期。</summary>
    public CycleRule? Cycle { get; set; }
    /// <summary>自定义逻辑（可选）：js / python / java，读 input JSON，写 result 文本。</summary>
    public GenderScript? Script { get; set; }
    /// <summary>内置项（出厂自带，可以改但不能删干净）。</summary>
    public bool Builtin { get; set; }

    public string CycleLabel => Cycle is { Enabled: true }
        ? $"{Cycle.CycleLength} 天 / 经期 {Cycle.PeriodLength} 天"
        : "无周期";

    public string ScriptLabel => Script is null || string.IsNullOrWhiteSpace(Script.Code)
        ? "无脚本"
        : Script.Language switch
        {
            "python" => "Python 逻辑",
            "java" => "Java 逻辑",
            _ => "JavaScript 逻辑"
        };
}

public sealed class GenderScript
{
    /// <summary>js / python / java。</summary>
    public string Language { get; set; } = "js";
    public string Code { get; set; } = "";
}

/// <summary>
/// 自定义性别库：素材包里的 JSON 可直接导入，也可以在设置里手写。
///
/// 脚本契约（三种语言一致）：
///   · 从 stdin 读入一段 JSON：{ name, gender, character, affection, trust, userGender, userAddress, now }
///   · 把要注入提示词的那句话写到 stdout（JS 写 result 变量）。
/// 逻辑失败绝不拖垮对话：这次退回描述文本，下一次照常再试。
/// JavaScript 走嵌入式 Jint，不依赖 WebView / Chrome 内核。
/// </summary>
public sealed class GenderLibrary
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true
    };

    private readonly StorageProvider _store;
    private readonly RuntimeManager? _runtimes;
    private readonly object _gate = new();
    private List<GenderDefinition> _items = new();
    private Task? _loading;
    private readonly Dictionary<string, string> _scripted = new(StringComparer.OrdinalIgnoreCase);

    public GenderLibrary(StorageProvider store, RuntimeManager? runtimes = null)
    {
        _store = store;
        _runtimes = runtimes;
    }

    public static string Dir => Path.Combine(App.RootDirectory, "genders");
    private static string FilePath => Path.Combine(Dir, "genders.json");

    /// <summary>出厂性别：描述很短，够模型理解即可；周期只给「女」默认带。</summary>
    public static List<GenderDefinition> Defaults() => new()
    {
        new GenderDefinition
        {
            Id = "female", Name = "女", Pronoun = "她", Builtin = true,
            Description = "女性。生理周期可按设置里的规则计算，语气与称呼按角色人设走，不要刻板化。",
            Cycle = new CycleRule { Enabled = true, CycleLength = 28, PeriodLength = 5 }
        },
        new GenderDefinition
        {
            Id = "male", Name = "男", Pronoun = "他", Builtin = true,
            Description = "男性。没有默认生理周期；不要用性别刻板印象替代角色自己的性格。",
            Cycle = new CycleRule { Enabled = false }
        },
        new GenderDefinition
        {
            Id = "other", Name = "其他", Pronoun = "TA", Builtin = true,
            Description = "非二元或其他性别。按角色自己的认同来称呼，不确定时用 TA，不要替角色解释。",
            Cycle = new CycleRule { Enabled = false }
        },
        new GenderDefinition
        {
            Id = "secret", Name = "保密", Pronoun = "TA", Builtin = true,
            Description = "性别不公开。不要猜测、不要追问，用 TA 或角色习惯的称呼。",
            Cycle = new CycleRule { Enabled = false }
        }
    };

    private Task EnsureLoadedAsync()
    {
        lock (_gate) return _loading ??= LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                lock (_gate) _items = Defaults();
                await PersistAsync();
                return;
            }
            var list = JsonSerializer.Deserialize<List<GenderDefinition>>(await File.ReadAllTextAsync(FilePath), Json);
            if (list is null || list.Count == 0)
            {
                var defaults = Defaults();
                lock (_gate) _items = defaults;
                await PersistAsync();
                return;
            }
            // 出厂项少了就补回来（旧文件可能只有自定义项）
            foreach (var d in Defaults())
                if (!list.Any(x => string.Equals(x.Name, d.Name, StringComparison.Ordinal)))
                    list.Add(d);
            lock (_gate) _items = list;
        }
        catch (Exception ex)
        {
            App.WriteLog("GenderLibrary.Load -> " + ex.Message);
            lock (_gate) _items = Defaults();
        }
    }

    private async Task PersistAsync()
    {
        List<GenderDefinition> snapshot;
        lock (_gate) snapshot = _items.ToList();
        await Task.Run(() =>
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(snapshot, Json));
        });
    }

    /// <summary>所有性别（出厂在前，自定义按名字排序）。</summary>
    public async Task<List<GenderDefinition>> AllAsync()
    {
        await EnsureLoadedAsync();
        lock (_gate) return _items
            .OrderByDescending(g => g.Builtin)
            .ThenBy(g => g.Name, StringComparer.CurrentCulture)
            .ToList();
    }

    /// <summary>名字列表（设置里的下拉直接用它）。</summary>
    public async Task<List<string>> NamesAsync()
        => (await AllAsync()).Select(g => g.Name).Where(n => !string.IsNullOrWhiteSpace(n)).ToList();

    /// <summary>同步取名称列表：设置页构造时不想再等一次 IO。</summary>
    public List<string> Names
    {
        get
        {
            lock (_gate)
                return (_items.Count == 0 ? Defaults() : _items)
                    .Select(g => g.Name).Where(n => !string.IsNullOrWhiteSpace(n))
                    .Distinct(StringComparer.Ordinal).ToList();
        }
    }

    public async Task<GenderDefinition?> FindAsync(string? name)
    {
        await EnsureLoadedAsync();
        var key = (name ?? "").Trim();
        if (key.Length == 0) return null;
        lock (_gate) return _items.FirstOrDefault(g =>
            string.Equals(g.Name, key, StringComparison.OrdinalIgnoreCase)
            || string.Equals(g.Id, key, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>给提示词用的描述（同步读缓存，脚本结果在 RefreshScriptsAsync 里预热）。</summary>
    public string PromptFor(string? name)
    {
        var key = (name ?? "").Trim();
        if (key.Length == 0) return "";
        GenderDefinition? g;
        lock (_gate) g = _items.FirstOrDefault(x =>
            string.Equals(x.Name, key, StringComparison.OrdinalIgnoreCase)
            || string.Equals(x.Id, key, StringComparison.OrdinalIgnoreCase));
        if (g is null) return $"性别：{key}。按角色自己的认同来称呼，不要替角色解释或猜测。";
        var line = $"性别：{g.Name}。{g.Description}".Trim();
        if (!string.IsNullOrWhiteSpace(g.Pronoun) && g.Pronoun != "TA")
            line += $" 第三人称用「{g.Pronoun}」。";
        lock (_gate)
            if (_scripted.TryGetValue(g.Name, out var extra) && !string.IsNullOrWhiteSpace(extra))
                line += " " + extra;
        return line;
    }

    /// <summary>用户性别的提示词行（空 = 不提）。</summary>
    public string UserPromptFor(string? userGender)
    {
        var key = (userGender ?? "").Trim();
        if (key.Length == 0) return "";
        GenderDefinition? g;
        lock (_gate) g = _items.FirstOrDefault(x => string.Equals(x.Name, key, StringComparison.OrdinalIgnoreCase));
        var desc = g?.Description ?? "";
        return $"对方（用户）的性别是：{key}。{desc} 用对方习惯的方式称呼与相处，不要用性别刻板印象代替对具体的人的理解。";
    }

    /// <summary>新增或更新一个自定义性别。</summary>
    public async Task<(bool Ok, string Message)> SaveAsync(GenderDefinition def)
    {
        await EnsureLoadedAsync();
        var name = (def.Name ?? "").Trim();
        if (name.Length is 0 or > 24) return (false, "名字要在 1~24 个字之间。");
        if (def.Script is { Code.Length: > 20000 }) return (false, "脚本太长了（上限 20000 字）。");
        def.Name = name;
        lock (_gate)
        {
            var idx = _items.FindIndex(g => string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0)
            {
                def.Id = _items[idx].Id;
                def.Builtin = _items[idx].Builtin;
                _items[idx] = def;
            }
            else _items.Add(def);
        }
        await PersistAsync();
        await RefreshScriptsAsync();
        return (true, $"已保存性别「{name}」。");
    }

    public async Task<bool> DeleteAsync(string name)
    {
        await EnsureLoadedAsync();
        bool removed;
        lock (_gate)
        {
            var item = _items.FirstOrDefault(g => string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase));
            if (item is null || item.Builtin) return false;
            removed = _items.Remove(item);
        }
        if (removed) await PersistAsync();
        return removed;
    }

    /// <summary>
    /// 从 JSON 导入：支持数组、{genders:[...]}、或单个对象；也接受素材包常见的
    /// {name, description, cycleLength, periodLength} 扁平行。
    /// </summary>
    public async Task<(bool Ok, string Message)> ImportJsonAsync(string path)
    {
        try
        {
            var raw = await File.ReadAllTextAsync(path);
            var parsed = ParseDefinitions(raw);
            if (parsed.Count == 0) return (false, "JSON 里没有找到性别定义。");
            await EnsureLoadedAsync();
            var added = 0;
            lock (_gate)
            {
                foreach (var g in parsed)
                {
                    if (string.IsNullOrWhiteSpace(g.Name)) continue;
                    var idx = _items.FindIndex(x => string.Equals(x.Name, g.Name, StringComparison.OrdinalIgnoreCase));
                    if (idx >= 0) _items[idx] = g;
                    else { _items.Add(g); added++; }
                }
            }
            await PersistAsync();
            await RefreshScriptsAsync();
            return (true, $"导入 {parsed.Count} 个性别（新增 {added} 个）。脚本逻辑会在下次进入游戏时生效。");
        }
        catch (Exception ex)
        {
            App.WriteLog("GenderLibrary.Import -> " + ex);
            return (false, "导入失败：" + ex.Message);
        }
    }

    private static List<GenderDefinition> ParseDefinitions(string raw)
    {
        var result = new List<GenderDefinition>();
        using var doc = JsonDocument.Parse(raw);
        var root = doc.RootElement;
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("genders", out var arr))
            root = arr;
        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in root.EnumerateArray()) Add(item);
        }
        else if (root.ValueKind == JsonValueKind.Object) Add(root);
        return result;

        void Add(JsonElement e)
        {
            if (e.ValueKind != JsonValueKind.Object) return;
            var name = Str(e, "name") ?? Str(e, "gender") ?? Str(e, "title");
            if (string.IsNullOrWhiteSpace(name)) return;
            var g = new GenderDefinition
            {
                Id = Str(e, "id") ?? Guid.NewGuid().ToString("N")[..8],
                Name = name.Trim(),
                Description = Str(e, "description") ?? Str(e, "desc") ?? "",
                Pronoun = Str(e, "pronoun") ?? Str(e, "thirdPerson") ?? "TA",
                Builtin = false
            };
            if (e.TryGetProperty("cycle", out var cyc) && cyc.ValueKind == JsonValueKind.Object)
            {
                g.Cycle = new CycleRule
                {
                    Enabled = Bool(cyc, "enabled") ?? false,
                    CycleLength = Int(cyc, "cycleLength") ?? Int(cyc, "length") ?? 28,
                    PeriodLength = Int(cyc, "periodLength") ?? Int(cyc, "period") ?? 5
                };
            }
            else
            {
                var len = Int(e, "cycleLength");
                if (len is > 0)
                    g.Cycle = new CycleRule { Enabled = true, CycleLength = len.Value, PeriodLength = Int(e, "periodLength") ?? 5 };
            }
            if (e.TryGetProperty("script", out var sc) && sc.ValueKind == JsonValueKind.Object)
            {
                g.Script = new GenderScript
                {
                    Language = (Str(sc, "language") ?? Str(sc, "lang") ?? "js").ToLowerInvariant(),
                    Code = Str(sc, "code") ?? ""
                };
            }
            else
            {
                var code = Str(e, "logic") ?? Str(e, "scriptCode");
                if (!string.IsNullOrWhiteSpace(code))
                    g.Script = new GenderScript { Language = (Str(e, "language") ?? "js").ToLowerInvariant(), Code = code };
            }
            result.Add(g);
        }

        static string? Str(JsonElement e, string name)
            => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        static bool? Bool(JsonElement e, string name)
            => e.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : null;
        static int? Int(JsonElement e, string name)
            => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : null;
    }

    /// <summary>
    /// 预热脚本：用一份样例上下文跑一遍，把结果缓存给同步的人设拼装使用。
    /// 失败只写日志，不抛。
    /// </summary>
    public async Task RefreshScriptsAsync()
    {
        await EnsureLoadedAsync();
        List<GenderDefinition> snapshot;
        lock (_gate) snapshot = _items.ToList();
        foreach (var g in snapshot)
        {
            if (g.Script is null || string.IsNullOrWhiteSpace(g.Script.Code)) continue;
            try
            {
                var text = await RunScriptAsync(g.Script, new
                {
                    name = g.Name,
                    gender = g.Name,
                    character = "角色",
                    affection = 50,
                    trust = 50,
                    userGender = "",
                    userAddress = "你",
                    now = DateTime.Now.ToString("yyyy-MM-dd HH:mm")
                });
                lock (_gate) _scripted[g.Name] = (text ?? "").Trim();
            }
            catch (Exception ex)
            {
                App.WriteLog($"GenderLibrary.Script({g.Name}) -> {ex.Message}");
            }
        }
    }

    /// <summary>运行一次性别脚本；返回 stdout / result。失败返回 null。</summary>
    public async Task<string?> RunScriptAsync(GenderScript script, object context)
    {
        var input = JsonSerializer.Serialize(context);
        return script.Language?.ToLowerInvariant() switch
        {
            "python" => await RunProcessScriptAsync("python", script.Code, input),
            "java" => await RunProcessScriptAsync("java", script.Code, input),
            _ => RunJs(script.Code, input)
        };
    }

    /// <summary>JS：脚本里可直接用 input（JSON 字符串），把要注入的文字写进 result。</summary>
    public static string? RunJs(string code, string inputJson)
    {
        var engine = new Engine(options =>
        {
            options.TimeoutInterval(TimeSpan.FromMilliseconds(250));
            options.MaxStatements(4000);
            options.LimitMemory(8_000_000);
        });
        engine.SetValue("input", inputJson);
        engine.SetValue("result", "");
        engine.Execute(code);
        var value = engine.GetValue("result");
        return value.IsUndefined() || value.IsNull() ? null : value.ToString();
    }

    private async Task<string?> RunProcessScriptAsync(string language, string code, string input)
    {
        if (_runtimes is null) return null;
        var resolved = await _runtimes.ResolveRuntimeAsync(
            language == "python" ? ToolLanguage.Python : ToolLanguage.Java);
        if (resolved is null) return null;
        var ext = language == "python" ? ".py" : ".java";
        Directory.CreateDirectory(Dir);
        var path = Path.Combine(Dir, $"script_{Guid.NewGuid():N}{ext}");
        await File.WriteAllTextAsync(path, code);
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = resolved.Value.Exe,
                Arguments = language == "python"
                    ? $"{resolved.Value.Args} \"{path}\"".Trim()
                    : $"\"{path}\"",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            };
            using var p = Process.Start(psi);
            if (p is null) return null;
            await p.StandardInput.WriteAsync(input);
            p.StandardInput.Close();
            var output = await p.StandardOutput.ReadToEndAsync();
            var error = await p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(4000))
            {
                try { p.Kill(true); } catch { }
                return null;
            }
            if (p.ExitCode != 0)
            {
                App.WriteLog($"GenderLibrary.{language} exit {p.ExitCode}: {error}");
                return null;
            }
            return output.Trim();
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }
}
