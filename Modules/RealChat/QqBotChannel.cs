using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace WarmAsBefore.Modules.RealChat;

/// <summary>
/// QQ 官方机器人通道（q.qq.com 开放平台）。
/// 使用官方 WebSocket 协议接收 C2C 私聊消息，HTTP 接口发送回复。
/// 官方方案不支持群聊；无需额外安装任何框架，也不占用鼠标键盘。
/// </summary>
public sealed class QqBotChannel : IOfficialChannel
{
    private const string TokenUrl = "https://bots.qq.com/app/getAppAccessToken";
    private const string WsUrl = "wss://api.sgroup.qq.com/websocket";
    private const string ApiBase = "https://api.sgroup.qq.com";

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private string _appId = "";
    private string _appSecret = "";
    private string _token = "";
    private DateTime _tokenExpires;
    private ClientWebSocket? _ws;
    private CancellationTokenSource? _cts;
    private string _status = "未配置";

    public string Name => "QQ官方机器人";
    public bool IsRunning => _ws is { State: WebSocketState.Open };
    public string Status => _status;
    public event Action<RealChatMessage>? MessageReceived;

    public void Configure(string appId, string appSecret)
    {
        _appId = appId ?? "";
        _appSecret = appSecret ?? "";
    }

    public async Task<string> TestAsync()
    {
        try
        {
            var ok = await RefreshTokenAsync(true);
            return ok ? $"获取凭证成功（{_appId}）" : $"获取凭证失败：{_status}";
        }
        catch (Exception ex)
        {
            return $"连接失败：{ex.Message}";
        }
    }

