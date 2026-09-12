using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Chert.Core.Resources;

/// <summary>缓存中的一份服务器资源包。</summary>
public class CachedServerPack
{
    /// <summary>缓存键（URL 的 SHA-1）。</summary>
    [JsonPropertyName("key")] public string Key { get; set; } = "";

    [JsonPropertyName("url")] public string Url { get; set; } = "";

    /// <summary>服务端声明的 SHA-1（若有）。</summary>
    [JsonPropertyName("declaredSha1")] public string? DeclaredSha1 { get; set; }

    /// <summary>本地文件名（相对缓存目录）。</summary>
    [JsonPropertyName("file")] public string FileName { get; set; } = "";

    [JsonPropertyName("sizeBytes")] public long SizeBytes { get; set; }
    [JsonPropertyName("cachedAt")] public DateTime CachedAt { get; set; } = DateTime.Now;
    [JsonPropertyName("lastUsed")] public DateTime LastUsed { get; set; } = DateTime.Now;
    [JsonPropertyName("hits")] public int Hits { get; set; }

    /// <summary>来源服务器地址（便于界面展示"哪个服的包"）。</summary>
    [JsonPropertyName("server")] public string? ServerAddress { get; set; }

    public double SizeMb => Math.Round(SizeBytes / 1024.0 / 1024, 1);
}

/// <summary>缓存统计。</summary>
public sealed class PackCacheStats
{
    public int Count { get; init; }
    public long TotalBytes { get; init; }
    public int TotalHits { get; init; }
    public double TotalMb => Math.Round(TotalBytes / 1024.0 / 1024, 1);
}

/// <summary>
/// 服务器资源包缓存（全局功能）：进服时下载的资源包按 URL 缓存到本地，
/// 下次进同一台服直接命中，省掉重复下载。容量超限时按 LRU 淘汰。
/// </summary>
public static class ServerResourcePackCache
{
    public const string DirName = "server-resource-packs";
    public const string IndexFileName = "index.json";

    /// <summary>默认容量上限（MB）。</summary>
    public const int DefaultCapacityMb = 2048;

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static string CacheDir(string gameRoot) => Path.Combine(gameRoot, DirName);

    public static string IndexPath(string gameRoot) => Path.Combine(CacheDir(gameRoot), IndexFileName);

