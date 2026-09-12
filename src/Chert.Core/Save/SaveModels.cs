namespace Chert.Core.Save;

/// <summary>§二.4 存档兼容性：用户面对"存档版本高于游戏版本"时的可选处置。</summary>
public enum SaveCompatAction
{
    /// <summary>安装存档所对应的更高游戏版本后再启动。</summary>
    InstallVersion,

    /// <summary>将存档降级到当前游戏版本可读取的范围（见 <see cref="SaveDowngrader"/>）。</summary>
    Downgrade,

    /// <summary>忽略警告，强行启动（Minecraft 可能拒绝打开或就地升级存档）。</summary>
    Ignore
}

/// <summary>兼容性严重程度。</summary>
public enum SaveCompatibilitySeverity
{
    /// <summary>兼容：存档版本 ≤ 游戏版本。</summary>
    Ok,

    /// <summary>存档略新：高出一个小版本，可直接降级且风险较低。</summary>
    SlightlyNewer,

    /// <summary>存档明显更新：跨多个大版本，降级有数据丢失风险，需谨慎。</summary>
    MuchNewer,

    /// <summary>无法判定：游戏版本未在对照表中，无法比较。</summary>
    Unknown
}

/// <summary>§二.4 单个存档的兼容性检测结果。</summary>
public class SaveCompatibilityReport
{
    /// <summary>存档目录名（即世界名）。</summary>
    public string SaveName { get; set; } = "";

    /// <summary>存档根目录（…/saves/&lt;SaveName&gt;）。</summary>
    public string SavePath { get; set; } = "";

    /// <summary>level.dat 中的 DataVersion。</summary>
    public int SaveDataVersion { get; set; }

    /// <summary>由 DataVersion 反查到的游戏版本（未知为 null）。</summary>
    public string? SaveGameVersion { get; set; }

    /// <summary>待启动的目标游戏版本 id。</summary>
    public string GameVersionId { get; set; } = "";

    /// <summary>目标游戏的 DataVersion（未知为 null）。</summary>
    public int? GameDataVersion { get; set; }

    /// <summary>是否兼容（存档版本 ≤ 游戏版本）。</summary>
    public bool Compatible { get; set; }

    /// <summary>严重程度。</summary>
    public SaveCompatibilitySeverity Severity { get; set; } = SaveCompatibilitySeverity.Ok;

    /// <summary>面向用户的说明。</summary>
    public string Message { get; set; } = "";

    /// <summary>推荐处置。</summary>
    public SaveCompatAction? RecommendedAction { get; set; }

    /// <summary>该存档是否存在可回滚的降级备份。</summary>
    public bool HasBackup { get; set; }
}

/// <summary>§三 降级方法。</summary>
public enum DowngradeMethod
{
    /// <summary>方案 A：直接改写 level.dat 的 DataVersion（快速、纯文件操作，但需游戏在加载时完成实际数据转换）。</summary>
    QuickModifyDataVersion,

    /// <summary>方案 B：调用 Amulet 工具做真正的数据层世界转换（更安全，但依赖外部程序）。</summary>
    Amulet
}

/// <summary>§三 一次存档降级操作的方案与结果（含强制备份与变更摘要）。</summary>
public class SaveDowngradePlan
{
    public string SaveName { get; set; } = "";
    public string SavePath { get; set; } = "";
    public DowngradeMethod Method { get; set; } = DowngradeMethod.QuickModifyDataVersion;
    public int FromDataVersion { get; set; }
    public int ToDataVersion { get; set; }

    /// <summary>降级前强制创建的备份目录（已存在则复用，不再覆盖原档）。</summary>
    public string? BackupPath { get; set; }

    /// <summary>变更摘要（人类可读，逐条）。</summary>
    public List<string> Summary { get; set; } = new();

    /// <summary>是否执行成功。</summary>
    public bool Success { get; set; }

    /// <summary>失败原因（Success=false 时填充）。</summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>一个存档备份的信息（用于回滚）。</summary>
public class SaveBackupInfo
{
    public string SaveName { get; set; } = "";
    public string BackupPath { get; set; } = "";
    public DateTime CreatedUtc { get; set; }
    public int? DataVersion { get; set; }
    public string? GameVersion { get; set; }
}

/// <summary>§四.2 降级联动：崩溃疑似由降级引起时的恢复方案。</summary>
public enum DowngradeRecoveryAction
{
    /// <summary>回滚到降级前的备份（最安全）。</summary>
    RevertToBackup,

    /// <summary>改用另一种降级方式重试（A↔B）。</summary>
    TryOtherMethod,

    /// <summary>安装存档原始版本（不再降级）。</summary>
    InstallOriginalVersion
}

/// <summary>§四.2 降级联动恢复方案。</summary>
public class DowngradeRecoveryPlan
{
    /// <summary>是否适用（崩溃疑似与降级相关，且存在备份）。</summary>
    public bool Applicable { get; set; }

    /// <summary>判定依据说明。</summary>
    public string Reason { get; set; } = "";

    public string SaveName { get; set; } = "";
    public string SavePath { get; set; } = "";

    /// <summary>当前存档的 DataVersion（降级后）。</summary>
    public int SaveDataVersion { get; set; }

    /// <summary>存档原始（降级前）游戏版本（由备份反查；未知为 null）。</summary>
    public string? OriginalGameVersion { get; set; }

    /// <summary>最近一次备份路径。</summary>
    public string? BackupPath { get; set; }

    /// <summary>可选恢复动作。</summary>
    public List<DowngradeRecoveryAction> Options { get; set; } = new();

    /// <summary>建议优先执行的动作。</summary>
    public DowngradeRecoveryAction? SuggestedAction { get; set; }
}
