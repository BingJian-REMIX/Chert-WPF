using System.Text.Json;
using System.Text.Json.Serialization;
using Chert.Core.Utils;

namespace Chert.Core.Download;

/// <summary>地图搜索排序方式。</summary>
public enum MapSort
{
    /// <summary>按发布时间（默认）。</summary>
    Published,
    /// <summary>按浏览量。</summary>
    Views
}

/// <summary>地图站搜索结果条目（开放 API 字段）。</summary>
public class PixelMapItem
{
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("summary")] public string Summary { get; set; } = "";
    [JsonPropertyName("author")] public string Author { get; set; } = "";

    /// <summary>唯一别名，用于拼详情地址。</summary>
    [JsonPropertyName("slug")] public string Slug { get; set; } = "";

    [JsonPropertyName("preview_image")] public string? PreviewImage { get; set; }
    [JsonPropertyName("versions")] public List<string> Versions { get; set; } = new();
    [JsonPropertyName("categories")] public List<string> Categories { get; set; } = new();
    [JsonPropertyName("views")] public int Views { get; set; }
    [JsonPropertyName("publish_time")] public DateTimeOffset? PublishTime { get; set; }

    /// <summary>作者是否允许下载。</summary>
    [JsonPropertyName("download_allowed")] public bool DownloadAllowed { get; set; }

    /// <summary>是否有附加资源（资源包 / 光影等）。</summary>
    [JsonPropertyName("extension_exist")] public bool ExtensionExist { get; set; }

    /// <summary>本站是否已托管地图文件（false 表示需跳转外链）。</summary>
    [JsonPropertyName("download_integrated")] public bool DownloadIntegrated { get; set; }

    [JsonPropertyName("extension_integrated")] public bool ExtensionIntegrated { get; set; }

    /// <summary>站内详情页地址。</summary>
    public string PageUrl => $"{PixelmapClient.SiteBase}/maps/{Slug}";

    /// <summary>版本摘要（超过 3 个折叠显示）。</summary>
    public string VersionSummary => Versions.Count switch
    {
        0 => "未标注",
        <= 3 => string.Join(" / ", Versions),
        _ => $"{Versions[0]} ~ {Versions[^1]}（{Versions.Count} 个版本）"
    };

    public string CategorySummary => Categories.Count == 0 ? "未分类" : string.Join("、", Categories);
}

/// <summary>地图搜索分页结果。</summary>
public class PixelMapSearchResult
{
    [JsonPropertyName("items")] public List<PixelMapItem> Items { get; set; } = new();
    [JsonPropertyName("total")] public int Total { get; set; }
    [JsonPropertyName("page")] public int Page { get; set; } = 1;
    [JsonPropertyName("limit")] public int Limit { get; set; } = 20;
    [JsonPropertyName("sort")] public string Sort { get; set; } = "published";

    /// <summary>总页数。</summary>
    public int PageCount => Limit <= 0 ? 1 : (int)Math.Ceiling(Total / (double)Limit);

    public bool HasNext => Page < PageCount;
}

/// <summary>地图分类。</summary>
public class PixelMapCategory
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";

    /// <summary>map / tutorial / both。</summary>
    [JsonPropertyName("type")] public string Type { get; set; } = "";

    /// <summary>是否适用于地图列表筛选。</summary>
    public bool IsMapCategory => Type is "map" or "both";
}

/// <summary>地图详情（含真实下载直链）。</summary>
public class PixelMapDetail
{
    public string Slug { get; set; } = "";
    public string Title { get; set; } = "";
    public string Summary { get; set; } = "";
    public string Author { get; set; } = "";
    public string? CoverImageUrl { get; set; }
    public string? ContentMarkdown { get; set; }
    public bool AllowDownload { get; set; }

    /// <summary>地图压缩包直链。</summary>
    public string? DownloadUrl { get; set; }

    /// <summary>附加资源（资源包 / 光影）直链。</summary>
    public string? AdditionalResourcesUrl { get; set; }

    public List<string> Versions { get; set; } = new();
    public List<string> Categories { get; set; } = new();
    public int Views { get; set; }
    public int Likes { get; set; }
    public double RatingAverage { get; set; }
    public int RatingCount { get; set; }

    /// <summary>能否直接下载（允许下载且有直链）。</summary>
    public bool CanDownload => AllowDownload && !string.IsNullOrWhiteSpace(DownloadUrl);

    /// <summary>是否提供附加资源（资源包 / 光影）直链。</summary>
    public bool HasAdditionalResources => !string.IsNullOrWhiteSpace(AdditionalResourcesUrl);

