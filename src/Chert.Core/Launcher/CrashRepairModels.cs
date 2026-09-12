using Chert.Core.Mvvm;

namespace Chert.Core.Launcher;

/// <summary>可被自动执行的修复策略类型。</summary>
public enum RepairStrategy
{
    /// <summary>无需修复 / 不可自动修复。</summary>
    None,

    /// <summary>调大分配内存（-Xmx），仅修改启动器配置，不触碰游戏文件。</summary>
    IncreaseMemory,

    /// <summary>切换到满足版本要求的 Java（或下载安装），不修改游戏文件。</summary>
    SwitchJava,

    /// <summary>重新下载缺失或损坏的依赖库，仅重写 libraries 缓存，不删除游戏原文件。</summary>
    RedownloadLibraries,

    /// <summary>禁用相互冲突的 Mod（将不需要的 .jar 重命名为 .disabled，可还原）。</summary>
    DisableConflictingMods,

    /// <summary>自动安装缺失的 Mod 前置依赖（从 Modrinth 下载到 mods 目录）。</summary>
    InstallMissingModDependency,

    /// <summary>§四.2 回滚到降级前的备份（用备份覆盖当前存档，原档另存不丢）。</summary>
    RevertDowngradeBackup,

    /// <summary>§四.2 改用另一种降级方式重试（A↔B）。</summary>
    RetryDowngradeOtherMethod,

    /// <summary>§四.2 安装存档原始版本（不再降级，用原版本打开）。</summary>
    InstallOriginalVersion,

    /// <summary>资源包 / 光影崩溃：将 options.txt 资源包重置为 vanilla、停用 shaderpacks、清空 cache（均非破坏性、可恢复）。</summary>
    ResetResourcePacks
}

/// <summary>一个相互冲突的 Mod 条目（用于"保留哪一个"的选择）。</summary>
public class ModConflictInfo : ObservableObject
{
    /// <summary>Mod 文件名（含路径）。</summary>
    public string FilePath { get; set; } = "";

    /// <summary>展示名（优先用 Mod 显示名，否则文件名）。</summary>
    public string Name { get; set; } = "";

    /// <summary>UI 单选：是否被保留（其余冲突 Mod 将被重命名为 .disabled）。</summary>
    public bool IsKeepSelected
    {
        get => _isKeepSelected;
        set => SetField(ref _isKeepSelected, value);
    }
    private bool _isKeepSelected;
}

/// <summary>
/// 一次崩溃对应的自动修复方案。
/// 所有方案均保证不删除、不修改游戏原文件（存档、配置、mod、版本 jar 等），
/// 仅调整启动器配置、外部 Java 或依赖库缓存。
/// </summary>
public class CrashRepairPlan
{
    /// <summary>是否可自动修复。false 时仅展示崩溃详情与手动建议。</summary>
    public bool CanRepair { get; set; }

    /// <summary>修复策略类型。</summary>
    public RepairStrategy Strategy { get; set; } = RepairStrategy.None;

    /// <summary>面向用户的标题（如"调大内存"）。</summary>
    public string Title { get; set; } = "";

    /// <summary>面向用户的说明（描述将做什么、为何安全）。</summary>
    public string Description { get; set; } = "";

    /// <summary>具体操作步骤（人类可读）。</summary>
    public List<string> Steps { get; set; } = new();

    /// <summary>
    /// 该修复是否对游戏原文件无副作用（始终为 true；仅用于界面明示安全性）。
    /// </summary>
    public bool NonDestructive { get; set; } = true;

    /// <summary>调大内存方案的目标内存（MB）。</summary>
    public int? TargetMemoryMb { get; set; }

    /// <summary>切换 Java 方案所需的最低 Java 主版本号。</summary>
    public int? RequiredJavaMajor { get; set; }

    /// <summary>重新下载库方案对应的版本 id。</summary>
    public string? VersionId { get; set; }

    /// <summary>禁用冲突 Mod 方案：相互冲突的 Mod 文件列表。</summary>
    public List<ModConflictInfo> ConflictingMods { get; set; } = new();

    /// <summary>禁用冲突 Mod 方案：用户选择保留的 Mod 文件路径（其余将被 .disabled）。为空时默认保留第一个。</summary>
    public string? KeepModFile { get; set; }

    /// <summary>安装缺失前置方案：缺失的前置 Mod ID 列表。</summary>
    public List<string> MissingModDependencies { get; set; } = new();

    /// <summary>§四.2 降级联动：涉及的存档路径。</summary>
    public string? SavePath { get; set; }

    /// <summary>§四.2 降级联动：回滚所用的备份路径。</summary>
    public string? BackupPath { get; set; }

    /// <summary>§四.2 降级联动：完整恢复方案（含三选项与建议动作），供 UI 渲染。</summary>
    public Chert.Core.Save.DowngradeRecoveryPlan? DowngradeRecovery { get; set; }
}
