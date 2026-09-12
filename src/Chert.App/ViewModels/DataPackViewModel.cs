using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows.Input;
using Chert.App.Services;
using Chert.Core.Mvvm;
using Chert.Core.Toolbox;

namespace Chert.App.ViewModels;

/// <summary>存档下拉项。</summary>
public class SaveChoice
{
    public string Name { get; init; } = "";
    public string Path { get; init; } = "";

    public override string ToString() => Name;
}

/// <summary>
/// 数据包冲突检测（工具箱面板 12）：扫描当前存档的 datapacks，
/// 检出同名文件覆盖与命名空间 ID 冲突，命中内置规则库时给出处理建议，
/// 点击冲突项可直接跳到对应数据包所在目录。
/// </summary>
public class DataPackViewModel : ObservableObject
{
    /// <summary>规则库联网更新地址（拉不到就继续用内置规则，不影响使用）。经 jsDelivr 国内直连。</summary>
    private const string RulesUpdateUrl =
        "https://cdn.jsdelivr.net/gh/BingJian-REMIX/MCLCS-WPF@main/data/datapack-conflict-rules.json";

    private ObservableCollection<SaveChoice> _saves = new();
    private ObservableCollection<DataPackInfo> _packs = new();
    private ObservableCollection<DataPackConflict> _conflicts = new();
    private ObservableCollection<string> _formatWarnings = new();

    private SaveChoice? _selectedSave;
    private DataPackConflict? _selectedConflict;
    private string _targetVersion = "";
    private string _statusMessage = "选择一个存档后点「扫描」";
    private string _summary = "";
    private string _rulesText = "";
    private bool _isBusy;

    public ObservableCollection<SaveChoice> Saves
    {
        get => _saves;
        set => SetField(ref _saves, value);
    }

    public ObservableCollection<DataPackInfo> Packs
    {
        get => _packs;
        set => SetField(ref _packs, value);
    }

    public ObservableCollection<DataPackConflict> Conflicts
    {
        get => _conflicts;
        set => SetField(ref _conflicts, value);
    }

    public ObservableCollection<string> FormatWarnings
    {
        get => _formatWarnings;
        set => SetField(ref _formatWarnings, value);
    }

    public SaveChoice? SelectedSave
    {
        get => _selectedSave;
        set => SetField(ref _selectedSave, value);
    }

    public DataPackConflict? SelectedConflict
    {
        get => _selectedConflict;
        set
        {
            if (SetField(ref _selectedConflict, value)) OnPropertyChanged(nameof(AdviceText));
        }
    }

    /// <summary>选中冲突的处理建议（没有建议时给一句通用说明）。</summary>
    public string AdviceText => SelectedConflict?.Advice
                               ?? (SelectedConflict is null
                                   ? "选中一处冲突可查看处理建议"
                                   : "Minecraft 采用「后加载者胜出」，调整数据包加载顺序即可改变生效方。");

