using WarmAsBefore.Drawables;
using WarmAsBefore.Models;
using WarmAsBefore.Services;

namespace WarmAsBefore.Services.Dev;

/// <summary>
/// 压测数据。每条都取自 <c>break-ui</c> 的 CATALOG：真实用户会真的产生这种值，
/// 不是 <c>"aaaa…"</c> 那种一测就没人信的假边界。
///
/// 这些数据只经 <see cref="WorstCaseProvider"/> 的开关进入四个页面的取数边界，
/// 页面里判断「数据是什么」的代码（头像兜底、时间文案、计数格式、空态）全部照常跑。
/// </summary>
public static class WorstCaseFixtures
{
    // ==================== 共用最坏值 ====================

    /// <summary>长、带连字符、带变音符：单行放不下，且连字符是合法断行点。</summary>
    private const string LongName = "Aleksandra Wiśniewska-Kowalczyk";

    /// <summary>无空格的邮箱：整行没有任何断行机会。</summary>
    private const string LongEmail = "bartholomew.fitzgerald@northwind-industries-holdings.example.com";

    private const string OneCharName = "乔";
    private const string ArabicName = "نور الهدى عبد الرحمن";
    private const string VietnameseName = "Đặng Thị Ngọc Hân";
    private const string EmojiFirstName = "🦊 Fox";
    private const string LongTitle = "高级平台基础设施前端交互设计工程师（企业协作与通知系统方向）";

    /// <summary>日文 + 阿拉伯文 + emoji 混排：行高、双向文本、字宽全都不同。</summary>
    private const string MixedScript =
        "今日はちょっとだけ泣いた 🥲 مرحبا بك يا صديقي، هل ما زلت هناك؟ 明天见 👋🏽";

    /// <summary>一条真实长度的中文长消息：一屏三行以上。</summary>
    private const string LongChinese =
        "今天下午那阵风把阳台上的衣架全吹倒了，我蹲在地上一件件捡，捡到一半就不想动了。" +
        "后来想起你上次说「晾不干就明天再晾」，就真的没再管它，回屋里泡了杯茶坐着。\n" +
        "茶凉了才想起来还没回你消息。其实我没什么事，就是想说一句：那件灰色的毛衣我留着了，" +
        "洗完有点缩水，但穿着刚好。你要是下次还来，我就穿着它给你开门。";

    /// <summary>没有断行机会的长文件名和长 URL。</summary>
    private const string LongFileName = "Q3 Board Deck — FINAL (revised) v12 [approved by legal].pdf";
    private const string LongUrl =
        "https://example.com/workspaces/acme/projects/q3-launch/docs/9f8e7d6c5b4a?tab=comments&filter=unresolved";

    /// <summary>一个标签比它所在的整行还宽。</summary>
    private const string WideTag = "customer-feedback-from-enterprise-onboarding";

    /// <summary>角色名：长名字 / 单字 / 空名字（空名字要能兜住，不能变成空白标题）。</summary>
    public static string PersonName(WorstCaseMode mode) => mode switch
    {
        WorstCaseMode.Empty => "",
        WorstCaseMode.One => OneCharName,
        _ => LongName
    };

    // ==================== 微信：会话列表 + 聊天区 ====================

    /// <summary>会话列表的一行：正名、头像绝对路径（空 = 缺头像）、最后一条、未读、时间。</summary>
    public sealed record ContactSeed(
        string CharacterId, string Name, string AvatarPath, string LastMessage, bool Unread, DateTime LastMessageAt);

    public sealed record MessageSeed(string Id, string Text, bool IsUser, string SenderName);

    public sealed record PartnerSeed(string Name, string ModeLabel);

    public static class Chat
    {
        private static DateTime Ago(int minutes) => DateTime.Now.AddMinutes(-minutes);

        /// <summary>聊天区顶栏：长名字 + 一句话就说清的状态（翻译后会长得很）。</summary>
        public static PartnerSeed Partner(WorstCaseMode mode) => mode switch
        {
            WorstCaseMode.Empty => new(PersonName(mode), "本地微信 · TA 还在"),
            WorstCaseMode.One => new(PersonName(mode), "本地微信 · TA 还在"),
            WorstCaseMode.Huge => new(PersonName(mode), "本地微信 · TA 还在"),
            _ => new(PersonName(mode),
                "真微信 · 已接入 · 上次同步 3 天前，当前正在网络很差的境外服务器上重试")
        };

        public static List<ContactSeed> Contacts(WorstCaseMode mode)
        {
            if (mode == WorstCaseMode.Empty) return new List<ContactSeed>();
            if (mode == WorstCaseMode.One)
                return new List<ContactSeed>
                {
                    new("worst-1", OneCharName, "", "", true, Ago(2))
                };
            if (mode == WorstCaseMode.Huge)
            {
                var many = new List<ContactSeed>();
                foreach (var first in Mixed())
                    many.Add(first with { CharacterId = "huge-" + first.CharacterId });
                for (var i = many.Count; i < 1000; i++)
                    many.Add(new ContactSeed($"huge-{i}", $"集团协作群 {i:0000} · 第三事业部",
                        "", $"第 {i} 条：这条消息很长，长到一行放不下，用来检查列表副标题到底会不会把时间挤走 #{i}",
                        i % 3 == 0, Ago(i * 7)));
                return many;
            }
            return Mixed();

            static List<ContactSeed> Mixed() => new()
            {
                // 行的顺序就是屏幕上的顺序：把四种不同的坏法分到前四行。
                new("worst-a", LongName, "", LongChinese, true, Ago(1)),
                new("worst-b", OneCharName, "", "嗯", false, Ago(26)),
                new("worst-c", EmojiFirstName, MissingAvatar("fox.png"), LongEmail, true, Ago(60 * 26)),
                new("worst-d", ArabicName, "", "", false, Ago(60 * 24 * 400)),
                new("worst-e", VietnameseName, "", LongUrl, true, Ago(60 * 24 * 3)),
                new("worst-f", "", "", string.Empty, false, default),
                new("worst-g", LongTitle, "", "刚看到你发的那个文件：" + LongFileName, true, Ago(60 * 5)),
                new("worst-h", "Ólafur Darri Ólafsson", "", "晚安", false, Ago(60 * 24 * 11))
            };
        }

