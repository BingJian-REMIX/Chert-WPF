using System.Text.Json;
using System.Text.Json.Serialization;
using Chert.Core.Download;
using Chert.Core.Models;
using Chert.Core.Utils;

namespace Chert.Core.Recommend;

/// <summary>
/// 热门榜单：从 Modrinth 按下载量拉取（周榜/总榜共用接口，按下载量即总榜，
/// 调用方可用 limit 控制数量），本地缓存每小时刷新。按游戏版本 + 加载器过滤。
/// </summary>
public static class HotRanking
{
    private const string CacheDirName = "cache";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(1);

    /// <summary>
    /// 获取热门 Mod 列表（按下载量排序）。优先返回 1 小时内的本地缓存，否则联网拉取并写入缓存。
    /// </summary>
    public static async Task<List<RecommendationItem>> GetHotAsync(HttpClient client, string gameRoot,
        string? gameVersion, LoaderType loader, GameplayCategory? category = null,
        int limit = 30, CancellationToken ct = default)
    {
        var key = $"hot_{(gameVersion ?? "any")}_{loader}_{(category?.ToString() ?? "all")}";
        var cached = TryLoadCache(gameRoot, key);
        if (cached is not null) return cached;

        var mclient = new ModrinthClient(client);
        var hits = await mclient.SearchAsync("", gameVersion, loader, ModrinthProjectType.Mod,
            limit: limit, index: "downloads", ct: ct);

        var items = hits.Hits.Select(ToItem).Where(i => i is not null).Select(i => i!).ToList();
        SaveCache(gameRoot, key, items);
        return items;
    }

    private static RecommendationItem? ToItem(ModrinthHit hit)
    {
        if (string.IsNullOrEmpty(hit.ProjectId)) return null;
        var category = GameplayCategoryMap.FromModrinthCategories(hit.Categories) ?? GameplayCategory.Utility;
        return new RecommendationItem
        {
            ProjectId = hit.ProjectId,
            Slug = hit.Slug,
            Title = hit.Title,
            Description = hit.Description,
            IconUrl = hit.IconUrl,
            Downloads = hit.Downloads,
            Category = category,
            CategoryLabel = GameplayCategoryMap.DisplayName(category),
            Reason = "热门榜单推荐",
            Source = RecommendationSource.HotRanking,
            Type = ModrinthProjectType.Mod
        };
    }

    // ---- 本地缓存（JSON + 时间戳）----

    private class CachedPayload
    {
        public DateTime CachedAt { get; set; }
        public List<RecommendationItem> Items { get; set; } = new();
    }

    private static string CachePath(string gameRoot, string key) =>
        Path.Combine(gameRoot, CacheDirName, $"mclcs_{key}.json");

    private static List<RecommendationItem>? TryLoadCache(string gameRoot, string key)
    {
        try
        {
            var path = CachePath(gameRoot, key);
            if (!File.Exists(path)) return null;
            var json = File.ReadAllText(path);
            var payload = JsonSerializer.Deserialize<CachedPayload>(json);
            if (payload is null) return null;
            if (DateTime.UtcNow - payload.CachedAt > CacheTtl) return null;
            return payload.Items;
        }
        catch
        {
            return null;
        }
    }

    private static void SaveCache(string gameRoot, string key, List<RecommendationItem> items)
    {
        try
        {
            var dir = Path.Combine(gameRoot, CacheDirName);
            Directory.CreateDirectory(dir);
            var payload = new CachedPayload { CachedAt = DateTime.UtcNow, Items = items };
            File.WriteAllText(CachePath(gameRoot, key),
                JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = false }));
        }
        catch
        {
            // 缓存失败不影响主流程
        }
    }
}
