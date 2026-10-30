using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace WarmAsBefore.Modules.MinecraftBridge;

/// <summary>插件设置：端口、是否允许局域网、是否允许世界动作等。值都来自插件的 settings.json（字符串）。</summary>
public sealed record MaidBridgeSettings
{
    public bool Enabled { get; init; } = true;
    public int Port { get; init; } = MaidBridgeService.DefaultPort;
    public bool Lan { get; init; }
    public bool AutoCreateCharacters { get; init; } = true;
    public bool AllowWorldActions { get; init; } = true;
    public bool AllowGestures { get; init; } = true;

    /// <summary>能不能从主人的客户端取一帧游戏画面给识图模型看。关掉后 maid_see 只剩文字。</summary>
    public bool AllowPhoto { get; init; } = true;

    /// <summary>游戏里绑了角色的女仆能不能借 WAB 的嗓子说话、借 WAB 的耳朵听。关掉后模组自己处理语音。</summary>
    public bool AllowVoice { get; init; } = true;
    public bool ChatAffection { get; init; } = true;
    public int AutonomyPulseMinutes { get; init; }
    public int BackupMinutes { get; init; } = 10;

    /// <summary>默认关：桥不许替用户切存档、新建存档、换界面上的当前角色。打开才恢复旧行为。</summary>
    public bool AutoSwitchSave { get; init; }

    /// <summary>哪些情况让她主动想一次（death/hurt/interaction/weather/time/respawn/owner_sleep/maid_chat）。</summary>
    public IReadOnlyList<string> ThinkScenes { get; init; } = Array.Empty<string>();

    /// <summary>陪睡时那句「爱语」的总开关：关掉后她照样过去睡下，只是这一晚不说话。</summary>
    public bool LoveWords { get; init; } = true;

    /// <summary>同一条触发最短间隔（秒）：多个女仆互相点着时不至于把 AI 调用打爆。</summary>
    public int ThinkCooldownSeconds { get; init; } = 90;

    /// <summary>要不要重开监听、重握手：只有端口和网卡模式变了才算。</summary>
    public string ListenerSignature => $"{Enabled}|{Port}|{Lan}";

    /// <summary>行为开关：变了只需把新配置换进内存，现有会话不该跟着抖一下。</summary>
    public string BehaviorSignature =>
        $"{AutoCreateCharacters}|{AllowWorldActions}|{AllowGestures}|{AllowPhoto}|{AllowVoice}|{ChatAffection}|" +
        $"{AutonomyPulseMinutes}|{BackupMinutes}|{AutoSwitchSave}|{LoveWords}|{ThinkCooldownSeconds}|" +
        string.Join(",", ThinkScenes);

    public static MaidBridgeSettings From(IReadOnlyDictionary<string, string> values)
    {
        bool Bool(string key, bool fallback)
        {
            if (!values.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw)) return fallback;
            return raw.Trim().ToLowerInvariant() switch
            {
                "true" or "1" or "on" or "yes" or "开" => true,
                "false" or "0" or "off" or "no" or "关" => false,
                _ => fallback
            };
        }

        int Int(string key, int fallback)
        {
            if (!values.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw)) return fallback;
            return int.TryParse(raw.Trim(), out var n) ? n : fallback;
        }

        // 逗号/空格分隔的场景清单：native 表单里的多选只能这样表达（Picker 是单选）
        string[] List(string key)
        {
            if (!values.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw)) return Array.Empty<string>();
            return raw.Split(new[] { ',', '，', '、', ';', '；', ' ', '\t', '\r', '\n' },
                    StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim().ToLowerInvariant())
                .Where(s => s.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }

        var port = Int("port", MaidBridgeService.DefaultPort);
        if (port is < 0 or > 65535) port = MaidBridgeService.DefaultPort;
        return new MaidBridgeSettings
        {
            Enabled = Bool("enabled", true),
            Port = port,
            Lan = Bool("lan", false),
            AutoCreateCharacters = Bool("auto_create_characters", true),
            AllowWorldActions = Bool("allow_world_actions", true),
            AllowGestures = Bool("allow_gestures", true),
            AllowPhoto = Bool("allow_photo", true),
            AllowVoice = Bool("allow_voice", true),
            ChatAffection = Bool("chat_affection", true),
            AutonomyPulseMinutes = Math.Clamp(Int("autonomy_pulse_minutes", 0), 0, 24 * 60),
            BackupMinutes = Math.Clamp(Int("backup_minutes", 10), 0, 24 * 60),
            AutoSwitchSave = Bool("auto_switch_save", false),
            LoveWords = Bool("love_words", true),
            ThinkScenes = List("think_scenes"),
            ThinkCooldownSeconds = Math.Clamp(Int("think_cooldown_seconds", 90), 5, 24 * 60 * 60)
        };
    }
}

