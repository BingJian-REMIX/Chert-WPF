using System.Text;
using System.Text.Json;

namespace Chert.Core.Auth;

/// <summary>
/// Authlib-Injector（Yggdrasil）认证。
/// 指定 Yggdrasil 服务器地址 + 邮箱/密码 → 获取 UUID + token。
/// 密码不持久化。
/// </summary>
public class AuthlibInjectorAuthenticator : IAuthenticator
{
    private readonly string _serverUrl;
    private readonly string _email;
    private readonly string _password;
    private readonly HttpClient _client;

    public AuthlibInjectorAuthenticator(HttpClient client, string serverUrl, string email, string password)
    {
        _client = client;
        _serverUrl = serverUrl.TrimEnd('/');
        _email = email;
        _password = password;
    }

    /// <summary>username 参数忽略，实际使用构造时传入的 email。</summary>
    public async Task<AuthSession> AuthenticateAsync(string? username, CancellationToken ct = default)
    {
        var clientToken = Guid.NewGuid().ToString("N");
        var payload = new
        {
            agent = new { name = "Minecraft", version = 1 },
            username = _email,
            password = _password,
            clientToken,
            requestUser = true
        };

        var json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var resp = await _client.PostAsync($"{_serverUrl}/authserver/authenticate", content, ct);
        resp.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        var root = doc.RootElement;
        var accessToken = root.GetProperty("accessToken").GetString()!;
        var profile = root.GetProperty("selectedProfile");
        var uuid = profile.GetProperty("id").GetString()!;
        var name = profile.GetProperty("name").GetString()!;

        string userProperties = "{}";
        if (root.TryGetProperty("user", out var userEl) && userEl.TryGetProperty("properties", out var props))
            userProperties = props.GetRawText();

        return new AuthSession
        {
            Username = name,
            Uuid = uuid,
            AccessToken = accessToken,
            UserType = "mojang",
            UserProperties = userProperties
        };
    }
}
