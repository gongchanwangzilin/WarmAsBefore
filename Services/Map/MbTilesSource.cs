using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace WarmAsBefore.Services;

public enum MbTilesState
{
    NotConfigured,
    Missing,
    Unreadable,
    Ready
}

/// <summary>离线包此刻的样子：状态、一句话交代、metadata 摘要、读了多少块 / 缺了多少块。</summary>
public sealed record MbTilesStatus(
    MbTilesState State,
    string Message,
    string Path,
    MbTilesMeta? Meta,
    int TilesServed,
    int Misses,
    int CacheCount,
    long Bytes)
{
    public bool IsReady => State == MbTilesState.Ready;

    /// <summary>没有选包时的样子：一句可操作的话，不是空字符串。</summary>
    public static MbTilesStatus Unset { get; } = new(
        MbTilesState.NotConfigured,
        "还没选离线包：点「选择 .mbtiles 文件」挑一个包，选完地图就能离线看。",
        "", null, 0, 0, 0, 0);

}

/// <summary>
/// 一个真实的 MBTiles 离线包：只读打开、单连接、全部 async。
///
/// 表结构两种都吃：
/// - <b>标准两张表</b>：tiles + metadata，tiles 是 table；
/// - <b>map + images 视图</b>：tiles 是 view，字段名照旧（zoom_level / tile_column / tile_row / tile_data）。
/// 这两种的读法其实是同一条 SQL，区别只在 sqlite_master 记的是 table 还是 view —— 记下来给状态行看。
///
/// tile_row 按 TMS 自南向北，而地图要 XYZ 自北向南：y = 2^z - 1 - row（见 <see cref="MbTilesInfo"/>）。
/// metadata 写了 scheme=xyz 的包例外，不能再翻。
///
/// 最近读过的 64 块瓦片字节留在内存里：拖回去、缩放回来不必再劳 SQLite。
/// </summary>
public sealed class MbTilesSource : IDisposable
{
    /// <summary>字节缓存容量。一块 256 像素的 JPEG/PNG 通常 10–60KB，64 块不到 4MB。</summary>
    public const int CacheCapacity = 64;

    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _cacheGate = new();
    private readonly Dictionary<string, byte[]> _bytes = new(StringComparer.Ordinal);
    private readonly LinkedList<string> _order = new();

    private SqliteConnection? _conn;
    private MbTilesStatus _status;
    private long _bytesTotal;
    private int _served;
    private int _misses;

    public MbTilesSource(string path)
    {
        _path = (path ?? "").Trim();
        var (state, message) = MbTilesInfo.CheckPath(_path, _path.Length > 0 && File.Exists(_path));
        _status = new MbTilesStatus(
            state switch
            {
                MbTilesPathState.Ok => MbTilesState.NotConfigured,
                MbTilesPathState.Missing => MbTilesState.Missing,
                MbTilesPathState.WrongExtension => MbTilesState.Unreadable,
                _ => MbTilesState.NotConfigured
            },
            message, _path, null, 0, 0, 0, 0);
    }

    public string Path => _path;

    public int CacheCount
    {
        get { lock (_cacheGate) return _bytes.Count; }
    }

    /// <summary>当下一刻的状态（计数器跟着走），UI 每次读都是新值。</summary>
    public MbTilesStatus Snapshot() => _status with
    {
        TilesServed = _served,
        Misses = _misses,
        CacheCount = CacheCount,
        Bytes = Interlocked.Read(ref _bytesTotal)
    };

    // ==================== 打开 ====================

    /// <summary>只读打开 + 读 metadata + 认表结构 + 探一下 tiles 表。失败返回带原因的状态，不抛。</summary>
    public async Task<MbTilesStatus> OpenAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var (pathState, pathMessage) = MbTilesInfo.CheckPath(_path, _path.Length > 0 && File.Exists(_path));
            if (pathState != MbTilesPathState.Ok)
            {
                _status = _status with
                {
                    State = pathState == MbTilesPathState.Missing ? MbTilesState.Missing : MbTilesState.Unreadable,
                    Message = pathMessage
                };
                return _status;
            }

