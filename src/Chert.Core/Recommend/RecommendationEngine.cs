using Chert.Core.Download;
using Chert.Core.Models;
using Chert.Core.Mods;
using Chert.Core.Profiles;
using Chert.Core.Utils;

namespace Chert.Core.Recommend;

/// <summary>
/// 智能依赖与推荐引擎：综合本地规则、热门榜单、更新推荐，结合用户偏好（玩法分区、智能推荐开关）
/// 生成推荐卡片列表。所有推荐均可一键安装（UI 层调用 LauncherService.DownloadModAsync）。
/// </summary>
public static class RecommendationEngine
{
    /// <summary>
    /// 构建推荐列表。
    /// - Disabled：返回空。
    /// - LocalOnly：仅本地规则（依赖补全）。
    /// - Enabled：本地规则 + 热门榜单 + 更新推荐（联网）。
    /// 最终按用户玩法偏好（PreferredCategories）过滤。
    /// </summary>
    public static async Task<List<RecommendationItem>> BuildAsync(string gameRoot,
        LauncherProfile profile, HttpClient client, ILogger? logger = null,
        CancellationToken ct = default)
    {
        var items = new List<RecommendationItem>();
        if (profile.IntelliRecommend == IntelliRecommendMode.Disabled)
            return items;

        var modManager = new ModManager(gameRoot, client, null!);
        var installed = modManager.ListInstalledMods();

        // 1) 本地规则：依赖补全类（无网络）
        items.AddRange(RuleEngine.EvaluateLocalRules(gameRoot, installed));

        if (profile.IntelliRecommend == IntelliRecommendMode.Enabled)
        {
            var loader = DetectLoader(installed);
            var gameVersion = RuleEngine.ExtractGameVersion(profile.LastVersionId);

            try
            {
                // 2) 热门榜单
                var hot = await HotRanking.GetHotAsync(client, gameRoot, gameVersion, loader, null, limit: 40, ct: ct);
                var installedSlugs = installed.Select(m => m.ModId).ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var h in hot)
                    if (!installedSlugs.Contains(h.Slug))
                        items.Add(h);

                // 3) 更新推荐
                var updated = await modManager.CheckForUpdatesAsync(ct);
                foreach (var u in updated.Where(m => m.HasUpdate))
                {
                    items.Add(new RecommendationItem
                    {
                        Slug = u.ModId ?? Path.GetFileNameWithoutExtension(u.FileName),
                        Title = u.Name,
                        Description = $"已安装版本 {u.InstalledVersion}，最新 {u.LatestVersion}",
                        Category = GameplayCategory.Utility,
                        CategoryLabel = GameplayCategoryMap.DisplayName(GameplayCategory.Utility),
                        Reason = $"已安装 {u.Name} 有新版本 {u.LatestVersion}",
                        Source = RecommendationSource.Update,
                        RelatedInstalledMod = u.FileName,
                        Type = ModrinthProjectType.Mod
                    });
                }
            }
            catch (Exception ex)
            {
                logger?.Log($"推荐引擎联网获取失败（使用本地规则）：{ex.Message}");
            }
        }

        // 按玩法偏好过滤
        items = items.Where(i => profile.PreferredCategories.Contains(i.Category)).ToList();
        return items;
    }

    /// <summary>根据已装 Mod 的加载器推断当前加载器类型（用于榜单过滤）。</summary>
    private static LoaderType DetectLoader(List<ModEntry> installed)
    {
        var loaders = installed.Select(m => m.Loader).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (loaders.Contains("fabric")) return LoaderType.Fabric;
        if (loaders.Contains("forge")) return LoaderType.Forge;
        if (loaders.Contains("neoforge")) return LoaderType.NeoForge;
        if (loaders.Contains("quilt")) return LoaderType.Quilt;
        return LoaderType.Any;
    }
}
