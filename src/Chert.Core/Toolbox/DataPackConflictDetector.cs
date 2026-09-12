using System.IO.Compression;
using System.Text.Json;

namespace Chert.Core.Toolbox;

/// <summary>一个数据包。</summary>
public class DataPackInfo
{
    /// <summary>数据包名（目录名或 zip 文件名）。</summary>
    public string Name { get; set; } = "";

    /// <summary>完整路径。</summary>
    public string Path { get; set; } = "";

    /// <summary>是否为 zip 形式。</summary>
    public bool IsZip { get; set; }

    /// <summary>pack.mcmeta 中的 pack_format。</summary>
    public int PackFormat { get; set; }

    /// <summary>pack.mcmeta 中的描述。</summary>
    public string? Description { get; set; }

    /// <summary>是否缺少 pack.mcmeta（非法数据包）。</summary>
    public bool MissingMeta { get; set; }

    /// <summary>包内资源路径（形如 <c>data/命名空间/类别/xxx.json</c>）。</summary>
    public List<string> Resources { get; set; } = new();

    /// <summary>加载顺序（越大越靠后，后者覆盖前者）。</summary>
    public int LoadOrder { get; set; }
}

/// <summary>冲突的严重程度。</summary>
public enum ConflictSeverity
{
    /// <summary>提示：同名但可能无害。</summary>
    Info,
    /// <summary>警告：资源被覆盖。</summary>
    Warning,
    /// <summary>严重：命中已知冲突规则库。</summary>
    Critical
}

/// <summary>冲突类型。</summary>
public enum ConflictKind
{
    /// <summary>同一文件路径被多个包定义（后者覆盖前者）。</summary>
    FileOverride,
    /// <summary>同一命名空间 ID（如 <c>minecraft:stone</c>）被多个包定义。</summary>
    NamespaceId,
    /// <summary>命中内置的已知冲突规则。</summary>
    KnownRule
}

/// <summary>一处冲突。</summary>
public sealed class DataPackConflict
{
    public DataPackConflict(string resource, List<string> packs, string winner,
        ConflictKind kind = ConflictKind.FileOverride,
        ConflictSeverity severity = ConflictSeverity.Warning,
        string? advice = null)
    {
        Resource = resource;
        Packs = packs;
        Winner = winner;
        Kind = kind;
        Severity = severity;
        Advice = advice;
    }

    /// <summary>冲突的资源路径或命名空间 ID。</summary>
    public string Resource { get; }

    /// <summary>涉及的数据包（按加载顺序）。</summary>
    public List<string> Packs { get; }

    /// <summary>最终生效的数据包（加载顺序最靠后的）。</summary>
    public string Winner { get; }

    public ConflictKind Kind { get; }
    public ConflictSeverity Severity { get; }

    /// <summary>处理建议（来自规则库）。</summary>
    public string? Advice { get; }

    /// <summary>被覆盖掉的数据包。</summary>
    public IEnumerable<string> Losers => Packs.Where(p => p != Winner);

    public string KindText => Kind switch
    {
        ConflictKind.FileOverride => "文件覆盖",
        ConflictKind.NamespaceId => "命名空间 ID",
        ConflictKind.KnownRule => "已知冲突",
        _ => "冲突"
    };

    public string SeverityText => Severity switch
    {
        ConflictSeverity.Critical => "严重",
        ConflictSeverity.Warning => "警告",
        _ => "提示"
    };

    /// <summary>被覆盖方的列表文本。</summary>
    public string LosersText => string.Join("、", Losers);

    public override string ToString() =>
        $"{Resource}：{string.Join(" → ", Packs)}（生效：{Winner}）";
}

/// <summary>一条已知冲突规则。</summary>
public sealed class ConflictRule
{
    /// <summary>规则 ID。</summary>
    public string Id { get; set; } = "";

    /// <summary>匹配的资源路径片段（不区分大小写的包含匹配）。</summary>
    public string Pattern { get; set; } = "";

    /// <summary>说明。</summary>
    public string Description { get; set; } = "";

    /// <summary>处理建议。</summary>
    public string Advice { get; set; } = "";

    public ConflictSeverity Severity { get; set; } = ConflictSeverity.Critical;
}

/// <summary>检测报告。</summary>
public sealed class DataPackReport
{
    public List<DataPackInfo> Packs { get; } = new();
    public List<DataPackConflict> Conflicts { get; } = new();

    /// <summary>pack_format 与目标游戏版本不符的包。</summary>
    public List<string> FormatWarnings { get; } = new();

    /// <summary>缺少 pack.mcmeta 的包。</summary>
    public IEnumerable<DataPackInfo> Invalid => Packs.Where(p => p.MissingMeta);

    public bool HasConflicts => Conflicts.Count > 0;

