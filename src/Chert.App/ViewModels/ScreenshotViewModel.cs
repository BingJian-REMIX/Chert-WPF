using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using Chert.Core.Mvvm;
using Chert.Core.Toolbox;
using Chert.App.Services;

namespace Chert.App.ViewModels;

public class ScreenshotRow : ObservableObject
{
    public string Name { get; init; } = "";
    public string FullPath { get; init; } = "";
    public long SizeBytes { get; init; }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }
}

/// <summary>截图管理面板：浏览、批量删除、打包分享。</summary>
public class ScreenshotViewModel : ObservableObject
{
    private ObservableCollection<ScreenshotRow> _shots = new();
    private string _statusMessage = "";

    public ObservableCollection<ScreenshotRow> Shots
    {
        get => _shots;
        set => SetField(ref _shots, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetField(ref _statusMessage, value);
    }

    public ICommand RefreshCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand PackageCommand { get; }

    public ScreenshotViewModel()
    {
        RefreshCommand = new RelayCommand(_ => Refresh());
        DeleteCommand = new RelayCommand(_ => Delete());
        PackageCommand = new RelayCommand(_ => Package());
        Refresh();
    }

    public void Refresh()
    {
        var root = LauncherService.Instance.GameRoot;
        Shots = new ObservableCollection<ScreenshotRow>(
            ScreenshotManager.ListScreenshots(root)
                .Select(s => new ScreenshotRow { Name = s.Name, FullPath = s.FullPath, SizeBytes = s.SizeBytes }));
        StatusMessage = $"共 {Shots.Count} 张截图";
    }

    private void Delete()
    {
        var paths = Shots.Where(s => s.IsSelected).Select(s => s.FullPath).ToList();
        if (paths.Count == 0) { StatusMessage = "请先勾选要删除的截图"; return; }
        var ok = ScreenshotManager.DeleteScreenshots(paths);
        StatusMessage = $"已删除 {ok} 张截图";
        Refresh();
    }

    private void Package()
    {
        var paths = Shots.Where(s => s.IsSelected).Select(s => s.FullPath).ToList();
        if (paths.Count == 0) { StatusMessage = "请先勾选要打包的截图"; return; }
        var dest = UIService.PickFolder("选择打包输出目录");
        if (string.IsNullOrEmpty(dest)) return;
        var zip = Path.Combine(dest, $"mclcs_screenshots_{DateTime.Now:yyyyMMdd_HHmmss}.zip");
        ScreenshotManager.Package(paths, zip);
        StatusMessage = $"已打包 {paths.Count} 张截图到 {zip}";
    }
}
