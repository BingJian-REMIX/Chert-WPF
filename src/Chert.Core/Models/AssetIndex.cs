using System.Text.Json.Serialization;

namespace Chert.Core.Models;

/// <summary>资源索引文件（assets/indexes/{id}.json）。</summary>
public class AssetIndex
{
    [JsonPropertyName("objects")]
    public Dictionary<string, AssetObject> Objects { get; set; } = new();
}

public class AssetObject
{
    [JsonPropertyName("hash")]
    public string Hash { get; set; } = "";

    [JsonPropertyName("size")]
    public long Size { get; set; }
}
