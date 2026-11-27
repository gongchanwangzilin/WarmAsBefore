using System.Text;
using WarmAsBefore.Helpers;
using WarmAsBefore.Services;

// 聊天记录导入的纯函数自测：嗅探、解析、归一化、归属与初始关系。
// 与 tools/rules_selftest 同一约定：断言失败即非零退出。
int passed = 0;
var failures = new List<string>();

void Check(bool ok, string name)
{
    if (ok) passed++;
    else failures.Add(name);
}

void Eq(int actual, int expect, string name) => Check(actual == expect, $"{name} (got {actual}, want {expect})");
void Text(string actual, string expect, string name) => Check(actual == expect, $"{name} (got {actual}, want {expect})");

// ---------- 1. 微信 txt：时间+昵称抬头，正文可多行 ----------
const string wechat = """
2023-05-01 20:14:00 晚晚
今天下雨了，你带伞了吗

2023-05-01 20:16:30 林晚
带了，在包里
2023-05-01 20:17:02 晚晚
那就好
""";
var w = ChatLogParser.Parse(wechat, "wechat.txt");
Check(w.Ok, "微信 txt 解析成功");
Eq(w.Messages.Count, 3, "微信 txt 三条消息");
Text(w.Messages[0].Speaker, "晚晚", "微信 txt 第一条发言者");
Text(w.Messages[1].Speaker, "林晚", "微信 txt 第二条发言者");
Check(w.Messages[0].At?.Hour == 20, "微信 txt 带出时刻");
Check(w.Format == ChatLogFormat.WeChat, "微信 txt 识别为微信格式");
Check(w.Messages[0].Text.Contains("下雨"), "微信 txt 正文保留");

// ---------- 2. QQ txt：信封行、昵称(QQ号)、时间 ----------
const string qq = """
消息记录（此消息记录为文本格式，不支持重新导入）

================================================================
消息分组:messages
================================================================
消息对象:晚晚

================================================================

2023-05-02 09:00:00 晚晚(12345678)
早
2023-05-02 09:01:00 林晚(87654321)
早呀
""";
var q = ChatLogParser.Parse(qq, "qq.txt");
Check(q.Ok, "QQ txt 解析成功");
Eq(q.Messages.Count, 2, "QQ txt 两条消息");
Text(q.Messages[0].Speaker, "晚晚", "QQ 号从昵称里剥掉");
Check(q.Format == ChatLogFormat.QqText, "QQ txt 识别为 QQ 格式");

// ---------- 3. CSV：表头 + 引号里的逗号 ----------
const string csv = """
时间,发送者,内容
2023-05-03 10:00:00,晚晚,"我们去看海吧，好吗"
2023-05-03 10:01:00,林晚,走
""";
var c = ChatLogParser.Parse(csv, "chat.csv");
Check(c.Ok, "CSV 解析成功");
Eq(c.Messages.Count, 2, "CSV 两条消息");
Check(c.Messages[0].Text.Contains("，"), "CSV 引号里的逗号没被切开");
Text(c.Messages[0].Speaker, "晚晚", "CSV 发送者列");

// ---------- 4. CSV 无表头 ----------
const string csvNoHead = """
2023-05-04 08:00:00,Nana,起床啦
2023-05-04 08:05:00,Kai,再睡五分钟
""";
var cn = ChatLogParser.Parse(csvNoHead, "plain.csv");
Check(cn.Ok && cn.Messages.Count == 2, "CSV 无表头按列序读");

// ---------- 5. JSON 数组 ----------
const string jsonArray = """
[
  {"time":"2023-05-05 12:00:00","sender":"晚晚","content":"吃饭了吗"},
  {"time":"2023-05-05 12:01:00","from":"林晚","text":"刚吃完"}
]
""";
var ja = ChatLogParser.Parse(jsonArray);
Check(ja.Ok && ja.Format == ChatLogFormat.Json, "JSON 数组解析");
Eq(ja.Messages.Count, 2, "JSON 数组两条");
Text(ja.Messages[1].Speaker, "林晚", "JSON from 字段当发言者");

// ---------- 6. JSON {messages:[...]} + 毫秒时间戳 ----------
var jsonObj = """
{"messages":[
  {"timestamp":1683273600000,"name":"A","message":"一"},
  {"timestamp":1683273660000,"name":"B","message":"二"}
]}
""";
var jo = ChatLogParser.Parse(jsonObj);
Check(jo.Ok && jo.Messages.Count == 2, "JSON messages 包装解析");
Check(jo.Messages[0].At is not null, "JSON 时间戳转成时间");

