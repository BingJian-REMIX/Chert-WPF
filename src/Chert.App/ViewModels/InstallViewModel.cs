using System.Collections.ObjectModel;
using System.Windows.Input;
using Chert.Core.Mvvm;
using Chert.Core.Utils;
using Chert.App.Services;

namespace Chert.App.ViewModels;

public class InstallViewModel : ObservableObject
{
    private string _selectedInstallType = "Vanilla";
    private string _versionId = "";
    private string _log = "";
    private bool _isBusy;

    public ObservableCollection<string> InstallTypes { get; } = new() { "Vanilla", "Fabric", "Forge" };

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

    public bool IsBusy
    {
        get => _isBusy;
        set => SetField(ref _isBusy, value);
    }

    public ICommand InstallCommand { get; }

    public InstallViewModel()
    {
        InstallCommand = new AsyncRelayCommand(_ => InstallAsync(), _ => !IsBusy);
    }

    private async Task InstallAsync()
    {
        if (string.IsNullOrWhiteSpace(VersionId))
        {
            Log = "请输入要安装的版本号（例如 1.20.1）";
            return;
        }

        IsBusy = true;
        try
        {
            var progress = new Progress<(int Done, int Total)>(p =>
                Log += $"[{p.Done}/{p.Total}] {SelectedInstallType} {VersionId}\n");
            Log = $"开始安装 {SelectedInstallType} {VersionId} …\n";

            if (!Elevation.IsAdministrator())
                Log += "提示：当前非管理员权限；若安装（尤其是 Forge）写入系统目录失败，请以管理员身份重新运行启动器。\n";
            await LauncherService.Instance.InstallAsync(SelectedInstallType, VersionId, progress);
            Log += "安装完成。\n";
        }
        catch (Exception ex)
        {
            Log += $"安装出错：{ex.Message}\n";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
