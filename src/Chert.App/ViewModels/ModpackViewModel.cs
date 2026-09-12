using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Windows.Input;
using Chert.Core.Download;
using Chert.Core.Installers;
using Chert.Core.Mvvm;
using Chert.Core.Toolbox;
using Microsoft.Win32;
using Chert.App.Services;

namespace Chert.App.ViewModels;

/// <summary>整合包面板：导出当前环境为整合包 zip，或导入 .mrpack / .zip 整合包。</summary>
public class ModpackViewModel : ObservableObject
{
    private ObservableCollection<string> _versions = new();
    private string _selectedVersion = "";
    private bool _includeMods = true;
    private bool _includeConfig = true;
    private bool _includeResourcePacks = true;
    private bool _includeShaderPacks = true;
    private bool _includeSaves;
    private string _displayName = "";
    private string _statusMessage = "";
    private bool _isBusy;

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

    public bool IncludeMods { get => _includeMods; set => SetField(ref _includeMods, value); }
    public bool IncludeConfig { get => _includeConfig; set => SetField(ref _includeConfig, value); }
    public bool IncludeResourcePacks { get => _includeResourcePacks; set => SetField(ref _includeResourcePacks, value); }
    public bool IncludeShaderPacks { get => _includeShaderPacks; set => SetField(ref _includeShaderPacks, value); }
    public bool IncludeSaves { get => _includeSaves; set => SetField(ref _includeSaves, value); }

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

    public bool IsBusy
    {
        get => _isBusy;
        set => SetField(ref _isBusy, value);
    }

    public ICommand RefreshCommand { get; }
    public ICommand ExportCommand { get; }
    public ICommand ImportCommand { get; }

    public ModpackViewModel()
    {
        RefreshCommand = new RelayCommand(_ => Refresh());
        ExportCommand = new AsyncRelayCommand(_ => ExportAsync(), _ => !IsBusy);
        ImportCommand = new AsyncRelayCommand(_ => ImportAsync(), _ => !IsBusy);
        Refresh();
    }

    public void Refresh()
    {
        Versions = new ObservableCollection<string>(
            LauncherService.Instance.ListInstalledVersions().Select(v => v.Id));
        if (string.IsNullOrEmpty(SelectedVersion))
            SelectedVersion = Versions.FirstOrDefault() ?? "";
    }

    private async Task ExportAsync()
    {
        if (string.IsNullOrEmpty(SelectedVersion)) { StatusMessage = "请先选择版本"; return; }
        var folder = UIService.PickFolder("选择整合包导出目录");
        if (string.IsNullOrEmpty(folder)) return;

        IsBusy = true;
        try
        {
            var root = LauncherService.Instance.GameRoot;
            var dest = Path.Combine(folder, $"mclcs_modpack_{SelectedVersion}_{DateTime.Now:yyyyMMdd}.zip");
            ModpackExporter.Export(root, SelectedVersion, dest, new ModpackExportOptions
            {
                IncludeMods = IncludeMods,
                IncludeConfig = IncludeConfig,
                IncludeResourcePacks = IncludeResourcePacks,
                IncludeShaderPacks = IncludeShaderPacks,
                IncludeSaves = IncludeSaves,
                DisplayName = string.IsNullOrWhiteSpace(DisplayName) ? null : DisplayName
            });
            StatusMessage = $"已导出整合包：{dest}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"导出失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ImportAsync()
    {
        var dlg = new OpenFileDialog
        {
            Filter = "整合包|*.mrpack;*.zip",
            Title = "选择整合包文件"
        };
        if (dlg.ShowDialog() != true) return;

        IsBusy = true;
        try
        {
            var path = dlg.FileName;
            var client = new HttpClient();
            var downloader = new HttpDownloader(client, 8, LauncherService.Instance);
            var root = LauncherService.Instance.GameRoot;

            StatusMessage = $"正在导入 {Path.GetFileName(path)} …";
            if (!path.EndsWith(".mrpack", StringComparison.OrdinalIgnoreCase))
            {
                StatusMessage = "仅支持 Modrinth .mrpack 整合包。";
                return;
            }
            var installer = new ModpackInstaller(root, client, downloader, LauncherService.Instance);
            await installer.InstallAsync(path);
            StatusMessage = $"整合包导入完成：{Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"导入失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
