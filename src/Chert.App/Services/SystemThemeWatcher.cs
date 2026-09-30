using Chert.Core.Theme;
using Microsoft.Win32;

namespace Chert.App.Services;

/// <summary>
/// 清单 #18：跟随操作系统主题自动切换亮 / 暗。
/// <para>
/// Windows 把「应用使用浅色主题」存在
/// <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize\AppsUseLightTheme</c>，
/// 本类读取该值并通过 <see cref="ThemeManager.Current"/> 落盘；实际换字典由 App 层订阅
/// <see cref="ThemeManager.OnThemeChanged"/> 完成。
/// </para>
/// <para>
/// 监听走 <see cref="SystemEvents.UserPreferenceChanged"/>（系统设置变化广播），
/// 用户可在设置页随时关闭跟随，关闭后保持手动选择的主题。
/// 注册表读取失败 / 非 Windows 环境一律静默，绝不抛给调用方。
/// </para>
/// </summary>
public static class SystemThemeWatcher
{
    private const string PersonalizeKeyPath =
        @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private const string AppsUseLightTheme = "AppsUseLightTheme";

    private static bool _started;

    /// <summary>启动监听（幂等）。未开启跟随时只注册事件、不改主题。</summary>
    public static void Start()
    {
        if (_started) return;
        _started = true;

        try
        {
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        }
        catch
        {
            // 事件注册失败不影响其它功能
        }

        ApplyFromSystem();
    }

    /// <summary>停止监听（退出时调用，避免静态事件悬挂）。</summary>
    public static void Stop()
    {
        try
        {
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        }
        catch
        {
            // 忽略
        }
        _started = false;
    }

    private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        // 主题切换会广播 General（ immersive color set ）与 VisualStyle 两类
        if (e.Category is not (UserPreferenceCategory.General or UserPreferenceCategory.VisualStyle))
            return;

        ApplyFromSystem();
    }

    /// <summary>读取系统主题并应用；未开启跟随或读取失败时不做任何事。</summary>
    public static void ApplyFromSystem()
    {
        if (!ThemeManager.FollowSystem) return;

        var light = IsSystemLightTheme();
        if (light is null) return;

        var target = light.Value ? ThemeType.Light : ThemeType.Dark;
        if (ThemeManager.Current == target) return;

        ThemeManager.Current = target;
    }

    /// <summary>系统当前是否为「应用使用浅色主题」；读取失败返回 null。</summary>
    public static bool? IsSystemLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKeyPath);
            if (key is null) return null;

            var raw = key.GetValue(AppsUseLightTheme);
            return raw switch
            {
                int i => i != 0,
                long l => l != 0,
                string s when int.TryParse(s, out var n) => n != 0,
                _ => null
            };
        }
        catch
        {
            return null;
        }
    }
}
