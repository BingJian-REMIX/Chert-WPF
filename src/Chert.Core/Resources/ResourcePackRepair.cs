using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace Chert.Core.Resources;

/// <summary>资源包问题类型。</summary>
public enum PackIssueKind
{
    /// <summary>缺少 pack.mcmeta。</summary>
    MissingMeta,
    /// <summary>pack.mcmeta 不是合法 JSON。</summary>
    BrokenMeta,
    /// <summary>pack_format 与目标游戏版本不匹配。</summary>
    FormatMismatch,
    /// <summary>zip 内多套了一层目录（assets 不在根）。</summary>
    NestedRoot,
    /// <summary>既没有 assets/ 也没有 pack.png，疑似不是资源包。</summary>
    NotAResourcePack
}

/// <summary>一条问题。</summary>
public sealed class PackIssue
{
    public PackIssue(PackIssueKind kind, string message, bool repairable)
    {
        Kind = kind;
        Message = message;
        Repairable = repairable;
    }

    public PackIssueKind Kind { get; }
    public string Message { get; }

    /// <summary>是否可自动修复。</summary>
    public bool Repairable { get; }

    public override string ToString() => $"[{Kind}] {Message}";
}

/// <summary>资源包检查结果。</summary>
public sealed class PackDiagnosis
{
    public string Path { get; init; } = "";
    public bool IsZip { get; init; }
    public int PackFormat { get; init; }
    public string? Description { get; init; }

    /// <summary>zip 内 assets 所在的目录前缀（"" 表示在根）。</summary>
    public string RootPrefix { get; init; } = "";

    public List<PackIssue> Issues { get; } = new();

    public bool Healthy => Issues.Count == 0;
    public bool CanAutoRepair => Issues.Count > 0 && Issues.All(i => i.Repairable);

    public string Summary => Healthy
        ? "未发现问题"
        : $"发现 {Issues.Count} 个问题：{string.Join("；", Issues.Select(i => i.Message))}";
}

/// <summary>修复结果。</summary>
public sealed class PackRepairResult
{
    public bool Ok { get; init; }
    public string? Error { get; init; }

    /// <summary>已修复的问题。</summary>
    public List<PackIssueKind> Repaired { get; init; } = new();

    /// <summary>修复前的备份路径。</summary>
    public string? BackupPath { get; init; }

    public static PackRepairResult Fail(string error) => new() { Ok = false, Error = error };
}

/// <summary>
/// 资源包自动修复（全局功能）：检测 <c>resourcepacks/</c> 下的包是否缺 <c>pack.mcmeta</c>、
/// pack_format 是否与当前版本匹配、zip 是否多套了一层目录，并可一键修复。
/// </summary>
public static class ResourcePackRepair
{
    /// <summary>游戏版本 → 资源包 pack_format。</summary>
    public static readonly Dictionary<string, int> ResourceFormats = new()
    {
        ["1.16"] = 6, ["1.17"] = 7, ["1.18"] = 8, ["1.19"] = 9,
        ["1.19.3"] = 12, ["1.19.4"] = 13, ["1.20"] = 15, ["1.20.2"] = 18,
        ["1.20.3"] = 22, ["1.20.5"] = 32, ["1.21"] = 34
    };

    public static string ResourcePacksDir(string gameRoot) => Path.Combine(gameRoot, "resourcepacks");

    /// <summary>按游戏版本推断期望的 pack_format；未知返回 0。</summary>
    public static int ExpectedFormat(string? version)
    {
        if (string.IsNullOrWhiteSpace(version)) return 0;
        if (ResourceFormats.TryGetValue(version!, out var exact)) return exact;

        var parts = version!.Split('.');
        if (parts.Length >= 3 && ResourceFormats.TryGetValue($"{parts[0]}.{parts[1]}.{parts[2]}", out var p3))
            return p3;
        if (parts.Length >= 2 && ResourceFormats.TryGetValue($"{parts[0]}.{parts[1]}", out var p2))
            return p2;
        return 0;
    }

    /// <summary>生成一份标准 pack.mcmeta 内容。</summary>
    public static string BuildMeta(int packFormat, string description) =>
        JsonSerializer.Serialize(new
        {
            pack = new { pack_format = packFormat, description }
        }, new JsonSerializerOptions { WriteIndented = true });

    /// <summary>解析 pack.mcmeta；返回 (是否合法, format, description)。</summary>
    public static (bool Valid, int Format, string? Description) ParseMeta(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return (false, 0, null);
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("pack", out var pack)) return (false, 0, null);

