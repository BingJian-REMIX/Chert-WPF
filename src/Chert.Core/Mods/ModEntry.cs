using System.Text.Json.Serialization;

namespace Chert.Core.Mods;

/// <summary>一个已安装的 Mod（含元数据）。</summary>
public class ModEntry
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("fileName")]
    public string FileName { get; set; } = "";

    [JsonPropertyName("installedVersion")]
    public string InstalledVersion { get; set; } = "unknown";

    [JsonPropertyName("latestVersion")]
    public string? LatestVersion { get; set; }

    [JsonPropertyName("projectUrl")]
    public string ProjectUrl { get; set; } = "";

    [JsonPropertyName("loader")]
    public string Loader { get; set; } = "";

    [JsonPropertyName("hasUpdate")]
    public bool HasUpdate => LatestVersion is not null && LatestVersion != InstalledVersion;

    /// <summary>Mod 内部 ID（fabric.mod.json id 或 mods.toml modId）。</summary>
    [JsonPropertyName("modId")]
    public string? ModId { get; set; }

    /// <summary>Mod 声明的依赖（modId -> versionRange）。</summary>
    [JsonPropertyName("depends")]
    public Dictionary<string, string> Depends { get; set; } = new();

    /// <summary>Mod 声明的冲突。</summary>
    [JsonPropertyName("conflicts")]
    public Dictionary<string, string> Conflicts { get; set; } = new();

    /// <summary>元数据是否成功解析。</summary>
    [JsonIgnore]
    public bool MetadataParsed => ModId is not null;
}
