using System.Collections.ObjectModel;
using System.Net.Http;
using System.Windows.Input;
using Chert.Core.Mvvm;
using Chert.Core.Servers;
using Chert.Core.Toolbox;
using Chert.Core.Utils;

namespace Chert.App.ViewModels;

/// <summary>网络诊断面板：检测各服务端点连通性与延迟。</summary>
public class NetworkDiagViewModel : ObservableObject
{
    private ObservableCollection<DiagnosticResult> _results = new();
    private bool _isBusy;

    public ObservableCollection<DiagnosticResult> Results
    {
        get => _results;
        set => SetField(ref _results, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (!SetField(ref _isBusy, value)) return;
            OnPropertyChanged(nameof(CanSpeedTest));
            // ★ ICommand 的 CanExecuteChanged 是**独立**于 PropertyChanged 的事件。
            //   只发 PropertyChanged 不会让 WPF 重新查询 CanExecute，
            //   按钮就会一直停在「禁用」外观（本次症状：测速按钮永久置灰）。
            SpeedTestCommand?.RaiseCanExecuteChanged();
        }
    }

    // ===== P05：本地带宽测速 =====

    private int _speedTestSeconds = 10;
    private bool _isSpeedTesting;
    private string _speedTestSummary = "尚未测速";
    private string _singleRateText = "—";
    private string _multiRateText = "—";
    private string _speedTestSource = "";

    /// <summary>可选测速时长（秒）。用户已定：5 / 10 / 15 / 30。</summary>
    public IReadOnlyList<int> SpeedTestSecondOptions { get; } = new[] { 5, 10, 15, 30 };

    /// <summary>测速时长（秒）。</summary>
    public int SpeedTestSeconds
    {
        get => _speedTestSeconds;
        set => SetField(ref _speedTestSeconds, value);
    }

    /// <summary>是否正在测速（测速期间禁用按钮）。</summary>
    public bool IsSpeedTesting
    {
        get => _isSpeedTesting;
        set
        {
            if (!SetField(ref _isSpeedTesting, value)) return;
            OnPropertyChanged(nameof(CanSpeedTest));
            SpeedTestCommand?.RaiseCanExecuteChanged();
        }
    }

    /// <summary>测速可否开始（未在诊断中、未在测速中）。</summary>
    public bool CanSpeedTest => !IsBusy && !IsSpeedTesting;

    /// <summary>单线程平均速率（贴近单文件下载体验）。</summary>
    public string SingleRateText
    {
        get => _singleRateText;
        set => SetField(ref _singleRateText, value);
    }

    /// <summary>多线程峰值带宽（贴近启动器实际下载行为）。</summary>
    public string MultiRateText
    {
        get => _multiRateText;
        set => SetField(ref _multiRateText, value);
    }

    /// <summary>测速源与耗时摘要。</summary>
    public string SpeedTestSummary
    {
        get => _speedTestSummary;
        set => SetField(ref _speedTestSummary, value);
    }

    /// <summary>当前测速使用的源名称。</summary>
    public string SpeedTestSource
    {
        get => _speedTestSource;
        set => SetField(ref _speedTestSource, value);
    }

    // 类型必须是 AsyncRelayCommand（而非 ICommand）——
    // IsBusy / IsSpeedTesting 变化时要调它的 RaiseCanExecuteChanged() 让按钮重新评估可用性。
    public AsyncRelayCommand SpeedTestCommand { get; }

    /// <summary>页面顶部提示文案（bug #4：此前无任何说明）。</summary>
    public string Hint =>
        "检测启动器依赖的各官方 / 镜像服务端点连通性与延迟；若你添加了服务器，会在末尾附带各服务器的可达性探测。" +
        (ServerCount > 0 ? $"当前已加载 {ServerCount} 个服务器。" : "尚未添加任何服务器。");

    /// <summary>已加载的服务器数量（用于提示文案）。</summary>
    public int ServerCount { get; private set; }

    public ICommand DiagnoseCommand { get; }

    public NetworkDiagViewModel()
    {
        // 先建命令再建 IsBusy：否则 DiagnoseAsync 里的 RaiseCanExecuteChanged
        // 会在 SpeedTestCommand 还是 null 时被调用（已用 ?. 兜住，但顺序更清晰）。
        DiagnoseCommand = new AsyncRelayCommand(_ => DiagnoseAsync(), _ => !IsBusy);
        SpeedTestCommand = new AsyncRelayCommand(_ => SpeedTestAsync(), _ => CanSpeedTest);
        _ = DiagnoseAsync();
    }

    /// <summary>
    /// P05：本地带宽测速。**单线程平均速率**与**多线程峰值带宽**两种模式都跑、分开标注。
    /// </summary>
    private async Task SpeedTestAsync()
    {
        IsSpeedTesting = true;
        SpeedTestSummary = $"正在测速（{SpeedTestSeconds} 秒）…";
        SingleRateText = "…";
        MultiRateText = "…";
        try
        {
            var r = await BandwidthTester.RunAsync(SpeedTestSeconds);
            // 源是动态解析出来的（随最新正式版变化），由结果带回
            SpeedTestSource = r.SourceName;
            SingleRateText = r.SingleText;
            MultiRateText = r.MultiText;
            var mb = r.BytesDownloaded / 1024.0 / 1024.0;
            SpeedTestSummary = r.Ok
                ? $"源：{SpeedTestSource}　实际下载 {mb:0.0} MB / 耗时 {r.ElapsedSeconds:0.#} 秒"
                : "测速失败：未能下载到数据，请检查网络连接或代理设置。";
        }
        catch (Exception ex)
        {
            SpeedTestSummary = "测速失败：" + ex.Message;
            SingleRateText = "—";
            MultiRateText = "—";
        }
        finally
        {
            IsSpeedTesting = false;
        }
    }

    private async Task DiagnoseAsync()
    {
        IsBusy = true;
        try
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            // 默认端点
            var list = await NetworkDiagnostics.DiagnoseAsync(null, client);

            // bug #4：把用户已添加的服务器也纳入探测
            var servers = ServerListStore.Load(GameConstants.DefaultGameRoot);
            ServerCount = servers.Count;
            if (servers.Count > 0)
            {
                var eps = servers.Select(s => (s.Name, s.Host)).ToList();
                var serverResults = await NetworkDiagnostics.DiagnoseAsync(eps, client);
                list.AddRange(serverResults);
            }

            Results = new ObservableCollection<DiagnosticResult>(list);
            OnPropertyChanged(nameof(Hint));
        }
        catch (Exception ex)
        {
            Results = new ObservableCollection<DiagnosticResult>
            {
                new() { Name = "诊断失败", Reachable = false, Error = ex.Message }
            };
        }
        finally
        {
            IsBusy = false;
        }
    }
}
