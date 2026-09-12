namespace Chert.App.ViewModels;

/// <summary>
/// 下载页卡片模型：统一承载 Modrinth 与像素茶艺的搜索结果。
/// 封面图走外联 URL（<see cref="IconUrl"/>），由 <see cref="ExternalIcon"/> 异步加载并缓存，
/// <see cref="FallbackToken"/> 指定加载失败时的内嵌 PNG 占位（对应 Resources/Icons 文件名）。
/// </summary>
public class DownloadCardItem
{
    /// <summary>项目 Id（Modrinth 为 project_id；像素茶艺为 slug）。</summary>
    public string Id { get; init; } = "";

    public string Title { get; init; } = "";
    public string Author { get; init; } = "";
    public string Summary { get; init; } = "";

    /// <summary>外联封面 URL（空表示无封面，直接显示占位）。</summary>
    public string? IconUrl { get; init; }

    /// <summary>占位图标 token（对应内嵌 PNG 文件名）。</summary>
    public string FallbackToken { get; init; } = "mod";

    /// <summary>来源：Modrinth / PixelMap。</summary>
    public string Source { get; init; } = "";

    /// <summary>所属副标签：mod / shader / resourcepack / modpack / map / minecraft。</summary>
    public string SubTab { get; init; } = "";

    /// <summary>Minecraft 版本类型（release / snapshot / old_beta / old_alpha），仅 Minecraft 子页使用。</summary>
    public string VersionType { get; init; } = "";

    // ---- 地图（像素茶艺）专用字段 ----

    public string? Slug { get; init; }
    public int Views { get; init; }
    public string VersionSummary { get; init; } = "";
    public string CategorySummary { get; init; } = "";
    public bool CanDownload { get; init; } = true;

    /// <summary>该地图是否带附加资源（资源包 / 光影），卡片上打标。</summary>
    public bool HasExtra { get; init; }

    /// <summary>浏览量友好文本（1.2万 / 8.1k 风格），仅地图卡片显示。</summary>
    public string ViewsText => Views switch
    {
        <= 0 => "",
        < 10000 => $"{Views} 次浏览",
        _ => $"{Views / 10000.0:0.#} 万次浏览"
    };

    /// <summary>是否为地图卡片（驱动浏览量 / 附加资源标记显隐）。</summary>
    public bool IsMapCard => SubTab == "map";

    /// <summary>整合包卡片的次级元信息行（如 "Fabric · 1.20.1 · 12.3K 下载"），其它卡片为空。</summary>
    public string MetaText { get; init; } = "";
}
