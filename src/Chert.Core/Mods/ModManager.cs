using System.IO.Compression;
using System.Security.Cryptography;
using Chert.Core.Download;
using Chert.Core.Models;
using Chert.Core.Utils;

namespace Chert.Core.Mods;

/// <summary>
/// Mod 管理：扫描、更新检查、依赖检查、卸载。
/// 支持 fabric.mod.json 和 mods.toml 元数据解析。
/// </summary>
public class ModManager
{
    private readonly string _gameRoot;
    private readonly ModrinthClient _modrinth;
    private readonly IDownloader _downloader;

    public ModManager(string gameRoot, HttpClient httpClient, IDownloader downloader)
    {
        _gameRoot = gameRoot;
        _modrinth = new ModrinthClient(httpClient);
        _downloader = downloader;
    }

    /// <summary>扫描 mods/ 目录，返回已安装 Mod 列表（含元数据）。</summary>
    /// <param name="includeDisabled">是否一并列出被禁用的 Mod（*.jar.disabled）。</param>
    public List<ModEntry> ListInstalledMods(bool includeDisabled = false)
    {
        var modsDir = PathEx.ModsDir(_gameRoot);
        if (!Directory.Exists(modsDir)) return new List<ModEntry>();

        var result = new List<ModEntry>();
        foreach (var file in Directory.EnumerateFiles(modsDir, "*.jar"))
        {
            var entry = BuildEntry(file);
            entry.Enabled = true;
            result.Add(entry);
        }

        if (includeDisabled)
        {
            foreach (var file in Directory.EnumerateFiles(modsDir, "*.jar.disabled"))
            {
                var entry = BuildEntry(file);
                entry.Enabled = false;
                result.Add(entry);
            }
        }

        return result.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// 静态、无网络的本地依赖扫描：解析 mods 目录下的所有 Mod 元数据，返回依赖/冲突检查结果。
    /// 供崩溃修复引擎在离线环境下判断 Mod 冲突与缺失前置。
    /// </summary>
    public static List<DependencyCheckResult> ScanDependencies(string gameRoot)
    {
        var mgr = new ModManager(gameRoot, new HttpClient(), null!);
        return mgr.CheckDependencies();
    }

    /// <summary>已安装 Mod 中是否至少存在一对冲突。</summary>
    public static bool HasModConflict(string gameRoot)
        => ScanDependencies(gameRoot).Any(r => r.Conflicts.Count > 0);

    /// <summary>已安装 Mod 中缺失的强制前置依赖（去重后的 modId 列表）。</summary>
    public static List<string> MissingDependencies(string gameRoot)
        => ScanDependencies(gameRoot)
            .SelectMany(r => r.Missing)
            .Where(m => m.Required)
            .Select(m => m.DependencyId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static ModEntry BuildEntry(string filePath)
    {
        var fileName = System.IO.Path.GetFileName(filePath);
        var entry = new ModEntry
        {
            Id = fileName,
            Name = fileName,
            FileName = fileName,
            InstalledVersion = "unknown",
            ProjectUrl = ""
        };

        // 尝试 fabric.mod.json
        var fabric = ModMetadataParser.ParseFabricMod(filePath);
        if (fabric is not null)
        {
            entry.ModId = fabric.Id;
            entry.Name = fabric.Name ?? fabric.Id;
            entry.InstalledVersion = fabric.Version;
            entry.Loader = "fabric";
            if (fabric.Depends is not null)
                foreach (var kv in fabric.Depends)
                    if (kv.Key != "minecraft" && kv.Key != "java")
                        entry.Depends[kv.Key] = kv.Value;
            if (fabric.Conflicts is not null)
                foreach (var kv in fabric.Conflicts)
                    if (kv.Key != "minecraft")
                        entry.Conflicts[kv.Key] = kv.Value;
            return entry;
        }

        // 尝试 mods.toml (Forge/NeoForge)
        var forge = ModMetadataParser.ParseForgeMod(filePath);
        if (forge is not null)
        {
            entry.ModId = forge.ModId;
            entry.Name = forge.DisplayName;
            entry.InstalledVersion = forge.Version;
            entry.Loader = "forge";
            foreach (var dep in forge.Dependencies)
            {
                if (dep.ModId is "minecraft" or "java" or "forge" or "neoforge") continue;
                if (dep.Mandatory)
                    entry.Depends[dep.ModId] = dep.VersionRange;
                else
                    entry.Conflicts[dep.ModId] = dep.VersionRange;
            }
            return entry;
        }

        return entry;
    }

    /// <summary>检查所有已安装 Mod 的依赖关系。</summary>
    public List<DependencyCheckResult> CheckDependencies()
    {
        var mods = ListInstalledMods();
        var parsed = mods.Where(m => m.MetadataParsed).ToList();

        // 构建已安装的 modId -> entry 映射
        var installedById = new Dictionary<string, ModEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in parsed)
        {
            if (m.ModId is not null)
                installedById[m.ModId] = m;
        }

        var results = new List<DependencyCheckResult>();
        foreach (var mod in parsed)
        {
            var result = new DependencyCheckResult
            {
                ModFileName = mod.FileName,
                ModId = mod.ModId ?? mod.FileName,
                ModName = mod.Name,
                ModVersion = mod.InstalledVersion
            };

            // 检查缺失依赖
            foreach (var (depId, versionRange) in mod.Depends)
            {
                if (!installedById.ContainsKey(depId))
                {
                    result.Missing.Add(new MissingDependency
                    {
                        DependencyId = depId,
                        VersionRange = versionRange,
                        Required = true
                    });
                }
            }

            // 检查冲突
            foreach (var (conflictId, conflictRange) in mod.Conflicts)
            {
                if (installedById.TryGetValue(conflictId, out var conflictMod))
                {
                    result.Conflicts.Add(new ConflictDependency
                    {
                        ConflictId = conflictId,
                        InstalledVersion = conflictMod.InstalledVersion,
                        ConflictRange = conflictRange
                    });
                }
            }

            if (result.Missing.Count > 0 || result.Conflicts.Count > 0)
                results.Add(result);
        }

        return results;
    }

    /// <summary>对已安装的 Mod 批量检查更新（通过 Modrinth 搜索）。</summary>
    public async Task<List<ModEntry>> CheckForUpdatesAsync(CancellationToken ct = default)
    {
        var mods = ListInstalledMods();
        foreach (var mod in mods)
        {
            try
            {
                // 优先用 modId 搜，否则用文件名
                var query = mod.ModId ?? System.IO.Path.GetFileNameWithoutExtension(mod.FileName);
                if (string.IsNullOrWhiteSpace(query) || query.Length < 3) continue;

                var result = await _modrinth.SearchAsync(query, type: ModrinthProjectType.Mod, limit: 3, ct: ct);
                var hit = result.Hits.FirstOrDefault();
                if (hit is null) continue;

                var versions = await _modrinth.GetVersionsAsync(hit.ProjectId, ct);
                var latest = versions.FirstOrDefault()?.VersionNumber;
                mod.LatestVersion = latest;
                mod.ProjectUrl = $"https://modrinth.com/mod/{hit.Slug}";
            }
            catch
            {
                // 单个查询失败不影响整体
            }
        }
        return mods;
    }

    /// <summary>卸载 Mod（删除文件）。</summary>
    public bool UninstallMod(string fileName)
    {
        var path = System.IO.Path.Combine(PathEx.ModsDir(_gameRoot), fileName);
        if (!File.Exists(path)) return false;
        File.Delete(path);
        return true;
    }

    /// <summary>清单 #66：核验单个 Mod 文件——存在性、体积、Jar 完整性、元数据与 SHA-1。</summary>
    public ModVerifyResult VerifyMod(string fileName)
    {
        var result = new ModVerifyResult { FileName = fileName };
        var path = System.IO.Path.Combine(PathEx.ModsDir(_gameRoot), fileName);
        if (!File.Exists(path))
        {
            result.Message = "文件不存在";
            return result;
        }

        var info = new FileInfo(path);
        result.Exists = true;
        result.SizeBytes = info.Length;
        if (info.Length == 0)
        {
            result.Message = "文件为空（0 字节），多半是下载不完整";
            return result;
        }

        try
        {
            using var archive = ZipFile.OpenRead(path);
            result.IsValidJar = archive.Entries.Count > 0;
        }
        catch (Exception ex)
        {
            result.Message = "Jar 损坏，无法读取：" + ex.Message;
            return result;
        }

        if (!result.IsValidJar)
        {
            result.Message = "Jar 内没有任何条目";
            return result;
        }

        var entry = BuildEntry(path);
        result.HasMetadata = entry.MetadataParsed;
        if (!result.HasMetadata)
        {
            result.Message = "未找到 fabric.mod.json / mods.toml，无法核验依赖与更新";
            return result;
        }

        try
        {
            using var sha1 = SHA1.Create();
            using var stream = File.OpenRead(path);
            result.Sha1 = Convert.ToHexString(sha1.ComputeHash(stream)).ToLowerInvariant();
        }
        catch
        {
            // 哈希计算失败不影响其它结论
        }

        result.Message = $"正常 · {result.SizeBytes / 1024.0:F1} KB · {entry.ModId} {entry.InstalledVersion}";
        return result;
    }

    /// <summary>清单 #66：批量核验所有已安装 Mod（含被禁用的）。</summary>
    public List<ModVerifyResult> VerifyAllMods()
        => ListInstalledMods(includeDisabled: true)
           .Select(m => VerifyMod(m.FileName))
           .ToList();

    /// <summary>清单 #66：启用 / 禁用 Mod（*.jar.disabled 重命名）。返回操作后的新文件名，失败返回 null。</summary>
    public string? SetModEnabled(string fileName, bool enabled)
    {
        var dir = PathEx.ModsDir(_gameRoot);
        var path = System.IO.Path.Combine(dir, fileName);
        if (!File.Exists(path)) return null;

        var isDisabled = fileName.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);
        if (enabled != isDisabled) return fileName; // 已经处于目标状态

        var target = enabled ? fileName[..^".disabled".Length] : fileName + ".disabled";
        var targetPath = System.IO.Path.Combine(dir, target);
        if (File.Exists(targetPath)) return null; // 目标文件名已存在，避免覆盖其它 Mod

        File.Move(path, targetPath);
        return target;
    }