            var format = pack.TryGetProperty("pack_format", out var pf) && pf.TryGetInt32(out var f) ? f : 0;
            string? desc = null;
            if (pack.TryGetProperty("description", out var d))
                desc = d.ValueKind == JsonValueKind.String ? d.GetString() : d.ToString();

            return (true, format, desc);
        }
        catch
        {
            return (false, 0, null);
        }
    }

    /// <summary>检查单个资源包（目录或 zip）。</summary>
    public static PackDiagnosis Diagnose(string path, string? targetVersion = null)
    {
        var isZip = File.Exists(path) && path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
        var names = new List<string>();
        string? metaText = null;

        if (isZip)
        {
            try
            {
                using var archive = ZipFile.OpenRead(path);
                names.AddRange(archive.Entries.Select(e => e.FullName.Replace('\\', '/')));
                var prefix0 = DetectRootPrefix(names);
                var metaEntry = archive.GetEntry((prefix0 ?? "") + "pack.mcmeta");
                if (metaEntry is not null)
                {
                    using var reader = new StreamReader(metaEntry.Open());
                    metaText = reader.ReadToEnd();
                }
            }
            catch (Exception ex)
            {
                var broken = new PackDiagnosis { Path = path, IsZip = true };
                broken.Issues.Add(new PackIssue(PackIssueKind.NotAResourcePack, $"压缩包无法读取：{ex.Message}", false));
                return broken;
            }
        }
        else if (Directory.Exists(path))
        {
            try
            {
                foreach (var f in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                    names.Add(Path.GetRelativePath(path, f).Replace('\\', '/'));
            }
            catch
            {
                // 部分不可读：用已收集的
            }
            var metaPath = Path.Combine(path, "pack.mcmeta");
            if (File.Exists(metaPath))
            {
                try { metaText = File.ReadAllText(metaPath); } catch { /* ignore */ }
            }
        }
        else
        {
            var missing = new PackDiagnosis { Path = path };
            missing.Issues.Add(new PackIssue(PackIssueKind.NotAResourcePack, "路径不存在", false));
            return missing;
        }

        var prefix = DetectRootPrefix(names) ?? "";
        var (valid, format, desc) = ParseMeta(metaText);

        var diag = new PackDiagnosis
        {
            Path = path,
            IsZip = isZip,
            PackFormat = format,
            Description = desc,
            RootPrefix = prefix
        };

        var hasAssets = names.Any(n => n.StartsWith(prefix + "assets/", StringComparison.OrdinalIgnoreCase));
        if (!hasAssets && !names.Any(n => n.EndsWith("pack.png", StringComparison.OrdinalIgnoreCase)))
            diag.Issues.Add(new PackIssue(PackIssueKind.NotAResourcePack, "未找到 assets/ 目录", false));

        if (metaText is null)
            diag.Issues.Add(new PackIssue(PackIssueKind.MissingMeta, "缺少 pack.mcmeta", true));
        else if (!valid)
            diag.Issues.Add(new PackIssue(PackIssueKind.BrokenMeta, "pack.mcmeta 不是合法 JSON", true));

        if (prefix.Length > 0)
            diag.Issues.Add(new PackIssue(PackIssueKind.NestedRoot, $"内容多套了一层目录：{prefix}", true));

        var expected = ExpectedFormat(targetVersion);
        if (expected > 0 && format > 0 && format != expected)
            diag.Issues.Add(new PackIssue(PackIssueKind.FormatMismatch,
                $"pack_format={format}，{targetVersion} 期望 {expected}", true));

        return diag;
    }

    /// <summary>扫描 resourcepacks 目录下的所有包。</summary>
    public static List<PackDiagnosis> DiagnoseAll(string gameRoot, string? targetVersion = null)
    {
        var dir = ResourcePacksDir(gameRoot);
        var list = new List<PackDiagnosis>();
        if (!Directory.Exists(dir)) return list;

        foreach (var entry in Directory.GetFileSystemEntries(dir).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            if (File.Exists(entry) && !entry.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) continue;
            list.Add(Diagnose(entry, targetVersion));
        }
        return list;
    }

    /// <summary>
    /// 自动修复目录形式的资源包（zip 会先解到同名目录再修，避免破坏原包）。
    /// 修复内容：补 pack.mcmeta、纠正 pack_format、拍平多余的顶层目录。
    /// </summary>
    public static PackRepairResult Repair(PackDiagnosis diagnosis, string? targetVersion = null, bool backup = true)
    {
        if (diagnosis.Healthy) return new PackRepairResult { Ok = true };
        if (!diagnosis.CanAutoRepair) return PackRepairResult.Fail("存在无法自动修复的问题");

        var repaired = new List<PackIssueKind>();
        string? backupPath = null;

        try
        {
            var workDir = diagnosis.Path;

            if (diagnosis.IsZip)
            {
                workDir = Path.Combine(
                    Path.GetDirectoryName(diagnosis.Path) ?? ".",
                    Path.GetFileNameWithoutExtension(diagnosis.Path));

                if (Directory.Exists(workDir)) return PackRepairResult.Fail($"目标目录已存在：{workDir}");
                ZipFile.ExtractToDirectory(diagnosis.Path, workDir);

                if (backup)
                {
                    backupPath = diagnosis.Path + ".bak";
                    File.Copy(diagnosis.Path, backupPath, overwrite: true);
                }
            }
            else if (backup)
            {
                backupPath = diagnosis.Path + "_backup_" + DateTime.Now.ToString("yyyyMMddHHmmss");
                CopyDirectory(diagnosis.Path, backupPath);
            }

            // 1. 拍平多余的顶层目录
            if (diagnosis.RootPrefix.Length > 0)
            {
                var nested = Path.Combine(workDir, diagnosis.RootPrefix.TrimEnd('/').Replace('/', Path.DirectorySeparatorChar));
                if (Directory.Exists(nested))
                {
                    foreach (var sub in Directory.GetFileSystemEntries(nested))
                    {
                        var dest = Path.Combine(workDir, Path.GetFileName(sub));
                        if (Directory.Exists(sub)) Directory.Move(sub, dest);
                        else File.Move(sub, dest, overwrite: true);
                    }
                    Directory.Delete(nested, recursive: true);
                    repaired.Add(PackIssueKind.NestedRoot);
                }
            }

            // 2. 补 / 修 pack.mcmeta
            var metaPath = Path.Combine(workDir, "pack.mcmeta");
            var expected = ExpectedFormat(targetVersion);
            var needMeta = diagnosis.Issues.Any(i =>
                i.Kind is PackIssueKind.MissingMeta or PackIssueKind.BrokenMeta);
            var needFormat = diagnosis.Issues.Any(i => i.Kind == PackIssueKind.FormatMismatch);

            if (needMeta || needFormat)
            {
                var format = expected > 0 ? expected : (diagnosis.PackFormat > 0 ? diagnosis.PackFormat : 15);
                var desc = diagnosis.Description ?? Path.GetFileNameWithoutExtension(diagnosis.Path);
                File.WriteAllText(metaPath, BuildMeta(format, desc), new UTF8Encoding(false));

                if (needMeta)
                    repaired.Add(diagnosis.Issues.First(i =>
                        i.Kind is PackIssueKind.MissingMeta or PackIssueKind.BrokenMeta).Kind);
                if (needFormat) repaired.Add(PackIssueKind.FormatMismatch);
            }

            return new PackRepairResult { Ok = true, Repaired = repaired, BackupPath = backupPath };
        }
        catch (Exception ex)
        {
            return PackRepairResult.Fail(ex.Message);
        }
    }

    /// <summary>在条目名列表中找到 pack.mcmeta / assets 所在的最浅目录前缀。</summary>
    public static string? DetectRootPrefix(IEnumerable<string> entryNames)
    {
        string? best = null;
        var bestDepth = int.MaxValue;

        foreach (var raw in entryNames)
        {
            var name = raw.Replace('\\', '/').TrimStart('/');
            var isAnchor = name.EndsWith("pack.mcmeta", StringComparison.OrdinalIgnoreCase);
            var assetsIdx = name.IndexOf("assets/", StringComparison.OrdinalIgnoreCase);

            string prefix;
            if (isAnchor)
            {
                var idx = name.LastIndexOf('/');
                prefix = idx < 0 ? "" : name[..(idx + 1)];
            }
            else if (assetsIdx >= 0)
            {
                prefix = name[..assetsIdx];
            }
            else
            {
                continue;
            }

            var depth = prefix.Length == 0 ? 0 : prefix.Count(c => c == '/');
            if (depth >= bestDepth) continue;
            bestDepth = depth;
            best = prefix;
        }
        return best;
    }

    private static void CopyDirectory(string source, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(dir.Replace(source, dest));
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            File.Copy(file, file.Replace(source, dest), overwrite: true);
    }
}
