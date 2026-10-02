using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace WarmAsBefore.Modules.RealChat;

/// <summary>
/// 微信公众号官方通道（公众号 + 客服消息 API）。
/// 在本机起一个回调端口接收微信服务器推送（需在公众号后台配置服务器 URL，可用内网穿透），
/// 回复通过官方客服消息接口异步推送。仅支持单聊（用户对话），官方方案不支持群聊。
/// </summary>
public sealed class WeChatOfficialChannel : IOfficialChannel
{
    private const string TokenApi = "https://api.weixin.qq.com/cgi-bin/token";
    private const string CustomSendApi = "https://api.weixin.qq.com/cgi-bin/message/custom/send";

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private string _appId = "";
    private string _appSecret = "";
    private string _token = "";
    private int _port = 8012;
    private string _accessToken = "";
    private DateTime _accessExpires;
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private string _status = "未配置";

    public string Name => "微信公众号";
    public bool IsRunning => _listener is { IsListening: true };
    public string Status => _status;
    public event Action<RealChatMessage>? MessageReceived;

    public void Configure(string appId, string appSecret, string token, int port)
    {
        _appId = appId ?? "";
        _appSecret = appSecret ?? "";
        _token = token ?? "";
        _port = port is > 0 and < 65536 ? port : 8012;
    }

    public async Task<string> TestAsync()
    {
        try
        {
            var ok = await RefreshAccessTokenAsync(true);
            if (!ok) return $"获取 access_token 失败：{_status}";
            return $"access_token 获取成功；回调端口 {_port} 监听中（需在公众号后台配置服务器 URL，可使用内网穿透）";
        }
        catch (Exception ex)
        {
            return $"连接失败：{ex.Message}";
        }
    }

    public Task StartAsync()
    {
        if (IsRunning || _cts is not null) return Task.CompletedTask;
        if (string.IsNullOrWhiteSpace(_appId) || string.IsNullOrWhiteSpace(_appSecret)
            || string.IsNullOrWhiteSpace(_token))
        {
            _status = "未配置 AppID / AppSecret / Token";
            return Task.CompletedTask;
        }
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        _ = Task.Run(() => ListenLoopAsync(ct));
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        _cts?.Cancel();
        try { _listener?.Stop(); } catch { }
        try { _listener?.Close(); } catch { }
        _listener = null;
        _cts = null;
        _status = "已停止";
        return Task.CompletedTask;
    }

