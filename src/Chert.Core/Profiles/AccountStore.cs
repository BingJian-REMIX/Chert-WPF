using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Chert.Core.Profiles;

/// <summary>
/// 一条账号记录。
/// <para>★ 实现 <see cref="INotifyPropertyChanged"/>：本类型是**绑定列表里的实体**
/// （设置页账号列表 / 游戏页账号下拉 / 版本设置页绑定下拉），直接改属性若不发通知，
/// 界面会静默停在旧值 —— 集合增删会发通知，但「改属性」不会。</para>
/// </summary>
public class AccountEntry : INotifyPropertyChanged
{
    private string _id = Guid.NewGuid().ToString("N");
    private string _displayName = "";
    private string _authType = "offline";
    private string _username = "";
    private string _uuid = "";
    private string _accessToken = "0";
    private string? _refreshToken;
    private string? _msExpiresAt;
    private string? _authlibServerUrl;
    private string? _lastUsed;
    private string? _skinUrl;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>就地改属性并通知（供代码内部使用，避免手写 OnPropertyChanged）。</summary>
    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        // 派生属性（AuthTypeText / ServerText 等）依赖上面这些字段，必须一并通知
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AuthTypeText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ServerHost)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SubtitleText)));
        return true;
    }

    [JsonPropertyName("id")]
    public string Id { get => _id; set => Set(ref _id, value); }

    [JsonPropertyName("displayName")]
    public string DisplayName { get => _displayName; set => Set(ref _displayName, value); }

    [JsonPropertyName("authType")]
    public string AuthType { get => _authType; set => Set(ref _authType, value); }

    [JsonPropertyName("username")]
    public string Username { get => _username; set => Set(ref _username, value); }

    [JsonPropertyName("uuid")]
    public string Uuid { get => _uuid; set => Set(ref _uuid, value); }

    [JsonPropertyName("accessToken")]
    public string AccessToken { get => _accessToken; set => Set(ref _accessToken, value); }

    [JsonPropertyName("refreshToken")]
    public string? RefreshToken { get => _refreshToken; set => Set(ref _refreshToken, value); }

    [JsonPropertyName("msExpiresAt")]
    public string? MsExpiresAt { get => _msExpiresAt; set => Set(ref _msExpiresAt, value); }

    [JsonPropertyName("authlibServerUrl")]
    public string? AuthlibServerUrl { get => _authlibServerUrl; set => Set(ref _authlibServerUrl, value); }

    [JsonPropertyName("lastUsed")]
    public string? LastUsed { get => _lastUsed; set => Set(ref _lastUsed, value); }

    [JsonPropertyName("skinUrl")]
    public string? SkinUrl { get => _skinUrl; set => Set(ref _skinUrl, value); }

    // ===== 以下为纯派生的展示属性（不落盘，供列表显示）=====

    /// <summary>
    /// 登录方式的中文名（离线 / 微软 / 外置）。列表里直接显示 <see cref="AuthType"/>
    /// 只会看到 authlib / offline 这类英文技术词，多角色时完全分不清谁是谁。
    /// </summary>
    [JsonIgnore]
    public string AuthTypeText => AuthType switch
    {
        "microsoft" => "微软登录",
        "authlib" => "外置登录",
        _ => "离线",
    };

    /// <summary>
    /// 外置登录的服务器主机名（不含协议与路径），用于区分**同一服务下的不同角色**。
    /// 非外置登录返回空串。
    /// </summary>
    [JsonIgnore]
    public string ServerHost
    {
        get
        {
            if (AuthType != "authlib" || string.IsNullOrWhiteSpace(AuthlibServerUrl)) return "";
            var u = AuthlibServerUrl.Trim();
            // 去掉协议前缀与末尾斜杠
            if (u.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) u = u["https://".Length..];
            else if (u.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) u = u["http://".Length..];
            u = u.TrimEnd('/');
            // 再去掉路径（Yggdrasil 常见 .../api/yggdrasil）
            var slash = u.IndexOf('/');
            if (slash > 0) u = u[..slash];
            return u;
        }
    }

    /// <summary>列表副标题：外置登录显示「外置登录 · 主机名」，其余显示登录方式。</summary>
    [JsonIgnore]
    public string SubtitleText
    {
        get
        {
            var host = ServerHost;
            return string.IsNullOrEmpty(host) ? AuthTypeText : $"{AuthTypeText} · {host}";
        }
    }
}

/// <summary>多账号存储（mclcs_accounts.json）。</summary>
public static class AccountStore
{
    /// <summary>
    /// 账号列表发生变化（新增 / 更新 / 删除）时触发，参数为 gameRoot。
    /// 供各 UI 页（游戏页账号下拉、设置页账号列表、版本设置页绑定下拉）同步刷新。
    /// </summary>
    public static event Action<string>? Changed;

    private static string Path(string gameRoot) => System.IO.Path.Combine(gameRoot, "mclcs_accounts.json");

    public static List<AccountEntry> Load(string gameRoot)
    {
        var p = Path(gameRoot);
        if (!File.Exists(p)) return new List<AccountEntry>();
        try
        {
            return JsonSerializer.Deserialize<List<AccountEntry>>(File.ReadAllText(p)) ?? new();
        }
        catch
        {
            return new List<AccountEntry>();
        }
    }

    public static void Save(string gameRoot, List<AccountEntry> accounts)
    {
        Directory.CreateDirectory(gameRoot);
        var json = JsonSerializer.Serialize(accounts, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path(gameRoot), json);
    }

    public static AccountEntry? GetLastUsed(string gameRoot)
    {
        var accounts = Load(gameRoot);
        return accounts
            .Where(a => !string.IsNullOrEmpty(a.LastUsed))
            .OrderByDescending(a => a.LastUsed)
            .FirstOrDefault()
            ?? accounts.FirstOrDefault();
    }

    /// <summary>
    /// 解析「启动某版本时应使用的账号」：优先返回 <paramref name="boundAccountId"/> 对应的账号
    /// （实现每版本独立账户绑定），找不到时回落到全局「最后使用」。
    /// </summary>
    public static AccountEntry? GetForVersion(string gameRoot, string? boundAccountId)
    {
        if (!string.IsNullOrWhiteSpace(boundAccountId))
        {
            var byId = Load(gameRoot).FirstOrDefault(a => a.Id == boundAccountId);
            if (byId is not null) return byId;
        }
        return GetLastUsed(gameRoot);
    }

    public static void MarkUsed(string gameRoot, string accountId)
    {
        var accounts = Load(gameRoot);
        var entry = accounts.Find(a => a.Id == accountId);
        if (entry is not null)
        {
            entry.LastUsed = DateTime.UtcNow.ToString("o");
            Save(gameRoot, accounts);
        }
    }

    public static void Upsert(string gameRoot, AccountEntry account)
    {
        var accounts = Load(gameRoot);
        var idx = accounts.FindIndex(a => a.Id == account.Id);
        if (idx >= 0) accounts[idx] = account;
        else accounts.Add(account);
        account.LastUsed = DateTime.UtcNow.ToString("o");
        Save(gameRoot, accounts);
        Changed?.Invoke(gameRoot);
    }

    public static bool Remove(string gameRoot, string accountId)
    {
        var accounts = Load(gameRoot);
        var removed = accounts.RemoveAll(a => a.Id == accountId) > 0;
        if (removed)
        {
            Save(gameRoot, accounts);
            Changed?.Invoke(gameRoot);
        }
        return removed;
    }
}
