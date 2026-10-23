namespace WarmAsBefore.Models;

/// <summary>一个节点在网上的位置（世界坐标，原点 = 焦点）。</summary>
public sealed record GraphPlace
{
    public string Id { get; init; } = "";
    public double X { get; init; }
    public double Y { get; init; }

    /// <summary>离焦点几跳：决定长出来的先后、枝条的细度、摆动的幅度。</summary>
    public int Depth { get; init; }

    /// <summary>它挂在谁的枝头（null = 焦点本身）。</summary>
    public string? ParentId { get; init; }

    /// <summary>与父节点那条联系的强度与类型（画枝条用）。</summary>
    public double ParentWeight { get; init; }
    public RelationKind ParentKind { get; init; } = RelationKind.Associated;

    /// <summary>这条枝该在什么时刻开始长（毫秒，相对生长起点）。</summary>
    public double GrowAtMs { get; init; }
}

/// <summary>一次布局的结果。</summary>
public sealed record RelationLayoutResult
{
    public IReadOnlyDictionary<string, GraphPlace> Places { get; init; } = new Dictionary<string, GraphPlace>();

    /// <summary>树上已有父节点的边（其余边当「缠绕的藤」另画）。</summary>
    public IReadOnlyList<GraphPlace> Order { get; init; } = Array.Empty<GraphPlace>();

    /// <summary>因为太深/太远这次没画出来的节点数。</summary>
    public int Hidden { get; init; }
}

/// <summary>一片网在画面上的地盘（圆心 + 包围半径），画底子和片名用它。</summary>
public sealed record NetPlace
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public double X { get; init; }
    public double Y { get; init; }

    /// <summary>这片摊开后的包围半径（世界坐标）：底子要罩住它，片名要贴着它上沿。</summary>
    public double Radius { get; init; }

    public int Count { get; init; }
    public double Weight { get; init; }
    public NodeKind Dominant { get; init; } = NodeKind.Word;
    public bool IsLoose { get; init; }

    /// <summary>这片从生长时钟的什么时候开始长（片与片错开，才看得出「一片一片长出来」）。</summary>
    public double GrowAtMs { get; init; }
}

/// <summary>多片网一起摊开的结果。</summary>
public sealed record MeshResult
{
    public IReadOnlyDictionary<string, GraphPlace> Places { get; init; } = new Dictionary<string, GraphPlace>();
    public IReadOnlyList<NetPlace> Nets { get; init; } = Array.Empty<NetPlace>();
    public IReadOnlyList<GraphPlace> Order { get; init; } = Array.Empty<GraphPlace>();
    public int Hidden { get; init; }

    /// <summary>整张画面长完要多久（含片与片之间的错峰）。</summary>
    public double TotalGrowMs { get; init; }
}

/// <summary>
/// 关系网的布局：把网摊成一个**从焦点往外长的树**，好让人看得见「枝条伸出去」这个动作。
///
/// 为什么不用力导向图（force-directed）：力导要跑几百轮迭代才稳，画面会一直抖，
/// 也没法回答「这根枝从哪儿长出来」。这里按人的联想方式摆：
/// · 焦点在正中，第一圈是它最直接的邻居，往外每圈更远、更弱、更细（扩散激活本来就是这样一层层淡下去的）；
/// · 用 BFS 找「最大强度树」——一个词有几个来源时，挂在**联系最强**的那个枝上，
///   所以枝的结构就是记忆的结构，不是随机的；
/// · 扇形角度按子树大小分配（radial tidy tree），枝多的方向占的角度大，不会互相插进对方地盘；
/// · 非树的边（等价、因果这些跨枝的）照原样画成藤，网感来自它们。
///
/// 纯函数、无随机数（抖动用 Id 的稳定哈希），所以同样的网每次摆出来的位置一样 —— 
/// 只有换焦点才重排，不然动画每帧都在跳。
/// </summary>
public static class RelationLayout
{
    public const double FirstRing = 150;
    public const double RingGap = 112;
    public const double DepthMs = 300;
    public const double BranchMs = 260;

    /// <summary>能画进来的最深层数（再深就只是背景噪音）。</summary>
    public const int MaxDepth = 4;

