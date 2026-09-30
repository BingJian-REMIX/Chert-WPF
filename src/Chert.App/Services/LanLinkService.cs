using Chert.Core.Lan;
using Chert.Core.Utils;

namespace Chert.App.Services;

/// <summary>一条来自局域网的邀请（待用户在屏幕上确认）。</summary>
public sealed class LanInviteRequest
{
    /// <summary>发起方对等体。</summary>
    public LanPeer From { get; init; } = new();

    /// <summary>目标世界地址 host:port。</summary>
    public string Endpoint { get; init; } = "";

    /// <summary>世界展示名。</summary>
    public string WorldName { get; init; } = "";

    /// <summary>收到时间。</summary>
    public DateTime ReceivedAt { get; init; } = DateTime.Now;
}

/// <summary>
/// 清单 #35 ~ #40：局域网对等联动服务（App 层生命周期管理）。
/// <para>被控端与主控端是同一套代码：既应答别人的探测，也能主动探测别人。
/// 唯一的前提是两端都开着启动器并打开了「允许局域网联动」开关 ——
/// 启动器是绿色版，不做向远端部署启动器这件事。</para>
/// </summary>
public sealed class LanLinkService
{
    public static LanLinkService Instance { get; } = new();

    private readonly Dictionary<string, string> _tokens = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();
    private LanPeerBeacon? _beacon;
    private LanCommandServer? _server;
    private LanLinkConfig _config = LanLinkConfig.CreateDefault();
    private string _pendingPairCode = "";

    private LanLinkService() { }

    /// <summary>当前生效的配置。</summary>
    public LanLinkConfig Config => _config;

    /// <summary>服务是否在运行（应答 + 指令监听任一成功即视为运行）。</summary>
    public bool Running => _beacon is { Listening: true } || _server is { Listening: true };

    /// <summary>人类可读的运行状态（设置页展示）。</summary>
    public string StatusText { get; private set; } = "未启用";

    /// <summary>最近一次出错原因。</summary>
    public string LastError { get; private set; } = "";

    /// <summary>收到「邀请加入」时触发（后台线程，UI 需自行切线程）。</summary>
    public event Action<LanInviteRequest>? InviteReceived;

    /// <summary>状态变化（开关 / 启动失败）时触发。</summary>
    public event Action? StatusChanged;

    /// <summary>应用配置：开则启动、关则停止。配置相同则不做任何事。</summary>
    public void ApplyConfig(LanLinkConfig config)
    {
        var cfg = (config ?? LanLinkConfig.CreateDefault()).Normalize();
        var wasEnabled = _config.Enabled;
        _config = cfg;

        if (!cfg.Enabled)
        {
            Stop();
            SetStatus("未启用", "");
            return;
        }

        if (wasEnabled && Running) { SetStatus(Running ? BuildStatus() : "未启用", ""); return; }

        Start();
    }

    /// <summary>启动应答与指令服务。</summary>
    public bool Start()
    {
        Stop();

        _beacon = new LanPeerBeacon(SelfPeer, _config.DiscoveryPort);
        _server = new LanCommandServer(HandleCommand, _config.CommandPort);
        var serverOk = _server.Start();

        var errors = new List<string>();
        if (!_beacon.Listening) errors.Add($"发现端口 {_config.DiscoveryPort} 监听失败：{_beacon.ErrorMessage}");
        if (!serverOk) errors.Add($"指令端口 {_config.CommandPort} 监听失败：{_server.ErrorMessage}");

        LastError = string.Join("；", errors);
        if (!_beacon.Listening && !serverOk)
        {
            SetStatus("启动失败", LastError);
            return false;
        }

        SetStatus(BuildStatus(), LastError);
        return true;
    }

    /// <summary>停止全部监听并撤销会话。</summary>
    public void Stop()
    {
        lock (_gate)
        {
            _tokens.Clear();
            _pendingPairCode = "";
        }

        try { _server?.Dispose(); } catch { /* ignore */ }
        try { _beacon?.Dispose(); } catch { /* ignore */ }
        _server = null;
        _beacon = null;
    }

    private string BuildStatus()
    {
        var bits = new List<string>();
        if (_beacon is { Listening: true }) bits.Add($"发现 UDP/{_config.DiscoveryPort}");
        if (_server is { Listening: true }) bits.Add($"指令 TCP/{_config.CommandPort}");
        return bits.Count == 0 ? "未运行" : $"已启用（{string.Join(" · ", bits)}）";
    }

    private void SetStatus(string text, string error)
    {
        StatusText = text;
        LastError = error;
        try { StatusChanged?.Invoke(); } catch { /* ignore */ }
    }

    /// <summary>本机的对等体描述（应答探测与 Info 指令共用）。</summary>
    public LanPeer SelfPeer()
    {
        var name = string.IsNullOrWhiteSpace(_config.DeviceName)
            ? Environment.MachineName
            : _config.DeviceName;

        var running = GameProcessRegistry.Current is not null;

        return new LanPeer
        {
            DeviceName = name,
            Address = LanWorldShare.GetLocalIPv4(),
            Port = _config.CommandPort,
            LauncherVersion = GameConstants.LauncherVersion,
            ProtocolVersion = LanPeerProtocol.Version,
            GameRunning = running,
            LinkEnabled = _config.Enabled,
            LanEndpoint = DetectLocalLanEndpoint(),
            LastSeen = DateTime.Now
        };
    }