    /// <summary>URL → 缓存键（SHA-1，40 位小写十六进制）。与原版命名规则一致。</summary>
    public static string KeyOf(string url)
    {
        var bytes = SHA1.HashData(Encoding.UTF8.GetBytes(url ?? ""));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>读取索引。</summary>
    public static List<CachedServerPack> LoadIndex(string gameRoot)
    {
        try
        {
            var p = IndexPath(gameRoot);
            if (!File.Exists(p)) return new List<CachedServerPack>();
            return JsonSerializer.Deserialize<List<CachedServerPack>>(File.ReadAllText(p))
                   ?? new List<CachedServerPack>();
        }
        catch
        {
            return new List<CachedServerPack>();
        }
    }

    /// <summary>写入索引。</summary>
    public static bool SaveIndex(string gameRoot, List<CachedServerPack> index)
    {
        try
        {
            Directory.CreateDirectory(CacheDir(gameRoot));
            File.WriteAllText(IndexPath(gameRoot), JsonSerializer.Serialize(index, JsonOpts));
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>查询缓存；命中时更新 LastUsed / Hits 并返回本地路径，未命中返回 null。</summary>
    public static string? TryHit(string gameRoot, string url)
    {
        var key = KeyOf(url);
        var index = LoadIndex(gameRoot);
        var entry = index.FirstOrDefault(e => e.Key == key);
        if (entry is null) return null;

        var path = Path.Combine(CacheDir(gameRoot), entry.FileName);
        if (!File.Exists(path))
        {
            index.Remove(entry);
            SaveIndex(gameRoot, index);
            return null;
        }

        entry.Hits++;
        entry.LastUsed = DateTime.Now;
        SaveIndex(gameRoot, index);
        return path;
    }

    /// <summary>把已下载的文件放入缓存（移动而非复制）。</summary>
    public static CachedServerPack? Put(
        string gameRoot, string url, string sourceFile,
        string? declaredSha1 = null, string? serverAddress = null)
    {
        if (!File.Exists(sourceFile)) return null;

        try
        {
            var dir = CacheDir(gameRoot);
            Directory.CreateDirectory(dir);

            var key = KeyOf(url);
            var fileName = key + ".zip";
            var dest = Path.Combine(dir, fileName);
            if (File.Exists(dest)) File.Delete(dest);
            File.Move(sourceFile, dest);

            var index = LoadIndex(gameRoot);
            index.RemoveAll(e => e.Key == key);

            var entry = new CachedServerPack
            {
                Key = key,
                Url = url,
                DeclaredSha1 = declaredSha1,
                FileName = fileName,
                SizeBytes = new FileInfo(dest).Length,
                ServerAddress = serverAddress
            };
            index.Add(entry);
            SaveIndex(gameRoot, index);
            return entry;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>校验缓存文件的 SHA-1 是否与服务端声明一致（未声明视为通过）。</summary>
    public static bool Verify(string gameRoot, CachedServerPack entry)
    {
        if (string.IsNullOrWhiteSpace(entry.DeclaredSha1)) return true;
        try
        {
            var path = Path.Combine(CacheDir(gameRoot), entry.FileName);
            if (!File.Exists(path)) return false;

            using var fs = File.OpenRead(path);
            using var sha = SHA1.Create();
            var actual = Convert.ToHexString(sha.ComputeHash(fs)).ToLowerInvariant();
            return string.Equals(actual, entry.DeclaredSha1!.Trim(), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 按 LRU 选出需要淘汰的条目，使总大小不超过容量上限（纯函数）。
    /// </summary>
    public static List<CachedServerPack> SelectEvictions(IEnumerable<CachedServerPack> index, int capacityMb)
    {
        var capacity = capacityMb * 1024L * 1024;
        var ordered = index.OrderByDescending(e => e.LastUsed).ToList();

        var evict = new List<CachedServerPack>();
        long running = 0;
        foreach (var e in ordered)
        {
            running += e.SizeBytes;
            if (running > capacity) evict.Add(e);
        }
        return evict;
    }

    /// <summary>执行 LRU 淘汰，返回删除的份数。</summary>
    public static int Evict(string gameRoot, int capacityMb = DefaultCapacityMb)
    {
        var index = LoadIndex(gameRoot);
        var evict = SelectEvictions(index, capacityMb);
        if (evict.Count == 0) return 0;

        foreach (var e in evict)
        {
            try
            {
                var p = Path.Combine(CacheDir(gameRoot), e.FileName);
                if (File.Exists(p)) File.Delete(p);
            }
            catch
            {
                // 占用中：索引先移除，文件下次清
            }
            index.Remove(e);
        }
        SaveIndex(gameRoot, index);
        return evict.Count;
    }

    /// <summary>
    /// 缓存文件在磁盘上的完整路径（不校验存在性）。
    /// </summary>
    public static string FilePathOf(string gameRoot, CachedServerPack entry) =>
        Path.Combine(CacheDir(gameRoot), entry.FileName);

    /// <summary>
    /// 为缓存条目生成一个人类可读的导出文件名。
    /// 缓存里落盘的是 40 位 SHA-1，直接导出用户根本认不出来是哪个服的包，
    /// 所以这里用「服务器地址 + URL 末段」重建，非法字符替换为下划线。
    /// </summary>
    public static string SuggestExportName(CachedServerPack entry)
    {
        var stem = "";
        try
        {
            if (!string.IsNullOrWhiteSpace(entry.Url))
            {
                var last = entry.Url.Split('?')[0].TrimEnd('/');
                var idx = last.LastIndexOf('/');
                if (idx >= 0 && idx < last.Length - 1) last = last[(idx + 1)..];
                if (last.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) last = last[..^4];
                stem = last;
            }
        }
        catch { /* 忽略畸形 URL */ }

        var server = Sanitize(entry.ServerAddress ?? "");
        stem = Sanitize(stem);
        if (string.IsNullOrWhiteSpace(stem)) stem = entry.Key.Length >= 8 ? entry.Key[..8] : entry.Key;

        var name = string.IsNullOrWhiteSpace(server) ? stem : $"{server}-{stem}";
        if (name.Length > 96) name = name[..96];
        return name + ".zip";
    }

    private static string Sanitize(string s)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = s.Select(c => invalid.Contains(c) || c is ':' or '/' or '\\' ? '_' : c).ToArray();
        return new string(chars).Trim('_', ' ', '.');
    }

    /// <summary>
    /// 把一份缓存资源包导出（复制）到目标目录，返回导出后的完整路径；失败返回 null。
    /// 同名文件自动追加 (2)(3) 序号，不覆盖用户已有文件。
    /// </summary>
    public static string? Export(string gameRoot, CachedServerPack entry, string destDir, string? fileName = null)
    {
        try
        {
            var src = FilePathOf(gameRoot, entry);
            if (!File.Exists(src)) return null;

            Directory.CreateDirectory(destDir);
            var name = string.IsNullOrWhiteSpace(fileName) ? SuggestExportName(entry) : fileName!;
            var dest = Path.Combine(destDir, name);

            var stem = Path.GetFileNameWithoutExtension(name);
            var ext = Path.GetExtension(name);
            var n = 2;
            while (File.Exists(dest))
            {
                dest = Path.Combine(destDir, $"{stem} ({n}){ext}");
                n++;
            }

            File.Copy(src, dest);
            return dest;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>导出到游戏的 resourcepacks 目录（导出后即可在游戏里直接启用）。</summary>
    public static string? ExportToResourcePacks(string gameRoot, CachedServerPack entry) =>
        Export(gameRoot, entry, Path.Combine(gameRoot, "resourcepacks"));

    /// <summary>删除单份缓存（文件 + 索引项），返回是否成功。</summary>
    public static bool Remove(string gameRoot, string key)
    {
        var index = LoadIndex(gameRoot);
        var entry = index.FirstOrDefault(e => e.Key == key);
        if (entry is null) return false;

        try
        {
            var p = FilePathOf(gameRoot, entry);
            if (File.Exists(p)) File.Delete(p);
        }
        catch
        {
            // 占用中：索引先移除，文件留待 Clear 时清理
        }

        index.Remove(entry);
        SaveIndex(gameRoot, index);
        return true;
    }

    /// <summary>清空缓存，返回删除的份数。</summary>
    public static int Clear(string gameRoot)
    {
        var index = LoadIndex(gameRoot);
        var n = index.Count;
        foreach (var e in index)
        {
            try
            {
                var p = Path.Combine(CacheDir(gameRoot), e.FileName);
                if (File.Exists(p)) File.Delete(p);
            }
            catch { /* ignore */ }
        }
        SaveIndex(gameRoot, new List<CachedServerPack>());
        return n;
    }

    /// <summary>统计信息。</summary>
    public static PackCacheStats Stats(string gameRoot)
    {
        var index = LoadIndex(gameRoot);
        return new PackCacheStats
        {
            Count = index.Count,
            TotalBytes = index.Sum(e => e.SizeBytes),
            TotalHits = index.Sum(e => e.Hits)
        };
    }
}
