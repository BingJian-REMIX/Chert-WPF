namespace Chert.Core.Toolbox;

/// <summary>工具箱面板分组。</summary>
public enum ToolboxGroup
{
    /// <summary>诊断与排障。</summary>
    Diagnostics,
    /// <summary>资源与内容管理。</summary>
    Content,
    /// <summary>开发工具。</summary>
    Development,
    /// <summary>其他。</summary>
    Misc
}

/// <summary>一个工具箱面板的注册信息。</summary>
public sealed class ToolboxPanel
{
    public ToolboxPanel(string id, string title, string description, ToolboxGroup group,
        string icon, int order, bool since2 = false)
    {
        Id = id;
        Title = title;
        Description = description;
        Group = group;
        Icon = icon;
        Order = order;
        SinceV2 = since2;
    }

    public string Id { get; }
    public string Title { get; }
    public string Description { get; }
    public ToolboxGroup Group { get; }
    public string Icon { get; }
    public int Order { get; }

    /// <summary>是否为 v2.0 新增（界面上打 NEW 角标）。</summary>
    public bool SinceV2 { get; }
}

/// <summary>
/// 工具箱面板注册表（v2.0：15 个面板）。
/// 界面层遍历此表生成入口卡片，避免面板增减时改动多处。
/// </summary>
public static class ToolboxCatalog
{
    /// <summary>规格要求的面板总数。</summary>
    public const int RequiredPanelCount = 15;

    public static IReadOnlyList<ToolboxPanel> Panels { get; } = new List<ToolboxPanel>
    {
        // 诊断与排障
        new("log",        "日志查看器",   "实时查看与过滤游戏 / 启动器日志",       ToolboxGroup.Diagnostics, "file-text",  0),
        new("crash",      "崩溃分析",     "解析崩溃报告并给出修复建议",             ToolboxGroup.Diagnostics, "bug",        1),
        new("network",    "网络诊断",     "测试各下载源连通性与延迟",               ToolboxGroup.Diagnostics, "activity",   2),
        new("perf",       "性能监控",     "监控 CPU / 内存 / FPS，可投送到 HUD",    ToolboxGroup.Diagnostics, "gauge",      3),

        // 资源与内容
        new("saves",      "存档管理",     "存档兼容性检测、降级与备份回滚",         ToolboxGroup.Content,     "database",   5),
        new("backup",     "备份管理器",   "定时备份存档 / 配置，一键恢复",          ToolboxGroup.Content,     "archive",    6, since2: true),
        new("screenshot", "截图管理",     "浏览、重命名与导出游戏截图",             ToolboxGroup.Content,     "image",      7),
        new("clean",      "冗余清理",     "清理重复库文件与失效缓存",               ToolboxGroup.Content,     "trash",      9),
        new("export",     "整合包导出",   "把当前实例打包为可分享的整合包",         ToolboxGroup.Content,     "package",   10),
        new("music",      "音乐播放器",   "挂机时播放本地音乐，支持四种播放模式",   ToolboxGroup.Content,     "music",     11, since2: true),

        // 开发工具
        new("nbt",        "NBT 编辑器",   "以树形结构查看与编辑 NBT 文件",          ToolboxGroup.Development, "braces",    12, since2: true),
        new("datapack",   "数据包冲突",   "检测数据包之间的同名资源覆盖",           ToolboxGroup.Development, "layers",    13, since2: true),
        new("skin",       "皮肤编辑器",   "编辑 64×64 皮肤，经典 / 纤细互转",       ToolboxGroup.Development, "user",      14, since2: true),

        // 其他
        new("aichat",     "AI 聊天",      "与本地 / 外部模型多轮对话",              ToolboxGroup.Misc,        "sparkles",  15, since2: true)
    };

    public static ToolboxPanel? ById(string? id) =>
        string.IsNullOrWhiteSpace(id) ? null : Panels.FirstOrDefault(p => p.Id == id);

    public static IEnumerable<ToolboxPanel> ByGroup(ToolboxGroup group) =>
        Panels.Where(p => p.Group == group).OrderBy(p => p.Order);

    /// <summary>v2.0 新增的面板。</summary>
    public static IEnumerable<ToolboxPanel> NewInV2 => Panels.Where(p => p.SinceV2);

    /// <summary>按分组归并（保持分组枚举顺序与组内 Order）。</summary>
    public static IReadOnlyList<(ToolboxGroup Group, IReadOnlyList<ToolboxPanel> Items)> Grouped() =>
        Enum.GetValues<ToolboxGroup>()
            .Select(g => (g, (IReadOnlyList<ToolboxPanel>)ByGroup(g).ToList()))
            .Where(t => t.Item2.Count > 0)
            .ToList();

    /// <summary>分组显示名。</summary>
    public static string GroupTitle(ToolboxGroup group) => group switch
    {
        ToolboxGroup.Diagnostics => "诊断与排障",
        ToolboxGroup.Content => "资源与内容",
        ToolboxGroup.Development => "开发工具",
        ToolboxGroup.Misc => "其他",
        _ => "其他"
    };
}
