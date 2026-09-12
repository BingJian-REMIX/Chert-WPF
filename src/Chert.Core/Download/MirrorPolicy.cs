using System;
using Chert.Core.Profiles;
using Chert.Core.Utils;

namespace Chert.Core.Download;

/// <summary>
/// 镜像源策略：BMCLAPI 优先，官方源回退。
/// 提供各资源的候选 URL 列表（按优先级排序）。
/// </summary>
public static class MirrorPolicy
{
    /// <summary>
    /// 下载源偏好（设置 → 下载）。由启动器在启动时 / 设置保存时从 profile 同步，
    /// 决定各候选 URL 中 BMCLAPI 与官方源的前后顺序。默认镜像优先，保持向后兼容。
    /// </summary>
    public static DownloadSourcePreference Preference { get; set; } = DownloadSourcePreference.MirrorFirst;

    /// <summary>按偏好返回 [首选, 回退] 顺序的候选对。</summary>
    private static IEnumerable<string> Order(string mirror, string official)
        => Preference == DownloadSourcePreference.OfficialFirst
            ? new[] { official, mirror }
            : new[] { mirror, official };

    /// <summary>版本清单候选 URL（按偏好决定 BMCLAPI / 官方先后）。</summary>
    public static IEnumerable<string> VersionManifestUrls()
        => Order(GameConstants.BmclapiVersionManifest, GameConstants.OfficialVersionManifest);

    /// <summary>
    /// 版本 JSON 候选 URL。官方源需要 manifest 中的 url，这里传入其官方地址作为回退。
    /// BMCLAPI 的正确路径为 <c>/version/{id}/json</c>；漏掉结尾的 <c>/json</c> 会 404（bug #20，已实测确认）。
    /// </summary>
    public static IEnumerable<string> VersionJsonUrls(string id, string? officialUrl = null)
    {
        var mirror = $"{GameConstants.BmclapiBase}/version/{id}/json";
        foreach (var u in Order(mirror, officialUrl ?? mirror))
            yield return u;
    }

    /// <summary>Library 候选 URL（path 为本地仓库相对路径）。</summary>
    public static IEnumerable<string> LibraryUrls(string path)
        => Order($"{GameConstants.BmclapiBase}/libraries/{path}", $"{GameConstants.OfficialLibrariesBase}/{path}");

    /// <summary>
    /// 资源对象候选 URL（hash 为资源 sha1）。
    /// BMCLAPI 与官方源都按 Mojang 约定以 hash 前两位分目录：<c>/{hash[0:2]}/{hash}</c>。
    /// 注意：BMCLAPI 的资源对象路径是 <c>/assets/{prefix}/{hash}</c>，
    /// 形如 <c>/assets/{hash}</c> 或 <c>/objects/{prefix}/{hash}</c> 的路径一律 404（已实测确认）。
    /// </summary>
    public static IEnumerable<string> AssetUrls(string hash)
    {
        var prefix = hash[..2];
        return Order(
            $"{GameConstants.BmclapiBase}/assets/{prefix}/{hash}",
            $"{GameConstants.OfficialAssetsBase}/{prefix}/{hash}");
    }

    /// <summary>
    /// 资源索引候选 URL。
    /// BMCLAPI 镜像官方资源索引需做<b>主机替换</b>（保留官方路径
    /// <c>/v1/packages/{sha1}/{id}.json</c>），而非 <c>/assets/indexes/{id}.json</c>
    /// （该路径实测恒 404）。例如官方
    /// <c>https://piston-meta.mojang.com/v1/packages/{sha1}/5.json</c>
    /// → BMCLAPI <c>https://bmclapi2.bangbang93.com/v1/packages/{sha1}/5.json</c>（实测 200）。
    /// </summary>
    public static IEnumerable<string> AssetIndexUrls(string officialUrl)
    {
        var mirror = ToBmclapiMirror(officialUrl);
        // 镜像优先时先镜像后官方；官方优先时先官方后镜像
        if (Preference == DownloadSourcePreference.OfficialFirst)
        {
            yield return officialUrl;
            if (mirror is not null) yield return mirror;
        }
        else
        {
            if (mirror is not null) yield return mirror;
            yield return officialUrl;
        }
    }

    /// <summary>
    /// 将官方 Mojang URL 转换为 BMCLAPI 镜像 URL（主机替换）。
    /// BMCLAPI 镜像官方源的方式是把 mojang 主机整体替换成 BMCLAPI 主机、路径不变。
    /// 无法识别的主机返回 null（调用方应回退官方 URL）。
    /// </summary>
    private static readonly string[] MojangHosts =
    {
        "piston-meta.mojang.com", "piston-data.mojang.com", "launcher.mojang.com",
        "resources.download.minecraft.net", "mc.resources.download.minecraft.net",
        "libraries.minecraft.net", "meta.mojang.com"
    };

    private static readonly string BmclapiHost = new Uri(GameConstants.BmclapiBase).Host;

    private static string? ToBmclapiMirror(string? officialUrl)
    {
        if (string.IsNullOrEmpty(officialUrl)) return null;
        foreach (var host in MojangHosts)
        {
            if (officialUrl.Contains(host, StringComparison.OrdinalIgnoreCase))
                return officialUrl.Replace(host, BmclapiHost, StringComparison.OrdinalIgnoreCase);
        }
        return null;
    }

    /// <summary>依次尝试候选 URL，返回首个成功的内容。全部失败抛异常。</summary>
    public static async Task<string> GetStringWithFallback(IEnumerable<string> urls, HttpClient client, CancellationToken ct = default)
    {
        var last = (Exception?)null;
        foreach (var url in urls)
        {
            try { return await client.GetStringAsync(url, ct); }
            catch (Exception ex) { last = ex; }
        }
        throw new HttpRequestException($"所有镜像源均失败：{string.Join(", ", urls)}", last);
    }

    /// <summary>依次尝试候选 URL 下载到流，返回首次成功的字节数组。</summary>
    public static async Task<byte[]> DownloadBytesWithFallback(IEnumerable<string> urls, HttpClient client, IProgress<double>? progress, CancellationToken ct)
    {
        var last = (Exception?)null;
        foreach (var url in urls)
        {
            try
            {
                using var resp = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
                resp.EnsureSuccessStatusCode();
                var total = resp.Content.Headers.ContentLength ?? -1L;
                await using var stream = await resp.Content.ReadAsStreamAsync(ct);
                using var ms = new MemoryStream();
                var buffer = new byte[8192];
                long read = 0;
                int n;
                while ((n = await stream.ReadAsync(buffer, ct)) > 0)
                {
                    await ms.WriteAsync(buffer.AsMemory(0, n), ct);
                    read += n;
                    if (total > 0) progress?.Report((double)read / total);
                }
                return ms.ToArray();
            }
            catch (Exception ex) { last = ex; }
        }
        throw new HttpRequestException($"所有镜像源均失败", last);
    }
}