        public static List<MessageSeed> Messages(WorstCaseMode mode)
        {
            if (mode == WorstCaseMode.Empty) return new List<MessageSeed>();
            if (mode == WorstCaseMode.One)
                return new List<MessageSeed> { new("m1", "在吗？", false, OneCharName) };
            if (mode == WorstCaseMode.Huge)
            {
                var many = new List<MessageSeed>();
                for (var i = 0; i < 1000; i++)
                    many.Add(new MessageSeed($"hm-{i}", i % 7 == 0 ? LongChinese : $"第 {i} 条消息：今天的风有点大。",
                        i % 2 == 0, i % 2 == 0 ? "我" : LongName));
                return many;
            }
            return new List<MessageSeed>
            {
                new("w1", MixedScript, false, LongName),
                new("w2", LongChinese, true, "我"),
                new("w3", "🥹", false, LongName),
                new("w4", "", false, LongName),
                new("w5", LongUrl, true, "我"),
                new("w6", LongFileName, false, LongName),
                new("w7", "第一行\n第二行\n第三行\n第四行\n第五行", true, "我"),
                new("w8", "那我先睡了，明天还要早起。晚安 🌙", false, LongName),
                new("w9", "嗯嗯", true, "我"),
                new("w10", "好", false, OneCharName),
                new("w11", new string('嗯', 220) + "——真的，就这样吧。", false, ArabicName),
                new("w12", "（这条只有标点）……？！", true, "我")
            };
        }

        /// <summary>指向一个不存在的文件：File.Exists 为假，正好走「缺头像」这条路。</summary>
        private static string MissingAvatar(string name) => Path.Combine(App.RootDirectory, "avatars", name);
    }

    // ==================== 监测页 ====================

    public static class Monitor
    {
        public static (string Name, double Affection, double Trust, double Energy) Numeric(WorstCaseMode mode) => mode switch
        {
            WorstCaseMode.Empty => (PersonName(mode), 0, 0, 0),
            WorstCaseMode.One => (PersonName(mode), 1, 1, 1),
            WorstCaseMode.Worst => (PersonName(mode), 9999, -1, 0),
            _ => (PersonName(mode), 9999, 9999, 9999)
        };

        public static DailyPlan? Plan(WorstCaseMode mode)
        {
            if (mode == WorstCaseMode.Empty) return null;
            if (mode == WorstCaseMode.One)
                return new DailyPlan
                {
                    CharacterId = "worst",
                    Outfit = "那件洗得有点缩水的灰毛衣",
                    Items = { new PlanItem { Id = "p0", Hour = 8, Minute = 5, Kind = PlanKind.Wear, Title = OneCharName, Detail = "" } }
                };
            if (mode == WorstCaseMode.Huge)
            {
                var all = new List<PlanItem>();
                for (var i = 0; i < 96; i++)
                    all.Add(new PlanItem
                    {
                        Id = $"ph-{i}",
                        Hour = (i * 15) / 60,
                        Minute = (i * 15) % 60,
                        Kind = (PlanKind)(i % 10),
                        Title = i % 5 == 0 ? LongTitle : $"第 {i} 件小事",
                        Detail = i % 4 == 0 ? LongChinese : "顺手记一下",
                        Done = i % 3 == 0,
                        ResultAt = i % 3 == 0 ? DateTime.UtcNow.AddHours(-1) : null
                    });
                return new DailyPlan { CharacterId = "worst", Outfit = LongTitle, Items = all };
            }

            var items = new List<PlanItem>
            {
                new() { Id = "p1", Hour = 7, Minute = 5, Kind = PlanKind.Wear, Title = LongTitle,
                        Detail = LongChinese, Done = true, ResultAt = DateTime.UtcNow.AddHours(-3) },
                new() { Id = "p2", Hour = 9, Minute = 0, Kind = PlanKind.Chat, Title = "", Detail = "" },
                new() { Id = "p3", Hour = 11, Minute = 30, Kind = PlanKind.Work, Title = "回消息",
                        Detail = "回 " + LongEmail },
                new() { Id = "p4", Hour = 12, Minute = 0, Kind = PlanKind.Eat, Title = "吃饭", Detail = "" },
                new() { Id = "p5", Hour = 14, Minute = 15, Kind = PlanKind.Social, Title = "见 " + LongName,
                        Detail = LongUrl },
                new() { Id = "p6", Hour = 18, Minute = 40, Kind = PlanKind.Move, Title = "去海边",
                        Detail = MixedScript },
                new() { Id = "p7", Hour = 21, Minute = 0, Kind = PlanKind.Mood, Title = "发呆", Detail = "……" },
                new() { Id = "p8", Hour = 23, Minute = 59, Kind = PlanKind.Sleep, Title = "睡觉", Detail = "" }
            };
            return new DailyPlan
            {
                CharacterId = "worst",
                Outfit = "灰毛衣 + 那条洗白了的牛仔裤（她说这样最好看）",
                Note = "今天风大",
                GeneratedByAi = true,
                Items = items
            };
        }

