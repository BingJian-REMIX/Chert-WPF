using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using Chert.Core.Launcher;
using Chert.Core.Localization;
using Chert.Core.Mvvm;
using Chert.Core.Save;
using Chert.Core.Toolbox;
using Chert.App.Services;

namespace Chert.App.ViewModels;

/// <summary>冲突 Mod 的可选项（用于"保留哪一个"）。</summary>
public class ModConflictChoice : ObservableObject
{
    public string FilePath { get; set; } = "";
    public string Name { get; set; } = "";

    private bool _isKeepSelected;
    public bool IsKeepSelected
    {
        get => _isKeepSelected;
        set => SetField(ref _isKeepSelected, value);
    }
}

/// <summary>
/// 崩溃分析报告视图模型：展示完整错误信息，并在可自动修复时提供"尝试自动修复"按钮。
/// 修复采用循环策略：每次点击执行一次修复并重新启动游戏，直到启动成功或问题无法继续自动修复。
/// </summary>
public class CrashReportViewModel : ObservableObject
{
    private LaunchResult _result;
    private readonly Func<LaunchResult, Task<LaunchResult?>> _relaunch;
    private readonly bool _allowRepair;

    private string _exceptionType = "";
    private string _summary = "";
    private ObservableCollection<string> _causes = new();
    private ObservableCollection<string> _suggestions = new();
    private string _rawReport = "";
    private string _categoryText = "";
    private bool _canRepair;
    private string _repairTitle = "";
    private string _repairDescription = "";
    private ObservableCollection<string> _repairSteps = new();
    private bool _isRepairing;
    private string _statusMessage = "";
    private bool _hasReport;
    private bool _isConflictPlan;
    private bool _isMissingDepPlan;
    private ObservableCollection<ModConflictChoice> _conflictingMods = new();
    private ObservableCollection<string> _missingDependencies = new();

    // ===== P11：聊天记录提取 / 导出 =====

    private ObservableCollection<ChatEntry> _chatEntries = new();
    private bool _hasChat;
    private string _chatStatus = "";

    /// <summary>从崩溃报告里提取到的聊天记录（绿色染色展示）。</summary>
    public ObservableCollection<ChatEntry> ChatEntries
    {
        get => _chatEntries;
        set => SetField(ref _chatEntries, value);
    }

    /// <summary>是否提取到聊天记录（决定页签是否有内容 / 是否显示空态提示）。</summary>
    public bool HasChat
    {
        get => _hasChat;
        set => SetField(ref _hasChat, value);
    }

    /// <summary>聊天区状态文案（未找到日志 / 无聊天 / 成功提取 N 条）。</summary>
    public string ChatStatus
    {
        get => _chatStatus;
        set => SetField(ref _chatStatus, value);
    }

    /// <summary>导出聊天记录（txt / md）。</summary>
    public ICommand ExportChatCommand { get; }

    public string ExceptionType
    {
        get => _exceptionType;
        set => SetField(ref _exceptionType, value);
    }

    public string Summary
    {
        get => _summary;
        set => SetField(ref _summary, value);
    }

    public ObservableCollection<string> Causes
    {
        get => _causes;
        set => SetField(ref _causes, value);
    }

    public ObservableCollection<string> Suggestions
    {
        get => _suggestions;
        set => SetField(ref _suggestions, value);
    }

    public string RawReport
    {
        get => _rawReport;
        set => SetField(ref _rawReport, value);
    }

    public string CategoryText
    {
        get => _categoryText;
        set => SetField(ref _categoryText, value);
    }

    /// <summary>当前崩溃是否可自动修复。</summary>
    public bool CanRepair
    {
        get => _canRepair;
        set => SetField(ref _canRepair, value);
    }

    public string RepairTitle
    {
        get => _repairTitle;
        set => SetField(ref _repairTitle, value);
    }

    public string RepairDescription
    {
        get => _repairDescription;
        set => SetField(ref _repairDescription, value);
    }

    public ObservableCollection<string> RepairSteps
    {
        get => _repairSteps;
        set => SetField(ref _repairSteps, value);
    }

