using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Chert.Core.Servers;

/// <summary>SLP（Server List Ping）查询结果。</summary>
public sealed class ServerStatus
{
    public bool Ok { get; init; }
    public string? Error { get; init; }
    public int PingMs { get; init; } = -1;
    public string? Motd { get; init; }
    public string? VersionName { get; init; }
    public int Protocol { get; init; }
    public int Online { get; init; }
    public int Max { get; init; }
    public string? FaviconBase64 { get; init; }

    public static ServerStatus Fail(string error) => new() { Ok = false, Error = error };
}

/// <summary>
/// Minecraft 服务器列表 Ping（1.7+ 协议）。
/// 流程：握手包(0x00) → 状态请求(0x00) → 读取 JSON 响应。
/// VarInt 编解码与 MOTD 提取均为纯函数，可离线自检。
/// </summary>
public static class ServerPinger
{
    /// <summary>协议号：使用 -1 表示"未确定版本"，服务端会照常返回状态。</summary>
    public const int UnknownProtocol = -1;

    public const int DefaultPort = 25565;
    public const int DefaultTimeoutMs = 3000;

    // ---- VarInt ----

    /// <summary>写 VarInt（Minecraft 变长整数，7 位一组，最高位为续位标志）。</summary>
    public static byte[] WriteVarInt(int value)
    {
        var buf = new List<byte>(5);
        var v = unchecked((uint)value);
        do
        {
            var temp = (byte)(v & 0b0111_1111);
            v >>= 7;
            if (v != 0) temp |= 0b1000_0000;
            buf.Add(temp);
        } while (v != 0);
        return buf.ToArray();
    }

    /// <summary>从字节数组读 VarInt，返回值与消耗字节数；非法返回 false。</summary>
    public static bool TryReadVarInt(ReadOnlySpan<byte> data, out int value, out int bytesRead)
    {
        value = 0;
        bytesRead = 0;
        var shift = 0;
        while (bytesRead < data.Length)
        {
            var b = data[bytesRead++];
            value |= (b & 0b0111_1111) << shift;
            if ((b & 0b1000_0000) == 0) return true;
            shift += 7;
            if (shift >= 35) return false;
        }
        return false;
    }

    /// <summary>写带长度前缀的 UTF-8 字符串。</summary>
    public static byte[] WriteString(string s)
    {
        var raw = Encoding.UTF8.GetBytes(s);
        var len = WriteVarInt(raw.Length);
        var result = new byte[len.Length + raw.Length];
        Buffer.BlockCopy(len, 0, result, 0, len.Length);
        Buffer.BlockCopy(raw, 0, result, len.Length, raw.Length);
        return result;
    }

    /// <summary>构造握手包（含长度前缀与包 ID 0x00）。</summary>
    public static byte[] BuildHandshake(string host, int port, int protocol = UnknownProtocol)
    {
        var body = new List<byte> { 0x00 };                 // packet id
        body.AddRange(WriteVarInt(protocol));               // protocol version
        body.AddRange(WriteString(host));                   // server address
        body.Add((byte)(port >> 8));                        // port (unsigned short, big endian)
        body.Add((byte)(port & 0xFF));
        body.AddRange(WriteVarInt(1));                      // next state = status

        var payload = body.ToArray();
        var prefix = WriteVarInt(payload.Length);
        var packet = new byte[prefix.Length + payload.Length];
        Buffer.BlockCopy(prefix, 0, packet, 0, prefix.Length);
        Buffer.BlockCopy(payload, 0, packet, prefix.Length, payload.Length);
        return packet;
    }

    /// <summary>状态请求包：长度 1，包 ID 0x00。</summary>
    public static byte[] BuildStatusRequest() => new byte[] { 0x01, 0x00 };

