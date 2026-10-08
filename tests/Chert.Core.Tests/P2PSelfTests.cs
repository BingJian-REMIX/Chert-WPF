using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Chert.Core.Lan.P2P;

namespace Chert.Core.Tests;

/// <summary>
/// 不需要第二台机器就能验的那部分。
/// <para>
/// 广域网直连有两块风险，一块来自「代码写错」，一块来自「NAT 不讲道理」。
/// 这里只管第一块 —— 隧道能否真的转发数据、打洞器能否互相看见、端口能不能被 QUIC 接手。
/// 第二块（真实 NAT 行为）留到真机上去验，没法在 CI 里模拟。
/// </para>
/// <para>需要 msquic 的用例在没有它的机器上直接跳过 —— 那是运行环境缺东西，不是代码错了。</para>
/// </summary>
public class P2PSelfTests
{
    private const string ReplyPrefix = "CHERT-FAKE-MC:";

    // ===== 打洞器 =====

    [Fact]
    public async Task 两个打洞器互发_双方都能看到对方()
    {
        var portA = P2PTunnel.PickFreePort(47400);
        var portB = P2PTunnel.PickFreePort(47410);
        var nonce = P2PIdentity.NewNonce();

        using var idA = P2PIdentity.TryCreate();
        using var idB = P2PIdentity.TryCreate();
        Assert.NotNull(idA);
        Assert.NotNull(idB);

        using var a = new UdpPuncher();
        using var b = new UdpPuncher();

        Assert.True(a.Start(portA, new[] { new IPEndPoint(IPAddress.Loopback, portB) }, nonce, idA!.PublicKey));
        Assert.True(b.Start(portB, new[] { new IPEndPoint(IPAddress.Loopback, portA) }, nonce, idB!.PublicKey));

        Assert.True(await WaitUntil(() => a.SawPeer && b.SawPeer, TimeSpan.FromSeconds(5)),
                    $"互相没看到对方：A={a.SawPeer} B={b.SawPeer}（已发 {a.SentPackets}/{b.SentPackets} 包）");

        Assert.Equal(portB, a.PeerAddress!.Port);
        Assert.Equal(portA, b.PeerAddress!.Port);
        Assert.True(a.SentPackets > 0);
        Assert.NotNull(a.PeerKeyHash);
    }

    [Fact]
    public async Task nonce不同的话互相不认账()
    {
        var portA = P2PTunnel.PickFreePort(47420);
        var portC = P2PTunnel.PickFreePort(47430);

        using var idA = P2PIdentity.TryCreate();
        using var idC = P2PIdentity.TryCreate();

        using var a = new UdpPuncher();
        using var c = new UdpPuncher();

        // 两个用不同 nonce：包能收到，但不该把它认成这次握手的人
        Assert.True(a.Start(portA, new[] { new IPEndPoint(IPAddress.Loopback, portC) }, P2PIdentity.NewNonce(), idA!.PublicKey));
        Assert.True(c.Start(portC, new[] { new IPEndPoint(IPAddress.Loopback, portA) }, P2PIdentity.NewNonce(), idC!.PublicKey));

        await Task.Delay(1200);

        Assert.False(a.SawPeer);
        Assert.False(c.SawPeer);
    }

    [Fact]
    public void 停止打洞后端口立刻能重新绑定()
    {
        // QUIC 必须沿用打洞用过的那个端口：换端口等于换一条 NAT 映射，刚戳开的洞就作废。
        // 这条要保证端口来得及释放，否则此后所有连接都会失败，而且症状会怪得很难查。
        var port = P2PTunnel.PickFreePort(47440);
        var puncher = new UdpPuncher();

        Assert.True(puncher.Start(port, new[] { new IPEndPoint(IPAddress.Loopback, 9) },
                                  P2PIdentity.NewNonce(), P2PIdentity.TryCreate()!.PublicKey));
        puncher.Stop();

        using var reuse = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        reuse.Bind(new IPEndPoint(IPAddress.Any, port));      // 占据中就抛，那才是失败
        Assert.Equal(port, ((IPEndPoint)reuse.LocalEndPoint!).Port);
    }

    [Fact]
    public void 打洞用的UDP端口能挑出一个能绑上的()
    {
        // 默认端口写死会撞车（上一次没退干净、别的程序占着），而症状是「打洞超时」——
        // 看起来跟路由器不支持直连一模一样。所以必须能往后找一个能用的。
        var port = P2PTunnel.PickFreeUdpPort(47600);
        Assert.True(port > 0, "没挑出可用端口");

        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.Bind(new IPEndPoint(IPAddress.Any, port));
        Assert.Equal(port, ((IPEndPoint)socket.LocalEndPoint!).Port);
    }

