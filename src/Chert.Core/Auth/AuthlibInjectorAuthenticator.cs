using System.Net;
using System.Text;
using System.Text.Json;

namespace Chert.Core.Auth;

/// <summary>
/// Authlib-Injector（Yggdrasil）认证。
/// 指定 Yggdrasil 服务器地址 + 邮箱/密码 → 获取 UUID + token。
/// 密码不持久化。
/// <para>
/// 所有失败路径统一抛 <see cref="AuthException"/> 且消息为可直接展示的中文说明；
/// 不再让 <c>JsonElement.GetProperty</c> 的 <c>KeyNotFoundException</c>
/// （"The given key was not present in the dictionary."）或裸 HTTP 状态码冒到界面上。
/// </para>
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

        HttpResponseMessage resp;
        string body;
        try
        {
            resp = await _client.PostAsync($"{_serverUrl}/authserver/authenticate", content, ct);
            body = await resp.Content.ReadAsStringAsync(ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new AuthException(
                $"连接外置登录服务器失败：{ex.Message}。请确认服务器地址可访问（网络 / 代理 / 地址拼写）。", ex);
        }

        using (resp)
        {
            if (!resp.IsSuccessStatusCode)
                throw new AuthException(DescribeHttpFailure(resp.StatusCode, body));

            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(body);
            }
            catch (JsonException ex)
            {
                throw new AuthException(
                    "外置登录服务器返回的内容不是合法 JSON。请确认填写的地址是 Yggdrasil API 根地址"
                    + "（例如 https://littleskin.cn/api/yggdrasil）。", ex);
            }

            using (doc)
            {
                var root = doc.RootElement;

                // ---- accessToken ----
                if (!root.TryGetProperty("accessToken", out var tokenEl)
                    || tokenEl.ValueKind != JsonValueKind.String
                    || string.IsNullOrWhiteSpace(tokenEl.GetString()))
                {
                    throw new AuthException("外置登录失败：服务器没有返回 accessToken，请确认地址指向的是 Yggdrasil 认证服务。");
                }

                // ---- selectedProfile ----
                // Yggdrasil 规范：账号下没有「已选角色」时不下发 selectedProfile。
                // 外置登录站点（如 LittleSkin）要求先在站点里创建角色，否则拿不到 UUID。
                if (!root.TryGetProperty("selectedProfile", out var profile)
                    || profile.ValueKind != JsonValueKind.Object)
                {
                    var available = 0;
                    if (root.TryGetProperty("availableProfiles", out var arr)
                        && arr.ValueKind == JsonValueKind.Array)
                    {
                        available = arr.GetArrayLength();
                    }

                    throw new AuthException(available == 0
                        ? "该外置账号下还没有创建任何游戏角色。请先到外置登录站点创建一个角色，再回来重试。"
                        : $"该外置账号下有 {available} 个游戏角色但没有设定默认角色，请到外置登录站点设置后再试。");
                }

                var uuid = profile.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? "" : "";
                var name = profile.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? "" : "";
                if (string.IsNullOrWhiteSpace(uuid) || string.IsNullOrWhiteSpace(name))
                    throw new AuthException("外置登录失败：服务器返回的角色信息不完整（缺少 id 或 name）。");

                string userProperties = "{}";
                if (root.TryGetProperty("user", out var userEl)
                    && userEl.ValueKind == JsonValueKind.Object
                    && userEl.TryGetProperty("properties", out var props))
                {
                    userProperties = props.GetRawText();
                }

                return new AuthSession
                {
                    Username = name,
                    Uuid = uuid,
                    AccessToken = tokenEl.GetString()!,
                    UserType = "mojang",
                    UserProperties = userProperties
                };
            }
        }
    }

    /// <summary>解析 Yggdrasil 错误体（error / errorMessage）。</summary>
    private static (string Error, string Message) ParseError(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var r = doc.RootElement;
            var err = r.TryGetProperty("error", out var e) ? e.GetString() ?? "" : "";
            var msg = r.TryGetProperty("errorMessage", out var m) ? m.GetString() ?? "" : "";
            return (err, msg);
        }
        catch
        {
            return ("", "");
        }
    }

    /// <summary>把 HTTP 失败 + Yggdrasil 错误体翻译成可读提示。</summary>
    private static string DescribeHttpFailure(HttpStatusCode status, string body)
    {
        var (error, message) = ParseError(body);

        if (status == HttpStatusCode.NotFound)
        {
            return "外置登录服务器地址不存在（404）。请确认填的是 Yggdrasil API 根地址，"
                 + "例如 https://littleskin.cn/api/yggdrasil 。";
        }

        if (error == "ForbiddenOperationException"
            && message.Contains("Invalid credentials", StringComparison.OrdinalIgnoreCase))
        {
            return "账号或密码不正确。注意这里要填的是外置登录站点的账号（通常是邮箱），不是微软账号。";
        }

        if (!string.IsNullOrWhiteSpace(message))
            return $"外置登录失败：{message}";

        if (!string.IsNullOrWhiteSpace(error))
            return $"外置登录失败：服务器返回 {error}（HTTP {(int)status}）。";

        return $"外置登录失败：服务器返回 HTTP {(int)status}（{status}）。";
    }
}
