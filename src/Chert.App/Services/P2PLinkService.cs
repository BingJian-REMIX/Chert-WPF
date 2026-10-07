using System.Diagnostics;
using System.Net;
using Chert.Core;
using Chert.Core.Lan.P2P;
using Chert.Core.Profiles;
using Chert.Core.Utils;

namespace Chert.App.Services;

/// <summary>本端在这次直连里扮演的角色。</summary>
public enum P2PRole
{
    None,
    /// <summary>房主：世界在他这儿，隧道终点是他的 MC 端口。</summary>
    Host,
    /// <summary>加入者：连到房主的世界，本机开一个端口给 Minecraft 连。</summary>
    Joiner
}

/// <summary>直连进度。</summary>
public enum P2PStage
{
    Idle,
    /// <summary>码已生成，等对方把他那一半发过来。</summary>
    CodeReady,
    Punching,
    Connecting,
    Ready,
    Failed
}

/// <summary>
/// 广域网直连的编排器：把「生成握手码 → 互换 → UDP 打洞 → QUIC 隧道 → 本地代理」串成一条线。
/// <para>
/// 为什么要分两步（先出码、后连接）：没有服务器，双方必须**手工**传递各自的码，
/// 中间人要花时间（微信发过去、对方粘回来）。所以三个阶段不能一股脑跑完，
/// 必须由用户在界面上推进：「我的码」→ 对方把他那半发回来 → 「连接」。
/// </para>
/// </summary>
public sealed class P2PLinkService : IDisposable
{
    public static P2PLinkService Instance { get; } = new();

    private const string PunchFailedHint =
        "打洞失败：看起来双方都在 NAT 后面且路由器不允许直连。" +
        "可以试试：① 开启 IPv6（有 IPv6 通常不用打洞）；② 在路由器里开启 UPnP；" +
        "③ 把公网 IP 填进上面的输入框；④ 同一个局域网就用上面的局域网联动，不用走这里。";

    private P2PIdentity? _identity;
    private P2POffer? _localOffer;
    private P2POffer? _peerOffer;
    private byte[] _myNonce = Array.Empty<byte>();
    private UdpPuncher? _puncher;
    private P2PTunnel? _tunnel;
    private P2PRole _role = P2PRole.None;
    private int _localPort = P2POffer.DefaultUdpPort;
    private int _mcPort;
    private bool _disposed;
    private NatProbeResult? _nat;
    private IReadOnlyList<P2PCandidate> _candidates = Array.Empty<P2PCandidate>();

    /// <summary>
    /// 自定义 STUN 服务器（每项 <c>host:port</c>）；留空用内置公开列表。
    /// 公司内网、校园网、某些宽带下，常常只有用户指定的那台才通 —— 所以留了这个口子。
    /// </summary>
    public IReadOnlyList<string> StunServers { get; set; } = Array.Empty<string>();

    /// <summary>最近一次 NAT 行为探测的结论（还没探测过为 null）。</summary>
    public NatProbeResult? Nat => _nat;

    /// <summary>本机这一侧能否被对方直连到。没测出来时乐观放行（真打不通会走到超时，那时再给建议）。</summary>
    public bool CanPunch => _nat?.LikelyPunchable ?? true;

    /// <summary>状态 / 进度变化（UI 直接显示 <see cref="StatusText"/>）。</summary>
    public event Action? Changed;

    public P2PStage Stage { get; private set; } = P2PStage.Idle;

    public P2PRole Role => _role;

    public string StatusText { get; private set; } = "";

    /// <summary>这条链路对运行时的两个硬要求：X25519（密钥交换）与 QUIC（隧道）都得可用。</summary>
    public static bool PlatformSupported => P2PIdentity.IsSupported && P2PTunnel.IsSupported;
    public string LocalCode { get; private set; } = "";

    /// <summary>对方的码（原始文本，UI 只做回显）。</summary>
    public string PeerCode { get; private set; } = "";

    public string LocalFingerprint => _localOffer?.Fingerprint ?? "";

