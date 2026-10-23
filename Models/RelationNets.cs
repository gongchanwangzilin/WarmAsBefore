namespace WarmAsBefore.Models;

/// <summary>
/// 一片网：TA 脑子里挨得最近的那一坨概念（海边那晚、玛丽、停电、考试周……各是一片）。
/// 名字取自这片里最中心的那个说法，所以「海边」这一片就叫海边，不用人再取一遍。
/// </summary>
public sealed record RelationNet
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";

    /// <summary>这片的地标节点（不是焦点：它只是最中心的一个点，画的时候这片围着它摊开）。</summary>
    public string HubId { get; init; } = "";

    public List<string> NodeIds { get; init; } = new();

    /// <summary>片内总强度：决定这片占多大地方、排在前头还是后头。</summary>
    public double Weight { get; init; }

    /// <summary>跨片联系条数：越多说明这片和别处咬得越紧。</summary>
    public int CrossEdges { get; init; }

    /// <summary>这片主要是什么（人 / 记得的事 / 地方 / 说法……），给标题和图例用。</summary>
    public NodeKind Dominant { get; init; } = NodeKind.Word;

    /// <summary>游离片：谁都没连上的那些点，单独放一片，免得满屏都是「一片一个点」。</summary>
    public bool IsLoose { get; init; }
}

/// <summary>
/// 把一张大网切成几片：标签传播（label propagation）——每个点跟着「身边最强的那几根线」投票，
/// 谁的票重就跟谁进同一片，几轮之后自然分出几坨。
///
/// 为什么用它而不是 K means / 谱聚类：
/// · 不需要先猜片数，片数由网自己决定（TA 记得的事本来就有多有少）；
/// · 只读邻接表，几百个点跑八轮是微秒级，每次换焦点/重摊网都算得起；
/// · 结果只取决于边和权重，不掺随机数，同一张网每次切出来一模一样。
///
/// 关键的一条：每个点只按**最强的 6 根线**投票。
/// 网一密（自动联系织满之后一定密），全票传播会把所有点并成一大坨，
/// 那就不是「几片网」而是一个毛球了 —— 人自己也从来不是靠弱关联分清「哪件事是哪件事」的。
/// </summary>
public static class RelationNets
{
    public const int MaxNets = 8;
    public const int Rounds = 8;
    public const int TopVotesPerNode = 6;
    public const string LooseId = "loose";

    public static List<RelationNet> Cluster(RelationGraph graph, int maxNets = MaxNets)
    {
        var nodeIds = graph.Nodes.Select(n => n.Id).Where(id => !string.IsNullOrEmpty(id)).Distinct().ToList();
        if (nodeIds.Count == 0) return new List<RelationNet>();

        // ---- 无向邻接：同一对点之间多条联系（等价 + 属于 + 因果）要合票 ----
        var adj = new Dictionary<string, Dictionary<string, double>>(StringComparer.Ordinal);
        void Arc(string from, string to, double w)
        {
            if (from == to) return;
            if (!adj.TryGetValue(from, out var row)) adj[from] = row = new Dictionary<string, double>(StringComparer.Ordinal);
            row[to] = (row.TryGetValue(to, out var old) ? old : 0) + Math.Max(0, w);
        }
        foreach (var e in graph.Edges)
        {
            if (string.IsNullOrEmpty(e.From) || string.IsNullOrEmpty(e.To)) continue;
            Arc(e.From, e.To, e.Weight);
            Arc(e.To, e.From, e.Weight);
        }

        var order = nodeIds.OrderBy(x => x, StringComparer.Ordinal).ToList();
        var label = order.ToDictionary(id => id, id => id, StringComparer.Ordinal);

        for (var round = 0; round < Rounds; round++)
        {
            var moved = 0;
            var next = new Dictionary<string, string>(label, StringComparer.Ordinal);
            foreach (var id in order)
            {
                if (!adj.TryGetValue(id, out var row) || row.Count == 0) continue;
                // 只让最强的几根线说话；票按标签汇总，谁的重跟谁
                var votes = new Dictionary<string, double>(StringComparer.Ordinal);
                foreach (var n in row.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal)
                             .Take(TopVotesPerNode))
                {
                    var lb = label[n.Key];
                    votes[lb] = (votes.TryGetValue(lb, out var v) ? v : 0) + n.Value;
                }
                var win = votes.OrderByDescending(kv => kv.Value)
                    .ThenBy(kv => kv.Key, StringComparer.Ordinal)
                    .First().Key;
                // 自己那一票也放进去：不然一个点会被人邻居整个拽走，片越滚越大
                win = string.CompareOrdinal(win, label[id]) == 0 ? win
                    : (votes.TryGetValue(label[id], out var mine) && mine >= votes[win] ? label[id] : win);
                if (win == label[id]) continue;
                next[id] = win;
                moved++;
            }
            label = next;
            if (moved == 0) break;
        }

