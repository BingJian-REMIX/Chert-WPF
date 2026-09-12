using System.Text.RegularExpressions;
using Chert.Core.Models;

namespace Chert.Core.Launcher;

/// <summary>启动时的用户选项。</summary>
public class LaunchOptions
{
    public string Username { get; set; } = "Player";
    public string Uuid { get; set; } = "";
    public string AccessToken { get; set; } = "0";
    public string UserType { get; set; } = "mojang";
    public string UserProperties { get; set; } = "{}";
    public int MaxMemoryMb { get; set; } = 2048;
    public int MinMemoryMb { get; set; } = 512;
    public bool Demo { get; set; }
    public (int Width, int Height)? Resolution { get; set; }
    public List<string> ExtraJvmArgs { get; set; } = new();
    /// <summary>直接连入的服务器地址（host:port）。非空时追加 --server --port 到游戏参数。</summary>
    public string? ServerAddress { get; set; }
    /// <summary>强制指定的游戏工作目录（每版本覆盖）。为 null 时由 VersionIsolation 按隔离标记决定。</summary>
    public string? GameDir { get; set; }
    /// <summary>是否以全屏启动（注入 --fullscreen 游戏参数）。</summary>
    public bool Fullscreen { get; set; }
}

/// <summary>解析后的启动参数。</summary>
public class ResolvedArguments
{
    public string MainClass { get; set; } = "";
    public List<string> JvmArgs { get; set; } = new();
    public List<string> GameArgs { get; set; } = new();
}

/// <summary>
/// 参数解析：处理 arguments（多态 + 条件规则）或旧版 minecraftArguments，
/// 替换全部变量，注入 -Xmx/内存与 -Djava.library.path / -Dorg.lwjgl.librarypath。
/// </summary>
public static class ArgumentProcessor
{
    private static readonly Regex VarRegex = new(@"\$\{([^}]+)\}", RegexOptions.Compiled);

    /// <summary>解析版本参数。variables 必须包含 classpath、natives_directory 等所有 ${...} 占位。</summary>
    public static ResolvedArguments Process(VersionJson version,
        Dictionary<string, string> variables,
        LaunchOptions options,
        string nativesDir)
    {
        var osName = RuleEvaluator.CurrentOsName();
        var features = BuildFeatures(options);
        var result = new ResolvedArguments { MainClass = version.MainClass };

        if (version.Arguments?.Jvm is { Count: > 0 } jvm)
        {
            foreach (var item in jvm)
            {
                if (!RuleEvaluator.IsAllowed(item.Rules, osName, features)) continue;
                foreach (var v in item.Values)
                    result.JvmArgs.Add(Substitute(v, variables));
            }
        }

        if (version.Arguments?.Game is { Count: > 0 } game)
        {
            foreach (var item in game)
            {
                if (!RuleEvaluator.IsAllowed(item.Rules, osName, features)) continue;
                foreach (var v in item.Values)
                    result.GameArgs.Add(Substitute(v, variables));
            }
        }
        else if (!string.IsNullOrWhiteSpace(version.MinecraftArguments))
        {
            ParseLegacyArguments(version.MinecraftArguments, variables, result);
        }

        InjectMemoryAndPaths(result.JvmArgs, options, nativesDir);
        return result;
    }

    private static void ParseLegacyArguments(string minecraftArguments, Dictionary<string, string> variables, ResolvedArguments result)
    {
        var tokens = minecraftArguments
            .Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        var cpIdx = Array.IndexOf(tokens, "-cp");

        if (cpIdx >= 0 && cpIdx + 2 < tokens.Length)
        {
            for (var i = 0; i < cpIdx; i++)
                result.JvmArgs.Add(Substitute(tokens[i], variables));
            result.MainClass = Substitute(tokens[cpIdx + 2], variables);
            for (var i = cpIdx + 3; i < tokens.Length; i++)
                result.GameArgs.Add(Substitute(tokens[i], variables));
        }
        else
        {
            foreach (var t in tokens)
                result.GameArgs.Add(Substitute(t, variables));
        }
    }

    private static void InjectMemoryAndPaths(List<string> jvm, LaunchOptions options, string nativesDir)
    {
        // 库路径：确保指向 natives 目录
        var replacedLibPath = false;
        for (var i = 0; i < jvm.Count; i++)
        {
            if (jvm[i].StartsWith("-Djava.library.path=", StringComparison.OrdinalIgnoreCase))
            {
                jvm[i] = $"-Djava.library.path={nativesDir}";
                replacedLibPath = true;
            }
        }
        if (!replacedLibPath)
            jvm.Add($"-Djava.library.path={nativesDir}");

        if (!jvm.Any(a => a.Contains("org.lwjgl.librarypath")))
            jvm.Add($"-Dorg.lwjgl.librarypath={nativesDir}");

        // 字符编码：强制 UTF-8，避免中文模组名/路径在 JVM 参数里乱码（很多启动器漏掉此项）
        if (!jvm.Any(a => a.StartsWith("-Dfile.encoding=", StringComparison.OrdinalIgnoreCase)))
            jvm.Add("-Dfile.encoding=UTF-8");
        if (!jvm.Any(a => a.StartsWith("-Dsun.jnu.encoding=", StringComparison.OrdinalIgnoreCase)))
            jvm.Add("-Dsun.jnu.encoding=UTF-8");

        // 内存：确保 -Xmx 存在并采用用户设置
        var hasXmx = false;
        var hasXms = false;
        for (var i = 0; i < jvm.Count; i++)
        {
            if (jvm[i].StartsWith("-Xmx", StringComparison.OrdinalIgnoreCase))
            {
                jvm[i] = $"-Xmx{options.MaxMemoryMb}M";
                hasXmx = true;
            }
            else if (jvm[i].StartsWith("-Xms", StringComparison.OrdinalIgnoreCase))
            {
                hasXms = true;
            }
        }
        if (!hasXmx) jvm.Add($"-Xmx{options.MaxMemoryMb}M");
        if (!hasXms && options.MinMemoryMb > 0) jvm.Add($"-Xms{options.MinMemoryMb}M");
    }

    private static Dictionary<string, bool> BuildFeatures(LaunchOptions options) => new()
    {
        ["is_demo_user"] = options.Demo,
        ["has_custom_resolution"] = options.Resolution.HasValue
    };

    private static string Substitute(string token, Dictionary<string, string> variables)
        => VarRegex.Replace(token, m =>
            variables.TryGetValue(m.Groups[1].Value, out var v) ? v : "");
}
