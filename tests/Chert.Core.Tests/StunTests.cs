using System.Net;
using System.Net.Sockets;
using Chert.Core.Lan.P2P;

namespace Chert.Core.Tests;

/// <summary>
/// STUN 的解析是纯字节操作，能在没有网络的情况下钉死；
/// 端到端那部分由一个本机环回上的假服务器完成 —— 不依赖外网，也不会因为 DNS 被挡而偶发失败。
/// </summary>
public class StunTests
{
    private const uint MagicCookie = 0x2112A442;

    // ===== 报文构造 =====

    [Fact]
    public void 请求头是标准的20字节()
    {
        var request = StunClient.BuildRequest();

        Assert.Equal(20, request.Length);
        Assert.Equal(0x00, request[0]);
        Assert.Equal(0x01, request[1]);                       // Binding Request
        Assert.Equal(0x00, request[2]);
        Assert.Equal(0x00, request[3]);                       // 没有 body
        Assert.Equal(0x21, request[4]);
        Assert.Equal(0x12, request[5]);
        Assert.Equal(0xA4, request[6]);
        Assert.Equal(0x42, request[7]);                       // magic cookie
    }

    [Fact]
    public void 不传事务id时每次都不一样()
    {
        var a = StunClient.BuildRequest();
        var b = StunClient.BuildRequest();

        Assert.NotEqual(a[8..20], b[8..20]);
    }

    // ===== 响应解析 =====

    [Fact]
    public void 能解开XOR混淆的IPv4地址()
    {
        var id = new byte[12];
        for (var i = 0; i < id.Length; i++) id[i] = (byte)(i + 1);

        var mapped = new IPEndPoint(IPAddress.Parse("203.0.113.7"), 40000);
        var response = BuildSuccess(id, mapped, xor: true);

        Assert.True(StunClient.TryParse(response, response.Length, id, out var parsed, out var error), error);
        Assert.Equal(mapped, parsed);
    }

    [Fact]
    public void 老式MAPPED_ADDRESS也能认()
    {
        var id = new byte[12];
        var mapped = new IPEndPoint(IPAddress.Parse("198.51.100.9"), 5000);
        var response = BuildSuccess(id, mapped, xor: false);

        Assert.True(StunClient.TryParse(response, response.Length, id, out var parsed, out _));
        Assert.Equal(mapped, parsed);
    }

    [Fact]
    public void IPv6的混淆要连事务id一起算()
    {
        var id = new byte[12];
        for (var i = 0; i < id.Length; i++) id[i] = (byte)(0xA0 + i);

        var mapped = new IPEndPoint(IPAddress.Parse("2001:db8::abcd"), 3478);
        var response = BuildSuccess(id, mapped, xor: true);

        Assert.True(StunClient.TryParse(response, response.Length, id, out var parsed, out var error), error);
        Assert.Equal(mapped.Address, parsed!.Address);
        Assert.Equal(3478, parsed.Port);
    }

    [Fact]
    public void 事务id不符的响应不会被当真()
    {
        var id = new byte[12];
        var other = new byte[12];
        other[0] = 0xFF;

        var response = BuildSuccess(id, new IPEndPoint(IPAddress.Parse("203.0.113.7"), 40000), xor: true);

        Assert.False(StunClient.TryParse(response, response.Length, other, out _, out var error));
        Assert.Equal("事务 id 不符", error);
    }

    [Fact]
    public void 不是STUN包的数据不会被误认()
    {
        var id = new byte[12];
        var garbage = new byte[64];
        Random.Shared.NextBytes(garbage);

        Assert.False(StunClient.TryParse(garbage, garbage.Length, id, out _, out var error));
        Assert.Equal("不是 STUN 包（魔数不符）", error);
    }

    [Fact]
    public void 前面带别的属性时也能跳过对齐读到映射()
    {
        var id = new byte[12];
        var mapped = new IPEndPoint(IPAddress.Parse("203.0.113.7"), 41000);

        // 故意放一个 5 字节的属性在前：它的 value 要垫到 8 字节，后面的偏移必须是 4 的倍数
        var junk = BuildSuccessWithJunk(id, mapped);

        Assert.True(StunClient.TryParse(junk, junk.Length, id, out var parsed, out var error), error);
        Assert.Equal(mapped, parsed);
    }