// ---------- 7. Markdown ----------
const string md = """
# 我们的聊天

**晚晚** 2023-05-06 21:00:00
晚安
**林晚** 2023-05-06 21:00:30
晚安，做个好梦
""";
var m = ChatLogParser.Parse(md, "chat.md");
Check(m.Ok, "Markdown 解析成功");
Check(m.Format == ChatLogFormat.Markdown, "Markdown 识别");
Eq(m.Messages.Count, 2, "Markdown 两条");

// ---------- 8. 只有「昵称: 内容」的通用文本 ----------
const string colon = """
晚晚: 在吗
林晚: 在
晚晚: 陪我聊会儿
林晚: 好
""";
var cp = ChatLogParser.Parse(colon, "paste.txt");
Check(cp.Ok, "冒号对白解析成功");
Check(cp.Messages.Count == 4, "冒号对白四条");
Check(cp.Note.Contains("没有时间戳"), "冒号对白说明缺时间");

// ---------- 9. mht：base64 的 HTML ----------
var html = "<html><body><p>2023-05-07 10:00:00 晚晚(111)</p><p>在干啥</p>" +
           "<p>2023-05-07 10:01:00 林晚(222)</p><p>写代码</p></body></html>";
var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(html));
var mht = "MIME-Version: 1.0\r\nContent-Type: multipart/related; boundary=\"----=_NextPart_01\"\r\n\r\n" +
          "------=_NextPart_01\r\nContent-Type: text/html; charset=\"utf-8\"\r\n" +
          "Content-Transfer-Encoding: base64\r\n\r\n" + b64 + "\r\n" +
          "------=_NextPart_01--\r\n";
var mh = ChatLogParser.Parse(mht, "qq.mht");
Check(mh.Ok, "mht 抽出 HTML 文本后解析");
Eq(mh.Messages.Count, 2, "mht 两条消息");
Check(mh.Format == ChatLogFormat.QqMht, "mht 格式标记");

// ---------- 10. 认不出就给一句人话 ----------
var bad = ChatLogParser.Parse("今天天气不错。我们出去走走吧。\n顺便买点菜。", "x.txt");
Check(!bad.Ok, "散文认不出");
Check(bad.Error.Contains("没认出") && bad.Error.Contains("csv"), "认不出时说明了支持的格式");

// ---------- 11. 多文件去重 ----------
var a = ChatLogParser.Parse(wechat, "a.txt").Messages;
var b = ChatLogParser.Parse(wechat, "a-copy.txt").Messages;
var merged = a.Concat(b.Select(x => x with { Index = x.Index + 100 })).ToList();
var (kept, dropped) = ChatLogParser.Normalize(merged);
Eq(kept.Count, 3, "同一份文件导入两次只留三条");
Eq(dropped, 3, "重复被计数");

// ---------- 12. 归一化按时间排 ----------
var shuffled = new List<ImportedMessage>
{
    new() { At = new DateTime(2023, 5, 1, 21, 0, 0), Speaker = "A", Text = "后", Index = 0 },
    new() { At = new DateTime(2023, 5, 1, 20, 0, 0), Speaker = "A", Text = "前", Index = 1 },
};
var (sorted, _) = ChatLogParser.Normalize(shuffled);
Text(sorted[0].Text, "前", "归一化按时间排序");

// ---------- 13. 归属：名字对得上 ----------
var dg = ChatLogDigest.Build(kept, "林晚", "晚晚");
Eq(dg.CharacterMessages, 2, "昵称「晚晚」的两条认成 TA");
Eq(dg.UserMessages, 1, "另一个发言者归给你");
Eq(dg.Messages, 3, "总条数");
Check(dg.Speakers.Count == 2, "发言者清单两人");
var dgNameOnly = ChatLogDigest.Build(kept, "林晚", "");
Eq(dgNameOnly.CharacterMessages, 1, "只有主名时认出同名那一条");

// ---------- 14. 用户指认谁 是 TA ----------
var dg2 = ChatLogDigest.Build(kept, "林晚", "", "晚晚");
Eq(dg2.CharacterMessages, 2, "指认之后 TA 变成两条");
Text(dg2.CharacterSpeaker, "晚晚", "回显指认结果");

