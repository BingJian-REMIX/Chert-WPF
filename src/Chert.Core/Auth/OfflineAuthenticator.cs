using System.Security.Cryptography;
using System.Text;

namespace Chert.Core.Auth;

/// <summary>离线账号认证：生成与官方一致的离线 UUID。</summary>
public class OfflineAuthenticator : IAuthenticator
{
    public Task<AuthSession> AuthenticateAsync(string username, CancellationToken ct = default)
    {
        return Task.FromResult(new AuthSession
        {
            Username = username,
            Uuid = GenerateOfflineUuid(username),
            AccessToken = "0",
            UserType = "mojang"
        });
    }

    /// <summary>按官方算法生成离线 UUID（基于 "OfflinePlayer:{name}" 的 MD5）。</summary>
    public static string GenerateOfflineUuid(string username)
    {
        using var md5 = MD5.Create();
        var bytes = md5.ComputeHash(Encoding.UTF8.GetBytes("OfflinePlayer:" + username));
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x30);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        var hex = Convert.ToHexString(bytes).ToLowerInvariant();
        return $"{hex[..8]}-{hex[8..12]}-{hex[12..16]}-{hex[16..20]}-{hex[20..]}";
    }
}
