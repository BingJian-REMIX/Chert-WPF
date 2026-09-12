using Chert.Core.Models;
using Chert.Core.Utils;

namespace Chert.Core.Download;

/// <summary>
/// Modrinth 整合包源。免 API Key，直接走 <c>/v2/search</c> + <c>/v2/project/{id}/version</c>。
/// </summary>
public class ModrinthModpackSource : IModpackSource
{
    private readonly ModrinthClient _client;

    public ModrinthModpackSource(HttpClient http) => _client = new ModrinthClient(http);

    public string Id => "modrinth";
    public string DisplayName => "Modrinth";
    public bool IsAvailable => true;
    public string? UnavailableReason => null;

    /// <summary>已知的加载器标签（用于从 categories 里筛出加载器）。</summary>
    private static readonly string[] KnownLoaders = { "fabric", "forge", "quilt", "neoforge", "liteloader", "rift" };

    public async Task<List<ModpackItem>> SearchAsync(string? keyword, string? gameVersion, string? loader,
        int limit = 24, int offset = 0, CancellationToken ct = default)
    {
        try
        {
            var loaderType = ParseLoader(loader);
            var result = await _client.SearchAsync(
                keyword ?? "", gameVersion, loaderType, ModrinthProjectType.Modpack,
                limit, offset, index: null, ct).ConfigureAwait(false);

            return result.Hits.Select(h => new ModpackItem
            {
                Source = Id,
                Id = h.ProjectId,
                Title = h.Title,
                Summary = h.Description,
                Author = h.Slug,
                IconUrl = string.IsNullOrWhiteSpace(h.IconUrl) ? null : h.IconUrl,
                Downloads = h.Downloads,
                GameVersions = SortVersionsDescending(h.Versions),
                Loaders = h.Categories.Where(c => KnownLoaders.Contains(c, StringComparer.OrdinalIgnoreCase)).ToList()
            }).ToList();
        }
        catch
        {
            return new List<ModpackItem>();
        }
    }

    public async Task<ModpackDetail?> GetDetailAsync(string id, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        try
        {
            var project = await _client.GetProjectAsync(id, ct).ConfigureAwait(false);
            if (project is null) return null;

            var versions = await _client.GetVersionsAsync(id, ct).ConfigureAwait(false);

            return new ModpackDetail
            {
                Source = Id,
                Id = project.Id,
                Title = project.Title,
                Summary = project.Description,
                Author = project.Slug,
                IconUrl = string.IsNullOrWhiteSpace(project.IconUrl) ? null : project.IconUrl,
                Description = MarkdownText.ToPlainText(project.Body, 4000),
                Downloads = project.Downloads,
                Versions = MapVersions(versions),
                PageUrl = $"https://modrinth.com/modpack/{project.Slug}"
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>把 Modrinth 版本列表映射成统一版本视图（纯函数，便于自检）。</summary>
    public static List<ModpackVersion> MapVersions(List<ModrinthVersion> versions)
    {
        var result = new List<ModpackVersion>();
        foreach (var v in versions)
        {
            // 整合包本体优先取 .mrpack；没有则退回 primary 文件
            var file = v.Files.FirstOrDefault(f => f.FileName.EndsWith(".mrpack", StringComparison.OrdinalIgnoreCase))
                       ?? v.Files.FirstOrDefault(f => f.Primary)
                       ?? v.Files.FirstOrDefault();
            if (file is null) continue;

            result.Add(new ModpackVersion
            {
                Id = v.Id,
                Name = v.Name,
                VersionNumber = v.VersionNumber,
                GameVersion = v.GameVersions.LastOrDefault() ?? "",
                Loader = v.Loaders.FirstOrDefault() ?? "",
                FileUrl = file.Url,
                FileName = file.FileName,
                Sha1 = string.IsNullOrWhiteSpace(file.Hashes.Sha1) ? null : file.Hashes.Sha1,
                FileSize = file.Size
            });
        }
        return result;
    }

    /// <summary>把版本号按语义倒序（新版本在前），无法解析的排到末尾。</summary>
    public static List<string> SortVersionsDescending(IEnumerable<string> versions)
    {
        return versions
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct()
            .OrderByDescending(VersionKey)
            .ToList();
    }

    private static (int Major, int Minor, int Patch) VersionKey(string v)
    {
        var parts = v.Split('.', '-', '_');
        int Get(int i) => parts.Length > i && int.TryParse(parts[i], out var n) ? n : 0;
        return (Get(0), Get(1), Get(2));
    }

    private static LoaderType ParseLoader(string? loader) => (loader ?? "").ToLowerInvariant() switch
    {
        "fabric" => LoaderType.Fabric,
        "forge" => LoaderType.Forge,
        "quilt" => LoaderType.Quilt,
        "neoforge" => LoaderType.NeoForge,
        _ => LoaderType.Any
    };
}