    [Fact]
    public void 同一网段的地址能被认出来_跨网段的不能()
    {
        // 打洞失败时这条判断决定给什么建议：同网段根本不用打洞，走局域网联动就行。
        var mine = P2PCandidateCollector.LocalAddresses()
                       .FirstOrDefault(x => x.AddressFamily == AddressFamily.InterNetwork);
        if (mine is null) return;      // 没网卡的机器上没法验

        Assert.True(P2PCandidateCollector.SharesLanWith(new[] { new IPEndPoint(mine, 25565) }));
        Assert.False(P2PCandidateCollector.SharesLanWith(new[] { new IPEndPoint(IPAddress.Loopback, 25565) }));
        // 文档地址段（203.0.113.0/24，RFC 5737）不可能是本机网段
        Assert.False(P2PCandidateCollector.SharesLanWith(new[] { new IPEndPoint(IPAddress.Parse("203.0.113.7"), 25565) }));
    }

    [Fact]
    public void 打洞包的格式是稳定且可判别的()
    {
        var nonce = P2PIdentity.NewNonce();
        var key = P2PIdentity.TryCreate()!.PublicKey;

        var packet = UdpPuncher.BuildPacket(nonce, key);
        Assert.Equal(4 + P2PIdentity.NonceSize + 6, packet.Length);

        Assert.True(UdpPuncher.TryParse(packet, packet.Length, nonce, out var hash));
        Assert.NotNull(hash);

        // 换一个 nonce 就该认不出来
        Assert.False(UdpPuncher.TryParse(packet, packet.Length, P2PIdentity.NewNonce(), out _));

        // 太短的包不该抛异常
        Assert.False(UdpPuncher.TryParse(packet, 10, nonce, out _));
    }

    // ===== QUIC 隧道 =====

    [Fact]
    public async Task 隧道自环_加入者能经QUIC拿到房主那头的数据()
    {
        if (!P2PTunnel.IsSupported) return;

        var mcPort = P2PTunnel.PickFreePort(47500);
        var hostUdp = P2PTunnel.PickFreePort(47510);
        var guestUdp = P2PTunnel.PickFreePort(47520);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        // 假 Minecraft：收到什么就加个前缀送回去
        var listener = new TcpListener(IPAddress.Loopback, mcPort);
        listener.Start();
        _ = Task.Run(async () =>
        {
            while (!cts.IsCancellationRequested)
            {
                try
                {
                    using var conn = await listener.AcceptTcpClientAsync(cts.Token);
                    await using var stream = conn.GetStream();
                    var buffer = new byte[256];
                    var read = await stream.ReadAsync(buffer, cts.Token);
                    var reply = Encoding.UTF8.GetBytes(ReplyPrefix + Encoding.UTF8.GetString(buffer, 0, read));
                    await stream.WriteAsync(reply, cts.Token);
                }
                catch { return; }
            }
        }, cts.Token);

        var secret = RandomNumberGenerator.GetBytes(32);

        using var host = new P2PTunnel(secret);
        await host.StartHostAsync(hostUdp, mcPort, cts.Token);

        using var guest = new P2PTunnel(secret);
        var proxyPort = await guest.ConnectAsync(guestUdp, new IPEndPoint(IPAddress.Loopback, hostUdp),
                                                 P2PTunnel.PreferredProxyPort, cts.Token);
        Assert.True(proxyPort > 0, "没起成本地代理");

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, proxyPort, cts.Token);
        await using var clientStream = client.GetStream();

        await clientStream.WriteAsync(Encoding.UTF8.GetBytes("hello"), cts.Token);
        var buffer2 = new byte[256];
        var got = await clientStream.ReadAsync(buffer2, cts.Token);

        Assert.Equal(ReplyPrefix + "hello", Encoding.UTF8.GetString(buffer2, 0, got));

