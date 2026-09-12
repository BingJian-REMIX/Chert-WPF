using System.Text.Json.Serialization;
using Chert.Core.Models;

namespace Chert.Core.Recommend;

/// <summary>单个推荐条目（用于 UI 卡片流）。</summary>
public class RecommendationItem
{
    /// <summary>Modrinth 项目 ID。</summary>
    public string ProjectId { get; set; } = "";

    /// <summary>项目 slug（URL 友好名）。</summary>
    public string Slug { get; set; } = "";

    /// <summary>展示名。</summary>
    public string Title { get; set; } = "";

    /// <summary>简介。</summary>
    public string Description { get; set; } = "";

    /// <summary>图标 URL（可空，UI 用占位图）。</summary>
    public string IconUrl { get; set; } = "";

    /// <summary>下载量。</summary>
    public int Downloads { get; set; }

    /// <summary>资源类型（mod / shader / resourcepack）。</summary>
    public ModrinthProjectType Type { get; set; } = ModrinthProjectType.Mod;

    /// <summary>玩法分区（用于标签与偏好过滤）。</summary>
    public GameplayCategory Category { get; set; } = GameplayCategory.Utility;

    /// <summary>分区展示名（缓存，便于 UI 直接显示）。</summary>
    public string CategoryLabel { get; set; } = "";

    /// <summary>推荐理由（如"你装了 Fabric，建议搭配 Fabric API"）。</summary>
    public string Reason { get; set; } = "";

    /// <summary>是否为依赖补全类推荐（UI 用醒目颜色标记）。</summary>
    public bool IsDependencyCompletion { get; set; }

    /// <summary>来源：本地规则 / 热门榜单 / 场景推荐 / 更新推荐。</summary>
    public RecommendationSource Source { get; set; }

    /// <summary>关联的已装 Mod 文件名（更新推荐 / 同类场景推荐时有值）。</summary>
    public string? RelatedInstalledMod { get; set; }
}

/// <summary>推荐来源。</summary>
public enum RecommendationSource
{
    /// <summary>本地规则引擎（必装前置等）。</summary>
    LocalRule,

    /// <summary>Modrinth 热门榜单。</summary>
    HotRanking,

    /// <summary>根据已装 Mod 类型做的同类场景推荐。</summary>
    Scene,

    /// <summary>已装 Mod 有新版本。</summary>
    Update
}
