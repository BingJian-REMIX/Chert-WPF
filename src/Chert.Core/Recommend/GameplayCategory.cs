namespace Chert.Core.Recommend;

/// <summary>玩法分区（用于推荐分类与用户偏好）。</summary>
public enum GameplayCategory
{
    /// <summary>生电 / 科技自动化。</summary>
    Tech,

    /// <summary>建筑 / 装饰。</summary>
    Building,

    /// <summary>冒险 / 战斗 / 探索。</summary>
    Adventure,

    /// <summary>魔法。</summary>
    Magic,

    /// <summary>优化 / 性能。</summary>
    Optimization,

    /// <summary>辅助 / 生活质量（QoL）。</summary>
    Utility
}

/// <summary>智能推荐总开关。</summary>
public enum IntelliRecommendMode
{
    /// <summary>启用全部推荐（本地规则 + 热门榜单 + 场景推荐）。</summary>
    Enabled,

    /// <summary>仅本地规则（不联网拉取榜单）。</summary>
    LocalOnly,

    /// <summary>完全禁用推荐。</summary>
    Disabled
}

/// <summary>玩法分区与 Modrinth 分类标签、展示名的映射工具。</summary>
public static class GameplayCategoryMap
{
    /// <summary>各分区的本地化展示名。</summary>
    public static IReadOnlyDictionary<GameplayCategory, string> DisplayNames { get; } =
        new Dictionary<GameplayCategory, string>
        {
            [GameplayCategory.Tech] = "生电",
            [GameplayCategory.Building] = "建筑",
            [GameplayCategory.Adventure] = "冒险",
            [GameplayCategory.Magic] = "魔法",
            [GameplayCategory.Optimization] = "优化",
            [GameplayCategory.Utility] = "辅助"
        };

    /// <summary>所有分区（默认全选）。</summary>
    public static IReadOnlyList<GameplayCategory> All { get; } =
        new[] { GameplayCategory.Tech, GameplayCategory.Building, GameplayCategory.Adventure,
                GameplayCategory.Magic, GameplayCategory.Optimization, GameplayCategory.Utility };

    /// <summary>
    /// 将 Modrinth 的分类标签（如 "technology"、"magic"）推断为玩法分区；
    /// 无法识别时返回 null。
    /// </summary>
    public static GameplayCategory? FromModrinthCategories(IEnumerable<string> categories)
    {
        foreach (var c in categories)
        {
            var key = c.Trim().ToLowerInvariant();
            var mapped = key switch
            {
                "technology" or "tech" or "automation" or "storage" or "food" or "library" or "economy" => GameplayCategory.Tech,
                "decoration" or "building" or "structure" or "cursed" => GameplayCategory.Building,
                "adventure" or "combat" or "equipment" or "worldgen" or "social" => GameplayCategory.Adventure,
                "magic" => GameplayCategory.Magic,
                "optimization" or "performance" or "utility" or "game-mechanics" or "qol" => GameplayCategory.Optimization,
                "utility" or "social" or "misc" => GameplayCategory.Utility,
                _ => (GameplayCategory?)null
            };
            if (mapped.HasValue) return mapped.Value;
        }
        return null;
    }

    /// <summary>将分区转换为 Modrinth 查询用的分类标签（用于榜单过滤）。</summary>
    public static string ToModrinthCategory(GameplayCategory category) => category switch
    {
        GameplayCategory.Tech => "technology",
        GameplayCategory.Building => "decoration",
        GameplayCategory.Adventure => "adventure",
        GameplayCategory.Magic => "magic",
        GameplayCategory.Optimization => "optimization",
        GameplayCategory.Utility => "utility",
        _ => ""
    };

    public static string DisplayName(GameplayCategory category) =>
        DisplayNames.TryGetValue(category, out var n) ? n : category.ToString();
}
