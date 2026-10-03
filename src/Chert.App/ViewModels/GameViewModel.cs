using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Input;
using Chert.Core.Launcher;
using Chert.Core.Models;
using Chert.Core.Mods;
using Chert.Core.Mvvm;
using Chert.Core.Profiles;
using Chert.Core.Recommend;
using Chert.Core.Servers;
using Chert.Core.Statistics;
using Chert.Core.UI;
using Chert.Core.Utils;
using Chert.App.Services;
using Chert.App.Views;

namespace Chert.App.ViewModels;

/// <summary>
/// 游戏页视图模型（需求规格 2.1）。
/// 快速启动区绑定版本列表 / 用户名 / 内存；局域网与服务器列表接真实后端
/// （LanServerScanner / ServerListStore / ServerPinger）；智能推荐接 RecommendationEngine。
/// 加入按钮目前触发启动（直接连接地址的接线见 LaunchCliOverrides 注释，留待 v2.2 完善）。
/// </summary>
public class GameViewModel : ObservableObject
{
    private readonly string _gameRoot = GameConstants.DefaultGameRoot;
    private readonly LauncherProfile _profile = ProfileStore.Load(GameConstants.DefaultGameRoot);

    public VersionListViewModel Versions { get; } = new();

    /// <summary>已保存的账号列表（mclcs_accounts.json）。</summary>
    public ObservableCollection<AccountEntry> Accounts { get; private set; } = new();

    private AccountEntry? _selectedAccount;
    /// <summary>
    /// 当前选中的账号。随所选版本自动跟随该版本的绑定账号（对齐 Linux 的 SyncAccountForVersion）。
    /// 为 null 表示使用 <see cref="Username"/> 文本框里的离线昵称。
    /// </summary>
    public AccountEntry? SelectedAccount
    {
        get => _selectedAccount;
        set
        {
            if (!SetField(ref _selectedAccount, value)) return;
            OnPropertyChanged(nameof(HasAccount));
        }
    }

    /// <summary>是否选中了已保存账号（决定用户名框是否作为「离线昵称」提示）。</summary>
    public bool HasAccount => SelectedAccount is not null;

    private string _username;
    /// <summary>
    /// 用户名。选中账号时由账号名自动回填；<b>手动编辑则视为改用临时离线昵称</b>，
    /// 会清空 <see cref="SelectedAccount"/>（下拉与文本框二选一）。
    /// </summary>
    public string Username
    {
        get => _username;
        set
        {
            if (!SetField(ref _username, value)) return;
            if (SelectedAccount is not null && !string.Equals(SelectedAccount.Username, value, StringComparison.Ordinal))
                SelectedAccount = null;
        }
    }

    private int _memoryMb;
    public int MemoryMb
    {
        get => _memoryMb;
        set => SetField(ref _memoryMb, value);
    }

    /// <summary>局域网世界（"对局域网开放"广播）。</summary>
    public ObservableCollection<LanServer> LanServers { get; } = new();

    /// <summary>服务器列表（来自 servers.dat）。</summary>
    public ObservableCollection<ServerEntry> Servers { get; } = new();

    /// <summary>智能推荐 Top N。</summary>
    public ObservableCollection<RecommendationItem> Recommendations { get; } = new();

    private bool _lanEmpty = true;
    /// <summary>局域网列表是否为空（驱动空状态提示）。</summary>
    public bool LanEmpty { get => _lanEmpty; private set => SetField(ref _lanEmpty, value); }

    private bool _serversEmpty = true;
    /// <summary>服务器列表是否为空。</summary>
    public bool ServersEmpty { get => _serversEmpty; private set => SetField(ref _serversEmpty, value); }

    private bool _recommendEmpty = true;
    /// <summary>推荐列表是否为空。</summary>
    public bool RecommendEmpty { get => _recommendEmpty; private set => SetField(ref _recommendEmpty, value); }

    // ---- 统计数据 ----
    private string _weekTimeText = "—";
    private string _crashCountText = "—";

    public string WeekTimeText { get => _weekTimeText; set => SetField(ref _weekTimeText, value); }
    public string CrashCountText { get => _crashCountText; set => SetField(ref _crashCountText, value); }