    public string PeerFingerprint => _peerOffer?.Fingerprint ?? "";

    public bool IsBusy => Stage is P2PStage.Punching or P2PStage.Connecting;

    /// <summary>加入者端本机给 Minecraft 连的端口（0 = 还没开）。</summary>
    public int LocalProxyPort { get; private set; }

    /// <summary>最后一次失败的原因（为空表示没失败）。</summary>
    public string Failure { get; private set; } = "";

    public bool HasCode => LocalCode.Length > 0;

    public bool IsReady => Stage == P2PStage.Ready;

    // ===== 房主流程 =====

    /// <summary>第 1 步：生成邀请码（含本机候选地址与临时公钥）。</summary>
    /// <param name="mcPort">本机 Minecraft 已开放的端口（对方要连的最终目标）。</param>
    public async Task<bool> StartAsHostAsync(int mcPort, string? mcVersion, string? worldName,
                                             bool tryUpnp = true, string? manualExternalIp = null)
    {
        if (!CheckPlatform()) return false;
        if (mcPort <= 0)
        {
            Fail("还不知道本机 Minecraft 的端口。先点上面的「读取本机端口」，或者手动填世界地址。");
            return false;
        }

        Reset();
        _identity = P2PIdentity.TryCreate();
        if (_identity is null)
        {
            Fail($"建不出这次连接要用的密钥：{P2PIdentity.UnsupportedReason}");
            return false;
        }

        _role = P2PRole.Host;
        _mcPort = mcPort;

        if (!await CollectCandidatesAsync(tryUpnp, manualExternalIp)) return false;

        _localOffer = BuildOffer(P2POfferKind.Offer, _candidates, mcVersion, worldName, mcPort);
        LocalCode = _localOffer.Encode();
        Stage = P2PStage.CodeReady;
        StatusText = $"已生成邀请码（{_candidates.Count} 个候选地址）。把它发给对方，然后粘贴对方回给你的应答码。";
        if (_nat is { } nat) StatusText += Environment.NewLine + DescribeNat(nat);
        Raise();
        return true;
    }

    /// <summary>第 2 步：粘贴对方的应答码 → 打洞 → 起 QUIC 监听。</summary>
    public async Task<bool> CompleteAsHostAsync(string? answerText)
    {
        if (_localOffer is null) { Fail("先生成邀请码"); return false; }
        if (!TryParsePeer(answerText, P2POfferKind.Answer, out var answer, out var error))
        {
            Fail(error);
            return false;
        }

        PeerCode = answer!.Encode();
        _peerOffer = answer;

        var secret = DeriveSecret();
        if (secret is null) return false;

        Stage = P2PStage.Punching;
        Raise();

        var peer = await PunchAsync(CandidatesOf(answer), TimeSpan.FromSeconds(12));
        if (peer is null) return false;

        try
        {
            Stage = P2PStage.Connecting;
            StatusText = "打洞成功，正在建立加密隧道…";
            Raise();

            _tunnel = new P2PTunnel(secret);
            await _tunnel.StartHostAsync(_localPort, _mcPort).ConfigureAwait(false);

            Stage = P2PStage.Ready;
            StatusText = $"隧道已就绪（对方 {peer}）。对方会在自己的 Minecraft 里输入 localhost —— " +
                         $"你这边什么都不用做，世界已经开在本机 {_mcPort} 端口上了。";
            Raise();
            return true;
        }
        catch (Exception ex)
        {
            Fail($"隧道建立失败：{ex.Message}");
            return false;
        }
    }

    // ===== 加入者流程 =====