        public static List<OrganState> Organs(WorstCaseMode mode)
        {
            if (mode == WorstCaseMode.Empty) return new List<OrganState>();
            var all = new List<OrganState>
            {
                Organ(OrganKind.Stomach, 0.92, OrganStatus.Alert, "胃里有灼烧感", "食欲下降"),
                Organ(OrganKind.Heart, 0.71, OrganStatus.Warn, "心口发紧", "心跳偏快"),
                Organ(OrganKind.Lungs, 1.0, OrganStatus.Alert, "呼吸有点急", "说不了长句"),
                Organ(OrganKind.Muscle, 0.0, OrganStatus.Sleeping, "不想动", ""),
                Organ(OrganKind.Brain, 0.48, OrganStatus.Normal, "有点钝", ""),
                Organ(OrganKind.Gut, 0.33, OrganStatus.Normal, "肚子隐隐发紧", ""),
                Organ(OrganKind.Skin, 0.12, OrganStatus.Normal, "", ""),
                Organ(OrganKind.Endocrine, 0.66, OrganStatus.Active, "皮质醇偏高", "睡不深")
            };
            return mode == WorstCaseMode.One ? all.Take(1).ToList() : all;

            static OrganState Organ(OrganKind kind, double value, OrganStatus status, string symptom, string influence) => new()
            {
                Kind = kind,
                Value = value,
                Status = status,
                Symptoms = string.IsNullOrEmpty(symptom) ? Array.Empty<string>() : new[] { symptom, "已经持续三小时" },
                Influences = string.IsNullOrEmpty(influence) ? Array.Empty<string>() : new[] { influence },
                UpdatedAt = DateTime.UtcNow.AddMinutes(-7)
            };
        }

        public static List<OrganInfluence> Influences(WorstCaseMode mode)
        {
            if (mode == WorstCaseMode.Empty) return new List<OrganInfluence>();
            var all = new List<OrganInfluence>
            {
                new() { From = "脑", To = "胃", Effect = "反复想同一件事 → 胃酸上来 → 食欲下降（连锁会一路走到肌肉发软）", DelayMinutes = 45 },
                new() { From = "内分泌", To = "全身", Effect = "皮质醇偏高 → 睡不深 → 第二天更难集中", DelayMinutes = 240 },
                new() { From = "心", To = "肺", Effect = "心口发紧 → 呼吸变浅 → 说不了长句", DelayMinutes = 5 },
                new() { From = "胃", To = "肠", Effect = "灼烧感 → 肚子隐隐发紧", DelayMinutes = 60 },
                new() { From = "肌肉", To = "全身", Effect = "不想动 → 越躺越沉", DelayMinutes = 120 },
                new() { From = "皮肤", To = "全身", Effect = "有点发痒 → 抓了之后更痒", DelayMinutes = 30 }
            };
            return mode == WorstCaseMode.One ? all.Take(1).ToList() : all;
        }

        public static List<MemoryOverviewService.LayerStat> Layers(WorstCaseMode mode)
        {
            var (working, human, semantic, vault, archive) = mode switch
            {
                WorstCaseMode.Empty => (0, 0, 0, 0, 0),
                WorstCaseMode.One => (1, 1, 1, 1, 1),
                WorstCaseMode.Worst => (1284, 9999, -1, 128, 9999),
                _ => (99999, 999999, 99999, 99999, 99999)
            };
            return new List<MemoryOverviewService.LayerStat>
            {
                Layer(MemoryLayer.Working, working, "条消息", "活跃会话 128 个 · 记忆条数设置 9999"),
                Layer(MemoryLayer.Human, human, "条", "其中照片 9999 张 · 重要时刻 9999 条 · 回想窗口 9999 天"),
                Layer(MemoryLayer.Semantic, semantic, "条联系", "最紧的：" + LongName + "↔" + LongEmail + "、" + ArabicName + "↔" + OneCharName),
                Layer(MemoryLayer.Vault, vault, "条记录", "标签 128 个：" + WideTag + "、" + WideTag + "-v2"),
                Layer(MemoryLayer.Archive, archive, "篇日记", "最近一篇 12-31")
            };

            static MemoryOverviewService.LayerStat Layer(MemoryLayer layer, int count, string unit, string detail) =>
                new(layer, MemoryLayers.Name(layer), MemoryLayers.Describe(layer), count, unit, detail);
        }

        public static List<HumanMemoryEntry> Memories(WorstCaseMode mode)
        {
            if (mode == WorstCaseMode.Empty) return new List<HumanMemoryEntry>();
            if (mode == WorstCaseMode.One)
                return new List<HumanMemoryEntry> { Entry("hm-1", HumanMemoryKind.Event, "她说那件灰毛衣穿着刚好。", 3) };
            if (mode == WorstCaseMode.Huge)
            {
                var many = new List<HumanMemoryEntry>();
                for (var i = 0; i < 400; i++)
                    many.Add(Entry($"hm-{i}", (HumanMemoryKind)(i % 4), i % 9 == 0 ? LongChinese : $"第 {i} 件事：她把伞忘在了便利店的门口。", i % 5));
                return many;
            }
            return new List<HumanMemoryEntry>
            {
                Entry("hm-a", HumanMemoryKind.Event, LongChinese, 128),
                Entry("hm-b", HumanMemoryKind.Image, "那天她发来一张只有一半的照片，剩下的一半她说不许看。", 0) with
                {
                    ImagePath = Path.Combine(App.RootDirectory, "memory", "half.jpg")
                },
                Entry("hm-c", HumanMemoryKind.Abstraction, "她其实很怕黑，但从来不说。", 2) with
                {
                    Tags = new List<string> { WideTag, WideTag + "-v2", "怕黑", "晚上", "灯", "便利店", "伞", "雨", "旧事", "小时候", "停电", "走廊" }
                },
                Entry("hm-d", HumanMemoryKind.Important, "", 0),
                Entry("hm-e", HumanMemoryKind.Event, MixedScript, 7),
                Entry("hm-f", HumanMemoryKind.Event, "她记错了，其实是周四。", -1)
            };

            static HumanMemoryEntry Entry(string id, HumanMemoryKind kind, string text, int recall) => new()
            {
                Id = id,
                CharacterId = "worst",
                Kind = kind,
                Text = text,
                RecallCount = recall,
                Importance = 0.5,
                CreatedAt = DateTime.UtcNow.AddDays(-3),
                Tags = new List<string> { "记得" }
            };
        }

        public static List<VectorPoint> Vectors(WorstCaseMode mode)
        {
            if (mode == WorstCaseMode.Empty) return new List<VectorPoint>();
            var points = new List<VectorPoint>
            {
                new("v1", Clip(LongName), -1, 1, -0.999, 1),
                new("v2", Clip(LongEmail), 0.999, -1, 1, 0.87),
                new("v3", OneCharName, 0, 0, 0, 0.12),
                new("v4", Clip(MixedScript), 0.5, 0.5, 0.5, 0.33)
            };
            return mode == WorstCaseMode.One ? points.Take(1).ToList() : points;

            static string Clip(string text) => text.Length <= 14 ? text : text[..14] + "…";
        }
    }

