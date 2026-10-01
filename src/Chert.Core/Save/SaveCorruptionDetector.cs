using System.Text.RegularExpressions;

namespace Chert.Core.Save;

/// <summary>存档损坏严重程度。</summary>
public enum SaveCorruptionSeverity
{
    /// <summary>未发现明显损坏。</summary>
    Ok,

    /// <summary>可疑 / 次级问题（如存在 level.dat_old 备份、区域文件大小异常），不一定无法加载。</summary>
    Warning,

    /// <summary>已损坏，很可能导致无法加载或地形异常（如缺少/损坏 level.dat、0 字节区域文件）。</summary>
    Corrupt
}

/// <summary>单个存档的损坏检测结果（只读，不修复）。</summary>
public class SaveCorruptionReport
{
    public string SaveName { get; set; } = "";
    public string SavePath { get; set; } = "";

    /// <summary>是否存在致命损坏（无法加载）。</summary>
    public bool IsCorrupt { get; set; }

    public SaveCorruptionSeverity Severity { get; set; } = SaveCorruptionSeverity.Ok;

    /// <summary>明确的问题清单（致命或损坏级）。</summary>
    public List<string> Issues { get; } = new();

    /// <summary>提示性说明（非致命，如存在自动备份可恢复）。</summary>
    public List<string> Notes { get; } = new();

    public string Summary
    {
        get
        {
            var detail = Issues.Count > 0
                ? string.Join("；", Issues)
                : string.Join("；", Notes);
            return Severity switch
            {
                SaveCorruptionSeverity.Corrupt => $"⚠ 已损坏，可能无法加载：{detail}",
                SaveCorruptionSeverity.Warning => $"⚠ 可疑，建议先备份再进入：{detail}",
                _ => "✓ 未检测到损坏。"
            };
        }
    }
}

/// <summary>
/// 游戏存档损坏检测（§三 存档修复的"只检测不修复"部分）。
/// <para>
/// 扫描每个世界的 <c>level.dat</c>（缺失 / NBT 不可解析）与区域文件 <c>*.mca</c>
/// （大小 &lt; 8192 或非 4096 对齐，通常是被截断或写入中断导致）。
/// <para>
/// <b>0 字节的 .mca 不算损坏</b>：poi / entities 等区域文件由游戏按需惰性创建，
/// 尚无数据时就是 0 字节，属正常现象（判为损坏会造成「所有存档都报损坏」的误报）。
/// </para>
/// 全程只读，不修改任何文件。检测到问题后交由 UI 展示，由用户自行决定用备份恢复或第三方工具修复。
/// </para>
/// </summary>
public static class SaveCorruptionDetector
{
    public static string SavesDir(string gameRoot) => Path.Combine(gameRoot, "saves");

    /// <summary>扫描全部存档的损坏情况（跳过备份目录）。</summary>
    public static List<SaveCorruptionReport> Scan(string gameRoot)
    {
        var result = new List<SaveCorruptionReport>();

        // 待扫描根：共享 saves/ + 各版本隔离 versions/<id>/saves/
        // 仅扫共享目录会漏掉版本隔离存档，且共享/隔离同名世界会被 ScanCorruptionAsync 按 SaveName 误匹配。
        var scanRoots = new List<string> { SavesDir(gameRoot) };
        var versionsDir = Path.Combine(gameRoot, "versions");
        if (Directory.Exists(versionsDir))
        {
            foreach (var v in Directory.GetDirectories(versionsDir))
            {
                var iso = Path.Combine(v, "saves");
                if (Directory.Exists(iso)) scanRoots.Add(iso);
            }
        }

        foreach (var savesDir in scanRoots)
        {
            if (!Directory.Exists(savesDir)) continue;
            foreach (var dir in Directory.GetDirectories(savesDir))
            {
                var name = Path.GetFileName(dir);
                if (name.EndsWith(".backup", StringComparison.OrdinalIgnoreCase)
                    || Regex.IsMatch(name, @"\.backup-\d{14}$")) continue;

                var report = ScanSingle(dir);
                if (report is not null) result.Add(report);
            }
        }
        return result;
    }

