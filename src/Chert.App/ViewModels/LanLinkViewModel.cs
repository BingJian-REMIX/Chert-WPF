using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using Chert.App.Services;
using Chert.Core.Lan;
using Chert.Core.Localization;
using Chert.Core.Mvvm;
using Chert.Core.Profiles;
using Chert.Core.Utils;

namespace Chert.App.ViewModels;

/// <summary>局域网列表里一台机器的展示卡片。</summary>
public sealed class LanPeerCard
{
    public LanPeer Peer { get; init; } = new();

    public string DisplayName => Peer.DisplayName;

    public string Detail => Peer.Endpoint;

    /// <summary>状态摘要：是否在游戏中 / 是否已开放局域网世界。</summary>
    public string StateText =>
        Peer.GameRunning
            ? (Peer.LanEndpoint.Length > 0 ? $"游戏中 · 已开放 {Peer.LanEndpoint}" : "游戏中")
            : "未启动游戏";

    public bool Paired { get; set; }

    public string PairText => Paired ? "已配对" : "未配对";

    public string VersionText => Peer.LauncherVersion;
}

/// <summary>
/// 清单 #35 ~ #40：局域网联动页（情人节专项）。
/// <para>对等模式：两端都跑着燧石启动器、都开启联动开关，谁都可以邀请谁。
/// 不做向远端部署启动器这件事 —— 启动器是绿色版，对方装一次即可。</para>
/// </summary>
public class LanLinkViewModel : ObservableObject
{
    private readonly LanLinkService _service = LanLinkService.Instance;

    private bool _enabled;
    private bool _busy;
    private string _statusText = "";
    private string _serviceStatus = "";
    private string _pairCode = "";
    private string _inputPairCode = "";
    private string _manualIp = "";
    private string _localEndpoint = "";
    private string _joinEndpoint = "";
    private LanPeerCard? _selected;

    public LanLinkViewModel()
    {
        Peers = new ObservableCollection<LanPeerCard>();

        RefreshCommand = new AsyncRelayCommand(_ => RefreshAsync());
        ProbeIpCommand = new AsyncRelayCommand(_ => ProbeIpAsync());
        GeneratePairCodeCommand = new RelayCommand(_ => GeneratePairCode());
        PairCommand = new AsyncRelayCommand(_ => PairAsync());
        PublishCommand = new RelayCommand(_ => PublishLocal());
        DetectCommand = new RelayCommand(_ => DetectLocal());
        InviteCommand = new AsyncRelayCommand(_ => InviteAsync());
        RequestOpenCommand = new AsyncRelayCommand(_ => RequestOpenAsync());
        JoinCommand = new AsyncRelayCommand(_ => JoinAsync());
        CopyShareCommand = new RelayCommand(_ => CopyShare());

        _service.StatusChanged += () => Application.Current?.Dispatcher.Invoke(() =>
        {
            ServiceStatus = _service.StatusText;
            OnPropertyChanged(nameof(ServiceStatus));
        });
        _service.InviteReceived += OnInviteReceived;

        LoadFromProfile();
        ServiceStatus = _service.StatusText;
    }

    // ===== 属性 =====

