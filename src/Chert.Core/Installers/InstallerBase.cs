using Chert.Core.Download;
using Chert.Core.Launcher;
using Chert.Core.Models;
using Chert.Core.Utils;

namespace Chert.Core.Installers;

/// <summary>
/// 安装器基类：提供版本清单获取、版本 JSON 下载、库依赖下载、资源索引与资源文件下载等公共能力。
/// 镜像策略统一走 MirrorPolicy（BMCLAPI 优先，官方回退）。
/// </summary>
public abstract class InstallerBase
{
    protected string GameRoot { get; }
    protected HttpClient Client { get; }
    protected IDownloader Downloader { get; }
    protected ILogger? Logger { get; }

    protected InstallerBase(string gameRoot, HttpClient client, IDownloader downloader, ILogger? logger = null)
    {
        GameRoot = gameRoot;
        Client = client;
        Downloader = downloader;
        Logger = logger;
    }

    /// <summary>获取官方/BMCLAPI 版本清单。</summary>
    protected async Task<VersionManifest> GetVersionManifestAsync(CancellationToken ct = default)
    {
        var json = await MirrorPolicy.GetStringWithFallback(MirrorPolicy.VersionManifestUrls(), Client, ct);
        return System.Text.Json.JsonSerializer.Deserialize<VersionManifest>(json)
               ?? throw new InvalidOperationException("无法解析版本清单");
    }

    /// <summary>下载并保存指定版本的 version.json（写入 versions/{id}/{id}.json）。返回解析结果。</summary>
    protected async Task<VersionJson> DownloadVersionJsonAsync(string id, string? officialUrl = null, CancellationToken ct = default)
    {
        var json = await MirrorPolicy.GetStringWithFallback(
            MirrorPolicy.VersionJsonUrls(id, officialUrl), Client, ct);
        var version = System.Text.Json.JsonSerializer.Deserialize<VersionJson>(json)
                      ?? throw new InvalidOperationException($"无法解析版本 JSON：{id}");

        var dir = PathEx.VersionDir(GameRoot, id);
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(PathEx.VersionJsonPath(GameRoot, id), json, ct);
        return version;
    }

    /// <summary>下载版本核心 JAR（downloads.client）。</summary>
    protected async Task DownloadClientJarAsync(VersionJson version, CancellationToken ct = default)
    {
        if (!version.Downloads.TryGetValue("client", out var client) || client?.Url is null) return;
        var dest = PathEx.VersionJarPath(GameRoot, version.Id);
        var urls = new List<string> { client.Url };
        if (client.Url.Contains("launcher.mojang.com") || client.Url.Contains("piston-data.mojang.com"))
        {
            // BMCLAPI 回退：用版本 id 推断
            urls.Add($"{GameConstants.BmclapiBase}/versions/{version.Id}/{version.Id}.jar");
        }
        await Downloader.DownloadAsync(new DownloadItem(urls, dest, client.Sha1, client.Size), null, ct);
        Logger?.Log($"已下载核心 JAR：{version.Id}");
    }

    /// <summary>下载全部库（artifact + 当前平台 natives），按规则过滤。返回下载任务列表（不在此 await）。</summary>
    protected List<DownloadItem> BuildLibraryDownloads(VersionJson version)
        => BuildLibraryDownloads(version, GameRoot);

    /// <summary>静态版本：给定版本 JSON 与游戏根目录，构建库下载项列表。供修复流程复用。</summary>
    public static List<DownloadItem> BuildLibraryDownloads(VersionJson version, string gameRoot)
    {
        var osName = RuleEvaluator.CurrentOsName();
        var items = new List<DownloadItem>();

        foreach (var lib in version.Libraries)
        {
            if (!RuleEvaluator.IsAllowed(lib.Rules, osName)) continue;

            if (lib.Downloads?.Artifact is { } artifact)
            {
                var dest = Path.Combine(gameRoot, "libraries", artifact.Path ?? lib.Coordinate.LocalPath());
                items.Add(new DownloadItem(LibraryUrls(lib, artifact), dest, artifact.Sha1, artifact.Size));
            }
            else if (lib.Downloads is null && !string.IsNullOrEmpty(lib.Name) && lib.Natives is null)
            {
                var path = lib.Coordinate.LocalPath();
                var dest = Path.Combine(gameRoot, "libraries", path);
                items.Add(new DownloadItem(LibraryUrls(lib, null), dest));
            }

            if (lib.Natives is not null && lib.Natives.TryGetValue(osName, out var classifier)
                && lib.Downloads?.Classifiers is not null
                && lib.Downloads.Classifiers.TryGetValue(classifier, out var nfo) && nfo is not null)
            {
                var dest = Path.Combine(gameRoot, "libraries", nfo.Path ?? lib.Coordinate.LocalPath(classifier));
                items.Add(new DownloadItem(LibraryUrls(lib, nfo), dest, nfo.Sha1, nfo.Size));
            }
        }
        return items;
    }

    /// <summary>下载库依赖（并行）。</summary>
    protected async Task DownloadLibrariesAsync(VersionJson version, IProgress<(int, int)>? progress = null, CancellationToken ct = default)
    {
        var items = BuildLibraryDownloads(version);
        Logger?.Log($"开始下载 {items.Count} 个库依赖 ...");
        await Downloader.DownloadBatchAsync(items, progress, ct);
    }

    /// <summary>下载资源索引 + 资源对象（并行）。</summary>
    protected async Task DownloadAssetsAsync(VersionJson version, IProgress<(int, int)>? progress = null, CancellationToken ct = default)
    {
        if (version.AssetIndex is null) return;

        var indexJson = await MirrorPolicy.GetStringWithFallback(
            MirrorPolicy.AssetIndexUrls(version.AssetIndex.Url), Client, ct);
        var index = System.Text.Json.JsonSerializer.Deserialize<AssetIndex>(indexJson)
                    ?? throw new InvalidOperationException("无法解析资源索引");

        var indexPath = Path.Combine(GameRoot, "assets", "indexes", version.AssetIndex.Id + ".json");
        await File.WriteAllTextAsync(indexPath, indexJson, ct);

        var items = index.Objects.Select(kv =>
        {
            var dest = PathEx.AssetObjectPath(GameRoot, kv.Value.Hash);
            return new DownloadItem(MirrorPolicy.AssetUrls(kv.Value.Hash), dest, kv.Value.Hash);
        }).ToList();

        Logger?.Log($"开始下载 {items.Count} 个资源文件 ...");
        await Downloader.DownloadBatchAsync(items, progress, ct);
    }

    /// <summary>生成库下载候选 URL（官方 url 优先，BMCLAPI/官方库/自定义 maven 回退）。</summary>
    public static List<string> LibraryUrls(Library lib, DownloadInfo? info)
    {
        var path = info?.Path ?? lib.Coordinate.LocalPath();
        var urls = new List<string>();
        if (info?.Url is not null) urls.Add(info.Url);
        urls.Add($"{GameConstants.BmclapiBase}/libraries/{path}");
        urls.Add($"{GameConstants.OfficialLibrariesBase}/{path}");
        if (lib.Url is not null) urls.Add(lib.Url.TrimEnd('/') + "/" + path);
        return urls.Distinct().ToList();
    }
}
