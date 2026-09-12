using System.IO.Compression;
using Chert.Core.Utils;

namespace Chert.Core.Download;

/// <summary>附加资源类型。</summary>
public enum ExtraResourceKind
{
    /// <summary>无法识别。</summary>
    Unknown,

    /// <summary>资源包（含 pack.mcmeta + assets）。</summary>
    ResourcePack,

    /// <summary>光影包（含 shaders 目录）。</summary>
    ShaderPack,

    /// <summary>数据包（含 pack.mcmeta + data）。</summary>
    DataPack,

    /// <summary>容器包：内部还套着若干个资源 zip / 资源目录。</summary>
    Container
}

/// <summary>单个附加资源的安装结果条目。</summary>
public sealed class ExtraResourceEntry
{
    /// <summary>资源名（落盘后的文件名 / 目录名）。</summary>
    public string Name { get; init; } = "";

    public ExtraResourceKind Kind { get; init; }

    /// <summary>落盘的完整路径。</summary>
    public string TargetPath { get; init; } = "";

    /// <summary>是否因重名而自动改名。</summary>
    public bool Renamed { get; init; }

    /// <summary>中文类型标签（界面展示用）。</summary>
    public string KindLabel => Kind switch
    {
        ExtraResourceKind.ResourcePack => "资源包",
        ExtraResourceKind.ShaderPack => "光影",
        ExtraResourceKind.DataPack => "数据包",
        ExtraResourceKind.Container => "资源合集",
        _ => "未识别"
    };

    public override string ToString() => $"{KindLabel}：{Name}";
}

/// <summary>附加资源安装结果。</summary>
public sealed class ExtraResourceInstallResult
{
    public bool Ok { get; init; }
    public string? Error { get; init; }

    public List<ExtraResourceEntry> Entries { get; init; } = new();

    /// <summary>安装摘要（如"资源包 ×2、光影 ×1"），用于状态栏提示。</summary>
    public string Summary
    {
        get
        {
            if (Entries.Count == 0) return "未安装任何资源";
            return string.Join("、", Entries
                .GroupBy(e => e.KindLabel)
                .Select(g => $"{g.Key} ×{g.Count()}"));
        }
    }

    /// <summary>是否存在落到兜底目录的未识别资源（需提示用户手动处理）。</summary>
    public bool HasUnknown => Entries.Any(e => e.Kind == ExtraResourceKind.Unknown);

    public static ExtraResourceInstallResult Fail(string error) => new() { Ok = false, Error = error };
}

/// <summary>
/// 地图附加资源安装器（规格 2.2 → 地图 → 详情窗"附加资源"按钮）。
/// <para>
/// 像素茶艺的附加资源包形态不固定，常见三种：
/// ①单个资源包 / 光影 zip；②容器 zip 里套着若干个资源 zip；③容器 zip 里是若干个资源目录。
/// 本安装器统一探测并分发：资源包 → <c>resourcepacks</c>，光影 → <c>shaderpacks</c>，
/// 数据包 → <c>downloads/extras/datapacks</c>（需用户自行放入具体存档），
/// 识别不了的原样留在 <c>downloads/extras</c>，绝不丢文件。
/// </para>
/// </summary>
public static class ExtraResourceInstaller
{
    /// <summary>兜底目录：识别不了的资源原样保留在这里。</summary>
    public static string FallbackDir(string gameRoot) => Path.Combine(gameRoot, "downloads", "extras");

    /// <summary>数据包暂存目录（数据包必须放进具体存档，故不自动分发）。</summary>
    public static string DataPackStageDir(string gameRoot) => Path.Combine(FallbackDir(gameRoot), "datapacks");

    private static readonly string[] ShaderExtensions = { ".fsh", ".vsh", ".gsh", ".glsl", ".csh" };

    /// <summary>
    /// 根据条目名集合判定资源类型（纯函数，便于自检）。
    /// <paramref name="names"/> 应为已去掉公共前缀的相对路径。
    /// </summary>
    public static ExtraResourceKind Detect(IEnumerable<string> names)
    {
        var list = names
            .Select(n => n.Replace('\\', '/').TrimStart('/'))
            .Where(n => n.Length > 0)
            .ToList();
        if (list.Count == 0) return ExtraResourceKind.Unknown;

        var hasShaderDir = list.Any(n =>
            n.StartsWith("shaders/", StringComparison.OrdinalIgnoreCase) &&
            (ShaderExtensions.Any(e => n.EndsWith(e, StringComparison.OrdinalIgnoreCase)) ||
             n.EndsWith("shaders.properties", StringComparison.OrdinalIgnoreCase) ||
             n.EndsWith(".png", StringComparison.OrdinalIgnoreCase)));
        if (hasShaderDir) return ExtraResourceKind.ShaderPack;

        var hasMeta = list.Any(n => n.Equals("pack.mcmeta", StringComparison.OrdinalIgnoreCase));
        if (hasMeta)
        {
            var hasData = list.Any(n => n.StartsWith("data/", StringComparison.OrdinalIgnoreCase));
            var hasAssets = list.Any(n => n.StartsWith("assets/", StringComparison.OrdinalIgnoreCase));
            if (hasData && !hasAssets) return ExtraResourceKind.DataPack;
            return ExtraResourceKind.ResourcePack;
        }

        // 顶层套着 zip，或子目录里各自是一个资源包 → 容器
        if (list.Any(n => n.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))) return ExtraResourceKind.Container;
        if (SubPackagePrefixes(list).Count > 0) return ExtraResourceKind.Container;

