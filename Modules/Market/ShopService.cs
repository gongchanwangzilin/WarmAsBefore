using System.Text.Json;
using WarmAsBefore.Models;
using WarmAsBefore.Modules.AiChat;
using WarmAsBefore.Services;

namespace WarmAsBefore.Modules.Market;

/// <summary>
/// 「美了么」商店服务：
/// - 内置商品池：饮品/甜品/药物/情趣用品/方便食品/餐饮/节日特殊商品/礼品/玩具 + 场景券（游乐园门票等），共 1000+ 种
/// - 钱包（亲密币）持久化；购买记录持久化
/// - 场景券：购买后自动在地图解锁对应场景
/// - AI 实时生成：用户描述需求 → 云端生成商品条目
/// - 送礼/使用：消耗库存触发小雨对话反应，并挂 buff 标记（注入后续 AI 对话上下文）
/// </summary>
public sealed class ShopService
{
    private readonly StorageProvider _store;
    private readonly MapService _map;
    private readonly ChatEngine _chat;
    private readonly SettingsManager _settings;
    private readonly GameEngine _engine;
    private readonly MemoryVault _memory;
    private readonly Random _rnd = new();
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };

    private const string ShopKey = "meileme";
    private const string GitHubRaw = "https://raw.githubusercontent.com";
    private const string GitHubApi = "https://api.github.com";

    private sealed class ShopState
    {
        public int Coins { get; set; } = 520;
        public Dictionary<string, int> Owned { get; set; } = new();
        public List<ShopItem> AiItems { get; set; } = new();
        public List<GameRecord> GameRecords { get; set; } = new();
        public List<CharacterBuff> Buffs { get; set; } = new();
    }

    private ShopState _state = new();
    private List<ShopItem> _catalog = new();
    private bool _loaded;

    /// <summary>商品目录（内置 + AI 生成 + GitHub 同步）。</summary>
    public IReadOnlyList<ShopItem> Catalog => _catalog;
    public int Coins => _state.Coins;
    public string WalletLabel => $"亲密币 {_state.Coins}";
    public IReadOnlyList<GameRecord> GameRecords => _state.GameRecords;
    public int GameWins => _state.GameRecords.Count(r => r.Won);
    public int GameLosses => _state.GameRecords.Count(r => !r.Won);
    public string RecordLabel => $"小游戏战绩：{GameWins} 胜 {GameLosses} 负";
    public string? SyncLastError { get; private set; }
    public int SyncCount { get; private set; }
    /// <summary>已配置的 GitHub 仓库（owner/repo），来自 UserSettings。</summary>
    public string SyncRepo => _settings.Current.ShopGitHubRepo;
    /// <summary>完整目录是否仍在后台补货中（UI 转圈反馈用）。</summary>
    public bool IsCatalogLoading { get; private set; }

    /// <summary>当前生效的 buff 标记（按剩余轮数排序，最新在前）。</summary>
    public IReadOnlyList<CharacterBuff> ActiveBuffs => _state.Buffs
        .Where(b => b.TurnsLeft > 0)
        .OrderByDescending(b => b.TurnsLeft)
        .ToList();

    /// <summary>已购买的商品（Owned &gt; 0，供聊天界面送礼/使用面板选择）。</summary>
    public IReadOnlyList<ShopItem> OwnedItems => _catalog
        .Where(x => _state.Owned.TryGetValue(x.Id, out var n) && n > 0)
        .ToList();

    public ShopService(StorageProvider store, MapService map, ChatEngine chat, SettingsManager settings,
        GameEngine engine, MemoryVault memory)
    {
        _store = store;
        _map = map;
        _chat = chat;
        _settings = settings;
        _engine = engine;
        _memory = memory;
        // 种子目录纯内存生成，构造即就绪：任何页面打开商店立即有货，不等待 IO
        _catalog = BuildSeedCatalog();
        AttachMerchants();
        // buff 上下文注入 + 每轮对话 tick（无循环依赖：ChatEngine 只暴露委托，由本服务注册）
        _chat.BuffContextProvider = () => BuffContextText();
        _chat.AfterSend = TickBuffs;
    }

    public async Task InitializeAsync()
    {
        if (_loaded) return;
        _loaded = true;
        IsCatalogLoading = true;   // 初始化/后台补货期间 → UI 转圈
        try
        {
            _state = await _store.Load<ShopState>(ShopKey) ?? new ShopState();
            // 合并持久化 AI 商品（保持在最前）
            foreach (var ai in _state.AiItems)
            {
                if (_catalog.All(x => x.Id != ai.Id))
                {
                    ai.Owned = _state.Owned.TryGetValue(ai.Id, out var n) ? n : 0;
                    _catalog.Insert(0, ai);
                }
            }
            // 完整目录（1000+）后台生成，完成后合并刷新 → 「先能用，后加载完」
            _ = Task.Run(async () =>
            {
                try
                {
                    var full = BuildFullCatalog();
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        var aiItems = _catalog.Where(x => x.AiGenerated).ToList();
                        _catalog = full;
                        foreach (var item in _catalog)
                            item.Owned = _state.Owned.TryGetValue(item.Id, out var n) ? n : 0;
                        _catalog.InsertRange(0, aiItems);          // AI 商品保持在最前
                        AttachMerchants();
                        IsCatalogLoading = false;
                        RaiseChanged();
                    });
                    // 后台顺带做一次 GitHub 仓库同步（若已配置）
                    var cfg = _settings.Current;
                    if (!string.IsNullOrWhiteSpace(cfg.ShopGitHubRepo))
                        _ = SyncFromGitHubAsync(cfg.ShopGitHubRepo, silent: true);
                }
                catch (Exception ex)
                {
                    App.WriteLog($"ShopService.后台目录 -> {ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            App.WriteLog("ShopService.Initialize -> " + ex);
        }
    }

    public event Action? Changed;
    private void RaiseChanged() => Changed?.Invoke();

    /// <summary>持久化：序列化 + 写盘在后台线程执行，绝不阻塞 UI（购买/记账即时响应）。
    /// 后台线程先快照目录再序列化，避免与 UI 线程的目录合并并发冲突。</summary>
    private Task SaveAsync()
    {
        var snapshot = _catalog.Where(x => x.AiGenerated).ToList();
        return Task.Run(async () =>
        {
            try
            {
                _state.AiItems = snapshot;
                await _store.Save(ShopKey, _state);
            }
            catch (Exception ex)
            {
                App.WriteLog($"ShopService.Save -> {ex.Message}");
            }
        });
    }

    /// <summary>购买。返回 (成功, 消息)。场景券购买后立即解锁地图场景。</summary>
    public async Task<(bool ok, string msg)> BuyAsync(ShopItem item)
    {
        if (_state.Coins < item.Price) return (false, "亲密币不够啦，多陪陪小雨、赢几局游戏赚币吧！");
        _state.Coins -= item.Price;
        _state.Owned.TryGetValue(item.Id, out var n);
        _state.Owned[item.Id] = n + 1;
        item.Owned = n + 1;

        if (item.IsTicket && !string.IsNullOrEmpty(item.TicketScene))
            await UnlockSceneAsync(item);

        _ = SaveAsync();        // 后台落盘，购买立即反馈不等待
        RaiseChanged();
        return (true, $"已购买「{item.Name}」{(item.IsTicket ? "，场景已解锁！" : "，去找小雨一起用吧")}");
    }

    /// <summary>场景券解锁：把券对应场景挂到地图指定地点，并立即移动过去。</summary>
    private async Task UnlockSceneAsync(ShopItem item)
    {
        try
        {
            var loc = _map.Map.Locations.FirstOrDefault(l => l.Name == item.TicketLocation)
                      ?? _map.Map.Locations.FirstOrDefault();
            if (loc is null) return;
            var scene = _map.AddScene(loc.Id, item.TicketScene);
            _map.AddEdge(loc.Scenes.First().Id, scene.Id);
            await _map.SaveAsync();
            _map.NotifyChanged();
        }
        catch (Exception ex)
        {
            App.WriteLog($"ShopService.UnlockScene({item.Name}) -> {ex.Message}");
        }
    }

    // ==================== 送礼 / 使用 / buff 标记 ====================

    /// <summary>把商品送给小雨：消耗库存 1 件，触发小雨对话反应，挂 buff 标记，加好感。</summary>
    public async Task<(bool ok, string msg)> GiftAsync(ShopItem item)
    {
        if (item is null) return (false, "商品不存在");
        if (!_state.Owned.TryGetValue(item.Id, out var n) || n <= 0)
            return (false, "还没买过这件商品，先去购买吧");
        return await GiveOrUseAsync(item, isGift: true);
    }

    /// <summary>自己使用商品：消耗库存 1 件，触发小雨对话反应，挂 buff 标记。</summary>
    public async Task<(bool ok, string msg)> UseAsync(ShopItem item)
    {
        if (item is null) return (false, "商品不存在");
        if (!_state.Owned.TryGetValue(item.Id, out var n) || n <= 0)
            return (false, "还没买过这件商品，先去购买吧");
        return await GiveOrUseAsync(item, isGift: false);
    }

    private async Task<(bool ok, string msg)> GiveOrUseAsync(ShopItem item, bool isGift)
    {
        var charId = _engine.State.CharacterId;
        if (string.IsNullOrEmpty(charId))
            return (false, "先进入游戏再送礼物给小雨吧");

        // 1) 消耗库存 1 件
        var n = _state.Owned[item.Id];
        if (n <= 1) _state.Owned.Remove(item.Id);
        else _state.Owned[item.Id] = n - 1;
        item.Owned = n - 1;

        // 2) 挂 buff 标记
        AddBuff(item, isGift);

        // 3) 触发小雨对话反应
        string reply;
        try
        {
            var prompt = isGift
                ? $"主人送了你一份礼物「{item.Name}」{item.Emoji}（{item.Desc}）。请开心地收下，用一两句话表达你的惊喜和感受，自然一些，不要提及系统提示。"
                : $"主人正在使用「{item.Name}」{item.Emoji}（{item.Desc}）。用一两句话自然地回应主人，关心或打趣都可以，不要提及系统提示。";
            reply = await _chat.Send(charId, prompt);
        }
        catch (Exception ex)
        {
            App.WriteLog($"ShopService.{nameof(GiveOrUseAsync)} -> {ex.Message}");
            reply = isGift
                ? $"（小雨开心地收下了「{item.Name}」，眼睛亮晶晶的）谢谢主人！"
                : $"（小雨看着你使用「{item.Name}」）主人要好好享受哦～";
        }

        // 4) 送礼加好感 + 记入回忆录
        if (isGift)
        {
            var delta = item.Price >= 50 ? 5 : 3;
            _ = _memory.LogAffection(charId, delta, $"送礼：{item.Name}");
        }

        _ = SaveAsync();
        RaiseChanged();
        return (true, reply);
    }

    /// <summary>挂一个 buff 标记（同商品重复送礼/使用：刷新剩余轮数并保持最新描述）。</summary>
    private void AddBuff(ShopItem item, bool isGift)
    {
        var key = $"{(isGift ? "gift" : "use")}_{item.Id}";
        var exist = _state.Buffs.FirstOrDefault(b => b.Id == key);
        if (exist is not null)
        {
            exist.TurnsLeft = 6;
            exist.AppliedAt = DateTime.Now;
            return;
        }
        _state.Buffs.Add(new CharacterBuff
        {
            Id = key,
            Name = item.Name,
            Emoji = item.Emoji,
            Desc = item.Desc,
            Source = isGift ? "送礼" : "使用",
            TurnsLeft = 6,
            AppliedAt = DateTime.Now
        });
    }

    /// <summary>每轮对话后调用：buff 剩余轮数减 1，归零移除，异步持久化。</summary>
    private void TickBuffs()
    {
        try
        {
            bool changed = false;
            for (int i = _state.Buffs.Count - 1; i >= 0; i--)
            {
                var b = _state.Buffs[i];
                if (b.TurnsLeft <= 0) { _state.Buffs.RemoveAt(i); changed = true; continue; }
                b.TurnsLeft--;
                if (b.TurnsLeft <= 0) { _state.Buffs.RemoveAt(i); changed = true; }
            }
            if (!changed) return;
            _ = SaveAsync();
            MainThread.BeginInvokeOnMainThread(RaiseChanged);
        }
        catch (Exception ex)
        {
            App.WriteLog($"ShopService.TickBuffs -> {ex.Message}");
        }
    }

    /// <summary>把当前生效的 buff 标记渲染成 AI 可见的上下文文本（拼接在用户消息前）。</summary>
    private string BuffContextText()
    {
        var buffs = ActiveBuffs;
        if (buffs.Count == 0) return "";
        var lines = buffs.Select(b => $"- {b.Emoji} {b.Name}（{b.Source}，剩余 {b.TurnsLeft} 轮）：{b.Desc}");
        return "【当前状态标记】\n" + string.Join("\n", lines);
    }

    /// <summary>小雨当前身体/状态总览（查看身体状态面板用）。无角色时返回 null。</summary>
    public string? BodyStatusText()
    {
        var ch = _engine.ActiveCharacter;
        if (ch is null) return null;
        var s = ch.State;
        var moodText = s.Mood switch
        {
            "happy" => "开心", "sad" => "难过", "angry" => "生气",
            "tired" => "疲惫", "excited" => "兴奋", "shy" => "害羞",
            _ => s.Mood
        };
        var lines = new List<string>
        {
            $"💗 好感 {s.Affection}  ·  🤝 信任 {s.Trust}  ·  ⚡ 精力 {s.Energy}",
            $"😊 心情：{moodText}",
            $"📍 位置：{s.Location}"
        };
        var cycle = s.Cycle;
        if (cycle is not null && cycle.Tracked)
        {
            var phase = cycle.Phase switch
            {
                "period" => "经期", "follicular" => "卵泡期",
                "ovulation" => "排卵期", "luteal" => "黄体期",
                _ => cycle.Phase
            };
            lines.Add($"🌸 生理周期：第 {cycle.CycleDay} 天（{phase}）");
        }
        var buffs = ActiveBuffs;
        if (buffs.Count > 0)
        {
            lines.Add("✨ 当前标记：" + string.Join("  ", buffs.Select(b => $"{b.Emoji}{b.Name}(剩{b.TurnsLeft}轮)")));
        }
        else
        {
            lines.Add("✨ 当前标记：无");
        }
        return string.Join("\n", lines);
    }

    /// <summary>记录一局游戏战绩并发放亲密币奖励（胜 +30 / 负 +10）。同一局只记一次。</summary>
    public async Task<bool> AddGameRecordAsync(string game, bool won, int moves, string note)
    {
        try
        {
            _state.GameRecords.Insert(0, new GameRecord
            {
                Game = game,
                Won = won,
                Moves = moves,
                Note = string.IsNullOrWhiteSpace(note) ? (won ? "你赢了！" : "输了，再战一局") : note
            });
            _state.Coins += won ? 30 : 10;
            _ = SaveAsync();   // 后台落盘
            RaiseChanged();
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>AI 实时生成商品：把用户需求描述交给云端，解析为 1 个商品条目加入目录。</summary>
    public async Task<(bool ok, string msg)> GenerateItemAsync(string prompt)
    {
        var text = string.IsNullOrWhiteSpace(prompt) ? "随便推荐一个实用又有趣的商品" : prompt.Trim();
        try
        {
            var reply = await _chat.Send("美了么", $"你是美了么商城的选品师。请根据需求「{text}」推荐 1 件商品，只输出 JSON，不要任何其他文字，格式：{{\"name\":\"商品名\",\"category\":\"品类\",\"desc\":\"一句话描述\",\"price\":数字(10-999),\"emoji\":\"一个emoji\",\"ticket\":false}}");
            var item = ParseItemJson(reply);
            if (item is null) return (false, "AI 没生成出来，换个说法试试？");
            item.Id = "ai_" + Guid.NewGuid().ToString("N")[..8];
            item.AiGenerated = true;
            _catalog.Insert(0, item);
            await SaveAsync();
            RaiseChanged();
            return (true, $"AI 上新：「{item.Name}」已加入商店！");
        }
        catch
        {
            return (false, "云端没响应，稍后再试");
        }
    }

    private ShopItem? ParseItemJson(string json)
    {
        try
        {
            var start = json.IndexOf('{');
            var end = json.LastIndexOf('}');
            if (start < 0 || end <= start) return null;
            json = json[start..(end + 1)];
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var name = root.TryGetProperty("name", out var n) ? n.GetString() : "";
            if (string.IsNullOrEmpty(name)) return null;
            var price = root.TryGetProperty("price", out var p) && p.TryGetInt32(out var pi) ? Math.Clamp(pi, 5, 999) : 66;
            var cat = root.TryGetProperty("category", out var c) ? c.GetString() ?? "杂货" : "杂货";
            var desc = root.TryGetProperty("desc", out var d) ? d.GetString() ?? "" : "";
            var emoji = root.TryGetProperty("emoji", out var e) ? e.GetString() ?? "🎁" : "🎁";
            return new ShopItem { Name = name, Category = cat, Desc = desc, Price = price, Emoji = emoji };
        }
        catch { return null; }
    }

    // ==================== 内置商品池（组合生成 1000+） ====================

    /// <summary>精选种子目录：立即可用的小集合（首屏先展示，完整目录后台接管）。</summary>
    private List<ShopItem> BuildSeedCatalog()
    {
        var list = new List<ShopItem>();
        int seq = 0;
        void Add(string cat, string name, string desc, int price, string emoji, bool ticket = false, string scene = "", string loc = "")
        {
            list.Add(new ShopItem
            {
                Id = $"s{seq++}",
                Name = name,
                Category = cat,
                Desc = desc,
                Price = price,
                Emoji = emoji,
                IsTicket = ticket,
                TicketScene = scene,
                TicketLocation = loc
            });
        }

        // 每类挑几样热门款，秒开不卡
        Add("饮品", "招牌珍珠奶茶", "经典黑糖珍珠，暖心暖胃", 16, "🧋");
        Add("饮品", "杨枝甘露", "芒果西柚，夏日清爽", 22, "🥭");
        Add("饮品", "生椰拿铁", "椰香浓郁，咖啡醇厚", 24, "🥥");
        Add("饮品", "蜜桃乌龙", "蜜桃香气配乌龙茶底", 18, "🍑");
        Add("甜品", "草莓蛋糕", "草莓口味的蛋糕，甜度刚好", 28, "🍰");
        Add("甜品", "提拉米苏", "咖啡酒香，入口即化", 26, "🍮");
        Add("甜品", "抹茶冰淇淋", "微苦回甘，细腻顺滑", 15, "🍦");
        Add("甜品", "马卡龙礼盒", "法式少女心，六色六味", 32, "🧁");
        Add("餐饮", "红烧肉套餐", "肥而不腻，下饭神器", 38, "🍱");
        Add("餐饮", "麻辣小龙虾", "夏夜大排档标配", 68, "🦞");
        Add("餐饮", "番茄炒蛋单人份", "家常味，暖暖的", 18, "🍛");
        Add("方便食品", "螺蛳粉", "酸辣鲜香，越吃越上瘾", 14, "🍜");
        Add("方便食品", "自热小火锅", "加班深夜救星", 22, "🥘");
        Add("药物", "感冒灵冲剂", "换季感冒，一包见效", 28, "💊");
        Add("药物", "创可贴一盒", "小伤口必备", 8, "🩹");
        Add("药物", "维生素C泡腾片", "增强抵抗力", 25, "🍊");
        Add("情趣用品", "香薰蜡烛", "氛围感拉满", 39, "🕯️");
        Add("情趣用品", "真丝眼罩", "助眠神器", 29, "🎭");
        Add("礼品", "玫瑰花束", "11朵红玫瑰，表白必备", 99, "🌹");
        Add("礼品", "毛绒玩具熊", "抱抱更温暖", 45, "🧸");
        Add("礼品", "星空投影灯", "满屋星河", 76, "🌌");
        Add("玩具", "乐高积木", "拼出想象力", 128, "🧱");
        Add("玩具", "盲盒", "拆开有惊喜", 29, "📦");
        Add("节日特殊商品", "生日蛋糕", "专属生日蛋糕，可定制", 88, "🎂");
        Add("节日特殊商品", "年货大礼包", "春节年味十足", 128, "🧧");
        Add("场景券", "游乐园门票", "解锁【游乐园】场景，旋转木马、过山车，和小雨尽情玩", 168, "🎡", true, "游乐园", "公园");
        Add("场景券", "电影院双人票", "解锁【电影院】场景，抱着爆米花看晚场电影", 98, "🎬", true, "电影院", "家");
        Add("场景券", "温泉度假券", "解锁【温泉汤屋】场景，一起泡温泉看星星", 198, "♨️", true, "温泉汤屋", "海边");
        return list;
    }

    private List<ShopItem> BuildFullCatalog()
    {
        var list = new List<ShopItem>();
        int seq = 0;
        void Add(string cat, string name, string desc, int price, string emoji, bool ticket = false, string scene = "", string loc = "")
        {
            list.Add(new ShopItem
            {
                Id = $"b{seq++}",
                Name = name,
                Category = cat,
                Desc = desc,
                Price = price,
                Emoji = emoji,
                IsTicket = ticket,
                TicketScene = scene,
                TicketLocation = loc
            });
        }

        // 1) 饮品（基料 × 风味 × 规格 组合 → 数百种）
        var bases = new[] { "奶茶", "果茶", "咖啡", "气泡水", "柠檬水", "奶昔", "冰沙", "酸梅汤", "豆浆", "蜂蜜柚子茶" };
        var flavors = new[] { "珍珠", "椰果", "布丁", "红豆", "芋圆", "燕麦", "黑糖", "芝士奶盖", "桂花", "玫瑰", "香草", "海盐", "茉莉", "蜜桃", "草莓", "芒果", "抹茶", "可可", "焦糖", "凤梨" };
        foreach (var b in bases)
            foreach (var f in flavors)
                Add("饮品", $"{b}·{f}", $"{f}风味的{b}，暖心暖胃", 12 + _rnd.Next(0, 20), _rnd.Next(2) == 0 ? "🧋" : "🥤");

        // 2) 甜品（品类 × 口味）
        var dessertKind = new[] { "蛋糕", "泡芙", "马卡龙", "布丁", "冰淇淋", "提拉米苏", "蛋挞", "铜锣烧", "大福", "曲奇" };
        var dessertFlav = new[] { "草莓", "巧克力", "抹茶", "芋泥", "芝士", "芒果", "蓝莓", "香草", "焦糖", "榴莲" };
        foreach (var k in dessertKind)
            foreach (var f in dessertFlav)
                Add("甜品", $"{f}{k}", $"{f}口味的{k}，甜度刚好", 15 + _rnd.Next(0, 25), _rnd.Next(2) == 0 ? "🍰" : "🧁");

        // 3) 药物（常见品类）
        var meds = new (string, string, int, string)[]
        {
            ("感冒灵冲剂", "换季感冒，一包见效", 28, "💊"), ("布洛芬缓释片", "头疼发热，缓解疼痛", 22, "💊"),
            ("创可贴一盒", "小伤口必备", 8, "🩹"), ("藿香正气水", "中暑肠胃不适", 15, "🧪"),
            ("维生素C泡腾片", "增强抵抗力", 25, "🍊"), ("润喉糖", "嗓子干哑", 12, "🍬"),
            ("退热贴", "物理降温", 18, "❄️"), ("眼药水", "眼睛干涩", 20, "👁️"),
            ("驱蚊液", "夏日防蚊", 16, "🦟"), ("暖宝宝贴", "手脚冰凉", 10, "🔥"),
        };
        foreach (var (n, d, p, e) in meds) Add("药物", n, d, p, e);

        // 4) 情趣用品（轻量表述）
        var fun = new (string, string, int, string)[]
        {
            ("香薰蜡烛", "氛围感拉满", 39, "🕯️"), ("真丝眼罩", "助眠神器", 29, "🎭"),
            ("按摩精油", "放松身心", 49, "🧴"), ("情趣骰子", "增加小情趣", 25, "🎲"),
            ("兔耳朵发箍", "可可爱爱", 19, "🐰"), ("蕾丝手套", "优雅点缀", 22, "🧤"),
        };
        foreach (var (n, d, p, e) in fun) Add("情趣用品", n, d, p, e);

        // 5) 方便食品（品牌 × 口味组合）
        var quick = new[] { "泡面", "自热火锅", "自热米饭", "速冻水饺", "螺蛳粉", "酸辣粉", "燕麦片", "即食鸡胸肉", "八宝粥", "能量棒" };
        var quickFlav = new[] { "麻辣", "番茄", "菌菇", "藤椒", "香辣", "酸菜", "咖喱", "黑椒", "原味", "泡菜" };
        foreach (var q in quick)
            foreach (var f in quickFlav)
                Add("方便食品", $"{f}{q}", $"{f}味{q}，即食方便", 6 + _rnd.Next(0, 18), _rnd.Next(2) == 0 ? "🍜" : "🥡");

        // 6) 餐饮（菜品 × 做法）
        var dishes = new[] { "红烧肉", "清蒸鱼", "糖醋排骨", "宫保鸡丁", "麻婆豆腐", "番茄炒蛋", "油焖大虾", "小炒黄牛肉", "地三鲜", "酸菜鱼" };
        var dishWay = new[] { "套餐", "单人份", "双人份", "家庭装", "加辣版", "少油版", "豪华版", "加蛋版", "升级版", "招牌版" };
        foreach (var d in dishes)
            foreach (var w in dishWay)
                Add("餐饮", $"{d}{w}", $"{d}的{w}，现点现做", 25 + _rnd.Next(0, 60), _rnd.Next(2) == 0 ? "🍱" : "🍛");

        // 7) 节日特殊商品
        var fest = new (string, string, int, string)[]
        {
            ("生日蛋糕", "专属生日蛋糕，可定制", 88, "🎂"), ("艾草", "端午驱邪祈福", 12, "🌿"),
            ("青团", "清明时令，豆沙馅", 16, "🟢"), ("砂糖橘", "过年必备，甜到心里", 20, "🍊"),
            ("月饼礼盒", "中秋团圆", 68, "🥮"), ("汤圆", "元宵节软糯甜蜜", 15, "🍡"),
            ("年货大礼包", "春节年味十足", 128, "🧧"), ("粽子", "端午咸甜双拼", 18, "🫔"),
            ("平安果", "平安夜祝福", 12, "🍎"), ("烟花棒", "跨年氛围", 22, "🎆"),
            ("圣诞帽", "节日氛围", 15, "🎅"), ("红围巾", "新年好运", 35, "🧣"),
            ("孔明灯", "许愿祈福", 10, "🏮"), ("压岁红包", "图个吉利", 5, "🧧"),
        };
        foreach (var (n, d, p, e) in fest) Add("节日特殊商品", n, d, p, e);

        // 8) 礼品
        var gifts = new (string, string, int, string)[]
        {
            ("玫瑰花束", "11朵红玫瑰，表白必备", 99, "🌹"), ("向日葵花束", "阳光灿烂的祝福", 66, "🌻"),
            ("毛绒玩具熊", "抱抱更温暖", 45, "🧸"), ("水晶球摆件", "梦幻雪花飘落", 58, "🔮"),
            ("手写贺卡", "亲手写下心意", 8, "💌"), ("香奈儿风礼盒", "精致包装", 188, "🎀"),
            ("定制项链", "刻名字的专属礼物", 158, "📿"), ("拍立得相册", "记录美好瞬间", 42, "📷"),
            ("星空投影灯", "满屋星河", 76, "🌌"), ("音乐盒", "八音盒轻响", 52, "🎶"),
        };
        foreach (var (n, d, p, e) in gifts) Add("礼品", n, d, p, e);

        // 9) 玩具
        var toys = new (string, string, int, string)[]
        {
            ("乐高积木", "拼出想象力", 128, "🧱"), ("毛线娃娃", "手工编织玩偶", 38, "🧵"),
            ("指尖陀螺", "解压神器", 12, "🌀"), ("泡泡机", "梦幻泡泡漫天", 25, "🫧"),
            ("弹跳球", "童年回忆", 8, "⚽"), ("磁力片", "创意搭建", 66, "🧲"),
            ("遥控赛车", "速度与激情", 88, "🏎️"), ("拼图1000片", "周末好时光", 35, "🧩"),
            ("盲盒", "拆开有惊喜", 29, "📦"), ("木制积木", "原木质感", 45, "🪵"),
        };
        foreach (var (n, d, p, e) in toys) Add("玩具", n, d, p, e);

        // 10) 场景券
        Add("场景券", "游乐园门票", "解锁【游乐园】场景，旋转木马、过山车，和小雨尽情玩", 168, "🎡", true, "游乐园", "公园");
        Add("场景券", "电影院双人票", "解锁【电影院】场景，抱着爆米花看晚场电影", 98, "🎬", true, "电影院", "家");
        Add("场景券", "温泉度假券", "解锁【温泉汤屋】场景，一起泡温泉看星星", 198, "♨️", true, "温泉汤屋", "海边");
        Add("场景券", "演唱会门票", "解锁【演唱会】场景，挥舞荧光棒听现场", 268, "🎤", true, "演唱会现场", "公园");
        Add("场景券", "摩天轮票", "解锁【摩天轮】场景，最高点许愿", 138, "🎡", true, "摩天轮", "海边");
        Add("场景券", "动物园门票", "解锁【动物园】场景，看小动物卖萌", 88, "🦁", true, "动物园", "公园");
        Add("场景券", "电玩城币券", "解锁【电玩城】场景，夹娃娃赢大奖", 58, "🕹️", true, "电玩城", "家");
        Add("场景券", "音乐节通票", "解锁【音乐节】场景，草地蹦迪看夕阳", 148, "🎸", true, "音乐节现场", "海边");
        return list;
    }

    // ==================== 虚拟商家分配 ====================

    /// <summary>按品类给内置/AI 商品分配虚拟商家（GitHub 同步商品用仓库自带的商家名，不覆盖）。</summary>
    private static readonly Dictionary<string, string> MerchantByCategory = new()
    {
        ["饮品"] = "茶语茶馆",
        ["甜品"] = "蜜语甜品屋",
        ["餐饮"] = "小雨私房菜",
        ["方便食品"] = "深夜食堂便利店",
        ["药物"] = "安心大药房",
        ["情趣用品"] = "浪漫小铺",
        ["礼品"] = "心意礼品阁",
        ["玩具"] = "童趣玩具城",
        ["节日特殊商品"] = "节庆杂货铺",
        ["场景券"] = "小雨带你玩",
        ["杂货"] = "美了么自营",
    };

    private void AttachMerchants()
    {
        foreach (var item in _catalog)
        {
            if (item.Source == "github") continue;               // 仓库自带商家
            if (!string.IsNullOrWhiteSpace(item.Merchant)) continue;
            item.Merchant = MerchantByCategory.TryGetValue(item.Category, out var m) ? m : "美了么自营";
        }
    }

    // ==================== GitHub 仓库同步 ====================

    /// <summary>
    /// 从 GitHub 仓库同步商品目录。约定仓库根目录含 shop/catalog.json：
    /// [{ "name","category","desc","price","emoji","merchant","image"(相对 shop/ 的路径或完整 URL) }]
    /// 图片下载到本地缓存目录，展示时用本地路径（离线可用）。
    /// </summary>
    public async Task<(bool ok, string msg)> SyncFromGitHubAsync(string repo, bool silent = false)
    {
        repo = (repo ?? "").Trim().TrimEnd('/')
            .Replace("https://github.com/", "", StringComparison.OrdinalIgnoreCase)
            .Replace("https://raw.githubusercontent.com/", "", StringComparison.OrdinalIgnoreCase)
            .Replace(".git", "", StringComparison.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(repo) || !repo.Contains('/'))
            return (false, "仓库格式应为 owner/repo，例如 user/my-shop");
        if (!silent) SyncLastError = null;

        try
        {
            // 1) 拉取 catalog.json（默认 main 分支，失败再试 master）
            var json = await TryFetchJsonAsync(repo);
            if (string.IsNullOrEmpty(json))
                return (false, "仓库里没找到 shop/catalog.json（默认 main/master 分支）");

            var items = ParseRemoteCatalog(json, repo);
            if (items.Count == 0) return (false, "catalog.json 解析失败或没有商品");

            // 2) 下载图片到本地缓存（后台执行，不阻塞返回）
            var cacheDir = Path.Combine(_store.Root, "ShopImages");
            Directory.CreateDirectory(cacheDir);
            foreach (var it in items)
            {
                if (string.IsNullOrWhiteSpace(it.ImagePath)) continue;
                if (!it.ImagePath.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    it.ImagePath = $"{GitHubRaw}/{repo}/main/shop/{it.ImagePath.TrimStart('/')}";
            }
            _ = Task.Run(async () =>
            {
                foreach (var it in items.Where(x => x.HasImage))
                {
                    try
                    {
                        var ext = Path.GetExtension(new Uri(it.ImagePath).AbsolutePath);
                        if (string.IsNullOrEmpty(ext)) ext = ".png";
                        var local = Path.Combine(cacheDir, it.Id + ext);
                        if (!File.Exists(local))
                        {
                            using var resp = await _http.GetAsync(it.ImagePath);
                            if (resp.IsSuccessStatusCode)
                            {
                                await using var fs = File.Create(local);
                                await resp.Content.CopyToAsync(fs);
                            }
                        }
                        if (File.Exists(local)) it.ImagePath = local;
                    }
                    catch (Exception ex)
                    {
                        App.WriteLog($"ShopService.图片下载({it.Name}) -> {ex.Message}");
                        it.ImagePath = "";   // 下载失败 → Emoji 兜底
                    }
                }
                MainThread.BeginInvokeOnMainThread(RaiseChanged);
            });

            // 3) 合并进目录（仓库商品去重后置顶），持久化仓库地址
            MainThread.BeginInvokeOnMainThread(() =>
            {
                var existing = _catalog.Where(x => x.Source == "github").Select(x => x.Id).ToHashSet();
                foreach (var it in items)
                {
                    it.Source = "github";
                    if (!existing.Contains(it.Id))
                    {
                        it.Owned = _state.Owned.TryGetValue(it.Id, out var n) ? n : 0;
                        _catalog.Insert(0, it);
                    }
                }
                _catalog = _catalog.DistinctBy(x => x.Id).ToList();
                RaiseChanged();
            });

            if (!silent)
            {
                _settings.Apply(_settings.Current with { ShopGitHubRepo = repo });
                await _settings.Persist();
            }
            SyncCount = items.Count;
            return (true, $"已从 {repo} 同步 {items.Count} 件商品");
        }
        catch (Exception ex)
        {
            if (!silent) SyncLastError = ex.Message;
            App.WriteLog($"ShopService.SyncFromGitHub -> {ex.Message}");
            return (false, $"同步失败：{ex.Message}");
        }
    }

    private async Task<string?> TryFetchJsonAsync(string repo)
    {
        var branches = new[] { "main", "master" };
        foreach (var br in branches)
        {
            try
            {
                var url = $"{GitHubRaw}/{repo}/{br}/shop/catalog.json";
                using var resp = await _http.GetAsync(url);
                if (resp.IsSuccessStatusCode)
                    return await resp.Content.ReadAsStringAsync();
            }
            catch { /* 试下一个分支 */ }
        }
        return null;
    }

    private List<ShopItem> ParseRemoteCatalog(string json, string repo)
    {
        try
        {
            var start = json.IndexOf('[');
            var end = json.LastIndexOf(']');
            if (start < 0 || end <= start) return new();
            json = json[start..(end + 1)];
            using var doc = JsonDocument.Parse(json);
            var list = new List<ShopItem>();
            int seq = 0;
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.Object) continue;
                var name = el.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                if (string.IsNullOrEmpty(name)) continue;
                var price = el.TryGetProperty("price", out var p) && p.TryGetInt32(out var pi) ? Math.Clamp(pi, 5, 9999) : 66;
                var cat = el.TryGetProperty("category", out var c) ? c.GetString() ?? "杂货" : "杂货";
                var desc = el.TryGetProperty("desc", out var d) ? d.GetString() ?? "" : "";
                var emoji = el.TryGetProperty("emoji", out var e) ? e.GetString() ?? "🎁" : "🎁";
                var merchant = el.TryGetProperty("merchant", out var m) ? m.GetString() ?? "" : "";
                var image = el.TryGetProperty("image", out var im) ? im.GetString() ?? "" : "";
                var isTicket = el.TryGetProperty("ticket", out var t) && t.ValueKind == JsonValueKind.True;
                var scene = el.TryGetProperty("scene", out var sc) ? sc.GetString() ?? "" : "";
                var loc = el.TryGetProperty("location", out var lo) ? lo.GetString() ?? "" : "";
                list.Add(new ShopItem
                {
                    Id = $"gh_{repo}_{seq++}",
                    Name = name,
                    Category = cat,
                    Desc = desc,
                    Price = price,
                    Emoji = emoji,
                    Merchant = merchant,
                    ImagePath = image,
                    IsTicket = isTicket,
                    TicketScene = scene,
                    TicketLocation = loc,
                    Source = "github"
                });
            }
            return list;
        }
        catch (Exception ex)
        {
            App.WriteLog($"ShopService.ParseRemoteCatalog -> {ex.Message}");
            return new();
        }
    }
}