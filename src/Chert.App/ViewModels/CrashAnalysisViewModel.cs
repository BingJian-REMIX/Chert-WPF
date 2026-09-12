using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using Chert.Core.Launcher;
using Chert.Core.Localization;
using Chert.Core.Mvvm;
using Chert.Core.Profiles;
using Chert.App.Services;

namespace Chert.App.ViewModels;

/// <summary>崩溃分析报告条目（崩溃分析标签页用）。</summary>
public class CrashReportEntry
{
    public string Path { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public DateTime TimeUtc { get; init; }
}

/// <summary>
/// 崩溃分析标签页：列出最近的崩溃报告，打开完整分析报告（含自动修复）。
/// </summary>
public class CrashAnalysisViewModel : ObservableObject
{
    private ObservableCollection<CrashReportEntry> _reports = new();
    private CrashReportEntry? _selectedReport;
    private string _summary = "";
    private string _statusMessage = "";

    public ObservableCollection<CrashReportEntry> Reports
    {
        get => _reports;
        set => SetField(ref _reports, value);
    }

    public CrashReportEntry? SelectedReport
    {
        get => _selectedReport;
        set
        {
            if (SetField(ref _selectedReport, value))
                UpdateSummary();
        }
    }

    public string Summary
    {
        get => _summary;
        set => SetField(ref _summary, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetField(ref _statusMessage, value);
    }

    public ICommand RefreshCommand { get; }
    public ICommand OpenDetailCommand { get; }

    public CrashAnalysisViewModel()
    {
        RefreshCommand = new RelayCommand(_ => Refresh());
        OpenDetailCommand = new RelayCommand(_ => OpenDetail(), _ => SelectedReport is not null);
        Refresh();
    }

    public void Refresh()
    {
        var gameRoot = LauncherService.Instance.GameRoot;
        var list = CrashDetector.FindAllCrashReports(gameRoot)
            .Select(p => new CrashReportEntry
            {
                Path = p,
                DisplayName = $"{Path.GetFileName(p)}  ({File.GetLastWriteTimeUtc(p):yyyy-MM-dd HH:mm})",
                TimeUtc = File.GetLastWriteTimeUtc(p)
            })
            .ToList();
        Reports = new ObservableCollection<CrashReportEntry>(list);

        StatusMessage = list.Count > 0
            ? LocaleManager.Tf("lbl.crash_analysis") + $"：{list.Count} 份"
            : LocaleManager.T("lbl.no_crash");
        SelectedReport = list.FirstOrDefault();
        UpdateSummary();
    }

    private void UpdateSummary()
    {
        if (SelectedReport is null)
        {
            Summary = LocaleManager.T("lbl.no_crash");
            return;
        }
        try
        {
            var text = File.ReadAllText(SelectedReport.Path);
            var analysis = CrashAnalyzer.Analyze(text);
            Summary = $"【{analysis.ExceptionType}】\n{analysis.Summary}\n\n建议：\n"
                      + string.Join("\n", analysis.Suggestions.Select(s => "• " + s));
        }
        catch (Exception ex)
        {
            Summary = $"无法读取报告：{ex.Message}";
        }
    }

    private void OpenDetail()
    {
        if (SelectedReport is null) return;

        var gameRoot = LauncherService.Instance.GameRoot;
        var profile = ProfileStore.Load(gameRoot);

        var text = File.ReadAllText(SelectedReport.Path);
        var analysis = CrashAnalyzer.Analyze(text);
        analysis.RawReport = text;
        var plan = CrashRepairEngine.BuildPlan(analysis, profile, null, gameRoot, profile.LastVersionId);
        var result = new LaunchResult
        {
            CrashReportPath = SelectedReport.Path,
            Analysis = analysis,
            RepairPlan = plan
        };

        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            var win = new Chert.App.Views.CrashReportView(result, Relaunch);
            win.Owner = System.Windows.Application.Current.MainWindow;
            win.Show();
        });
    }

    private async Task<LaunchResult?> Relaunch(LaunchResult current)
    {
        if (current.RepairPlan is null) return current;
        await LauncherService.Instance.ApplyRepairAsync(current.RepairPlan);
        var profile = ProfileStore.Load(LauncherService.Instance.GameRoot);
        if (string.IsNullOrEmpty(profile.LastVersionId)) return current;
        var res = await LauncherService.Instance.LaunchAsync(profile.LastVersionId);
        return res.CrashReportPath is null ? null : res;
    }
}