    public string Summary =>
        $"共 {Packs.Count} 个数据包，冲突 {Conflicts.Count} 处，格式告警 {FormatWarnings.Count} 条";
}

/// <summary>
/// 数据包冲突检测（工具箱开发工具）：扫描存档 <c>datapacks/</c> 目录，
/// 解析每个包的 <c>pack.mcmeta</c> 与资源清单，找出同一资源被多个包定义的情况。
/// Minecraft 的覆盖规则是"后加载者胜出"，因此加载顺序靠后的包为最终生效方。
/// </summary>
public static class DataPackConflictDetector
{
    /// <summary>常见 pack_format 与游戏版本的对应（用于格式告警）。</summary>
    public static readonly Dictionary<string, int> KnownPackFormats = new()
    {
        ["1.16"] = 6, ["1.17"] = 7, ["1.18"] = 8, ["1.19"] = 10,
        ["1.20"] = 15, ["1.20.5"] = 41, ["1.21"] = 48
    };

    /// <summary>规则库文件名（放在 gameRoot 下，可联网更新覆盖）。</summary>
    public const string RulesFileName = "datapack-conflict-rules.json";

    /// <summary>
    /// 内置的已知冲突规则库。命中这些路径的覆盖通常会造成实际游戏问题，
    /// 比 "两个包改了同一个战利品表" 更值得警告。
    /// </summary>
    public static readonly IReadOnlyList<ConflictRule> BuiltinRules = new List<ConflictRule>
    {
        new() { Id = "recipe-override", Pattern = "/recipes/",
                Description = "配方被覆盖，可能导致合成表缺失",
                Advice = "调整数据包加载顺序，或合并两个包的配方定义" },
        new() { Id = "loot-override", Pattern = "/loot_tables/",
                Description = "战利品表被覆盖，掉落物会以后加载的包为准",
                Advice = "若两个包都需生效，请手动合并战利品表的 pools" },
        new() { Id = "tag-override", Pattern = "/tags/",
                Description = "标签被整体覆盖而非合并",
                Advice = "在标签 JSON 中加入 \"replace\": false 可改为追加合并",
                Severity = ConflictSeverity.Critical },
        new() { Id = "advancement-override", Pattern = "/advancements/",
                Description = "进度被覆盖，成就树可能断裂",
                Advice = "重命名其中一个包的进度 ID 以避免碰撞",
                Severity = ConflictSeverity.Warning },
        new() { Id = "function-override", Pattern = "/functions/",
                Description = "函数被覆盖，另一个包的逻辑不会执行",
                Advice = "改用各自命名空间下的函数，并由 tick.json 统一调度" },
        new() { Id = "worldgen-override", Pattern = "/worldgen/",
                Description = "世界生成配置被覆盖，可能导致地形不一致",
                Advice = "世界生成冲突风险高，建议只保留一个包",
                Severity = ConflictSeverity.Critical },
        new() { Id = "vanilla-namespace", Pattern = "data/minecraft/",
                Description = "直接覆盖原版 minecraft 命名空间",
                Advice = "尽量使用自有命名空间，仅在必要时覆盖原版内容",
                Severity = ConflictSeverity.Critical }
    };

    /// <summary>
    /// 从 <c>data/&lt;ns&gt;/&lt;类别&gt;/&lt;路径&gt;.json</c> 提取命名空间 ID，
    /// 形如 <c>minecraft:stone</c>（类别单独返回）。无法解析返回 null。
    /// </summary>
    public static (string Namespace, string Category, string Id)? ParseResourceId(string relativePath)
    {
        var p = relativePath.Replace('\\', '/').TrimStart('/');
        var parts = p.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 4) return null;
        if (!string.Equals(parts[0], "data", StringComparison.OrdinalIgnoreCase)) return null;

        var ns = parts[1];
        var category = parts[2];

        // tags 类别多一层（tags/items/xxx.json）
        var idStart = 3;
        if (string.Equals(category, "tags", StringComparison.OrdinalIgnoreCase) && parts.Length >= 5)
        {
            category = "tags/" + parts[3];
            idStart = 4;
        }

        var id = string.Join('/', parts.Skip(idStart));
        var dot = id.LastIndexOf('.');
        if (dot > 0) id = id[..dot];
        if (string.IsNullOrEmpty(id)) return null;

