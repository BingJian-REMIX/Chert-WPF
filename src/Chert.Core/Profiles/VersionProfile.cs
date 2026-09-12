using System.Text.Json.Serialization;

namespace Chert.Core.Profiles;

/// <summary>版本隔离模式：决定该版本运行时的「游戏工作目录」来源。</summary>
public enum IsolationMode
{
    /// <summary>共享：与默认 .minecraft 共用根目录（versions/&lt;id&gt; 仅存版本 JSON/jar）。</summary>
    Shared,

    /// <summary>自动隔离：工作目录切到 versions/&lt;id&gt;（创建 .mclcs-isolated 标记，mods/saves 独立）。</summary>
    Auto,

    /// <summary>自定义目录：手动指定任意路径，实现物理层面的彻底隔离。</summary>
    Custom
}

/// <summary>模组加载器种类（用于版本设置展示与安装入口）。</summary>
public enum ModLoaderKind
{
    None,
    Forge,
    Fabric,
    Quilt,
    NeoForge
}

/// <summary>
/// 每版本配置（实例级覆盖），持久化于 <c>versions/&lt;id&gt;/profile.json</c>。
/// 覆盖全局 <see cref="LauncherProfile"/> 中同名字段，实现「每个版本 = 独立沙盒」。
/// </summary>
public class VersionProfile
{
    /// <summary>① 基础信息：在版本列表中显示的名称（为空时回退到版本 Id）。</summary>
    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = "";

    /// <summary>④ 隔离模式。</summary>
    [JsonPropertyName("isolation")]
    public IsolationMode Isolation { get; set; } = IsolationMode.Auto;

    /// <summary>④ 自定义游戏目录（仅 Isolation == Custom 时生效，实现物理隔离）。</summary>
    [JsonPropertyName("customGameDir")]
    public string? CustomGameDir { get; set; }

    /// <summary>③ 模组加载器（元数据/展示；真实安装由 LauncherService 完成）。</summary>
    [JsonPropertyName("modLoader")]
    public ModLoaderKind ModLoader { get; set; } = ModLoaderKind.None;

    /// <summary>③ 加载器版本号（展示用）。</summary>
    [JsonPropertyName("modLoaderVersion")]
    public string? ModLoaderVersion { get; set; }

    /// <summary>⑤ Java 路径（覆盖全局）。</summary>
    [JsonPropertyName("javaPath")]
    public string? JavaPath { get; set; }

    /// <summary>⑤ 最大内存 -Xmx（MB，覆盖全局）。</summary>
    [JsonPropertyName("maxMemoryMb")]
    public int? MaxMemoryMb { get; set; }

    /// <summary>⑤ 最小内存 -Xms（MB，覆盖全局）。</summary>
    [JsonPropertyName("minMemoryMb")]
    public int? MinMemoryMb { get; set; }

    /// <summary>⑤ 额外 JVM 参数（追加到全局额外参数之后）。</summary>
    [JsonPropertyName("extraJvmArgs")]
    public List<string> ExtraJvmArgs { get; set; } = new();

    /// <summary>⑥ 分辨率宽（覆盖全局；0 表示跟随全局）。</summary>
    [JsonPropertyName("resolutionWidth")]
    public int? ResolutionWidth { get; set; }

    /// <summary>⑥ 分辨率高（覆盖全局；0 表示跟随全局）。</summary>
    [JsonPropertyName("resolutionHeight")]
    public int? ResolutionHeight { get; set; }

    /// <summary>⑥ 是否全屏。</summary>
    [JsonPropertyName("fullscreen")]
    public bool Fullscreen { get; set; }

    /// <summary>⑧ 版本锁定：阻止自动更新覆盖该版本。</summary>
    [JsonPropertyName("locked")]
    public bool Locked { get; set; }

    /// <summary>⑨ 绑定账号：启动该版本时优先使用的账号 Id（为空则回落全局「最后使用」）。</summary>
    [JsonPropertyName("boundAccountId")]
    public string? BoundAccountId { get; set; }

    /// <summary>最后更新时间。</summary>
    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; set; }
}
