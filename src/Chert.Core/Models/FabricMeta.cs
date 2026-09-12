using System.Text.Json.Serialization;

namespace Chert.Core.Models;

/// <summary>Fabric 元数据（meta.fabricmc.net）。</summary>
public class FabricLoaderVersion
{
    [JsonPropertyName("separator")]
    public string Separator { get; set; } = "-";

    [JsonPropertyName("build")]
    public int Build { get; set; }

    [JsonPropertyName("maven")]
    public string Maven { get; set; } = ""; // net.fabricmc:fabric-loader:0.14.x

    [JsonPropertyName("version")]
    public string Version { get; set; } = "";

    [JsonPropertyName("stable")]
    public bool Stable { get; set; }
}

public class FabricGameVersion
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = "";

    [JsonPropertyName("stable")]
    public bool Stable { get; set; }
}

/// <summary>loader 版本组合（/v2/versions/loader/{mc}/ 返回）。</summary>
public class FabricLoaderEntry
{
    [JsonPropertyName("loader")]
    public FabricLoaderVersion Loader { get; set; } = new();

    [JsonPropertyName("intermediary")]
    public FabricLoaderVersion Intermediary { get; set; } = new();

    [JsonPropertyName("launcherMeta")]
    public FabricLauncherMeta LauncherMeta { get; set; } = new();
}

public class FabricLauncherMeta
{
    [JsonPropertyName("version")]
    public int Version { get; set; }

    [JsonPropertyName("libraries")]
    public List<Library> Libraries { get; set; } = new();

    [JsonPropertyName("mainClass")]
    public Dictionary<string, string> MainClass { get; set; } = new();

    [JsonPropertyName("arguments")]
    public Dictionary<string, List<string>> Arguments { get; set; } = new();
}