    public ObservableCollection<LanPeerCard> Peers { get; }

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (!SetField(ref _enabled, value)) return;
            var cfg = _service.Config;
            cfg.Enabled = value;
            cfg.Normalize();
            _service.ApplyConfig(cfg);
            Persist();
            ServiceStatus = _service.StatusText;
            StatusText = value
                ? (_service.Running ? "已开启：可被局域网内的燧石发现" : $"开启失败：{_service.LastError}")
                : "已关闭";
        }
    }

    public bool Busy
    {
        get => _busy;
        set => SetField(ref _busy, value);
    }

    public string ServiceStatus
    {
        get => _serviceStatus;
        set => SetField(ref _serviceStatus, value);
    }

    public string StatusText
    {
        get => _statusText;
        set => SetField(ref _statusText, value);
    }

    public string PairCode
    {
        get => _pairCode;
        set => SetField(ref _pairCode, value);
    }

    public string InputPairCode
    {
        get => _inputPairCode;
        set => SetField(ref _inputPairCode, value);
    }

    public string ManualIp
    {
        get => _manualIp;
        set => SetField(ref _manualIp, value);
    }

    public string LocalEndpoint
    {
        get => _localEndpoint;
        set
        {
            if (!SetField(ref _localEndpoint, value)) return;
            OnPropertyChanged(nameof(ShareText));
        }
    }

    public string JoinEndpoint
    {
        get => _joinEndpoint;
        set => SetField(ref _joinEndpoint, value);
    }

    public LanPeerCard? Selected
    {
        get => _selected;
        set
        {
            if (!SetField(ref _selected, value)) return;
            OnPropertyChanged(nameof(HasSelection));
        }
    }

    public bool HasSelection => _selected is not null;

    /// <summary>可复制给对方的邀请文本。</summary>
    public string ShareText =>
        LocalEndpoint.Length == 0
            ? ""
            : LanWorldShare.BuildShareText(LocalEndpoint);

    // ===== 命令 =====

    public ICommand RefreshCommand { get; }
    public ICommand ProbeIpCommand { get; }
    public ICommand GeneratePairCodeCommand { get; }
    public ICommand PairCommand { get; }
    public ICommand PublishCommand { get; }
    public ICommand DetectCommand { get; }
    public ICommand InviteCommand { get; }
    public ICommand RequestOpenCommand { get; }
    public ICommand JoinCommand { get; }
    public ICommand CopyShareCommand { get; }

    // ===== 实现 =====

    private void LoadFromProfile()
    {
        try
        {
            var profile = ProfileStore.Load(GameConstants.DefaultGameRoot);
            var cfg = (profile.LanLink ?? LanLinkConfig.CreateDefault()).Normalize();
            _enabled = cfg.Enabled;
            OnPropertyChanged(nameof(Enabled));
        }
        catch
        {
            _enabled = false;
        }
    }

    private void Persist()
    {
        try
        {
            var profile = ProfileStore.Load(GameConstants.DefaultGameRoot);
            profile.LanLink = _service.Config;
            profile.GameRoot = string.IsNullOrWhiteSpace(profile.GameRoot)
                ? GameConstants.DefaultGameRoot
                : profile.GameRoot;
            ProfileStore.Save(profile);
        }
        catch
        {
            // 持久化失败不影响当前会话
        }
    }

    private async Task RefreshAsync()
    {
        if (!_enabled)
        {
            StatusText = "请先开启「允许局域网联动」";
            return;
        }

        Busy = true;
        StatusText = "正在搜索局域网内的燧石启动器…";
        try
        {
            var found = await _service.ProbeAsync(1400);
            Application.Current?.Dispatcher.Invoke(() =>
            {
                Peers.Clear();
                foreach (var p in found)
                    Peers.Add(new LanPeerCard { Peer = p, Paired = _service.IsPaired(p) });
            });
            StatusText = found.Count == 0
                ? "没搜到。确认对方也开着燧石并开启联动；也可以直接填对方 IP 探测。"
                : $"发现 {found.Count} 台设备";
        }
        catch (Exception ex)
        {
            StatusText = $"搜索失败：{ex.Message}";
        }
        finally
        {
            Busy = false;
        }
    }

    private async Task ProbeIpAsync()
    {
        var ip = (ManualIp ?? "").Trim();
        if (ip.Length == 0) { StatusText = "请先填写对方 IP"; return; }

        Busy = true;
        StatusText = $"正在探测 {ip} …";
        try
        {
            var peer = await _service.ProbeDirectAsync(ip);
            if (peer is null)
            {
                StatusText = $"{ip} 没有响应。确认对方已开启联动，且防火墙放行了发现端口。";
                return;
            }

            Application.Current?.Dispatcher.Invoke(() =>
            {
                if (Peers.All(x => x.Peer.Endpoint != peer.Endpoint))
                    Peers.Add(new LanPeerCard { Peer = peer, Paired = _service.IsPaired(peer) });
                Selected = Peers.FirstOrDefault(x => x.Peer.Endpoint == peer.Endpoint);
            });
            StatusText = $"已连接到 {peer.DisplayName}";
        }
        catch (Exception ex)
        {
            StatusText = $"探测失败：{ex.Message}";
        }
        finally
        {
            Busy = false;
        }
    }

    private void GeneratePairCode()
    {
        if (!_enabled) { StatusText = "请先开启联动"; return; }
        PairCode = _service.IssuePairCode();
        StatusText = $"配对码 {PairCode}（10 分钟内有效）—— 让对方在自己的启动器里输入它";
    }

    private async Task PairAsync()
    {
        if (Selected is null) { StatusText = "先在列表里选一台设备"; return; }
        var code = (InputPairCode ?? "").Trim();
        if (code.Length == 0) { StatusText = "请输入对方显示的 6 位配对码"; return; }

        Busy = true;
        StatusText = "正在配对…";
        try
        {
            var token = await _service.PairAsync(Selected.Peer, code);
            if (token is null)
            {
                StatusText = "配对失败：配对码错误或已过期";
                return;
            }

            Selected.Paired = true;
            OnPropertyChanged(nameof(Selected));
            StatusText = $"已与 {Selected.DisplayName} 配对";
        }
        catch (Exception ex)
        {
            StatusText = $"配对失败：{ex.Message}";
        }
        finally
        {
            Busy = false;
        }
    }

    /// <summary>向本机游戏投递 /publish。</summary>
    private void PublishLocal()
    {
        var proc = GameProcessRegistry.Current;
        if (proc is null) { StatusText = "本机没有运行中的 Minecraft"; return; }

        var hwnd = GameKeySender.FindGameWindow(proc, 3000);
        if (hwnd == IntPtr.Zero) { StatusText = "拿不到游戏窗口句柄"; return; }

        GameKeySender.SendChatCommand(hwnd, "/publish");
        StatusText = "已投递 /publish，2 秒后点「读取本机端口」";
    }

    /// <summary>从日志尾部读取本机已开放的局域网端口。</summary>
    private void DetectLocal()
    {
        var port = LanWorldShare.TryReadPublishedPort(GameConstants.DefaultGameRoot);
        if (port is null)
        {
            StatusText = "没读到端口。确认游戏里已经「对局域网开放」，或先点「一键开放」。";
            return;
        }

        LocalEndpoint = LanWorldShare.BuildEndpoint(LanWorldShare.GetLocalIPv4(), port.Value);
        StatusText = $"本机世界地址 {LocalEndpoint}";
    }

    private async Task InviteAsync()
    {
        if (Selected is null) { StatusText = "先选一台设备"; return; }
        if (LocalEndpoint.Length == 0) { StatusText = "先读取本机世界地址"; return; }

        Busy = true;
        StatusText = $"正在邀请 {Selected.DisplayName} …";
        try
        {
            var res = await _service.SendAsync(Selected.Peer, LanCommandKind.JoinLan,
                LocalEndpoint, Environment.MachineName);
            StatusText = res is null
                ? "对方没有响应"
                : res.Ok ? $"已发送邀请：{res.Message}" : $"对方拒绝了：{res.Message}";
        }
        catch (Exception ex)
        {
            StatusText = $"发送失败：{ex.Message}";
        }
        finally
        {
            Busy = false;
        }
    }

    private async Task RequestOpenAsync()
    {
        if (Selected is null) { StatusText = "先选一台设备"; return; }

        Busy = true;
        StatusText = $"正在请求 {Selected.DisplayName} 开放局域网世界…";
        try
        {
            var res = await _service.SendAsync(Selected.Peer, LanCommandKind.OpenLan);
            StatusText = res is null
                ? "对方没有响应"
                : res.Ok ? res.Message : $"失败：{res.Message}";
        }
        catch (Exception ex)
        {
            StatusText = $"发送失败：{ex.Message}";
        }
        finally
        {
            Busy = false;
        }
    }

    /// <summary>加入一个局域网世界（地址来自粘贴或收到的邀请）。</summary>
    private async Task JoinAsync()
    {
        var text = (JoinEndpoint ?? "").Trim();
        if (text.Length == 0) { StatusText = "请先填写世界地址"; return; }

        if (LanWorldShare.ParseEndpoint(text) is not { } parsed)
        {
            StatusText = $"地址格式不对：{text}（应为 host:port）";
            return;
        }

        Busy = true;
        StatusText = $"正在加入 {parsed.Host}:{parsed.Port} …";
        try
        {
            var profile = ProfileStore.Load(GameConstants.DefaultGameRoot);
            var versionId = profile.LastVersionId;
            if (string.IsNullOrWhiteSpace(versionId))
            {
                StatusText = "还没有选择要启动的版本";
                return;
            }

            // 只启动、不等退出（problem3 多实例）：否则按钮会被锁到游戏关闭
            await LauncherService.Instance.LaunchAndDetachAsync(versionId, null, new LaunchCliOverrides
            {
                Username = profile.DefaultUsername,
                ServerAddress = $"{parsed.Host}:{parsed.Port}"
            });
            StatusText = $"已启动并直连 {parsed.Host}:{parsed.Port}";
        }
        catch (Exception ex)
        {
            StatusText = $"加入失败：{ex.Message}";
        }
        finally
        {
            Busy = false;
        }
    }

    private void CopyShare()
    {
        if (ShareText.Length == 0) { StatusText = "还没有可分享的地址"; return; }
        try
        {
            System.Windows.Clipboard.SetText(ShareText);
            StatusText = "邀请文本已复制到剪贴板";
        }
        catch
        {
            StatusText = "复制失败，请手动选中文本复制";
        }
    }

    /// <summary>收到别人的邀请：在屏幕上确认后才执行（局域网不可信，绝不静默加入）。</summary>
    private void OnInviteReceived(LanInviteRequest request)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            var text = $"「{request.From.DisplayName}」邀请你加入局域网世界\n" +
                       $"地址：{request.Endpoint}\n" +
                       (request.WorldName.Length > 0 ? $"世界：{request.WorldName}\n" : "") +
                       "\n是否加入？";

            var yes = System.Windows.MessageBox.Show(
                text,
                LocaleManager.T("lan.invite_title"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (yes != MessageBoxResult.Yes)
            {
                StatusText = "已拒绝邀请";
                return;
            }

            JoinEndpoint = request.Endpoint;
            OnPropertyChanged(nameof(JoinEndpoint));
            _ = JoinAsync();
        });
    }
}
