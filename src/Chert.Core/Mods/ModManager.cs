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
    public List<ModEntry> ListInstalledMods()
    {
        var modsDir = PathEx.ModsDir(_gameRoot);
        if (!Directory.Exists(modsDir)) return new List<ModEntry>();

        var result = new List<ModEntry>();
        foreach (var file in Directory.EnumerateFiles(modsDir, "*.jar"))
        {
            var entry = BuildEntry(file);
            result.Add(entry);
        }
        return result;
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
}