    public bool IsRepairing
    {
        get => _isRepairing;
        set => SetField(ref _isRepairing, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetField(ref _statusMessage, value);
    }

    public bool HasReport
    {
        get => _hasReport;
        set => SetField(ref _hasReport, value);
    }

    /// <summary>是否为"禁用冲突 Mod"方案（需用户选择保留哪一个）。</summary>
    public bool IsConflictPlan
    {
        get => _isConflictPlan;
        set => SetField(ref _isConflictPlan, value);
    }

    /// <summary>是否为"安装缺失前置"方案。</summary>
    public bool IsMissingDepPlan
    {
        get => _isMissingDepPlan;
        set => SetField(ref _isMissingDepPlan, value);
    }

    /// <summary>冲突 Mod 选项列表。</summary>
    public ObservableCollection<ModConflictChoice> ConflictingMods
    {
        get => _conflictingMods;
        set => SetField(ref _conflictingMods, value);
    }

    /// <summary>缺失的前置 Mod ID 列表。</summary>
    public ObservableCollection<string> MissingDependencies
    {
        get => _missingDependencies;
        set => SetField(ref _missingDependencies, value);
    }

    /// <summary>§四.2 降级联动：是否存在适用的降级恢复方案。</summary>
    public bool HasDowngradeRecovery => _result.RepairPlan?.DowngradeRecovery is { Applicable: true };

    /// <summary>§四.2 降级联动：判定依据说明。</summary>
    public string DowngradeRecoveryReason => _result.RepairPlan?.DowngradeRecovery?.Reason ?? "";

    /// <summary>始终展示的非破坏性提示。</summary>
    public string NonDestructiveNote => LocaleManager.T("crash.non_destructive");

    public ICommand TryRepairCommand { get; }

    /// <summary>§四.2 降级联动：按所选动作（RevertBackup / RetryOther / InstallOriginal）执行恢复。</summary>
    public ICommand DowngradeRecoveryCommand { get; }

    /// <summary>修复成功并启动游戏后触发，宿主应关闭窗口。</summary>
    public event Action? Repaired;

    /// <summary>修复后再次崩溃时触发，携带最新结果。</summary>
    public event Action<LaunchResult>? ReCrashed;

    public CrashReportViewModel(LaunchResult result, Func<LaunchResult, Task<LaunchResult?>> relaunch, bool allowRepair = true)
    {
        _result = result;
        _relaunch = relaunch;
        _allowRepair = allowRepair;
        TryRepairCommand = new AsyncRelayCommand(_ => TryRepairAsync(null), _ => CanRepair && !IsRepairing);
        DowngradeRecoveryCommand = new AsyncRelayCommand(p => TryRepairAsync(p),
            _ => HasDowngradeRecovery && !IsRepairing);
        ExportChatCommand = new RelayCommand(p => ExportChat(p as string ?? "txt"), _ => HasChat);
        ApplyResult(result);
    }

    private void ApplyResult(LaunchResult result)
    {
        var analysis = result.Analysis;
        if (analysis is null)
        {
            ExceptionType = "未知错误";
            Summary = "未能解析崩溃报告。";
            Causes = new ObservableCollection<string> { "未附带分析结果。" };
            Suggestions = new ObservableCollection<string> { "查看下方原始崩溃报告以定位问题。" };
            CategoryText = "";
        }
        else
        {
            ExceptionType = analysis.ExceptionType;
            Summary = analysis.Summary;
            Causes = new ObservableCollection<string>(analysis.Causes);
            Suggestions = new ObservableCollection<string>(analysis.Suggestions);
            CategoryText = CategoryLabel(analysis.Category);
        }

        RawReport = result.Analysis?.RawReport ?? "";
        HasReport = !string.IsNullOrEmpty(RawReport);

        var plan = result.RepairPlan;
        CanRepair = plan is { CanRepair: true } && _allowRepair;
        RepairTitle = plan?.Title ?? "";
        RepairDescription = plan?.Description ?? "";
        RepairSteps = plan is null
            ? new ObservableCollection<string>()
            : new ObservableCollection<string>(plan.Steps);

        IsConflictPlan = plan is { Strategy: RepairStrategy.DisableConflictingMods };
        IsMissingDepPlan = plan is { Strategy: RepairStrategy.InstallMissingModDependency };
        ConflictingMods = new ObservableCollection<ModConflictChoice>();
        MissingDependencies = new ObservableCollection<string>();
        if (IsConflictPlan && plan is not null)
        {
            foreach (var m in plan.ConflictingMods)
            {
                var keep = string.Equals(m.FilePath, plan.KeepModFile, StringComparison.OrdinalIgnoreCase);
                ConflictingMods.Add(new ModConflictChoice
                {
                    FilePath = m.FilePath,
                    Name = m.Name,
                    IsKeepSelected = keep
                });
            }
        }
        if (IsMissingDepPlan && plan is not null)
        {
            foreach (var d in plan.MissingModDependencies)
                MissingDependencies.Add(d);
        }

        StatusMessage = CanRepair
            ? LocaleManager.T("crash.repairable")
            : LocaleManager.T("crash.not_repairable");

        ExtractChat();
    }

    /// <summary>
    /// P11：从本次会话的游戏日志中提取聊天记录。
    /// 崩溃报告本身不含聊天，日志在 &lt;游戏目录&gt;/logs/latest.log（崩溃前那份通常是
    /// latest-*.log.gz，按修改时间取最近的一个即可）。
    /// </summary>
    private void ExtractChat()
    {
        try
        {
            var root = LauncherService.Instance.GameRoot;
            var logsDir = LogManager.LogsDir(root);
            if (!Directory.Exists(logsDir))
            {
                ChatEntries = new ObservableCollection<ChatEntry>();
                HasChat = false;
                ChatStatus = "未找到游戏日志目录，无法提取聊天记录。";
                return;
            }

            // 取最近写入的 .log（优先 latest.log，其次按时间倒序的压缩包）
            var candidates = Directory.GetFiles(logsDir, "*.log")
                .Concat(Directory.GetFiles(logsDir, "*.log.gz"))
                .Select(p => new FileInfo(p))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .ToList();
            if (candidates.Count == 0)
            {
                ChatEntries = new ObservableCollection<ChatEntry>();
                HasChat = false;
                ChatStatus = "日志目录为空，无法提取聊天记录。";
                return;
            }

            var text = LogManager.ReadLog(candidates[0].FullName);
            var list = ChatExtractor.Extract(text);
            ChatEntries = new ObservableCollection<ChatEntry>(list);
            HasChat = list.Count > 0;
            ChatStatus = list.Count > 0
                ? $"已从 {candidates[0].Name} 提取 {list.Count} 条聊天记录。"
                : $"未在 {candidates[0].Name} 中找到聊天记录。";
        }
        catch (Exception ex)
        {
            ChatEntries = new ObservableCollection<ChatEntry>();
            HasChat = false;
            ChatStatus = "提取聊天记录失败：" + ex.Message;
        }
    }

    /// <summary>把聊天记录导出为 txt / md。</summary>
    private void ExportChat(string format)
    {
        if (!HasChat || ChatEntries.Count == 0) return;
        try
        {
            var isMd = format.Equals("md", StringComparison.OrdinalIgnoreCase);
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                $"minecraft-chat-{stamp}.{(isMd ? "md" : "txt")}");

            File.WriteAllText(path, isMd
                ? ChatExtractor.ToMarkdown(ChatEntries)
                : ChatExtractor.ToPlainText(ChatEntries));

            ToastService.Show("聊天记录已导出", path);
        }
        catch (Exception ex)
        {
            ToastService.Show("导出失败", ex.Message, ToastKind.Error);
        }
    }