    /// <summary>
    /// 摆一次网。
    /// <param name="edges">全量边（有向；布局时不分方向，扩散检索那边才区分因果/先后的方向）。</param>
    /// <param name="focusId">焦点节点 Id。</param>
    /// <param name="maxDepth">最深画几跳。</param>
    /// <param name="nodeCount">网里总节点数，只用来报「还有多少个没画」。</param>
    /// </summary>
    public static RelationLayoutResult Build(IReadOnlyList<RelationEdge> edges, string focusId,
        int maxDepth = MaxDepth, int nodeCount = 0)
    {
        var empty = new RelationLayoutResult { Hidden = Math.Max(0, nodeCount - 1) };
        if (edges.Count == 0 || string.IsNullOrEmpty(focusId)) return empty;
        var depthCap = Math.Clamp(maxDepth, 1, MaxDepth);

        var neighbours = new Dictionary<string, List<(string Other, double Weight, RelationKind Kind)>>(StringComparer.Ordinal);
        void Add(string from, string to, RelationEdge e)
        {
            if (!neighbours.TryGetValue(from, out var list)) neighbours[from] = list = new List<(string, double, RelationKind)>();
            list.Add((to, e.Weight, e.Kind));
        }
        foreach (var e in edges)
        {
            if (string.IsNullOrEmpty(e.From) || string.IsNullOrEmpty(e.To) || e.From == e.To) continue;
            Add(e.From, e.To, e);
            Add(e.To, e.From, e);
        }
        if (!neighbours.ContainsKey(focusId))
            return empty with { Hidden = Math.Max(0, nodeCount - 1) };

        // ---- 逐层展开，同一个点被多个来源够到时，挂在联系最强的那根枝上 ----
        var best = new Dictionary<string, (string Parent, double Weight, RelationKind Kind, int Depth)>(StringComparer.Ordinal)
        {
            [focusId] = (""!, 0, RelationKind.Associated, 0)
        };
        var frontier = new List<string> { focusId };
        for (var d = 1; d <= depthCap; d++)
        {
            var next = new List<string>();
            foreach (var from in frontier)
            {
                if (!neighbours.TryGetValue(from, out var list)) continue;
                foreach (var n in list)
                {
                    var seen = best.TryGetValue(n.Other, out var cur);
                    if (seen && cur.Depth < d) continue;          // 已经有更浅的路径，不绕远
                    if (seen && cur.Weight >= n.Weight) continue; // 同层但原来那根枝更粗
                    best[n.Other] = (from, n.Weight, n.Kind, d);
                    next.Add(n.Other);
                }
            }
            frontier = next.Distinct(StringComparer.Ordinal).ToList();
            if (frontier.Count == 0) break;
        }

        var kids = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var kv in best)
        {
            if (kv.Key == focusId) continue;
            if (!kids.TryGetValue(kv.Value.Parent, out var list)) kids[kv.Value.Parent] = list = new List<string>();
            list.Add(kv.Key);
        }

        // ---- 子树大小：枝多的方向该占更大角度 ----
        var size = new Dictionary<string, int>(StringComparer.Ordinal);
        int SizeOf(string id, int guard)
        {
            if (size.TryGetValue(id, out var known)) return known;
            if (guard > depthCap + 2) return 1;
            var s = 1;
            if (kids.TryGetValue(id, out var children))
                foreach (var c in children) s += SizeOf(c, guard + 1);
            size[id] = s;
            return s;
        }
        var rootSize = SizeOf(focusId, 0);