    /// <summary>清单 #66：一键更新单个 Mod——在 Modrinth 定位项目、取最新版本并下载覆盖。</summary>
    public async Task<ModUpdateOutcome> UpdateModAsync(ModEntry mod,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        var outcome = new ModUpdateOutcome();
        try
        {
            var query = string.IsNullOrWhiteSpace(mod.ModId)
                ? System.IO.Path.GetFileNameWithoutExtension(mod.FileName)
                : mod.ModId;
            if (query.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
                query = query[..^".disabled".Length];
            if (string.IsNullOrWhiteSpace(query) || query.Length < 3)
            {
                outcome.Message = "无法定位项目（缺少 Mod ID）";
                return outcome;
            }

            var search = await _modrinth.SearchAsync(query, type: ModrinthProjectType.Mod, limit: 3, ct: ct);
            var hit = search.Hits.FirstOrDefault();
            if (hit is null)
            {
                outcome.Message = "Modrinth 上未找到该项目";
                return outcome;
            }

            var versions = await _modrinth.GetVersionsAsync(hit.ProjectId, ct);
            var latest = versions.FirstOrDefault();
            if (latest is null)
            {
                outcome.Message = "该项目没有可下载的版本";
                return outcome;
            }

            var file = _modrinth.SelectBestFile(latest, null, LoaderType.Any) ?? latest.Files.FirstOrDefault();
            if (file is null)
            {
                outcome.Message = "最新版本没有可下载的文件";
                return outcome;
            }

            var dir = PathEx.ModsDir(_gameRoot);
            Directory.CreateDirectory(dir);
            var targetName = string.IsNullOrWhiteSpace(file.FileName)
                ? hit.Slug + "-" + latest.VersionNumber + ".jar"
                : file.FileName;
            var targetPath = System.IO.Path.Combine(dir, targetName);
            var tmpPath = targetPath + ".chert-tmp";

            try
            {
                using var client = new HttpClient();
                await using var http = await client.GetStreamAsync(file.Url, ct);
                await using var fs = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None);
                var buffer = new byte[81920];
                long read = 0;
                int n;
                while ((n = await http.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
                {
                    await fs.WriteAsync(buffer.AsMemory(0, n), ct);
                    read += n;
                    if (file.Size > 0) progress?.Report((double)read / file.Size);
                }
            }
            catch
            {
                TryDelete(tmpPath); // 中断或失败时不要留下半截文件
                throw;
            }

            File.Move(tmpPath, targetPath, overwrite: true);

            // 旧文件若不同名则删除，避免同一 Mod 多版本共存引发冲突
            var oldPath = System.IO.Path.Combine(dir, mod.FileName);
            if (!string.Equals(oldPath, targetPath, StringComparison.OrdinalIgnoreCase) && File.Exists(oldPath))
                File.Delete(oldPath);

            outcome.Success = true;
            outcome.NewFileName = targetName;
            outcome.Message = "已更新到 " + latest.VersionNumber;
        }
        catch (OperationCanceledException)
        {
            outcome.Message = "已取消";
        }
        catch (Exception ex)
        {
            outcome.Message = "更新失败：" + ex.Message;
        }
        return outcome;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* 忽略 */ }
    }
}