    // ===== 端到端：本机环回上的假 STUN 服务器 =====

    [Fact]
    public async Task 假服务器能问出本机的映射端点()
    {
        using var server = NewServer();
        var serverEp = (IPEndPoint)server.LocalEndPoint!;
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var loop = Task.Run(() => EchoLoop(server, stop.Token), stop.Token);

        using var client = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        client.Bind(new IPEndPoint(IPAddress.Loopback, 0));

        var result = await StunClient.QueryAsync(client, serverEp, TimeSpan.FromSeconds(3));

        Assert.True(result.Ok, result.Error);
        Assert.Equal(((IPEndPoint)client.LocalEndPoint!).Port, result.Mapped!.Port);
        Assert.Equal(IPAddress.Loopback, result.Mapped.Address);

        stop.Cancel();
    }

    [Fact]
    public async Task 服务器不回包时超时而不是卡死()
    {
        using var blackhole = NewServer();                 // 起了 socket 但从不回包
        var ep = (IPEndPoint)blackhole.LocalEndPoint!;

        using var client = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        client.Bind(new IPEndPoint(IPAddress.Loopback, 0));

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var result = await StunClient.QueryAsync(client, ep, TimeSpan.FromMilliseconds(400));

        stopwatch.Stop();
        Assert.False(result.Ok);
        Assert.Equal("没有收到服务器回应", result.Error);
        // 两次重发 × 400ms + 收尾，给到 2.5 秒的余量够宽
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(3), $"不该等这么久：{stopwatch.Elapsed}");
    }

    // ===== NAT 行为判定（纯逻辑） =====

    [Fact]
    public void 三个都没变是锥形NAT()
    {
        var base1 = Ep("1.1.1.1:100");
        var same = Ep("1.1.1.1:100");
        var other = Ep("1.1.1.1:100");

        Assert.Equal(NatMappingBehavior.EndpointIndependent, NatProbe.Classify(base1, same, other));
    }

    [Fact]
    public void 换目的端口就换端口是对称NAT()
    {
        var base1 = Ep("1.1.1.1:100");
        Assert.Equal(NatMappingBehavior.AddressAndPortDependent, NatProbe.Classify(base1, Ep("1.1.1.1:101"), Ep("1.1.1.1:100")));
    }

    [Fact]
    public void 换目的IP才换IP是依赖地址的NAT()
    {
        var base1 = Ep("1.1.1.1:100");
        Assert.Equal(NatMappingBehavior.AddressDependent, NatProbe.Classify(base1, Ep("1.1.1.1:100"), Ep("1.1.1.1:200")));
    }

    [Fact]
    public void 什么都没测出来时坦白说不知道()
    {
        Assert.Equal(NatMappingBehavior.Unknown, NatProbe.Classify(null, null, null));
        Assert.Equal(NatMappingBehavior.Unknown, NatProbe.Classify(Ep("1.1.1.1:100"), null, null));
    }

    [Fact]
    public void 只测到换IP时仍能给出结论()
    {
        Assert.Equal(NatMappingBehavior.EndpointIndependent, NatProbe.Classify(Ep("1.1.1.1:100"), null, Ep("1.1.1.1:100")));
        Assert.Equal(NatMappingBehavior.AddressDependent, NatProbe.Classify(Ep("1.1.1.1:100"), null, Ep("1.1.1.1:101")));
    }

    [Fact]
    public void 只有锥形NAT才被认为打得通()
    {
        bool Punchable(NatMappingBehavior b) => new NatProbeResult { Ok = true, Mapping = b }.LikelyPunchable;

        Assert.True(Punchable(NatMappingBehavior.EndpointIndependent));
        Assert.False(Punchable(NatMappingBehavior.AddressDependent));
        Assert.False(Punchable(NatMappingBehavior.AddressAndPortDependent));
        Assert.False(Punchable(NatMappingBehavior.Unknown));
    }

    [Fact]
    public void UDP不通时给的是专门的提示而不是通用失败()
    {
        var blocked = new NatProbeResult { Ok = false, UdpLooksBlocked = true };
        Assert.Equal("nat.udp_blocked", blocked.Key);
        Assert.Equal("nat.unknown", new NatProbeResult { Ok = false }.Key);
    }

    // ===== 辅助 =====

    private static IPEndPoint Ep(string text)
    {
        var parts = text.Split(':');
        return new IPEndPoint(IPAddress.Parse(parts[0]), int.Parse(parts[1]));
    }

    private static Socket NewServer()
    {
        var server = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        server.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        return server;
    }

    private static void EchoLoop(Socket server, CancellationToken ct)
    {
        var buffer = new byte[512];
        EndPoint from = new IPEndPoint(IPAddress.Any, 0);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                server.ReceiveTimeout = 200;
                var received = server.ReceiveFrom(buffer, ref from);
                if (received < 20) continue;
                if (buffer[0] != 0x00 || buffer[1] != 0x01) continue;

                var id = buffer[8..20];
                var reply = BuildSuccess(id, (IPEndPoint)from, xor: true);
                server.SendTo(reply, from);
            }
            catch (SocketException) { }
            catch (ObjectDisposedException) { return; }
        }
    }

    private static byte[] BuildSuccess(byte[] id, IPEndPoint mapped, bool xor)
    {
        var ipv6 = mapped.AddressFamily == AddressFamily.InterNetworkV6;
        var valueLength = ipv6 ? 20 : 8;
        var message = new byte[20 + 4 + valueLength];

        Write16(message, 0, 0x0101);                       // Binding Success Response
        Write16(message, 2, (ushort)(4 + valueLength));
        Write32(message, 4, MagicCookie);
        id.CopyTo(message, 8);

        Write16(message, 20, xor ? (ushort)0x0020 : (ushort)0x0001);
        Write16(message, 22, (ushort)valueLength);
        message[24] = 0;
        message[25] = ipv6 ? (byte)0x02 : (byte)0x01;

        var portValue = (ushort)(mapped.Port ^ (xor ? (ushort)(MagicCookie >> 16) : 0));
        Write16(message, 26, portValue);

        var address = mapped.Address.GetAddressBytes();
        for (var i = 0; i < address.Length; i++)
        {
            var key = !xor ? (byte)0
                    : i < 4 ? (byte)((MagicCookie >> (24 - i * 8)) & 0xFF)
                            : id[i - 4];
            message[28 + i] = (byte)(address[i] ^ key);
        }

        return message;
    }

    private static byte[] BuildSuccessWithJunk(byte[] id, IPEndPoint mapped)
    {
        var clean = BuildSuccess(id, mapped, xor: true);

        // 插一个 SOFTWARE 属性（值长 5 字节，需垫到 8）：值为 "abcde"
        const ushort software = 0x8022;
        var junkValue = new byte[] { 0x61, 0x62, 0x63, 0x64, 0x65, 0, 0, 0 };
        var result = new byte[clean.Length + 4 + junkValue.Length];
        clean.CopyTo(result, 0);

        // body 长度要改写在头部第 2-3 字节，属性值整体搬动到新位置
        Write16(result, 2, (ushort)(Read16(clean, 2) + 4 + junkValue.Length));
        Write16(result, 20, software);
        Write16(result, 22, 5);
        junkValue.CopyTo(result, 24);
        Array.Copy(clean, 20, result, 24 + junkValue.Length, clean.Length - 20);

        return result;
    }

    private static void Write16(byte[] target, int offset, ushort value)
    {
        target[offset] = (byte)(value >> 8);
        target[offset + 1] = (byte)value;
    }

    private static void Write32(byte[] target, int offset, uint value)
    {
        target[offset] = (byte)(value >> 24);
        target[offset + 1] = (byte)(value >> 16);
        target[offset + 2] = (byte)(value >> 8);
        target[offset + 3] = (byte)value;
    }

    private static ushort Read16(byte[] source, int offset) => (ushort)((source[offset] << 8) | source[offset + 1]);
}
