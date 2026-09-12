using System.Text.Json;
using Chert.Core.Utils;

namespace Chert.Core.Toolbox;

/// <summary>一个待清理的冗余文件。</summary>
public class RedundantFile
{
    public string FullPath { get; set; } = "";
    public long SizeBytes { get; set; }
    public string RelativePath { get; set; } = "";
}

/// <summary>
/// 清理冗余文件（工具箱功能 8）：扫描未被任何已安装版本引用的
/// <c>libraries/</c> 与 <c>assets/</c> 文件，列出并可清理（默认移入回收目录，不直接删除）。
/// </summary>
public static class RedundantFileCleaner
{
    /// <summary>计算所有已安装版本引用的库与资源文件路径集合（相对 gameRoot，归一化）。</summary>
    public static HashSet<string> ComputeReferencedPaths(string gameRoot)
    {
        var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var versionsDir = PathEx.VersionsDir(gameRoot);
        if (!Directory.Exists(versionsDir)) return referenced;

        foreach (var dir in Directory.GetDirectories(versionsDir))
        {
            var id = Path.GetFileName(dir);
            var jsonPath = PathEx.VersionJsonPath(gameRoot, id);
            if (!File.Exists(jsonPath)) continue;

            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(jsonPath));
                var root = doc.RootElement;

                // 库文件
                if (root.TryGetProperty("libraries", out var libs) && libs.ValueKind == JsonValueKind.Array)
                {
                    foreach (var lib in libs.EnumerateArray())
                    {
                        var path = LibraryArtifactPath(lib);
                        if (path is not null)
                            referenced.Add(Normalize(Path.Combine(gameRoot, path)));
                    }
                }

                // 资源索引
                string? assetId = null;
                if (root.TryGetProperty("assetIndex", out var ai) && ai.TryGetProperty("id", out var aid))
                    assetId = aid.GetString();
                else if (root.TryGetProperty("assets", out var a) && a.ValueKind == JsonValueKind.String)
                    assetId = a.GetString();

                if (!string.IsNullOrEmpty(assetId))
                    AddAssetObjects(gameRoot, assetId!, referenced);
            }
            catch
            {
                /* 忽略单个版本解析失败 */
            }
        }
        return referenced;
    }

    private static string? LibraryArtifactPath(JsonElement lib)
    {
        // 优先 downloads.artifact.path
        if (lib.TryGetProperty("downloads", out var dl) && dl.ValueKind == JsonValueKind.Object
            && dl.TryGetProperty("artifact", out var art) && art.TryGetProperty("path", out var p))
            return p.GetString();

        // 退而用 name（group:artifact:version[:classifier]）推导
        if (lib.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String)
        {
            var parts = name.GetString()!.Split(':');
            if (parts.Length >= 3)
            {
                var grp = parts[0].Replace('.', '/');
                var art2 = parts[1];
                var ver = parts[2];
                var fileName = parts.Length >= 4
                    ? $"{art2}-{ver}-{parts[3]}.jar"
                    : $"{art2}-{ver}.jar";
                return Path.Combine("libraries", grp, art2, ver, fileName);
            }
        }
        return null;
    }

    private static void AddAssetObjects(string gameRoot, string assetId, HashSet<string> referenced)
    {
        var indexPath = Path.Combine(PathEx.AssetsIndexDir(gameRoot), assetId + ".json");
        if (!File.Exists(indexPath)) return;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(indexPath));
            if (doc.RootElement.TryGetProperty("objects", out var objs) && objs.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in objs.EnumerateObject())
                {
                    if (prop.Value.TryGetProperty("hash", out var h))
                    {
                        var hash = h.GetString();
                        if (!string.IsNullOrEmpty(hash))
                            referenced.Add(Normalize(PathEx.AssetObjectPath(gameRoot, hash)));
                    }
                }
            }
        }
        catch { /* 忽略 */ }
    }

    /// <summary>扫描未被引用的库/资源文件。</summary>
    public static List<RedundantFile> Scan(string gameRoot)
    {
        var referenced = ComputeReferencedPaths(gameRoot);
        var result = new List<RedundantFile>();

        void Walk(string dir)
        {
            if (!Directory.Exists(dir)) return;
            foreach (var file in Directory.GetFiles(dir))
            {
                var norm = Normalize(file);
                if (!referenced.Contains(norm))
                    result.Add(new RedundantFile
                    {
                        FullPath = file,
                        SizeBytes = new FileInfo(file).Length,
                        RelativePath = Relative(gameRoot, file)
                    });
            }
            foreach (var sub in Directory.GetDirectories(dir)) Walk(sub);
        }

        Walk(PathEx.LibrariesDir(gameRoot));
        Walk(PathEx.AssetsObjectsDir(gameRoot));
        return result;
    }

    /// <summary>
    /// 清理冗余文件。默认移入 <c>gameRoot/mclcs_redundant_trash/</c>（可还原）；
    /// <paramref name="deleteDirectly"/> 为 true 时直接删除。
    /// </summary>
    public static int Clean(IEnumerable<RedundantFile> files, string gameRoot, bool deleteDirectly = false)
    {
        var trash = Path.Combine(gameRoot, "mclcs_redundant_trash");
        if (!deleteDirectly) Directory.CreateDirectory(trash);

        var ok = 0;
        foreach (var f in files)
        {
            try
            {
                if (!File.Exists(f.FullPath)) continue;
                if (deleteDirectly)
                {
                    File.Delete(f.FullPath);
                }
                else
                {
                    var dest = Path.Combine(trash, f.RelativePath);
                    Directory.CreateDirectory(Path.GetDirectoryName(dest) ?? trash);
                    if (File.Exists(dest)) File.Delete(dest);
                    File.Move(f.FullPath, dest);
                }
                ok++;
            }
            catch { /* 忽略单个失败 */ }
        }
        return ok;
    }

    private static string Normalize(string p) =>
        p.Replace('\\', '/').ToLowerInvariant();

    private static string Relative(string gameRoot, string file) =>
        Normalize(file).StartsWith(Normalize(gameRoot))
            ? Normalize(file)[Normalize(gameRoot).Length..].TrimStart('/')
            : Path.GetFileName(file);
}