        // ---- 角度分配：焦点的孩子平分一整圈，往下各自继承自己的扇形 ----
        var places = new Dictionary<string, GraphPlace>(StringComparer.Ordinal)
        {
            [focusId] = new GraphPlace { Id = focusId, X = 0, Y = 0, Depth = 0 }
        };
        var queue = new List<(string Id, double From, double To, int Depth)> { (focusId, 0, Math.PI * 2, 0) };
        while (queue.Count > 0)
        {
            var carry = new List<(string Id, double From, double To, int Depth)>();
            foreach (var item in queue)
            {
                if (!kids.TryGetValue(item.Id, out var children)) continue;
                var spanTotal = children.Sum(c => Math.Max(1, SizeOf(c, item.Depth + 1)));
                var wedge = item.To - item.From;
                var cursor = item.From;
                foreach (var c in children
                             .OrderByDescending(x => Math.Max(1, SizeOf(x, item.Depth + 1)))
                             .ThenBy(x => x, StringComparer.Ordinal))
                {
                    var share = wedge * Math.Max(1, SizeOf(c, item.Depth + 1)) / Math.Max(1, spanTotal);
                    var mid = cursor + share / 2;
                    // 越深的圈越紧一点：远亲不该比近邻还占地方
                    var radius = FirstRing + (item.Depth) * RingGap * Math.Pow(0.94, item.Depth);
                    var jitter = StableHash(c);
                    var angle = mid + Normalized(jitter, 0) * share * 0.22 - share * 0.11;
                    var r = radius + (Normalized(jitter, 1) - 0.5) * RingGap * 0.34;
                    var info = best[c];
                    places[c] = new GraphPlace
                    {
                        Id = c,
                        X = Math.Cos(angle) * r,
                        Y = Math.Sin(angle) * r,
                        Depth = info.Depth,
                        ParentId = item.Id,
                        ParentWeight = info.Weight,
                        ParentKind = info.Kind,
                        GrowAtMs = (info.Depth - 1) * DepthMs + Normalized(jitter, 2) * BranchMs
                    };
                    carry.Add((c, cursor, cursor + share, item.Depth + 1));
                    cursor += share;
                }
            }
            queue = carry;
        }

        var order = places.Values
            .OrderBy(p => p.Depth)
            .ThenBy(p => p.GrowAtMs)
            .ThenBy(p => p.Id, StringComparer.Ordinal)
            .ToList();