    /// <summary>检测单个存档；无法判定时返回 null。</summary>
    public static SaveCorruptionReport? ScanSingle(string savePath)
    {
        var name = Path.GetFileName(savePath);
        var report = new SaveCorruptionReport { SaveName = name, SavePath = savePath };

        // 1) level.dat 缺失
        var levelDat = Path.Combine(savePath, "level.dat");
        if (!File.Exists(levelDat))
        {
            // 仅缺失 level.dat 但存在 level.dat_old 备份时，视为可恢复（Warning）而非致命损坏，避免误报
            if (File.Exists(Path.Combine(savePath, "level.dat_old")))
            {
                if (report.Severity < SaveCorruptionSeverity.Warning)
                    report.Severity = SaveCorruptionSeverity.Warning;
                report.Notes.Add("缺少 level.dat，但存在 level.dat_old 备份，可用其恢复。");
            }
            else
            {
                report.IsCorrupt = true;
                report.Severity = SaveCorruptionSeverity.Corrupt;
                report.Issues.Add("缺少 level.dat，世界无法加载。");
            }
            return report;
        }

        // 2) level.dat 是否可解析（NBT gzip）
        try
        {
            var root = NbtFile.ReadGzip(levelDat);
            if (root is null || root.GetDataVersion() == 0 && root.Find("Data") is null)
            {
                report.IsCorrupt = true;
                report.Severity = SaveCorruptionSeverity.Corrupt;
                report.Issues.Add("level.dat 结构异常，可能无法被游戏识别。");
            }
        }
        catch (Exception ex)
        {
            report.IsCorrupt = true;
            report.Severity = SaveCorruptionSeverity.Corrupt;
            report.Issues.Add($"level.dat 已损坏，无法解析：{ex.Message}");
        }

        // 3) 区域文件 *.mca 损坏检测（递归，覆盖主世界/下界/末地及自定义维度）
        //
        // ⚠️ 这里曾经把「0 字节 .mca」判为损坏，属于**误报**，已移除：
        //    poi / entities 这类区域文件是 Minecraft **按需惰性创建**的——某区域尚无 POI / 实体
        //    数据时，文件被建出来就是 0 字节，游戏自己按「无数据」处理，完全不影响加载。
        //    实测一个正常世界 38 个 .mca 中 10 个为 0 字节且全在 poi/ 下，旧逻辑会把每个存档
        //    都标成「已损坏」（用户反馈的「所有存档都报损坏」即由此而来）。
        //
        // 真正能说明写入中断 / 被截断的判据是**尺寸异常**：非空区域文件至少 8192 字节
        // （4KB 区块位置表 + 4KB 时间戳表），且必须是 4096 的整数倍（按扇区分配）。
        try
        {
            foreach (var mca in Directory.EnumerateFiles(savePath, "*.mca", SearchOption.AllDirectories))
            {
                var fi = new FileInfo(mca);
                // 展示用相对路径统一成正斜杠，避免 Windows 反斜杠把 poi\r.x.z.mca 显示得像转义序列
                var rel = Path.GetRelativePath(savePath, mca).Replace('\\', '/');

                if (fi.Length == 0)
                    continue;   // 合法的空区域文件，见上方说明

                if (fi.Length < 8192 || fi.Length % 4096 != 0)
                {
                    report.Severity = SaveCorruptionSeverity.Corrupt;
                    report.IsCorrupt = true;
                    report.Issues.Add(
                        $"区域文件 {rel} 大小异常（{fi.Length} 字节，应为 4096 的整数倍且不小于 8192），很可能写入中断被截断。");
                }
            }
        }
        catch (Exception ex)
        {
            report.Notes.Add($"区域文件扫描失败：{ex.Message}");
        }

        // 4) level.dat_old：上次崩溃的自动备份（提示可用备份恢复）
        if (File.Exists(Path.Combine(savePath, "level.dat_old")))
        {
            if (report.Severity < SaveCorruptionSeverity.Warning)
                report.Severity = SaveCorruptionSeverity.Warning;
            report.Notes.Add("检测到 level.dat_old（上次崩溃的自动备份），可尝试用其恢复。");
        }

        return report;
    }
}
