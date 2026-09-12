using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Chert.Core.UI;

/// <summary>
/// 四色索引贴主标签标识。
    /// 顺序即视觉排列顺序（从左到右：游戏 → 下载 → 工具箱 → 设置），
    /// 左侧标签压在右侧之上（ZIndex 递减）。四贴未选中时均折叠为色块、仅显文字于展开态。
    /// </summary>
public enum MainTabKind
{
    /// <summary>游戏（绿 #4CAF50）。无侧边栏，未选中时同样折叠为色块。</summary>
    Game,
    /// <summary>下载（蓝 #2196F3）。</summary>
    Download,
    /// <summary>工具箱（橙 #FF9800）。</summary>
    Toolbox,
    /// <summary>设置（灰 #607D8B）。</summary>
    Settings
}

/// <summary>
/// 主标签页定义：标识 / 标题 / 默认配色 / 图标 / 排序 / 层叠序 / 是否有侧边栏。
/// 界面层据此渲染四个索引贴主标签，配色可被 <see cref="TabThemeConfig"/> 覆盖。
/// </summary>
public sealed class MainTabDefinition
{
    public MainTabDefinition(
        MainTabKind kind,
        string title,
        string defaultColor,
        string icon,
        int order,
        bool hasSidebar,
        bool alwaysExpanded = false)
    {
        Kind = kind;
        Title = title;
        DefaultColor = defaultColor;
        Icon = icon;
        Order = order;
        HasSidebar = hasSidebar;
        AlwaysExpanded = alwaysExpanded;
    }

    public MainTabKind Kind { get; }

    /// <summary>显示标题。</summary>
    public string Title { get; }

    /// <summary>默认配色（#RRGGBB）。</summary>
    public string DefaultColor { get; }

    /// <summary>图标名（界面层自行映射字体图标）。</summary>
    public string Icon { get; }

    /// <summary>展示顺序（从 0 起，左 → 右）。</summary>
    public int Order { get; }

    /// <summary>该页是否带副标签侧边栏（游戏页无）。</summary>
    public bool HasSidebar { get; }

    /// <summary>
    /// 是否常驻展开（不收缩为图标态）。
    /// 仅游戏页为 true —— 它在最左侧，左边没有可以覆盖它的标签，收起没有意义。
    /// <para>
    /// <b>仅影响布局</b>：宽度恒为 <see cref="MainTabs.ExpandedWidth"/> 且常显文字。
    /// <b>不影响状态表达</b>：提亮与选中细线只由"是否为当前页"决定。
    /// 否则游戏标签会永远亮着细线，与真正选中的标签同时高亮，无法分辨当前页。
    /// </para>
    /// </summary>
    public bool AlwaysExpanded { get; }

    /// <summary>层叠序：左侧压右侧，Order 越小 ZIndex 越大。</summary>
    public int ZIndex => MainTabs.All.Count - Order;
}

/// <summary>四色主标签注册表（对齐需求规格 1.2）。</summary>
public static class MainTabs
{
    /// <summary>游戏 - 绿。</summary>
    public const string DefaultGameColor = "#4CAF50";

    /// <summary>下载 - 蓝。</summary>
    public const string DefaultDownloadColor = "#2196F3";

    /// <summary>工具箱 - 橙。</summary>
    public const string DefaultToolboxColor = "#FF9800";

    /// <summary>设置 - 灰。</summary>
    public const string DefaultSettingsColor = "#607D8B";

    /// <summary>收起态宽度（px）。</summary>
    public const double CollapsedWidth = 56;

    /// <summary>展开态宽度（px）。</summary>
    public const double ExpandedWidth = 130;

    /// <summary>收起态相邻重叠（px，负 Margin 取值）。</summary>
    public const double CollapsedOverlap = 20;

    /// <summary>展开态相邻重叠（px）。</summary>
    public const double ExpandedOverlap = 10;

    /// <summary>标签高度（px）。</summary>
    public const double TabHeight = 30;

    /// <summary>选中指示细线高度（px）。</summary>
    public const double UnderlineHeight = 3;

    /// <summary>宽度 / 细线滑动动画时长（毫秒）。</summary>
    public const int TransitionMs = 200;

    /// <summary>悬停响应时长（毫秒）。</summary>
    public const int HoverMs = 150;

    /// <summary>四个主标签，按 Order 升序（左 → 右）。</summary>
    public static IReadOnlyList<MainTabDefinition> All { get; } = new List<MainTabDefinition>
    {
        new(MainTabKind.Game,     "tab.game",    DefaultGameColor,     "gamepad",  0, hasSidebar: false),
        new(MainTabKind.Download, "tab.download", DefaultDownloadColor, "download", 1, hasSidebar: true),
        new(MainTabKind.Toolbox,  "tab.toolbox",  DefaultToolboxColor,  "toolbox",  2, hasSidebar: true),
        new(MainTabKind.Settings, "tab.settings", DefaultSettingsColor, "cog",      3, hasSidebar: true)
    };

    public static MainTabDefinition Get(MainTabKind kind) => All.First(t => t.Kind == kind);

    /// <summary>按标题查找（找不到返回 null）。</summary>
    public static MainTabDefinition? ByTitle(string? title) =>
        string.IsNullOrWhiteSpace(title) ? null : All.FirstOrDefault(t => t.Title == title);

    /// <summary>按 Id 字符串查找（game / download / toolbox / settings）。</summary>
    public static MainTabDefinition? ById(string? id) => ParseKind(id) is { } k ? Get(k) : null;

