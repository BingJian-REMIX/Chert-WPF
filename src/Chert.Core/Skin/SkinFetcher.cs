using System.Text.Json;
using System.Text.Json.Serialization;

namespace Chert.Core.Skin;

/// <summary>Mojang Sessionserver 返回的玩家档案。</summary>
public class MinecraftProfile
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("properties")]
    public List<ProfileProperty> Properties { get; set; } = new();
}

public class ProfileProperty
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("value")]
    public string Value { get; set; } = "";
}

/// <summary>皮肤信息（解码后的 textures 属性）。</summary>
public class SkinInfo
{
    public string SkinUrl { get; set; } = "";
    public string? CapeUrl { get; set; }
    public string Model { get; set; } = "classic"; // classic | slim
}

/// <summary>
/// Minecraft 皮肤获取：通过 Mojang Sessionserver API 查询玩家皮肤。
/// </summary>
public static class SkinFetcher
{
    /// <summary>通过玩家 UUID 获取皮肤信息。</summary>
    public static async Task<SkinInfo?> FetchByUuidAsync(HttpClient client, string uuid, CancellationToken ct = default)
    {
        try
        {
            var url = $"https://sessionserver.mojang.com/session/minecraft/profile/{uuid}";
            var json = await client.GetStringAsync(url, ct);
            var profile = JsonSerializer.Deserialize<MinecraftProfile>(json);
            return profile is not null ? ParseTextures(profile) : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>通过玩家用户名获取皮肤信息。</summary>
    public static async Task<SkinInfo?> FetchByUsernameAsync(HttpClient client, string username, CancellationToken ct = default)
    {
        try
        {
            // 先获取 UUID
            var uuidUrl = $"https://api.mojang.com/users/profiles/minecraft/{Uri.EscapeDataString(username)}";
            var uuidJson = await client.GetStringAsync(uuidUrl, ct);
            using var doc = JsonDocument.Parse(uuidJson);
            if (!doc.RootElement.TryGetProperty("id", out var idEl)) return null;
            var uuid = idEl.GetString()!;

            return await FetchByUuidAsync(client, uuid, ct);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>从玩家档案的 textures 属性中解析皮肤 URL 和模型类型。</summary>
    private static SkinInfo? ParseTextures(MinecraftProfile profile)
    {
        foreach (var prop in profile.Properties)
        {
            if (prop.Name != "textures") continue;

            try
            {
                var decoded = Convert.FromBase64String(prop.Value);
                var json = System.Text.Encoding.UTF8.GetString(decoded);
                using var doc = JsonDocument.Parse(json);

                var root = doc.RootElement;
                if (!root.TryGetProperty("textures", out var textures)) continue;

                var result = new SkinInfo();

                if (textures.TryGetProperty("SKIN", out var skin))
                {
                    if (skin.TryGetProperty("url", out var skinUrl))
                        result.SkinUrl = skinUrl.GetString() ?? "";
                    if (skin.TryGetProperty("metadata", out var meta)
                        && meta.TryGetProperty("model", out var model))
                        result.Model = model.GetString() ?? "classic";
                }

                if (textures.TryGetProperty("CAPE", out var cape)
                    && cape.TryGetProperty("url", out var capeUrl))
                    result.CapeUrl = capeUrl.GetString();

                return string.IsNullOrEmpty(result.SkinUrl) ? null : result;
            }
            catch
            {
                return null;
            }
        }
        return null;
    }

    /// <summary>生成皮肤下载缓存路径。</summary>
    public static string GetSkinCachePath(string gameRoot, string uuid)
    {
        var cacheDir = System.IO.Path.Combine(gameRoot, "mclcs_cache", "skins");
        System.IO.Directory.CreateDirectory(cacheDir);
        return System.IO.Path.Combine(cacheDir, $"{uuid}.png");
    }

    /// <summary>下载皮肤/披风图片字节（用于本地解码为位图，例如 3D 预览纹理）。Core 层不引用 WPF，故返回原始字节。</summary>
    public static async Task<byte[]?> DownloadImageBytesAsync(HttpClient client, string url, CancellationToken ct = default)
    {
        try
        {
            return await client.GetByteArrayAsync(url, ct);
        }
        catch
        {
            return null;
        }
    }
}