    // ==================== 回忆录 / 记得的事 / 日记 / CG ====================

    public static class Gallery
    {
        public static List<MemoryOverviewService.LayerStat> LayerStats(WorstCaseMode mode) => Monitor.Layers(mode);

        public static List<HumanMemoryEntry> HumanMemories(WorstCaseMode mode) => Monitor.Memories(mode);

        public static List<MemoryTag> Tags(WorstCaseMode mode)
        {
            if (mode == WorstCaseMode.Empty) return new List<MemoryTag>();
            if (mode == WorstCaseMode.One) return new List<MemoryTag> { Tag(WideTag, 1) };
            if (mode == WorstCaseMode.Huge)
                return Enumerable.Range(0, 128).Select(i => Tag(i % 4 == 0 ? $"{WideTag}-{i:000}" : $"标签{i:000}", i)).ToList();
            return new List<MemoryTag>
            {
                Tag(WideTag, 128), Tag(WideTag + "-v2", 9999), Tag("怕黑", 1),
                Tag("晚上", 0), Tag("", 3), Tag(MixedScript, 2)
            };

            static MemoryTag Tag(string name, int count) => new() { Name = name, Count = count };
        }

        public static List<LinkView> Links(WorstCaseMode mode)
        {
            if (mode == WorstCaseMode.Empty) return new List<LinkView>();
            if (mode == WorstCaseMode.One) return new List<LinkView> { new(OneCharName, "灯", 1, "auto", RelationKind.Associated) };
            var all = new List<LinkView>
            {
                new(LongName, LongEmail, 9999, "ai", RelationKind.Associated),
                new(ArabicName, OneCharName, 128, "human", RelationKind.Equivalent),
                new("灰毛衣", "海边", 72, "auto", RelationKind.Spatial),
                new(WideTag, "怕黑", 44, "ai", RelationKind.Emotional),
                new("伞", "便利店", 12, "auto", RelationKind.Temporal),
                new("停电", "走廊", 8, "auto", RelationKind.Causal),
                new("周四", "周五", 3, "human", RelationKind.Contrasts),
                new(MixedScript, VietnameseName, 1, "ai", RelationKind.Associated)
            };
            return all;
        }

        public static List<MemoryEntry> Affection(WorstCaseMode mode)
        {
            if (mode == WorstCaseMode.Empty) return new List<MemoryEntry>();
            if (mode == WorstCaseMode.One)
                return new List<MemoryEntry> { Aff(1, "互动") };
            if (mode == WorstCaseMode.Huge)
                return Enumerable.Range(1, 300).Select(i => Aff(i, i % 6 == 0 ? WideTag : "聊天")).ToList();
            return new List<MemoryEntry>
            {
                Aff(9999, WideTag),
                Aff(-1, ""),
                Aff(128, LongEmail),
                Aff(0, MixedScript),
                Aff(5, null)
            };

            static MemoryEntry Aff(int weight, string? keywords) => new()
            {
                CharacterId = "worst",
                Content = LongChinese,
                Keywords = keywords,
                Weight = weight,
                At = DateTime.UtcNow.AddDays(-2)
            };
        }

        public static List<MemoryEntry> Memories(WorstCaseMode mode)
        {
            if (mode == WorstCaseMode.Empty) return new List<MemoryEntry>();
            if (mode == WorstCaseMode.One)
                return new List<MemoryEntry> { Dialogue("m1", "她说那件灰毛衣穿着刚好。") };
            if (mode == WorstCaseMode.Huge)
                return Enumerable.Range(0, 1000)
                    .Select(i => Dialogue($"m{i}", i % 11 == 0 ? LongChinese : $"第 {i} 轮：她还记得我上次说的那句话。"))
                    .ToList();
            return new List<MemoryEntry>
            {
                Dialogue("m1", LongChinese),
                Dialogue("m2", LongUrl),
                Dialogue("m3", ""),
                Dialogue("m4", MixedScript),
                Dialogue("m5", new string('啊', 220) + "——好了别说了。"),
                Dialogue("m6", "【CG:雨夜_车站.png】"),
                Dialogue("m7", "还记着：\"她说那是最后一次\"。"),
                Dialogue("m8", LongFileName)
            };

            static MemoryEntry Dialogue(string id, string content) => new()
            {
                Id = id,
                CharacterId = "worst",
                Content = content,
                At = DateTime.UtcNow.AddHours(-3)
            };
        }

        public static List<DiaryNote> Diary(WorstCaseMode mode)
        {
            if (mode == WorstCaseMode.Empty) return new List<DiaryNote>();
            if (mode == WorstCaseMode.One) return new List<DiaryNote> { Day(DateTime.Today, "平静", "今天没什么事。") };
            if (mode == WorstCaseMode.Huge)
                return Enumerable.Range(0, 365)
                    .Select(i => Day(DateTime.Today.AddDays(-i), i % 3 == 0 ? "低落" : "平静", LongChinese))
                    .ToList();
            return new List<DiaryNote>
            {
                Day(DateTime.Today, "平静", LongChinese),
                Day(DateTime.Today.AddDays(-1), "", "只写了两个字：还好。"),
                Day(DateTime.Today.AddDays(-12), "有点难受但说不清", MixedScript),
                // 跨年：日期格式「yyyy年M月d日」要在旧年份上也读得通，而不是只剩月日。
                Day(new DateTime(DateTime.Today.Year - 1, 12, 31), "跨年", LongChinese),
                Day(default, "", "")
            };

            static DiaryNote Day(DateTime date, string mood, string content) => new()
            {
                CharacterId = "worst",
                Date = date,
                Mood = mood,
                Content = content
            };
        }

