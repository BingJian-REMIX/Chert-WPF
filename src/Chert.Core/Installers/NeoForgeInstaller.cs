using System.Diagnostics;
using System.Xml.Linq;
using Chert.Core.Download;
using Chert.Core.Launcher;
using Chert.Core.Models;
using Chert.Core.Utils;

namespace Chert.Core.Installers;

/// <summary>
/// NeoForge 安装：从 NeoForge maven（BMCLAPI 镜像优先）解析适用于指定 MC 版本的最新构建，
/// 下载 installer 并校验，以无头模式运行安装器（失败则提示手动 GUI），最后补全缺失依赖库。
/// </summary>
public class NeoForgeInstaller : InstallerBase
{
    public NeoForgeInstaller(string gameRoot, HttpClient client, IDownloader downloader, ILogger? logger = null)
        : base(gameRoot, client, downloader, logger) { }

    /// <summary>安装 NeoForge（mcVersion 为原版版本号，如 1.20.4）。返回新版本 Id（neoforge-&lt;build&gt;）。</summary>
    public async Task<string> InstallAsync(string mcVersion,
        IProgress<(int Done, int Total)>? progress = null,
        CancellationToken ct = default)
    {
        // 确保原版基础已就绪
        Logger?.Log($"确保原版 {mcVersion} 已安装 ...");
        await new VanillaInstaller(GameRoot, Client, Downloader, Logger).InstallAsync(mcVersion, progress, ct);

        var neoVersion = await ResolveVersionAsync(mcVersion, ct);
        var newId = "neoforge-" + neoVersion;
        Logger?.Log($"目标 NeoForge 版本：{neoVersion}");

        // 下载 installer jar（官方 maven 优先，BMCLAPI 镜像回退）
        var installerJar = Path.Combine(Path.GetTempPath(), $"neoforge-{neoVersion}-installer.jar");
        var urlMaven = $"https://maven.neoforged.net/releases/net/neoforged/neoforge/{neoVersion}/neoforge-{neoVersion}-installer.jar";
        var urlBmcl = $"{GameConstants.BmclapiBase}/maven/net/neoforged/neoforge/{neoVersion}/neoforge-{neoVersion}-installer.jar";
        await Downloader.DownloadAsync(new DownloadItem(new[] { urlMaven, urlBmcl }, installerJar), null, ct);

        // 校验（魔术字节）
        if (!HashUtil.IsZip(installerJar))
            throw new InvalidDataException("下载的 NeoForge installer 不是有效的 JAR/ZIP 文件");

        // 运行安装器（无头模式）
        var java = await JavaDetector.FindBestAsync(GameConstants.MinimumJavaMajorVersion)
                   ?? throw new InvalidOperationException("未找到满足条件的 Java 以运行 NeoForge 安装器");
        Logger?.Log($"运行 NeoForge 安装器（{java.MajorVersion}）...");

        var psi = new ProcessStartInfo
        {
            FileName = java.JavaExe,
            ArgumentList = { "-jar", installerJar, "--installClient", GameRoot },
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException("无法启动 NeoForge 安装器");
        var stdErr = await proc.StandardError.ReadToEndAsync();
        await proc.WaitForExitAsync(ct);

        if (proc.ExitCode != 0)
        {
            Logger?.Log($"NeoForge 无头安装返回 {proc.ExitCode}，如需可手动运行 installer 完成 GUI 安装。");
            Logger?.Log(stdErr);
        }

        // 补全缺失依赖库
        var neoJson = PathEx.VersionJsonPath(GameRoot, newId);
        if (File.Exists(neoJson))
        {
            var v = System.Text.Json.JsonSerializer.Deserialize<VersionJson>(await File.ReadAllTextAsync(neoJson, ct));
            if (v is not null) await DownloadLibrariesAsync(v, progress, ct);
        }
        else
        {
            Logger?.Log($"未找到 NeoForge 版本 JSON（{newId}），安装器可能未完成，请检查。");
        }

        Logger?.Log($"NeoForge {newId} 安装流程结束");
        return newId;
    }

    private async Task<string> ResolveVersionAsync(string mcVersion, CancellationToken ct)
    {
        var metaUrl = "https://maven.neoforged.net/releases/net/neoforged/neoforge/maven-metadata.xml";
        var xml = await Client.GetStringAsync(metaUrl, ct);
        var doc = XDocument.Parse(xml);
        var versions = doc.Descendants("version").Select(e => e.Value).ToList();
        if (versions.Count == 0)
            throw new InvalidOperationException("无法解析 NeoForge 版本元数据");

        // 优先匹配 <mcVersion>- 前缀的构建（如 1.20.4-49.0.0），按语义化版本号排序
        var matched = versions
            .Where(v => v.StartsWith(mcVersion + "-", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(v => v, VersionComparer.Instance)
            .ToList();
        if (matched.Count > 0) return matched[0];

        // 新格式（无 MC 前缀）回退到全部最新（按语义化版本号排序）
        Logger?.Log($"未找到 {mcVersion} 前缀的 NeoForge 构建，回退到最新版本。");
        return versions.OrderByDescending(v => v, VersionComparer.Instance).First();
    }
}