    /// <summary>第 1 步：粘贴房主的邀请码 → 生成自己的应答码。</summary>
    public async Task<bool> StartAsJoinerAsync(string? offerText, bool tryUpnp = true, string? manualExternalIp = null)
    {
        if (!CheckPlatform()) return false;
        if (!TryParsePeer(offerText, P2POfferKind.Offer, out var offer, out var error))
        {
            Fail(error);
            return false;
        }

        Reset();
        _identity = P2PIdentity.TryCreate();
        if (_identity is null)
        {
            Fail($"建不出这次连接要用的密钥：{P2PIdentity.UnsupportedReason}");
            return false;
        }

        _role = P2PRole.Joiner;
        _peerOffer = offer;
        PeerCode = offer!.Encode();

        if (offer.McPort <= 0)
            StatusText = "提示：对方没带上 MC 端口，连接可能失败 —— 让他先点「读取本机端口」再生成邀请码。";

        if (!await CollectCandidatesAsync(tryUpnp, manualExternalIp)) return false;

        _localOffer = BuildOffer(P2POfferKind.Answer, _candidates, CurrentVersionId(), null, 0);
        LocalCode = _localOffer.Encode();
        Stage = P2PStage.CodeReady;
        StatusText = $"已生成应答码（{_candidates.Count} 个候选地址）。把它发回给房主，" +
                     "然后点「开始连接」—— 两边要几乎同时点，太久房主那边的码就过期了。";
        if (_nat is { } nat) StatusText += Environment.NewLine + DescribeNat(nat);
        Raise();
        return true;
    }

    /// <summary>第 2 步：打洞 → 建立 QUIC 隧道 → 本机开代理端口。</summary>
    public async Task<bool> ConnectAsJoinerAsync(int preferredProxyPort = P2PTunnel.PreferredProxyPort)
    {
        if (_localOffer is null || _peerOffer is null) { Fail("先粘贴房主的邀请码"); return false; }

        var secret = DeriveSecret();
        if (secret is null) return false;

        Stage = P2PStage.Punching;
        Raise();

        var peer = await PunchAsync(CandidatesOf(_peerOffer), TimeSpan.FromSeconds(12));
        if (peer is null) return false;

        try
        {
            Stage = P2PStage.Connecting;
            StatusText = "打洞成功，正在建立加密隧道…";
            Raise();

            _tunnel = new P2PTunnel(secret);
            LocalProxyPort = await _tunnel.ConnectAsync(_localPort, peer, preferredProxyPort).ConfigureAwait(false);

            Stage = P2PStage.Ready;
            StatusText = LocalProxyPort == P2PTunnel.PreferredProxyPort
                ? "直连成功！请在 Minecraft 的「多人游戏 → 直接连接」里填 localhost"
                : $"直连成功！本机 {P2PTunnel.PreferredProxyPort} 端口被占用了，请在 Minecraft 里连接 localhost:{LocalProxyPort}";
            Raise();
            return true;
        }
        catch (Exception ex)
        {
            Fail($"连接失败：{ex.Message}");
            return false;
        }
    }

    /// <summary>断开隧道，回到初始状态（码也会作废，下次要重新生成）。</summary>
    public void Reset()
    {
        _puncher?.Dispose();
        _puncher = null;
        _tunnel?.Dispose();
        _tunnel = null;
        _identity?.Dispose();
        _identity = null;

        _localOffer = null;
        _peerOffer = null;
        _myNonce = Array.Empty<byte>();
        _role = P2PRole.None;
        _candidates = Array.Empty<P2PCandidate>();
        LocalCode = "";
        PeerCode = "";
        LocalProxyPort = 0;
        Failure = "";
        Stage = P2PStage.Idle;
        StatusText = "";
        Raise();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _puncher?.Dispose();
        _tunnel?.Dispose();
        _identity?.Dispose();
    }

    // ===== 内部 =====

    private bool CheckPlatform()
    {
        if (!P2PIdentity.IsSupported)
        {
            Fail($"本机的加密组件不支持所需的密钥交换（X25519）：{P2PIdentity.UnsupportedReason}");
            return false;
        }
        if (!P2PTunnel.IsSupported)
        {
            Fail(P2PTunnel.UnsupportedReason);
            return false;
        }
        return true;
    }

    private void Fail(string reason)
    {
        Failure = reason;
        Stage = P2PStage.Failed;
        StatusText = reason;
        Raise();
    }

