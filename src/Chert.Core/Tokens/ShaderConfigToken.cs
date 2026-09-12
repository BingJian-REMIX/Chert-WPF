using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Chert.Core.Tokens;

/// <summary>光影配置（可分享的一组光影设置）。</summary>
public class ShaderConfig
{
    /// <summary>光影包文件名（如 ComplementaryUnbound_r5.3.zip）。</summary>
    [JsonPropertyName("pack")]
    public string Pack { get; set; } = "";

    /// <summary>配置档名称（用户自取）。</summary>
    [JsonPropertyName("profile")]
    public string Profile { get; set; } = "默认";

    /// <summary>适用的游戏版本（可空，仅作提示）。</summary>
    [JsonPropertyName("mcVersion")]
    public string? McVersion { get; set; }

    /// <summary>光影加载器：iris / optifine。</summary>
    [JsonPropertyName("loader")]
    public string Loader { get; set; } = "iris";

    /// <summary>选项键值对（对应光影包 shaders.properties 的设置项）。</summary>
    [JsonPropertyName("options")]
    public Dictionary<string, string> Options { get; set; } = new();

    /// <summary>创建者备注。</summary>
    [JsonPropertyName("note")]
    public string? Note { get; set; }
}

/// <summary>光影 Token 解析结果。</summary>
public sealed class ShaderTokenResult
{
    public bool Ok { get; init; }
    public string? Error { get; init; }
    public ShaderConfig? Config { get; init; }

    public static ShaderTokenResult Fail(string error) => new() { Ok = false, Error = error };
}

/// <summary>
/// 光影配置 Token 编解码。
/// <para>格式：<c>SHDR1.&lt;base64url(gzip(json))&gt;.&lt;校验和8位十六进制&gt;</c></para>
/// 用于把一整套光影设置压成一行字符串分享，粘贴即可还原。
/// </summary>
public static class ShaderConfigToken
{
    public const string Prefix = "SHDR1";
    private const char Sep = '.';

    /// <summary>Token 长度上限（防止粘贴超长串导致界面卡顿）。</summary>
    public const int MaxTokenLength = 8192;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>编码为 Token。</summary>
    public static string Encode(ShaderConfig config)
    {
        if (config is null) throw new ArgumentNullException(nameof(config));

        var json = JsonSerializer.Serialize(config, JsonOpts);
        var raw = Encoding.UTF8.GetBytes(json);

        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Optimal, leaveOpen: true))
            gz.Write(raw, 0, raw.Length);

        var payload = ToBase64Url(ms.ToArray());
        return $"{Prefix}{Sep}{payload}{Sep}{Checksum(payload)}";
    }

    /// <summary>解码 Token；任何异常都转成 <see cref="ShaderTokenResult.Fail"/>，不抛出。</summary>
    public static ShaderTokenResult Decode(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return ShaderTokenResult.Fail("Token 为空");

        var t = token.Trim();
        if (t.Length > MaxTokenLength) return ShaderTokenResult.Fail("Token 过长");

        var parts = t.Split(Sep);
        if (parts.Length != 3) return ShaderTokenResult.Fail("Token 格式错误（应为 SHDR1.<数据>.<校验和>）");
        if (!string.Equals(parts[0], Prefix, StringComparison.OrdinalIgnoreCase))
            return ShaderTokenResult.Fail($"版本前缀不支持：{parts[0]}");

        var payload = parts[1];
        if (!string.Equals(Checksum(payload), parts[2], StringComparison.OrdinalIgnoreCase))
            return ShaderTokenResult.Fail("校验和不匹配，Token 可能已损坏");

        try
        {
            var bytes = FromBase64Url(payload);
            using var input = new MemoryStream(bytes);
            using var gz = new GZipStream(input, CompressionMode.Decompress);
            using var outMs = new MemoryStream();
            gz.CopyTo(outMs);

            var json = Encoding.UTF8.GetString(outMs.ToArray());
            var cfg = JsonSerializer.Deserialize<ShaderConfig>(json);
            if (cfg is null) return ShaderTokenResult.Fail("配置内容为空");
            if (string.IsNullOrWhiteSpace(cfg.Pack)) return ShaderTokenResult.Fail("缺少光影包名称");

            cfg.Options ??= new Dictionary<string, string>();
            return new ShaderTokenResult { Ok = true, Config = cfg };
        }
        catch (Exception ex)
        {
            return ShaderTokenResult.Fail($"解码失败：{ex.Message}");
        }
    }

    public static bool IsValid(string? token) => Decode(token).Ok;

    /// <summary>从光影包的 shaders.properties 文本解析出选项字典（<c>key=value</c>，忽略注释）。</summary>
    public static Dictionary<string, string> ParseProperties(string? text)
    {
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(text)) return dict;

        foreach (var line in text.Split('\n'))
        {
            var s = line.Trim();
            if (s.Length == 0 || s.StartsWith('#') || s.StartsWith("//")) continue;
            var idx = s.IndexOf('=');
            if (idx <= 0) continue;
            var key = s[..idx].Trim();
            var val = s[(idx + 1)..].Trim();
            if (key.Length > 0) dict[key] = val;
        }
        return dict;
    }

    /// <summary>把选项字典写回 shaders.properties 文本（按键名排序，便于 diff）。</summary>
    public static string WriteProperties(IDictionary<string, string> options)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Generated by MCLCS shader token");
        foreach (var kv in options.OrderBy(k => k.Key, StringComparer.Ordinal))
            sb.AppendLine($"{kv.Key}={kv.Value}");
        return sb.ToString();
    }

    /// <summary>比较两份配置，返回差异（键 → (左值, 右值)），仅列出不同项。</summary>
    public static Dictionary<string, (string? Left, string? Right)> Diff(ShaderConfig a, ShaderConfig b)
    {
        var result = new Dictionary<string, (string?, string?)>(StringComparer.Ordinal);
        foreach (var key in a.Options.Keys.Union(b.Options.Keys))
        {
            a.Options.TryGetValue(key, out var l);
            b.Options.TryGetValue(key, out var r);
            if (!string.Equals(l, r, StringComparison.Ordinal)) result[key] = (l, r);
        }
        return result;
    }

    private static string Checksum(string payload)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash, 0, 4).ToLowerInvariant();
    }

    private static string ToBase64Url(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string s)
    {
        var t = s.Replace('-', '+').Replace('_', '/');
        switch (t.Length % 4)
        {
            case 2: t += "=="; break;
            case 3: t += "="; break;
            case 1: throw new FormatException("Base64URL 长度非法");
        }
        return Convert.FromBase64String(t);
    }
}
