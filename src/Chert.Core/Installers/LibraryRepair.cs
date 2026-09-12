using Chert.Core.Download;
using Chert.Core.Launcher;
using Chert.Core.Models;
using Chert.Core.Utils;

namespace Chert.Core.Installers;

/// <summary>
/// 非破坏性依赖库修复：重新下载缺失或校验失败的库文件。
/// 仅重写 libraries 缓存中的依赖（缺失/损坏时覆盖），从不删除游戏原文件
/// （存档、配置、mod、版本 jar 等均不受影响）。
/// </summary>
public static class LibraryRepair
{
    /// <summary>
    /// 修复指定版本的依赖库。返回是否需要修复以及实际重新下载的数量。
    /// </summary>
    /// <param name="gameRoot">游戏根目录。</param>
    /// <param name="versionId">版本 id（将沿继承链合并依赖）。</param>
    /// <param name="client">HTTP 客户端（用于可能的清单回退）。</param>
    /// <param name="downloader">下载器。</param>
    /// <param name="logger">进度日志（可选）。</param>
    /// <param name="ct">取消令牌。</param>
    public static async Task<LibraryRepairResult> RepairAsync(string gameRoot,
        string versionId, HttpClient client, IDownloader downloader, ILogger? logger = null,
        CancellationToken ct = default)
    {
        var result = new LibraryRepairResult();

        VersionJson merged;
        try
        {
            merged = VersionMerger.Merge(gameRoot, versionId);
        }
        catch (Exception ex)
        {
            logger?.Log($"无法合并版本 {versionId} 以修复库：{ex.Message}");
            result.Error = ex.Message;
            return result;
        }

        var items = InstallerBase.BuildLibraryDownloads(merged, gameRoot);

        // 仅挑选缺失或校验失败的库重新下载
        var toFix = new List<DownloadItem>();
        foreach (var item in items)
        {
            if (!File.Exists(item.Destination))
            {
                toFix.Add(item);
                continue;
            }
            if (!HashUtil.VerifySha1(item.Destination, item.ExpectedSha1)
                || !HashUtil.VerifySize(item.Destination, item.ExpectedSize))
            {
                logger?.Log($"库校验失败，将重新下载：{Path.GetFileName(item.Destination)}");
                toFix.Add(item);
            }
        }

        result.TotalLibraries = items.Count;
        result.FixedCount = toFix.Count;

        if (toFix.Count == 0)
        {
            logger?.Log("所有依赖库均已完整，无需修复。");
            result.AllHealthy = true;
            return result;
        }

        logger?.Log($"开始重新下载 {toFix.Count} 个缺失/损坏的依赖库 …");
        try
        {
            await downloader.DownloadBatchAsync(toFix, null, ct);
            result.Success = true;
            logger?.Log("依赖库修复完成。");
        }
        catch (Exception ex)
        {
            logger?.Log($"依赖库修复失败：{ex.Message}");
            result.Error = ex.Message;
        }

        return result;
    }
}

/// <summary>库修复结果。</summary>
public class LibraryRepairResult
{
    /// <summary>版本依赖总数。</summary>
    public int TotalLibraries { get; set; }

    /// <summary>实际需要重新下载的数量。</summary>
    public int FixedCount { get; set; }

    /// <summary>所有库均健康（无需下载）。</summary>
    public bool AllHealthy { get; set; }

    /// <summary>修复下载是否成功。</summary>
    public bool Success { get; set; }

    /// <summary>错误信息（成功或无需修复时为空）。</summary>
    public string? Error { get; set; }
}
