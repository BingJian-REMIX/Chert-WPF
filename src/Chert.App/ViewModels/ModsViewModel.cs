using System.Collections.ObjectModel;
using System.Net.Http;
using System.Windows.Input;
using Chert.Core.Models;
using Chert.Core.Mods;
using Chert.Core.Mvvm;
using Chert.Core.Utils;

namespace Chert.App.ViewModels;

/// <summary>Mod 管理页 ViewModel。</summary>
public class ModsViewModel : ObservableObject
{
    private ObservableCollection<ModEntry> _mods = new();
    private ObservableCollection<DependencyCheckResult> _depResults = new();
    private string _statusMessage = "";
    private bool _isBusy;

    public ObservableCollection<ModEntry> Mods
    {
        get => _mods;
        set => SetField(ref _mods, value);
    }

    public ObservableCollection<DependencyCheckResult> DepResults
    {
        get => _depResults;
        set => SetField(ref _depResults, value);
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

    public ICommand RefreshModsCommand { get; }
    public ICommand CheckUpdatesCommand { get; }
    public ICommand CheckDependenciesCommand { get; }
    public ICommand UninstallModCommand { get; }

    public ModsViewModel()
    {
        RefreshModsCommand = new RelayCommand(_ => RefreshMods());
        CheckUpdatesCommand = new AsyncRelayCommand(_ => CheckUpdatesAsync());
        CheckDependenciesCommand = new RelayCommand(_ => CheckDependencies());
        UninstallModCommand = new RelayCommand(p => UninstallMod(p as string));
        RefreshMods();
    }

    private void RefreshMods()
    {
        var manager = new ModManager(GameConstants.DefaultGameRoot, new HttpClient(), null!);
        Mods = new ObservableCollection<ModEntry>(manager.ListInstalledMods());
        StatusMessage = Mods.Count == 0 ? "未找到已安装的 Mod" : $"共 {Mods.Count} 个 Mod";
    }

    private async Task CheckUpdatesAsync()
    {
        IsBusy = true;
        try
        {
            var manager = new ModManager(GameConstants.DefaultGameRoot, new HttpClient(), null!);
            var updated = await manager.CheckForUpdatesAsync();
            Mods = new ObservableCollection<ModEntry>(updated);
            var hasUpdate = updated.Count(m => m.HasUpdate);
            StatusMessage = hasUpdate == 0 ? "所有 Mod 均为最新" : $"{hasUpdate} 个 Mod 有新版本";
        }
        catch (Exception ex)
        {
            StatusMessage = $"检查更新失败：{ex.Message}";
        }
        finally { IsBusy = false; }
    }

    private void CheckDependencies()
    {
        var manager = new ModManager(GameConstants.DefaultGameRoot, new HttpClient(), null!);
        var results = manager.CheckDependencies();
        DepResults = new ObservableCollection<DependencyCheckResult>(results);
        StatusMessage = results.Count == 0 ? "所有依赖已满足" : $"{results.Count} 个 Mod 存在依赖问题";
    }

    private void UninstallMod(string? fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return;
        var manager = new ModManager(GameConstants.DefaultGameRoot, new HttpClient(), null!);
        if (manager.UninstallMod(fileName))
        {
            StatusMessage = $"已卸载 {fileName}";
            RefreshMods();
        }
    }
}
