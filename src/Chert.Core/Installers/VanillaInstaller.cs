using Chert.Core.Download;
using Chert.Core.Models;
using Chert.Core.Utils;

namespace Chert.Core.Installers;

/// <summary>原版安装：下载版本 JSON（含继承链）、核心 JAR、全部库、资源索引与资源文件。</summary>
public class VanillaInstaller : InstallerBase
{
    public VanillaInstaller(string gameRoot, HttpClient client, IDownloader downloader, ILogger? logger = null)
        : base(gameRoot, client, downloader, logger) { }

    public async Task InstallAsync(string versionId,
        IProgress<(int Done, int Total)>? progress = null,
        CancellationToken ct = default)
    {
        var manifest = await GetVersionManifestAsync(ct);
        var urlMap = manifest.Versions.ToDictionary(v => v.Id, v => v.Url, StringComparer.OrdinalIgnoreCase);

        // 沿继承链下载所有版本 JSON
        var chain = new List<string>();
        var current = versionId;
        VersionJson? leaf = null;
        var guard = 0;
        while (!string.IsNullOrEmpty(current) && guard++ < 20 && !chain.Contains(current))
        {
            chain.Add(current);
            var v = await DownloadVersionJsonAsync(current,
                urlMap.TryGetValue(current, out var u) ? u : null, ct);
            leaf ??= v;
            current = v.InheritsFrom ?? "";
        }

        // 核心 JAR（拥有 downloads.client 的版本）
        foreach (var id in chain)
        {
            var v = System.Text.Json.JsonSerializer.Deserialize<VersionJson>(
                await File.ReadAllTextAsync(PathEx.VersionJsonPath(GameRoot, id), ct));
            if (v is not null) await DownloadClientJarAsync(v, ct);
        }

        // 库与资源（链上所有版本）
        foreach (var id in chain)
        {
            var v = System.Text.Json.JsonSerializer.Deserialize<VersionJson>(
                await File.ReadAllTextAsync(PathEx.VersionJsonPath(GameRoot, id), ct));
            if (v is null) continue;
            await DownloadLibrariesAsync(v, progress, ct);
            if (v.AssetIndex is not null) await DownloadAssetsAsync(v, progress, ct);
        }

        Logger?.Log($"原版 {versionId} 安装完成");
    }
}
