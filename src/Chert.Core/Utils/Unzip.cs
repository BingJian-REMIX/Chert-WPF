using System.IO.Compression;

namespace Chert.Core.Utils;

/// <summary>ZIP 解压工具（用于 natives 等）。</summary>
public static class Unzip
{
    /// <summary>解压 zip 到目标目录，可排除指定前缀的文件（如 META-INF/）。</summary>
    public static void ExtractToDirectory(string zipPath, string destDir, IEnumerable<string>? exclude = null)
    {
        Directory.CreateDirectory(destDir);
        var excludes = exclude?.Select(e => e.TrimEnd('/')).ToHashSet(StringComparer.OrdinalIgnoreCase)
                       ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var entry in archive.Entries)
        {
            var fullName = entry.FullName.TrimStart('/');
            if (string.IsNullOrEmpty(fullName)) continue;
            if (excludes.Any(e => fullName.StartsWith(e + "/", StringComparison.OrdinalIgnoreCase)
                               || fullName.Equals(e, StringComparison.OrdinalIgnoreCase)))
                continue;

            var outPath = Path.Combine(destDir, fullName);
            var outDir = Path.GetDirectoryName(outPath);
            if (outDir is not null) Directory.CreateDirectory(outDir);

            // 防御 zip 穿越
            if (!outPath.StartsWith(Path.GetFullPath(destDir), StringComparison.OrdinalIgnoreCase))
                continue;

            if (entry.FullName.EndsWith("/")) continue;
            entry.ExtractToFile(outPath, overwrite: true);
        }
    }
}
