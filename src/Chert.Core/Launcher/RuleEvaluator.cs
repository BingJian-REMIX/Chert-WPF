using System.Runtime.InteropServices;
using Chert.Core.Models;

namespace Chert.Core.Launcher;

/// <summary>条件规则求值（os / features）。被 ClasspathBuilder 与 ArgumentProcessor 共用。</summary>
public static class RuleEvaluator
{
    /// <summary>当前操作系统名，对应规则中的 windows / linux / osx。</summary>
    public static string CurrentOsName()
        => OperatingSystem.IsWindows() ? "windows"
         : OperatingSystem.IsMacOS() ? "osx"
         : "linux";

    /// <summary>
    /// 判断一组规则是否允许该条目。
    /// 无规则 -> 允许；有规则 -> 任一 allow 规则匹配且未被 disallow 规则否决则允许。
    /// </summary>
    public static bool IsAllowed(List<Rule>? rules, string osName, IDictionary<string, bool>? features = null)
    {
        if (rules is null || rules.Count == 0) return true;
        var result = false;
        foreach (var rule in rules)
            if (RuleApplies(rule, osName, features))
                result = rule.Action == "allow";
        return result;
    }

    public static bool RuleApplies(Rule rule, string osName, IDictionary<string, bool>? features)
    {
        if (rule.Os is not null)
        {
            if (!string.IsNullOrEmpty(rule.Os.Name)
                && !rule.Os.Name.Equals(osName, StringComparison.OrdinalIgnoreCase))
                return false;
            if (!string.IsNullOrEmpty(rule.Os.Arch) && !ArchMatches(rule.Os.Arch))
                return false;
            if (!string.IsNullOrEmpty(rule.Os.Version)
                && !System.Text.RegularExpressions.Regex.IsMatch(GetOsVersion(), rule.Os.Version))
                return false;
        }

        if (rule.Features is not null)
        {
            foreach (var kv in rule.Features)
            {
                var has = features is not null && features.TryGetValue(kv.Key, out var v) && v;
                if (has != kv.Value) return false;
            }
        }

        return true;
    }

    private static bool ArchMatches(string required)
    {
        var arch = RuntimeInformation.OSArchitecture;
        return required.ToLowerInvariant() switch
        {
            "x86" => arch == Architecture.X86,
            "x64" or "amd64" => arch == Architecture.X64,
            "arm" => arch == Architecture.Arm,
            "arm64" => arch == Architecture.Arm64,
            _ => false
        };
    }

    private static string GetOsVersion()
    {
        try { return Environment.OSVersion.VersionString; }
        catch { return ""; }
    }
}