// ---------- 15. 初始关系在合法范围内 ----------
Check(dg.Affection is >= 30 and <= 920, $"初始好感是 0–1000 点数 ({dg.Affection})");
Check(dg.Trust is >= 0 and <= 85, $"初始信任有界 ({dg.Trust})");
Check(dg.Affection <= 140, "三条消息不会一上来就很熟");
Check(AffectionScale.Clamp(dg.Affection) == dg.Affection, "好感落点已在合法刻度内");

// ---------- 16. 单方面记录会压低初始关系 ----------
var oneSided = Enumerable.Range(0, 60)
    .Select(i => new ImportedMessage
    {
        At = new DateTime(2023, 5, 1).AddMinutes(i * 3),
        Speaker = "甲",
        Text = "在吗在吗在吗",
        Index = i
    }).ToList();
var ds = ChatLogDigest.Build(oneSided, "甲");
Check(ds.UserMessages == 0, "一个人说话时没有你这一侧");
Check(ds.Affection < 400, $"单方面刷屏不该很熟 ({ds.Affection})");

// ---------- 17. 长记录会长到更高但仍有上限 ----------
var many = Enumerable.Range(0, 800)
    .Select(i => new ImportedMessage
    {
        At = new DateTime(2023, 1, 1).AddHours(i * 6),
        Speaker = i % 2 == 0 ? "晚晚" : "林晚",
        Text = i % 7 == 0 ? "我想你了" : "今天也还不错",
        Index = i
    }).ToList();
var dm = ChatLogDigest.Build(many, "林晚", "晚晚");
Check(dm.Affection > 400, $"八百条、跨两百天该有分量 ({dm.Affection})");
Check(dm.Affection <= 920, "仍不超过上限");
Check(dm.UserInitiatedRatio is > 0.2 and < 0.8, "双方主动比例是有来有回");
Check(dm.Notables.Count > 0 && dm.Notables.Count <= 8, "情绪事件最多八条");

// ---------- 18. 记忆种子有界、单条不超过 60 字 ----------
var seeds = dm.MemorySeeds();
Check(seeds.Count is > 0 and <= 40, $"记忆种子数量有界 ({seeds.Count})");
Check(seeds.All(s => s.Text.Length <= 60), "每条记忆不超过 60 字");
Check(seeds.Any(s => s.Important), "有重要时刻种子");

// ---------- 19. 情绪判定 ----------
Check(ChatLogDigest.EmotionOf("我想你了") >= 1.5, "想念是强情绪");
Check(ChatLogDigest.EmotionOf("今天吃了饭") == 0, "日常叙述不是情绪事件");

// ---------- 20. 空输入不死 ----------
var empty = ChatLogDigest.Build(Array.Empty<ImportedMessage>(), "林晚");
Check(empty.Messages == 0 && empty.MemorySeeds().Count == 0, "空输入安全");

// ---------- 21. 中文日期与按天续时刻 ----------
const string cnDate = """
2023年5月8日 09:30:00 晚晚
早
09:31:00 林晚
早呀
""";
var cd = ChatLogParser.Parse(cnDate, "cn.txt");
Check(cd.Ok && cd.Messages.Count == 2, "中文日期 + 只带时刻的续行");
Check(cd.Messages[1].At?.Hour == 9 && cd.Messages[1].At?.Day == 8, "续行沿用前一条的日期");

// ---------- 22. 方括号时间不被误判成 JSON ----------
const string bracket = """
[2023-05-09 09:00:00] 晚晚
在吗
[2023-05-09 09:01:00] 林晚
在
""";
var br = ChatLogParser.Parse(bracket, "b.txt");
Check(br.Ok, "方括号时间格式能解析（且没被当成 JSON）");
Eq(br.Messages.Count, 2, "方括号时间两条");

// ---------- 23. 时间解析 ----------
Check(ChatLogTime.TryParse("2023/5/1 8:05", out var t1) && t1.Minute == 5, "斜杠日期解析");
Check(ChatLogTime.TryParse("2023-05-01T20:14:00", out _), "ISO 时间解析");
Check(!ChatLogTime.TryParse("随便写的", out _), "乱写的时间不通过");

Console.WriteLine($"ChatImportSelfTest: {passed} passed, {failures.Count} failed");
foreach (var f in failures) Console.WriteLine("  FAIL " + f);
return failures.Count == 0 ? 0 : 1;
