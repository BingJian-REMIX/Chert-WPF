using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using Chert.Core.Localization;
using Chert.Core.Theme;

namespace Chert.App.Themes;

/// <summary>
/// 节日特效管理器（第一期）：以「置顶半透明叠加层」方式在现有界面之上叠加节日资源，
/// <b>不整体换肤 / 不切换主题</b>，仅在 Application 资源字典末尾追加一层半透明特效字典。
/// <para>
/// 设计约束（来自任务规格）：
/// <list type="bullet">
/// <item>任何失败（网络 / JSON 解析 / XAML 加载）都<b>静默回退默认外观</b>，绝不阻塞启动。</item>
/// <item>兼容 <see cref="ThemeManager.OnThemeChanged"/>：用户手动切换亮 / 暗后，叠加层会被
/// 新主题字典压到下层，因此必须订阅事件做<b>兜底重插</b>，把叠加层重新移到末尾。</item>
/// </list>
/// </para>
/// </summary>
public static class SeasonalThemeManager
{
    /// <summary>节日 key → 特效字典文件名（位于 <c>Themes/Seasonal/</c>）。新增节日只需在此登记并放入对应 XAML。</summary>
    private static readonly Dictionary<string, string> OverlayFiles =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["midautumn"] = "MidAutumnOverlay.xaml"
        };

    private static ResourceDictionary? _overlay;
    private static string _activeKey = "";
    private static string _greetedKey = "";
    private static bool _subscribed;
    private static bool _enabled = true;

    /// <summary>当前生效的节日 key；未生效时为空。</summary>
    public static string CurrentSeasonKey => _activeKey;

    /// <summary>
    /// 是否启用节日特效（对应设置项「关闭节日特效」）。置为 false 时立即移除叠加层。
    /// </summary>
    public static bool Enabled
    {
        get => _enabled;
        set
        {
            _enabled = value;
            if (!value) Clear();
        }
    }

    /// <summary>
    /// 启动时的异步初始化：拉取配置 → 选出当前节日 → 挂载特效叠加层 → 弹一次节日问候。
    /// 本方法<b>不抛异常</b>，任何异常都被吞掉并回退到默认外观。
    /// </summary>
    public static async Task InitializeAsync(string gameRoot)
    {
        try
        {
            EnsureSubscribed();

            if (!_enabled)
            {
                Clear();
                return;
            }

            var config = await HolidayConfig.LoadAsync(gameRoot).ConfigureAwait(false);
            var season = config.PickActive(DateTimeOffset.Now);
            if (season is null || string.IsNullOrWhiteSpace(season.Key))
            {
                // 不在任何节日档期内 → 确保没有残留叠加层
                Clear();
                return;
            }

            _activeKey = season.Key;

            if (!TryLoadOverlay(season.Key, out var dict) || dict is null)
                return;

            _overlay = dict;
            Reapply();
            GreetOnce(season.Key);
        }
        catch
        {
            // 节日特效为非关键功能：全链路静默失败，界面保持默认外观
        }
    }

    /// <summary>移除节日叠加层（不入错线程；无叠加层时为空操作）。</summary>
    public static void Clear()
    {
        var app = Application.Current;
        var overlay = _overlay;
        if (app is null || overlay is null) return;

        void Act() => app.Resources.MergedDictionaries.Remove(overlay);

        try
        {
            if (app.Dispatcher.CheckAccess()) Act();
            else app.Dispatcher.Invoke(Act);
        }
        catch
        {
            // 忽略：线程已关闭等
        }

        _overlay = null;
        _activeKey = "";
    }

    /// <summary>
    /// 把叠加层重新插到资源字典末尾（幂等）。
    /// <para>
    /// 之所以必须「先移除再追加」：App.ApplyTheme 在切换亮 / 暗时会
    /// <b>移除旧主题字典并把新主题字典 Add 到末尾</b>，若叠加层原地不动，
    /// 就会从「末位」变成新主题字典之前，从而被主题字典的键值覆盖、特效失效。
    /// </para>
    /// </summary>
    private static void Reapply()
    {
        var app = Application.Current;
        var overlay = _overlay;
        if (app is null || overlay is null) return;

        void Act()
        {
            var md = app.Resources.MergedDictionaries;
            md.Remove(overlay);
            md.Add(overlay);
        }

        try
        {
            if (app.Dispatcher.CheckAccess()) Act();
            else app.Dispatcher.Invoke(Act);
        }
        catch
        {
            // 忽略
        }
    }

    /// <summary>订阅主题变更，保证用户手动切换亮 / 暗后节日特效层仍在最上层。</summary>
    private static void EnsureSubscribed()
    {
        if (_subscribed) return;
        _subscribed = true;
        ThemeManager.OnThemeChanged += _ => Reapply();
    }

    /// <summary>按节日 key 加载对应的特效资源字典；无登记或加载失败返回 false。</summary>
    private static bool TryLoadOverlay(string key, out ResourceDictionary? dict)
    {
        dict = null;
        if (!OverlayFiles.TryGetValue(key, out var file)) return false;

        try
        {
            var asm = typeof(SeasonalThemeManager).Assembly.GetName().Name;
            var uri = new Uri($"pack://application:,,,/{asm};component/Themes/Seasonal/{file}",
                UriKind.Absolute);
            dict = new ResourceDictionary { Source = uri };
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>每个节日在每次运行中只问候一次；词条缺失时直接不打扰用户。</summary>
    private static void GreetOnce(string key)
    {
        if (string.Equals(_greetedKey, key, StringComparison.OrdinalIgnoreCase)) return;
        _greetedKey = key;

        try
        {
            var titleKey = $"seasonal.{key}.greeting.title";
            var msgKey = $"seasonal.{key}.greeting.message";

            // 词条缺失时 LocaleManager.T 会原样返回 key，据此静默跳过
            var title = LocaleManager.T(titleKey);
            if (string.Equals(title, titleKey, StringComparison.Ordinal)) return;

            Chert.App.Services.ToastService.Show(title, LocaleManager.T(msgKey));
        }
        catch
        {
            // 问候失败不影响特效本身
        }
    }
}