        public static List<CgRecord> Cgs(WorstCaseMode mode)
        {
            if (mode == WorstCaseMode.Empty) return new List<CgRecord>();
            if (mode == WorstCaseMode.One) return new List<CgRecord> { Cg("cg1", OneCharName) };
            if (mode == WorstCaseMode.Huge)
                return Enumerable.Range(1, 120).Select(i => Cg($"cg{i}", i % 7 == 0 ? LongTitle : $"第 {i} 张")).ToList();
            return new List<CgRecord>
            {
                Cg("cg1", LongTitle),
                Cg("cg2", ""),
                Cg("cg3", MixedScript),
                Cg("cg4", LongFileName),
                Cg("cg5", new string('长', 60))
            };

            static CgRecord Cg(string id, string title) => new()
            {
                Id = id,
                CharacterId = "worst",
                Title = title,
                File = $"worst/{id}.png",
                Unlocked = true
            };
        }
    }

    // ==================== 关系网 ====================

    public static class Graph
    {
        public static RelationGraph Build(WorstCaseMode mode)
        {
            var graph = new RelationGraph();
            if (mode == WorstCaseMode.Empty) return graph;

            if (mode == WorstCaseMode.One)
            {
                Add(graph, NodeKind.Word, OneCharName);
                return graph;
            }

            if (mode == WorstCaseMode.Huge)
            {
                for (var i = 0; i < 480; i++)
                    Add(graph, (NodeKind)(i % 9), i % 40 == 0 ? LongName + " " + i : $"概念 {i:0000}");
                var ids = graph.Nodes.Select(n => n.Id).ToList();
                for (var i = 0; i < 1200; i++)
                    Link(graph, ids[i % ids.Count], ids[(i * 7 + 3) % ids.Count], 1 + i % 9, (RelationKind)(i % 8));
                return graph;
            }

            var nodes = new[]
            {
                Add(graph, NodeKind.Person, LongName),
                Add(graph, NodeKind.Word, OneCharName),
                Add(graph, NodeKind.Text, LongEmail),
                Add(graph, NodeKind.Word, ArabicName),
                Add(graph, NodeKind.Word, EmojiFirstName),
                Add(graph, NodeKind.Word, LongTitle),
                Add(graph, NodeKind.Memory, "她说那件灰毛衣穿着刚好，缩水了也没换"),
                Add(graph, NodeKind.Tag, WideTag),
                Add(graph, NodeKind.Place, "海边那条要爬很久的栈道入口"),
                Add(graph, NodeKind.Emotion, "怕黑"),
                Add(graph, NodeKind.Thing, LongFileName),
                Add(graph, NodeKind.Word, MixedScript),
                Add(graph, NodeKind.Word, VietnameseName),
                Add(graph, NodeKind.Word, "伞"),
                Add(graph, NodeKind.Word, "便利店"),
                Add(graph, NodeKind.Word, "停电")
            };
            for (var i = 0; i < nodes.Length; i++)
            {
                Link(graph, nodes[i], nodes[(i + 1) % nodes.Length], 1 + i * 3, RelationKind.Associated);
                Link(graph, nodes[i], nodes[(i * 5 + 2) % nodes.Length], 0.5 + i, (RelationKind)(i % 8));
            }
            return graph;
        }

        /// <summary>片名条：按真实的聚类算，长名字原样进胶囊。</summary>
        public static List<RelationNet> Nets(RelationGraph graph) => RelationNets.Cluster(graph);

        private static string Add(RelationGraph graph, NodeKind kind, string label)
        {
            var id = RelationIds.For(kind, label);
            graph.Nodes.Add(new RelationNode
            {
                Id = id,
                Label = label,
                Kind = kind,
                Salience = 0.4,
                Source = kind == NodeKind.Person ? "human" : "auto"
            });
            return id;
        }

        private static void Link(RelationGraph graph, string from, string to, double weight, RelationKind kind)
        {
            if (from == to) return;
            graph.Edges.Add(new RelationEdge { From = from, To = to, Kind = kind, Weight = weight });
        }
    }

    // ==================== 商店（美了么） ====================

    /// <summary>
    /// 货架与钱包。价格取「能录进来的极值」：9999999（AI/仓库商品没有上限）与 -1（坏 JSON / 手改账本），
    /// 品类留空（老同步商品没有 category）、名字留空、图片指向一个不存在的本地缓存（清缓存后留下的悬空路径）。
    /// </summary>
    public static class Shop
    {
        private static string MissingImage(string name) => Path.Combine(App.RootDirectory, "ShopImages", name);

        public static string Wallet(WorstCaseMode mode) => mode switch
        {
            WorstCaseMode.Empty => "亲密币 0",
            WorstCaseMode.One => "亲密币 1",
            WorstCaseMode.Worst => "亲密币 99999999",
            _ => "亲密币 2147483647"
        };

        public static string RecordLabel(WorstCaseMode mode) => mode switch
        {
            WorstCaseMode.Empty => "小游戏战绩：0 胜 0 负",
            WorstCaseMode.One => "小游戏战绩：1 胜 0 负",
            WorstCaseMode.Worst => "小游戏战绩：9999 胜 9998 负",
            _ => "小游戏战绩：12848 胜 12847 负"
        };

        public static string SyncRepo(WorstCaseMode mode) => mode switch
        {
            WorstCaseMode.Empty => "",
            WorstCaseMode.Worst => "northwind-industries-holdings/enterprise-onboarding-shop-catalog-mirror",
            _ => "demo-user/my-shop"
        };

        public static string BodyStatus(WorstCaseMode mode) => mode switch
        {
            WorstCaseMode.Empty => "",
            WorstCaseMode.Worst => LongChinese + "\n" + LongUrl,
            _ => "精力 0/100 · 好感 -1 · 胃里有灼烧感，已经持续三小时；皮质醇偏高，睡不深"
        };