        return new RelationLayoutResult
        {
            Places = places,
            Order = order,
            Hidden = Math.Max(0, nodeCount - places.Count)
        };
    }

    /// <summary>一条枝从起点到完全长好的时刻（毫秒）。</summary>
    public static double GrowDoneMs(GraphPlace p) => p.GrowAtMs + BranchMs + 420;

    // ==================== 多片网：没有焦点，只有一坨坨挨在一起的关系 ====================

    /// <summary>片与片之间留的空隙（世界坐标）：挤在一起就看不出「这是两张网」了。</summary>
    public const double NetGap = 54;

    /// <summary>一片比下一片晚开始长多久：错开才看得见「先长主干那片，再长旁边的」。</summary>
    public const double NetStaggerMs = 240;

    /// <summary>
    /// 把几张片一起摊到画面上：**没有谁是焦点**。
    ///
    /// 先按片内强度定大小，一片一片摊开：每片在自己地盘里仍然是「从地标往外长的树」
    /// （枝条生长的观感靠这个），片与片之间按**跨片联系的强度**互相靠拢、按包围半径互相避让。
    /// 于是海边那晚和玛丽那两张片会挨着，考试周自己缩到角落里 —— 画面的远近就是关系的远近。
    ///
    /// 全对角度搜索是 8 片 × 24 个候选位 × 8 次评分，纯函数、无随机数：
    /// 同一张网每次摆出来一模一样，不会因为重排一次就满屏乱跳。
    /// </summary>
    public static MeshResult BuildMesh(IReadOnlyList<RelationEdge> edges, IReadOnlyList<RelationNet> nets,
        int nodeCount, int maxNodes = 420, int maxDepth = MaxDepth)
    {
        var empty = new MeshResult { Hidden = Math.Max(0, nodeCount) };
        if (nets.Count == 0) return empty;
        var depthCap = Math.Clamp(maxDepth, 1, MaxDepth);

        var member = RelationNets.Membership(nets);
        var inside = new Dictionary<string, List<RelationEdge>>(StringComparer.Ordinal);
        var pull = new Dictionary<string, double>(StringComparer.Ordinal);   // "片A‖片B" -> 跨片强度
        foreach (var net in nets) inside[net.Id] = new List<RelationEdge>();
        foreach (var e in edges)
        {
            if (!member.TryGetValue(e.From, out var a) || !member.TryGetValue(e.To, out var b)) continue;
            if (a == b) { inside[a].Add(e); continue; }
            var key = PairKey(a, b);
            pull[key] = (pull.TryGetValue(key, out var old) ? old : 0) + Math.Max(0.1, e.Weight);
        }

        // 大片先摊：小片往大片的缝里塞，比反过来省地方，也让主要的事占住中间
        var ranked = nets
            .OrderByDescending(n => n.NodeIds.Count)
            .ThenByDescending(n => n.Weight)
            .ThenBy(n => n.Id, StringComparer.Ordinal)
            .ToList();

        var places = new Dictionary<string, GraphPlace>(StringComparer.Ordinal);
        var disks = new List<NetPlace>();
        var centers = new List<(double X, double Y, double R, string Id)>();
        var budget = Math.Max(12, maxNodes);

        for (var rank = 0; rank < ranked.Count; rank++)
        {
            var net = ranked[rank];
            var remain = budget - places.Count;
            if (remain <= 0) break;

            var local = LayoutNet(net, inside.TryGetValue(net.Id, out var ie) ? ie : Empty, remain, depthCap);
            if (local.Count == 0) continue;

            var reach = 0.0;
            foreach (var p in local.Values)
            {
                var d = Math.Sqrt(p.X * p.X + p.Y * p.Y);
                if (d > reach) reach = d;
            }
            var radius = Math.Max(58, reach + 34);
            var at = Pack(centers, net.Id, radius, pull);
            var growAt = rank * NetStaggerMs;

            foreach (var kv in local)
                places[kv.Key] = kv.Value with
                {
                    X = kv.Value.X + at.X,
                    Y = kv.Value.Y + at.Y,
                    GrowAtMs = kv.Value.GrowAtMs + growAt
                };

            centers.Add(at);
            disks.Add(new NetPlace
            {
                Id = net.Id, Name = net.Name, X = at.X, Y = at.Y, Radius = radius,
                Count = local.Count, Weight = net.Weight, Dominant = net.Dominant,
                IsLoose = net.IsLoose, GrowAtMs = growAt
            });
        }

        var order = places.Values
            .OrderBy(p => p.GrowAtMs)
            .ThenBy(p => p.Id, StringComparer.Ordinal)
            .ToList();

        return new MeshResult
        {
            Places = places,
            Nets = disks.OrderBy(d => d.GrowAtMs).ThenBy(d => d.Id, StringComparer.Ordinal).ToList(),
            Order = order,
            Hidden = Math.Max(0, nodeCount - places.Count),
            TotalGrowMs = order.Count == 0 ? 0 : order.Max(GrowDoneMs)
        };
    }

    private static readonly List<RelationEdge> Empty = new();

    /// <summary>
    /// 一片摊在自己地盘里（原点 = 这片的地标）。地标孤零零一个点、连不上自己片里任何人时，
    /// 退化成摆一圈：游离片就是这样，谁都没连上的那些点围成一个环，仍然看得见「这是一堆还没归类的」。
    /// </summary>
    private static Dictionary<string, GraphPlace> LayoutNet(RelationNet net, List<RelationEdge> internalEdges,
        int budget, int depthCap)
    {
        var hub = internalEdges.Count == 0 || !internalEdges.Any(e => e.From == net.HubId || e.To == net.HubId)
            ? null : net.HubId;

        if (hub is not null)
        {
            for (var d = depthCap; d >= 1; d--)
            {
                var r = Build(internalEdges, hub, d, net.NodeIds.Count);
                if (r.Places.Count > budget && d > 1) continue;    // 一圈太肥就先少画一圈
                var kept = TrimTo(r.Places, budget);
                // 地标一个邻居都够不到时 Build 只给一个点：这种片改摆环，否则整片凭空消失
                if (kept.Count > 1 || d == 1) return kept;
            }
        }
        return Ring(net.NodeIds, budget);
    }

    /// <summary>超预算的那几个先不画：按「离地标多近」收，深处的枝最后才被砍掉。</summary>
    private static Dictionary<string, GraphPlace> TrimTo(IReadOnlyDictionary<string, GraphPlace> src, int budget)
    {
        if (src.Count <= budget) return new Dictionary<string, GraphPlace>(src, StringComparer.Ordinal);
        return src.OrderBy(kv => kv.Value.Depth)
            .ThenBy(kv => Math.Sqrt(kv.Value.X * kv.Value.X + kv.Value.Y * kv.Value.Y))
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Take(Math.Max(1, budget))
            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
    }

    /// <summary>没联系的点围成一圈：半径跟着点数走，点越多圈越大，不会挤成一坨。</summary>
    private static Dictionary<string, GraphPlace> Ring(List<string> ids, int budget)
    {
        var take = ids.OrderBy(x => x, StringComparer.Ordinal).Take(Math.Max(1, budget)).ToList();
        var count = Math.Max(1, take.Count);
        var radius = Math.Max(52, 20 + count * 84.0 / (2 * Math.PI));
        var outp = new Dictionary<string, GraphPlace>(StringComparer.Ordinal);
        for (var i = 0; i < count; i++)
        {
            var ang = i / (double)count * Math.PI * 2 - Math.PI / 2;
            outp[take[i]] = new GraphPlace
            {
                Id = take[i],
                X = Math.Cos(ang) * radius,
                Y = Math.Sin(ang) * radius,
                Depth = 1,
                GrowAtMs = i * 26.0
            };
        }
        return outp;
    }

    /// <summary>
    /// 两片之间该留多远：没联系的两片保持原间距，有联系的就把它收掉大半、越紧收得越多。
    /// 画面的远近就是关系的远近 —— 光靠「挨着」看不出海边那晚和考试周其实是一晚的事。
    /// </summary>
    private static double NetSpacing(double tie)
        => tie <= 0 ? NetGap
            : NetGap * Math.Clamp(1 - 0.75 * RelationMath.Saturate(tie, 8), 0.12, 1.0);

    /// <summary>
    /// 给新片找一个位置：绕着已摆好的片各试 24 个角度，
    /// 跨片联系越重加分越多（有关系的两片要挨着），压到别人的地盘重罚，离原点太远轻罚。
    /// </summary>
    private static (double X, double Y, double R, string Id) Pack(
        List<(double X, double Y, double R, string Id)> placed, string id, double radius,
        Dictionary<string, double> pull)
    {
        if (placed.Count == 0) return (0, 0, radius, id);

        var best = (X: 0.0, Y: 0.0, R: radius, Id: id);
        var bestScore = double.NegativeInfinity;
        const int steps = 24;

        for (var i = 0; i < placed.Count; i++)
        {
            var host = placed[i];
            var tieToHost = pull.TryGetValue(PairKey(id, host.Id), out var hv) ? hv : 0;
            var ring = host.R + radius + NetSpacing(tieToHost);
            for (var s = 0; s < steps; s++)
            {
                var ang = s / (double)steps * Math.PI * 2 + (i % 3) * 0.14;
                var cand = (X: host.X + Math.Cos(ang) * ring, Y: host.Y + Math.Sin(ang) * ring);
                var score = 0.0;

                for (var k = 0; k < placed.Count; k++)
                {
                    var other = placed[k];
                    var dist = Math.Sqrt(Math.Pow(cand.X - other.X, 2) + Math.Pow(cand.Y - other.Y, 2));
                    var tie = pull.TryGetValue(PairKey(id, other.Id), out var v) ? v : 0;
                    if (tie > 0) score += tie * 26 / (tie + Math.Max(1, dist));   // 挨着有关系的片
                    var need = other.R + radius + NetSpacing(tie) * 0.6;
                    if (dist < need) score -= (need - dist) * 14;                   // 不许压别人的地盘
                    else score -= (dist - need) * 0.05;                             // 也别甩到天际
                }
                score -= Math.Sqrt(cand.X * cand.X + cand.Y * cand.Y) * 0.02;       // 整张画面往中间收
                score -= s * 0.001;                                                 // 定摆动的平手：同分永远取同一个位置

                if (score <= bestScore) continue;
                bestScore = score;
                best = (cand.X, cand.Y, radius, id);
            }
        }
        return best;
    }

    private static string PairKey(string a, string b)
        => string.CompareOrdinal(a, b) <= 0 ? a + "‖" + b : b + "‖" + a;


    public static double TotalGrowMs(IEnumerable<GraphPlace> places) =>
        places.Where(p => p.Depth > 0).Select(GrowDoneMs).DefaultIfEmpty(0).Max();

    /// <summary>不依赖进程的稳定哈希（string.GetHashCode 每个进程都不一样，会让网每次摆的位置都变）。</summary>
    public static int StableHash(string s)
    {
        unchecked
        {
            var h = 2166136261u;
            foreach (var ch in s) { h ^= ch; h *= 16777619u; }
            return (int)(h & 0x7FFFFFFF);
        }
    }

    /// <summary>把哈希的第 k 个字节段摊成 0~1，用来给每条枝一点固定的「不一样」。</summary>
    public static double Normalized(int hash, int lane) => ((hash >> (lane * 7)) & 0xFF) / 255.0;
}
