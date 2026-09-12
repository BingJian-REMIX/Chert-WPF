using Chert.Core.Models;
using Chert.Core.Mods;
using Chert.Core.Profiles;
using Chert.Core.Save;
using Chert.Core.Utils;

namespace Chert.Core.Launcher;

/// <summary>
/// 崩溃修复规划引擎：根据崩溃分析、启动器配置与当前 Java，决定是否可以自动修复，
/// 并产出对应的 <see cref="CrashRepairPlan"/>。
///
/// 设计原则：
/// 1. 纯函数、离线可测——不发起网络请求，也不读取磁盘状态（除本地 Mod 元数据扫描）；
///    真实的"能否成功"由 <see cref="LauncherService.ApplyRepairAsync"/> 在执行时判定。
/// 2. 所有方案保证非破坏性：仅修改启动器配置、外部 Java 或依赖库缓存，
///    或把冲突 Mod 重命名为 .disabled（可还原），绝不删除/改写游戏原文件
///    （存档、配置、mod、版本 jar 等）。
/// </summary>
public static class CrashRepairEngine
{
    /// <summary>
    /// 为一次崩溃构建修复方案。
    /// </summary>
    /// <param name="analysis">崩溃分析结果。</param>
    /// <param name="profile">当前启动器配置（用于判断内存、Java 路径、缺失前置安装策略等）。</param>
    /// <param name="currentJava">实际用于本次启动的 Java 信息（可能为空）。</param>
    /// <param name="gameRoot">游戏根目录。</param>
    /// <param name="versionId">崩溃的版本 id（用于重下库 / 安装前置）。</param>
    /// <param name="savePath">可疑存档路径（§四.2 降级联动用；为空时自动推断）。</param>
    public static CrashRepairPlan BuildPlan(CrashAnalysis analysis,
        LauncherProfile profile, JavaInfo? currentJava, string gameRoot, string? versionId,
        string? savePath = null)
    {
        var plan = new CrashRepairPlan { NonDestructive = true };

        switch (analysis.Category)
        {
            case CrashCategory.OutOfMemory:
                BuildMemoryPlan(plan, profile);
                return plan;

            case CrashCategory.JavaVersion:
                BuildJavaPlan(plan, analysis, currentJava);
                return plan;

            case CrashCategory.ResourcePackOrShader:
                // 资源包 / 光影崩溃：安全回滚到默认（非破坏性、可恢复），纳入自动修复
                BuildResourcePackPlan(plan, gameRoot);
                return plan;

            // 其余类别：先检查 Mod 冲突 / 缺失前置（用户显式要求的崩溃修复增强），
            // 再尝试 §四.2 降级联动，最后按原类别兜底。
            default:
                if (TryBuildModRepair(plan, profile, gameRoot, versionId))
                    return plan;

                if (analysis.Category == CrashCategory.MissingLibrary)
                {
                    BuildLibraryPlan(plan, versionId);
                    return plan;
                }

                if (TryBuildDowngradeRecovery(plan, analysis, gameRoot, savePath))
                    return plan;

                BuildUnrepairablePlan(plan, analysis);
                return plan;
        }
    }

