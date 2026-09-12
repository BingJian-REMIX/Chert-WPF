namespace Chert.Core.Auth;

/// <summary>认证会话信息。</summary>
public class AuthSession
{
    public string Username { get; set; } = "";
    public string Uuid { get; set; } = "";
    public string AccessToken { get; set; } = "0";
    public string UserType { get; set; } = "mojang";
    public string UserProperties { get; set; } = "{}";
}

/// <summary>认证接口。</summary>
public interface IAuthenticator
{
    Task<AuthSession> AuthenticateAsync(string username, CancellationToken ct = default);
}
