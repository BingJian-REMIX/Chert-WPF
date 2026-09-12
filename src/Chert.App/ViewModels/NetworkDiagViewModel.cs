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
        set => SetField(ref _isBusy, value);
    }

    /// <summary>页面顶部提示文案（bug #4：此前无任何说明）。</summary>
    public string Hint =>
        "检测启动器依赖的各官方 / 镜像服务端点连通性与延迟；若你添加了服务器，会在末尾附带各服务器的可达性探测。" +
        (ServerCount > 0 ? $"当前已加载 {ServerCount} 个服务器。" : "尚未添加任何服务器。");

    /// <summary>已加载的服务器数量（用于提示文案）。</summary>
    public int ServerCount { get; private set; }

    public ICommand DiagnoseCommand { get; }

    public NetworkDiagViewModel()
    {
        DiagnoseCommand = new AsyncRelayCommand(_ => DiagnoseAsync(), _ => !IsBusy);
        _ = DiagnoseAsync();
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
