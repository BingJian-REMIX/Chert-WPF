using System.Collections.ObjectModel;
using System.Windows.Input;
using Chert.Core.Mvvm;
using Chert.Core.Toolbox;
using Chert.App.Services;

namespace Chert.App.ViewModels;

public class RedundantRow : ObservableObject
{
    public string RelativePath { get; init; } = "";
    public string FullPath { get; init; } = "";
    public long SizeBytes { get; init; }

    private bool _isSelected = true;
    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }
}

/// <summary>冗余文件清理面板：扫描未被引用库/资源并清理（默认移入回收目录）。</summary>
public class RedundantCleanViewModel : ObservableObject
{
    private ObservableCollection<RedundantRow> _files = new();
    private bool _deleteDirectly;
    private string _statusMessage = "";
    private bool _isBusy;

    public ObservableCollection<RedundantRow> Files
    {
        get => _files;
        set => SetField(ref _files, value);
    }

    public bool DeleteDirectly
    {
        get => _deleteDirectly;
        set => SetField(ref _deleteDirectly, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetField(ref _statusMessage, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        set => SetField(ref _isBusy, value);
    }

    public ICommand ScanCommand { get; }
    public ICommand CleanCommand { get; }

    public RedundantCleanViewModel()
    {
        ScanCommand = new AsyncRelayCommand(_ => ScanAsync(), _ => !IsBusy);
        CleanCommand = new AsyncRelayCommand(_ => CleanAsync(), _ => !IsBusy);
        _ = ScanAsync();
    }

    private async Task ScanAsync()
    {
        IsBusy = true;
        try
        {
            var root = LauncherService.Instance.GameRoot;
            var list = RedundantFileCleaner.Scan(root);
            Files = new ObservableCollection<RedundantRow>(
                list.Select(f => new RedundantRow
                {
                    RelativePath = f.RelativePath,
                    FullPath = f.FullPath,
                    SizeBytes = f.SizeBytes
                }));
            StatusMessage = Files.Count > 0
                ? $"发现 {Files.Count} 个冗余文件"
                : "未检测到冗余文件（干净）";
        }
        catch (Exception ex)
        {
            StatusMessage = $"扫描失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task CleanAsync()
    {
        var selected = Files.Where(f => f.IsSelected).ToList();
        if (selected.Count == 0) { StatusMessage = "请先勾选要清理的文件"; return; }
        IsBusy = true;
        try
        {
            var root = LauncherService.Instance.GameRoot;
            var ok = RedundantFileCleaner.Clean(selected.Select(f => new RedundantFile
            {
                FullPath = f.FullPath,
                RelativePath = f.RelativePath,
                SizeBytes = f.SizeBytes
            }), root, DeleteDirectly);
            StatusMessage = DeleteDirectly
                ? $"已直接删除 {ok} 个文件"
                : $"已移入回收目录 {ok} 个文件（可还原）";
            await ScanAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }
}