/// <summary>一份 Minecraft 世界与其中女仆的快照；只保留展示与提示词需要的字段。</summary>
public sealed class MaidBridgeWorldSnapshot
{
    public string World { get; set; } = "";
    public string Instance { get; set; } = "";
    public string Player { get; set; } = "";
    public string Dimension { get; set; } = "";
    public string Weather { get; set; } = "";
    public string GameTime { get; set; } = "";
    public bool OwnerOnline { get; set; } = true;
    public DateTime ReceivedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class MaidSnapshot
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Model { get; set; } = "";
    public double Health { get; set; } = 20;
    public double MaxHealth { get; set; } = 20;
    public double Food { get; set; } = 20;
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    public string Dimension { get; set; } = "";
    public string Task { get; set; } = "";
    public string Activity { get; set; } = "";
    public string Owner { get; set; } = "";
    public double DistanceToOwner { get; set; } = -1;
    public bool Bound { get; set; }
    public MaidLoveLoathe? LoveLoathe { get; set; }

    public static MaidSnapshot From(JsonObject o)
    {
        var pos = o["pos"] as JsonObject;
        var healthNode = o["health"];
        var maxNode = o["maxHealth"];
        var health = BridgeJson.Num(o, "health", 20);
        var maxHealth = BridgeJson.Num(o, "maxHealth", 20);
        if (healthNode is JsonObject healthObj)
        {
            health = BridgeJson.Num(healthObj, "current", health);
            maxHealth = BridgeJson.Num(healthObj, "max", maxHealth);
        }

        double x = pos is not null ? BridgeJson.Num(pos, "x") : BridgeJson.Num(o, "x");
        double y = pos is not null ? BridgeJson.Num(pos, "y") : BridgeJson.Num(o, "y");
        double z = pos is not null ? BridgeJson.Num(pos, "z") : BridgeJson.Num(o, "z");
        if (o["pos"] is JsonArray arr && arr.Count >= 3)
        {
            x = BridgeJson.Num(arr[0]);
            y = BridgeJson.Num(arr[1]);
            z = BridgeJson.Num(arr[2]);
        }
        _ = maxNode;

        return new MaidSnapshot
        {
            Id = BridgeJson.Str(o, "id"),
            Name = BridgeJson.Str(o, "name"),
            Model = BridgeJson.Str(o, "model"),
            Health = health,
            MaxHealth = maxHealth <= 0 ? 20 : maxHealth,
            Food = BridgeJson.Num(o, "food", 20),
            X = x,
            Y = y,
            Z = z,
            Dimension = BridgeJson.Str(o, "dimension"),
            Task = BridgeJson.Str(o, "task"),
            Activity = BridgeJson.Str(o, "activity"),
            Owner = BridgeJson.Str(o, "owner"),
            DistanceToOwner = BridgeJson.Num(o, "distanceToOwner", -1),
            Bound = BridgeJson.Bool(o, "bound"),
            LoveLoathe = o["loveLoathe"] is JsonObject ll ? MaidLoveLoathe.From(ll) : null
        };
    }
}

/// <summary>车万女仆：爱憎分明的只读数值。WAB 不写回，只用来把话说得更像眼前这个人。</summary>
public sealed class MaidLoveLoathe
{
    public double Trust { get; set; }
    public double Fear { get; set; }
    public double Hunger { get; set; }
    public string Tendency { get; set; } = "";

