using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using Chert.Core.Mvvm;
using Chert.Core.Profiles;
using Chert.Core.Save;
using Chert.App.Services;

namespace Chert.App.ViewModels;

/// <summary>「存档」标签页的单行：一个世界及其兼容性/备份信息。</summary>
public class SaveRow : ObservableObject
{
    public string SaveName { get; init; } = "";
    public string SavePath { get; init; } = "";

    /// <summary>存档所在位置（"saves"=共享目录；"versions/&lt;id&gt;"=版本隔离目录）。</summary>
    public string Location { get; init; } = "saves";

    private string _currentVersion = "";
    private int _dataVersion;
    private string _compatText = "";
    private string _severityColor = "#888888";
    private bool _hasBackup;
    private string _backupText = "";
    private string _corruptionText = "";
    private string _corruptionColor = "#888888";
    private bool _hasCorruption;

    public string CurrentVersion { get => _currentVersion; set => SetField(ref _currentVersion, value); }
    public int DataVersion { get => _dataVersion; set => SetField(ref _dataVersion, value); }
    public string CompatText { get => _compatText; set => SetField(ref _compatText, value); }
    public string SeverityColor { get => _severityColor; set => SetField(ref _severityColor, value); }
    public bool HasBackup { get => _hasBackup; set => SetField(ref _hasBackup, value); }
    public string BackupText { get => _backupText; set => SetField(ref _backupText, value); }

    /// <summary>存档损坏检测结果文本。</summary>
    public string CorruptionText { get => _corruptionText; set => SetField(ref _corruptionText, value); }
    /// <summary>损坏指示颜色。</summary>
    public string CorruptionColor { get => _corruptionColor; set => SetField(ref _corruptionColor, value); }
    /// <summary>是否存在致命损坏（无法加载）。</summary>
    public bool HasCorruption { get => _hasCorruption; set => SetField(ref _hasCorruption, value); }
}

/// <summary>
/// 「存档」标签页视图模型（§二.4 兼容性检测 + §三 降级 / 备份回滚）。
/// 选择目标游戏版本后可扫描全部存档的兼容性；对每个存档可一键降级（方案 A）或回滚到备份。
/// </summary>
public class SavesViewModel : ObservableObject
{
    private string _targetVersion = "";
    private ObservableCollection<string> _availableVersions = new();
    private ObservableCollection<SaveRow> _saves = new();
    private string _statusMessage = "";
    private bool _isBusy;

    public ObservableCollection<string> AvailableVersions
    {
        get => _availableVersions;
        set => SetField(ref _availableVersions, value);
    }

    public string TargetVersion
    {
        get => _targetVersion;
        set => SetField(ref _targetVersion, value);
    }

