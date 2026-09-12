using System.Diagnostics;
using System.Text.Json;
using Chert.Core.Download;
using Chert.Core.Launcher;
using Chert.Core.Models;
using Chert.Core.Utils;

namespace Chert.Core.Installers;

/// <summary>
/// Forge 安装：从 BMCLAPI/官方获取版本列表（promotions），下载 installer 并校验，
/// 以无头模式运行安装器（失败则提示用户手动 GUI 安装），最后补全缺失依赖库。
/// </summary>
public class ForgeInstaller : InstallerBase
{
    public ForgeInstaller(string gameRoot, HttpClient client, IDownloader downloader, ILogger? logger = null)
        : base(gameRoot, client, downloader, logger) { }

    public async Task<string> InstallAsync(string mcVersion,
        IProgress<(int Done, int Total)>? progress = null,
        CancellationToken ct = default)
    {
        // 确保原版基础已就绪
        Logger?.Log($"确保原版 {mcVersion} 已安装 ...");
        await new VanillaInstaller(GameRoot, Client, Downloader, Logger).InstallAsync(mcVersion, progress, ct);

        // 1. 解析推荐/最新 build
        var build = await ResolveBuildAsync(mcVersion, ct);
        var forgeVersion = $"{mcVersion}-{build}";
        var newId = $"{mcVersion}-forge-{build}";
        Logger?.Log($"目标 Forge 版本：{forgeVersion}");

        // 2. 下载 installer jar
        var installerJar = Path.Combine(Path.GetTempPath(), $"forge-{forgeVersion}-installer.jar");
        var urlBmcl = $"{GameConstants.BmclapiBase}/maven/net/minecraftforge/forge/{forgeVersion}/forge-{forgeVersion}-installer.jar";
        var urlOff = $"https://maven.minecraftforge.net/net/minecraftforge/forge/{forgeVersion}/forge-{forgeVersion}-installer.jar";
        await Downloader.DownloadAsync(new DownloadItem(new[] { urlBmcl, urlOff }, installerJar), null, ct);

        // 3. 校验（魔术字节）
        if (!HashUtil.IsZip(installerJar))
            throw new InvalidDataException("下载的 Forge installer 不是有效的 JAR/ZIP 文件");

        // 4. 运行安装器（无头模式）
        var java = await JavaDetector.FindBestAsync(GameConstants.MinimumJavaMajorVersion)
                   ?? throw new InvalidOperationException("未找到满足条件的 Java 以运行 Forge 安装器");
        Logger?.Log($"运行 Forge 安装器（{java.MajorVersion}）...");

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
            ?? throw new InvalidOperationException("无法启动 Forge 安装器");
        var stdErr = await proc.StandardError.ReadToEndAsync();
        await proc.WaitForExitAsync(ct);

        if (proc.ExitCode != 0)
        {
            Logger?.Log($"Forge 无头安装返回 {proc.ExitCode}，如需可手动运行 installer 完成 GUI 安装。");
            Logger?.Log(stdErr);
        }

        // 5. 补全缺失依赖库
        var forgeJson = PathEx.VersionJsonPath(GameRoot, newId);
        if (File.Exists(forgeJson))
        {
            var v = JsonSerializer.Deserialize<VersionJson>(await File.ReadAllTextAsync(forgeJson, ct));
            if (v is not null) await DownloadLibrariesAsync(v, progress, ct);
        }
        else
        {
            Logger?.Log($"未找到 Forge 版本 JSON（{newId}），安装器可能未完成，请检查。");
        }

        Logger?.Log($"Forge {newId} 安装流程结束");
        return newId;
    }

    private async Task<string> ResolveBuildAsync(string mcVersion, CancellationToken ct)
    {
        var promoUrl = "https://files.minecraftforge.net/net/minecraftforge/forge/promotions_slim.json";
        var json = await Client.GetStringAsync(promoUrl, ct);
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("promos", out var promos))
        {
            if (promos.TryGetProperty($"{mcVersion}-recommended", out var r) && r.ValueKind == JsonValueKind.String)
                return r.GetString()!;
            if (promos.TryGetProperty($"{mcVersion}-latest", out var l) && l.ValueKind == JsonValueKind.String)
                return l.GetString()!;
        }
        throw new InvalidOperationException($"在 Forge promotions 中找不到 {mcVersion} 的可用构建");
    }
}