        /// <summary>生效中的 buff 标记：40 条长名字，用来检查「小雨的标记：…」那条单行提示。</summary>
        public static List<CharacterBuff> Buffs(WorstCaseMode mode)
        {
            if (mode == WorstCaseMode.Empty) return new List<CharacterBuff>();
            if (mode == WorstCaseMode.One)
                return new List<CharacterBuff> { Buff(0, "喝了奶茶") };
            if (mode == WorstCaseMode.Huge)
                return Enumerable.Range(0, 120).Select(i => Buff(i, $"标记 {i:000}")).ToList();
            return Enumerable.Range(0, 40)
                .Select(i => Buff(i, (i % 3) switch
                {
                    0 => WideTag,
                    1 => "喝了" + LongTitle + "同一家的奶茶",
                    _ => ArabicName + "的护身符"
                }))
                .ToList();

            static CharacterBuff Buff(int i, string name) => new()
            {
                Id = "buff-" + i,
                Name = name,
                Emoji = i % 4 == 0 ? "" : "🧋",
                Desc = LongChinese,
                Source = "送礼",
                TurnsLeft = 9999 - i,
                AppliedAt = DateTime.Now.AddMinutes(-i)
            };
        }

        public static List<ShopItem> Catalog(WorstCaseMode mode)
        {
            if (mode == WorstCaseMode.Empty) return new List<ShopItem>();
            if (mode == WorstCaseMode.One) return new List<ShopItem> { Item("one-1", "白开水", "饮品", "", 1) };
            if (mode == WorstCaseMode.Huge)
                return Enumerable.Range(0, 1000)
                    .Select(i => Item($"huge-{i}",
                        i % 9 == 0 ? LongTitle + i : $"集团采购品 {i:0000}",
                        i % 5 == 0 ? WideTag : "杂货",
                        i % 7 == 0 ? LongChinese : $"第 {i} 件：店里放的，随手能拿。",
                        i % 11 == 0 ? 9999999 : 20 + i % 900,
                        owned: i % 4, ai: i % 13 == 0))
                    .ToList();

            return new List<ShopItem>
            {
                // 一屏能看到的头几行，一行一种坏法。
                Item("worst-a", LongName, "饮品", LongChinese + "\n" + LongUrl, 9999999,
                    emoji: "🧋", merchant: LongTitle, image: MissingImage("worst-a.png"), owned: 9999),
                Item("worst-b", "白开水", "", "", -1, emoji: "", merchant: ""),
                Item("worst-c", "", "甜品", "只有描述没有名字（同步商品缺 name）", 0, emoji: "🍮"),
                Item("worst-d", MixedScript, WideTag, MixedScript, 128, emoji: "🍡"),
                Item("worst-e", OneCharName, "杂货", LongEmail, 66, emoji: "🧦"),
                Item("worst-f", "游乐园门票（双人 · 周末限定 · 需提前一天预约）", "场景券",
                    LongChinese, 999, emoji: "🎡", ticket: true, scene: LongTitle, location: LongTitle + "入口"),
                Item("worst-g", ArabicName, "药", "", 5, emoji: "💊", ai: true),
                Item("worst-h", new string('长', 60), "节日限定 · 跨年 · 情人节 · 七夕 · 圣诞 · 周年庆",
                    new string('好', 300), 520, emoji: "🎁", merchant: WideTag),
                // 远程图（下载中 / 下载前的状态）：HasImage 为真、本地没有文件，必须仍然显示这张图。
                Item("worst-i", "小雨的旧毛衣", "礼品", "缩水了，但穿着刚好。", 520,
                    emoji: "🧶", image: "https://raw.githubusercontent.com/x/y/main/shop/old-sweater.png")
            };

            static ShopItem Item(string id, string name, string category, string desc, int price,
                string emoji = "🎁", string merchant = "", string image = "", int owned = 0,
                bool ticket = false, string scene = "", string location = "", bool ai = false) => new()
            {
                Id = id,
                Name = name,
                Category = category,
                Desc = desc,
                Price = price,
                Emoji = emoji,
                Merchant = merchant,
                ImagePath = image,
                Owned = owned,
                IsTicket = ticket,
                TicketScene = scene,
                TicketLocation = location,
                AiGenerated = ai,
                Source = ai ? "ai" : image.Length > 0 ? "github" : "builtin"
            };
        }
    }

    // ==================== 存档管理 / 分支树（档案） ====================

    /// <summary>
    /// 存档列表与分支树共用同一份种子：名字超长 / 全是长句 / 空名字 / 场景空 / 角色空 / 日期跨年 /
    /// 父档指向一个已经不存在的档（删档后留下的孤儿分支）。
    /// </summary>
    public static class Archive
    {
        public static List<(SaveSlot Slot, string CharacterName)> Slots(WorstCaseMode mode)
        {
            if (mode == WorstCaseMode.Empty) return new List<(SaveSlot, string)>();
            if (mode == WorstCaseMode.One) return new List<(SaveSlot, string)> { (Make("one-1", "第一档", "卧室", Now(-1), null), OneCharName) };
            if (mode == WorstCaseMode.Huge)
                return Enumerable.Range(0, 200)
                    .Select(i => (Make($"huge-{i}",
                            i % 17 == 0 ? LongTitle + " " + i : $"第 {i:000} 档",
                            i % 13 == 0 ? LongChinese : "海边栈道",
                            Now(-i * 9),
                            i == 0 ? null : $"huge-{i - 1}",
                            i % 6 == 0 ? "分支 " + i : ""),
                        i % 7 == 0 ? LongName : "小雨"))
                    .ToList();

            var worst = new List<(SaveSlot, string)>
            {
                (Make("worst-a", LongTitle, MixedScript, Now(-3 * 60 * 24 * 400), null,
                    "主线 · " + LongTitle), LongName),
                (Make("worst-b", "", "", Now(-1), "worst-a"), "未知角色"),
                (Make("worst-c", "再来一次", LongEmail, Now(0), "worst-a", ""), EmojiFirstName),
                (Make("worst-d", WideTag, LongUrl, Now(-60 * 24 * 500), "gone-save-id", WideTag), ArabicName),
                (Make("worst-e", new string('存', 80), "", default, null), VietnameseName),
                (Make("worst-f", "跨年那一档", "旧年的房间", Now(-60 * 24 * 800), null), LongTitle),
                (Make("worst-g", MixedScript, MixedScript, Now(60 * 24 * 40), null), "未知角色")
            };
            return worst;

            static SaveSlot Make(string id, string label, string scene, DateTime at, string? parent, string branch = "") => new()
            {
                Id = id,
                Label = label,
                Scene = scene,
                SavedAt = at,
                Character = "worst",
                ParentId = parent,
                BranchName = branch,
                ForkedAt = parent is null ? null : at
            };

            static DateTime Now(int minutes) => DateTime.UtcNow.AddMinutes(minutes);
        }
    }

