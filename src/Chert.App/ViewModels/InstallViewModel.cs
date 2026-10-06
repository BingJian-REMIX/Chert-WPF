using System.Collections.ObjectModel;
using System.Windows.Input;
using Chert.App.Services;
using Chert.Core.Models;
using Chert.Core.Mvvm;
using Chert.Core.Utils;

namespace Chert.App.ViewModels;

/// <summary>
/// 「安装」副页：手动安装指定 Minecraft 版本（原版 / Fabric / Forge / NeoForge / Quilt）。
/// <para>
/// 接入前这个页全项目没有任何入口（死代码），而且只有一行行追加的文字日志：
/// 没有进度条、不能取消，装完也不通知版本列表刷新 ——
/// 新装的版本在游戏页下拉里要重启启动器才看得到。
/// </para>
/// </summary>
public class InstallViewModel : ObservableObject
{
    private readonly AsyncRelayCommand _installCommand;
    private readonly AsyncRelayCommand _reloadCommand;
    private readonly RelayCommand _cancelCommand;

    private string _selectedInstallType = "Vanilla";
    private string _versionId = "";
    private string _log = "";
    private bool _isBusy;
    private double _progressValue;
    private bool _versionsLoaded;
    private CancellationTokenSource? _cts;

    public InstallViewModel()
    {
        _installCommand = new AsyncRelayCommand(_ => InstallAsync(), _ => !IsBusy);
        _cancelCommand = new RelayCommand(_ => Cancel(), _ => IsBusy);
        _reloadCommand = new AsyncRelayCommand(_ => LoadVersionsAsync(true), _ => !IsBusy);
    }

    /// <summary>可安装的类型（InstallVersionAsync 的 loader 取值）。</summary>
    public ObservableCollection<string> InstallTypes { get; } =
        new() { "Vanilla", "Fabric", "Forge", "NeoForge", "Quilt" };

    /// <summary>可安装版本清单（原版 release）。首次显示本页时拉取，拉不到也能手输。</summary>
    public ObservableCollection<string> AvailableVersions { get; } = new();

    public string SelectedInstallType
    {
        get => _selectedInstallType;
        set => SetField(ref _selectedInstallType, value);
    }

    public string VersionId
    {
        get => _versionId;
        set => SetField(ref _versionId, value);
    }

    public string Log
    {
        get => _log;
        set => SetField(ref _log, value);
    }

    /// <summary>安装进度（0-100）。此前根本没有进度可言。</summary>
    public double ProgressValue
    {
        get => _progressValue;
        set
        {
            if (!SetField(ref _progressValue, value)) return;
            OnPropertyChanged(nameof(ProgressText));
        }
    }

    public string ProgressText => IsBusy ? $"{_progressValue:0}%" : "";

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (!SetField(ref _isBusy, value)) return;
            OnPropertyChanged(nameof(ProgressText));
            // AsyncRelayCommand 有重入保护，不显式刷新可用性的话按钮会一直灰着
            _installCommand.RaiseCanExecuteChanged();
            _cancelCommand.RaiseCanExecuteChanged();
            _reloadCommand.RaiseCanExecuteChanged();
        }
    }

    public ICommand InstallCommand => _installCommand;
    public ICommand CancelCommand => _cancelCommand;
    public ICommand ReloadVersionsCommand => _reloadCommand;

    /// <summary>首次显示本页时拉取可安装版本（幂等，失败不影响手输）。</summary>
    public async Task EnsureVersionsLoadedAsync() => await LoadVersionsAsync(false);

    private async Task LoadVersionsAsync(bool force)
    {
        if (_versionsLoaded && !force) return;
        _versionsLoaded = true;
        try
        {
            var list = await LauncherService.Instance.GetVanillaVersionsDetailedAsync();
            var ids = list.Where(v => v.Type == "release").Select(v => v.Id).ToList();
            if (ids.Count == 0) ids = list.Select(v => v.Id).ToList();
            await RunOnUiAsync(() =>
            {
                AvailableVersions.Clear();
                foreach (var id in ids) AvailableVersions.Add(id);
                if (string.IsNullOrWhiteSpace(VersionId) && AvailableVersions.Count > 0)
                    VersionId = AvailableVersions[0];
            });
        }
        catch (Exception ex)
        {
            AppendLog($"获取版本清单失败（可手动输入版本号）：{ex.Message}");
        }
    }

    private async Task InstallAsync()
    {
        var version = (VersionId ?? "").Trim();
        if (version.Length == 0)
        {
            AppendLog("请先选择或输入要安装的版本（例如 1.20.1）。");
            return;
        }

        IsBusy = true;
        ProgressValue = 0;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        AppendLog($"开始安装 {SelectedInstallType} {version} …");
        if (!Elevation.IsAdministrator())
            AppendLog("提示：当前非管理员权限；若安装（尤其是 Forge）写入系统目录失败，请以管理员身份重新运行启动器。");

        try
        {
            var progress = new Progress<double>(p =>
                ProgressValue = Math.Clamp(p * 100d, 0, 100));

            // 走 InstallVersionAsync 而不是 InstallAsync：前者装完会触发
            // LauncherService.VersionInstalled，游戏页版本列表立即刷新。
            var id = await LauncherService.Instance.InstallVersionAsync(
                version, LoaderKeyOf(SelectedInstallType), progress, ct);

            if (string.IsNullOrEmpty(id))
            {
                AppendLog("安装已结束，但未返回版本 Id —— 请到游戏页版本列表确认。");
            }
            else
            {
                AppendLog($"安装完成：{id}（版本列表已刷新，可在游戏页直接选择）");
                ToastService.Show("安装完成", $"{SelectedInstallType} {id}", ToastKind.Success);
            }
        }
        catch (OperationCanceledException)
        {
            AppendLog("已取消安装。");
            ToastService.Show("安装已取消", $"{SelectedInstallType} {version}", ToastKind.Info);
        }
        catch (Exception ex)
        {
            AppendLog($"安装失败：{ex.Message}");
            ToastService.Show("安装失败", ex.Message, ToastKind.Error);
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            IsBusy = false;
        }
    }

    private void Cancel()
    {
        if (_cts is null)
        {
            AppendLog("当前没有正在进行的安装。");
            return;
        }
        AppendLog("正在取消…（已下载的部分会保留，下次继续）");
        _cts.Cancel();
    }

    private void AppendLog(string line)
    {
        if (Log.Length > 8000) Log = Log[^4000..];
        Log += line + "\n";
    }

    private static string LoaderKeyOf(string? type) => (type ?? "").Trim().ToLowerInvariant() switch
    {
        "fabric" => "fabric",
        "forge" => "forge",
        "neoforge" => "neoforge",
        "quilt" => "quilt",
        _ => "none"
    };

    /// <summary>回到 UI 线程更新集合（Core 的 HTTP 调用可能 ConfigureAwait(false) 后落在后台线程）。</summary>
    private static async Task RunOnUiAsync(Action action)
    {
        var d = System.Windows.Application.Current?.Dispatcher;
        if (d is null || d.CheckAccess())
        {
            action();
            return;
        }
        await d.InvokeAsync(action);
    }
}