    /// <summary>目标游戏版本（用于 pack_format 告警，可留空）。</summary>
    public string TargetVersion
    {
        get => _targetVersion;
        set => SetField(ref _targetVersion, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetField(ref _statusMessage, value);
    }

    public string Summary
    {
        get => _summary;
        set => SetField(ref _summary, value);
    }

    public string RulesText
    {
        get => _rulesText;
        set => SetField(ref _rulesText, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        set => SetField(ref _isBusy, value);
    }

    public ICommand ScanCommand { get; }
    public ICommand RefreshSavesCommand { get; }
    public ICommand JumpToPackCommand { get; }
    public ICommand UpdateRulesCommand { get; }
    public ICommand ResetRulesCommand { get; }

    public DataPackViewModel()
    {
        ScanCommand = new AsyncRelayCommand(_ => ScanAsync(), _ => !IsBusy);
        RefreshSavesCommand = new RelayCommand(_ => LoadSaves());
        JumpToPackCommand = new RelayCommand(p => JumpToPack(p as DataPackConflict ?? SelectedConflict));
        UpdateRulesCommand = new AsyncRelayCommand(_ => UpdateRulesAsync(), _ => !IsBusy);
        ResetRulesCommand = new RelayCommand(_ => ResetRules());

        LoadSaves();
        RefreshRulesText();
    }

    private static string GameRoot => LauncherService.Instance.GameRoot;

    private void RefreshRulesText()
    {
        var rules = DataPackConflictDetector.LoadRules(GameRoot);
        var custom = File.Exists(Path.Combine(GameRoot, DataPackConflictDetector.RulesFileName));
        RulesText = $"规则库：{rules.Count} 条（{(custom ? "已联网更新" : "内置")}）";
    }

    private void LoadSaves()
    {
        var list = new List<SaveChoice>();
        try
        {
            var savesDir = Path.Combine(GameRoot, "saves");
            if (Directory.Exists(savesDir))
            {
                foreach (var dir in Directory.GetDirectories(savesDir).OrderBy(Path.GetFileName))
                    list.Add(new SaveChoice { Name = Path.GetFileName(dir), Path = dir });
            }
        }
        catch { /* 目录不可读 */ }

        var keep = SelectedSave?.Path;
        Saves = new ObservableCollection<SaveChoice>(list);
        SelectedSave = list.FirstOrDefault(s => s.Path == keep) ?? list.FirstOrDefault();

        if (list.Count == 0) StatusMessage = "没找到任何存档（saves 目录为空）";
    }

    private async Task ScanAsync()
    {
        if (SelectedSave is null) { StatusMessage = "请先选择存档"; return; }

        IsBusy = true;
        StatusMessage = "正在扫描数据包 …";
        try
        {
            var save = SelectedSave;
            var version = string.IsNullOrWhiteSpace(TargetVersion) ? null : TargetVersion.Trim();

            var report = await Task.Run(() =>
                DataPackConflictDetector.Scan(save.Path, version, GameRoot));

            Packs = new ObservableCollection<DataPackInfo>(report.Packs);
            Conflicts = new ObservableCollection<DataPackConflict>(report.Conflicts);
            FormatWarnings = new ObservableCollection<string>(report.FormatWarnings);
            SelectedConflict = report.Conflicts.FirstOrDefault();

            var critical = report.Conflicts.Count(c => c.Severity == ConflictSeverity.Critical);
            Summary = report.Summary + (critical > 0 ? $"，其中严重 {critical} 处" : "");

            StatusMessage = report.Packs.Count == 0
                ? "该存档没有数据包（datapacks 目录为空）"
                : report.HasConflicts
                    ? $"检出 {report.Conflicts.Count} 处冲突，点击条目查看建议"
                    : "未发现冲突，数据包干净";

            if (critical > 0)
                ToastService.Show("数据包冲突", $"{save.Name}：{critical} 处严重冲突", ToastKind.Warning);
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

    /// <summary>
    /// 跳转到冲突涉及的数据包：打开存档的 datapacks 目录并选中该包。
    /// </summary>
    private void JumpToPack(DataPackConflict? conflict)
    {
        if (conflict is null) { StatusMessage = "请先选中一处冲突"; return; }
        if (SelectedSave is null) return;

        var packName = conflict.Winner;
        var pack = Packs.FirstOrDefault(p => p.Name == packName);
        var target = pack?.Path ?? DataPackConflictDetector.DataPacksDir(SelectedSave.Path);

        try
        {
            if (File.Exists(target))
            {
                // zip 形式的包：在资源管理器里选中该文件
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{target}\"",
                    UseShellExecute = true
                });
            }
            else
            {
                var dir = Directory.Exists(target)
                    ? target
                    : DataPackConflictDetector.DataPacksDir(SelectedSave.Path);
                Directory.CreateDirectory(dir);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = dir,
                    UseShellExecute = true
                });
            }
            StatusMessage = $"已定位到 {packName}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"跳转失败：{ex.Message}";
        }
    }

    private async Task UpdateRulesAsync()
    {
        IsBusy = true;
        StatusMessage = "正在拉取最新的冲突规则库 …";
        try
        {
            var json = await LauncherService.Instance.ApiClient.GetStringAsync(RulesUpdateUrl);
            var rules = JsonSerializer.Deserialize<List<ConflictRule>>(json);

            if (rules is not { Count: > 0 })
            {
                StatusMessage = "远端规则库为空或格式不对，已保留当前规则";
                return;
            }

            if (DataPackConflictDetector.SaveRules(GameRoot, rules))
            {
                StatusMessage = $"规则库已更新到 {rules.Count} 条";
                ToastService.Show("规则库已更新", $"共 {rules.Count} 条冲突规则", ToastKind.Success);
                RefreshRulesText();
            }
            else
            {
                StatusMessage = "规则库写入失败（游戏目录不可写？）";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"更新失败：{ex.Message}（继续使用内置规则）";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ResetRules()
    {
        try
        {
            var path = Path.Combine(GameRoot, DataPackConflictDetector.RulesFileName);
            if (File.Exists(path)) File.Delete(path);
            StatusMessage = "已恢复内置规则库";
            RefreshRulesText();
        }
        catch (Exception ex)
        {
            StatusMessage = $"恢复失败：{ex.Message}";
        }
    }
}
