using Chert.Core.Utils;

namespace Chert.Core.Launcher;

/// <summary>崩溃报告检测：扫描 crash-reports 目录。</summary>
public static class CrashDetector
{
    /// <summary>返回最新的崩溃报告文件（crash-*.txt），无则 null。</summary>
    public static string? FindLatestCrashReport(string gameRoot)
    {
        var dir = PathEx.CrashReportsDir(gameRoot);
        if (!Directory.Exists(dir)) return null;

        return Directory.EnumerateFiles(dir, "crash-*.txt", SearchOption.TopDirectoryOnly)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    /// <summary>返回所有崩溃报告（按时间倒序）。</summary>
    public static List<string> FindAllCrashReports(string gameRoot)
    {
        var dir = PathEx.CrashReportsDir(gameRoot);
        if (!Directory.Exists(dir)) return new List<string>();
        return Directory.EnumerateFiles(dir, "crash-*.txt", SearchOption.TopDirectoryOnly)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ToList();
    }
}