            SqliteConnection? conn = null;
            try
            {
                conn = new SqliteConnection(MbTilesInfo.ReadOnlyConnectionString(_path));
                await conn.OpenAsync(ct).ConfigureAwait(false);

                var metadata = await ReadMetadataAsync(conn, ct).ConfigureAwait(false);
                var layout = await ReadLayoutAsync(conn, ct).ConfigureAwait(false);
                var hasRows = await ProbeAsync(conn, ct).ConfigureAwait(false);
                if (hasRows is null)
                {
                    conn.Dispose();
                    _status = _status with
                    {
                        State = MbTilesState.Unreadable,
                        Message = $"离线包能打开，但 tiles 表读不出来：{System.IO.Path.GetFileName(_path)}"
                    };
                    return _status;
                }

                var meta = MbTilesInfo.ParseMetadata(metadata, System.IO.Path.GetFileNameWithoutExtension(_path), layout);
                _conn = conn;
                conn = null;   // 交出去了，别在 finally 里关掉
                var summary = MbTilesInfo.Describe(meta) + (hasRows.Value ? "" : " · 包是空的");
                _status = _status with { State = MbTilesState.Ready, Message = summary, Meta = meta };
                return _status;
            }
            catch (OperationCanceledException)
            {
                conn?.Dispose();
                _status = _status with { State = MbTilesState.Unreadable, Message = "打开离线包被取消。" };
                return _status;
            }
            catch (Exception ex)
            {
                conn?.Dispose();
                App.WriteLog("MbTilesSource.Open -> " + ex.Message);
                _status = _status with
                {
                    State = MbTilesState.Unreadable,
                    Message = $"离线包读不出来（可能不是 MBTiles 或文件损坏）：{ex.Message}"
                };
                return _status;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private static async Task<Dictionary<string, string?>> ReadMetadataAsync(SqliteConnection conn, CancellationToken ct)
    {
        var dict = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT name, value FROM metadata";
            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                if (reader.IsDBNull(0)) continue;
                dict[reader.GetString(0)] = reader.IsDBNull(1) ? null : reader.GetString(1);
            }
        }
        catch (Exception ex)
        {
            // metadata 是可选表：少了它照样能读瓦片，档位按 0–22 兜底。
            App.WriteLog("MbTilesSource.Metadata -> " + ex.Message);
        }
        return dict;
    }

    private static async Task<string> ReadLayoutAsync(SqliteConnection conn, CancellationToken ct)
    {
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT type FROM sqlite_master WHERE name = 'tiles' LIMIT 1";
            var type = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) as string;
            if (type is null) return "missing";
            return MbTilesInfo.IsViewLayout(type) ? "view" : "table";
        }
        catch (Exception ex)
        {
            App.WriteLog("MbTilesSource.Layout -> " + ex.Message);
            return "unknown";
        }
    }

    /// <summary>null = tiles 表读不出来；true/false = 有没有至少一行。</summary>
    private static async Task<bool?> ProbeAsync(SqliteConnection conn, CancellationToken ct)
    {
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 1 FROM tiles LIMIT 1";
            var v = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
            return v is not null;
        }
        catch (Exception ex)
        {
            App.WriteLog("MbTilesSource.Probe -> " + ex.Message);
            return null;
        }
    }

    // ==================== 读瓦片 ====================

    /// <summary>z/x/y 是 XYZ（自北向南）；内部翻成 TMS 查表。没有就返回 null，调用方用父级顶替。</summary>
    public async Task<byte[]?> GetTileAsync(int z, int x, int y, CancellationToken ct)
    {
        var key = Key(z, x, y);
        if (CacheGet(key) is { } hit) return hit;
        if (_conn is null) return null;

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_conn is null) return null;
            var xyz = Snapshot().Meta?.XyzScheme ?? false;
            var row = MbTilesInfo.RowForY(y, z, xyz);

            await using var cmd = _conn.CreateCommand();
            cmd.CommandText =
                "SELECT tile_data FROM tiles WHERE zoom_level = $z AND tile_column = $x AND tile_row = $r LIMIT 1";
            cmd.Parameters.AddWithValue("$z", z);
            cmd.Parameters.AddWithValue("$x", x);
            cmd.Parameters.AddWithValue("$r", row);

            var raw = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) as byte[];
            if (raw is null || raw.Length == 0 || MbTilesInfo.SniffFormat(raw).Length == 0)
            {
                // 包里这一格是空的，或者不是图片：记一次缺图，别让坏数据进缓存。
                Interlocked.Increment(ref _misses);
                return null;
            }

            CachePut(key, raw);
            Interlocked.Add(ref _bytesTotal, raw.Length);
            Interlocked.Increment(ref _served);
            return raw;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            App.WriteLog($"MbTilesSource.Tile({_path} {z}/{x}/{y}) -> {ex.GetType().Name}: {ex.Message}");
            Interlocked.Increment(ref _misses);
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static string Key(int z, int x, int y) => $"{z}/{x}/{y}";

    private byte[]? CacheGet(string key)
    {
        lock (_cacheGate)
        {
            if (!_bytes.TryGetValue(key, out var bytes)) return null;
            _order.Remove(key);
            _order.AddLast(key);
            return bytes;
        }
    }

    private void CachePut(string key, byte[] bytes)
    {
        lock (_cacheGate)
        {
            if (_bytes.ContainsKey(key)) return;
            _bytes[key] = bytes;
            _order.AddLast(key);
            while (_order.Count > CacheCapacity && _order.First is { } first)
            {
                _order.RemoveFirst();
                _bytes.Remove(first.Value);
            }
        }
    }

    public void Dispose()
    {
        try { _conn?.Dispose(); } catch { /* 故意忽略：释放 SQLite 连接，可能已释放 */ }
        _conn = null;
        lock (_cacheGate)
        {
            _bytes.Clear();
            _order.Clear();
        }
        _gate.Dispose();
    }
}