    public ObservableCollection<SaveRow> Saves
    {
        get => _saves;
        set => SetField(ref _saves, value);
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
    public ICommand ScanCorruptionCommand { get; }
    public ICommand DowngradeCommand { get; }
    public ICommand RestoreCommand { get; }
    public ICommand BackupSaveCommand { get; }
    public ICommand DeleteSaveCommand { get; }
    public ICommand ExtractSeedCommand { get; }

    public SavesViewModel()
    {
        ScanCommand = new AsyncRelayCommand(_ => ScanAsync(), _ => !IsBusy);
        ScanCorruptionCommand = new AsyncRelayCommand(_ => ScanCorruptionAsync(), _ => !IsBusy);
        DowngradeCommand = new AsyncRelayCommand(p => DowngradeAsync(p as string), _ => !IsBusy);
        RestoreCommand = new AsyncRelayCommand(p => RestoreAsync(p as string), _ => !IsBusy);
        BackupSaveCommand = new AsyncRelayCommand(p => BackupSaveAsync(p as string), _ => !IsBusy);
        DeleteSaveCommand = new AsyncRelayCommand(p => DeleteSaveAsync(p as string), _ => !IsBusy);
        ExtractSeedCommand = new RelayCommand(p => ExtractSeed(p as string));
        RefreshVersions();
        _ = ScanAsync();
    }

    private void RefreshVersions()
    {
        var gameRoot = LauncherService.Instance.GameRoot;
        // 存档管理目标版本只应列出已安装版本，避免显示用户未安装的版本
        var installed = LauncherService.Instance.ListInstalledVersions().Select(v => v.Id).ToList();
        AvailableVersions = new ObservableCollection<string>(installed);
        TargetVersion = installed.FirstOrDefault() ?? "";
    }

    private async Task ScanAsync()
    {
        IsBusy = true;
        try
        {
            var gameRoot = LauncherService.Instance.GameRoot;
            var target = TargetVersion;
            var rows = new ObservableCollection<SaveRow>();

            // 待扫描目录：共享 saves/ + 各版本隔离 versions/<id>/saves/
            // （版本隔离存档此前未被扫描，是「扫描不全」的根因）
            var scanRoots = new List<(string Dir, string Location)>();
            var sharedSaves = SaveCompatibilityDetector.SavesDir(gameRoot);
            if (Directory.Exists(sharedSaves))
                scanRoots.Add((sharedSaves, "saves"));
            foreach (var v in LauncherService.Instance.ListInstalledVersions())
            {
                var iso = Path.Combine(gameRoot, "versions", v.Id, "saves");
                if (Directory.Exists(iso)) scanRoots.Add((iso, $"versions/{v.Id}"));
            }

            if (scanRoots.Count == 0)
            {
                Saves = rows;
                StatusMessage = "未找到任何 saves 目录。";
                return;
            }

            foreach (var (savesDir, location) in scanRoots)
            {
                foreach (var dir in Directory.GetDirectories(savesDir))
                {
                    var name = Path.GetFileName(dir);
                    if (System.Text.RegularExpressions.Regex.IsMatch(name, @"\.backup-\d{14}$")) continue;

                    var dv = SaveDowngrader.GetSaveDataVersion(dir);
                    var curVer = DataVersionMap.DescribeDataVersion(dv);
                    var row = new SaveRow
                    {
                        SaveName = name,
                        SavePath = dir,
                        Location = location,
                        CurrentVersion = curVer,
                        DataVersion = dv
                    };

                    // 单存档兼容性检测（共享/隔离目录统一走 CheckSingleSave；level.dat 缺失时返回 null）
                    var rep = string.IsNullOrEmpty(target) ? null : SaveCompatibilityDetector.CheckSingleSave(dir, target);
                    if (rep is not null)
                    {
                        row.CompatText = rep.Message;
                        row.SeverityColor = rep.Compatible ? "#5BBF6A"
                            : rep.Severity == SaveCompatibilitySeverity.MuchNewer ? "#E0533A"
                            : "#E0A040";
                    }
                    else
                    {
                        var lvlPath = SaveCompatibilityDetector.LevelDatPath(dir);
                        if (!File.Exists(lvlPath))
                        {
                            // 无 level.dat：不是有效存档，标记警告而非误报为「兼容」
                            row.CompatText = "缺少 level.dat，可能不是有效的 Minecraft 存档。";
                            row.SeverityColor = "#E0A040";
                        }
                        else
                        {
                            row.CompatText = string.IsNullOrEmpty(target)
                                ? "选择目标游戏版本后可检测兼容性。"
                                : "兼容。";
                            row.SeverityColor = "#5BBF6A";
                        }
                    }

                    // 备份发现：使用存档所在目录（而非固定共享 saves/），版本隔离存档的备份也能正确识别
                    var backups = SaveCompatibilityDetector.FindBackups(Path.GetDirectoryName(dir) ?? savesDir, name);
                    row.HasBackup = backups.Count > 0;
                    row.BackupText = backups.Count > 0
                        ? $"{backups.Count} 个备份（最新 {backups[^1].CreatedUtc:yyyy-MM-dd HH:mm}）"
                        : "无备份";

                // 存档损坏检测（只读，不修复）
                var corrupt = SaveCorruptionDetector.ScanSingle(dir);
                if (corrupt is not null && corrupt.Severity != SaveCorruptionSeverity.Ok)
                {
                    row.HasCorruption = corrupt.IsCorrupt;
                    row.CorruptionColor = corrupt.Severity == SaveCorruptionSeverity.Corrupt ? "#E0533A" : "#E0A040";
                    row.CorruptionText = corrupt.Summary;
                }
                else
                {
                    row.CorruptionText = corrupt?.Summary ?? "✓ 未检测到损坏。";
                    row.CorruptionColor = "#5BBF6A";
                }

                rows.Add(row);
            }
            }

            Saves = rows;
            StatusMessage = rows.Count > 0
                ? $"共 {rows.Count} 个存档（目标版本：{target}）"
                : "未发现存档。";
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

    private async Task ScanCorruptionAsync()
    {
        IsBusy = true;
        try
        {
            var gameRoot = LauncherService.Instance.GameRoot;
            var reports = await Task.Run(() => SaveCorruptionDetector.Scan(gameRoot));

            var corrupt = reports.Where(r => r.IsCorrupt).ToList();
            var warn = reports.Where(r => !r.IsCorrupt && r.Severity == SaveCorruptionSeverity.Warning).ToList();

            // 同步刷新列表中各行的损坏状态
            foreach (var row in Saves)
            {
                var hit = reports.FirstOrDefault(r => r.SavePath == row.SavePath);
                if (hit is null || hit.Severity == SaveCorruptionSeverity.Ok)
                {
                    row.HasCorruption = false;
                    row.CorruptionColor = "#5BBF6A";
                    row.CorruptionText = "✓ 未检测到损坏。";
                }
                else
                {
                    row.HasCorruption = hit.IsCorrupt;
                    row.CorruptionColor = hit.Severity == SaveCorruptionSeverity.Corrupt ? "#E0533A" : "#E0A040";
                    row.CorruptionText = hit.Summary;
                }
            }

            if (corrupt.Count == 0 && warn.Count == 0)
                StatusMessage = "存档损坏扫描完成：未发现损坏。";
            else
                StatusMessage = $"存档损坏扫描完成：{corrupt.Count} 个可能损坏，{warn.Count} 个需注意。";
            ToastService.Show("存档损坏检测",
                $"{corrupt.Count} 个可能损坏，{warn.Count} 个需注意。",
                corrupt.Count > 0 ? ToastKind.Warning : ToastKind.Success);
        }
        catch (Exception ex)
        {
            StatusMessage = $"损坏扫描失败：{ex.Message}";
        }
        finally { IsBusy = false; }
    }

    private async Task DowngradeAsync(string? savePath)
    {
        if (string.IsNullOrEmpty(savePath) || string.IsNullOrEmpty(TargetVersion)) return;
        IsBusy = true;
        try
        {
            var name = Path.GetFileName(savePath);
            var plan = await SaveDowngrader.DowngradeAsync(savePath, TargetVersion, DowngradeMethod.QuickModifyDataVersion);
            StatusMessage = plan.Success
                ? $"已降级 {name} 到 {TargetVersion}（已备份原档）。"
                : $"降级失败：{plan.ErrorMessage}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"降级异常：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
            await ScanAsync();
        }
    }

    private async Task RestoreAsync(string? savePath)
    {
        if (string.IsNullOrEmpty(savePath)) return;
        IsBusy = true;
        try
        {
            var name = Path.GetFileName(savePath);
            // 备份存放在存档所在目录内（与共享/隔离位置一致），用实际目录查找
            var backups = SaveCompatibilityDetector.FindBackups(Path.GetDirectoryName(savePath) ?? "", name);
            if (backups.Count == 0)
            {
                StatusMessage = $"{name} 没有可回滚的备份。";
                return;
            }
            var latest = backups[^1];
            var replaced = SaveDowngrader.RestoreBackupAsync(latest.BackupPath, savePath);
            StatusMessage = $"已回滚 {name} 到备份（当前档另存于 {Path.GetFileName(replaced)}）。";
        }
        catch (Exception ex)
        {
            StatusMessage = $"回滚异常：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
            await ScanAsync();
        }
    }

    private async Task BackupSaveAsync(string? savePath)
    {
        if (string.IsNullOrEmpty(savePath)) return;
        IsBusy = true;
        try
        {
            var gameRoot = LauncherService.Instance.GameRoot;
            var name = Path.GetFileName(savePath);
            var result = await Task.Run(() =>
                Chert.Core.Toolbox.BackupManager.Create(gameRoot, savePath, Chert.Core.Toolbox.BackupKind.Save,
                    $"手动备份 {name}", auto: false, policy: ProfileStore.Load(gameRoot).Backup));
            StatusMessage = result.Ok
                ? $"已备份 {name}（{result.Record?.SizeText ?? "?"}）"
                : $"备份失败：{result.Error}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"备份异常：{ex.Message}";
        }
        finally { IsBusy = false; await ScanAsync(); }
    }

    private async Task DeleteSaveAsync(string? savePath)
    {
        if (string.IsNullOrEmpty(savePath)) return;
        var name = Path.GetFileName(savePath);
        if (!UIService.Confirm($"确定删除存档「{name}」及其数据？操作不可撤销。", "确认删除")) return;

        IsBusy = true;
        try
        {
            await Task.Run(() =>
            {
                if (Directory.Exists(savePath)) Directory.Delete(savePath, true);
            });
            StatusMessage = $"已删除存档 {name}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"删除失败：{ex.Message}";
        }
        finally { IsBusy = false; await ScanAsync(); }
    }

    private void ExtractSeed(string? savePath)
    {
        if (string.IsNullOrEmpty(savePath)) return;
        try
        {
            var levelDat = Path.Combine(savePath, "level.dat");
            if (!File.Exists(levelDat)) { StatusMessage = "找不到 level.dat"; return; }
            long seed = 0;
            try
            {
                var raw = File.ReadAllText(levelDat);
                var idx = raw.IndexOf("RandomSeed", StringComparison.Ordinal);
                if (idx >= 0)
                {
                    var start = raw.IndexOfAny(new[] { ':', ' ' }, idx + 10) + 1;
                    while (start < raw.Length && raw[start] == ' ') start++;
                    var end = raw.IndexOfAny(new[] { ',', '}', '\n', '\r' }, start);
                    if (end > start && long.TryParse(raw[start..end].Trim(), out var s)) seed = s;
                }
            }
            catch { StatusMessage = "种子提取失败"; return; }
            System.Windows.Clipboard.SetText(seed.ToString());
            StatusMessage = $"种子 {seed} 已复制到剪贴板";
            ToastService.Show("种子", $"{Path.GetFileName(savePath)}: {seed}", ToastKind.Success);
        }
        catch (Exception ex) { StatusMessage = $"提取失败: {ex.Message}"; }
    }
}