    private void Raise() => Changed?.Invoke();

    /// <summary>收集本机候选地址 + 顺带摸清 NAT 行为。结果落在 <see cref="_candidates"/> 与 <see cref="_nat"/>。</summary>
    private async Task<bool> CollectCandidatesAsync(bool tryUpnp, string? manualExternalIp)
    {
        StatusText = "正在摸清你的网络（查公网地址、判断能不能被穿透，最多需要几秒）…";
        Stage = P2PStage.Punching;      // 借用忙状态，让用户看到按钮变灰
        Raise();

        var set = await P2PCandidateCollector
            .CollectAsync(_localPort, manualExternalIp, tryUpnp,
                          stunServers: StunServers.Count > 0 ? StunServers : null)
            .ConfigureAwait(false);

        _nat = set.Nat;
        _candidates = set.Candidates;

        if (_candidates.Count == 0)
        {
            Fail("一个本机地址都没取到。请连上网络，或在上面手填自己的公网 IP。");
            return false;
        }
        return true;
    }

    /// <summary>把 NAT 结论说成人话。写得长一点是有意的：这一步决定了接下来值不值得等。</summary>
    private static string DescribeNat(NatProbeResult nat)
    {
        if (!nat.Ok)
        {
            return nat.UdpLooksBlocked
                ? "注意：UDP 出不去（公开的 STUN 服务器全都没回应）。这种情况下基本连不通 —— 先检查防火墙 / 代理软件是否拦了 UDP。"
                : $"注意：没能判断出 NAT 类型（{nat.Error}）。可以照常试一次。";
        }

        var addr = nat.ServerReflexive is null ? "" : $"（公网地址 {nat.ServerReflexive}）";
        return nat.Mapping switch
        {
            NatMappingBehavior.EndpointIndependent =>
                $"你的网络是锥形 NAT {addr}，对方可以直接连到你，正常往下走。",
            NatMappingBehavior.AddressDependent =>
                $"你的路由器会按目的地址换端口 {addr}，直连成功率偏低 —— 先试一次，不行就退回局域网联动。",
            _ =>
                $"你的 NAT 是对称型 {addr}，几乎不可能直连。建议改用局域网联动，或先把两台机组进同一个虚拟局域网。"
        };
    }

    private P2POffer BuildOffer(string kind, IReadOnlyList<P2PCandidate> candidates,
                                string? mcVersion, string? worldName, int mcPort)
    {
        _myNonce = P2PIdentity.NewNonce();
        return new P2POffer
        {
            Kind = kind,
            ExpiresAt = DateTimeOffset.UtcNow.Add(P2POffer.Ttl).ToUnixTimeSeconds(),
            Nonce = _myNonce,
            PublicKey = _identity!.PublicKey,
            Candidates = candidates.Select(x => x.Uri).ToList(),
            DeviceName = Environment.MachineName,
            McVersion = mcVersion ?? "",
            WorldName = worldName ?? "",
            McPort = mcPort,
            Fingerprint = _identity.Fingerprint
        };
    }