    public Task StartAsync()
    {
        if (IsRunning || _cts is not null) return Task.CompletedTask;
        if (string.IsNullOrWhiteSpace(_appId) || string.IsNullOrWhiteSpace(_appSecret))
        {
            _status = "未配置 AppID / AppSecret";
            return Task.CompletedTask;
        }
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        _ = Task.Run(() => RunLoopAsync(ct));
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        _cts?.Cancel();
        try { _ws?.Dispose(); } catch { }
        _ws = null;
        _cts = null;
        _status = "已停止";
        return Task.CompletedTask;
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        var backoff = TimeSpan.FromSeconds(3);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (!await RefreshTokenAsync(false)) { await Delay(backoff, ct); continue; }

                using var ws = new ClientWebSocket();
                ws.Options.SetRequestHeader("Authorization", $"QQBot {_token}");
                _ws = ws;
                _status = "连接中…";
                await ws.ConnectAsync(new Uri(WsUrl), ct);

                _status = "已连接";
                backoff = TimeSpan.FromSeconds(3);

                var interval = await HandshakeAsync(ws, ct);
                var hb = HeartbeatLoop(ws, interval, ct);

                while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
                {
                    var evt = await ReceiveEventAsync(ws, ct);
                    if (evt is not null) HandleDispatch(evt);
                }
                await hb;
            }
            catch (TaskCanceledException) { break; }
            catch (Exception ex)
            {
                _status = $"连接异常：{ex.Message}";
                App.WriteLog($"QqBotChannel: {ex}");
            }
            try { _ws = null; } catch { }
            if (!ct.IsCancellationRequested) await Delay(backoff, ct);
            backoff = TimeSpan.FromSeconds(Math.Min(backoff.TotalSeconds * 1.7, 60));
        }
    }

    private async Task<int> HandshakeAsync(ClientWebSocket ws, CancellationToken ct)
    {
        var interval = 30000;
        while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            var node = await ReceiveEventAsync(ws, ct);
            if (node is null) continue;
            var op = node["op"]?.GetValue<int>() ?? -1;
            if (op == 0) // Hello
            {
                var d = node["d"];
                if (d is not null && d["heartbeat_interval"] is not null)
                    interval = d["heartbeat_interval"]!.GetValue<int>();
                var session = d?["session_id"]?.GetValue<string>() ?? "";
                await SendJsonAsync(ws, new { op = 2, d = new { version = 1, session_id = session } }, ct);
            }
            else if (op == 4)
            {
                return interval;
            }
            else if (op == 6)
            {
                // heartbeat ack
            }
        }
        return interval;
    }

    private static async Task HeartbeatLoop(ClientWebSocket ws, int interval, CancellationToken ct)
    {
        var elapsed = 0;
        while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            await Task.Delay(1000, ct);
            if (++elapsed * 1000 < interval) continue;
            elapsed = 0;
            if (ws.State != WebSocketState.Open) break;
            await SendJsonAsync(ws, new { op = 1, d = new { heartbeat_interval = interval, s = 1 } }, ct);
        }
    }

    private async Task<bool> RefreshTokenAsync(bool force)
    {
        if (!force && !string.IsNullOrEmpty(_token) && DateTime.Now < _tokenExpires) return true;
        try
        {
            var body = JsonSerializer.Serialize(new { appId = _appId, clientSecret = _appSecret });
            using var req = new HttpRequestMessage(HttpMethod.Post, TokenUrl)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            var resp = await _http.SendAsync(req);
            var raw = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            if (root.TryGetProperty("access_token", out var t))
            {
                _token = t.GetString() ?? "";
                _tokenExpires = DateTime.Now.AddSeconds(
                    root.TryGetProperty("expires_in", out var e) ? e.GetInt32() - 60 : 3600);
                _status = "凭证已获取";
                return true;
            }
            _status = root.TryGetProperty("message", out var m) ? $"凭证失败：{m.GetString()}" : "凭证失败";
            return false;
        }
        catch (Exception ex)
        {
            _status = $"凭证请求异常：{ex.Message}";
            return false;
        }
    }

    private void HandleDispatch(JsonNode evt)
    {
        var t = evt["t"]?.GetValue<string>();
        var d = evt["d"];
        if (t != "C2C_MESSAGE_CREATE" || d is null) return;

        try
        {
            var content = d["content"]?.GetValue<string>() ?? "";
            var author = d["author"];
            var openid = author?["id"]?.GetValue<string>()
                         ?? author?["user_openid"]?.GetValue<string>()
                         ?? "";
            var nickname = author?["member_openid"]?.GetValue<string>() ?? "";
            if (string.IsNullOrWhiteSpace(content) || string.IsNullOrWhiteSpace(openid)) return;

            MessageReceived?.Invoke(new RealChatMessage
            {
                Channel = Name,
                SenderId = openid,
                SenderName = string.IsNullOrEmpty(nickname) ? "QQ好友" : nickname,
                Content = content.Trim()
            });
        }
        catch (Exception ex)
        {
            App.WriteLog("QqBotChannel.HandleDispatch -> " + ex);
        }
    }

    public async Task SendAsync(RealChatMessage from, string text)
    {
        if (!IsRunning) return;
        try
        {
            var url = $"{ApiBase}/v2/users/{Uri.EscapeDataString(from.SenderId)}/messages";
            var body = JsonSerializer.Serialize(new { content = text, msg_type = 0 });
            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            req.Headers.TryAddWithoutValidation("Authorization", $"QQBot {_token}");
            var resp = await _http.SendAsync(req);
            if (!resp.IsSuccessStatusCode)
                App.WriteLog($"QqBotChannel.Send failed {resp.StatusCode}: {await resp.Content.ReadAsStringAsync()}");
        }
        catch (Exception ex)
        {
            App.WriteLog("QqBotChannel.SendAsync -> " + ex);
        }
    }

    private static async Task<JsonNode?> ReceiveEventAsync(ClientWebSocket ws, CancellationToken ct)
    {
        var buffer = new byte[16 * 1024];
        var ms = new MemoryStream();
        WebSocketReceiveResult result;
        do
        {
            result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", ct); } catch { }
                return null;
            }
            ms.Write(buffer, 0, result.Count);
        }
        while (!result.EndOfMessage);

        if (ms.Length == 0) return null;
        try
        {
            var json = Encoding.UTF8.GetString(ms.ToArray());
            return JsonNode.Parse(json);
        }
        catch
        {
            return null;
        }
    }

    private static async Task SendJsonAsync(ClientWebSocket ws, object payload, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(payload);
        var bytes = Encoding.UTF8.GetBytes(json);
        await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);
    }

    private static async Task Delay(TimeSpan d, CancellationToken ct)
    {
        try { await Task.Delay(d, ct); } catch { }
    }
}