    /// <summary>年度报告入口仅 12 月 31 日可见。</summary>
    public bool ShowAnnualReport =>
        DateTime.Now.Month == 12 && DateTime.Now.Day == 31;

    public ICommand LaunchCommand { get; }
    public ICommand ScanLanCommand { get; }
    public ICommand RefreshServersCommand { get; }
    public ICommand RefreshRecommendCommand { get; }
    public ICommand JoinLanCommand { get; }
    public ICommand JoinServerCommand { get; }
    public ICommand OpenAnnualReportCommand { get; }
    public ICommand InstallRecommendCommand { get; }
    public ICommand NotInterestedRecommendCommand { get; }
    public ICommand AddServerCommand { get; }
    public ICommand EditServerCommand { get; }
    public ICommand DeleteServerCommand { get; }
    public ICommand OpenAfkCommand { get; }
    public ICommand OpenVersionLibraryCommand { get; }

    public GameViewModel()
    {
        _username = _profile.DefaultUsername;
        _memoryMb = _profile.MaxMemoryMb > 0 ? _profile.MaxMemoryMb : DetectSystemMemoryMb();

        LaunchCommand = new AsyncRelayCommand(_ => LaunchAsync());
        ScanLanCommand = new AsyncRelayCommand(_ => ScanLanAsync());
        RefreshServersCommand = new AsyncRelayCommand(_ => RefreshServersAsync());
        RefreshRecommendCommand = new AsyncRelayCommand(_ => RefreshRecommendAsync());
        JoinLanCommand = new AsyncRelayCommand(p => JoinLanAsync(p as LanServer));
        JoinServerCommand = new AsyncRelayCommand(p => JoinServerAsync(p as ServerEntry));
        OpenAnnualReportCommand = new RelayCommand(_ => OpenAnnualReport());
        InstallRecommendCommand = new AsyncRelayCommand(p => InstallRecommendAsync(p as RecommendationItem));
        NotInterestedRecommendCommand = new RelayCommand(p => NotInterestedRecommend(p as RecommendationItem));
        AddServerCommand = new RelayCommand(_ => AddServer());
        EditServerCommand = new RelayCommand(p => EditServer(p as ServerEntry));
        DeleteServerCommand = new RelayCommand(p => DeleteServer(p as ServerEntry));
        OpenAfkCommand = new RelayCommand(_ => OpenAfk());
        OpenVersionLibraryCommand = new RelayCommand(_ => OpenVersionLibrary());

        Versions.Refresh();
        LoadAccounts();
        LoadServers();
        _ = RefreshStatsAsync();

        // 切换版本时账号下拉自动跟随该版本的绑定账号（每版本独立账号绑定）
        Versions.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(VersionListViewModel.SelectedVersion))
                SyncAccountForVersion();
        };

        // 在设置页增删账号后同步下拉（事件可能来自登录回调线程，统一切回 UI 线程）
        AccountStore.Changed += OnAccountsChanged;

        // 下载/安装新版本后，快速启动下拉实时刷新（LauncherService 在后台线程触发，统一切回 UI 线程）
        LauncherService.VersionInstalled += OnVersionInstalled;

        // 删除版本后同样要刷新，否则下拉里仍残留已删除的版本
        LauncherService.VersionListChanged += RefreshVersionsOnUi;

        LanServers.CollectionChanged += (_, _) => LanEmpty = LanServers.Count == 0;
        Servers.CollectionChanged += (_, _) => ServersEmpty = Servers.Count == 0;
        Recommendations.CollectionChanged += (_, _) => RecommendEmpty = Recommendations.Count == 0;

        // problem3：启动时自动刷新推荐。此前只在用户手点「刷新推荐」时才加载，
        // 首次进入游戏页推荐区恒为空（截图里「暂无推荐」）。这里后台预热 ——
        // RecommendationEngine 内部有 1 小时 TTL 缓存（HotRanking.CacheTtl），
        // 命中缓存时几乎零开销；未命中才真联网，失败会静默回退本地规则。
        // 用 fire-and-forget：不能阻塞构造（否则游戏页首屏卡住）。
        _ = WarmupRecommendationsAsync();
    }

    /// <summary>
    /// 启动时后台预热推荐列表。吞掉所有异常 —— 推荐是「锦上添花」，
    /// 绝不能因它拖慢或破坏启动。
    /// </summary>
    private async Task WarmupRecommendationsAsync()
    {
        try
        {
            await RefreshRecommendAsync();
        }
        catch
        {
            // 网络 / 解析失败已在 RefreshRecommendAsync 内兜底，这里再兜一层
        }
    }

    private string SelectedVersionId =>
        Versions.SelectedVersion?.Id ?? _profile.LastVersionId ?? "";

    /// <summary>重新载入账号列表（外部新增/删除账号后调用）。</summary>
    public void LoadAccounts()
    {
        Accounts = new ObservableCollection<AccountEntry>(AccountStore.Load(_gameRoot));
        SyncAccountForVersion();
    }

    private void OnAccountsChanged(string gameRoot)
    {
        if (!string.Equals(gameRoot, _gameRoot, StringComparison.OrdinalIgnoreCase)) return;

        var app = Application.Current;
        if (app is null) return;
        if (app.Dispatcher.CheckAccess()) LoadAccounts();
        else app.Dispatcher.BeginInvoke(LoadAccounts);
    }

    /// <summary>有新版本安装完成时刷新快速启动列表（来自后台线程，切回 UI 线程）。</summary>
    private void OnVersionInstalled(string id) => RefreshVersionsOnUi();

    /// <summary>在 UI 线程刷新已安装版本列表（安装 / 删除版本后统一走这里）。</summary>
    private void RefreshVersionsOnUi()
    {
        var app = Application.Current;
        if (app is null) return;
        if (app.Dispatcher.CheckAccess()) Versions.Refresh();
        else app.Dispatcher.BeginInvoke(Versions.Refresh);
    }

    /// <summary>
    /// 依据当前所选版本解析应使用的账号：优先该版本绑定的账号，否则回落全局「最后使用」。
    /// 实现「每版本独立账号绑定」——切换版本时账号下拉自动跟随（对齐 Linux GameHomeViewModel）。
    /// </summary>
    private void SyncAccountForVersion()
    {
        var id = Versions.SelectedVersion?.Id;
        var bound = !string.IsNullOrWhiteSpace(id)
            ? VersionProfileStore.Load(_gameRoot, id).BoundAccountId
            : null;
        var resolved = AccountStore.GetForVersion(_gameRoot, bound);
        // 确保 ComboBox 的 SelectedItem 与 ItemsSource 中是同一实例，否则下拉不会正确回显
        SelectedAccount = resolved is null ? null : Accounts.FirstOrDefault(a => a.Id == resolved.Id) ?? resolved;
        if (SelectedAccount is not null) Username = SelectedAccount.Username;
    }

    /// <summary>构建本次启动的账号覆盖参数：选中账号时传 Id，否则走离线昵称。</summary>
    private LaunchCliOverrides BuildOverrides(string? serverAddress = null) => new()
    {
        Username = SelectedAccount?.Username ?? Username,
        AccountId = SelectedAccount?.Id,
        MaxMemoryMb = MemoryMb,
        ServerAddress = serverAddress
    };

    private async Task LaunchAsync()
    {
        var id = SelectedVersionId;
        if (string.IsNullOrWhiteSpace(id)) return;
        // ★ 只启动、不等退出（problem3 多实例）：否则本命令的 _isRunning 会一直为 true
        //   直到游戏关闭，启动按钮全程灰着、无法再开第二个实例。
        await LauncherService.Instance.LaunchAndDetachAsync(id, null, BuildOverrides());
    }

    private async Task ScanLanAsync()
    {
        LanServers.Clear();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
        try
        {
            var found = await LanServerScanner.ScanAsync(onFound: s =>
            {
                if (!LanServers.Any(x => x.Endpoint == s.Endpoint))
                    LanServers.Add(s);
            }, ct: cts.Token);
            foreach (var s in found)
                if (!LanServers.Any(x => x.Endpoint == s.Endpoint))
                    LanServers.Add(s);
        }
        catch
        {
            // 监听失败（无权限 / 平台不支持）静默返回空列表
        }
    }

    private async Task RefreshStatsAsync()
    {
        try
        {
            var sessions = await Task.Run(() => SessionLog.Load(_gameRoot));
            var now = DateTime.Now;
            var weekAgo = now.AddDays(-7);
            var weekMin = sessions.Where(s => s.StartLocal >= weekAgo).Sum(s => s.Minutes);
            WeekTimeText = weekMin >= 60 ? $"{weekMin / 60:F0}h{weekMin % 60:F0}m" : $"{weekMin:F0}m";

            var thisYear = sessions.Where(s => s.StartLocal.Year == now.Year).ToList();
            CrashCountText = thisYear.Count(s => s.Crashed).ToString();
        }
        catch { /* 未记录过会话时保持 — */ }
    }

    /// <summary>智能内存：取系统总 RAM 的 80%，锁在 2048–16384 MB 之间。</summary>
    private static int DetectSystemMemoryMb()
    {
        try
        {
            var totalMb = (int)(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1024 / 1024);
            var recommended = (int)(totalMb * 0.8);
            return Math.Clamp(recommended, 2048, 16384);
        }
        catch { return 2048; }
    }

    private void LoadServers()
    {
        Servers.Clear();
        foreach (var s in ServerListStore.Load(_gameRoot))
            Servers.Add(s);
    }

    /// <summary>外部调用：窗口获得焦点时自动同步游戏内添加的服务器。</summary>
    public void RefreshServers()
    {
        LoadServers();
        ServersEmpty = Servers.Count == 0;
    }

    private async Task RefreshServersAsync()
    {
        LoadServers();
        try { await ServerPinger.PingAllAsync(Servers); }
        catch { /* 离线时保持 -1 */ }
    }

    private async Task RefreshRecommendAsync()
    {
        // ★ 该方法既可能被命令（UI 线程）调用，也可能被启动预热（后台线程）调用，
        //   集合写入必须统一切回 UI 线程，否则跨线程访问 ObservableCollection 会抛。
        var app = Application.Current;
        void ApplyAll(List<RecommendationItem>? items)
        {
            void Core()
            {
                Recommendations.Clear();
                if (items is null) return;
                foreach (var it in items.Take(8))
                    Recommendations.Add(it);
            }
            if (app is null || app.Dispatcher.CheckAccess()) Core();
            else app.Dispatcher.Invoke(Core);
        }

        ApplyAll(null);   // 立即清空，进入加载态
        try
        {
            using var client = new HttpClient();
            var items = await RecommendationEngine.BuildAsync(_gameRoot, _profile, client, null);
            ApplyAll(items);
        }
        catch
        {
            // 联网失败时用本地规则，引擎内部已处理；此处兜底为空
            ApplyAll(null);
        }
    }

    private async Task JoinLanAsync(LanServer? s)
    {
        if (s is null) return;
        // 同上：只启动不等退出，多开时按钮不被锁住
        await LauncherService.Instance.LaunchAndDetachAsync(_profile.LastVersionId ?? SelectedVersionId, null,
            BuildOverrides(s.Endpoint));
    }

    private async Task JoinServerAsync(ServerEntry? s)
    {
        if (s is null) return;
        // 同上：只启动不等退出
        await LauncherService.Instance.LaunchAndDetachAsync(_profile.LastVersionId ?? SelectedVersionId, null,
            BuildOverrides(s.Address));
    }

    /// <summary>打开年度报告独立窗口（规格 2.1 统计区入口）。</summary>
    private static void OpenAnnualReport()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            var win = new Window
            {
                Title = $"年度报告 · {DateTime.Now.Year}",
                Content = new AnnualReportView(),
                Width = 720,
                Height = 600,
                Owner = Application.Current.MainWindow,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            win.Show();
        });
    }

    private async Task InstallRecommendAsync(RecommendationItem? item)
    {
        if (item is null || string.IsNullOrEmpty(item.ProjectId)) return;
        try
        {
            var profile = ProfileStore.Load(_gameRoot);
            var loader = DetectRecommendLoader();
            var gameVersion = RuleEngine.ExtractGameVersion(profile.LastVersionId);
            var modsDir = Path.Combine(_gameRoot, "mods");
            var ok = await LauncherService.Instance.DownloadModAsync(item.ProjectId, modsDir, gameVersion, loader);
            if (ok)
            {
                Recommendations.Remove(item);
                ToastService.Show("智能推荐", $"已安装 {item.Title}", ToastKind.Success);
            }
            else
            {
                ToastService.Show("智能推荐", $"安装 {item.Title} 失败", ToastKind.Error);
            }
        }
        catch (Exception ex)
        {
            ToastService.Show("智能推荐", $"安装出错：{ex.Message}", ToastKind.Error);
        }
    }

    private void NotInterestedRecommend(RecommendationItem? item)
    {
        if (item is null) return;
        Recommendations.Remove(item);
        ToastService.Show("智能推荐", $"已隐藏 {item.Title}", ToastKind.Info);
    }

    private LoaderType DetectRecommendLoader()
    {
        var modsDir = System.IO.Path.Combine(_gameRoot, "mods");
        if (!Directory.Exists(modsDir)) return LoaderType.Any;
        try
        {
            foreach (var f in Directory.GetFiles(modsDir, "*.jar").Take(8))
            {
                var n = System.IO.Path.GetFileName(f).ToLowerInvariant();
                if (n.Contains("fabric")) return LoaderType.Fabric;
                if (n.Contains("neoforge")) return LoaderType.NeoForge;
                if (n.Contains("forge")) return LoaderType.Forge;
            }
        }
        catch { }
        return LoaderType.Any;
    }

    // ---- 服务器管理 ----

    /// <summary>
    /// 把外部传入的 ServerEntry 解析为 <see cref="Servers"/> 集合内的**真实实例**。
    /// ★ ServerEntry 未重写 Equals/GetHashCode，是纯引用相等；而菜单传入的对象来自
    ///   DataTemplate 的 DataContext，在 RefreshServers() 重新 LoadServers()（Clear + 重新
    ///   Load，产出全新实例）之后与集合里的实例不同 → ObservableCollection.Remove 找不到、
    ///   返回 false（删除无效），且「s != server」把自己也当成别人（编辑被误判重名）。
    /// 因此这里按业务键（地址优先、名称兜底）解析，绝不依赖引用相等。
    /// </summary>
    private ServerEntry? ResolveServer(ServerEntry? entry)
    {
        if (entry is null) return null;

        // ★★ 必须返回**集合内那个实例**，而不是传入的 entry。
        //   上一个版本写的是「if (Servers.Contains(entry)) return entry;」——
        //   Contains 因为值语义返回 true（说明集合里有等值对象），但返回的却是
        //   传入的旧实例。调用方随后用 ReferenceEquals 排除自己时，
        //   集合里没有任何元素与旧实例引用相同 → 自己被当成别人 →
        //   「已存在同名服务器」每次必报（用户连续 4 次反馈该症状）。
        //   正解：用索引反查集合内的真实元素。

        // 1) 引用相同（本来就在集合里）
        var byRef = Servers.FirstOrDefault(s => ReferenceEquals(s, entry));
        if (byRef is not null) return byRef;

        // 2) 值相同（DataTemplate 缓存的旧实例 / RefreshServers 重建后的等价对象）
        var byValue = Servers.FirstOrDefault(s => s.Equals(entry));
        if (byValue is not null) return byValue;

        // 3) 按地址（业务主键）匹配
        var addr = entry.Address?.Trim() ?? "";
        if (addr.Length > 0)
        {
            var byAddr = Servers.FirstOrDefault(s =>
                string.Equals(s.Address?.Trim(), addr, StringComparison.OrdinalIgnoreCase));
            if (byAddr is not null) return byAddr;
        }

        // 4) 按名称兜底
        var name = entry.Name?.Trim() ?? "";
        if (name.Length > 0)
            return Servers.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.Ordinal));

        return null;
    }

    private void AddServer()
    {
        var result = ShowServerDialog(null, null);
        if (result is null) return;

        // bug2.txt #82：避免添加同名服务器无提示
        if (Servers.Any(s => s.Name == result.Name))
        {
            ToastService.Show("服务器", $"已存在同名服务器「{result.Name}」", ToastKind.Warning);
            return;
        }

        Servers.Add(result);
        if (!ServerListStore.Save(Servers.ToList(), _gameRoot))
        {
            ToastService.Show("服务器", "已添加到列表，但写入 servers.dat 失败", ToastKind.Warning);
            return;
        }
        ServersEmpty = Servers.Count == 0;
        ToastService.Show("服务器", $"已添加 {result.Name}", ToastKind.Success);
    }

    private void EditServer(ServerEntry? server)
    {
        if (server is null) return;
        var result = ShowServerDialog(server.Name, server.Address);
        if (result is null) return;

        // ★ 解析成集合内的真实实例 —— 见 ResolveServer 注释。
        //   旧代码用「s != server」排除自己，但传入对象与集合内对象**不是同一实例**，
        //   条件恒为 true → 自己被当成重名 → 编辑被无端拦截。
        var target = ResolveServer(server);
        if (target is null)
        {
            ToastService.Show("服务器", "该服务器已不在列表中，请刷新后重试", ToastKind.Warning);
            return;
        }

        // bug2.txt #82：改名时若与**别的**服务器重名，提示并放弃保存
        if (Servers.Any(s => !ReferenceEquals(s, target) && s.Name == result.Name))
        {
            ToastService.Show("服务器", $"已存在同名服务器「{result.Name}」", ToastKind.Warning);
            return;
        }

        // 地址改了还要检查是否与别的服务器撞地址（地址是 servers.dat 的业务主键）
        if (Servers.Any(s => !ReferenceEquals(s, target) &&
            string.Equals(s.Address, result.Address, StringComparison.OrdinalIgnoreCase)))
        {
            ToastService.Show("服务器", $"已存在同地址服务器「{result.Address}」", ToastKind.Warning);
            return;
        }

        // ★★ 用户定案：改为「移除 > 重添加」。
        //   之前是就地改属性（target.Name = ...），即使 ServerEntry 已实现 INPC，
        //   卡片上绑定 Name / Address 的 TextBlock 在某些情况下仍不刷新，用户实测无效。
        //   走集合的 Remove + Insert 会强制 ItemsControl 丢弃旧容器、重建新容器并重新绑定，
        //   不依赖任何属性通知，是最可靠的刷新方式。
        var idx = Servers.IndexOf(target);
        if (idx < 0)
        {
            ToastService.Show("服务器", "定位失败：服务器不在列表中，请刷新后重试", ToastKind.Warning);
            return;
        }

        var newEntry = new ServerEntry
        {
            Name = result.Name,
            Address = result.Address,
            Icon = target.Icon,
            AcceptTextures = target.AcceptTextures
        };

        // 1) 先移除，2) 立刻按新值重添加 —— 两步都作用于集合，
        //    强制 ItemsControl 丢弃旧容器、重建新卡片并重新绑定（不依赖属性通知）。
        //    ★ 顺序很关键：必须「移除→添加」都做完再存盘。
        //    若在 RemoveAt 之后、Insert 之前 Save，磁盘上这条会**整条丢失**。
        Servers.RemoveAt(idx);
        Servers.Insert(idx, newEntry);

        // 3) 存盘（此时集合已是最终正确状态）
        if (!ServerListStore.Save(Servers.ToList(), _gameRoot))
        {
            // 存盘失败：回滚成原条目，别让界面和磁盘脱节
            Servers.RemoveAt(idx);
            Servers.Insert(idx, target);
            ToastService.Show("服务器", "保存失败：无法写入 servers.dat", ToastKind.Warning);
            return;
        }

        // 4) 终极兜底：保存成功后**从磁盘重载**整个列表。
        //    前面已经用 Remove+Insert 强制重建卡片，这里再让集合内容与 servers.dat 完全对齐 ——
        //    无论此前 UI 刷新链路有多少不确定性，重载后显示的一定是磁盘真实内容。
        //    顺带把已失效的 ping 状态（LatencyLevel / OnlinePlayers）复位，符合原设计。
        LoadServers();
        ServersEmpty = Servers.Count == 0;
        ToastService.Show("服务器", $"已更新 {newEntry.Name}", ToastKind.Success);
    }

    private void DeleteServer(ServerEntry? server)
    {
        if (server is null) return;
        if (!UIService.Confirm($"删除服务器「{server.Name}」？", "确认删除")) return;

        // ★ 解析成集合内的真实实例 —— 见 ResolveServer 注释。
        //   旧代码直接 Servers.Remove(server)，而传入对象与集合内对象不是同一实例，
        //   Remove 找不到就静默返回 false（实测：Toast 说「已删除」，卡片却还在）。
        var target = ResolveServer(server);
        if (target is null)
        {
            ToastService.Show("服务器", "该服务器已不在列表中，请刷新后重试", ToastKind.Warning);
            return;
        }

        var name = target.Name;
        Servers.Remove(target);
        if (!ServerListStore.Save(Servers.ToList(), _gameRoot))
        {
            ToastService.Show("服务器", "已从列表移除，但写入 servers.dat 失败", ToastKind.Warning);
            return;
        }
        ServersEmpty = Servers.Count == 0;
        ToastService.Show("服务器", $"已删除 {name}", ToastKind.Success);
    }

    /// <summary>bug #14：游戏页触发挂机工作流——打开独立窗口承载 AfkWorkflowView，运行器自动接管正在运行的 MC 实例。</summary>
    private static void OpenAfk()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            var win = new Window
            {
                Title = "挂机工作流",
                Content = new AfkWorkflowView(),
                Width = 880,
                Height = 600,
                Owner = Application.Current.MainWindow,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            win.Show();
        });
    }

    /// <summary>bug #10：游戏页快速启动触发版本库大页（版本列表 / 版本设置独立于四色索引贴）。</summary>
    private static void OpenVersionLibrary()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            var page = new VersionListView { OnBack = BigPageNavigator.Close };
            BigPageNavigator.Show(page);
        });
    }

    private static ServerEntry? ShowServerDialog(string? name, string? address)
    {
        ServerEntry? result = null;
        Application.Current.Dispatcher.Invoke(() =>
        {
            var view = new AddServerView(name, address);
            var win = new Window
            {
                Title = name is null ? "添加服务器" : "编辑服务器",
                Content = view,
                WindowStyle = WindowStyle.None,
                // ★ 恢复 AllowsTransparency + 透明背景：圆角裁剪**只能**在透明窗口里做。
                //   不透明窗口的窗体区域是矩形，ModalOverlayStyle 的半透明黑遮罩
                //   （ModalScrimBrush #80000000）铺满整个矩形 → 卡片圆角外就露出黑边
                //   （用户实机截图里左上角那截黑直角就是这么来的）。
                //   配合 AddServerView 里「遮罩只覆盖卡片区」的圆角容器即可消除。
                AllowsTransparency = true,
                Background = System.Windows.Media.Brushes.Transparent,
                SizeToContent = SizeToContent.WidthAndHeight,
                Owner = Application.Current.MainWindow,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false
            };
            win.ShowDialog();
            if (view.VM.Confirmed)
                result = new ServerEntry { Name = view.VM.Name, Address = view.VM.Address };
        });
        return result;
    }

    private static class ServerListStore
    {
        public static List<ServerEntry> Load(string gameRoot) =>
            Chert.Core.Servers.ServerListStore.Load(gameRoot);

        public static bool Save(List<ServerEntry> list, string gameRoot) =>
            Chert.Core.Servers.ServerListStore.Save(gameRoot, list);

        public static bool AddOrUpdate(List<ServerEntry> list, ServerEntry entry) =>
            Chert.Core.Servers.ServerListStore.AddOrUpdate(list, entry);
    }
}
