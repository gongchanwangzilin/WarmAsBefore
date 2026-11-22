using WarmAsBefore.Drawables;
using WarmAsBefore.Models;

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
                Day(DateTime.Today.AddDays(-12), "有点难受但说不清", MixedScript)
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
}