    /// <summary>站内详情页地址。</summary>
    public string PageUrl => $"{PixelmapClient.SiteBase}/maps/{Slug}";

    /// <summary>版本摘要（超过 3 个折叠显示）。</summary>
    public string VersionSummary => Versions.Count switch
    {
        0 => "未标注",
        <= 3 => string.Join(" / ", Versions),
        _ => $"{Versions[0]} ~ {Versions[^1]}（{Versions.Count} 个版本）"
    };

    /// <summary>分类摘要。</summary>
    public string CategorySummary => Categories.Count == 0 ? "未分类" : string.Join("、", Categories);

    /// <summary>评分摘要（无评分时显示"暂无评分"）。</summary>
    public string RatingSummary => RatingCount <= 0
        ? "暂无评分"
        : $"{RatingAverage:0.0} 分（{RatingCount} 人）";

    /// <summary>正文纯文本（Markdown 去标记，详情窗展示用）。</summary>
    public string DescriptionText
    {
        get
        {
            var body = MarkdownText.ToPlainText(ContentMarkdown);
            return body.Length > 0 ? body : Summary;
        }
    }
}

/// <summary>
/// 像素茶艺地图站客户端（下载页 → 地图）。
/// <list type="bullet">
///   <item>搜索：<c>GET /api/open/v1/maps/search</c>（开放 API）</item>
///   <item>详情：<c>GET /api/maps/{slug}</c>（含 <c>download_url</c> 直链）</item>
///   <item>分类：<c>GET /api/categories</c>；版本：<c>GET /api/versions</c></item>
/// </list>
/// 所有方法在网络失败时返回空结果而不抛异常，界面层据此提示"地图站暂不可用"。
/// </summary>
public class PixelmapClient
{
    /// <summary>站点根地址。</summary>
    public const string SiteBase = "https://goto.pixelmap.cc";

    /// <summary>开放 API 基址。</summary>
    public const string OpenApiBase = SiteBase + "/api/open/v1";

    /// <summary>站点内部 API 基址（详情 / 分类 / 版本）。</summary>
    public const string ApiBase = SiteBase + "/api";

    /// <summary>单页最大条数。</summary>
    public const int MaxLimit = 50;

    private readonly HttpClient _client;

    public PixelmapClient(HttpClient client) => _client = client;

    /// <summary>构造搜索 URL（纯函数，便于自检）。</summary>
    public static string BuildSearchUrl(
        string? keyword = null, string? category = null, string? version = null,
        int page = 1, int limit = 20, MapSort sort = MapSort.Published)
    {
        page = Math.Max(1, page);
        limit = Math.Clamp(limit, 1, MaxLimit);

        var qs = new List<string>
        {
            "page=" + page,
            "limit=" + limit,
            "sort=" + (sort == MapSort.Views ? "views" : "published")
        };
        if (!string.IsNullOrWhiteSpace(keyword)) qs.Add("keyword=" + Uri.EscapeDataString(keyword!.Trim()));
        if (!string.IsNullOrWhiteSpace(category)) qs.Add("category=" + Uri.EscapeDataString(category!.Trim()));
        if (!string.IsNullOrWhiteSpace(version)) qs.Add("version=" + Uri.EscapeDataString(version!.Trim()));

        return $"{OpenApiBase}/maps/search?{string.Join("&", qs)}";
    }

    /// <summary>搜索地图。</summary>
    public async Task<PixelMapSearchResult> SearchAsync(
        string? keyword = null, string? category = null, string? version = null,
        int page = 1, int limit = 20, MapSort sort = MapSort.Published,
        CancellationToken ct = default)
    {
        try
        {
            var json = await _client.GetStringAsync(
                BuildSearchUrl(keyword, category, version, page, limit, sort), ct).ConfigureAwait(false);
            return JsonSerializer.Deserialize<PixelMapSearchResult>(json) ?? new PixelMapSearchResult();
        }
        catch
        {
            return new PixelMapSearchResult();
        }
    }

    /// <summary>获取分类列表（仅返回适用于地图的分类）。</summary>
    public async Task<List<PixelMapCategory>> GetCategoriesAsync(CancellationToken ct = default)
    {
        try
        {
            var json = await _client.GetStringAsync($"{ApiBase}/categories", ct).ConfigureAwait(false);
            var all = JsonSerializer.Deserialize<List<PixelMapCategory>>(json) ?? new List<PixelMapCategory>();
            return all.Where(c => c.IsMapCategory).ToList();
        }
        catch
        {
            return new List<PixelMapCategory>();
        }
    }