    private async Task ListenLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                _listener = new HttpListener();
                _listener.Prefixes.Add($"http://127.0.0.1:{_port}/");
                _listener.Start();
                _status = $"回调监听中（端口 {_port}）";
                while (!ct.IsCancellationRequested && _listener.IsListening)
                {
                    HttpListenerContext ctx;
                    try { ctx = await _listener.GetContextAsync().WaitAsync(ct); }
                    catch { break; }
                    _ = Task.Run(() => HandleRequestAsync(ctx));
                }
            }
            catch (Exception ex)
            {
                _status = $"监听异常：{ex.Message}";
                App.WriteLog("WeChatOfficialChannel.Listen -> " + ex);
            }
            try { _listener?.Stop(); } catch { }
            _listener = null;
            if (!ct.IsCancellationRequested)
            {
                try { await Task.Delay(TimeSpan.FromSeconds(5), ct); } catch { }
            }
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext ctx)
    {
        try
        {
            var req = ctx.Request;
            var query = req.QueryString;
            var timestamp = query["timestamp"] ?? "";
            var nonce = query["nonce"] ?? "";
            var signature = query["signature"] ?? "";

            if (!VerifySignature(timestamp, nonce, signature))
            {
                ctx.Response.StatusCode = 403;
                ctx.Response.Close();
                return;
            }

            if (req.HttpMethod == "GET")
            {
                var echostr = query["echostr"] ?? "";
                await WriteResponseAsync(ctx, echostr);
                return;
            }

            string body;
            using (var reader = new StreamReader(req.InputStream, Encoding.UTF8))
                body = await reader.ReadToEndAsync();

            var msg = ParseXmlMessage(body);
            if (msg is not null)
            {
                MessageReceived?.Invoke(msg);
                // 立即返回空串，避免微信重试；AI 回复走客服消息异步推送
                await WriteResponseAsync(ctx, "");
                return;
            }

            await WriteResponseAsync(ctx, "");
        }
        catch (Exception ex)
        {
            App.WriteLog("WeChatOfficialChannel.HandleRequest -> " + ex);
            try { ctx.Response.StatusCode = 500; ctx.Response.Close(); } catch { }
        }
    }

    private static async Task WriteResponseAsync(HttpListenerContext ctx, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        ctx.Response.ContentType = "text/plain; charset=utf-8";
        ctx.Response.ContentLength64 = bytes.Length;
        await ctx.Response.OutputStream.WriteAsync(bytes);
        ctx.Response.Close();
    }

    private bool VerifySignature(string timestamp, string nonce, string signature)
    {
        if (string.IsNullOrWhiteSpace(_token)) return false;
        var arr = new[] { _token, timestamp, nonce }.OrderBy(s => s, StringComparer.Ordinal);
        var joined = string.Concat(arr);
        var sha1 = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(joined))).ToLowerInvariant();
        return sha1 == signature;
    }

    private static RealChatMessage? ParseXmlMessage(string xml)
    {
        try
        {
            var doc = XDocument.Parse(xml);
            var root = doc.Root;
            if (root is null) return null;
            var msgType = root.Element("MsgType")?.Value ?? "";
            if (msgType != "text") return null;
            var from = root.Element("FromUserName")?.Value ?? "";
            var content = root.Element("Content")?.Value ?? "";
            if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(content)) return null;
            return new RealChatMessage
            {
                Channel = "微信公众号",
                SenderId = from,
                SenderName = "微信好友",
                Content = content.Trim()
            };
        }
        catch (Exception ex)
        {
            App.WriteLog("WeChatOfficialChannel.ParseXml -> " + ex);
            return null;
        }
    }

    private async Task<bool> RefreshAccessTokenAsync(bool force)
    {
        if (!force && !string.IsNullOrEmpty(_accessToken) && DateTime.Now < _accessExpires) return true;
        try
        {
            var url = $"{TokenApi}?grant_type=client_credential&appid={Uri.EscapeDataString(_appId)}&secret={Uri.EscapeDataString(_appSecret)}";
            var raw = await _http.GetStringAsync(url);
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            if (root.TryGetProperty("access_token", out var t))
            {
                _accessToken = t.GetString() ?? "";
                _accessExpires = DateTime.Now.AddSeconds(
                    root.TryGetProperty("expires_in", out var e) ? e.GetInt32() - 60 : 3600);
                _status = "access_token 已获取";
                return true;
            }
            _status = root.TryGetProperty("errmsg", out var m) ? $"凭证失败：{m.GetString()}" : "凭证失败";
            return false;
        }
        catch (Exception ex)
        {
            _status = $"凭证请求异常：{ex.Message}";
            return false;
        }
    }

    public async Task SendAsync(RealChatMessage from, string text)
    {
        if (!IsRunning) return;
        try
        {
            if (!await RefreshAccessTokenAsync(false))
            {
                App.WriteLog("WeChatOfficialChannel.Send: no access_token -> " + _status);
                return;
            }
            var url = $"{CustomSendApi}?access_token={Uri.EscapeDataString(_accessToken)}";
            var body = JsonSerializer.Serialize(new
            {
                touser = from.SenderId,
                msgtype = "text",
                text = new { content = text }
            });
            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            var resp = await _http.SendAsync(req);
            var raw = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            if (root.TryGetProperty("errcode", out var code) && code.GetInt32() != 0)
                App.WriteLog($"WeChatOfficialChannel.Send err {raw}");
        }
        catch (Exception ex)
        {
            App.WriteLog("WeChatOfficialChannel.SendAsync -> " + ex);
        }
    }
}