        // ---- 汇总成片 ----
        var groups = label
            .GroupBy(kv => kv.Value, kv => kv.Key)
            .Select(g => new
            {
                Key = g.Key,
                Ids = g.OrderBy(x => x, StringComparer.Ordinal).ToList(),
                Size = g.Count()
            })
            .ToList();

        var keep = Math.Max(1, maxNets);
        var ranked = groups
            .Where(g => g.Size > 1)
            .OrderByDescending(g => InternalWeight(g.Ids, adj))
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .ToList();
        if (ranked.Count > keep - 1) ranked = ranked.Take(Math.Max(1, keep - 1)).ToList();
        var keptIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var g in ranked) foreach (var id in g.Ids) keptIds.Add(id);

        var rest = order.Where(id => !keptIds.Contains(id)).ToList();

        var byId = graph.Nodes.GroupBy(n => n.Id).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var nets = new List<RelationNet>();
        foreach (var g in ranked) nets.Add(Build(g.Ids, adj, byId, graph));
        if (rest.Count > 0) nets.Add(Build(rest, adj, byId, graph, loose: true));

        return nets;
    }

    private static double InternalWeight(List<string> ids, Dictionary<string, Dictionary<string, double>> adj)
    {
        var set = new HashSet<string>(ids, StringComparer.Ordinal);
        double sum = 0;
        foreach (var id in ids)
            if (adj.TryGetValue(id, out var row))
                foreach (var kv in row)
                    if (set.Contains(kv.Key)) sum += kv.Value;
        return sum / 2;   // 无向，每条数了两遍
    }

    private static RelationNet Build(List<string> ids, Dictionary<string, Dictionary<string, double>> adj,
        Dictionary<string, RelationNode> byId, RelationGraph graph, bool loose = false)
    {
        // 地标：显著性 × 在这片里牵着多少线，谁最「拽得动别人」谁就是地标
        var set = new HashSet<string>(ids, StringComparer.Ordinal);
        double Pull(string n) => adj.TryGetValue(n, out var row)
            ? row.Where(kv => set.Contains(kv.Key)).Sum(kv => kv.Value)
            : 0;
        var hub = ids
            .Select(n => new
            {
                Id = n,
                Score = (byId.TryGetValue(n, out var nd) ? nd.Salience : 0.2) * 20 + Pull(n) / 40.0
            })
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Id, StringComparer.Ordinal)
            .First().Id;

        var dominant = ids
            .Select(n => byId.TryGetValue(n, out var nd) ? nd.Kind : NodeKind.Word)
            .GroupBy(k => k)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key)
            .First().Key;

        var cross = graph.Edges.Count(e =>
            (set.Contains(e.From) && !set.Contains(e.To)) || (set.Contains(e.To) && !set.Contains(e.From)));

        return new RelationNet
        {
            // 片号按地标命名，不按序号：序号会在重摊网时错位，同一批记忆今天叫 3 号明天叫 5 号，
            // 用户选的「只看这片」和配的颜色就全乱了
            Id = loose ? LooseId : "net:" + hub,
            Name = loose ? "还没连上的" : (byId.TryGetValue(hub, out var h) ? h.Label : hub),
            HubId = hub,
            NodeIds = ids,
            Weight = InternalWeight(ids, adj),
            CrossEdges = cross,
            Dominant = dominant,
            IsLoose = loose
        };
    }

    /// <summary>节点 → 所属片。给布局和「只看这片」用。</summary>
    public static Dictionary<string, string> Membership(IReadOnlyList<RelationNet> nets)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var n in nets)
            foreach (var id in n.NodeIds) map[id] = n.Id;
        return map;
    }
}
