using System.Net;
using System.Net.Sockets;

namespace Chert.Core.Lan.P2P;

/// <summary>
/// NAT 的映射行为（RFC 4787 的术语）。决定「对方能否用我从 STUN 问来的地址连到我」。
/// <para>
/// 本机同一个 UDP 端口发出去的包，NAT 会给它分配一个公网 ip:port。
/// 关键是这个分配只看 (内网 ip:port)，还是连目的地址一起看：
/// <list type="bullet">
/// <item>只看内网 ip:port —— 无论发给谁都是同一个公网端口（<see cref="EndpointIndependent"/>，俗称锥形 NAT）。
///       这时 STUN 问到的地址对任何人都成立，打洞能成功。</item>
/// <item>还看目的 IP —— 发给张三和发给李四得到不同的公网端口（<see cref="AddressDependent"/>）。
///       STUN 问到的地址只对 STUN 服务器那头的通信有效，对方照着发多半进不来。</item>
/// <item>连目的端口也看 —— 只有完全相同的目标 (ip:port) 才复用同一映射（<see cref="AddressAndPortDependent"/>，
///       俗称对称 NAT）。这条连 мероприятий: prediction 都救不了，直连基本判死刑。</item>
/// </list>
/// </para>
/// </summary>
public enum NatMappingBehavior
{
    Unknown = 0,
    EndpointIndependent = 1,
    AddressDependent = 2,
    AddressAndPortDependent = 3
}

/// <summary>一次 NAT 行为探测的结果。</summary>
public sealed class NatProbeResult
{
    public bool Ok { get; init; }

    /// <summary>可用作候选的公网映射端点。</summary>
    public IPEndPoint? ServerReflexive { get; init; }

    public NatMappingBehavior Mapping { get; init; }

    /// <summary>所有 STUN 服务器都没回应 —— 多半是 UDP 出不去，而不是服务器都挂了。</summary>
    public bool UdpLooksBlocked { get; init; }

    public string Server { get; init; } = "";

    public string Error { get; init; } = "";

    /// <summary>锥形 NAT（或本身就是公网 IP）才可能打洞成功。</summary>
    public bool LikelyPunchable => Ok && Mapping == NatMappingBehavior.EndpointIndependent;

    /// <summary>本地化键，UI 层拿去翻译。</summary>
    public string Key => !Ok
        ? UdpLooksBlocked ? "nat.udp_blocked" : "nat.unknown"
        : Mapping switch
        {
            NatMappingBehavior.EndpointIndependent => "nat.eim",
            NatMappingBehavior.AddressDependent => "nat.ad",
            NatMappingBehavior.AddressAndPortDependent => "nat.apd",
            _ => "nat.unknown"
        };

    public override string ToString()
        => Ok ? $"{Mapping} via {Server} → {ServerReflexive}" : $"failed: {Error}";
}

