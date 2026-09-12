using System.Text.Json.Serialization;

namespace Chert.Core.Models;

/// <summary>
/// 单个版本的 version.json（原版、Fabric、Forge 共用此结构）。
/// 关键字段：arguments（多态）、minecraftArguments（旧版）、libraries、assetIndex、downloads。
/// </summary>
public class VersionJson
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("inheritsFrom")]
    public string? InheritsFrom { get; set; }

    [JsonPropertyName("mainClass")]
    public string MainClass { get; set; } = "";

    [JsonPropertyName("minecraftArguments")]
    public string? MinecraftArguments { get; set; }

    [JsonPropertyName("arguments")]
    public Arguments? Arguments { get; set; }

    [JsonPropertyName("libraries")]
    public List<Library> Libraries { get; set; } = new();

    [JsonPropertyName("assetIndex")]
    public AssetIndexInfo? AssetIndex { get; set; }

    [JsonPropertyName("assets")]
    public string? Assets { get; set; }

    [JsonPropertyName("javaVersion")]
    public JavaVersion? JavaVersion { get; set; }

    [JsonPropertyName("downloads")]
    public Dictionary<string, DownloadInfo> Downloads { get; set; } = new();

    [JsonPropertyName("logging")]
    public Logging? Logging { get; set; }

    [JsonPropertyName("minimumLauncherVersion")]
    public int MinimumLauncherVersion { get; set; }

    [JsonPropertyName("releaseTime")]
    public string? ReleaseTime { get; set; }
}

/// <summary>arguments.game / arguments.jvm，元素为字符串或带规则的条目。</summary>
public class Arguments
{
    [JsonPropertyName("game")]
    public List<ArgumentItem> Game { get; set; } = new();

    [JsonPropertyName("jvm")]
    public List<ArgumentItem> Jvm { get; set; } = new();
}

/// <summary>库依赖声明。</summary>
public class Library
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("downloads")]
    public LibraryDownloads? Downloads { get; set; }

    [JsonPropertyName("natives")]
    public Dictionary<string, string>? Natives { get; set; }

    [JsonPropertyName("rules")]
    public List<Rule>? Rules { get; set; }

    [JsonPropertyName("extract")]
    public ExtractSpec? Extract { get; set; }

    [JsonPropertyName("checksums")]
    public List<string>? Checksums { get; set; }

    /// <summary>由 name 解析出的 group:artifact:version。</summary>
    [JsonIgnore]
    public MavenCoordinate Coordinate => MavenCoordinate.Parse(Name);
}

/// <summary>Maven 坐标解析（group:artifact:version[:classifier]）。</summary>
public class MavenCoordinate
{
    public string Group { get; set; } = "";
    public string Artifact { get; set; } = "";
    public string Version { get; set; } = "";
    public string? Classifier { get; set; }

    public static MavenCoordinate Parse(string name)
    {
        var parts = name.Split(':');
        var c = new MavenCoordinate
        {
            Group = parts.Length > 0 ? parts[0] : "",
            Artifact = parts.Length > 1 ? parts[1] : "",
            Version = parts.Length > 2 ? parts[2] : ""
        };
        if (parts.Length > 3) c.Classifier = parts[3];
        return c;
    }

    /// <summary>本地仓库相对路径，如 org/lwjgl/lwjgl/3.3.1/lwjgl-3.3.1.jar。</summary>
    public string LocalPath(string? classifier = null)
    {
        var cl = classifier ?? Classifier;
        var fileName = cl == null
            ? $"{Artifact}-{Version}.jar"
            : $"{Artifact}-{Version}-{cl}.jar";
        return Path.Combine(Group.Replace('.', Path.DirectorySeparatorChar),
                            Artifact,
                            Version,
                            fileName).Replace('\\', '/');
    }
}

public class LibraryDownloads
{
    [JsonPropertyName("artifact")]
    public DownloadInfo? Artifact { get; set; }

    [JsonPropertyName("classifiers")]
    public Dictionary<string, DownloadInfo> Classifiers { get; set; } = new();
}

public class DownloadInfo
{
    [JsonPropertyName("path")]
    public string? Path { get; set; }

    [JsonPropertyName("sha1")]
    public string? Sha1 { get; set; }

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }
}

public class ExtractSpec
{
    [JsonPropertyName("exclude")]
    public List<string> Exclude { get; set; } = new();
}

public class AssetIndexInfo
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("sha1")]
    public string? Sha1 { get; set; }

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("totalSize")]
    public long TotalSize { get; set; }

    [JsonPropertyName("url")]
    public string Url { get; set; } = "";
}

public class JavaVersion
{
    [JsonPropertyName("component")]
    public string Component { get; set; } = "";

    [JsonPropertyName("majorVersion")]
    public int MajorVersion { get; set; }
}

public class Logging
{
    [JsonPropertyName("client")]
    public LoggingClient? Client { get; set; }
}

public class LoggingClient
{
    [JsonPropertyName("argument")]
    public string Argument { get; set; } = "";

    [JsonPropertyName("file")]
    public DownloadInfo? File { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = "";
}
