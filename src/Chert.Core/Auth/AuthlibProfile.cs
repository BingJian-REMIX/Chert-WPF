using System.Collections.Generic;

namespace Chert.Core.Auth;

/// <summary>外置登录返回的单个可选游戏角色（UUID + 名称）。</summary>
public sealed record AuthlibProfile(string Uuid, string Name);

/// <summary>
/// 外置账号存在多个游戏角色但未设定默认角色时由 <see cref="AuthlibInjectorAuthenticator"/>
/// 抛出。调用方应弹出选择框让用户挑一个角色，再用 <see cref="Profiles"/> 中的某一项
/// 配合 <see cref="AccessToken"/> / <see cref="UserProperties"/> 直接构造最终会话（无需再次请求网络）。
/// </summary>
public sealed class AuthlibProfileSelectionException : Exception
{
    public IReadOnlyList<AuthlibProfile> Profiles { get; }

    public string AccessToken { get; }

    public string UserProperties { get; }

    public AuthlibProfileSelectionException(
        IReadOnlyList<AuthlibProfile> profiles, string accessToken, string userProperties)
        : base(profiles.Count == 0
            ? "该外置账号下还没有创建任何游戏角色。"
            : $"该外置账号下有 {profiles.Count} 个游戏角色，请选择一个用于登录。")
    {
        Profiles = profiles;
        AccessToken = accessToken;
        UserProperties = userProperties;
    }
}