/// <summary>
/// NAT 行为探测：连着问三家公司，看 NAT 怎么分配端口。
/// <para>
/// 三次查询刻意从**同一个 socket**（也就是同一个本地端口）发出：
/// 换 socket 就等于换了一条映射，比出来的结果毫无意义。
/// 依次为：① 服务器 A ② 服务器 A 的另一个端口 ③ 服务器 B（不同 IP）。
/// 组合起来的差异正好对应上面三种行为，不需要服务器支持 CHANGE-REQUEST ——
/// 公共 STUN 服务器大多不支持那个扩展属性，靠服务器组合反而通用。
/// </para>
/// </summary>
public static class NatProbe
{
    /// <summary>
    /// 探测本机 NAT 行为。<paramref name="localPort"/> 必须是后面真正用来打洞的端口。
    /// </summary>
    public static async Task<NatProbeResult> ProbeAsync(int localPort,
                                                        IReadOnlyList<string>? servers = null,
                                                        TimeSpan? perQueryTimeout = null,
                                                        CancellationToken ct = default)
    {
        // DNS 是同步阻塞的，别拖住调用线程（UI 线程很可能就在外面等）
        var list = await Task.Run(() => Resolve(servers ?? StunClient.DefaultServers), ct).ConfigureAwait(false);
        if (list.Count == 0)
            return new NatProbeResult { Ok = false, Error = "一个 STUN 服务器地址都解析不出来" };

        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        try
        {
            socket.Bind(new IPEndPoint(IPAddress.Any, localPort));
        }
        catch (SocketException ex)
        {
            return new NatProbeResult { Ok = false, Error = $"本地 UDP 端口 {localPort} 绑定失败：{ex.SocketErrorCode}" };
        }

        // ① 服务器 A
        var first = await StunClient.QueryAsync(socket, list[0], perQueryTimeout, ct).ConfigureAwait(false);
        if (!first.Ok || first.Mapped is null)
        {
            return new NatProbeResult
            {
                Ok = false,
                UdpLooksBlocked = true,
                Error = $"STUN 全部没回应（试了 {list.Count} 个）。UDP 可能被防火墙挡住了，或需要自己在下面指定能通的 STUN 服务器。"
            };
        }

        var baseEp = first.Mapped;

        // ② 同一台服务器的另一个端口：多数公共 STUN 同时开 3478 与 19302
        IPEndPoint? sameHostOtherPort = null;
        var alt = SameHostOtherPort(list);
        if (alt is not null)
        {
            var second = await StunClient.QueryAsync(socket, alt, perQueryTimeout, ct).ConfigureAwait(false);
            if (second.Ok) sameHostOtherPort = second.Mapped;
        }

        // ③ 换一台服务器（不同 IP）
        IPEndPoint? otherHost = null;
        var third = OtherHost(list, list[0]);
        if (third is not null)
        {
            var thirdResult = await StunClient.QueryAsync(socket, third, perQueryTimeout, ct).ConfigureAwait(false);
            if (thirdResult.Ok) otherHost = thirdResult.Mapped;
        }

        return new NatProbeResult
        {
            Ok = true,
            ServerReflexive = baseEp,
            Mapping = Classify(baseEp, sameHostOtherPort, otherHost),
            Server = list[0].ToString() ?? ""
        };
    }

    /// <summary>
    /// 纯判定逻辑（抽出来是为了能在没有网络的情况下单测）。
    /// <para>只要就算不了「连目的端口也看」这一项（没拿到 ② 的结果），就退化为只比 ③。</para>
    /// </summary>
    public static NatMappingBehavior Classify(IPEndPoint? baseline, IPEndPoint? sameHostOtherPort, IPEndPoint? otherHost)
    {
        if (baseline is null) return NatMappingBehavior.Unknown;

        // 换目的端口就换映射 —— 对称 NAT，没得谈
        if (sameHostOtherPort is not null &&
            (sameHostOtherPort.Port != baseline.Port || !sameHostOtherPort.Address.Equals(baseline.Address)))
            return NatMappingBehavior.AddressAndPortDependent;

        // 换目的 IP 就换映射 —— 依赖地址的 NAT
        if (otherHost is not null &&
            (otherHost.Port != baseline.Port || !otherHost.Address.Equals(baseline.Address)))
            return NatMappingBehavior.AddressDependent;

        // 都拿到了且都一样 —— 锥形 NAT
        if (sameHostOtherPort is not null && otherHost is not null)
            return NatMappingBehavior.EndpointIndependent;

        // 只测到了 ①：只能说「至少没发现变化」，不能断定是锥形，
        // 但这时也没有理由拦着用户 —— 真打不通会走到超时，届时再给建议
        return sameHostOtherPort is null && otherHost is null
            ? NatMappingBehavior.Unknown
            : NatMappingBehavior.EndpointIndependent;
    }

    // ===== 服务器列表处理 =====

    private static List<IPEndPoint> Resolve(IReadOnlyList<string> servers)
    {
        var result = new List<IPEndPoint>();
        foreach (var text in servers)
        {
            foreach (var ip in StunClient.ResolveEntries(text))
            {
                if (!result.Any(x => x.Equals(ip))) result.Add(ip);
            }
        }
        return result;
    }

    private static IPEndPoint? SameHostOtherPort(List<IPEndPoint> list)
    {
        if (list.Count == 0) return null;
        var first = list[0];
        var altPort = first.Port == 3478 ? 19302 : 3478;
        return list.FirstOrDefault(x => x.Address.Equals(first.Address) && x.Port == altPort);
    }

    private static IPEndPoint? OtherHost(List<IPEndPoint> list, IPEndPoint first)
        => list.FirstOrDefault(x => !x.Address.Equals(first.Address));
}
