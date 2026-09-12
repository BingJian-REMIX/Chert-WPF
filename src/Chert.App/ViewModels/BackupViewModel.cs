using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using Chert.App.Services;
using Chert.Core.Mvvm;
using Chert.Core.Profiles;
using Chert.Core.Toolbox;

namespace Chert.App.ViewModels;

/// <summary>可备份的来源（存档 / 配置 / Mod 目录）。</summary>
public class BackupSourceItem
{
    public string Display { get; init; } = "";
    public string Path { get; init; } = "";
    public BackupKind Kind { get; init; }

    public override string ToString() => Display;
}

/// <summary>
/// 备份管理器（工具箱面板 10）：
/// 仅备份存档打包 zip、自定义存储路径、定时备份（每天/每周/每月）、
/// 数量限制自动删旧、手动备份 / 恢复（恢复前自动备份当前状态）。
/// </summary>
public class BackupViewModel : ObservableObject
{
    private static readonly string[] ScheduleOptions = { "关闭", "每天", "每周", "每月" };

    private ObservableCollection<BackupRecord> _records = new();
    private ObservableCollection<BackupSourceItem> _sources = new();
    private BackupSourceItem? _selectedSource;
    private BackupRecord? _selectedRecord;

    private string _folder = "backups";
    private int _keepPerSource = 5;
    private int _maxAgeDays = 30;
    private string _selectedSchedule = "关闭";
    private bool _backupBeforeRestore = true;
    private bool _autoBeforeLaunch;
    private string _note = "";

    private string _statusMessage = "";
    private string _totalText = "";
    private bool _isBusy;

    public ObservableCollection<BackupRecord> Records
    {
        get => _records;
        set => SetField(ref _records, value);
    }

    public ObservableCollection<BackupSourceItem> Sources
    {
        get => _sources;
        set => SetField(ref _sources, value);
    }

    public BackupSourceItem? SelectedSource
    {
        get => _selectedSource;
        set => SetField(ref _selectedSource, value);
    }

    public BackupRecord? SelectedRecord
    {
        get => _selectedRecord;
        set => SetField(ref _selectedRecord, value);
    }

    /// <summary>备份存储路径（相对 gameRoot 或绝对路径，如移动硬盘）。</summary>
    public string Folder
    {
        get => _folder;
        set => SetField(ref _folder, value);
    }

    /// <summary>每个来源最多保留几份（超出自动删旧）。</summary>
    public int KeepPerSource
    {
        get => _keepPerSource;
        set => SetField(ref _keepPerSource, value);
    }

    public int MaxAgeDays
    {
        get => _maxAgeDays;
        set => SetField(ref _maxAgeDays, value);
    }

    public string[] Schedules => ScheduleOptions;

    public string SelectedSchedule
    {
        get => _selectedSchedule;
        set => SetField(ref _selectedSchedule, value);
    }

    public bool BackupBeforeRestore
    {
        get => _backupBeforeRestore;
        set => SetField(ref _backupBeforeRestore, value);
    }

    public bool AutoBeforeLaunch
    {
        get => _autoBeforeLaunch;
        set => SetField(ref _autoBeforeLaunch, value);
    }

