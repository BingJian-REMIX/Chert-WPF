namespace Chert.Core.Theme;

/// <summary>主题类型。</summary>
public enum ThemeType
{
    Light,
    Dark
}

/// <summary>
/// 主题管理器：存储/读取当前主题偏好，通知 App 层切换 ResourceDictionary。
/// Core 层只负责状态持久化，实际 UI 切换由 App 层处理。
/// </summary>
public static class ThemeManager
{
    private static ThemeType _current = ThemeType.Light;

    /// <summary>当前主题。</summary>
    public static ThemeType Current
    {
        get => _current;
        set
        {
            if (_current == value) return;
            _current = value;
            OnThemeChanged?.Invoke(value);
        }
    }

    /// <summary>主题变更事件（App 层订阅以切换 ResourceDictionary）。</summary>
    public static event Action<ThemeType>? OnThemeChanged;

    /// <summary>从配置文件加载主题偏好。</summary>
    public static void LoadPreference(string gameRoot)
    {
        var path = System.IO.Path.Combine(gameRoot, "mclcs_theme.json");
        if (!System.IO.File.Exists(path)) return;
        try
        {
            var json = System.IO.File.ReadAllText(path);
            var pref = System.Text.Json.JsonSerializer.Deserialize<ThemePreference>(json);
            if (pref is not null && Enum.TryParse<ThemeType>(pref.Theme, true, out var t))
                _current = t;
        }
        catch { }
    }

    /// <summary>保存主题偏好到文件。</summary>
    public static void SavePreference(string gameRoot)
    {
        var path = System.IO.Path.Combine(gameRoot, "mclcs_theme.json");
        var pref = new ThemePreference { Theme = _current.ToString() };
        System.IO.Directory.CreateDirectory(gameRoot);
        System.IO.File.WriteAllText(path,
            System.Text.Json.JsonSerializer.Serialize(pref));
    }

    private class ThemePreference
    {
        public string Theme { get; set; } = "Light";
    }
}