    private static string CategoryLabel(CrashCategory category) => category switch
    {
        CrashCategory.OutOfMemory => LocaleManager.T("lbl.memory"),
        CrashCategory.JavaVersion => LocaleManager.T("lbl.java_path"),
        CrashCategory.MissingLibrary => "依赖库",
        CrashCategory.LinkageError => "兼容性",
        CrashCategory.ModConflict => "Mod 冲突",
        CrashCategory.OpenGL => "OpenGL",
        CrashCategory.ResourcePackOrShader => "资源包/光影",
        _ => "未知"
    };

    private async Task TryRepairAsync(object? param = null)
    {
        if (param is string action && _result.RepairPlan is not null)
        {
            // §四.2 降级联动：按用户所选动作切换策略
            _result.RepairPlan.Strategy = action switch
            {
                "RetryOther" => RepairStrategy.RetryDowngradeOtherMethod,
                "InstallOriginal" => RepairStrategy.InstallOriginalVersion,
                _ => RepairStrategy.RevertDowngradeBackup
            };
        }

        if (!CanRepair || IsRepairing) return;

        // 冲突 Mod：先确定用户要保留的那一个
        if (_result.RepairPlan is { Strategy: RepairStrategy.DisableConflictingMods })
        {
            var keep = ConflictingMods.FirstOrDefault(c => c.IsKeepSelected)?.FilePath
                       ?? _result.RepairPlan.KeepModFile;
            _result.RepairPlan.KeepModFile = keep;
        }

        IsRepairing = true;
        StatusMessage = LocaleManager.T("crash.repairing");

        try
        {
            var newResult = await _relaunch(_result);
            if (newResult is null)
            {
                // 修复成功，游戏已正常启动
                IsRepairing = false;
                StatusMessage = LocaleManager.T("crash.repaired_success");
                Repaired?.Invoke();
                return;
            }

            // 再次崩溃：更新展示内容并继续循环
            _result = newResult;
            ApplyResult(newResult);
            IsRepairing = false;
            StatusMessage = newResult.RepairPlan is { CanRepair: true }
                ? LocaleManager.T("crash.repaired_recrash")
                : LocaleManager.T("crash.repair_unrepairable");
            ReCrashed?.Invoke(newResult);
        }
        catch (Exception ex)
        {
            IsRepairing = false;
            StatusMessage = LocaleManager.Tf("crash.repair_failed", ex.Message);
        }
    }
}
