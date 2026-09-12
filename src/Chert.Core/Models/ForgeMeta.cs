using System.Text.Json.Serialization;

namespace Chert.Core.Models;

/// <summary>BMCLAPI Forge 版本列表条目（/forge/minecraft 返回）。</summary>
public class ForgePromotion
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = ""; // 版本号，如 "1.20.1"

    [JsonPropertyName("promos")]
    public Dictionary<string, int> Promos { get; set; } = new(); // "recommended" -> build, "latest" -> build
}

public class ForgeVersionEntry
{
    [JsonPropertyName("mcversion")]
    public string McVersion { get; set; } = "";

    [JsonPropertyName("version")]
    public string Version { get; set; } = ""; // 形如 1.20.1-47.1.0

    [JsonPropertyName("build")]
    public int Build { get; set; }

    [JsonPropertyName("files")]
    public List<ForgeFileInfo> Files { get; set; } = new();
}

public class ForgeFileInfo
{
    [JsonPropertyName("format")]
    public string Format { get; set; } = ""; // "jar"

    [JsonPropertyName("category")]
    public string Category { get; set; } = ""; // "installer" | "universal" | "client" ...

    [JsonPropertyName("hash")]
    public string Hash { get; set; } = "";

    [JsonPropertyName("href")]
    public string Href { get; set; } = "";
}