    private bool TryParsePeer(string? text, string expectedKind, out P2POffer? offer, out string error)
    {
        offer = null;
        error = "";

        if (!P2POffer.TryDecode(text, out var parsed, out var why))
        {
            error = HumanError(why);
            return false;
        }

        if (!string.Equals(parsed!.Kind, expectedKind, StringComparison.Ordinal))
        {
            error = expectedKind == P2POfferKind.Answer
                ? "这里要填对方回给你的**应答码**（拿到邀请码生成的那一段），别把你自己刚才发的邀请码粘回来。"
                : "这里要填房主发来的**邀请码**，不是你自己生成的应答码。";
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        if (parsed.IsExpired(now))
        {
            error = "这个码已经过期了（超过 10 分钟没用）。让对方重新生成一个 —— 生成后要尽快互换。";
            return false;
        }

        offer = parsed;
        return true;
    }

    private byte[]? DeriveSecret()
    {
        try
        {
            if (_identity is null || _peerOffer is null) return null;
            return _identity.DeriveSessionSecret(_peerOffer.PublicKey, _myNonce, _peerOffer.Nonce);
        }
        catch (Exception ex)
        {
            Fail($"算不出会话密钥：{ex.Message}（对方可能用了不兼容的版本）");
            return null;
        }
    }

    private static List<IPEndPoint> CandidatesOf(P2POffer offer)
        => offer.Candidates.Select(P2PCandidate.TryParse).Where(x => x is not null).Select(x => x!).ToList();

    /// <summary>
    /// 朝对方的候选地址打洞，直到看到对方或超时。
    /// 看到对方之后还要再打一小会儿 —— 只通一半是不够的，对方也得能看到我们，
    /// 否则接下来 QUIC 握手的第一批包就会被他的 NAT 丢掉。
    /// </summary>
    private async Task<IPEndPoint?> PunchAsync(List<IPEndPoint> remote, TimeSpan budget)
    {
        if (remote.Count == 0)
        {
            Fail("对方的码里没有可用地址（可能被截断了）。让他重新生成一次。");
            return null;
        }

        _puncher?.Dispose();
        _puncher = new UdpPuncher();
        if (!_puncher.Start(_localPort, remote, _myNonce, _identity!.PublicKey))
        {
            Fail($"本机 UDP 端口 {_localPort} 被别的程序占用了。关掉它或重启启动器再试。");
            return null;
        }

        StatusText = $"正在打洞（对方 {remote.Count} 个地址）…";
        Raise();

        var sw = Stopwatch.StartNew();
        var reported = 0;
        while (sw.Elapsed < budget && !_puncher.SawPeer)
        {
            await Task.Delay(150).ConfigureAwait(false);
            var left = (int)(budget - sw.Elapsed).TotalSeconds;
            if (left == reported) continue;
            reported = left;
            StatusText = $"打洞中… 已发 {_puncher.SentPackets} 个包，还没收到对方（还剩 {left} 秒）";
            Raise();
        }

        if (!_puncher.SawPeer)
        {
            Fail(PunchFailedHint);
            StopPuncher();
            return null;
        }

        var peer = _puncher.PeerAddress!;
        StatusText = $"看到对方了（{peer}），再稳一会儿…";
        Raise();
        await Task.Delay(800).ConfigureAwait(false);

        StopPuncher();     // 必须放手端口，QUIC 才能用同一个源端口出发
        return peer;
    }

    private void StopPuncher()
    {
        try { _puncher?.Stop(); } catch { /* 打洞 socket 释放失败也不影响后续 */ }
        _puncher?.Dispose();
        _puncher = null;
    }

    private static string HumanError(string why) => why switch
    {
        "p2p_not_code" => "这不是直连握手码（应以 CHERT2: 开头）。局域网的邀请码是 CHERT1: 开头，两种别混用。",
        "p2p_checksum" or "p2p_fingerprint_mismatch" =>
            "这个码不完整或被改动过（校验没通过）。让对方重新生成，复制的时候注意别漏字符。",
        "p2p_checksum_missing" =>
            "这个码少了末尾那一段检错码 —— 多半是复制时只选中了一半。整段重新复制一次。",
        "p2p_bad_base64" or "p2p_bad_deflate" or "p2p_bad_payload" =>
            "看不出这是一个码 —— 多半是复制时被截断或混进了别的内容。请整段重新复制。",
        "p2p_version_mismatch" => "对方的启动器版本更旧/更新，码的格式对不上。两边都升级到同一版本再试。",
        "p2p_bad_nonce" or "p2p_bad_pubkey" => "这个码缺了必要的内容（公钥或随机数不完整）。请让对方重新生成。",
        "p2p_empty" => "还没有粘贴对方的码。",
        _ => $"解不开这个码（{why}）。"
    };

    private static string CurrentVersionId()
    {
        try { return ProfileStore.Load(GameConstants.DefaultGameRoot).LastVersionId ?? ""; }
        catch { return ""; }
    }
}