    /// <summary>
    /// 扫描 Mod 冲突与缺失前置；命中则返回 true 并已填充 plan。
    /// 冲突优先于缺失前置（冲突需用户选择保留哪一个）。
    /// </summary>
    private static bool TryBuildModRepair(CrashRepairPlan plan, LauncherProfile profile,
        string gameRoot, string? versionId)
    {
        List<DependencyCheckResult> results;
        try
        {
            results = ModManager.ScanDependencies(gameRoot);
        }
        catch
        {
            return false;
        }

        // 1) Mod 冲突 -> 禁用（保留一个，其余 .disabled）
        var conflictResults = results.Where(r => r.Conflicts.Count > 0).ToList();
        if (conflictResults.Count > 0)
        {
            BuildConflictPlan(plan, conflictResults, gameRoot);
            return true;
        }

        // 2) 缺失前置 -> 自动安装（受 AutoInstallMissingMods 策略控制）
        if (profile.AutoInstallMissingMods != AutoInstallPolicy.Never)
        {
            var missing = results
                .SelectMany(r => r.Missing)
                .Where(m => m.Required)
                .Select(m => m.DependencyId)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (missing.Count > 0)
            {
                BuildMissingDepPlan(plan, missing);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// §四.2 降级联动：若崩溃疑似由存档降级引起（存在降级备份且崩溃与世界数据相关），
    /// 填充对应的恢复方案并返回 true。所有方案非破坏性（回滚仅复制，不删原档）。
    /// </summary>
    private static bool TryBuildDowngradeRecovery(CrashRepairPlan plan, CrashAnalysis analysis,
        string gameRoot, string? savePath)
    {
        if (analysis.Category != CrashCategory.Unknown) return false;

        var recovery = DowngradeCrashLinkage.BuildPlan(analysis, savePath, gameRoot);
        if (!recovery.Applicable || recovery.SuggestedAction is null) return false;

        plan.CanRepair = true;
        plan.NonDestructive = true;
        plan.DowngradeRecovery = recovery;
        plan.SavePath = recovery.SavePath;
        plan.BackupPath = recovery.BackupPath;
        plan.Strategy = recovery.SuggestedAction.Value switch
        {
            DowngradeRecoveryAction.RevertToBackup => RepairStrategy.RevertDowngradeBackup,
            DowngradeRecoveryAction.TryOtherMethod => RepairStrategy.RetryDowngradeOtherMethod,
            DowngradeRecoveryAction.InstallOriginalVersion => RepairStrategy.InstallOriginalVersion,
            _ => RepairStrategy.RevertDowngradeBackup
        };
        plan.Title = "崩溃疑似由存档降级引起";
        plan.Description = recovery.Reason + " 可回滚备份 / 改用其他降级方式 / 安装存档原版本。";
        plan.Steps = new List<string>
        {
            recovery.SuggestedAction == DowngradeRecoveryAction.RevertToBackup
                ? $"回滚到降级前备份：{recovery.BackupPath}"
                : "按所选方式处理（回滚备份最安全，原档另存不丢失）"
        };
        if (recovery.OriginalGameVersion is not null)
            plan.VersionId = recovery.OriginalGameVersion;
        return true;
    }

    private static void BuildConflictPlan(CrashRepairPlan plan,
        List<DependencyCheckResult> conflictResults, string gameRoot)
    {
        // 建立 modId -> 文件名 映射，便于把冲突 ID 解析成文件路径
        Dictionary<string, string>? idToFile = null;
        var involved = new List<string>();
        foreach (var r in conflictResults)
        {
            involved.Add(r.ModFileName);
            foreach (var c in r.Conflicts)
            {
                idToFile ??= BuildModIdMap(gameRoot);
                if (idToFile.TryGetValue(c.ConflictId, out var file))
                    involved.Add(file);
            }
        }

        var modsDir = PathEx.ModsDir(gameRoot);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in involved)
        {
            if (string.IsNullOrEmpty(file) || !seen.Add(file)) continue;
            if (file.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)) continue; // 已禁用不再参与
            plan.ConflictingMods.Add(new ModConflictInfo
            {
                FilePath = Path.Combine(modsDir, file),
                Name = Path.GetFileNameWithoutExtension(file)
            });
        }

        if (plan.ConflictingMods.Count == 0)
        {
            BuildUnrepairablePlan(plan, new CrashAnalysis { Category = CrashCategory.ModConflict });
            return;
        }

        plan.CanRepair = true;
        plan.Strategy = RepairStrategy.DisableConflictingMods;
        plan.KeepModFile = plan.ConflictingMods[0].FilePath; // 默认保留第一个，UI 可改
        plan.Title = "禁用冲突的 Mod";
        plan.Description =
            "检测到相互冲突的 Mod。请选择要保留的一个，其余将被重命名为 .disabled（可随时改回）。不删除任何文件。";
        plan.Steps.Add("列出所有相互冲突的 Mod 文件。");
        plan.Steps.Add("将你不需要的 Mod 重命名为 *.disabled（Fabric/Forge 会忽略此类文件）。");
        plan.Steps.Add("保留你选择的 Mod，重新启动游戏。");
    }

    private static void BuildMissingDepPlan(CrashRepairPlan plan, List<string> missing)
    {
        plan.CanRepair = true;
        plan.Strategy = RepairStrategy.InstallMissingModDependency;
        plan.MissingModDependencies = missing;
        plan.Title = "安装缺失的 Mod 前置";
        plan.Description =
            $"检测到 {missing.Count} 个缺失的强制前置依赖。将尝试从 Modrinth 自动下载并安装到 mods 目录。";
        plan.Steps.Add("根据缺失的前置 modId 在 Modrinth 中检索对应项目。");
        plan.Steps.Add("挑选与当前游戏版本/加载器匹配的版本文件并下载到 mods 目录。");
        plan.Steps.Add("重新启动游戏以加载完整依赖。");
    }

    private static Dictionary<string, string> BuildModIdMap(string gameRoot)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var mgr = new ModManager(gameRoot, new HttpClient(), null!);
            foreach (var m in mgr.ListInstalledMods())
                if (!string.IsNullOrEmpty(m.ModId) && !map.ContainsKey(m.ModId))
                    map[m.ModId] = m.FileName;
        }
        catch { /* 忽略 */ }
        return map;
    }

    private static void BuildMemoryPlan(CrashRepairPlan plan, LauncherProfile profile)
    {
        var current = Math.Max(profile.MaxMemoryMb, 512);
        if (current < GameConstants.MaxRepairMemoryMb)
        {
            var target = Math.Min(Math.Max(current * 2, 4096), GameConstants.MaxRepairMemoryMb);
            plan.CanRepair = true;
            plan.Strategy = RepairStrategy.IncreaseMemory;
            plan.TargetMemoryMb = target;
            plan.Title = "调大分配内存";
            plan.Description =
                $"将启动内存从 {current}MB 提升到 {target}MB。仅修改启动器内存设置，不影响任何游戏文件。";
            plan.Steps.Add($"修改配置中的最大内存（maxMemoryMb）：{current}MB → {target}MB。");
            plan.Steps.Add("重新启动游戏；若仍内存不足，可再次自动调大（上限 " +
                           $"{GameConstants.MaxRepairMemoryMb}MB）。");
        }
        else
        {
            plan.CanRepair = false;
            plan.Strategy = RepairStrategy.None;
            plan.Title = "内存已到达上限";
            plan.Description =
                $"当前内存已为 {current}MB（上限 {GameConstants.MaxRepairMemoryMb}MB），继续调大可能引发系统卡顿。";
            plan.Steps.Add("建议关闭占用内存过大的模组/光影。");
            plan.Steps.Add("或在系统允许范围内手动调高上限后重试。");
        }
    }

    private static void BuildJavaPlan(CrashRepairPlan plan, CrashAnalysis analysis, JavaInfo? currentJava)
    {
        var required = analysis.RequiredJavaMajor > 0 ? analysis.RequiredJavaMajor : GameConstants.MinimumJavaMajorVersion;
        plan.CanRepair = true;
        plan.Strategy = RepairStrategy.SwitchJava;
        plan.RequiredJavaMajor = required;
        plan.Title = "切换到兼容的 Java";
        plan.Description =
            $"游戏/模组要求 Java {required} 或以上，将自动选择或下载安装匹配的 Java。仅影响启动器使用的 Java，不修改游戏文件。";
        plan.Steps.Add($"检测系统中是否存在 Java {required}+。");
        if (currentJava is not null)
            plan.Steps.Add($"当前使用：{currentJava}（不满足需求）。");
        plan.Steps.Add($"若已存在兼容 Java，则切换到该 Java；否则尝试下载安装 Java {required}（Temurin 或 Oracle）。");
        plan.Steps.Add("使用新的 Java 重新启动游戏。");
    }

    private static void BuildLibraryPlan(CrashRepairPlan plan, string? versionId)
    {
        if (string.IsNullOrEmpty(versionId))
        {
            plan.CanRepair = false;
            plan.Title = "缺少依赖库";
            plan.Description = "未能确定崩溃对应的版本，无法自动重下依赖库。";
            plan.Steps.Add("请在版本列表中手动重装该版本。");
            return;
        }

        plan.CanRepair = true;
        plan.Strategy = RepairStrategy.RedownloadLibraries;
        plan.VersionId = versionId;
        plan.Title = "重新下载依赖库";
        plan.Description =
            $"针对版本 {versionId} 重新下载缺失或损坏的依赖库（libraries 缓存）。仅重写依赖缓存，不删除游戏原文件。";
        plan.Steps.Add($"合并版本 {versionId} 的依赖清单。");
        plan.Steps.Add("校验每个库文件：缺失或校验和不符的将重新下载（不删除其它文件）。");
        plan.Steps.Add("使用修复后的库重新启动游戏。");
    }

    private static void BuildResourcePackPlan(CrashRepairPlan plan, string gameRoot)
    {
        // 即便无法定位具体 shaderpacks 目录也照常给出方案（options.txt 一定存在时才会真正执行）。
        plan.CanRepair = true;
        plan.Strategy = RepairStrategy.ResetResourcePacks;
        plan.Title = "回滚资源包 / 光影到默认";
        plan.Description =
            "检测到资源包或光影引起的崩溃。将把 options.txt 中的资源包重置为 vanilla（原文件已备份），" +
            "并临时停用光影目录（shaderpacks 重命名为 .disabled，可改回）、清空缓存目录（cache 重命名为 .disabled，游戏会重建）。" +
            "全程不删除任何文件，可随时恢复。";
        plan.Steps.Add("备份 options.txt，将其 resourcePacks 设为 [\"vanilla\"]。");
        plan.Steps.Add("将 shaderpacks 目录重命名为 shaderpacks.disabled（停用光影，可改回）。");
        plan.Steps.Add("将 cache 目录重命名为 cache.disabled（清空缓存，游戏会重建）。");
        plan.Steps.Add("重新启动游戏以加载默认资源包 / 关闭光影。");
    }

    private static void BuildUnrepairablePlan(CrashRepairPlan plan, CrashAnalysis analysis)
    {
        plan.CanRepair = false;
        plan.Strategy = RepairStrategy.None;
        plan.Title = "无法自动修复";
        plan.Description = "该问题通常需要手动干预（如处理模组冲突、更新显卡驱动等），不在此自动修复范围内。";
        plan.Steps.AddRange(analysis.Suggestions);
    }
}
