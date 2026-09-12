using System.Text.RegularExpressions;

namespace Chert.Core.Launcher;

/// <summary>崩溃类别，用于驱动修复策略。</summary>
public enum CrashCategory
{
    /// <summary>未知 / 未识别。</summary>
    Unknown,

    /// <summary>内存不足（OutOfMemoryError）。</summary>
    OutOfMemory,

    /// <summary>Java 版本不匹配（UnsupportedClassVersionError）。</summary>
    JavaVersion,

    /// <summary>缺少依赖库或模组（ClassNotFoundException）。</summary>
    MissingLibrary,

    /// <summary>链接/兼容性问题（NoClassDefFoundError、NoSuchMethodError 等）。</summary>
    LinkageError,

    /// <summary>Fabric 模组冲突（ModResolutionException）。</summary>
    ModConflict,

    /// <summary>OpenGL / 显卡相关错误。</summary>
    OpenGL,

    /// <summary>资源包 / 光影（着色器）引起的加载崩溃。</summary>
    ResourcePackOrShader
}

/// <summary>崩溃分析结果。</summary>
public class CrashAnalysis
{
    public string ExceptionType { get; set; } = "未知错误";
    public string Summary { get; set; } = "";
    public List<string> Causes { get; set; } = new();
    public List<string> Suggestions { get; set; } = new();

    /// <summary>崩溃类别，用于驱动修复策略。</summary>
    public CrashCategory Category { get; set; } = CrashCategory.Unknown;

    /// <summary>原始崩溃报告文本（用于详情展示）。</summary>
    public string RawReport { get; set; } = "";

    /// <summary>由 UnsupportedClassVersionError 推算出的所需 Java 主版本号（未知为 0）。</summary>
    public int RequiredJavaMajor { get; set; }

    /// <summary>从类文件主版本号推算所需 Java 版本（major = version - 44）。</summary>
    public static int ClassVersionToJava(int classVersion) => classVersion - 44;
}

/// <summary>
/// 解析 Minecraft 崩溃报告，识别关键异常并给出可能原因与修复建议。
/// </summary>
public static class CrashAnalyzer
{
    private static readonly List<CrashRule> Rules = new()
    {
        new("OutOfMemoryError", CrashCategory.OutOfMemory, "内存不足（OutOfMemoryError）",
            new[] { "分配的内存不足以运行游戏或当前模组。" },
            new[] { "在设置中调大 -Xmx（如 4096M）。", "关闭占用内存过大的模组/光影。", "减少同时加载的存档或资源包。" }),

        new("UnsupportedClassVersionError", CrashCategory.JavaVersion, "Java 版本不匹配（UnsupportedClassVersionError）",
            new[] { "游戏/模组需要更高版本的 Java。" },
            new[] { "将启动使用的 Java 升级到版本要求以上（见下方推算）。", "在设置中指定正确的 Java 路径。" },
            extractClassVersion: true),

        new("ClassNotFoundException", CrashCategory.MissingLibrary, "类未找到（ClassNotFoundException）",
            new[] { "缺少依赖库或模组文件损坏/缺失。" },
            new[] { "检查日志中缺失的类名对应模组。", "重新安装对应模组及其依赖（如 Fabric API）。", "验证 libraries 是否下载完整。" }),

        new("NoClassDefFoundError", CrashCategory.LinkageError, "类定义未找到（NoClassDefFoundError）",
            new[] { "运行时缺失某个类，通常是依赖未加载或版本冲突。" },
            new[] { "确认相关库/模组已安装。", "排查模组冲突（二分法禁用）。" }),

        new("NoSuchMethodError", CrashCategory.LinkageError, "方法不存在（NoSuchMethodError）",
            new[] { "模组与游戏/其他模组版本不兼容。" },
            new[] { "更新或回退冲突的模组到与游戏版本匹配。", "检查是否混用了不同加载器（Fabric/Forge）。" }),

        new("ModResolutionException", CrashCategory.ModConflict, "Fabric 模组冲突（ModResolutionException）",
            new[] { "Fabric 模组依赖不满足或相互冲突。" },
            new[] { "根据日志提示安装缺失的依赖模组。", "移除/更新冲突模组。" }),

        new("resourcepack|ResourcePack|ShaderPack|Shader|net\\.coderbot\\.iris|net\\.irisshaders|Oculus|optifine|Rubidium|Embeddium|shaderpack",
            CrashCategory.ResourcePackOrShader, "资源包 / 光影相关崩溃",
            new[] { "资源包或光影（着色器）与当前游戏版本 / 显卡环境不兼容，导致加载时崩溃。" },
            new[] { "将资源包回滚到默认（vanilla）并临时关闭光影后重试。", "确认资源包 / 光影与游戏版本匹配。", "如仍崩溃，检查显卡驱动或移除问题资源包。" }),

        new("OpenGL", CrashCategory.OpenGL, "OpenGL / 显卡相关错误",
            new[] { "显卡驱动过旧、不支持所需 OpenGL 版本，或 OptiFine/光影不兼容。" },
            new[] { "更新显卡驱动到最新版。", "暂时关闭光影或 OptiFine。", "尝试切换渲染后端（如 VulkanMod/Sodium）。" }),

        new("Could not create context|GL_INVALID|GLException|Failed to create OpenGL", CrashCategory.OpenGL, "无法创建 OpenGL 上下文",
            new[] { "显卡驱动问题或显存/上下文创建失败。" },
            new[] { "更新显卡驱动。", "降低分辨率/关闭全屏后重试。", "关闭占用 GPU 的其他程序。" })
    };

    public static CrashAnalysis Analyze(string reportText)
    {
        var analysis = new CrashAnalysis();
        foreach (var rule in Rules)
        {
            if (Regex.IsMatch(reportText, rule.Pattern, RegexOptions.IgnoreCase))
            {
                analysis.ExceptionType = rule.Title;
                analysis.Summary = rule.Title;
                analysis.Category = rule.Category;
                analysis.Causes.AddRange(rule.Causes);
                analysis.Suggestions.AddRange(rule.Suggestions);

                if (rule.ExtractClassVersion)
                {
                    var m = Regex.Match(reportText, @"class file version (\d+)\.(\d+)");
                    if (m.Success && int.TryParse(m.Groups[1].Value, out var cv))
                    {
                        var java = CrashAnalysis.ClassVersionToJava(cv);
                        analysis.RequiredJavaMajor = java;
                        analysis.Summary += $"（需要 Java {java} 或以上）";
                        analysis.Suggestions.Insert(0, $"当前报错要求 Java {java}+，请升级启动用 Java。");
                    }
                }
                return analysis;
            }
        }

        analysis.Causes.Add("未能自动识别具体异常类型。");
        analysis.Suggestions.Add("查看完整崩溃报告中的堆栈信息以定位问题。");
        analysis.Suggestions.Add("尝试更新显卡驱动、检查模组冲突或重新安装游戏版本。");
        return analysis;
    }
}

internal class CrashRule
{
    public string Pattern { get; }
    public CrashCategory Category { get; }
    public string Title { get; }
    public string[] Causes { get; }
    public string[] Suggestions { get; }
    public bool ExtractClassVersion { get; }

    public CrashRule(string pattern, CrashCategory category, string title, string[] causes, string[] suggestions, bool extractClassVersion = false)
    {
        Pattern = pattern;
        Category = category;
        Title = title;
        Causes = causes;
        Suggestions = suggestions;
        ExtractClassVersion = extractClassVersion;
    }
}
