using Chert.Core.Lan;

namespace Chert.Core.Tests;

/// <summary>
/// 邀请码（CHERT1:）编码自检：
/// 往返一致、可安全粘贴（Base64URL 无 +/= ）、改一个字符能被指纹发现、过期判定。
/// </summary>
public class LanInviteCodeTests
{
    private static LanInviteCode Build(long expiresAt, string endpoint) => new()
    {
        ExpiresAt = expiresAt,
        Endpoint = endpoint,
        DeviceName = "REMIX-PC",
        WorldName = "生存存档",
        McVersion = "1.20.1|fabric",
        PairCode = "955416"
    };

    private static LanInviteCode Sample(DateTimeOffset? now = null)
        => Build((now ?? DateTimeOffset.UtcNow).AddMinutes(10).ToUnixTimeSeconds(), "192.168.1.5:52931");

    [Fact]
    public void Encode_Then_Decode_保留全部字段()
    {
        var code = Sample().Encode();
        Assert.StartsWith(LanInviteCode.Prefix, code);

        Assert.True(LanInviteCode.TryDecode(code, out var parsed, out var err), err);
        Assert.NotNull(parsed);
        Assert.Equal("192.168.1.5:52931", parsed!.Endpoint);
        Assert.Equal("REMIX-PC", parsed.DeviceName);
        Assert.Equal("生存存档", parsed.WorldName);
        Assert.Equal("1.20.1|fabric", parsed.McVersion);
        Assert.Equal("955416", parsed.PairCode);
        Assert.Equal("invite", parsed.Kind);
    }

    [Fact]
    public void 编码只含_Base64URL_安全字符()
    {
        var code = Sample().Encode();
        Assert.DoesNotContain("+", code);
        Assert.DoesNotContain("/", code);
        Assert.DoesNotContain("=", code);
        // 单码不过长，二维码 / 聊天窗口都放得下
        Assert.True(code.Length < 260, $"邀请码过长：{code.Length}");
    }

    [Theory]
    [InlineData(true)]    // 改动载荷里的一个字符
    [InlineData(false)]   // 被聊天软件截断
    public void 被改动或被截断的码解析失败(bool mutate)
    {
        var code = Sample().Encode();
        var broken = mutate ? FlipOneChar(code) : code[..^6];

        Assert.False(LanInviteCode.TryDecode(broken, out _, out var err));
        Assert.False(string.IsNullOrEmpty(err));
    }

    [Fact]
    public void 内容不同则指纹不同()
    {
        var exp = DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeSeconds();
        var a = Build(exp, "192.168.1.5:52931").Encode();
        var b = Build(exp, "192.168.1.9:60000").Encode();

        Assert.True(LanInviteCode.TryDecode(a, out var ca, out _));
        Assert.True(LanInviteCode.TryDecode(b, out var cb, out _));
        Assert.NotEqual(ca!.Fingerprint, cb!.Fingerprint);

        // 同一份内容重复编码必须稳定（否则对方解不开自己刚生成的码）
        Assert.Equal(a, Build(exp, "192.168.1.5:52931").Encode());
    }

    private static string FlipOneChar(string code)
    {
        var chars = code.ToCharArray();
        var idx = code.Length - 6;              // 落在载荷段，不动前缀
        chars[idx] = chars[idx] == 'A' ? 'B' : 'A';
        return new string(chars);
    }

    [Fact]
    public void 过期码被拒绝()
    {
        var past = DateTimeOffset.UtcNow.AddMinutes(-30);
        var code = Sample(past).Encode();

        Assert.True(LanInviteCode.TryDecode(code, out var parsed, out _));
        Assert.True(parsed!.IsExpired());
        Assert.Equal(TimeSpan.Zero, parsed.Remaining());

        var (_, _, err) = LanInviteCode.Resolve(code);
        Assert.Equal("invite_expired", err);
    }

    [Fact]
    public void Resolve_兼容长码与裸地址()
    {
        var code = Sample().Encode();
        var (endpoint, parsed, err) = LanInviteCode.Resolve(code);
        Assert.Equal("", err);
        Assert.Equal("192.168.1.5:52931", endpoint);
        Assert.NotNull(parsed);

        // 裸地址照旧能解析（不带 Code）
        var (ep2, c2, err2) = LanInviteCode.Resolve("10.0.0.7:25565");
        Assert.Equal("", err2);
        Assert.Equal("10.0.0.7:25565", ep2);
        Assert.Null(c2);

        // 旧版二维码负载 chert-lan://host:port
        var (ep3, _, err3) = LanInviteCode.Resolve("chert-lan://192.168.1.9:12345");
        Assert.Equal("", err3);
        Assert.Equal("192.168.1.9:12345", ep3);
    }

    [Fact]
    public void 垃圾输入不抛异常且给出原因()
    {
        Assert.False(LanInviteCode.TryDecode("", out _, out var e1));
        Assert.Equal("invite_empty", e1);

        Assert.False(LanInviteCode.TryDecode("hello", out _, out var e2));
        Assert.Equal("invite_not_code", e2);

        Assert.False(LanInviteCode.TryDecode("CHERT1:@@@@", out _, out var e3));
        Assert.Contains(e3, new[] { "invite_bad_base64", "invite_bad_deflate", "invite_bad_cbor" });
    }
}
