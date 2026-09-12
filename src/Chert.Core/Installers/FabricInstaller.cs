using Chert.Core.Download;
using Chert.Core.Launcher;
using Chert.Core.Models;
using Chert.Core.Utils;

namespace Chert.Core.Installers;

/// <summary>
/// Fabric 安装：基于原版，获取最新 Fabric Loader，合并生成新版本 JSON，
/// 安装依赖库、创建 .fabric 标记，并自动安装 Fabric API。
/// </summary>
public class FabricInstaller : InstallerBase
{
    private readonly ModrinthClient _modrinth;

    public FabricInstaller(string gameRoot, HttpClient client, IDownloader downloader, ILogger? logger = null)
        : base(gameRoot, client, downloader, logger)
    {
        _modrinth = new ModrinthClient(client);
    }

    /// <summary>安装 Fabric（mcVersion 为原版版本号，如 1.20.1）。</summary>
    public async Task<string> InstallAsync(string mcVersion,
        IProgress<(int Done, int Total)>? progress = null,
        CancellationToken ct = default)
    {
        var newId = "fabric-" + mcVersion;

        // 1. 确保原版已安装（提供 base 库/资源）
        Logger?.Log($"确保原版 {mcVersion} 已安装 ...");
        var vanilla = new VanillaInstaller(GameRoot, Client, Downloader, Logger);
        await vanilla.InstallAsync(mcVersion, progress, ct);

        // 2. 获取最新 Loader + Intermediary
        var url = $"{GameConstants.FabricMetaBase}/versions/loader/{mcVersion}";
        var json = await Client.GetStringAsync(url, ct);
        var entries = System.Text.Json.JsonSerializer.Deserialize<List<FabricLoaderEntry>>(json)
                      ?? throw new InvalidOperationException("无法解析 Fabric Loader 元数据");
        var entry = entries.FirstOrDefault();
        if (entry is null)
            throw new InvalidOperationException($"Fabric 不支持的 Minecraft 版本或元数据为空：{mcVersion}");

        // 3. 合并生成版本 JSON
        var mainClass = entry.LauncherMeta.MainClass.TryGetValue("client", out var mc) && !string.IsNullOrEmpty(mc)
            ? mc : "net.fabricmc.loader.impl.launcher.Main";

        var gameArgs = new List<ArgumentItem>();
        if (entry.LauncherMeta.Arguments.TryGetValue("client", out var clientArgs))
            foreach (var a in clientArgs)
                gameArgs.Add(new ArgumentItem { Values = new List<string> { a } });

        var merged = new VersionJson
        {
            Id = newId,
            Type = "release",
            InheritsFrom = mcVersion,
            MainClass = mainClass,
            Libraries = entry.LauncherMeta.Libraries,
            Arguments = new Arguments { Game = gameArgs, Jvm = new List<ArgumentItem>() },
            ReleaseTime = DateTime.UtcNow.ToString("o")
        };

        var dir = PathEx.VersionDir(GameRoot, newId);
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(PathEx.VersionJsonPath(GameRoot, newId),
            System.Text.Json.JsonSerializer.Serialize(merged, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }), ct);
        Logger?.Log($"已生成 Fabric 版本 JSON：{newId}（Loader {entry.Loader.Version}）");

        // 4. 下载 Fabric 依赖库
        await DownloadLibrariesAsync(merged, progress, ct);

        // 5. 创建 .fabric 标记文件夹
        Directory.CreateDirectory(PathEx.FabricMarker(GameRoot, newId));

        // 6. 自动安装 Fabric API
        await InstallFabricApiAsync(mcVersion, ct);

        Logger?.Log($"Fabric {newId} 安装完成");
        return newId;
    }

    private async Task InstallFabricApiAsync(string mcVersion, CancellationToken ct)
    {
        try
        {
            var search = await _modrinth.SearchAsync(GameConstants.FabricApiProjectId,
                gameVersion: mcVersion, loader: LoaderType.Fabric, type: ModrinthProjectType.Mod);
            var hit = search.Hits.FirstOrDefault();
            if (hit is null) { Logger?.Log("未找到 Fabric API，跳过自动安装"); return; }

            var versions = await _modrinth.GetVersionsAsync(hit.ProjectId, ct);
            var ver = versions.FirstOrDefault(v => v.GameVersions.Contains(mcVersion)
                                                    && v.Loaders.Contains("fabric", StringComparer.OrdinalIgnoreCase))
                      ?? versions.FirstOrDefault(v => v.Loaders.Contains("fabric", StringComparer.OrdinalIgnoreCase));
            if (ver is null) return;

            var file = _modrinth.SelectBestFile(ver, mcVersion, LoaderType.Fabric);
            if (file is null) return;

            var dest = Path.Combine(PathEx.ModsDir(GameRoot), file.FileName);
            Directory.CreateDirectory(PathEx.ModsDir(GameRoot));
            await Downloader.DownloadAsync(new DownloadItem(new[] { file.Url }, dest, file.Hashes.Sha1), null, ct);
            Logger?.Log($"已安装 Fabric API：{file.FileName}");
        }
        catch (Exception ex)
        {
            Logger?.Log($"Fabric API 自动安装失败（可稍后手动安装）：{ex.Message}");
        }
    }
}
