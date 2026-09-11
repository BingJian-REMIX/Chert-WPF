using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace MCLCS.Core.Utils;

/// <summary>
/// 外联图标 / 封面图缓存。
/// <para>
/// 下载页（规格 2.2）的卡片封面来自外部 URL：Modrinth <c>icon_url</c>、像素茶艺 <c>preview_image</c> 等。
/// 本类负责把它们下载到本地缓存目录并复用，避免重复联网、支持离线回退。
/// </para>
/// <para>
/// 这是「外联 icon 文件」的统一落盘位置——任何未来的外部图标（皮肤、画廊图、整合包封面等）都可走这里，
/// 调用方只需传入 URL，缓存键、并发、超时、回退都由本类处理。
/// </para>
/// </summary>
public static class IconCache
{
    /// <summary>外联图标缓存根目录（%LocalAppData%/MCLCS/cache/icons）。</summary>
    public static string CacheRoot { get; } =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Chert", "cache", "icons");

    /// <summary>根据 URL 计算缓存文件名（sha256 十六进制 + 推测扩展名）。</summary>
    public static string CacheFileFor(string url)
    {
        var ext = "";
        try
        {
            ext = Path.GetExtension(new Uri(url, UriKind.Absolute).AbsolutePath);
        }
        catch
        {
            ext = "";
        }

        if (string.IsNullOrWhiteSpace(ext) || ext.Length > 5 || ext.IndexOf('.') != 0)
            ext = ".img";

        using var sha = SHA256.Create();
        var hash = BitConverter
            .ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(url)))
            .Replace("-", "", StringComparison.Ordinal)
            .ToLowerInvariant();
        return hash + ext;
    }

    /// <summary>取缓存文件完整路径（不保证已下载）。</summary>
    public static string PathFor(string url) => Path.Combine(CacheRoot, CacheFileFor(url));

    /// <summary>
    /// 取本地缓存路径：已缓存且非空则直接返回；否则用给定 <paramref name="client"/> 下载并落盘（带 20s 超时）。
    /// 任意失败（网络/解码/写盘）返回 <c>null</c>，调用方应显示占位图。
    /// </summary>
    public static async Task<string?> GetOrDownloadAsync(string? url, HttpClient client,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        try
        {
            var path = PathFor(url);
            if (File.Exists(path) && new FileInfo(path).Length > 0)
                return path;

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            linked.CancelAfter(TimeSpan.FromSeconds(20));

            var bytes = await client.GetByteArrayAsync(url, linked.Token).ConfigureAwait(false);
            if (bytes.Length == 0) return null;

            Directory.CreateDirectory(CacheRoot);
            await File.WriteAllBytesAsync(path, bytes, linked.Token).ConfigureAwait(false);
            return path;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>清空整个图标缓存（供设置页「清理缓存」调用）。</summary>
    public static void Clear()
    {
        try
        {
            if (Directory.Exists(CacheRoot))
                Directory.Delete(CacheRoot, true);
        }
        catch
        {
            // 忽略清理过程中的错误
        }
    }
}