    /// <summary>获取可筛选的游戏版本列表。</summary>
    public async Task<List<string>> GetVersionsAsync(CancellationToken ct = default)
    {
        try
        {
            var json = await _client.GetStringAsync($"{ApiBase}/versions", ct).ConfigureAwait(false);
            return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
        }
        catch
        {
            return new List<string>();
        }
    }

    /// <summary>获取地图详情（含下载直链）；失败返回 null。</summary>
    public async Task<PixelMapDetail?> GetDetailAsync(string slug, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(slug)) return null;
        try
        {
            var json = await _client.GetStringAsync(
                $"{ApiBase}/maps/{Uri.EscapeDataString(slug)}", ct).ConfigureAwait(false);
            return ParseDetail(json, slug);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>解析详情响应（纯函数）。响应结构：<c>{ "post": { ... } }</c>。</summary>
    public static PixelMapDetail? ParseDetail(string? json, string slug)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("post", out var post)) return null;

            string? Str(string name) =>
                post.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

            int Int(string name) =>
                post.TryGetProperty(name, out var v) && v.TryGetInt32(out var i) ? i : 0;

            double Dbl(string name) =>
                post.TryGetProperty(name, out var v) && v.TryGetDouble(out var d) ? d : 0;

            bool Bool(string name) =>
                post.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

            var detail = new PixelMapDetail
            {
                Slug = slug,
                Title = Str("title") ?? "",
                Summary = Str("summary") ?? "",
                CoverImageUrl = Str("cover_image_url"),
                ContentMarkdown = Str("content_md"),
                AllowDownload = Bool("allow_download"),
                DownloadUrl = Str("download_url"),
                AdditionalResourcesUrl = Str("additional_resources_url"),
                Views = Int("views"),
                Likes = Int("likes"),
                RatingAverage = Dbl("rating_average"),
                RatingCount = Int("rating_count")
            };

            if (post.TryGetProperty("author", out var author) &&
                author.ValueKind == JsonValueKind.Object)
            {
                detail.Author = (author.TryGetProperty("display_name", out var dn) ? dn.GetString() : null)
                                ?? (author.TryGetProperty("username", out var un) ? un.GetString() : null)
                                ?? "";
            }

            if (post.TryGetProperty("versions", out var vers) && vers.ValueKind == JsonValueKind.Array)
                foreach (var v in vers.EnumerateArray())
                    if (v.TryGetProperty("id", out var vid) && vid.ValueKind == JsonValueKind.String)
                        detail.Versions.Add(vid.GetString()!);

            if (post.TryGetProperty("categories", out var cats) && cats.ValueKind == JsonValueKind.Array)
                foreach (var c in cats.EnumerateArray())
                    if (c.TryGetProperty("name", out var cn) && cn.ValueKind == JsonValueKind.String)
                        detail.Categories.Add(cn.GetString()!);

            if (string.IsNullOrWhiteSpace(detail.DownloadUrl)) detail.DownloadUrl = null;
            if (string.IsNullOrWhiteSpace(detail.AdditionalResourcesUrl)) detail.AdditionalResourcesUrl = null;

            return detail;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>把详情转成下载任务（保存到 <c>{gameRoot}/downloads/maps</c>）。</summary>
    public static DownloadItem? ToDownloadItem(PixelMapDetail detail, string gameRoot)
    {
        if (!detail.CanDownload) return null;

        var dir = Path.Combine(gameRoot, "downloads", "maps");
        var fileName = SafeFileName(detail.DownloadUrl!, detail.Title);
        return new DownloadItem(new[] { detail.DownloadUrl! }, Path.Combine(dir, fileName));
    }

    /// <summary>把附加资源转成下载任务（保存到 <c>{gameRoot}/downloads/extras</c>）。</summary>
    public static DownloadItem? ToExtraDownloadItem(PixelMapDetail detail, string gameRoot)
    {
        if (!detail.HasAdditionalResources) return null;

        var dir = Path.Combine(gameRoot, "downloads", "extras");
        var fileName = SafeFileName(detail.AdditionalResourcesUrl!, detail.Title + "-附加资源");
        return new DownloadItem(new[] { detail.AdditionalResourcesUrl! }, Path.Combine(dir, fileName));
    }

    /// <summary>从直链推断安全的本地文件名（非法字符替换为下划线）。</summary>
    public static string SafeFileName(string url, string fallbackTitle)
    {
        var name = "";
        try
        {
            name = Path.GetFileName(new Uri(url).AbsolutePath);
        }
        catch
        {
            // 保持空，走标题回退
        }

        if (string.IsNullOrWhiteSpace(name) || !name.Contains('.'))
            name = (string.IsNullOrWhiteSpace(fallbackTitle) ? "map" : fallbackTitle) + ".zip";

        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name;
    }
}