        return (ns, category, $"{ns}:{id}");
    }

    /// <summary>匹配已知冲突规则；未命中返回 null。</summary>
    public static ConflictRule? MatchRule(string resourcePath, IEnumerable<ConflictRule>? rules = null)
    {
        var p = resourcePath.Replace('\\', '/');
        return (rules ?? BuiltinRules)
            .FirstOrDefault(r => !string.IsNullOrEmpty(r.Pattern) &&
                                 p.Contains(r.Pattern, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 载入规则库：优先读 gameRoot 下的 JSON（可联网更新），
    /// 读取失败或不存在时回退到内置规则。
    /// </summary>
    public static IReadOnlyList<ConflictRule> LoadRules(string? gameRoot)
    {
        if (string.IsNullOrWhiteSpace(gameRoot)) return BuiltinRules;
        try
        {
            var path = Path.Combine(gameRoot!, RulesFileName);
            if (!File.Exists(path)) return BuiltinRules;

            var list = JsonSerializer.Deserialize<List<ConflictRule>>(File.ReadAllText(path));
            return list is { Count: > 0 } ? list : BuiltinRules;
        }
        catch
        {
            return BuiltinRules;
        }
    }

    /// <summary>把规则库写入 gameRoot（供联网更新后落盘）。</summary>
    public static bool SaveRules(string gameRoot, IEnumerable<ConflictRule> rules)
    {
        try
        {
            Directory.CreateDirectory(gameRoot);
            File.WriteAllText(Path.Combine(gameRoot, RulesFileName),
                JsonSerializer.Serialize(rules, new JsonSerializerOptions { WriteIndented = true }));
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>存档中的数据包目录。</summary>
    public static string DataPacksDir(string saveDir) => Path.Combine(saveDir, "datapacks");

    /// <summary>解析 pack.mcmeta 文本，返回 (pack_format, description)。</summary>
    public static (int Format, string? Description) ParseMeta(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return (0, null);
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("pack", out var pack)) return (0, null);

            var format = pack.TryGetProperty("pack_format", out var pf) && pf.TryGetInt32(out var f) ? f : 0;

            string? desc = null;
            if (pack.TryGetProperty("description", out var d))
            {
                desc = d.ValueKind switch
                {
                    JsonValueKind.String => d.GetString(),
                    JsonValueKind.Object when d.TryGetProperty("text", out var t) => t.GetString(),
                    _ => d.ToString()
                };
            }
            return (format, desc);
        }
        catch
        {
            return (0, null);
        }
    }

    /// <summary>
    /// 判断资源路径是否参与冲突比较：只统计 <c>data/</c> 下的实际资源文件。
    /// </summary>
    public static bool IsComparableResource(string relativePath)
    {
        var p = relativePath.Replace('\\', '/').TrimStart('/');
        if (!p.StartsWith("data/", StringComparison.OrdinalIgnoreCase)) return false;
        if (p.EndsWith('/')) return false;
        return p.Count(c => c == '/') >= 3;   // data/<ns>/<类别>/<文件>
    }

    /// <summary>扫描一个存档的数据包目录。</summary>
    public static DataPackReport Scan(string saveDir, string? targetVersion = null, string? gameRoot = null)
    {
        var report = new DataPackReport();
        var dir = DataPacksDir(saveDir);
        if (!Directory.Exists(dir)) return report;

        var order = 0;
        var candidates = Directory.GetFileSystemEntries(dir)
            .OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase);

        foreach (var entry in candidates)
        {
            var info = Directory.Exists(entry) ? ReadFolderPack(entry) : ReadZipPack(entry);
            if (info is null) continue;
            info.LoadOrder = order++;
            report.Packs.Add(info);
        }

        // 格式告警
        if (!string.IsNullOrWhiteSpace(targetVersion))
        {
            var expected = ExpectedFormat(targetVersion!);
            if (expected > 0)
            {
                foreach (var p in report.Packs.Where(p => p.PackFormat > 0 && p.PackFormat != expected))
                    report.FormatWarnings.Add(
                        $"{p.Name}：pack_format={p.PackFormat}，{targetVersion} 期望 {expected}");
            }
        }

        report.Conflicts.AddRange(FindConflicts(report.Packs, LoadRules(gameRoot)));
        return report;
    }

    /// <summary>
    /// 在已解析的包列表中找出冲突（纯函数）。
    /// 同时检测两类冲突：同一文件路径被覆盖、同一命名空间 ID 被多个包定义。
    /// 命中规则库的会升级为对应严重度并附带处理建议。
    /// </summary>
    public static List<DataPackConflict> FindConflicts(
        IEnumerable<DataPackInfo> packs, IEnumerable<ConflictRule>? rules = null)
    {
        var all = packs.ToList();
        var ruleList = (rules ?? BuiltinRules).ToList();
        var result = new List<DataPackConflict>();

        // ① 文件路径级冲突
        var fileMap = new Dictionary<string, List<DataPackInfo>>(StringComparer.OrdinalIgnoreCase);
        foreach (var pack in all)
        {
            foreach (var res in pack.Resources.Where(IsComparableResource))
            {
                var key = res.Replace('\\', '/').TrimStart('/');
                if (!fileMap.TryGetValue(key, out var list)) fileMap[key] = list = new List<DataPackInfo>();
                if (!list.Contains(pack)) list.Add(pack);
            }
        }

        foreach (var kv in fileMap.Where(kv => kv.Value.Count > 1))
        {
            var ordered = kv.Value.OrderBy(p => p.LoadOrder).ToList();
            var rule = MatchRule(kv.Key, ruleList);
            result.Add(new DataPackConflict(
                kv.Key,
                ordered.Select(p => p.Name).ToList(),
                ordered[^1].Name,
                rule is null ? ConflictKind.FileOverride : ConflictKind.KnownRule,
                rule?.Severity ?? ConflictSeverity.Warning,
                rule is null ? null : $"{rule.Description}。{rule.Advice}"));
        }

        // ② 命名空间 ID 级冲突（路径不同但指向同一 ID，文件级查不出来）
        var idMap = new Dictionary<string, List<(DataPackInfo Pack, string Path)>>(StringComparer.OrdinalIgnoreCase);
        foreach (var pack in all)
        {
            foreach (var res in pack.Resources.Where(IsComparableResource))
            {
                var parsed = ParseResourceId(res);
                if (parsed is null) continue;

                var key = $"{parsed.Value.Category}|{parsed.Value.Id}";
                if (!idMap.TryGetValue(key, out var list))
                    idMap[key] = list = new List<(DataPackInfo, string)>();
                if (list.All(x => x.Pack != pack)) list.Add((pack, res.Replace('\\', '/')));
            }
        }

        var seenFiles = new HashSet<string>(
            result.Select(c => c.Resource), StringComparer.OrdinalIgnoreCase);

        foreach (var kv in idMap.Where(kv => kv.Value.Count > 1))
        {
            // 已经被文件级冲突覆盖到的就不重复报
            if (kv.Value.Select(x => x.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1)
                continue;
            if (kv.Value.All(x => seenFiles.Contains(x.Path))) continue;

            var ordered = kv.Value.OrderBy(x => x.Pack.LoadOrder).ToList();
            var id = kv.Key.Split('|', 2)[^1];
            var rule = MatchRule(ordered[0].Path, ruleList);

            result.Add(new DataPackConflict(
                id,
                ordered.Select(x => x.Pack.Name).ToList(),
                ordered[^1].Pack.Name,
                ConflictKind.NamespaceId,
                rule?.Severity ?? ConflictSeverity.Warning,
                rule is null
                    ? "不同文件路径指向同一命名空间 ID，后加载者生效"
                    : $"{rule.Description}。{rule.Advice}"));
        }

        return result
            .OrderByDescending(c => c.Severity)
            .ThenBy(c => c.Resource, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>按游戏版本推断期望的 pack_format（取最接近的主版本）。</summary>
    public static int ExpectedFormat(string version)
    {
        if (KnownPackFormats.TryGetValue(version, out var exact)) return exact;

        var parts = version.Split('.');
        if (parts.Length >= 2)
        {
            var major = $"{parts[0]}.{parts[1]}";
            if (KnownPackFormats.TryGetValue(major, out var m)) return m;
        }
        return 0;
    }

    private static DataPackInfo? ReadFolderPack(string dir)
    {
        try
        {
            var info = new DataPackInfo { Name = Path.GetFileName(dir), Path = dir, IsZip = false };
            var metaPath = Path.Combine(dir, "pack.mcmeta");
            if (File.Exists(metaPath))
            {
                var (fmt, desc) = ParseMeta(File.ReadAllText(metaPath));
                info.PackFormat = fmt;
                info.Description = desc;
            }
            else
            {
                info.MissingMeta = true;
            }

            var dataDir = Path.Combine(dir, "data");
            if (Directory.Exists(dataDir))
            {
                foreach (var f in Directory.GetFiles(dataDir, "*", SearchOption.AllDirectories))
                    info.Resources.Add(Path.GetRelativePath(dir, f).Replace('\\', '/'));
            }
            return info;
        }
        catch
        {
            return null;
        }
    }

    private static DataPackInfo? ReadZipPack(string zipPath)
    {
        if (!zipPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) return null;
        try
        {
            var info = new DataPackInfo
            {
                Name = Path.GetFileName(zipPath),
                Path = zipPath,
                IsZip = true
            };

            using var archive = ZipFile.OpenRead(zipPath);
            var meta = archive.GetEntry("pack.mcmeta");
            if (meta is not null)
            {
                using var reader = new StreamReader(meta.Open());
                var (fmt, desc) = ParseMeta(reader.ReadToEnd());
                info.PackFormat = fmt;
                info.Description = desc;
            }
            else
            {
                info.MissingMeta = true;
            }

            foreach (var e in archive.Entries)
            {
                if (e.FullName.EndsWith('/')) continue;
                info.Resources.Add(e.FullName.Replace('\\', '/'));
            }
            return info;
        }
        catch
        {
            return null;
        }
    }
}
