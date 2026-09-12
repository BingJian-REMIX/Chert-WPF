using System.Windows.Input;
using Chert.Core.Download;
using Chert.Core.Mvvm;
using Chert.Core.Utils;

namespace Chert.App.ViewModels;

/// <summary>
/// 工具箱 → 地图（对齐 MCLCS-Linux MapView）：安装本地地图 zip 到 saves/，自动检测根目录前缀。
/// </summary>
public class MapViewModel : ObservableObject
{
    private readonly string _gameRoot = GameConstants.DefaultGameRoot;

    private string _zipPath = "";
    public string ZipPath
    {
        get => _zipPath;
        set => SetField(ref _zipPath, value);
    }

    private string _status = "就绪";
    public string Status
    {
        get => _status;
        set => SetField(ref _status, value);
    }

    public ICommand InstallCommand => new RelayCommand(_ => Install());

    /// <summary>选择本地地图 zip 文件。</summary>
    public ICommand SelectFileCommand => new RelayCommand(_ => SelectFile());

    private void SelectFile()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择地图 zip",
            Filter = "地图压缩包 (*.zip)|*.zip|所有文件 (*.*)|*.*"
        };
        if (dlg.ShowDialog() == true) ZipPath = dlg.FileName;
    }

    private void Install()
    {
        if (string.IsNullOrWhiteSpace(ZipPath) || !File.Exists(ZipPath))
        {
            Status = "请填写有效的地图 zip 路径";
            return;
        }
        try
        {
            var r = MapInstaller.Install(ZipPath, _gameRoot);
            Status = r.Ok
                ? $"安装成功：{r.SaveName} → {r.SaveDir}"
                : $"安装失败：{r.Error}";
        }
        catch (Exception ex)
        {
            Status = $"安装异常：{ex.Message}";
        }
    }
}