    // ==================== 设置 · 用量与单价 ====================

    /// <summary>
    /// 用量账本的极值：模型名长到铺满一行、带中文、带「（未标记）」、花费七位数、
    /// 单价一万以下的小数、调用 0 次的模型、跨年 30 天时间序列。
    /// </summary>
    public static class Usage
    {
        /// <summary>留空的单价档 = 只统计 token；最坏档给「有价」这一路，好让金额分支也被走到。</summary>
        public static bool HasPrices(WorstCaseMode mode) => mode != WorstCaseMode.Empty;

        public static CostEstimatorService.UsageOverview Overview(WorstCaseMode mode)
        {
            if (mode == WorstCaseMode.Empty)
                return new(new(0, 0, 0, 0, 0, 0), new(0, 0, 0, 0, 0, 0), new(0, 0, 0, 0, 0, 0));
            if (mode == WorstCaseMode.One)
                return new(new(1, 12, 340, 0, 0, 0.0001), new(1, 12, 340, 0, 0, 0.0001), new(1, 12, 340, 0, 0, 0.0001));
            if (mode == WorstCaseMode.Huge)
                return new(new(99999, 999999999, 999999999, 99999999, 99999999, 99999.9999),
                    new(999999, 9999999999, 9999999999, 999999999, 999999999, 999999.9999),
                    new(2147483647, 999999999999, 999999999999, 99999999999, 99999999999, 9999999.9999));
            return new(
                new(1284, 9_876_543, 1_234_567, 8_765_432, 234_567, 1234.5678),
                new(9999, 88_888_888, 9_999_999, 77_777_777, 1_999_999, 9999.9999),
                new(99999, 999_999_999, 99_999_999, 888_888_888, 19_999_999, 123456.7891));
        }

        public static List<CostEstimatorService.ModelUsageRow> Models(WorstCaseMode mode)
        {
            if (mode == WorstCaseMode.Empty) return new();
            if (mode == WorstCaseMode.One)
                return new() { new CostEstimatorService.ModelUsageRow("gpt-4o-mini", 1, 12, 340, 0, 0, 0.0001) };
            if (mode == WorstCaseMode.Huge)
                return Enumerable.Range(0, 60)
                    .Select(i => new CostEstimatorService.ModelUsageRow($"vendor-{i:00}/model-with-a-long-deployment-suffix-{i}",
                        1 + i * 37, 1000L * i, 500L * i, 400L * i, 90L * i, i * 13.37))
                    .ToList();
            return new()
            {
                new CostEstimatorService.ModelUsageRow("openai/gpt-6.2-astra-with-a-very-long-deployment-name-2026-11-22",
                    12848, 123_456_789, 9_876_543, 111_111_111, 1_234_567, 123456.7891),
                new CostEstimatorService.ModelUsageRow("（未标记）", 3, 120, 30, 0, 0, 0.0004),
                new CostEstimatorService.ModelUsageRow("", 1, 4, 1, 0, 0, 0),
                new CostEstimatorService.ModelUsageRow("deepseek-v4-pro", 99999, 999_999_999, 99_999_999, 888_888_888, 19_999_999, 0.0001),
                new CostEstimatorService.ModelUsageRow("نموذج-عربي-طويل-الاسم", 42, 1_000, 2_000, 500, 0, 0.5)
            };
        }

        public static List<CostEstimatorService.DayUsageRow> Days(WorstCaseMode mode, int days)
        {
            var today = DateTime.Now.Date;
            var rows = new List<CostEstimatorService.DayUsageRow>();
            for (var i = 0; i < days; i++)
            {
                var day = today.AddDays(-i);
                var calls = mode switch
                {
                    WorstCaseMode.Empty => 0,
                    WorstCaseMode.One => 1,
                    WorstCaseMode.Huge => 99999,
                    _ => (i * 137) % 9999
                };
                rows.Add(new(day, calls, calls * 1000L, calls * 300L, calls * 900L, calls * 20L, calls * 0.0123));
            }
            return rows;
        }

        public static List<CostEstimatorService.HeatBucket> Heat(WorstCaseMode mode)
        {
            var cells = new List<CostEstimatorService.HeatBucket>(168);
            for (var wd = 0; wd < 7; wd++)
                for (var h = 0; h < 24; h++)
                {
                    var calls = mode switch
                    {
                        WorstCaseMode.Empty => 0,
                        WorstCaseMode.One => wd == 0 && h == 9 ? 1 : 0,
                        WorstCaseMode.Huge => 99999,
                        _ => (wd * 31 + h * 7) % 900
                    };
                    cells.Add(new(wd, h, calls * 1000L, calls));
                }
            return cells;
        }

        public static List<CostEstimatorService.CharacterUsageRow> Characters(WorstCaseMode mode)
        {
            if (mode == WorstCaseMode.Empty) return new();
            if (mode == WorstCaseMode.One) return new() { new("小雨", "worst", 1, 12, 340, 0, 0, 0.0001) };
            if (mode == WorstCaseMode.Huge)
                return Enumerable.Range(0, 80)
                    .Select(i => new CostEstimatorService.CharacterUsageRow($"角色 {i:00} · 第三事业部", $"id-{i}", i * 13, i * 1000L, i * 300L, 0, 0, i * 0.7))
                    .ToList();
            return new()
            {
                new(LongName, "worst", 12848, 123_456_789, 9_876_543, 111_111_111, 1_234_567, 123456.7891),
                new("（未标记）", "", 12, 1_000, 200, 0, 0, 0.3),
                new("", "", 1, 4, 1, 0, 0, 0),
                new(ArabicName, "worst", 99999, 999_999, 9_999, 0, 0, 0.0001),
                new(LongTitle, "worst", 42, 1_000, 2_000, 500, 0, 0.5)
            };
        }