        return ExtraResourceKind.Unknown;
    }

    /// <summary>
    /// 找出"目录形式子资源包"的前缀集合：<c>foo/pack.mcmeta</c> 或 <c>foo/shaders/*.fsh</c> 中的 <c>foo/</c>。
    /// </summary>
    public static List<string> SubPackagePrefixes(IEnumerable<string> names)
    {
        var result = new List<string>();
        foreach (var raw in names)
        {
            var n = raw.Replace('\\', '/').TrimStart('/');
            var idx = n.IndexOf('/');
            if (idx <= 0) continue;
            var prefix = n[..(idx + 1)];
            var rest = n[(idx + 1)..];

            var isPack = rest.Equals("pack.mcmeta", StringComparison.OrdinalIgnoreCase)
                         || (rest.StartsWith("shaders/", StringComparison.OrdinalIgnoreCase)
                             && ShaderExtensions.Any(e => rest.EndsWith(e, StringComparison.OrdinalIgnoreCase)));
            if (isPack && !result.Contains(prefix)) result.Add(prefix);
        }
        return result;
    }

    /// <summary>把资源类型映射到落盘目录。</summary>
    public static string TargetDirFor(ExtraResourceKind kind, string gameRoot) => kind switch
    {
        ExtraResourceKind.ResourcePack => PathEx.ResourcePacksDir(gameRoot),
        ExtraResourceKind.ShaderPack => PathEx.ShaderPacksDir(gameRoot),
        ExtraResourceKind.DataPack => DataPackStageDir(gameRoot),
        _ => FallbackDir(gameRoot)
    };

    /// <summary>安装附加资源压缩包，自动分发到各自目录。</summary>
    public static ExtraResourceInstallResult Install(string zipPath, string gameRoot, string? preferredName = null)
    {
        if (!File.Exists(zipPath)) return ExtraResourceInstallResult.Fail("附加资源包不存在");

        try
        {
            List<string> names;
            using (var probe = ZipFile.OpenRead(zipPath))
                names = probe.Entries.Select(e => e.FullName).ToList();

            var kind = Detect(names);
            var entries = new List<ExtraResourceEntry>();

            if (kind == ExtraResourceKind.Container)
            {
                entries.AddRange(InstallContainer(zipPath, gameRoot, names));

                // 容器里一个都没识别出来时，整包保底
                if (entries.Count == 0)
                    entries.Add(CopyWhole(zipPath, gameRoot, ExtraResourceKind.Unknown, preferredName));
            }
            else
            {
                // 单包：zip 形式的资源包 / 光影 MC 可直接读取，整包复制即可
                entries.Add(CopyWhole(zipPath, gameRoot, kind, preferredName));
            }

            return new ExtraResourceInstallResult { Ok = true, Entries = entries };
        }
        catch (Exception ex)
        {
            return ExtraResourceInstallResult.Fail(ex.Message);
        }
    }

    /// <summary>整包复制到目标目录（保持 zip 形式）。</summary>
    private static ExtraResourceEntry CopyWhole(string zipPath, string gameRoot, ExtraResourceKind kind, string? preferredName)
    {
        var dir = TargetDirFor(kind, gameRoot);
        Directory.CreateDirectory(dir);

        var baseName = string.IsNullOrWhiteSpace(preferredName)
            ? Path.GetFileNameWithoutExtension(zipPath)
            : preferredName!;
        var fileName = ResolveFileConflict(dir, SafeName(baseName) + ".zip", out var renamed);
        var target = Path.Combine(dir, fileName);

        File.Copy(zipPath, target, overwrite: false);
        return new ExtraResourceEntry
        {
            Name = fileName,
            Kind = kind,
            TargetPath = target,
            Renamed = renamed
        };
    }

    /// <summary>拆容器：内部 zip 逐个判类型分发，目录形式的子包整体解压。</summary>
    private static List<ExtraResourceEntry> InstallContainer(string zipPath, string gameRoot, List<string> names)
    {
        var entries = new List<ExtraResourceEntry>();
        var temp = Path.Combine(Path.GetTempPath(), "mclcs_extra_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);

        try
        {
            using (var archive = ZipFile.OpenRead(zipPath))
            {
                // ① 内部 zip
                foreach (var e in archive.Entries)
                {
                    if (!e.FullName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) continue;
                    if (e.Length == 0) continue;

                    var inner = Path.Combine(temp, SafeName(Path.GetFileName(e.FullName)));
                    e.ExtractToFile(inner, overwrite: true);

                    ExtraResourceKind innerKind;
                    try
                    {
                        using var ia = ZipFile.OpenRead(inner);
                        innerKind = Detect(ia.Entries.Select(x => x.FullName));
                    }
                    catch
                    {
                        innerKind = ExtraResourceKind.Unknown;
                    }
                    if (innerKind == ExtraResourceKind.Container) innerKind = ExtraResourceKind.Unknown;

                    entries.Add(CopyWhole(inner, gameRoot, innerKind, Path.GetFileNameWithoutExtension(e.FullName)));
                }

                // ② 目录形式子包
                foreach (var prefix in SubPackagePrefixes(names))
                {
                    var relatives = names
                        .Select(n => n.Replace('\\', '/').TrimStart('/'))
                        .Where(n => n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        .Select(n => n[prefix.Length..])
                        .Where(n => n.Length > 0)
                        .ToList();

                    var subKind = Detect(relatives);
                    if (subKind is ExtraResourceKind.Unknown or ExtraResourceKind.Container) continue;

                    var dir = TargetDirFor(subKind, gameRoot);
                    Directory.CreateDirectory(dir);

                    var folder = SafeName(prefix.TrimEnd('/').Split('/').Last());
                    var name = ResolveDirConflict(dir, folder, out var renamed);
                    var destRoot = Path.Combine(dir, name);
                    var destFull = Path.GetFullPath(destRoot);
                    Directory.CreateDirectory(destRoot);

                    foreach (var e in archive.Entries)
                    {
                        var full = e.FullName.Replace('\\', '/').TrimStart('/');
                        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                        var rel = full[prefix.Length..];
                        if (rel.Length == 0 || rel.EndsWith('/')) continue;

                        var outPath = Path.GetFullPath(Path.Combine(destRoot, rel));
                        if (!outPath.StartsWith(destFull, StringComparison.OrdinalIgnoreCase)) continue;  // 防 zip 穿越

                        var outDir = Path.GetDirectoryName(outPath);
                        if (outDir is not null) Directory.CreateDirectory(outDir);
                        e.ExtractToFile(outPath, overwrite: true);
                    }

                    entries.Add(new ExtraResourceEntry
                    {
                        Name = name,
                        Kind = subKind,
                        TargetPath = destRoot,
                        Renamed = renamed
                    });
                }
            }
        }
        finally
        {
            try { Directory.Delete(temp, recursive: true); } catch { /* 临时目录清理失败可忽略 */ }
        }

        return entries;
    }

    /// <summary>清洗成合法文件名 / 目录名。</summary>
    public static string SafeName(string? name)
    {
        var s = (name ?? "").Trim();
        if (s.Length == 0) s = "附加资源";
        foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        s = s.Trim('.', ' ');
        if (s.Length == 0) s = "附加资源";
        return s.Length > 80 ? s[..80] : s;
    }

    /// <summary>文件重名时追加 " (2)"。</summary>
    public static string ResolveFileConflict(string dir, string fileName, out bool renamed)
    {
        renamed = false;
        if (!File.Exists(Path.Combine(dir, fileName))) return fileName;

        var stem = Path.GetFileNameWithoutExtension(fileName);
        var ext = Path.GetExtension(fileName);
        for (var i = 2; i < 1000; i++)
        {
            var candidate = $"{stem} ({i}){ext}";
            if (File.Exists(Path.Combine(dir, candidate))) continue;
            renamed = true;
            return candidate;
        }
        renamed = true;
        return $"{stem} ({DateTime.Now:yyyyMMddHHmmss}){ext}";
    }

    /// <summary>目录重名时追加 " (2)"。</summary>
    public static string ResolveDirConflict(string parent, string name, out bool renamed)
    {
        renamed = false;
        if (!Directory.Exists(Path.Combine(parent, name))) return name;

        for (var i = 2; i < 1000; i++)
        {
            var candidate = $"{name} ({i})";
            if (Directory.Exists(Path.Combine(parent, candidate))) continue;
            renamed = true;
            return candidate;
        }
        renamed = true;
        return $"{name} ({DateTime.Now:yyyyMMddHHmmss})";
    }
}
