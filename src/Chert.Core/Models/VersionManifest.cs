using System.Text.Json.Serialization;

namespace Chert.Core.Models;

/// <summary>
/// 官方版本清单（version_manifest_v2.json，托管于 piston-meta.mojang.com）。
/// v2 在 v1 基础上为每条版本增加了 sha1 / complianceLevel 字段，url 仍指向 piston-meta 的包地址。
/// </summary>
public class VersionManifest
{
    [JsonPropertyName("latest")]
    public LatestVersion Latest { get; set; } = new();

    [JsonPropertyName("versions")]
    public List<VersionEntry> Versions { get; set; } = new();
}

public class LatestVersion
{
    [JsonPropertyName("release")]
    public string Release { get; set; } = "";

    [JsonPropertyName("snapshot")]
    public string Snapshot { get; set; } = "";
}

public class VersionEntry
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("url")]
    public string Url { get; set; } = "";

    [JsonPropertyName("time")]
    public string Time { get; set; } = "";

    [JsonPropertyName("releaseTime")]
    public string ReleaseTime { get; set; } = "";
}
