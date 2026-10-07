using Chert.Core.Lan.P2P;

namespace Chert.Core.Tests;

public class P2POfferTests
{
    [Fact]
    public void 平台支持_X25519_且两端派生出同一个会话密钥()
    {
        Assert.True(P2PIdentity.IsSupported, $"当前平台不支持 X25519：{P2PIdentity.UnsupportedReason}");

        using var a = P2PIdentity.TryCreate();
        using var b = P2PIdentity.TryCreate();
        Assert.NotNull(a);
        Assert.NotNull(b);

        var offerNonce = P2PIdentity.NewNonce();
        var answerNonce = P2PIdentity.NewNonce();

        var sa = a!.DeriveSessionSecret(b!.PublicKey, offerNonce, answerNonce);
        var sb = b.DeriveSessionSecret(a.PublicKey, offerNonce, answerNonce);

        Assert.Equal(32, sa.Length);
        Assert.Equal(sa, sb);

        // 换一个 nonce 组合就换一个密钥（重放旧包无效）
        Assert.NotEqual(sa, a.DeriveSessionSecret(b.PublicKey, offerNonce, P2PIdentity.NewNonce()));
    }

    private const int PrefixLength = 7;   // "CHERT2:"

    [Fact]
    public void 每次生成的临时身份都不同()
    {
        if (!P2PIdentity.IsSupported) return;

        using var a = P2PIdentity.TryCreate();
        using var b = P2PIdentity.TryCreate();

        Assert.NotEqual(a!.PublicKey, b!.PublicKey);
        Assert.True(P2PIdentity.IsValidPublicKey(a.PublicKey));
        // 指纹可能碰撞，但连续两次不可能都是同一个
        Assert.NotEqual(a.Fingerprint, b.Fingerprint);
    }

    [Fact]
    public void 挑战应答能区分持有私钥的人和旁观者()
    {
        if (!P2PIdentity.IsSupported) return;

        using var a = P2PIdentity.TryCreate();
        using var b = P2PIdentity.TryCreate();
        var secret = a!.DeriveSessionSecret(b!.PublicKey, P2PIdentity.NewNonce(), P2PIdentity.NewNonce());

        var challenge = P2PIdentity.NewNonce();
        var answer = P2PIdentity.Answer(secret, challenge);

        Assert.True(P2PIdentity.Verify(answer, P2PIdentity.Answer(secret, challenge)));
        // 只知道李子 Hemingway 的人算不出：换 challenge 答案就变
        Assert.False(P2PIdentity.Verify(answer, P2PIdentity.Answer(secret, P2PIdentity.NewNonce())));
    }

    private static P2POffer Sample(P2PIdentity? id = null) => new()
    {
        ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeSeconds(),
        Nonce = P2PIdentity.NewNonce(),
        PublicKey = id?.PublicKey ?? new byte[32],
        Candidates = new List<string> { "udp://192.168.1.5:47810", P2PCandidate.FormatUri(new System.Net.IPEndPoint(System.Net.IPAddress.Parse("2001:db8::1"), 47810)) },
        DeviceName = "REMIX-PC",
        McVersion = "1.20.1|fabric",
        WorldName = "生存存档",
        McPort = 54321
    };

    [Fact]
    public void offer_往返一致()
    {
        using var id = P2PIdentity.TryCreate();
        var offer = Sample(id);

        var code = offer.Encode();
        Assert.StartsWith(P2POffer.Prefix, code);

        Assert.True(P2POffer.TryDecode(code, out var back, out var err), err);
        Assert.Equal(offer.ExpiresAt, back!.ExpiresAt);
        Assert.Equal(offer.Nonce, back.Nonce);
        Assert.Equal(offer.PublicKey, back.PublicKey);
        Assert.Equal(offer.McPort, back.McPort);
        Assert.Equal(offer.DeviceName, back.DeviceName);
        Assert.Equal(offer.Candidates, back.Candidates);
        Assert.Equal(P2POffer.ComputeFingerprint(offer.PublicKey), back.Fingerprint);
    }

    [Fact]
    public void answer_与_offer_都能往返()
    {
        var answer = new P2POffer
        {
            Kind = P2POfferKind.Answer,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeSeconds(),
            Nonce = P2PIdentity.NewNonce(),
            PublicKey = new byte[32],
            Candidates = new List<string> { "udp://192.168.1.9:47810" }
        };
        Assert.True(P2POffer.TryDecode(answer.Encode(), out var back, out _));
        Assert.Equal(P2POfferKind.Answer, back!.Kind);
        Assert.Equal(0, back.McPort);
    }

