using System.Diagnostics;
using System.Xml.Linq;
using Chert.Core.Download;
using Chert.Core.Launcher;
using Chert.Core.Models;
using Chert.Core.Utils;

namespace Chert.Core.Installers;

/// <summary>
/// Quilt 安装：下载 Quilt installer（maven.quiltmc.org），以无头模式运行，
/// 为指定 MC 版本安装客户端 profile（自动选用最新 Quilt Loader）。
/// </summary>
public class QuiltInstaller : InstallerBase
{
    public QuiltInstaller(string gameRoot, HttpClient client, IDownloader downloader, ILogger? logger = null)
        : base(gameRoot, client, downloader, logger) { }

    /// <summary>安装 Quilt（mcVersion 为原版版本号，如 1.20.1）。返回新版本 Id（quilt-loader-&lt;mcVersion&gt;）。</summary>
    public async Task<string> InstallAsync(string mcVersion,
        IProgress<(int Done, int Total)>? progress = null,
        CancellationToken ct = default)
    {
        // 确保原版基础已就绪
        Logger?.Log($"确保原版 {mcVersion} 已安装 ...");
        await new VanillaInstaller(GameRoot, Client, Downloader, Logger).InstallAsync(mcVersion, progress, ct);

        var installerVer = await ResolveInstallerVersionAsync(ct);
        var newId = "quilt-loader-" + mcVersion;
        Logger?.Log($"目标 Quilt 安装器版本：{installerVer}（MC {mcVersion}）");

        // 下载 installer jar
        var installerJar = Path.Combine(Path.GetTempPath(), $"quilt-installer-{installerVer}.jar");
        var url = $"https://maven.quiltmc.org/repository/release/org/quiltmc/quilt-installer/{installerVer}/quilt-installer-{installerVer}.jar";
        await Downloader.DownloadAsync(new DownloadItem(new[] { url }, installerJar), null, ct);

        // 校验（魔术字节）
        if (!HashUtil.IsZip(installerJar))
            throw new InvalidDataException("下载的 Quilt installer 不是有效的 JAR/ZIP 文件");

        // 运行安装器（无头模式，自动选用最新 Loader）
        var java = await JavaDetector.FindBestAsync(GameConstants.MinimumJavaMajorVersion)
                   ?? throw new InvalidOperationException("未找到满足条件的 Java 以运行 Quilt 安装器");
        Logger?.Log($"运行 Quilt 安装器（{java.MajorVersion}）...");

        var psi = new ProcessStartInfo
        {
            FileName = java.JavaExe,
            ArgumentList = { "-jar", installerJar, "install", "client", mcVersion },
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException("无法启动 Quilt 安装器");
        var stdErr = await proc.StandardError.ReadToEndAsync();
        await proc.WaitForExitAsync(ct);

        if (proc.ExitCode != 0)
        {
            Logger?.Log($"Quilt 无头安装返回 {proc.ExitCode}，如需可手动运行 installer 完成 GUI 安装。");
            Logger?.Log(stdErr);
        }

        // 补全缺失依赖库
        var quiltJson = PathEx.VersionJsonPath(GameRoot, newId);
        if (File.Exists(quiltJson))
        {
            var v = System.Text.Json.JsonSerializer.Deserialize<VersionJson>(await File.ReadAllTextAsync(quiltJson, ct));
            if (v is not null) await DownloadLibrariesAsync(v, progress, ct);
        }
        else
        {
            Logger?.Log($"未找到 Quilt 版本 JSON（{newId}），安装器可能未完成，请检查。");
        }

        Logger?.Log($"Quilt {newId} 安装流程结束");
        return newId;
    }

    private async Task<string> ResolveInstallerVersionAsync(CancellationToken ct)
    {
        var metaUrl = "https://maven.quiltmc.org/repository/release/org/quiltmc/quilt-installer/maven-metadata.xml";
        var xml = await Client.GetStringAsync(metaUrl, ct);
        var doc = XDocument.Parse(xml);
        var versions = doc.Descendants("version").Select(e => e.Value).ToList();
        if (versions.Count == 0)
            throw new InvalidOperationException("无法解析 Quilt installer 版本元数据");
        // 取最新（按语义化版本号比较，避免 "0.9.1" 被误判为比 "0.10.0" 新）
        return versions.OrderByDescending(v => v, VersionComparer.Instance).First();
    }
}
