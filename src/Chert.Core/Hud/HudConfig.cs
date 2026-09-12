using System.Text.Json.Serialization;

namespace Chert.Core.Hud;

/// <summary>HUD 悬浮窗的停靠位置。</summary>
public enum HudAnchor
{
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
    Custom
}

/// <summary>HUD 可显示的字段。</summary>
[Flags]
public enum HudField
{
    None = 0,
    Fps = 1 << 0,
    Memory = 1 << 1,
    Cpu = 1 << 2,
    Ping = 1 << 3,
    Coordinates = 1 << 4,
    Biome = 1 << 5,
    GameTime = 1 << 6,
    SessionTime = 1 << 7,

    /// <summary>默认显示：FPS + 内存 + 延迟 + 坐标。</summary>
    Default = Fps | Memory | Ping | Coordinates,

    All = Fps | Memory | Cpu | Ping | Coordinates | Biome | GameTime | SessionTime
}

/// <summary>
/// HUD 悬浮窗配置（全局功能）。持久化在 LauncherProfile 中，
/// 界面层据此创建置顶的透明窗口。
/// </summary>
public class HudConfig
{
    /// <summary>总开关（默认关闭，关闭时不采集任何指标）。</summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    [JsonPropertyName("anchor")]
    public HudAnchor Anchor { get; set; } = HudAnchor.TopLeft;

    /// <summary>自定义位置（Anchor=Custom 时生效）。</summary>
    [JsonPropertyName("x")] public int X { get; set; }
    [JsonPropertyName("y")] public int Y { get; set; }

    /// <summary>距离屏幕边缘的留白（px）。</summary>
    [JsonPropertyName("margin")] public int Margin { get; set; } = 12;

    /// <summary>不透明度 0.1—1.0。</summary>
    [JsonPropertyName("opacity")]
    public double Opacity
    {
        get => _opacity;
        set => _opacity = Math.Clamp(value, 0.1, 1.0);
    }
    private double _opacity = 0.75;

    /// <summary>字号（px）。</summary>
    [JsonPropertyName("fontSize")]
    public int FontSize
    {
        get => _fontSize;
        set => _fontSize = Math.Clamp(value, 8, 32);
    }
    private int _fontSize = 12;

    /// <summary>要显示的字段。</summary>
    [JsonPropertyName("fields")]
    public HudField Fields { get; set; } = HudField.Default;

    /// <summary>刷新间隔（毫秒，200—5000）。</summary>
    [JsonPropertyName("refreshMs")]
    public int RefreshMs
    {
        get => _refreshMs;
        set => _refreshMs = Math.Clamp(value, 200, 5000);
    }
    private int _refreshMs = 1000;

    /// <summary>只在游戏进程处于前台时显示（默认关闭：HUD 常显游戏指标；开启后游戏非前台显示占位提示）。</summary>
    [JsonPropertyName("onlyWhenGameForeground")]
    public bool OnlyWhenGameForeground { get; set; }

    /// <summary>鼠标穿透（不拦截点击）。</summary>
    [JsonPropertyName("clickThrough")]
    public bool ClickThrough { get; set; } = true;

    public bool Has(HudField field) => (Fields & field) == field && field != HudField.None;

    /// <summary>切换某个字段的显示状态，返回切换后的状态。</summary>
    public bool Toggle(HudField field)
    {
        if (field == HudField.None) return false;
        if (Has(field)) { Fields &= ~field; return false; }
        Fields |= field;
        return true;
    }

    /// <summary>字段中文名。</summary>
    public static string FieldName(HudField field) => field switch
    {
        HudField.Fps => "帧率",
        HudField.Memory => "内存",
        HudField.Cpu => "CPU",
        HudField.Ping => "延迟",
        HudField.Coordinates => "坐标",
        HudField.Biome => "生物群系",
        HudField.GameTime => "游戏内时间",
        HudField.SessionTime => "本次时长",
        _ => "未知"
    };

    /// <summary>可供界面勾选的字段列表（按显示顺序）。</summary>
    public static IReadOnlyList<HudField> SelectableFields { get; } = new[]
    {
        HudField.Fps, HudField.Memory, HudField.Cpu, HudField.Ping,
        HudField.Coordinates, HudField.Biome, HudField.GameTime, HudField.SessionTime
    };

    /// <summary>根据锚点计算窗口左上角坐标。</summary>
    public (int X, int Y) ComputePosition(int screenWidth, int screenHeight, int hudWidth, int hudHeight) =>
        Anchor switch
        {
            HudAnchor.TopLeft => (Margin, Margin),
            HudAnchor.TopRight => (Math.Max(0, screenWidth - hudWidth - Margin), Margin),
            HudAnchor.BottomLeft => (Margin, Math.Max(0, screenHeight - hudHeight - Margin)),
            HudAnchor.BottomRight => (Math.Max(0, screenWidth - hudWidth - Margin),
                                      Math.Max(0, screenHeight - hudHeight - Margin)),
            _ => (Math.Clamp(X, 0, Math.Max(0, screenWidth - hudWidth)),
                  Math.Clamp(Y, 0, Math.Max(0, screenHeight - hudHeight)))
        };
}
