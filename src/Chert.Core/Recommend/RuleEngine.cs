using System.Text.RegularExpressions;
using Chert.Core.Models;
using Chert.Core.Mods;
using Chert.Core.Utils;

namespace Chert.Core.Recommend;

/// <summary>
/// 本地规则引擎（纯本地、无网络）：
/// · 必装推荐：Fabric 已装但无 Fabric API；存在光影包但无 Iris 等光影核心。
/// · 场景推荐 / 更新推荐需要联网，见 <see cref="RecommendationEngine"/>。
/// </summary>
public static class RuleEngine
{
    /// <summary>已知「必装前置」项目（slug + projectId + 展示信息），用于本地规则，无需联网查询。</summary>
    private static readonly Dictionary<string, (string Slug, string ProjectId, string Title, string Reason)> KnownMustHave = new()
    {
        ["fabric-api"] = ("fabric-api", "P7dR8mSH", "Fabric API", "检测到 Fabric 加载器，建议安装 Fabric API 作为前置"),
        ["iris"] = ("iris", "YL57xq9U", "Iris", "检测到光影包，建议安装 Iris 光影核心以加载光影")
    };

    /// <summary>纯本地规则：根据已装 Mod 与光影包推断「依赖补全」类推荐。</summary>
    public static List<RecommendationItem> EvaluateLocalRules(string gameRoot, List<ModEntry> installed)
    {
        var result = new List<RecommendationItem>();
        var loaders = installed.Select(m => m.Loader).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var slugs = installed.Select(m => m.ModId).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // 必装：Fabric 已装但无 Fabric API
        if (loaders.Contains("fabric") && !slugs.Contains("fabric-api"))
            result.Add(MakeKnown("fabric-api", GameplayCategory.Tech, true));

        // 必装：存在光影包但无光影核心（Iris）
        if (HasShaderpacks(gameRoot) && !slugs.Contains("iris"))
            result.Add(MakeKnown("iris", GameplayCategory.Optimization, true));

        return result;
    }

    private static RecommendationItem MakeKnown(string key, GameplayCategory category, bool depCompletion)
    {
        var info = KnownMustHave[key];
        return new RecommendationItem
        {
            ProjectId = info.ProjectId,
            Slug = info.Slug,
            Title = info.Title,
            Description = info.Reason,
            Category = category,
            CategoryLabel = GameplayCategoryMap.DisplayName(category),
            Reason = info.Reason,
            IsDependencyCompletion = depCompletion,
            Source = RecommendationSource.LocalRule,
            Type = ModrinthProjectType.Mod
        };
    }

    private static bool HasShaderpacks(string gameRoot)
    {
        try
        {
            var dir = PathEx.ShaderPacksDir(gameRoot);
            return Directory.Exists(dir) && Directory.EnumerateFiles(dir, "*.zip").Any();
        }
        catch
        {
            return false;
        }
    }

    /// <summary>从版本 id（如 fabric-1.20.1）提取 Minecraft 游戏版本号。</summary>
    public static string? ExtractGameVersion(string? versionId)
    {
        if (string.IsNullOrEmpty(versionId)) return null;
        var m = Regex.Match(versionId, @"\d+\.\d+(?:\.\d+)?");
        return m.Success ? m.Value : null;
    }
}
