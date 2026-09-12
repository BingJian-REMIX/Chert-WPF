using System.Text.RegularExpressions;
using Chert.Core.Launcher;

namespace Chert.Core.Save;

/// <summary>
/// §四.2 降级联动。
/// <para>
/// 当一次崩溃疑似由"存档降级"引起时（最近对该存档执行过降级且存在备份，崩溃又呈现世界/区块相关数据错误），
/// 给出三个恢复选项：
/// <list type="number">
///   <item><description>回滚到降级前的备份（最安全）；</description></item>
///   <item><description>改用另一种降级方式重试（A↔B）；</description></item>
///   <item><description>安装存档原始版本（不再降级，直接用原版本打开）。</description></item>
/// </list>
/// 仅在确实存在降级备份且崩溃与世界数据相关时才适用，避免对内存/Java/显卡类崩溃误报。
/// </para>
/// </summary>
public static class DowngradeCrashLinkage
{
    private static readonly string[] WorldKeywords =
    {
        "chunk", "region", "level.dat", "nbt", "world", "anvil",
        "tileentity", "blockentity", "corrupt", "the save", "saves"
    };

    private static readonly Regex BackupDirRegex =
        new(@"\.backup-\d{14}$", RegexOptions.Compiled);

    /// <summary>
    /// 为一次崩溃构建降级联动恢复方案。
    /// </summary>
    /// <param name="analysis">崩溃分析。</param>
    /// <param name="savePath">可疑存档路径；为空时自动在 gameRoot 下推断最近被降级过的存档。</param>
    /// <param name="gameRoot">游戏根目录（用于自动推断存档）。</param>
    public static DowngradeRecoveryPlan BuildPlan(CrashAnalysis analysis,
        string? savePath, string? gameRoot)
    {
        var none = new DowngradeRecoveryPlan { Applicable = false };

        if (string.IsNullOrEmpty(savePath) && !string.IsNullOrEmpty(gameRoot))
            savePath = FindMostLikelyDowngradedSave(gameRoot);

        if (string.IsNullOrEmpty(savePath) || !Directory.Exists(savePath))
        {
            none.Reason = "未找到可疑存档，无法判定降级关联。";
            return none;
        }

        var savesDir = Path.GetDirectoryName(savePath) ?? "";
        var saveName = Path.GetFileName(savePath);
        var backups = SaveCompatibilityDetector.FindBackups(savesDir, saveName);

        if (backups.Count == 0)
        {
            none.Reason = "该存档无降级备份，崩溃与降级无关。";
            return none;
        }

        // 降级引起的崩溃通常表现为世界/区块数据错误；对内存/Java/显卡类崩溃不误报
        var worldRelated = analysis.Category == CrashCategory.Unknown || IsWorldRelated(analysis);
        if (!worldRelated)
        {
            none.Reason = $"崩溃类别（{analysis.Category}）与世界数据无关，不判定为降级引起。";
            return none;
        }

        var latest = backups[^1];
        var plan = new DowngradeRecoveryPlan
        {
            Applicable = true,
            SaveName = saveName,
            SavePath = savePath,
            SaveDataVersion = SaveDowngrader.GetSaveDataVersion(savePath),
            OriginalGameVersion = latest.GameVersion,
            BackupPath = latest.BackupPath,
            Reason = $"该存档最近执行过降级（存在 {backups.Count} 个备份），且本次崩溃呈现世界/区块相关数据错误，"
                     + "很可能由降级不完全或数据不兼容引起。"
        };

        plan.Options.Add(DowngradeRecoveryAction.RevertToBackup);
        plan.Options.Add(DowngradeRecoveryAction.TryOtherMethod);
        if (latest.GameVersion is not null)
            plan.Options.Add(DowngradeRecoveryAction.InstallOriginalVersion);

        plan.SuggestedAction = DowngradeRecoveryAction.RevertToBackup;
        return plan;
    }

    /// <summary>在 gameRoot/saves 下查找"最近被修改且带有降级备份"的存档（最可能是崩溃来源）。</summary>
    public static string? FindMostLikelyDowngradedSave(string gameRoot)
    {
        var savesDir = SaveCompatibilityDetector.SavesDir(gameRoot);
        if (!Directory.Exists(savesDir)) return null;

        string? best = null;
        DateTime bestTime = DateTime.MinValue;
        foreach (var dir in Directory.GetDirectories(savesDir))
        {
            var name = Path.GetFileName(dir);
            if (BackupDirRegex.IsMatch(name)) continue;

            var backups = SaveCompatibilityDetector.FindBackups(savesDir, name);
            if (backups.Count == 0) continue;

            var lastWrite = Directory.GetLastWriteTimeUtc(dir);
            if (lastWrite > bestTime)
            {
                bestTime = lastWrite;
                best = dir;
            }
        }
        return best;
    }

    private static bool IsWorldRelated(CrashAnalysis analysis)
    {
        var hay = $"{analysis.ExceptionType}\n{analysis.Summary}\n{analysis.RawReport}".ToLowerInvariant();
        return WorldKeywords.Any(k => hay.Contains(k));
    }
}