    [Fact]
    public void 只含_Base64URL_安全字符()
    {
        using var id = P2PIdentity.TryCreate();
        var payload = Sample(id).Encode()[P2POffer.Prefix.Length..];

        Assert.All(payload, c => Assert.True(char.IsLetterOrDigit(c) || c is '-' or '_',
            $"Base64URL 里不该出现 {c}（聊天软件会把 + / = 转义掉）"));
    }

    [Fact]
    public void 改一个字符会被判为破损()
    {
        using var id = P2PIdentity.TryCreate();
        var code = Sample(id).Encode();

        // 动中间而不是尾部：Base64URL 尾部有冗余比特，抹掉最后一个字符有时根本不影响载荷
        var chars = code.ToCharArray();
        var idx = PrefixLength + (code.Length - PrefixLength) / 2;
        chars[idx] = chars[idx] == 'A' ? 'B' : 'A';

        Assert.False(P2POffer.TryDecode(new string(chars), out _, out var err));
        Assert.True(err is "p2p_fingerprint_mismatch" or "p2p_bad_deflate" or "p2p_bad_base64", err);
    }

    [Fact]
    public void 截断一半会被判为破损()
    {
        using var id = P2PIdentity.TryCreate();
        var code = Sample(id).Encode();

        Assert.False(P2POffer.TryDecode(code[..(code.Length / 2)], out _, out _));
    }

    [Fact]
    public void 粘贴_CHERT1_邀请码不会被误认成握手码()
    {
        Assert.False(P2POffer.TryDecode("CHERT1:abc", out _, out var err));
        Assert.Equal("p2p_not_code", err);
    }

    [Fact]
    public void 垃圾输入不会抛异常()
    {
        foreach (var junk in new[] { "", "   ", "CHERT2:", "CHERT2:!!!!", "CHERT2:QQQQ", "hello world" })
            Assert.False(P2POffer.TryDecode(junk, out _, out _));
    }

    [Fact]
    public void 过期判定()
    {
        var expired = new P2POffer
        {
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeSeconds(),
            Nonce = P2PIdentity.NewNonce(),
            PublicKey = new byte[32]
        };
        Assert.True(expired.IsExpired());
        Assert.Equal(TimeSpan.Zero, expired.Remaining());

        var fresh = Sample();
        Assert.False(fresh.IsExpired());
        Assert.True(fresh.Remaining() > TimeSpan.FromMinutes(9));
    }

    [Theory]
    [InlineData("udp://192.168.1.5:47810", "192.168.1.5", 47810)]
    [InlineData("192.168.1.5:47810", "192.168.1.5", 47810)]
    [InlineData("udp://[2001:db8::1]:47810", "2001:db8::1", 47810)]
    [InlineData("[2001:db8::1]:47810", "2001:db8::1", 47810)]
    public void 候选地址的三种写法都能解析(string text, string host, int port)
    {
        var ep = P2PCandidate.TryParse(text);
        Assert.NotNull(ep);
        Assert.Equal(host, ep!.Address.ToString());
        Assert.Equal(port, ep.Port);
    }

    [Fact]
    public void IPv6_地址必须带方括号否则端口会被吃掉()
    {
        var ep = new System.Net.IPEndPoint(System.Net.IPAddress.Parse("2001:db8::1"), 47810);
        Assert.Equal("udp://[2001:db8::1]:47810", P2PCandidate.FormatUri(ep));
        Assert.NotNull(P2PCandidate.TryParse(P2PCandidate.FormatUri(ep)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("udp://")]
    [InlineData("not-an-ip:47810")]
    [InlineData("udp://1.2.3.4:99999")]
    public void 脏候选地址返回_null_而不是抛(string text)
        => Assert.Null(P2PCandidate.TryParse(text));

    [Fact]
    public void 打洞包只认同一次握手的_nonce()
    {
        var nonce = P2PIdentity.NewNonce();
        var packet = UdpPuncher.BuildPacket(nonce, new byte[32]);

        Assert.True(UdpPuncher.TryParse(packet, packet.Length, nonce, out var hash));
        Assert.Equal(6, hash!.Length);
        Assert.False(UdpPuncher.TryParse(packet, packet.Length, P2PIdentity.NewNonce(), out _));

        // 短包 / 其它协议的包不当自己人
        Assert.False(UdpPuncher.TryParse(packet, 8, nonce, out _));
    }

    [Fact]
    public void 本机至少能收集到一个候选地址()
    {
        var addrs = P2PCandidateCollector.LocalAddresses();
        Assert.NotEmpty(addrs);
        Assert.Contains(addrs, x => x.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
    }
}