    private static string DetectLocalLanEndpoint()
    {
        var port = LanWorldShare.TryReadPublishedPort(GameConstants.DefaultGameRoot);
        return port is null ? "" : LanWorldShare.BuildEndpoint(LanWorldShare.GetLocalIPv4(), port.Value);
    }

    // ===== 主控端：探测 / 配对 / 发指令 =====

    /// <summary>广播探测局域网内的其它启动器。</summary>
    public Task<List<LanPeer>> ProbeAsync(int timeoutMs = 1200)
        => LanPeerDiscovery.ProbeAsync(timeoutMs, Environment.MachineName);

    /// <summary>定向探测指定 IP。</summary>
    public Task<LanPeer?> ProbeDirectAsync(string ip)
        => LanPeerDiscovery.ProbeDirectAsync(ip, _config.CommandPort);

    /// <summary>
    /// 生成并显示一个一次性配对码（10 分钟内有效）。
    /// 对方要在自己的启动器里输入这个码才能拿到会话 token。
    /// </summary>
    public string IssuePairCode()
    {
        var code = LanSessionStore.GeneratePairCode();
        lock (_gate)
        {
            _server?.Sessions.IssuePairCode(code);
            _pendingPairCode = code;
        }
        return code;
    }

    /// <summary>当前待使用的配对码（空表示未生成或已过期）。</summary>
    public string PendingPairCode
    {
        get { lock (_gate) { return _pendingPairCode; } }
    }

    /// <summary>用对方显示的配对码换取会话 token。</summary>
    public async Task<string?> PairAsync(LanPeer peer, string pairCode)
    {
        if (peer is null || string.IsNullOrWhiteSpace(pairCode)) return null;
        var token = await LanCommandClient.PairAsync(peer.BaseUrl, pairCode.Trim()).ConfigureAwait(false);
        if (string.IsNullOrEmpty(token)) return null;

        lock (_gate) { _tokens[peer.Endpoint] = token!; }
        return token;
    }

    /// <summary>取已缓存的会话 token；没有则返回空串。</summary>
    public string TokenFor(LanPeer peer)
    {
        lock (_gate) { return _tokens.TryGetValue(peer.Endpoint, out var t) ? t : ""; }
    }

    /// <summary>是否已与该对等体完成配对。</summary>
    public bool IsPaired(LanPeer peer) => TokenFor(peer).Length > 0;

    /// <summary>向对等体发指令（自动附带会话 token）。</summary>
    public async Task<LanCommandResult?> SendAsync(LanPeer peer, LanCommandKind kind, string endpoint = "", string worldName = "")
    {
        var cmd = new LanCommand
        {
            Kind = kind,
            Token = TokenFor(peer),
            From = Environment.MachineName,
            Endpoint = endpoint,
            WorldName = worldName
        };
        return await LanCommandClient.SendAsync(peer.BaseUrl, cmd).ConfigureAwait(false);
    }

    // ===== 被控端：处理别人发来的指令 =====

    private LanCommandResult HandleCommand(LanCommand cmd)
    {
        switch (cmd.Kind)
        {
            case LanCommandKind.Info:
                return new LanCommandResult
                {
                    Ok = true,
                    Status = "ok",
                    Message = "ok",
                    Peer = SelfPeer()
                };

            case LanCommandKind.OpenLan:
                return HandleOpenLan();

            case LanCommandKind.JoinLan:
                return HandleJoinLan(cmd);

            default:
                return LanCommandResult.Success("pong");
        }
    }

    private static LanCommandResult HandleOpenLan()
    {
        var proc = GameProcessRegistry.Current;
        if (proc is null)
            return LanCommandResult.Fail("busy", "本机当前没有运行中的 Minecraft");

        var hwnd = GameKeySender.FindGameWindow(proc, 3000);
        if (hwnd == IntPtr.Zero)
            return LanCommandResult.Fail("busy", "拿不到游戏窗口句柄");

        try
        {
            GameKeySender.SendChatCommand(hwnd, "/publish");
            return LanCommandResult.Success("已向游戏投递 /publish，请稍候 2 秒再刷新");
        }
        catch (Exception ex)
        {
            return LanCommandResult.Fail("error", ex.Message);
        }
    }

    private LanCommandResult HandleJoinLan(LanCommand cmd)
    {
        var parsed = LanWorldShare.ParseEndpoint(cmd.Endpoint);
        if (parsed is null)
            return LanCommandResult.Fail("error", $"无法解析世界地址：{cmd.Endpoint}");

        var request = new LanInviteRequest
        {
            From = new LanPeer { DeviceName = cmd.From, Address = "", Port = _config.CommandPort },
            Endpoint = $"{parsed.Value.Host}:{parsed.Value.Port}",
            WorldName = cmd.WorldName
        };

        try { InviteReceived?.Invoke(request); }
        catch { /* UI 处理失败不影响回包 */ }

        return LanCommandResult.Success(_config.AutoAcceptFromPaired ? "accepted" : "pending");
    }

    /// <summary>撤销所有已建立的会话（关闭开关或用户点「断开」时）。</summary>
    public void RevokeSessions()
    {
        lock (_gate)
        {
            _tokens.Clear();
            _server?.Sessions.RevokeAll();
        }
    }
}
