using System.IO.Compression;

namespace Chert.Core.Download;

/// <summary>地图安装结果。</summary>
public sealed class MapInstallResult
{
    public bool Ok { get; init; }
    public string? Error { get; init; }

    /// <summary>安装后的存档目录。</summary>
    public string? SaveDir { get; init; }

    /// <summary>存档名（saves 下的文件夹名）。</summary>
    public string? SaveName { get; init; }

    /// <summary>zip 内 level.dat 所在的目录前缀（"" 表示在根）。</summary>
    public string RootPrefix { get; init; } = "";

    /// <summary>是否因重名而自动改名。</summary>
    public bool Renamed { get; init; }

    public static MapInstallResult Fail(string error) => new() { Ok = false, Error = error };
}

/// <summary>
/// 地图安装器：把下载到的地图压缩包解压进 <c>.minecraft/saves</c>。
/// 自动识别 zip 内的存档根（有些包多套了一层目录），并处理重名。
/// </summary>
public static class MapInstaller
{
    /// <summary>存档目录。</summary>
    public static string SavesDir(string gameRoot) => Path.Combine(gameRoot, "saves");

    /// <summary>
    /// 在条目名列表中定位存档根：返回含 <c>level.dat</c> 的最浅层目录前缀（根为 ""）。
    /// 找不到返回 null。
    /// </summary>
    public static string? DetectRootPrefix(IEnumerable<string> entryNames)
    {
        string? best = null;
        var bestDepth = int.MaxValue;

        foreach (var raw in entryNames)
        {
            var name = raw.Replace('\\', '/').TrimStart('/');
            if (!name.EndsWith("level.dat", StringComparison.OrdinalIgnoreCase)) continue;

            var idx = name.LastIndexOf('/');
            var prefix = idx < 0 ? "" : name[..(idx + 1)];
            var depth = prefix.Length == 0 ? 0 : prefix.Count(c => c == '/');
            if (depth >= bestDepth) continue;

            bestDepth = depth;
            best = prefix;
        }
        return best;
    }

    /// <summary>把候选名转成合法的存档文件夹名。</summary>
    public static string SafeSaveName(string? name)
    {
        var s = (name ?? "").Trim();
        if (s.Length == 0) s = "新地图";
        foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        s = s.Trim('.', ' ');
        if (s.Length == 0) s = "新地图";
        return s.Length > 64 ? s[..64] : s;
    }

    /// <summary>重名时追加 " (2)"、" (3)" …，最多尝试 999 次。</summary>
    public static string ResolveConflict(string savesDir, string name, out bool renamed)
    {
        renamed = false;
        var target = Path.Combine(savesDir, name);
        if (!Directory.Exists(target)) return name;

        for (var i = 2; i < 1000; i++)
        {
            var candidate = $"{name} ({i})";
            if (Directory.Exists(Path.Combine(savesDir, candidate))) continue;
            renamed = true;
            return candidate;
        }
        renamed = true;
        return $"{name} ({DateTime.Now:yyyyMMddHHmmss})";
    }

    /// <summary>
    /// 安装地图压缩包。<paramref name="preferredName"/> 为空时使用 zip 内目录名 / 文件名。
    /// </summary>
    public static MapInstallResult Install(string zipPath, string gameRoot, string? preferredName = null)
    {
        if (!File.Exists(zipPath)) return MapInstallResult.Fail("压缩包不存在");

        try
        {
            using var archive = ZipFile.OpenRead(zipPath);
            var names = archive.Entries.Select(e => e.FullName).ToList();

            var prefix = DetectRootPrefix(names);
            if (prefix is null) return MapInstallResult.Fail("压缩包内未找到 level.dat，可能不是存档地图");

            var savesDir = SavesDir(gameRoot);
            Directory.CreateDirectory(savesDir);

            var baseName = preferredName;
            if (string.IsNullOrWhiteSpace(baseName))
            {
                baseName = prefix.Length > 0
                    ? prefix.TrimEnd('/').Split('/').Last()
                    : Path.GetFileNameWithoutExtension(zipPath);
            }

            var saveName = ResolveConflict(savesDir, SafeSaveName(baseName), out var renamed);
            var destRoot = Path.Combine(savesDir, saveName);
            var destFull = Path.GetFullPath(destRoot);
            Directory.CreateDirectory(destRoot);

            foreach (var entry in archive.Entries)
            {
                var full = entry.FullName.Replace('\\', '/').TrimStart('/');
                if (full.Length == 0) continue;
                if (prefix.Length > 0)
                {
                    if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                    full = full[prefix.Length..];
                }
                if (full.Length == 0 || full.EndsWith('/')) continue;

                var outPath = Path.GetFullPath(Path.Combine(destRoot, full));
                if (!outPath.StartsWith(destFull, StringComparison.OrdinalIgnoreCase)) continue;  // 防 zip 穿越

                var outDir = Path.GetDirectoryName(outPath);
                if (outDir is not null) Directory.CreateDirectory(outDir);
                entry.ExtractToFile(outPath, overwrite: true);
            }

            if (!File.Exists(Path.Combine(destRoot, "level.dat")))
                return MapInstallResult.Fail("解压后缺少 level.dat");

            return new MapInstallResult
            {
                Ok = true,
                SaveDir = destRoot,
                SaveName = saveName,
                RootPrefix = prefix,
                Renamed = renamed
            };
        }
        catch (Exception ex)
        {
            return MapInstallResult.Fail(ex.Message);
        }
    }
}