    /// <summary>字符串 → 枚举（大小写不敏感）。</summary>
    public static MainTabKind? ParseKind(string? id) => id?.Trim().ToLowerInvariant() switch
    {
        "game" => MainTabKind.Game,
        "download" => MainTabKind.Download,
        "toolbox" => MainTabKind.Toolbox,
        "settings" => MainTabKind.Settings,
        _ => null
    };

    /// <summary>枚举 → 字符串 Id。</summary>
    public static string ToId(MainTabKind kind) => kind switch
    {
        MainTabKind.Game => "game",
        MainTabKind.Download => "download",
        MainTabKind.Toolbox => "toolbox",
        MainTabKind.Settings => "settings",
        _ => "game"
    };
}

/// <summary>
/// 主标签配色配置（持久化到 LauncherProfile）。允许用户自定义四色，非法值自动回退默认。
/// </summary>
public class TabThemeConfig
{
    [JsonPropertyName("game")]
    public string Game { get; set; } = MainTabs.DefaultGameColor;

    [JsonPropertyName("download")]
    public string Download { get; set; } = MainTabs.DefaultDownloadColor;

    [JsonPropertyName("toolbox")]
    public string Toolbox { get; set; } = MainTabs.DefaultToolboxColor;

    [JsonPropertyName("settings")]
    public string Settings { get; set; } = MainTabs.DefaultSettingsColor;

    /// <summary>
    /// 标题栏是否跟随当前主标签变色。
    /// true = 切到下载页标题栏变蓝（模板行为）；false = 恒定主题色。
    /// </summary>
    [JsonPropertyName("titleBarFollowsTab")]
    public bool TitleBarFollowsTab { get; set; } = true;

    /// <summary>
    /// 选中细线的提亮系数。细线与标签同色时不可见，需提亮才能看出选中态。
    /// 1.0 = 完全同色（不可见），推荐 1.45。
    /// </summary>
    [JsonPropertyName("underlineBrightness")]
    public double UnderlineBrightness { get; set; } = 1.45;

    private static readonly Regex HexColor =
        new("^#(?:[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$", RegexOptions.Compiled);

    /// <summary>校验 #RRGGBB 或 #AARRGGBB。</summary>
    public static bool IsValidColor(string? color) =>
        !string.IsNullOrWhiteSpace(color) && HexColor.IsMatch(color!.Trim());

    /// <summary>取某个标签的实际配色；自定义值非法时回退默认色。</summary>
    public string ColorOf(MainTabKind kind)
    {
        var custom = kind switch
        {
            MainTabKind.Game => Game,
            MainTabKind.Download => Download,
            MainTabKind.Toolbox => Toolbox,
            MainTabKind.Settings => Settings,
            _ => null
        };
        return IsValidColor(custom) ? custom!.Trim().ToUpperInvariant() : MainTabs.Get(kind).DefaultColor;
    }

    /// <summary>设置某个标签的配色；非法值返回 false 且不修改。</summary>
    public bool SetColor(MainTabKind kind, string? color)
    {
        if (!IsValidColor(color)) return false;
        var v = color!.Trim().ToUpperInvariant();
        switch (kind)
        {
            case MainTabKind.Game: Game = v; break;
            case MainTabKind.Download: Download = v; break;
            case MainTabKind.Toolbox: Toolbox = v; break;
            case MainTabKind.Settings: Settings = v; break;
            default: return false;
        }
        return true;
    }

    /// <summary>
    /// 取选中细线颜色：标签色按 <see cref="UnderlineBrightness"/> 提亮，各通道钳制到 255。
    /// 返回 #RRGGBB。
    /// </summary>
    public string UnderlineColorOf(MainTabKind kind) =>
        Brighten(ColorOf(kind), UnderlineBrightness);

    /// <summary>取未选中标签的暗化色（模板 filter:brightness(.7)）。</summary>
    public string DimColorOf(MainTabKind kind) => Brighten(ColorOf(kind), 0.7);

    /// <summary>取选中标签的提亮色（模板 filter:brightness(1.12)）。</summary>
    public string ActiveColorOf(MainTabKind kind) => Brighten(ColorOf(kind), 1.12);

    /// <summary>
    /// 按系数缩放 RGB 亮度。factor &gt; 1 提亮，&lt; 1 变暗，各通道钳制在 0-255。
    /// 输入非法时原样返回。
    /// </summary>
    public static string Brighten(string hex, double factor)
    {
        if (!IsValidColor(hex)) return hex;
        var s = hex.Trim().TrimStart('#');
        // 兼容 #AARRGGBB：取后 6 位
        if (s.Length == 8) s = s.Substring(2);

        var r = Convert.ToInt32(s.Substring(0, 2), 16);
        var g = Convert.ToInt32(s.Substring(2, 2), 16);
        var b = Convert.ToInt32(s.Substring(4, 2), 16);

        static int Scale(int c, double f) => Math.Clamp((int)Math.Round(c * f), 0, 255);

        return $"#{Scale(r, factor):X2}{Scale(g, factor):X2}{Scale(b, factor):X2}";
    }

    /// <summary>是否已被用户改动过（与默认四色不同）。</summary>
    public bool IsCustomized() =>
        MainTabs.All.Any(t => !string.Equals(ColorOf(t.Kind), t.DefaultColor, StringComparison.OrdinalIgnoreCase));

    /// <summary>恢复默认四色。</summary>
    public void Reset()
    {
        Game = MainTabs.DefaultGameColor;
        Download = MainTabs.DefaultDownloadColor;
        Toolbox = MainTabs.DefaultToolboxColor;
        Settings = MainTabs.DefaultSettingsColor;
    }
}