    public static MaidLoveLoathe From(JsonObject o) => new()
    {
        Trust = BridgeJson.Num(o, "trust"),
        Fear = BridgeJson.Num(o, "fear"),
        Hunger = BridgeJson.Num(o, "hunger"),
        Tendency = BridgeJson.Str(o, "tendency")
    };
}

/// <summary>世界 → 存档 → 女仆绑定。文件存在 minecraft_bridge/bindings.json，随云存档一起走。</summary>
public sealed class MaidBridgeBindingStore
{
    [JsonPropertyName("worlds")]
    public Dictionary<string, MaidBridgeWorldBinding> Worlds { get; set; } = new(StringComparer.Ordinal);
}

public sealed class MaidBridgeWorldBinding
{
    [JsonPropertyName("saveId")]
    public string SaveId { get; set; } = "";

    [JsonPropertyName("maids")]
    public Dictionary<string, MaidBridgeMaidBinding> Maids { get; set; } = new(StringComparer.Ordinal);
}

public sealed class MaidBridgeMaidBinding
{
    [JsonPropertyName("characterId")]
    public string CharacterId { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("lastSeen")]
    public DateTime LastSeen { get; set; } = DateTime.UtcNow;

    /// <summary>累计在游戏里说过几句；每 5 句加 1 点信任。存在绑定表里，重启后不会从零再数。</summary>
    [JsonPropertyName("chats")]
    public int Chats { get; set; }

    [JsonPropertyName("lastBackup")]
    public DateTime LastBackup { get; set; }
}

/// <summary>轻量 JSON 读取：协议字段可能是数字/字符串/对象，任何类型不符都退回默认值，不让一条坏字段打断整帧。</summary>
internal static class BridgeJson
{
    /// <summary>
    /// 协议里的字符串一律在入口掐长度。模组那边的字段是外部输入，
    /// 一个 4 MB 的 name/task/id 会原样流进绑定表、文件名和提示词，不该等到用的时候才发现。
    /// </summary>
    public const int MaxStringChars = 2000;

    public static string Str(JsonObject o, string key, string fallback = "")
    {
        var node = o[key];
        if (node is JsonValue v)
        {
            if (v.TryGetValue<string>(out var s)) return Clip(s ?? fallback, MaxStringChars);
            if (v.TryGetValue<bool>(out var b)) return b ? "true" : "false";
        }
        return fallback;
    }

    public static string Clip(string? text, int max)
        => string.IsNullOrEmpty(text) || text.Length <= max ? (text ?? "") : text[..max];

    public static double Num(JsonObject o, string key, double fallback = 0)
        => o.TryGetPropertyValue(key, out var node) ? Num(node, fallback) : fallback;

    public static double Num(JsonNode? node, double fallback = 0)
    {
        if (node is not JsonValue v) return fallback;
        if (v.TryGetValue<double>(out var d)) return Clean(d, fallback);
        if (v.TryGetValue<long>(out var l)) return Clean(l, fallback);
        if (v.TryGetValue<int>(out var i)) return i;
        if (v.TryGetValue<string>(out var s) && double.TryParse(s, out var parsed)) return Clean(parsed, fallback);
        return fallback;
    }

    /// <summary>
    /// 对端是 Java，NaN 与 Infinity 能原样传过来，而 Math.Clamp 拿到 NaN 会照样返回 NaN，
    /// 后面无论是格式化显示还是当权重取整都会被打穿。读到非有限数就退回默认值。
    /// </summary>
    private static double Clean(double value, double fallback)
        => double.IsNaN(value) || double.IsInfinity(value) ? fallback : value;

    public static int Int(JsonObject o, string key, int fallback = 0)
    {
        var n = Num(o, key, fallback);
        if (double.IsNaN(n) || double.IsInfinity(n)) return fallback;
        return (int)Math.Clamp(n, int.MinValue, int.MaxValue);
    }

    public static bool Bool(JsonObject o, string key, bool fallback = false)
    {
        var node = o[key];
        if (node is not JsonValue v) return fallback;
        if (v.TryGetValue<bool>(out var b)) return b;
        if (v.TryGetValue<string>(out var s))
            return s.Trim().ToLowerInvariant() is "true" or "1" or "on" or "yes";
        if (v.TryGetValue<int>(out var i)) return i != 0;
        return fallback;
    }
}
