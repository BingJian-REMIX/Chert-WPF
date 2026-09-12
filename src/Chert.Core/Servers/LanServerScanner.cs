using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace Chert.Core.Servers;

/// <summary>发现到的局域网存档（"对局域网开放"）。</summary>
public sealed class LanServer
{
    /// <summary>广播方 IP。</summary>
    public string Address { get; init; } = "";

    /// <summary>端口（来自 [AD] 段）。</summary>
    public int Port { get; init; }

    /// <summary>MOTD（来自 [MOTD] 段，已剥离颜色码）。</summary>
    public string Motd { get; init; } = "";

    /// <summary>首次发现时间。</summary>
    public DateTime DiscoveredAt { get; init; } = DateTime.Now;

    /// <summary>最近一次收到广播的时间（用于超时下线）。</summary>
    public DateTime LastSeen { get; set; } = DateTime.Now;

    public string Endpoint => $"{Address}:{Port}";

    public override string ToString() => $"{Motd} ({Endpoint})";
}

/// <summary>
/// 局域网联机自动发现。
/// <para>Minecraft 在开启"对局域网开放"后，每 1.5 秒向组播地址 <c>224.0.2.60:4445</c>
/// 发送形如 <c>[MOTD]某某的世界[/MOTD][AD]52931[/AD]</c> 的 UDP 报文。</para>
/// 报文解析为纯函数，可离线自检；监听部分失败时静默返回空结果。
/// </summary>
public static class LanServerScanner
{
    /// <summary>Minecraft LAN 组播地址。</summary>
    public const string MulticastAddress = "224.0.2.60";

    /// <summary>Minecraft LAN 组播端口。</summary>
    public const int MulticastPort = 4445;

    /// <summary>超过该时长未再收到广播即认为已下线。</summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(10);

    private static readonly Regex MotdRegex =
        new(@"\[MOTD\](?<motd>.*?)\[/MOTD\]", RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex AdRegex =
        new(@"\[AD\](?<ad>.*?)\[/AD\]", RegexOptions.Compiled | RegexOptions.Singleline);

    /// <summary>
    /// 解析一条广播报文。<paramref name="sourceIp"/> 为发送方 IP；
    /// 当 [AD] 段形如 <c>host:port</c> 时以其中的 host 为准。
    /// </summary>
    public static LanServer? ParseBroadcast(string? payload, string sourceIp)
    {
        if (string.IsNullOrWhiteSpace(payload)) return null;

        var ad = AdRegex.Match(payload);
        if (!ad.Success) return null;

        var adText = ad.Groups["ad"].Value.Trim();
        if (adText.Length == 0) return null;

        var host = sourceIp;
        var portText = adText;
        var colon = adText.LastIndexOf(':');
        if (colon > 0)
        {
            host = adText[..colon];
            portText = adText[(colon + 1)..];
        }

        if (!int.TryParse(portText, out var port) || port is <= 0 or > 65535) return null;

        var motd = MotdRegex.Match(payload) is { Success: true } m
            ? ServerPinger.StripColorCodes(m.Groups["motd"].Value).Trim()
            : "Minecraft 局域网世界";

        return new LanServer
        {
            Address = string.IsNullOrWhiteSpace(host) ? sourceIp : host,
            Port = port,
            Motd = motd.Length == 0 ? "Minecraft 局域网世界" : motd
        };
    }

    /// <summary>构造一条标准广播报文（用于自检与本地测试）。</summary>
    public static string BuildBroadcast(string motd, int port) => $"[MOTD]{motd}[/MOTD][AD]{port}[/AD]";

    /// <summary>
    /// 在指定时长内监听组播，返回去重后的局域网服务器列表。
    /// 沙箱 / 无网络环境下会因无法加入组播而返回空列表，不抛异常。
    /// </summary>
    public static async Task<List<LanServer>> ScanAsync(
        int durationMs = 4000,
        Action<LanServer>? onFound = null,
        CancellationToken ct = default)
    {
        var found = new Dictionary<string, LanServer>(StringComparer.OrdinalIgnoreCase);
        UdpClient? udp = null;

        try
        {
            udp = new UdpClient(AddressFamily.InterNetwork);
            udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            udp.Client.Bind(new IPEndPoint(IPAddress.Any, MulticastPort));
            udp.JoinMulticastGroup(IPAddress.Parse(MulticastAddress));

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(durationMs);

            while (!cts.IsCancellationRequested)
            {
                UdpReceiveResult result;
                try
                {
                    result = await udp.ReceiveAsync(cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                var text = Encoding.UTF8.GetString(result.Buffer);
                var server = ParseBroadcast(text, result.RemoteEndPoint.Address.ToString());
                if (server is null) continue;

                if (found.TryGetValue(server.Endpoint, out var existing))
                {
                    existing.LastSeen = DateTime.Now;
                }
                else
                {
                    found[server.Endpoint] = server;
                    onFound?.Invoke(server);
                }
            }
        }
        catch
        {
            // 组播不可用（容器 / 无网卡 / 权限不足）：返回已发现的部分
        }
        finally
        {
            try { udp?.Dispose(); } catch { /* ignore */ }
        }

        return found.Values.OrderBy(s => s.Motd, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>剔除超时未再广播的条目，返回被剔除的数量。</summary>
    public static int PruneStale(IList<LanServer> list, DateTime? now = null)
    {
        var t = now ?? DateTime.Now;
        var removed = 0;
        for (var i = list.Count - 1; i >= 0; i--)
        {
            if (t - list[i].LastSeen <= StaleAfter) continue;
            list.RemoveAt(i);
            removed++;
        }
        return removed;
    }
}