        public static List<ModelPrice> Prices(WorstCaseMode mode)
        {
            if (mode is WorstCaseMode.Empty or WorstCaseMode.One)
                return new() { new ModelPrice { Model = "gpt-4o-mini", InputPerMillion = 0.15, OutputPerMillion = 0.6 } };
            return new()
            {
                new ModelPrice
                {
                    Model = "openai/gpt-6.2-astra-with-a-very-long-deployment-name-2026-11-22",
                    InputPerMillion = 9999999, OutputPerMillion = 0.0001,
                    CachedInputPerMillion = 1234567.8912, CachedOutputPerMillion = 0
                },
                // 只录了名字、四个价全 0：价格列必须显示空格子，而不是「0」
                new ModelPrice { Model = "只录了名字的模型-" + WideTag }
            };
        }
    }

    // ==================== 设置 · 能力包（Skills） ====================

    public static class Settings
    {
        /// <summary>30 个能力包，名字长短差十倍：设置页那条「已装 N 个，启用 M 个：…」会一次性铺满。</summary>
        public static List<SkillLibrary.SkillInfo> Skills(WorstCaseMode mode)
        {
            if (mode == WorstCaseMode.Empty) return new List<SkillLibrary.SkillInfo>();
            if (mode == WorstCaseMode.One)
                return new List<SkillLibrary.SkillInfo> { Skill("diary", "写日记", "把一天收成一篇日记。", true) };
            if (mode == WorstCaseMode.Huge)
                return Enumerable.Range(0, 120)
                    .Select(i => Skill($"skill-{i:000}", $"能力包 {i}", LongChinese, i % 3 != 0))
                    .ToList();
            return new List<SkillLibrary.SkillInfo>
            {
                Skill("break-ui", "把界面压到最坏", "喂超长名字、空值、极端计数，看哪里先坏。", true),
                Skill(WideTag, "企业上手引导", LongChinese, true),
                Skill("diary", "写日记", "把一天收成一篇日记。", false),
                Skill("一", "只有一个字", "", true),
                Skill("نموذج-عربي", "阿拉伯语能力包", MixedScript, true),
                Skill("🦊-fox", "emoji 开头的名字", "emoji 开头的能力包名在列表里是怎样排的。", false),
                Skill("very-long-category-name-for-a-skill-that-does-something-quite-specific", "长名字",
                    LongTitle + LongEmail, true),
                Skill("map", "地图", "离线地图与地点。", true)
            };

            static SkillLibrary.SkillInfo Skill(string name, string title, string desc, bool enabled) =>
                new(name, title, desc, Path.Combine(App.RootDirectory, "skills", name), true, name.Length % 2 == 0, enabled);
        }
    }

    // ==================== 角色库 ====================

    /// <summary>
    /// 角色名册：超长名字 / 空名字（CharacterMask 出「？？？」）/ 单字名 / emoji 开头 / 阿拉伯名 /
    /// 缺头像和立绘 / 超长性格与背景 / 好感越界（9999、-1）/ 一个角色挂在 128 份存档里。
    /// </summary>
    public static class Characters
    {
        public static List<(CharacterData Data, int SaveCount)> Roster(WorstCaseMode mode)
        {
            if (mode == WorstCaseMode.Empty) return new List<(CharacterData, int)>();
            if (mode == WorstCaseMode.One)
                return new List<(CharacterData, int)> { (Make("one-1", OneCharName, "话不多。", "", 12, 0, ""), 0) };
            if (mode == WorstCaseMode.Huge)
                return Enumerable.Range(0, 60)
                    .Select(i => (Make($"huge-{i}",
                        i % 9 == 0 ? LongTitle : $"第 {i:00} 位", i % 4 == 0 ? LongChinese : "温和", LongChinese,
                        i % 5, i % 7, avatar: i % 3 == 0 ? "" : MissingAvatar($"huge-{i}.png")), i % 4))
                    .ToList();

            return new List<(CharacterData, int)>
            {
                (Make("worst-a", LongName, LongTitle + LongChinese, LongChinese, 9999, 12,
                    MissingAvatar("worst-a.png")), 128),
                (Make("worst-b", "", "", "", 0, 0, ""), 0),
                (Make("worst-c", EmojiFirstName, "话少，但每句都记着。", MixedScript, -1, 3, ""), 1),
                (Make("worst-d", ArabicName, LongEmail, LongEmail, 100, 1, MissingAvatar("worst-d.png")), 9999),
                (Make("worst-e", OneCharName, "嗯", "", 50, 0, ""), 0),
                (Make("worst-f", new string('名', 60), new string('性', 300), new string('背', 400), 72, 240,
                    MissingAvatar("worst-f.png")), 12),
                (Make("worst-g", VietnameseName, "Đặng Thị Ngọc Hân", "", 88, 0, ""), 3),
                (Make("worst-h", LongTitle, "", "", 1, 0, ""), 0)
            };

            static CharacterData Make(string id, string name, string personality, string description,
                int affection, int sprites, string avatar)
            {
                // 格子数与「有图的格子」一致：路径可以指向不存在的立绘 —— CellCount 数的是条目，不是文件。
                var map = new Dictionary<string, string>();
                for (var i = 0; i < sprites; i++)
                    map[$"sprite_{i}"] = $"assets/characters/{id}/sprite_{i}-standing.png";
                return new CharacterData
                {
                    Introduced = true,
                    Avatar = avatar,
                    SpriteMap = map,
                    Profile = new CharacterProfile
                    {
                        Id = id,
                        Name = name,
                        Gender = "女",
                        Personality = personality,
                        Description = description,
                        Catchphrase = MixedScript,
                        UserAddress = "主人"
                    },
                    State = new CharacterState { Affection = affection, Trust = 0, Energy = 0 }
                };
            }

            static string MissingAvatar(string name) => Path.Combine(App.RootDirectory, "avatars", name);
        }
    }
}
