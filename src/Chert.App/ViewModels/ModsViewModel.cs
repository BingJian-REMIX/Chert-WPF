using System.Collections.ObjectModel;
using System.Net.Http;
using System.Windows.Input;
using Chert.Core.Models;
using Chert.Core.Mods;
using Chert.Core.Mvvm;
using Chert.Core.Utils;

namespace Chert.App.ViewModels;

/// <summary>
/// Mod 管理页 ViewModel（清单 #66）：核验 / 一键更新 / 批量处理。
/// </summary>
public class ModsViewModel : ObservableObject
{
    private ObservableCollection<ModItemViewModel> _items = new();
    private ObservableCollection<DependencyCheckResult> _depResults = new();
    private string _statusMessage = "";
    private bool _isBusy;
    private int _selectedCount;
    private double _updateProgress;
    private string _currentTaskName = "";

    public ObservableCollection<ModItemViewModel> Items
    {
        get => _items;
        set
        {
            if (SetField(ref _items, value)) OnPropertyChanged(nameof(HasUpdateCandidates));
        }
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

    /// <summary>当前勾选的数量。</summary>
    public int SelectedCount
    {
        get => _selectedCount;
        set => SetField(ref _selectedCount, value);
    }

    /// <summary>单个 Mod 的下载进度（0~1）。</summary>
    public double UpdateProgress
    {
        get => _updateProgress;
        set => SetField(ref _updateProgress, value);
    }

    /// <summary>当前正在处理的 Mod 名称（批量时带序号）。</summary>
    public string CurrentTaskName
    {
        get => _currentTaskName;
        set => SetField(ref _currentTaskName, value);
    }

    /// <summary>是否存在可更新的 Mod。</summary>
    public bool HasUpdateCandidates => Items.Any(i => i.HasUpdate);

    // ---- 命令 ----
    public ICommand RefreshModsCommand { get; }
    public ICommand CheckUpdatesCommand { get; }
    public ICommand CheckDependenciesCommand { get; }
    public ICommand UninstallModCommand { get; }
    public ICommand ToggleModCommand { get; }
    public ICommand VerifySelectedCommand { get; }
    public ICommand VerifyAllCommand { get; }
    public ICommand UpdateModCommand { get; }
    public ICommand UpdateSelectedCommand { get; }
    public ICommand UpdateAllCommand { get; }
    public ICommand EnableSelectedCommand { get; }
    public ICommand DisableSelectedCommand { get; }
    public ICommand UninstallSelectedCommand { get; }
    public ICommand SelectAllCommand { get; }
    public ICommand SelectNoneCommand { get; }
    public ICommand SelectOutdatedCommand { get; }

    public ModsViewModel()
    {
        RefreshModsCommand = new RelayCommand(_ => RefreshMods());
        CheckUpdatesCommand = new AsyncRelayCommand(_ => CheckUpdatesAsync());
        CheckDependenciesCommand = new RelayCommand(_ => CheckDependencies());
        UninstallModCommand = new RelayCommand(p => Uninstall(p as ModItemViewModel));
        ToggleModCommand = new RelayCommand(p => Toggle(p as ModItemViewModel));

        VerifySelectedCommand = new RelayCommand(_ => VerifyItems(Items.Where(i => i.IsSelected)));
        VerifyAllCommand = new RelayCommand(_ => VerifyItems(Items));
        UpdateModCommand = new AsyncRelayCommand(p => UpdateOne(p as ModItemViewModel));
        UpdateSelectedCommand = new AsyncRelayCommand(_ => UpdateMany(all: false));
        UpdateAllCommand = new AsyncRelayCommand(_ => UpdateMany(all: true));

        EnableSelectedCommand = new RelayCommand(_ => ToggleSelected(true));
        DisableSelectedCommand = new RelayCommand(_ => ToggleSelected(false));
        UninstallSelectedCommand = new RelayCommand(_ => UninstallSelected());

        SelectAllCommand = new RelayCommand(_ => SelectAll());
        SelectNoneCommand = new RelayCommand(_ => SelectNone());
        SelectOutdatedCommand = new RelayCommand(_ => SelectOutdated());

        RefreshMods();
    }

    private static ModManager CreateManager()
        => new(GameConstants.DefaultGameRoot, new HttpClient(), null!);

    private void RefreshMods()
    {
        var mods = CreateManager().ListInstalledMods(includeDisabled: true);
        ApplyMods(mods);
        StatusMessage = mods.Count == 0 ? "未找到已安装的 Mod" : $"共 {mods.Count} 个 Mod";
    }

    private void ApplyMods(IEnumerable<ModEntry> mods)
    {
        var selected = new HashSet<string>(
            _items.Where(i => i.IsSelected).Select(i => i.FileName),
            StringComparer.OrdinalIgnoreCase);

        var list = new ObservableCollection<ModItemViewModel>();
        foreach (var m in mods)
        {
            var item = new ModItemViewModel(m);
            if (selected.Contains(m.FileName)) item.IsSelected = true;
            item.PropertyChanged += OnItemPropertyChanged;
            list.Add(item);
        }

        Items = list;
        RefreshSelectedCount();
    }

    private void OnItemPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ModItemViewModel.IsSelected)) RefreshSelectedCount();
    }

    private void RefreshSelectedCount() => SelectedCount = Items.Count(i => i.IsSelected);

    private async Task CheckUpdatesAsync()
    {
        IsBusy = true;
        CurrentTaskName = "检查更新";
        try
        {
            var manager = CreateManager();
            var updated = await manager.CheckForUpdatesAsync();
            // CheckForUpdatesAsync 只扫 .jar，这里把被禁用的补回来，避免列表「少一半」
            var disabled = manager.ListInstalledMods(includeDisabled: true).Where(m => !m.Enabled);
            ApplyMods(updated.Concat(disabled));

            var count = Items.Count(i => i.HasUpdate);
            StatusMessage = count == 0 ? "所有 Mod 均为最新" : $"{count} 个 Mod 有新版本";
        }
        catch (Exception ex)
        {
            StatusMessage = $"检查更新失败：{ex.Message}";
        }
        finally { IsBusy = false; CurrentTaskName = ""; }
    }

    private void CheckDependencies()
    {
        var results = CreateManager().CheckDependencies();
        DepResults = new ObservableCollection<DependencyCheckResult>(results);
        StatusMessage = results.Count == 0 ? "所有依赖已满足" : $"{results.Count} 个 Mod 存在依赖问题";
    }

    /// <summary>核验：存在性、体积、Jar 完整性、元数据、SHA-1。</summary>
    private void VerifyItems(IEnumerable<ModItemViewModel> targets)
    {
        var list = targets.ToList();
        if (list.Count == 0)
        {
            StatusMessage = "请先勾选要核验的 Mod";
            return;
        }

        var manager = CreateManager();
        var bad = 0;
        foreach (var item in list)
        {
            var result = manager.VerifyMod(item.FileName);
            item.ApplyVerify(result);
            if (!result.Ok) bad++;
        }

        StatusMessage = bad == 0
            ? $"{list.Count} 个 Mod 核验通过"
            : $"核验完成：{bad}/{list.Count} 个异常";
    }

    /// <summary>一键更新单个 Mod。</summary>
    private async Task UpdateOne(ModItemViewModel? item)
    {
        if (item is null) return;
        IsBusy = true;
        CurrentTaskName = item.Name;
        UpdateProgress = 0;
        try
        {
            var progress = new Progress<double>(v => UpdateProgress = v);
            var outcome = await CreateManager().UpdateModAsync(item.Mod, progress);
            StatusMessage = $"{item.Name}：{outcome.Message}";
            RefreshMods();
        }
        finally { IsBusy = false; CurrentTaskName = ""; UpdateProgress = 0; }
    }

    /// <summary>批量更新：all=false 只更新勾选中有新版本的，all=true 更新全部有新版本的。</summary>
    private async Task UpdateMany(bool all)
    {
        var targets = Items
            .Where(i => i.HasUpdate && (all || i.IsSelected))
            .ToList();
        if (targets.Count == 0)
        {
            StatusMessage = "没有需要更新的 Mod";
            return;
        }

        IsBusy = true;
        try
        {
            var manager = CreateManager();
            var ok = 0;
            for (var n = 0; n < targets.Count; n++)
            {
                var item = targets[n];
                CurrentTaskName = $"({n + 1}/{targets.Count}) {item.Name}";
                UpdateProgress = 0;

                var progress = new Progress<double>(v => UpdateProgress = v);
                var outcome = await manager.UpdateModAsync(item.Mod, progress);
                if (outcome.Success)
                {
                    ok++;
                }
                else
                {
                    // 借核验列展示失败原因
                    item.VerifyLevel = "error";
                    item.VerifyText = outcome.Message;
                }
            }
            StatusMessage = $"更新完成：{ok}/{targets.Count} 成功";
            RefreshMods();
        }
        finally { IsBusy = false; CurrentTaskName = ""; UpdateProgress = 0; }
    }

    /// <summary>启用 / 禁用单个 Mod（*.jar.disabled 重命名）。</summary>
    private void Toggle(ModItemViewModel? item)
    {
        if (item is null) return;
        var manager = CreateManager();
        var newName = manager.SetModEnabled(item.FileName, !item.Enabled);
        if (newName is null)
        {
            StatusMessage = $"{item.Name}：切换失败（文件缺失或目标文件名已存在）";
            return;
        }
        StatusMessage = $"{item.Name}：{(item.Enabled ? "已禁用" : "已启用")}";
        RefreshMods();
    }

    /// <summary>批量启用 / 禁用。</summary>
    private void ToggleSelected(bool enable)
    {
        var targets = Items.Where(i => i.IsSelected && i.Enabled != enable).ToList();
        if (targets.Count == 0)
        {
            StatusMessage = "勾选的 Mod 已处于目标状态";
            return;
        }

        var manager = CreateManager();
        var ok = 0;
        foreach (var item in targets)
        {
            if (manager.SetModEnabled(item.FileName, enable) is not null) ok++;
        }

        StatusMessage = $"已{(enable ? "启用" : "禁用")} {ok}/{targets.Count} 个 Mod";
        RefreshMods();
    }

    private void Uninstall(ModItemViewModel? item)
    {
        if (item is null) return;
        if (CreateManager().UninstallMod(item.FileName))
        {
            StatusMessage = $"已卸载 {item.Name}";
            RefreshMods();
        }
    }

    private void UninstallSelected()
    {
        var targets = Items.Where(i => i.IsSelected).ToList();
        if (targets.Count == 0)
        {
            StatusMessage = "请先勾选要卸载的 Mod";
            return;
        }

        var manager = CreateManager();
        var ok = 0;
        foreach (var item in targets)
        {
            if (manager.UninstallMod(item.FileName)) ok++;
        }

        StatusMessage = $"已卸载 {ok}/{targets.Count} 个 Mod";
        RefreshMods();
    }

    private void SelectAll()
    {
        foreach (var i in Items) i.IsSelected = true;
    }

    private void SelectNone()
    {
        foreach (var i in Items) i.IsSelected = false;
    }

    private void SelectOutdated()
    {
        foreach (var i in Items) i.IsSelected = i.HasUpdate;
    }
}
