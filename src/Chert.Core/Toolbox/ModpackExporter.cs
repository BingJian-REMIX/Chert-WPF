using System.IO.Compression;
using System.Text.Json;
using Chert.Core.Utils;

namespace Chert.Core.Toolbox;

/// <summary>整合包导出选项。</summary>
public class ModpackExportOptions
{
    public bool IncludeMods { get; set; } = true;
    public bool IncludeConfig { get; set; } = true;
    public bool IncludeResourcePacks { get; set; } = true;
    public bool IncludeShaderPacks { get; set; } = true;
    public bool IncludeSaves { get; set; } = false;
    public string? DisplayName { get; set; }
}

/// <summary>整合包导出清单（写入压缩包根目录）。</summary>
public class ModpackManifest
{
    public string Format { get; set; } = "mclcs-modpack";
    public string Version { get; set; } = "1";
    public string? DisplayName { get; set; }
    public string? GameVersion { get; set; }
    public string? MinecraftVersionId { get; set; }
    public DateTime ExportedUtc { get; set; } = DateTime.UtcNow;
    public List<string> IncludedFolders { get; set; } = new();
}

/// <summary>
/// 整合包导入导出（工具箱功能 9 / 全局功能 7）：将当前版本环境（mods / config /
/// resourcepacks / shaderpacks / 可选 saves）打包导出为整合包 zip，并写入 mclcs 清单。
/// 导入由 <see cref="Installers.ModpackInstaller"/> 负责。
/// </summary>
public static class ModpackExporter
{
    private static readonly (string Folder, bool Flag)[] _folders =
    {
        ("mods", true), ("config", true), ("resourcepacks", true),
        ("shaderpacks", true), ("saves", false)
    };

    /// <summary>导出整合包；返回产物路径。不写入除目标 zip 外的任何文件。</summary>
    public static string Export(string gameRoot, string versionId, string destZip,
        ModpackExportOptions? options = null)
    {
        options ??= new ModpackExportOptions();
        Directory.CreateDirectory(Path.GetDirectoryName(destZip) ?? ".");
        if (File.Exists(destZip)) File.Delete(destZip);

        var manifest = new ModpackManifest
        {
            DisplayName = options.DisplayName,
            MinecraftVersionId = versionId,
            GameVersion = versionId
        };

        using var zip = ZipFile.Open(destZip, ZipArchiveMode.Create);

        foreach (var (folder, isOpt) in _folders)
        {
            var include = isOpt
                ? (folder == "saves" ? options.IncludeSaves
                    : folder == "mods" ? options.IncludeMods
                    : folder == "config" ? options.IncludeConfig
                    : folder == "resourcepacks" ? options.IncludeResourcePacks
                    : options.IncludeShaderPacks)
                : true;

            var dir = Path.Combine(gameRoot, folder);
            if (!include || !Directory.Exists(dir)) continue;

            manifest.IncludedFolders.Add(folder);
            foreach (var file in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
            {
                var rel = folder + "/" + Relative(dir, file);
                zip.CreateEntryFromFile(file, rel);
            }
        }

        // 写入清单
        var manifestJson = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
        var entry = zip.CreateEntry("mclcs_manifest.json");
        using var sw = new StreamWriter(entry.Open());
        sw.Write(manifestJson);

        return destZip;
    }

    private static string Relative(string baseDir, string file)
        => Normalize(file).StartsWith(Normalize(baseDir))
            ? Normalize(file)[Normalize(baseDir).Length..].TrimStart('/')
            : Path.GetFileName(file);

    private static string Normalize(string p) => p.Replace('\\', '/').ToLowerInvariant();
}
