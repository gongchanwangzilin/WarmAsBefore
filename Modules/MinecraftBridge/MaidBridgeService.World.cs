using WarmAsBefore.Services;

namespace WarmAsBefore.Modules.MinecraftBridge;

/// <summary>
/// 世界书与这具身体的关系：她在 Minecraft 里看见的每一块石头，都要落进一个她本来就知道的世界里。
/// 那份「本来就知道」就是当前存档绑着的世界书——桥不替它做主，只保证她拿得到。
/// </summary>
public sealed partial class MaidBridgeService
{
    /// <summary>同一个角色隔多久允许重新整理一次世界树常识。</summary>
    private static readonly TimeSpan WorldbookGap = TimeSpan.FromMinutes(30);

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTime> _worldbookSeeded = new(StringComparer.Ordinal);
    private DateTime _worldbookScanUtc = DateTime.MinValue;

    /// <summary>
    /// 给每个绑定到角色的女仆补一遍世界树常识。缺了就补，已经有了就不动——
    /// 整理一次要打模型，不能每 5 秒一帧状态就重来。
    /// </summary>
    private async Task SyncWorldbookAsync(MaidSession session)
    {
        if (!session.SaveActive) return;
        if ((DateTime.UtcNow - _worldbookScanUtc).TotalMinutes < 5) return;
        _worldbookScanUtc = DateTime.UtcNow;

        var worldId = _engine.State.WorldId?.Trim() ?? "";
        if (worldId.Length == 0)
        {
            // 存档没绑世界书：她只剩模组上报的坐标和天气，讲不出「这是哪儿、这里有什么」。
            // 不替用户去挑、也不去建，只把这件事说清楚，让他自己决定。
            session.WorldbookNotice = "WAB 这份存档还没绑世界书。她在游戏里看到的方块、生物和地点就没有背景，"
                + "只能报坐标和天气。到「世界书」页给这份存档挑一份或新建一份，下一次握手她会自己拿去用。";
            return;
        }

        foreach (var charId in session.MaidCharacters.Values
                     .Where(c => !string.IsNullOrWhiteSpace(c))
                     .Distinct(StringComparer.Ordinal)
                     .ToList())
        {
            if (_worldbookSeeded.TryGetValue(charId, out var last) && DateTime.UtcNow - last < WorldbookGap) continue;

            // 判断标准是「她有没有这一份」，不是「存档有没有」：
            // 桥自动建出来的角色从没做过初始化，整套世界树常识对她来说是一片空白。
            var cores = await _human.CoreAsync(charId);
            if (cores.Any(m => m.Tags.Contains(PersonaMemoryService.WorldTag, StringComparer.OrdinalIgnoreCase)))
            {
                _worldbookSeeded[charId] = DateTime.UtcNow;
                continue;
            }

            var written = await _persona.SeedWorldAsync(_engine.State, charId);
            if (written > 0)
            {
                _worldbookSeeded[charId] = DateTime.UtcNow;
                session.WorldbookNotice = "";
                App.WriteLog($"MaidBridge.Worldbook: 已把这份存档的世界书讲给 {charId}（{written} 条）");
            }
            else
            {
                // 拆不出条目：要么世界树本身是空的，要么模型和本地兜底都没东西可写。
                _worldbookSeeded[charId] = DateTime.UtcNow;
                session.WorldbookNotice = "绑着的这份世界书里还没有能讲给她的条目。"
                    + "在世界书里补上世界的常识，她才会真的知道自己站在一个什么样的世界里。";
            }
        }
    }

    /// <summary>会话断开/换存档时作废缓存：下一份存档的世界书得重新对照。</summary>
    private void ForgetWorldbookCache()
    {
        _worldbookSeeded.Clear();
        _worldbookScanUtc = DateTime.MinValue;
    }
}
