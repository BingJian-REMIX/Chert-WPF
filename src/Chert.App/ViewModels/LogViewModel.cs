using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using Chert.Core.Mvvm;
using Chert.Core.Toolbox;
using Chert.App.Services;

namespace Chert.App.ViewModels;

/// <summary>日志管理面板：列出游戏日志/崩溃报告，读取、过滤、导出。</summary>
public class LogViewModel : ObservableObject
{
    private ObservableCollection<LogFileInfo> _files = new();
    private LogFileInfo? _selectedFile;
    private ObservableCollection<LogLine> _lines = new();
    private string _keyword = "";
    private bool _onlyErrors;
    private string _statusMessage = "";

    public ObservableCollection<LogFileInfo> Files
    {
        get => _files;
        set => SetField(ref _files, value);
    }

    public LogFileInfo? SelectedFile
    {
        get => _selectedFile;
        set
        {
            if (SetField(ref _selectedFile, value))
                _ = LoadSelectedAsync();
        }
    }

    public ObservableCollection<LogLine> Lines
    {
        get => _lines;
        set => SetField(ref _lines, value);
    }

    public string Keyword
    {
        get => _keyword;
        set
        {
            if (SetField(ref _keyword, value))
                ApplyFilter();
        }
    }

    public bool OnlyErrors
    {
        get => _onlyErrors;
        set
        {
            if (SetField(ref _onlyErrors, value))
                ApplyFilter();
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetField(ref _statusMessage, value);
    }

    public ICommand RefreshCommand { get; }
    public ICommand ExportCommand { get; }

    public LogViewModel()
    {
        RefreshCommand = new RelayCommand(_ => Refresh());
        ExportCommand = new RelayCommand(_ => Export());
        Refresh();
    }

    public void Refresh()
    {
        var root = LauncherService.Instance.GameRoot;
        Files = new ObservableCollection<LogFileInfo>(LogManager.ListLogs(root));
        StatusMessage = $"共 {Files.Count} 个日志/崩溃报告文件";
    }

    private void ApplyFilter()
    {
        if (SelectedFile is null) return;
        var text = LogManager.ReadLog(SelectedFile.FullPath);
        var all = LogManager.ParseLines(text);
        Lines = new ObservableCollection<LogLine>(LogManager.Filter(all, Keyword, OnlyErrors));
    }

    private void Export()
    {
        if (SelectedFile is null) { StatusMessage = "请先选择一个文件"; return; }
        var dest = UIService.PickFolder("选择导出目录");
        if (string.IsNullOrEmpty(dest)) return;
        var target = Path.Combine(dest, SelectedFile.Name);
        var ok = LogManager.Export(SelectedFile.FullPath, target);
        StatusMessage = ok ? $"已导出到 {target}" : "导出失败";
    }

    private void LoadSelected()
    {
        if (SelectedFile is null) { Lines = new(); return; }
        var text = LogManager.ReadLog(SelectedFile.FullPath);
        var all = LogManager.ParseLines(text);
        Lines = new ObservableCollection<LogLine>(LogManager.Filter(all, Keyword, OnlyErrors));
    }

    private Task LoadSelectedAsync()
    {
        LoadSelected();
        return Task.CompletedTask;
    }
}