    /// <summary>
    /// 从状态 JSON 提取 MOTD 纯文本。支持 <c>description</c> 为字符串、
    /// <c>{text}</c>、<c>{extra:[...]}</c> 以及 <c>translate</c> 结构，并剥离 §颜色码。
    /// </summary>
    public static string ExtractMotd(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return "";
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("description", out var desc)) return "";
            var sb = new StringBuilder();
            FlattenText(desc, sb);
            return StripColorCodes(sb.ToString()).Trim();
        }
        catch
        {
            return "";
        }
    }

    /// <summary>递归展平 chat component 为纯文本。</summary>
    private static void FlattenText(JsonElement el, StringBuilder sb)
    {
        switch (el.ValueKind)
        {
            case JsonValueKind.String:
                sb.Append(el.GetString());
                break;

            case JsonValueKind.Array:
                foreach (var item in el.EnumerateArray()) FlattenText(item, sb);
                break;

            case JsonValueKind.Object:
                if (el.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String)
                    sb.Append(t.GetString());
                else if (el.TryGetProperty("translate", out var tr) && tr.ValueKind == JsonValueKind.String)
                    sb.Append(tr.GetString());
                if (el.TryGetProperty("extra", out var extra)) FlattenText(extra, sb);
                break;
        }
    }

    /// <summary>剥离 Minecraft §x 颜色/格式代码。</summary>
    public static string StripColorCodes(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder(s!.Length);
        for (var i = 0; i < s.Length; i++)
        {
            if (s[i] == '\u00a7' && i + 1 < s.Length) { i++; continue; }
            sb.Append(s[i]);
        }
        return sb.ToString();
    }

    /// <summary>解析完整状态 JSON。</summary>
    public static ServerStatus ParseStatusJson(string? json, int pingMs)
    {
        if (string.IsNullOrWhiteSpace(json)) return ServerStatus.Fail("空响应");
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            string? verName = null;
            var protocol = 0;
            if (root.TryGetProperty("version", out var ver))
            {
                if (ver.TryGetProperty("name", out var vn) && vn.ValueKind == JsonValueKind.String)
                    verName = vn.GetString();
                if (ver.TryGetProperty("protocol", out var pr) && pr.TryGetInt32(out var pv))
                    protocol = pv;
            }

            int online = 0, max = 0;
            if (root.TryGetProperty("players", out var pl))
            {
                if (pl.TryGetProperty("online", out var on) && on.TryGetInt32(out var o)) online = o;
                if (pl.TryGetProperty("max", out var mx) && mx.TryGetInt32(out var m)) max = m;
            }

            string? favicon = null;
            if (root.TryGetProperty("favicon", out var fv) && fv.ValueKind == JsonValueKind.String)
                favicon = fv.GetString();

            return new ServerStatus
            {
                Ok = true,
                PingMs = pingMs,
                Motd = ExtractMotd(json),
                VersionName = StripColorCodes(verName),
                Protocol = protocol,
                Online = online,
                Max = max,
                FaviconBase64 = favicon
            };
        }
        catch (Exception ex)
        {
            return ServerStatus.Fail($"响应解析失败：{ex.Message}");
        }
    }

    /// <summary>向服务器发起一次 SLP 查询；任何失败都返回 Ok=false，不抛异常。</summary>
    public static async Task<ServerStatus> PingAsync(
        string host, int port = DefaultPort, int timeoutMs = DefaultTimeoutMs,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(host)) return ServerStatus.Fail("地址为空");

        var sw = Stopwatch.StartNew();
        try
        {
            using var tcp = new TcpClient();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeoutMs);

            await tcp.ConnectAsync(host, port, cts.Token).ConfigureAwait(false);
            var stream = tcp.GetStream();

            var hs = BuildHandshake(host, port);
            await stream.WriteAsync(hs, cts.Token).ConfigureAwait(false);
            await stream.WriteAsync(BuildStatusRequest(), cts.Token).ConfigureAwait(false);
            await stream.FlushAsync(cts.Token).ConfigureAwait(false);

            var buffer = new byte[64 * 1024];
            var total = 0;
            while (total < buffer.Length)
            {
                var n = await stream.ReadAsync(buffer.AsMemory(total), cts.Token).ConfigureAwait(false);
                if (n <= 0) break;
                total += n;

                // 尝试解析：长度 VarInt → 包 ID → 字符串 VarInt 长度 → JSON
                if (TryExtractJson(buffer.AsSpan(0, total), out var json))
                {
                    sw.Stop();
                    return ParseStatusJson(json, (int)sw.ElapsedMilliseconds);
                }
            }
            return ServerStatus.Fail("响应不完整");
        }
        catch (OperationCanceledException)
        {
            return ServerStatus.Fail("连接超时");
        }
        catch (Exception ex)
        {
            return ServerStatus.Fail(ex.Message);
        }
    }

    /// <summary>批量 ping 并把结果写回条目（并发受 <paramref name="concurrency"/> 限制）。</summary>
    public static async Task PingAllAsync(
        IEnumerable<ServerEntry> entries, int timeoutMs = DefaultTimeoutMs,
        int concurrency = 8, CancellationToken ct = default)
    {
        using var gate = new SemaphoreSlim(Math.Max(1, concurrency));
        var tasks = entries.Select(async e =>
        {
            await gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var st = await PingAsync(e.Host, e.Port, timeoutMs, ct).ConfigureAwait(false);
                e.PingMs = st.Ok ? st.PingMs : -1;
                e.Motd = st.Motd;
                e.OnlinePlayers = st.Online;
                e.MaxPlayers = st.Max;
                e.VersionName = st.VersionName;
            }
            finally
            {
                gate.Release();
            }
        });
        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    /// <summary>从原始响应字节中提取状态 JSON（不足则返回 false）。</summary>
    public static bool TryExtractJson(ReadOnlySpan<byte> data, out string json)
    {
        json = "";
        if (!TryReadVarInt(data, out var packetLen, out var used)) return false;
        if (packetLen <= 0 || data.Length < used + packetLen) return false;

        var body = data.Slice(used, packetLen);
        if (!TryReadVarInt(body, out var packetId, out var idUsed) || packetId != 0x00) return false;

        var rest = body[idUsed..];
        if (!TryReadVarInt(rest, out var strLen, out var lenUsed)) return false;
        if (strLen < 0 || rest.Length < lenUsed + strLen) return false;

        json = Encoding.UTF8.GetString(rest.Slice(lenUsed, strLen));
        return true;
    }
}
