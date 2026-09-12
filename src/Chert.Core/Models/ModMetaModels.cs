using System.Text.Json.Serialization;

namespace Chert.Core.Models;

// ---- Fabric mod 元数据 (fabric.mod.json) ----

public class FabricModJson
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; }

    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("version")]
    public string Version { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("depends")]
    public Dictionary<string, string>? Depends { get; set; }

    [JsonPropertyName("recommends")]
    public Dictionary<string, string>? Recommends { get; set; }

    [JsonPropertyName("suggests")]
    public Dictionary<string, string>? Suggests { get; set; }

    [JsonPropertyName("breaks")]
    public Dictionary<string, string>? Breaks { get; set; }

    [JsonPropertyName("conflicts")]
    public Dictionary<string, string>? Conflicts { get; set; }
}

// ---- Forge/NeoForge mod 元数据 (META-INF/mods.toml / META-INF/neoforge.mods.toml) ----

/// <summary>mods.toml 中解析出的依赖信息（精简版，不解析完整 TOML）。</summary>
public class ForgeModMeta
{
    public string ModId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Version { get; set; } = "";
    public List<ForgeModDependency> Dependencies { get; set; } = new();
}

public class ForgeModDependency
{
    public string ModId { get; set; } = "";
    public string VersionRange { get; set; } = "";
    public bool Mandatory { get; set; }
    public string Ordering { get; set; } = "NONE"; // NONE | BEFORE | AFTER | BOTH
    public string Side { get; set; } = "BOTH";     // BOTH | CLIENT | SERVER
}

// ---- 通用依赖检查结果 ----

public class DependencyCheckResult
{
    public string ModFileName { get; set; } = "";
    public string ModId { get; set; } = "";
    public string ModName { get; set; } = "";
    public string ModVersion { get; set; } = "";
    public List<MissingDependency> Missing { get; set; } = new();
    public List<ConflictDependency> Conflicts { get; set; } = new();
}

public class MissingDependency
{
    public string DependencyId { get; set; } = "";
    public string VersionRange { get; set; } = "";
    public bool Required { get; set; }
}

public class ConflictDependency
{
    public string ConflictId { get; set; } = "";
    public string InstalledVersion { get; set; } = "";
    public string ConflictRange { get; set; } = "";
}
