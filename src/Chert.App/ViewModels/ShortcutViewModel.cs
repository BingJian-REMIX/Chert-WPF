using System.Collections.ObjectModel;
using System.Windows.Input;
using Chert.Core.Mvvm;
using Chert.Core.Toolbox;
using Chert.App.Services;

namespace Chert.App.ViewModels;

/// <summary>快捷方式面板：为指定版本创建桌面快捷方式（双击即用该版本启动）。</summary>
public class ShortcutViewModel : ObservableObject
{
    private ObservableCollection<string> _versions = new();
    private string _selectedVersion = "";
    private string _displayName = "";
    private string _statusMessage = "";

    public ObservableCollection<string> Versions
    {
        get => _versions;
        set => SetField(ref _versions, value);
    }

    public string SelectedVersion
    {
        get => _selectedVersion;
        set => SetField(ref _selectedVersion, value);
    }

    public string DisplayName
    {
        get => _displayName;
        set => SetField(ref _displayName, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetField(ref _statusMessage, value);
    }

    public ICommand RefreshCommand { get; }
    public ICommand CreateCommand { get; }

    public ShortcutViewModel()
    {
        RefreshCommand = new RelayCommand(_ => Refresh());
        CreateCommand = new RelayCommand(_ => Create());
        Refresh();
    }

    public void Refresh()
    {
        Versions = new ObservableCollection<string>(
            LauncherService.Instance.ListInstalledVersions().Select(v => v.Id));
        if (string.IsNullOrEmpty(SelectedVersion))
            SelectedVersion = Versions.FirstOrDefault() ?? "";
    }

    private void Create()
    {
        if (string.IsNullOrEmpty(SelectedVersion))
        {
            StatusMessage = "请先选择版本";
            return;
        }
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var res = ShortcutGenerator.CreateShortcut(desktop, SelectedVersion, DisplayName);
        StatusMessage = res.Success
            ? $"已创建快捷方式（{res.Method}）：{res.FilePath}"
            : $"创建失败：{res.Error}";
    }
}
