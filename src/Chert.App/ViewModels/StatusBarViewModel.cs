using System.Collections.ObjectModel;
using System.Net.Http;
using System.Windows;
using System.Windows.Input;
using Chert.Core.Launcher;
using Chert.Core.Localization;
using Chert.Core.MultiInstance;
using Chert.Core.Mvvm;
using Chert.Core.Toolbox;
using Chert.Core.Utils;
using Chert.App.Services;

namespace Chert.App.ViewModels;

/// <summary>
/// 全局状态栏视图模型（单例）。各业务页在下载/网络/启动过程中向此处写入进度，
/// 主窗口状态栏只读绑定到这里。下载进度由 <see cref="StatusBarViewModel"/> 维护，
/// 网络状态按需探测。
/// </summary>
public class StatusBarViewModel : ObservableObject
{
    /// <summary>全局单例。</summary>
    public static StatusBarViewModel Current { get; } = new();

    private string _javaVersionText = "检测中…";
    private int _installedCount;
    private double _downloadProgress;      // 0-100
    private string _downloadText = "空闲";
    private string _networkStatusText = "未探测";
    private bool _isNetworkOk;
    private int _runningInstances;
    private string _lastLog = "";

    public string JavaVersionText
    {
        get => _javaVersionText;
        set => SetField(ref _javaVersionText, value);
    }

    public int InstalledCount
    {
        get => _installedCount;
        set => SetField(ref _installedCount, value);
    }

    /// <summary>下载进度（0-100）。</summary>
    public double DownloadProgress
    {
        get => _downloadProgress;
        set
        {
            if (!SetField(ref _downloadProgress, value)) return;
            // 进度在 (0,100) 之间表示有下载任务在进行中；0/100 视为空闲（含完成后归零）
            OnPropertyChanged(nameof(IsDownloading));
        }
    }

    /// <summary>是否有正在进行的下载任务（驱动标题栏下载按钮的环形进度显隐）。</summary>
    public bool IsDownloading => _downloadProgress is > 0 and < 100;

    public string DownloadText
    {
        get => _downloadText;
        set => SetField(ref _downloadText, value);
    }

    public string NetworkStatusText
    {
        get => _networkStatusText;
        set => SetField(ref _networkStatusText, value);
    }

    public bool IsNetworkOk
    {
        get => _isNetworkOk;
        set => SetField(ref _isNetworkOk, value);
    }

    public int RunningInstances
    {
        get => _runningInstances;
        set => SetField(ref _runningInstances, value);
    }

    public string LastLog
    {
        get => _lastLog;
        set => SetField(ref _lastLog, value);
    }

    public ICommand RefreshCommand { get; }

    // ---- 本地化属性（用于 StringFormat 替换） ----
    public string InstalledCountText => LocaleManager.Tf("status.installed", InstalledCount);
    public string RunningInstancesText => LocaleManager.Tf("status.running", RunningInstances);

    public StatusBarViewModel()
    {
        RefreshCommand = new AsyncRelayCommand(_ => RefreshAsync());
        LocaleManager.LocaleChanged += _ =>
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                OnPropertyChanged(nameof(InstalledCountText));
                OnPropertyChanged(nameof(RunningInstancesText));
            });
        };
        _ = RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        try
        {
            InstalledCount = LauncherService.Instance.ListInstalledVersions().Count;
            RunningInstances = InstanceTracker.ActiveCount();

            var java = await JavaDetector.FindBestAsync(GameConstants.MinimumJavaMajorVersion);
            JavaVersionText = java is not null
                ? $"Java {java.MajorVersion}"
                : LocaleManager.Tf("status.no_java", GameConstants.MinimumJavaMajorVersion);
        }
        catch
        {
            JavaVersionText = "检测失败";
        }

        _ = ProbeNetworkAsync();
    }

    private async Task ProbeNetworkAsync()
    {
        try
        {
            var results = await NetworkDiagnostics.DiagnoseAsync(null, new HttpClient());
            var ok = results.Count(r => r.Reachable);
            IsNetworkOk = ok > 0;
            NetworkStatusText = $"{ok}/{results.Count} 可达";
        }
        catch
        {
            IsNetworkOk = false;
            NetworkStatusText = "探测失败";
        }
    }
}
