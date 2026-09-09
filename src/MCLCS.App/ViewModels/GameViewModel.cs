using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Input;
using MCLCS.Core.Launcher;
using MCLCS.Core.Models;
using MCLCS.Core.Mods;
using MCLCS.Core.Mvvm;
using MCLCS.Core.Profiles;
using MCLCS.Core.Recommend;
using MCLCS.Core.Servers;
using MCLCS.Core.Statistics;
using MCLCS.Core.UI;
using MCLCS.Core.Utils;
using MCLCS.App.Services;
using MCLCS.App.Views;

namespace MCLCS.App.ViewModels;

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
        await LauncherService.Instance.LaunchAsync(id, null, BuildOverrides());
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
        Recommendations.Clear();
        try
        {
            using var client = new HttpClient();
            var items = await RecommendationEngine.BuildAsync(_gameRoot, _profile, client, null);
            foreach (var it in items.Take(8))
                Recommendations.Add(it);
        }
        catch
        {
            // 联网失败时用本地规则，引擎内部已处理；此处兜底为空
        }
    }

    private async Task JoinLanAsync(LanServer? s)
    {
        if (s is null) return;
        await LauncherService.Instance.LaunchAsync(_profile.LastVersionId ?? SelectedVersionId, null,
            BuildOverrides(s.Endpoint));
    }

    private async Task JoinServerAsync(ServerEntry? s)
    {
        if (s is null) return;
        await LauncherService.Instance.LaunchAsync(_profile.LastVersionId ?? SelectedVersionId, null,
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
        ServerListStore.Save(Servers.ToList(), _gameRoot);
        ToastService.Show("服务器", $"已添加 {result.Name}", ToastKind.Success);
    }

    private void EditServer(ServerEntry? server)
    {
        if (server is null) return;
        var result = ShowServerDialog(server.Name, server.Address);
        if (result is null) return;

        // bug2.txt #82：改名时若与别的服务器重名，提示并放弃保存
        if (Servers.Any(s => s != server && s.Name == result.Name))
        {
            ToastService.Show("服务器", $"已存在同名服务器「{result.Name}」", ToastKind.Warning);
            return;
        }

        server.Name = result.Name;
        server.Address = result.Address;
        ServerListStore.Save(Servers.ToList(), _gameRoot);
        ServersEmpty = Servers.Count == 0;
        ToastService.Show("服务器", $"已更新 {server.Name}", ToastKind.Success);
    }

    private void DeleteServer(ServerEntry? server)
    {
        if (server is null) return;
        if (!UIService.Confirm($"删除服务器「{server.Name}」？", "确认删除")) return;

        Servers.Remove(server);
        ServerListStore.Save(Servers.ToList(), _gameRoot);
        ServersEmpty = Servers.Count == 0;
        ToastService.Show("服务器", $"已删除 {server.Name}", ToastKind.Info);
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
                AllowsTransparency = true,
                Background = null,
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
            MCLCS.Core.Servers.ServerListStore.Load(gameRoot);

        public static bool Save(List<ServerEntry> list, string gameRoot) =>
            MCLCS.Core.Servers.ServerListStore.Save(gameRoot, list);

        public static bool AddOrUpdate(List<ServerEntry> list, ServerEntry entry) =>
            MCLCS.Core.Servers.ServerListStore.AddOrUpdate(list, entry);
    }
}