    public string Note
    {
        get => _note;
        set => SetField(ref _note, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetField(ref _statusMessage, value);
    }

    public string TotalText
    {
        get => _totalText;
        set => SetField(ref _totalText, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        set => SetField(ref _isBusy, value);
    }

    public ICommand RefreshCommand { get; }
    public ICommand CreateCommand { get; }
    public ICommand RestoreCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand PruneCommand { get; }
    public ICommand BrowseFolderCommand { get; }
    public ICommand SavePolicyCommand { get; }
    public ICommand OpenFolderCommand { get; }

    public BackupViewModel()
    {
        RefreshCommand = new AsyncRelayCommand(_ => RefreshAsync(), _ => !IsBusy);
        CreateCommand = new AsyncRelayCommand(_ => CreateAsync(), _ => !IsBusy);
        RestoreCommand = new AsyncRelayCommand(_ => RestoreAsync(), _ => !IsBusy);
        DeleteCommand = new AsyncRelayCommand(_ => DeleteAsync(), _ => !IsBusy);
        PruneCommand = new AsyncRelayCommand(_ => PruneAsync(), _ => !IsBusy);
        BrowseFolderCommand = new RelayCommand(_ => BrowseFolder());
        SavePolicyCommand = new RelayCommand(_ => SavePolicy());
        OpenFolderCommand = new RelayCommand(_ => OpenFolder());

        LoadPolicy();
        _ = RefreshAsync();
    }

    private static string GameRoot => LauncherService.Instance.GameRoot;

    /// <summary>把界面上的策略字段汇成一个 <see cref="BackupPolicy"/>。</summary>
    private BackupPolicy CurrentPolicy() => new()
    {
        Folder = string.IsNullOrWhiteSpace(Folder) ? "backups" : Folder.Trim(),
        KeepPerSource = Math.Max(0, KeepPerSource),
        MaxAgeDays = Math.Max(0, MaxAgeDays),
        Schedule = Array.IndexOf(ScheduleOptions, SelectedSchedule) switch
        {
            1 => BackupSchedule.Daily,
            2 => BackupSchedule.Weekly,
            3 => BackupSchedule.Monthly,
            _ => BackupSchedule.Off
        },
        BackupBeforeRestore = BackupBeforeRestore,
        AutoBeforeLaunch = AutoBeforeLaunch,
        LastScheduledRun = ProfileStore.Load(GameRoot).Backup.LastScheduledRun
    };

    private void LoadPolicy()
    {
        var p = ProfileStore.Load(GameRoot).Backup;
        Folder = p.Folder;
        KeepPerSource = p.KeepPerSource;
        MaxAgeDays = p.MaxAgeDays;
        BackupBeforeRestore = p.BackupBeforeRestore;
        AutoBeforeLaunch = p.AutoBeforeLaunch;
        SelectedSchedule = BackupManager.ScheduleText(p.Schedule);
    }

    private void SavePolicy()
    {
        try
        {
            var profile = ProfileStore.Load(GameRoot);
            profile.Backup = CurrentPolicy();
            ProfileStore.Save(profile);
            StatusMessage = "备份策略已保存";
            ToastService.Show("备份管理器", "备份策略已保存", ToastKind.Success);
            _ = RefreshAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"保存失败：{ex.Message}";
        }
    }

    private void BrowseFolder()
    {
        var picked = UIService.PickFolder("选择备份存储目录");
        if (string.IsNullOrWhiteSpace(picked)) return;
        Folder = picked!;
        StatusMessage = "已选择新的备份目录，记得点「保存策略」";
    }

    private void OpenFolder()
    {
        try
        {
            var root = BackupManager.BackupRoot(GameRoot, CurrentPolicy());
            Directory.CreateDirectory(root);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = root,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            StatusMessage = $"打开目录失败：{ex.Message}";
        }
    }

    /// <summary>扫描可备份的来源：saves 下每个存档 + config + mods。</summary>
    private void LoadSources()
    {
        var list = new List<BackupSourceItem>();
        var root = GameRoot;

        var savesDir = Path.Combine(root, "saves");
        if (Directory.Exists(savesDir))
        {
            foreach (var dir in Directory.GetDirectories(savesDir).OrderBy(Path.GetFileName))
                list.Add(new BackupSourceItem
                {
                    Display = $"存档 · {Path.GetFileName(dir)}",
                    Path = dir,
                    Kind = BackupKind.Save
                });
        }

        foreach (var (name, kind) in new[] { ("config", BackupKind.Config), ("mods", BackupKind.Mods) })
        {
            var dir = Path.Combine(root, name);
            if (Directory.Exists(dir))
                list.Add(new BackupSourceItem { Display = $"目录 · {name}", Path = dir, Kind = kind });
        }

        var keep = SelectedSource?.Path;
        Sources = new ObservableCollection<BackupSourceItem>(list);
        SelectedSource = list.FirstOrDefault(s => s.Path == keep) ?? list.FirstOrDefault();
    }

    private Task RefreshAsync()
    {
        IsBusy = true;
        try
        {
            LoadSources();

            var policy = CurrentPolicy();
            var list = BackupManager.List(GameRoot, policy: policy);
            Records = new ObservableCollection<BackupRecord>(list);

            var total = list.Sum(r => r.SizeBytes);
            TotalText = $"共 {list.Count} 份备份，占用 {FormatSize(total)}｜目录：{BackupManager.BackupRoot(GameRoot, policy)}";
            StatusMessage = list.Count == 0 ? "还没有备份，选一个来源点「立即备份」" : "";
        }
        catch (Exception ex)
        {
            StatusMessage = $"读取备份索引失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
        return Task.CompletedTask;
    }

    private async Task CreateAsync()
    {
        if (SelectedSource is null) { StatusMessage = "请先选择备份来源"; return; }

        IsBusy = true;
        StatusMessage = $"正在打包 {SelectedSource.Display} …";
        try
        {
            var source = SelectedSource;
            var policy = CurrentPolicy();
            var note = string.IsNullOrWhiteSpace(Note) ? null : Note.Trim();

            var result = await Task.Run(() =>
                BackupManager.Create(GameRoot, source.Path, source.Kind, note, auto: false, policy: policy));

            if (result.Ok && result.Record is not null)
            {
                StatusMessage = $"备份完成：{Path.GetFileName(result.Record.ArchivePath)}（{result.Record.SizeText}）";
                ToastService.Show("备份完成", $"{source.Display} → {result.Record.SizeText}", ToastKind.Success);
                Note = "";

                // 数量限制：自动删旧
                var pruned = await Task.Run(() => BackupManager.Prune(GameRoot, policy));
                if (pruned > 0) StatusMessage += $"；按策略清理了 {pruned} 份旧备份";
            }
            else
            {
                StatusMessage = $"备份失败：{result.Error}";
                ToastService.Show("备份失败", result.Error ?? "未知错误", ToastKind.Error);
            }
        }
        finally
        {
            IsBusy = false;
            await RefreshAsync();
        }
    }

    private async Task RestoreAsync()
    {
        var rec = SelectedRecord;
        if (rec is null) { StatusMessage = "请先在列表中选择要恢复的备份"; return; }
        if (!rec.Exists) { StatusMessage = "该备份的 zip 文件已丢失，无法恢复"; return; }

        var target = ResolveRestoreTarget(rec);
        var policy = CurrentPolicy();

        var tip = policy.BackupBeforeRestore
            ? "恢复前会自动备份当前状态。"
            : "⚠ 当前未开启「恢复前自动备份」，目标目录会被直接覆盖。";

        if (!UIService.Confirm(
                $"将备份「{rec.SourceName}（{rec.CreatedAt:yyyy-MM-dd HH:mm}）」恢复到：\n{target}\n\n{tip}\n\n确定继续？",
                "确认恢复"))
            return;

        IsBusy = true;
        StatusMessage = "正在恢复 …";
        try
        {
            var (restore, safety) = await Task.Run(() =>
                BackupManager.RestoreSafely(GameRoot, rec, target, rec.Kind, policy));

            if (restore.Ok)
            {
                StatusMessage = safety is null
                    ? $"已恢复到 {target}"
                    : $"已恢复到 {target}（恢复前状态已另存为 {Path.GetFileName(safety.ArchivePath)}）";
                ToastService.Show("恢复完成", rec.SourceName, ToastKind.Success);
            }
            else
            {
                StatusMessage = $"恢复失败：{restore.Error}";
                ToastService.Show("恢复失败", restore.Error ?? "未知错误", ToastKind.Error);
            }
        }
        finally
        {
            IsBusy = false;
            await RefreshAsync();
        }
    }

    /// <summary>推断恢复目标目录：存档回 saves/&lt;名&gt;，其余回 gameRoot/&lt;名&gt;。</summary>
    private static string ResolveRestoreTarget(BackupRecord rec) => rec.Kind == BackupKind.Save
        ? Path.Combine(GameRoot, "saves", rec.SourceName)
        : Path.Combine(GameRoot, rec.SourceName);

    private async Task DeleteAsync()
    {
        var rec = SelectedRecord;
        if (rec is null) { StatusMessage = "请先选择要删除的备份"; return; }

        if (!UIService.Confirm(
                $"删除备份「{rec.SourceName}（{rec.CreatedAt:yyyy-MM-dd HH:mm}）」？\nzip 文件会一并删除，不可撤销。",
                "确认删除"))
            return;

        var policy = CurrentPolicy();
        var ok = await Task.Run(() => BackupManager.Delete(GameRoot, rec.Id, policy));
        StatusMessage = ok ? "已删除" : "删除失败（索引中找不到该备份）";
        await RefreshAsync();
    }

    private async Task PruneAsync()
    {
        var policy = CurrentPolicy();
        var n = await Task.Run(() => BackupManager.Prune(GameRoot, policy));
        StatusMessage = n > 0 ? $"按策略清理了 {n} 份旧备份" : "没有需要清理的备份";
        await RefreshAsync();
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        < 1024L * 1024 * 1024 => $"{bytes / 1024.0 / 1024:F1} MB",
        _ => $"{bytes / 1024.0 / 1024 / 1024:F2} GB"
    };
}