        cts.Cancel();
        listener.Stop();
    }

    [Fact]
    public async Task 多条TCP连接各自独立_不会串数据()
    {
        if (!P2PTunnel.IsSupported) return;

        var mcPort = P2PTunnel.PickFreePort(47530);
        var hostUdp = P2PTunnel.PickFreePort(47540);
        var guestUdp = P2PTunnel.PickFreePort(47550);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        var listener = new TcpListener(IPAddress.Loopback, mcPort);
        listener.Start();
        _ = Task.Run(async () =>
        {
            while (!cts.IsCancellationRequested)
            {
                try
                {
                    using var conn = await listener.AcceptTcpClientAsync(cts.Token);
                    await using var stream = conn.GetStream();
                    var buffer = new byte[256];
                    var read = await stream.ReadAsync(buffer, cts.Token);
                    await stream.WriteAsync(Encoding.UTF8.GetBytes(ReplyPrefix + Encoding.UTF8.GetString(buffer, 0, read)), cts.Token);
                }
                catch { return; }
            }
        }, cts.Token);

        var secret = RandomNumberGenerator.GetBytes(32);
        using var host = new P2PTunnel(secret);
        await host.StartHostAsync(hostUdp, mcPort, cts.Token);

        using var guest = new P2PTunnel(secret);
        var proxyPort = await guest.ConnectAsync(guestUdp, new IPEndPoint(IPAddress.Loopback, hostUdp),
                                                 P2PTunnel.PreferredProxyPort, cts.Token);

        // Minecraft 会同时开好几条连接，各走一条 QUIC 流；串了就会握手失败，症状很隐蔽
        async Task<string> RoundTrip(string payload)
        {
            using var tcp = new TcpClient();
            await tcp.ConnectAsync(IPAddress.Loopback, proxyPort, cts.Token);
            await using var stream = tcp.GetStream();
            await stream.WriteAsync(Encoding.UTF8.GetBytes(payload), cts.Token);
            var buffer = new byte[256];
            var got = await stream.ReadAsync(buffer, cts.Token);
            return Encoding.UTF8.GetString(buffer, 0, got);
        }

        var first = await RoundTrip("one");
        var second = await RoundTrip("two");

        Assert.Equal(ReplyPrefix + "one", first);
        Assert.Equal(ReplyPrefix + "two", second);

        cts.Cancel();
        listener.Stop();
    }

    [Fact]
    public async Task 会话密钥不一致时应用层挑战会被拒()
    {
        if (!P2PTunnel.IsSupported) return;

        var mcPort = P2PTunnel.PickFreePort(47560);
        var hostUdp = P2PTunnel.PickFreePort(47570);
        var guestUdp = P2PTunnel.PickFreePort(47580);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var listener = new TcpListener(IPAddress.Loopback, mcPort);
        listener.Start();

        using var host = new P2PTunnel(RandomNumberGenerator.GetBytes(32));
        await host.StartHostAsync(hostUdp, mcPort, cts.Token);

        // 换一把密钥：QUIC 的 TLS 照样握得上（证书是自签的），
        // 但应用层的挑战—应答必须把它挡在门外
        using var guest = new P2PTunnel(RandomNumberGenerator.GetBytes(32));
        await Assert.ThrowsAnyAsync<Exception>(
            () => guest.ConnectAsync(guestUdp, new IPEndPoint(IPAddress.Loopback, hostUdp),
                                     P2PTunnel.PreferredProxyPort, cts.Token));

        cts.Cancel();
        listener.Stop();
    }

    [Fact]
    public async Task 心跳能穿过隧道_长时间不传游戏数据也不会被判死()
    {
        // 这条验的是真机上最常见的一种「莫名其妙断线」：NAT 的 UDP 映射一段时间没流量就被回收。
        // Minecraft 在加载地形、切维度时十几秒没有数据是常态，没有心跳的话洞就没了。
        if (!P2PTunnel.IsSupported) return;

        var mcPort = P2PTunnel.PickFreePort(47700);
        var hostUdp = P2PTunnel.PickFreeUdpPort(47710);
        var guestUdp = P2PTunnel.PickFreeUdpPort(47720);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        var listener = new TcpListener(IPAddress.Loopback, mcPort);
        listener.Start();
        _ = Task.Run(async () =>
        {
            while (!cts.IsCancellationRequested)
            {
                try
                {
                    using var conn = await listener.AcceptTcpClientAsync(cts.Token);
                    await using var stream = conn.GetStream();
                    var buffer = new byte[256];
                    var read = await stream.ReadAsync(buffer, cts.Token);
                    await stream.WriteAsync(Encoding.UTF8.GetBytes(ReplyPrefix + Encoding.UTF8.GetString(buffer, 0, read)), cts.Token);
                }
                catch { return; }
            }
        }, cts.Token);

        var secret = RandomNumberGenerator.GetBytes(32);
        using var host = new P2PTunnel(secret);
        await host.StartHostAsync(hostUdp, mcPort, cts.Token);

        using var guest = new P2PTunnel(secret);
        var proxyPort = await guest.ConnectAsync(guestUdp, new IPEndPoint(IPAddress.Loopback, hostUdp),
                                                 P2PTunnel.PreferredProxyPort, cts.Token);

        // 先通一次数据，确认隧道本身是好的
        await RoundTripAsync(proxyPort, "warmup", cts.Token);

        var before = host.LastActivityUtc;
        var wait = P2PTunnel.KeepAliveInterval + TimeSpan.FromSeconds(2);
        await Task.Delay(wait, cts.Token);

        // 全程没有发过一条游戏数据：房主这边还能看到动静，就只能是心跳送到了
        Assert.True(host.LastActivityUtc > before,
                    $"等了 {(int)wait.TotalSeconds} 秒房主都没收到任何东西 —— 心跳没穿过隧道，" +
                    "真机上会表现为『挂机一会儿就断线』");

        // 而且链路还真的能用
        Assert.Equal(ReplyPrefix + "still-alive", await RoundTripAsync(proxyPort, "still-alive", cts.Token));

        cts.Cancel();
        listener.Stop();
    }

    // ===== 辅助 =====

    private static async Task<string> RoundTripAsync(int proxyPort, string payload, CancellationToken ct)
    {
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, proxyPort, ct);
        await using var stream = tcp.GetStream();
        await stream.WriteAsync(Encoding.UTF8.GetBytes(payload), ct);
        var buffer = new byte[256];
        var got = await stream.ReadAsync(buffer, ct);
        return Encoding.UTF8.GetString(buffer, 0, got);
    }

    private static async Task<bool> WaitUntil(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return true;
            await Task.Delay(50);
        }
        return condition();
    }
}
